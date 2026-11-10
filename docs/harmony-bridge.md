# Harmony 桥接原版研究（成分仪 / 倒液 / 纯度标签 / 顾客收购 / 打印机）

> 只读研究：全部结论来自 `Assembly-CSharp.dll` 元数据 + IL（探针 `tools/api-probe/`，本轮新增第 15 组 selectors；
> `out/il/` 现约 820 个方法 IL）。**未改 src、未做游戏内验证**。用户已授权 Harmony 补丁与纯度体系重做。
>
> 探针输出命名：`il/<类型>__<方法>.il.txt`；`members.txt`（签名/可见性/常量）；`strings.txt`（`ldstr` 表）；`xref.txt`（成员交叉引用）。

## 0. 结论速览

| # | 问题 | 结论 | 推荐做法 |
|---|---|---|---|
| 1 | 成分仪为何对 mod 独立物品显示空 | 它只认原版 tag/feature/液体组分，不认我们的 `fc_*` tag | **数据桥接即可，不必补丁**：给物品打原版 tag + ItemFeature + 原版液体组分；补丁只作兜底 |
| 2 | 倒液 | 原版 `WaterHelper.TransferLiquid` 只认 `LIQUID_CONTAINER_TAG` + 原版液体组分 tag | 物品工厂用 `WaterHelper.InitLiquidContainerItem` + `AddLiquid`（映射表见 §2.3）；已存档旧物品用 Harmony Prefix 兜底 |
| 3 | 纯度/标签 | `CHEMICAL_PRODUCT_PURITY_TAG`(double 0–100) + `CATEGORY_CHEMICAL_PURITY` 的 `ItemFeature`（`ItemCondition` 七档）；水体系用 0–10000 | 用 `GradeProduct`/`SetPurityFeature` 或按比例写组分；价值差由 `ItemCondition.modValue` 自动进入报价 |
| 4 | 顾客收购 | `StoreClient.IsClientBuyingThisItem` 依次看 `IsClientRefusingItem` → `ALL_TYPE` → `clientBuyingIdList` → `ALL_TYPE_EXCEPT_CONTRABAND` → `clientBuyingTagList` 逐 tag → `willAcceptItemPreference`；纯度档由 `acceptedChemicalProductGrade`+`WillAcceptNarcotic` 卡 | 优先给物品打客户列表里的 tag；兜底 Harmony Postfix `IsClientBuyingThisItem(GameItem,bool)` |
| 5 | Harmony 接入 | 加载器自动 PatchAll；`LoadedMod.HarmonyInstance`（id `mod.<模组ID>`）可获取；游戏代码补丁**不需要**任何 Capability，`<Capabilities />` 保持空 | 只写 `[HarmonyPatch]` 类，不手动 PatchAll；不要 UnpatchAll(null)；不要引用/捆绑 0Harmony |
| 6 | 打印机可行性 | **可行**：`PixelWindow` + `GameSlotInventory`/`GameGridInventory` + `GridPixelElement`/`TagElement`，本模组加热台已在用同一套 API | 复用 `src/ChemMachines.cs` 的窗口构建模式（见 §6） |

---

## 1. 成分光谱仪（chem_scanner）

### 1.1 触发链（IL 证据）

`MedsItemDirectory.ChemScanner()`（`il/MedsItemDirectory__ChemScanner.il.txt`）创建原版扫描仪物品，并注册 **GameItemFunc 物品-对-物品钩子**：

- `mayThisTargetItemFunc` / `canThisTargetItemFunc` / `onItemTargetItemFunc`（`il/GameItemFunc__*.il.txt` 同名成员）；
- 目标接受条件在 `MedsItemDirectory+<>c::<ChemScanner>b__25_2/3/4`：要求目标 `IsTag("CHEM_ITEM_TAG")` 或 `IsTag("CHEMICAL_PRODUCT_TAG")`（两者都出现在 `strings.txt` 的对应方法里）；
- `onItemTargetItemFunc` → `ChemScannerUIManager.Instance.Open(scanner, target)`（xref 唯一调用方）。

`ChemScannerUIManager.Open(GameItem scanner, GameItem water)`（`il/ChemScannerUIManager__Open.il.txt`）：
面板 `SetActive(true)` → `currentScannedItem = 被扫描物品` → `RefreshUI()` → 起 `CallFunctionWhileCondition` 协程。

### 1.2 显示行生成（`ChemScannerUIManager.RefreshUI` / `RefreshChemMode`）

`RefreshUI`（`il/ChemScannerUIManager__RefreshUI.il.txt`，332 行）：

