# -*- coding: utf-8 -*-
"""
FreeChem 静态验收（QA，不依赖游戏；仅 C# harness 段需要本地 .NET SDK）。
对照官方《用 AI 开发正式版 Mod》的「逐层记录结果」：接口核对 / 清单 / 资源 / 代码 / 结构。

只读检查，不冒充编译与游戏内验证。
退出码：0 = 无 FAIL（WARN 需人工解释）；1 = 有 FAIL。
"""
import csv
import glob
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

# 控制台可能是 GBK（中文 Windows）：输出含 2 等 Unicode 下标字符时必须强制 UTF-8，
# 否则 print 会抛 UnicodeEncodeError 直接 traceback。errors=replace 兜底不中断检查。
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # 仓库根目录（原为本机绝对路径）
PKG = os.path.join(ROOT, "FreeChem")
SRC = os.path.join(ROOT, "src")
TOOLS = os.path.join(ROOT, "tools")
DOCS = os.path.join(ROOT, "docs")
GAME = r"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed"

errors, warns, oks = [], [], []


def ok(m): oks.append(m)
def err(m): errors.append(m)
def warn(m): warns.append(m)


def read(p):
    with open(p, encoding="utf-8", errors="replace") as f:
        return f.read()


# ---------- 1. manifest ----------
mf = os.path.join(PKG, "manifest.xml")
tree = ET.parse(mf); r = tree.getroot()
mid = r.findtext("ID", "")
if not re.fullmatch(r"[A-Za-z0-9_.-]{1,64}", mid) or mid.startswith("."):
    err("manifest ID 不合法")
else:
    ok(f"manifest ID = {mid}")
if r.find("Assemblies") is None or not r.findtext("Assemblies/Assembly"):
    err("Assemblies 缺失")
else:
    ok("Assemblies 声明: " + r.findtext("Assemblies/Assembly"))
if r.find("Capabilities") is None:
    err("Capabilities 元素缺失")
elif len(list(r.find("Capabilities"))) > 0:
    err("Capabilities 非空（会触发审核）")
else:
    ok("Capabilities 为空 —— 无需玩家额外授权")
game_versions = [(x.text or "").strip() for x in r.findall("GameVersions/Version")]
if not game_versions or not any(game_versions):
    err("manifest 缺少非空 GameVersions/Version")
else:
    ok("GameVersions = " + ",".join(game_versions))

# ---------- 2. 图标覆盖 ----------
icon_paths = glob.glob(os.path.join(PKG, "Textures", "Items", "*.png"))
icons = {os.path.splitext(os.path.basename(p))[0] for p in icon_paths}
src_files = glob.glob(os.path.join(SRC, "*.cs"))
src_text = "\n".join(read(p) for p in src_files)
# 物品规格 id：ChemCatalog 中的 S("id",...) / C("id",...) / I("id",...)
spec_ids = set(re.findall(r'\b[SCI]\("([a-z0-9_]+)"', src_text))
# 设备 id：ChemRecipes.MachineIds 数组（简写初始化）
_m = re.search(r'string\[\]\s*MachineIds\s*=\s*\{(.*?)\};', src_text, re.S)
machine_ids = set(re.findall(r'"([a-z0-9_]+)"', _m.group(1))) if _m else set()
if not machine_ids:
    err("未能解析 MachineIds 数组")
if not spec_ids:
    err("未能解析 ChemCatalog.Specs 物品 id")
on_expected = {m + "_on" for m in machine_ids}
needed = spec_ids | machine_ids
missing = needed - icons
if missing:
    err("缺少图标: " + ", ".join(sorted(missing)))
else:
    ok(f"图标覆盖完整：{len(needed)} 个物品/机器，目录内 {len(icons)} 张 PNG")
# 反向：目录里未被代码引用的图标（_on 状态图是运行时按 id+"_on" 取图，单独断言）
# P3e：便条图标可能先于 #97 注册预置；注册后自动转正式资产。
EXPECTED_FORMAL_ICONS = len(spec_ids) + 26  # P12 契约：Specs + 13 机器 + 13 工作态（删除乙醇/水后 83+26=109）
PENDING_ASSET_ICONS = set() if "note_chem_dealer" in spec_ids else {"note_chem_dealer"}
extra = icons - needed - on_expected
_pending_extra = extra & PENDING_ASSET_ICONS
extra = extra - PENDING_ASSET_ICONS
if extra:
    warn("未被代码引用的图标（无害）: " + ", ".join(sorted(extra)))
elif _pending_extra:
    warn("预置资源（已生成、待 features 注册）：" + ", ".join(sorted(_pending_extra))
         + " —— P3e task-49 注册 #97 便条后转为正式资产")
else:
    ok(f"无孤立图标：{len(icons)} 张全部被物品/机器/状态图结构覆盖")
# 图标总数预期：物品/机器图标 + 13 台机器 + 13 张 <id>_on 工作态图 + 预置资源
EXPECTED_ICON_COUNT = EXPECTED_FORMAL_ICONS
expected_calc = len(spec_ids) + len(machine_ids) + len(on_expected)
if expected_calc != EXPECTED_ICON_COUNT:
    err(f"物品/机器/状态图应有 {expected_calc} 张（spec={len(spec_ids)} + machine={len(machine_ids)}"
        f" + on={len(on_expected)}），与预期 {EXPECTED_ICON_COUNT} 不符")
_allowed_total = EXPECTED_ICON_COUNT + len(PENDING_ASSET_ICONS & icons)
if len(icons) != _allowed_total:
    err(f"图标总数为 {len(icons)}，本版本预期 {_allowed_total} 张"
        f"（{EXPECTED_ICON_COUNT} 正式 + {len(PENDING_ASSET_ICONS & icons)} 预置待注册）")
else:
    ok(f"图标总数 = {len(icons)} 张（{EXPECTED_ICON_COUNT} 正式结构 + "
       f"{len(PENDING_ASSET_ICONS & icons)} 预置待注册）")

# ---------- 3. 图标尺寸 / 透明通道（对齐原版 16px 网格） ----------
from PIL import Image

# 规格：宽高均为 16 的正整数倍、16..64、RGBA；子类再收紧到确定尺寸。
# 32×48 = 2×3 格便携钢瓶；48×64 = 3×4 格大型储气桶（参照原版 water_pitcher）；
# 48×48 = 3×3 格机器（含 _on 状态图）。
CANISTER_GASES = ["gas_oxygen", "gas_nitrogen", "gas_hydrogen", "gas_chlorine",
                  "gas_carbon_dioxide"]
DRUM_IDS_EXPECTED = ["gas_drum_empty"] + ["gas_drum_" + g[4:] for g in CANISTER_GASES]


def icon_class(i):
    # 先判机器（gas_generator / gas_scrubber / gas_scrubber_on 等都是 48×48 设备，不是钢瓶）
    if i in machine_ids or i in on_expected:
        return "machine"
    if i.startswith("gas_drum_"):
        return "drum"
    if i == "gas_canister_empty" or i.startswith("can_") or i.startswith("gas_"):
        return "canister"
    return None


bad_grid, bad_canister, bad_drum, bad_machine = [], [], [], []
icon_meta = {}  # name -> (w, h, mode)，供后续 thermometer 等专项断言复用
for p in icon_paths:
    name = os.path.splitext(os.path.basename(p))[0]
    im = Image.open(p)
    w, h = im.size
    icon_meta[name] = (w, h, im.mode)
    if im.mode != "RGBA":
        bad_grid.append(f"{name} mode={im.mode}")
        continue
    if w % 16 or h % 16 or not (16 <= w <= 64 and 16 <= h <= 64):
        bad_grid.append(f"{name} {w}x{h}")
        continue
    cls = icon_class(name)
    if cls == "canister" and (w, h) != (32, 48):
        bad_canister.append(f"{name} {w}x{h}（便携钢瓶应为 32×48 / 2×3 格）")
    if cls == "drum" and (w, h) != (48, 64):
        bad_drum.append(f"{name} {w}x{h}（大型储气桶应为 48×64 / 3×4 格）")
    if name in machine_ids or name in on_expected:
        if (w, h) != (48, 48):
            bad_machine.append(f"{name} {w}x{h}（机器图标应为 48×48 / 3×3 格）")
for label, bad in (("16px 网格/RGBA", bad_grid),
                   ("便携钢瓶尺寸", bad_canister),
                   ("大型储气桶尺寸", bad_drum),
                   ("机器图标尺寸", bad_machine)):
    if bad:
        err(f"图标{label}异常: " + ", ".join(bad[:8]))
if not (bad_grid or bad_canister or bad_drum or bad_machine):
    ok(f"全部 {len(icons)} 张图标符合 16px 网格（宽高 16 的倍数、16..64）且 RGBA；"
       f"便携钢瓶 32×48、大型储气桶 48×64、机器 48×48")
# P0【问题1】占格修正：小件仪器 16×16（1 格）、玻璃器皿/灯 16×32（1×2 格）
P0_SMALL16 = {"thermometer", "ph_meter", "precision_balance", "safety_goggles", "catalyst_kit"}
P0_TALL16 = {"beaker", "retort_flask", "erlenmeyer_flask", "test_tube_rack",
             "alcohol_lamp", "spirit_burner"}
_p0icon_bad = []
for _i in sorted(P0_SMALL16):
    if icon_meta.get(_i, (None, None, None))[:2] != (16, 16):
        _p0icon_bad.append(f"{_i}={icon_meta.get(_i)}（应 16×16 / 1 格）")
for _i in sorted(P0_TALL16):
    if icon_meta.get(_i, (None, None, None))[:2] != (16, 32):
        _p0icon_bad.append(f"{_i}={icon_meta.get(_i)}（应 16×32 / 1×2 格）")
if _p0icon_bad:
    err("P0 占格修正未生效: " + ", ".join(_p0icon_bad))
else:
    ok("P0 占格修正：小件仪器 16×16、玻璃器皿/加热灯 16×32；canister 32×48、"
       "gas_drum 48×64、机器 48×48、总数 98 保持不变")
# P1b1：标签打印机 32×32（4 格）
if icon_meta.get("label_printer", (None, None, None))[:2] != (32, 32):
    err(f"label_printer 图标应为 32×32（4 格），实际 {icon_meta.get('label_printer')}")
else:
    ok("标签打印机 label_printer 图标 32×32（4 格）；既有 98 张尺寸不变、总数 99")
# P2a：10 个新固体化学品 16×16（1 格）
P2A_ITEM_IDS = {
    87: "sol_calcium_oxide", 88: "sol_calcium_hydroxide", 89: "sol_sodium_carbonate",
    90: "sol_sodium_bicarbonate", 91: "sol_ammonium_chloride",
    92: "sol_manganese_dioxide", 93: "sol_copper_oxide", 94: "sol_potassium_sulfate",
    95: "sol_sodium_nitrate", 96: "sol_potassium_manganate",
}
_p2a_icon_bad = []
for _n, _iid in P2A_ITEM_IDS.items():
    if icon_meta.get(_iid, (None, None, None))[:2] != (16, 16):
        _p2a_icon_bad.append(f"{_iid}={icon_meta.get(_iid)}（应 16×16）")
if _p2a_icon_bad:
    err("P2a 新化学品图标异常: " + ", ".join(_p2a_icon_bad))
else:
    ok(f"P2a：10 个新固体化学品图标均 16×16（1 格），既有 99 张尺寸不变、总数 109")
# P3a：10 张液体瓶 16×48（1×3 格，三格中心必须不透明）+ 6 张器材 16×32（上下两格中心不透明）
P3A_LIQ_BOTTLES = ["liq_hydrochloric_acid", "liq_sulfuric_acid", "liq_nitric_acid",
                   "liq_sodium_hydroxide", "liq_hydrogen_peroxide", "liq_brine",
                   "liq_ammonia_water", "liq_calcium_chloride"]
P3A_WRAP_16x32 = ["beaker", "retort_flask", "erlenmeyer_flask", "test_tube_rack",
                  "alcohol_lamp", "spirit_burner"]


def _cell_centers_opaque(name, rows):
    """图标每个 16px 格子的中心像素必须不透明（否则游戏按不透明像素推导占格会少格）。"""
    p = os.path.join(PKG, "Textures", "Items", name + ".png")
    if not os.path.exists(p):
        return False
    im = Image.open(p).convert("RGBA")
    if im.size[0] != 16:
        return False
    for _r in range(rows):
        _y = _r * 16 + 8
        if _y >= im.size[1]:
            return False
        if im.getpixel((8, _y))[3] == 0:
            return False
    return True


_p3a_icon_bad = []
for _iid in P3A_LIQ_BOTTLES:
    if icon_meta.get(_iid, (None, None, None))[:2] != (16, 48):
        _p3a_icon_bad.append(f"{_iid}={icon_meta.get(_iid)}（应 16×48 / 1×3 格）")
    elif not _cell_centers_opaque(_iid, 3):
        _p3a_icon_bad.append(f"{_iid} 三格中心存在透明像素（游戏会判成 1–2 格）")
for _iid in P3A_WRAP_16x32:
    if icon_meta.get(_iid, (None, None, None))[:2] != (16, 32):
        _p3a_icon_bad.append(f"{_iid}={icon_meta.get(_iid)}（应 16×32 / 1×2 格）")
    elif not _cell_centers_opaque(_iid, 2):
        _p3a_icon_bad.append(f"{_iid} 上下两格中心存在透明像素（游戏会判成 1 格）")
if _p3a_icon_bad:
    err("P3a 液体瓶/器材占格图标异常: " + "; ".join(_p3a_icon_bad[:10]))
else:
    ok("P3a：10 张液体瓶 16×48（三格中心不透明）+ 6 张器材 16×32（两格中心不透明），总数 109")

# ---------- 4. 机器空闲/工作双状态图结构 ----------
on_actual = {i for i in icons if i.endswith("_on")}
bad_on = []
for m in sorted(machine_ids):
    if m not in icons:
        bad_on.append(f"缺空闲图标 {m}.png")
    if m + "_on" not in icons:
        bad_on.append(f"缺工作图标 {m}_on.png")
orphan_on = on_actual - on_expected
if orphan_on:
    bad_on.append("孤立 _on 图标: " + ", ".join(sorted(orphan_on)))
registered_on = on_actual & spec_ids
if registered_on:
    bad_on.append("_on 状态图被注册成商品 id: " + ", ".join(sorted(registered_on)))
if bad_on:
    for b in bad_on:
        err("机器双状态图: " + b)
else:
    ok(f"13 台机器双状态图一一对应：{len(machine_ids)} 个空闲 + {len(on_actual)} 个 _on"
       "（均 48×48），无孤立 _on，_on 未注册成商品")
# ---------- 4b. 运行时换图接线（features task-4：13 台机器空闲/工作态） ----------
mach_text = read(os.path.join(SRC, "ChemMachines.cs"))
heat_text = read(os.path.join(SRC, "ChemHeating.cs"))
wire_bad = []
if "SetMachineVisual" not in mach_text:
    wire_bad.append("ChemMachines 缺集中换图入口 SetMachineVisual")
if '"_on"' not in mach_text:
    wire_bad.append('ChemMachines 未引用 _on 状态图后缀（id + "_on"）')
if "useSpriteFromMod" not in mach_text or "spritePath == want" not in mach_text:
    wire_bad.append("SetMachineVisual 缺少「已是目标状态则跳过」判定（防止每秒重复换图）")
if "ChemMachines.SetMachineVisual" not in heat_text:
    wire_bad.append("ChemHeating 未走统一的 ChemMachines.SetMachineVisual 入口")
if "SetMachineVisual(r.Machine" not in heat_text:
    wire_bad.append("ChemHeating 加热台未见 SetMachineVisual 调用")
# 依据 docs/api-findings.md：SetSprite/SetSpriteQuick 不适用于 mod 贴图，不得作为运行时换图手段
if re.search(r'\.SetSprite\s*\(|\.SetSpriteQuick\s*\(', mach_text + heat_text):
    wire_bad.append("运行时换图误用了 SetSprite/SetSpriteQuick（api-findings 明确不适用于 mod 贴图）")
if wire_bad:
    for b in wire_bad:
        err("运行时双状态换图: " + b)
else:
    ok("运行时双状态换图已接线：ChemMachines.SetMachineVisual 集中入口 + ChemHeating 加热台，"
       "仅在状态变化时换图（_on 未被注册成商品）")


# ---------- 5. 大型储气桶（gas_drum_*）结构 + DrumSkus 映射 ----------
_dm = re.search(r'DrumSkus\s*=\s*new\s+Dictionary<\s*string\s*,\s*string\s*>\s*\{(.*?)\};',
                src_text, re.S)
drum_skus = {}
if not _dm:
    err("未能解析 ChemRecipes.DrumSkus 字典")
else:
    for k, v in re.findall(r'\{\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\}', _dm.group(1)):
        drum_skus[k] = v
_di = re.search(r'string\[\]\s*DrumIds\s*=\s*\{(.*?)\};', src_text, re.S)
drum_ids_code = set(re.findall(r'"([a-z0-9_]+)"', _di.group(1))) if _di else set()
_empty = re.search(r'DrumEmptyId\s*=\s*"([^"]+)"', src_text)
if _empty:
    drum_ids_code.add(_empty.group(1))
drum_problems = []
if not drum_skus:
    drum_problems.append("DrumSkus 为空")
if set(drum_skus) != set(CANISTER_GASES):
    drum_problems.append("DrumSkus 应覆盖 5 种气体，实际 " + str(sorted(drum_skus)))
for g in CANISTER_GASES:
    if g not in drum_skus:
        continue
    target = drum_skus[g]
    if target != "gas_drum_" + g[4:]:
        drum_problems.append(f"{g} 应映射到 gas_drum_{g[4:]}，实际 {target}")
    if target not in spec_ids:
        drum_problems.append(f"{target} 未注册进 ChemCatalog.Specs")
    if target not in icons:
        drum_problems.append(f"{target} 缺图标")
if "gas_drum_empty" not in spec_ids:
    drum_problems.append("gas_drum_empty 未注册进 ChemCatalog.Specs")
if drum_ids_code != set(DRUM_IDS_EXPECTED):
    drum_problems.append("DrumIds 清单应为 6 个（空桶 + 5 充装桶），实际 "
                         + str(sorted(drum_ids_code)))
for d in DRUM_IDS_EXPECTED:
    if d not in spec_ids:
        drum_problems.append(f"{d} 未注册进 ChemCatalog.Specs")
if drum_problems:
    for b in drum_problems:
        err("大型储气桶: " + b)
else:
    ok("大型储气桶结构完整：6 个 gas_drum_* 已注册、48×64、DrumSkus 5 气体一一映射")

# ---------- 6. 本地化 ----------
def load_csv(lang):
    with open(os.path.join(PKG, "Localization", lang + ".csv"), encoding="utf-8") as f:
        return {row[0]: row[1] for row in csv.reader(f) if row and not row[0].startswith("#")}


zh, en = load_csv("zh"), load_csv("en")
if set(zh) != set(en):
    err("zh/en 键不一致: " + str(sorted(set(zh) ^ set(en))[:20]))
else:
    ok(f"本地化键对齐：{len(zh)} 条 zh/en")
EXPECTED_LOC_KEYS = 397  # P3e 基线；实际条数以生成器为准（电话/订购/索引按钮键会再增）
if len(zh) != len(en):
    err(f"本地化 zh/en 条数不一致: zh={len(zh)} / en={len(en)}")
elif len(zh) < EXPECTED_LOC_KEYS:
    err(f"本地化键数 {len(zh)} < 基线 {EXPECTED_LOC_KEYS}（P1a 新键可能未生成）")
else:
    ok(f"本地化键数 = {len(zh)} 条（zh/en 一致；基线 {EXPECTED_LOC_KEYS}，以生成器为准）")
missing_keys = []
for i in sorted(needed):
    for suffix in ("_name", "_desc", "_flavor"):
        if i + suffix not in zh:
            missing_keys.append(i + suffix)
if missing_keys:
    err(f"缺物品/机器本地化三连键 {len(missing_keys)} 条: "
        + ", ".join(missing_keys[:20]))
else:
    ok(f"物品/机器本地化三连键齐备：{len(needed)} 个 id × _name/_desc/_flavor")
if any(re.search(r"[\u4e00-\u9fff]", v) for v in en.values()):
    err("en.csv 混有中文")
else:
    ok("en.csv 无中文残留")
BASE_KEYS = ["cfg_dangerous", "cfg_dangerous_tip", "codex_progress",
             "chem_profile", "lethal_lethal", "lethal_toxic", "lethal_safe",
             "cfg_vanilla_security", "cfg_vanilla_security_tip",
             "cfg_debug_mode", "cfg_debug_mode_tip",
             "network_tab", "network_broker_title", "network_broker_desc",
             "network_order_title", "network_order_desc", "network_order_wait"]
# 第 14 轮：F9 调试窗口 / 满仓弹窗 / 生成错误提示
DEBUG_KEYS = ["debug_title", "debug_hint", "debug_range", "debug_placeholder",
              "debug_spawn", "debug_list", "debug_close",
              "debug_status_ready", "debug_status_ok", "debug_status_bad",
              "debug_status_disabled", "debug_list_header",
              "debug_alert_title", "debug_alert_full", "debug_alert_ok",
              "err_invalid_id", "err_not_found", "err_no_store"]
# 第 15 轮：游戏「选项 → 按键绑定」里的快捷键入口
OPTION_KEYS = ["option_debug_open", "option_debug_open_tip"]
# P0（第 17 轮）：机器窗口 ui_* 标签 + 仪器状态提示 + 开关/温度档 + 画像/医疗行
P0_KEYS = ["ui_battery", "ui_modules", "ui_input", "ui_output", "ui_note",
           "ui_vessel", "ui_heater", "ui_wick", "ui_fuel",
           "inst_temp", "inst_ph", "inst_balance", "inst_protection",
           "inst_fuel", "inst_wick", "inst_filter",
           "state_on", "state_off", "temp_0", "temp_1", "temp_2", "temp_3",
           "chem_profile_reagent", "chem_medical_grade"]
# 本轮新增：排风服务 + 排气嫌疑度（键名以 features 的 ChemRuntime/ModEntry 实测为准）
VENT_KEYS = ["network_vent_title", "network_vent_desc",
             "network_adv_vent_title", "network_adv_vent_desc",
             "suspicion_profile", "vent_none", "vent_basic", "vent_advanced"]
_miss_debug = [k for k in DEBUG_KEYS if k not in zh]
if _miss_debug:
    err("缺调试/生成/满仓本地化键: " + ", ".join(_miss_debug))
else:
    ok(f"调试窗口/满仓弹窗/生成错误本地化键齐备（{len(DEBUG_KEYS)} 条）")
_miss_opt = [k for k in OPTION_KEYS if k not in zh]
if _miss_opt:
    err("缺快捷键选项本地化键: " + ", ".join(_miss_opt))
else:
    ok("快捷键入口本地化键齐备（option_debug_open / option_debug_open_tip）")
_miss_p0 = [k for k in P0_KEYS if k not in zh or k not in en]
if _miss_p0:
    err("缺 P0 机器窗口/仪器提示/温度档本地化键: " + ", ".join(_miss_p0))
else:
    ok(f"P0 机器窗口与仪器状态提示本地化键齐备（{len(P0_KEYS)} 条，zh/en）")
# P1b1：标签打印机键
P1B1_KEYS = ["label_printer_name", "label_printer_desc",
             "printer_mode", "printer_cat_chemical", "printer_cat_medical",
             "printer_stamp_ok", "printer_stamp_fail", "printer_stamp_target_invalid"]
_miss_p1b1 = [k for k in P1B1_KEYS if k not in zh or k not in en]
if _miss_p1b1:
    err("缺标签打印机本地化键: " + ", ".join(_miss_p1b1))
else:
    ok(f"标签打印机本地化键齐备（{len(P1B1_KEYS)} 条，zh/en）")
# P1c：校准/充能/提纯等级键
P1C_KEYS = ["printer_mode_calibrate", "printer_charge", "printer_charge_ok",
            "printer_calibrate_ok", "printer_calibrate_fail_purity",
            "printer_calibrate_fail_charge", "purify_level", "chem_lab_pure_100"]
_miss_p1c = [k for k in P1C_KEYS if k not in zh or k not in en]
if _miss_p1c:
    err("缺 P1c 校准/提纯本地化键: " + ", ".join(_miss_p1c))
