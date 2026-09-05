# -*- coding: utf-8 -*-
"""生成テクスチャをタイル可能にする後処理。

使い方:
    python make_seamless.py <入力.png> [出力.png]

処理内容:
  1. 大きくぼかした明度で割って照明ムラ（低周波成分）を平坦化
  2. 半分オフセット＋クロスブレンドで上下左右の継ぎ目を消す
  3. <出力>_tile2x2.png を並べて目視検証用に出力
"""
import sys

from PIL import Image, ImageFilter
import numpy as np


def equalize_illumination(im, radius=96):
    blur = im.filter(ImageFilter.GaussianBlur(radius=radius))
    a = np.asarray(im).astype("float32")
    b = np.asarray(blur).astype("float32")
    mean = b.mean(axis=(0, 1), keepdims=True)
    out = a / (b + 1e-3) * mean
    return Image.fromarray(out.clip(0, 255).astype("uint8"))


def wrap_blend(img, band, horizontal):
    w, h = img.size
    off = Image.new("RGB", (w, h))
    mask = Image.new("L", (w, h), 255)
    if horizontal:
        off.paste(img.crop((w // 2, 0, w, h)), (0, 0))
        off.paste(img.crop((0, 0, w // 2, h)), (w // 2, 0))
        for x in range(band):
            a = int(255 * abs(x - band / 2) / (band / 2))
            mask.paste(a, (w // 2 - band // 2 + x, 0, w // 2 - band // 2 + x + 1, h))
    else:
        off.paste(img.crop((0, h // 2, w, h)), (0, 0))
        off.paste(img.crop((0, 0, w, h // 2)), (0, h // 2))
        for y in range(band):
            a = int(255 * abs(y - band / 2) / (band / 2))
            mask.paste(a, (0, h // 2 - band // 2 + y, w, h // 2 - band // 2 + y + 1))
    return Image.composite(off, img, mask)


def make_seamless(src, dst, blend=0.10):
    im = equalize_illumination(Image.open(src).convert("RGB"))
    w, h = im.size
    out = wrap_blend(wrap_blend(im, int(w * blend), True), int(h * blend), False)
    out.save(dst)

    tile = Image.new("RGB", (w * 2, h * 2))
    for dx in (0, w):
        for dy in (0, h):
            tile.paste(out, (dx, dy))
    tile.resize((800, 800), Image.LANCZOS).save(dst.replace(".png", "_tile2x2.png"))
    print("OK:", dst)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else src.replace(".png", "_seamless.png")
    make_seamless(src, dst)
