# FreeChem 化学反应 / 流程 / 器具逻辑审计（P3f）

> 本文件由 features 在 task-51 审计后生成；数据来自 `src/ChemRecipes.cs`（夜间反应）、`src/ChemLab.cs`（实时热反应）、
> `src/ChemCatalog.cs`（注册台账）、`src/ChemMachines.cs`（设备/槽位/相态）、`FreeChem/LootTables/free_chem_drops.json`。
> 审计结论为**静态/逻辑级**：build 0 警告 0 错误、logic-harness 全通过；所有游戏内交互仍标注「未实机验证」。

## 1. 总览

- 夜间反应 **44 条**：T1 7 / T2 15 / T3 18 / T4 4。
- 实时热反应 **6 条**（加热台，每秒 tick）。
- 设备 **13 台**：`chem_reactor`、`electrolyzer`、`gas_generator`、`condenser_unit`、`crystallizer`、`high_pressure_synth`、`fractionator`、`titration_bench`、`canister_filling`、`decanter`、`gas_scrubber`、`filtration_bench`、`heating_platform`；其中 `filtration_bench` 无固定配方（滤纸+液体动态过滤），`heating_platform` 为实时加热台。
- 数字 ID 表 1–97（#97 为化学品商人便条，非化学品）。
- 配平审计：可严格计量的方程全部配平；20 条为**物理过程或虚构设计**（明确标注，见各表「配平」列）。

## 2. 夜间反应逐条审计（44 条）

