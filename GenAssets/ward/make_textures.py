# -*- coding: utf-8 -*-
"""臨床病棟（ward）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/ward/make_textures.py

出力: GenAssets/ward/tex/*.png（Blenderの確認用）と
      project/Assets/EscapePrototype/Textures/HQ/Ward/*.png（Unity用。_n が法線）
天井の吸音板は研究所応接室（lab）の ceiling_tile を使う。
"""
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, normal_map, rgb, colorize, _font, Saver, fabric  # noqa: E402

save = Saver(HERE, "Ward")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


# ---------------------------------------------------------------- 素材

def vinyl_floor(n=1024):
    """温かい灰色の長尺ビニル床（1枚 = 2m、継ぎ目は溶接棒の線）。細かいチップ"""
    y, x = np.mgrid[0:n, 0:n] / n
    speck = fbm(n, 0.05, 1001)
    tone = fbm(n, 2.3, 1002)
    rnd = np.random.default_rng(1003)
    chips = (rnd.random((n, n)) > 0.982).astype(float)
    chips = np.asarray(Image.fromarray((chips * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.7))).astype(float) / 255
    dark = (rnd.random((n, n)) > 0.992).astype(float)
    dark = np.asarray(Image.fromarray((dark * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))).astype(float) / 255
    base = np.array([176, 172, 162], dtype=float)
    lum = 0.95 + (speck - 0.5) * 0.07 + (tone - 0.5) * 0.08 + chips * 0.18 - dark * 0.25
    seam = (np.abs(x - 0.5) < 0.0015).astype(float)
    lum -= seam * 0.2
    # 通路の擦れ（ワックスのむら）
    wax = fbm(n, 2.8, 1004, ax=1.0, ay=3.0)
    lum *= 0.97 + (wax - 0.5) * 0.06
    save("vinyl_floor", rgb(base[None, None, :] * lum[..., None]), normal_map(speck * 0.3 - seam - dark * 0.2, 1.0))


def paint(n=1024):
    """クリーム色の塗り壁（1枚 = 1m）。ローラーの細かいむら"""
    roll = fbm(n, 0.7, 1011, ax=1.0, ay=4.0)
    mott = fbm(n, 2.3, 1012)
    base = np.array([236, 232, 220], dtype=float)
    lum = 1.0 + (roll - 0.5) * 0.03 + (mott - 0.5) * 0.04
    save("paint", rgb(base[None, None, :] * lum[..., None]), normal_map(roll, 0.8))


def wainscot(n=1024):
    """腰の高さまでの淡いミント色の塩ビ壁紙（1枚 = 1m）。細かいエンボス"""
    emb = fbm(n, 0.25, 1021)
    mott = fbm(n, 2.0, 1022)
    base = np.array([190, 212, 200], dtype=float)
    lum = 1.0 + (emb - 0.5) * 0.06 + (mott - 0.5) * 0.04
    save("wainscot", rgb(base[None, None, :] * lum[..., None]), normal_map(emb, 1.6))


def curtain(n=1024):
    """間仕切りカーテン（1枚 = 0.5m）：淡い緑の織物に小さな葉の地模様"""
    img, nrm = fabric(n, 1031, (176, 206, 186), weave=260)
    im = Image.fromarray(np.asarray(img)).convert("RGB")
    d = ImageDraw.Draw(im)
    rnd = random.Random(1032)
    for i in range(8):
        for j in range(8):
            cx, cy = (i + 0.5 + (j % 2) * 0.5) * n / 8, (j + 0.5) * n / 8
            a = rnd.uniform(0, math.pi)
            pts = []
            for k in range(12):
                t = k / 11
                r = 18 * math.sin(math.pi * t)
                pts.append((cx + math.cos(a) * (t - 0.5) * 60 - math.sin(a) * r * 0.5, cy + math.sin(a) * (t - 0.5) * 60 + math.cos(a) * r * 0.5))
            for k in range(11, -1, -1):
                t = k / 11
                r = 18 * math.sin(math.pi * t)
                pts.append((cx + math.cos(a) * (t - 0.5) * 60 + math.sin(a) * r * 0.5, cy + math.sin(a) * (t - 0.5) * 60 - math.cos(a) * r * 0.5))
            d.polygon(pts, fill=(158, 192, 170))
    save("curtain", im, nrm)


