# v3 最小版本（阶段 2）设计与测试步骤

计划书见 `v3/HANDOFF.md`，侦察结论见 `v3/RECON.md`。本文只写阶段 2（"查表 + 计时 MVP"）实际做了什么。

## 做了什么

三条配方，全部作用在容器里的**原版液体组分**上，不新增任何物质：

| 配方 | 输入 → 输出 | 温度档 / 用时 / 转化率 | 为什么 |
|---|---|---|---|
| `hydroxide_precipitation` 氢氧化物沉淀 | 重金属 1 + 碱 2 → 碎屑 3 | 15–45°C：120 秒、90%；45–70°C：60 秒、75% | 原版**没有任何**化学手段除重金属（RECON 第 4 节）。碎屑（`debris`，粒径 200）能被滤芯拦住。温度高时部分沉淀回溶，所以转化率降低 |
| `sulfide_polishing` 硫化物精除 | 重金属 1 + 还原剂 1 → 碎屑 2 | 20–50°C：150 秒、99% | 氢氧化物法除不干净，硫化物法更彻底但更慢 |
| `lime_softening` 石灰软化 | 矿物质 2 + 碱 1 → 碎屑 3 | 15–60°C：90 秒、85% | 去水里的钙镁硬度 |

偏硬核的体现：一轮只转化 75%–99%，要多轮；重金属低于 1 ml（1000 part）后不再反应（残留下限，仿原版氯的 0.7% 下限）。

## 怎么运行

- **挂钩点**：`ChemicalHelper.HandleTemperatureReaction(GameItem)` 的 Harmony Postfix（`src/ReactionPatch.cs`）。白天每秒一次；夜间游戏对同一回调连续调用 360 次，所以夜间补算自动包含本模组。
- **一个容器同一时间只进行一个反应**，按配方表顺序先匹配先开始。
- **进度**存在容器自己的 tag 上（`fc3_rxn` = 配方 id，`fc3_prog` = 已累计秒数），所以中途存档、读档后继续。
- **暂停策略（实现时选定的一种）**：温度离开温度档 → **暂停并保留进度**，回到任一温度档继续；不会按新温度重算剩余时间。投料不够 → 进度清零。
- **结算**：`extent = min(投料 ÷ 系数) × 转化率`，按系数扣输入、加输出，总体积守恒。
- **显示**：悬停容器，tooltip 里有配方名和文本进度条（RECON 第 5 节的保底方案）。机器窗口的 `MachineryProgressBar` 留到有自己的机器时再用。

## 文件

| 文件 | 作用 |
|---|---|
| `v3/recipes.json` | 配方源数据 |
| `tools/v3/gen_recipes.py` | 校验并生成 `src/Recipes.g.cs` 与 `FreeChem/Localization/{zh,en}.csv`。校验：id 重复、未登记物质、系数、体积守恒、温度档重叠 / 缺口、时间、转化率、英文名 |
| `src/ReactionEngine.cs` | 纯逻辑，不引用游戏类型 |
| `src/GameApi.cs` | **全部**游戏 API 调用都在这里 |
| `src/ReactionPatch.cs` / `src/ModEntry.cs` | Harmony 补丁 / 入口与 tooltip |
| `tools/v3/engine-tests/` | 引擎离线测试（34 项） |
| `tools/v3/test_gen_recipes.py` | 生成器反例测试 |
| `legacy/` | 旧版代码和资源，只作参考，不参与构建 |

## 已经验证的（不需要游戏）

```bash
python3 tools/v3/gen_recipes.py --check
python3 tools/v3/test_gen_recipes.py
dotnet run --project tools/v3/engine-tests -c Release
```

引擎测试覆盖：基本流程与体积守恒、残留下限、多轮提纯、两个温度档、暂停保留进度、投料被取走中止、**中途存档读档**、存档里的配方被删除、夜间 360 次。

## 还没有的（需要在有游戏的机器上做）

1. **编译**：`dotnet build src/FreeChem.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen\\"`。只用游戏接口的替身做过类型检查。
2. **【待核】容器温度怎么读。** `src/GameApi.cs` 的 `GetTemperature` 现在返回 26°C 兜底，所以温度档暂时形同虚设。查法：在 IL 里看 `ChemicalHelper.HandleChlorine`、`HandleBoilingReaction`、`HandleAmbientTempDrop` 怎么取温度，照抄；写温度的是加热板 `HeatLiquidOnStove`。
3. **`Liquid.GetCurrentPartFromId` 是不是 public static**：RECON 里 `HandleChlorine` 在用，旧文档也写过，但没有单独核对可见性。
4. **`WaterFeatureHelper.OnLiquidChanged(container, bool)` 和 `GetWaterFeature(container).IsExposed()`** 的签名来自 RECON 的 IL 描述，没有用编译验证。