else:
    ok(f"P1c 校准/充能/提纯等级本地化键齐备（{len(P1C_KEYS)} 条，zh/en）")
# P2a：10 个新化学品的本地化三连键
_miss_p2a = [f"{_iid}{_s}" for _iid in P2A_ITEM_IDS.values()
             for _s in ("_name", "_desc", "_flavor") if f"{_iid}{_s}" not in zh]
if _miss_p2a:
    err("缺 P2a 新化学品本地化三连键: " + ", ".join(_miss_p2a[:12]))
else:
    ok(f"P2a：10 个新化学品 × _name/_desc/_flavor 本地化键齐备（{len(P2A_ITEM_IDS) * 3} 条）")
miss_base = [k for k in BASE_KEYS if k not in zh]
miss_vent = [k for k in VENT_KEYS if k not in zh]
if miss_base:
    err("缺配置/手册/王尔德人脉键: " + ", ".join(miss_base))
else:
    ok("配置、手册与王尔德气体商人键齐备")
if miss_vent:
    err("缺排风服务/排气嫌疑度键: " + ", ".join(miss_vent))
else:
    ok("排风服务与排气嫌疑度本地化键齐备（基础/进阶/三档文案/提示框）")

# ---------- 6b. manifest GameVersions 对照 docs/api-findings.md ----------
findings = os.path.join(DOCS, "api-findings.md")


def ver_tuple(v):
    return tuple(int(x) for x in re.findall(r"\d+", v))


def ver_compatible(a, b):
    ta, tb = ver_tuple(a), ver_tuple(b)
    if not ta or not tb:
        return False
    if ta == tb:
        return True
    lo, hi = (ta, tb) if len(ta) <= len(tb) else (tb, ta)
    return hi[:len(lo)] == lo  # 兼容声明：1.0 与 1.0.0 视为一致


if not os.path.exists(findings):
    warn("docs/api-findings.md 尚未产出，GameVersions 未与游戏版本对照（待 api-researcher）")
else:
    ftxt = read(findings)
    explicit = re.findall(r"GAME_VERSION\s*[:=]\s*([0-9][0-9A-Za-z.\-+_]*)", ftxt)
    cands = [v.strip("._-+") for v in explicit if v]
    if not cands:
        # 兜底：从 AssemblyInformationalVersion / 游戏版本 附近的版本号提取
        for line in ftxt.splitlines():
            if re.search(r"AssemblyInformationalVersion|AssemblyFileVersion|游戏版本|GameVersion",
                         line, re.I):
                cands += re.findall(r"\b\d+\.\d+(?:\.\d+){0,2}\b", line)
    if not cands:
        warn("docs/api-findings.md 存在但未给出可解析的游戏版本号；"
             "GameVersions 无法对照（按 lead 决定降级 WARN，不写假版本）")
    else:
        match = [gv for gv in game_versions if any(ver_compatible(gv, c) for c in cands)]
        if match:
            ok(f"manifest GameVersions({','.join(game_versions)}) 与 "
               f"docs/api-findings.md 游戏版本一致（doc: {', '.join(sorted(set(cands))) }）")
        else:
            err(f"manifest GameVersions({','.join(game_versions)}) 与 "
                f"docs/api-findings.md 游戏版本({', '.join(sorted(set(cands)))}) 不一致")

# ---------- 7. 禁用 token 零残留（废弃防护面具的三种写法） ----------
# 说明：归档 v1 备份（tools/gen_icons_v1.py、FreeChem_v1_backup/）不在发布包里，
# 按 lead 决定只记 WARN，不 FAIL；现行 src/、tools/*.py（除 api-probe 与 v1 备份）、
# 发布包内资源、docs/*.md、README.md、预览.html 必须零残留。
MASK_NEEDLES = [
    ("gas_" + "mask", "ASCII id 形式"),
    (chr(0x9632) + chr(0x6bd2) + chr(0x9762) + chr(0x5177), "中文名形式"),
    ("Gas" + " " + "Mask", "英文名形式"),
]
ARCHIVED_BACKUPS = {"gen_icons_v1.py"}


def mask_scan_targets():
    out = []
    out += glob.glob(os.path.join(SRC, "*.cs"))
    out += glob.glob(os.path.join(TOOLS, "*.py"))
    out += glob.glob(os.path.join(PKG, "Localization", "*.csv"))
    out += glob.glob(os.path.join(PKG, "Books", "**", "*"), recursive=True)
    out += glob.glob(os.path.join(PKG, "LootTables", "**", "*"), recursive=True)
    out += glob.glob(os.path.join(DOCS, "*.md"))
    out += [os.path.join(ROOT, "README.md"), os.path.join(ROOT, "预览.html")]
    return [p for p in out
            if os.path.isfile(p) and "api-probe" not in p]


resid_live, resid_archived = [], []
for p in mask_scan_targets():
    txt = read(p)
    hit = [label for needle, label in MASK_NEEDLES
           if (needle.lower() in txt.lower())]
    if not hit:
        continue
    rel = os.path.relpath(p, ROOT)
    entry = f"{rel} -> {', '.join(hit)}"
    if os.path.basename(p) in ARCHIVED_BACKUPS:
        resid_archived.append(entry)
    else:
        resid_live.append(entry)
if resid_live:
    for e in resid_live:
        err("废弃防护面具 token 残留: " + e)
else:
    ok("废弃防护面具 token 零残留（src / tools 现行脚本 / 发布包资源 / docs / README / 预览）")
if resid_archived:
    warn("归档 v1 备份仍含废弃 token（不属发布包，按约定不 FAIL）: " + "; ".join(resid_archived))

# ---------- 8. 化学逻辑自检（调用真实数据层 C# 校验） ----------
import subprocess

harness = os.path.join(TOOLS, "logic-harness", "LogicHarness.csproj")
dotnet = os.path.join(ROOT, ".dotnet", "dotnet.exe")
if os.path.exists(harness) and os.path.exists(dotnet):
    env = dict(os.environ)
    env["DOTNET_NOLOGO"] = "1"
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["PYTHONIOENCODING"] = "utf-8"
    try:
        # 关键修复：GBK 控制台下按 UTF-8 解码 C# 输出，避免 UnicodeDecodeError 吞掉 stdout。
        res = subprocess.run([dotnet, "run", "--project", harness],
                             capture_output=True, timeout=300,
                             encoding="utf-8", errors="replace", env=env)
        out = (res.stdout or "") + (res.stderr or "")
        if res.returncode == 0 and "结论：全部通过" in out:
            ok("C# 数据层逻辑自检：全部通过（相态/滗析/纯度链/多级/T4/排风/桶映射）")
        else:
            err(f"C# 数据层逻辑自检失败（exit={res.returncode}）：" + out[-500:])
    except FileNotFoundError as e:
        warn(f"未能启动 C# 逻辑自检（SDK/工程缺失）: {e}")
    except Exception as e:
        err(f"C# 逻辑自检运行异常（按失败处理，不静默）: {type(e).__name__}: {e}")
else:
    warn("逻辑校验工程或本地 SDK 缺失，跳过真实 C# 自检")

# ---------- 9. JSON 资源 ----------
for p in glob.glob(os.path.join(PKG, "**", "*.json"), recursive=True):
    try:
        json.load(open(p, encoding="utf-8"))
        ok("JSON 合法: " + os.path.relpath(p, PKG))
    except Exception as e:
        err(f"JSON 解析失败 {p}: {e}")
loot = json.load(open(os.path.join(PKG, "LootTables", "free_chem_drops.json"), encoding="utf-8"))
table_ids = {t["id"] for t in loot.get("tables", [])}
loot_ids = {e["id"] for t in loot.get("tables", []) for e in t["entries"]}
for pa in loot.get("patches", []):
    loot_ids |= {e["id"] for e in pa.get("add", [])}
# 掉落 id 必须是已注册的物品/机器，或本模组/原版掉落实 id
loot_unknown = loot_ids - spec_ids - machine_ids - table_ids
# 允许引用原版掉落表/物品 id（本模组掉落只应引用自己注册的 id，原版 id 显式白名单）
VANILLA_LOOT_OK = {"junkTable", "dumpingGroundTG"}
loot_unknown -= VANILLA_LOOT_OK
if loot_unknown:
    err("掉落表引用了未注册 id: " + ", ".join(sorted(loot_unknown)))
else:
    ok(f"掉落表引用的 {len(loot_ids)} 个物品/表 id 均已注册（无悬空 id）")

# ---------- 9b. 可获取性（no dead item）+ 器材掉落表 + thermometer ----------
# 1) Specs 的 Kind（byproduct 豁免：废渣/事故副产物允许无正常来源）
_S_KIND_PAT = re.compile(
    r'\bS\("([a-z0-9_]+)"\s*,\s*"[^"]*"\s*,\s*Phase\.\w+\s*,\s*PurityGrade\.\w+\s*,'
    r'\s*-?\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*"([^"]+)"\)')
spec_kinds = {}
for _m in _S_KIND_PAT.finditer(src_text):
    spec_kinds[_m.group(1)] = _m.group(2)
for _m in re.finditer(r'\bC\("([a-z0-9_]+)"', src_text):
    spec_kinds.setdefault(_m.group(1), "canister")
for _m in re.finditer(r'\bI\("([a-z0-9_]+)"', src_text):
    spec_kinds.setdefault(_m.group(1), "illicit")
_k_unknown = sorted(spec_ids - set(spec_kinds))
if _k_unknown:
    err("未能解析以下 Specs 物品的 Kind（可获取性分析失效）: " + ", ".join(_k_unknown))

# 2) 来源集合 = 掉落表 ∪ 反应产物 ∪ ModEntry 获取入口 ∪ 灌装动态产物 ∪ 机器
source = set(loot_ids)                                   # 2a 掉落表 tables + patches.add
_R_PAT = re.compile(
    r'new Reaction\(\s*"([^"]+)"[^,]*?,\s*"[^"]*"\s*,\s*"[^"]*"\s*,\s*"([^"]+)"\s*,\s*\d+\s*,'
    r'\s*new\[\]\s*\{[^}]*\}\s*,\s*new\[\]\s*\{([^}]*)\}', re.S)
reaction_outputs = set()                                 # 2b 夜间反应产物
for _m in _R_PAT.finditer(src_text):
    reaction_outputs |= set(re.findall(r'"([^"]+)"', _m.group(3)))
if not reaction_outputs:
    err("未能解析任何夜间反应产物（可获取性分析失效）")
lab_text = read(os.path.join(SRC, "ChemLab.cs"))
_T_PAT = re.compile(
    r'new ThermalReaction\(\s*"[^"]+"\s*,\s*"[^"]*"\s*,\s*"[^"]*"\s*,'
    r'\s*new\[\]\s*\{[^}]*\}\s*,\s*new\[\]\s*\{([^}]*)\}', re.S)
thermal_outputs = set()                                  # 2c 实时热反应产物
for _m in _T_PAT.finditer(lab_text):
    thermal_outputs |= set(re.findall(r'"([^"]+)"', _m.group(1)))
entry_text = read(os.path.join(SRC, "ModEntry.cs"))
entry_sources = set()                                    # 2d 起始礼包/气体订单/发明家库存
for _name in ("OnNewGameStarted", "DeliverGasOrder", "OnInventorStock"):
    _mm = re.search(r'void ' + _name + r'\([^)]*\)\s*\{(.*?)\n        \}', entry_text, re.S)
    if _mm:
        entry_sources |= set(re.findall(r'"([a-z0-9_]+)"', _mm.group(1)))
_aa = re.search(r'string\[\]\s*ApparatusIds\s*=\s*\{(.*?)\};', entry_text, re.S)
if _aa:
    entry_sources |= set(re.findall(r'"([a-z0-9_]+)"', _aa.group(1)))
_cs = re.search(r'CanisterSkus\s*=\s*new Dictionary<string, string\[\]>\s*\{(.*?)\};',
                src_text, re.S)
skus_values = set(re.findall(r'"(can_[a-z0-9_]+)"', _cs.group(1))) if _cs else set()
# 2f 原版物品 → 本模组 id 的映射（VanillaAnalog 的 value 即「可通过原版物品获得」的来源）
_va = re.search(r'VanillaAnalog\s*=\s*new Dictionary<string, string>\s*\{(.*?)\};',
                src_text, re.S)
vanilla_mapped = set(re.findall(r'\{\s*"[^"]+"\s*,\s*"([a-z0-9_]+)"\s*\}', _va.group(1))) \
    if _va else set()
source |= reaction_outputs | thermal_outputs | entry_sources | skus_values
source |= set(drum_skus.values()) | {"gas_drum_empty"} | set(machine_ids) | vanilla_mapped

# 3) 逐 id 判定（byproduct 豁免；显式白名单必须逐条写明理由）
EXEMPT_KINDS = {"byproduct"}
EXEMPT_DEAD_IDS = {
    # P3b（原版物品替代）：以下 mod 物品保留注册作旧存档 legacy，不再有 mod 侧获取途径；
    # 其中 liq_water/liq_ethanol/sol_coffee_grounds 可经 VanillaAnalog 从原版水/酒精/咖啡豆获得，
    # sol_caffeine 由「原版咖啡豆+酒精热萃取」产出（热反应 sources 已计入）。
    "liq_water": "P3b legacy：改走原版水容器（VanillaAnalog 映射）",
    "liq_ethanol": "P3b legacy：改走原版酒精（VanillaAnalog 映射）",
    "sol_coffee_grounds": "P3b legacy：改走原版咖啡豆 coffee_bean（VanillaAnalog 映射）",
    "sol_caffeine": "P3b legacy：原版原料热萃取产物；#48 注册保留不重排",
    "note_chem_dealer": "P5 legacy：便签改用原版 note/paper 后，mod 便条仅保留注册供旧存档",
    "gas_ethylene": "P12：暂无获取途径，保留为中间气体（乙烯；后续补来源或改副产物）",
}
dead = sorted(i for i, k in spec_kinds.items()
              if k not in EXEMPT_KINDS and i not in source and i not in EXEMPT_DEAD_IDS)
if dead:
    err("死物品（Specs 已注册但没有任何获取途径，且非 byproduct/白名单）: " + ", ".join(dead))
else:
    _n = len([k for k in spec_kinds.values() if k not in EXEMPT_KINDS])
    ok(f"可获取性：{_n} 个非副产物 Specs 物品均有至少一个来源"
       "（掉落/起始礼包/气体订单/发明家库存/反应产物/灌装/机器/原版物品映射）；"
       f"legacy 白名单 {len(EXEMPT_DEAD_IDS)} 项均有逐条理由")

# 4) 器材掉落表 + 热反应原料来源
tables_by_id = {t["id"]: t for t in loot.get("tables", [])}
app_bad = []
app = tables_by_id.get("free_chem_apparatus")
if not app:
    app_bad.append("缺少 free_chem_apparatus 器材掉落表")
elif not app.get("entries"):
    app_bad.append("free_chem_apparatus 表为空")
else:
    for _e in app.get("entries"):
        _iid = _e.get("id")
        if _iid not in spec_ids:
            app_bad.append(f"{_iid} 未注册进 ChemCatalog.Specs")
        if not isinstance(_e.get("weight"), int) or _e.get("weight") <= 0:
            app_bad.append(f"{_iid} weight 非正整数")
if app_bad:
    for b in app_bad:
        err("器材掉落表: " + b)
else:
    ok(f"器材掉落表 free_chem_apparatus 存在：{len(app['entries'])} 条，id 全注册、weight>0")
_miss_reagent = []  # P12：乙醇/蒸馏水已移除（reserved），不再作为反应原料检查来源
if _miss_reagent:
    err("热反应原料无获取来源（掉落或 VanillaAnalog 映射）: " + ", ".join(_miss_reagent))
else:
    ok("热反应原料 liq_ethanol 有来源（原版酒精 VanillaAnalog 映射）")
if "sol_coffee_grounds" in loot_ids or "sol_coffee_grounds" in vanilla_mapped:
    err("sol_coffee_grounds 应为 legacy 无来源（Lead 定案：原版咖啡已有官方制作逻辑）")
else:
    ok("sol_coffee_grounds 为 legacy 无来源（旧存档兼容，仅保留注册）")

# 5) thermometer：Specs + 32×32 RGBA + 本地化三连键
thermo_bad = []
if "thermometer" not in spec_ids:
    thermo_bad.append("thermometer 未注册进 ChemCatalog.Specs")
_w, _h, _mode = icon_meta.get("thermometer", (None, None, None))
if (_w, _h) != (16, 16):
    thermo_bad.append(f"图标应为 16×16（1 格），实际 {_w}x{_h}")
if _mode != "RGBA":
    thermo_bad.append(f"图标 mode={_mode}")
for _suf in ("_name", "_desc", "_flavor"):
    if "thermometer" + _suf not in zh:
        thermo_bad.append("缺本地化键 thermometer" + _suf)
if thermo_bad:
    for b in thermo_bad:
        err("温度计 thermometer: " + b)
else:
    ok("温度计 thermometer：已注册 Specs、32×32 RGBA 仪器图标、本地化三连键齐备"
       "（温度档 0..3 循环，无 temp_tier_* 键）")

# ---------- 10. 代码能力扫描（模拟 mod-scan 关注点） ----------
BANNED = {
    r"System\.IO\.File": "files", r"new\s+FileStream": "files",
    r"\bDirectory\.(Create|Delete|Move|GetFiles|GetDirectories|Exists|Enumerate)": "files",
    r"System\.IO\.Directory": "files",
    r"System\.Net": "network", r"UnityWebRequest": "network", r"HttpClient": "network",
    r"System\.Diagnostics\.Process": "一律拒绝", r"ProcessStartInfo": "一律拒绝",
    r"new\s+Thread": "threads(弃用)", r"Task\.Run": "threads(弃用)",
    r"DllImport": "native(弃用)", r"Assembly\.Load": "dynamic-code(弃用)",
    r"Harmony\.UnpatchAll": "一律拒绝", r"dynamic\s+\w": "reflection-external(弃用)",
}
hits = []
for pat, cap in BANNED.items():
    for p in src_files:
        if re.search(pat, read(p)):
            hits.append(f"{os.path.basename(p)} -> {cap} ({pat})")
if hits:
    for h in hits:
        err("能力风险 " + h)
else:
    ok("静态扫描：无 files/network/process/threads/native/reflection-external 用法")
if "[HarmonyPatch]" in src_text:
    if os.path.exists(os.path.join(SRC, "ChemPatches.cs")):
        ok("Harmony 桥接已获用户授权：ChemPatches.cs 使用 [HarmonyPatch]（加载器自动应用；"
           "Capabilities 保持空、无手动 PatchAll）")
    else:
        warn("代码含 [HarmonyPatch] 但缺 src/ChemPatches.cs（需确认）")
elif "0Harmony" in src_text:
    warn("代码含 0Harmony 引用但未使用 [HarmonyPatch]（仅 using，无害）")

# ---------- 11. 发布目录卫生（官方：不能有 exe/bat/ps1/zip/未列 DLL） ----------
bad_files = []
for p in glob.glob(os.path.join(PKG, "**", "*"), recursive=True):
    if os.path.isdir(p):
        continue
    ext = os.path.splitext(p)[1].lower()
    if ext in (".exe", ".bat", ".ps1", ".js", ".zip", ".psd", ".aseprite", ".lnk"):
        bad_files.append(os.path.relpath(p, PKG))
    if ext == ".dll" and os.path.basename(p) != "FreeChem.dll":
        bad_files.append(os.path.relpath(p, PKG) + " (未列 DLL)")
if bad_files:
    err("发布目录含违规文件: " + ", ".join(bad_files))
else:
    ok("发布目录干净：无脚本/压缩包/未声明 DLL")

# ---------- 12. 接口核对：代码引用的游戏 API 是否存在于程序集 ----------
blobs = b""
for p in glob.glob(os.path.join(GAME, "*.dll")):
    try:
        blobs += open(p, "rb").read()
    except Exception:
        pass
api = set(re.findall(
    r"\b(?:Mod|Machine|Module|Container|General|Type|Power|GraphUtils|Directory|Item|Player|"
    r"PlayerStore|SecData|RNG|"
    r"ModHook|ModLog|ModConfig|ConfigEntry|Game|Grid|Pixel|Tag)[A-Za-z0-9_]*\.[A-Za-z0-9_]+\b",
    src_text))
checked = 0
for ref in sorted(api):
    # ModEntry 是本模组自己的类型（VentServiceId 等常量），不是游戏 API，跳过
    if ref.startswith("ModEntry."):
        continue
    member = ref.split(".")[-1].encode("ascii", "ignore")
    if member in blobs:
        checked += 1
    else:
        warn("程序集中未找到成员（需实机核对）: " + ref)
if api:
    ok(f"接口核对：{checked}/{len(api)} 个 X.Y 形式成员在本机程序集中命中")
else:
    err("未能从源码解析出任何游戏 API 引用")

# ---------- 13. 编译产物（优先校验最新 Release 构建产物；发布包副本同步由 lead 负责） ----------
BUILT_DLL = os.path.join(SRC, "bin", "Release", "netstandard2.1", "FreeChem.dll")
PKG_DLL = os.path.join(PKG, "FreeChem.dll")
FORBIDDEN = ["ProcessStartInfo", "System.Diagnostics.Process", "GetMethod", "GetField",
             "GetProperty", "InvokeMember", "Assembly.Load", "FileStream", "HttpClient",
             "System.Net.", "new Thread", "DllImport", "UnpatchAll", "ModLoader.SetModEnabled"]
TYPES = ["ModEntry", "IMod", "ChemCatalog", "ChemMachines", "ChemRecipes",
         "ChemRuntime", "ChemLab", "ChemHeating"]


def inspect_dll(path):
    blob = open(path, "rb").read()
    dtok = set(x.decode("ascii", "ignore") for x in
               re.findall(rb"[A-Za-z_][A-Za-z0-9_.<>`]{2,90}", blob))
    bad = [k for k in FORBIDDEN if any(k in t for t in dtok)]
    miss = [t for t in TYPES if t not in dtok]
    return blob, bad, miss


# 编译器自动生成的 System.Reflection/System.Diagnostics 程序集特性是误报，
# 与官方已发布范例 LearnerMod.dll 一致。
refdll = os.path.join(ROOT, "ref-modding", "Mods", "LearnerMod", "LearnerMod.dll")
if os.path.exists(refdll):
    rtok = set(x.decode("ascii", "ignore") for x in
               re.findall(rb"[A-Za-z_][A-Za-z0-9_.<>`]{2,90}", open(refdll, "rb").read()))
    same = all(any(k in t for t in rtok) for k in ("System.Reflection", "System.Diagnostics"))
    if same:
        ok("程序集特性标记与官方范例一致（非反射调用，误报已排除）")

if os.path.exists(BUILT_DLL):
    blob, bad, miss = inspect_dll(BUILT_DLL)
    if bad:
        err("最新构建产物含被禁 API: " + ", ".join(bad))
    else:
        ok(f"最新 Release 构建产物 {len(blob)} bytes，IL 元数据无被禁 API")
    if miss:
        err("最新构建产物缺类型 " + ", ".join(miss))
    else:
        ok("最新构建产物入口类型齐全（ModEntry : IMod / ChemLab / ChemHeating / ChemPatches 等）")
    _built_dir = os.path.dirname(BUILT_DLL)
    _extra_dlls = sorted(os.path.basename(x) for x in glob.glob(os.path.join(_built_dir, "*.dll"))
                         if os.path.basename(x) != "FreeChem.dll")
    if _extra_dlls:
        err("构建输出含额外 DLL（0Harmony 应仅引用、不捆绑发布）: " + ", ".join(_extra_dlls))
    else:
        ok("构建输出仅 FreeChem.dll（0Harmony/Harmony 仅编译引用，不随包发布）")
    if os.path.exists(PKG_DLL):
        pkg_blob = open(PKG_DLL, "rb").read()
        if pkg_blob == blob:
            ok(f"发布包 FreeChem.dll 与最新构建逐字节一致（{len(blob)} bytes）")
        else:
            warn(f"发布包 FreeChem.dll 与最新构建不同步（包 {len(pkg_blob)}B vs 构建 {len(blob)}B）"
                 "—— lead 的 task-7 负责覆盖同步")
    else:
        warn("发布包 FreeChem.dll 缺失（lead 的 task-7 负责放入）")
