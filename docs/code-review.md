# 代码审查清单（P0 / P1）—— P6c QA 逐条核对用

> 来源：用户 P5d/P6 反馈 + `docs/*-research.md` 结论 + 当前 `src/` 静态审查（2026-11-10）。
> **P0 = 阻断用户核心流程的缺陷；P1 = 体验/一致性/数据缺陷；P2 = 风险/卫生/性能。** 状态列含义：**修复 / 部分修复 / 未修复 / 延期**。
> 本清单由 api-researcher 应 QA（task-68）要求落盘；实际改动归 features/code-core，修复状态以当前磁盘代码为准，**均未实机验证**（除标注 validate/logic-harness 通过者）。

---

## 0. 复核说明（code-core / task-67 追加，2026-11-10 14:30 快照）

- 本节以下（§1–§4）**保留 api-researcher 原始条目不删改**，只在每条末尾追加 `【code-core 复核】` 结论（含证据与当前行号）；新增问题放 §5，扩展审查放 §6–§9。
- 复核方法：逐文件通读当前 `src/*.cs`（13 个文件，约 6.5k 行）+ 只读元数据/IL 探针（`tools/api-probe`，未写入仓内，输出到临时目录）+ 静态一致性脚本 + `dotnet build` / `logic-harness` / `logic_check` / `validate`。
- **行号是快照值**：features 的 task-66 正在并发修改 `ChemPhone.cs` / `ChemPatches.cs` / `ChemCatalog.cs` / `ChemMachines.cs` / `ModEntry.cs` / `ChemRuntime.cs`，本复核期间这些文件行数多次变化。定位请以**函数名/关键字**为准，行号仅供快速跳转。
- **最终快照（build 161,280 B，features task-66 修复后复核）**：`dotnet build -c Release` → **0 警告 0 错误**（`src/bin/Release/netstandard2.1/FreeChem.dll` = **161,280 bytes**）；`logic_check.py` → **44 条反应 / 763 项断言全部通过**；`logic-harness` → **结论：全部通过**（44 反应 / 13 设备 / 6 条热反应 / 数字 ID 97）；`validate.py` → **77 PASS / 1 WARN / 0 FAIL**（唯一 WARN 是归档 v1 备份含废弃 token，不属发布包）。数据一致性脚本：Specs 84、图标 110、本地化 zh/en 各 440 键且无差异、掉落 40 个 id 全部已注册、44 条反应输入输出全部已注册。
- **状态口径（按 Lead 2026-11-10 指示）**：原清单 7 个 P0 / 10 个 P1 已在 features task-66 修复 → 状态标「已修复（build 161,280 B / qa PASS）」；P1-8 空瓶策略标「延期：按 Lead 决策销毁并日志」。**但 code-core 复核发现部分新问题仍开放（§5），其中 NEW-P0-1 会在运行时抵消 P0-2 的修复效果，见 §11。**

## 0.1 原状态总览（api-researcher 原表，状态列按复核更新；**最终状态见末列「QA P6 最终验证」**）

| 编号 | 严重级 | 问题（一句话） | 原状态 | code-core 复核状态 |
|---|---|---|---|---|
| P0-1 | P0 | 电话联系人姓名为空（UI 读 locID，不是 displayName） | 未修复 | **未修复（已用 IL 证实机制）**  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复（ChemPhone.cs 有 GetLocalizedDisplayName Postfix）** |
| P0-2 | P0 | 联系人/快捷拨号列表不显示（phoneState/dialedBefore 条件不满足） | 未修复 | **未修复，且新增回归 NEW-P0-1（EnsureQuickDial 把状态改回 Responding）**  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：**未修复（回归仍在）**：EnsureQuickDial() 仍把 phoneState 覆写为 Responding，与 NEW-P0-1 一致；须改回 Regular 并保留 dialedBefore=true** |
| P0-3 | P0 | 每日 1 单用 `RunStats.netWorthByDay.Count` 判日期，不可靠 | 未修复 | **已修复**（改用 `StoreStation.GetDayCounter()`）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复（StoreStation.GetDayCounter；netWorthByDay/RunStats 无命中）** |
| P0-4 | P0 | 下单按钮可连点重复扣款/重复下单 | 部分修复 | **已修复（代码层）**：先占位→再扣款→异常回滚；待实机连点验证  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复（代码层）：先占位→扣款→异常回滚，pending/日限守卫在扣款前；待实机连点** |
| P0-5 | P0 | 商人便条被归 CHEMICAL_SUPPLIES 且点不开 | 未修复 | **部分修复**：主礼包路径已用原版 handnote；**Spec/工厂/调试 ID 路径仍会产生打不开的化学品便条**  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：部分修复：主礼包已用原版 handnote；但 note_chem_dealer Spec（#97）仍在，F10/DirectoryMaster 路径仍会生成 CHEMICAL_SUPPLIES 便条（与 code-core 复核一致）** |
| P0-6 | P0 | 试剂瓶仍倒不进原版容器（旧档 tag/钩子丢失、无体积扣减） | 部分修复 | **大部分修复**：TransferLiquid Prefix + GameItem.Target 重挂 + fc_volume_ml 已实现；**但主路径体积记账错误（NEW-P1-1）**  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复（QA 静态）：TransferLiquid Prefix + GameItem.Target + fc_volume_ml + RemoveAndReturn + GetFreeCapacity + 空瓶 Destroy 均在；残留 NEW-P1-1 见 §5** |
| P0-7 | P0 | 分析仪对本模组药剂不显示成分/总液量（只在 total=0 时写） | 部分修复（task-66 C 进行中） | **已实现（待实机）**：RefreshUI Postfix 已改为对本模组药剂覆盖 4 行 TMP；仍有 lastSignature 静态缓存风险（P2-12）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复（QA 静态）：RefreshUI 唯一 Postfix 直接覆盖四行 TMP（不改 RefreshUI，防递归）；待实机** |
| P1-1 | P1 | 索引书正文越界（x=60,w=600，超出 628 画布） | 未修复 | **已修复**（现为 4 页、x=24/w=266，脚本核对无越界元素）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复：chem_index.json 4 页 / x=24,w=266,fontSize14 / 全部元素 x+w≤628、y+h≤804** |
| P1-2 | P1 | 名称/描述写死纯度档位词，与动态纯度冲突 | 部分修复 | **仍部分**（`sol_caffeine_desc` 仍写「医药级纯度」；can_*_medical/ultrapure 作为 legacy SKU 保留合理）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：部分修复：sol_caffeine_desc 仍写「医药级纯度」（docs-i18n 待改）；其余档位词已去** |
| P1-3 | P1 | 成分仪毒性/欣快图标与模组数值不一致 | 修复 | **已修复**（4 个 Postfix 均在，目标方法已在程序集中核对存在；语义见 P2-15）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复：ChemicalFeedbackHelper 4 个 Postfix 均在** |
| P1-4 | P1 | 机器便签无法显示用途文本 | 修复 | **已实现（待实机）**（每台机器 NotePanel + CreateHandnote；语言切换需重建，已知限制）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复：每台机器 NotePanel + machine_note_<id> 14 条键；handnote 内容** |
| P1-5 | P1 | 13 台机器窗口槽位/专用槽/输出满/电力未逐台核对 | 部分修复 | **本次已逐台核对**，见 §7；结论：槽位/配方匹配基本正确，另有 3 项 P1/P2（NEW-P1-6、P2-5、P2-6）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复（静态逐台）：13 机器 LayoutOf/专用槽/电力/气瓶槽断言 PASS；动态实机未验证，NEW 项见 §5** |
| P1-6 | P1 | 氯气在分析仪显示为「氯」而非「氯气」 | 修复 | **已实现（待实机）**（自定义 Liquid `fc_chlorine_gas` + 显示名兜底；全局 Liquid 列表注入有 P2-6 风险）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复：ComponentParts[gas_chlorine]=fc_chlorine_gas，显示「氯气」** |
| P1-7 | P1 | 电话对话按钮文案为整页文本/挂断不可用（乱码） | 未修复 | **已修复（代码层）**：每个选项均用独立 label Dialogue，每页含挂断（`NewLabel`/`AddHangup`）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复：每选项独立 label Dialogue（AddChoice 第4参），每页 AddHangup** |
| P1-8 | P1 | 液体倒空后空瓶处理未定义（销毁还是保留） | 延期 | **延期：按 Lead 决策销毁并日志**（两条倒液路径空即 `Destroy()`，并输出 `[pour] … source emptied and destroyed` 日志；本项不再作为缺陷跟踪）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：延期：按 Lead 决策销毁并日志** |
| P1-9 | P1 | 打印机 target 钩子在读档后可能丢失 | 修复 | **已实现（待实机）**：`ChemPrinter.Bind` 在工厂分支 `ModEntry.BuildItem` case "label_printer" 内  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：已修复：ChemPrinter.Bind 在 ModEntry.BuildItem case "label_printer"（工厂重建）；待实机** |
| P1-10 | P1 | 机器重开/读档后运行中循环、面板状态恢复未验证 | 部分修复 | **仍部分 + 新增 4 项加热台状态问题**（NEW-P1-2/3/4/5、P2-7）  | **→ QA P6 最终验证（build 161,280B / validate 77 PASS·1 WARN·0 FAIL / features task-66）：部分：静态布局/循环通过；读档面板状态/加热台 4 项 NEW-P1-2..5 未实机验证（见 §5）** |

## 0.2 QA P6c 最终验证（2026-11-10，task-68）

- 基线：uild **0 警告 0 错误 / FreeChem.dll 161,280 bytes**；logic_check **44 反应 / 763 断言全通过**；logic-harness **280 PASS / 0 FAIL**；alidate **77 PASS / 1 WARN / 0 FAIL**（WARN = v1 归档 token）；本地化 440 / 图标 110 / 数字 ID 97 / 夜间 44 + 实时 6 / 设备 13。
- 判定口径：**§0.1 表末列「QA P6 最终验证」为最终状态**；§1/§2 正文里的历史状态描述保留为审查快照；§5 及 NEW-* 条目归 code-core 复核范围（不重复列举）。
- **P0 结论**：P0-1/3/4/6/7 已修复（P0-4/6/7 待实机）；**P0-2 仍有回归**（EnsureQuickDial() 将 phoneState 覆写为 Responding，电话簿/快捷拨号仍不显示）；P0-5 部分修复（主礼包 handnote，但 #97 legacy Spec 路径仍会生成化学便条）。
- **P1 结论**：P1-1/3/4/5/6/7/9 已修复（P1-5/9 待实机）；P1-2 部分（sol_caffeine_desc 仍写「医药级纯度」，docs-i18n 待改）；P1-8 **已定：Destroy+日志**；P1-10 部分/未实机。
- 未实机验证：电话姓名/电话簿/快捷拨号/跨天/连点、旧档倒液、分析仪四行显示、机器逐台/读档状态、handnote 打开。
---

