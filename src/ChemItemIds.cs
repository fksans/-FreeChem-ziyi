using System;
using System.Globalization;
using System.Text;

namespace FreeChem
{
    /// <summary>
    /// FreeChem 私有数字 ID 表（调试生成入口用）。
    ///
    /// 规则（重要）：编号 1–97 (dynamic) **显式固定顺序**，索引 = 编号 - 1。
    ///   · 后续新增物品只能**追加到末尾**，绝不重排 / 插队 / 删除，
    ///     否则玩家记下的编号会错位。
    ///   · 编号不是原版物品 id，只是本模组 F9 调试入口的简称。
    ///   · 85 = ChemCatalog.Specs 的 72 件物品 + ChemRecipes.MachineIds 的 13 台设备，
    ///     顺序为「气体 → 液体 → 固体 → 容器/工具/器材 → 钢瓶商品 → 大型气桶 →
    ///     违禁品 → 副产物 → 13 台设备」。
    ///
    /// 本文件是**纯数据层**：不引用 UnityEngine / ModHelper / 任何游戏程序集，
    /// 便于 tools/logic-harness 直接编译并断言（唯一允许的依赖是 BCL）。
    /// 需要本地化名称的展示逻辑在 ChemDebug.Describe() 里（它才引用 ModHelper）。
    /// </summary>
    public static class ChemItemIds
    {
        public const int MinId = 1;
        public const int MaxId = 98;

        /// <summary>TryParseCommand 对 "list" 指令返回的 numId（不是合法物品编号）。</summary>
        public const int ListCommand = -1;

        /// <summary>
        /// 1 号起的稳定顺序表。索引 = 数字 id - 1。
        /// 【只允许在 MaxId 之后追加】，并同步更新 MaxId。
        /// </summary>
        public static readonly string[] Ordered =
        {
            // ── 1–7 气体 ──
            "gas_chlorine",              // 1
            "gas_hydrogen",              // 2
            "gas_oxygen",                // 3
            "gas_nitrogen",              // 4
            "gas_hydrogen_chloride",     // 5
            "gas_ammonia",               // 6
            "gas_carbon_dioxide",        // 7
            // ── 8–15 液体 ──
            "liq_hydrochloric_acid",     // 8
            "liq_sulfuric_acid",         // 9
            "liq_nitric_acid",           // 10
            "liq_sodium_hydroxide",      // 11
            "liq_hydrogen_peroxide",     // 12
            "liq_brine",                 // 13
            "liq_ammonia_water",         // 14
            "liq_calcium_chloride",      // 15
            // ── 16–25 固体 ──
            "sol_sodium_chloride",       // 16
            "sol_sodium_hydroxide",      // 17
            "sol_copper_sulfate",        // 18
            "sol_potassium_nitrate",     // 19
            "sol_calcium_carbonate",     // 20
            "sol_ammonium_nitrate",      // 21
            "sol_potassium_permanganate",// 22
            "sol_sulfur",                // 23
            "sol_activated_carbon",      // 24
            "sol_bleaching_powder",      // 25
            // ── 26–31 容器 / 工具 / 防护 ──
            "gas_canister_empty",        // 26
            "catalyst_kit",              // 27
            "precision_balance",         // 28
            "ph_meter",                  // 29
            "thermometer",               // 30
            "safety_goggles",            // 31
            // ── 32–42 实验器材 / 耗材 / 加热器 ──
            "beaker",                    // 32
            "retort_flask",              // 33
            "erlenmeyer_flask",          // 34
            "test_tube_rack",            // 35
            "filter_paper",              // 36
            "filter_paper_fine",         // 37
            "filter_paper_ash",          // 38
            "wick_cotton",               // 39
            "wick_ceramic",              // 40
            "alcohol_lamp",              // 41
            "spirit_burner",             // 42
            // ── 43–48 热反应新增物质 ──
            "liq_ethanol",               // 43  [RESERVED 已移除：改用原版酒精瓶]
            "liq_water",                 // 44  [RESERVED 已移除：改用原版水容器]
            "gas_ethylene",              // 45
            "gas_nitrous_oxide",         // 46
            "sol_coffee_grounds",        // 47
            "sol_caffeine",              // 48
            // ── 49–60 气罐商品（三档纯度）──
            "can_oxygen_industrial",     // 49
            "can_oxygen_medical",        // 50
            "can_oxygen_ultrapure",      // 51
            "can_hydrogen_industrial",   // 52
            "can_hydrogen_medical",      // 53
            "can_hydrogen_ultrapure",    // 54
            "can_nitrogen_industrial",   // 55
            "can_nitrogen_medical",      // 56
            "can_nitrogen_ultrapure",    // 57
            "can_chlorine_industrial",   // 58
            "can_chlorine_medical",      // 59
            "can_chlorine_ultrapure",    // 60
            // ── 61–66 大型储气桶 ──
            "gas_drum_empty",            // 61
            "gas_drum_oxygen",           // 62
            "gas_drum_nitrogen",         // 63
            "gas_drum_hydrogen",         // 64
            "gas_drum_chlorine",         // 65
            "gas_drum_carbon_dioxide",   // 66
            // ── 67–70 T4 虚构违禁品 ──
            "illicit_voidsalt",          // 67
            "illicit_redmist",           // 68
            "illicit_nightglass",        // 69
            "illicit_ashbreath",         // 70
            // ── 71–72 副产物 ──
            "sludge",                    // 71
            "sol_sediment",              // 72
            // ── 73–85 设备（与 ChemRecipes.MachineIds 顺序一致）──
            "chem_reactor",              // 73
            "electrolyzer",              // 74
            "gas_generator",             // 75
            "condenser_unit",            // 76
            "crystallizer",              // 77
            "high_pressure_synth",       // 78
            "fractionator",              // 79
            "titration_bench",           // 80
            "canister_filling",          // 81
            "decanter",                  // 82
            "gas_scrubber",              // 83
            "filtration_bench",          // 84
            "heating_platform",          // 85
            // ── 86+ 后续新增只能追加（P1b1：标签打印机）──
            "label_printer",             // 86
            // ── 87–96 P2a：补齐缺失化学品（石灰/碱类/氧化剂副产物）──
            "sol_calcium_oxide",         // 87  生石灰 CaO
            "sol_calcium_hydroxide",     // 88  熟石灰 Ca(OH)2
            "sol_sodium_carbonate",      // 89  纯碱 Na2CO3
            "sol_sodium_bicarbonate",    // 90  小苏打 NaHCO3
            "sol_ammonium_chloride",     // 91  氯化铵 NH4Cl
            "sol_manganese_dioxide",     // 92  二氧化锰 MnO2
            "sol_copper_oxide",          // 93  氧化铜 CuO
            "sol_potassium_sulfate",     // 94  硫酸钾 K2SO4
            "sol_sodium_nitrate",        // 95  硝酸钠 NaNO3
            "sol_potassium_manganate",   // 96  锰酸钾 K2MnO4
            "note_chem_dealer",          // 97  化学品商人便条（P3e 电话订购线索）
            "test_tube",                 // 98  试管（P10c：小容量液体容器，可放试管架）
        };