def curtain_net(n=256):
    """カーテン上部のメッシュ（抜き：アルファ）"""
    y, x = np.mgrid[0:n, 0:n] / n
    gx = np.abs(((x * 16) % 1) - 0.5); gy = np.abs(((y * 16) % 1) - 0.5)
    hole = (gx < 0.36) & (gy < 0.36)
    a = np.where(hole, 0, 255).astype(np.uint8)
    col = np.zeros((n, n, 4), np.uint8)
    col[..., 0], col[..., 1], col[..., 2] = 214, 226, 216
    col[..., 3] = a
    save("curtain_net", Image.fromarray(col, "RGBA"))


def sky_day(w=2048, h=640):
    """窓の外（幅16m x 高さ5m の背景板）：薄曇りの昼の空・中庭の木々・向かいの病棟"""
    y = np.mgrid[0:h, 0:w][0] / h; x = np.mgrid[0:h, 0:w][1] / w
    top = np.array([150, 180, 214]); low = np.array([226, 234, 238])
    t = y[..., None]
    col = top + (low - top) * np.clip(t / 0.75, 0, 1)
    clouds = np.clip(fbm(w, 1.9, 1041, ax=4.0, ay=1.0, m=h) - 0.45, 0, 1)[..., None] * np.clip(1.1 - t * 1.3, 0, 1) * 120
    col = col + clouds
    col = col + np.random.default_rng(1042).uniform(-1.5, 1.5, col.shape)
    img = rgb(col)
    d = ImageDraw.Draw(img)
    # 向かいの病棟（右側）：白い壁と窓の列
    bx0 = int(w * 0.62)
    d.rectangle([bx0, int(h * 0.3), w, h], fill=(214, 214, 208))
    d.rectangle([bx0, int(h * 0.3) - 12, w, int(h * 0.3)], fill=(170, 172, 170))
    for r in range(5):
        for c in range((w - bx0) // 90):
            wx, wy = bx0 + 24 + c * 90, int(h * 0.36) + r * 80
            d.rectangle([wx, wy, wx + 56, wy + 44], fill=(120, 140, 156))
            d.rectangle([wx, wy, wx + 56, wy + 6], fill=(190, 196, 200))
    # 中庭の木（丸い樹冠を重ねる）
    rnd = random.Random(1043)
    for k in range(26):
        cx = rnd.uniform(-40, w * 0.9); cy = h - rnd.uniform(60, 250); r = rnd.uniform(60, 130)
        g = rnd.uniform(0.8, 1.15)
        d.rectangle([cx - 6, cy, cx + 6, h], fill=(70, 60, 50))
        for m in range(7):
            ox, oy, rr = rnd.uniform(-r * 0.6, r * 0.6), rnd.uniform(-r * 0.5, r * 0.3), r * rnd.uniform(0.5, 0.8)
            c0 = (int(70 * g), int(112 * g), int(68 * g))
            d.ellipse([cx + ox - rr, cy + oy - rr, cx + ox + rr, cy + oy + rr], fill=c0)
    a = np.asarray(img).astype(float)
    leaf = fbm(w, 0.6, 1044, m=h)[..., None]
    green = (a[..., 1:2] > a[..., 0:1] + 25) & (a[..., 1:2] > a[..., 2:3] + 20)
    a = np.where(green, a * (0.8 + leaf * 0.4), a)
    save("sky_day", rgb(a))


def screen_vitals(w=320, h=240):
    """ベッドサイドモニター（患者なし：平らな緑の線と --- ）"""
    img = Image.new("RGB", (w, h), (6, 14, 10))
    d = ImageDraw.Draw(img)
    for gy in range(0, h, 20):
        d.line([(0, gy), (w, gy)], fill=(12, 26, 18))
    d.line([(10, 70), (220, 70)], fill=(60, 230, 110), width=2)
    d.line([(10, 130), (220, 130)], fill=(80, 190, 230), width=2)
    f = _font(22, mincho=False)
    d.text((232, 40), "HR", font=f, fill=(60, 230, 110)); d.text((232, 64), "---", font=_font(34, mincho=False), fill=(60, 230, 110))
    d.text((232, 110), "SpO2", font=f, fill=(80, 190, 230)); d.text((232, 134), "--", font=_font(34, mincho=False), fill=(80, 190, 230))
    d.text((12, 190), "待機中　プローブ未装着", font=_font(18, mincho=False), fill=(200, 200, 90))
    save("screen_vitals", img)


def screen_chart(w=640, h=400):
    """ナースステーションの電子カルテ（患者一覧）"""
    img = Image.new("RGB", (w, h), (236, 240, 246))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 40], fill=(40, 90, 150))
    d.text((12, 6), "電子カルテ　3F 病棟　患者一覧", font=_font(24, mincho=False), fill=(255, 255, 255))
    f = _font(22, mincho=False)
    rows = [("301", "佐々木 様", "安定"), ("302", "田中 様", "面会 14:00"), ("303", "", ""), ("304", "空床", ""), ("305", "空床", ""), ("306", "空床", "")]
    for i, (a, b, c) in enumerate(rows):
        y = 60 + i * 52
        d.rectangle([10, y, w - 10, y + 44], fill=(255, 255, 255) if i % 2 == 0 else (244, 246, 250), outline=(200, 206, 214))
        d.text((24, y + 9), a, font=f, fill=(30, 30, 30))
        if a == "303":
            d.rectangle([100, y + 12, 330, y + 32], fill=(150, 150, 160))          # 読めない
        else:
            d.text((100, y + 9), b, font=f, fill=(30, 30, 30))
        d.text((380, y + 9), c, font=f, fill=(40, 90, 150))
    save("screen_chart", img)


