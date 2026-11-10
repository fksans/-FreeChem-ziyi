# -*- coding: utf-8 -*-
"""
FreeChem 图标生成器 v2 —— 对齐原版美术风格。

参考原版 CDN 图标（cdn.nicokobo.com/ps/items/*.webp）实测：
  - 网格基数 16px：小物件 16x16 / 16x32，机器 48x48
  - 淡灰白 / 米白机身，1px 深灰描边，低饱和点缀，柔和高光，透明背景
  - 药剂瓶 = 深色瓶盖 + 玻璃瓶身 + 米白标签 + 低饱和色带
  - 机器 = 灰白机箱 + 深色面板 + 细线条结构 + 少量指示灯
透明区域决定物品在背包中的占格（SetSpriteAndShapeFromMod）。

本作新增的是【原版没有的具名基础药剂】：
原版只有抽象的「酸 / 碱 / 水 / 酒精 / 脂合剂 / 还原剂 / 稳定剂 /
N-羟甲基碱 / 氯」等混合物组分，没有 HCl / H2SO4 / HNO3 / NaOH /
H2O2 / Cl2 / H2 / CuSO4 / KMnO4 这类具体化合物。
设备也全部避开原版已有的搅拌机 / 加热板 / 蒸发器 / 成型机 /
光谱仪 / 熔炉 / 净水器。
"""
from PIL import Image, ImageDraw, ImageFont
import os

OUT = r"C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\FreeChem\Textures\Items"
SHEET = r"C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\icon_sheet.png"
os.makedirs(OUT, exist_ok=True)

# ---------- 原版风格调色板 ----------
INK      = (58, 62, 68)
INK2     = (88, 94, 100)
METAL    = (200, 205, 202)
METAL_D  = (150, 158, 158)
METAL_HI = (238, 239, 232)
PANEL    = (112, 120, 122)
PANEL_D  = (78, 86, 90)
GLASS    = (186, 205, 206)
GLASS_HI = (232, 240, 238)
LABEL    = (230, 226, 212)

C_ACID   = (178, 104, 96)
C_ALK    = (108, 132, 168)
C_CHLOR  = (166, 178, 108)
C_GAS    = (126, 166, 172)
C_SULF   = (190, 166, 104)
C_NIT    = (150, 130, 168)
C_COPPER = (106, 158, 168)
C_PEROX  = (176, 190, 176)
C_CARB   = (110, 114, 116)
C_LAMP   = (198, 172, 104)
C_GREEN  = (130, 172, 138)

TRANSPARENT = (0, 0, 0, 0)


def new(w, h): return Image.new("RGBA", (w, h), TRANSPARENT)
def R(d, b, c, o=None): d.rectangle(b, fill=c, outline=o)
def E(d, b, c, o=None): d.ellipse(b, fill=c, outline=o)
def L(d, p, c, w=1): d.line(p, fill=c, width=w)
def P(d, p, c, o=None):
    d.polygon(p, fill=c)
    if o: d.line(list(p) + [p[0]], fill=o, width=1)


def bottle(color, mark="", hazard=False):
    im = new(16, 32); d = ImageDraw.Draw(im)
    R(d, (5, 1, 10, 4), INK)
    R(d, (6, 4, 9, 8), GLASS, INK)
    R(d, (2, 8, 13, 30), GLASS, INK)
    R(d, (3, 15, 12, 28), color)
    L(d, [(3, 15), (12, 15)], GLASS_HI)
    R(d, (4, 10, 5, 27), GLASS_HI)
    R(d, (3, 29, 12, 30), INK2)
    R(d, (4, 17, 11, 23), LABEL, INK)
    if mark:
        try: f = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 6)
        except Exception: f = ImageFont.load_default()
        d.text((5, 17), mark, fill=INK, font=f)
    if hazard:
        P(d, [(10, 24), (14, 24), (12, 20)], C_SULF, INK)
    return im


def crystal(color, hi=None):
    hi = hi or tuple(min(255, c + 42) for c in color)
    im = new(16, 16); d = ImageDraw.Draw(im)
    E(d, (1, 12, 14, 15), (120, 124, 124, 160))
    P(d, [(2, 13), (4, 5), (8, 2), (10, 13)], color, INK)
    P(d, [(8, 2), (10, 13), (2, 13)], hi)
    P(d, [(8, 13), (11, 6), (14, 4), (14, 13)], hi, INK)
    P(d, [(14, 4), (14, 13), (8, 13)], color)
    L(d, [(8, 2), (4, 5)], GLASS_HI)
    L(d, [(11, 6), (14, 4)], GLASS_HI)
    return im


def powder(color):
    im = new(16, 16); d = ImageDraw.Draw(im)
    R(d, (4, 1, 11, 4), INK)
    R(d, (5, 4, 10, 6), METAL_D, INK)
    R(d, (2, 6, 13, 14), METAL, INK)
    R(d, (3, 9, 12, 13), color)
    L(d, [(3, 9), (12, 9)], GLASS_HI)
    R(d, (4, 7, 5, 8), GLASS_HI)
    R(d, (3, 14, 12, 14), INK2)
    R(d, (5, 9, 10, 11), LABEL, INK)
    return im


def cylinder(color, mark="", hazard=False):
    """便携式高压钢瓶 32×48（2×3 格）。精绘：阀门手轮、肩弧、加固箍、
    玻璃高光、色带、化学式铭牌、底部护圈、危险菱形。"""
    im = new(32, 48); d = ImageDraw.Draw(im)
    lo = tuple(max(0, c - 42) for c in color)
    hi = tuple(min(255, c + 34) for c in color)
    # 阀门组
    R(d, (13, 0, 19, 3), INK2, INK)                 # 阀门接口
    E(d, (10, 0, 22, 6), METAL_D, INK)              # 手轮
    E(d, (13, 2, 19, 5), METAL, INK)
    R(d, (14, 5, 18, 9), METAL_D, INK)              # 阀颈
    # 肩部圆弧 + 瓶身
    E(d, (5, 6, 27, 20), METAL, INK)
    R(d, (5, 13, 27, 42), METAL, INK)
    E(d, (5, 34, 27, 46), METAL_D, INK)
    # 色带（气体种类）
    R(d, (6, 20, 26, 30), color)
    R(d, (6, 20, 26, 22), hi)
    R(d, (6, 28, 26, 30), lo)
    # 加固箍
    R(d, (5, 17, 27, 19), METAL_D, INK)
    R(d, (5, 31, 27, 33), METAL_D, INK)
    # 高光与反光
    R(d, (8, 12, 9, 40), GLASS_HI)
    R(d, (11, 12, 11, 18), GLASS_HI)
    R(d, (23, 22, 24, 40), (238, 239, 232, 150))
    # 铭牌
    R(d, (9, 23, 23, 29), LABEL, INK)
    L(d, [(9, 23), (23, 23)], GLASS_HI)
    if mark:
        try: f = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 7)
        except Exception: f = ImageFont.load_default()
        d.text((11, 23), mark, fill=INK, font=f)
    # 底部护圈
    R(d, (4, 43, 28, 46), INK2, INK)
    R(d, (7, 44, 25, 45), METAL_D)
    if hazard:
        P(d, [(24, 12), (30, 12), (27, 7)], C_SULF, INK)
        d.text((26, 10), "!", fill=INK, font=ImageFont.load_default())
    # 铆钉
    for (x, y) in [(7, 18), (25, 18), (7, 32), (25, 32)]:
        E(d, (x - 1, y - 1, x + 1, y + 1), INK2)
    return im


