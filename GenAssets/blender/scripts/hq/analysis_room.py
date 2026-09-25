"""
第8研究室 脳神経解析室（analysis：幅8 x 奥行9 x 天井3）を品質重視で作る。
白いパネルの壁・青灰のビニル床・配管むき出しの濃い天井（ダクト・ケーブルトレイ・スプリンクラー）・
北壁のモニター群・実験台（脳模型・スキャン用ヘルメット）・施錠キャビネット・ガラスの観察ブース。

  Shell      部屋の原点。床・壁・巾木と腰の保護材・天井と設備・吊り下げ照明・扉の枠・室名札
  Interior   部屋の原点。北壁のモニターラック3台と画面、施錠キャビネット3台、観察ブースと検査椅子
  Table      作業台（台の原点 = ビルダーの WorkTable、天板の上面 y=0.89）と脳模型
  Helmet     スキャン用ヘルメット（箱の中心が原点）
  Anomaly    異常レポートの画面（箱の中心が原点、表示は -Z）
  IdCard / Plan / Spec   資料の見た目（箱の中心が原点）
  Door / Breaker / Lever
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

import hq
from hq import U, span, lathe, cyl, cyl_between, pipe, quad, plate_xy, profile_z, frame_ring, rrect_face, finish, join
import train_room

W, D, H = 8.0, 9.0, 3.0
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
RACK_XS = (-2.2, 0.0, 2.2)
LIGHT_ZS = (-2.25, 2.25)


def mats():
    M = {}
    M["floor"] = hq.mat("ANA_Vinyl", (1, 1, 1), 0.45, tex="vinyl_floor.png")
    M["panel"] = hq.mat("ANA_Panel", (1, 1, 1), 0.5, tex="clean_panel.png")
    M["ceil"] = hq.mat("ANA_ConcreteDark", (1, 1, 1), 0.9, tex="concrete_dark.png")
    M["galv"] = hq.mat("ANA_Galvanized", (1, 1, 1), 0.35, 0.8, tex="galvanized.png")
    M["seam"] = hq.mat("ANA_Seam", (0.55, 0.57, 0.6), 0.5)
    M["bumper"] = hq.mat("ANA_Bumper", (0.46, 0.5, 0.56), 0.4)
    M["cove"] = hq.mat("ANA_Cove", (0.28, 0.3, 0.33), 0.5)
    M["steel"] = hq.mat("ANA_SteelGrey", (0.6, 0.62, 0.64), 0.4, 0.4)
    M["rack"] = hq.mat("ANA_RackBlack", (0.07, 0.075, 0.085), 0.45, 0.4)
    M["alu"] = hq.mat("ANA_Aluminum", (0.8, 0.81, 0.82), 0.3, 1.0)
    M["black"] = hq.mat("ANA_Black", (0.02, 0.02, 0.025), 0.4)
    M["red"] = hq.mat("ANA_SprinklerRed", (0.6, 0.08, 0.06), 0.45)
    M["led"] = hq.mat("ANA_LedPanel", (0.93, 0.95, 1.0), 0.3, emit=(0.9, 0.95, 1.0), emit_strength=3.0)
    M["glass"] = hq.mat("ANA_GlassClear", (0.7, 0.8, 0.85), 0.05, alpha=0.2)
    M["epoxy"] = hq.mat("ANA_Epoxy", (0.08, 0.085, 0.09), 0.25)
    M["brain"] = hq.mat("ANA_Brain", (0.78, 0.6, 0.58), 0.45)
    M["acrylic"] = hq.mat("ANA_Acrylic", (0.85, 0.9, 0.95), 0.05)
    M["leather"] = hq.mat("ANA_Leather", (0.3, 0.22, 0.16), 0.5)
    M["pad"] = hq.mat("ANA_Pad", (0.35, 0.36, 0.38), 0.8)
    M["vinylSeat"] = hq.mat("ANA_SeatVinyl", (0.72, 0.8, 0.82), 0.4)
    M["cable"] = hq.mat("ANA_Cable", (0.05, 0.05, 0.06), 0.6)
    M["cableG"] = hq.mat("ANA_CableGrey", (0.5, 0.52, 0.55), 0.6)
    M["doorGrey"] = hq.mat("ANA_DoorGrey", (0.52, 0.55, 0.58), 0.45, 0.3)
    M["wired"] = hq.mat("ANA_WiredGlass", (0.1, 0.12, 0.13), 0.05)
    for key, tex, name, emit in (
            ("scrConn", "screen_connectome.png", "ANA_ScreenConnectome", True), ("scrCt", "screen_ct.png", "ANA_ScreenCT", True),
            ("scrSpec", "screen_spectrum.png", "ANA_ScreenSpectrum", True), ("scrAnom", "screen_anomaly.png", "ANA_ScreenAnomaly", True),
            ("scrEeg", "../../lab/tex/screen_eeg.png", "ANA_ScreenEEG", True), ("scrMri", "../../lab/tex/screen_mri.png", "ANA_ScreenMRI", True),
            ("card", "id_card.png", "ANA_IdCard", False), ("plan", "doc_plan.png", "ANA_DocPlan", False),
            ("spec", "doc_spec.png", "ANA_DocSpec", False), ("binder", "binders.png", "ANA_Binders", False),
            ("sign", "sign_room.png", "ANA_SignRoom", False), ("caution", "sign_caution.png", "ANA_SignCaution", False)):
        M[key] = hq.mat(name, (1, 1, 1), 0.3, tex=tex, emit=(0.6, 0.7, 0.9) if emit else None, emit_strength=0.6 if emit else 0.0)
    return M


# ============================== 外殻 ==============================

def shell(M):
    out = []
    out.append(finish(span("AS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["floor"], bev=0), 0.5))
    walls = []
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"AS_WallNS{zs}", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["panel"])
    for xs in (-1, 1):
        walls.append(span(f"AS_WallEW{xs}", *sorted((xs * HW, xs * HW0)), 0.0, H, -HD0, HD0, M["panel"], bev=0))
    out += [finish(o, 1.0, angle=30) for o in walls]
    trims = []
    # パネルの目地（1.2m ごと）と、腰の保護材・ソフト巾木
    for xs in (-1, 1):
        z = -HD + 1.2
        while z < HD - 0.3:
            trims.append(span(f"AS_SeamEW{xs}{z:.1f}", *sorted((xs * HW, xs * (HW - 0.004))), 0.1, H, z - 0.004, z + 0.004, M["seam"], 0))
            z += 1.2
        trims.append(span(f"AS_BumpEW{xs}", *sorted((xs * HW, xs * (HW - 0.03))), 0.86, 0.96, -HD, HD, M["bumper"], 0.01, 3))
        trims.append(span(f"AS_CoveEW{xs}", *sorted((xs * HW, xs * (HW - 0.012))), 0, 0.1, -HD, HD, M["cove"], 0.004))
    for zs in (-1, 1):
        x = -HW + 1.2
        while x < HW - 0.3:
            if abs(x) > DOOR_HALF + 0.1:
                trims.append(span(f"AS_SeamNS{zs}{x:.1f}", x - 0.004, x + 0.004, 0.1, H, *sorted((zs * HD, zs * (HD - 0.004))), M["seam"], 0))
            x += 1.2
        for (x0, x1) in ((-HW, -DOOR_HALF - 0.08), (DOOR_HALF + 0.08, HW)):
            trims.append(span(f"AS_BumpNS{zs}{x0:.0f}", x0, x1, 0.86, 0.96, *sorted((zs * HD, zs * (HD - 0.03))), M["bumper"], 0.01, 3))
            trims.append(span(f"AS_CoveNS{zs}{x0:.0f}", x0, x1, 0, 0.1, *sorted((zs * HD, zs * (HD - 0.012))), M["cove"], 0.004))
        zin = zs * HD
        for xs in (-1, 1):
            trims.append(span(f"AS_Frame{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.06))), 0, DOOR_H + 0.06,
                              *sorted((zin, zin - zs * 0.02)), M["steel"], 0.004))
            trims.append(span(f"AS_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), M["steel"], 0.002))
        trims.append(span(f"AS_FrameT{zs}", -DOOR_HALF - 0.06, DOOR_HALF + 0.06, DOOR_H, DOOR_H + 0.06, *sorted((zin, zin - zs * 0.02)), M["steel"], 0.004))
        trims.append(span(f"AS_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), M["steel"], 0.002))
    out += [finish(o, 1.0, angle=40) for o in trims]
    # 室名札（出口の扉の東）と注意札（モニターの東端）
    zf = HD - 0.004
    sg = quad("AS_Sign", [(1.35, 1.55, zf), (0.75, 1.55, zf), (0.75, 1.7, zf), (1.35, 1.7, zf)], M["sign"])
    hq.face_toward(sg, (0, 0, -1))
    ct = quad("AS_Caution", [(3.75, 1.45, zf), (3.35, 1.45, zf), (3.35, 1.65, zf), (3.75, 1.65, zf)], M["caution"])
    hq.face_toward(ct, (0, 0, -1))
    out += [sg, ct]
    out += ceiling(M)
    return out


def ceiling(M):
    p = []
    p.append(span("AC_Slab", -HW0, HW0, H, H + 0.1, -HD0, HD0, M["ceil"], bev=0))
    # 角ダクト（ビルダーの Duct：x=-2.0、y=2.65 中心、0.4 角）と継ぎ手のフランジ、吊りボルト、吹出口
    dx, dy, s = -2.0, H - 0.35, 0.4
    p.append(span("AC_Duct", dx - s / 2, dx + s / 2, dy - s / 2, dy + s / 2, -HD + 0.3, HD - 0.3, M["galv"], 0.004))
    z = -HD + 0.6
    while z < HD - 0.3:
        p.append(span(f"AC_Flange{z:.1f}", dx - s / 2 - 0.025, dx + s / 2 + 0.025, dy - s / 2 - 0.025, dy + s / 2 + 0.025, z - 0.015, z + 0.015, M["galv"], 0.004))
        for sx in (-1, 1):
            p.append(cyl(f"AC_Rod{z:.1f}{sx}", (dx + sx * (s / 2 + 0.05), dy - s / 2 - 0.04, z), H - (dy - s / 2 - 0.04), 0.005, M["steel"], 8))
        p.append(span(f"AC_Hanger{z:.1f}", dx - s / 2 - 0.07, dx + s / 2 + 0.07, dy - s / 2 - 0.045, dy - s / 2 - 0.02, z - 0.02, z + 0.02, M["steel"], 0.003))
        z += 1.2
    for k, zz in enumerate((-2.8, 0.0, 2.8)):
        p.append(span(f"AC_Diffuser{k}", dx - 0.14, dx + 0.14, dy - s / 2 - 0.01, dy - s / 2, zz - 0.2, zz + 0.2, M["alu"], 0.004))
        for g in range(6):
            gz = zz - 0.16 + g * 0.064
            p.append(span(f"AC_DiffG{k}{g}", dx - 0.12, dx + 0.12, dy - s / 2 - 0.016, dy - s / 2 - 0.01, gz - 0.008, gz + 0.008, M["black"], 0))
    # ケーブルトレイ（ビルダーの CableTray：x=2.2、y=2.85）とケーブルの束
    tx, ty = 2.2, H - 0.15
    for sx in (-1, 1):
        p.append(span(f"AC_TrayRail{sx}", tx + sx * 0.15 - 0.01, tx + sx * 0.15 + 0.01, ty - 0.03, ty + 0.03, -HD + 0.3, HD - 0.3, M["galv"], 0.003))
    z = -HD + 0.4
    while z < HD - 0.3:
        p.append(span(f"AC_Rung{z:.1f}", tx - 0.15, tx + 0.15, ty - 0.03, ty - 0.02, z - 0.012, z + 0.012, M["galv"], 0.002))
        z += 0.3
    for k in range(6):
        cx = tx - 0.1 + k * 0.04
        p.append(pipe(f"AC_Cable{k}", [(cx, ty - 0.005, -HD + 0.3), (cx + 0.005 * (k % 2), ty - 0.005, HD - 0.3)], 0.012, M["cable"] if k % 2 else M["cableG"]))
    # スプリンクラーの赤い配管とヘッド
    p.append(pipe("AC_Sprinkler", [(0.8, H - 0.12, -HD + 0.2), (0.8, H - 0.12, HD - 0.2)], 0.025, M["red"]))
    for zz in (-3.0, -1.0, 1.0, 3.0):
        p.append(cyl(f"AC_SprDrop{zz}", (0.8, H - 0.24, zz), 0.12, 0.012, M["red"], 12))
        p.append(lathe(f"AC_SprHead{zz}", (0.8, H - 0.29, zz), [(0.0, 0.0), (0.025, 0.005), (0.012, 0.03), (0.01, 0.05)], M["alu"], 16))
    # 吊り下げのLEDパネル（ビルダーの RoomLight z=±2.25 の上と、東の列）
    for i, (lx, lz) in enumerate([(0.0, z0) for z0 in LIGHT_ZS] + [(1.5, z0) for z0 in LIGHT_ZS]):
        y = 2.55
        p.append(span(f"AC_LBody{i}", lx - 0.62, lx + 0.62, y, y + 0.06, lz - 0.16, lz + 0.16, M["alu"], 0.008))
        p.append(span(f"AC_LLed{i}", lx - 0.6, lx + 0.6, y - 0.004, y + 0.001, lz - 0.14, lz + 0.14, M["led"], 0))
        for sx in (-1, 1):
            p.append(cyl(f"AC_LWire{i}{sx}", (lx + sx * 0.5, y + 0.06, lz), H - y - 0.06, 0.0015, M["black"], 6))
    return [finish(o, 2.0, angle=40) for o in p]                   # 亜鉛めっきの結晶が細かく見えるよう 0.5m/枚


# ============================== 北壁のモニター・キャビネット・観察ブース ==============================

def display(name, xc, yc, zf, w, h, scr, M):
    """薄いモニター（前面 z=zf、画面は -Z を向く）"""
    p = [span(name + "_B", xc - w / 2, xc + w / 2, yc - h / 2, yc + h / 2, zf, zf + 0.035, M["black"], 0.006)]
    s = quad(name + "_S", [(xc - w / 2 + 0.015, yc - h / 2 + 0.015, zf - 0.001), (xc + w / 2 - 0.015, yc - h / 2 + 0.015, zf - 0.001),
                           (xc + w / 2 - 0.015, yc + h / 2 - 0.015, zf - 0.001), (xc - w / 2 + 0.015, yc + h / 2 - 0.015, zf - 0.001)], scr)
    hq.face_toward(s, (0, 0, -1))
    return p, s


def interior(M):
    out, screens = [], []
    zr0, zr1 = HD0 - 0.5, HD0 - 0.2                  # ラック（ビルダー：中心 hd-0.35、奥行き 0.3。hd は壁の中心基準）
    zf = zr0 - 0.005
    layouts = {
        -2.2: [(-0.45, 1.98, M["scrMri"]), (0.45, 1.98, M["scrEeg"])],                     # 下は記憶回路の端末
        0.0: [(-0.45, 2.02, M["scrConn"]), (0.45, 2.02, M["scrCt"]), (-0.6, 0.98, M["scrSpec"]), (0.6, 0.98, M["scrEeg"])],
        2.2: [(-0.45, 1.98, M["scrCt"]), (0.45, 1.98, M["scrConn"]), (-0.45, 1.4, M["scrSpec"]), (0.45, 1.4, M["scrMri"])],
    }
    for x in RACK_XS:
        out.append(span(f"AI_Rack{x}", x - 0.9, x + 0.9, 0.2, 2.4, zr0, zr1, M["rack"], 0.01, 3))
        out.append(span(f"AI_RackBase{x}", x - 0.88, x + 0.88, 0.0, 0.2, zr0 + 0.03, zr1, M["black"], 0.004))
        out.append(span(f"AI_RackTop{x}", x - 0.92, x + 0.92, 2.4, 2.44, zr0 - 0.02, zr1, M["rack"], 0.01))
        for k, (dx, y, scr) in enumerate(layouts[x]):
            b, s = display(f"AI_D{x}_{k}", x + dx, y, zf - 0.03, 0.8, 0.47, scr, M)
            out += b; screens.append(s)
        if x != -2.2:
            out.append(span(f"AI_Shelf{x}", x - 0.5, x + 0.5, 0.74, 0.77, zr0 - 0.3, zr0, M["steel"], 0.006))
            out.append(span(f"AI_Kb{x}", x - 0.2, x + 0.2, 0.77, 0.785, zr0 - 0.25, zr0 - 0.1, M["black"], 0.004))
        if x == -2.2:
            # 記憶回路の端末（ビルダーの CircuitTerminal 0.9 x 0.6、y=1.2）の黒い縁と下の操作盤
            def to3d(u, v, d):
                return (x + u, 1.2 + v, zf - d)
            out.append(frame_ring("AI_TermBezel", to3d, 1.02, 0.72, 0.03, 0.9, 0.6, 0.01, -0.005, 0.03, M["black"]))
            out.append(span("AI_TermPanel", x - 0.35, x + 0.35, 0.72, 0.78, zr0 - 0.22, zr0, M["steel"], 0.006))
            for k in range(6):
                bx = x - 0.25 + k * 0.1
                out.append(cyl_between(f"AI_TermKnob{k}", (bx, 0.78, zr0 - 0.12), (bx, 0.8, zr0 - 0.12), 0.015, M["black"], 12))
        # ラックから天井へのケーブル
        out.append(pipe(f"AI_Up{x}", [(x + 0.7, 2.44, zr0 + 0.15), (x + 0.7, H - 0.02, zr0 + 0.15)], 0.02, M["cable"]))
    out += cabinets(M)
    booth_parts, booth_glass = booth(M)
    out += booth_parts
    glass = [o for o in out if o.name.startswith("AI_CabGlass")]
    out = [o for o in out if not o.name.startswith("AI_CabGlass")]
    for o in out:
        finish(o, 1.0, angle=40, keep_uv=o.name.startswith("AI_Binder"))
    return out + screens + booth_glass + glass


def cabinets(M):
    """西壁の施錠キャビネット3台（ガラス戸の中にバインダー）"""
    p = []
    rnd = random.Random(701)
    x0, x1 = -HW, -HW + 0.5
    for i, zc in enumerate((-2.0, -0.6, 0.8)):
        za, zb = zc - 0.6, zc + 0.6
        # 中空の箱（背・両側・天地・中段）。下は鋼の扉、上はガラス戸で中のバインダーが見える
        p.append(span(f"AI_CabBack{i}", x0, x0 + 0.02, 0.05, 2.0, za, zb, M["steel"], 0.003))
        for zz in (za, zb - 0.02):
            p.append(span(f"AI_CabSide{i}{zz:.1f}", x0, x1 - 0.02, 0.05, 2.0, zz, zz + 0.02, M["steel"], 0.004))
        p.append(span(f"AI_CabTop{i}", x0, x1 - 0.02, 1.98, 2.0, za, zb, M["steel"], 0.004))
        p.append(span(f"AI_CabBot{i}", x0, x1 - 0.02, 0.05, 0.93, za + 0.02, zb - 0.02, M["steel"], 0.003))
        p.append(span(f"AI_CabBase{i}", x0, x1 - 0.04, 0.0, 0.05, za + 0.02, zb - 0.02, M["black"], 0.003))
        for zs in (-1, 1):
            zz0, zz1 = sorted((zc, zc + zs * 0.58))
            p.append(span(f"AI_CabLow{i}{zs}", x1 - 0.02, x1, 0.08, 0.92, zz0 + 0.005, zz1 - 0.005, M["steel"], 0.006))
            # 上のガラス戸（枠とガラス）
            for (ya, yb, a, b) in ((0.95, 0.99, zz0, zz1), (1.93, 1.97, zz0, zz1), (0.95, 1.97, zz0, zz0 + 0.04), (0.95, 1.97, zz1 - 0.04, zz1)):
                p.append(span(f"AI_CabFr{i}{zs}", x1 - 0.02, x1, ya, yb, a + 0.005, b - 0.005, M["steel"], 0.003))
            gl = quad(f"AI_CabGlass{i}{zs}", [(x1 - 0.01, 0.99, zz0 + 0.045), (x1 - 0.01, 0.99, zz1 - 0.045),
                                              (x1 - 0.01, 1.93, zz1 - 0.045), (x1 - 0.01, 1.93, zz0 + 0.045)], M["glass"])
            hq.face_toward(gl, (1, 0, 0))
            p.append(gl)
            p.append(span(f"AI_CabHd{i}{zs}", x1, x1 + 0.02, 0.8, 0.9, zc + zs * 0.03 - 0.006, zc + zs * 0.03 + 0.006, M["alu"], 0.003))
        p.append(cyl_between(f"AI_CabLock{i}", (x1, 0.86, zc + 0.08), (x1 + 0.012, 0.86, zc + 0.08), 0.01, M["alu"], 12))
        for s in (1.0, 1.46):
            p.append(span(f"AI_CabShelf{i}{s}", x0 + 0.02, x1 - 0.04, s - 0.015, s, za + 0.02, zb - 0.02, M["steel"], 0.003))
            z = za + 0.05
            while z < zb - 0.1:
                w = rnd.uniform(0.05, 0.08)
                cell = rnd.randrange(16)
                u0, v0 = (cell % 8) / 8, 1 - (cell // 8 + 1) / 2
                hgt = rnd.uniform(0.3, 0.33)
                b = quad(f"AI_Binder{i}{s}{z:.2f}", [(x1 - 0.06, s, z), (x1 - 0.06, s, z + w), (x1 - 0.06, s + hgt, z + w), (x1 - 0.06, s + hgt, z)],
                         M["binder"], uv=((u0 + 0.005, v0), (u0 + 0.12, v0), (u0 + 0.12, v0 + 0.5), (u0 + 0.005, v0 + 0.5)))
                hq.face_toward(b, (1, 0, 0))
                p.append(b)
                p.append(span(f"AI_BinderBody{i}{s}{z:.2f}", x0 + 0.06, x1 - 0.062, s, s + hgt, z, z + w, M["black"], 0))
                z += w + rnd.uniform(0.0, 0.01)
    return p


def booth(M):
    """南西：ガラスの観察ブース（北と東のガラス壁。東の南側は入口）と検査用リクライニングチェア"""
    p, glass = [], []
    zg, xg = -HD0 + 2.0, -HW0 + 2.4             # ビルダーの PartitionGlass の z、PartitionFrame の x（壁の中心基準）
    top = 2.6
    # 枠（アルミ）
    p.append(span("AB_RailN", -HW, xg + 0.03, top, top + 0.05, zg - 0.03, zg + 0.03, M["alu"], 0.004))
    p.append(span("AB_SillN", -HW, xg + 0.03, 0.0, 0.05, zg - 0.03, zg + 0.03, M["alu"], 0.004))
    p.append(span("AB_Post", xg - 0.03, xg + 0.03, 0.0, top + 0.05, zg - 0.03, zg + 0.03, M["alu"], 0.004))
    for x in (-HW + 1.2,):
        p.append(span(f"AB_MullN{x}", x - 0.02, x + 0.02, 0.05, top, zg - 0.025, zg + 0.025, M["alu"], 0.003))
    ze = -HD0 + 1.0                              # 東のガラス壁の南端（その南は入口）
    p.append(span("AB_RailE", xg - 0.03, xg + 0.03, top, top + 0.05, ze, zg, M["alu"], 0.004))
    p.append(span("AB_SillE", xg - 0.03, xg + 0.03, 0.0, 0.05, ze, zg, M["alu"], 0.004))
    p.append(span("AB_PostE", xg - 0.03, xg + 0.03, 0.0, top + 0.05, ze - 0.03, ze + 0.03, M["alu"], 0.004))
    gN = quad("AB_GlassN", [(-HW, 0.05, zg), (xg, 0.05, zg), (xg, top, zg), (-HW, top, zg)], M["glass"])
    gE = quad("AB_GlassE", [(xg, 0.05, ze), (xg, 0.05, zg), (xg, top, zg), (xg, top, ze)], M["glass"])
    glass += [gN, gE]
    # 検査用リクライニングチェア（頭は西の壁側）
    cx, cz = -HW + 1.25, -HD0 + 1.0
    p.append(lathe("AB_ChairBase", (cx, 0, cz), [(0.28, 0), (0.28, 0.02), (0.2, 0.04), (0.06, 0.06)], M["alu"], 32))
    p.append(cyl("AB_ChairPost", (cx, 0.06, cz), 0.36, 0.05, M["alu"], 20))
    def seg(name, x0, x1, y0, y1, ang):
        o = span(name, x0, x1, y0, y1, cz - 0.3, cz + 0.3, M["vinylSeat"], 0.04, 4)
        hq.apply_mods(o)
        pivot = U(x0 if ang > 0 else x1, (y0 + y1) / 2, cz)
        o.data.transform(Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(ang), 4, "Y") @ Matrix.Translation(-pivot))
        return o
    p.append(span("AB_Seat", cx - 0.3, cx + 0.3, 0.48, 0.6, cz - 0.3, cz + 0.3, M["vinylSeat"], 0.04, 4))
    p.append(seg("AB_Back", cx - 0.95, cx - 0.3, 0.52, 0.64, -28))
    p.append(seg("AB_Legs", cx + 0.3, cx + 0.95, 0.46, 0.56, 18))
    p.append(span("AB_Head", cx - 1.15, cx - 0.9, 0.9, 1.02, cz - 0.16, cz + 0.16, M["vinylSeat"], 0.04, 4))
    for zs in (-1, 1):
        p.append(span(f"AB_Arm{zs}", cx - 0.2, cx + 0.25, 0.66, 0.71, cz + zs * 0.34 - 0.04, cz + zs * 0.34 + 0.04, M["pad"], 0.015, 3))
        p.append(span(f"AB_Strap{zs}", cx + 0.0, cx + 0.06, 0.66, 0.73, cz + zs * 0.34 - 0.05, cz + zs * 0.34 + 0.05, M["leather"], 0.004))
    # 脇の機器（モニターを載せたワゴン）
    wx, wz = -HW + 0.4, -HD + 1.6                # ガラス壁（z=-HD+2.0）の内側
    p.append(span("AB_CartTop", wx - 0.25, wx + 0.25, 0.8, 0.83, wz - 0.2, wz + 0.2, M["steel"], 0.006))
    p.append(cyl("AB_CartPost", (wx, 0.05, wz), 0.75, 0.02, M["alu"], 12))
    p.append(lathe("AB_CartFoot", (wx, 0, wz), [(0.22, 0), (0.22, 0.02), (0.05, 0.05)], M["alu"], 24))
    b, s = display("AB_Mon", wx, 1.12, wz - 0.03, 0.42, 0.26, M["scrEeg"], M)
    # 画面は部屋（+X）側に向ける
    for o in b + [s]:
        o.data.transform(Matrix.Translation(U(wx, 0, wz)) @ Matrix.Rotation(math.radians(90), 4, "Z") @ Matrix.Translation(-U(wx, 0, wz)))
    p += b
    glass.append(s)
    return p, glass


# ============================== 作業台・脳模型・ヘルメット ==============================

def _fix_normals(o):
    """頂点を U() で置き直すと鏡映になって面が裏返るので、外向きに揃え直す"""
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()


def brain_mesh(name, c, s, M, seed=3):
    """解剖用の脳模型：左右の半球（脳回のしわを変位で）＋小脳＋脳幹"""
    parts = []
    for side in (-1, 1):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=40, radius=1.0, location=(0, 0, 0))
        o = bpy.context.active_object; o.name = f"{name}_H{side}"
        for v in o.data.vertices:
            p = v.co.copy()
            # 形：前後に長く、下が平ら、内側は縦に平ら
            q = Vector((p.x, p.y, p.z))
            ridge = 1 - abs(noise.noise(q * 6.0 + Vector((seed, side, 0))))
            ridge = ridge ** 3
            k = 1 + 0.06 * ridge - 0.03
            x, y, z = p.x * s[0] * 0.25 * k, p.y * s[2] * 0.5 * k, p.z * s[1] * 0.5 * k
            if z < -s[1] * 0.18:
                z = -s[1] * 0.18 + (z + s[1] * 0.18) * 0.35
            v.co = U(c[0] + side * (s[0] * 0.26 + x), c[1] + z, c[2] + y)
        _fix_normals(o)
        o.data.materials.append(M["brain"])
        hq.smooth(o, 180)
        parts.append(o)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=1.0, location=(0, 0, 0))
    cb = bpy.context.active_object; cb.name = name + "_Cb"
    for v in cb.data.vertices:
        p = v.co.copy()
        v.co = U(c[0] + p.x * s[0] * 0.3, c[1] - s[1] * 0.28 + p.z * s[1] * 0.18, c[2] - s[2] * 0.3 + p.y * s[2] * 0.18)
    _fix_normals(cb)
    cb.data.materials.append(M["brain"]); hq.smooth(cb, 180)
    parts.append(cb)
    parts.append(cyl_between(name + "_Stem", (c[0], c[1] - s[1] * 0.2, c[2] - s[2] * 0.1), (c[0], c[1] - s[1] * 0.55, c[2] - s[2] * 0.15), 0.028, M["brain"], 16))
    return parts


def table(M):
    """作業台（ビルダーの WorkTable：天板 2.2 x 1.1、上面 0.89、下は 1.8 x 0.8 x 0.9 の箱）"""
    p = []
    p.append(span("AT_Top", -1.1, 1.1, 0.81, 0.89, -0.55, 0.55, M["epoxy"], 0.015, 4))
    p.append(span("AT_Base", -0.9, 0.9, 0.08, 0.8, -0.45, 0.45, M["steel"], 0.006))
    p.append(span("AT_Kick", -0.88, 0.88, 0.0, 0.08, -0.42, 0.42, M["black"], 0.004))
    for zs in (-1, 1):
        zf = zs * 0.45
        for k, x in enumerate((-0.6, 0.0, 0.6)):
            p.append(span(f"AT_Dr{zs}{k}", x - 0.28, x + 0.28, 0.62, 0.78, *sorted((zf, zf + zs * 0.015)), M["steel"], 0.005))
            p.append(span(f"AT_DrH{zs}{k}", x - 0.08, x + 0.08, 0.72, 0.735, *sorted((zf + zs * 0.015, zf + zs * 0.035)), M["alu"], 0.004))
            p.append(span(f"AT_Door{zs}{k}", x - 0.28, x + 0.28, 0.1, 0.6, *sorted((zf, zf + zs * 0.015)), M["steel"], 0.005))
            p.append(span(f"AT_DoorH{zs}{k}", x + 0.18, x + 0.2, 0.4, 0.55, *sorted((zf + zs * 0.015, zf + zs * 0.035)), M["alu"], 0.004))
    # 脳模型（ビルダーの BrainModel：台の上 (-0.6, 1.05, 0)、0.3 x 0.26 x 0.34）と透明な台座
    p.append(span("AT_Plinth", -0.78, -0.42, 0.89, 0.92, -0.2, 0.2, M["acrylic"], 0.01))
    p.append(cyl("AT_PlinthPost", (-0.6, 0.92, 0.0), 0.04, 0.02, M["acrylic"], 16))
    brain = brain_mesh("AT_Brain", (-0.6, 1.07, 0.0), (0.3, 0.24, 0.34), M)
    # 脳波アンプと束ねたケーブル
    p.append(span("AT_Amp", 0.2, 0.5, 0.89, 0.99, 0.28, 0.5, M["steel"], 0.008))
    p.append(pipe("AT_AmpCable", [(0.35, 0.95, 0.28), (0.45, 0.92, 0.2), (0.55, 0.91, 0.15)], 0.008, M["cable"], 0.05))
    for o in p:
        finish(o, 1.0, angle=40)
    return [join(p + brain, "Analysis_Table")]


def helmet(M, seed=711):
    """スキャン用ヘルメット（箱 0.4 x 0.18 x 0.4 の中心が原点。底 y=-0.09）。電極の並ぶ黒い半球と顎のベルト"""
    rnd = random.Random(seed)
    p = []
    r = 0.17
    base = -0.09
    p.append(lathe("AH_Shell", (0, base, 0), [(r, 0.0), (r, 0.02)] + [(r * math.cos(t), 0.02 + r * math.sin(t)) for t in
                                                                          [math.pi / 2 * k / 10 for k in range(1, 11)]], M["black"], 48, cap_bottom=False))
    p.append(lathe("AH_Pad", (0, base + 0.005, 0), [(r - 0.012, 0.0), (r - 0.012, 0.018)] + [((r - 0.012) * math.cos(t), 0.018 + (r - 0.012) * math.sin(t))
                                                                                         for t in [math.pi / 2 * k / 10 for k in range(1, 10)]], M["pad"], 48, cap_bottom=False))
    # 電極（半球の表面に 60 個）
    for k in range(60):
        th = math.acos(1 - rnd.uniform(0.05, 0.95))
        ph = rnd.uniform(0, math.pi * 2)
        n = Vector((math.sin(th) * math.cos(ph), math.cos(th), math.sin(th) * math.sin(ph)))
        pos = Vector((0, base + 0.02, 0)) + n * r
        p.append(cyl_between(f"AH_El{k}", tuple(pos), tuple(pos + n * 0.012), 0.008, M["alu"], 10))
    # 後ろへ伸びるケーブルの束と顎のベルト
    p.append(pipe("AH_Cable", [(0, base + 0.12, 0.15), (0, base + 0.05, 0.25), (0.05, base + 0.01, 0.35)], 0.018, M["cable"], 0.08))
    for sx in (-1, 1):
        p.append(pipe(f"AH_Strap{sx}", [(sx * (r + 0.002), base + 0.03, 0.02), (sx * (r - 0.01), base - 0.0, 0.05), (sx * 0.08, base + 0.004, 0.14)],
                      0.01, M["leather"], 0.05))
    for o in p:
        finish(o, 1.0, angle=45)
    return [join(p, "Analysis_Helmet")]


def anomaly(M):
    """異常レポートの画面（箱 0.6 x 0.4 x 0.04 の中心が原点）"""
    b, s = display("AA", 0.0, 0.0, -0.02, 0.62, 0.42, M["scrAnom"], M)
    for o in b:
        finish(o, 1.0, angle=40)
    return [join(b + [s], "Analysis_Anomaly")]


def _card(name, w, d, y, mat, rot=0.0):
    q = quad(name, [(-w / 2, y, -d / 2), (w / 2, y, -d / 2), (w / 2, y, d / 2), (-w / 2, y, d / 2)], mat)
    hq.face_toward(q, (0, 1, 0))
    q.data.transform(Matrix.Rotation(-rot, 4, "Z"))
    sol = q.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.001
    return finish(q, keep_uv=True, angle=60)


def id_card(M):
    """職員証（箱 0.13 x 0.01 x 0.09、底 -0.005）とストラップ"""
    c = _card("AIdCard", 0.0856, 0.054, -0.004, M["card"], 0.3)
    st = pipe("AIdStrap", [(-0.03, -0.004, 0.02), (-0.06, -0.004, 0.05), (-0.02, -0.004, 0.07), (0.04, -0.004, 0.06)], 0.004, M["bumper"], 0.02)
    finish(st, 1.0, angle=60)
    return [join([c, st], "Analysis_IdCard")]


def plan(M):
    return [_card("APlan", 0.21, 0.297, -0.008, M["plan"], 0.1)]


def spec(M):
    return [_card("ASpec", 0.21, 0.297, -0.008, M["spec"], -0.2)]


def door(M):
    """鋼製の扉（網入りガラスの細い窓・レバー・蹴板・ドアクローザー）。部屋側は +Z"""
    t = 0.022
    p = []
    slot = (-0.12, 0.12, 1.2, 1.85)
    p += train_room.grid_wall("ADr_Leaf", [slot], -0.458, 0.458, 0.0, 2.1, lambda a, b, c, d: (a, b, c, d, -t, t), M["doorGrey"])
    for zs in (-1, 1):
        def to3d(u, v, d, zs=zs):
            return (u, 1.525 + v, zs * (t + d))
        p.append(frame_ring(f"ADr_Bead{zs}", to3d, 0.24, 0.65, 0.004, 0.2, 0.61, 0.004, -t, 0.006, M["steel"]))
        g = rrect_face(f"ADr_Glass{zs}", to3d, 0.205, 0.615, 0.003, -t + 0.002, M["wired"])
        hq.face_toward(g, (0, 0, zs)); p.append(g)
        zf = zs * t
        p.append(span(f"ADr_Plate{zs}", -0.4, -0.33, 0.95, 1.2, *sorted((zf, zf + zs * 0.004)), M["alu"], 0.003))
        p.append(pipe(f"ADr_Lever{zs}", [(-0.365, 1.05, zf + zs * 0.004), (-0.365, 1.05, zf + zs * 0.05), (-0.23, 1.05, zf + zs * 0.05)], 0.01, M["alu"], 0.02))
        p.append(span(f"ADr_Kick{zs}", -0.44, 0.44, 0.02, 0.28, *sorted((zf, zf + zs * 0.002)), M["alu"], 0.001))
    # ドアクローザー（部屋側の上）
    p.append(span("ADr_Closer", 0.1, 0.38, 1.94, 2.02, t, t + 0.06, M["alu"], 0.01))
    p.append(pipe("ADr_Arm", [(0.12, 2.0, t + 0.05), (-0.1, 2.06, t + 0.08), (-0.25, 2.08, t + 0.02)], 0.008, M["black"], 0.02))
    for y in (0.25, 1.05, 1.85):
        p.append(cyl_between(f"ADr_Hinge{y}", (0.462, y - 0.05, 0.0), (0.462, y + 0.05, 0.0), 0.008, M["alu"], 12))
    for o in p:
        finish(o, 1.0, angle=40, keep_uv=o.name.startswith("ADr_Glass"))
    return [join(p, "Analysis_Door")]


def _breaker_mats(M):
    return {"mel": M["panel"], "grille": M["black"], "hazard": M["caution"], "sus": M["steel"], "rubber": M["black"],
            "lever": hq.mat("ANA_LeverRed", (0.75, 0.12, 0.1), 0.45)}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.02), "Analysis_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "Analysis_Lever")]


PIECES = {"Shell": shell, "Interior": interior, "Table": table, "Helmet": helmet, "Anomaly": anomaly,
          "IdCard": id_card, "Plan": plan, "Spec": spec, "Door": door, "Breaker": breaker, "Lever": lever}
