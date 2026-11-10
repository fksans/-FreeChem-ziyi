# FreeChem API 核对结论（静态程序集反编译 / 元数据核对）

> 本文只记录**静态核对**结果：用只读元数据探针（`System.Reflection.Metadata` + `PEReader`）读取
> `Assembly-CSharp.dll` 的元数据表、方法签名与 IL，**没有加载、没有执行游戏程序集，也没有做任何游戏内验证**。
> 探针工程：`tools/api-probe/`；原始输出：`tools/api-probe/out/`（可复现，见 §9）。
>
> 结论写法：**可行 / 不可行 / 签名未知**。凡「可行」均给出可见性前提与 IL 依据；凡「不可行」给出反证。

GAME_VERSION: 0.46

---

## 0. 核对对象与环境

| 项 | 实测值 |
|---|---|
| 程序集 | `D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed\Assembly-CSharp.dll` |
| 大小 | 4,820,224 bytes |
| 程序集标识 | `Assembly-CSharp`，AssemblyVersion **0.0.0.0**，MVID `911508a6-e0eb-4cc0-91e8-e14b16ea28d7` |
| `AssemblyInformationalVersion` / `AssemblyFileVersion` | **不存在**（程序集自定义特性只有 ExtensionAttribute / CompilationRelaxations / RuntimeCompatibility / Debuggable，见 `tools/api-probe/out/assembly.txt`） |
| Steam buildid | **26525552**（`D:\SteamLibrary\steamapps\appmanifest_4348910.acf`：`"appid" "4348910"`, `"name" "Probably Stolen"`, `"buildid" "26525552"`） |
| 类型 / 方法总数 | 2,788 个类型 / 25,202 个方法 |
| `InternalsVisibleTo` | **无**（所以只有 `public` 成员对 Mod 可用；`internal` 一律不可用） |

> 由此：所谓「游戏版本号」不能从 `Assembly-CSharp.dll` 的 AssemblyInformationalVersion 取——该特性根本不存在。
> 版本依据与结论见 §4。

---

## 1. GameItem 运行时换图（机器空闲/工作双状态图标）

### 1.1 结论（一句话）

**可行。** 用 `GameItem.SetSpriteAndShapeFromMod(string modId, string spriteKey)` 在运行时反复切换
`<id>` ↔ `<id>_on` 是安全的；它是 `public` 实例方法、不抛异常、找不到图只记一条 Warning。
**不要**用 `SetSprite` / `SetSpriteQuick` / `SetSpriteAndShape` 来换 mod 贴图（原因见 §1.4）。
**`GameItem` 上不存在** `UpdateSprite` / `RefreshSprite` / `SetSpriteFromCapacity` / `SetIcon`（见 §1.5 反证）。

### 1.2 `GameItem` 换图相关的真实成员（全部来自元数据 dump）

`GameItem` 是 `public` 抽象类（`base=GameItemFunc`；`Destroy()`/`Validate()` 为 abstract）。实际创建出来的实例类型是
`GameItemElement`（`ItemDirectory.CreateEmptyItem(string)` 的 IL：`newobj GameItemElement::.ctor(string,string,string,PixelWindow)`，
见 `tools/api-probe/out/il/ItemDirectory__CreateEmptyItem.il.txt`）。换图相关成员：

| 成员 | 可见性 | 真实签名 | 语义（IL 依据） |
|---|---|---|---|
| `SetSpriteAndShapeFromMod` | **public** 实例 | `GameItem SetSpriteAndShapeFromMod(string modId, string spriteKey)` | 见 §1.3，**唯一正确的 mod 贴图 setter** |
| `SetSprite` | public 实例 | `GameItem SetSprite(string spriteAtlasPath, string spritePath)` | 只 `set_spriteAtlasPath` / `set_spritePath` / `set_spriteChanged(true)`，**不设** `useSpriteFromMod`、不校验、不改 shape |
| `SetSpriteQuick` | public 实例 | `GameItem SetSpriteQuick(string spriteAtlasPath, string spritePath)` | `set_spriteAtlasPath` + `set_spritePath` → `(GameItemElement)castclass` → **立刻** `GameItemElement.ValidatePartialSprite()`；**不设** `spriteChanged`/`useSpriteFromMod`、不校验存在性、不重建 shape（IL：`GameItem__SetSpriteQuick.il.txt`） |
| `SetSpriteAndShape` | public 实例 | `GameItem SetSpriteAndShape(string spriteAtlasPath, string spritePath)` | 3 个 setter + `BuildShapeFromSprite(atlas,path)` + `SetShape(shape)`；`BuildShapeFromSprite` 走 `RenderHandler.LoadFromAtlas`＝**原版资源图集**，不能用于 mod 贴图 |
| `SetShape` | public 实例 | `GameItem SetShape(GridShape itemShape)` | `set_shape(itemShape.Clone())` + `ValidateShapeState()` |
| `BuildShapeFromSprite` | **public static** | `GridShapeBuilder BuildShapeFromSprite(string spriteAtlasPath, string spritePath)` | `RenderHandler.LoadFromAtlas(atlas,path)` → 按 `UISettings.current.gridSize` 逐格取**格子中心像素 alpha** 生成 `GridShapeBuilder` |
| `spriteAtlasPath` | public 属性 | `string` get/set | mod 贴图时为 **`modId`**（如 `"free_chem"`） |
| `spritePath` | public 属性 | `string` get/set | **裸文件名 key**（如 `"gas_scrubber_on"`，无扩展名、无目录、无 `modId:` 前缀） |
| `useSpriteFromMod` | public 属性 | `bool` get/set | 渲染时走 mod 解析分支的开关；只有 `SetSpriteAndShapeFromMod` 会把它置 `true` |
| `spriteChanged` | **protected** 属性 | `bool` get/protected set | 视觉刷新标志；Mod **不能直接写**，只能通过上面的 public setter 间接触发 |
| `Create` | public static（obsolete） | `GameItem Create(string spriteAtlasPath, string spritePath, PixelWindow window)` | 旧工厂，不用于 mod |

IL 原文摘录（`tools/api-probe/out/il/`）：

- `GameItem__SetSprite.il.txt`：`set_spriteAtlasPath` → `set_spritePath` → `set_spriteChanged(true)` → `ret`（23 字节 IL）。
- `GameItem__SetSpriteAndShape.il.txt`：前 3 步同上，再 `BuildShapeFromSprite(atlas,path)` → `SetShape(shape)`。
- `GameItem__SetShape.il.txt`：`GridShape::Clone()` → `set_shape` → `ValidateShapeState()`。
- `GameItem__get_spriteChanged.il.txt` 等：`spriteChanged` 的 setter 是 protected（元数据 `protected specialname`）。

### 1.3 `SetSpriteAndShapeFromMod` 的完整 IL 语义（核心证据）

文件：`tools/api-probe/out/il/GameItem__SetSpriteAndShapeFromMod.il.txt`（159 行）。逐步：

1. `ModLoader.Mods.Find(m => m.Manifest.ID.Equals(modId))`（`GameItem+<>c__DisplayClass187_0::<SetSpriteAndShapeFromMod>b__0` 的 IL 即
   `m.Manifest.ID.Equals(modId)`；闭包持有 `modId`；`ModLoader.Mods` 是 public static 属性 `List<LoadedMod>`）。
2. `LoadedMod.Sprites.TryGetValue(spriteKey, out Sprite)`。
3. **找不到 mod 或 key**：`Debug.LogWarning("[GameItem] Mod sprite '<key>' from mod '<modId>' not found.")`，然后 `ldarg.0; ret` —— **原样返回，不改任何字段、不抛异常**。
4. 找到时依次：
   - `set_spriteAtlasPath(modId)`
   - `set_spritePath(spriteKey)`
   - `set_useSpriteFromMod(true)`
   - `set_spriteChanged(true)`
5. 用 `sprite.rect` + `UISettings.current.gridSize`（背包格 16px）**逐格采样 sprite 纹理中心像素 alpha**，生成 `byte[]` 掩码 → `new GridShapeBuilder().SetData(mask, width)` → `SetShape(GridShape)`。

所以：**`spriteKey` 就是 `Textures/Items/` 下的文件名（不含 `.png`）**；`modId` 必须等于 manifest 的 `<ID>`（`free_chem`），不是文件夹名。
形如 `"free_chem:gas_scrubber_on"` 的写法是**错的**（那是 UI 元素 `CustomUIManager.GetSprite` / `AddImage` / `AddNetworkUpgrade(iconKey:)` 的格式，不是 GameItem 物品贴图格式）。解析分支在
`GameItemElement.ResolveSpriteByName`（见下）。

### 1.4 渲染/刷新路径（为什么重复调用能生效）

`GameItemElement.ResolveSpriteByName(string name)`（public，但渲染内部用）IL：

- `if (useSpriteFromMod)` → `ModLoader.Mods.Find(m => m.Manifest.ID == get_spriteAtlasPath())` → `LoadedMod.Sprites.TryGetValue(name, out sprite)` → 返回 sprite；
  失败则 `Debug.LogWarning("Mod sprite '<name>' from mod '<atlas>' not found. Loading dummy sprite.")` 并返回 `RenderHandler.unknownSprite`。
- `else` → `RenderHandler.LoadFromAtlas(get_spriteAtlasPath(), name)`（原版图集分支）。
- `GameItemElement.ResolveItemSprite()` = `ResolveSpriteByName(get_spritePath())`。

刷新消费点：`GameItemElement.ValidateSelectLocked(bool updateBackground)`（IL 前 35 行）：

```
if (get_spriteChanged()) {
    s = ResolveItemSprite();
    background.image.sprite = s;  background.image.SetNativeSize();
    fakeBackground.image.sprite = background.image.sprite; fakeBackground.image.SetNativeSize();
    set_spriteChanged(false);
}
```

即：**`spriteChanged=true` 会在下一次 UI 校验/绑定（GameItemElement 存活期间）把新 sprite 应用到已打开的物品/机器窗口**，
应用后自动复位。静态核对范围内**没有发现其它 sprite 缓存会挡住刷新**（`spriteChanged` 的读取点只有这一处；
`ResolveItemSprite` 每次重新走 `ResolveSpriteByName`，内部直接查字典，没有按物品缓存 sprite 的字段）。

