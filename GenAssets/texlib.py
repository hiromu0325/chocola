# -*- coding: utf-8 -*-
"""手続きテクスチャの共通部品（FFTノイズ・法線マップ・保存）。部屋ごとの make_textures.py から使う"""
import os

import numpy as np
from PIL import Image, ImageFont

ROOT = os.path.dirname(os.path.abspath(__file__))
UNITY_TEX = os.path.join(ROOT, "..", "project", "Assets", "EscapePrototype", "Textures", "HQ")


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


def colorize(t, c0, c1):
    t = t[..., None]
    return np.array(c0)[None, None, :] * (1 - t) + np.array(c1)[None, None, :] * t


# ---------------------------------------------------------------- 各テクスチャ


def _font(size, mincho=True):
    cands = (["yumin.ttf", "msmincho.ttc", "YuMincho.ttc"] if mincho else ["YuGothM.ttc", "msgothic.ttc", "meiryo.ttc"])
    for f in cands:
        p = os.path.join(r"C:\Windows\Fonts", f)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


class Saver:
    """GenAssets/<部屋>/tex（Blenderの確認用）と Unity の Textures/HQ/<部屋> の両方に保存する"""

    def __init__(self, room_dir, unity_name):
        self.out = os.path.join(room_dir, "tex")
        self.unity = os.path.join(UNITY_TEX, unity_name)

    def __call__(self, name, img, normal=None):
        os.makedirs(self.out, exist_ok=True)
        os.makedirs(self.unity, exist_ok=True)
        img.save(os.path.join(self.out, name + ".png"))
        img.save(os.path.join(self.unity, name + ".png"))
        if normal is not None:
            normal.save(os.path.join(self.out, name + "_n.png"))
            normal.save(os.path.join(self.unity, name + "_n.png"))
        print("OK", name)
