# FreeChem 项目交接卡 v0.7.0（可直接作为其他 Agent 的上下文引用）

> 用法：把本文件整段复制给接手的其他模型 / Agent，它无需任何会话历史即可理解全局。
> 本文件是**事实描述**，不是提案。所有「已完成 / 未完成」均按 **2026-11-10 12:35 的真实磁盘状态**标注。
> 配套文件：`docs/api-findings.md`（反编译核对结论 + Lead 决策）、`docs/参考资源索引.md`（所有原始依据）、`docs/设计说明.md`、`docs/进度报告.md`。建议一起给接手方。

---

## 0-A. 工作区绝对路径速查

**Windows 原生路径**

| 用途 | 绝对路径 |
|---|---|
| 工作区根 | `C:\Users\Administrator\WorkBuddy\Claw` |
| **项目根** | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod` |
| 本交接卡 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\HANDOFF.md` |
| API 核对结论（含 IL 证据） | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\docs\api-findings.md` |
| 参考资源索引 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\docs\参考资源索引.md` |
| C# 源码目录（7 个 .cs） | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\src` |
| 项目文件 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\src\FreeChem.csproj` |
| 发布包（Mod 本体，105 文件） | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\FreeChem` |
| 清单 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\FreeChem\manifest.xml` |
| 物品图标（98 张 PNG） | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\FreeChem\Textures\Items` |
| 本地化（298 条 ×2） | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\FreeChem\Localization\zh.csv` / `en.csv` |
| 掉落实 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\FreeChem\LootTables\free_chem_drops.json` |
| 纸质手册 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\FreeChem\Books\chem_codex.json` |
| 图标 / 本地化生成器 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\tools\gen_icons.py`、`gen_localization.py` |
| 静态验收 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\tools\validate.py` |
| 文本层逻辑自检 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\tools\logic_check.py` |
| C# 纯逻辑校验工程 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\tools\logic-harness\LogicHarness.csproj` |
| **API 探针（只读元数据/IL）** | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\tools\api-probe`（`run.ps1` 可复现） |
| 官方范例仓库（clone） | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\ref-modding` |
| 官方中文指南 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\ref-modding\ModdingGuide.zh-CN.md` |
| 原版图标对照素材 | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\ref-icons` |
| 图标总览图（98 格） | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\icon_sheet.png` |
| v1 备份（勿当现行版本） | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\FreeChem_v1_backup`、`tools/gen_icons_v1.py` |
| 本地 .NET SDK | `C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\.dotnet\dotnet.exe`（8.0.427） |
| **Bundled Python** | `C:\Users\Administrator\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe` |
| **游戏安装目录** | `D:\SteamLibrary\steamapps\common\Probably Stolen` |
| 游戏 Mods 目录（已同步） | `D:\SteamLibrary\steamapps\common\Probably Stolen\Mods\FreeChem` |
| **开发工具开关（F8/F9 必需）** | `D:\SteamLibrary\steamapps\common\Probably Stolen\dev.txt`（空文件；本机已建，不随模组发布） |
| 游戏程序集（API 核对用） | `D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed` |
| 主程序集 | `...\Managed\Assembly-CSharp.dll` |

> ⚠️ 本机系统 PATH 里**没有可用的 `python`**（只有 Microsoft Store 占位符）。所有 Python 命令必须用上面的 bundled 绝对路径。
> Windows 路径含空格（`Probably Stolen`），bash / 引号要小心。相对路径基准一律是**项目根** `ps-chem-mod`。

---

## 0. 一句话定位

为 Steam 游戏《Probably Stolen（深空当铺：可能是偷的）》开发的 C# Mod **FreeChem · 自由化学**（`netstandard2.1`，官方内置 ModLoader，Mod ID `free_chem`，ModVersion **0.5.0**，`<GameVersions>0.46</GameVersions>`）。

当前状态：**四层静态验收全绿、发布包已全量同步到游戏 Mods 目录并逐字节校验一致**；但**尚未进游戏实机验证**（模组加载 / 游戏内功能 / 存档重载 / 运行时换图观感 / 快捷键与调试窗口 / 后柜台生成与满仓弹窗 / 原版治安部写入的实际平衡）。

最终门禁（2026-11-10 09:45 实测，Lead 亲自复跑；DLL 同步后 validate 的同步 WARN 已消失）：

| 层 | 命令 | 结果 |
|---|---|---|
| 编译 | `dotnet build -c Release -p:GameDir=...` | **0 警告 0 错误**，`FreeChem.dll = 133,120 bytes` |
| 文本逻辑自检 | `tools/logic_check.py` | **44 条反应 / 763 项断言全通过** |
| C# 数据层校验 | `tools/logic-harness` | **276 项 [PASS] / 0 [FAIL]**，「结论：全部通过」 |
| 全量静态验收 | `tools/validate.py` | **76 PASS / 1 WARN / 0 FAIL** |

唯一 WARN 已解释：`tools/gen_icons_v1.py`（v1 归档备份、不在发布包、不参与构建）含废弃物 token；发布包与现行源码零残留。

---

## 1. 环境与硬约束（先读）

| 项 | 值 |
|---|---|
| 游戏安装目录 | `D:\SteamLibrary\steamapps\common\Probably Stolen` |
| Steam Build | **26525552**（`appmanifest_4348910.acf` 实测） |
| 游戏版本（`Application.version`） | **0.46**（`globalgamemanagers` 的 PlayerSettings bundleVersion 实测 `0.46D` → 规范化 0.46） |
| 目标框架 | `netstandard2.1`，`LangVersion=latest` |
| 模组 ID | `free_chem` |
| 当前版本 | manifest `0.5.0`；`<GameVersions>0.46</GameVersions>`；描述已中文化 |
| 发布包 | `FreeChem/`，105 个文件（DLL + manifest + preview + 98 图标 + 2 CSV + 1 掉落 + 1 手册） |