另有「立即刷新」旁路（仅记录事实，不推荐作主路径）：`GameItemElement.ValidatePartialSprite()` 是 **public** 方法，
`SetSpriteQuick` 就是「设 spriteAtlasPath/spritePath 后 castclass 成 GameItemElement 并立即调用 ValidatePartialSprite()」
（`ValidatePartialSprite` 内部 = `ResolveItemSprite()` + 给 background/fakeBackground 换图 + SetNativeSize，不碰 shape/标志）。
所以理论上 `SetSprite(ModId, key)` + `((GameItemElement)item).ValidatePartialSprite()` 也能立刻换图；但它跳过存在性校验与形状重建，
且依赖 `useSpriteFromMod` 已为 true，比 `SetSpriteAndShapeFromMod` 更脆——统一用后者即可。

### 1.5 为什么不用 `SetSprite` / `SetSpriteAndShape`（反例）

- **`SetSpriteAndShape(atlas, key)`（不适用于 mod）**：它调用 `BuildShapeFromSprite` → `RenderHandler.LoadFromAtlas(atlas,key)`，
  按原版资源图集路径加载。把 `modId` 当 atlas 传进去只会找不到图（unknown sprite），并可能把 shape 建造成空/错误形状。
- **`SetSprite` / `SetSpriteQuick`（不推荐作主路径，但不是完全不能用）**：只写 `spriteAtlasPath`/`spritePath`/`spriteChanged`，
  不校验 sprite 存在、不更新 shape、**不设 `useSpriteFromMod`**。对「本来就是用 FromMod 创建、`useSpriteFromMod` 已为 true」的物品，
  它理论上也能解析到 mod 贴图；但它依赖 `useSpriteFromMod` 没被清掉，且 key 写错时不会有任何提示（只在渲染端刷 Warning）。
  统一走 `SetSpriteAndShapeFromMod` 更稳（存在性检查 + 形状重建 + 标志齐全）。
- **`GameItem` 上没有 `UpdateSprite` / `RefreshSprite` / `SetSpriteFromCapacity` / `SetIcon`**。这些标识符确实存在，但都属于别的类型：
  - `CurrentConverterHelper.RefreshSprite(GameItem)`（public static）
  - `VendingMachineHelper.UpdateSprite(GameItem, string)`、`VendingMachineHelper.SetSpriteFromCapacity(GameItem, string, int)`、`VendingFountainHelper.UpdateSprite(GameItem, string, GameSlotInventory, GameSlotInventory, GameSlotInventory)`、`MachineDnaManipulator.UpdateSprite(GameItem)`、`MachineRAS.UpdateSprite(GameItem)`、`MachineTurboBoosterAdv.UpdateSprite(GameItem, GameSlotInventory)`（`MachineHydroponic.UpdateSprite` 是 private）
  - `PerkElement.SetIcon()`、`MachineryButton/MachineryLabel.SetIcon(Sprite|string atlas,string name)`
  - 这些全是「原版某台机器/某个 UI 元件专用的硬编码 helper」，不是通用 GameItem setter，不能被 13 台自定义机器复用。
  → HANDOFF §3.7「62/62 命中」其实是**跨类型**命中，不能据此认为 `GameItem` 有这些成员。

### 1.6 推荐调用片段（给 features / task-4；带可见性前提）

可见性前提：`GameItem.SetSpriteAndShapeFromMod` 是 **public 实例方法**（元数据 `public`）；`modId` 用 manifest `<ID>`；
`spriteKey` 是 `Textures/Items/<key>.png` 的文件名；该 PNG 必须已被 ModLoader 载入（`LoadedMod.Sprites` 有此 key；控制台可见 `item sprite(s) loaded` 计数）。

```csharp
// 集中 helper：只在状态变化时调用；idle 传 "<id>"，工作传 "<id>_on"
static void SetMachineVisual(GameItem machine, string id, bool on)
{
    if (machine == null) return;
    // 裸 key，不能写 "free_chem:gas_scrubber_on"
    machine.SetSpriteAndShapeFromMod(ChemCatalog.ModId, on ? id + "_on" : id);
}
```

使用约束（据 IL 得出的实际开销/坑）：

1. 每次调用 = `ModLoader.Mods.Find`（O(模组数)）+ 字典查 + 重建一次 `GridShape`（48×48 图标在 16px 网格下 = 3×3=9 次像素采样）。
   13 台机器只在「状态变化」时调用（每周期最多 2 次）完全没问题；**不要每帧调**。
2. key 写错/图未载入时不会抛异常，只会每次调用刷一条 Warning —— 上线前用 `LoadedMod.Sprites`/控制台计数核对 26 张 `_on` 图都在。
3. **占格不变的前提**：idle 与 `_on` 两张 PNG 的 alpha 足印必须一致（`SetSpriteAndShapeFromMod` 每次都重建 shape）。
   features 已实测 13 对 PNG 按 16px 格子中心采样的 GridShape 完全一致（13/13 SAME，含 filtration_bench 凹形），因此重复调用**不会改变占格**。
   今后新增机器图标时必须保持这条不变量（建议 qa 加断言：同 id 的 `<id>.png` 与 `<id>_on.png` 尺寸一致且格子 alpha 掩码一致）。
4. 存档重载：`SaveItemNode` 只持久化 `spriteAtlasPath` + `spritePath`，**没有** `useSpriteFromMod` 字段；
   但 `SaveManager.DecodeNodesInternal` 恢复物品时先用 `DirectoryMaster.Item(identifier, true)`（原版物品目录里的模板，模组物品模板由工厂创建，
   工厂里 `SetSpriteAndShapeFromMod` 会把 `useSpriteFromMod` 置 true），再用存档的 `spriteAtlasPath/spritePath` 调 `SetSprite(...)`，
   所以 mod 贴图解析标志来自模板、不依赖存档字段。**本条为静态推断，未做存档重载实机验证。**
5. 另一个静态观察（低风险，非本任务）：`GameItem.CloneLinked` 复制 `spritePath/spriteAtlasPath/spriteChanged`，但**不复制** `useSpriteFromMod`
   （xref 证据：`set_useSpriteFromMod` 的调用方只有 `SetSpriteAndShapeFromMod`）。若将来对 mod 物品做栈拆分/克隆，需在克隆后重新调用一次
   `SetSpriteAndShapeFromMod`。当前机器物品都是单件，暂不受影响。

---

## 2. 治安部 heat / 嫌疑度（AddHeat / BalanceHeat / CommitCrime / SEC_SUSPICION_PENALTY …）

### 2.1 结论（一句话）

- **按官方文档口径：不可行。** `ref-modding/ModdingGuide.zh-CN.md`（含 APIChanges）**没有任何**「写治安部 heat/嫌疑度/调查进度」的公开 API；
  程序集里也**没有**通用的 `AddSuspicion(int)` / `AddHeat(int)` / `SetSuspicion` 之类成员。
- **按程序集 public 成员口径：有限可行（未文档化）。** 存在 `SecData.CommitCrime(string crimeID, int amount)`（public 实例方法）+ 可获取的
  `PlayerStore.Instance.secData`（public static 属性 + public 字段），这是原版自己记犯罪的同一入口；但它有真实副作用、且随版本可能变。
- 核实时（api-researcher）**建议保持模组自有计数**（`ChemRuntime.AddExhaust` / `SuspicionFrom` / `VentMultiplierBp`）。
- **Lead 决策（2026-11-10，晚，已落地代码）**：**采用 §2.4 的 guard 方案接入原版治安部**，
  默认开启、可在模组设置关闭；实现与未验证项见 §2.5。风险事实不变：这是**未文档化**成员，官方无公开 API。

### 2.2 相关标识符的真实归属与签名（全部来自 `focus-heat.txt` / `members.txt`）

| 标识符 | 归属类型 | 签名 | 实际语义（IL） |
|---|---|---|---|
| `AddHeat` / `ClearHeat` / `GetHeat` | `PlantGeneticsHelper` | `public static void AddHeat(GameItem seed, PlantTrait trait)` / `ClearHeat(GameItem seed)` | **植物遗传热**（`GENETIC_HEAT_INT` tag），与治安无关 |
| `BalanceHeat` | `ThingContainer` | `public void BalanceHeat()` | **空方法**：IL 只有 `ret`（`tools/api-probe/out/il/ThingContainer__BalanceHeat.il.txt`） |
| `CommitCrime` | `SecData` | `public void SecData.CommitCrime(string crimeID, int amount)` | 见 §2.3 |
| `CheckCrime` | `PlayerStore` | `public void PlayerStore.CheckCrime()` | 原版把各种 tracker（脏水/错标水/劣质化学品/贿赂…）折算成犯罪，内部调 `SecData.CommitCrime` |
| `SEC_SUSPICION_PENALTY` | `LouOperativeHelper` | `public static const int SEC_SUSPICION_PENALTY = 12` | Lou 探员剧情专用常量；**没有**一个名为 suspicion 的通用数值字段，不能当通用 API |
| `SecurityInvestigationNearComplete` | `NightLog` | `public static void NightLog.SecurityInvestigationNearComplete()` | 只往夜间日志加一条本地化文本（`AddNightLog`），不改任何数值 |
| `SEC_WATCHLIST` / `SEC_PRIORITY` | `StoreReputationPerk` | public const string | 名声 perk 的 id，不是嫌疑度 |
| `OnInvestigationToolSelected` 等 | `EmporiumRTSController` | public void | 店面建造 UI 的「调查工具」选择，与数值无关 |

### 2.3 `SecData.CommitCrime` 的 IL 语义与副作用（核心证据）

文件：`tools/api-probe/out/il/SecData__CommitCrime.il.txt`（66 行）：

1. `if (StartingPerk.IsIntimidating()) amount = RNG.MultiplyAndRound(amount, 0.67, false);`
2. `if (StartingPerk.IsBlabbermouth()) amount = RNG.MultiplyAndRound(amount, 1.33, false);`
   → 即**会消耗 RNG 状态**（仅在这两个起始专精时），可能影响其它随机结果。
3. `idx = crimeTypes.IndexOf(crimeID)`；已存在则 `crimeAmount[idx] += amount; crimeAmountTotal[idx] += amount;`
   否则 `crimeTypes.Add(crimeID); crimeAmount.Add(amount); crimeAmountTotal.Add(amount);`
4. 方法到此结束：**不发事件、不当场罚款、不立即生成检查员**；后续由原版夜间/开店流程（`SecData.OnNewDay`、`PlayerStore.HandleSecurityWarning`、
   `StoreClientManager.HandleInspectionClient` 等，均读取 `PlayerStore.secData`）自行推进证据度/调查/判罚。

