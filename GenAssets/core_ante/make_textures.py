# -*- coding: utf-8 -*-
"""CORE前室（core_ante）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/core_ante/make_textures.py

出力: GenAssets/core_ante/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/CoreAnte/*.png
天井のコンクリートと亜鉛めっき鋼板は脳神経解析室（analysis）のものを使う。
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

save = Saver(HERE, "CoreAnte")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


def epoxy_floor(n=1024):
    """暗い灰色のエポキシ塗りの床（1枚 = 2m）。擦り傷・剥がれ・ひび"""
    y, x = np.mgrid[0:n, 0:n] / n
    tone = fbm(n, 2.4, 1101)
    grit = fbm(n, 0.05, 1102)
    scuff = np.clip(fbm(n, 1.0, 1103, ax=6.0, ay=1.0) - 0.62, 0, 1) * 4
    peel = np.clip(fbm(n, 2.0, 1104) - 0.74, 0, 1) * 4
    base = np.array([74, 78, 80], dtype=float)
    lum = 0.95 + (tone - 0.5) * 0.18 + (grit - 0.5) * 0.08 + scuff * 0.12
    col = base[None, None, :] * lum[..., None]
    conc = np.array([98, 97, 92], dtype=float)
    col = col * (1 - np.clip(peel, 0, 1)[..., None]) + conc[None, None, :] * (0.9 + grit[..., None] * 0.2) * np.clip(peel, 0, 1)[..., None]
    img = rgb(col)
    d = ImageDraw.Draw(img)
    rnd = random.Random(1105)
    for k in range(3):                                   # ひび
        px, py = rnd.uniform(0, n), rnd.uniform(0, n)
        pts = [(px, py)]
        for s in range(30):
            px += rnd.uniform(-14, 14) + 10; py += rnd.uniform(-14, 14) + 4
            pts.append((px % n, py % n))
        for a, b in zip(pts, pts[1:]):
            if abs(a[0] - b[0]) < 100 and abs(a[1] - b[1]) < 100:
                d.line([a, b], fill=(34, 36, 38), width=2)
    h = np.asarray(img).astype(float).mean(axis=-1) / 255
    save("epoxy_floor", img, normal_map(grit * 0.3 + h * 0.6 - peel * 0.2, 1.2))


def hazard(w=1024, h=128):
    """黄と黒の斜めの縞（1枚 = 幅1m x 0.125m）。擦れて下地が見える"""
    y, x = np.mgrid[0:h, 0:w] / np.array([h, w], dtype=float)[:, None, None]
    xx = np.mgrid[0:h, 0:w][1]; yy = np.mgrid[0:h, 0:w][0]
    band = (((xx + yy) // 64) % 2).astype(float)
    yel = np.array([226, 180, 30], dtype=float); blk = np.array([24, 24, 24], dtype=float)
    col = yel * band[..., None] + blk * (1 - band[..., None])
    wear = np.clip(fbm(w, 1.4, 1111, m=h) - 0.62, 0, 1) * 3
    col = col * (1 - np.clip(wear, 0, 0.8)[..., None]) + np.array([90, 90, 88]) * np.clip(wear, 0, 0.8)[..., None]
    save("hazard", rgb(col), normal_map(-wear, 1.0))


def block_wall(n=1024):
    """塗装したコンクリートブロックの壁（1枚 = 1.2m 四方、ブロック 0.4 x 0.2、半分ずらし）"""
    y, x = np.mgrid[0:n, 0:n] / n
    row = np.floor(y * 6)
    xo = (x * 3 + (row % 2) * 0.5) % 1
    yo = (y * 6) % 1
    joint = np.clip(1 - np.minimum(np.minimum(xo, 1 - xo) * 0.4, np.minimum(yo, 1 - yo) * 0.2) / 0.006, 0, 1)
    pores = fbm(n, 0.12, 1121)
    face = fbm(n, 1.6, 1122)
    base = np.array([112, 122, 126], dtype=float)
    lum = 0.94 + (pores - 0.5) * 0.12 + (face - 0.5) * 0.08 - joint * 0.2
    # 雨だれのような汚れ（上から下へ）
    drip = np.clip(fbm(n, 1.2, 1123, ax=0.08, ay=1.0) - 0.66, 0, 1) * 2.5
    lum -= drip * 0.06
    col = base[None, None, :] * lum[..., None]
    col[..., 0] += drip * 5                              # わずかに錆色
    save("block_wall", rgb(col), normal_map(pores * 0.4 - joint * 1.2, 2.0))


def steel_plate(n=1024):
    """塗装した鋼板（暗い灰緑、1枚 = 1m）。擦り傷と錆の筋"""
    y, x = np.mgrid[0:n, 0:n] / n
    tone = fbm(n, 2.2, 1131)
    scratch = np.clip(fbm(n, 0.8, 1132, ax=12.0, ay=1.0) - 0.66, 0, 1) * 4
    rust = np.clip(fbm(n, 1.4, 1133, ax=0.1, ay=1.0) - 0.62, 0, 1) * 3
    base = np.array([62, 72, 70], dtype=float)
    lum = 0.95 + (tone - 0.5) * 0.14 + scratch * 0.35
    col = base[None, None, :] * lum[..., None]
    col = col * (1 - np.clip(rust, 0, 0.7)[..., None]) + np.array([110, 60, 34]) * np.clip(rust, 0, 0.7)[..., None]
    save("steel_plate", rgb(col), normal_map(tone * 0.2 + scratch * 0.3 - rust * 0.3, 1.0))


def screen_list(w=640, h=512):
    """BRAIN DATA 登録一覧（非常電源で暗く表示。登録者は削除）"""
    img = Image.new("RGB", (w, h), (6, 10, 16))
    d = ImageDraw.Draw(img)
    for gy in range(0, h, 4):
        d.line([(0, gy), (w, gy)], fill=(9, 14, 22))
    d.rectangle([0, 0, w, 44], fill=(60, 18, 16))
    d.text((14, 8), "非常電源運転中　表示制限", font=_font(24, mincho=False), fill=(255, 150, 130))
    d.text((24, 70), "BRAIN DATA 登録一覧", font=_font(34, mincho=False), fill=(150, 200, 230))
    f = _font(28, mincho=False)
    for i in range(3):
        y = 150 + i * 80
        d.text((34, y), f"BRAIN DATA:0{i + 1}", font=f, fill=(150, 200, 230))
        d.text((300, y), "登録者:", font=f, fill=(150, 200, 230))
        d.rectangle([410, y + 4, 590, y + 34], fill=(60, 70, 80))
    d.text((34, 420), "登録者情報は削除されています", font=_font(22, mincho=False), fill=(200, 120, 110))
    save("screen_list", img)


def screen_mail(w=640, h=512):
    """未送信メッセージ（送信トレイ。本文が途中で途切れる）"""
    img = Image.new("RGB", (w, h), (10, 12, 16))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 44], fill=(30, 50, 80))
    d.text((14, 8), "送信トレイ（1）　未送信", font=_font(24, mincho=False), fill=(200, 220, 240))
    d.text((24, 64), "宛先: 所長", font=_font(26, mincho=False), fill=(170, 190, 210))
    d.line([(20, 104), (w - 20, 104)], fill=(60, 70, 90), width=2)
    f = _font(28, mincho=False)
    for i, s in enumerate(["主任、あの人を止めてください。", "私はもう、黙っていられません。", "あの人は間違っています。でも──"]):
        d.text((30, 130 + i * 56), s, font=f, fill=(200, 210, 220))
    d.rectangle([30, 310, 44, 344], fill=(200, 210, 220))            # カーソル
    d.rectangle([w - 170, h - 70, w - 30, h - 30], outline=(90, 110, 140), width=2)
    d.text((w - 140, h - 64), "送信", font=_font(24, mincho=False), fill=(90, 110, 140))
    save("screen_mail", img)


def screen_crack(n=512):
    """割れた黒い画面（ひびの線、アルファなし）"""
    img = Image.new("RGB", (n, n), (8, 9, 11))
    d = ImageDraw.Draw(img)
    rnd = random.Random(1141)
    cx, cy = n * 0.62, n * 0.4
    for k in range(14):
        a = rnd.uniform(0, math.pi * 2)
        px, py = cx, cy
        for s in range(12):
            a += rnd.uniform(-0.35, 0.35)
            nx, ny = px + math.cos(a) * rnd.uniform(15, 40), py + math.sin(a) * rnd.uniform(15, 40)
            d.line([(px, py), (nx, ny)], fill=(90, 96, 104), width=1)
            px, py = nx, ny
    for r in (18, 34):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=(70, 76, 84), width=1)
    save("screen_crack", img)


def signs():
    """MAIN CORE のステンシル板・区画表示・配管ラベル"""
    img = Image.new("RGB", (1024, 256), (40, 42, 44))
    d = ImageDraw.Draw(img)
    d.rectangle([8, 8, 1016, 248], outline=(200, 60, 50), width=10)
    d.text((80, 40), "MAIN CORE", font=_font(150, mincho=False), fill=(214, 70, 58))
    save("plate_core", img)
    img = Image.new("RGB", (1024, 320), (230, 226, 214))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 1024, 90], fill=(200, 40, 32))
    d.text((30, 14), "立入制限区域", font=_font(60, mincho=False), fill=(255, 255, 255))
    d.text((30, 120), "B2  CORE 前室", font=_font(72, mincho=False), fill=(30, 30, 30))
    d.text((30, 228), "許可なき者の立入を禁ず　小川脳神経総合研究所", font=_font(34, mincho=False), fill=(60, 60, 60))
    save("sign_restricted", img)
    img = Image.new("RGB", (1024, 128), (40, 110, 180))
    d = ImageDraw.Draw(img)
    for k in range(3):
        x0 = 40 + k * 340
        d.text((x0, 26), "冷却水 送", font=_font(60, mincho=False), fill=(255, 255, 255))
        d.polygon([(x0 + 270, 40), (x0 + 310, 64), (x0 + 270, 88)], fill=(255, 255, 255))
    save("pipe_label", img)
    img = Image.new("RGB", (512, 256), (60, 64, 66))
    d = ImageDraw.Draw(img)
    f = _font(40, mincho=False)
    for i, s in enumerate(["系統別給電盤", "非常電源 → 各系統"]):
        d.text((30, 40 + i * 80), s, font=f, fill=(230, 230, 220))
    d.rectangle([20, 200, 492, 236], fill=(226, 180, 30))
    d.text((40, 202), "順序を誤ると不安定化", font=_font(28, mincho=False), fill=(20, 20, 20))
    save("panel_label", img)


def console_panel(w=1024, h=256):
    """制御卓の傾斜パネル（印刷の枠線と小さな文字。ボタンは形で作る）"""
    img = Image.new("RGB", (w, h), (70, 74, 78))
    d = ImageDraw.Draw(img)
    f = _font(18, mincho=False)
    labels = ["記憶野", "言語野", "自己認識", "同期", "負荷", "冷却", "警報", "予備"]
    for i, s in enumerate(labels):
        x = 30 + i * 124
        d.rectangle([x, 30, x + 110, 220], outline=(150, 156, 160), width=2)
        d.text((x + 8, 36), s, font=f, fill=(210, 214, 216))
    a = np.asarray(img).astype(float)
    a *= (0.92 + fbm(w, 1.8, 1151, m=h)[..., None] * 0.16)
    save("console_panel", rgb(a))


def manual():
    """運用手順書（左頁：表紙の裏、右頁：3-1〜4-3 の抜粋）"""
    w, h = 1480, 1040
    img = Image.new("RGB", (w, h), (240, 238, 230))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w // 2, h], fill=(232, 230, 222))
    d.text((80, 120), "リナシータ", font=_font(80, mincho=True), fill=(30, 40, 70))
    d.text((80, 240), "運用手順書", font=_font(64, mincho=True), fill=(30, 30, 30))
    d.text((80, 360), "第3版　取扱注意", font=_font(36, mincho=False), fill=(160, 40, 40))
    for k in range(3):
        d.ellipse([w // 2 - 40, 200 + k * 300, w // 2 - 10, 230 + k * 300], fill=(150, 150, 150))
    x0 = w // 2 + 50
    f = _font(30, mincho=True)
    lines = ["3-1. 補完には「提供体」の脳情報を用いる。", "　　 提供体は健常な成人であること。", "3-2. 提供体の記憶・人格情報は登録後、",
             "　　 システム内で保持され続ける。", "3-3. 復電時は 記憶野 → 言語野 → 自己認識野", "　　 の順に給電すること。",
             "4-2. スキャンは脳内の情報を計算機向けに", "　　 整理する。", "4-3. 取得率100%では整理し直す基準が", "　　 残らず、脳機能は失われる。"]
    for i, s in enumerate(lines):
        d.text((x0, 80 + i * 78), s, font=f, fill=(25, 25, 25))
    d.rectangle([x0 - 10, 80 + 4 * 78 - 6, x0 + 640, 80 + 6 * 78 - 16], outline=(200, 60, 50), width=3)    # 赤枠
    save("manual", img)


def wmemo():
    """水野の発見メモ（メモ用紙、急いだ筆跡）"""
    w, h = 780, 540
    img = Image.new("RGB", (w, h), (250, 248, 236))
    d = ImageDraw.Draw(img)
    for i in range(9):
        d.line([(20, 70 + i * 54), (w - 20, 70 + i * 54)], fill=(190, 210, 226), width=2)
    f = hand(34)
    rnd = random.Random(1161)
    lines = ["照合結果。脳情報の一部が、", "うちの研究員のものと一致する。", "そんなはずない。", "所長に報告 →『そのデータには触るな』", "どうして？　どうしてみんな、何も言わないの"]
    for i, s in enumerate(lines):
        x = 30
        for ch in s:
            d.text((x, 30 + i * 54 + rnd.uniform(-2, 2)), ch, font=f, fill=(30, 40, 120))
            x += 33
    d.line([(30, 30 + 2 * 54 + 44), (270, 30 + 2 * 54 + 40)], fill=(170, 40, 40), width=3)       # 下線
    save("wmemo", img)


if __name__ == "__main__":
    epoxy_floor(); hazard(); block_wall(); steel_plate()
    screen_list(); screen_mail(); screen_crack(); signs(); console_panel(); manual(); wmemo()
