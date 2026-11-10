# 用户反馈问题研究（P2 前置，只读）

> 依据：`Assembly-CSharp.dll` 元数据 + IL（探针新增第 17 组 selectors，`out/il/` 约 951 个方法）、官方 ModdingGuide、`src/` 现状只读。
> **未改 src、未实机验证**。IL 文件均指 `tools/api-probe/out/il/<类型>__<方法>.il.txt`。

## 1. 为什么 mod 液体瓶倒不进原版容器

### 1.1 原版倒液交互的真实注册方式（`WaterHelper.InitLiquidContainerItem`）

`InitLiquidContainerItem(item, capacityInMl, basePrice, isFilterContainer, isGraduated, isDropper, isTransparentContainer, sheetPage)`
（`WaterHelper__InitLiquidContainerItem.il.txt`，235 行）对一个"液体容器物品"做这些事：

1. **tags**：`LIQUID_CONTAINER_TAG`、`LIQUID_CONTAINER_BASE_PRICE_TAG`、`LIQUID_CONTAINER_CAPACITY`（= `capacityInMl * 1000`，**1 ml = 1000 part**）；
   可选 `LIQUID_CONTAINER_DROPPER_TAG` / `FILTER_CONTAINER_TAG` / `GRADUATED_CONTAINER_TAG`；
   **为 `Liquid.Liquids` 里每一种液体 enable 对应 `currentPart` tag 并置 0**（如 `water`/`acid`/`base`…）；
2. `ChemicalFeedbackHelper.InitChemicalIntentItem` + `ChemicalHelper.InitContainerTemp` + `InitContainerTempFunction` + `WaterFeatureHelper.InitWaterFeature`；
3. **GameItemFunc 钩子（关键）**：
   - `maySelectSlotItemFunc` / `canSelectSlotItemFunc`（选择限制，直接赋值）；
   - `mayThisTargetItemFunc` / `canThisTargetItemFunc`：用 `Delegate.Combine` **追加**（`b__10_7` / `b__10_8`）；
   - `onThisTargetItemFunc`：`Delegate.Combine` 追加 `b__9`；
4. `WaterHelper.UpdateContainerGoodType`、`UpdateWaterBottleSound`、可选 `getItemSpriteModifierForItemFunc`。

**交互模型（`b__9` IL 已读）**：`onThisTargetItemFunc(s, t)` 的 `s` = 当前**选中**的容器，`t` = **被点**的物品。
若 `t.IsTag(LIQUID_CONTAINER_TAG)` → `WaterHelper.TransferLiquid(source: s, target: t)`（把 **选中的瓶**倒入 **被点的容器**）；
否则 `t` 是 drain → 清空；`t` 是 faucet → 灌水。
→ **倒液 = 选中源瓶（源瓶必须有上面这组钩子和 tag）→ 点击目标容器（目标必须有 `LIQUID_CONTAINER_TAG`）**。

`WaterHelper.TransferLiquid` 再次硬校验：**source 和 target 都必须有 `LIQUID_CONTAINER_TAG`**，否则直接 return
（`WaterHelper__TransferLiquid.il.txt` 前 17 行）；容量取 target 的 `LIQUID_CONTAINER_CAPACITY`，按 `Liquid.currentPart` tag 比例搬运，末尾调
`OnLiquidContainerPourInto/OutOf`。

### 1.2 `SetGameItemType` 是"追加"还是"替换"？（IL）

`GameItem.SetGameItemType(string)`（`GameItem__SetGameItemType.il.txt`）：
`EnableTag(type)` + `itemTypes.Contains ? skip : itemTypes.Add(type)` → **追加**，不会清掉已有 tag。
`IsGameItemType` 只查 `itemTypes` 列表。
→ 在 `InitLiquidContainerItem` 之后调用 `SetGameItemType(CHEMICAL_SUPPLIES)` **不会删除** `LIQUID_CONTAINER_TAG`；
`SetSpriteAndShapeFromMod` 只改 sprite/shape，也不动钩子/tag。**所以"Init 之后再 SetGameItemType + SetSprite"本身不会破坏容器交互**（前提是 Init 确实调用过、且之后没有把 `onThisTargetItemFunc` 等覆盖成 null）。