1. 无物品：清空 `testedTMP/labelTMP/contaminantTMP/totalVolumeTMP`、隐藏按钮与效果图标；
2. 有物品：`productNameTMP.text = item.name`；
3. **分支判据是 `item.IsTag("CHEM_ITEM_TAG")`**：真 → `RefreshChemMode()`，假 → `RefreshWaterMode()`（水容器体系，读 `Liquid.Liquids` 的组分 tag）；
4. **总液量** = `Σ Liquid.Liquids[i].currentPart` tag 的 `GetInt()`（即每种原版液体一个 int tag，tag 名 = `Liquid.currentPart`）；
5. 杂质/组分行：对每个 `Liquid`，若该 tag >0 则 `contaminantTMP += string.Format("{0}: {1} ({2:F2}%)\n", liquid.displayName, FormatAmount(amount), amount/total*100)`；
6. 总容量行：`mech_scanner_total_volume`（本地化机制键）；
7. 毒性/欣快/抗成瘾/避孕图标：`ChemicalFeedbackHelper.IsLethal/IsToxic/IsNarcotic/GetEuphoriaLevel/IsSynaprest/IsBirthControl`。

`RefreshChemMode`（`il/ChemScannerUIManager__RefreshChemMode.il.txt`）：

```
if (!item.IsTag("CHEMICAL_PRODUCT_TAG")) { testedTMP=""; labelTMP=""; return; }   // ← mod 物品显示空的直接原因
purity = item.GetTagReadonly("CHEMICAL_PRODUCT_PURITY_TAG").GetDouble();
testedTMP = loc("mech_tooltip_tested") + "\n" + FormatPurityPercentageFromDouble(purity)
          + "\n" + item.FindItemFeatureByCategory("CATEGORY_CHEMICAL_PURITY").realCondition.display;
labelTMP  = loc("mech_tooltip_labeled") + "\n" + ...fakeCondition.display;
```

**为什么 mod 独立物品是空**：我们的物品没有 `CHEM_ITEM_TAG`/`CHEMICAL_PRODUCT_TAG`、没有 `CATEGORY_CHEMICAL_PURITY` 的 `ItemFeature`、没有原版液体组分 tag，
所以既进不了 chem 分支，也没有纯度/组分可显示。

### 1.3 推荐实现（优先数据桥接，0 补丁）

在物品工厂（或首次进店时）给每个药剂物品补齐原版数据：

```csharp
// 可见性前提：全部是 public static / public 实例成员（见 members.txt）。
// 1) 物品身份 tag（成分仪分支 + 客户 tag 匹配都靠它）
item.EnableTag("CHEM_ITEM_TAG", false);
item.EnableTag("CHEMICAL_PRODUCT_TAG", false);

// 2) 原版液体组分（水量体系共用；单位是 part，1000 part = 1 ml，见 §2.2）
WaterHelper.AddLiquid(item, "acid", 150000);   // 150 ml 的 acid 组分
WaterHelper.AddLiquid(item, "water", 100000);  // 100 ml water（总量 250 ml）

// 3) 纯度：让原版自己算（GradeProduct = ACTIVATE_INGREDIENT 液体占比 ×100 → 纯度 tag + 七档 ItemCondition）
item.ModifyTag(ChemicalProductHelper.ACTIVATE_INGREDIENT_TAG,
               s => s.Enable().SetString("acid"), false);   // 主成分液体 id
ChemicalProductHelper.GradeProduct(item);                   // 触发 SetPurityFeature
// 或者不用 GradeProduct，直接写：
// item.ModifyTag(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG, s => s.Enable().SetDouble(92.5), false);
// ChemicalProductHelper.SetPurityFeature(item, ItemConditionList.CreatePharmaGrade());
```

补充：`ChemicalProductHelper.InitChemicalProduct(item, liquidId)`（`il/ChemicalProductHelper__InitChemicalProduct.il.txt`）=
`EnableTag("CHEMICAL_PRODUCT_TAG")` + 设置 `ACTIVATE_INGREDIENT_TAG` 液体 id + `GradeProduct` —— 一条调用完成身份+纯度（前提是先有该液体组分）。

### 1.4 兜底补丁（若不想改所有物品/旧存档）

可 patch（Postfix，`__instance` 的字段 `testedTMP/labelTMP/contaminantTMP/totalVolumeTMP` 都是 **public 字段**，Harmony 可直接读写）：

