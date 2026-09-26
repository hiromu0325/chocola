# -*- coding: utf-8 -*-
"""回廊（ロの字の廊下）と、扉の奥の暗い廊下のテクスチャを手続き的に作る（継ぎ目なし）。

    python GenAssets/corridor/make_textures.py

1つの素材につき 色（<名前>.png）・法線（_n）・光沢（_ms：R=金属度 A=滑らかさ）・AO（_ao）を作る。
光沢と AO は色・法線と同じノイズから作るので、擦れ・埃・継ぎ目・溝が見た目と質感でそろう。
（URP の SSAO は使わない方針なので、隙間の暗がりは AO マップで出す）

出力: GenAssets/corridor/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/Corridor/*.png
"""
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, band, normal_map, rgb, Saver, planks, wood, ms_map, ao_map  # noqa: E402

save = Saver(HERE, "Corridor")


def blur(a, r):
    img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    return np.asarray(img.filter(ImageFilter.GaussianBlur(r)), dtype=float) / 255.0


def scratches(n, seed, count, length=(20, 120), width=1, angle=None):
    """細い引っかき傷（0〜1）。angle を与えると方向をそろえる（研磨目）"""
    rnd = random.Random(seed)
    img = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(img)
    for _ in range(count):
        x, y = rnd.uniform(0, n), rnd.uniform(0, n)
        a = angle + rnd.uniform(-0.08, 0.08) if angle is not None else rnd.uniform(0, np.pi)
        L = rnd.uniform(*length)
        v = rnd.randint(90, 255)
        for ox in (-n, 0, n):
            for oy in (-n, 0, n):
                d.line([x + ox, y + oy, x + ox + np.cos(a) * L, y + oy + np.sin(a) * L], fill=v, width=width)
    return np.asarray(img, dtype=float) / 255.0


def plaster(n=1024):
    """白い漆喰（1枚 = 1m）：ごく淡いむら、こての跡、細かい砂粒。こてで押さえた所だけ少し艶がある"""
    base = np.array([226, 223, 216], dtype=float)
    mott = fbm(n, 1.3, 3101)
    trowel = fbm(n, 2.2, 3102, ax=1.0, ay=2.5)
    grain = band(n, 180, 420, 3103)
    lum = 0.965 + (mott - 0.5) * 0.06 + (trowel - 0.5) * 0.03 + (grain - 0.5) * 0.025
    save("plaster", rgb(base[None, None, :] * lum[..., None]), normal_map(trowel * 0.8 + grain * 0.5, 2.2))
    smooth = 0.1 + np.clip(trowel - 0.55, 0, 1) * 0.35 + (grain - 0.5) * 0.04
    save("plaster_ms", ms_map(0.0, smooth))
    save("plaster_ao", ao_map(1.0 - np.clip(0.5 - grain, 0, 1) * 0.25))


def paint(n=1024):
    """塗装した木（1枚 = 1m、木目は U）：扉・枠・腰壁に色を掛けて使う。
    下の木目が導管の筋として浮き、刷毛目と塗りむら、薄い埃と擦れで艶が場所ごとに変わる"""
    img, _ = wood(n, 3111, (205, 205, 205), (238, 238, 238), rings=9)
    a = np.asarray(img).astype(float)
    lum = a.mean(axis=2) / 255.0
    grain = (lum - lum.mean()) / (lum.std() + 1e-6)                # 木目（塗膜越しの凹凸）
    brush = fbm(n, 1.6, 3112, ax=30.0, ay=1.0)                      # 刷毛目（木目方向）
    peel = band(n, 60, 140, 3113)                                   # 塗膜の柚子肌
    dust = np.clip((fbm(n, 1.4, 3114) - 0.6) * 3.0, 0, 1)           # 薄い埃・手垢のむら
    dings = blur(scratches(n, 3115, 60, (4, 18), 2), 1.0)           # 小さな打痕
    a = 226 + (a - a.mean()) * 0.16
    a *= (0.97 + (brush[..., None] - 0.5) * 0.06) * (1.0 - dust[..., None] * 0.05) * (1.0 - dings[..., None] * 0.12)
    h = grain * 0.04 + brush * 0.5 + peel * 0.3 - dings * 0.8
    save("paint", rgb(a), normal_map(h, 2.2))
    smooth = 0.58 + (brush - 0.5) * 0.12 - np.clip(-grain, 0, 3) * 0.035 - dust * 0.28 - dings * 0.3 + (peel - 0.5) * 0.05
    save("paint_ms", ms_map(0.0, smooth))
    save("paint_ao", ao_map(1.0 - np.clip(-grain, 0, 3) * 0.05 - dings * 0.35))


def floor(n=1024):
    """焦茶の床板（ウレタン塗装）：板ごとに艶が違い、継ぎ目と導管は艶消し、細かい擦り傷と埃"""
    img, nrm, m = planks(n, 3141, (58, 36, 26), (106, 68, 46), count=8, maps=True)
    save("floor", img, nrm)
    dust = np.clip((fbm(n, 1.5, 3142) - 0.58) * 2.5, 0, 1)
    scr = blur(scratches(n, 3143, 260, (30, 160), 1, angle=0.0), 0.6)
    smooth = 0.5 + (m["rnd"] - 0.5) * 0.16 + (m["h"] - 0.5) * 0.1 - scr * 0.25 - dust * 0.3
    smooth[m["edge"]] = 0.08
    smooth[m["cut"]] = 0.1
    save("floor_ms", ms_map(0.0, smooth))
    gap = blur(m["edge"].astype(float) + m["cut"].astype(float), 2.5)
    save("floor_ao", ao_map(1.0 - np.clip(gap * 1.6, 0, 0.6) - (1.0 - m["h"]) * 0.06))


