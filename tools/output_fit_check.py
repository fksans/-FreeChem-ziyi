#!/usr/bin/env python3
"""
输出格容量检查（代码审查 3.1 的回归脚本）。

读 src/ChemMachines.cs 的 LayoutOf（输出格宽高）、src/ChemRecipes.cs 的夜间反应、
src/ChemLab.cs 的热反应，以及 FreeChem/Textures/Items/*.png 的尺寸（16px = 1 格），
穷举摆放（不旋转）检查每条配方的产物（含「天平 + 催化剂」奖励多出的一份）能否放进输出格。
放不下的配方在游戏里会一直停在待机（CanAcceptOutputs 返回 false）。

用法：python3 tools/output_fit_check.py   （有放不下的配方时退出码 1）
"""
import os
import re
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
TEX = os.path.join(ROOT, "FreeChem", "Textures", "Items")
HEATING_OUTPUT = (6, 3)   # ChemMachines.CreateHeatingPlatform: new GameGridInventory(6, 3)


def read(name):
    with open(os.path.join(SRC, name), encoding="utf-8-sig") as f:
        return f.read()


def cells(item_id):
    with open(os.path.join(TEX, item_id + ".png"), "rb") as f:
        w, h = struct.unpack(">II", f.read(24)[16:24])
    return (w // 16, h // 16)


def fits(width, height, items):
    grid = [[False] * width for _ in range(height)]
    items = sorted(items, key=lambda s: -s[0] * s[1])

    def place(k):
        if k == len(items):
            return True
        w, h = items[k]
        for y in range(height - h + 1):
            for x in range(width - w + 1):
                if all(not grid[y + j][x + i] for i in range(w) for j in range(h)):
                    for i in range(w):
                        for j in range(h):
                            grid[y + j][x + i] = True
                    if place(k + 1):
                        return True
                    for i in range(w):
                        for j in range(h):
                            grid[y + j][x + i] = False
        return False

    return place(0)


def main():
    mach = read("ChemMachines.cs")
    layouts = {}
    for m in re.finditer(r'case\s+"([a-z_]+)":\s*return new MachineLayout\s*\{([^}]*)\};', mach):
        props = dict(re.findall(r"(\w+)\s*=\s*([^,\s]+)", m.group(2)))
        layouts[m.group(1)] = (int(props["OutputW"]), int(props["OutputH"]))

    rec = read("ChemRecipes.cs")
    problems = []
    checked = 0
    pattern = (r'new Reaction\("(\w+)",[^;]*?"(\w+)",\s*\d,\s*new\[\]\s*\{([^}]*)\},'
               r'\s*new\[\]\s*\{([^}]*)\}([^;]*?)\)')
    for m in re.finditer(pattern, rec, re.S):
        rid, machine, _ins, outs, rest = m.groups()
        outs = re.findall(r'"(\w+)"', outs)
        if machine not in layouts:
            problems.append(f"{rid}: 机器 {machine} 没有 LayoutOf 输出格规格")
            continue
        w, h = layouts[machine]
        variants = [outs] + ([outs + [outs[0]]] if "catalystBonus: true" in rest else [])
        for o in variants:
            checked += 1
            if not fits(w, h, [cells(x) for x in o]):
                problems.append(f"{rid} @ {machine} 输出格 {w}x{h} 放不下 "
                                + ", ".join(f"{x}{cells(x)}" for x in o))

    lab = read("ChemLab.cs")
    for m in re.finditer(r'new ThermalReaction\("(\w+)",.*?new\[\]\s*\{[^}]*\},\s*new\[\]\s*\{([^}]*)\}',
                         lab, re.S):
        rid, outs = m.group(1), re.findall(r'"(\w+)"', m.group(2))
        checked += 1
        if not fits(*HEATING_OUTPUT, [cells(x) for x in outs]):
            problems.append(f"{rid} @ heating_platform 输出格 6x3 放不下 {outs}")

    for p in problems:
        print("[FAIL]", p)
    print(f"检查 {checked} 组产物，{len(problems)} 组放不下")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
