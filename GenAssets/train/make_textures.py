# -*- coding: utf-8 -*-
"""電車車内のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/train/make_textures.py

出力: GenAssets/train/tex/*.png（Blenderの確認用）と
      project/Assets/EscapePrototype/Textures/HQ/Train/*.png（Unity用。_n が法線）
中吊りの研究所の広告（調べられる資料）は既存の Assets/Arts/Generated/ad_poster_jp.png をそのまま使う。
"""
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, normal_map, rgb, colorize, _font, Saver  # noqa: E402

save = Saver(HERE, "Train")


def melamine(n=1024):
    """クリーム色の化粧板（1枚 = 1m）。細かい粒と、ごく薄い経年のむら"""
    speck = fbm(n, 0.1, 101)
    mott = fbm(n, 2.4, 102)
    base = np.array([224, 216, 192], dtype=float)
    lum = 1.0 + (speck - 0.5) * 0.035 + (mott - 0.5) * 0.07
    col = base[None, None, :] * lum[..., None]
    col[..., 2] -= (mott - 0.5) * 10          # 黄ばみのむら
    save("melamine", rgb(col), normal_map(speck, 0.6))


def linoleum(n=1024):
    """灰色のリノリウム床（1枚 = 1m）。細かいチップ模様と擦れ"""
    rnd = np.random.default_rng(111)
    base = np.array([112, 114, 112], dtype=float)
    tone = fbm(n, 2.2, 112)
    scuff = fbm(n, 1.4, 113, ax=6.0, ay=1.0)                              # 進行方向（U）に伸びた擦れ
    img = base[None, None, :] * (0.94 + (tone - 0.5) * 0.12 + (scuff - 0.5) * 0.10)[..., None]
    chips = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(chips)
    for _ in range(9000):
        x, y = rnd.integers(0, n, 2)
        r = rnd.uniform(0.8, 2.6)
        v = int(rnd.choice([40, 200, 255]))
        for ox in (-n, 0, n):
            for oy in (-n, 0, n):
                d.ellipse([x + ox - r, y + oy - r, x + ox + r, y + oy + r], fill=v)
    c = np.asarray(chips).astype(float) / 255.0
    light = (c > 0.7).astype(float) * c
    dark = ((c > 0.1) & (c < 0.3)).astype(float)
    img = img * (1 + light[..., None] * 0.35 - dark[..., None] * 0.35)
    save("linoleum", rgb(img), normal_map(fbm(n, 0.4, 114) * 0.5 + scuff * 0.2, 0.8))


def moquette(n=1024):
    """深紅のモケット（1枚 = 0.5m）。短い毛足と、座り込まれて色の抜けたむら"""
    pile = fbm(n, 0.15, 121)
    pile2 = fbm(n, 0.5, 122, ax=1.0, ay=3.0)
    wear = fbm(n, 2.3, 123)
    t = np.clip(0.5 + (pile - 0.5) * 0.5 + (pile2 - 0.5) * 0.35 + (wear - 0.5) * 0.45, 0, 1)
    col = colorize(t, (84, 12, 18), (150, 34, 40))
    save("moquette", rgb(col), normal_map(pile * 0.7 + pile2 * 0.3, 2.5))


def stainless(n=1024):
    """ヘアライン仕上げのステンレス（1枚 = 0.5m、筋は U 方向）"""
    hair = fbm(n, 0.5, 131, ax=60.0, ay=1.0)
    hair2 = fbm(n, 1.0, 132, ax=20.0, ay=1.0)
    blot = fbm(n, 2.5, 133)
    lum = 0.88 + (hair - 0.5) * 0.10 + (hair2 - 0.5) * 0.08 + (blot - 0.5) * 0.06
    base = np.array([196, 198, 202], dtype=float)
    save("stainless", rgb(base[None, None, :] * lum[..., None]), normal_map(hair, 0.7))


