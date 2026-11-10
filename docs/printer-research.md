# 标签打印机 / 100% 纯度 / 移除纯度 SKU —— P1b 前置研究

> 只读研究：元数据 + IL（探针 `tools/api-probe/`，本轮新增第 16 组 selectors；`out/il/` 现约 877 个方法）。
> **未改 src**（`src/ChemPatches.cs` 为 P1a 既有实现，本文只给建议）。**未做游戏内验证**。
> IL 文件名均为 `tools/api-probe/out/il/<类型>__<方法>.il.txt` 的简写。

## 0. 结论速览

| # | 问题 | 结论 |
|---|---|---|
| 1 | 物品-对-物品 target 钩子 | **可行，全部 public 属性**（`GameItemFunc` 上）：`mayThisTargetItemFunc`/`canThisTargetItemFunc` = `Func<GameItem,GameItem,bool>`，`onThisTargetItemFunc` = `Action<GameItem,GameItem>`；原版 `chem_scanner` 与 `labeler` 都用同一组"this"钩子（选中工具 → 点目标物品） |
| 2 | 模式切换 | **激活点击切换可行**：`onActivateSlotItemFunc`/`mayActivateSlotItemFunc` 是 public 钩子（本模组温度计已用该模式）；tooltip 用 `GameItemFunc.tooltipRichTextFunc` 或现有 `ModHook.OnCreateTooltipLate`。7 档 × 2 标签建议改成"2 模式 + 7 档"两层，或直接用原版 labeler 式 UI（`LabelerUIManager.Instance.OpenUI(target)` 是 public） |
| 3 | 100% 纯度 | **自洽**：直接写 `CHEMICAL_PRODUCT_PURITY_TAG=100` + `SetPurityFeature(CreateLabPure())` 在扫描/收购/价格路径上无冲突（见 §3 证据）；建议用"校准打印机 + 消耗催化剂 + 输入≥99.5"作为解锁条件 |
| 4 | 移除 `can_*_medical/ultrapure` | **不建议物理删除注册**（旧存档会变 dummy、QA 的 85 数字 ID 锚点断言会碎）。建议：保留注册/图标/本地化作 legacy、停止产出与掉落、单 id + 纯度 tag 产出、旧物品原地迁移（§4） |

---

## 1. 物品-对-物品 target 钩子（打印机 → 药剂瓶）

### 1.1 真实签名与可见性（`members.txt` / `GameItemFunc` 段）

全部是 **public 属性**（backing field private，setter public）：

```csharp
// 本物品作为"主动方"（选中它去点别的物品）
public Func<GameItem, GameItem, bool> mayItemTargetItemFunc   { get; set; }
public Func<GameItem, GameItem, bool> canItemTargetItemFunc   { get; set; }
public Action<GameItem, GameItem>     onItemTargetItemFunc    { get; set; }

// 本物品作为"被点方"（选中别的物品来点它）—— chem_scanner / labeler 用的就是这组
public Func<GameItem, GameItem, bool> mayThisTargetItemFunc   { get; set; }
public Func<GameItem, GameItem, bool> canThisTargetItemFunc   { get; set; }
public Action<GameItem, GameItem>     onThisTargetItemFunc    { get; set; }

// 还有 *SlotItemTarget* 变体（带 GameInventory/SlotMarker，public）与 onItemTargetThisFunc / onItemTargetSlotItemFunc
```

派发点：`GameItem.Target(GameItem targetItem)`（`il/GameItem__Target.il.txt`）在调用时依次触发
`A.onItemTargetItemFunc(A,target)`、`target.onItemTargetItemFunc(A,target)`、`A.onThisTargetItemFunc(A,target)`、`target.onItemTargetThisFunc(A,target)`；
`GameItem.CanTarget(targetItem)`（`il/GameItem__CanTarget.il.txt`）综合 `canItemTargetItemFunc`/`canThisTargetItemFunc`/槽位变体决定能否点。
**选中方 = A**（第一个参数 `s`），**被点物品 = B**（第二个参数 `t`）。

### 1.2 参考实现（chem_scanner / labeler）

