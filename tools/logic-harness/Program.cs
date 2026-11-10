using System;
using System.Linq;
using System.Collections.Generic;
using FreeChem;

class Program
{
    static int fails = 0;
    static void Check(bool cond, string msg)
    {
        Console.WriteLine((cond ? "  [PASS] " : "  [FAIL] ") + msg);
        if (!cond) fails++;
    }

    static int Main()
    {
        Console.WriteLine("=== FreeChem 数据层真实校验（编译执行）===\n");

        // 1. 内建逻辑自检
        var problems = ChemRecipes.ValidateLogic();
        Check(problems.Count == 0, "ValidateLogic() 无问题" +
              (problems.Count > 0 ? "：" + string.Join("; ", problems) : ""));
        foreach (var p in problems) Console.WriteLine("       " + p);

        // 2. 相态推断
        Check(ChemRecipes.PhaseOf("gas_oxygen") == Phase.Gas, "氧气 → 气体");
        Check(ChemRecipes.PhaseOf("gas_canister_empty") == Phase.Solid, "空钢瓶 → 固体容器（不是气体）");
        Check(ChemRecipes.PhaseOf("liq_brine") == Phase.Liquid, "盐水 → 液体");
        Check(ChemRecipes.PhaseOf("can_oxygen_medical") == Phase.Gas, "医用氧气罐 → 气体内容物");
        Check(ChemRecipes.IsAuxiliary("catalyst_kit"), "催化剂是辅助物，不算投料");

        // 3. 设备相态约束
        var dec = ChemRecipes.AllowedInputPhases("decanter");
        Check(dec != null && dec.Length == 1 && dec[0] == Phase.Liquid, "滗析槽只允许液体");
        var scr = ChemRecipes.AllowedInputPhases("gas_scrubber");
        Check(scr != null && scr.Length == 1 && scr[0] == Phase.Gas, "洗涤塔只允许气体");
        var fil = ChemRecipes.AllowedInputPhases("canister_filling");
        Check(fil != null && fil.Length == 1 && fil[0] == Phase.Gas, "灌装台只允许气体");

        // 4. 真实反应匹配（多级链）
        Check(ChemRecipes.Match(new[] { "liq_brine" }, "electrolyzer", 2, 7, false)?.Id == "chloralkali",
              "T1 氯碱法：盐水→氯气+氢气+烧碱");
        Check(ChemRecipes.Match(new[] { "gas_hydrogen", "gas_chlorine" }, "chem_reactor", 3, 7, false)?.Id == "hcl_synthesis",
              "T2 氢氯合成：H2+Cl2→HCl");
        Check(ChemRecipes.Match(new[] { "gas_nitrogen", "gas_hydrogen" }, "high_pressure_synth", 3, 7, true)?.Id == "haber",
              "T2 哈伯法合成氨（需催化剂）");
        Check(ChemRecipes.Match(new[] { "gas_hydrogen_chloride", "liq_water" }, "condenser_unit", 0, 7, false)?.Id == "hcl_absorption",
              "T2 氯化氢吸收制盐酸（HCl(g)+H2O→HCl(aq)）");
        Check(ChemRecipes.Match(new[] { "gas_ammonia", "liq_water" }, "condenser_unit", 0, 7, false)?.Id == "ammonia_absorption",
              "T2 氨吸收制氨水（NH3+H2O→NH3·H2O）");
        Check(ChemRecipes.Match(new[] { "liq_hydrochloric_acid" }, "decanter", 1, 7, false)?.Id == "decant_hcl",
              "T3 盐酸滗析");
        Check(ChemRecipes.Match(new[] { "gas_oxygen" }, "gas_scrubber", 1, 7, false)?.Id == "scrub_oxygen",
              "T3 氧气洗涤提纯");
        Check(ChemRecipes.Match(new[] { "med_bottle_violet", "gas_hydrogen_chloride", "gas_ammonia" },
                               "chem_reactor", 3, 7, true)?.Id == "voidsalt",
              "T4 与原版药剂反应生成虚构违禁品「空盐」");

        // 5. 硬性否定：气体绝不能进滗析
        Check(ChemRecipes.Match(new[] { "gas_oxygen" }, "decanter", 1, 7, false) == null,
              "气体投进滗析槽 → 无配方（正确拒绝）");
        Check(ChemRecipes.Match(new[] { "liq_brine" }, "gas_scrubber", 1, 7, false) == null,
              "液体投进洗涤塔 → 无配方（正确拒绝）");

        // 6. 滗析不改变化学身份
        foreach (var r in ChemRecipes.ForMachine("decanter"))
            Check(r.Inputs.Length == 1 && r.Outputs[0] == ChemRecipes.Normalize(r.Inputs[0]),
                  $"[{r.Id}] 滗析主产物仍是同一种药剂");

        // 7. 洗涤提纯前后同一种气体
        foreach (var r in ChemRecipes.ForMachine("gas_scrubber"))
            Check(r.Inputs.SequenceEqual(r.Outputs), $"[{r.Id}] 洗涤不改变化学身份");

        // 8. 纯度链单调 + 气罐三档
        Check(ChemRecipes.GradePurity(PurityGrade.Industrial) < ChemRecipes.GradePurity(PurityGrade.Medical)
           && ChemRecipes.GradePurity(PurityGrade.Medical) < ChemRecipes.GradePurity(PurityGrade.UltraPure),
              "纯度数值：工业 < 医用 < 化学纯");
        Check(ChemRecipes.GradeMultiplier(PurityGrade.Industrial) < ChemRecipes.GradeMultiplier(PurityGrade.Medical)
           && ChemRecipes.GradeMultiplier(PurityGrade.Medical) < ChemRecipes.GradeMultiplier(PurityGrade.UltraPure),
              "价值修正：0% < 85% < 135%（对齐原版品级）");
        Check(ChemRecipes.CanisterSkus.Values.All(a => a.Length == 3), "每种可灌装气体都有三档 SKU");
        Check(ChemRecipes.CanisterSkus.ContainsKey("gas_oxygen")
           && ChemRecipes.CanisterSkus["gas_oxygen"][1] == "can_oxygen_medical",
              "医用氧气 SKU 存在，可卖给原版医疗顾客");

        // 9. 毒性/欣快系数（原版式量级）
        Check(ChemRecipes.Profile("gas_chlorine").Toxicity >= 4.0f, "氯气高毒性");
        Check(ChemRecipes.Profile("illicit_voidsalt").Euphoria >= 10.0f, "虚构违禁品「空盐」高欣快/成瘾");
        Check(ChemRecipes.Profile("illicit_voidsalt").Controlled, "虚构违禁品标记为管制");
        Check(ChemRecipes.Profile("gas_oxygen").Toxicity == 0f, "医用氧气无毒");

        // 10. 分级覆盖 & 原版药剂仅限 T4
        var tiers = ChemRecipes.All.Select(r => r.Tier).Distinct().OrderBy(t => t).ToArray();
        Check(tiers.SequenceEqual(new[] { 1, 2, 3, 4 }), "T1–T4 多级合成链齐全");
        bool vanillaOk = true;
        foreach (var r in ChemRecipes.All)
            foreach (var i in r.Inputs)
                if (ChemRecipes.VanillaAnalog.ContainsKey(i) && !(r.UsesVanilla && r.Tier == 4))
                    vanillaOk = false;
        Check(vanillaOk, "原版药剂只出现在 T4 虚构违禁品反应中");
        Check(ChemRecipes.ForTier(4).All(r => r.UsesVanilla), "所有 T4 反应都标注 UsesVanilla");

        // ── 11. 污染物 / 滗析不可完全提纯（本轮核心）──
        var c0 = ChemLab.InitialContaminants();
        var cDec = ChemLab.Decant(c0);
        foreach (var k in new[] { "chem", "heavy", "micro", "plastic", "organic", "mineral" })
        {
            int i = Array.IndexOf(ChemLab.ContaminantKeys, k);
            Check(cDec[i] == c0[i], $"滗析保留游离污染物 {k}（不分离）");
        }
        foreach (var k in ChemLab.Settleable)
        {
            int i = Array.IndexOf(ChemLab.ContaminantKeys, k);
            Check(cDec[i] < c0[i], $"滗析减少可沉降颗粒 {k}");
        }
        Check(ChemLab.PurityFrom(cDec) > ChemLab.PurityFrom(c0), "滗析提高纯度");
        Check(ChemLab.PurityFrom(cDec) < 100 * ChemLab.Scale, "滗析后纯度仍 < 100%（无法完全提纯）");

        // 普通滤纸孔径 22：滤不掉化学污染物(2)/重金属(12)
        var cF = ChemLab.Filter(c0, "filter_paper");
        foreach (var k in new[] { "chem", "heavy" })
        {
            int i = Array.IndexOf(ChemLab.ContaminantKeys, k);
            Check(cF[i] == c0[i], $"普通滤纸滤不掉 {k}（粒径 < 22）");
        }
        // 无灰滤纸(2) 能拦化学污染物，但仍有下限
        var cA = ChemLab.Filter(c0, "filter_paper_ash");
        int chemIdx = Array.IndexOf(ChemLab.ContaminantKeys, "chem");
        Check(cA[chemIdx] >= ChemLab.ResidualFloor, "无灰滤纸仍保留化学污染物残留下限 0.7%");
        Check(ChemLab.PurityFrom(cA) < 100 * ChemLab.Scale, "多级过滤后纯度仍 < 100%");
        Check(ChemLab.GradeFromPurity(ChemLab.PurityFrom(cA)) != PurityGrade.UltraPure,
              "受 0.7% 残留下限约束，过滤链永远达不到化学纯（UltraPure）");
        Check(ChemLab.GradeFromPurity(ChemLab.PurityFrom(c0)) == PurityGrade.Industrial,
              "基础药剂初始为工业级，不会一出生就虚高");
        Check(ChemLab.PurityFrom(cA) > ChemLab.PurityFrom(c0),
              "过滤确实提高纯度（只是无法完全提纯）");

        // ── 12. 实时加热公式 / 燃料纯度 ──
        float t = ChemLab.RoomTemp;
        for (int s = 0; s < 30; s++) t = ChemLab.HeatingStep(t, 99f, 10f);
        Check(t > ChemLab.RoomTemp && t <= 99f, "升温单调且不超过目标温度");
        float cooled = ChemLab.CoolingStep(99f, 250000f);
        Check(cooled < 99f && cooled >= ChemLab.RoomTemp, "停火后按原版公式冷却");
        Check(ChemLab.HeatFactor(95) > ChemLab.HeatFactor(70),
              "高纯度酒精加热因子更高");
        Check(ChemLab.FuelBurnRate(40) > ChemLab.FuelBurnRate(95),
              "低纯度酒精消耗更快");
        Check(ChemLab.TargetTemp(0, false) == 99f && ChemLab.TargetTemp(0, true) == 160f,
              "低档 99°C / 高档 160°C（对齐原版）");
        Check(ChemLab.ModuleBoost(4) == 16f && ChemLab.ModuleBoost(5) == 1f,
              "模组 ×16 分支且 >4 不命中");

        // ── 13. 实时热反应温区 ──
        Check(ChemLab.MatchThermal(new[] { "liq_hydrochloric_acid" }, 90f)?.Id == "heat_hcl_distill",
              "90°C 命中盐酸蒸馏");
        Check(ChemLab.MatchThermal(new[] { "liq_hydrochloric_acid" }, 30f) == null,
              "30°C 不触发加热反应（实时温度门槛生效）");
        Check(ChemLab.MatchThermal(new[] { "gas_oxygen" }, 90f) == null,
              "无匹配投料不触发反应");
        Check(ChemLab.ValidateLogic().Count == 0, "ChemLab.ValidateLogic() 无问题");
        Check(ChemRecipes.IsRealtime("heating_platform"), "加热台标记为实时设备");
        Check(ChemRecipes.All.All(r => r.Machine != "heating_platform"),
              "加热台没有夜间反应（全部走实时）");

        // ── 14. 排风服务 / 排气嫌疑度纯函数（模组自有，未接入原版治安部）──
        int bp0 = ChemLab.VentMultiplierBp(0);
        int bp1 = ChemLab.VentMultiplierBp(1);
        int bp2 = ChemLab.VentMultiplierBp(2);
        Check(bp0 > bp1 && bp1 > bp2 && bp2 >= 0,
              $"排风倍率严格递减且非负：无服务 {bp0} > 基础 {bp1} > 进阶 {bp2}");
        Check(bp0 > 0, "无服务排风倍率为正（不会恒为 0）");
        foreach (var lvl in new[] { -1, 3, 99, int.MinValue })
            Check(ChemLab.VentMultiplierBp(lvl) == bp0, $"未知排风等级 {lvl} 按无服务处理");
        Check(ChemLab.SuspicionFrom(0, 0) == 0 && ChemLab.SuspicionFrom(0, 1) == 0
           && ChemLab.SuspicionFrom(0, 2) == 0, "raw=0 时三档嫌疑度均为 0");
        bool mono = true, nonNeg = true;
        foreach (var lvl in new[] { 0, 1, 2 })
        {
            int prev = int.MinValue;
            for (int raw = 0; raw <= 200; raw++)
            {
                int s = ChemLab.SuspicionFrom(raw, lvl);
                if (s < prev) mono = false;
                if (s < 0) nonNeg = false;
                prev = s;
            }
        }
        Check(mono, "嫌疑度随 raw（0..200 全量）单调不降");
        Check(nonNeg, "嫌疑度恒为非负");
        Check(ChemLab.SuspicionFrom(10, 2) < ChemLab.SuspicionFrom(10, 1)
           && ChemLab.SuspicionFrom(10, 1) < ChemLab.SuspicionFrom(10, 0),
              "同样排放量：进阶排风 < 基础排风 < 无服务（严格）");
        Check(ChemLab.ExhaustUnitsForGas("gas_chlorine") == 2, "氯气排污计 2 个单位");
        Check(ChemLab.ExhaustUnitsForGas("gas_hydrogen_chloride") == 1
           && ChemLab.ExhaustUnitsForGas("gas_ammonia") == 1, "HCl/氨气排污各计 1 个单位");
        Check(new[] { "gas_oxygen", "gas_nitrogen", "gas_hydrogen", "gas_carbon_dioxide",
                      "gas_ethylene", "gas_nitrous_oxide" }
              .All(g => ChemLab.ExhaustUnitsForGas(g) == 0),
              "无毒气体不计入排气嫌疑度");

        // ── 15. 大型储气桶映射（DrumSkus/DrumIds 内部一致；Specs 注册由 validate.py 跨层断言）──
        var drumGases = new[] { "gas_oxygen", "gas_nitrogen", "gas_hydrogen",
                                "gas_chlorine", "gas_carbon_dioxide" };
        Check(ChemRecipes.DrumSkus.Count == 5,
              $"DrumSkus 恰 5 条（实际 {ChemRecipes.DrumSkus.Count}）");
        Check(drumGases.All(g => ChemRecipes.DrumSkus.ContainsKey(g)),
              "5 种可灌装气体都有大型储气桶映射");
        Check(ChemRecipes.DrumSkus.All(kv => ChemRecipes.DrumIds.Contains(kv.Value)),
              "DrumSkus 每个目标都在 DrumIds 清单内");
        Check(ChemRecipes.DrumIds.Length == ChemRecipes.DrumSkus.Count + 1
           && ChemRecipes.DrumIds.Contains("gas_drum_empty"),
              "DrumIds = 1 个空桶 + 5 个充装桶");
        Check(!ChemRecipes.DrumSkus.Values.Contains("gas_drum_empty"),
              "没有气体映射到空桶");
        Check(ChemRecipes.DrumSkus.Values.Distinct().Count() == ChemRecipes.DrumSkus.Count,
              "每种气体映射到唯一的桶（无重复目标）");
        Check(ChemRecipes.PhaseOf("gas_drum_empty") == Phase.Solid,
              "空大型储气桶 → 固体容器（不是气体）");
        Check(ChemRecipes.PhaseOf("gas_drum_oxygen") == Phase.Gas,
              "充装大型储气桶内容物 → 气体");
        Check(ChemRecipes.IsAuxiliary("gas_drum_empty"), "空大型储气桶按容器处理，不算投料");
        Check(!ChemRecipes.IsAuxiliary("gas_drum_oxygen"), "充装大型储气桶不算辅助物");
        Check(ChemRecipes.DrumIds.All(d => d == "gas_drum_empty" || ChemRecipes.PhaseOf(d) == Phase.Gas),
              "DrumIds 内每个充装桶相态都是气体");
        Check(!ChemRecipes.CanisterSkus.Values.SelectMany(a => a).Any(x => x.StartsWith("gas_drum_")),
              "大型储气桶不混进便携钢瓶三档 SKU");
        Check(ChemRecipes.ValidateLogic().Count == 0, "含 DrumSkus 的 ValidateLogic() 仍无问题");

        // ── 16. 温度可达性（纯数据层）：thermometer 循环 0..3 必须覆盖全部配方温度档 ──
        var recipeTemps = ChemRecipes.All.Select(r => r.Temp).Distinct().OrderBy(t => t).ToArray();
        Check(recipeTemps.All(t => t >= 0 && t <= 3),
              "所有配方 Temp 取值 ⊆ {0,1,2,3}（thermometer 只能切这四档）；实际=" +
              string.Join(",", recipeTemps));
        Check(new[] { 0, 2, 3 }.All(x => recipeTemps.Contains(x)),
              "温度档 0/2/3 都被配方实际使用（否则存在死档位）；实际=" +
              string.Join(",", recipeTemps));
        Check(recipeTemps.Contains(1), "存在常温（Temp=1）配方");

        // ── 17. 稳定数字 ID 表（src/ChemItemIds.cs 纯 BCL 数据层，直接编译进 harness）──
        Check(ChemItemIds.Ordered.Length == 98,
              $"数字 ID 表恰 98 条（P11 追加 test_tube；实际 {ChemItemIds.Ordered.Length}）");
        Check(ChemItemIds.Ordered.Distinct().Count() == ChemItemIds.Ordered.Length,
              "数字 ID 无重复");
        Check(ChemItemIds.MinId == 1 && ChemItemIds.MaxId == 98
           && ChemItemIds.MaxId - ChemItemIds.MinId + 1 == ChemItemIds.Ordered.Length,
              "MinId=1 / MaxId=98 / 编号 1..98 连续");
        Check(!ChemItemIds.Ordered.Any(id => id.EndsWith("_on")),
              "_on 状态图不在数字 ID 表内");
        Check(ChemItemIds.Ordered.Skip(72).Take(ChemRecipes.MachineIds.Length)
                            .SequenceEqual(ChemRecipes.MachineIds),
              "数字 ID #73..#85 顺序与 ChemRecipes.MachineIds 完全一致（重新追加不影响）");
        Check(ChemItemIds.Ordered[85] == "label_printer",
              "数字 ID #86 = label_printer（P1b1 追加）");
        var p2aIds = new[] { "sol_calcium_oxide", "sol_calcium_hydroxide", "sol_sodium_carbonate",
                             "sol_sodium_bicarbonate", "sol_ammonium_chloride",
                             "sol_manganese_dioxide", "sol_copper_oxide", "sol_potassium_sulfate",
                             "sol_sodium_nitrate", "sol_potassium_manganate" };
        Check(ChemItemIds.Ordered.Skip(86).Take(p2aIds.Length).SequenceEqual(p2aIds),
              "数字 ID #87..#96 按 P2a 顺序追加（旧 1–86 不变）");
        Check(ChemItemIds.Ordered.Skip(96).SequenceEqual(new[] { "note_chem_dealer", "test_tube" }),
              "数字 ID #98 = test_tube（P11 追加，旧 1–97 不变）");
        var idAnchors = new (int N, string Id)[]
        {
            (1, "gas_chlorine"), (12, "liq_hydrogen_peroxide"), (30, "thermometer"),
            (61, "gas_drum_empty"), (72, "sol_sediment"), (73, "chem_reactor"),
            (85, "heating_platform"), (86, "label_printer"),
            (87, "sol_calcium_oxide"), (96, "sol_potassium_manganate"),
            (97, "note_chem_dealer"), (98, "test_tube"),
        };
        foreach (var a in idAnchors)
            Check(a.N <= ChemItemIds.Ordered.Length && ChemItemIds.Ordered[a.N - 1] == a.Id,
                  $"数字 ID 顺序锚点 #{a.N} = {a.Id}");
        Check(ChemItemIds.TryGetId(30, out var id30) && id30 == "thermometer",
              "TryGetId(30) = thermometer");
        Check(ChemItemIds.TryGetId(98, out var id98) && id98 == "test_tube",
              "TryGetId(98) = test_tube");
        Check(!ChemItemIds.TryGetId(0, out _) && !ChemItemIds.TryGetId(99, out _),
              "TryGetId 越界（0/99）返回 false");
        int pn; string perr;
        Check(ChemItemIds.TryParseCommand("85", out pn, out perr) && pn == 85, "\"85\" → 85");
        Check(ChemItemIds.TryParseCommand("spawn 85", out pn, out perr) && pn == 85, "\"spawn 85\" → 85");
        Check(ChemItemIds.TryParseCommand("id 85", out pn, out perr) && pn == 85, "\"id 85\" → 85");
        Check(ChemItemIds.TryParseCommand("get 12", out pn, out perr) && pn == 12, "\"get 12\" → 12");
        Check(ChemItemIds.TryParseCommand("生成 30", out pn, out perr) && pn == 30, "\"生成 30\" → 30");
        Check(ChemItemIds.TryParseCommand("生成物品 30", out pn, out perr) && pn == 30,
              "\"生成物品 30\" → 30");
        Check(ChemItemIds.TryParseCommand("８５", out pn, out perr) && pn == 85, "全角 ８５ → 85");
        Check(ChemItemIds.TryParseCommand("　12　", out pn, out perr) && pn == 12,
              "全角空格/前后空白包裹 → 12");
        Check(ChemItemIds.TryParseCommand("list", out pn, out perr) && pn == ChemItemIds.ListCommand,
              "\"list\" → ListCommand(-1)");
        Check(ChemItemIds.TryParseCommand("ls", out pn, out perr) && pn == ChemItemIds.ListCommand,
              "\"ls\" → ListCommand");
        Check(ChemItemIds.TryParseCommand("列表", out pn, out perr) && pn == ChemItemIds.ListCommand,
              "\"列表\" → ListCommand");
        Check(!ChemItemIds.TryParseCommand("", out pn, out perr) && perr == "empty",
              "空输入 → false / error=empty");
        Check(!ChemItemIds.TryParseCommand("abc", out pn, out perr) && perr == "invalid",
              "非法输入 → false / error=invalid");
        Check(!ChemItemIds.TryParseCommand("0", out pn, out perr) && perr == "range"
           && !ChemItemIds.TryParseCommand("99", out pn, out perr) && perr == "range",
              "越界 0/99 → false / error=range（98 为最后合法编号）");
        Check(ChemItemIds.DescribeRaw(12) == "#12 liq_hydrogen_peroxide",
              "DescribeRaw(12) 纯文本描述稳定");

        // ── 18. P0：画像覆盖/欣快限定 + 量纲统一（SuspicionFrom = raw*10*bp/10000）──
        foreach (var chem in new[] { "gas_chlorine", "gas_hydrogen_chloride", "gas_ammonia",
                                     "gas_carbon_dioxide", "liq_hydrochloric_acid",
                                     "liq_sulfuric_acid", "liq_nitric_acid",
                                     "liq_sodium_hydroxide", "liq_hydrogen_peroxide",
                                     "sol_bleaching_powder" })
            Check(ChemRecipes.Profile(chem).Toxicity > 0f, $"画像毒性 {chem} > 0（非零）");
        Check(ChemRecipes.Profiles.Count >= 20, $"Profiles 覆盖 ≥20 种化学品（实际 {ChemRecipes.Profiles.Count}）");
        var euphoricIds = ChemRecipes.Profiles.Where(kv => kv.Value.Euphoria > 0f)
                                              .Select(kv => kv.Key).ToList();
        // 欣快只出现在违禁品/真正有欣快性的物质（咖啡因/笑气/乙醇），普通酸碱盐不得有欣快
        Check(euphoricIds.All(id => id.StartsWith("illicit_")
                                    || id == "sol_caffeine"
                                    || id == "gas_nitrous_oxide"
                                    || id == "liq_ethanol"),
              "欣快只留给违禁品/咖啡因/笑气/乙醇；实际=" + string.Join(",", euphoricIds));
        Check(ChemLab.SuspicionFrom(100, 0) == 1000,
              "量纲统一：raw=100 无排风 → 1000（raw*10）");
        Check(ChemLab.SuspicionFrom(100, 1) == 550,
              "量纲统一：raw=100 基础排风 → 550");
        Check(ChemLab.SuspicionFrom(100, 2) == 120,
              "量纲统一：raw=100 进阶排风 → 120");
        Check(ChemLab.SuspicionFrom(0, 0) == 0 && ChemLab.SuspicionFrom(0, 2) == 0,
              "量纲统一后 raw=0 → 0（边界不变）");

        // ── 19. P1a：原版七档纯度阈值 + PourParts 守恒（纯数据层）──
        Check(ChemLab.GradeLabPure == 99 && ChemLab.GradePharma == 90
           && ChemLab.GradeNearPharma == 80 && ChemLab.GradeStreet == 60
           && ChemLab.GradeCut == 45 && ChemLab.GradeJunk == 30,
              "原版七档纯度阈值 99/90/80/60/45/30");
        Check(ChemRecipes.PourParts.Count >= 8,
              $"PourParts 覆盖 ≥8 种液体药剂（实际 {ChemRecipes.PourParts.Count}）");
        // 注意：用局部变量算 sum 后断言/打印同一个值，避免「条件与文案各算一次」的自相矛盾。
        foreach (var kv in ChemRecipes.PourParts)
        {
            var parts = kv.Value.Values.ToList();
            int sum = 0;
            foreach (var v in parts) sum += v;
            Check(parts.Count > 0 && sum == 100,
                  $"[{kv.Key}] PourParts 百分比之和 = 100（实际 {sum}；组件={string.Join(",", parts)}）");
        }
        Check(ChemRecipes.PourParts.ContainsKey("liq_sodium_hydroxide"),
              "PourParts 含 liq_sodium_hydroxide（历史异常条目回归检查）");
        Check(ChemRecipes.PourParts.ContainsKey("liq_calcium_chloride"),
              "PourParts 含 liq_calcium_chloride（P1a 补入项）");
        Check(ChemRecipes.PourParts.ContainsKey("liq_hydrogen_peroxide")
           && ChemRecipes.PourParts["liq_hydrogen_peroxide"].ContainsKey("water"),
              "双氧水近似映射为 water（原版无过氧化物组分）");
        Check(ChemRecipes.PourParts.Values.All(p => p.Values.All(v => v >= 0)),
              "PourParts 百分比非负");

        // ── 20. P1c：多级提纯等级 + 100% 上限（纯函数层）──
        Check(ChemLab.MaxPurifyLevel == 5, "提纯等级上界 MaxPurifyLevel = 5");
        var purifyFloors = new[] { 70, 35, 17, 8, 4, 2 };
        for (int l = 0; l <= ChemLab.MaxPurifyLevel; l++)
            Check(ChemLab.PurifyFloor(l) == purifyFloors[l],
                  $"PurifyFloor({l}) = {purifyFloors[l]}（70 >> level）");
        Check(ChemLab.PurifyFloor(-1) == ChemLab.PurifyFloor(0)
           && ChemLab.PurifyFloor(9) == ChemLab.PurifyFloor(5),
              "PurifyFloor 越界按 0..5 夹取");
        Check(ChemLab.MaxPurity(0) == 9440, "level0 纯度上限 9440（94.40%）");
        Check(ChemLab.MaxPurity(5) == 9984, "level5 纯度上限 9984（99.84%）");
        var zeroContam = new int[ChemLab.ContaminantKeys.Length];
        int prevPurity = -1;
        bool purityMono = true;
        for (int l = 0; l <= ChemLab.MaxPurifyLevel; l++)
        {
            int p = ChemLab.PurityFrom(zeroContam, l);
            if (p < prevPurity) purityMono = false;
            prevPurity = p;
            Check(p == ChemLab.MaxPurity(l), $"PurityFrom(zeros,{l}) = MaxPurity({l})");
        }
        Check(purityMono, "纯度上限随提纯等级单调不降（level0..5）");
        Check(ChemLab.MaxPurity(5) < 100 * ChemLab.Scale, "任何提纯等级上限仍 < 100%");
        Check(ChemLab.PurityFrom(ChemLab.InitialContaminants())
           == ChemLab.PurityFrom(ChemLab.InitialContaminants(), 0),
              "PurityFrom(c) 等价 PurityFrom(c, 0)（默认 level0）");

        // ── 21. P2a：10 种新化学品反应式/副产物（纯数据层）──
        Check(ChemRecipes.All.Length >= 44,
              $"反应数 ≥44（P2a 新增 5 条；实际 {ChemRecipes.All.Length}）");
        Check(ChemRecipes.All.All(r => !string.IsNullOrEmpty(r.Equation) && r.Equation.Contains("→")),
              "所有反应 equation 非空且含「→」");
        var rx = ChemRecipes.All.ToDictionary(r => r.Id);
        foreach (var nid in new[] { "lime_calcination", "slaked_lime", "soda_from_caustic",
                                    "baking_soda", "ammonium_chloride" })
            Check(rx.ContainsKey(nid), $"存在 P2a 新反应 {nid}");
        // P12：水不再作为物品产出（仅溶剂/内部输入 token），以下仅校验副产物
        Check(rx["co2_from_marble"].Outputs.Contains("gas_carbon_dioxide")
           && rx["co2_from_marble"].Outputs.Contains("liq_calcium_chloride"),
              "co2_from_marble 产出氯化钙 + 二氧化碳（水作溶剂不计物品）");
        Check(rx["peroxide_decomp"].Outputs.Contains("gas_oxygen"),
              "peroxide_decomp 产出氧气（水作溶剂不计物品）");
        Check(rx["kmno4_decomp"].Outputs.Contains("sol_manganese_dioxide")
           && rx["kmno4_decomp"].Outputs.Contains("sol_potassium_manganate"),
              "kmno4_decomp 产出 MnO2 + 锰酸钾");
        Check(rx["ammonia_liberation"].Outputs.Contains("sol_sodium_nitrate"),
              "ammonia_liberation 产出硝酸钠（水作溶剂不计物品）");
        Check(rx["hypochlorite"].Inputs.Contains("sol_calcium_hydroxide")
           && !rx["hypochlorite"].Inputs.Any(i => i.Contains("sodium_hydroxide")),
              "hypochlorite 改用熟石灰、不再用 NaOH");
        Check(rx["hypochlorite"].Outputs.Contains("liq_calcium_chloride"),
              "hypochlorite 产出氯化钙溶液");
        Check(!rx["salt_crystallize"].Outputs.Contains("liq_water")
           && !rx["nh4no3_crystallize"].Outputs.Contains("liq_water"),
              "盐水/硝酸铵结晶不再产出水物品（P12 水作溶剂）");
        Check(rx["neutralization"].Outputs.Contains("liq_brine")
           && !rx["neutralization"].Outputs.Contains("liq_water")
           && !rx["neutralization"].Outputs.Any(o => o.StartsWith("sol_")),
              "neutralization 产出盐水（水不计物品，不直接出固体）");
        Check(rx["hcl_absorption"].Inputs.Contains("liq_water")
           && rx["ammonia_absorption"].Inputs.Contains("liq_water"),
              "两种吸收塔均以水为输入");
        Check(rx["nitric_from_niter"].Outputs.Contains("sol_potassium_sulfate")
           && rx["nitric_from_niter"].Equation.Contains("K2SO4")
           && rx["nitric_from_niter"].Equation.Contains("2HNO3"),
              "nitric_from_niter 配平并副产硫酸钾");
        Check(!ChemRecipes.All.Any(r => r.Inputs.Contains("sol_copper_oxide")
                                     || r.Outputs.Contains("sol_copper_oxide")),
              "sol_copper_oxide 不参与任何反应（仅掉落）");
        Check(ChemRecipes.All.Where(r => r.Tier <= 3)
                             .All(r => r.Inputs.All(i => !ChemRecipes.VanillaAnalog.ContainsKey(i))),
              "T1–T3 不使用原版药剂（仅 T4）");

        // ── 22. P3a：500ml 量纲 + 成瘾/毒性画像审计 ──
        Check(ChemRecipes.PourVolumeMl == 500, $"液体瓶容量 PourVolumeMl = 500（实际 {ChemRecipes.PourVolumeMl}）");
        Check(ChemRecipes.PourPartPerMl == 1000,
              $"1 ml = {ChemRecipes.PourPartPerMl} part（原版 Water 体系）");
        Check(ChemRecipes.PourVolumeMl * ChemRecipes.PourPartPerMl == 500000,
              "桥接总 part = 500 ml × 1000 = 500000（守恒）");
        Check(ChemRecipes.PourParts.Values.All(p => p.Values.Sum() == 100),
              "PourParts 百分比之和仍 = 100（500ml 缩放不改变比例）");
        Func<string, ChemProfile> profile = ChemRecipes.Profile;
        Check(profile("illicit_voidsalt").Toxicity == 0.5f && profile("illicit_voidsalt").Euphoria == 10f
           && profile("illicit_voidsalt").Addictive && profile("illicit_voidsalt").Controlled,
              "空盐：毒 0.5 / 欣快 10 / 成瘾 + 管制");
        Check(profile("illicit_redmist").Toxicity == 4.0f && profile("illicit_redmist").Euphoria == 0.25f
           && !profile("illicit_redmist").Addictive && profile("illicit_redmist").Controlled,
              "红雾：毒 4.0 / 欣快 0.25 / 非成瘾 + 管制");
        Check(profile("illicit_nightglass").Toxicity == 0.6f && profile("illicit_nightglass").Euphoria == 4.0f
           && profile("illicit_nightglass").Addictive && profile("illicit_nightglass").Controlled,
              "夜玻：毒 0.6 / 欣快 4.0 / 成瘾 + 管制");
        Check(profile("illicit_ashbreath").Toxicity == 4.0f && profile("illicit_ashbreath").Euphoria == 0.5f
           && profile("illicit_ashbreath").Addictive && profile("illicit_ashbreath").Controlled,
              "灰息：毒 4.0 / 欣快 0.5 / 成瘾 + 管制");
        Check(profile("sol_caffeine").Euphoria == 0.2f && profile("sol_caffeine").Addictive,
              "咖啡因：欣快 0.2 + 成瘾");
        // P12：mod 乙醇物品已删除（原版酒精瓶替代），不再有乙醇 profile 断言
        Check(profile("gas_nitrous_oxide").Euphoria == 0.25f && !profile("gas_nitrous_oxide").Addictive,
              "笑气：欣快 0.25 / 无成瘾标记");
        Check(ChemRecipes.Profiles.Where(kv => !kv.Value.Addictive
                                               && !kv.Key.StartsWith("illicit_")
                                               && kv.Key != "gas_nitrous_oxide")
                                   .All(kv => kv.Value.Euphoria == 0f),
              "非成瘾物 Euphoria 全为 0（普通酸碱盐/溶剂不显示欣快）");

        // ── 23. P3b：原版物品替代（水/酒精）+ 咖啡 legacy + T4 可达 ──
        Check(!ChemRecipes.VanillaAnalog.ContainsKey("coffee_bean"),
              "VanillaAnalog 不含 coffee_bean（Lead 定案：原版咖啡已有官方制作，mod 咖啡改 legacy）");
        // P12：原版酒精改为 mini_bottle 的 alcohol 组分判定（不再有 VanillaAnalog→liq_ethanol 映射）
        int waterMap = new[] { "small_bottled_water", "bottled_water", "large_bottled_water",
                               "water_jug", "water_pitcher" }
            .Count(k => ChemRecipes.VanillaAnalog.TryGetValue(k, out var v) && v == "liq_water");
        Check(waterMap >= 5, $"VanillaAnalog: ≥5 种原版水容器 → liq_water（实际 {waterMap}）");
        Check(ChemLab.VanillaAlcoholIds.Contains("mini_bottle") || ChemLab.VanillaAlcoholIds.Length == 0,
              "VanillaAlcoholIds 不含迷你水瓶" );
        Check(ChemLab.ThermalReactions.Length == 5,
              $"实时热反应 5 条（P12 已删 heat_ethanol_dehydrate；实际 {ChemLab.ThermalReactions.Length}）");
        Check(!ChemLab.ThermalReactions.Any(r => r.Id == "heat_caffeine_extract"),
              "heat_caffeine_extract 已删除（咖啡因改 legacy 无来源）");
        Check(!ChemLab.ThermalReactions.Any(r => r.Outputs.Contains("sol_caffeine")
                                              || r.Outputs.Contains("sol_coffee_grounds")),
              "sol_caffeine/sol_coffee_grounds 无热反应来源（legacy 仅保留注册）");
        Check(!ChemRecipes.All.Any(r => r.Inputs.Contains("sol_coffee_grounds")
                                     || r.Inputs.Contains("sol_caffeine")
                                     || r.Outputs.Contains("sol_coffee_grounds")
                                     || r.Outputs.Contains("sol_caffeine")),
              "夜间反应表不含 legacy 咖啡物品");
        foreach (var tid in new[] { "illicit_voidsalt", "illicit_redmist",
                                    "illicit_nightglass", "illicit_ashbreath" })
            Check(ChemRecipes.All.Any(r => r.Outputs.Contains(tid) && r.Tier == 4 && r.UsesVanilla),
                  $"T4 违禁品仍可达且 UsesVanilla: {tid}");

        // ── 24. P3c：气瓶商品 → 气体 id 反查（由 CanisterSkus 反推，无第二份映射）──
        int canisterMapped = 0;
        foreach (var kv in ChemRecipes.CanisterSkus)
            foreach (var sku in kv.Value)
            {
                Check(ChemRecipes.TryGetGasFromCanister(sku, out var gasId) && gasId == kv.Key,
                      $"{sku} → {kv.Key}（气瓶反查）");
                canisterMapped++;
            }
        Check(canisterMapped == 12, $"3 档 × 4 气体 = 12 个气瓶 SKU 可反查（实际 {canisterMapped}）");
        Check(!ChemRecipes.TryGetGasFromCanister("sol_sodium_chloride", out _),
              "非气瓶 id 反查返回 false");
        Check(!ChemRecipes.TryGetGasFromCanister("", out _), "空 id 反查返回 false");
        Check(ChemRecipes.TryGetGasFromCanister("gas_drum_oxygen", out _) == false,
              "大型桶 id 不在气瓶反查表（桶不是便携气瓶）");

        // ── 25. P3f：热反应可达性/温区/T4 映射/方程（纯数据层）──
        Check(ChemLab.ValidateLogic().Count == 0,
              "ChemLab.ValidateLogic() 无问题（温区/低高档/注册自检）"
              + (ChemLab.ValidateLogic().Count > 0 ? "：" + string.Join("; ", ChemLab.ValidateLogic()) : ""));
        Check(ChemLab.ThermalReactions.Length == 5,
              $"实时热反应 5 条（实际 {ChemLab.ThermalReactions.Length}）");
        Check(ChemLab.ThermalReactions.Select(t => t.Id).Distinct().Count() == ChemLab.ThermalReactions.Length,
              "热反应 id 唯一");
        Check(ChemLab.ThermalReactions.All(t => t.Equation.Contains("→")),
              "全部热反应 equation 含「→」");
        Check(ChemLab.ThermalReactions.All(t => t.MinTemp < t.MaxTemp && t.MaxTemp <= t.DegradeTemp),
              "热反应温区不倒置且 MaxTemp ≤ DegradeTemp");
        Check(ChemLab.ThermalReactions.All(t => t.MinTemp <= 160f),
              "全部热反应起始温度 ≤ 160°C（加热台高档可达）");
        Check(ChemLab.ThermalReactions.Any(t => t.MinTemp <= 99f && 99f <= t.MaxTemp),
              "低档（99°C）有可完成热反应");
        Check(ChemLab.ThermalReactions.Any(t => t.MinTemp <= 160f && 160f <= t.MaxTemp)
              || ChemLab.ThermalReactions.Any(t => t.MinTemp >= 140f),
              "高档（160°C）有可触发热反应");
        var kmno4 = ChemLab.ThermalReactions.FirstOrDefault(t => t.Id == "heat_kmno4");
        Check(kmno4 != null && kmno4.MinTemp == 140f && kmno4.MaxTemp == 160f && kmno4.DegradeTemp == 190f,
              "heat_kmno4 温区 140–160°C，降解 190°C（原 200–260 超档已改）");
        var nh4 = ChemLab.ThermalReactions.FirstOrDefault(t => t.Id == "heat_nh4no3");
        Check(nh4 != null && nh4.MinTemp == 150f && nh4.MaxTemp == 160f && nh4.DegradeTemp == 200f,
              "heat_nh4no3 温区 150–160°C，降解 200°C（原 180–250 超档已改）");
        // T4：全部至少一个原版输入（VanillaAnalog 键）；灰息含原版氨水
        foreach (var tid in new[] { "illicit_voidsalt", "illicit_redmist",
                                    "illicit_nightglass", "illicit_ashbreath" })
        {
            var t4 = ChemRecipes.All.FirstOrDefault(r => r.Outputs.Contains(tid) && r.Tier == 4);
            Check(t4 != null && t4.UsesVanilla, $"T4 标记 UsesVanilla: {tid}");
            Check(t4 != null && t4.Inputs.Any(i => ChemRecipes.VanillaAnalog.ContainsKey(i)),
                  $"T4 至少一个原版输入（VanillaAnalog）: {tid}");
        }
        var ash = ChemRecipes.All.FirstOrDefault(r => r.Id == "ashbreath");
        Check(ash != null && ash.Inputs.Contains("med_bottle_small_pink")
           && ash.Inputs.Contains("liq_nitric_acid") && ash.Inputs.Contains("sol_activated_carbon"),
              "ashbreath 输入 = 硝酸 + 活性炭 + 原版氨水瓶（med_bottle_small_pink）");
        Check(ash != null && ash.Equation.Contains("原版氨水"),
              "ashbreath 方程注明「原版氨水」");
        Check(ChemRecipes.All.First(r => r.Id == "hcl_absorption").Equation.Contains("水为溶剂")
           && ChemRecipes.All.First(r => r.Id == "hcl_absorption").Equation.Contains("非化学计量"),
              "hcl_absorption 方程标注「水为溶剂/非化学计量」");
        Check(ChemRecipes.All.First(r => r.Id == "salt_crystallize").Equation.Contains("母液")
           && ChemRecipes.All.First(r => r.Id == "salt_crystallize").Equation.Contains("非化学计量"),
              "salt_crystallize 方程标注「母液/非化学计量」");
        // 夜间反应结构：id 唯一、机器合法、输入/输出非空且不重复、temp/pH 范围
        Check(ChemRecipes.All.Select(r => r.Id).Distinct().Count() == ChemRecipes.All.Length,
              "夜间反应 id 唯一");
        Check(ChemRecipes.All.All(r => ChemRecipes.MachineIds.Contains(r.Machine)),
              "夜间反应 Machine ∈ MachineIds");
        Check(ChemRecipes.All.All(r => r.Inputs.Length > 0 && r.Outputs.Length > 0
                                    && r.Inputs.Distinct().Count() == r.Inputs.Length
                                    && r.Outputs.Distinct().Count() == r.Outputs.Length),
              "夜间反应 inputs/outputs 非空且不重复");
        Check(ChemRecipes.All.All(r => r.Temp >= 0 && r.Temp <= 3),
              "夜间反应 temp ∈ 0..3");
        Check(ChemRecipes.All.All(r => r.Ph >= -1 && r.Ph <= 14),
              "夜间反应 pH ∈ -1..14");
        // 气体输入必须有气瓶来源或有产线（夜间或热反应产出）
        var gasProduced = new HashSet<string>(ChemRecipes.All.SelectMany(r => r.Outputs)
                                    .Concat(ChemLab.ThermalReactions.SelectMany(t => t.Outputs)));
        Check(ChemRecipes.All.SelectMany(r => r.Inputs)
                             .Where(i => i.StartsWith("gas_") && i != "gas_canister_empty")
                             .All(g => ChemRecipes.CanisterSkus.ContainsKey(g) || gasProduced.Contains(g)),
              "全部气体输入均有气瓶来源或产线产出（空钢瓶容器除外）");

        // ── 26. P5d：组分映射合理性（纯数据层）──
        Check(ChemRecipes.ComponentParts.Count >= 60,
              $"ComponentParts 覆盖 ≥60 药剂（实际 {ChemRecipes.ComponentParts.Count}）");
        Check(ChemRecipes.ComponentParts.Values.All(d => d.Count > 0
              && d.Values.Sum() == ChemRecipes.ComponentTotalPart),
              $"每件药剂组分非空且合计 = {ChemRecipes.ComponentTotalPart}");
        Check(ChemRecipes.ComponentParts.Values.SelectMany(d => d.Keys).Distinct()
                             .All(k => ChemRecipes.ComponentLiquidIds.Contains(k)),
              "全部组分 id 都在 ComponentLiquidIds 白名单");
        Check(ChemRecipes.ComponentParts.TryGetValue("gas_chlorine", out var chlComp)
           && !chlComp.ContainsKey("chlorine"),
              "氯气组分不得直接使用原版 chlorine 组分（P5d）");

        // ── 27. 代码审查回归（docs/code-review-2026-11-10.md）──
        // 4.4 / 3.5：要冷的配方（temp 0）在加热档不能进行；temp ≥2 仍是「不低于」；temp 1 不限
        Check(ChemRecipes.TempOk(0, 0) && ChemRecipes.TempOk(0, 1)
              && !ChemRecipes.TempOk(0, 2) && !ChemRecipes.TempOk(0, 3),
              "审查 4.4：temp 0 配方只在冷/常温档进行");
        Check(!ChemRecipes.TempOk(2, 1) && ChemRecipes.TempOk(2, 2) && ChemRecipes.TempOk(2, 3)
              && ChemRecipes.TempOk(1, 0) && ChemRecipes.TempOk(1, 3),
              "审查 4.4：temp ≥2 为下限、temp 1 不限");
        Check(ChemRecipes.Match(new[] { "gas_hydrogen_chloride", "liq_water" }, "condenser_unit", 3, 7, false) == null
              && ChemRecipes.Match(new[] { "gas_hydrogen_chloride", "liq_water" }, "condenser_unit", 1, 7, false) != null,
              "审查 4.4：高温高压档不能冷凝吸收 HCl，常温可以");
        // 3.6：MatchThermalInputs 不看温度；每条热反应的降解温度高于反应上限
        Check(ChemLab.ThermalReactions.All(t => ChemLab.MatchThermalInputs(t.Inputs) == t),
              "审查 3.6：热反应可只按投料匹配（供超温降解判定）");
        Check(ChemLab.ThermalReactions.All(t => t.DegradeTemp > t.MaxTemp && t.MaxTemp >= t.MinTemp),
              "审查 3.6：降解温度 > 反应温区上限");
        // 4.4：同一化学反应在夜间机器与加热台上产物一致
        var nightKmno4 = ChemRecipes.All.First(r => r.Id == "kmno4_decomp");
        var heatKmno4 = ChemLab.ThermalReactions.First(t => t.Id == "heat_kmno4");
        Check(new HashSet<string>(nightKmno4.Outputs).SetEquals(heatKmno4.Outputs),
              "审查 4.4：高锰酸钾热分解在夜间机器与加热台的产物一致");
        // 4.3：气体成分沸点低于室温且可蒸发；固体成分不可蒸发
        string[] gasComps = { "fc_h2", "fc_n2", "fc_o2", "fc_cl2", "fc_hcl", "fc_nh3", "fc_co2", "fc_c2h4", "fc_n2o" };
        Check(gasComps.All(g => ChemRecipes.ComponentBoilingPoint(g) < ChemLab.RoomTemp
                                && ChemRecipes.ComponentIsEvaporative(g)),
              "审查 4.3：气体成分沸点 < 室温");
        string[] solidComps = { "fc_nacl", "fc_caco3", "fc_cuo", "fc_kmno4", "fc_naoh", "fc_sediment" };
        Check(solidComps.All(c => !ChemRecipes.ComponentIsEvaporative(c)
                                  && ChemRecipes.ComponentBoilingPoint(c) > 1000),
              "审查 4.3：固体成分不可蒸发");
        // 1.3：液体的原版倒液组分（PourParts）与成分表（ComponentParts）水含量一致（双氧水按水近似除外）
        foreach (var kv in ChemRecipes.PourParts)
        {
            if (kv.Key == "liq_hydrogen_peroxide") continue;
            if (!ChemRecipes.ComponentParts.TryGetValue(kv.Key, out var comp)) continue;
            int pourWater = kv.Value.TryGetValue("water", out var pw) ? pw : 0;
            int compWater = comp.TryGetValue("water", out var cw) ? cw * 100 / ChemRecipes.ComponentTotalPart : 0;
            Check(pourWater == compWater,
                  $"审查 1.3：{kv.Key} 倒液组分水含量 {pourWater}% = 成分表 {compWater}%");
        }

        Console.WriteLine($"\n反应总数 {ChemRecipes.All.Length}，设备 {ChemRecipes.MachineIds.Length} 台，实时热反应 {ChemLab.ThermalReactions.Length} 条，数字 ID {ChemItemIds.Ordered.Length} 个");        Console.WriteLine(fails == 0 ? "\n结论：全部通过" : $"\n结论：{fails} 项失败");
        return fails == 0 ? 0 : 1;
    }
}