```csharp
[HarmonyPatch(typeof(ChemScannerUIManager), nameof(ChemScannerUIManager.RefreshChemMode))]
static class ScannerShowModChem
{
    static void Postfix(ChemScannerUIManager __instance)
    {
        GameItem it = __instance.currentScannedItem;           // public 字段
        if (it == null || !ChemCatalog.IsOurReagent(it.identifier)) return; // 自家判断
        double p = ChemRuntime.PurityOf(it);                   // 自家纯度 0–100
        __instance.testedTMP.text  = ModHelper.GetLocalized("free_chem", "scanner_tested")
                                   + "\n" + WaterFeatureHelper.FormatPurityPercentageFromDouble(p);
        __instance.labelTMP.text   = ModHelper.GetLocalized("free_chem", "scanner_labeled")
                                   + "\n" + ChemCatalog.GradeDisplay(it);   // 自家七档文案
    }
}
```

风险：`RefreshUI` 前面已清空字段，Postfix 在 `RefreshChemMode` 之后写即可覆盖；若之后游戏再调 `RefreshUI` 会重算（我们的 Postfix 每次都在）。
另可 Patch `ChemicalFeedbackHelper.GetItemFeedbackLevel/IsLethal/IsToxic`（public static）让扫描仪效果图标也识别我们（tox/euphoria 已由我们 `fc_tox/fc_euph` 提供）。

### 1.5 原版组分 id（`Liquid` public const string，完整列表见 `members.txt`）

`water, heavy_metal, microbe, organic_waste, mineral, chemical_contaminant, physical_contaminant, microplastic, dream_cap_mush, dream_cap_extract, plant_matter,
alcohol, caffeine, protein, fat, starch, sugar, coffee_mush, contraceptive, zyanide, nightmare_extract, nightmare_cap_mush, chlorine, synaprest, fanta, methylol,
reducing_agent, acid, base, stabilizing_agent, debris, lipobond_agent, livestock_steroid, livestock_immunity, wine_extract, nomore`。
对应显示名在 `Liquid.displayName`（由 `Liquid.InitLiquid()` 构造；**没有独立本地化键**，`displayName` 本身就是已本地化/固定文案）。

---

## 2. 倒液 / 加料

### 2.1 原版倒液入口（IL 证据）

**水/液体容器体系（主路径）**：`WaterHelper.TransferLiquid(GameItem sourceContainer, GameItem targetContainer)`（`il/WaterHelper__TransferLiquid.il.txt`，522 行）：

1. 要求 **两者都有 `LIQUID_CONTAINER_TAG`**，否则直接 return；
2. `capacity = target.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").GetInt()`；
3. `targetUsed = Σ Liquid.currentPart tag`；`free = capacity - targetUsed`；`sourceTotal = Σ 同 tag`；`amount = min(free, sourceTotal)`；
4. `WaterHelper.SplitProportionally(sourceParts, amount)` 按比例分液；每个 liquid：
   `WaterHelper.HandleFilter(...)` → `ChemicalHelper.HandleReceiveLiquidTemp(target, amount, source)` →
   `target.ModifyTag(part, +=amount)` / `source.ModifyTag(part, -=amount)`；
5. 收尾：`WaterFeatureHelper.OnLiquidContainerPourInto(target, source, wasEmpty)` + `OnLiquidContainerPourOutOf(source)`。

**通用储液容器体系（另一套 tag）**：`LiquidContainerHelper.TransferLiquid(targetItem, sourceItem)`（`il/LiquidContainerHelper__TransferLiquid.il.txt`）：
用 `LIQUID_CONTAINER_CURRENT` / `LIQUID_CONTAINER_MAX` 传"总量"，不区分液体身份；`LiquidContainerHelper.InitLiquidContainerItem` 的钩子里调它。

**单位**：`WaterHelper.InitLiquidContainerItem(item, capacityInMl, ...)` 里 `LIQUID_CONTAINER_CAPACITY = capacityInMl * 1000`（`il/WaterHelper__InitLiquidContainerItem.il.txt` IL_00CF-00D7），
即 **1 ml = 1000 part**；`WaterHelper.AddLiquid(container, liquidId, addAmount)` 的 `addAmount` 也是 part。

**倒液交互本身由物品钩子触发**：`WaterHelper.InitLiquidContainerItem` 给物品注册 GameItemFunc 物品-对-物品钩子（含 `b__5_15`/`b__9` 等 lambda 调 TransferLiquid），
所以**不要自己写 UI**——让物品成为原版液体容器即可获得原版拖拽/倒液交互。

### 2.2 让 mod 液体被识别（推荐：工厂数据桥接，0 补丁）