### 1.3 仍倒不进去的可能原因（按概率排序，含验证方法）

1. **物品不是"owned"**：`GameItem.CanTarget`（`GameItem__CanTarget.il.txt` 前 9 行）第一步就是
   `if (!GeneralHelper.IsItemEffectivelyOwned(A)) return false;`（A=选中的源瓶）。机器/合成产出的物品若没有走
   `SetItemOwned/SetItemOwnerShip`，玩家点击目标会被直接判为"不能使用"（无提示）。
   **验证**：在物品工厂末尾 `ChemRuntime.Log(item.identifier + " owned=" + GeneralHelper.IsItemOwned(item))`；
   **修复**：产出物品处 `GeneralHelper.SetItemOwned(item, true)`（public；`ModHelper.SetItemOwnerShip(item,true)` 也封了）。
2. **源瓶没有真正成为液体容器**：`ChemCatalog` 工厂只对 `Kind=="liquid" && Phase==Liquid && ChemRecipes.TryGetPourParts(...)` 调 Init；
   若某液体缺 `PourParts` 映射（ValidateLogic 有断言，正常应齐全）→ 该瓶没有钩子/tag，`CanTarget` 时 `mayThisTargetItemFunc` 为 null，
   原版不会提供"倒入"反馈。**验证**：日志 `item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)` 与
   `item.GetTagReadonly(WaterHelper.LIQUID_CONTAINER_CAPACITY).GetInt()` 是否 >0。
3. **目标容器缺 `LIQUID_CONTAINER_TAG`**：例如把我们的瓶倒进"原版药瓶（非液体容器类）"或某些机器内容槽——原版只接受带 tag 的液体容器
   （水罐/水壶/水瓶/水箱/水龙头/排水口）；`WaterHelper.TransferLiquid` 会静默 return。**验证**：打印目标 tag。
4. **钩子被后续覆盖**：`Init` 用 `Delegate.Combine` 追加；若工厂/后续代码直接 `item.onThisTargetItemFunc = 别的委托` 会覆盖（本模组未见）。
5. **物品被 `CloneLinked`/`Transform` 后钩子丢失**：`GameItem.CloneLinked` 只复制 sprite/shape/state 等，**不复制 GameItemFunc 委托**
   （之前 IL 已确认它只复制 spritePath/atlasPath/spriteChanged）。若液体瓶被拆分/克隆，克隆件没有倒液钩子。**修复**：克隆后重新调用一次
   `WaterHelper.InitLiquidContainerItem`（幂等：tag 重复 enable 无副作用、钩子 Combine 会重复追加，建议先判 `IsTag(LIQUID_CONTAINER_TAG)`）。

**正确顺序 / 必须保留项**：
```
ItemDirectory.CreateEmptyItem()
  → 基础字段（name/desc/flavor/value/type/sprite）
  → WaterHelper.InitLiquidContainerItem(item, 500, price)      // 必须：tag+钩子+液体组分槽
  → WaterHelper.AddLiquid(item, "acid", 150000) 等             // 必须在 Init 之后（容量 tag 已存在）
  → 原版 chem 身份：EnableTag(CHEM_ITEM_TAG/CHEMICAL_PRODUCT_TAG) + 纯度 tag/Feature
  → 最后才恢复 name/desc/flavor/value/SetGameItemType/SetSpriteAndShapeFromMod（这些都不影响容器 tag/钩子）
  → GeneralHelper.SetItemOwned(item, true)
```
**绝不要**在 Init 之后 `DisableTag(LIQUID_CONTAINER_TAG)`、清空 `Liquid.currentPart`、或覆盖 `onThisTargetItemFunc`。

## 2. 原版可装液体的容器清单（IL 证据）

`WaterHelper..cctor`（`WaterHelper__.cctor.il.txt`）里 `WaterBottleIds` = **`small_bottled_water`, `bottled_water`, `mini_bottle`, `large_bottled_water`, `water_jug`**。

