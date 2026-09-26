# -*- coding: utf-8 -*-
"""黒田の自宅（kuroda_home）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/kuroda/make_textures.py

出力: GenAssets/kuroda/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/KurodaHome/*.png
"""
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, normal_map, rgb, colorize, _font, Saver, planks, wood, fabric  # noqa: E402

save = Saver(HERE, "KurodaHome")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


def wallpaper(n=1024):
    """淡い若草色の細い縦縞の壁紙（1枚 = 0.5m）"""
    y, x = np.mgrid[0:n, 0:n] / n
    stripe = (np.sin(2 * np.pi * x * 10) > 0.6).astype(float)
    emb = fbm(n, 0.2, 2101)
    mott = fbm(n, 2.2, 2102)
    base = np.array([226, 230, 212], dtype=float)
    lum = 1.0 + stripe * 0.03 + (emb - 0.5) * 0.05 + (mott - 0.5) * 0.04
    save("wallpaper", rgb(base[None, None, :] * lum[..., None]), normal_map(emb * 0.8 + stripe * 0.2, 1.4))


def kitchen_tile(n=1024):
    """台所の壁の白いタイル（1枚 = 0.5m、10cm角 x 5、目地）"""
    y, x = np.mgrid[0:n, 0:n] / n
    gx = np.minimum((x * 5) % 1, 1 - (x * 5) % 1); gy = np.minimum((y * 5) % 1, 1 - (y * 5) % 1)
    grout = (np.minimum(gx, gy) < 0.035).astype(float)
    glaze = fbm(n, 1.2, 2111)
    base = np.array([240, 240, 236], dtype=float)
    lum = 1.0 + (glaze - 0.5) * 0.04 - grout * 0.2
    col = base[None, None, :] * lum[..., None]
    col = col * (1 - grout[..., None]) + np.array([180, 176, 166]) * grout[..., None]
    edge = np.clip(np.minimum(gx, gy) / 0.08, 0, 1)
    save("kitchen_tile", rgb(col), normal_map(edge - grout, 2.0))