```csharp
// 物品工厂里（只对"可倒的液体药剂"做）：
WaterHelper.InitLiquidContainerItem(item, capacityMl: 250, basePrice: 55); // 加 LIQUID_CONTAINER_TAG/CAPACITY、温度、钩子
WaterHelper.AddLiquid(item, "acid",  150 * 1000);   // 150 ml 主成分（acid）
WaterHelper.AddLiquid(item, "water", 100 * 1000);   // 100 ml 水
// 之后原版 WaterHelper.TransferLiquid / 原版混合器/水罐都能把它当液体容器处理。
```

注意：`AddLiquid` 会 `UpdateWaterFeature` + `SpriteHelper.UpdateWaterContainerSprite`（自动加水特征/换图标）；`InitLiquidContainerItem` 会
`ChemicalFeedbackHelper.InitChemicalIntentItem` + `ChemicalHelper.InitContainerTemp`，因此**先 Init 再 Add**。
另外 `WaterHelper.AddLiquid` 对数进行 `HandleReceiveLiquidTemp(item, amount, 26.0)`（室温 26°C），与 HANDOFF §3.3 一致。

### 2.3 mod 药剂 → 原版组分映射表（建议写进 ChemRecipes，纯数据便于 QA 断言）

| mod 液体物品 | 原版组分（占 part） | 说明 |
|---|---|---|
| `liq_hydrochloric_acid` | `acid` : `water` ≈ 0.365 g/ml 对应体积比；建议 1 M HCl = 8% w/w → acid 20% / water 80%（近似） | 盐酸是强酸溶液，映射 acid+water |
| `liq_sulfuric_acid` | `acid` + `water`（浓硫酸 98% → acid 高） | 同上 |
| `liq_nitric_acid` | `acid` + `water` | 同上 |
| `liq_sodium_hydroxide` | `base` + `water` | 碱液 |
| `liq_ammonia_water` | `base` + `water` | 氨水是弱碱 |
| `liq_brine` / 盐水 | `water` + `mineral` | 盐水不是酸碱 |
| `liq_ethanol` | `alcohol` + `water` | 纯度→酒精占比 |
| `liq_water` | `water` | 纯水 |
| `liq_hydrogen_peroxide` | **无忠实映射**（原版 Liquid 无过氧化物）→ 不自动倒；如必须，请求 lead 决策（例如不映射/仅当"清洁剂"特例） | 避免凭空造出化学不存在的组分 |
| `gas_*`（气体） | 不适用（气体不是液体容器） | 保持独立物品 |

**守恒**：`Σ 映射后 part 体积 = 原瓶 ml × 1000`；**不得**凭空增加目标容器液量；目标容器满时只倒入 `min(free, ...)`（交给 `TransferLiquid` 自身或我们的 Prefix 计算）。
纯度编码：主成分占比 = 纯度（见 §3），即"纯度"变成可被原版计算的组分比例。

### 2.4 兜底补丁（已存档旧物品 / 不想改工厂时）

```csharp
[HarmonyPatch(typeof(WaterHelper), nameof(WaterHelper.TransferLiquid))]
static class PourModLiquidBridge
{
    // 仅在 source 是我们自己的液体瓶、且没有原版液体组分时接管；否则走原版
    static bool Prefix(GameItem sourceContainer, GameItem targetContainer)
    {
        if (!ChemCatalog.TryGetPourParts(sourceContainer.identifier, out var parts)) return true; // 非 mod 液体：原版
        int free = WaterHelper.GetFreeCapacity(targetContainer);           // 公开 API
        if (free <= 0 || !targetContainer.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)) return false;
        bool wasEmpty = WaterHelper.IsEmpty(targetContainer);
        int ml = ChemRuntime.VolumeMl(sourceContainer);                    // 自家 ml（clamp 到 free）
        int totalPart = Math.Min(free, ml * 1000);
        foreach (var kv in ChemCatalog.ScaleParts(parts, totalPart))       // 按比例分配
            WaterHelper.AddLiquid(targetContainer, kv.Key, kv.Value);     // 原版加组分
        ChemRuntime.ConsumeVolumeMl(sourceContainer, totalPart / 1000);    // 消耗自家体积
        WaterFeatureHelper.OnLiquidContainerPourInto(targetContainer, sourceContainer, wasEmpty);
        WaterFeatureHelper.OnLiquidContainerPourOutOf(sourceContainer);
        return false; // 完全接管，跳过原版
    }
}
```

