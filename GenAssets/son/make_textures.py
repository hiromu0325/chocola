# -*- coding: utf-8 -*-
"""息子の部屋（son_room）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/son/make_textures.py

出力: GenAssets/son/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/SonRoom/*.png
"""
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, normal_map, rgb, colorize, _font, Saver, planks, fabric  # noqa: E402

save = Saver(HERE, "SonRoom")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


def wallpaper(n=1024):
    """淡い空色に白い雲と小さな星の壁紙（1枚 = 0.6m）"""
    img = Image.new("RGB", (n, n), (206, 226, 240))
    d = ImageDraw.Draw(img)
    rnd = random.Random(2701)
    for k in range(6):
        cx, cy = rnd.uniform(0, n), rnd.uniform(0, n)
        for j in range(5):
            ox, oy, r = rnd.uniform(-60, 60), rnd.uniform(-18, 18), rnd.uniform(30, 50)
            for sx in (-n, 0, n):
                for sy in (-n, 0, n):
                    d.ellipse([cx + ox - r + sx, cy + oy - r * 0.7 + sy, cx + ox + r + sx, cy + oy + r * 0.7 + sy], fill=(244, 248, 252))
    for k in range(18):
        cx, cy, r = rnd.uniform(0, n), rnd.uniform(0, n), rnd.uniform(8, 14)
        pts = []
        for j in range(10):
            a = -math.pi / 2 + j * math.pi / 5
            rr = r if j % 2 == 0 else r * 0.45
            pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
        for sx in (-n, 0, n):
            for sy in (-n, 0, n):
                d.polygon([(x + sx, y + sy) for x, y in pts], fill=(250, 230, 150))
    a = np.asarray(img).astype(float)
    emb = fbm(n, 0.2, 2702)
    a *= (0.98 + emb[..., None] * 0.04)
    save("wallpaper", rgb(a), normal_map(emb, 1.2))


def quilt(n=1024):
    """掛け布団（1枚 = 0.5m）：紺の地に黄色い星と月"""
    img, nrm = fabric(n, 2711, (46, 62, 110), weave=260)
    im = Image.fromarray(np.asarray(img)).convert("RGB")
    d = ImageDraw.Draw(im)
    rnd = random.Random(2712)
    for i in range(5):
        for j in range(5):
            cx, cy = (i + 0.5 + (j % 2) * 0.5) * n / 5, (j + 0.5) * n / 5
            if (i + j) % 3 == 0:
                for sx in (-n, 0, n):
                    d.ellipse([cx - 34 + sx, cy - 34, cx + 34 + sx, cy + 34], fill=(246, 220, 120))
                    d.ellipse([cx - 20 + sx, cy - 40, cx + 44 + sx, cy + 26], fill=(46, 62, 110))
            else:
                r = rnd.uniform(22, 32)
                pts = []
                for k in range(10):
                    a = -math.pi / 2 + k * math.pi / 5
                    rr = r if k % 2 == 0 else r * 0.45
                    pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
                for sx in (-n, 0, n):
                    d.polygon([(x + sx, y) for x, y in pts], fill=(246, 220, 120))
    save("quilt", im, nrm)


def playmat(w=1024, h=910):
    """遊びのマット（1.8 x 1.6m）：緑の地に道路と家と川"""
    img = Image.new("RGB", (w, h), (150, 196, 120))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w - 1, h - 1], outline=(90, 130, 190), width=18)
    for y in (240, 640):
        d.rectangle([40, y - 40, w - 40, y + 40], fill=(110, 110, 116))
        for x in range(60, w - 60, 70):
            d.rectangle([x, y - 4, x + 36, y + 4], fill=(250, 250, 240))
    d.rectangle([460, 60, 540, h - 60], fill=(110, 110, 116))
    d.polygon([(700, 330), (1000, 360), (990, 560), (720, 540)], fill=(110, 170, 220))
    rnd = random.Random(2721)
    for (x, y) in ((150, 380), (280, 400), (620, 80), (780, 80), (140, 760), (650, 740)):
        c = rnd.choice([(230, 120, 110), (240, 200, 110), (140, 170, 230)])
        d.rectangle([x, y, x + 90, y + 70], fill=c)
        d.polygon([(x - 10, y), (x + 45, y - 45), (x + 100, y)], fill=(170, 80, 70))
    for k in range(10):
        x, y = rnd.uniform(60, w - 60), rnd.uniform(60, h - 60)
        d.ellipse([x - 20, y - 20, x + 20, y + 20], fill=(90, 150, 80))
    a = np.asarray(img).astype(float) * (0.92 + fbm(w, 0.3, 2722, m=h)[..., None] * 0.12)
    save("playmat", rgb(a), normal_map(fbm(w, 0.3, 2722, m=h), 1.5))


