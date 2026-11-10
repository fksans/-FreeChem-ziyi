# 便签 / 电话对话 / 分析仪组分 / 氯气区分 / 机器便签 / 索引书 —— 研究

> 只读：元数据 + IL（探针第 19 组 selectors，`out/il/` 约 1082 方法）+ 官方指南 + src 现状（`ChemPhone.cs` 等仅读）。
> **未改 src、未实机验证**。IL 路径 `tools/api-probe/out/il/<类型>__<方法>.il.txt`。

## 0. 结论速览

| # | 问题 | 结论 |
|---|---|---|
| 1 | 便签点不开/被归化学分类 | 原版可点击便签 = item **`handnote`**（`MiscItemDirectory.Handnote`）：type `DOCUMENT`、`IMPORTANT_TAG`、value 0、tags `HANDNOTE_TITLE/BODY/SIGN/FONT`，并在工厂里注册 `canActivate/mayActivate/onActivateSlotItemFunc`（打开 `HandnoteUIManager`）。**直接 `PreBuiltItemHelper.CreateHandnote(body,title,sign,font)` 生成即可**，不要自己造 spec / 不要 `SetGameItemType(CHEMICAL_SUPPLIES)`。 |
| 2 | 电话按钮乱码/`xxlog` | 原版电话按钮文本 **不是** `choiceItem.name`，而是 `choiceNextDialogue.plainText`（`DialoguePhoneUIManager.DisplayChoice` IL：`bubble.TMP.text = choices[i].Item2.plainText`，且先 `LocGenderHelper.ResolveDialogueLine`）。当前 `ChemPhone.cs` 把整页 dialogue 当 `choiceNextDialogue`、挂断传 `null` → 按钮显示整段页面文字/空 → 乱码。原版模式（`PhoneDialogList.ChemistRegular`）：先建**标签用 Dialogue**（如 `new Dialogue().SetText("PLAYER", loc("end_call"))`），再用 `page.AddChoice(DirectoryMaster.Item("$",true), "", "", labelDialogue, action, null)`。 |
| 3 | 分析仪注入组分仍不显示 | `AddLiquidToChemFromList` 只按 `Liquid.GetCurrentPartFromId(liquidId)` 写 **`SetInt(value)`（覆盖、不累加）**；若 `liquidId` 不在 `Liquid.LiquidDict` → `LogError("Liquid with identifier ... not found.")` 且不写。扫描仪组分行来自 `Liquid.Liquids` 的 `currentPart` tag（`RefreshUI` 分支之后统一算），与 chem/water 分支无关。→ 用**原版 liquid id**（acid/base/water/alcohol/chlorine/...）一次给全列表；确保操作的是被扫描的那个 item 实例；旧存档物品需读档后重注入。 |
| 4 | 氯气 vs 原版氯 | 原版 `Liquid` 已有 `identifier="chlorine"`（displayName 本地化键 `liquid_chlorine`、particleSize 5、color #84a251）。它的显示名是「氯」而非「氯气」。**推荐**：`gas_chlorine` 物品用**自定义 Liquid** `fc_chlorine_gas`（displayName=「氯气」）只用于 chem 物品（`CHEM_ITEM_TAG`，**绝不给 LIQUID_CONTAINER 物品**），避免稀释水纯度；或退一步用原版 `chlorine` 组分（显示为「氯」）。自定义注册细节/风险见 §4。 |
| 5 | 机器便签文本 | `MachineHelper.NotePanel(noteSlot)` = `IconPanel(MachineryStyle.IconNote, noteSlot, "note")`；文本在**槽里的 handnote/slip 物品 tag** 中。放一个 `PreBuiltItemHelper.CreateHandnote(body,title)` 到 `noteSlot`（`GraphUtils.TryAcceptAll`/`UncheckedAccept`）即可；随物品存档保存，运行时改文本=换 item 的 tag。 |
| 6 | 索引书版式 | `chem_index.json` 628×804、正文 `x=60,w=600` → 右边 660 **超出 628**，且跨越书脊。安全版式：`x=24..330` 两列各 `w=266`（照 chem_codex），标题 `y=28,h=22`、正文 `y=60,h=650`、`fontSize 14`、每页 20–24 条；若单列只放左页 `x=24,w=266`。 |