def gas_drum(color, mark="", filled=True, hazard=False):
    """大型储气桶 48×64（3×4 格），造型参考原版水罐 pitcher。
    立式桶体 + 三道加固箍 + 顶部阀门组 + 液位窗 + 大铭牌 + 底座支脚。
    与便携钢瓶明确区分：更宽、有液位窗与支脚、占 3×4 格。"""
    im = new(48, 64); d = ImageDraw.Draw(im)
    lo = tuple(max(0, c - 42) for c in color)
    hi = tuple(min(255, c + 34) for c in color)
    # 底座支脚
    R(d, (6, 58, 14, 63), INK2, INK)
    R(d, (34, 58, 42, 63), INK2, INK)
    R(d, (8, 56, 40, 59), METAL_D, INK)
    # 桶身
    E(d, (4, 8, 44, 24), METAL, INK)
    R(d, (4, 16, 44, 56), METAL, INK)
    E(d, (4, 48, 44, 62), METAL_D, INK)
    # 内容液位（装载态才显示）
    if filled:
        R(d, (5, 28, 43, 54), color)
        R(d, (5, 28, 43, 30), hi)
        L(d, [(5, 28), (43, 28)], GLASS_HI)
        R(d, (5, 52, 43, 54), lo)
    # 三道加固箍
    for y in (18, 33, 49):
        R(d, (4, y, 44, y + 3), METAL_D, INK)
        L(d, [(5, y), (43, y)], METAL_HI)
    # 玻璃高光
    R(d, (8, 18, 9, 54), GLASS_HI)
    R(d, (11, 20, 11, 26), GLASS_HI)
    R(d, (39, 24, 40, 54), (238, 239, 232, 140))
    # 顶部阀门组
    R(d, (20, 0, 28, 4), INK2, INK)
    E(d, (16, 2, 32, 11), METAL_D, INK)
    E(d, (20, 4, 28, 9), METAL, INK)
    R(d, (22, 9, 26, 14), METAL_D, INK)
    L(d, [(34, 6), (43, 6), (43, 12)], INK, 2)      # 出气管
    E(d, (38, 10, 46, 18), LABEL, INK)              # 压力表
    L(d, [(42, 14), (44, 11)], C_ACID)
    # 液位观察窗
    R(d, (12, 36, 20, 46), GLASS, INK)
    if filled: R(d, (13, 40, 19, 45), color)
    L(d, [(13, 40), (19, 40)], GLASS_HI)
    # 大铭牌
    R(d, (23, 35, 40, 46), LABEL, INK)
    L(d, [(23, 35), (40, 35)], GLASS_HI)
    if mark:
        try: f = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 9)
        except Exception: f = ImageFont.load_default()
        d.text((25, 38), mark, fill=INK, font=f)
    if hazard:
        P(d, [(33, 24), (41, 24), (37, 18)], C_SULF, INK)
    for (x, y) in [(7, 19), (41, 19), (7, 34), (41, 34), (7, 50), (41, 50)]:
        E(d, (x - 1, y - 1, x + 1, y + 1), INK2)
    return im


def machine(kind, color=C_GAS, active=True):
    """机器 48×48。active=True 工作态（彩色液体/亮灯/气泡）；False 空闲态
    （空容器、指示灯熄灭），对应「合成反应釜内没有试剂时不显示红色液体」。"""
    im = new(48, 48); d = ImageDraw.Draw(im)
    liquid = color if active else GLASS          # 空闲态容器是空玻璃
    lamp1 = C_GREEN if active else INK2
    lamp2 = C_LAMP if active else INK2
    R(d, (3, 6, 44, 43), METAL, INK)
    R(d, (3, 6, 44, 10), METAL_D, INK)
    R(d, (3, 39, 44, 43), INK2)
    R(d, (5, 12, 42, 37), PANEL, INK)
    R(d, (6, 13, 41, 20), PANEL_D, INK)
    E(d, (7, 7, 10, 10), lamp1, INK)
    E(d, (13, 7, 16, 10), lamp2, INK)

    if kind == "reactor":
        E(d, (12, 15, 36, 39), GLASS, INK)
        if active: E(d, (15, 24, 33, 37), color)
        L(d, [(15, 24), (33, 24)], GLASS_HI if active else INK2)
        R(d, (22, 8, 25, 22), METAL_D, INK)
        R(d, (18, 7, 29, 9), METAL_HI, INK)
        L(d, [(36, 22), (42, 22), (42, 32)], INK, 2)
        if active:
            for (x, y) in [(19, 28), (25, 31), (30, 27)]:
                E(d, (x-1, y-1, x+1, y+1), GLASS_HI)
    elif kind == "electrolyzer":
        R(d, (9, 20, 39, 37), GLASS, INK)
        if active: R(d, (10, 26, 38, 36), color)
        R(d, (15, 12, 17, 33), METAL_HI, INK)
        R(d, (31, 12, 33, 33), METAL_D, INK)
        R(d, (13, 9, 19, 12), C_ACID if active else METAL_D, INK)
        R(d, (29, 9, 35, 12), C_ALK if active else METAL_D, INK)
        if active:
            for (x, y) in [(21, 28), (26, 31), (23, 33), (29, 27)]:
                E(d, (x-1, y-1, x+1, y+1), GLASS_HI)
    elif kind == "gasgen":
        P(d, [(16, 20), (11, 37), (31, 37), (26, 20)], GLASS, INK)
        if active: P(d, [(13, 37), (15, 30), (27, 30), (29, 37)], color)
        R(d, (19, 12, 23, 20), GLASS, INK)
        L(d, [(23, 16), (33, 16), (33, 24)], INK, 2)
        R(d, (30, 24, 39, 38), METAL, INK)
        if active: R(d, (31, 29, 38, 36), C_GAS)
    elif kind == "condenser":
        for i in range(5):
            x = 9 + i * 7
            R(d, (x, 15, x + 3, 33), GLASS, INK)
            L(d, [(x, 15), (x, 33)], GLASS_HI)
        R(d, (6, 12, 42, 15), METAL_D, INK)
        R(d, (6, 33, 42, 36), METAL_D, INK)
        if active: E(d, (22, 36, 26, 40), C_GAS, INK)
    elif kind == "crystallizer":
        R(d, (8, 28, 40, 37), GLASS, INK)
        if active:
            R(d, (9, 32, 39, 36), color)
            for (bx, by, s) in [(14, 26, 4), (24, 23, 5), (34, 27, 3)]:
                P(d, [(bx, by+s), (bx-s, by), (bx, by-s), (bx+s, by)], GLASS_HI, INK)
        L(d, [(9, 32), (39, 32)], METAL_HI)
    elif kind == "liquefier":
        E(d, (9, 16, 29, 39), METAL, INK)
        if active: E(d, (11, 20, 27, 37), color)
        E(d, (30, 11, 41, 22), LABEL, INK)
        L(d, [(35, 17), (39, 13)], C_ACID if active else INK2)
        R(d, (9, 9, 12, 17), METAL_D, INK)
    elif kind == "fractionator":
        R(d, (17, 8, 30, 38), GLASS, INK)
        for y in (14, 19, 24, 29, 34):
            L(d, [(18, y), (29, y)], INK2)
        if active: R(d, (18, 25, 29, 37), color)
        L(d, [(30, 12), (39, 12), (39, 22)], INK, 2)
        R(d, (36, 22, 43, 38), METAL, INK)
    elif kind == "titration":
        R(d, (8, 34, 40, 37), METAL_D, INK)
        R(d, (12, 10, 14, 34), METAL_HI, INK)
        R(d, (19, 12, 22, 28), GLASS, INK)
        if active: R(d, (20, 18, 21, 27), C_ACID)
        E(d, (19, 28, 22, 30), GLASS_HI)
        P(d, [(20, 30), (15, 37), (29, 37), (24, 30)], GLASS, INK)
        if active: P(d, [(17, 37), (19, 33), (25, 33), (27, 37)], C_ALK)
    elif kind == "filling":        # 灌装台：立罐 + 压力表 + 三档纯度阀
        R(d, (8, 14, 22, 38), METAL, INK)
        E(d, (8, 12, 22, 18), METAL, INK)
        if active: R(d, (10, 22, 20, 36), C_GAS)
        R(d, (11, 15, 12, 36), GLASS_HI)
        R(d, (26, 9, 40, 14), METAL_D, INK)
        E(d, (28, 16, 38, 26), LABEL, INK)
        L(d, [(33, 21), (36, 18)], C_ACID if active else INK2, 2)
        for i, c in enumerate((C_SULF, C_ALK, GLASS_HI)):
            R(d, (26, 29 + i * 3, 32, 30 + i * 3), c, INK)
    elif kind == "decanter":       # 滗析槽：静置沉降，上层清液 / 下层沉淀分层
        R(d, (10, 8, 38, 39), GLASS, INK)
        if active:
            R(d, (11, 14, 37, 32), C_GAS)      # 上层清液
            L(d, [(11, 14), (37, 14)], GLASS_HI)
            R(d, (11, 32, 37, 37), C_CARB)     # 下层沉淀
            L(d, [(11, 32), (37, 32)], INK2)
        R(d, (11, 10, 12, 37), GLASS_HI)
        R(d, (22, 4, 26, 8), METAL_D, INK)     # 导流嘴
        L(d, [(38, 16), (42, 16), (42, 24)], INK, 2)  # 滗出管
        E(d, (6, 6, 9, 9), C_LAMP if active else INK2, INK)
    elif kind == "scrubber":       # 气体洗涤塔：进气 + 洗涤液鼓泡 + 出气
        R(d, (14, 8, 34, 38), METAL, INK)
        if active:
            R(d, (15, 20, 33, 36), C_GAS)      # 洗涤液
            L(d, [(15, 20), (33, 20)], GLASS_HI)
        R(d, (22, 4, 26, 9), METAL_D, INK)     # 出气口
        R(d, (22, 12, 26, 14), LABEL, INK)
        L(d, [(5, 28), (15, 28), (15, 32)], INK, 2)   # 进气管插入液面下
        L(d, [(34, 10), (42, 10)], INK, 2)
        if active:
            for (x, y) in [(19, 26), (24, 29), (29, 25), (27, 33)]:
                E(d, (x-1, y-1, x+1, y+1), GLASS_HI)
        E(d, (6, 6, 9, 9), C_GREEN if active else INK2, INK)
    return im