`MedsItemDirectory.ChemScanner()`（`il/MedsItemDirectory__ChemScanner.il.txt`）注册：
`mayThisTargetItemFunc = b__25_2`、`canThisTargetItemFunc = b__25_3`、`onThisTargetItemFunc = b__25_4`；并设置 `maySelectSlotItemFunc/canSelectSlotItemFunc` 限制选中场合。

- `b__25_2/3`（`il/MedsItemDirectory___c___ChemScanner_b__25_2/3.il.txt`）：`bool (GameItem s, GameItem t)` →
  检查 **t**：`t.IsTag("LIQUID_CONTAINER_TAG") || t.IsTag("CHEM_ITEM_TAG") || t.IsTag("CHEMICAL_PRODUCT_TAG")`；
- `b__25_4`：`void (GameItem s, GameItem t)` → `ChemScannerUIManager.Instance.Open(s, t)`（s=扫描仪，t=被点物品）+ 音效。

`ToolDirectory.CreateLabeler()`（`il/ToolDirectory__CreateLabeler.il.txt`）同款：`InitLabelerItem` 只 `EnableTag("LABELER_TAG")`，
然后**用 `Delegate.Combine` 在既有钩子上追加** `mayThisTargetItemFunc/canThisTargetItemFunc/onThisTargetItemFunc`
（`onThisTargetItemFunc` → `LabelerUIManager.Instance.OpenUI(target)`，见 `il/LabelerHelper__OnTargetItem.il.txt`）。
→ **注册方式：直接赋值或 `Delegate.Combine` 追加；不要覆盖原值（若复用原版物品）**。

`GameItem.CanTarget` 的前置（`il/GameItem__CanTarget.il.txt` 前 25 行）：
`GeneralHelper.IsItemEffectivelyOwned(A)` 必须为真；若 B 非玩家所有，则 A 必须有 `CAN_USE_ON_NON_OWNED_TAG`（原版 chem_scanner 创建时 `EnableTag("CAN_USE_ON_NON_OWNED_TAG")` 就有此意图）。

### 1.3 打印机可编译骨架（建议新文件 `src/ChemPrinter.cs`，ModEntry 里创建物品时调用）

```csharp
// 可见性前提：GameItemFunc 上的 target 钩子、TAG 常量、GameItem/GameInventory 都是 public；
// 打印机物品由 ItemDirectory.CreateEmptyItem() 创建（与 chem_scanner 相同工厂路径）。
static GameItem CreatePrinter()
{
    GameItem printer = ItemDirectory.CreateEmptyItem()
        .SetSpriteAndShapeFromMod(ChemCatalog.ModId, "printer")
        .SetName(ModHelper.GetLocalized(ChemCatalog.ModId, "printer_name"));
    printer.shortDescription = ModHelper.GetLocalized(ChemCatalog.ModId, "printer_desc");
    printer.SetGameItemType(TypeHelper.TOOL);
    printer.SetValue(120);
    printer.EnableTag("IMPORTANT_TAG", false);
    printer.EnableTag("CAN_USE_ON_NON_OWNED_TAG", false);   // 允许对顾客/非玩家所有物品使用（同 chem_scanner）

    // 1) 能否被选中去"点"物品（与 chem_scanner 同款限制）
    printer.maySelectSlotItemFunc = (it, inv, slot) => GeneralHelper.IsItemOwned(it);
    printer.canSelectSlotItemFunc = (it, inv, slot) => GeneralHelper.IsItemOwned(it);

    // 2) target 钩子：s = 打印机(选中方)，t = 被点物品
    Func<GameItem, GameItem, bool> may = (s, t) => IsPrintableTarget(t);
    Func<GameItem, GameItem, bool> can = (s, t) => IsPrintableTarget(t);
    Action<GameItem, GameItem> on = (s, t) => { if (IsPrintableTarget(t)) ChemPrinter.OpenOrStamp(s, t); };
    printer.mayThisTargetItemFunc = may;   // 从 null 开始 → 直接赋值；若复用原版物品请用 Delegate.Combine
    printer.canThisTargetItemFunc = can;
    printer.onThisTargetItemFunc = on;
    return printer;
}

static bool IsPrintableTarget(GameItem t)
{
    if (t == null) return false;
    // 只接受"本模组真药剂"：Kind 为 reagent 且带我们的 ReagentTag（与 ChemPatches 同判据）
    return ChemCatalog.IsOurReagent(t.identifier) && t.IsTag(ChemRecipes.ReagentTag);
}
```