**绝对约束（违反会导致 Mod 被扫描器拒绝或崩溃）：**
1. `<Capabilities />` 必须保持为空 → 禁止 `System.IO.File`、`System.Net.*`、`new Thread`/`Task.Run`/`Task.Delay`、`Process.Start`、`DllImport`、`Assembly.Load`、反射外部类型。实时逻辑只能用 `ModHelper.StartCoroutine` + `WaitForSeconds`。
2. 不得臆造未核对的 API。已核对结论见 `docs/api-findings.md`（含 IL 证据）。任何新 API 必须先在 `Assembly-CSharp.dll` 里核对成员名与可见性再用（`tools/api-probe` 可复现）。
3. `Init`/`OnEnable` 在主菜单运行，**不能**读存档、生成物品、访问游戏内管理器。物品注册只能在 `ModHook.OnModItemDirectoryInit`；游戏内钩子在 `OnEnable` 订阅、`OnDisable` 取消。
4. `SetSpriteAndShapeFromMod(modId, spriteKey)` 会按图片**不透明像素推导背包占格**，图片尺寸即占格；网格基数 16px。**运行时换图**也用它（见 §3.7），spriteKey 是**裸文件名**（`gas_scrubber_on`，不是 `free_chem:xxx`）。
5. 官方**不支持**的：注册新的混合物组分、新顾客精灵图/阵营、改动现有 UI 或商店核心循环、大修型模组。因此新药剂是**独立瓶装物品**，无法注册进原版「液体组分表」，成分光谱仪不会把 HCl 显示为组分。
6. 原版治安部 heat **没有官方 API**。本项目采用程序集层面核对过的未文档化成员 `PlayerStore.Instance.secData.CommitCrime(SecData.ENDANGERMENT, n)`（`PlayerStore.HasInstance` guard + try/catch + 可关闭配置），**不得**说成官方支持。

---

## 2. 需求全史（用户共 13 轮反馈，逐条）

| # | 用户要求 | 状态 |
|---|---|---|
| 1 | 读官方 AI 辅助开发指南、下载 GitHub 官方范例、设计化学 Mod、画图标、气液固三态、机器/试剂/工具/仪器、自由化学 | 完成 |
| 2 | 提供游戏安装目录 | 完成 |
| 3 | 参考原版机器图标与已有物品；「我列的六类试剂原版都有，需要的是扩展」 | 完成（原版 16px 网格风格） |
| 4 | **不是**用原版药剂合成新东西，而是**新增和原版不同的基础药剂** | 完成 |
| 5 | 贴近真实化学、具体药剂与设备、不重复造轮子、设备必须兼容原版容器/电力/模组 | 完成（`CHEMICAL_SUPPLIES` + 原版机器框架） |
| 6 | 气罐容器；复用原版致命/毒性/成瘾；王尔德服务加气体商人；纯度分级（工业/医用/化学纯氧）卖给原版人物 | 完成 |
| 7 | 多级合成链；与原版药剂反应生成虚构违禁品 | 完成（T1–T4 + 4 个虚构违禁品） |
| 8 | 滗析：静置沉淀一天取上层清液提高纯度 | 完成 |
| 9 | 气体无法滗析、注意合乎逻辑、检查几遍 | 完成（三层校验 + 反例矩阵） |
| 10 | 滗析无法分离游离污染物、无法完全提纯；化学滤纸、烧杯/鹅颈瓶；大型加热台 + 酒精灯 + 灯芯；燃料可用原版酒精且纯度影响加热效率；加热与加热后反应实时、参考原版加热板、用协程 | 完成 |
| 11 | 氧气罐图标太小；大型气罐（类似原版水罐）与钢瓶区分并分配占格；精绘；机器**空闲/工作双状态图标**；**去除防毒面具**；王尔德加**排风服务/进阶排风服务**，毒气排放提高**治安部嫌疑度**，进阶大幅削减 | **完成（本轮，含图标+代码+文案+验收）** |
| 12 | （集成时发现并修复的真实缺陷）温度档无交互入口导致 T2–T4 配方不可达；大量器材没有任何获取途径 | 完成（thermometer 温度计 + `free_chem_apparatus` 掉落表 + 起始/发明家/订单来源） |
| 13 | （Lead 决策）毒气排放真正写入原版治安部，而非只做模组内计数 | 完成（`SecData.ENDANGERMENT`，未文档化成员，默认开启、配置可关，如实标注未实机验证） |
| 14 | 模组设置入口 + 中文描述；设置里有调试选项；给所有物品私有数字 ID；游戏内调试窗口输入指令按数字 ID 生成物品，物品进**后柜台**而非售货柜台；后柜台放不下时用原版风格窗口提示「柜台已满，无法加入新物品。」 | **完成（第 14 轮）**：`manifest` 描述中文化、`debug_mode` 配置、F9 调试面板入口 + 自定义输入窗口、1–85 数字 ID 表、后柜台生成（`CanAccept` 早探测 + `TryAcceptAllOrDestroy` 兜底）+ **原版 `GenericConfirmationUIManager.OpenPanelSingleButton` 满仓弹窗**（自建 meta 窗口仅兜底） |
| 15 | （用户实测反馈）F9 调试面板里没有生成按钮 → 改为**快捷键**打开生成窗口 | **完成（第 15 轮）**：`ModHelper.AddGameOptionKeyBind` 注册到游戏「选项 → 按键绑定」（默认 **F10**，可重绑，`PlayerPrefs` 持久化）；每帧协程轮询 `Input.GetKeyDown`（`SuppressHotkeys`/场景 guard/`IsOpen` 切换/`StopCoroutine` 清理，无线程）；**不依赖 dev.txt**；F9 降级为开发机备用。csproj 为此新增 `UnityEngine.InputLegacyModule` 引用（`Private=false`，不捆绑 DLL） |
| 16 | （用户实测反馈）模组在「已安装模组」列表报错 + 设置面板没有中文 | **完成（第 16 轮热修复 v0.5.2）**：根因 = `OnEnable` 在主菜单调用 `AddNetworkUpgrade` → `PlayerStore.Instance` 惰性构造 → `PlayerStore..ctor` NRE → Init 失败、模组被禁、Localization 未加载（设置项显示 `cfg_*`）；修复 = Wild Network 注册移到 `OnGameLoaded`（`PlayerStore.HasInstance` guard + try/catch + 重试标志），OnDisable 清理加 try/catch；设置面板分区名改中文「玩法」「调试」；顺带修复 ModEntry.cs 的 UTF-8 mojibake（工具链事故） |
| 17 | （用户 9 条问题，已授权 Harmony/纯度重做）①小件占格②成分仪可分析并显示成分③可倒入原版容器④取消现成医用/化学纯终产物，改原版纯度分级 + 标签打印机，允许 100% 但需复杂提纯⑤化学品不足/仪器不可用⑥工具器材不应有欣快等级⑦商人逻辑⑧量纲⑨反应式与条件 | **P0 完成（v0.6.0）**：占格修正（5 件仪器 16×16、6 件器材 16×32）、只给真药剂打 tag/画像（欣快仅成瘾品）、运行期相态过滤 + 原版药剂 `VanillaAnalog` 兼容、订单改后柜台 + owner/source、量纲统一（嫌疑度=原版写入量、250ml/件）、仪器状态 tooltip + 机器窗口中文标签。**P1/P2 待做**：标签打印机、100% 纯度链、化学品扩充、39 条反应式重写 |
| 18 | （P1a）成分仪分析/倒入原版容器/原版七档纯度/收购 | **完成（v0.6.1）**：真药剂补 `CHEM_ITEM_TAG`+`CHEMICAL_PRODUCT_TAG`+纯度 tag+`SetPurityFeature`（七档 99/90/80/60/45/30）；液体成为原版液体容器（`InitLiquidContainerItem`+`AddLiquid`，250ml/件守恒，10 条映射；双氧水→水近似）；新增 `src/ChemPatches.cs` 6 个 Harmony 兜底补丁；`ChemRecipes.PourParts`/`ChemRuntime.PurityOf`。**未实机验证** |
| 19 | （P1b1）标签打印机 + 取消现成医用/化学纯终产物 | **完成（v0.6.2）**：新物品 `label_printer`（数字 ID **86**、32×32、掉落 apparatus/toolTable）；target 三钩子（拿打印机点本模组药剂）→ `ChemPrinter.Stamp`：激活循环 **14 模式**（7 档 × 化学品/医疗品），`SetPurityFeature(real, fake)` 真实/标签分离，医疗标签 `SetGameItemType(MEDICAL)+fc_medical`；灌装台改为**只产工业钢瓶**（can_*_medical/ultrapure 保留注册作 legacy 但不再产出），纯度由`SyncVanillaPurity`同步原版 tag/七档。**100% 纯度留 P1c**。未实机验证 |
| 20 | （P1c）多级提纯 + 打印机校准实现 100% 纯度 | **完成（v0.6.3）**：`fc_purify_level` 0–5，过滤台每次过滤 +1 级；`PurifyFloor(level)=70>>level`，纯度上限 94.40%→**99.84%**（仍 <100%）；打印机第 **15** 模式「校准」：校准模式下点 `catalyst_kit` 充能（tag `fc_catalyst_charge` ≤3、消耗套件）→ 点纯度 ≥99.5% 药剂 → 污染物清零、level5、100%、原版 `CreateLabPure()`（real=fake）+ `SyncVanillaPurity`，消耗 1 充能；失败两分支；校准不调 GradeProduct。**未实机验证** |
| 21 | （P2a）化学品扩充 + 反应式/条件逐条重写 | **完成（v0.6.4）**：新增 10 种化学品（**ID 87–96**：生石灰/熟石灰/纯碱/小苏打/氯化铵/MnO₂/CuO/K₂SO₄/NaNO₃/K₂MnO₄，物品 83、图标 **109**、本地化 **382**）；修正 11 条反应（CO₂/双氧水/结晶补水、KMnO₄ 副产物、铵盐放氨副产物、**次氯酸盐改用熟石灰**、中和产盐水、吸收塔补水、硝石制硝酸配平+硫酸钾）；新增 5 条（石灰煅烧/熟化/纯碱/小苏打/氯化铵）；反应 39→**44**；equation 统一含「→」、T1–T3 不用原版药剂、全部 inputs/outputs 注册断言。**未实机验证** |

