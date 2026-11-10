# tools/api-probe —— 只读程序集元数据/IL 探针

用途：在调用任何未文档化 API 之前，**静态核对** `Assembly-CSharp.dll` 的类型/成员/签名/可见性/常量值与方法 IL。
不加载、不执行游戏程序集；用 `System.Reflection.Metadata` + `PEReader` 直接读元数据表与 IL 字节。

- 目标框架：`net8.0`，零 NuGet 依赖（`System.Reflection.Metadata` 在 .NET 8 共享框架内），
  `NuGet.config` 清空包源以便离线构建。
- 运行工具：项目根 `.dotnet\dotnet.exe`（8.0.427）。
- 结论文档：`docs/api-findings.md`。

## 运行

从项目根 `ps-chem-mod\`（默认读取已安装游戏程序集，输出到 `tools\api-probe\out\`）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\api-probe\run.ps1
```

本机执行策略禁止直接 `.\run.ps1`；本机 shell 为 Windows PowerShell 5.1（`$PSHOME` 下无 `pwsh.exe`）。

自定义程序集 / 输出目录 / 追加 IL selector：

```powershell
.\.dotnet\dotnet.exe run --project tools\api-probe\api-probe.csproj -c Release -- `
  'D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed\Assembly-CSharp.dll' `
  tools\api-probe\out `
  '^GameItem$::SetSpriteAndShapeFromMod$' '^SecData$::CommitCrime$'
```

## 参数

1. `assembly.dll`（省略则用默认游戏程序集路径）
2. `outDir`（省略则 `tools\api-probe\out`）
3. 之后的每个参数都是 IL selector：`<TypeRegex>::<MethodRegex>`（正则，忽略大小写）。
   只写方法名（不含 `::`）等同于类型任意、按方法名匹配；一个方法命中一个 IL 文件。

## 输出（`out/`）

| 文件 | 内容 |
|---|---|
| `assembly.txt` | 程序集标识、自定义特性（含 `AssemblyInformationalVersion` 是否存在）、PE 版本、`InternalsVisibleTo` |
| `types.txt` | 每个类型：全名、可见性/种类、基类、接口、static/obsolete 标记 |
| `members.txt` | 每个类型的字段/属性/事件/方法完整签名 + 可见性 + `const` 值 + **参数默认值** |
| `focus-gameitem.txt` | `GameItem` 全部成员（含 private 字段，如 sprite 相关 backing field） |
| `focus-heat.txt` | 名字含 heat/suspicion/crime/investigation/security/penalty/contraband 的成员及归属类型 |
| `focus-modhelper.txt` / `focus-moddata.txt` | `ModHelper` 全部成员 / 所有含 `ModData` 的成员 |
| `focus-modloader.txt` | 名字含 LoadedMod/ModManifest/ModLoader/ModDirectory/... 的类型与成员 |
| `focus-sprite-any.txt` | 全程序集名字含 sprite/icon 的成员（用于区分「GameItem 有」和「别的类型才有」） |
| `strings.txt` | 全程序集 IL 里所有 `ldstr` 用户字符串（含出处方法），用于查版本号/文案/格式串 |
| `xref.txt` | 成员交叉引用：`被引用成员  <=  调用方类型::方法`（含 Application.version、CommitCrime、spriteChanged 等关键成员） |
| `il-index.txt` | 每个 selector 命中详情与对应 IL 文件路径 |
| `il/*.il.txt` | 方法 IL 反汇编：偏移、opcode、操作数（字符串带引号；token 解析成可读类型/方法/字段签名；分支目标） |

## 实现要点（便于扩展）

- `TypeProvider` 实现 `ISignatureTypeProvider<string, object?>` + `ICustomAttributeTypeProvider<string>`，把类型签名渲染成 C# 风格字符串。
- 字段/参数的编译期常量从 Constant 表解码（`Constants`），因此 `const string/int/float` 与枚举值都能直接看到。
- IL 解码用 `System.Reflection.Emit.OpCodes` 反射建 opcode 表；操作数按 `OperandType` 读取，token 通过 `MetadataReader` 解析。
- 全量 IL 扫描（strings + xref）覆盖 25,202 个方法，一次运行约 2–3 秒。

## 限制

只读静态核对，不代表运行时行为。泛型实例化、`ldtoken` 等显示为 `<typespec>`/token 形式；不还原局部变量类型表；
Unity 生命周期、协程时序、UI 刷新时机、存档重载等需实机验证。
