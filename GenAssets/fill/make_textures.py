# -*- coding: utf-8 -*-
"""黒塗りの資料（穴埋め）と、埋めると開く入れ物の中の資料・飾りのテクスチャを作る。

    python GenAssets/fill/make_textures.py

出力: GenAssets/fill/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/Fill/*.png
文面はゲームの FillDocs.cs（Loop/FillDocs.cs）と同じ。［答え］の所を黒く塗る。文面を変えたらここも合わせる。
"""
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, rgb, _font, Saver  # noqa: E402

save = Saver(HERE, "Fill")
HAND = r"C:\Windows\Fonts\UDDigiKyokashoN-R.ttc"
INK_BLUE = (24, 32, 78)
INK_BLACK = (28, 26, 26)
BAR = (14, 13, 13)


def hand(size):
    return ImageFont.truetype(HAND, size) if os.path.exists(HAND) else _font(size)


def gothic(size):
    return _font(size, mincho=False)


def paper(w, h, base=(242, 239, 230), seed=1, grain=0.05):
    """紙の地（ごく薄いむらと繊維）"""
    g = fbm(w, 0.6, seed, m=h)
    blot = fbm(w, 3.0, seed + 1, m=h)
    a = np.array(base, dtype=float)[None, None, :] * (1.0 + (g[..., None] - 0.5) * grain + (blot[..., None] - 0.5) * 0.04)
    return rgb(a)


def parse(line):
    """［答え］を空欄として地の文と分ける → [(文, 空欄か)]"""
    out, i = [], 0
    while i < len(line):
        a = line.find("［", i)
        b = line.find("］", a + 1) if a >= 0 else -1
        if a < 0 or b < 0:
            out.append((line[i:], False)); break
        if a > i:
            out.append((line[i:a], False))
        out.append((line[a + 1:b], True))
        i = b + 1
    return out


def draw_line(d, x, y, line, font, ink, hand_bar=False, rnd=None, jitter=0.0):
    """1行を書く。空欄は答えの幅ぶん黒く塗る（手書きはマーカーで何度も塗り潰した感じ）"""
    size = font.size
    for text, blank in parse(line):
        w = font.getlength(text)
        if blank:
            x0, x1 = x - 2, x + w + 2
            y0, y1 = y + size * 0.05, y + size * 1.08
            if hand_bar:
                for k in range(5):
                    dy = rnd.uniform(-2, 2)
                    d.rounded_rectangle([x0 + rnd.uniform(-3, 2), y0 + dy, x1 + rnd.uniform(-2, 4), y1 + dy], radius=6, fill=BAR)
            else:
                d.rectangle([x0, y0, x1, y1], fill=BAR)
        else:
            if jitter > 0:
                cx = x
                for ch in text:
                    d.text((cx, y + rnd.uniform(-jitter, jitter)), ch, font=font, fill=ink)
                    cx += font.getlength(ch)
            else:
                d.text((x, y), text, font=font, fill=ink)
        x += w


def guide():
    """研究所応接室の来客用の案内状（A4 縦・印刷）"""
    w, h = 840, 1188
    img = paper(w, h, (246, 245, 240), 11)
    d = ImageDraw.Draw(img)
    # 上の帯とロゴ代わりの細罫
    d.rectangle([60, 70, w - 60, 74], fill=(60, 80, 110))
    d.text((60, 86), "三木脳神経総合研究所", font=gothic(26), fill=(60, 80, 110))
    d.text((w - 60 - gothic(20).getlength("MIKI NEURO INSTITUTE"), 92), "MIKI NEURO INSTITUTE", font=gothic(20), fill=(120, 130, 150))
    f = _font(34)
    lines = ["《ご来訪の皆さまへ》", "",
             "当研究所では、損傷した記憶領域をAIで補完し、",
             "［人格の連続性］を維持する研究を進めております。", "",
             "本日のご案内は、［神経計測担当］の［松原］が",
             "務めます。"]
    y = 240
    for s in lines:
        draw_line(d, 70, y, s, f, INK_BLACK)
        y += 64 if s else 40
    d.text((w - 60 - _font(26).getlength("三木脳神経総合研究所"), h - 160), "三木脳神経総合研究所", font=_font(26), fill=INK_BLACK)
    d.rectangle([60, h - 90, w - 60, h - 88], fill=(60, 80, 110))
    save("guide", img)