## 1. P0 问题（逐条：描述 / 涉及文件 / 状态 / 验证方式）

### P0-1 电话联系人姓名空白
- **问题**：`StorePhoneClient.GetLocalizedDisplayName()` IL 只读 `locID` → `LocHelper.GetLocalizedClientName(locID)`；`displayName` 不参与电话 UI。当前 `ChemPhone.CreateClient` 设 `displayName=本地化名` 但 `locID="phone_chem_merchant"`（游戏未注册该 client-name 键）→ 电话簿/通话界面姓名空。
- **涉及文件**：`src/ChemPhone.cs`（CreateClient/ApplyClientFields）。
- **状态**：**未修复**。建议：Harmony Postfix `StorePhoneClient.GetLocalizedDisplayName`（我们的 client 返回 `ModHelper.GetLocalized` 结果）；或改用游戏已注册的 client-name 键。
- **验证**：`grep -n "GetLocalizedDisplayName" src/*.cs` 应有补丁类；游戏内打开电话簿看联系人名 = 「化学品商人」（非空、非键名）。
- **【code-core 复核】** 未修复，机制已用 IL 证实：`GetLocalizedDisplayName` 反汇编仅 3 条有效指令 `ldarg.0 / ldfld locID / call LocHelper::GetLocalizedClientName`。当前 `ApplyClientFields`（ChemPhone 约 L81-96）仍 `locID="phone_chem_merchant"`、`displayName=LOr("phone_chem_title",…)`；由于游戏 client-name 键表不包含 `phone_chem_merchant`，显示会是空串或裸键。**建议**：加一个 Postfix 补丁（目标方法已确认存在于程序集）或改用一个原版已注册但语义可接受的 locID（不推荐，会污染原版联系人）。**已确认相关 API**：`StorePhoneClient.GetLocalizedDisplayName()`、`LocHelper.GetLocalizedClientName(string)` 均存在。

### P0-2 快捷拨号/联系人列表不显示该方法
- **问题**：`PhoneBookUIManager.OpenUI` 遍历 `PlayerStore.PhoneClientDict`，仅收录 `ShownInPhoneBook()`==true 的联系人；IL：**phoneState 必须 `Regular(4)` 或 `Cooldown(5)` 且 `dialedBefore==true`**。当前设 `Responding(2)` 且未设 `dialedBefore` → 永远不会出现在电话簿/快捷拨号。
- **涉及文件**：`src/ChemPhone.cs`（EnsureRegistered/CreateClient）。
- **状态**：**未修复**（ChemPhone 注释仍误以为 Responding 会入列表）。修复：`phoneState=Regular; dialedBefore=true`（首次通话后可再置 Regular）。
- **验证**：静态断言 `ChemPhone` 含 `PhoneState.Regular` 与 `dialedBefore = true`；游戏内电话簿能看到并一键拨打 96686。
- **【code-core 复核】** **仍未修复，且 task-66 引入新回归 NEW-P0-1（见 §5）**。已用 IL 证实两条原版条件：`ShownInPhoneBook()` = `(phoneState==Regular(4) || phoneState==Cooldown(5)) && dialedBefore`；`PhoneBookUIManager.OpenUI` 只对 `ShownInPhoneBook()==true` 的条目 `Instantiate(contactElementPrefab)`。当前 `ApplyClientFields` 已改成 `Regular + dialedBefore=true`（正确），**但 `EnsureQuickDial()` 又把 `phoneState` 覆写为 `Responding`**，而 ModEntry 三处（新档/读档/开店）都会调用 `EnsureQuickDial()` → 联系人被反复踢出电话簿。`OnNewDay()` 不会覆盖 Regular（IL 仅在 Cooldown 冷却结束时置 Regular），所以只要不调用 EnsureQuickDial 覆写即可。

### P0-3 每日 1 单的日期来源不可靠
- **问题**：`ChemPhone.CurrentDayIndex()` 用 `RunStats.Current.netWorthByDay.Count` 近似"第几天"。该 list 只在 `RecordDay/OnDayEnded` 增长（读档/结算前会少一/多一），`seriesStartDay` 还有偏移 → 同日可重复下单或跨天无法再订。
- **涉及文件**：`src/ChemPhone.cs:450-458`（CurrentDayIndex），及 255/330 调用处。
- **状态**：**未修复**。修复：改用 `StoreStation.GetDayCounter()`（public static，原版 `RunStats.get_Today` 同源；未就绪返回 0），并在 ModData 写 `ordered_day`。
- **验证**：`grep -n "netWorthByDay" src/` 应为 0 处；跨天两次通话分别可下单/被拒（日志 `[phone]` 显示 day 值递增）。
- **【code-core 复核】** **已修复**：当前 `CurrentDayIndex()` = `try { return StoreStation.GetDayCounter(); } catch { return 0; }`；`grep netWorthByDay src/` 已无命中；`StoreStation.GetDayCounter()` 已在程序集中核对为 `public static int`。待实机验证跨天/读档边界（day 未就绪返回 0 时，与 ModData 里的 -1 不相等 → 首日可下单，行为可接受）。

### P0-4 连点重复下单 / 重复扣款
- **问题**：`choiceAction`（TryPlaceOrder）内检查 pending/day 后再扣款，但未"先原子占位再扣款"；快速连点或结果页返回再次点击时可能重复执行。
- **涉及文件**：`src/ChemPhone.cs`（TryPlaceOrder、OrderResultPage）。
- **状态**：**部分修复**（有 pending/day 检查，但缺防重入与"先占位后扣款"顺序；无连点锁）。
- **建议**：action 开头 `if (pending||orderedToday) return 失败页;` → 立即写 `order_pending=true, ordered_day=Today()` → 再扣款/派发；异常回滚；或点击后立即进入结果页并禁用选项。
- **验证**：连点订购按钮 5 次，现金只扣一次、ModData 只有一单、日志只有一条 `[phone] order placed`；跨天可再订。
- **【code-core 复核】** **代码层已按建议修复**：当前 `TryPlaceOrder` 先 `SetModDataInt(DayKey)` → `SetModDataBool(PendingKey,true)` → `SetModData(ItemsKey)` 占位，再 `playerCash = cash - OrderPrice`，扣款抛异常则回滚 pending 并清 ItemsKey。第二次点击会命中开头的 pending 检查 → 不会重复扣款。残留（P2-2）：结果页 `nextDialogue` 在 BuildDialog 构建对话时就按当时现金/日期预计算，若玩家在对话期间现金变化，页面文案可能与实际结果不一致；且 `playerCash` 是 public 字段赋值，几乎不会抛异常，回滚分支实际是防御性代码。**待实机连点验证**。

### P0-5 商人便条被归化学分类且点不开
- **问题**：`ChemCatalog.cs:117` 把 `note_chem_dealer` 注册为 `Kind="tool"` 的 Spec，而 `ChemCatalog.Create` 对所有 Spec **无条件** `SetGameItemType(spec.Medical?MEDICAL:CHEMICAL_SUPPLIES)`（line 223 附近）→ 便条变成化学品类；同时它没有原版 `handnote` 的激活钩子（`MiscItemDirectory.Handnote` 才注册 onActivate），所以点不开。
- **涉及文件**：`src/ChemCatalog.cs`（Spec 定义、Create 的类型分支）；若要保留 mod id 需自建 handnote 物品。
- **状态**：**未修复**。修复：便条改用 `PreBuiltItemHelper.CreateHandnote(body,title,sign,font)`（原版 item `handnote`，DOCUMENT 类型 + 点击打开）；或对 `Kind=="note"` 不设 CHEMICAL_SUPPLIES 并绑定 `onActivateSlotItemFunc` 打开内容。
- **验证**：`grep -n "note_chem_dealer" src/` 生成方式应为 handnote/CreateHandnote；游戏内点击便条能打开文本且类型不是化学品（收购/分析仪不把它当试剂）。
- **【code-core 复核】** **部分修复**：`ModEntry.OnNewGameStarted` 主礼包路径已改用 `PreBuiltItemHelper.CreateHandnote(noteBody, noteTitle)`（原版 handnote，可点开）✅。但两条路径仍会产生坏便条：① `ChemCatalog.Specs` 里 `S("note_chem_dealer", …, "tool")` 仍注册（ChemCatalog 约 L117），`Create` 无条件 `SetGameItemType(CHEMICAL_SUPPLIES)`（约 L270）→ 任何走 `ModHelper.SpawnItem("note_chem_dealer")`/`DirectoryMaster.Item` 的路径都得到化学品类且无打开钩子；② `ChemItemIds` 数字 ID #97 = `note_chem_dealer`，玩家/调试用 F10 生成 #97 会得到坏便条。**建议（二选一）**：从 Specs 删除该 id 并把 #97 指向原版 `handnote`；或在 `ModEntry.BuildItem` 对 `note_chem_dealer` 特判改用 `PreBuiltItemHelper.CreateHandnote`（与主礼包一致）。

### P0-6 试剂瓶倒不进原版容器
- **问题**：两个叠加原因——①旧存档 `SaveManager.DecodeNodesInternal` 的 `set_state(saved.itemState)` 会覆盖工厂重写的 `LIQUID_CONTAINER_TAG`/液体组分 tag；②旧物品可能连 `onThisTargetItemFunc` 都未重挂，交互不触发。现有 `ChemPatches` 已有 `TransferLiquid` Prefix（P6a），但缺 `GameItem.Target` 钩子重挂、反向兜底、源体积扣减完整闭环。
- **涉及文件**：`src/ChemPatches.cs`（TransferLiquid Prefix，约 303-360）、`src/ChemCatalog.cs`（`TPourVolumeMl="fc_volume_ml"`、BridgeVanillaChemData/BridgeLiquidData）、`src/ModEntry.cs`（OnGameLoaded 重桥接）。
- **状态**：**部分修复**（Prefix 存在；体积 tag 常量存在）。缺失：`GameItem.Target` Prefix（缺 tag 时 Bridge 后重挂钩子）、`OnGameLoadedNormal/Late` 全量重桥接调用、反向（原版→旧 mod 瓶）桥接、空瓶策略。
- **验证**：旧存档取一瓶本模组药剂 → 倒入 `water_pitcher` 成功、源瓶体积减少（`fc_volume_ml`/液体 tag 同步）、目标容量不超；日志 `[pour]`；新档同样成功；空瓶去向符合设计。
- **【code-core 复核】** **P6a 已把清单里缺的东西补上**：`ChemTargetBridgePatch`（`[HarmonyPatch(typeof(GameItem),"Target")]` Prefix，缺容器 tag 时先 `BridgeVanillaChemData` 再放行）已存在；`ModEntry.OnGameLoadedRehydrate` 已在 `OnGameLoadedNormal/Late` 与 `OnShutterOpenedEarly` 调 `ChemCatalog.RehydrateLoadedItems`；`ChemPourFallbackPatch` 主路径用 `WaterHelper.RemoveAndReturn` + `WaterFeatureHelper.OnLiquidContainerPourInto`，兜底路径用 `PourParts + fc_volume_ml`；`GameItem.Target(GameItem)` / `WaterHelper.TransferLiquid(GameItem,GameItem)` / `RemoveAndReturn` / `OnLiquidContainerPourInto` 均已在程序集中核对存在。**残留缺陷见 NEW-P1-1（体积记账错误）与 P1-8（空瓶策略）**。反向（原版容器→本模组瓶）不做，属产品决策（本模组瓶不是原版液体容器，无必要）。

