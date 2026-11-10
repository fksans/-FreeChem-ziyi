# RECON（阶段 0 侦察）· 草稿

> **状态：草稿，未在游戏文件上核对。**
> 这份草稿只整理了仓库里已有的研究文档（`docs/api-findings.md`、`docs/harmony-bridge.md`、`docs/container-component-research.md`、`docs/feedback-research.md`，都是上一版开发时对 `Assembly-CSharp.dll` 做 IL 探针得到的记录，游戏版本 0.46 / Steam Build 26525552）。
> 每条结论标了来源：**[文档]** = 旧研究文档里有 IL 证据；**[待核]** = 需要在有游戏文件的机器上确认；**[未知]** = 现有资料里没有。
> 需要本地核对的清单在最后一节。

## 1. 构建类型

- **Mono**。游戏程序集在 `Probably Stolen_Data\Managed\Assembly-CSharp.dll`，旧版 csproj 直接引用它编译。**[文档]**
- 可以用常规 C# 反编译工具（ILSpy / dnSpy）直接读原版代码；仓库里的 `tools/api-probe` 是一个只读的元数据 / IL 导出工具，可复用。

## 2. Mod 加载器

- 游戏自带**官方 ModLoader**：模组是一个文件夹（`Mods/<名字>/`），里面有 `manifest.xml`、一个实现 `IMod` 的 DLL，以及 `Textures/`、`Localization/`、`LootTables/`、`Books/` 等数据目录。**[文档]**
- 游戏自带 `0Harmony.dll`；加载器会自动应用模组里的 `[HarmonyPatch]` 类，不需要手动 `PatchAll`。**[文档]**
- **联机 mod 用的是什么加载器：[未知]**。计划书假设是 BepInEx 或 MelonLoader，但这个游戏有官方加载器，联机 mod 很可能也走官方加载器。需要用户给出联机 mod 的名称和版本后确认。
- **与计划书冲突的硬约束：Capabilities。** `manifest.xml` 的 `<Capabilities />` 为空时，加载器的扫描器会拒绝使用文件 IO、网络、线程（`Thread` / `Task.Run`）、`Process`、`DllImport`、反射外部类型的模组。**[文档]**
  - 影响一：**运行时读 `recipes.json` 属于文件访问**。两条路：① 生成器直接输出 C# 源文件（`Recipes.g.cs`），配方表编译进 DLL；② 在 manifest 里声明文件访问能力，用 `ModHelper.ReadModDataFileText`。推荐 ①：不需要任何能力，联机双方只要 DLL 相同，配方就一定相同。能力名称和声明后的效果 **[待核]**。
  - 影响二：计时只能用 `ModHelper.StartCoroutine` + `WaitForSeconds`，或者挂在原版机器的周期回调上，不能开线程。
- `Init` / `OnEnable` 在主菜单运行，不能访问存档和游戏内管理器；物品注册只能在 `ModHook.OnModItemDirectoryInit`。**[文档]**

## 3. 原版化学的实现

- **液体 = `Liquid` 类**，硬编码在程序集里（`Liquid.InitLiquid()` 构造，`Liquid.Liquids` 列表 / `Liquid.LiquidDict` 字典，均为 public）。每种液体有 `identifier`、`displayName`、`currentPart`（物品上存量的 tag 名）、`boilingPoint`、`isEvaporative`、`particleSize`、`lethality`、`euphoria`、`addictiveness` 等字段。**[文档]**
- **原版液体 id 全表**（`Liquid` 的 public const string）**[文档]**：
  `water, heavy_metal, microbe, organic_waste, mineral, chemical_contaminant, physical_contaminant, microplastic, dream_cap_mush, dream_cap_extract, plant_matter, alcohol, caffeine, protein, fat, starch, sugar, coffee_mush, contraceptive, zyanide, nightmare_extract, nightmare_cap_mush, chlorine, synaprest, fanta, methylol, reducing_agent, acid, base, stabilizing_agent, debris, lipobond_agent, livestock_steroid, livestock_immunity, wine_extract, nomore`
  共 36 种，与 wiki 的「约 36 种液体」吻合。
