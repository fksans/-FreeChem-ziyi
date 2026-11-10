using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeChem
{
    /// <summary>
    /// 本局状态（按局存档）+ 原版式毒性/欣快计算。
    /// 原版公式：L=Σ(体积 μl × 毒性系数)，L≥1000 致命、750≤L&lt;1000 有毒；
    /// E=Σ(体积 μl × 欣快系数)，欣快等级 0–4。
    /// 本 Mod 物品按整件计：毒性/欣快载荷基准 250 ml = 250000 μl（见 ToxicityLoad 注释）。
    /// 液体瓶实际容量 500 ml（ChemRecipes.PourVolumeMl），画像基准与瓶容量解耦。
    /// </summary>
    public static class ChemRuntime
    {
        const string KTemp = "cond_temp";
        const string KPh = "cond_ph";
        const string KBalanced = "cond_balanced";
        const string KProtected = "cond_protected";
        const string KExhaustRaw = "exhaust_raw";

        /// <summary>王尔德人脉：常驻排风服务（与 ModEntry 的常量同源，避免两处手写字符串漂移）。</summary>
        public const string VentServiceId = ModEntry.VentServiceId;
        public const string AdvVentServiceId = ModEntry.AdvVentServiceId;

        public static int Temp = 1;
        public static int Ph = 7;
        public static bool Balanced = false;
        // 玩家审查：默认开启防护（无护目镜时不至于把 hazard≥2 的反应全部变废渣）
        public static bool Protected = true;
        public static bool DangerousEnabled = true;
        public static bool GasBrokerUnlocked = false;

        /// <summary>
        /// 调试模式（运行期标志，不写 ModData；持久化由模组配置项 debug_mode 负责）。
        /// 默认关闭；开启后 F9 调试面板的 FreeChem 入口才允许生成物品。
        /// </summary>
        public static bool DebugMode = false;

        /// <summary>
        /// 累计的「有毒气体排放原始量」（单位：嫌疑单位，见 ChemLab.ExhaustUnitsForGas）。
        /// 按局存档（ModData key "exhaust_raw"）。
        /// </summary>
        public static int ExhaustRaw = 0;

        /// <summary>排风服务等级：0 无服务 / 1 基础排风 / 2 进阶排风；Load 时由王尔德升级解锁状态推导。</summary>
        public static int VentLevel = 0;

        /// <summary>
        /// 是否把有毒气体排放同时记入原版治安部（SecData.ENDANGERMENT 犯罪记录）。
        /// 默认开启，可在模组设置里关闭；原理见 docs/api-findings.md §2。
        /// </summary>
        public static bool VanillaSecurityEnabled = true;

        /// <summary>
        /// 每个「排气嫌疑单位」折算成多少点原版 ENDANGERMENT 犯罪量。
        /// 常量本体已移到 ChemLab（纯数据层，便于 harness 断言与两处共用），此处仅做别名。
        /// ⚠ 该倍率为静态设计值，未经实机平衡验证（见 docs/api-findings.md §2.4 风险清单）。
        /// </summary>
        public const int VanillaEndangermentPerUnit = ChemLab.VanillaEndangermentPerUnit;

        /// <summary>
        /// 排气嫌疑度（只读计算）。**显示值 = 写入原版治安部的 ENDANGERMENT 量**（问题 8 量纲统一）：
        ///   嫌疑度 = raw × VanillaEndangermentPerUnit(10) × 排风倍率 / 10000。
        /// 即无排风时 1 个排放单位显示 10；进阶排风 ×0.12 时 1 单位显示 1（整数截断）。
        /// 若 <see cref="VanillaSecurityEnabled"/> 且本局已载入，同一份排放还会按同一公式写入原版 SecData
        /// （见 CommitVanillaEndangerment）；官方 modding API 未公开该入口，属程序集层面核对过的未文档化用法。
        /// </summary>
        public static int Suspicion => ChemLab.SuspicionFrom(ExhaustRaw, VentLevel);

        /// <summary>排风等级对应的本地化文案键（供提示框拼接）。</summary>
        public static string VentLabelKey
            => VentLevel >= 2 ? "vent_advanced" : (VentLevel == 1 ? "vent_basic" : "vent_none");

        // ── P1a：原版桥接辅助（供 Harmony 兜底补丁与测试使用）──

        /// <summary>物品的模组纯度（0–100，读 fc_purity tag）。</summary>
        public static double PurityOf(GameItem item)
            => item == null ? 0.0 : ChemCatalog.ReadInt(item, ChemCatalog.TPurity, 0);

        /// <summary>物品对应的原版七档纯度阈值数值（99/90/80/60/45/30/0）。</summary>
        public static int VanillaGradeNumber(GameItem item)
            => ChemLab.VanillaGradeOf((int)Math.Round(PurityOf(item)));

        static readonly HashSet<string> Discovered = new HashSet<string>();
        static ModLog log;

        public static void Bind(ModLog modLog) => log = modLog;
        public static void Log(string msg) { if (log != null) log.Log("[FreeChem] " + msg); }

        public static void Load()
        {
            Temp      = ModHelper.GetModDataInt(ChemCatalog.ModId, KTemp, 1);
            Ph        = ModHelper.GetModDataInt(ChemCatalog.ModId, KPh, 7);
            Balanced  = ModHelper.GetModDataBool(ChemCatalog.ModId, KBalanced);
            Protected = ModHelper.GetModDataBool(ChemCatalog.ModId, KProtected, true);
            GasBrokerUnlocked = ModHelper.IsNetworkUpgradeUnlocked("free_chem.GAS_BROKER");
            ExhaustRaw = Mathf.Max(0, ModHelper.GetModDataInt(ChemCatalog.ModId, KExhaustRaw, 0));
            RefreshVentLevel();

            Discovered.Clear();
            string raw = ModHelper.GetModData(ChemCatalog.ModId, ChemRecipes.DiscoveredKey, "");
            if (!string.IsNullOrEmpty(raw))
                foreach (var id in raw.Split(','))
                    if (!string.IsNullOrEmpty(id)) Discovered.Add(id);
        }

        public static void SetTemp(int v)
        { Temp = Mathf.Clamp(v, 0, 3); ModHelper.SetModDataInt(ChemCatalog.ModId, KTemp, Temp); }
        public static void SetPh(int v)
        { Ph = Mathf.Clamp(v, 0, 14); ModHelper.SetModDataInt(ChemCatalog.ModId, KPh, Ph); }
        public static void SetBalanced(bool v)
        { Balanced = v; ModHelper.SetModDataBool(ChemCatalog.ModId, KBalanced, v); }
        public static void SetProtected(bool v)
        { Protected = v; ModHelper.SetModDataBool(ChemCatalog.ModId, KProtected, v); }

        // ── 排风服务 / 排气嫌疑度 / 原版治安部 ENDANGERMENT 折算 ──

        /// <summary>从王尔德人脉的解锁状态重新推导排风等级：进阶优先于基础。</summary>
        public static void RefreshVentLevel()
        {
            int lvl = 0;
            if (ModHelper.IsNetworkUpgradeUnlocked(AdvVentServiceId)) lvl = 2;
            else if (ModHelper.IsNetworkUpgradeUnlocked(VentServiceId)) lvl = 1;
            if (lvl != VentLevel)
                Log("vent service level " + VentLevel + " → " + lvl);
            VentLevel = lvl;
        }

        /// <summary>
        /// 累加有毒气体排放量并立即落盘；同时（默认）折算写入原版治安部犯罪记录。
        /// </summary>
        public static void AddExhaust(int units)
        {
            if (units <= 0) return;
            long sum = (long)ExhaustRaw + units;
            ExhaustRaw = sum > int.MaxValue ? int.MaxValue : (int)sum;
            ModHelper.SetModDataInt(ChemCatalog.ModId, KExhaustRaw, ExhaustRaw);
            int vanilla = CommitVanillaEndangerment(units);
            Log("toxic exhaust +" + units + " → raw " + ExhaustRaw
                + ", suspicion " + Suspicion
                + ", vanilla ENDANGERMENT +" + vanilla
                + (VanillaSecurityEnabled ? "" : " (vanilla hook disabled)"));
        }

        /// <summary>
        /// P7c：违禁品交易风险 —— controlled 物品交易时按价值增加原版治安部 ENDANGERMENT 嫌疑。
        /// </summary>
        public static void NoteContrabandTrade(GameItem item)
        {
            if (item == null) return;
            try
            {
                if (ChemCatalog.ReadInt(item, ChemCatalog.TControlled, 0) != 1) return;
                ChemSpec spec = ChemCatalog.Find(item.identifier);
                int value = spec == null ? 0 : spec.Value;
                int units = Mathf.Max(1, value / 40);
                int vanilla = CommitVanillaEndangerment(units);
                Log("contraband trade risk: " + item.identifier + " (+" + units + " units, vanilla +" + vanilla + ")");
            }
            catch (Exception e) { Log("contraband risk failed: " + e.Message); }
        }

        /// <summary>
        /// 把本次排放折算成原版治安部的 ENDANGERMENT 犯罪量并写入。
        ///
        /// 依据（静态核对，见 docs/api-findings.md §2）：PlayerStore.HasInstance / Instance
        /// 是 public static 成员，PlayerStore.secData 是 public 字段，SecData.CommitCrime(string,int)
        /// 与原版自身 CheckCrime 用的是同一入口；ENDANGERMENT 是 public const string。
        /// ⚠ 这是【未文档化】用法：官方 modding API 没有公开写治安部 heat/调查进度的接口；
        /// 写入会真实推进原版犯罪/调查流程，且数值平衡未实机验证，因此默认开启但可在模组设置关闭。
        /// ⚠ 必须先用 HasInstance 判空：PlayerStore.Instance 在无存档时会惰性新建一份 store。
        /// </summary>
        static int CommitVanillaEndangerment(int units)
        {
            if (!VanillaSecurityEnabled || units <= 0) return 0;
            try
            {
                if (!PlayerStore.HasInstance) return 0;   // 菜单/无存档：绝不调用 Instance
                PlayerStore store = PlayerStore.Instance;
                if (store == null) return 0;
                SecData sec = store.secData;
                if (sec == null) return 0;
                int bp = ChemLab.VentMultiplierBp(VentLevel);
                long amount = (long)units * ChemLab.VanillaEndangermentPerUnit * bp / 10000L;
                if (amount <= 0) return 0;
                sec.CommitCrime(SecData.ENDANGERMENT, (int)amount);
                return (int)amount;
            }
            catch (Exception e)
            {
                Log("vanilla ENDANGERMENT commit skipped: " + e.Message);
                return 0;
            }
        }

        /// <summary>提示框里的一行：排气嫌疑度 + 当前排风服务档位。</summary>
        public static string SuspicionLine()
            => ModHelper.GetLocalized(ChemCatalog.ModId, "suspicion_profile",
                   Suspicion,
                   ModHelper.GetLocalized(ChemCatalog.ModId, VentLabelKey));

        public static bool IsDiscovered(string id) => Discovered.Contains(id);
        public static int DiscoveredCount => Discovered.Count;

        public static bool Discover(string reactionId)
        {
            if (string.IsNullOrEmpty(reactionId) || !Discovered.Add(reactionId)) return false;
            ModHelper.SetModData(ChemCatalog.ModId, ChemRecipes.DiscoveredKey,
                                 string.Join(",", Discovered));
            return true;
        }

        public static bool IsAccident(int hazard)
            => hazard >= 2 && (!DangerousEnabled || !Protected);

        static readonly HashSet<string> accidentHintShown = new HashSet<string>();

        public static void NoteAccident(string reactionId)
        {
            ModHelper.SetModDataInt(ChemCatalog.ModId, "accidents",
                ModHelper.GetModDataInt(ChemCatalog.ModId, "accidents", 0) + 1);
            if (log != null) log.Warning("[FreeChem] Unprotected hazardous reaction: " + reactionId);

            // 玩家审查：事故只写日志玩家看不到 → 首次（每会话每反应）弹原版单按钮提示
            if (reactionId == null || !accidentHintShown.Add(reactionId)) return;
            try
            {
                string msg = ModHelper.GetLocalized(ChemCatalog.ModId, "accident_protection_hint");
                if (string.IsNullOrEmpty(msg) || msg == "accident_protection_hint")
                    msg = "危险反应需要防护（佩戴安全护目镜）且未关闭「危险反应」，否则只会得到废渣。";
                if (GenericConfirmationUIManager.Instance != null)
                    GenericConfirmationUIManager.Instance.OpenPanelSingleButton(msg, "OK", null);
            }
            catch (Exception e)
            {
                if (log != null) log.Warning("accident hint failed: " + e.Message);
            }
        }

        // ── 原版式毒性 / 欣快 ──
        /// <summary>
        /// 每件物品的有效成分体积基准：250 ml = 250000 μl（标准药剂瓶/气罐包装，问题 8 量纲统一）。
        /// 推导：ToxLoad 存的是「毒性系数 ×100」（如 4.0 → 400），L = VolumeUl × 系数 / 100
        ///   = 250000 × (ToxLoad/100) / 100 = ToxLoad × 25。
        /// 例：氯气 ToxLoad=400 → L=10000（致死）；盐酸 ToxLoad=30 → L=750（有毒边界）。
        /// 原版阈值：L ≥ 1000 致命、750 ≤ L &lt; 1000 有毒；E 同理，E ≥ 750 起欣快 1 级。
        /// </summary>
        const int VolumeUl = 250000;

        public static int ToxicityLoad(GameItem item)
        {
            if (item == null) return 0;
            int tox = ChemCatalog.ReadInt(item, ChemCatalog.TTox, 0);
            return Mathf.RoundToInt(VolumeUl * tox / 100f);
        }

        public static int EuphoriaLoad(GameItem item)
        {
            if (item == null) return 0;
            int euph = ChemCatalog.ReadInt(item, ChemCatalog.TEuph, 0);
            return Mathf.RoundToInt(VolumeUl * euph / 100f);
        }

        /// <summary>致命 / 有毒 / 安全，对齐原版阈值。</summary>
        public static string Lethality(GameItem item)
        {
            int l = ToxicityLoad(item);
            if (l >= 1000) return "lethal";
            if (l >= 750) return "toxic";
            return "safe";
        }

        /// <summary>欣快等级 0–4，对齐原版分段。</summary>
        public static int EuphoriaTier(GameItem item)
        {
            int e = EuphoriaLoad(item);
            if (e < 750) return 0;
            if (e < 1000) return 1;
            if (e < 1600) return 2;
            if (e < 2200) return 3;
            return 4;
        }

        public static bool IsAddictive(GameItem item)
            => item != null && ChemCatalog.ReadInt(item, ChemCatalog.TControlled, 0) == 1
               && EuphoriaTier(item) >= 1;

        /// <summary>
        /// 供提示框使用的一行化学画像（只对「真药剂」返回内容；问题 6）。
        /// 欣快等级只在 EuphoriaTier ≥ 1 或该物品 Controlled 时才显示，
        /// 普通酸碱盐只显示「纯度 + 致命性」，不再给每个物品都挂「欣快等级 0」。
        /// </summary>
        public static string ProfileLine(GameItem item)
        {
            if (item == null) return "";

            ChemSpec spec = ChemCatalog.Find(item.identifier);
            // 工具/器材/容器/耗材/加热器/副产物不是药剂：不显示化学画像
            if (spec != null && !ChemCatalog.IsReagentKind(spec.Kind)) return "";
            // 非本模组注册物品（原版瓶等）：只有带 ReagentTag 时才显示
            if (spec == null && !item.IsTag(ChemRecipes.ReagentTag)) return "";

            int purity = ChemCatalog.ReadInt(item, ChemCatalog.TPurity, 0);
            string lethal = Lethality(item);
            int tier = EuphoriaTier(item);
            bool controlled = ChemCatalog.ReadInt(item, ChemCatalog.TControlled, 0) == 1;

            string lethalText = ModHelper.GetLocalized(ChemCatalog.ModId, "lethal_" + lethal);
            if (tier >= 1 || controlled)
                return ModHelper.GetLocalized(ChemCatalog.ModId, "chem_profile",
                    purity, lethalText, tier);
            return ModHelper.GetLocalized(ChemCatalog.ModId, "chem_profile_reagent",
                purity, lethalText);
        }
    }
}
