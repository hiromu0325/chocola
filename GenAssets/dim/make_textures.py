# -*- coding: utf-8 -*-
"""薄暗い部屋のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/dim/make_textures.py

出力: GenAssets/dim/tex/*.png（Blenderの確認用）と
      project/Assets/EscapePrototype/Textures/HQ/Dim/*.png（Unity用。_n が法線）
ノイズはFFTで作るので上下左右が必ずつながる。
"""
import math
import os
import random
import shutil

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "tex")
UNITY = os.path.join(HERE, "..", "..", "project", "Assets", "EscapePrototype", "Textures", "HQ", "Dim")


# ---------------------------------------------------------------- ノイズ

def fbm(n, beta=1.5, seed=0, ax=1.0, ay=1.0, m=None):
    """1/f^beta のタイル可能ノイズ（0..1）。ax,ay は方向ごとの周波数の重み（大きいほどその方向に滑らか＝伸びる）"""
    m = m or n
    rng = np.random.default_rng(seed)
    white = rng.standard_normal((m, n))
    fy = np.fft.fftfreq(m)[:, None] * m
    fx = np.fft.fftfreq(n)[None, :] * n
    f = np.sqrt((fx * ax) ** 2 + (fy * ay) ** 2)
    f[0, 0] = 1.0
    spec = np.fft.fft2(white) / f ** beta
    spec[0, 0] = 0
    img = np.real(np.fft.ifft2(spec))
    img -= img.min()
    img /= img.max() + 1e-9
    return img


def band(n, lo, hi, seed=0, m=None):
    """指定の周波数帯だけのノイズ（0..1）"""
    m = m or n
    rng = np.random.default_rng(seed)
    white = rng.standard_normal((m, n))
    fy = np.fft.fftfreq(m)[:, None] * m
    fx = np.fft.fftfreq(n)[None, :] * n
    f = np.sqrt(fx ** 2 + fy ** 2)
    spec = np.fft.fft2(white) * ((f >= lo) & (f <= hi))
    img = np.real(np.fft.ifft2(spec))
    img -= img.min()
    img /= img.max() + 1e-9
    return img


def normal_map(h, strength):
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5 * strength
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5 * strength
    nx, ny, nz = -dx, dy, np.ones_like(h)
    ln = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    n = np.stack([nx / ln, ny / ln, nz / ln], axis=-1)
    return Image.fromarray(((n * 0.5 + 0.5) * 255).clip(0, 255).astype(np.uint8))


def rgb(arr):
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))


def save(name, img, normal=None):
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(UNITY, exist_ok=True)
    img.save(os.path.join(OUT, name + ".png"))
    img.save(os.path.join(UNITY, name + ".png"))
    if normal is not None:
        normal.save(os.path.join(OUT, name + "_n.png"))
        normal.save(os.path.join(UNITY, name + "_n.png"))
    print("OK", name)


def colorize(t, c0, c1):
    t = t[..., None]
    return np.array(c0)[None, None, :] * (1 - t) + np.array(c1)[None, None, :] * t


# ---------------------------------------------------------------- 各テクスチャ

def wallpaper(n=1024):
    """布目調のビニール壁紙（1枚 = 0.8m）。暗い灰緑、控えめな縦縞と経年のむら"""
    x = np.arange(n)[None, :] / n
    weave = fbm(n, 0.35, 11, ax=1.0, ay=3.0) * 0.5 + fbm(n, 0.35, 12, ax=3.0, ay=1.0) * 0.5
    grain = 0.5 + 0.5 * np.sin(2 * np.pi * x * 20)                      # 4cm ピッチの縦縞
    stripe = np.clip((grain - 0.5) * 3 + 0.5, 0, 1)
    pin = np.exp(-((((x * 20) % 1.0) - 0.5) ** 2) / 0.0008)             # 細い筋
    blot = fbm(n, 2.6, 13)
    streak = fbm(n, 2.2, 14, ax=0.25, ay=1.0)                            # 上下に流れる汚れ
    base = np.array([96, 104, 92], dtype=float)
    lum = 1.0 + (weave - 0.5) * 0.10 + (stripe - 0.5) * 0.05 - pin * 0.05 \
        + (blot - 0.5) * 0.14 + (streak - 0.5) * 0.10
    col = base[None, None, :] * lum[..., None]
    col[..., 0] += (blot - 0.5) * 8          # むらは少し黄ばむ
    h = weave * 0.8 + stripe * 0.25
    save("wallpaper", rgb(col), normal_map(h, 3.0))


