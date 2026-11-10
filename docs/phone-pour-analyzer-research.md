# 电话 / 每天限制 / 倒液兜底 / 分析仪 UI / 仪器自检 —— 研究

> 只读：元数据 + IL（探针第 20 组 selectors，`out/il/` 约 1113 方法）+ src 现状读取。**未改 src、未实机验证**。
> IL 路径：`tools/api-probe/out/il/<类型>__<方法>.il.txt`。

## 1. 电话姓名 / 快捷拨号（为什么空白、为什么不进电话簿）

### 1.1 真正显示的字段：`locID`，不是 `displayName`

- `StorePhoneClient.GetLocalizedDisplayName()` IL（8 行）：`return LocHelper.GetLocalizedClientName(this.locID);` —— **电话 UI（`ContactElement.OnInit` 的 titleTMP）只读 `locID`**。
- `displayName` 字段在电话 UI 路径里**没有被使用**（仅可能用于日志/其他系统）。所以注入时设 `displayName = "FreeChem 化学品供应商"` 不会显示；`locID = "phone_chem_merchant"` 不是游戏内 client-name 键 → `GetLocalizedClientName` 返回空/键名 → **姓名空白**。
- ✅ 修复（两种，推荐 A）：
  - **A. Harmony Postfix**（game code 补丁免费，官方允许）：
    ```csharp
    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.GetLocalizedDisplayName))]
    static class PhoneNamePatch {
        static void Postfix(StorePhoneClient __instance, ref string __result) {
            if (__instance != null && ChemPhone.IsOurClient(__instance))
                __result = ChemPhone.OurDisplayName();   // 已本地化字符串（ModHelper.GetLocalized）
        }
    }
    ```
  - B. 把 `locID` 设成一个**游戏已注册的 client-name 键**（如原版 `name_ashish` 之类），但会显示成原版人名，不推荐。

### 1.2 快捷拨号 / 电话簿列表：`phoneState` + `dialedBefore`

- `PhoneBookUIManager.OpenUI()` IL：清空列表后**遍历 `PlayerStore.PhoneClientDict`（Dictionary 顺序，无单独快捷拨号列表）**，对每个 client 调用 `ShownInPhoneBook()`，为 true 才 `Instantiate(contactElementPrefab)` + `ContactElement.OnInit(number)`。
- `StorePhoneClient.ShownInPhoneBook()` IL：
  ```csharp
  if (phoneState == Cooldown(5) || phoneState == Regular(4)) return dialedBefore;
  return false;
  ```
  → **`phoneState = Responding(2)` 时永远不在电话簿里**；且必须 `dialedBefore = true`。
- `ContactElement.OnInit` IL：标题 = `GetLocalizedDisplayName()`；状态行：`Regular(4)` → `mech_contact_ready`；`Cooldown(5)` → `mech_contact_cooldown_n/1`；否则 `mech_contact_unknown`。
- ✅ 正确设置：
  ```csharp
  c.locID = "name_chem_dealer";                 // 或配合 §1.1-A 的 Postfix 用任意标识
  c.displayName = L("phone_chem_title");        // 保留给 Postfix/日志
  c.phoneState = StorePhoneClient.PhoneState.Regular;   // 4：电话簿可见、"ready"
  c.dialedBefore = true;                         // 5 状态也要求此字段
  c.phoneClientType = StorePhoneClient.PhoneClientType.Supplier; // 1
  c.cooldownDuration = 0; c.currentCooldown = 0; c.amountSpent = 0;
  ```
  若希望"首次拨打后才进电话簿"：初始 `phoneState=Responding(2)`（可拨号但不在簿），第一次通话结束 action 里改 `phoneState=Regular; dialedBefore=true`。
- 拨号流程：`PhoneUIManager.PressNumber(int)` 累积（≤9 位）；`HandleCall(long)`：`IsNumberExist` false 或 `phoneState==None(0)` → `NoNumber()`；`NotResponding(1)` → 无应答；其余状态进入对话（`StorePhoneClient.GetCallDialog()`）。`alias` 只是可拨别名列表，**不决定电话簿**。

## 2. 每天 1 单：可靠日期字段