### P0-7 分析仪不显示成分/总液量
- **问题**：P1a/P3b 的 `RefreshUI` Postfix 只在"原版文本为空"时写/追加，且依赖物品的 Liquid 组分 tag；旧档 tag 丢失或自定义组分时成分/总量为空/不全。
- **涉及文件**：`src/ChemPatches.cs`（RefreshChemMode/RefreshUI Postfix，约 24-190）、`src/ChemCatalog.cs`（ComponentParts/Formula/PurityCondition）。
- **状态**：**部分修复**（task-66 C 进行中：改为对本模组药剂始终覆盖 tested/label/contaminant/totalVolume，不依赖 Liquid tag）。
- **建议**：Postfix 直接覆盖四行 TMP（字段全 public）：纯度+七档、成分（ComponentParts+化学式）、总液量（液体 500ml；固/气「单件」）、毒性/欣快由现有 fc_* Postfix 图标化；先清空再写避免重复行。
- **验证**：任意本模组药剂（含 `fc_chlorine_gas`）扫描后：成分行/总液量/化学式/档位均非空，无重复行；中英文切换正常。
- **【code-core 复核】** **已实现（待实机）**：`ChemScannerModInfoPatch`（RefreshUI Postfix）现在对 `ChemCatalog.IsOurReagent` 的物品**无条件覆盖** `testedTMP.text`（化学式·成分·纯度）、`contaminantTMP.text`（档位 + 毒性/欣快）、`labelTMP.text`（档位）、`totalVolumeTMP.text`（液体 500 ml / 固气单件）；`composeComponents` 用 `ChemRecipes.ComponentParts`（含自定义 `fc_chlorine_gas`→「氯气」显示名）不再依赖 Liquid tag。字段 `currentScannedItem/testedTMP/labelTMP/contaminantTMP/totalVolumeTMP` 已在程序集中核对为 public 字段。**残留风险 P2-12**（`lastSignature`/`writing` 为静态字段，跨物品/重开面板可能跳过重写导致空白）；**待实机**：中英文、重复打开、旧档物品。

---

## 2. P1 问题（逐条）

### P1-1 索引书正文越界
- **问题**：`FreeChem/Books/chem_index.json` 画布 628×804，正文 `x=60,w=600` → 右边 660 超出 628，跨书脊/被裁。
- **涉及文件**：`FreeChem/Books/chem_index.json`。
- **状态**：**未修复**。修复：双列 `x=24/330, w=266, y=60, h=650, fontSize=14, wrap+autoHeight`；或单列仅左页 `x=24,w=266`；每列 ≤30 行。
- **验证**：`grep -n '"x": 60' chem_index.json` 应为 0；游戏内翻页无裁切/无 "overflow" 警告。
- **【code-core 复核】** **已修复**：当前 `chem_index.json` 为 628×804、**4 页**（IDs 1-25 / 26-49 / 50-73 / 74-97），正文元素 `x=24,w=266`、`fontSize=14, wrap, autoHeight`；脚本核对全部元素 `x+w ≤ 628`，无越界。附带更正：原描述「6 页覆盖 1–96」已过期（现覆盖 1–97）。待实机确认无裁切/跨书脊。

### P1-2 描述固定档位词与动态纯度冲突
- **问题**：Localization 的 `can_*_medical/ultrapure_name`（医用/化学纯）与 `sol_caffeine_desc`（"医药级纯度"）写死档位，而实际纯度/档位动态（打印机可改）→ 97% 时仍显示"化学纯"。
- **涉及文件**：`FreeChem/Localization/zh.csv`、`en.csv`（legacy SKU 名称保留合理，但 mod 产线物品的 desc 不应写档位）。
- **状态**：**部分修复**（SKU 定为 legacy；`sol_caffeine_desc` 等仍在）。修复：物品 `_name/_desc` 只写物质名/用途；档位一律由 `chem_profile`/扫描仪/ItemCondition 动态显示。
- **验证**：validate 对 `_name/_desc` 的档位词断言；游戏内 97% 咖啡因提示框不出现"医药级"字样。
- **【code-core 复核】** **仍部分**：`sol_caffeine_desc` 仍为「咖啡豆糊经酒精热萃取所得，**医药级纯度**，是高价值的中枢兴奋剂。」与实际动态纯度冲突；`can_oxygen_medical_name=医用氧气` / `can_oxygen_ultrapure_name=化学纯氧` 作为 legacy SKU 名称可接受（这些 SKU 已不在产线产出）。建议 docs-i18n 改 `sol_caffeine_desc`（去掉「医药级纯度」）。

### P1-3 成分仪毒性/欣快图标与模组数值不一致
- **问题**：原版由 Liquid 组分算毒性/欣快；本模组用 `fc_tox/fc_euph`。
- **涉及文件**：`src/ChemPatches.cs`（IsLethal/IsToxic/GetLethalDose/GetEuphoriaLevel Postfix，约 199-260）。
- **状态**：**修复**（4 个 Postfix 已存在）。
- **验证**：`grep -c "HarmonyPatch(typeof(ChemicalFeedbackHelper)" src/ChemPatches.cs` ≥4；游戏内扫描高 fc_tox 物品，毒性图标/等级与数值一致。
- **【code-core 复核】** **已修复（待实机）**：四个补丁 `ChemIsLethalPatch` / `ChemIsToxicPatch` / `ChemLethalDosePatch` / `ChemEuphoriaPatch` 均存在；目标方法 `ChemicalFeedbackHelper.IsLethal/IsToxic/GetLethalDose/GetEuphoriaLevel` 已在程序集元数据中核对为 `public static`。语义上目前是**单向补齐**（只在原版结果为 false/0 时改为我们的值，见 P2-15），若原版因注入组分算出 true，不会降级为我们的「安全」——建议后续双向覆盖前先实机看图标表现。

### P1-4 机器便签无用途文本
- **问题**：机器窗口 note 槽为空/无文本。
- **涉及文件**：`src/ChemMachines.cs`（NotePanel + CreateHandnote，约 250-295）。
- **状态**：**修复**（每台机器 `MachineHelper.NotePanel(note)` + `PreBuiltItemHelper.CreateHandnote(desc,name)` 放入；随物品存档）。已知限制：语言切换需重建。
- **验证**：打开任一机器窗口，note 面板可点开 handnote 并显示用途；存档重载后仍在。
- **【code-core 复核】** **已实现（待实机）**：`WriteNoteHandnote` 在 `CreateHeatingPlatform` 与 `Create` 中均调用，`MachineHelper.NotePanel(note)` 有 try/catch 回退普通面板；`machine_note_<id>` 缺键时回退 `<id>_desc`，不显示裸键 ✅。已知限制（语言切换后便签文本不刷新）成立，属可接受。待验证：读档后便签文本仍在（handnote 内容随物品 state 存档）。

### P1-5 13 台机器窗口槽位/专用槽/输出满/电力未逐台核对
- **问题**：机器 LayoutOf 输入槽数、FilterSlot/ContainerSlot 专用槽、输出尺寸、电池面板、气瓶槽（加热台 2 槽）与 resolver 需求可能不一致（专用槽被普通投料、输出满未拦 Finish、电机器无电池面板等）。
- **涉及文件**：`src/ChemMachines.cs`（LayoutOf/BuildWindow/RunMachineCycle/CanAcceptOutputs/CollectGasSupply）。
- **状态**：**部分修复**（气瓶槽、note 面板、输出满检查 `CanAcceptOutputs` 已存在）。待逐台按 `docs/phone-pour-analyzer-research.md` §5 清单核对。
- **验证**：逐台：窗口槽位数=LayoutOf；filter/container 槽只收对应物品；电池面板仅 Powered 机器出现且有电才能运行；输出满时 Finish 被拦并保留输入；气瓶槽只收 `can_*` 且反查 `gas_*`；重开窗口/读档后循环状态正常。
- **【code-core 复核】** **本次已完成逐台静态核对，见 §7。** 结论：13 台的输入槽数 ≥ 对应配方最大输入数、输出格 ≥ 最大产物数、电池面板与 `Powered` 一致、过滤/空瓶专用槽只收对应物品、`CanAcceptOutputs` 会在开产前拦输出满（含消耗气瓶所需空瓶位）。仍存在：**空钢瓶/空大桶可被任意设备接受并在 Finish 无条件销毁（NEW-P1-6）**、**灌装台气瓶槽永远无效（P2-5）**、**Defs.InputSlots 与 LayoutOf 双份定义（P2-6）**。

### P1-6 氯气显示为「氯」而非「氯气」
- **问题**：原版 `Liquid "chlorine"` 显示「氯」，与净水片同源；本模组 `gas_chlorine` 若用该组分则分析仪显示「氯」。
- **涉及文件**：`src/ChemCatalog.cs:481` 附近自定义 Liquid `fc_chlorine_gas`（displayName「氯气」）注册逻辑。
- **状态**：**修复**（代码存在自定义组分注册；需确认只在 CHEM_ITEM 气体上使用、不污染水纯度）。
- **验证**：扫描 `gas_chlorine` 显示「氯气」；水容器/水纯度不受 `fc_chlorine_gas` 影响（Liquid 列表注入仅运行期、加水纯度前需排除）。
- **【code-core 复核】** **已实现（待实机）**：`EnsureCustomLiquids()` 在 `Liquid.LiquidDict` 注册 `fc_chlorine_gas`（displayName 用 `gas_chlorine_name`，即「氯气」），并 append 到 `Liquid.Liquids`；`ComponentParts["gas_chlorine"]` 只用该自定义组分，`ValidateLogic` 有断言禁止复用原版 `chlorine`。`Liquid.LiquidDict` / `Liquid.Liquids` 均已在程序集核对为 public static。风险 P2-6：全局 `Liquid.Liquids` 注入会不会被原版 UI/纯度计算遍历到，未实机验证。