- **组分存储**：容器里每种液体的量存在物品 tag 上（tag 名 = `Liquid.currentPart`），单位是 part，**1 ml = 1000 part**。液体容器由 `WaterHelper.InitLiquidContainerItem(item, capacityMl, basePrice, ...)` 初始化；增减用 `WaterHelper.AddLiquid` / `RemoveAndReturn` / `TransferLiquid`。**[文档]**
- **纯度**：成品纯度存在 `ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG`，七档（阈值 99 / 90 / 80 / 60 / 45 / 30），对应 `ItemConditionList.CreateLabPure()` 等，最高档 Lab Pure +135%。水另有一套按 `water` 占比算的纯度档。**[文档]**
- **反应规则定义在哪里、有几条：[待核]**。旧文档只记了「按温度驱动、加热板每秒回调、夜间对非空容器连续跑 360 轮」，没有定位到反应表所在的类。wiki 说 7 个配方，旧交接文档说 22 项反应，两个数字需要在代码里核实。
- **进度 / 计时概念：原版有。** `MachineProgressHelper`：`InitProgressTypeMachine(machine, speed, ...)`、`ContinueProgressTypeMachine`、`IsProgressTypeMachineFinished`、`FinishProgressTypeMachine`；状态存在机器物品的 tag 上（`MACHINE_STATE_TAG` = `STATE_READY` / `STATE_WORKING`，`MACHINE_PROGRESS_CURRENT_TAG` / `MACHINE_PROGRESS_TARGET_TAG`）。机器靠 `onCycleEndSlotItemFunc` 周期回调推进。**[文档]**
  - 周期回调多久触发一次、白天是否触发、和「游戏秒」怎么换算：**[待核]**。这决定「反应时间」用原版进度机制，还是用协程自己计时。

## 4. 原版杂质系统

- **杂质就是液体组分**，id 见上表。粒径（决定能否被滤芯拦住）**[文档]**：

  | 杂质 id | 粒径 | 杂质 id | 粒径 |
  |---|---|---|---|
  | `chemical_contaminant` | 2 | `organic_waste` | 40 |
  | `heavy_metal` | 12 | `microbe` | 50 |
  | `microplastic` | 25 | `physical_contaminant` | 100 |
  | `mineral` | 30 | `debris` | 200 |

- **重金属确认存在**，id 是 `heavy_metal`。另外还有 wiki 没列的 `microplastic`、`mineral`、`debris`。
- 滤芯孔径：普通 / 高级 / 水培芦苇 22，韧棉织物 150。粒径小于 22 的化学杂质、重金属不会被普通滤芯去除。化学杂质有 0.7% 残留下限。**[文档]**
- `Liquid` 上有过滤相关字段：`filterMinimumPurity`、`filterDurabilityUsage`、`filterVolumeRemoved`、`canBeRemovedByPurifier`、`basePurifierRemoveAmount`、`baseMaxPurityFromMachine`。**[文档]**
- 预装试剂瓶会把 2%–8% 替换为杂质（来自 wiki，即**随机比例**）。生成代码的位置 **[待核]**（候选：`ChemicalCompositionHelper`、`PreBuildChemHelper`）。
- **各条去除规则（氯、酸碱、酒精、加热、稳定剂、Lipobond）在代码里怎么实现、能不能加新规则：[待核]**。这是阶段 0 最重要的缺口。
- 上一版的教训（详见 `docs/code-review-2026-11-10.md`）：自己另造一套污染物 tag 和成分表，会和原版的显示、倒液、过滤全部对不上。v3 直接读写原版的杂质组分 tag 是对的。

## 5. UI

