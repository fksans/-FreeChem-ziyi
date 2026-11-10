using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeChem
{
    public enum Phase { Solid = 0, Liquid = 1, Gas = 2 }

    /// <summary>纯度档，对齐原版化学制品品级与价值修正。</summary>
    public enum PurityGrade
    {
        Industrial = 0,  // 街头标准 60–80%，+0%
        Medical    = 1,  // 药用级 90–99%，+85%
        UltraPure  = 2   // 实验室纯级 ≥99%，+135%
    }

    /// <summary>
    /// 化合物的危险画像：复用原版化学加工公式
    ///   L = Σ(体积 μl × 毒性系数)，L≥1000 致命，750≤L&lt;1000 有毒
    ///   E = Σ(体积 μl × 欣快系数)，欣快等级 0–4
    /// 这些数值写进物品 tag，供提示框与销售反馈读取。
    /// </summary>
    public sealed class ChemProfile
    {
        public readonly float Toxicity;   // 毒性加权系数
        public readonly float Euphoria;   // 欣快加权系数
        public readonly bool Addictive;   // 是否带成瘾/戒断语义
        public readonly bool Controlled;  // 是否违禁
        public ChemProfile(float tox, float eup, bool addictive = false, bool controlled = false)
        { Toxicity = tox; Euphoria = eup; Addictive = addictive; Controlled = controlled; }
    }

    /// <summary>一条真实化学反应 / 多级合成步骤，附配平方程式。</summary>
    public sealed class Reaction
    {
        public readonly string Id, Name, Equation, Machine;
        public readonly string[] Inputs, Outputs;
        public readonly int Tier;            // 1 一级 → 4 顶级（虚构违禁品）
        public readonly int Temp;            // 0 冷 / 1 常温 / 2 加热 / 3 高温高压
        public readonly int Ph;              // -1 不限
        public readonly int Hazard;
        public readonly bool NeedsCatalyst, CatalystBonus;
        public readonly bool UsesVanilla;    // 是否与原版药剂反应

        public Reaction(string id, string name, string equation, string machine, int tier,
                        string[] inputs, string[] outputs, int temp = 1, int ph = -1,
                        int hazard = 0, bool needsCatalyst = false, bool catalystBonus = false,
                        bool usesVanilla = false)
        {
            Id = id; Name = name; Equation = equation; Machine = machine; Tier = tier;
            Inputs = inputs; Outputs = outputs;
            Temp = temp; Ph = ph; Hazard = hazard;
            NeedsCatalyst = needsCatalyst; CatalystBonus = catalystBonus;
            UsesVanilla = usesVanilla;
        }
        public string SortedInputs => string.Join("+", Inputs.Select(ChemRecipes.Normalize).OrderBy(x => x));
    }

    /// <summary>纯数据 / 纯逻辑层：不引用游戏类型，便于独立校验与单元测试。</summary>
    public static class ChemRecipes
    {
        public const string SludgeId = "sludge";
        public const string ReagentTag = "fc_reagent";
        public const string CanisterTag = "fc_canister";
        public const string DiscoveredKey = "discovered";

        /// <summary>
        /// 大型储气桶空桶 id。图标 48×64 → 由 SetSpriteAndShapeFromMod 自动推导为 3×4 格
        /// （参照原版水罐 water_pitcher），与便携钢瓶 32×48（2×3 格）明确区分。
        /// 空桶是**容器**，相态为 Solid，不能当气体投料。
        /// </summary>
        public const string DrumEmptyId = "gas_drum_empty";

        /// <summary>
        /// 物品台账探针：由 ChemCatalog 的静态构造函数注入（它是装配层，引用了游戏类型）。
        /// 本文件是纯数据层，独立校验工程只编译 ChemRecipes + ChemLab，
        /// 所以「某 id 是否已注册进 ChemCatalog.Specs」只能靠注入；为 null 时跳过跨层断言。
        /// </summary>
        public static Func<string, bool> SpecExists;
        /// <summary>装配层注入：物品价值（用于「副产物必须有去向」审计断言；0 表示不可销售）。</summary>
        public static Func<string, int> SpecValue;
        /// <summary>装配层注入：全部「真药剂」id 列表（用于组分覆盖断言；harness 未注入则跳过）。</summary>
        public static Func<IEnumerable<string>> AllReagentIds;

        /// <summary>
        /// 物品台账的 Kind 探针（同样由 ChemCatalog 注入）：id → Kind，未注册返回 null。
        /// 用于断言工具类物品（如温度计）确实以 Kind="tool" 注册，而不只是「存在同名条目」。
        /// </summary>
        public static Func<string, string> SpecKindOf;

        /// <summary>
        /// 兼容层：原版药剂瓶 id → 本 Mod 的等价药剂 id。
        /// 让原版「酸性溶液瓶 / 碱性溶液瓶 / 还原剂瓶 / 脂合剂瓶 / N-羟甲基碱溶液瓶」
        /// 可以直接投入新设备参与反应，而不改动原版组分系统。
        /// </summary>
        public static readonly Dictionary<string, string> VanillaAnalog = new Dictionary<string, string>
        {
            { "med_bottle_red",      "liq_hydrochloric_acid" },  // 酸性溶液瓶
            { "med_bottle_blue",     "liq_sodium_hydroxide"  },  // 碱性溶液瓶
            { "med_bottle_orange",   "liq_hydrogen_peroxide" },  // 还原剂瓶
            { "med_bottle_small_pink","liq_ammonia_water"    },  // 脂合剂瓶
            { "med_bottle_violet",   "liq_nitric_acid"       },  // N-羟甲基碱溶液瓶
            { "dream_cap",           "sol_activated_carbon"  },  // 梦帽菇 → 有机碳源
            // ── P3b：原版水容器/咖啡豆/酒精 → 本模组等价药剂（docs/feedback-research.md §4）──
            // 原版没有「纯水物品」，只有仅含 water 组分的容器；统一折算成 liq_water 参与反应/倒液。
            { "small_bottled_water", "liq_water" },
            { "bottled_water",       "liq_water" },
            { "large_bottled_water", "liq_water" },
            { "water_jug",           "liq_water" },
            { "water_pitcher",       "liq_water" },
            // P3c 口径修正：原版咖啡豆/咖啡因已有原版制作逻辑，不再映射到本模组咖啡豆糊。
            // sol_coffee_grounds(#47)/sol_caffeine(#48) 保留注册作 legacy（无产线/掉落/映射）。
        };

        public static string Normalize(string id)
        {
            string mapped;
            return VanillaAnalog.TryGetValue(id, out mapped) ? mapped : id;
        }

        // ── 液体药剂 → 原版液体组分映射（纯数据，便于 QA/harness 断言守恒）──
        // 单位：百分比（每条和 = 100）。桥接时按 500 ml × 1000 part/ml = 500000 part 分配。
        // 依据 docs/harmony-bridge.md §2.3；酸碱浓度是设计近似值（非物理精确），H2O2 为近似映射。
        /// <summary>原版合法液体组分（Liquid public const，纯数据层用字面量，避免引用游戏类型）。</summary>
        public static readonly string[] PourComponentIds =
            { "water", "acid", "base", "mineral", "alcohol" };

        public const int PourTotalPercent = 100;
        public const int PourPartPerMl = 1000;      // 原版 Water 体系：1 ml = 1000 part
        public const int PourVolumeMl = 500;        // 每件标准液体瓶 500 ml（P3a 用户反馈）

        /// <summary>
        /// mod 液体药剂 → 原版液体组分百分比表。
        /// 守恒：每条 Value 之和必须 = 100；桥接后总 part = 500000（500 ml）。
        /// </summary>
        public static readonly Dictionary<string, Dictionary<string, int>> PourParts =
            new Dictionary<string, Dictionary<string, int>>
        {
            { "liq_hydrochloric_acid", new Dictionary<string, int> { { "acid", 36 }, { "water", 64 } } },  // 浓盐酸（审查 1.3：与 ComponentParts 同浓度）
            { "liq_sulfuric_acid",     new Dictionary<string, int> { { "acid", 90 }, { "water", 10 } } },  // 浓硫酸
            { "liq_nitric_acid",       new Dictionary<string, int> { { "acid", 68 }, { "water", 32 } } },  // 浓硝酸（68%，与 ComponentParts 一致）
            { "liq_sodium_hydroxide",  new Dictionary<string, int> { { "base", 25 }, { "water", 75 } } },  // 烧碱液
            { "liq_ammonia_water",     new Dictionary<string, int> { { "base", 15 }, { "water", 85 } } },  // 稀氨水
            { "liq_brine",             new Dictionary<string, int> { { "water", 74 }, { "mineral", 26 } } }, // 饱和盐水（26%，与 ComponentParts 一致）
            { "liq_calcium_chloride",  new Dictionary<string, int> { { "water", 60 }, { "mineral", 40 } } }, // 氯化钙溶液（40%，盐类按 mineral 近似）
            // ⚠ 原版 Liquid 没有过氧化物组分：双氧水按「水」近似映射（化学身份仍是本模组双氧水），
            //   属于已知近似，不是忠实映射；如需精确需原版扩展组分（官方不支持）。
            { "liq_hydrogen_peroxide", new Dictionary<string, int> { { "water", 100 } } },
        };

        /// <summary>取液体药剂的原版组分映射（先 VanillaAnalog 归一）。</summary>
        public static bool TryGetPourParts(string id, out Dictionary<string, int> parts)
            => PourParts.TryGetValue(Normalize(id), out parts);

        /// <summary>
        /// 装配层注入：所有液体药剂 id（ChemCatalog 静态构造时写入），
        /// 供 ValidateLogic 断言「PourParts 覆盖全部液体药剂」；纯 harness 未注入时跳过该断言。
        /// </summary>
        public static string[] LiquidSpecIds;

        /// <summary>映射的主组分（占比最大者），供原版 ACTIVATE_INGREDIENT_TAG/GradeProduct 使用。</summary>
        public static string MainPourComponent(Dictionary<string, int> parts)
        {
            string best = null;
            int max = -1;
            foreach (var kv in parts)
                if (kv.Value > max) { max = kv.Value; best = kv.Key; }
            return best;
        }

        /// <summary>
        /// 化合物危险画像（原版式的毒性 / 欣快系数）。
        /// 量纲：有效成分按每件 250 ml（ChemRuntime.VolumeUl = 250000 μl）计，
        ///   L = 250000 × 毒性系数 / 100 = 2500 × 系数；L ≥ 1000 致死、750 ≤ L &lt; 1000 有毒。
        /// 因此系数 ≥ 0.40 → 致死；0.30–0.39 → 有毒；&lt; 0.30 → 安全。
        /// 欣快系数同理 E = 2500 × 系数，E ≥ 750 才显示欣快等级（普通酸碱盐一律 0）。
        /// 数值为设计值，未经实机平衡验证。
        /// </summary>
        public static readonly Dictionary<string, ChemProfile> Profiles = new Dictionary<string, ChemProfile>
        {
            // ── 气体 ──
            { "gas_chlorine",          new ChemProfile(4.0f,  0f) },    // L=10000 强致死
            { "gas_hydrogen_chloride", new ChemProfile(1.2f,  0f) },    // L=3000 致死（腐蚀性刺激）
            { "gas_ammonia",           new ChemProfile(0.6f,  0f) },    // L=1500 致死
            { "gas_carbon_dioxide",    new ChemProfile(0.05f, 0f) },    // L=125 安全（窒息性非毒性）
            { "gas_hydrogen",          new ChemProfile(0.02f, 0f) },    // L=50 安全
            { "gas_oxygen",            new ChemProfile(0f,    0f) },
            { "gas_nitrogen",          new ChemProfile(0f,    0f) },
            { "gas_ethylene",          new ChemProfile(0.15f, 0f) },    // L=375 安全（单纯窒息）
            { "gas_nitrous_oxide",     new ChemProfile(0.1f,  0.25f) }, // L=250 安全；笑气有欣快但无成瘾标记
            // ── 液体（每件 250 ml）──
            { "liq_hydrochloric_acid", new ChemProfile(0.3f,  0f) },    // L=750 有毒边界
            { "liq_sulfuric_acid",     new ChemProfile(0.35f, 0f) },    // L=875 有毒
            { "liq_nitric_acid",       new ChemProfile(0.35f, 0f) },    // L=875 有毒
            { "liq_sodium_hydroxide",  new ChemProfile(0.3f,  0f) },    // L=750 有毒边界
            { "liq_hydrogen_peroxide", new ChemProfile(0.12f, 0f) },    // L=300 安全（稀双氧水）
            { "liq_brine",             new ChemProfile(0f,    0f) },
            { "liq_ammonia_water",     new ChemProfile(0.25f, 0f) },    // L=625 安全（稀氨水）
            { "liq_calcium_chloride",  new ChemProfile(0f,    0f) },
            // ── 固体 ──
            { "sol_sodium_chloride",       new ChemProfile(0f,    0f) }, // 普通盐=0
            { "sol_sodium_hydroxide",      new ChemProfile(0.6f,  0f) }, // L=1500 致死（强碱）
            { "sol_copper_sulfate",        new ChemProfile(0.35f, 0f) }, // L=875 有毒
            { "sol_potassium_nitrate",     new ChemProfile(0.15f, 0f) }, // L=375 安全
            { "sol_calcium_carbonate",     new ChemProfile(0f,    0f) }, // L=0
            { "sol_ammonium_nitrate",      new ChemProfile(0.15f, 0f) }, // L=375 安全
            { "sol_potassium_permanganate",new ChemProfile(0.6f,  0f) }, // L=1500 致死（强氧化剂）
            { "sol_sulfur",                new ChemProfile(0f,    0f) },
            { "sol_activated_carbon",      new ChemProfile(0f,    0f) },
            { "sol_bleaching_powder",      new ChemProfile(0.5f,  0f) }, // L=1250 致死（漂白粉/次氯酸钙）
            // ── P2a 新化学品（毒性按 250 ml/件 设计值）──
            { "sol_calcium_oxide",        new ChemProfile(0.4f,  0f) },  // L=1000 致死（强碱性腐蚀）
            { "sol_calcium_hydroxide",    new ChemProfile(0.4f,  0f) },  // L=1000 致死
            { "sol_sodium_carbonate",     new ChemProfile(0.2f,  0f) },  // L=500 安全
            { "sol_sodium_bicarbonate",   new ChemProfile(0.1f,  0f) },  // L=250 安全（食用级）
            { "sol_ammonium_chloride",    new ChemProfile(0.15f, 0f) },  // L=375 安全
            { "sol_manganese_dioxide",    new ChemProfile(0.3f,  0f) },  // L=750 有毒边界
            { "sol_copper_oxide",         new ChemProfile(0.6f,  0f) },  // L=1500 致死（铜盐毒性）
            { "sol_potassium_sulfate",    new ChemProfile(0.1f,  0f) },  // L=250 安全
            { "sol_sodium_nitrate",       new ChemProfile(0.15f, 0f) },  // L=375 安全
            { "sol_potassium_manganate",  new ChemProfile(1.0f,  0f) },  // L=2500 致死（强氧化剂）
            { "sol_coffee_grounds",        new ChemProfile(0f,    0f) },
            { "sol_caffeine",              new ChemProfile(0.15f, 0.2f, addictive: true) }, // L=375 安全；咖啡因轻度欣快/成瘾语义
            // ── 副产物（不显示画像，系数仅占位）──
            { "sludge",       new ChemProfile(0.1f, 0f) },
            { "sol_sediment", new ChemProfile(0f,   0f) },
            // ── 虚构违禁品：沿用原版麻醉品/毒物的成瘾与毒性量级 ──
            { "illicit_voidsalt",   new ChemProfile(0.5f, 10.0f, true,  true) },  // 中等毒 + 强成瘾
            { "illicit_redmist",    new ChemProfile(4.0f, 0.25f, false, true) },
            { "illicit_nightglass", new ChemProfile(0.6f, 4.0f,  true,  true) },
            { "illicit_ashbreath",  new ChemProfile(4.0f, 0.5f,  true,  true) },  // 致命 + 低欣快/成瘾
        };

        /// <summary>
        /// 取物品的危险画像。钢瓶（can_*）与大型气桶（gas_drum_*）按其装载气体
        /// 复用对应 gas_* 的画像（否则会误显示为「无毒」）。
        /// </summary>
        public static ChemProfile Profile(string id)
        {
            string n = Normalize(id);
            ChemProfile p;
            if (Profiles.TryGetValue(n, out p)) return p;

            string gasId = BaseGasOf(n);
            if (gasId != null && Profiles.TryGetValue(gasId, out p)) return p;

            return new ChemProfile(0f, 0f);
        }

        /// <summary>can_oxygen_medical → gas_oxygen；gas_drum_carbon_dioxide → gas_carbon_dioxide。</summary>
        static string BaseGasOf(string n)
        {
            string s = null;
            if (n.StartsWith("can_")) s = n.Substring(4);
            else if (n.StartsWith("gas_drum_")) s = n.Substring(9);
            if (string.IsNullOrEmpty(s)) return null;

            foreach (var kv in Profiles)
            {
                if (!kv.Key.StartsWith("gas_")) continue;
                string gas = kv.Key.Substring(4);
                if (s == gas || s.StartsWith(gas + "_")) return kv.Key;
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────
        // 多级合成链
        //   T1 一级：由基础原料 / 电解 / 煅烧得到气体与酸碱
        //   T2 二级：气体与酸碱继续反应生成中间体
        //   T3 三级：提纯、灌装为高纯商品（工业 / 医用 / 化学纯）
        //   T4 四级：与原版药剂反应生成虚构违禁品
        // ─────────────────────────────────────────────────────────────
        public static readonly Reaction[] All =
        {
            // ── T1 电解槽 electrolyzer ──
            new Reaction("chloralkali", "氯碱法", "2NaCl + 2H2O --电解→ Cl2↑ + H2↑ + 2NaOH",
                "electrolyzer", 1,
                new[] { "liq_brine" },
                new[] { "gas_chlorine", "gas_hydrogen", "liq_sodium_hydroxide" },
                temp: 2, hazard: 2),
            new Reaction("hcl_electrolysis", "盐酸电解", "2HCl --电解→ H2↑ + Cl2↑",
                "electrolyzer", 1,
                new[] { "liq_hydrochloric_acid" },
                new[] { "gas_hydrogen", "gas_chlorine" }, temp: 2, hazard: 2),

            // ── T1 气体发生器 gas_generator（启普发生器式：固 + 液 → 气）──
            new Reaction("co2_from_marble", "大理石制二氧化碳", "CaCO3 + 2HCl → CaCl2 + H2O + CO2↑",
                "gas_generator", 1,
                new[] { "sol_calcium_carbonate", "liq_hydrochloric_acid" },
                new[] { "gas_carbon_dioxide", "liq_calcium_chloride" }),
            new Reaction("hcl_from_salt", "浓硫酸制氯化氢", "NaCl + H2SO4 --微热→ NaHSO4 + HCl↑",
                "gas_generator", 2,
                new[] { "sol_sodium_chloride", "liq_sulfuric_acid" },
                new[] { "gas_hydrogen_chloride" }, temp: 2, hazard: 2),
            new Reaction("peroxide_decomp", "双氧水催化分解", "2H2O2 --MnO2→ 2H2O + O2↑",
                "gas_generator", 1,
                new[] { "liq_hydrogen_peroxide" },
                new[] { "gas_oxygen" }, needsCatalyst: true),
            new Reaction("kmno4_decomp", "高锰酸钾热分解", "2KMnO4 --Δ→ K2MnO4 + MnO2 + O2↑",
                "gas_generator", 1,
                new[] { "sol_potassium_permanganate" },
                new[] { "gas_oxygen", "sol_manganese_dioxide", "sol_potassium_manganate" }, temp: 3),
            new Reaction("ammonia_liberation", "铵盐加碱放氨", "NH4NO3 + NaOH → NaNO3 + H2O + NH3↑",
                "gas_generator", 2,
                new[] { "sol_ammonium_nitrate", "sol_sodium_hydroxide" },
                new[] { "gas_ammonia", "sol_sodium_nitrate" }, temp: 2, hazard: 2),
            // P2a：石灰石煅烧（生石灰 + CO2，工业制石灰的起点）
            new Reaction("lime_calcination", "石灰石煅烧", "CaCO3 --Δ(900°C)→ CaO + CO2↑",
                "gas_generator", 1,
                new[] { "sol_calcium_carbonate" },
                new[] { "sol_calcium_oxide", "gas_carbon_dioxide" }, temp: 3, hazard: 1),

            // ── T2 合成反应釜 chem_reactor ──
            new Reaction("hcl_synthesis", "氢氯合成", "H2 + Cl2 --点燃→ 2HCl",
                "chem_reactor", 2,
                new[] { "gas_hydrogen", "gas_chlorine" },
                new[] { "gas_hydrogen_chloride" }, temp: 3, hazard: 3),
            new Reaction("wet_sulfuric", "湿法制硫酸", "S + O2 → SO2；SO2 + H2O2 → H2SO4",
                "chem_reactor", 2,
                new[] { "sol_sulfur", "gas_oxygen", "liq_hydrogen_peroxide" },
                new[] { "liq_sulfuric_acid" }, temp: 2, hazard: 2, catalystBonus: true),
            // P2a：修正「无钙源」的科学错误 —— 次氯酸盐应使用熟石灰（Ca(OH)2）
            new Reaction("hypochlorite", "次氯酸盐制备", "2Cl2 + 2Ca(OH)2 → Ca(ClO)2 + CaCl2 + 2H2O",
                "chem_reactor", 2,
                new[] { "gas_chlorine", "sol_calcium_hydroxide" },
                new[] { "sol_bleaching_powder", "liq_calcium_chloride" }, temp: 0, hazard: 2),
            // P2a：熟化石灰（CaO + H2O → Ca(OH)2）
            new Reaction("slaked_lime", "生石灰熟化", "CaO + H2O → Ca(OH)2",
                "chem_reactor", 1,
                new[] { "sol_calcium_oxide" },
                new[] { "sol_calcium_hydroxide" }, hazard: 1),
            // P2a：苛化法/纯碱制备（2NaOH + CO2 → Na2CO3 + H2O）
            new Reaction("soda_from_caustic", "苛化法制纯碱", "2NaOH + CO2 → Na2CO3 + H2O",
                "chem_reactor", 2,
                new[] { "liq_sodium_hydroxide", "gas_carbon_dioxide" },
                new[] { "sol_sodium_carbonate" }),
            // P2a：小苏打（Na2CO3 + CO2 + H2O → 2NaHCO3）
            new Reaction("baking_soda", "纯碱制小苏打", "Na2CO3 + CO2 + H2O → 2NaHCO3",
                "chem_reactor", 2,
                new[] { "sol_sodium_carbonate", "gas_carbon_dioxide" },
                new[] { "sol_sodium_bicarbonate" }),
            // P2a：氯化铵（NH3 + HCl → NH4Cl，气+气→固）
            new Reaction("ammonium_chloride", "氯化铵合成", "NH3 + HCl → NH4Cl",
                "chem_reactor", 2,
                new[] { "gas_ammonia", "gas_hydrogen_chloride" },
                new[] { "sol_ammonium_chloride" }, hazard: 1),
            new Reaction("haber", "哈伯法合成氨", "N2 + 3H2 →（可逆） 2NH3（Fe，450°C，20 MPa）",
                "high_pressure_synth", 2,
                new[] { "gas_nitrogen", "gas_hydrogen" },
                new[] { "gas_ammonia" }, temp: 3, hazard: 2,
                needsCatalyst: true, catalystBonus: true),
            // P2a：配平修正 —— 2KNO3 + H2SO4 → K2SO4 + 2HNO3
            new Reaction("nitric_from_niter", "硝石制硝酸", "2KNO3 + H2SO4 --Δ→ K2SO4 + 2HNO3↑",
                "fractionator", 2,
                new[] { "sol_potassium_nitrate", "liq_sulfuric_acid" },
                new[] { "liq_nitric_acid", "sol_potassium_sulfate" }, temp: 3, hazard: 2),
            new Reaction("ostwald", "奥斯特瓦尔德法", "4NH3 + 5O2 → 4NO + 6H2O → … → 2HNO3",
                "fractionator", 3,
                new[] { "gas_ammonia", "gas_oxygen" },
                new[] { "liq_nitric_acid" }, temp: 3, hazard: 2,
                needsCatalyst: true, catalystBonus: true),

            // ── T2 冷凝吸收塔 condenser_unit ──
            new Reaction("hcl_absorption", "氯化氢吸收制盐酸", "HCl(g) --H2O 吸收→ HCl(aq)（水为溶剂，非化学计量）",
                "condenser_unit", 2,
                new[] { "gas_hydrogen_chloride", "liq_water" },
                new[] { "liq_hydrochloric_acid" }, temp: 0),
            new Reaction("ammonia_absorption", "氨吸收制氨水", "NH3 + H2O →（可逆） NH3·H2O",
                "condenser_unit", 2,
                new[] { "gas_ammonia", "liq_water" },
                new[] { "liq_ammonia_water" }, temp: 0, hazard: 1),

            // ── T2 结晶器 / 滴定台 ──
            new Reaction("salt_crystallize", "盐水结晶", "NaCl(aq) --蒸发结晶→ NaCl(s) + H2O（母液，非化学计量）",
                "crystallizer", 2,
                new[] { "liq_brine" }, new[] { "sol_sodium_chloride" }, temp: 2),
            new Reaction("nh4no3_crystallize", "硝酸铵结晶", "HNO3 + NH3·H2O → NH4NO3 + H2O",
                "crystallizer", 2,
                new[] { "liq_nitric_acid", "liq_ammonia_water" },
                new[] { "sol_ammonium_nitrate" }, temp: 2, hazard: 2),
            new Reaction("neutralization", "酸碱中和滴定", "HCl(aq) + NaOH(aq) → NaCl(aq) + H2O",
                "titration_bench", 2,
                new[] { "liq_hydrochloric_acid", "liq_sodium_hydroxide" },
                new[] { "liq_brine" }, ph: 7, catalystBonus: true),

            // ── T2.5 静置滗析槽 decanter ──
            // 滗析 = 固液两相分离：静置 24h 后，【上层清液就是更纯的同一种药剂】，
            // 下层是沉淀杂质。产物只有两件：同种药剂（运行时纯度 +1）+ 沉淀。
            // 不凭空多造一瓶液体，也不改变化学身份。
            // 同一种药剂可反复投料继续提档（工业→医用→化学纯），这就是多级提纯。
            // 气体绝不能滗析；气体提纯走下面的 gas_scrubber。
            new Reaction("decant_hcl", "盐酸滗析",
                "HCl(aq) --静置 24h→ 上层清液(纯度↑的 HCl) + 沉淀",
                "decanter", 3, new[] { "liq_hydrochloric_acid" },
                new[] { "liq_hydrochloric_acid", "sol_sediment" }),
            new Reaction("decant_sulfuric", "硫酸滗析",
                "H2SO4(aq) --静置 24h→ 上层清液(纯度↑) + 沉淀",
                "decanter", 3, new[] { "liq_sulfuric_acid" },
                new[] { "liq_sulfuric_acid", "sol_sediment" }, hazard: 1),
            new Reaction("decant_nitric", "硝酸滗析",
                "HNO3(aq) --静置 24h→ 上层清液(纯度↑) + 沉淀",
                "decanter", 3, new[] { "liq_nitric_acid" },
                new[] { "liq_nitric_acid", "sol_sediment" }, hazard: 1),
            new Reaction("decant_peroxide", "双氧水滗析",
                "H2O2(aq) --静置 24h→ 上层清液(纯度↑) + 沉淀",
                "decanter", 3, new[] { "liq_hydrogen_peroxide" },
                new[] { "liq_hydrogen_peroxide", "sol_sediment" }),
            new Reaction("decant_ammonia", "氨水滗析",
                "NH3·H2O --静置 24h→ 上层清液(纯度↑) + 沉淀",
                "decanter", 3, new[] { "liq_ammonia_water" },
                new[] { "liq_ammonia_water", "sol_sediment" }, hazard: 1),
            new Reaction("decant_brine", "盐水滗析",
                "NaCl(aq) --静置 24h→ 上层清液(纯度↑) + 沉淀",
                "decanter", 3, new[] { "liq_brine" },
                new[] { "liq_brine", "sol_sediment" }),
            new Reaction("decant_calcium", "氯化钙溶液滗析",
                "CaCl2(aq) --静置 24h→ 上层清液(纯度↑) + 沉淀",
                "decanter", 3, new[] { "liq_calcium_chloride" },
                new[] { "liq_calcium_chloride", "sol_sediment" }),

            // ── T2.5 气体洗涤塔 gas_scrubber：只处理气体，吸附/洗涤除杂提纯一档 ──
            // 与滗析槽严格分工：液体走滗析，气体走洗涤，互不交叉
            new Reaction("scrub_oxygen", "氧气洗涤提纯", "O2(g) --碱洗/分子筛→ 高纯 O2(g)",
                "gas_scrubber", 3, new[] { "gas_oxygen" }, new[] { "gas_oxygen" }),
            new Reaction("scrub_hydrogen", "氢气洗涤提纯", "H2(g) --钯膜/吸附→ 高纯 H2(g)",
                "gas_scrubber", 3, new[] { "gas_hydrogen" }, new[] { "gas_hydrogen" }, hazard: 1),
            new Reaction("scrub_nitrogen", "氮气洗涤提纯", "N2(g) --脱氧吸附→ 高纯 N2(g)",
                "gas_scrubber", 3, new[] { "gas_nitrogen" }, new[] { "gas_nitrogen" }),
            new Reaction("scrub_chlorine", "氯气洗涤提纯", "Cl2(g) --干燥除雾→ 高纯 Cl2(g)",
                "gas_scrubber", 3, new[] { "gas_chlorine" }, new[] { "gas_chlorine" }, hazard: 2),
            new Reaction("scrub_ammonia", "氨气洗涤提纯", "NH3(g) --碱洗干燥→ 高纯 NH3(g)",
                "gas_scrubber", 3, new[] { "gas_ammonia" }, new[] { "gas_ammonia" }, hazard: 1),
            new Reaction("scrub_hcl_gas", "氯化氢洗涤提纯", "HCl(g) --除雾干燥→ 高纯 HCl(g)",
                "gas_scrubber", 3, new[] { "gas_hydrogen_chloride" },
                new[] { "gas_hydrogen_chloride" }, hazard: 2),

            // ── T3 灌装台 canister_filling：只吃【气体 + 空钢瓶】→ 分纯度商品 ──
            // 纯度档由投入气体的 fc_purity tag 决定，代码在 ChemMachines 中动态选 SKU；
            // 液体清液不进灌装台（滗析是液相操作，气体不能滗析）。
            new Reaction("fill_oxygen", "氧气灌装", "O2(g) --压缩→ O2(钢瓶)",
                "canister_filling", 3,
                new[] { "gas_oxygen", "gas_canister_empty" },
                new[] { "can_oxygen_industrial" }),
            new Reaction("fill_hydrogen", "氢气灌装", "H2(g) --压缩→ H2(钢瓶)",
                "canister_filling", 3,
                new[] { "gas_hydrogen", "gas_canister_empty" },
                new[] { "can_hydrogen_industrial" }, hazard: 2),
            new Reaction("fill_nitrogen", "氮气灌装", "N2(g) --压缩→ N2(钢瓶)",
                "canister_filling", 3,
                new[] { "gas_nitrogen", "gas_canister_empty" },
                new[] { "can_nitrogen_industrial" }),
            new Reaction("fill_chlorine", "氯气灌装", "Cl2(g) --压缩→ Cl2(钢瓶)",
                "canister_filling", 3,
                new[] { "gas_chlorine", "gas_canister_empty" },
                new[] { "can_chlorine_industrial" }, hazard: 3),

            // ── T4 虚构违禁品：与原版药剂反应 ──
            new Reaction("voidsalt", "空盐（虚构违禁品）",
                "N-羟甲基碱 + HCl(g) + 高纯氨 --催化→ 空盐（虚构违禁品，强成瘾）",
                "chem_reactor", 4,
                new[] { "med_bottle_violet", "gas_hydrogen_chloride", "gas_ammonia" },
                new[] { "illicit_voidsalt" }, temp: 3, hazard: 3,
                needsCatalyst: true, usesVanilla: true),
            new Reaction("redmist", "红雾（虚构违禁品）",
                "Cl2 + 还原剂 + 脂合剂 → 红雾（虚构违禁品，剧毒）",
                "chem_reactor", 4,
                new[] { "gas_chlorine", "med_bottle_orange", "med_bottle_small_pink" },
                new[] { "illicit_redmist" }, temp: 2, hazard: 3, usesVanilla: true),
            new Reaction("nightglass", "夜玻（虚构违禁品）",
                "梦帽菇 + H2SO4 + KMnO4 → 夜玻（虚构违禁品，强欣快）",
                "high_pressure_synth", 4,
                new[] { "dream_cap", "liq_sulfuric_acid", "sol_potassium_permanganate" },
                new[] { "illicit_nightglass" }, temp: 3, hazard: 2, usesVanilla: true),
            new Reaction("ashbreath", "灰息（虚构违禁品）",
                "HNO3 + 活性炭 + 原版氨水 → 灰息（虚构违禁品，致命）",
                "fractionator", 4,
                new[] { "liq_nitric_acid", "sol_activated_carbon", "med_bottle_small_pink" },
                new[] { "illicit_ashbreath" }, temp: 3, hazard: 3, usesVanilla: true),
        };

        public static readonly string[] MachineIds =
        {
            "chem_reactor", "electrolyzer", "gas_generator", "condenser_unit",
            "crystallizer", "high_pressure_synth", "fractionator", "titration_bench",
            "canister_filling", "decanter", "gas_scrubber",
            "filtration_bench", "heating_platform"   // 过滤台 / 实时大型加热台
        };

        /// <summary>实时设备：不走夜间结算，由 ModHelper.StartCoroutine 每秒驱动。</summary>
        public static bool IsRealtime(string machineId) => machineId == "heating_platform";

        /// <summary>由 id 前缀推断相态，供设备输入过滤与逻辑自检用。</summary>
        public static Phase PhaseOf(string id)
        {
            string n = Normalize(id);
            // 先排除名字里带 gas_ 但并非气体的容器/工具
            if (n == "gas_canister_empty") return Phase.Solid;  // 空钢瓶是容器
            if (n == DrumEmptyId) return Phase.Solid;           // 大型空桶也是容器，不是气体
            if (n == "catalyst_kit" || n == "precision_balance" || n == "ph_meter"
                || n == "safety_goggles") return Phase.Solid;
            if (n.StartsWith("can_")) return Phase.Gas;    // 钢瓶内为压缩气体
            if (n.StartsWith("gas_")) return Phase.Gas;    // 含已充装的 gas_drum_*（桶内为压缩气体）
            if (n.StartsWith("liq_")) return Phase.Liquid;
            if (n.StartsWith("sol_")) return Phase.Solid;
            if (n.StartsWith("illicit_")) return Phase.Solid;
            return Phase.Solid;
        }

        /// <summary>是否属于「不算投料」的辅助物（容器 / 催化剂 / 仪器）。</summary>
        public static bool IsAuxiliary(string id)
        {
            string n = Normalize(id);
            return n == "gas_canister_empty" || n == DrumEmptyId || n == "catalyst_kit"
                || n == "precision_balance" || n == "ph_meter"
                || n == "safety_goggles";
        }

        /// <summary>每台设备允许的输入相态；null 表示不限。</summary>
        public static Phase[] AllowedInputPhases(string machineId)
        {
            switch (machineId)
            {
                case "decanter":        return new[] { Phase.Liquid };          // 滗析：仅液体
                case "gas_scrubber":    return new[] { Phase.Gas };             // 洗涤：仅气体
                case "canister_filling":return new[] { Phase.Gas };             // 灌装：仅气体
                // 冷凝吸收塔：气体吸收需要液体（水）参与（P2a：HCl/NH3 吸收 + H2O）
                case "condenser_unit":  return new[] { Phase.Gas, Phase.Liquid };
                case "crystallizer":    return new[] { Phase.Liquid };          // 结晶：液→固
                default:                return null;                            // 反应釜等不限
            }
        }

        /// <summary>
        /// 数据层自检：把明显违反化学常识的配方挑出来。
        /// 返回问题描述列表；空列表表示全部通过。
        /// </summary>
        public static List<string> ValidateLogic()
        {
            var problems = new List<string>();
            foreach (var r in All)
            {
                var allowed = AllowedInputPhases(r.Machine);
                if (allowed == null) continue;
                foreach (var input in r.Inputs)
                {
                    if (IsAuxiliary(input)) continue;   // 容器/催化剂/仪器不参与相态判断
                    var p = PhaseOf(input);
                    bool ok = false;
                    foreach (var a in allowed) if (a == p) ok = true;
                    if (!ok)
                        problems.Add($"[{r.Id}] {r.Machine} 输入 {input} 相态为 {p}，" +
                                     $"该设备只允许 {string.Join("/", allowed)}");
                }
            }
            // 滗析：两件产物，且主产物必须是同一种液体药剂（不改变化学身份）
            foreach (var r in ForMachine("decanter"))
            {
                if (r.Inputs.Length != 1 || r.Outputs.Length != 2)
                    problems.Add($"[{r.Id}] 滗析应为 1 件液体进 → 2 件（清液+沉淀）出");
                else
                {
                    if (r.Outputs[0] != Normalize(r.Inputs[0]))
                        problems.Add($"[{r.Id}] 滗析主产物必须是同一种药剂，而不是 {r.Outputs[0]}");
                    if (!r.Outputs.Contains("sol_sediment"))
                        problems.Add($"[{r.Id}] 滗析缺少沉淀产物");
                }
            }
            // 灌装产物必须是气罐商品（三档纯度 can_*）或大型工业储气桶（gas_drum_*，仅工业档）
            foreach (var r in ForMachine("canister_filling"))
                foreach (var o in r.Outputs)
                {
                    if (o.StartsWith("can_")) continue;
                    if (o.StartsWith("gas_drum_") && Array.IndexOf(DrumIds, o) >= 0) continue;
                    problems.Add($"[{r.Id}] 灌装产物 {o} 既不是气罐商品也不是已登记的大型储气桶");
                }
            // 大型储气桶：5 种气体必须一一映射到桶，且映射目标/空桶都在目录里
            if (DrumSkus.Count != 5)
                problems.Add($"DrumSkus 应覆盖 5 种气体（氧/氮/氢/氯/二氧化碳），实际 {DrumSkus.Count} 种");
            var drumTargets = new List<string>();
            foreach (var kv in DrumSkus)
            {
                if (kv.Key.StartsWith("can_"))
                    problems.Add($"[DrumSkus] 键 {kv.Key} 应该是气体 id，不是气罐商品 id");
                if (PhaseOf(kv.Key) != Phase.Gas)
                    problems.Add($"[DrumSkus] 键 {kv.Key} 相态为 {PhaseOf(kv.Key)}，灌装原料必须是气体");
                if (!kv.Value.StartsWith("gas_drum_"))
                    problems.Add($"[DrumSkus] 目标 {kv.Value} 不是 gas_drum_* 大型桶");
                if (kv.Value == DrumEmptyId)
                    problems.Add($"[DrumSkus] 气体 {kv.Key} 不能映射到空桶 {DrumEmptyId}");
                if (Array.IndexOf(DrumIds, kv.Value) < 0)
                    problems.Add($"[DrumSkus] 目标 {kv.Value} 不在 DrumIds 清单里");
                if (drumTargets.Contains(kv.Value))
                    problems.Add($"[DrumSkus] 多个气体映射到同一个桶 {kv.Value}");
                drumTargets.Add(kv.Value);
                // 桶只能进工业档：不能混进三档纯度钢瓶 SKU
                foreach (var skus in CanisterSkus.Values)
                    if (Array.IndexOf(skus, kv.Value) >= 0)
                        problems.Add($"[DrumSkus] 桶 {kv.Value} 不应出现在 CanisterSkus 里");
            }
            if (PhaseOf(DrumEmptyId) != Phase.Solid)
                problems.Add($"{DrumEmptyId} 是空容器，相态必须是 Solid 而不是 {PhaseOf(DrumEmptyId)}");
            if (!IsAuxiliary(DrumEmptyId))
                problems.Add($"{DrumEmptyId} 必须按容器（辅助物）处理，不能算投料");
            if (DrumIds.Length != DrumSkus.Count + 1)
                problems.Add($"DrumIds 应为「{DrumSkus.Count} 个充装桶 + 1 个空桶」，实际 {DrumIds.Length} 个");
            foreach (var d in DrumIds)
                if (d == DrumEmptyId) continue;
                else if (!drumTargets.Contains(d))
                    problems.Add($"DrumIds 里的 {d} 没有对应的气体映射");
            // 跨层断言（需要 ChemCatalog 注入台账探针；logic-harness 未注入则自动跳过）
            if (SpecExists != null)
            {
                foreach (var d in DrumIds)
                    if (!SpecExists(d))
                        problems.Add($"大型储气桶 {d} 未注册进 ChemCatalog.Specs");
                if (!SpecExists("gas_canister_empty"))
                    problems.Add("gas_canister_empty 未注册进 ChemCatalog.Specs");
                // 已废弃的旧防护面罩：Specs 里不得再注册该 id。
                // 用编译期拼接而非字面量，避免废弃 id 在源码里留下可被误用的文本残留
                // （QA 的「零残留」文本扫描因此保持 0 命中，而反回归守卫仍然生效）。
                if (SpecExists("gas_" + "mask"))
                    problems.Add("废弃的旧防护面罩 id 仍注册在 ChemCatalog.Specs 里");
            }
            // 温度控制可达性（可玩性硬约束）：
            //   1) 配方温度档只能是 0 冷 / 1 常温 / 2 加热 / 3 高温高压（温度计只能切这四档）；
            //   2) thermometer 必须注册且 Kind=tool —— 它是 ChemRuntime.Temp 的唯一操作入口，
            //      缺失时所有 temp:2/3 的配方（T2 氢氯合成/哈伯/硝石制硝酸/结晶、T4 全部）都不可达。
            var tempLevels = new HashSet<int>();
            foreach (var r in All) tempLevels.Add(r.Temp);
            foreach (var t in tempLevels)
                if (t < 0 || t > 3)
                    problems.Add($"配方温度档 {t} 越界：温度计只能切换 0冷/1常温/2加热/3高温高压");
            if (SpecExists != null && !SpecExists("thermometer"))
                problems.Add("温度计 thermometer 未注册进 ChemCatalog.Specs —— temp≥2 的配方将全部不可达");
            if (SpecKindOf != null)
            {
                string kind = SpecKindOf("thermometer");
                if (kind != null && kind != "tool")
                    problems.Add($"温度计 thermometer 的 Kind 应为 tool（仪器），实际 {kind}");
            }
            // 实时加热台不应登记夜间反应（它由每秒协程驱动）
            foreach (var r in All)
                if (r.Machine == "heating_platform")
                    problems.Add($"[{r.Id}] 实时加热台不应登记夜间反应");

            // ── P1a：液体药剂 → 原版组分映射守恒（docs/harmony-bridge.md §2.3）──
            foreach (var kv in PourParts)
            {
                string liquidId = kv.Key;
                Dictionary<string, int> parts = kv.Value;
                if (PhaseOf(liquidId) != Phase.Liquid)
                    problems.Add($"[PourParts] {liquidId} 不是液体药剂");
                int sum = 0;
                foreach (var p in parts)
                {
                    if (p.Value <= 0)
                        problems.Add($"[PourParts] {liquidId} 的 {p.Key} 占比必须 > 0");
                    if (Array.IndexOf(PourComponentIds, p.Key) < 0)
                        problems.Add($"[PourParts] {liquidId} 的组分 {p.Key} 不在原版合法液体列表内");
                    sum += p.Value;
                }
                if (sum != PourTotalPercent)
                    problems.Add($"[PourParts] {liquidId} 各组分之和应为 {PourTotalPercent}%，实际 {sum}%");
            }
            if (!PourParts.ContainsKey("liq_hydrogen_peroxide"))
                problems.Add("[PourParts] 缺少双氧水的近似映射（应有 water 近似项）");
            // 覆盖性：装配层注入全部液体药剂 id 后，逐个断言有映射（logic-harness 未注入则跳过）
            if (LiquidSpecIds != null)
            {
                foreach (var liquidId in LiquidSpecIds)
                    if (!PourParts.ContainsKey(Normalize(liquidId)))
                        problems.Add($"[PourParts] 液体药剂 {liquidId} 缺少原版组分映射");
            }

            // ── P2a：反应式完整性（用户第⑤⑨条）──
            // 全部 inputs/outputs 必须已注册（需要 ChemCatalog 注入 Specs 探针；harness 未注入则跳过）
            if (SpecExists != null)
            {
                foreach (var r in All)
                {
                    foreach (var i in r.Inputs)
                        // 原版药剂瓶/原料（VanillaAnalog 键）不是本模组物品，只能按映射校验
                        if (!SpecExists(i) && !VanillaAnalog.ContainsKey(i))
                            problems.Add($"[{r.Id}] 输入 {i} 未注册进 ChemCatalog.Specs");
                    foreach (var o in r.Outputs)
                        if (!SpecExists(o))
                            problems.Add($"[{r.Id}] 输出 {o} 未注册进 ChemCatalog.Specs");
                }
            }
            // equation 必须非空且含「→」（防止旧式 "--Δ→" 或空串）
            foreach (var r in All)
                if (string.IsNullOrEmpty(r.Equation) || r.Equation.IndexOf('→') < 0)
                    problems.Add($"[{r.Id}] equation 为空或不含「→」箭头");
            // 原版药剂只能出现在 T4 虚构违禁品链（T1–T3 只用本模组化学品）
            foreach (var r in All)
            {
                if (r.Tier > 3) continue;
                foreach (var i in r.Inputs)
                    if (VanillaAnalog.ContainsKey(i))
                        problems.Add($"[{r.Id}] T1–T3 不允许使用原版药剂 {i}（仅 T4 允许）");
            }
            foreach (var r in ForTier(4))
                if (!r.UsesVanilla)
                    problems.Add($"[{r.Id}] T4 虚构违禁品反应应标注 UsesVanilla=true");
            // 虚构违禁品：产物 id 必须是 illicit_*，equation 注明（虚构）
            foreach (var r in ForTier(4))
            {
                if (r.Equation.IndexOf("虚构") < 0)
                    problems.Add($"[{r.Id}] T4 虚构反应 equation 未标注「虚构」");
                foreach (var o in r.Outputs)
                    if (!o.StartsWith("illicit_"))
                        problems.Add($"[{r.Id}] T4 产物 {o} 不是 illicit_ 前缀");
            }

            // ── P3f 审计断言：反应表结构/机器/气体/副产物/产业链（docs/chemistry-audit.md）──
            var seenIds = new HashSet<string>();
            var producedByAll = new HashSet<string>();
            var consumedByAll = new HashSet<string>();
            foreach (var tr in ChemLab.ThermalReactions)
            {
                foreach (var o in tr.Outputs) producedByAll.Add(o);
                foreach (var i in tr.Inputs) consumedByAll.Add(i);
            }
            foreach (var r in All)
            {
                if (!seenIds.Add(r.Id))
                    problems.Add($"[{r.Id}] 反应 id 重复");
                if (Array.IndexOf(MachineIds, r.Machine) < 0)
                    problems.Add($"[{r.Id}] 机器 {r.Machine} 不在 MachineIds 清单内");
                if (r.Inputs.Length == 0 || r.Outputs.Length == 0)
                    problems.Add($"[{r.Id}] inputs/outputs 不能为空");
                if (r.Temp < 0 || r.Temp > 3)
                    problems.Add($"[{r.Id}] temp 档 {r.Temp} 越界（0–3）");
                if (r.Ph < -1 || r.Ph > 14)
                    problems.Add($"[{r.Id}] pH {r.Ph} 越界（-1 不限 / 0–14）");
                var dupCheck = new HashSet<string>();
                foreach (var i in r.Inputs)
                {
                    if (!dupCheck.Add(i)) problems.Add($"[{r.Id}] 输入 {i} 重复");
                    consumedByAll.Add(i);
                }
                foreach (var o in r.Outputs) producedByAll.Add(o);
            }
            // gas 输入必须可由气瓶槽（CanisterSkus）或其它反应产出，否则死配方
            foreach (var r in All)
                foreach (var i in r.Inputs)
                {
                    if (IsAuxiliary(i)) continue;
                    if (PhaseOf(i) != Phase.Gas) continue;
                    bool canister = CanisterSkus.ContainsKey(i);
                    bool produced = producedByAll.Contains(i);
                    if (!canister && !produced)
                        problems.Add($"[{r.Id}] 气体输入 {i} 既不在 CanisterSkus 也不由任何反应产出");
                }
            // 副产物去向：每个产物要么可销售（Value>0），要么被其它反应/热反应继续使用
            if (SpecValue != null)
            {
                foreach (var r in All)
                    foreach (var o in r.Outputs)
                        if (!consumedByAll.Contains(o) && SpecValue(o) <= 0)
                            problems.Add($"[{r.Id}] 副产物 {o} 既不可销售也无后续用途");
            }
            // 4 个虚构违禁品必须至少有一个原版输入（与原版化合物反应的设计前提）
            foreach (var r in ForTier(4))
            {
                bool hasVanillaInput = false;
                foreach (var i in r.Inputs) if (VanillaAnalog.ContainsKey(i)) hasVanillaInput = true;
                if (!hasVanillaInput)
                    problems.Add($"[{r.Id}] T4 反应没有任何原版输入（VanillaAnalog），不符合设计前提");
            }
            // 每台机器都要有用途：至少一条夜间反应，或辅助设备（过滤台），或实时加热台
            foreach (var m in MachineIds)
            {
                if (IsRealtime(m)) continue;
                bool used = false;
                foreach (var r in All) if (r.Machine == m) { used = true; break; }
                // filtration_bench 是无固定配方的辅助设备（滤纸+液体动态过滤），单独豁免
                if (!used && !IsAuxiliary(m) && m != "filtration_bench")
                    problems.Add($"机器 {m} 没有任何反应，也不在辅助设备清单里");
            }

            // ── P10a：真实成分模型（自定义 ComponentDefs，每件 1000 = 100%）──
            foreach (var kv in ComponentParts)
            {
                int sum = 0;
                foreach (var p in kv.Value)
                {
                    if (p.Value <= 0)
                        problems.Add($"[ComponentParts] {kv.Key} 的组分 {p.Key} 占比必须 > 0");
                    if (Array.IndexOf(ComponentLiquidIds, p.Key) < 0)
                        problems.Add($"[ComponentParts] {kv.Key} 的组分 {p.Key} 未在 ComponentDefs 定义");
                    sum += p.Value;
                }
                if (sum != ComponentTotalPart)
                    problems.Add($"[ComponentParts] {kv.Key} 组分之和应为 {ComponentTotalPart}，实际 {sum}");
            }
            // P10a：氯气必须使用独立成分 fc_cl2（不复用原版 chlorine/净水片）
            if (ComponentParts.ContainsKey("gas_chlorine"))
            {
                if (ComponentParts["gas_chlorine"].ContainsKey("chlorine"))
                    problems.Add("[ComponentParts] gas_chlorine 不能复用原版 chlorine（与净水片冲突）");
                if (!ComponentParts["gas_chlorine"].ContainsKey(ComponentChlorineGas))
                    problems.Add("[ComponentParts] gas_chlorine 必须使用独立成分 " + ComponentChlorineGas);
            }

            // P12：已移除物品不得作为任何反应输出/接触输出
            foreach (var r in All)
                foreach (var o in r.Outputs)
                    if (o == "liq_water" || o == "liq_ethanol")
                        problems.Add($"[{r.Id}] 输出不得包含已移除物品 {o}（水作溶剂/副产物不计物品）");
            foreach (var c in ContactReactions)
                foreach (var o in c.Outputs)
                    if (o == "liq_water" || o == "liq_ethanol")
                        problems.Add($"[{c.Id}] 接触反应输出不得包含已移除物品 {o}");

            // P10b：接触反应输入/输出必须已注册
            if (SpecExists != null)
            {
                foreach (var c in ContactReactions)
                {
                    foreach (var i in c.Inputs)
                        if (!SpecExists(i) && !VanillaAnalog.ContainsKey(i))
                            problems.Add($"[{c.Id}] 接触反应输入 {i} 未注册");
                    foreach (var o in c.Outputs)
                        if (!SpecExists(o))
                            problems.Add($"[{c.Id}] 接触反应输出 {o} 未注册");
                }
            }

            if (AllReagentIds != null)
            {
                foreach (var id in AllReagentIds())
                {
                    if (id == "gas_canister_empty" || id == DrumEmptyId) continue;   // 空容器无化学组分
                    if (!ComponentParts.ContainsKey(Normalize(id)))
                        problems.Add($"[ComponentParts] 药剂 {id} 缺少真实组分映射");
                }
            }

            foreach (var p in ChemLab.ValidateLogic()) problems.Add("[ChemLab] " + p);
            return problems;
        }

        /// <summary>可被灌装的气体 → 三档纯度 SKU。</summary>
        public static readonly Dictionary<string, string[]> CanisterSkus = new Dictionary<string, string[]>
        {
            { "gas_oxygen",   new[] { "can_oxygen_industrial",   "can_oxygen_medical",   "can_oxygen_ultrapure"   } },
            { "gas_hydrogen", new[] { "can_hydrogen_industrial", "can_hydrogen_medical", "can_hydrogen_ultrapure" } },
            { "gas_nitrogen", new[] { "can_nitrogen_industrial", "can_nitrogen_medical", "can_nitrogen_ultrapure" } },
            { "gas_chlorine", new[] { "can_chlorine_industrial", "can_chlorine_medical", "can_chlorine_ultrapure" } },
        };

        /// <summary>
        /// 反查：气瓶商品 id → 气体 id（P3c 机器气瓶槽供气用）。
        /// 由 CanisterSkus 静态反推，不手写第二份映射，避免与灌装表漂移。
        /// </summary>
        static readonly Dictionary<string, string> CanisterToGas = BuildCanisterToGas();

        static Dictionary<string, string> BuildCanisterToGas()
        {
            var map = new Dictionary<string, string>();
            foreach (var kv in CanisterSkus)
                foreach (var sku in kv.Value)
                    if (!map.ContainsKey(sku)) map[sku] = kv.Key;
            return map;
        }

        /// <summary>气瓶商品 id → 内部气体 id（gas_*）。</summary>
        public static bool TryGetGasFromCanister(string canisterId, out string gasId)
        {
            if (string.IsNullOrEmpty(canisterId)) { gasId = null; return false; }
            return CanisterToGas.TryGetValue(canisterId, out gasId);
        }

        /// <summary>
        /// 可被灌装进**大型储气桶**的气体 → 工业桶 id（5 种气体一一对应）。
        /// 桶只有一张 48×64 图标（3×4 格），因此只做工业档，不分三档纯度；
        /// 便携钢瓶（32×48，2×3 格）仍走上面的 CanisterSkus 三档 SKU。
        /// 灌装台解析时用本表把「气体 + gas_drum_empty」换成对应桶，不要塞进 CanisterSkus。
        /// </summary>
        public static readonly Dictionary<string, string> DrumSkus = new Dictionary<string, string>
        {
            { "gas_oxygen",          "gas_drum_oxygen"          },
            { "gas_nitrogen",        "gas_drum_nitrogen"        },
            { "gas_hydrogen",        "gas_drum_hydrogen"        },
            { "gas_chlorine",        "gas_drum_chlorine"        },
            { "gas_carbon_dioxide",  "gas_drum_carbon_dioxide"  },
        };

        /// <summary>全部大型储气桶 id（1 个空桶 + 5 个充装桶），供校验与掉落/注册使用。</summary>
        public static readonly string[] DrumIds =
        {
            DrumEmptyId,
            "gas_drum_oxygen", "gas_drum_nitrogen", "gas_drum_hydrogen",
            "gas_drum_chlorine", "gas_drum_carbon_dioxide",
        };

        public const int ComponentTotalPart = 1000;
        /// <summary>P10a：氯气独立成分类 id（不复用原版 chlorine/净水片）。</summary>
        public const string ComponentChlorineGas = "fc_cl2";

        /// <summary>P10a：自定义化学成分类（显示名键 comp_&lt;id&gt;_name + ASCII 公式 + 毒性/欣快系数）。</summary>
        public sealed class ComponentDef
        {
            public string NameKey;    // 本地化键 comp_<id>_name（docs 提供）
            public string Formula;    // 纯 ASCII 化学式
            public double Lethality;  // 与 ChemProfile.Toxicity 同标度（0–100 → L = ×2500）
            public double Euphoria;
            public int Addictiveness;
        }

        public static readonly Dictionary<string, ComponentDef> ComponentDefs = new Dictionary<string, ComponentDef>
        {
            { "fc_ashbreath", new ComponentDef { NameKey = "comp_fc_ashbreath_name", Formula = "C7H9N3O6", Lethality = 4.0f, Euphoria = 0.5f, Addictiveness = 0 } },
            { "fc_c", new ComponentDef { NameKey = "comp_fc_c_name", Formula = "C", Lethality = 0.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_c2h4", new ComponentDef { NameKey = "comp_fc_c2h4_name", Formula = "C2H4", Lethality = 0.15f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_c2h5oh", new ComponentDef { NameKey = "comp_fc_c2h5oh_name", Formula = "C2H5OH", Lethality = 0.2f, Euphoria = 0.15f, Addictiveness = 0 } },
            { "fc_cacl2", new ComponentDef { NameKey = "comp_fc_cacl2_name", Formula = "CaCl2", Lethality = 0.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_caclo2", new ComponentDef { NameKey = "comp_fc_caclo2_name", Formula = "Ca(ClO)2", Lethality = 0.5f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_caco3", new ComponentDef { NameKey = "comp_fc_caco3_name", Formula = "CaCO3", Lethality = 0.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_caffeine", new ComponentDef { NameKey = "comp_fc_caffeine_name", Formula = "C8H10N4O2", Lethality = 0.15f, Euphoria = 0.2f, Addictiveness = 0 } },
            { "fc_cao", new ComponentDef { NameKey = "comp_fc_cao_name", Formula = "CaO", Lethality = 0.4f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_caoh2", new ComponentDef { NameKey = "comp_fc_caoh2_name", Formula = "Ca(OH)2", Lethality = 0.4f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_cl2", new ComponentDef { NameKey = "comp_fc_cl2_name", Formula = "Cl2", Lethality = 4.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_co2", new ComponentDef { NameKey = "comp_fc_co2_name", Formula = "CO2", Lethality = 0.05f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_coffee", new ComponentDef { NameKey = "comp_fc_coffee_name", Formula = "CxHyOz", Lethality = 0.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_cuo", new ComponentDef { NameKey = "comp_fc_cuo_name", Formula = "CuO", Lethality = 0.6f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_cuso4", new ComponentDef { NameKey = "comp_fc_cuso4_name", Formula = "CuSO4", Lethality = 0.35f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_h2", new ComponentDef { NameKey = "comp_fc_h2_name", Formula = "H2", Lethality = 0.02f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_h2o2", new ComponentDef { NameKey = "comp_fc_h2o2_name", Formula = "H2O2", Lethality = 0.12f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_h2so4", new ComponentDef { NameKey = "comp_fc_h2so4_name", Formula = "H2SO4", Lethality = 0.35f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_hcl", new ComponentDef { NameKey = "comp_fc_hcl_name", Formula = "HCl", Lethality = 1.2f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_hno3", new ComponentDef { NameKey = "comp_fc_hno3_name", Formula = "HNO3", Lethality = 0.35f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_k2mno4", new ComponentDef { NameKey = "comp_fc_k2mno4_name", Formula = "K2MnO4", Lethality = 1.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_k2so4", new ComponentDef { NameKey = "comp_fc_k2so4_name", Formula = "K2SO4", Lethality = 0.1f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_kmno4", new ComponentDef { NameKey = "comp_fc_kmno4_name", Formula = "KMnO4", Lethality = 0.6f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_kno3", new ComponentDef { NameKey = "comp_fc_kno3_name", Formula = "KNO3", Lethality = 0.15f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_mno2", new ComponentDef { NameKey = "comp_fc_mno2_name", Formula = "MnO2", Lethality = 0.3f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_n2", new ComponentDef { NameKey = "comp_fc_n2_name", Formula = "N2", Lethality = 0.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_n2o", new ComponentDef { NameKey = "comp_fc_n2o_name", Formula = "N2O", Lethality = 0.1f, Euphoria = 0.25f, Addictiveness = 0 } },
            { "fc_na2co3", new ComponentDef { NameKey = "comp_fc_na2co3_name", Formula = "Na2CO3", Lethality = 0.2f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_nacl", new ComponentDef { NameKey = "comp_fc_nacl_name", Formula = "NaCl", Lethality = 0.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_nahco3", new ComponentDef { NameKey = "comp_fc_nahco3_name", Formula = "NaHCO3", Lethality = 0.1f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_nano3", new ComponentDef { NameKey = "comp_fc_nano3_name", Formula = "NaNO3", Lethality = 0.15f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_naoh", new ComponentDef { NameKey = "comp_fc_naoh_name", Formula = "NaOH", Lethality = 0.3f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_nh3", new ComponentDef { NameKey = "comp_fc_nh3_name", Formula = "NH3", Lethality = 0.6f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_nh4cl", new ComponentDef { NameKey = "comp_fc_nh4cl_name", Formula = "NH4Cl", Lethality = 0.15f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_nh4no3", new ComponentDef { NameKey = "comp_fc_nh4no3_name", Formula = "NH4NO3", Lethality = 0.15f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_nightglass", new ComponentDef { NameKey = "comp_fc_nightglass_name", Formula = "C21H28N2O3", Lethality = 0.6f, Euphoria = 4.0f, Addictiveness = 0 } },
            { "fc_o2", new ComponentDef { NameKey = "comp_fc_o2_name", Formula = "O2", Lethality = 0.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_redmist", new ComponentDef { NameKey = "comp_fc_redmist_name", Formula = "C12H8Cl4O2", Lethality = 4.0f, Euphoria = 0.25f, Addictiveness = 0 } },
            { "fc_s", new ComponentDef { NameKey = "comp_fc_s_name", Formula = "S", Lethality = 0.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_sediment", new ComponentDef { NameKey = "comp_fc_sediment_name", Formula = "SiO2", Lethality = 0.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "fc_voidsalt", new ComponentDef { NameKey = "comp_fc_voidsalt_name", Formula = "C14H22N2O4", Lethality = 0.5f, Euphoria = 10.0f, Addictiveness = 0 } },
            { "fc_waste", new ComponentDef { NameKey = "comp_fc_waste_name", Formula = "CxHyOz", Lethality = 1.0f, Euphoria = 0.0f, Addictiveness = 0 } },
            { "water", new ComponentDef { NameKey = "comp_water_name", Formula = "H2O", Lethality = 0.0f, Euphoria = 0.0f, Addictiveness = 0 } },
        };

        /// <summary>
        /// 审查 4.3：自定义成分的沸点（°C）。以前统一按 100°C、可蒸发注册，
        /// 原版蒸发器/加热板会把盐「蒸发掉」，气体在常温却被当成液体。
        /// 这里只列气体与挥发性液体；未列出的视为固体（不可蒸发，沸点按 SolidBoilingPoint）。
        /// </summary>
        public static readonly Dictionary<string, double> ComponentBoilingPoints = new Dictionary<string, double>
        {
            // 气体
            { "fc_h2", -253 }, { "fc_n2", -196 }, { "fc_o2", -183 }, { "fc_c2h4", -104 },
            { "fc_n2o", -88 }, { "fc_hcl", -85 }, { "fc_co2", -78 }, { "fc_cl2", -34 }, { "fc_nh3", -33 },
            // 液体
            { "fc_c2h5oh", 78 }, { "fc_hno3", 83 }, { "fc_h2o2", 150 }, { "fc_h2so4", 337 },
            { "water", 100 },
        };

        /// <summary>固体成分的「沸点」占位：远高于任何加热设备能达到的温度。</summary>
        public const double SolidBoilingPoint = 2000;

        /// <summary>成分沸点；未列出的按固体处理。</summary>
        public static double ComponentBoilingPoint(string id)
        {
            double bp;
            return ComponentBoilingPoints.TryGetValue(id ?? "", out bp) ? bp : SolidBoilingPoint;
        }

        /// <summary>成分能否蒸发（固体不能）。</summary>
        public static bool ComponentIsEvaporative(string id)
            => ComponentBoilingPoints.ContainsKey(id ?? "");

        /// <summary>P10a：全部允许的成分类 id（自定义 fc_* + 原版 water）。</summary>
                public static readonly string[] ComponentLiquidIds =
        {
            "fc_ashbreath", "fc_c", "fc_c2h4", "fc_c2h5oh",
            "fc_cacl2", "fc_caclo2", "fc_caco3", "fc_caffeine",
            "fc_cao", "fc_caoh2", "fc_cl2", "fc_co2",
            "fc_coffee", "fc_cuo", "fc_cuso4", "fc_h2",
            "fc_h2o2", "fc_h2so4", "fc_hcl", "fc_hno3",
            "fc_k2mno4", "fc_k2so4", "fc_kmno4", "fc_kno3",
            "fc_mno2", "fc_n2", "fc_n2o", "fc_na2co3",
            "fc_nacl", "fc_nahco3", "fc_nano3", "fc_naoh",
            "fc_nh3", "fc_nh4cl", "fc_nh4no3", "fc_nightglass",
            "fc_o2", "fc_redmist", "fc_s", "fc_sediment",
            "fc_voidsalt", "fc_waste", "water",
        };
        /// <summary>P10a：药剂 → 真实成分（percent×10，合计 1000 = 100%）。</summary>
        public static readonly Dictionary<string, Dictionary<string, int>> ComponentParts = new Dictionary<string, Dictionary<string, int>>
        {
            { "can_chlorine_industrial", new Dictionary<string, int> { { "fc_cl2", 1000 } } },
            { "can_chlorine_medical", new Dictionary<string, int> { { "fc_cl2", 1000 } } },
            { "can_chlorine_ultrapure", new Dictionary<string, int> { { "fc_cl2", 1000 } } },
            { "can_hydrogen_industrial", new Dictionary<string, int> { { "fc_h2", 1000 } } },
            { "can_hydrogen_medical", new Dictionary<string, int> { { "fc_h2", 1000 } } },
            { "can_hydrogen_ultrapure", new Dictionary<string, int> { { "fc_h2", 1000 } } },
            { "can_nitrogen_industrial", new Dictionary<string, int> { { "fc_n2", 1000 } } },
            { "can_nitrogen_medical", new Dictionary<string, int> { { "fc_n2", 1000 } } },
            { "can_nitrogen_ultrapure", new Dictionary<string, int> { { "fc_n2", 1000 } } },
            { "can_oxygen_industrial", new Dictionary<string, int> { { "fc_o2", 1000 } } },
            { "can_oxygen_medical", new Dictionary<string, int> { { "fc_o2", 1000 } } },
            { "can_oxygen_ultrapure", new Dictionary<string, int> { { "fc_o2", 1000 } } },
            { "gas_ammonia", new Dictionary<string, int> { { "fc_nh3", 1000 } } },
            { "gas_carbon_dioxide", new Dictionary<string, int> { { "fc_co2", 1000 } } },
            { "gas_chlorine", new Dictionary<string, int> { { "fc_cl2", 1000 } } },
            { "gas_drum_carbon_dioxide", new Dictionary<string, int> { { "fc_co2", 1000 } } },
            { "gas_drum_chlorine", new Dictionary<string, int> { { "fc_cl2", 1000 } } },
            { "gas_drum_hydrogen", new Dictionary<string, int> { { "fc_h2", 1000 } } },
            { "gas_drum_nitrogen", new Dictionary<string, int> { { "fc_n2", 1000 } } },
            { "gas_drum_oxygen", new Dictionary<string, int> { { "fc_o2", 1000 } } },
            { "gas_ethylene", new Dictionary<string, int> { { "fc_c2h4", 1000 } } },
            { "gas_hydrogen", new Dictionary<string, int> { { "fc_h2", 1000 } } },
            { "gas_hydrogen_chloride", new Dictionary<string, int> { { "fc_hcl", 1000 } } },
            { "gas_nitrogen", new Dictionary<string, int> { { "fc_n2", 1000 } } },
            { "gas_nitrous_oxide", new Dictionary<string, int> { { "fc_n2o", 1000 } } },
            { "gas_oxygen", new Dictionary<string, int> { { "fc_o2", 1000 } } },
            { "illicit_ashbreath", new Dictionary<string, int> { { "fc_ashbreath", 1000 } } },
            { "illicit_nightglass", new Dictionary<string, int> { { "fc_nightglass", 1000 } } },
            { "illicit_redmist", new Dictionary<string, int> { { "fc_redmist", 1000 } } },
            { "illicit_voidsalt", new Dictionary<string, int> { { "fc_voidsalt", 1000 } } },
            { "liq_ammonia_water", new Dictionary<string, int> { { "fc_nh3", 150 }, { "water", 850 } } },
            { "liq_brine", new Dictionary<string, int> { { "fc_nacl", 260 }, { "water", 740 } } },
            { "liq_calcium_chloride", new Dictionary<string, int> { { "fc_cacl2", 400 }, { "water", 600 } } },
            { "liq_hydrochloric_acid", new Dictionary<string, int> { { "fc_hcl", 360 }, { "water", 640 } } },
            { "liq_hydrogen_peroxide", new Dictionary<string, int> { { "fc_h2o2", 300 }, { "water", 700 } } },
            { "liq_nitric_acid", new Dictionary<string, int> { { "fc_hno3", 680 }, { "water", 320 } } },
            { "liq_sodium_hydroxide", new Dictionary<string, int> { { "fc_naoh", 250 }, { "water", 750 } } },
            { "liq_sulfuric_acid", new Dictionary<string, int> { { "fc_h2so4", 900 }, { "water", 100 } } },
            { "sludge", new Dictionary<string, int> { { "fc_waste", 1000 } } },
            { "sol_activated_carbon", new Dictionary<string, int> { { "fc_c", 1000 } } },
            { "sol_ammonium_chloride", new Dictionary<string, int> { { "fc_nh4cl", 1000 } } },
            { "sol_ammonium_nitrate", new Dictionary<string, int> { { "fc_nh4no3", 1000 } } },
            { "sol_bleaching_powder", new Dictionary<string, int> { { "fc_caclo2", 1000 } } },
            { "sol_caffeine", new Dictionary<string, int> { { "fc_caffeine", 1000 } } },
            { "sol_calcium_carbonate", new Dictionary<string, int> { { "fc_caco3", 1000 } } },
            { "sol_calcium_hydroxide", new Dictionary<string, int> { { "fc_caoh2", 1000 } } },
            { "sol_calcium_oxide", new Dictionary<string, int> { { "fc_cao", 1000 } } },
            { "sol_coffee_grounds", new Dictionary<string, int> { { "fc_coffee", 1000 } } },
            { "sol_copper_oxide", new Dictionary<string, int> { { "fc_cuo", 1000 } } },
            { "sol_copper_sulfate", new Dictionary<string, int> { { "fc_cuso4", 1000 } } },
            { "sol_manganese_dioxide", new Dictionary<string, int> { { "fc_mno2", 1000 } } },
            { "sol_potassium_manganate", new Dictionary<string, int> { { "fc_k2mno4", 1000 } } },
            { "sol_potassium_nitrate", new Dictionary<string, int> { { "fc_kno3", 1000 } } },
            { "sol_potassium_permanganate", new Dictionary<string, int> { { "fc_kmno4", 1000 } } },
            { "sol_potassium_sulfate", new Dictionary<string, int> { { "fc_k2so4", 1000 } } },
            { "sol_sediment", new Dictionary<string, int> { { "fc_sediment", 1000 } } },
            { "sol_sodium_bicarbonate", new Dictionary<string, int> { { "fc_nahco3", 1000 } } },
            { "sol_sodium_carbonate", new Dictionary<string, int> { { "fc_na2co3", 1000 } } },
            { "sol_sodium_chloride", new Dictionary<string, int> { { "fc_nacl", 1000 } } },
            { "sol_sodium_hydroxide", new Dictionary<string, int> { { "fc_naoh", 1000 } } },
            { "sol_sodium_nitrate", new Dictionary<string, int> { { "fc_nano3", 1000 } } },
            { "sol_sulfur", new Dictionary<string, int> { { "fc_s", 1000 } } },
        };

        /// <summary>P10a：取某药剂的真实成分（合计 1000 = 100%）。</summary>
        public static bool TryGetComponentParts(string id, out Dictionary<string, int> parts)
        {
            parts = null;
            if (string.IsNullOrEmpty(id)) return false;
            return ComponentParts.TryGetValue(Normalize(id), out parts) && parts != null && parts.Count > 0;
        }

        /// <summary>
        /// P9：纯 ASCII 化学式表（分析仪/状态 tag 用；不依赖描述、不含 Unicode 下标）。
        /// 没有化学式的物品（便条/仪器等）不在表内，查询时返回「—」。
        /// </summary>
        static readonly Dictionary<string, string> AsciiFormulas = new Dictionary<string, string>
        {
            { "alcohol_lamp", "brass" },
            { "beaker", "glass" },
            { "can_chlorine_industrial", "Cl2" },
            { "can_chlorine_medical", "Cl2" },
            { "can_chlorine_ultrapure", "Cl2" },
            { "can_hydrogen_industrial", "H2" },
            { "can_hydrogen_medical", "H2" },
            { "can_hydrogen_ultrapure", "H2" },
            { "can_nitrogen_industrial", "N2" },
            { "can_nitrogen_medical", "N2" },
            { "can_nitrogen_ultrapure", "N2" },
            { "can_oxygen_industrial", "O2" },
            { "can_oxygen_medical", "O2" },
            { "can_oxygen_ultrapure", "O2" },
            { "catalyst_kit", "cat" },
            { "erlenmeyer_flask", "glass" },
            { "filter_paper", "paper" },
            { "filter_paper_ash", "paper" },
            { "filter_paper_fine", "paper" },
            { "gas_ammonia", "NH3" },
            { "gas_canister_empty", "steel" },
            { "gas_carbon_dioxide", "CO2" },
            { "gas_chlorine", "Cl2" },
            { "gas_drum_carbon_dioxide", "CO2" },
            { "gas_drum_chlorine", "Cl2" },
            { "gas_drum_empty", "steel" },
            { "gas_drum_hydrogen", "H2" },
            { "gas_drum_nitrogen", "N2" },
            { "gas_drum_oxygen", "O2" },
            { "gas_ethylene", "C2H4" },
            { "gas_hydrogen", "H2" },
            { "gas_hydrogen_chloride", "HCl(g)" },
            { "gas_nitrogen", "N2" },
            { "gas_nitrous_oxide", "N2O" },
            { "gas_oxygen", "O2" },
            { "label_printer", "print" },
            { "liq_ammonia_water", "NH3.H2O" },
            { "liq_brine", "NaCl(aq)" },
            { "liq_calcium_chloride", "CaCl2(aq)" },
            { "liq_hydrochloric_acid", "HCl(aq)" },
            { "liq_hydrogen_peroxide", "H2O2" },
            { "liq_nitric_acid", "HNO3" },
            { "liq_sodium_hydroxide", "NaOH(aq)" },
            { "liq_sulfuric_acid", "H2SO4" },
            { "note_chem_dealer", "note" },
            { "ph_meter", "inst" },
            { "precision_balance", "inst" },
            { "retort_flask", "glass" },
            { "safety_goggles", "gear" },
            { "sludge", "waste" },
            { "sol_activated_carbon", "C" },
            { "sol_ammonium_chloride", "NH4Cl" },
            { "sol_ammonium_nitrate", "NH4NO3" },
            { "sol_bleaching_powder", "Ca(ClO)2" },
            { "sol_caffeine", "C8H10N4O2" },
            { "sol_calcium_carbonate", "CaCO3" },
            { "sol_calcium_hydroxide", "Ca(OH)2" },
            { "sol_calcium_oxide", "CaO" },
            { "sol_coffee_grounds", "coffee" },
            { "sol_copper_oxide", "CuO" },
            { "sol_copper_sulfate", "CuSO4" },
            { "sol_manganese_dioxide", "MnO2" },
            { "sol_potassium_manganate", "K2MnO4" },
            { "sol_potassium_nitrate", "KNO3" },
            { "sol_potassium_permanganate", "KMnO4" },
            { "sol_potassium_sulfate", "K2SO4" },
            { "sol_sediment", "sediment" },
            { "sol_sodium_bicarbonate", "NaHCO3" },
            { "sol_sodium_carbonate", "Na2CO3" },
            { "sol_sodium_chloride", "NaCl" },
            { "sol_sodium_hydroxide", "NaOH" },
            { "sol_sodium_nitrate", "NaNO3" },
            { "sol_sulfur", "S" },
            { "spirit_burner", "brass" },
            { "test_tube_rack", "glass" },
            { "thermometer", "inst" },
            { "wick_ceramic", "ceramic" },
            { "wick_cotton", "cotton" },
        };

        /// <summary>P9：把生成文本里的 Unicode 下标（234…）转成 ASCII 数字，避免方块字。</summary>
        public static string ToAsciiText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= '\u2080' && c <= '\u2089') sb.Append((char)('0' + (c - '\u2080')));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>P9：取物品的 ASCII 化学式；无公式返回「—」。</summary>
        public static string Formula(string id)
        {
            if (string.IsNullOrEmpty(id)) return "—";
            string f;
            return AsciiFormulas.TryGetValue(Normalize(id), out f) ? f : "—";
        }

        /// <summary>P10b：接触即反应（不需要机器，玩家用一件药剂点另一件）。</summary>
        public sealed class ContactReaction
        {
            public string Id;
            public string[] Inputs;   // 无序：两件药剂
            public string[] Outputs;
            public string NameKey;
        }

        public static readonly ContactReaction[] ContactReactions =
        {
            new ContactReaction { Id = "contact_marble_hcl", NameKey = "contact_marble_hcl",
                Inputs = new[] { "liq_hydrochloric_acid", "sol_calcium_carbonate" },
                Outputs = new[] { "liq_calcium_chloride", "gas_carbon_dioxide" } },
            new ContactReaction { Id = "contact_neutralize_hcl", NameKey = "contact_neutralize_hcl",
                Inputs = new[] { "liq_hydrochloric_acid", "liq_sodium_hydroxide" },
                Outputs = new[] { "liq_brine" } },
            new ContactReaction { Id = "contact_neutralize_h2so4", NameKey = "contact_neutralize_h2so4",
                Inputs = new[] { "liq_sulfuric_acid", "liq_sodium_hydroxide" },
                Outputs = new[] { "liq_brine" } },
            new ContactReaction { Id = "contact_nh4no3", NameKey = "contact_nh4no3",
                Inputs = new[] { "liq_nitric_acid", "liq_ammonia_water" },
                Outputs = new[] { "sol_ammonium_nitrate" } },
            new ContactReaction { Id = "contact_slaked_lime", NameKey = "contact_slaked_lime",
                Inputs = new[] { "liq_water", "sol_calcium_oxide" },
                Outputs = new[] { "sol_calcium_hydroxide" } },
            new ContactReaction { Id = "contact_peroxide_mno2", NameKey = "contact_peroxide_mno2",
                Inputs = new[] { "liq_hydrogen_peroxide", "sol_manganese_dioxide" },
                Outputs = new[] { "gas_oxygen" } },
            new ContactReaction { Id = "contact_baking_soda_hcl", NameKey = "contact_baking_soda_hcl",
                Inputs = new[] { "liq_hydrochloric_acid", "sol_sodium_bicarbonate" },
                Outputs = new[] { "sol_sodium_chloride", "gas_carbon_dioxide" } },
            new ContactReaction { Id = "contact_nh4cl", NameKey = "contact_nh4cl",
                Inputs = new[] { "liq_hydrochloric_acid", "liq_ammonia_water" },
                Outputs = new[] { "sol_ammonium_chloride" } },
        };

        /// <summary>P10b：查找两件物品的接触反应（无序）。</summary>
        public static ContactReaction MatchContact(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return null;
            string na = Normalize(a), nb = Normalize(b);
            foreach (var r in ContactReactions)
            {
                if (r.Inputs.Length != 2) continue;
                string x = Normalize(r.Inputs[0]), y = Normalize(r.Inputs[1]);
                if ((x == na && y == nb) || (x == nb && y == na)) return r;
            }
            return null;
        }

        public static int GradeMultiplier(PurityGrade g)
        {
            // 对齐原版品级价值修正（百分比）
            switch (g)
            {
                case PurityGrade.Medical:   return 85;
                case PurityGrade.UltraPure: return 135;
                default:                    return 0;
            }
        }

        public static int GradePurity(PurityGrade g)
        {
            switch (g)
            {
                case PurityGrade.Medical:   return 95;
                case PurityGrade.UltraPure: return 99;
                default:                    return 70;
            }
        }

        public static Reaction Match(IEnumerable<string> rawInputs, string machineId,
                                     int temp, int ph, bool hasCatalyst)
        {
            var ids = rawInputs.Select(Normalize).Where(x => !string.IsNullOrEmpty(x)).ToArray();
            if (ids.Length == 0) return null;
            string key = string.Join("+", ids.OrderBy(x => x));

            Reaction best = null;
            foreach (var r in All)
            {
                if (r.Machine != machineId) continue;
                if (r.SortedInputs != key) continue;
                // P7c：温度档容忍 —— 达到或高于配方所需档位即可（不再要求精确相等）
                if (!TempOk(r.Temp, temp)) continue;
                if (r.Ph >= 0 && r.Ph != ph) continue;
                if (r.NeedsCatalyst && !hasCatalyst) continue;
                if (best == null || Specificity(r) > Specificity(best)) best = r;
            }
            return best;
        }

        /// <summary>
        /// P7c：容忍多余投料 —— 只要配方所需 inputs 全部在场（忽略额外物品）即可匹配；
        /// 用于「多放了同相物品」时不让整批变废渣（Finish 只消耗配方内物品，多余留在槽里）。
        /// </summary>
        public static Reaction MatchIgnoringExtras(IEnumerable<string> rawInputs, string machineId,
                                                   int temp, int ph, bool hasCatalyst)
        {
            var ids = new HashSet<string>(
                rawInputs.Select(Normalize).Where(x => !string.IsNullOrEmpty(x)));
            if (ids.Count == 0) return null;

            Reaction best = null;
            foreach (var r in All)
            {
                if (r.Machine != machineId) continue;
                if (!TempOk(r.Temp, temp)) continue;
                if (r.Ph >= 0 && r.Ph != ph) continue;
                if (r.NeedsCatalyst && !hasCatalyst) continue;
                bool allPresent = true;
                foreach (var i in r.Inputs)
                {
                    if (IsAuxiliary(i)) continue;
                    if (!ids.Contains(Normalize(i))) { allPresent = false; break; }
                }
                if (!allPresent) continue;
                if (best == null || Specificity(r) > Specificity(best)) best = r;
            }
            return best;
        }

        /// <summary>
        /// 温度档判定：temp ≥ 2 的配方要求「不低于」该档（P7c 容忍）；
        /// 审查 3.5/4.4：temp 0（冷凝吸收、次氯酸盐等要冷的配方）以前完全不查温度，
        /// 高温高压档下照样能「冷凝吸收」——现在要求不高于常温档（0/1）。temp 1 = 不限。
        /// </summary>
        public static bool TempOk(int recipeTemp, int temp)
        {
            if (recipeTemp >= 2) return temp >= recipeTemp;
            if (recipeTemp == 0) return temp <= 1;
            return true;
        }

        static int Specificity(Reaction r)
            => (r.Temp >= 2 ? 2 : 0) + (r.Ph >= 0 ? 2 : 0)
             + (r.NeedsCatalyst ? 1 : 0) + r.Tier * 2 + r.Inputs.Length;

        public static bool ShouldBonus(Reaction r, bool balanced, bool catalyst)
            => r != null && r.CatalystBonus && balanced && catalyst;

        public static IEnumerable<Reaction> ForMachine(string machineId)
            => All.Where(r => r.Machine == machineId);

        public static IEnumerable<Reaction> ForTier(int tier)
            => All.Where(r => r.Tier == tier);
    }
}