可读取的公开数据（用于展示/自检）：`SecData.crimeTypes/crimeAmount/crimeAmountTotal`（public List）、`SecData.evidenceLevel`（public int）、
`SecData.GetCrimeIndex(string)`、`SecData.GetTotalCrimeValue()`、`SecData.GetCrimeCount()`、`SecData.GetSuspectedCrimeDisplay()`、`SecData.GetEvidenceLevelDisplay()`。
犯罪类型用 `SecData` 上的 public const string：`ENDANGERMENT` / `FRAUD` / `BRIBERY` / `CONTRABAND_TRAFFICKING` / `WEAPON_TRAFFICKING` /
`DRUG_TRAFFICKING` / `CARD_TRAFFICKING` / `FENCING` / `OPERATING_WITHOUT_LICENSE` / `MANSLAUGHTER`（常量值见 `members.txt`，值即字符串本身）。

实例获取路径（IL 证据）：

- `PlayerStore.Instance`：`public static` get；**惰性创建**（`instance == null` 时 `new PlayerStore()` 并 `Debug.Log("A new PlayerStore has been created")`）——
  **这是坑**：在菜单/无存档时调用会凭空创建一份 store 数据。必须先用 `PlayerStore.HasInstance`（public static get，`instance != null`）判空。
- `PlayerStore.secData`：public 字段，`PlayerStore..ctor` 里 `new SecData()` 初始化（非 null）。
- `ModHelper.SetModData*` 内部也是先 `PlayerStore.Instance` 再 `PlayerStore.ModData()`——它们**不会**惰性创建出可用数据（`Instance` 自动建出来的 store 里 `ModData()` 可能返回 null）。

### 2.4 接入原版的 guard 方案（原「可选，默认不建议」；第 13 轮已被 Lead 决策采用，见 §2.5）

```csharp
// 可见性前提：PlayerStore 与 SecData 都是 public 类；Instance/HasInstance 是 public static 属性；
// secData 是 public 字段；CommitCrime 是 public 实例方法；ENDANGERMENT 是 public const string。
// 前置：仅在「已载入本局存档/游戏内」调用；绝不能在 Init/OnEnable（主菜单）调用。
void CommitExhaustEndangerment(int units)
{
    if (!PlayerStore.HasInstance) return;             // 必须！否则 Instance 会惰性新建 store
    var sec = PlayerStore.Instance.secData;            // ctor 中初始化，正常非 null，仍建议判空
    if (sec == null || units <= 0) return;
    sec.CommitCrime(SecData.ENDANGERMENT, units);      // 真实写入原版治安系统
}
```

风险清单（据 IL，逐条）：

1. 写入的是**真实犯罪记录**（`crimeAmount` + `crimeAmountTotal`），会推进原版调查/证据/判罚，玩家无法区分「排气」与「其他犯罪」；
2. 起始专精 `IsIntimidating/IsBlabbermouth` 会改变数值并消耗 RNG；
3. 原版每次登记量级是 20×n（脏水）、300（劣质化学品/弹药）这种；「每次排气 units=1」在数值上几乎不可见，要产生可感知效果就得写大数，
   而大数会直接推动判罚——数值设计需要实机调，不能静态确定；
4. 非官方文档化 API，随版本可能改名/改语义（本次核对就发现 HANDOFF 里 3 个 API 签名/归属描述有误，见 §6）；
5. 若只是为了「王尔德排风服务降低嫌疑增长」，模组自有计数 + 倍率（features 已实现）完全够用，且不影响原版平衡。

**因此：默认结论「不可行（无官方 API）」，保持模组自有计数；§2.4 仅作为需要原版联动时的备选，需 lead 明确决策并如实告知用户未官方支持。**

> ⚠️ 该「核实时默认结论」已于 2026-11-10 被 Lead 的接入决策覆盖，见下节 §2.5。

---


### 2.5 Lead 决策与实现（2026-11-10，本段由 docs-i18n 代 Lead 记录）

**决策**：采用 §2.4 方案，把有毒气体排放按倍率折算写入原版 `SecData.ENDANGERMENT`，
以满足「排放过多有毒气体提高治安部嫌疑度」的需求；因使用未文档化成员，做成**默认开启、可在模组设置关闭**。

**代码落点**（`src/ChemRuntime.cs` / `src/ModEntry.cs`）：

- `ChemRuntime.VanillaSecurityEnabled = true`（模组设置项 `vanilla_security`，`Gameplay` 分类；
  ModEntry 用 `config.Define("vanilla_security", true, ...)` 注册，本地化键 `cfg_vanilla_security` / `cfg_vanilla_security_tip`）。
- `ChemRuntime.VanillaEndangermentPerUnit = 10`（每个排气嫌疑单位折算 10 点 `ENDANGERMENT`）。
- `AddExhaust(units)` → `CommitVanillaEndangerment(units)`：
  1. `VanillaSecurityEnabled == false` → 直接 return（关闭后只保留模组自有计数）；
  2. `if (!PlayerStore.HasInstance) return;`（**绝不**在菜单/无存档时触发 `Instance` 惰性新建 store）；
  3. `sec = PlayerStore.Instance.secData`，判空；
  4. `amount = units × VanillaEndangermentPerUnit × VentMultiplierBp / 10000`；
  5. `sec.CommitCrime(SecData.ENDANGERMENT, (int)amount)`，整体 try/catch + log（失败不影响玩法）。
- 排风服务倍率（10000 / 5500 / 1200 = ×1.00 / ×0.55 / ×0.12）**同时**作用于模组计数增速与原版写入量。

**未验证项（如实列出）**：

1. **写入量平衡**：10/单位是静态设计值，未在游戏内调过；原版自身登记量级是 20×n（脏水）、300（劣质化学品）这种，
   排气单次 units=1~2 → 10~20 点，是否产生可感知的调查推进**未验证**。
2. **调查节奏**：是否会让正常游玩的玩家被无故调查/判罚，**未验证**。
3. **专精 RNG 影响**：`StartingPerk.IsIntimidating()` / `IsBlabbermouth()` 会改数值并消耗 `RNG` 状态，
   是否影响其它随机结果**未验证**。
4. **关闭开关后的零写入**：静态可证（关闭时直接 return），但未做游戏内验证。
5. 非官方 API，随游戏版本可能改名/改语义。

**风险与回退**：默认开启属产品决策；玩家可在模组设置关闭，关闭后行为退回「仅模组自有计数 + 提示框」。

---

## 3. ModHelper ModData 全套签名（防踩坑）

全部为 `public static`，元数据与 IL 已核（`tools/api-probe/out/focus-moddata.txt`、`il/ModHelper__*.il.txt`）：

| 签名 | 行为（IL） |
|---|---|
| `void SetModData(string modId, string key, string value)` | 值 `null` 会被替换成 `""`；底层 `PlayerStore.modData[modId + ":" + key]` |
| `string GetModData(string modId, string key, string fallback = null)` | 无此 key 返回 fallback（默认 null） |
| `bool HasModData(string modId, string key)` | 字典 ContainsKey |
| `bool RemoveModData(string modId, string key)` | 字典 Remove |
| `List<string> GetModDataKeys(string modId)` | 返回去掉 `"modId:"` 前缀后的 key 列表 |
| `int ClearModData(string modId)` | 删掉本模组全部 key，返回删除条数 |
| `void SetModDataInt(string modId, string key, int value)` | `value.ToString(CultureInfo.InvariantCulture)` → `SetModData` |
| `int GetModDataInt(string modId, string key, int fallback)` | `GetModData` → `int.TryParse(s, NumberStyles 7 = Integer, InvariantCulture, out)` 失败返回 fallback |
| `void SetModDataFloat(...)` / `float GetModDataFloat(...)` | 同 int 模式（InvariantCulture） |
| `void SetModDataBool(...)` / `bool GetModDataBool(...)` | 同 int 模式（bool 存成 `"True"/"False"` 字符串，读取用 TryParse） |
| `string GetModDataFolder(string modId)`、`ModDataFileExists / ReadModDataFileBytes / ReadModDataFileText / WriteModDataFileBytes / WriteModDataFileText / DeleteModDataFile / ListModDataFiles` | 模组数据目录的文件读写 API；**Capabilities 为空时禁止文件访问（HANDOFF §1）**，FreeChem 不要用这一组，只用 key-value |

关键行为：

- 存储介质是 `PlayerStore.ModData()` 返回的 `Dictionary<string,string>`——**按本局存档保存，只在「游戏内已载入一局」时可用**。
  `ModDataStore(bool warn)` 的 IL：`PlayerStore.Instance == null` 或 `PlayerStore.ModData() == null` 时，若 warn 则
  `Debug.LogWarning("[ModHelper] Mod data is only available while a run is loaded")`，然后返回 null → `SetModData` 静默 no-op。
  **所以：`Init`/`OnEnable`（主菜单）里调 SetModData 不会保存任何东西**，必须在 `OnGameLoaded`/游戏内钩子里做。
- key 命名空间是 `"modId:key"`（`ModHelper.ModDataKey` IL：`string.Concat(modId, ":", key)`），因此不同模组不会冲突，同一个 key 在模组内唯一。
- `GetModDataInt` 用 InvariantCulture + `NumberStyles.Integer`：`"3.0"`/`" 3 "` 等解析会失败（`NumberStyles.Integer` 允许前后空白与符号，但不允许小数点）。
  本项目的 `exhaust_raw` 只需整数，没问题。
- ModData 在「读取/修改」时不校验 modId 是否已注册；也不会自动清理。

本项目的用法（features 已实现）符合以上语义：`ChemRuntime` 里 `ExhaustRaw` 用 `SetModDataInt/GetModDataInt("free_chem","exhaust_raw",0)`，在 `OnGameLoaded` 恢复。

---

## 4. 游戏版本号（manifest `<GameVersions>` 用）

### 4.1 结论

**GAME_VERSION: 0.46**（`Application.version` 规范化后的值）

- 现 manifest `FreeChem/manifest.xml` 写的是 `<Version>1.0</Version>` → **与实测版本不匹配**，应改为 **`<Version>0.46</Version>`**（manifest 归 lead 管，请 lead 修改）。
- 版本不匹配的后果：**不阻断加载**。`GameVersions` 在本版本程序集里只被 `ModUIManager.GetModIssue` / `FormatGameVersions` 读取，
  用于「模组」菜单的兼容性着色/警告文本（`ui_mods_issue_version`）；`ModLoader.CheckEnableCore` 与 `ModLoader.ParseManifestCore` **都没有**读取
  `Application.version`（xref：`Application::get_version` 的调用方只有 `ModLoader.LogAudit`、`ModTrust.Approve`、`ModUIManager.GetModIssue`、
  `ModUIManager.VersionMatchesGame`）。所以写错只是 UI 显示「版本不兼容」警告，不会导致模组被扫描器拒绝。