---

## 3. 原版机制事实库（已查证）

来源：`probably-stolen.nicokobo.com` 中文 Wiki；CDN `cdn.nicokobo.com/ps/items/*.webp`；本机程序集静态解析；`tools/api-probe` 元数据/IL 探针。

**3.1 原版化学是「液体容器里的组分」体系**
- 组分：酸 / 碱 / 水 / 酒精 / 脂合剂 / 还原剂 / 稳定剂 / N-羟甲基碱 / 氯 等，以 μl 计量，按温度驱动 22 项反应。
- 原版设备（**不要重复造**）：搅拌机 `blender`、加热板 `heating_plate`、蒸发器 `evaporator`、化学制剂成型机 `chem_finisher`、成分光谱仪 `chem_scanner`、熔炉、净水器、水分收集器、冰箱、3D 打印机、催陈器、水罐 `water_pitcher`。
- 原版已有器材：滴管、量筒、50mL 量筒、滤芯、紫外线灯、净水药片、工具箱。**没有**烧杯、鹅颈瓶、化学滤纸、酒精灯、灯芯、大型气罐。

**3.2 污染物粒径与过滤（决定「无法完全提纯」）**
| 组分 | 粒径 | 组分 | 粒径 |
|---|---|---|---|
| 化学污染物 | 2 | 有机废物 | 40 |
| 重金属 | 12 | 微生物 | 50 |
| 微塑料 | 25 | 物理污染物 | 100 |
| 矿物质 | 30 | 不溶性碎屑 | 200 |

- 滤芯孔径：普通/高级/水培芦苇 22，韧棉织物 150。粒径 < 22 的化学污染物、重金属不被普通滤芯去除。
- 化学污染物有 **0.7% 残留下限**；预装试剂瓶会把 2%–8% 替换为杂质。
- 沉淀把小粒径组分变成粒径 200 的碎屑以便过滤；过滤还受滤材孔径、耐久、成分残留下限约束。
- 原版纯度分级（价值修正）：淀粉级 <30% −95%、垃圾级 30–45 −75%、掺兑级 45–60 −45%、街头标准 60–80 +0%、近药用级 80–90 +40%、药用级 90–99 +85%、实验室纯级 ≥99 +135%。

**3.3 加热板（实时，每秒回调）**
- 升温：`min(目标, T + (目标−T) × 加热因子 / 350 + 1)`；低档因子 10、高档 30。
- 冷却：`26 + (T−26) × exp(−6.25/(√V+11))`，V 为总液量 μl；室温 26°C。
- 模组数 0/1/2/3/4 → 目标 99°C / 160°C / 26–160 可调 / 可调×4 / 可调×16（>4 不命中加速分支）。
- 容器反应**每秒调用一次**；夜间对非空容器连续 360 轮。超温会降解有效成分。
- 原版酒精：小瓶酒精 250ml = 酒精 175ml + 水 75ml → **70% 纯度**；酒精沸点 78.37°C，水 100°C，脂合剂 55°C。
- 原版小瓶酒精物品 id **仍未最终确认**：候选数组 `ChemLab.VanillaAlcoholIds = { mini_alcohol_bottle, mini_bottle, alcohol_bottle_small, med_bottle_small_pink }`，**需实机核对**。