| # | 反应 id | Tier | 设备 | 条件 | 投料 → 产物 | 配平 | 可达性 |
|---|---|---|---|---|---|---|---|
| 1 | `chloralkali` | T1 | `electrolyzer` | temp档2 | l<br>i<br>q<br>_<br>b<br>r<br>i<br>n<br>e<br>（<br>N<br>a<br>C<br>l<br>(<br>a<br>q<br>)<br>） → g<br>a<br>s<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>n<br>e<br>（<br>C<br>l<br>2<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>（<br>H<br>2<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>s<br>o<br>d<br>i<br>u<br>m<br>_<br>h<br>y<br>d<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>N<br>a<br>O<br>H<br>(<br>a<br>q<br>)<br>） | 已配平 | 是 |
| 2 | `hcl_electrolysis` | T1 | `electrolyzer` | temp档2 | l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>c<br>h<br>l<br>o<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>C<br>l<br>(<br>a<br>q<br>)<br>） → g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>（<br>H<br>2<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>n<br>e<br>（<br>C<br>l<br>2<br>） | 已配平 | 是 |
| 3 | `co2_from_marble` | T1 | `gas_generator` | 默认 | s<br>o<br>l<br>_<br>c<br>a<br>l<br>c<br>i<br>u<br>m<br>_<br>c<br>a<br>r<br>b<br>o<br>n<br>a<br>t<br>e<br>（<br>C<br>a<br>C<br>O<br>3<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>c<br>h<br>l<br>o<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>C<br>l<br>(<br>a<br>q<br>)<br>） → g<br>a<br>s<br>_<br>c<br>a<br>r<br>b<br>o<br>n<br>_<br>d<br>i<br>o<br>x<br>i<br>d<br>e<br>（<br>C<br>O<br>2<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>c<br>a<br>l<br>c<br>i<br>u<br>m<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>C<br>a<br>C<br>l<br>2<br>(<br>a<br>q<br>)<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 已配平 | 是 |
| 4 | `hcl_from_salt` | T2 | `gas_generator` | temp档2 | s<br>o<br>l<br>_<br>s<br>o<br>d<br>i<br>u<br>m<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>N<br>a<br>C<br>l<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>s<br>u<br>l<br>f<br>u<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>2<br>S<br>O<br>4<br>） → g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>H<br>C<br>l<br>(<br>g<br>)<br>） | 已配平 | 是 |
| 5 | `peroxide_decomp` | T1 | `gas_generator` | 默认 | l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>p<br>e<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>H<br>2<br>O<br>2<br>） → g<br>a<br>s<br>_<br>o<br>x<br>y<br>g<br>e<br>n<br>（<br>O<br>2<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 已配平 | 是 |
| 6 | `kmno4_decomp` | T1 | `gas_generator` | temp档3 | s<br>o<br>l<br>_<br>p<br>o<br>t<br>a<br>s<br>s<br>i<br>u<br>m<br>_<br>p<br>e<br>r<br>m<br>a<br>n<br>g<br>a<br>n<br>a<br>t<br>e<br>（<br>K<br>M<br>n<br>O<br>4<br>） → g<br>a<br>s<br>_<br>o<br>x<br>y<br>g<br>e<br>n<br>（<br>O<br>2<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>m<br>a<br>n<br>g<br>a<br>n<br>e<br>s<br>e<br>_<br>d<br>i<br>o<br>x<br>i<br>d<br>e<br>（<br>M<br>n<br>O<br>2<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>p<br>o<br>t<br>a<br>s<br>s<br>i<br>u<br>m<br>_<br>m<br>a<br>n<br>g<br>a<br>n<br>a<br>t<br>e<br>（<br>K<br>2<br>M<br>n<br>O<br>4<br>） | 已配平 | 是 |
| 7 | `ammonia_liberation` | T2 | `gas_generator` | temp档2 | s<br>o<br>l<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>u<br>m<br>_<br>n<br>i<br>t<br>r<br>a<br>t<br>e<br>（<br>N<br>H<br>4<br>N<br>O<br>3<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>s<br>o<br>d<br>i<br>u<br>m<br>_<br>h<br>y<br>d<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>N<br>a<br>O<br>H<br>） → g<br>a<br>s<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>（<br>N<br>H<br>3<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>s<br>o<br>d<br>i<br>u<br>m<br>_<br>n<br>i<br>t<br>r<br>a<br>t<br>e<br>（<br>N<br>a<br>N<br>O<br>3<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 已配平 | 是 |
| 8 | `lime_calcination` | T1 | `gas_generator` | temp档3 | s<br>o<br>l<br>_<br>c<br>a<br>l<br>c<br>i<br>u<br>m<br>_<br>c<br>a<br>r<br>b<br>o<br>n<br>a<br>t<br>e<br>（<br>C<br>a<br>C<br>O<br>3<br>） → s<br>o<br>l<br>_<br>c<br>a<br>l<br>c<br>i<br>u<br>m<br>_<br>o<br>x<br>i<br>d<br>e<br>（<br>C<br>a<br>O<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>c<br>a<br>r<br>b<br>o<br>n<br>_<br>d<br>i<br>o<br>x<br>i<br>d<br>e<br>（<br>C<br>O<br>2<br>） | 已配平 | 是 |
| 9 | `hcl_synthesis` | T2 | `chem_reactor` | temp档3 | g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>（<br>H<br>2<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>n<br>e<br>（<br>C<br>l<br>2<br>） → g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>H<br>C<br>l<br>(<br>g<br>)<br>） | 已配平 | 是 |
| 10 | `wet_sulfuric` | T2 | `chem_reactor` | temp档2、催化剂 | s<br>o<br>l<br>_<br>s<br>u<br>l<br>f<br>u<br>r<br>（<br>S<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>o<br>x<br>y<br>g<br>e<br>n<br>（<br>O<br>2<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>p<br>e<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>H<br>2<br>O<br>2<br>） → l<br>i<br>q<br>_<br>s<br>u<br>l<br>f<br>u<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>2<br>S<br>O<br>4<br>） | 已配平 | 是 |
| 11 | `hypochlorite` | T2 | `chem_reactor` | temp档0 | g<br>a<br>s<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>n<br>e<br>（<br>C<br>l<br>2<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>c<br>a<br>l<br>c<br>i<br>u<br>m<br>_<br>h<br>y<br>d<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>C<br>a<br>(<br>O<br>H<br>)<br>2<br>） → s<br>o<br>l<br>_<br>b<br>l<br>e<br>a<br>c<br>h<br>i<br>n<br>g<br>_<br>p<br>o<br>w<br>d<br>e<br>r<br>（<br>C<br>a<br>(<br>C<br>l<br>O<br>)<br>2<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>c<br>a<br>l<br>c<br>i<br>u<br>m<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>C<br>a<br>C<br>l<br>2<br>(<br>a<br>q<br>)<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 已配平 | 是 |
| 12 | `slaked_lime` | T1 | `chem_reactor` | 默认 | s<br>o<br>l<br>_<br>c<br>a<br>l<br>c<br>i<br>u<br>m<br>_<br>o<br>x<br>i<br>d<br>e<br>（<br>C<br>a<br>O<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） → s<br>o<br>l<br>_<br>c<br>a<br>l<br>c<br>i<br>u<br>m<br>_<br>h<br>y<br>d<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>C<br>a<br>(<br>O<br>H<br>)<br>2<br>） | 已配平 | 是 |
| 13 | `soda_from_caustic` | T2 | `chem_reactor` | 默认 | l<br>i<br>q<br>_<br>s<br>o<br>d<br>i<br>u<br>m<br>_<br>h<br>y<br>d<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>N<br>a<br>O<br>H<br>(<br>a<br>q<br>)<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>c<br>a<br>r<br>b<br>o<br>n<br>_<br>d<br>i<br>o<br>x<br>i<br>d<br>e<br>（<br>C<br>O<br>2<br>） → s<br>o<br>l<br>_<br>s<br>o<br>d<br>i<br>u<br>m<br>_<br>c<br>a<br>r<br>b<br>o<br>n<br>a<br>t<br>e<br>（<br>N<br>a<br>2<br>C<br>O<br>3<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 已配平 | 是 |
| 14 | `baking_soda` | T2 | `chem_reactor` | 默认 | s<br>o<br>l<br>_<br>s<br>o<br>d<br>i<br>u<br>m<br>_<br>c<br>a<br>r<br>b<br>o<br>n<br>a<br>t<br>e<br>（<br>N<br>a<br>2<br>C<br>O<br>3<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>c<br>a<br>r<br>b<br>o<br>n<br>_<br>d<br>i<br>o<br>x<br>i<br>d<br>e<br>（<br>C<br>O<br>2<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） → s<br>o<br>l<br>_<br>s<br>o<br>d<br>i<br>u<br>m<br>_<br>b<br>i<br>c<br>a<br>r<br>b<br>o<br>n<br>a<br>t<br>e<br>（<br>N<br>a<br>H<br>C<br>O<br>3<br>） | 已配平 | 是 |
| 15 | `ammonium_chloride` | T2 | `chem_reactor` | 默认 | g<br>a<br>s<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>（<br>N<br>H<br>3<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>H<br>C<br>l<br>(<br>g<br>)<br>） → s<br>o<br>l<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>u<br>m<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>N<br>H<br>4<br>C<br>l<br>） | 已配平 | 是 |
| 16 | `haber` | T2 | `high_pressure_synth` | temp档3、催化剂 | g<br>a<br>s<br>_<br>n<br>i<br>t<br>r<br>o<br>g<br>e<br>n<br>（<br>N<br>2<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>（<br>H<br>2<br>） → g<br>a<br>s<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>（<br>N<br>H<br>3<br>） | 已配平 | 是 |
| 17 | `nitric_from_niter` | T2 | `fractionator` | temp档3 | s<br>o<br>l<br>_<br>p<br>o<br>t<br>a<br>s<br>s<br>i<br>u<br>m<br>_<br>n<br>i<br>t<br>r<br>a<br>t<br>e<br>（<br>K<br>N<br>O<br>3<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>s<br>u<br>l<br>f<br>u<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>2<br>S<br>O<br>4<br>） → l<br>i<br>q<br>_<br>n<br>i<br>t<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>N<br>O<br>3<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>p<br>o<br>t<br>a<br>s<br>s<br>i<br>u<br>m<br>_<br>s<br>u<br>l<br>f<br>a<br>t<br>e<br>（<br>K<br>2<br>S<br>O<br>4<br>） | 已配平 | 是 |
| 18 | `ostwald` | T3 | `fractionator` | temp档3、催化剂 | g<br>a<br>s<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>（<br>N<br>H<br>3<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>o<br>x<br>y<br>g<br>e<br>n<br>（<br>O<br>2<br>） → l<br>i<br>q<br>_<br>n<br>i<br>t<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>N<br>O<br>3<br>） | 未配平/需复核 | 是 |
| 19 | `hcl_absorption` | T2 | `condenser_unit` | temp档0 | g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>H<br>C<br>l<br>(<br>g<br>)<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） → l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>c<br>h<br>l<br>o<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>C<br>l<br>(<br>a<br>q<br>)<br>） | 物理过程（非化学计量） | 是 |
| 20 | `ammonia_absorption` | T2 | `condenser_unit` | temp档0 | g<br>a<br>s<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>（<br>N<br>H<br>3<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） → l<br>i<br>q<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>N<br>H<br>3<br>·<br>H<br>2<br>O<br>） | 已配平 | 是 |
| 21 | `salt_crystallize` | T2 | `crystallizer` | temp档2 | l<br>i<br>q<br>_<br>b<br>r<br>i<br>n<br>e<br>（<br>N<br>a<br>C<br>l<br>(<br>a<br>q<br>)<br>） → s<br>o<br>l<br>_<br>s<br>o<br>d<br>i<br>u<br>m<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>N<br>a<br>C<br>l<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 物理过程（非化学计量） | 是 |
| 22 | `nh4no3_crystallize` | T2 | `crystallizer` | temp档2 | l<br>i<br>q<br>_<br>n<br>i<br>t<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>N<br>O<br>3<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>N<br>H<br>3<br>·<br>H<br>2<br>O<br>） → s<br>o<br>l<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>u<br>m<br>_<br>n<br>i<br>t<br>r<br>a<br>t<br>e<br>（<br>N<br>H<br>4<br>N<br>O<br>3<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 物理过程（非化学计量） | 是 |
| 23 | `neutralization` | T2 | `titration_bench` | pH=7、催化剂 | l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>c<br>h<br>l<br>o<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>C<br>l<br>(<br>a<br>q<br>)<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>s<br>o<br>d<br>i<br>u<br>m<br>_<br>h<br>y<br>d<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>N<br>a<br>O<br>H<br>(<br>a<br>q<br>)<br>） → l<br>i<br>q<br>_<br>b<br>r<br>i<br>n<br>e<br>（<br>N<br>a<br>C<br>l<br>(<br>a<br>q<br>)<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 已配平 | 是 |
| 24 | `decant_hcl` | T3 | `decanter` | 默认 | l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>c<br>h<br>l<br>o<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>C<br>l<br>(<br>a<br>q<br>)<br>） → l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>c<br>h<br>l<br>o<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>C<br>l<br>(<br>a<br>q<br>)<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>（<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>） | 物理过程（非化学计量） | 是 |
| 25 | `decant_sulfuric` | T3 | `decanter` | 默认 | l<br>i<br>q<br>_<br>s<br>u<br>l<br>f<br>u<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>2<br>S<br>O<br>4<br>） → l<br>i<br>q<br>_<br>s<br>u<br>l<br>f<br>u<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>2<br>S<br>O<br>4<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>（<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>） | 物理过程（非化学计量） | 是 |
| 26 | `decant_nitric` | T3 | `decanter` | 默认 | l<br>i<br>q<br>_<br>n<br>i<br>t<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>N<br>O<br>3<br>） → l<br>i<br>q<br>_<br>n<br>i<br>t<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>N<br>O<br>3<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>（<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>） | 物理过程（非化学计量） | 是 |
| 27 | `decant_peroxide` | T3 | `decanter` | 默认 | l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>p<br>e<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>H<br>2<br>O<br>2<br>） → l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>p<br>e<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>H<br>2<br>O<br>2<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>（<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>） | 物理过程（非化学计量） | 是 |
| 28 | `decant_ammonia` | T3 | `decanter` | 默认 | l<br>i<br>q<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>N<br>H<br>3<br>·<br>H<br>2<br>O<br>） → l<br>i<br>q<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>N<br>H<br>3<br>·<br>H<br>2<br>O<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>（<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>） | 物理过程（非化学计量） | 是 |
| 29 | `decant_brine` | T3 | `decanter` | 默认 | l<br>i<br>q<br>_<br>b<br>r<br>i<br>n<br>e<br>（<br>N<br>a<br>C<br>l<br>(<br>a<br>q<br>)<br>） → l<br>i<br>q<br>_<br>b<br>r<br>i<br>n<br>e<br>（<br>N<br>a<br>C<br>l<br>(<br>a<br>q<br>)<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>（<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>） | 物理过程（非化学计量） | 是 |
| 30 | `decant_calcium` | T3 | `decanter` | 默认 | l<br>i<br>q<br>_<br>c<br>a<br>l<br>c<br>i<br>u<br>m<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>C<br>a<br>C<br>l<br>2<br>(<br>a<br>q<br>)<br>） → l<br>i<br>q<br>_<br>c<br>a<br>l<br>c<br>i<br>u<br>m<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>C<br>a<br>C<br>l<br>2<br>(<br>a<br>q<br>)<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>（<br>s<br>e<br>d<br>i<br>m<br>e<br>n<br>t<br>） | 物理过程（非化学计量） | 是 |
| 31 | `scrub_oxygen` | T3 | `gas_scrubber` | 默认 | g<br>a<br>s<br>_<br>o<br>x<br>y<br>g<br>e<br>n<br>（<br>O<br>2<br>） → g<br>a<br>s<br>_<br>o<br>x<br>y<br>g<br>e<br>n<br>（<br>O<br>2<br>） | 物理过程（非化学计量） | 是 |
| 32 | `scrub_hydrogen` | T3 | `gas_scrubber` | 默认 | g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>（<br>H<br>2<br>） → g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>（<br>H<br>2<br>） | 物理过程（非化学计量） | 是 |
| 33 | `scrub_nitrogen` | T3 | `gas_scrubber` | 默认 | g<br>a<br>s<br>_<br>n<br>i<br>t<br>r<br>o<br>g<br>e<br>n<br>（<br>N<br>2<br>） → g<br>a<br>s<br>_<br>n<br>i<br>t<br>r<br>o<br>g<br>e<br>n<br>（<br>N<br>2<br>） | 物理过程（非化学计量） | 是 |
| 34 | `scrub_chlorine` | T3 | `gas_scrubber` | 默认 | g<br>a<br>s<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>n<br>e<br>（<br>C<br>l<br>2<br>） → g<br>a<br>s<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>n<br>e<br>（<br>C<br>l<br>2<br>） | 物理过程（非化学计量） | 是 |
| 35 | `scrub_ammonia` | T3 | `gas_scrubber` | 默认 | g<br>a<br>s<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>（<br>N<br>H<br>3<br>） → g<br>a<br>s<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>（<br>N<br>H<br>3<br>） | 物理过程（非化学计量） | 是 |
| 36 | `scrub_hcl_gas` | T3 | `gas_scrubber` | 默认 | g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>H<br>C<br>l<br>(<br>g<br>)<br>） → g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>H<br>C<br>l<br>(<br>g<br>)<br>） | 物理过程（非化学计量） | 是 |
| 37 | `fill_oxygen` | T3 | `canister_filling` | 默认 | g<br>a<br>s<br>_<br>o<br>x<br>y<br>g<br>e<br>n<br>（<br>O<br>2<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>c<br>a<br>n<br>i<br>s<br>t<br>e<br>r<br>_<br>e<br>m<br>p<br>t<br>y<br>（<br>s<br>t<br>e<br>e<br>l<br>） → c<br>a<br>n<br>_<br>o<br>x<br>y<br>g<br>e<br>n<br>_<br>i<br>n<br>d<br>u<br>s<br>t<br>r<br>i<br>a<br>l<br>（<br>O<br>2<br>） | 物理过程（非化学计量） | 是 |
| 38 | `fill_hydrogen` | T3 | `canister_filling` | 默认 | g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>（<br>H<br>2<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>c<br>a<br>n<br>i<br>s<br>t<br>e<br>r<br>_<br>e<br>m<br>p<br>t<br>y<br>（<br>s<br>t<br>e<br>e<br>l<br>） → c<br>a<br>n<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>i<br>n<br>d<br>u<br>s<br>t<br>r<br>i<br>a<br>l<br>（<br>H<br>2<br>） | 物理过程（非化学计量） | 是 |
| 39 | `fill_nitrogen` | T3 | `canister_filling` | 默认 | g<br>a<br>s<br>_<br>n<br>i<br>t<br>r<br>o<br>g<br>e<br>n<br>（<br>N<br>2<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>c<br>a<br>n<br>i<br>s<br>t<br>e<br>r<br>_<br>e<br>m<br>p<br>t<br>y<br>（<br>s<br>t<br>e<br>e<br>l<br>） → c<br>a<br>n<br>_<br>n<br>i<br>t<br>r<br>o<br>g<br>e<br>n<br>_<br>i<br>n<br>d<br>u<br>s<br>t<br>r<br>i<br>a<br>l<br>（<br>N<br>2<br>） | 物理过程（非化学计量） | 是 |
| 40 | `fill_chlorine` | T3 | `canister_filling` | 默认 | g<br>a<br>s<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>n<br>e<br>（<br>C<br>l<br>2<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>c<br>a<br>n<br>i<br>s<br>t<br>e<br>r<br>_<br>e<br>m<br>p<br>t<br>y<br>（<br>s<br>t<br>e<br>e<br>l<br>） → c<br>a<br>n<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>n<br>e<br>_<br>i<br>n<br>d<br>u<br>s<br>t<br>r<br>i<br>a<br>l<br>（<br>C<br>l<br>2<br>） | 物理过程（非化学计量） | 是 |
| 41 | `voidsalt` | T4 | `chem_reactor` | temp档3 | m<br>e<br>d<br>_<br>b<br>o<br>t<br>t<br>l<br>e<br>_<br>v<br>i<br>o<br>l<br>e<br>t<br> <br>+<br> <br>g<br>a<br>s<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>d<br>e<br>（<br>H<br>C<br>l<br>(<br>g<br>)<br>）<br> <br>+<br> <br>g<br>a<br>s<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>a<br>（<br>N<br>H<br>3<br>） → i<br>l<br>l<br>i<br>c<br>i<br>t<br>_<br>v<br>o<br>i<br>d<br>s<br>a<br>l<br>t<br>（<br>v<br>o<br>i<br>d<br>s<br>a<br>l<br>t<br>） | 虚构（设计非化学计量） | 是 |
| 42 | `redmist` | T4 | `chem_reactor` | temp档2 | g<br>a<br>s<br>_<br>c<br>h<br>l<br>o<br>r<br>i<br>n<br>e<br>（<br>C<br>l<br>2<br>）<br> <br>+<br> <br>m<br>e<br>d<br>_<br>b<br>o<br>t<br>t<br>l<br>e<br>_<br>o<br>r<br>a<br>n<br>g<br>e<br> <br>+<br> <br>m<br>e<br>d<br>_<br>b<br>o<br>t<br>t<br>l<br>e<br>_<br>s<br>m<br>a<br>l<br>l<br>_<br>p<br>i<br>n<br>k → i<br>l<br>l<br>i<br>c<br>i<br>t<br>_<br>r<br>e<br>d<br>m<br>i<br>s<br>t<br>（<br>r<br>e<br>d<br>m<br>i<br>s<br>t<br>） | 虚构（设计非化学计量） | 是 |
| 43 | `nightglass` | T4 | `high_pressure_synth` | temp档3 | d<br>r<br>e<br>a<br>m<br>_<br>c<br>a<br>p<br> <br>+<br> <br>l<br>i<br>q<br>_<br>s<br>u<br>l<br>f<br>u<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>2<br>S<br>O<br>4<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>p<br>o<br>t<br>a<br>s<br>s<br>i<br>u<br>m<br>_<br>p<br>e<br>r<br>m<br>a<br>n<br>g<br>a<br>n<br>a<br>t<br>e<br>（<br>K<br>M<br>n<br>O<br>4<br>） → i<br>l<br>l<br>i<br>c<br>i<br>t<br>_<br>n<br>i<br>g<br>h<br>t<br>g<br>l<br>a<br>s<br>s<br>（<br>n<br>i<br>g<br>h<br>t<br>g<br>l<br>a<br>s<br>s<br>） | 虚构（设计非化学计量） | 是 |
| 44 | `ashbreath` | T4 | `fractionator` | temp档3 | l<br>i<br>q<br>_<br>n<br>i<br>t<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>N<br>O<br>3<br>）<br> <br>+<br> <br>s<br>o<br>l<br>_<br>a<br>c<br>t<br>i<br>v<br>a<br>t<br>e<br>d<br>_<br>c<br>a<br>r<br>b<br>o<br>n<br>（<br>C<br>）<br> <br>+<br> <br>m<br>e<br>d<br>_<br>b<br>o<br>t<br>t<br>l<br>e<br>_<br>s<br>m<br>a<br>l<br>l<br>_<br>p<br>i<br>n<br>k → i<br>l<br>l<br>i<br>c<br>i<br>t<br>_<br>a<br>s<br>h<br>b<br>r<br>e<br>a<br>t<br>h<br>（<br>a<br>s<br>h<br>b<br>r<br>e<br>a<br>t<br>h<br>） | 虚构（设计非化学计量） | 是 |