`OpenOrStamp` 的两种实现：
- **A. 直接盖章（最简）**：按打印机当前模式写 tag/feature（无需窗口）：
  ```csharp
  static void OpenOrStamp(GameItem printer, GameItem target)
  {
      ChemPrinter.Stamp(target, ChemRuntime.PrinterMode, ChemRuntime.PrinterGrade);
      ChemRuntime.Log("[printer] stamped " + target.identifier + " mode=" + ChemRuntime.PrinterMode
                      + " grade=" + ChemRuntime.PrinterGrade);
  }
  ```
  `Stamp` 内部（可见性全 public）：
  ```csharp
  // 化学品身份（已有）→ 覆盖纯度 tag + 七档 ItemCondition（§3）
  item.EnableTag(ChemicalHelper.CHEM_ITEM_TAG, false);
  item.EnableTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG, false);
  item.ModifyTag(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG, s => s.Enable().SetDouble(purity), false);
  ChemicalProductHelper.SetPurityFeature(item, ChemCatalog.PurityConditionFor(purity));
  // 医疗/化学品标签：SetGameItemType = EnableTag(type) + itemTypes.Add（可叠加多次！见 §1.4）
  item.SetGameItemType(TypeHelper.CHEMICAL_SUPPLIES);
  if (medicalLabel) item.SetGameItemType(TypeHelper.MEDICAL); else item.RemoveGameItemType(TypeHelper.MEDICAL);
  item.EnableTag("fc_medical", false);   // 本模组自己的镜像 tag（ChemCatalog 已在用）
  ```
- **B. 打开打印机窗口**（单槽 + 模式/档位按钮）再盖章：见 §2.3。

### 1.4 多类型叠加证据

`GameItem.SetGameItemType(string)`（`il/GameItem__SetGameItemType.il.txt`）：`EnableTag(type)` + `itemTypes` 去重后 Add；
`IsGameItemType` 只查 `itemTypes` 列表。→ **同一物品可同时是 `CHEMICAL_SUPPLIES` 和 `MEDICAL`**（tag 与列表双写），
这对"一个钢瓶同时被化学品/医疗客户接受"很关键（§4）。`RemoveGameItemType` 与 `RemoveAllGameItemType` 也是 public。

---

## 2. 模式切换

### 2.1 激活点击（推荐）

`onActivateSlotItemFunc` 是 **public** `Action<GameItem, GameInventory, SlotMarker>`，`mayActivateSlotItemFunc`/`canActivateSlotItemFunc` 是
`Func<GameItem, GameInventory, SlotMarker, bool>`（`GameItemFunc` 段，全部 public）。本模组 `src/ModEntry.cs` 的温度计已经用这个模式循环 `ChemRuntime.SetTemp`。
→ 打印机同样可以：**点击（激活）打印机 = 循环模式**，例如
`(Mode, Grade) = (化学标签, street_standard) → … → (医疗标签, lab_pure) → 回到起点`（2 模式 × 7 档 = 14 步，建议分两层：单击切模式、Shift 或 `onToggleSlotItemFunc` 切档位；或只暴露 4 档常用 + 其余由窗口）。

### 2.2 tooltip 显示当前模式（两种都可行）

- **原版钩子**：`GameItemFunc.tooltipRichTextFunc` 是 public `Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, RichTextBuilder>`，
  返回 null 则走原版；可用它给打印机追加一行 `Mode: 化学品 / 纯度档: 药用级`（比 `OnCreateTooltipLate` 更贴近物品自身）。
- **现有做法**：本模组已订阅 `ModHook.OnCreateTooltipLate`（`ModEntry.OnTooltip`），继续用它即可，二者不冲突。

### 2.3 备选：窗口式（7 档 + 标签选择）

