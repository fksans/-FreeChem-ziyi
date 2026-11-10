# -*- coding: utf-8 -*-
"""
FreeChem 化学/相态逻辑自检 —— 独立于 C# 编译，直接解析反应表文本并逐条断言。

检查项：
 1. 设备输入相态（滗析只液体、洗涤/灌装只气体、冷凝只气体、结晶只液体）
 2. 滗析产物必须是「上层清液 + 沉淀」；灌装产物必须是气罐 SKU
 3. 每条反应的输入/输出 id 都必须在图标目录里存在（否则游戏里会缺图）
 4. 多级链可达性：从基础原料出发能逐级抵达 T4 违禁品
 5. 原版药剂只在 UsesVanilla 的 T4 反应里出现
 6. 气罐 SKU 三档齐全，且纯度档与物品规格一致
 7. 相变方向自洽：滗析液→液、洗涤气→气、灌装气→气罐
"""
import re, os, sys, glob

# 中文 Windows 控制台默认 GBK：输出含 2 等 Unicode 字符时强制 UTF-8，避免 UnicodeEncodeError。
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # 仓库根目录（原为本机绝对路径）
SRC = os.path.join(ROOT, "src")
RECIPES = os.path.join(SRC, "ChemRecipes.cs")
CATALOG = os.path.join(SRC, "ChemCatalog.cs")
ICONS = {os.path.splitext(os.path.basename(p))[0]
         for p in glob.glob(os.path.join(ROOT, "FreeChem", "Textures", "Items", "*.png"))}

text = open(RECIPES, encoding="utf-8").read()
cat = open(CATALOG, encoding="utf-8").read()

errors, checks, softs = [], [], []


def ck(cond, msg):
    checks.append(msg)
    if not cond:
        errors.append(msg)


def sw(cond, msg):
    """软断言：不满足记 WARN，不阻断退出码（用于暂由其他 owner 落地的项）。"""
    checks.append(msg)
    if not cond:
        softs.append(msg)


# 解析所有 Reaction(...) 块：抓 id、machine、tier、inputs、outputs、usesVanilla
pat = re.compile(
    r'new Reaction\(\s*"(?P<id>[^"]+)"[^,]*?,\s*"[^"]*"\s*,\s*"[^"]*"\s*,\s*"(?P<machine>[^"]+)"\s*,\s*(?P<tier>\d+)\s*,\s*'
    r'new\[\]\s*\{(?P<inputs>[^}]*)\}\s*,\s*new\[\]\s*\{(?P<outputs>[^}]*)\}(?P<rest>.*?)\)\s*[,;]',
    re.S)

def ids(s): return re.findall(r'"([^"]+)"', s)

reactions = []
for m in pat.finditer(text):
    reactions.append({
        "id": m.group("id"), "machine": m.group("machine"), "tier": int(m.group("tier")),
        "inputs": ids(m.group("inputs")), "outputs": ids(m.group("outputs")),
        "usesVanilla": "usesVanilla: true" in m.group("rest"),
        "hazard": int(re.search(r'hazard:\s*(\d+)', m.group("rest")).group(1))
                   if re.search(r'hazard:\s*(\d+)', m.group("rest")) else 0,
    })

ck(len(reactions) >= 30, f"解析到 {len(reactions)} 条反应（应 ≥30）")

# 相态
AUX = {"gas_canister_empty", "catalyst_kit", "precision_balance", "ph_meter",
       "safety_goggles"}
VANILLA = {"med_bottle_red", "med_bottle_blue", "med_bottle_orange",
           "med_bottle_small_pink", "med_bottle_violet", "dream_cap"}

def phase(i):
    if i in AUX: return "aux"
    if i in VANILLA: return "vanilla"
    if i.startswith("can_"): return "gas"
    if i.startswith("gas_"): return "gas"
    if i.startswith("liq_"): return "liquid"
    if i.startswith("sol_") or i.startswith("illicit_"): return "solid"
    return "unknown"

ALLOWED = {
    "decanter": {"liquid"},
    "gas_scrubber": {"gas"},
    "canister_filling": {"gas"},
    # P2a：冷凝吸收塔需要液体（水）参与 HCl/NH3 吸收
    "condenser_unit": {"gas", "liquid"},
    "crystallizer": {"liquid"},
}

# 1. 相态断言（核心：气体绝不能进滗析）
for r in reactions:
    allow = ALLOWED.get(r["machine"])
    if not allow: continue
    for i in r["inputs"]:
        p = phase(i)
        if p in ("aux", "vanilla"): continue
        ck(p in allow or p == "unknown",
           f"[{r['id']}] {r['machine']} 输入 {i} 相态={p}，允许={sorted(allow)}")

# 特别断言：滗析槽里绝不出现 gas_ 输入
for r in reactions:
    if r["machine"] == "decanter":
        ck(not any(i.startswith("gas_") for i in r["inputs"]),
           f"[{r['id']}] 滗析槽出现了气体输入（不合逻辑）")
    if r["machine"] == "canister_filling":
        ck(any(i.startswith("gas_") for i in r["inputs"]),
           f"[{r['id']}] 灌装台缺少气体输入")
        ck("gas_canister_empty" in r["inputs"],
           f"[{r['id']}] 灌装台缺少空钢瓶")

# 2. 产物规则
for r in reactions:
    if r["machine"] == "decanter":
        ck(len(r["inputs"]) == 1 and len(r["outputs"]) == 2,
           f"[{r['id']}] 滗析应为 1 件液体进 → 2 件（清液+沉淀）出")
        if len(r["inputs"]) == 1 and len(r["outputs"]) == 2:
            ck(r["outputs"][0] == r["inputs"][0],
               f"[{r['id']}] 滗析主产物须是同一种药剂（上层清液），实际 {r['outputs'][0]}")
            ck("sol_sediment" in r["outputs"], f"[{r['id']}] 滗析缺少沉淀")
    if r["machine"] == "canister_filling":
        ck(all(o.startswith("can_") for o in r["outputs"]),
           f"[{r['id']}] 灌装产物必须全是气罐 SKU")
    if r["machine"] == "gas_scrubber":
        ck(all(o.startswith("gas_") for o in r["outputs"]),
           f"[{r['id']}] 洗涤产物必须仍是气体")

# 3. 所有 id 有图标（原版药剂除外，它们用原版自己的图标）
all_ids = set()
for r in reactions:
    all_ids.update(r["inputs"]); all_ids.update(r["outputs"])
missing = sorted(i for i in all_ids if i not in ICONS and i not in VANILLA
                 and i not in {"liq_water", "liq_ethanol"})  # P12 reserved（内部水 token/已删乙醇）
ck(not missing, "所有反应引用的新物品都有图标；缺失=" + str(missing))

# 4. 多级链可达性：T1..T4 都有反应，且 T4 产物来自 T1/T2 产物
tiers = sorted({r["tier"] for r in reactions})
ck(tiers == [1, 2, 3, 4], f"合成层级覆盖 T1–T4，实际={tiers}")
t1_out = set(o for r in reactions if r["tier"] == 1 for o in r["outputs"])
t2_out = set(o for r in reactions if r["tier"] == 2 for o in r["outputs"])
t3_out = set(o for r in reactions if r["tier"] == 3 for o in r["outputs"])
feed_t4 = set(i for r in reactions if r["tier"] == 4 for i in r["inputs"])
ck(len(feed_t4 & (t1_out | t2_out | t3_out)) > 0,
   f"T4 违禁品确实由低级合成物投料（交集 {len(feed_t4 & (t1_out|t2_out|t3_out))} 项）")

# 5. 原版药剂只在 T4 / UsesVanilla 出现
for r in reactions:
    for i in r["inputs"]:
        if i in VANILLA:
            ck(r["usesVanilla"] and r["tier"] == 4,
               f"[{r['id']}] 原版药剂 {i} 只应出现在 T4 虚构违禁品反应中")

# 6. 气罐三档齐全
skus = re.search(r'CanisterSkus\s*=\s*new Dictionary<string, string\[\]>\s*\{(.*?)\};', text, re.S)
ck(skus is not None, "找到气罐 SKU 映射")
if skus:
    for line in re.findall(r'\{\s*"([^"]+)"\s*,\s*new\[\]\s*\{([^}]*)\}', skus.group(1)):
        gas, arr = line[0], ids(line[1])
        ck(len(arr) == 3, f"{gas} 气罐应含三档纯度，实际 {len(arr)}")
        for a in arr:
            ck(a in ICONS, f"气罐 SKU {a} 有图标")

# 7. 相变方向
for r in reactions:
    if r["machine"] == "decanter":
        ck(all(phase(o) in ("liquid", "solid", "unknown", "vanilla") for o in r["outputs"]),
           f"[{r['id']}] 滗析产物不应含气体")
    if r["machine"] == "condenser_unit":
        ck(all(phase(o) in ("liquid", "unknown") for o in r["outputs"]),
           f"[{r['id']}] 冷凝吸收产物应为液体")

# 8. 洗涤塔反应输入==输出（提纯不改变化学身份）
for r in reactions:
    if r["machine"] == "gas_scrubber":
        ck(set(r["inputs"]) == set(r["outputs"]),
           f"[{r['id']}] 洗涤提纯前后应为同一种气体")

# 9. 不存在无来源的悬空物品：每个反应引用的新物品都应能在物品表注册
spec_ids = set(re.findall(r'[SC]\("([a-z0-9_]+)"', cat)) | set(re.findall(r'I\("([a-z0-9_]+)"', cat))
P12_RESERVED = {"liq_water", "liq_ethanol"}  # 已移除物品：仅保留 ID/内部 token
orphan = sorted(i for i in all_ids
                if i not in ICONS and i not in VANILLA and i not in spec_ids
                and i not in P12_RESERVED)
ck(not orphan, "反应引用的物品均有目录注册；悬空=" + str(orphan))

# ── 10. 图标 16px 网格（独立实现，与 validate.py 互为交叉校验）──
try:
    from PIL import Image
    icon_sizes = {}
    bad_grid = []
    for _p in glob.glob(os.path.join(ROOT, "FreeChem", "Textures", "Items", "*.png")):
        _name = os.path.splitext(os.path.basename(_p))[0]
        _w, _h = Image.open(_p).size
        icon_sizes[_name] = (_w, _h)
        if _w % 16 or _h % 16 or not (16 <= _w <= 64 and 16 <= _h <= 64):
            bad_grid.append(f"{_name}={_w}x{_h}")
    ck(not bad_grid, "全部图标宽高为 16 的正整数倍且 16..64；异常=" + str(bad_grid[:8]))
except Exception as _e:  # Pillow 缺失时如实失败，不静默跳过
    icon_sizes = {}
    ck(False, f"读取图标尺寸失败（Pillow 不可用）：{_e}")

# ── 11. 机器空闲/工作双状态图一一对应 ──
_mm = re.search(r'string\[\]\s*MachineIds\s*=\s*\{(.*?)\};', text, re.S)
machine_ids = re.findall(r'"([a-z0-9_]+)"', _mm.group(1)) if _mm else []
ck(bool(machine_ids), f"解析到设备清单 MachineIds（{len(machine_ids)} 台）")
_on_icons = {i for i in ICONS if i.endswith("_on")}
_missing_on = [m for m in machine_ids if m + "_on" not in ICONS]
ck(not _missing_on, "每台机器都有 <id>_on 工作状态图；缺失=" + str(_missing_on))
_orphan_on = sorted(_on_icons - {m + "_on" for m in machine_ids})
ck(not _orphan_on, "不存在孤立 _on 图标；孤立=" + str(_orphan_on))
ck(not (_on_icons & spec_ids), "「_on」状态图未被注册成商品 id；违规="
   + str(sorted(_on_icons & spec_ids)))