**逐条备注**：
- T1 电解/煅烧/分解类为有限投料（盐水、石灰石、KMnO₄、H₂O₂ 等来自掉落或前序反应），电解氯化钠同时产 Cl₂/H₂/NaOH，是整条链的起点。
- T2 的 `hcl_absorption` / `ammonia_absorption` 按「气体溶于水」处理：产出为同种酸/碱溶液，方程标注「水为溶剂，非化学计量」，不计入严格配平。
- T2 `salt_crystallize` 为蒸发结晶（NaCl(aq)→NaCl(s)+H₂O，母液），同样为物理过程。
- T3 滗析（decanter）与气体洗涤（gas_scrubber）是**物理提纯**：主产物与投料同 id（仅纯度/等级提升），沉淀为 `sol_sediment` 副产物。
- T3 灌装（canister_filling）为物理灌装：`gas_* + gas_canister_empty → can_*_industrial`（仅工业 SKU，纯度写入 `fc_purity`）。
- T4 四条为虚构违禁品（equation 已标「虚构」），按设计不追求真实配平；每条至少使用一种原版化合物（见 §4）。

## 3. 实时热反应审计（6 条，加热台）

| 反应 id | 温区 °C | 降解温度 | 秒 | 投料 → 产物 | 配平 |
|---|---|---|---|---|---|
| `heat_hcl_distill` | 78–110 | 130 | 20 | l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>c<br>h<br>l<br>o<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>C<br>l<br>(<br>a<br>q<br>)<br>） → l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>c<br>h<br>l<br>o<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>C<br>l<br>(<br>a<br>q<br>)<br>） | 未配平/需复核 |
| `heat_ethanol_dehydrate` | 160–180 | 200 | 30 | l<br>i<br>q<br>_<br>e<br>t<br>h<br>a<br>n<br>o<br>l<br>（<br>C<br>2<br>H<br>5<br>O<br>H<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>s<br>u<br>l<br>f<br>u<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>2<br>S<br>O<br>4<br>） → g<br>a<br>s<br>_<br>e<br>t<br>h<br>y<br>l<br>e<br>n<br>e<br>（<br>C<br>2<br>H<br>4<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 已配平 |
| `heat_kmno4` | 140–160 | 190 | 25 | s<br>o<br>l<br>_<br>p<br>o<br>t<br>a<br>s<br>s<br>i<br>u<br>m<br>_<br>p<br>e<br>r<br>m<br>a<br>n<br>g<br>a<br>n<br>a<br>t<br>e<br>（<br>K<br>M<br>n<br>O<br>4<br>） → g<br>a<br>s<br>_<br>o<br>x<br>y<br>g<br>e<br>n<br>（<br>O<br>2<br>） | 已配平 |
| `heat_nh4no3` | 150–160 | 200 | 25 | s<br>o<br>l<br>_<br>a<br>m<br>m<br>o<br>n<br>i<br>u<br>m<br>_<br>n<br>i<br>t<br>r<br>a<br>t<br>e<br>（<br>N<br>H<br>4<br>N<br>O<br>3<br>） → g<br>a<br>s<br>_<br>n<br>i<br>t<br>r<br>o<br>u<br>s<br>_<br>o<br>x<br>i<br>d<br>e<br>（<br>N<br>2<br>O<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 已配平 |
| `heat_peroxide` | 80–120 | 150 | 15 | l<br>i<br>q<br>_<br>h<br>y<br>d<br>r<br>o<br>g<br>e<br>n<br>_<br>p<br>e<br>r<br>o<br>x<br>i<br>d<br>e<br>（<br>H<br>2<br>O<br>2<br>） → g<br>a<br>s<br>_<br>o<br>x<br>y<br>g<br>e<br>n<br>（<br>O<br>2<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>w<br>a<br>t<br>e<br>r<br>（<br>H<br>2<br>O<br>） | 已配平 |
| `heat_niter` | 120–160 | 190 | 30 | s<br>o<br>l<br>_<br>p<br>o<br>t<br>a<br>s<br>s<br>i<br>u<br>m<br>_<br>n<br>i<br>t<br>r<br>a<br>t<br>e<br>（<br>K<br>N<br>O<br>3<br>）<br> <br>+<br> <br>l<br>i<br>q<br>_<br>s<br>u<br>l<br>f<br>u<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>2<br>S<br>O<br>4<br>） → l<br>i<br>q<br>_<br>n<br>i<br>t<br>r<br>i<br>c<br>_<br>a<br>c<br>i<br>d<br>（<br>H<br>N<br>O<br>3<br>） | 已配平 |

