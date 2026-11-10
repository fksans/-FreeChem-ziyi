# 容器 / 组分 / 电话 / 电力 / 描述 / 索引书 —— 深度研究

> 只读：元数据 + IL（探针新增第 18 组 selectors，`out/il/` 约 1030 个方法）+ 官方指南 + src 现状。**未改 src、未实机验证**。
> IL 文件 = `tools/api-probe/out/il/<类型>__<方法>.il.txt`。

## 0. 结论速览

| # | 问题 | 结论 |
|---|---|---|
| 1 | 液体瓶倒不进 | **不是 Init 顺序问题**；最可能是**旧存档 TagSystem 覆盖**：读档时 `set_state(saved.itemState)` 把 Init 写入的 `LIQUID_CONTAINER_TAG`/液体组分覆盖掉（函数委托仍在，但 tag 没了 → `TransferLiquid` 提前 return）。修复：`OnGameLoadedInit/Late` 遍历全部物品跑一次 `BridgeVanillaChemData`（P1a 已有函数）。 |
| 2 | 分析仪无组分 | 组分 = 每种 `Liquid.currentPart` 的 item tag；`ChemicalHelper.GetTotalVolume` 要求 `CHEM_ITEM_TAG` 后遍历 `Liquid.Liquids` 求和。旧存档 tag 被覆盖 → total=0 → 无行；非液体物品用 `ChemicalHelper.AddLiquidToChemFromList` 写组分（不需要 LIQUID_CONTAINER）。 |
| 3 | 电话 96686 无效 | `PhoneClientDict` 只在 `PlayerStore.StartNewGame()` 里由 `StorePhoneClient.InitPhoneClientDict()` 初始化（xref 唯一调用方）；**读档不重建**，补丁打在 InitPhoneClientDict 对老存档无效。正确做法：`OnGameLoadedInit`/`OnNewGameStarted` 里直接确保 `PlayerStore.Instance.PhoneClientDict[96686]` 存在。 |
| 4 | 机器电力 | `SetupBatterySlot` 只加电池槽 UI + `MACHINE_RECHARGABLE_TAG`；`MachineryHelper.InitMachinery(item, drawnPower, isTogglable)` 写 `machinery_drawn_power`/`MACHINERY_DRAWN_POWER_BASE`；不需要电的机器**不调用**这两个即可；最简窗口 = `CreateMachineryWindow(canMove, new[]{ Panel(...), Panel(...) })`。 |
| 5 | 描述纯度冲突 | 固定档位词在 Localization 的 `_name/_desc`（如「化学纯氧」「医药级纯度」），动态档位来自 `chem_profile`/`ItemCondition.display`/`CHEMICAL_PRODUCT_PURITY_TAG`；**名称/描述只写用途，档位一律动态显示**。 |
| 6 | 索引书排版 | 现有 `chem_codex.json` 628×804、双列 266 宽；单列建议 x=24、w≈560、fontSize 13–14、`wrap:true, autoHeight:true`，每页 ~30 行；85 个 ID → 3 页。 |

---

## 1. 液体容器：为什么倒不进，以及确实可行的方案

### 1.1 Init 到底注册了什么（IL 复核）

`WaterHelper.InitLiquidContainerItem`（235 行）：
- tags：`LIQUID_CONTAINER_TAG`、`LIQUID_CONTAINER_BASE_PRICE_TAG`、`LIQUID_CONTAINER_CAPACITY`（=ml×1000）、可选 DROPPER/FILTER/GRADUATED；**给 `Liquid.Liquids` 每种液体的 `currentPart` tag enable 并置 0**；
- GameItemFunc（全部 **Delegate.Combine 追加**）：`mayThisTargetItemFunc`(b__10_7)、`canThisTargetItemFunc`(b__10_8)、`onThisTargetItemFunc`(b__9)；`maySelectSlotItemFunc`/`canSelectSlotItemFunc`（直接赋值）；
- 其它：`InitChemicalIntentItem`、`InitContainerTemp`、`InitContainerTempFunction`、`InitWaterFeature`、`UpdateContainerGoodType`、`UpdateWaterBottleSound`。
- **没有**要求子类（`WaterBottle` 类不存在为必要条件；原版水瓶也是 `DirectoryUtils`/`Empty()` 生成后调 Init）；不要求特定 unitValue/sprite。