def rug(w=1024, h=866):
    """食卓の下のラグ（2.6 x 2.2m）：紺の縁取りと小さな菱形の連続模様"""
    img = Image.new("RGB", (w, h), (196, 176, 148))
    d = ImageDraw.Draw(img)
    d.rectangle([20, 20, w - 20, h - 20], outline=(50, 62, 96), width=30)
    d.rectangle([70, 70, w - 70, h - 70], outline=(160, 70, 60), width=6)
    for yy in range(120, h - 100, 60):
        for xx in range(120 + (yy // 60 % 2) * 30, w - 100, 60):
            d.polygon([(xx, yy - 10), (xx + 10, yy), (xx, yy + 10), (xx - 10, yy)], fill=(170, 150, 122))
    a = np.asarray(img).astype(float)
    pile = fbm(w, 0.2, 2121, m=h)[..., None]
    wear = fbm(w, 2.4, 2122, m=h)[..., None]
    a = a * (0.86 + pile * 0.2) * (0.92 + wear * 0.12)
    save("rug", rgb(a), normal_map(pile[..., 0], 2.0))


def drawing(w=560, h=720):
    """冷蔵庫の子供の絵（クレヨン）：真ん中のお父さんが一番大きい。「おとうさんは　ただしい」"""
    img = Image.new("RGB", (w, h), (248, 246, 236))
    d = ImageDraw.Draw(img)
    rnd = random.Random(2131)

    def crayon_line(pts, col, width=6):
        for a, b in zip(pts, pts[1:]):
            for k in range(3):
                jx, jy = rnd.uniform(-2, 2), rnd.uniform(-2, 2)
                d.line([(a[0] + jx, a[1] + jy), (b[0] + jx, b[1] + jy)], fill=col, width=width - k * 2)
    # 太陽と地面
    d.ellipse([440, 30, 530, 120], outline=(240, 150, 40), width=8)
    for k in range(8):
        a = k * math.pi / 4
        crayon_line([(485 + math.cos(a) * 55, 75 + math.sin(a) * 55), (485 + math.cos(a) * 80, 75 + math.sin(a) * 80)], (240, 150, 40), 5)
    crayon_line([(20, 560), (540, 555)], (90, 170, 70), 8)
    # 家族（母・父・娘ふたり）
    people = [(110, 330, 0.8, (220, 80, 110)), (280, 250, 1.2, (60, 90, 170)), (410, 380, 0.6, (240, 170, 60)), (490, 400, 0.55, (120, 190, 90))]
    for cx, top, s, col in people:
        r = 40 * s
        crayon_line([(cx - r, top + r), (cx, top), (cx + r, top + r), (cx, top + 2 * r), (cx - r, top + r)], (120, 80, 60), 5)
        d.ellipse([cx - r * 0.35, top + r * 0.8, cx - r * 0.15, top + r], fill=(40, 40, 40))
        d.ellipse([cx + r * 0.15, top + r * 0.8, cx + r * 0.35, top + r], fill=(40, 40, 40))
        crayon_line([(cx - r * 0.4, top + r * 1.4), (cx, top + r * 1.6), (cx + r * 0.4, top + r * 1.4)], (200, 40, 40), 4)
        body_top = top + 2 * r
        crayon_line([(cx, body_top), (cx - 1.4 * r, 560), (cx + 1.4 * r, 560), (cx, body_top)], col, 7)
        crayon_line([(cx - 0.3 * r, body_top + 0.5 * r), (cx - 1.8 * r, body_top + 1.2 * r)], col, 6)
        crayon_line([(cx + 0.3 * r, body_top + 0.5 * r), (cx + 1.8 * r, body_top + 1.2 * r)], col, 6)
    f = hand(46)
    x = 60
    for ch in "おとうさんは":
        d.text((x, 600 + rnd.uniform(-6, 6)), ch, font=f, fill=(60, 60, 150)); x += 48 + rnd.uniform(-4, 6)
    x = 180
    for ch in "ただしい":
        d.text((x, 660 + rnd.uniform(-6, 6)), ch, font=f, fill=(60, 60, 150)); x += 50 + rnd.uniform(-4, 6)
    a = np.asarray(img).astype(float) * (0.96 + fbm(w, 0.3, 2132, m=h)[..., None] * 0.06)
    save("drawing", rgb(a))


def drawings_wall(w=1024, h=512):
    """壁に貼った子供の絵2枚（花と、猫）"""
    img = Image.new("RGB", (w, h), (246, 244, 234))
    d = ImageDraw.Draw(img)
    d.line([(w // 2, 0), (w // 2, h)], fill=(200, 196, 186), width=8)
    rnd = random.Random(2141)
    for k in range(5):
        cx, cy = 80 + k * 90, 260 + rnd.uniform(-30, 30)
        d.line([(cx, cy), (cx, 470)], fill=(80, 160, 70), width=6)
        for j in range(6):
            a = j * math.pi / 3
            d.ellipse([cx + math.cos(a) * 26 - 20, cy + math.sin(a) * 26 - 20, cx + math.cos(a) * 26 + 20, cy + math.sin(a) * 26 + 20],
                      outline=[(230, 90, 120), (240, 170, 40), (150, 90, 200)][k % 3], width=5)
    d.ellipse([640, 180, 880, 400], outline=(60, 60, 60), width=7)
    d.polygon([(660, 200), (690, 120), (720, 190)], outline=(60, 60, 60))
    d.polygon([(800, 190), (830, 120), (860, 200)], outline=(60, 60, 60))
    d.ellipse([700, 250, 720, 270], fill=(40, 40, 40)); d.ellipse([800, 250, 820, 270], fill=(40, 40, 40))
    d.text((620, 430), "たま", font=hand(48), fill=(60, 60, 150))
    save("drawings_wall", img)


def calendar(w=600, h=840):
    """家族のカレンダー（4月。子供の行事と、赤い丸の「お父さん 会議」）"""
    img = Image.new("RGB", (w, h), (250, 250, 246))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 300], fill=(240, 200, 200))
    for k in range(6):
        d.ellipse([60 + k * 80, 100 + (k % 2) * 50, 120 + k * 80, 160 + (k % 2) * 50], fill=(250, 230, 236))
    d.text((40, 320), "4", font=_font(90, mincho=False), fill=(40, 40, 40))
    d.text((120, 360), "April", font=_font(40, mincho=False), fill=(80, 80, 80))
    f = _font(28, mincho=False)
    day = 1
    for r in range(5):
        for c in range(7):
            if day > 30 or (r == 0 and c < 2):
                continue
            x, y = 40 + c * 78, 460 + r * 70
            d.text((x, y), str(day), font=f, fill=(200, 60, 60) if c == 0 else (40, 40, 40))
            if day == 10:
                d.text((x - 4, y + 32), "入学式", font=hand(18), fill=(200, 80, 120))
            if day == 18:
                d.ellipse([x - 8, y - 4, x + 40, y + 36], outline=(200, 50, 50), width=3)
                d.text((x - 10, y + 32), "会議", font=hand(18), fill=(200, 50, 50))
            if day == 21:
                d.text((x - 4, y + 32), "遠足", font=hand(18), fill=(60, 120, 60))
            day += 1
    save("calendar", img)


def family_photo(w=480, h=360):
    """家族写真（父・母・娘ふたり。遊園地の前）"""
    img = Image.new("RGB", (w, h), (170, 200, 230))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 230, w, h], fill=(150, 170, 120))
    d.ellipse([300, 40, 460, 200], outline=(230, 120, 120), width=10)
    for k, (cx, s, col) in enumerate(((110, 1.0, (200, 90, 110)), (190, 1.2, (60, 80, 130)), (270, 0.7, (240, 170, 60)), (330, 0.6, (120, 180, 90)))):
        d.ellipse([cx - 22 * s, 120 - 30 * s, cx + 22 * s, 120 + 14 * s], fill=(236, 220, 200))
        d.rectangle([cx - 28 * s, 120 + 14 * s, cx + 28 * s, 330], fill=col)
    a = np.asarray(img.filter(ImageFilter.GaussianBlur(1.6))).astype(float) * 0.9 + 20
    save("family_photo", rgb(a))


def notebook_rules():
    """黒田の手帳（開いた頁：規則と、娘たちのこと）"""
    w, h = 1000, 700
    img = Image.new("RGB", (w, h), (244, 242, 232))
    d = ImageDraw.Draw(img)
    d.line([(w // 2, 0), (w // 2, h)], fill=(200, 194, 180), width=6)
    for side in (0, 1):
        for i in range(12):
            d.line([(20 + side * w // 2, 60 + i * 52), (w // 2 - 20 + side * w // 2, 60 + i * 52)], fill=(200, 210, 222), width=2)
    f = hand(28)
    left = ["規則は人を縛るためにあるんじゃない。", "守るためにある。", "それを娘たちに教えられる", "父親でいたい"]
    right = ["正しいことと、救うことは", "同じではない。", "それでも私は、正しい方を選ぶ。", "誰かが選ばなければならない", "からだ"]
    for i, s in enumerate(left):
        d.text((30, 24 + i * 52), s, font=f, fill=(20, 20, 40))
    for i, s in enumerate(right):
        d.text((w // 2 + 30, 24 + i * 52), s, font=f, fill=(20, 20, 40))
    save("notebook_rules", img)


def notebook_blank():
    """白紙のページ（罫線だけ。判定で書き込む手帳）"""
    w, h = 800, 600
    img = Image.new("RGB", (w, h), (246, 244, 236))
    d = ImageDraw.Draw(img)
    d.line([(w // 2, 0), (w // 2, h)], fill=(200, 194, 180), width=6)
    for side in (0, 1):
        for i in range(11):
            d.line([(20 + side * w // 2, 50 + i * 50), (w // 2 - 20 + side * w // 2, 50 + i * 50)], fill=(200, 210, 222), width=2)
    d.text((30, 14), "4/19", font=hand(26), fill=(20, 20, 40))
    save("notebook_blank", img)


def recorder_lcd(w=256, h=128):
    img = Image.new("RGB", (w, h), (120, 140, 110))
    d = ImageDraw.Draw(img)
    d.text((10, 10), "REC 004", font=_font(34, mincho=False), fill=(20, 30, 20))
    d.text((10, 64), "ERR 破損", font=_font(34, mincho=False), fill=(20, 30, 20))
    save("recorder_lcd", img)


if __name__ == "__main__":
    wallpaper(); kitchen_tile(); rug(); drawing(); drawings_wall(); calendar(); family_photo()
    notebook_rules(); notebook_blank(); recorder_lcd()
    img, nrm = planks(1024, 2151, (110, 74, 46), (170, 124, 82), count=7); save("flooring", img, nrm)
    img, nrm = wood(1024, 2152, (150, 110, 70), (210, 170, 120), rings=5); save("wood", img, nrm)
    img, nrm = fabric(1024, 2153, (150, 120, 96), weave=220, slub=0.2); save("sofa", img, nrm)
    img, nrm = fabric(1024, 2154, (200, 190, 160), weave=260); save("curtain", img, nrm)
