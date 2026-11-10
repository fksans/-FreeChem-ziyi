#!/usr/bin/env python3
"""
v3 配方生成器（MVP 版）：读 v3/recipes.json，校验后输出 src/Recipes.g.cs。

校验项（加载前报错，错误信息要能直接定位）：
  · 配方 id 重复
  · 引用了 substances 里没有的物质
  · 输入 / 输出系数不是正整数
  · 体积守恒：输出系数之和必须等于输入系数之和
  · 温度档：tMin < tMax；同一配方里的档不能重叠，也不能留缺口（缺口会让配方在两档之间"断掉"）
  · time > 0；conversion 在 (0, 1]
  · minExtent > 0

用法：python3 tools/v3/gen_recipes.py          生成 src/Recipes.g.cs
      python3 tools/v3/gen_recipes.py --check  只校验，不写文件（退出码 0 / 1）
"""
import json
import math
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC_JSON = os.path.join(ROOT, "v3", "recipes.json")
OUT_CS = os.path.join(ROOT, "src", "Recipes.g.cs")
LOC_DIR = os.path.join(ROOT, "FreeChem", "Localization")

# 界面文案：键 -> (中文, 英文)。配方名由配方数据生成（recipe_<id>）。
UI_TEXT = {
    "fc3_tip_header":   ("化学反应：{0}", "Reaction: {0}"),
    "fc3_tip_progress": ("进度 {0}/{1} 秒 {2}", "Progress {0}/{1} s {2}"),
    "metal_sludge_desc":   ("重金属沉淀、过滤后留下的滤渣。含有害金属，不要随手丢弃。", "Filter residue left after heavy metals are precipitated and filtered out. Contains toxic metals — handle with care."),
    "metal_sludge_flavor": ("沉得下去的，总得有人处理。", "What sinks has to be dealt with by someone."),
    "fc3_back_full":    ("柜台已满，无法加入新物品。", "The counter is full — no room for new items."),
    "cfg_hardcore_impurity":     ("杂质偏硬核", "Hardcore impurities"),
    "cfg_hardcore_impurity_tip": ("把原版试剂瓶（酸、碱、还原剂等）的杂质含量放大到 150%（2%–8% 变成 3%–12%）。下次生成的瓶子生效。", "Scales the impurity of vanilla reagent bottles (acid, base, reducing agent…) to 150% (2%–8% becomes 3%–12%). Applies to bottles generated from now on."),
    "fc3_tip_paused":   ("温度不在反应温区，已暂停（进度保留）", "Temperature is outside the reaction window — paused (progress kept)"),
}


R_GAS = 8.314      # J/(mol·K)
T_ROOM = 25.0      # °C
MIN_TIME = 15      # 秒


def band_temp(b):
    """温度档里最接近室温的温度：包含室温的档用室温，高于室温的档用下限，低于室温的档用上限。"""
    return min(max(T_ROOM, b["tMin"]), b["tMax"])


def band_time(r, b):
    """档的反应时间（秒，未乘 timeScale）：手写 time 优先，否则按 Arrhenius 算。"""
    if "time" in b:
        return b["time"]
    k = r["kinetics"]
    t_k = band_temp(b) + 273.15
    ratio = math.exp(k["EaKJ"] * 1000 / R_GAS * (1 / t_k - 1 / (T_ROOM + 273.15)))
    return max(MIN_TIME, k["tau25"] * ratio)


