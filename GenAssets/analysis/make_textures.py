# -*- coding: utf-8 -*-
"""脳神経解析室（analysis）のテクスチャを手続き的に作る（継ぎ目なし＋法線マップ）。

    python GenAssets/analysis/make_textures.py

出力: GenAssets/analysis/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/Analysis/*.png
MRI断面・職員証の顔写真は研究所応接室（lab）の生成関数を使う。
"""
import importlib.util
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, normal_map, rgb, colorize, _font, Saver  # noqa: E402

_spec = importlib.util.spec_from_file_location("lab_tex", os.path.join(HERE, "..", "lab", "make_textures.py"))
lab = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(lab)

save = Saver(HERE, "Analysis")


def vinyl_floor(n=1024):
    """青みの灰色の長尺ビニル床（1枚 = 2m、継ぎ目は溶接棒の線）。細かい粒"""
    y, x = np.mgrid[0:n, 0:n] / n
    speck = fbm(n, 0.05, 601)
    tone = fbm(n, 2.3, 602)
    rnd = np.random.default_rng(603)
    chips = (rnd.random((n, n)) > 0.985).astype(float)
    chips = np.asarray(Image.fromarray((chips * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))).astype(float) / 255
    base = np.array([150, 160, 168], dtype=float)
    lum = 0.95 + (speck - 0.5) * 0.08 + (tone - 0.5) * 0.08 + chips * 0.25
    seam = (np.abs(x - 0.5) < 0.0015).astype(float)
    lum -= seam * 0.25
    save("vinyl_floor", rgb(base[None, None, :] * lum[..., None]), normal_map(speck * 0.3 - seam, 1.0))


def clean_panel(n=512):
    """白い壁パネル（1枚 = 1m）。わずかなむら"""
    t = fbm(n, 2.0, 611)
    g = fbm(n, 0.4, 612)
    base = np.array([226, 230, 232], dtype=float)
    lum = 1.0 + (t - 0.5) * 0.03 + (g - 0.5) * 0.015
    save("clean_panel", rgb(base[None, None, :] * lum[..., None]), normal_map(g, 0.4))


def concrete_dark(n=1024):
    """濃い灰色に塗った天井のコンクリート（1枚 = 1m）。小さな気泡と型枠のむら"""
    y, x = np.mgrid[0:n, 0:n] / n
    t = fbm(n, 2.2, 621)
    g = fbm(n, 0.5, 622)
    rnd = np.random.default_rng(623)
    pits = np.zeros((n, n))
    img = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(img)
    for _ in range(600):
        cx, cy = rnd.integers(0, n, 2); r = rnd.uniform(1, 3.5)
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
    pits = np.asarray(img).astype(float) / 255
    base = np.array([70, 72, 76], dtype=float)
    lum = 1.0 + (t - 0.5) * 0.18 + (g - 0.5) * 0.06 - pits * 0.3
    save("concrete_dark", rgb(base[None, None, :] * lum[..., None]), normal_map(g * 0.5 - pits, 1.5))


def galvanized(n=512, seeds=900):
    """亜鉛めっき鋼板（スパングルの結晶模様、1枚 = 0.5m、結晶は 1〜2cm）"""
    rnd = np.random.default_rng(631)
    y, x = np.mgrid[0:n, 0:n].astype(float)
    pts = rnd.random((seeds, 2)) * n
    best = np.full((n, n), 1e9); idx = np.zeros((n, n), int)
    for k, (px, py) in enumerate(pts):
        dx = np.minimum(np.abs(x - px), n - np.abs(x - px))
        dy = np.minimum(np.abs(y - py), n - np.abs(y - py))
        dd = dx * dx + dy * dy
        m = dd < best
        best[m] = dd[m]; idx[m] = k
    shade = rnd.uniform(0.9, 1.04, seeds)[idx]
    fine = fbm(n, 0.4, 632)
    base = np.array([176, 180, 184], dtype=float)
    lum = shade * (0.97 + (fine - 0.5) * 0.06)
    save("galvanized", rgb(base[None, None, :] * lum[..., None]))


# ---------------------------------------------------------------- 画面

def screen_connectome(w=640, h=400, seed=641):
    rnd = random.Random(seed)
    img = Image.new("RGB", (w, h), (4, 8, 16))
    d = ImageDraw.Draw(img)
    cx, cy = w * 0.5, h * 0.55
    nodes = []
    for _ in range(90):
        a = rnd.uniform(0, math.pi * 2); r = rnd.uniform(0, 1) ** 0.6
        nodes.append((cx + math.cos(a) * r * 230, cy + math.sin(a) * r * 150))
    for i, a in enumerate(nodes):
        for b in rnd.sample(nodes, 3):
            if math.dist(a, b) < 140:
                c = (40, 120, 220) if rnd.random() > 0.15 else (230, 120, 60)
                d.line([a, b], fill=c, width=1)
    for (x, y) in nodes:
        r = rnd.uniform(2, 5)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(140, 210, 255))
    glow = img.filter(ImageFilter.GaussianBlur(4))
    a = np.asarray(img).astype(float) + np.asarray(glow).astype(float) * 1.2
    img = rgb(a)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 20], fill=(16, 24, 40))
    d.text((8, 3), "CONNECTOME  SUBJ-03   SYNC 87.2%", font=_font(14, mincho=False), fill=(170, 200, 230))
    save("screen_connectome", img)


