using System;
using UnityEngine;

namespace FreeChem
{
    /// <summary>
    /// 标签打印机（P1b1 + P1c）：拿打印机点本模组药剂瓶即可「盖章」；第 15 个模式是「校准」，
    /// 是全游戏**唯一**把药剂做到 100% 分析纯的路径。
    ///
    /// 语义（依据 docs/printer-research.md）：
    ///  · **真实纯度 vs 标签档位分离**：real = 目标当前真实纯度对应的原版七档；
    ///    fake = 打印机当前选中的标签档位。调用 `ChemicalProductHelper.SetPurityFeature(target, real, fake)`，
    ///    原版成分仪会分别显示 tested（real）与 labeled（fake）两行。
    ///  · 盖章只写 feature/tag，**绝不调用 GradeProduct**（那会按液体组分比例重算并覆盖真实纯度）。
    ///  · 医疗品模式额外 `SetGameItemType(TypeHelper.MEDICAL)` + `EnableTag("fc_medical")`；
    ///    切回化学品模式**只加不撤**（保留已有医疗标签，日志说明）。
    ///  · 模式：14 个标签模式（7 档 × 化学品/医疗品）+ 第 15 个「校准」。
    ///    激活打印机循环 15 个模式；默认「街头标准 / 化学品」。
    ///  · 校准（P1c）：校准模式下先用打印机点 `catalyst_kit` 充能（tag fc_catalyst_charge，≤3，消耗套件）；
    ///    再点纯度 ≥99.5% 的本模组药剂 → 污染物清零、提纯等级=5、fc_purity=100、
    ///    原版 purity=100 + LabPure（real=fake），消耗 1 充能。校准后同样不调 GradeProduct。
    ///  · target 钩子用原版 chem_scanner/labeler 同款「this」三钩子（selected 打印机 → 点目标）。
    ///
    /// 未实机验证：选中/点击交互、模式循环手感、双标签在成分仪的显示、医疗客户接受度、校准充能与 100% 表现。
    /// </summary>
    public static class ChemPrinter
    {
        public const int GradeCount = 7;
        public const int CategoryCount = 2;
        /// <summary>14 个标签模式 = 7 档 × 2 类。</summary>
        public const int LabelModeCount = GradeCount * CategoryCount;
        /// <summary>第 15 个模式：校准（分析纯）。</summary>
        public const int CalibrationModeIndex = LabelModeCount;
        /// <summary>总模式数 15 = 14 标签 + 1 校准。</summary>
        public const int ModeCount = LabelModeCount + 1;

        /// <summary>校准充能上限。</summary>
        public const int MaxCatalystCharge = 3;
        /// <summary>校准要求的最低真实纯度（%）。</summary>
        public const double CalibrationMinPurity = 99.5;

        /// <summary>默认模式：街头标准(档位序 3) / 化学品(0) → 3*2+0。</summary>
        public const int DefaultModeIndex = 3 * 2;

        static int modeIndex = DefaultModeIndex;

        public static int ModeIndex => modeIndex;
        /// <summary>档位序号：0 淀粉级 … 6 实验室纯级（仅 14 标签模式有效）。</summary>
        public static int GradeIndex => Math.Min(GradeCount - 1, modeIndex / CategoryCount);
        /// <summary>true = 医疗品标签；false = 化学品标签（仅 14 标签模式有效）。</summary>
        public static bool MedicalMode => !IsCalibrationMode && (modeIndex % CategoryCount) == 1;
        /// <summary>当前是否处于「校准」模式。</summary>
        public static bool IsCalibrationMode => modeIndex == CalibrationModeIndex;

        /// <summary>7 档对应的本地化键（docs-i18n 已提供）。</summary>
        public static readonly string[] GradeKeys =
        {
            "grade_starch_grade", "grade_junk_grade", "grade_cut_product",
            "grade_street_standard", "grade_near_pharma", "grade_pharma", "grade_lab_pure",
        };

        // ── 绑定：由 ModEntry.BuildItem 在创建 label_printer 时调用 ──

        public static void Bind(GameItem printer)
        {
            if (printer == null) return;
            try
            {
                // 与 chem_scanner 同款：允许对非玩家所有的物品使用（顾客手里的药剂也能盖章）
                printer.EnableTag("CAN_USE_ON_NON_OWNED_TAG");

                // 选中限制（照 chem_scanner/labeler 的 maySelect/canSelect）
                printer.maySelectSlotItemFunc = (it, inv, slot) => GeneralHelper.IsItemOwned(it);
                printer.canSelectSlotItemFunc = (it, inv, slot) => GeneralHelper.IsItemOwned(it);

                // 激活点击 = 循环 15 模式（14 标签 + 校准）
                printer.mayActivateSlotItemFunc = (it, inv, slot) => GeneralHelper.IsItemOwned(it);
                printer.onActivateSlotItemFunc = (it, inv, slot) => CycleMode();

                // target 钩子：s=打印机（选中方），t=被点物品。
                // 普通模式只接受本模组药剂；校准模式还接受 catalyst_kit（充能）。
                printer.mayThisTargetItemFunc = (s, t) => IsValidTarget(t);
                printer.canThisTargetItemFunc = (s, t) =>
                    t != null && (IsPrintableTarget(t) || (IsCalibrationMode && IsCatalyst(t)));
                printer.onThisTargetItemFunc = (s, t) =>
                {
                    // 普通 14 标签模式：直接盖章；校准模式：充能/校准分发（见 HandleTarget）
                    if (!IsCalibrationMode && IsPrintableTarget(t)) { Stamp(t); return; }
                    HandleTarget(s, t);
                };

                ChemRuntime.Log("[printer] hooks bound (mode=" + ModeLine(printer) + ").");
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[printer] bind failed: " + e.Message);
            }
        }