**3.4 王尔德（Wild's Network）人脉服务（签名已核对，与旧记录不同！）**
```csharp
public static void ModHelper.AddNetworkUpgrade(
    string modId, string id, int cost, string subtitle,
    Func<string> title, Func<string> description, Action onUnlock,
    string tab = null, string iconKey = null, string[] prerequisites = null, int cooldownDays = 0,
    Func<string> subtitleLabel = null, Func<string> alreadyBought = null, bool recommended = false);
public static void ModHelper.SetNetworkTabLabel(string tab, Func<string> label);  // 只有 2 个参数
public static void ModHelper.RemoveNetworkUpgrades(string modId);
public static bool ModHelper.IsNetworkUpgradeUnlocked(string id);
```
- `title`/`description` 是 **`Func<string>`**；`subtitle` 复用原版卡片：`Introduction | Favor | Informant | Referral | Service | Permit | Supplier`。
- `tab` 留空 = 第一个「基础」标签页；自定义 tab（本项目用 `"free_chem"`）+ `SetNetworkTabLabel("free_chem", ...)` 才会出现在「自由化学」页。
- 已实现升级（全部 `tab: "free_chem"`）：
  - `free_chem.GAS_BROKER`（600，Supplier，气体商人引荐）
  - `free_chem.GAS_ORDER`（120，3 天冷却，前置 GAS_BROKER，送空钢瓶×2 + **空大型储气桶** + 氧气 + 氮气 + 饱和盐水）
  - `free_chem.VENT_SERVICE`（450，Service，常驻，无冷却）
  - `free_chem.ADV_VENT_SERVICE`（900，Service，前置 VENT_SERVICE，常驻）
- ⚠️ **调用时机（第 16 轮实机日志修正）**：官方指南示例写在 `OnEnable`，但 `AddNetworkUpgrade` 内部会走 `NetworkUpgrade.RegisterModUpgrade → PlayerStore.Instance`；**主菜单下 `PlayerStore..ctor` 会抛 NullReferenceException**（Player.log 实测：`Init failed: FreeChem v0.5.0`，模组被禁、本地化未加载 → 设置面板显示原始键）。本项目已改为在 `ModHook.OnGameLoadedInit → OnGameLoaded` 里注册，并先判 `PlayerStore.HasInstance`、整段 try/catch；`OnDisable` 的 `RemoveNetworkUpgrades` 同样 try/catch。
- 模组设置面板分区名是**原始字符串、不本地化**（label/tooltip 是 `Func<string>` 可本地化）；本项目用中文分区「玩法」「调试」，label/tooltip 走 `cfg_*` 本地化键。

**3.5 治安部嫌疑度（官方无 API；本项目采用未文档化路径）**
- 官方 modding API **没有**写治安部 heat / 调查进度的公开接口。`AddHeat`/`ClearHeat` 是植物遗传热；`ThingContainer.BalanceHeat()` 是空方法；`SEC_SUSPICION_PENALTY` 是 Lou 剧情常量。
- 程序集层面存在未文档化路径（已核对 public + IL）：
  - `PlayerStore.HasInstance`（public static get，**必须先用它判空**，否则 `Instance` 会惰性新建 store）
  - `PlayerStore.Instance.secData`（public 字段，类型 `SecData`）
  - `SecData.CommitCrime(string crimeID, int amount)`（public 实例方法；原版 `PlayerStore.CheckCrime` 也走它）
  - `SecData.ENDANGERMENT`（public const string）
- 本项目实现：每个排气单位写入 `units × 10 × 排风倍率(10000/5500/1200 万分比) / 10000` 点 `ENDANGERMENT`；先 `PlayerStore.HasInstance` guard，整段 try/catch；默认开启，配置 `vanilla_security` 可关闭。
- ⚠️ 风险：会真实推进原版犯罪/调查；起始专精 `IsIntimidating/IsBlabbermouth` 会改变数值并消耗 RNG；数值平衡**未实机验证**。详见 `docs/api-findings.md` §2.4/§2.5。

**3.6 图标规格（原版实测 + 本项目实测）**
| 物品 | 尺寸 | 占格 |
|---|---|---|
| 药瓶 `med_bottle_*` | 16×32 | 1×2 |
| 水罐 `water_pitcher`（大型液体容器） | 48×64 | 3×4 |
| 滤芯 `water_filter` | 16×32 / 16×48 | 1×2 / 1×3 |
| 机器（如 `moisture_farm`） | 48×48 | 3×3 |
- 本项目：便携钢瓶 **32×48（2×3 格）**；大型储气桶 **48×64（3×4 格）**；机器 **48×48（3×3 格）**，13 台各有一张 `_on` 工作态。
- 美术风格：淡灰白/米白机身、1px 深灰描边、低饱和点缀色、柔和高光、透明背景、16px 网格。

**3.7 API 核对结果（本机程序集，见 `docs/api-findings.md`）**
- **运行时换图 = 可行**：`GameItem.SetSpriteAndShapeFromMod(string modId, string spriteKey)` 是 public 实例方法，可双向重复调用；裸 key；内部 `ModLoader.Mods.Find(Manifest.ID==modId) → LoadedMod.Sprites[key]`（找不到只 Warning + 原样 return），然后置 `spriteAtlasPath/spritePath/useSpriteFromMod=true/spriteChanged=true`，并按 16px 格子 alpha 重建 `GridShape`。`spriteChanged` 由 `GameItemElement.ValidateSelectLocked` 消费 → 已打开窗口会刷新。
- `GameItem.SetSprite(string spriteAtlasPath, string spritePath)` 只改路径与 `spriteChanged`，**不置 `useSpriteFromMod`**，不用于 mod 贴图；`SetSpriteAndShape` 走原版图集。`UpdateSprite`/`RefreshSprite`/`SetSpriteFromCapacity`/`SetIcon` **不在** `GameItem` 上（旧记录 62/62 是跨类型命中）。
- `ModHelper.Get/SetModData*`：int/float/bool/string 全套 public static，键前缀 `modId:key`，**仅本局存档内有效**（主菜单调用只 Warning+no-op）。
- 游戏版本：`Assembly-CSharp` 无 AssemblyInformationalVersion；`<GameVersions>` 比对 `Application.version`（来自 `globalgamemanagers` 的 bundleVersion `0.46D`）→ **0.46**。