**可达性修复**：本轮审计发现加热台原实现存在三个硬伤，已修复——
1. 反应槽只接受烧杯/锥形瓶等容器，而匹配读取的是槽内物品 id → 6 条热反应永远匹配不到；现反应槽改为 **2×2 网格**并接受「容器 + 本模组药剂 + 原版药剂瓶」。
2. 热反应成功后从不销毁投料 → 可无限白嫖产物；现按匹配配方销毁参与投料的药剂（容器保留），超温降解同样消耗投料。
3. 高档 160°C 无任何入口（`ToggleHigh` 无调用点），且 `heat_kmno4`(200–260°C)、`heat_nh4no3`(180–250°C) 超过高档上限；现**点击酒精灯/喷灯切换高/低档**，并把两条超出上限的温区改为 140–160 / 150–160°C（降解温度同步下调），ValidateLogic 增加「热反应起始温度 ≤160°C」断言。

## 4. 与原版化合物反应的 T4 违禁品映射

| 违禁品 | 原版输入（VanillaAnalog） | 替代/理由 | 其余投料来源 |
|---|---|---|---|
| `illicit_voidsalt` | med_bottle_violet → liq_ammonia_water | 原版紫药瓶作为「N-羟甲基碱」载体（研究 §4：原版药瓶可作反应物） | HCl(g)、NH₃ 由 T1/T2 链产出 |
| `illicit_redmist` | med_bottle_orange、med_bottle_small_pink | 原版橙药瓶=还原剂载体、小粉瓶=脂合剂载体 | Cl₂ 由电解/盐酸电解或钢瓶供给 |
| `illicit_nightglass` | dream_cap → sol_activated_carbon | 原版梦帽菇（水培产出）作为有机碳源 | H₂SO₄（湿法硫酸）、KMnO₄（掉落/热分解） |
| `illicit_ashbreath` | med_bottle_small_pink | 原版氨水瓶（本轮补入；此前该 T4 没有任何原版输入，违背设计前提） | HNO₃（硝石制硝酸/奥斯特瓦尔德）、活性炭（掉落） |