- ✅ **`StoreStation.GetDayCounter()`**（public static，IL：`StoreStation.Instance != null ? Instance.dayCounter : 0`）——这是原版所有"今天"逻辑用的天数（`RunStats.get_Today` 也是同一实现，但它是 private）。
- ❌ `RunStats.Current.netWorthByDay.Count` 不可靠：该 list 是**每个结束日记录的净值序列**，只在 `RecordDay/OnDayEnded` 时增长，和当前游戏天数不同步（开头/读档/结算前会少一/多一）；`RunStats.seriesStartDay` 还有偏移。
- 推荐：
  ```csharp
  static int Today() => StoreStation.GetDayCounter();   // 0 表示实例未就绪
  bool OrderedToday() => ModHelper.GetModDataInt(ChemCatalog.ModId, "ordered_day", -1) == Today();
  bool PendingDeliver() => ModHelper.GetModDataBool(ChemCatalog.ModId, "order_pending", false);
  // 下单成功：SetModDataInt(ordered_day, Today()); SetModDataBool(order_pending, true);
  // 发放后：SetModDataBool(order_pending, false)（保留 ordered_day 作为当天已订标记）
  ```
- **防连点重复下单**：电话选项点击可能被快速重复触发（`DialogChoiceBubble` 点击→选择）。下单逻辑必须**先原子占位再扣钱/发货**：`if (PendingDeliver() || OrderedToday()) return 失败页;` 然后立刻写 `order_pending=true/ordered_day=today` 再扣钱；`choiceAction` 内用 try/finally 保证失败回滚。另可在 `DisplayChoice` 后禁用 bubble（`SetInteractable(false)`）或在下单成功页立刻 `PhoneUIManager` 结束对话。

## 3. 倒液兜底：Harmony Prefix 完整骨架

### 3.1 关键 public API（IL 已核）

```csharp
int  WaterHelper.GetFreeCapacity(GameItem item);                    // 无 LIQUID_CONTAINER_TAG → 0；capacity - Σ parts
int  WaterHelper.GetTotalVolume(GameItem item);                     // Σ Liquid.currentPart tag（无 tag 检查）
bool WaterHelper.IsEmpty(GameItem item);                            // 无 LIQUID_CONTAINER_TAG → true
void WaterHelper.AddLiquid(GameItem container, string liquidId, int addAmount); // parts；非法 id → LogError；超容量自动截断
List<KeyValuePair<string,int>> WaterHelper.RemoveAndReturn(GameItem container, int volumeInPart);
// ↑ 无 LIQUID_CONTAINER_TAG 要求：按比例取出、扣减源 tag、调 OnLiquidContainerPourOutOf；volume≥总量则全取
// 其它：TransferLiquid(source,target)（要求双方 LIQUID_CONTAINER_TAG）；RemoveMl/Remove/RemoveAndReturn 同族
void WaterFeatureHelper.OnLiquidContainerPourInto(GameItem target, GameItem source, bool wasEmpty);
```
液体单位：**1 ml = 1000 part**；本模组 `ChemRecipes.PourVolumeMl=500`、`PourPartPerMl=1000`、`PourParts` 为比例表。

### 3.2 Prefix 骨架（正向：本模组液体源 → 原版容器）

