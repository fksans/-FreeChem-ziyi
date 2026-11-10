#!/usr/bin/env python3
"""生成器校验的反例测试：每种配方数据错误都必须报出明确错误。python3 tools/v3/test_gen_recipes.py"""
import copy, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_recipes as g

base = json.load(open(g.SRC_JSON, encoding="utf-8"))
fails = 0

def case(name, mutate, expect):
    global fails
    d = copy.deepcopy(base); mutate(d)
    errs = g.validate(d)
    ok = any(expect in e for e in errs)
    print(("[PASS] " if ok else "[FAIL] ") + name + ("" if ok else f"  (期望含 '{expect}'，实际 {errs})"))
    fails += 0 if ok else 1

r0 = lambda d: d["recipes"][0]
case("正常数据无错误", lambda d: None, "") if False else None
errs = g.validate(base); print(("[PASS] " if not errs else "[FAIL] ") + "正常数据校验通过", errs if errs else ""); fails += 1 if errs else 0
case("配方 id 重复", lambda d: d["recipes"].append(copy.deepcopy(d["recipes"][0])), "id 重复")
case("引用未登记物质", lambda d: r0(d)["inputs"].update({"unobtainium": 1}), "未登记的物质")
case("系数不是正整数", lambda d: r0(d)["inputs"].update({"base": 0}), "必须是正整数")
case("体积不守恒", lambda d: r0(d)["outputs"].update({"debris": 5}), "体积不守恒")
case("温度档重叠", lambda d: r0(d)["bands"][1].update({"tMin": 40}), "温度档重叠")
case("温度档缺口", lambda d: r0(d)["bands"][1].update({"tMin": 50}), "温度档有缺口")
case("tMin >= tMax", lambda d: r0(d)["bands"][0].update({"tMax": 10}), "tMin 必须小于 tMax")
case("time <= 0", lambda d: r0(d)["bands"][0].update({"time": 0}), "time 必须大于 0")
case("conversion 越界", lambda d: r0(d)["bands"][0].update({"conversion": 1.5}), "conversion")
case("没有温度档", lambda d: r0(d).update({"bands": []}), "没有温度档")
case("缺英文名", lambda d: r0(d).update({"nameEn": ""}), "nameEn")
case("英文名含中文", lambda d: r0(d).update({"nameEn": "沉淀"}), "不能有中文")
case("minExtent 非法", lambda d: r0(d).update({"minExtent": 0}), "minExtent")
print("结论：" + ("全部通过" if not fails else f"{fails} 项失败"))
sys.exit(1 if fails else 0)
