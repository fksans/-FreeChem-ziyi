using System;
using System.Collections.Generic;
using UnityEngine;

namespace FreeChem
{
    /// <summary>
    /// 模组入口。遵循官方生命周期：Init/OnEnable 在主菜单运行，不读存档、不生成物品。
    /// 物品注册在 OnModItemDirectoryInit；游戏内钩子在 OnEnable 订阅、OnDisable 取消。
    /// </summary>
    public class ModEntry : IMod
    {
        private ModLog log;
        private ConfigEntry<bool> dangerousReactions;
        /// <summary>是否把有毒气体排放的嫌疑度同步写入原版治安部（SecData.ENDANGERMENT）。</summary>
        private ConfigEntry<bool> vanillaSecurity;
        /// <summary>调试模式（默认关）：控制 F9 调试面板按数字 ID 生成物品的入口。</summary>
        private ConfigEntry<bool> debugMode;

        /// <summary>
        /// 王尔德人脉是否已注册。AddNetworkUpgrade 依赖 PlayerStore（主菜单下会 NRE），
        /// 因此延迟到 OnGameLoaded 注册，并用此标志防止重复注册。
        /// </summary>
        private bool networkRegistered;

        public const string GasBrokerId = "free_chem.GAS_BROKER";
        public const string GasOrderId = "free_chem.GAS_ORDER";
        /// <summary>常驻排风服务：有毒气体排放的嫌疑度增长降至 55%。</summary>
        public const string VentServiceId = "free_chem.VENT_SERVICE";
        /// <summary>进阶排风服务：在基础排风之上，增长降至 12%；前置必须含 VentServiceId。</summary>
        public const string AdvVentServiceId = "free_chem.ADV_VENT_SERVICE";

        /// <summary>
        /// 温度档短名（仅写日志用，刻意不新增本地化键）。
        /// 档位语义：0=冷 / 1=常温 / 2=加热 / 3=高温高压。
        /// 对应 ChemRecipes 的 Temp 门禁：只有 ChemRuntime.Temp == 配方 Temp 时才匹配。
        /// 需要 Temp=2 的：electrolyzer 氯碱法/盐酸电解、gas_generator 浓硫酸制 HCl/铵盐放氨、
        ///                  chem_reactor 湿法制硫酸、crystallizer 盐水/硝酸铵结晶；
        /// 需要 Temp=3 的：gas_generator 高锰酸钾热分解、chem_reactor 氢氯合成、
        ///                  high_pressure_synth 哈伯法、fractionator 硝石制硝酸/奥斯特瓦尔德、
        ///                  T4 违禁品（空盐/夜玻/灰息）。
        /// thermometer 的点击循环是这些档位唯一的游戏内交互入口。
        /// </summary>
        static readonly string[] TempTierNames = { "cold", "ambient", "heated", "high-pressure" };

        /// <summary>
        /// 发明家库存可随机上架的实验器材（单次最多 1 件，避免刷屏）。
        /// 覆盖：反应容器 / 滤纸 / 灯芯 / 加热器 / 仪器，保证器材不是「死物品」。
        /// </summary>
        static readonly string[] ApparatusIds =
        {
            "beaker", "erlenmeyer_flask", "retort_flask", "test_tube_rack",
            "filter_paper", "filter_paper_fine", "filter_paper_ash",
            "wick_cotton", "wick_ceramic", "alcohol_lamp", "spirit_burner",
            "thermometer", "precision_balance", "ph_meter", "catalyst_kit",
        };

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            ChemRuntime.Bind(log);
            log.Log("FreeChem initializing (Build 26525552 / netstandard2.1)");

            ModConfig config = ModHelper.GetConfig(manifest.ID);
            dangerousReactions = config.Define("dangerous_reactions", true, "玩法",
                () => ModHelper.GetLocalized(ChemCatalog.ModId, "cfg_dangerous"),
                tooltip: () => ModHelper.GetLocalized(ChemCatalog.ModId, "cfg_dangerous_tip"));
            dangerousReactions.OnChanged += v =>
            {
                ChemRuntime.DangerousEnabled = v;
                log.Log("Dangerous reactions " + (v ? "enabled" : "disabled"));
            };
            ChemRuntime.DangerousEnabled = dangerousReactions.Value;