`UpdateContainerGoodType`（IL）：只切换 `WATER` / `HOUSEHOLD_GOOD` 两个 itemTypes（`RemoveGameItemType`+`SetGameItemType`），**不会删我们的 tag/type**。
`GameItem.SetGameItemType` = `EnableTag(type)` + `itemTypes.Add`（只追加）→ 现有"Init 后恢复 CHEMICAL_SUPPLIES + SetSprite"**没有破坏容器**。

### 1.2 读档路径的关键风险（最可能根因）

- `ItemDirectory.CreateEmptyItem`/`DirectoryMaster.Item`：`DirectoryMaster.Create<T>` 遍历已注册的 `Directory<T>` 的 `factoryDictionary` 调用工厂；本模组已在
  `ModEntry.RegisterItems(ModItemDirectory)` 里 `dir.Add(id, () => BuildItem(id))` 注册了全部 Specs + 机器，**所以读档时工厂会重建物品，Init 也会重新执行、钩子会重新挂上**。
- 但 `SaveManager.DecodeNodesInternal`（既有 IL）在工厂重建之后还会：`set_state(saved.itemState.Clone())` / `set_modifiedState(...)` / `set_shape(...)` / `SetSprite(...)`。
  **`set_state` 会用存档里的 TagSystem 整体替换 Init 刚写好的 tag**：如果该存档物品创建于"液体桥接"（P3a/P3b）之前，存档里没有
  `LIQUID_CONTAINER_TAG`/`LIQUID_CONTAINER_CAPACITY`/各液体组分 tag → 读档后这些 tag 全部丢失（函数委托还在），于是：
  - `WaterHelper.TransferLiquid` 第一行 `if (!source.IsTag(LIQUID_CONTAINER_TAG)) return;` → **倒不进**；
  - 成分仪 total=0、组分行空（无 liquid part tag）→ **只显示纯度/什么都不显示**；
  - 这与用户"我存档里的瓶子倒不进/分析无组分"完全吻合；新开局的物品不受影响。

**结论**：不要只加"owned"或顺序补丁；**必须在读档后对全部本模组物品重跑一次液体/化学桥接**：

```csharp
// 在 OnEnable 订阅（已有钩子，名称见 guide）
ModHook.OnGameLoadedNormal += () => RebBridgeAllItems();
// 或 OnGameLoadedLate
static void RebBridgeAllItems() {
    if (EmporiumEntry.Instance == null) return;
    foreach (var it in EmporiumEntry.Instance.GetAllItems()) {   // public；包含后柜/展示柜/背包/夜间
        if (it == null || !ChemCatalog.IsOurReagent(it.identifier)) continue;
        ChemCatalog.BridgeVanillaChemData(it);   // P1a 已有：缺 tag 时 Init+AddLiquid+chem 身份/纯度
        ChemCatalog.BridgeLiquidData(it);        // P3b 已有：液体容器自检/补液（若函数名不同，按现有实现调用）
    }
    // 容器的 GameItemFunc 若因旧存档/克隆丢失（见 §1.3），在此重新 Init 一次
}
```

**校验**：读档后日志打印 `it.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)`、`GetTagReadonly(LIQUID_CONTAINER_CAPACITY).GetInt()`、`GetTotalVolume(it)`；
若为 0/缺失 → 桥接没覆盖（例如 `GetAllItems` 不含某容器、或该物品不在 Specs 里）。

### 1.3 函数委托是否会在克隆/读档时丢？

- `GameItem.CloneLinked`（IL 285 行）：**复制全部 GameItemFunc 委托**（含 `mayThisTargetItemFunc`/`canThisTargetItemFunc`/`onThisTargetItemFunc`、激活/选择/目标槽等，从 IL_0106 到 IL_02E6 逐项 set），并复制 TagSystem/GridShape/sprite 路径；
  **但**：① 若原物品有 `contentWindow`/`examineWindow`（机器/容器）会直接 `throw NotImplementedException("Deep item cloning is not yet implemented")`；
  ② 克隆基对象来自 `DirectoryMaster.Item(identifier, true)`（未注册 id → dummy），所以 mod 物品必须先注册（已注册）。
  → 普通液体瓶克隆**不会**丢钩子；机器类物品不能 CloneLinked。
