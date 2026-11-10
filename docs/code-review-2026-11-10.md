# FreeChem v0.9.6 代码审查（2026-11-10）

> 范围：`src/` 全部 C# 源码 + 物品贴图尺寸 + 本地化键。重点是协作者提出的五类问题：
> **UI 不匹配 / 瓶内实际液量不合理 / 机器逻辑 / 气液态转换 / 与原版物品联动**。
>
> 方法：静态阅读代码，并用脚本核对贴图占格和本地化键。**没有进游戏，也没有编译**：环境里没有游戏 DLL，自带的 Python 校验脚本这次也没跑成。
> 凡是结论依赖原版内部行为（来源是 `docs/*-research.md` 里的 IL 记录）的条目，都标了「需实机确认」。
>
> 背景：`docs/进度报告.md` 里几乎每一轮都写着「未实机验证」。现有的 `logic_check.py`、`validate.py`、`logic-harness` 只测纯数据表，测不到下面这些运行时问题。

严重度：**P0** = 功能完全失效、刷物品或丢物品；**P1** = 数值或逻辑明显错误；**P2** = 体验与一致性问题。

---

## 一、瓶内实际液量不合理

### 1.1 【P0】反应只按「物品个数」计，完全不看瓶里还剩多少液体
- **位置**：`ChemMachines.cs:567`（`ids.Add(cid)`）、`ChemHeating.cs:261` `CollectThermalInput`、`ChemCatalog.cs:907` `TryContactReaction`
- **问题**：倒液补丁会维护 `fc_volume_ml`，也会扣原版液体 tag，但机器、加热台、接触反应都只看 `identifier`。
- **游戏内表现**：一瓶 500 ml 盐酸先倒出 499 ml 到原版容器，剩下 1 ml 的瓶子放进机器，仍然按整瓶参与反应，产出整瓶产物。这是一个**液体复制漏洞**。
- **建议**：反应前检查每件液体投料的剩余量（读 `WaterHelper.GetTotalVolume`，不读 `fc_volume_ml`），不足一份就不匹配；或者按剩余比例缩放产物份数。

### 1.2 【P0】往原版容器倒剩的药剂瓶可以被「灌水复活」
- **位置**：`ChemPatches.cs:341-415`（只在**源**是本模组液体时接管）
- **问题**：本模组液体瓶本身就是原版液体容器（`EnsureLiquidBridge` 调了 `InitLiquidContainerItem`）。倒空一部分后，用原版水罐往里倒水，走的是原版 `TransferLiquid`。瓶子的身份仍然是 `liq_hydrochloric_acid`，纯度不变，`fc_volume_ml` 也不更新。
- **游戏内表现**：盐酸倒掉一半，加满自来水，还是一瓶「纯度 85% 的盐酸」。
- **建议**：禁止原版液体倒进本模组药剂瓶。做法是加一个 `TransferLiquid` Prefix：当 **target** 是本模组液体时 return false。或者在倒入后重算纯度和身份。

### 1.3 【P1】成分有两张表，扫描仪显示的和瓶里实际装的不一样
- **位置**：`ChemRecipes.cs:139`（`PourParts`：盐酸 = acid 20% + water 80%），`ChemRecipes.cs:1011`（`ComponentParts`：盐酸 = fc_hcl 36% + water 64%）
- **问题**：在 `ChemCatalog.Create` 里，`ApplyComponentComposition` 先写入 fc_* 成分（`:345`），随后 `EnsureLiquidBridge` 调 `InitLiquidContainerItem`（`:387`）。按 IL 记录，Init 会把所有 `Liquid.Liquids` 的 currentPart 清零（需实机确认），然后再写 PourParts。之后 `ApplyComponentComposition` 因为已有 `fc_components` 标记直接 return。
- **结果**：瓶里真实的液体 tag 是 acid/water；扫描仪补丁（`ChemPatches.cs:152-167`）却按 `ComponentParts` 显示「HCl 36%」；倒进原版容器、混进原版配方用的又是 acid 20%。三处数字对不上。
- **建议**：每种液体只保留**一张**成分表。扫描仪显示改为直接读物品上的实际 tag，不读静态表。