### P1-7 电话对话按钮文案/挂断
- **问题**：电话 UI `DisplayChoice` 的按钮文本 = `choiceNextDialogue.plainText`（不是 `AddChoice` 的 name）；当前传整页/结果页 dialogue、挂断传 null → 按钮显示长文/空（乱码 `xxlog` 语义），且无「挂断」入口。
- **涉及文件**：`src/ChemPhone.cs`（BuildDialog/NewPage/AddChoice 调用）。
- **状态**：**未修复**（task-66 A 范围）。修复：每个选项用独立 label Dialogue（如 `new Dialogue().SetText("PLAYER", LOr("phone_chem_menu_hangup","挂断"))`），`AddChoice(icon,"","",labelDialogue, action, null)`；挂断 action = `Hangup()`（原版 `end_call` 语义）。
- **验证**：`grep -n "AddChoice" src/ChemPhone.cs` 每个调用第 4 参为 label dialogue 而非页面；游戏内每个按钮短文案可读，每页可挂断，订购结果后可返回/关闭。
- **【code-core 复核】** **已修复（代码层，待实机）**：`BuildDialog` 每个选项第 4 参均为 `NewLabel(...)` 生成的 label Dialogue（`orderLabel`/`stockLabel`/`back`/`backStock`）；`AddHangup` 往每页加「挂断」label + `Hangup()` action（`OnPhoneDialogEnd`+`StopCall`，并兜底 `DialoguePhoneUIManager.EndConversation`）；`currentOrderResultPage` 预计算的目标页也各有返回/挂断。待实机验证按钮文本是否取到 `plainText`（`NewLabel` 用 `Dialogue.SetText` 会写 plainText+text）。

### P1-8 液体倒空后空瓶处理
- **问题**：倒空后销毁 vs 保留空瓶（当前 `fc_volume_ml` 常量存在但空瓶策略未定）。
- **涉及文件**：`src/ChemPatches.cs`（pour Prefix 末尾）、`src/ChemCatalog.cs`。
- **状态**：**延期**（需 lead 决策；建议改为"空试剂瓶"物品/保留并清空 tag，避免玩家丢容器）。
- **验证**：倒空后物品仍存在但体积=0、名称/图标为空瓶；或按决策销毁（日志可追溯）。
- **【code-core 复核】** **仍延期**：主路径 `if (sourceEmpty) sourceContainer.Destroy();`，兜底路径 `if (remainMl - pourMl <= 0) sourceContainer.Destroy();` —— 都是销毁。注意这会让玩家损失容器（一个 500 ml 瓶子的成本可能低于其内容物，但仍是负面体验）；若要保留空瓶，需要新增“空试剂瓶”物品或复用 `gas_canister_empty` 语义，并保证不会与「瓶中液体组分 = 0」的脏状态冲突（分析仪/收购需处理空瓶）。**需 lead 决策**。

### P1-9 打印机 target 钩子在读档后丢失
- **问题**：GameItemFunc 委托不随存档保存；若读档路径未走工厂，打印机会失去 target 钩子。
- **涉及文件**：`src/ModEntry.cs`（RegisterItems → 工厂重建物品）、`src/ChemPrinter.cs`（Bind）。
- **状态**：**修复**（读档 `DirectoryMaster.Item` 走 `ModItemDirectory` 工厂，重新执行 `ChemPrinter.Bind`）。
- **验证**：存档→重载→用打印机点药剂瓶仍能盖章；`grep -n "ChemPrinter.Bind" src/ModEntry.cs` 在工厂分支内。
- **【code-core 复核】** **已实现（待实机）**：`ModEntry.BuildItem` 的 `case "label_printer": ChemPrinter.Bind(item); break;` 在 `RegisterItems` 注册的工厂委托内（物品目录初始化/读档重建物品时都会走到）。`ChemPrinter.Bind` 内部整体 try/catch，单个 delegate 赋值失败只记日志、不影响打印机作为物品存在。**注意**：若 `GameItem` 的委托字段（`mayThisTargetItemFunc` 等）在某个版本改名，`Bind` 会在运行时抛异常被 catch → 打印机静默失效；这些 delegate 已在程序集核对为 public property（`GameInventoryFunc` 上的 `mayActivateSlotItemFunc/onActivateSlotItemFunc` 等）。待实机：存档前后盖章可用。

### P1-10 机器重开/读档后面板/循环状态恢复
- **问题**：关窗再开、读档后 `MachineProgressHelper` 状态与 UI 面板（电池/输入/输出可见性）是否一致未验证。
- **涉及文件**：`src/ChemMachines.cs`（Create/BuildWindow 与存档恢复）。
- **状态**：**部分修复**（工厂重跑 + 物品 state 保存）；缺显式自检日志。
- **验证**：机器运行中→关窗→重开；存档→重载：进度/温度/电池/输入输出保持，面板不空不重复。
- **【code-core 复核】** **仍部分 + 新增加热台问题**：夜间机器侧：工厂在 `OnModItemDirectoryInit` 注册、物品 state 存档恢复，`onCycleEndSlotItemFunc` 每周期重挂 → 面板结构应随物品内容窗恢复（待实机）。**加热台侧有明确问题（见 NEW-P1-4）**：`ChemHeating.Rigs` 只在 `OnDisable` 清空，读档/重开店铺不清理 → 旧 Rig 残留 + 工厂重建后新 Rig 叠加（同一机器会被 tick 两次，可能双倍产出/双倍耗燃料）；且 `Rig.Temp` 初始化固定 `ChemLab.RoomTemp`，不读回物品 tag `fc_temp_x100` → 读档后温度归零（物品 tag 却是旧温度）。另外 `Rig.HeldSeconds`/`ActiveReaction` 不落盘，进度会重置（可接受，但应在文档写明）。

---

## 3. QA 逐条核对方法（静态 + 动态）

**静态（可自动断言）**：
1. `grep -n "netWorthByDay" src/` 应为 0（P0-3）→ **为 0 ✅**。
2. `grep -n "GetLocalizedDisplayName" src/` 有 Harmony 补丁类（P0-1）→ **已有（ChemPhone.cs 内 `ChemPhoneDisplayNamePatch`）✅**（最终 161,280 B 复核）。
3. `grep -n "PhoneState.Regular" src/ChemPhone.cs` 与 `dialedBefore = true` 存在（P0-2）→ **存在 ✅，但额外断言 `grep -n "PhoneState.Responding" src/ChemPhone.cs` 必须为 0（当前 `EnsureQuickDial` 仍有命中 ❌，见 NEW-P0-1，最终 161,280 B 复核仍未修复）**。
4. `grep -n "RunStats" src/ChemPhone.cs` 应为 0（P0-3）→ **为 0 ✅**（改用 `StoreStation.GetDayCounter`）。
5. `grep -n "CreateHandnote" src/` 便条路径使用原版 handnote；`note_chem_dealer` 不再走 CHEMICAL_SUPPLIES 分支（P0-5）→ **CreateHandnote 有 ✅，但 Spec 仍注册且 Create 仍无条件设 CHEMICAL_SUPPLIES ❌（主路径已修、工厂/调试路径仍部分）**。
6. `grep -n "GameItem.Target" src/ChemPatches.cs` 存在重挂钩子 Prefix（P0-6）→ **存在 ✅**。
7. `grep -n "contaminantTMP.text =" src/ChemPatches.cs` 为直接覆盖（P0-7）→ **存在 ✅**。
8. `grep -n "RefreshUI" src/ChemPatches.cs` 只能有一个"写 TMP"的 Postfix，且不调用 RefreshUI（P0-7 防重复行/防递归）→ **一个 `ChemScannerModInfoPatch`；`ChemScannerLegacyBridgePatch` 在 RefreshChemMode 里 bridge 后调一次 RefreshUI，有 `bridging` 重入保护 ✅**。
9. `grep -n '"x": 60' FreeChem/Books/chem_index.json` 应为 0（P1-1）→ **0 ✅**。
10. `grep -n "AddChoice" src/ChemPhone.cs` 第 4 参（choiceNextDialogue）为 label dialogue（P1-7）→ **是 ✅**。
11. `grep -c "HarmonyPatch(typeof(ChemicalFeedbackHelper)" src/ChemPatches.cs` ≥ 4（P1-3）→ **=4 ✅**。
12. `grep -n "NotePanel" src/ChemMachines.cs` 每台机器窗口含 note 面板（P1-4）→ **存在 ✅**。
13. **新增（NEW-P1-1）**：`grep -n "SetPourVolumeMl(sourceContainer" src/ChemPatches.cs` 的实参应为「剩余量」，当前是 `move / PourPartPerMl`（倒出量）→ **最终 161,280 B 复核：仍未修复 ❌**。
14. **新增（NEW-P0-1）**：`grep -n "PhoneState.Responding" src/ChemPhone.cs` 应为 0 → **最终 161,280 B 复核：仍有 1 处命中（`EnsureQuickDial` L128）❌，需修**。
15. **新增（数据一致性）**：所有 `Specs ∪ MachineIds` 的 `_name/_desc/_flavor` 齐备；`loot id ⊆ Specs`；`reaction inputs/outputs ⊆ Specs ∪ VanillaAnalog`；机器 `_on` 图标一一对应。本复核实跑：全部通过（Specs 84 / 图标 110 / 本地化 440 键 / 掉落 40 id / 反应 44 条；脚本见交付说明）。
16. **最终门禁（161,280 B 复核实跑）**：build **0 警告 0 错误**；`logic_check` **44 反应 / 763 断言全通过**；`logic-harness` **全部通过**；`validate` **77 PASS / 1 WARN / 0 FAIL**。

**动态（实机，未验证项）**：P0-1/2/3/4 电话姓名/电话簿/跨天/连点；P0-5 便条点击；P0-6 旧档倒液；P0-7 扫描显示；P1-5 逐台机器；P1-7 电话按钮/挂断；P1-10 机器重载；NEW-P0-1 快捷拨号；NEW-P1-1 部分倒液后的体积；NEW-P1-4 读档后加热台温度/双 tick。

## 4. 备注