```csharp
[HarmonyPatch(typeof(WaterHelper), nameof(WaterHelper.TransferLiquid))]
static class PourModSourcePatch
{
    // 只接管「源 = 本模组液体物品、且源没有原版容器 tag（旧档/未桥接）」；
    // 新物品已 InitLiquidContainerItem（有 tag），交给原版走。
    static bool Prefix(GameItem sourceContainer, GameItem targetContainer)
    {
        try {
            if (sourceContainer == null || targetContainer == null) return true;
            if (sourceContainer.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)) return true;      // 新物品→原版
            if (!ChemCatalog.IsOurLiquid(sourceContainer.identifier)) return true;          // 非本模组
            if (!targetContainer.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)) return false;     // 目标必须原版容器

            // 1) 一次性把源桥接成原版液体容器（幂等）：Init + 按 PourParts 写液体 tag
            ChemCatalog.BridgeLiquidData(sourceContainer);   // 内部 Init + AddLiquid（保持 name/desc/value/sprite）
            if (!sourceContainer.IsTag(WaterHelper.LIQUID_CONTAINER_TAG)) return false;

            // 2) 容量限流：free = 目标剩余；src = 源当前 part 总量
            int free = WaterHelper.GetFreeCapacity(targetContainer);
            if (free <= 0) return false;
            int src = WaterHelper.GetTotalVolume(sourceContainer);
            int move = Math.Min(free, src);
            if (move <= 0) return false;
            bool wasEmpty = WaterHelper.IsEmpty(targetContainer);

            // 3) 从源按比例取出（扣减源 tag）→ 注入目标（AddLiquid 限容、更新水特征）
            var parts = WaterHelper.RemoveAndReturn(sourceContainer, move);
            foreach (var kv in parts)
                if (!string.IsNullOrEmpty(kv.Key) && kv.Value > 0)
                    WaterHelper.AddLiquid(targetContainer, kv.Key, kv.Value);

            // 4) 特征/纯度/价格刷新（原版倒液必须调这个）
            WaterFeatureHelper.OnLiquidContainerPourInto(targetContainer, sourceContainer, wasEmpty);

            // 5) 源空 → 处理：删除物品（Don 需按原版语义；建议保持空瓶可回收：改成 empty 物品）
            if (WaterHelper.GetTotalVolume(sourceContainer) <= 0)
                ChemCatalog.OnBottleEmptied(sourceContainer);   // 建议：改成空瓶/销毁，见风险
            return false;   // 完全接管，跳过原版
        }
        catch (Exception e) { ChemRuntime.Log("[pour] prefix failed: " + e.Message); return true; }
    }
}
```
**源空处理建议**：不要直接 `Destroy()`（用户可能想要空瓶）；更原版的做法 = 把源物品替换为 `gas_canister_empty` 的等价"空试剂瓶"（或保留物品、`fc_purity=0`、液体 tag 清零、改名「空瓶」），并在 `OnGameLoaded*` 桥接时保持。若 lead 决定销毁，用 `sourceContainer.Destroy()`（public）并在 `GraphUtils.ReportUnplaced` 之外不要重复报告。

**反向（原版容器 → 本模组瓶）**：本模组新瓶已 `InitLiquidContainerItem` → 有 `LIQUID_CONTAINER_TAG`，原版 `TransferLiquid` 可直接工作（双向），无需补丁；旧档瓶需要同一函数先 `BridgeLiquidData(target)`：
```csharp
static bool Prefix(GameItem source, GameItem target) {
    if (target != null && ChemCatalog.IsOurLiquid(target.identifier)
        && !target.IsTag(WaterHelper.LIQUID_CONTAINER_TAG))
        ChemCatalog.BridgeLiquidData(target);   // 桥接后返回 true，让原版 TransferLiquid 正常处理
    ...
}
```
（两个方向可合并成一个 Prefix，按 source/target 分别判断。）

**交互触发兜底**：Prefix 只在原版调用 `TransferLiquid` 时生效；若旧档源瓶连 `onThisTargetItemFunc` 都没有，玩家"选中它点目标"根本不会触发。兜底 patch：
```csharp
[HarmonyPatch(typeof(GameItem), nameof(GameItem.Target))]
static class PourLegacyRelinkPatch {
    static void Prefix(GameItem __instance) {
        if (__instance == null || !ChemCatalog.IsOurLiquid(__instance.identifier)) return;
        if (!__instance.IsTag(WaterHelper.LIQUID_CONTAINER_TAG))
            ChemCatalog.BridgeLiquidData(__instance);   // 重新 Init → 恢复 target 钩子
    }
}
```
（`GameItem.Target` 是 public；Prefix 不改变返回值，原版随后会执行；避免每次倒液重复 Init：BridgeLiquidData 内部判 tag。）

## 4. 分析仪 UI：Postfix 直接写 TMP（不依赖 Liquid tag）

`ChemScannerUIManager` **全部字段 public**（`members.txt`）：`productNameTMP / testedTMP / labelTMP / contaminantTMP / totalVolumeTMP`（TMPro）、`currentScannedItem`、`panel`、`confrontButton/matchButton`、4 个效果组件。
`RefreshUI()` 是 public void；Postfix 在**原版全部计算之后**执行 → 安全覆盖，不会递归（不要再调 RefreshUI/RefreshChemMode）。