**3.8 调试生成与设置入口（第 14 轮，签名已核对）**
- **模组设置入口** = 游戏 Mods 菜单中该模组条目的配置面板，由 `ModHelper.GetConfig(modId).Define(...)` 注册；本项目现有 3 项：`dangerous_reactions`、`vanilla_security`、`debug_mode`（均在 "Gameplay"/"Debug" 分区；section 是原始字符串，不本地化，label/tooltip 用 `Func<string>` 跟随语言）。
- **F9 调试面板**：`ModHelper.AddDebugFunction(modId, ModHelper.DEBUG_CATEGORY_COMMAND, name, Action, tall)`（5 参）；分类常量 `DEBUG_CATEGORY_STORE/COMMAND/SPAWN/SPECIAL/MODDING/MARKETING`；`RemoveDebugFunctions(modId)` 返回移除数（在 `OnEnable` 注册、`OnDisable` 注销）。
  ⚠️ **F9/F8 受 `DevTools.IsEnabled` 门控**：只有 Unity 编辑器或游戏 exe 同级存在空 `dev.txt` 时才可用（`1dev.txt` 为 full 开关）。本机已在 `D:\SteamLibrary\steamapps\common\Probably Stolen\dev.txt` 放置空文件；**`dev.txt` 是开发机开关，不要随模组发布**（官方指南明确）。没有它时 `AddDebugFunction` 注册成功但面板不显示。
- **自定义窗口**：`CustomUIManager.Window(id, title, layerId)`（static，返回 `CustomUIBuilder`）；builder 链式 `UseMetaPrefabs(true)`（深色选项窗口外观）/`UseMachineryStyle()`、`AddLabel/AddWarning/AddHeader/AddInput/AddButton/AddCloseButton/AddSeparator`、`SetSize/Center/SetCloseOnEscape/SetDraggable/Show`；`CustomUIManager.Instance.GetElement(windowId, elementId)` 返回可 `SetText/SetVisible/OnClick` 的元素句柄；层常量 `LAYER_FRONT/PAPER/REAR/META/MENU/OVERLAY`（`LAYER_OVERLAY` 跨场景常驻，最稳）。
- **生成到后柜台**：`EmporiumEntry.Instance`（public static get，MonoBehaviour）→ `.backInvinvElement`（public get / private set，`GameGridInventory`，`Start` 里才赋值）；官方 `ModHelper.MoveItemToBackCounter(item)` 内部就是 `GraphUtils.TryAcceptAll(backInvinvElement, item, -1)` 并丢弃返回值（无 null guard、满仓不提示不销毁，所以本项目不走它）。
- **满仓探测/放置语义（易踩坑）**：`GraphUtils.CanAccept(GraphNodeStorage, GameItem)` 是**只读探测**；`GraphUtils.TryAcceptAll(node, item, amount=-1)` 返回**剩余未接受数量**（成功=0、失败>0，**不是** 0=失败）；`GraphUtils.TryAcceptAllOrDestroy(node, item, context=null)` 返回 true=已放入 / false=没放下且已自动 Destroy。本项目做法：`CanAccept` 只读早失败（失败 → `item.Destroy()` + 原版弹窗）→ 成功走 `TryAcceptAllOrDestroy`（false 时不再 Destroy，只弹窗）。**禁止把 TryAcceptAll 返回值当「已接受数」。**
- **原版弹窗 API（存在）**：`GenericConfirmationUIManager.Instance.OpenPanelSingleButton(string bodyText, string confirmText, Action onConfirm = null)`（public 单例，TMP 文本 + 暗化遮罩；原版店内提示如 `PlayerStore.ShowDisplayCaseTimingTipOnce`/`AmenitiesItemDirectory.ShowPurifierMessage` 都用它）。本项目满仓提示优先用它，`Instance == null` 时回退 `CustomUIManager` meta 窗口。
- **私有数字 ID**：`src/ChemItemIds.cs` 的 1–85 显式顺序表（72 物品 + 13 设备），纯数据不引用游戏程序集；**只追加、不重排**；解析支持 `85` / `spawn 85` / `id 85` / `get 85` / `生成 85` / 全角数字 / `list|ls|列表`。
- **没有原版警告弹窗专用 API**：`GenericConfirmationUIManager.Instance.OpenPanelSingleButton(bodyText, confirmText, onConfirm=null)` 是**真正的原版 modal + 暗化遮罩**（原版店内提示同款），本项目满仓提示优先用它，`Instance == null` 时回退 `CustomUIManager` meta 窗口。

**3.9 快捷键入口（第 15 轮，签名已核对）**
- 注册到游戏「选项 → 按键绑定」：`ModHelper.AddGameOptionKeyBind(string modId, string sectionId, string optionId, Func<string> label, Func<KeyCode> get, Action<KeyCode> set, Func<string> tooltip = null)`；`sectionId` 用 `ModHelper.OPTION_SECTION_KEYBIND`（`"keybind"`）。`ModHelper.RemoveGameOptions(modId)` 清理。
- 键值由模组自己存：本项目用 `PlayerPrefs` 键 `free_chem.debug_hotkey`（存 `(int)KeyCode`，默认 `KeyCode.F10`；无值/None/Escape/Mouse0 回退 F10）。官方指南明确「全局偏好用 PlayerPrefs 或 ES3」。
- 轮询用 `ModHelper.StartCoroutine(modId, IEnumerator)` + `yield return null` **每帧**（禁止线程）；`KeyBindHelper.SuppressHotkeys`（public static get）为 true 时跳过（玩家正在重绑）；`Input.GetKeyDown(hotkey)` 触发；只在 `EmporiumEntry.Instance != null`（店铺场景）生效；窗口已开则 `CustomUIManager.Instance.IsOpen(id)` → `CloseWindow`，否则打开；`OnDisable` 用 `ModHelper.StopCoroutine(modId, coroutine)` 停止。
- ⚠️ **csproj 偏离官方模板**：`Input` 位于 `UnityEngine.InputLegacyModule.dll`，官方模板 csproj 未列；本项目新增该 `<Reference>`（`Private=false`），**不把游戏 DLL 复制进发布包**，`<Capabilities />` 保持空。
- F9/F8 仍受 `DevTools.IsEnabled`（编辑器或 exe 同级空 `dev.txt`）门控且需重启游戏 → F9 入口降级为开发机备用；**快捷键入口不依赖 dev.txt**。

---

## 4. 工程结构（每个文件的职责）

