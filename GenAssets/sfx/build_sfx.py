"""Kenney の CC0 素材から、ゲームで使う効果音を作って Unity の Resources/Sfx/<key>/ に書き出す。

  python -I build_sfx.py <kenney展開先> <出力先(Assets/Audio/Resources/Sfx)>

各音: モノラル化 → 頭と尻の無音を切る → 短いフェード → 鳴っている部分の RMS を key ごとの目標に合わせる（ピークは 0.95 まで）。
いくつかは素材を重ねて作る（鍵の掛かった扉を揺する音、錠が外れる音、引き出し）。
"""
import sys, pathlib, shutil
import numpy as np, soundfile as sf
from scipy.signal import resample_poly

SRC = pathlib.Path(sys.argv[1])
DST = pathlib.Path(sys.argv[2])
SR = 44100

RPG = SRC / "kenney_rpg-audio" / "Audio"
IMP = SRC / "kenney_impact-sounds" / "Audio"
UI = SRC / "kenney_interface-sounds" / "Audio"


def load(path):
    d, sr = sf.read(str(path), always_2d=True)
    m = d.mean(axis=1)
    if sr != SR:
        g = np.gcd(sr, SR)
        m = resample_poly(m, SR // g, sr // g)
    return m.astype(np.float64)


def pitch(m, k):
    """k 倍の高さに（長さは 1/k）。k<1 で低く重く"""
    if abs(k - 1.0) < 1e-6:
        return m
    up, down = int(round(1000 / k)), 1000
    g = np.gcd(up, down)
    return resample_poly(m, up // g, down // g)


def trim(m, thr_db=-50.0, pre=0.004, post=0.03):
    peak = np.abs(m).max()
    if peak <= 0:
        return m
    thr = peak * 10 ** (thr_db / 20)
    idx = np.nonzero(np.abs(m) > thr)[0]
    a = max(0, idx[0] - int(pre * SR))
    b = min(len(m), idx[-1] + int(post * SR))
    return m[a:b]


def fade(m, fin=0.002, fout=0.02):
    m = m.copy()
    n1, n2 = min(len(m), int(fin * SR)), min(len(m), int(fout * SR))
    if n1 > 0:
        m[:n1] *= np.linspace(0, 1, n1)
    if n2 > 0:
        m[-n2:] *= np.linspace(1, 0, n2)
    return m


def active_rms(m):
    peak = np.abs(m).max()
    a = m[np.abs(m) > peak * 0.05]
    return float(np.sqrt((a ** 2).mean())) if len(a) else 0.0


def level(m, rms):
    r = active_rms(m)
    if r <= 0:
        return m
    m = m * (rms / r)
    p = np.abs(m).max()
    if p > 0.95:
        m = m * (0.95 / p)
    return m


def mix(parts, length=None):
    """parts: [(signal, start秒, gain)]"""
    end = max(int(s * SR) + len(x) for x, s, g in parts)
    out = np.zeros(length or end)
    for x, s, g in parts:
        i = int(s * SR)
        out[i:i + len(x)] += x[: max(0, len(out) - i)] * g
    return out


def write(key, name, m, rms):
    m = fade(trim(m))
    m = level(m, rms)
    d = DST / key
    d.mkdir(parents=True, exist_ok=True)
    sf.write(str(d / (name + ".ogg")), m.astype(np.float32), SR, format="OGG", subtype="VORBIS")
    print(f"{key:13s} {name:22s} {len(m) / SR:5.2f}s peak {np.abs(m).max():.2f}")


# key: (目標RMS, [(名前, 信号)])
def single(folder, *names):
    return [(n, load(folder / (n + ".ogg"))) for n in names]


latch = load(RPG / "metalLatch.ogg")
mclick = trim(load(RPG / "metalClick.ogg"), thr_db=-26.0)   # 立ち上がり前の小さな擦れは切る
wood = trim(load(IMP / "impactWood_light_002.ogg"))
creak3 = load(RPG / "creak3.ogg")

rattle = []
for v, (ks, gains) in enumerate([((1.0, 0.93, 1.06), (0.8, 1.0, 0.65)), ((0.96, 1.04, 0.9), (0.9, 0.75, 0.6))]):
    lt = trim(latch)
    parts = []
    t = 0.0
    for k, g in zip(ks, gains):
        parts.append((pitch(lt, k), t, g))
        parts.append((pitch(wood, 0.8 * k), t + 0.004, 0.45 * g))   # 扉の板が枠に当たる鈍い音
        t += 0.105 + 0.012 * v
    rattle.append((f"rattle_{v + 1}", mix(parts)))

unlock = [("unlock_1", mix([(mclick, 0.0, 1.0), (pitch(trim(latch), 0.85), 0.10, 0.8)])),
          ("unlock_2", mix([(pitch(mclick, 0.92), 0.0, 1.0), (pitch(trim(latch), 0.8), 0.12, 0.75)]))]

drawer = [("drawer_1", mix([(pitch(creak3, 0.8), 0.0, 1.0), (pitch(wood, 1.1), 0.30, 0.35)])),
          ("drawer_2", mix([(pitch(creak3, 0.72), 0.0, 1.0), (pitch(wood, 1.0), 0.34, 0.3)]))]


# ---- 電車の車端の引き戸（手で開ける金属の引き戸）：戸車がレールを転がるゴロゴロ＋金属の戸板の響き、端で止まる音 ----

def rolling(dur, seed, lo=90.0, hi=1600.0, rate=26.0):
    """戸車がレールを転がる音。帯域を絞ったノイズを、戸車の回転とレールの継ぎ目でざらつかせ、金属の戸板の響きを通す"""
    rng = np.random.default_rng(seed)
    n = int(dur * SR)
    tt = np.arange(n) / SR
    spec = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / SR)
    spec *= ((f > lo) & (f < hi)) / (1 + (f / 500.0) ** 2) ** 0.35
    rum = np.fft.irfft(spec, n)
    rum /= np.abs(rum).max() + 1e-9
    grit = 0.65 + 0.35 * np.sin(2 * np.pi * (rate + 6 * np.sin(2 * np.pi * 1.7 * tt)) * tt) ** 2
    ticks = np.zeros(n)
    for k in range(int(dur * 9)):                        # 継ぎ目・ほこりのコツコツ
        i = int(rng.uniform(0, n - 400))
        ticks[i:i + 300] += rng.uniform(0.3, 0.8) * np.exp(-np.arange(300) / 40.0) * rng.standard_normal(300)
    body = trim(load(IMP / "impactPlate_light_001.ogg"))[: int(0.12 * SR)]   # 金属の戸板の響き（短く切って畳み込む）
    body = body / (np.abs(body).sum() + 1e-9) * 40.0
    sig = np.convolve(rum * grit + ticks * 0.6, body)[:n]
    hum = np.sin(2 * np.pi * 82 * tt + 0.6 * np.sin(2 * np.pi * 2.3 * tt)) * 0.18   # 戸板のうなり
    return sig / (np.abs(sig).max() + 1e-9) + hum


def envelope(n, a, r):
    e = np.ones(n)
    na, nr = int(a * SR), int(r * SR)
    e[:na] = np.linspace(0, 1, na) ** 1.5
    e[-nr:] = np.linspace(1, 0, nr) ** 1.2
    return e


click_t = trim(load(RPG / "metalClick.ogg"), thr_db=-26.0)
thunk = trim(load(IMP / "impactMetal_medium_001.ogg"))
bang = trim(load(IMP / "impactMetal_heavy_000.ogg"))
plate = trim(load(IMP / "impactPlate_heavy_001.ogg"))

train_open = []
for v, (dur, seed) in enumerate([(0.95, 301), (1.05, 302)]):
    r = rolling(dur, seed)
    r *= envelope(len(r), 0.12, 0.22)
    train_open.append((f"open_{v + 1}", mix([(pitch(click_t, 0.9), 0.0, 0.7),              # 取っ手のかんぬきを外す
                                              (r, 0.05, 0.38),                              # ゴロゴロ…と引き込む
                                              (pitch(thunk, 0.7 + 0.05 * v), 0.05 + dur - 0.06, 0.9)])))   # 戸袋で止まる
train_close = []
for v, (dur, seed) in enumerate([(0.62, 311), (0.7, 312)]):
    r = rolling(dur, seed, rate=34.0)
    r *= envelope(len(r), 0.06, 0.05)
    end = dur
    train_close.append((f"close_{v + 1}", mix([(r, 0.0, 0.32),                                   # 勢いよく戻る
                                                (pitch(bang, 0.72 + 0.04 * v), end - 0.02, 1.0),    # ガシャン
                                                (pitch(plate, 0.8), end - 0.01, 0.55),
                                                (pitch(trim(latch), 0.9), end + 0.03, 0.45)])))     # かんぬきが掛かる
train_rattle = []
for v, ks in enumerate([(1.0, 0.94, 1.05), (0.96, 1.03, 0.92)]):
    lt = trim(latch)
    parts, tt = [], 0.0
    for k in ks:
        parts.append((pitch(lt, k), tt, 0.8))
        parts.append((pitch(thunk, 0.85 * k), tt + 0.003, 0.35))   # 鋼の戸板が枠に当たる
        tt += 0.1 + 0.01 * v
    train_rattle.append((f"rattle_{v + 1}", mix(parts)))

SETS = {
    # 扉
    "door_open":   (0.16, single(RPG, "doorOpen_1", "doorOpen_2")),
    "door_close":  (0.20, single(RPG, "doorClose_1", "doorClose_2", "doorClose_3", "doorClose_4")),
    "door_latch":  (0.16, single(RPG, "metalLatch")),
    "door_rattle": (0.18, rattle),
    "unlock":      (0.16, unlock),
    # 家具・物
    "creak":       (0.14, single(RPG, "creak1", "creak2")),
    "drawer":      (0.14, drawer),
    "clunk":       (0.20, single(IMP, "impactMetal_heavy_002", "impactMetal_heavy_004")),
    "switch":      (0.16, single(UI, "switch_002")),
    "click":       (0.14, single(UI, "click_002", "click_003")),
    "footstep":    (0.18, single(RPG, *[f"footstep0{i}" for i in range(10)])),
    # 紙・手帳
    "paper":       (0.10, single(RPG, "bookFlip2", "bookFlip3")),
    "pickup":      (0.10, single(RPG, "cloth2", "cloth3")),
    "book_open":   (0.12, single(RPG, "bookOpen")),
    "book_close":  (0.14, single(RPG, "bookClose")),
    "page":        (0.09, single(RPG, "bookFlip1", "bookFlip2", "bookFlip3")),
    # UI
    "ui_move":     (0.09, single(UI, "tick_002")),
    "ui_decide":   (0.12, single(UI, "select_002")),
    "ui_cancel":   (0.12, single(UI, "back_002")),
    "ui_error":    (0.12, single(UI, "error_008")),
    "solve":       (0.12, single(UI, "confirmation_001")),
    "dial_tick":   (0.12, single(UI, "tick_004")),
    "dial_buzz":   (0.12, single(UI, "error_006")),
    # 電車の車端の引き戸
    "train_door_open":   (0.16, train_open),
    "train_door_close":  (0.20, train_close),
    "train_door_rattle": (0.18, train_rattle),
}

if DST.exists():
    for k in SETS:
        if (DST / k).exists():
            for f in (DST / k).glob("*.ogg"):
                f.unlink()   # 作り直し（.meta は残して GUID を保つ）
for key, (rms, items) in SETS.items():
    for name, sig in items:
        write(key, name, sig, rms)