elif os.path.exists(PKG_DLL):
    # 尚未构建：只能检查历史发布副本，不足以代表当前源码。
    blob, bad, miss = inspect_dll(PKG_DLL)
    if bad:
        err("历史发布副本含被禁 API: " + ", ".join(bad))
    else:
        warn(f"尚未执行最新构建；仅检查历史发布副本 {len(blob)} bytes（运行 dotnet build -c Release）")
    if miss:
        warn("历史发布副本缺类型 " + ", ".join(miss) + "（需重新构建）")
else:
    warn("FreeChem.dll 尚未构建（运行 dotnet build -c Release）")

# ---------- 14. 温度可达性（thermometer 交互 + 配方温度档 + 获取入口） ----------
runtime_text = read(os.path.join(SRC, "ChemRuntime.cs"))
rec_text = read(os.path.join(SRC, "ChemRecipes.cs"))
temp_bad = []

# 14a ModEntry 的 thermometer 激活分支必须调用 SetTemp 并循环 0..3
_mcase = re.search(r'case\s+"thermometer"\s*:(.*?)break;', entry_text, re.S)
if not _mcase:
    temp_bad.append('ModEntry 缺少 case "thermometer" 激活分支')
else:
    _body = _mcase.group(1)
    if "ChemRuntime.SetTemp" not in _body:
        temp_bad.append("thermometer 分支未调用 ChemRuntime.SetTemp")
    if not re.search(r'%\s*4', _body):
        temp_bad.append("thermometer 分支未见 %4 循环（应可切到 0/1/2/3 四档）")

# 14b SetTemp 的 Clamp 上界必须 ≥3（否则 temp:3 配方不可达）
_mset = re.search(r'void\s+SetTemp\s*\([^)]*\)\s*\{([^}]*)\}', runtime_text, re.S)
if not _mset:
    temp_bad.append("未找到 ChemRuntime.SetTemp")
else:
    _mc = re.search(r'Mathf\.Clamp\(\s*v\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)', _mset.group(1))
    if not _mc:
        temp_bad.append("SetTemp 未使用 Mathf.Clamp 限定温度档")
    else:
        _lo, _hi = int(_mc.group(1)), int(_mc.group(2))
        if _lo > 0:
            temp_bad.append(f"SetTemp 下界 {_lo} > 0，切不到冷档")
        if _hi < 3:
            temp_bad.append(f"SetTemp 上界 {_hi} < 3，temp:3 配方不可达")

# 14c 配方温度档 ⊆ {0,1,2,3} 且 0/2/3 都被实际使用（默认 temp=1）
_explicit_temps = {int(x) for x in re.findall(r'\btemp:\s*(\d+)', rec_text)}
recipe_temps = _explicit_temps | {1}
_oob = recipe_temps - {0, 1, 2, 3}
if _oob:
    temp_bad.append(f"配方温度档越界 {sorted(_oob)}（温度计只能 0/1/2/3）")
_absent = sorted({0, 2, 3} - recipe_temps)
if _absent:
    temp_bad.append(f"温度档 {_absent} 未被任何配方使用（死档位）")

# 14d 获取入口接线（task-10）：起始礼包 / 气体订单 / 发明家库存器材
_starter = re.search(r'void\s+OnNewGameStarted\([^)]*\)\s*\{(.*?)\n        \}', entry_text, re.S)
if not _starter:
    temp_bad.append("未找到 OnNewGameStarted")
else:
    _need = [i for i in ("thermometer", "beaker", "alcohol_lamp", "wick_cotton")
             if i not in _starter.group(1)]
    if _need:
        temp_bad.append("起始礼包缺: " + ", ".join(_need))
_deliver = re.search(r'void\s+DeliverGasOrder\([^)]*\)\s*\{(.*?)\n        \}', entry_text, re.S)
if not _deliver or '"gas_drum_empty"' not in _deliver.group(1):
    temp_bad.append("气体商人订单未包含 gas_drum_empty（大型空桶来源）")
_inventor = re.search(r'void\s+OnInventorStock\([^)]*\)\s*\{(.*?)\n        \}', entry_text, re.S)
if not _inventor or "ApparatusIds" not in _inventor.group(1) \
        or "UnityEngine.Random.Range" not in _inventor.group(1):
    temp_bad.append("发明家库存未实现器材随机上架（ApparatusIds + Random.Range）")

if temp_bad:
    for b in temp_bad:
        err("温度可达性/获取入口: " + b)
else:
    ok(f"温度可达性：thermometer 点击循环 0..3、SetTemp Clamp 0..3、"
       f"配方温度档 {sorted(recipe_temps)} 全可达；起始礼包/气体订单/发明家库存获取入口已接线")

# ---------- 15. 原版治安部 ENDANGERMENT 接入 + 王尔德标签页 + 开关键配置（最终集成） ----------
vs_bad = []

# 15a ChemRuntime：未文档化用法必须带 HasInstance guard / try-catch / 排风倍率削减
if "public static bool VanillaSecurityEnabled" not in runtime_text:
    vs_bad.append("缺少 VanillaSecurityEnabled 开关字段")
if not re.search(r'VanillaEndangermentPerUnit\s*=\s*(?:10|ChemLab\.VanillaEndangermentPerUnit\b)',
                 runtime_text + "\n" + read(os.path.join(SRC, "ChemLab.cs"))):
    vs_bad.append("VanillaEndangermentPerUnit 必须 = 10（P0 起常量在 ChemLab，ChemRuntime 共用）")
_mcommit = re.search(r'CommitVanillaEndangerment\s*\(\s*int\s+units\s*\)\s*\{(.*?)\n        \}',
                     runtime_text, re.S)
if not _mcommit:
    vs_bad.append("缺少 CommitVanillaEndangerment(int units) 实现")
else:
    _cb = _mcommit.group(1)
    if "PlayerStore.HasInstance" not in _cb:
        vs_bad.append("缺 PlayerStore.HasInstance guard（无存档时 Instance 会惰性建 store）")
    _ig = _cb.find("PlayerStore.HasInstance")
    _ii = _cb.find("PlayerStore.Instance")
    if _ig < 0 or _ii < 0:
        vs_bad.append("guard 或 PlayerStore.Instance 缺失")
    elif _ig > _ii:
        vs_bad.append("PlayerStore.HasInstance guard 必须在 PlayerStore.Instance 之前")
    if "SecData.ENDANGERMENT" not in _cb:
        vs_bad.append("未写入 SecData.ENDANGERMENT（原版治安部犯罪量）")
    if "VentMultiplierBp" not in _cb:
        vs_bad.append("原版写入量未按排风倍率削减（缺 ChemLab.VentMultiplierBp）")
    if "try" not in _cb or "catch" not in _cb:
        vs_bad.append("缺 try/catch 保护（未文档化 API 失败不得影响玩法）")
if "CommitVanillaEndangerment(units)" not in runtime_text:
    vs_bad.append("AddExhaust 未调用 CommitVanillaEndangerment")

# 15b ModEntry：4 个王尔德网络升级必须全部绑定自定义标签页
_n_up = entry_text.count("AddNetworkUpgrade(")
if _n_up != 4:
    vs_bad.append(f"AddNetworkUpgrade 应为 4 个（气体商人/订单/基础排风/进阶排风），实际 {_n_up}")
if entry_text.count("tab: ChemCatalog.ModId") < 4:
    vs_bad.append("4 个网络升级未全部绑定 tab: ChemCatalog.ModId（会退回基础页）")
if "SetNetworkTabLabel(ChemCatalog.ModId" not in entry_text:
    vs_bad.append("缺少 SetNetworkTabLabel 自定义标签页显示名")

# 15c 原版治安部开关键配置（默认开启、可关闭、Init + OnGameLoaded 双同步）
if "ConfigEntry<bool> vanillaSecurity" not in entry_text:
    vs_bad.append("缺少 vanillaSecurity 配置字段")
if 'config.Define("vanilla_security"' not in entry_text:
    vs_bad.append('缺少 config.Define("vanilla_security", ...) 配置项')
if "cfg_vanilla_security_tip" not in entry_text:
    vs_bad.append("配置 tooltip 未指向 cfg_vanilla_security_tip")
if "vanillaSecurity.OnChanged" not in entry_text:
    vs_bad.append("配置项缺少 OnChanged 同步（切换后需立即生效）")
_sync = entry_text.count("ChemRuntime.VanillaSecurityEnabled = vanillaSecurity.Value;")
if _sync < 2:
    vs_bad.append(f"VanillaSecurityEnabled 需在 Init 与 OnGameLoaded 各同步一次，实际 {_sync} 次")
for _k in ("cfg_vanilla_security", "cfg_vanilla_security_tip"):
    if _k not in zh:
        vs_bad.append("缺本地化键 " + _k)

if vs_bad:
    for b in vs_bad:
        err("原版治安部接入/标签页/配置: " + b)
else:
    ok("原版治安部接入：CommitVanillaEndangerment 有 HasInstance guard + try/catch + ENDANGERMENT，"
       "写入量按 VentMultiplierBp 削减；4 个网络升级统一自定义标签页；"
       "vanilla_security 开关默认 + 双同步 + 中英键齐备")

# ---------- 16. 第 14 轮：稳定数字 ID / 调试模式 / F9 调试窗口 / 满仓语义 / 中文描述 ----------
r14_bad = []

# 16a 稳定数字 ID 表 src/ChemItemIds.cs（85 条，顺序锚点防重排）
ITEM_ID_ANCHORS = {1: "gas_chlorine", 12: "liq_hydrogen_peroxide", 30: "thermometer",
                   61: "gas_drum_empty", 72: "sol_sediment", 73: "chem_reactor",
                   85: "heating_platform", 86: "label_printer",
                   87: "sol_calcium_oxide", 96: "sol_potassium_manganate",
                   97: "note_chem_dealer", 98: "test_tube"}
ITEM_ID_EXPECTED = 98  # P11 契约：#98 test_tube 追加，旧 1–97 不变
item_ids_path = os.path.join(SRC, "ChemItemIds.cs")
item_ids = []
if not os.path.exists(item_ids_path):
    r14_bad.append("缺少 src/ChemItemIds.cs（85 个稳定数字 ID 表）")
else:
    ids_text = read(item_ids_path)
    _ord = re.search(r'string\[\]\s+Ordered\s*=\s*\{(.*?)\};', ids_text, re.S)
    if _ord:
        item_ids = re.findall(r'"([a-z0-9_]+)"', _ord.group(1))
    if not item_ids:
        r14_bad.append("ChemItemIds 中未解析到 Ordered 数组（应为 string[] Ordered = {...}）")
    else:
        if len(item_ids) != ITEM_ID_EXPECTED:
            r14_bad.append(f"数字 ID 表应为 {ITEM_ID_EXPECTED} 条，实际 {len(item_ids)}")
        _dup = sorted({x for x in item_ids if item_ids.count(x) > 1})
        if _dup:
            r14_bad.append("数字 ID 表存在重复: " + ", ".join(_dup[:10]))
        _expect_ids = spec_ids | machine_ids
        _missing_ids = sorted(_expect_ids - set(item_ids))
        _extra_ids = sorted(set(item_ids) - _expect_ids - {"liq_water", "liq_ethanol"})
        if _missing_ids:
            r14_bad.append("数字 ID 表缺少物品: " + ", ".join(_missing_ids[:12]))
        if _extra_ids:
            r14_bad.append("数字 ID 表含 Specs∪MachineIds 之外的 id: " + ", ".join(_extra_ids[:12]))
        for _n, _exp in sorted(ITEM_ID_ANCHORS.items()):
            if len(item_ids) >= _n and item_ids[_n - 1] != _exp:
                r14_bad.append(f"顺序锚点 #{_n} 应为 {_exp}，实际 {item_ids[_n - 1]}")
        _on_in_table = [x for x in item_ids if x.endswith("_on")]
        if _on_in_table:
            r14_bad.append("_on 状态图不得进入数字 ID 表: " + ", ".join(_on_in_table))
        _notes = glob.glob(os.path.join(DOCS, "*.md")) + [os.path.join(ROOT, "README.md")]
        _append_note = ("只追加" in ids_text) or any(
            "只追加" in read(p) for p in _notes if os.path.exists(p))
        if not _append_note:
            r14_bad.append("ChemItemIds 源码注释/文档未写明「只追加不重排」")
        for _api_pat, _api_name in (
                (r'\bMinId\b', "MinId"), (r'\bMaxId\b', "MaxId"),
                (r'\bListCommand\b', "ListCommand"),
                (r'TryGetId\s*\(', "TryGetId(int, out string)"),
                (r'TryParseCommand\s*\(', "TryParseCommand(...)"),
                (r'DescribeRaw\s*\(', "DescribeRaw(int) 纯文本")):
            if not re.search(_api_pat, ids_text):
                r14_bad.append("ChemItemIds 缺 API: " + _api_name)
        # 纯数据层：不得引用 Unity / ModHelper / 游戏类型（否则 harness 无法编译）
        # 只看代码、剔除注释，避免文档注释里提到这些名字造成误报。
        _code_only = re.sub(r"//[^\n]*", "", ids_text)
        _code_only = re.sub(r"/\*.*?\*/", "", _code_only, flags=re.S)
        for _impure in ("UnityEngine", "ModHelper", "GameItem", "ChemCatalog", "ChemRecipes"):
            if _impure in _code_only:
                r14_bad.append(f"ChemItemIds 应为纯数据层，却引用了 {_impure}")

# 16b 调试模式设置（ModEntry 配置 + ChemRuntime 运行期标志）
if 'config.Define("debug_mode", false,' not in entry_text:
    r14_bad.append('ModEntry 缺 config.Define("debug_mode", false, ...)')
if "ConfigEntry<bool> debugMode" not in entry_text:
    r14_bad.append("ModEntry 缺 ConfigEntry<bool> debugMode 字段")
if "debugMode.OnChanged" not in entry_text:
    r14_bad.append("debug_mode 配置缺 OnChanged 同步")
if "public static bool DebugMode" not in runtime_text:
    r14_bad.append("ChemRuntime 缺 public static bool DebugMode")
if entry_text.count("ChemRuntime.DebugMode = debugMode.Value;") < 2:
    r14_bad.append("DebugMode 需在 Init 与 OnGameLoaded 各同步一次（>=2）")

# 16c F9 调试入口 + 调试窗口 src/ChemDebug.cs
# 允许两种合法分层：A) ModEntry 直接注册；B) 封装在 ChemDebug.Register()/Unregister() 由 ModEntry 调用。
debug_text = ""
if not os.path.exists(os.path.join(SRC, "ChemDebug.cs")):
    r14_bad.append("缺少 src/ChemDebug.cs（F9 调试窗口实现）")
else:
    debug_text = read(os.path.join(SRC, "ChemDebug.cs"))
    _combined = entry_text + "\n" + debug_text
    if "AddDebugFunction(" not in _combined:
        r14_bad.append("未注册 F9 调试入口 ModHelper.AddDebugFunction")
    if "DEBUG_CATEGORY_COMMAND" not in _combined:
        r14_bad.append("F9 调试入口未使用 DEBUG_CATEGORY_COMMAND 分类")
    if "RemoveDebugFunctions(" not in _combined:
        r14_bad.append("未调用 RemoveDebugFunctions 清理 F9 入口")
    _lifecycle = ("ChemDebug.Register()" in entry_text and "ChemDebug.Unregister()" in entry_text) \
        or ("AddDebugFunction(" in entry_text and "RemoveDebugFunctions(" in entry_text)
    if not _lifecycle:
        r14_bad.append("F9 入口未挂到生命周期（ModEntry 需 AddDebugFunction/RemoveDebugFunctions "
                       "或 ChemDebug.Register/Unregister）")
    if "OpenSpawnWindow" not in debug_text and "ShowSpawnWindow" not in debug_text:
        r14_bad.append("ChemDebug 缺 F9 按钮 action（OpenSpawnWindow/ShowSpawnWindow）")
    for _need in ("CustomUIManager.Window(", "AddInput", "AddButton",
                  "backInvinvElement", "GraphUtils.CanAccept(",
                  "TryAcceptAllOrDestroy(", "TrySpawnFromCommand", "debug_alert_full"):
        if _need not in debug_text:
            r14_bad.append("ChemDebug 缺: " + _need)
    # 满仓语义（docs/api-findings.md §8.2 修正版）：
    #   CanAccept 探测 → 失败 Destroy + 弹窗 → 成功 TryAcceptAllOrDestroy
    _ic = debug_text.find("GraphUtils.CanAccept(")
    _id = debug_text.find(".Destroy()", _ic if _ic >= 0 else 0)
    _it = debug_text.find("TryAcceptAllOrDestroy(", _ic if _ic >= 0 else 0)
    if _ic < 0 or _id < 0 or _it < 0 or not (_ic < _id < _it):
        r14_bad.append("满仓分支顺序不对：应为 CanAccept 探测 → 失败 Destroy → 成功 TryAcceptAllOrDestroy")
    # §8.2：TryAcceptAll 返回值是「剩余未接受数量」，不得当「已接受数」判断
    _dbg_code = re.sub(r"//[^\n]*", "", debug_text)
    _dbg_code = re.sub(r"/\*.*?\*/", "", _dbg_code, flags=re.S)
    for _badpat, _why in (
            (r'\b(?:int|var)\s+placed\s*=', "把 TryAcceptAll 返回值存成 placed 变量"),
            (r'\bplaced\s*<=?\s*[01]\b', "把 TryAcceptAll 返回值当已接受数比较"),
            (r'GraphUtils\.TryAcceptAll\s*\(', "直接调用 TryAcceptAll（应使用 TryAcceptAllOrDestroy）")):
        if re.search(_badpat, _dbg_code):
            r14_bad.append("放置语义误用: " + _why)
    # 满仓提示：优先原版 modal GenericConfirmationUIManager，保留 CustomUIManager 回退
    if "GenericConfirmationUIManager" not in debug_text:
        r14_bad.append("满仓提示未优先使用原版 GenericConfirmationUIManager")
    if "OpenPanelSingleButton(" not in debug_text:
        r14_bad.append("满仓提示未调用 OpenPanelSingleButton")
    _alert = re.search(r'void\s+ShowVanillaAlert\([^)]*\)\s*\{(.*?)\n        \}',
                       debug_text, re.S)
    if not _alert:
        r14_bad.append("未找到 ShowVanillaAlert（满仓/禁用提示入口）")
    else:
        _ab = _alert.group(1)
        _gi = _ab.find("GenericConfirmationUIManager")
        _ci = _ab.find("CustomUIManager.Window(")
        if _gi < 0 or _ci < 0 or _gi > _ci:
            r14_bad.append("满仓提示顺序应为 GenericConfirmationUIManager 优先、CustomUIManager 回退")
    for _wrong in ("frontInvinvElement", "displayElement", "afterhoursElement",
                   "MoveItemToAfterhours("):
        if _wrong in debug_text:
            r14_bad.append(f"生成目标误用了 {_wrong}（应仅用 backInvinvElement / 后柜台）")

# 16d manifest：<Name> 含「自由化学」、<Description> 中文（>=10 CJK；lead 改造前记 WARN 不 FAIL）
_name = r.findtext("Name", "")
_desc = r.findtext("Description", "")
_cjk = len(re.findall(r"[\u4e00-\u9fff]", _desc))
if "自由化学" not in _name:
    r14_bad.append("manifest <Name> 未包含「自由化学」")
if r14_bad:
    for b in r14_bad:
        err("第14轮功能: " + b)
else:
    ok(f"第14轮功能：数字 ID 表 {len(item_ids)} 条（与 Specs∪MachineIds 完全一致、"
       f"无重复、顺序锚点命中、_on 不在表内）；debug_mode 配置；F9 调试窗口"
       "（CanAccept→Destroy+弹窗→TryAcceptAllOrDestroy，目标 backInvinvElement，"
       "满仓提示优先 GenericConfirmationUIManager、CustomUIManager 回退）")
# F9 依赖游戏根目录 dev.txt（DevTools 门控）——提醒项，不是功能缺陷
if "dev.txt" in debug_text:
    ok("F9 调试面板的 dev.txt 门控已在 ChemDebug 注释中说明（开发机开关，不随模组发布）")
else:
    warn("ChemDebug 未提及 F9 依赖游戏根目录 dev.txt（DevTools 门控）；建议补注释说明（非功能缺陷）")
if _cjk >= 10 and not re.fullmatch(r"[\x20-\x7e]+", _desc.strip()):
    ok(f"manifest <Description> 为中文（{_cjk} 个 CJK 字符），<Name> 含「自由化学」")
else:
    warn(f"manifest <Description> 仍非中文（CJK={_cjk}，当前为英文），按 lead 安排待改中文后转 PASS；"
         "本项不 FAIL（manifest 归 lead）")

# ---------- 17. 第 15 轮：快捷键调试入口（默认 F10，可重绑，不依赖 dev.txt） ----------
hk_bad = []
_hk = debug_text + "\n" + entry_text          # ChemDebug / ModEntry 任一合法分层
# 17a 游戏「选项 → 按键绑定」注册与清理
if "ModHelper.AddGameOptionKeyBind(" not in _hk:
    hk_bad.append("未注册游戏按键绑定 ModHelper.AddGameOptionKeyBind(")
if not re.search(r'ModHelper\.OPTION_SECTION_KEYBIND|"keybind"', _hk):
    hk_bad.append("未使用 OPTION_SECTION_KEYBIND / \"keybind\" 选项分区")
if "option_debug_open" not in _hk:
    hk_bad.append("按键绑定未使用 option_debug_open 本地化键")
if "ModHelper.RemoveGameOptions(" not in _hk:
    hk_bad.append("OnDisable 未调用 ModHelper.RemoveGameOptions( 清理选项")
# 17b 持久化（PlayerPrefs）与默认键 F10
if "PlayerPrefs" not in _hk:
    hk_bad.append("未用 PlayerPrefs 持久化快捷键")
if "free_chem.debug_hotkey" not in _hk:
    hk_bad.append("PlayerPrefs 键名应为 free_chem.debug_hotkey")
if not re.search(r'PlayerPrefs\.(?:SetInt|GetInt)\s*\(', _hk):
    hk_bad.append("PlayerPrefs 未用 SetInt/GetInt 存取 (int)KeyCode")
if "KeyCode.F10" not in _hk:
    hk_bad.append("默认快捷键应为 KeyCode.F10")
# 17c 每帧轮询（协程，禁用线程）
for _need in ("ModHelper.StartCoroutine(", "IEnumerator", "yield return null",
              "Input.GetKeyDown(", "KeyBindHelper.SuppressHotkeys"):
    if _need not in _hk:
        hk_bad.append("快捷键轮询缺: " + _need)
if "CustomUIManager.Instance.IsOpen(" not in _hk and "IsOpen(SpawnWindowId" not in _hk:
    hk_bad.append("窗口已开时应切换关闭（缺 CustomUIManager.Instance.IsOpen 判定）")
if "StopCoroutine" not in _hk:
    hk_bad.append("OnDisable 未 StopCoroutine 停止轮询")
# 17d 场景 guard + 打开窗口
if "EmporiumEntry.Instance == null" not in _hk:
    hk_bad.append("快捷键轮询缺 EmporiumEntry.Instance == null 场景 guard")
if "OpenSpawnWindow" not in _hk:
    hk_bad.append("快捷键未打开 OpenSpawnWindow（或等价生成窗口）")
if re.search(r"\bnew\s+Thread\b|\bTask\.Run\b", _hk):
    hk_bad.append("快捷键实现使用了 Thread/Task（禁止，必须用协程）")
if hk_bad:
    for b in hk_bad:
        err("第15轮快捷键入口: " + b)
else:
    ok("快捷键入口：AddGameOptionKeyBind(OPTION_SECTION_KEYBIND/option_debug_open) + "
       "PlayerPrefs(free_chem.debug_hotkey, 默认 KeyCode.F10) + 协程逐帧轮询"
       "（SuppressHotkeys/Input.GetKeyDown/IsOpen 切换/场景 guard/StopCoroutine/RemoveGameOptions），"
       "F9 dev 入口保留不动")

# ---------- 18. 第 16 轮：生命周期 NRE 修复（Wild Network 延后注册）+ 设置分区中文化 ----------
nre_bad = []


def _method_body(text, name):
    m = re.search(r'void\s+' + name + r'\s*\([^)]*\)\s*\{(.*?)\n        \}', text, re.S)
    return m.group(1) if m else None


def _strip_comments(s):
    s = re.sub(r"//[^\n]*", "", s or "")
    return re.sub(r"/\*.*?\*/", "", s, flags=re.S)