---

## 1. 原版便签（可点击打开 + 自定义文字）

### 1.1 原版机制（IL）

`MiscItemDirectory.Handnote()`（121 行）：

- item id **`handnote`**，sprite `Items/items_misc/handnote`，name/desc 用 loc `item_handnote_name/desc`；
- tags：`HANDNOTE_TITLE`、`HANDNOTE_BODY`、`HANDNOTE_SIGN`、`HANDNOTE_FONT`（字符串/整数）；
- type：`DOCUMENT`（**不是** CHEMICAL_SUPPLIES）；`IMPORTANT_TAG`；value 0；
- `canActivateSlotItemFunc` / `mayActivateSlotItemFunc` / `onActivateSlotItemFunc` 全部注册（点击时由 `HandnoteUIManager` 打开内容）。

`PreBuiltItemHelper.CreateHandnote(body, title="", sign="", font=1)`（`PreBuiltItemHelper__CreateHandnote.il.txt`）：
`DirectoryMaster.Item("handnote", true)` → 覆写 4 个 HANDNOTE_* tag → 返回；**保留原版点击打开钩子**。
`PreBuiltItemHelper.CreatePaperWithText(text, title="Note")`：用 `slip` 物品 + `SLIP_TITLE`/`SLIP_BODY`（纸片，单页文字）。
`HandnoteUIManager` 是 public MonoBehaviour（显示用），无需直接调用。

### 1.2 推荐骨架（替换当前 mod 便条）

```csharp
// 生成可点击、带自定义文字（含电话号码）的原版便签
GameItem note = PreBuiltItemHelper.CreateHandnote(
    body: ModHelper.GetLocalized(ChemCatalog.ModId, "phone_merchant_note_body"), // 多行文本可直接用 \n
    title: ModHelper.GetLocalized(ChemCatalog.ModId, "phone_merchant_note_title"),
    sign: "FreeChem",
    font: 1);
ModHelper.SetItemOwnerShip(note, true);          // 归玩家所有，可放入背包/前柜台
// 放置：ModHelper.MoveItemToInventory / MoveItemToFrontCounter / GraphUtils.TryAcceptAll(...)
```
**要点**：便条不要走 `ChemCatalog.Specs`（否则会被 SetGameItemType(CHEMICAL_SUPPLIES)、被当成药剂参与分析/收购）；
若必须用 mod 自己的 id 便于数字 ID 表，也请把 Kind 设为 `document` 且 `SetGameItemType(TypeHelper.DOCUMENT)`，并自行把 `onActivateSlotItemFunc` 绑定到打开 handnote 的方式
（最简单仍是直接使用原版 `handnote` 物品，靠 `PreBuiltItemHelper` 生成，**完全不用注册新 id**）。

---

## 2. 电话 Dialogue API（按钮文本/多页/挂断/返回）

### 2.1 真实签名

```csharp
public class Dialogue {
    public string title; public RichTextBuilder text; public string plainText;
    public Dialogue nextDialogue; public Action startAction; public Action endAction; public float endActionDelay;
    public List<(GameItem, Dialogue, Action)> choices;
    public Dialogue SetText(string title, string text);          // text 可以是本地化键或已解析文本（见下）
    public Dialogue SetText(string title, RichTextBuilder text);
    public Dialogue NextDialogue();                              // 创建并连接到下一页，返回新页
    public Dialogue SetNextDialogue(Dialogue dialogue);
    public Dialogue SetStartAction(Action); public Dialogue SetEndAction(Action);
    public Dialogue AddChoice(GameItem choiceItem, string name, string description = "",
                              Dialogue choiceNextDialogue = null, Action choiceAction = null,
                              Func<bool> choiceEnabled = null);
    public Dialogue AddCustomChoice(GameItem choiceItem, Dialogue choiceNextDialogue = null, Action choiceAction = null);
}
```
`AddChoice` IL：`choiceItem.name = name; choiceItem.shortDescription = description; OverlayHelper.TransformItemChoice(choiceItem,false);`
然后 `choices.Add((choiceItem, choiceNextDialogue, choiceAction))`。
`choiceEnabled` 必须配 `choiceNextDialogue`，否则 LogWarning 且忽略（IL 明确）。
`DialoguePhoneUIManager.DisplayChoice` IL：对每个 choice：`bubble.SetActive(true)`；`LocGenderHelper.ResolveDialogueLine(choices[i].Item2, ...)`；
**`bubble.TMP.text = choices[i].Item2.plainText`**；`SetInteractable(IsChoiceEnabledAt(i))`。
→ **电话按钮文字 = `choiceNextDialogue.plainText`**，不是 `name`；`name/description` 在原版电话里直接传 `""`（`PhoneDialogList.ChemistRegular` IL 可见）。