### 1.4 【P1】扫描仪「总液量」固定写 500 ml
- **位置**：`ChemPatches.cs:191-198` `VolumeText`
- **问题**：只要是液体就返回 `PourVolumeMl`（500），不读实际剩余量。倒掉一半后扫描仍然显示 500 ml。
- **建议**：改用 `WaterHelper.GetTotalVolume(item) / 1000`。

### 1.5 【P1】固体和气体在原版眼里只有 1 ml
- **位置**：`ChemRecipes.cs:899` `ComponentTotalPart = 1000`；`ComponentParts` 里固体和气体都是 `{fc_x: 1000}`
- **问题**：1000 part 只等于 1 ml。原版搅拌机、蒸发器、扫描仪看到的固体和气体药剂只有 1 ml，扫描仪还要靠补丁改写成「单件」来掩盖。
- **建议**：先定义每件固体和每瓶气体的标准量（例如固体 50 g 对应 50 ml 当量，钢瓶 250 ml 当量），再把 `ComponentTotalPart` 换算到这个量。毒性载荷基准（`ChemRuntime.VolumeUl`）也统一到同一口径。

### 1.6 【P1】原版 250 ml 酒精瓶被当成 500 ml 用，而且瓶里的酒精不会减少
- **位置**：`ChemHeating.cs:386`（`GetPourVolumeMl(fuel)`），`ChemCatalog.cs:835-842`（tag 缺失时默认 500）
- **问题**：原版 `mini_bottle` 没有 `fc_volume_ml` tag，于是按 500 ml 算，能灌 350 ml 有效燃料（实际最多只有 175 ml 酒精）。扣减只写 mod tag，原版的 alcohol/water part 原封不动，这瓶酒精在原版系统里仍是满瓶，还能喝、能卖、能倒。
- **建议**：燃料一律从原版液体 tag 读取和扣减（`WaterHelper.RemoveAndReturn`）。

### 1.7 【P2】纯水瓶也能放进燃料槽
- **位置**：`ChemCatalog.cs:793-796`（`alcohol + water > 0` 就算燃料）
- **游戏内表现**：任何水瓶或水罐都能放进燃料槽，但纯度 0%，加不了燃料，就卡在槽里。
- **建议**：只判断 `alcohol > 0`。

---

## 二、UI 不匹配

### 2.1 【P0】烧杯、锥形瓶、鹅颈瓶、试管、试管架的初始化代码执行不到
- **位置**：`ChemCatalog.cs:328` `if (IsReagentKind(spec.Kind))` 包住了 `:348`（容器 `InitLiquidContainerItem`）和 `:360`（试管架窗口）。但这五样物品的 Kind 是 `"vessel"`，不在 `ReagentKinds`（`:237-238`）里。
- **游戏内表现**：烧杯类**不是液体容器**，倒不进液体；试管架**打不开格子窗口**。机器里整套「容器内容 `fc_content_items`」逻辑（`ChemMachines.cs:548-565`、`:809-821`）因此全部失效。所有机器便签和手册都写着「可把烧杯放进投料槽并倒入试剂」，这是最明显的 UI 与实际不符。
- **建议**：把容器和试管架的初始化挪到 `IsReagentKind` 块外面。修好后要同时处理第 3.3、3.4 条，否则会冒出新的漏洞。

### 2.2 【P0】扫描仪的覆盖文字只在第一次刷新时生效
- **位置**：`ChemPatches.cs:66` 静态 `lastSignature`，`:84` 签名相同直接 return
- **问题**：`RefreshUI` 每次都会先跑原版逻辑重写 TMP，Postfix 发现签名没变就跳过。结果是同一物品第二次刷新、或换一件同 id 同纯度的物品时，屏幕上是**原版文字**（或混排）。
- **建议**：去掉这个缓存，或者按 `__instance` 加物品引用缓存，并且仍然每次都写 TMP（写 TMP 很便宜）。