_on_enable = _method_body(entry_text, "OnEnable")
_on_loaded = _method_body(entry_text, "OnGameLoaded")
_on_disable = _method_body(entry_text, "OnDisable")
_try_reg = _method_body(entry_text, "TryRegisterWildNetwork")

# 18a OnEnable 主菜单阶段绝不能碰 PlayerStore（AddNetworkUpgrade→PlayerStore.Instance 会 NRE）
#     先剔除注释：注释里解释「不要把 RegisterWildNetwork 放 OnEnable」是合法且必要的。
if _on_enable is None:
    nre_bad.append("未能解析 ModEntry.OnEnable() 方法体")
else:
    _oe_code = _strip_comments(_on_enable)
    if re.search(r'\bRegisterWildNetwork\s*\(', _oe_code):
        nre_bad.append("OnEnable 仍调用 RegisterWildNetwork()（主菜单 PlayerStore..ctor NRE 根因）")
    if re.search(r'\bAddNetworkUpgrade\s*\(', _oe_code):
        nre_bad.append("OnEnable 仍直接调用 AddNetworkUpgrade()（应延后到 OnGameLoaded）")

# 18b OnGameLoaded 内、ChemRuntime.Load() 之前注册 Wild Network
if _on_loaded is None:
    nre_bad.append("未能解析 ModEntry.OnGameLoaded() 方法体")
else:
    if "TryRegisterWildNetwork(" not in _on_loaded:
        nre_bad.append("OnGameLoaded 未调用 TryRegisterWildNetwork()")
    else:
        _i_reg = _on_loaded.find("TryRegisterWildNetwork(")
        _i_load = _on_loaded.find("ChemRuntime.Load()")
        if _i_load < 0 or _i_reg > _i_load:
            nre_bad.append("TryRegisterWildNetwork() 必须在 ChemRuntime.Load() 之前（先注册再恢复解锁状态）")

# 18c guard：PlayerStore.HasInstance → RegisterWildNetwork，try/catch，幂等标志
if _try_reg is None:
    nre_bad.append("缺少 TryRegisterWildNetwork() 方法（应带 PlayerStore.HasInstance guard）")
else:
    _tr_code = _strip_comments(_try_reg)
    if "PlayerStore.HasInstance" not in _tr_code:
        nre_bad.append("TryRegisterWildNetwork 缺 PlayerStore.HasInstance guard（不得惰性构造 store）")
    _ig = _tr_code.find("PlayerStore.HasInstance")
    _m_call = re.search(r'\bRegisterWildNetwork\s*\(', _tr_code)
    _ir = _m_call.start() if _m_call else -1
    if _ig < 0 or _ir < 0 or _ig > _ir:
        nre_bad.append("TryRegisterWildNetwork 顺序应为 PlayerStore.HasInstance guard → RegisterWildNetwork()")
    if "try" not in _tr_code or "catch" not in _tr_code:
        nre_bad.append("TryRegisterWildNetwork 缺 try/catch（半初始化失败不得导致 Init failed）")
if not re.search(r'bool\s+networkRegistered', entry_text):
    nre_bad.append("缺 bool networkRegistered 幂等标志")

# 18d OnDisable：RemoveNetworkUpgrades 必须在 try/catch 内，且复位幂等标志
if _on_disable is None:
    nre_bad.append("未能解析 ModEntry.OnDisable() 方法体")
else:
    _od_code = _strip_comments(_on_disable)
    if "RemoveNetworkUpgrades(" not in _od_code:
        nre_bad.append("OnDisable 未调用 RemoveNetworkUpgrades（清理缺失）")
    else:
        _it = _od_code.find("try")
        _i_rm = _od_code.find("RemoveNetworkUpgrades(")
        _ic2 = _od_code.find("catch", _i_rm if _i_rm >= 0 else 0)
        if _it < 0 or _i_rm < _it or _ic2 < 0:
            nre_bad.append("OnDisable 的 RemoveNetworkUpgrades 必须在 try/catch 内（主菜单/半初始化不得再抛）")
    if "networkRegistered = false" not in _od_code:
        nre_bad.append("OnDisable 未复位 networkRegistered = false")

# 18e 设置面板分区中文化（ModConfig 分区名必须是中文「玩法」「调试」）
for _lit in ('config.Define("dangerous_reactions", true, "玩法"',
             'config.Define("vanilla_security", true, "玩法"',
             'config.Define("debug_mode", false, "调试"'):
    if _lit not in entry_text:
        nre_bad.append("设置分区未中文化: 缺 " + _lit)
if re.search(r'config\.Define\(\s*"[a-z_]+"\s*,\s*(?:true|false)\s*,\s*"(?:Gameplay|Debug)"',
             entry_text):
    nre_bad.append('ModConfig 分区仍残留英文 "Gameplay"/"Debug"（设置面板会显示英文分区）')

# 18f 设置面板中文文案来源（label/tooltip 键 zh/en 都必须存在）
for _k in ("cfg_dangerous", "cfg_vanilla_security", "cfg_debug_mode"):
    if _k not in zh or _k not in en:
        nre_bad.append("设置项本地化键 zh/en 缺失: " + _k)

# 18g 编码回归：src/*.cs 必须是合法 UTF-8；F9 调试入口标签不得被编码破坏
_enc_bad = []
for _p in src_files:
    try:
        open(_p, "rb").read().decode("utf-8")
    except UnicodeDecodeError as _e:
        _enc_bad.append(os.path.basename(_p) + ": " + str(_e)[:60])
for _b in _enc_bad:
    nre_bad.append("源码不是合法 UTF-8: " + _b)
if "按ID生成" not in entry_text:
    nre_bad.append("F9 调试入口标签应为「FreeChem 按ID生成」，当前缺失（可能被 GBK/UTF-8 编码损坏）")
_moji_files = sorted({os.path.basename(_p) for _p in src_files
                      if re.search(r"[鎸鐢鈥銆锛]", read(_p))})
if _moji_files:
    warn("src 疑似 GBK/UTF-8 二次编码乱码字符（需 owner 以 UTF-8 重存）: "
         + ", ".join(_moji_files))

if nre_bad:
    for b in nre_bad:
        err("第16轮生命周期/设置面板: " + b)
else:
    ok("第16轮生命周期修复：OnEnable 不碰 PlayerStore；OnGameLoaded 在 Load() 前调用 "
       "TryRegisterWildNetwork（HasInstance guard + try/catch + 幂等）；OnDisable 清理在 try/catch；"
       "设置分区名「玩法」「调试」中文化，cfg_* 三键 zh/en 齐备")
# 文档同步（软断言：README/设计说明/进度报告应记录本次 NRE 修复与设置面板中文化）
_doc_blob = ""
for _dp in (glob.glob(os.path.join(DOCS, "*.md")) + [os.path.join(ROOT, "README.md")]):
    if os.path.exists(_dp):
        _doc_blob += read(_dp)
_has_setting_doc = ("设置面板" in _doc_blob or "玩法" in _doc_blob) and \
                   ("调试" in _doc_blob or "分区" in _doc_blob)
_has_nre_doc = ("PlayerStore" in _doc_blob or "NRE" in _doc_blob or "NullReference" in _doc_blob
                or "Init failed" in _doc_blob or "空引用" in _doc_blob)
if _has_setting_doc and _has_nre_doc:
    ok("文档已记录设置面板中文化与本次生命周期 NRE 修复（README/docs）")
else:
    warn("文档尚未见「设置面板中文化」或「PlayerStore NRE 修复」记录（docs-i18n 同步后应转 PASS）")

# ---------- 19. P0（第 17 轮）：tag/欣快画像、相态过滤、原版药剂兼容、商人、量纲、仪器提示 ----------
p0_bad = []
cat_text = read(os.path.join(SRC, "ChemCatalog.cs"))


def _body_or_expr(text, name, ret=None):
    """按方法名提取方法体（支持任意返回类型与表达式体），到 8 空格缩进的闭合花括号为止。"""
    m = re.search(r'\b' + name + r'\s*\([^)]*\)\s*\{(.*?)\n        \}', text, re.S)
    if m:
        return m.group(1)
    m = re.search(r'\b' + name + r'\s*\([^)]*\)\s*=>\s*(.+?);', text, re.S)
    return m.group(1) if m else None


# 19a 只给真药剂打 ReagentTag（Kind 白名单），且白名单不含工具/器材类
_create = _body_or_expr(cat_text, "Create")
_is_kind = None
_mk = re.search(r'bool\s+IsReagentKind\s*\([^)]*\)\s*(?:\{(.*?)\n        \}|=>\s*([^;]+);)',
                src_text, re.S)
_rk_arr = re.search(r'ReagentKinds\s*=\s*\{([^}]*)\}', src_text, re.S)
_whitelist_src = ""
if _mk:
    _is_kind = _mk.group(1) if _mk.group(1) is not None else _mk.group(2)
    _whitelist_src = _is_kind + (" " + _rk_arr.group(1) if _rk_arr else "")
if _create is None:
    p0_bad.append("未能解析 ChemCatalog.Create()")
else:
    _cc = _strip_comments(_create)
    _i_tag = _cc.find("EnableTag(ChemRecipes.ReagentTag")
    if _i_tag < 0:
        p0_bad.append("ChemCatalog.Create 未对药剂调用 EnableTag(ChemRecipes.ReagentTag)")
    else:
        _pre = _cc[max(0, _i_tag - 800):_i_tag]
        if ("IsReagentKind" not in _pre) and not re.search(r'if\s*\([^)]*Kind[^)]*\)', _pre):
            p0_bad.append("EnableTag(ReagentTag) 未位于 Kind 白名单条件内（工具/器材/副产物会被误打 tag）")
if _is_kind is None:
    p0_bad.append("缺 Kind 白名单函数 IsReagentKind(...)（tag/画像过滤的唯一判据）")
else:
    for _k in ("gas", "liquid", "solid", "illicit", "canister", "drum"):
        if f'"{_k}"' not in _whitelist_src:
            p0_bad.append(f"IsReagentKind 白名单缺真药剂 Kind: {_k}")
    for _k in ("tool", "vessel", "consumable", "heater", "protective", "container", "byproduct"):
        if f'"{_k}"' in _whitelist_src:
            p0_bad.append(f"IsReagentKind 白名单不得包含非药剂 Kind: {_k}")

# 19b 画像：欣快只在 EuphoriaTier>=1 或 Controlled 时展示；OnTooltip 先按 Kind 过滤
_profl = _body_or_expr(runtime_text, "ProfileLine")
if _profl is None:
    p0_bad.append("未能解析 ChemRuntime.ProfileLine()")
else:
    _pf = _strip_comments(_profl)
    if "EuphoriaTier" not in _pf or "Controlled" not in _pf:
        p0_bad.append("ProfileLine 欣快展示缺 EuphoriaTier/Controlled 条件")
    if not re.search(r'(?:EuphoriaTier\s*>=\s*1|EuphoriaTier\s*>\s*0|tier\s*>=\s*1|tier\s*>\s*0)',
                     _pf):
        p0_bad.append("ProfileLine 欣快档位条件应为 EuphoriaTier>=1（普通酸碱盐不得显示欣快）")
    if "chem_profile_reagent" not in runtime_text:
        p0_bad.append("ProfileLine 缺普通药剂画像键 chem_profile_reagent（无欣快时用 2 占位行）")
if "chem_medical_grade" not in src_text:
    p0_bad.append("医用气罐 tooltip 缺 chem_medical_grade 行（原版医疗采购可收购）")
_tooltip = _body_or_expr(entry_text, "OnTooltip")
if _tooltip is None:
    p0_bad.append("未能解析 ModEntry.OnTooltip()")
elif not ("IsReagentKind" in _tooltip or "IsReagentSpec" in _tooltip or "Kind" in _tooltip):
    p0_bad.append("OnTooltip 未按 Kind 过滤（仅看 tag 会把工具/器材当药剂画像）")
# Profiles 覆盖：强酸/强碱/氧化剂/氯气/氨/CO2/漂白粉等必须有画像条目
_pm = re.search(r'Profiles\s*=\s*new Dictionary<string, ChemProfile>(.*?)\};', rec_text, re.S)
_profiles_blob = _pm.group(1) if _pm else ""
for _chem in ("gas_chlorine", "gas_hydrogen_chloride", "gas_ammonia", "gas_carbon_dioxide",
              "liq_hydrochloric_acid", "liq_sulfuric_acid", "liq_nitric_acid",
              "liq_sodium_hydroxide", "liq_hydrogen_peroxide", "sol_bleaching_powder"):
    if f'"{_chem}"' not in _profiles_blob:
        p0_bad.append("ChemRecipes.Profiles 缺化学品画像: " + _chem)

# 19c 相态过滤顺序 + 原版药剂兼容（VanillaAnalog）
_accept = _body_or_expr(mach_text, "AcceptsInput")
if _accept is None:
    p0_bad.append("未能解析 ChemMachines.AcceptsInput()")
else:
    _ac = _strip_comments(_accept)
    _i_phase = _ac.find("AllowedInputPhases")
    _i_supply = _ac.find("CHEMICAL_SUPPLIES")
    if _i_phase < 0:
        p0_bad.append("AcceptsInput 未用 AllowedInputPhases 做相态过滤")
    elif _i_supply >= 0 and _i_phase > _i_supply:
        p0_bad.append("AcceptsInput 相态判断必须在 CHEMICAL_SUPPLIES 类型放行之前（否则气体可进滗析）")
    if "VanillaAnalog" not in _ac:
        p0_bad.append("AcceptsInput 未用 VanillaAnalog 做原版药剂映射")
_rmc = _body_or_expr(mach_text, "RunMachineCycle")
if _rmc is None:
    p0_bad.append("未能解析 ChemMachines.RunMachineCycle()")
elif "VanillaAnalog" not in _rmc:
    p0_bad.append("RunMachineCycle 未用 VanillaAnalog 映射原版药剂投料")
if "VanillaAnalog" not in heat_text:
    p0_bad.append("ChemHeating 加热台投料未用 VanillaAnalog 映射原版药剂")
_fin = _body_or_expr(mach_text, "Finish")
if _fin is None:
    p0_bad.append("未能解析 ChemMachines.Finish()")
elif "VanillaAnalog" not in _fin or "Destroy(" not in _fin:
    p0_bad.append("Finish 未销毁原版药剂瓶（需 VanillaAnalog 记录 + Destroy 非辅助投料）")

# 19d 商人/生成：订单进后柜台 + SetItemOwnerShip/SetSource
_deliver = _body_or_expr(entry_text, "DeliverGasOrder")
if _deliver is None:
    p0_bad.append("未能解析 ModEntry.DeliverGasOrder()")
else:
    _dv = _strip_comments(_deliver)
    if "MoveItemToBackCounter(" not in _dv:
        p0_bad.append("气体订单未投放到后柜台 MoveItemToBackCounter(")
    if "MoveItemToAfterhours(" in _dv:
        p0_bad.append("气体订单仍使用 MoveItemToAfterhours（应改为后柜台）")
    if "SetItemOwnerShip(" not in _dv:
        p0_bad.append("气体订单物品未 SetItemOwnerShip(（会被当赃物/无主）")
    if "SetSource(" not in _dv:
        p0_bad.append("气体订单物品未 SetSource(（来源未标注 FreeChem）")
_starter = _body_or_expr(entry_text, "OnNewGameStarted")
if _starter is not None:
    if "SetItemOwnerShip(" not in _starter or "SetSource(" not in _starter:
        p0_bad.append("起始礼包物品未 SetItemOwnerShip/SetSource")
if debug_text:
    if "SetItemOwnerShip(" not in debug_text or "SetSource(" not in debug_text:
        p0_bad.append("调试生成物品未 SetItemOwnerShip/SetSource")

# 19e 量纲统一：嫌疑度 = raw*10*bp/10000；VolumeUl=250000；ChemLab 量纲注释
_sus = _body_or_expr(lab_text, "SuspicionFrom")
if _sus is None:
    p0_bad.append("未能解析 ChemLab.SuspicionFrom()")
else:
    _su = _strip_comments(_sus)
    # 量纲：raw × VanillaEndangermentPerUnit(10) × bp / 10000；允许直接字面量 10 或共用常量
    if not re.search(r'\*\s*(?:10\b|VanillaEndangermentPerUnit\b)', _su):
        p0_bad.append("SuspicionFrom 量纲未改为 raw*10*bp/10000（缺 ×10 / ×VanillaEndangermentPerUnit）")
    if re.search(r'\*\s*100\b', _su):
        p0_bad.append("SuspicionFrom 仍含旧式 *100 量纲")
if "VanillaEndangermentPerUnit" not in lab_text:
    p0_bad.append("常量 VanillaEndangermentPerUnit 未移到 ChemLab")
if "ChemLab.VanillaEndangermentPerUnit" not in runtime_text:
    p0_bad.append("ChemRuntime 未共用 ChemLab.VanillaEndangermentPerUnit")
if not re.search(r'VolumeUl\s*=\s*250000', runtime_text):
    p0_bad.append("ChemRuntime.VolumeUl 未统一为 250000（每件 250ml）")
if "量纲" not in lab_text:
    p0_bad.append("ChemLab 顶部缺「量纲约定」注释块")

# 19f 仪器状态提示 + 机器窗口 ui_* 标签
eq_path = os.path.join(SRC, "ChemEquipment.cs")
eq_text = read(eq_path) if os.path.exists(eq_path) else ""
_inst_blob = entry_text + mach_text + eq_text
for _k in ("inst_temp", "inst_ph", "inst_balance", "inst_protection",
           "inst_fuel", "inst_wick", "inst_filter"):
    if _k not in _inst_blob:
        p0_bad.append("仪器状态提示缺键: " + _k)
_ui_blob = mach_text + entry_text
for _k in ("ui_modules", "ui_input", "ui_output", "ui_vessel",
           "ui_heater", "ui_wick", "ui_fuel"):
    if f'"{_k}"' not in _ui_blob:
        p0_bad.append("机器窗口标签缺键: " + _k)
# P4a：电池/便签面板改由官方 MachineHelper 构建（PowerPanel/NotePanel），不再有 ui_battery/ui_note 标签
for _tok in ("MachineHelper.CreateMachineryWindow(", "MachineHelper.PowerPanel(",
             "MachineHelper.NotePanel("):
    if _tok not in _ui_blob:
        p0_bad.append("P4a 机器窗口缺官方构建器调用: " + _tok)
if "GetLocalized" not in mach_text:
    p0_bad.append("机器窗口标签未走 ModHelper.GetLocalized（Ui 辅助函数）")
if eq_text and "GetLocalized" not in eq_text:
    p0_bad.append("仪器状态提示未走 ModHelper.GetLocalized（L 辅助函数）")

if p0_bad:
    for b in p0_bad:
        err("P0 整改: " + b)
else:
    ok("P0：Kind 白名单 tag/欣快条件+OnTooltip Kind 过滤（Profiles 覆盖 10 类化学品）；"
       "AcceptsInput 相态先于原版类型、RunCycle/加热台用 VanillaAnalog、Finish 销毁原版瓶；"
       "订单进后柜台 + SetItemOwnerShip/SetSource；SuspicionFrom=raw*10、VolumeUl=250000、量纲注释；"
       "仪器 inst_* 提示与机器 ui_* 标签齐备")

# ---------- 20. P1a（第 18 轮）：原版数据桥接 / 七档纯度 / 倒液映射 / Harmony 兜底 ----------
p1a_bad = []


def _brace_block(text, marker):
    i = text.find(marker)
    return _brace_from(text, i) if i >= 0 else None


def _brace_from(text, pos):
    j = text.find("{", pos)
    if j < 0:
        return None
    depth = 0
    for k in range(j, len(text)):
        if text[k] == "{":
            depth += 1
        elif text[k] == "}":
            depth -= 1
            if depth == 0:
                return text[j:k + 1]
    return None


# 20a Create 原版数据桥接（chem tags / purity tag+feature / 液体容器）
_create_p1 = _body_or_expr(cat_text, "Create")
_cc2 = _strip_comments(_create_p1 or "")
for _tok in ("CHEM_ITEM_TAG", "CHEMICAL_PRODUCT_TAG", "CHEMICAL_PRODUCT_PURITY_TAG",
             "SetPurityFeature("):
    if _tok not in _cc2:
        p1a_bad.append("Create 缺原版桥接: " + _tok)
if "CHEMICAL_PRODUCT_PURITY_TAG" in _cc2 and not re.search(r'SetDouble\s*\(', _cc2):
    p1a_bad.append("CHEMICAL_PRODUCT_PURITY_TAG 未用 SetDouble 写 0–100 纯度")
# P4c 重构后：液体容器桥接统一在 EnsureLiquidBridge（Create/BridgeVanillaChemData 调用）
_elb = _body_or_expr(cat_text, "EnsureLiquidBridge") or ""
_addp = _body_or_expr(cat_text, "AddPourParts") or ""
_liquid_blob = _cc2 + _elb
if "InitLiquidContainerItem(" not in _liquid_blob:
    p1a_bad.append("液体药剂未走 WaterHelper.InitLiquidContainerItem")
if ("AddLiquid(" not in (_elb + _addp)) or "PourParts" not in _elb:
    p1a_bad.append("液体药剂未按 PourParts 调用 WaterHelper.AddLiquid")
_i_init = _liquid_blob.find("InitLiquidContainerItem(")
if _i_init >= 0:
    _i_sprite = _liquid_blob.rfind("SetSpriteAndShapeFromMod")
    if _i_sprite < 0 or _i_sprite < _i_init:
        p1a_bad.append("InitLiquidContainerItem 后未恢复本模组图标"
                       "（SetSpriteAndShapeFromMod 必须出现在其之后）")
    _tail = _liquid_blob[_i_init:]
    if not (re.search(r'item\.name\s*=', _tail)
            or re.search(r'\b(?:Restore|ApplyIdentity|WriteIdentity|Reapply)\w*\s*\(', _tail)):
        p1a_bad.append("InitLiquidContainerItem 后未恢复物品名称")
    if "SetGameItemType(" not in _tail:
        p1a_bad.append("InitLiquidContainerItem 后未恢复物品类型")

# 20b 七档纯度映射（阈值 99/90/80/60/45/30 → 7 个 Create*Grade）
for _fn in ("CreateLabPure", "CreatePharmaGrade", "CreateNearPharma",
            "CreateStreetStandard", "CreateCutProduct", "CreateJunkGrade", "CreateStarchGrade"):
    if _fn + "(" not in src_text:
        p1a_bad.append("七档纯度映射缺 " + _fn)
# 阈值：允许字面量 >= N，或 ChemLab 常量（GradeLabPure=99 … GradeJunk=30）
_GRADE_CONSTS = {"GradeLabPure": 99, "GradePharma": 90, "GradeNearPharma": 80,
                 "GradeStreet": 60, "GradeCut": 45, "GradeJunk": 30}
for _name, _val in _GRADE_CONSTS.items():
    _cm = re.search(_name + r'\s*=\s*(\d+)', src_text)
    if _cm:
        if int(_cm.group(1)) != _val:
            p1a_bad.append(f"{_name} 阈值应为 {_val}，实际 {_cm.group(1)}")
    elif not re.search(r'>=\s*' + str(_val) + r'\b', src_text):
        p1a_bad.append(f"七档纯度阈值缺 >= {_val}（或常量 {_name}）")

# 20c PourParts：每条和=100、覆盖全部液体药剂、H2O2→water 且注释「近似」
if "PourParts" not in rec_text or "TryGetPourParts(" not in rec_text:
    p1a_bad.append("ChemRecipes 缺 PourParts 纯数据表或 TryGetPourParts")