| item id | 来源 IL | 容量/参数 | 类型/tag | 能否被成分仪分析 |
|---|---|---|---|---|
| `water_pitcher` | `AmenitiesItemDirectory__WaterPitcher.il.txt` | `Init(3000, 25, filter:true, graduated:true, dropper:false, transparent:true, sheetPage:4)` | `HOUSEHOLD_GOOD` + `CONTAINER_TOOLTIP_TAG` + `LIQUID_CONTAINER_TAG`，带滤芯 UI 槽 | 可：water 模式（有 LIQUID_CONTAINER_TAG，非空时显示纯度/组分/总量） |
| `water_jug` | `FoodItemDirectory__WaterJug.il.txt` | `Init(6000, 15, 0,0,0, transparent:true, 4)` | LiquidContainer tag | 可（water 模式） |
| `small_bottled_water` / `bottled_water` / `large_bottled_water` / `mini_bottle` | WaterBottleIds（具体工厂未在本轮 dump） | 由各自工厂 `Init(...)` 决定（容量看 `LIQUID_CONTAINER_CAPACITY` tag） | LiquidContainer | 可（water 模式） |
| 水塔/水箱（`WaterTankHelper.Create` / `MachineRAS.CreateWaterTank`） | `WaterTankHelper__Create.il.txt` / `MachineRAS__CreateWaterTank.il.txt` | `WaterTankHelper.GetCapacityMl`；机器水槽 | LiquidContainer + 机器 | 可（water 模式） |
| 水龙头/排水口（`WaterHelper.IsFaucet/IsDrain`） | `WaterHelper` 常量/接口 | 水源，不是容器 | — | 倒液/灌水交互用 |
| 原版药瓶/化学瓶（`med_bottle_*`、`common_chemical` 等） | 未调用 `WaterHelper.InitLiquidContainerItem`（xref 里 Init 的调用方只有 WaterPitcher/WaterJug/UnusedDirectory.PouchHydration 等） | **不是** Water 液体容器 | 不能倒液；若要有液体身份，需自己 Init（会把该物品变成液体容器） |

**能被成分仪分析的条件**：`CHEM_ITEM_TAG` 或 `LIQUID_CONTAINER_TAG` 或 `CHEMICAL_PRODUCT_TAG` 之一（原版 scanner 的 target 判定
`MedsItemDirectory+<>c::<ChemScanner>b__25_2/3` 只查这三个 tag）；**有 tag 才可被点、才进分支**。

## 3. 成分仪只显示纯度、不显示成分/总量

**IL 结论：组分行/总量行不是 water 专属**。`ChemScannerUIManager.RefreshUI`（`RefreshUI.il.txt`）结构：

1. 先按 `item.IsTag("CHEM_ITEM_TAG")` 分流：真 → `RefreshChemMode()`（显示纯度 tag + 七档条件），假 → `RefreshWaterMode()`（显示水纯度/标签）；
2. **分流之后**统一计算：`total = Σ Liquid.Liquids[i].currentPart tag`；对每个 >0 的液体追加
   `contaminantTMP += "{displayName}: {amount} ({pct:F2}%)\n"`；`totalVolumeTMP = total>0 ? loc("mech_scanner_total_volume", FormatAmount(total)) : ""`。
   这两段与 chem/water 分支无关。
3. `RefreshWaterMode` 额外在 `WaterHelper.IsEmpty(item)` 时清空 tested/label/contaminant（chem 模式没有这个清空）。

→ 我们的液体显示"只有纯度"的原因**不是 chem 分支跳过组分**，而是 **该物品没有 `Liquid.currentPart` 液体组分 tag（total=0 → 总量行空、组分行空）**，
或者物品是气体/固体（本来无液体组分）。验证/修复顺序：

1. 工厂里确认 `Init` 之后 `AddLiquid` 真的执行（`WaterHelper.AddLiquid` 对无效 liquidId 会 `Debug.LogError("Invalid liquid ID: ...")` 后 return）；
2. 确认 `ChemRecipes.PourParts` 覆盖该液体（ValidateLogic 已断言 `液体药剂缺少原版组分映射`，若上报就是这里）；
3. **不要为了显示组分去掉 `CHEM_ITEM_TAG`**：去掉后进 water 模式，七档纯度（`lab_pure` 等）会被替换成水纯度档，反而丢信息；
   正解是"chem 身份 + 液体组分 tag 同时存在"（本模组 P1a 工厂已按此设计）；