风险：`Prefix` 返回 false 会跳过原版全部逻辑（包括 filter/温度/水特征更新），所以上面手动补了特征更新；温度 `ChemicalHelper.HandleReceiveLiquidTemp` 也应调用（目标容器温度守恒）。
目标若是原版混合器里的化学容器（`CHEM_ITEM_TAG` 体系），用 `ChemicalHelper.TransferLiquidToChem(GameItem source, GameItem target, int amount)` 更合适（见 `strings.txt`/xref：`MachineChemFinisher` 使用它；签名可从 `members.txt` 取得，建议优先用它，避免自己动组分 tag）。

---

## 3. 纯度 / 标签体系（原版表示）

### 3.1 化学产品（药剂）

- `ChemicalProductHelper`（`il/ChemicalProductHelper__*.il.txt`）：
  - tag：`CHEMICAL_PRODUCT_TAG`（bool 身份）、`CHEMICAL_PRODUCT_PURITY_TAG`（**double，0–100**）、`ACTIVATE_INGREDIENT_TAG`（string，主成分液体 id）；
  - 七档常量：`STARCH_GRADE=0, JUNK_GRADE=30, CUT_PRODUCT_GRADE=45, STREET_STANDARD_GRADE=60, NEAR_PHARMA_GRADE=80, PHARMA_GRADE=90, PURE_GRADE=99`；
  - 条件 id（`ItemCondition.identifier`）：`starch_grade / junk_grade / cut_product / street_standard / near_pharma / pharma_grade / lab_pure`（对应 `ItemConditionList.CreateStarchGrade()` … `CreateLabPure()`）；
  - `GradeProduct(item)`：`purity = (GetTagReadonly(ACTIVATE_INGREDIENT 对应 currentPart).GetInt() / ChemicalHelper.GetTotalVolume(item)) * 100`，
    写 `CHEMICAL_PRODUCT_PURITY_TAG`，再按阈值 `SetPurityFeature(item, condition)`；
  - `SetPurityFeature(item, condition)`：找/建 `ItemFeature(category="CATEGORY_CHEMICAL_PURITY")`（identifier `chemical_purity`）→ `SetConditions(fake=condition, real=condition)`；
  - `GetPurity(item)`：读 purity tag（无则 0）；`HasChemicalPurity(item)`：有 `CHEMICAL_PRODUCT_TAG`；
  - `WillAcceptNarcotic(item, grade)`：拒绝 starch/junk 永远；`grade>=2` 再拒 cut_product；`grade>=3` 再拒 street_standard；否则接受（neat 阈值）。
- **本地化/显示**：`ItemCondition.display`（`ItemConditionList` 构造的本地化串）；扫描仪用 `realCondition.display/fakeCondition.display`；
  提示框走 `GameItem.GetTooltipAdvanced` 的 ItemFeature 行（`ItemFeature.GetPublicDisplay/GetActualDisplay`）。
- **价值**：`ItemCondition.modValue`（int）+ `ItemFeature` 的 `valueModifier` 进入 `GameItem.GetCurrentValue` 的 feature 阶段；
  即**只要 SetPurityFeature 成功，纯度自动影响报价**（不需要单独 patch 价格）。各档具体 `modValue` 由 `ItemConditionList..cctor` 构造（未逐值核对，风险项）。

### 3.2 水体系（水/液体容器）

- `LIQUID_CONTAINER_TAG` + 各液体 part tag；纯度 `WaterHelper.GetWaterPurity` = `water` part / 总 part × **10000**（`9990`＝99.9%，`il/WaterHelper__GetWaterPurity.il.txt`）；
- 等级阈值（`WaterFeatureHelper` 常量）：`PURE 9990 / HIGH 9900 / BASE 9800 / GHOST 9600 / RUST 9000 / GUTTERFLOW 3500`；
  `WaterFeatureHelper.GetConditionFromPurity(int)` 返回对应的 `ItemConditionList.CreatePureWater()…CreateGutterflow()`；
- 价格：`WaterFeatureHelper.GetWaterPrice(int totalLiquidParts)` → `item.unitValue = GetWaterPrice(GetTotalVolume(item))`（`OnLiquidContainerPourInto` 里可见）；
- 医用/化学纯识别：**原版没有"医用纯气体"概念**；他们识别的是
  - 物品类型 tag：`TypeHelper.MEDICAL / CHEMICAL_SUPPLIES / NARCOTIC / POISON / OXYGEN_SUPPLY / WATER`（`SetGameItemType` → 对应 tag，客户 `clientBuyingTagList` 里就是这些字符串）；
  - 化学纯度档：`CHEMICAL_PRODUCT_PURITY_TAG` + `CATEGORY_CHEMICAL_PURITY` feature（§3.1）；
  - 水纯度档：水 tag 比例（§3.2）。