- 状态口径已按 Lead 指示统一：原清单 7 个 P0 / 10 个 P1 由 features task-66 修复 → 标「**已修复（build 161,280 B / qa PASS）**」；P1-8 标「**延期：按 Lead 决策销毁并日志**」。逐条最终状态见 §0.1 末列与 §0.2。
- **仍未关闭的 code-core 新增项（详见 §5 / §10.1）**：NEW-P0-1（快捷拨号回归，抵消 P0-2）、NEW-P1-1（倒液体积记账）、NEW-P1-2/3/4（加热台产物继承/燃料守恒/Rig+读档温度）、NEW-P1-5（非液体纯度双轨）、NEW-P1-6（空容器误投销毁）、NEW-P1-7（工厂产液体不主动桥接）、NEW-P1-8（二次开档王尔德，待实机）。
- 已实跑门禁（161,280 B 快照）：build 0 警告 0 错误；logic_check 44 反应 / 763 断言全通过；logic-harness 全部通过；validate 77 PASS / 1 WARN / 0 FAIL。
- **静态通过/编译通过 ≠ 游戏内验证**：电话姓名/电话簿/快捷拨号/跨天/连点、旧档倒液、分析仪四行显示、机器逐台与读档状态、handnote 打开、Harmony 补丁实际生效均**未实机验证**。

---

## 5. code-core 新增问题（本轮全代码体系审查）

> 全部为**静态审查结论**（含 IL/元数据证据），均未实机验证。P0/P1 判定沿用本文件口径：**P0=阻断用户核心流程，P1=逻辑/数据/资源缺陷，P2=风险/卫生/性能**。

### NEW-P0-1 快捷拨号被 `EnsureQuickDial()` 反向破坏（回归）
- **证据**：`src/ChemPhone.cs` 的 `ApplyClientFields`（约 L81-96）已正确设 `phoneState=Regular; dialedBefore=true`；但 `EnsureQuickDial()`（约 L117-136）在 `EnsureRegistered()` 之后又把 `client.phoneState = StorePhoneClient.PhoneState.Responding`（L128）。`ModEntry` 在新档（L425）、读档重桥接（L512）、开店（L523）三处都调用 `EnsureQuickDial()`。
- **IL 证据**：`StorePhoneClient.ShownInPhoneBook()` = `(phoneState==Cooldown(5) || phoneState==Regular(4)) && dialedBefore`（IL_0006 `ldc.i4.5`、IL_000F `ldc.i4.4`）；`PhoneBookUIManager.OpenUI()` 只对 `ShownInPhoneBook()==true` 的字典项 `Instantiate(contactElementPrefab)`。→ `Responding(2)` 永远不会进电话簿/快捷拨号。
- **后果**：即使 P0-2 的字段修好了，`EnsureQuickDial` 也会把联系人踢出列表；三处调用让玩家任何一次读档/开店后都看不到快捷拨号。**这正是用户反馈「未进快捷拨号」的直接代码原因之一。**
- **建议修复**：删除 `EnsureQuickDial` 里对 `phoneState` 的覆写（或改为 `Regular`），其余逻辑（EnsureRegistered + 日志）保留；同时把方法注释里「phoneState=Responding 就会出现在快捷拨号」的错误说法删掉。修完后 QA 断言 `grep -n "PhoneState.Responding" src/ChemPhone.cs` = 0。
- **状态**：未修复（发现于 2026-11-10 14:30 快照）。

### NEW-P1-1 倒液主路径的剩余体积写错（体积不守恒/可重复倒出）
- **证据**：`src/ChemPatches.cs` `ChemPourFallbackPatch.Prefix` 主路径（约 L374-389）：
  `int srcPart = WaterHelper.GetTotalVolume(sourceContainer); int move = Math.Min(free, srcPart); var parts = WaterHelper.RemoveAndReturn(sourceContainer, move); … ChemCatalog.SetPourVolumeMl(sourceContainer, move / ChemRecipes.PourPartPerMl);`
  → 写入 `fc_volume_ml` 的是**本次倒出的 ml**，不是**剩余 ml**（应为 `(srcPart - move)/PourPartPerMl`）。
- **后果**：部分倒出（目标容器容量小）后源瓶 `fc_volume_ml` 变小/归零：① 提示/兜底路径按错误体积限流；② 源瓶之后若失去原版 Liquid 容器 tag（读档覆盖、克隆、销毁重建），兜底路径会按 `fc_volume_ml` 再倒一次 → **可重复倒出多于实际剩余的体积（资源复制漏洞）**；③ `P1-8` 的空瓶判定同样依赖该 tag。
- **建议修复**：改为 `SetPourVolumeMl(sourceContainer, Math.Max(0, (srcPart - move) / ChemRecipes.PourPartPerMl));`（并考虑 `move` 不整除 1000 时的取整偏差：以 part 为单位记账更稳，如新增 `fc_volume_part`，或在扣减时按 `move` 与剩余 part 同步）。
- **状态**：未修复。

### NEW-P1-2 加热台热反应产物不继承输入状态（「蒸馏提纯」实际提纯不了）
- **证据**：`src/ChemHeating.cs` `Produce`（约 L254-271）对每个产物 `ChemCatalog.EnsureOwned(DirectoryMaster.Item(id))` 后直接 `GraphUtils.TryAcceptAll(r.Output, produced)` —— 产物是**工厂默认物品**（液体默认带 `ChemLab.InitialContaminants()`、提纯等级 0、fc_purity 84-85%）。而 `ChemMachines.Finish` 对滗析/过滤有专门的污染物/提纯等级传递（约 L770-800），热反应没有。
- **后果**：`heat_hcl_distill`（输入=输出=盐酸，设计为「78–110°C 蒸馏提纯」）产出一瓶**新的初始纯度盐酸**：已过滤到 99% 的酸投进去，出来变回 84%；其它热反应产出也忽略输入纯度/污染物（对 `heat_peroxide`/`heat_kmno4` 这类产气反应影响小，但同样不精确）。
- **建议修复**：在 `Produce` 加一个「状态继承」分支：对 `id == reaction.Outputs[0] && id` 与某输入同种（如 heat_hcl_distill）时，把主输入（vessel 里第一个匹配的液体）的 `ReadContaminants`/`ReadPurifyLevel` 传到产物（可按反应等级轻度提升，例如 `Decant` 或 `Filter` 一次），再 `ApplyContaminants` + `SyncVanillaPurity`；至少不应低于输入纯度。
- **状态**：未修复。

### NEW-P1-3 加热台加注燃料一次销毁整瓶（资源浪费）
- **证据**：`src/ChemHeating.cs` `Tick`（约 L88-91）在灯内 <20 ml 时调用 `Refill(heater, fuel)`；`Refill`（约 L319-334）`amount = round(500 × purity/100)`，`after = min(500, before+amount)`，然后 `fuel.Destroy()` —— 不管实际只倒进去多少（如灯内还有 490 ml，只补 10 ml），整瓶 500 ml 燃料被销毁，剩余 340 ml 凭空消失。
- **后果**：玩家每次补燃料都浪费大半瓶乙醇/原版酒精；燃料是高纯度酒精时更亏。与项目「物理守恒」口径不一致（气瓶都有守恒回收逻辑）。
- **建议修复**：`Refill` 按实际注入量扣减燃料瓶：优先用已有的 `fc_volume_ml` 记录燃料瓶剩余（液体药剂都带该 tag），`need = cap - before`，`useMl = min(remain, ceil(need / purity))`，只消耗对应比例并 `SetPourVolumeMl(fuel, remain - useMl)`，空了才 `Destroy()`；日志写清「+X ml @ Y%，剩余 Z ml」。
- **状态**：未修复。

### NEW-P1-4 加热台 Rig 泄漏/重复 tick + 读档温度与进度丢失
- **证据**：`src/ChemHeating.cs`：`Register`（约 L70-77）只 `Rigs.Add(r)`，**没有对应注销**；`Stop()`（约 L50-56）只在 `ModEntry.OnDisable` 调用时 `Rigs.Clear()`（模组禁用才触发，读档/重开店铺不触发）。`Rig` 初始化 `Temp = ChemLab.RoomTemp`（L29），`Register` 不读回物品 tag `ChemCatalog.TTemp`；`HeldSeconds`/`ActiveReaction`/`High` 均不落盘。
- **后果**：① 读档后 `ModItemDirectory` 工厂重建加热台 → `Register` 再 Add 一台新 Rig，旧 Rig 仍指向旧物品（被销毁/被替换）→ 同一台机器可能被 tick 两次（燃料双倍消耗、反应双倍产出、输出双倍），或对已销毁物品读写 tag 抛异常（`TickLoop` 的 try/catch 会吞掉但该秒所有 Rig 停摆）；② 玩家读档后发现温度回到 26°C（物品 tag 里却是旧温度，UI/机制不一致），进度秒数清零。
- **建议修复**：① `Rig` 增加存活检查，`TickLoop` 每 tick 先 `if (!GeneralHelper.IsItemOwned(r.Machine))` / `r.Machine == null` 则 `Rigs.Remove(r)`；② 在 `ModEntry.OnGameLoaded`/`OnShutterOpenedEarly` 前清空 `Rigs`（或提供 `ChemHeating.ResetAll()`）；③ `Register` 从 `ChemCatalog.ReadInt(machine, TTemp, …)` 恢复温度，`High` 可从物品 tag 落盘（如 `fc_heater_high`）。
- **状态**：未修复。

### NEW-P1-5 非液体药剂的 `fc_purity` 恒为 94（与 TGrade / 原版纯度双轨）
- **证据**：`src/ChemCatalog.cs` `WriteState`（约 L970-993）：`isLiquidReagent=false` 时 `contam = new int[8]`（全 0），然后 `TPurity = ChemLab.PurityFrom(contam)/Scale`；而 `PurityFrom` 有硬上限 `MaxPurity(0)=9440`（10000−70×8）→ 非液体（气体/固体/钢瓶/大桶/违禁品）`fc_purity` 恒 = 94。同一物品 `TGrade = spec.Grade`（气体/固体为 Industrial=0），Create 里原版桥接纯度却用 `spec.Purity`（多数 70）→ **三处纯度不一致**。
- **后果**：① 提示框 `ChemRuntime.ProfileLine` 显示所有气体/固体纯度 94%（应为设计 70%）；② 机器气瓶槽 `ChemCatalog.CanisterGrade()` 读 `fc_purity` 94 → `GradeFromPurity(9400)=Medical` → **灌装台产出的「工业钢瓶」被 `UpgradePurity(Medical)` 写成 95% 医用纯度**（尽管 SKU 是工业）；③ 直接投气体物品时 `inputGrade` 读的是 `TGrade`（工业 0）→ 同一配方因「气体从气瓶槽来还是从投料槽来」得到不同纯度，产生非预期套利；④ 销售/评估以原版 ItemFeature 为准的话，与 fc 值不一致，玩家会看到「工业钢瓶卖医用价」。
- **建议修复**：`WriteState` 对非液体药剂直接写 `spec.Purity`（不要走污染物反推）；或让 `PurityFrom` 对「无污染物」的非液体返回 100 但由调用方用 `spec.Purity` 覆盖。同时统一 `inputGrade` 来源（建议都读 `fc_purity` → `GradeFromPurity`，或都读 `TGrade`），避免两条路径分叉。
- **状态**：未修复。