def validate(data):
    errs = []
    subs = data.get("substances", [])
    if len(set(subs)) != len(subs):
        errs.append("substances 里有重复的物质 id")
    sl = data.get("sludge")
    if sl is None:
        if any(r.get("sludge") for r in data.get("recipes", [])):
            errs.append("有配方标了 sludge，但顶层缺 sludge 定义")
    else:
        for k in ("item", "partsPerItem", "nameZh", "nameEn", "value"):
            if not sl.get(k):
                errs.append(f"sludge 定义缺 {k}")
        if sl.get("nameEn") and any("\u4e00" <= ch <= "\u9fff" for ch in sl["nameEn"]):
            errs.append("sludge.nameEn 里不能有中文")
        if sl.get("partsPerItem") is not None and (not isinstance(sl["partsPerItem"], int) or sl["partsPerItem"] <= 0):
            errs.append("sludge.partsPerItem 必须是正整数")
    seen = set()
    for r in data.get("recipes", []):
        rid = r.get("id", "<缺 id>")
        where = f"配方 {rid}"
        if rid in seen:
            errs.append(f"{where}: id 重复")
        if not r.get("name"):
            errs.append(f"{where}: 缺中文名 name")
        ne = r.get("nameEn", "")
        if not ne:
            errs.append(f"{where}: 缺英文名 nameEn")
        elif any("\u4e00" <= ch <= "\u9fff" for ch in ne):
            errs.append(f"{where}: nameEn 里不能有中文")
        seen.add(rid)
        for side in ("inputs", "outputs"):
            d = r.get(side)
            if not d:
                errs.append(f"{where}: 缺 {side}")
                continue
            for k, v in d.items():
                if k not in subs:
                    errs.append(f"{where}: {side} 引用了未登记的物质 '{k}'")
                if not isinstance(v, int) or v <= 0:
                    errs.append(f"{where}: {side}['{k}'] 必须是正整数，实际 {v!r}")
        if r.get("inputs") and r.get("outputs"):
            si, so = sum(r["inputs"].values()), sum(r["outputs"].values())
            if si != so:
                errs.append(f"{where}: 体积不守恒，输入系数和 {si} ≠ 输出系数和 {so}")
        if r.get("sludge") and set(r.get("outputs", {})) != {"debris"}:
            errs.append(f"{where}: sludge 配方的输出只能是 debris（滤渣是靠碎屑记账的）")
        if not isinstance(r.get("minExtent"), int) or r["minExtent"] <= 0:
            errs.append(f"{where}: minExtent 必须是正整数")
        bands = r.get("bands", [])
        if not bands:
            errs.append(f"{where}: 没有温度档")
            continue
        for i, b in enumerate(bands):
            bw = f"{where} 第 {i + 1} 档"
            if not b["tMin"] < b["tMax"]:
                errs.append(f"{bw}: tMin 必须小于 tMax")
            if "time" in b:
                if not b["time"] > 0:
                    errs.append(f"{bw}: time 必须大于 0")
            else:
                k = r.get("kinetics")
                if not k:
                    errs.append(f"{bw}: 没写 time，配方也没有 kinetics")
                else:
                    if not (isinstance(k.get("EaKJ"), (int, float)) and 0 < k["EaKJ"] <= 200):
                        errs.append(f"{bw}: kinetics.EaKJ 必须在 (0, 200] kJ/mol")
                    if not (isinstance(k.get("tau25"), (int, float)) and k["tau25"] > 0):
                        errs.append(f"{bw}: kinetics.tau25 必须大于 0")
            if not 0 < b["conversion"] <= 1:
                errs.append(f"{bw}: conversion 必须在 (0, 1]")
        ordered = sorted(bands, key=lambda b: b["tMin"])
        for a, b in zip(ordered, ordered[1:]):
            if b["tMin"] < a["tMax"]:
                errs.append(f"{where}: 温度档重叠 [{a['tMin']},{a['tMax']}) 与 [{b['tMin']},{b['tMax']})")
            elif b["tMin"] > a["tMax"]:
                errs.append(f"{where}: 温度档有缺口 [{a['tMax']},{b['tMin']})")
    return errs


def cs_dict(d):
    return "new Dictionary<string, int> { " + ", ".join(f'{{ "{k}", {v} }}' for k, v in d.items()) + " }"


def emit(data):
    scale = data.get("timeScale", 1.0)
    lines = [
        "// <auto-generated>",
        "// 由 tools/v3/gen_recipes.py 从 v3/recipes.json 生成。不要手改，改源数据后重新运行生成器。",
        "// </auto-generated>",
        "using System.Collections.Generic;",
        "",
        "namespace FreeChem",
        "{",
        "    public static partial class RecipeTable",
        "    {",
        f'        public const string SludgeItemId = "{data["sludge"]["item"]}";',
        f"        public const int SludgePartsPerItem = {data['sludge']['partsPerItem']};",
        f"        public const int SludgeValue = {data['sludge']['value']};",
        "",
        "        public static readonly Recipe[] All =",
        "        {",
    ]
    for r in data["recipes"]:
        lines.append("            new Recipe(")
        lines.append(f'                "{r["id"]}", "{r["name"]}",')
        lines.append(f"                {cs_dict(r['inputs'])},")
        lines.append(f"                {cs_dict(r['outputs'])},")
        lines.append(f"                {r['minExtent']},")
        lines.append("                new[]")
        lines.append("                {")
        for b in sorted(r["bands"], key=lambda b: b["tMin"]):
            t = max(1, round(band_time(r, b) * scale))
            lines.append(f"                    new Band({b['tMin']}f, {b['tMax']}f, {t}, {b['conversion']}),")
        lines.append("                }" + (", producesSludge: true" if r.get("sludge") else "") + "),")
    lines += ["        };", "    }", "}", ""]
    return "\n".join(lines)


def emit_csv(data, lang):
    idx = 0 if lang == "zh" else 1
    rows = ["# FreeChem v3 localization — key,text（由 tools/v3/gen_recipes.py 生成，不要手改）"]
    def q(t):
        return '"' + t.replace('"', '""') + '"' if any(c in t for c in ',"\n') else t
    for k, v in UI_TEXT.items():
        rows.append(f"{k},{q(v[idx])}")
    for r in data["recipes"]:
        rows.append(f"recipe_{r['id']},{q(r['name'] if lang == 'zh' else r['nameEn'])}")
    return "\n".join(rows) + "\n"


def main():
    with open(SRC_JSON, encoding="utf-8") as f:
        data = json.load(f)
    errs = validate(data)
    for e in errs:
        print("[错误]", e)
    if errs:
        print(f"校验失败：{len(errs)} 个问题")
        return 1
    print(f"校验通过：{len(data['recipes'])} 条配方")
    if "--check" not in sys.argv:
        with open(OUT_CS, "w", encoding="utf-8", newline="\n") as f:
            f.write(emit(data))
        print("已写入", os.path.relpath(OUT_CS, ROOT))
        os.makedirs(LOC_DIR, exist_ok=True)
        for lang in ("zh", "en"):
            with open(os.path.join(LOC_DIR, lang + ".csv"), "w", encoding="utf-8", newline="\n") as f:
                f.write(emit_csv(data, lang))
        print("已写入 FreeChem/Localization/{zh,en}.csv")
    return 0


if __name__ == "__main__":
    sys.exit(main())