- 读档不走 CloneLinked，而走 `DirectoryMaster.Item(identifier, true)` 工厂（见 §1.2），钩子由 Init 重挂；问题只在 **TagSystem 覆盖**。
- 若仍有"钩子丢失"的极端情况（例如物品由 `ItemDirectory.CreateEmptyItem()` 手工构造、未走工厂），兜底：
  ```csharp
  [HarmonyPatch(typeof(GameItem), nameof(GameItem.CloneLinked))]
  static class RelinkContainerHooksPatch {
      static void Postfix(GameItem __result) {
          if (__result == null || !ChemCatalog.IsOurReagent(__result.identifier)) return;
          if (__result.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)
              && __result.onThisTargetItemFunc == null)            // 只在确实丢了时重挂
              ChemCatalog.BridgeLiquidData(__result);               // 内部 Init+AddLiquid 会 Combine 钩子
      }
  }
  ```
  （`onThisTargetItemFunc` 是 public 属性可读；但注意重复 Init 会重复 Combine，必须"缺了才补"。）

### 1.4 原版水瓶创建方式（对照）

`AmenitiesItemDirectory.WaterPitcher`：`DirectoryUtils.CreateSlotInventoryWindow(true)` → `Empty()` → `SetContentWindow` → `SetSpriteAndShape("Items/water_container4","pitcher")`
→ name/desc → `set_identifier("water_pitcher")` → `SetGameItemType("HOUSEHOLD_GOOD")` → 滤芯槽 UI → `WaterHelper.InitLiquidContainerItem(item, 3000, 25, true,true,false,true,4)`
→ `AudioHelper.InitWaterFilter` → `EnableTag("CONTAINER_TOOLTIP_TAG")`。`FoodItemDirectory.WaterJug`：Empty→SetName→Sprite→SetValue→identifier→`Init(item,6000,15,0,0,0,1,4)`→音效。
→ **mod 物品完全可以照做**（我们已照做，缺的是读档后重桥接）。

---

## 2. 组分 / 分析仪 / 属性

### 2.1 组分存储与读取（IL）

- `ChemicalHelper.GetTotalVolume(chemicalItem)`：**先要求 `CHEM_ITEM_TAG`**，否则返回 0；然后 `Σ Liquid.Liquids[i].currentPart tag.GetInt()`。
- `ChemicalHelper.AddLiquidToChemFromList(chemicalItem, List<KeyValuePair<string,int>>)`：对列表逐项 `ModifyTag(Liquid.GetCurrentPartFromId(id), +=amount)` —— **这是给固体/气体等非液体物品添加"组分"的正确方式**（不需要 LIQUID_CONTAINER_TAG/Init；只写 tag）。
- `ChemicalHelper.TransferLiquidToChem(container, chemicalItem, volumeInPart)`：`WaterHelper.RemoveAndReturn(container, volumeInPart)` → `AddLiquidToChemFromList(chemicalItem, parts)`；用于"从液体容器取定量组分写入另一个化学物品"（计量/分装/分析）。
- 分析仪 `ChemScannerUIManager.RefreshUI`：组分/总量行在分支之后统一算（见 `docs/feedback-research.md` §3），数据源就是上面的 liquid part tag；`RefreshChemMode` 的纯度行来自 `CHEMICAL_PRODUCT_PURITY_TAG` + `CATEGORY_CHEMICAL_PURITY` feature。

→ **给固态/气态化学品补组分的推荐骨架**（让分析仪显示成分与总量）：
```csharp
item.EnableTag(ChemicalHelper.CHEM_ITEM_TAG, false);
ChemicalHelper.AddLiquidToChemFromList(item, new List<KeyValuePair<string,int>> {
    new KeyValuePair<string,int>("acid", 120000),   // 120 ml（1 ml = 1000 part）
    new KeyValuePair<string,int>("water", 80000),
});
// 注意：GetTotalVolume 只在该物品有 CHEM_ITEM_TAG 时生效；不要给气体加 LIQUID_CONTAINER_TAG（那会变成可倒液体）
```

### 2.2 能否运行时注册新 `Liquid` 组分