def carpet(n=1024):
    """短いループパイル（1枚 = 1m）。ベージュ"""
    y, x = np.mgrid[0:n, 0:n] / n
    jit = fbm(n, 1.0, 21) * 0.6
    loops = (np.sin(2 * np.pi * (x * 256 + jit)) * np.sin(2 * np.pi * (y * 256 + jit * 0.7))) ** 2
    fiber = fbm(n, 0.2, 22)
    tone = fbm(n, 2.0, 23)
    base = np.array([176, 162, 136], dtype=float)
    lum = 0.86 + loops * 0.14 + (fiber - 0.5) * 0.22 + (tone - 0.5) * 0.10
    col = base[None, None, :] * lum[..., None]
    h = loops * 0.6 + fiber * 0.5
    save("carpet", rgb(col), normal_map(h, 4.0))


def walnut(n=1024):
    """ウォールナットの木目（1枚 = 1m、木目は U 方向）。
    規則的な波にならないよう、蛇行の大きい年輪＋流れ方向に長い筋を重ねる"""
    y, x = np.mgrid[0:n, 0:n] / n
    warp = (fbm(n, 2.6, 31, ax=10.0, ay=1.0) - 0.5) * 0.9
    w = y * 9 + warp * 4
    ring = 0.5 + 0.5 * np.sin(2 * np.pi * w)
    ring = np.clip((ring - 0.55) * 4, 0, 1)                             # 細く暗い年輪の線
    streak = fbm(n, 1.1, 32, ax=40.0, ay=1.0)                            # 長い筋（x方向に滑らか）
    streak2 = fbm(n, 0.7, 33, ax=25.0, ay=1.0)
    pores = fbm(n, 0.3, 35, ax=10.0, ay=1.0)
    fig = fbm(n, 2.2, 34, ax=3.0, ay=1.0)
    t = 0.55 + (streak - 0.5) * 0.9 + (streak2 - 0.5) * 0.35 + (fig - 0.5) * 0.4 - ring * 0.22 - (pores > 0.62) * 0.08
    t = np.clip(t, 0, 1)
    col = colorize(t, (52, 32, 22), (136, 94, 62))
    h = streak2 * 0.5 + (pores > 0.62) * -0.3 + ring * -0.2
    save("walnut", rgb(col), normal_map(h, 1.2))


def quilt(n=1024, seed=41):
    """くすんだ灰緑地にベージュの小花（1枚 = 0.45m）"""
    rnd = random.Random(seed)
    S = n * 2
    img = Image.new("RGB", (S, S), (132, 140, 120))
    d = ImageDraw.Draw(img)

    def petal(cx, cy, ang, r, w, col):
        pts = []
        for k in range(18):
            a = k / 18 * 2 * math.pi
            px, py = math.cos(a) * r * 0.5 + r * 0.5, math.sin(a) * w * 0.5
            pts.append((cx + px * math.cos(ang) - py * math.sin(ang), cy + px * math.sin(ang) + py * math.cos(ang)))
        d.polygon(pts, fill=col)

    def flower(cx, cy, r):
        for ox in (-S, 0, S):
            for oy in (-S, 0, S):
                x0, y0 = cx + ox, cy + oy
                if -r * 3 < x0 < S + r * 3 and -r * 3 < y0 < S + r * 3:
                    rot = rnd.random() * math.pi
                    for k in range(2):                                   # 葉
                        a = rot + k * math.pi + 0.8
                        petal(x0, y0, a, r * 1.7, r * 0.55, (88, 104, 82))
                    for k in range(5):
                        a = rot + k * 2 * math.pi / 5
                        petal(x0, y0, a, r, r * 0.62, (214, 202, 172))
                    d.ellipse([x0 - r * 0.22, y0 - r * 0.22, x0 + r * 0.22, y0 + r * 0.22], fill=(176, 138, 82))

    for _ in range(46):
        flower(rnd.random() * S, rnd.random() * S, rnd.uniform(44, 62))
    for _ in range(40):
        flower(rnd.random() * S, rnd.random() * S, rnd.uniform(24, 32))
    for _ in range(220):                                                 # 小さな点
        cx, cy = rnd.random() * S, rnd.random() * S
        r = rnd.uniform(5, 9)
        for ox in (-S, 0, S):
            for oy in (-S, 0, S):
                d.ellipse([cx + ox - r, cy + oy - r, cx + ox + r, cy + oy + r], fill=(196, 184, 154))
    img = img.resize((n, n), Image.LANCZOS)
    a = np.asarray(img).astype(float)
    y, x = np.mgrid[0:n, 0:n] / n
    weave = (np.sin(2 * np.pi * x * 340) * np.sin(2 * np.pi * y * 340)) * 0.5 + 0.5
    fade = fbm(n, 2.2, 42)
    a *= (0.93 + weave[..., None] * 0.07) * (0.94 + fade[..., None] * 0.1)
    save("quilt", rgb(a), normal_map(weave * 0.5 + fbm(n, 0.5, 43) * 0.5, 1.2))