4. 兜底补丁（若旧存档物品缺组分，无法再补）：
   ```csharp
   [HarmonyPatch(typeof(ChemScannerUIManager), "RefreshUI")]
   static class ScannerAppendCompositionPatch {
       static void Postfix(ChemScannerUIManager __instance) {
           var it = __instance.currentScannedItem; if (it == null) return;
           if (!ChemCatalog.IsOurReagent(it.identifier)) return;
           // 用自家 fc_ 组分写 contaminantTMP / totalVolumeTMP（字段 public）
           __instance.contaminantTMP.text = ChemCatalog.BuildCompositionLines(it);
           int total = ChemCatalog.TotalVolumePart(it);
           if (total > 0) __instance.totalVolumeTMP.text =
               ModHelper.GetLocalized("free_chem","scanner_total") + WaterScannerUIManager.FormatAmount(total);
       }
   }
   ```
   注意：`RefreshChemMode` 的 Postfix（P1a 已有旧存档桥接）**不要**在这里重复写 TMP。

## 4. 原版水 / 酒精 / 咖啡物品

- **纯水/水容器 id**：`WaterHelper.WaterBottleIds`（§2）：`small_bottled_water`、`bottled_water`、`mini_bottle`、`large_bottled_water`、`water_jug`；
  另有 `water_pitcher`（水罐）与机器水箱。**原版"100% 纯水"不是独立物品**，而是"只有一条 `water` 液体 part 的容器"
  （`WaterHelper.GetWaterPurity` = water/total×10000；只有 water 时 = 10000 → 显示 100%）；可用 `WaterHelper.FillWithPureWater(item)` 构造。
  → **原版水完全可以作为反应投料/倒液源**：它带 `LIQUID_CONTAINER_TAG` 与 `water` 组分 tag，`WaterHelper.TransferLiquid` 可倒；
  反应的 `inputs` 若用原版水物品 id，需要按 **item id** 匹配（注意原版水有多个瓶子 id，建议在 `ChemRecipes.Normalize`/`VanillaAnalog` 里把
  `small_bottled_water/bottled_water/large_bottled_water/water_jug/mini_bottle` 统一映射为 `liq_water`）。
- **酒精**：原版酒类容器由 `WaterHelper.PourNudka`（`NUDKA_VOLUME_ML=750`, `NUDKA_ALCOHOL_PERCENT=45`）/
  `WaterHelper.PourSpirit(container, volumeMl, alcoholPercent)` 灌入，液体组分为 `alcohol` + `water`；
  具体瓶子 item id 本轮未在字符串表里定位到（`strings.txt` 无 `nudka/vodka` 直串；HANDOFF §3.3 也标注原版酒精 id 未确认）。
  → **酒精灯燃料建议**：继续用现有 `ChemHeating` 的"原版酒精 id 候选数组 + 70% 纯度（175/250）"逻辑；若删除 mod 乙醇，把候选数组换成
  实机确认的原版酒瓶 id（推荐在游戏内用 `DirectoryMaster`/F8 或 `ModHelper.SpawnItem` 逐个试），**不要**写死未经验证的 id。
- **咖啡**：原版咖啡豆由 `HydroponicDirectory.CoffeeBean` 工厂创建，内部 `ChemicalCompositionHelper.SetCoffeeBeanComposition`
  （xref 唯一调用方）；咖啡因是原版液体 id `Liquid.LIQUID_CAFFEINE`（`"caffeine"`），不是独立物品。
  → 删除 mod 咖啡豆糊/咖啡因后：把 T4/热反应的 `sol_coffee_grounds` 输入改为**原版咖啡豆 item id**（需实机确认 id：可直接
  `ModHelper.SpawnItem("<候选>")` 或反射 `HydroponicDirectory.CoffeeBean` 取 `identifier`），咖啡因需求用 `WaterHelper.AddLiquid(item,"caffeine",n)`
  或保留一个 mod 咖啡因物品。**推荐**：保留 mod 咖啡豆糊/咖啡因作为独立物品（删除会牵动 PourParts/反应/本地化/图标/存档），
  只在反应里增加"原版咖啡豆 → 咖啡因"替代路径。