def instrument(kind):
    im = new(32, 32); d = ImageDraw.Draw(im)
    if kind == "balance":
        R(d, (5, 26, 27, 29), METAL_D, INK)
        R(d, (15, 9, 17, 26), METAL_HI, INK)
        L(d, [(5, 12), (27, 12)], METAL_HI, 2)
        E(d, (14, 9, 18, 13), METAL, INK)
        for cx in (8, 24):
            L(d, [(cx, 12), (cx, 17)], INK)
            P(d, [(cx-5, 17), (cx+5, 17), (cx+3, 21), (cx-3, 21)], METAL, INK)
    elif kind == "ph_meter":
        R(d, (4, 7, 27, 26), METAL, INK)
        R(d, (6, 9, 25, 16), PANEL_D, INK)
        try: f = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 8)
        except Exception: f = ImageFont.load_default()
        d.text((8, 9), "pH 7", fill=C_GREEN, font=f)
        R(d, (8, 19, 22, 22), PANEL, INK)
        L(d, [(15, 26), (15, 30)], METAL_D)
        R(d, (6, 3, 10, 6), C_ACID, INK)
    elif kind == "thermometer":    # 温度计：米白标尺板 + 玻璃管 + 低饱和红水银柱 + 刻度
        R(d, (9, 3, 23, 29), METAL, INK)          # 米白标尺板
        R(d, (13, 5, 19, 30), GLASS, INK)         # 玻璃管
        R(d, (15, 12, 17, 28), C_ACID)            # 水银柱
        E(d, (13, 26, 19, 31), C_ACID, INK)       # 球泡
        R(d, (12, 1, 20, 4), METAL_D, INK)        # 顶部金属帽
        for y in (6, 9, 12, 15, 18, 21):          # 左右刻度
            L(d, [(10, y), (12, y)], INK2)
            L(d, [(20, y), (22, y)], INK2)
        R(d, (14, 6, 15, 25), GLASS_HI)           # 玻璃高光
        R(d, (10, 5, 11, 27), METAL_HI)           # 标尺板柔和高光
    elif kind == "goggles":
        R(d, (3, 11, 15, 21), GLASS, INK)
        R(d, (17, 11, 29, 21), GLASS, INK)
        R(d, (15, 14, 17, 16), INK)
        L(d, [(3, 12), (1, 11)], INK, 2); L(d, [(29, 12), (31, 11)], INK, 2)
        R(d, (5, 13, 6, 18), GLASS_HI); R(d, (19, 13, 20, 18), GLASS_HI)
    elif kind == "canister":
        R(d, (13, 2, 15, 6), INK2)
        R(d, (10, 5, 18, 8), METAL_D, INK)
        R(d, (9, 8, 19, 29), METAL, INK)
        E(d, (9, 7, 19, 12), METAL, INK)
        R(d, (9, 15, 19, 20), METAL_D)
        R(d, (11, 9, 12, 26), GLASS_HI)
    elif kind == "catalyst":
        R(d, (6, 10, 25, 27), METAL, INK)
        R(d, (6, 10, 25, 13), METAL_D, INK)
        for (x, y, c) in [(10, 17, C_SULF), (16, 20, C_COPPER), (21, 16, C_NIT)]:
            E(d, (x-2, y-2, x+2, y+2), c, INK)
        R(d, (9, 23, 22, 25), LABEL, INK)
    return im


def graded_canister(color, mark, grade):
    """气罐商品（便携钢瓶 32×48）：纯度用铭牌边的色环条数区分
    （工业 1 条 / 医用 2 条 / 化学纯 3 条）。"""
    im = cylinder(color, mark, hazard=(grade == 0 and mark in ("Cl2", "H2")))
    d = ImageDraw.Draw(im)
    c = (C_SULF, C_ALK, GLASS_HI)[grade]
    for i in range(grade + 1):
        R(d, (6, 33 + i * 2, 26, 34 + i * 2), c)
    R(d, (9, 22, 23, 22), c)
    return im