### 2.2 原版电话写法（照抄即可）

`PhoneDialogList.ChemistRegular`（IL）：
```
displayName = LocHelper.GetLocalizedName("name_ashish");
var main  = new Dialogue().SetText(displayName, loc("ashish_regular1"));
var menu  = new Dialogue().SetText(displayName, loc("ashish_regular2"));  main.SetNextDialogue(menu);
var order1= new Dialogue().SetText("PLAYER", loc("chem_order1", 8, 1));   // 标签页
var quit  = new Dialogue().SetText("PLAYER", loc("end_call"));            // 「挂断」标签页
menu.AddChoice(DirectoryMaster.Item("$", true), "", "", order1, () => {...}, null);
menu.AddChoice(DirectoryMaster.Item("$", true), "", "", quit,   () => {...}, null);
```
- 多页 = `SetText(...).NextDialogue()` 链（或 `SetNextDialogue`），页与页之间自然推进；
- 选项按钮 = **单独的 label Dialogue**（放按钮文案）作为 `choiceNextDialogue`，点击后 `choiceAction` 执行逻辑；
- 图标 = `DirectoryMaster.Item("$", true)`（原版用货币符号物品当占位图标），可换自己的物品；
- 「挂断」= label「结束通话」+ action 里 `PhoneUIManager.Instance.OnPhoneDialogEnd()`/`StopCall()`（原版 `end_call`）。

### 2.3 当前 `ChemPhone.cs` 的问题与修法（据 IL）

- `main.AddChoice(icon, label, desc, OrderResultPage(...), TryPlaceOrder, null)`：把**结果页**当按钮标签 → 按钮显示整段"订购成功…"；
- 挂断 `AddChoice(icon, HangupText(), null, null, Hangup, null)`：`choiceNextDialogue=null` → `DisplayChoice` 取 `Item2.plainText` 时为空/异常 → 乱码（`xxlog` 一类）。
- 修法：每个 choice 传入**独立 label Dialogue**：
  ```csharp
  static Dialogue Label(string text) { var d = new Dialogue(); d.SetText("PLAYER", text); return d; }
  var orderLabel = Label(LOr("phone_chem_order_choice", "Order"));
  main.AddChoice(icon, "", "", orderLabel, () => { TryPlaceOrder(); }, null);
  // 点击后由结果逻辑用 main.SetNextDialogue(resultPage) 或 action 里 PhoneUIManager.StartPhoneDialogSequence 推进
  var hangLabel = Label(LOr("phone_chem_menu_hangup", "Hang up"));
  main.AddChoice(icon, "", "", hangLabel, () => Hangup(), null);
  ```
  若希望"点订购→看结果页"，在 `TryPlaceOrder` 里显式计算并 `DialoguePhoneUIManager.Instance.StartDialogue(resultPage)` 或把 result 页设为 label 之后的 next；**不要**把结果页当按钮 label。
- 按钮 `xxlog`：确认 `LOr` 在缺键时返回的字符串（建议缺键返回英文 fallback，绝不返回 loc key 原样）；所有 label 文本必须来自 `ModHelper.GetLocalized`/`LocHelper.GetLocalizedDialoguePhone` 的**已解析字符串**。

---

## 3. 分析仪组分（为何注入后仍不显示）

IL 事实：

