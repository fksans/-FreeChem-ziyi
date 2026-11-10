using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace FreeChem
{
    // ═══════════════════════════════════════════════════════════════════
    // 原版桥接 Harmony 兜底补丁（数据桥接优先，补丁只处理
    //      「旧存档物品 / 原版 UI 未识别 / 原版反馈图标 / 客户 tag 匹配 / 倒液」五类兜底）。
    //
    // 依据 docs/harmony-bridge.md + docs/note-phone-component-research.md：
    //  · 加载器会自动扫描并应用 [HarmonyPatch] 类 —— 不要手动 PatchAll/UnpatchAll；
    //  · 修补游戏代码不需要任何 Capability，manifest 的 <Capabilities /> 保持为空；
    //  · 不引用/不捆绑 0Harmony.dll（csproj 用游戏目录副本，Private=false）；
    //  · 所有补丁体 try/catch + 防御 null，补丁异常不得污染游戏。
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 成分仪旧存档兜底：RefreshChemMode 里若本模组药剂缺 CHEMICAL_PRODUCT_TAG
    /// （旧存档 tag 被覆盖），先补一次桥接数据再重绘；不直接写 TMP（避免递归）。
    /// </summary>
    [HarmonyPatch(typeof(ChemScannerUIManager), "RefreshChemMode")]
    static class ChemScannerLegacyBridgePatch
    {
        static bool bridging;

        static void Postfix(ChemScannerUIManager __instance)
        {
            try
            {
                if (bridging || __instance == null) return;
                GameItem item = __instance.currentScannedItem;
                if (item == null || !ChemCatalog.IsOurReagent(item.identifier)) return;
                if (item.IsTag(ChemicalHelper.CHEM_ITEM_TAG)
                    && item.IsTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG)) return;

                bridging = true;
                try
                {
                    if (ChemCatalog.BridgeVanillaChemData(item))
                    {
                        ChemRuntime.Log("[patch] scanner legacy bridge applied: " + item.identifier);
                        __instance.RefreshUI();
                    }
                }
                finally { bridging = false; }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[patch] scanner legacy bridge failed: " + e.Message);
            }
        }
    }

    /// <summary>
    /// P6a 成分仪可靠显示：对本模组药剂**始终**写成分/总液量/化学式/毒性欣快，
    /// 数据来自 ChemRecipes.ComponentParts + fc_tox/fc_euph，不依赖原版 Liquid tag；
    /// 覆盖本模组自己的行，避免与原版组分行重复。
    /// </summary>
    [HarmonyPatch(typeof(ChemScannerUIManager), "RefreshUI")]
    static class ChemScannerModInfoPatch
    {
        static bool writing;

        static void Postfix(ChemScannerUIManager __instance)
        {
            try
            {
                if (writing || __instance == null) return;
                GameItem item = __instance.currentScannedItem;
                if (item == null || !ChemCatalog.IsOurReagent(item.identifier)) return;

                // P6a：旧存档可能没有组分 tag（marker 也丢了）→ 扫描时补一次注入，
                // 保证「始终显示成分」不依赖读档时序。
                TryEnsureComponents(item);

                string purity = ChemRuntime.PurityOf(item).ToString("0.#");
                // 审查 2.2：以前用静态 lastSignature 去重——原版 RefreshUI 每次都会先重写 TMP，
                // 签名相同就跳过，于是第二次刷新起屏幕上就是原版文字。写 TMP 很便宜，每次都写。

                string comp = ComposeComponents(item);
                string line = L("scanner_fc_line", comp, purity);
                string grade = L("scanner_fc_grade", GradeName(item));
                string vol = VolumeText(item);
                string toxEuph = ToxEuphLine(item);

                writing = true;
                try
                {
                    // P6a（研究 §4）：整体覆盖四行，避免与原版 Liquid 组分行重复
                    if (__instance.testedTMP != null)
                        __instance.testedTMP.text = line;                 // 化学式·成分·纯度
                    if (__instance.contaminantTMP != null)
                        __instance.contaminantTMP.text = grade
                            + (string.IsNullOrEmpty(toxEuph) ? "" : "\n" + toxEuph);
                    if (__instance.labelTMP != null)
                        __instance.labelTMP.text = grade;
                    if (__instance.totalVolumeTMP != null)
                        __instance.totalVolumeTMP.text = vol;

                }
                finally { writing = false; }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[patch] scanner refresh info failed: " + e.Message);
            }
        }

        /// <summary>
        /// P6a 扫描兜底：若本模组药剂尚未注入组分（旧存档/注入失败），用 ComponentParts 补一次。
        /// 原版 Liquid id 走 AddLiquidToChemFromList；自定义组分（fc_chlorine_gas）手动写 currentPart。
        /// </summary>
        static void TryEnsureComponents(GameItem item)
        {
            try
            {
                if (item.IsTag(ChemCatalog.TCompApplied)) return;
                int existing;
                try { existing = ChemicalHelper.GetTotalVolume(item); }
                catch (Exception) { existing = 0; }
                if (existing > 0) return;

                Dictionary<string, int> parts;
                if (!ChemRecipes.TryGetComponentParts(item.identifier, out parts)) return;
                ChemCatalog.EnsureCustomLiquids();
                var original = new List<KeyValuePair<string, int>>();
                foreach (var kv in parts)
                {
                    Liquid liq = Liquid.GetLiquidFromId(kv.Key);
                    if (liq != null && !string.IsNullOrEmpty(liq.currentPart))
                        original.Add(new KeyValuePair<string, int>(kv.Key, kv.Value));
                }
                if (original.Count > 0)
                    ChemicalHelper.AddLiquidToChemFromList(item, original);
                item.ModifyTag(ChemCatalog.TCompApplied, s => s.Enable().SetInt(1));
                ChemRuntime.Log("[components] scanner fallback injected components: " + item.identifier);
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[components] scanner fallback failed: " + e.Message);
            }
        }

        /// <summary>成分文本：化学式 · 总液量 · 组分百分比（来自 ComponentParts）。</summary>
        static string ComposeComponents(GameItem item)
        {
            // P9/P10c：用户可见行不出现化学式缩写（CO2/H2O/HCl…），改用物品中文名；
            // ASCII 公式仍保留在内部数据（fc_formula tag / Recpeer.Formula）。
            string name = L(item.identifier + "_name");
            if (string.IsNullOrEmpty(name) || name == item.identifier + "_name") name = item.identifier;
            string volume = VolumeText(item);
            // 审查 1.3：优先显示物品上真实的成分 tag（倒液/混合都以它为准），
            // 读不到时才退回静态成分表。
            string comp = ActualComponents(item);
            Dictionary<string, int> parts;
            if (comp.Length == 0 && ChemRecipes.TryGetComponentParts(item.identifier, out parts))
                foreach (var kv in parts)
                    comp += (comp.Length == 0 ? "" : " / ") + ComponentName(kv.Key)
                            + " " + (kv.Value / (ChemRecipes.ComponentTotalPart / 100)) + "%";
            if (comp.Length == 0) comp = LFallback("scanner_fc_pure", "pure substance");
            return name + " · " + volume + " · " + comp;
        }

        /// <summary>读物品上每种 Liquid 的 currentPart tag，拼成「名称 百分比」列表；全为 0 时返回空串。</summary>
        static string ActualComponents(GameItem item)
        {
            try
            {
                if (Liquid.Liquids == null) return "";
                var found = new List<KeyValuePair<Liquid, int>>();
                long total = 0;
                foreach (var liq in Liquid.Liquids)
                {
                    if (liq == null || string.IsNullOrEmpty(liq.currentPart)) continue;
                    int v = ChemCatalog.ReadInt(item, liq.currentPart, 0);
                    if (v <= 0) continue;
                    found.Add(new KeyValuePair<Liquid, int>(liq, v));
                    total += v;
                }
                if (total <= 0) return "";
                found.Sort((a, b) => b.Value.CompareTo(a.Value));
                string comp = "";
                foreach (var kv in found)
                {
                    string name = ComponentName(kv.Key.identifier);
                    if (name == kv.Key.identifier && !string.IsNullOrEmpty(kv.Key.displayName))
                        name = kv.Key.displayName;
                    comp += (comp.Length == 0 ? "" : " / ") + name + " "
                            + Math.Round(kv.Value * 100.0 / total) + "%";
                }
                return comp;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[patch] read actual components failed: " + e.Message);
                return "";
            }
        }

        /// <summary>P10a：成分显示名来自 ComponentDefs 的本地化键 comp_&lt;id&gt;_name（缺键回退 ASCII 公式）。</summary>
        static string ComponentName(string id)
        {
            ChemRecipes.ComponentDef def;
            if (ChemRecipes.ComponentDefs.TryGetValue(id, out def) && def != null)
            {
                string name = L(def.NameKey);
                if (!string.IsNullOrEmpty(name) && name != def.NameKey) return name;
                return def.Formula;
            }
            return id;
        }

        /// <summary>毒性/欣快（由组分计算并写入 fc_tox/fc_euph 的标签值）。</summary>
        static string ToxEuphLine(GameItem item)
        {
            int tox = ChemCatalog.ReadInt(item, ChemCatalog.TCompTox, 0);
            int euph = ChemCatalog.ReadInt(item, ChemCatalog.TCompEuph, 0);
            return LFallback("scanner_tox_euph", "Toxicity " + tox + " / Euphoria " + euph, tox, euph);
        }

        /// <summary>总量文本：液体为 500 ml/件（ChemRecipes.PourVolumeMl），其他为单件。</summary>
        static string VolumeText(GameItem item)
        {
            ChemSpec spec = ChemCatalog.Find(item.identifier);
            if (spec != null && spec.Kind == "liquid" && spec.Phase == Phase.Liquid)
            {
                // 审查 1.4：显示瓶里真实剩余量（以前固定写 500 ml）
                int ml = ChemCatalog.LiquidVolumeMl(item);
                return LFallback("scanner_fc_volume", ml + " ml", ml);
            }
            return LFallback("scanner_fc_single", "single item");
        }

        static string GradeName(GameItem item)
        {
            int idx = ChemPrinter.GradeIndexForPurity(ChemRuntime.PurityOf(item));
            return L(ChemPrinter.GradeKeys[idx]);
        }

        static string L(string key, params object[] args)
            => ModHelper.GetLocalized(ChemCatalog.ModId, key, args);

        /// <summary>缺键时回退英文兜底（避免把键名显示给玩家）。</summary>
        static string LFallback(string key, string fallback, params object[] args)
        {
            string s = ModHelper.GetLocalized(ChemCatalog.ModId, key, args);
            return (string.IsNullOrEmpty(s) || s == key) ? fallback : s;
        }
    }

    /// <summary>
    /// 原版反馈图标兜底：扫描仪/顾客反馈用的 IsLethal/IsToxic/GetLethalDose/GetEuphoriaLevel
    /// 只读原版 Liquid 组分；本模组物品用 fc_tox/fc_euph（ChemRuntime 画像）补齐，避免显示为安全。
    /// </summary>
    [HarmonyPatch(typeof(ChemicalFeedbackHelper), "IsLethal")]
    static class ChemIsLethalPatch
    {
        static void Postfix(GameItem item, ref bool __result)
        {
            if (__result || item == null) return;
            try
            {
                if (ChemCatalog.IsOurReagent(item.identifier) && ChemRuntime.ToxicityLoad(item) >= 1000)
                    __result = true;
            }
            catch (Exception e) { ChemRuntime.Log("[patch] IsLethal failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ChemicalFeedbackHelper), "IsToxic")]
    static class ChemIsToxicPatch
    {
        static void Postfix(GameItem item, ref bool __result)
        {
            if (__result || item == null) return;
            try
            {
                if (!ChemCatalog.IsOurReagent(item.identifier)) return;
                int load = ChemRuntime.ToxicityLoad(item);
                if (load >= 750 && load < 1000) __result = true;
            }
            catch (Exception e) { ChemRuntime.Log("[patch] IsToxic failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ChemicalFeedbackHelper), "GetLethalDose")]
    static class ChemLethalDosePatch
    {
        static void Postfix(GameItem item, ref int __result)
        {
            if (__result > 0 || item == null) return;
            try
            {
                if (ChemCatalog.IsOurReagent(item.identifier))
                    __result = ChemRuntime.ToxicityLoad(item);
            }
            catch (Exception e) { ChemRuntime.Log("[patch] GetLethalDose failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ChemicalFeedbackHelper), "GetEuphoriaLevel")]
    static class ChemEuphoriaPatch
    {
        static void Postfix(GameItem item, ref int __result)
        {
            if (__result > 0 || item == null) return;
            try
            {
                if (ChemCatalog.IsOurReagent(item.identifier))
                    __result = ChemRuntime.EuphoriaTier(item);
            }
            catch (Exception e) { ChemRuntime.Log("[patch] GetEuphoriaLevel failed: " + e.Message); }
        }
    }

    /// <summary>
    /// 顾客收购兜底（仅放宽 tag 匹配，不绕过拒绝/管制语义）。
    /// IsClientBuyingThisItem 有两个重载，必须显式写参数类型避免 Harmony ambiguous。
    /// </summary>
    [HarmonyPatch(typeof(StoreClient), "IsClientBuyingThisItem",
                  new[] { typeof(GameItem), typeof(bool) })]
    static class ChemClientBuyingPatch
    {
        static readonly HashSet<int> contrabandRiskNoted = new HashSet<int>();

        static void Postfix(StoreClient __instance, GameItem __0, bool __1, ref bool __result)
        {
            try
            {
                // P7c：controlled 物品被顾客购买时叠加违禁品交易风险（每件一次）
                if (__result && __0 != null
                    && ChemCatalog.ReadInt(__0, ChemCatalog.TControlled, 0) == 1
                    && contrabandRiskNoted.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__0)))
                    ChemRuntime.NoteContrabandTrade(__0);
                if (__result || __instance == null || __0 == null) return;
                if (!ChemCatalog.IsOurReagent(__0.identifier)) return;

                // 原版拒绝（纯度档/管制）优先：WillAcceptNarcotic 不通过时不放宽
                if (!ChemicalProductHelper.WillAcceptNarcotic(__0, __instance.acceptedChemicalProductGrade))
                    return;

                List<string> tags = __instance.clientBuyingTagList;
                if (tags == null) return;
                bool acceptsType = tags.Contains(TypeHelper.CHEMICAL_SUPPLIES)
                                   || tags.Contains(TypeHelper.MEDICAL)
                                   || tags.Contains("ALL_TYPE")
                                   || tags.Contains("ALL_TYPE_EXCEPT_CONTRABAND");
                if (!acceptsType) return;

                // 客户黑名单命中则不放宽
                List<string> blackTags = __instance.clientBlackTagList;
                if (blackTags != null)
                    foreach (var tag in blackTags)
                        if (!string.IsNullOrEmpty(tag) && __0.IsTag(tag)) return;

                __result = true;
                if (ChemCatalog.ReadInt(__0, ChemCatalog.TControlled, 0) == 1
                    && contrabandRiskNoted.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__0)))
                    ChemRuntime.NoteContrabandTrade(__0);
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[patch] client buying failed: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 倒液补丁（审查 1.2 / 5.4 重写）。
    ///  · 主路径交还原版 TransferLiquid（以前整段替换掉，原版的滤芯过滤、液温、源瓶外观/价格刷新都没执行）；
    ///    Postfix 只做本模组的记账：源瓶剩余量、实验容器内容记录（带纯度）、倒空的药剂瓶销毁。
    ///  · 禁止把别的液体倒进本模组药剂瓶（以前倒掉一半再灌自来水，还是一瓶原纯度的盐酸）；
    ///    同种药剂瓶之间可以合并，纯度取较低者。
    ///  · 旧存档缺 LIQUID_CONTAINER_TAG 的本模组液体仍走 PourParts 兜底。
    /// </summary>
    [HarmonyPatch(typeof(WaterHelper), "TransferLiquid")]
    static class ChemPourFallbackPatch
    {
        internal sealed class PourState
        {
            public int SourceBefore;
            public List<ChemCatalog.ContentEntry> SourceEntries;
        }

        static bool Prefix(GameItem sourceContainer, GameItem targetContainer, out PourState __state)
        {
            __state = null;
            try
            {
                if (sourceContainer == null || targetContainer == null) return true;

                // 目标是本模组药剂瓶：只允许同种药剂合并
                if (ChemCatalog.IsOurLiquid(targetContainer)
                    && !(ChemCatalog.IsOurLiquid(sourceContainer)
                         && sourceContainer.identifier == targetContainer.identifier))
                {
                    ChemRuntime.Log("[pour] blocked: cannot pour " + sourceContainer.identifier
                                    + " into reagent bottle " + targetContainer.identifier + ".");
                    return false;
                }

                bool sourceIsOurs = ChemCatalog.IsOurLiquid(sourceContainer);
                if (sourceIsOurs && !ChemCatalog.IsLiquidContainerItem(sourceContainer))
                {
                    LegacyPour(sourceContainer, targetContainer);
                    return false;
                }

                if (!ChemCatalog.IsLiquidContainerItem(sourceContainer)) return true;
                __state = new PourState
                {
                    SourceBefore = WaterHelper.GetTotalVolume(sourceContainer),
                    SourceEntries = ChemCatalog.IsVessel(sourceContainer.identifier)
                        ? ChemCatalog.ReadContainerEntries(sourceContainer) : null,
                };
                return true;   // 交给原版
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[pour] prefix failed: " + e.Message);
                __state = null;
                return true;    // 异常时交回原版，避免玩家卡住
            }
        }

        static void Postfix(GameItem sourceContainer, GameItem targetContainer, PourState __state)
        {
            if (__state == null || sourceContainer == null || targetContainer == null) return;
            try
            {
                int after = WaterHelper.GetTotalVolume(sourceContainer);
                int moved = __state.SourceBefore - after;
                if (moved <= 0) return;
                int movedMl = moved / ChemRecipes.PourPartPerMl;
                bool targetIsVessel = ChemCatalog.IsVessel(targetContainer.identifier);

                if (ChemCatalog.IsOurLiquid(sourceContainer))
                {
                    int purity = ChemCatalog.ReadInt(sourceContainer, ChemCatalog.TPurity, 0);
                    if (targetIsVessel)
                        ChemCatalog.AddContainerContent(targetContainer, sourceContainer.identifier, movedMl, purity);
                    if (ChemCatalog.IsOurLiquid(targetContainer))
                    {
                        // 同种合并：纯度取较低者（把源瓶的污染物状态带过去）
                        int tp = ChemCatalog.ReadInt(targetContainer, ChemCatalog.TPurity, 0);
                        if (purity < tp)
                        {
                            ChemCatalog.ApplyContaminants(targetContainer,
                                ChemCatalog.ReadContaminants(sourceContainer),
                                ChemCatalog.ReadPurifyLevel(sourceContainer));
                            ChemCatalog.SyncVanillaPurity(targetContainer);
                        }
                        ChemCatalog.SetPourVolumeMl(targetContainer, ChemCatalog.LiquidVolumeMl(targetContainer));
                    }
                    ChemCatalog.SetPourVolumeMl(sourceContainer, after / ChemRecipes.PourPartPerMl);
                    ChemRuntime.Log("[pour] " + sourceContainer.identifier + " → " + targetContainer.identifier
                                    + " " + movedMl + " ml" + (after <= 0 ? " (source empty)." : "."));
                    if (after <= 0) sourceContainer.Destroy();
                    return;
                }

                // 源是实验容器：内容记录按倒出比例搬走
                if (__state.SourceEntries != null && __state.SourceBefore > 0)
                {
                    foreach (var e in __state.SourceEntries)
                    {
                        int take = (int)((long)e.Ml * moved / __state.SourceBefore);
                        if (take <= 0) continue;
                        e.Ml -= take;
                        if (targetIsVessel)
                            ChemCatalog.AddContainerContent(targetContainer, e.Id, take, e.Purity);
                    }
                    ChemCatalog.WriteContainerEntries(sourceContainer, __state.SourceEntries);
                }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[pour] postfix failed: " + e.Message);
            }
        }

        /// <summary>旧存档/克隆的本模组液体缺原版液体 tag：用 PourParts + fc_volume_ml 倒出。</summary>
        static void LegacyPour(GameItem sourceContainer, GameItem targetContainer)
        {
            if (!ChemCatalog.IsLiquidContainerItem(targetContainer)) return;
            int free = WaterHelper.GetFreeCapacity(targetContainer);
            Dictionary<string, int> pourParts;
            if (free <= 0 || !ChemRecipes.TryGetPourParts(sourceContainer.identifier, out pourParts)) return;
            int remainMl = ChemCatalog.GetPourVolumeMl(sourceContainer);
            int pourMl = Math.Min(remainMl, free / ChemRecipes.PourPartPerMl);
            if (pourMl <= 0) return;
            ChemCatalog.PourPartsInto(targetContainer, pourParts, pourMl * ChemRecipes.PourPartPerMl);
            WaterFeatureHelper.OnLiquidContainerPourInto(targetContainer, sourceContainer, remainMl - pourMl <= 0);
            if (ChemCatalog.IsVessel(targetContainer.identifier))
                ChemCatalog.AddContainerContent(targetContainer, sourceContainer.identifier, pourMl,
                    ChemCatalog.ReadInt(sourceContainer, ChemCatalog.TPurity, 0));
            ChemCatalog.SetPourVolumeMl(sourceContainer, remainMl - pourMl);
            ChemRuntime.Log("[pour] " + sourceContainer.identifier + " → " + targetContainer.identifier
                            + " " + pourMl + " ml (legacy path, left " + (remainMl - pourMl) + " ml).");
            if (remainMl - pourMl <= 0) sourceContainer.Destroy();
        }
    }

    /// <summary>
    /// P6a：旧存档/克隆的本模组液体可能缺 LIQUID_CONTAINER_TAG（target 钩子丢失），
    /// 导致 WaterHelper.TransferLiquid 的倒液 Prefix 根本不触发。在 GameItem.Target 前先补桥接。
    /// 只在物品是本模组液体且缺容器 tag 时做一次（幂等、低成本）。
    /// </summary>
    [HarmonyPatch(typeof(GameItem), "Target")]
    static class ChemTargetBridgePatch
    {
        static bool Prefix(GameItem __instance, GameItem targetItem)
        {
            try
            {
                if (__instance == null || targetItem == null) return true;
                // P10b：接触即反应（不需要机器）
                if (ChemCatalog.TryContactReaction(__instance, targetItem)) return false;
                ChemSpec spec = ChemCatalog.Find(__instance.identifier);
                if (spec == null || spec.Kind != "liquid" || spec.Phase != Phase.Liquid) return true;
                if (__instance.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)) return true;
                if (ChemCatalog.BridgeVanillaChemData(__instance))
                    ChemRuntime.Log("[patch] liquid container bridged on Target: " + __instance.identifier);
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[patch] target bridge failed: " + e.Message);
            }
            return true;
        }
    }
}