def illicit(kind):
    """虚构违禁品：小瓶 / 晶体，深色标签 + 警示条（沿用原版低饱和风格）。"""
    im = new(16, 16); d = ImageDraw.Draw(im)
    if kind == "voidsalt":       # 空盐 · 强成瘾结晶
        P(d, [(2, 13), (5, 4), (9, 2), (12, 13)], C_NIT, INK)
        P(d, [(9, 2), (12, 13), (2, 13)], (186, 168, 202))
        R(d, (3, 9, 11, 11), LABEL, INK)
        R(d, (4, 10, 10, 10), C_NIT)
    elif kind == "redmist":      # 红雾 · 剧毒气雾瓶
        R(d, (4, 2, 11, 4), INK)
        R(d, (3, 4, 12, 14), GLASS, INK)
        R(d, (4, 7, 11, 13), C_ACID)
        R(d, (4, 9, 11, 10), LABEL, INK)
        R(d, (4, 5, 5, 12), GLASS_HI)
    elif kind == "nightglass":   # 夜玻 · 致幻片
        E(d, (2, 3, 13, 14), METAL_D, INK)
        E(d, (4, 5, 11, 12), C_NIT, INK)
        L(d, [(5, 8), (10, 8)], GLASS_HI)
    else:                        # 灰息 · 致命粉末
        R(d, (3, 5, 12, 14), METAL, INK)
        R(d, (4, 8, 11, 13), C_CARB)
        R(d, (4, 7, 11, 8), LABEL, INK)
        R(d, (5, 2, 10, 4), INK)
    return im


def labware(kind):
    """实验器材，32x32（烧杯/瓶类）或 16x16（耗材）。对齐原版淡灰白玻璃质感。"""
    if kind in ("filter_paper", "filter_paper_fine", "filter_paper_ash"):
        im = new(16, 16); d = ImageDraw.Draw(im)
        col = {"filter_paper": LABEL, "filter_paper_fine": GLASS_HI, "filter_paper_ash": METAL}[kind]
        E(d, (2, 3, 13, 14), col, INK)
        E(d, (4, 5, 11, 12), (36, 42, 52), INK)
        L(d, [(4, 8), (11, 8)], INK2)
        if kind == "filter_paper_fine":
            for (x, y) in [(6, 7), (9, 9), (7, 10)]: R(d, (x, y, x, y), INK2)
        if kind == "filter_paper_ash":
            L(d, [(5, 6), (10, 11)], INK2); L(d, [(10, 6), (5, 11)], INK2)
        return im
    if kind in ("wick_cotton", "wick_ceramic"):
        im = new(16, 16); d = ImageDraw.Draw(im)
        col = LABEL if kind == "wick_cotton" else METAL
        R(d, (5, 2, 10, 13), col, INK)
        R(d, (6, 3, 9, 6), GLASS_HI)
        L(d, [(7, 3), (7, 12)], INK2); L(d, [(9, 3), (9, 12)], INK2)
        R(d, (6, 13, 9, 14), INK2)
        return im
    im = new(32, 32); d = ImageDraw.Draw(im)
    if kind == "beaker":                 # 烧杯：敞口 + 刻度 + 液体
        R(d, (6, 8, 25, 28), GLASS, INK)
        R(d, (7, 16, 24, 27), C_GAS)
        L(d, [(7, 16), (24, 16)], GLASS_HI)
        L(d, [(5, 8), (26, 8)], INK, 2)
        for y in (12, 18, 22): L(d, [(8, y), (12, y)], INK2)
        R(d, (8, 10, 9, 26), GLASS_HI)
        P(d, [(24, 9), (28, 7), (26, 11)], GLASS, INK)   # 倾倒嘴
    elif kind == "retort_flask":         # 鹅颈瓶：圆肚 + 长弯颈
        E(d, (7, 13, 24, 29), GLASS, INK)
        E(d, (9, 20, 22, 28), C_GAS)
        L(d, [(9, 20), (22, 20)], GLASS_HI)
        R(d, (15, 4, 19, 14), GLASS, INK)
        P(d, [(19, 5), (28, 3), (28, 7), (19, 9)], GLASS, INK)  # 弯颈
        R(d, (9, 15, 10, 26), GLASS_HI)
        R(d, (14, 29, 20, 30), INK2)
    elif kind == "erlenmeyer_flask":     # 锥形瓶
        P(d, [(14, 6), (18, 6), (27, 27), (5, 27)], GLASS, INK)
        P(d, [(9, 27), (12, 18), (20, 18), (23, 27)], C_GAS)
        L(d, [(12, 18), (20, 18)], GLASS_HI)
        R(d, (13, 4, 19, 7), INK)
        L(d, [(14, 8), (14, 14)], GLASS_HI)
    elif kind == "test_tube_rack":       # 试管架
        R(d, (3, 20, 29, 27), METAL, INK)
        R(d, (3, 12, 29, 15), METAL_D, INK)
        for x in (7, 14, 21):
            R(d, (x, 6, x + 4, 24), GLASS, INK)
            R(d, (x + 1, 17, x + 3, 23), C_ACID)
            L(d, [(x + 1, 7), (x + 1, 22)], GLASS_HI)
    elif kind == "alcohol_lamp":         # 酒精灯：玻璃壶 + 灯芯管 + 火焰
        E(d, (7, 15, 25, 29), GLASS, INK)
        E(d, (9, 21, 23, 28), (196, 206, 190))
        R(d, (13, 10, 19, 16), METAL_D, INK)
        R(d, (14, 7, 18, 11), LABEL, INK)
        P(d, [(16, 1), (19, 6), (16, 8), (13, 6)], C_SULF, INK)
        P(d, [(16, 3), (17, 6), (16, 7), (15, 6)], C_LAMP)
        R(d, (9, 17, 10, 26), GLASS_HI)
    elif kind == "spirit_burner":        # 酒精喷灯：金属罐 + 喷嘴 + 侧焰
        R(d, (7, 14, 25, 29), METAL, INK)
        E(d, (7, 12, 25, 18), METAL, INK)
        R(d, (8, 20, 24, 27), C_SULF)
        R(d, (14, 8, 18, 13), METAL_D, INK)
        L(d, [(18, 10), (26, 7)], INK, 2)
        P(d, [(26, 7), (30, 3), (28, 9), (25, 10)], C_LAMP, INK)
    return im


# 注：原 liq_ethanol / liq_water 已移除（用户要求），改用原版酒精瓶/水容器；
#     对应图标条目已删，保留此说明避免误加回。
def bottle_500(color, mark="", hazard=False):
    """液体试剂瓶 16x48（500 ml，1x3 格）：细长瓶身 + 瓶盖 + 标签 + 液面。
    注意：必须让 3 个 16px 格子的中心（y=8/24/40）与画布上下缘（y=0 / y=47）都有不透明像素，
    否则游戏按不透明像素推导占格时会判成 1~2 格。"""
    im = new(16, 48); d = ImageDraw.Draw(im)
    R(d, (5, 0, 10, 4), INK)                 # 瓶盖（贴顶）
    R(d, (6, 4, 9, 10), GLASS, INK)          # 瓶颈（y=8 处为玻璃）
    R(d, (2, 9, 13, 46), GLASS, INK)         # 瓶身
    R(d, (3, 18, 12, 45), color)             # 液体（y=24 / y=40 均在不透明区域内）
    L(d, [(3, 18), (12, 18)], GLASS_HI)      # 液面高光
    R(d, (3, 26, 12, 36), LABEL, INK)        # 标签（跨 2/3 格）
    L(d, [(5, 29), (10, 29)], INK2)
    R(d, (6, 31, 9, 33), (60, 66, 72) if hazard else (110, 130, 132))
    L(d, [(5, 33), (10, 33)], INK2)
    R(d, (3, 10, 4, 44), GLASS_HI)           # 瓶身高光
    R(d, (4, 46, 11, 47), INK2)              # 瓶底（贴底）
    return im


