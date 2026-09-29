# -*- coding: utf-8 -*-
"""隠しミニゲーム「わさび当て」の寿司と下駄のテクスチャを手続き的に作る（法線マップ付き）。

    python GenAssets/sushi/make_textures.py

出力: GenAssets/sushi/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/Sushi/*.png

ネタの UV は u = 長さ方向（0..1 でネタ1枚）、v = 断面の周（0.5 が上面の中央、0/1 が裏の中央）。
シャリ・わさびは実寸の箱投影（シャリ 1枚 = 4cm、わさび 1枚 = 2cm）。下駄は 1枚 = 0.5m（木目は U）。
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, normal_map, rgb, colorize, Saver, wood  # noqa: E402

save = Saver(HERE, "Sushi")
W, H = 512, 256   # ネタ（u = 長さ、v = 周）


def grid(w=W, h=H):
    v, u = np.mgrid[0:h, 0:w]
    return u / w, v / h


def top_mask(v, lo=0.28, hi=0.72, soft=0.04):
    """上面（v が lo〜hi）で 1、側面・裏で 0"""
    return np.clip((v - lo) / soft, 0, 1) * np.clip((hi - v) / soft, 0, 1)


def fbm_wh(beta, seed, ax=1.0, ay=1.0):
    return fbm(W, beta, seed, ax=ax, ay=ay, m=H)


def maguro():
    """赤身：深い赤。筋が淡く弧を描き、切り口はつやのあるまだら"""
    u, v = grid()
    warp = (fbm_wh(2.4, 701) - 0.5) * 0.6
    sinew = np.abs(np.sin(2 * np.pi * (u * 2.6 + v * 1.1 + warp)))
    sinew = np.clip(1 - (1 - sinew) / 0.05, 0, 1)   # 細い筋
    mott = fbm_wh(1.3, 702)
    base = colorize(np.clip(0.5 + (mott - 0.5) * 0.9, 0, 1), (120, 12, 24), (170, 30, 40))
    col = base * (1 - sinew[..., None] * 0.35) + np.array([205, 120, 125]) * (sinew[..., None] * 0.35)
    save("maguro", rgb(col), normal_map(mott * 0.3 + sinew * 0.25, 0.8))


def salmon():
    """サーモン：橙の身に白い脂の縞（斜め）"""
    u, v = grid()
    warp = (fbm_wh(2.2, 711) - 0.5) * 0.7
    s = 0.5 + 0.5 * np.sin(2 * np.pi * (u * 6.0 + v * 2.2 + warp))
    width = 0.86 + (fbm_wh(1.8, 713) - 0.5) * 0.12          # 縞の太さは場所でまちまち
    fat = np.clip((s - width) / 0.05, 0, 1) * (0.75 + fbm_wh(1.0, 714) * 0.25)
    mott = fbm_wh(1.4, 712)
    base = colorize(np.clip(0.5 + (mott - 0.5) * 0.8, 0, 1), (225, 92, 45), (250, 130, 70))
    col = base * (1 - fat[..., None]) + np.array([250, 218, 190]) * fat[..., None]
    save("salmon", rgb(col), normal_map(fat * 0.6 + mott * 0.2, 1.0))


def tai():
    """鯛：白身（ほんのり桃色の半透明）。片側の縁に湯霜の皮（赤と銀）"""
    u, v = grid()
    warp = (fbm_wh(2.4, 721) - 0.5) * 0.5
    sinew = np.clip(1 - (1 - np.abs(np.sin(2 * np.pi * (u * 3.2 - v * 0.9 + warp)))) / 0.05, 0, 1)
    mott = fbm_wh(1.2, 722)
    flesh = colorize(np.clip(0.5 + (mott - 0.5) * 0.7, 0, 1), (232, 206, 200), (248, 230, 224))
    col = flesh * (1 - sinew[..., None] * 0.18) + np.array([250, 245, 240]) * sinew[..., None] * 0.18
    # 皮：上面の片側の縁（v 0.6〜0.75）に赤い皮、その中に銀の光
    edge = np.clip((v - 0.6) / 0.03, 0, 1) * np.clip((0.78 - v) / 0.03, 0, 1)
    skin_n = fbm_wh(0.8, 723, ax=6.0, ay=1.0)
    skin = colorize(skin_n, (170, 45, 55), (225, 110, 115))
    silver = np.clip((fbm_wh(0.4, 724) - 0.62) / 0.1, 0, 1)
    skin = skin * (1 - silver[..., None] * 0.5) + np.array([225, 222, 228]) * silver[..., None] * 0.5
    col = col * (1 - edge[..., None]) + skin * edge[..., None]
    save("tai", rgb(col), normal_map(mott * 0.2 + sinew * 0.15 + edge * skin_n * 0.3, 0.8))


def ika():
    """イカ：乳白色。上面に細かい鹿の子の切れ目"""
    u, v = grid()
    # 上面の実寸：長さ 7cm = 512px、幅 3cm ≒ v 0.44 幅 → 切れ目の間隔 3.5mm くらい
    a = (u * 20 + v * 9) % 1.0
    b = (u * 20 - v * 9) % 1.0
    cut = (np.clip(1 - np.minimum(a, 1 - a) / 0.05, 0, 1) + np.clip(1 - np.minimum(b, 1 - b) / 0.05, 0, 1)).clip(0, 1)
    cut *= top_mask(v, 0.3, 0.7)
    mott = fbm_wh(1.0, 731)
    base = colorize(np.clip(0.5 + (mott - 0.5) * 0.6, 0, 1), (232, 232, 222), (246, 246, 238))
    col = base * (1 - cut[..., None] * 0.12)
    save("ika", rgb(col), normal_map(mott * 0.15 - cut * 0.7, 1.6))


def tamago():
    """玉子：黄色。細かい気泡、側面に焼き重ねの層、上面はうっすら焼き色"""
    u, v = grid()
    mott = fbm_wh(0.9, 741)
    bub = np.clip((fbm_wh(0.2, 742) - 0.66) / 0.08, 0, 1)
    base = colorize(np.clip(0.5 + (mott - 0.5) * 0.7, 0, 1), (238, 186, 62), (252, 214, 100))
    side = 1 - top_mask(v, 0.28, 0.72, 0.06)
    layers = (0.5 + 0.5 * np.sin(2 * np.pi * v * 22 + (fbm_wh(2.0, 743) - 0.5) * 3)) * side
    brown = np.clip((fbm_wh(1.8, 744) - 0.55) / 0.2, 0, 1) * top_mask(v, 0.32, 0.68)
    col = base * (1 - bub[..., None] * 0.12) * (1 - layers[..., None] * 0.08)
    col = col * (1 - brown[..., None] * 0.35) + np.array([190, 120, 45]) * brown[..., None] * 0.35
    save("tamago", rgb(col), normal_map(mott * 0.2 - bub * 0.3 + layers * 0.2, 1.0))


def anago():
    """穴子：皮を上に。焦げ茶の皮につめ（甘だれ）のつや、横に細かい筋、ところどころ焼き目"""
    u, v = grid()
    seg = 0.5 + 0.5 * np.sin(2 * np.pi * (u * 26 + (fbm_wh(2.2, 751) - 0.5) * 1.2))
    mott = fbm_wh(1.1, 752)
    base = colorize(np.clip(0.45 + (mott - 0.5) * 0.9 + seg * 0.08, 0, 1), (70, 38, 20), (140, 85, 45))
    char = np.clip((fbm_wh(1.6, 753) - 0.62) / 0.12, 0, 1)
    col = base * (1 - char[..., None] * 0.45)
    # 側面は身の色（淡い茶）
    side = 1 - top_mask(v, 0.3, 0.7, 0.05)
    col = col * (1 - side[..., None] * 0.6) + np.array([190, 140, 90]) * side[..., None] * 0.6
    save("anago", rgb(col), normal_map(seg * 0.25 + mott * 0.2 - char * 0.2, 1.0))


def nori():
    """海苔：黒緑の繊維"""
    n = 256
    f = fbm(n, 0.5, 761, ax=0.25, ay=1.0) * 0.5 + fbm(n, 0.5, 762, ax=1.0, ay=0.25) * 0.5
    col = colorize(f, (14, 22, 16), (40, 56, 38))
    save("nori", rgb(col), normal_map(f, 1.4))


def rice(n=512):
    """シャリ（1枚 = 4cm）：米粒を敷き詰めた高さ場。継ぎ目なしに折り返して描く"""
    rnd = np.random.default_rng(771)
    hmap = Image.new("L", (n, n), 0)
    shade = Image.new("L", (n, n), 215)   # すき間の地（奥の粒）も白っぽく
    dh, ds = ImageDraw.Draw(hmap), ImageDraw.Draw(shade)
    grain_l, grain_w = n * 0.11, n * 0.055   # 粒 約 4.5mm x 2.2mm
    for _ in range(1100):
        cx, cy = rnd.uniform(0, n), rnd.uniform(0, n)
        ang = rnd.uniform(0, np.pi)
        L, Wd = grain_l * rnd.uniform(0.8, 1.1), grain_w * rnd.uniform(0.85, 1.15)
        tone = int(rnd.uniform(170, 255))
        pts = []
        for k in range(20):
            t = 2 * np.pi * k / 20
            x, y = np.cos(t) * L / 2, np.sin(t) * Wd / 2
            pts.append((x * np.cos(ang) - y * np.sin(ang), x * np.sin(ang) + y * np.cos(ang)))
        for ox in (-n, 0, n):
            for oy in (-n, 0, n):
                poly = [(cx + ox + px, cy + oy + py) for px, py in pts]
                dh.polygon(poly, fill=255)
                ds.polygon(poly, fill=tone)
    h = np.asarray(hmap.filter(ImageFilter.GaussianBlur(2.2)), dtype=float) / 255.0
    s = np.asarray(shade.filter(ImageFilter.GaussianBlur(0.8)), dtype=float) / 255.0
    gap = 1 - np.clip(h * 1.6, 0, 1)
    lum = 0.92 + (s - 0.85) * 0.3 - gap * 0.12
    col = np.array([246, 244, 236], dtype=float)[None, None, :] * lum[..., None]
    col = col + np.array([-6, -4, 6])[None, None, :] * gap[..., None]   # 粒のすき間はわずかに青みの影
    save("rice", rgb(col), normal_map(h, 3.0))


def wasabi(n=256):
    """わさび（1枚 = 2cm）：すりおろしのざらつき"""
    f = fbm(n, 0.35, 781)
    g = fbm(n, 1.2, 782)
    # 懐中電灯の強い光でも緑に見えるよう、濃いめ・彩度高め
    col = colorize(np.clip(0.5 + (g - 0.5) * 0.8 + (f - 0.5) * 0.3, 0, 1), (62, 118, 22), (112, 168, 40))
    save("wasabi", rgb(col), normal_map(f, 2.4))


def geta(n=1024):
    """下駄（寿司下駄）：白木（檜）。1枚 = 0.5m、木目は U"""
    col, nrm = wood(n, 791, (196, 160, 112), (226, 198, 150), rings=9)
    save("geta", col, nrm)


if __name__ == "__main__":
    maguro(); salmon(); tai(); ika(); tamago(); anago(); nori(); rice(); wasabi(); geta()
