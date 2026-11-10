using System;
using System.Collections.Generic;

namespace FreeChem
{
    // ═══════════════════════════════════════════════════════════════════
    // 纯逻辑层：不引用任何游戏类型，可以脱离游戏单独测试（tools/v3/engine-tests）。
    //
    // 模型（见 v3/DESIGN.md）：
    //  · 配方作用在【容器里的原版 Liquid 组分】上，不新造物质；
    //  · 每个容器每秒 tick 一次（白天由游戏每秒调用，夜间游戏补 360 次）；
    //  · 一个容器同一时间只进行一个反应；
    //  · 条件满足（投料够、温度落在某个温度档）时进度 +1；到达该档的 time 时结算；
    //  · 温度离开温度档：暂停计时、保留进度（回到任一档继续）；
    //  · 投料不够：进度清零，容器回到空闲；
    //  · 结算：extent = min(投料 / 系数) × conversion，按系数扣输入、加输出（体积守恒）。
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>一个温度档。区间是 [TMin, TMax)。</summary>
    public sealed class Band
    {
        public readonly float TMin, TMax;
        public readonly double Conversion;
        public readonly int Time;
        public Band(float tMin, float tMax, int time, double conversion)
        { TMin = tMin; TMax = tMax; Time = time; Conversion = conversion; }
        public bool Contains(float t) => t >= TMin && t < TMax;
    }

    public sealed class Recipe
    {
        public readonly string Id, Name;
        public readonly Dictionary<string, int> Inputs, Outputs;
        /// <summary>开始反应所需的最小反应当量（part）：Extent 低于它不会开始（残留下限）。</summary>
        public readonly int MinExtent;
        public readonly Band[] Bands;
        /// <summary>产出的碎屑是重金属滤渣：结算时记入容器的滤渣账（见 SludgeLedger）。</summary>
        public readonly bool ProducesSludge;
        public Recipe(string id, string name, Dictionary<string, int> inputs,
                      Dictionary<string, int> outputs, int minExtent, Band[] bands,
                      bool producesSludge = false)
        {
            ProducesSludge = producesSludge;
            Id = id; Name = name; Inputs = inputs; Outputs = outputs;
            MinExtent = minExtent; Bands = bands;
        }

        /// <summary>找到包含温度 t 的温度档；没有返回 null。</summary>
        public Band BandAt(float t)
        {
            foreach (var b in Bands) if (b.Contains(t)) return b;
            return null;
        }

        /// <summary>限量试剂能支撑的"反应当量"（part）：min(投料 / 系数)。缺任何一种返回 0。</summary>
        public int Extent(IDictionary<string, int> parts)
        {
            long best = long.MaxValue;
            foreach (var kv in Inputs)
            {
                int have;
                parts.TryGetValue(kv.Key, out have);
                long e = have / kv.Value;
                if (e < best) best = e;
            }
            return best == long.MaxValue ? 0 : (int)best;
        }
    }

    /// <summary>某个容器上持久化的反应状态（写在容器 tag 里，读档可恢复）。</summary>
    public struct ReactionState
    {
        public string RecipeId;   // null 或空 = 空闲
        public int Progress;      // 已累计的 tick 数
        public bool Idle => string.IsNullOrEmpty(RecipeId);
    }

    public enum TickOutcome
    {
        Idle,        // 没有可进行的反应
        Progressed,  // 进度 +1
        Paused,      // 温度不在任何温度档里，暂停
        Completed,   // 本次 tick 结算了产物
        Aborted,     // 投料不够，进度清零
    }

    public struct TickResult
    {
        public TickOutcome Outcome;
        public string RecipeId;
        public int Progress, Target;   // Target = 当前温度档的 time（暂停时为上一次可用档的 time，无则 0）
        public bool PartsChanged;      // 组分是否被改写
        public int SludgeParts;        // 本次结算新增的重金属滤渣（part，记账用）
    }