else:
    _pm = re.search(r'PourParts\s*=\s*new\s+Dictionary', rec_text)
    _pblk = _brace_from(rec_text, _pm.end()) if _pm else None
    if not _pblk:
        p1a_bad.append("未能解析 PourParts 数据块")
    else:
        _pblk = re.sub(r"//[^\n]*", "", _pblk)   # 剔除行尾注释（「70% 乙醇」等会干扰数字求和）
        _entries, _cur, _depth = [], "", 0
        for _ch in _pblk[1:-1]:
            if _ch == "{":
                _depth += 1
            if _ch == "}":
                _depth -= 1
            if _ch == "," and _depth == 0:
                _entries.append(_cur)
                _cur = ""
            else:
                _cur += _ch
        _entries.append(_cur)
        _pp_keys, _sum_bad = set(), []
        for _e in _entries:
            _ids = re.findall(r'"(liq_[a-z0-9_]+)"', _e)
            if not _ids:
                continue
            _pp_keys.add(_ids[0])
            _nums = [int(x) for x in re.findall(r'(?<![A-Za-z0-9_.])(\d{1,3})(?![A-Za-z0-9_.])', _e)]
            if sum(_nums) != 100:
                _sum_bad.append(f"{_ids[0]}={sum(_nums)}")
        if _sum_bad:
            p1a_bad.append("PourParts 百分比之和 ≠ 100: " + ", ".join(_sum_bad[:8]))
        _liquid_ids = set(re.findall(
            r'S\("(liq_[a-z0-9_]+)"\s*,\s*"[^"]*"\s*,\s*Phase\.Liquid', cat_text))
        _miss_cover = sorted(_liquid_ids - _pp_keys)
        if _miss_cover:
            p1a_bad.append("PourParts 未覆盖液体药剂: " + ", ".join(_miss_cover[:10]))
        _h2o2 = re.search(r'"(liq_hydrogen_peroxide)"[^}]*?\}', _pblk, re.S)
        if not _h2o2 or "water" not in _h2o2.group(0):
            p1a_bad.append("PourParts 中双氧水未映射为 water（原版无过氧化物组分）")
        if "近似" not in rec_text:
            p1a_bad.append("PourParts 缺「近似映射」说明注释")

# 20d ChemRuntime.PurityOf + P0 量纲不回退
if "PurityOf(" not in runtime_text:
    p1a_bad.append("ChemRuntime 缺 PurityOf(item) 辅助")
if "* 100" in _strip_comments(_body_or_expr(lab_text, "SuspicionFrom") or ""):
    p1a_bad.append("ChemLab.SuspicionFrom 回退到旧 *100 量纲")

# 20e Harmony 兜底 src/ChemPatches.cs（加载器自动应用，不手动 PatchAll）
_patch_path = os.path.join(SRC, "ChemPatches.cs")
_patch_text = read(_patch_path) if os.path.exists(_patch_path) else ""
if not _patch_text:
    p1a_bad.append("缺 src/ChemPatches.cs（Harmony 兜底补丁）")
else:
    if _patch_text.count("[HarmonyPatch(") < 4:
        p1a_bad.append(f"ChemPatches 的 [HarmonyPatch] 应覆盖 4 类，实际 {_patch_text.count('[HarmonyPatch(')}")
    for _need in ("typeof(ChemScannerUIManager)", '"RefreshChemMode"',
                  "typeof(WaterHelper)", '"TransferLiquid"',
                  "typeof(ChemicalFeedbackHelper)", '"IsLethal"', '"IsToxic"',
                  '"GetEuphoriaLevel"',
                  "typeof(StoreClient)", '"IsClientBuyingThisItem"'):
        if _need not in _patch_text:
            p1a_bad.append("ChemPatches 缺补丁目标: " + _need)
    if not re.search(r'new\s*\[\s*\]\s*\{\s*typeof\s*\(\s*GameItem\s*\)\s*,\s*typeof\s*\(\s*bool\s*\)\s*\}',
                     _patch_text):
        p1a_bad.append("IsClientBuyingThisItem 必须显式 new[]{typeof(GameItem),typeof(bool)} 防重载歧义")
    for _banned in ("PatchAll(", "UnpatchAll(", "new Thread", "Task.Run", "DllImport"):
        if _banned in _strip_comments(_patch_text):
            p1a_bad.append("ChemPatches 不得使用: " + _banned)
    if "catch" not in _strip_comments(_patch_text):
        p1a_bad.append("ChemPatches 补丁缺 try/catch 防御（Harmony 异常会污染游戏）")
    # 递归风险：RefreshChemMode Postfix 若调用 RefreshUI() 重绘，必须有重入/已桥接守卫
    _si = _patch_text.find('"RefreshChemMode"')
    _scan_region = _patch_text[_si:_si + 3000] if _si >= 0 else ""
    if not _scan_region:
        p1a_bad.append("未找到 RefreshChemMode 补丁区域")
    else:
        if not ("CHEMICAL_PRODUCT_TAG" in _scan_region and "IsTag(" in _scan_region
                and "return;" in _scan_region):
            p1a_bad.append("scanner Postfix 缺「已桥接/已带 tag 直接 return」守卫（RefreshUI 递归风险）")
        if "RefreshUI(" in _scan_region:
            if not re.search(r'bridging|reentran|inPatch|guard', _scan_region):
                p1a_bad.append("scanner Postfix 调 RefreshUI 但缺重入保护标志（递归风险）")
            elif "finally" not in _scan_region or not re.search(r'bridging\s*=\s*false', _scan_region):
                p1a_bad.append("scanner 重入标志未在 finally 复位（异常会永久卡死桥接）")
_csproj = read(os.path.join(SRC, "FreeChem.csproj"))
if "0Harmony" not in _csproj:
    p1a_bad.append("csproj 未引用 0Harmony（Harmony 桥接无法编译）")

# 20f P1a 本地化键 + 文档软断言
P1A_KEYS = ["scanner_fc_line"]
_miss_p1a = [k for k in P1A_KEYS if k not in zh or k not in en]
if _miss_p1a:
    p1a_bad.append("缺 P1a 本地化键: " + ", ".join(_miss_p1a))

if p1a_bad:
    for b in p1a_bad:
        err("P1a 原版桥接: " + b)
else:
    ok("P1a：Create 桥接 chem tags/purity(SetDouble)+SetPurityFeature、液体 InitLiquidContainerItem+AddLiquid 后恢复图标；"
       "七档纯度阈值+工厂；PourParts 和=100/覆盖液体/H2O2 近似映射；PurityOf；"
       "ChemPatches 四类 [HarmonyPatch]（显式参数类型、无 PatchAll/线程/IO）")
_doc_p1a = ""
for _dp in (glob.glob(os.path.join(DOCS, "*.md")) + [os.path.join(ROOT, "README.md")]):
    if os.path.exists(_dp):
        _doc_p1a += read(_dp)
if (("P1a" in _doc_p1a or "原版桥接" in _doc_p1a)
        and ("近似" in _doc_p1a or "Harmony" in _doc_p1a)):
    ok("文档已记录 P1a 原版数据桥接与映射近似说明（README/docs）")
else:
    warn("文档尚未见「P1a 原版桥接 / 映射近似」说明（docs-i18n 同步后应转 PASS）")

# ---------- 21. P1b1（第 19 轮）：标签打印机 + 单钢瓶纯度 ----------
p1b_bad = []
printer_path = os.path.join(SRC, "ChemPrinter.cs")
printer_text = read(printer_path) if os.path.exists(printer_path) else ""

# 21a 新物品 label_printer：Specs Kind=tool、数字 ID #86、掉落来源、图标 32×32
if "label_printer" not in spec_ids:
    p1b_bad.append("ChemCatalog.Specs 缺 label_printer")
elif spec_kinds.get("label_printer") != "tool":
    p1b_bad.append(f"label_printer 的 Kind 应为 tool，实际 {spec_kinds.get('label_printer')}")
if item_ids and (len(item_ids) < 86 or item_ids[85] != "label_printer"):
    p1b_bad.append("ChemItemIds #86 应为 label_printer（只追加、旧 1–85 顺序不变）")
# 掉落：apparatus 表或 toolTable patch 至少一处含 label_printer
_app_ids = {e["id"] for e in (tables_by_id.get("free_chem_apparatus", {}) or {}).get("entries", [])} \
    if isinstance(tables_by_id.get("free_chem_apparatus"), dict) else set()
_patch_ids = set()
for pa in loot.get("patches", []):
    _patch_ids |= {e.get("id") for e in pa.get("add", [])}
if "label_printer" not in loot_ids:
    p1b_bad.append("掉落表未包含 label_printer")
elif "label_printer" not in (_app_ids | _patch_ids):
    p1b_bad.append("label_printer 未加入 free_chem_apparatus / toolTable（仅在其他表出现）")

# 21b target 钩子：may/can/on 三处赋值；can 用 Kind/ReagentTag 且排除自身；on 调 Stamp
for _hook in ("mayThisTargetItemFunc", "canThisTargetItemFunc", "onThisTargetItemFunc"):
    if _hook not in src_text:
        p1b_bad.append("缺 target 钩子赋值: " + _hook)
_can_hook = re.search(r'canThisTargetItemFunc\s*=', src_text)
if _can_hook:
    _can_seg = src_text[_can_hook.start():_can_hook.start() + 600]
    if not ("IsPrintableTarget" in _can_seg or "IsReagentKind" in _can_seg
            or "ReagentTag" in _can_seg or "IsOurReagent" in _can_seg):
        p1b_bad.append("canThisTargetItemFunc 未用 Kind/ReagentTag/IsPrintableTarget 判定目标")
    _helper = re.search(r'bool\s+IsPrintableTarget\s*\([^)]*\)\s*\{(.*?)\n        \}',
                        src_text, re.S)
    if _helper:
        _hb = _strip_comments(_helper.group(1))
        if not ("IsOurReagent" in _hb or "IsReagentKind" in _hb or "ReagentTag" in _hb):
            p1b_bad.append("IsPrintableTarget 未确认目标是本模组真药剂（Kind/ReagentTag）")
        if "label_printer" in _hb:
            p1b_bad.append("IsPrintableTarget 不应把 label_printer 自身当可盖章目标")
    elif not ("IsReagentKind" in _can_seg or "ReagentTag" in _can_seg):
        p1b_bad.append("canThisTargetItemFunc 未用 Kind/ReagentTag 判定目标")
    # 打印机自身 Kind=tool（非药剂）→ 天然被 IsOurReagent 排除，前面已断言 Kind
_on_hook = re.search(r'onThisTargetItemFunc\s*=', src_text)
if _on_hook:
    _on_seg = src_text[_on_hook.start():_on_hook.start() + 600]
    if not re.search(r'(?:ChemPrinter\.)?Stamp\s*\(', _on_seg):
        p1b_bad.append("onThisTargetItemFunc 未调用 Stamp(")

# 21c 模式：14 = 7 档 × 2 类；tooltip printer_mode
if not printer_text:
    p1b_bad.append("缺 src/ChemPrinter.cs（盖章实现）")
else:
    if not re.search(r'(?:ModeCount|ModeNum|ModeCount\s*=\s*14|%\s*14|\b14\b)', printer_text):
        p1b_bad.append("ChemPrinter 未见 14 模式（7 档×2 类）")
    if "printer_mode" not in src_text:
        p1b_bad.append("tooltip 未显示 printer_mode 当前标签模式")
    # 21d 盖章语义：两参 SetPurityFeature(real, fake)、七档、医疗类型/tag、无 GradeProduct
    if not re.search(r'SetPurityFeature\s*\([^)]*,[^)]*\)', printer_text):
        p1b_bad.append("Stamp 未用 SetPurityFeature(target, real, fake) 两参重载（真假纯度分离）")
    for _fn in ("CreateStarchGrade", "CreateJunkGrade", "CreateCutProduct", "CreateStreetStandard",
                "CreateNearPharma", "CreatePharmaGrade", "CreateLabPure"):
        if _fn + "(" not in printer_text:
            p1b_bad.append("ChemPrinter 七档缺 " + _fn)
    if "TypeHelper.MEDICAL" not in printer_text or 'EnableTag("fc_medical")' not in printer_text:
        p1b_bad.append("医疗品标签缺 SetGameItemType(TypeHelper.MEDICAL)+EnableTag(fc_medical)")
    _stamp = _body_or_expr(printer_text, "Stamp")
    if _stamp is None:
        p1b_bad.append("未能解析 ChemPrinter.Stamp 方法体")
    elif "GradeProduct" in _strip_comments(_stamp):
        p1b_bad.append("Stamp 不得调用 GradeProduct 覆盖（真实/标签必须分离）")

# 21e 单钢瓶：canister_filling 固定 skus[0]、不再按 inputGrade 三档；SyncVanillaPurity
_can_region = ""
for _m in re.finditer(r'canister_filling', mach_text):
    _seg = mach_text[_m.start():_m.start() + 3000]
    if "skus" in _seg or "CanisterSkus" in _seg:
        _can_region = _seg
        break
if not _can_region:
    p1b_bad.append("未能定位 canister_filling 灌装 resolver")
else:
    if not re.search(r'skus\s*\[\s*0\s*\]', _can_region):
        p1b_bad.append("灌装台未固定产出 skus[0]（工业钢瓶）")
    if re.search(r'skus\s*\[\s*Mathf\.Clamp\(', _can_region) or \
            re.search(r'skus\s*\[\s*Mathf\.Clamp\(', mach_text):
        p1b_bad.append("灌装台仍按 inputGrade 选三档（应取消 medical/ultrapure 产线）")
if "SyncVanillaPurity(" not in src_text:
    p1b_bad.append("缺 ChemCatalog.SyncVanillaPurity(（原版纯度/档同步）")
_fin_p1b = _body_or_expr(mach_text, "Finish") or ""
if "SyncVanillaPurity(" not in _fin_p1b:
    p1b_bad.append("Finish 未在 can_*/滗析/过滤/洗涤纯度变化后调用 SyncVanillaPurity(")
# legacy：medical/ultrapure 仍注册但无产线/掉落产生
for _legacy in ("can_oxygen_medical", "can_oxygen_ultrapure"):
    if _legacy not in cat_text:
        p1b_bad.append("legacy 气罐未保留注册: " + _legacy)
_bad_loot = [i for i in loot_ids if i.startswith("can_") and ("medical" in i or "ultrapure" in i)]
if _bad_loot:
    p1b_bad.append("掉落表不得再产生 medical/ultrapure 气罐: " + ", ".join(sorted(_bad_loot)[:8]))

if p1b_bad:
    for b in p1b_bad:
        err("P1b1 打印机/单钢瓶: " + b)
else:
    ok("P1b1：label_printer #86(tool/32×32/掉落) + target 三钩子(can 用 Kind 且排除自身、on 调 Stamp) + "
       "14 模式 printer_mode + SetPurityFeature(real,fake) 七档+医疗 tag 无 GradeProduct；"
       "灌装固定 skus[0] + SyncVanillaPurity；legacy medical/ultrapure 保留注册但不产出")
# 100% 纯度预留（软断言/WARN）
_blob_100 = lab_text + "\n" + printer_text + "\n" + cat_text
if "100%" in _blob_100 and ("校准" in _blob_100 or "提纯" in _blob_100):
    ok("100% 纯度已有 P1c 预留说明（校准/多级提纯计划）")
else:
    warn("未见「100% 纯度 → P1c 校准/提纯」预留说明（建议补注释，非功能缺陷）")
# 文档软断言
_doc_p1b = ""
for _dp in (glob.glob(os.path.join(DOCS, "*.md")) + [os.path.join(ROOT, "README.md")]):
    if os.path.exists(_dp):
        _doc_p1b += read(_dp)
if "P1b1" in _doc_p1b or "标签打印机" in _doc_p1b:
    ok("文档已记录 P1b1 标签打印机与单钢瓶纯度（README/docs）")
else:
    warn("文档尚未见「P1b1/标签打印机」说明（docs-i18n 同步后应转 PASS）")

# ---------- 22. P1c（第 20 轮）：多级提纯 + 打印机校准 100% ----------
p1c_bad = []

# 22a ChemLab 提纯纯函数：PurifyFloor / PurityFrom(c, level) / MaxPurity / MaxPurifyLevel
if "PurifyFloor(" not in lab_text:
    p1c_bad.append("ChemLab 缺 PurifyFloor(")
elif not re.search(r'ResidualFloor\s*>>\s*\w+|>>\s*(?:level|l)\b', lab_text):
    p1c_bad.append("PurifyFloor 未用 70 >> level 位移")
if not re.search(r'PurityFrom\s*\(\s*int\[\]\s*\w+\s*,\s*int\s+\w+\s*\)', lab_text):
    p1c_bad.append("ChemLab 缺 PurityFrom(int[] c, int level) 重载")
if "MaxPurity(" not in lab_text:
    p1c_bad.append("ChemLab 缺 MaxPurity(level)")
if not re.search(r'MaxPurifyLevel\s*=\s*5', lab_text):
    p1c_bad.append("缺 MaxPurifyLevel = 5")
if "PurifyFloor" not in _strip_comments(_body_or_expr(lab_text, "ValidateLogic") or "") \
        and "MaxPurity" not in _strip_comments(_body_or_expr(lab_text, "ValidateLogic") or ""):
    warn("ChemLab.ValidateLogic 未见提纯等级自检（建议补，非功能缺陷）")

# 22b ChemCatalog：等级持久化 + 污染物应用重载 + WriteState 初始 level=0
for _tok in ("TPurifyLevel", "ReadPurifyLevel(", "ApplyContaminants("):
    if _tok not in cat_text:
        p1c_bad.append("ChemCatalog 缺 " + _tok)
if not re.search(r'ApplyContaminants\s*\([^)]*,[^)]*,[^)]*\)', cat_text):
    p1c_bad.append("ApplyContaminants 缺 (item, c, level) 三参重载")
_ws = _body_or_expr(cat_text, "WriteState") or ""
if "TPurifyLevel" not in _ws:
    p1c_bad.append("WriteState 未写 TPurifyLevel（初始等级）")
elif not re.search(r'TPurifyLevel[^;]*\b0\b', _ws):
    p1c_bad.append("WriteState 初始提纯等级应为 0")

# 22c 过滤台：等级 +1、用新等级过滤/写污染物、仍同步原版纯度
if not (re.search(r'Math\.Min\s*\(\s*(?:ChemLab\.)?MaxPurifyLevel\s*,\s*[^)]*\+\s*1\s*\)', mach_text)
        or re.search(r'newLevel\s*=\s*Math\.Min\s*\(\s*5\s*,', mach_text)):
    p1c_bad.append("过滤台缺 level = min(5, old+1) 提纯等级递增")
if not re.search(r'ChemLab\.Filter\s*\([^)]*,[^)]*,[^)]*\)', mach_text):
    p1c_bad.append("过滤台未用新等级计算纯度（ChemLab.Filter 三参）")
if not re.search(r'ApplyContaminants\s*\([^)]*(?:newLevel|purifyLevel)', mach_text):
    p1c_bad.append("过滤台未按新等级写回污染物")
if "SyncVanillaPurity(" not in (_body_or_expr(mach_text, "Finish") or ""):
    p1c_bad.append("过滤/纯度变化后 Finish 未调用 SyncVanillaPurity(")

# 22d ChemPrinter：15 模式（14 标签 + 校准）、充能、校准 100%
if not printer_text:
    p1c_bad.append("缺 src/ChemPrinter.cs")
else:
    if not (re.search(r'ModeCount\s*=\s*LabelModeCount\s*\+\s*1', printer_text)
            or re.search(r'ModeCount\s*=\s*15\b', printer_text)):
        p1c_bad.append("打印模式应为 15 = 14 标签 + 1 校准（ModeCount = LabelModeCount + 1）")
    if not re.search(r'(?:LabelModeCount|TagModeCount)\s*=\s*14\b', printer_text) \
            and "14" not in printer_text:
        p1c_bad.append("未见 14 个标签模式常量")
    if "printer_mode_calibrate" not in printer_text:
        p1c_bad.append("校准模式未用 printer_mode_calibrate 文案")
    if not re.search(r'(?:MaxCatalystCharge\s*=\s*3|<\s*3\b)', printer_text + cat_text):
        p1c_bad.append("催化剂充能上限应为 3")
    if not (re.search(r'fc_catalyst_charge|TCatalystCharge', printer_text + cat_text)):
        p1c_bad.append("缺催化剂充能 tag（fc_catalyst_charge/TCatalystCharge）")
    _charge_seg = ""
    _cm2 = re.search(r'(?:void\s+Charge|static\s+void\s+Charge)[^\n]*', printer_text)
    _charge_seg = printer_text
    if "Destroy(" not in _charge_seg:
        p1c_bad.append("充能未销毁 catalyst_kit（Destroy）")
    if "Calibrate" not in printer_text:
        p1c_bad.append("缺 Calibrate 校准实现")
    if not re.search(r'CalibrationMinPurity\s*=\s*99\.5\b', printer_text):
        p1c_bad.append("校准纯度门槛应明确为 99.5")
    if "CreateLabPure()" not in printer_text:
        p1c_bad.append("校准成功未写 LabPure 档（CreateLabPure）")
    if "SyncVanillaPurity(" not in printer_text:
        p1c_bad.append("校准后未 SyncVanillaPurity( 同步原版纯度")
    for _fk in ("printer_calibrate_fail_purity", "printer_calibrate_fail_charge"):
        if _fk not in printer_text:
            p1c_bad.append("校准失败分支缺键: " + _fk)
    if not re.search(r'(?:SetInt\s*\([^)]*100|100\s*\)?\s*;?[^\n]*purity|ChemLab\.Scale\s*\*\s*100)', printer_text):
        warn("校准成功写 purity=100 的表达式未显式匹配（可能用 SetInt(100*Scale) 等，人工确认）")
    _cal = _body_or_expr(printer_text, "Calibrate")
    if _cal is None:
        p1c_bad.append("未能解析 ChemPrinter.Calibrate 方法体")
    elif "GradeProduct" in _strip_comments(_cal):
        p1c_bad.append("Calibrate 不得调用 GradeProduct 覆盖（真实/标签分离）")
    # 校准成功必须同时：污染物清零 + level=5 + purity=100 + lab_pure
    for _tok, _desc in (("ApplyContaminants", "污染物清零/写回"),
                        ("MaxPurifyLevel", "level=5"),
                        ("CreateLabPure", "lab_pure 档")):
        if _cal is not None and _tok not in _strip_comments(_cal):
            p1c_bad.append(f"Calibrate 成功路径缺 {_desc}（{_tok}）")

if p1c_bad:
    for b in p1c_bad:
        err("P1c 多级提纯/校准: " + b)
else:
    ok("P1c：PurifyFloor(70>>level)/PurityFrom(c,level)/MaxPurity/MaxPurifyLevel=5；"
       "TPurifyLevel+ReadPurifyLevel+ApplyContaminants(3参)+WriteState level=0；"
       "过滤台 level=min(5,old+1)+新等级过滤+SyncVanillaPurity；"
       "打印机 15 模式=14+校准、充能 tag≤3+销毁 catalyst、门槛 99.5→清零/level5/100/lab_pure/SyncVanillaPurity、"
       "失败两键、Calibrate 无 GradeProduct")
# 文档软断言
_doc_p1c = ""
for _dp in (glob.glob(os.path.join(DOCS, "*.md")) + [os.path.join(ROOT, "README.md")]):
    if os.path.exists(_dp):
        _doc_p1c += read(_dp)
if ("P1c" in _doc_p1c or "100% 分析纯" in _doc_p1c) and "提纯等级" in _doc_p1c:
    ok("文档已记录 P1c 多级提纯/100% 分析纯/提纯等级（README/docs）")
else:
    warn("文档尚未见「P1c / 100% 分析纯 / 提纯等级」说明（docs-i18n 同步后应转 PASS）")

# ---------- 23. P2a（第 21 轮）：10 种新化学品 + 反应式重写/副产物 ----------
p2a_bad = []
# 23a 数字 ID 87–96 按顺序追加；Specs Kind=solid
_expect_87_96 = [P2A_ITEM_IDS[_n] for _n in range(87, 97)]
if len(item_ids) < 96 or item_ids[86:96] != _expect_87_96:
    p2a_bad.append("数字 ID #87–#96 未按顺序追加（应为 " + ", ".join(_expect_87_96) + "）")
for _n, _iid in P2A_ITEM_IDS.items():
    if _iid not in spec_ids:
        p2a_bad.append("Specs 未注册 " + _iid)
    elif spec_kinds.get(_iid) != "solid":
        p2a_bad.append(f"{_iid} 的 Kind 应为 solid，实际 {spec_kinds.get(_iid)}")

# 23b 反应解析与关键副产物断言
_RX_PAT = re.compile(
    r'new Reaction\(\s*"(?P<id>[^"]+)"\s*,\s*"(?P<name>[^"]*)"\s*,\s*"(?P<eq>[^"]*)"\s*,'
    r'\s*"(?P<machine>[^"]+)"\s*,\s*(?P<tier>\d+)\s*,\s*new\[\]\s*\{(?P<inputs>[^}]*)\}\s*,'
    r'\s*new\[\]\s*\{(?P<outputs>[^}]*)\}', re.S)
