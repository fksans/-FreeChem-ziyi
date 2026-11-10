using System;
using System.Collections.Generic;

namespace FreeChem
{
    /// <summary>
    /// 全部游戏 API 调用集中在这个文件。引擎（ReactionEngine）不引用游戏类型。
    /// 签名来源：旧版已用真实 DLL 编译通过的写法 + v3/RECON.md 的 IL 核对。
    /// 带 【待核】 的地方没有任何资料，需要在有游戏文件的机器上确认。
    /// </summary>
    public static class GameApi
    {
        public const string ModId = "free_chem";
        public const string TagRecipe = "fc3_rxn";
        public const string TagProgress = "fc3_prog";

        static ModLog log;
        public static void Bind(ModLog l) { log = l; }
        public static void Log(string msg) { if (log != null) log.Log("[FreeChem] " + msg); }
        static void Warn(string msg) { if (log != null) log.Warning("[FreeChem] " + msg); }

        /// <summary>配方用到的全部原版 Liquid id（输入 + 输出）。</summary>
        static readonly string[] Substances = CollectSubstances();

        static string[] CollectSubstances()
        {
            var set = new HashSet<string>();
            foreach (var r in RecipeTable.All)
            {
                foreach (var k in r.Inputs.Keys) set.Add(k);
                foreach (var k in r.Outputs.Keys) set.Add(k);
            }
            var arr = new string[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        public static bool IsLiquidContainer(GameItem item)
            => item != null && item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG);

        static int ReadInt(GameItem item, string tag, int fallback)
        {
            var t = item.GetTagReadonly(tag);
            return t == null ? fallback : t.GetInt();
        }

        /// <summary>读容器里配方涉及的各组分（part）。</summary>
        public static Dictionary<string, int> ReadParts(GameItem container)
        {
            var parts = new Dictionary<string, int>();
            foreach (var id in Substances)
                parts[id] = ReadInt(container, Liquid.GetCurrentPartFromId(id), 0);
            return parts;
        }

        /// <summary>把变化的组分写回容器（只写和原值不同的）。</summary>
        public static void WriteParts(GameItem container, Dictionary<string, int> before,
                                      Dictionary<string, int> after)
        {
            foreach (var id in Substances)
            {
                int v = after[id];
                if (v == before[id]) continue;
                if (v < 0) v = 0;   // 防御：引擎保证不会出现，但绝不往 tag 里写负数
                container.ModifyTag(Liquid.GetCurrentPartFromId(id), s => s.Enable().SetInt(v));
            }
            // 原版每条 Handle* 有变化时的收尾（RECON 第 3 节）。
            try { WaterFeatureHelper.OnLiquidChanged(container, WaterFeatureHelper.GetWaterFeature(container).IsExposed()); }
            catch (Exception e) { Warn("OnLiquidChanged failed: " + e.Message); }
        }

        public static ReactionState ReadState(GameItem container)
        {
            var t = container.GetTagReadonly(TagRecipe);
            string id = t == null ? null : t.GetString();
            return new ReactionState { RecipeId = id, Progress = ReadInt(container, TagProgress, 0) };
        }

        public static void WriteState(GameItem container, ReactionState st)
        {
            string id = st.RecipeId ?? "";
            int p = st.Progress;
            container.ModifyTag(TagRecipe, s => s.Enable().SetString(id));
            container.ModifyTag(TagProgress, s => s.Enable().SetInt(p));
        }

        static bool warnedTemp;

        /// <summary>
        /// 【待核】容器当前温度（°C）。
        /// RECON 只知道：容器温度由 ChemicalHelper.InitContainerTemp / InitContainerTempFunction 初始化，
        /// 每秒回调 containerTempTick 里先 HandleAmbientTempDrop 再 HandleTemperatureReaction；
        /// 加热板 HeatLiquidOnStove 写温度。读温度的方法名 / tag 名没有查到。
        /// 候选：在 IL 里看 HandleChlorine / HandleBoilingReaction 怎么取温度，照抄。
        /// 在核对之前返回室温 26°C：配方的温度档功能上"总在室温档"，其余逻辑照常工作。
        /// </summary>
        public static float GetTemperature(GameItem container)
        {
            if (!warnedTemp)
            {
                warnedTemp = true;
                Warn("GetTemperature 尚未接入真实的容器温度（返回室温 26°C）。见 src/GameApi.cs 的【待核】。");
            }
            return 26f;
        }
    }
}
