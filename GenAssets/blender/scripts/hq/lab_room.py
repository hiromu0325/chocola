"""
研究所応接室（lab：幅13 x 奥行10 x 天井3.2）を品質重視で作る。コンセプトアート lab4_* 準拠の
モダンな脳神経研究所のオフィス（格子天井・ライン照明・木目と紺の机・メッシュチェア・ガラスのホワイトボード・
MRIのライトボックス・濃色のタイルカーペット）。

  Shell        部屋の原点。床・壁・巾木・格子天井・吹出口・吊り下げのライン照明・木のルーバー壁
  Interior     部屋の原点。ホワイトボード・ライトボックス・受付カウンターとスツール・計測ワゴン・サーバーラック・観葉植物
  Desk         研究員の机（ユニットの原点。座る側は -Z、天板の上面 y=0.75、左 0.6m は資料を置くため空ける）
  Chair        メッシュのオフィスチェア（ユニットの原点。机の方 = +Z）
  MemberBoard  社員名簿のボード（ボードの中心が原点、表は -Z）
  Door / Breaker / Lever
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import hq
from hq import U, span, lathe, cyl, cyl_between, pipe, quad, plate_xy, profile_z, frame_ring, rrect_face, finish, join
import train_room

W, D, H = 13.0, 10.0, 3.2
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075          # 壁の内面
DOOR_HALF, DOOR_H = 0.55, 2.1
DESKS = [(-HW0 + 1.8, 2.4), (-HW0 + 1.8, 0.4), (-HW0 + 1.8, -1.6), (HW0 - 1.8, 1.6), (HW0 - 1.8, -0.8)]
# 島にするため隣に並べる机（資料は置かない。ビルダーの LabExtraDesks と同じ）
DESKS_EXTRA = [(x + (1.4 if x < 0 else -1.4), z) for x, z in DESKS]


def mats():
    M = {}
    M["carpet"] = hq.mat("LAB_Carpet", (1, 1, 1), 0.95, tex="carpet_tile.png")
    M["paint"] = hq.mat("LAB_Paint", (1, 1, 1), 0.8, tex="paint.png")
    M["tile"] = hq.mat("LAB_CeilingTile", (1, 1, 1), 0.9, tex="ceiling_tile.png")
    M["grid"] = hq.mat("LAB_Grid", (0.93, 0.93, 0.93), 0.4)
    M["cove"] = hq.mat("LAB_Cove", (0.2, 0.2, 0.22), 0.6)
    M["oak"] = hq.mat("LAB_Oak", (1, 1, 1), 0.45, tex="oak.png")
    M["edge"] = hq.mat("LAB_Edge", (0.55, 0.56, 0.57), 0.4)
    M["navy"] = hq.mat("LAB_Navy", (0.12, 0.2, 0.36), 0.4, 0.3)
    M["steel"] = hq.mat("LAB_SteelGrey", (0.62, 0.63, 0.64), 0.4, 0.3)
    M["alu"] = hq.mat("LAB_Aluminum", (0.8, 0.81, 0.82), 0.3, 1.0)
    M["black"] = hq.mat("LAB_BlackPlastic", (0.03, 0.03, 0.035), 0.4)
    M["fabric"] = hq.mat("LAB_ChairFabric", (0.05, 0.05, 0.055), 0.9)
    M["mesh"] = hq.mat("LAB_Mesh", (1, 1, 1), 0.7, tex="mesh.png")
    M["eeg"] = hq.mat("LAB_ScreenEEG", (1, 1, 1), 0.2, tex="screen_eeg.png", emit=(0.3, 0.8, 0.7), emit_strength=1.0)
    M["mri"] = hq.mat("LAB_ScreenMRI", (1, 1, 1), 0.2, tex="screen_mri.png", emit=(0.8, 0.8, 0.85), emit_strength=1.0)
    M["led"] = hq.mat("LAB_LedBar", (0.95, 0.97, 1.0), 0.3, emit=(0.92, 0.96, 1.0), emit_strength=6.0)
    M["film"] = hq.mat("LAB_Lightbox", (1, 1, 1), 0.3, tex="lightbox.png", emit=(0.85, 0.92, 1.0), emit_strength=2.0)
    M["wb"] = hq.mat("LAB_Whiteboard", (1, 1, 1), 0.1, tex="whiteboard.png")
    M["board"] = hq.mat("LAB_MemberBoard", (1, 1, 1), 0.6, tex="member_board.png")
    M["paper"] = hq.mat("LAB_PaperGraph", (1, 1, 1), 0.8, tex="paper_graph.png")
    M["hazard"] = hq.mat("LAB_Hazard", (1, 1, 1), 0.5, tex="hazard.png")
    M["device"] = hq.mat("LAB_DeviceFront", (1, 1, 1), 0.4, tex="device_front.png")
    M["perf"] = hq.mat("LAB_Perforated", (1, 1, 1), 0.4, 0.6, tex="perforated.png")
    M["sus"] = hq.mat("LAB_Stainless", (0.82, 0.83, 0.85), 0.2, 1.0)
    M["white"] = hq.mat("LAB_SolidWhite", (0.93, 0.93, 0.92), 0.3)
    M["glass"] = hq.mat("LAB_Glass", (0.03, 0.035, 0.04), 0.05)
    M["rubber"] = hq.mat("LAB_Rubber", (0.04, 0.04, 0.04), 0.7)
    M["pot"] = hq.mat("LAB_Pot", (0.22, 0.22, 0.23), 0.6)
    M["soil"] = hq.mat("LAB_Soil", (0.12, 0.09, 0.07), 0.95)
    M["leaf"] = hq.mat("LAB_Leaf", (0.12, 0.3, 0.12), 0.35)
    M["stem"] = hq.mat("LAB_Stem", (0.3, 0.24, 0.16), 0.8)
    M["led_g"] = hq.mat("LAB_LedGreen", (0.1, 0.4, 0.2), 0.3, emit=(0.3, 1.0, 0.5), emit_strength=4.0)
    M["led_a"] = hq.mat("LAB_LedAmber", (0.4, 0.3, 0.1), 0.3, emit=(1.0, 0.6, 0.15), emit_strength=4.0)
    M["panel"] = hq.mat("LAB_PanelWhite", (0.9, 0.9, 0.88), 0.4)
    M["lever"] = hq.mat("LAB_LeverRed", (0.75, 0.12, 0.1), 0.45)
    M["mug"] = hq.mat("LAB_Mug", (0.85, 0.85, 0.82), 0.2)
    return M


def door_openings():
    return [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)]


# ============================== 外殻 ==============================

def shell(M):
    out = []
    out.append(finish(span("LS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["carpet"], bev=0), 1.0))
    walls = []
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"LS_WallNS{zs}", door_openings(), -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["paint"])
    for xs in (-1, 1):
        walls.append(span(f"LS_WallEW{xs}", *sorted((xs * HW, xs * HW0)), 0.0, H, -HD0, HD0, M["paint"], bev=0))
    out += [finish(o, 1.0, angle=30) for o in walls]
    # 天井：600角の吸音板と格子（格子は吊り下がる細い T バー）
    ce = span("LS_Ceil", -HW0, HW0, H, H + 0.1, -HD0, HD0, M["tile"], bev=0)
    finish(ce, 1 / 0.6)
    out.append(ce)
    grid = []
    x = -HW + 0.6
    while x < HW - 0.1:
        grid.append(span(f"LS_GridX{x:.1f}", x - 0.012, x + 0.012, H - 0.012, H, -HD, HD, M["grid"], 0.002)); x += 0.6
    z = -HD + 0.6
    while z < HD - 0.1:
        grid.append(span(f"LS_GridZ{z:.1f}", -HW, HW, H - 0.012, H, z - 0.012, z + 0.012, M["grid"], 0.002)); z += 0.6
    # 線状の吹出口（黒いスリット2本の帯）
    for zc in (-3.0, 3.0):
        grid.append(span(f"LS_Slot{zc}", -3.0, 3.0, H - 0.006, H, zc - 0.07, zc + 0.07, M["white"], 0.002))
        for k in (-1, 1):
            grid.append(span(f"LS_SlotG{zc}{k}", -2.98, 2.98, H - 0.008, H - 0.006, zc + k * 0.03 - 0.012, zc + k * 0.03 + 0.012, M["black"], 0))
    out += [finish(o, 2.0, angle=40) for o in grid]
    # 巾木（ソフト巾木）と扉の枠
    trims = []
    for zs in (-1, 1):
        for (x0, x1) in ((-HW, -DOOR_HALF - 0.06), (DOOR_HALF + 0.06, HW)):
            trims.append(span(f"LS_Cove{zs}{x0:.0f}", x0, x1, 0, 0.08, *sorted((zs * HD, zs * (HD - 0.012))), M["cove"], 0.003))
        zin = zs * HD
        # アルミの細い扉枠（見付け 5cm）と開口の縦枠（扉板 ±0.46 まで）
        for xs in (-1, 1):
            trims.append(span(f"LS_Frame{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.05))), 0, DOOR_H + 0.05,
                              *sorted((zin, zin - zs * 0.015)), M["alu"], 0.003))
            trims.append(span(f"LS_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H,
                              *sorted((zs * HD0, zs * HD)), M["alu"], 0.002))
        trims.append(span(f"LS_FrameT{zs}", -DOOR_HALF - 0.05, DOOR_HALF + 0.05, DOOR_H, DOOR_H + 0.05,
                          *sorted((zin, zin - zs * 0.015)), M["alu"], 0.003))
        trims.append(span(f"LS_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), M["alu"], 0.002))
    for xs in (-1, 1):
        trims.append(span(f"LS_CoveEW{xs}", *sorted((xs * HW, xs * (HW - 0.012))), 0, 0.08, -HD, HD, M["cove"], 0.003))
    out += [finish(o, 2.0, angle=40) for o in trims]
    out += louver_wall(M)
    out += pendants(M)
    return out


def louver_wall(M):
    """南の壁の東側：縦のオーク材のルーバー（黒い下地の上に）"""
    p = []
    x0, x1 = DOOR_HALF + 0.9, HW - 0.3
    zb = -HD
    p.append(span("LL_Back", x0 - 0.02, x1 + 0.02, 0.08, 2.72, zb, zb + 0.012, M["black"], 0))
    x = x0
    k = 0
    while x + 0.045 <= x1:
        p.append(span(f"LL_Slat{k}", x, x + 0.045, 0.08, 2.72, zb + 0.012, zb + 0.047, M["oak"], 0.004))
        x += 0.07; k += 1
    p.append(span("LL_Cap", x0 - 0.03, x1 + 0.03, 2.72, 2.76, zb, zb + 0.05, M["oak"], 0.005))
    out = []
    for o in p:
        finish(o, 1.0, rot90=True, angle=40)
        out.append(o)
    return out


def pendants(M):
    """吊り下げのライン照明（机の列の上を南北に2本、中央を東西に1本）"""
    p = []
    y = 2.55
    runs = [((-HW0 + 2.5, -2.4), (-HW0 + 2.5, 3.2)), ((HW0 - 2.5, -1.6), (HW0 - 2.5, 2.4)), ((-1.8, 0.8), (1.8, 0.8))]
    for i, ((xa, za), (xb, zb)) in enumerate(runs):
        along_z = abs(xa - xb) < 1e-3
        if along_z:
            body = span(f"LP_Body{i}", xa - 0.03, xa + 0.03, y, y + 0.05, za, zb, M["alu"], 0.006)
            led = span(f"LP_Led{i}", xa - 0.022, xa + 0.022, y - 0.004, y + 0.001, za + 0.01, zb - 0.01, M["led"], 0)
            anchors = [(xa, za + 0.3), (xa, zb - 0.3)]
        else:
            body = span(f"LP_Body{i}", xa, xb, y, y + 0.05, za - 0.03, za + 0.03, M["alu"], 0.006)
            led = span(f"LP_Led{i}", xa + 0.01, xb - 0.01, y - 0.004, y + 0.001, za - 0.022, za + 0.022, M["led"], 0)
            anchors = [(xa + 0.3, za), (xb - 0.3, za)]
        p += [body, led]
        for k, (ax, az) in enumerate(anchors):
            p.append(cyl_between(f"LP_Cable{i}{k}", (ax, y + 0.05, az), (ax, H, az), 0.0015, M["black"], 6))
            p.append(lathe(f"LP_Canopy{i}{k}", (ax, H - 0.02, az), [(0.04, 0), (0.04, 0.02)], M["alu"], 24))
    return [finish(o, 2.0, angle=40) for o in p]


# ============================== 机・椅子 ==============================

def desk(M):
    p = []
    p.append(span("LD_Top", -0.7, 0.7, 0.72, 0.75, -0.35, 0.35, M["oak"], 0.004, 3))
    p.append(span("LD_EdgeF", -0.7, 0.7, 0.72, 0.75, -0.352, -0.348, M["edge"], 0.001))
    for xs in (-1, 1):
        x = xs * 0.64
        p.append(span(f"LD_LegF{xs}", x - 0.025, x + 0.025, 0.04, 0.72, -0.3, -0.25, M["navy"], 0.004))
        p.append(span(f"LD_LegB{xs}", x - 0.025, x + 0.025, 0.04, 0.72, 0.25, 0.3, M["navy"], 0.004))
        p.append(span(f"LD_Foot{xs}", x - 0.03, x + 0.03, 0.0, 0.04, -0.33, 0.33, M["navy"], 0.008))
        p.append(span(f"LD_Rail{xs}", x - 0.025, x + 0.025, 0.66, 0.72, -0.3, 0.3, M["navy"], 0.004))
    p.append(span("LD_Beam", -0.62, 0.62, 0.62, 0.7, 0.25, 0.3, M["navy"], 0.004))
    p.append(span("LD_Modesty", -0.61, 0.61, 0.3, 0.62, 0.27, 0.28, M["navy"], 0.003))
    # 引き出し（右側）
    p.append(span("LD_Ped", 0.18, 0.6, 0.03, 0.64, -0.28, 0.25, M["steel"], 0.006))
    for k, (y0, y1) in enumerate(((0.04, 0.26), (0.27, 0.45), (0.46, 0.63))):
        p.append(span(f"LD_Drawer{k}", 0.19, 0.59, y0, y1, -0.295, -0.28, M["steel"], 0.004))
        p.append(span(f"LD_Grip{k}", 0.3, 0.48, y1 - 0.03, y1 - 0.018, -0.3, -0.294, M["black"], 0.002))
    # モニター2台（脳波・MRI）とアーム
    for k, (xc, scr) in enumerate(((0.01, M["eeg"]), (0.45, M["mri"]))):
        p.append(span(f"LD_Mon{k}", xc - 0.215, xc + 0.215, 0.95, 1.22, 0.17, 0.195, M["black"], 0.006))
        s = quad(f"LD_Scr{k}", [(xc - 0.2, 0.963, 0.169), (xc + 0.2, 0.963, 0.169), (xc + 0.2, 1.208, 0.169), (xc - 0.2, 1.208, 0.169)], scr)
        hq.face_toward(s, (0, 0, -1))
        p.append(s)
        p.append(span(f"LD_Neck{k}", xc - 0.02, xc + 0.02, 0.75, 1.0, 0.2, 0.225, M["alu"], 0.005))
        p.append(span(f"LD_Base{k}", xc - 0.1, xc + 0.1, 0.75, 0.758, 0.12, 0.28, M["alu"], 0.004))
    # キーボード・マウス・マグ・資料
    p.append(span("LD_Kb", 0.08, 0.5, 0.75, 0.768, -0.2, -0.06, M["black"], 0.004))
    for r in range(4):
        for c in range(14):
            x = 0.095 + c * 0.028
            z = -0.19 + r * 0.032
            p.append(span(f"LD_Key{r}_{c}", x, x + 0.024, 0.768, 0.774, z, z + 0.026, M["black"], 0.002, 2))
    p.append(lathe("LD_Mouse", (0.58, 0.75, -0.12), [(0.0, 0.0), (0.03, 0.004), (0.032, 0.015), (0.02, 0.028), (0.0, 0.03)], M["black"], 16))
    p.append(lathe("LD_Mug", (0.62, 0.75, 0.12), [(0.0, 0.0), (0.04, 0.0), (0.042, 0.1), (0.036, 0.1), (0.034, 0.012)], M["mug"], 24, cap_top=False))
    pap = quad("LD_Paper", [(-0.12, 0.7515, -0.32), (0.09, 0.7515, -0.29), (0.07, 0.7515, -0.0), (-0.14, 0.7515, -0.03)], M["paper"])
    hq.face_toward(pap, (0, 1, 0))
    p.append(pap)
    for o in p:
        finish(o, 1.0, angle=40, keep_uv=o.name.startswith(("LD_Scr", "LD_Paper")))
    return [join(p, "Lab_Desk")]


def chair(M):
    p = []
    # 5本脚（アルミ）とキャスター
    for k in range(5):
        a = math.pi * 2 * k / 5 + math.pi / 2
        tip = (0.32 * math.cos(a), 0.07, 0.32 * math.sin(a))
        p.append(pipe(f"LC_Leg{k}", [(0, 0.11, 0), (tip[0] * 0.2, 0.1, tip[2] * 0.2), tip], 0.018, M["alu"], 0.08))
        p.append(cyl(f"LC_CasterAx{k}", (tip[0], 0.045, tip[2]), 0.03, 0.008, M["black"], 12))
        p.append(cyl_between(f"LC_Wheel{k}", (tip[0] - 0.02, 0.028, tip[2]), (tip[0] + 0.02, 0.028, tip[2]), 0.028, M["black"], 16))
    p.append(lathe("LC_Hub", (0, 0.09, 0), [(0.05, 0), (0.05, 0.03), (0.035, 0.05)], M["alu"], 24))
    p.append(cyl("LC_Gas", (0, 0.13, 0), 0.26, 0.025, M["black"], 20))
    p.append(cyl("LC_GasTop", (0, 0.3, 0), 0.12, 0.018, M["alu"], 16))
    p.append(span("LC_Mech", -0.12, 0.12, 0.4, 0.45, -0.12, 0.1, M["black"], 0.01))
    seat = span("LC_Seat", -0.24, 0.24, 0.45, 0.52, -0.22, 0.24, M["fabric"], 0.035, 4)
    p.append(seat)
    # 背もたれ：黒い枠＋メッシュ（わずかに反らせる）
    def back_fn(u, v):
        x = (u - 0.5) * 0.44
        y = 0.62 + v * 0.46
        z = -0.24 - 0.05 * math.sin(math.pi * v) + 0.03 * (2 * u - 1) ** 2
        return (x, y, z)
    bk = hq.grid_surface("LC_BackMesh", 10, 10, back_fn, M["mesh"])
    me = bk.data
    uvl = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = (co.x * 4, co.z * 4)
    sol = bk.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.006
    p.append(bk)
    frame_pts = [back_fn(0, 0), back_fn(0, 1), back_fn(1, 1), back_fn(1, 0)]
    p.append(pipe("LC_BackFrame", [frame_pts[0], frame_pts[1], frame_pts[2], frame_pts[3], frame_pts[0]], 0.014, M["black"], 0.06))
    p.append(pipe("LC_Spine", [(0, 0.45, -0.12), (0, 0.5, -0.27), (0, 0.66, -0.29)], 0.02, M["black"], 0.05))
    for xs in (-1, 1):
        p.append(pipe(f"LC_Arm{xs}", [(xs * 0.2, 0.47, -0.05), (xs * 0.24, 0.47, -0.05), (xs * 0.25, 0.68, -0.05)], 0.015, M["black"], 0.05))
        p.append(span(f"LC_ArmPad{xs}", xs * 0.25 - 0.035, xs * 0.25 + 0.035, 0.68, 0.705, -0.17, 0.1, M["black"], 0.01, 3))
    for o in p:
        finish(o, 4.0 if o.name.startswith("LC_BackMesh") else 1.0, angle=40, keep_uv=o.name.startswith("LC_BackMesh"))
    return [join(p, "Lab_Chair")]


# ============================== 壁・部屋の備品 ==============================

def member_board(M):
    """社員名簿（2.7 x 1.2、中心が原点、表は -Z）"""
    p = []
    w, h = 2.7, 1.2
    b = quad("LM_Face", [(w / 2 - 0.03, -h / 2 + 0.03, -0.02), (-w / 2 + 0.03, -h / 2 + 0.03, -0.02),
                         (-w / 2 + 0.03, h / 2 - 0.03, -0.02), (w / 2 - 0.03, h / 2 - 0.03, -0.02)], M["board"],
             uv=((1, 0), (0, 0), (0, 1), (1, 1)))
    hq.face_toward(b, (0, 0, -1))
    p.append(b)
    p.append(span("LM_Back", -w / 2, w / 2, -h / 2, h / 2, -0.018, 0.02, M["white"], 0.004))
    for (x0, x1, y0, y1) in ((-w / 2, w / 2, h / 2 - 0.03, h / 2), (-w / 2, w / 2, -h / 2, -h / 2 + 0.03),
                             (-w / 2, -w / 2 + 0.03, -h / 2, h / 2), (w / 2 - 0.03, w / 2, -h / 2, h / 2)):
        p.append(span("LM_Frame", x0, x1, y0, y1, -0.03, 0.02, M["alu"], 0.004))
    rnd = random.Random(301)
    cols = [M["lever"], M["navy"], M["hazard"], M["mug"]]
    for i in range(4):
        # 写真の四隅の画鋲（テクスチャの写真の位置：x 150+350i〜+220 / 1620px、y 150〜430 / 720px）
        px0 = -w / 2 + (150 + 350 * i) / 1620 * w
        px1 = -w / 2 + (370 + 350 * i) / 1620 * w
        py1 = h / 2 - 150 / 720 * h
        for (x, y) in ((px0 + 0.01, py1 - 0.01), (px1 - 0.01, py1 - 0.01)):
            p.append(lathe(f"LM_Pin{i}{x:.2f}", (x, y, -0.022), [(0.008, 0), (0.006, -0.008), (0.0, -0.012)], rnd.choice(cols), 12))
    for o in p:
        finish(o, 1.0, angle=40, keep_uv=o.name.startswith("LM_Face"))
    return [join(p, "Lab_MemberBoard")]


def interior(M):
    out = []
    out += whiteboard(M)
    out += light_box(M)
    out += counter(M)
    out += cart(M)
    out += rack(M)
    out += plant(M)
    return out


def whiteboard(M):
    """北の壁の東側：ガラスのホワイトボード（3.4 x 1.2）"""
    p = []
    xc, yc, w, h = 2.95, 1.45, 3.4, 1.2
    zf = HD - 0.03
    zp = zf - 0.001                                   # ガラス板の表面と重ならないよう 1mm 手前
    g = quad("LW_Face", [(xc - w / 2, yc - h / 2, zp), (xc + w / 2, yc - h / 2, zp), (xc + w / 2, yc + h / 2, zp), (xc - w / 2, yc + h / 2, zp)],
             M["wb"])
    hq.face_toward(g, (0, 0, -1))
    p.append(g)
    p.append(span("LW_Glass", xc - w / 2, xc + w / 2, yc - h / 2, yc + h / 2, zf, zf + 0.008, M["white"], 0.003))
    for xs in (-1, 1):
        for ys in (-1, 1):
            p.append(cyl_between(f"LW_Standoff{xs}{ys}", (xc + xs * (w / 2 - 0.06), yc + ys * (h / 2 - 0.06), zf - 0.004),
                                 (xc + xs * (w / 2 - 0.06), yc + ys * (h / 2 - 0.06), HD), 0.012, M["alu"], 16))
    p.append(span("LW_Tray", xc - 0.8, xc + 0.8, yc - h / 2 - 0.05, yc - h / 2 - 0.035, zf - 0.07, HD, M["alu"], 0.003))
    for k, m in enumerate((M["black"], M["navy"], M["lever"])):
        x = xc - 0.4 + k * 0.1
        p.append(cyl_between(f"LW_Marker{k}", (x, yc - h / 2 - 0.02, zf - 0.06), (x + 0.12, yc - h / 2 - 0.02, zf - 0.05), 0.009, m, 12))
    for o in p:
        finish(o, 1.0, angle=40, keep_uv=o.name.startswith("LW_Face"))
    return p


def light_box(M):
    """西の壁：MRIフィルムのライトボックス（2.4 x 0.7、中心 z=0.4）"""
    p = []
    zc, yc, w, h = 0.4, 1.68, 2.4, 0.7
    xf = -HW + 0.08
    p.append(span("LX_Box", -HW, xf - 0.005, yc - h / 2 - 0.04, yc + h / 2 + 0.04, zc - w / 2 - 0.04, zc + w / 2 + 0.04, M["white"], 0.008))
    # 西の壁を見ると右が +Z。U が右へ増えるよう始点は -Z 側
    f = quad("LX_Film", [(xf, yc - h / 2, zc - w / 2), (xf, yc - h / 2, zc + w / 2), (xf, yc + h / 2, zc + w / 2), (xf, yc + h / 2, zc - w / 2)], M["film"])
    hq.face_toward(f, (1, 0, 0))
    p.append(f)
    for o in p:
        finish(o, 1.0, angle=40, keep_uv=o.name.startswith("LX_Film"))
    return p


def counter(M):
    """南西：波板ステンレスの受付カウンター（x -6.2〜-2.4）とスツール2脚"""
    p = []
    x0, x1 = -HW + 0.2, -2.4
    z0, z1 = -HD, -HD + 0.6
    p.append(span("LK_Body", x0, x1, 0.06, 1.0, z0, z1 - 0.02, M["white"], 0.006))
    p.append(span("LK_Top", x0 - 0.03, x1 + 0.03, 1.0, 1.04, z0, z1 + 0.08, M["white"], 0.012, 4))
    p.append(span("LK_Plinth", x0 + 0.02, x1 - 0.02, 0.0, 0.06, z0, z1 - 0.06, M["black"], 0.003))

    def wave(u, v):
        x = x0 + (x1 - x0) * u
        y = 0.08 + 0.9 * v
        z = z1 + 0.018 + 0.016 * math.sin(2 * math.pi * ((x - x0) / 0.26 + 0.35 * math.sin(2 * math.pi * y / 0.9)))
        return (x, y, z)
    wv = hq.grid_surface("LK_Wave", 240, 24, wave, M["sus"])
    hq.face_toward(wv, (0, 0, 1))
    hq.smooth(wv, 180)
    p.append(wv)
    for k, xs in enumerate((-5.2, -3.7)):
        zs = z1 + 0.42
        p.append(lathe(f"LK_StoolBase{k}", (xs, 0, zs), [(0.2, 0), (0.2, 0.01), (0.15, 0.02), (0.03, 0.04)], M["sus"], 32))
        p.append(cyl(f"LK_StoolPost{k}", (xs, 0.04, zs), 0.66, 0.025, M["sus"], 20))
        p.append(lathe(f"LK_StoolRing{k}", (xs, 0.3, zs), [(0.16, 0), (0.17, 0.01), (0.16, 0.02), (0.15, 0.01), (0.16, 0)], M["sus"], 32,
                       cap_top=False, cap_bottom=False))
        p.append(lathe(f"LK_StoolSeat{k}", (xs, 0.7, zs), [(0.0, 0.0), (0.17, 0.0), (0.18, 0.02), (0.17, 0.05), (0.0, 0.055)], M["fabric"], 32))
    for o in p:
        if not o.name.startswith("LK_Wave"):
            finish(o, 1.0, angle=40)
    return p


def cart(M):
    """北東：計測機器を載せたステンレスのワゴン"""
    p = []
    cx, cz = HW - 0.45, 3.8
    w, d = 0.6, 0.5
    for xs in (-1, 1):
        for zs in (-1, 1):
            x, z = cx + xs * (w / 2 - 0.02), cz + zs * (d / 2 - 0.02)
            p.append(cyl(f"LG_Post{xs}{zs}", (x, 0.08, z), 0.85, 0.012, M["sus"], 12))
            p.append(cyl_between(f"LG_Wheel{xs}{zs}", (x - 0.02, 0.04, z), (x + 0.02, 0.04, z), 0.038, M["black"], 16))
    for y in (0.15, 0.55, 0.93):
        p.append(span(f"LG_Shelf{y}", cx - w / 2, cx + w / 2, y, y + 0.02, cz - d / 2, cz + d / 2, M["sus"], 0.006))
    for k, (y, hgt) in enumerate(((0.57, 0.16), (0.95, 0.14))):
        p.append(span(f"LG_Dev{k}", cx - 0.25, cx + 0.2, y, y + hgt, cz - 0.18, cz + 0.2, M["steel"], 0.01))
        fr = quad(f"LG_Front{k}", [(cx - 0.26, y + 0.01, cz + 0.18), (cx - 0.26, y + 0.01, cz - 0.16), (cx - 0.26, y + hgt - 0.01, cz - 0.16),
                                  (cx - 0.26, y + hgt - 0.01, cz + 0.18)], M["device"])
        hq.face_toward(fr, (-1, 0, 0))
        p.append(fr)
    hz = quad("LG_Hazard", [(cx - 0.305, 0.25, cz + 0.08), (cx - 0.305, 0.25, cz - 0.04), (cx - 0.305, 0.37, cz - 0.04), (cx - 0.305, 0.37, cz + 0.08)], M["hazard"])
    hq.face_toward(hz, (-1, 0, 0))
    p.append(hz)
    p.append(pipe("LG_Cable", [(cx + 0.18, 0.7, cz + 0.2), (cx + 0.25, 0.6, cz + 0.28), (cx + 0.27, 0.1, cz + 0.3), (HW - 0.05, 0.02, cz + 0.3)], 0.006, M["black"], 0.08))
    for o in p:
        finish(o, 2.0, angle=40, keep_uv=o.name.startswith(("LG_Front", "LG_Hazard")))
    return p


def rack(M):
    """南東：サーバーラック（東の壁に背を付け、前面は -X）"""
    p = []
    x0, x1 = HW - 1.0, HW - 0.02
    z0, z1 = -HD + 0.3, -HD + 0.9
    p.append(span("LR_Body", x0 + 0.02, x1, 0.06, 2.0, z0, z1, M["black"], 0.008))
    p.append(span("LR_Top", x0, x1, 2.0, 2.04, z0 - 0.01, z1 + 0.01, M["black"], 0.006))
    p.append(span("LR_Base", x0 + 0.03, x1, 0.0, 0.06, z0 + 0.02, z1 - 0.02, M["black"], 0.004))
    door = span("LR_Door", x0, x0 + 0.02, 0.08, 1.98, z0 + 0.02, z1 - 0.02, M["perf"], 0.004)
    p.append(door)
    p.append(span("LR_Handle", x0 - 0.02, x0, 0.9, 1.2, z1 - 0.07, z1 - 0.05, M["alu"], 0.004))
    rnd = random.Random(311)
    for k in range(18):
        y = 0.25 + k * 0.095
        for j in range(3):
            m = M["led_g"] if rnd.random() > 0.2 else M["led_a"]
            z = z0 + 0.1 + j * 0.018 + rnd.uniform(0, 0.2)
            p.append(span(f"LR_Led{k}_{j}", x0 - 0.005, x0 - 0.001, y, y + 0.006, z, z + 0.006, m, 0))   # 扉の表面の表示灯
    hz = quad("LR_Hazard", [(x0 - 0.001, 1.6, z0 + 0.35), (x0 - 0.001, 1.6, z0 + 0.23), (x0 - 0.001, 1.72, z0 + 0.23), (x0 - 0.001, 1.72, z0 + 0.35)], M["hazard"])
    hq.face_toward(hz, (-1, 0, 0))
    p.append(hz)
    for o in p:
        finish(o, 4.0 if o.name == "LR_Door" else 1.0, rot90=True, angle=40, keep_uv=o.name.startswith("LR_Hazard"))
    return p


def plant(M, seed=321):
    """北西：観葉植物（鉢・幹・葉 約140枚）"""
    rnd = random.Random(seed)
    p = []
    cx, cz = -HW + 0.45, HD - 0.45
    p.append(lathe("LP_Pot", (cx, 0, cz), [(0.14, 0), (0.2, 0.42), (0.21, 0.44), (0.19, 0.44), (0.185, 0.41)], M["pot"], 32, cap_top=False))
    p.append(cyl("LP_Soil", (cx, 0.38, cz), 0.01, 0.185, M["soil"], 32))
    stems = []
    for k in range(3):
        a = k * 2.1 + 0.3
        top = (cx + 0.18 * math.cos(a), 1.35 + 0.25 * k, cz + 0.18 * math.sin(a))
        mid = (cx + 0.06 * math.cos(a), 0.9, cz + 0.06 * math.sin(a))
        p.append(pipe(f"LP_Stem{k}", [(cx + 0.02 * k, 0.39, cz), mid, top], 0.012, M["stem"], 0.2))
        stems.append((mid, top))
    # 葉：細長い楕円を中央で少し折る。枝先ほど密に
    me = bpy.data.meshes.new("LP_Leaves")
    bm = bmesh.new()
    for i in range(140):
        (mx, my, mz), (tx, ty, tz) = stems[i % 3]
        t = rnd.uniform(0.25, 1.0)
        bx, by, bz = mx + (tx - mx) * t, my + (ty - my) * t, mz + (tz - mz) * t
        ang = rnd.uniform(0, math.pi * 2)
        droop = rnd.uniform(-0.6, 0.3)
        L, Wd = rnd.uniform(0.12, 0.2), rnd.uniform(0.04, 0.06)
        d = Vector((math.cos(ang) * math.cos(droop), math.sin(droop), math.sin(ang) * math.cos(droop)))
        side = Vector((-math.sin(ang), 0, math.cos(ang)))
        up = d.cross(side).normalized()
        verts = []
        for s in range(7):
            u = s / 6
            wv = Wd * math.sin(math.pi * u) * 0.5
            c = Vector((bx, by, bz)) + d * (L * u) - up * (0.02 * u * u)
            for sd in (-1, 0, 1):
                q = c + side * (wv * sd) + up * (0.008 * (1 - abs(sd)))
                verts.append(bm.verts.new(U(q.x, q.y, q.z)))
        for s in range(6):
            for sd in range(2):
                a0 = s * 3 + sd; b0 = (s + 1) * 3 + sd
                bm.faces.new((verts[a0], verts[a0 + 1], verts[b0 + 1], verts[b0]))
    bm.to_mesh(me); bm.free()
    leaves = hq._obj("LP_Leaves", me, M["leaf"])
    sol = leaves.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.002
    for o in p:
        finish(o, 2.0, angle=50)
    finish(leaves, 4.0, angle=60)
    return p + [leaves]


# ============================== 扉・配電盤 ==============================

def door(M):
    """オーク突板の扉：縦長のガラス、ステンレスのレバーハンドル、蹴板"""
    t = 0.02
    p = []
    slot = (0.18, 0.32, 0.95, 1.9)
    p += train_room.grid_wall("LDr_Leaf", [slot], -0.458, 0.458, 0.0, 2.1, lambda a, b, c, d: (a, b, c, d, -t, t), M["oak"])
    for zs in (-1, 1):
        def to3d(u, v, d, zs=zs):
            return (0.25 + u, 1.425 + v, zs * (t + d))
        p.append(frame_ring(f"LDr_Bead{zs}", to3d, 0.14, 0.95, 0.005, 0.11, 0.92, 0.005, -t, 0.006, M["alu"]))
        g = rrect_face(f"LDr_Glass{zs}", to3d, 0.115, 0.925, 0.004, -t + 0.002, M["glass"])
        hq.face_toward(g, (0, 0, zs)); p.append(g)
        zf = zs * t
        p.append(cyl_between(f"LDr_Rose{zs}", (-0.36, 1.0, zf), (-0.36, 1.0, zf + zs * 0.01), 0.028, M["sus"], 24))
        p.append(pipe(f"LDr_Lever{zs}", [(-0.36, 1.0, zf + zs * 0.01), (-0.36, 1.0, zf + zs * 0.055), (-0.22, 1.0, zf + zs * 0.055)], 0.009, M["sus"], 0.02))
        p.append(span(f"LDr_Kick{zs}", -0.44, 0.44, 0.02, 0.22, *sorted((zf, zf + zs * 0.002)), M["sus"], 0.001))
    for y in (0.25, 1.05, 1.85):
        p.append(cyl_between(f"LDr_Hinge{y}", (0.462, y - 0.05, 0.0), (0.462, y + 0.05, 0.0), 0.007, M["sus"], 12))
    for o in p:
        finish(o, 1.0, rot90=True, angle=40, keep_uv=o.name.startswith("LDr_Glass"))
    return [join(p, "Lab_Door")]


def _breaker_mats(M):
    return {"mel": M["panel"], "grille": M["black"], "hazard": M["hazard"], "sus": M["steel"], "rubber": M["rubber"], "lever": M["lever"]}


def breaker(M):
    # 壁から 0.35 離れたユニット（前面 x=-0.12、壁は x=+0.275）
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.01), "Lab_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "Lab_Lever")]


PIECES = {"Shell": shell, "Interior": interior, "Desk": desk, "Chair": chair, "MemberBoard": member_board,
          "Door": door, "Breaker": breaker, "Lever": lever}