for _m in machine_ids:
    ck(icon_sizes.get(_m) == (48, 48), f"{_m} 空闲图应为 48×48，实际={icon_sizes.get(_m)}")
    ck(icon_sizes.get(_m + "_on") == (48, 48),
       f"{_m}_on 工作图应为 48×48，实际={icon_sizes.get(_m + '_on')}")

# ── 12. 废弃防护面具 token 零残留（独立实现；归档 v1 备份不在发布包，跳过）──
_MASK_NEEDLES = [
    "gas_" + "mask",
    chr(0x9632) + chr(0x6bd2) + chr(0x9762) + chr(0x5177),
    "Gas" + " " + "Mask",
]
_scan_paths = []
_scan_paths += glob.glob(os.path.join(SRC, "*.cs"))
_scan_paths += glob.glob(os.path.join(ROOT, "tools", "*.py"))
_scan_paths += glob.glob(os.path.join(ROOT, "FreeChem", "Localization", "*.csv"))
_scan_paths += glob.glob(os.path.join(ROOT, "FreeChem", "Books", "*.json"))
_scan_paths += glob.glob(os.path.join(ROOT, "FreeChem", "LootTables", "*.json"))
_scan_paths += glob.glob(os.path.join(ROOT, "docs", "*.md"))
_scan_paths += [os.path.join(ROOT, "README.md"), os.path.join(ROOT, "预览.html")]
_scan_paths = [p for p in _scan_paths if os.path.isfile(p)
               and "api-probe" not in p and not os.path.basename(p).endswith("_v1.py")]
_resid = []
for _p in _scan_paths:
    try:
        _txt = open(_p, encoding="utf-8", errors="replace").read()
    except Exception:
        continue
    if any(n.lower() in _txt.lower() for n in _MASK_NEEDLES):
        _resid.append(os.path.relpath(_p, ROOT))
ck(not _resid, "废弃防护面具 token 零残留（现行代码/资源/文档）；残留=" + str(_resid))

# ── 13. LootTables 不含未注册 id ──
import json as _json
_loot = _json.load(open(os.path.join(ROOT, "FreeChem", "LootTables", "free_chem_drops.json"),
                        encoding="utf-8"))
_table_ids = {t["id"] for t in _loot.get("tables", [])}
_loot_ids = {e["id"] for t in _loot.get("tables", []) for e in t["entries"]}
for _pa in _loot.get("patches", []):
    _loot_ids |= {e["id"] for e in _pa.get("add", [])}
_unknown_loot = _loot_ids - spec_ids - set(machine_ids) - _table_ids
_unknown_loot -= {"junkTable", "dumpingGroundTG"}
ck(not _unknown_loot, "LootTables 引用的物品 id 均已注册；悬空=" + str(sorted(_unknown_loot)))

# ── 14. 温度可达性（文本层；thermometer 循环 0..3 必须覆盖配方用到的全部档位）──
_explicit_temps = {int(x) for x in re.findall(r'\btemp:\s*(\d+)', text)}
_recipe_temps = _explicit_temps | {1}   # Reaction 构造默认 temp=1
ck(_recipe_temps <= {0, 1, 2, 3},
   "配方温度档 ⊆ {0,1,2,3}（越界会导致温度计无法命中）；实际=" + str(sorted(_recipe_temps)))
ck({0, 2, 3} <= _recipe_temps,
   "温度档 0/2/3 都被配方实际使用（无死档位）；实际=" + str(sorted(_recipe_temps)))

# ── 15. 器材掉落来源 + thermometer（防「死物品」回归）──
_tbl = {t["id"]: t for t in _loot.get("tables", [])}
_app = _tbl.get("free_chem_apparatus")
ck(_app is not None, "存在器材掉落表 free_chem_apparatus")
if _app:
    _bad_app = [e["id"] for e in _app.get("entries", []) if e["id"] not in spec_ids]
    ck(not _bad_app, "器材表条目 id 均已注册；未注册=" + str(_bad_app))
    _bad_w = [e["id"] for e in _app.get("entries", [])
              if not isinstance(e.get("weight"), int) or e.get("weight") <= 0]
    ck(not _bad_w, "器材表 weight 均为正整数；异常=" + str(_bad_w))
_va2 = re.search(r'VanillaAnalog\s*=\s*new Dictionary<string, string>\s*\{(.*?)\};', text, re.S)
_vanilla_mapped = set(re.findall(r'\{\s*"[^"]+"\s*,\s*"([a-z0-9_]+)"\s*\}', _va2.group(1))) \
    if _va2 else set()
# P12：mod 乙醇/蒸馏水已移除（reserved），不再作为热反应原料检查来源
ck("sol_coffee_grounds" not in _loot_ids and "sol_coffee_grounds" not in _vanilla_mapped,
   "sol_coffee_grounds 为 legacy 无来源（Lead 定案：原版咖啡已有官方制作逻辑）")
ck("thermometer" in spec_ids, "温度控制物品 thermometer 已注册进 Specs")
ck(icon_sizes.get("thermometer") == (16, 16),
   "thermometer 图标 16×16（P0 占格 1 格）；实际=" + str(icon_sizes.get("thermometer")))
# P0【问题1】占格：小件仪器 16×16；玻璃器皿/灯 16×32
for _i in ("ph_meter", "precision_balance", "safety_goggles", "catalyst_kit"):
    ck(icon_sizes.get(_i) == (16, 16), f"{_i} 应为 16×16（1 格）；实际={icon_sizes.get(_i)}")
for _i in ("beaker", "retort_flask", "erlenmeyer_flask", "test_tube_rack",
           "alcohol_lamp", "spirit_burner"):
    ck(icon_sizes.get(_i) == (16, 32), f"{_i} 应为 16×32（1×2 格）；实际={icon_sizes.get(_i)}")
ck(icon_sizes.get("label_printer") == (32, 32),
   "label_printer 图标 32×32（4 格）；实际=" + str(icon_sizes.get("label_printer")))
for _p2a_id in ("sol_calcium_oxide", "sol_calcium_hydroxide", "sol_sodium_carbonate",
                "sol_sodium_bicarbonate", "sol_ammonium_chloride", "sol_manganese_dioxide",
                "sol_copper_oxide", "sol_potassium_sulfate", "sol_sodium_nitrate",
                "sol_potassium_manganate"):
    ck(icon_sizes.get(_p2a_id) == (16, 16),
       f"P2a 图标 {_p2a_id} 应为 16×16；实际={icon_sizes.get(_p2a_id)}")

# ── 16. 原版治安部 ENDANGERMENT 接入 + 王尔德标签页/配置（文本层防回退）──
import csv as _csv
_runtime_text = open(os.path.join(SRC, "ChemRuntime.cs"), encoding="utf-8").read()
_entry_text = open(os.path.join(SRC, "ModEntry.cs"), encoding="utf-8").read()
ck("PlayerStore.HasInstance" in _runtime_text,
   "ChemRuntime 用 PlayerStore.HasInstance 判空（避免无存档时惰性新建 store）")
_ig = _runtime_text.find("PlayerStore.HasInstance")
_ii = _runtime_text.find("PlayerStore.Instance")
ck(_ig >= 0 and _ii >= 0 and _ig < _ii,
   "PlayerStore.HasInstance guard 位于 PlayerStore.Instance 之前")
ck("SecData.ENDANGERMENT" in _runtime_text, "写入原版 SecData.ENDANGERMENT（治安部犯罪量）")
ck(re.search(r'VanillaEndangermentPerUnit\s*=\s*(?:10|ChemLab\.VanillaEndangermentPerUnit\b)',
             _runtime_text + "\n"
             + open(os.path.join(SRC, "ChemLab.cs"), encoding="utf-8").read()) is not None,
   "原版折算系数 VanillaEndangermentPerUnit = 10（常量在 ChemLab，两处共用）")
ck("CommitVanillaEndangerment(units)" in _runtime_text, "AddExhaust 调用 CommitVanillaEndangerment")
ck("VentMultiplierBp" in _runtime_text, "原版写入量按排风倍率削减（VentMultiplierBp）")
ck(_entry_text.count("AddNetworkUpgrade(") == 4, "王尔德网络升级恰 4 个")
ck(_entry_text.count("tab: ChemCatalog.ModId") >= 4, "4 个网络升级全部绑定自定义标签页")
ck('config.Define("vanilla_security"' in _entry_text, "存在 vanilla_security 配置项（可关闭原版写入）")
ck("vanillaSecurity.OnChanged" in _entry_text, "配置变更立即同步 VanillaSecurityEnabled")
_zh_keys = {r[0] for r in _csv.reader(
    open(os.path.join(ROOT, "FreeChem", "Localization", "zh.csv"), encoding="utf-8"))
    if r and not r[0].startswith("#")}
ck("cfg_vanilla_security" in _zh_keys and "cfg_vanilla_security_tip" in _zh_keys,
   "原版治安部开关键 cfg_vanilla_security / _tip 中文字键齐备")

# ── 17. 第 14 轮：稳定数字 ID / 调试模式 / F9 窗口 / 满仓分支 / 中文描述（文本层独立实现）──
_item_ids_path = os.path.join(SRC, "ChemItemIds.cs")
_item_ids_text = open(_item_ids_path, encoding="utf-8").read() if os.path.exists(_item_ids_path) else ""
ck(bool(_item_ids_text), "存在 src/ChemItemIds.cs 稳定数字 ID 表")
_item_ids = []
if _item_ids_text:
    _om = re.search(r'string\[\]\s+Ordered\s*=\s*\{(.*?)\};', _item_ids_text, re.S)
    if _om:
        _item_ids = re.findall(r'"([a-z0-9_]+)"', _om.group(1))
    ck(len(_item_ids) == 98, f"数字 ID 表恰 98 条（P11 追加 test_tube）；实际={len(_item_ids)}")
    ck(len(set(_item_ids)) == len(_item_ids), "数字 ID 无重复")
    ck(set(_item_ids) == (spec_ids | set(machine_ids) | P12_RESERVED),
       "数字 ID 集合 == Specs ∪ MachineIds ∪ reserved(43/44)；差集="
       + str(sorted((spec_ids | set(machine_ids) | P12_RESERVED) ^ set(_item_ids))[:10]))
    for _n, _exp in {1: "gas_chlorine", 12: "liq_hydrogen_peroxide", 30: "thermometer",
                     61: "gas_drum_empty", 72: "sol_sediment", 73: "chem_reactor",
                     85: "heating_platform", 86: "label_printer",
                     87: "sol_calcium_oxide", 96: "sol_potassium_manganate",
                     97: "note_chem_dealer", 98: "test_tube"}.items():
        ck(len(_item_ids) >= _n and _item_ids[_n - 1] == _exp,
           f"数字 ID 顺序锚点 #{_n} = {_exp}")
    ck(not [x for x in _item_ids if x.endswith("_on")], "_on 不在数字 ID 表内")
    _id_notes = glob.glob(os.path.join(ROOT, "docs", "*.md")) + [os.path.join(ROOT, "README.md")]
    ck("只追加" in _item_ids_text
       or any(os.path.exists(p) and "只追加" in open(p, encoding="utf-8").read() for p in _id_notes),
       "源码注释/文档写明数字 ID「只追加不重排」")

