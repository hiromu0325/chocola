import math
from chocola_kit import Kit
import chocola_shell as S

O, I, H = 10.0, 7.0, 3.0
GAP = (I*2 - 10*1.0) / 11
DOOR_S = [-I + GAP + 0.5 + s*(1.0 + GAP) for s in range(10)]      # 内壁に沿った扉の中心

def corridor():
    k = Kit("Shell_corridor")
    mid, w = (O+I)/2, O-I
    for cx, cz, sx, sz in ((0, mid, O*2, w), (0, -mid, O*2, w), (mid, 0, w, I*2), (-mid, 0, w, I*2)):
        k.box(cx, -0.06, cz, sx, 0.12, sz, "LP_WoodFloor")
        k.box(cx, H+0.06, cz, sx, 0.12, sz, "LP_Ceiling")
    k.box(0, H/2, O, O*2+0.15, H, 0.15, "LP_White"); k.box(0, H/2, -O, O*2+0.15, H, 0.15, "LP_White")
    k.box(O, H/2, 0, 0.15, H, O*2, "LP_White"); k.box(-O, H/2, 0, 0.15, H, O*2, "LP_White")
    for sx in (1, -1):
        for sz in (1, -1):
            k.box(O*sx, H/2, O*sz, 0.5, H, 0.5, "LP_White")                         # 隅の柱型
    for s in (1, -1):                                                              # 内周の壁（中央の塊の面）
        k.box(0, H/2, s*(I-0.075), I*2, H, 0.15, "LP_White")
        k.box(s*(I-0.075), H/2, 0, 0.15, H, I*2, "LP_White")
    of = O - 0.075

    def outer(y0, y1, dep, m):
        k.span(-of, of, y0, y1, of-dep, of, m); k.span(-of, of, y0, y1, -of, -of+dep, m)
        k.span(of-dep, of, y0, y1, -of, of, m); k.span(-of, -of+dep, y0, y1, -of, of, m)

    def inner(y0, y1, dep, m, skip_doors=True):
        # 内周4面。扉ユニット（±0.54）を避けて区間に分ける
        cuts = sorted(DOOR_S) if skip_doors else []
        segs, a = [], -I
        for c in cuts:
            segs.append((a, c-0.58)); a = c+0.58
        segs.append((a, I))
        for s0, s1 in segs:
            if s1 - s0 < 0.01: continue
            k.span(s0, s1, y0, y1, I, I+dep, m); k.span(s0, s1, y0, y1, -I-dep, -I, m)
            k.span(I, I+dep, y0, y1, s0, s1, m); k.span(-I-dep, -I, y0, y1, s0, s1, m)

    outer(0, 0.12, 0.018, "LP_DoorFrame"); inner(0, 0.12, 0.018, "LP_DoorFrame")        # 巾木
    outer(0.12, 0.88, 0.01, "LP_CorridorWainscot")                                      # 外周の腰壁
    outer(0.88, 0.94, 0.03, "LP_DoorFrame"); inner(0.88, 0.94, 0.03, "LP_DoorFrame")    # 腰の見切り
    outer(H-0.07, H, 0.025, "LP_DoorFrame"); inner(H-0.07, H, 0.025, "LP_DoorFrame", False)  # 回り縁
    outer(H-0.04, H, 0.045, "LP_DoorFrame"); inner(H-0.04, H, 0.045, "LP_DoorFrame", False)
    # 外周の腰壁の束（向かいの扉と扉の間に合わせて並べる）
    ticks = sorted(set([round(c + d, 3) for c in DOOR_S for d in (-0.682, 0.682)] + [-8.18, 8.18, -9.55, 9.55]))
    for s in ticks:
        if abs(s) > of - 0.05: continue
        for (x0, x1, z0, z1) in ((s-0.025, s+0.025, of-0.022, of), (s-0.025, s+0.025, -of, -of+0.022),
                                 (of-0.022, of, s-0.025, s+0.025), (-of, -of+0.022, s-0.025, s+0.025)):
            k.span(x0, x1, 0.12, 0.88, z0, z1, "LP_DoorFrame")
    # 天井の照明器具（ビルダーの点光源の真上）
    m = 8.5
    for (cx, cz, lx, lz) in ((0, m, 1.2, 0.4), (0, -m, 1.2, 0.4), (m, 0, 0.4, 1.2), (-m, 0, 0.4, 1.2),
                             (m*0.98, m*0.98, 0.6, 0.6), (-m*0.98, m*0.98, 0.6, 0.6),
                             (m*0.98, -m*0.98, 0.6, 0.6), (-m*0.98, -m*0.98, 0.6, 0.6)):
        k.box(cx, H-0.02, cz, lx+0.1, 0.04, lz+0.1, "LP_DoorFrame")
        k.box(cx, H-0.035, cz, lx, 0.03, lz, "LP_CorridorLamp")
    return k

