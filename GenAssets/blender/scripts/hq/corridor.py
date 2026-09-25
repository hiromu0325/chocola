"""
回廊（ロの字：外周の壁 ±10・中央の塊の面 ±7・天井 3）と扉まわり、扉の奥の暗い廊下を品質重視で作る。
白い漆喰の壁・焦茶の床板・外周の腰壁（鏡板）・幅木・腰の見切り・回り縁・乳白ガラスの吊り灯。
内周の面には各辺10か所の扉の開口を空け、扉は枠・戸当たり・額縁・台輪・冠・沓摺と、別部品の扉板で作る。
回廊の扉はすべて「押して」奥へ開く（戸当たりは回廊側、蝶番は奥の廊下側）。

  Shell      回廊の原点。床・壁（内周に開口）・天井・腰壁・見切り・回り縁・隅の柱型・吊り灯
  DoorFrame  扉ユニットの原点（+Z = 回廊側、壁の面 z=0、壁の裏 z=-0.15）。枠・戸当たり・額縁・冠・沓摺・表示灯の台
  DoorLeaf   扉板（ユニットの座標のまま。Unity側で蝶番の軸 HINGE を中心に回す）
  Signal     表示灯のガラス（Unity側で消灯／点灯／警報の材質に差し替える）
  Hallway    扉の奥の廊下（原点 = 壁の裏の開口の中心、+Z = 扉の側、-Z へ HALL_L 奥へ続く）
             回廊の扉の奥にも、各部屋の扉の外にも同じものを置く
  HallwayFog 廊下の闇（半透明の黒い板を奥へ重ねる。奥へ行くほど暗くなる）
"""
import math

import bmesh
import bpy
from mathutils import Euler, Matrix

import hq
from hq import U, span, box, lathe, cyl_between, pipe, quad, plate_xy, frame_ring, rrect, finish, join

O, I, H = 10.0, 7.0, 3.0
OF = O - 0.075                                   # 外周の壁の内面
WALL_T = 0.15                                    # 内周の壁の厚み（面 z=0 〜 裏 z=-0.15）
GAP = (I * 2 - 10 * 1.0) / 11
DOOR_S = [-I + GAP + 0.5 + s * (1.0 + GAP) for s in range(10)]
OPEN_HALF, OPEN_H = 0.5, 2.18                    # 開口（枠の外寸）
LEAF = (-0.465, 0.465, 0.02, 2.135, -0.0825, -0.0375)   # 扉板 x0, x1, y0, y1, z0, z1
HINGE = (-0.47, -0.09)                           # 蝶番の軸 (x, z)。Unity側の回転の中心
SIGNAL_Y = 2.585
HALL_L, HALL_HW, HALL_H = 8.0, 0.65, 2.45
SIDE_DOORS = {1: -3.3, -1: -6.1}                 # 奥の廊下の側面の扉（壁の側 → 奥行き）
YAWS = (0, 90, 180, 270)
LAMPS = [(0, (O + I) / 2), (0, -(O + I) / 2), ((O + I) / 2, 0), (-(O + I) / 2, 0)] + \
        [(sx * (O + I) / 2 * 0.98, sz * (O + I) / 2 * 0.98) for sx in (1, -1) for sz in (1, -1)]


