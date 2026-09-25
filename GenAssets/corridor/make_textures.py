# -*- coding: utf-8 -*-
"""回廊（ロの字の廊下）と、扉の奥の暗い廊下のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/corridor/make_textures.py

出力: GenAssets/corridor/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/Corridor/*.png
"""
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, band, normal_map, rgb, colorize, Saver, planks, wood  # noqa: E402

save = Saver(HERE, "Corridor")


def plaster(n=1024):
    """白い漆喰（1枚 = 1m）：ごく淡いむらと、こての跡・細かい砂粒"""
    base = np.array([236, 233, 227], dtype=float)
    mott = fbm(n, 1.3, 3101)
    trowel = fbm(n, 2.2, 3102, ax=1.0, ay=2.5)
    grain = band(n, 180, 420, 3103)
    lum = 0.965 + (mott - 0.5) * 0.05 + (trowel - 0.5) * 0.025 + (grain - 0.5) * 0.02
    save("plaster", rgb(base[None, None, :] * lum[..., None]), normal_map(trowel * 0.6 + grain * 0.4, 1.4))


def paint(n=1024):
    """塗装した木（1枚 = 1m、木目は U）：扉・枠・腰壁に色を掛けて使う。下の木目がうっすら透ける"""
    img, _ = wood(n, 3111, (205, 205, 205), (238, 238, 238), rings=9)
    a = np.asarray(img).astype(float)
    brush = fbm(n, 1.6, 3112, ax=30.0, ay=1.0)          # 刷毛目（木目方向）
    a *= (0.97 + (brush[..., None] - 0.5) * 0.06)
    lum = a.mean(axis=2) / 255.0
    a = 232 + (a - a.mean()) * 0.28                      # 色は白に寄せ、Unity側の色で塗り分ける
    save("paint", rgb(a), normal_map(lum * 0.6 + brush * 0.4, 0.9))


def old_plaster(n=1024):
    """扉の奥の古い廊下の壁（1枚 = 幅2.5m x 高さ2.5m、画像の下端 = 床）：
    黄ばんだ漆喰、上からの雨染み、床際の黒ずみ、ひび割れ"""
    y, x = np.mgrid[0:n, 0:n] / n
    base = np.array([214, 206, 190], dtype=float)
    mott = fbm(n, 1.4, 3121)
    col = base[None, None, :] * (0.92 + (mott[..., None] - 0.5) * 0.12)
    # 雨染み：天井際から垂れる縦長のしみ
    drip = fbm(n, 1.8, 3122, ax=6.0, ay=0.4)
    stain = np.clip((drip - 0.55) * 3.0, 0, 1) * np.clip(1.0 - y * 1.6, 0, 1) ** 0.7
    col = col * (1 - stain[..., None] * 0.35) + np.array([120, 100, 70])[None, None, :] * stain[..., None] * 0.25
    # 床際の黒ずみ（画像の下 = 床）
    grime = np.clip((y - 0.72) / 0.28, 0, 1) ** 1.6 * (0.6 + 0.4 * fbm(n, 1.5, 3123))
    col *= (1 - grime[..., None] * 0.45)
    img = rgb(col)
    d = ImageDraw.Draw(img)
    rnd = random.Random(3124)
    cracks = Image.new("L", (n, n), 0)
    dc = ImageDraw.Draw(cracks)
    for k in range(7):
        px, py = rnd.uniform(0, n), rnd.uniform(0, n * 0.6)
        ang = rnd.uniform(1.2, 1.9)
        for s in range(rnd.randint(20, 55)):
            ang += rnd.uniform(-0.35, 0.35)
            nx, ny = px + np.cos(ang) * 6, py + np.sin(ang) * 6
            for ox in (-n, 0, n):
                d.line([px + ox, py, nx + ox, ny], fill=(118, 108, 94), width=1)
                dc.line([px + ox, py, nx + ox, ny], fill=255, width=2)
            px, py = nx % n, ny
    h = mott * 0.5 + drip * 0.2 - np.asarray(cracks.filter(ImageFilter.GaussianBlur(1)), dtype=float) / 255.0 * 0.6
    save("old_plaster", img, normal_map(h, 2.0))


def old_floor(n=1024):
    """扉の奥の廊下の床（1枚 = 1m）：暗い床板に埃が溜まり、真ん中だけ擦れている"""
    img, nrm = planks(n, 3131, (48, 32, 24), (88, 60, 42), count=8)
    a = np.asarray(img).astype(float)
    dust = fbm(n, 1.5, 3132)
    m = np.clip((dust - 0.35) * 1.8, 0, 1)[..., None] * 0.5
    a = a * (1 - m) + np.array([112, 104, 94])[None, None, :] * m
    save("old_floor", rgb(a), nrm)


if __name__ == "__main__":
    plaster(); paint(); old_plaster(); old_floor()
    img, nrm = planks(1024, 3141, (58, 36, 26), (106, 68, 46), count=8); save("floor", img, nrm)