            // 原版治安部接入开关（默认开）：关闭后只记模组自有嫌疑度，不写原版 ENDANGERMENT。
            vanillaSecurity = config.Define("vanilla_security", true, "玩法",
                () => ModHelper.GetLocalized(ChemCatalog.ModId, "cfg_vanilla_security"),
                tooltip: () => ModHelper.GetLocalized(ChemCatalog.ModId, "cfg_vanilla_security_tip"));
            vanillaSecurity.OnChanged += v =>
            {
                ChemRuntime.VanillaSecurityEnabled = v;
                log.Log("Vanilla security hook " + (v ? "enabled" : "disabled")
                        + " (mod-local suspicion still recorded).");
            };
            ChemRuntime.VanillaSecurityEnabled = vanillaSecurity.Value;

            // 调试模式开关（默认关）：开启后 F9 调试面板的 FreeChem Command 入口才允许生成物品。
            debugMode = config.Define("debug_mode", false, "调试",
                () => ModHelper.GetLocalized(ChemCatalog.ModId, "cfg_debug_mode"),
                tooltip: () => ModHelper.GetLocalized(ChemCatalog.ModId, "cfg_debug_mode_tip"));
            debugMode.OnChanged += v =>
            {
                ChemRuntime.DebugMode = v;
                log.Log("Debug mode " + (v ? "enabled" : "disabled"));
            };
            ChemRuntime.DebugMode = debugMode.Value;

            ModHook.OnModItemDirectoryInit += RegisterItems;
        }

        void RegisterItems(ModItemDirectory dir)
        {
            foreach (var spec in ChemCatalog.Specs)
            {
                string id = spec.Id;
                dir.Add(id, () => BuildItem(id));
            }
            foreach (var def in ChemMachines.Defs)
            {
                ChemMachineDef d = def;
                dir.Add(d.Id, () => ChemMachines.Create(d));
            }
            // 实时加热台不在夜间 Defs 里，单独注册
            dir.Add("heating_platform", () => ChemMachines.Create(
                new ChemMachineDef { Id = "heating_platform" }));
            log.Log("Registered " + ChemCatalog.Specs.Count + " items, "
                    + ChemMachines.Defs.Count + " nightly machines + 1 realtime heating platform.");
        }

        GameItem BuildItem(string id)
        {
            GameItem item = ChemCatalog.Create(id);
            switch (id)
            {
                case "thermometer":
                    // 温度档 0 冷 / 1 常温 / 2 加热 / 3 高温高压：决定 temp:2/3 配方能否匹配
                    BindInstrument(item, () =>
                    { ChemRuntime.SetTemp((ChemRuntime.Temp + 1) % 4);
                      log.Log("Temperature tier " + ChemRuntime.Temp
                              + " / 3 (" + TempTierNames[ChemRuntime.Temp] + ")"); }); break;
                case "ph_meter":
                    BindInstrument(item, () =>
                    { int[] steps = { 1, 7, 13 };
                      int i = Array.IndexOf(steps, ChemRuntime.Ph);
                      ChemRuntime.SetPh(steps[(i + 1) % steps.Length]);
                      log.Log("pH tier " + ChemRuntime.Ph); }); break;
                case "precision_balance":
                    BindInstrument(item, () =>
                    { ChemRuntime.SetBalanced(!ChemRuntime.Balanced);
                      log.Log("Balanced " + ChemRuntime.Balanced); }); break;
                case "safety_goggles":
                    BindInstrument(item, () =>
                    { ChemRuntime.SetProtected(!ChemRuntime.Protected);
                      log.Log("Protection " + ChemRuntime.Protected); }); break;
                case "catalyst_kit":
                    BindInstrument(item, () => log.Log("Catalyst ready for next reaction.")); break;
                case "label_printer":
                    // 标签打印机：激活循环 7 档×2 类模式；点药剂瓶盖章（真实/标签纯度分离）
                    ChemPrinter.Bind(item);
                    break;
                case "alcohol_lamp":
                case "spirit_burner":
                    // 燃料通过加热台的燃料槽供给；点击灯切换所在加热台的高/低档（P3f：高档 160°C 的唯一入口）
                    BindInstrument(item, () =>
                    {
                        log.Log("Alcohol lamp fuel (ml): " +
                            ChemCatalog.ReadInt(item, ChemCatalog.TFuel, 0));
                        ChemHeating.ToggleHighByHeater(item);
                    }); break;
            }
            return item;
        }