```
ps-chem-mod/
├── FreeChem/                     ← 发布包（105 文件，已同步到游戏 Mods/FreeChem）
│   ├── manifest.xml              ID=free_chem, v0.5.2, GameVersions 0.46, 空 Capabilities, 中文描述
│   ├── FreeChem.dll              87,040 bytes（与 src/bin/Release 逐字节一致）
│   ├── preview.png
│   ├── Textures/Items/*.png      98 张图标（72 物品 + 13 机器空闲 + 13 机器工作态）
│   ├── Localization/{zh,en}.csv  各 300 条键
│   ├── LootTables/free_chem_drops.json  2 表 + 3 patch
│   └── Books/chem_codex.json     5 页纸质手册
├── src/                          ← C# 源码（9 个 .cs + csproj）
│   ├── FreeChem.csproj           netstandard2.1，引用游戏 5 个 DLL（新增 UnityEngine.InputLegacyModule，Private=false，不捆绑；GameDir 可覆盖）
│   ├── ChemRecipes.cs            反应表(39 条)/相态/纯度档/MachineIds(13)/DrumSkus/CanisterSkus/VanillaAnalog/ValidateLogic
│   ├── ChemLab.cs                污染物粒径/滤纸孔径/器材/加热公式/7 条热反应/排风倍率与嫌疑度纯函数/ValidateLogic
│   ├── ChemCatalog.cs            72 条物品规格/工厂/物品 tag 读写（含 8 类污染物、thermometer、6 种大桶）
│   ├── ChemRuntime.cs            本局状态(ModData)/毒性欣快(原版 L/E 公式)/已发现配方/排气计数/原版 ENDANGERMENT 折算/DebugMode
│   ├── ChemMachines.cs           设备工厂(窗口/电力/模组/结算) + 滗析/过滤/灌装/加热台构建 + 双状态图标 helper
│   ├── ChemHeating.cs            实时加热台每秒协程（升温/冷却/燃料/灯芯/实时反应/排放计数）
│   ├── ChemItemIds.cs            ★ 私有数字 ID 1–85（纯数据，只追加不重排；解析/锚点）
│   ├── ChemDebug.cs              ★ F9 调试窗口（CustomUIManager）+ 后柜台生成 + 满仓弹窗 + 状态行
│   └── ModEntry.cs               IMod 入口、钩子、王尔德网络(4 升级)、供货、温度计、配置(4 项)、提示框、F9 注册
├── tools/
│   ├── gen_icons.py              图标生成器（98 张 + icon_sheet.png；含 thermometer、双状态、大桶）
│   ├── gen_icons_v1.py           v1 备份（勿当现行；validate 只 WARN）
│   ├── gen_localization.py       本地化生成器（298 条 ×2）
│   ├── validate.py               全量静态验收（44 PASS / 1 WARN / 0 FAIL）
│   ├── logic_check.py            文本层逻辑自检（39 反应 / 243 断言）
│   ├── logic-harness/            C# 纯逻辑校验工程（129 断言，引用 ChemRecipes+ChemLab+ChemItemIds）
│   └── api-probe/                只读元数据/IL 探针（net8.0，零 NuGet，run.ps1 可复现）
├── ref-modding/                  官方范例仓库 clone（指南 + LearnerMod）
├── ref-icons/                    原版图标对照素材
├── docs/
│   ├── api-findings.md           ★ 反编译核对结论 + Lead 决策（含 IL 证据、未验证项）
│   ├── 设计说明.md               v0.4 设计全稿（相态/纯度/多级链/王尔德/温度/获取/验收；§13 = 第 14 轮调试与设置）
│   ├── 进度报告.md               需求演进（13 轮）+ 自查记录
│   └── 参考资源索引.md           所有参考来源与查证结论索引
├── README.md / 预览.html / icon_sheet.png
├── FreeChem_v1_backup/           第一版完整备份（勿当现行版本）
└── .dotnet/                      本地 .NET 8 SDK
```

---

## 5. 当前设计要点（接手方必须理解的核心不变量）

1. **相态严格分工**：滗析槽 `decanter` 只吃液体；气体洗涤塔 `gas_scrubber` 只吃气体；灌装台 `canister_filling` 只吃气体 + 空容器；冷凝塔只吃气体；结晶器只吃液体。**气体不能滗析**——三层强制（数据层 `AllowedInputPhases`、输入过滤、独立校验）。
2. **滗析是物理分离**：静置 24h，产物只有两件——上层清液就是**更纯的同一种药剂** + 下层沉淀。不改变化学身份。
3. **污染物不可完全提纯**：每件液体药剂带 8 类污染物 tag（`fc_contam_*`）；滗析只移除可沉降的 `phys`/`debris`，游离的 `chem`(2)/`heavy`(12) 等完全保留；滤纸按孔径拦截但留 0.7% 下限；纯度由 `ChemLab.PurityFrom()` 反推，**永远 < 100%**。
4. **纯度链**：液体滗析 +1 档 → 气体洗涤 +1 档 → 提纯气体灌装得更高档气罐（工业/医用/化学纯）。医用气罐设 `MEDICAL` 类型，可被原版医生/护士/医院供货商收购。
5. **多级合成 T1→T4（39 条反应）**：T1 氯碱法/大理石制 CO₂/双氧水分解/高锰酸钾热分解；T2 氢氯合成/哈伯法/吸收/中和/结晶；T3 提纯与灌装；T4 与原版药剂（N-羟甲基碱、还原剂、脂合剂、梦帽菇）反应生成虚构违禁品「空盐/红雾/夜玻/灰息」。**原版药剂只出现在 T4**。
6. **兼容原版**：所有药剂 `SetGameItemType(TypeHelper.CHEMICAL_SUPPLIES)`；`VanillaAnalog` 把原版药瓶折算为新体系 id；设备沿用 `MachineHelper.SetupBatterySlot`/`MachineryHelper.InitMachinery`/`MachineProgressHelper`/`ModuleHelper` 与原版电池、模组、电力通用。
7. **实时加热台**：`ChemHeating` 用 `ModHelper.StartCoroutine` 每秒 tick；酒精灯需灯芯 + 燃料；燃料纯度决定加热因子与消耗（`liq_ethanol` 或原版酒精，70% 折算）；升温/冷却套原版公式；温度落入热反应温区后按秒累积，超温降解为废渣。**不能用线程**。
8. **毒性/成瘾**：复用原版 `L=Σ(μl×毒性系数)`（≥1000 致命、≥750 有毒）、`E=Σ(μl×欣快系数)`（欣快等级 0–4），写在 `fc_tox`/`fc_euph`/`fc_controlled` tag，`OnCreateTooltipLate` 追加画像行。
9. **大型储气桶**（第 11 轮）：`gas_drum_empty` + 5 种充装桶（氧/氮/氢/氯/二氧化碳），图标 48×64 → 3×4 格；只能在灌装台用「气体 + 空桶」产出工业档；**已充装大桶是产物，绝不作为气体源或任何设备投料**。
10. **双状态图标**（第 11 轮）：13 台机器空闲 `<id>.png` / 工作 `<id>_on.png`；唯一入口 `ChemMachines.SetMachineVisual()`，按 `MachineProgressHelper.GetMachineState == STATE_WORKING` 在周期回调 `finally` 收敛（异常/提前 return 也不会卡在 `_on`）；加热台在每秒 tick 按点火/升温/热反应切换，`Stop()` 兜底回 idle。
11. **排风服务 + 治安部嫌疑度**（第 11/13 轮）：毒气（氯气/氯化氢/氨气）排放 → `ChemRuntime.AddExhaust(units)`：先累加模组内 `exhaust_raw`（按局存档，供 tooltip），再按 `units × 10 × 排风倍率` 折算写入原版 `SecData.ENDANGERMENT`（`HasInstance` guard，默认开启、`vanilla_security` 可关）；排风服务 ×0.55 / 进阶 ×0.12 同时削减两层。
12. **温度控制**（第 12 轮修复）：`thermometer` 温度计点击循环 `ChemRuntime.SetTemp(0..3)`（0 冷 / 1 常温 / 2 加热 / 3 高温高压），是 T2–T4 配方匹配的前提；缺了它那些配方永远不可达。
13. **获取途径**（第 12 轮修复）：起始礼包（空钢瓶+盐+碳酸钙+盐水+温度计+烧杯+酒精灯+棉灯芯）、发明家库存随机器材、气体商人订单（含空大桶）、掉落表 `free_chem_reagents` / `free_chem_apparatus` / `junkTable` / `toolTable` patch。`validate.py` 有「无死物品」硬断言。
14. **私有数字 ID**（第 14 轮）：`src/ChemItemIds.cs` 的 1–85 显式顺序表（72 物品 + 13 设备）；**只追加、不重排**（新物品放末尾并同步 `MaxId`）；纯数据层不引用游戏程序集；解析支持 `85` / `spawn 85` / `id 85` / `get 85` / `生成 85` / 全角数字 / `list|ls|列表`。`logic-harness` 有 85 条/无重复/集合==Specs∪Machines/锚点/解析用例断言。
15. **模组设置入口 + 调试窗口 + 快捷键**（第 14–15 轮）：Mods 菜单模组条目的配置面板（`ModConfig.Define`）现有 `dangerous_reactions` / `vanilla_security` / `debug_mode`；`debug_mode` 默认关。**首选入口 = 快捷键**（游戏「选项 → 按键绑定」里的「FreeChem 调试生成窗口」，默认 **F10**、可重绑、`PlayerPrefs` 持久化、不依赖 dev.txt）；按 F10 开/关窗口；输入数字 ID/`spawn N` → 生成到**后柜台** `EmporiumEntry.backInvinvElement`（窗口能开，但生成需先开 `debug_mode`）；先 `CanAccept` 探测，**放不下 → `Destroy` + 原版 `GenericConfirmationUIManager.OpenPanelSingleButton` 提示「柜台已满，无法加入新物品。」**，放得下用 `TryAcceptAllOrDestroy` 兜底。F9 调试面板入口保留为开发机备用（需 exe 同级空 `dev.txt` 且重启游戏）。