- 但为了 Mods 菜单不误报 + 信任记录里的 `gameVersion` 正确，建议仍然改成 0.46。

### 4.2 证据链（为什么是 0.46）

1. `Assembly-CSharp.dll` **没有** `AssemblyInformationalVersion` / `AssemblyFileVersion` 特性（§0）。
2. `ModUIManager.VersionMatchesGame(string version)` 的 IL：`NormalizeVersion(version)` 与 `NormalizeVersion(Application.version)` 比较，
   相等或互为「前缀 + `.`」即匹配（`"1.0"` vs `"1.0.3"` 这种会匹配）。
   `NormalizeVersion`：`Trim()` → `TakeWhile(c => char.IsDigit(c) || c == '.')` → `new string(...)` → `TrimEnd('.')`
   （`ModUIManager+<>c.<NormalizeVersion>b__90_0` 的 IL：`char.IsDigit(c) || c == '.'`）。
3. `Application.version` 来自 Unity 的 `PlayerSettings.bundleVersion`，存放在 `Probably Stolen_Data/globalgamemanagers`。
   实测该文件 offset `0x1230` 处的序列化字符串（Unity 长度前缀 int32=5 + UTF-16/ASCII）为 **`0.46D`**：
   `05 00 00 00 30 2e 34 36 44`（`globalgamemanagers` 310,140 bytes）。
   规范化后为 **`0.46`**（`D` 被 `TakeWhile` 丢弃）。
4. `PlayerStore.VERSION`（public static readonly string）实测为 **`"VERSION 100-Rev13"`**（`PlayerStore..cctor` 的 IL：`ldstr "VERSION 100-Rev13"`），
   它只用于存档预览/主菜单/日志显示（xref：`SavePreviewData.GenerateData`、`MainMenuUIController`、`StoreUIManager`、`PlayerStore.StartNewGame`），
   **不是** `<GameVersions>` 的比对对象，也不是 `Application.version`。
5. 官方范例 `ref-modding/Mods/LearnerMod/manifest.xml` 写 `<Version>0.46</Version>`——与本机实测一致（官方指南编写时的版本）。

### 4.3 未验证项

- 没有启动游戏读取运行时 `Application.version` 的真实返回值（静态推断依据是 `globalgamemanagers` 的 bundleVersion 与官方范例）。
  若 QA 需要 100% 确定，唯一办法是游戏内控制台/F8 或看 Mods 菜单版本警告是否消失（需人工实机）。
- 若游戏后续更新（Steam buildid 变化），该值需重新核对 `globalgamemanagers`。

---

## 5. 顺带核对（features/qa 会用到，均来自元数据 + IL）

### 5.1 `ModHelper.AddNetworkUpgrade` —— 真实签名与 HANDOFF 记录**不一致**（重要）

元数据（含默认值）：

```csharp
public static void ModHelper.AddNetworkUpgrade(
    string modId, string id, int cost, string subtitle,
    System.Func<string> title, System.Func<string> description, System.Action onUnlock,
    string tab = null, string iconKey = null, string[] prerequisites = null, int cooldownDays = 0,
    System.Func<string> subtitleLabel = null, System.Func<string> alreadyBought = null, bool recommended = false)
```

- `title` / `description` 是 **`Func<string>`**（每次开窗求值，支持语言切换），不是 `string`；`subtitleLabel` / `alreadyBought` 也是 `Func<string>`。
- 官方指南（ModdingGuide.zh-CN.md:947-994）与元数据一致；**HANDOFF §3.4 的旧记录（title/description 为 string、参数顺序不同、缺 subtitleLabel/recommended）是错误的**，
  现有 `src/ModEntry.cs` 用命名实参调用，恰好与真实签名一致（编译通过即为证），无误。
- `ModHelper.RemoveNetworkUpgrades(string modId)` → `NetworkUpgrade.RemoveModUpgrades(modId)`（int 返回）。
- `ModHelper.IsNetworkUpgradeUnlocked(string id)`：`PlayerStore.Instance == null || PlayerStore.Instance.networkUpgrade == null` 时返回 `false`，否则
  `NetworkUpgrade.IsUnlocked(id)` —— 在 `OnEnable` 里调用是**安全**的。
- `ModHelper.SetNetworkTabLabel(string tab, Func<string> label)`：**只有 2 个参数**（tab 名 + 取标签的函数），没有 modId 参数。
  ⚠️ 截至 2026-11-10 08:45 核对：`src/ModEntry.cs` 里是 `SetNetworkTabLabel(ChemCatalog.ModId, () => ...)`，即把 `"free_chem"` 当成了 **tab 名**；
  而 4 个 `AddNetworkUpgrade` 调用都**没有传 `tab:`**，按官方指南「`tab` 留空 = 第一个标签页（基础）」，
  → 气体商人/订单/排风服务实际会全部出现在**默认「基础」标签页**，而不是自定义标签页。
  若想在自定义标签页显示：给每个 `AddNetworkUpgrade` 加 `tab: "free_chem"`（并保留那行 `SetNetworkTabLabel("free_chem", ...)`）。
  （这条属于 features/task-3 的收尾建议，写作用域在 features；本文只记录核对事实。）

### 5.2 `iconKey` 格式

`AddNetworkUpgrade(iconKey:)` 的 IL 只是把字符串存进 `NetworkUpgrade.iconKey`；渲染时走与 `AddImage` 相同的查找，
官方指南明确写 `iconKey: "my_mod_id:courier_icon"`（`"modId:spriteName"`）。所以 `free_chem:gas_scrubber` / `free_chem:condenser_unit`
格式**正确**（注意：这是 UI 图标格式，与 §1 物品贴图的裸 key 不同，两者不要混）。

### 5.3 机器状态与协程

- `MachineProgressHelper.GetMachineState(GameItem)`（public static）：`machine.GetTagReadonly("MACHINE_STATE_TAG").GetString()`。
- 常量（public const string）：`MachineProgressHelper.MACHINE_STATE_TAG = "MACHINE_STATE_TAG"`、`STATE_WORKING = "STATE_WORKING"`、`STATE_READY = "STATE_READY"`；
  进度相关 `MACHINE_PROGRESS_CURRENT_TAG` / `MACHINE_PROGRESS_TARGET_TAG`（`members.txt` 有解码值）。
- `ModHelper.StartCoroutine(string modId, IEnumerator routine)`（public static）：先 `RequireCaller(modId, "start a coroutine for")` 校验调用方，
  `routine == null` 抛 `ArgumentNullException`。与 HANDOFF 一致。

### 5.4 `LoadedMod` / sprite 字典

- `LoadedMod` 是 public 类，`public Dictionary<string, Sprite> Sprites`、`public Dictionary<string, Sprite> PaperSprites`、
  `public Dictionary<string, Texture2D> Textures`、`public Dictionary<string, AudioClip> Sounds`、`public ModManifest Manifest`。
  `ModLoader.Mods` 是 `public static List<LoadedMod>`。
- 所以除模型外，Mod 也可以直接 `ModLoader.Mods.Find(m => m.Manifest.ID == "free_chem").Sprites["xxx"]` 取 sprite（只读用），
  但**不要**用它绕过 `SetSpriteAndShapeFromMod` 去手写 `spritePath`（会缺 shape/标志，见 §1.5）。
- `CustomUIManager.Instance.GetSprite("modId:spriteName")` 是 UI 侧解析入口（`PaperUI.ResolveSpriteKey`、`CustomUIElement.SetSprite(string)` 同理）。

---

## 6. 对 HANDOFF.md 的更正（供 lead 更新交接卡）

1. **§3.7**：「`SetSprite` / `SetSpriteFromCapacity` 的签名未知」→ 已核对：`GameItem.SetSprite(string spriteAtlasPath, string spritePath)`；
   `SetSpriteFromCapacity` **不在** `GameItem` 上，在 `VendingMachineHelper`（public static，参数 `GameItem, string prefix, int currentCapacity`）。
   同段列出的 `UpdateSprite`/`RefreshSprite`/`SetIcon` 也**不在** `GameItem` 上（§1.5 有归属清单）。
2. **§3.4**：`AddNetworkUpgrade` 的签名/参型有误，正确版本见 §5.1（title/description 是 `Func<string>`，tier 与默认值不同）。
   另外现行 `src` 的 `SetNetworkTabLabel` 用法把模组 ID 当 tab 名、且 `AddNetworkUpgrade` 未传 `tab`，导致自定义标签页实际未使用（§5.1）。
3. **§3.5**：结论应从「签名不明」升级为「**官方无 API → 不可行**；程序集层面存在未文档化的 `PlayerStore.Instance.secData.CommitCrime(...)` 可行但有真实副作用（§2）」。

---

## 7. 未验证项（如实声明）

以下均**未做游戏内验证**，只是静态结论：

- 运行时换图是否真的在已打开的背包/机器窗口中即时可见（静态证据链完整，但未实机点开确认）；
- 存档 → 重载后 mod 贴图是否仍正确（§1.6 第 4 条为静态推断）；
- `SecData.CommitCrime` 接入后的实际游戏表现与数值平衡；
- 运行时 `Application.version` 的真实字符串（§4.3）；
- 26 张 `_on` 图标在 `LoadedMod.Sprites` 中的实际载入情况（需游戏内控制台 `item sprite(s) loaded` 计数核对）。

---

## 8. 调试生成与设置入口（第 14 轮）

> 本节对应第 14 轮需求：模组设置入口（含调试开关）+ 私有数字 ID + F9 调试面板入口 + 自定义调试窗口输入指令生成物品 + 物品进**后柜台** + 满时原版风格弹窗。
> 全部结论来自元数据/IL 静态核对（探针 `tools/api-probe/`，本轮新增 selectors 已写入 `run.ps1`；`out/il/` 现共约 410 个方法）。

### 8.0 结论速览

