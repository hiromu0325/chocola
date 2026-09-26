# -*- coding: utf-8 -*-
"""SYSTEM ROOM（system_room）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/system/make_textures.py

出力: GenAssets/system/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/SystemRoom/*.png
脳の結線図の画面は脳神経解析室（analysis）の screen_connectome を使う。
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

save = Saver(HERE, "SystemRoom")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


def dark_floor(n=1024):
    """暗い灰色のフリーアクセス床（1枚 = 1.2m = 600角 x 4）。ほのかな光沢と擦れ"""
    y, x = np.mgrid[0:n, 0:n] / n
    speck = fbm(n, 0.06, 1901)
    tone = fbm(n, 2.0, 1902)
    scuff = np.clip(fbm(n, 1.0, 1903, ax=5.0, ay=1.0) - 0.62, 0, 1) * 3
    base = np.array([46, 50, 56], dtype=float)
    lum = 0.95 + (speck - 0.5) * 0.12 + (tone - 0.5) * 0.1 + scuff * 0.15
    gx = np.minimum((x * 2) % 1, 1 - (x * 2) % 1); gy = np.minimum((y * 2) % 1, 1 - (y * 2) % 1)
    seam = (np.minimum(gx, gy) < 0.004).astype(float)
    lum -= seam * 0.35
    save("dark_floor", rgb(base[None, None, :] * lum[..., None]), normal_map(speck * 0.2 - seam, 1.6))


def wall_panel(n=1024):
    """紺灰の吸音パネル（1枚 = 1.2m、縦長のパネル 0.6 x 1.2 と目地）。布の細かい織り"""
    y, x = np.mgrid[0:n, 0:n] / n
    weave = fbm(n, 0.12, 1911)
    tone = fbm(n, 2.2, 1912)
    base = np.array([44, 52, 64], dtype=float)
    lum = 0.95 + (weave - 0.5) * 0.12 + (tone - 0.5) * 0.08
    gx = np.minimum((x * 2) % 1, 1 - (x * 2) % 1)
    gy = np.minimum(y % 1, 1 - y % 1)
    seam = ((gx < 0.006) | (gy < 0.004)).astype(float)
    lum -= seam * 0.45
    save("wall_panel", rgb(base[None, None, :] * lum[..., None]), normal_map(weave * 0.3 - seam, 2.0))


def core_inner(w=1024, h=2048):
    """コアの芯：縦に流れる光の繊維と、神経のような枝（横方向は継ぎ目なし）"""
    rnd = random.Random(1921)
    img = Image.new("RGB", (w, h), (4, 10, 22))
    d = ImageDraw.Draw(img)
    for k in range(180):
        x = rnd.uniform(0, w)
        y = rnd.uniform(0, h)
        pts = [(x, y)]
        for s in range(rnd.randint(8, 30)):
            x += rnd.uniform(-18, 18); y += rnd.uniform(10, 40)
            pts.append((x, y))
        c = rnd.choice([(80, 170, 255), (120, 210, 255), (60, 120, 220), (180, 230, 255)])
        for ox in (-w, 0, w):
            d.line([(px + ox, py) for px, py in pts], fill=c, width=rnd.choice([1, 2, 2, 3]))
    for k in range(260):
        x, y = rnd.uniform(0, w), rnd.uniform(0, h)
        r = rnd.uniform(2, 6)
        for ox in (-w, 0, w):
            d.ellipse([x - r + ox, y - r, x + r + ox, y + r], fill=(200, 240, 255))
    glow = img.filter(ImageFilter.GaussianBlur(6))
    a = np.asarray(img).astype(float) * 0.7 + np.asarray(glow).astype(float) * 1.2
    bands = (0.75 + 0.25 * np.sin(np.mgrid[0:h, 0:w][0] / h * math.pi * 16))[..., None]
    save("core_inner", rgb(a * bands))


def screen_status(w=1024, h=640):
    """BRAIN DATA の状態表示（3件、取得率100%、同期中）"""
    img = Image.new("RGB", (w, h), (6, 12, 24))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 56], fill=(20, 50, 90))
    d.text((20, 10), "RENASCITA CORE  ─  BRAIN DATA", font=_font(32, mincho=False), fill=(200, 230, 255))
    f = _font(30, mincho=False)
    for i in range(3):
        y = 110 + i * 160
        d.text((40, y), f"DATA:0{i + 1}", font=_font(40, mincho=False), fill=(150, 210, 255))
        d.rectangle([300, y + 8, 900, y + 44], outline=(90, 150, 220), width=2)
        d.rectangle([304, y + 12, 896, y + 40], fill=(70, 150, 240))
        d.text((300, y + 60), "取得率 100%　　人格モデル: 同期中", font=f, fill=(180, 210, 240))
        d.text((920, y + 8), "●", font=f, fill=(90, 230, 150))
    save("screen_status", img)


def screen_restore(w=1024, h=640):
    """操作記録の復元中（まだ読めない。調べると全文が出る）"""
    img = Image.new("RGB", (w, h), (10, 8, 12))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 56], fill=(90, 20, 20))
    d.text((20, 10), "操作記録 復元中 ...", font=_font(32, mincho=False), fill=(255, 190, 180))
    rnd = random.Random(1931)
    f = _font(30, mincho=False)
    for i in range(3):
        y = 100 + i * 70
        d.text((40, y), f"DATA:0{i + 1}  登録実行者 ──", font=f, fill=(220, 200, 200))
        x = 470
        for k in range(14):
            c = rnd.choice(["縺", "繧", "繝", "譁", "蟄", "ｿ", "ｮ", "九", "溘", "Ζ", "?"])      # 文字化け
            d.text((x + k * 32, y), c, font=f, fill=(220, 120, 110))
    d.rectangle([40, 420, 980, 460], outline=(200, 90, 80), width=2)
    d.rectangle([44, 424, 44 + int(936 * 0.97), 456], fill=(200, 70, 60))
    d.text((40, 480), "97%  ── 調べると続きを表示", font=_font(26, mincho=False), fill=(220, 160, 150))
    save("screen_restore", img)


def screen_wave(w=1024, h=640):
    """同期の波形（3本の線がゆっくり重なっていく）"""
    img = Image.new("RGB", (w, h), (5, 10, 18))
    d = ImageDraw.Draw(img)
    for gx in range(0, w, 64):
        d.line([(gx, 0), (gx, h)], fill=(12, 24, 40))
    for gy in range(0, h, 64):
        d.line([(0, gy), (w, gy)], fill=(12, 24, 40))
    for k, c in enumerate(((90, 170, 255), (120, 230, 200), (230, 180, 120))):
        pts = []
        for x in range(0, w, 4):
            t = x / w
            y = h / 2 + (1 - t) * (60 + k * 40) * math.sin(t * 30 + k * 2.1) + math.sin(t * 90 + k) * 10
            pts.append((x, y))
        d.line(pts, fill=c, width=3)
    d.text((20, 16), "SYNC  PHASE", font=_font(30, mincho=False), fill=(150, 200, 240))
    save("screen_wave", img)


def core_plate(w=1024, h=256):
    img = Image.new("RGB", (w, h), (30, 34, 40))
    d = ImageDraw.Draw(img)
    d.rectangle([8, 8, w - 8, h - 8], outline=(120, 140, 160), width=6)
    d.text((40, 40), "RENASCITA CORE", font=_font(90, mincho=False), fill=(190, 210, 230))
    d.text((44, 170), "中枢記憶装置　小川脳神経総合研究所", font=_font(40, mincho=False), fill=(150, 170, 190))
    save("core_plate", img)


def devlog():
    """開発記録・治療手順（リング綴じの記録簿の開いた頁）"""
    w, h = 1480, 1040
    img = Image.new("RGB", (w, h), (240, 240, 234))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w // 2, h], fill=(232, 232, 226))
    d.text((70, 90), "開発記録", font=_font(72, mincho=True), fill=(20, 30, 60))
    d.text((70, 200), "治療手順 全文", font=_font(48, mincho=True), fill=(20, 20, 20))
    d.text((70, 300), "RENASCITA", font=_font(56, mincho=False), fill=(40, 70, 130))
    x0 = w // 2 + 50
    f = _font(30, mincho=True)
    lines = ["1. 提供体の脳情報を採取し、", "　 BRAIN DATA として登録", "2. 対象者（患者）の欠損領域へ", "　 写像・補完", "3. 対象者の人格が再構成されるまで反復", "",
             "備考: 完全な補完には提供体の脳情報を", "100% 取得する必要がある。", "100% 取得後の覚醒は不可", "（運用手順書 4-3 参照）"]
    for i, s in enumerate(lines):
        d.text((x0, 90 + i * 74), s, font=f, fill=(25, 25, 25))
    d.line([(x0, 90 + 8 * 74 + 44), (x0 + 400, 90 + 8 * 74 + 44)], fill=(170, 40, 40), width=3)
    save("devlog", img)


def gaplog():
    """黒田の照合ログ（手書きの検算。端が握り潰されてしわ）"""
    w, h = 700, 500
    img = Image.new("RGB", (w, h), (240, 238, 226))
    d = ImageDraw.Draw(img)
    for i in range(8):
        d.line([(20, 60 + i * 54), (w - 20, 60 + i * 54)], fill=(190, 206, 222), width=2)
    f = hand(30)
    lines = ["深夜帯に第12へ3回の入室。使用IDは主任。", "だが主任は当日、学会で北海道にいた。", "IDでは説明できない移動記録がある。", "", "……主任じゃない。", "なら、誰だ。誰が主任のIDを──"]
    for i, s in enumerate(lines):
        d.text((30, 24 + i * 54), s, font=f, fill=(20, 20, 30))
    a = np.asarray(img).astype(float)
    crease = fbm(w, 1.0, 1941, ax=0.4, ay=1.0, m=h)
    a *= (0.88 + crease[..., None] * 0.18)
    save("gaplog", rgb(a), normal_map(crease, 2.0))


if __name__ == "__main__":
    dark_floor(); wall_panel(); core_inner()
    screen_status(); screen_restore(); screen_wave(); core_plate(); devlog(); gaplog()
