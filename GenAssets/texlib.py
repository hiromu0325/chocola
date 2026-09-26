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


# ---------------------------------------------------------------- 住宅系の共通素材（色を指定して使う）

def planks(n, seed, c0, c1, count=8, vary=0.12, maps=False):
    """床板（1枚 = 1m、幅 1/count の板。板ごとに色と木目を変える。長手は U）。(色, 法線) を返す。
    maps=True なら (色, 法線, 詳細) で、詳細は光沢・AO マップ用の {"h": 木目の高さ, "edge": 板の継ぎ目,
    "cut": 板の端の突き付け, "plank": 板の番号, "rnd": 板ごとの乱数} を返す"""
    rnd = np.random.default_rng(seed)
    y, x = np.mgrid[0:n, 0:n] / n
    plank = np.floor(y * count).astype(int)
    col = np.zeros((n, n, 3)); h = np.zeros((n, n)); cuts = np.zeros((n, n), bool); prnd = np.zeros((n, n))
    for p in range(count):
        m = plank == p
        warp = (fbm(n, 2.4, seed + 10 + p, ax=10.0, ay=1.0) - 0.5) * 0.8
        ring = 0.5 + 0.5 * np.sin(2 * np.pi * ((y * count - p) * 3 + warp * 2))
        streak = fbm(n, 1.0, seed + 40 + p, ax=40.0, ay=1.0)
        t = np.clip(0.45 + (streak - 0.5) * 0.7 - (ring > 0.8) * 0.15 + rnd.uniform(-vary, vary), 0, 1)
        c = colorize(t, c0, c1)
        col[m] = c[m]; h[m] = streak[m]
        cut = rnd.uniform(0.1, 0.9)
        col[m & (np.abs(x - cut) < 0.0025)] *= 0.4
        cuts |= m & (np.abs(x - cut) < 0.0025)
        prnd[m] = rnd.uniform(0, 1)
    edge = np.abs((y * count) % 1 - 0.5) > 0.492
    col[edge] *= 0.35; h[edge] = 0
    col *= (0.92 + fbm(n, 2.2, seed + 60)[..., None] * 0.12)
    nrm = normal_map(h * 0.6 - edge * 0.8, 1.5)
    if maps:
        return rgb(col), nrm, {"h": h, "edge": edge, "cut": cuts, "plank": plank, "rnd": prnd}
    return rgb(col), nrm


def ms_map(metal, smooth):
    """URP Lit の Metallic＋Smoothness マップ（R = 金属度、A = 滑らかさ。どちらも 0〜1 の配列）"""
    n0, n1 = smooth.shape
    a = np.zeros((n0, n1, 4))
    a[..., 0] = np.broadcast_to(metal, smooth.shape) * 255
    a[..., 3] = np.clip(smooth, 0, 1) * 255
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def ao_map(ao):
    """AO マップ（1 = 遮られていない、0 = 真っ暗）"""
    v = np.clip(ao, 0, 1)[..., None] * 255
    return Image.fromarray(np.repeat(v, 3, axis=2).astype(np.uint8))


def wood(n, seed, c0, c1, rings=7):
    """木目（1枚 = 1m、木目は U）。(色, 法線) を返す"""
    y, x = np.mgrid[0:n, 0:n] / n
    warp = (fbm(n, 2.6, seed, ax=10.0, ay=1.0) - 0.5) * 0.9
    ring = np.clip(((0.5 + 0.5 * np.sin(2 * np.pi * (y * rings + warp * 3))) - 0.6) * 3.5, 0, 1)
    streak = fbm(n, 1.1, seed + 1, ax=40.0, ay=1.0)
    pores = fbm(n, 0.3, seed + 2, ax=12.0, ay=1.0)
    t = np.clip(0.5 + (streak - 0.5) * 0.7 - ring * 0.2 - (pores > 0.64) * 0.08, 0, 1)
    return rgb(colorize(t, c0, c1)), normal_map(streak * 0.5 - ring * 0.3, 1.0)


def fabric(n, seed, base, weave=256, slub=0.05):
    """織物（1枚 = 0.5m）。(色, 法線) を返す"""
    y, x = np.mgrid[0:n, 0:n] / n
    wx = 0.5 + 0.5 * np.sin(2 * np.pi * x * weave)
    wy = 0.5 + 0.5 * np.sin(2 * np.pi * y * weave)
    w = np.where(((np.floor(x * weave) + np.floor(y * weave)) % 2) == 0, wx, wy)
    s = fbm(n, 1.2, seed, ax=0.1, ay=1.0) * 0.5 + fbm(n, 1.2, seed + 1, ax=1.0, ay=0.1) * 0.5
    lum = 0.9 + w * 0.1 + (s - 0.5) * slub * 2 + (fbm(n, 0.4, seed + 2) - 0.5) * 0.06
    return rgb(np.array(base, dtype=float)[None, None, :] * lum[..., None]), normal_map(w, 1.6)
