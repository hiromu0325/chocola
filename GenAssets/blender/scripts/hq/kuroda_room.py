"""
黒田の自宅（kuroda_home：幅7 x 奥行8 x 天井2.6）を品質重視で作る。
家族のダイニングキッチンと居間：木目の床・若草色の縦縞の壁紙・食卓の上の布のペンダント・
四人分の夕食（父の席だけ手がつけられていない）・台所（流し・ガスコンロと鍋・換気扇・吊り戸棚・タイル）・
冷蔵庫（子供の絵・マグネット・学校のプリント）・東の窓（カーテン・夜の住宅街）・サイドボード（家族写真・
手帳・ICレコーダー・白紙の手帳）・テレビ台と薄型テレビ・ソファ（ランドセルとくまのぬいぐるみ）・
壁の子供の絵・カレンダー・時計・観葉植物。

  Shell      部屋の原点。床・壁・天井・巾木・窓・外の景色・台所の壁のタイル
  Lamps      部屋の原点。食卓のペンダントと天井の照明（Unityでは影を落とさない）
  Interior   部屋の原点。台所・冷蔵庫・テレビ・ソファ・サイドボード・壁の飾り・植物・ラグ
  Dining     食卓（ユニットの原点、天板の上面 0.745）と椅子4脚・四人分の夕食
  Drawing / Rules / LastRec / Verdict   資料と手帳の見た目（ビルダーの箱の中心が原点）
  Door / Breaker / Lever
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import hq
from hq import U, span, lathe, cyl, cyl_between, pipe, quad, profile_z, finish, join, pillow
import train_room
import dim_room
import lab_room

W, D, H = 7.0, 8.0, 2.6
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
TABLE = (0.0, 0.6)
WIN = (-2.4, -1.2, 0.95, 2.05)            # 東の窓（z0, z1, y0, y1）
SIDEB = (HW0 - 0.55, -1.8)


def mats():
    M = {}
    M["floor"] = hq.mat("KUR_Flooring", (1, 1, 1), 0.45, tex="flooring.png")
    M["wall"] = hq.mat("KUR_Wallpaper", (1, 1, 1), 0.85, tex="wallpaper.png")
    M["ceil"] = hq.mat("KUR_Ceiling", (0.95, 0.95, 0.93), 0.9)
    M["trim"] = hq.mat("KUR_Trim", (0.55, 0.4, 0.28), 0.45, tex="wood.png")
    M["wood"] = hq.mat("KUR_Wood", (1, 1, 1), 0.45, tex="wood.png")
    M["woodD"] = hq.mat("KUR_WoodDark", (0.6, 0.5, 0.42), 0.45, tex="wood.png")
    M["tile"] = hq.mat("KUR_KitchenTile", (1, 1, 1), 0.2, tex="kitchen_tile.png")
    M["cream"] = hq.mat("KUR_CabinetCream", (0.9, 0.87, 0.78), 0.4)
    M["steel"] = hq.mat("KUR_Stainless", (0.8, 0.81, 0.83), 0.25, 0.9)
    M["black"] = hq.mat("KUR_Black", (0.03, 0.03, 0.035), 0.5)
    M["white"] = hq.mat("KUR_WhitePlastic", (0.93, 0.93, 0.91), 0.35)
    M["fridge"] = hq.mat("KUR_Fridge", (0.9, 0.9, 0.86), 0.3)
    M["rug"] = hq.mat("KUR_Rug", (1, 1, 1), 0.95, tex="rug.png")
    M["sofa"] = hq.mat("KUR_Sofa", (1, 1, 1), 0.9, tex="sofa.png")
    M["curtain"] = hq.mat("KUR_Curtain", (1, 1, 1), 0.9, tex="curtain.png")
    M["shade"] = hq.mat("KUR_LampShade", (1.0, 0.94, 0.82), 0.9, tex="curtain.png", emit=(1.0, 0.78, 0.5), emit_strength=1.5)
    M["bulb"] = hq.mat("KUR_Bulb", (1, 0.95, 0.85), 0.2, emit=(1.0, 0.82, 0.58), emit_strength=8.0)
    M["ceilamp"] = hq.mat("KUR_CeilingLight", (1.0, 0.97, 0.92), 0.3, emit=(1.0, 0.92, 0.78), emit_strength=3.0)
    M["night"] = hq.mat("KUR_Night", (1, 1, 1), 0.9, tex="../../mizuno/tex/night_city.png", emit=(1, 1, 1), emit_strength=1.0)
    M["glass"] = hq.mat("KUR_WindowGlass", (0.6, 0.62, 0.66), 0.05, alpha=0.12)
    M["alu"] = hq.mat("KUR_Aluminum", (0.78, 0.79, 0.8), 0.3, 1.0)
    M["porcelain"] = hq.mat("KUR_Porcelain", (0.94, 0.93, 0.9), 0.2)
    M["blue"] = hq.mat("KUR_Indigo", (0.16, 0.22, 0.4), 0.25)
    M["lacq"] = hq.mat("KUR_Lacquer", (0.35, 0.08, 0.06), 0.2)
    M["rice"] = hq.mat("KUR_Rice", (0.96, 0.95, 0.9), 0.6)
    M["fish"] = hq.mat("KUR_GrilledFish", (0.62, 0.44, 0.28), 0.5)
    M["miso"] = hq.mat("KUR_Miso", (0.55, 0.38, 0.2), 0.1)
    M["greens"] = hq.mat("KUR_Greens", (0.25, 0.45, 0.18), 0.5)
    M["chop"] = hq.mat("KUR_Chopsticks", (0.3, 0.18, 0.1), 0.4)
    M["juice"] = hq.mat("KUR_Juice", (0.95, 0.6, 0.15), 0.1)
    M["cup"] = hq.mat("KUR_Glass", (0.8, 0.85, 0.9), 0.05, alpha=0.3)
    M["pot"] = hq.mat("KUR_PotRed", (0.62, 0.12, 0.1), 0.35)
    M["drawing"] = hq.mat("KUR_Drawing", (1, 1, 1), 0.9, tex="drawing.png")
    M["drawings"] = hq.mat("KUR_DrawingsWall", (1, 1, 1), 0.9, tex="drawings_wall.png")
    M["cal"] = hq.mat("KUR_Calendar", (1, 1, 1), 0.8, tex="calendar.png")
    M["photo"] = hq.mat("KUR_FamilyPhoto", (1, 1, 1), 0.5, tex="family_photo.png")
    M["rules"] = hq.mat("KUR_NotebookRules", (1, 1, 1), 0.9, tex="notebook_rules.png")
    M["blank"] = hq.mat("KUR_NotebookBlank", (1, 1, 1), 0.9, tex="notebook_blank.png")
    M["lcd"] = hq.mat("KUR_RecorderLcd", (1, 1, 1), 0.3, tex="recorder_lcd.png")
    M["cover"] = hq.mat("KUR_NotebookCover", (0.12, 0.12, 0.16), 0.5)
    M["cover2"] = hq.mat("KUR_NotebookBrown", (0.38, 0.24, 0.16), 0.5)
    M["tv"] = hq.mat("KUR_TvScreen", (0.02, 0.025, 0.03), 0.05)
    M["randoseru"] = hq.mat("KUR_Randoseru", (0.6, 0.08, 0.1), 0.3)
    M["bear"] = hq.mat("KUR_Bear", (0.58, 0.42, 0.28), 0.95)
    M["magnet"] = hq.mat("KUR_Magnet", (0.2, 0.5, 0.85), 0.3)
    M["paper"] = hq.mat("KUR_PaperPlain", (0.94, 0.93, 0.9), 0.9)
    M["clockface"] = hq.mat("KUR_ClockFace", (0.95, 0.95, 0.93), 0.3)
    M["terracotta"] = hq.mat("KUR_PlantPot", (0.55, 0.36, 0.26), 0.8)
    M["soil"] = hq.mat("KUR_Soil", (0.12, 0.08, 0.05), 0.95)
    M["stem"] = hq.mat("KUR_Stem", (0.3, 0.24, 0.16), 0.8)
    M["leaf"] = hq.mat("KUR_Leaf", (0.14, 0.32, 0.12), 0.45)
    M["lever"] = hq.mat("KUR_LeverRed", (0.75, 0.12, 0.1), 0.45)
    M["hazard"] = hq.mat("KUR_Hazard", (1, 1, 1), 0.5, tex="../../lab/tex/hazard.png")
    return M


def _outward(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()


def _cloth(name, fn, nu, nv, mat, uvs=2.0, thick=0.004):
    """東西の壁に垂れる布（UV = z, y）"""
    o = hq.grid_surface(name, nu, nv, fn, mat)
    me = o.data
    uvl = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = (-co.y * uvs, co.z * uvs)
    s = o.modifiers.new("Solid", "SOLIDIFY"); s.thickness = thick
    finish(o, keep_uv=True, angle=60)
    _outward(o)
    return o


# ============================== 外殻 ==============================

def shell(M):
    out = []
    out.append(finish(span("KS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["floor"], bev=0), 1.0, rot90=True))
    walls = []
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"KS_WallNS{zs}", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["wall"])
    walls.append(span("KS_WallW", -HW0, -HW, 0.0, H, -HD0, HD0, M["wall"], bev=0))
    walls += train_room.grid_wall("KS_WallE", [WIN], -HD0, HD0, 0.0, H, lambda a, b, c, d: (HW, HW0, c, d, a, b), M["wall"])
    out += [finish(o, 2.0, angle=30) for o in walls]
    out.append(finish(span("KS_Ceil", -HW0, HW0, H, H + 0.1, -HD0, HD0, M["ceil"], bev=0), 1.0))
    p = []
    # 巾木・回り縁・扉の枠
    for zs in (-1, 1):
        for (x0, x1) in ((-HW, -DOOR_HALF - 0.08), (DOOR_HALF + 0.08, HW)):
            p.append(span(f"KT_Base{zs}{x0:.0f}", x0, x1, 0, 0.07, *sorted((zs * HD, zs * (HD - 0.012))), M["trim"], 0.003))
            p.append(span(f"KT_Crown{zs}{x0:.0f}", x0, x1, H - 0.04, H, *sorted((zs * HD, zs * (HD - 0.02))), M["ceil"], 0.004))
        zin = zs * HD
        for xs in (-1, 1):
            p.append(span(f"KT_Cas{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.08))), 0, DOOR_H + 0.08, *sorted((zin, zin - zs * 0.02)), M["trim"], 0.004))
            p.append(span(f"KT_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), M["trim"], 0.002))
        p.append(span(f"KT_CasT{zs}", -DOOR_HALF - 0.08, DOOR_HALF + 0.08, DOOR_H, DOOR_H + 0.08, *sorted((zin, zin - zs * 0.02)), M["trim"], 0.004))
        p.append(span(f"KT_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), M["trim"], 0.002))
    for xs in (-1, 1):
        a, b = sorted((xs * HW, xs * (HW - 0.012)))
        p.append(span(f"KT_BaseEW{xs}", a, b, 0, 0.07, -HD, HD, M["trim"], 0.003))
        a2, b2 = sorted((xs * HW, xs * (HW - 0.02)))
        p.append(span(f"KT_CrownEW{xs}", a2, b2, H - 0.04, H, -HD, HD, M["ceil"], 0.004))
    # 台所の壁のタイル（カウンターと吊り戸棚の間）
    p.append(span("KT_Tile", -HW, -HW + 0.006, 0.89, 1.55, 0.05, 2.35, M["tile"], 0))
    out += [finish(o, 1.0, angle=40) if not o.name.startswith("KT_Tile") else finish(o, 2.0, angle=40) for o in p]
    out += window(M)
    return out


def window(M):
    """東の窓：アルミサッシ・窓台・カーテン（半分閉じる）・夜の住宅街"""
    z0, z1, y0, y1 = WIN
    p, g = [], []
    xi, xo = HW, HW0
    for (a0, a1, b0, b1) in ((z0, z1, y0 - 0.02, y0), (z0, z1, y1, y1 + 0.02)):
        p.append(span("KW_Rev", xi, xo, b0, b1, a0, a1, M["white"], 0.002))
    for zz in (z0 - 0.02, z1):
        p.append(span("KW_RevS", xi, xo, y0 - 0.02, y1 + 0.02, zz, zz + 0.02, M["white"], 0.002))
    xs = HW + 0.045
    for (a0, a1, b0, b1) in ((z0, z1, y0, y0 + 0.035), (z0, z1, y1 - 0.035, y1), (z0, z0 + 0.035, y0, y1), (z1 - 0.035, z1, y0, y1)):
        p.append(span("KW_Frame", xs - 0.022, xs + 0.022, b0, b1, a0, a1, M["alu"], 0.003))
    zm = (z0 + z1) / 2
    for k, (c0, c1, off) in enumerate(((z0 + 0.035, zm + 0.02, -0.01), (zm - 0.02, z1 - 0.035, 0.01))):
        xx = xs + off
        for (a0, a1, b0, b1) in ((c0, c1, y0 + 0.035, y0 + 0.065), (c0, c1, y1 - 0.065, y1 - 0.035), (c0, c0 + 0.03, y0 + 0.035, y1 - 0.035), (c1 - 0.03, c1, y0 + 0.035, y1 - 0.035)):
            p.append(span(f"KW_Sash{k}", xx - 0.011, xx + 0.011, b0, b1, a0, a1, M["alu"], 0.003))
        q = quad(f"KW_Glass{k}", [(xx, y0 + 0.065, c0 + 0.03), (xx, y0 + 0.065, c1 - 0.03), (xx, y1 - 0.065, c1 - 0.03), (xx, y1 - 0.065, c0 + 0.03)], M["glass"])
        hq.face_toward(q, (-1, 0, 0)); g.append(q)
    p.append(span("KW_Sill", HW - 0.06, HW, y0 - 0.03, y0, z0 - 0.04, z1 + 0.04, M["white"], 0.006, 3))
    rx = HW - 0.08
    p.append(pipe("KW_Rail", [(rx, y1 + 0.12, z0 - 0.4), (rx, y1 + 0.12, z1 + 0.4)], 0.01, M["white"], 0.02, 10))
    out = [finish(o, 2.0, angle=40) for o in p] + g
    # カーテン：南は窓の半分まで引かれ、北は寄せてある
    out.append(_cloth("KW_CurtainS", lambda u, v: (rx - 0.02 + 0.035 * math.sin(u * math.pi * 14), 0.9 + v * (y1 + 0.1 - 0.9), (z0 - 0.3) + u * 0.95), 70, 6, M["curtain"]))
    out.append(_cloth("KW_CurtainN", lambda u, v: (rx - 0.02 + 0.035 * math.sin(u * math.pi * 7), 0.9 + v * (y1 + 0.1 - 0.9), (z1 + 0.05) + u * 0.32), 40, 6, M["curtain"]))
    xk = HW0 + 4.0
    sky = quad("KW_Night", [(xk, -0.8, 6.0), (xk, -0.8, -10.0), (xk, 4.2, -10.0), (xk, 4.2, 6.0)], M["night"])
    hq.face_toward(sky, (-1, 0, 0))
    out.append(sky)
    return out


def lamps(M):
    """食卓の上の布のペンダント（ビルダーの KitchenLight の上）と、天井の丸い照明2灯（RoomLight の上）"""
    p = []
    tx, tz = TABLE
    p.append(lathe("KL_Canopy", (tx, H - 0.03, tz), [(0.06, 0), (0.06, 0.03)], M["white"], 24))
    p.append(cyl("KL_Cord", (tx, 2.35, tz), H - 0.03 - 2.35, 0.004, M["black"], 8))
    sh = lathe("KL_Shade", (tx, 2.14, tz), [(0.3, 0.0), (0.26, 0.08), (0.14, 0.2), (0.05, 0.22)], M["shade"], 48, cap_top=False, cap_bottom=False)
    s = sh.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.004
    p.append(sh)
    p.append(lathe("KL_Bulb", (tx, 2.2, tz), [(0.0, 0.0), (0.025, 0.01), (0.032, 0.05), (0.02, 0.09), (0.015, 0.1)], M["bulb"], 24))
    for i, z in enumerate((-2.0, 2.0)):
        p.append(lathe(f"KL_Ceil{i}", (0, H - 0.07, z), [(0.0, 0.0), (0.18, 0.004), (0.23, 0.03), (0.24, 0.07)], M["ceilamp"], 48))
    return [finish(o, 3.0, angle=60) for o in p]


# ============================== 食卓 ==============================

def place_setting(M, x, z, facing, state, seed):
    """一人分の夕食。facing = 座る人の方向（-1: 南に座る、+1: 北に座る）。state: full / half / done"""
    rnd = random.Random(seed)
    p = []
    y = 0.745
    # 主菜の皿（焼き魚と付け合わせ）
    p.append(lathe("PS_Plate", (x, y, z), [(0.0, 0.0), (0.07, 0.0), (0.1, 0.012), (0.115, 0.02), (0.11, 0.022), (0.0, 0.008)], M["porcelain"], 32))
    if state != "done":
        fish = pillow("PS_Fish", (x, y + 0.028, z), (0.15, 0.03, 0.05) if state == "full" else (0.08, 0.025, 0.04), M["fish"], seed=seed)
        p.append(fish)
        p.append(pillow("PS_Greens", (x + 0.05, y + 0.024, z + facing * -0.04), (0.04, 0.02, 0.04), M["greens"], seed=seed + 1))
    # ご飯茶碗と汁椀（座る人の手前）
    rx, rz = x - 0.1, z + facing * 0.14
    sx, sz = x + 0.1, z + facing * 0.14
    p.append(lathe("PS_Bowl", (rx, y, rz), [(0.0, 0.0), (0.03, 0.0), (0.03, 0.008), (0.055, 0.05), (0.058, 0.06), (0.054, 0.06), (0.0, 0.012)], M["blue"], 32))
    if state != "done":
        rh = 0.075 if state == "full" else 0.05
        p.append(lathe("PS_Rice", (rx, y + 0.012, rz), [(0.0, 0.0), (0.05, 0.035), (0.048, rh - 0.01), (0.0, rh)], M["rice"], 24))
    p.append(lathe("PS_Soup", (sx, y, sz), [(0.0, 0.0), (0.03, 0.0), (0.03, 0.008), (0.056, 0.055), (0.058, 0.065), (0.054, 0.065), (0.0, 0.012)], M["lacq"], 32))
    if state == "full":
        p.append(cyl("PS_Miso", (sx, y + 0.052, sz), 0.001, 0.05, M["miso"], 24))
    # 箸と箸置き（full は揃えて置いたまま）
    bx, bz = x, z + facing * 0.215
    p.append(span("PS_Rest", bx - 0.1, bx - 0.07, y, y + 0.01, bz - 0.01, bz + 0.01, M["porcelain"], 0.003))
    ang = 0.0 if state == "full" else rnd.uniform(-0.4, 0.4)
    for k in (-1, 1):
        a0 = (bx - 0.09 + math.cos(ang) * 0.0, y + 0.012, bz + k * 0.006)
        a1 = (bx - 0.09 + math.cos(ang) * 0.22, y + 0.008, bz + k * 0.006 + math.sin(ang) * 0.22)
        p.append(cyl_between("PS_Chop", a0, a1, 0.003, M["chop"], 6, r1=0.0018))
    # 飲み物のコップ
    gx, gz = x + 0.2, z + facing * 0.12
    p.append(lathe("PS_Cup", (gx, y, gz), [(0.0, 0.0), (0.03, 0.0), (0.035, 0.1), (0.032, 0.1), (0.027, 0.004), (0.0, 0.004)], M["cup"], 20, cap_top=False))
    if state != "full":
        p.append(cyl("PS_Juice", (gx, y + 0.005, gz), 0.03 if state == "half" else 0.008, 0.026, M["juice"], 16))
    else:
        p.append(cyl("PS_Water", (gx, y + 0.005, gz), 0.07, 0.027, M["cup"], 16))
    return p


def dining(M):
    """食卓（ビルダーの DiningTable：天板 1.6 x 1.1、上面 0.745、中央の一本脚）と椅子4脚・四人分の夕食"""
    p = []
    p.append(span("KD_Top", -0.8, 0.8, 0.695, 0.745, -0.55, 0.55, M["wood"], 0.012, 4))
    p.append(span("KD_Apron", -0.7, 0.7, 0.62, 0.695, -0.45, 0.45, M["wood"], 0.004))
    for sx in (-1, 1):
        p.append(span(f"KD_Leg{sx}", sx * 0.55 - 0.04, sx * 0.55 + 0.04, 0.0, 0.62, -0.04, 0.04, M["wood"], 0.008))
        p.append(span(f"KD_Foot{sx}", sx * 0.55 - 0.05, sx * 0.55 + 0.05, 0.0, 0.05, -0.42, 0.42, M["wood"], 0.01))
    p.append(span("KD_Stretch", -0.55, 0.55, 0.3, 0.36, -0.03, 0.03, M["wood"], 0.006))
    # 四人分：南の2席は子供（食べかけ）、北東は母（ほぼ食べ終え）、北西は父（手つかず）
    seats = [(-0.55, -0.85, "half"), (0.55, -0.85, "half"), (-0.55, 0.85, "full"), (0.55, 0.85, "done")]
    for k, (sx, sz, st) in enumerate(seats):
        facing = -1 if sz < 0 else 1
        p += place_setting(M, sx * 0.7, sz * 0.36, facing, st, 2200 + k)
    # 真ん中：大皿のサラダ・醤油差し
    p.append(lathe("KD_Serve", (0, 0.745, 0), [(0.0, 0.0), (0.1, 0.0), (0.16, 0.04), (0.17, 0.05), (0.16, 0.05), (0.0, 0.01)], M["porcelain"], 40))
    for k in range(9):
        a = k * 0.7
        p.append(pillow(f"KD_Salad{k}", (0.05 * math.cos(a), 0.775, 0.05 * math.sin(a)), (0.07, 0.03, 0.05), M["greens"], seed=2300 + k))
    p.append(lathe("KD_Soy", (0.3, 0.745, 0.05), [(0.0, 0.0), (0.025, 0.0), (0.026, 0.07), (0.012, 0.1), (0.008, 0.12), (0.0, 0.12)], M["cup"], 20))
    p.append(cyl("KD_SoyIn", (0.3, 0.748, 0.05), 0.05, 0.022, M["lacq"], 16))
    out = [finish(o, 2.0, angle=45) for o in p]
    # 椅子4脚（南は背を南へ、北は背を北へ）＋子供の席には座布団
    for k, (sx, sz, st) in enumerate(seats):
        ch = dim_room.chair({"wood": M["woodD"]})[0]
        parts = [ch]
        if sz < 0:
            cu = pillow(f"KD_Cushion{k}", (0.0, 0.48, 0.0), (0.36, 0.05, 0.34), M["sofa"], seed=2310 + k)
            finish(cu, 3.0, angle=60)
            parts.append(cu)
        c = join(parts, f"KD_Chair{k}")
        yaw = 0 if sz < 0 else 180
        c.data.transform(Matrix.Translation(U(sx, 0, sz)) @ Matrix.Rotation(math.radians(-yaw), 4, "Z"))
        out.append(c)
    return [join(out, "KurodaHome_Dining")]


# ============================== 台所・居間 ==============================

def kitchen(M):
    """台所（西の壁：ビルダーの KitchenCounter x -3.4〜-2.7、z 0.1〜2.3、天板 0.89）"""
    p = []
    x0, x1 = -HW, -HW0 + 0.8
    z0, z1 = 0.1, 2.3
    p.append(span("KK_Body", x0, x1 - 0.02, 0.08, 0.86, z0, z1, M["cream"], 0.006))
    p.append(span("KK_Plinth", x0, x1 - 0.06, 0.0, 0.08, z0 + 0.02, z1 - 0.02, M["black"], 0.004))
    # 扉と引き出し（前面 +X）
    xf = x1 - 0.02
    for k in range(4):
        a, b = z0 + 0.01 + k * 0.545, z0 + 0.01 + (k + 1) * 0.545
        p.append(span(f"KK_Door{k}", xf, xf + 0.018, 0.1, 0.62, a + 0.004, b - 0.004, M["cream"], 0.006))
        p.append(span(f"KK_Drawer{k}", xf, xf + 0.018, 0.64, 0.84, a + 0.004, b - 0.004, M["cream"], 0.006))
        p.append(span(f"KK_Pull{k}", xf + 0.018, xf + 0.035, 0.73, 0.75, (a + b) / 2 - 0.08, (a + b) / 2 + 0.08, M["wood"], 0.006))
        p.append(span(f"KK_Knob{k}", xf + 0.018, xf + 0.035, 0.5, 0.56, b - 0.07, b - 0.05, M["wood"], 0.006))
    # 天板（ステンレス）と流し（z 1.3〜1.9）
    sz0, sz1 = 1.3, 1.9
    sx0, sx1 = x0 + 0.08, x1 - 0.1
    p += train_room.grid_wall("KK_Top", [(sx0, sx1, sz0, sz1)], x0, x1, z0 - 0.02, z1 + 0.02, lambda a, b, c, d: (a, b, 0.86, 0.89, c, d), M["steel"])
    p.append(span("KK_SinkB", sx0, sx1, 0.7, 0.71, sz0, sz1, M["steel"], 0.01))
    for (a0, a1, b0, b1) in ((sx0, sx1, sz0, sz0 + 0.005), (sx0, sx1, sz1 - 0.005, sz1), (sx0, sx0 + 0.005, sz0, sz1), (sx1 - 0.005, sx1, sz0, sz1)):
        p.append(span("KK_SinkW", a0, a1, 0.7, 0.89, b0, b1, M["steel"], 0))
    fx = x0 + 0.04
    p.append(pipe("KK_Faucet", [(fx, 0.89, 1.6), (fx, 1.12, 1.6), (fx + 0.04, 1.15, 1.6), (fx + 0.2, 1.08, 1.6)], 0.012, M["steel"], 0.04, 10))
    p.append(cyl_between("KK_FaucetLever", (fx, 1.05, 1.6), (fx + 0.02, 1.08, 1.68), 0.006, M["steel"], 8))
    # ガスコンロ（南端 z 0.15〜0.7）と味噌汁の鍋
    gz = 0.42
    p.append(span("KK_Stove", x0 + 0.06, x1 - 0.06, 0.89, 0.97, gz - 0.28, gz + 0.28, M["black"], 0.01))
    for k, zz in enumerate((gz - 0.14, gz + 0.14)):
        p.append(lathe(f"KK_Burner{k}", (x0 + 0.35, 0.97, zz), [(0.0, 0.0), (0.06, 0.0), (0.06, 0.012), (0.0, 0.012)], M["steel"], 20))
        for j in range(4):
            a = j * math.pi / 2 + 0.3
            p.append(span(f"KK_Trivet{k}{j}", x0 + 0.35 + math.cos(a) * 0.06 - 0.008, x0 + 0.35 + math.cos(a) * 0.1 + 0.008, 0.97, 0.985,
                          zz + math.sin(a) * 0.06 - 0.008, zz + math.sin(a) * 0.1 + 0.008, M["black"], 0.002))
    p.append(span("KK_StoveKnobs", x1 - 0.07, x1 - 0.06, 0.9, 0.95, gz - 0.2, gz + 0.2, M["steel"], 0.004))
    px, pz = x0 + 0.35, gz + 0.14
    p.append(lathe("KK_Pot", (px, 0.985, pz), [(0.0, 0.0), (0.1, 0.0), (0.105, 0.012), (0.105, 0.13), (0.11, 0.135), (0.1, 0.135)], M["pot"], 32, cap_top=False))
    p.append(lathe("KK_Lid", (px, 1.12, pz), [(0.108, 0.0), (0.09, 0.025), (0.02, 0.04), (0.02, 0.055), (0.0, 0.058)], M["pot"], 32))
    for s in (-1, 1):
        p.append(span(f"KK_PotHandle{s}", px - 0.03, px + 0.03, 1.08, 1.1, pz + s * 0.105, pz + s * 0.135, M["black"], 0.006))
    # 炊飯器・水切りかご・まな板
    p.append(span("KK_RiceCooker", x0 + 0.08, x0 + 0.36, 0.89, 1.12, 2.0, 2.24, M["white"], 0.04, 4))
    p.append(span("KK_CookerPanel", x0 + 0.36, x0 + 0.365, 0.95, 1.02, 2.06, 2.18, M["black"], 0.005))
    p.append(span("KK_Board", x0 + 0.1, x0 + 0.5, 0.89, 0.905, 0.85, 1.15, M["wood"], 0.004))
    # 換気扇のフード（コンロの上）と吊り戸棚（z 0.75〜2.3）
    p.append(span("KK_Hood", x0, x0 + 0.5, 1.5, 1.7, 0.1, 0.72, M["steel"], 0.01))
    p.append(span("KK_HoodDuct", x0, x0 + 0.28, 1.7, H, 0.28, 0.56, M["steel"], 0.006))
    p.append(span("KK_Upper", x0, x0 + 0.38, 1.55, 2.25, 0.75, 2.3, M["cream"], 0.006))
    for k in range(3):
        a, b = 0.76 + k * 0.513, 0.76 + (k + 1) * 0.513
        p.append(span(f"KK_UDoor{k}", x0 + 0.38, x0 + 0.395, 1.57, 2.23, a + 0.004, b - 0.004, M["cream"], 0.006))
        p.append(span(f"KK_UPull{k}", x0 + 0.395, x0 + 0.41, 1.6, 1.66, b - 0.07, b - 0.05, M["wood"], 0.004))
    return [finish(o, 1.0, angle=40) for o in p]


def fridge(M):
    """冷蔵庫（ビルダーの Fridge：(-hw+0.5, *, 2.8)、0.7 x 1.8 x 0.7、前 = +X）。マグネットと学校のプリント"""
    cx, cz = -HW0 + 0.5, 2.8
    p, flat = [], []
    x0, x1 = cx - 0.35, cx + 0.35
    p.append(span("KF_Body", x0, x1, 0.02, 1.8, cz - 0.35, cz + 0.35, M["fridge"], 0.02, 3))
    xf = x1
    p.append(span("KF_Gap", xf, xf + 0.002, 1.2, 1.215, cz - 0.34, cz + 0.34, M["black"], 0))
    for (y0, y1) in ((1.3, 1.7), (0.45, 1.1)):
        p.append(span(f"KF_Handle{y0}", xf, xf + 0.03, y0, y1, cz + 0.25, cz + 0.29, M["steel"], 0.01))
    # マグネットで留めた学校のプリント（上段）
    pr = quad("KF_Print", [(xf + 0.002, 1.35, cz - 0.28), (xf + 0.002, 1.35, cz - 0.04), (xf + 0.002, 1.68, cz - 0.04), (xf + 0.002, 1.68, cz - 0.28)], M["paper"])
    hq.face_toward(pr, (1, 0, 0)); flat.append(pr)
    for (y, zz) in ((1.66, cz - 0.16), (1.1, cz - 0.12), (1.3, cz + 0.05)):
        p.append(cyl_between(f"KF_Magnet{y}", (xf + 0.002, y, zz), (xf + 0.012, y, zz), 0.014, M["magnet"], 12))
    return [finish(o, 1.0, angle=40) for o in p] + flat


def living(M):
    """テレビ台と薄型テレビ（南の壁）・ソファ（テレビへ向く）・ランドセルとくま"""
    p = []
    tx, tz = -1.8, -HD0 + 0.45
    p.append(span("KV_Board", tx - 0.7, tx + 0.7, 0.04, 0.44, tz - 0.25, tz + 0.25, M["woodD"], 0.008))
    p.append(span("KV_BoardFeet", tx - 0.66, tx + 0.66, 0.0, 0.04, tz - 0.21, tz + 0.21, M["black"], 0.004))
    for k in range(2):
        a, b = tx - 0.68 + k * 0.68, tx + k * 0.68
        p.append(span(f"KV_Drawer{k}", a + 0.004, b - 0.004, 0.08, 0.4, tz + 0.25, tz + 0.265, M["wood"], 0.006))
        p.append(span(f"KV_DPull{k}", (a + b) / 2 - 0.08, (a + b) / 2 + 0.08, 0.34, 0.36, tz + 0.265, tz + 0.28, M["steel"], 0.004))
    # テレビ（ビルダーの Tv：y 0.475〜1.125）
    p.append(span("KV_TvStand", tx - 0.14, tx + 0.14, 0.44, 0.46, tz - 0.1, tz + 0.08, M["black"], 0.006))
    p.append(span("KV_TvNeck", tx - 0.03, tx + 0.03, 0.46, 0.52, tz - 0.02, tz + 0.02, M["black"], 0.004))
    p.append(span("KV_Tv", tx - 0.55, tx + 0.55, 0.5, 1.12, tz - 0.03, tz + 0.02, M["black"], 0.01))
    scr = quad("KV_Screen", [(tx + 0.53, 0.52, tz + 0.021), (tx - 0.53, 0.52, tz + 0.021), (tx - 0.53, 1.1, tz + 0.021), (tx + 0.53, 1.1, tz + 0.021)], M["tv"])
    hq.face_toward(scr, (0, 0, 1))
    # ソファ（テレビの北、座面は南向き。x -2.55〜-1.05、z -2.6〜-1.8）
    sx, sz = -1.8, -2.2
    q = []
    q.append(span("KS_SofaBase", sx - 0.75, sx + 0.75, 0.1, 0.38, sz - 0.4, sz + 0.4, M["sofa"], 0.04, 4))
    q.append(span("KS_SofaBack", sx - 0.75, sx + 0.75, 0.3, 0.82, sz + 0.2, sz + 0.4, M["sofa"], 0.05, 4))
    for s in (-1, 1):
        q.append(span(f"KS_SofaArm{s}", *sorted((sx + s * 0.75, sx + s * 0.62)), 0.1, 0.6, sz - 0.4, sz + 0.4, M["sofa"], 0.05, 4))
        for sz2 in (-1, 1):
            q.append(dim_room.taper_leg(f"KS_SofaFoot{s}{sz2}", sx + s * 0.68, sz + sz2 * 0.34, 0.0, 0.1, 0.045, 0.03, M["woodD"]))
    for k, x in enumerate((sx - 0.31, sx + 0.31)):
        q.append(pillow(f"KS_SofaSeat{k}", (x, 0.44, sz - 0.05), (0.6, 0.13, 0.6), M["sofa"], seed=2400 + k))
    bc = pillow("KS_SofaBackCush", (sx, 0.62, sz + 0.12), (1.2, 0.12, 0.38), M["sofa"], seed=2410)
    bc.data.transform(Matrix.Translation(U(sx, 0.62, sz + 0.12)) @ Matrix.Rotation(math.radians(-80), 4, "X") @ Matrix.Translation(-U(sx, 0.62, sz + 0.12)))
    q.append(bc)
    # ランドセル（ソファの東の端）とくまのぬいぐるみ
    rx, rz = sx + 0.4, sz - 0.02
    q.append(span("KS_Randoseru", rx - 0.13, rx + 0.13, 0.52, 0.82, rz - 0.1, rz + 0.08, M["randoseru"], 0.05, 4))
    q.append(span("KS_RandoseruFlap", rx - 0.135, rx + 0.135, 0.6, 0.83, rz - 0.115, rz - 0.095, M["randoseru"], 0.04, 4))
    q.append(span("KS_RandoseruClasp", rx - 0.02, rx + 0.02, 0.6, 0.64, rz - 0.12, rz - 0.113, M["steel"], 0.004))
    bx, bz = sx - 0.45, sz + 0.02
    q.append(pillow("KS_BearBody", (bx, 0.58, bz), (0.16, 0.18, 0.13), M["bear"], seed=2420))
    q.append(lathe("KS_BearHead", (bx, 0.66, bz - 0.01), [(0.0, 0.0), (0.06, 0.01), (0.07, 0.05), (0.055, 0.1), (0.0, 0.11)], M["bear"], 20))
    for s in (-1, 1):
        q.append(lathe(f"KS_BearEar{s}", (bx + s * 0.05, 0.75, bz - 0.01), [(0.0, 0.0), (0.022, 0.005), (0.022, 0.02), (0.0, 0.028)], M["bear"], 12))
        q.append(cyl_between(f"KS_BearEye{s}", (bx + s * 0.022, 0.71, bz - 0.075), (bx + s * 0.022, 0.71, bz - 0.08), 0.006, M["black"], 8))
    return [finish(o, 1.0, angle=40) for o in p] + [scr] + [finish(o, 2.0, angle=55) for o in q]


def sideboard(M):
    """サイドボード（ビルダーの Sideboard：(hw-0.55, *, -1.8)、0.5 x 0.9 x 1.6。前 = -X）。家族写真と小さな植物"""
    cx, cz = SIDEB
    p, flat = [], []
    x0, x1 = cx - 0.25, cx + 0.25
    xf = x0
    p.append(span("KB_Body", x0 + 0.01, x1, 0.08, 0.88, cz - 0.8, cz + 0.8, M["wood"], 0.006))
    p.append(span("KB_Top", x0 - 0.01, x1, 0.88, 0.9, cz - 0.81, cz + 0.81, M["wood"], 0.006))
    p.append(span("KB_Plinth", x0 + 0.04, x1 - 0.02, 0.0, 0.08, cz - 0.76, cz + 0.76, M["woodD"], 0.004))
    for k in range(2):
        a, b = cz - 0.78 + k * 0.78, cz + k * 0.78
        p.append(span(f"KB_Slide{k}", xf - 0.012 * (k + 1), xf, 0.12, 0.84, a + 0.004 - 0.04 * k, b - 0.004 + 0.04 * (1 - k), M["woodD"], 0.006))
        p.append(span(f"KB_Pull{k}", xf - 0.012 * (k + 1) - 0.004, xf - 0.012 * (k + 1), 0.45, 0.55, (b if k == 0 else a) - 0.05, (b if k == 0 else a) + 0.05, M["black"], 0.002))
    # 家族写真（南端、部屋側を向く）
    fx, fz = cx - 0.02, cz - 0.62
    p.append(span("KB_PFrame", fx - 0.012, fx + 0.012, 0.9, 1.1, fz - 0.13, fz + 0.13, M["woodD"], 0.006))
    p.append(span("KB_PLeg", fx + 0.012, fx + 0.07, 0.9, 1.04, fz - 0.02, fz + 0.02, M["woodD"], 0.003))
    # 見る人（西から +X を見る）の左は +Z
    ph = quad("KB_Photo", [(fx - 0.013, 0.92, fz + 0.11), (fx - 0.013, 0.92, fz - 0.11), (fx - 0.013, 1.08, fz - 0.11), (fx - 0.013, 1.08, fz + 0.11)], M["photo"])
    hq.face_toward(ph, (-1, 0, 0)); flat.append(ph)
    # 小さな鉢植え（北端）
    px, pz = cx + 0.05, cz + 0.65
    p.append(lathe("KB_Pot", (px, 0.9, pz), [(0.0, 0.0), (0.05, 0.0), (0.065, 0.1), (0.06, 0.1), (0.0, 0.09)], M["terracotta"], 20))
    rnd = random.Random(2451)
    for k in range(7):
        a = rnd.uniform(0, math.pi * 2)
        tip = (px + math.cos(a) * 0.08, 1.08 + rnd.uniform(0, 0.06), pz + math.sin(a) * 0.08)
        p.append(cyl_between(f"KB_Leaf{k}", (px, 0.99, pz), tip, 0.014, M["leaf"], 6, r1=0.004))
    return [finish(o, 1.0, angle=40) for o in p] + flat


def wall_items(M):
    """北の壁の子供の絵とカレンダー、東の壁の時計、冷蔵庫の脇の観葉植物"""
    p, flat = [], []
    zn = HD - 0.003
    # 見る人（北の壁を +Z に見る）の左は -X
    d = quad("KI_Drawings", [(-2.5, 1.2, zn), (-1.3, 1.2, zn), (-1.3, 1.8, zn), (-2.5, 1.8, zn)], M["drawings"])
    hq.face_toward(d, (0, 0, -1)); flat.append(d)
    for x in (-2.45, -1.95, -1.35):
        p.append(cyl_between(f"KI_Tape{x}", (x, 1.79, zn), (x, 1.79, zn - 0.002), 0.02, M["paper"], 8))
    c = quad("KI_Calendar", [(1.3, 1.2, zn), (1.7, 1.2, zn), (1.7, 1.76, zn), (1.3, 1.76, zn)], M["cal"])
    hq.face_toward(c, (0, 0, -1)); flat.append(c)
    p.append(cyl_between("KI_CalPin", (1.5, 1.78, HD), (1.5, 1.78, HD - 0.02), 0.004, M["steel"], 6))
    cx, cy, cz = HW, 2.0, 1.2
    p.append(cyl_between("KI_ClockRim", (cx, cy, cz), (cx - 0.05, cy, cz), 0.16, M["woodD"], 48))
    p.append(cyl_between("KI_ClockFace", (cx - 0.05, cy, cz), (cx - 0.052, cy, cz), 0.14, M["clockface"], 48))
    for k in range(12):
        a = math.pi * 2 * k / 12
        y, z = cy + math.cos(a) * 0.115, cz + math.sin(a) * 0.115
        p.append(span(f"KI_Tick{k}", cx - 0.054, cx - 0.052, y - 0.01, y + 0.01, z - 0.004, z + 0.004, M["black"], 0))
    p.append(cyl_between("KI_H", (cx - 0.056, cy, cz), (cx - 0.056, cy - 0.05, cz + 0.03), 0.004, M["black"], 6))
    p.append(cyl_between("KI_M", (cx - 0.058, cy, cz), (cx - 0.058, cy + 0.1, cz + 0.02), 0.003, M["black"], 6))
    out = [finish(o, 2.0, angle=40) for o in p] + flat
    objs = lab_room.plant({"pot": M["terracotta"], "soil": M["soil"], "stem": M["stem"], "leaf": M["leaf"]}, seed=2461)
    src = (-lab_room.HW + 0.45, lab_room.HD - 0.45)
    dst = (HW0 - 0.45, -HD0 + 0.5)
    mv = Matrix.Translation(U(dst[0] - src[0], 0, dst[1] - src[1]) - U(0, 0, 0))
    sc = Matrix.Translation(U(dst[0], 0, dst[1])) @ Matrix.Scale(0.85, 4) @ Matrix.Translation(-U(dst[0], 0, dst[1]))
    for o in objs:
        o.data.transform(mv)
        o.data.transform(sc)
    return out + objs


def interior(M):
    out = []
    out += kitchen(M)
    out += fridge(M)
    out += living(M)
    out += sideboard(M)
    out += wall_items(M)
    tx, tz = TABLE
    rug = quad("KR_Rug", [(tx - 1.3, 0.01, tz - 1.1), (tx + 1.3, 0.01, tz - 1.1), (tx + 1.3, 0.01, tz + 1.1), (tx - 1.3, 0.01, tz + 1.1)], M["rug"])
    hq.face_toward(rug, (0, 1, 0))
    s = rug.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.01
    out.append(finish(rug, keep_uv=True, angle=40))
    return out


# ============================== 資料と手帳 ==============================

def _sheet(name, w, d, y, mat):
    q = quad(name, [(-w / 2, y, -d / 2), (w / 2, y, -d / 2), (w / 2, y, d / 2), (-w / 2, y, d / 2)], mat)
    hq.face_toward(q, (0, 1, 0))
    return q


def _read_from_west(o, extra=0.0):
    """サイドボードの上の物を、部屋側（西）から読める向きへ（文字の上 = +X）"""
    o.data.transform(Matrix.Rotation(math.radians(-90 + extra), 4, "Z"))


def drawing(M):
    """冷蔵庫の子供の絵（ビルダーの drawing：箱 0.03 x 0.28 x 0.22、冷蔵庫の前面 = +X）"""
    xf = -0.015 + 0.006
    q = quad("DR_Paper", [(xf, -0.14, -0.105), (xf, -0.14, 0.105), (xf, 0.14, 0.105), (xf, 0.14, -0.105)], M["drawing"])
    hq.face_toward(q, (1, 0, 0))
    q.data.transform(Matrix.Translation(U(xf, 0, 0)) @ Matrix.Rotation(math.radians(3), 4, "Y") @ Matrix.Translation(-U(xf, 0, 0)))
    mg = [finish(cyl_between(f"DR_Magnet{k}", (xf, 0.12, z), (xf + 0.01, 0.12, z), 0.013, M["magnet"] if k else M["lacq"], 12), 2.0) for k, z in enumerate((-0.08, 0.08))]
    return [join([q] + mg, "KurodaHome_Drawing")]


def rules(M):
    """黒田の手帳（茶色の革、開いている）"""
    cover = finish(span("RU_Cover", -0.15, 0.15, -0.01, -0.005, -0.1, 0.1, M["cover2"], 0.003), 2.0)
    pg = _sheet("RU_Pages", 0.29, 0.2, -0.004, M["rules"])
    one = join([cover, pg], "KurodaHome_Rules")
    _read_from_west(one, -6)
    return [one]


def lastrec(M):
    """IC レコーダー（破損）と、下に敷いた書き起こしのメモ"""
    p = [span("LR_Body", -0.025, 0.025, -0.01, 0.006, -0.06, 0.06, M["black"], 0.006, 3),
         span("LR_Grille", -0.016, 0.016, 0.006, 0.007, 0.03, 0.052, M["steel"], 0.001)]
    lcd = quad("LR_Lcd", [(-0.016, 0.0072, -0.02), (0.016, 0.0072, -0.02), (0.016, 0.0072, 0.02), (-0.016, 0.0072, 0.02)], M["lcd"],
               uv=((0, 0), (0, 1), (1, 1), (1, 0)))
    hq.face_toward(lcd, (0, 1, 0))
    for o in p:
        finish(o, 4.0, angle=40)
    dev = join(p + [lcd], "LR_Device")
    dev.data.transform(Matrix.Translation(U(0.03, 0, 0.01)) @ Matrix.Rotation(math.radians(20), 4, "Z"))
    memo = _sheet("LR_Memo", 0.15, 0.1, -0.0095, M["paper"])
    memo.data.transform(Matrix.Rotation(math.radians(-8), 4, "Z"))
    return [join([dev, memo], "KurodaHome_LastRec")]


def verdict(M):
    """白紙の頁を開いた黒い手帳とペン（判定で書き込む）"""
    cover = finish(span("VD_Cover", -0.11, 0.11, -0.015, -0.009, -0.075, 0.075, M["cover"], 0.003), 2.0)
    pg = _sheet("VD_Pages", 0.21, 0.145, -0.0085, M["blank"])
    pen = finish(cyl_between("VD_Pen", (0.02, -0.004, 0.09), (0.15, -0.004, 0.03), 0.005, M["black"], 10), 4.0)
    cap = finish(cyl_between("VD_PenCap", (0.12, -0.004, 0.044), (0.15, -0.004, 0.03), 0.0055, M["steel"], 10), 4.0)
    one = join([cover, pg, pen, cap], "KurodaHome_Verdict")
    _read_from_west(one, 4)
    return [one]


def door(M):
    return [join(dim_room.door({"woodDark": M["woodD"], "wood": M["wood"], "brass": M["steel"]}), "KurodaHome_Door")]


def _breaker_mats(M):
    return {"mel": M["white"], "grille": M["black"], "hazard": M["hazard"], "sus": M["steel"], "rubber": M["black"], "lever": M["lever"]}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.02), "KurodaHome_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "KurodaHome_Lever")]


PIECES = {"Shell": shell, "Lamps": lamps, "Interior": interior, "Dining": dining,
          "Drawing": drawing, "Rules": rules, "LastRec": lastrec, "Verdict": verdict,
          "Door": door, "Breaker": breaker, "Lever": lever}