`Liquid` 是普通 public 类：字段 `identifier/displayName/currentPart/lethality/euphoria/addictiveness/color/...` 全 public；静态 `List<Liquid> Liquids`、`Dictionary<string,Liquid> LiquidDict` public；`public Liquid()` 构造；`Liquid.GetLiquidFromId`/`GetCurrentPartFromId` 都读 `LiquidDict`。
**技术上可行**：
```csharp
var l = new Liquid { identifier = "fc_hcl", displayName = "盐酸（mod）", currentPart = "fc_hcl" /* tag 名 */, lethality = 0.0, euphoria = 0.0, addictiveness = 0 };
Liquid.Liquids.Add(l); Liquid.LiquidDict["fc_hcl"] = l;
```
风险/代价（不建议第一版）：
1. `currentPart` 必须是合法 tag 名；`GetCurrentPartFromId` 读 `LiquidDict[id].currentPart`（IL 需确认），不一致会写错 tag；
2. 所有遍历 `Liquid.Liquids` 的原版逻辑都会看到它：分析仪组分行（想要）、**水纯度 `water part/total` 会被稀释**（不想要）、`GetLethalDose` 求和（lethality=0 无影响）等；
3. 不持久化：Liquid 注册只在本次运行；存档只存 tag，读档后要重新注册，否则组分行显示空白/异常；
4. 可能影响原版存档兼容（原版不认识该 tag，但 tag 只是数值，风险低）。
→ **推荐**：优先用原版组分组合（`acid/base/water/alcohol/chlorine/…`）表达，化学品属性用本模组 `fc_tox/fc_euph/fc_controlled` + 现有 patches；
只有"必须显示专属名称"时才注册自定义 Liquid，并接受水纯度被稀释的副作用（或把自定义组分不计入 water 体系，另用 `CHEM_ITEM_TAG` 物品避免 LIQUID_CONTAINER_TAG）。

### 2.3 组分 → 毒性/欣快/成瘾/致命（原版公式，IL）

- `GetLethalDose(item)` = `Σ(part × Liquid.lethality) / 1000`（`ChemicalFeedbackHelper__GetLethalDose.il.txt`）；
- `IsLethal`/`IsToxic` 阈值常量 `LETHALITY_THRESHOLD=1000`、`TOXICITY_THRESHOLD=750`（members.txt 常量）；
- `GetEuphoriaLevel(item)` 按 `Euphoria` 总量与 `WEAK/MILD/STRONG/EXTREME_EUPHORIA_THRESHOLD`（750/1000/1600/2200）分级；
- `IsNarcotic` 由 `CHEMICAL_INTENT_TAG`（`ChemicalFeedbackHelper.InitChemicalIntentItem` 写）或组分判定；
- `GetItemFeedbackLevel` 组合：intent + purity grade diff + lethal/toxic/narcotic/euphoria 等级（IL 164 行）。
→ **我们的实现路径**：要么在物品上写原版组分（lethality/euphoria 来自 `Liquid` 字段，自动生效），要么继续用现有 patches
（`ChemPatches` 已 Postfix `IsLethal/IsToxic/GetEuphoriaLevel` 用 `fc_tox/fc_euph` 覆盖）——两条路可共存；**不要让两者给出矛盾结果**
（若物品既写原版组分又写 fc 值，则以 patches 覆盖值为准，建议二选一并在文档注明）。

---

## 3. 电话：为什么 96686 无效

- `StorePhoneClient.IsNumberExist(number)`（IL）= `PlayerStore.Instance.PhoneClientDict.ContainsKey(number)`；
- `PhoneClientDict` 的初始化：**`StorePhoneClient.InitPhoneClientDict()` 的唯一调用方是 `PlayerStore.StartNewGame`**（xref）；
  `PlayerStore.PhoneClientDict` 是 public 字段（`Dictionary<long, StorePhoneClient>`），读档时若存档里没有该 number（老存档），随后也不会再调用 InitPhoneClientDict → `IsNumberExist` 返回 false；
- `PhoneUIManager.HandleCall`：number 不存在或 `phoneState == None(0)` → `NoNumber()`（无应答；96686 表现就是"无效"）；存在但 `phoneState == NotResponding(1)` → 无应答；
  `IsPhoneClientAtStore(number)` 也会拒绝（客户正在店里时不能打电话）。
- `PressNumber` 允许最多 9 位（超过 999999999 会截掉首位）→ 96686（5 位）**格式没问题**。
- `PhoneState` 枚举：`None=0, NotResponding=1, Responding=2, Introduction=3, Regular=4, Cooldown=5`；
  `PhoneClientType`：`None=0, Supplier=1`。可拨打的供应商应 `phoneState = Responding(2)`（不是 None），`phoneClientType = Supplier(1)`。
- `PhoneClientList.DialogFuncs`：`public static Dictionary<string, Func<StorePhoneClient, Dialogue>>`；
  `StorePhoneClient.GetCallDialog()` 用它按 `dialogFuncId` 取对话。