def instrument_small(kind):
    """仪器小件 16x16（占 1 格）：与 32x32 版同一套调色板 / 1px 描边，压缩到 16px 网格。"""
    im = new(16, 16); d = ImageDraw.Draw(im)
    if kind == "thermometer":     # 米白标尺板 + 玻璃管 + 红水银 + 刻度
        R(d, (4, 1, 11, 14), METAL, INK)
        R(d, (6, 2, 9, 14), GLASS, INK)
        R(d, (7, 7, 8, 13), C_ACID)
        E(d, (6, 12, 9, 15), C_ACID, INK)
        R(d, (5, 0, 10, 2), METAL_D, INK)
        for y in (3, 5, 7, 9):
            L(d, [(4, y), (5, y)], INK2); L(d, [(10, y), (11, y)], INK2)
        R(d, (5, 2, 5, 12), METAL_HI)
    elif kind == "ph_meter":      # 手持 pH 计：机身 + 屏幕 + 按钮 + 探头
        R(d, (1, 2, 13, 13), METAL, INK)
        R(d, (2, 3, 12, 8), PANEL_D, INK)
        R(d, (4, 5, 10, 6), C_GREEN)          # 屏幕读数条
        R(d, (3, 10, 7, 12), PANEL, INK)
        R(d, (9, 10, 11, 12), C_ALK, INK)
        L(d, [(7, 13), (7, 15)], METAL_D)
        R(d, (2, 0, 5, 2), C_ACID, INK)
    elif kind == "balance":       # 精密天平：底座 + 立柱 + 横梁 + 两个秤盘
        R(d, (1, 13, 14, 15), METAL_D, INK)
        R(d, (7, 4, 8, 13), METAL_HI, INK)
        L(d, [(1, 6), (14, 6)], METAL_HI, 2)
        E(d, (6, 2, 9, 5), METAL, INK)
        for cx in (3, 12):
            L(d, [(cx, 6), (cx, 9)], INK)
            R(d, (cx - 2, 9, cx + 2, 11), METAL, INK)
    elif kind == "goggles":       # 护目镜：两片镜片 + 鼻梁 + 侧带
        R(d, (1, 6, 7, 11), GLASS, INK)
        R(d, (8, 6, 14, 11), GLASS, INK)
        R(d, (7, 8, 8, 9), INK)
        L(d, [(0, 7), (1, 6)], INK, 2); L(d, [(14, 6), (15, 7)], INK, 2)
        R(d, (2, 7, 3, 9), GLASS_HI); R(d, (9, 7, 10, 9), GLASS_HI)
    elif kind == "catalyst":      # 催化剂套件：金属盒 + 三色催化剂 + 标签
        R(d, (2, 5, 13, 13), METAL, INK)
        R(d, (2, 5, 13, 7), METAL_D, INK)
        for (x, y, c) in [(4, 10, C_SULF), (7, 11, C_COPPER), (10, 10, C_NIT)]:
            E(d, (x - 1, y - 1, x + 1, y + 1), c, INK)
        R(d, (4, 8, 11, 9), LABEL, INK)
    return im


def labware_tall(kind):
    """反应容器 / 加热器 16x32（占 1x2 格）：窄长的玻璃器皿与灯具造型。"""
    im = new(16, 32); d = ImageDraw.Draw(im)
    if kind == "beaker":              # 烧杯：敞口 + 刻度 + 液体
        R(d, (3, 2, 12, 30), GLASS, INK)
        R(d, (2, 0, 13, 2), GLASS, INK)      # 500ml 瓶口
        R(d, (4, 16, 11, 29), C_GAS)
        L(d, [(4, 16), (11, 16)], GLASS_HI)
        L(d, [(3, 6), (12, 6)], INK, 2)
        for y in (12, 15, 18, 22): L(d, [(4, y), (7, y)], INK2)
        R(d, (2, 30, 13, 31), METAL_D, INK)      # 底座（贴底）
        R(d, (4, 10, 5, 25), GLASS_HI)
        P(d, [(11, 9), (14, 7), (12, 11)], GLASS, INK)     # 倾倒嘴
    elif kind == "retort_flask":      # 鹅颈瓶：圆肚 + 长弯颈
        E(d, (2, 14, 13, 29), GLASS, INK)
        E(d, (3, 21, 12, 28), C_GAS)
        L(d, [(3, 21), (12, 21)], GLASS_HI)
        R(d, (7, 0, 10, 15), GLASS, INK)
        P(d, [(10, 4), (14, 2), (14, 6), (10, 9)], GLASS, INK)
        R(d, (3, 17, 4, 26), GLASS_HI)
        R(d, (4, 30, 11, 31), METAL_D, INK)      # 底座（贴底）
    elif kind == "erlenmeyer_flask":  # 锥形瓶
        P(d, [(7, 5), (10, 5), (14, 29), (2, 29)], GLASS, INK)
        P(d, [(4, 27), (6, 18), (10, 18), (12, 27)], C_GAS)
        L(d, [(6, 18), (10, 18)], GLASS_HI)
        R(d, (6, 0, 11, 5), INK)                # 瓶口（贴顶）
        R(d, (2, 30, 13, 31), METAL_D, INK)
        L(d, [(5, 9), (3, 22)], GLASS_HI)
    elif kind == "test_tube_rack":    # 试管架：横梁 + 底座 + 三支试管
        R(d, (1, 20, 14, 31), METAL, INK)
        R(d, (1, 13, 14, 16), METAL_D, INK)
        R(d, (2, 20, 13, 22), METAL_D)
        for x in (2, 6, 10):
            R(d, (x, 0, x + 3, 24), GLASS, INK)
            R(d, (x + 1, 18, x + 2, 23), C_ACID)
            L(d, [(x + 1, 5), (x + 1, 17)], GLASS_HI)
    elif kind == "alcohol_lamp":      # 酒精灯：玻璃壶 + 灯芯管 + 火焰
        E(d, (2, 16, 13, 29), GLASS, INK)
        E(d, (3, 22, 12, 28), (196, 206, 190))
        R(d, (6, 11, 10, 17), METAL_D, INK)
        R(d, (7, 8, 9, 12), LABEL, INK)
        P(d, [(8, 0), (10, 5), (8, 7), (6, 5)], C_SULF, INK)
        P(d, [(8, 3), (9, 5), (8, 6), (7, 5)], C_LAMP)
        R(d, (3, 18, 4, 27), GLASS_HI)
        R(d, (4, 30, 11, 31), METAL_D, INK)      # 底座（贴底）
    elif kind == "spirit_burner":     # 酒精喷灯：金属罐 + 喷嘴 + 侧焰
        R(d, (3, 14, 13, 30), METAL, INK)
        E(d, (3, 12, 13, 17), METAL, INK)
        R(d, (4, 20, 12, 29), C_SULF)
        R(d, (5, 21, 6, 26), METAL_HI)
        R(d, (6, 0, 10, 13), METAL_D, INK)      # 喷口延伸到顶
        L(d, [(10, 9), (14, 6)], INK, 2)
        P(d, [(13, 6), (15, 1), (15, 8), (12, 9)], C_LAMP, INK)
        R(d, (3, 30, 13, 31), METAL_D, INK)      # 底座（贴底）
    return im