# 调试模式配置 + F9 入口
ck('config.Define("debug_mode", false,' in _entry_text, "ModEntry 有 debug_mode 配置（默认关）")
ck("ConfigEntry<bool> debugMode" in _entry_text, "ModEntry 有 ConfigEntry<bool> debugMode 字段")
ck("debugMode.OnChanged" in _entry_text, "debug_mode 配置 OnChanged 同步")
ck("public static bool DebugMode" in _runtime_text, "ChemRuntime 有 public static bool DebugMode")
_dbg_path = os.path.join(SRC, "ChemDebug.cs")
_dbg_text = open(_dbg_path, encoding="utf-8").read() if os.path.exists(_dbg_path) else ""
_combined_dbg = _entry_text + "\n" + _dbg_text
ck("AddDebugFunction(" in _combined_dbg, "注册 F9 调试入口 AddDebugFunction（允许 ModEntry/ChemDebug 分层）")
ck("DEBUG_CATEGORY_COMMAND" in _combined_dbg, "F9 调试入口使用 DEBUG_CATEGORY_COMMAND")
ck("RemoveDebugFunctions(" in _combined_dbg, "调用 RemoveDebugFunctions 清理 F9 入口")
ck(("ChemDebug.Register()" in _entry_text and "ChemDebug.Unregister()" in _entry_text)
   or ("AddDebugFunction(" in _entry_text and "RemoveDebugFunctions(" in _entry_text),
   "F9 入口已挂到 OnEnable/OnDisable 生命周期")
ck(bool(_dbg_text), "存在 src/ChemDebug.cs（F9 调试窗口）")
if _dbg_text:
    ck("OpenSpawnWindow" in _dbg_text or "ShowSpawnWindow" in _dbg_text,
       "ChemDebug 有 F9 按钮 action（OpenSpawnWindow/ShowSpawnWindow）")
    for _need in ("CustomUIManager.Window(", "AddInput", "AddButton",
                  "backInvinvElement", "GraphUtils.CanAccept(",
                  "TryAcceptAllOrDestroy(", "debug_alert_full"):
        ck(_need in _dbg_text, f"ChemDebug 含 {_need}")
    # §8.2 修正：CanAccept 探测 → 失败 Destroy/弹窗 → 成功 TryAcceptAllOrDestroy
    _ic = _dbg_text.find("GraphUtils.CanAccept(")
    _id = _dbg_text.find(".Destroy()", _ic if _ic >= 0 else 0)
    _it = _dbg_text.find("TryAcceptAllOrDestroy(", _ic if _ic >= 0 else 0)
    ck(_ic >= 0 and _id >= 0 and _it >= 0 and _ic < _id < _it,
       "满仓语义：CanAccept 探测 → 失败 Destroy → 成功 TryAcceptAllOrDestroy（顺序）")
    # TryAcceptAll 返回值是「剩余未接受数量」，不得当已接受数使用（剔注释后检查源码）
    _dbg_code = re.sub(r"//[^\n]*", "", _dbg_text)
    _dbg_code = re.sub(r"/\*.*?\*/", "", _dbg_code, flags=re.S)
    ck(re.search(r'\b(?:int|var)\s+placed\s*=', _dbg_code) is None
       and re.search(r'\bplaced\s*<=?\s*[01]\b', _dbg_code) is None,
       "未把 TryAcceptAll 返回值当已接受数（无 placed 变量/比较）")
    ck(re.search(r'GraphUtils\.TryAcceptAll\s*\(', _dbg_code) is None,
       "未直接调用 GraphUtils.TryAcceptAll（应使用 TryAcceptAllOrDestroy）")
    # 满仓提示：优先原版 modal，保留 CustomUIManager 回退
    ck("GenericConfirmationUIManager" in _dbg_text
       and "OpenPanelSingleButton(" in _dbg_text,
       "满仓提示优先原版 GenericConfirmationUIManager.OpenPanelSingleButton")
    _alert = re.search(r'void\s+ShowVanillaAlert\([^)]*\)\s*\{(.*?)\n        \}',
                       _dbg_text, re.S)
    ck(_alert is not None
       and 0 <= _alert.group(1).find("GenericConfirmationUIManager")
       < _alert.group(1).find("CustomUIManager.Window("),
       "满仓提示顺序：GenericConfirmationUIManager 优先、CustomUIManager 回退")
    sw("dev.txt" in _dbg_text,
       "ChemDebug 注释说明 F9 依赖游戏根目录 dev.txt（DevTools 门控，提醒项非缺陷）")
    ck(not any(w in _dbg_text for w in ("frontInvinvElement", "displayElement", "afterhoursElement")),
       "生成目标仅 backInvinvElement（未用 front/display/afterhours）")

# manifest 中文描述（lead 尚未改时记 WARN，改后自动 PASS）
import xml.etree.ElementTree as _ET
_mroot = _ET.parse(os.path.join(ROOT, "FreeChem", "manifest.xml")).getroot()
_mdesc = _mroot.findtext("Description", "")
_mcjk = len(re.findall(r"[\u4e00-\u9fff]", _mdesc))
sw(_mcjk >= 10, f"manifest <Description> 为中文（≥10 CJK）；实际 CJK={_mcjk}")
ck("自由化学" in _mroot.findtext("Name", ""), "manifest <Name> 含「自由化学」")

# ── 18. 第 15 轮：快捷键调试入口（默认 F10，可重绑，不依赖 dev.txt；文本层独立实现）──
_hk_all = _dbg_text + "\n" + _entry_text
ck("ModHelper.AddGameOptionKeyBind(" in _hk_all,
   "注册游戏「选项→按键绑定」快捷键 AddGameOptionKeyBind(")
ck(re.search(r'ModHelper\.OPTION_SECTION_KEYBIND|"keybind"', _hk_all) is not None,
   "按键绑定使用 OPTION_SECTION_KEYBIND/keybind 分区")
ck("option_debug_open" in _hk_all, "按键绑定使用 option_debug_open 本地化键")
ck("ModHelper.RemoveGameOptions(" in _hk_all, "OnDisable 调用 RemoveGameOptions 清理选项")
ck("PlayerPrefs" in _hk_all and "free_chem.debug_hotkey" in _hk_all,
   "PlayerPrefs 持久化 free_chem.debug_hotkey")
ck(re.search(r'PlayerPrefs\.(?:SetInt|GetInt)\s*\(', _hk_all) is not None,
   "PlayerPrefs 用 SetInt/GetInt 存 (int)KeyCode")
ck("KeyCode.F10" in _hk_all, "默认快捷键 KeyCode.F10")
for _hk_need in ("ModHelper.StartCoroutine(", "IEnumerator", "yield return null",
                 "Input.GetKeyDown(", "KeyBindHelper.SuppressHotkeys"):
    ck(_hk_need in _hk_all, f"快捷键轮询含 {_hk_need}")
ck("CustomUIManager.Instance.IsOpen(" in _hk_all,
   "窗口已开时用 CustomUIManager.Instance.IsOpen 切换关闭")
ck("StopCoroutine" in _hk_all, "OnDisable 用 StopCoroutine 停止轮询")
ck("EmporiumEntry.Instance == null" in _hk_all, "轮询含 EmporiumEntry.Instance == null 场景 guard")
ck("OpenSpawnWindow" in _hk_all, "快捷键调用 OpenSpawnWindow（或等价）打开窗口")
ck(re.search(r"\bnew\s+Thread\b|\bTask\.Run\b", _hk_all) is None,
   "快捷键实现无 Thread/Task（仅协程）")
ck("option_debug_open" in _zh_keys and "option_debug_open_tip" in _zh_keys,
   "快捷键入口本地化键 option_debug_open / _tip 齐备")

# ── 19. 第 16 轮：生命周期 NRE 修复 + 设置分区中文化（文本层独立实现）──
def _mbody(text, name):
    m = re.search(r'void\s+' + name + r'\s*\([^)]*\)\s*\{(.*?)\n        \}', text, re.S)
    return m.group(1) if m else None


def _strip_comments(s):
    s = re.sub(r"//[^\n]*", "", s or "")
    return re.sub(r"/\*.*?\*/", "", s, flags=re.S)


_oe = _mbody(_entry_text, "OnEnable")
_ol = _mbody(_entry_text, "OnGameLoaded")
_od = _mbody(_entry_text, "OnDisable")
_tr = _mbody(_entry_text, "TryRegisterWildNetwork")
ck(_oe is not None, "解析到 ModEntry.OnEnable() 方法体")
_oe_code = _strip_comments(_oe)
ck(re.search(r'\bRegisterWildNetwork\s*\(', _oe_code) is None,
   "OnEnable 不调用 RegisterWildNetwork（主菜单 PlayerStore..ctor NRE 根因；注释除外）")
ck(re.search(r'\bAddNetworkUpgrade\s*\(', _oe_code) is None,
   "OnEnable 不直接调用 AddNetworkUpgrade")
ck(_ol is not None and "TryRegisterWildNetwork(" in (_ol or ""),
   "OnGameLoaded 调用 TryRegisterWildNetwork")
if _ol and "TryRegisterWildNetwork(" in _ol and "ChemRuntime.Load()" in _ol:
    ck(_ol.find("TryRegisterWildNetwork(") < _ol.find("ChemRuntime.Load()"),
       "TryRegisterWildNetwork 在 ChemRuntime.Load() 之前（先注册再恢复解锁）")
ck(_tr is not None, "存在 TryRegisterWildNetwork() 方法")
if _tr:
    _tr_code = _strip_comments(_tr)
    ck("PlayerStore.HasInstance" in _tr_code, "TryRegisterWildNetwork 有 PlayerStore.HasInstance guard")
    _rg = re.search(r'\bRegisterWildNetwork\s*\(', _tr_code)
    if "PlayerStore.HasInstance" in _tr_code and _rg:
        ck(_tr_code.find("PlayerStore.HasInstance") < _rg.start(),
           "guard 在 RegisterWildNetwork() 之前")
    else:
        ck(False, "TryRegisterWildNetwork 内缺 RegisterWildNetwork() 调用")
    ck("try" in _tr_code and "catch" in _tr_code, "TryRegisterWildNetwork 有 try/catch")
ck(re.search(r'bool\s+networkRegistered', _entry_text) is not None, "有 networkRegistered 幂等标志")
if _od:
    _od_code = _strip_comments(_od)
    ck("RemoveNetworkUpgrades(" in _od_code, "OnDisable 调用 RemoveNetworkUpgrades")
    if "RemoveNetworkUpgrades(" in _od_code and "try" in _od_code:
        _i_rm = _od_code.find("RemoveNetworkUpgrades(")
        ck(_od_code.find("try") < _i_rm and _od_code.find("catch", _i_rm) > _i_rm,
           "OnDisable 的 RemoveNetworkUpgrades 在 try/catch 内")
    ck("networkRegistered = false" in _od_code, "OnDisable 复位 networkRegistered")
for _lit in ('config.Define("dangerous_reactions", true, "玩法"',
             'config.Define("vanilla_security", true, "玩法"',
             'config.Define("debug_mode", false, "调试"'):
    ck(_lit in _entry_text, "设置分区中文化: " + _lit)
ck(re.search(r'config\.Define\(\s*"[a-z_]+"\s*,\s*(?:true|false)\s*,\s*"(?:Gameplay|Debug)"',
             _entry_text) is None,
   'ModConfig 分区无英文 "Gameplay"/"Debug" 残留')
_en_keys = {r[0] for r in _csv.reader(
    open(os.path.join(ROOT, "FreeChem", "Localization", "en.csv"), encoding="utf-8"))
    if r and not r[0].startswith("#")}
for _k in ("cfg_dangerous", "cfg_vanilla_security", "cfg_debug_mode"):
    ck(_k in _zh_keys and _k in _en_keys, f"设置项本地化键 zh/en 齐备: {_k}")
_doc_blob = ""
for _dp in glob.glob(os.path.join(ROOT, "docs", "*.md")) + [os.path.join(ROOT, "README.md")]:
    if os.path.exists(_dp):
        _doc_blob += open(_dp, encoding="utf-8", errors="replace").read()
sw(("设置面板" in _doc_blob or "玩法" in _doc_blob)
   and ("PlayerStore" in _doc_blob or "NRE" in _doc_blob or "空引用" in _doc_blob
        or "Init failed" in _doc_blob),
   "文档记录设置面板中文化与 PlayerStore NRE 生命周期修复（软断言）")