- 机器窗口用官方构建器：`MachineHelper.CreateMachineryWindow(bool, params MachineryPanel[])`，面板有 `Panel(label, element, role)`、`PowerPanel`、`NotePanel`；元素有 `GameSlotInventory`、`GameGridInventory`、`GridPixelElement`、`TagElement`（文字标签）。**[文档]**
- **进度条**：`InitProgressTypeMachine` 有一个 `useDefaultTooltip` 参数，说明原版进度机自带进度提示。窗口里有没有现成的进度条控件 **[待核]**。保底方案是物品 tooltip（`ModHook.OnCreateTooltipLate`）显示进度。
- **检测结果显示**：原版成分仪 `ChemScannerUIManager` 会列出物品上每种 `Liquid` 的量和百分比。只要杂质用的是原版组分 tag，**不打任何补丁就能显示**。**[文档]**
- 自定义窗口：`CustomUIManager.Window(id, title, layer)` 链式构建器（标签、按钮、输入框）。原版弹窗：`GenericConfirmationUIManager.Instance.OpenPanelSingleButton`。**[文档]**

## 6. 联动对象的原版物品 ID

| 对象 | 已知 | 状态 |
|---|---|---|
| 水 | 容器：`small_bottled_water`、`bottled_water`、`large_bottled_water`、`water_jug`、`water_pitcher`（3000 ml，带滤芯）。水是容器里的 `water` 组分，没有单独的「纯水物品」；水纯度按 `water` 占比分档（PURE 99.9% / HIGH 99% / BASE 98% …）。原版设备有净水器、水分收集器 | **[文档]**；净水器、收集器的 id **[待核]** |
| 两种激素 | 液体组分里有 `livestock_steroid` 和 `livestock_immunity`，很可能就是用户说的两种 | 组分 id **[文档]**；对应的成品物品 id、用途、是不是用户指的那两种 **[待核 + 待用户确认]** |
| 水培 | 存在 `HydroponicDirectory` 物品目录类（咖啡豆由它创建）；有「水培芦苇」滤芯 | 营养品物品 id **[待核]** |
| 养殖 | 只知道上面两个 `livestock_*` 组分 | **[待核]** |
| 养鱼 | 现有文档里没有任何记录 | **[未知]** |
| 其它可能相关的组分 | `chlorine`（净水片）、`stabilizing_agent`、`lipobond_agent`、`reducing_agent`、`methylol`、`acid`、`base` | **[文档]** |

## 7. 存档

- 物品状态存在 tag 里，随存档保存。读档时 `SaveManager.DecodeNodesInternal` 先用物品工厂重建模板，再用存档里的 `itemState` **整体覆盖** tag。也就是说：工厂里写的 tag 读档后以存档为准；工厂里挂的**委托（回调函数）不进存档**，每次都由工厂重新挂。**[文档]**
- **反应到一半存档**：如果进度存在机器的 tag 上（原版 `MACHINE_PROGRESS_*`，或自己的 tag），读档后自然恢复。上一版遇到过「读档后机器卡在 working」的情况，需要一个读档自检。**[文档]**
- 模组级的按存档数据：`ModHelper.Get/SetModData*`（键值对，存在本局存档里，主菜单不可用）。**[文档]**
- **卸载 mod 后读档会怎样：[待核]**。存档里会留下模组物品的 identifier，原版目录找不到模板时是跳过、变成空物品还是报错，没有记录。

## 8. 联机同步（用户确认：不用管，本节仅作记录）

- **[未知]**：没有联机 mod 的任何资料。需要它的名称、版本和文件才能看。
- 设计上先按这些原则准备：
  - 配方表编译进 DLL，结果只取决于「投料 + 温度档」，不含随机数。
  - 随机只出现在「原料生成时带多少杂质」，只在主机上生成。
  - 计时状态存在机器物品的 tag 上。如果联机 mod 同步物品 tag，计时就跟着同步；如果不同步，就需要另想办法。这一点要等看到联机 mod 才能定。

## 9. 结论与建议

