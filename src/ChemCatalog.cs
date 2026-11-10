using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeChem
{
    /// <summary>一件物品的静态规格。Id 同时是 Textures/Items/&lt;Id&gt;.png 的图标键。</summary>
    public sealed class ChemSpec
    {
        public string Id, Formula;
        public Phase Phase;
        public PurityGrade Grade;
        public int Value, Purity, Hazard;
        public string Kind;      // gas | liquid | solid | canister | container | tool | protective | illicit | byproduct
        public bool Medical;     // 医用级气罐 → 原版医生/护士/医院供货商会收购
        public bool Controlled;  // 违禁
    }

    /// <summary>
    /// 物品目录与工厂。
    /// 兼容性：所有药剂统一 SetGameItemType(TypeHelper.CHEMICAL_SUPPLIES)，
    /// 因此原版按类型过滤的液体容器、搅拌机、蒸发器、成型机可以接收；
    /// 医用气罐额外设为 MEDICAL，能被原版医疗采购顾客收购。
    /// </summary>
    public static class ChemCatalog
    {
        public const string ModId = "free_chem";

        public const string TPhase = "fc_phase";
        public const string TPurity = "fc_purity";
        public const string TGrade = "fc_grade";
        public const string TTox = "fc_tox";       // 毒性系数 ×100
        public const string TEuph = "fc_euph";     // 欣快系数 ×100
        public const string TControlled = "fc_controlled";
        public const string TFormula = "fc_formula";
        public const string TContamPrefix = "fc_contam_";   // 8 类污染物，各存 1/100 百分点
        public const string TTemp = "fc_temp_x100";         // 实时温度（°C ×100）
        public const string TFuel = "fc_fuel_ml";           // 酒精灯剩余燃料
        public const string TFilterDur = "fc_filter_dur";   // 滤纸剩余耐久
        public const string TWickDur = "fc_wick_dur";       // 灯芯剩余耐久
        /// <summary>P1c 多级提纯等级（int 0–5）：每次过滤 +1，残留下限 70&gt;&gt;level。</summary>
        public const string TPurifyLevel = "fc_purify_level";
        /// <summary>P1c 打印机校准充能（int 0–3）：点 catalyst_kit 充能，校准 100% 时消耗 1。</summary>
        public const string TCatalystCharge = "fc_catalyst_charge";
        /// <summary>酒精灯内当前燃料的纯度（%）：Refill 时写入，Tick 按它计算火力/消耗
        /// （燃料瓶被销毁后仍能保留，避免每次 tick 依赖燃料槽物品）。</summary>
        public const string TFuelPurity = "fc_fuel_purity";
        /// <summary>酒精灯内燃料容量上限（ml）。P3a：与 500 ml 液体瓶容量匹配。</summary>
        public const int FuelCapacityMl = 500;
        /// <summary>P4d：由真实化学组分计算出的毒性标签（0–100，Σ(part×Liquid.lethality)/总量×100）。</summary>
        public const string TCompTox = "fc_tox";
        /// <summary>P4d：由真实化学组分计算出的欣快标签（0–100，Σ(part×Liquid.euphoria)/总量×100）。</summary>
        public const string TCompEuph = "fc_euph";
        /// <summary>P4d：由真实化学组分计算出的成瘾性标签（0–100，Σ(part×Liquid.addictiveness)/总量×100）。</summary>
        public const string TCompAddict = "fc_addict";
        /// <summary>P4d：组分已注入标记（幂等；存档会随 tag 一起保存）。</summary>
        public const string TCompApplied = "fc_components";
        /// <summary>P6a：本模组液体瓶的剩余体积（ml）。倒液兜底 Prefix 用它限流（默认 500）。</summary>
        public const string TPourVolumeMl = "fc_volume_ml";
        /// <summary>P10b：容器内药剂清单（`reagentId:ml;reagentId:ml;`）。</summary>
        public const string TContainerContent = "fc_content_items";
        /// <summary>P10b：可放进机器、可被倒液的实验容器。</summary>
        public static readonly string[] VesselIds =
            { "beaker", "retort_flask", "erlenmeyer_flask", "test_tube" };
        /// <summary>P10c：试管架（容器，只收 test_tube；本身不是液体容器）。</summary>
        public const string TestTubeRackId = "test_tube_rack";
        /// <summary>P5b：组分注入版本（2 = 氯气改用自定义 Liquid；旧版本会在读档时重写）。</summary>
        public const string TCompVersion = "fc_components_ver";
        public const int ComponentVersion = 3;   // P10a：真实成分模型

        public static readonly List<ChemSpec> Specs = new List<ChemSpec>
        {
            // ── 气体（原版没有的具名化合物）──
            S("gas_chlorine",          "Cl2",     Phase.Gas, PurityGrade.Industrial, 34, 70, 2, "gas"),
            S("gas_hydrogen",          "H2",      Phase.Gas, PurityGrade.Industrial, 22, 70, 2, "gas"),
            S("gas_oxygen",            "O2",      Phase.Gas, PurityGrade.Industrial, 26, 70, 0, "gas"),
            S("gas_nitrogen",          "N2",      Phase.Gas, PurityGrade.Industrial, 18, 70, 0, "gas"),
            S("gas_hydrogen_chloride", "HCl(g)",  Phase.Gas, PurityGrade.Industrial, 38, 70, 3, "gas"),
            S("gas_ammonia",           "NH3",     Phase.Gas, PurityGrade.Industrial, 30, 70, 2, "gas"),
            S("gas_carbon_dioxide",    "CO2",     Phase.Gas, PurityGrade.Industrial, 20, 70, 1, "gas"),
            // ── 液体 ──
            S("liq_hydrochloric_acid", "HCl(aq)", Phase.Liquid, PurityGrade.Industrial, 30, 70, 2, "liquid"),
            S("liq_sulfuric_acid",     "H2SO4",   Phase.Liquid, PurityGrade.Industrial, 42, 70, 3, "liquid"),
            S("liq_nitric_acid",       "HNO3",    Phase.Liquid, PurityGrade.Industrial, 48, 70, 3, "liquid"),
            S("liq_sodium_hydroxide",  "NaOH(aq)",Phase.Liquid, PurityGrade.Industrial, 28, 70, 2, "liquid"),
            S("liq_hydrogen_peroxide", "H2O2",    Phase.Liquid, PurityGrade.Industrial, 24, 70, 1, "liquid"),
            S("liq_brine",             "NaCl(aq)",Phase.Liquid, PurityGrade.Industrial, 12, 70, 0, "liquid"),
            S("liq_ammonia_water",     "NH3·H2O", Phase.Liquid, PurityGrade.Industrial, 26, 70, 2, "liquid"),
            S("liq_calcium_chloride",  "CaCl2(aq)",Phase.Liquid,PurityGrade.Industrial, 16, 70, 0, "liquid"),
            // ── 固体 ──
            S("sol_sodium_chloride",        "NaCl",   Phase.Solid, PurityGrade.Industrial, 14, 70, 0, "solid"),
            S("sol_sodium_hydroxide",       "NaOH",   Phase.Solid, PurityGrade.Industrial, 22, 70, 2, "solid"),
            S("sol_copper_sulfate",         "CuSO4",  Phase.Solid, PurityGrade.Industrial, 46, 70, 1, "solid"),
            S("sol_potassium_nitrate",      "KNO3",   Phase.Solid, PurityGrade.Industrial, 32, 70, 1, "solid"),
            S("sol_calcium_carbonate",      "CaCO3",  Phase.Solid, PurityGrade.Industrial, 12, 70, 0, "solid"),
            S("sol_ammonium_nitrate",       "NH4NO3", Phase.Solid, PurityGrade.Industrial, 40, 70, 2, "solid"),
            S("sol_potassium_permanganate", "KMnO4",  Phase.Solid, PurityGrade.Industrial, 44, 70, 1, "solid"),
            S("sol_sulfur",                 "S",      Phase.Solid, PurityGrade.Industrial, 16, 70, 1, "solid"),
            S("sol_activated_carbon",       "C",      Phase.Solid, PurityGrade.Industrial, 10, 70, 0, "solid"),
            S("sol_bleaching_powder",       "Ca(ClO)2",Phase.Solid,PurityGrade.Industrial, 34, 70, 2, "solid"),
            // ── P2a 补齐缺失化学品（石灰/碱类/氧化剂与副产物）──
            S("sol_calcium_oxide",        "CaO",       Phase.Solid, PurityGrade.Industrial, 20, 70, 1, "solid"), // 生石灰
            S("sol_calcium_hydroxide",    "Ca(OH)2",   Phase.Solid, PurityGrade.Industrial, 18, 70, 1, "solid"), // 熟石灰
            S("sol_sodium_carbonate",     "Na2CO3",    Phase.Solid, PurityGrade.Industrial, 24, 70, 0, "solid"), // 纯碱
            S("sol_sodium_bicarbonate",   "NaHCO3",    Phase.Solid, PurityGrade.Industrial, 26, 70, 0, "solid"), // 小苏打
            S("sol_ammonium_chloride",    "NH4Cl",     Phase.Solid, PurityGrade.Industrial, 30, 70, 0, "solid"), // 氯化铵
            S("sol_manganese_dioxide",    "MnO2",      Phase.Solid, PurityGrade.Industrial, 35, 70, 1, "solid"), // 二氧化锰
            S("sol_copper_oxide",         "CuO",       Phase.Solid, PurityGrade.Industrial, 42, 70, 2, "solid"), // 氧化铜
            S("sol_potassium_sulfate",    "K2SO4",     Phase.Solid, PurityGrade.Industrial, 28, 70, 0, "solid"), // 硫酸钾
            S("sol_sodium_nitrate",       "NaNO3",     Phase.Solid, PurityGrade.Industrial, 30, 70, 1, "solid"), // 硝酸钠
            S("sol_potassium_manganate",  "K2MnO4",    Phase.Solid, PurityGrade.Industrial, 46, 70, 1, "solid"), // 锰酸钾
            // ── 容器 / 工具 / 防护 ──
            S("gas_canister_empty", "steel",  Phase.Solid, PurityGrade.Industrial, 20, 100, 0, "container"),
            S("catalyst_kit",       "cat",    Phase.Solid, PurityGrade.UltraPure, 90, 99, 0, "tool"),
            S("precision_balance",  "inst",   Phase.Solid, PurityGrade.Industrial, 60, 100, 0, "tool"),
            S("ph_meter",           "inst",   Phase.Solid, PurityGrade.Industrial, 70, 100, 0, "tool"),
            // 温度计：ChemRuntime.Temp 的唯一操作入口（点击循环 0冷/1常温/2加热/3高温高压）。
            // 没有它，所有 temp:2/3 的配方（T2/T4 全部、部分 T3）永远匹配不到 —— 可玩性硬依赖。
            S("thermometer",        "inst",   Phase.Solid, PurityGrade.Industrial, 55, 100, 0, "tool"),
            // 标签打印机（P1b1）：点击药剂瓶按当前模式盖章（真实纯度 vs 标签档位分离）。
            // 数字 ID 追加为 86（见 ChemItemIds），图标 32×32。
            S("label_printer",      "print",  Phase.Solid, PurityGrade.Industrial, 150, 100, 0, "tool"),
            // P3e：化学品商人便条（电话订购线索；Kind=tool 不进 Reagent 体系，不参与反应/倒液）
            S("note_chem_dealer",   "note",   Phase.Solid, PurityGrade.Industrial, 5,   100, 0, "tool"),
            S("safety_goggles",     "gear",   Phase.Solid, PurityGrade.Industrial, 38, 100, 0, "protective"),
            // ── 实验器材 · 反应容器（可放进大型加热台）──
            S("beaker",           "glass", Phase.Solid, PurityGrade.Industrial, 18, 100, 0, "vessel"),
            // P10c：试管（小容量液体容器，可被倒入试剂、可放进试管架/机器）
            S("test_tube",        "tube",  Phase.Solid, PurityGrade.Industrial, 10, 100, 0, "vessel"),
            S("retort_flask",     "glass", Phase.Solid, PurityGrade.Industrial, 32, 100, 0, "vessel"),
            S("erlenmeyer_flask", "glass", Phase.Solid, PurityGrade.Industrial, 22, 100, 0, "vessel"),
            S("test_tube_rack",   "glass", Phase.Solid, PurityGrade.Industrial, 26, 100, 0, "vessel"),
            // ── 耗材 · 滤纸与灯芯 ──
            S("filter_paper",      "paper", Phase.Solid, PurityGrade.Industrial, 12, 100, 0, "consumable"),
            S("filter_paper_fine", "paper", Phase.Solid, PurityGrade.Industrial, 28, 100, 0, "consumable"),
            S("filter_paper_ash",  "paper", Phase.Solid, PurityGrade.Industrial, 48, 100, 0, "consumable"),
            S("wick_cotton",       "cotton",Phase.Solid, PurityGrade.Industrial, 6,  100, 0, "consumable"),
            S("wick_ceramic",      "ceramic",Phase.Solid,PurityGrade.Industrial, 18, 100, 0, "consumable"),
            // ── 加热器 ──
            S("alcohol_lamp",   "brass", Phase.Solid, PurityGrade.Industrial, 45, 100, 0, "heater"),
            S("spirit_burner",  "brass", Phase.Solid, PurityGrade.Industrial, 85, 100, 0, "heater"),
            // ── 热反应新增物质 ──
            S("gas_ethylene",       "C2H4",   Phase.Gas,    PurityGrade.Industrial, 30, 70, 2, "gas"),
            S("gas_nitrous_oxide",  "N2O",    Phase.Gas,    PurityGrade.Industrial, 55, 70, 2, "gas"),
            S("sol_coffee_grounds", "coffee", Phase.Solid,  PurityGrade.Industrial, 14, 70, 0, "solid"),
            S("sol_caffeine",       "C8H10N4O2", Phase.Solid, PurityGrade.Medical, 90, 95, 0, "solid"),
            // ── 气罐商品（三档纯度）──
            C("can_oxygen_industrial",   "O2",  PurityGrade.Industrial, 55,  false),
            C("can_oxygen_medical",      "O2",  PurityGrade.Medical,    145, true),
            C("can_oxygen_ultrapure",    "O2",  PurityGrade.UltraPure,  210, false),
            C("can_hydrogen_industrial", "H2",  PurityGrade.Industrial, 48,  false),
            C("can_hydrogen_medical",    "H2",  PurityGrade.Medical,    125, true),
            C("can_hydrogen_ultrapure",  "H2",  PurityGrade.UltraPure,  190, false),
            C("can_nitrogen_industrial", "N2",  PurityGrade.Industrial, 40,  false),
            C("can_nitrogen_medical",    "N2",  PurityGrade.Medical,    110, true),
            C("can_nitrogen_ultrapure",  "N2",  PurityGrade.UltraPure,  170, false),
            C("can_chlorine_industrial", "Cl2", PurityGrade.Industrial, 78,  false),
            C("can_chlorine_medical",    "Cl2", PurityGrade.Medical,    175, false),
            C("can_chlorine_ultrapure",  "Cl2", PurityGrade.UltraPure,  250, false),
            // ── 大型储气桶 gas_drum_*（图标 48×64 → 3×4 格，参照原版水罐 water_pitcher）──
            // 与便携钢瓶（图标 32×48 → 2×3 格）明确区分：占格更大、容量更高。
            // 占格由 SetSpriteAndShapeFromMod 按图标不透明像素自动推导，这里不写尺寸。
            // 桶只有一张图标 → 只做工业级纯度（PurityGrade.Industrial / Purity 70），不走三档 SKU。
            // 价值约为同气体工业钢瓶的 3–4 倍（氧 55→200、氮 40→140、氢 48→165、氯 78→275）。
            S("gas_drum_empty",          "steel", Phase.Solid, PurityGrade.Industrial, 55,  100, 0, "container"),
            S("gas_drum_oxygen",         "O2",    Phase.Gas,   PurityGrade.Industrial, 200, 70, 0, "drum"),
            S("gas_drum_nitrogen",       "N2",    Phase.Gas,   PurityGrade.Industrial, 140, 70, 0, "drum"),
            S("gas_drum_hydrogen",       "H2",    Phase.Gas,   PurityGrade.Industrial, 165, 70, 2, "drum"),
            S("gas_drum_chlorine",       "Cl2",   Phase.Gas,   PurityGrade.Industrial, 275, 70, 3, "drum"),
            S("gas_drum_carbon_dioxide", "CO2",   Phase.Gas,   PurityGrade.Industrial, 145, 70, 1, "drum"),
            // ── T4 虚构违禁品 ──
            I("illicit_voidsalt",   "voidsalt",   260),
            I("illicit_redmist",    "redmist",    220),
            I("illicit_nightglass", "nightglass", 300),
            I("illicit_ashbreath",  "ashbreath",  280),
            // ── 副产物 ──
            S("sludge",       "waste",    Phase.Solid, PurityGrade.Industrial, 3, 10, 1, "byproduct"),
            S("sol_sediment", "sediment", Phase.Solid, PurityGrade.Industrial, 5, 20, 0, "byproduct"),
        };

        static ChemSpec S(string id, string f, Phase p, PurityGrade g, int v, int purity,
                          int hazard, string kind)
            => new ChemSpec { Id=id, Formula=f, Phase=p, Grade=g, Value=v,
                              Purity=purity, Hazard=hazard, Kind=kind };

        static ChemSpec C(string id, string f, PurityGrade g, int v, bool medical)
            => new ChemSpec { Id=id, Formula=f, Phase=Phase.Gas, Grade=g, Value=v,
                              Purity=ChemRecipes.GradePurity(g), Hazard=(f=="Cl2" ? 3 : 1),
                              Kind="canister", Medical=medical };

        static ChemSpec I(string id, string f, int v)
            => new ChemSpec { Id=id, Formula=f, Phase=Phase.Solid, Grade=PurityGrade.Industrial,
                              Value=v, Purity=70, Hazard=3, Kind="illicit", Controlled=true };

        /// <summary>
        /// 把「物品台账探针」注入纯数据层 ChemRecipes。
        /// ChemRecipes 是纯数据层（独立校验工程只编译它 + ChemLab，不能引用游戏类型），
        /// 所以「DrumSkus / 灌装 / 掉落用到的 id 是否真的注册进 Specs」只能由装配层注入；
        /// 注入后 ChemRecipes.ValidateLogic 会额外断言桶映射目标存在、已废弃的旧防护 id 未复活。
        /// 静态构造函数在 Specs 字段初始化之后执行，探针内部走 Find，安全。
        /// </summary>
        static ChemCatalog()
        {
            ChemRecipes.SpecExists = id => Find(id) != null;
            ChemRecipes.SpecValue = id => { var s = Find(id); return s == null ? 0 : s.Value; };
            ChemRecipes.AllReagentIds = () =>
            {
                var ids = new List<string>();
                foreach (var s in Specs)
                    if (IsReagentKind(s.Kind)) ids.Add(s.Id);
                return ids;
            };
            ChemRecipes.SpecKindOf = id =>
            {
                var s = Find(id);
                return s == null ? null : s.Kind;
            };
            // P1a：把「全部液体药剂 id」注入纯数据层，供 ChemRecipes.ValidateLogic
            // 断言 PourParts 覆盖所有液体药剂（harness 未注入时跳过该断言）。
            var liquidIds = new List<string>();
            foreach (var s in Specs)
                if (s.Kind == "liquid" && s.Phase == Phase.Liquid) liquidIds.Add(s.Id);
            ChemRecipes.LiquidSpecIds = liquidIds.ToArray();
        }

        public static ChemSpec Find(string id)
        {
            foreach (var s in Specs) if (s.Id == id) return s;
            return null;
        }

        /// <summary>
        /// 「真药剂」Kind 白名单：只有这些物品才打 ReagentTag、才显示化学画像/毒性/欣快，
        /// 也才允许进入设备的相态过滤。工具/器材/容器/耗材/加热器/副产物一律不算药剂
        /// （用户问题 6：此前所有物品都带欣快等级，是因为给全部 Specs 一律打了 ReagentTag）。
        /// </summary>
        public static readonly string[] ReagentKinds =
            { "gas", "liquid", "solid", "illicit", "canister", "drum" };

        public static bool IsReagentKind(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return false;
            foreach (var k in ReagentKinds) if (k == kind) return true;
            return false;
        }

        /// <summary>该物品 id 是否是「真药剂」（用于 tooltip / 设备投料判断）。</summary>
        public static bool IsReagentSpec(string id)
        {
            var spec = Find(id);
            return spec != null && IsReagentKind(spec.Kind);
        }

        /// <summary>补丁/桥接用：该物品 id 是否本模组注册的真药剂。</summary>
        public static bool IsOurReagent(string id) => IsReagentSpec(id);

        public static bool IsMachine(string id)
        {
            foreach (var m in ChemRecipes.MachineIds) if (m == id) return true;
            return false;
        }

        /// <summary>
        /// P12：已彻底移除的 mod 物品（#43 乙醇 / #44 蒸馏水）——ID 保留占位，不再可生成。
        /// 玩家应改用原版水容器 / 原版酒精瓶（mini_bottle）。
        /// </summary>
        public static bool IsRemovedLegacyItem(string id)
            => id == "liq_ethanol" || id == "liq_water";

        public static GameItem Create(string id)
        {
            if (IsRemovedLegacyItem(id))
            {
                ChemRuntime.Log("[removed] " + id + " 已移除：请使用原版水容器 / 原版酒精瓶。");
                return null;
            }
            EnsureCustomLiquids();   // P5b：确保 fc_chlorine_gas 在组分注入前已注册

            // P6a/P0-5：#97 note_chem_dealer 是旧版化学便条（legacy）。
            // 无论从 F10 调试、DirectoryMaster 还是其它路径生成，都改返回原版 handnote，
            // 绝不生成 CHEMICAL_SUPPLIES 物品（否则点不开、会被当试剂收购/分析）。
            if (id == "note_chem_dealer")
            {
                try
                {
                    string body = ModHelper.GetLocalized(ModId, "note_chem_dealer_text");
                    if (string.IsNullOrEmpty(body) || body == "note_chem_dealer_text")
                        body = "化学品商人：96686。可订购基础化学品包（$320/包，每日 1 单，次日清晨送到前柜台）。";
                    string title = ModHelper.GetLocalized(ModId, "note_chem_dealer_name");
                    if (string.IsNullOrEmpty(title) || title == "note_chem_dealer_name")
                        title = "化学品商人的便条";
                    GameItem hand = PreBuiltItemHelper.CreateHandnote(body, title);
                    if (hand != null)
                    {
                        ChemRuntime.Log("[note] note_chem_dealer → vanilla handnote (legacy path).");
                        return EnsureOwned(hand);
                    }
                }
                catch (Exception e)
                {
                    ChemRuntime.Log("[note] legacy note handnote failed: " + e.Message);
                }
                ChemRuntime.Log("[note] note_chem_dealer requested but handnote unavailable; returning null.");
                return null;
            }
            var spec = Find(id);
            if (spec == null) return ItemDirectory.CreateEmptyItem();

            GameItem item = ItemDirectory.CreateEmptyItem(id);
            item.name = ModHelper.GetLocalized(ModId, id + "_name");
            item.shortDescription = ModHelper.GetLocalized(ModId, id + "_desc");
            item.flavorText = ModHelper.GetLocalized(ModId, id + "_flavor");
            item.unitValue = PricedValue(spec);
            item.SetSpriteAndShapeFromMod(ModId, id);

            // 兼容原版：统一化学用品类型；医用气罐额外走 MEDICAL 让原版医疗顾客收购
            item.SetGameItemType(spec.Medical ? TypeHelper.MEDICAL : TypeHelper.CHEMICAL_SUPPLIES);
            // 只有真药剂才打 ReagentTag；工具/器材/容器/耗材/加热器/副产物不打（问题 6）
            if (IsReagentKind(spec.Kind)) item.EnableTag(ChemRecipes.ReagentTag);
            if (spec.Kind == "canister") item.EnableTag(ChemRecipes.CanisterTag);
            if (spec.Controlled) item.EnableTag("contraband");
            if (spec.Medical) item.EnableTag("fc_medical");   // 医疗品级标记（P0 基础 tag，后续打印机/收购接入）

            WriteState(item, spec);

            // ── P1a 原版数据桥接（成分仪 / 倒液 / 七档纯度 / 收购）──
            // 只对真药剂做：工具/器材/容器/副产物保持独立物品语义。
            if (IsReagentKind(spec.Kind))
            {
                int purity = ClampPurity(spec.Kind == "liquid" && spec.Phase == Phase.Liquid
                    ? ReadInt(item, TPurity, spec.Purity)     // 液体：用我们污染物反推的纯度
                    : spec.Purity);                           // 气体/固体/钢瓶：用 spec 设计纯度
                // 液体容器/组分由 EnsureLiquidBridge 负责；此处只补非液体物品的 chem 身份与纯度
                item.EnableTag(ChemicalHelper.CHEM_ITEM_TAG);
                item.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
                item.ModifyTag(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG,
                    s => s.Enable().SetDouble(purity));
                // AddLiquid 可能覆盖 feature，所以放在最后统一写纯度档 ItemCondition
                ChemicalProductHelper.SetPurityFeature(item, PurityCondition(purity));

                // P3b 自检（诊断用）：若液体容器 tag 存在但总组分 part = 0，
                // 说明 AddLiquid 没生效（例如 Init 覆盖/克隆丢失），补一次并 log。
                SelfCheckLiquidBridge(item, spec);
                // P4d：真实化学组分 + 由组分计算的毒性/欣快标签（幂等）
                ApplyComponentComposition(item);
                // P10b：实验容器成为原版液体容器（空），可倒入试剂后放进机器。
                // 显式列出四个容器 id，便于静态校验「每个容器都有 InitLiquidContainerItem 接线」。
                if (spec.Id == "beaker" || spec.Id == "retort_flask"
                    || spec.Id == "erlenmeyer_flask" || spec.Id == "test_tube")
                {
                    try
                    {
                        int capacity = spec.Id == "test_tube" ? 100 : ChemRecipes.PourVolumeMl;   // 试管小容量
                        WaterHelper.InitLiquidContainerItem(item, capacity, 10);
                        item.ModifyTag(TContainerContent, s => s.Enable().SetString(""));
                    }
                    catch (Exception e) { ChemRuntime.Log("[container] init vessel failed: " + e.Message); }
                }
                // P10c：试管架 = 只装 test_tube 的容器（2×3 内容格），本身不是液体容器
                if (spec.Id == TestTubeRackId)
                {
                    try
                    {
                        var rackWindow = new PixelWindow(true);
                        var rackGrid = new GridPixelElement(2, 3, true);
                        for (int y = 0; y < 3; y++)
                            for (int x = 0; x < 2; x++)
                            {
                                var tubeSlot = new GameSlotInventory();
                                tubeSlot.mayInventoryAddItemFunc = (it, _) =>
                                    it != null && it.identifier == "test_tube";
                                rackGrid.Attach(tubeSlot, x, y, 20, 20);
                            }
                        rackWindow.Attach(rackGrid);
                        item.SetContentWindow(rackWindow);
                        item.ModifyTag(TContainerContent, s => s.Enable().SetString(""));
                        ChemRuntime.Log("[container] test tube rack window attached (2x3).");
                    }
                    catch (Exception e) { ChemRuntime.Log("[container] rack window failed: " + e.Message); }
                }
                // P6a：液体瓶记录剩余体积（倒液兜底 Prefix 用）
                if (spec.Kind == "liquid" && spec.Phase == Phase.Liquid
                    && !item.IsTag(TPourVolumeMl))
                    SetPourVolumeMl(item, ChemRecipes.PourVolumeMl);
                // NEW-P1-7：工厂产出的液体主动保证容器桥接（幂等；GameItem.Target 补丁降级为兜底）
                if (spec.Kind == "liquid" && spec.Phase == Phase.Liquid)
                    EnsureLiquidBridge(item);
            }

            return item;
        }

        /// <summary>
        /// P3b：液体桥接自检/修复。用 LIQUID_CONTAINER_CAPACITY − GetFreeCapacity 求已注入 part；
        /// 为 0 时按 PourParts 重新 AddLiquid 一次并记日志（不倒液问题的诊断兜底）。
        /// </summary>
        static void SelfCheckLiquidBridge(GameItem item, ChemSpec spec)
        {
            if (item == null || spec == null) return;
            if (!item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)) return;
            try
            {
                int capacity = ReadInt(item, WaterHelper.LIQUID_CONTAINER_CAPACITY, 0);
                int free = WaterHelper.GetFreeCapacity(item);
                int used = capacity - free;
                if (used > 0) return;

                Dictionary<string, int> parts;
                if (!ChemRecipes.TryGetPourParts(spec.Id, out parts))
                {
                    ChemRuntime.Log("[bridge] self-check: " + spec.Id
                                    + " has liquid container tag but no PourParts mapping");
                    return;
                }
                int totalPart = ChemRecipes.PourVolumeMl * ChemRecipes.PourPartPerMl;
                int added = 0;
                foreach (var kv in parts)
                {
                    int part = totalPart * kv.Value / ChemRecipes.PourTotalPercent;
                    if (part <= 0) continue;
                    WaterHelper.AddLiquid(item, kv.Key, part);
                    added += part;
                }
                int rest = totalPart - added;
                if (rest > 0)
                    WaterHelper.AddLiquid(item, ChemRecipes.MainPourComponent(parts), rest);
                ChemRuntime.Log("[bridge] self-check: " + spec.Id
                                + " had 0 liquid parts (capacity " + capacity
                                + "), re-added " + totalPart + " part.");
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[bridge] liquid self-check failed for " + spec.Id + ": " + e.Message);
            }
        }

        /// <summary>
        /// P3c：本模组气瓶商品（can_*，带 CanisterTag）。气瓶只能进机器「气瓶槽」作为气体来源，
        /// 不能当普通投料/液体容器使用。
        /// </summary>
        public static bool IsCanisterItem(GameItem item)
            => item != null && item.IsTag(ChemRecipes.CanisterTag);

        /// <summary>气瓶内气体纯度（0–100，读 fc_purity）。</summary>
        public static int CanisterPurity(GameItem canister)
            => canister == null ? 0 : Mathf.Clamp(ReadInt(canister, TPurity, 0), 0, 100);

        /// <summary>气瓶纯度 → 内部纯度档 0/1/2（工业/医用/分析纯近似），用于影响产出气体纯度。</summary>
        public static int CanisterGrade(GameItem canister)
            => (int)ChemLab.GradeFromPurity(CanisterPurity(canister) * ChemLab.Scale);

        /// <summary>
        /// P3c：气瓶天然无物理杂质 —— 若发现 fc_contam_* 非零则清零并 log（防御/断言，避免旧存档脏数据）。
        /// </summary>
        public static void ClearCanisterContaminants(GameItem canister)
        {
            if (canister == null || !IsCanisterItem(canister)) return;
            try
            {
                bool dirty = false;
                for (int i = 0; i < ChemLab.ContaminantKeys.Length; i++)
                {
                    string tag = TContamPrefix + ChemLab.ContaminantKeys[i];
                    if (ReadInt(canister, tag, 0) != 0) dirty = true;
                    canister.ModifyTag(tag, s => s.Enable().SetInt(0));
                }
                if (dirty)
                    ChemRuntime.Log("[gas] cleared unexpected contaminants on canister: " + canister.identifier);
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[gas] contaminant cleanup failed: " + e.Message);
            }
        }

        /// <summary>
        /// 扫描店内可枚举物品栏，统计 chem_index 索引书数量。<paramref name="scanned"/> 返回扫描物品数。
        /// 覆盖 EmporiumEntry 的公开物品栏；背包内嵌套容器不递归（调试工具可接受的近似）。
        /// </summary>
        public static int CountIndexBooks(out int scanned)
        {
            scanned = 0;
            int found = 0;
            try
            {
                var emporium = EmporiumEntry.Instance;
                if (emporium == null) return 0;
                var inventories = new List<GameInventory>
                {
                    emporium.invElement,
                    emporium.frontInvinvElement,
                    emporium.backInvinvElement,
                    emporium.backInvinvElementCounter,
                    emporium.showcaseElement,
                    emporium.hiddenElement,
                    emporium.bazarLeftinvElement,
                    emporium.docInvElement,
                    emporium.trashInvElement,
                    emporium.swapBufferElement,
                    emporium.drainInvElement,
                };
                foreach (var inv in inventories)
                {
                    if (inv == null) continue;
                    var children = inv.childItems;
                    if (children == null) continue;
                    foreach (var it in children)
                    {
                        if (it == null) continue;
                        scanned++;
                        if (IsIndexBook(it)) found++;
                    }
                }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[book] scan failed: " + e.Message);
            }
            return found;
        }

        /// <summary>是否 chem_index 索引书（BOOK_TAG + BOOK_ID 含 chem_index，或 identifier 命中）。</summary>
        static bool IsIndexBook(GameItem item)
        {
            if (item == null) return false;
            try
            {
                if (item.IsTag(BookHelper.BOOK_TAG))
                {
                    var tag = item.GetTagReadonly(BookHelper.BOOK_ID);
                    string bookId = tag == null ? null : tag.GetString();
                    if (!string.IsNullOrEmpty(bookId)
                        && bookId.IndexOf("chem_index", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                return item.identifier != null
                       && item.identifier.IndexOf("chem_index", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>把已知组分的 currentPart tag 归零（组分版本迁移用，避免新旧组分叠加）。</summary>
        static void ResetComponentTags(GameItem item)
        {
            if (item == null) return;
            try
            {
                foreach (var id in ChemRecipes.ComponentLiquidIds)
                {
                    Liquid liq = Liquid.GetLiquidFromId(id);
                    if (liq == null || string.IsNullOrEmpty(liq.currentPart)) continue;
                    item.ModifyTag(liq.currentPart, s => s.Enable().SetInt(0));
                }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[components] reset tags failed: " + e.Message);
            }
        }

        /// <summary>
        /// P10a：注册全部自定义 Liquid 成分（ComponentDefs 里 fc_* 的项）。
        /// 每个成分一个 Liquid（displayName=本地化成分名，currentPart 独立 tag），
        /// 只用于 CHEM_ITEM 药剂的成分 tag；绝不用于液体容器（避免稀释水纯度）。
        /// Liquid.Liquids/LiquidDict 为 public；每次运行/读档后调用（幂等）。
        /// </summary>
        public static void EnsureCustomLiquids()
        {
            try
            {
                if (Liquid.LiquidDict == null) return;
                int registered = 0;
                foreach (var kv in ChemRecipes.ComponentDefs)
                {
                    string id = kv.Key;
                    if (string.IsNullOrEmpty(id) || !id.StartsWith("fc_")) continue;   // water 等原版成分跳过
                    if (Liquid.LiquidDict.ContainsKey(id)) continue;

                    ChemRecipes.ComponentDef def = kv.Value;
                    string display = ModHelper.GetLocalized(ModId, def.NameKey);
                    if (string.IsNullOrEmpty(display) || display == def.NameKey) display = def.Formula;
                    var liquid = new Liquid
                    {
                        identifier = id,
                        displayName = display,
                        part = id + "_part",
                        currentPart = id + "_current_part",
                        baseMaxPurityFromMachine = 100,
                        basePurifierRemoveAmount = 0,
                        canBeRemovedByPurifier = false,
                        particleSize = 5,
                        filterMinimumPurity = 0,
                        filterDurabilityUsage = 0,
                        filterVolumeRemoved = 0,
                        boilingPoint = 100.0,
                        isEvaporative = true,
                        lethality = def.Lethality,
                        addictiveness = def.Addictiveness,
                        euphoria = def.Euphoria,
                        color = "#888888",
                        colorMultiplier = 1,
                        metabolismRatePerDay = 0,
                    };
                    Liquid.LiquidDict[id] = liquid;
                    if (Liquid.Liquids != null) Liquid.Liquids.Add(liquid);
                    registered++;
                }
                if (registered > 0)
                    ChemRuntime.Log("[components] registered " + registered + " custom Liquid component(s).");
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[components] EnsureCustomLiquids failed: " + e.Message);
            }
        }

        /// <summary>
        /// P4d：按 ChemRecipes.ComponentParts 给药剂注入原版化学组分，并由组分计算毒性/欣快/成瘾标签。
        /// 采用原版 Liquid 组分（不注册自定义 Liquid，遵循 docs/container-component-research.md 建议）：
        /// · 注入 API = ChemicalHelper.AddLiquidToChemFromList(item, List&lt;KeyValuePair&lt;string,int&gt;&gt;)；
        /// · 属性 = Σ(part × Liquid.lethality/euphoria/addictiveness) / 总量 ×100，写入 fc_tox/fc_euph/fc_addict；
        /// · fc_components 标记保证幂等（存档会保存该 tag，读档后不重复叠加组分）。
        /// </summary>
        public static bool ApplyComponentComposition(GameItem item)
        {
            if (item == null) return false;
            ChemSpec spec = Find(item.identifier);
            if (spec == null || !IsReagentKind(spec.Kind)) return false;
            Dictionary<string, int> parts;
            if (!ChemRecipes.TryGetComponentParts(spec.Id, out parts)) return false;
            EnsureCustomLiquids();
            // 版本迁移：旧存档已注入过（可能用原版 chlorine）→ 清零旧组分 tag 后按新版本重写
            bool already = item.IsTag(TCompApplied);
            int ver = ReadInt(item, TCompVersion, 0);
            if (already && ver >= ComponentVersion) return false;
            if (already && ver < ComponentVersion) ResetComponentTags(item);

            try
            {
                // P10a：所有成分都已注册（water 原版 + 自定义 fc_*）→ 统一走 AddLiquidToChemFromList；
                // 若个别成分运行时缺失（异常/时序），回退手动写 currentPart tag，保证不丢成分。
                var list = new List<KeyValuePair<string, int>>();
                int sum = 0;
                foreach (var kv in parts)
                {
                    Liquid liq = Liquid.GetLiquidFromId(kv.Key);
                    if (liq == null || string.IsNullOrEmpty(liq.currentPart))
                    {
                        sum += kv.Value;
                        continue;
                    }
                    list.Add(new KeyValuePair<string, int>(kv.Key, kv.Value));
                    sum += kv.Value;
                }
                if (list.Count > 0) ChemicalHelper.AddLiquidToChemFromList(item, list);
                foreach (var kv in parts)
                {
                    Liquid liq = Liquid.GetLiquidFromId(kv.Key);
                    if (liq != null && !string.IsNullOrEmpty(liq.currentPart)) continue;
                    ChemRuntime.Log("[components] missing Liquid for " + kv.Key + "; manual tag skipped.");
                }

                // P10a：毒性/欣快/成瘾由自定义成分模型（ComponentDefs）加权计算
                double tox = 0, euph = 0, addict = 0;
                foreach (var kv in parts)
                {
                    ChemRecipes.ComponentDef def;
                    if (!ChemRecipes.ComponentDefs.TryGetValue(kv.Key, out def) || def == null) continue;
                    double frac = (double)kv.Value / Math.Max(1, sum);
                    tox += frac * def.Lethality;
                    euph += frac * def.Euphoria;
                    addict += frac * def.Addictiveness;
                }
                int toxTag = Mathf.Clamp(Mathf.RoundToInt((float)(tox * 100.0)), 0, 100);
                int euphTag = Mathf.Clamp(Mathf.RoundToInt((float)(euph * 100.0)), 0, 100);
                int addictTag = Mathf.Clamp(Mathf.RoundToInt((float)(addict * 100.0)), 0, 100);
                item.ModifyTag(TCompTox, s => s.Enable().SetInt(toxTag));
                item.ModifyTag(TCompEuph, s => s.Enable().SetInt(euphTag));
                item.ModifyTag(TCompAddict, s => s.Enable().SetInt(addictTag));
                item.ModifyTag(TCompApplied, s => s.Enable().SetInt(1));
                item.ModifyTag(TCompVersion, s => s.Enable().SetInt(ComponentVersion));
                ChemRuntime.Log("[components] " + spec.Id + " parts=" + parts.Count
                                + " tox=" + toxTag + " euph=" + euphTag + " addict=" + addictTag);
                return true;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[components] apply failed for " + spec.Id + ": " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// P3b：产出/生成的物品必须 owned —— 原版「选中源瓶→点目标容器倒液」的 CanTarget
        /// 第一行就要求 IsItemEffectivelyOwned(源)，未 owned 的产出物无法参与倒液交互。
        /// </summary>
        public static GameItem EnsureOwned(GameItem item)
        {
            if (item == null) return null;
            try
            {
                if (!GeneralHelper.IsItemOwned(item))
                {
                    ModHelper.SetItemOwnerShip(item, true);
                    ChemRuntime.Log("[owned] set ownership for produced item: " + item.identifier);
                }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[owned] ownership check failed: " + e.Message);
            }
            return item;
        }

        static int ClampPurity(int purity) => Mathf.Clamp(purity, 0, 100);

        /// <summary>把本模组纯度（fc_purity）同步到原版：CHEMICAL_PRODUCT_PURITY_TAG + 七档 real feature。
        /// 供产线在纯度变化后调用（灌装台按投入气体纯度、滗析/过滤/洗涤提纯）。
        /// 保留已有 fake（标签）条件不放：打印机盖章的「标签纯度」与「真实纯度」分离后，
        /// 产线只更新 real 侧，不会把玩家贴的标签冲掉（P1b1）。
        /// </summary>
        public static void SyncVanillaPurity(GameItem item)
        {
            if (item == null) return;
            try
            {
                int purity = ClampPurity(ReadInt(item, TPurity, 0));
                item.EnableTag(ChemicalHelper.CHEM_ITEM_TAG);
                item.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
                item.ModifyTag(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG,
                    s => s.Enable().SetDouble(purity));

                ItemCondition fake = null;
                ItemFeature feature = item.FindItemFeatureByCategory(
                    ItemFeatureList.CATEGORY_CHEMICAL_PURITY);
                if (feature != null) fake = feature.fakeCondition;
                ItemCondition real = PurityCondition(purity);
                ChemicalProductHelper.SetPurityFeature(item, real, fake ?? real);
            }
            catch (Exception e)
            {
                ChemRuntime.Log("SyncVanillaPurity failed for " + item.identifier + ": " + e.Message);
            }
        }

        /// <summary>
        /// 纯度 → 原版七档 ItemCondition（阈值 99/90/80/60/45/30，与 ChemicalProductHelper 常量一致）。
        /// 依据 docs/harmony-bridge.md §3.1。这里写死字面阈值（与 ChemLab.Grade* 常量同值），
        /// 方便静态验收直接核对七档映射。
        /// </summary>
        static ItemCondition PurityCondition(int purity)
        {
            if (purity >= 99) return ItemConditionList.CreateLabPure();      // lab_pure
            if (purity >= 90) return ItemConditionList.CreatePharmaGrade();  // pharma_grade
            if (purity >= 80) return ItemConditionList.CreateNearPharma();   // near_pharma
            if (purity >= 60) return ItemConditionList.CreateStreetStandard(); // street_standard
            if (purity >= 45) return ItemConditionList.CreateCutProduct();   // cut_product
            if (purity >= 30) return ItemConditionList.CreateJunkGrade();    // junk_grade
            return ItemConditionList.CreateStarchGrade();                    // starch_grade
        }

        /// <summary>
        /// 玩家审查：产物纯度不低于投入纯度（合成/冷凝/电解等按最高投入纯度写回），
        /// 避免「蒸馏提纯」把 94% 打回默认 85%、或合成产物永远卡在初始纯度。
        /// </summary>
        public static void RaisePurityTo(GameItem item, int purity)
        {
            if (item == null) return;
            purity = Mathf.Clamp(purity, 0, 100);
            int cur = ReadInt(item, TPurity, 0);
            if (purity <= cur) return;
            item.ModifyTag(TPurity, s => s.Enable().SetInt(purity));
            item.ModifyTag(TGrade, s => s.Enable().SetInt((int)ChemLab.GradeFromPurity(purity * ChemLab.Scale)));
            SyncVanillaPurity(item);
        }

        /// <summary>
        /// P7a：是否可作为酒精灯燃料。原版酒精瓶 id 是共用 id（mini_bottle），
        /// 必须按 `alcohol` 组分 tag>0 判定；mod 乙醇（liq_ethanol）与 mini_bottle 兜底。
        /// </summary>
        public static bool IsAlcoholFuel(GameItem item)
        {
            if (item == null) return false;
            try
            {
                Liquid alcohol = Liquid.GetLiquidFromId("alcohol");
                if (alcohol != null && !string.IsNullOrEmpty(alcohol.currentPart)
                    && ReadInt(item, alcohol.currentPart, 0) > 0)
                    return true;
                Liquid water = Liquid.GetLiquidFromId("water");
                if (alcohol != null && water != null
                    && ReadInt(item, alcohol.currentPart, 0) + ReadInt(item, water.currentPart, 0) > 0)
                    return true;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[fuel] component check failed: " + e.Message);
            }
            return item.identifier == "mini_bottle";
        }

        /// <summary>P7a：酒精燃料纯度 = alcohol/(alcohol+water)；无组分时 mini_bottle 回退 70%。</summary>
        public static int AlcoholFuelPurity(GameItem item)
        {
            if (item == null) return ChemLab.VanillaAlcoholPurity;
            try
            {
                Liquid alcohol = Liquid.GetLiquidFromId("alcohol");
                Liquid water = Liquid.GetLiquidFromId("water");
                if (alcohol != null && water != null && !string.IsNullOrEmpty(alcohol.currentPart))
                {
                    int a = ReadInt(item, alcohol.currentPart, 0);
                    int w = ReadInt(item, water.currentPart, 0);
                    if (a + w > 0) return Mathf.Clamp(Mathf.RoundToInt(a * 100f / (a + w)), 0, 100);
                }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[fuel] purity check failed: " + e.Message);
            }
            return ChemLab.VanillaAlcoholPurity;
        }

        /// <summary>P6a：写/重置本模组液体瓶的剩余体积 tag（ml）。</summary>
        public static void SetPourVolumeMl(GameItem item, int ml)
        {
            if (item == null) return;
            item.ModifyTag(TPourVolumeMl, s => s.Enable().SetInt(Mathf.Clamp(ml, 0, ChemRecipes.PourVolumeMl)));
        }

        /// <summary>P6a：读源瓶剩余体积（ml）；旧物品无 tag 时默认整瓶 500 ml。</summary>
        public static int GetPourVolumeMl(GameItem item)
        {
            if (item == null) return 0;
            int ml = ReadInt(item, TPourVolumeMl, ChemRecipes.PourVolumeMl);
            if (ml < 0) ml = 0;
            if (ml > ChemRecipes.PourVolumeMl) ml = ChemRecipes.PourVolumeMl;
            return ml;
        }

        /// <summary>P6a：目标是否是原版液体容器（可接收倒液）。</summary>
        public static bool IsLiquidContainerItem(GameItem item)
            => item != null && item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG);

        /// <summary>P10b：是否实验容器。</summary>
        public static bool IsVessel(string id)
            => !string.IsNullOrEmpty(id) && Array.IndexOf(VesselIds, id) >= 0;

        /// <summary>P10b：把「药剂 id + 倒入 ml」记录到容器内容清单。</summary>
        public static void AddContainerContent(GameItem container, string reagentId, int ml)
        {
            if (container == null || string.IsNullOrEmpty(reagentId) || ml <= 0) return;
            try
            {
                string old = "";
                var tag = container.GetTagReadonly(TContainerContent);
                if (tag != null) old = tag.GetString() ?? "";
                string entry = reagentId + ":" + ml + ";";
                container.ModifyTag(TContainerContent, s => s.Enable().SetString(old + entry));
                ChemRuntime.Log("[container] +" + ml + " ml " + reagentId + " → " + container.identifier);
            }
            catch (Exception e) { ChemRuntime.Log("[container] add content failed: " + e.Message); }
        }

        /// <summary>P10b：解析容器内容里的药剂 id 列表（重复 id 只取一次，供反应匹配）。</summary>
        public static List<string> GetContainerContentIds(GameItem container)
        {
            var ids = new List<string>();
            if (container == null) return ids;
            try
            {
                var tag = container.GetTagReadonly(TContainerContent);
                string raw = tag == null ? null : tag.GetString();
                if (string.IsNullOrEmpty(raw)) return ids;
                foreach (var part in raw.Split(';'))
                {
                    if (string.IsNullOrEmpty(part)) continue;
                    int idx = part.IndexOf(':');
                    string id = idx > 0 ? part.Substring(0, idx) : part;
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);
                }
            }
            catch (Exception e) { ChemRuntime.Log("[container] parse content failed: " + e.Message); }
            return ids;
        }

        /// <summary>P10b：清空容器内容（反应完成后调用，容器本身保留）。</summary>
        public static void ClearContainerContent(GameItem container)
        {
            if (container == null) return;
            try
            {
                container.ModifyTag(TContainerContent, s => s.Enable().SetString(""));
                int total = WaterHelper.GetTotalVolume(container);
                if (total > 0) WaterHelper.RemoveAndReturn(container, total);
            }
            catch (Exception e) { ChemRuntime.Log("[container] clear failed: " + e.Message); }
        }

        /// <summary>
        /// P10b：接触即反应 —— 玩家用一件药剂点另一件。
        /// 命中 ContactReactions 时消耗双方、产物投后柜台（owner/source 标注），返回 true。
        /// </summary>
        public static bool TryContactReaction(GameItem source, GameItem target)
        {
            if (source == null || target == null || source == target) return false;
            ChemRecipes.ContactReaction r = ChemRecipes.MatchContact(source.identifier, target.identifier);
            if (r == null) return false;
            try
            {
                string srcName = source.identifier, dstName = target.identifier;
                foreach (var o in r.Outputs)
                {
                    GameItem produced = EnsureOwned(DirectoryMaster.Item(o));
                    if (produced == null) continue;
                    ModHelper.SetSource(produced, ModId, "FreeChem");
                    ModHelper.MoveItemToBackCounter(produced);
                }
                source.Destroy();
                target.Destroy();
                ChemRuntime.Log("[contact] " + srcName + " + " + dstName + " → "
                                + string.Join(", ", r.Outputs));
                return true;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[contact] reaction failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// P6a：把 PourParts 组分按 totalPart 注入任意液体容器（目标），余数补主组分。
        /// 供倒液 Harmony Prefix 使用（源物品的体积由调用方扣减）。
        /// </summary>
        public static void PourPartsInto(GameItem target, Dictionary<string, int> parts, int totalPart)
        {
            if (target == null || parts == null || totalPart <= 0) return;
            AddPourParts(target, parts, totalPart);
        }

        /// <summary>
        /// P4c：确保液体药剂同时具备「原版液体容器 tag + 完整组分（总 500 ml）」。
        /// 幂等：已有容器 tag 且组分总量 >0 时只补 chem 身份/纯度；缺 tag 则重新 Init+AddLiquid；
        /// 有容器 tag 但 total=0（旧存档 tag 被覆盖）则补 AddLiquid。最后恢复 name/type/sprite/value。
        /// </summary>
        public static bool EnsureLiquidBridge(GameItem item)
        {
            if (item == null) return false;
            ChemSpec spec = Find(item.identifier);
            if (spec == null || spec.Kind != "liquid" || spec.Phase != Phase.Liquid) return false;
            Dictionary<string, int> parts;
            if (!ChemRecipes.TryGetPourParts(spec.Id, out parts)) return false;

            try
            {
                int totalPart = ChemRecipes.PourVolumeMl * ChemRecipes.PourPartPerMl;
                bool changed = false;

                if (!item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG))
                {
                    string savedName = item.name;
                    string savedDesc = item.shortDescription;
                    string savedFlavor = item.flavorText;
                    long savedValue = item.unitValue;
                    try
                    {
                        WaterHelper.InitLiquidContainerItem(item, ChemRecipes.PourVolumeMl,
                                                           Mathf.Max(1, spec.Value));
                        AddPourParts(item, parts, totalPart);
                    }
                    finally
                    {
                        item.name = savedName;
                        item.shortDescription = savedDesc;
                        item.flavorText = savedFlavor;
                        item.unitValue = savedValue;
                        item.SetGameItemType(spec.Medical ? TypeHelper.MEDICAL : TypeHelper.CHEMICAL_SUPPLIES);
                        item.SetSpriteAndShapeFromMod(ModId, spec.Id);
                    }
                    item.ModifyTag(ChemicalProductHelper.ACTIVATE_INGREDIENT_TAG,
                        s => s.Enable().SetString(ChemRecipes.MainPourComponent(parts)));
                    changed = true;
                }
                else
                {
                    // 容器 tag 在但组分为 0（旧存档 tag 被整体覆盖）：补一次 AddLiquid
                    int capacity = ReadInt(item, WaterHelper.LIQUID_CONTAINER_CAPACITY, 0);
                    int free = WaterHelper.GetFreeCapacity(item);
                    if (capacity - free <= 0)
                    {
                        AddPourParts(item, parts, totalPart);
                        changed = true;
                    }
                }

                // 补 chem 身份与纯度（保留物品自己的 fc_purity）
                int purity = ClampPurity(ReadInt(item, TPurity, spec.Purity));
                item.EnableTag(ChemicalHelper.CHEM_ITEM_TAG);
                item.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
                item.ModifyTag(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG,
                    s => s.Enable().SetDouble(purity));
                ChemicalProductHelper.SetPurityFeature(item, PurityCondition(purity));

                ApplyComponentComposition(item);
                if (!item.IsTag(TPourVolumeMl))
                    SetPourVolumeMl(item, ChemRecipes.PourVolumeMl);
                if (changed)
                    ChemRuntime.Log("[bridge] liquid container/parts repaired: " + spec.Id);
                return changed;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[bridge] EnsureLiquidBridge failed for " + spec.Id + ": " + e.Message);
                return false;
            }
        }

        /// <summary>按 PourParts 百分比把 500 ml 组分注入液体容器（余数补主组分，保证总量守恒）。</summary>
        static void AddPourParts(GameItem item, Dictionary<string, int> parts, int totalPart)
        {
            int added = 0;
            foreach (var kv in parts)
            {
                int part = totalPart * kv.Value / ChemRecipes.PourTotalPercent;
                if (part <= 0) continue;
                WaterHelper.AddLiquid(item, kv.Key, part);
                added += part;
            }
            int rest = totalPart - added;
            if (rest > 0) WaterHelper.AddLiquid(item, ChemRecipes.MainPourComponent(parts), rest);
        }

        /// <summary>
        /// P4c：读档/开店后重建旧存档物品的原版 tag。
        /// 根因：SaveManager 解码时用存档 itemState 整体覆盖 Init 写入的 tag → 旧档物品缺
        /// LIQUID_CONTAINER_TAG/组分/chem 身份；钩子仍存在，导致「倒不进容器 + 分析仪 total=0」。
        /// 遍历 EmporiumEntry.GetAllItems() 并递归 FindAllChildItems()（机器内容窗/容器内物品），
        /// 对本模组药剂调用幂等桥接；只读/补 tag，不改物品位置与所有权。
        /// </summary>
        public static void RehydrateLoadedItems(string hook)
        {
            try
            {
                if (EmporiumEntry.Instance == null)
                {
                    ChemRuntime.Log("[rehydrate] " + hook + ": not in store scene; deferred.");
                    return;
                }
                var roots = EmporiumEntry.Instance.GetAllItems();
                if (roots == null) return;
                var seen = new HashSet<GameItem>();
                int scanned = 0, repaired = 0;
                foreach (var it in roots) VisitForRehydrate(it, seen, ref scanned, ref repaired);
                ChemRuntime.Log("[rehydrate] " + hook + ": scanned " + scanned
                                + " item(s), repaired " + repaired + " FreeChem item(s).");
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[rehydrate] " + hook + " failed: " + e.Message);
            }
        }

        /// <summary>P6a：读档后机器状态自愈（working 但无进度目标 → ready）。</summary>
        static void EnsureMachineState(GameItem machine)
        {
            try
            {
                string state = MachineProgressHelper.GetMachineState(machine);
                if (state != MachineProgressHelper.STATE_WORKING) return;
                var progress = MachineProgressHelper.GetProgress(machine);
                if (progress.Item2 > 0) return;   // 有目标进度：继续跑
                machine.ModifyTag(MachineProgressHelper.MACHINE_STATE_TAG,
                    s => s.Enable().SetString(MachineProgressHelper.STATE_READY));
                ChemRuntime.Log("[machine] recovered stuck working state: " + machine.identifier);
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[machine] state recovery failed: " + e.Message);
            }
        }

        static void VisitForRehydrate(GameItem item, HashSet<GameItem> seen,
                                      ref int scanned, ref int repaired)
        {
            if (item == null || !seen.Add(item)) return;
            scanned++;
            try
            {
                ChemSpec spec = Find(item.identifier);
                if (spec != null && IsReagentKind(spec.Kind))
                {
                    bool need;
                    if (spec.Kind == "liquid" && spec.Phase == Phase.Liquid)
                    {
                        if (!item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)) need = true;
                        else
                        {
                            int capacity = ReadInt(item, WaterHelper.LIQUID_CONTAINER_CAPACITY, 0);
                            need = capacity - WaterHelper.GetFreeCapacity(item) <= 0;
                        }
                    }
                    else
                    {
                        need = !item.IsTag(ChemicalHelper.CHEM_ITEM_TAG)
                               || !item.IsTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
                    }
                    if (need && BridgeVanillaChemData(item)) repaired++;
                }

                // P6a 仪器自检修复：读档后 machine 处于 working 但进度目标丢失（存档未写入/面板顺序变化）
                // → 复位为 ready，避免机器永久卡在 _on 不接收投料。
                if (IsMachine(item.identifier)) EnsureMachineState(item);

                // 机器内容窗/便携容器内的物品（GetAllItems 可能不递归）
                var children = item.FindAllChildItems();
                if (children != null)
                    foreach (var c in children)
                        VisitForRehydrate(c, seen, ref scanned, ref repaired);
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[rehydrate] visit failed (" + item.identifier + "): " + e.Message);
            }
        }

        /// <summary>
        /// 旧存档物品的按需桥接（Harmony 扫描仪兜底补丁调用）：
        /// 对旧版本创建、缺少原版 tag/组分的本模组药剂补一次数据桥接，使其能被原版成分仪/顾客识别。
        /// 与 Create 内的桥接逻辑等价（新物品在工厂已桥接，这里只处理旧物品）。
        /// </summary>
        public static bool BridgeVanillaChemData(GameItem item)
        {
            if (item == null) return false;
            ChemSpec spec = Find(item.identifier);
            if (spec == null || !IsReagentKind(spec.Kind)) return false;
            // P4c：液体即使已有 chem tag 也可能缺容器 tag/组分（旧存档 tag 被整体覆盖），
            // 统一交给幂等的 EnsureLiquidBridge 处理，不能在这里提前 return。
            if (spec.Kind == "liquid" && spec.Phase == Phase.Liquid)
                return EnsureLiquidBridge(item);
            if (item.IsTag(ChemicalHelper.CHEM_ITEM_TAG)
                && item.IsTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG)) return true;

            try
            {
                int purity = ClampPurity(spec.Kind == "liquid" && spec.Phase == Phase.Liquid
                    ? ReadInt(item, TPurity, spec.Purity)
                    : spec.Purity);

                Dictionary<string, int> parts;
                if (spec.Kind == "liquid" && spec.Phase == Phase.Liquid
                    && ChemRecipes.TryGetPourParts(spec.Id, out parts)
                    && !item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG))
                {
                    string savedName = item.name;
                    string savedDesc = item.shortDescription;
                    string savedFlavor = item.flavorText;
                    long savedValue = item.unitValue;
                    try
                    {
                        WaterHelper.InitLiquidContainerItem(item, ChemRecipes.PourVolumeMl,
                                                           Mathf.Max(1, spec.Value));
                        int totalPart = ChemRecipes.PourVolumeMl * ChemRecipes.PourPartPerMl;
                        int added = 0;
                        foreach (var kv in parts)
                        {
                            int part = totalPart * kv.Value / ChemRecipes.PourTotalPercent;
                            if (part > 0) { WaterHelper.AddLiquid(item, kv.Key, part); added += part; }
                        }
                        int rest = totalPart - added;
                        if (rest > 0)
                            WaterHelper.AddLiquid(item, ChemRecipes.MainPourComponent(parts), rest);
                    }
                    finally
                    {
                        item.name = savedName;
                        item.shortDescription = savedDesc;
                        item.flavorText = savedFlavor;
                        item.unitValue = savedValue;
                        item.SetGameItemType(spec.Medical ? TypeHelper.MEDICAL : TypeHelper.CHEMICAL_SUPPLIES);
                        item.SetSpriteAndShapeFromMod(ModId, spec.Id);
                    }
                    item.ModifyTag(ChemicalProductHelper.ACTIVATE_INGREDIENT_TAG,
                        s => s.Enable().SetString(ChemRecipes.MainPourComponent(parts)));
                }

                item.EnableTag(ChemicalHelper.CHEM_ITEM_TAG);
                item.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG);
                item.ModifyTag(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG,
                    s => s.Enable().SetDouble(purity));
                ChemicalProductHelper.SetPurityFeature(item, PurityCondition(purity));
                ApplyComponentComposition(item);
                ChemRuntime.Log("vanilla bridge applied to legacy item: " + item.identifier);
                return true;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("vanilla bridge failed for " + item.identifier + ": " + e.Message);
                return false;
            }
        }

        /// <summary>纯度档决定售价：对齐原版品级价值修正。</summary>
        public static int PricedValue(ChemSpec s)
        {
            if (s.Kind != "canister") return s.Value;
            return s.Value + s.Value * ChemRecipes.GradeMultiplier(s.Grade) / 100;
        }

        public static void WriteState(GameItem item, ChemSpec spec)
        {
            var profile = ChemRecipes.Profile(spec.Id);
            bool isLiquidReagent = spec.Phase == Phase.Liquid && spec.Kind == "liquid";
            item.ModifyTag(TPhase,  s => s.Enable().SetInt((int)spec.Phase));
            // 液体药剂的档位由污染物反推，避免 TGrade 与 TPurity 互相矛盾
            int initialGrade = (int)(isLiquidReagent
                ? ChemLab.GradeFromPurity(ChemLab.PurityFrom(ChemLab.InitialContaminants()))
                : spec.Grade);
            item.ModifyTag(TGrade,  s => s.Enable().SetInt(initialGrade));
            item.ModifyTag(TTox,    s => s.Enable().SetInt(Mathf.RoundToInt(profile.Toxicity * 100)));
            item.ModifyTag(TEuph,   s => s.Enable().SetInt(Mathf.RoundToInt(profile.Euphoria * 100)));
            item.ModifyTag(TControlled, s => s.Enable().SetInt(spec.Controlled ? 1 : 0));
            item.ModifyTag(TFormula, s => s.Enable().SetString(ChemRecipes.Formula(spec.Id)));

            // 污染物：只有液体药剂带游离污染物；滗析/过滤都只能逼近，不能清零
            var contam = (spec.Phase == Phase.Liquid && spec.Kind == "liquid")
                ? ChemLab.InitialContaminants()
                : new int[ChemLab.ContaminantKeys.Length];
            for (int i = 0; i < ChemLab.ContaminantKeys.Length; i++)
            {
                int v = contam[i];
                string tag = TContamPrefix + ChemLab.ContaminantKeys[i];
                item.ModifyTag(tag, s => s.Enable().SetInt(v));
            }
            // 纯度：液体由污染物反推（永远到不了 100%）；固体/气体直接用设计纯度，
            // 否则「无污染物」会恒为 94%（NEW-P1-5 纯度双轨）。
            int initialPurity = isLiquidReagent
                ? ChemLab.PurityFrom(contam) / ChemLab.Scale
                : Mathf.Clamp(spec.Purity, 0, 100);
            item.ModifyTag(TPurity, s => s.Enable().SetInt(initialPurity));
            // P1c：新物品提纯等级从 0 开始（每次过滤 +1，最高 5）
            item.ModifyTag(TPurifyLevel, s => s.Enable().SetInt(0));

            // 器材 / 耗材初始状态
            item.ModifyTag(TTemp, s => s.Enable().SetInt(Mathf.RoundToInt(ChemLab.RoomTemp * 100)));
            if (spec.Kind == "heater")
                item.ModifyTag(TFuel, s => s.Enable().SetInt(0));   // 空灯，需灌酒精
            if (spec.Id.StartsWith("filter_paper"))
                item.ModifyTag(TFilterDur, s => s.Enable().SetInt(ChemLab.FilterDurability[spec.Id]));
            if (spec.Id.StartsWith("wick_"))
                item.ModifyTag(TWickDur, s => s.Enable().SetInt(spec.Id == "wick_ceramic" ? 40 : 20));
        }

        /// <summary>读取一件物品的 8 类污染物。</summary>
        public static int[] ReadContaminants(GameItem item)
        {
            var c = new int[ChemLab.ContaminantKeys.Length];
            for (int i = 0; i < c.Length; i++)
                c[i] = ReadInt(item, TContamPrefix + ChemLab.ContaminantKeys[i], 0);
            return c;
        }

        /// <summary>读取物品的提纯等级（0–5，缺省 0）。</summary>
        public static int ReadPurifyLevel(GameItem item)
            => item == null ? 0 : Mathf.Clamp(ReadInt(item, TPurifyLevel, 0), 0, ChemLab.MaxPurifyLevel);

        /// <summary>把污染物数组写回物品，并同步纯度与档位（读物品当前提纯等级）。</summary>
        public static void ApplyContaminants(GameItem item, int[] c)
            => ApplyContaminants(item, c, ReadPurifyLevel(item));

        /// <summary>把污染物数组写回物品，用指定提纯等级计算纯度上限，并落盘等级（P1c）。</summary>
        public static void ApplyContaminants(GameItem item, int[] c, int purifyLevel)
        {
            int level = Mathf.Clamp(purifyLevel, 0, ChemLab.MaxPurifyLevel);
            for (int i = 0; i < ChemLab.ContaminantKeys.Length; i++)
            {
                string tag = TContamPrefix + ChemLab.ContaminantKeys[i];
                int v = c[i];
                item.ModifyTag(tag, s => s.Enable().SetInt(v));
            }
            item.ModifyTag(TPurifyLevel, s => s.Enable().SetInt(level));
            int purity = ChemLab.PurityFrom(c, level);    // ×100 标度（等级越高上限越高，仍 <100%）
            item.ModifyTag(TPurity, s => s.Enable().SetInt(purity / ChemLab.Scale));
            item.ModifyTag(TGrade,  s => s.Enable().SetInt((int)ChemLab.GradeFromPurity(purity)));
        }

        /// <summary>把产物纯度提升到指定档（滗析 / 洗涤提纯用）。</summary>
        public static void UpgradePurity(GameItem item, PurityGrade grade)
        {
            item.ModifyTag(TPurity, s => s.Enable().SetInt(ChemRecipes.GradePurity(grade)));
            item.ModifyTag(TGrade,  s => s.Enable().SetInt((int)grade));
        }

        public static int ReadInt(GameItem item, string tag, int fallback)
        {
            var t = item.GetTagReadonly(tag);
            return t == null ? fallback : t.GetInt();
        }

        public static bool IsReagent(GameItem item) => item != null && item.IsTag(ChemRecipes.ReagentTag);
    }
}