def diarypage():
    """松原の日記の欠けたページ（横罫のノートを切り取った頁。左端がミシン目でちぎれている）"""
    w, h = 760, 1060
    img = paper(w, h, (240, 237, 226), 21)
    d = ImageDraw.Draw(img)
    for i in range(26):
        d.line([(30, 70 + i * 38), (w - 30, 70 + i * 38)], fill=(172, 192, 212), width=2)
    d.line([(96, 0), (96, h)], fill=(214, 150, 150), width=2)
    rnd = random.Random(23)
    f = hand(27)
    lines = ["4月2日　所長から「例の件」を打ち明けられた。",
             "病棟の、名前の読めない少女。",
             "その父親は［毎日面会に来られる］。",
             "所長は、その人を研究所に招いたのだという。", "",
             "4月9日　補完が、［研究員］の脳情報で学習している。",
             "所長の返事は「［今は結論を出すな］」。", "",
             "4月21日　今夜、［解析室］で、あの人と話す。",
             "もし僕に何かあったら──机の引き出しを。"]
    y = 40
    for s in lines:
        if s:
            draw_line(d, 110, y, s, f, INK_BLUE, hand_bar=True, rnd=rnd, jitter=1.0)
        y += 76 if s else 38
    save("diarypage", img)   # 左端のちぎれは形（メッシュ）で作る


def page303():
    """川崎の観察記録の続きのページ（印刷の書式に手書き）"""
    w, h = 840, 1188
    img = paper(w, h, (244, 243, 238), 31)
    d = ImageDraw.Draw(img)
    d.text((60, 60), "患者観察記録", font=gothic(36), fill=(50, 60, 70))
    d.text((60, 112), "記録者：", font=gothic(24), fill=(50, 60, 70))
    d.text((160, 106), "川崎", font=hand(32), fill=INK_BLUE)
    d.rectangle([56, 160, w - 56, 163], fill=(120, 130, 140))
    for i in range(18):
        d.line([(56, 230 + i * 52), (w - 56, 230 + i * 52)], fill=(200, 205, 210), width=1)
    rnd = random.Random(33)
    f = hand(30)
    lines = ["・303号　名前は、まだ書かない。お父さんとの約束。",
             "　今日も［お父さんの声のときだけ］、目が動いた。",
             "　絵本は『［おやすみ］』。",
             "　最後のページで、いつも声が小さくなる。", "",
             "・私がこの仕事を選んだのは、",
             "　［眠っている人を起こす］ため。",
             "　この子を起こすのは、薬じゃない。",
             "　たぶん、あの声だ。"]
    y = 192
    for s in lines:
        if s:
            draw_line(d, 66, y, s, f, INK_BLUE, hand_bar=True, rnd=rnd, jitter=0.8)
        y += 52 * 2 if s else 52
    # 欄外の小さな似顔絵（丸い顔と王冠）
    d.ellipse([w - 170, h - 230, w - 90, h - 150], outline=INK_BLUE, width=3)
    d.line([(w - 150, h - 182), (w - 140, h - 182)], fill=INK_BLUE, width=3)
    d.line([(w - 120, h - 182), (w - 110, h - 182)], fill=INK_BLUE, width=3)
    d.arc([w - 150, h - 180, w - 110, h - 160], 20, 160, fill=INK_BLUE, width=3)
    d.line([(w - 165, h - 232), (w - 150, h - 262), (w - 130, h - 238), (w - 112, h - 262), (w - 95, h - 232)], fill=INK_BLUE, width=3)
    save("page303", img)


def kmemo2():
    """海尾のメモの破り取られた後半（上の端がちぎれている）"""
    w, h = 900, 760
    img = paper(w, h, (238, 236, 228), 41)
    d = ImageDraw.Draw(img)
    for i in range(14):
        d.line([(24, 70 + i * 50), (w - 24, 70 + i * 50)], fill=(190, 200, 205), width=2)
    rnd = random.Random(43)
    f = hand(30)
    lines = ["「──と、書いたのは間違いだった。",
             "主任はその夜、［札幌］にいた。",
             "第12研究室に入ったのは、［主任じゃない］。",
             "主任のIDを持ち出せたのは、主任に招かれ、",
             "書斎に出入りできた人間だけだ。",
             "［久賀 夏樹］。毎晩、病棟で絵本を読んでいる、",
             "あの父親だ。",
             "明日、本人と話す。［正しいこと］をしに行く」"]
    y = 50
    for s in lines:
        draw_line(d, 40, y, s, f, INK_BLACK, hand_bar=True, rnd=rnd, jitter=0.6)
        y += 84
    save("kmemo2", img)   # 上の端のちぎれは形（メッシュ）で作る


