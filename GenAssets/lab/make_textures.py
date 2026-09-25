# -*- coding: utf-8 -*-
"""研究所応接室（lab）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/lab/make_textures.py

出力: GenAssets/lab/tex/*.png（Blenderの確認用）と
      project/Assets/EscapePrototype/Textures/HQ/Lab/*.png（Unity用。_n が法線）
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

save = Saver(HERE, "Lab")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"        # 手書きに近い教科書体


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


# ---------------------------------------------------------------- 素材

def carpet_tile(n=1024):
    """濃い茶灰色のタイルカーペット（1枚 = 1m = 50cm角 x 4、目の向きを市松に変える）"""
    y, x = np.mgrid[0:n, 0:n] / n
    grain_h = fbm(n, 0.4, 201, ax=8.0, ay=1.0)
    grain_v = fbm(n, 0.4, 202, ax=1.0, ay=8.0)
    checker = ((np.floor(x * 2) + np.floor(y * 2)) % 2).astype(float)
    grain = grain_h * checker + grain_v * (1 - checker)
    speck = fbm(n, 0.05, 203)
    tone = fbm(n, 2.4, 204)
    base = np.array([62, 56, 56], dtype=float)
    lum = 0.9 + (grain - 0.5) * 0.25 + (speck - 0.5) * 0.25 + (tone - 0.5) * 0.08 + checker * 0.03
    # 目地（タイルの継ぎ目）
    gx = np.minimum((x * 2) % 1, 1 - (x * 2) % 1); gy = np.minimum((y * 2) % 1, 1 - (y * 2) % 1)
    seam = np.clip(1 - np.minimum(gx, gy) / 0.004, 0, 1)
    lum *= 1 - seam * 0.25
    save("carpet_tile", rgb(base[None, None, :] * lum[..., None]), normal_map(grain * 0.7 + speck * 0.3 - seam, 2.5))


def paint(n=1024):
    """薄い青灰色の塗り壁（1枚 = 1m）。ローラーの細かいむら"""
    roll = fbm(n, 0.7, 211, ax=1.0, ay=4.0)
    mott = fbm(n, 2.3, 212)
    base = np.array([214, 220, 224], dtype=float)
    lum = 1.0 + (roll - 0.5) * 0.03 + (mott - 0.5) * 0.04
    save("paint", rgb(base[None, None, :] * lum[..., None]), normal_map(roll, 0.8))


def ceiling_tile(n=512):
    """600角の吸音板（1枚 = 0.6m）。細かい穴と縁の面取り"""
    y, x = np.mgrid[0:n, 0:n] / n
    img = np.full((n, n), 238.0)
    holes = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(holes)
    rnd = random.Random(221)
    step = n / 48
    for i in range(48):
        for j in range(48):
            if rnd.random() < 0.8:
                cx, cy = (i + 0.5) * step + rnd.uniform(-0.8, 0.8), (j + 0.5) * step + rnd.uniform(-0.8, 0.8)
                d.ellipse([cx - 1.2, cy - 1.2, cx + 1.2, cy + 1.2], fill=255)
    h = np.asarray(holes).astype(float) / 255
    edge = np.clip(np.minimum(np.minimum(x, 1 - x), np.minimum(y, 1 - y)) / 0.02, 0, 1)
    img = img * (1 - h * 0.35) * (0.88 + edge * 0.12) * (0.98 + (fbm(n, 1.8, 222) - 0.5) * 0.04)
    col = np.stack([img, img, img * 1.01], axis=-1)
    save("ceiling_tile", rgb(col), normal_map(edge - h * 0.5, 2.0))


def oak(n=1024):
    """淡いオークの天板（1枚 = 1m、木目は U 方向）"""
    y, x = np.mgrid[0:n, 0:n] / n
    warp = (fbm(n, 2.6, 231, ax=10.0, ay=1.0) - 0.5) * 0.9
    ring = 0.5 + 0.5 * np.sin(2 * np.pi * (y * 11 + warp * 3))
    ring = np.clip((ring - 0.6) * 3.5, 0, 1)
    streak = fbm(n, 1.1, 232, ax=40.0, ay=1.0)
    pores = fbm(n, 0.3, 233, ax=12.0, ay=1.0)
    t = np.clip(0.55 + (streak - 0.5) * 0.6 - ring * 0.18 - (pores > 0.64) * 0.08, 0, 1)
    col = colorize(t, (170, 138, 100), (224, 200, 160))
    save("oak", rgb(col), normal_map(streak * 0.5 - (pores > 0.64) * 0.3 - ring * 0.2, 1.0))


def mesh_fabric(n=256):
    """椅子の背のメッシュ（黒）"""
    y, x = np.mgrid[0:n, 0:n] / n
    w = (np.sin(2 * np.pi * x * 32) * np.sin(2 * np.pi * y * 32)) * 0.5 + 0.5
    lum = 18 + w * 30
    save("mesh", rgb(np.stack([lum, lum, lum * 1.05], axis=-1)), normal_map(w, 3.0))


def perforated(n=512):
    """サーバーラックの扉（黒い鋼板の六角パンチング）"""
    img = Image.new("RGB", (n, n), (26, 27, 30))
    d = ImageDraw.Draw(img)
    step = n / 16
    for j in range(17):
        for i in range(17):
            cx = i * step + (step / 2 if j % 2 else 0)
            cy = j * step * 0.87
            r = step * 0.32
            d.regular_polygon((cx, cy, r), 6, fill=(6, 6, 8))
    a = np.asarray(img).astype(float)
    save("perforated", rgb(a), normal_map(a.mean(axis=2) / 255, 3.0))


# ---------------------------------------------------------------- 画面・掲示

def mri_slice(size, seed, level=0.5):
    """脳のMRIの断面（グレースケール配列 0..1）。頭蓋・脳脊髄液・灰白質・白質の指状の突起・脳室・大脳縦裂"""
    y, x = np.mgrid[0:size, 0:size] / size * 2 - 1
    ex, ey = x / 0.78, y / 0.92
    r = np.sqrt(ex ** 2 + ey ** 2)
    th = np.arctan2(ey, ex)
    n1 = fbm(size, 2.0, seed) - 0.5
    n2 = fbm(size, 1.2, seed + 1) - 0.5
    # 脳の外形（しわで凹凸）と、白質の指状の突起（脳回の芯）
    outer = 0.86 + 0.025 * np.sin(th * 23 + n1 * 8)
    fingers = (0.5 + 0.5 * np.sin(th * 17 + n1 * 10 + n2 * 4)) ** 2.5
    white_r = 0.55 + 0.26 * fingers + 0.04 * n2
    val = np.zeros_like(r)
    val = np.where(r < outer, 0.42, 0.08)                       # 灰白質 / 脳脊髄液
    val = np.where(r < white_r, 0.78, val)                      # 白質
    sulci = np.abs(np.sin(th * 23 + n1 * 8)) < 0.18
    val = np.where(sulci & (r > white_r) & (r < outer), 0.12, val)   # 脳溝（黒い筋）
    skull = np.exp(-((r - 0.96) ** 2) / 0.0015)
    val = val + skull * 0.8
    val = np.where(r > 1.02, 0.0, val)
    # 脳室（蝶の形）と大脳縦裂
    lv = 0.05 * level
    vent = np.exp(-(((x + 0.1) / 0.06) ** 2 + ((y + lv) / 0.22) ** 2) * 2) +         np.exp(-(((x - 0.1) / 0.06) ** 2 + ((y + lv) / 0.22) ** 2) * 2)
    val = val * (1 - np.clip(vent * 1.5, 0, 0.9))
    fissure = np.exp(-(x / 0.012) ** 2) * (np.abs(y) > 0.35) * (r < outer)
    val = val * (1 - fissure * 0.8)
    val += (fbm(size, 0.3, seed + 2) - 0.5) * 0.08
    img = Image.fromarray((np.clip(val, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))
    return np.asarray(img).astype(float) / 255


def screens():
    # 脳波（8ch）
    w, h = 640, 400
    img = Image.new("RGB", (w, h), (6, 12, 20))
    d = ImageDraw.Draw(img)
    rnd = random.Random(241)
    f = _font(14, mincho=False)
    for c in range(8):
        y0 = 30 + c * 46
        d.text((8, y0 - 8), f"CH{c + 1}", font=f, fill=(90, 140, 170))
        pts = []
        ph = rnd.random() * 10
        for i in range(560):
            t = i / 560
            v = math.sin(t * 40 + ph) * 6 + math.sin(t * 13 + ph * 2) * 8 + rnd.gauss(0, 2.5)
            if c == 5 and 0.62 < t < 0.7:
                v += math.sin(t * 400) * 18                  # 異常な振れ
            pts.append((60 + i, y0 + v))
        d.line(pts, fill=(80, 230, 170) if c != 5 else (240, 120, 90), width=1)
    d.rectangle([0, 0, w - 1, 18], fill=(20, 30, 44))
    d.text((8, 2), "EEG  REC 00:14:52   SUBJ-03", font=f, fill=(180, 210, 230))
    save("screen_eeg", img)
    # MRI（ビューア）
    img = Image.new("RGB", (w, h), (8, 8, 10))
    for k, (ox, sd) in enumerate(((20, 251), (330, 252))):
        m = mri_slice(290, sd, level=k)
        im = Image.fromarray((m * 235).astype(np.uint8)).convert("RGB")
        img.paste(im, (ox, 60))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w - 1, 22], fill=(22, 26, 34))
    d.text((8, 4), "MR  AX T2   SLICE 42/88      MEMORY REGION MAP", font=f, fill=(190, 200, 220))
    d.rectangle([180, 170, 240, 230], outline=(240, 200, 60), width=2)     # 注目領域
    save("screen_mri", img)


def lightbox():
    """シャウカステン：MRIフィルム 6 x 2 枚（2.4 x 0.7m）"""
    w, h = 1536, 448
    img = Image.new("RGB", (w, h), (200, 225, 245))
    for j in range(2):
        for i in range(6):
            m = mri_slice(200, 300 + i * 7 + j * 3, level=(i - 2.5) / 2.5)
            film = (m * 210 + 10).astype(np.uint8)
            im = Image.fromarray(film).convert("RGB")
            img.paste(im, (24 + i * 252, 16 + j * 216))
    d = ImageDraw.Draw(img)
    f = _font(14, mincho=False)
    for i in range(6):
        d.text((30 + i * 252, 18), f"SUBJ-0{(i % 4) + 1}  AX {10 + i * 8}", font=f, fill=(230, 230, 230))
    save("lightbox", img)


def _brain_outline(d, cx, cy, s, col, width=4, seed=0):
    rnd = random.Random(seed)
    pts = []
    for k in range(60):
        a = math.pi * 2 * k / 60
        rr = 1 + 0.05 * math.sin(a * 9 + rnd.random()) + 0.03 * rnd.random()
        pts.append((cx + math.cos(a) * s * 1.25 * rr, cy + math.sin(a) * s * 0.9 * rr * (0.85 if math.sin(a) > 0 else 1)))
    d.line(pts + [pts[0]], fill=col, width=width, joint="curve")
    for k in range(9):                                        # 脳溝
        x0 = cx + rnd.uniform(-1, 1) * s * 0.9
        y0 = cy + rnd.uniform(-0.6, 0.4) * s
        path = [(x0, y0)]
        for _ in range(6):
            x0 += rnd.uniform(-0.15, 0.15) * s; y0 += rnd.uniform(0.02, 0.1) * s
            path.append((x0, y0))
        d.line(path, fill=col, width=max(2, width - 1))
    d.line([(cx - s * 0.1, cy + s * 0.8), (cx + s * 0.05, cy + s * 1.25)], fill=col, width=width)   # 脳幹


def whiteboard():
    """ガラスのホワイトボード（3.4 x 1.2m）：脳の図・回路・手書きのメモ"""
    w, h = 2040, 720
    img = Image.new("RGB", (w, h), (242, 246, 246))
    d = ImageDraw.Draw(img)
    black, blue, red = (30, 32, 38), (30, 70, 160), (190, 40, 40)
    _brain_outline(d, 260, 300, 150, black, 9, 1)
    _brain_outline(d, 760, 280, 110, blue, 8, 2)
    d.ellipse([700, 250, 760, 300], outline=red, width=7)
    d.line([(760, 275), (900, 200)], fill=red, width=6)
    f = hand(52); fs = hand(40)
    d.text((910, 170), "記憶領域 → 回廊構造", font=f, fill=red)
    # 神経回路（ノードと線）
    rnd = random.Random(261)
    nodes = [(1150 + (i % 5) * 110 + rnd.uniform(-20, 20), 120 + (i // 5) * 120 + rnd.uniform(-20, 20)) for i in range(15)]
    for a in nodes:
        for b in rnd.sample(nodes, 2):
            d.line([a, b], fill=black, width=4)
    for (x, y) in nodes:
        d.ellipse([x - 14, y - 14, x + 14, y + 14], outline=blue, width=7)
    d.text((1150, 520), "欠損区画に AI 生成記憶を移植", font=fs, fill=black)
    d.text((1150, 570), "齟齬 ＜ 閾値 なら 覚醒", font=fs, fill=black)
    d.text((100, 520), "被験体 No.03  30%", font=f, fill=blue)
    d.text((100, 580), "Φ(t) = ∫ w·m(t) dt", font=fs, fill=black)
    d.text((1780, 60), "2015.4.9", font=fs, fill=black)
    d.line([(1760, 110), (1990, 110)], fill=black, width=2)
    d.text((1760, 130), "佐伯さんに", font=fs, fill=red)
    d.text((1760, 175), "確認！", font=fs, fill=red)
    # 消し跡
    a = np.asarray(img).astype(float)
    smear = fbm(w, 2.0, 262, m=h)[..., None]
    a = a * (0.97 + smear * 0.03) + (1 - (a / 255)) * smear * 20
    save("whiteboard", rgb(a))


def _id_photo(w, h, ok, seed):
    rnd = random.Random(seed)
    img = Image.new("RGB", (w, h), (170, 190, 210))
    d = ImageDraw.Draw(img)
    d.ellipse([w * 0.3, h * 0.12, w * 0.7, h * 0.55], fill=(206, 180, 160))          # 顔
    d.ellipse([w * 0.28, h * 0.08, w * 0.72, h * 0.3], fill=(40, 34, 30))            # 髪
    d.polygon([(w * 0.1, h), (w * 0.25, h * 0.62), (w * 0.75, h * 0.62), (w * 0.9, h)], fill=(40, 44, 60))
    d.polygon([(w * 0.42, h * 0.62), (w * 0.5, h * 0.8), (w * 0.58, h * 0.62)], fill=(240, 240, 240))
    if ok:
        d.ellipse([w * 0.4, h * 0.3, w * 0.45, h * 0.34], fill=(40, 30, 30))
        d.ellipse([w * 0.55, h * 0.3, w * 0.6, h * 0.34], fill=(40, 30, 30))
        d.line([(w * 0.45, h * 0.45), (w * 0.55, h * 0.45)], fill=(140, 90, 80), width=2)
        return img
    # 薬品で溶かされたような黒い滲み（下へ垂れる）
    a = np.asarray(img).astype(float)
    yy, xx = np.mgrid[0:h, 0:w]
    blob = fbm(max(w, h), 1.6, seed)[:h, :w]
    r = np.sqrt(((xx - w * 0.5) / (w * 0.42)) ** 2 + ((yy - h * 0.38) / (h * 0.42)) ** 2)
    mask = np.clip((1.1 - r + (blob - 0.5) * 0.8) * 4, 0, 1)
    for _ in range(6):
        x0 = rnd.uniform(0.25, 0.75) * w
        drip = np.exp(-((xx - x0) / (w * 0.02)) ** 2) * (yy > h * 0.5) * (yy < h * rnd.uniform(0.7, 1.0))
        mask = np.maximum(mask, drip)
    a = a * (1 - mask[..., None]) + np.array([12, 10, 10]) * mask[..., None]
    return rgb(a)


def member_board():
    """社員名簿のボード（2.7 x 1.2m）"""
    w, h = 1620, 720
    img = Image.new("RGB", (w, h), (236, 234, 226))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 90], fill=(40, 60, 96))
    d.text((40, 16), "研究メンバー　／　社員名簿", font=_font(52, mincho=False), fill=(240, 240, 240))
    names = [("佐伯 恒一", "主任研究員補佐"), ("水野 美奈", "臨床研究員"), ("黒田 恒一", "上席研究員"), ("――――", "所長")]
    for i, (nm, role) in enumerate(names):
        x0 = 150 + i * 350
        ph = _id_photo(220, 280, i == 0, 271 + i)
        img.paste(ph, (x0, 150))
        d.rectangle([x0 - 4, 146, x0 + 224, 434], outline=(120, 120, 120), width=2)
        d.text((x0, 460), nm, font=_font(44, mincho=False), fill=(30, 30, 30))
        d.text((x0, 520), role, font=_font(30, mincho=False), fill=(80, 80, 80))
    d.text((150, 620), "※ 無断での持ち出しを禁ず　総務課", font=_font(28, mincho=False), fill=(120, 60, 60))
    a = np.asarray(img).astype(float)
    a = a * (0.95 + fbm(w, 2.2, 279, m=h)[..., None] * 0.05)
    save("member_board", rgb(a))


def labels():
    img = Image.new("RGB", (256, 256), (250, 206, 30))
    d = ImageDraw.Draw(img)
    d.polygon([(128, 20), (236, 206), (20, 206)], fill=(20, 20, 20))
    d.polygon([(128, 52), (208, 190), (48, 190)], fill=(250, 206, 30))
    d.rectangle([120, 90, 136, 148], fill=(20, 20, 20)); d.ellipse([118, 158, 138, 178], fill=(20, 20, 20))
    t = "注意"; f = _font(34, mincho=False)
    d.text(((256 - d.textlength(t, font=f)) / 2, 212), t, font=f, fill=(20, 20, 20))
    save("hazard", img)
    # 計測機器の前面（小さな画面・つまみ・目盛）
    img = Image.new("RGB", (512, 256), (196, 198, 196))
    d = ImageDraw.Draw(img)
    d.rectangle([24, 30, 230, 150], fill=(10, 30, 20))
    pts = [(30 + i * 2, 90 + 30 * math.sin(i * 0.25) * math.exp(-i / 70)) for i in range(98)]
    d.line(pts, fill=(90, 240, 150), width=2)
    for i in range(4):
        cx = 290 + i * 55
        d.ellipse([cx - 18, 70, cx + 18, 106], fill=(40, 40, 42))
        d.line([(cx, 88), (cx + 10, 74)], fill=(220, 220, 220), width=3)
    for i in range(12):
        d.rectangle([30 + i * 38, 190, 50 + i * 38, 210], fill=(60, 60, 64))
    d.text((260, 20), "NEURO-AMP 8", font=_font(30, mincho=False), fill=(40, 40, 40))
    save("device_front", img)
    # 机上の資料（グラフ付きの紙）
    img = Image.new("RGB", (512, 724), (240, 240, 234))
    d = ImageDraw.Draw(img)
    f = _font(22, mincho=False)
    d.text((40, 40), "被験体 03　経過観察記録", font=_font(30, mincho=False), fill=(30, 30, 30))
    for i in range(10):
        d.line([(40, 110 + i * 26), (470, 110 + i * 26)], fill=(150, 150, 150), width=2)
    d.rectangle([40, 400, 470, 640], outline=(60, 60, 60), width=2)
    pts = [(40 + i * 4.3, 520 - 60 * math.sin(i * 0.12) - i * 0.4) for i in range(100)]
    d.line(pts, fill=(40, 80, 160), width=3)
    save("paper_graph", img)


if __name__ == "__main__":
    carpet_tile(); paint(); ceiling_tile(); oak(); mesh_fabric(); perforated()
    screens(); lightbox(); whiteboard(); member_board(); labels()
