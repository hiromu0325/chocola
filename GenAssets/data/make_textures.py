# -*- coding: utf-8 -*-
"""データ管理室（data_room）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/data/make_textures.py

出力: GenAssets/data/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/DataRoom/*.png
壁は脳神経解析室（analysis）の clean_panel、天井は研究所応接室（lab）の ceiling_tile を使う。
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

save = Saver(HERE, "DataRoom")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


def access_floor(n=1024, perforated=False, seed=1501):
    """フリーアクセス床（1枚 = 1.2m = 600角 x 4）。灰色のHPLに黒い縁、目地。perforated=通気口付きのパネル"""
    y, x = np.mgrid[0:n, 0:n] / n
    speck = fbm(n, 0.06, seed)
    tone = fbm(n, 2.0, seed + 1)
    base = np.array([168, 172, 172], dtype=float)
    lum = 0.95 + (speck - 0.5) * 0.1 + (tone - 0.5) * 0.06
    gx = np.minimum((x * 2) % 1, 1 - (x * 2) % 1); gy = np.minimum((y * 2) % 1, 1 - (y * 2) % 1)
    edge = np.minimum(gx, gy)
    trim = (edge < 0.012).astype(float)
    seam = (edge < 0.003).astype(float)
    lum = lum * (1 - trim * 0.55) - seam * 0.2
    h = speck * 0.2 - seam
    if perforated:
        lx, ly = (x * 2) % 1, (y * 2) % 1
        hole = np.zeros((n, n))
        inside = (lx > 0.08) & (lx < 0.92) & (ly > 0.08) & (ly < 0.92)
        px = (lx * 22) % 1; py = (ly * 22) % 1
        hole = (inside & (np.sqrt((px - 0.5) ** 2 + (py - 0.5) ** 2) < 0.28)).astype(float)
        lum = lum * (1 - hole * 0.8)
        h = h - hole
    save("access_floor_perf" if perforated else "access_floor", rgb(base[None, None, :] * lum[..., None]), normal_map(h, 1.8))


def server_fronts(w=1024, h=1024):
    """サーバーの前面アトラス（8種 x 高さ128px。1U/2Uの面：ドライブベイ・通気孔・ランプ・ロゴ）"""
    img = Image.new("RGB", (w, h), (20, 21, 24))
    d = ImageDraw.Draw(img)
    rnd = random.Random(1511)
    for k in range(8):
        y0 = k * 128
        base = [(28, 30, 34), (60, 62, 66), (24, 25, 28), (190, 192, 196), (36, 38, 44), (28, 30, 34), (70, 72, 76), (22, 23, 26)][k]
        d.rectangle([0, y0, w, y0 + 127], fill=base)
        d.rectangle([0, y0, 40, y0 + 127], fill=tuple(min(255, c + 25) for c in base))          # 耳（ラックへの取付金具）
        d.rectangle([w - 40, y0, w, y0 + 127], fill=tuple(min(255, c + 25) for c in base))
        d.ellipse([12, y0 + 52, 28, y0 + 68], fill=(120, 120, 124)); d.ellipse([w - 28, y0 + 52, w - 12, y0 + 68], fill=(120, 120, 124))
        kind = k % 4
        if kind == 0:      # ドライブベイ 8台
            for b in range(8):
                bx = 60 + b * 90
                d.rectangle([bx, y0 + 14, bx + 82, y0 + 114], fill=(12, 12, 14), outline=(90, 92, 96), width=2)
                for s in range(6):
                    d.line([(bx + 10, y0 + 30 + s * 12), (bx + 60, y0 + 30 + s * 12)], fill=(50, 52, 56), width=3)
                d.rectangle([bx + 66, y0 + 22, bx + 74, y0 + 30], fill=(30, 200, 90) if rnd.random() < 0.8 else (240, 150, 30))
        elif kind == 1:    # 通気孔（ハニカム）とロゴ
            for yy in range(y0 + 14, y0 + 114, 12):
                for xx in range(60 + ((yy // 12) % 2) * 6, 720, 12):
                    d.ellipse([xx, yy, xx + 8, yy + 8], fill=(18, 18, 20))
            d.text((760, y0 + 44), "NINOMIYA", font=_font(34, mincho=False), fill=(200, 202, 206))
        elif kind == 2:    # テープ装置
            d.rectangle([80, y0 + 40, 420, y0 + 80], fill=(8, 8, 10), outline=(80, 80, 84), width=2)
            d.rectangle([480, y0 + 30, 700, y0 + 100], fill=(40, 60, 50))
            d.text((500, y0 + 48), "READY", font=_font(30, mincho=False), fill=(120, 220, 160))
        else:              # 細い 1U ×2 （上下）
            for yy in (y0 + 8, y0 + 68):
                d.rectangle([50, yy, w - 50, yy + 52], fill=tuple(max(0, c - 10) for c in base), outline=(70, 72, 76), width=2)
                for b in range(10):
                    d.rectangle([80 + b * 60, yy + 14, 128 + b * 60, yy + 38], fill=(14, 14, 16))
        # 電源ランプ
        d.ellipse([w - 110, y0 + 20, w - 94, y0 + 36], fill=(60, 230, 110))
    save("server_fronts", img)


def cabinet_labels(w=1024, h=256):
    """ファイル引き出しのラベル（8コマ x 2段、各 128 x 128。年度と番号）"""
    img = Image.new("RGB", (w, h), (236, 234, 224))
    d = ImageDraw.Draw(img)
    f = _font(30, mincho=False)
    for k in range(16):
        x0, y0 = (k % 8) * 128, (k // 8) * 128
        d.rectangle([x0 + 4, y0 + 4, x0 + 123, y0 + 123], outline=(90, 90, 90), width=3)
        d.text((x0 + 16, y0 + 20), f"No.{k + 1:02d}", font=f, fill=(30, 30, 30))
        d.text((x0 + 16, y0 + 70), ["記録", "議事", "機器", "被験", "契約", "人事", "計測", "予備"][k % 8], font=f, fill=(30, 30, 30))
    save("cabinet_labels", img)


def crt_audit(w=640, h=480):
    """入退室ログ（緑の蛍光文字の CRT）"""
    img = Image.new("RGB", (w, h), (4, 12, 6))
    d = ImageDraw.Draw(img)
    for gy in range(0, h, 3):
        d.line([(0, gy), (w, gy)], fill=(2, 8, 4))
    g = (80, 240, 130)
    f = _font(22, mincho=False)
    d.text((20, 14), "入退室ログ照合  4/16 - 4/20", font=_font(26, mincho=False), fill=g)
    d.line([(20, 50), (w - 20, 50)], fill=g, width=1)
    # LoopAuditLock.DefaultRows() と同じ行（画面の見た目と照合の画面を揃える）
    rows = ["04/16 09:02  黒田  第10研究室 入室", "04/16 13:40  水野  臨床病棟   入室", "04/17 22:15  佐伯  第8研究室  入室",
            "04/18 08:55  水野  臨床病棟   入室", "04/18 23:41  主任  第12研究室 入室", "04/18 23:58  主任  第12研究室 退室",
            "04/19 00:12  主任  第12研究室 入室", "04/19 07:30  黒田  データ管理室 入室", "04/19 09:10  佐伯  第8研究室  入室",
            "04/20 18:05  主任  第1研究室  入室"]
    for i, s in enumerate(rows):
        d.text((20, 64 + i * 36), s, font=f, fill=g)
    d.text((20, 430), "> 照合する行を選択してください_", font=f, fill=g)
    a = np.asarray(img.filter(ImageFilter.GaussianBlur(0.6))).astype(float)
    save("crt_audit", rgb(a * 1.05))


def minutes():
    """緊急会議の議事録（A4）"""
    w, h = 740, 1040
    img = Image.new("RGB", (w, h), (242, 241, 236))
    d = ImageDraw.Draw(img)
    d.text((60, 60), "緊急会議 議事録", font=_font(52, mincho=True), fill=(20, 20, 20))
    d.text((60, 140), "起案: 黒田", font=_font(30, mincho=True), fill=(20, 20, 20))
    f = _font(26, mincho=True)
    lines = ["日時: 4月18日〜19日（2日間）", "出席: 黒田・佐伯・水野", "欠席: 所長（学会出張のため札幌に滞在。", "　　　19日夜まで戻らず、電話で参加）", "",
             "「システムは登録された脳情報を", "　単なるデータではなく、人格モデルとして", "　扱い始めている。これは治療ではない。", "　直ちに運用を停止すべきだ」", "",
             "・所長（電話）: 保留", "・佐伯: 検証に時間を要すると発言"]
    for i, s in enumerate(lines):
        d.text((60, 220 + i * 52), s, font=f, fill=(25, 25, 25))
    d.rectangle([520, 60, 680, 120], outline=(170, 40, 40), width=4)
    d.text((540, 70), "要回覧", font=_font(34, mincho=True), fill=(170, 40, 40))
    save("minutes", img)


def kmemo():
    """黒田のメモ（後半が破り取られている：下の縁はモデル側で破る）"""
    w, h = 600, 420
    img = Image.new("RGB", (w, h), (246, 244, 234))
    d = ImageDraw.Draw(img)
    for i in range(7):
        d.line([(20, 60 + i * 52), (w - 20, 60 + i * 52)], fill=(190, 206, 222), width=2)
    f = hand(32)
    for i, s in enumerate(["主任は3人を犠牲にする", "覚悟を決めた。", "止められるのは私しかいない──"]):
        d.text((30, 20 + i * 52), s, font=f, fill=(20, 20, 30))
    save("kmemo", img)


def scribble():
    """キャビネットの付箋（黄色、細い走り書き）"""
    w, h = 400, 400
    img = Image.new("RGB", (w, h), (248, 228, 110))
    d = ImageDraw.Draw(img)
    f = hand(30)
    for i, s in enumerate(["主任を信用", "しすぎないほうが", "いい。", "あの人は", "優しすぎる。"]):
        d.text((24, 24 + i * 66), s, font=f, fill=(30, 30, 50))
    a = np.asarray(img).astype(float) * (0.95 + fbm(w, 1.5, 1521)[..., None] * 0.1)
    save("scribble", rgb(a))


def signs():
    img = Image.new("RGB", (1024, 256), (236, 238, 240))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 1024, 256], outline=(40, 70, 130), width=12)
    d.text((40, 30), "データ管理室", font=_font(88, mincho=False), fill=(40, 70, 130))
    d.text((44, 160), "関係者以外立入禁止　飲食厳禁", font=_font(48, mincho=False), fill=(30, 30, 30))
    save("sign_room", img)
    img = Image.new("RGB", (1024, 128), (240, 240, 236))
    d = ImageDraw.Draw(img)
    for k in range(8):
        x0 = k * 128
        d.rectangle([x0 + 4, 4, x0 + 124, 124], outline=(30, 30, 30), width=3)
        d.text((x0 + 18, 36), f"{'AB'[k // 4]}-{k % 4 + 1:02d}", font=_font(40, mincho=False), fill=(20, 20, 20))
    save("rack_labels", img)


if __name__ == "__main__":
    access_floor(); access_floor(perforated=True, seed=1502)
    server_fronts(); cabinet_labels(); crt_audit(); minutes(); kmemo(); scribble(); signs()
