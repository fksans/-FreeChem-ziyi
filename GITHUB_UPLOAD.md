# FreeChem GitHub 源码包（v0.9.6）

这是可直接提交到 GitHub 的源码与资源包。**不包含游戏程序集、反编译 IL 输出、构建产物或本机路径依赖**。

## 包含内容
- src/：C# 源码（FreeChem.csproj + 全部 .cs）
- FreeChem/：模组资源（manifest、Books、Localization、LootTables、Textures）；**不含 FreeChem.dll**（构建产物，请自行编译）
- docs/：设计说明、进度报告、化学/代码审查、API 研究记录
- 	ools/：图标/本地化生成器、静态验收、逻辑自检、C# 数据层 harness、只读 API 探针源码
- README.md、HANDOFF.md、预览.html、icon_sheet.png、.gitignore

## 不包含（保持仓库干净/合规）
- .dotnet/（本地 .NET SDK，约 700MB）
- FreeChem_v1_backup/、ef-icons/、ef-modding/（备份与第三方参考资料）
- 	ools/api-probe/out*/（从游戏程序集导出的 IL/成员数据）
- src/bin、src/obj、	ools/**/bin|obj、__pycache__、临时 txt/日志
- FreeChem/FreeChem.dll（发布时由 CI 或本地构建生成）

## 构建
需要 .NET SDK 8（netstandard2.1 目标）和正版游戏 Assembly-CSharp.dll 等引用程序集。

`powershell
cd src
dotnet build FreeChem.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Probably Stolen\\"
`

构建后把 src/bin/Release/netstandard2.1/FreeChem.dll 复制到 FreeChem/FreeChem.dll，整个 FreeChem/ 文件夹放入游戏的 Mods/FreeChem/。

## 上传 GitHub
`powershell
git init
git add .
git commit -m "FreeChem v0.9.6"
git branch -M main
git remote add origin https://github.com/<你的账号>/<仓库名>.git
git push -u origin main
`

## 注意
- 仓库未附带许可证（如需开源请自行添加 LICENSE）。
- docs/ 为开发过程记录，可能含本地环境说明；上传前可按需删减。
- 游戏本体代码/资源不在此包内；	ools/api-probe 仅含探针源码，输出目录已忽略。