- `ChemicalHelper.AddLiquidToChemFromList(chemItem, list)`：逐项 `ModifyTag(Liquid.GetCurrentPartFromId(kv.Key), s => s.Enable().SetInt(kv.Value))`
  —— **`SetInt` 覆盖**（重复调用会覆盖旧值，不是累加）；**不要求 CHEM_ITEM_TAG/ACTIVATE/纯度**。
- `Liquid.GetCurrentPartFromId(id)`：`LiquidDict` 无此 id → `Debug.LogError("Liquid with identifier <id> not found.")` → 返回 null；
  此时 `ModifyTag(null, ...)` 不会写入任何东西（可能异常/空 tag）。
- `ChemScannerUIManager.RefreshUI`：先按 `CHEM_ITEM_TAG` 分支，**分支之后**再遍历 `Liquid.Liquids` 计算总量与组分行——
  所以只要物品上存在 `currentPart` int tag 且 >0，chem 模式也会显示组分与总液量（不是 water 专属）。
- `ChemicalHelper.GetTotalVolume` 要求 `CHEM_ITEM_TAG`（供 GradeProduct 用），扫描仪显示本身不依赖它。

**注入后不显示的三个实证原因**：
1. **liquid id 用了自定义名**（如 `"fc_hcl"`）→ `LiquidDict` 没有 → LogError、无 tag。必须用原版 id：`water/heavy_metal/.../acid/base/alcohol/chlorine/...`（完整表见 `members.txt` Liquid 段）。
2. **注入到错误实例**：工厂创建 A，扫描的是 B（克隆/读档重建/另一 spec）；应在被扫描/被持有的物品上注入（或统一在工厂与读档桥接时注入）。
3. **被后续 `SetInt` 覆盖**：多次调用 AddLiquidToChemFromList 同一 tag 会覆盖；应一次构造完整 list。
旧存档物品：读档 TagSystem 覆盖（见 `docs/container-component-research.md` §1.2）会把组分 tag 抹掉 → 读档后重注入。

**必定显示成分/总液量的方案**：
```csharp
// 在物品工厂 + OnGameLoaded* 桥接时调用（vanilla ids）
GameItem item = ...;
item.EnableTag(ChemicalHelper.CHEM_ITEM_TAG, false);   // 进 chem 分支并按原版规则显示
ChemicalHelper.AddLiquidToChemFromList(item, new List<KeyValuePair<string,int>> {
    new KeyValuePair<string,int>("acid",   120000),    // 120 ml
    new KeyValuePair<string,int>("water",   80000),    // 80 ml → 总量 200 ml
});
// 校验日志：sum = Σitem.GetTagReadonly(Liquid.currentPart).GetInt()
```
若只想显示「名称」而非原版液体名，可注册自定义 Liquid（见 §4）或在 `RefreshUI` 的 Postfix 里往 `contaminantTMP/totalVolumeTMP` 追加自家行
（`docs/api-findings.md` §8.6 的 public 字段用法；P1a `ChemPatches` 已有类似 Postfix）。

---

## 4. 氯气 vs 原版 `chlorine`

- 原版 `Liquid` 列表**已有 `chlorine`**：`identifier="chlorine"`、`displayName=LocHelper.GetLocalizedMechanic("liquid_chlorine")`（中文「氯」）、`particleSize=5`、`color="#84a251"`、`baseMaxPurityFromMachine=12`、`basePurifierRemoveAmount=4000`；
  `Liquid..ctor` 默认 `canBeRemovedByPurifier=true, particleSize=10, filterDurabilityUsage=2, filterVolumeRemoved=1000, color=#67aaa5, colorMultiplier=1`；lethality/euphoria/addictiveness 默认 0（InitLiquid 里未设置则 0）。
