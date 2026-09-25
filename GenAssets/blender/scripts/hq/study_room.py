"""
所長の書斎（study：幅5.5 x 奥行7 x 天井2.9）を品質重視で作る。コンセプト concept_02_study 準拠：
太い梁と板の天井・古い漆喰の壁と濃い柱・濃い床板・裸電球・壁一面の本棚（本を1冊ずつ）・両袖机。

  Shell      部屋の原点。床・壁・柱と長押・巾木・梁と天井板・裸電球・スイッチ
  Interior   部屋の原点。本棚3台（西1・東2。東は配電盤を挟む）と本、額の書、柱時計
  Desk       両袖机（ユニットの原点、引き出しは -Z、天板の上面 y=0.75）
  Chair      木の椅子（原点、机の方 = +Z）
  SideTable  一本脚の小卓（原点、天板の上面 y=0.575）
  Safe       壁の金庫（金庫の箱の中心が原点、前面 -Z）
  Photo      伏せた写真立て（箱の中心が原点）
  Document / Scrap / Envelope   資料の見た目（箱の中心が原点）
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
import dim_room

W, D, H = 5.5, 7.0, 2.9
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
CASE_D, CASE_H = 0.36, 2.1
CASES = [(-1, -3.0, 1.9), (1, -3.0, -0.45), (1, 0.45, 1.9)]      # (側, z0, z1)


def mats():
    M = {}
    M["plaster"] = hq.mat("STD_Plaster", (1, 1, 1), 0.85, tex="plaster_old.png")
    M["floor"] = hq.mat("STD_FloorPlanks", (1, 1, 1), 0.55, tex="floor_planks.png")
    M["wood"] = hq.mat("STD_DarkWood", (1, 1, 1), 0.5, tex="dark_wood.png")
    M["woodL"] = hq.mat("STD_WoodLight", (1.35, 1.3, 1.25), 0.5, tex="dark_wood.png")
    M["book"] = hq.mat("STD_BookSpine", (1, 1, 1), 0.7, tex="book_atlas.png")
    M["pages"] = hq.mat("STD_BookPages", (1, 1, 1), 0.9, tex="book_pages.png")
    M["brass"] = hq.mat("STD_Brass", (0.72, 0.55, 0.3), 0.35, 1.0)
    M["bulb"] = hq.mat("STD_Bulb", (1, 0.95, 0.8), 0.2, emit=(1.0, 0.8, 0.5), emit_strength=12.0)
    M["porcelain"] = hq.mat("STD_Porcelain", (0.9, 0.88, 0.84), 0.2)
    M["cord"] = hq.mat("STD_Cord", (0.08, 0.07, 0.06), 0.6)
    M["safe"] = hq.mat("STD_SafeGreen", (0.2, 0.25, 0.22), 0.45, 0.4)
    M["chrome"] = hq.mat("STD_Chrome", (0.8, 0.8, 0.8), 0.15, 1.0)
    M["dial"] = hq.mat("STD_DialFace", (0.78, 0.74, 0.62), 0.3, 0.8)
    M["black"] = hq.mat("STD_Black", (0.03, 0.03, 0.03), 0.5)
    M["glass"] = hq.mat("STD_Glass", (0.05, 0.05, 0.05), 0.05)
    M["paper"] = hq.mat("STD_DocReport", (1, 1, 1), 0.9, tex="doc_report.png")
    M["scrap"] = hq.mat("STD_DocScrap", (1, 1, 1), 0.9, tex="doc_scrap.png")
    M["env"] = hq.mat("STD_Envelope", (1, 1, 1), 0.9, tex="envelope.png")
    M["note"] = hq.mat("STD_FrameNote", (1, 1, 1), 0.9, tex="frame_note.png")
    M["plain"] = hq.mat("STD_PaperPlain", (0.9, 0.87, 0.78), 0.9)
    M["ink"] = hq.mat("STD_Ink", (0.02, 0.02, 0.06), 0.1)
    M["plate"] = hq.mat("STD_SwitchPlate", (0.86, 0.83, 0.76), 0.3)
    M["clockface"] = hq.mat("STD_ClockFace", (0.9, 0.87, 0.78), 0.4)
    return M


# ============================== 外殻 ==============================

def shell(M):
    out = []
    fl = span("SS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["floor"], bev=0)
    out.append(finish(fl, 1.0, rot90=True))                        # 床板は奥行き方向
    walls = []
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"SS_WallNS{zs}", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["plaster"])
    for xs in (-1, 1):
        walls.append(span(f"SS_WallEW{xs}", *sorted((xs * HW, xs * HW0)), 0.0, H, -HD0, HD0, M["plaster"], bev=0))
    out += [finish(o, 1.0, angle=30) for o in walls]
    # 天井板（濃い木、幅 18cm の板）と梁
    out.append(finish(span("SS_Ceil", -HW0, HW0, H, H + 0.08, -HD0, HD0, M["wood"], bev=0), 1.0))
    wood = []
    x = -HW
    k = 0
    while x < HW:
        wood.append(span(f"SS_Board{k}", x + 0.003, min(HW, x + 0.18) - 0.003, H - 0.015, H, -HD, HD, M["wood"], 0.003))
        x += 0.18; k += 1
    for i, z in enumerate((-2.2, -0.4, 1.4)):
        wood.append(span(f"SS_Beam{i}", -HW, HW, H - 0.24, H - 0.015, z - 0.1, z + 0.1, M["wood"], 0.012, 3))
    wood.append(span("SS_BeamLong", -0.09, 0.09, H - 0.2, H - 0.015, -HD, HD, M["wood"], 0.01, 3))
    # 真壁の柱（四隅と長い壁の中ほど）、長押、巾木
    for xs in (-1, 1):
        for zs in (-1, 1):
            wood.append(span(f"SS_Post{xs}{zs}", *sorted((xs * HW, xs * (HW - 0.12))), 0, H, *sorted((zs * HD, zs * (HD - 0.12))), M["wood"], 0.008))
    rail_y0, rail_y1 = DOOR_H + 0.02, DOOR_H + 0.12
    for zs in (-1, 1):
        wood.append(span(f"SS_RailNS{zs}", -HW, HW, rail_y0, rail_y1, *sorted((zs * HD, zs * (HD - 0.05))), M["wood"], 0.006))
        for (x0, x1) in ((-HW, -DOOR_HALF - 0.08), (DOOR_HALF + 0.08, HW)):
            wood.append(span(f"SS_Base{zs}{x0:.0f}", x0, x1, 0, 0.1, *sorted((zs * HD, zs * (HD - 0.02))), M["wood"], 0.004))
        # 扉の枠（太い木の枠）と縦枠
        zin = zs * HD
        for xs in (-1, 1):
            wood.append(span(f"SS_Frame{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.08))), 0, DOOR_H + 0.02,
                             *sorted((zin, zin - zs * 0.03)), M["wood"], 0.006))
            wood.append(span(f"SS_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), M["wood"], 0.003))
        wood.append(span(f"SS_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H + 0.02, *sorted((zs * HD0, zs * HD)), M["wood"], 0.003))
    for xs in (-1, 1):
        wood.append(span(f"SS_RailEW{xs}", *sorted((xs * HW, xs * (HW - 0.05))), rail_y0, rail_y1, -HD, HD, M["wood"], 0.006))
        wood.append(span(f"SS_BaseEW{xs}", *sorted((xs * HW, xs * (HW - 0.02))), 0, 0.1, -HD, HD, M["wood"], 0.004))
    out += [finish(o, 1.0, angle=40) for o in wood]
    # 裸電球（ビルダーの RoomLight の z=±1.75）：天井の座・コード・陶器のソケット・電球
    for i, z in enumerate((-1.75, 1.75)):
        # 光源（ビルダーの RoomLight、y=2.2）が電球の中に入ると影で光が遮られるので、電球はその少し上
        parts = [lathe(f"SS_Rose{i}", (0, H - 0.25, z), [(0.05, 0), (0.05, 0.02), (0.02, 0.035)], M["porcelain"], 24),
                 cyl(f"SS_Cord{i}", (0, 2.45, z), H - 0.25 - 2.45, 0.004, M["cord"], 8),
                 lathe(f"SS_Socket{i}", (0, 2.37, z), [(0.018, 0), (0.022, 0.01), (0.022, 0.07), (0.014, 0.085)], M["porcelain"], 24)]
        b = lathe(f"SS_Bulb{i}", (0, 2.26, z), [(0.0, 0.0), (0.02, 0.005), (0.032, 0.03), (0.03, 0.07), (0.016, 0.1), (0.014, 0.112)], M["bulb"], 24)
        parts.append(b)
        out += [finish(o, 1.0, angle=70) for o in parts]
    # 古いスイッチ（入口の脇）
    zS = -HD
    sw = [span("SS_Switch", 0.72, 0.84, 1.2, 1.32, zS, zS + 0.012, M["plate"], 0.004),
          cyl_between("SS_Toggle", (0.78, 1.26, zS + 0.012), (0.78, 1.28, zS + 0.035), 0.006, M["brass"], 10)]
    out += [finish(o, 1.0, angle=40) for o in sw]
    return out


# ============================== 本棚と本 ==============================

def bookcase(name, side, z0, z1, M, rnd):
    """壁付けの本棚（前面は部屋側）。ベイごとの縦板・棚板・台輪・笠木"""
    p = []
    xb = side * HW                       # 背板（壁）
    xf = side * (HW - CASE_D)            # 前面
    xa, xz = sorted((xb, xf))
    p.append(span(f"{name}_Back", *sorted((xb, xb - side * 0.012)), 0.1, CASE_H, z0, z1, M["wood"], 0))
    p.append(span(f"{name}_Plinth", xa, xz, 0.0, 0.1, z0, z1, M["wood"], 0.006))
    p.append(span(f"{name}_Cap", xa - (0.02 if side > 0 else 0), xz + (0.02 if side < 0 else 0), CASE_H, CASE_H + 0.04,
                  z0 - 0.02, z1 + 0.02, M["wood"], 0.01, 3))
    bays = max(1, round((z1 - z0) / 1.0))
    bw = (z1 - z0) / bays
    for b in range(bays + 1):
        z = z0 + b * bw
        p.append(span(f"{name}_Div{b}", xa, xz, 0.1, CASE_H, z - 0.012 if 0 < b < bays else (z if b == 0 else z - 0.024),
                      z + 0.012 if 0 < b < bays else (z + 0.024 if b == 0 else z), M["wood"], 0.004))
    shelves = [0.1, 0.43, 0.76, 1.09, 1.42, 1.75]
    for s in shelves[1:]:
        p.append(span(f"{name}_Shelf{s}", xa, xz, s - 0.022, s, z0, z1, M["wood"], 0.004))
    books = []
    for b in range(bays):
        za, zb = z0 + b * bw + 0.014, z0 + (b + 1) * bw - 0.014
        for s in shelves:
            books += shelf_books(f"{name}_B{b}_{s}", side, za, zb, s, s + 0.33 - 0.022, xf, rnd)
    # 上の段の上にも横積みの本（笠木の上。写真立てを置く場所は空ける）
    return p, books


def shelf_books(name, side, za, zb, y0, y1, xf, rnd):
    """棚1段分の本（幅・高さ・奥行き・色をばらつかせ、たまに倒れた本・横積み・隙間）"""
    out = []
    z = za + rnd.uniform(0, 0.05)
    maxh = y1 - y0 - 0.02
    while z < zb - 0.02:
        r = rnd.random()
        if r < 0.06:                                  # 隙間
            z += rnd.uniform(0.04, 0.12); continue
        if r < 0.1 and zb - z > 0.3:                  # 横積み 3〜5冊
            n = rnd.randint(3, 5)
            yy = y0
            w = rnd.uniform(0.2, 0.26)
            for k in range(n):
                t = rnd.uniform(0.025, 0.04)
                out.append(("flat", z, z + w, yy, yy + t, rnd.uniform(0.15, 0.2), rnd.randrange(16), rnd.uniform(-0.1, 0.1)))
                yy += t
            z += w + 0.01; continue
        w = rnd.uniform(0.022, 0.06)
        hh = min(maxh, rnd.uniform(0.18, 0.29))
        dep = rnd.uniform(0.15, 0.22)
        lean = 0.0
        if r > 0.95:
            lean = rnd.uniform(0.15, 0.35)            # もたれかかった本
        out.append(("up", z, z + w, y0, y0 + hh, dep, rnd.randrange(16), lean))
        z += w + (hh * math.sin(lean) if lean else 0.0) + rnd.uniform(0.0, 0.004)
    return [(name, side, xf) + b for b in out if b[2] <= zb]


def build_books(name, items, M):
    """本の一覧を1つのメッシュに（背表紙=アトラスの1コマ、天地と小口=紙、表紙=アトラスの無地部分）"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    for (bn, side, xf, kind, z0, z1, y0, y1, dep, cell, extra) in items:
        cu, cv = (cell % 4) / 4, 1 - (cell // 4 + 1) / 4          # アトラスのコマ（左下）
        inset = 0.01 + (0.03 if kind == "up" else 0.0) * abs(math.sin(z0 * 37))
        x_front = xf + side * inset                                           # 前面から少し奥（壁側）へ
        x_back = x_front + side * dep
        corners = []
        if kind == "up":
            lean = extra
            # 傾ける（z0 の下端を軸に +Z 方向へ倒す）
            def P(x, y, z, lean=lean, z0=z0, y0=y0):
                dz, dy = z - z0, y - y0
                return (x, y0 + dy * math.cos(lean) - dz * math.sin(lean), z0 + dz * math.cos(lean) + dy * math.sin(lean))
        else:
            def P(x, y, z):
                return (x, y, z)
        xs = (x_front, x_back)
        v = {}
        for i, x in enumerate(xs):
            for j, y in enumerate((y0, y1)):
                for k, z in enumerate((z0, z1)):
                    v[(i, j, k)] = bm.verts.new(U(*P(x, y, z)))
        # 面：(頂点キー, 材質, UV)  背表紙=前面（i=0）
        spine_uv = [(cu + 0.02, cv + 0.02), (cu + 0.23, cv + 0.02), (cu + 0.23, cv + 0.23), (cu + 0.02, cv + 0.23)]
        cover_uv = [(cu + 0.05, cv + 0.08), (cu + 0.06, cv + 0.08), (cu + 0.06, cv + 0.09), (cu + 0.05, cv + 0.09)]
        page_uv = [(0, 0), (1, 0), (1, 1), (0, 1)]
        if kind == "up":
            faces = [([(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)], 0, spine_uv),
                     ([(1, 0, 1), (1, 0, 0), (1, 1, 0), (1, 1, 1)], 1, page_uv),
                     ([(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)], 1, page_uv),
                     ([(1, 0, 0), (1, 0, 1), (0, 0, 1), (0, 0, 0)], 1, page_uv),
                     ([(1, 0, 0), (0, 0, 0), (0, 1, 0), (1, 1, 0)], 0, cover_uv),
                     ([(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)], 0, cover_uv)]
        else:                                           # 横積み：背表紙は手前の側面
            faces = [([(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)], 0, [(cu + 0.02, cv + 0.02), (cu + 0.02, cv + 0.23),
                                                                        (cu + 0.23, cv + 0.23), (cu + 0.23, cv + 0.02)]),
                     ([(1, 0, 1), (1, 0, 0), (1, 1, 0), (1, 1, 1)], 1, page_uv),
                     ([(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)], 0, cover_uv),
                     ([(1, 0, 0), (1, 0, 1), (0, 0, 1), (0, 0, 0)], 0, cover_uv),
                     ([(1, 0, 0), (0, 0, 0), (0, 1, 0), (1, 1, 0)], 1, page_uv),
                     ([(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)], 1, page_uv)]
        for keys, mi, uvs in faces:
            try:
                f = bm.faces.new([v[k] for k in keys])
            except ValueError:
                continue
            f.material_index = mi
            for l, t in zip(f.loops, uvs):
                l[uvl].uv = t
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    o = hq._obj(name, me, M["book"])
    o.data.materials.append(M["pages"])
    hq.smooth(o, 20)
    return o


def interior(M, seed=501):
    rnd = random.Random(seed)
    out = []
    all_books = []
    for i, (side, z0, z1) in enumerate(CASES):
        p, books = bookcase(f"SI_Case{i}", side, z0, z1, M, rnd)
        out += [finish(o, 1.0, angle=40) for o in p]
        all_books += books
    # 笠木の上の横積み（西の本棚の上と、東の北の本棚の上。東の南は写真立ての場所を空ける）
    for side, zs in ((-1, (-2.6, -1.2, 0.6)), (1, (1.0,))):
        for z in zs:
            yy = CASE_H + 0.04
            for k in range(rnd.randint(2, 4)):
                t = rnd.uniform(0.03, 0.05)
                all_books.append((f"SI_Top{z}", side, side * (HW - 0.34), "flat", z, z + rnd.uniform(0.22, 0.28), yy, yy + t,
                                  rnd.uniform(0.16, 0.2), rnd.randrange(16), 0.0))
                yy += t
    out.append(build_books("SI_Books", all_books, M))
    # 額の書（南の壁の西側）と柱時計（北の壁の東側）
    x0, x1, y0, y1 = -2.0, -1.4, 1.35, 2.2
    zf = -HD + 0.025
    fr = [span("SI_FrameL", x0 - 0.04, x0, y0 - 0.04, y1 + 0.04, -HD, zf, M["wood"], 0.006),
          span("SI_FrameR", x1, x1 + 0.04, y0 - 0.04, y1 + 0.04, -HD, zf, M["wood"], 0.006),
          span("SI_FrameT", x0, x1, y1, y1 + 0.04, -HD, zf, M["wood"], 0.006),
          span("SI_FrameB", x0, x1, y0 - 0.04, y0, -HD, zf, M["wood"], 0.006)]
    note = quad("SI_Note", [(x1, y0, zf - 0.012), (x0, y0, zf - 0.012), (x0, y1, zf - 0.012), (x1, y1, zf - 0.012)], M["note"])
    hq.face_toward(note, (0, 0, 1))
    out += [finish(o, 1.0, angle=40) for o in fr] + [note]
    out += wall_clock(M)
    return out


def wall_clock(M):
    """北の壁の東側：止まった柱時計"""
    p = []
    cx, zb = 1.6, HD
    p.append(span("SC_Case", cx - 0.17, cx + 0.17, 1.2, 2.0, zb - 0.14, zb, M["wood"], 0.01, 3))
    p.append(span("SC_Crown", cx - 0.2, cx + 0.2, 2.0, 2.08, zb - 0.16, zb, M["wood"], 0.012, 3))
    zf = zb - 0.141
    face = cyl_between("SC_Face", (cx, 1.78, zf + 0.004), (cx, 1.78, zf), 0.12, M["clockface"], 40)
    p.append(face)
    p.append(cyl_between("SC_Bezel", (cx, 1.78, zf), (cx, 1.78, zf - 0.01), 0.132, M["brass"], 40))
    for k in range(12):
        a = math.pi * 2 * k / 12
        x, y = cx + math.sin(a) * 0.1, 1.78 + math.cos(a) * 0.1
        p.append(span(f"SC_Tick{k}", x - 0.004, x + 0.004, y - 0.01, y + 0.01, zf - 0.002, zf, M["black"], 0))
    p.append(cyl_between("SC_HandH", (cx, 1.78, zf - 0.003), (cx - 0.05, 1.8, zf - 0.003), 0.003, M["black"], 6))
    p.append(cyl_between("SC_HandM", (cx, 1.78, zf - 0.004), (cx + 0.02, 1.87, zf - 0.004), 0.002, M["black"], 6))
    # 振り子の窓（ガラス）と振り子
    p.append(span("SC_Window", cx - 0.12, cx + 0.12, 1.26, 1.6, zf - 0.003, zf, M["glass"], 0.003))
    p.append(cyl_between("SC_Rod", (cx, 1.6, zf + 0.03), (cx, 1.34, zf + 0.03), 0.003, M["brass"], 6))
    p.append(cyl_between("SC_Bob", (cx, 1.34, zf + 0.025), (cx, 1.34, zf + 0.035), 0.04, M["brass"], 24))
    return [finish(o, 1.0, angle=40) for o in p]


# ============================== 机・椅子・小卓・金庫 ==============================

def drop_handle(name, x, y, z, M):
    """真鍮の吊り手（座金2つと弧のハンドル）"""
    p = [cyl_between(f"{name}_R0", (x - 0.035, y, z), (x - 0.035, y, z - 0.006), 0.009, M["brass"], 12),
         cyl_between(f"{name}_R1", (x + 0.035, y, z), (x + 0.035, y, z - 0.006), 0.009, M["brass"], 12),
         pipe(f"{name}_Bail", [(x - 0.035, y, z - 0.008), (x - 0.035, y - 0.02, z - 0.012), (x + 0.035, y - 0.02, z - 0.012),
                               (x + 0.035, y, z - 0.008)], 0.0035, M["brass"], 0.012)]
    return p


def desk(M):
    p = []
    p.append(span("SD_Top", -0.72, 0.72, 0.705, 0.75, -0.37, 0.37, M["wood"], 0.012, 4))
    for xs in (-1, 1):
        x0, x1 = sorted((xs * 0.28, xs * 0.7))
        p.append(span(f"SD_Ped{xs}", x0, x1, 0.06, 0.705, -0.32, 0.33, M["wood"], 0.006))
        p.append(span(f"SD_Plinth{xs}", x0 - 0.01, x1 + 0.01, 0.0, 0.06, -0.33, 0.34, M["wood"], 0.008))
        for k in range(4):
            y0 = 0.09 + k * 0.15
            p.append(span(f"SD_Drawer{xs}{k}", x0 + 0.02, x1 - 0.02, y0, y0 + 0.13, -0.335, -0.32, M["wood"], 0.008))
            p += drop_handle(f"SD_H{xs}{k}", (x0 + x1) / 2, y0 + 0.085, -0.335, M)
    p.append(span("SD_Center", -0.28, 0.28, 0.6, 0.705, -0.32, 0.3, M["wood"], 0.004))
    p.append(span("SD_CenterDr", -0.26, 0.26, 0.61, 0.695, -0.335, -0.32, M["wood"], 0.006))
    p += drop_handle("SD_HC", 0.0, 0.66, -0.335, M)
    p.append(span("SD_Modesty", -0.28, 0.28, 0.06, 0.6, 0.26, 0.28, M["wood"], 0.004))
    # 机の上：本の山・インク壺とペン
    p.append(span("SD_BookA", 0.36, 0.62, 0.75, 0.79, 0.08, 0.3, M["woodL"], 0.004))
    p.append(span("SD_BookB", 0.38, 0.6, 0.79, 0.82, 0.1, 0.28, M["plain"], 0.003))
    p.append(lathe("SD_Ink", (0.28, 0.75, 0.24), [(0.0, 0.0), (0.03, 0.0), (0.032, 0.035), (0.015, 0.05), (0.012, 0.065), (0.0, 0.066)], M["ink"], 20))
    p.append(cyl_between("SD_Pen", (0.12, 0.753, 0.22), (0.3, 0.756, 0.16), 0.004, M["black"], 8))
    for o in p:
        finish(o, 1.0, angle=40)
    return [join(p, "Study_Desk")]


def chair(M):
    return [join(dim_room.chair({"wood": M["woodL"]}), "Study_Chair")]


def side_table(M):
    p = [span("ST_Top", -0.3, 0.3, 0.525, 0.575, -0.25, 0.25, M["wood"], 0.012, 4),
         lathe("ST_Post", (0, 0.06, 0), [(0.04, 0), (0.045, 0.05), (0.03, 0.12), (0.035, 0.3), (0.028, 0.42), (0.04, 0.465)], M["wood"], 24)]
    for k in range(3):
        a = math.pi * 2 * k / 3
        p.append(pipe(f"ST_Foot{k}", [(0, 0.1, 0), (math.cos(a) * 0.12, 0.06, math.sin(a) * 0.12), (math.cos(a) * 0.24, 0.015, math.sin(a) * 0.24)],
                      0.018, M["wood"], 0.08))
    for o in p:
        finish(o, 1.0, angle=45)
    return [join(p, "Study_SideTable")]


def safe(M):
    """壁の金庫（0.6 x 0.5 x 0.3 の箱の中心が原点。前面 -Z）"""
    p = []
    p.append(span("SF_Body", -0.3, 0.3, -0.25, 0.25, -0.13, 0.15, M["safe"], 0.02, 3))
    p.append(span("SF_Door", -0.26, 0.26, -0.21, 0.21, -0.15, -0.13, M["safe"], 0.012, 3))
    p.append(span("SF_Stripe", -0.24, 0.24, -0.19, -0.185, -0.1505, -0.149, M["brass"], 0))
    p.append(span("SF_Stripe2", -0.24, 0.24, 0.185, 0.19, -0.1505, -0.149, M["brass"], 0))
    # ダイヤル（目盛り付き）と取っ手、鍵穴、蝶番
    p.append(cyl_between("SF_DialBase", (0, 0.04, -0.15), (0, 0.04, -0.162), 0.075, M["chrome"], 48))
    p.append(cyl_between("SF_DialFace", (0, 0.04, -0.162), (0, 0.04, -0.166), 0.06, M["dial"], 48))
    p.append(lathe("SF_Knob", (0, 0, 0), [(0.03, 0), (0.028, 0.018), (0.02, 0.028), (0.0, 0.03)], M["black"], 32))
    kn = p[-1]
    kn.data.transform(Matrix.Translation(U(0, 0.04, -0.166)) @ Matrix.Rotation(math.radians(-90), 4, "X"))   # 前(-Z)へ突き出す
    for k in range(40):
        a = math.pi * 2 * k / 40
        L = 0.012 if k % 5 == 0 else 0.006
        r0 = 0.058
        x, y = math.sin(a) * r0, 0.04 + math.cos(a) * r0
        x1, y1 = math.sin(a) * (r0 - L), 0.04 + math.cos(a) * (r0 - L)
        p.append(cyl_between(f"SF_Tick{k}", (x, y, -0.1665), (x1, y1, -0.1665), 0.0012, M["black"], 4))
    hub = (0.16, -0.08, -0.15)
    p.append(cyl_between("SF_HubBase", hub, (hub[0], hub[1], hub[2] - 0.02), 0.022, M["chrome"], 24))
    for k in range(3):
        a = math.pi * 2 * k / 3 + 0.4
        p.append(cyl_between(f"SF_Spoke{k}", (hub[0], hub[1], hub[2] - 0.02), (hub[0] + math.cos(a) * 0.07, hub[1] + math.sin(a) * 0.07, hub[2] - 0.035), 0.006, M["chrome"], 10))
        p.append(lathe(f"SF_Ball{k}", (hub[0] + math.cos(a) * 0.07, hub[1] + math.sin(a) * 0.07 - 0.012, hub[2] - 0.035),
                       [(0.0, 0.0), (0.011, 0.006), (0.012, 0.012), (0.011, 0.018), (0.0, 0.024)], M["chrome"], 16))
    p.append(span("SF_Keyhole", -0.03, 0.03, -0.14, -0.1, -0.152, -0.15, M["brass"], 0.004))
    for y in (-0.15, 0.15):
        p.append(cyl_between(f"SF_Hinge{y}", (-0.27, y - 0.04, -0.15), (-0.27, y + 0.04, -0.15), 0.012, M["chrome"], 16))
    # 壁の中に埋める木の額縁
    for (a0, a1, b0, b1) in ((-0.36, -0.3, -0.31, 0.31), (0.3, 0.36, -0.31, 0.31), (-0.36, 0.36, 0.25, 0.31), (-0.36, 0.36, -0.31, -0.25)):
        p.append(span("SF_Surround", a0, a1, b0, b1, -0.12, 0.15, M["wood"], 0.006))
    for o in p:
        finish(o, 1.0, angle=40)
    return [join(p, "Study_Safe")]


def photo(M):
    """伏せた写真立て（箱 0.2 x 0.03 x 0.15 の中心が原点。底 y=-0.015）"""
    p = [span("SP_Back", -0.1, 0.1, -0.015, 0.0, -0.075, 0.075, M["wood"], 0.004),
         span("SP_Board", -0.085, 0.085, 0.0, 0.004, -0.06, 0.06, M["plain"], 0.002),
         span("SP_Leg", -0.015, 0.015, 0.004, 0.01, -0.06, 0.03, M["wood"], 0.002)]
    for o in p:
        finish(o, 1.0, angle=40)
    return [join(p, "Study_Photo")]


def _sheet(name, w, d, y, mat, curl=0.004, rot=0.0, uv_flip=False, res=(10, 14)):
    def f(u, v):
        x, z = (u - 0.5) * w, (v - 0.5) * d
        xr = x * math.cos(rot) - z * math.sin(rot)
        zr = x * math.sin(rot) + z * math.cos(rot)
        return (xr, y + curl * (math.sin(math.pi * u) * 0.5 + (v ** 3)), zr)
    s = hq.grid_surface(name, res[0], res[1], f, mat)
    me = s.data
    uvl = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            i, j = vi // (res[1] + 1), vi % (res[1] + 1)
            u, v = i / res[0], j / res[1]
            uvl[li].uv = (u, v) if not uv_flip else (1 - u, v)
    hq.face_toward(s, (0, 1, 0))
    sol = s.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.0008
    return finish(s, keep_uv=True, angle=180)


def document(M):
    """机の上の文書（箱 0.4 x 0.02 x 0.3、底 -0.01）。報告書の抜粋と、その下の紙"""
    return [_sheet("SDoc_Under", 0.21, 0.297, -0.0095, M["plain"], 0.002, rot=0.12),
            _sheet("SDoc_Top", 0.21, 0.297, -0.008, M["paper"], 0.004, rot=-0.05)]


def scrap(M):
    """手帳の切れ端（箱 0.24 x 0.01 x 0.12、底 -0.005）。上端は破れてぎざぎざ"""
    return [_sheet("SScrap", 0.2, 0.1, -0.0045, M["scrap"], 0.003, rot=0.1, res=(24, 6))]


def envelope(M):
    """招聘状の封筒（箱 0.3 x 0.02 x 0.22、底 -0.01）と、少しはみ出した便箋"""
    return [_sheet("SEnv_Letter", 0.2, 0.14, -0.0095, M["plain"], 0.002, rot=-0.25),
            _sheet("SEnv", 0.235, 0.13, -0.008, M["env"], 0.003, rot=0.08)]


def door(M):
    objs = dim_room.door({"woodDark": M["wood"], "wood": M["woodL"], "brass": M["brass"]})
    return [join(objs, "Study_Door")]


def _breaker_mats(M):
    return {"mel": M["wood"], "grille": M["black"], "hazard": M["plain"], "sus": M["brass"], "rubber": M["black"],
            "lever": hq.mat("STD_LeverRed", (0.6, 0.1, 0.08), 0.45)}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.02), "Study_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "Study_Lever")]


PIECES = {"Shell": shell, "Interior": interior, "Desk": desk, "Chair": chair, "SideTable": side_table, "Safe": safe,
          "Photo": photo, "Document": document, "Scrap": scrap, "Envelope": envelope,
          "Door": door, "Breaker": breaker, "Lever": lever}