# 编码回归：src/*.cs 合法 UTF-8；F9 标签「FreeChem 按ID生成」不得被编码破坏
_enc_bad = []
for _p in glob.glob(os.path.join(SRC, "*.cs")):
    try:
        open(_p, "rb").read().decode("utf-8")
    except UnicodeDecodeError:
        _enc_bad.append(os.path.basename(_p))
ck(not _enc_bad, "src/*.cs 全部为合法 UTF-8；异常=" + str(_enc_bad))
ck("按ID生成" in _entry_text, "F9 调试入口标签「FreeChem 按ID生成」未被编码破坏")
_moji = sorted({os.path.basename(_p) for _p in glob.glob(os.path.join(SRC, "*.cs"))
                if re.search(r"[鎸鐢鈥銆锛]", open(_p, encoding="utf-8", errors="replace").read())})
sw(not _moji, "src 无 GBK/UTF-8 二次编码乱码字符；疑似=" + str(_moji))

# ── 20. P0（第 17 轮）：tag/欣快、相态、原版药剂兼容、商人、量纲、仪器提示（独立实现）──
_mach_text = open(os.path.join(SRC, "ChemMachines.cs"), encoding="utf-8").read()
_heat_text = open(os.path.join(SRC, "ChemHeating.cs"), encoding="utf-8").read()
_lab_text = open(os.path.join(SRC, "ChemLab.cs"), encoding="utf-8").read()


def _mb(text, name, ret=None):
    """按方法名提取方法体（支持任意返回类型与表达式体）。"""
    m = re.search(r'\b' + name + r'\s*\([^)]*\)\s*\{(.*?)\n        \}', text, re.S)
    if m:
        return m.group(1)
    m = re.search(r'\b' + name + r'\s*\([^)]*\)\s*=>\s*(.+?);', text, re.S)
    return m.group(1) if m else None


_create = _mb(cat, "Create")
_ck = _strip_comments(_create)
ck("EnableTag(ChemRecipes.ReagentTag" in _ck, "Create 对药剂打 ReagentTag")
_ik = re.search(r'bool\s+IsReagentKind\s*\([^)]*\)\s*(?:\{(.*?)\n        \}|=>\s*([^;]+);)',
                cat + "\n" + text, re.S)
_rk_arr = re.search(r'ReagentKinds\s*=\s*\{([^}]*)\}', cat + "\n" + text, re.S)
ck(_ik is not None, "存在 Kind 白名单函数 IsReagentKind(...)")
if _ik:
    _ikb = _ik.group(1) if _ik.group(1) is not None else _ik.group(2)
    _wl = _ikb + (" " + _rk_arr.group(1) if _rk_arr else "")
    ck(all(f'"{k}"' in _wl for k in ("gas", "liquid", "solid", "illicit", "canister", "drum")),
       "IsReagentKind 白名单包含真药剂 6 类 Kind")
    ck(not any(f'"{k}"' in _wl for k in ("tool", "vessel", "heater", "protective",
                                         "container", "byproduct", "consumable")),
       "IsReagentKind 白名单不含工具/器材/副产物 Kind")
_profl = _mb(_runtime_text, "ProfileLine")
ck(_profl is not None and re.search(r'(?:EuphoriaTier|tier)\s*>=\s*1', _strip_comments(_profl)) is not None,
   "ProfileLine 欣快仅在 EuphoriaTier>=1 时展示")
ck(_profl is not None and "Controlled" in _strip_comments(_profl),
   "ProfileLine 欣快条件含 Controlled（违禁品）")
_tip = _mb(_entry_text, "OnTooltip")
ck(_tip is not None and ("IsReagentKind" in _tip or "IsReagentSpec" in _tip or "Kind" in _tip),
   "OnTooltip 先按 Kind 过滤（不只看 tag）")
_pblock = re.search(r'Profiles\s*=\s*new Dictionary<string, ChemProfile>(.*?)\};', text, re.S)
_pb = _pblock.group(1) if _pblock else ""
ck(all(f'"{c}"' in _pb for c in ("gas_chlorine", "gas_hydrogen_chloride", "gas_ammonia",
                                 "liq_hydrochloric_acid", "liq_sulfuric_acid", "liq_nitric_acid",
                                 "liq_sodium_hydroxide", "liq_hydrogen_peroxide",
                                 "sol_bleaching_powder")),
   "Profiles 覆盖强酸/强碱/氧化剂/氯气/氨/漂白粉等")
_acc = _mb(_mach_text, "AcceptsInput")
_acc_c = _strip_comments(_acc)
_i_p = _acc_c.find("AllowedInputPhases")
_i_s = _acc_c.find("CHEMICAL_SUPPLIES")
ck(_i_p >= 0, "AcceptsInput 用 AllowedInputPhases 做相态过滤")
ck(_i_p < 0 or _i_s < 0 or _i_p < _i_s,
   "AcceptsInput 相态判断在 CHEMICAL_SUPPLIES 放行之前（若仍保留该放行）")
ck("VanillaAnalog" in _acc_c, "AcceptsInput 用 VanillaAnalog 兼容原版药剂")
ck("VanillaAnalog" in (_mb(_mach_text, "RunMachineCycle") or ""),
   "RunMachineCycle 用 VanillaAnalog 映射原版投料")
ck("VanillaAnalog" in _heat_text, "ChemHeating 用 VanillaAnalog 映射原版投料")
_fin = _mb(_mach_text, "Finish") or ""
ck("VanillaAnalog" in _fin and "Destroy(" in _fin, "Finish 销毁原版药剂瓶")
_dv = _strip_comments(_mb(_entry_text, "DeliverGasOrder") or "")
ck("MoveItemToBackCounter(" in _dv, "气体订单投放到后柜台")
ck("MoveItemToAfterhours(" not in _dv, "气体订单不再用 Afterhours")
ck("SetItemOwnerShip(" in _dv and "SetSource(" in _dv, "订单物品 SetItemOwnerShip/SetSource")
_ng = _mb(_entry_text, "OnNewGameStarted") or ""
ck("SetItemOwnerShip(" in _ng and "SetSource(" in _ng, "起始礼包 SetItemOwnerShip/SetSource")
ck("SetItemOwnerShip(" in _dbg_text and "SetSource(" in _dbg_text,
   "调试生成物品 SetItemOwnerShip/SetSource")
_sus_b = _strip_comments(_mb(_lab_text, "SuspicionFrom") or "")
ck(re.search(r'\*\s*(?:10\b|VanillaEndangermentPerUnit\b)', _sus_b) is not None,
   "SuspicionFrom = raw*10*bp/10000（含 ×10 / ×VanillaEndangermentPerUnit）")
ck(re.search(r'\*\s*100\b', _sus_b) is None, "SuspicionFrom 无旧式 *100")
ck("VanillaEndangermentPerUnit" in _lab_text, "VanillaEndangermentPerUnit 常量在 ChemLab")
ck("ChemLab.VanillaEndangermentPerUnit" in _runtime_text,
   "ChemRuntime 共用 ChemLab.VanillaEndangermentPerUnit")
ck(re.search(r'VolumeUl\s*=\s*250000', _runtime_text) is not None, "VolumeUl = 250000（250ml/件）")
ck("量纲" in _lab_text, "ChemLab 有「量纲约定」注释块")
_eq_text = open(os.path.join(SRC, "ChemEquipment.cs"), encoding="utf-8").read() \
    if os.path.exists(os.path.join(SRC, "ChemEquipment.cs")) else ""
_inst_blob = _entry_text + _mach_text + _eq_text
for _k in ("inst_temp", "inst_ph", "inst_balance", "inst_protection",
           "inst_fuel", "inst_wick", "inst_filter"):
    ck(_k in _inst_blob, f"仪器状态提示键 {_k} 已接线")
_ui_blob = _mach_text + _entry_text
for _k in ("ui_modules", "ui_input", "ui_output", "ui_vessel",
           "ui_heater", "ui_wick", "ui_fuel"):
    ck(f'"{_k}"' in _ui_blob, f"机器窗口标签键 {_k} 已接线")
for _tok in ("MachineHelper.CreateMachineryWindow(", "MachineHelper.PowerPanel(",
             "MachineHelper.NotePanel("):
    ck(_tok in _ui_blob, f"P4a 机器窗口官方构建器 {_tok}")
ck("GetLocalized" in _mach_text, "机器窗口标签走 ModHelper.GetLocalized（Ui 辅助）")
ck(not _eq_text or "GetLocalized" in _eq_text, "仪器状态提示走 ModHelper.GetLocalized（L 辅助）")
for _k in ("state_on", "state_off", "temp_0", "temp_1", "temp_2", "temp_3"):
    ck(_k in _zh_keys and _k in _en_keys, f"P0 状态/温度档本地化键 {_k} zh/en 齐备")
ck("chem_profile_reagent" in _runtime_text, "ProfileLine 普通药剂画像键 chem_profile_reagent")
ck("chem_medical_grade" in (_entry_text + _runtime_text + cat + _mach_text),
   "医用气罐 tooltip 键 chem_medical_grade")

# ── 21. P1a（第 18 轮）：原版数据桥接 / 七档纯度 / PourParts / Harmony 兜底（独立实现）──
_cat_text = cat
_create_p1 = _mb(_cat_text, "Create") or ""
_c1 = _strip_comments(_create_p1)
for _tok in ("CHEM_ITEM_TAG", "CHEMICAL_PRODUCT_TAG", "CHEMICAL_PRODUCT_PURITY_TAG",
             "SetPurityFeature("):
    ck(_tok in _c1, f"Create 含原版桥接 {_tok}")
ck(re.search(r'SetDouble\s*\(', _c1) is not None, "纯度 tag 用 SetDouble 写 0–100")
_elb2 = _mb(_cat_text, "EnsureLiquidBridge") or ""
_addp2 = _mb(_cat_text, "AddPourParts") or ""
ck("InitLiquidContainerItem(" in (_c1 + _elb2), "液体走 WaterHelper.InitLiquidContainerItem（Create/EnsureLiquidBridge）")
ck("AddLiquid(" in (_elb2 + _addp2) and "PourParts" in _elb2,
   "液体按 PourParts 调 WaterHelper.AddLiquid")
_i_init = (_c1 + _elb2).find("InitLiquidContainerItem(")
ck(_i_init >= 0 and (_c1 + _elb2).rfind("SetSpriteAndShapeFromMod") > _i_init,
   "InitLiquidContainerItem 之后恢复本模组图标")
for _fn in ("CreateLabPure", "CreatePharmaGrade", "CreateNearPharma", "CreateStreetStandard",
            "CreateCutProduct", "CreateJunkGrade", "CreateStarchGrade"):
    ck(_fn + "(" in _cat_text + "\n" + text, f"七档纯度工厂 {_fn} 存在")
for _name, _val in {"GradeLabPure": 99, "GradePharma": 90, "GradeNearPharma": 80,
                    "GradeStreet": 60, "GradeCut": 45, "GradeJunk": 30}.items():
    _cm = re.search(_name + r'\s*=\s*(\d+)', _cat_text + "\n"
                    + open(os.path.join(SRC, "ChemLab.cs"), encoding="utf-8").read())
    ok_th = (int(_cm.group(1)) == _val) if _cm else \
        (re.search(r'>=\s*' + str(_val) + r'\b', _cat_text) is not None)
    ck(ok_th, f"七档阈值 {_name}={_val}（或字面 >= {_val}）")
ck("PourParts" in text and "TryGetPourParts(" in text, "ChemRecipes 有 PourParts + TryGetPourParts")
_pp = None
_pm2 = re.search(r'PourParts\s*=\s*new\s+Dictionary', text)
if _pm2:
    _j = text.find("{", _pm2.end())
    _depth = 0
    for _k in range(_j, len(text)):
        if text[_k] == "{":
            _depth += 1
        elif text[_k] == "}":
            _depth -= 1
            if _depth == 0:
                _pp = text[_j:_k + 1]
                break