若不想用 14 步循环，用「target 钩子打开窗口」：`onThisTargetItemFunc` 收到目标 → 打开窗口 → 选择档位/标签 → 盖章。
可复用 API（均 public，`docs/api-findings.md` §8.6 已核对）：

| 用途 | API |
|---|---|
| 单槽容器窗口 | `new PixelWindow(true)` + `new GridPixelElement(1,1)` / `new GameSlotInventory()`；`PixelWindow.Show()/Center()`；`GameItem.SetContentWindow(window)` |
| 机器面板 | `MachineHelper.CreateMachineryWindow(bool canMove, MachineryPanel[] panels)`、`MachineHelper.Panel(label, content, role=null, styleInventories=true)` |
| 浮层按钮/下拉 | `CustomUIManager.Window(id,title,LAYER_OVERLAY)` + `.AddDropdown(档位列表).AddButton("盖章",…).AddCloseButton(…).SetSize(...).Center().Show()`；状态行 `GetElement(...)?.SetText(...)` |
| 原版 labeler 式 | `LabelerUIManager.Instance.OpenUI(GameItem target)` 是 public（`il/LabelerHelper__OnTargetItem.il.txt`）；但它是"标签编辑"语义，不建议硬改成纯度选择 |

**建议**：第一版用"激活循环（模式）+ tooltip 显示"，第二版再加 PixelWindow 精细选择，降低风险。

---

## 3. 100% 纯度可行性

### 3.1 直接把 `CHEMICAL_PRODUCT_PURITY_TAG` 写 100 + `CreateLabPure()`：**自洽**

证据链（全部读路径无上限拦截）：

| 系统 | 读法 | 100 的表现 |
|---|---|---|
| 成分仪 | `RefreshChemMode`：`GetTagReadonly(CHEMICAL_PRODUCT_PURITY_TAG).GetDouble()` + `FormatPurityPercentageFromDouble`（`"{0:F2}%"`） | 显示 `100.00%` + `lab_pure` 的中文条件名（`mech_condition_lab_pure`） |
| 七档映射 | `ChemicalProductHelper.GetGradeNumber`：`lab_pure`=6（最高），`GetGradeDifference` 按数字比较 | 100 → `lab_pure`（≥99 分支），最高档 |
| 客户拒绝 | `WillAcceptNarcotic(item, grade)`：只拒 starch/junk；`grade>=2` 拒 cut_product；`grade>=3` 拒 street_standard | lab_pure 永远通过；`acceptedChemicalProductGrade` 最高也只卡到 street，不会卡 lab_pure |
| 价格 | `CreateLabPure()` 的 `ItemCondition.modValue = +135`（`il/ItemConditionList__CreateLabPure.il.txt`）；`ItemFeature` 的价值修正进入 `GetCurrentValue` | 100 与 99.5 同档同价（价值由档位而非小数决定）——**不会因为写 100 而额外涨价**，这是合理的 |
| 写回 | `GradeProduct` 只在被调用时按液体比例重算；扫描/出售不会重算 | 手动写的 100 不会被自动覆盖；但**任何再次调用 `GradeProduct` 的地方会改回比例值**（我们自己的工厂/桥接要注意别在盖章后再 GradeProduct） |

注意点：
- **本模组已有 `ChemCatalog.ClampPurity(0..100)`，可容纳 100；P1a 的 `BridgeVanillaChemData` 走同一 clamp → 存档物品不会被压回 99。**
- 原版水体系的 99.9%（9990）上限只约束 **water 纯度**，与 `CHEMICAL_PRODUCT_PURITY_TAG` 无关。
- 原版 `ChemicalProductHelper.PURE_GRADE = 99` 是**档位阈值**，不是上限；没有任何 `Math.Min(purity,99)` 一类代码（`GetPurity/RefreshChemMode/GetGradeNumber` 都直读）。
- 风险：`ChemicalFeedbackHelper.GetPurityGradeDifference` 对 `purity` 做档位比较，100 与 99 同档；`ItemCondition.display` 不变；无异常路径。

### 3.2 「复杂提纯后允许 100%」的建议实现（打印机校准）