        static void BindInstrument(GameItem item, Action onUse)
        {
            item.mayActivateSlotItemFunc = (it, _, _) => GeneralHelper.IsItemOwned(it);
            item.onActivateSlotItemFunc = (it, _, _) => onUse();
        }

        public void OnEnable()
        {
            log.Log("FreeChem enabled.");
            ChemHeating.Bind(log);
            ChemHeating.Start();          // 实时每秒加热回调
            ModHook.OnGameLoadedInit += OnGameLoaded;
            // P4c：读档后 TagSystem 会用存档状态整体覆盖 Init 写入的 tag →
            // 旧存档的液体容器 tag/组分被清掉。加载完成（Normal/Late）与每天开店时重建一次。
            ModHook.OnGameLoadedNormal += OnGameLoadedRehydrate;
            ModHook.OnGameLoadedLate += OnGameLoadedRehydrate;
            ModHook.OnPlaceInventorInventoryItemLate += OnInventorStock;
            ModHook.OnNewGameStarted += OnNewGameStarted;
            ModHook.OnNetworkUpgradeUnlocked += OnNetworkUpgrade;
            ModHook.OnCreateTooltipLate += OnTooltip;
            // P3e/P4c：开店时先重建旧存档 tag、确保电话商人已注册，再配送电话订购的化学品包
            ModHook.OnShutterOpenedEarly += OnShutterOpened;

            // ⚠ 不要在这里注册王尔德人脉（注册 API 会惰性访问 PlayerStore，主菜单下会 NRE，
            //    导致整个模组 Init failed 被禁用 —— v0.5.0 用户实测 Player.log 证据见
            //    下方延迟注册方法的注释）。本方法在主菜单运行，不能碰存档相关对象；
            //    正确的注册时机是 OnGameLoaded（存档已载入、PlayerStore 已存在）。

            // 首选入口：快捷键（默认 F10，可在「游戏选项 → 按键绑定」重绑；不依赖 dev.txt）
            ChemDebug.RegisterHotkey();

            // F9 调试面板：Command 列「FreeChem 按ID生成」入口（窗口/生成逻辑在 ChemDebug）。
            // ⚠ F9/F8 面板受 DevTools.IsEnabled 门控：需要游戏 exe 同级存在空 dev.txt（开发机开关，
            //   不随模组发布；模组不会自动创建文件）。注册本身在 OnEnable 是安全的（静态表）。
            try
            {
                ModHelper.AddDebugFunction(ChemCatalog.ModId, ModHelper.DEBUG_CATEGORY_COMMAND,
                    "FreeChem 按ID生成", ChemDebug.OpenSpawnWindow, tall: true);
                log.Log("F9 debug entry registered (Command / tall). "
                        + "NOTE: F9 panel requires an empty dev.txt next to the game exe "
                        + "(dev-machine switch; not shipped with the mod).");
            }
            catch (Exception e)
            {
                log.Warning("F9 debug entry registration failed: " + e.Message);
            }

            // 论文/手册回调：纯静态注册（PaperBookRegistry），不触碰 PlayerStore；仍包一层 try/catch，
            // 保证任何注册期异常都不会拖垮 OnEnable → 模组 Init。
            try
            {
                ModHelper.OnPaperOpened(ChemCatalog.ModId, "chem_codex", ui =>
                    ui.OnAction("refresh", () =>
                        ui.SetText("discovered",
                            ModHelper.GetLocalized(ChemCatalog.ModId, "codex_progress",
                                ChemRuntime.DiscoveredCount, ChemRecipes.All.Length))));
            }
            catch (Exception e)
            {
                log.Warning("Paper codex callback registration failed: " + e.Message);
            }
        }

