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

import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, band, normal_map, rgb, colorize, _font, Saver  # noqa: E402

save = Saver(HERE, "Dim")


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
    # 右から左へ：見出し → 副見出し → 本文の段（本文はゲームの資料 進行/文書/ja/doc_dim.news.body.txt と同じ。縦書きなので数字は全角）
    vtext(w - 90, 28, "三木脳神経総合研究所", 64, head, 66)
    vtext(w - 150, 40, "臨床実験を再開", 34, sub, 38)
    d.line([(w - 168, 20), (w - 168, h - 20)], fill=ink, width=2)
    article = ("三木脳神経総合研究所（所在地・ＸＸ県）は２２日、脳神経活動に関する新たな治療法の確立を目指す臨床実験を再開したと発表した。"
               "一方、関係者によると、実験に参加した被験者３名が意識を回復しない状態に陥っていることが明らかになった。"
               "同研究所は、脳の神経活動を解析・制御することで、意識障害の治療や脳機能の回復を目指す研究を進めてきた。"
               "今回の臨床実験は、過去に実施された研究計画を発展させたもので、長期にわたり意識の回復が見られない患者への応用も視野に入れているという。"
               "しかし、複数の関係者の証言によれば、実験終了後も目を覚まさない被験者が確認されており、医療関係者の間では安全性を懸念する声が上がっている。"
               "被験者らは生命維持に必要な身体機能を保っているものの、外部からの呼びかけに対する反応は無く、意識状態の詳細については分かっていない。"
               "同研究所の三木宗英主任は取材に対し、「現在、原因の特定と被験者の状態")
    filler = "市内の研究機関をめぐっては昨年来の説明不足を指摘する声が相次ぎ地域住民の間に不安が広がっている。"
    # 下の段は別の地域の記事（同じ文の繰り返しにならないよう何種類かを順に流す）
    others = [filler,
              "市立図書館の改修工事が来月から始まり、期間中は駅前の分館で貸し出しを続ける。開館時間は午前十時から午後六時まで。",
              "北町の商店街で恒例の秋祭りが開かれ、親子連れなど約三千人が訪れた。夜には提灯の行列が通りを練り歩いた。",
              "県警は二十三日、市内の交差点で発生した交通事故について、信号機の不具合が原因とみて調べを進めている。",
              "港の再開発計画をめぐり、市は住民向けの説明会を来月二回開く。商業施設と公園を一体で整備する方針。",
              "気象台によると、週末は寒気の影響で冷え込みが強まる見込み。山沿いでは初雪の可能性もあるという。"]
    text = article + "".join(others[i % len(others)] for i in range(24))
    col_h = (h // 2 - 44) // 20   # 1段の文字数（上下2段が紙の半分ずつに収まる）
    xcol = w - 196
    k = 0
    for block in range(2):
        ytop = 24 + block * (h // 2)
        for c in range(34):
            # 上の段は左上の写真の手前で止める（写真に文字を重ねない）
            if xcol - c * 23 < (236 if block == 0 else 20):
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


# ---------------------------------------------------------------- 四つ折りの新聞（縦書きの紙面）

_TURNED = "ー－―—…‥〜～「」『』（）［］【】〈〉《》〔〕＝：；"
_CORNER = "、。，．"
_SMALL = "ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ"
_NOSTART = "、。，．」』）】〉》・ーっゃゅょぁぃぅぇぉッャュョァィゥェォ！？…‥"
_NOEND = "「『（【〈《"
_FONTS = r"C:\Windows\Fonts"


def _tt(name, size, index=0):
    path = os.path.join(_FONTS, name)
    return ImageFont.truetype(path, size, index=index) if os.path.exists(path) else _font(size)


def _zen(s):
    s = s.replace("...", "…")
    return "".join(chr(ord(c) + 0xFEE0) if "!" <= c <= "~" else ("　" if c == " " else c) for c in s)


class _VPen:
    """縦書き：字を上から下へ、列を右から左へ。「ー」「（」などは横倒し、「、。」は字の枠の右上へ"""

    def __init__(self, img):
        self.img = img
        self.d = ImageDraw.Draw(img)

    def char(self, x, y, ch, font, size, fill):
        if ch in "　 ":
            return
        if ch in _TURNED:
            tile = Image.new("L", (size * 2, size * 2), 0)
            ImageDraw.Draw(tile).text((size // 2, size // 2), ch, font=font, fill=255)
            tile = tile.rotate(-90, resample=Image.BICUBIC)
            self.img.paste(Image.new("RGB", tile.size, fill), (int(x - size // 2), int(y - size // 2)), tile)
        elif ch in _CORNER:
            self.d.text((x + size * 0.56, y - size * 0.56), ch, font=font, fill=fill)
        elif ch in _SMALL:
            self.d.text((x + size * 0.12, y - size * 0.1), ch, font=font, fill=fill)
        else:
            self.d.text((x, y), ch, font=font, fill=fill)

    def column(self, x, y, text, font, size, fill, pitch=None):
        for i, ch in enumerate(text):
            self.char(x, y + i * (pitch or size), ch, font, size, fill)


def _columns(text, n):
    """1段 n 字の列に分ける（句読点はぶら下げ、ほかの行頭禁則は追い出し、行末禁則は次の列へ）"""
    cols, i = [], 0
    while i < len(text):
        k = min(n, len(text) - i)
        if i + k < len(text) and text[i + k] in _NOSTART:
            if text[i + k] in "、。，．":
                k += 1
            elif k > 1:
                k -= 1
        if k > 1 and i + k < len(text) and text[i + k - 1] in _NOEND:
            k -= 1
        cols.append(text[i:i + k])
        i += k
    return cols


class _Flow:
    """段落の列を、いくつかの段（x の右端・左端・y）へ順に流し込む"""

    def __init__(self, pen, font, size, pitch, n, ink):
        self.pen, self.font, self.size, self.pitch, self.n, self.ink = pen, font, size, pitch, n, ink
        self.cols = []

    def feed(self, paras):
        for para in paras:
            self.cols += _columns("　" + _zen(para), self.n)

    def fill(self, x_right, x_left, y):
        x = x_right - self.size
        while self.cols and x >= x_left - self.size * 0.5:
            self.pen.column(x, y, self.cols.pop(0), self.font, self.size, self.ink)
            x -= self.pitch
        return x


def _halftone(img, box, seed, kind="building"):
    """網点の写真（建物の外観／人の集まり）"""
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    yy, xx = np.mgrid[0:h, 0:w]
    u, v = xx / w, yy / h
    tone = 0.55 + 0.25 * (fbm(max(w, h), 1.6, seed)[:h, :w] - 0.5)
    if kind == "building":
        sky = v < 0.32 + 0.05 * np.sin(u * 7)
        tone = np.where(sky, 0.22 + 0.1 * v, tone)
        body = (~sky) & (v < 0.86)
        win = body & (((u * 14) % 1.0) < 0.42) & (((v * 9) % 1.0) < 0.5)
        tone = np.where(body, 0.62 + 0.08 * (u - 0.5), tone)
        tone = np.where(win, 0.85, tone)
        tone = np.where(v >= 0.86, 0.5 + 0.15 * fbm(max(w, h), 1.2, seed + 1)[:h, :w], tone)
    d = ImageDraw.Draw(img)
    d.rectangle([x0, y0, x1, y1], fill=(222, 214, 192))
    step = 4
    for yy_ in range(0, h, step):
        for xx_ in range(0, w, step):
            r = tone[yy_, xx_] * step * 0.62
            if r > 0.3:
                d.ellipse([x0 + xx_ - r, y0 + yy_ - r, x0 + xx_ + r, y0 + yy_ + r], fill=(40, 36, 33))
    d.rectangle([x0, y0, x1, y1], outline=(40, 36, 33), width=1)


def newspaper_fold(w=1024, h=768):
    """四つ折りの新聞の紙面（横 2w：左＝表、右＝裏）。表は1面の右上の四つ割り（題字と、ゲームの記事の見出し・本文）、
    裏は地域面の左上の四つ割り。上と右が紙の端、左と下が折り目（文字は折り目で切れる）。
    本文はゲームの資料（進行/文書/ja/doc_dim.news.body.txt）と同じ"""
    W = w * 2
    paper = fbm(W, 1.8, 81, m=h)
    base = np.stack([226 + paper * 9, 217 + paper * 9, 192 + paper * 8], axis=-1)
    img = rgb(base)
    pen = _VPen(img)
    d = pen.d
    ink = (34, 30, 28)
    paper_c = (224, 216, 192)
    head = _tt("yumindb.ttf", 66)
    side = _tt("yumindb.ttf", 42)
    band = _tt("YuGothB.ttc", 34)
    mast = _tt("yumindb.ttf", 84)
    body = _tt("yumin.ttf", 18)
    small = _tt("YuGothM.ttc", 15)
    tiny = _tt("YuGothM.ttc", 13)
    hd2 = _tt("YuGothB.ttc", 30)
    hd3 = _tt("yumindb.ttf", 34)

    doc = os.path.join(os.path.dirname(HERE), "..", "進行", "文書", "ja", "doc_dim.news.body.txt")
    with open(doc, encoding="utf-8-sig") as fp:
        lines = fp.read().replace("\r", "").split("\n\n")
    article = [s.strip() for s in lines[1:] if s.strip()]   # 1行目は日付
    others = ["市内の研究機関をめぐっては昨年来の説明不足を指摘する声が相次ぎ地域住民の間に不安が広がっている。",
              "市立図書館の改修工事が来月から始まり、期間中は駅前の分館で貸し出しを続ける。開館時間は午前十時から午後六時まで。",
              "北町の商店街で恒例の秋祭りが開かれ、親子連れなど約三千人が訪れた。夜には提灯の行列が通りを練り歩いた。",
              "県警は二十三日、市内の交差点で発生した交通事故について、信号機の不具合が原因とみて調べを進めている。",
              "港の再開発計画をめぐり、市は住民向けの説明会を来月二回開く。商業施設と公園を一体で整備する方針。",
              "気象台によると、週末は寒気の影響で冷え込みが強まる見込み。山沿いでは初雪の可能性もあるという。"]

    # ======== 表（左半分）：1面の右上 ========
    # 題字の箱（右上の角）
    mx0, mx1, my0, my1 = 874, 1000, 22, 300
    d.rectangle([mx0, my0, mx1, my1], outline=ink, width=3)
    d.rectangle([mx0 + 6, my0 + 6, mx1 - 6, my1 - 6], outline=ink, width=1)
    pen.column(mx0 + 21, my0 + 22, "朝刊", mast, 84, ink, 90)
    pen.column(mx0 + 52, my0 + 214, "日刊", tiny, 13, ink, 14)
    pen.column(mx0 + 34, my0 + 214, "第３種郵便", tiny, 13, ink, 13)
    # 日付と号
    pen.column(978, 318, "２０ＸＸ年", small, 15, ink, 16)
    pen.column(958, 318, "１１月２５日", small, 15, ink, 16)
    pen.column(938, 318, "（朝刊）", small, 15, ink, 16)
    # きょうの紙面
    ix0, iy0, ix1, iy1 = 874, 470, 1000, 748
    d.rectangle([ix0, iy0, ix1, iy1], outline=ink, width=2)
    d.rectangle([ix0, iy0, ix1, iy0 + 34], fill=ink)
    d.text((ix0 + 14, iy0 + 7), "きょうの紙面", font=small, fill=paper_c)
    for k, (a, b) in enumerate([("社会", "２７"), ("地域", "２４"), ("天気", "２８"), ("テレビ", "３２"), ("くらし", "１９")]):
        pen.column(ix1 - 24 - k * 23, iy0 + 46, a + "　" + b, tiny, 13, ink, 14)

    # この記事の見出し（大見出し・袖見出し・黒地に白抜き）
    pen.column(784, 26, "臨床実験を再開", head, 66, ink, 66)
    pen.column(728, 46, "三木脳神経総合研究所", side, 42, ink, 43)
    bx0, by0 = 662, 40
    sub = "被験者３名　意識戻らず"
    d.rectangle([bx0, by0, bx0 + 52, by0 + len(sub) * 36 + 26], fill=ink)
    pen.column(bx0 + 9, by0 + 14, sub, band, 34, paper_c, 36)

    # 写真（左上。左の端は折り目で切れる）と説明
    _halftone(img, (0, 26, 262, 410), 82)
    pen.column(270, 30, "再開が発表された研究所＝ＸＸ県", tiny, 13, ink, 14)

    # 本文：1〜2段は写真と見出しの間、3〜4段は見出しの下から左の折り目まで
    flow = _Flow(pen, body, 18, 27, 12, ink)
    flow.feed(article + others)
    tier = [24, 254, 484, 714]
    flow.fill(640, 300, tier[0])
    flow.fill(640, 300, tier[1])
    flow.fill(860, 0, tier[2])
    d.line([(0, 704), (860, 704)], fill=ink, width=1)   # 下の段は別の記事
    flow.cols = []
    flow.feed(others[1:] + others)
    flow.fill(860, 0, tier[3])
    for y in (244, 474):
        d.line([(300, y), (640, y)], fill=(120, 112, 100), width=1)

    # ======== 裏（右半分）：地域面の左上 ========
    ox = w
    # 柱（左上）
    d.text((ox + 24, 18), "２４　　地　域", font=small, fill=ink)
    d.text((ox + 420, 18), "２０ＸＸ年１１月２５日（朝刊）", font=small, fill=ink)
    d.line([(ox + 20, 44), (ox + w, 44)], fill=ink, width=2)
    d.line([(ox + 20, 49), (ox + w, 49)], fill=ink, width=1)
    flow2 = _Flow(pen, body, 18, 27, 12, ink)
    # 記事1：右上（見出しは2段にまたがる）
    pen.column(ox + w - 40, 66, "駅前再開発", hd3, 34, ink, 35)
    pen.column(ox + w - 84, 96, "来月説明会", hd2, 30, ink, 31)
    flow2.feed([others[4], others[0]])
    flow2.fill(ox + w - 110, ox + 560, 62)
    flow2.fill(ox + w - 110, ox + 560, 292)
    d.line([(ox + 540, 60), (ox + 540, 520)], fill=ink, width=1)
    # 記事2
    pen.column(ox + 504, 66, "秋祭りに三千人", hd3, 34, ink, 35)
    flow2.cols = []
    flow2.feed([others[2], others[5]])
    flow2.fill(ox + 470, ox + 260, 62)
    flow2.fill(ox + 470, ox + 260, 292)
    d.line([(ox + 240, 60), (ox + 240, 520)], fill=ink, width=1)
    # 記事3（左は折り目で切れる）
    pen.column(ox + 204, 66, "図書館を改修", hd2, 30, ink, 31)
    flow2.cols = []
    flow2.feed([others[1], others[3]])
    flow2.fill(ox + 170, ox + 20, 62)
    flow2.fill(ox + 170, ox + 20, 292)
    d.line([(ox + 20, 530), (ox + w, 530)], fill=ink, width=1)
    # 下：広告枠（献血のお願い）と記事の続き
    ax0, ay0, ax1, ay1 = ox + 24, 548, ox + 420, 768
    d.rectangle([ax0, ay0, ax1, ay1 + 10], outline=ink, width=3)
    pen.column(ax1 - 64, ay0 + 18, "献血に", hd3, 40, ink, 42)
    pen.column(ax1 - 114, ay0 + 38, "ご協力を", hd3, 40, ink, 42)
    pen.column(ax1 - 170, ay0 + 22, "市保健センター", small, 15, ink, 16)
    pen.column(ax1 - 192, ay0 + 22, "毎週土曜　午前十時から", small, 15, ink, 16)
    d.ellipse([ax0 + 40, ay0 + 40, ax0 + 170, ay0 + 170], outline=ink, width=3)
    d.text((ax0 + 78, ay0 + 86), "献血", font=hd2, fill=ink)
    flow2.cols = []
    flow2.feed(others[::-1] + others)
    flow2.fill(ox + w - 20, ox + 440, 548)

    img = img.filter(ImageFilter.GaussianBlur(0.35))
    a = np.asarray(img).astype(float)
    yellow = fbm(W, 2.4, 84, m=h)[..., None]
    a = a * (0.94 + yellow * 0.06) - np.array([0, 3, 9]) * yellow
    # 折り目の際はわずかに影（表は左と下、裏は右と下＝裏から見て表の左の折り目が右に来る）
    xs = np.arange(W)[None, :]
    xs = np.where(xs < w, xs, W - 1 - xs)
    ys = np.arange(h)[:, None]
    fold = np.clip(1 - np.minimum(xs / 26.0, 1), 0, 1) * 0.08 + np.clip(1 - np.minimum((h - 1 - ys) / 26.0, 1), 0, 1) * 0.08
    a = a * (1 - fold[..., None])
    save("newspaper_fold", rgb(a))


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
    wallpaper(); carpet(); walnut(); quilt(); linen(); plaster(); stain(); newspaper(); newspaper_fold(); crt_screen()