```csharp
// 建议新增 ChemCatalog.TryCalibrateToPure(GameItem item)：
// 门槛（全部可用现有 tag/字段判断）：
//  · 输入已是 lab_pure 且 fc_purity >= 99.5（复杂提纯链的结果）
//  · 消耗 1 个 catalyst_kit（工具/耗材，可从打印机输入槽扣）
//  · 打印机有电池且电量足够（MachineHelper.CanPower / MachineryHelper.GetMachinePowerUsage）
//  · 温度/pH 在"校准"窗口内（ChemRuntime.Temp/Ph，与现有配方系统一致）
// 通过后：
item.ModifyTag(ChemCatalog.TPurity, s => s.Enable().SetInt(100), false);                 // 本模组主纯度
item.ModifyTag(ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG,
               s => s.Enable().SetDouble(100.0), false);                                  // 原版桥接纯度
ChemicalProductHelper.SetPurityFeature(item, ItemConditionList.CreateLabPure());          // 原版七档显示/价值
item.SetSpriteAndShapeFromMod(ChemCatalog.ModId, "printer_sealed");                       // 可选：盖章后的图标状态
```

叙事上把 100% 定义为「打印机对 ≥99.5% 的复杂提纯品做最后校准/封签」，与用户"取消现成终产物、只留纯度链"的方向一致；
数值上：100 与 99.9 同档，**不产生额外经济优势**，避免破坏原版平衡。

---

## 4. 移除 `can_*_medical / can_*_ultrapure` 现成终产物

### 4.1 现状代码位置（src，只读确认）

| 位置 | 内容 |
|---|---|
| `src/ChemCatalog.cs:103-114` | 12 个 `can_<gas>_<grade>` Spec（`C(...)`，medical 参数决定 MEDICAL 类型 + `fc_medical`） |
| `src/ChemRecipes.cs:633-638` | `CanisterSkus: gas → string[3]`（industrial/medical/ultrapure） |
| `src/ChemMachines.cs:79-104` | 灌装台 resolver：`return Out(skus[Mathf.Clamp(inputGrade,0,2)])` —— **按 inputGrade 选 SKU 的唯一产线入口** |
| `src/ChemMachines.cs:486-487` | 提纯台 `UpgradePurity(produced, (PurityGrade)upgraded)`（三档升级） |
| `src/ChemItemIds.cs:91-102` | 数字 ID 49-60（85 表的一部分） |
| `ChemRecipes.RemoveVanillaAnalog` / 掉落 / 本地化 | `free_chem_drops.json` 有 `can_*_industrial` 引用；Localization 每 id 3 键；图标 `can_*.png` |

### 4.2 若改成「每气体 1 个钢瓶 + 纯度 tag」牵动的系统

| 系统 | 影响 | 处理建议 |
|---|---|---|
| 产出 | 灌装台不再按 inputGrade 出 3 SKU | resolver 改为固定 `can_<gas>_industrial`（或新 id）+ 写 `fc_purity`/原版纯度 tag/`SetPurityFeature`；`inputGrade` 只用于决定纯度数值 |
| 客户收购 | 医疗客户靠 `clientBuyingTagList` 的 `MEDICAL`/`OXYGEN_SUPPLY` tag；成品目前靠 Spec.Medical 设 MEDICAL 类型 | 单钢瓶需 **同时** `SetGameItemType(TypeHelper.CHEMICAL_SUPPLIES)` + `MEDICAL`（§1.4 证明可叠加）；纯度由 `WillAcceptNarcotic`/Feature 决定，医疗客户是否接受需实机确认 |
| 价格 | 目前三档靠 Spec.Value（55/145/210）；单 id 需按纯度动态定价 | `unitValue` 基础价 + `ItemFeature`(+135 lab_pure 等)自动修正；或按 `fc_purity` 手动重算（建议前者，少侵入） |
| 图标 | 三档各有 PNG；单 id 只有一张 | 保留一张"标准钢瓶"（可用 industrial 图）；纯度靠 tooltip/成分仪/打印机盖章显示；若必须视觉区分，用 `SetSpriteAndShapeFromMod` 在盖章后切换 seal 版图标（不影响占格前提：图标同尺寸同 alpha 足印） |
| 存档 | `SaveItemNode` 持久化 `identifier`、`itemState`(TagSystem)、`itemTypes` | 纯度 tag/类型 tag 都会随存档保存；**唯一风险是旧 id 的物品**（见 4.3） |
| 本地化/掉落/QA | 现有 12 个 SKU 均有 3 条本地化键、掉落引用、validate 的"可获取性"断言、`ChemItemIds` 85 条锚点断言（#49-60 固定） | **保留这些注册与键**，只把产线/掉落改成不再产出；QA 断言不需要改 id 表 |