### 2.3 【P1】机器便签和实际接受的投料矛盾
- **位置**：`FreeChem/Localization/zh.csv` 的 `machine_note_*`；`ChemMachines.cs:154`、`:398-401`
  - 每台机器的便签末尾都追加了「可把烧杯或锥形瓶放进投料槽」。但灌装台投料槽只收气相（`:398-401`），烧杯放不进去。洗涤塔便签写「仅接受气体」，代码却在相态检查**之前**就无条件放行容器（`:154`）。
  - 加热台便签写「燃料槽放乙醇」，但乙醇在 P12 已经删除。
  - 加热台「反应槽可放容器」：放进去的烧杯在热反应匹配时被忽略（`ChemHeating.cs:261` 不读容器内容），只起到「点火开关」的作用：放一个空烧杯也会点火烧燃料。
- **建议**：按每台机器真实接受的物品重写便签，去掉通用尾句。

### 2.4 【P1】加热台没有任何状态显示
- **问题**：当前温度、高低档、正在进行的反应和进度秒数都只存在内存里的 `Rig` 对象中。机器 tooltip（`ModEntry.cs` `OnTooltip`）只显示嫌疑度和便签。玩家只能靠猜。
- **建议**：tooltip 加一行，内容为「温度 / 档位 / 反应 / 进度」。夜间机器也建议显示当前匹配到的配方，或「无匹配，将产出废渣」的预警。

### 2.5 【P2】液体瓶 tooltip 不显示剩余量
- **问题**：倒过的瓶子看起来和满瓶一样。
- **建议**：`OnTooltip` 对液体追加一行「剩余 x / 500 ml」。

---

## 三、机器逻辑问题

### 3.1 【P0】电解槽永远跑不了「氯碱法」
- **位置**：`ChemMachines.cs:345`（电解槽输出格 4×4）、`:725-761` `CanAcceptOutputs`
- **问题**：产物是 Cl₂ 2×3、H₂ 2×3、NaOH 1×3。我用脚本按真实贴图尺寸穷举了所有摆法，**在不旋转的前提下放不下**。`CanAcceptOutputs` 返回 false，机器就一直停在待机，没有任何提示。这是手册里 T1 的第一条反应。
- 同样的问题：哈伯法在开启「天平 + 催化剂」奖励时产出 2 个 NH₃（2×3），高压合成釜输出格只有 3×3，也放不下。
- **需实机确认**：原版网格是否支持旋转。如果支持，这条降为 P2。
- **建议**：电解槽输出格改为 5×4 或 6×3；或者让 CanAcceptOutputs 失败时在 tooltip 或日志中给出明确提示。

### 3.2 【P0】输出格检查用的是空网格，没算已有产物
- **位置**：`ChemMachines.cs:735` `var scratch = new GameGridInventory(...)`
- **问题**：探测用的是一个**空的**临时网格，不包含输出格里已经放着的东西。只要上一批产物没取走，检查照样通过，进入 `Finish` 后再走 `MoveItemToBackCounter`（`:917`）。按 HANDOFF §3.8，这个 API 满仓时**不报错、不销毁、也不提示**，物品可能就此丢失。
- **建议**：把输出格里的现有物品复制进 scratch 再探测，或者直接对真实输出格逐件 `CanAccept` 并累计占格。满了就不开工，并给出提示。

### 3.3 【P1】实验容器可以绕过相态限制
- **位置**：`ChemMachines.cs:154`（`IsVessel → return true`，位置在相态过滤之前）
- **游戏内表现**（2.1 修好后会暴露）：液体装进烧杯就能放进「仅收气体」的洗涤塔、灌装台以外的气体设备。
- **建议**：对容器按其内容的相态再走一遍 `AllowedInputPhases`。

### 3.4 【P1】容器内容只记 id，丢失纯度和用量，且结束时整杯清空
- **位置**：`ChemCatalog.cs:853-866`（只记 `id:ml`）、`ChemMachines.cs:794-798`（只从 `liq_` 开头的物品读污染物）、`:809-812`（`ClearContainerContent` 无条件清空）
- **问题**（2.1 修好后会暴露）：
  1. 经过容器的液体，提纯等级和污染物全部丢失，纯度链断掉。
  2. 只要有记录就算一整份投料，1 ml 也算。一瓶 500 ml 分装进 5 支 100 ml 试管，就变成 5 份投料（**复制漏洞**）。
  3. 配方只用到容器里一部分内容，或者走「容忍多余投料」分支时，整杯照样被清空。