def linen(n=1024):
    """生成りの麻布（1枚 = 0.5m）"""
    y, x = np.mgrid[0:n, 0:n] / n
    slub_x = fbm(n, 1.2, 51, ax=0.02, ay=1.0)
    slub_y = fbm(n, 1.2, 52, ax=1.0, ay=0.02)
    wx = 0.5 + 0.5 * np.sin(2 * np.pi * (x * 256 + (slub_y - 0.5) * 0.8))
    wy = 0.5 + 0.5 * np.sin(2 * np.pi * (y * 256 + (slub_x - 0.5) * 0.8))
    weave = np.where(((np.floor(x * 256) + np.floor(y * 256)) % 2) == 0, wx, wy)
    base = np.array([226, 219, 201], dtype=float)
    lum = 0.92 + weave * 0.07 + (slub_x - 0.5) * 0.035 + (slub_y - 0.5) * 0.035 + (fbm(n, 0.4, 53) - 0.5) * 0.05
    save("linen", rgb(base[None, None, :] * lum[..., None]), normal_map(weave, 1.5))


def plaster():
    src = os.path.join(HERE, "..", "tex_wall_seamless.png")
    im = Image.open(src).convert("RGB").resize((1024, 1024), Image.LANCZOS)
    a = np.asarray(im).astype(float)
    g = a.mean(axis=2, keepdims=True)
    a = g * 0.85 + a * 0.15
    a = (a - a.mean()) * 0.8 + 218
    h = (g[..., 0] - g.min()) / (g.max() - g.min() + 1e-9)
    save("plaster", rgb(a), normal_map(h, 2.0))


def stain(n=512, seed=61):
    """雨漏りの染み（アルファ付き、タイルしない）"""
    y, x = np.mgrid[0:n, 0:n] / n
    r = np.sqrt((x - 0.5) ** 2 + (y - 0.5) ** 2) * 2
    nz = fbm(n, 1.8, seed)
    field = 1 - r + (nz - 0.5) * 0.9
    inside = np.clip((field - 0.25) * 6, 0, 1)
    rings = np.zeros_like(field)
    for lv in (0.25, 0.4, 0.52):
        rings += np.exp(-((field - lv) ** 2) / 0.0006)
    alpha = np.clip(inside * 0.16 + rings * 0.32, 0, 0.55) * np.clip((1 - r) * 3, 0, 1)
    col = np.zeros((n, n, 4))
    col[..., 0] = 74 + nz * 20
    col[..., 1] = 62 + nz * 16
    col[..., 2] = 44 + nz * 10
    col[..., 3] = alpha * 255
    img = Image.fromarray(np.clip(col, 0, 255).astype(np.uint8), "RGBA")
    save("stain", img)