---

## 6. 当前真实状态（2026-11-10 09:55 实测，v0.5.2 热修复后）

**已通过（四层）**
- `dotnet build -c Release`：**0 警告 0 错误**，`FreeChem.dll = 87,040 bytes`。
- `tools/logic_check.py`：39 条反应 / **243 项断言全通过**。
- `tools/logic-harness`：**129 项 PASS / 0 FAIL**，「结论：全部通过」。
- `tools/validate.py`：**44 PASS / 1 WARN / 0 FAIL**。
- 发布包 105 文件已全量同步到 `D:\...\Mods\FreeChem`，**文件名 + SHA256 全部一致**。
- 原 97 张图标字节未变（改动前后基线比对 99/99 一致，含 icon_sheet/preview），新增 `thermometer.png` 为第 98 张。
- 防毒面具：发布包、现行源码、文档、本地化、手册、掉落表**零残留**（唯一残留是 v1 归档脚本，按约定只 WARN）。
- 本机已放置空 `D:\SteamLibrary\steamapps\common\Probably Stolen\dev.txt`（F9/F8 开发工具开关；**不属于模组发布内容**；快捷键入口不需要它）。
- **v0.5.2 热修复**：模组列表 NRE（OnEnable 调 AddNetworkUpgrade → PlayerStore..ctor NRE）已修（注册移到 OnGameLoaded + `PlayerStore.HasInstance` guard）；设置面板分区名中文；ModEntry.cs UTF-8 mojibake 已修复。

**已知 WARN（1 条，已解释）**
- `tools/gen_icons_v1.py` 是 v1 归档备份，含废弃物 token，不在发布包、不参与构建 → 只 WARN 不 FAIL。

**未验证（绝对不能冒充已完成 —— 需进游戏人工验收）**
1. 模组能否被游戏加载/启用（扫描器、`mod-scan` 无错误）。
2. 游戏内实际功能：设备能否放置和结算、灌装台产桶、加热台实时加热、温度计点击、滗析/过滤/洗涤、多级链产线。
3. 存档 → 重载后 `ModData`（Temp/Ph/exhaust_raw/discovered/accidents）与物品 tag 的恢复。
4. **运行时双状态图标**是否真的刷新已打开的背包/机器窗口、观感与闪烁。
5. 王尔德「自由化学」标签页是否实际出现、4 张卡片是否都在该页。
6. `vanilla_security` 配置项在 Mods 菜单的显示与开关是否真正阻断原版 ENDANGERMENT 写入。
7. 原版治安部写入的实际数值/调查节奏/判罚平衡（10/单位是未实机验证的设计值）。
8. 大型储气桶 3×4 占格、便携钢瓶 2×3 占格在游戏背包中是否与图标一致。
9. 掉落表实际刷出权重、起始礼包发放、发明家库存上架。
10. 原版小瓶酒精的真实物品 id（候选数组尚未实机核对）。
11. 毒性 tag 是否触发原版售后/顾客反馈。
12. **第 14–16 轮调试/设置功能**：F10 是否与游戏已有按键冲突/被占用、按 F10 是否真的开/关窗口、重绑后重启是否保持（`PlayerPrefs`）；窗口输入法/回车行为与呈现；`GenericConfirmationUIManager` 在店铺场景是否非 null 与暗化遮罩/叠加顺序；后柜台满判定与 `CanAccept` 是否一致；`debug_mode` 在 Mods 菜单的显示与生效；按数字 ID 生成的物品实际占格/去向/拾取；**F9 备用入口**（`dev.txt` + 重启）是否出现。
13. **v0.5.2 热修复实机确认**：「已安装模组」列表不再报 NullReferenceException；模组能正常启用；设置面板 3 项与两个分区（玩法/调试）全中文显示；按键绑定条目「FreeChem 调试生成窗口」中文；Wild Network 4 张卡在存档载入后出现。

