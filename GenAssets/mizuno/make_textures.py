# -*- coding: utf-8 -*-
"""水野のアパート（mizuno_apart）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/mizuno/make_textures.py

出力: GenAssets/mizuno/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/MizunoApart/*.png
奥の病室は臨床病棟（ward）の床・壁・寝具・カーテンと研究所の天井板を使う。
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

save = Saver(HERE, "MizunoApart")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


def wallpaper(n=1024):
    """白いビニールクロス（1枚 = 1m）。細かい石目のエンボス"""
    emb = fbm(n, 0.2, 1401) * 0.6 + fbm(n, 0.6, 1402) * 0.4
    mott = fbm(n, 2.2, 1403)
    base = np.array([238, 236, 230], dtype=float)
    lum = 1.0 + (emb - 0.5) * 0.06 + (mott - 0.5) * 0.03
    save("wallpaper", rgb(base[None, None, :] * lum[..., None]), normal_map(emb, 2.0))


def duvet(n=1024):
    """掛け布団カバー（1枚 = 0.5m）：淡い桃色の地に小花柄"""
    img, nrm = fabric(n, 1411, (236, 214, 214), weave=300)
    im = Image.fromarray(np.asarray(img)).convert("RGB")
    d = ImageDraw.Draw(im)
    rnd = random.Random(1412)
    for i in range(6):
        for j in range(6):
            cx = (i + 0.5 + (j % 2) * 0.5) * n / 6 + rnd.uniform(-10, 10)
            cy = (j + 0.5) * n / 6 + rnd.uniform(-10, 10)
            for k in range(5):
                a = math.pi * 2 * k / 5
                px, py = cx + math.cos(a) * 16, cy + math.sin(a) * 16
                for ox in (-n, 0, n):
                    for oy in (-n, 0, n):
                        d.ellipse([px - 11 + ox, py - 11 + oy, px + 11 + ox, py + 11 + oy], fill=(246, 246, 240))
            for ox in (-n, 0, n):
                for oy in (-n, 0, n):
                    d.ellipse([cx - 6 + ox, cy - 6 + oy, cx + 6 + ox, cy + 6 + oy], fill=(240, 200, 110))
            lx, ly = cx + 30, cy + 22
            for ox in (-n, 0, n):
                for oy in (-n, 0, n):
                    d.ellipse([lx - 14 + ox, ly - 6 + oy, lx + 14 + ox, ly + 6 + oy], fill=(170, 200, 160))
    save("duvet", im, nrm)


def fusuma_paper(n=1024):
    """襖紙（1枚 = 0.9m）：生成りに雲の地紋と金の砂子"""
    y, x = np.mgrid[0:n, 0:n] / n
    cloud = fbm(n, 2.0, 1421, ax=3.0, ay=1.0)
    fib = fbm(n, 0.15, 1422)
    base = np.array([226, 214, 186], dtype=float)
    lum = 1.0 + (np.clip(cloud - 0.55, 0, 1) * 0.35) + (fib - 0.5) * 0.05
    col = base[None, None, :] * lum[..., None]
    rnd = np.random.default_rng(1423)
    flecks = (rnd.random((n, n)) > 0.9985).astype(float)
    flecks = np.asarray(Image.fromarray((flecks * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.9))).astype(float) / 255
    col = col * (1 - flecks[..., None]) + np.array([214, 176, 90]) * flecks[..., None]
    save("fusuma_paper", rgb(col), normal_map(fib, 0.8))


def lace(n=512):
    """レースのカーテン（抜き：アルファ）"""
    y, x = np.mgrid[0:n, 0:n] / n
    m = (np.sin(2 * np.pi * x * 24) * np.sin(2 * np.pi * y * 24)) ** 2
    flower = np.zeros((n, n))
    for cx, cy in ((0.25, 0.25), (0.75, 0.75)):
        r = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
        a = np.arctan2(y - cy, x - cx)
        flower = np.maximum(flower, ((r < 0.12 + 0.04 * np.cos(a * 6)) & (r > 0.03)).astype(float))
    alpha = np.clip(0.28 + m * 0.3 + flower * 0.45, 0, 1)          # 半透明で描く（抜きにすると細かい網目が消える）
    col = np.zeros((n, n, 4), np.uint8)
    col[..., 0:3] = 248
    col[..., 3] = (alpha * 255).astype(np.uint8)
    save("lace", Image.fromarray(col, "RGBA"))


def night_city(w=2048, h=640):
    """窓の外（夜。幅16m x 高さ5m）：紺の空と、灯りのともるアパートの窓"""
    y = np.mgrid[0:h, 0:w][0] / h
    top = np.array([10, 14, 32]); low = np.array([40, 44, 70])
    col = top + (low - top) * y[..., None]
    col = col + np.random.default_rng(1431).uniform(-1.2, 1.2, col.shape)
    img = rgb(col)
    d = ImageDraw.Draw(img)
    rnd = random.Random(1432)
    x = 0
    while x < w:
        bw = rnd.randint(160, 360); bh = rnd.randint(220, 460)
        top_y = h - bh
        d.rectangle([x, top_y, x + bw, h], fill=(20, 22, 30))
        # ベランダの手すりの列と窓
        for fy in range(top_y + 20, h, 46):
            for fx in range(x + 14, x + bw - 30, 44):
                if rnd.random() < 0.35:
                    c = rnd.choice([(255, 214, 150), (250, 236, 200), (190, 220, 255)])
                    d.rectangle([fx, fy, fx + 26, fy + 22], fill=c)
                    if rnd.random() < 0.5:
                        d.rectangle([fx, fy, fx + 12, fy + 22], fill=(c[0] // 2, c[1] // 2, c[2] // 2))   # カーテンの影
            d.line([(x, fy + 30), (x + bw, fy + 30)], fill=(34, 36, 46), width=3)
        x += bw + rnd.randint(10, 60)
    # 電柱と電線
    d.rectangle([620, 120, 630, h], fill=(8, 8, 12))
    d.line([(0, 150), (625, 140), (w, 170)], fill=(8, 8, 12), width=2)
    save("night_city", img)


def cork_board(w=700, h=500):
    """コルクボード（0.7 x 0.5m）：研究室の集合写真・押し花・メモ・チケットの半券"""
    a = fbm(w, 0.2, 1441, m=h)[..., None] * 0.5 + fbm(w, 0.05, 1442, m=h)[..., None] * 0.5
    col = np.array([176, 132, 84]) * (0.75 + a * 0.5)
    img = rgb(col)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w - 1, h - 1], outline=(120, 90, 60), width=14)
    # 集合写真（5人、1人の顔は滲む）
    ph = Image.new("RGB", (240, 170), (200, 210, 214))
    pd = ImageDraw.Draw(ph)
    pd.rectangle([0, 110, 240, 170], fill=(150, 160, 170))
    for k in range(5):
        cx = 30 + k * 45
        pd.ellipse([cx - 12, 50, cx + 12, 76], fill=(232, 210, 190))
        pd.rectangle([cx - 18, 76, cx + 18, 150], fill=[(70, 80, 110), (230, 230, 230), (90, 60, 60), (230, 230, 230), (60, 90, 80)][k])
    ph = ph.filter(ImageFilter.GaussianBlur(1.2))
    img.paste(ph.rotate(-4, expand=True, fillcolor=(176, 132, 84)), (40, 40))
    d.text((60, 225), "第8研究室 歓迎会", font=hand(22), fill=(40, 40, 40))
    # 押し花
    d.rectangle([330, 50, 470, 230], fill=(246, 244, 236))
    for k in range(5):
        a_ = math.pi * 2 * k / 5
        d.ellipse([400 + math.cos(a_) * 22 - 14, 120 + math.sin(a_) * 22 - 14, 400 + math.cos(a_) * 22 + 14, 120 + math.sin(a_) * 22 + 14], fill=(170, 150, 200))
    d.line([(400, 130), (395, 215)], fill=(90, 130, 80), width=3)
    # メモ（黄色い付箋）
    d.rectangle([500, 60, 660, 200], fill=(250, 232, 120))
    for i, s in enumerate(["学会発表", "10/3", "スライド直す"]):
        d.text((515, 75 + i * 38), s, font=hand(28), fill=(40, 40, 60))
    # チケットの半券と、手書きのメモ
    d.rectangle([80, 300, 300, 380], fill=(230, 120, 110))
    d.text((95, 318), "水族館  大人 1", font=_font(26, mincho=False), fill=(255, 255, 255))
    d.rectangle([360, 280, 640, 450], fill=(250, 250, 246))
    for i, s in enumerate(["眠っている人を", "起こす仕事をする。", "　　　― 18歳の私へ"]):
        d.text((375, 295 + i * 44), s, font=hand(30), fill=(30, 50, 120))
    # 画鋲
    for (px, py) in ((160, 44), (400, 54), (580, 64), (190, 304), (500, 284)):
        d.ellipse([px - 9, py - 9, px + 9, py + 9], fill=(200, 40, 40))
    save("cork_board", img)


def calendar(w=600, h=840):
    """壁掛けカレンダー（9月。14日に小さな丸）"""
    img = Image.new("RGB", (w, h), (250, 250, 246))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 380], fill=(120, 170, 200))
    for k in range(9):
        d.ellipse([60 + k * 60, 160 + (k % 2) * 60, 110 + k * 60, 210 + (k % 2) * 60], fill=(180, 210, 230))
    d.text((40, 400), "9", font=_font(90, mincho=False), fill=(40, 40, 40))
    d.text((120, 440), "September", font=_font(40, mincho=False), fill=(80, 80, 80))
    f = _font(30, mincho=False)
    for i, s in enumerate("日月火水木金土"):
        d.text((40 + i * 78, 520), s, font=f, fill=(200, 60, 60) if i == 0 else (60, 60, 60))
    day = 1
    start = 1
    for r in range(5):
        for c in range(7):
            k = r * 7 + c
            if k < start or day > 30:
                continue
            x, y = 40 + c * 78, 570 + r * 52
            d.text((x, y), str(day), font=f, fill=(200, 60, 60) if c == 0 else (40, 40, 40))
            if day == 14:
                d.ellipse([x - 10, y - 6, x + 44, y + 42], outline=(200, 50, 50), width=3)
            day += 1
    save("calendar", img)


def diary():
    """水野の日記（開いた頁。左：3月2日、右：9月14日と最終頁）"""
    w, h = 1480, 1040
    img = Image.new("RGB", (w, h), (246, 242, 230))
    d = ImageDraw.Draw(img)
    d.line([(w // 2, 0), (w // 2, h)], fill=(210, 200, 186), width=6)
    for side in (0, 1):
        for i in range(18):
            y = 90 + i * 52
            d.line([(40 + side * w // 2, y), (w // 2 - 40 + side * w // 2, y)], fill=(200, 210, 226), width=2)
    f = hand(34)
    left = ["3月2日", "誕生日。同期の子がケーキをくれた。29歳", "", "", "9月14日", "叔父さんの命日。今年も花を持っていった。",
            "叔父さんが倒れてから、ずっと決めていた。", "眠っている人を起こす仕事をするって"]
    right = ["最近、あの人の質問が怖い。", "『提供体はどこから来るんですか』って、", "どうしてそんなことばかり聞くんだろう", "", "",
             "……信じたい。あの人はきっと、", "娘さんを助けたいだけの、", "普通のお父さんのはず"]
    for i, s in enumerate(left):
        d.text((50, 50 + i * 52), s, font=f, fill=(30, 40, 110))
    for i, s in enumerate(right):
        d.text((w // 2 + 50, 50 + i * 52), s, font=f, fill=(30, 40, 110))
    save("diary", img)


def laptop_screen(w=640, h=400):
    """ノートPCのログイン画面（ヒント欄：忘れられない日）"""
    y = np.mgrid[0:h, 0:w][0] / h
    col = np.array([30, 50, 90]) + (np.array([80, 120, 170]) - np.array([30, 50, 90])) * y[..., None]
    img = rgb(col)
    d = ImageDraw.Draw(img)
    d.ellipse([w / 2 - 44, 70, w / 2 + 44, 158], fill=(220, 226, 236))
    d.ellipse([w / 2 - 18, 88, w / 2 + 18, 124], fill=(150, 160, 180))
    d.text((w / 2 - 60, 170), "Mizuno", font=_font(34, mincho=False), fill=(255, 255, 255))
    d.rectangle([w / 2 - 150, 230, w / 2 + 150, 270], fill=(240, 242, 246))
    d.text((w / 2 - 140, 236), "パスワード（4桁）", font=_font(22, mincho=False), fill=(150, 150, 160))
    d.text((w / 2 - 150, 290), "ヒント：忘れられない日", font=_font(24, mincho=False), fill=(230, 236, 246))
    save("laptop_screen", img)


def photo(w=480, h=360):
    """写真立て：病院の中庭で、制服の少女と車椅子の男性（顔は光で飛んでいる）"""
    img = Image.new("RGB", (w, h), (200, 214, 200))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 220, w, h], fill=(150, 170, 140))
    d.ellipse([140, 90, 200, 150], fill=(236, 226, 214))
    d.rectangle([120, 150, 220, 290], fill=(90, 110, 140))
    d.ellipse([110, 250, 230, 350], outline=(60, 60, 60), width=6)
    d.ellipse([290, 60, 346, 118], fill=(236, 226, 214))
    d.polygon([(270, 330), (286, 118), (350, 118), (366, 330)], fill=(40, 50, 80))
    a = np.asarray(img.filter(ImageFilter.GaussianBlur(2.2))).astype(float) * 0.85 + 30
    save("photo", rgb(a))


if __name__ == "__main__":
    wallpaper(); duvet(); fusuma_paper(); lace(); night_city(); cork_board(); calendar(); diary(); laptop_screen(); photo()
    img, nrm = planks(1024, 1451, (150, 112, 74), (206, 164, 116), count=6); save("flooring", img, nrm)
    img, nrm = fabric(1024, 1452, (170, 196, 170), weave=90, slub=0.4); save("rug", img, nrm)
    img, nrm = fabric(1024, 1453, (236, 218, 160), weave=240); save("drape", img, nrm)
    img, nrm = fabric(1024, 1454, (200, 180, 170), weave=200); save("cushion", img, nrm)