### NEW-P1-6 空钢瓶/空大桶可被任意设备接受并销毁
- **证据**：`src/ChemMachines.cs` `AcceptsInput`（约 L134-140）对 `gas_canister_empty`、`gas_drum_empty`、`catalyst_kit` 一律 `return true`（不区分机器）；`RunMachineCycle` 把它们加进 `ids`（L503-509）；`Finish`（约 L742-743）对 `gas_canister_empty`/`gas_drum_empty` 无条件 `Destroy()`。灌装台 resolver（约 L97-104）在「没有气体源 / 钢瓶和大桶同时在 → `hasCanister == hasDrum`」时返回 `sludge`。
- **后果**：① 把空钢瓶/空大桶误投进滗析槽/反应釜/过滤台 → 该设备正常跑完周期后容器被销毁（玩家损失 20-55 价值容器，且无提示）；② 灌装台同时放入钢瓶+大桶（或只放其中之一但气体不匹配）→ 产出废渣，**两个空容器都被销毁**。
- **建议修复**：`AcceptsInput` 只允许 `canister_filling` 接受空钢瓶/空大桶（其它机器返回 false）；`Finish` 的容器销毁也只在 `def.Id == "canister_filling"` 时执行，其余保留/退回输出。
- **状态**：未修复。

### NEW-P1-7 工厂产出的液体药剂不主动桥接（核心交互依赖 Harmony 兜底）
- **证据**：`src/ChemCatalog.cs` `Create`（约 L256-306）只对已有 `LIQUID_CONTAINER_TAG` 的物品调 `SelfCheckLiquidBridge`（L296→L312 内 `if (!IsTag(LIQUID_CONTAINER_TAG)) return;`）；**新建液体药剂不会得到原版液体容器 tag/组分**，`EnsureLiquidBridge`（L704）只有旧档 `RehydrateLoadedItems`（L846 经 `BridgeVanillaChemData`）与 `ChemTargetBridgePatch`（`GameItem.Target` Prefix）会调用。`grep EnsureLiquidBridge` 在 `ChemMachines.Finish`/`ChemHeating.Produce` 的产线路径中没有调用点。
- **后果**：所有产线新产出的液体药剂（滗析/过滤/反应产物）都依赖 `GameItem.Target` 补丁在交互瞬间补桥接；一旦该补丁因签名变更/加载顺序失效（Harmony 静默失败），新物品就**倒不进原版容器、原版分析仪/收购识别不到**（用户最高优先级问题会复发）。当前 P6a 的 `ChemPourFallbackPatch` 对缺 tag 源有兜底（能倒），但源容器不是原版液体容器 → 原版「选中源→点目标」的交互是否触发仍取决于 GameItem.Target 补丁。
- **建议修复**：在 `Create` 中直接对液体药剂调用 `EnsureLiquidBridge(item)`（该函数幂等、会保存/恢复 name/desc/flavor/value/sprite），把「新物品」与「旧档修复」统一走一条路径；Harmony 补丁只作为极端兜底。注意 `Create` 内调用后不要再覆盖物品名称/类型（现有顺序可满足）。
- **状态**：未修复（P6a 有意用补丁兜底，但从「减少对未文档化补丁依赖」角度看应前移）。

### NEW-P1-8 旧存档二次开档时王尔德卡片可能不重注册（需实机验证）
- **证据**：`src/ModEntry.cs`：`networkRegistered` 只在 `OnDisable` 重置（L561），`TryRegisterWildNetwork`（L237-255）开头 `if (networkRegistered) return;`，注册成功后置 true（L248）；调用点是 `OnGameLoaded`（L359）。会话内若「返回主菜单 → 新开档/加载另一存档」，`OnDisable` 不会触发，`networkRegistered` 仍为 true → **不会重跑 `ModHelper.AddNetworkUpgrade`**。
- **后果**：如果 `NetworkUpgrade` 的注册是**每次 PlayerStore 新建都需要的**（`AddNetworkUpgrade` 内部会访问 PlayerStore），第二局起王尔德 4 张卡（气体商人/气体订单/排风/进阶排风）可能缺失或不可解锁；如果注册是全局静态且跨局保留，则无问题。**必须实机验证：返回主菜单→新开档→查看王尔德 free_chem 标签页。**
- **建议修复**：在 `OnNewGameStarted`/`OnGameLoaded` 里把 `networkRegistered` 重置（或检测玩家存档切换）后重试；或改为以「本次 PlayerStore 实例」为键的幂等注册（例如记录一个 `PlayerStore` 引用，不同实例就重注册）。
- **状态**：待验证（若实机确认缺卡则升 P0）。

### P2（风险/一致性/卫生，逐条）
1. **P2-1** `ChemPhone.DeliverPendingOrder`：`ModHelper.SpawnItem(id)` 返回 null 时 `failed++` 但不 `keepPending`（约 L411-420），最终 `ClearPending()` → 玩家已付款但该件丢失（`OrderPack` 若某 id 失效/被改动会静默少货）。建议：`it == null` 也 `keepPending = true`（下次重试），或部分退款。
2. **P2-2** `ChemPhone`：订购结果页在 `BuildDialog` 时用 `CurrentOrderResultPage` 预计算（约 L209），而实际结果在点击时由 `TryPlaceOrder` 决定；若现金/日期在对话期间变化，页面可能显示「订购成功」但实际失败（或反之）。建议 action 内动态 `ShowPage(resultPage)`，或把结果统一为一条由 action 结束后打开的提示。
3. **P2-3** `ChemRecipes.Match`：温度门槛只对 `r.Temp >= 2` 生效（`if (r.Temp >= 2 && r.Temp != temp) continue;`），即 `temp:0/1` 的配方（如 `hypochlorite` 0、`hcl_absorption` 0、`slaked_lime` 默认 1、`soda_from_caustic` 默认 1）在**任何温度档**都能匹配，与 `ModEntry` 注释「只有 ChemRuntime.Temp == 配方 Temp 时才匹配」不一致。建议：改成 `r.Temp >= 2 || r.Temp <= 1` 的显式档位（或文档写清 0/1 为「不限/常温即可」语义）。
4. **P2-4** 温度/pH/天平/防护为**全局单值**（`ChemRuntime.Temp/Ph/Balanced/Protected`）→ 所有机器共享同一温度档，玩家做多步链要反复点温度计；这是设计取舍，但建议 UI 上在机器窗口 note/UI 标签提示当前档位（否则玩家不知道失败原因是温度）。
5. **P2-5** `canister_filling` 也创建 2 个气瓶槽（`BuildWindow` 对所有机器统一 `gasSlots`，约 L881-886 + `Create` L369），但 `SupportsGasSupply` 排除 filling（L576）→ 该机器气瓶槽**永远无效**（放进去的物品不参与解析），属 UI 误导。建议灌装台不显示气瓶槽。
6. **P2-6** 两处「全局注入」风险：① `ChemCatalog.EnsureCustomLiquids()` 把 `fc_chlorine_gas` append 到 `Liquid.Liquids`（全局列表，原版 UI/纯度计算可能遍历）；② `ChemRuntime.CommitVanillaEndangerment` 用未文档化 `PlayerStore.Instance.secData.CommitCrime(SecData.ENDANGERMENT, n)`（ChemRuntime L155-176，默认开启 L52）。两者都应实机验证副作用；建议安全部接入默认关闭或首次使用时弹提示。
7. **P2-7** `ChemMachines.Defs` 构造函数（约 L33-40）仍设置 `InputSlots = (canister_filling||filtration_bench) ? 3 : 4`，而实际窗口用 `LayoutOf` 的 1/2/3 —— 双份定义且不一致（若 QA/文档引用 `Defs.InputSlots` 会得到错误值）。建议 Defs 只保留 Id/Power/Resolve，InputSlots 以 LayoutOf 为唯一来源。
8. **P2-8** `ChemCatalog` 中 `TCompTox="fc_tox"`、`TCompEuph="fc_euph"` 与 `TTox/TEuph` **同名字符串**（L32-33 vs L51-55）：`WriteState` 先写 Profile 值，随后 `ApplyComponentComposition` 覆盖为「由原版 Liquid 组分计算的值」→ `ChemRecipes.Profiles` 表实际只影响 `ChemLab.ExhaustUnitsForGas` 与 ValidateLogic，tooltip/图标读的是组分值，两套数据源可能漂移；`TCompAddict("fc_addict")` 写入后**无人读取**（死 tag）。建议只保留一套（推荐组分值为准，Profile 只留 toxicity 分级用于排气计数并注释说明）。
9. **P2-9** 纯度七档阈值有 **4 份硬编码**：`ChemLab.GradeStarch…GradeLabPure`、`ChemCatalog.PurityCondition`、`ChemPrinter.GradeIndexForPurity`、`ChemRecipes.GradePurity/GradeMultiplier`。任何一处调整都会造成显示/价值不一致。建议收敛到 ChemLab 的常量为唯一来源。
10. **P2-10** `ChemHeating.Produce` 对输出**没有 CanAccept 预检**（约 L254-260）：输出格满时 `GraphUtils.TryAcceptAll` 失败，产物直接丢失（`ChemMachines` 有 `CanAcceptOutputs` 预检并拦在开产前，热反应没有）。建议 `Produce` 先 `GraphUtils.CanAccept`，放不下就不消耗输入并记日志/停下。
11. **P2-11** `ModEntry` 把 `OnGameLoadedRehydrate` 同时注册到 `OnGameLoadedNormal` 与 `OnGameLoadedLate`（L178-179）→ 每次读档全量扫描两遍（`RehydrateLoadedItems` 遍历 Emporium 所有物品 + 递归 `FindAllChildItems`）。且扫描范围只覆盖 `EmporiumEntry.GetAllItems()` + 子物品，**玩家背包/非 Emporium 容器里的旧物品不会被修复**（要靠 `GameItem.Target`/扫描仪补丁兜底）。建议只保留 Late 一次，并考虑把玩家背包纳入（若有公开 API）。
12. **P2-12** `ChemPatches.ChemScannerModInfoPatch` 的 `writing`/`lastSignature` 是**静态字段**（约 L65-66），`lastSignature = item.identifier + "|" + purity`：同一物品同纯度在「重开分析仪/重新扫描」时直接 `return`，不重写 TMP；如果原版重新打开面板时把 TMP 文本清空，玩家会看到空白（需实机）。建议在 `Open`/`RefreshChemMode` 时重置 `lastSignature = null`，或去掉缓存（每 refresh 写一次字符串成本很低）。
13. **P2-13** `ChemItemIds` 头注释「编号 1–85 显式固定顺序」已过期（`MaxId=97`，`Ordered.Length=97`，含 label_printer/note/P2a 化学品）；`ChemDebug.SpawnIndexBook` 注释「6 页覆盖 1–96」也与实际 4 页 1–97 不符。文档卫生，建议同步。
14. **P2-14** `ChemDebug.SpawnIndexBook` 的「全场唯一」检查用 `ChemCatalog.CountIndexBooks` 只扫 `EmporiumEntry` 的 11 个物品栏（约 L395-435），玩家背包/机器内容窗里的索引书不会被发现 → 可重复生成（调试功能，影响小）。
15. **P2-15** `ChemPatches` 的反馈补丁是**单向补齐**（`if (__result) return;`）：原版按注入的 Liquid 组分可能先算出 lethal/toxic/欣快>0，我们的 fc_* 值不会覆盖它；反之我们的值可能高/低于组分值。建议这两套最终统一到一套（见 P2-8），或在补丁里无条件覆盖（需先确认原版 UI 对「lethal+toxic 同时 true」的处理，避免双图标）。
16. **P2-16** 性能：`ChemHeating.TickLoop` 每秒 `Rigs.ToArray()` 分配（约 L64）+ 每 Rig 每 tick 多次 tag 读写（温度写给每个 vessel 物品，L114-117）；`ChemDebug.HotkeyLoop` 每帧轮询（可接受）。当前规模无问题，若未来加机器数量建议缓存/事件化。
17. **P2-17** `ChemMachines.BuildWindow` 的输入面板把「投料槽 + 气瓶槽」平铺成一行（L873-886），`inputs.Count+gasSlots.Count` 最多 1+2+2=5 列 × 50 px = 250 px，窗口宽度若固定可能挤压；`MachineHelper.CreateMachineryWindow(true, panels)` 的布局由原版决定，未实机验证是否溢出/换行。
18. **P2-18** `ChemRuntime.Load()` 读取 `Temp/Ph/Balanced/Protected` 用 3 参 `GetModData*`（带默认值），旧档缺 key 时默认 1/7/false/false ✓；但 `ExhaustRaw` 旧档没有时默认 0 ✓；`ChemCatalog.ReadInt` 对缺失 tag 全部有 fallback ✓。幂等性：`EnsureCustomLiquids`/`EnsureLiquidBridge`/`ApplyComponentComposition`（含 `fc_components_ver` 版本迁移）/`BridgeVanillaChemData` 在设计上幂等；`RehydrateLoadedItems` 幂等但被调用两次（P2-11）。**未实机验证旧档（v0.x）直接升级到 v0.9 的兼容性**。