## 5. 电话系统

- 数据结构全 public：`PlayerStore.PhoneClientDict`（`Dictionary<long, StorePhoneClient>`，public 字段）；
  `StorePhoneClient.InitPhoneClientDict()`（static，构建默认 6 个号码：8376 Wild / 8815 RedImp / 56371 Mining / 4615 Pharma / 3319 Chem / 51189 RevMerchant）；
  `PhoneClientList.DialogFuncs`（public static `Dictionary<string, Func<StorePhoneClient, Dialogue>>`）；
  `StorePhoneClient` 字段 `displayName/locID/dialogFuncId/phoneState/phoneClientType/cooldownDuration/currentCooldown/alias/stockType/amountSpent`；
  `PhoneUIManager.Instance`（public）、`AutoCall(long)`、`HandleCall(long)`、`StartPhoneDialog(long)`、`OpenUI()`；
  `DialoguePhoneUIManager.Instance.StartDialogue(Dialogue)`（public）。
- **加可拨打商人**：没有公开"注册电话联系人"API（ModHelper 无 Phone 方法；指南也没有电话章节）→ **Harmony 兜底**：
  ```csharp
  [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.InitPhoneClientDict))]
  static class AddPhoneMerchantPatch {
      static void Postfix(ref Dictionary<long, StorePhoneClient> __result) {
          if (__result == null || __result.ContainsKey(ChemCatalog.PhoneNumber)) return;
          var c = new StorePhoneClient {
              displayName = "FreeChem 气体商人", locID = "phone_chem_merchant",
              dialogFuncId = "free_chem_gas_merchant", phoneState = StorePhoneClient.PhoneState.Available, // 枚举值需按实际枚举定义
              phoneClientType = (StorePhoneClient.PhoneClientType)0,   // 选与原版 Chem(3319) 相同的类型
              cooldownDuration = 0, alias = new List<string>(),
          };
          __result[ChemCatalog.PhoneNumber] = c;
          PhoneClientList.DialogFuncs["free_chem_gas_merchant"] = pc => ChemDialog.BuildGasMerchantDialog(pc);
      }
  }
  ```
  注意：`PhoneState`/`PhoneClientType` 是嵌套 enum（值需按 `members.txt` 里的枚举常量核对）；号码必须唯一（`StorePhoneClient.IsNumberExist`）；
  玩家拨号后由 `PhoneUIManager` 走 `GetCallDialog()`/`DialogFuncs`。**不要 patch `PaperBookRegistry`/加载器类**（见 §6）。
- **次日清晨送到前柜台**：`ModHelper.MoveItemToFrontCounter(item)` 是 public（IL = `EmporiumEntry.Instance.frontInvinvElement` + `TryAcceptAll`），
  配合 `ModHook.OnShutterOpenedEarly`（每天开店时）：
  ```csharp
  ModHook.OnShutterOpenedEarly += DeliverPendingOrders;
  void DeliverPendingOrders() {
      if (!ChemRuntime.TryTakePendingOrder(out string itemId)) return;
      var it = ModHelper.SpawnItem(itemId);
      if (it != null) ModHelper.MoveItemToFrontCounter(it);   // 满柜会静默失败，见 docs/api-findings.md §8.1
  }
  ```
  订单/价格：原版电话没有公开的商品价格接口；建议在对话 `onChoice` 动作里自己结算（给物品/扣钱用 `PlayerStore` 公开成员需谨慎），
  或复用现有王尔德网络升级/气体商人订单作为"价格"载体（`AddNetworkUpgrade` 支付已由原版处理）。

## 6. 书籍 / 纸张

- `ModHelper.SpawnPaperItem(modId, bookId)`（`ModHelper__SpawnPaperItem.il.txt`）→ `PaperBookRegistry.SpawnItem(fullId)`；
  生成物品带 `DOCUMENT`/`BOOK_TAG`/`BOOK_ID`（= fullId）`/CURRENT_BOOK_PAGE`（`BookHelper__InitBookItem.il.txt`）。