### 3.3 让「mod 物品 + 纯度 tag + 化学品/医疗标签」被原版识别（三处，均可用数据实现）

| 原版系统 | 需要的条件 | 实现（无需补丁） |
|---|---|---|
| 成分仪 | `CHEM_ITEM_TAG`（进 chem 模式）+ `CHEMICAL_PRODUCT_TAG` + purity tag + `CATEGORY_CHEMICAL_PURITY` feature（+液体组分 tag 供总量/杂质行） | `InitChemicalProduct/GradeProduct/SetPurityFeature` + `WaterHelper.AddLiquid`（§1.3） |
| 提示框 | 同上 feature；`GameItem.GetTooltipAdvanced` 自动加 ItemFeature 行 | 同上；`ItemFeature.SetPublicDisplay/SetActualDisplay` 可加自定义文案行 |
| 出售/收购 | `item.IsTag(clientBuyingTagList 某 tag)`（类型 tag 如 `CHEMICAL_SUPPLIES`/`MEDICAL`）+ `IsClientRefusingItem` 通过（化学档 `acceptedChemicalProductGrade`） | 物品工厂已 `SetGameItemType(TypeHelper.CHEMICAL_SUPPLIES / MEDICAL)`；补 `CHEMICAL_PRODUCT_TAG`+档位；必要时 Patch（§4.3） |

---

## 4. 顾客收购逻辑

### 4.1 决策链（IL）

`PlayerStore.CanSellThisItem(GameItem, bool ignoreBudget = false)`（`il/PlayerStore__CanSellThisItem.il.txt`）：
后柜台必须有物品 → `currentClientInstance.GetClientBlueprint()`（`StoreClient`）→

1. `client.IsClientBuyingThisItem(item)` 为 false → 不可卖；
2. `client.CanClientExposeAnyFeatureDeep(item)` 为 true → 可以卖（用于当面对质骗局）；
3. `ignoreBudget=false && client.useClientBudget` 时 `client.HasBudgetLeftToBuy(item)`（`GetNegociatedValue() > clientBudget` → false）；
4. `client.isRequireVariety` 时 `IsItemInVarietyList(item)` 为 true → 不可卖（要求多样化）。

`StoreClient.IsClientBuyingThisItem(GameItem, bool ignoreContents)`（`il/StoreClient__IsClientBuyingThisItem_2.il.txt`）：
`IsClientRefusingItem(item, ignoreContents)` → 若拒绝则 false；否则
`clientBuyingTagList.Contains("ALL_TYPE")` → true；
`clientBuyingIdList.Contains(item.identifier)` → true；
`clientBuyingTagList.Contains("ALL_TYPE_EXCEPT_CONTRABAND")` → 无 `CONTRABAND` tag 则 true；
**遍历 `clientBuyingTagList`，`item.IsTag(tag)` 任一命中 → true**；
最后 `willAcceptItemPreference?.Invoke(item)`（private Func，public 属性 `willAcceptItem` 可 get/set）。

`IsClientRefusingItem`（public，本轮回转时未展开 IL）被 xref 证实调用
`ChemicalProductHelper.HasChemicalPurity/WillAcceptNarcotic`（`out/xref.txt`）→ 化学产品的纯度档位卡在这里（`acceptedChemicalProductGrade`）。
`StoreClient` 的 `clientBuyingTagList / clientBuyingIdList / clientBlackTagList / clientBlackIdList / acceptedChemicalProductGrade` 都是 **public 字段/属性**，
客户工厂（`StoreClientList::CreateLowerLevelChemist/CreatePoisonBuyer`、`StoreClientListSupplier::CreateCourrierChem` 等）用 `CHEMICAL_SUPPLIES` 等类型字符串填列表（`strings.txt`）。

### 4.2 结论：优先数据桥接

- 我们的物品已 `SetGameItemType(TypeHelper.CHEMICAL_SUPPLIES/MEDICAL)`；若该类型以 tag 形式落到物品（游戏用 `IsTag` 判断，见上），
  则原版化学/医疗客户（`clientBuyingTagList` 含同类 tag）**本来就能买**；
- 挡住的是**纯度档**：给物品补 `CHEMICAL_PRODUCT_TAG`+纯度档（§3.1）后，`WillAcceptNarcotic(item, acceptedChemicalProductGrade)` 才会通过；
- 报价：`GetNegociatedValue/GetCurrentValue` 自动含 ItemFeature 的价值修正 → 不需要补丁改价格。