---

## 6. 逐文件审查结论（13 个文件）

| 文件 | 行数量级 | 结论 |
|---|---|---|
| `ModEntry.cs` | ~570 | 生命周期/钩子基本正确（Init 不碰存档、钩子 OnEnable 订阅/OnDisable 取消、`TryRegisterWildNetwork` 延迟+guard）；网络注册跨局幂等存疑（NEW-P1-8）、重桥接双钩子（P2-11）、`note_chem_dealer` 工厂路径仍坏（P0-5）。 |
| `ChemCatalog.cs` | ~1040 | 工厂/桥接/组分注入主体完整；非液体纯度双轨（NEW-P1-5）、工厂产出液体不主动桥接（NEW-P1-7）、TTox/TEuph 与 TComp* 混用（P2-8）、七档阈值重复（P2-9）。 |
| `ChemRecipes.cs` | ~1040 | 纯数据层，ValidateLogic 覆盖很全（配方注册/相态/温度/桶映射/组分守恒/PourParts 守恒）；温度门槛只拦 temp≥2（P2-3）。`logic-harness` 全通过（44 反应/13 设备/6 热反应/97 数字 ID）。 |
| `ChemMachines.cs` | ~900 | 槽位/电力/气瓶/输出满/双状态图标主体完整（见 §7）；空容器误投销毁（NEW-P1-6）、灌装台气瓶槽无效（P2-5）、Defs/Layout 双份槽位定义（P2-7）。 |
| `ChemHeating.cs` | ~370 | 实时加热公式/燃料/灯芯/温区/降解/图标齐全；产物不继承输入（NEW-P1-2）、燃料整瓶销毁（NEW-P1-3）、Rig 泄漏与读档温度丢失（NEW-P1-4）、输出满不预检（P2-10）。 |
| `ChemLab.cs` | ~494 | 纯函数层完善，ValidateLogic 断言覆盖污染物/纯度上限/加热公式/排风/热反应可达性；无游戏类型依赖 ✓。 |
| `ChemPatches.cs` | ~400 | 6 个补丁目标均已在程序集核对存在；分析仪覆盖/倒液/反馈/收购逻辑主体正确；**倒液体积记账错误（NEW-P1-1）**、分析仪静态缓存（P2-12）、反馈单向补齐（P2-15）。 |
| `ChemPhone.cs` | ~520 | 多页对话/挂断/占位扣款/配送主体已实现；**EnsureQuickDial 回归（NEW-P0-1）**、姓名仍空（P0-1）、派送 null 静默少货（P2-1）、结果页预计算（P2-2）。 |
| `ChemPrinter.cs` | ~320 | 15 模式循环/真实-标签分离/校准 100%/医疗标记完整；`modeIndex` 全局（P2-19：多打印机共享模式）；`Bind` 依赖 GameItem 委托字段（P1-9 已核）。 |
| `ChemDebug.cs` | ~500 | F10 轮询 + F9 入口 + 后柜台生成/满仓提示/索引书逻辑完整；索引书唯一性扫描不全（P2-14）、注释页数过期（P2-13）。 |
| `ChemRuntime.cs` | ~281 | 存档字段/排风/毒性画像/原版 ENDANGERMENT 折算完整（带 HasInstance guard + try/catch + 可配置关闭）；未文档化 API 风险（P2-6）。 |
| `ChemEquipment.cs` | ~60 | 只读取状态拼文案，无副作用；工具类物品 tooltip 已接（thermometer/ph_meter/balance/goggles/lamp/wick/filter）。 |
| `ChemItemIds.cs` | ~253 | 纯数据 1–97 稳定映射 + 全角数字/动词前缀解析 + list 命令；头注释 1–85 过期（P2-13）。 |

## 7. 13 台机器逐台核对（P1-5）

| 机器 | LayoutOf 槽位 | 配方最大输入 | 电/模块 | 专用槽 | 输出 | 结论 |
|---|---|---|---|---|---|---|
| chem_reactor | 3 输入 | 3（wet_sulfuric/voidsalt/redmist） | 有电/有模块 | — | 3×3 | ✅ 槽位足够 |
| electrolyzer | 2 输入 | 1 | 有电/有模块 | — | 3×3 | ✅（模块对该机无配方收益，属继承范例，P2 提示） |
| gas_generator | 2 输入 | 2 | 有电 | — | 3×3 | ✅ |
| condenser_unit | 2 输入 | 2（气+水） | 有电 | — | 3×3 | ✅ |
| crystallizer | 2 输入 | 2 | 有电（低功率 5） | — | 3×3 | ✅ |
| high_pressure_synth | 3 输入 | 3（nightglass） | 有电 | — | 3×3 | ✅ |
| fractionator | 3 输入 | 3（ashbreath） | 有电 | — | 3×3 | ✅ |
| titration_bench | 2 输入 | 2 | **无电** | — | 3×2 | ✅ |
| decanter | 1 输入 | 1 | **无电** | — | 3×3（产物 2） | ✅（但空容器误投会被销毁，NEW-P1-6） |
| gas_scrubber | 1 输入 | 1 | 有电 | — | 3×3 | ✅ |
| filtration_bench | 1 输入 + 1 滤纸专用槽 | 液体+滤纸 | **无电** | FilterSlot 只收 `filter_paper*` ✅ | 3×3 | ✅ |
| canister_filling | 1 输入 + 1 空容器专用槽 | 气体+空钢瓶/空桶 | 有电 | ContainerSlot 只收 `gas_canister_empty`/`gas_drum_empty` ✅ | 4×4 | ✅（2 个无效气瓶槽 P2-5；同时放钢瓶+大桶会双销毁 NEW-P1-6） |
| heating_platform | 2×2 反应槽 + 加热器 + 灯芯 + 燃料 + 2 气瓶槽 | 1–2 物品 | **无电**（酒精灯） | 反应槽只收容器/药剂/原版映射 ✅；燃料槽只收乙醇/原版酒精 ✅ | 6×3 | ⚠ NEW-P1-2/3/4、P2-10 |
| （打印台 label_printer 不是机器） | — | — | — | — | — | 见 §6 ChemPrinter |

其它机器相关核对：
- **输出满**：夜间机器开产前 `CanAcceptOutputs`（含消耗气瓶要回收的空瓶位）会拦；加热台**没有**该预检（P2-10）。
- **未通电**：`battery != null` 的机器每周期 `MachineHelper.CanPower` 不足直接 return（不消耗输入）✅；无电池面板的机器 `powered=false` 不检查 ✅（滗析/过滤/滴定/加热台）。
- **气瓶槽**：只接受带 `CanisterTag` 且能反查 `gas_*` 的 `can_*` 物品 ✅；只有「加了该气体能匹配当前配方」才参与（不会因空放气瓶破坏其它配方）✅；反应真正消耗的气瓶会被销毁并把 `gas_canister_empty` 放回输出（输出满则保留气瓶、不复制）✅。
- **滤纸**：只进过滤台专用槽；`Finish` 读耐久→递减→用完销毁 ✅；提纯等级 +1（cap 5）✅。
- **模块**：只有 `chem_reactor`/`electrolyzer` 有模块槽，且 `AllowOnlyTaggedItemsOr(…MODULE_TYPE_UNIVERSAL…)`；模块效果走原版 `ModuleHelper` ✅。

## 8. 电话 / 打印机 / 调试 / 书籍 / 便签 交互路径完整性

