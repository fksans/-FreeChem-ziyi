using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeChem
{
    /// <summary>一台设备的规格 + 它的「投料 → 产物」解析函数。</summary>
    public sealed class ChemMachineDef
    {
        public string Id;
        public int Power, Speed, Cycles, InputSlots;
        // 参数：投料 id、温度、pH、是否有催化剂、投料最高纯度档 → 产物 id 列表
        public Func<List<string>, int, int, bool, int, List<string>> Resolve;
        public int TargetProgress => Speed * Cycles;
    }

    /// <summary>
    /// 设备工厂。窗口骨架沿用官方 LearnerMod 范例，因此与原版设备共用
    /// 电池槽 / 模组槽 / 电力 / 进度机制（MachineHelper / MachineryHelper /
    /// MachineProgressHelper / ModuleHelper），做到「不同功能但兼容原版」。
    /// </summary>
    public static class ChemMachines
    {
        static List<string> Out(params string[] ids) => new List<string>(ids);

        public static readonly List<ChemMachineDef> Defs = new List<ChemMachineDef>();

        static ChemMachines()
        {
            foreach (var id in ChemRecipes.MachineIds)
            {
                if (ChemRecipes.IsRealtime(id)) continue;   // 加热台由 ChemHeating 单独构建
                Defs.Add(new ChemMachineDef {
                    Id = id, Power = PowerOf(id), Speed = 100,
                    Cycles = (id == "decanter" || id == "gas_scrubber" || id == "filtration_bench") ? 1 : 2,
                    InputSlots = (id == "canister_filling" || id == "filtration_bench") ? 3 : 4,
                    // ↑ canister_filling 的 resolver 固定产出 skus[0]（工业钢瓶单 SKU；
                    //   P1b1 已取消 medical/ultrapure 产线，纯度改写入 fc_purity + 原版七档 feature）
                    Resolve = MakeResolver(id)
                });
            }
        }

        static int PowerOf(string id)
        {
            switch (id)
            {
                case "electrolyzer":       return 14;
                case "high_pressure_synth":return 16;
                case "fractionator":       return 12;
                case "chem_reactor":       return 10;
                case "gas_generator":      return 6;
                case "condenser_unit":     return 8;
                case "crystallizer":       return 5;   // P4a：低功率
                case "canister_filling":   return 9;
                case "gas_scrubber":       return 7;
                case "filtration_bench":   return 0;   // 重力过滤，不耗电
                case "heating_platform":   return 0;   // 酒精灯供热，不耗电；实时协程驱动
                case "decanter":           return 0;   // 静置沉淀，不耗电
                case "titration_bench":    return 0;   // 滴定：重力/手动滴定，不耗电
                default:                   return 8;
            }
        }

        static Func<List<string>, int, int, bool, int, List<string>> MakeResolver(string machineId)
        {
            // 过滤台：液体药剂 + 滤纸 → 同一种药剂（污染物按孔径下降，仍留下限）+ 用过的滤纸
            if (machineId == "filtration_bench")
                return (ids, temp, ph, catalyst, inputGrade) =>
                {
                    string liquid = null, paper = null, solid = null;
                    foreach (var i in ids)
                    {
                        if (i.StartsWith("liq_")) liquid = i;
                        if (i.StartsWith("filter_paper")) paper = i;
                        if (i.StartsWith("sol_") || i.StartsWith("illicit_")) solid = i;
                    }
                    if (paper == null) return Out("sludge");
                    if (liquid != null) return Out(liquid);   // 液体：污染物在 Finish 里按滤纸孔径处理
                    // P7c：固体「洗涤/重结晶」——固体 + 滤纸 也可提纯（纯度在 Finish 里 +5）
                    if (solid != null) return Out(solid);
                    return Out("sludge");
                };

            // 灌装台：目标空容器二选一——
            //   gas_canister_empty → CanisterSkus 三档（纯度档由进气决定）；
            //   gas_drum_empty     → DrumSkus 单档工业大桶（桶只有一张工业图标，不混档）。
            // 已充装的大桶 gas_drum_* 是【产物】，绝不能当气体源，否则会造成经济漏洞。
            if (machineId == "canister_filling")
                return (ids, temp, ph, catalyst, inputGrade) =>
                {
                    string gas = null;
                    bool hasCanister = false, hasDrum = false;
                    foreach (var i in ids)
                    {
                        if (i == "gas_canister_empty") { hasCanister = true; continue; }
                        if (i == ChemRecipes.DrumEmptyId) { hasDrum = true; continue; }
                        if (i.StartsWith("gas_drum_")) continue;   // 已充装大桶：产物，不是气体源
                        if (i.StartsWith("gas_")) gas = i;
                    }
                    // 没有气体源 / 两个空容器同时在场 / 没有目标空容器 → 不猜，产废渣
                    if (gas == null || hasCanister == hasDrum) return Out("sludge");
                    if (hasDrum)
                    {
                        string drumId;
                        if (!ChemRecipes.DrumSkus.TryGetValue(gas, out drumId)) return Out("sludge");
                        return Out(drumId);
                    }
                    string[] skus;
                    if (!ChemRecipes.CanisterSkus.TryGetValue(gas, out skus)) return Out("sludge");
                    // P1b1：取消医用/化学纯终产物 —— 灌装台**始终产出工业钢瓶**，
                    // 纯度档改由 Finish 按投入气体纯度写入 fc_purity + 原版七档 feature。
                    // can_*_medical/ultrapure 仅保留注册作旧存档兼容（legacy），不再产线产出。
                    return Out(skus[0]);
                };

            // 其余设备：查真实反应表；无匹配产废渣
            return (ids, temp, ph, catalyst, inputGrade) =>
            {
                // P7c：严格匹配失败时容忍多余投料（只消耗配方内物品）
                var r = ChemRecipes.Match(ids, machineId, temp, ph, catalyst)
                        ?? ChemRecipes.MatchIgnoringExtras(ids, machineId, temp, ph, catalyst);
                if (r == null) return Out(ChemRecipes.SludgeId);
                if (ChemRuntime.IsAccident(r.Hazard)) return Out(ChemRecipes.SludgeId);
                var result = Out(r.Outputs);
                if (ChemRecipes.ShouldBonus(r, ChemRuntime.Balanced, catalyst))
                    result.Add(r.Outputs[0]);
                return result;
            };
        }

        /// <summary>
        /// 设备投料过滤（问题 5：运行时相态过滤 + 原版药剂兼容）。
        /// 判定顺序：
        ///  1) 容器/催化剂/滤纸等按设备特判；
        ///  2) 有 ReagentTag 的本模组药剂：**先**按 AllowedInputPhases 判相态（再谈类型兼容）；
        ///  3) 无 tag 的原版物品：只接受 ChemRecipes.VanillaAnalog 里有映射的药剂瓶，
        ///     其余（工具/垃圾/其他原版化学用品）一律拒绝，避免被当投料销毁。
        /// </summary>
        static bool AcceptsInput(string machineId, GameItem it)
        {
            if (it == null) return false;
            if (!GeneralHelper.IsItemOwned(it)) return false;
            string cid = it.identifier;
            // NEW-P1-6：空钢瓶/空大桶只允许作为灌装台的目标容器，其它设备一律拒绝，
            // 避免误投后被 Finish 无条件销毁。
            if (cid == "gas_canister_empty" || cid == ChemRecipes.DrumEmptyId)
                return machineId == "canister_filling";
            if (cid == "catalyst_kit") return true;
            // 已充装的大桶是产物（不是容器、也不是气体源），任何设备都不收
            if (cid.StartsWith("gas_drum_")) return false;
            if (cid.StartsWith("filter_paper")) return machineId == "filtration_bench";
            // P10b/P10c：实验容器（烧杯/锥形瓶/试管）与试管架可放进任意机器输入槽
            if (ChemCatalog.IsVessel(cid) || cid == ChemCatalog.TestTubeRackId) return true;
            if (cid == "alcohol_lamp" || cid == "spirit_burner" || cid.StartsWith("wick_")) return false;
            // 仪器不参与任何设备的投料
            if (cid == "thermometer" || cid == "precision_balance"
                || cid == "ph_meter" || cid == "safety_goggles") return false;

            // 本模组真药剂：先相态过滤（问题 5 核心：不能因为类型是 CHEMICAL_SUPPLIES 就放行气体进滗析）
            if (it.IsTag(ChemRecipes.ReagentTag))
            {
                var allowed = ChemRecipes.AllowedInputPhases(machineId);
                if (allowed == null) return true;
                var p = ChemRecipes.PhaseOf(cid);
                foreach (var a in allowed) if (a == p) return true;
                return false;
            }

            // 原版物品：仅接受 VanillaAnalog 映射过的药剂瓶/原料；
            // 映射后的相态同样要先过设备相态过滤（P3b：原版水瓶映射为 liq_water 后仍不能进气体设备）
            if (ChemRecipes.VanillaAnalog.ContainsKey(cid))
            {
                var allowed = ChemRecipes.AllowedInputPhases(machineId);
                if (allowed == null) return true;
                var p = ChemRecipes.PhaseOf(cid);
                foreach (var a in allowed) if (a == p) return true;
                return false;
            }
            return false;
        }

        /// <summary>P3c 气瓶槽：只接受本模组气瓶商品（can_*，CanisterTag）且 owned。</summary>
        static bool AcceptsGasCanister(GameItem it)
        {
            if (it == null) return false;
            if (!GeneralHelper.IsItemOwned(it)) return false;
            if (!ChemCatalog.IsCanisterItem(it)) return false;
            string gasId;
            return ChemRecipes.TryGetGasFromCanister(it.identifier, out gasId);
        }

        /// <summary>实时大型加热台：容器 + 加热器 + 灯芯 + 燃料 + 输出，不走夜间结算。</summary>
        static GameItem CreateHeatingPlatform()
        {
            // P3f 审计修复：反应槽由单槽改为 2×2 网格（可同时放容器与多种药剂；
            // 双投料热反应 heat_ethanol_dehydrate / heat_niter 之前根本放不下两种原料）
            var vessel = new GameGridInventory(2, 2);
            var heater = new GameSlotInventory();
            var wick   = new GameSlotInventory();
            var fuel   = new GameSlotInventory();
            // P3c：加热台也保留 2 个气瓶槽（可作气体来源参与实时反应匹配）
            var gasSlots = new List<GameSlotInventory> { new GameSlotInventory(), new GameSlotInventory() };
            var output = new GameGridInventory(6, 3);
            var note   = new GameSlotInventory();

            // P3f 审计修复：反应槽既接受「反应容器」（烧杯/锥形瓶，纯展示不参与匹配），
            // 也接受本模组药剂与原版药剂瓶（否则 CollectThermalInput 只能读到 beaker 等容器 id，
            // 6 条热反应永远匹配不到 = 死功能）。
            vessel.mayInventoryAddItemFunc = (it, _) =>
                it != null && GeneralHelper.IsItemOwned(it) &&
                (it.identifier == "beaker" || it.identifier == "retort_flask"
                 || it.identifier == "erlenmeyer_flask" || it.identifier == "test_tube_rack"
                 || it.IsTag(ChemRecipes.ReagentTag)
                 || ChemRecipes.VanillaAnalog.ContainsKey(it.identifier));
            heater.mayInventoryAddItemFunc = (it, _) =>
                it != null && GeneralHelper.IsItemOwned(it) &&
                (it.identifier == "alcohol_lamp" || it.identifier == "spirit_burner");
            wick.mayInventoryAddItemFunc = (it, _) =>
                it != null && GeneralHelper.IsItemOwned(it) && it.identifier.StartsWith("wick_");
            fuel.mayInventoryAddItemFunc = (it, _) =>
                it != null && GeneralHelper.IsItemOwned(it) &&
                ChemCatalog.IsAlcoholFuel(it);
            foreach (var gs in gasSlots)
            {
                gs.mayInventoryAddItemFunc = (it, _) => AcceptsGasCanister(it);
                gs.onSlotAddItemEndFunc = (added, _, __) =>
                {
                    ChemCatalog.ClearCanisterContaminants(added);
                    ChemRuntime.Log("[gas] heating platform gas slot: " + added.identifier);
                };
            }
            output.mayInventoryAddItemFunc = (it, _) => it.IsTag(ChemRecipes.ReagentTag);
            note.mayInventoryAddItemFunc = (it, _) =>
                it != null && (it.identifier.Equals("postit") || it.identifier.Equals("handnote")
                               || it.identifier.Equals("slip"));
            note.SetBackgroundFadeSprite("Items/items_tool", "postit");
            WriteNoteHandnote(note, "heating_platform");

            // P4a：加热台也改用官方 Machinery 窗口（无电池面板；酒精灯供热）。
            // 面板顺序 = 存档顺序：反应槽 / 加热器（灯+芯+燃料）/ 气瓶槽 / 产出 / 便签。
            var heaterGrid = new GridPixelElement(3, 2, true);
            heaterGrid.Attach(new TagElement().SetText(Ui("ui_heater")), 0, 0, 20, 20);
            heaterGrid.Attach(new TagElement().SetText(Ui("ui_wick")),   1, 0, 20, 20);
            heaterGrid.Attach(new TagElement().SetText(Ui("ui_fuel")),   2, 0, 20, 20);
            heaterGrid.Attach(heater, 0, 1, 50, 20);
            heaterGrid.Attach(wick,   1, 1, 50, 20);
            heaterGrid.Attach(fuel,   2, 1, 50, 20);

            var gasGrid = new GridPixelElement(2, 1, true);
            gasGrid.Attach(gasSlots[0], 0, 0, 50, 20);
            gasGrid.Attach(gasSlots[1], 1, 0, 50, 20);

            var panels = new List<MachineryPanel>
            {
                MachineHelper.Panel(Ui("ui_vessel"), vessel, "vessel"),
                MachineHelper.Panel(Ui("ui_heater"), heaterGrid, "heater"),
                MachineHelper.Panel(Ui("ui_gas_slot"), gasGrid, "gas"),
                MachineHelper.Panel(Ui("ui_output"), output, "output"),
            };
            try { panels.Add(MachineHelper.NotePanel(note)); }
            catch (Exception) { panels.Add(MachineHelper.Panel(Ui("ui_note"), note, "note")); }
            var window = MachineHelper.CreateMachineryWindow(true, panels.ToArray());

            GameItem machine = ItemDirectory.CreateEmptyItem("heating_platform")
                .SetContentWindow(window)
                .SetSpriteAndShapeFromMod(ChemCatalog.ModId, "heating_platform");
            machine.name = ModHelper.GetLocalized(ChemCatalog.ModId, "heating_platform_name");
            machine.shortDescription = ModHelper.GetLocalized(ChemCatalog.ModId, "heating_platform_desc");
            machine.flavorText = ModHelper.GetLocalized(ChemCatalog.ModId, "heating_platform_flavor");
            machine.unitValue = 420;
            machine.SetGameItemType(TypeHelper.MACHINE);
            machine.EnableTag(ContainerHelper.CONTAINER_TAG);
            machine.EnableTag(MachineHelper.STANDARD_MACHINE_TAG);
            machine.EnableTag("fc_machine_heating_platform");

            ChemHeating.Register(machine, vessel, heater, wick, fuel, gasSlots, output);
            return machine;
        }

        /// <summary>
        /// P5a：机器便签（NotePanel）里放一张原版 handnote，写明用途/槽位/条件；随机器存档。
        /// 文案优先用 docs 提供的 machine_note_&lt;id&gt; 键，缺键时回退机器描述（不显示裸键）。
        /// </summary>
        static void WriteNoteHandnote(GameSlotInventory note, string machineId)
        {
            if (note == null) return;
            try
            {
                string key = "machine_note_" + machineId;
                string body = ModHelper.GetLocalized(ChemCatalog.ModId, key);
                if (string.IsNullOrEmpty(body) || body == key)
                    body = ModHelper.GetLocalized(ChemCatalog.ModId, machineId + "_desc");
                string title = ModHelper.GetLocalized(ChemCatalog.ModId, machineId + "_name");
                body = ChemRecipes.ToAsciiText(body);   // P9：便签文本不含 Unicode 下标
                GameItem hand = PreBuiltItemHelper.CreateHandnote(body, title);
                if (hand == null)
                {
                    ChemRuntime.Log("[note] CreateHandnote returned null for " + machineId + "; tooltip fallback active.");
                    return;
                }
                ModHelper.SetItemOwnerShip(hand, true);
                int left = GraphUtils.TryAcceptAll(note, hand);
                if (left > 0)
                    ChemRuntime.Log("[note] handnote not placed in note slot for " + machineId
                                    + "; tooltip fallback active.");
                else
                    ChemRuntime.Log("[note] machine note handnote placed for " + machineId + ".");
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[note] machine note failed (" + machineId + "): " + e.Message);
            }
        }

        /// <summary>P4a：按机器功能定制的窗口/槽位规格（越简单越好，减少无意义空格）。</summary>
        sealed class MachineLayout
        {
            public bool Powered;      // 是否有电池面板、是否需要扣电
            public bool Modules;      // 是否有模块槽
            public int InputSlots;    // 投料槽数量（按实际配方所需最小数量）
            public int OutputW, OutputH;
            public bool FilterSlot;   // 过滤台：滤纸专用槽
            public bool ContainerSlot;// 灌装台：空钢瓶/空桶专用槽
        }

        /// <summary>功能 → 窗口槽位规格。按「真实工艺 + 现有配方最小需求」设计：
        /// 静置/物理设备（滗析、过滤、滴定、加热台）**不用电**；
        /// 需要电的机器才带电池面板（crystallizer 低功率），只有反应釜/电解槽带模块槽。</summary>
        static MachineLayout LayoutOf(string id)
        {
            switch (id)
            {
                case "decanter":
                    return new MachineLayout { Powered = false, InputSlots = 1, OutputW = 3, OutputH = 3 };
                case "filtration_bench":
                    return new MachineLayout { Powered = false, InputSlots = 1, FilterSlot = true, OutputW = 3, OutputH = 3 };
                case "titration_bench":
                    return new MachineLayout { Powered = false, InputSlots = 2, OutputW = 3, OutputH = 3 };
                case "crystallizer":
                    return new MachineLayout { Powered = true, InputSlots = 2, OutputW = 3, OutputH = 3 };
                case "chem_reactor":
                    return new MachineLayout { Powered = true, Modules = true, InputSlots = 3, OutputW = 3, OutputH = 3 };
                case "electrolyzer":
                    return new MachineLayout { Powered = true, Modules = true, InputSlots = 2, OutputW = 4, OutputH = 4 };
                case "gas_generator":
                    return new MachineLayout { Powered = true, InputSlots = 2, OutputW = 4, OutputH = 4 };
                case "condenser_unit":
                    return new MachineLayout { Powered = true, InputSlots = 2, OutputW = 3, OutputH = 3 };
                case "fractionator":
                    return new MachineLayout { Powered = true, InputSlots = 3, OutputW = 3, OutputH = 3 };
                case "high_pressure_synth":
                    return new MachineLayout { Powered = true, InputSlots = 3, OutputW = 3, OutputH = 3 };
                case "canister_filling":
                    return new MachineLayout { Powered = true, InputSlots = 1, ContainerSlot = true, OutputW = 4, OutputH = 4 };
                case "gas_scrubber":
                    return new MachineLayout { Powered = true, InputSlots = 1, OutputW = 3, OutputH = 3 };
                default:
                    return new MachineLayout { Powered = true, InputSlots = 2, OutputW = 3, OutputH = 3 };
            }
        }

        public static GameItem Create(ChemMachineDef def)
        {
            if (ChemRecipes.IsRealtime(def.Id)) return CreateHeatingPlatform();
            MachineLayout layout = LayoutOf(def.Id);

            // P4a：只有需要电的机器才有电池槽；静置/物理设备完全没有电池面板，也不扣电
            GameSlotInventory battery = layout.Powered ? new GameSlotInventory() : null;
            GameGridInventory modules = layout.Modules
                ? new GameGridInventory(MachineHelper.DEFAULT_MODULE_BAY_CONFIG,
                                        MachineHelper.DEFAULT_MODULE_BAY_WIDTH)
                : null;

            var inputs = new List<GameSlotInventory>();
            for (int i = 0; i < layout.InputSlots; i++) inputs.Add(new GameSlotInventory());
            GameSlotInventory filterSlot = layout.FilterSlot ? new GameSlotInventory() : null;
            if (filterSlot != null) inputs.Add(filterSlot);
            GameSlotInventory containerSlot = layout.ContainerSlot ? new GameSlotInventory() : null;
            if (containerSlot != null) inputs.Add(containerSlot);
            // P3c：气瓶槽保留（气瓶商品 → 内部气体 id；双气配方需要 2 个）；
            // 玩家审查：灌装台自身就是灌瓶设备，气瓶槽会与「气体→钢瓶」语义冲突 → 不显示气瓶槽。
            var gasSlots = def.Id == "canister_filling"
                ? new List<GameSlotInventory>()
                : new List<GameSlotInventory> { new GameSlotInventory(), new GameSlotInventory() };
            var output = new GameGridInventory(layout.OutputW, layout.OutputH);
            var note = new GameSlotInventory();

            var window = BuildWindow(def, layout, battery, modules, inputs, gasSlots, output, note);

            foreach (var slot in inputs)
                slot.mayInventoryAddItemFunc = (it, _) => AcceptsInput(def.Id, it);
            // P6a 仪器自检修复：专用槽只允许一侧语义，避免把专用物放进普通投料槽
            if (def.Id == "filtration_bench" && inputs.Count > 0)
                inputs[0].mayInventoryAddItemFunc = (it, _) =>
                    AcceptsInput(def.Id, it)
                    && (it == null || !it.identifier.StartsWith("filter_paper"));
            if (def.Id == "canister_filling" && inputs.Count > 0)
                inputs[0].mayInventoryAddItemFunc = (it, _) =>
                    AcceptsInput(def.Id, it)
                    && it != null && ChemRecipes.PhaseOf(it.identifier) == Phase.Gas;
            if (filterSlot != null)
                filterSlot.mayInventoryAddItemFunc = (it, _) =>
                    it != null && GeneralHelper.IsItemOwned(it) && it.identifier.StartsWith("filter_paper");
            if (containerSlot != null)
                containerSlot.mayInventoryAddItemFunc = (it, _) =>
                    it != null && GeneralHelper.IsItemOwned(it) &&
                    (it.identifier == "gas_canister_empty" || it.identifier == ChemRecipes.DrumEmptyId);
            foreach (var slot in gasSlots)
            {
                slot.mayInventoryAddItemFunc = (it, _) => AcceptsGasCanister(it);
                slot.onSlotAddItemEndFunc = (added, _, __) =>
                {
                    // 气瓶天然无液体杂质（防御旧存档脏数据）
                    ChemCatalog.ClearCanisterContaminants(added);
                    string gasId;
                    if (ChemRecipes.TryGetGasFromCanister(added.identifier, out gasId)
                        && ChemCatalog.IsCanisterItem(added))
                        ChemRuntime.Log("[gas] gas slot: " + added.identifier + " → " + gasId
                                        + " / purity " + ChemCatalog.CanisterPurity(added) + "%");
                };
            }
            output.mayInventoryAddItemFunc = (it, _) =>
                it.IsTag(ChemRecipes.ReagentTag) || it.identifier == ChemRecipes.SludgeId;
            note.mayInventoryAddItemFunc = (it, _) =>
                it != null && (it.identifier.Equals("postit") || it.identifier.Equals("handnote")
                               || it.identifier.Equals("slip"));
            note.SetBackgroundFadeSprite("Items/items_tool", "postit");
            WriteNoteHandnote(note, def.Id);

            GameItem machine = ItemDirectory.CreateEmptyItem(def.Id)
                .SetContentWindow(window)
                .SetSpriteAndShapeFromMod(ChemCatalog.ModId, def.Id);

            machine.name = ModHelper.GetLocalized(ChemCatalog.ModId, def.Id + "_name");
            machine.shortDescription = ModHelper.GetLocalized(ChemCatalog.ModId, def.Id + "_desc");
            machine.flavorText = ModHelper.GetLocalized(ChemCatalog.ModId, def.Id + "_flavor");
            machine.unitValue = 300 + def.Power * 20;
            machine.SetGameItemType(TypeHelper.MACHINE);
            machine.EnableTag(ContainerHelper.CONTAINER_TAG);
            machine.EnableTag("manufacture");
            machine.EnableTag(MachineHelper.STANDARD_MACHINE_TAG);
            machine.EnableTag("fc_machine_" + def.Id);

            if (battery != null) MachineHelper.SetupBatterySlot(battery, machine);
            MachineryHelper.InitMachinery(machine, layout.Powered ? Mathf.Max(1, def.Power) : 0);
            MachineProgressHelper.InitProgressTypeMachine(machine, def.Speed, useDefaultTooltip: false);
            machine.ModifyTag(MachineHelper.MACHINE_PERFORMANCE_RATIO_INT, s => s.Enable().SetInt(1));

            if (modules != null)
            {
                string[] allowed = { ModuleHelper.MODULE_TYPE_UNIVERSAL };
                ContainerHelper.AllowOnlyTaggedItemsOr(modules, allowed, true);

                modules.onSlotAddItemEndFunc = (added, parent, slot) =>
                {
                    ModuleEffectHelper.OnAddedModuleToGrid(added, machine, modules.childItems);
                    if (added.IsTag(ModuleHelper.MODULE_TAG))
                    {
                        ModuleHelper.ComputeModuleEffect(modules, added, machine);
                        ModuleHelper.ApplyBasicModuleEffect(modules, added, machine);
                    }
                    MachineryHelper.UpdatePowerUsageProcessTypeMachine(machine);
                };
                modules.onSlotRemoveItemEndFunc = (removed, parent, slot) =>
                {
                    ModuleEffectHelper.OnRemovedModuleFromGrid(removed, machine, modules.childItems);
                    if (removed.IsTag(ModuleHelper.MODULE_TAG))
                    {
                        ModuleHelper.ComputeModuleEffect(modules, removed, machine);
                        ModuleHelper.ApplyBasicModuleEffect(modules, removed, machine);
                    }
                    MachineryHelper.UpdatePowerUsageProcessTypeMachine(machine);
                };
            }

            machine.onCycleEndSlotItemFunc = (item, parent, slot) =>
            {
                // 异常不得让机器永久卡在 _on：无论周期逻辑成功/失败，finally 都按真实状态收敛图标。
                try
                {
                    RunMachineCycle(item, def, machine, battery, modules, inputs, gasSlots, output);
                }
                catch (Exception e)
                {
                    ChemRuntime.Log("machine cycle failed (" + def.Id + "): " + e.Message);
                }
                finally
                {
                    // 双状态图标：按周期结束后的真实状态收敛（helper 内部只在状态变化时真正换图）
                    SetMachineVisual(machine, def.Id,
                        MachineProgressHelper.GetMachineState(item)
                            .Equals(MachineProgressHelper.STATE_WORKING));
                }
            };

            return machine;
        }

        /// <summary>
        /// 一台夜间进度机的一次周期回调。从 onCycleEndSlotItemFunc 抽出来，
        /// 便于在末尾统一收敛双状态图标（含提前 return 的路径）。
        /// </summary>
        static void RunMachineCycle(GameItem item, ChemMachineDef def, GameItem machine,
            GameSlotInventory battery, GameGridInventory modules,
            List<GameSlotInventory> inputs, List<GameSlotInventory> gasSlots, GameGridInventory output)
        {
            // P4a：无电池面板的机器（滗析/过滤/滴定/加热台）不检查也不扣电
            bool powered = battery != null;
            int energy = powered ? MachineryHelper.GetMachinePowerUsage(item) : 0;
            if (powered && !MachineHelper.CanPower(item, battery)) return;

            var ids = new List<string>();
            bool catalyst = false;
            int inputGrade = 0;
            foreach (var s in inputs)
            {
                var child = s.childItem;
                if (child == null) continue;
                string cid = child.identifier;
                if (cid == "catalyst_kit") { catalyst = true; continue; }  // 催化剂只作条件，不算投料
                if (cid == "precision_balance" || cid == "ph_meter"
                    || cid == "safety_goggles" || cid == "thermometer") continue;
                // 纯度链：取投料中最高的纯度档，作为产物纯度与灌装 SKU 的依据
                int g = ChemCatalog.ReadInt(child, ChemCatalog.TGrade, 0);
                if (g > inputGrade) inputGrade = g;
                // 空钢瓶是灌装台的必要容器（历史行为：不进反应匹配，但保留在 ids 里）
                if (cid == "gas_canister_empty")
                {
                    if (def.Id == "canister_filling") ids.Add(cid);
                    continue;
                }
                // 大型空桶只在灌装台作为目标容器；投到别的设备不算投料（避免误销毁/误产废渣）
                if (cid == ChemRecipes.DrumEmptyId)
                {
                    if (def.Id == "canister_filling") ids.Add(cid);
                    continue;
                }
                // P3a：滤纸不是 ReagentTag（避免被当成药剂），但过滤台 resolver 需要它才能匹配，
                // 因此在过滤台把滤纸 id 放进 ids（其他设备不接受滤纸）。
                if (cid.StartsWith("filter_paper"))
                {
                    if (def.Id == "filtration_bench") ids.Add(cid);
                    continue;
                }
                // P10b/P10c：实验容器 / 试管架 → 解析内容药剂 id 参与反应匹配（容器本身不销毁）。
                // 容器内容 tag 为 fc_content_items（"reagentId:ml;" 列表，由 ChemCatalog.GetContainerContentIds 解析）。
                if (ChemCatalog.IsVessel(cid))
                {
                    foreach (var contentId in ChemCatalog.GetContainerContentIds(child))
                        ids.Add(contentId);
                    continue;
                }
                if (cid == ChemCatalog.TestTubeRackId)
                {
                    var tubes = child.FindAllChildItems();
                    if (tubes != null)
                        foreach (var tube in tubes)
                        {
                            if (tube == null || tube.identifier != "test_tube") continue;
                            foreach (var contentId in ChemCatalog.GetContainerContentIds(tube))
                                ids.Add(contentId);
                        }
                    continue;
                }
                // 本模组真药剂：直接进投料集合
                if (child.IsTag(ChemRecipes.ReagentTag)) { ids.Add(cid); continue; }
                // 原版药剂瓶：按 VanillaAnalog 映射成本模组药剂 id 参与反应匹配
                // （问题 5：兼容原版药剂；Finish 会销毁原瓶）
                string mapped;
                if (ChemRecipes.VanillaAnalog.TryGetValue(cid, out mapped)) ids.Add(mapped);
            }

            // ── P3c：气瓶槽供气（气瓶商品 → 内部气体 id 参与反应匹配）──
            var gasSlotsUsed = CollectGasSupply(def, ids, gasSlots, catalyst, ref inputGrade);

            string state = MachineProgressHelper.GetMachineState(item);
            if (state.Equals(MachineProgressHelper.STATE_READY))
            {
                if (ids.Count == 0) return;
                int emptyCanisters = GasCanistersConsumed(def, ids, gasSlotsUsed, catalyst);
                if (!CanAcceptOutputs(output, def, ids, catalyst, inputGrade, emptyCanisters)) return;

                if (powered && battery.childItem != null) PowerHelper.DrawPowerSource(battery.childItem, energy);
                item.ModifyTag(MachineProgressHelper.MACHINE_STATE_TAG,
                    s => s.Enable().SetString(MachineProgressHelper.STATE_WORKING));
                item.ModifyTag(MachineProgressHelper.MACHINE_PROGRESS_CURRENT_TAG, s => s.Enable().SetInt(0));
                item.ModifyTag(MachineProgressHelper.MACHINE_PROGRESS_TARGET_TAG, s => s.Enable().SetInt(def.TargetProgress));
                MachineProgressHelper.ContinueProgressTypeMachine(item);
                if (modules != null) MachineHelper.OnMachineActioned(machine, modules);
                if (MachineProgressHelper.IsProgressTypeMachineFinished(item))
                    Finish(item, def, inputs, gasSlots, output, ids, catalyst, inputGrade);
            }
            else if (state.Equals(MachineProgressHelper.STATE_WORKING))
            {
                if (powered && battery.childItem != null) PowerHelper.DrawPowerSource(battery.childItem, energy);
                MachineProgressHelper.ContinueProgressTypeMachine(item);
                if (modules != null) MachineHelper.OnMachineActioned(machine, modules);
                if (MachineProgressHelper.IsProgressTypeMachineFinished(item))
                {
                    if (ids.Count == 0) { MachineProgressHelper.FinishProgressTypeMachine(item); return; }
                    // P6a 仪器自检修复：输出满时不得 Finish（否则产物被 TryAcceptAll 丢弃/机器卡住）
                    int emptyCans = GasCanistersConsumed(def, ids, gasSlotsUsed, catalyst);
                    if (!CanAcceptOutputs(output, def, ids, catalyst, inputGrade, emptyCans))
                    {
                        MachineProgressHelper.FinishProgressTypeMachine(item);
                        ChemRuntime.Log("[machine] " + def.Id + " output full; cycle aborted, inputs kept.");
                        return;
                    }
                    Finish(item, def, inputs, gasSlots, output, ids, catalyst, inputGrade);
                }
            }
        }

        /// <summary>哪些机器支持气瓶槽供气（灌装台自身的气瓶会被当成气体源，排除）。</summary>
        static bool SupportsGasSupply(string machineId) => machineId != "canister_filling";

        /// <summary>
        /// P3c：把气瓶槽里的气瓶映射成气体 id 加入投料集合。
        /// 只有「加了该气体后能匹配到当前机器配方」的气体才参与（兼容双气配方：先试全部，再逐个试），
        /// 把气瓶放在槽里不会破坏原本不消耗气体的配方。气瓶纯度（fc_purity）会抬高 inputGrade。
        /// </summary>
        static List<GameSlotInventory> CollectGasSupply(ChemMachineDef def, List<string> ids,
            List<GameSlotInventory> gasSlots, bool catalyst, ref int inputGrade)
        {
            var used = new List<GameSlotInventory>();
            if (gasSlots == null || !SupportsGasSupply(def.Id)) return used;

            var candidates = new List<GameSlotInventory>();
            var candidateGas = new List<string>();
            foreach (var gs in gasSlots)
            {
                var can = gs.childItem;
                if (can == null) continue;
                string gasId;
                if (!ChemRecipes.TryGetGasFromCanister(can.identifier, out gasId)) continue;
                ChemCatalog.ClearCanisterContaminants(can);
                candidates.Add(gs);
                candidateGas.Add(gasId);
            }
            if (candidates.Count == 0) return used;

            // 先尝试全部气体一起匹配（支持双气配方）
            var all = new List<string>(ids);
            all.AddRange(candidateGas);
            if (ChemRecipes.Match(all, def.Id, ChemRuntime.Temp, ChemRuntime.Ph, catalyst) != null)
            {
                ids.AddRange(candidateGas);
                used.AddRange(candidates);
            }
            else
            {
                // 再逐个尝试（单气配方）
                for (int i = 0; i < candidates.Count; i++)
                {
                    var probe = new List<string>(ids);
                    probe.Add(candidateGas[i]);
                    if (ChemRecipes.Match(probe, def.Id, ChemRuntime.Temp, ChemRuntime.Ph, catalyst) == null)
                        continue;
                    ids.Add(candidateGas[i]);
                    used.Add(candidates[i]);
                }
            }

            // 气瓶纯度影响输入纯度档（进而影响产出气体/药剂的纯度）
            foreach (var gs in used)
            {
                int canGrade = ChemCatalog.CanisterGrade(gs.childItem);
                if (canGrade > inputGrade) inputGrade = canGrade;
                ChemRuntime.Log("[gas] gas slot supplies gas (purity "
                                + ChemCatalog.CanisterPurity(gs.childItem) + "%, grade " + canGrade + ")");
            }
            return used;
        }

        /// <summary>本次匹配的反应会消耗几个气瓶（只有配方 inputs 里出现该气体才消耗）。</summary>
        static int GasCanistersConsumed(ChemMachineDef def, List<string> ids,
            List<GameSlotInventory> gasSlotsUsed, bool catalyst)
        {
            var r = ChemRecipes.Match(ids, def.Id, ChemRuntime.Temp, ChemRuntime.Ph, catalyst);
            if (r == null) return 0;
            int n = 0;
            foreach (var gs in gasSlotsUsed)
            {
                var can = gs.childItem;
                if (can == null) continue;
                string gasId;
                if (!ChemRecipes.TryGetGasFromCanister(can.identifier, out gasId)) continue;
                foreach (var inp in r.Inputs)
                    if (inp == gasId) { n++; break; }
            }
            return n;
        }

        /// <summary>
        /// 机器「空闲 / 工作」双状态图标的唯一入口（13 台设备全部只走这里）。
        ///
        /// 依据 docs/api-findings.md（api-researcher 反编译结论）+ 本机探针：
        /// · 官方公开 API `GameItem.SetSpriteAndShapeFromMod(modId, spriteKey)` 可双向重复调用；
        ///   裸 key（如 "gas_scrubber_on"），内部会 set spriteAtlasPath=modId / spritePath=key /
        ///   useSpriteFromMod=true / spriteChanged=true，并按 gridSize 采样 alpha 重建 GridShape；
        ///   mod 或 key 不存在时只 Debug.LogWarning 并原样 return（不抛异常、不改状态）。
        /// · 13 对 <id>/<id>_on 图标占格完全一致（按 16px 格子中心 alpha 采样 13/13 SAME），
        ///   所以重复换图不会改变背包占格。
        /// · `SetSprite(atlas,sprite)` / `SetSpriteQuick` 不会置 useSpriteFromMod、不校验 key，
        ///   `SetSpriteAndShape(atlas,sprite)` 走原版资源图集，两者都不用于 mod 贴图。
        /// 仅在目标状态变化时调用，避免无谓重建 shape。
        /// </summary>
        public static void SetMachineVisual(GameItem machine, string id, bool on)
        {
            if (machine == null || string.IsNullOrEmpty(id)) return;
            string want = on ? id + "_on" : id;
            try
            {
                if (machine.useSpriteFromMod && machine.spritePath == want) return;  // 已是目标状态
                machine.SetSpriteAndShapeFromMod(ChemCatalog.ModId, want);
            }
            catch (Exception e)
            {
                // 换图失败绝不能影响玩法：只记日志，保持原图标
                ChemRuntime.Log("machine visual switch failed (" + want + "): " + e.Message);
            }
        }

        static bool CanAcceptOutputs(GameGridInventory output, ChemMachineDef def,
                                     List<string> ids, bool catalyst, int inputGrade,
                                     int emptyCanistersNeeded)
        {
            var probe = def.Resolve(ids, ChemRuntime.Temp, ChemRuntime.Ph, catalyst, inputGrade);
            if (probe.Count == 0) return false;

            // P7a：累计占格判定 —— 用同尺寸临时网格逐件真实 TryAcceptAll，
            // 避免「逐件 CanAccept 通过、合起来放不下」导致的产物丢失。
            MachineLayout layout = LayoutOf(def.Id);
            var scratch = new GameGridInventory(layout.OutputW, layout.OutputH);
            try
            {
                foreach (var id in probe)
                {
                    GameItem test = DirectoryMaster.Item(id);
                    if (test == null) continue;
                    if (!GraphUtils.CanAccept(scratch, test)) { test.Destroy(); return false; }
                    GraphUtils.TryAcceptAll(scratch, test);
                }
                for (int i = 0; i < emptyCanistersNeeded; i++)
                {
                    GameItem testEmpty = DirectoryMaster.Item("gas_canister_empty");
                    if (testEmpty == null) continue;
                    if (!GraphUtils.CanAccept(scratch, testEmpty)) { testEmpty.Destroy(); return false; }
                    GraphUtils.TryAcceptAll(scratch, testEmpty);
                }
                return true;
            }
            finally
            {
                // 清理临时网格里的探测物品
                if (scratch.childItems != null)
                    foreach (var it in scratch.childItems.ToArray())
                        if (it != null) it.Destroy();
            }
        }

        static void Finish(GameItem machine, ChemMachineDef def, List<GameSlotInventory> inputs,
                           List<GameSlotInventory> gasSlots, GameGridInventory output,
                           List<string> ids, bool catalyst, int inputGrade)
        {
            MachineProgressHelper.FinishProgressTypeMachine(machine);

            // 记录首次发现 / 事故（副作用只发生一次）
            var r = ChemRecipes.Match(ids, def.Id, ChemRuntime.Temp, ChemRuntime.Ph, catalyst);
            bool extrasMatched = false;
            if (r == null)
            {
                r = ChemRecipes.MatchIgnoringExtras(ids, def.Id, ChemRuntime.Temp, ChemRuntime.Ph, catalyst);
                extrasMatched = r != null;
                if (extrasMatched)
                    ChemRuntime.Log("[machine] " + def.Id + ": extra inputs tolerated, only recipe inputs consumed.");
            }
            if (r != null)
            {
                if (ChemRuntime.IsAccident(r.Hazard)) ChemRuntime.NoteAccident(r.Id);
                else if (ChemRuntime.Discover(r.Id)) ChemRuntime.Log("New reaction: " + r.Name);
            }

            // ★ 先读投料状态（污染物 / 滤纸耐久 / 提纯等级），再销毁——销毁后 tag 就读不到了
            int[] sourceContam = null;
            string paperId = null;
            int sourcePurifyLevel = 0;
            int sourcePurity = 0;   // 玩家审查：取投入最高纯度，产物至少继承它
            foreach (var s in inputs)
            {
                if (s.childItem == null) continue;
                string cid = s.childItem.identifier;
                if (cid.StartsWith("liq_") && sourceContam == null)
                {
                    sourceContam = ChemCatalog.ReadContaminants(s.childItem);
                    sourcePurifyLevel = ChemCatalog.ReadPurifyLevel(s.childItem);   // P1c
                }
                int p = ChemCatalog.ReadInt(s.childItem, ChemCatalog.TPurity, 0);
                if (p > sourcePurity) sourcePurity = p;
                if (cid.StartsWith("filter_paper")) paperId = cid;
            }

            foreach (var s in inputs)
            {
                if (s.childItem == null) continue;
                string cid = s.childItem.identifier;
                // P10b/P10c：实验容器保留，只清空内容；试管架清空其中的试管
                if (ChemCatalog.IsVessel(cid))
                {
                    ChemCatalog.ClearContainerContent(s.childItem);
                    continue;
                }
                if (cid == ChemCatalog.TestTubeRackId)
                {
                    var tubes = s.childItem.FindAllChildItems();
                    if (tubes != null)
                        foreach (var tube in tubes)
                            if (tube != null && tube.identifier == "test_tube")
                                ChemCatalog.ClearContainerContent(tube);
                    continue;
                }
                if ((cid == "gas_canister_empty" || cid == ChemRecipes.DrumEmptyId)
                    && def.Id == "canister_filling")
                { s.childItem.Destroy(); continue; }   // 仅灌装台会消耗目标空容器
                if (cid.StartsWith("filter_paper"))
                {
                    // 滤纸按耐久消耗：还有次数就留在槽里，用完才销毁
                    int dur = ChemCatalog.ReadInt(s.childItem, ChemCatalog.TFilterDur, 0) - 1;
                    if (dur > 0)
                    {
                        s.childItem.ModifyTag(ChemCatalog.TFilterDur, t => t.Enable().SetInt(dur));
                        ChemRuntime.Log("[filter] " + cid + " used, durability left " + dur + ".");
                        continue;
                    }
                    ChemRuntime.Log("[filter] " + cid + " consumed (durability 0) — insert a new filter paper.");
                    s.childItem.Destroy(); continue;
                }
                if (ChemRecipes.IsAuxiliary(cid)) continue;
                // P7c：容忍多余投料时，只销毁配方内的物品，多余同相物品留在槽里
                if (extrasMatched && r != null)
                {
                    string norm = ChemRecipes.Normalize(cid);
                    bool needed = false;
                    foreach (var need in r.Inputs)
                        if (ChemRecipes.Normalize(need) == norm) { needed = true; break; }
                    if (!needed) continue;
                }
                // 其余投料全部销毁——包括本模组药剂与原版药剂瓶（VanillaAnalog 映射过的原版瓶，
                // 问题 5 的兼容路径），确保原版药剂被消耗而不是留在槽里反复利用。
                s.childItem.Destroy();
            }

            var outputs = def.Resolve(ids, ChemRuntime.Temp, ChemRuntime.Ph, catalyst, inputGrade);

            foreach (var id in outputs)
            {
                // P3b：产出物必须 owned，否则无法参与原版「倒液到容器」等交互
                GameItem produced = ChemCatalog.EnsureOwned(DirectoryMaster.Item(id));

                if (def.Id == "decanter" && id.StartsWith("liq_") && sourceContam != null)
                {
                    // 滗析只沉降大颗粒，游离的化学污染物/重金属完全保留 → 纯度只能提高，不能提纯
                    // P1c：滗析不改变提纯等级（保留原等级及其纯度上限）
                    ChemCatalog.ApplyContaminants(produced, ChemLab.Decant(sourceContam), sourcePurifyLevel);
                    ChemCatalog.SyncVanillaPurity(produced);
                }
                else if (def.Id == "filtration_bench" && paperId != null && !id.StartsWith("liq_"))
                {
                    // P7c：固体洗涤/重结晶：纯度 +5（可逐次洗到可校准档）
                    ChemCatalog.RaisePurityTo(produced, sourcePurity + 5);
                    ChemRuntime.Log("[filter] solid washed: " + id + " purity → "
                                    + ChemRuntime.PurityOf(produced).ToString("0.#") + "%");
                }
                else if (def.Id == "filtration_bench" && id.StartsWith("liq_")
                         && sourceContam != null && paperId != null)
                {
                    // P1c：每完成一次过滤，提纯等级 +1（cap 5）→ 残留下限减半，纯度上限提高
                    int newLevel = Math.Min(5, sourcePurifyLevel + 1);
                    ChemCatalog.ApplyContaminants(produced,
                        ChemLab.Filter(sourceContam, paperId, newLevel), newLevel);
                    ChemCatalog.SyncVanillaPurity(produced);
                    ChemRuntime.Log("[filter] purify level " + sourcePurifyLevel + " -> " + newLevel
                                    + " (purity ceiling " + (ChemLab.MaxPurity(newLevel) / 100f) + "%)");
                }
                else if (def.Id == "gas_scrubber" && id.StartsWith("gas_"))
                {
                    // 气体洗涤：清洁气体不携带液体污染物，按档位提纯
                    int upgraded = Mathf.Clamp(inputGrade + 1, 0, 2);
                    ChemCatalog.UpgradePurity(produced, (PurityGrade)upgraded);
                    ChemCatalog.SyncVanillaPurity(produced);
                }
                else if (id.StartsWith("can_"))
                {
                    // P1b1：固定产出工业钢瓶（单 SKU）；纯度档由投入气体档位决定，
                    // 写入 fc_purity/fc_grade 后再同步到原版七档 feature（价格由 ItemFeature 修正）。
                    ChemCatalog.UpgradePurity(produced, (PurityGrade)Mathf.Clamp(inputGrade, 0, 2));
                    ChemCatalog.SyncVanillaPurity(produced);
                }
                else if (id.StartsWith("gas_drum_"))
                {
                    // 大型桶同样单 SKU；纯度按投入气体档位写 tag/feature（只有一张工业桶图标）
                    ChemCatalog.UpgradePurity(produced, (PurityGrade)Mathf.Clamp(inputGrade, 0, 2));
                    ChemCatalog.SyncVanillaPurity(produced);
                }

                // 玩家审查：合成/冷凝/电解等产物纯度不低于投入最高纯度
                // （滗析/过滤已有各自提纯逻辑；钢瓶/桶由 inputGrade 决定 SKU/档位）
                if (sourcePurity > 0 && def.Id != "decanter" && def.Id != "filtration_bench")
                    ChemCatalog.RaisePurityTo(produced, sourcePurity);

                // P7a：产物放不下时改投后柜台并日志，绝不静默销毁
                if (!GraphUtils.CanAccept(output, produced))
                {
                    try
                    {
                        ModHelper.MoveItemToBackCounter(produced);
                        ChemRuntime.Log("[machine] output full → produced item moved to back counter: " + id);
                    }
                    catch (Exception moveEx)
                    {
                        GraphUtils.TryAcceptAllOrDestroy(output, produced, "free_chem.finish");
                        ChemRuntime.Log("[machine] output full and back counter failed (" + id + "): " + moveEx.Message);
                    }
                }
                else
                {
                    GraphUtils.TryAcceptAll(output, produced);
                }

                // 有毒气体排放 → 排气嫌疑度（CO2/O2/N2/H2 不计；ChemRuntime 再折算写入原版 ENDANGERMENT）
                NoteExhaust(id);
            }

            // P3c：反应完成后消耗气瓶并回收空瓶（守恒；输出满则保留气瓶并日志）
            ConsumeGasCanisters(def, ids, gasSlots, catalyst, output);
        }

        /// <summary>
        /// P3c：气瓶消耗与空瓶回收。只有匹配反应 inputs 里出现过该气体才消耗该气瓶；
        /// 输出放不下空瓶时保留气瓶（不销毁），绝不丢失/凭空复制。
        /// </summary>
        static void ConsumeGasCanisters(ChemMachineDef def, List<string> ids,
            List<GameSlotInventory> gasSlots, bool catalyst, GameGridInventory output)
        {
            if (gasSlots == null) return;
            var r = ChemRecipes.Match(ids, def.Id, ChemRuntime.Temp, ChemRuntime.Ph, catalyst);
            if (r == null) return;

            foreach (var gs in gasSlots)
            {
                var can = gs.childItem;
                if (can == null) continue;
                string canisterId = can.identifier;
                string gasId;
                if (!ChemRecipes.TryGetGasFromCanister(canisterId, out gasId)) continue;

                bool consumed = false;
                foreach (var inp in r.Inputs)
                    if (inp == gasId) { consumed = true; break; }
                if (!consumed) continue;   // 反应不消耗该气体 → 气瓶保留

                GameItem empty = DirectoryMaster.Item("gas_canister_empty");
                if (empty == null) { ChemRuntime.Log("[gas] empty canister item missing; canister kept."); continue; }
                ChemCatalog.EnsureOwned(empty);
                if (!GraphUtils.CanAccept(output, empty))
                {
                    empty.Destroy();
                    ChemRuntime.Log("[gas] output full: canister " + canisterId
                                    + " NOT consumed (conservation kept).");
                    continue;
                }
                can.Destroy();
                GraphUtils.TryAcceptAll(output, empty);
                ChemRuntime.Log("[gas] consumed " + canisterId + " → " + gasId + ", returned gas_canister_empty.");
            }
        }

        /// <summary>
        /// 有毒气体排放计数：先累加模组内排气嫌疑度（按局存档），
        /// 再由 ChemRuntime 按排风服务倍率折算写入原版治安部 ENDANGERMENT（默认开启、可配置关闭）。
        /// 只对 gas_chlorine / gas_hydrogen_chloride / gas_ammonia 计数，单位见 ChemLab.ExhaustUnitsForGas。
        /// </summary>
        static void NoteExhaust(string producedId)
        {
            int units = ChemLab.ExhaustUnitsForGas(producedId);
            if (units <= 0) return;
            ChemRuntime.AddExhaust(units);
        }

        /// <summary>先试新键，缺键时回退旧键（避免玩家看到裸键名）。</summary>
        static string UiOr(string key, string fallbackKey, params object[] fallbackArgs)
        {
            string s = ModHelper.GetLocalized(ChemCatalog.ModId, key);
            if (!string.IsNullOrEmpty(s) && s != key) return s;
            return ModHelper.GetLocalized(ChemCatalog.ModId, fallbackKey, fallbackArgs);
        }

        /// <summary>窗口/标签本地化快捷方法（ui_* 键，问题 5c）。</summary>
        static string Ui(string key, params object[] args)
            => ModHelper.GetLocalized(ChemCatalog.ModId, key, args);

        static PixelWindow BuildWindow(ChemMachineDef def, MachineLayout layout,
            GameSlotInventory battery, GameGridInventory modules, List<GameSlotInventory> inputs,
            List<GameSlotInventory> gasSlots, GameGridInventory output, GameSlotInventory note)
        {
            // 官方机器窗口构建器：面板顺序 = 存档顺序（本模组新发布，可自由定）。
            var panels = new List<MachineryPanel>();
            if (battery != null)
            {
                // 官方 PowerPanel 自带电池图标/角色；若该 API 在本机不可用，退回带本地化标签的普通面板
                try { panels.Add(MachineHelper.PowerPanel(battery)); }
                catch (Exception) { panels.Add(MachineHelper.Panel(Ui("ui_battery"), battery, "battery")); }
            }
            if (modules != null) panels.Add(MachineHelper.Panel(Ui("ui_modules"), modules, "module"));

            // 输入面板：投料/滤纸/空瓶/气瓶槽按顺序排成一行，标签行 + 槽位行（无多余空格）
            var inGrid = new GridPixelElement(inputs.Count + gasSlots.Count, 2, true);
            int col = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                string label;
                if (layout.FilterSlot && i == inputs.Count - 1)
                    label = UiOr("ui_filter_slot", "ui_input", i + 1);
                else if (layout.ContainerSlot && i == inputs.Count - 1)
                    label = UiOr("ui_container_slot", "ui_input", i + 1);
                else
                    label = Ui("ui_input", i + 1);
                inGrid.Attach(new TagElement().SetText(label), col, 0, 20, 20);
                inGrid.Attach(inputs[i], col, 1, 50, 20);
                col++;
            }
            for (int i = 0; i < gasSlots.Count; i++)
            {
                // 两个气瓶槽使用不同标签（1/2）；缺键时回退 ui_gas_slot
                string gasLabel = UiOr("ui_gas_slot_" + (i + 1), "ui_gas_slot");
                inGrid.Attach(new TagElement().SetText(gasLabel), col, 0, 20, 20);
                inGrid.Attach(gasSlots[i], col, 1, 50, 20);
                col++;
            }
            panels.Add(MachineHelper.Panel(Ui("ui_input_title"), inGrid, "input"));

            panels.Add(MachineHelper.Panel(Ui("ui_output"), output, "output"));
            // 官方 NotePanel 自带便签图标/角色；异常时退回带 ui_note 标签的普通面板
            try { panels.Add(MachineHelper.NotePanel(note)); }
            catch (Exception) { panels.Add(MachineHelper.Panel(Ui("ui_note"), note, "note")); }

            return MachineHelper.CreateMachineryWindow(true, panels.ToArray());
        }
    }
}