        /// <summary>
        /// 王尔德人脉注册（延迟到游戏内）。
        /// 根因（v0.5.0 用户实测 Player.log）：
        ///   NullReferenceException at PlayerStore..ctor ← PlayerStore.get_Instance
        ///   ← NetworkUpgrade.RegisterModUpgrade ← ModHelper.AddNetworkUpgrade
        ///   ← ModEntry.RegisterWildNetwork ← ModEntry.OnEnable
        /// AddNetworkUpgrade 内部会访问 PlayerStore.Instance（惰性构造），主菜单下构造会抛 NRE；
        /// 因此必须等 OnGameLoaded（PlayerStore 已存在）后调用，并用 PlayerStore.HasInstance 做 guard，
        /// 失败只记日志、下次载入重试，绝不让模组 Init 抛异常。
        /// </summary>
        void TryRegisterWildNetwork()
        {
            if (networkRegistered) return;
            try
            {
                if (!PlayerStore.HasInstance)
                {
                    log.Warning("Wild network registration deferred: PlayerStore not ready yet.");
                    return;
                }
                RegisterWildNetwork();
                networkRegistered = true;
                log.Log("Wild network registered after game load (4 upgrades, tab free_chem).");
            }
            catch (Exception e)
            {
                log.Warning("Wild network registration failed (will retry on next load): " + e.Message);
            }
        }

        /// <summary>
        /// 在「Wild 的人脉」里加气体商人 / 气体订购 / 排风服务 / 进阶排风服务。
        /// 4 张卡片统一放到自定义标签页 tab = "free_chem"（官方指南：tab 留空=「基础」页；
        /// 自定义 tab 字符串创建独立标签页，SetNetworkTabLabel 只负责该页的显示名）。
        /// </summary>
        void RegisterWildNetwork()
        {
            ModHelper.SetNetworkTabLabel(ChemCatalog.ModId,
                () => ModHelper.GetLocalized(ChemCatalog.ModId, "network_tab"));

            ModHelper.AddNetworkUpgrade(ChemCatalog.ModId, GasBrokerId,
                cost: 600,
                subtitle: "Supplier",
                title: () => ModHelper.GetLocalized(ChemCatalog.ModId, "network_broker_title"),
                description: () => ModHelper.GetLocalized(ChemCatalog.ModId, "network_broker_desc"),
                onUnlock: () => { ChemRuntime.GasBrokerUnlocked = true;
                                  log.Log("Gas broker unlocked."); },
                iconKey: "free_chem:gas_canister_empty",
                tab: ChemCatalog.ModId);

            // 可重复：向气体商人下订单，冷却 3 天，送一批空钢瓶 + 基础气体
            ModHelper.AddNetworkUpgrade(ChemCatalog.ModId, GasOrderId,
                cost: 120,
                subtitle: "Supplier",
                title: () => ModHelper.GetLocalized(ChemCatalog.ModId, "network_order_title"),
                description: () => ModHelper.GetLocalized(ChemCatalog.ModId, "network_order_desc"),
                onUnlock: () => DeliverGasOrder(),
                tab: ChemCatalog.ModId,
                cooldownDays: 3,
                alreadyBought: () => ModHelper.GetLocalized(ChemCatalog.ModId, "network_order_wait"),
                prerequisites: new[] { GasBrokerId });

            // ── 常驻排风服务（Service 卡片）──
            // 不是一次性订单：不加 cooldownDays；解锁后长期生效，由 ChemRuntime.VentLevel 驱动倍率。
            ModHelper.AddNetworkUpgrade(ChemCatalog.ModId, VentServiceId,
                cost: 450,
                subtitle: "Service",
                title: () => ModHelper.GetLocalized(ChemCatalog.ModId, "network_vent_title"),
                description: () => ModHelper.GetLocalized(ChemCatalog.ModId, "network_vent_desc"),
                onUnlock: () =>
                {
                    ChemRuntime.RefreshVentLevel();
                    log.Log("Vent service unlocked (mod-local exhaust suspicion ×"
                            + ChemLab.VentMultiplierBp(1) / 10000f + ").");
                },
                iconKey: "free_chem:gas_scrubber",
                tab: ChemCatalog.ModId);

            // 进阶排风：必须已买基础排风（链式前置），同样常驻、无冷却。
            ModHelper.AddNetworkUpgrade(ChemCatalog.ModId, AdvVentServiceId,
                cost: 900,
                subtitle: "Service",
                title: () => ModHelper.GetLocalized(ChemCatalog.ModId, "network_adv_vent_title"),
                description: () => ModHelper.GetLocalized(ChemCatalog.ModId, "network_adv_vent_desc"),
                onUnlock: () =>
                {
                    ChemRuntime.RefreshVentLevel();
                    log.Log("Advanced vent service unlocked (mod-local exhaust suspicion ×"
                            + ChemLab.VentMultiplierBp(2) / 10000f + ").");
                },
                iconKey: "free_chem:condenser_unit",
                tab: ChemCatalog.ModId,
                prerequisites: new[] { VentServiceId });
        }