- 它用于净水片/水处理语义（水体系）；`gas_chlorine` 用同一个 `chlorine` 组分时，分析仪会显示「氯」而不是「氯气」，且进入水容器会按水纯度分母被稀释。
- **自定义 Liquid 注册（可行，细节）**：
  ```csharp
  var liq = new Liquid();
  liq.identifier = "fc_chlorine_gas";
  liq.displayName = ModHelper.GetLocalized(ChemCatalog.ModId, "liquid_fc_chlorine_gas"); // 「氯气」
  liq.currentPart = "fc_chlorine_gas";      // tag 名；必须唯一、合法
  liq.lethality = 1.0;                       // 供 GetLethalDose；按模组 hazard 设定
  liq.euphoria = 0; liq.addictiveness = 0;
  Liquid.Liquids.Add(liq); Liquid.LiquidDict[liq.identifier] = liq;   // 必须在首次 AddLiquidToChemFromList 之前
  ```
  风险：① **不持久化**——每次运行都要重新注册（读档后 tag 仍在但 Liquid 缺失 → 组分行显示异常/`GetCurrentPartFromId` 报错）；② 遍历 `Liquid.Liquids` 的原版逻辑会看到它（`WaterHelper.GetWaterPurity` 的 water 占比被稀释、`ChemicalFeedbackHelper.GetLethalDose` 求和）；③ `GetCurrentPartFromId` 依赖 `LiquidDict` 键，若不一致写错 tag。
  → **推荐**：自定义 Liquid 只用于 **chem 物品（`CHEM_ITEM_TAG`，气体/固体）**，**不要**把它写进 `LIQUID_CONTAINER` 物品（避免水纯度污染）；每次 `Init`/`OnGameLoaded*` 重新注册；`gas_chlorine` 的分组分成用 `fc_chlorine_gas`（显示「氯气」），水体系继续只认原版 `chlorine`。
  → 若要最省事/零风险：`gas_chlorine` 用原版 `chlorine` 组分，分析仪显示「氯」（可接受但非「氯气」）。

---

## 5. 机器便签（NotePanel）

`MachineHelper.NotePanel(noteSlot)` IL = `MachineHelper.IconPanel(MachineryStyle.IconNote, noteSlot, "note")`
→ 便签内容就是 `noteSlot` 里的**物品**（handnote/slip），不是独立文本控件。

```csharp
var noteSlot = new GameSlotInventory();
var panels = new[] { MachineHelper.Panel("In", input), MachineHelper.NotePanel(noteSlot), MachineHelper.Panel("Out", output) };
var win = MachineHelper.CreateMachineryWindow(true, panels);
machine.SetContentWindow(win);

// 设置用途文案：放一个原版 handnote（带 HANDNOTE_BODY），随物品存档
GameItem note = PreBuiltItemHelper.CreateHandnote(
    ModHelper.GetLocalized(ChemCatalog.ModId, "machine_<id>_note"),   // 用途说明（多行 \n）
    ModHelper.GetLocalized(ChemCatalog.ModId, "machine_<id>_name"));
ModHelper.SetItemOwnerShip(note, true);
GraphUtils.TryAcceptAll(noteSlot, note, -1);    // 或 noteSlot.UncheckedAccept(note)
```
- 读档：handnote 是普通物品，`SaveItemNode` 保存 identifier/tags → 文本自动保存；**语言切换不会自动更新**（HANDNOTE_BODY 是字面文本），要更新就换 item 或重建。
- 每台机器的用途文案由 docs/手册提供；本地化在**创建时**用 `ModHelper.GetLocalized` 解析。

---

## 6. 索引书（`chem_index.json`）不溢出版式

现状：`width:628 height:804`，正文 `x:60, w:600, fontSize:16, autoHeight:true` → **x+w=660 > 628**，右侧被裁 + 跨书脊。
原版 `chem_codex.json` 的正常版式：标题 `x:24,w:266`、正文 `x:24,w:266`（左页），右页另起一列（约 `x:330`）；628 宽是**展开的双页**（每页约 314 设计单位）。
**推荐参数**（二选一）：