def label_printer():
    """标签打印机 32x32（2x2 格）：淡灰白机身 + 出纸口 + 标签纸 + 小指示灯。"""
    im = new(32, 32); d = ImageDraw.Draw(im)
    R(d, (3, 9, 28, 27), METAL, INK)              # 机身
    R(d, (3, 9, 28, 12), METAL_D, INK)            # 顶部盖板
    R(d, (6, 18, 25, 22), INK2)                   # 出纸口（深色缝）
    R(d, (8, 12, 23, 19), LABEL, INK)             # 标签纸（从上方吐出）
    L(d, [(10, 14), (21, 14)], INK2)              # 标签上的条形码线
    for y in (15, 16): L(d, [(10, y), (17, y)], INK2)
    R(d, (5, 23, 20, 25), PANEL_D, INK)           # 控制面板
    E(d, (25, 22, 28, 26), C_GREEN, INK)          # 工作指示灯
    R(d, (5, 6, 8, 9), METAL_D, INK)              # 左侧走纸轴
    R(d, (23, 6, 26, 9), METAL_D, INK)            # 右侧走纸轴
    R(d, (4, 27, 8, 29), INK2); R(d, (23, 27, 27, 29), INK2)   # 支脚
    R(d, (4, 10, 5, 25), METAL_HI)                # 机身柔和高光
    return im


def test_tube():
    """试管 16x16（1 格）：玻璃管 + 液面 + 管口。中心 (8,8) 必须不透明。"""
    im = new(16, 16); d = ImageDraw.Draw(im)
    R(d, (5, 1, 10, 3), GLASS, INK)          # 管口
    R(d, (6, 3, 9, 14), GLASS, INK)          # 管身
    R(d, (7, 7, 9, 13), (150, 200, 206))     # 液体（(8,8) 不透明）
    L(d, [(7, 7), (9, 7)], GLASS_HI)         # 液面
    L(d, [(7, 4), (7, 12)], GLASS_HI)        # 高光
    R(d, (5, 13, 10, 15), METAL_D, INK)      # 管底
    return im


def note_paper():
    """化学品商人便条 16x32（1x2 格）：纸片 + 折角 + 手写行 + 回形针。
    上下两个 16px 格中心（y=8 / y=24）与画布上下缘都必须有不透明像素，保证占 1x2 格。"""
    im = new(16, 32); d = ImageDraw.Draw(im)
    R(d, (2, 0, 13, 31), LABEL, INK)              # 纸面（贴顶贴底）
    P(d, [(9, 0), (13, 0), (13, 4)], METAL_HI, INK)  # 折角
    for y in (6, 9, 12, 15, 18, 21, 24, 27):      # 手写行
        L(d, [(4, y), (11, y)], INK2)
    L(d, [(4, 28), (8, 28)], INK2)
    # 回形针（左侧）
    R(d, (0, 7, 2, 17), METAL_HI, INK)
    R(d, (1, 9, 1, 15), INK2)
    # 几个「试剂染点」让便条更真实
    E(d, (9, 20, 11, 22), C_SULF)
    E(d, (5, 30, 6, 31), C_ACID)
    return im


def heating_platform(active=True):       # 大型加热台：台座 + 容器位 + 加热器位
    im = new(48, 48); d = ImageDraw.Draw(im)
    R(d, (3, 30, 44, 43), METAL, INK)
    R(d, (3, 30, 44, 34), METAL_D, INK)
    R(d, (3, 40, 44, 43), INK2)
    R(d, (7, 8, 41, 30), METAL, INK)          # 背板
    R(d, (9, 10, 39, 27), PANEL, INK)
    # 左侧反应容器（烧杯）：空闲态为空玻璃
    R(d, (12, 14, 24, 29), GLASS, INK)
    if active:
        R(d, (13, 21, 23, 28), C_GAS)
        L(d, [(13, 21), (23, 21)], GLASS_HI)
    L(d, [(11, 14), (25, 14)], INK, 2)
    # 右侧加热器（酒精灯）：空闲态无火焰
    E(d, (28, 20, 40, 30), GLASS, INK)
    if active: E(d, (30, 24, 38, 29), (196, 206, 190))
    R(d, (32, 16, 36, 21), METAL_D, INK)
    if active:
        P(d, [(34, 10), (37, 15), (34, 17), (31, 15)], C_LAMP, INK)
        P(d, [(34, 12), (36, 15), (34, 16), (33, 15)], C_SULF)
    # 石棉网 / 支架
    L(d, [(10, 30), (42, 30)], INK, 2)
    for x in (12, 20, 28, 36): L(d, [(x, 30), (x, 32)], INK2)
    E(d, (7, 33, 10, 36), C_GREEN if active else INK2, INK)
    E(d, (13, 33, 16, 36), C_LAMP if active else INK2, INK)
    return im


def filtration_bench(active=True):       # 过滤台：漏斗 + 滤纸 + 锥形瓶接收
    im = new(48, 48); d = ImageDraw.Draw(im)
    R(d, (4, 36, 44, 43), METAL, INK)         # 台座
    R(d, (4, 40, 44, 43), INK2)
    R(d, (10, 10, 14, 40), METAL_D, INK)      # 立柱
    R(d, (9, 8, 34, 11), METAL, INK)          # 横臂
    # 漏斗
    P(d, [(16, 12), (30, 12), (25, 24), (21, 24)], GLASS, INK)
    R(d, (21, 24, 25, 29), GLASS, INK)
    E(d, (18, 14, 28, 19), LABEL if active else GLASS, INK)  # 滤纸
    if active:
        R(d, (18, 16, 28, 20), C_GAS)         # 待滤液
        L(d, [(18, 16), (28, 16)], GLASS_HI)
        for (x, y) in [(23, 30), (23, 33)]:
            E(d, (x - 1, y - 1, x + 1, y + 1), C_GAS, INK)  # 滤液滴
    # 接收锥形瓶
    P(d, [(21, 34), (27, 34), (32, 42), (16, 42)], GLASS, INK)
    if active:
        P(d, [(19, 42), (22, 37), (26, 37), (29, 42)], C_GAS)
        L(d, [(22, 37), (26, 37)], GLASS_HI)
    L(d, [(20, 34), (28, 34)], INK, 2)
    E(d, (38, 38, 41, 41), C_GREEN if active else INK2, INK)
    return im


def sludge():
    im = new(16, 16); d = ImageDraw.Draw(im)
    E(d, (1, 11, 14, 15), (90, 94, 92, 160))
    P(d, [(1, 14), (4, 7), (9, 5), (13, 9), (14, 14)], C_CARB, INK)
    P(d, [(4, 7), (9, 5), (9, 14), (1, 14)], (140, 144, 138))
    for (x, y, c) in [(5, 10, C_NIT), (9, 9, C_SULF), (11, 12, C_CHLOR)]:
        E(d, (x-1, y-1, x+1, y+1), c)
    return im


# ---------- 清单 ----------
ICONS = []
def add(i, n, c, size, maker): ICONS.append((i, n, c, size, maker))

