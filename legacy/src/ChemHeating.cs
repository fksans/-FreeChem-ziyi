using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FreeChem
{
    /// <summary>
    /// 大型加热台：实时每秒驱动（参考原版加热板的加热回调）。
    ///
    /// 槽位：反应容器（烧杯/鹅颈瓶…）+ 加热器（酒精灯/喷灯）+ 灯芯 + 燃料（原版酒精）+ 输出。
    ///  · 酒精灯必须装灯芯、灌燃料才能点火；灯芯有耐久，燃料按秒消耗
    ///  · 燃料纯度决定加热因子与消耗速率（低纯度酒精火力弱且更费）
    ///  · 升温沿用原版公式 min(目标, T+(目标−T)×因子/350+1)；冷却沿用 26+(T−26)×exp(...)
    ///  · 温度落入配方的温区后实时累积秒数，达到时长即产出；超温降解为废渣
    ///
    /// 实时用 ModHelper.StartCoroutine + WaitForSeconds，绝不用 Thread/Task（会被扫描器拒绝）。
    /// </summary>
    public static class ChemHeating
    {
        // 每台加热台的运行时会话（不进存档；温度/燃料存在物品 tag 里，重载后可恢复）
        sealed class Rig
        {
            public GameItem Machine;
            public GameGridInventory VesselSlot;   // P3f 审计修复：反应槽改为 2×2 网格（可同时放多种投料/容器）
            public GameSlotInventory HeaterSlot, WickSlot, FuelSlot;
            public List<GameSlotInventory> GasSlots;   // P3c：气瓶槽（最多 2 个）
            public GameGridInventory Output;
            public float Temp = ChemLab.RoomTemp;
            public int HeldSeconds = 0;
            public string ActiveReaction = null;
            public bool High = false;
        }

        static readonly List<Rig> Rigs = new List<Rig>();
        static Coroutine ticker;
        static ModLog log;

        public static void Bind(ModLog modLog) => log = modLog;

        /// <summary>由 ModEntry.OnEnable 启动全局每秒协程。</summary>
        public static void Start()
        {
            if (ticker != null) return;
            ticker = ModHelper.StartCoroutine(ChemCatalog.ModId, TickLoop());
            if (log != null) log.Log("[FreeChem] realtime heating ticker started (1s).");
        }

        /// <summary>由 ModEntry.OnDisable 停止；加载器停用模组时也会一并取消。</summary>
        public static void Stop()
        {
            // 先把加热台图标收敛回空闲态，避免模组停用后残留 _on 图标
            foreach (var r in Rigs) ChemMachines.SetMachineVisual(r.Machine, "heating_platform", false);
            if (ticker != null) { ModHelper.StopCoroutine(ChemCatalog.ModId, ticker); ticker = null; }
            Rigs.Clear();
        }

        static IEnumerator TickLoop()
        {
            var wait = new WaitForSeconds(1f);
            while (true)
            {
                yield return wait;
                try
                {
                    // NEW-P1-4：清理已销毁/不属于本局的 Rig，避免残留对象被反复 tick
                    foreach (var rig in Rigs.ToArray())
                    {
                        if (rig.Machine == null || !GeneralHelper.IsItemOwned(rig.Machine))
                        {
                            Rigs.Remove(rig);
                            continue;
                        }
                        Tick(rig);
                    }
                }
                catch (Exception e) { if (log != null) log.Warning("[FreeChem] heating tick: " + e.Message); }
            }
        }

        /// <summary>由 ChemMachines.Create 在构建加热台时注册。</summary>
        public static void Register(GameItem machine, GameGridInventory vessel, GameSlotInventory heater,
                                    GameSlotInventory wick, GameSlotInventory fuel,
                                    List<GameSlotInventory> gasSlots, GameGridInventory output)
        {
            int savedTemp = ChemCatalog.ReadInt(machine, ChemCatalog.TTemp,
                                                Mathf.RoundToInt(ChemLab.RoomTemp * 100));
            var r = new Rig
            {
                Machine = machine, VesselSlot = vessel, HeaterSlot = heater,
                WickSlot = wick, FuelSlot = fuel, GasSlots = gasSlots, Output = output,
                // NEW-P1-4：读档后从机器 tag 恢复温度，避免归零回 26°C
                Temp = Mathf.Clamp(savedTemp / 100f, ChemLab.RoomTemp, 300f),
            };
            Rigs.Add(r);
        }

        /// <summary>单台每秒 tick —— 纯逻辑部分可独立推演，见 LogicHarness。</summary>
        static void Tick(Rig r)
        {
            var vesselItems = r.VesselSlot != null ? r.VesselSlot.childItems : null;
            bool hasVesselItems = vesselItems != null && vesselItems.Count > 0;
            var heater = r.HeaterSlot.childItem;
            var wick   = r.WickSlot.childItem;
            var fuel   = r.FuelSlot.childItem;

            // 燃料槽里的酒精自动灌进酒精灯（纯度决定灌入量），灌完燃料物品销毁
            if (heater != null && fuel != null &&
                ChemCatalog.ReadInt(heater, ChemCatalog.TFuel, 0) < 20)
                Refill(heater, fuel);

            bool lit = hasVesselItems && heater != null && wick != null
                       && ChemCatalog.ReadInt(heater, ChemCatalog.TFuel, 0) > 0
                       && ChemCatalog.ReadInt(wick, ChemCatalog.TWickDur, 0) > 0;

            // 加热 / 冷却
            float target = ChemLab.TargetTemp(0, r.High);
            if (lit)
            {
                // P3a：燃料瓶在 Refill 时已被销毁，这里从灯自身读「燃料纯度」tag，
                // 绝不能再用 FuelPurity(燃料槽物品)（旧实现燃料耗尽后 fuel==null 会 NRE，导致加热台停摆）。
                int purity = ChemCatalog.ReadInt(heater, ChemCatalog.TFuelPurity,
                                                ChemLab.VanillaAlcoholPurity);
                float factor = 10f * ChemLab.HeatFactor(purity);
                r.Temp = ChemLab.HeatingStep(r.Temp, target, factor);
                ConsumeFuel(r, purity);
            }
            else
            {
                // 冷却参考液量：500 ml 标准瓶（P3a：液体瓶容量 500ml）
                r.Temp = ChemLab.CoolingStep(r.Temp, ChemRecipes.PourVolumeMl * 1000f);
            }
            if (vesselItems != null)
                foreach (var vi in vesselItems)
                    if (vi != null)
                        vi.ModifyTag(ChemCatalog.TTemp, s => s.Enable().SetInt(Mathf.RoundToInt(r.Temp * 100)));
            // NEW-P1-4：机器自身也保存温度，读档时 Register 可恢复
            if (r.Machine != null)
                r.Machine.ModifyTag(ChemCatalog.TTemp, s => s.Enable().SetInt(Mathf.RoundToInt(r.Temp * 100)));

            // 双状态图标：有燃料在烧且已升温（或正在反应）→ <id>_on；熄火/回温 → 空闲态。
            // 走 ChemMachines.SetMachineVisual 统一入口，内部只在状态变化时真正换图。
            bool workingVisual = lit
                && (r.Temp > ChemLab.RoomTemp + 1f || r.ActiveReaction != null);
            ChemMachines.SetMachineVisual(r.Machine, "heating_platform", workingVisual);

            if (!lit) { r.HeldSeconds = 0; r.ActiveReaction = null; return; }

            // 找当前温区内的实时反应
            // P3f 审计修复：燃料槽只作燃料，不再把其中的乙醇当反应投料（原实现会与燃烧语义冲突）；
            // 投料统一来自反应槽（容器或直接放入的药剂，原版药剂瓶按 VanillaAnalog 映射）。
            var ids = new List<string>();
            if (vesselItems != null)
                foreach (var vi in vesselItems)
                    CollectThermalInput(ids, vi, applyVanillaAnalog: true);

            // P3c：加热台气瓶槽同样可作气体来源。默认先用「普通投料」匹配（避免气瓶空占破坏既有配方）；
            // 若匹配不到，再把气瓶映射的 gas id 加入重试（供后续含气体的热反应使用）。
            var reaction = ChemLab.MatchThermal(ids, r.Temp);
            if (reaction == null && r.GasSlots != null)
            {
                var withGas = new List<string>(ids);
                foreach (var gs in r.GasSlots)
                {
                    var can = gs.childItem;
                    if (can == null) continue;
                    string gasId;
                    if (ChemRecipes.TryGetGasFromCanister(can.identifier, out gasId))
                    {
                        ChemCatalog.ClearCanisterContaminants(can);
                        withGas.Add(gasId);
                    }
                }
                if (withGas.Count > ids.Count)
                    reaction = ChemLab.MatchThermal(withGas, r.Temp);
            }
            if (reaction == null) { r.HeldSeconds = 0; r.ActiveReaction = null; return; }

            // 超温降解
            if (r.Temp > reaction.DegradeTemp)
            {
                Produce(r, new[] { ChemRecipes.SludgeId });
                ConsumeThermalInputs(r, reaction);
                r.HeldSeconds = 0; r.ActiveReaction = null;
                if (log != null) log.Warning("[FreeChem] overheated: " + reaction.Id);
                return;
            }

            if (r.ActiveReaction != reaction.Id) { r.ActiveReaction = reaction.Id; r.HeldSeconds = 0; }
            r.HeldSeconds++;
            if (r.HeldSeconds < reaction.Seconds) return;

            Produce(r, reaction.Outputs, reaction);
            ConsumeThermalInputs(r, reaction);
            ConsumeThermalGas(r, reaction);
            r.HeldSeconds = 0; r.ActiveReaction = null;
            if (ChemRuntime.Discover(reaction.Id) && log != null)
                log.Log("[FreeChem] thermal reaction discovered: " + reaction.Name);
        }

        /// <summary>
        /// P3f 审计修复：热反应成功后消耗反应槽中参与本次配方的投料（容器/未参与项保留）。
        /// 原实现从不销毁投料 → 会无限白嫖产物（尤其 heat_hcl_distill 同质提纯）。
        /// </summary>
        static void ConsumeThermalInputs(Rig r, ChemLab.ThermalReaction reaction)
        {
            if (r.VesselSlot == null) return;
            foreach (var it in r.VesselSlot.childItems.ToArray())
            {
                if (it == null) continue;
                string normalized = ChemRecipes.Normalize(it.identifier);
                bool participates = false;
                if (!string.IsNullOrEmpty(normalized))
                    foreach (var inp in reaction.Inputs)
                        if (inp == normalized) { participates = true; break; }
                if (participates) it.Destroy();
            }
        }

        /// <summary>
        /// P3c：若实时反应真的消耗了某气瓶气体，则销毁气瓶并在输出位放回空瓶（守恒）。
        /// 输出放不下空瓶时保留气瓶并日志，绝不丢失/复制。
        /// </summary>
        static void ConsumeThermalGas(Rig r, ChemLab.ThermalReaction reaction)
        {
            if (r.GasSlots == null) return;
            foreach (var gs in r.GasSlots)
            {
                var can = gs.childItem;
                if (can == null) continue;
                string canisterId = can.identifier;
                string gasId;
                if (!ChemRecipes.TryGetGasFromCanister(canisterId, out gasId)) continue;
                bool used = false;
                foreach (var inp in reaction.Inputs)
                    if (inp == gasId) { used = true; break; }
                if (!used) continue;

                GameItem empty = DirectoryMaster.Item("gas_canister_empty");
                if (empty == null) continue;
                ChemCatalog.EnsureOwned(empty);
                if (!GraphUtils.CanAccept(r.Output, empty))
                {
                    empty.Destroy();
                    if (log != null)
                        log.Warning("[FreeChem] heating output full: canister " + canisterId + " kept.");
                    continue;
                }
                can.Destroy();
                GraphUtils.TryAcceptAll(r.Output, empty);
                if (log != null)
                    log.Log("[FreeChem] heating platform consumed " + canisterId + " → empty canister returned.");
            }
        }

        /// <summary>
        /// 收集实时加热台的投料 id。
        /// · 本模组真药剂（ReagentTag）直接取 identifier；
        /// · 原版药剂瓶在容器槽里按 VanillaAnalog 映射（问题 5 兼容），燃料槽不做映射（酒精只作燃料）。
        /// </summary>
        static void CollectThermalInput(List<string> ids, GameItem it, bool applyVanillaAnalog)
        {
            if (it == null) return;
            if (applyVanillaAnalog)
            {
                string mapped;
                if (ChemRecipes.VanillaAnalog.TryGetValue(it.identifier, out mapped))
                {
                    ids.Add(mapped);
                    return;
                }
            }
            if (it.IsTag(ChemRecipes.ReagentTag)) ids.Add(it.identifier);
        }

        /// <summary>找反应槽中与产物同种（Normalize 后 id 相同）的投料，用于继承纯度状态。</summary>
        static GameItem FindSameInputSource(Rig r, string outputId)
        {
            if (r.VesselSlot == null || r.VesselSlot.childItems == null) return null;
            string want = ChemRecipes.Normalize(outputId);
            if (string.IsNullOrEmpty(want)) return null;
            foreach (var it in r.VesselSlot.childItems)
                if (it != null && ChemRecipes.Normalize(it.identifier) == want) return it;
            return null;
        }

        static void Produce(Rig r, string[] outputIds, ChemLab.ThermalReaction reaction = null)
        {
            foreach (var id in outputIds)
            {
                // P3b：实时反应产出同样必须 owned（否则无法倒液/交易交互）
                GameItem produced = ChemCatalog.EnsureOwned(DirectoryMaster.Item(id));

                // NEW-P1-2：产物与投料同种（如 heat_hcl_distill 输入=输出）时继承投料的
                // 污染物/提纯等级，避免把已提纯的药剂降回初始纯度。
                if (reaction != null)
                {
                    GameItem source = FindSameInputSource(r, id);
                    if (source != null)
                    {
                        try
                        {
                            int level = ChemCatalog.ReadPurifyLevel(source);
                            int[] contam = ChemCatalog.ReadContaminants(source);
                            ChemCatalog.ApplyContaminants(produced, contam, level);
                            // P7c：蒸馏/加热提纯应提升而非保持 → 同种产物纯度 +2（上限 100）
                            int srcP = ChemCatalog.ReadInt(source, ChemCatalog.TPurity, 0);
                            ChemCatalog.RaisePurityTo(produced, Math.Min(100, srcP + 2));
                            ChemCatalog.SyncVanillaPurity(produced);
                            ChemRuntime.Log("[heat] output " + id + " inherited state (level " + level
                                            + ") and purified to " + ChemRuntime.PurityOf(produced).ToString("0.#") + "%.");
                        }
                        catch (Exception e)
                        {
                            ChemRuntime.Log("[heat] inherit state failed: " + e.Message);
                        }
                    }
                }
                GraphUtils.TryAcceptAll(r.Output, produced);
                // 实时反应同样可能放出有毒气体：计入排气嫌疑度，并按倍率折算写入原版治安部（默认开启、可配置关闭）
                int units = ChemLab.ExhaustUnitsForGas(id);
                if (units > 0)
                {
                    ChemRuntime.AddExhaust(units);
                    if (log != null)
                        log.Warning("[FreeChem] toxic gas vented from heating platform: " + id
                                    + " (+" + units + " exhaust units)");
                }
            }
        }

        /// <summary>
        /// 燃料纯度：本 Mod 的乙醇读 fc_purity；原版酒精按官方拆包表 175/250 = 70%。
        /// 原版酒精物品 id 由 ChemLab.VanillaAlcoholIds 候选匹配，未核对的按默认 70%。
        /// 仅在「灌装」时对燃料瓶调用；Tick 期间读灯自身的 TFuelPurity，不依赖燃料槽物品。
        /// </summary>
        static int FuelPurity(GameItem fuel)
        {
            if (fuel == null) return ChemLab.VanillaAlcoholPurity;
            int p = ChemCatalog.ReadInt(fuel, ChemCatalog.TPurity, -1);
            if (p >= 0) return Mathf.Clamp(p, 0, 100);
            // P7a：原版酒精按 alcohol/(alcohol+water) 组分占比；无组分且 mini_bottle 回退 70%
            return ChemCatalog.AlcoholFuelPurity(fuel);
        }

        static void ConsumeFuel(Rig r, int purity)
        {
            var heater = r.HeaterSlot.childItem;
            var wick = r.WickSlot.childItem;
            if (heater == null || wick == null) return;

            // 消耗速率按燃料纯度：纯度越低越费（ChemLab.FuelBurnRate）
            int amount = Mathf.Max(1, Mathf.RoundToInt(2f * ChemLab.FuelBurnRate(purity)));
            int left = Mathf.Max(0, ChemCatalog.ReadInt(heater, ChemCatalog.TFuel, 0) - amount);
            heater.ModifyTag(ChemCatalog.TFuel, s => s.Enable().SetInt(left));

            int wickLeft = Mathf.Max(0, ChemCatalog.ReadInt(wick, ChemCatalog.TWickDur, 0) - 1);
            wick.ModifyTag(ChemCatalog.TWickDur, s => s.Enable().SetInt(wickLeft));
            if (wickLeft == 0)
            {
                wick.Destroy();
                if (log != null)
                    log.Log("[FreeChem] wick burned out and was consumed — put a new wick_* into the wick slot to continue.");
            }
            else if (wickLeft <= 3 && log != null)
            {
                log.Log("[FreeChem] wick durability low: " + wickLeft);
            }
            // 燃料已在 Refill 时消耗，这里不再重复销毁，避免对同一物品二次 Destroy
        }

        /// <summary>
        /// 给酒精灯灌燃料（加热台 Tick 自动调用，也可由外部激活回调调用）。
        /// P3a：灌装量 = 500 ml × 纯度（与 500 ml 液体瓶容量匹配），灯容量 500 ml；
        /// 同时把燃料纯度写进灯自身 tag（TFuelPurity），供后续 tick 计算火力/消耗。
        /// </summary>
        public static bool Refill(GameItem lamp, GameItem fuel)
        {
            if (lamp == null || fuel == null) return false;
            int purity = FuelPurity(fuel);
            int before = ChemCatalog.ReadInt(lamp, ChemCatalog.TFuel, 0);
            int need = Mathf.Max(0, ChemCatalog.FuelCapacityMl - before);
            if (need <= 0) return false;

            // NEW-P1-3：按瓶内剩余体积消耗，只倒需要量，剩余燃料保留在瓶里（空了才销毁）
            int fuelVol = ChemCatalog.GetPourVolumeMl(fuel);
            int effective = Mathf.RoundToInt(fuelVol * purity / 100f);
            int add = Mathf.Min(need, effective);
            if (add <= 0) return false;
            int usedMl = Mathf.Clamp(Mathf.CeilToInt(add * 100f / Mathf.Max(1, purity)), 1, Mathf.Max(1, fuelVol));

            lamp.ModifyTag(ChemCatalog.TFuel, s => s.Enable().SetInt(before + add));
            lamp.ModifyTag(ChemCatalog.TFuelPurity, s => s.Enable().SetInt(purity));
            int leftMl = fuelVol - usedMl;
            if (leftMl <= 0) fuel.Destroy();
            else ChemCatalog.SetPourVolumeMl(fuel, leftMl);
            if (log != null)
                log.Log("[FreeChem] fuel loaded: +" + add + " ml @ " + purity
                        + "% purity → lamp " + (before + add) + "/" + ChemCatalog.FuelCapacityMl
                        + " ml; fuel bottle left " + Mathf.Max(0, leftMl) + " ml"
                        + (leftMl <= 0 ? " (bottle consumed)." : "."));
            return true;
        }

        /// <summary>切换高档/低档（由加热台的按钮回调调用）。</summary>
        public static void ToggleHigh(GameItem machine)
        {
            foreach (var r in Rigs)
                if (r.Machine == machine) { r.High = !r.High; return; }
        }

        /// <summary>
        /// P3f 审计修复：点击加热器（酒精灯/喷灯）切换其所在加热台的高/低档。
        /// 之前 ToggleHigh 没有任何调用点 → 高档 160°C 不可达，heat_kmno4/heat_nh4no3 等高温热反应永远跑不了。
        /// 返回 true 表示找到该加热器所属的加热台。
        /// </summary>
        public static bool ToggleHighByHeater(GameItem heater)
        {
            if (heater == null) return false;
            foreach (var r in Rigs)
            {
                if (r.HeaterSlot != null && r.HeaterSlot.childItem == heater)
                {
                    r.High = !r.High;
                    if (log != null)
                        log.Log("[FreeChem] heating platform set to " + (r.High ? "HIGH (160°C)" : "LOW (99°C)") + ".");
                    return true;
                }
            }
            return false;
        }

        public static bool IsHigh(GameItem machine)
        {
            foreach (var r in Rigs) if (r.Machine == machine) return r.High;
            return false;
        }
    }
}