def corridor_door():
    """回廊の扉ユニット（ビルダーの Panel/Jamb/Lintel/Knob と同じ位置。+Z=回廊側）"""
    k = Kit("CorridorDoor")
    z = 0.09
    k.span(-0.54, -0.46, 0, 2.2, z-0.05, z+0.05, "LP_DoorFrame"); k.span(0.46, 0.54, 0, 2.2, z-0.05, z+0.05, "LP_DoorFrame")
    k.span(-0.54, 0.54, 2.2, 2.29, z-0.05, z+0.05, "LP_DoorFrame")
    k.span(-0.6, -0.54, 0, 2.29, z-0.03, z+0.065, "LP_DoorFrame"); k.span(0.54, 0.6, 0, 2.29, z-0.03, z+0.065, "LP_DoorFrame")  # 額縁
    k.span(-0.6, 0.6, 2.29, 2.35, z-0.03, z+0.065, "LP_DoorFrame")
    k.span(-0.46, 0.46, 0, 2.1, z-0.03, z+0.03, "LP_Door")                                  # 扉板
    fz = z + 0.03
    for (x0, x1, y0, y1) in ((-0.36, -0.04, 1.2, 1.95), (0.04, 0.36, 1.2, 1.95), (-0.36, -0.04, 0.2, 1.05), (0.04, 0.36, 0.2, 1.05)):
        k.span(x0, x1, y0, y1, fz, fz+0.008, "LP_DoorFrame")                                  # 鏡板の縁
        k.span(x0+0.03, x1-0.03, y0+0.03, y1-0.03, fz, fz+0.016, "LP_Door")                   # 鏡板
    k.cyl((0.33, 1.02, fz), (0.33, 1.02, fz+0.012), 0.035, "LP_Brass")                         # 座金
    k.span(0.24, 0.35, 1.01, 1.035, fz+0.03, fz+0.05, "LP_Brass"); k.cyl((0.33, 1.02, fz), (0.33, 1.02, fz+0.045), 0.012, "LP_Brass")
    k.span(0.315, 0.345, 0.9, 0.95, fz, fz+0.006, "LP_Brass")                                 # 鍵穴の座
    k.span(-0.44, 0.44, 0.02, 0.2, fz, fz+0.004, "LP_Brass")                                  # 蹴り板
    k.span(-0.46, 0.46, 2.1, 2.2, 0.06, 0.12, "LP_DoorFrame")        # 欄間（扉板の上端2.1〜枠の下端2.2の隙間を塞ぐ）
    k.span(-0.46, 0.46, 2.095, 2.105, 0.12, 0.13, "LP_DoorFrame")    # 扉との見切り
    return k