1. **走官方 ModLoader，不引入 BepInEx / MelonLoader**（除非联机 mod 强制要求）。
2. **不是纯数据 mod**：新机器、计时、查表都需要 C#。但 Harmony 补丁可以压到很少，因为：
   - 杂质直接用原版 `Liquid` 组分 → 成分仪显示、倒液、过滤、纯度计价全部由原版处理，不需要补丁；
   - 新增的提纯方法做成「带反应时间的配方」，放在本模组自己的机器上，直接改物品的原版组分 tag。
   需要补丁的地方预计只有：给原版原料生成加杂质（如果要改原版物品），以及重金属的负面效果（植物、牲畜、鱼）。
3. **配方表用生成的 C# 源文件，不在运行时读 JSON**（见第 2 节的 Capabilities 约束）。`recipes.json` 仍然可以作为生成器的中间产物和校验对象。
4. **不注册自定义 `Liquid`**。上一版往 `Liquid.Liquids` 里加了 42 种成分，每个原版容器都会多出 42 个 tag。v3 的新物质如果只是固体原料，做成普通物品即可；确实需要新液体组分时再逐个评估。
5. **反应时间**优先复用原版 `MachineProgressHelper`（进度存 tag、读档自动恢复、可能自带进度提示），前提是周期回调的节奏合适（第 3 节待核项）。

## 10. 需要在有游戏文件的机器上核对的清单

按重要程度排序：

1. 原版反应规则在哪个类里（搜 `chlorine`、`stabilizing_agent`、`organic_waste` 的引用），有几条，怎么被调用；能不能往里加规则。
2. 原料杂质的生成代码（搜 `chemical_contaminant` 的写入处、`ChemicalCompositionHelper`、`PreBuildChemHelper`）：随机范围、哪些物品带杂质。
3. `onCycleEndSlotItemFunc` 的触发节奏：多久一次，白天和夜间是否不同。
4. manifest 的 Capabilities 有哪些可选值，声明文件访问能力后 `ReadModDataFileText` 是否可用。
5. `livestock_steroid`、`livestock_immunity` 对应的物品 id 和用途；水培营养品、养殖、养鱼相关的物品 id（搜 `HydroponicDirectory`，以及含 `fish`、`livestock`、`nutrient` 的类名和字符串）。
6. 机器窗口里有没有现成的进度条控件（看原版进度机，比如 3D 打印机、催陈器的窗口怎么建）。
7. 卸载模组后读档，模组物品怎么处理（看 `SaveManager.DecodeNodesInternal` 找不到模板时的分支）。
8. 联机 mod：用什么加载器，同步哪些东西（物品 tag、机器状态、随机数），有没有给其它模组用的接口。

## 用户已确认（2026-11-10）

1. **联机 mod 不用管。** 阶段 0 第 8 项（联机同步）和推进顺序里的「联机验证」都跳过；设计上仍保持结果确定、状态存在物品 tag 上，但不为联机做专门工作。
2. **养殖 = 养老鼠**；两种激素是养老鼠用的生长激素。原版组分里的 `livestock_steroid`、`livestock_immunity` 是最可能的对应项，物品 id 和用途仍需在游戏文件里核对（第 10 节第 5 项，搜老鼠相关的类和物品）。按计划书的内容边界，激素只做虚构 / 抽象配方。
3. **反应时间不要太长，跟原版的反应效率走。** 所以优先复用原版机器的进度机制和节奏（第 10 节第 3 项的核对结果决定具体数值），生成器的全局缩放系数按原版同类机器的耗时来定。
4. **杂质偏硬核。** 原料杂质比例取原版范围的上沿或更高，纯水等联动产品的合格线定得严，需要多步提纯才能达标。
5. **旧版代码放弃**（修复 PR 已关闭），**旧贴图保留**：`FreeChem/Textures/Items/` 和生成脚本 `tools/gen_icons.py` 可以在 v3 里复用。

仍待确认：游戏安装路径是否还是 `D:\SteamLibrary\steamapps\common\Probably Stolen`。