**正确注册骨架**（不要只 patch InitPhoneClientDict）：
```csharp
const long MerchantNumber = 96686;

void EnsurePhoneMerchant() {
    var ps = PlayerStore.Instance; if (ps == null) return;
    // 1) 号码（每个新游戏/每次读档都要保证存在）
    if (ps.PhoneClientDict == null) ps.PhoneClientDict = new Dictionary<long, StorePhoneClient>();
    if (!ps.PhoneClientDict.ContainsKey(MerchantNumber)) {
        var c = new StorePhoneClient {
            displayName = ModHelper.GetLocalized(ChemCatalog.ModId, "phone_merchant_name"),
            locID = "phone_merchant_name",
            dialogFuncId = "free_chem_gas_merchant",
            phoneState = StorePhoneClient.PhoneState.Responding,     // 2 = 可应答
            phoneClientType = StorePhoneClient.PhoneClientType.Supplier, // 1
            cooldownDuration = 0, currentCooldown = 0, dialedBefore = false,
            alias = new List<string>(),
        };
        ps.PhoneClientDict[MerchantNumber] = c;
    }
    // 2) 对话（每次运行都注册，DialogFuncs 是静态内存表，不持久化）
    PhoneClientList.DialogFuncs["free_chem_gas_merchant"] = pc => ChemDialog.BuildMerchantDialog(pc);
}

// OnEnable: ModHook.OnGameLoadedNormal += EnsurePhoneMerchant; ModHook.OnNewGameStarted += EnsurePhoneMerchant;
//           （如果希望老存档也能用，必须在 OnGameLoaded* 里调用，而不是只在 StartNewGame）
```
**"次日清晨送前柜台"**：`ModHook.OnShutterOpenedEarly`（每天开店）里 `ModHelper.SpawnItem(id)` + `ModHelper.MoveItemToFrontCounter(item)`；订单状态存 `ModHelper.SetModData`。
另：`PhoneClientList.DialogFuncs` 是静态表，`Dialogue` 用 `SetText/NextDialogue/SetStartAction` 构造（public）。

---

## 4. 机器电力 / 窗口

- `MachineHelper.SetupBatterySlot(batterySlot, machine)`（IL）只做三件事：`machine.EnableTag("MACHINE_RECHARGABLE_TAG")`、电池槽 `mayInventoryAddItemFunc` 只收电池、`onSlotAdd/Remove` 里 Combine 更新机器电力面板；**它不决定耗电**。
- `MachineryHelper.InitMachinery(item, drawnPower=0, isTogglable=false)`（IL）：`SetGameItemType("MACHINE")`、enable `machinery` tag、`SetType("machinery")`、写 `machinery_drawn_power`/`MACHINERY_DRAWN_POWER_BASE`/`is_machinery_powered`/`machinery_received_power`、`IMPORTANT_TAG`、音效、（可选）toggle 钩子。
- `MachineryHelper.GetMachinePowerUsage(item)` = 读 `machinery_drawn_power` tag（IL 9 行）。
- **不需要电**的机器：不调用 `SetupBatterySlot` + `InitMachinery(item, 0)` 即可（甚至完全不用 MachineryHelper，只 `MachineHelper.STANDARD_MACHINE_TAG` + `MachineProgressHelper` 走周期）；需要电：`SetupBatterySlot` + `InitMachinery(machine, power)` + `MachineryHelper.UpdatePowerUsageProcessTypeMachine(machine)`（现 src 已用）。
- 最简 MachineryWindow（滗析=无电池、1 输入+1 输出）：
  ```csharp
  var input = new GameSlotInventory(); var output = new GameSlotInventory();
  PixelWindow win = MachineHelper.CreateMachineryWindow(true, new[] {   // 重载2 可传 displayOrder
      MachineHelper.Panel("Input",  input),
      MachineHelper.Panel("Output", output),
  });
  machine.SetContentWindow(win);
  machine.EnableTag(MachineHelper.STANDARD_MACHINE_TAG);
  MachineProgressHelper.InitProgressTypeMachine(machine, speed);   // 周期结算；无需电池
  ```
  电解（需电）：再加 `var battery = new GameSlotInventory(); MachineHelper.SetupBatterySlot(battery, machine); MachineryHelper.InitMachinery(machine, power);` 并把 `battery` 用 `MachineHelper.PowerPanel(battery)` 放进 panels。
  读槽内容统一用 `MachineHelper.GetBodyStorage(machine, index)` 或直接持有 `GameSlotInventory` 引用（src ChemMachines 已持引用）。
  面板顺序：`CreateMachineryWindow(bool canMove, int[] displayOrder, MachineryPanel[] panels)` 重载（`members.txt` 有）→ 用它固定顺序。