def visitlog():
    """面会相談の記録票（書式の印刷＋所長の手書き。相談者の氏名だけ塗り潰し）"""
    w, h = 840, 1188
    img = paper(w, h, (246, 245, 240), 51)
    d = ImageDraw.Draw(img)
    d.text((60, 60), "面会相談　記録票", font=gothic(40), fill=(40, 50, 60))
    d.text((w - 60 - gothic(22).getlength("三木脳神経総合研究所"), 76), "三木脳神経総合研究所", font=gothic(22), fill=(80, 90, 100))
    rows = [("対応", "所長"), ("相談者", "ご家族（父）"), ("氏名", None), ("内容", "娘さんが、目を覚まさない。"), ("", "受け入れ先を探している。"),
            ("所見", "当研究所の病棟で受け入れる。"), ("", "費用は問わない。"), ("", "以後の対応は、所長が直接行う。")]
    y = 150
    fh = hand(32)
    for label, val in rows:
        d.rectangle([56, y, w - 56, y + 84], outline=(120, 130, 140), width=2)
        d.line([(200, y), (200, y + 84)], fill=(120, 130, 140), width=2)
        if label:
            d.text((74, y + 26), label, font=gothic(26), fill=(50, 60, 70))
        if val is None:
            d.rectangle([224, y + 18, 560, y + 66], fill=BAR)
        else:
            d.text((224, y + 22), val, font=fh, fill=INK_BLUE)
        y += 84
    save("visitlog", img)


def envelope(name, text, seed, sealed=True, w=900, h=560, base=(236, 226, 200)):
    """封筒の表（宛名だけ手書き）。sealed = 糊付けしてある"""
    img = paper(w, h, base, seed, grain=0.08)
    d = ImageDraw.Draw(img)
    f = hand(64)
    y = h * 0.5 - 40
    lines = text.split("\n")
    y = h * 0.5 - len(lines) * 40
    for s in lines:
        d.text(((w - f.getlength(s)) / 2, y), s, font=f, fill=INK_BLACK)
        y += 84
    save(name, img)


def binders():
    """研究所の書類棚のバインダーの背（8種類のアトラス。横に並ぶ）"""
    w, h = 1024, 512
    img = Image.new("RGB", (w, h), (0, 0, 0))
    d = ImageDraw.Draw(img)
    cols = [(40, 60, 100), (120, 40, 40), (50, 90, 70), (200, 190, 170), (70, 70, 75), (40, 60, 100), (150, 120, 60), (90, 50, 90)]
    labels = ["被験者記録\n2014", "被験者記録\n2015", "臨床試験\n申請", "見学\n受付簿", "設備\n点検", "倫理委員会\n議事", "補完試験\n報告", "研究計画\n草案"]
    sw = w // 8
    for i in range(8):
        x0 = i * sw
        d.rectangle([x0, 0, x0 + sw - 2, h], fill=cols[i])
        d.rectangle([x0 + 18, 60, x0 + sw - 20, 250], fill=(240, 238, 230))
        f = gothic(20)
        for k, s in enumerate(labels[i].split("\n")):
            # 縦書きの代わりに短い横書きを2行
            d.text((x0 + 24, 80 + k * 40), s, font=f, fill=(30, 30, 30))
        d.ellipse([x0 + sw // 2 - 18, 360, x0 + sw // 2 + 18, 396], outline=(220, 220, 220), width=4)
    a = np.asarray(img).astype(float) * (0.92 + fbm(w, 0.8, 61, m=h)[..., None] * 0.12)
    save("binders", rgb(a))


def namecards():
    """病棟の職員ロッカーの名札（3段のアトラス：田村／川崎／森。札の比率は 0.124 x 0.038）"""
    w, row = 600, 184
    img = Image.new("RGB", (w, row * 3), (246, 246, 242))
    d = ImageDraw.Draw(img)
    f = gothic(120)
    for i, s in enumerate(("田村", "川崎", "森")):
        y0 = i * row
        d.rectangle([4, y0 + 4, w - 5, y0 + row - 5], outline=(190, 190, 186), width=4)
        tw = f.getlength(s)
        d.text(((w - tw) / 2, y0 + (row - 120) / 2 - 12), s, font=f, fill=(30, 32, 40))
    save("namecards", img)


if __name__ == "__main__":
    namecards()
    guide()
    diarypage()
    page303()
    kmemo2()
    visitlog()
    envelope("env_saeki", "君へ", 71)
    envelope("env_ward", "303号の、お父さんへ", 72, sealed=False, base=(244, 240, 232))
    envelope("env_kuroda", "お姉ちゃんへ\nちびへ", 73, base=(232, 236, 244))
    binders()