def mats():
    M = {}
    M["floor"] = hq.mat("COR_Floor", (1, 1, 1), 0.5, tex="floor.png")
    M["plaster"] = hq.mat("COR_Plaster", (1, 1, 1), 0.9, tex="plaster.png")
    M["ceil"] = hq.mat("COR_Ceiling", (0.98, 0.98, 0.97), 0.9, tex="plaster.png")
    M["wains"] = hq.mat("COR_Wainscot", (0.74, 0.72, 0.66), 0.5, tex="paint.png")
    M["trim"] = hq.mat("COR_Trim", (0.5, 0.46, 0.41), 0.45, tex="paint.png")
    M["door"] = hq.mat("COR_Door", (0.88, 0.85, 0.78), 0.45, tex="paint.png")
    M["brass"] = hq.mat("COR_Brass", (0.78, 0.62, 0.34), 0.3, 1.0)
    M["steel"] = hq.mat("COR_Steel", (0.3, 0.3, 0.31), 0.4, 0.8)
    M["black"] = hq.mat("COR_Black", (0.02, 0.02, 0.02), 0.6)
    M["oak"] = hq.mat("COR_Oak", (0.8, 0.7, 0.55), 0.45, tex="../../lab/tex/oak.png")
    M["lamp"] = hq.mat("COR_LampGlass", (1.0, 0.98, 0.94), 0.3, emit=(1.0, 0.96, 0.88), emit_strength=3.0)
    M["signal"] = hq.mat("COR_Signal", (0.35, 0.3, 0.24), 0.2)
    M["oldwall"] = hq.mat("COR_OldPlaster", (1, 1, 1), 0.9, tex="old_plaster.png")
    M["oldfloor"] = hq.mat("COR_OldFloor", (1, 1, 1), 0.7, tex="old_floor.png")
    M["oldwains"] = hq.mat("COR_OldWainscot", (0.5, 0.47, 0.41), 0.6, tex="paint.png")
    M["oldceil"] = hq.mat("COR_OldCeiling", (0.78, 0.76, 0.72), 0.9, tex="plaster.png")
    M["olddoor"] = hq.mat("COR_OldDoor", (0.6, 0.57, 0.5), 0.6, tex="paint.png")
    M["shade"] = hq.mat("COR_OldShade", (0.62, 0.6, 0.55), 0.3)
    M["cord"] = hq.mat("COR_Cord", (0.05, 0.05, 0.05), 0.6)
    M["fog"] = hq.mat("COR_Fog", (0, 0, 0), 1.0, alpha=0.16)
    M["void"] = hq.mat("COR_Void", (0, 0, 0), 1.0)
    return M


# ============================== 補助 ==============================