T4 外部依赖（仅需这些外部物品即可合成，掉落表可重复刷新）：
- 空盐：`med_bottle_violet` +（H₂+Cl₂→HCl(g)）+（N₂+H₂→NH₃(g)）；N₂ 来自掉落 `gas_nitrogen`，Cl₂ 来自电解盐水/盐酸。
- 红雾：`med_bottle_orange` + `med_bottle_small_pink` + Cl₂。
- 夜玻：`dream_cap` + `sol_potassium_permanganate`（掉落）+ H₂SO₄。
- 灰息：`med_bottle_small_pink` + `sol_activated_carbon`（掉落）+ HNO₃。

## 5. 气体供应 / 气瓶槽审计

- 气瓶槽（2 个/台，含加热台）反查 `CanisterSkus`：可供应 **O₂ / H₂ / N₂ / Cl₂** 四种气体（工业/医用/分析纯钢瓶均可，纯度带入投入纯度档）。
- 双气配方均可匹配：`hcl_synthesis`(H₂+Cl₂)、`haber`(N₂+H₂) 用 2 个气瓶槽；`ostwald`(NH₃+O₂) 用 1 个钢瓶 + 散装 NH₃ 气体。
- 非钢瓶气体（CO₂、NH₃、HCl(g)、N₂O、C₂H₄）**没有 canister SKU**：由反应产出为散装 `gas_*` 物品后直接投入设备输入槽（`AcceptsInput` 允许气体相态、chem_reactor/fractionator 等不限相态），这是当前设计的明确限制（不新增 ID）。
- 守恒：气瓶被消耗后在产出位放回 `gas_canister_empty`；输出满则保留气瓶并日志（不丢失/复制）。
- 无物理杂质：`fc_contam_*` 只对 `Phase.Liquid && Kind=="liquid"` 写入，气瓶进槽时再清零一次并日志。

