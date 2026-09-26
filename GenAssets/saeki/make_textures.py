# -*- coding: utf-8 -*-
"""佐伯の自宅（saeki_home）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/saeki/make_textures.py

出力: GenAssets/saeki/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/SaekiHome/*.png
本棚の本は書斎（study）の背表紙アトラスを使う。
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

save = Saver(HERE, "SaekiHome")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


def wallpaper(n=1024):
    """温かいベージュの織物調の壁紙（1枚 = 0.6m）。ごく細い地模様"""
    y, x = np.mgrid[0:n, 0:n] / n
    weave = fbm(n, 0.35, 801, ax=1.0, ay=3.0) * 0.5 + fbm(n, 0.35, 802, ax=3.0, ay=1.0) * 0.5
    motif = (np.sin(2 * np.pi * x * 8) * np.sin(2 * np.pi * y * 8)) ** 2
    blot = fbm(n, 2.5, 803)
    base = np.array([208, 192, 164], dtype=float)
    lum = 1.0 + (weave - 0.5) * 0.08 + motif * 0.03 + (blot - 0.5) * 0.06
    save("wallpaper", rgb(base[None, None, :] * lum[..., None]), normal_map(weave * 0.8 + motif * 0.2, 1.5))


def rug(w=1024, h=853):
    """絨毯（2.4 x 2.0m）：縁取りと幾何学模様、使い込まれた色あせ"""
    img = Image.new("RGB", (w, h), (120, 40, 36))
    d = ImageDraw.Draw(img)
    d.rectangle([30, 30, w - 30, h - 30], outline=(40, 44, 70), width=36)
    d.rectangle([80, 80, w - 80, h - 80], outline=(190, 160, 110), width=8)
    cx, cy = w / 2, h / 2
    for k in range(4, 0, -1):
        s = k * 70
        d.polygon([(cx, cy - s), (cx + s * 1.3, cy), (cx, cy + s), (cx - s * 1.3, cy)],
                  fill=[(150, 60, 50), (40, 44, 70), (190, 160, 110), (120, 40, 36)][k % 4])
    rnd = random.Random(811)
    for i in range(14):
        x = 120 + i * 57
        for y in (130, h - 130):
            d.polygon([(x, y - 16), (x + 16, y), (x, y + 16), (x - 16, y)], fill=(190, 160, 110))
    a = np.asarray(img).astype(float)
    pile = fbm(w, 0.2, 812, m=h)[..., None]
    wear = fbm(w, 2.4, 813, m=h)[..., None]
    a = a * (0.85 + pile * 0.2) * (0.9 + wear * 0.15) + wear * 12
    save("rug", rgb(a), normal_map(pile[..., 0], 2.0))


def sunset(w=2048, h=640):
    """窓の外（幅16m x 高さ5m の背景板）：夕焼けの空と、黒い家並みのシルエット（自発光で使う）"""
    y = np.mgrid[0:h, 0:w][0] / h; x = np.mgrid[0:h, 0:w][1] / w
    top = np.array([60, 50, 90]); mid = np.array([230, 120, 60]); low = np.array([255, 190, 110])
    t = y[..., None]
    col = np.where(t < 0.5, top + (mid - top) * (t / 0.5), mid + (low - mid) * ((t - 0.5) / 0.5))
    sun = np.exp(-(((x - 0.55) * 3.2) ** 2 + ((y - 0.45) * 1.0) ** 2) / 0.0025)[..., None] * np.array([255, 230, 180])
    glow = np.exp(-(((x - 0.55) * 3.2) ** 2 + ((y - 0.45) * 1.0) ** 2) / 0.03)[..., None] * np.array([80, 40, 10])
    clouds = np.clip(fbm(w, 1.8, 821, ax=5.0, ay=1.0, m=h) - 0.5, 0, 1)[..., None] * np.clip(1.1 - t * 1.4, 0, 1) * np.array([140, 70, 70]) * 1.6
    col = col + sun * 0.8 + glow + clouds
    col = col + np.random.default_rng(824).uniform(-1.5, 1.5, col.shape)          # 階調の縞を消す
    img = rgb(col)
    d = ImageDraw.Draw(img)
    rnd = random.Random(822)
    xx = 0
    while xx < w:
        bw = rnd.randint(70, 200); bh = rnd.randint(90, 240)
        d.rectangle([xx, h - bh, xx + bw, h], fill=(22, 16, 22))
        if rnd.random() < 0.55:
            d.polygon([(xx - 6, h - bh), (xx + bw / 2, h - bh - rnd.randint(30, 70)), (xx + bw + 6, h - bh)], fill=(22, 16, 22))
        if rnd.random() < 0.3:
            d.rectangle([xx + bw * 0.7, h - bh - 50, xx + bw * 0.7 + 12, h - bh], fill=(22, 16, 22))     # 煙突
        for k in range(rnd.randint(0, 3)):
            wx, wy = xx + rnd.randint(10, max(11, bw - 24)), h - bh + rnd.randint(20, max(21, bh - 30))
            d.rectangle([wx, wy, wx + 12, wy + 14], fill=(255, 190, 110))
        xx += bw + rnd.randint(0, 14)
    # 電柱と電線
    for px in (300, 1250, 1900):
        d.rectangle([px, h - 420, px + 9, h], fill=(26, 18, 24))
        d.rectangle([px - 40, h - 400, px + 49, h - 394], fill=(26, 18, 24))
    for k, dy in enumerate((0, 10)):
        pts = [(0, h - 380 + dy)]
        for px0, px1 in ((0, 300), (300, 1250), (1250, 1900), (1900, w)):
            for i in range(1, 21):
                u = i / 20
                sag = 26 * math.sin(math.pi * u) * (px1 - px0) / 900
                pts.append((px0 + (px1 - px0) * u, h - 397 + dy + sag))
        d.line(pts, fill=(30, 20, 26), width=2)
    save("sunset", img)


def letter():
    """妻の書きかけの手紙（便箋、縦書きの罫、途中で終わる）"""
    w, h = 740, 1040
    img = Image.new("RGB", (w, h), (244, 240, 228))
    d = ImageDraw.Draw(img)
    for i in range(14):
        x = w - 70 - i * 46
        d.line([(x, 60), (x, h - 60)], fill=(200, 160, 160), width=2)
    f = hand(34)
    cols = ["お母さんへ。", "恒一さんのことで相談があります。", "最近、帰りがとても遅いんです。", "何を聞いても『大丈夫だ』としか──",
            "昨日は夜中に、書斎で一人で", "誰かに謝っているのが聞こえました"]
    for i, s in enumerate(cols):
        x = w - 100 - i * 92
        for j, ch in enumerate(s):
            d.text((x, 80 + j * 38), ch, font=f, fill=(40, 40, 80))
    save("letter", img)


def plog():
    """佐伯の私的ログ（ノートの最終頁。横罫に万年筆、最後の行が乱れる）"""
    w, h = 740, 1040
    img = Image.new("RGB", (w, h), (240, 238, 228))
    d = ImageDraw.Draw(img)
    for i in range(24):
        d.line([(40, 90 + i * 38), (w - 40, 90 + i * 38)], fill=(170, 190, 210), width=2)
    f = hand(30)
    lines = ["所長は動かない。黒田さんは正論しか言わない。", "なら、私が直接あの人と話すしかない。", "悪い人ではないんだ。ただ、追い詰められている。",
             "今夜、解析室で会う約束をした。", "", "……何をするつもりですか？"]
    rnd = random.Random(831)
    for i, s in enumerate(lines):
        y = 60 + i * 76
        x = 60
        for ch in s:
            jit = rnd.uniform(-5, 5) if i == 5 else rnd.uniform(-1, 1)
            d.text((x, y + jit), ch, font=f, fill=(20, 30, 80))
            x += 31 + (rnd.uniform(-2, 6) if i == 5 else 0)
    save("plog", img)


def unsent():
    """宛先のないメモ（小さなメモ用紙）"""
    w, h = 600, 420
    img = Image.new("RGB", (w, h), (246, 236, 190))
    d = ImageDraw.Draw(img)
    f = hand(30)
    for i, s in enumerate(["あなたに伝えたいことがある。", "ここから先へ行けば、", "もう戻れなくなる。", "娘さんのことは、別の道を", "一緒に探せるはずだ"]):
        d.text((36, 40 + i * 64), s, font=f, fill=(30, 30, 30))
    save("unsent", img)


def painting(w=900, h=640):
    """居間の額の絵（海と岬の油彩風）"""
    y = np.mgrid[0:h, 0:w][0] / h; x = np.mgrid[0:h, 0:w][1] / w
    sky = np.array([170, 190, 200]) * (1 - y[..., None]) + np.array([230, 210, 180]) * y[..., None]
    sea = np.array([60, 90, 110])
    col = np.where((y > 0.55)[..., None], sea * (0.9 + fbm(w, 1.0, 841, m=h)[..., None] * 0.3), sky)
    cliff = (y > 0.35 + 0.25 * (x - 0.1) ** 0.5) & (x < 0.45) & (y > 0.3)
    col = np.where(cliff[..., None], np.array([70, 80, 50]) * (0.8 + fbm(w, 1.4, 842, m=h)[..., None] * 0.4), col)
    stroke = fbm(w, 0.8, 843, ax=0.3, ay=1.0, m=h)[..., None]
    col = col * (0.9 + stroke * 0.2)
    save("painting", rgb(col))


def photo_frame(w=480, h=360):
    """写真立ての写真（夫婦のシルエット。顔は光で飛んでいる）"""
    img = Image.new("RGB", (w, h), (200, 190, 170))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 200, w, h], fill=(110, 130, 110))
    for cx, col in ((170, (60, 60, 70)), (300, (150, 90, 90))):
        d.ellipse([cx - 32, 70, cx + 32, 140], fill=(235, 225, 210))
        d.polygon([(cx - 70, h), (cx - 45, 150), (cx + 45, 150), (cx + 70, h)], fill=col)
    a = np.asarray(img.filter(ImageFilter.GaussianBlur(2))).astype(float)
    a = a * 0.85 + 30
    save("photo", rgb(a))


if __name__ == "__main__":
    wallpaper()
    img, nrm = planks(1024, 851, (92, 64, 40), (168, 124, 84)); save("floor", img, nrm)
    img, nrm = wood(1024, 861, (70, 46, 30), (140, 100, 66), rings=6); save("wood", img, nrm)
    img, nrm = fabric(1024, 871, (118, 108, 100)); save("sofa", img, nrm)
    img, nrm = fabric(1024, 881, (224, 216, 198), weave=320); save("linen", img, nrm)
    rug(); sunset(); letter(); plog(); unsent(); painting(); photo_frame()
