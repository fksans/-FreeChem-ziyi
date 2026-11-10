using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FreeChem
{
    /// <summary>
    /// P3e：电话化学品商人 + 便条订购 + 次日清晨前柜台配送。
    ///
    /// 原版电话系统没有公开「注册联系人」API（docs/feedback-research.md §5），因此：
    ///  · 用 Harmony Postfix 在 `StorePhoneClient.InitPhoneClientDict()` 里向返回字典注入一个
    ///    Supplier 类型联系人，并把对话构造函数注册进 `PhoneClientList.DialogFuncs`；
    ///  · 玩家拨号后由原版 PhoneUIManager 走 dialogFuncId→DialogFuncs 得到我们的 Dialogue；
    ///  · 订购只扣现金（原版电话无公开支付 API，直接扣 `PlayerStore.playerCash` 并日志）；
    ///  · 次日开店的 `ModHook.OnShutterOpenedEarly` 里把 pending 订单逐件生成并送前柜台
    ///    （放不下退回后柜台），成功后清 pending。
    ///
    /// 所有游戏调用均 try/catch，绝不在主菜单碰 PlayerStore（只在玩家拨号/开店时访问）。
    /// </summary>
    static class ChemPhone
    {
        /// <summary>本商人的电话号码（避开原版 6 个号码，避免 IsNumberExist 冲突）。</summary>
        public const long PhoneNumber = 96686L;
        public const string DialogFuncId = "free_chem_chem_merchant";

        /// <summary>基础化学品包价格（原版价格量级，8 件约 $234 货值 → 售价 $320）。</summary>
        public const int OrderPrice = 320;

        public const string PendingKey = "fc_chem_order_pending";
        public const string ItemsKey = "fc_chem_order_items";
        public const string DayKey = "fc_chem_order_day";

        /// <summary>基础化学品包：盐酸/硫酸/硝酸/烧碱/双氧水/盐水/氨水/纯碱 各 1。</summary>
        public static readonly string[] OrderPack =
        {
            "liq_hydrochloric_acid", "liq_sulfuric_acid", "liq_nitric_acid",
            "liq_sodium_hydroxide",  "liq_hydrogen_peroxide", "liq_brine",
            "liq_ammonia_water",     "sol_sodium_carbonate",
            // P7a：电话包附带 1 瓶原版酒精（PreBuildChemHelper 生成，可作酒精灯燃料）
            "mini_bottle",
        };

        /// <summary>
        /// Harmony Postfix：向原版电话簿注入本商人（一次），并注册对话构造器。
        /// 存档加载时 InitPhoneClientDict 可能重建字典，Postfix 会在每次重建后补回。
        /// </summary>
        [HarmonyPatch(typeof(StorePhoneClient), "InitPhoneClientDict")]
        static class ChemPhoneMerchantPatch
        {
            static void Postfix(Dictionary<long, StorePhoneClient> __result)
            {
                try
                {
                    if (__result == null) return;
                    if (!__result.ContainsKey(PhoneNumber))
                    {
                        __result[PhoneNumber] = CreateClient();
                        ChemRuntime.Log("[phone] injected FreeChem supplier at #" + PhoneNumber);
                    }
                    else
                    {
                        // 旧存档可能已有条目但状态不是 Regular → 统一纠正为电话簿可见状态
                        ApplyClientFields(__result[PhoneNumber]);
                        ChemRuntime.Log("[phone] supplier already in phone book; state normalized.");
                    }
                    RegisterDialogFunc();
                }
                catch (Exception e)
                {
                    ChemRuntime.Log("[phone] inject merchant failed: " + e.Message);
                }
            }
        }

        /// <summary>P4c/P6a：构造供应商联系人（phoneState=Regular(4)+dialedBefore=true 才会进电话簿/快捷拨号；type=Supplier(1)）。</summary>
        static StorePhoneClient CreateClient()
        {
            var c = new StorePhoneClient();
            ApplyClientFields(c);
            return c;
        }

        static void ApplyClientFields(StorePhoneClient c)
        {
            if (c == null) return;
            c.displayName = LOr("phone_chem_title", "FreeChem Chemical Supplier");
            c.locID = "phone_chem_merchant";
            c.dialogFuncId = DialogFuncId;
            // P6a（研究 §1/§2）：电话簿/快捷拨号只收录 ShownInPhoneBook()==true 的联系人，
            // 其条件为 phoneState ∈ {Regular, Cooldown} 且 dialedBefore==true；Responding 永不进电话簿。
            c.phoneState = StorePhoneClient.PhoneState.Regular;
            c.dialedBefore = true;
            c.phoneClientType = StorePhoneClient.PhoneClientType.Supplier;
            c.cooldownDuration = 0;
            c.currentCooldown = 0;
            if (c.alias == null) c.alias = new List<string>();
            c.stockType = 0;
        }

        static void RegisterDialogFunc()
        {
            try
            {
                if (PhoneClientList.DialogFuncs != null)
                    PhoneClientList.DialogFuncs[DialogFuncId] = pc => BuildDialog(pc);
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[phone] register dialog func failed: " + e.Message);
            }
        }

        /// <summary>
        /// P6a：快捷拨号收录。原版 PhoneBookUIManager 枚举 PlayerStore.PhoneClientDict 中
        /// ShownInPhoneBook() 的联系人生成 ContactElement（OnQuickDial(number) 一键拨号）；
        /// 因此 96686 必须保持 phoneState=Regular(4) 且 dialedBefore=true 才会出现在快捷拨号/联系人列表。
        /// 本方法保证该状态并幂等（重复调用不会重复收录）。
        /// </summary>
        public static void EnsureQuickDial()
        {
            EnsureRegistered();
            try
            {
                if (!PlayerStore.HasInstance) return;
                var dict = PlayerStore.Instance.PhoneClientDict;
                if (dict == null) return;
                StorePhoneClient client;
                if (dict.TryGetValue(PhoneNumber, out client) && client != null)
                {
                    // P6a：电话簿/快捷拨号只收 Regular/Cooldown + dialedBefore；
                    // 绝不能覆写成 Responding（Responding 永不进电话簿）。
                    client.phoneState = StorePhoneClient.PhoneState.Regular;
                    client.dialedBefore = true;
                    bool shown = false;
                    try { shown = client.ShownInPhoneBook(); } catch (Exception) { }
                    ChemRuntime.Log("[phone] quick dial ensured for #96686 (Regular/Supplier, ShownInPhoneBook="
                                    + shown + ").");
                }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[phone] EnsureQuickDial failed: " + e.Message);
            }
        }

        /// <summary>
        /// P4c：确保电话簿里有 96686。根因：PlayerStore.PhoneClientDict 只在 StartNewGame 由
        /// InitPhoneClientDict 初始化，读档不会重建 → 旧存档拨不到本模组商人。
        /// 在 OnGameLoadedNormal / OnNewGameStarted / OnShutterOpenedEarly 反复调用，幂等。
        /// </summary>
        public static void EnsureRegistered()
        {
            try
            {
                if (!PlayerStore.HasInstance)
                {
                    ChemRuntime.Log("[phone] ensure skipped: PlayerStore not loaded yet.");
                    return;
                }
                var dict = PlayerStore.Instance.PhoneClientDict;
                if (dict == null)
                {
                    ChemRuntime.Log("[phone] ensure skipped: PhoneClientDict is null; will retry on next hook.");
                    return;
                }
                StorePhoneClient client;
                if (!dict.TryGetValue(PhoneNumber, out client) || client == null)
                {
                    client = CreateClient();
                    dict[PhoneNumber] = client;
                    ChemRuntime.Log("[phone] registered PhoneClientDict[96686] dealer (Regular/Supplier, phone book visible).");
                }
                else
                {
                    ApplyClientFields(client);
                    ChemRuntime.Log("[phone] PhoneClientDict[96686] already registered; fields normalized.");
                }
                RegisterDialogFunc();
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[phone] EnsureRegistered failed: " + e.Message);
            }
        }

        /// <summary>P7d：电话订购目录（套餐/单项）。价格由 spec.Value 推导，套餐比单买便宜 15%。</summary>
        sealed class PhonePack
        {
            public string Key;
            public string FallbackName;
            public string[] Items;
            public int Price;
        }

        sealed class PhoneCategory
        {
            public string Key;
            public string FallbackName;
            public string[] Items;
        }

        /// <summary>单项价 = spec.Value × 1.6（取整，最低 1）。</summary>
        static int SinglePrice(string id)
        {
            ChemSpec spec = ChemCatalog.Find(id);
            int value = spec == null ? 10 : spec.Value;
            return Mathf.Max(1, Mathf.RoundToInt(value * 1.6f));
        }

        static int PackPrice(string[] items, int discountPercent)
        {
            int sum = 0;
            foreach (var id in items) sum += SinglePrice(id);
            return Mathf.Max(1, Mathf.RoundToInt(sum * (100 - discountPercent) / 100f));
        }

        static string ItemName(string id)
        {
            string name = ModHelper.GetLocalized(ChemCatalog.ModId, id + "_name");
            if (string.IsNullOrEmpty(name) || name == id + "_name") return id;
            return name;
        }

        static List<PhonePack> BuildPacks()
        {
            var packs = new List<PhonePack>
            {
                // 基础包沿用原有价格/内容（含 8 件化学品 + 1 瓶原版酒精）
                new PhonePack { Key = "phone_pack_basic", FallbackName = "Basic chemical pack",
                                Items = OrderPack, Price = OrderPrice },
            };
            packs.Add(new PhonePack
            {
                Key = "phone_pack_acid", FallbackName = "Acid pack",
                Items = new[] { "liq_hydrochloric_acid", "liq_sulfuric_acid", "liq_nitric_acid" },
            });
            packs.Add(new PhonePack
            {
                Key = "phone_pack_base", FallbackName = "Base pack",
                Items = new[] { "liq_sodium_hydroxide", "liq_ammonia_water", "sol_sodium_carbonate" },
            });
            packs.Add(new PhonePack
            {
                // P12：mod 水/乙醇已彻底移除 → 溶剂包 = 双氧水 + 盐水
                Key = "phone_pack_solvent", FallbackName = "Solvent pack",
                Items = new[] { "liq_hydrogen_peroxide", "liq_brine" },
            });
            packs.Add(new PhonePack
            {
                Key = "phone_pack_solid", FallbackName = "Solid pack",
                Items = new[] { "sol_sodium_carbonate", "sol_sodium_bicarbonate",
                                "sol_calcium_oxide", "sol_ammonium_chloride" },
            });
            foreach (var p in packs)
                if (p.Price <= 0) p.Price = PackPrice(p.Items, 15);
            return packs;
        }

        static List<PhoneCategory> BuildCategories()
        {
            return new List<PhoneCategory>
            {
                new PhoneCategory { Key = "phone_cat_acid", FallbackName = "Acids",
                    Items = new[] { "liq_hydrochloric_acid", "liq_sulfuric_acid", "liq_nitric_acid" } },
                new PhoneCategory { Key = "phone_cat_base", FallbackName = "Bases",
                    Items = new[] { "liq_sodium_hydroxide", "liq_ammonia_water",
                                    "sol_sodium_hydroxide", "sol_sodium_carbonate" } },
                new PhoneCategory { Key = "phone_cat_solvent", FallbackName = "Solvents",
                    Items = new[] { "liq_hydrogen_peroxide", "liq_brine" } },
                new PhoneCategory { Key = "phone_cat_solid", FallbackName = "Solids",
                    Items = new[] { "sol_sodium_chloride", "sol_calcium_carbonate", "sol_potassium_nitrate",
                                    "sol_sulfur", "sol_activated_carbon" } },
            };
        }

        enum OrderResult { Placed, Insufficient, Pending, DailyLimit, Failed }

        /// <summary>
        /// P7d：多页电话对话。首页（基础包 / 套餐目录 / 单项目录 / 挂断）；
        /// 套餐页（酸/碱/溶剂/固体/基础包，标价，套餐比单买便宜 15%）；
        /// 单项页（酸/碱/溶剂/固体分类 → 每项单项价）；
        /// 选中后进入结果页（成功/资金不足/待配送/当日已订/失败），每页都有挂断+返回。
        /// 按钮文本仍用 label Dialogue（电话 UI 取 nextDialogue.plainText），动态「物品名 - $价」。
        /// </summary>
        static Dialogue BuildDialog(StorePhoneClient pc)
        {
            GameItem icon = ChoiceIcon();
            var packs = BuildPacks();
            var categories = BuildCategories();

            Dialogue home = NewPage("phone_chem_greeting",
                "FreeChem chemical supplier. Choose a pack or a single chemical.");
            Dialogue packPage = NewPage("phone_pack_menu", "Chemical packs:");
            Dialogue singlePage = NewPage("phone_single_menu", "Single chemicals by category:");

            var ok = NewPage("phone_chem_result_ok", "Order placed. Delivered to the front counter next morning.");
            var insufficient = NewPage("phone_chem_result_fail_insufficient", "Not enough cash. Nothing was charged.");
            var pending = NewPage("phone_chem_result_pending", "Your previous order is still waiting for delivery.");
            var daily = NewPage("phone_chem_result_daily_limit", "You already ordered today. Call again tomorrow.");
            var failed = NewPage("phone_chem_result_fail_insufficient", "The order could not be placed. Please try again.");

            // 结果页统一带「返回首页 / 挂断」
            foreach (var page in new[] { ok, insufficient, pending, daily, failed })
            {
                Dialogue back = NewLabel(LOr("phone_chem_back", "Back"));
                back.nextDialogue = home;
                page.AddChoice(icon, "", "", back, null, null);
                AddHangup(page, OtherIcon(icon));
            }

            // 首页：基础包 / 套餐目录 / 单项目录 / 挂断
            PhonePack basic = packs[0];
            AddOrderChoice(home, icon, LabelFor(basic), basic.Items, basic.Price,
                ok, insufficient, pending, daily, failed);
            Dialogue packLabel = NewLabel(LOr("phone_pack_menu_choice", "Pack catalog"));
            packLabel.nextDialogue = packPage;
            home.AddChoice(OtherIcon(icon), "", "", packLabel, null, null);
            Dialogue singleLabel = NewLabel(LOr("phone_single_menu_choice", "Single chemicals"));
            singleLabel.nextDialogue = singlePage;
            home.AddChoice(OtherIcon(icon), "", "", singleLabel, null, null);
            AddHangup(home, OtherIcon(icon));

            // 套餐目录
            foreach (var pack in packs)
                AddOrderChoice(packPage, icon, LabelFor(pack), pack.Items, pack.Price,
                    ok, insufficient, pending, daily, failed);
            Dialogue backPack = NewLabel(LOr("phone_chem_back", "Back"));
            backPack.nextDialogue = home;
            packPage.AddChoice(OtherIcon(icon), "", "", backPack, null, null);
            AddHangup(packPage, OtherIcon(icon));

            // 单项目录：4 个分类入口
            var categoryPages = new Dictionary<string, Dialogue>();
            foreach (var cat in categories)
            {
                Dialogue page = NewPage(cat.Key, cat.FallbackName);
                categoryPages[cat.Key] = page;
                Dialogue catLabel = NewLabel(LOr(cat.Key, cat.FallbackName));
                catLabel.nextDialogue = page;
                singlePage.AddChoice(icon, "", "", catLabel, null, null);
            }
            Dialogue backSingle = NewLabel(LOr("phone_chem_back", "Back"));
            backSingle.nextDialogue = home;
            singlePage.AddChoice(OtherIcon(icon), "", "", backSingle, null, null);
            AddHangup(singlePage, OtherIcon(icon));

            // 每个分类的具体条目
            foreach (var cat in categories)
            {
                Dialogue page = categoryPages[cat.Key];
                foreach (var id in cat.Items)
                {
                    int price = SinglePrice(id);
                    AddOrderChoice(page, icon, ItemName(id) + " - $" + price, new[] { id }, price,
                        ok, insufficient, pending, daily, failed);
                }
                Dialogue backCat = NewLabel(LOr("phone_chem_back", "Back"));
                backCat.nextDialogue = singlePage;
                page.AddChoice(OtherIcon(icon), "", "", backCat, null, null);
                AddHangup(page, OtherIcon(icon));
            }

            return home;
        }

        static string LabelFor(PhonePack pack)
        {
            return LOr(pack.Key, pack.FallbackName) + " - $" + pack.Price;
        }

        /// <summary>给页面加一个订购选项：按钮文案动态「名称 - $价」，点击后按结果跳对应结果页。</summary>
        static void AddOrderChoice(Dialogue page, GameItem icon, string labelText, string[] items, int price,
            Dialogue ok, Dialogue insufficient, Dialogue pending, Dialogue daily, Dialogue failed)
        {
            Dialogue label = NewLabel(labelText);
            page.AddChoice(icon, "", "", label, () =>
            {
                OrderResult result = TryPlaceOrder(items, price);
                label.nextDialogue = result switch
                {
                    OrderResult.Placed => ok,
                    OrderResult.Insufficient => insufficient,
                    OrderResult.Pending => pending,
                    OrderResult.DailyLimit => daily,
                    _ => failed,
                };
            }, null);
        }

        /// <summary>只含按钮文案的标签 Dialogue（电话 UI 用 plainText 显示按钮）。</summary>
        static Dialogue NewLabel(string text)
        {
            var d = new Dialogue();
            d.SetText("PLAYER", string.IsNullOrEmpty(text) ? "…" : text);
            return d;
        }

        /// <summary>给页面加「挂断」选项：标签文案 + action 结束通话。</summary>
        static void AddHangup(Dialogue page, GameItem icon)
        {
            Dialogue label = NewLabel(LOr("phone_chem_menu_hangup", "Hang up"));
            page.AddChoice(icon, "", "", label, Hangup, null);
        }

        static Dialogue NewPage(string textKey, string fallback)
        {
            var d = new Dialogue();
            d.title = LOr("phone_chem_title", "FreeChem Chemical Supplier");
            string text = LOr(textKey, fallback);
            d.plainText = text;
            d.text = new RichTextBuilder(46);
            d.text.AddLine(text);
            return d;
        }

        /// <summary>挂断：结束电话对话并停止通话（异常逐层吞掉，保证不卡 UI）。</summary>
        static void Hangup()
        {
            try
            {
                if (PhoneUIManager.Instance != null)
                {
                    PhoneUIManager.Instance.OnPhoneDialogEnd();
                    PhoneUIManager.Instance.StopCall();
                }
            }
            catch (Exception e) { ChemRuntime.Log("[phone] hang up failed: " + e.Message); }
            try
            {
                if (DialoguePhoneUIManager.Instance != null)
                    DialoguePhoneUIManager.Instance.EndConversation();
            }
            catch (Exception e) { ChemRuntime.Log("[phone] end conversation failed: " + e.Message); }
        }

        /// <summary>选项图标：原版 handnote / postit。</summary>
        static GameItem ChoiceIcon()
        {
            try { var it = DirectoryMaster.Item("handnote"); if (it != null) return it; }
            catch (Exception e) { ChemRuntime.Log("[phone] choice icon handnote failed: " + e.Message); }
            try { return DirectoryMaster.Item("postit"); } catch { return null; }
        }

        static GameItem OtherIcon(GameItem first)
        {
            var it = ChoiceIcon();
            return it ?? first;
        }

        /// <summary>
        /// P7d：通用下单（套餐/单项共用）。保留：每日 1 单、先占位（day/pending/items）再扣款、
        /// 资金不足不扣款、pending 拦截连点；价格写入日志。返回结果供对话跳结果页。
        /// </summary>
        static OrderResult TryPlaceOrder(string[] items, int price)
        {
            try
            {
                if (items == null || items.Length == 0) return OrderResult.Failed;
                if (!PlayerStore.HasInstance) return OrderResult.Failed;
                if (ModHelper.GetModDataBool(ChemCatalog.ModId, PendingKey, false))
                    return OrderResult.Pending;

                int day = CurrentDayIndex();
                int lastDay = ModHelper.GetModDataInt(ChemCatalog.ModId, DayKey, -1);
                if (lastDay == day) return OrderResult.DailyLimit;

                int cash = PlayerStore.Instance.playerCash;
                if (cash < price) return OrderResult.Insufficient;

                // 防连点：先占位，再扣款
                ModHelper.SetModDataInt(ChemCatalog.ModId, DayKey, day);
                ModHelper.SetModDataBool(ChemCatalog.ModId, PendingKey, true);
                ModHelper.SetModData(ChemCatalog.ModId, ItemsKey, string.Join(",", items));
                try
                {
                    PlayerStore.Instance.playerCash = cash - price;
                }
                catch (Exception payEx)
                {
                    ModHelper.SetModDataBool(ChemCatalog.ModId, PendingKey, false);
                    ModHelper.RemoveModData(ChemCatalog.ModId, ItemsKey);
                    ChemRuntime.Log("[phone] payment failed, order rolled back: " + payEx.Message);
                    return OrderResult.Failed;
                }
                ChemRuntime.Log("[phone] order placed: " + items.Length + " item(s), price $" + price
                                + ", cash " + cash + " → " + (cash - price) + "; day " + day + ".");
                return OrderResult.Placed;
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[phone] order failed: " + e.Message);
                return OrderResult.Failed;
            }
        }

        /// <summary>
        /// 次日开店（OnShutterOpenedEarly）配送：逐件生成到前柜台；前柜台放不下退回后柜台。
        /// 全部尝试后清 pending（spawn 异常时保留 pending，等下一次开店重试）。
        /// </summary>
        public static void DeliverPendingOrder()
        {
            try
            {
                if (!ModHelper.GetModDataBool(ChemCatalog.ModId, PendingKey, false)) return;
                var emporium = EmporiumEntry.Instance;
                if (emporium == null)
                {
                    ChemRuntime.Log("[phone] delivery deferred: EmporiumEntry.Instance == null.");
                    return;
                }

                string packed = ModHelper.GetModData(ChemCatalog.ModId, ItemsKey, "");
                if (string.IsNullOrEmpty(packed))
                {
                    ClearPending();
                    ChemRuntime.Log("[phone] pending order was empty; cleared.");
                    return;
                }
                string[] ids = packed.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                int front = 0, back = 0, failed = 0;
                bool keepPending = false;
                foreach (var id in ids)
                {
                    try
                    {
                        // P7a：mini_bottle 不能走通用工厂（内容不定）→ 用 PreBuildChemHelper 生成原版酒精瓶
                        GameItem it = id == "mini_bottle"
                            ? PreBuildChemHelper.CreateMiniAlcoholBottle()
                            : ModHelper.SpawnItem(id);
                        if (it == null) { failed++; continue; }
                        ModHelper.SetItemOwnerShip(it, true);
                        ModHelper.SetSource(it, ChemCatalog.ModId, "FreeChem");

                        if (emporium.frontInvinvElement != null
                            && GraphUtils.CanAccept(emporium.frontInvinvElement, it))
                        {
                            ModHelper.MoveItemToFrontCounter(it);
                            front++;
                        }
                        else
                        {
                            ModHelper.MoveItemToBackCounter(it);
                            back++;
                            ChemRuntime.Log("[phone] front counter full → " + id + " placed on back counter.");
                        }
                    }
                    catch (Exception e)
                    {
                        failed++;
                        keepPending = true;
                        ChemRuntime.Log("[phone] delivery item failed (" + id + "): " + e.Message);
                    }
                }

                if (keepPending)
                    ChemRuntime.Log("[phone] delivery partially failed; pending kept for retry. front="
                                    + front + " back=" + back + " failed=" + failed);
                else
                {
                    ClearPending();
                    ChemRuntime.Log("[phone] delivered pending order: front=" + front
                                    + " back=" + back + " failed=" + failed);
                }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[phone] delivery failed: " + e.Message);
            }
        }

        static void ClearPending()
        {
            try
            {
                ModHelper.SetModDataBool(ChemCatalog.ModId, PendingKey, false);
                ModHelper.RemoveModData(ChemCatalog.ModId, ItemsKey);
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[phone] clear pending failed: " + e.Message);
            }
        }

        /// <summary>P6a：可靠的游戏日期（StoreStation.GetDayCounter；null 时返回 0）。</summary>
        static int CurrentDayIndex()
        {
            try { return StoreStation.GetDayCounter(); }
            catch (Exception e)
            {
                ChemRuntime.Log("[phone] GetDayCounter failed, fallback 0: " + e.Message);
                return 0;
            }
        }

        /// <summary>原版单按钮提示（缺失时只写日志）。</summary>
        static void Alert(string message)
        {
            try
            {
                if (GenericConfirmationUIManager.Instance != null)
                {
                    GenericConfirmationUIManager.Instance.OpenPanelSingleButton(
                        message, LOr("debug_alert_ok", "OK"), null);
                    return;
                }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[phone] alert panel failed: " + e.Message);
            }
            ChemRuntime.Log("[phone] " + message);
        }

        static string LOr(string key, string fallback, params object[] args)
        {
            string s = ModHelper.GetLocalized(ChemCatalog.ModId, key, args);
            return (string.IsNullOrEmpty(s) || s == key) ? fallback : s;
        }

    /// <summary>
    /// P6a：电话 UI 用 locID→LocHelper.GetLocalizedClientName 显示联系人姓名，
    /// displayName 不直接显示。对本模组商人返回我们写入的本地化 displayName。
    /// </summary>
    [HarmonyPatch(typeof(StorePhoneClient), "GetLocalizedDisplayName")]
    static class ChemPhoneDisplayNamePatch
    {
        static void Postfix(StorePhoneClient __instance, ref string __result)
        {
            try
            {
                if (__instance == null || ChemPhone.DialogFuncId != __instance.dialogFuncId) return;
                if (!string.IsNullOrEmpty(__instance.displayName)) __result = __instance.displayName;
            }
            catch (Exception e) { ChemRuntime.Log("[phone] display name patch failed: " + e.Message); }
        }
    }
    }
}
