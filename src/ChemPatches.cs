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
        static string lastSignature;

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
                // ASCII 化学式仅内部数据/去重签名使用，不显示给玩家（P10c 用户可见行不出现缩写）
                string asciiFormula = ChemRecipes.Formula(item.identifier);
                string signature = item.identifier + "|" + purity + "|" + asciiFormula;
                if (signature == lastSignature) return;   // 同一物品同一纯度不重复写

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

                    lastSignature = signature;
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
            string comp = "";
            Dictionary<string, int> parts;
            if (ChemRecipes.TryGetComponentParts(item.identifier, out parts))
                foreach (var kv in parts)
                    comp += (comp.Length == 0 ? "" : " / ") + ComponentName(kv.Key)
                            + " " + (kv.Value / (ChemRecipes.ComponentTotalPart / 100)) + "%";
            if (comp.Length == 0) comp = LFallback("scanner_fc_pure", "pure substance");
            return name + " · " + volume + " · " + comp;
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
                return LFallback("scanner_fc_volume", ChemRecipes.PourVolumeMl + " ml",
                                 ChemRecipes.PourVolumeMl);
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
    /// P6a：倒液可靠兜底。原版 TransferLiquid 要求双方都有 LIQUID_CONTAINER_TAG；
    /// 旧存档/克隆物品的 tag 可能丢失 → 提前 return。此 Prefix 在 source 是本模组液体时接管：
    /// 按 PourParts + 源剩余体积（fc_volume_ml，默认 500）+ 目标剩余容量限流，
    /// 直接 AddLiquid 注入目标、扣减源体积、源空销毁，返回 false 跳过原版逻辑。
    /// 目标只需是原版液体容器（LIQUID_CONTAINER_TAG），不要求 source 有 tag。
    /// </summary>
    [HarmonyPatch(typeof(WaterHelper), "TransferLiquid")]
    static class ChemPourFallbackPatch
    {
        static bool Prefix(GameItem sourceContainer, GameItem targetContainer)
        {
            try
            {
                if (sourceContainer == null || targetContainer == null) return true;

                ChemSpec spec = ChemCatalog.Find(sourceContainer.identifier);
                if (spec == null || spec.Kind != "liquid" || spec.Phase != Phase.Liquid) return true;
                // target 必须是原版液体容器（LIQUID_CONTAINER_TAG）；source 不要求有 tag
                if (!ChemCatalog.IsLiquidContainerItem(targetContainer)) return true;

                // 目标剩余容量（part，1 ml = 1000 part）
                int free = WaterHelper.GetFreeCapacity(targetContainer);
                if (free <= 0)
                {
                    ChemRuntime.Log("[pour] target " + targetContainer.identifier + " is full; skip.");
                    return false;
                }

                // 主路径：源仍是原版液体容器 → RemoveAndReturn 按比例扣减源 tag 并返回组分
                int srcPart = WaterHelper.GetTotalVolume(sourceContainer);
                if (srcPart > 0)
                {
                    int move = Math.Min(free, srcPart);
                    var parts = WaterHelper.RemoveAndReturn(sourceContainer, move);
                    if (parts != null)
                        foreach (var kv in parts)
                            WaterHelper.AddLiquid(targetContainer, kv.Key, kv.Value);

                    bool sourceEmpty = WaterHelper.GetTotalVolume(sourceContainer) <= 0;
                    WaterFeatureHelper.OnLiquidContainerPourInto(targetContainer, sourceContainer, sourceEmpty);
                    if (ChemCatalog.IsVessel(targetContainer.identifier) || targetContainer.identifier == "test_tube")
                        ChemCatalog.AddContainerContent(targetContainer, sourceContainer.identifier, move / ChemRecipes.PourPartPerMl);
                    // 记录【剩余】体积而不是倒出量：否则部分倒液后 fc_volume_ml 错误，可重复倒出（资源复制漏洞）。
                    ChemCatalog.SetPourVolumeMl(sourceContainer,
                        WaterHelper.GetTotalVolume(sourceContainer) / ChemRecipes.PourPartPerMl);
                    ChemRuntime.Log("[pour] " + sourceContainer.identifier + " → " + targetContainer.identifier
                                    + " " + (move / ChemRecipes.PourPartPerMl) + " ml"
                                    + (sourceEmpty ? " (source empty)." : "."));
                    if (sourceEmpty) sourceContainer.Destroy();
                    return false;
                }

                // 兜底路径：源没有原版液体 tag（旧存档/克隆）→ 用 PourParts + fc_volume_ml
                Dictionary<string, int> pourParts;
                if (!ChemRecipes.TryGetPourParts(sourceContainer.identifier, out pourParts)) return true;
                int remainMl = ChemCatalog.GetPourVolumeMl(sourceContainer);
                int freeMl = free / ChemRecipes.PourPartPerMl;
                int pourMl = Math.Min(remainMl, freeMl);
                if (pourMl <= 0)
                {
                    ChemRuntime.Log("[pour] no room/volume for " + sourceContainer.identifier + "; skip.");
                    return false;
                }
                ChemCatalog.PourPartsInto(targetContainer, pourParts, pourMl * ChemRecipes.PourPartPerMl);
                WaterFeatureHelper.OnLiquidContainerPourInto(targetContainer, sourceContainer,
                    remainMl - pourMl <= 0);
                if (ChemCatalog.IsVessel(targetContainer.identifier) || targetContainer.identifier == "test_tube")
                    ChemCatalog.AddContainerContent(targetContainer, sourceContainer.identifier, pourMl);
                ChemCatalog.SetPourVolumeMl(sourceContainer, remainMl - pourMl);
                ChemRuntime.Log("[pour] " + sourceContainer.identifier + " → " + targetContainer.identifier
                                + " " + pourMl + " ml (legacy path, left " + (remainMl - pourMl) + " ml).");
                if (remainMl - pourMl <= 0) sourceContainer.Destroy();
                return false;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[pour] fallback failed: " + e.Message);
                return true;    // 异常时交回原版，避免玩家卡住
            }
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