        /// <summary>给已存在的本模组物品补「已拥有 + 来源=FreeChem」（发明家库存的模板物品等）。</summary>
        static GameItem MarkOwned(GameItem it)
        {
            if (it == null) return null;
            ModHelper.SetItemOwnerShip(it, true);
            ModHelper.SetSource(it, ChemCatalog.ModId, "FreeChem");
            return it;
        }

        void DeliverGasOrder()
        {
            string[] pack = { "gas_canister_empty", "gas_canister_empty",
                              "gas_drum_empty",                      // 大型空桶来源（3×4 格）
                              "gas_oxygen", "gas_nitrogen", "liq_brine" };
            foreach (var id in pack)
            {
                GameItem it = ModHelper.SpawnItem(id);
                if (it == null) continue;
                // 问题 7：生成物必须标记「已拥有 + 来源=FreeChem」，否则会被顾客当成赃物/无来源
                ModHelper.SetItemOwnerShip(it, true);
                ModHelper.SetSource(it, ChemCatalog.ModId, "FreeChem");
                // 气体订单送到「后柜台」（不再放 Afterhours 仓库）
                ModHelper.MoveItemToBackCounter(it);
            }
            log.Log("Gas broker delivered an order to the back counter.");
        }

        void OnNetworkUpgrade(string id)
        {
            if (id == GasBrokerId) ChemRuntime.GasBrokerUnlocked = true;
            // 排风服务解锁：重新推导 0/1/2 档，立即影响后续排放的嫌疑度
            if (id == VentServiceId || id == AdvVentServiceId) ChemRuntime.RefreshVentLevel();
        }

        void OnGameLoaded()
        {
            // 先注册王尔德人脉（PlayerStore 已存在），再 Load 以便 IsNetworkUpgradeUnlocked 能恢复状态
            TryRegisterWildNetwork();
            ChemRuntime.Load();
            ChemRuntime.DangerousEnabled = dangerousReactions.Value;
            // 配置值可能在 Load() 之前被重置，载入后再同步一次（与原版开关一致的做法）
            ChemRuntime.VanillaSecurityEnabled = vanillaSecurity.Value;
            ChemRuntime.DebugMode = debugMode.Value;
            log.Log("Run state restored: " + ChemRuntime.DiscoveredCount + " reactions known.");
        }

        void OnInventorStock(List<GameItem> items)
        {
            // P7c：机器获取 —— 首次发明家库存时稳定上架全部 13 台机器（+加热台），
            // 之后按 40% 随机补货，避免玩家只能靠随机 1 台。
            bool machinesAdded = ModHelper.GetModDataBool(ChemCatalog.ModId, "inventor_machines_added", false);
            if (!machinesAdded)
            {
                foreach (var d in ChemMachines.Defs)
                    PlayerStore.Instance.AddDirectSellingItemToTable(
                        MarkOwned(DirectoryMaster.Item(d.Id)));
                PlayerStore.Instance.AddDirectSellingItemToTable(
                    MarkOwned(DirectoryMaster.Item("heating_platform")));
                ModHelper.SetModDataBool(ChemCatalog.ModId, "inventor_machines_added", true);
                log.Log("Inventor stock: all 13 machines + heating platform offered (first visit).");
            }
            else if (RNG.Roll(40))
            {
                int i = UnityEngine.Random.Range(0, ChemMachines.Defs.Count);
                PlayerStore.Instance.AddDirectSellingItemToTable(
                    MarkOwned(DirectoryMaster.Item(ChemMachines.Defs[i].Id)));
            }
            if (ChemRuntime.GasBrokerUnlocked && RNG.Roll(55))
                PlayerStore.Instance.AddDirectSellingItemToTable(
                    MarkOwned(DirectoryMaster.Item("gas_canister_empty")));
            if (RNG.Roll(50))
                PlayerStore.Instance.AddDirectSellingItemToTable(
                    MarkOwned(DirectoryMaster.Item("sol_sodium_chloride")));
            // 器材随机上架（单次最多 1 件）：烧杯/滤纸/灯芯/加热器/仪器/温度计都应有获取途径
            if (RNG.Roll(35))
                PlayerStore.Instance.AddDirectSellingItemToTable(
                    MarkOwned(DirectoryMaster.Item(
                        ApparatusIds[UnityEngine.Random.Range(0, ApparatusIds.Length)])));
        }

