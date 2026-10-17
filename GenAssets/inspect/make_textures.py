# -*- coding: utf-8 -*-
"""調べる画面の実物（紙・革・段ボール）のテクスチャを手続き的に作る（継ぎ目なし）。

    python GenAssets/inspect/make_textures.py

実物のUVは実寸の箱投影（1m = 1）。1枚のテクスチャ＝1m四方として、紙の繊維・むら・革のしぼを細かく入れる。
色は白〜灰の「明るさのむら」だけにして、材質の色（INS_Paper など）を掛けて使う。
出力: GenAssets/inspect/tex/*.png と project/Assets/EscapePrototype/Textures/HQ/Inspect/*.png
"""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
from texlib import fbm, band, normal_map, rgb, Saver  # noqa: E402

save = Saver(HERE, "Inspect")
N = 1024


def blur(a, r):
    img = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))
    return np.asarray(img.filter(ImageFilter.GaussianBlur(r)), dtype=float) / 255.0


def paper():
    """紙：大きなむら（日焼け・湿気）＋細い繊維の筋＋ごく細かい粒"""
    mottle = fbm(N, 2.2, seed=11)
    fibers = band(N, 180, 420, seed=12) * 0.6 + fbm(N, 1.2, seed=13, ax=0.35, ay=1.0) * 0.4
    grain = band(N, 300, 512, seed=14)
    lum = 0.94 + (mottle - 0.5) * 0.07 + (fibers - 0.5) * 0.035 + (grain - 0.5) * 0.02
    save("paper", rgb(np.stack([lum * 255] * 3, axis=-1)))
    h = fibers * 0.6 + grain * 0.4
    save("paper_n", normal_map(h, 2.2))


def leather():
    """革：しぼ（小さな盛り上がりの網目）＋擦れ"""
    cells = band(N, 60, 140, seed=21)
    pebble = np.abs(cells - 0.5) * 2.0
    wear = fbm(N, 2.0, seed=22)
    lum = 0.82 + (pebble - 0.5) * 0.12 + (wear - 0.5) * 0.12
    save("leather", rgb(np.stack([lum * 255] * 3, axis=-1)))
    save("leather_n", normal_map(blur(1.0 - pebble, 0.8), 5.0))


def cardboard():
    """段ボール・ボール紙：粗い繊維と波打ち"""
    rough = fbm(N, 1.6, seed=31) * 0.6 + band(N, 120, 300, seed=32) * 0.4
    lum = 0.88 + (rough - 0.5) * 0.12
    save("cardboard", rgb(np.stack([lum * 255] * 3, axis=-1)))
    save("cardboard_n", normal_map(rough, 2.5))


if __name__ == "__main__":
    paper()
    leather()
    cardboard()
    print("ok")
