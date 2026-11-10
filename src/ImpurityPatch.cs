using HarmonyLib;

namespace FreeChem
{
    /// <summary>
    /// 放大原版试剂瓶的杂质（RECON 第 4 节：PreBuildChemHelper.SetImpurity 是 public static，
    /// 酸 / 碱 / 还原剂 / 脂合剂 / 芬达前体瓶都用 (200, 800)；迷你酒精瓶不调用它，不受影响）。
    /// 用位置参数 __1 / __2（min / max），不依赖参数名。模组设置里可关。
    /// </summary>
    [HarmonyPatch(typeof(PreBuildChemHelper), "SetImpurity")]
    static class ImpurityPatch
    {
        public static bool Enabled = true;

        static void Prefix(ref int __1, ref int __2)
        {
            if (!Enabled) return;
            ImpurityScale.Apply(ref __1, ref __2, ImpurityScale.HardcorePercent);
        }
    }
}