        void OnNewGameStarted(string scenarioId)
        {
            GameItem book = ModHelper.SpawnPaperItem(ChemCatalog.ModId, "chem_codex");
            if (book != null) ModHelper.MoveItemToInventory(book);
            // P5a：化学品商人便条改用原版 handnote（type=DOCUMENT，自带打开钩子，含电话 96686）
            try
            {
                string noteBody = ModHelper.GetLocalized(ChemCatalog.ModId, "note_chem_dealer_text");
                if (string.IsNullOrEmpty(noteBody) || noteBody == "note_chem_dealer_text")
                    noteBody = "化学品商人：96686。可订购基础化学品包（$320/包，每日 1 单，次日清晨送到前柜台）。";
                string noteTitle = ModHelper.GetLocalized(ChemCatalog.ModId, "note_chem_dealer_name");
                if (string.IsNullOrEmpty(noteTitle) || noteTitle == "note_chem_dealer_name") noteTitle = "化学品商人便条";
                GameItem dealerNote = PreBuiltItemHelper.CreateHandnote(noteBody, noteTitle);
                if (dealerNote != null)
                {
                    ModHelper.SetItemOwnerShip(dealerNote, true);
                    ModHelper.SetSource(dealerNote, ChemCatalog.ModId, "FreeChem");
                    ModHelper.MoveItemToInventory(dealerNote);
                }
            }
            catch (Exception e) { log.Warning("dealer handnote spawn failed: " + e.Message); }
            foreach (var id in new[] { "gas_canister_empty", "sol_sodium_chloride",
                                       "sol_calcium_carbonate", "liq_brine",
                                       // 温度计 + 加热台必备器材：保证开局就能控温、搭起加热台
                                       "thermometer", "beaker", "alcohol_lamp", "wick_cotton",
                                       })
            {
                GameItem it = ModHelper.SpawnItem(id);
                if (it == null) continue;
                // 问题 7：起始礼包同样标记「已拥有 + 来源=FreeChem」
                ModHelper.SetItemOwnerShip(it, true);
                ModHelper.SetSource(it, ChemCatalog.ModId, "FreeChem");
                ModHelper.MoveItemToInventory(it);
            }
            // P7a：起始礼包 2 瓶原版酒精（PreBuildChemHelper 生成，alcohol 70%）供酒精灯燃料
            for (int n = 0; n < 2; n++)
            {
                try
                {
                    GameItem fuelBottle = PreBuildChemHelper.CreateMiniAlcoholBottle();
                    if (fuelBottle == null) continue;
                    ModHelper.SetItemOwnerShip(fuelBottle, true);
                    ModHelper.SetSource(fuelBottle, ChemCatalog.ModId, "FreeChem");
                    ModHelper.MoveItemToInventory(fuelBottle);
                }
                catch (Exception e) { log.Warning("starter alcohol bottle failed: " + e.Message); }
            }
            log.Log("Chemist starter kit placed (incl. thermometer + heating apparatus + 2 vanilla alcohol bottles).");
                    // P4c：新游戏时电话簿由 StartNewGame 初始化，主动补一次本模组商人
            ChemPhone.EnsureQuickDial();
            // P5b：确保自定义 Liquid 组分已注册
            try { ChemCatalog.EnsureCustomLiquids(); } catch (Exception e) { log.Warning("custom liquids failed: " + e.Message); }
            // NEW-P1-8：回到主菜单再开新档时重置王尔德网络注册标志，避免 4 张升级卡不重注册
            networkRegistered = false;
            try { TryRegisterWildNetwork(); } catch (Exception e) { log.Warning("network re-register failed: " + e.Message); }
}