- **建议**：容器内容改为记录 `id:ml:purity:level`；反应按配方用量扣减，不是整杯清空；不足一份不匹配。

### 3.5 【P1】温度是全店共用的一个旋钮，而且只限制下限
- **位置**：`ChemRuntime.cs:26`（静态 `Temp`），`ChemRecipes.cs:1242`、`:1265`
- **问题**：
  - 所有夜间机器共用温度计上的同一个档位。想让冷凝塔「冷」、结晶器「热」，同一晚做不到。
  - `temp ≥ 2` 的配方只检查「不低于」；`temp 0/1` 的配方（冷凝吸收 `temp: 0`、次氯酸盐 `temp: 0`）**完全不检查温度**。结果是高温高压档下照样能「冷凝吸收」。
  - 加热台却有每台独立的实时温度（`ChemHeating.Rig.Temp`）。两套温度模型并存。
- **建议**：温度改成每台机器自己的设定（存在机器 tag 上，用机器面板按钮或模块切换）；配方给出 `[minTemp, maxTemp]` 区间。

### 3.6 【P1】加热台「超温降解」永远不会触发；产物放不下会丢失
- **位置**：`ChemHeating.cs:180`；`MatchThermal`（`ChemLab.cs:344`）只在 `temp <= MaxTemp` 时返回配方，而所有配方的 `DegradeTemp > MaxTemp`。所以 `r.Temp > reaction.DegradeTemp` 恒为 false。另外，高档目标温度上限 160°C，本来也到不了 190–200°C。
- **位置**：`ChemHeating.cs:319` `GraphUtils.TryAcceptAll(r.Output, produced)` 不看返回值，之后照样销毁投料（`ConsumeThermalInputs`）。输出格满了，产物就丢了。
- **建议**：降解改为「温度超过 MaxTemp 持续 N 秒」；产出前先检查输出格。

### 3.7 【P1】毒性和欣快数值算错 100 倍，几乎所有药剂都显示「致命」
- **位置**：`ChemRuntime.cs:249-256`
- **问题**：注释写的是 `L = ToxLoad × 25`，代码却是 `250000 × tox / 100 = tox × 2500`。盐酸 tox 30 算出 75000，而 1000 就算致命。只要系数不为 0（CO₂、H₂、氨水……）都会显示「致命」；欣快同理，系数 ≥ 0.01 就直接 4 级。这个结果还会通过 `ChemIsLethalPatch` 等补丁影响原版顾客反馈。
- **另一个问题**：`TTox` 和 `TCompTox` 是**同一个 tag 名** `fc_tox`（`ChemCatalog.cs:32`、`:51`）。`WriteState` 写入 0–400 量程（Profile × 100），`ApplyComponentComposition` 又按成分重算，并 Clamp 到 0–100 覆盖掉（`:677-682`）。氯气 400 变成 100，空盐欣快 1000 也变成 100，所有强效品数值一样。
- **建议**：确定一个量纲（推荐「系数 × 100」，不 clamp），把公式改为 `VolumeUl * tox / 100 / 100`，再把两个常量拆成两个不同的 tag，或者只保留一个来源。

### 3.8 【P2】配错就吞料
- **位置**：`ChemMachines.cs:123`、`:851`
- **问题**：没有匹配到配方时产出废渣，并销毁全部投料。放错东西就是整批损失，事先没有提示。建议改为「无匹配不开工」，并在 tooltip 上提示。

---

## 四、气液态转换逻辑问题