        /// <summary>切换模式（15 步循环：14 标签 + 校准）。</summary>
        public static void CycleMode()
        {
            modeIndex = (modeIndex + 1) % ModeCount;
            ChemRuntime.Log("[printer] mode -> " + ModeLine());
        }

        /// <summary>tooltip 用：当前模式文案（不含充能，用于日志）。</summary>
        public static string ModeLine() => ModeLine(null);

        /// <summary>tooltip 用：当前模式文案；校准模式附带充能数。</summary>
        public static string ModeLine(GameItem printer)
        {
            if (IsCalibrationMode)
            {
                string text = L("printer_mode_calibrate");
                if (printer != null)
                    text += " · " + L("printer_charge", GetCharge(printer));
                return text;
            }
            return L("printer_mode",
                     L(GradeKeys[GradeIndex]),
                     L(MedicalMode ? "printer_cat_medical" : "printer_cat_chemical"));
        }

        /// <summary>目标是否可盖章：本模组真药剂（Kind 白名单）且带 ReagentTag。</summary>
        public static bool IsPrintableTarget(GameItem target)
        {
            if (target == null) return false;
            if (!ChemCatalog.IsOurReagent(target.identifier)) return false;
            return target.IsTag(ChemRecipes.ReagentTag);
        }

        /// <summary>是否催化剂套件（校准模式充能用）。</summary>
        public static bool IsCatalyst(GameItem item)
            => item != null && item.identifier == "catalyst_kit";

        /// <summary>当前模式下该目标是否可点。</summary>
        public static bool IsValidTarget(GameItem target)
        {
            if (target == null) return false;
            if (IsCalibrationMode && IsCatalyst(target)) return true;
            return IsPrintableTarget(target);
        }

        /// <summary>target 钩子分发：校准模式 → 充能/校准；其他模式 → 盖章。</summary>
        public static void HandleTarget(GameItem printer, GameItem target)
        {
            if (IsCalibrationMode)
            {
                if (IsCatalyst(target)) { Charge(printer, target); return; }
                if (IsPrintableTarget(target)) { Calibrate(printer, target); return; }
                ChemRuntime.Log("[printer] " + L("printer_stamp_target_invalid"));
                return;
            }
            if (IsPrintableTarget(target)) Stamp(target);
        }

        // ── P1c：催化剂充能 / 校准到 100% ──

        public static int GetCharge(GameItem printer)
            => printer == null ? 0
               : Mathf.Clamp(ChemCatalog.ReadInt(printer, ChemCatalog.TCatalystCharge, 0),
                             0, MaxCatalystCharge);