ck(_pp is not None, "解析到 PourParts 数据块")
if _pp:
    _pp = re.sub(r"//[^\n]*", "", _pp)   # 剔除注释（「70% 乙醇」等数字会干扰求和）
    _ents, _cur, _d = [], "", 0
    for _ch in _pp[1:-1]:
        if _ch == "{":
            _d += 1
        if _ch == "}":
            _d -= 1
        if _ch == "," and _d == 0:
            _ents.append(_cur)
            _cur = ""
        else:
            _cur += _ch
    _ents.append(_cur)
    _keys, _bad_sum = set(), []
    for _e in _ents:
        _ids = re.findall(r'"(liq_[a-z0-9_]+)"', _e)
        if not _ids:
            continue
        _keys.add(_ids[0])
        _nums = [int(x) for x in re.findall(r'(?<![A-Za-z0-9_.])(\d{1,3})(?![A-Za-z0-9_.])', _e)]
        if sum(_nums) != 100:
            _bad_sum.append(f"{_ids[0]}={sum(_nums)}")
    ck(not _bad_sum, "PourParts 每条和=100；异常=" + str(_bad_sum[:8]))
    _liq_ids = set(re.findall(r'S\("(liq_[a-z0-9_]+)"\s*,\s*"[^"]*"\s*,\s*Phase\.Liquid', cat))
    ck(not (_liq_ids - _keys), "PourParts 覆盖全部液体药剂；缺=" + str(sorted(_liq_ids - _keys)[:8]))
    _h2 = re.search(r'"(liq_hydrogen_peroxide)"[^}]*\}', _pp, re.S)
    ck(_h2 is not None and "water" in _h2.group(0), "双氧水近似映射为 water")
    ck("近似" in text, "PourParts 有「近似映射」说明")
ck("PurityOf(" in _runtime_text, "ChemRuntime 有 PurityOf(item)")
_ppath = os.path.join(SRC, "ChemPatches.cs")
_patch = open(_ppath, encoding="utf-8").read() if os.path.exists(_ppath) else ""
ck(bool(_patch), "存在 src/ChemPatches.cs")
if _patch:
    ck(_patch.count("[HarmonyPatch(") >= 4, "ChemPatches 含 ≥4 个 [HarmonyPatch]")
    for _need in ('typeof(ChemScannerUIManager)', '"RefreshChemMode"', 'typeof(WaterHelper)',
                  '"TransferLiquid"', 'typeof(ChemicalFeedbackHelper)', '"IsLethal"',
                  '"IsToxic"', '"GetEuphoriaLevel"', 'typeof(StoreClient)',
                  '"IsClientBuyingThisItem"'):
        ck(_need in _patch, f"ChemPatches 覆盖 {_need}")
    ck(re.search(r'new\s*\[\s*\]\s*\{\s*typeof\s*\(\s*GameItem\s*\)\s*,\s*typeof\s*\(\s*bool\s*\)\s*\}',
                 _patch) is not None,
       "IsClientBuyingThisItem 显式参数类型防歧义")
    ck(not any(b in _strip_comments(_patch) for b in ("PatchAll(", "UnpatchAll(",
                                                      "new Thread", "Task.Run", "DllImport")),
       "ChemPatches 无 PatchAll/UnpatchAll/线程/IO")
    ck("catch" in _strip_comments(_patch), "ChemPatches 补丁有 try/catch 防御")
    _si = _patch.find('"RefreshChemMode"')
    _region = _patch[_si:_si + 3000] if _si >= 0 else ""
    ck("CHEMICAL_PRODUCT_TAG" in _region and "IsTag(" in _region and "return;" in _region,
       "scanner Postfix 有「已桥接/已带 tag 直接 return」守卫")
    ck("RefreshUI(" not in _region
       or (re.search(r'bridging|reentran|inPatch|guard', _region) is not None
           and "finally" in _region and re.search(r'bridging\s*=\s*false', _region) is not None),
       "scanner RefreshUI 有重入保护标志且 finally 复位（防递归）")
_csproj = open(os.path.join(SRC, "FreeChem.csproj"), encoding="utf-8").read()
ck("0Harmony" in _csproj, "csproj 引用 0Harmony")
ck("scanner_fc_line" in _zh_keys and "scanner_fc_line" in _en_keys,
   "P1a 本地化键 scanner_fc_line zh/en 齐备")
ck(_zh_keys == _en_keys, "zh/en 本地化键集合一致")
_doc_p1a = ""
for _dp in glob.glob(os.path.join(ROOT, "docs", "*.md")) + [os.path.join(ROOT, "README.md")]:
    if os.path.exists(_dp):
        _doc_p1a += open(_dp, encoding="utf-8", errors="replace").read()
sw(("P1a" in _doc_p1a or "原版桥接" in _doc_p1a) and "Harmony" in _doc_p1a,
   "文档记录 P1a 原版桥接与 Harmony 兜底（软断言）")

# ── 22. P1b1（第 19 轮）：标签打印机 + 单钢瓶纯度（文本层独立实现）──
_p_path = os.path.join(SRC, "ChemPrinter.cs")
_printer = open(_p_path, encoding="utf-8").read() if os.path.exists(_p_path) else ""
ck("label_printer" in spec_ids, "Specs 注册 label_printer")
ck(re.search(r'S\("label_printer"[^)]*"tool"', cat) is not None, "label_printer Kind = tool")
ck("label_printer" in _loot_ids, "掉落表含 label_printer")
_app2 = {e["id"] for t in _loot.get("tables", []) if t.get("id") == "free_chem_apparatus"
         for e in t.get("entries", [])}
_patch2 = {e.get("id") for pa in _loot.get("patches", []) for e in pa.get("add", [])}
ck("label_printer" in (_app2 | _patch2), "label_printer 进 free_chem_apparatus/toolTable")
for _hk in ("mayThisTargetItemFunc", "canThisTargetItemFunc", "onThisTargetItemFunc"):
    ck(_hk in (_entry_text + _printer), f"target 钩子 {_hk} 已赋值")
_canm = re.search(r'canThisTargetItemFunc\s*=', _entry_text + _printer)
if _canm:
    _cs = (_entry_text + _printer)[_canm.start():_canm.start() + 600]
    ck(("IsPrintableTarget" in _cs or "IsReagentKind" in _cs
        or "ReagentTag" in _cs or "IsOurReagent" in _cs),
       "can 钩子按 Kind/ReagentTag/IsPrintableTarget 判定")
    _hm = re.search(r'bool\s+IsPrintableTarget\s*\([^)]*\)\s*\{(.*?)\n        \}',
                    _entry_text + _printer, re.S)
    ck(_hm is not None and ("IsOurReagent" in _strip_comments(_hm.group(1))
                            or "IsReagentKind" in _strip_comments(_hm.group(1))
                            or "ReagentTag" in _strip_comments(_hm.group(1))),
       "IsPrintableTarget 只接受本模组真药剂")
    ck(_hm is not None and "label_printer" not in _strip_comments(_hm.group(1)),
       "IsPrintableTarget 不把打印机自身当目标")
_onm = re.search(r'onThisTargetItemFunc\s*=', _entry_text + _printer)
if _onm:
    _os = (_entry_text + _printer)[_onm.start():_onm.start() + 600]
    ck(re.search(r'(?:ChemPrinter\.)?Stamp\s*\(', _os) is not None, "on 钩子调用 Stamp(")
ck(bool(_printer), "存在 src/ChemPrinter.cs")
if _printer:
    ck(re.search(r'\b14\b', _printer) is not None, "ChemPrinter 含 14 模式（7×2）")
    ck("printer_mode" in (_entry_text + _printer + cat), "tooltip/激活显示 printer_mode")
    ck(re.search(r'SetPurityFeature\s*\([^)]*,[^)]*\)', _printer) is not None,
       "Stamp 用 SetPurityFeature(real, fake) 两参重载")
    for _fn in ("CreateStarchGrade", "CreateJunkGrade", "CreateCutProduct",
                "CreateStreetStandard", "CreateNearPharma", "CreatePharmaGrade", "CreateLabPure"):
        ck(_fn + "(" in _printer, f"ChemPrinter 七档 {_fn}")
    ck("TypeHelper.MEDICAL" in _printer and 'EnableTag("fc_medical")' in _printer,
       "医疗品标签 SetGameItemType(MEDICAL)+fc_medical")
    _st = _mb(_printer, "Stamp")
    ck(_st is None or "GradeProduct" not in _strip_comments(_st),
       "Stamp 不调用 GradeProduct 覆盖真实/标签")
ck(re.search(r'skus\s*\[\s*Mathf\.Clamp\(', _mach_text) is None,
   "灌装台不再按 inputGrade 选三档")
ck(re.search(r'skus\s*\[\s*0\s*\]', _mach_text) is not None, "灌装台固定 skus[0] 工业钢瓶")
ck("SyncVanillaPurity(" in cat, "存在 SyncVanillaPurity( 原版纯度同步")
ck("SyncVanillaPurity(" in (_mb(_mach_text, "Finish") or ""),
   "Finish 在纯度变化后调用 SyncVanillaPurity(")
ck("can_oxygen_medical" in cat and "can_oxygen_ultrapure" in cat,
   "legacy medical/ultrapure 气罐仍注册")
ck(not [i for i in _loot_ids if i.startswith("can_") and ("medical" in i or "ultrapure" in i)],
   "掉落表不再产生 medical/ultrapure 气罐")
for _k in ("label_printer_name", "label_printer_desc", "printer_mode",
           "printer_cat_chemical", "printer_cat_medical", "printer_stamp_ok",
           "printer_stamp_fail", "printer_stamp_target_invalid"):
    ck(_k in _zh_keys and _k in _en_keys, f"打印机本地化键 {_k} zh/en 齐备")
_100blob = _lab_text + "\n" + _printer + "\n" + cat
sw("100%" in _100blob and ("校准" in _100blob or "提纯" in _100blob),
   "100% 纯度 P1c 校准/提纯预留说明（软断言）")
_doc_p1b = ""
for _dp in glob.glob(os.path.join(ROOT, "docs", "*.md")) + [os.path.join(ROOT, "README.md")]:
    if os.path.exists(_dp):
        _doc_p1b += open(_dp, encoding="utf-8", errors="replace").read()
sw("P1b1" in _doc_p1b or "标签打印机" in _doc_p1b, "文档记录 P1b1 标签打印机（软断言）")

# ── 23. P1c（第 20 轮）：多级提纯 + 打印机校准 100%（文本层独立实现）──
ck("PurifyFloor(" in _lab_text, "ChemLab 有 PurifyFloor(")
ck(re.search(r'ResidualFloor\s*>>\s*\w+|>>\s*(?:level|l)\b', _lab_text) is not None,
   "PurifyFloor 用 70 >> level 位移")
ck(re.search(r'PurityFrom\s*\(\s*int\[\]\s*\w+\s*,\s*int\s+\w+\s*\)', _lab_text) is not None,
   "ChemLab 有 PurityFrom(int[] c, int level) 重载")
ck("MaxPurity(" in _lab_text, "ChemLab 有 MaxPurity(level)")
ck(re.search(r'MaxPurifyLevel\s*=\s*5', _lab_text) is not None, "MaxPurifyLevel = 5")
for _tok in ("TPurifyLevel", "ReadPurifyLevel(", "ApplyContaminants("):
    ck(_tok in cat, "ChemCatalog 有 " + _tok)
ck(re.search(r'ApplyContaminants\s*\([^)]*,[^)]*,[^)]*\)', cat) is not None,
   "ApplyContaminants 有 (item,c,level) 三参重载")