```json
// 方案 A：双列（照 chem_codex，最稳、信息密度最高）
{ "type":"text", "x":24, "y":28, "w":266, "h":22, "style":"title", "align":"center" }
{ "type":"text", "x":24, "y":60, "w":266, "h":650, "style":"body", "fontSize":14, "wrap":true, "autoHeight":true }
{ "type":"text", "x":330,"y":60, "w":266, "h":650, "style":"body", "fontSize":14, "wrap":true, "autoHeight":true }
// 方案 B：单列（只放左页）
{ "type":"text", "x":24, "y":60, "w":266, "h":650, "style":"body", "fontSize":14, "wrap":true, "autoHeight":true }
```
- 行高 ≈ fontSize×1.3 ≈ 18；650 高 → **约 34 行/列**；中文每行约 18–20 字（w=266）；
- 每页/每列 **≤30 行**（留 4 行余量防溢出）；双列 = 每页最多 ~60 行；97 个 ID → **2–3 页**（双列）或 4–5 页（单列）；
- 每行 `#12 盐酸 HCl(aq)·液体` 约 20 字，fontSize 14 可行；`autoHeight:true` 时必须限制行数，否则溢出到书页外；
- 标题/页码/翻页沿用现有 `chrome` 即可；正文 `x` 不要超过 330+266=596（<628）。

---

## 7. 风险 / 未验证

1. 全静态；便签打开、电话按钮、分析仪显示、氯气名、机器便签、书版式未实机验证。
2. 电话按钮语义（label = `choiceNextDialogue.plainText`）来自 `DisplayChoice` IL，非常明确；但"点击后如何推进页面"（是否自动显示 nextDialogue 内容）需按原版 `ChemistRegular` 的 action 行为实测。
3. `xxlog` 具体来源未定位（DLL/CSV 里都没有该串）；按 IL 判断是「label 传 null/整页 dialogue」导致，修复后若仍出现请打印 `LChoiceName` 实际值。
4. 自定义 Liquid 是否会影响原版水纯度/存档，建议先只对气体 chem 物品使用并观察。
5. handnote 的 `onActivateSlotItemFunc` 具体打开哪个 UI（推断 `HandnoteUIManager`）未读 lambda；用 `PreBuiltItemHelper.CreateHandnote` 生成时沿用原版钩子，风险低。
6. 书排版参数来自 628×804 坐标与 chem_codex 实测版式；`notebook_manual` 背景可用绘制区若有内边框，需要按截图微调 x/y ±8。

---

## 8. 初始化 / 加载时序（幂等性，供 QA 语义断言）

**钩子顺序（初始化时机）**：新游戏 `PlayerStore.StartNewGame()` → `StorePhoneClient.InitPhoneClientDict()` 建 `PhoneClientDict` 并注册电话商人（`EnsureRegistered` 幂等：已有号码则只归一化字段）→ 读档 `OnGameLoadedInit/Early/Normal/Late`（旧档 `PhoneClientDict` 不重建 → `EnsureRegistered` 补注册；`RehydrateLoadedItems` 重建容器 tag）→ `OnShutterOpenedEarly`（次晨配送时机：取 pending 订单 → `SpawnItem` + `MoveItemToFrontCounter`）。

**加载顺序**：`ModHook.OnModItemDirectoryInit`（注册物品工厂）先于读档；`DirectoryMaster.Item` 读档重建物品时工厂重跑（钩子/tag 重新生成），随后 `set_state(saved.itemState)` 覆盖 tag → 所以读档后必须跑 `RehydrateLoadedItems`（`BridgeVanillaChemData`/`LIQUID_CONTAINER_TAG` 重建）。

**幂等性**：`EnsureRegistered`、`RehydrateLoadedItems`、`BridgeVanillaChemData` 可在多个钩子重复调用——重复注册只归一化字段、已桥接物品按 `IsTag` 直接跳过；发货先清 pending 标志再 Spawn/Move，避免重复发货；scanner 桥接用 `bridging` 标志防递归。

**时序风险**：`OnGameLoadedEarly` 时 `PlayerStore.Instance`/`EmporiumEntry.Instance` 可能尚未就绪 → 用 `PlayerStore.HasInstance && EmporiumEntry.Instance != null` 守卫，未就绪延后到 `OnGameLoadedNormal/Late`（或 finally 重试一次）；`OnShutterOpenedEarly` 时店铺容器已存在，可安全发货。

**结论**：初始化/加载统一按「先注册 → 再桥接物品 → 最后发货」的顺序，每步幂等；重复调用/旧档路径/时序竞争均已有对应守卫与重试。