- Book JSON schema（官方指南 `ModdingGuide.zh-CN.md:760-870`）：顶层 `id/title/background/width/height/designWidth/designHeight/border/
  posX/posY/sounds/draggable/zoomable/chrome/templates/pages/item`；元素 `type=text|image|button|flow|line` + `id/x/y/w/h/visible/
  raycast`；文字 `text|loc|translations/style/fontSize/align/valign/color/wrap/autoHeight/preview`；图片 `sprite/fit/scale/color/
  silhouette/fill`；按钮 `action/text/sprite/hoverSprite`；flow `spacing/align`；`pages` 数组、每页 `elements/slots`。
  `PaperBookData.GetPage(int)`、`PaperUI.FlipTo(page)/CurrentPage/PageCount`；**没有自动分页**——分页由你在 `pages` 里手工排。
- **唯一索引书判断**：不要 patch `PaperBookRegistry`（官方指南 1346 行明确禁止对加载器/信任代码/`PaperBookRegistry` 打 Harmony 补丁），
  但可以**读** public API：`PaperBookRegistry.All` / `OfMod(modId)` / `Get(fullId)`（keyed by fullId，注册本身保证同 id 唯一）。
  道具层唯一性：遍历 `EmporiumEntry.Instance.GetAllItems()`（public）→ `item.IsTag("BOOK_TAG")` → `GetTagReadonly("BOOK_ID").GetString()`
  与目标 fullId 比较；或用 ModData 标志 + `ModHelper.GetPaperBookIds`。建议：书 id 固定（如 `free_chem.chem_codex`），
  生成前先扫 BOOK_TAG/BOOK_ID 是否已存在；排版每页 ~40 行 ID（`text` 元素 + `wrap:true/autoHeight:true`，fontSize 调小），
  85 IDs → 3 页（标题/范围/逐条），并在 `pages[].id` 里给每页稳定 id 供 `PaperUI.Get(id)` 更新。

## 7. 机器窗口气瓶槽

- 现有 API 足够：`PixelWindow` + `GameSlotInventory`/`GameGridInventory` + `GridPixelElement.Attach` + `TagElement` + `MachineHelper.Panel`
  （签名见 `docs/printer-research.md` §6 / `docs/api-findings.md` §8.6；本模组 `src/ChemMachines.cs` 已用）。
- 方案：机器窗口加一个 `GameSlotInventory gasSlot = new GameSlotInventory()`，`TagElement` 标 "Gas"，`grid.Attach(gasSlot, col, row)`；
  `gasSlot.mayInventoryAddItemFunc = (it, _) => ChemCatalog.IsGasCanister(it.identifier);`
  在 `onCycleEndSlotItemFunc`/`Finish` 里：
  ```csharp
  GameItem can = gasSlot.childItem;
  if (can == null) return;
  string gasId;   // 反查 ChemRecipes.CanisterSkus：can_oxygen_* → gas_oxygen
  if (!ChemRecipes.TryGetGasFromCanister(can.identifier, out gasId)) return;
  int purity = ChemCatalog.ReadPurity(can);          // fc_purity tag（0–100）
  // 用 gasId 参与反应；纯度可用于档位/加成；不把 can 当作液体投料
  ```
  "气瓶如何表示气体成分"：**can_* 物品本身没有液体组分**，用 **id 前缀 + `fc_purity` tag** 映射到 `gas_*`；
  反应输入匹配用 `gasId`（把 can 从 inputs 列表里替换为 gasId），而不是把 can 当 `liq_*`。
- **"瓶内无物理杂质"保证**：本模组污染物 tag `fc_contam_*` **只对 `Phase.Liquid && Kind=="liquid"` 的物品写入**
  （`src/ChemCatalog.cs:454-464`：`spec.Phase == Phase.Liquid && spec.Kind == "liquid"` 才 `InitialContaminants()`，否则全 0）；
  can_* 是 `Kind="canister"` 且 Phase.Gas → 永远没有 `fc_contam_*`。因此气瓶槽映射时**不要复制任何 contam 数据**，
  也不要对 can 调 `WaterHelper.*`/`WaterFeatureHelper.*`（它们只认 LIQUID_CONTAINER_TAG）。消耗 can 后建议产出 `gas_canister_empty`
  （守恒：空瓶回到输出/空瓶槽），或按产品设计直接销毁——需 lead 决策。