        /// <summary>用催化剂套件给打印机充能（+1，上限 3），并销毁该套件。</summary>
        public static bool Charge(GameItem printer, GameItem catalyst)
        {
            if (printer == null || !IsCatalyst(catalyst)) return false;
            try
            {
                int charge = GetCharge(printer);
                if (charge >= MaxCatalystCharge)
                {
                    ChemRuntime.Log("[printer] " + L("printer_charge_ok", charge) + " (already full)");
                    return false;
                }
                charge++;
                printer.ModifyTag(ChemCatalog.TCatalystCharge, s => s.Enable().SetInt(charge));
                catalyst.Destroy();
                ChemRuntime.Log("[printer] " + L("printer_charge_ok", charge));
                return true;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[printer] charge failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 校准到 100% 分析纯（全游戏唯一 100% 路径）：
        /// 要求目标实际纯度 ≥ 99.5% 且打印机有催化剂充能；成功后污染物清零、提纯等级=5、
        /// fc_purity=100、原版 purity=100 + LabPure(real=fake)、消耗 1 充能。
        /// 校准不调用 GradeProduct（避免按液体比例覆盖 100）。
        /// </summary>
        public static bool Calibrate(GameItem printer, GameItem target)
        {
            if (!IsPrintableTarget(target))
            {
                ChemRuntime.Log("[printer] " + L("printer_stamp_target_invalid"));
                return false;
            }
            // P7c：盖章有成本（消耗标签/耗材，按现金计），防止无成本刷 lab_pure
            int stampCost = StampCost;
            try
            {
                if (!PlayerStore.HasInstance)
                {
                    ChemRuntime.Log("[printer] stamp refused: store not loaded.");
                    return false;
                }
                if (PlayerStore.Instance.playerCash < stampCost)
                {
                    ChemRuntime.Log("[printer] stamp refused: not enough cash (" + stampCost + ").");
                    return false;
                }
                PlayerStore.Instance.playerCash -= stampCost;
                ChemRuntime.Log("[printer] stamp cost " + stampCost + " charged for " + target.identifier);
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[printer] stamp cost failed: " + e.Message);
                return false;
            }

            try
            {
                double actualPurity = ChemRuntime.PurityOf(target);
                if (actualPurity < CalibrationMinPurity)
                {
                    ChemRuntime.Log("[printer] " + L("printer_calibrate_fail_purity",
                                                     actualPurity.ToString("F1")));
                    return false;
                }
                int charge = GetCharge(printer);
                if (charge <= 0)
                {
                    ChemRuntime.Log("[printer] " + L("printer_calibrate_fail_charge"));
                    return false;
                }

                // 污染物清零 + 提纯等级=5（理论上限 99.84%，随后显式写 100）
                ChemCatalog.ApplyContaminants(target,
                    new int[ChemLab.ContaminantKeys.Length], ChemLab.MaxPurifyLevel);
                target.ModifyTag(ChemCatalog.TPurity, s => s.Enable().SetInt(100));
                target.ModifyTag(ChemCatalog.TGrade,
                    s => s.Enable().SetInt((int)PurityGrade.UltraPure));
                // 原版：purity=100 + LabPure（real=fake=lab_pure）
                target.EnableTag(ChemicalHelper.CHEM_ITEM_TAG);
                target.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
                target.ModifyTag(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG,
                    s => s.Enable().SetDouble(100.0));
                ChemicalProductHelper.SetPurityFeature(target, ItemConditionList.CreateLabPure());
                ChemCatalog.SyncVanillaPurity(target);

                // 消耗 1 充能
                printer.ModifyTag(ChemCatalog.TCatalystCharge, s => s.Enable().SetInt(charge - 1));

                ChemRuntime.Log("[printer] " + L("printer_calibrate_ok")
                                + " target=" + target.identifier + " chargeLeft=" + (charge - 1));
                return true;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[printer] calibrate failed: " + e.Message);
                return false;
            }
        }

        /// <summary>盖章：real=真实纯度档，fake=选中标签档。</summary>
        /// <summary>P7c：每盖一次章的现金成本（标签纸/耗材的等价成本，防止无成本刷高档标签）。</summary>
        public const int StampCost = 8;

        public static bool Stamp(GameItem target)
        {
            if (!IsPrintableTarget(target))
            {
                ChemRuntime.Log("[printer] " + L("printer_stamp_target_invalid"));
                return false;
            }
            try
            {
                double actualPurity = ChemRuntime.PurityOf(target);
                ItemCondition real = ConditionForGrade(GradeIndexForPurity(actualPurity));
                ItemCondition fake = ConditionForGrade(GradeIndex);

                // 补齐原版化学产品身份（旧存档物品可能没有 P1a 桥接）
                target.EnableTag(ChemicalHelper.CHEM_ITEM_TAG);
                target.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
                target.ModifyTag(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG,
                    s => s.Enable().SetDouble(actualPurity));

                // 真实/标签分离；不调用 GradeProduct（会按液体比例覆盖真实纯度）
                ChemicalProductHelper.SetPurityFeature(target, real, fake);

                if (MedicalMode)
                {
                    target.SetGameItemType(TypeHelper.MEDICAL);
                    target.EnableTag("fc_medical");
                }
                // 化学品模式：只加不撤 —— 已存在的医疗标签保留（避免撤销玩家/旧存档已有语义）

                ChemRuntime.Log("[printer] " + L("printer_stamp_ok", real.identifier, fake.identifier)
                                + " target=" + target.identifier + " medical=" + MedicalMode
                                + " realPurity=" + actualPurity.ToString("F2"));
                return true;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[printer] " + L("printer_stamp_fail", e.Message));
                return false;
            }
        }

        /// <summary>7 档 → 原版 ItemCondition（阈值 99/90/80/60/45/30，与 ChemicalProductHelper 一致）。</summary>
        public static ItemCondition ConditionForGrade(int gradeIndex)
        {
            switch (gradeIndex)
            {
                case 6: return ItemConditionList.CreateLabPure();
                case 5: return ItemConditionList.CreatePharmaGrade();
                case 4: return ItemConditionList.CreateNearPharma();
                case 3: return ItemConditionList.CreateStreetStandard();
                case 2: return ItemConditionList.CreateCutProduct();
                case 1: return ItemConditionList.CreateJunkGrade();
                default: return ItemConditionList.CreateStarchGrade();
            }
        }

        /// <summary>0–100 纯度 → 档位序号 0..6。</summary>
        public static int GradeIndexForPurity(double purity)
        {
            if (purity >= 99) return 6;
            if (purity >= 90) return 5;
            if (purity >= 80) return 4;
            if (purity >= 60) return 3;
            if (purity >= 45) return 2;
            if (purity >= 30) return 1;
            return 0;
        }

        static string L(string key, params object[] args)
            => ModHelper.GetLocalized(ChemCatalog.ModId, key, args);
    }
}