# 气体 · 便携钢瓶（32×48 = 2×3 格，放大并精绘）
add("gas_chlorine",          "氯气 Cl₂",      "气体", (32,48), lambda: cylinder(C_CHLOR, "Cl2", True))
add("gas_hydrogen",          "氢气 H₂",       "气体", (32,48), lambda: cylinder(C_GAS, "H2"))
add("gas_oxygen",            "氧气 O₂",       "气体", (32,48), lambda: cylinder(C_ALK, "O2"))
add("gas_hydrogen_chloride", "氯化氢 HCl(g)", "气体", (32,48), lambda: cylinder(C_ACID, "HCl", True))
add("gas_ammonia",           "氨气 NH₃",      "气体", (32,48), lambda: cylinder(C_NIT, "NH3", True))
add("gas_carbon_dioxide",    "二氧化碳 CO₂",  "气体", (32,48), lambda: cylinder(C_PEROX, "CO2"))
add("gas_nitrogen",          "氮气 N₂",       "气体", (32,48), lambda: cylinder((176,182,190), "N2"))
# 液体 · 试剂瓶
add("liq_hydrochloric_acid", "盐酸 HCl",      "液体", (16,48), lambda: bottle_500(C_ACID, "HCl", True))
add("liq_sulfuric_acid",     "硫酸 H₂SO₄",    "液体", (16,48), lambda: bottle_500(C_SULF, "H2SO4", True))
add("liq_nitric_acid",       "硝酸 HNO₃",     "液体", (16,48), lambda: bottle_500(C_NIT, "HNO3", True))
add("liq_sodium_hydroxide",  "烧碱液 NaOH",   "液体", (16,48), lambda: bottle_500(C_ALK, "NaOH", True))
add("liq_hydrogen_peroxide", "双氧水 H₂O₂",   "液体", (16,48), lambda: bottle_500(C_PEROX, "H2O2"))
add("liq_brine",             "饱和盐水",      "液体", (16,48), lambda: bottle_500((190,196,190), "NaCl"))
add("liq_ammonia_water",     "氨水 NH₃·H₂O",  "液体", (16,48), lambda: bottle_500(C_NIT, "NH3aq", True))
add("liq_calcium_chloride",  "氯化钙溶液",    "液体", (16,48), lambda: bottle_500((186,194,200), "CaCl2"))
# 固体 · 晶体 / 粉末
add("sol_sodium_chloride",        "氯化钠晶体", "固体", (16,16), lambda: crystal((214,214,208), (238,238,232)))
add("sol_sodium_hydroxide",       "片状烧碱",   "固体", (16,16), lambda: powder((206,210,204)))
add("sol_copper_sulfate",         "硫酸铜晶体", "固体", (16,16), lambda: crystal(C_COPPER, (150,196,202)))
add("sol_potassium_nitrate",      "硝酸钾",     "固体", (16,16), lambda: crystal((216,212,198), (240,236,222)))
add("sol_calcium_carbonate",      "碳酸钙",     "固体", (16,16), lambda: powder((224,222,212)))
add("sol_ammonium_nitrate",       "硝酸铵",     "固体", (16,16), lambda: crystal((212,216,210), (236,238,232)))
add("sol_potassium_permanganate", "高锰酸钾",   "固体", (16,16), lambda: crystal(C_NIT, (178,156,196)))
add("sol_sulfur",                 "硫磺",       "固体", (16,16), lambda: powder(C_SULF))
add("sol_activated_carbon",       "活性炭",     "固体", (16,16), lambda: powder(C_CARB))
add("sol_bleaching_powder",       "漂白粉",     "固体", (16,16), lambda: powder((220,222,208)))
# 设备 · 48x48（全部避开原版已有机器）
# 每台设备有两张状态图：`<id>` = 空闲态（无试剂、指示灯灭），`<id>_on` = 工作态
MACHINES = [
    ("chem_reactor",       "合成反应釜", "reactor",      C_ACID),
    ("electrolyzer",       "电解槽",     "electrolyzer", C_GAS),
    ("gas_generator",      "气体发生器", "gasgen",       C_CHLOR),
    ("condenser_unit",     "冷凝塔",     "condenser",    C_GAS),
    ("crystallizer",       "结晶器",     "crystallizer", C_COPPER),
    ("high_pressure_synth","高压合成塔", "liquefier",    C_NIT),
    ("fractionator",       "分馏柱",     "fractionator", C_SULF),
    ("titration_bench",    "滴定台",     "titration",    C_ALK),
    ("canister_filling",   "气体灌装台", "filling",      C_GAS),
    ("decanter",           "静置滗析槽", "decanter",     C_GAS),
    ("gas_scrubber",       "气体洗涤塔", "scrubber",     C_GAS),
]
for mid, mname, mkind, mcolor in MACHINES:
    add(mid, mname + "（空闲）", "设备", (48, 48),
        lambda k=mkind, c=mcolor: machine(k, c, active=False))
    add(mid + "_on", mname + "（工作）", "设备状态", (48, 48),
        lambda k=mkind, c=mcolor: machine(k, c, active=True))
# 仪器 / 工具（避开原版成分光谱仪、量筒、滴管）
add("precision_balance",  "精密天平",   "仪器", (16,16), lambda: instrument_small("balance"))
add("ph_meter",           "pH 计",      "仪器", (16,16), lambda: instrument_small("ph_meter"))
add("thermometer",        "温度计",     "仪器", (16,16), lambda: instrument_small("thermometer"))
add("test_tube",            "试管",       "器材", (16,16), test_tube)
add("note_chem_dealer",   "化学品商人的便条", "工具", (16,32), note_paper)
add("label_printer",    "标签打印机", "仪器", (32,32), label_printer)

add("gas_canister_empty", "空钢瓶",     "器皿", (32,48), lambda: cylinder(METAL, "", False))
add("catalyst_kit",       "催化剂套件", "仪器", (16,16), lambda: instrument_small("catalyst"))
add("safety_goggles",     "护目镜",     "防护", (16,16), lambda: instrument_small("goggles"))
# 大型储气桶（48×64 = 3×4 格），与便携钢瓶区分：类似原版水罐
add("gas_drum_empty",        "大型储气桶（空）", "大型容器", (48,64), lambda: gas_drum(METAL, "", filled=False))
add("gas_drum_oxygen",       "大型氧气桶",       "大型容器", (48,64), lambda: gas_drum(C_ALK, "O2"))
add("gas_drum_hydrogen",     "大型氢气桶",       "大型容器", (48,64), lambda: gas_drum(C_GAS, "H2", hazard=True))
add("gas_drum_nitrogen",     "大型氮气桶",       "大型容器", (48,64), lambda: gas_drum((176,182,190), "N2"))
add("gas_drum_chlorine",     "大型氯气桶",       "大型容器", (48,64), lambda: gas_drum(C_CHLOR, "Cl2", hazard=True))
add("gas_drum_carbon_dioxide","大型二氧化碳桶",  "大型容器", (48,64), lambda: gas_drum(C_PEROX, "CO2"))
# 气罐商品 · 三档纯度（工业 / 医用 / 化学纯）
GRADES = [("industrial", "工业级"), ("medical", "医用级"), ("ultrapure", "化学纯")]
CANS = [
    ("oxygen",   "氧气",   C_ALK,    "O2"),
    ("hydrogen", "氢气",   C_GAS,    "H2"),
    ("nitrogen", "氮气",   (176,182,190), "N2"),
    ("chlorine", "氯气",   C_CHLOR,  "Cl2"),
]
for gname, glabel in GRADES:
    gi = {"industrial": 0, "medical": 1, "ultrapure": 2}[gname]
    for cid, cname, ccol, cmark in CANS:
        add(f"can_{cid}_{gname}", f"{cname}·{glabel}", "气罐商品", (32,48),
            lambda col=ccol, mk=cmark, g=gi: graded_canister(col, mk, g))

# T4 虚构违禁品（与原版药剂反应生成）
add("illicit_voidsalt",   "空盐（违禁）",   "违禁品", (16,16), lambda: illicit("voidsalt"))
add("illicit_redmist",    "红雾（违禁）",   "违禁品", (16,16), lambda: illicit("redmist"))
add("illicit_nightglass", "夜玻（违禁）",   "违禁品", (16,16), lambda: illicit("nightglass"))
add("illicit_ashbreath",  "灰息（违禁）",   "违禁品", (16,16), lambda: illicit("ashbreath"))