### 4.3 兜底补丁（当原版客户的 tag 列表里没有我们的类型/需要放宽）

```csharp
[HarmonyPatch(typeof(StoreClient), nameof(StoreClient.IsClientBuyingThisItem), new[] { typeof(GameItem), typeof(bool) })]
static class ClientAcceptsModItems
{
    static void Postfix(StoreClient __instance, GameItem gameItem, bool ignoreContents, ref bool __result)
    {
        if (__result || gameItem == null) return;
        // 只放宽我们自己的物品：有 CATEGORY_CHEMICAL_PURITY 档位，且客户接受化学品/医疗大类
        if (ChemCatalog.IsOurReagent(gameItem.identifier) &&
            (__instance.clientBuyingTagList.Contains(TypeHelper.CHEMICAL_SUPPLIES) ||
             __instance.clientBuyingTagList.Contains(TypeHelper.MEDICAL) ||
             __instance.clientBuyingTagList.Contains("ALL_TYPE")))
        {
            __result = true;   // 注意：不覆盖 IsClientRefusingItem 的"拒绝"语义 → 只放宽 tag 匹配
        }
    }
}
```

风险：`IsClientRefusingItem` 在前，若它因纯度档拒绝，本 Postfix 仍无法绕过（`__result` 已 false 且我们没有判断拒绝原因）——**应先让纯度体系正确**；
如果确实要绕过，可另 Patch `StoreClient.IsClientRefusingItem` 的 Postfix，但那是玩家可见的"欺骗"语义，需 lead 明确决策。

---

## 5. Harmony 接入规范（官方文档 + 程序集证据）

- **补丁会自动应用**：加载器扫描已加载模组程序集里的每个 `[HarmonyPatch]` 类并应用；**不要**自己 `PatchAll()`（`ref-modding/ModdingGuide.zh-CN.md:1498-1502`）。
- **Harmony 实例**：`ModLoader.Mods.Find(m => m.Manifest.ID == "free_chem").HarmonyInstance`，其 id 为 `"mod.free_chem"`
  （`LoadedMod.HarmonyInstance` 是 public 字段；`ModLoader.Mods` public static 属性）。手动 Patch 才需要它，并且只能用自己的 id 撤销。
- **Capabilities**：修补**游戏代码**不需要任何能力——`<Capabilities />` 保持空即可。
  禁止/风险：patch 引擎/系统类型（`System.*`、`UnityEngine.*` 除 UI 命名空间、`HarmonyLib.*`、`Steamworks.*`、`ES3.*`）属于已弃用 `reflection-external`；
  不要补丁加载器/信任代码（`ModLoader/ModTrust*/ModScanner/...`）；不要 `UnpatchAll()`（字面 null 会被扫描器直接拒绝）；不要自带 `0Harmony.dll`（csproj 已引用游戏目录的副本）。
- **OnEnable/OnDisable**：自动补丁随模组启用自动应用/停用自动移除（加载器管理，`ModdingGuide.zh-CN.md:1375` 停止模组会移除其补丁）；
  建议 `OnDisable` 里**不要**手动 `UnpatchAll`；如确有手动补丁，用 `harmony.Unpatch(original, HarmonyPatchType.All, harmony.Id)` 或 `harmony.UnpatchAll(harmony.Id)`（显式 id）。
- **示例（本模组可直接加，引用现有 tag 常量）**：

```csharp
using HarmonyLib;

[HarmonyPatch(typeof(WaterHelper), nameof(WaterHelper.TransferLiquid))]
static class ModLiquidPourBridge { /* 见 §2.4 */ }

[HarmonyPatch(typeof(ChemScannerUIManager), nameof(ChemScannerUIManager.RefreshChemMode))]
static class ModScannerLines { /* 见 §1.4 */ }

[HarmonyPatch(typeof(StoreClient), nameof(StoreClient.IsClientBuyingThisItem),
              new[] { typeof(GameItem), typeof(bool) })]
static class ModClientBuy { /* 见 §4.3 */ }
```

`FreeChem.csproj` 已包含 `<Reference Include="0Harmony">`（指向游戏 `Managed\0Harmony.dll`），无需改工程。

---

## 6. 打印机可行性

**可行**。本模组 `src/ChemMachines.cs` 已经在用同一套 API 构建「单槽/多槽 + 标签 + 按钮」的容器物品窗口：

