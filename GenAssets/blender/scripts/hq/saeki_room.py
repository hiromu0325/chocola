"""
佐伯の自宅（saeki_home：幅7 x 奥行8 x 天井2.6）を品質重視で作る。
研究者の落ち着いた住まい：腰板と壁紙・オークの床・布のペンダント・夕焼けの見える東の窓とカーテン・
几帳面な本棚・二人分のダイニング・ソファ・絨毯・サイドボード。

  Shell      部屋の原点。床・壁（東に窓の開口）・腰板・回り縁・天井・窓（枠・ガラス・外の夕焼け・カーテン）・絵・時計
  Lamps      部屋の原点。布のペンダント2灯（Unityでは影を落とさない：笠の影で天井がまだらになるため）
  Interior   部屋の原点。本棚と本、ソファ、絨毯、サイドボードと小物、フロアランプ
  Dining     ダイニングテーブル（ユニットの原点、天板の上面 y=0.745）と椅子2脚・冷めたコーヒー2杯
  Desk       書き物机（ユニットの原点、天板の上面 y=0.75）と緑のバンカーズランプ
  Chair      机の椅子（原点、机の方 = +Z）
  Letter / Plog / Unsent   資料の見た目（箱の中心が原点）
  Door / Breaker / Lever
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import hq
from hq import U, span, lathe, cyl, cyl_between, pipe, quad, plate_xy, profile_z, frame_ring, rrect_face, finish, join, pillow
import train_room
import dim_room
import study_room

W, D, H = 7.0, 8.0, 2.6
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
WIN_Z, WIN_Y, WIN_W, WIN_H = 1.5, 1.5, 1.4, 1.2          # ビルダーの Window（東の壁）
LIGHT_ZS = (-2.0, 2.0)


def mats():
    M = {}
    M["wall"] = hq.mat("SAE_Wallpaper", (1, 1, 1), 0.85, tex="wallpaper.png")
    M["floor"] = hq.mat("SAE_Floor", (1, 1, 1), 0.5, tex="floor.png")
    M["wood"] = hq.mat("SAE_Wood", (1, 1, 1), 0.45, tex="wood.png")
    M["woodL"] = hq.mat("SAE_WoodLight", (1.3, 1.25, 1.2), 0.45, tex="wood.png")
    M["plaster"] = hq.mat("SAE_Ceiling", (0.95, 0.94, 0.9), 0.9)
    M["paint"] = hq.mat("SAE_TrimPaint", (0.93, 0.91, 0.86), 0.4)
    M["sofa"] = hq.mat("SAE_SofaFabric", (1, 1, 1), 0.9, tex="sofa.png")
    M["linen"] = hq.mat("SAE_Linen", (1, 1, 1), 0.9, tex="linen.png")
    M["curtain"] = hq.mat("SAE_Curtain", (0.95, 0.85, 0.7), 0.9, tex="linen.png")
    M["shade"] = hq.mat("SAE_LampShade", (1.0, 0.92, 0.78), 0.9, tex="linen.png", emit=(1.0, 0.75, 0.45), emit_strength=1.5)
    M["bulb"] = hq.mat("SAE_Bulb", (1, 0.95, 0.85), 0.2, emit=(1.0, 0.8, 0.55), emit_strength=8.0)
    M["brass"] = hq.mat("SAE_Brass", (0.75, 0.58, 0.32), 0.3, 1.0)
    M["black"] = hq.mat("SAE_Black", (0.03, 0.03, 0.03), 0.5)
    M["glass"] = hq.mat("SAE_WindowGlass", (0.6, 0.55, 0.5), 0.05, alpha=0.15)
    M["sky"] = hq.mat("SAE_Sunset", (1, 1, 1), 0.9, tex="sunset.png", emit=(1.0, 0.7, 0.45), emit_strength=2.0)
    M["rug"] = hq.mat("SAE_Rug", (1, 1, 1), 0.95, tex="rug.png")
    M["cup"] = hq.mat("SAE_Porcelain", (0.94, 0.93, 0.9), 0.2)
    M["coffee"] = hq.mat("SAE_Coffee", (0.08, 0.04, 0.02), 0.05)
    M["green"] = hq.mat("SAE_BankerGreen", (0.05, 0.3, 0.16), 0.1)
    M["painting"] = hq.mat("SAE_Painting", (1, 1, 1), 0.7, tex="painting.png")
    M["photo"] = hq.mat("SAE_Photo", (1, 1, 1), 0.5, tex="photo.png")
    M["letter"] = hq.mat("SAE_Letter", (1, 1, 1), 0.9, tex="letter.png")
    M["plog"] = hq.mat("SAE_Plog", (1, 1, 1), 0.9, tex="plog.png")
    M["unsent"] = hq.mat("SAE_Unsent", (1, 1, 1), 0.9, tex="unsent.png")
    M["book"] = hq.mat("SAE_BookSpine", (1, 1, 1), 0.7, tex="../../study/tex/book_atlas.png")
    M["pages"] = hq.mat("SAE_BookPages", (1, 1, 1), 0.9, tex="../../study/tex/book_pages.png")
    M["vase"] = hq.mat("SAE_Vase", (0.3, 0.38, 0.45), 0.15)
    M["dried"] = hq.mat("SAE_DriedFlower", (0.55, 0.42, 0.3), 0.8)
    M["clockface"] = hq.mat("SAE_ClockFace", (0.94, 0.92, 0.86), 0.4)
    M["plate"] = hq.mat("SAE_SwitchPlate", (0.92, 0.9, 0.86), 0.3)
    M["paper"] = hq.mat("SAE_PaperPlain", (0.92, 0.9, 0.84), 0.9)
    M["terracotta"] = hq.mat("SAE_Terracotta", (0.62, 0.36, 0.24), 0.8)
    M["soil"] = hq.mat("SAE_Soil", (0.12, 0.08, 0.05), 0.95)
    M["stem"] = hq.mat("SAE_Stem", (0.3, 0.24, 0.16), 0.8)
    M["leaf"] = hq.mat("SAE_Leaf", (0.12, 0.26, 0.1), 0.45)
    M["felt"] = hq.mat("SAE_Felt", (0.22, 0.2, 0.18), 0.9)
    return M


# ============================== 外殻 ==============================

def shell(M):
    out = []
    out.append(finish(span("HS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["floor"], bev=0), 1.0, rot90=True))
    walls = []
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"HS_WallNS{zs}", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["wall"])
    walls.append(span("HS_WallW", -HW0, -HW, 0.0, H, -HD0, HD0, M["wall"], bev=0))
    win = (WIN_Z - WIN_W / 2, WIN_Z + WIN_W / 2, WIN_Y - WIN_H / 2, WIN_Y + WIN_H / 2)
    walls += train_room.grid_wall("HS_WallE", [win], -HD0, HD0, 0.0, H, lambda a, b, c, d: (HW, HW0, c, d, a, b), M["wall"])
    out += [finish(o, 1 / 0.6, angle=30) for o in walls]
    out.append(finish(span("HS_Ceil", -HW0, HW0, H, H + 0.1, -HD0, HD0, M["plaster"], bev=0), 1.0))
    out += wainscot(M)
    out += window(M)
    out += wall_decor(M)
    return out


def wainscot(M):
    """腰板（高さ 0.85、框と鏡板）・笠木・巾木・回り縁・扉の額縁"""
    p = []
    top = 0.85
    def run(name, wall, s0, s1):
        # 壁ごとに：板・笠木・巾木。鏡板は 0.6m ごと
        if wall in "NS":
            zs = 1 if wall == "N" else -1
            z0, z1 = sorted((zs * HD, zs * (HD - 0.012)))
            p.append(span(f"{name}_Board", s0, s1, 0.0, top, z0, z1, M["wood"], 0))
            zc0, zc1 = sorted((zs * HD, zs * (HD - 0.035)))
            p.append(span(f"{name}_Cap", s0, s1, top, top + 0.035, zc0, zc1, M["wood"], 0.008))
            p.append(span(f"{name}_Base", s0, s1, 0.0, 0.1, *sorted((zs * HD, zs * (HD - 0.022))), M["wood"], 0.005))
            s = s0 + 0.05
            while s + 0.5 <= s1:
                p.append(span(f"{name}_Panel{s:.2f}", s + 0.04, s + 0.54, 0.16, top - 0.08, *sorted((zs * (HD - 0.012), zs * (HD - 0.02))), M["woodL"], 0.008))
                s += 0.6
        else:
            xs = 1 if wall == "E" else -1
            x0, x1 = sorted((xs * HW, xs * (HW - 0.012)))
            p.append(span(f"{name}_Board", x0, x1, 0.0, top, s0, s1, M["wood"], 0))
            p.append(span(f"{name}_Cap", *sorted((xs * HW, xs * (HW - 0.035))), top, top + 0.035, s0, s1, M["wood"], 0.008))
            p.append(span(f"{name}_Base", *sorted((xs * HW, xs * (HW - 0.022))), 0.0, 0.1, s0, s1, M["wood"], 0.005))
            s = s0 + 0.05
            while s + 0.5 <= s1:
                p.append(span(f"{name}_Panel{s:.2f}", *sorted((xs * (HW - 0.012), xs * (HW - 0.02))), 0.16, top - 0.08, s + 0.04, s + 0.54, M["woodL"], 0.008))
                s += 0.6
    for zs, nm in ((1, "N"), (-1, "S")):
        run(f"HW_{nm}a", nm, -HW, -DOOR_HALF - 0.09)
        run(f"HW_{nm}b", nm, DOOR_HALF + 0.09, HW)
    run("HW_E", "E", -HD, HD)
    run("HW_W", "W", -HD, HD)
    # 回り縁（段付き）
    for wl in "NSEW":
        prof = [(0, H - 0.07), (0.008, H - 0.07), (0.016, H - 0.055), (0.026, H - 0.04), (0.032, H - 0.02), (0.036, H), (0, H)]
        s0, s1 = (-HW, HW) if wl in "NS" else (-HD, HD)
        p.append(dim_room.profile_run(f"HW_Crown{wl}", prof, wl, s0, s1, M["paint"], hw=HW0, hd=HD0))
    # 扉の額縁
    for zs in (-1, 1):
        zin = zs * HD
        for xs in (-1, 1):
            p.append(span(f"HW_Cas{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.085))), 0, DOOR_H + 0.085,
                          *sorted((zin, zin - zs * 0.022)), M["wood"], 0.005))
            p.append(span(f"HW_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), M["wood"], 0.002))
        p.append(span(f"HW_CasT{zs}", -DOOR_HALF - 0.095, DOOR_HALF + 0.095, DOOR_H + 0.08, DOOR_H + 0.12, *sorted((zin, zin - zs * 0.03)), M["wood"], 0.006))
        p.append(span(f"HW_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), M["wood"], 0.002))
    return [finish(o, 1.0, rot90=o.name.endswith(("_Board", "_Base", "_Cap")) is False, angle=40) for o in p]


def window(M):
    """東の窓：木の枠・十字の桟・ガラス・窓台・外の夕焼け・両脇に寄せたカーテンとレール"""
    p, glass = [], []
    z0, z1, y0, y1 = WIN_Z - WIN_W / 2, WIN_Z + WIN_W / 2, WIN_Y - WIN_H / 2, WIN_Y + WIN_H / 2
    xi, xo = HW, HW0
    # 開口の内側（見込み）と額縁
    p.append(span("HN_RevealB", xi, xo, y0 - 0.02, y0, z0, z1, M["paint"], 0.002))
    p.append(span("HN_RevealT", xi, xo, y1, y1 + 0.02, z0, z1, M["paint"], 0.002))
    for zz in (z0 - 0.02, z1):
        p.append(span(f"HN_RevealS{zz:.1f}", xi, xo, y0, y1, zz, zz + 0.02, M["paint"], 0.002))
    for (a0, a1, b0, b1) in ((z0 - 0.09, z0, y0 - 0.09, y1 + 0.09), (z1, z1 + 0.09, y0 - 0.09, y1 + 0.09), (z0 - 0.09, z1 + 0.09, y1, y1 + 0.09)):
        p.append(span("HN_Casing", xi - 0.02, xi, b0, b1, a0, a1, M["paint"], 0.004))
    p.append(span("HN_Sill", xi - 0.12, xi, y0 - 0.04, y0, z0 - 0.12, z1 + 0.12, M["paint"], 0.008, 3))
    # サッシ（木）：外枠と十字の桟
    xs = HW + 0.06
    for (a0, a1, b0, b1) in ((z0, z1, y0, y0 + 0.05), (z0, z1, y1 - 0.05, y1), (z0, z0 + 0.05, y0, y1), (z1 - 0.05, z1, y0, y1),
                             (WIN_Z - 0.02, WIN_Z + 0.02, y0, y1), (z0, z1, WIN_Y - 0.02, WIN_Y + 0.02)):
        p.append(span("HN_Sash", xs - 0.02, xs + 0.02, b0, b1, a0, a1, M["paint"], 0.004))
    g = quad("HN_Glass", [(xs, y0, z0), (xs, y0, z1), (xs, y1, z1), (xs, y1, z0)], M["glass"])
    hq.face_toward(g, (-1, 0, 0)); glass.append(g)
    # 外の夕焼け（窓から 4m 外に 16m x 5m の背景板。家並みの屋根が窓の下半分に見える高さ）
    xk = HW0 + 4.0
    sky = quad("HN_Sky", [(xk, -0.8, WIN_Z + 8), (xk, -0.8, WIN_Z - 8), (xk, 4.2, WIN_Z - 8), (xk, 4.2, WIN_Z + 8)], M["sky"])
    hq.face_toward(sky, (-1, 0, 0)); glass.append(sky)
    # カーテンレールと、両脇に寄せたひだのあるカーテン
    rx = HW - 0.1
    p.append(pipe("HN_Rail", [(rx, y1 + 0.18, z0 - 0.45), (rx, y1 + 0.18, z1 + 0.45)], 0.012, M["brass"]))
    for zz in (z0 - 0.47, z1 + 0.47):
        p.append(lathe(f"HN_Finial{zz:.1f}", (rx, y1 + 0.18, zz), [(0.0, -0.02), (0.018, -0.012), (0.02, 0.0), (0.012, 0.015), (0.0, 0.02)], M["brass"], 16))
        p.append(cyl_between(f"HN_Bracket{zz:.1f}", (HW, y1 + 0.18, zz + (0.03 if zz < WIN_Z else -0.03)), (rx, y1 + 0.18, zz + (0.03 if zz < WIN_Z else -0.03)), 0.006, M["brass"], 8))
    for k, zc in enumerate((z0 - 0.25, z1 + 0.25)):
        def f(u, v, zc=zc):
            z = zc + (u - 0.5) * 0.45
            x = rx + 0.03 * math.sin(u * math.pi * 7) - 0.03
            y = (y0 - 0.4) + v * (y1 + 0.16 - (y0 - 0.4))
            return (x, y, z)
        c = hq.grid_surface(f"HN_Curtain{k}", 56, 6, f, M["curtain"])
        me = c.data
        uvl = me.uv_layers.new(name="UVMap").data
        for poly in me.polygons:
            for li in poly.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                uvl[li].uv = (-co.y * 2, co.z * 2)
        sol = c.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.004
        finish(c, keep_uv=True, angle=180)
        glass.append(c)
    out = [finish(o, 1.0, angle=40) for o in p]
    return out + glass


def pendants(M):
    """布のドラム型ペンダント（ビルダーの RoomLight z=±2 の上）"""
    p = []
    for i, z in enumerate(LIGHT_ZS):
        p.append(lathe(f"HP_Canopy{i}", (0, H - 0.03, z), [(0.06, 0), (0.06, 0.03)], M["brass"], 24))
        p.append(cyl(f"HP_Cord{i}", (0, 2.2, z), H - 0.03 - 2.2, 0.004, M["black"], 8))
        sh = lathe(f"HP_Shade{i}", (0, 1.98, z), [(0.24, 0.0), (0.24, 0.22)], M["shade"], 48, cap_top=False, cap_bottom=False)
        sol = sh.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.004
        p.append(sh)
        for k in range(3):
            a = math.pi * 2 * k / 3
            p.append(cyl_between(f"HP_Spider{i}{k}", (0, 2.2, z), (0.24 * math.cos(a), 2.2, z + 0.24 * math.sin(a)), 0.002, M["brass"], 6))
        p.append(lathe(f"HP_Bulb{i}", (0, 2.08, z), [(0.0, 0.0), (0.025, 0.01), (0.032, 0.05), (0.02, 0.09), (0.015, 0.1)], M["bulb"], 24))
    return [finish(o, 3.0, angle=60) for o in p]


def wall_decor(M):
    """西の壁の絵（机の上）・北の壁の時計（ソファの上）・スイッチ"""
    p = []
    zc, yc, w, h = -1.8, 1.65, 0.75, 0.55
    xw = -HW
    for (a0, a1, b0, b1) in ((zc - w / 2 - 0.05, zc - w / 2, yc - h / 2 - 0.05, yc + h / 2 + 0.05), (zc + w / 2, zc + w / 2 + 0.05, yc - h / 2 - 0.05, yc + h / 2 + 0.05),
                             (zc - w / 2, zc + w / 2, yc + h / 2, yc + h / 2 + 0.05), (zc - w / 2, zc + w / 2, yc - h / 2 - 0.05, yc - h / 2)):
        p.append(span("HD_Frame", xw, xw + 0.035, b0, b1, a0, a1, M["brass"], 0.006))
    pt = quad("HD_Painting", [(xw + 0.02, yc - h / 2, zc - w / 2), (xw + 0.02, yc - h / 2, zc + w / 2), (xw + 0.02, yc + h / 2, zc + w / 2), (xw + 0.02, yc + h / 2, zc - w / 2)], M["painting"])
    hq.face_toward(pt, (1, 0, 0))
    # 時計（ビルダーの WallClock：(-1.0, 2.0, hd-0.09)）
    cx, cy, zf = -1.0, 2.0, HD - 0.06
    p.append(cyl_between("HD_ClockRim", (cx, cy, HD), (cx, cy, zf), 0.19, M["wood"], 48))
    p.append(cyl_between("HD_ClockFace", (cx, cy, zf), (cx, cy, zf - 0.002), 0.165, M["clockface"], 48))
    for k in range(12):
        a = math.pi * 2 * k / 12
        x, y = cx + math.sin(a) * 0.14, cy + math.cos(a) * 0.14
        p.append(span(f"HD_Tick{k}", x - 0.005, x + 0.005, y - 0.014, y + 0.014, zf - 0.004, zf - 0.002, M["black"], 0))
    p.append(cyl_between("HD_HandH", (cx, cy, zf - 0.005), (cx + 0.07, cy - 0.03, zf - 0.005), 0.004, M["black"], 6))
    p.append(cyl_between("HD_HandM", (cx, cy, zf - 0.007), (cx - 0.02, cy + 0.12, zf - 0.007), 0.003, M["black"], 6))
    zS = -HD
    p.append(span("HD_Switch", 0.72, 0.84, 1.2, 1.32, zS, zS + 0.01, M["plate"], 0.004))
    p.append(span("HD_SwitchKey", 0.755, 0.805, 1.23, 1.29, zS + 0.01, zS + 0.016, M["plate"], 0.003))
    return [finish(o, 1.0, angle=40) for o in p] + [pt]


# ============================== 家具 ==============================

def orderly_books(name, side, za, zb, y0, y1, xf, rnd):
    """几帳面な本棚：高さ順、色の近い本が並び、隙間がない"""
    items = []
    z = za + 0.01
    cell = rnd.randrange(16)
    while z < zb - 0.04:
        w = rnd.uniform(0.028, 0.045)
        hh = min(y1 - y0 - 0.02, 0.22 + 0.06 * ((z - za) / max(0.1, zb - za)))
        if rnd.random() < 0.12:
            cell = rnd.randrange(16)
        items.append((name, side, xf, "up", z, z + w, y0, y0 + hh, 0.2, cell, 0.0))
        z += w + 0.001
    return items


def interior(M, seed=901):
    rnd = random.Random(seed)
    out = []
    # 本棚（ビルダーの Bookshelf：(-hw+0.3, *, 1.6)、0.45 x 2.1 x 2.4）
    side, z0, z1 = -1, 0.4, 2.8
    xb, xf = -HW, -HW + 0.45
    parts = [span("HB_Back", xb, xb + 0.012, 0.1, 2.1, z0, z1, M["wood"], 0),
             span("HB_Plinth", xb, xf, 0.0, 0.1, z0, z1, M["wood"], 0.006),
             span("HB_Cap", xb, xf + 0.02, 2.1, 2.14, z0 - 0.02, z1 + 0.02, M["wood"], 0.01, 3)]
    for b in range(4):
        z = z0 + b * (z1 - z0) / 3
        parts.append(span(f"HB_Div{b}", xb, xf, 0.1, 2.1, max(z0, z - 0.012), min(z1, z + 0.012), M["wood"], 0.004))
    shelves = [0.1, 0.5, 0.9, 1.3, 1.7]
    for s in shelves[1:]:
        parts.append(span(f"HB_Shelf{s}", xb, xf, s - 0.022, s, z0, z1, M["wood"], 0.004))
    out += [finish(o, 1.0, angle=40) for o in parts]
    books = []
    for b in range(3):
        za, zb = z0 + b * (z1 - z0) / 3 + 0.012, z0 + (b + 1) * (z1 - z0) / 3 - 0.012
        for s in shelves:
            books += orderly_books(f"HB_B{b}{s}", side, za, zb, s, s + 0.38, xf, rnd)
    bk = study_room.build_books("HB_Books", books, {"book": M["book"], "pages": M["pages"]})
    out.append(bk)
    out += sofa(M)
    # 絨毯（ビルダーの Rug：(1.4, *, 1.2)、2.4 x 2.0）
    rug = quad("HR_Rug", [(0.2, 0.012, 0.2), (2.6, 0.012, 0.2), (2.6, 0.012, 2.2), (0.2, 0.012, 2.2)], M["rug"])
    hq.face_toward(rug, (0, 1, 0))
    sol = rug.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.01
    out.append(finish(rug, keep_uv=True, angle=40))
    out += sideboard(M)
    out += floor_lamp(M)
    out += potted_plant(M)
    out += coat_stand(M)
    return out


PLANT_POS = (HW0 - 0.45, HD0 - 0.45)          # 北東の隅
COAT_POS = (-1.6, -HD0 + 0.4)                 # 南の扉の西


def potted_plant(M):
    """北東の隅の観葉植物（研究所の植物と同じ作り方、素焼きの鉢）"""
    import lab_room
    objs = lab_room.plant({"pot": M["terracotta"], "soil": M["soil"], "stem": M["stem"], "leaf": M["leaf"]}, seed=907)
    src = (-lab_room.HW + 0.45, lab_room.HD - 0.45)
    mv = Matrix.Translation(U(PLANT_POS[0] - src[0], 0, PLANT_POS[1] - src[1]) - U(0, 0, 0))
    for o in objs:
        o.data.transform(mv)
    return objs


def coat_stand(M):
    """南の扉の脇の曲げ木のコート掛け：帽子と、たたんだ傘"""
    x, z = COAT_POS
    p = [lathe("HC_Base", (x, 0, z), [(0.0, 0.0), (0.2, 0.0), (0.2, 0.015), (0.06, 0.04), (0.03, 0.06)], M["wood"], 32),
         lathe("HC_Pole", (x, 0.05, z), [(0.024, 0.0), (0.018, 1.6), (0.026, 1.66), (0.0, 1.72)], M["wood"], 24)]
    for k in range(6):
        a = math.pi * 2 * k / 6
        hy = 1.55 if k % 2 == 0 else 1.4
        c, s_ = math.cos(a), math.sin(a)
        p.append(pipe(f"HC_Hook{k}", [(x + c * 0.015, hy - 0.1, z + s_ * 0.015), (x + c * 0.1, hy, z + s_ * 0.1), (x + c * 0.14, hy + 0.03, z + s_ * 0.14),
                                     (x + c * 0.15, hy + 0.07, z + s_ * 0.15)], 0.009, M["wood"], 0.04))
    # 中折れ帽（北東のフック）
    a = 0.0
    hx, hz = x + 0.13, z
    p.append(lathe("HC_Hat", (hx, 1.52, hz), [(0.0, 0.0), (0.16, 0.0), (0.165, 0.01), (0.1, 0.012), (0.095, 0.1), (0.07, 0.13), (0.0, 0.12)], M["felt"], 40))
    p.append(lathe("HC_HatBand", (hx, 1.525, hz), [(0.101, 0.0), (0.101, 0.03)], M["black"], 40, cap_top=False, cap_bottom=False))
    # たたんだ傘（床に立てかける）
    ub = (x + 0.12, 0.0, z + 0.14)
    ut = (x + 0.05, 0.9, z + 0.05)
    p.append(cyl_between("HC_Umbrella", ub, (ub[0] + (ut[0] - ub[0]) * 0.8, 0.72, ub[2] + (ut[2] - ub[2]) * 0.8), 0.035, M["black"], 16, r1=0.012))
    p.append(cyl_between("HC_UmbShaft", (ub[0] + (ut[0] - ub[0]) * 0.8, 0.72, ub[2] + (ut[2] - ub[2]) * 0.8), ut, 0.006, M["brass"], 8))
    p.append(pipe("HC_UmbHandle", [ut, (ut[0] - 0.02, 0.95, ut[2] - 0.02), (ut[0] - 0.06, 0.94, ut[2] - 0.06)], 0.01, M["wood"], 0.02))
    return [finish(o, 2.0, angle=50) for o in p]


def sofa(M):
    """ソファ（ビルダーの Sofa：(-2.0, *, hd-0.55)、座 1.8 x 0.44 x 0.8、背は +Z）"""
    cx, cz = -2.0, HD0 - 0.55
    p = []
    p.append(span("HF_Base", cx - 0.9, cx + 0.9, 0.12, 0.4, cz - 0.4, cz + 0.4, M["sofa"], 0.04, 4))
    p.append(span("HF_Back", cx - 0.9, cx + 0.9, 0.35, 0.86, cz + 0.2, cz + 0.4, M["sofa"], 0.05, 4))
    for sx in (-1, 1):
        p.append(span(f"HF_Arm{sx}", *sorted((cx + sx * 0.9, cx + sx * 0.76)), 0.12, 0.64, cz - 0.4, cz + 0.4, M["sofa"], 0.05, 4))
    for k, x in enumerate((cx - 0.38, cx + 0.38)):
        p.append(pillow(f"HF_Seat{k}", (x, 0.46, cz - 0.05), (0.75, 0.14, 0.62), M["sofa"], seed=11 + k))
        p.append(pillow(f"HF_BackCush{k}", (x, 0.66, cz + 0.12), (0.74, 0.12, 0.4), M["sofa"], seed=21 + k))
        cush = p[-1]
        cush.data.transform(Matrix.Translation(U(x, 0.66, cz + 0.12)) @ Matrix.Rotation(math.radians(-80), 4, "X") @ Matrix.Translation(-U(x, 0.66, cz + 0.12)))
    p.append(pillow("HF_Throw", (cx + 0.55, 0.62, cz - 0.02), (0.38, 0.12, 0.38), M["linen"], seed=31))
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(taper(f"HF_Foot{sx}{sz}", cx + sx * 0.84, cz + sz * 0.34, 0.0, 0.12, 0.05, 0.035, M["wood"]))
    return [finish(o, 2.0, angle=50) for o in p]


def taper(name, x, z, y0, y1, wt, wb, m):
    return dim_room.taper_leg(name, x, z, y0, y1, wt, wb, m)


def sideboard(M):
    """南の壁の東側：サイドボード（花瓶のドライフラワーと写真立て）"""
    p = []
    x0, x1, zb = 1.3, 3.0, -HD
    zf = zb + 0.45
    p.append(span("HS2_Top", x0 - 0.02, x1 + 0.02, 0.76, 0.79, zb, zf + 0.02, M["wood"], 0.008, 3))
    p.append(span("HS2_Body", x0, x1, 0.14, 0.76, zb, zf, M["wood"], 0.004))
    for k in range(3):
        a, b = x0 + 0.02 + k * (x1 - x0 - 0.04) / 3, x0 + 0.02 + (k + 1) * (x1 - x0 - 0.04) / 3
        p.append(span(f"HS2_Door{k}", a + 0.004, b - 0.004, 0.17, 0.73, zf, zf + 0.018, M["woodL"], 0.006))
        p += dim_room.knob(f"HS2_Knob{k}", ((a + b) / 2, 0.62, zf + 0.018), M["brass"], face="+z")
    for sx in (x0 + 0.06, x1 - 0.06):
        for sz in (zb + 0.06, zf - 0.06):
            p.append(taper(f"HS2_Leg{sx:.1f}{sz:.1f}", sx, sz, 0.0, 0.14, 0.04, 0.03, M["wood"]))
    # 花瓶とドライフラワー
    vx, vz = x0 + 0.35, zb + 0.22
    p.append(lathe("HS2_Vase", (vx, 0.79, vz), [(0.0, 0.0), (0.06, 0.0), (0.09, 0.08), (0.07, 0.18), (0.035, 0.24), (0.04, 0.27), (0.034, 0.27)], M["vase"], 32, cap_top=False))
    rnd = random.Random(911)
    for k in range(9):
        a = rnd.uniform(0, math.pi * 2); r = rnd.uniform(0.05, 0.16); ht = rnd.uniform(0.25, 0.45)
        tip = (vx + math.cos(a) * r, 0.79 + 0.27 + ht, vz + math.sin(a) * r)
        p.append(cyl_between(f"HS2_Stem{k}", (vx, 1.02, vz), tip, 0.003, M["dried"], 6))
        p.append(lathe(f"HS2_Head{k}", (tip[0], tip[1] - 0.01, tip[2]), [(0.0, 0.0), (0.018, 0.01), (0.012, 0.03), (0.0, 0.034)], M["dried"], 10))
    # 写真立て（二人の写真）
    fx, fz = x1 - 0.45, zb + 0.2
    fr = [span("HS2_PFrame", fx - 0.13, fx + 0.13, 0.79, 0.99, fz - 0.01, fz + 0.01, M["woodL"], 0.006)]
    ph = quad("HS2_Photo", [(fx - 0.11, 0.81, fz + 0.012), (fx + 0.11, 0.81, fz + 0.012), (fx + 0.11, 0.97, fz + 0.012), (fx - 0.11, 0.97, fz + 0.012)], M["photo"])
    hq.face_toward(ph, (0, 0, 1))
    fr.append(span("HS2_PLeg", fx - 0.02, fx + 0.02, 0.79, 0.93, fz - 0.08, fz - 0.01, M["woodL"], 0.003))
    out = [finish(o, 1.0, angle=40) for o in p + fr]
    return out + [ph]


def floor_lamp(M):
    """ソファの脇のフロアランプ"""
    x, z = -0.9, HD0 - 0.4
    p = [lathe("HL_Base", (x, 0, z), [(0.16, 0), (0.16, 0.02), (0.1, 0.04), (0.02, 0.05)], M["brass"], 32),
         cyl("HL_Pole", (x, 0.05, z), 1.35, 0.012, M["brass"], 16)]
    sh = lathe("HL_Shade", (x, 1.28, z), [(0.2, 0.0), (0.14, 0.26)], M["shade"], 48, cap_top=False, cap_bottom=False)
    sol = sh.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.004
    p.append(sh)
    return [finish(o, 3.0, angle=60) for o in p]


def dining(M):
    """ダイニング（ビルダーの DiningTable：天板 1.3 x 0.9、上面 0.745、中央の一本脚）と椅子2脚・冷めたコーヒー"""
    p = []
    p.append(span("HT_Top", -0.65, 0.65, 0.695, 0.745, -0.45, 0.45, M["wood"], 0.012, 4))
    p.append(span("HT_Apron", -0.55, 0.55, 0.62, 0.695, -0.35, 0.35, M["wood"], 0.004))
    p.append(lathe("HT_Post", (0, 0.12, 0), [(0.06, 0), (0.07, 0.05), (0.045, 0.2), (0.05, 0.4), (0.04, 0.5)], M["wood"], 32))
    for k in range(4):
        a = math.pi / 4 + math.pi / 2 * k
        p.append(pipe(f"HT_Foot{k}", [(0, 0.14, 0), (math.cos(a) * 0.2, 0.08, math.sin(a) * 0.2), (math.cos(a) * 0.4, 0.02, math.sin(a) * 0.4)], 0.025, M["wood"], 0.12))
    # テーブルランナーと、二人分のカップ（ソーサー・冷めたコーヒー）と砂糖壺
    run = quad("HT_Runner", [(-0.18, 0.746, -0.5), (0.18, 0.746, -0.5), (0.18, 0.746, 0.5), (-0.18, 0.746, 0.5)], M["linen"])
    hq.face_toward(run, (0, 1, 0))
    for z in (-0.25, 0.25):
        p.append(lathe(f"HT_Saucer{z}", (0, 0.748, z), [(0.0, 0.0), (0.07, 0.0), (0.075, 0.012), (0.06, 0.008), (0.0, 0.006)], M["cup"], 32))
        p.append(lathe(f"HT_Cup{z}", (0, 0.756, z), [(0.0, 0.0), (0.028, 0.0), (0.04, 0.04), (0.042, 0.07), (0.038, 0.07), (0.035, 0.012), (0.0, 0.012)], M["cup"], 32))
        p.append(cyl(f"HT_Coffee{z}", (0, 0.81, z), 0.001, 0.036, M["coffee"], 24))
        hx = 0.045 if z < 0 else -0.045
        p.append(pipe(f"HT_Handle{z}", [(hx * 0.9, 0.81, z), (hx * 1.35, 0.8, z), (hx * 1.3, 0.77, z), (hx * 0.9, 0.77, z)], 0.005, M["cup"], 0.01))
    p.append(lathe("HT_Sugar", (0.2, 0.745, 0.0), [(0.0, 0.0), (0.04, 0.0), (0.045, 0.06), (0.03, 0.07), (0.035, 0.075), (0.0, 0.085)], M["cup"], 24))
    out = [finish(o, 1.0, angle=45) for o in p] + [run]
    # 椅子2脚（ビルダー：(0,0,-0.75) yaw0 と (0,0,0.75) yaw180）
    for zz, yaw in ((-0.75, 0), (0.75, 180)):
        ch = dim_room.chair({"wood": M["woodL"]})[0]
        cu = pillow(f"HT_Cushion{zz}", (0.0, 0.475, 0.0), (0.38, 0.05, 0.36), M["linen"], seed=41)
        finish(cu, 3.0, angle=60)
        c = join([ch, cu], f"HT_Chair{zz}")
        c.data.transform(Matrix.Translation(U(0, 0, zz)) @ Matrix.Rotation(math.radians(-yaw), 4, "Z"))
        out.append(c)
    return [join(out, "SaekiHome_Dining")]


def desk(M):
    """書き物机（ビルダーの Desk：天板 1.4 x 0.7、上面 0.75）と緑のバンカーズランプ"""
    p = []
    p.append(span("HK_Top", -0.7, 0.7, 0.715, 0.75, -0.35, 0.35, M["wood"], 0.01, 4))
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(taper(f"HK_Leg{sx}{sz}", sx * 0.63, sz * 0.28, 0.0, 0.715, 0.055, 0.035, M["wood"]))
    p.append(span("HK_Apron", -0.6, 0.6, 0.62, 0.715, -0.29, 0.29, M["wood"], 0.004))
    p += dim_room.drawer_front("HK_Drawer", -0.3, 0.3, 0.63, 0.705, -0.29, M["woodL"], M["brass"])
    # バンカーズランプ（ビルダーの DeskLamp：(0.5, 0.95, 0.2)）
    lx, lz = 0.5, 0.2
    p.append(lathe("HK_LBase", (lx, 0.75, lz), [(0.07, 0), (0.07, 0.015), (0.05, 0.025), (0.0, 0.03)], M["brass"], 32))
    p.append(cyl("HK_LPost", (lx, 0.78, lz), 0.25, 0.008, M["brass"], 12))
    # 緑のガラスの笠（半円筒、長手は机の幅方向 = X、下は開いている）
    arc = [(0.075 * math.cos(math.pi * k / 16), 1.04 + 0.05 * math.sin(math.pi * k / 16)) for k in range(17)]
    sh = profile_z("HK_LShade", [(lz + a, b) for a, b in arc], lx - 0.14, lx + 0.14, M["green"], closed=False)
    # profile_z は (x, y) 断面を z に押し出すので、x と z を入れ替える（Unity の x<->z の鏡映 = Blender の -Y<->-X）
    for v in sh.data.vertices:
        ux, uy, uz = -v.co.x, v.co.z, -v.co.y
        v.co = U(uz, uy, ux)
    sol = sh.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.003
    hq.apply_mods(sh)
    _outward(sh)
    p.append(sh)
    for zz in (lz - 0.075, lz + 0.075):
        p.append(cyl_between(f"HK_LRim{zz}", (lx - 0.142, 1.04, zz), (lx + 0.142, 1.04, zz), 0.004, M["brass"], 8))
    p.append(cyl_between("HK_LArm", (lx, 1.03, lz), (lx, 1.07, lz), 0.006, M["brass"], 8))
    p.append(cyl_between("HK_LTube", (lx - 0.11, 1.05, lz), (lx + 0.11, 1.05, lz), 0.012, M["bulb"], 12))
    p.append(cyl_between("HK_LChain", (lx + 0.05, 1.04, lz - 0.02), (lx + 0.05, 0.96, lz - 0.03), 0.002, M["brass"], 6))
    p.append(cyl_between("HK_Pen", (0.0, 0.754, -0.25), (0.17, 0.754, -0.3), 0.004, M["black"], 8))
    for o in p:
        finish(o, 1.0, angle=40)
    return [join(p, "SaekiHome_Desk")]


def chair(M):
    return [join(dim_room.chair({"wood": M["woodL"]}), "SaekiHome_Chair")]


def _outward(o):
    """頂点を U() で置き直すと鏡映になって面が裏返るので、外向きに揃え直す"""
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()


def _sheet(name, w, d, y, mat, rot=0.0):
    q = quad(name, [(-w / 2, y, -d / 2), (w / 2, y, -d / 2), (w / 2, y, d / 2), (-w / 2, y, d / 2)], mat)
    hq.face_toward(q, (0, 1, 0))
    q.data.transform(Matrix.Rotation(-rot, 4, "Z"))
    sol = q.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.0008
    return finish(q, keep_uv=True, angle=60)


def letter(M):
    return [_sheet("HLetter", 0.148, 0.21, -0.004, M["letter"], 0.15)]


def plog(M):
    """開いたノート（黒い表紙、左頁は白紙、右頁が最終頁）"""
    rot = Matrix.Rotation(0.08, 4, "Z")
    cover = finish(span("HPlogCover", -0.16, 0.16, -0.01, -0.005, -0.112, 0.112, M["black"], 0.002), 1.0)
    left = _sheet("HPlogL", 0.152, 0.216, -0.004, M["paper"])
    left.data.transform(Matrix.Translation(U(-0.079, 0, 0)))
    right = _sheet("HPlogR", 0.152, 0.216, -0.004, M["plog"])
    right.data.transform(Matrix.Translation(U(0.079, 0, 0)))
    for o in (cover, left, right):
        o.data.transform(rot)
    return [cover, left, right]


def unsent(M):
    return [_sheet("HUnsent", 0.14, 0.098, -0.004, M["unsent"], 0.25)]


def door(M):
    return [join(dim_room.door({"woodDark": M["wood"], "wood": M["woodL"], "brass": M["brass"]}), "SaekiHome_Door")]


def _breaker_mats(M):
    return {"mel": M["paint"], "grille": M["black"], "hazard": M["plate"], "sus": M["plate"], "rubber": M["black"],
            "lever": hq.mat("SAE_LeverRed", (0.75, 0.12, 0.1), 0.45)}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.02), "SaekiHome_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "SaekiHome_Lever")]


PIECES = {"Shell": shell, "Lamps": pendants, "Interior": interior, "Dining": dining, "Desk": desk, "Chair": chair,
          "Letter": letter, "Plog": plog, "Unsent": unsent, "Door": door, "Breaker": breaker, "Lever": lever}
