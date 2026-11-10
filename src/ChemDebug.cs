using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FreeChem
{
    /// <summary>
    /// 调试生成窗口：按 <see cref="ChemItemIds"/> 的私有数字 ID 把物品生成到「后柜台」。
    ///
    /// 入口（第 14/15 轮需求）：
    ///   · **首选：快捷键**（默认 F10，可在「游戏选项 → 按键绑定」重绑，不需要 dev.txt）。
    ///     ModEntry.OnEnable 调 <see cref="RegisterHotkey"/> 注册 ModHelper.AddGameOptionKeyBind，
    ///     并用 ModHelper.StartCoroutine 每帧轮询 Input.GetKeyDown(hotkey)（禁止线程）；
    ///     玩家正在重绑按键时（KeyBindHelper.SuppressHotkeys）跳过，只在店铺场景生效，
    ///     按键打开/关闭同一个生成窗口。快捷键本身不受 debug_mode 限制（先生成窗口，生成时才检查）。
    ///   · 兼容：F9 调试面板 Command 列按钮（ModHelper.AddDebugFunction，dev 机专用）。
    ///     ⚠ F9/F8 面板受 DevTools.IsEnabled 门控：仅编辑器、或游戏 exe 同级存在空文件 dev.txt 时可用。
    ///       dev.txt 是**开发机开关，不随模组发布**；本模组不会（也不能，Capabilities 为空）自动创建该文件。
    ///       依据 docs/api-findings.md §8.4 与官方 ModdingGuide「受限构建无 F9」章节。
    ///   · 窗口用 CustomUIManager.Window(id, title, LAYER_REAR) + 默认 game 预制体，
    ///     与 F9 调试面板 / 原版 Item Spawner 同外观（docs §8.6）；
    ///     含「说明 / 有效范围 / 输入框 / 状态行 / 生成物品 / 列出 ID 对照 / 关闭」。
    ///   · 生成目标固定为「后柜台」EmporiumEntry.backInvinvElement；
    ///     先用 GraphUtils.CanAccept 只读探测，放不下则销毁物品并弹提示；
    ///     实际放置用 GraphUtils.TryAcceptAllOrDestroy(...,-1 语义)，失败时它已自动 Destroy，不会重复销毁。
    ///   · 满仓提示优先用真正的原版 modal GenericConfirmationUIManager.OpenPanelSingleButton，
    ///     单例不存在时回退 meta 风格自建窗口（docs §8.5）。
    ///   · 生成受模组设置 debug_mode 开关控制（ChemRuntime.DebugMode），默认关闭。
    ///
    /// 依据：docs/api-findings.md §8（元数据 + IL 核对）与官方 ModdingGuide 对应章节。
    /// 未实机验证项：F10 实际是否被游戏占用/生效、窗口呈现、输入法/回车行为、后柜台满判定、原版外观差异。
    /// </summary>
    public static class ChemDebug
    {
        public const string SpawnWindowId = "free_chem.debug_spawn";
        public const string AlertWindowId = "free_chem.debug_alert";

        const string InputId = "input";
        const string StatusId = "status";

        /// <summary>PlayerPrefs 键：快捷键（存 (int)KeyCode；全局偏好，跨存档共享）。</summary>
        const string HotkeyPrefKey = "free_chem.debug_hotkey";

        /// <summary>默认快捷键 F10（官方指南：键值由模组自己保管，全局偏好用 PlayerPrefs）。</summary>
        public const KeyCode DefaultHotkey = KeyCode.F10;

        /// <summary>当前快捷键（启动时从 PlayerPrefs 读回；用户重绑时由 setter 更新）。</summary>
        static KeyCode hotkey = DefaultHotkey;

        /// <summary>每帧轮询协程句柄（OnDisable 停止）。</summary>
        static Coroutine hotkeyTicker;

        /// <summary>输入框最近一次内容（onChanged/onTyping 同步）。</summary>
        static string lastInput = "";

        // ── 快捷键：注册 / 注销 / 每帧轮询（第 15 轮）──

        /// <summary>
        /// 注册「选项 → 按键绑定」条目 + 启动每帧轮询协程。由 ModEntry.OnEnable 调用。
        /// 注册在主菜单即可（官方指南允许）；协程内对场景/窗口做 guard。
        /// </summary>
        public static void RegisterHotkey()
        {
            LoadHotkeyFromPrefs();
            try
            {
                ModHelper.AddGameOptionKeyBind(ChemCatalog.ModId, ModHelper.OPTION_SECTION_KEYBIND,
                    "debug_open",
                    () => L("option_debug_open"),
                    () => hotkey,
                    k =>
                    {
                        hotkey = k;
                        PlayerPrefs.SetInt(HotkeyPrefKey, (int)k);
                    },
                    tooltip: () => L("option_debug_open_tip"));
                Log("hotkey option registered (default F10, rebindable in game Options > Key Bindings).");
            }
            catch (Exception e)
            {
                Log("hotkey option registration failed: " + e.Message);
            }

            if (hotkeyTicker == null)
            {
                try
                {
                    hotkeyTicker = ModHelper.StartCoroutine(ChemCatalog.ModId, HotkeyLoop());
                    Log("hotkey poll coroutine started.");
                }
                catch (Exception e)
                {
                    Log("hotkey poll coroutine start failed: " + e.Message);
                }
            }
        }

        /// <summary>清理：移除游戏选项条目 + 停止轮询协程。由 ModEntry.OnDisable 调用。</summary>
        public static void UnregisterHotkey()
        {
            try
            {
                int removed = ModHelper.RemoveGameOptions(ChemCatalog.ModId);
                Log("game options removed (" + removed + ").");
            }
            catch (Exception e)
            {
                Log("game options removal failed: " + e.Message);
            }
            StopHotkeyLoop();
        }

        static void StopHotkeyLoop()
        {
            if (hotkeyTicker == null) return;
            try
            {
                ModHelper.StopCoroutine(ChemCatalog.ModId, hotkeyTicker);
                Log("hotkey poll coroutine stopped.");
            }
            catch (Exception e)
            {
                Log("hotkey poll coroutine stop failed: " + e.Message);
            }
            hotkeyTicker = null;
        }

        /// <summary>
        /// 每帧轮询（协程，绝不用线程）：玩家重绑时静默 → 按键按下 → 只在店铺场景 → 切换窗口。
        /// 异常逐帧吞掉并记日志，保证循环不中断。
        /// </summary>
        static IEnumerator HotkeyLoop()
        {
            while (true)
            {
                yield return null;   // 每帧检查一次
                try
                {
                    // 玩家正在「按键绑定」里选键：游戏热键静默，我们也跳过
                    if (KeyBindHelper.SuppressHotkeys) continue;
                    if (!Input.GetKeyDown(hotkey)) continue;
                    // 只在店铺场景生效（后柜台只在店里存在）
                    if (EmporiumEntry.Instance == null) continue;
                    ToggleSpawnWindow();
                }
                catch (Exception e)
                {
                    Log("hotkey poll error: " + e.Message);
                }
            }
        }

        /// <summary>窗口已开则关闭，否则打开（快捷键是切换语义）。</summary>
        public static void ToggleSpawnWindow()
        {
            try
            {
                if (CustomUIManager.Instance == null) return;
                if (CustomUIManager.Instance.IsOpen(SpawnWindowId))
                {
                    CustomUIManager.Instance.CloseWindow(SpawnWindowId);
                    Log("debug spawn window closed by hotkey.");
                }
                else
                {
                    OpenSpawnWindow();
                    Log("debug spawn window opened by hotkey.");
                }
            }
            catch (Exception e)
            {
                Log("hotkey window toggle failed: " + e.Message);
            }
        }

        /// <summary>从 PlayerPrefs 读回快捷键；无值/非法值回退 F10。</summary>
        static void LoadHotkeyFromPrefs()
        {
            int raw;
            try
            {
                raw = PlayerPrefs.GetInt(HotkeyPrefKey, (int)DefaultHotkey);
            }
            catch (Exception e)
            {
                Log("hotkey load failed, fallback F10: " + e.Message);
                hotkey = DefaultHotkey;
                return;
            }
            KeyCode loaded = (KeyCode)raw;
            hotkey = IsUsableHotkey(loaded) ? loaded : DefaultHotkey;
            Log("hotkey loaded: " + hotkey + " (" + (int)hotkey + ").");
        }

        /// <summary>可用性校验：None/Escape/鼠标左键不可作热键（键绑定 UI 本身会拒，这里兜底旧值/手改配置）。</summary>
        static bool IsUsableHotkey(KeyCode key)
        {
            if (key == KeyCode.None || key == KeyCode.Escape || key == KeyCode.Mouse0) return false;
            return Enum.IsDefined(typeof(KeyCode), key);
        }

        /// <summary>F9 面板按钮入口：打开「按 ID 生成」窗口。</summary>
        public static void OpenSpawnWindow()
        {
            if (CustomUIManager.Instance == null)
            {
                Log("open spawn window skipped: CustomUIManager.Instance == null");
                return;
            }

            lastInput = "";
            try
            {
                // 外观：与 F9 调试面板 / 原版 Item Spawner 同款——layer "rear" + 默认 game 预制体
                // （docs/api-findings.md §8.6：原版 CustomUIDebugPanel.Open / CustomUIItemSpawner.Open
                //   都是 CreateWindow(..., "rear", null, Style.Auto)；UseMetaPrefabs 是 Mods 菜单深色风，不采用）。
                // rear 层缺 anchor 时游戏会自动记警告并回落 overlay，不会崩。
                CustomUIManager.Window(SpawnWindowId, L("debug_title"), CustomUIManager.LAYER_REAR)
                    .AddLabel(L("debug_hint"))
                    .AddLabel(L("debug_range", ChemItemIds.MinId, ChemItemIds.MaxId))
                    .AddInput(L("debug_placeholder"), "",
                              v => lastInput = v, InputId, v => lastInput = v)
                    .AddLabel(L("debug_status_ready"), StatusId)
                    .AddButton(L("debug_spawn"), () =>
                    {
                        string status;
                        TrySpawnFromCommand(lastInput, out status);
                        SetStatus(status);
                    })
                    // P3d：原「列出 ID 对照（控制台）」已移除，改为生成一本全场唯一的 ID 索引书
                    .AddButton(LOr("debug_gen_index_book", "Spawn ID index book"), () =>
                    {
                        string status;
                        SpawnIndexBook(out status);
                        SetStatus(status);
                    })
                    // 注：AddCloseButton(Action, string id=null) 只绑回调、不会自动关窗（docs §8.9 第 6 条），
                    // 所以这里手动 CloseWindow；另放一个带本地化文案的「关闭」按钮。
                    .AddButton(L("debug_close"),
                        () => CustomUIManager.Instance.CloseWindow(SpawnWindowId))
                    .AddCloseButton(() => CustomUIManager.Instance.CloseWindow(SpawnWindowId))
                    .SetSize(560, 340)
                    .SetDraggable(true)
                    .Center()
                    .SetCloseOnEscape()
                    .Show();
                Log("spawn window opened.");
            }
            catch (Exception e)
            {
                Log("open spawn window failed: " + e.Message);
            }
        }

        // ── 生成逻辑 ──

        /// <summary>
        /// 解析输入指令并尝试生成到后柜台。<paramref name="status"/> 为给窗口状态行/日志的最终文案。
        /// 返回 true 仅表示「成功放入后柜台」或「成功列出 ID」。
        /// </summary>
        public static bool TrySpawnFromCommand(string raw, out string status)
        {
            status = "";
            if (!ChemRuntime.DebugMode)
            {
                status = L("debug_status_disabled");
                ShowVanillaAlert(L("debug_alert_title"), status);
                Log("spawn blocked: debug mode is disabled (enable it in the mod settings).");
                return false;
            }

            int numId;
            string parseError;
            if (!ChemItemIds.TryParseCommand(raw, out numId, out parseError))
            {
                status = L("err_invalid_id");
                Log("spawn parse failed: '" + (raw ?? "") + "' (" + parseError + ")");
                return false;
            }

            if (numId == ChemItemIds.ListCommand)
            {
                // P3d：控制台 ID 列表已移除，提示改用「生成 ID 索引书」按钮
                status = LOr("debug_list_removed", "The ID list is now a book: use the 'Spawn ID index book' button.");
                Log("list command removed; use the index book button.");
                return false;
            }

            if (EmporiumEntry.Instance == null)
            {
                status = L("err_no_store");
                Log("spawn failed: EmporiumEntry.Instance == null (not in the store scene).");
                return false;
            }
            GameGridInventory back = EmporiumEntry.Instance.backInvinvElement;
            if (back == null)
            {
                status = L("err_no_store");
                Log("spawn failed: EmporiumEntry.backInvinvElement == null.");
                return false;
            }

            string itemId;
            if (!ChemItemIds.TryGetId(numId, out itemId))
            {
                status = L("err_invalid_id");
                Log("spawn failed: no item for numeric id " + numId + ".");
                return false;
            }

            if (ChemCatalog.IsRemovedLegacyItem(itemId))
            {
                status = "已移除：请使用原版水容器 / 原版酒精瓶（mini_bottle）。";
                Log("spawn blocked: " + itemId + " is removed (reserved ID).");
                ShowVanillaAlert(L("debug_alert_title"), status);
                return false;
            }
            GameItem item = ModHelper.SpawnItem(itemId);
            if (item == null)
            {
                status = L("err_not_found");
                Log("spawn failed: SpawnItem returned null for '" + itemId + "'.");
                return false;
            }
            // 问题 7：调试生成物同样标注「已拥有 + 来源=FreeChem」
            ModHelper.SetItemOwnerShip(item, true);
            ModHelper.SetSource(item, ChemCatalog.ModId, "FreeChem");

            // ① 先只读探测：CanAccept 只查有没有合法槽位，不改任何状态（docs §8.2）。
            //    放不下就销毁刚 spawn 出来的悬空物品 + 弹原版风格提示。
            if (!GraphUtils.CanAccept(back, item))
            {
                item.Destroy();
                string full = L("debug_alert_full");
                ShowVanillaAlert(L("debug_alert_title"), full);
                status = L("debug_status_bad", full);
                Log("spawn failed: back counter is full (id " + numId + " " + itemId + ").");
                return false;
            }

            // ② 实际放置用 TryAcceptAllOrDestroy：内部先 TryAcceptAll，没放下会自动 DestroyIfUnplaced
            //    （含 ReportUnplaced 日志），不会重复销毁。
            //    ⚠ TryAcceptAll 的返回值是「剩余未接受数量」（0=全部放入），
            //      绝不能反过来当「已接受数量」用（docs §8.2 明确记录的误用，此处改用 bool 版 API 规避）。
            if (!GraphUtils.TryAcceptAllOrDestroy(back, item, "free_chem.debug_spawn"))
            {
                string full = L("debug_alert_full");
                ShowVanillaAlert(L("debug_alert_title"), full);
                status = L("debug_status_bad", full);
                Log("spawn failed: TryAcceptAllOrDestroy returned false for id "
                    + numId + " " + itemId + " (item was destroyed).");
                return false;
            }

            status = L("debug_status_ok", Describe(numId));
            Log("spawned into back counter: " + Describe(numId) + ".");
            return true;
        }

        /// <summary>
        /// P3d：生成「化学 ID 索引书」（chem_index，6 页覆盖 1–96）。
        /// 全场唯一：生成前扫描店内可见物品栏（玩家背包/前后柜台/展示柜/隐藏区/巴扎货架/文档槽等），
        /// 已存在 BOOK_ID 含 chem_index 的书则不生成并弹原版提示。成功后 owner/source 标注 + 放后柜台。
        /// </summary>
        public static bool SpawnIndexBook(out string status)
        {
            status = "";
            if (!ChemRuntime.DebugMode)
            {
                status = L("debug_status_disabled");
                ShowVanillaAlert(L("debug_alert_title"), status);
                return false;
            }
            if (EmporiumEntry.Instance == null)
            {
                status = L("err_no_store");
                Log("index book spawn failed: EmporiumEntry.Instance == null.");
                return false;
            }
            try
            {
                int scanned;
                int existing = ChemCatalog.CountIndexBooks(out scanned);
                if (existing > 0)
                {
                    status = LOr("debug_book_exists", "An index book already exists in the store.");
                    ShowVanillaAlert(L("debug_alert_title"), status);
                    Log("[book] spawn blocked: " + existing + " index book(s) among " + scanned + " scanned item(s).");
                    return false;
                }

                GameItem book = ModHelper.SpawnPaperItem(ChemCatalog.ModId, "chem_index");
                if (book == null)
                {
                    status = LOr("debug_book_spawn_fail", "Failed to create the index book.");
                    Log("[book] SpawnPaperItem returned null.");
                    return false;
                }
                ModHelper.SetItemOwnerShip(book, true);
                ModHelper.SetSource(book, ChemCatalog.ModId, "FreeChem");
                ModHelper.MoveItemToBackCounter(book);
                status = LOr("debug_book_ok", "Index book created on the back counter.");
                Log("[book] scanned " + scanned + " item(s), no existing index book → spawned chem_index once.");
                return true;
            }
            catch (Exception e)
            {
                status = LOr("debug_book_spawn_fail", "Failed to create the index book.");
                Log("[book] spawn failed: " + e.Message);
                return false;
            }
        }

        /// <summary>缺键时回退英文兜底（避免把裸键名显示给玩家）。</summary>
        static string LOr(string key, string fallback, params object[] args)
        {
            string s = ModHelper.GetLocalized(ChemCatalog.ModId, key, args);
            return (string.IsNullOrEmpty(s) || s == key) ? fallback : s;
        }

        /// <summary>带本地化名称的编号描述，如 "#12 双氧水 H2O2"（纯数据部分在 ChemItemIds）。</summary>
        public static string Describe(int numId)
        {
            string itemId;
            if (!ChemItemIds.TryGetId(numId, out itemId))
                return ChemItemIds.DescribeRaw(numId);

            if (ChemCatalog.IsRemovedLegacyItem(itemId)) return "#" + numId + " [已移除：用原版水/酒精]";
            string name = ModHelper.GetLocalized(ChemCatalog.ModId, itemId + "_name");
            ChemSpec spec = ChemCatalog.Find(itemId);
            string formula = spec == null ? null : ChemRecipes.Formula(spec.Id);
            if (!string.IsNullOrEmpty(formula))
                return "#" + numId + " " + name + " " + formula;
            return "#" + numId + " " + name;
        }

        // ── 原版风格弹窗 / 状态行 ──

        /// <summary>
        /// 满仓/禁用提示。首选**真正的原版 modal**：GenericConfirmationUIManager.OpenPanelSingleButton
        /// （带暗化遮罩，原版自己就在店内用它做提示；docs/api-findings.md §8.5）。
        /// 该单例只在有 UI 场景存在，故 guard Instance != null；缺失时回退到 meta 风格自建窗口。
        /// </summary>
        public static void ShowVanillaAlert(string title, string body)
        {
            try
            {
                if (GenericConfirmationUIManager.Instance != null)
                {
                    // OpenPanelSingleButton 没有标题参数，标题只用于回退窗口/日志。
                    GenericConfirmationUIManager.Instance.OpenPanelSingleButton(
                        body, L("debug_alert_ok"), null);
                    return;
                }
            }
            catch (Exception e)
            {
                Log("vanilla confirmation panel failed: " + e.Message);
            }

            // 回退：meta 风格自建窗口（主菜单等没有 Confirmation 单例的场景仍可显示）
            if (CustomUIManager.Instance == null)
            {
                Log("[alert] " + title + " / " + body);
                return;
            }
            try
            {
                CustomUIManager.Window(AlertWindowId, title, CustomUIManager.LAYER_OVERLAY)
                    .UseMetaPrefabs(true)
                    .AddWarning(body)
                    .AddButton(L("debug_alert_ok"),
                        () => CustomUIManager.Instance.CloseWindow(AlertWindowId))
                    .SetSize(420, 180)
                    .Center()
                    .SetCloseOnEscape()
                    .Show();
            }
            catch (Exception e)
            {
                Log("alert window failed: " + e.Message);
                Log("[alert] " + title + " / " + body);
            }
        }

        /// <summary>刷新调试窗口状态行；窗口/元素不存在时退回日志（不重建窗口，避免丢焦点）。</summary>
        static void SetStatus(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                if (CustomUIManager.Instance == null) return;
                CustomUIElement element =
                    CustomUIManager.Instance.GetElement(SpawnWindowId, StatusId);
                if (element != null) element.SetText(text);
                else ChemRuntime.Log("[debug] status: " + text);
            }
            catch (Exception e)
            {
                Log("status refresh failed: " + e.Message);
            }
        }

        static string L(string key, params object[] args)
            => ModHelper.GetLocalized(ChemCatalog.ModId, key, args);

        static void Log(string msg) => ChemRuntime.Log("[debug] " + msg);
    }
}
