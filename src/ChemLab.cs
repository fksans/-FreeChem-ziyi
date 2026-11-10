using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeChem
{
    /// <summary>
    /// 器材 / 耗材 / 污染物 / 实时加热的纯数据层。
    /// 不引用游戏类型，便于独立编译校验。
    ///
    /// 依据原版「化学加工」「水务处理」「加热板」词条：
    ///  · 污染物粒径：化学污染物 2、重金属 12、物理污染物 100、微生物 50、碎屑 200
    ///  · 滤芯孔径：普通 22、韧棉 150；粒径 &lt; 22 的重金属/化学污染物不被去除
    ///  · 化学污染物有 0.7% 残留下限 → 纯度永远到不了 100%
    ///  · 加热板每秒回调，升温/冷却公式见 HeatingStep / CoolingStep
    ///
    /// ═══ 量纲约定（问题 8：全模组统一，勿混用）═══
    ///  · 温度：°C（float），室温 26；物品 tag 里存 ×100 的 int（ChemCatalog.TTemp）。
    ///  · 液体体积：μl。加热台冷却公式参考满液量 500 ml 标准瓶（P3a：液体瓶容量 500 ml）。
    ///  · 每件物品的毒性/欣快载荷基准：250 ml = 250000 μl（ChemRuntime.VolumeUl，与瓶容量解耦）。
    ///  · 燃料：ml（酒精灯 TFuel），灯芯耐久为「次数」。
    ///  · 纯度：百分数整数 0–100（tag TPurity）；内部污染物之和用 1/100 百分点标度
    ///    （ChemLab.Scale=100，ResidualFloor=70 表示 0.70%）。两者差 100 倍，勿混用。
    ///  · 排气嫌疑度：单位 = 原版治安部 ENDANGERMENT 写入量（每排放单位 ×10，再乘排风倍率）；
    ///    raw 单位是「一次毒性气体产出」（见 ExhaustUnitsForGas），不是体积。
    ///  · 毒性/欣快：L/E 载荷 = VolumeUl × 系数/100；L≥1000 致命、750≤L&lt;1000 有毒、E≥750 起欣快 1 级。
    /// ═══════════════════════════════════════════
    /// </summary>
    public static class ChemLab
    {
        // ── 污染物类别与粒径 ──
        public static readonly Dictionary<string, int> ContaminantSize = new Dictionary<string, int>
        {
            { "chem",   2   },   // 化学污染物：游离、溶解态，滗析滤不掉
            { "heavy",  12  },   // 重金属：粒径 12 < 22，普通滤纸滤不掉
            { "micro",  50  },   // 微生物
            { "plastic",25  },   // 微塑料
            { "organic",40  },   // 有机废物
            { "mineral",30  },   // 矿物质
            { "phys",   100 },   // 物理污染物：大颗粒，可沉降
            { "debris", 200 },   // 不溶性碎屑：大颗粒，可沉降
        };

        public static readonly string[] ContaminantKeys =
            { "chem", "heavy", "micro", "plastic", "organic", "mineral", "phys", "debris" };

        /// <summary>可沉降的大颗粒（滗析能分离的部分）。</summary>
        public static readonly string[] Settleable = { "phys", "debris" };

        public const int ResidualFloor = 70;      // 0.70%（×100），对齐原版化学污染物下限
        public const int Scale = 100;             // 数值以 1/100 百分点存储

        // ── 原版化学产品七档纯度（阈值取自 ChemicalProductHelper 常量，见 docs/harmony-bridge.md §3.1）──
        // 映射到 ItemConditionList.CreateStarchGrade() .. CreateLabPure() 七档。
        public const int GradeStarch      = 0;
        public const int GradeJunk        = 30;
        public const int GradeCut         = 45;
        public const int GradeStreet      = 60;
        public const int GradeNearPharma  = 80;
        public const int GradePharma      = 90;
        public const int GradeLabPure     = 99;

        /// <summary>把 0–100 的纯度映射为原版七档的阈值数值（99/90/80/60/45/30/0）。</summary>
        public static int VanillaGradeOf(int purity)
        {
            if (purity >= GradeLabPure) return GradeLabPure;
            if (purity >= GradePharma) return GradePharma;
            if (purity >= GradeNearPharma) return GradeNearPharma;
            if (purity >= GradeStreet) return GradeStreet;
            if (purity >= GradeCut) return GradeCut;
            if (purity >= GradeJunk) return GradeJunk;
            return GradeStarch;
        }

        // ── 多级提纯等级（P1c）：突破口 94.4% 的纯度高限 ────────────────
        // 每件液体药剂带 fc_purify_level（0–5）。滤纸每次过滤把等级 +1（封顶 5）：
        //   · 残留下限 = 70 >> level（0.70% → 0.35% → … → 0.02%）
        //   · 理论纯度上限 = 100% − 残留下限×8 类：level0 94.40%、level5 99.84%
        //     → 过滤链可以把纯度推到 99.84%，但**任何 level 都仍 <100%**；
        //     100% 只能靠标签打印机的「校准」路径（消耗催化剂充能，见 ChemPrinter）。
        public const int MaxPurifyLevel = 5;

        /// <summary>提纯等级对应的单类残留下限（×100 标度）：70 >> level。</summary>
        public static int PurifyFloor(int level)
        {
            int l = Math.Max(0, Math.Min(MaxPurifyLevel, level));
            return ResidualFloor >> l;   // 70 / 35 / 17 / 8 / 4 / 2
        }

        /// <summary>该等级下的理论纯度上限（×100 标度）：10000 − PurifyFloor(level)×8。
        /// level0 = 9440（94.4%）、level5 = 9984（99.84%），永远 &lt; 10000。</summary>
        public static int MaxPurity(int level)
            => 100 * Scale - PurifyFloor(level) * ContaminantKeys.Length;


        /// <summary>滤纸：孔径越小能拦的颗粒越多，但仍有残留下限。</summary>
        public static readonly Dictionary<string, int> FilterPore = new Dictionary<string, int>
        {
            { "filter_paper",       22  },  // 普通化学滤纸，拦 ≥22 μm 等效粒径
            { "filter_paper_fine",  5   },  // 精密滤纸，可拦重金属(12)、微塑料(25)
            { "filter_paper_ash",   2   },  // 无灰定量滤纸，可拦化学污染物(2)，仍受下限
        };

        public static readonly Dictionary<string, int> FilterDurability = new Dictionary<string, int>
        {
            { "filter_paper",       8 },
            { "filter_paper_fine",  5 },
            { "filter_paper_ash",   3 },
        };

        // ── 器材与加热器 ──
        public static readonly string[] Vessels =
            { "beaker", "retort_flask", "erlenmeyer_flask", "test_tube_rack" };

        public static readonly string[] HeaterIds = { "alcohol_lamp", "spirit_burner" };
        public static readonly string[] WickIds   = { "wick_cotton", "wick_ceramic" };

        /// <summary>
        /// 原版酒精燃料候选 id。用户要求「燃料可以使用原版的酒精」。
        /// 研究确认（IL）：原版酒精瓶 = `mini_bottle`（PreBuildChemHelper.CreateMiniAlcoholBottle，
        /// alcohol 175 / water 75 = 70%）。mini_bottle 是共用 id（水/营养液/酒精），
        /// 因此燃料判定看 alcohol 组分 tag（ChemCatalog.IsAlcoholFuel/AlcoholFuelPurity），本表仅为兼容候选。
        /// </summary>
        public static readonly string[] VanillaAlcoholIds =
            { "mini_bottle" };   // 原版酒精瓶（燃料判定优先按 alcohol 组分，见 ChemCatalog.IsAlcoholFuel）

        /// <summary>原版酒精组分纯度：小瓶酒精 175 ml 酒精 + 75 ml 水 = 70%。</summary>
        public const int VanillaAlcoholPurity = 70;

        // ── 排气嫌疑度（纯函数层）────────────────────────────────────────
        // 本层只做与游戏无关的纯计算：倍率、折算量与气体分级。
        // 存档由 ChemRuntime 负责；其中「写入原版治安部 ENDANGERMENT」也在 ChemRuntime，
        // 使用 docs/api-findings.md §2 核对过的未文档化 public 成员（带 guard、可配置关闭）。
        // 官方 modding API 没有公开写 heat / 调查进度的接口，因此该接入不可视为官方支持。
        // ─────────────────────────────────────────────────────────────

        /// <summary>毒性/刺激性气体：只有这些气体的排放会计入嫌疑度。CO2/O2/N2/H2 不计。</summary>
        public static readonly string[] ToxicExhaustGases =
            { "gas_chlorine", "gas_hydrogen_chloride", "gas_ammonia" };

        public static bool IsToxicExhaustGas(string id)
        {
            string n = ChemRecipes.Normalize(id);
            foreach (var g in ToxicExhaustGases) if (g == n) return true;
            return false;
        }

        /// <summary>
        /// 单次排放的嫌疑单位：按原版式毒性系数（ChemRecipes.Profiles 的 Toxic 列）分级，
        /// ≥3.0 记 2 单位，否则 1 单位；非毒性气体记 0。
        /// 说明：不直接读 ChemCatalog 的 spec.Hazard —— gas_chlorine 的 spec.Hazard=2
        /// 与氯气罐 Hazard=3 自相矛盾，而毒性系数（Cl2 4.0 / HCl 2.2 / NH3 1.6）
        /// 是与原版 L 公式同源的判据，氯气最毒、计数最高，符合化学常识。
        /// </summary>
        public static int ExhaustUnitsForGas(string id)
        {
            if (!IsToxicExhaustGas(id)) return 0;
            return ChemRecipes.Profile(id).Toxicity >= 3.0f ? 2 : 1;
        }

        /// <summary>排风服务倍率（万分比）：0 无服务 10000 / 1 基础排风 5500 / 2 进阶排风 1200；未知等级按无服务。</summary>
        public static int VentMultiplierBp(int ventLevel)
        {
            switch (ventLevel)
            {
                case 1: return 5500;
                case 2: return 1200;
                default: return 10000;
            }
        }

        /// <summary>
        /// 每个「排气嫌疑单位」折算成多少点原版治安部 ENDANGERMENT 犯罪量（问题 8 量纲统一）。
        /// 常量放纯数据层，ChemLab.SuspicionFrom 与 ChemRuntime.CommitVanillaEndangerment 共用，
        /// 保证「提示框显示值 = 实际写入原版治安部的量」。
        /// </summary>
        public const int VanillaEndangermentPerUnit = 10;

        /// <summary>
        /// 排气嫌疑度（显示值 = 原版 ENDANGERMENT 写入量）：
        ///   rawExhaust × VanillaEndangermentPerUnit(10) × 排风倍率 / 10000，整数截断。
        /// raw=0 → 0；对同一 raw，进阶 &lt; 基础 &lt; 无服务（严格）；随 raw 单调不降。
        /// </summary>
        public static int SuspicionFrom(int rawExhaust, int ventLevel)
        {
            if (rawExhaust <= 0) return 0;
            long v = (long)rawExhaust * VanillaEndangermentPerUnit
                     * VentMultiplierBp(ventLevel) / 10000L;
            return v > int.MaxValue ? int.MaxValue : (int)v;
        }

        /// <summary>燃料纯度 → 加热因子倍率（0.4 ~ 1.6）。低纯度酒精火力弱且消耗快。</summary>
        public static float HeatFactor(int fuelPurity)
            => 0.4f + 1.2f * Math.Max(0, Math.Min(100, fuelPurity)) / 100f;

        /// <summary>燃料消耗速率倍率：纯度越低越费。</summary>
        public static float FuelBurnRate(int fuelPurity)
            => 1.6f - 0.9f * Math.Max(0, Math.Min(100, fuelPurity)) / 100f;

        // ── 原版加热板公式 ──
        /// <summary>每秒升温：min(目标, T + (目标−T)×因子/350 + 1)。</summary>
        public static float HeatingStep(float current, float target, float factor)
            => Math.Min(target, current + (target - current) * factor / 350f + 1f);

        /// <summary>每秒冷却：26+(T−26)×exp(−6.25/(√V+11))。</summary>
        public static float CoolingStep(float current, float volumeUl)
        {
            float v = Math.Max(1f, volumeUl);
            return 26f + (current - 26f) * (float)Math.Exp(-6.25 / (Math.Sqrt(v) + 11.0));
        }

        public const float RoomTemp = 26f;

        /// <summary>低档目标 99°C / 高档 160°C，模组数 2–4 时 26–160 可调（对齐原版）。</summary>
        public static float TargetTemp(int moduleCount, bool high, float adjusted = -1f)
        {
            if (moduleCount >= 2) return Math.Max(26f, Math.Min(160f, adjusted >= 0 ? adjusted : 99f));
            return high ? 160f : 99f;
        }

        /// <summary>模组数 3 / 4 的加速分支（×4 / ×16），超过 4 不命中。</summary>
        public static float ModuleBoost(int moduleCount)
            => moduleCount == 3 ? 4f : (moduleCount == 4 ? 16f : 1f);

        // ── 纯度：只能逼近，不能达到 100% ──
        /// <summary>
        /// 滗析：静置沉降低层大颗粒，上层清液仍是同一种药剂。
        /// 【游离的化学污染物、重金属、微生物不会被分离】——这是本轮的核心约束。
        /// 返回分离后的污染物数组（每类 1/100 百分点）。
        /// </summary>
        public static int[] Decant(int[] c)
        {
            var r = (int[])c.Clone();
            foreach (var k in Settleable)
            {
                int i = Array.IndexOf(ContaminantKeys, k);
                r[i] = Math.Max(0, r[i] - r[i] * 85 / 100);   // 移走 85% 可沉降颗粒
            }
            return r;
        }

        /// <summary>
        /// 过滤：按孔径拦截粒径 ≥ 孔径的污染物，每类保留残留下限。
        /// 化学污染物(2)、重金属(12) 用普通滤纸(22) 无法去除；即使无灰滤纸(2) 也有下限。
        /// 2 参重载等价于 level 0（旧行为）；3 参重载把残留下限降到 PurifyFloor(level)。
        /// </summary>
        public static int[] Filter(int[] c, string filterId) => Filter(c, filterId, 0);

        public static int[] Filter(int[] c, string filterId, int purifyLevel)
        {
            int pore;
            if (!FilterPore.TryGetValue(filterId, out pore)) return (int[])c.Clone();
            var r = (int[])c.Clone();
            int floor = PurifyFloor(purifyLevel);
            for (int i = 0; i < ContaminantKeys.Length; i++)
            {
                if (ContaminantSize[ContaminantKeys[i]] < pore) continue;  // 穿过滤纸
                r[i] = Math.Max(floor, r[i] - r[i] * 70 / 100);
            }
            return r;
        }

        /// <summary>纯度 = 100% − Σ(残留污染物)，上限受提纯等级约束（level0 上限 94.4%，任意 level &lt;100%）。
        /// 100% 只能由打印机校准显式写入。</summary>
        public static int PurityFrom(int[] c) => PurityFrom(c, 0);

        /// <summary>带提纯等级的纯度计算：ceiling = 10000 − PurifyFloor(level)×8。</summary>
        public static int PurityFrom(int[] c, int purifyLevel)
        {
            long sum = 0;
            foreach (var v in c) sum += Math.Max(0, v);
            int p = 100 * Scale - (int)Math.Min(100 * Scale, sum);
            int ceiling = MaxPurity(purifyLevel);   // 硬上限（随提纯等级提高，但永远 <100%）
            return Math.Min(p, ceiling);
        }

        /// <summary>由纯度换算档位：≥99% 化学纯 / ≥90% 医用 / 其余工业。</summary>
        public static PurityGrade GradeFromPurity(int purityScaled)
        {
            if (purityScaled >= 99 * Scale) return PurityGrade.UltraPure;
            if (purityScaled >= 90 * Scale) return PurityGrade.Medical;
            return PurityGrade.Industrial;
        }

        /// <summary>
        /// 新建一件「脏」药剂的初始污染物。
        /// 每类约 2%，8 类合计 15% → 纯度 85%，落在「街头标准/工业级」。
        /// 这样基础药剂不会一出生就是医用级。
        /// </summary>
        public static int[] InitialContaminants()
        {
            var c = new int[ContaminantKeys.Length];
            for (int i = 0; i < c.Length; i++) c[i] = 200;
            c[Array.IndexOf(ContaminantKeys, "phys")]   = 150;
            c[Array.IndexOf(ContaminantKeys, "debris")] = 150;
            return c;
        }

        // ── 实时热反应（加热台专属，温度窗口 + 降解） ──
        public sealed class ThermalReaction
        {
            public readonly string Id, Name, Equation;
            public readonly string[] Inputs, Outputs;
            public readonly float MinTemp, MaxTemp, DegradeTemp;
            public readonly int Seconds;         // 需要持续在温区内多少秒
            public readonly bool UsesVanilla;
            public ThermalReaction(string id, string name, string equation, string[] inputs,
                string[] outputs, float minT, float maxT, float degradeT, int seconds,
                bool usesVanilla = false)
            {
                Id=id; Name=name; Equation=equation; Inputs=inputs; Outputs=outputs;
                MinTemp=minT; MaxTemp=maxT; DegradeTemp=degradeT; Seconds=seconds;
                UsesVanilla=usesVanilla;
            }
            public string SortedInputs => string.Join("+", Inputs.Select(ChemRecipes.Normalize).OrderBy(x => x));
        }

        public static readonly ThermalReaction[] ThermalReactions =
        {
            new ThermalReaction("heat_hcl_distill", "盐酸蒸馏提纯",
                "HCl(aq) --78–110°C→ 更纯 HCl(aq)",
                new[]{"liq_hydrochloric_acid"}, new[]{"liq_hydrochloric_acid"},
                78f, 110f, 130f, 20),
            // 审查 4.4：与夜间 kmno4_decomp 产物一致（以前只产 O2，两种固体凭空消失）
            new ThermalReaction("heat_kmno4", "高锰酸钾热分解",
                "2KMnO4 --Δ→ K2MnO4 + MnO2 + O2↑",
                new[]{"sol_potassium_permanganate"},
                new[]{"gas_oxygen", "sol_manganese_dioxide", "sol_potassium_manganate"},
                140f, 160f, 190f, 25),
            new ThermalReaction("heat_nh4no3", "硝酸铵热分解",
                "NH4NO3 --Δ→ N2O↑ + 2H2O",
                new[]{"sol_ammonium_nitrate"}, new[]{"gas_nitrous_oxide"},
                150f, 160f, 200f, 25),
            // P3b/P3c 口径修正（Lead 确认）：原版咖啡豆/咖啡因已有原版制作逻辑，本模组不再提供
            // 咖啡因热萃取路径；sol_coffee_grounds(#47) / sol_caffeine(#48) 仅保留注册作旧存档 legacy。
            new ThermalReaction("heat_peroxide", "双氧水热分解",
                "2H2O2 --Δ→ 2H2O + O2↑",
                new[]{"liq_hydrogen_peroxide"}, new[]{"gas_oxygen"},
                80f, 120f, 150f, 15),
            new ThermalReaction("heat_niter", "硝石制硝酸（加热）",
                "KNO3 + H2SO4 --Δ→ KHSO4 + HNO3↑",
                new[]{"sol_potassium_nitrate", "liq_sulfuric_acid"}, new[]{"liq_nitric_acid"},
                120f, 160f, 190f, 30),
        };

        /// <summary>
        /// 审查 3.6：只按投料匹配热反应、不看温度（供加热台判断「升温中 / 反应中 / 超温降解」）。
        /// 以前只有 MatchThermal：温度一超过 MaxTemp 就匹配不到，超温降解分支永远走不到。
        /// </summary>
        public static ThermalReaction MatchThermalInputs(IEnumerable<string> rawInputs)
        {
            var ids = rawInputs.Select(ChemRecipes.Normalize).Where(x => !string.IsNullOrEmpty(x)).ToArray();
            if (ids.Length == 0) return null;
            string key = string.Join("+", ids.OrderBy(x => x));
            foreach (var r in ThermalReactions)
                if (r.SortedInputs == key) return r;
            return null;
        }

        public static ThermalReaction MatchThermal(IEnumerable<string> rawInputs, float temp)
        {
            var ids = rawInputs.Select(ChemRecipes.Normalize).Where(x => !string.IsNullOrEmpty(x)).ToArray();
            if (ids.Length == 0) return null;
            string key = string.Join("+", ids.OrderBy(x => x));
            foreach (var r in ThermalReactions)
                if (r.SortedInputs == key && temp >= r.MinTemp && temp <= r.MaxTemp)
                    return r;
            return null;
        }

        /// <summary>数据层自检。</summary>
        public static List<string> ValidateLogic()
        {
            var p = new List<string>();
            // 滗析不能去除游离污染物
            var init = InitialContaminants();
            var dec = Decant(init);
            foreach (var k in new[] { "chem", "heavy", "micro", "plastic", "organic", "mineral" })
            {
                int i = Array.IndexOf(ContaminantKeys, k);
                if (dec[i] != init[i]) p.Add($"滗析错误地去除了游离污染物 {k}");
            }
            foreach (var k in Settleable)
            {
                int i = Array.IndexOf(ContaminantKeys, k);
                if (dec[i] >= init[i]) p.Add($"滗析未减少可沉降颗粒 {k}");
            }
            // 普通滤纸滤不掉化学污染物/重金属
            var filt = Filter(init, "filter_paper");
            foreach (var k in new[] { "chem", "heavy" })
            {
                int i = Array.IndexOf(ContaminantKeys, k);
                if (filt[i] != init[i]) p.Add($"普通滤纸错误地滤掉了 {k}（粒径 < 22）");
            }
            // 纯度永远 < 100%
            var best = Filter(Filter(Filter(init, "filter_paper_ash"), "filter_paper_fine"), "filter_paper");
            if (PurityFrom(best) >= 100 * Scale) p.Add("纯度达到了 100%，违反残留下限");
            if (PurityFrom(Decant(init)) <= PurityFrom(init)) p.Add("滗析没有提升纯度");

            // ── P1c 多级提纯：等级越高上限越高，但任意等级仍 <100%（100% 只能打印机校准）──
            if (MaxPurity(0) != 100 * Scale - ResidualFloor * ContaminantKeys.Length)
                p.Add("level0 纯度上限不等于 94.4% 基准（10000−70×8）");
            for (int lv = 1; lv <= MaxPurifyLevel; lv++)
                if (MaxPurity(lv) <= MaxPurity(lv - 1))
                    p.Add($"提纯等级 {lv} 的纯度上限未高于等级 {lv - 1}");
            if (PurifyFloor(0) != ResidualFloor || PurifyFloor(MaxPurifyLevel) != (ResidualFloor >> MaxPurifyLevel))
                p.Add("PurifyFloor 位移逻辑不符（应为 70>>level）");
            if (PurifyFloor(-1) != PurifyFloor(0) || PurifyFloor(99) != PurifyFloor(MaxPurifyLevel))
                p.Add("PurifyFloor 未对等级做 0..5 夹取");
            for (int lv = 0; lv <= MaxPurifyLevel; lv++)
            {
                if (PurityFrom(init, lv) < PurityFrom(init, Math.Max(0, lv - 1)) && lv > 0)
                    p.Add($"提纯等级 {lv} 下纯度反而下降");
                if (PurityFrom(init, lv) >= 100 * Scale)
                    p.Add($"提纯等级 {lv} 下纯度达到/超过 100%（应永远 <100%，除非打印机校准）");
            }
            if (MaxPurity(MaxPurifyLevel) != 100 * Scale - 2 * ContaminantKeys.Length)
                p.Add("level5 纯度上限应为 99.84%（10000−2×8）");
            var clean = new int[ContaminantKeys.Length];
            if (PurityFrom(clean, MaxPurifyLevel) != MaxPurity(MaxPurifyLevel))
                p.Add("无污染物时 level5 纯度未达到理论上限");
            if (PurityFrom(clean, MaxPurifyLevel) >= 100 * Scale)
                p.Add("多级提纯理论上限达到了 100%（应 <100%，100% 只能打印机校准）");
            // 等级提高后过滤确实能把纯度推高（滤纸孔径仍生效）
            if (PurityFrom(Filter(init, "filter_paper_ash", MaxPurifyLevel), MaxPurifyLevel)
                <= PurityFrom(Filter(init, "filter_paper_ash", 0), 0))
                p.Add("高提纯等级下过滤未提升纯度上限");
            // 加热公式单调且不超过目标
            if (HeatingStep(26f, 99f, 10f) <= 26f || HeatingStep(26f, 99f, 10f) > 99f)
                p.Add("升温公式越界");
            if (CoolingStep(100f, 500000f) >= 100f || CoolingStep(100f, 500000f) < 26f)
                p.Add("冷却公式越界");
            // 燃料纯度影响
            if (!(HeatFactor(95) > HeatFactor(70))) p.Add("高纯度酒精未提升加热效率");
            if (!(FuelBurnRate(40) > FuelBurnRate(95))) p.Add("低纯度酒精未增加消耗");
            // 热反应温区合法
            foreach (var r in ThermalReactions)
                if (!(r.MinTemp < r.MaxTemp && r.MaxTemp <= r.DegradeTemp && r.Seconds > 0))
                    p.Add($"[{r.Id}] 温区或时长不合法");

            // ── 排气嫌疑度纯函数（排风倍率 × 模组内索引；原版写入在 ChemRuntime）──
            if (VentMultiplierBp(0) <= VentMultiplierBp(1) || VentMultiplierBp(1) <= VentMultiplierBp(2))
                p.Add("排风倍率未严格递减（无服务 > 基础排风 > 进阶排风）");
            if (VentMultiplierBp(0) <= 0 || VentMultiplierBp(1) <= 0 || VentMultiplierBp(2) <= 0)
                p.Add("排风倍率出现非正值");
            if (VentMultiplierBp(-1) != VentMultiplierBp(0) || VentMultiplierBp(3) != VentMultiplierBp(0))
                p.Add("未知排风等级未按「无服务」处理");
            if (SuspicionFrom(0, 0) != 0 || SuspicionFrom(0, 2) != 0)
                p.Add("raw=0 时嫌疑度不为 0");
            if (SuspicionFrom(10, 2) >= SuspicionFrom(10, 1)
                || SuspicionFrom(10, 1) >= SuspicionFrom(10, 0))
                p.Add("进阶排风未严格降低嫌疑度增长");
            if (SuspicionFrom(10, 1) < SuspicionFrom(5, 1))
                p.Add("嫌疑度不随排放量单调不降");
            if (ExhaustUnitsForGas("gas_chlorine") != 2)
                p.Add("氯气（毒性 4.0）应记 2 个嫌疑单位");
            if (ExhaustUnitsForGas("gas_hydrogen_chloride") != 1
                || ExhaustUnitsForGas("gas_ammonia") != 1)
                p.Add("HCl/氨气应记 1 个嫌疑单位");
            foreach (var g in new[] { "gas_oxygen", "gas_nitrogen", "gas_hydrogen",
                                      "gas_carbon_dioxide", "gas_ethylene", "gas_nitrous_oxide" })
                if (ExhaustUnitsForGas(g) != 0)
                    p.Add($"无毒气体 {g} 被计入排气嫌疑度");
            if (IsToxicExhaustGas("liq_hydrochloric_acid"))
                p.Add("液体盐酸被误判为毒性气体排放");
            // ── P3f：热反应表结构/可达性断言（docs/chemistry-audit.md）──
            var thermalIds = new HashSet<string>();
            foreach (var tr in ThermalReactions)
            {
                if (!thermalIds.Add(tr.Id)) p.Add($"[热反应] id 重复: {tr.Id}");
                if (tr.Inputs.Length == 0 || tr.Outputs.Length == 0)
                    p.Add($"[热反应] {tr.Id} inputs/outputs 不能为空");
                if (tr.MinTemp < RoomTemp)
                    p.Add($"[热反应] {tr.Id} 起始温度 {tr.MinTemp} 低于室温 {RoomTemp}");
                if (tr.MinTemp > tr.MaxTemp)
                    p.Add($"[热反应] {tr.Id} 温区倒置（min {tr.MinTemp} > max {tr.MaxTemp}）");
                if (tr.MaxTemp > tr.DegradeTemp)
                    p.Add($"[热反应] {tr.Id} 降解温度 {tr.DegradeTemp} 必须 ≥ 温区上限 {tr.MaxTemp}");
                // 加热台高档目标温度 160°C 是全局上限：起始温度必须 ≤160，否则该热反应不可达
                if (tr.MinTemp > 160f)
                    p.Add($"[热反应] {tr.Id} 起始温度 {tr.MinTemp} > 加热台高档上限 160°C，永远无法触发");
                if (string.IsNullOrEmpty(tr.Equation) || tr.Equation.IndexOf('→') < 0)
                    p.Add($"[热反应] {tr.Id} equation 缺失或不含「→」");
                if (ChemRecipes.SpecExists != null)
                {
                    foreach (var i in tr.Inputs)
                        if (!ChemRecipes.SpecExists(i) && !ChemRecipes.VanillaAnalog.ContainsKey(i))
                            p.Add($"[热反应] {tr.Id} 输入 {i} 未注册进 Specs");
                    foreach (var o in tr.Outputs)
                        if (!ChemRecipes.SpecExists(o))
                            p.Add($"[热反应] {tr.Id} 输出 {o} 未注册进 Specs");
                }
            }
            bool lowBand = false, highBand = false;
            foreach (var tr in ThermalReactions)
            {
                if (tr.MinTemp <= 99f && tr.MaxTemp >= 99f) lowBand = true;
                if (tr.MinTemp >= 140f) highBand = true;
            }
            if (!lowBand) p.Add("没有任何热反应能在低档 99°C 完成");
            if (!highBand) p.Add("没有任何热反应需要高档 160°C（高档档位形同虚设）");

            return p;
        }
    }
}