def screen_ct_montage(w=640, h=400):
    img = Image.new("RGB", (w, h), (0, 0, 0))
    for j in range(3):
        for i in range(5):
            m = lab.mri_slice(118, 700 + i * 5 + j * 11, level=(i - 2) / 2)
            im = Image.fromarray((m * 225).astype(np.uint8)).convert("RGB")
            img.paste(im, (10 + i * 126, 26 + j * 124))
    d = ImageDraw.Draw(img)
    d.text((8, 4), "AXIAL 1-15   T2 FLAIR", font=_font(14, mincho=False), fill=(200, 200, 200))
    save("screen_ct", img)


def screen_spectrogram(w=640, h=400, seed=651):
    y, x = np.mgrid[0:h, 0:w] / np.array([h, w])[:, None, None][0]
    y, x = np.mgrid[0:h, 0:w]
    fy, fx = y / h, x / w
    band = np.exp(-((fy - 0.72 - 0.05 * np.sin(fx * 9)) ** 2) / 0.004) * 0.8
    band += np.exp(-((fy - 0.45 - 0.08 * np.sin(fx * 5 + 1)) ** 2) / 0.002) * 0.6
    noise = fbm(w, 1.0, seed, m=h) * 0.5
    burst = np.exp(-((fx - 0.64) ** 2) / 0.0008) * (fy > 0.2)
    v = np.clip(band + noise * 0.6 + burst * 0.9, 0, 1)
    r = np.clip(v * 3 - 1.5, 0, 1); g = np.clip(v * 3 - 0.8, 0, 1) * (1 - np.clip(v * 3 - 2.4, 0, 1) * 0.3); b = np.clip(1 - v * 2, 0, 1) * 0.6 + v * 0.3
    a = np.stack([r, g, b], axis=-1) * 255
    img = rgb(a)
    d = ImageDraw.Draw(img)
    d.text((8, 4), "SPECTRUM  0-120Hz", font=_font(14, mincho=False), fill=(230, 230, 230))
    save("screen_spectrum", img)


def screen_anomaly(w=640, h=426):
    """異常レポート（佐伯→所長）。調べる資料の画面"""
    img = Image.new("RGB", (w, h), (18, 8, 10))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 44], fill=(150, 24, 30))
    d.text((16, 8), "⚠ ANOMALY REPORT　異常報告", font=_font(26, mincho=False), fill=(255, 235, 235))
    f = _font(22, mincho=False)
    lines = ["報告者：佐伯　　宛先：所長", "", "・存在しないはずの記憶が生成されている", "・システムが研究員自身の脳情報で",
             "　学習している可能性がある", "・至急、運用の停止を進言する", "", "返信：「今は結論を出すな」"]
    for i, s in enumerate(lines):
        d.text((24, 64 + i * 40), s, font=f, fill=(240, 200, 200) if i != 7 else (255, 120, 120))
    a = np.asarray(img).astype(float)
    yy = np.arange(h)[:, None]
    a *= (0.88 + 0.12 * (np.sin(yy * np.pi / 2) ** 2))[..., None]
    save("screen_anomaly", rgb(a))


# ---------------------------------------------------------------- 紙・札

def id_card(w=856, h=540):
    img = Image.new("RGB", (w, h), (240, 242, 246))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, 110], fill=(30, 70, 140))
    d.text((30, 26), "小川脳神経総合研究所", font=_font(46, mincho=False), fill=(255, 255, 255))
    d.text((620, 40), "職員証", font=_font(40, mincho=False), fill=(220, 230, 250))
    ph = lab._id_photo(220, 280, True, 271)
    img.paste(ph, (40, 150))
    d.text((300, 160), "佐伯 恒一", font=_font(64, mincho=False), fill=(20, 20, 20))
    d.text((300, 260), "主任研究員補佐", font=_font(34, mincho=False), fill=(60, 60, 60))
    d.text((300, 312), "補完アルゴリズム担当", font=_font(30, mincho=False), fill=(60, 60, 60))
    d.text((300, 400), "ID 08-0417", font=_font(34, mincho=False), fill=(30, 70, 140))
    for i in range(34):
        d.rectangle([300 + i * 14, 460, 300 + i * 14 + (4 if i % 3 else 8), 510], fill=(20, 20, 20))
    save("id_card", img)