_ws = _mb(cat, "WriteState") or ""
ck("TPurifyLevel" in _ws and re.search(r'TPurifyLevel[^;]*\b0\b', _ws) is not None,
   "WriteState 初始提纯等级 = 0")
ck(re.search(r'Math\.Min\s*\(\s*(?:ChemLab\.)?MaxPurifyLevel\s*,\s*[^)]*\+\s*1\s*\)', _mach_text) is not None
   or re.search(r'newLevel\s*=\s*Math\.Min\s*\(\s*5\s*,', _mach_text) is not None,
   "过滤台提纯等级 = min(5, old+1)")
ck(re.search(r'ChemLab\.Filter\s*\([^)]*,[^)]*,[^)]*\)', _mach_text) is not None,
   "过滤台用新等级计算纯度（Filter 三参）")
ck(re.search(r'ApplyContaminants\s*\([^)]*(?:newLevel|purifyLevel)', _mach_text) is not None,
   "过滤台按新等级写回污染物")
ck("SyncVanillaPurity(" in (_mb(_mach_text, "Finish") or ""),
   "Finish 纯度变化后 SyncVanillaPurity(")
if _printer:
    ck(re.search(r'ModeCount\s*=\s*LabelModeCount\s*\+\s*1', _printer) is not None
       or re.search(r'ModeCount\s*=\s*15\b', _printer) is not None,
       "打印机模式 15 = 14 标签 + 校准")
    ck("printer_mode_calibrate" in _printer, "校准模式用 printer_mode_calibrate")
    ck(re.search(r'(?:MaxCatalystCharge\s*=\s*3|<\s*3\b)', _printer + cat) is not None,
       "催化剂充能上限 3")
    ck(re.search(r'fc_catalyst_charge|TCatalystCharge', _printer + cat) is not None,
       "催化剂充能 tag")
    ck("Destroy(" in _printer, "充能销毁 catalyst_kit")
    ck("Calibrate" in _printer, "存在 Calibrate 校准实现")
    ck(re.search(r'CalibrationMinPurity\s*=\s*99\.5\b', _printer) is not None,
       "校准纯度门槛 99.5")
    ck("CreateLabPure()" in _printer, "校准成功写 LabPure 档")
    ck("SyncVanillaPurity(" in _printer, "校准后 SyncVanillaPurity(")
    ck("printer_calibrate_fail_purity" in _printer and "printer_calibrate_fail_charge" in _printer,
       "校准失败两分支键齐备")
    _cal = _mb(_printer, "Calibrate")
    ck(_cal is None or "GradeProduct" not in _strip_comments(_cal),
       "Calibrate 不调用 GradeProduct")
    if _cal:
        _calc = _strip_comments(_cal)
        ck("ApplyContaminants" in _calc and "MaxPurifyLevel" in _calc and "CreateLabPure" in _calc,
           "校准成功路径：污染物清零 + level=5 + lab_pure")
for _k in ("printer_mode_calibrate", "printer_charge", "printer_charge_ok",
           "printer_calibrate_ok", "printer_calibrate_fail_purity",
           "printer_calibrate_fail_charge", "purify_level", "chem_lab_pure_100"):
    ck(_k in _zh_keys and _k in _en_keys, f"P1c 本地化键 {_k} zh/en 齐备")
_doc_p1c = ""
for _dp in glob.glob(os.path.join(ROOT, "docs", "*.md")) + [os.path.join(ROOT, "README.md")]:
    if os.path.exists(_dp):
        _doc_p1c += open(_dp, encoding="utf-8", errors="replace").read()
sw(("P1c" in _doc_p1c or "100% 分析纯" in _doc_p1c) and "提纯等级" in _doc_p1c,
   "文档记录 P1c/100% 分析纯/提纯等级（软断言）")

# ── 24. P2a（第 21 轮）：10 种新化学品 + 反应式重写/副产物（独立实现）──
_by_id = {r["id"]: r for r in reactions}
for _iid in ("sol_calcium_oxide", "sol_calcium_hydroxide", "sol_sodium_carbonate",
             "sol_sodium_bicarbonate", "sol_ammonium_chloride", "sol_manganese_dioxide",
             "sol_copper_oxide", "sol_potassium_sulfate", "sol_sodium_nitrate",
             "sol_potassium_manganate"):
    ck(_iid in spec_ids, f"Specs 注册 P2a 物品 {_iid}")
    ck(re.search(r'S\("' + _iid + r'"[^\n]*Phase\.Solid', cat) is not None,
       f"{_iid} 相态为 solid")
    for _sfx in ("_name", "_desc", "_flavor"):
        ck(_iid + _sfx in _zh_keys and _iid + _sfx in _en_keys,
           f"{_iid}{_sfx} zh/en 本地化齐备")


def _rxin(rid):
    return _by_id.get(rid, {}).get("inputs", [])


def _rxout(rid):
    return _by_id.get(rid, {}).get("outputs", [])


ck("liq_water" not in _rxout("co2_from_marble"), "co2_from_marble 不再产出水物品（水作溶剂）")
ck("liq_water" not in _rxout("peroxide_decomp"), "peroxide_decomp 不再产出水物品（水作溶剂）")
ck({"sol_manganese_dioxide", "sol_potassium_manganate"} <= set(_rxout("kmno4_decomp")),
   "kmno4_decomp 产出 MnO2 + 锰酸钾")
ck("sol_sodium_nitrate" in _rxout("ammonia_liberation"),
   "ammonia_liberation 产出硝酸钠（水不计物品）")
ck("sol_calcium_hydroxide" in _rxin("hypochlorite"), "hypochlorite 输入熟石灰")
ck("liq_calcium_chloride" in _rxout("hypochlorite"), "hypochlorite 产出氯化钙溶液")
ck(not any("sodium_hydroxide" in t for t in _rxin("hypochlorite")),
   "hypochlorite 不再以 NaOH 为输入")
ck("liq_water" not in _rxout("salt_crystallize"), "salt_crystallize 不再产出水物品（水作溶剂）")
ck("liq_water" not in _rxout("nh4no3_crystallize"), "nh4no3_crystallize 不再产出水物品（水作溶剂）")
ck("liq_brine" in set(_rxout("neutralization")),
   "neutralization 产出盐水（水不计物品）")
ck(not any(o.startswith("sol_") for o in _rxout("neutralization")),
   "neutralization 不直接产出固体")
ck("liq_water" in _rxin("hcl_absorption") and "liq_water" in _rxin("ammonia_absorption"),
   "两种吸收塔均以水为输入")
ck("sol_potassium_sulfate" in _rxout("nitric_from_niter"), "硝石制硝酸副产硫酸钾")
_mnx = re.search(r'new Reaction\(\s*"nitric_from_niter"\s*,\s*"[^"]*"\s*,\s*"([^"]*)"', text, re.S)
ck(_mnx is not None and "K2SO4" in _mnx.group(1) and "2HNO3" in _mnx.group(1),
   "nitric_from_niter 配平式含 K2SO4 与 2HNO3")
for _rid in ("lime_calcination", "slaked_lime", "soda_from_caustic",
             "baking_soda", "ammonium_chloride"):
    ck(_rid in _by_id, f"存在新反应 {_rid}")
    for _tok in _rxin(_rid) + _rxout(_rid):
        ck(_tok in (spec_ids | set(machine_ids)), f"[{_rid}] 引用 id {_tok} 已注册")
_allio = set()
for _r in reactions:
    _allio |= set(_r["inputs"]) | set(_r["outputs"])
ck("sol_copper_oxide" not in _allio, "sol_copper_oxide 不在反应表（仅掉落）")
_vl2 = _mb(text, "ValidateLogic") or ""
ck("SpecExists" in _vl2 and "Inputs" in _vl2 and "Outputs" in _vl2,
   "ValidateLogic 校验反应输入/输出均已注册")
ck("→" in _vl2, "ValidateLogic 校验 equation 含「→」")
_eq_pat = re.compile(r'new Reaction\(\s*"([^"]+)"\s*,\s*"[^"]*"\s*,\s*"([^"]*)"', re.S)
_eqs = dict(_eq_pat.findall(text))
ck(len(_eqs) >= 39, f"解析到反应 equation（{len(_eqs)} 条）")
ck(all("→" in _eq for _eq in _eqs.values()), "所有反应 equation 均含「→」")
for _i in ("sol_calcium_oxide", "sol_sodium_carbonate", "sol_sodium_bicarbonate",
           "sol_ammonium_chloride", "sol_copper_oxide"):
    ck(_i in _loot_ids, f"掉落表含新化学品 {_i}")
_doc_p2a = ""
for _dp in glob.glob(os.path.join(ROOT, "docs", "*.md")) + [os.path.join(ROOT, "README.md")]:
    if os.path.exists(_dp):
        _doc_p2a += open(_dp, encoding="utf-8", errors="replace").read()
sw(("P2a" in _doc_p2a or "化学品扩充" in _doc_p2a) and "反应式" in _doc_p2a,
   "文档记录 P2a 化学品扩充与反应式重写（软断言）")

# ── 25. P3a（第 22 轮）：500ml / 画像 / 酒精灯 / 滤纸 / 16×48 占格（独立实现）──
ck(re.search(r'PourVolumeMl\s*=\s*500\b', text) is not None, "PourVolumeMl = 500（每件 500ml）")
ck("500000" in text or "500000" in cat
   or re.search(r'PourVolumeMl\s*\*\s*PourPartPerMl', text + cat) is not None,
   "桥接总 part = 500000（500ml×1000 守恒）")
ck("PourVolumeMl" in _heat_text, "加热台冷却公式使用 500ml 量纲")
ck("TFuelPurity" in cat, "ChemCatalog 有 TFuelPurity 常量")
ck(re.search(r'FuelCapacityMl\s*=\s*500\b', cat) is not None, "FuelCapacityMl = 500")
_tick3 = _mb(_heat_text, "Tick") or ""
ck("TFuelPurity" in _tick3, "Tick 读灯自身燃料纯度 tag")
ck("FuelPurity(" not in _strip_comments(_tick3), "Tick 不再对已销毁燃料调用 FuelPurity（旧 bug）")
_ref3 = _mb(_heat_text, "Refill") or ""
ck("TFuelPurity" in _ref3 and "FuelCapacityMl" in _ref3,
   "Refill 写灯 TFuelPurity 且灌装量按 FuelCapacityMl")
_con3 = _mb(_heat_text, "ConsumeFuel") or ""
ck("FuelBurnRate" in _con3, "ConsumeFuel 按 FuelBurnRate 消耗")
ck("TWickDur" in _con3 and "Destroy(" in _con3 and "Log(" in _con3,
   "灯芯耐久 -1、归零销毁、日志")
_fpb = re.search(r'if\s*\(cid\.StartsWith\("filter_paper"\)\)\s*\{(.*?)\}', _mach_text, re.S)
ck(_fpb is not None and "filtration_bench" in _fpb.group(1) and "ids.Add(cid)" in _fpb.group(1),
   "滤纸仅在 filtration_bench 进入投料 ids")
_fin3 = _mb(_mach_text, "Finish") or ""
ck("TFilterDur" in _fin3 and "Destroy(" in _fin3 and "[filter]" in _fin3,
   "Finish 滤纸耐久 -1 / 归零销毁 / 日志")
for _i in ("liq_hydrochloric_acid", "liq_sulfuric_acid", "liq_nitric_acid",
           "liq_sodium_hydroxide", "liq_hydrogen_peroxide", "liq_brine",
           "liq_ammonia_water", "liq_calcium_chloride"):
    ck(icon_sizes.get(_i) == (16, 48), f"{_i} 图标 16×48（1×3 格）；实际={icon_sizes.get(_i)}")


