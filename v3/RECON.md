# RECON（阶段 0 侦察）

> **状态：第 10 节第 1–7 项已在游戏文件上核对（2026-11-10）。**第 8 项是联机，用户已确认跳过。
> 核对对象：`C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed\Assembly-CSharp.dll`，4,820,224 字节，修改时间 2026-11-09 03:18 UTC。程序集里有 2,788 个类型、25,202 个方法。
> 核对方法：用仓库里的 `tools/api-probe` 对全部方法导出 IL，再按字符串和调用关系逐条查。另外写了一个只读的小脚本，扫描 `ModRestrictedAttribute`。**全部是静态阅读，没有在游戏里实测。**运行时的行为（时序、暂停、UI 刷新）仍要进游戏确认，相关条目已标出。
> 来源标记：**[文档]** = 旧研究文档里有 IL 证据；**[核实]** = 这次在当前 DLL 上读 IL 确认过；**[待实机]** = 静态读法已经清楚，运行效果还要进游戏看；**[未知]** = 资料里没有。

## 1. 构建类型

- **Mono**。游戏程序集在 `Probably Stolen_Data\Managed\Assembly-CSharp.dll`，旧版 csproj 直接引用它编译。**[文档]** **[核实]**：`Managed\` 目录里有 `Assembly-CSharp.dll`、`0Harmony.dll`、`DseCecil.dll`（加载器的扫描器用它读 IL）和 `Newtonsoft.Json.dll`，没有 IL2CPP 的产物。
- 可以用常规 C# 反编译工具（ILSpy / dnSpy）直接读原版代码；仓库里的 `tools/api-probe` 是一个只读的元数据 / IL 导出工具，可复用。

## 2. Mod 加载器

- 游戏自带**官方 ModLoader**：模组是一个文件夹（`Mods/<名字>/`），里面有 `manifest.xml`、一个实现 `IMod` 的 DLL，以及 `Textures/`、`Localization/`、`LootTables/`、`Books/` 等数据目录。**[文档]**
- 游戏自带 `0Harmony.dll`；加载器会自动应用模组里的 `[HarmonyPatch]` 类，不需要手动 `PatchAll`。**[文档]**
- **联机 mod 用的是什么加载器：[未知]**。计划书假设是 BepInEx 或 MelonLoader，但这个游戏有官方加载器，联机 mod 很可能也走官方加载器。需要用户给出联机 mod 的名称和版本后确认。
- **与计划书冲突的硬约束：Capabilities。** `manifest.xml` 的 `<Capabilities />` 为空时，加载器的扫描器会拒绝使用文件 IO、网络、线程（`Thread` / `Task.Run`）、`Process`、`DllImport`、反射外部类型的模组。**[文档]**
  - 影响一：**运行时读 `recipes.json`**。两条路：① 生成器直接输出 C# 源文件（`Recipes.g.cs`），把配方表编译进 DLL；② 运行时用游戏 API 读模组自带的文件。两条都可行，仍推荐 ①：DLL 相同，配方就一定相同。
  - 影响二：计时不能开线程。可以用 `ModHelper.StartCoroutine` + `WaitForSeconds`，或者挂在原版物品的每秒回调上（见第 3 节，推荐后者）。
- **【核实】Capabilities 的可选值**：见 `ModCapability` 类，它的 `public const string` 字段和 `ModCapability.All`（在 `ModCapability..cctor` 里初始化）共 9 个：
  `files`、`network`、`process`、`dynamic-code`、`native`、`reflection-external`、`steam`、`steam-auth`、`threads`。
  - `ModCapability.Unscannable`（扫描器查不出来，只能靠声明）：`reflection-external`、`native`、`dynamic-code`。
  - `ModCapability.Retiring`（正在淘汰）：`reflection-external`、`native`、`dynamic-code`、`threads`。
  - `ModTrustList.VerifiedOnlyCapabilities` 和 `WORKSHOP_VERIFIED_ONLY_CAPABILITIES` 说明，有些能力只对认证过的创意工坊模组开放。具体名单是运行时下载的信任列表，DLL 里看不到。
- **【核实】`files` 能力是怎么触发的**：扫描器在 `ModScanner+Pass::CheckCall` 里检查模组 IL 是否**直接**用到这些类型：`System.IO.File`、`FileStream`、`Directory`、`FileInfo`、`StreamReader`、`StreamWriter`、`ZipFile`、`UnityEngine.AssetBundle` 的按路径加载、ES3 的按路径读写、注册表等（名单在 `ModScanner+Pass..cctor`）。
  调用 `ModHelper` 的方法本身不会被判成 `files`。唯一的例外是同一个函数里对 `ModHelper` 的 steam-auth 方法做的特殊检查。另外，带 `ModRestrictedAttribute` 的游戏方法会被拦下。我扫描了全程序集，`ModHelper` 的文件 API 和 `ChemicalHelper` **都没有**这个特性，被限制的只有 `SaveFiles`、`PaperEditorFiles`、`ModLoader`、`ModTrust`、`ModTrustList`、`ModUIManager.OpenModFolder`、`ModWorkshop.Unsubscribe`。
- **【核实】读文件的 API**：
  - **`ModHelper.ReadModFileText(modId, relativePath)` / `ReadModFileBytes` / `ModFileExists`**：读的是**模组自己的安装文件夹**，即 `ModHelper.GetModFolder` → `LoadedMod.FolderPath`。内部用 `RequireInside` 限制只能读这个文件夹里面的文件，再调 `File.ReadAllText`。**这是读随模组发布的 `recipes.json` 的正确接口。**
  - `ModHelper.ReadModDataFileText` / `WriteModDataFileText` / `ListModDataFiles` / `DeleteModDataFile`：读写的是 `Application.persistentDataPath\ModData\<modId>`，见 `ModHelper.GetModDataFolder`。这是可写的数据目录，**不是**模组的安装目录，随模组发布的文件不在这里。旧草稿写成用 `ReadModDataFileText` 读配方，是写错了。
  - 结论：调用 `ReadModFileText` **按静态阅读不需要声明任何能力**，因为模组 IL 里没有直接用 System.IO。**[待实机]**：要用一个空的 `<Capabilities />` 模组试一次，确认扫描器不报错。
- `Init` / `OnEnable` 在主菜单运行，不能访问存档和游戏内管理器；物品注册只能在 `ModHook.OnModItemDirectoryInit`。**[文档]**

## 3. 原版化学的实现

- **液体 = `Liquid` 类**，硬编码在程序集里（`Liquid.InitLiquid()` 构造，`Liquid.Liquids` 列表 / `Liquid.LiquidDict` 字典，均为 public）。每种液体有 `identifier`、`displayName`、`currentPart`（物品上存量的 tag 名）、`boilingPoint`、`isEvaporative`、`particleSize`、`lethality`、`euphoria`、`addictiveness` 等字段。**[文档]**
- **原版液体 id 全表**（`Liquid` 的 public const string）**[文档]**：
  `water, heavy_metal, microbe, organic_waste, mineral, chemical_contaminant, physical_contaminant, microplastic, dream_cap_mush, dream_cap_extract, plant_matter, alcohol, caffeine, protein, fat, starch, sugar, coffee_mush, contraceptive, zyanide, nightmare_extract, nightmare_cap_mush, chlorine, synaprest, fanta, methylol, reducing_agent, acid, base, stabilizing_agent, debris, lipobond_agent, livestock_steroid, livestock_immunity, wine_extract, nomore`
  共 36 种，与 wiki 的「约 36 种液体」吻合。
- **组分存储**：容器里每种液体的量存在物品 tag 上（tag 名 = `Liquid.currentPart`），单位是 part，**1 ml = 1000 part**。液体容器由 `WaterHelper.InitLiquidContainerItem(item, capacityMl, basePrice, ...)` 初始化；增减用 `WaterHelper.AddLiquid` / `RemoveAndReturn` / `TransferLiquid`。**[文档]**
- **纯度**：成品纯度存在 `ChemicalProductHelper.CHEMICAL_PRODUCT_PURITY_TAG`，七档（阈值 99 / 90 / 80 / 60 / 45 / 30），对应 `ItemConditionList.CreateLabPure()` 等，最高档 Lab Pure +135%。水另有一套按 `water` 占比算的纯度档。**[文档]**
- **【核实】反应规则在 `ChemicalHelper` 里，没有表，是一串硬编码的静态方法。**
  - 入口是 `ChemicalHelper.HandleTemperatureReaction(GameItem container)`（`public static void`）。容器空了直接返回；否则按固定顺序调用 22 个 `public static int Handle*(GameItem container)`，把返回值加起来。总和 > 0 时调用 `WaterFeatureHelper.OnLiquidChanged(container, WaterFeatureHelper.GetWaterFeature(container).IsExposed())`。
  - 22 条的调用顺序：`HandleMicrobeHeat` → `HandleMicrobeAlcohol` → `HandleOrganicWasteHeat` → `HandleDreamCapMushReaction` → `HandleNightmareCapMushReaction` → `HandleCoffeeBeanExtractionSolvent` → `HandleCoffeeBeanExtractionWater` → `HandleNomoreReaction` → `HandleDreamCapExtractDegradation` → `HandleDreamCapMushDegradation` → `HandleNightmareCapExtractDegradation` → `HandleNightmareCapMushDegradation` → `HandleOrganicWasteMineralization` → `HandleAcidBaseMicrobeKill` → `HandleStabilizingAgentSedimentation` → `HandleProteinPrecipitation` → `HandleLipobondAgentSedimentation` → `HandleAcidBaseNeutralization` → `HandleFantaHeat` → `HandleMethylolHeatReaction` → `HandleChlorine` → `HandleBoilingReaction`。
  - **两个数字都对得上**：旧交接文档的"22 项反应"就是这 22 个方法。wiki 的"7 个配方"按方法名看，对应的是会生成产品的那几条：DreamCap、NightmareCap、咖啡因两条（溶剂萃取和水萃取）、Nomore、Methylol / Fanta 一类。其余是杀菌、降解、沉淀、中和、氯化和沸腾。
  - **单条规则的写法**（以 `HandleChlorine` 为例）：用 `GetTagReadonly(Liquid.GetCurrentPartFromId(id)).GetInt()` 读出各组分的 part 数，按常数算出这一秒的变化量，再用 `ModifyTag(part, Action<TagState>, true)` 写回去，返回 1 表示有变化、0 表示没变化。
    - `HandleChlorine` 的具体逻辑：每秒最多处理 15000 part 的氯（`Math.Min(chlorine, 15000)`），处理能力 = 这部分氯 × 5.1。先去除化学杂质，最多去到总体积的 0.7%（`total * 0.007f`，这就是 wiki 说的 0.7% 下限）；剩下的处理能力用来杀微生物。最后氯会自然损耗，损耗量 = `min(剩余氯, max(1000, 剩余氯 × 0.2))`。
  - **调用方**：`HandleTemperatureReaction` 只有一个调用者，就是 `ChemicalHelper.containerTempTick`。这是一个委托，在 `ChemicalHelper..cctor` 里由 `<>c.<.cctor>b__62_0` 创建，内容是先 `HandleAmbientTempDrop(container)`，再 `HandleTemperatureReaction(container)`。`WaterHelper.InitLiquidContainerItem` 里调用 `ChemicalHelper.InitContainerTempFunction(item)`，把这个委托挂到容器的 `onOneSecondItemFunc` 上。
- **【核实】能不能加规则：能，而且只需要一个补丁。**规则没有注册表，所以没法"往表里加一行"。但 `HandleTemperatureReaction` 是 `public static`，可以给它挂一个 Harmony **Postfix**，在里面跑本模组的规则：
  - 模组规则仿照原版写法：读组分 tag → 算这一秒的量 → `ModifyTag` 写回。有变化时，自己再调一次 `WaterFeatureHelper.OnLiquidChanged(...)`，因为 Postfix 拿不到原方法里的累加和。
  - 这样白天的每秒 tick 和夜间的 360 轮模拟（见下）**会自动包含本模组的规则**，不用另外处理。
  - 已确认 `ChemicalHelper` 和它的方法上**没有** `ModRestrictedAttribute`，扫描器不会拦这个补丁。
  - 如果只想改某一条原版规则，也可以对那一个 `Handle*` 单独打补丁（它们都是 `public static int`）。
- **【核实】tick 的节奏**（第 10 节第 3 项）：
  - **白天**：`EmporiumEntry.Update()` 每帧把 `Time.deltaTime` 累加到 `oneSecondTimer`；满 1 秒时减掉 1，调用 `OneSecPrefixFunction()`，然后对 `GetAllItems()` 里每个挂了 `onOneSecondItemFunc` 的物品调用一次。所以化学容器**每 1 秒（受 timeScale 影响的游戏秒）执行一次全部 22 条规则**。程序集里找不到 `Time.set_timeScale` 的调用。打开菜单或暂停时 tick 会不会停，**[待实机]**。
  - **夜间**：`PlayerStore.EndNight()` 调用 `ChemicalHelper.RunNightlyChemicalSimulation()`。这个方法先收集 `EmporiumEntry.GetAllItems()` 里挂了 `onOneSecondItemFunc` 的物品（其中挂的是 `containerTempTick` 而且是空容器的会被跳过），然后**连续调用 360 轮**（`ldc.i4 360`）。也就是说，过一夜相当于给每个容器补 **360 秒**的反应。注意这 360 轮跑的是**所有**挂了每秒回调的物品，不只是化学容器。
  - **`onCycleEndSlotItemFunc` 每夜只触发一次**：在 `PlayerStore.EndNight()` 里通过 `FindAllItem(...).ForEach(<EndNight>b__289_1)` 调用；之后还有一次 `onCycleEndLateSlotItemFunc`（`b__289_2`）。
  - **原版"进度机"是按天推进的**：`MachineProgressHelper.ContinueProgressTypeMachine` 的逻辑是 `MACHINE_PROGRESS_CURRENT_TAG += CURRENT_PROCESSING_SPEED_TAG`。打印机（`MachinePrinter.Printer` → `DisplayClass23_0.<Printer>b__4`）和反序列器（`MachineDesequencer.Desequencer` → `b__5`）都把它挂在 `onCycleEndSlotItemFunc` 上，所以**每夜只加一次**。`MachineDnaManipulator.WorkOneCycle` 和 `MachineHydroponic.GrowOneCycle` 也走这条路径。
  - 加热板：`MachineHeatingPlate.HeatingPlate` 把 `<HeatingPlate>b__4` 挂在 `onOneSecondItemFunc` 上，也就是每秒加热一次（`HeatLiquidOnStove` 里有 `350` 和 `Math.Min`，应该是温度上限）。
  - **结论**：用户要求"反应时间跟原版的化学反应效率走"，原版化学是**秒级**的，白天每秒一次、夜里补 360 秒。所以模组的反应**不要用 `MachineProgressHelper`**，它是按天推进的，跟化学的节奏不一样。应该按上面说的方法挂在容器的每秒 tick 上：反应时间 = 需要多少次 1 秒 tick，进度直接由组分的变化体现。

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
- **【核实】原料杂质是怎么生成的**（第 10 节第 2 项）。按原料分三类，规则各不相同：
  1. **预装试剂瓶：随机。**用的是 `PreBuildChemHelper.SetImpurity(GameItem item, int minImpurity, int maxImpurity)`：
     - 物品必须带 `LIQUID_CONTAINER_TAG`，否则直接返回。
     - 取 `r = RNG.GetRandomInt(min, max)`，单位是万分之一。先从容器里 `WaterHelper.Remove` 掉 `当前容量(UL) × r / 10000`，再把同样的量分给 `PreBuildChemHelper.impurityTypes` 里的四种杂质。这个数组在 `PreBuildChemHelper..cctor` 里定义，依次是 `chemical_contaminant`、`physical_contaminant`、`microbe`、`heavy_metal`。
     - 分配方式：前三种依次各取 `RNG.GetRandomInt(0, 剩余量)`，最后剩下的全给 `heavy_metal`。结果是前面的杂质平均分得多，重金属经常拿到"零头"，但偶尔会很多。
     - 调用方和参数：`CreateAcidBottle`、`CreateBaseBottle`、`CreateReducingAgentBottle`、`CreateLipobondAgentBottle`、`CreateFantaPrecusorBottle` 全部用 `(200, 800)`，也就是 **2%–8%**，和 wiki 一致。`CreateMiniAlcoholBottle` **不加杂质**。`RNG.GetRandomInt` 的上界是否包含在内，没有展开核对。
  2. **水：按水的等级随机。**入口是 `WaterHelper.AddWater(container, grade, addAmount = -1, useRange = false, lowPara = 0, highPara = 1, initWaterFeature = true)`，各 `FillWith*` 传入的 grade：`PureWater` 0、`HighQualityWater` 1、`BaseWater` 2、`Ghostwater` 3、`RustWater` 4、`Gutterflow` 5。杂质的总量在等级对应的范围内随机（里面有一处 `RNG.GetRandomInt`），各杂质之间的**比例是固定的**，定义在 `WaterPremadeHelper` 的静态字典里：

     | 字典 | organic_waste | microbe | physical | chemical | mineral | heavy_metal |
     |---|---|---|---|---|---|---|
     | `ghostwaterContaminantDistribution` | 0.25 | 0.30 | 0.10 | 0.15 | 0.05 | **0.15** |
     | `waterContaminantDistribution` | 0.20 | 0.15 | 0.10 | 0.30 | 0.15 | 0.10 |
     | `rustWaterContaminantDistribution` | 0.22 | 0.15 | 0.10 | 0.30 | 0.15 | 0.08 |
     | `gutterflowContaminantDistribution` | 0.299 | 0.15 | 0.10 | 0.30 | 0.15 | 0.001 |
     | `baseWaterContaminantDistribution` | 0.20 | … | | | | |

     `baseWater…` 只读到了第一项。其它按水源随机的入口还有 `WaterPremadeHelper.GetRandomWater`（配合 `waterOdds` / `waterActions`）。
  3. **蘑菇泥、咖啡泥、土豆：固定比例，不随机。**由 `ChemicalCompositionHelper.SetCoffeeBeanComposition`、`SetDreamCapComposition`、`SetNightmareCapComposition`、`SetPotatoComposition` 按重量写入。比如咖啡豆的配方里有 `water 2`、`fat 5`、`coffee_mush 16.15`、`caffeine 2.75`、`physical_contaminant 5`、`protein 5`。其中蘑菇和土豆还带 `microbe`。
  - 其它会写入杂质的地方：`MachineChemFinisher.IntroduceImpurity`（封装时混入 `physical_contaminant`）、`ChemFinisherRecipe.SetCompositionByPurity`（按纯度反推成分）、`WineHelper.PourWineIntoContainer`（`chemical_contaminant`），以及老鼠粪便 `HusbandryDirectory.FecesSmall/Medium/Large`（`microbe`）。
- **【核实】原版对每种杂质的去除手段**，按"哪些代码会减少这个组分"统计：

  | 杂质 | 原版 `ChemicalHelper` 规则 | 原版机器 / 物品 |
  |---|---|---|
  | `chemical_contaminant` | `HandleChlorine`（下限 0.7%） | 净水片 `WaterHelper.OnWaterPurificationTabletAdded`、`MachinePurifier`、`WaterHelper.PurifyToBaseWater`、鱼缸的 `AquacultureHelper.RunRemediation` |
  | `microbe` | `HandleMicrobeHeat`、`HandleMicrobeAlcohol`、`HandleAcidBaseMicrobeKill`、`HandleChlorine` | `WaterHelper.HandleFilter`、`WaterHelper.OnUVUsed` |
  | `organic_waste` | `HandleOrganicWasteHeat`（有一部分转成 `physical_contaminant`）、`HandleOrganicWasteMineralization` | — |
  | `physical_contaminant` | **没有**（`HandleOrganicWasteHeat` 反而会产生它） | 滤芯（粒径 100 > 孔径 22）、鱼缸 `RunRemediation` |
  | **`heavy_metal`** | **一条都没有** | 只有 `MachinePurifier`（在它的 `.cctor` 配置里）、`WaterHelper.PurifyToBaseWater` 和鱼缸 `RunRemediation`；粒径 12 < 22，普通滤芯滤不掉 |

  → **重金属在原版里没有任何化学去除方法**，这正是本模组可以补的空白，比如沉淀剂 + 过滤 → 滤渣。
- **【核实】杂质对联动对象的影响**：
  - **水培**：`HydroponicHelper.HandleContaminant` 会依次对 `heavy_metal`、`chemical_contaminant`、`microbe`、`organic_waste`、`acid`、`base` 调用 `ApplyContaminantEffect(...)`，强度由 `GetContaminantEffect` × `GetContaminantSensitivity` 决定。**原版已经有重金属伤植物的效果**，不用另打补丁。
  - **养鱼**：鱼的健康只看 `AquacultureHelper.GetWastePercent`，而这个函数**只统计 `organic_waste`**；死亡阈值 `WATER_DEATH_RATIO = 0.5`。鱼缸的 `RunRemediation` 会清掉 `AquacultureHelper.REMEDIATED_LIQUIDS` 里的三种：`heavy_metal`、`chemical_contaminant`、`physical_contaminant`。→ **原版里重金属不伤鱼**。要做"重金属伤鱼"，得给 `ApplyWaterChecks` 或 `GetWastePercent` 打补丁。
  - **养鼠**：`Husbandry*` 系列类里完全没有引用 `heavy_metal`。→ **原版里重金属不伤鼠**，要做也得打补丁。
- 去除规则的实现方式和加规则的办法，见第 3 节。
- 上一版的教训（详见 `docs/code-review-2026-11-10.md`）：自己另造一套污染物 tag 和成分表，会和原版的显示、倒液、过滤全部对不上。v3 直接读写原版的杂质组分 tag 是对的。

## 5. UI

- 机器窗口用官方构建器：`MachineHelper.CreateMachineryWindow(bool, params MachineryPanel[])`，面板有 `Panel(label, element, role)`、`PowerPanel`、`NotePanel`；元素有 `GameSlotInventory`、`GameGridInventory`、`GridPixelElement`、`TagElement`（文字标签）。**[文档]**
- **【核实】进度条有现成控件**（第 10 节第 6 项）：
  - **`MachineryProgressBar`**（public class，实现了 `PixelElement` 和 `MachineryWidget`）：构造函数 `MachineryProgressBar(float value = 0, int width = -1)`，可以链式调用 `SetValue(float)`、`SetColor(Color)`、`SetWidth(int)`、`SetFillWidth(bool)`，有只读属性 `value`。它是 `PixelElement`，所以可以直接放进 `MachineHelper.Panel(string label, PixelElement content, string role = null, bool styleInventories = true)`，也可以放进 `new MachineryPanel(label, content, role)`，再交给 `CreateMachineryWindow`。
    - 注意：在整个程序集里，**原版没有任何地方构造它**（引用它的只有它自己的构造函数）。也就是说这个控件能用，但原版没用过。显示效果和刷新时机 **[待实机]**。
  - **原版进度机怎么显示进度**：靠 tooltip，不靠窗口控件。`MachineProgressHelper.CreateMachineTooltip` 先检查 `PROGRESS_TYPE_MACHINE_TAG` 和 `PROGRESS_TYPE_DEFAULT_TOOLTIP`；状态是 `STATE_WORKING` 时，用 `MACHINE_PROGRESS_CURRENT_TAG / MACHINE_PROGRESS_TARGET_TAG` 调 `RichTextBuilder.AddProgressBar(float ratio, int length, ...)` 画一条文本进度条。水培、DNA 机、鱼缸健康和饥饿（`AquacultureHelper.AddHealthLine` / `AddHungerLine`）也是这么画的。
  - 自定义窗口：`CustomUIBuilder.AddProgressBar(float initial = 0, string id = null)`，背后是 `CustomUIProgressBar`，有 `SetProgress(float normalized)` 和 `SetLabel(string)`。
  - **建议**：机器窗口里放一个 `MachineryProgressBar`，同时在 tooltip 里用 `RichTextBuilder.AddProgressBar` 画一条，作为保底，挂在 `ModHook.OnCreateTooltipLate` 上。
- **检测结果显示**：原版成分仪 `ChemScannerUIManager` 会列出物品上每种 `Liquid` 的量和百分比。只要杂质用的是原版组分 tag，**不打任何补丁就能显示**。**[文档]**
- 自定义窗口：`CustomUIManager.Window(id, title, layer)` 链式构建器（标签、按钮、输入框）。原版弹窗：`GenericConfirmationUIManager.Instance.OpenPanelSingleButton`。**[文档]**

## 6. 联动对象的原版物品 ID

以下物品 id 是对照各个 `*Directory.InitDirectory` 里注册时用的字符串核实的（第 10 节第 5 项）。

| 对象 | 原版物品 id 和要点 | 状态 |
|---|---|---|
| 水 | 容器：`small_bottled_water`、`bottled_water`、`large_bottled_water`、`water_jug`、`water_pitcher`。水是容器里的 `water` 组分，**没有单独的"纯水物品"**，等级按 `water` 占比划分。设备和耗材：`water_purifier`（净水器，`MachinePurifier`）、`moisture_farm`（水分收集器，`MachineMoistureFarm`）、`portable_water_purifier`、`water_tablet`（净水片，含氯，`AmenitiesItemDirectory.WaterPurificationTablet`）、`water_tablet_container`、`water_filter`、`water_filter_adv`、`uv_filter`、`hydroreed_filter`；水箱 `water_tank`（`WaterTankHelper`）。 | **[核实]** |
| 两种激素 | **`serum_red`**（`HusbandryDirectory.SerumRed`，显示名取 `liquid_livestock_steroid`）和 **`serum_green`**（`HusbandryDirectory.SerumGreen`，显示名取 `liquid_livestock_immunity`）。<br>它们**不是液体容器**，是**次数型物品**：`UseCountHelper.InitUseCountItem(item, 10, …)`，可以用 10 次，`unitValue` 为 100。<br>用法是作用在带 `ANIMAL_ITEM_TAG` 的动物（老鼠）身上：每用一次扣一次次数，并执行 `IngesterHelper.Inject(animal, "livestock_steroid" 或 "livestock_immunity", 20)`。判断能不能用的函数是 `HusbandryDirectory.CanUseSerumOnAnimal`。<br>→ 和用户说的"养老鼠用的两种激素"对得上。模组用化学做出来的成品，应该产出这两个物品，用 `DirectoryMaster.Item("serum_red")` 生成即可，不需要新做激素物品。 | **[核实]** |
| 水培 | 机器 `hydroponic`（`MachineHydroponic`，按天推进 `GrowOneCycle`）。营养片 **`nutrient_tablet`**（`AmenitiesItemDirectory.HydroponicNutrientTablet`）是化学物品（`ChemicalHelper.InitChemItem`），带 `NUTRIENT_TABLET` tag，成分是 **`mineral` 15 ml**；放进水里会触发 `WaterHelper.OnHydroponicNutrientTabletAdded`。另有 `nutrient_tablet_container`，预装盒 `full_nutrient_tablet_box`（`PreBuiltItemHelper.FullNutrientTabletBox`）。营养液：`WaterHelper.FillWithNutrientSolution` / `PreBuiltItemHelper.CreateNutrientSolution`。杂质对植物的影响见第 4 节。 | **[核实]** |
| 养殖（养鼠） | `rat`、`animal_cage`（`AnimalCage.CreateAnimalCage`）、`livestock_feed_dispenser`、`auto_butcher`、`marking_pen`、`mouse_trap`、`vacuum_robot`、`feces_small` / `feces_medium` / `feces_large`（含 `microbe`）。逻辑在 `HusbandryHelper`（`InitAnimalItem` 等）。 | **[核实]** |
| 养鱼 | 系统叫 RAS：`ras`（`MachineRAS.CreateRAS`）、`ras_water_tank`。逻辑在 `AquacultureHelper`（`TickTank`、`ApplyWaterChecks`、`RunRemediation`、`RunFilter`）。<br>鱼种在 `FishSpecies` 里：`tilapia`、`gutter_tilapia`、`graft_tilapia`、`eel`、`electric_eel`、`sturgeon`，以及对应的 `*_roe` / `*_fry`。<br>饲料 `gutterflow_pellet`、`industrial_fish_feed`。产物有 `tiny_fish_fillet`、`small_fish_fillet`、`fish_fillet`、`large_fish_fillet`、`small_eel_fillet`、`eel_fillet`、`fish_scrap`、`caviar`、`sturgeon_roe`、`tilapia_skin_graft`。 | **[核实]** |
| 化学机器 | `blender`、`heating_plate`（`MachineHeatingPlate`）、`evaporator`（`MachineEvaporator`，模块 `evaporator_module_solvent_recovery`）、`chem_finisher`（`MachineChemFinisher`），都注册在 `FurnitureItemDirectory` 里。 | **[核实]** |
| 其它可能相关的组分 | `chlorine`（净水片）、`stabilizing_agent`、`lipobond_agent`、`reducing_agent`、`methylol`、`acid`、`base`、`mineral`（营养片的成分） | **[文档]** + **[核实]** |

## 7. 存档

- 物品状态存在 tag 里，随存档保存。读档时 `SaveManager.DecodeNodesInternal` 先用物品工厂重建模板，再用存档里的 `itemState` **整体覆盖** tag。也就是说：工厂里写的 tag 读档后以存档为准；工厂里挂的**委托（回调函数）不进存档**，每次都由工厂重新挂。**[文档]**
- **反应到一半存档**：如果进度存在机器的 tag 上（原版 `MACHINE_PROGRESS_*`，或自己的 tag），读档后自然恢复。上一版遇到过「读档后机器卡在 working」的情况，需要一个读档自检。**[文档]**
- 模组级的按存档数据：`ModHelper.Get/SetModData*`（键值对，存在本局存档里，主菜单不可用）。**[文档]**
- **【核实】卸载 mod 后读档会怎样**（第 10 节第 7 项）：**不会崩溃，模组物品会变成"未知"占位物品。**
  - `SaveManager.DecodeNodesInternal` 对每个存档节点调用 `DirectoryMaster.Item(identifier, true)`。
  - `DirectoryMaster.Item` 找不到模板时，会 `Debug.LogWarning("Could not find GameItem identifier '…' when attempting to create GameItem! Creating dummy GameItem instead...")`，然后返回 `ItemDirectory.CreateEmptyItem(id)`。这是一个 `GameItemElement`，贴图是 `Items/itematlas` 里的 `unknown`。
  - 之后 `DecodeNodesInternal` 照常用存档覆盖它的 `state` / `modifiedState`（tag 全部保留）、形状、数量、`uniqueId` 和价格，名字和描述经过 `LocHelper.RelocalizeIfMissing`。所以重新装回 mod 后，物品数据还在；但占位期间，工厂挂的委托和功能都没有。
  - **风险：模组机器里装的东西**。读子物品时，如果父物品的背包在字典里找不到，会打 `"Warning, child item load failure, Item ID:… Inv ID:… Parent ID:…"`；如果背包无效，会打 `"Warning, GameInventory is invalid…"`。占位物品没有模组机器的格子，**卸载后，放在模组机器里的物品很可能丢失**。这一点是静态读法，**[待实机]**。
  - 如果本模组只往**原版**容器里写原版组分 tag（v3 的方案就是这样），卸载后这些容器完全正常。

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
   **【核实后更新】**具体的补丁清单：
   - **必需**：给 `ChemicalHelper.HandleTemperatureReaction` 挂一个 Postfix，用来跑本模组的反应规则。白天和夜间 360 轮都会经过这里（第 3 节）。
   - **重金属伤鱼**（可选）：给 `AquacultureHelper.ApplyWaterChecks` 或 `GetWastePercent` 打补丁，因为原版只看 `organic_waste`。
   - **重金属伤鼠**（可选）：原版完全没有相关逻辑，要找一个 `HusbandryHelper` 里的每日或每次喂食的入口来补。
   - **重金属伤植物：不用补丁**，原版 `HydroponicHelper.HandleContaminant` 已经实现了。
   - **原料杂质**：本模组自己的新原料可以直接调用原版的 `PreBuildChemHelper.SetImpurity(item, min, max)`（public），不需要补丁。"偏硬核"的设定就是把 min / max 调高。只有想改原版试剂瓶的杂质范围时，才需要补丁。
3. **配方表用生成的 C# 源文件，不在运行时读 JSON**（见第 2 节的 Capabilities 约束）。`recipes.json` 仍然可以作为生成器的中间产物和校验对象。**【核实后补充】**如果以后想让配方不重新编译也能改，可以用 `ModHelper.ReadModFileText(modId, "recipes.json")` 读模组文件夹里的文件。按静态阅读，这不需要声明能力（第 2 节）。
4. **不注册自定义 `Liquid`**。上一版往 `Liquid.Liquids` 里加了 42 种成分，每个原版容器都会多出 42 个 tag。v3 的新物质如果只是固体原料，做成普通物品即可；确实需要新液体组分时再逐个评估。
5. ~~**反应时间**优先复用原版 `MachineProgressHelper`~~ **【核实后改为】反应时间不用 `MachineProgressHelper`。**原版进度机是挂在 `onCycleEndSlotItemFunc` 上的，**每夜只推进一次**；原版化学则是**每秒一次，夜里再补 360 秒**（第 3 节）。要"跟原版化学的反应效率走"，就应该挂在容器的每秒 tick 上（第 2 点里的 Postfix）：
   - 反应时间 = 需要的 tick 数，按 1 tick = 1 秒（受 timeScale 影响）算；夜里补的 360 轮相当于 6 分钟。原版每条规则每秒处理多少，在各个 `Handle*` 的常数里，比如氯每秒最多 15000 part。生成器的全局缩放系数可以按这些常数来定。
   - 进度可以存在容器的 tag 上（读档能恢复），用 `MachineryProgressBar` 或 tooltip 的 `RichTextBuilder.AddProgressBar` 显示（第 5 节）。
   - 如果做本模组自己的反应机器，机器本身也用 `onOneSecondItemFunc`，参照加热板 `MachineHeatingPlate.HeatingPlate` 的写法，不要用 `onCycleEndSlotItemFunc`。

## 10. 需要在有游戏文件的机器上核对的清单

按重要程度排序。第 1–7 项已于 2026-11-10 静态核对完毕，结论写在对应章节里：

1. ✅ 原版反应规则在哪个类里，有几条，怎么被调用；能不能往里加规则。→ **第 3 节**：`ChemicalHelper.HandleTemperatureReaction` 里硬编码了 22 条；可以加，给它挂一个 Postfix 就行。
2. ✅ 原料杂质的生成代码：随机范围、哪些物品带杂质。→ **第 4 节**：试剂瓶用 `PreBuildChemHelper.SetImpurity`，范围 2%–8%；水按等级随机、比例固定；蘑菇泥等是固定比例。
3. ✅ `onCycleEndSlotItemFunc` 的触发节奏。→ **第 3 节**：每夜一次；化学用的是 `onOneSecondItemFunc`，每秒一次，夜里再补 360 轮。
4. ✅ manifest 的 Capabilities 有哪些可选值；读文件的 API。→ **第 2 节**：一共 9 个能力；读模组自带文件用 `ReadModFileText`，按静态阅读不需要能力。**[待实机]**确认一下扫描器。
5. ✅ 两种激素、水培、养殖、养鱼的物品 id。→ **第 6 节**：激素是 `serum_red` / `serum_green`；营养片是 `nutrient_tablet`；鱼缸是 `ras`。
6. ✅ 机器窗口里的进度条控件。→ **第 5 节**：有 `MachineryProgressBar`，原版没用过；原版进度条走的是 tooltip 里的 `RichTextBuilder.AddProgressBar`。
7. ✅ 卸载模组后读档。→ **第 7 节**：模组物品变成 `unknown` 占位物品，不会崩溃；模组机器里装的物品可能丢失。**[待实机]**
8. ⏭ 联机 mod：用户已确认跳过。

**仍需进游戏实测的点**，汇总如下：
- 暂停或打开菜单时，每秒 tick 是否停止（程序集里没有修改 `timeScale` 的代码）。
- 声明为空的 `<Capabilities />`、只调用 `ModHelper.ReadModFileText` 的模组能不能通过扫描。
- `MachineryProgressBar` 放进机器窗口后的显示效果和刷新方式。
- 卸载模组后，模组机器里装的物品会不会丢。

## 用户已确认（2026-11-10）

1. **联机 mod 不用管。** 阶段 0 第 8 项（联机同步）和推进顺序里的「联机验证」都跳过；设计上仍保持结果确定、状态存在物品 tag 上，但不为联机做专门工作。
2. **养殖 = 养老鼠**；两种激素是养老鼠用的生长激素。原版组分里的 `livestock_steroid`、`livestock_immunity` 是最可能的对应项，物品 id 和用途仍需在游戏文件里核对（第 10 节第 5 项，搜老鼠相关的类和物品）。按计划书的内容边界，激素只做虚构 / 抽象配方。→ **已核实**：就是 `serum_red` / `serum_green`，详见第 6 节。
3. **反应时间不要太长，跟原版的反应效率走。** 所以优先复用原版机器的进度机制和节奏（第 10 节第 3 项的核对结果决定具体数值），生成器的全局缩放系数按原版同类机器的耗时来定。→ **核对后改为**：跟原版**化学**的节奏走，也就是每秒 tick 加夜间 360 轮，不用按天推进的进度机。详见第 9 节第 5 点。
4. **杂质偏硬核。** 原料杂质比例取原版范围的上沿或更高，纯水等联动产品的合格线定得严，需要多步提纯才能达标。
5. **旧版代码放弃**（修复 PR 已关闭），**旧贴图保留**：`FreeChem/Textures/Items/` 和生成脚本 `tools/gen_icons.py` 可以在 v3 里复用。

游戏安装路径（2026-11-10 核对时）：`C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen`。不再是旧文档写的 `D:\SteamLibrary\…`。`tools/api-probe` 里的 `DefaultAssemblyPath` 还是旧路径，运行时需要通过第一个参数传入新路径。