def rack_net(n=512):
    """網棚の菱形の網（アルファ付き、1枚 = 網目4つ）"""
    y, x = np.mgrid[0:n, 0:n] / n
    u, v = (x + y) * 4, (x - y) * 4
    du = np.abs(u - np.round(u)); dv = np.abs(v - np.round(v))
    wire = np.clip(1 - np.minimum(du, dv) / 0.07, 0, 1)
    col = np.zeros((n, n, 4))
    col[..., 0] = 170; col[..., 1] = 172; col[..., 2] = 176
    col[..., 3] = (wire > 0.35) * 255
    save("rack_net", Image.fromarray(col.astype(np.uint8), "RGBA"))


# ---------------------------------------------------------------- 広告・掲示

def _poster(w, h, bg, blocks, name):
    img = Image.new("RGB", (w, h), bg)
    d = ImageDraw.Draw(img)
    for kind, *args in blocks:
        if kind == "rect":
            d.rectangle(args[0], fill=args[1])
        elif kind == "ellipse":
            d.ellipse(args[0], fill=args[1])
        elif kind == "text":
            (x, y), text, size, col, *rest = args
            f = _font(size, mincho=bool(rest and rest[0] == "mincho"))
            d.text((x, y), text, font=f, fill=col)
        elif kind == "vtext":
            (x, y), text, size, col = args
            f = _font(size)
            for i, ch in enumerate(text):
                d.text((x, y + i * size * 1.05), ch, font=f, fill=col)
    # 経年：色あせと角の汚れ
    a = np.asarray(img).astype(float)
    fade = fbm(w, 2.2, sum(map(ord, name)) % 1000, m=h)[..., None]   # hash() は実行ごとに変わるので使わない
    a = a * (0.9 + fade * 0.08) + 12
    return rgb(a)


def posters():
    # 中吊り（1.0 x 0.6 → 1000x600）
    save("ad_eikaiwa", _poster(1000, 600, (238, 232, 214), [
        ("rect", [0, 0, 1000, 150], (32, 84, 150)),
        ("text", (40, 30), "話せる自分へ、あと３か月。", 64, (255, 255, 255)),
        ("text", (60, 210), "英会話のモリタ", 96, (32, 84, 150)),
        ("text", (60, 350), "駅前校・北町校　無料体験レッスン受付中", 38, (60, 60, 60)),
        ("ellipse", [760, 330, 950, 520], (230, 120, 40)),
        ("text", (790, 385), "入会金", 40, (255, 255, 255)),
        ("text", (795, 435), "０円", 52, (255, 255, 255)),
        ("rect", [0, 560, 1000, 600], (32, 84, 150)),
    ], "eikaiwa"))
    save("ad_travel", _poster(1000, 600, (200, 222, 232), [
        ("rect", [0, 330, 1000, 600], (70, 110, 70)),
        ("ellipse", [640, 60, 820, 240], (250, 236, 200)),
        ("text", (50, 40), "夏の高原へ。", 88, (40, 60, 90), "mincho"),
        ("text", (60, 170), "特急で　２時間１０分", 44, (40, 60, 90)),
        ("text", (60, 420), "白樺台・霧ヶ丘　ゆったり周遊きっぷ", 42, (255, 255, 255)),
        ("text", (60, 490), "くわしくは駅の窓口で", 32, (230, 240, 230)),
    ], "travel"))
    save("ad_medicine", _poster(1000, 600, (250, 246, 236), [
        ("rect", [0, 0, 260, 600], (200, 40, 50)),
        ("vtext", (90, 40), "胃腸に", 72, (255, 255, 255)),
        ("text", (300, 60), "カイセイ胃腸薬", 90, (200, 40, 50)),
        ("text", (310, 200), "つかれた胃に、やさしく効く。", 44, (70, 60, 50)),
        ("text", (310, 470), "第２類医薬品　使用上の注意をよく読んでお使いください", 26, (90, 90, 90)),
    ], "medicine"))
    # 窓上の小さな広告（0.5 x 0.32 → 500x320）
    save("side_realestate", _poster(500, 320, (245, 245, 240), [
        ("rect", [0, 0, 500, 70], (40, 120, 90)),
        ("text", (20, 12), "みなと不動産", 44, (255, 255, 255)),
        ("text", (24, 110), "駅徒歩５分　新築分譲", 34, (40, 40, 40)),
        ("text", (24, 170), "２ＬＤＫ　２９８０万円〜", 36, (200, 40, 40)),
        ("text", (24, 250), "☎ 0120-00-3812", 30, (60, 60, 60)),
    ], "realestate"))
    save("side_clinic", _poster(500, 320, (236, 242, 248), [
        ("text", (24, 20), "ねむれない夜に。", 44, (40, 60, 110), "mincho"),
        ("text", (24, 100), "こころとねむりの相談室", 34, (40, 40, 40)),
        ("text", (24, 170), "小川脳神経総合研究所　附属外来", 26, (80, 80, 80)),
        ("rect", [24, 230, 476, 234], (40, 60, 110)),
        ("text", (24, 250), "予約制　平日９時〜１７時", 28, (80, 80, 80)),
    ], "clinic"))