def documents():
    w, h = 740, 1040
    img = Image.new("RGB", (w, h), (240, 238, 232))
    d = ImageDraw.Draw(img)
    d.text((60, 120), "RENASCITA", font=_font(96, mincho=False), fill=(30, 50, 90))
    d.text((64, 250), "研究計画書", font=_font(64, mincho=True), fill=(30, 30, 30))
    d.line([(60, 340), (680, 340)], fill=(30, 50, 90), width=4)
    f = _font(28, mincho=True)
    for i, s in enumerate(["対象：遷延性意識障害", "方針：記憶・人格野の外部補完", "脳情報の取得：ヘルメット型スキャン装置", "",
                           "極秘　小川脳神経総合研究所"]):
        d.text((64, 400 + i * 56), s, font=f, fill=(40, 40, 40))
    d.ellipse([520, 780, 660, 920], outline=(170, 40, 40), width=6)
    d.text((548, 822), "極秘", font=_font(40, mincho=True), fill=(170, 40, 40))
    save("doc_plan", img)
    img = Image.new("RGB", (w, h), (244, 244, 240))
    d = ImageDraw.Draw(img)
    d.text((60, 60), "補完アルゴリズム 概略　担当：佐伯", font=_font(34, mincho=False), fill=(20, 20, 20))
    marks = "○△□"
    rnd = random.Random(661)
    for j in range(5):
        for i in range(5):
            x, y = 110 + i * 110, 160 + j * 100
            d.rectangle([x, y, x + 80, y + 80], outline=(60, 60, 60), width=3)
            c = "?" if (i, j) in ((2, 1), (3, 3)) else rnd.choice(marks)
            d.text((x + 20, y + 12), c, font=_font(48, mincho=False), fill=(170, 40, 40) if c == "?" else (30, 30, 30))
    f = _font(28, mincho=False)
    for i, s in enumerate(["規則：欠損区画には、経路上の両隣と同じ傾向を置く", "異なる傾向を置くと光が濁り、補完は失敗する"]):
        d.text((60, 700 + i * 50), s, font=f, fill=(30, 30, 30))
    d.text((80, 860), "補完された記憶は、本当に本人のものか？", font=lab.hand(34), fill=(30, 60, 150))
    save("doc_spec", img)


def binders():
    """バインダーの背（8色 x 2 = 16コマ、各 128 x 512）"""
    cols = [(40, 70, 130), (140, 40, 40), (40, 110, 70), (200, 170, 50), (90, 90, 96), (230, 230, 226), (60, 40, 90), (180, 90, 40)]
    img = Image.new("RGB", (128 * 8, 512 * 2), (0, 0, 0))
    d = ImageDraw.Draw(img)
    for k in range(16):
        x0, y0 = (k % 8) * 128, (k // 8) * 512
        c = cols[k % 8]
        d.rectangle([x0, y0, x0 + 128, y0 + 512], fill=c)
        d.rectangle([x0 + 18, y0 + 60, x0 + 110, y0 + 300], fill=(236, 234, 226))
        f = _font(30, mincho=False)
        label = ["被験体記録", "計測データ", "倫理審査", "機器台帳", "論文草稿", "議事録"][k % 6]
        for i, ch in enumerate(label):
            d.text((x0 + 48, y0 + 70 + i * 36), ch, font=f, fill=(30, 30, 30))
        d.ellipse([x0 + 44, y0 + 380, x0 + 84, y0 + 420], fill=(20, 20, 20))
        d.ellipse([x0 + 52, y0 + 388, x0 + 76, y0 + 412], fill=c)
    save("binders", img)


def signs():
    img = Image.new("RGB", (800, 200), (236, 238, 240))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 800, 200], outline=(40, 70, 130), width=10)
    d.text((40, 30), "第8研究室", font=_font(56, mincho=False), fill=(40, 70, 130))
    d.text((40, 110), "脳神経解析室　関係者以外立入禁止", font=_font(40, mincho=False), fill=(30, 30, 30))
    save("sign_room", img)
    img = Image.new("RGB", (512, 256), (250, 206, 30))
    d = ImageDraw.Draw(img)
    d.polygon([(90, 30), (160, 150), (20, 150)], fill=(20, 20, 20))
    d.polygon([(90, 58), (138, 140), (42, 140)], fill=(250, 206, 30))
    d.text((80, 80), "!", font=_font(56, mincho=False), fill=(20, 20, 20))
    d.text((190, 40), "高電圧注意", font=_font(56, mincho=False), fill=(20, 20, 20))
    d.text((190, 124), "装置の電源を切らないこと", font=_font(25, mincho=False), fill=(20, 20, 20))
    save("sign_caution", img)


if __name__ == "__main__":
    vinyl_floor(); clean_panel(); concrete_dark(); galvanized()
    screen_connectome(); screen_ct_montage(); screen_spectrogram(); screen_anomaly()
    lab.screens = lab.screens  # MRI と脳波は lab の画面を流用（Unity側で Lab/ を参照）
    id_card(); documents(); binders(); signs()