| 需求 | 可复用 API（public） | 证据/签名 |
|---|---|---|
| 弹出窗口 | `new PixelWindow(bool canMove=false, string title=null, WindowStyle style=0)` / `PixelWindow(int width,int height,bool canMove,...)`；`.Show()`、`.Center()`、`.SetTitle()`、`.Validate()` | `members.txt` PixelWindow 段；src/ChemMachines.cs:185 `new PixelWindow(true)` |
| 布局 | `new GridPixelElement(int gridWidth,int gridHeight,bool isCentered=false)`；`.Attach(PixelElement, int x,int y,int minW=0,int minH=0)`、`.AttachPos`、`.SetSpacing` | src:186/191/525-534 |
| 单槽 | `new GameSlotInventory()`；`.SetBackgroundFadeSprite(atlas,sprite)`、`.SetMachineryStyle()`、`SetTooltipName`；`mayInventoryAddItemFunc` 输入过滤 | src:162-167、526-534 |
| 多槽 | `new GameGridInventory(int width,int height)`；`new GameGridInventory(config,width)`（模组仓） | src:166/227/231 |
| 文本标签 | `TagElement`：`.SetText(string text, int maxWidth=10000, ColorPalette color=...)`、`.SetRichText` | src:191/528-534 |
| 机器外壳（可选） | `MachineHelper.CreateMachineryWindow(bool canMove, MachineryPanel[] panels)`、`MachineHelper.Panel(label, content, role=null, styleInventories=true)`、`PowerPanel`、`NotePanel`、`IconPanel` | `members.txt` MachineHelper 段 |
| 把窗口挂到物品 | `GameItem.SetContentWindow(PixelWindow)`（src 已用）；`MachineHelper.SetupBatterySlot` / `MachineryHelper.InitMachinery` 若需电力 | src:209-260 段落 |
| 按钮 | 原版机器按钮为 `MachineryButton` + `PixelWindow.AddHeaderButton(MachineryButton)`（`members.txt`）；**更简单**：用 `CustomUIManager` 的 `AddButton`（见 `docs/api-findings.md` §8.6）做浮层 UI，容器仍用 PixelWindow 窗口 | `docs/api-findings.md` §8.6 |

**推荐**：打印机 = 单槽 `GameSlotInventory`（输入：空白介质/纸）+ 若干 `GameSlotInventory`/`GameGridInventory`（耗材、输出）+ `TagElement` 标签 + `PixelWindow` 外壳（`PixelWindow(true,"Printer")`），
上料/出料逻辑用 `onCycleEndSlotItemFunc` 或按钮回调；与现有 13 台机器完全同构，无需新 API。
若打印机还需要"配方选择按钮"，优先复用 `CustomUIManager.Window` + `AddDropdown/AddButton`（api-findings §8.6 已核对 public），避免自造按钮预制体。

---

## 7. 风险 / 未验证项（如实）

1. **未做游戏内验证**：以上全部是静态 IL/元数据推断；成分仪实际显示、倒液交互、客户收购、价格变化都需要实机。
2. `ChemicalHelper.GetTotalVolume / InitChemItem / TransferLiquidToChem / HandleReceiveLiquidTemp` 本轮未单独展开 IL（只从 `strings.txt`/xref 拿到名字与调用关系）；
   建议下一轮补 dump（探针 selectors 已预留 `^ChemicalHelper$::*` 可加）。`GradeProduct` 依赖 `GetTotalVolume`，若其口径（是否含杂质液体）与假设不同，纯度会偏移。
3. `StoreClient.IsClientRefusingItem` IL 未展开：`acceptedChemicalProductGrade` 的确切阈值组合来自 `WillAcceptNarcotic`（已读）+ xref，未逐行核对调用参数。
4. `ItemConditionList..cctor` 里七档的 `modValue`（价值修正百分比）未逐值核对 → 纯度对报价的实际幅度需实机/下一轮 IL。
5. 映射表（§2.3）的酸碱浓度是设计值，非物理精确；若用户要求"贴近真实化学"，需按摩尔浓度重算并以 QA 断言守恒（总 part = ml×1000）。
6. Harmony 补丁目标随游戏更新会失效；本轮已尽量选 `public` 方法且用 `nameof`，但 `IsClientBuyingThisItem` 有 2 个重载，Patch 必须带 `new[]{typeof(GameItem),typeof(bool)}` 显式参数类型（否则 Harmony 会报 ambiguous）。
7. 扫描仪 Postfix 写入的是 `public` 字段（`testedTMP/labelTMP`），若未来版本把字段改 private 会失效（当前元数据确认 public）。