    /// <summary>
    /// 重金属滤渣账。容器里记着"碎屑里有多少 part 是重金属沉淀"（Metal），
    /// 过滤把碎屑滤走时，按比例把这部分换成滤渣物品，零头留在 Remainder 里下次再算。
    /// 纯逻辑，可离线测试；游戏侧只负责把 Metal / Remainder 存进容器 tag、在过滤钩子里调用。
    /// </summary>
    public static class SludgeLedger
    {
        /// <summary>被滤走的 <paramref name="removedDebris"/> 里，属于滤渣的 part 数（按账上占比）。</summary>
        public static int MetalShare(int removedDebris, int debrisBefore, int metal)
        {
            if (removedDebris <= 0 || debrisBefore <= 0 || metal <= 0) return 0;
            if (removedDebris >= debrisBefore) return Math.Min(metal, debrisBefore);
            long share = (long)removedDebris * Math.Min(metal, debrisBefore) / debrisBefore;
            return (int)share;
        }

        /// <summary>
        /// 一次过滤的结算：返回新的滤渣件数；<paramref name="metal"/> 扣掉被滤走的部分，
        /// <paramref name="remainder"/> 累加后按 <paramref name="partsPerItem"/> 取整。
        /// </summary>
        public static int OnDebrisRemoved(int removedDebris, int debrisBefore, ref int metal,
                                          ref int remainder, int partsPerItem)
        {
            int share = MetalShare(removedDebris, debrisBefore, metal);
            metal -= share;
            remainder += share;
            int items = remainder / partsPerItem;
            remainder -= items * partsPerItem;
            return items;
        }
    }

    public static class ReactionEngine
    {
        /// <summary>
        /// 推进一个容器一秒。<paramref name="parts"/> 是容器当前各组分的 part 数（会被原地修改）。
        /// <paramref name="recipes"/> 按顺序匹配，先匹配到先开始。
        /// </summary>
        public static TickResult Step(Recipe[] recipes, IDictionary<string, int> parts,
                                      float temp, ref ReactionState state)
        {
            var res = new TickResult { Outcome = TickOutcome.Idle };

            // 1) 空闲：找第一条投料够、温度对的配方
            if (state.Idle)
            {
                foreach (var r in recipes)
                {
                    if (r.Extent(parts) < r.MinExtent) continue;
                    if (r.BandAt(temp) == null) continue;
                    state.RecipeId = r.Id;
                    state.Progress = 0;
                    break;
                }
                if (state.Idle) return res;
            }

            var cur = Find(recipes, state.RecipeId);
            if (cur == null)   // 配方在更新后被删掉了：清掉状态，不崩
            {
                state = default(ReactionState);
                return res;
            }
            res.RecipeId = cur.Id;

            // 2) 投料不够：中止并清零
            if (cur.Extent(parts) < 1)
            {
                state = default(ReactionState);
                res.Outcome = TickOutcome.Aborted;
                return res;
            }

            // 3) 温度不在任何档：暂停
            var band = cur.BandAt(temp);
            res.Progress = state.Progress;
            if (band == null)
            {
                res.Outcome = TickOutcome.Paused;
                res.Target = LastKnownTarget(cur, state.Progress);
                return res;
            }

            // 4) 推进
            state.Progress++;
            res.Progress = state.Progress;
            res.Target = band.Time;
            if (state.Progress < band.Time)
            {
                res.Outcome = TickOutcome.Progressed;
                return res;
            }

            // 5) 结算
            int extent = (int)Math.Floor(cur.Extent(parts) * band.Conversion + 1e-9);
            if (extent < 1) extent = 1;
            foreach (var kv in cur.Inputs) parts[kv.Key] = parts[kv.Key] - kv.Value * extent;
            foreach (var kv in cur.Outputs)
            {
                int have;
                parts.TryGetValue(kv.Key, out have);
                parts[kv.Key] = have + kv.Value * extent;
            }
            if (cur.ProducesSludge)
                foreach (var kv in cur.Outputs) res.SludgeParts += kv.Value * extent;
            state = default(ReactionState);
            res.Outcome = TickOutcome.Completed;
            res.PartsChanged = true;
            return res;
        }

        static Recipe Find(Recipe[] recipes, string id)
        {
            foreach (var r in recipes) if (r.Id == id) return r;
            return null;
        }

        // 暂停时 tooltip 要显示"x / 目标"：取进度所在的最高档 time，没有就取第一档
        static int LastKnownTarget(Recipe r, int progress)
        {
            int best = r.Bands[0].Time;
            foreach (var b in r.Bands) if (progress < b.Time) { best = b.Time; break; }
            return best;
        }
    }
}