## 6. 设备与器具操作步骤清单

| 设备 | 槽位/相态 | 能做什么 | 玩家步骤 |
|---|---|---|---|
| `chem_reactor` | 4 输入 + 2 气瓶槽（不限相态） | 绝大多数合成/吸收/结晶前置反应 | 电池+模组 → 投料 → 设温度计/可选 pH → 周期结束取产物 |
| `electrolyzer` | 4 输入 + 2 气瓶槽 | 盐水/盐酸电解产 Cl₂/H₂/NaOH | 投料（盐水或盐酸）→ 温度计设「加热」→ 取产物/气体 |
| `gas_generator` | 4 输入 + 2 气瓶槽 | 加热分解产气体（CO₂/O₂/NH₃/HCl(g)） | 投料 → 温度计按需设档 → 气体以散装 gas_* 物品产出 |
| `condenser_unit` | 4 输入 + 2 气瓶槽（Gas+Liquid） | 气体吸收制酸/碱液（HCl/NH₃ 吸收） | 投入气体 + 水 + 可选气瓶 → 取溶液 |
| `crystallizer` | 4 输入 + 2 气瓶槽（Liquid） | 蒸发结晶（盐水→盐+水；硝酸铵结晶） | 投液体 → 温度计设「加热」→ 取固体+水 |
| `high_pressure_synth` | 4 输入 + 2 气瓶槽 | 哈伯法 NH₃、夜玻 | 温度计设「高温高压」+ 双气瓶槽（N₂/H₂）→ 取产物 |
| `fractionator` | 4 输入 + 2 气瓶槽 | 硝石制硝酸、奥斯特瓦尔德、灰息 | 温度计设「高温高压」→ 投料（可含散装 NH₃）→ 取硝酸/违禁品 |
| `titration_bench` | 4 输入 + 2 气瓶槽 | 酸碱中和（pH=7） | pH 计设 7 → 投 HCl+NaOH → 取盐水+水 |
| `canister_filling` | 3 输入 + 2 气瓶槽（Gas） | 气体灌装钢瓶/大桶 | 投入散装气体 + 空钢瓶/空桶 → 取工业钢瓶/工业桶 |
| `decanter` | 4 输入 + 2 气瓶槽（Liquid） | 静置沉降提纯液体（7 种药液） | 投液体 → 取同种清液 + 沉淀 |
| `gas_scrubber` | 4 输入 + 2 气瓶槽（Gas） | 气体洗涤提纯（6 种气体） | 投气体 → 取高纯气体 |
| `filtration_bench` | 3 输入（Liquid）+ 2 气瓶槽 | 滤纸过滤：拦截颗粒、提纯等级 +1 | 投液体 + 滤纸 → 取滤液（滤纸耐久 -1，用完销毁） |
| `heating_platform` | 反应槽 2×2 + 加热器/灯芯/燃料槽 + 2 气瓶槽 + 产出 | 6 条实时热反应；点击酒精灯切高/低档 | 装灯芯+酒精灯 → 燃料槽放乙醇/原版酒精 → 反应槽放投料（可直接放药剂）→ 点火升温到温区保持 → 取产物 |