| # | 问题 | 结论 |
|---|---|---|
| 1 | `EmporiumEntry.Instance` + `backInvinvElement` 在店铺场景可用？ | **可行**（public static get + public get；注意 `Instance` 是场景对象、`backInvinvElement` 在 `Start` 才赋值，且 `Instance` 销毁后是 Unity 伪 null） |
| 2 | `GraphUtils.CanAccept` 只读？`TryAcceptAll` 返回什么？ | **CanAccept 只读探测**；`TryAcceptAll` 返回**剩余未接受数量**（`amount - accepted`，默认 amount=unitCount，所以 **0=全部成功**，不是 0=失败） |
| 3 | `ModHelper.SpawnItem` 何时 null？ | **几乎不会 null**：未知 id 会 LogWarning 并返回 dummy `GameItemElement`（不是 null）；不能靠 null 判非法 id → 必须先用自家数字 ID 表校验；且内部触到 `PlayerStore.Instance`，应在游戏内调用 |
| 4 | `AddDebugFunction` / `RemoveDebugFunctions` 语义、注册时机 | **可行**，`OnEnable` 注册安全；按钮进 F9 面板（静态表，面板打开时读取/重建）；回调在主线程、异常被捕获。**但 F9 面板本身需要 `dev.txt`**（本机安装目录没有 → F9/F8 默认不可用） |
| 5 | `CustomUIManager.Window` / builder / element setter / 层 / 风格 | **可行**；`CustomUIManager` 是 DontDestroyOnLoad 单例，`LAYER_OVERLAY` 恒可用、场景层缺 anchor 会警告并回落 overlay；原版 F9 面板与 Item Spawner 用 **"rear" + 默认 game 风格**，最接近游戏内窗口 |
| 6 | `ModConfig.Define(bool)` / `OnChanged` / 设置面板入口 / section 本地化 | **可行**；入口＝Mods 菜单该模组条目的设置齿轮（主菜单）；section 是**原样字符串、不本地化**（label/tooltip 是 `Func<string>` 可本地化） |
| 7 | 有现成原版弹窗/警告 API？ | **有：`GenericConfirmationUIManager`**（public 单例 + `OpenPanelSingleButton`），原版自己就在店内用它做提示（证据见 §8.5） |
| 8 | `CustomUIElement` 链式 setter | 全部 public（`SetText`/`SetVisible`/`OnClick`/…，清单见 §8.7），`SetText` 对缺失组件空安全（Warn 后 no-op） |

### 8.1 后柜台容器 `EmporiumEntry.backInvinvElement`

成员（`focus-modloader.txt` / `members.txt`）：

- `public static EmporiumEntry Instance { public get; private set; }`（`[compiler-generated]`）
- `public GameGridInventory backInvinvElement { public get; private set; }`（backing field `private GameGridInventory <backInvinvElement>k__BackingField`）
- 同类还有 `backInvWindow`、`backInvinvElementCounter`、`frontInvinvElement`、`invElement`（玩家背包）、`showcaseElement`、`afterhourInventory`、`responseWindow/responseInventory` 等。
- 私有常量 `COUNTER_WIDTH = 18`、`COUNTER_HEIGHT = 4`（只作容量参考，非 public，不能依赖）。

可用性（IL 证据）：

- `EmporiumEntry.Awake`（`il/EmporiumEntry__Awake.il.txt`）：单例判断后 `set_Instance(this)`；**没有** `DontDestroyOnLoad` → 它是**店铺场景内的 MonoBehaviour**，只在店铺场景存在。
- `EmporiumEntry.OnDestroy`（`il/EmporiumEntry__OnDestroy.il.txt`）：只移除 Letterbox 回调，**没有把 Instance 置 null**。
  因此离开店铺场景后 `Instance` 指向已销毁对象；由于 `EmporiumEntry : UnityEngine.Object`，`Instance == null` 会走 Unity 重载的 `op_Equality` 返回 true——**用它判空是安全且必须的**；但直接对已销毁对象取 `backInvinvElement` 会抛 `MissingReferenceException`。
- `EmporiumEntry.Start`（`il/EmporiumEntry__Start.il.txt`，43KB）里才 `set_backInvinvElement(...)`/`set_backInvWindow(...)`/`set_invElement(...)` 等 → **不能在模组 `OnEnable`/主菜单期使用**；在店铺场景加载完成后的钩子（`OnGameLoadedInit`）或游戏内按钮回调中使用。

`ModHelper.MoveItemToBackCounter(GameItem)` 的完整 IL（`il/ModHelper__MoveItemToBackCounter.il.txt`，19 字节）：

```
call EmporiumEntry::get_Instance()
callvirt EmporiumEntry::get_backInvinvElement()
ldarg.0
ldc.i4.m1                    // amount = -1
call GraphUtils::TryAcceptAll(GraphNodeStorage, GameItem, int) : int
pop                          // ← 返回值被丢弃
ret
```

结论：它**没有 null 保护**（不在店铺/未初始化时 NRE/MissingReference），且**满仓时既不提示也不销毁物品**（返回值直接 `pop`）→ 直接用它做「生成到后柜台」会静默丢物品，**不满足本轮需求**；需要自己先探测再放置/销毁/弹窗（§8.8 片段）。

### 8.2 满仓探测与放置：`GraphUtils.CanAccept` / `TryAcceptAll` / `TryAcceptAllOrDestroy`

| 方法 | 签名（均 public static） | IL 语义 |
|---|---|---|
| `CanAccept` | `bool CanAccept(GraphNodeStorage currentNode, GameItem item)` | **只读探测**：`currentNode.TryFindOneValidInventorySlot(item) != null` 即返回 true；不移动物品、不改 `parentInventory`（`il/GraphUtils__CanAccept.il.txt`） |
| `TryAccept` / `TryAcceptOnce` | `int TryAccept(GraphNodeStorage, GameItem)` / `int TryAcceptOnce(GraphNodeStorage, GameItem, int amount = -1)` | 单次/单个放置入口 |
| `TryAcceptAll` | `int TryAcceptAll(GraphNodeStorage currentNode, GameItem item, int amount = -1)` | `amount < 0` → 取 `item.unitCount`；`amount <= 0` → 返回 0；最多 40 轮 `TryFindOneValidInventorySlot` + `SlotMarker.TryAcceptOnce(amount - accepted)`；**返回 `amount - accepted` = 剩余未接受数量**（`il/GraphUtils__TryAcceptAll.il.txt`）。默认调用下 **返回 0 表示全部放进去了**，不是 0=失败 |
| `TryAcceptAllOrDestroy` | `bool TryAcceptAllOrDestroy(GraphNodeStorage currentNode, GameItem item, string context = null)` | 先 `TryAcceptAll(...,-1)`（丢弃返回值），再 `DestroyIfUnplaced(item, context)`；**返回 true = 物品已有 parentInventory（放成功），false = 没放下且已被销毁**（`il/GraphUtils__TryAcceptAllOrDestroy.il.txt`）→ **推荐用这个**，失败自动 Destroy |
| `DestroyIfUnplaced` | `bool DestroyIfUnplaced(GameItem item, string context = null)` | item null→false；`parentInventory != null`→true；已是销毁态→false；否则 `unitCount>0` 时 `ReportUnplaced(identifier, unitCount, context)`，再 `item.Destroy()`→false（`il/GraphUtils__DestroyIfUnplaced.il.txt`） |
| `ReportUnplaced` | `void ReportUnplaced(string identifier, int count, string context = null)` | 只把计数累加进静态字典，下一帧 `LetterboxManager.RunNextUpdate(FlushUnplaced)` **打到日志**（F8/Player.log），不弹任何 UI（`il/GraphUtils__ReportUnplaced.il.txt`） |
| `EmporiumTryAdd` | `void EmporiumTryAdd(GraphNodeStorage, GameItem)` / `void EmporiumTryAdd(GameItem)`（后者= 以 `invElement` 为起点） | 原版「生成到店里」的级联：先试给定容器 → `HopperHelper.TryRouteOverflow` → 地图外时 afterhour → 后柜台 → 玩家背包 → 展示柜 → 前柜台；**全满时 `DestroyIfUnplaced(item, "EmporiumTryAdd: every store inventory is full")`**（原版行为＝销毁+日志，无弹窗） |

> 原版 `CustomUIItemSpawner.Spawn`（F9 > Spawn 的「Item Spawner」）用的正是 `DirectoryMaster.Item(id, true)` + `GraphUtils.EmporiumTryAdd(item)`，
> 即**原版生成也会在满仓时静默销毁并只写日志**（`il/CustomUIItemSpawner__Spawn.il.txt`）。本轮要求的「满仓弹窗」是模组自己加的 UX。

**常见误用（本轮实测代码里出现过，必须改）**：把 `TryAcceptAll` 的返回值当成「已接受数量」写 `if (placed <= 0) 失败` 是**反的**——`TryAcceptAll` 返回的是「剩余未接受数量」，
成功（单个非堆叠物品单元）返回 **0**，失败返回 `unitCount`（>0）。正确写法：
`int remaining = GraphUtils.TryAcceptAll(back, item, -1); if (remaining > 0) { /* 没放完/没放下 */ }`
但更推荐直接用 `GraphUtils.TryAcceptAllOrDestroy(back, item, context)`：它内部先 `TryAcceptAll`，没放下时自动 `DestroyIfUnplaced`（含 ReportUnplaced 日志）并返回 false，**不会重复销毁**，也不需要调用方再 `Destroy()`。
（`CanAccept` 只是单槽只读探测，理论上仍有极小竞态/多单位差异；先 `CanAccept` 早失败 + 再 `TryAcceptAllOrDestroy` 兜底，既省一次 spawn 又不会漏销毁。）

### 8.3 `ModHelper.SpawnItem` 的 null 条件与调用时机

调用链：`ModHelper.SpawnItem(id)` → `ItemSpawner.Spawn(id)`（`il/ModHelper__SpawnItem.il.txt`、`il/ItemSpawner__Spawn.il.txt`）：

1. `id != null && PreBuiltItemHelper.preBuiltItems.ContainsKey(id)` → 调用预构建工厂（原版某些物品/装备的快捷构造）。
2. 否则 `DirectoryMaster.Item(id, true)`。

`DirectoryMaster.Item(string identifier, bool isOwned = true)` IL（`il/DirectoryMaster__Item.il.txt`，61 行）：

- `identifier == null` 或 `DirectoryMaster.Create(identifier) == null` → `Debug.LogWarning("Could not find GameItem identifier '<id>' when attempting to create GameItem! Creating dummy GameItem instead...")` → **`ItemDirectory.CreateEmptyItem(identifier)` 返回一个空壳 GameItem（非 null）**；
- 成功时：`GeneralHelper.SetItemOwned(item, isOwned)`、若 `uniqueId == 0` 则 `PlayerStore.Instance.GetUniqueID()`（**会触到 PlayerStore.Instance**，主菜单调用可能惰性新建 store）、`PermitHelper.OnNewItemCreated(item)`。

