# -*- coding: utf-8 -*-
"""所長の書斎（study）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/study/make_textures.py

出力: GenAssets/study/tex/*.png（Blenderの確認用）と
      project/Assets/EscapePrototype/Textures/HQ/Study/*.png（Unity用。_n が法線）
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

save = Saver(HERE, "Study")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


def plaster_old(n=1024):
    """古い漆喰の壁（1枚 = 1m）。灰色がかった白、細かい凹凸、薄い染みとひび"""
    src = os.path.join(HERE, "..", "tex_wall_seamless.png")
    im = np.asarray(Image.open(src).convert("L").resize((n, n), Image.LANCZOS)).astype(float) / 255
    grain = fbm(n, 0.6, 401)
    blot = fbm(n, 2.4, 402)
    drip = fbm(n, 1.8, 403, ax=0.3, ay=1.0)
    lum = 0.78 + (im - im.mean()) * 0.5 + (grain - 0.5) * 0.06 - np.clip(blot - 0.62, 0, 1) * 0.5 - np.clip(drip - 0.66, 0, 1) * 0.35
    base = np.array([214, 208, 196], dtype=float)
    col = base[None, None, :] * lum[..., None]
    col[..., 2] -= np.clip(blot - 0.55, 0, 1)[...] * 30
    save("plaster_old", rgb(col), normal_map(im * 0.6 + grain * 0.4, 1.6))


def floor_planks(n=1024):
    """濃い色の床板（1枚 = 1m、幅 12.5cm の板 8枚。板ごとに色と木目を変える、長手は U）"""
    rnd = np.random.default_rng(411)
    y, x = np.mgrid[0:n, 0:n] / n
    plank = np.floor(y * 8).astype(int)
    col = np.zeros((n, n, 3))
    h = np.zeros((n, n))
    for p in range(8):
        m = plank == p
        warp = (fbm(n, 2.4, 420 + p, ax=10.0, ay=1.0) - 0.5) * 0.8
        ring = 0.5 + 0.5 * np.sin(2 * np.pi * ((y * 8 - p) * 3 + warp * 2))
        streak = fbm(n, 1.0, 440 + p, ax=40.0, ay=1.0)
        t = np.clip(0.45 + (streak - 0.5) * 0.7 - (ring > 0.8) * 0.15 + rnd.uniform(-0.12, 0.12), 0, 1)
        c = colorize(t, (40, 26, 18), (104, 70, 46))
        col[m] = c[m]
        h[m] = streak[m]
        # 板の継ぎ目（木口）
        cut = rnd.uniform(0.1, 0.9)
        seam_x = np.abs(x - cut) < 0.0025
        col[m & seam_x] *= 0.35
    edge = np.abs((y * 8) % 1 - 0.5) > 0.492
    col[edge] *= 0.3
    h[edge] = 0
    wear = fbm(n, 2.2, 460)
    col *= (0.9 + wear[..., None] * 0.15)
    save("floor_planks", rgb(col), normal_map(h * 0.6 - edge * 0.8, 1.5))


def dark_wood(n=1024):
    """梁・家具の濃い木（1枚 = 1m、木目は U）"""
    y, x = np.mgrid[0:n, 0:n] / n
    warp = (fbm(n, 2.6, 471, ax=10.0, ay=1.0) - 0.5) * 0.9
    ring = np.clip(((0.5 + 0.5 * np.sin(2 * np.pi * (y * 7 + warp * 3))) - 0.6) * 3.5, 0, 1)
    streak = fbm(n, 1.1, 472, ax=40.0, ay=1.0)
    pores = fbm(n, 0.3, 473, ax=12.0, ay=1.0)
    t = np.clip(0.5 + (streak - 0.5) * 0.7 - ring * 0.2 - (pores > 0.64) * 0.08, 0, 1)
    col = colorize(t, (34, 22, 15), (96, 64, 42))
    save("dark_wood", rgb(col), normal_map(streak * 0.5 - ring * 0.3, 1.0))


# ---------------------------------------------------------------- 本の背表紙（アトラス）

BOOK_COLS = [(92, 28, 24), (38, 52, 40), (30, 38, 62), (118, 86, 48), (70, 46, 30), (40, 30, 26),
             (140, 120, 90), (80, 20, 36), (54, 62, 46), (98, 70, 44), (28, 28, 30), (120, 104, 76),
             (66, 38, 26), (46, 60, 72), (104, 40, 30), (84, 78, 60)]
TITLES = ["脳神経外科学", "記憶の構造", "神経回路網", "情報理論", "人工知能概論", "臨床心理学", "脳と意識",
          "統計学", "解剖学図譜", "精神医学", "計算論", "Neuroscience", "MEMORY", "Cortex", "学会誌 1998", "研究録"]


def book_atlas():
    """背表紙 16種（4 x 4 のアトラス、各 256 x 1024 = 幅 3.5cm x 高さ 25cm 相当）"""
    cw, ch = 256, 1024
    img = Image.new("RGB", (cw * 4, ch * 4), (0, 0, 0))
    d = ImageDraw.Draw(img)
    rnd = random.Random(481)
    for k in range(16):
        x0, y0 = (k % 4) * cw, (k // 4) * ch
        base = BOOK_COLS[k]
        d.rectangle([x0, y0, x0 + cw, y0 + ch], fill=base)
        gold = (190, 160, 90) if k % 3 else (170, 170, 160)
        style = k % 4
        if style in (0, 2):
            for yy in (60, 110, ch - 160, ch - 110):
                d.rectangle([x0 + 10, y0 + yy, x0 + cw - 10, y0 + yy + 12], fill=gold)
        if style == 1:
            d.rectangle([x0, y0 + 200, x0 + cw, y0 + 480], fill=tuple(max(0, c - 25) for c in base))
        if style == 3:
            d.rectangle([x0 + 30, y0 + 160, x0 + cw - 30, y0 + 560], outline=gold, width=6)
        title = TITLES[k]
        if all(ord(c) < 128 for c in title):
            f = _font(64, mincho=True)
            tmp = Image.new("RGB", (700, 110), base)
            ImageDraw.Draw(tmp).text((10, 10), title, font=f, fill=gold)
            tmp = tmp.rotate(90, expand=True)
            img.paste(tmp.crop((0, 0, min(110, cw - 40), 700)), (x0 + 70, y0 + 180))
        else:
            f = _font(92, mincho=True)
            yy = y0 + 170
            for c in title[:7]:
                d.text((x0 + (cw - 92) // 2, yy), c, font=f, fill=gold)
                yy += 100
        d.text((x0 + 80, y0 + ch - 90), "著", font=_font(60), fill=gold)
    a = np.asarray(img).astype(float)
    wear = fbm(1024, 1.6, 482)
    wear = np.asarray(Image.fromarray((wear * 255).astype(np.uint8)).resize((cw * 4, ch * 4))).astype(float) / 255
    a = a * (0.8 + wear[..., None] * 0.3)
    save("book_atlas", rgb(a))
    # 本の小口（紙の束）
    n = 256
    y, x = np.mgrid[0:n, 0:n] / n
    lines = 0.5 + 0.5 * np.sin(2 * np.pi * y * 90)
    p = np.array([214, 204, 178], dtype=float)[None, None, :] * (0.85 + lines[..., None] * 0.1)
    save("book_pages", rgb(p))


def documents():
    """机の上の文書（古い紙に手書き）・手帳の切れ端・招聘状の封筒・額のメモ"""
    w, h = 740, 1040
    img = Image.new("RGB", (w, h), (226, 214, 184))
    d = ImageDraw.Draw(img)
    f = hand(34)
    lines = ["被験体経過報告　抜粋", "", "被験体は自身を研究員と認識している", "記憶の再生時、同一の空間を反復して認知する",
             "（回廊状の構造として観測される）", "移植したAI領域が宿主の記憶を", "再構成している可能性がある", "",
             "報告者名の欄は黒く塗り潰されている。"]
    y = 70
    for s in lines:
        d.text((60, y), s, font=f, fill=(40, 34, 30)); y += 60
    d.rectangle([380, 800, 660, 846], fill=(18, 16, 16))
    d.text((60, 800), "報告者：", font=f, fill=(40, 34, 30))
    a = np.asarray(img).astype(float)
    stain = fbm(w, 2.2, 491, m=h)[..., None]
    a = a * (0.85 + stain * 0.18) - np.array([0, 6, 18]) * stain
    save("doc_report", rgb(a))
    # 手帳の切れ端（罫線・破れた上端は形状で表す）
    w, h = 600, 300
    img = Image.new("RGB", (w, h), (236, 230, 212))
    d = ImageDraw.Draw(img)
    for i in range(7):
        d.line([(0, 40 + i * 38), (w, 40 + i * 38)], fill=(170, 186, 210), width=2)
    f = hand(28)
    for i, s in enumerate(["4/2　佐伯と面談。例の件、佐伯にだけは", "　　　話しておく。彼なら気づいている。",
                           "4/9　佐伯より報告。「今は結論を出すな」と", "　　　伝えた。時間が要る。"]):
        d.text((24, 14 + i * 38), s, font=f, fill=(30, 40, 90))
    save("doc_scrap", img)
    # 封筒（表：宛名、裏：封）
    w, h = 800, 440
    img = Image.new("RGB", (w, h), (238, 234, 222))
    d = ImageDraw.Draw(img)
    d.text((290, 120), "二宮秀樹 様", font=_font(64, mincho=True), fill=(30, 30, 30))
    d.text((80, 330), "小川脳神経総合研究所", font=_font(34, mincho=True), fill=(60, 60, 60))
    d.rectangle([640, 40, 740, 150], outline=(170, 40, 40), width=4)
    d.text((660, 70), "親展", font=_font(34, mincho=True), fill=(170, 40, 40))
    save("envelope", img)
    # 額に入ったメモ（壁）
    w, h = 400, 560
    img = Image.new("RGB", (w, h), (220, 210, 186))
    d = ImageDraw.Draw(img)
    f = _font(96, mincho=True)
    for i, c in enumerate("温故知新"):
        d.text((150, 50 + i * 110), c, font=f, fill=(30, 26, 22))
    d.rectangle([250, 470, 290, 510], fill=(170, 40, 40))
    save("frame_note", img)


if __name__ == "__main__":
    plaster_old(); floor_planks(); dark_wood(); book_atlas(); documents()