def extrude(name, prof, a0, a1, to3d, material):
    """閉じた2D断面 prof=[(p, q), ...] を a0〜a1 に押し出す。to3d(p, q, a) → Unity座標"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    A = [bm.verts.new(U(*to3d(p, q, a0))) for p, q in prof]
    B = [bm.verts.new(U(*to3d(p, q, a1))) for p, q in prof]
    n = len(prof)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((A[i], A[j], B[j], B[i]))
    bm.faces.new(A)
    bm.faces.new(list(reversed(B)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return hq._obj(name, me, material)


def ring(name, half, prof, material, inward=True):
    """正方形の周に沿って断面を回した見切り（角は留め）。prof=[(壁からの出, 高さ), ...] の閉じた断面。
    inward=True は外周の壁の内側、False は中央の塊の外側"""
    s = -1 if inward else 1
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for d, y in prof:
        h = half + s * d
        rings.append([bm.verts.new(U(x, y, z)) for x, z in ((h, h), (-h, h), (-h, -h), (h, -h))])
    n = len(rings)
    for k in range(n):
        r0, r1 = rings[k], rings[(k + 1) % n]
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return hq._obj(name, me, material)


def raised_panel(name, u0, u1, v0, v1, to3d, rim, field, margin, material, both=True, base=0.0):
    """框に落とし込む鏡板（縁が薄く、中央が面取りで盛り上がる）。to3d(u, v, d) → Unity座標、d は板の中心面からの出。
    both=True は両面、False は片面（壁に張る腰壁）で裏は base の面"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    ou = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    iu = [(u0 + margin, v0 + margin), (u1 - margin, v0 + margin), (u1 - margin, v1 - margin), (u0 + margin, v1 - margin)]
    fo = [bm.verts.new(U(*to3d(u, v, rim))) for u, v in ou]
    fi = [bm.verts.new(U(*to3d(u, v, field))) for u, v in iu]
    if both:
        bo = [bm.verts.new(U(*to3d(u, v, -rim))) for u, v in ou]
        bi = [bm.verts.new(U(*to3d(u, v, -field))) for u, v in iu]
    else:
        bo = [bm.verts.new(U(*to3d(u, v, base))) for u, v in ou]
        bi = None
    bm.faces.new(fi)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((fo[i], fo[j], fi[j], fi[i]))
        bm.faces.new((fo[i], bo[i], bo[j], fo[j]))
    if both:
        bm.faces.new(list(reversed(bi)))
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((bo[j], bo[i], bi[i], bi[j]))
    else:
        bm.faces.new(list(reversed(bo)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return hq._obj(name, me, material)


def _rot(objs, yaw):
    m = Matrix.Rotation(math.radians(-yaw), 4, "Z")
    for o in objs:
        o.data.transform(m)
    return objs


def _knob(name, x, y, z, zs, material):
    """丸い真鍮の握り玉（zs=+1 で +Z 側へ、-1 で -Z 側へ出る）"""
    kb = lathe(name, (0, 0, 0), [(0.0, 0.0), (0.02, 0.004), (0.028, 0.016), (0.026, 0.03), (0.016, 0.04), (0.0, 0.043)],
               material, 32)
    rot = Euler((math.radians(90 * zs), 0, 0)).to_matrix().to_4x4()
    kb.data.transform(Matrix.Translation(U(x, y, z)) @ rot)
    return kb


# ============================== 回廊の外殻 ==============================

BASE_PROF = [(-0.005, 0.0), (0.02, 0.0), (0.02, 0.1), (0.015, 0.11), (0.011, 0.122), (0.005, 0.128), (-0.005, 0.13)]
RAIL_PROF = [(-0.005, 0.855), (0.03, 0.855), (0.038, 0.866), (0.04, 0.885), (0.036, 0.905), (0.026, 0.92),
             (0.01, 0.926), (-0.005, 0.93)]


def _crown_prof():
    pts = [(-0.005, H - 0.15), (0.008, H - 0.15), (0.012, H - 0.138), (0.02, H - 0.13)]
    for k in range(1, 7):                        # 凹の曲面（コーブ）
        a = math.radians(90 * k / 6)
        pts.append((0.02 + 0.07 * (1 - math.cos(a)), H - 0.13 + 0.07 * math.sin(a)))
    pts += [(0.1, H - 0.045), (0.112, H - 0.04), (0.118, H - 0.028), (0.12, H - 0.01), (0.12, H + 0.01), (-0.005, H + 0.01)]
    return pts


def shell(M):
    out = []

    def put(objs, yaw=0, uv=1.0, rot90=False):
        _rot(objs, yaw)
        for o in objs:
            finish(o, uv, rot90=rot90)
        out.extend(objs)

    for k, yaw in enumerate(YAWS):
        a = O if k % 2 == 0 else I - WALL_T
        # 床（板は廊下の向きに流す）と天井
        put([span(f"CS_Floor{k}", -a, a, -0.1, 0.0, I - WALL_T, O, M["floor"], 0)], yaw, 0.85, rot90=(k % 2 == 1))
        put([span(f"CS_Ceil{k}", -a, a, H, H + 0.1, I - WALL_T, O, M["ceil"], 0)], yaw, 1.0)
        # 外周の壁
        put([span(f"CS_WallOut{k}", -O, O, 0, H, OF, O + 0.075, M["plaster"], 0)], yaw, 1.0)
        # 内周の壁（扉の開口を避けて柱と欄間に分ける）
        edges = [-I] + [e for c in DOOR_S for e in (c - OPEN_HALF, c + OPEN_HALF)] + [I]
        piers = [span(f"CS_Pier{k}_{i}", edges[2 * i], edges[2 * i + 1], 0, H, I - WALL_T, I, M["plaster"], 0.003)
                 for i in range(len(edges) // 2)]
        heads = [span(f"CS_Head{k}_{i}", c - OPEN_HALF, c + OPEN_HALF, OPEN_H, H, I - WALL_T, I, M["plaster"], 0)
                 for i, c in enumerate(DOOR_S)]
        put(piers + heads, yaw, 1.0)
        # 内周の幅木（扉の台輪の間だけ。角では隣の辺と突き合わせる）
        cuts = [-I - 0.02] + [e for c in DOOR_S for e in (c - 0.61, c + 0.61)] + [I + 0.02]
        bases = [extrude(f"CS_BaseIn{k}_{i}", BASE_PROF, cuts[2 * i], cuts[2 * i + 1],
                         lambda d, y, t: (t, y, I + d), M["trim"]) for i in range(len(cuts) // 2)]
        put(bases, yaw, 1.0)
        # 外周の腰壁（向かいの扉の間ごとに鏡板を1枚。角の区間は2枚）
        stiles = sorted([c + 0.682 for c in DOOR_S[:-1]] + [DOOR_S[0] - 0.682, DOOR_S[-1] + 0.682, -8.284, 8.284])
        end = O - 0.25
        wp = [span(f"CS_WBack{k}", -end, end, 0.12, 0.86, OF - 0.01, OF, M["wains"], 0.001),
              span(f"CS_WRailB{k}", -end, end, 0.12, 0.2, OF - 0.028, OF - 0.01, M["wains"], 0.003),
              span(f"CS_WRailT{k}", -end, end, 0.8, 0.86, OF - 0.028, OF - 0.01, M["wains"], 0.003)]
        st = [span(f"CS_WStile{k}_{i}", p - 0.035, p + 0.035, 0.2, 0.8, OF - 0.028, OF - 0.01, M["wains"], 0.003)
              for i, p in enumerate(stiles)]
        st += [span(f"CS_WStileE{k}_{s}", *sorted((s * end, s * (end - 0.06))), 0.2, 0.8, OF - 0.028, OF - 0.01,
                    M["wains"], 0.003) for s in (-1, 1)]
        bays = [-end + 0.06] + stiles + [end - 0.06]
        pn = []
        for i in range(len(bays) - 1):
            u0 = bays[i] + (0.0 if i == 0 else 0.035) + 0.022
            u1 = bays[i + 1] - (0.0 if i == len(bays) - 2 else 0.035) - 0.022
            pn.append(raised_panel(f"CS_WPanel{k}_{i}", u0, u1, 0.222, 0.778,
                                   lambda u, v, d: (u, v, OF - 0.01 - d), 0.004, 0.014, 0.04, M["wains"],
                                   both=False, base=0.0))
        put(wp + pn, yaw, 1.0)
        put(st, yaw, 1.0, rot90=True)

    # 見切り（留めで一周させる）
    put([ring("CS_BaseOut", OF, BASE_PROF, M["trim"], True),
         ring("CS_RailOut", OF, RAIL_PROF, M["wains"], True),
         ring("CS_CrownOut", OF, _crown_prof(), M["trim"], True),
         ring("CS_CrownIn", I, _crown_prof(), M["trim"], False)])

    # 隅の柱型（外周の四隅）と、その幅木・見切りの回し
    for sx in (1, -1):
        for sz in (1, -1):
            f = O - 0.25                     # 柱の面
            post = [box(f"CS_Post{sx}{sz}", (sx * O, H / 2, sz * O), (0.5, H, 0.5), M["plaster"], 0.006)]
            for (y0, y1, d, m) in ((0.0, 0.13, 0.02, M["trim"]), (0.855, 0.93, 0.04, M["wains"]), (H - 0.15, H, 0.05, M["trim"])):
                post.append(span(f"CS_PostB{sx}{sz}{y0}", *sorted((sx * (f - d), sx * f)), y0, y1,
                                 *sorted((sz * (f - d), sz * OF)), m, 0.003))
                post.append(span(f"CS_PostC{sx}{sz}{y0}", *sorted((sx * (f - d), sx * OF)), y0, y1,
                                 *sorted((sz * (f - d), sz * f)), m, 0.003))
            put(post)

    # 天井の吊り灯（乳白ガラスの球・真鍮の飾り・短い吊り棒）
    for i, (x, z) in enumerate(LAMPS):
        lp = [lathe(f"CS_LCanopy{i}", (x, H - 0.035, z), [(0.08, 0.0), (0.08, 0.012), (0.06, 0.03), (0.0, 0.035)], M["brass"], 40),
              cyl_between(f"CS_LRod{i}", (x, H - 0.03, z), (x, 2.86, z), 0.007, M["brass"], 12),
              lathe(f"CS_LGallery{i}", (x, 2.835, z), [(0.02, 0.0), (0.062, 0.0), (0.07, 0.012), (0.066, 0.028), (0.02, 0.03)],
                    M["brass"], 40)]
        prof = []
        for k in range(15):                  # 球（上に口がある）
            a = math.radians(-90 + 180 * k / 14)
            prof.append((0.135 * math.cos(a), 0.135 * math.sin(a)))
        prof = [(r, h) for r, h in prof if h < 0.115] + [(0.058, 0.118), (0.058, 0.13)]
        lp.append(lathe(f"CS_LGlobe{i}", (x, 2.705, z), prof, M["lamp"], 40, cap_top=False))
        put(lp)
    return out


# ============================== 扉の枠・扉板・表示灯 ==============================

CASING_PROF = [(0.0, 0.0), (0.0, 0.012), (0.006, 0.018), (0.014, 0.02), (0.062, 0.02), (0.068, 0.026),
               (0.078, 0.032), (0.1, 0.032), (0.1, 0.0)]
CORNICE_PROF = [(0.0, -0.002), (0.0, 0.03), (0.012, 0.034), (0.026, 0.046), (0.04, 0.054), (0.055, 0.056),
                (0.062, 0.056), (0.062, -0.002)]


def door_frame(M):
    p, vert = [], []
    t0 = -WALL_T
    vert += [span("DF_JambL", -OPEN_HALF, -0.47, 0, OPEN_H, t0, 0.0, M["trim"], 0.002),
             span("DF_JambR", 0.47, OPEN_HALF, 0, OPEN_H, t0, 0.0, M["trim"], 0.002)]
    p.append(span("DF_Head", -OPEN_HALF, OPEN_HALF, 2.15, OPEN_H, t0, 0.0, M["trim"], 0.002))
    # 戸当たり（回廊側。扉は奥へ押して開き、閉じると戸当たりに当たって止まる）
    vert += [span("DF_StopL", -0.47, -0.448, 0, 2.15, -0.0375, -0.012, M["trim"], 0.002),
             span("DF_StopR", 0.448, 0.47, 0, 2.15, -0.0375, -0.012, M["trim"], 0.002)]
    p.append(span("DF_StopT", -0.47, 0.47, 2.115, 2.15, -0.0375, -0.012, M["trim"], 0.002))
    # 額縁（両脇は台輪の上から、上は突き付けで重ねて留めに見せる）
    for sx in (-1, 1):
        vert.append(extrude(f"DF_Casing{sx}", CASING_PROF, 0.2, 2.28,
                            lambda u, d, y, sx=sx: (sx * (OPEN_HALF + u), y, d), M["trim"]))
        p.append(span(f"DF_Plinth{sx}", *sorted((sx * 0.49, sx * 0.615)), 0.0, 0.2, 0.0, 0.036, M["trim"], 0.004))
    p.append(extrude("DF_CasingT", CASING_PROF, -0.6, 0.6, lambda u, d, x: (x, OPEN_H + u, d), M["trim"]))
    # 冠（帯板＋持ち送りの蛇腹）
    p.append(span("DF_Frieze", -0.62, 0.62, 2.28, 2.37, 0.0, 0.026, M["trim"], 0.003))
    p.append(extrude("DF_Cornice", CORNICE_PROF, -0.665, 0.665, lambda q, d, x: (x, 2.37 + q, d), M["trim"]))
    # 沓摺（樫）
    p.append(span("DF_Sill", -0.52, 0.52, 0.0, 0.016, t0, 0.04, M["oak"], 0.005))
    # 表示灯の台と配管
    sig = [(x, SIGNAL_Y + y) for x, y in rrect(0.24, 0.11, 0.022, 5)]
    p.append(plate_xy("DF_SigPlate", sig, 0.0, 0.012, M["brass"], 0.002))
    for sx in (-1, 1):
        p.append(cyl_between(f"DF_SigCap{sx}", (sx * 0.085, SIGNAL_Y, 0.012), (sx * 0.093, SIGNAL_Y, 0.012), 0.045, M["brass"], 32))
    for o in p:
        finish(o, 1.0)
    for o in vert:
        finish(o, 1.0, rot90=True)
    return p + vert


def door_leaf(M, paint=None, pfx="DL"):
    """框と4枚の鏡板の扉（回廊側 = +Z の面に握り玉・鍵座・蹴り板・名札。奥の側に丁番）"""
    paint = paint or M["door"]
    x0, x1, y0, y1, z0, z1 = LEAF
    zc, t = (z0 + z1) / 2, (z1 - z0) / 2
    p, vert = [], []
    sw, mw = 0.12, 0.045
    vert += [span(f"{pfx}_StileL", x0, x0 + sw, y0, y1, z0, z1, paint, 0.003),
             span(f"{pfx}_StileR", x1 - sw, x1, y0, y1, z0, z1, paint, 0.003)]
    rails = ((y1 - 0.14, y1), (0.93, 1.13), (y0, y0 + 0.23))
    for i, (a, b) in enumerate(rails):
        p.append(span(f"{pfx}_Rail{i}", x0 + sw, x1 - sw, a, b, z0, z1, paint, 0.003))
    for i, (a, b) in enumerate(((1.13, y1 - 0.14), (y0 + 0.23, 0.93))):
        vert.append(span(f"{pfx}_Mull{i}", -mw, mw, a, b, z0, z1, paint, 0.003))
    panels = [(x0 + sw, -mw, 1.13, y1 - 0.14), (mw, x1 - sw, 1.13, y1 - 0.14),
              (x0 + sw, -mw, y0 + 0.23, 0.93), (mw, x1 - sw, y0 + 0.23, 0.93)]
    for i, (a, b, c, d) in enumerate(panels):
        p.append(raised_panel(f"{pfx}_Panel{i}", a - 0.01, b + 0.01, c - 0.01, d + 0.01,
                              lambda u, v, dd: (u, v, zc + dd), 0.007, 0.016, 0.055, paint))
        w, h = b - a, d - c
        for zs in (-1, 1):
            def to3d(u, v, dd, zs=zs, a=a, b=b, c=c, d=d):
                return ((a + b) / 2 + u, (c + d) / 2 + v, zc + zs * (t + dd))
            rg = frame_ring(f"{pfx}_Bead{i}{zs}", to3d, w + 0.024, h + 0.024, 0.002, w - 0.014, h - 0.014, 0.002,
                            -0.003, 0.006, paint, 2)
            hq.bevel(rg, 0.0025, 2, 30)
            p.append(rg)
    # 金物
    for zs, zf in ((1, z1), (-1, z0)):
        p.append(cyl_between(f"{pfx}_Rose{zs}", (0.385, 1.02, zf), (0.385, 1.02, zf + zs * 0.008), 0.03, M["brass"], 32))
        p.append(cyl_between(f"{pfx}_Neck{zs}", (0.385, 1.02, zf + zs * 0.008), (0.385, 1.02, zf + zs * 0.04), 0.009, M["brass"], 16))
        p.append(_knob(f"{pfx}_Knob{zs}", 0.385, 1.02, zf + zs * 0.04, zs, M["brass"]))
        esc = [(0.385 + x, 0.9 + y) for x, y in rrect(0.032, 0.075, 0.012, 4)]
        p.append(plate_xy(f"{pfx}_Esc{zs}", esc, *sorted((zf, zf + zs * 0.004)), M["brass"], 0.001))
        key = [(0.385 + x, 0.9 + y) for x, y in rrect(0.008, 0.024, 0.0035, 3)]
        p.append(plate_xy(f"{pfx}_Key{zs}", key, *sorted((zf + zs * 0.004, zf + zs * 0.0048)), M["black"], 0.0))
    p.append(span(f"{pfx}_Kick", x0 + 0.025, x1 - 0.025, 0.03, 0.21, z1, z1 + 0.0025, M["brass"], 0.001))
    plate = [(x, 2.065 + y) for x, y in rrect(0.17, 0.055, 0.01, 4)]     # 上の框に真鍮の名札（文字は無い）
    p.append(plate_xy(f"{pfx}_Plate", plate, z1, z1 + 0.004, M["brass"], 0.001))
    for sx in (-1, 1):
        p.append(cyl_between(f"{pfx}_Screw{sx}", (sx * 0.07, 2.065, z1 + 0.004), (sx * 0.07, 2.065, z1 + 0.0055), 0.004, M["brass"], 12))
    # 丁番（奥の側。軸 = HINGE）
    hx, hz = HINGE
    for y in (0.28, 1.08, 1.88):
        p.append(span(f"{pfx}_HFlap{y}", x0, x0 + 0.03, y - 0.05, y + 0.05, z0 - 0.003, z0, M["steel"], 0.001))
        p.append(cyl_between(f"{pfx}_HKnuckle{y}", (hx, y - 0.052, hz), (hx, y + 0.052, hz), 0.0085, M["steel"], 16))
        p.append(cyl_between(f"{pfx}_HPin{y}", (hx, y - 0.058, hz), (hx, y + 0.058, hz), 0.004, M["steel"], 12))
    for o in p:
        if o.name.startswith(f"{pfx}_Bead"):
            finish(o, 1.0, rot90=True, angle=50)
        else:
            finish(o, 1.0)
    for o in vert:
        finish(o, 1.0, rot90=True)
    return p + vert


def signal(M):
    o = cyl_between("SG_Lens", (-0.085, SIGNAL_Y, 0.012), (0.085, SIGNAL_Y, 0.012), 0.04, M["signal"], 40)
    finish(o, 4.0, angle=50)
    return [o]


# ============================== 扉の奥の暗い廊下 ==============================

def hallway(M):
    L, hw, h = HALL_L, HALL_HW, HALL_H
    zf = 0.1                                       # 壁の厚みへ少し食い込ませて隙間を作らない
    p = []

    def put(objs, uv=1.0, rot90=False):
        for o in objs:
            finish(o, uv, rot90=rot90)
        p.extend(objs)

    put([span("HW_Floor", -hw - 0.08, hw + 0.08, -0.08, 0.0, -L - 0.1, zf, M["oldfloor"], 0)], 0.85, rot90=True)
    put([span("HW_Ceil", -hw - 0.08, hw + 0.08, h, h + 0.06, -L - 0.1, zf, M["oldceil"], 0)], 1.0)
    for s in (-1, 1):
        put([span(f"HW_Wall{s}", *sorted((s * hw, s * (hw + 0.08))), 0.0, h + 0.03, -L - 0.1, zf, M["oldwall"], 0)], 0.4)
        # 腰壁（鏡板は省き、框と板だけの簡素なもの）
        wx = lambda d: s * (hw - d)                                           # noqa: E731
        put([span(f"HW_WBack{s}", *sorted((wx(0.01), wx(0.0))), 0.12, 0.86, -L - 0.1, zf, M["oldwains"], 0.001),
             span(f"HW_WRailB{s}", *sorted((wx(0.024), wx(0.01))), 0.12, 0.19, -L - 0.1, zf, M["oldwains"], 0.003),
             span(f"HW_WRailT{s}", *sorted((wx(0.024), wx(0.01))), 0.8, 0.86, -L - 0.1, zf, M["oldwains"], 0.003)], 1.0, rot90=True)
        put([span(f"HW_WStile{s}_{k}", *sorted((wx(0.024), wx(0.01))), 0.19, 0.8, -0.6 - k * 1.1 - 0.035,
                  -0.6 - k * 1.1 + 0.035, M["oldwains"], 0.003) for k in range(7)], 1.0)
        # 幅木と腰の見切りは側面の扉の額縁で切る
        dz = SIDE_DOORS[s]
        for i, (za, zb) in enumerate(((-L - 0.1, dz - 0.6), (dz + 0.6, zf))):
            put([extrude(f"HW_Base{s}{i}", BASE_PROF, za, zb, lambda d, y, z: (s * (hw - d), y, z), M["oldwains"]),
                 extrude(f"HW_Rail{s}{i}", RAIL_PROF, za, zb, lambda d, y, z: (s * (hw - d), y, z), M["oldwains"])],
                1.0, rot90=True)
        put([span(f"HW_Crown{s}", *sorted((s * (hw - 0.05), s * hw)), h - 0.05, h, -L - 0.1, zf, M["oldwains"], 0.008)],
            1.0, rot90=True)
    # 側面の扉（奥にも部屋が続いている気配。閉じたまま）
    for s, zc in SIDE_DOORS.items():
        side = []
        for sz in (-1, 1):
            side.append(span(f"HW_SCasing{s}{sz}", *sorted((s * (hw - 0.045), s * hw)), 0.0, 2.2,
                             *sorted((zc + sz * 0.49, zc + sz * 0.6)), M["oldwains"], 0.004))
        side.append(span(f"HW_SCasingT{s}", *sorted((s * (hw - 0.045), s * hw)), 2.1, 2.2, zc - 0.6, zc + 0.6, M["oldwains"], 0.004))
        side.append(span(f"HW_SLeaf{s}", *sorted((s * (hw - 0.03), s * (hw - 0.005))), 0.02, 2.1, zc - 0.47, zc + 0.47,
                         M["olddoor"], 0.003))
        for i, (a, b, c, d) in enumerate(((-0.36, -0.04, 1.15, 1.95), (0.04, 0.36, 1.15, 1.95),
                                          (-0.36, -0.04, 0.22, 0.95), (0.04, 0.36, 0.22, 0.95))):
            side.append(span(f"HW_SPanel{s}{i}", *sorted((s * (hw - 0.038), s * (hw - 0.03))), c, d, zc + a, zc + b,
                             M["olddoor"], 0.006))
        side.append(cyl_between(f"HW_SKnob{s}", (s * (hw - 0.03), 1.0, zc + 0.37), (s * (hw - 0.07), 1.0, zc + 0.37),
                                0.022, M["brass"], 20))
        put(side, 1.0, rot90=True)
    # 天井の、もう灯らない照明
    for i, zc in enumerate((-2.2, -5.4)):
        sh = lathe(f"HW_Shade{i}", (0.0, 1.96, zc), [(0.16, 0.0), (0.15, 0.03), (0.1, 0.1), (0.04, 0.155), (0.012, 0.165)],
                   M["shade"], 32, cap_top=False, cap_bottom=False)
        sol = sh.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.003
        put([cyl_between(f"HW_Cord{i}", (0.0, h, zc), (0.0, 2.12, zc), 0.004, M["cord"], 8), sh], 1.0)
    # 突き当たり（光の届かない闇）
    end = quad("HW_Void", [(hw + 0.1, -0.1, -L), (-hw - 0.1, -0.1, -L), (-hw - 0.1, h + 0.1, -L), (hw + 0.1, h + 0.1, -L)],
               M["void"])
    hq.face_toward(end, (0, 0, 1))
    p.append(end)
    return p


def hallway_fog(M):
    """半透明の黒い板を奥へ等間隔に重ねる：1枚ごとに光が減り、奥ほど暗くなる"""
    hw, h = HALL_HW + 0.02, HALL_H + 0.02
    out = []
    for k in range(24):
        z = -0.5 - 0.33 * k
        q = quad(f"HF_Card{k}", [(hw, -0.02, z), (-hw, -0.02, z), (-hw, h, z), (hw, h, z)], M["fog"])
        hq.face_toward(q, (0, 0, 1))
        out.append(q)
    return out


PIECES = {"Shell": shell, "DoorFrame": door_frame, "DoorLeaf": door_leaf, "Signal": signal,
          "Hallway": hallway, "HallwayFog": hallway_fog}