def route_map(w=1100, h=280):
    """ドア上の路線図"""
    img = Image.new("RGB", (w, h), (246, 244, 236))
    d = ImageDraw.Draw(img)
    f_small = _font(26, mincho=False)
    f_title = _font(30, mincho=False)
    d.text((24, 16), "ご案内　この電車の停車駅", font=f_title, fill=(40, 40, 40))
    stations = ["港", "北町", "中央", "緑ヶ丘", "白樺台", "小川研究所前", "霧ヶ丘", "終点"]
    y = 150
    x0, x1 = 70, w - 70
    d.rectangle([x0, y - 7, x1, y + 7], fill=(200, 40, 50))
    for i, s in enumerate(stations):
        x = x0 + (x1 - x0) * i / (len(stations) - 1)
        here = s == "小川研究所前"
        r = 16 if here else 12
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255), outline=(200, 40, 50), width=5)
        if here:
            d.ellipse([x - 7, y - 7, x + 7, y + 7], fill=(200, 40, 50))
        tw = d.textlength(s, font=f_small)
        d.text((x - tw / 2, y + 28 if i % 2 == 0 else y - 64), s, font=f_small, fill=(200, 40, 50) if here else (40, 40, 40))
    save("route_map", img)


def stickers():
    """ドアの注意ステッカー・号車番号・優先席"""
    img = Image.new("RGB", (256, 256), (250, 208, 40))
    d = ImageDraw.Draw(img)
    d.polygon([(128, 22), (226, 182), (30, 182)], fill=(40, 40, 40))
    d.polygon([(128, 50), (202, 170), (54, 170)], fill=(250, 208, 40))
    d.rectangle([120, 84, 136, 140], fill=(40, 40, 40))
    d.ellipse([119, 148, 137, 164], fill=(40, 40, 40))
    f = _font(24, mincho=False)
    t = "ドアにご注意"
    d.text(((256 - d.textlength(t, font=f)) / 2, 204), t, font=f, fill=(40, 40, 40))
    save("door_sticker", img)
    img = Image.new("RGB", (512, 128), (32, 36, 40))
    d = ImageDraw.Draw(img)
    d.text((30, 18), "４号車", font=_font(80, mincho=False), fill=(240, 240, 240))
    d.text((300, 40), "モハ 3812", font=_font(46, mincho=False), fill=(200, 200, 200))
    save("car_number", img)
    img = Image.new("RGB", (256, 256), (40, 90, 170))
    d = ImageDraw.Draw(img)
    d.text((38, 170), "優先席", font=_font(58, mincho=False), fill=(255, 255, 255))
    for i, (cx, cy) in enumerate(((70, 80), (128, 80), (186, 80))):
        d.ellipse([cx - 14, cy - 44, cx + 14, cy - 16], fill=(255, 255, 255))
        d.rectangle([cx - 16, cy - 10, cx + 16, cy + 50], fill=(255, 255, 255))
    save("priority", img)


if __name__ == "__main__":
    melamine(); linoleum(); moquette(); stainless(); rack_net(); posters(); route_map(); stickers()