结论：

- **`SpawnItem` 基本不会返回 null**；未知 id 得到的是 dummy 空壳（identifier 就是传入值，无 sprite/无注册数据），只在预构建工厂内部返回 null 时才会拿到 null。
- **不能用 `it == null` 判非法 id**。features 计划里已有 `ChemItemIds.TryGetId` 前置校验——保留它就够了；dummy 兜底检查可选（例如 `it.GetDisplayName(true)` 或 sprite 判断都不可靠，不建议依赖）。
- 应在**游戏内**调用（内部会访问 `PlayerStore.Instance`；`Move*` 系列在主菜单会 NRE，官方指南明确要求放在 `OnGameLoadedInit` 或自己的 UI 回调里）。

### 8.4 F9 调试入口：`AddDebugFunction` / `RemoveDebugFunctions`

签名与实现（`il/ModHelper__AddDebugFunction.il.txt`、`il/CustomUIDebugPanel__Add.il.txt`）：

- `public static void ModHelper.AddDebugFunction(string modId, string category, string name, Action action, bool tall = false)`
  → `CustomUIDebugPanel.Add(category, name, action, tall, modId, info: null)`（`source=modId` 用于 Remove 和 tooltip）。
- `public static int ModHelper.RemoveDebugFunctions(string modId)` → `CustomUIDebugPanel.Remove(modId)`：按 `source` 移除全部并返回数量，若移除了会 `RebuildIfOpen()`。
- `CustomUIDebugPanel.Add` 语义：
  1. `category == "Marketing"` 时：`DevTools.IsFullEnabled ? "Scene" : "Special"`；
  2. 不在 `CATEGORIES` 里 → `Debug.LogWarning("[CustomUI] Debug panel: unknown category '<x>' for '<name>', using Special")` → 落入 `Special`；
  3. 把 `Entry{category,name,action,tall,source,info}` 加进静态 `entries` 列表；
  4. 若面板**当前打开**（`RebuildIfOpen`：对每个 PanelDef，`CustomUIManager.Instance.GetWindow(id)` 存在且 `IsVisible` 时重建）→ 重建，否则什么都不做。
- `Run(entry)`：`try { action(); } catch (e) { Debug.LogError("[CustomUI] Debug function '<name>' threw: <e>") }` → **异常被捕获，不会拖垮 F9 面板**；按钮回调来自 UI `onClick`，运行在 Unity 主线程。
- 分类常量（`ModHelper` 上 public const string）：`DEBUG_CATEGORY_STORE="Store"`、`COMMAND="Command"`、`SPAWN="Spawn"`、`SPECIAL="Special"`、`MODDING="Modding"`、`MARKETING="Marketing"`。F9 主面板四列 = Store / Command / Spawn / Special；`MODDING` 是独立窗口（主面板 Special 的 "Modding Panel" 按钮打开）。
- 文档依据：`ref-modding/ModdingGuide.zh-CN.md:166-200`（在 `OnEnable` 注册、菜单打开时读取/自动重建、异常捕获、受限构建无 F9）。

**关键限制：F9 面板本身默认关闭**

- `DevTools.IsEnabled`（`il/DevTools__get_IsEnabled.il.txt`）= `Application.isEditor || File.Exists(<dataPath>/../dev.txt)`；
  `DevTools.IsFullEnabled` = `Application.isEditor || File.Exists(<dataPath>/../1dev.txt)`；路径由 `DevTools.MarkerPath`：`Path.GetFullPath(Path.Combine(Application.dataPath, "..", markerFile))`。
- 官方指南 `ModdingGuide.zh-CN.md:130-142`：**F8 控制台和 F9 开发菜单在正式游戏中默认关闭，直到游戏可执行文件旁存在空的 `dev.txt`**；试玩/测试版构建里根本没有这些工具。
- 本机实测：`D:\SteamLibrary\steamapps\common\Probably Stolen\` 下**没有** `dev.txt`，也没有 `1dev.txt`（只有 exe / UnityPlayer.dll / CrashHandler）→ **当前安装下 F9 面板与 F8 控制台不会打开**，`AddDebugFunction` 注册的按钮不会显示。
  → 要让「F9 → Command → FreeChem 按ID生成」可用，需要在该目录放一个空 `dev.txt`（仅开发机；官方明确说不要随模组发布）。
  → 若不想依赖 dev.txt，需要另加入口（例如模组设置里加一个「打开调试窗口」按钮，或用游戏内物品/机器交互打开同一窗口）；这属于 features/lead 决策，本文只给事实。
- 注册时机：**`OnEnable` 安全**（`Add` 只在面板已打开时才碰 `CustomUIManager`，且 `RebuildIfOpen` 对 `Instance == null` 直接 return）；`OnDisable` 调 `RemoveDebugFunctions(modId)` 清理。

### 8.5 原版弹窗/警告 API（结论：**存在，可行**）

本轮找到的「最接近原版」文本弹窗是 **`GenericConfirmationUIManager`**（public MonoBehaviour，`il/GenericConfirmationUIManager__*.il.txt`）：

```csharp
public static GenericConfirmationUIManager Instance { public get; private set; }   // Awake 里赋值
public void OpenPanel(string bodyText, string confirmText, string cancelText,
                      System.Action onConfirm = null, System.Action onCancel = null);