def drawings(w=1536, h=512):
    """クレヨン画3枚（アトラス）：手をつなぐ父と子・ロケット・犬"""
    img = Image.new("RGB", (w, h), (248, 246, 236))
    d = ImageDraw.Draw(img)
    rnd = random.Random(2731)

    def cl(pts, col, width=6):
        for a, b in zip(pts, pts[1:]):
            for k in range(2):
                j = rnd.uniform(-2, 2)
                d.line([(a[0] + j, a[1] + j), (b[0] + j, b[1] - j)], fill=col, width=width - k * 2)
    for k in range(3):
        d.line([(k * 512, 0), (k * 512, h)], fill=(210, 206, 196), width=6)
    # 1: 父と子（手をつなぐ）
    for cx, s, col in ((160, 1.3, (60, 90, 170)), (320, 0.8, (230, 120, 60))):
        r = 34 * s
        d.ellipse([cx - r, 150 - 40 * s, cx + r, 150 - 40 * s + 2 * r], outline=(120, 80, 60), width=5)
        cl([(cx, 150 - 40 * s + 2 * r), (cx - 60 * s, 420), (cx + 60 * s, 420), (cx, 150 - 40 * s + 2 * r)], col, 7)
    cl([(205, 300), (270, 330)], (120, 80, 60), 5)
    d.text((60, 440), "おとうさんと", font=hand(40), fill=(60, 60, 150))
    # 2: ロケット
    cl([(768, 60), (840, 200), (840, 380), (696, 380), (696, 200), (768, 60)], (200, 60, 60), 8)
    d.ellipse([740, 220, 796, 276], outline=(60, 120, 200), width=6)
    cl([(696, 380), (650, 440)], (240, 150, 40), 7); cl([(840, 380), (886, 440)], (240, 150, 40), 7)
    for k in range(6):
        d.ellipse([560 + k * 60, 60 + (k % 2) * 30, 572 + k * 60, 72 + (k % 2) * 30], fill=(240, 200, 60))
    d.text((580, 450), "うちゅうへいく", font=hand(36), fill=(60, 60, 150))
    # 3: 犬
    d.ellipse([1150, 200, 1350, 360], outline=(140, 90, 50), width=7)
    d.ellipse([1300, 150, 1420, 260], outline=(140, 90, 50), width=7)
    d.ellipse([1380, 180, 1396, 196], fill=(40, 40, 40))
    cl([(1180, 360), (1170, 430)], (140, 90, 50), 6); cl([(1320, 360), (1330, 430)], (140, 90, 50), 6)
    cl([(1150, 260), (1100, 220)], (140, 90, 50), 6)
    d.text((1120, 450), "ポチ", font=hand(44), fill=(60, 60, 150))
    save("drawings", img)


