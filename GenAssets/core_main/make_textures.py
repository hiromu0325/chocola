# -*- coding: utf-8 -*-
"""MAIN CORE ROOM（core_main）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/core_main/make_textures.py

出力: GenAssets/core_main/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/CoreMain/*.png
"""
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, normal_map, rgb, colorize, _font, Saver  # noqa: E402

save = Saver(HERE, "CoreMain")


def dark_stone(n=1024):
    """磨いた黒い石の床（1枚 = 2m、1m角 x 4、細い金の目地、かすかな石目）"""
    y, x = np.mgrid[0:n, 0:n] / n
    vein = np.abs(fbm(n, 1.6, 2501, ax=2.0, ay=1.0) - 0.5)
    vein = np.clip(1 - vein / 0.03, 0, 1) * 0.5
    tone = fbm(n, 2.2, 2502)
    base = np.array([24, 24, 28], dtype=float)
    lum = 0.9 + (tone - 0.5) * 0.3 + vein * 0.8
    col = base[None, None, :] * lum[..., None]
    gx = np.minimum((x * 2) % 1, 1 - (x * 2) % 1); gy = np.minimum((y * 2) % 1, 1 - (y * 2) % 1)
    seam = (np.minimum(gx, gy) < 0.003).astype(float)
    col = col * (1 - seam[..., None]) + np.array([120, 96, 50]) * seam[..., None]
    save("dark_stone", rgb(col), normal_map(-seam * 0.5 + tone * 0.05, 1.0))


def wall_rib(n=1024):
    """縦の畝のある暗い壁パネル（1枚 = 1m、畝 8本、上下に目地）"""
    y, x = np.mgrid[0:n, 0:n] / n
    rib = 0.5 + 0.5 * np.cos(2 * np.pi * x * 8)
    tone = fbm(n, 2.0, 2511)
    grit = fbm(n, 0.1, 2512)
    seam = (np.minimum(y % 1, 1 - y % 1) < 0.004).astype(float)
    base = np.array([34, 36, 42], dtype=float)
    lum = 0.85 + rib * 0.25 + (tone - 0.5) * 0.12 + (grit - 0.5) * 0.05 - seam * 0.4
    save("wall_rib", rgb(base[None, None, :] * lum[..., None]), normal_map(rib * 0.6 + grit * 0.1 - seam, 2.0))


def core_gold(w=2048, h=1024):
    """球形コアの芯：金色の神経の枝と光の粒（横方向は継ぎ目なし、球に巻く）"""
    rnd = random.Random(2521)
    img = Image.new("RGB", (w, h), (30, 18, 4))
    d = ImageDraw.Draw(img)
    for k in range(260):
        x, y = rnd.uniform(0, w), rnd.uniform(0, h)
        pts = [(x, y)]
        a = rnd.uniform(0, math.pi * 2)
        for s in range(rnd.randint(6, 26)):
            a += rnd.uniform(-0.6, 0.6)
            x += math.cos(a) * rnd.uniform(10, 30); y += math.sin(a) * rnd.uniform(6, 18)
            pts.append((x, y))
        c = rnd.choice([(255, 210, 110), (255, 180, 70), (240, 150, 50), (255, 235, 170)])
        for ox in (-w, 0, w):
            d.line([(px + ox, py) for px, py in pts], fill=c, width=rnd.choice([1, 2, 2, 3]))
    for k in range(400):
        x, y = rnd.uniform(0, w), rnd.uniform(0, h)
        r = rnd.uniform(2, 7)
        for ox in (-w, 0, w):
            d.ellipse([x - r + ox, y - r, x + r + ox, y + r], fill=(255, 240, 200))
    glow = img.filter(ImageFilter.GaussianBlur(8))
    a = np.asarray(img).astype(float) * 0.7 + np.asarray(glow).astype(float) * 1.4
    save("core_gold", rgb(a))


def screen_message(w=1024, h=640):
    """祭壇の端末：再生メッセージ（小川 暁）"""
    img = Image.new("RGB", (w, h), (18, 14, 8))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 60], fill=(80, 60, 24))
    d.text((24, 12), "再生メッセージ　─　小川 暁", font=_font(34, mincho=False), fill=(255, 230, 170))
    pts = []
    for x in range(40, w - 40, 4):
        t = (x - 40) / (w - 80)
        yy = 300 + math.sin(t * 60) * 60 * math.sin(t * math.pi) * (0.6 + 0.4 * math.sin(t * 13))
        pts.append((x, yy))
    d.line(pts, fill=(255, 200, 100), width=3)
    d.rectangle([40, 470, w - 40, 490], outline=(200, 160, 80), width=2)
    d.rectangle([44, 474, 44 + int((w - 88) * 0.02), 486], fill=(255, 200, 100))
    d.text((40, 510), "▶  00:03 / 04:12", font=_font(30, mincho=False), fill=(230, 200, 140))
    d.text((40, 560), "二宮さん。ここまで来たなら──", font=_font(30, mincho=False), fill=(230, 210, 170))
    save("screen_message", img)


def plate(w=1024, h=256):
    img = Image.new("RGB", (w, h), (40, 32, 20))
    d = ImageDraw.Draw(img)
    d.rectangle([8, 8, w - 8, h - 8], outline=(200, 160, 80), width=6)
    d.text((60, 40), "RENASCITA", font=_font(120, mincho=False), fill=(230, 190, 110))
    d.text((64, 180), "MAIN CORE　記憶の再生", font=_font(40, mincho=False), fill=(200, 170, 110))
    save("plate", img)


if __name__ == "__main__":
    dark_stone(); wall_rib(); core_gold(); screen_message(); plate()
