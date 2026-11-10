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
| `tools/v3/engine-tests/` | 引擎离线测试（23 项） |
| `tools/v3/test_gen_recipes.py` | 生成器反例测试（14 项） |
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

## 下一步（阶段 4 起）

- 重金属滤渣物品：这是计划书里"唯一新增的杂质类物品"。需要决定怎么从含碎屑的水里"提取"出来（过滤时产出？还是新机器？），这一点要先和用户商量。
- 原料杂质：自己的新原料用 `PreBuildChemHelper.SetImpurity(item, min, max)` 带杂质，"偏硬核"就是把范围调高。
- 离线生成器：用 Arrhenius 算出反应时间和转化率，替换目前手写的数值，并加经济检查。