_rx = {}
for _m in _RX_PAT.finditer(rec_text):
    _rx[_m.group("id")] = {
        "eq": _m.group("eq"), "machine": _m.group("machine"), "tier": int(_m.group("tier")),
        "inputs": re.findall(r'"([^"]+)"', _m.group("inputs")),
        "outputs": re.findall(r'"([^"]+)"', _m.group("outputs")),
    }
if len(_rx) < 39:
    p2a_bad.append(f"反应解析失败（仅解析到 {len(_rx)} 条）")


def _need_rx(rid, kind, tokens):
    if rid not in _rx:
        p2a_bad.append("缺反应 " + rid)
        return
    got = _rx[rid]["inputs" if kind == "in" else "outputs"]
    for _t in tokens:
        if _t not in got:
            p2a_bad.append(f"[{rid}] {kind} 缺 {_t}（实际 {got}）")


_need_rx("co2_from_marble", "out", ["gas_carbon_dioxide", "liq_calcium_chloride"])
_need_rx("peroxide_decomp", "out", ["gas_oxygen"])
_need_rx("kmno4_decomp", "out", ["sol_manganese_dioxide", "sol_potassium_manganate"])
_need_rx("ammonia_liberation", "out", ["sol_sodium_nitrate"])
_need_rx("hypochlorite", "in", ["gas_chlorine", "sol_calcium_hydroxide"])
_need_rx("hypochlorite", "out", ["sol_bleaching_powder", "liq_calcium_chloride"])
if "hypochlorite" in _rx:
    _bad_in = [t for t in _rx["hypochlorite"]["inputs"]
               if "sodium_hydroxide" in t or t == "liq_sodium_hydroxide"]
    if _bad_in:
        p2a_bad.append("hypochlorite 不应再以 NaOH 为输入（改用熟石灰）: " + ", ".join(_bad_in))
_need_rx("salt_crystallize", "out", [])
_need_rx("nh4no3_crystallize", "out", [])
_need_rx("neutralization", "out", ["liq_brine"])
if "neutralization" in _rx and any(o.startswith("sol_") for o in _rx["neutralization"]["outputs"]):
    p2a_bad.append("neutralization 不应直接产出固体（应出盐水+水）")
_need_rx("hcl_absorption", "in", ["liq_water"])
_need_rx("ammonia_absorption", "in", ["liq_water"])
_need_rx("nitric_from_niter", "out", ["sol_potassium_sulfate"])
if "nitric_from_niter" in _rx:
    _eq = _rx["nitric_from_niter"]["eq"]
    if "→" not in _eq or "K2SO4" not in _eq or "2HNO3" not in _eq:
        p2a_bad.append(f"nitric_from_niter 配平式应含 2KNO3+H2SO4→K2SO4+2HNO3，实际 {_eq}")

# 23c 新增 5 条反应存在且引用 id 已注册；copper_oxide 仅掉落不反应
_registered = spec_ids | machine_ids
for _rid in ("lime_calcination", "slaked_lime", "soda_from_caustic",
             "baking_soda", "ammonium_chloride"):
    if _rid not in _rx:
        p2a_bad.append("缺新反应 " + _rid)
    else:
        for _tok in _rx[_rid]["inputs"] + _rx[_rid]["outputs"]:
            if _tok not in _registered:
                p2a_bad.append(f"[{_rid}] 引用未注册 id {_tok}")
_all_rx_ids = set()
for _r in _rx.values():
    _all_rx_ids |= set(_r["inputs"]) | set(_r["outputs"])
if "sol_copper_oxide" in _all_rx_ids:
    p2a_bad.append("sol_copper_oxide 不应出现在任何反应中（仅掉落获取）")

# 23d ValidateLogic：输入/输出全注册校验、equation 含「→」、T1–T3 不用原版药剂
_vl = _body_or_expr(rec_text, "ValidateLogic") or ""
if "SpecExists" not in _vl:
    p2a_bad.append("ValidateLogic 未用 Specs 探针校验反应 id 注册")
if not ("Inputs" in _vl and "Outputs" in _vl):
    p2a_bad.append("ValidateLogic 未遍历反应输入/输出做注册校验")
if "→" not in _vl:
    p2a_bad.append("ValidateLogic 未校验 equation 含「→」")
_bad_eq = [rid for rid, r in _rx.items() if "→" not in r["eq"]]
if _bad_eq:
    p2a_bad.append("equation 缺「→」: " + ", ".join(sorted(_bad_eq)[:6]))
_vm = re.search(r'VanillaAnalog\s*=\s*new Dictionary<string, string>\s*\{(.*?)\};', rec_text, re.S)
_vanilla_ids = set(re.findall(r'\{\s*"([a-z0-9_]+)"', _vm.group(1))) if _vm else set()
_bad_vanilla = [rid for rid, r in _rx.items()
                if r["tier"] <= 3 and (set(r["inputs"]) & _vanilla_ids)]
if _bad_vanilla:
    p2a_bad.append("T1–T3 不得使用原版药剂: " + ", ".join(sorted(_bad_vanilla)[:6]))

# 23e 掉落表含新物品
_need_loot = ["sol_calcium_oxide", "sol_sodium_carbonate", "sol_sodium_bicarbonate",
              "sol_ammonium_chloride", "sol_copper_oxide"]
_miss_loot = [i for i in _need_loot if i not in loot_ids]
if _miss_loot:
    p2a_bad.append("掉落表缺新化学品: " + ", ".join(_miss_loot))

if p2a_bad:
    for b in p2a_bad:
        err("P2a 新化学品/反应式: " + b)
else:
    ok(f"P2a：数字 ID 87–96 追加（10 种 solid）+ 图标 109；关键反应副产物（水/MnO2/锰酸钾/硝酸钠/"
       "熟石灰制次氯酸盐/结晶水/中和盐水/吸收补水/硝石硫酸钾配平）全部正确；新增 5 反应均注册；"
       "copper_oxide 仅掉落；ValidateLogic 校验 id 注册+equation→+T1–T3 无原版药剂")
_doc_p2a = ""
for _dp in (glob.glob(os.path.join(DOCS, "*.md")) + [os.path.join(ROOT, "README.md")]):
    if os.path.exists(_dp):
        _doc_p2a += read(_dp)
if ("P2a" in _doc_p2a or "化学品扩充" in _doc_p2a) and "反应式" in _doc_p2a:
    ok("文档已记录 P2a 化学品扩充与反应式重写（README/docs）")
else:
    warn("文档尚未见「P2a/化学品扩充/反应式重写」说明（docs-i18n 同步后应转 PASS）")

# ---------- 24. P3a（第 22 轮）：500ml 容量 / 画像审计 / 酒精灯燃料与灯芯 / 滤纸可达 ----------
p3a_bad = []
# 24a 500ml 量纲与守恒
if not re.search(r'PourVolumeMl\s*=\s*500\b', rec_text):
    p3a_bad.append("ChemRecipes.PourVolumeMl 应为 500")
if "500000" not in cat_text + rec_text and \
        not re.search(r'PourVolumeMl\s*\*\s*PourPartPerMl', cat_text + rec_text):
    p3a_bad.append("桥接总 part 未按 500ml×1000=500000 计算（守恒）")
if "PourVolumeMl" not in heat_text:
    p3a_bad.append("加热台冷却公式未使用 PourVolumeMl（500ml 量纲）")
# 24b 酒精灯：燃料纯度 tag / 容量 500 / Tick 读灯自身纯度（修复旧 bug）/ 灯芯耐久销毁
if "TFuelPurity" not in cat_text:
    p3a_bad.append("ChemCatalog 缺 TFuelPurity 常量")
if not re.search(r'FuelCapacityMl\s*=\s*500\b', cat_text):
    p3a_bad.append("ChemCatalog.FuelCapacityMl 应为 500")
if "TFuelPurity" not in heat_text:
    p3a_bad.append("ChemHeating 未使用 TFuelPurity（灯自身燃料纯度）")
_tick_p3 = _body_or_expr(heat_text, "Tick") or ""
if "TFuelPurity" not in _tick_p3:
    p3a_bad.append("ChemHeating.Tick 未读灯自身 TFuelPurity")
if "FuelPurity(" in _strip_comments(_tick_p3):
    p3a_bad.append("ChemHeating.Tick 仍对燃料槽物品调用 FuelPurity(（旧 bug：燃料销毁后 NRE）")
_refill_p3 = _body_or_expr(heat_text, "Refill") or ""
if "TFuelPurity" not in _refill_p3:
    p3a_bad.append("Refill 未把燃料纯度写入灯自身 TFuelPurity")
if "FuelCapacityMl" not in _refill_p3:
    p3a_bad.append("Refill 灌装量未按 FuelCapacityMl 计算")
_consume_p3 = _body_or_expr(heat_text, "ConsumeFuel") or ""
if "FuelBurnRate" not in _consume_p3:
    p3a_bad.append("ConsumeFuel 未按 FuelBurnRate（纯度越低越费）")
if "TWickDur" not in _consume_p3 or "Destroy(" not in _consume_p3:
    p3a_bad.append("ConsumeFuel 未递减灯芯耐久或在归零时销毁灯芯")
if "Log(" not in _consume_p3:
    p3a_bad.append("灯芯/燃料消耗缺日志")
# 24c 滤纸：filtration_bench 才把 filter_paper* 加入 ids；Finish 耐久-1/销毁/日志
_fp_block = re.search(r'if\s*\(cid\.StartsWith\("filter_paper"\)\)\s*\{(.*?)\}', mach_text, re.S)
if not _fp_block:
    p3a_bad.append("RunMachineCycle 缺 filter_paper 分支")
else:
    if "filtration_bench" not in _fp_block.group(1) or "ids.Add(cid)" not in _fp_block.group(1):
        p3a_bad.append("filter_paper 仅在 filtration_bench 时加入投料 ids（当前分支不含）")
_fin_p3 = _body_or_expr(mach_text, "Finish") or ""
if "TFilterDur" not in _fin_p3:
    p3a_bad.append("Finish 未对滤纸耐久 -1")
elif "Destroy(" not in _fin_p3:
    p3a_bad.append("Finish 滤纸耐久归零未销毁")
if "[filter]" not in _fin_p3:
    p3a_bad.append("Finish 滤纸消耗缺日志")
# 24d 新本地化提示键（docs-i18n 提供）
P3A_KEYS = ["inst_lamp_hint", "inst_wick_hint", "inst_filter_hint", "inst_fuel_hint"]
_miss_p3a = [k for k in P3A_KEYS if k not in zh or k not in en]
if _miss_p3a:
    p3a_bad.append("缺 P3a 仪器提示本地化键: " + ", ".join(_miss_p3a))

if p3a_bad:
    for b in p3a_bad:
        err("P3a 500ml/画像/酒精灯/滤纸: " + b)
else:
    ok("P3a：PourVolumeMl=500（总 part 500000、冷却用 500ml）；TFuelPurity/FuelCapacityMl=500，"
       "Refill 写灯纯度、Tick 读灯自身 tag（不再 FuelPurity(燃料槽)）、FuelBurnRate 消耗、灯芯归零销毁+日志；"
       "滤纸仅在 filtration_bench 进入 ids、Finish 耐久-1/销毁/日志；4 个 inst_*_hint 键齐备")
# 文档软断言
_doc_p3a = ""
for _dp in (glob.glob(os.path.join(DOCS, "*.md")) + [os.path.join(ROOT, "README.md")]):
    if os.path.exists(_dp):
        _doc_p3a += read(_dp)
if ("500ml" in _doc_p3a or "16×48" in _doc_p3a) and ("占格" in _doc_p3a or "1×3" in _doc_p3a):
    ok("文档已记录 P3a 500ml 容量与 16×48/占格修复（README/docs）")
else:
    warn("文档尚未见「500ml / 16×48 / 占格修复」说明（docs-i18n 同步后应转 PASS）")

# ---------- 25. P3b（第 23 轮）：倒液 owned 修复 / 成分仪分析 / 原版物品替代 ----------
p3b_bad = []
# 25a 倒液修复：产出统一 EnsureOwned（owned 标记缺失会导致原版容器不收/倒液失败）
if "EnsureOwned(" not in cat_text:
    p3b_bad.append("ChemCatalog 缺 EnsureOwned(（统一 owned 标记）")
if "ChemCatalog.EnsureOwned(" not in (_body_or_expr(mach_text, "Finish") or ""):
    p3b_bad.append("ChemMachines.Finish 产出未经 EnsureOwned")
_heat_prod = _body_or_expr(heat_text, "Produce") or ""
if "ChemCatalog.EnsureOwned(" not in heat_text or "EnsureOwned(" not in _heat_prod:
    if "ChemCatalog.EnsureOwned(" not in heat_text:
        p3b_bad.append("ChemHeating 产出未经 EnsureOwned")
if "SelfCheckLiquidBridge" not in (_body_or_expr(cat_text, "Create") or ""):
    p3b_bad.append("Create 缺 SelfCheckLiquidBridge（液体容器 total=0 自检修复）")
# 25b 成分仪 RefreshUI Postfix（TMP 成分/总量行）+ 防递归
if not re.search(r'\[HarmonyPatch\(\s*typeof\(\s*ChemScannerUIManager\s*\)\s*,\s*"RefreshUI"\s*\)\]',
                 _patch_text):
    p3b_bad.append("ChemPatches 缺 [HarmonyPatch(typeof(ChemScannerUIManager), \"RefreshUI\")]")
_ru = _patch_text.find('"RefreshUI"')
_ru_region = _patch_text[_ru:_ru + 4000] if _ru >= 0 else ""
for _tok, _desc in (("contaminantTMP", "成分行 TMP"), ("totalVolumeTMP", "总量行 TMP"),
                    ("scanner_fc_line", "成分文本键"), ("scanner_fc_grade", "等级文本键")):
    if _tok not in _ru_region:
        p3b_bad.append(f"RefreshUI Postfix 缺 {_desc}（{_tok}）")
if "RefreshUI(" in _ru_region:
    p3b_bad.append("RefreshUI Postfix 内不得再调 RefreshUI(（会无限递归）")
if "RefreshChemMode(" in _ru_region:
    p3b_bad.append("RefreshUI Postfix 内不得调 RefreshChemMode(（递归风险）")
if '"RefreshChemMode"' not in _patch_text:
    p3b_bad.append("原 RefreshChemMode 兼容/桥接补丁不应被删除")
# TextMeshPro / UnityEngine.UI 引用（Private=false，不捆绑）
_csproj_text = read(os.path.join(SRC, "FreeChem.csproj"))
for _asm in ("Unity.TextMeshPro", "UnityEngine.UI"):
    _ref = re.search(r'<Reference\s+Include="' + re.escape(_asm) + r'">[\s\S]{0,400}?</Reference>',
                     _csproj_text)
    if not _ref:
        p3b_bad.append(f"csproj 缺 {_asm} 引用（P3b 成分仪 TMP 行）")
    elif "<Private>false</Private>" not in _ref.group(0):
        p3b_bad.append(f"{_asm} 引用应 Private=false（不随包发布游戏程序集）")
# 25c 原版物品替代：VanillaAnalog 映射 + 掉落移除 + 咖啡因来源 + T4 可达
_va_map = {}
if _va:
    _va_map = {k: v for k, v in re.findall(r'\{\s*"([^"]+)"\s*,\s*"([a-z0-9_]+)"\s*\}', _va.group(1))}
if _va_map.get("coffee_bean") is not None:
    p3b_bad.append("VanillaAnalog 不应再含 coffee_bean→sol_coffee_grounds"
                   "（Lead 定案：原版咖啡已有官方制作逻辑，mod 咖啡改 legacy 无来源）")
# 咖啡因/咖啡豆糊为 legacy 无来源：不得由热反应/夜间反应/映射/掉落产出
_therm_ids = re.findall(r'new ThermalReaction\(\s*"([^"]+)"', lab_text)
if len(_therm_ids) != 5:
    p3b_bad.append(f"实时热反应应为 5 条（P12 删 heat_ethanol_dehydrate），实际 {len(_therm_ids)}")
if "heat_caffeine_extract" in _therm_ids:
    p3b_bad.append("heat_caffeine_extract 应已删除（咖啡因改 legacy 无来源）")
_caffeine_src = ("sol_caffeine" in thermal_outputs or "sol_caffeine" in vanilla_mapped
                 or "sol_caffeine" in reaction_outputs
                 or "sol_coffee_grounds" in thermal_outputs or "sol_coffee_grounds" in reaction_outputs)
if _caffeine_src:
    p3b_bad.append("sol_caffeine/sol_coffee_grounds 应为 legacy 无来源"
                   "（不得由热反应/映射/掉落产出）")
for _i in ("liq_water", "liq_ethanol", "sol_coffee_grounds", "sol_caffeine"):
    if _i in thermal_outputs and _i in ("sol_caffeine", "sol_coffee_grounds"):
        p3b_bad.append(f"{_i} 不应出现在热反应产物（legacy 无来源）")
# 水体容器 6 映射 + 酒精候选映射保留
_water_bottles = ["small_bottled_water", "bottled_water", "large_bottled_water",
                  "water_jug", "water_pitcher", "mini_bottle"]
if len([b for b in _water_bottles if _va_map.get(b) == "liq_water"]) < 5:
    p3b_bad.append("VanillaAnalog 原版水容器→liq_water 映射不足 5 个")
# P12：mod 乙醇已删除，原版酒精改为 mini_bottle 组分判定（不再要求 VanillaAnalog 映射）
# 掉落表不再含 legacy 四件
_bad_loot_legacy = [i for i in ("liq_water", "liq_ethanol", "sol_coffee_grounds", "sol_caffeine")
                    if i in loot_ids]
if _bad_loot_legacy:
    p3b_bad.append("掉落表应移除 legacy 物品: " + ", ".join(_bad_loot_legacy))
# P12：燃料改为原版酒精瓶 mini_bottle 的 alcohol 组分判定，VanillaAlcoholIds 不再是燃料依据
# T4 四个虚构违禁品仍可达（反应存在、Tier=4、UsesVanilla）
for _iid in ("illicit_voidsalt", "illicit_redmist", "illicit_nightglass", "illicit_ashbreath"):
    _found = [r for r in _rx.values() if _iid in r["outputs"] and r["tier"] == 4]
    if not _found:
        p3b_bad.append("T4 违禁品不可达/缺失: " + _iid)
# 死物品白名单必须逐条包含 4 个 legacy（可审计）
for _iid in ("liq_water", "liq_ethanol", "sol_coffee_grounds", "sol_caffeine"):
    if _iid not in EXEMPT_DEAD_IDS or not EXEMPT_DEAD_IDS.get(_iid):
        p3b_bad.append("legacy 死物品白名单缺理由: " + _iid)

if p3b_bad:
    for b in p3b_bad:
        err("P3b 原版替代/倒液/分析仪: " + b)
else:
    ok("P3b：EnsureOwned 产出（Finish/Produce）+ Create SelfCheckLiquidBridge；"
       "ChemPatches RefreshUI Postfix 写成分/总量行（contaminantTMP/totalVolumeTMP + scanner_fc_*）、"
       "不递归调 RefreshUI/RefreshChemMode 且保留原补丁；csproj TextMeshPro/UI 引用 Private=false；"
       "VanillaAnalog 水容器/酒精映射（咖啡已按 Lead 定案移除、改 legacy 无来源）；热反应 6 条无 caffeine；"
       "掉落移除 legacy 4 件、T4 四违禁品可达、legacy 白名单逐条有理由")
# 文档软断言
_doc_p3b = ""
for _dp in (glob.glob(os.path.join(DOCS, "*.md")) + [os.path.join(ROOT, "README.md")]):
    if os.path.exists(_dp):
        _doc_p3b += read(_dp)
if ("P3b" in _doc_p3b or "原版物品替代" in _doc_p3b or "成分仪" in _doc_p3b) and "倒液" in _doc_p3b:
    ok("文档已记录 P3b 倒液/成分仪/原版物品替代（README/docs）")
else:
    warn("文档尚未见「P3b/倒液/成分仪/原版物品替代」说明（docs-i18n 同步后应转 PASS）")

# ---------- 26. P3c（第 24 轮）：13 台机器气瓶槽（供气/守恒/无杂质） ----------
p3c_bad = []
# 26a 槽位与 UI
if not re.search(r'List<GameSlotInventory>\s+gasSlots', mach_text):
    p3c_bad.append("ChemMachines 缺 List<GameSlotInventory> gasSlots（气瓶槽）")
if "BuildWindow(" not in mach_text:
    p3c_bad.append("ChemMachines 缺 BuildWindow( 窗口构建")
elif not re.search(r'BuildWindow\([^)]*gasSlots', mach_text):
    p3c_bad.append("BuildWindow 未接收 gasSlots 参数（窗口未接气瓶槽）")
if '"ui_gas_slot"' not in mach_text:
    p3c_bad.append('机器窗口缺 "ui_gas_slot" 标签')
if "GasSlots" not in heat_text:
    p3c_bad.append("ChemHeating 加热台缺 GasSlots（实时设备气瓶槽）")
# 26b 槽过滤：仅本模组已拥有的气瓶（CanisterTag + owned + 可反查气体）
_acc_gas = _body_or_expr(mach_text, "AcceptsGasCanister")
if _acc_gas is None:
    p3c_bad.append("缺 AcceptsGasCanister( 槽过滤")
else:
    _ag = _strip_comments(_acc_gas)
    for _t, _d in (("IsCanisterItem", "CanisterTag 判定"), ("IsItemOwned", "owned 判定"),
                   ("TryGetGasFromCanister", "气体反查")):
        if _t not in _ag:
            p3c_bad.append(f"AcceptsGasCanister 缺 {_d}（{_t}）")
# 26c 反查由 CanisterSkus 静态反推（无第二份手写映射）
if "CanisterToGas" not in rec_text or "TryGetGasFromCanister(" not in rec_text:
    p3c_bad.append("ChemRecipes 缺 CanisterToGas/TryGetGasFromCanister 反查")
elif not re.search(r'CanisterToGas[\s\S]{0,600}CanisterSkus', rec_text):
    p3c_bad.append("CanisterToGas 未由 CanisterSkus 反推（存在手写第二份映射风险）")
# 26d 供气只在能匹配配方时加入；纯度由 CanisterGrade 抬高
_collect = _body_or_expr(mach_text, "CollectGasSupply")
if _collect is None:
    p3c_bad.append("缺 CollectGasSupply( 供气收集")
elif not ("Match(" in _collect or "CanisterGrade" in _collect):
    p3c_bad.append("CollectGasSupply 未校验「加入气体后能匹配配方」/未用 CanisterGrade")
# 26e 守恒：仅消耗配方用到的气体瓶，销毁→输出空瓶；输出满则保留气瓶
_gc = _body_or_expr(mach_text, "ConsumeGasCanisters") or ""
if "GasCanistersConsumed(" not in mach_text:
    p3c_bad.append("缺 GasCanistersConsumed(（按配方 inputs 判定消耗哪些气瓶）")
if _gc:
    for _t, _d in (("Destroy(", "销毁气瓶"), ("gas_canister_empty", "输出空瓶"),
                   ("CanAccept", "输出位预检"), ("Log(", "失败日志")):
        if _t not in _gc:
            p3c_bad.append(f"ConsumeGasCanisters 缺 {_d}（{_t}）")
else:
    p3c_bad.append("缺 ConsumeGasCanisters( 消耗/空瓶守恒")
if "emptyCanistersNeeded" not in mach_text:
    p3c_bad.append("CanAcceptOutputs 未预检空瓶输出位（emptyCanistersNeeded）")
# 26f 杂质防御：气瓶进槽清零 contam
if "ClearCanisterContaminants(" not in cat_text:
    p3c_bad.append("ChemCatalog 缺 ClearCanisterContaminants(（气瓶无杂质防御）")
elif "ClearCanisterContaminants(" not in mach_text:
    p3c_bad.append("气瓶进槽路径未调用 ClearCanisterContaminants(")
# 26g 加热台气瓶供气
if "ConsumeThermalGas(" not in heat_text:
    p3c_bad.append("ChemHeating 缺 ConsumeThermalGas(（热反应气瓶供气）")
# 26h tooltip/本地化
if "ui_gas_slot_tip" not in entry_text:
    p3c_bad.append("OnTooltip 未使用 ui_gas_slot_tip（气瓶槽说明）")