### 4.3 旧存档兼容策略（推荐）

**不要物理删除 `ChemCatalog.Specs` 里的 `can_*_medical/ultrapure`**：`DirectoryMaster.Item(unknownId, true)` 会返回 dummy 空壳并写 Warning
（`il/DirectoryMaster__Item.il.txt`），旧存档里的三档钢瓶会变成无图无价的空壳。三种策略按风险排序：

1. **保留注册 + 停止产出（推荐，风险最低）**：
   - `ChemRecipes.CanisterSkus` 只用于"读取旧 SKU→气体"映射（保留，供迁移/掉落清理用）；灌装台改为单 id + 纯度 tag；
   - 掉落表移除 medical/ultrapure（或保留极低权重作 legacy 来源）；
   - 图标/本地化/`ChemItemIds` 全保留 → 旧存档物品照常存在、可正常出售/展示；新游戏不再产出这些 id。
   - 旧物品的纯度在 P1a `BridgeVanillaChemData` 已桥接，可被扫描/收购；用户可自行用打印机重新盖章成"单一钢瓶"（若做迁移）。
2. **原地迁移（中等风险，建议第二步再做）**：`ModHook.OnGameLoadedInit` 里遍历 `EmporiumEntry.Instance.GetAllItems()` /
   `GetAllOwnedItems()`（均 public），对旧 id 物品执行：改 `identifier`/`identifierName`（public set）→ `SetSpriteAndShapeFromMod` 换图 →
   `SetGameItemType` 叠加/移除类型 → 写纯度 tag/Feature → 调 `unitValue`，下次存档写入新 id。风险：正在被任务/剧情引用的物品、堆叠/容器内物品遍历不全、迁移中断产生半状态。
3. **物理删除（不推荐）**：会让旧存档出 dummy 空壳、QA 的 85 ID/顺序锚点断言（#49-60）失败、本地化/图标出现孤立文件。

**结论**：P1b 先做 1（保留注册、停产出、单 id + 纯度 tag），迁移脚本作为可选后续；任何情况下都**不要动 `ChemItemIds` 的 49-60 顺序**。

---

## 5. 风险 / 未验证

1. 未实机验证：target 钩子交互（选中打印机点药剂瓶）、激活循环模式、打印窗口、100% 显示、客户接受度。
2. `GameItem.CanTarget` 逻辑复杂（530 行 IL，多钩子 OR/AND 归约）；若打印机同时设 `mayItemTargetItemFunc` 与 `mayThisTargetItemFunc` 可能产生双重触发——**只设一组**（推荐照 chem_scanner/labeler 的 "this" 组）。
3. 原版 `LabelerUIManager` 是标签编辑语义，不建议复用来做纯度选择；若用 `CustomUIManager` 浮层窗口，注意它在 `LAYER_OVERLAY` 上跨场景常驻（见 api-findings §8.6）。
4. `ItemFeature` 的 `SetPurityFeature` 会 `AddItemFeature`（若已存在同 category 则复用）；重复盖章是幂等的（`FindItemFeatureByCategory` 先找）。
5. 100% 纯度虽自洽，但**原版敌人/事件/配方是否读取 `purity>99` 做特殊分支**未逐一核对（`ExchangeList` 等对 averagePurity 只做阈值比较，风险低）。
6. 三档 SKU 是否有任务/剧情/商店列表硬引用（如 `NetworkUpgradeList` 的气体订单、`StoreClientListProcurement` 医疗采购）未逐条核对——保留注册的策略对这些无害。
7. `can_*` 图标若最终删除会产生 Localization orphan 与 validate 的图标数断言变化；建议本轮不删。