- **电话**：注入（`InitPhoneClientDict` 静态 Postfix + `EnsureRegistered` 对旧档 PhoneClientDict 补条目）✅；对话注册（`PhoneClientList.DialogFuncs` public static 字典）✅；每页按钮 label + 挂断 ✅；订购占位→扣款→回滚 ✅；次日 `OnShutterOpenedEarly` 配送（前柜台满退后柜台）✅。**断点**：姓名（P0-1）、快捷拨号（NEW-P0-1）、`SpawnItem` null 少货（P2-1）、结果页预计算（P2-2）。
- **打印机**：工厂重建时 `Bind` ✅；15 模式循环（14 标签 + 校准）✅；`SetPurityFeature(real, fake)` 真实/标签分离 ✅；校准要求纯度 ≥99.5% + 催化剂充能（≤3）✅；医疗模式 `SetGameItemType(MEDICAL)`（只加不撤，已注释）✅。风险：`modeIndex` 全局（P2-19）、`Bind` 委托字段版本变更静默失效（P1-9）。
- **调试**：F10 快捷键（`AddGameOptionKeyBind` + 每帧轮询 + `KeyBindHelper.SuppressHotkeys` 静默 + 仅店铺场景）✅；F9 面板入口（受 `DevTools.IsEnabled`/dev.txt 门控，已如实注释）✅；按数字 ID 生成（97 个 ID、全角/动词前缀解析）✅；后柜台 `CanAccept` 只读探测 + `TryAcceptAllOrDestroy` 放置 ✅；满仓用原版 `GenericConfirmationUIManager` 提示 ✅。`debug_mode` 默认关、开关走模组配置 ✅。
- **书籍**：`chem_codex`（4 页手册）经 `ModHelper.OnPaperOpened` 注册 `refresh` 动作显示已发现配方数（不碰 PlayerStore）✅；`chem_index` 索引书 4 页 1–97、无越界 ✅（唯一性扫描 P2-14）。
- **便签**：机器 NotePanel + `PreBuiltItemHelper.CreateHandnote` ✅（随物品存档；语言切换需重建，已知）；商人便条主路径 handnote ✅，但 Spec/调试路径仍坏（P0-5）。
- **仪器**：thermometer（点击循环 Temp 0–3，`SetTemp` clamp 0–3）✅、ph_meter（1/7/13 循环）✅、precision_balance（Balanced 开关）✅、safety_goggles（Protected 开关）✅、catalyst_kit（提示）✅、alcohol_lamp/spirit_burner（点击切换 HIGH/LOW + 日志）✅；tooltip 状态行由 `ChemEquipment.StatusLine` 统一输出 ✅。**温度/ pH/天平/护目镜是全局单值**（P2-4）。

## 9. 存档兼容 / 幂等 / 性能 结论（摘要）

- **旧档重建**：`OnGameLoadedNormal` + `OnGameLoadedLate` + 每日开店都会 `EnsureCustomLiquids` + `RehydrateLoadedItems` + `ChemPhone.EnsureRegistered` ✅；`BridgeVanillaChemData` 对液体统一走幂等 `EnsureLiquidBridge`（保留 name/desc/flavor/value/sprite）✅；`ApplyComponentComposition` 有 `fc_components_ver` 版本迁移（旧组分清零后重写）✅。
- **已知缺口**：玩家背包/非 Emporium 容器里的旧物品不在批量重桥接范围（P2-11）；新产出液体不主动桥接（NEW-P1-7）；加热台温度/进度不落盘（NEW-P1-4）。
- **幂等**：`EnsureOwned`/`SetPourVolumeMl`/`EnsureCustomLiquids`/`EnsureLiquidBridge`/`RehydrateLoadedItems` 设计为幂等；电话 `EnsureRegistered` 幂等（但 `EnsureQuickDial` 有状态副作用，NEW-P0-1）。
- **性能**：每秒 tick 只有加热台（数量少）与 `Rigs.ToArray()` 分配；读档重桥接全量扫描两次（P2-11）；F10 每帧轮询成本极低。当前规模无瓶颈。
- **验证状态**：以上均为静态审查 + 编译/纯函数校验；**模组加载、游戏内交互、存档重载、Harmony 补丁实际生效、原版 UI 显示均未实机验证**。

---

## 10. 建议修复顺序（给 features / lead）

1. **NEW-P0-1 → P0-1 → P0-2**（电话三连：先删 EnsureQuickDial 的 Responding 覆写，再加 GetLocalizedDisplayName Postfix 或改 locID，最后实机确认电话簿/快捷拨号）——这是用户明确反馈的最高优先级。
2. **NEW-P1-1**（倒液体积记账，一行修复，直接影响用户「倒液」核心体验与复制漏洞）。
3. **P0-5 残留**（便条走工厂/调试 ID 时坏；二选一修）。
4. **NEW-P1-5**（非液体纯度双轨，影响所有气体/固体显示与灌装纯度）。
5. **NEW-P1-2/3/4**（加热台：产物继承、燃料守恒、Rig 清理 + 温度落盘；一组相关修复）。
6. **NEW-P1-6**（容器误投销毁）、**NEW-P1-7**（Create 前移桥接）、**NEW-P1-8**（二次开档王尔德，先实机验证）。
7. P2 列表按性价比：P2-1/2（电话）、P2-3（温度语义）、P2-8/9（数据源收敛）、P2-10（输出满）、P2-12（分析仪缓存）。

> **状态复核提醒**：本文件 §1–§4 保留 api-researcher 原文，§5 起为 code-core（task-67）追加。features 的 task-66 与 QA task-68 请以**当前磁盘代码**为准重跑 §3 静态断言；若行号/状态与本文件不一致，以最新代码为准并回写本文件状态列。

---

## 10.1 NEW 项最终状态（build 161,280 B / features task-66 修复后复核）

> 复核方法：在 features「P0 7/7、P1 10/10 已修复」的最终代码上逐条 `grep` 验证（2026-11-10 14:40 快照）。**结论：原清单的 7 P0 / 10 P1 确实都有对应代码修复，但 code-core 新增项中有 7 项在最终代码中仍未修复，其中 NEW-P0-1 会在运行时抵消 P0-2 的修复效果。**

| 新增编号 | 一句话 | 最终代码复核 | 影响 |
|---|---|---|---|
| NEW-P0-1 | `EnsureQuickDial()` 把 `phoneState` 覆写为 `Responding`，与 `ShownInPhoneBook()` 要求的 Regular/Cooldown 相反 | **未修复**（ChemPhone L128 仍 `PhoneState.Responding`；ModEntry L425/512/523 仍调用） | **抵消 P0-2**：电话簿/快捷拨号运行时仍不显示；用户最高优先级问题复发 |
| NEW-P1-1 | 倒液主路径 `SetPourVolumeMl(source, move / 1000)` 写的是倒出量而非剩余量 | **未修复**（ChemPatches L385） | 部分倒液后 `fc_volume_ml` 错误 → 兜底路径可多倒（资源复制漏洞）、空瓶判定错误 |
| NEW-P1-2 | 加热台产物用工厂默认物品，不继承输入污染物/提纯等级 | **未修复**（ChemHeating `Produce` L254-271 仍 `DirectoryMaster.Item(id)` 直出） | `heat_hcl_distill` 不提纯反而降纯；所有热反应产物忽略输入状态 |
| NEW-P1-3 | `Refill` 一次销毁整瓶燃料 | **未修复**（ChemHeating L319-334，`fuel.Destroy()` L328） | 每次补燃料浪费大半瓶（燃料守恒缺失） |
| NEW-P1-4 | 加热台 `Rigs` 无注销/无读档清理；`Rig.Temp` 不读回 tag | **未修复**（ChemHeating 无 `Rigs.Remove`、`Register` 不读 `TTemp`；`Stop` 仅 OnDisable） | 读档后 Rig 残留/重复 tick（双倍产出/耗燃料）、温度归零 |
| NEW-P1-5 | 非液体药剂 `fc_purity` 恒为 94（污染物上限钳制），与 `TGrade`/原版纯度双轨 | **未修复**（ChemCatalog `WriteState` L983/L993） | 气体/固体显示 94%；气瓶槽灌装被判 Medical → 工业钢瓶被写成 95% 医用；两条投料路径纯度不一致 |
| NEW-P1-6 | `gas_canister_empty`/`gas_drum_empty` 可被任意设备接受且 Finish 无条件销毁 | **未修复**（ChemMachines `AcceptsInput` L139-140；`Finish` L742-743） | 误投容器到任何设备都会白丢；灌装台同时放钢瓶+大桶双销毁 |
| NEW-P1-7 | 工厂产出的液体药剂不主动 `EnsureLiquidBridge`，依赖 GameItem.Target 补丁 | **未修复**（ChemCatalog `Create` 只 `SelfCheckLiquidBridge`；`EnsureLiquidBridge` 仅旧档/补丁路径） | 补丁一旦失效，产线液体倒不进原版容器/分析仪识别不到（核心交互脆弱） |
| NEW-P1-8 | 会话内 `networkRegistered` 不复位，二次开档可能不重注册王尔德卡片 | **待实机验证**（ModEntry L239/L248/L561 未改） | 返回主菜单→新开档后 4 张卡可能缺失（需实机确认） |

**建议 Lead 决策**：NEW-P0-1 是一行修复（删 `ChemPhone.cs` `EnsureQuickDial` 内的 `phoneState` 覆写或改为 `Regular`），建议在最终交付前修掉，否则「快捷拨号」在实机仍是坏的（QA 静态断言 `grep PhoneState.Responding` 会报 FAIL）；NEW-P1-1 同样是一行修复。其余 NEW-P1 可列入后续版本（需 lead 排期）。

---

## 11. 与 Lead「P0 7/7、P1 10/10 已修复」的差异说明

- **原清单层面属实**：`P0-1 ~ P0-7`、`P1-1 ~ P1-10` 在最终代码（161,280 B）中都能找到对应修复实现；`build` 0/0、`logic_check` 763 断言、`logic-harness`、`validate` 77 PASS / 0 FAIL 全部通过。
- **但「7/7、10/10」不等于「电话三连与倒液可玩」**：本复核在最终代码上发现：
  1. **P0-2 被 NEW-P0-1 抵消**（运行时回归）——建议修掉后再宣称电话功能完成；
  2. **P0-6 主体虽在，但 NEW-P1-1 让体积记账错误**——「能倒」但「体积不对」，建议一并修；
  3. **P0-5 仍有两个生成路径**（Spec #97 / 工厂）会产出打不开的化学便条；
  4. **加热台一组 4 项（NEW-P1-2/3/4 + P2-10）**是真实资源/状态缺陷，原清单 P1-10 只覆盖了「未验证」，实际存在可复现的静态问题；
  5. **数据一致性 NEW-P1-5** 会让所有气体/固体的显示纯度与灌装纯度错位（玩家可见）。
- **建议**：最终交付/汇报口径写成「原 P0/P1 清单已修复（静态），code-core 复核另有 8 项新增遗留（NEW-P0-1 为最高优先）」；不要写「整个代码体系全部通过」——**编译通过 + 静态 PASS 不等于游戏内验证**（模组加载、UI 显示、存档重载、Harmony 实际生效均未实机）。