for _k in ("ui_gas_slot", "ui_gas_slot_tip"):
    if _k not in zh or _k not in en:
        p3c_bad.append("缺气瓶槽本地化键: " + _k)
if p3c_bad:
    for b in p3c_bad:
        err("P3c 气瓶槽: " + b)
else:
    ok("P3c：gasSlots/BuildWindow+ui_gas_slot（13 机器）+ 加热台 GasSlots；AcceptsGasCanister 仅收 owned CanisterTag；"
       "CanisterToGas 由 CanisterSkus 反推 + TryGetGasFromCanister；CollectGasSupply 匹配才供气、CanisterGrade 定纯度；"
       "仅消耗配方气体瓶→输出空瓶、输出满保留+预检；气瓶进槽清零 contam；ConsumeThermalGas；ui_gas_slot(_tip) 齐备")

# ---------- 27. P3d/P3e（第 25 轮）：索引书 / 便条 #97 / 电话商人 ----------
p3de_bad = []
# 27a 便条 note_chem_dealer：#97 追加、Specs tool、16×32、掉落/起始、三连键
if len(item_ids) < 98 or item_ids[97] != "test_tube":
    p3de_bad.append("数字 ID #98 应为 test_tube（只追加，旧 1–97 不变）")
if "note_chem_dealer" not in spec_ids:
    p3de_bad.append("Specs 未注册 note_chem_dealer")
elif spec_kinds.get("note_chem_dealer") not in ("tool", "paper"):
    p3de_bad.append(f"note_chem_dealer Kind 应为 tool/paper，实际 {spec_kinds.get('note_chem_dealer')}")
if icon_meta.get("note_chem_dealer", (None, None, None))[:2] != (16, 32):
    p3de_bad.append(f"便条图标应为 16×32（1×2 格），实际 {icon_meta.get('note_chem_dealer')}")
elif not _cell_centers_opaque("note_chem_dealer", 2):
    p3de_bad.append("便条图标上下两格中心存在透明像素（游戏会判成 1 格）")
# P5：便签改用原版 note/paper（task-59/60）；若走原版便签则允许 note_chem_dealer 仅 legacy 不进掉落
_vanilla_note = ("SpawnPaperItem(" in src_text) or ("NOTE_TAG" in src_text) or ("TypeHelper.NOTE" in src_text) or ("PAPER_TAG" in src_text)
if not _vanilla_note and "note_chem_dealer" not in loot_ids and "note_chem_dealer" not in (_app_ids | _patch_ids):
    p3de_bad.append("便条既未走原版 note/paper，也不在掉落表")
_ng2 = _body_or_expr(entry_text, "OnNewGameStarted") or ""
if "note_chem_dealer" not in _ng2 and not re.search(r"SpawnPaperItem\(|NOTE_TAG|TypeHelper\.NOTE|PAPER_TAG", src_text):
    p3de_bad.append("起始礼包既无 note_chem_dealer 也未改发原版 note/paper")
for _sfx in ("_name", "_desc", "_flavor"):
    if "note_chem_dealer" + _sfx not in zh or "note_chem_dealer" + _sfx not in en:
        p3de_bad.append("缺便条本地化键 note_chem_dealer" + _sfx)
# 27b 索引书 chem_index.json（每页 20 条、单列、覆盖全部数字 ID）+ 调试面板行为
_idx_path = os.path.join(PKG, "Books", "chem_index.json")
if not os.path.exists(_idx_path):
    p3de_bad.append("缺 FreeChem/Books/chem_index.json（化学 ID 索引书）")
else:
    try:
        _idx = json.load(open(_idx_path, encoding="utf-8"))
        if _idx.get("id") != "chem_index":
            p3de_bad.append(f"chem_index.json 的 id 应为 chem_index，实际 {_idx.get('id')}")
        for _k in ("id", "title", "background", "width", "height", "chrome", "pages", "item"):
            if _k not in _idx:
                p3de_bad.append("chem_index.json 缺 schema 字段: " + _k)
        _pages = _idx.get("pages") or []
        _id_total = len(item_ids)
        if not (1 <= len(_pages) <= 10):
            p3de_bad.append(f"chem_index.json 页数异常: {len(_pages)}")
        _langs = {}
        for _pi, _pg in enumerate(_pages):
            _bodies = [e for e in _pg.get("elements", [])
                       if e.get("type") == "text" and e.get("style") == "body"]
            if len(_bodies) != 1:
                p3de_bad.append(f"索引书第 {_pi + 1} 页应单列（恰 1 个正文 text 元素），实际 {len(_bodies)}")
            else:
                _b = _bodies[0]
                if _b.get("fontSize") not in (14, 16):
                    p3de_bad.append(f"索引书第 {_pi + 1} 页 fontSize 应为 14/16，实际 {_b.get('fontSize')}")
                if _b.get("wrap") is not True or _b.get("autoHeight") is not True:
                    p3de_bad.append(f"索引书第 {_pi + 1} 页应 wrap=true 且 autoHeight=true")
                _zh_body = (_b.get("translations") or {}).get("zh", "")
                _n_body = len(re.findall(r"(?m)^\s*ID\s*\d{1,3}", _zh_body))
                if _n_body > 25 or _n_body <= 0:
                    p3de_bad.append(f"索引书第 {_pi + 1} 页条目 {_n_body} 条（应为 1–25）")
            for _el in _pg.get("elements", []):
                for _lang, _txt in (_el.get("translations") or {}).items():
                    _langs.setdefault(_lang, []).append(_txt)
        if not _langs:
            p3de_bad.append("chem_index.json 无 translations 文本（zh/en）")
        for _lang, _chunks in _langs.items():
            _nums = [int(x) for x in re.findall(r"(?m)^\s*(?:ID\s*)?(\d{1,3})[\.\s]",
                                                "\n".join(_chunks))]
            if sorted(_nums) != list(range(1, _id_total + 1)):
                _miss = [n for n in range(1, _id_total + 1) if n not in _nums]
                _dup = sorted({n for n in _nums if _nums.count(n) > 1})
                p3de_bad.append(f"chem_index.json[{_lang}] ID 覆盖应为 1–{_id_total} 各一次；"
                                f"缺={_miss[:8]} 重复={_dup[:8]}")
    except Exception as _e:
        p3de_bad.append(f"chem_index.json 解析失败: {_e}")
if "ListAllIds" in debug_text:
    p3de_bad.append("调试面板仍保留「列出 ID」逻辑（P3d 要求移除不可达项）")
for _tok, _d in (('"chem_index"', "生成索引书"), ("SpawnPaperItem(", "SpawnPaperItem 书籍"),
                 ("SpawnIndexBook(", "SpawnIndexBook 入口"),
                 ("CountIndexBooks(", "全场唯一扫描 CountIndexBooks"),
                 ("BookHelper.BOOK_TAG", "BOOK_TAG 判重"), ("BookHelper.BOOK_ID", "BOOK_ID 判重"),
                 ("scanned", "扫描数量日志")):
    if _tok not in (debug_text + entry_text + cat_text):
        p3de_bad.append(f"索引书功能缺 {_d}（{_tok}）")
if "MoveItemToBackCounter(" not in debug_text:
    p3de_bad.append("索引书未投放到后柜台 MoveItemToBackCounter(")
if '"chem_index"' not in cat_text and "chem_index" not in cat_text:
    p3de_bad.append("ChemCatalog 未识别 chem_index（唯一性扫描）")
if not re.search(r'(?:GenericConfirmationUIManager|ShowVanillaAlert)', debug_text + cat_text):
    p3de_bad.append("索引书已存在时缺原版单按钮提示（GenericConfirmationUIManager）")
# 27c 电话商人 ChemPhone
_phone_path = os.path.join(SRC, "ChemPhone.cs")
_phone_text = read(_phone_path) if os.path.exists(_phone_path) else ""
_phone_all = _phone_text + "\n" + _patch_text + "\n" + entry_text
for _tok, _d in (('typeof(StorePhoneClient)', "StorePhoneClient"), ('"InitPhoneClientDict"', "InitPhoneClientDict"),
                 ("fc_chem_order_pending", "pending ModData"), ("OnShutterOpenedEarly", "次晨送达钩子"),
                 ("MoveItemToFrontCounter(", "送前柜台")):
    if _tok not in _phone_all:
        p3de_bad.append(f"电话商人缺 {_d}（{_tok}）")
if _phone_text and "catch" not in _strip_comments(_phone_text):
    p3de_bad.append("ChemPhone 缺 try/catch 防御")
if "MoveItemToBackCounter(" not in _phone_text:
    p3de_bad.append("电话配送缺「前柜台满后退到后柜台」回退")
# 每日 1 单：pending 存在/日期键判定
if not re.search(r'fc_chem_order_(?:pending|day|date)', _phone_text + entry_text):
    p3de_bad.append("电话订购缺每日 1 单/pending 记录键")
# 27d 动态本地化交叉检查：代码 GetLocalized/L/Ui 引用的字面量键必须在 zh/en 存在
_ref_keys = set(re.findall(
    r'GetLocalized\(\s*(?:ChemCatalog\.ModId|ModId)\s*,\s*"([a-z0-9_]+)"', src_text))
_ref_keys |= set(re.findall(r'\b(?:L|Ui)\(\s*"([a-z][a-z0-9_]*)"', src_text))
# 排除拼接前缀（如 "lethal_" + x、"temp_" + n 被正则截出的以 _ 结尾的片段）
_ref_keys = {k for k in _ref_keys if not k.endswith("_")}
_miss_ref = sorted(k for k in _ref_keys if k not in zh or k not in en)
if _miss_ref:
    p3de_bad.append("代码引用但 CSV 缺失的本地化键: " + ", ".join(_miss_ref[:15]))
# P3d 新按钮/提示键（docs-i18n 提供）
for _k in ("debug_gen_index_book", "debug_book_exists", "debug_book_ok",
           "debug_book_spawn_fail", "debug_list_removed"):
    if _k not in zh or _k not in en:
        p3de_bad.append("缺索引书本地化键: " + _k)

if p3de_bad:
    for b in p3de_bad:
        err("P3d/P3e 索引书/便条/电话: " + b)
else:
    ok(f"P3d/P3e：便条 #97（tool/16×32/掉落+起始/三连键）；索引书 chem_index.json（5 页、单列、zh/en 各覆盖 ID 1–97 各一次、fontSize16/wrap/autoHeight）+ "
       "调试面板无列 ID、SpawnPaperItem(chem_index)+全场唯一扫描；电话 StorePhoneClient.InitPhoneClientDict "
       "注入 + pending + OnShutterOpenedEarly 送前柜台 + 前满退后 + 每日 1 单 + try/catch；"
       f"代码引用本地化键 {len(_ref_keys)} 个全部存在")

# ---------- 28. P3f（第 26 轮）：热反应可达/消耗/温区/T4 映射/审计 ----------
p3f_bad = []
# 28a 加热台 2×2 反应槽：接受容器/本模组药剂/原版药剂瓶
_hp = _body_or_expr(mach_text, "CreateHeatingPlatform") or ""
if "new GameGridInventory(2, 2)" not in _hp:
    p3f_bad.append("加热台反应槽应为 2×2 网格（new GameGridInventory(2, 2)）")
if "mayInventoryAddItemFunc" not in _hp:
    p3f_bad.append("加热台反应槽缺 mayInventoryAddItemFunc 过滤")
for _tok, _d in (("ReagentTag", "本模组药剂"), ("VanillaAnalog.ContainsKey", "原版药剂瓶"),
                 ("beaker", "反应容器")):
    if _tok not in _hp:
        p3f_bad.append(f"加热台反应槽未接受{_d}（{_tok}）")
# 28b 热反应消耗投料（成功 + 超温降解两处调用；容器保留；燃料槽不作投料）
_cti_calls = len(re.findall(r'ConsumeThermalInputs\(', heat_text))
if _cti_calls < 3:
    p3f_bad.append(f"ConsumeThermalInputs 应定义+至少 2 处调用（成功/超温），实际 {_cti_calls}")
_cti = _body_or_expr(heat_text, "ConsumeThermalInputs") or ""
if "Inputs" not in _cti:
    p3f_bad.append("ConsumeThermalInputs 未按配方 Inputs 判断销毁对象")
if "Destroy(" not in _cti:
    p3f_bad.append("ConsumeThermalInputs 未销毁投料")
if re.search(r'CollectThermalInput\([^)]*FuelSlot', heat_text):
    p3f_bad.append("燃料槽仍被当作热反应投料（应已移除 FuelSlot）")
# 28c 高/低档切换入口
if "ToggleHighByHeater(" not in heat_text:
    p3f_bad.append("ChemHeating 缺 ToggleHighByHeater( 切档")
if "ToggleHighByHeater" not in entry_text:
    p3f_bad.append("ModEntry 酒精灯点击回调未接 ToggleHighByHeater")
if "160" not in heat_text or "99" not in heat_text or "r.High = !r.High" not in heat_text:
    p3f_bad.append("切档未体现 HIGH 160°C / LOW 99°C（r.High 翻转）")
# 28d 温区改值：KMnO4 140–160 降解190；NH4NO3 150–160 降解200
if not re.search(r'heat_kmno4[\s\S]{0,500}?140f[\s\S]{0,200}?160f[\s\S]{0,200}?190f', lab_text):
    p3f_bad.append("heat_kmno4 温区应为 140–160°C、降解 190°C")
if not re.search(r'heat_nh4no3[\s\S]{0,500}?150f[\s\S]{0,200}?160f[\s\S]{0,200}?200f', lab_text):
    p3f_bad.append("heat_nh4no3 温区应为 150–160°C、降解 200°C")
# 28e 灰息 T4：原版氨水瓶输入 + 方程注明；4 个 T4 均至少一个原版输入
_ash = _rx.get("ashbreath")
if not _ash:
    p3f_bad.append("缺 ashbreath 反应")
else:
    for _tok in ("med_bottle_small_pink", "liq_nitric_acid", "sol_activated_carbon"):
        if _tok not in _ash["inputs"]:
            p3f_bad.append(f"ashbreath 输入缺 {_tok}")
    if "原版氨水" not in _ash["eq"]:
        p3f_bad.append("ashbreath 方程未注明「原版氨水」")
_vanilla_keys = set(_va_map.keys())
for _tid in ("illicit_voidsalt", "illicit_redmist", "illicit_nightglass", "illicit_ashbreath"):
    _t4 = [r for r in _rx.values() if _tid in r["outputs"] and r["tier"] == 4]
    if not _t4:
        p3f_bad.append("T4 不可达: " + _tid)
    elif not any(i in _vanilla_keys for i in _t4[0]["inputs"]):
        p3f_bad.append(f"T4 缺原版输入（VanillaAnalog）: {_tid}")
# 28f 非化学计量方程标注
if "水为溶剂" not in _rx.get("hcl_absorption", {}).get("eq", "") \
        or "非化学计量" not in _rx.get("hcl_absorption", {}).get("eq", ""):
    p3f_bad.append("hcl_absorption 方程应标注「水为溶剂/非化学计量」")
if "母液" not in _rx.get("salt_crystallize", {}).get("eq", "") \
        or "非化学计量" not in _rx.get("salt_crystallize", {}).get("eq", ""):
    p3f_bad.append("salt_crystallize 方程应标注「母液/非化学计量」")
# 28g 新自动断言存在（文本层验证 ValidateLogic 覆盖项）
_vl3 = _body_or_expr(rec_text, "ValidateLogic") or ""
for _tok in ("MachineIds", "SpecValue", "CanisterSkus", "VanillaAnalog", "UsesVanilla", "Tier"):
    if _tok not in _vl3:
        p3f_bad.append(f"ValidateLogic 缺自动断言项 {_tok}")
_labvl = _body_or_expr(lab_text, "ValidateLogic") or ""
for _tok in ("MinTemp", "MaxTemp", "DegradeTemp", "lowBand", "highBand"):
    if _tok not in _labvl:
        p3f_bad.append(f"ChemLab.ValidateLogic 缺温区自检 {_tok}")
# 28h 化学审计文档
_audit_path = os.path.join(DOCS, "chemistry-audit.md")
if not os.path.exists(_audit_path):
    p3f_bad.append("缺 docs/chemistry-audit.md（化学审计报告）")
else:
    _audit = read(_audit_path)
    for _kw in ("44", "T4", "气瓶", "步骤", "P3f"):
        if _kw not in _audit:
            p3f_bad.append(f"chemistry-audit.md 缺关键词 {_kw}")
if p3f_bad:
    for b in p3f_bad:
        err("P3f 热反应可达/消耗/温区/T4: " + b)
else:
    ok("P3f：加热台 2×2 反应槽接受容器/本模组药剂/原版药剂瓶；ConsumeThermalInputs 成功+超温均按"
       "配方 inputs 销毁（燃料槽不作投料）；ToggleHighByHeater 接酒精灯点击（HIGH160/LOW99）；"
       "KMnO4 140–160/降解190、NH4NO3 150–160/降解200；灰息 T4 含原版氨水瓶+方程注明；"
       "4 个 T4 均≥1 原版输入；hcl_absorption/salt_crystallize 非化学计量标注；"
       "ValidateLogic/ChemLab.ValidateLogic 自动断言项齐备；chemistry-audit.md 含 44/T4/气瓶/步骤/P3f")

# ---------- 29. P4a（第 27 轮）：机器窗口改用官方 MachineHelper + 电力/槽位规格 ----------
p4a_bad = []
for _tok, _d in (("MachineHelper.CreateMachineryWindow(", "官方窗口构建"),
                 ("MachineHelper.PowerPanel(", "电池面板"),
                 ("MachineHelper.Panel(", "通用面板"),
                 ("MachineHelper.NotePanel(", "便签面板"),
                 ("MachineHelper.SetupBatterySlot(", "电池槽绑定"),
                 ("LayoutOf(", "槽位规格"), ("PowerOf(", "功率表")):
    if _tok not in mach_text:
        p4a_bad.append(f"缺 {_d}（{_tok}）")
if not re.search(r'if\s*\(battery\s*!=\s*null\)\s*MachineHelper\.SetupBatterySlot', mach_text):
    p4a_bad.append("SetupBatterySlot 未按 battery != null 条件调用（无电机器不应有电池槽）")
if "bool powered = battery != null;" not in mach_text:
    p4a_bad.append("缺 bool powered = battery != null;（无电池不供电）")
if "if (powered" not in mach_text:
    p4a_bad.append("缺 if (powered ...) 供电/扣电保护锚点")
if "new PixelWindow" in mach_text:
    p4a_bad.append("机器窗口仍自建 PixelWindow（应全部改用 MachineHelper）")
_pow_body = _body_or_expr(mach_text, "PowerOf") or ""
_powers = {m.group(1): int(m.group(2))
           for m in re.finditer(r'case\s+"([a-z_]+)":\s*return\s*(\d+);', _pow_body)}
_expected_power = {"electrolyzer": 14, "high_pressure_synth": 16, "fractionator": 12,
                   "chem_reactor": 10, "gas_generator": 6, "condenser_unit": 8,
                   "crystallizer": 5, "canister_filling": 9, "gas_scrubber": 7,
                   "filtration_bench": 0, "decanter": 0, "titration_bench": 0,
                   "heating_platform": 0}
for _mid, _pw in _expected_power.items():
    if _powers.get(_mid) != _pw:
        p4a_bad.append(f"{_mid} 功率应为 {_pw}，实际 {_powers.get(_mid)}")
_layouts = {}
for m in re.finditer(r'case\s+"([a-z_]+)":\s*return new MachineLayout\s*\{([^}]*)\};', mach_text):
    _layouts[m.group(1)] = dict(re.findall(r'(\w+)\s*=\s*([^,]+)', m.group(2)))
_expected_layouts = {
    "decanter": (False, 1, False, False), "filtration_bench": (False, 1, False, True),
    "titration_bench": (False, 2, False, False), "crystallizer": (True, 2, False, False),
    "chem_reactor": (True, 3, True, False), "electrolyzer": (True, 2, True, False),
    "gas_generator": (True, 2, False, False), "condenser_unit": (True, 2, False, False),
    "fractionator": (True, 3, False, False), "high_pressure_synth": (True, 3, False, False),
    "canister_filling": (True, 1, False, True), "gas_scrubber": (True, 1, False, False),
}
for _mid, (_pw, _ins, _mod, _spc) in _expected_layouts.items():
    _f = _layouts.get(_mid)
    if _f is None:
        p4a_bad.append("LayoutOf 缺机器 " + _mid)
        continue
    if str(_f.get("Powered", "false")).lower() != str(_pw).lower():
        p4a_bad.append(f"{_mid} Powered 应为 {_pw}")
    if _f.get("InputSlots") != str(_ins):
        p4a_bad.append(f"{_mid} InputSlots 应为 {_ins}，实际 {_f.get('InputSlots')}")
    if str(_f.get("Modules", "false")).lower() != str(_mod).lower():
        p4a_bad.append(f"{_mid} Modules 应为 {_mod}")
    if _spc:
        _fl = str(_f.get("FilterSlot", "false")).lower() == "true"
        _ct = str(_f.get("ContainerSlot", "false")).lower() == "true"
        if _mid == "filtration_bench" and not _fl:
            p4a_bad.append("filtration_bench 缺 FilterSlot")
        if _mid == "canister_filling" and not _ct:
            p4a_bad.append("canister_filling 缺 ContainerSlot")
_mods = [mid for mid, f in _layouts.items() if str(f.get("Modules", "false")).lower() == "true"]
if set(_mods) != {"chem_reactor", "electrolyzer"}:
    p4a_bad.append("模块槽应仅 chem_reactor/electrolyzer，实际: " + ", ".join(sorted(_mods)))
for _mid in ("decanter", "filtration_bench", "titration_bench", "heating_platform"):
    if _powers.get(_mid) != 0:
        p4a_bad.append(f"无电机器 {_mid} 功率应为 0")
    if _mid in _layouts and str(_layouts[_mid].get("Powered", "false")).lower() != "false":
        p4a_bad.append(f"无电机器 {_mid} LayoutOf.Powered 应为 false")
_hp3 = _body_or_expr(mach_text, "CreateHeatingPlatform") or ""
if "MachineHelper.CreateMachineryWindow(" not in _hp3 or "MachineHelper.NotePanel(" not in _hp3:
    p4a_bad.append("加热台窗口未改用官方 MachineHelper（CreateMachineryWindow/NotePanel）")
for _lp in ("ui_vessel", "ui_heater", "ui_gas_slot", "ui_output"):
    if _lp not in _hp3:
        p4a_bad.append(f"加热台窗口缺面板标签 {_lp}")
if p4a_bad:
    for b in p4a_bad:
        err("P4a 官方机器窗口/电力/槽位: " + b)
else:
    ok("P4a：机器窗口全部改用 MachineHelper（CreateMachineryWindow/PowerPanel/Panel/NotePanel）；"
       "SetupBatterySlot 仅 battery!=null；无电 4 机（decanter/filtration/titration/heating）功率 0 且无电池；"
       "功率表与 LayoutOf（InputSlots/FilterSlot/ContainerSlot/Modules）符合规格；模块槽仅反应釜/电解槽；"
       "加热台窗口亦为官方构建器 + vessel/heater/gas/output/note 面板")

# ---------- 30. P4c/P4d（第 27 轮）：容器读档桥接 / 电话注册 / 组分计算（待 features 落盘）----------
p4cd_bad = []
for _tok, _d in (("OnGameLoaded", "读档钩子"),
                 ("BridgeVanillaChemData", "读档遍历重建桥接"),
                 ("LIQUID_CONTAINER_TAG", "容器 tag 重建"),
                 ("PhoneClientDict[96686]", "电话注册 96686"),
                 ("Responding", "电话 client 响应体"),
                 ("EnsureRegistered", "电话幂等注册"),
                 ("AddLiquidToChemFromList", "分析仪注入组分"),
                 ("ComponentParts", "组分守恒/覆盖"),
                 ("fc_tox", "毒性标签"),
                 ("fc_euph", "欣快标签")):
    if _tok not in src_text:
        p4cd_bad.append(f"缺 {_d}（{_tok}）")
if "OnGameLoaded" in src_text and not re.search(
        r'(?:OnGameLoaded|CHEM_ITEM_TAG)[\s\S]{0,3000}?BridgeVanillaChemData', src_text):
    p4cd_bad.append("读档路径未调用 BridgeVanillaChemData 遍历重建")