**耗材/仪器交互步骤**：
- 酒精灯：加热台「加热器槽」放酒精灯/喷灯，「灯芯槽」放 wick_*；「燃料槽」放乙醇或原版酒精 → 自动灌入灯内（500 ml 容量，按纯度 500×纯度% 灌入；70% 原版酒精≈350 ml）。灯内燃料耗尽/灯芯归零后需补燃料、换灯芯。**点击灯=切换高档 160°C / 低档 99°C**（日志显示）。
- 滤纸：仅过滤台接受 `filter_paper*`；按孔径提纯并消耗耐久，用尽自动销毁。
- 标签打印机：点击药剂瓶按当前模式盖章（14 标签模式 + 第 15 校准模式；校准需真实纯度 ≥99.5% 且 catalyst_kit 充能）。
- 灌装机：散装气体 + 空钢瓶/空桶 → 工业钢瓶/工业桶（P1b1 后仅工业 SKU；医用/分析纯钢瓶为旧存档 legacy）。
- 气瓶槽：把钢瓶放入机器新增的 2 个气瓶槽即可按气体供料；反应消耗后回空瓶。

## 7. 发现并修复的问题（本轮）

| # | 问题 | 影响 | 修复 |
|---|---|---|---|
| 1 | 加热台反应槽只接受容器，匹配却读投料 id | 6 条热反应全部不可达 | 反应槽改 2×2 网格并接受药剂/原版药剂瓶 |
| 2 | 热反应成功后不消耗投料 | 可无限复制产物 | 新增 `ConsumeThermalInputs`，按配方销毁参与投料（超温降解同样消耗） |
| 3 | `ToggleHigh` 无调用点、高档 160°C 不可达 | 高温热反应不可用 | 点击酒精灯/喷灯切换高/低档；日志提示 |
| 4 | `heat_kmno4` 温区 200–260°C > 高档 160°C | 永远无法触发 | 改为 140–160°C（降解 190°C） |
| 5 | `heat_nh4no3` 温区 180–250°C > 高档 160°C | 永远无法触发 | 改为 150–160°C（降解 200°C） |
| 6 | `illicit_ashbreath` 没有任何原版输入 | 违背 T4「与原版化合物反应」设计 | 原版氨水瓶（med_bottle_small_pink）替换散装 NH₃ |
| 7 | `ValidateLogic` 把所有 inputs 当本模组 id 校验 | 原版药剂输入被误报未注册 | 对 `VanillaAnalog` 键豁免 |
| 8 | `hcl_absorption` / `salt_crystallize` 方程按化学计量看「不平衡」 | 审计误报 | 方程明确标注「水为溶剂/母液，非化学计量」的物理过程 |
| 9 | 加热台高档无量测/提示 | 玩家不知道当前档位 | 灯点击日志显示 HIGH/LOW；`inst_temp` 继续显示实时温度 |