### 4.1 【P1】气体是可以直接拿在手里的物品，不需要容器
- **现状**：电解、气体发生器直接产出 `gas_chlorine` 这类 2×3 物品，放进背包、摆在柜台上都行；灌装台再用「气体 + 空钢瓶」做成 `can_*`。玩家眼里有两种看起来几乎一样的「气体物品」，语义说不清。
- **建议**（需要你们决定方向）：
  - **A**：产气反应必须有空钢瓶槽，气体直接灌进钢瓶，取消独立的 `gas_*` 物品（`gas_*` 只作为内部 token）。
  - **B**：保留 `gas_*`，但把它定义为「气囊或临时储气袋」，有泄漏计时，不及时灌装就逸散并计入排放。

### 4.2 【P1】有毒气体「排放」计在了产出时
- **位置**：`ChemMachines.cs:932` `NoteExhaust(id)`、`ChemHeating.cs:321-325`
- **问题**：Cl₂ 等被产出、放进输出格（等于被收集了），却按「排放」计嫌疑度。真正把它扔掉或销毁时反而不计。
- **建议**：产出并收集时不计；没有容器可装（溢出）、主动丢弃，或按 4.1 方案 B 逸散时才计入。

### 4.3 【P1】自定义成分统一按「沸点 100°C、可蒸发」注册
- **位置**：`ChemCatalog.cs:585-606`
- **问题**：NaCl、CaCO₃、CuO 这类固体，和 O₂（沸点 −183°C）、Cl₂（−34°C）这类气体，在原版液体系统里都按沸点 100°C 处理。药剂类型是 `CHEMICAL_SUPPLIES`，原版蒸发器和加热板会接收（`ChemCatalog.cs:21` 的注释也写了这一点）：盐会在 100°C 被「蒸发掉」，气体在常温下却被当成液体。
- **建议**：`ComponentDef` 增加 `BoilingPoint` 和 `IsEvaporative`（或者 Phase）字段：固体设很高的沸点并关闭蒸发；气体设真实沸点。

### 4.4 【P1】「冷凝 / 吸收 / 蒸馏」不受温度影响，各处产物也不一致
- 冷凝吸收（`hcl_absorption`、`ammonia_absorption`）在任何温度档都能进行（见 3.5）。
- 夜间版 `kmno4_decomp`：O₂ + MnO₂ + K₂MnO₄；加热台版 `heat_kmno4`（`ChemLab.cs:324`）：只产 O₂，两种固体凭空消失。
- `heat_hcl_distill` 在 78–110°C 让盐酸「纯度 +2」，却不消耗水、也不产生冷凝液。
- **建议**：同一个化学反应在不同设备上的产物应该一致；相变类操作（蒸馏、冷凝、结晶）按温度区间加方向判定。

---

## 五、与原版物品的联动逻辑问题

### 5.1 【P0】原版物品只按 id 映射，不看里面装了什么、装了多少
- **位置**：`ChemRecipes.cs:96-112` `VanillaAnalog`；`ChemMachines.cs:170-179`、`:570-571`
- **游戏内表现**：
  - **空的**原版水瓶，或者装着脏水的水罐（`water_pitcher`，3×4，带滤芯），会被当作一份「水」投料，然后**整件销毁**（`:851`），滤芯一起没了。
  - `med_bottle_*` 不管剩多少、纯度多少，都折算成一整瓶本模组药剂；原版的纯度体系被丢掉。
- **建议**：映射前读原版实际成分和体积（`ChemicalHelper.GetTotalVolume`、`WaterHelper.GetTotalVolume` 加组分比例），按用量扣减原版容器里的液体，而不是 `Destroy()`；至少要把 `water_pitcher`、`water_jug` 这类工具型容器排除在「可销毁」之外。

### 5.2 【P1】T4「原版药剂反应」实际上不需要原版药剂
- **位置**：`ChemRecipes.cs:459-479`；`Match` 会对配方输入也做 `Normalize`
- **问题**：`med_bottle_violet` 被映射成 `liq_nitric_acid`、`med_bottle_small_pink` 映射成 `liq_ammonia_water`、`med_bottle_orange` 映射成 `liq_hydrogen_peroxide`、`dream_cap` 映射成 `sol_activated_carbon`。于是空盐、灰息、红雾、夜玻四条违禁品配方都能**只用本模组自制的化学品**完成，设计里「原版药剂只出现在 T4」的约束形同虚设。
- 另外，映射本身在化学上不成立：「还原剂」被映射成强**氧化剂** H₂O₂，「N-羟甲基碱」被映射成硝酸。
- **建议**：T4 配方改用独立的 token（例如 `vanilla:med_bottle_violet`），不经过 `Normalize`；`VanillaAnalog` 只保留化学上说得通的映射（酸 → 盐酸、碱 → 烧碱、水 → 水）。