```csharp
[HarmonyPatch(typeof(ChemScannerUIManager), nameof(ChemScannerUIManager.RefreshUI))]
static class ScannerCustomLinesPatch
{
    static void Postfix(ChemScannerUIManager __instance)
    {
        try {
            var it = __instance.currentScannedItem;
            if (it == null || !ChemCatalog.IsOurReagent(it.identifier)) return;
            var spec = ChemCatalog.Find(it.identifier);
            string L(string k, params object[] a) => ModHelper.GetLocalized(ChemCatalog.ModId, k, a);
            if (__instance.productNameTMP != null) __instance.productNameTMP.text = it.name;

            // 纯度/档位（我们自己算，不依赖 CHEMICAL_PRODUCT_PURITY_TAG）
            double purity = ChemRuntime.PurityOf(it);                    // 0–100
            string grade = ChemCatalog.GradeDisplayOf(purity);           // 七档文案（动态）
            if (__instance.testedTMP != null)
                __instance.testedTMP.text = L("scanner_tested") + "\n"
                    + WaterFeatureHelper.FormatPurityPercentageFromDouble(purity) + "\n" + grade;
            if (__instance.labelTMP != null)
                __instance.labelTMP.text = L("scanner_labeled") + "\n" + grade;

            // 成分（用我们自己的 ComponentParts：formula/percent），不写 Liquid tag
            string comp = ChemCatalog.BuildCompositionLines(it);         // 例：HCl 36% / H2O 64%
            if (__instance.contaminantTMP != null) __instance.contaminantTMP.text = comp;

            // 总液量：固定 500 ml/件（或按 spec）；FormatAmount 是 public static
            int parts = ChemCatalog.TotalVolumePart(it);                 // 500*1000
            if (__instance.totalVolumeTMP != null && parts > 0)
                __instance.totalVolumeTMP.text = L("scanner_total")
                    + WaterScannerUIManager.FormatAmount(parts);

            // 毒性/欣快图标：依赖我们已有 fc_tox/fc_euph patches（IsToxic/GetEuphoriaLevel 已被覆盖），
            // 或直接 __instance.toxicityEffect.gameObject.SetActive(...)（字段 public 是组件引用）
        }
        catch (Exception e) { ChemRuntime.Log("[scanner] postfix failed: " + e.Message); }
    }
}
```
**防重复行**：P1a 的 `RefreshUI` Postfix 是"空则写/非空追加"（`ChemPatches.cs:256-260`）；本 Postfix 改为**直接覆盖**四行文本（先 `= ""` 再写），并把 P1a 的追加逻辑改为写标记（或直接删除），避免同一行出现两遍。建议最终只保留一个 Postfix（本文件逻辑），原版行仅在"我们的物品"时被覆盖。

## 5. 仪器全面自检清单

现有机器（`src/ChemMachines.cs`）：`decanter, filtration_bench, titration_bench, crystallizer, chem_reactor, electrolyzer, gas_generator, condenser_unit, fractionator, high_pressure_synth, canister_filling, gas_scrubber` + 实时 `heating_platform` + 物品型 `label_printer`（ChemPrinter）。
窗口 API：`MachineHelper.CreateMachineryWindow(bool canMove, MachineryPanel[] panels)`（另有 `displayOrder` 重载）、`MachineHelper.Panel(label, content, role=null)`、`PowerPanel(battery)`、`NotePanel(noteSlot)`、`IconPanel(icon, content)`、`GetBodyStorage(machine,index)`；槽 = `GameSlotInventory` / `GameGridInventory`；机器逻辑 = `MachineryHelper.InitMachinery(item, power)` + `MachineProgressHelper.InitProgressTypeMachine` + 自定义 `Finish`。