        /// <summary>
        /// 提示框追加内容：
        ///  · 真药剂（Kind 白名单）= 纯度 / 致命性 /（仅在成瘾或有欣快等级时）欣快等级 / 医疗品级；
        ///  · 本模组机器 = 排气嫌疑度 + 当前排风服务档位（与写入原版治安部的量一致）；
        ///  · 仪器/耗材 = 当前状态（温度档、pH、天平、防护、灯内燃料、灯芯耐久、滤纸剩余次数）。
        /// 工具/器材/容器/副产物不再显示化学画像（问题 6）。
        /// </summary>
        void OnTooltip(RichTextBuilder builder, GameItem item)
        {
            if (item == null) return;
            string id = item.identifier;

            if (ChemCatalog.IsMachine(id))
            {
                // 审查 2.4：加热台显示温度 / 档位 / 状态 / 进度
                if (id == "heating_platform")
                {
                    string heat = ChemHeating.StatusLine(item);
                    if (!string.IsNullOrEmpty(heat)) builder.AddLine(heat);
                }
                builder.AddLine(ChemRuntime.SuspicionLine());
                // P10c：机器用途便签 —— 即使 noteSlot 的 handnote 未显示，tooltip 也给出用法
                string noteKey = "machine_note_" + id;
                string noteText = ModHelper.GetLocalized(ChemCatalog.ModId, noteKey);
                if (!string.IsNullOrEmpty(noteText) && noteText != noteKey)
                    builder.AddLine(noteText);
                return;
            }

            // 标签打印机：显示当前盖章模式（14 标签模式 + 第 15 校准模式，含充能数）
            if (id == "label_printer")
            {
                builder.AddLine(ChemPrinter.ModeLine(item));
                return;
            }

            ChemSpec spec = ChemCatalog.Find(id);
            if (spec != null && ChemCatalog.IsReagentKind(spec.Kind))
            {
                string line = ChemRuntime.ProfileLine(item);
                if (!string.IsNullOrEmpty(line)) builder.AddLine(line);
                if (spec.Medical)
                    builder.AddLine(ModHelper.GetLocalized(ChemCatalog.ModId, "chem_medical_grade"));
                // P1a：可被原版成分仪分析；液体还可倒入原版液体容器（双氧水为「按水近似」）
                if (spec.Kind == "liquid")
                {
                    // 审查 2.5：显示剩余量；不足一份时提示不能参与反应
                    int ml = ChemCatalog.LiquidVolumeMl(item);
                    builder.AddLine(ModHelper.GetLocalized(ChemCatalog.ModId,
                        ml >= ChemCatalog.PortionMl - 1 ? "liquid_remaining" : "liquid_remaining_partial",
                        ml, ChemCatalog.PortionMl));
                    builder.AddLine(ModHelper.GetLocalized(ChemCatalog.ModId, "chem_vanilla_bridge_tip"));
                    if (id == "liq_hydrogen_peroxide")
                        builder.AddLine(ModHelper.GetLocalized(ChemCatalog.ModId, "pour_approx_water"));
                }
                // P1c：显示多级提纯等级（0 时不显示，避免噪音）
                int purifyLevel = ChemCatalog.ReadPurifyLevel(item);
                if (purifyLevel > 0)
                    builder.AddLine(ModHelper.GetLocalized(ChemCatalog.ModId, "purify_level", purifyLevel));
                // P3c：气瓶可用于机器「气瓶槽」供气（键未落盘时不显示，避免暴露裸键）
                if (ChemCatalog.IsCanisterItem(item))
                {
                    string gasTip = ModHelper.GetLocalized(ChemCatalog.ModId, "ui_gas_slot_tip");
                    if (!string.IsNullOrEmpty(gasTip) && gasTip != "ui_gas_slot_tip")
                        builder.AddLine(gasTip);
                }
                return;
            }

            // 仪器 / 加热器 / 耗材：显示当前状态 + 交互提示（P3a：加注/换芯/滤纸用法）
            string status = ChemEquipment.StatusLine(item);
            if (!string.IsNullOrEmpty(status)) builder.AddLine(status);

            if (id == "alcohol_lamp" || id == "spirit_burner")
                builder.AddLine(ModHelper.GetLocalized(ChemCatalog.ModId, "inst_lamp_hint"));
            else if (id.StartsWith("wick_"))
                builder.AddLine(ModHelper.GetLocalized(ChemCatalog.ModId, "inst_wick_hint"));
            else if (id.StartsWith("filter_paper"))
                builder.AddLine(ModHelper.GetLocalized(ChemCatalog.ModId, "inst_filter_hint"));
            else if (ChemCatalog.IsAlcoholFuel(item))
                builder.AddLine(ModHelper.GetLocalized(ChemCatalog.ModId, "inst_fuel_hint"));
        }