def plan():
    """書きかけの治療計画書（初版・手書き。余白いっぱいの計算式と、何度も消した跡）"""
    w, h = 740, 1040
    img = Image.new("RGB", (w, h), (238, 232, 214))
    d = ImageDraw.Draw(img)
    d.text((50, 50), "治療計画書（初版）", font=_font(46, mincho=True), fill=(30, 30, 30))
    d.text((50, 130), "対象: 小川", font=hand(36), fill=(30, 30, 60))
    d.rectangle([250, 134, 330, 170], fill=(120, 120, 130))
    d.text((340, 130), "（当時7歳）", font=hand(36), fill=(30, 30, 60))
    d.text((50, 190), "起案: 小川 暁", font=hand(36), fill=(30, 30, 60))
    d.text((50, 280), "「必ず、もう一度あの声を聞く」", font=hand(40), fill=(30, 30, 90))
    rnd = random.Random(2741)
    f = hand(24)
    for i in range(14):
        y = 380 + i * 44
        s = "".join(rnd.choice("∑∫λΔψ0123456789=+−×/()xyz") for _ in range(rnd.randint(10, 24)))
        d.text((50 + rnd.uniform(0, 60), y), s, font=f, fill=(60, 60, 80))
        if rnd.random() < 0.4:
            y2 = y + 14
            d.line([(40, y2), (w - 60, y2 + rnd.uniform(-6, 6))], fill=(170, 160, 150), width=10)   # 消しゴムの跡
    a = np.asarray(img).astype(float) * (0.9 + fbm(w, 1.0, 2742, m=h)[..., None] * 0.12)
    save("plan", rgb(a))


def height_chart(w=256, h=1024):
    """身長計のシール（キリンの柄、1.1m のところに「7さい」の印）"""
    img = Image.new("RGB", (w, h), (250, 226, 150))
    d = ImageDraw.Draw(img)
    rnd = random.Random(2751)
    for k in range(30):
        x, y = rnd.uniform(10, w - 10), rnd.uniform(10, h - 10)
        d.ellipse([x - 16, y - 12, x + 16, y + 12], fill=(200, 140, 70))
    for k in range(0, 13):
        y = h - k * (h / 13)
        d.line([(0, y), (60, y)], fill=(80, 60, 40), width=3)
        d.text((66, y - 18), f"{60 + k * 5}", font=_font(22, mincho=False), fill=(80, 60, 40))
    # 110cm に赤い線と「7さい」
    y = h - 10 * (h / 13)
    d.line([(0, y), (w, y)], fill=(210, 50, 50), width=6)
    d.text((110, y - 40), "7さい", font=hand(34), fill=(210, 50, 50))
    save("height_chart", img)


def sky_morning(w=2048, h=640):
    """窓の外（幅16m x 高さ5m）：白くやわらかい朝の空と、遠くの屋根と木々"""
    y = np.mgrid[0:h, 0:w][0] / h
    top = np.array([180, 210, 240]); low = np.array([250, 250, 246])
    col = top + (low - top) * np.clip(y / 0.7, 0, 1)[..., None]
    clouds = np.clip(fbm(w, 1.8, 2761, ax=4.0, ay=1.0, m=h) - 0.45, 0, 1)[..., None] * 160 * np.clip(1.1 - y[..., None] * 1.2, 0, 1)
    col = np.clip(col + clouds, 0, 255)
    img = rgb(col)
    d = ImageDraw.Draw(img)
    rnd = random.Random(2762)
    x = 0
    while x < w:
        bw = rnd.randint(120, 260); bh = rnd.randint(60, 140)
        d.rectangle([x, h - bh, x + bw, h], fill=(200, 196, 190))
        d.polygon([(x - 10, h - bh), (x + bw / 2, h - bh - 50), (x + bw + 10, h - bh)], fill=(170, 120, 110))
        x += bw + rnd.randint(40, 120)
    for k in range(14):
        cx, cy, r = rnd.uniform(0, w), h - rnd.uniform(60, 160), rnd.uniform(50, 90)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(150, 190, 140))
    a = np.asarray(img.filter(ImageFilter.GaussianBlur(1.5))).astype(float)
    a = a * 0.8 + 255 * 0.2                        # 白く霞ませる（記憶ではなく祈りの光）
    save("sky_morning", rgb(a))


if __name__ == "__main__":
    wallpaper(); quilt(); playmat(); drawings(); plan(); height_chart(); sky_morning()
    img, nrm = planks(1024, 2771, (196, 178, 150), (236, 224, 200), count=7); save("floor", img, nrm)
    img, nrm = fabric(1024, 2772, (248, 248, 244), weave=280); save("curtain", img, nrm)