逐机器自检（逐项打勾；括号=可能不可用的点）：
1. **decanter（滗析）**：1 输入槽（液体容器）+3×3 输出；无电（Power=0 → 不应有电池面板）；只接受液体（`PhaseOf==Liquid`），气体拒绝；产物取出后输入空瓶处理（保留/销毁）；循环 1。
2. **filtration_bench（过滤）**：1 输入 + 滤纸专用槽 + 输出；无电；**滤纸槽不能被普通投料占位**（`AcceptsInput` 把 filter_paper 限定给该槽）；每循环消耗 1 滤纸。
3. **titration_bench（滴定）**：2 输入 + 3×2 输出；无电；仪器（precision_balance/ph_meter）应作为"工具"影响结果而不是被当投料（`AcceptsInput` 已排除）。
4. **crystallizer（结晶）**：2 输入、需电（5）；电池面板 presence=Powered；输出满时 `CanAcceptOutputs` 必须阻止 Finish。
5. **chem_reactor / electrolyzer**：3/2 输入、模块槽 + 电池；`MachineryHelper.UpdatePowerUsageProcessTypeMachine` 在模块增删后调用；电力不足时低电模式（`CheckLowPowerMode/IsLowPowerMode`）。
6. **gas_generator / condenser_unit / fractionator / high_pressure_synth**：输入槽数、输出 3×3、电力对应 PowerOf；温度/pH/catalyst 输入条件必须能从 UI 读到（仪器类物品是"拿在手里"的全局状态，不是槽）。
7. **canister_filling（灌装）**：1 输入 + **专用空瓶/空桶槽**（ContainerSlot）+ 4×4 输出；电力 9；空瓶槽不能被气体源/产物占位；`CanAcceptOutputs` 防溢出；P1b 单 SKU 后纯度写 tag。
8. **gas_scrubber（洗气）**：1 输入 + 3×3 输出；电力 7；只接受气体；过滤后纯度升档；输出满判定。
9. **heating_platform（实时加热台）**：非 Defs；容器槽 + 加热器槽 + 灯芯 + 燃料 + 输出；`ChemHeating` 每秒驱动；点击酒精灯/喷灯切换高档（`ToggleHighByHeater`）；燃料用原版酒精。
10. **label_printer（打印机）**：物品不在机器 Defs；target 钩子（选中打印机→点药剂瓶）；模式激活循环；缺 `onThisTargetItemFunc` 时点不出效果（检查 `ChemPrinter.Bind` 注册）；盖章写 tag/feature。
11. **note 面板**：13 台机器都应 `NotePanel(noteSlot)`，首次创建时 `PreBuiltItemHelper.CreateHandnote(desc, name)` 放入；读档后 note 物品随存档恢复。
12. **通用检查**：①窗口槽位与 `MachineLayout.InputSlots/OutputW/OutputH/Powered/Modules/FilterSlot/ContainerSlot` 一一对应；②`MachineHelper.GetBodyStorage` 读取顺序与窗口 Attach 顺序一致；③每个循环 Finish 前 `CanAcceptOutputs`；④输入消耗后空瓶的归属（销毁/回架）；⑤机器 close/open 状态保存（`MachineProgressHelper`）；⑥点击切换/激活的仪器（温度计/pH计/天平/护目镜/催化剂/酒精灯）都有 `onActivateSlotItemFunc` 并有 tooltip 提示；⑦气瓶槽（若加热台/煅烧加）只接受 `can_*` 并按 `CanisterSkus` 反查 `gas_*`；⑧窗口面板顺序固定（`displayOrder` 重载）。

## 6. 风险 / 未验证

1. 全部静态；电话姓名/电话簿、倒液、分析仪 TMP、机器窗口均未实机。
2. `LocHelper.GetLocalizedClientName(locID)` 的缺键行为未读 IL（返回 ""/键名未知）→ §1.1-A Postfix 最稳。
3. `RemoveAndReturn` 的 `volumeInPart` 单位是 part（1 ml=1000）；Prefix 中 `free`/`src` 同为 part，不要混用 ml。
4. 反向倒液：新物品 Init 后原版 `TransferLiquid` 已可用；旧档瓶需 `BridgeLiquidData`，但"选中源瓶"本身依赖 target 钩子（见 §3 的 `GameItem.Target` 兜底）。
5. 机器自检中"专用槽不能被投料"依赖 `GameSlotInventory.mayInventoryAddItemFunc` + `AcceptsInput` 双侧限制；遗漏一侧会出现"槽位互相污染"。
6. 索引/其它文档状态与本次结论无关；本文件仅研究。