def _centers_ok(_name, _rows):
    _p = os.path.join(ROOT, "FreeChem", "Textures", "Items", _name + ".png")
    if not os.path.exists(_p):
        return False
    from PIL import Image
    _im = Image.open(_p).convert("RGBA")
    if _im.size[0] != 16:
        return False
    for _r in range(_rows):
        _y = _r * 16 + 8
        if _y >= _im.size[1] or _im.getpixel((8, _y))[3] == 0:
            return False
    return True


for _i in ("liq_hydrochloric_acid", "liq_sulfuric_acid", "liq_nitric_acid",
           "liq_sodium_hydroxide", "liq_hydrogen_peroxide", "liq_brine",
           "liq_ammonia_water", "liq_calcium_chloride"):
    ck(_centers_ok(_i, 3), f"{_i} 三格中心不透明（否则游戏占格判成 1–2 格）")
for _i in ("beaker", "retort_flask", "erlenmeyer_flask", "test_tube_rack",
           "alcohol_lamp", "spirit_burner"):
    ck(icon_sizes.get(_i) == (16, 32), f"{_i} 图标 16×32；实际={icon_sizes.get(_i)}")
    ck(_centers_ok(_i, 2), f"{_i} 上下两格中心不透明（否则游戏判成 1 格）")
for _k in ("inst_lamp_hint", "inst_wick_hint", "inst_filter_hint", "inst_fuel_hint"):
    ck(_k in _zh_keys and _k in _en_keys, f"P3a 仪器提示键 {_k} zh/en 齐备")
_doc_p3a = ""
for _dp in glob.glob(os.path.join(ROOT, "docs", "*.md")) + [os.path.join(ROOT, "README.md")]:
    if os.path.exists(_dp):
        _doc_p3a += open(_dp, encoding="utf-8", errors="replace").read()
sw(("500ml" in _doc_p3a or "16×48" in _doc_p3a) and "占格" in _doc_p3a,
   "文档记录 P3a 500ml/16×48/占格修复（软断言）")

# ── 26. P3b（第 23 轮）：倒液 owned / 成分仪 RefreshUI / 原版物品替代（独立实现）──
ck("EnsureOwned(" in cat, "ChemCatalog 有 EnsureOwned(")
ck("ChemCatalog.EnsureOwned(" in (_mb(_mach_text, "Finish") or ""),
   "Finish 产出经 EnsureOwned")
ck("ChemCatalog.EnsureOwned(" in _heat_text, "ChemHeating 产出经 EnsureOwned")
ck("SelfCheckLiquidBridge" in (_mb(cat, "Create") or ""),
   "Create 有 SelfCheckLiquidBridge 液体自检")
ck(re.search(r'\[HarmonyPatch\(\s*typeof\(\s*ChemScannerUIManager\s*\)\s*,\s*"RefreshUI"\s*\)\]',
             _patch) is not None,
   "ChemPatches 有 RefreshUI Postfix 补丁")
_ru = _patch.find('"RefreshUI"')
_ru_reg = _patch[_ru:_ru + 4000] if _ru >= 0 else ""
for _tok in ("contaminantTMP", "totalVolumeTMP", "scanner_fc_line", "scanner_fc_grade"):
    ck(_tok in _ru_reg, f"RefreshUI Postfix 含 {_tok}")
ck("RefreshUI(" not in _ru_reg and "RefreshChemMode(" not in _ru_reg,
   "RefreshUI Postfix 不递归调用 RefreshUI/RefreshChemMode")
ck('"RefreshChemMode"' in _patch, "原 RefreshChemMode 补丁保留")
_csp = open(os.path.join(SRC, "FreeChem.csproj"), encoding="utf-8").read()
for _asm in ("Unity.TextMeshPro", "UnityEngine.UI"):
    _ref = re.search(r'<Reference\s+Include="' + re.escape(_asm) + r'">[\s\S]{0,400}?</Reference>', _csp)
    ck(_ref is not None and "<Private>false</Private>" in _ref.group(0),
       f"csproj 引用 {_asm} 且 Private=false")
_vap = re.search(r'VanillaAnalog\s*=\s*new Dictionary<string, string>\s*\{(.*?)\};', text, re.S)
_vm2 = {k: v for k, v in re.findall(r'\{\s*"([^"]+)"\s*,\s*"([a-z0-9_]+)"\s*\}', _vap.group(1))} \
    if _vap else {}
ck("coffee_bean" not in _vm2,
   "VanillaAnalog 不含 coffee_bean（Lead 定案：咖啡改 legacy 无来源）")
# P12：原版酒精改 mini_bottle 组分判定，不再有 VanillaAnalog→liq_ethanol 映射
ck(sum(1 for b in ("small_bottled_water", "bottled_water", "large_bottled_water",
                   "water_jug", "water_pitcher") if _vm2.get(b) == "liq_water") >= 5,
   "VanillaAnalog ≥5 种原版水容器→liq_water")
# P12：VanillaAlcoholIds 仅兼容候选，燃料改按 alcohol 组分判定（不强制含/不含 mini_bottle）
for _i in ("liq_water", "liq_ethanol", "sol_coffee_grounds", "sol_caffeine"):
    ck(_i not in _loot_ids, f"掉落表已移除 legacy 物品 {_i}")
_teq = re.findall(r'new ThermalReaction\(\s*"([^"]+)"\s*,\s*"[^"]*"\s*,\s*"[^"]*"\s*,'
                  r'\s*new\[\]\s*\{[^}]*\}\s*,\s*new\[\]\s*\{([^}]*)\}', _lab_text, re.S)
ck(len(_teq) == 5, f"实时热反应 5 条（P12 删 heat_ethanol_dehydrate；实际 {len(_teq)}）")
ck(not any(_rid == "heat_caffeine_extract" for _rid, _out in _teq),
   "heat_caffeine_extract 已删除")
ck(not any("sol_caffeine" in out or "sol_coffee_grounds" in out for _rid, out in _teq),
   "sol_caffeine/sol_coffee_grounds 无热反应来源（legacy）")
for _tid in ("illicit_voidsalt", "illicit_redmist", "illicit_nightglass", "illicit_ashbreath"):
    ck(any(_tid in r["outputs"] for r in reactions if r["tier"] == 4),
       f"T4 违禁品仍可达: {_tid}")
_doc_p3b = ""
for _dp in glob.glob(os.path.join(ROOT, "docs", "*.md")) + [os.path.join(ROOT, "README.md")]:
    if os.path.exists(_dp):
        _doc_p3b += open(_dp, encoding="utf-8", errors="replace").read()
sw(("P3b" in _doc_p3b or "原版物品替代" in _doc_p3b) and "倒液" in _doc_p3b,
   "文档记录 P3b 倒液/成分仪/原版替代（软断言）")

# ── 27. P3c（第 24 轮）：机器气瓶槽（供气/守恒/无杂质，文本层独立实现）──
ck(re.search(r'List<GameSlotInventory>\s+gasSlots', _mach_text) is not None, "有 gasSlots 气瓶槽列表")
ck(re.search(r'BuildWindow\([^)]*gasSlots', _mach_text) is not None, "BuildWindow 接收 gasSlots")
ck('"ui_gas_slot"' in _mach_text, "机器窗口标签 ui_gas_slot")
ck("GasSlots" in _heat_text, "加热台 GasSlots")
_accg = _strip_comments(_mb(_mach_text, "AcceptsGasCanister") or "")
ck("IsCanisterItem" in _accg and "IsItemOwned" in _accg and "TryGetGasFromCanister" in _accg,
   "AcceptsGasCanister 只收 owned CanisterTag 且可反查气体")
ck(re.search(r'CanisterToGas[\s\S]{0,600}CanisterSkus', text) is not None,
   "CanisterToGas 由 CanisterSkus 反推")
_colg = _strip_comments(_mb(_mach_text, "CollectGasSupply") or "")
ck("Match(" in _colg or "CanisterGrade" in _colg, "CollectGasSupply 匹配才供气/用 CanisterGrade")
_consg = _strip_comments(_mb(_mach_text, "ConsumeGasCanisters") or "")
ck("GasCanistersConsumed(" in _mach_text, "有 GasCanistersConsumed 判定")
ck("Destroy(" in _consg and "gas_canister_empty" in _consg and "CanAccept" in _consg,
   "仅消耗配方气体瓶→输出空瓶，输出满预检保留")
ck("emptyCanistersNeeded" in _mach_text, "CanAcceptOutputs 预检空瓶位")
ck("ClearCanisterContaminants(" in cat and "ClearCanisterContaminants(" in _mach_text,
   "气瓶进槽清零 contam")
ck("ConsumeThermalGas(" in _heat_text, "加热台 ConsumeThermalGas")
ck("ui_gas_slot_tip" in _entry_text, "tooltip 使用 ui_gas_slot_tip")
for _k in ("ui_gas_slot", "ui_gas_slot_tip"):
    ck(_k in _zh_keys and _k in _en_keys, f"气瓶槽本地化键 {_k} zh/en 齐备")

# ── 28. P3d/P3e（第 25 轮）：索引书 / 便条 #97 / 电话商人（文本层独立实现）──
_p5_src = "".join(open(os.path.join(SRC, _f), encoding="utf-8").read()
                   for _f in os.listdir(SRC) if _f.endswith(".cs"))
_vanilla_note = any(_t in _p5_src for _t in ("SpawnPaperItem(", "NOTE_TAG", "TypeHelper.NOTE", "PAPER_TAG"))
ck(len(_item_ids) >= 97 and _item_ids[96] == "note_chem_dealer",
   "数字 ID #97 = note_chem_dealer（只追加）")
ck("note_chem_dealer" in spec_ids, "Specs 注册便条 note_chem_dealer")
ck(icon_sizes.get("note_chem_dealer") == (16, 32),
   "便条图标 16×32；实际=" + str(icon_sizes.get("note_chem_dealer")))
ck("note_chem_dealer" in _loot_ids or _vanilla_note, "便条有来源（mod 掉落或原版 note/paper）")
ck("note_chem_dealer" in (_mb(_entry_text, "OnNewGameStarted") or "") or _vanilla_note, "起始礼包含便条或已改原版便签")
for _sfx in ("_name", "_desc", "_flavor"):
    ck("note_chem_dealer" + _sfx in _zh_keys and "note_chem_dealer" + _sfx in _en_keys,
       f"便条本地化键 note_chem_dealer{_sfx} zh/en 齐备")
_idx_p = os.path.join(ROOT, "FreeChem", "Books", "chem_index.json")
ck(os.path.exists(_idx_p), "存在 Books/chem_index.json")
if os.path.exists(_idx_p):
    import json as _json2
    _ix = _json2.load(open(_idx_p, encoding="utf-8"))
    ck(_ix.get("id") == "chem_index", "chem_index.json id = chem_index")
    _nout = len(_item_ids)
    ck(isinstance(_ix.get("pages"), list) and 1 <= len(_ix["pages"]) <= 10,
       "chem_index.json 页数在 1–10 内")
    _lng = {}
    for _pi, _pg in enumerate(_ix.get("pages", [])):
        _bs = [e for e in _pg.get("elements", [])
               if e.get("type") == "text" and e.get("style") == "body"]
        ck(len(_bs) == 1, f"索引书第 {_pi + 1} 页单列（1 个正文元素）")
        if _bs:
            ck(_bs[0].get("fontSize") in (14, 16), f"索引书第 {_pi + 1} 页 fontSize=14/16")
            ck(_bs[0].get("wrap") is True and _bs[0].get("autoHeight") is True,
               f"索引书第 {_pi + 1} 页 wrap/autoHeight")
        for _el in _pg.get("elements", []):
            for _lg, _tx in (_el.get("translations") or {}).items():
                _lng.setdefault(_lg, []).append(_tx)
    for _lg, _ch in _lng.items():
        _ns = [int(x) for x in re.findall(r"(?m)^\s*(?:ID\s*)?(\d{1,3})[\.\s]", "\n".join(_ch))]
        ck(sorted(_ns) == list(range(1, _nout + 1)), f"chem_index.json[{_lg}] 覆盖 ID 1–{_nout} 各一次")
