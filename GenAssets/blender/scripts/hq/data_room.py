"""
データ管理室（data_room：幅9 x 奥行10 x 天井3.0）を品質重視で作る。
フリーアクセス床（通路は通気口付きのパネル）・白い壁パネル・吸音板の天井と埋込み照明・
サーバーラック8台（オープンラックに1U/2Uの機器・LED・パッチパネルと配線・ラック名札）・
ラック列の上のケーブルラダー・西の壁一面の紙ファイルのキャビネット（引き出し1段が開いている・付箋）・
精密空調機・消火器・管理者デスク（ベージュのCRT・キーボード・眼鏡・書類・アームライト）。

  Shell      部屋の原点。床・壁・天井・照明・ラダー・空調機・消火器・掲示・時計
  Interior   部屋の原点。サーバーラック8台・ファイルキャビネット4台
  Desk       管理者デスク（ユニットの原点、天板の上面 0.75、座る側 = -Z）と CRT・小物
  Minutes / KMemo / Scribble / Glasses   資料の見た目（箱の中心が原点）
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
import lab_room

W, D, H = 9.0, 10.0, 3.0
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
RACK_ZS = [-HD0 + 2.4 + i * 1.7 for i in range(4)]         # -2.6, -0.9, 0.8, 2.5
CAB_ZS = [-HD0 + 1.8 + i * 1.7 for i in range(4)]          # -3.2, -1.5, 0.2, 1.9
LIGHT_ZS = (-2.5, 2.5)


def mats():
    M = {}
    M["floor"] = hq.mat("DAT_AccessFloor", (1, 1, 1), 0.5, tex="access_floor.png")
    M["perf"] = hq.mat("DAT_AccessFloorPerf", (1, 1, 1), 0.5, tex="access_floor_perf.png")
    M["wall"] = hq.mat("DAT_Panel", (1, 1, 1), 0.7, tex="../../analysis/tex/clean_panel.png")
    M["tile"] = hq.mat("DAT_CeilingTile", (1, 1, 1), 0.9, tex="../../lab/tex/ceiling_tile.png")
    M["grid"] = hq.mat("DAT_Grid", (0.93, 0.93, 0.93), 0.4)
    M["led"] = hq.mat("DAT_LedPanel", (0.9, 0.94, 1.0), 0.3, emit=(0.85, 0.92, 1.0), emit_strength=5.0)
    M["cove"] = hq.mat("DAT_Cove", (0.25, 0.27, 0.29), 0.6)
    M["rack"] = hq.mat("DAT_RackBlack", (0.05, 0.055, 0.06), 0.45, 0.4)
    M["rail"] = hq.mat("DAT_RackRail", (0.35, 0.36, 0.38), 0.4, 0.8)
    M["fronts"] = hq.mat("DAT_ServerFronts", (1, 1, 1), 0.4, tex="server_fronts.png")
    M["blank"] = hq.mat("DAT_BlankPanel", (0.08, 0.085, 0.09), 0.5, 0.3)
    M["led_g"] = hq.mat("DAT_LedGreen", (0.1, 0.4, 0.2), 0.3, emit=(0.3, 1.0, 0.5), emit_strength=5.0)
    M["led_a"] = hq.mat("DAT_LedAmber", (0.4, 0.3, 0.1), 0.3, emit=(1.0, 0.6, 0.15), emit_strength=5.0)
    M["led_b"] = hq.mat("DAT_LedBlue", (0.1, 0.2, 0.4), 0.3, emit=(0.3, 0.6, 1.0), emit_strength=5.0)
    M["cab_y"] = hq.mat("DAT_CableYellow", (0.85, 0.7, 0.1), 0.5)
    M["cab_b"] = hq.mat("DAT_CableBlue", (0.15, 0.35, 0.75), 0.5)
    M["cab_k"] = hq.mat("DAT_CableBlack", (0.03, 0.03, 0.035), 0.5)
    M["cab_r"] = hq.mat("DAT_CableRed", (0.7, 0.12, 0.1), 0.5)
    M["galv"] = hq.mat("DAT_Galvanized", (1, 1, 1), 0.4, 0.8, tex="../../analysis/tex/galvanized.png")
    M["cabinet"] = hq.mat("DAT_CabinetGrey", (0.55, 0.57, 0.6), 0.45, 0.3)
    M["handle"] = hq.mat("DAT_Chrome", (0.85, 0.85, 0.86), 0.2, 1.0)
    M["labels"] = hq.mat("DAT_CabinetLabels", (1, 1, 1), 0.6, tex="cabinet_labels.png")
    M["folder"] = hq.mat("DAT_FolderGreen", (0.35, 0.5, 0.35), 0.7)
    M["folder2"] = hq.mat("DAT_FolderBuff", (0.8, 0.7, 0.5), 0.8)
    M["box"] = hq.mat("DAT_ArchiveBox", (0.72, 0.62, 0.46), 0.8)
    M["crac"] = hq.mat("DAT_CracWhite", (0.88, 0.89, 0.88), 0.45)
    M["vent"] = hq.mat("DAT_VentGrey", (0.35, 0.36, 0.38), 0.5, 0.4)
    M["red"] = hq.mat("DAT_ExtinguisherRed", (0.7, 0.06, 0.05), 0.35)
    M["black"] = hq.mat("DAT_Black", (0.03, 0.03, 0.035), 0.5)
    M["beige"] = hq.mat("DAT_CrtBeige", (0.74, 0.71, 0.63), 0.45)
    M["crt"] = hq.mat("DAT_CrtScreen", (1, 1, 1), 0.1, tex="crt_audit.png", emit=(0.3, 1.0, 0.5), emit_strength=1.0)
    M["desk"] = hq.mat("DAT_DeskGrey", (0.6, 0.62, 0.64), 0.45, 0.3)
    M["desktop"] = hq.mat("DAT_DeskTop", (0.78, 0.78, 0.74), 0.4)
    M["paper"] = hq.mat("DAT_PaperPlain", (0.93, 0.92, 0.88), 0.9)
    M["minutes"] = hq.mat("DAT_Minutes", (1, 1, 1), 0.9, tex="minutes.png")
    M["kmemo"] = hq.mat("DAT_KMemo", (1, 1, 1), 0.9, tex="kmemo.png")
    M["scribble"] = hq.mat("DAT_Scribble", (1, 1, 1), 0.9, tex="scribble.png")
    M["sign"] = hq.mat("DAT_SignRoom", (1, 1, 1), 0.5, tex="sign_room.png")
    M["racklabel"] = hq.mat("DAT_RackLabels", (1, 1, 1), 0.5, tex="rack_labels.png")
    M["lens"] = hq.mat("DAT_Lens", (0.8, 0.85, 0.9), 0.02, alpha=0.2)
    M["mug"] = hq.mat("DAT_Mug", (0.3, 0.42, 0.55), 0.3)
    M["clockface"] = hq.mat("DAT_ClockFace", (0.95, 0.95, 0.93), 0.3)
    M["lever"] = hq.mat("DAT_LeverRed", (0.75, 0.12, 0.1), 0.45)
    M["hazard"] = hq.mat("DAT_Hazard", (1, 1, 1), 0.5, tex="../../lab/tex/hazard.png")
    return M


def _outward(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()


# ============================== 外殻 ==============================

def shell(M):
    out = []
    out.append(finish(span("DS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["floor"], bev=0), 1 / 1.2))
    # 通路（ラック列の間）の通気口付きパネル（600角、市松に）
    for xi, x0 in enumerate((-1.2, 0.6)):
        k = -8
        while k < 8:
            z0 = k * 0.6
            if -HD < z0 and z0 + 0.6 < HD and (k + xi) % 2 == 0:
                q = quad(f"DS_Perf{x0}{k}", [(x0, 0.001, z0), (x0 + 0.6, 0.001, z0), (x0 + 0.6, 0.001, z0 + 0.6), (x0, 0.001, z0 + 0.6)], M["perf"],
                         uv=((0, 0), (0.5, 0), (0.5, 0.5), (0, 0.5)))
                hq.face_toward(q, (0, 1, 0))
                out.append(q)
            k += 1
    walls = []
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"DS_WallNS{zs}", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["wall"])
    for xs in (-1, 1):
        walls.append(span(f"DS_WallEW{xs}", *sorted((xs * HW, xs * HW0)), 0.0, H, -HD0, HD0, M["wall"], bev=0))
    out += [finish(o, 1.0, angle=30) for o in walls]
    ce = span("DS_Ceil", -HW0, HW0, H, H + 0.1, -HD0, HD0, M["tile"], bev=0)
    finish(ce, 1 / 0.6)
    out.append(ce)
    g = []
    x = -HW + 0.6
    while x < HW - 0.1:
        g.append(span(f"DS_GridX{x:.1f}", x - 0.012, x + 0.012, H - 0.012, H, -HD, HD, M["grid"], 0.002)); x += 0.6
    z = -HD + 0.6
    while z < HD - 0.1:
        g.append(span(f"DS_GridZ{z:.1f}", -HW, HW, H - 0.012, H, z - 0.012, z + 0.012, M["grid"], 0.002)); z += 0.6
    for i, zc in enumerate(LIGHT_ZS):
        g.append(span(f"DS_LightFrame{i}", -0.62, 0.62, H - 0.02, H - 0.012, zc - 0.32, zc + 0.32, M["crac"], 0.004))
        g.append(span(f"DS_LightPanel{i}", -0.58, 0.58, H - 0.024, H - 0.019, zc - 0.28, zc + 0.28, M["led"], 0.002))
    out += [finish(o, 2.0, angle=40) for o in g]
    # 巾木・扉の枠
    t = []
    for zs in (-1, 1):
        for (x0, x1) in ((-HW, -DOOR_HALF - 0.06), (DOOR_HALF + 0.06, HW)):
            t.append(span(f"DT_Cove{zs}{x0:.0f}", x0, x1, 0, 0.1, *sorted((zs * HD, zs * (HD - 0.012))), M["cove"], 0.003))
        zin = zs * HD
        for xs in (-1, 1):
            t.append(span(f"DT_Frame{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.05))), 0, DOOR_H + 0.05, *sorted((zin, zin - zs * 0.015)), M["rail"], 0.003))
            t.append(span(f"DT_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), M["rail"], 0.002))
        t.append(span(f"DT_FrameT{zs}", -DOOR_HALF - 0.05, DOOR_HALF + 0.05, DOOR_H, DOOR_H + 0.05, *sorted((zin, zin - zs * 0.015)), M["rail"], 0.003))
        t.append(span(f"DT_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), M["rail"], 0.002))
    for xs in (-1, 1):
        t.append(span(f"DT_CoveEW{xs}", *sorted((xs * HW, xs * (HW - 0.012))), 0, 0.1, -HD, HD, M["cove"], 0.003))
    out += [finish(o, 1.0, angle=40) for o in t]
    out += ladders(M)
    out += crac(M)
    out += wall_items(M)
    return out


def ladders(M):
    """ラック列の上のケーブルラダー（y=2.45）と吊りボルト"""
    p = []
    y = 2.45
    for xc in (-1.6, 1.6):
        for xx in (xc - 0.25, xc + 0.25):
            p.append(span(f"DL_Rail{xx}", xx - 0.01, xx + 0.01, y - 0.06, y, -HD + 0.3, HD - 0.3, M["galv"], 0.003))
        z = -HD + 0.45
        while z < HD - 0.3:
            p.append(span(f"DL_Rung{xc}{z:.1f}", xc - 0.25, xc + 0.25, y - 0.05, y - 0.03, z - 0.012, z + 0.012, M["galv"], 0.002))
            z += 0.3
        z = -HD + 0.8
        while z < HD - 0.3:
            for xx in (xc - 0.28, xc + 0.28):
                p.append(cyl(f"DL_Rod{xc}{z:.1f}{xx}", (xx, y - 0.08, z), H - (y - 0.08), 0.006, M["handle"], 8))
            p.append(span(f"DL_Strut{xc}{z:.1f}", xc - 0.32, xc + 0.32, y - 0.1, y - 0.06, z - 0.02, z + 0.02, M["rail"], 0.003))
            z += 1.5
        # ラダーの上のケーブル束
        rnd = random.Random(int(xc * 10) + 1600)
        for k in range(10):
            xx = xc - 0.2 + (k % 5) * 0.1
            yy = y - 0.02 + (k // 5) * 0.025
            m = [M["cab_y"], M["cab_b"], M["cab_k"], M["cab_k"], M["cab_r"]][k % 5]
            pts = [(xx, yy, -HD + 0.35)]
            for zz in (-2.5, 0.0, 2.5):
                pts.append((xx + rnd.uniform(-0.02, 0.02), yy + 0.005, zz))
            pts.append((xx, yy, HD - 0.35))
            p.append(pipe(f"DL_Cable{xc}{k}", pts, 0.009, m, 0.3, 6))
    return [finish(o, 2.0, angle=40) for o in p]


def crac(M):
    """精密空調機（東の壁の南端）"""
    p = []
    x0, x1, z0, z1 = HW - 0.6, HW, -HD + 0.45, -HD + 1.35
    p.append(span("DC_Body", x0, x1, 0.0, 1.9, z0, z1, M["crac"], 0.02, 3))
    p.append(span("DC_Grille", x0 - 0.005, x0, 1.2, 1.8, z0 + 0.06, z1 - 0.06, M["vent"], 0.004))
    for k in range(14):
        y = 1.23 + k * 0.04
        p.append(span(f"DC_Louver{k}", x0 - 0.02, x0 - 0.005, y, y + 0.012, z0 + 0.07, z1 - 0.07, M["vent"], 0.002))
    p.append(span("DC_Panel", x0 - 0.004, x0, 0.9, 1.1, z0 + 0.1, z0 + 0.4, M["black"], 0.004))
    p.append(span("DC_Screen", x0 - 0.006, x0 - 0.004, 0.95, 1.05, z0 + 0.13, z0 + 0.3, M["led_g"], 0.002))
    p.append(span("DC_Kick", x0 - 0.004, x0, 0.0, 0.12, z0 + 0.02, z1 - 0.02, M["vent"], 0.004))
    return [finish(o, 1.0, angle=40) for o in p]


def wall_items(M):
    """消火器（南の扉の西）・北の壁の室名札・東の壁の時計"""
    p, flat = [], []
    ex, ez = -0.95, -HD + 0.15
    p.append(lathe("DE_Body", (ex, 0.02, ez), [(0.0, 0.0), (0.08, 0.0), (0.085, 0.02), (0.085, 0.45), (0.07, 0.5), (0.03, 0.53), (0.0, 0.53)], M["red"], 32))
    p.append(cyl("DE_Neck", (ex, 0.55, ez), 0.05, 0.02, M["black"], 12))
    p.append(span("DE_Lever", ex - 0.01, ex + 0.08, 0.6, 0.62, ez - 0.015, ez + 0.015, M["black"], 0.004))
    p.append(pipe("DE_Hose", [(ex + 0.02, 0.58, ez), (ex + 0.1, 0.5, ez + 0.03), (ex + 0.09, 0.2, ez + 0.05)], 0.008, M["black"], 0.05, 6))
    p.append(span("DE_Base", ex - 0.12, ex + 0.12, 0.0, 0.02, ez - 0.1, ez + 0.1, M["red"], 0.004))
    zn = HD - 0.004
    s = quad("DW_Sign", [(0.8, 1.6, zn), (1.6, 1.6, zn), (1.6, 1.8, zn), (0.8, 1.8, zn)], M["sign"])
    hq.face_toward(s, (0, 0, -1)); flat.append(s)
    cx, cy, cz = HW, 2.3, 3.4
    p.append(cyl_between("DK_Rim", (cx, cy, cz), (cx - 0.05, cy, cz), 0.17, M["rail"], 48))
    p.append(cyl_between("DK_Face", (cx - 0.05, cy, cz), (cx - 0.052, cy, cz), 0.15, M["clockface"], 48))
    for k in range(12):
        a = math.pi * 2 * k / 12
        y, z = cy + math.cos(a) * 0.125, cz + math.sin(a) * 0.125
        p.append(span(f"DK_Tick{k}", cx - 0.054, cx - 0.052, y - 0.012, y + 0.012, z - 0.004, z + 0.004, M["black"], 0))
    p.append(cyl_between("DK_H", (cx - 0.056, cy, cz), (cx - 0.056, cy + 0.05, cz + 0.04), 0.004, M["black"], 6))
    p.append(cyl_between("DK_M", (cx - 0.058, cy, cz), (cx - 0.058, cy - 0.03, cz - 0.1), 0.003, M["black"], 6))
    return [finish(o, 2.0, angle=40) for o in p] + flat


# ============================== ラック・キャビネット ==============================

def rack(M, cx, cz, face, idx):
    """オープンラック（奥行 0.8 = X、幅 1.0 = Z）。face=+1 で前面が +X"""
    rnd = random.Random(1700 + idx)
    p, flat = [], []
    xf, xb = cx + face * 0.4, cx - face * 0.4
    xa, xz = sorted((xf, xb))
    # 柱・天板・台輪・側板・背板
    for zz in (cz - 0.47, cz + 0.47):
        for xx in (cx + face * 0.37, cx - face * 0.37):
            p.append(span("RK_Post", xx - 0.025, xx + 0.025, 0.08, 2.05, zz - 0.025, zz + 0.025, M["rack"], 0.004))
    p.append(span("RK_Top", xa, xz, 2.05, 2.1, cz - 0.5, cz + 0.5, M["rack"], 0.008))
    p.append(span("RK_Plinth", xa + 0.02, xz - 0.02, 0.0, 0.08, cz - 0.48, cz + 0.48, M["rack"], 0.004))
    for zz in (cz - 0.5, cz + 0.49):
        p.append(span("RK_Side", xa + 0.03, xz - 0.03, 0.1, 2.03, zz, zz + 0.01, M["rack"], 0.004))
    p.append(span("RK_Back", *sorted((xb, xb + face * 0.012)), 0.1, 2.03, cz - 0.48, cz + 0.48, M["rack"], 0.003))
    # 取付レール（前面の内側）
    xr = cx + face * 0.345
    for zz in (cz - 0.44, cz + 0.44):
        p.append(span("RK_Rail", *sorted((xr, xr + face * 0.02)), 0.1, 2.02, zz - 0.02, zz + 0.02, M["rail"], 0.002))
    # 機器（下から積む）
    y = 0.12
    xs = xr + face * 0.021
    zl, zr = (cz - 0.44, cz + 0.44) if face > 0 else (cz + 0.44, cz - 0.44)     # 見る人の左 / 右
    while y < 1.9:
        r = rnd.random()
        hU = 0.089 if r < 0.55 else (0.178 if r < 0.8 else 0.0445)
        if y + hU > 1.9:
            break
        blank = rnd.random() < 0.15
        if blank:
            p.append(span("RK_Blank", *sorted((xs - face * 0.005, xs)), y + 0.002, y + hU - 0.002, cz - 0.44, cz + 0.44, M["blank"], 0.002))
        else:
            k = rnd.randrange(8)
            v0, v1 = 1 - (k + 1) / 8, 1 - k / 8
            q = quad("RK_Face", [(xs, y + 0.002, zl), (xs, y + 0.002, zr), (xs, y + hU - 0.002, zr), (xs, y + hU - 0.002, zl)], M["fronts"],
                     uv=((0, v0), (1, v0), (1, v1), (0, v1)))
            hq.face_toward(q, (face, 0, 0))
            flat.append(q)
            p.append(span("RK_Box", *sorted((xs - face * 0.6, xs - face * 0.002)), y + 0.003, y + hU - 0.003, cz - 0.42, cz + 0.42, M["blank"], 0))
            # LED（光る点）
            for j in range(rnd.randint(1, 4)):
                zz = cz + rnd.uniform(-0.35, 0.35)
                yy = y + rnd.uniform(0.012, hU - 0.012)
                m = rnd.choice([M["led_g"], M["led_g"], M["led_g"], M["led_a"], M["led_b"]])
                p.append(span("RK_Led", *sorted((xs, xs + face * 0.003)), yy - 0.004, yy + 0.004, zz - 0.004, zz + 0.004, m, 0))
        y += hU
    # 最上段のパッチパネルと、上へ抜ける配線
    yp = 1.93
    p.append(span("RK_Patch", *sorted((xs - face * 0.02, xs)), yp, yp + 0.044, cz - 0.44, cz + 0.44, M["blank"], 0.002))
    for j in range(12):
        zz = cz - 0.38 + j * 0.068
        m = [M["cab_b"], M["cab_y"], M["cab_b"], M["cab_r"]][j % 4]
        p.append(pipe(f"RK_Patchcord{j}", [(xs + face * 0.005, yp + 0.022, zz), (xs + face * 0.05, yp + 0.02, zz), (xs + face * 0.04, 2.0, zz + 0.02),
                                            (cx, 2.12, zz * 0.7 + cz * 0.3), (cx, 2.4, cz * 0.8 + zz * 0.2)], 0.004, m, 0.03, 5))
    # ラック名札（天板の前縁）
    li = idx
    u0 = (li % 8) / 8
    lab = quad("RK_Label", [(xf + face * 0.001, 2.055, zl * 0.2 + cz * 0.8), (xf + face * 0.001, 2.055, zr * 0.2 + cz * 0.8),
                            (xf + face * 0.001, 2.095, zr * 0.2 + cz * 0.8), (xf + face * 0.001, 2.095, zl * 0.2 + cz * 0.8)], M["racklabel"],
               uv=((u0, 0), (u0 + 0.125, 0), (u0 + 0.125, 1), (u0, 1)))
    hq.face_toward(lab, (face, 0, 0))
    flat.append(lab)
    return [finish(o, 2.0, angle=40) for o in p] + flat


def cabinet(M, cz, idx, open_drawer=-1):
    """ラテラルファイルキャビネット（西の壁、前面 = +X、幅 1.4、高さ 2.2、5段）"""
    p, flat = [], []
    x0, x1 = -HW, -HW + 0.5
    xf = x1
    z0, z1 = cz - 0.7, cz + 0.7
    p.append(span("FC_Body", x0, x1 - 0.02, 0.0, 2.2, z0, z1, M["cabinet"], 0.006))
    p.append(span("FC_Top", x0, x1, 2.18, 2.2, z0 - 0.005, z1 + 0.005, M["cabinet"], 0.004))
    for k in range(5):
        y0 = 0.08 + k * 0.42
        pull = 0.25 if k == open_drawer else 0.0
        fx = xf - 0.02 + pull
        p.append(span(f"FC_Drawer{k}", fx - 0.001, fx + 0.02, y0 + 0.005, y0 + 0.41, z0 + 0.01, z1 - 0.01, M["cabinet"], 0.006))
        p.append(span(f"FC_Pull{k}", fx + 0.02, fx + 0.035, y0 + 0.28, y0 + 0.3, cz - 0.2, cz + 0.2, M["handle"], 0.004))
        p.append(span(f"FC_LabelHolder{k}", fx + 0.02, fx + 0.024, y0 + 0.31, y0 + 0.37, cz - 0.08, cz + 0.08, M["handle"], 0.002))
        cell = (idx * 5 + k) % 16
        u0, v0 = (cell % 8) / 8, 1 - (cell // 8 + 1) / 2
        lq = quad(f"FC_Label{k}", [(fx + 0.0245, y0 + 0.315, cz - 0.07), (fx + 0.0245, y0 + 0.315, cz + 0.07), (fx + 0.0245, y0 + 0.365, cz + 0.07), (fx + 0.0245, y0 + 0.365, cz - 0.07)],
                  M["labels"], uv=((u0, v0), (u0 + 0.125, v0), (u0 + 0.125, v0 + 0.5), (u0, v0 + 0.5)))
        hq.face_toward(lq, (1, 0, 0))
        flat.append(lq)
        if k == open_drawer:
            # 開いた引き出しの中：吊り下げフォルダーの列
            for j in range(18):
                zz = z0 + 0.1 + j * 0.068
                m = M["folder"] if j % 3 else M["folder2"]
                p.append(span(f"FC_Folder{k}{j}", fx - 0.28, fx - 0.02, y0 + 0.08, y0 + 0.38 + (0.02 if j % 4 == 0 else 0), zz - 0.002, zz + 0.002, m, 0))
    # 上に積んだ保存箱
    if idx % 2 == 0:
        for j in range(2):
            p.append(span(f"FC_Box{j}", x0 + 0.03, x1 - 0.06, 2.2 + j * 0.28, 2.47 + j * 0.28, cz - 0.35 + j * 0.05, cz + 0.05 + j * 0.05, M["box"], 0.008))
    return [finish(o, 1.0, angle=40) for o in p] + flat


def interior(M):
    out = []
    idx = 0
    for row in (0, 1):
        sx = 1 if row == 0 else -1
        x = -sx * 1.6
        for i, z in enumerate(RACK_ZS):
            out += rack(M, x, z, sx, idx)
            idx += 1
    for i, z in enumerate(CAB_ZS):
        out += cabinet(M, z, i, open_drawer=2 if i == 1 else -1)
    # 付箋（ビルダーの走り書き：(-hw+0.62, 1.55, hd-3.5)。4台目のキャビネットの正面に貼る）
    xs = -HW + 0.5 + 0.0005
    zc, yc = HD0 - 3.5, 1.55
    note = quad("FC_Scribble", [(xs, yc - 0.04, zc - 0.04), (xs, yc - 0.04, zc + 0.04), (xs, yc + 0.04, zc + 0.04), (xs, yc + 0.04, zc - 0.04)], M["scribble"])
    hq.face_toward(note, (1, 0, 0))
    note.data.transform(Matrix.Translation(U(xs, yc, zc)) @ Matrix.Rotation(math.radians(6), 4, "X") @ Matrix.Translation(-U(xs, yc, zc)))
    out.append(note)
    return out


# ============================== 管理者デスク ==============================

def desk(M):
    """管理者デスク（原点、天板 1.4 x 0.7・上面 0.75）：灰色の鋼製机・右袖・ベージュのCRT（画面 = -Z）・キーボード・マグ・アームライト"""
    p, flat = [], []
    p.append(span("AD_Top", -0.7, 0.7, 0.72, 0.75, -0.35, 0.35, M["desktop"], 0.008, 3))
    p.append(span("AD_Ped", 0.28, 0.68, 0.0, 0.72, -0.33, 0.33, M["desk"], 0.008))
    for k in range(3):
        y0 = 0.05 + k * 0.22
        p.append(span(f"AD_Drw{k}", 0.3, 0.66, y0, y0 + 0.2, -0.345, -0.33, M["desk"], 0.005))
        p.append(span(f"AD_Pull{k}", 0.42, 0.54, y0 + 0.15, y0 + 0.17, -0.36, -0.345, M["handle"], 0.004))
    p.append(span("AD_LegL", -0.68, -0.62, 0.0, 0.72, -0.33, 0.33, M["desk"], 0.006))
    p.append(span("AD_Modesty", -0.62, 0.28, 0.2, 0.72, 0.3, 0.32, M["desk"], 0.004))
    # CRT（ビルダーの CrtBody：(0, 0.95, 0.18) 0.45 x 0.4 x 0.4）
    p.append(span("AD_CrtBezel", -0.225, 0.225, 0.75, 1.15, -0.02, 0.1, M["beige"], 0.02, 3))
    tube = profile_z("AD_CrtTube", [(-0.2, 0.78), (0.2, 0.78), (0.14, 0.86), (0.13, 1.08), (-0.13, 1.08), (-0.14, 0.86)], 0.1, 0.38, M["beige"])
    p.append(tube)
    p.append(span("AD_CrtFoot", -0.14, 0.14, 0.75, 0.78, 0.0, 0.3, M["beige"], 0.01))
    scr = quad("AD_Screen", [(-0.18, 0.8, -0.021), (0.18, 0.8, -0.021), (0.18, 1.1, -0.021), (-0.18, 1.1, -0.021)], M["crt"])
    hq.face_toward(scr, (0, 0, -1)); flat.append(scr)
    p.append(cyl_between("AD_CrtLed", (0.18, 0.77, -0.02), (0.18, 0.77, -0.024), 0.005, M["led_g"], 8))
    # キーボード・マグ・書類の山・アームライト
    p.append(span("AD_Kb", -0.2, 0.25, 0.75, 0.77, -0.3, -0.14, M["beige"], 0.006))
    p.append(lathe("AD_Mug", (0.58, 0.75, 0.15), [(0.0, 0.0), (0.038, 0.0), (0.042, 0.1), (0.038, 0.1), (0.034, 0.008), (0.0, 0.008)], M["mug"], 20))
    for k in range(5):
        p.append(span(f"AD_Stack{k}", -0.62 + k * 0.004, -0.38 + k * 0.004, 0.75 + k * 0.008, 0.758 + k * 0.008, 0.18 - k * 0.003, 0.34 - k * 0.003, M["paper"], 0.001))
    lx, lz = -0.6, 0.28
    p.append(lathe("AD_LampBase", (lx, 0.75, lz), [(0.07, 0), (0.07, 0.02), (0.02, 0.03)], M["black"], 20))
    p.append(pipe("AD_LampArm", [(lx, 0.78, lz), (lx + 0.05, 1.1, lz - 0.05), (lx + 0.25, 1.2, lz - 0.2)], 0.008, M["black"], 0.03, 8))
    shade = lathe("AD_LampShade", (0, 0, 0), [(0.0, 0.0), (0.03, 0.0), (0.08, 0.1), (0.075, 0.1)], M["black"], 24, cap_top=False)
    shade.data.transform(Matrix.Translation(U(lx + 0.27, 1.2, lz - 0.22)) @ Matrix.Rotation(math.radians(160), 4, "X"))
    p.append(shade)
    for o in p:
        finish(o, 1.0, angle=40)
    return [join(p + flat, "DataRoom_Desk")]


# ============================== 資料 ==============================

def _sheet(name, w, d, y, mat, rot=0.0):
    q = quad(name, [(-w / 2, y, -d / 2), (w / 2, y, -d / 2), (w / 2, y, d / 2), (-w / 2, y, d / 2)], mat)
    hq.face_toward(q, (0, 1, 0))
    s = q.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.0008
    finish(q, keep_uv=True, angle=60)
    q.data.transform(Matrix.Rotation(-rot, 4, "Z"))
    return q


def minutes(M):
    return [_sheet("MN_Minutes", 0.21, 0.297 * 0.8, -0.008, M["minutes"], 0.06)]


def kmemo(M):
    """破り取られたメモ（下の縁がぎざぎざ）"""
    w, d = 0.2, 0.14
    rnd = random.Random(1801)
    me = bpy.data.meshes.new("KM_Memo")
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    pts = [(-w / 2, d / 2), (w / 2, d / 2)]
    n = 12
    for k in range(n + 1):
        x = w / 2 - w * k / n
        z = -d / 2 + 0.02 + rnd.uniform(-0.012, 0.012)
        pts.append((x, z))
    vs = [bm.verts.new(U(x, -0.008, z)) for x, z in pts]
    f = bm.faces.new(list(reversed(vs)))
    for l in f.loops:
        co = l.vert.co
        l[uvl].uv = ((-co.x + w / 2) / w, (-co.y + d / 2) / d)
    bm.to_mesh(me); bm.free()
    o = hq._obj("KM_Memo", me, M["kmemo"])
    hq.face_toward(o, (0, 1, 0))
    s = o.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.0008
    finish(o, keep_uv=True, angle=60)
    o.data.transform(Matrix.Rotation(math.radians(-10), 4, "Z"))
    return [o]


def glasses(M):
    """読みかけの眼鏡（ビルダーの Glasses：机の (-0.15, 0.765, -0.22)）"""
    p = []
    for s in (-1, 1):
        rim = lathe(f"GL_Rim{s}", (0, 0, 0), [(0.024 + 0.002 * math.cos(t / 6 * math.pi * 2), 0.002 * math.sin(t / 6 * math.pi * 2)) for t in range(7)],
                    M["black"], 24, cap_top=False, cap_bottom=False)
        rim.data.transform(Matrix.Translation(U(s * 0.032, 0.008, 0)) @ Matrix.Rotation(math.radians(80), 4, "X"))
        p.append(rim)
        lens = cyl_between(f"GL_Lens{s}", (s * 0.032, 0.008, -0.001), (s * 0.032, 0.008, 0.001), 0.023, M["lens"], 20)
        p.append(lens)
        p.append(pipe(f"GL_Temple{s}", [(s * 0.056, 0.01, 0), (s * 0.06, 0.004, 0.06), (s * 0.058, -0.004, 0.12)], 0.0015, M["black"], 0.01, 5))
    p.append(pipe("GL_Bridge", [(-0.008, 0.012, 0), (0, 0.016, 0), (0.008, 0.012, 0)], 0.0015, M["black"], 0.005, 5))
    for o in p:
        finish(o, 4.0, angle=60)
    one = join(p, "DataRoom_Glasses")
    one.data.transform(Matrix.Rotation(math.radians(20), 4, "Z"))
    return [one]


def door(M):
    objs = lab_room.door({"oak": hq.mat("DAT_DoorLaminate", (0.8, 0.82, 0.84), 0.45, tex="../../lab/tex/oak.png"),
                          "alu": M["rail"], "glass": M["black"], "sus": M["handle"]})
    return [join(objs, "DataRoom_Door")]


def _breaker_mats(M):
    return {"mel": M["crac"], "grille": M["black"], "hazard": M["hazard"], "sus": M["handle"], "rubber": M["black"], "lever": M["lever"]}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.01), "DataRoom_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "DataRoom_Lever")]


PIECES = {"Shell": shell, "Interior": interior, "Desk": desk, "Minutes": minutes, "KMemo": kmemo, "Glasses": glasses,
          "Door": door, "Breaker": breaker, "Lever": lever}