---

## 7. 构建 / 校验 / 安装命令（接手方直接用）

```powershell
# 注意：系统 PATH 没有 python，必须用 bundled 绝对路径
$py = "C:\Users\Administrator\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe"
Set-Location C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod

# 图标 / 本地化重生成（确定性，重跑不改变既有 PNG 字节）
& $py tools\gen_icons.py
& $py tools\gen_localization.py

# 编译（注意 GameDir 末尾的双反斜杠）
Set-Location src
..\.dotnet\dotnet.exe build FreeChem.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Probably Stolen\\"
Set-Location ..

# 三层校验
& $py tools\logic_check.py
Set-Location tools\logic-harness; ../../.dotnet/dotnet.exe run --project LogicHarness.csproj; Set-Location ../..
& $py tools\validate.py

# API 探针（只读反编译，可复现 docs/api-findings.md）
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\api-probe\run.ps1

# 安装到游戏（覆盖式）
Copy-Item src\bin\Release\netstandard2.1\FreeChem.dll FreeChem\FreeChem.dll -Force
$game = "D:\SteamLibrary\steamapps\common\Probably Stolen\Mods\FreeChem"
Remove-Item $game -Recurse -Force
Copy-Item FreeChem $game -Recurse -Force
# 校验：逐文件 SHA256 一致
```

**游戏内人工验收清单（必须在实机做，静态校验替代不了）**
1. 启动游戏 →「模组」菜单启用 FreeChem → 重启（**v0.5.2 起列表不应再报 NullReferenceException**）。
2. 模组设置入口：Mods 菜单 → FreeChem → 配置，确认 3 项分两个中文分区：**玩法**（允许危险反应 / 排气计入原版治安部）、**调试**（调试模式）；标签与提示应为中文。
3. 快捷键入口：游戏「选项 → 按键绑定」里确认有「FreeChem 调试生成窗口」（默认 **F10**，可重绑）；进店后按 **F10** 应开/关生成窗口（**不需要 dev.txt**）。
4. 调试生成：先开启「调试模式」→ F10 → 输入 `12` 或 `spawn 12` → 物品进**后柜台**；把后柜台塞满再试 → 应弹原版风格「柜台已满，无法加入新物品。」；输入 `list` 可在 F8 控制台（开发机）看 1–85 对照。
5. 关闭调试模式再按 F10：窗口应仍能打开，但生成被拒绝并提示开启。
6. **F9 备用入口**（仅开发机）：确认 exe 同级空 `dev.txt` 存在并**重启游戏**后，F9 → Command 列应出现「FreeChem 按ID生成」；用户环境实测 F9 无效时属正常（无 dev.txt/未重启）。
7. F8 控制台（开发机）：`mod-scan free_chem`（无错误）、`loot-list free_chem_reagents`、`loot-list free_chem_apparatus`、`paper-list`。
8. 开新局：检查起始礼包 8 件、温度计点击循环 4 档、日志出现温度档。
9. 摆设备：给机器涂电池、放投料、观察进度与图标从空闲切 `_on` 再回空闲。
10. 王尔德人脉：确认「自由化学」标签页 4 张卡（Broker/Order/Vent/AdvVent）、购买排风服务后倍率生效。
11. 毒气排放：造氯气/氯化氢/氨气，确认 tooltip 嫌疑度增长；到治安部界面确认 ENDANGERMENT 犯罪量是否写入；购买进阶排风后增长明显变慢。
12. 灌装：气体 + 空钢瓶 → 三档气罐；气体 + 空大桶 → `gas_drum_*` 工业桶；检查 2×3 / 3×4 占格。
13. 存档 → 退出 → 重载：确认 ModData、物品 tag、图标状态恢复。

---

## 8. 给接手 Agent 的分工建议（按依赖顺序）

1. **实机验收 Agent**（最高优先）：按 §7 人工清单逐项跑，把「实际加载/功能/存档/换图/治安部/**第 14 轮调试生成**」结果记进 `docs/进度报告.md` 与 §6；任何不符 → 定位并修，不要只改文档。**重点先验第 14 轮**：dev.txt 开启 F9/F8、F9 按钮与窗口、数字 ID 生成到后柜台、满仓弹窗、debug_mode 开关。
2. **美术复核 Agent**：`icon_sheet.png` 与 98 张 PNG 在原版 UI 下的观感、`_on` 状态辨识度、 thermometer 风格，必要时精修 `tools/gen_icons.py` 并复核「既有 PNG 字节不变」。
3. **数值平衡 Agent**：排气 `10/单位` 与原版 ENDANGERMENT 调查节奏是否合理；热反应时长/温度窗口；气罐/桶定价。凡是改动都要同步 `logic-harness` / `validate.py` 断言。
4. **文档 Agent**：`HANDOFF.md` §6、`docs/*`、`README.md`、`预览.html` 的未验证项与实际验收结果保持一致。

---

## 9. 沟通约定

- 用户通过小程序/IM 一问一答沟通，通道无法后台定时推送（此前已如实说明「每 5 分钟汇报」做不到，改为每次交互汇报 + 落盘报告）。
- 语言：中文。风格：简洁、如实、不把未验证项说成已完成。用户对**逻辑自洽**要求很高（已多次纠正：气体不能滗析、游离污染物不能靠滗析分离、不能重复造轮子、必须兼容原版、温度档必须可达、器材必须有获取途径）。
- 用户对**原版机制准确性**敏感，任何新玩法都应先查 Wiki / 程序集（`tools/api-probe`）再落地。
- ⚠️ **文本编码坑（第 16 轮事故）**：本机 PowerShell 的 `Get-Content` / `Set-Content` 管道可能以 ANSI(GBK) 处理 UTF-8 文件，回写会让中文二次编码（mojibake）甚至吃掉换行。**含中文的源文件一律用 `read` / `write` / `edit` 工具或 .NET `File.ReadAllText/WriteAllText` + `UTF8Encoding(false)`**；每次中文改动后检查 BOM=False、无 `妯/鎸/璋/鈥/锛/銆` 等 mojibake 标记。
- ⚠️ **AddNetworkUpgrade 生命周期坑（第 16 轮实测）**：不要在 `OnEnable`（主菜单）调用——内部 `PlayerStore.Instance` 会惰性构造并抛 NRE，导致整个 Mod Init 失败、被禁、本地化不加载。改在 `OnGameLoaded` 且先判 `PlayerStore.HasInstance`。