_dbg = open(os.path.join(SRC, "ChemDebug.cs"), encoding="utf-8").read()
ck("ListAllIds" not in _dbg, "调试面板已移除列 ID 逻辑")
for _t in ("SpawnIndexBook(", "debug_gen_index_book", "debug_list_removed", "MoveItemToBackCounter("):
    ck(_t in _dbg, f"ChemDebug 含索引书/提示实现 {_t}")
ck("CountIndexBooks(" in cat and "BookHelper.BOOK_TAG" in cat and "BookHelper.BOOK_ID" in cat,
   "ChemCatalog.CountIndexBooks 用 BOOK_TAG/BOOK_ID 判唯一")
_phone_p = os.path.join(SRC, "ChemPhone.cs")
_phone = open(_phone_p, encoding="utf-8").read() if os.path.exists(_phone_p) else ""
ck(bool(_phone), "存在 src/ChemPhone.cs（电话商人）")
if _phone:
    ck('typeof(StorePhoneClient)' in _phone and '"InitPhoneClientDict"' in _phone,
       "ChemPhone 注入 StorePhoneClient.InitPhoneClientDict")
    ck("fc_chem_order_pending" in _phone + _entry_text, "电话订购 pending ModData")
    ck("OnShutterOpenedEarly" in _phone + _entry_text, "次晨送达钩子 OnShutterOpenedEarly")
    ck("MoveItemToFrontCounter(" in _phone, "送达投前柜台")
    ck("MoveItemToBackCounter(" in _phone, "前柜台满退后柜台")
    ck("catch" in _strip_comments(_phone), "ChemPhone 有 try/catch")
for _k in ("debug_gen_index_book", "debug_book_exists", "debug_book_ok",
           "debug_book_spawn_fail", "debug_list_removed"):
    ck(_k in _zh_keys and _k in _en_keys, f"索引书按钮/提示键 {_k} zh/en 齐备")

# ── 29. P3f（第 26 轮）：热反应可达/消耗/温区/T4/审计（文本层独立实现）──
_hp2 = _mb(_mach_text, "CreateHeatingPlatform") or ""
ck("new GameGridInventory(2, 2)" in _hp2, "加热台反应槽为 2×2 网格")
ck("mayInventoryAddItemFunc" in _hp2 and "ReagentTag" in _hp2 and "VanillaAnalog.ContainsKey" in _hp2,
   "加热台反应槽接受容器/本模组药剂/原版药剂瓶")
ck(len(re.findall(r'ConsumeThermalInputs\(', _heat_text)) >= 3,
   "ConsumeThermalInputs 定义+成功/超温两处调用")
_cti2 = _strip_comments(_mb(_heat_text, "ConsumeThermalInputs") or "")
ck("Inputs" in _cti2 and "Destroy(" in _cti2, "热反应消耗按配方 Inputs 销毁投料")
ck(re.search(r'CollectThermalInput\([^)]*FuelSlot', _heat_text) is None,
   "燃料槽不再作为热反应投料")
ck("ToggleHighByHeater(" in _heat_text and "ToggleHighByHeater" in _entry_text,
   "ToggleHighByHeater 存在且接入酒精灯点击")
ck("160" in _heat_text and "99" in _heat_text and "r.High = !r.High" in _heat_text,
   "HIGH 160°C / LOW 99°C 切档（r.High 翻转）")
ck(re.search(r'heat_kmno4[\s\S]{0,500}?140f[\s\S]{0,200}?160f[\s\S]{0,200}?190f', _lab_text) is not None,
   "heat_kmno4 温区 140–160、降解 190")
ck(re.search(r'heat_nh4no3[\s\S]{0,500}?150f[\s\S]{0,200}?160f[\s\S]{0,200}?200f', _lab_text) is not None,
   "heat_nh4no3 温区 150–160、降解 200")
_ash2 = next((r for r in reactions if r["id"] == "ashbreath"), None)
ck(_ash2 is not None and {"med_bottle_small_pink", "liq_nitric_acid", "sol_activated_carbon"} <= set(_ash2["inputs"]),
   "ashbreath 输入含原版氨水瓶 med_bottle_small_pink")
_mash = re.search(r'new Reaction\(\s*"ashbreath"[\s\S]{0,600}', text)
ck(_mash is not None and "原版氨水" in _mash.group(0), "ashbreath 方程注明原版氨水")
_vkeys2 = set(_vm2.keys())
for _tid in ("illicit_voidsalt", "illicit_redmist", "illicit_nightglass", "illicit_ashbreath"):
    _t4r = [r for r in reactions if _tid in r["outputs"] and r["tier"] == 4]
    ck(bool(_t4r) and any(i in _vkeys2 for i in _t4r[0]["inputs"]),
       f"T4 至少一个原版输入: {_tid}")
_mh = re.search(r'new Reaction\(\s*"hcl_absorption"[\s\S]{0,300}', text)
_mc = re.search(r'new Reaction\(\s*"salt_crystallize"[\s\S]{0,300}', text)
ck(_mh is not None and "水为溶剂" in _mh.group(0) and "非化学计量" in _mh.group(0),
   "hcl_absorption 标注水为溶剂/非化学计量")
ck(_mc is not None and "母液" in _mc.group(0) and "非化学计量" in _mc.group(0),
   "salt_crystallize 标注母液/非化学计量")
_vl4 = _mb(text, "ValidateLogic") or ""
for _t in ("MachineIds", "SpecValue", "CanisterSkus", "VanillaAnalog", "UsesVanilla", "Tier"):
    ck(_t in _vl4, f"ValidateLogic 覆盖 {_t}")
_labvl2 = _mb(_lab_text, "ValidateLogic") or ""
for _t in ("MinTemp", "MaxTemp", "DegradeTemp", "lowBand", "highBand"):
    ck(_t in _labvl2, f"ChemLab.ValidateLogic 覆盖温区 {_t}")
_ap2 = os.path.join(ROOT, "docs", "chemistry-audit.md")
ck(os.path.exists(_ap2), "存在 docs/chemistry-audit.md")
if os.path.exists(_ap2):
    _at = open(_ap2, encoding="utf-8").read()
    for _kw in ("44", "T4", "气瓶", "步骤", "P3f"):
        ck(_kw in _at, f"chemistry-audit.md 含 {_kw}")

# ── 30. P4a（第 27 轮）：机器窗口官方 MachineHelper + 电力/槽位（文本独立实现）──
for _tok in ("MachineHelper.CreateMachineryWindow(", "MachineHelper.PowerPanel(",
             "MachineHelper.Panel(", "MachineHelper.NotePanel(",
             "MachineHelper.SetupBatterySlot(", "LayoutOf(", "PowerOf("):
    ck(_tok in _mach_text, f"P4a 机器窗口/电力实现 {_tok}")
ck(re.search(r'if\s*\(battery\s*!=\s*null\)\s*MachineHelper\.SetupBatterySlot', _mach_text) is not None,
   "SetupBatterySlot 仅 battery != null 时调用")
ck("bool powered = battery != null;" in _mach_text and "if (powered" in _mach_text,
   "powered 由电池存在决定并保护供电")
ck("new PixelWindow" not in _mach_text, "不再自建 PixelWindow（全部官方 MachineHelper）")
_pow2 = {m.group(1): int(m.group(2))
         for m in re.finditer(r'case\s+"([a-z_]+)":\s*return\s*(\d+);',
                              _mb(_mach_text, "PowerOf") or "")}
for _mid, _pw in (("electrolyzer", 14), ("high_pressure_synth", 16), ("fractionator", 12),
                  ("chem_reactor", 10), ("gas_generator", 6), ("condenser_unit", 8),
                  ("crystallizer", 5), ("canister_filling", 9), ("gas_scrubber", 7),
                  ("filtration_bench", 0), ("decanter", 0), ("titration_bench", 0),
                  ("heating_platform", 0)):
    ck(_pow2.get(_mid) == _pw, f"{_mid} 功率 {_pw}")
_layout2 = {}
for m in re.finditer(r'case\s+"([a-z_]+)":\s*return new MachineLayout\s*\{([^}]*)\};', _mach_text):
    _layout2[m.group(1)] = dict(re.findall(r'(\w+)\s*=\s*([^,]+)', m.group(2)))
for _mid, (_pw, _ins, _mod) in {"decanter": (False, 1, False), "filtration_bench": (False, 1, False),
                                "titration_bench": (False, 2, False), "crystallizer": (True, 2, False),
                                "chem_reactor": (True, 3, True), "electrolyzer": (True, 2, True),
                                "gas_generator": (True, 2, False), "condenser_unit": (True, 2, False),
                                "fractionator": (True, 3, False), "high_pressure_synth": (True, 3, False),
                                "canister_filling": (True, 1, False),
                                "gas_scrubber": (True, 1, False)}.items():
    _f2 = _layout2.get(_mid) or {}
    ck(str(_f2.get("Powered", "false")).lower() == str(_pw).lower()
       and _f2.get("InputSlots") == str(_ins)
       and str(_f2.get("Modules", "false")).lower() == str(_mod).lower(),
       f"LayoutOf {_mid}: Powered={_pw} InputSlots={_ins} Modules={_mod}")
ck(str(_layout2.get("filtration_bench", {}).get("FilterSlot", "false")).lower() == "true",
   "filtration_bench 有 FilterSlot 专用槽")
ck(str(_layout2.get("canister_filling", {}).get("ContainerSlot", "false")).lower() == "true",
   "canister_filling 有 ContainerSlot 专用槽")
_mods2 = sorted(mid for mid, f in _layout2.items()
                if str(f.get("Modules", "false")).lower() == "true")
ck(_mods2 == ["chem_reactor", "electrolyzer"], "模块槽仅 chem_reactor/electrolyzer")
_hp4 = _mb(_mach_text, "CreateHeatingPlatform") or ""
ck("MachineHelper.CreateMachineryWindow(" in _hp4 and "MachineHelper.NotePanel(" in _hp4,
   "加热台窗口官方构建器 + 便签面板")

# ── 31. P4c/P4d（第 27 轮）：容器读档 / 电话注册 / 组分计算（待 features 落盘）──
_src_blob = ""
for _f in os.listdir(SRC):
    if _f.endswith(".cs"):
        _src_blob += open(os.path.join(SRC, _f), encoding="utf-8").read()
for _tok, _d in (("OnGameLoaded", "读档钩子"), ("BridgeVanillaChemData", "读档重建桥接"),
                 ("LIQUID_CONTAINER_TAG", "容器 tag 重建"), ("PhoneClientDict[96686]", "电话注册 96686"),
                 ("Responding", "电话 client"), ("EnsureRegistered", "电话幂等注册"),
                 ("AddLiquidToChemFromList", "分析仪注入组分"), ("ComponentParts", "组分守恒"),
                 ("fc_tox", "毒性标签由组分计算"), ("fc_euph", "欣快标签由组分计算")):
    ck(_tok in _src_blob, f"P4c/P4d 实现 {_d}（{_tok}）")

# ── 输出 ──
print(f"共解析 {len(reactions)} 条反应，执行 {len(checks)} 项断言")
if softs:
    print(f"\n{len(softs)} 项 WARN（不阻断，需人工解释）：")
    for s in softs:
        print("  [WARN] " + s)
if errors:
    print(f"\n发现 {len(errors)} 处逻辑问题：")
    for e in errors: print("  [FAIL] " + e)
    sys.exit(1)
print("逻辑自检全部通过：相态一致、滗析不含气体、灌装只吃气体、多级链可达、原版药剂仅限 T4")