- 注意：现有 `canister_filling` 已经把 `gas_*` 当输入、产出 can_*；若把 can_* 当气体源，要避免与灌装台语义冲突
  （建议只在"消耗气体"的机器加气瓶槽，且 `AcceptsInput` 明确拒绝 `gas_drum_*` 已灌装桶，沿用现规则）。

## 8. `SetSpriteAndShapeFromMod` 的 shape 推导与 16×32/16×48

`GameItem.SetSpriteAndShapeFromMod` IL（`GameItem__SetSpriteAndShapeFromMod.il.txt`）推导规则：

- `gridSize = UISettings.current.gridSize`（public 字段 `float gridSize`；HANDOFF 记录为 16，**运行时按此值**）；
- `w = max(1, sprite.rect.width / gridSize)`，`h = max(1, sprite.rect.height / gridSize)`；
- 对每个格子 **只采样该格中心像素**：`x = rect.xMin + col*(int)gridSize + gridSize/2`，`y = rect.yMin + (h-1-row)*gridSize + gridSize/2`，
  `GetPixel(x,y).a > 0` → 该格占用；
- `SetShape(new GridShapeBuilder().SetData(mask, w))`；此外 `BuildShapeFromSprite`（原版图集路径）会
  `WarnIfSliceNotGridAligned`：图片宽高不是 gridSize 整数倍时日志警告。

**16×32 只占 1 格的最可能原因**：`w×h = 1×2` 但**上半格/下半格的中心像素透明**（典型：瓶身在下、瓶颈/瓶盖细，中心列在某一 16px 带内是透明背景），
该格不计入 shape → 只剩 1 格。其次：图片尺寸不是 16 的整数倍（被截断/错位），或 sprite rect 有透明边距。
**修复/美术要求**：
- 16×32 → 1×2 格：两格中心 `(8, 8)` 与 `(8, 24)`（以 sprite 左下为原点，Unity y-up；实际导出的 PNG 需上下对应）必须**不透明**；
- 16×48 → 1×3 格：三格中心 `(8,8)/(8,24)/(8,40)` 都要不透明；
- 图片宽高必须是 `gridSize` 的整数倍（16/32/48…），不要留半格透明边；透明只出现在"确实不占格"的整格区域；
- 不要依赖"整张图有不透明像素"——只看每格中心那一个像素。可用 `BuildShapeFromSprite` 相同的采样逻辑在 Python/测试里预检
  （读 PNG alpha，按 16px 格子中心采样，断言 mask 与期望一致）。本模组图标生成器可加这条自检。

## 9. 风险 / 未验证

1. 全部为静态结论；未实机验证倒入、扫描显示、电话、书籍、气瓶槽、图标占格。
2. 倒液失败原因的排序（owned / 缺 tag / 目标不支持 / 克隆丢钩子）需按 §1.3 的日志逐项验证；未读 `b__10_7/b__10_8` IL（探针未导出这两个 lambda，下一轮可加 selector）。
3. 原版纯水/酒精/咖啡的**具体 item id** 本轮未全部确认（WaterBottleIds 已确认；酒精/咖啡豆 id 需实机或补 dump）。
4. 电话没有公开"加联系人"API，方案依赖 Harmony patch `StorePhoneClient.InitPhoneClientDict`；枚举值（PhoneState/PhoneClientType）需按元数据常量核对；且 `PhoneClientDict` 存档字段可能被覆盖（加载时重建），需在 patch 里处理。
5. `PaperBookRegistry` 禁止 patch（官方指南），只能用 public 读 API；`SpawnPaperItem` 唯一性靠 id + BOOK_TAG/BOOK_ID 扫描。
6. 气瓶槽会改 `ChemMachines`（features 写作用域），本轮只给方案；需确认与灌装台/大桶现有语义不冲突。
7. 图标占格依赖运行时 `UISettings.gridSize` 真值（序列化字段，静态未知）；建议实机打印一次 `UISettings.current.gridSize`。
