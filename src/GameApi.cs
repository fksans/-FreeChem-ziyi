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
        /// <summary>容器碎屑里属于重金属滤渣的 part 数（SludgeLedger 的 Metal）。</summary>
        public const string TagSludgeMetal = "fc3_msludge";
        /// <summary>不足一件的滤渣零头（SludgeLedger 的 Remainder）。</summary>
        public const string TagSludgeRemainder = "fc3_msludge_rem";

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

        public static void AddSludgeMetal(GameItem container, int parts)
        {
            if (parts <= 0) return;
            int v = ReadInt(container, TagSludgeMetal, 0) + parts;
            container.ModifyTag(TagSludgeMetal, s => s.Enable().SetInt(v));
        }

        /// <summary>
        /// 过滤钩子的落点：容器里的碎屑被滤走 <paramref name="removedDebris"/> part（滤前共 <paramref name="debrisBefore"/>）。
        /// 按账换成滤渣物品，放到后柜台。【待核】谁来调用它：见 v3/DESIGN.md「过滤钩子」。
        /// </summary>
        public static int OnDebrisRemoved(GameItem container, int removedDebris, int debrisBefore)
        {
            int metal = ReadInt(container, TagSludgeMetal, 0);
            int rem = ReadInt(container, TagSludgeRemainder, 0);
            int items = SludgeLedger.OnDebrisRemoved(removedDebris, debrisBefore, ref metal, ref rem,
                                                     RecipeTable.SludgePartsPerItem);
            int m = metal, r = rem;
            container.ModifyTag(TagSludgeMetal, s => s.Enable().SetInt(m));
            container.ModifyTag(TagSludgeRemainder, s => s.Enable().SetInt(r));
            for (int i = 0; i < items; i++) SpawnSludge();
            if (items > 0) Log("metal sludge +" + items);
            return items;
        }

        /// <summary>造一件滤渣放到后柜台。放不下时先只读探测，失败就销毁并弹原版提示，绝不静默丢失。</summary>
        static void SpawnSludge()
        {
            GameItem item = ItemDirectory.CreateEmptyItem(RecipeTable.SludgeItemId);   // 工厂已注册，见 ModEntry
            if (item == null) return;
            ModHelper.SetItemOwnerShip(item, true);
            ModHelper.SetSource(item, ModId, "FreeChem");
            var emporium = EmporiumEntry.Instance;
            GameGridInventory back = emporium == null ? null : emporium.backInvinvElement;
            if (back == null || !GraphUtils.CanAccept(back, item))
            {
                item.Destroy();
                try
                {
                    GenericConfirmationUIManager.Instance.OpenPanelSingleButton(
                        ModHelper.GetLocalized(ModId, "fc3_back_full"), "OK", null);
                }
                catch (Exception e) { Warn("full-counter alert failed: " + e.Message); }
                return;
            }
            GraphUtils.TryAcceptAllOrDestroy(back, item, "free_chem.sludge");
        }

        /// <summary>注册滤渣物品用的工厂。贴图 Textures/Items/metal_sludge.png（1×1 格）。</summary>
        public static GameItem BuildSludgeItem()
        {
            string id = RecipeTable.SludgeItemId;
            GameItem item = ItemDirectory.CreateEmptyItem(id);
            item.name = ModHelper.GetLocalized(ModId, id + "_name");
            item.shortDescription = ModHelper.GetLocalized(ModId, id + "_desc");
            item.flavorText = ModHelper.GetLocalized(ModId, id + "_flavor");
            item.unitValue = RecipeTable.SludgeValue;
            item.SetSpriteAndShapeFromMod(ModId, id);
            item.SetGameItemType(TypeHelper.CHEMICAL_SUPPLIES);
            return item;
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