def room_door(style):
    """部屋側の扉板（ビルダーの Panel: 0.92x2.1x0.08, 中心 y=1.05。+Z=室内）"""
    k = Kit("RoomDoor_" + style)
    leaf = {"facility": "LP_FacilityDoor", "core": "LP_CoreDoor", "home": "LP_HomeDoor", "plain": "LP_Door", "train": "LP_TrainDoor"}[style]
    k.span(-0.46, 0.46, 0, 2.1, -0.04, 0.04, leaf)
    f = 0.04
    if style == "facility":
        k.span(0.12, 0.3, 1.25, 1.85, f, f+0.006, "LP_FacilityTrim"); k.span(0.14, 0.28, 1.27, 1.83, f+0.002, f+0.008, "LP_DoorGlass")
        k.span(-0.44, 0.44, 0.02, 0.28, f, f+0.004, "LP_Metal")                                 # 蹴り板
        k.span(0.26, 0.4, 1.0, 1.03, f+0.03, f+0.05, "LP_Metal"); k.cyl((0.35, 1.015, f), (0.35, 1.015, f+0.05), 0.012, "LP_Metal")
        k.span(-0.3, 0.3, 1.9, 1.97, f, f+0.005, "LP_FacilityTrim")                             # 表示板の台
    elif style == "core":
        for y in (0.35, 0.75, 1.15, 1.55, 1.95):
            k.span(-0.42, 0.42, y-0.03, y+0.03, f, f+0.02, "LP_CoreTrim")                      # 補強リブ
        k.span(-0.44, 0.44, 0.04, 0.16, f, f+0.006, "LP_HazardYellow")
        k.span(0.3, 0.36, 0.8, 1.3, f+0.02, f+0.08, "LP_Metal"); k.span(0.3, 0.36, 0.8, 0.84, f, f+0.08, "LP_Metal")
        k.span(0.3, 0.36, 1.26, 1.3, f, f+0.08, "LP_Metal")
        for y in (0.3, 1.8):
            k.cyl((-0.44, y, 0), (-0.44, y+0.14, 0), 0.03, "LP_CoreTrim")                        # 丁番
    elif style == "train":
        k.span(-0.3, 0.3, 1.2, 1.9, f, f+0.006, "LP_TrainChrome"); k.span(-0.27, 0.27, 1.23, 1.87, f+0.002, f+0.008, "LP_DoorGlass")
        k.span(0.26, 0.4, 0.95, 0.98, f+0.02, f+0.04, "LP_TrainChrome")
    else:   # home / plain：框と鏡板の木の扉
        frame = "LP_HomeTrim" if style == "home" else "LP_DoorFrame"
        for (x0, x1, y0, y1) in ((-0.36, 0.36, 1.15, 1.95), (-0.36, 0.36, 0.2, 1.0)):
            k.span(x0, x1, y0, y1, f, f+0.008, frame); k.span(x0+0.03, x1-0.03, y0+0.03, y1-0.03, f, f+0.016, leaf)
        k.cyl((0.33, 1.02, f), (0.33, 1.02, f+0.012), 0.035, "LP_Brass"); k.cyl((0.33, 1.02, f), (0.33, 1.02, f+0.06), 0.028, "LP_Brass", r1=0.022)
    return k

def breaker():
    """ブレイカー盤（ビルダーの Body: 0.25x0.8x0.5 中心 y=1.35。前面=-X。レバーは別の箱のまま）"""
    k = Kit("BreakerBox")
    k.span(-0.125, 0.125, 0.95, 1.75, -0.25, 0.25, "LP_Breaker")
    k.span(0.125, 0.275, 1.0, 1.7, -0.2, 0.2, "LP_CoreTrim")                                  # 壁への取付台
    k.span(-0.135, -0.125, 0.98, 1.72, -0.23, 0.23, "LP_Breaker")                             # 扉
    k.span(-0.14, -0.135, 1.58, 1.68, -0.15, 0.15, "LP_HazardYellow")                          # 注意表示
    k.span(-0.14, -0.135, 1.1, 1.5, 0.08, 0.2, "LP_DoorGlass")                                # 覗き窓
    k.span(-0.15, -0.135, 1.3, 1.4, -0.22, -0.2, "LP_Metal")                                  # 取っ手
    k.cyl((0, 1.75, 0.15), (0, 2.35, 0.15), 0.025, "LP_Metal")                               # 上へ伸びる配管
    k.cyl((0, 1.75, -0.15), (0, 2.35, -0.15), 0.025, "LP_Metal")
    return k
