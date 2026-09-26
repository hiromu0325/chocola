"""
MAIN CORE ROOM（core_main：幅12 x 奥行12 x 天井5.0）を品質重視で作る。
終章の大空間：磨いた黒い石の床（金の目地）・畝のある暗い壁と金の縦の光・天井のコアの真上の光の輪と放射状の梁。
中央奥に巨大な球形コア：段付きの台座（金の象嵌・ボルト・銘板）・4本の支持腕・半透明の殻と経緯の格子・
金色に脈打つ芯・天井へ昇るケーブル束。床の金の輪と放射状の光の線、6本の光の柱（ケーブルを巻いた暗い柱）、
壁際のケーブルトレイ、南に二段の段と祭壇の端末（再生メッセージの画面）。

  Shell      部屋の原点。床・壁・天井・照明・床の光の輪・光の柱・壁際のケーブル・段
  Core       部屋の原点。球形コア一式
  Altar      祭壇の端末（ユニットの原点 = ビルダーの AltarTerminal、画面は -Z を向く）
  Door / Breaker / Lever
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import hq
from hq import U, span, lathe, cyl, cyl_between, pipe, quad, profile_z, finish, join
import train_room
import core_ante_room

W, D, H = 12.0, 12.0, 5.0
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
CORE = (0.0, 2.6, 2.5)            # 球の中心（x, y, z）
R_SHELL, R_INNER = 1.8, 1.3
LIGHT_ZS = (-4.0, 0.0, 4.0)


def pillars():
    """光の柱（ビルダーの CablePillar：角度 60i+30、半径 4.2、奥行きは 0.7 倍の楕円）"""
    out = []
    for i in range(6):
        a = math.radians(i * 60 + 30)
        out.append((math.sin(a) * 4.2, 2.5 + math.cos(a) * 4.2 * 0.7))
    return out


def mats():
    M = {}
    M["floor"] = hq.mat("CMN_Stone", (1, 1, 1), 0.15, tex="dark_stone.png")
    M["wall"] = hq.mat("CMN_WallRib", (1, 1, 1), 0.6, tex="wall_rib.png")
    M["ceil"] = hq.mat("CMN_Ceiling", (0.05, 0.05, 0.06), 0.9)
    M["metal"] = hq.mat("CMN_MetalDark", (0.1, 0.1, 0.12), 0.3, 0.8)
    M["brass"] = hq.mat("CMN_Brass", (0.75, 0.58, 0.32), 0.25, 1.0)
    M["gold"] = hq.mat("CMN_GoldLight", (0.6, 0.45, 0.2), 0.3, emit=(1.0, 0.8, 0.4), emit_strength=5.0)
    M["blue"] = hq.mat("CMN_BlueLight", (0.15, 0.3, 0.5), 0.3, emit=(0.35, 0.6, 0.9), emit_strength=4.0)
    M["shell"] = hq.mat("CMN_CoreShell", (0.8, 0.65, 0.4), 0.05, alpha=0.2)
    M["inner"] = hq.mat("CMN_CoreGold", (1, 1, 1), 0.3, tex="core_gold.png", emit=(1, 1, 1), emit_strength=2.5)
    M["cable"] = hq.mat("CMN_Cable", (0.03, 0.03, 0.035), 0.35)
    M["plate"] = hq.mat("CMN_Plate", (1, 1, 1), 0.4, tex="plate.png")
    M["screen"] = hq.mat("CMN_ScreenMessage", (1, 1, 1), 0.2, tex="screen_message.png", emit=(1, 1, 1), emit_strength=1.0)
    M["black"] = hq.mat("CMN_Black", (0.02, 0.02, 0.025), 0.4)
    M["down"] = hq.mat("CMN_Downlight", (1.0, 0.95, 0.85), 0.3, emit=(1.0, 0.9, 0.7), emit_strength=5.0)
    M["lever"] = hq.mat("CMN_LeverRed", (0.75, 0.12, 0.1), 0.45)
    M["hazard"] = hq.mat("CMN_Hazard", (1, 1, 1), 0.5, tex="../../lab/tex/hazard.png")
    M["steelplate"] = hq.mat("CMN_Steel", (1, 1, 1), 0.5, 0.3, tex="../../core_ante/tex/steel_plate.png")
    M["wglass"] = hq.mat("CMN_WiredGlass", (0.08, 0.1, 0.11), 0.05)
    M["chrome"] = hq.mat("CMN_Chrome", (0.85, 0.85, 0.86), 0.2, 1.0)
    return M


def _outward(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()


def sphere(name, c, r, mat, seg=64, rings=32, uv=False):
    """球（回転体）。uv=True で経度・緯度の UV（テクスチャを巻く）"""
    prof = [(r * math.sin(math.pi * k / rings), r * (1 - math.cos(math.pi * k / rings))) for k in range(rings + 1)]
    o = lathe(name, (c[0], c[1] - r, c[2]), prof, mat, seg)
    if uv:
        me = o.data
        uvl = (me.uv_layers[0] if me.uv_layers else me.uv_layers.new(name="UVMap")).data
        cc = U(*c)
        for poly in me.polygons:
            us = []
            for li in poly.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                us.append((math.atan2(co.y - cc.y, co.x - cc.x) / (2 * math.pi)) % 1.0)
            if max(us) - min(us) > 0.5:
                us = [u + 1.0 if u < 0.5 else u for u in us]
            for li, u in zip(poly.loop_indices, us):
                co = me.vertices[me.loops[li].vertex_index].co
                v = 0.5 + math.asin(max(-1.0, min(1.0, (co.z - cc.z) / r))) / math.pi
                uvl[li].uv = (u * 2.0, v)
    return o


# ============================== 外殻 ==============================

def shell(M):
    out = []
    out.append(finish(span("MS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["floor"], bev=0), 0.5))
    walls = []
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"MS_WallNS{zs}", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["wall"])
    for xs in (-1, 1):
        walls.append(span(f"MS_WallEW{xs}", *sorted((xs * HW, xs * HW0)), 0.0, H, -HD0, HD0, M["wall"], bev=0))
    out += [finish(o, 1.0, angle=30) for o in walls]
    out.append(finish(span("MS_Ceil", -HW0, HW0, H, H + 0.1, -HD0, HD0, M["ceil"], bev=0), 1.0))
    p = []
    # 壁の金の縦の光（2m おき）と黒い巾木
    for xs in (-1, 1):
        for z in (-4.0, -2.0, 0.0, 2.0, 4.0):
            if xs > 0 and abs(z) < 0.5:
                continue                                  # 東の壁の中央は配電盤
            a, b = sorted((xs * HW, xs * (HW - 0.02)))
            p.append(span(f"MS_Rib{xs}{z}", a, b, 0.3, H - 0.3, z - 0.03, z + 0.03, M["metal"], 0.004))
            a2, b2 = sorted((xs * (HW - 0.02), xs * (HW - 0.024)))
            p.append(span(f"MS_RibLight{xs}{z}", a2, b2, 0.35, H - 0.35, z - 0.008, z + 0.008, M["gold"], 0))
        p.append(span(f"MS_Base{xs}", *sorted((xs * HW, xs * (HW - 0.015))), 0.0, 0.15, -HD, HD, M["black"], 0.004))
    for zs in (-1, 1):
        for x in (-4.0, -2.0, 2.0, 4.0):
            a, b = sorted((zs * HD, zs * (HD - 0.02)))
            p.append(span(f"MS_RibNS{zs}{x}", x - 0.03, x + 0.03, 0.3, H - 0.3, a, b, M["metal"], 0.004))
            a2, b2 = sorted((zs * (HD - 0.02), zs * (HD - 0.024)))
            p.append(span(f"MS_RibLightNS{zs}{x}", x - 0.008, x + 0.008, 0.35, H - 0.35, a2, b2, M["gold"], 0))
        for (x0, x1) in ((-HW, -DOOR_HALF - 0.08), (DOOR_HALF + 0.08, HW)):
            p.append(span(f"MS_BaseNS{zs}{x0:.0f}", x0, x1, 0.0, 0.15, *sorted((zs * HD, zs * (HD - 0.015))), M["black"], 0.004))
        zin = zs * HD
        for xs in (-1, 1):
            p.append(span(f"MS_Frame{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.1))), 0, DOOR_H + 0.1, *sorted((zin, zin - zs * 0.04)), M["brass"], 0.006))
            p.append(span(f"MS_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), M["metal"], 0.002))
        p.append(span(f"MS_FrameT{zs}", -DOOR_HALF - 0.1, DOOR_HALF + 0.1, DOOR_H, DOOR_H + 0.1, *sorted((zin, zin - zs * 0.04)), M["brass"], 0.006))
        p.append(span(f"MS_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), M["metal"], 0.002))
    # 天井：コアの真上の光の輪と、放射状の梁
    cx, cy, cz = CORE
    p.append(lathe("MS_CeilRing", (cx, H - 0.12, cz), [(2.6, 0.0), (2.9, 0.0), (2.9, 0.12), (2.6, 0.12), (2.6, 0.0)], M["metal"], 96, cap_top=False, cap_bottom=False))
    p.append(lathe("MS_CeilRingLight", (cx, H - 0.125, cz), [(2.66, 0.0), (2.84, 0.0), (2.84, 0.004), (2.66, 0.004), (2.66, 0.0)], M["gold"], 96, cap_top=False, cap_bottom=False))
    for k in range(8):
        a = math.pi * 2 * k / 8
        x0, z0 = cx + math.cos(a) * 2.9, cz + math.sin(a) * 2.9
        x1, z1 = cx + math.cos(a) * 7.0, cz + math.sin(a) * 7.0
        x1 = max(-HW, min(HW, x1)); z1 = max(-HD, min(HD, z1))
        p.append(cyl_between(f"MS_Beam{k}", (x0, H - 0.08, z0), (x1, H - 0.08, z1), 0.06, M["metal"], 12))
    for i, z in enumerate(LIGHT_ZS):
        if z > 2.0:
            continue                                      # 北の照明の上は光の輪
        p.append(lathe(f"MS_Down{i}", (0, H - 0.03, z), [(0.0, 0.0), (0.12, 0.0), (0.14, 0.03)], M["down"], 32, cap_top=False))
        p.append(lathe(f"MS_DownRim{i}", (0, H - 0.01, z), [(0.14, 0.0), (0.18, 0.0), (0.18, 0.01)], M["brass"], 32, cap_top=False, cap_bottom=False))
    # 床の金の輪（ビルダーの FloorRingSeg：半径 2.6）と、柱への光の線
    p.append(lathe("MS_FloorRing", (cx, 0.0, cz), [(2.55, 0.0), (2.65, 0.0), (2.65, 0.005), (2.55, 0.005), (2.55, 0.0)], M["gold"], 128, cap_top=False, cap_bottom=False))
    p.append(lathe("MS_FloorRingRim", (cx, 0.0, cz), [(2.5, 0.0), (2.7, 0.0), (2.7, 0.003), (2.5, 0.003)], M["brass"], 128, cap_top=False, cap_bottom=False))
    for (px, pz) in pillars():
        dx, dz = px - cx, pz - cz
        L = math.hypot(dx, dz)
        ux, uz = dx / L, dz / L
        a0 = (cx + ux * 2.7, 0.004, cz + uz * 2.7)
        a1 = (px - ux * 0.25, 0.004, pz - uz * 0.25)
        nx, nz = -uz * 0.012, ux * 0.012
        q = quad("MS_Line", [(a0[0] - nx, 0.004, a0[2] - nz), (a1[0] - nx, 0.004, a1[2] - nz), (a1[0] + nx, 0.004, a1[2] + nz), (a0[0] + nx, 0.004, a0[2] + nz)], M["gold"])
        hq.face_toward(q, (0, 1, 0))
        p.append(q)
    out += [finish(o, 1.0, angle=45) for o in p]
    out += light_pillars(M)
    out += wall_cables(M)
    out += steps(M)
    return out


def light_pillars(M):
    """光の柱6本：暗い円柱に3本の青い光の線、螺旋に巻いたケーブル、上下の襟"""
    p = []
    for i, (px, pz) in enumerate(pillars()):
        p.append(cyl(f"LP_Core{i}", (px, 0.0, pz), H, 0.16, M["metal"], 24))
        for k in range(3):
            a = math.pi * 2 * k / 3 + i
            x, z = px + math.cos(a) * 0.162, pz + math.sin(a) * 0.162
            p.append(cyl(f"LP_Line{i}{k}", (x, 0.3, z), H - 0.6, 0.012, M["blue"], 8))
        for y0, y1 in ((0.0, 0.3), (H - 0.3, H)):
            p.append(lathe(f"LP_Collar{i}{y0}", (px, y0, pz), [(0.22, 0.0), (0.24, 0.02), (0.24, y1 - y0 - 0.02), (0.22, y1 - y0)], M["brass"], 32, cap_top=False, cap_bottom=False))
        pts = []
        for s in range(40):
            t = s / 39
            a = t * math.pi * 2 * 5 + i
            pts.append((px + math.cos(a) * 0.19, 0.35 + t * (H - 0.7), pz + math.sin(a) * 0.19))
        p.append(pipe(f"LP_Wrap{i}", pts, 0.02, M["cable"], 0.02, 8))
    return [finish(o, 2.0, angle=50) for o in p]


def wall_cables(M):
    """東西の壁際の床のケーブルトレイ（ビルダーの WallCables：x ±(hw-0.2)、高さ 0.4 以下）"""
    p = []
    for xs in (-1, 1):
        x = xs * (HW0 - 0.2)
        a, b = sorted((x - 0.13, x + 0.13))
        p.append(span(f"WC_Tray{xs}", a, b, 0.0, 0.05, -HD + 0.2, HD - 0.2, M["metal"], 0.004))
        for side in (a, b - 0.01):
            p.append(span(f"WC_TrayWall{xs}{side:.1f}", side, side + 0.01, 0.0, 0.14, -HD + 0.2, HD - 0.2, M["metal"], 0.002))
        rnd = random.Random(2600 + xs)
        for k in range(7):
            xx = x - 0.09 + (k % 4) * 0.06
            yy = 0.07 + (k // 4) * 0.06
            pts = [(xx, yy, -HD + 0.25)]
            for zz in (-3.0, 0.0, 3.0):
                pts.append((xx + rnd.uniform(-0.01, 0.01), yy, zz))
            pts.append((xx, yy, HD - 0.25))
            p.append(pipe(f"WC_Cable{xs}{k}", pts, 0.025, M["cable"], 0.3, 8))
    return [finish(o, 2.0, angle=50) for o in p]


def steps(M):
    """祭壇の二段の段（ビルダーの Step1 / Step2）。縁に金の線"""
    p = []
    for (w, d, y0, y1, nm) in ((3.0, 2.2, 0.0, 0.1, "S1"), (2.2, 1.6, 0.1, 0.2, "S2")):
        z = -0.8
        p.append(span(f"ST_{nm}", -w / 2, w / 2, y0, y1, z - d / 2, z + d / 2, M["metal"], 0.01, 3))
        p.append(span(f"ST_{nm}Edge", -w / 2 + 0.05, w / 2 - 0.05, y1 - 0.02, y1 - 0.012, z - d / 2 - 0.002, z - d / 2 + 0.004, M["gold"], 0))
    return [finish(o, 1.0, angle=40) for o in p]


# ============================== コア ==============================

def core(M):
    cx, cy, cz = CORE
    p, flat = [], []
    # 台座（ビルダーの CoreCradle：r 2.1、高さ 0.7）。段と金の象嵌、ボルト
    p.append(lathe("CC_Cradle", (cx, 0.0, cz), [(2.1, 0.0), (2.1, 0.3), (1.95, 0.35), (1.95, 0.62), (1.7, 0.7), (1.2, 0.7)], M["metal"], 96, cap_bottom=False))
    p.append(lathe("CC_Inlay", (cx, 0.3, cz), [(2.101, 0.0), (2.101, 0.03)], M["gold"], 96, cap_top=False, cap_bottom=False))
    for k in range(32):
        a = math.pi * 2 * k / 32
        p.append(cyl_between(f"CC_Bolt{k}", (cx + math.cos(a) * 1.95, 0.5, cz + math.sin(a) * 1.95), (cx + math.cos(a) * 1.97, 0.5, cz + math.sin(a) * 1.97), 0.02, M["brass"], 8))
    # 銘板（南）
    pz = cz - 1.952
    pl = quad("CC_Plate", [(cx - 0.6, 0.38, pz), (cx + 0.6, 0.38, pz), (cx + 0.6, 0.62, pz), (cx - 0.6, 0.62, pz)], M["plate"])
    hq.face_toward(pl, (0, 0, -1))
    flat.append(pl)
    # 支持腕4本（台座から殻の赤道の少し下を抱える）
    for k in range(4):
        a = math.pi / 4 + math.pi / 2 * k
        ca, sa = math.cos(a), math.sin(a)
        pts = [(cx + ca * 1.6, 0.7, cz + sa * 1.6), (cx + ca * 1.95, 1.2, cz + sa * 1.95), (cx + ca * 1.95, 2.2, cz + sa * 1.95), (cx + ca * 1.83, 2.9, cz + sa * 1.83)]
        p.append(pipe(f"CC_Arm{k}", pts, 0.07, M["metal"], 0.3, 16))
        p.append(sphere(f"CC_Joint{k}", (cx + ca * 1.83, 2.9, cz + sa * 1.83), 0.1, M["brass"], 16, 8))
    # 殻（r 1.8）と経緯の格子
    sh = sphere("CC_Shell", (cx, cy, cz), R_SHELL, M["shell"], 96, 48)
    s = sh.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.01
    finish(sh, 1.0, angle=180)
    _outward(sh)
    flat.append(sh)
    for k in range(12):
        a = math.pi * k / 12
        ring = lathe(f"CC_Meridian{k}", (0, 0, 0), [(R_SHELL + 0.015 + 0.015 * math.cos(t / 6 * math.pi * 2), 0.015 * math.sin(t / 6 * math.pi * 2)) for t in range(7)],
                     M["brass"], 96, cap_top=False, cap_bottom=False)
        ring.data.transform(Matrix.Translation(U(cx, cy, cz)) @ Matrix.Rotation(a, 4, "Z") @ Matrix.Rotation(math.radians(90), 4, "X"))
        p.append(ring)
    for lat in (-50, -25, 0, 25, 50):
        la = math.radians(lat)
        rr = (R_SHELL + 0.015) * math.cos(la)
        yy = cy + (R_SHELL + 0.015) * math.sin(la)
        p.append(lathe(f"CC_Parallel{lat}", (cx, yy - 0.015, cz), [(rr, 0.0), (rr + 0.02, 0.0), (rr + 0.02, 0.03), (rr, 0.03), (rr, 0.0)], M["brass"], 96, cap_top=False, cap_bottom=False))
    # 金色の芯（r 1.3）と、芯の周りを巡る光の粒の帯
    inner = sphere("CC_Inner", (cx, cy, cz), R_INNER, M["inner"], 96, 48, uv=True)
    finish(inner, keep_uv=True, angle=60)
    flat.append(inner)
    for k in range(3):
        tilt = math.radians(20 + k * 35)
        orb = lathe(f"CC_Orbit{k}", (0, 0, 0), [(1.52 + 0.006 * math.cos(t / 6 * math.pi * 2), 0.006 * math.sin(t / 6 * math.pi * 2)) for t in range(7)],
                    M["gold"], 96, cap_top=False, cap_bottom=False)
        orb.data.transform(Matrix.Translation(U(cx, cy, cz)) @ Matrix.Rotation(k * 1.1, 4, "Z") @ Matrix.Rotation(tilt, 4, "X"))
        p.append(orb)
    # 天井へ昇るケーブル束（殻の頂から光の輪へ）
    top = (cx, cy + R_SHELL, cz)
    p.append(lathe("CC_TopCap", (cx, cy + R_SHELL - 0.08, cz), [(0.35, 0.0), (0.3, 0.12), (0.18, 0.18), (0.18, 0.25)], M["metal"], 48))
    for k in range(5):
        a = math.pi * 2 * k / 5
        pts = [(cx + math.cos(a) * 0.1, top[1] + 0.2, cz + math.sin(a) * 0.1), (cx + math.cos(a) * 0.3, top[1] + 0.45, cz + math.sin(a) * 0.3),
               (cx + math.cos(a) * 1.6, H - 0.3, cz + math.sin(a) * 1.6), (cx + math.cos(a) * 2.7, H - 0.05, cz + math.sin(a) * 2.7)]
        p.append(pipe(f"CC_UpCable{k}", pts, 0.05, M["cable"], 0.3, 12))
    return [finish(o, 1.0, angle=45) for o in p] + flat


# ============================== 祭壇 ==============================

def altar(M):
    """祭壇の端末（原点 = ビルダーの AltarTerminal（段の上 y 0.2）。Stand 0.9 x 1.0 x 0.6、画面は y 1.15 で -Z を向く）"""
    p, flat = [], []
    p.append(lathe("AL_Foot", (0, 0.0, 0), [(0.42, 0.0), (0.42, 0.06), (0.36, 0.1), (0.2, 0.12)], M["metal"], 48))
    prof = [(-0.3, 0.1), (0.3, 0.1), (0.22, 0.9), (-0.22, 0.9)]
    body = profile_z("AL_Body", prof, -0.2, 0.2, M["metal"])
    p.append(body)
    p.append(span("AL_Strip", -0.2, 0.2, 0.45, 0.47, -0.205, -0.19, M["gold"], 0))
    # 画面の枠（直立、-Z を向く）
    p.append(span("AL_Frame", -0.4, 0.4, 0.88, 1.42, -0.04, 0.04, M["black"], 0.015, 3))
    p.append(span("AL_FrameEdge", -0.41, 0.41, 0.87, 0.9, -0.05, 0.05, M["brass"], 0.006))
    scr = quad("AL_Screen", [(-0.37, 0.92, -0.041), (0.37, 0.92, -0.041), (0.37, 1.39, -0.041), (-0.37, 1.39, -0.041)], M["screen"])
    hq.face_toward(scr, (0, 0, -1))
    flat.append(scr)
    return [join([finish(o, 1.0, angle=40) for o in p] + flat, "CoreMain_Altar")]


def door(M):
    Mc = {"steel": M["steelplate"], "dark": M["metal"], "glass": M["wglass"], "chrome": M["chrome"], "hazard": M["hazard"]}
    return [join(core_ante_room.door(Mc), "CoreMain_Door")]


def _breaker_mats(M):
    return {"mel": M["metal"], "grille": M["black"], "hazard": M["hazard"], "sus": M["brass"], "rubber": M["black"], "lever": M["lever"]}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.01), "CoreMain_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "CoreMain_Lever")]


PIECES = {"Shell": shell, "Core": core, "Altar": altar, "Door": door, "Breaker": breaker, "Lever": lever}