# 器材 · 反应容器
add("beaker",            "烧杯",        "器材", (16,32), lambda: labware_tall("beaker"))
add("retort_flask",      "鹅颈瓶",      "器材", (16,32), lambda: labware_tall("retort_flask"))
add("erlenmeyer_flask",  "锥形瓶",      "器材", (16,32), lambda: labware_tall("erlenmeyer_flask"))
add("test_tube_rack",    "试管架",      "器材", (16,32), lambda: labware_tall("test_tube_rack"))
# 器材 · 耗材
add("filter_paper",      "化学滤纸",    "耗材", (16,16), lambda: labware("filter_paper"))
add("filter_paper_fine", "精密滤纸",    "耗材", (16,16), lambda: labware("filter_paper_fine"))
add("filter_paper_ash",  "无灰定量滤纸", "耗材", (16,16), lambda: labware("filter_paper_ash"))
add("wick_cotton",       "棉灯芯",      "耗材", (16,16), lambda: labware("wick_cotton"))
add("wick_ceramic",      "陶瓷灯芯",    "耗材", (16,16), lambda: labware("wick_ceramic"))
# 加热器
add("alcohol_lamp",      "酒精灯",      "加热器", (16,32), lambda: labware_tall("alcohol_lamp"))
add("spirit_burner",     "酒精喷灯",    "加热器", (16,32), lambda: labware_tall("spirit_burner"))
# 实时加热台 + 过滤台：也有空闲/工作两态
add("heating_platform",     "大型加热台（空闲）", "设备", (48,48), lambda: heating_platform(False))
add("heating_platform_on",  "大型加热台（工作）", "设备状态", (48,48), lambda: heating_platform(True))
add("filtration_bench",     "过滤台（空闲）",     "设备", (48,48), lambda: filtration_bench(False))
add("filtration_bench_on",  "过滤台（工作）",     "设备状态", (48,48), lambda: filtration_bench(True))
# 热反应新增物质
add("gas_ethylene",      "乙烯 C₂H₄",   "气体", (32,48), lambda: cylinder((186,196,180), "C2H4"))
add("gas_nitrous_oxide", "笑气 N₂O",    "气体", (32,48), lambda: cylinder((196,188,210), "N2O"))
add("sol_coffee_grounds","咖啡豆糊",    "固体", (16,16), lambda: powder((120, 96, 74)))
add("sol_caffeine",      "咖啡因",      "固体", (16,16), lambda: crystal((224, 220, 206), (244, 240, 226)))

# 副产物
add("sludge", "废渣", "副产物", (16,16), sludge)
# 第 21 轮 P2a：10 种新化学品（全部 16×16 固体，1 格）
add("sol_calcium_oxide",        "生石灰 CaO",       "固体", (16,16), lambda: crystal((232,232,226), (250,250,244)))
add("sol_calcium_hydroxide",    "熟石灰 Ca(OH)₂",   "固体", (16,16), lambda: powder((238,238,232)))
add("sol_sodium_carbonate",     "纯碱 Na₂CO₃",      "固体", (16,16), lambda: crystal((228,230,224), (246,246,240)))
add("sol_sodium_bicarbonate",   "小苏打 NaHCO₃",    "固体", (16,16), lambda: powder((242,242,236)))
add("sol_ammonium_chloride",    "氯化铵 NH₄Cl",     "固体", (16,16), lambda: crystal((236,238,234), (252,252,248)))
add("sol_manganese_dioxide",    "二氧化锰 MnO₂",    "固体", (16,16), lambda: powder((62,64,66)))
add("sol_copper_oxide",         "氧化铜 CuO",       "固体", (16,16), lambda: powder((88,62,52)))
add("sol_potassium_sulfate",    "硫酸钾 K₂SO₄",     "固体", (16,16), lambda: crystal((232,230,218), (250,248,238)))
add("sol_sodium_nitrate",       "硝酸钠 NaNO₃",     "固体", (16,16), lambda: crystal((238,238,230), (252,252,246)))
add("sol_potassium_manganate",  "锰酸钾 K₂MnO₄",    "固体", (16,16), lambda: crystal((46,86,64), (98,134,106)))
add("sol_sediment", "沉降沉淀", "副产物", (16,16), lambda: powder((128, 132, 126)))


def preview():
    w, h, s = 320, 200, 48
    im = Image.new("RGBA", (w, h), (24, 28, 34, 255)); d = ImageDraw.Draw(im)
    try:
        f = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 18)
        sm = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 10)
    except Exception:
        f = ImageFont.load_default(); sm = ImageFont.load_default()
    R(d, (0, 0, w, 34), (34, 42, 52))
    d.text((12, 6), "FreeChem 自由化学 · 新增基础药剂", fill=(236, 240, 246), font=f)
    d.text((12, 178), "原版没有的具名化合物 · gas / liquid / solid", fill=(150, 165, 180), font=sm)
    picks = ["liq_hydrochloric_acid", "liq_sulfuric_acid", "gas_chlorine", "gas_hydrogen",
             "chem_reactor", "electrolyzer", "sol_copper_sulfate", "sol_sodium_chloride",
             "gas_generator", "condenser_unit", "liq_nitric_acid", "ph_meter"]
    for i, k in enumerate(picks):
        mk = next((m for kk, _, _, _, m in ICONS if kk == k), None)
        if mk is None: continue
        ic = mk(); ic.thumbnail((s, s), Image.NEAREST)
        x, y = 12 + (i % 6) * 50, 44 + (i // 6) * 62
        R(d, (x-2, y-2, x+s+2, y+s+2), (38, 44, 54), (60, 70, 86))
        im.alpha_composite(ic, (x + (s-ic.width)//2, y + (s-ic.height)//2))
    return im


def main():
    for key, name, cat, size, maker in ICONS:
        im = maker()
        if im.size != size:
            raise ValueError(f"{key}: expected {size}, got {im.size}")
        im.save(os.path.join(OUT, key + ".png"))

    cell, scale, cols = 48, 2, 8
    ch = cell * scale + 20
    rows = (len(ICONS) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell * scale, rows * ch + 46), (24, 28, 34, 255))
    sd = ImageDraw.Draw(sheet)
    try:
        tf = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 21)
        lf = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 12)
    except Exception:
        tf = ImageFont.load_default(); lf = ImageFont.load_default()
    sd.text((16, 9), "FreeChem · 自由化学 — 新增基础药剂与设备（对齐原版美术风格）",
            fill=(236, 240, 246), font=tf)
    for i, (key, name, cat, size, maker) in enumerate(ICONS):
        cx, cy = (i % cols) * cell * scale, 46 + (i // cols) * ch
        ic = maker(); ic.thumbnail((cell * scale - 8, cell * scale - 8), Image.NEAREST)
        sd.rectangle((cx+2, cy+2, cx+cell*scale-2, cy+cell*scale-2),
                     fill=(36, 42, 52), outline=(58, 68, 82))
        sheet.alpha_composite(ic, (cx + (cell*scale-ic.width)//2, cy + (cell*scale-ic.height)//2))
        sd.text((cx+4, cy+cell*scale+2), name, fill=(216, 224, 234), font=lf)
    sheet.convert("RGB").save(SHEET)
    preview().convert("RGB").save(r"C:\Users\Administrator\WorkBuddy\Claw\ps-chem-mod\FreeChem\preview.png")
    print(f"generated {len(ICONS)} icons -> {OUT}")
    print("sheet ->", SHEET)


if __name__ == "__main__":
    main()
