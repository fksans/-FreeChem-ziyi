using System;
using System.Collections.Generic;
using HarmonyLib;

namespace FreeChem
{
    /// <summary>
    /// 在原版"容器每秒反应"之后跑本模组的规则（v3/RECON.md 第 3 节）。
    /// 白天：EmporiumEntry.Update 每秒触发 containerTempTick → HandleTemperatureReaction。
    /// 夜间：PlayerStore.EndNight → RunNightlyChemicalSimulation 连续调用 360 次同一回调，
    ///       所以夜间补算天然包含本模组的规则，不用另外处理。
    /// ChemicalHelper 及其方法没有 ModRestrictedAttribute，扫描器不会拦。
    /// </summary>
    [HarmonyPatch(typeof(ChemicalHelper), "HandleTemperatureReaction")]
    static class ReactionPatch
    {
        static void Postfix(GameItem container)
        {
            try
            {
                if (!GameApi.IsLiquidContainer(container)) return;

                var parts = GameApi.ReadParts(container);
                var before = new Dictionary<string, int>(parts);
                var state = GameApi.ReadState(container);
                var stateBefore = state;

                var res = ReactionEngine.Step(RecipeTable.All, parts,
                                              GameApi.GetTemperature(container), ref state);

                if (res.PartsChanged) GameApi.WriteParts(container, before, parts);
                if (state.RecipeId != stateBefore.RecipeId || state.Progress != stateBefore.Progress)
                    GameApi.WriteState(container, state);
                if (res.Outcome == TickOutcome.Completed)
                    GameApi.Log("reaction completed: " + res.RecipeId);
            }
            catch (Exception e)
            {
                // 补丁异常绝不能影响原版的每秒回调
                GameApi.Log("ReactionPatch failed: " + e.Message);
            }
        }
    }
}