        /// <summary>P4c：读档完成后重建旧存档物品的原版 tag（液体容器/组分/chem 身份）+ 确保电话商人可拨。</summary>
        void OnGameLoadedRehydrate()
        {
            try { ChemCatalog.EnsureCustomLiquids(); }
            catch (Exception e) { log.Warning("custom liquids after load failed: " + e.Message); }
            try { ChemCatalog.RehydrateLoadedItems("OnGameLoaded"); }
            catch (Exception e) { log.Warning("rehydrate after load failed: " + e.Message); }
            try { ChemPhone.EnsureQuickDial(); }
            catch (Exception e) { log.Warning("phone ensure after load failed: " + e.Message); }
        }

        /// <summary>P4c：每天开店时兜底重建 tag/电话商人，然后配送电话订购的化学品包。</summary>
        void OnShutterOpened()
        {
            try { ChemCatalog.EnsureCustomLiquids(); }
            catch (Exception e) { log.Warning("custom liquids on shutter failed: " + e.Message); }
            try { ChemCatalog.RehydrateLoadedItems("OnShutterOpenedEarly"); }
            catch (Exception e) { log.Warning("rehydrate on shutter failed: " + e.Message); }
            try { ChemPhone.EnsureQuickDial(); }
            catch (Exception e) { log.Warning("phone ensure on shutter failed: " + e.Message); }
            try { ChemPhone.DeliverPendingOrder(); }
            catch (Exception e) { log.Warning("deliver pending order failed: " + e.Message); }
        }

        public void OnDisable()
        {
            ChemHeating.Stop();
            ModHook.OnModItemDirectoryInit -= RegisterItems;
            ModHook.OnGameLoadedInit -= OnGameLoaded;
            ModHook.OnGameLoadedNormal -= OnGameLoadedRehydrate;
            ModHook.OnGameLoadedLate -= OnGameLoadedRehydrate;
            ModHook.OnPlaceInventorInventoryItemLate -= OnInventorStock;
            ModHook.OnNewGameStarted -= OnNewGameStarted;
            ModHook.OnNetworkUpgradeUnlocked -= OnNetworkUpgrade;
            ModHook.OnCreateTooltipLate -= OnTooltip;
            ModHook.OnShutterOpenedEarly -= OnShutterOpened;
            ChemDebug.UnregisterHotkey();
            try
            {
                ModHelper.RemoveDebugFunctions(ChemCatalog.ModId);
                log.Log("F9 debug entries removed.");
            }
            catch (Exception e)
            {
                log.Warning("F9 debug entry removal failed: " + e.Message);
            }
            // 主菜单/半初始化状态下也必须安全：清理失败只记日志，绝不让 OnDisable 抛
            try
            {
                ModHelper.RemoveNetworkUpgrades(ChemCatalog.ModId);
                log.Log("Wild network upgrades removed.");
            }
            catch (Exception e)
            {
                log.Warning("Wild network cleanup failed: " + e.Message);
            }
            networkRegistered = false;
            log.Log("FreeChem disabled.");
        }
    }
}