## 实机测试步骤

先做 1–2 项的核对并编译，再进游戏。

**单机**
1. 把 `src/bin/Release/netstandard2.1/FreeChem.dll` 复制到仓库 `FreeChem/`，再把整个 `FreeChem/` 放进游戏 `Mods/FreeChem/`（先备份旧的）。启动游戏，确认 Mods 菜单里 FreeChem 已启用，日志里有 `FreeChem v3 initializing: 3 recipes.`。
2. 准备一个同时含重金属和碱的容器。最简单：把一瓶**原版碱性溶液瓶**倒进一个空水壶（碱瓶带 2%–8% 随机杂质，其中有 `heavy_metal`；重金属要 ≥ 1 ml 才会触发，碱瓶太少的话换一瓶）。用成分仪确认里面有重金属和碱。
3. 悬停水壶：应出现「化学反应：氢氧化物沉淀」和进度条。等 120 秒（可以加速或睡一觉，夜里会补 360 秒）。完成后再用成分仪看：重金属少了约 90%，不溶碎屑（debris）多了；总液量不变。
4. 反复做直到重金属低于 1 ml，确认不再触发。把含碎屑的水倒过滤芯，确认碎屑被滤掉。
5. **中途存档**：反应进行到一半（比如 60 秒）存档退出，再读档，悬停容器，进度应该还在 60 秒附近，并继续走到结束。
6. 石灰软化：往一个含矿物质的水里加碱，同样观察。
7. 记下：打开菜单 / 暂停时进度会不会继续走（RECON 的待实机项）。

**卸载**
8. 在容器反应到一半时存档，退出，把 `Mods/FreeChem` 改名，启动游戏读档：不应崩溃；容器是原版物品，里面的液体组分原样保留，只是不再有反应。重新装回后读档，进度继续。

**联机**：按用户决定，跳过。

## 阶段 4a：重金属滤渣（直接产出，不新增机器）

用户决定：滤渣在**过滤时直接产出**，尽量少造机器。

**记账**：重金属沉淀产出的碎屑和别处来源的碎屑混在一起，所以容器上记一本账：
- `fc3_msludge`：碎屑里有多少 part 是重金属沉淀（`sludge: true` 的配方结算时增加，目前是氢氧化物沉淀和硫化物精除）；
- `fc3_msludge_rem`：不足一件的零头。

**换算**（`SludgeLedger`，纯逻辑，已测）：过滤滤走 `removed` part 碎屑（滤前共 `before`）时，账上 `removed/before` 的比例转出，每 5000 part（5 ml）换 1 件 `metal_sludge`，零头留到下次。账比碎屑多时按碎屑总量封顶；总量守恒。
物品：`metal_sludge`（暂用旧版 `sludge` 的 1×1 贴图，价值 40），放到后柜台，放不下时弹原版「柜台已满」提示，不静默丢失。

**还差：谁来调用 `GameApi.OnDebrisRemoved(container, removedDebris, debrisBefore)`——过滤钩子。** 资料里只知道：
- 滤芯容器带 `FILTER_CONTAINER_TAG`，`WaterHelper.HandleFilter(...)` 在倒液 `TransferLiquid` 时被调用（见 `docs/feedback-research.md` 1.1）；
- `Liquid` 有 `filterVolumeRemoved`、`filterDurabilityUsage`、`filterMinimumPurity` 字段；碎屑 `debris` 粒径 200，滤芯孔径 22，所以滤得住。

本地 Claude 需要读 IL 回答（写回本文件）：
1. `WaterHelper.HandleFilter` 的签名和逻辑：滤掉的碎屑是被直接删除、还是搬到别处？滤前滤后碎屑量在哪里可以读到？
2. 有没有别的过滤入口：`MachinePurifier`（净水器）、`WaterHelper.OnUVUsed`、鱼缸的 `RunRemediation` / `RunFilter`。要不要都接？（只接倒液过滤是最小方案。）
3. 钩子打在哪一个方法上最稳：Prefix 里记下 `debris` 滤前的量，Postfix 里读滤后的量，差值就是 `removedDebris`。
4. 倒液把含碎屑的水倒进另一个容器时，滤渣账不会跟着走（账在源容器上）。是否要在 `TransferLiquid` 的 Postfix 里按比例把账搬到目标容器？（建议要，否则倒一次液账就断了。）