def _font(size, mincho=True):
    cands = (["yumin.ttf", "msmincho.ttc", "YuMincho.ttc"] if mincho else ["YuGothM.ttc", "msgothic.ttc", "meiryo.ttc"])
    for f in cands:
        p = os.path.join(r"C:\Windows\Fonts", f)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def newspaper(w=1024, h=742):
    """新聞の切り抜き（縦書き）。見出しはゲーム内の記事と同じ"""
    rnd = random.Random(71)
    y, x = np.mgrid[0:h, 0:w]
    paper = fbm(w, 1.8, 72, m=h)
    base = np.stack([224 + paper * 10, 214 + paper * 10, 186 + paper * 8], axis=-1)
    img = rgb(base)
    d = ImageDraw.Draw(img)
    ink = (38, 34, 32)

    def vtext(x0, y0, text, size, font, pitch=None):
        pitch = pitch or size
        cy = y0
        for ch in text:
            if ch == "\n":
                break
            d.text((x0, cy), ch, font=font, fill=ink)
            cy += pitch
        return cy

    head = _font(64)
    sub = _font(34)
    body = _font(19)
    # 右から左へ：見出し → 副見出し → 本文の段
    vtext(w - 90, 28, "小川脳神経総合研究所", 64, head, 66)
    vtext(w - 150, 40, "臨床試験を再開", 34, sub, 38)
    d.line([(w - 168, 20), (w - 168, h - 20)], fill=ink, width=2)
    article = ("同研究所は脳神経とＡＩの融合を掲げ記憶領域への介入実験を進めていたとされる。"
               "関係者によれば被験者の一部に眠りから覚めない症例が報告されており、"
               "研究所は取材に対し「安全性は確認されている」と回答した。")
    filler = "市内の研究機関をめぐっては昨年来の説明不足を指摘する声が相次ぎ地域住民の間に不安が広がっている。"
    text = article + filler * 6
    col_h = 24
    xcol = w - 196
    k = 0
    for block in range(2):
        ytop = 24 + block * (h // 2)
        for c in range(34):
            if xcol - c * 23 < 20:
                break
            seg = text[k:k + col_h]
            k += col_h
            vtext(xcol - c * 23, ytop, seg, 19, body, 20)
        d.line([(20, ytop + col_h * 20 + 8), (w - 176, ytop + col_h * 20 + 8)], fill=ink, width=1)
    # 写真の網点ブロック
    px0, py0 = 40, 30
    ph = fbm(180, 1.4, 73, m=150)
    for yy in range(0, 150, 4):
        for xx in range(0, 180, 4):
            r = (1 - ph[yy, xx]) * 1.9
            d.ellipse([px0 + xx - r, py0 + yy - r, px0 + xx + r, py0 + yy + r], fill=ink)
    img = img.filter(ImageFilter.GaussianBlur(0.4))
    a = np.asarray(img).astype(float)
    yellow = fbm(w, 2.4, 74, m=h)[..., None]
    a = a * (0.93 + yellow * 0.07) - np.array([0, 3, 10]) * yellow
    save("newspaper", rgb(a))


def crt_screen(w=512, h=384):
    """記録端末の画面（緑の文字・走査線・周辺減光）"""
    img = Image.new("RGB", (w, h), (4, 14, 8))
    d = ImageDraw.Draw(img)
    f = _font(22, mincho=False)
    for p in (r"C:\Windows\Fonts\consola.ttf", r"C:\Windows\Fonts\cour.ttf"):
        if os.path.exists(p):
            f = ImageFont.truetype(p, 22)
            break
    lines = ["RENASCITA  MEMORY TERMINAL", "v0.9  (c)2015 OGAWA INST.", "-" * 26, "SUBJECT   NINOMIYA H.",
             "SESSION   0001", "STATUS    AWAKE", "", "> RECORD STATE", "  READY_"]
    for i, s in enumerate(lines):
        d.text((34, 30 + i * 34), s, font=f, fill=(120, 255, 150))
    glow = img.filter(ImageFilter.GaussianBlur(6))
    a = np.asarray(img).astype(float) * 0.85 + np.asarray(glow).astype(float) * 0.9
    yy, xx = np.mgrid[0:h, 0:w]
    scan = 0.82 + 0.18 * (np.sin(yy * np.pi / 1.5) ** 2)
    vig = 1 - (((xx / w - 0.5) * 1.6) ** 4 + ((yy / h - 0.5) * 1.8) ** 4)
    a = a * scan[..., None] * np.clip(vig, 0.2, 1)[..., None] + np.array([2, 10, 5])
    save("crt_screen", rgb(a))


if __name__ == "__main__":
    wallpaper(); carpet(); walnut(); quilt(); linen(); plaster(); stain(); newspaper(); crt_screen()