public void OpenPanelSingleButton(string bodyText, string confirmText, System.Action onConfirm = null);
public void ClosePanel();
// public 字段：bodyTMP / confirmButtonTMP / cancelButtonTMP / panel / confirmationDarkness / confirmButton / cancelButton
```

IL 语义（`OpenPanelSingleButton`）：`bodyTMP.text = bodyText; confirmButtonTMP.text = confirmText; cancelButton.SetActive(false);
onConfirm = 回调; onCancel = null; panel.SetActive(true); confirmationDarkness.SetActive(true);`。
`ConfirmButtonPressed()`：有回调则 `onConfirm()`，否则跳过，然后 `ClosePanel()`（`panel`/`confirmationDarkness` 关闭）。`OpenPanel` 是双按钮版（显示取消按钮并设 `cancelButtonTMP`、`onCancel`）。

**原版自己就在店铺/地图场景用它做提示**（xref 证据，`out/xref.txt`）：

- `PlayerStore.ShowDisplayCaseTimingTipOnce`（店内首次展示柜提示，`OpenPanelSingleButton`）
- `MapUIManager.CheckItemLeftOnGround`（地图上物品遗留提示，`OpenPanel`）
- `MaterialDirectory.UnpackOrWarn`（拆包警告，`OpenPanel`/`ClosePanel`）
- `AmenitiesItemDirectory.ShowPurifierMessage`（店内设备提示，`OpenPanelSingleButton`）
- `WineHelper.TryStartFermentation`（酿酒提示，`OpenPanelSingleButton`）

→ 在店铺场景里应当可用（使用时仍要判 `GenericConfirmationUIManager.Instance != null`；它也是场景对象，Awake 赋 Instance）。
注意 `bodyText` 是 TMP 文本，支持 rich text / `<sprite>`；「柜台已满，无法加入新物品。」直接用一条本地化键传入即可。

**不适用/不可行的候选**：

- `ConfirmPanelUIManager`（yes/no 双按钮）：public 方法 `OpenPanel(body, yesAction, noAction, hideNoButton=false)`，但**没有静态 Instance**，且调用方全是**主菜单**（`MainMenuUIController`、`ModUIManager`、`SaveSlotElement`、`NewGameDifficultyUI`）→ 店铺场景不保证存在，不推荐。
- `EmporiumEntry.SetResponseWindow(bool closable, List<(GameItem, Action)> choices, string title="Choices")` / `ShowResponseWindow(StoreResponse)`：这是**物品选择窗口**（把 choices 里的 GameItem 图标放进 responseInventory，点击执行 Action，并 `Interactable.isPaused=true`、`TooltipContainer.isPaused=true`），不是文本提示框，不适合「柜台已满」。
- 没有找到通用的 toast / 漂浮通知 public API（全程序集无 `Toast`/`Notification` 类型；`NightLog.AddNightLog` 只写夜间日志，不是即时弹窗）。

### 8.6 自建窗口：`CustomUIManager` / `CustomUIBuilder` / `CustomUIElement`

**单例与层**（`il/CustomUIManager__Awake.il.txt` 等）：

- `public static CustomUIManager Instance { public get; private set; }`；`Awake` 里 `DontDestroyOnLoad(gameObject)` → **跨场景常驻**。
- 层常量（public const string）：`LAYER_FRONT="front"`、`LAYER_PAPER="paper"`、`LAYER_REAR="rear"`、`LAYER_META="meta"`、`LAYER_MENU="menu"`、`LAYER_OVERLAY="overlay"`。
- `ResolveAnchor(layerId)`：`overlay`/空 → null（用 OverlayRoot）；否则查静态 `anchors` 字典，缺失/失效 → `Debug.LogWarning("[CustomUI] No canvas anchor for layer '<id>' in the current scene, using the overlay layer")` 并回落到 overlay（**不抛异常**）。
- `GetAvailableLayers()` 返回 `["overlay"] + 当前已注册 anchor 的层`；`HasLayer(layerId)` 同判据；`IsMetaLayer(layerId)` = `meta` 或 `menu`。
- `LAYER_OVERLAY` 由管理器自己的 Canvas 承载，**任何场景都可用**；`paper/front/rear/meta/menu` 需要当前场景存在 `CustomUICanvasAnchor`（店铺场景存在 paper/front/rear 等；主菜单有 menu）。场景层窗口随场景销毁（官方指南 `ModdingGuide.zh-CN.md:738`；管理器在 `Awake` 注册 `SceneManager.sceneUnloaded`）。
- `CreateWindow(id, title=null, layerId="overlay", windowPrefabOverride=null, Style style=0)`：
  - 同 id 已存在 → 先 `Close()` 再新建（**重复 `Window()` 是安全的，会替换**）；
  - `Style.Auto(0)`：`useMetaPrefabs = IsMetaLayer(layer)`（meta/menu → 深色 meta 元件，否则游戏内元件）；`Style.Meta(2)` 强制 meta；`Style.Machinery(3)` 用 `CustomUIMachineryStyle.CreateFrame`（机器内容窗外观）；`Style.Game(1)` 游戏内元件。
  - `windowPrefab` 为空会 LogError 并用临时 RectTransform 兜底；frame 缺 `CustomUIWindowFrame` 也会 LogError。
  - `GetPrefab(kind, meta)`：meta=true 且对应 meta prefab 存在 → meta 预制体，否则回落到普通预制体。
- `Window(...)` 是 `CreateWindow` 的静态包装；**`Instance == null` 时 LogError 并返回 null**（链式会 NRE，调用前判 `Instance != null`）。

**`CustomUIBuilder` 链式 API**（均 public，返回 `CustomUIBuilder`，最后 `Show()` 返回 `CustomUIWindow`）：

`SetSize(w,h)`、`SetPosition(Vector2)`、`Center()`、`SetDraggable(bool)`、`SetCloseOnEscape(bool)`、`OnClose(Action)`、`UseMetaPrefabs(bool=true)`、`UseMachineryStyle(bool=true)`、
`AddLabel(text,id=null)`、`AddHeader`、`AddWarning`、`AddSeparator`、`AddButton(text,onClick,id=null)`、`AddCloseButton(Action onClick,id=null)`（**只绑 onClick，不自动关闭**）、`AddIconButton`、`AddImage(Sprite|string, w=-1,h=-1,id)`、`AddToggle`、`AddSlider`、`AddInput(placeholder, initial="", Action<string> onChanged=null, id=null, Action<string> onTyping=null)`、`AddDropdown`、`AddProgressBar`、`AddSpace`、`AddFlexibleSpace`、`WithTooltip`、`BeginRow/Column/Panel/Grid/Scroll/BeginTabs` 等。

`AddInput`：`onChanged` 在**回车/失焦**时触发，`onTyping` 逐字触发；`SetString(initial, false)` 初始化；`CustomUIElement.GetString()` 返回输入框文本（输入框缺失返回 `""`）。

**`CustomUIElement` 链式 setter 清单**（全部 public，返回 `this`；用于窗口内状态行实时刷新）：

`SetVisible(bool)`（直接 `root.SetActive`，空安全）、`SetInteractable(bool)`、`SetText(string)`（`text` 缺失 → `Warn` 后 no-op）、`SetTextColor(Color)`、`SetSprite(Sprite)`、`SetSprite(string spriteKey)`、`SetImageColor(Color)`、`OnClick(Action)`、`SetBool(bool,bool notify=false)` / `OnToggle(Action<bool>)`、`SetFloat(float,bool notify=false)` / `OnSlider(Action<float>)`、`SetString(string,bool notify=false)` / `OnInput(Action<string>)` / `OnInputChanged(Action<string>)`、`SetIndex(int,bool notify=false)` / `SetOptions(IList<string>)` / `OnDropdown(Action<int>)`、`SetProgress(float, string labelText=null)`、`SetTooltip(string)`、`SetPreferredSize`、`SetFixedWidth`、`SetFlexible`；读取：`GetText/GetBool/GetFloat/GetString/GetIndex`。
`CustomUIManager.GetElement(windowId, elementId)`：`GetWindow(windowId)` 为 null → 返回 null；窗口内元素缺失 → `Debug.LogWarning("[CustomUI] Window '<id>' has no element '<id>'")` → null（**空安全，可 `?.SetText(...)`**）。`CustomUIManager.IsOpen(id)` / `CloseWindow(id)` / `GetWindow(id)` 均可用；`CloseWindow` 找不到时静默返回。

**「最接近原版窗口外观」的推荐（有原版自身实现作依据）**：

| 方案 | 外观 | 原版证据 |
|---|---|---|
| `Style.Auto` + `LAYER_REAR`（默认 game 预制体） | **游戏内窗口外观**（与 F9 面板、原版 Item Spawner 完全同款） | `CustomUIDebugPanel.Open`：`CreateWindow(id,title,"rear",null,Style.Auto).SetSize(...).SetDraggable(true).SetCloseOnEscape(true).Center()`；`CustomUIItemSpawner.Open`：`CreateWindow("debug_item_spawner","Item Spawner","rear",null,Style.Auto).SetSize(680,620).SetDraggable(true).SetCloseOnEscape(true).OnClose(FlushSave)` |
| `.UseMetaPrefabs(true)`（或 Style.Meta / meta/menu 层） | 深色「选项/Mods 菜单」外观 | `ModConfigWindow.Open`：层 `meta`/`menu` + `optionUIPrefab` + meta 元件 |
| `.UseMachineryStyle()`（或 Style.Machinery） | 机器内容窗（金属框）外观 | `CustomUIMachineryStyle.CreateFrame` |

→ 推荐：**满仓提示**用 `GenericConfirmationUIManager.OpenPanelSingleButton`（真正的原版弹窗 + 暗化遮罩）；
**按 ID 生成窗口**用 `CustomUIManager.Window("free_chem.debug_spawn", title, CustomUIManager.LAYER_REAR)` + `SetSize/Center/SetCloseOnEscape`（与 F9 调试面板同风格，`UseMetaPrefabs` 不开启）。
若担心场景层在切场景时被销毁，可改用 `LAYER_OVERLAY`（恒可用，但外观相同、跨场景常驻——生成逻辑本身有店铺场景 guard，窗口残留也无害，可在 `OnGameLoaded`/离开店铺时 `CloseWindow`）。

### 8.7 模组设置：`ModConfig.Define(bool)` / `ConfigEntry<bool>` / 设置面板入口

签名（`il/ModConfig__Define.il.txt`、`il/ModConfig__Register.il.txt`）：

```csharp
// ModHelper.GetConfig(modId) => ModConfig.For(modId)
public ConfigEntry<bool> Define(string key, bool defaultValue, string section,
    Func<string> label, Func<string> tooltip = null, bool requiresRestart = false);
// 其它重载：int/float（可带 min/max → 滑块）、string（可带 choices → 下拉）、DefineEnum<TEnum>
public ConfigEntry<string> Get(string key);           // 按 key 取
public IReadOnlyList<ConfigEntry> Entries { get; }    // 全部
public bool HasEntries { get; }
public void Save(); public void ResetToDefaults();
```

`ConfigEntry<T>`（public）：`Value { get; }`、`Default { get; }`、`event Action<T> OnChanged;`、`void Set(T newValue)`、`void Reset()`、`Type ValueType`、`BoxedValue/BoxedDefault`。
`ConfigEntry<T>.Set`：`ApplyValue`（先 `Sanitize` 截断到范围内，再与旧值 `Equals` 比较；**相同则不触发 OnChanged 返回 false**）→ 变化才触发 `OnChanged`，然后 `Config.Save()` 落盘。`ApplyValue` 对 OnChanged 异常也会 catch 并 `LogError("[ModConfig] OnChanged of '<key>' threw: ...")`。

`Register` 行为：key 为空 → `LogError` 忽略；**section 为空 → `"General"`**；label 为 null → 用默认（key）；**重复 key → Warning + 后定义覆盖前定义**；若配置文件里已有该 key → `TryDeserialize` 恢复，解析失败 → Warning + 用默认。加载时**不**触发 OnChanged。

设置面板入口：**Mods 菜单里该模组条目的设置（齿轮）按钮** → `ModUIManager.OpenSelectedModSettings` → `ModConfigWindow.Open(LoadedMod)`（xref 唯一调用方）：
窗口 id `"mod_config:<modId>"`，层 `meta`/`menu`（`StoreUIManager.Instance != null ? "meta" : "menu"`，两者都是 meta 风格），使用 `optionUIPrefab`，包含「恢复默认」「应用」按钮与重启提示（`ui_mods_restart_note`）。**没有游戏内入口**（官方指南 `ModdingGuide.zh-CN.md:1298`）。
配置文件：`<persistentDataPath>/ModConfigs/<modId>.json`（跨存档共享，可按语言/玩家编辑；缺键取默认、越界截断、坏值回退并警告）。

**section 字符串不需要也不支持本地化**：`section` 是普通 `string`，`ModConfigWindow.Open` 直接 `AddHeader(section, "section_"+section)` 原样显示；只有 `label`/`tooltip` 是 `Func<string>`，每次开窗求值 → 可 `ModHelper.GetLocalized(...)`。因此：section 建议用语言中性短词（如 `"General"` / `"Debug"`）或英文；**不要**在 Define 时传 `GetLocalized(...)`（Init 期语言未必就绪，且切语言不会刷新 section）。

对 features 的 `debug_mode`：`ConfigEntry<bool>` 字段 + `config.Define("debug_mode", false, "Debug", () => ModHelper.GetLocalized(ModId,"cfg_debug_mode"), tooltip: () => ModHelper.GetLocalized(ModId,"cfg_debug_mode_tip"))`；`OnChanged += v => ChemRuntime.DebugMode = v;`；`Init` 末尾 `ChemRuntime.DebugMode = debugMode.Value;`，`OnGameLoaded` 再同步一次（配置是文件级、跨存档，ModData 才是按局）。

### 8.8 推荐代码片段（features / task-14，带可见性前提）

可见性前提：`EmporiumEntry`/`GraphUtils`/`CustomUIManager`/`GenericConfirmationUIManager`/`ModConfig` 都是 public 类型，所用成员均 public（§8.1–§8.7）。

```csharp
// ① 模组设置 + 调试开关（Init / OnEnable）
ConfigEntry<bool> debugMode;
debugMode = ModHelper.GetConfig(ChemCatalog.ModId)
    .Define("debug_mode", false, "Debug",
        () => ModHelper.GetLocalized(ChemCatalog.ModId, "cfg_debug_mode"),
        tooltip: () => ModHelper.GetLocalized(ChemCatalog.ModId, "cfg_debug_mode_tip"));
debugMode.OnChanged += v => ChemRuntime.DebugMode = v;
ChemRuntime.DebugMode = debugMode.Value;         // Init 末尾；OnGameLoaded 再同步一次（配置跨存档）

// ② F9 入口（OnEnable 注册；注意 F9 面板本身需要 dev.txt，见 §8.4）
ModHelper.AddDebugFunction(ChemCatalog.ModId, ModHelper.DEBUG_CATEGORY_COMMAND,
    "FreeChem 按ID生成", OpenSpawnWindow, tall: true);
// OnDisable: ModHelper.RemoveDebugFunctions(ChemCatalog.ModId);