接好钩子之前，滤渣账会一直累积但不会产出物品，其它部分不受影响。

## 阶段 4b：杂质偏硬核（用户要求）

- **做法**：给原版 `PreBuildChemHelper.SetImpurity` 挂 Harmony Prefix（`src/ImpurityPatch.cs`），把杂质范围放大到 150%：酸、碱、还原剂、脂合剂、芬达前体瓶的 2%–8% 变成 **3%–12%**，封顶 20%（`ImpurityScale`，纯逻辑，已测）。迷你酒精瓶不调用 `SetImpurity`，不受影响。
- 用位置参数 `__1` / `__2`，不依赖参数名；方法本身是 public static，RECON 已核对。
- **模组设置里有开关「杂质偏硬核」**（默认开）。这会改变原版试剂瓶的杂质，所以做成可关的。只影响之后生成的瓶子。
- 这样一来，碱瓶和还原剂瓶里的重金属更多，正好是本模组三条配方的原料。
- **没做的**：原版水的杂质（按水的等级随机，比例固定，在 `WaterPremadeHelper` 的字典里）没有动。要改的话得补丁 `WaterHelper.AddWater`，等有具体需求再说。

## 阶段 5（部分）：反应时间由生成器按温度算

- 配方写 `kinetics: { EaKJ, tau25 }`，档里不再手写 `time`。生成器算：`time = tau25 × exp(Ea/R × (1/T − 1/298.15))`，T 取该档里最接近室温的温度（包含 25°C 的档用 25°C，高于室温的档用下限，保守）。最短 15 秒，再乘 `timeScale`。档里仍可手写 `time` 覆盖。
- Ea 和 tau25 是**游戏设计值，不是文献值**。目前：氢氧化物沉淀 50 kJ/mol、25°C 时 120 秒 → 45°C 以上档 34 秒；硫化物精除 40 kJ/mol、150 秒；石灰软化 35 kJ/mol、90 秒。
- 校验：缺 time 又缺 kinetics、Ea 不在 (0, 200]、tau25 ≤ 0 都会报错；测试含与手算对照、"温度越高越快"。
- **还没做**：把转化率也由平衡常数算出来；经济检查（现在只有一种物品有价值，没有可检查的循环，等有了联动产品再做）。

## 联动内容：还缺的原版信息（下一步要本地 Claude 先查）

阶段 6 的纯水、营养片、激素、养鱼用品，现在**不能凭空做**：前面的配方只会改容器里的液体组分，而这些产物里有的是物品（`nutrient_tablet`、`serum_red` / `serum_green`）。需要先确认：

1. **营养片 `nutrient_tablet`**：原版怎么获得（商店？`chem_finisher` 压片？）；`MachineChemFinisher` / `ChemFinisherRecipe` 能不能压任意化学容器，模组能不能加压片配方。如果原版成型机已经能把含 `mineral` 的液体压成营养片，那就只需要让玩家能配出含矿物质的营养液，不用造新物品。
2. **激素 `serum_red` / `serum_green`**：原版怎么获得，用 `DirectoryMaster.Item("serum_red")` 能不能直接生成。
3. **纯水**：原版 `water_purifier`（`MachinePurifier`）已经能去重金属，把水净化到什么等级；`PureWater` 档要求 `water` 占比 ≥ 99.9%，而氯的化学杂质下限是 0.7%，所以要达到纯水档还缺一步去化学杂质（计划书里的活性炭）。原版有没有现成的"炭"类组分或物品可以用？
4. **养鱼 / 养鼠**：重金属伤鱼要给 `AquacultureHelper.ApplyWaterChecks` / `GetWastePercent` 打补丁；伤鼠要找 `HusbandryHelper` 里的每日入口。需要确认补丁点和影响的幅度。

确认之后，引擎要加一个能力：配方结算时除了改组分，还能产出物品（和滤渣用同一套"放后柜台"代码）。这部分等确认了再写，避免凭空猜。
