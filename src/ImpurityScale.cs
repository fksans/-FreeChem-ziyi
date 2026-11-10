using System;

namespace FreeChem
{
    /// <summary>
    /// "杂质偏硬核"：把原版试剂瓶生成时的杂质范围按百分比放大（纯逻辑，可离线测试）。
    /// 原版 PreBuildChemHelper.SetImpurity(item, min, max)：单位万分之一，酸、碱、还原剂、脂合剂等瓶子用 (200, 800)，即 2%–8%。
    /// </summary>
    public static class ImpurityScale
    {
        /// <summary>放大后的杂质上限（万分之一）：最多 20%，再多瓶子基本没法用。</summary>
        public const int Cap = 2000;

        /// <summary>默认放大到原版的 150%：2%–8% 变成 3%–12%。</summary>
        public const int HardcorePercent = 150;

        public static void Apply(ref int min, ref int max, int percent)
        {
            if (percent == 100) return;
            long lo = (long)min * percent / 100;
            long hi = (long)max * percent / 100;
            // 原版本来就超过上限的（理论上没有）不动；其余封顶
            if (hi > Cap) hi = Math.Max(Cap, max);
            if (lo > hi) lo = hi;
            min = (int)lo;
            max = (int)hi;
        }
    }
}