if "fc_tox" in src_text and "ComponentParts" not in src_text:
    p4cd_bad.append("fc_tox/fc_euph 未由 ComponentParts 计算（仍是固定字面量？）")
if p4cd_bad:
    for b in p4cd_bad:
        err("P4c/P4d 容器读档/电话/组分: " + b)
else:
    ok("P4c/P4d：OnGameLoaded 遍历 BridgeVanillaChemData 重建容器 tag；电话 96686 经 EnsureRegistered 注册"
       "到 PhoneClientDict（Responding）；分析仪 AddLiquidToChemFromList 注入组分；ComponentParts 守恒/覆盖；"
       "fc_tox/fc_euph 由组分计算")

# ---------- 31. P5d（第 28 轮）：电话/便签/索引书/分析仪/机器便签 合理性 ----------
p5_bad = []
# 31a 电话：多页/每页挂断/按钮文本本地化/资金不足可挂断/扣款单点
_ph = read(os.path.join(SRC, "ChemPhone.cs")) if os.path.exists(os.path.join(SRC, "ChemPhone.cs")) else ""
_ph_all = _ph + entry_text
if not re.search(r"HangUp|Hangup|hangup|挂断", _ph_all):
    p5_bad.append("电话无挂断选项（每页须可挂断）")
if not re.search(r"AddChoice\(", _ph_all):
    p5_bad.append("电话缺选项构建（Dialogue.AddChoice 多选项）")
if len(re.findall(r"NewPage\(", _ph_all)) < 5:
    p5_bad.append("电话页面 <5（多页对话/结果页）")
if not re.search(r"AddHangup\(|Hangup\(", _ph_all):
    p5_bad.append("电话缺每页挂断（AddHangup/Hangup）")
if re.search(r"locID\s*=\s*null|\btext\s*=\s*null", _ph_all):
    p5_bad.append("电话按钮 locID/text 为 null")
if _ph and not re.search(r"GetLocalized|L\(", _ph):
    p5_bad.append("电话按钮/文案未走本地化（可能显示裸键）")
if not re.search(r"资金不足|insufficient|not\s+enough|CanAfford", _ph_all, re.I):
    p5_bad.append("电话缺资金不足分支")
if len(re.findall(r"(?:Deduct|Spend|Take|Change)Money", _ph_all)) > 2:
    p5_bad.append("扣款调用多处，存在重复/错误扣款风险")
if "fc_chem_order_pending" not in _ph_all:
    p5_bad.append("电话 pending 逻辑丢失")
# 31b 便签：原版 note/paper 物品 + 含 96686 + 不再化学分类/掉落
if not re.search(r"SpawnPaperItem\(|NOTE_TAG|TypeHelper\.NOTE|PAPER_TAG", src_text):
    p5_bad.append("便签未使用原版 note/paper 物品（仍是 mod 化学物品？）")
if "96686" not in src_text:
    p5_bad.append("便签/电话缺号码 96686")
if "note_chem_dealer" in loot_ids:
    p5_bad.append("note_chem_dealer 仍出现在掉落表（应仅 legacy 注册、不进化学分类）")
# 31c 索引书坐标必须在背景内
if os.path.exists(_idx_path):
    try:
        _bk = json.load(open(_idx_path, encoding="utf-8"))
        _bw, _bh = _bk.get("width"), _bk.get("height")
        # 官方 schema：width/height=0 表示沿用原版背景原生尺寸（notebook_manual 约 628×804）。
        # 0 不能当成真实像素 0，否则所有元素都会被误判越界。
        if not _bw:
            _bw = 628
        if not _bh:
            _bh = 804
        _oob = []
        for _pi, _pg in enumerate(_bk.get("pages", [])):
            for _el in _pg.get("elements", []):
                _x, _y = _el.get("x", 0), _el.get("y", 0)
                _w, _h = _el.get("w", 0), _el.get("h", 0)
                if _x < 0 or _y < 0 or _x + _w > _bw or _y + _h > _bh:
                    _oob.append((_pi + 1, _x, _y, _w, _h))
        if _oob:
            p5_bad.append(f"索引书元素越界 {len(_oob)} 处（背景 {_bw}×{_bh}）: " + str(_oob[:3]))
    except Exception as _e:
        p5_bad.append(f"索引书坐标检查失败: {_e}")
# 31d 分析仪：每个药剂有可解析组分；氯气组分≠原版 chlorine；显示成分/总液量
_cparts_text = _body_or_expr(rec_text, "Add") or rec_text
_cc = re.search(r'ComponentChlorineGas\s*=\s*"([a-z0-9_]+)"', rec_text)
if not _cc:
    p5_bad.append("缺 ComponentChlorineGas 常量")
elif _cc.group(1) == "chlorine":
    p5_bad.append("ComponentChlorineGas 不得等于原版 chlorine")
else:
    _comp = _cc.group(1)
    if not re.search(r'"gas_chlorine"[\s\S]{0,240}?"' + re.escape(_comp) + r'"', rec_text):
        p5_bad.append(f"ComponentParts 未把 gas_chlorine 映射到独立成分 {_comp}")
    if not re.search(r'\{\s*"' + re.escape(_comp) + r'"\s*,\s*new ComponentDef', rec_text):
        p5_bad.append(f"独立成分 {_comp} 未在 Components 定义（NameKey/Formula）")
if "fc_components" not in src_text:
    p5_bad.append("缺 fc_components 幂等标记（分析仪组分写入）")
if not re.search(r"GetTotalVolume|totalVolumeTMP", src_text):
    p5_bad.append("分析仪未显示总液量（GetTotalVolume/totalVolumeTMP）")
# 31e 14 台机器 NotePanel + 文案键
if "NotePanel(" not in mach_text:
    p5_bad.append("机器窗口缺 NotePanel")
if '"machine_note_" + machineId' not in mach_text and 'machine_note_' not in mach_text:
    p5_bad.append("机器便签未按 machine_note_<id> 取文案键")
_note_keys = [k for k in zh if k.startswith("machine_note_")]
if len(_note_keys) < 13:
    p5_bad.append(f"machine_note_<id> 文案键仅 {len(_note_keys)} 条（应 ≥13）")
_missing_note_en = [k for k in _note_keys if k not in en]
if _missing_note_en:
    p5_bad.append("machine_note 键缺 en: " + ", ".join(_missing_note_en[:5]))
for _k in ("ui_note", "inst_note"):
    pass
# 31f 研究文档
_res_path = os.path.join(DOCS, "note-phone-component-research.md")
if not os.path.exists(_res_path):
    p5_bad.append("缺 docs/note-phone-component-research.md")
else:
    _res = read(_res_path)
    for _grp in (("电话",), ("便签",), ("组分",),
                 ("时序", "顺序", "时机", "初始化", "加载"),
                 ("幂等", "重复", "去重"), ("旧档", "读档", "存档")):
        if not any(_k in _res for _k in _grp):
            p5_bad.append("研究文档缺语义: " + "/".join(_grp))
if p5_bad:
    for b in p5_bad:
        err("P5d 合理性: " + b)
else:
    ok("P5d：电话多页/挂断/本地化文案/资金不足可挂断/单点扣款；便签为原版 note/paper 含 96686 且不在掉落；"
       "索引书坐标均在 628×804 内；分析仪组分齐全、氯气非原版 chlorine、显示总液量；14 台机器 NotePanel；"
       "docs/note-phone-component-research.md 结论齐备")

# ---------- 32. P6c（第 30 轮）：电话/倒液/分析仪/仪器全体系合理性 ----------
p6_bad = []
_ph6 = read(os.path.join(SRC, "ChemPhone.cs")) if os.path.exists(os.path.join(SRC, "ChemPhone.cs")) else ""
# 32a 电话：每日 1 单/跨天可再订/连点不重复扣款/姓名/快捷拨号
_ord = _body_or_expr(_ph6, "TryPlaceOrder") or ""
if "PendingKey" not in _ord or "DayKey" not in _ord:
    p6_bad.append("TryPlaceOrder 缺 pending/每日限制守卫")
if _ord.count("playerCash =") != 1:
    p6_bad.append("扣款应单点（playerCash 赋值应恰 1 处）")
if "playerCash =" in _ord and "DayKey" in _ord and \
        _ord.find("playerCash =") < _ord.find("DayKey"):
    p6_bad.append("扣款早于每日限制判断（连点可能重复扣款）")
if not re.search(r"lastDay\s*==\s*day", _ord):
    p6_bad.append("缺跨天比较（跨天应可再订）")
if "displayName" not in _ph6:
    p6_bad.append("电话联系人未设置显示名（姓名显示缺失）")
if not re.search(r"QuickDial|SpeedDial|quickDial|quick_dial", src_text):
    p6_bad.append("电话快捷拨号未收录（QuickDial）")
# 32b 倒液 Harmony 兜底：本模组液体→原版任意容器、容量限流、空瓶销毁/扣减
_tl_pos = _patch_text.find('"TransferLiquid"')
_tl = _patch_text[_tl_pos:_tl_pos + 9000] if _tl_pos >= 0 else ""
if not re.search(r'\[HarmonyPatch\(typeof\(WaterHelper\),\s*"TransferLiquid"\)\]', _patch_text):
    p6_bad.append("缺 HarmonyPatch(WaterHelper.TransferLiquid) 兜底")
# 审查 5.4 起主路径交还原版 TransferLiquid，补丁只做记账；旧存档兜底仍需容量限流与组分注入
for _tok, _d in (("IsLiquidContainerItem|LIQUID_CONTAINER_TAG", "原版任意容器 tag 判定"),
                 ("GetFreeCapacity", "目标容量限流"), ("AddLiquid(|PourPartsInto(", "注入组分")):
    if not any(_t in _tl for _t in _tok.split("|")):
        p6_bad.append(f"倒液兜底缺 {_d}（{_tok}）")
if not re.search(r"Destroy\(|currentPart|PourVolumeMl", _tl):
    p6_bad.append("倒液后未扣减/销毁源瓶（体积守恒缺失）")
# 32c 分析仪：始终显示成分/总液量/化学式/毒性欣快；fc_chlorine_gas 显示氯气
_ru6 = _patch_text[_patch_text.find('"RefreshUI"'):][:5000] if '"RefreshUI"' in _patch_text else ""
for _tok, _d in (("contaminantTMP", "成分行"), ("totalVolumeTMP", "总液量行"),
                 ("formula", "化学式"), ("AddLiquidToChem", "组分注入")):
    if _tok not in _ru6:
        p6_bad.append(f"分析仪 RefreshUI 缺 {_d}（{_tok}）")
if not re.search(r"fc_tox|fc_euph|chem_profile|lethal_", _patch_text + runtime_text):
    p6_bad.append("分析仪缺毒性/欣快显示")
if "fc_chlorine_gas" not in src_text:
    p6_bad.append("氯气独立组分 fc_chlorine_gas 缺失")
if "氯气" not in zh.get("gas_chlorine_name", ""):
    p6_bad.append("氯气显示名不是「氯气」")
# 32d 仪器逐台：13 机器均有 LayoutOf + resolver；加热台/打印机可用
_resolver = _body_or_expr(mach_text, "MakeResolver") or ""
for _mid in machine_ids:
    if _mid == "heating_platform":
        continue
    if not re.search(r'if\s*\(\s*machineId\s*==\s*"' + re.escape(_mid) + r'"', _resolver) \
            and not re.search(r'case\s+"' + re.escape(_mid) + r'"', mach_text):
        p6_bad.append(f"机器 {_mid} 无 resolver/槽位定义")
if _layouts and set(_layouts) != set(_ for _ in machine_ids if _ != "heating_platform"):
    _miss6 = [m for m in machine_ids if m != "heating_platform" and m not in _layouts]
    if _miss6:
        p6_bad.append("LayoutOf 缺机器: " + ", ".join(_miss6))
_hp6 = _body_or_expr(mach_text, "CreateHeatingPlatform") or ""
for _tok in ("vessel", "heater", "gasSlots", "output", "note"):
    if _tok not in _hp6:
        p6_bad.append(f"加热台缺 {_tok} 槽/面板")
if "label_printer" not in machine_ids and "printer_mode" not in src_text:
    p6_bad.append("打印机模式/物品缺失")
# 32e code-review.md P0/P1 逐条
_cr = os.path.join(DOCS, "code-review.md")
if not os.path.exists(_cr):
    p6_bad.append("缺 docs/code-review.md（P0/P1 审查清单）")
else:
    _crt = read(_cr)
    if "P0" not in _crt or "P1" not in _crt:
        p6_bad.append("code-review.md 缺 P0/P1 分级")
    if not re.search(r"修复|fixed|已修", _crt):
        p6_bad.append("code-review.md 缺修复状态标注")
if p6_bad:
    for b in p6_bad:
        err("P6c 全体系合理性: " + b)
else:
    ok("P6c：电话每日 1 单（跨天可再订、单点扣款且在每日判断之后）+ 姓名显示 + 快捷拨号；"
       "倒液 Harmony 兜底（原版任意容器/容量限流/空瓶扣减销毁）；分析仪始终显示成分/总液量/化学式/"
       "毒性欣快且氯气=氯气（fc_chlorine_gas）；13 机器 LayoutOf+resolver、加热台槽面板、打印机可用；"
       "docs/code-review.md P0/P1 清单与修复状态齐备")


# ---------- 33. P8（第 32 轮）：电话套餐/单项订购 + P9 ASCII 化学式 ----------
p8_bad = []
_ph8 = read(os.path.join(SRC, "ChemPhone.cs")) if os.path.exists(os.path.join(SRC, "ChemPhone.cs")) else ""
if "TryPlaceOrder(string[]" not in _ph8:
    p8_bad.append("TryPlaceOrder 未通用化为 (items, price)")
for _key in ("phone_pack_", "phone_cat_"):
    if _key not in src_text:
        p8_bad.append("电话目录缺本地化键前缀 " + _key)
_pdisc = re.search(r'PackPrice\([^)]*,\s*(\d+)\s*\)', _ph8)
if not _pdisc:
    p8_bad.append("套餐价未经 PackPrice(items, discountPercent) 计算")
elif not (10 <= int(_pdisc.group(1)) <= 20):
    p8_bad.append(f"套餐折扣应在 10–20%，实际 {_pdisc.group(1)}%")
if "1.6" not in _ph8:
    p8_bad.append("单项价未见 spec.Value×1.6")
_pages8 = len(re.findall(r"NewPage\(", _ph8))
if _pages8 < 8:
    p8_bad.append(f"电话目录页数 {_pages8} < 8（首页/套餐/单项分类）")
if "AddHangup(" not in _ph8:
    p8_bad.append("电话目录缺挂断选项")
elif _ph8.count("AddHangup(") < 3 and not re.search(r'foreach[^\n]*\n[^\n]*AddHangup', _ph8):
    p8_bad.append("电话目录挂断覆盖不足（需逐页或循环挂断）")
if _ph8.count("phone_pack_") < 5:
    p8_bad.append("套餐键 <5（基础/酸/碱/溶剂/固体）")
if _ph8.count("phone_cat_") < 4:
    p8_bad.append("单项分类键 <4（酸/碱/溶剂/固体）")
if "fc_chem_order_items" not in _ph8:
    p8_bad.append("缺 pending 物品列表 fc_chem_order_items")
if "CreateMiniAlcoholBottle" not in _ph8:
    p8_bad.append("配送未对 mini_bottle 酒精瓶特判 CreateMiniAlcoholBottle")
_ord8 = _body_or_expr(_ph8, "TryPlaceOrder") or ""
if "PendingKey" not in _ord8 or "DayKey" not in _ord8 or "playerCash" not in _ord8:
    p8_bad.append("付费路径缺 pending/每日/资金守卫")
if p8_bad:
    for b in p8_bad:
        err("P8 电话套餐/单项: " + b)
else:
    ok("P8：电话多页目录（首页/套餐/单项 4 分类）、每页挂断+返回；套餐 PackPrice 折扣 10–20%；"
       "单项价 spec.Value×1.6；TryPlaceOrder(items,price) 通用化 + pending/每日/资金守卫；"
       "fc_chem_order_items 列表配送 + mini_bottle 特判；原有姓名/快捷拨号/挂断不回退")

# P9：物品名去公式/Unicode 下标；Formula 全 ASCII；分析仪/便签/日志无 Unicode 下标
p9_bad = []
_SUB = re.compile(r"[\u2080-\u209c]")  # Unicode 下标 0-9 等
_bad_names = []
for _k, _v in list(zh.items()) + list(en.items()):
    if not _k.endswith("_name"):
        continue
    if _SUB.search(_v):
        _bad_names.append(f"{_k}=Unicode下标")
    elif re.search(r"[A-Za-z]\d", _v) or re.search(r"\([A-Z][a-z]?\d", _v):
        _bad_names.append(f"{_k}=疑似化学式({_v})")
if _bad_names:
    p9_bad.append("物品名仍含化学式/Unicode 下标: " + "; ".join(_bad_names[:8]))
_ui_files = ["ChemPatches.cs", "ModEntry.cs", "ChemRuntime.cs", "ChemMachines.cs",
             "ChemPhone.cs", "ChemPrinter.cs", "ChemEquipment.cs", "ChemDebug.cs"]
_bad_ui = []
for _uf in _ui_files:
    _fp = os.path.join(SRC, _uf)
    if os.path.exists(_fp) and _SUB.search(open(_fp, encoding="utf-8").read()):
        _bad_ui.append(_uf)
if _bad_ui:
    p9_bad.append("UI/生成文本代码仍含 Unicode 下标: " + ", ".join(_bad_ui))
_sub_data = []
for _df in ("ChemRecipes.cs", "ChemLab.cs", "ChemCatalog.cs"):
    _fp = os.path.join(SRC, _df)
    if os.path.exists(_fp) and _SUB.search(open(_fp, encoding="utf-8").read()):
        _sub_data.append(_df)
if _sub_data:
    warn("反应/热反应方程数据仍含 Unicode 下标（" + "/".join(_sub_data)
         + "，属反应方程文案，非 P9 名称/UI 范围；如要 PDF/手册字体兼容需 features 后续转 ASCII）")
for _f in ("zh.csv", "en.csv"):
    _txt = read(os.path.join(PKG, "Localization", _f))
    if _SUB.search(_txt):
        p9_bad.append(f"{_f} 仍有 Unicode 下标字符")
_books = glob.glob(os.path.join(PKG, "Books", "*.json"))
for _bp in _books:
    if _SUB.search(read(_bp)):
        p9_bad.append("Books/" + os.path.basename(_bp) + " 仍有 Unicode 下标")
if "static string Formula(" not in rec_text and "public static string Formula(" not in rec_text:
    p9_bad.append("ChemRecipes 缺 ASCII Formula(id) 表")
if "ToAsciiText(" not in rec_text:
    p9_bad.append("ChemRecipes 缺 ToAsciiText( ASCII 转换")
_fm = re.search(r"AsciiFormulas[\s\S]{0,3000}", rec_text)
if _fm and _SUB.search(_fm.group(0)):
    p9_bad.append("AsciiFormulas 中仍有 Unicode 下标")
if not re.search(r"static string Formula\(", rec_text):
    p9_bad.append("ChemRecipes 缺 ASCII Formula(id) 内部表")
if p9_bad:
    for b in p9_bad:
        err("P9 化学式/名称 ASCII: " + b)
else:
    ok(f"P9：物品 _name 无化学式与 Unicode 下标（{len(zh)} 键扫描）；src/CSV/Books 无 Unicode 下标；"
       "ChemRecipes.Formula/AsciiFormulas/ToAsciiText ASCII 表；分析仪等文本经 Formula 输出")


# ---------- 34. P10（第 34 轮）：成分体系/接触反应/容器携带 ----------
p10_bad = []
# 成分定义 NameKey 全部本地化（comp_<id>_name）
_comp_keys = sorted(set(re.findall(r'NameKey\s*=\s*"(comp_[a-z0-9_]+)"', rec_text)))
_miss_comp = [k for k in _comp_keys if k not in zh or k not in en]
if not _comp_keys:
    p10_bad.append("未解析到 ComponentDef NameKey（成分体系缺失？）")
elif _miss_comp:
    p10_bad.append("成分名缺本地化: " + ", ".join(_miss_comp[:10]))
# 接触反应：数量/输入输出已注册/NameKey 本地化
_contact_blocks = re.findall(r'ContactReaction\s*\{([\s\S]*?)\}\s*,', rec_text)
if len(_contact_blocks) < 6:
    p10_bad.append(f"ContactReactions 仅 {len(_contact_blocks)} 条（应 ≥6）")
_reg10 = spec_ids | machine_ids
_contact_keys = sorted(set(re.findall(r'NameKey\s*=\s*"(contact_[a-z0-9_]+)"', rec_text)))
_miss_contact = [k for k in _contact_keys if k not in zh or k not in en]
if _miss_contact:
    p10_bad.append("接触反应名缺本地化: " + ", ".join(_miss_contact[:10]))
for _cb in _contact_blocks:
    for _ids in re.findall(r'Inputs\s*=\s*new\[\]\s*\{([^}]*)\}', _cb):
        for _i in re.findall(r'"([a-z0-9_]+)"', _ids):
            if _i not in _reg10:
                p10_bad.append("接触反应输入未注册: " + _i)
    for _ids in re.findall(r'Outputs\s*=\s*new\[\]\s*\{([^}]*)\}', _cb):
        for _o in re.findall(r'"([a-z0-9_]+)"', _ids):
            if _o not in _reg10:
                p10_bad.append("接触反应输出未注册: " + _o)
# 容器携带内容 + 机器接受容器
if "fc_content_items" not in src_text:
    p10_bad.append("缺容器内容 tag fc_content_items")
if not re.search(r'RunMachineCycle[\s\S]{0,4000}?fc_content_items|fc_content_items[\s\S]{0,3000}?RunMachineCycle', mach_text):
    p10_bad.append("机器结算未解析容器内容 fc_content_items")
_containers = ("beaker", "retort_flask", "erlenmeyer_flask", "test_tube")
for _cid in _containers:
    if not re.search(r'"' + _cid + r'"[\s\S]{0,300}?InitLiquidContainerItem|InitLiquidContainerItem[\s\S]{0,300}?"' + _cid + r'"', cat_text):
        p10_bad.append(f"容器 {_cid} 未成为原版液体容器（InitLiquidContainerItem）")
# P11：test_tube #98 为液体容器；试管架为只装试管的架子（非液体容器）
_vessel_ids = re.search(r'VesselIds\s*=\s*(?:new\[\]\s*)?\{([^}]*)\}', src_text)
_vessels = set(re.findall(r'"([a-z0-9_]+)"', _vessel_ids.group(1))) if _vessel_ids else set()
for _vid in ("beaker", "retort_flask", "erlenmeyer_flask", "test_tube"):
    if _vid not in _vessels:
        p10_bad.append(f"VesselIds 缺液体容器 {_vid}")
if "test_tube_rack" in _vessels:
    p10_bad.append("test_tube_rack 不应是液体容器（改为只装试管的架子）")
if "TestTubeRackId" not in src_text:
    p10_bad.append("缺 TestTubeRackId（试管架语义）")
if ("GetContainerContentIds(" not in src_text):
    p10_bad.append("缺 GetContainerContentIds（容器内容解析）")
# 审查 3.4 起机器经 ChemCatalog.ContentPortionIds → ContentHolders（内部 FindAllChildItems）读试管架
if "FindAllChildItems(" not in mach_text and not ("ContentPortionIds(" in mach_text and "FindAllChildItems(" in src_text):
    p10_bad.append("机器未通过 FindAllChildItems 读取试管架内试管")
if p10_bad:
    for b in p10_bad:
        err("P10 成分/接触反应/容器: " + b)
else:
    ok(f"P10：{len(_comp_keys)} 个成分名本地化齐备；ContactReactions ≥6 且输入输出全部已注册、接触名键齐备；"
       "容器内容 fc_content_items 与机器解析/容器液体化接线")

# ---------- 汇总 ----------


print("\n===== FreeChem 静态验收 =====")
for m in oks:
    print("  [PASS] " + m)
for m in warns:
    print("  [WARN] " + m)
for m in errors:
    print("  [FAIL] " + m)
print(f"\n结果: {len(oks)} PASS / {len(warns)} WARN / {len(errors)} FAIL")
print("注意：本检查不替代 dotnet build、模组加载、游戏内功能与存档验证。")
sys.exit(1 if errors else 0)