### 新增/加强的自动断言（`ChemRecipes.ValidateLogic` / `ChemLab.ValidateLogic`）

- 反应 id 唯一；`Machine ∈ MachineIds`；inputs/outputs 非空、输入不重复；temp 0–3、pH -1..14。
- 气体输入必须「在 CanisterSkus 里」或「由其它反应/热反应产出」，否则死配方。
- 每个产物必须有去向：可销售（Value>0）或被其它反应继续使用。
- 每个 T4 至少一个原版输入（`VanillaAnalog` 键）。
- `MachineIds` 每台机器必须有反应、或属辅助设备（filtration_bench）、或实时加热台。
- 热反应 id 唯一、温区不倒置、`MaxTemp ≤ DegradeTemp`、**起始温度 ≤160°C（高档上限）**、equation 含「→」；并断言低档 99°C 与高档 160°C 都有可完成的热反应。

## 8. 仍未解决 / 需实机验证

- 医用/分析纯钢瓶（8 个 legacy `can_*_medical/ultrapure`）自 P1b1 起不再产出，仅保留注册与旧存档兼容；图标/本地化仍在。
- `sol_caffeine`(#48) / `sol_coffee_grounds`(#47) 按 Lead 口径为 legacy 无来源（原版咖啡/咖啡因走原版逻辑）。
- 双氧水（`liq_hydrogen_peroxide`）当前只有掉落来源，没有合成反应；湿法硫酸与双氧水分解都消耗它，长期依赖掉落刷新（掉落表可重复，非死锁）。
- CO₂/NH₃/HCl(g)/N₂O/C₂H₄ 没有钢瓶 SKU，需散装气体物品投料；气瓶槽只覆盖 O₂/H₂/N₂/Cl₂。
- 温度档/pH 档是**全局仪器设置**（温度计/pH 计点击切换），不是逐台机器独立温控；玩家需在投料前设定正确档位，否则配方不匹配。
- 全部游戏内行为（热反应实机可用性、投料销毁、灯档切换、滤纸、电话/索引书/气瓶槽）**未实机验证**，仅编译+逻辑断言。