// ③ 生成：CanAccept 只读探测 → 满仓销毁 + 原版弹窗；成功用 TryAcceptAllOrDestroy
bool TrySpawnByNumericId(int numId, out string status)
{
    status = "";
    if (!ChemRuntime.DebugMode) { status = ModHelper.GetLocalized(ChemCatalog.ModId, "debug_status_disabled"); return false; }
    if (!ChemItemIds.TryGetId(numId, out string id)) { status = ModHelper.GetLocalized(ChemCatalog.ModId, "err_invalid_id"); return false; }
    if (EmporiumEntry.Instance == null) { status = ModHelper.GetLocalized(ChemCatalog.ModId, "err_no_store"); return false; }
    GameGridInventory back = EmporiumEntry.Instance.backInvinvElement;   // public get
    if (back == null) { status = ModHelper.GetLocalized(ChemCatalog.ModId, "err_no_store"); return false; }

    GameItem it = ModHelper.SpawnItem(id);      // 未知 id 会 LogWarning + dummy，不会 null；表已前置校验
    if (it == null) { status = ModHelper.GetLocalized(ChemCatalog.ModId, "err_not_found"); return false; }

    if (!GraphUtils.CanAccept(back, it))       // 只读探测：不改任何状态
    {
        it.Destroy();                          // SpawnItem 出来的物品不在任何容器，必须自行销毁
        ShowVanillaAlert(ModHelper.GetLocalized(ChemCatalog.ModId, "debug_alert_title"),
                         ModHelper.GetLocalized(ChemCatalog.ModId, "debug_alert_full"));
        status = ModHelper.GetLocalized(ChemCatalog.ModId, "debug_status_bad");
        return false;
    }
    // 0 = 全部放入；OrDestroy 在没放下时自动 Destroy + ReportUnplaced 并返回 false
    if (!GraphUtils.TryAcceptAllOrDestroy(back, it, "free_chem.debug_spawn"))
    {
        ShowVanillaAlert(ModHelper.GetLocalized(ChemCatalog.ModId, "debug_alert_title"),
                         ModHelper.GetLocalized(ChemCatalog.ModId, "debug_alert_full"));
        status = ModHelper.GetLocalized(ChemCatalog.ModId, "debug_status_bad");
        return false;
    }
    status = string.Format(ModHelper.GetLocalized(ChemCatalog.ModId, "debug_status_ok"), it.GetDisplayName(true), numId);
    return true;
}

// ④ 原版风格弹窗（GenericConfirmationUIManager：真正的原版 modal + 暗化遮罩）
static void ShowVanillaAlert(string title, string body)
{
    if (GenericConfirmationUIManager.Instance == null) return;
    GenericConfirmationUIManager.Instance.OpenPanelSingleButton(
        body, ModHelper.GetLocalized(ChemCatalog.ModId, "debug_alert_ok"), null);
}

// ⑤ 生成窗口（与 F9 面板 / 原版 Item Spawner 同风格：layer "rear" + 默认 game 预制体）
const string SpawnWindowId = "free_chem.debug_spawn";
string input = "";
void OpenSpawnWindow()
{
    if (CustomUIManager.Instance == null) return;
    CustomUIManager.Window(SpawnWindowId, ModHelper.GetLocalized(ChemCatalog.ModId, "debug_title"),
                           CustomUIManager.LAYER_REAR)          // 或 LAYER_OVERLAY
        .SetSize(520, 320).SetDraggable(true).SetCloseOnEscape()
        .AddLabel(ModHelper.GetLocalized(ChemCatalog.ModId, "debug_hint"), "hint")
        .AddLabel(string.Format(ModHelper.GetLocalized(ChemCatalog.ModId, "debug_range"), 1, ChemItemIds.MaxId), "range")
        .AddInput(ModHelper.GetLocalized(ChemCatalog.ModId, "debug_placeholder"), "", v => input = v, "input")
        .AddLabel("", "status")
        .AddButton(ModHelper.GetLocalized(ChemCatalog.ModId, "debug_spawn"), OnSpawnClicked, "spawn")
        .AddButton(ModHelper.GetLocalized(ChemCatalog.ModId, "debug_list"), LogIdTable, "list")
        .AddCloseButton(() => CustomUIManager.Instance.CloseWindow(SpawnWindowId), "close")  // 不自动关，需自己 Close
        .Center()
        .Show();
}
void OnSpawnClicked()
{
    ChemItemIds.TryParseCommand(input, out int n, out _);
    bool ok = TrySpawnByNumericId(n, out string status);
    CustomUIManager.Instance?.GetElement(SpawnWindowId, "status")?.SetText(status);   // 空安全
}
```

### 8.9 不可行 / 不适用清单（附证据）

1. **`ModHelper.MoveItemToBackCounter` 不满足「满仓提示」需求**：IL 丢弃 `TryAcceptAll` 返回值、无 null guard、满仓不销毁（§8.1）→ 必须自己写探测/销毁/弹窗。
2. **`ConfirmPanelUIManager` 不适合店内**：无静态 Instance，原版调用方全在主菜单（§8.5）。
3. **`EmporiumEntry.ResponseWindow` 不是文本提示**：它是物品选择窗口（§8.5）。
4. **F9 面板在无 `dev.txt` 的安装不可用**：本机安装目录无 `dev.txt`/`1dev.txt`；需手动放空文件（仅开发机），否则「F9 Command 入口」不会出现（§8.4）。
5. **`SpawnItem` 不能靠 null 判 id 合法性**：未知 id 返回 dummy 空壳（§8.3）。
6. **`CustomUIBuilder.AddCloseButton` 不自动关闭窗口**：只绑定 `onClick`（IL 仅 `Spawn(CloseButton)` + `OnClick`），需在回调里 `CloseWindow(id)`/`window.Close()`。
7. 没有通用 toast/notification public API（§8.5）。

### 8.10 未验证项（如实声明）

- F9 面板在本机（无 dev.txt）的实际行为只由 IL/官方文档推断，未启动游戏验证；放 `dev.txt` 后的实际呈现也未实机确认。
- 自建窗口在店铺场景的实际外观/层级、`"rear"` 层在店铺场景是否一定有 anchor（原版 F9/Spawner 同层，静态推断为有），未实机确认。
- `GenericConfirmationUIManager` 在店铺场景运行时是否为非 null、以及它与自建窗口叠加时的显示顺序，未实机确认。
- 后柜台容量（私有常量 18×4）与「满仓」判定的实际体验（哪些能堆叠/多单位物品）未实机验证。
- `GraphUtils.CanAccept` 与 `TryAcceptAll` 的“单槽探测 vs 实际放置”在多单位/可堆叠物品上的差异未实测（本模组机器物品均为单件，风险低）。
- 模组配置面板在游戏内的实际打开路径（Mods 菜单齿轮）未实机点开验证（静态证据为 `ModUIManager.OpenSelectedModSettings` → `ModConfigWindow.Open`）。

---

## 9. 探针工程结构

```
tools/api-probe/
├── api-probe.csproj      net8.0 console，无 NuGet 包依赖（System.Reflection.Metadata 在共享框架内）
├── NuGet.config          <clear/>：离线构建，避免 nuget.org 网络
├── Program.cs            只读元数据/IL 探针（约 1,000 行，无第三方依赖）
├── README.md             探针说明（参数、输出文件、限制、扩展点）
├── run.ps1               可复现的调用脚本（含全部 IL selector）
└── out/                  运行输出（assembly/types/members/focus-*/strings/xref/il-index/il）
```

探针能力：程序集标识与自定义特性；全部类型/方法/字段/属性签名 + 可见性 + const 值 + 参数默认值；
按名字聚焦 heat/sprite/ModData/ModLoader 成员；全量 `ldstr` 字符串表；方法 IL 反汇编（opcode + token 解析为可读签名）；
成员交叉引用表（`xref.txt`：谁调用/读取了某成员）。

**限制**：只读元数据与 IL，不做类型加载/依赖解析/执行；泛型实例化与 `ldtoken` 在 IL 中显示为 `<typespec>`/token 形式；
不还原局部变量类型表；不代表运行时行为（Unity 生命周期、协程时序、UI 刷新时机需实机验证）。

---

## 10. 复现命令

从项目根 `ps-chem-mod\` 执行（本机 PowerShell 执行策略禁止直接 `.\run.ps1`；本机 shell 是 Windows PowerShell 5.1，
`$PSHOME` 下只有 `powershell.exe`，没有 `pwsh.exe`）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\api-probe\run.ps1
```

（若在 PowerShell 7 环境：`& "$PSHOME\pwsh.exe" -NoProfile -ExecutionPolicy Bypass -File .\tools\api-probe\run.ps1`。）

本次规范运行输出：约 410 个方法的 IL + 全部元数据 dump（含第 1–14 轮全部 selectors）；跑一遍约 2–3 秒。

等价的手工命令（自定义输出目录 / 追加 selector 时）：

```powershell
.\.dotnet\dotnet.exe run --project tools\api-probe\api-probe.csproj -c Release -- `
  'D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed\Assembly-CSharp.dll' `
  tools\api-probe\out `
  '^GameItem$::SetSpriteAndShapeFromMod$' '^SecData$::CommitCrime$' '^ModHelper$::SetModDataInt$'
```

selector 语法：`<TypeRegex>::<MethodRegex>`（正则，忽略大小写；只写方法名则匹配所有类型）。
输出全部落在 `tools/api-probe/out/`；`il-index.txt` 列出每个被反汇编方法与对应文件。

本次核对的关键 IL 文件（可直接打开对照本文）：

- `out/il/GameItem__SetSpriteAndShapeFromMod.il.txt`（§1.3）
- `out/il/GameItemElement__ResolveSpriteByName.il.txt`、`GameItemElement___ResolveSpriteByName_b__81_0.il.txt`（§1.4 解析路径）
- `out/il/GameItemElement__ValidateSelectLocked.il.txt`（§1.4 刷新消费点）
- `out/il/GameItem__SetSprite.il.txt`、`GameItem__SetSpriteAndShape.il.txt`、`GameItem__BuildShapeFromSprite.il.txt`、`GameItem__SetShape.il.txt`
- `out/il/SecData__CommitCrime.il.txt`、`PlayerStore__CheckCrime.il.txt`、`PlayerStore__get_Instance.il.txt`、`PlayerStore__get_HasInstance.il.txt`
- `out/il/ModHelper__SetModDataInt.il.txt`、`GetModDataInt.il.txt`、`ModDataKey.il.txt`、`ModDataStore.il.txt`
- `out/il/ModHelper__AddNetworkUpgrade.il.txt`、`SetNetworkTabLabel.il.txt`
- `out/il/PlayerStore__.cctor.il.txt`（`VERSION 100-Rev13`）、`ModUIManager__VersionMatchesGame.il.txt`、`ModUIManager__NormalizeVersion.il.txt`
- `out/il/SaveManager__DecodeNodesInternal.il.txt`（§1.6 第 4 条）
