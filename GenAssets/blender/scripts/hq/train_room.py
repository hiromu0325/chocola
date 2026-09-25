"""
電車車内（train：幅3 x 奥行18 x 天井3）を品質重視で作る。
配置はビルダー（LoopPrototypeBuilder.FurnishTrain）と同じ計算で決める。

  Shell      部屋の原点。床・壁（窓と側扉の開口つき）・丸い天井・妻面・側扉・窓・照明・冷房吹出口・扇風機
  Interior   部屋の原点。ロングシート・袖仕切り・握り棒・網棚・吊革・窓上広告・中吊り（読めない広告）・路線図
  AdPanel    調べられる中吊り広告の見た目（パネルの中心が原点、表裏とも +Z/-Z に向く）
  Door       車端の赤い扉（RoomDoor ユニットの原点。部屋側は +Z）
  Breaker    車内の配電盤（BuildBreaker ユニットの原点。前面は -X）
  Lever      配電盤のレバー（レバーの中心が原点）
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

import hq
from hq import U, span, lathe, cyl, cyl_between, pipe, quad, plate_xy, profile_z, frame_ring, rrect_face, finish, join

W, D, H = 3.0, 18.0, 3.0
HW0, HD = W / 2, D / 2
HW = HW0 - 0.075                    # 側壁の内面
END_Z = HD - 0.5                    # 妻面（ビルダーの EndPanel の中心）
END_FACE = END_Z - 0.05             # 妻面の室内側の面
OPEN_W = 1.15                       # 妻面の通路の幅
DOOR_Z, DOOR_W = 4.5, 1.3           # 側扉
END_CLEAR = 1.5
SEAT_D = 0.55
RAIL_Y, RACK_Y = 2.35, 1.95
WALL_TOP = 2.45                     # 側壁のまっすぐな部分の上端（その上は丸い天井）
CEIL_FLAT = 0.62                    # 天井の平らな帯の半幅
WIN_Y0, WIN_Y1 = 1.13, 2.11         # 窓枠の外形（ビルダーの WindowFrame と同じ）
BREAKER_GAP = 0.5


def mats():
    M = {}
    M["mel"] = hq.mat("TRN_Melamine", (1, 1, 1), 0.5, tex="melamine.png")
    M["floor"] = hq.mat("TRN_Linoleum", (1, 1, 1), 0.6, tex="linoleum.png")
    M["moq"] = hq.mat("TRN_Moquette", (1, 1, 1), 0.9, tex="moquette.png")
    M["sus"] = hq.mat("TRN_Stainless", (1, 1, 1), 0.3, 1.0, tex="stainless.png")
    M["alu"] = hq.mat("TRN_Aluminum", (0.78, 0.79, 0.8), 0.35, 1.0)
    M["glass"] = hq.mat("TRN_Glass", (0.02, 0.025, 0.03), 0.05)
    M["rubber"] = hq.mat("TRN_Rubber", (0.04, 0.04, 0.04), 0.7)
    M["grille"] = hq.mat("TRN_DarkGrille", (0.12, 0.12, 0.13), 0.5, 0.6)
    M["cover"] = hq.mat("TRN_LightCover", (0.95, 0.95, 0.92), 0.3, emit=(0.92, 0.96, 1.0), emit_strength=3.0)
    M["red"] = hq.mat("TRN_DoorRed", (0.55, 0.07, 0.07), 0.35)
    M["strap"] = hq.mat("TRN_StrapWhite", (0.9, 0.89, 0.84), 0.4)
    M["belt"] = hq.mat("TRN_StrapBelt", (0.75, 0.74, 0.7), 0.7)
    M["net"] = hq.mat("TRN_Net", (1, 1, 1), 0.4, 0.8, tex="rack_net.png", tex_alpha=True)
    M["fan"] = hq.mat("TRN_FanCream", (0.82, 0.8, 0.72), 0.45)
    M["hazard"] = hq.mat("TRN_Hazard", (0.9, 0.72, 0.1), 0.5)
    M["lever"] = hq.mat("TRN_LeverRed", (0.75, 0.12, 0.1), 0.45)
    for key, tex in (("adLab", "../../../project/Assets/Arts/Generated/ad_poster_jp.png"),
                     ("adEik", "ad_eikaiwa.png"), ("adTrv", "ad_travel.png"), ("adMed", "ad_medicine.png"),
                     ("sideRe", "side_realestate.png"), ("sideCl", "side_clinic.png"),
                     ("route", "route_map.png"), ("sticker", "door_sticker.png"),
                     ("carno", "car_number.png"), ("prio", "priority.png")):
        name = {"adLab": "TRN_AdLab", "adEik": "TRN_AdEikaiwa", "adTrv": "TRN_AdTravel", "adMed": "TRN_AdMedicine",
                "sideRe": "TRN_SideRealEstate", "sideCl": "TRN_SideClinic", "route": "TRN_RouteMap",
                "sticker": "TRN_DoorSticker", "carno": "TRN_CarNumber", "prio": "TRN_Priority"}[key]
        M[key] = hq.mat(name, (1, 1, 1), 0.6, tex=tex)
    return M


# ============================== 配置（ビルダーと同じ計算） ==============================

def segments(sx):
    plain = [(-HD + END_CLEAR, -DOOR_Z - DOOR_W / 2 - 0.35),
             (-DOOR_Z + DOOR_W / 2 + 0.35, DOOR_Z - DOOR_W / 2 - 0.35),
             (DOOR_Z + DOOR_W / 2 + 0.35, HD - END_CLEAR)]
    if sx < 0:
        return plain
    c0, c1 = plain[1]
    return [plain[0], (c0, -BREAKER_GAP), (BREAKER_GAP, c1), plain[2]]


def windows(z0, z1):
    ln = z1 - z0
    n = max(1, round(ln / 1.9))
    w = (ln - 0.25 * (n + 1)) / n
    return [(z0 + 0.25 + w / 2 + i * (w + 0.25), w) for i in range(n)]


def wall_openings(sx):
    """側壁の開口（z0, z1, y0, y1）：窓と側扉。端の区間にも小窓を1枚ずつ"""
    ops = []
    for z0, z1 in segments(sx):
        if z1 - z0 < 0.8:
            continue
        for wz, ww in windows(z0, z1):
            ops.append((wz - (ww + 0.08) / 2, wz + (ww + 0.08) / 2, WIN_Y0, WIN_Y1))
    for dz in (-DOOR_Z, DOOR_Z):
        ops.append((dz - DOOR_W / 2, dz + DOOR_W / 2, 0.0, 2.0))
    for zs in (-1, 1):                                                 # 妻面寄りの小窓
        zc = zs * (HD - END_CLEAR / 2 - 0.3)
        ops.append((zc - 0.38, zc + 0.38, WIN_Y0, WIN_Y1))
    return ops


def end_windows():
    pw = HW0 - OPEN_W / 2
    return [(xs * (OPEN_W / 2 + pw / 2), max(0.3, pw - 0.3)) for xs in (-1, 1)]


def ceil_profile(n=10):
    """天井の断面（西の壁の上端 → 平らな帯 → 東の壁の上端）。四分の一楕円で丸める"""
    a, b = HW - CEIL_FLAT, H - WALL_TOP
    pts = []
    for i in range(n + 1):                         # 西側：(-HW, WALL_TOP) → (-CEIL_FLAT, H)
        t = math.pi / 2 * i / n
        pts.append((-CEIL_FLAT - a * math.cos(t), WALL_TOP + b * math.sin(t)))
    for i in range(n + 1):                         # 東側：(CEIL_FLAT, H) → (HW, WALL_TOP)
        t = math.pi / 2 * (n - i) / n
        pts.append((CEIL_FLAT + a * math.cos(t), WALL_TOP + b * math.sin(t)))
    return pts


def ceil_point(t, sx):
    """天井の曲面上の点と内向きの法線（t=0 壁側, 1=平らな帯側）"""
    a, b = HW - CEIL_FLAT, H - WALL_TOP
    ang = math.pi / 2 * t
    x = sx * (CEIL_FLAT + a * math.cos(ang)); y = WALL_TOP + b * math.sin(ang)
    nx, ny = sx * math.cos(ang) / a, math.sin(ang) / b
    ln = math.hypot(nx, ny)
    return (x, y), (-nx / ln, -ny / ln)


# ============================== 外殻 ==============================

def grid_wall(name, openings, u0, u1, v0, v1, to_box, material):
    """開口を避けて壁を箱で埋める。openings=[(u0,u1,v0,v1)]、to_box(ua,ub,va,vb) → span 引数"""
    us = sorted({u0, u1, *[o[0] for o in openings], *[o[1] for o in openings]})
    vs = sorted({v0, v1, *[o[2] for o in openings], *[o[3] for o in openings]})
    us = [u for u in us if u0 <= u <= u1]; vs = [v for v in vs if v0 <= v <= v1]
    out = []
    for i in range(len(us) - 1):
        for j in range(len(vs) - 1):
            cu, cv = (us[i] + us[i + 1]) / 2, (vs[j] + vs[j + 1]) / 2
            if any(o[0] < cu < o[1] and o[2] < cv < o[3] for o in openings):
                continue
            out.append(span(f"{name}_{i}_{j}", *to_box(us[i], us[i + 1], vs[j], vs[j + 1]), material, bev=0))
    return out


def shell(M):
    out = []
    fl = span("TS_Floor", -HW0, HW0, -0.12, 0.0, -HD, HD, M["floor"], bev=0)
    out += [finish(fl, 1.0)]
    walls = []
    for sx in (-1, 1):
        walls += grid_wall(f"TS_Wall{sx}", wall_openings(sx), -END_FACE, END_FACE, 0.0, WALL_TOP,
                           lambda a, b, c, d, sx=sx: (sx * HW, sx * HW0, c, d, a, b), M["mel"])
    for zs in (-1, 1):
        ops = [(-OPEN_W / 2, OPEN_W / 2, 0.0, 2.12)] + [(x - w / 2, x + w / 2, 1.25, 1.95) for x, w in end_windows()]
        walls += grid_wall(f"TS_End{zs}", ops, -HW, HW, 0.0, H,
                           lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * END_FACE, zs * (END_FACE + 0.1)))), M["mel"])
    for o in walls:
        finish(o, 1.0, angle=30)
    out += walls
    # 丸い天井（化粧板）
    ce = profile_z("TS_Ceiling", ceil_profile(12), -END_FACE, END_FACE, M["mel"], closed=False)
    hq.face_toward(ce, (0, -1, 0))
    finish(ce, 1.0, angle=25)
    out.append(ce)
    # 腰の見切り（窓下の細いステンレス）と天井との見切り
    trims = []
    for sx in (-1, 1):
        for (z0, z1, y0, y1) in [(-END_FACE, END_FACE, 2.44, 2.46)]:
            trims.append(span(f"TS_TopTrim{sx}", sx * (HW - 0.012), sx * HW, y0, y1, z0, z1, M["sus"], 0.002))
    out += [finish(t, 2.0) for t in trims]
    out += windows_and_doors(M)
    out += end_details(M)
    out += ceiling_fixtures(M)
    return out


def window_unit(name, sx, zc, w, M, y0=WIN_Y0, y1=WIN_Y1, sash=True):
    """角丸の窓：アルミの額縁（外形は開口）＋奥のガラス＋上下の仕切り"""
    h = y1 - y0
    yc = (y0 + y1) / 2

    def to3d(u, v, d):
        return (sx * (HW + d), yc + v, zc + u)
    parts = [frame_ring(name + "_F", to3d, w, h, 0.02, w - 0.1, h - 0.1, 0.07, -0.012, 0.05, M["alu"])]
    g = rrect_face(name + "_G", to3d, w - 0.09, h - 0.09, 0.065, 0.045, M["glass"])
    hq.face_toward(g, (-sx, 0, 0))
    parts.append(g)
    if sash:
        sy = y0 + h * 0.66
        parts.append(span(name + "_S", *sorted((sx * (HW - 0.004), sx * (HW + 0.045))), sy - 0.018, sy + 0.018,
                          zc - (w - 0.1) / 2, zc + (w - 0.1) / 2, M["alu"], 0.004))
    for p in parts:
        finish(p, 2.0, angle=35)
    return parts


def windows_and_doors(M):
    out = []
    for sx in (-1, 1):
        k = 0
        for z0, z1, y0, y1 in wall_openings(sx):
            if y0 > 0.5:                                        # 窓
                zc, w = (z0 + z1) / 2, z1 - z0
                out += window_unit(f"TS_Win{sx}_{k}", sx, zc, w, M)
                if abs(zc) > 5.5 and abs(zc) < 7.5:                 # 端の区間は優先席
                    xg = sx * (HW + 0.044)
                    zz = zc + sx * (w / 2 - 0.2)
                    pr = quad(f"TS_Prio{sx}_{k}", [(xg, 1.85, zz + 0.07 * sx), (xg, 1.85, zz - 0.07 * sx),
                                                  (xg, 1.99, zz - 0.07 * sx), (xg, 1.99, zz + 0.07 * sx)], M["prio"])
                    hq.face_toward(pr, (-sx, 0, 0)); out.append(pr)
                k += 1
        for dz in (-DOOR_Z, DOOR_Z):
            out += side_door(f"TS_Door{sx}_{dz:+.0f}", sx, dz, M)
    return out


def side_door(name, sx, dz, M):
    """側扉：両開きのステンレス扉（角丸の窓・ゴム・注意ステッカー）、戸袋の枠、床の敷居"""
    p = []
    xf = sx * (HW + 0.012)                                      # 扉の室内側の面
    for i, zs in enumerate((-1, 1)):
        za, zb = sorted((dz + zs * 0.004, dz + zs * DOOR_W / 2))
        wzc = dz + zs * 0.325
        # 窓の穴を開けた扉板（ゴム枠の外形 0.44 x 0.77）
        p += grid_wall(f"{name}_Leaf{i}", [(wzc - 0.22, wzc + 0.22, 1.45 - 0.385, 1.45 + 0.385)], za, zb, 0.01, 1.99,
                       lambda a, b, c, d: (xf, xf + sx * 0.03, c, d, a, b), M["sus"])

        def to3d(u, v, d, wzc=wzc):
            return (sx * (HW + 0.012 + d), 1.45 + v, wzc + u)
        p.append(frame_ring(f"{name}_Gasket{i}", to3d, 0.44, 0.77, 0.01, 0.40, 0.73, 0.07, -0.004, 0.03, M["rubber"]))
        g = rrect_face(f"{name}_Glass{i}", to3d, 0.405, 0.735, 0.07, 0.015, M["glass"])
        hq.face_toward(g, (-sx, 0, 0)); p.append(g)
        # 室内から見て左→右 に U が増えるよう、始点は +sx 側
        xs_ = xf - sx * 0.001
        st = quad(f"{name}_Sticker{i}", [(xs_, 0.92, wzc + 0.06 * sx), (xs_, 0.92, wzc - 0.06 * sx),
                                        (xs_, 1.04, wzc - 0.06 * sx), (xs_, 1.04, wzc + 0.06 * sx)], M["sticker"])
        hq.face_toward(st, (-sx, 0, 0)); p.append(st)
    p.append(span(f"{name}_Seam", *sorted((xf - sx * 0.006, xf + sx * 0.03)), 0.01, 1.99, dz - 0.006, dz + 0.006, M["rubber"], 0.003))
    # 戸袋の枠（開口の両脇と上）
    for zs in (-1, 1):
        ze = dz + zs * DOOR_W / 2
        p.append(span(f"{name}_Jamb{zs}", *sorted((sx * (HW - 0.01), sx * (HW + 0.05))), 0.0, 2.02,
                      *sorted((ze, ze + zs * 0.045)), M["sus"], 0.004))
    p.append(span(f"{name}_Head", *sorted((sx * (HW - 0.01), sx * (HW + 0.05))), 2.0, 2.05,
                  dz - DOOR_W / 2 - 0.045, dz + DOOR_W / 2 + 0.045, M["sus"], 0.004))
    # 敷居（溝付きのアルミ）
    p.append(span(f"{name}_Sill", *sorted((sx * (HW - 0.12), sx * (HW + 0.02))), 0.0, 0.006,
                  dz - DOOR_W / 2, dz + DOOR_W / 2, M["alu"], 0.002))
    for g in range(4):
        gx = sx * (HW - 0.1 + g * 0.03)
        p.append(span(f"{name}_SillG{g}", gx - 0.003, gx + 0.003, 0.0055, 0.0075, dz - DOOR_W / 2, dz + DOOR_W / 2, M["rubber"], 0))
    for o in p:
        finish(o, 2.0, angle=35, keep_uv=o.name.startswith((f"{name}_Glass", f"{name}_Sticker")))
    return p


def end_details(M):
    """妻面：通路の枠と奥の壁、妻窓、号車札、非常通報器、握り棒"""
    p = []
    for zs in (-1, 1):
        zf = zs * END_FACE
        zb = zs * (HD - 0.06)                                   # 扉の手前まで
        # 通路（奥行 END_FACE〜扉）の両脇と上をステンレスで張る
        for xs in (-1, 1):
            p.append(span(f"TE_Side{zs}{xs}", *sorted((xs * OPEN_W / 2, xs * (OPEN_W / 2 + 0.02))), 0.0, 2.12,
                          *sorted((zf, zb)), M["sus"], 0.003))
            # 扉の脇の隙間（扉板 ±0.46 と通路 ±0.575 の間）
            p.append(span(f"TE_Fill{zs}{xs}", *sorted((xs * 0.46, xs * OPEN_W / 2)), 0.0, 2.12,
                          *sorted((zs * (HD - 0.14), zb)), M["sus"], 0.003))
        p.append(span(f"TE_Top{zs}", -OPEN_W / 2, OPEN_W / 2, 2.1, 2.14, *sorted((zf, zb)), M["sus"], 0.003))
        p.append(span(f"TE_Tread{zs}", -OPEN_W / 2, OPEN_W / 2, 0.0, 0.006, *sorted((zf - zs * 0.02, zb)), M["alu"], 0.002))
        # 通路の額縁
        for xs in (-1, 1):
            p.append(span(f"TE_Casing{zs}{xs}", *sorted((xs * (OPEN_W / 2 - 0.005), xs * (OPEN_W / 2 + 0.05))), 0.0, 2.17,
                          *sorted((zf, zf - zs * 0.012)), M["sus"], 0.003))
        p.append(span(f"TE_CasingT{zs}", -OPEN_W / 2 - 0.05, OPEN_W / 2 + 0.05, 2.12, 2.17,
                      *sorted((zf, zf - zs * 0.012)), M["sus"], 0.003))
        # 妻窓（角丸）
        for i, (xc, w) in enumerate(end_windows()):
            def to3d(u, v, d, xc=xc):
                return (xc + u, 1.6 + v, zs * (END_FACE + d))
            p.append(frame_ring(f"TE_WinF{zs}{i}", to3d, w, 0.7, 0.02, w - 0.1, 0.6, 0.06, -0.012, 0.05, M["alu"]))
            g = rrect_face(f"TE_WinG{zs}{i}", to3d, w - 0.09, 0.61, 0.055, 0.045, M["glass"])
            hq.face_toward(g, (0, 0, -zs)); p.append(g)
        # 号車札（通路の上）と非常通報器（通路の脇）
        zc_ = zf - zs * 0.004
        cn = quad(f"TE_CarNo{zs}", [(-0.3 * zs, 2.3, zc_), (0.3 * zs, 2.3, zc_),
                                    (0.3 * zs, 2.45, zc_), (-0.3 * zs, 2.45, zc_)], M["carno"])
        hq.face_toward(cn, (0, 0, -zs)); p.append(cn)
        p.append(span(f"TE_Intercom{zs}", 0.72, 0.88, 1.3, 1.52, *sorted((zf, zf - zs * 0.035)), M["sus"], 0.006))
        p.append(span(f"TE_IntercomBtn{zs}", 0.77, 0.83, 1.36, 1.42, *sorted((zf - zs * 0.035, zf - zs * 0.045)), M["lever"], 0.004))
        # 通路脇の縦の握り棒（ビルダーの EndPole の位置）
        for xs in (-1, 1):
            x = xs * (OPEN_W / 2 + 0.08)
            z = zs * (END_Z - 0.12)
            p.append(pipe(f"TE_Pole{zs}{xs}", [(x, 0.45, zf), (x, 0.45, z), (x, 2.05, z), (x, 2.05, zf)], 0.015, M["sus"], 0.05))
    for o in p:
        finish(o, 2.0, angle=35, keep_uv=o.name.startswith(("TE_WinG", "TE_CarNo")))
    return p


def ceiling_fixtures(M):
    """天井：両側の蛍光灯（乳白カバー）、中央の冷房吹出口、扇風機"""
    p = []
    zs_tube = []
    z = -HD + 1.6
    while z < HD - 1.0:
        zs_tube.append(z); z += 2.4
    for sx in (-1, 1):
        (cx, cy), (nx, ny) = ceil_point(0.5, sx)
        ang = math.atan2(nx, -ny)                                # (0,-1) を内向き法線へ回す角度
        for i, zc in enumerate(zs_tube):
            base = span(f"TL_Base{sx}_{i}", -0.08, 0.08, -0.01, 0.03, zc - 0.62, zc + 0.62, M["mel"], 0.01)
            cov = profile_z(f"TL_Cover{sx}_{i}", [(0.06 * math.cos(math.pi * k / 10), -0.045 * math.sin(math.pi * k / 10))
                                                  for k in range(11)], zc - 0.6, zc + 0.6, M["cover"])
            caps = [span(f"TL_Cap{sx}_{i}_{e}", -0.065, 0.065, -0.05, 0.0, *sorted((zc + e * 0.6, zc + e * 0.62)), M["mel"], 0.004)
                    for e in (-1, 1)]
            for o in [base, cov] + caps:
                hq.apply_mods(o)
                # ローカル（下向き）で作ったものを曲面の傾きに合わせて回し、曲面の点へ
                o.data.transform(Matrix.Translation(U(cx + nx * 0.005, cy + ny * 0.005, 0) - U(0, 0, 0))
                                 @ Matrix.Rotation(ang, 4, "Y"))
            p += [base, cov] + caps
    # 冷房吹出口（中央の平らな帯）：暗い枠に細い羽根
    vz = -HD + 2.8
    k = 0
    while vz < HD - 1.0:
        p.append(span(f"TA_Frame{k}", -0.32, 0.32, H - 0.02, H, vz - 0.65, vz + 0.65, M["mel"], 0.006))
        p.append(span(f"TA_Hole{k}", -0.27, 0.27, H - 0.025, H - 0.02, vz - 0.6, vz + 0.6, M["grille"], 0))
        for s in range(14):
            x = -0.26 + s * 0.04
            p.append(span(f"TA_Vane{k}_{s}", x - 0.006, x + 0.006, H - 0.05, H - 0.025, vz - 0.58, vz + 0.58, M["alu"], 0.002))
        vz += 4.8; k += 1
    # 中央の見切り（天井の平らな帯の縁）
    for sx in (-1, 1):
        p.append(span(f"TA_Edge{sx}", sx * CEIL_FLAT - 0.01, sx * CEIL_FLAT + 0.01, H - 0.012, H, -END_FACE, END_FACE, M["sus"], 0.002))
    # 扇風機（広告・吹出口の間）
    for i, fz in enumerate((-3.8, 1.0, 5.2)):
        p += ceiling_fan(f"TF{i}", fz, M)
    for o in p:
        finish(o, 2.0, angle=40)
    return p


def ceiling_fan(name, fz, M):
    p = []
    p.append(lathe(name + "_Mount", (0, H - 0.03, fz), [(0.085, 0), (0.085, 0.02), (0.06, 0.03)], M["fan"], 32))
    p.append(cyl(name + "_Rod", (0, H - 0.16, fz), 0.14, 0.012, M["fan"], 16))
    p.append(lathe(name + "_Motor", (0, H - 0.25, fz), [(0.0, 0.0), (0.05, 0.005), (0.085, 0.03), (0.09, 0.06),
                                                         (0.06, 0.09), (0.02, 0.095)], M["fan"], 32))
    # 羽根4枚（わずかに傾けた丸い板）
    for k in range(4):
        a = math.pi / 2 * k + 0.3
        pts = []
        for s in range(12):
            t = s / 11
            r = 0.08 + 0.22 * t
            w = 0.05 + 0.04 * math.sin(math.pi * t)
            pts.append((r, w))
        poly = [(r, w) for r, w in pts] + [(r, -w) for r, w in reversed(pts)]
        me = bpy.data.meshes.new(f"{name}_Blade{k}")
        bm = bmesh.new()
        vs = []
        for (r, w) in poly:
            x = r * math.cos(a) - w * math.sin(a)
            z = r * math.sin(a) + w * math.cos(a)
            tilt = 0.012 * (w / 0.09)
            vs.append(bm.verts.new(U(x, H - 0.22 + tilt, fz + z)))
        f = bm.faces.new(vs)
        bm.to_mesh(me); bm.free()
        b = hq._obj(f"{name}_Blade{k}", me, M["fan"])
        sol = b.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.004
        p.append(b)
    # 安全ガード（リングと放射の針金）
    bpy.ops.mesh.primitive_torus_add(major_radius=0.33, minor_radius=0.004, major_segments=48, minor_segments=6,
                                     location=U(0, H - 0.22, fz))
    ring = bpy.context.active_object; ring.name = name + "_Guard"; ring.data.materials.append(M["fan"])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    p.append(ring)
    for k in range(8):
        a = math.pi / 4 * k
        p.append(cyl_between(f"{name}_Spoke{k}", (0, H - 0.2, fz), (0.33 * math.cos(a), H - 0.22, fz + 0.33 * math.sin(a)),
                             0.0025, M["fan"], 6))
    return p


# ============================== 座席まわり ==============================

SEAT_PROF = [(0.0, 0.33), (-0.012, 0.36), (-0.02, 0.41), (-0.018, 0.45), (-0.008, 0.472), (0.015, 0.484),
             (0.06, 0.487), (0.45, 0.476), (0.5, 0.47), (0.5, 0.33)]
BACK_PROF = [(0.0, 0.49), (0.012, 0.7), (0.03, 0.92), (0.045, 1.05), (0.062, 1.1), (0.085, 1.117),
             (0.11, 1.11), (0.125, 1.09), (0.125, 0.49)]


def _side(prof, sx, x_front):
    return [(sx * (x_front + dx), y) for dx, y in prof]


def interior(M):
    out = []
    for sx in (-1, 1):
        x_front = HW - SEAT_D - 0.05 + 0.0                     # 座面の前縁（ビルダー：座面の中心 = HW-0.325）
        for si, (z0, z1) in enumerate(segments(sx)):
            if z1 - z0 < 0.8:
                continue
            n = f"{sx}_{si}"
            za, zb = z0 + 0.06, z1 - 0.06
            seat = profile_z(f"TI_Seat{n}", _side(SEAT_PROF, sx, x_front + 0.01), za, zb, M["moq"])
            back = profile_z(f"TI_Back{n}", _side(BACK_PROF, sx, HW - 0.14), za, zb, M["moq"])
            # 座面の下：暗い奥＋ヒーターの羽根板、足元の蹴込み
            under = span(f"TI_Under{n}", *sorted((sx * (x_front + 0.05), sx * HW)), 0.0, 0.33, za, zb, M["grille"], 0)
            kick = span(f"TI_Kick{n}", *sorted((sx * (x_front + 0.02), sx * (x_front + 0.05))), 0.0, 0.06, za, zb, M["sus"], 0.003)
            parts = [seat, back, under, kick]
            for s in range(7):
                y = 0.09 + s * 0.032
                parts.append(span(f"TI_Louver{n}_{s}", *sorted((sx * (x_front + 0.028), sx * (x_front + 0.04))), y, y + 0.012,
                                  za + 0.05, zb - 0.05, M["sus"], 0.002))
            for o in parts:
                finish(o, 2.0 if o.name.startswith(("TI_Seat", "TI_Back")) else 1.0, rot90=True, angle=40)
            out += parts
            out += sleeves(n, sx, z0, z1, x_front, M)
            out += rack(n, sx, z0, z1, M)
            out += straps(n, sx, z0, z1, M)
            out += side_posters(n, sx, z0, z1, M)
        for dz in (-DOOR_Z, DOOR_Z):
            rm = quad(f"TI_Route{sx}_{dz:+.0f}", [(sx * (HW - 0.004), 2.12, dz + 0.55 * sx), (sx * (HW - 0.004), 2.12, dz - 0.55 * sx),
                                                 (sx * (HW - 0.004), 2.4, dz - 0.55 * sx), (sx * (HW - 0.004), 2.4, dz + 0.55 * sx)],
                      M["route"])
            hq.face_toward(rm, (-sx, 0, 0)); out.append(rm)
            out.append(finish(span(f"TI_RouteF{sx}_{dz:+.0f}", *sorted((sx * (HW - 0.003), sx * HW)), 2.11, 2.41,
                                   dz - 0.56, dz + 0.56, M["alu"], 0.002), 2.0))
    out += hanging_ads(M)
    return out


def sleeves(n, sx, z0, z1, x_front, M):
    """袖仕切り：化粧板の板をステンレスのパイプが縁取る＋上へ伸びる握り棒"""
    p = []
    xp = x_front - 0.02                                         # パイプの位置（座面の前縁より少し前）
    for e, ze in enumerate((z0, z1)):
        zs = ze + (0.03 if e == 0 else -0.03)
        prof = [(sx * HW, 0.36), (sx * (xp + 0.02), 0.36), (sx * (xp + 0.02), 0.95), (sx * (xp + 0.07), 1.05),
                (sx * (xp + 0.16), 1.07), (sx * HW, 1.07)]
        p.append(plate_xy(f"TI_SleeveP{n}_{e}", prof, zs - 0.012, zs + 0.012, M["mel"], 0.004))
        p.append(pipe(f"TI_SleeveT{n}_{e}", [(sx * xp, 0.0, zs), (sx * xp, 1.08, zs), (sx * (HW - 0.01), 1.08, zs)],
                      0.017, M["sus"], 0.1))
        # 座面の高さの肘掛け（パイプ）
        p.append(pipe(f"TI_Arm{n}_{e}", [(sx * xp, 0.6, zs), (sx * (HW - 0.01), 0.6, zs)], 0.012, M["sus"], 0.05))
        # 上への握り棒（ビルダーの Pole：座面の前縁の外、天井まで）
        pz = zs
        top = H - 0.01
        p.append(pipe(f"TI_Pole{n}_{e}", [(sx * xp, 1.08, pz), (sx * xp, 2.05, pz), (sx * (xp + 0.06), 2.25, pz),
                                          (sx * (xp + 0.06), top, pz)], 0.015, M["sus"], 0.12))
        p.append(lathe(f"TI_PoleCap{n}_{e}", (sx * (xp + 0.06), top - 0.015, pz), [(0.03, 0), (0.03, 0.01), (0.018, 0.015)], M["sus"], 16))
    for o in p:
        finish(o, 2.0, angle=40)
    return p


def rack(n, sx, z0, z1, M):
    """網棚：壁の受け金具・前のパイプ・網"""
    p = []
    xf = sx * (HW - 0.4)                                        # 前のパイプ
    xb = sx * (HW - 0.03)
    p.append(pipe(f"TI_RackF{n}", [(xf, RACK_Y + 0.04, z0 + 0.05), (xf, RACK_Y + 0.04, z1 - 0.05)], 0.014, M["sus"]))
    p.append(pipe(f"TI_RackB{n}", [(xb, RACK_Y + 0.06, z0 + 0.05), (xb, RACK_Y + 0.06, z1 - 0.05)], 0.01, M["sus"]))
    bz = z0 + 0.3
    k = 0
    while bz < z1 - 0.2:
        # 受け金具：上の腕・斜めの支え・壁の座金（三角の板にすると暗い楔に見えるので枠にする）
        top = [(sx * HW, RACK_Y + 0.03), (sx * (HW - 0.4), RACK_Y + 0.03), (sx * (HW - 0.4), RACK_Y + 0.062),
               (sx * HW, RACK_Y + 0.07)]
        ax_, ay_ = sx * HW, RACK_Y - 0.135
        bx_, by_ = sx * (HW - 0.37), RACK_Y + 0.04
        dx_, dy_ = bx_ - ax_, by_ - ay_
        ln_ = math.hypot(dx_, dy_); nx_, ny_ = -dy_ / ln_ * 0.014, dx_ / ln_ * 0.014
        brace = [(ax_ - nx_, ay_ - ny_), (bx_ - nx_, by_ - ny_), (bx_ + nx_, by_ + ny_), (ax_ + nx_, ay_ + ny_)]
        p.append(plate_xy(f"TI_RackBrT{n}_{k}", top, bz - 0.008, bz + 0.008, M["alu"], 0.004))
        p.append(plate_xy(f"TI_RackBrD{n}_{k}", brace, bz - 0.007, bz + 0.007, M["alu"], 0.003))
        p.append(span(f"TI_RackBrW{n}_{k}", *sorted((sx * (HW - 0.012), sx * HW)), RACK_Y - 0.18, RACK_Y + 0.09,
                      bz - 0.03, bz + 0.03, M["alu"], 0.004))
        bz += 1.2; k += 1
    net = quad(f"TI_RackNet{n}", [(xf, RACK_Y + 0.035, z0 + 0.06), (xb, RACK_Y + 0.05, z0 + 0.06),
                                  (xb, RACK_Y + 0.05, z1 - 0.06), (xf, RACK_Y + 0.035, z1 - 0.06)], M["net"],
               uv=((0, 0), (3.7, 0), (3.7, (z1 - z0) * 10), (0, (z1 - z0) * 10)))
    hq.face_toward(net, (0, 1, 0))
    for o in p:
        finish(o, 2.0, angle=40)
    p.append(net)
    return p


def straps(n, sx, z0, z1, M):
    p = []
    rx = sx * (HW - 0.85)
    p.append(pipe(f"TI_Rail{n}", [(rx, RAIL_Y, z0 + 0.05), (rx, RAIL_Y, z1 - 0.05)], 0.016, M["sus"]))
    hz = z0 + 0.4
    k = 0
    while hz < z1:
        p.append(cyl_between(f"TI_Hanger{n}_{k}", (rx, RAIL_Y, hz), (rx, H - 0.005, hz), 0.009, M["sus"], 12))
        p.append(lathe(f"TI_HangerCap{n}_{k}", (rx, H - 0.012, hz), [(0.025, 0), (0.025, 0.007), (0.012, 0.012)], M["sus"], 16))
        hz += 1.6; k += 1
    cnt = max(1, int((z1 - z0) / 0.5))
    rings = []
    for k in range(cnt):
        z = z0 + (k + 0.5) * ((z1 - z0) / cnt)
        p.append(span(f"TI_Clip{n}_{k}", rx - 0.022, rx + 0.022, RAIL_Y - 0.03, RAIL_Y + 0.022, z - 0.02, z + 0.02, M["strap"], 0.008))
        p.append(span(f"TI_Belt{n}_{k}", rx - 0.014, rx + 0.014, RAIL_Y - 0.3, RAIL_Y - 0.02, z - 0.003, z + 0.003, M["belt"], 0.002))
        bpy.ops.mesh.primitive_torus_add(major_radius=0.058, minor_radius=0.011, major_segments=32, minor_segments=10,
                                         location=U(rx, RAIL_Y - 0.37, z), rotation=(math.pi / 2, 0, 0))
        r = bpy.context.active_object; r.name = f"TI_Ring{n}_{k}"
        r.data.materials.append(M["strap"])
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
        rings.append(r)
    for o in p:
        finish(o, 2.0, angle=40)
    for r in rings:
        hq.smooth(r, 180)
    return p + rings


def side_posters(n, sx, z0, z1, M):
    p = []
    k = 0
    pz = z0 + 0.6
    while pz < z1 - 0.5:
        m = M["sideRe"] if (k + (1 if sx > 0 else 0)) % 2 == 0 else M["sideCl"]
        xw = sx * (HW - 0.004)
        q = quad(f"TI_Poster{n}_{k}", [(xw, 2.145, pz + 0.225 * sx), (xw, 2.145, pz - 0.225 * sx),
                                        (xw, 2.425, pz - 0.225 * sx), (xw, 2.425, pz + 0.225 * sx)], m)
        hq.face_toward(q, (-sx, 0, 0))
        fr = span(f"TI_PosterF{n}_{k}", *sorted((sx * (HW - 0.003), sx * HW)), 2.135, 2.435, pz - 0.235, pz + 0.235, M["alu"], 0.002)
        finish(fr, 2.0)
        p += [q, fr]
        pz += 1.9; k += 1
    return p


def hanging_ad_geo(name, w, h, m_front, frame_m, wire_top=None):
    """中吊り1枚（中心が原点、表裏とも印刷）。上下にアルミの挟み、上に吊り金具"""
    p = []
    t = 0.004
    q = quad(name + "_Front", [(-w / 2, -h / 2, -t), (w / 2, -h / 2, -t), (w / 2, h / 2, -t), (-w / 2, h / 2, -t)], m_front)
    hq.face_toward(q, (0, 0, -1))
    b = quad(name + "_Back", [(w / 2, -h / 2, t), (-w / 2, -h / 2, t), (-w / 2, h / 2, t), (w / 2, h / 2, t)], m_front)
    hq.face_toward(b, (0, 0, 1))
    p += [q, b]
    for yy in (-h / 2, h / 2):
        p.append(finish(span(name + f"_Clip{yy:+.2f}", -w / 2 - 0.01, w / 2 + 0.01, yy - 0.012, yy + 0.012, -0.009, 0.009, frame_m, 0.004), 2.0))
    if wire_top is not None:
        for xs in (-1, 1):
            p.append(finish(cyl_between(name + f"_Wire{xs}", (xs * (w / 2 - 0.05), h / 2, 0), (xs * (w / 2 - 0.05), wire_top, 0),
                                        0.003, frame_m, 6), 2.0, angle=60))
    return p


def hanging_ads(M):
    """読めない中吊り（ビルダーの DummyAd の位置）"""
    out = []
    ad_h = 0.78
    ad_y = max(H - 0.45, 2.2 + ad_h / 2)
    for i, (z, m) in enumerate(((3.5, M["adEik"]), (-6.0, M["adTrv"]), (6.5, M["adMed"]))):
        parts = hanging_ad_geo(f"TI_Ad{i}", 1.0, 0.6, m, M["alu"], wire_top=H - ad_y)
        for o in parts:
            o.data.transform(Matrix.Translation(U(0, ad_y, z) - U(0, 0, 0)))
        out += parts
    return out


def ad_panel(M):
    """調べられる中吊り広告（Find_ad の子。パネルの中心が原点、1.15 x 0.78）"""
    return hanging_ad_geo("TAd", 1.15, 0.78, M["adLab"], M["alu"], wire_top=0.39 + 0.06)


# ============================== 車端扉・配電盤 ==============================

def door(M):
    """車端の赤い扉（引き戸）：角丸の窓、ゴム、ステンレスの蹴板と取っ手、窓上の札"""
    t = 0.02
    p = []
    # 窓の穴を開けた扉板（ゴム枠の外形 0.44 x 0.9）
    p += grid_wall("TD_Leaf", [(-0.22, 0.22, 1.42 - 0.45, 1.42 + 0.45)], -0.458, 0.458, 0.0, 2.1,
                   lambda a, b, c, d: (a, b, c, d, -t, t), M["red"])
    for zs in (-1, 1):
        def to3d(u, v, d, zs=zs):
            return (u, 1.42 + v, zs * (t + d))
        p.append(frame_ring(f"TD_Gasket{zs}", to3d, 0.44, 0.9, 0.01, 0.39, 0.85, 0.08, -t, 0.006, M["rubber"]))
        g = rrect_face(f"TD_Glass{zs}", to3d, 0.395, 0.855, 0.08, -t + 0.002, M["glass"])
        hq.face_toward(g, (0, 0, zs)); p.append(g)
        p.append(span(f"TD_Kick{zs}", -0.44, 0.44, 0.02, 0.2, *sorted((zs * t, zs * (t + 0.003))), M["sus"], 0.002))
        # 縦長の取っ手（くぼみの縁）
        p.append(span(f"TD_Pull{zs}", 0.33, 0.37, 0.8, 1.25, *sorted((zs * t, zs * (t + 0.006))), M["sus"], 0.004))
        p.append(span(f"TD_PullIn{zs}", 0.338, 0.362, 0.81, 1.24, *sorted((zs * (t + 0.004), zs * (t + 0.0065))), M["rubber"], 0.001))
    for o in p:
        finish(o, 2.0, angle=35, keep_uv=o.name.startswith("TD_Glass"))
    return p


def breaker(M):
    """車内の配電盤（前面 -X、壁は +X 側。本体 x -0.125〜0.125 → 壁に付くよう奥へ）"""
    p = []
    x0, x1 = -0.12, 0.125                                        # 前面〜壁
    p.append(span("TB_Box", x0 + 0.01, x1, 0.95, 1.75, -0.25, 0.25, M["mel"], 0.01, 3))
    p.append(span("TB_Door", x0, x0 + 0.012, 0.98, 1.72, -0.225, 0.225, M["mel"], 0.006))
    # 通風の切り欠き・警告ラベル・蝶番・鍵
    for s in range(6):
        y = 1.02 + s * 0.022
        p.append(span(f"TB_Vent{s}", x0 - 0.001, x0 + 0.004, y, y + 0.01, -0.16, 0.16, M["grille"], 0.001))
    p.append(span("TB_Label", x0 - 0.002, x0 + 0.002, 1.6, 1.68, -0.12, 0.12, M["hazard"], 0.001))
    for y in (1.05, 1.62):
        p.append(cyl_between(f"TB_Hinge{y}", (x0 - 0.004, y - 0.03, -0.228), (x0 - 0.004, y + 0.03, -0.228), 0.006, M["sus"], 12))
    p.append(cyl_between("TB_Lock", (x0, 1.5, 0.19), (x0 - 0.012, 1.5, 0.19), 0.012, M["sus"], 16))
    # レバーの溝（黒い縦長の穴）
    p.append(span("TB_Slot", x0 - 0.002, x0 + 0.004, 1.0, 1.5, -0.035, 0.035, M["rubber"], 0.003))
    # 天井へ上がる電線管
    p.append(pipe("TB_Conduit", [(0.06, 1.75, 0.15), (0.06, 2.65, 0.15)], 0.014, M["sus"], 0.05))
    for o in p:
        finish(o, 2.0, angle=35)
    return p


def lever(M):
    """配電盤のレバー（ビルダーの Lever 箱 0.1 x 0.22 x 0.12 の位置。前 = -X）"""
    p = []
    p.append(span("TLv_Arm", -0.03, 0.05, -0.03, 0.03, -0.022, 0.022, M["sus"], 0.006))
    p.append(span("TLv_Grip", -0.06, -0.02, -0.1, 0.1, -0.035, 0.035, M["lever"], 0.012, 4))
    for o in p:
        finish(o, 2.0, angle=40)
    return p


PIECES = {"Shell": shell, "Interior": interior, "AdPanel": ad_panel, "Door": door, "Breaker": breaker, "Lever": lever}