def old_plaster(n=1024):
    """扉の奥の古い廊下の壁（1枚 = 幅2.5m x 高さ2.5m、画像の下端 = 床）：
    黄ばんだ漆喰、上からの雨染み、床際の黒ずみ、ひび割れ。乾いた雨染みだけ薄く艶がある"""
    y, x = np.mgrid[0:n, 0:n] / n
    base = np.array([214, 206, 190], dtype=float)
    mott = fbm(n, 1.4, 3121)
    col = base[None, None, :] * (0.92 + (mott[..., None] - 0.5) * 0.12)
    drip = fbm(n, 1.8, 3122, ax=6.0, ay=0.4)
    stain = np.clip((drip - 0.55) * 3.0, 0, 1) * np.clip(1.0 - y * 1.6, 0, 1) ** 0.7
    col = col * (1 - stain[..., None] * 0.35) + np.array([120, 100, 70])[None, None, :] * stain[..., None] * 0.25
    grime = np.clip((y - 0.72) / 0.28, 0, 1) ** 1.6 * (0.6 + 0.4 * fbm(n, 1.5, 3123))
    col *= (1 - grime[..., None] * 0.45)
    img = rgb(col)
    d = ImageDraw.Draw(img)
    rnd = random.Random(3124)
    cracks = Image.new("L", (n, n), 0)
    dc = ImageDraw.Draw(cracks)
    for k in range(7):
        px, py = rnd.uniform(0, n), rnd.uniform(0, n * 0.6)
        ang = rnd.uniform(1.2, 1.9)
        for s in range(rnd.randint(20, 55)):
            ang += rnd.uniform(-0.35, 0.35)
            nx, ny = px + np.cos(ang) * 6, py + np.sin(ang) * 6
            for ox in (-n, 0, n):
                d.line([px + ox, py, nx + ox, ny], fill=(118, 108, 94), width=1)
                dc.line([px + ox, py, nx + ox, ny], fill=255, width=2)
            px, py = nx % n, ny
    crack = np.asarray(cracks.filter(ImageFilter.GaussianBlur(1)), dtype=float) / 255.0
    h = mott * 0.5 + drip * 0.2 - crack * 0.6
    save("old_plaster", img, normal_map(h, 2.4))
    save("old_plaster_ms", ms_map(0.0, 0.08 + stain * 0.18 - grime * 0.05 + (mott - 0.5) * 0.04))
    save("old_plaster_ao", ao_map(1.0 - crack * 0.5 - grime * 0.25))


def old_floor(n=1024):
    """扉の奥の廊下の床（1枚 = 1m）：暗い床板に埃が溜まり、継ぎ目に汚れ"""
    img, nrm, m = planks(n, 3131, (48, 32, 24), (88, 60, 42), count=8, maps=True)
    a = np.asarray(img).astype(float)
    dust = fbm(n, 1.5, 3132)
    dm = np.clip((dust - 0.35) * 1.8, 0, 1)
    a = a * (1 - dm[..., None] * 0.5) + np.array([112, 104, 94])[None, None, :] * dm[..., None] * 0.5
    save("old_floor", rgb(a), nrm)
    smooth = 0.3 + (m["rnd"] - 0.5) * 0.1 - dm * 0.22
    smooth[m["edge"]] = 0.05
    save("old_floor_ms", ms_map(0.0, smooth))
    gap = blur(m["edge"].astype(float) + m["cut"].astype(float), 2.5)
    save("old_floor_ao", ao_map(1.0 - np.clip(gap * 1.8, 0, 0.7)))


def metal(name, n, seed, base, tarnish, polish=0.78):
    """金物（1枚 = 0.25m）：研磨目の細い筋、指で触る所の曇り、くすみ、小傷"""
    brushed = fbm(n, 1.2, seed, ax=60.0, ay=1.0)                    # 研磨目（U 方向）
    tar = np.clip((fbm(n, 1.6, seed + 1) - 0.45) * 2.2, 0, 1)       # くすみ・酸化
    smudge = np.clip((fbm(n, 1.1, seed + 2) - 0.55) * 2.5, 0, 1)    # 指紋・曇り
    scr = blur(scratches(n, seed + 3, 140, (10, 60), 1), 0.5)
    col = np.array(base, float)[None, None, :] * (0.94 + (brushed[..., None] - 0.5) * 0.12)
    col = col * (1 - tar[..., None]) + np.array(tarnish, float)[None, None, :] * tar[..., None]
    col *= (1.0 - scr[..., None] * 0.08)
    save(name, rgb(col), normal_map(brushed * 0.35 - scr * 0.6, 1.8))
    smooth = polish + (brushed - 0.5) * 0.08 - tar * 0.3 - smudge * 0.22 - scr * 0.25
    save(name + "_ms", ms_map(1.0 - tar * 0.35, smooth))


if __name__ == "__main__":
    plaster(); paint(); floor(); old_plaster(); old_floor()
    metal("brass", 512, 3151, (212, 170, 96), (120, 100, 62))
    metal("steel", 512, 3161, (150, 152, 156), (92, 90, 86), polish=0.62)