        /// <summary>编号 → 物品/设备 id。越界返回 false。</summary>
        public static bool TryGetId(int numId, out string itemId)
        {
            if (numId >= MinId && numId <= MaxId && numId - MinId < Ordered.Length)
            {
                itemId = Ordered[numId - MinId];
                return true;
            }
            itemId = null;
            return false;
        }

        /// <summary>物品/设备 id 是否在编号表内。</summary>
        public static bool IsKnown(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            for (int i = 0; i < Ordered.Length; i++)
                if (Ordered[i] == itemId) return true;
            return false;
        }

        /// <summary>纯文本描述（无本地化）："#12 liq_hydrogen_peroxide"。</summary>
        public static string DescribeRaw(int numId)
        {
            string itemId;
            return TryGetId(numId, out itemId) ? "#" + numId + " " + itemId : "#" + numId + " ?";
        }

        /// <summary>
        /// 解析调试输入。接受：
        ///   "85"、"spawn 85"、"id 85"、"get 85"、"生成 85"、"生成物品 85"、
        ///   全角数字 "８５"、前后空白/全角空格；
        ///   "list" / "ls" / "列表" → numId = ListCommand。
        /// 返回 false 时 error 为 "empty" / "invalid" / "range"（供上层映射本地化文案）。
        /// </summary>
        public static bool TryParseCommand(string raw, out int numId, out string error)
        {
            numId = 0;
            error = null;

            string s = NormalizeFullWidthDigits(raw);
            if (s == null) { error = "empty"; return false; }
            s = s.Trim();
            if (s.Length == 0) { error = "empty"; return false; }

            if (IsListWord(s)) { numId = ListCommand; return true; }

            // 去掉一个可选动词前缀（要求前缀后是空白，避免 "id85" 之类的歧义）
            string[] prefixes = { "spawn", "id", "get", "生成物品", "生成" };
            foreach (var p in prefixes)
            {
                if (s.Length > p.Length && s.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                    && char.IsWhiteSpace(s[p.Length]))
                {
                    s = s.Substring(p.Length).Trim();
                    break;
                }
            }
            if (s.Length == 0) { error = "empty"; return false; }

            int parsed;
            if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed))
            {
                error = "invalid";
                return false;
            }
            if (parsed < MinId || parsed > MaxId)
            {
                error = "range";
                return false;
            }
            numId = parsed;
            return true;
        }

        static bool IsListWord(string s)
            => string.Equals(s, "list", StringComparison.OrdinalIgnoreCase)
               || string.Equals(s, "ls", StringComparison.OrdinalIgnoreCase)
               || s == "列表";

        /// <summary>把全角数字 ０–９ 归一为半角；非全角字符原样保留。</summary>
        static string NormalizeFullWidthDigits(string raw)
        {
            if (raw == null) return null;
            StringBuilder sb = null;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c >= '\uFF10' && c <= '\uFF19')   // ０..９
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder(raw.Length);
                        sb.Append(raw, 0, i);
                    }
                    sb.Append((char)('0' + (c - '\uFF10')));
                }
                else if (sb != null)
                {
                    sb.Append(c);
                }
            }
            return sb == null ? raw : sb.ToString();
        }
    }
}