---

## 5. 描述与纯度冲突

- **动态档位来源**：扫描仪 `testedTMP` 用 `FormatPurityPercentageFromDouble(purity)` + `FindItemFeatureByCategory("CATEGORY_CHEMICAL_PURITY").realCondition.display`；
  工具提示用 `ItemFeature.GetPublicDisplay/GetActualDisplay`（`chem_profile` 键 `纯度 {0}% · {1}`）。
- **冲突来源**：Localization 的 `_name/_desc` 里写死档位词，例如 `can_*_ultrapure_name=化学纯氧`、`sol_caffeine_desc=…医药级纯度`，
  而实际纯度 tag 由打印机/提纯动态变化 → 97% 时仍显示"化学纯"。`grade_*` 键（`grade_pharma/grade_near_pharma/grade_street_standard`）本来就该只由动态逻辑使用。
- **改法**：
  1. `_name` 只写物质名（「氯气」「咖啡因」），**不带任何纯度/等级字样**；`_desc` 只写用途/来源，不写"XX 级纯度"；
  2. 档位一律走 `chem_profile`/`ItemCondition.display`/扫描仪动态行；如需文案，用 `{0}% · {1}` 占位由代码填；
  3. 移除 SKU 后（can_*_medical/ultrapure），这些固定档位名也要一并删/改（保留键会造成旧物品显示误导）。
  4. `chem_profile` 已有 `纯度 {0}% · {1} · 欣快等级 {2}`，`chem_profile_reagent` 已有 `纯度 {0}% · {1}` — 统一用它们即可。

---

## 6. 索引书（单列）排版建议

现 `FreeChem/Books/chem_codex.json`：`width:628 height:804`、文本宽 266（双列）、`chrome` 里有 close/flip/page_indicator。
单列索引（85 个数字 ID）建议：

```json
{ "type": "text", "id": "index_body", "x": 28, "y": 60, "w": 560, "h": 680,
  "style": "body", "fontSize": 14, "align": "left", "valign": "top",
  "wrap": true, "autoHeight": true, "translations": { "zh": "...", "en": "..." } }
```
- 每行格式 `#12 盐酸 HCl(aq) · 液体` ≈ 22–28 个全角字符；`fontSize 14` 在 560 宽下约 25–30 字/行（1 行 1 个 ID）；
- 行高 ≈ fontSize×1.3 ≈ 18；680 高 → **约 36 行/页**；85 行 → **3 页**（建议第 1 页 1–30、第 2 页 31–60、第 3 页 61–85，每页标题单独 `text`）；
- 若坚持双列：宽 266、fontSize 11–12、每列 ~24 行/页 → 每页约 48 行，2 页即可，但中文可读性差；
- 文本内容用 `translations`（zh/en 同页切换），不要塞进 CSV 键（85 行 × 2 语言会爆本地化）；
- `autoHeight:true` 时注意不要超过页面高度，否则溢出（`Page` 检查会报"溢出的文字"）；给每页底部留 ~40 设计单位。

---

## 7. 风险 / 未验证

1. 未实机验证。§1.2 的"旧存档 TagSystem 覆盖"是 IL 推断（`set_state(saved.itemState)` 顺序明确），建议先加日志确认再决定是否保留重桥接补丁。
2. `BridgeVanillaChemData` 是否已覆盖所有旧存档 item 未知；若 `GetAllItems()` 不含某容器（如机器内容窗口内的瓶子），需要在 `OnGameLoaded` 额外遍历 `GameItem.FindAllChildItems()` 或内容窗口。
3. 自定义 Liquid 注册虽可行，但会改变水纯度/百分比显示；不建议第一版使用。
4. 电话注入依赖 `PlayerStore.start` 时序：`OnGameLoaded*` 钩子触发时 `PlayerStore.Instance` 应已存在；需实机确认 `PhoneClientDict` 不会被读档覆盖（若被覆盖，可再在 `OnShutterOpenedEarly` ensure）。
5. 机器电力面板的排序/`displayOrder` 需按 `MachineHelper.CreateMachineryWindow` 重载 2 的参数顺序实际测试。
6. Localization 档位词清理会牵动 validate 的键存在性断言与旧存档物品名显示，需 docs-i18n/qa 协同。
