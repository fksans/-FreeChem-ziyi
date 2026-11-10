using System;
using System.Collections.Generic;
using System.Linq;
using FreeChem;

// 反应引擎的离线测试：不需要游戏。运行：dotnet run -c Release
static class Tests
{
    static int fails, checks;
    static void Check(bool ok, string msg)
    {
        checks++;
        if (!ok) { fails++; Console.WriteLine("  [FAIL] " + msg); }
        else Console.WriteLine("  [PASS] " + msg);
    }

    static Dictionary<string, int> P(params (string, int)[] kv)
        => kv.ToDictionary(x => x.Item1, x => x.Item2);

    static int Total(IDictionary<string, int> p) => p.Values.Sum();

    // 一直 tick 到状态变化或超过上限，返回 tick 数
    static int RunUntil(Recipe[] rs, Dictionary<string, int> parts, float temp, ref ReactionState st,
                        TickOutcome stop, int max = 10000)
    {
        for (int i = 1; i <= max; i++)
            if (ReactionEngine.Step(rs, parts, temp, ref st).Outcome == stop) return i;
        return -1;
    }

    static int Main()
    {
        var rs = RecipeTable.All;
        Console.WriteLine("── 基本流程 ──");
        {
            var parts = P(("water", 400000), ("heavy_metal", 20000), ("base", 60000));
            int before = Total(parts);
            var st = default(ReactionState);
            int n = RunUntil(rs, parts, 25f, ref st, TickOutcome.Completed);
            Check(n == 120, $"氢氧化物沉淀在 25°C 恰好 120 tick 完成（实际 {n}）");
            Check(Total(parts) == before, "体积守恒：反应前后总 part 不变");
            Check(parts["heavy_metal"] == 2000, $"一轮只除掉 90%：剩 2000 part 重金属（实际 {parts["heavy_metal"]}）");
            Check(parts["debris"] == 54000, $"生成 54000 part 碎屑（实际 {parts.GetValueOrDefault("debris")}）");
            Check(st.Idle, "完成后回到空闲");
        }

        Console.WriteLine("── 残留下限 ──");
        {
            var parts = P(("water", 400000), ("heavy_metal", 900), ("base", 60000));
            var st = default(ReactionState);
            var r = ReactionEngine.Step(rs, parts, 25f, ref st);
            Check(r.Outcome == TickOutcome.Idle && st.Idle, "重金属不足 1000 part（1 ml）时不开始：残留下限");
        }

        Console.WriteLine("── 多轮提纯（偏硬核） ──");
        {
            var parts = P(("water", 300000), ("heavy_metal", 100000), ("base", 300000));
            var st = default(ReactionState);
            int rounds = 0;
            while (parts["heavy_metal"] >= 1000 && rounds < 20)
            {
                RunUntil(rs, parts, 25f, ref st, TickOutcome.Completed);
                rounds++;
            }
            Check(rounds >= 3 && rounds <= 6, $"从 10 万 part 除到 1 ml 以下要 3–6 轮（实际 {rounds} 轮）");
            Check(parts["heavy_metal"] < 1000, $"最终残留 < 1000 part（实际 {parts["heavy_metal"]}）");
        }

        Console.WriteLine("── 温度档 ──");
        {
            var parts = P(("heavy_metal", 20000), ("base", 60000), ("water", 100000));
            var st = default(ReactionState);
            int n = RunUntil(rs, parts, 60f, ref st, TickOutcome.Completed);
            int t2 = rs[0].Bands[1].Time;
            Check(n == t2 && t2 < rs[0].Bands[0].Time, $"60°C 走第二档，{t2} tick 完成且比第一档快（实际 {n}）");
            Check(parts["heavy_metal"] == 5000, $"第二档转化率 75%：剩 5000（实际 {parts["heavy_metal"]}）");
            var parts2 = P(("heavy_metal", 20000), ("base", 60000));
            var st2 = default(ReactionState);
            var r = ReactionEngine.Step(rs, parts2, 10f, ref st2);
            Check(r.Outcome == TickOutcome.Idle, "10°C 低于所有温度档，不开始");
            r = ReactionEngine.Step(rs, parts2, 70f, ref st2);
            Check(r.Outcome == TickOutcome.Idle, "70°C（档上界不含）不开始");
        }

        Console.WriteLine("── 暂停：温度离开温度档，保留进度 ──");
        {
            var parts = P(("heavy_metal", 20000), ("base", 60000), ("water", 100000));
            var st = default(ReactionState);
            for (int i = 0; i < 50; i++) ReactionEngine.Step(rs, parts, 25f, ref st);
            Check(st.Progress == 50, "先跑 50 tick");
            TickResult r = default;
            for (int i = 0; i < 30; i++) r = ReactionEngine.Step(rs, parts, 90f, ref st);
            Check(r.Outcome == TickOutcome.Paused && st.Progress == 50, "90°C 暂停 30 tick：进度仍是 50");
            int n = RunUntil(rs, parts, 25f, ref st, TickOutcome.Completed);
            Check(n == 70, $"回到 25°C 再跑 70 tick 完成（实际 {n}）");
        }

        Console.WriteLine("── 中止：投料被取走 ──");
        {
            var parts = P(("heavy_metal", 20000), ("base", 60000));
            var st = default(ReactionState);
            for (int i = 0; i < 40; i++) ReactionEngine.Step(rs, parts, 25f, ref st);
            parts["base"] = 0;
            var r = ReactionEngine.Step(rs, parts, 25f, ref st);
            Check(r.Outcome == TickOutcome.Aborted && st.Idle, "碱被取走：中止并清零");
        }

        Console.WriteLine("── 中途存档再读档（状态只有 RecipeId + Progress） ──");
        {
            var parts = P(("heavy_metal", 20000), ("base", 60000), ("water", 100000));
            var st = default(ReactionState);
            for (int i = 0; i < 70; i++) ReactionEngine.Step(rs, parts, 25f, ref st);
            // "存档"：只保留这两个值和组分
            string savedId = st.RecipeId; int savedProg = st.Progress;
            var loaded = default(ReactionState); loaded.RecipeId = savedId; loaded.Progress = savedProg;
            int n = RunUntil(rs, parts, 25f, ref loaded, TickOutcome.Completed);
            Check(n == 50, $"读档后再跑 50 tick 完成（实际 {n}）");
        }

        Console.WriteLine("── 存档里的配方被删除 ──");
        {
            var parts = P(("heavy_metal", 20000), ("base", 60000));
            var st = new ReactionState { RecipeId = "no_such_recipe", Progress = 5 };
            var r = ReactionEngine.Step(rs, parts, 25f, ref st);
            Check(r.Outcome == TickOutcome.Idle && st.Idle, "未知配方 id：清掉状态，不崩");
        }

        Console.WriteLine("── 夜间补 360 次 ──");
        {
            var parts = P(("heavy_metal", 100000), ("base", 300000), ("water", 300000));
            var st = default(ReactionState);
            for (int i = 0; i < 360; i++) ReactionEngine.Step(rs, parts, 25f, ref st);
            Check(parts["heavy_metal"] < 100000 / 100 * 2, $"一夜（360 tick）能完成 3 轮氢氧化物沉淀，重金属降到 2% 以下（实际 {parts["heavy_metal"]}）");
        }

        Console.WriteLine("── 重金属滤渣账 ──");
        {
            var parts = P(("water", 400000), ("heavy_metal", 20000), ("base", 60000));
            var st = default(ReactionState);
            TickResult last = default;
            for (int i = 0; i < 120; i++) last = ReactionEngine.Step(rs, parts, 25f, ref st);
            Check(last.SludgeParts == 54000, $"氢氧化物沉淀结算时记入 54000 part 滤渣（实际 {last.SludgeParts}）");
            var soft = P(("mineral", 20000), ("base", 10000));
            var st2 = default(ReactionState);
            TickResult l2 = default;
            for (int i = 0; i < 90; i++) l2 = ReactionEngine.Step(rs, soft, 25f, ref st2);
            Check(l2.Outcome == TickOutcome.Completed && l2.SludgeParts == 0, "石灰软化不记滤渣");

            int metal = 54000, rem = 0;
            // 过滤滤走一半碎屑（容器里碎屑共 54000，另有别处来源的 6000）
            int items = SludgeLedger.OnDebrisRemoved(30000, 60000, ref metal, ref rem, RecipeTable.SludgePartsPerItem);
            Check(items == 5 && metal == 27000 && rem == 2000, $"滤走一半碎屑：账上 27000 转出，换 5 件、零头 2000（实际 {items}/{metal}/{rem}）");
            items = SludgeLedger.OnDebrisRemoved(30000, 30000, ref metal, ref rem, RecipeTable.SludgePartsPerItem);
            Check(items == 5 && metal == 0 && rem == 4000, $"碎屑滤光：余下的账全部结清（实际 {items}/{metal}/{rem}）");
            int m2 = 0, r2 = 0;
            Check(SludgeLedger.OnDebrisRemoved(5000, 5000, ref m2, ref r2, 5000) == 0, "没有滤渣账时滤碎屑不产出物品");
            int m3 = 9000, r3 = 0;
            Check(SludgeLedger.OnDebrisRemoved(1000, 5000, ref m3, ref r3, 5000) == 0 && m3 > 0, "账比碎屑多时按碎屑总量封顶，不会凭空多产出");
            // 总量守恒：无论分几次滤，换出的件数 × 5000 + 零头 + 剩余账 = 原账
            int mm = 54000, rr = 0, got = 0;
            for (int i = 0; i < 9; i++) got += SludgeLedger.OnDebrisRemoved(6000, 54000 - i * 6000, ref mm, ref rr, 5000);
            Check(got * 5000 + rr + mm == 54000, $"分 9 次滤完：件数×5000 + 零头 + 剩余账 = 原账（{got} 件，零头 {rr}，剩 {mm}）");
        }

        Console.WriteLine("── 杂质放大 ──");
        {
            int lo = 200, hi = 800;
            ImpurityScale.Apply(ref lo, ref hi, 150);
            Check(lo == 300 && hi == 1200, $"原版 2%–8% 放大到 150% = 3%–12%（实际 {lo}–{hi}）");
            lo = 200; hi = 800;
            ImpurityScale.Apply(ref lo, ref hi, 100);
            Check(lo == 200 && hi == 800, "100% 不改动");
            lo = 1000; hi = 1900;
            ImpurityScale.Apply(ref lo, ref hi, 150);
            Check(hi == 2000 && lo <= hi, $"上限封顶 20%（实际 {lo}–{hi}）");
            lo = 0; hi = 0;
            ImpurityScale.Apply(ref lo, ref hi, 150);
            Check(lo == 0 && hi == 0, "没有杂质的瓶子保持 0");
        }

        Console.WriteLine("── 配方表自检 ──");
        {
            Check(rs.Select(r => r.Id).Distinct().Count() == rs.Length, "配方 id 不重复");
            Check(rs.All(r => r.Inputs.Values.Sum() == r.Outputs.Values.Sum()), "每条配方体积守恒");
            Check(rs.All(r => r.Bands.Zip(r.Bands.Skip(1), (a, b) => a.TMax == b.TMin).All(x => x)), "温度档无缺口无重叠");
            Check(rs.All(r => r.Bands.All(b => b.Time >= 30 && b.Time <= 600)), "反应时间在 30–600 秒（不要太长）");
        }

        Console.WriteLine($"\n共 {checks} 项，失败 {fails} 项");
        Console.WriteLine(fails == 0 ? "结论：全部通过" : "结论：有失败");
        return fails == 0 ? 0 : 1;
    }
}