def bed_cards():
    """ベッドの名札（4コマ：301 佐々木 / 302 田中 / 303 滲み / 空床）。各 512 x 256"""
    img = Image.new("RGB", (1024, 512), (250, 250, 246))
    d = ImageDraw.Draw(img)
    items = [("301", "佐々木 様"), ("302", "田中 様"), ("303", None), ("---", "空床")]
    for k, (no, name) in enumerate(items):
        x0, y0 = (k % 2) * 512, (k // 2) * 256
        d.rectangle([x0 + 6, y0 + 6, x0 + 506, y0 + 250], outline=(40, 110, 90), width=8)
        d.rectangle([x0 + 6, y0 + 6, x0 + 506, y0 + 70], fill=(40, 110, 90))
        d.text((x0 + 26, y0 + 12), f"{no}　主治医 小川", font=_font(40, mincho=False), fill=(255, 255, 255))
        if name:
            d.text((x0 + 40, y0 + 110), name, font=hand(78), fill=(30, 30, 30))
        else:
            blot = Image.new("L", (440, 140), 0)
            bd = ImageDraw.Draw(blot)
            rnd = random.Random(1051)
            for i in range(40):
                cx, cy = rnd.uniform(40, 400), rnd.uniform(30, 110)
                bd.ellipse([cx - 30, cy - 20, cx + 30, cy + 20], fill=rnd.randint(80, 200))
            blot = blot.filter(ImageFilter.GaussianBlur(10))
            img.paste((70, 70, 90), (x0 + 36, y0 + 90), blot)
    save("bed_cards", img)


def whiteboard():
    """ナースステーションのホワイトボード（受け持ちと申し送り）"""
    w, h = 1400, 800
    img = Image.new("RGB", (w, h), (244, 246, 246))
    d = ImageDraw.Draw(img)
    d.text((40, 24), "3F 病棟　本日の受け持ち", font=_font(52, mincho=False), fill=(30, 60, 140))
    d.line([(40, 96), (w - 40, 96)], fill=(30, 60, 140), width=4)
    f = hand(40)
    rows = [("301", "佐々木", "水野"), ("302", "田中", "水野"), ("303", "", "水野"), ("304", "", ""), ("305", "", ""), ("306", "", "")]
    for i, (no, name, ns) in enumerate(rows):
        y = 120 + i * 70
        d.rectangle([40, y, 700, y + 62], outline=(80, 80, 80), width=2)
        d.line([(160, y), (160, y + 62)], fill=(80, 80, 80), width=2)
        d.line([(480, y), (480, y + 62)], fill=(80, 80, 80), width=2)
        d.text((60, y + 10), no, font=_font(38, mincho=False), fill=(20, 20, 20))
        if no == "303":
            d.line([(180, y + 32), (460, y + 30)], fill=(150, 150, 160), width=18)
        else:
            d.text((180, y + 8), name, font=f, fill=(20, 20, 20))
        d.text((500, y + 8), ns, font=f, fill=(170, 40, 40))
    d.text((760, 130), "申し送り", font=_font(40, mincho=False), fill=(30, 60, 140))
    notes = ["・302 田中さん　娘さん面会 14:00〜", "　手を握ると脈が上がる。声かけ続けて", "・303　ご家族（父）毎日面会",
             "　──面会簿の名前が読めない", "・消灯 21:00　巡視 2時間ごと"]
    for i, s in enumerate(notes):
        d.text((760, 190 + i * 62), s, font=hand(34), fill=(20, 20, 20) if i != 3 else (60, 60, 150))
    d.text((60, 600), "笑顔で名前を呼ぶこと！", font=hand(48), fill=(40, 120, 80))
    # マグネット
    for (mx, my, c) in ((680, 150, (200, 50, 50)), (680, 220, (200, 50, 50)), (680, 290, (50, 90, 200))):
        d.ellipse([mx - 14, my - 14, mx + 14, my + 14], fill=c)
    save("whiteboard", img)


def obs_sheet():
    """水野の患者観察記録（クリップボードの紙。名前で呼び、似顔絵まで添える）"""
    w, h = 740, 1040
    img = Image.new("RGB", (w, h), (246, 245, 238))
    d = ImageDraw.Draw(img)
    d.text((40, 40), "患者観察記録", font=_font(52, mincho=True), fill=(20, 20, 20))
    d.text((440, 60), "記録者: 水野", font=hand(36), fill=(20, 20, 20))
    d.line([(40, 120), (700, 120)], fill=(60, 60, 60), width=3)
    f = hand(34)
    entries = [("301号 佐々木さん", ["今日は目元が和らいでいた"]), ("302号 田中さん", ["娘さんの面会。", "手を握ると脈が上がる"]), ("303号", ["──"])]
    for i, (a, b) in enumerate(entries):
        y = 160 + i * 250
        d.text((50, y), "・" + a, font=f, fill=(20, 30, 90))
        for k, line in enumerate(b):
            d.text((80, y + 56 + k * 48), line, font=f, fill=(20, 30, 90))
        # 欄外の似顔絵（303 は描きかけ）
        cx, cy = 620, y + 90
        d.ellipse([cx - 50, cy - 60, cx + 50, cy + 60], outline=(40, 40, 90), width=3)
        if i < 2:
            d.arc([cx - 30, cy - 20, cx - 8, cy], 200, 340, fill=(40, 40, 90), width=3)
            d.arc([cx + 8, cy - 20, cx + 30, cy], 200, 340, fill=(40, 40, 90), width=3)
            d.arc([cx - 24, cy + 10, cx + 24, cy + 40], 20, 160, fill=(40, 40, 90), width=3)
        d.line([(40, y + 210), (700, y + 210)], fill=(200, 200, 200), width=2)
    save("obs_sheet", img)


def girl_file():
    """患者番号の無いファイル（左頁：滲んだ写真と読めない欄、右頁：特記事項）"""
    w, h = 1480, 1040
    img = Image.new("RGB", (w, h), (244, 243, 236))
    d = ImageDraw.Draw(img)
    d.line([(w // 2, 0), (w // 2, h)], fill=(200, 196, 186), width=4)
    d.text((40, 40), "診療記録", font=_font(56, mincho=True), fill=(20, 20, 20))
    d.text((40, 130), "患者番号: ──", font=_font(38, mincho=False), fill=(20, 20, 20))
    # 写真（水に濡れたように滲んで顔が見えない）
    ph = Image.new("RGB", (300, 380), (210, 200, 190))
    pd = ImageDraw.Draw(ph)
    pd.ellipse([90, 60, 210, 200], fill=(236, 222, 208))
    pd.polygon([(40, 380), (80, 220), (220, 220), (260, 380)], fill=(200, 110, 120))
    pd.ellipse([70, 40, 230, 150], fill=(60, 40, 30))
    ph = ph.filter(ImageFilter.GaussianBlur(14))
    img.paste(ph, (60, 220))
    for k in range(6):
        d.ellipse([60 + k * 40, 480 + (k % 3) * 30, 140 + k * 40, 600 + (k % 3) * 30], outline=(170, 160, 150), width=2)
    for i, lab in enumerate(["氏名", "年齢", "住所"]):
        y = 660 + i * 90
        d.text((60, y), lab, font=_font(34, mincho=False), fill=(20, 20, 20))
        blot = Image.new("L", (380, 60), 0)
        ImageDraw.Draw(blot).rectangle([10, 10, 370, 50], fill=180)
        img.paste((120, 120, 140), (180, y - 6), blot.filter(ImageFilter.GaussianBlur(8)))
    x0 = w // 2 + 50
    d.text((x0, 60), "特記事項", font=_font(46, mincho=True), fill=(20, 20, 20))
    d.text((x0, 160), "ご家族: 父。", font=hand(52), fill=(20, 30, 90))
    d.text((x0, 240), "毎日面会に来られる", font=hand(52), fill=(20, 30, 90))
    for i in range(10):
        y = 360 + i * 60
        d.line([(x0, y), (w - 50, y)], fill=(210, 210, 214), width=2)
    save("girl_file", img)


def signs():
    img = Image.new("RGB", (1024, 200), (244, 246, 244))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 1024, 200], fill=(40, 110, 90))
    d.text((60, 40), "ナースステーション", font=_font(96, mincho=False), fill=(255, 255, 255))
    save("sign_nurse", img)
    img = Image.new("RGB", (600, 800), (250, 250, 246))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 600, 160], fill=(60, 140, 190))
    d.text((40, 40), "面会のご案内", font=_font(64, mincho=False), fill=(255, 255, 255))
    f = _font(36, mincho=False)
    for i, s in enumerate(["面会時間　14:00〜20:00", "入室前に手指消毒を", "お願いいたします", "", "大きな声での会話は", "お控えください"]):
        d.text((50, 220 + i * 70), s, font=f, fill=(30, 30, 30))
    d.ellipse([400, 600, 540, 740], fill=(60, 140, 190))
    d.polygon([(470, 622), (500, 672), (504, 690), (492, 712), (470, 720), (448, 712), (436, 690), (440, 672)], fill=(255, 255, 255))   # しずく
    save("poster_visit", img)
    img = Image.new("RGB", (256, 256), (240, 240, 236))
    d = ImageDraw.Draw(img)
    d.text((30, 90), "手指消毒", font=_font(48, mincho=False), fill=(30, 100, 160))
    save("label_sanitizer", img)


if __name__ == "__main__":
    vinyl_floor(); paint(); wainscot(); curtain(); curtain_net(); sky_day()
    img, nrm = fabric(1024, 1061, (150, 186, 206), weave=180, slub=0.3); save("blanket", img, nrm)
    img, nrm = fabric(1024, 1062, (236, 238, 238), weave=320); save("sheet", img, nrm)
    screen_vitals(); screen_chart(); bed_cards(); whiteboard(); obs_sheet(); girl_file(); signs()