### 5.3 【P1】接触反应会误吞原版物品，产物投到可能已满的后柜台
- **位置**：`ChemPatches.cs:431`（`GameItem.Target` 的 Prefix 里，比任何原版逻辑都早执行 `TryContactReaction`）、`ChemCatalog.cs:907-933`
- **游戏内表现**：拿本模组盐酸去点一个原版「碱性溶液瓶」，本意可能只是想倒液，结果两件都被销毁，产出一瓶整的盐水。拿生石灰去点原版水罐，**水罐连同滤芯一起被销毁**。产物走 `MoveItemToBackCounter`，没有满仓检查。
- **建议**：接触反应只在两件都是本模组药剂时触发，或者改成需要二次确认；产物投放复用 `ChemDebug` 里已经写好的「`CanAccept` + 满仓弹窗」逻辑。

### 5.4 【P1】倒液补丁跳过了原版的过滤、温度和源容器收尾
- **位置**：`ChemPatches.cs:363-385`
- **问题**：Prefix 返回 false，替换掉整个原版 `TransferLiquid`，于是 `HandleFilter`（滤芯）、`HandleReceiveLiquidTemp`（液温）、`OnLiquidContainerPourOutOf(source)`（源瓶外观和价格刷新）都不执行。从本模组瓶往带滤芯的原版容器里倒液时，滤芯不起作用。
- **建议**：主路径（源有 `LIQUID_CONTAINER_TAG`）直接交给原版处理（return true），Postfix 里只同步 `fc_volume_ml` 和容器内容；只有旧存档缺 tag 时才走兜底路径。

### 5.5 【P2】注册的自定义 Liquid 会进入所有原版容器
- **位置**：`ChemCatalog.cs:607-608`
- **问题**：自定义 Liquid 被追加进 `Liquid.Liquids` 以后，原版每个液体容器在 Init 时都会给这 42 个 fc_* 成分各建一个 tag（存档变大）；`displayName` 只在第一次注册时本地化一次，之后切换语言不会更新。

---

## 六、修复顺序建议

1. **先修「执行不到」和「丢物品、刷物品」的问题**：2.1（容器初始化，一处改动）→ 3.2（输出格检查）→ 3.6（加热台产出丢失）→ 1.1 / 1.2 / 3.4（液量复制漏洞）→ 5.1 / 5.3（误销毁原版物品）。
2. **再统一数据口径**：1.3 / 1.5（成分表合一，定义标准用量）→ 3.7（毒性量纲）→ 4.3（沸点与相态）。
3. **最后做设计层调整，需要你们拍板**：4.1（气体是否必须进容器）、3.5（温度改成每台机器独立）、5.2（T4 是否真的要求原版药剂）。
4. **配套**：每修一条，往 `tools/logic-harness` 加一个回归断言（比如 1.1 的「1 ml 瓶不匹配」、3.1 的「每条配方产物都能放进输出格」）；然后**进游戏实机走一遍**，项目到现在还没在游戏里验证过。

## 七、没有覆盖的部分

- 标签打印机（`ChemPrinter.cs`）的「实验室纯级」刷价问题、电话商人、王尔德人脉、掉落表，都只是粗看，没有深挖。旧版 `docs/player-perspective-review.md` 里列过的电话 `Responding` 问题，在当前代码中已经修复。
- 所有涉及原版内部行为的判断（Init 是否清零 tag、`AddLiquidToChemFromList` 是覆盖还是累加、网格是否支持旋转、`MoveItemToBackCounter` 满仓时的行为），依据都是仓库里的研究文档，需要实机确认。
