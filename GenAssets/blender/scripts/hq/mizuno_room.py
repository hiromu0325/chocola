"""
水野のアパート（mizuno_apart：幅5 x 奥行9 x 天井2.5）を品質重視で作る。
手前（z < 1.0）は生活感のある暖かいワンルーム、襖の向こう（z > 1.0）は記憶が滲んだ病院の個室。

  手前：木目のフローリング・白いクロス・丸いシーリングライト・東の窓（レースと厚手のカーテン、夜の街）・
        ベッド（小花柄の布団・うさぎのぬいぐるみ）・観葉植物・チェスト（写真立て・鏡・小物）・ラグ・
        コルクボード・カレンダー・ローテーブル（マグ・リモコン）・座布団・台所カウンター（流し・ケトル・
        カセットコンロ・水切り）・小さな冷蔵庫・扉の脇の白衣・詰めかけのボストンバッグ
  境界：襖（引手・縁）と敷居・鴨居。病室側の面は病院の白い壁
  奥  ：長尺ビニルの床・ミントの腰壁・吸音板の天井とLED・電動ベッド・点滴スタンド・床頭台（花）・
        モニター・カーテン・ブラインドの窓（昼の中庭）

  Shell      部屋の原点。床・壁・天井・照明器具・窓・外の景色・襖・巾木
  Interior   部屋の原点。手前の家具と小物、奥の病室の家具
  Laptop / Diary / Recorder   資料と装置の見た目（ビルダーの箱の中心が原点）
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
import ward_room

W, D, H = 5.0, 9.0, 2.5
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
SPLIT = 1.0
WARM_WIN = (-1.7, -0.7, 0.95, 2.05)       # 東の窓（z0, z1, y0, y1）
HOSP_WIN = (2.2, 3.4, 1.1, 2.1)
HBED = (1.1, HD0 - 1.7)                    # 病室のベッド（頭 = 北）。出口の扉への通り道を空けるため東寄り
HCAB = (-1.4, HD0 - 0.9)                   # 床頭台（西の壁際。以前は扉の前でベッドとの隙間が 0.66m しかなく通れなかった）


def mats():
    M = {}
    M["floor"] = hq.mat("MZA_Flooring", (1, 1, 1), 0.45, tex="flooring.png")
    M["wall"] = hq.mat("MZA_Wallpaper", (1, 1, 1), 0.85, tex="wallpaper.png")
    M["ceil"] = hq.mat("MZA_Ceiling", (0.95, 0.95, 0.93), 0.9)
    M["trim"] = hq.mat("MZA_TrimWhite", (0.92, 0.91, 0.88), 0.4)
    M["wood"] = hq.mat("MZA_WoodLight", (1.05, 1.0, 0.95), 0.45, tex="../../lab/tex/oak.png")
    M["woodD"] = hq.mat("MZA_WoodMid", (0.7, 0.6, 0.5), 0.45, tex="../../lab/tex/oak.png")
    M["duvet"] = hq.mat("MZA_Duvet", (1, 1, 1), 0.9, tex="duvet.png")
    M["pillow"] = hq.mat("MZA_PillowCase", (0.95, 0.93, 0.9), 0.9)
    M["rabbit"] = hq.mat("MZA_Plush", (0.92, 0.88, 0.84), 0.95)
    M["rug"] = hq.mat("MZA_Rug", (1, 1, 1), 0.95, tex="rug.png")
    M["drape"] = hq.mat("MZA_Drape", (1, 1, 1), 0.9, tex="drape.png")
    M["lace"] = hq.mat("MZA_Lace", (1, 1, 1), 0.9, tex="lace.png", tex_alpha=True)
    M["cushion"] = hq.mat("MZA_Cushion", (1, 1, 1), 0.9, tex="cushion.png")
    M["fusuma"] = hq.mat("MZA_FusumaPaper", (1, 1, 1), 0.8, tex="fusuma_paper.png")
    M["lacq"] = hq.mat("MZA_Lacquer", (0.08, 0.05, 0.04), 0.2)
    M["brass"] = hq.mat("MZA_Brass", (0.75, 0.58, 0.32), 0.3, 1.0)
    M["night"] = hq.mat("MZA_NightCity", (1, 1, 1), 0.9, tex="night_city.png", emit=(1, 1, 1), emit_strength=1.0)
    M["glass"] = hq.mat("MZA_WindowGlass", (0.6, 0.62, 0.66), 0.05, alpha=0.12)
    M["alu"] = hq.mat("MZA_Aluminum", (0.78, 0.79, 0.8), 0.3, 1.0)
    M["light"] = hq.mat("MZA_CeilingLight", (1.0, 0.96, 0.9), 0.3, emit=(1.0, 0.9, 0.75), emit_strength=4.0)
    M["cork"] = hq.mat("MZA_CorkBoard", (1, 1, 1), 0.9, tex="cork_board.png")
    M["cal"] = hq.mat("MZA_Calendar", (1, 1, 1), 0.8, tex="calendar.png")
    M["photo"] = hq.mat("MZA_Photo", (1, 1, 1), 0.5, tex="photo.png")
    M["diary"] = hq.mat("MZA_Diary", (1, 1, 1), 0.9, tex="diary.png")
    M["cover"] = hq.mat("MZA_DiaryCover", (0.55, 0.22, 0.24), 0.6)
    M["screen"] = hq.mat("MZA_LaptopScreen", (1, 1, 1), 0.2, tex="laptop_screen.png", emit=(0.5, 0.6, 0.9), emit_strength=0.8)
    M["silver"] = hq.mat("MZA_Silver", (0.72, 0.73, 0.75), 0.35, 0.8)
    M["black"] = hq.mat("MZA_Black", (0.03, 0.03, 0.035), 0.5)
    M["white"] = hq.mat("MZA_WhitePlastic", (0.93, 0.93, 0.91), 0.35)
    M["steel"] = hq.mat("MZA_Stainless", (0.8, 0.81, 0.83), 0.2, 1.0)
    M["mug"] = hq.mat("MZA_Mug", (0.86, 0.72, 0.62), 0.25)
    M["kettle"] = hq.mat("MZA_Kettle", (0.72, 0.2, 0.18), 0.3)
    M["coat"] = hq.mat("MZA_LabCoat", (0.94, 0.94, 0.93), 0.9)
    M["bag"] = hq.mat("MZA_BagCanvas", (0.25, 0.3, 0.38), 0.8)
    M["leather"] = hq.mat("MZA_Leather", (0.35, 0.22, 0.14), 0.45)
    M["clothes"] = hq.mat("MZA_Clothes", (0.8, 0.72, 0.6), 0.9)
    M["mirror"] = hq.mat("MZA_Mirror", (0.9, 0.9, 0.92), 0.02, 1.0)
    M["bottle"] = hq.mat("MZA_Bottle", (0.7, 0.85, 0.9), 0.1)
    M["terracotta"] = hq.mat("MZA_Pot", (0.9, 0.9, 0.88), 0.4)
    M["soil"] = hq.mat("MZA_Soil", (0.12, 0.08, 0.05), 0.95)
    M["stem"] = hq.mat("MZA_Stem", (0.3, 0.24, 0.16), 0.8)
    M["leaf"] = hq.mat("MZA_Leaf", (0.14, 0.32, 0.12), 0.45)
    M["flower"] = hq.mat("MZA_Flower", (0.95, 0.9, 0.5), 0.6)
    # 奥の病室（臨床病棟の材質を流用）
    M["hfloor"] = hq.mat("MZA_HospVinyl", (1, 1, 1), 0.5, tex="../../ward/tex/vinyl_floor.png")
    M["hpaint"] = hq.mat("MZA_HospPaint", (1, 1, 1), 0.8, tex="../../ward/tex/paint.png")
    M["hwain"] = hq.mat("MZA_HospWainscot", (1, 1, 1), 0.7, tex="../../ward/tex/wainscot.png")
    M["htile"] = hq.mat("MZA_HospCeiling", (1, 1, 1), 0.9, tex="../../lab/tex/ceiling_tile.png")
    M["hled"] = hq.mat("MZA_HospLed", (0.95, 0.97, 1.0), 0.3, emit=(0.9, 0.95, 1.0), emit_strength=5.0)
    M["sky"] = hq.mat("MZA_HospSky", (1, 1, 1), 0.9, tex="../../ward/tex/sky_day.png", emit=(1, 1, 1), emit_strength=1.0)
    M["curtain"] = hq.mat("MZA_HospCurtain", (1, 1, 1), 0.9, tex="../../ward/tex/curtain.png")
    M["cove"] = hq.mat("MZA_HospCove", (0.42, 0.44, 0.42), 0.6)
    M["blind"] = hq.mat("MZA_Blind", (0.88, 0.88, 0.86), 0.4, 0.3)
    H_ = {}
    H_["grey"] = hq.mat("MZA_HospGrey", (0.55, 0.57, 0.58), 0.45)
    H_["ivory"] = hq.mat("MZA_HospIvory", (0.9, 0.89, 0.84), 0.4)
    H_["chrome"] = hq.mat("MZA_Chrome", (0.9, 0.9, 0.9), 0.1, 1.0)
    H_["rubber"] = hq.mat("MZA_Rubber", (0.05, 0.05, 0.05), 0.8)
    H_["laminate"] = hq.mat("MZA_HospLaminate", (1.15, 1.1, 1.05), 0.45, tex="../../lab/tex/oak.png")
    H_["mattress"] = hq.mat("MZA_HospMattress", (0.55, 0.66, 0.74), 0.5)
    H_["sheet"] = hq.mat("MZA_HospSheet", (1, 1, 1), 0.9, tex="../../ward/tex/sheet.png")
    H_["blanket"] = hq.mat("MZA_HospBlanket", (1, 1, 1), 0.9, tex="../../ward/tex/blanket.png")
    H_["ivbag"] = hq.mat("MZA_IvBag", (0.85, 0.9, 0.92), 0.05, alpha=0.5)
    H_["black"] = M["black"]
    H_["tvscreen"] = hq.mat("MZA_TvScreen", (0.02, 0.025, 0.03), 0.05)
    H_["tissue"] = hq.mat("MZA_Tissue", (0.95, 0.95, 0.93), 0.9)
    H_["towel"] = hq.mat("MZA_Towel", (0.9, 0.92, 0.94), 0.95)
    H_["vitals"] = hq.mat("MZA_ScreenVitals", (1, 1, 1), 0.2, tex="../../ward/tex/screen_vitals.png", emit=(0.4, 1.0, 0.6), emit_strength=0.8)
    M["H"] = H_
    M["lever"] = hq.mat("MZA_LeverRed", (0.75, 0.12, 0.1), 0.45)
    M["hazard"] = hq.mat("MZA_Hazard", (1, 1, 1), 0.5, tex="../../lab/tex/hazard.png")
    return M


def _outward(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()


def _cloth(name, fn, nu, nv, mat, uvs=2.0, thick=0.004, uvmode="top"):
    """布の面（実寸UV、厚み付き）。uvmode: top=上から見た位置、zy=東西の壁に垂れる布、xy=南北の壁に垂れる布"""
    o = hq.grid_surface(name, nu, nv, fn, mat)
    me = o.data
    uvl = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if uvmode == "zy":
                uvl[li].uv = (-co.y * uvs, co.z * uvs)
            elif uvmode == "xy":
                uvl[li].uv = (-co.x * uvs, co.z * uvs)
            else:
                uvl[li].uv = (co.x * uvs, co.y * uvs + co.z * uvs * 0.5)
    s = o.modifiers.new("Solid", "SOLIDIFY"); s.thickness = thick
    finish(o, keep_uv=True, angle=60)
    _outward(o)
    return o


# ============================== 外殻 ==============================

def shell(M):
    out = []
    # 床：手前はフローリング、奥（襖の向こう）は病院のビニル床
    out.append(finish(span("MS_FloorWarm", -HW0, HW0, -0.12, 0.0, -HD0, SPLIT, M["floor"], bev=0), 1.0, rot90=True))
    out.append(finish(span("MS_FloorHosp", -HW0, HW0, -0.12, 0.0, SPLIT, HD0, M["hfloor"], bev=0), 0.5, rot90=True))
    walls = []
    walls += train_room.grid_wall("MS_WallS", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                  lambda a, b, c, d: (a, b, c, d, -HD0, -HD), M["wall"])
    walls += train_room.grid_wall("MS_WallN", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                  lambda a, b, c, d: (a, b, c, d, HD, HD0), M["hpaint"])
    for xs in (-1, 1):
        for (z0, z1, m) in ((-HD0, SPLIT, M["wall"]), (SPLIT, HD0, M["hpaint"])):
            ops = []
            if xs > 0:
                w = WARM_WIN if z1 <= SPLIT else HOSP_WIN
                ops = [w]
            walls += train_room.grid_wall(f"MS_WallEW{xs}{z0}", ops, z0, z1, 0.0, H,
                                          lambda a, b, c, d, xs=xs: (*sorted((xs * HW, xs * HW0)), c, d, a, b), m)
    out += [finish(o, 1.0, angle=30) for o in walls]
    # 天井：手前は白い天井、奥は吸音板
    out.append(finish(span("MS_CeilWarm", -HW0, HW0, H, H + 0.1, -HD0, SPLIT, M["ceil"], bev=0), 1.0))
    out.append(finish(span("MS_CeilHosp", -HW0, HW0, H, H + 0.1, SPLIT, HD0, M["htile"], bev=0), 1 / 0.6))
    out += trims(M)
    out += fixtures(M)
    out += warm_window(M)
    out += hosp_window(M)
    out += fusuma(M)
    return out


def trims(M):
    """手前：白い巾木と回り縁。奥：ミントの腰壁とソフト巾木"""
    p = []
    for (x0, x1) in ((-HW, -DOOR_HALF - 0.07), (DOOR_HALF + 0.07, HW)):
        p.append(span(f"MT_BaseS{x0:.0f}", x0, x1, 0, 0.06, -HD, -HD + 0.012, M["trim"], 0.003))
        p.append(span(f"MT_CoveN{x0:.0f}", x0, x1, 0, 0.08, HD - 0.012, HD, M["cove"], 0.003))
        p.append(span(f"MT_WainN{x0:.0f}", x0, x1, 0, 0.9, HD - 0.004, HD, M["hwain"], 0))
    for xs in (-1, 1):
        a, b = sorted((xs * HW, xs * (HW - 0.012)))
        p.append(span(f"MT_BaseEW{xs}", a, b, 0, 0.06, -HD, SPLIT - 0.05, M["trim"], 0.003))
        p.append(span(f"MT_CoveEW{xs}", a, b, 0, 0.08, SPLIT + 0.05, HD, M["cove"], 0.003))
        a2, b2 = sorted((xs * HW, xs * (HW - 0.004)))
        p.append(span(f"MT_WainEW{xs}", a2, b2, 0, 0.9, SPLIT + 0.05, HD, M["hwain"], 0))
        a3, b3 = sorted((xs * HW, xs * (HW - 0.025)))
        p.append(span(f"MT_CrownEW{xs}", a3, b3, H - 0.04, H, -HD, SPLIT - 0.05, M["trim"], 0.004))
    p.append(span("MT_CrownS", -HW, HW, H - 0.04, H, -HD, -HD + 0.025, M["trim"], 0.004))
    # 扉の枠（手前は白い木枠、奥はアルミ）
    for zs, m in ((-1, M["trim"]), (1, M["alu"])):
        zin = zs * HD
        for xs in (-1, 1):
            p.append(span(f"MT_Cas{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.07))), 0, DOOR_H + 0.07,
                          *sorted((zin, zin - zs * 0.018)), m, 0.004))
            p.append(span(f"MT_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), m, 0.002))
        p.append(span(f"MT_CasT{zs}", -DOOR_HALF - 0.07, DOOR_HALF + 0.07, DOOR_H, DOOR_H + 0.07, *sorted((zin, zin - zs * 0.018)), m, 0.004))
        p.append(span(f"MT_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), m, 0.002))
    return [finish(o, 1.0, angle=40) for o in p]


def fixtures(M):
    """手前：丸いシーリングライト。奥：埋込みLED。扉の脇のスイッチ"""
    p = []
    p.append(lathe("MF_CeilLight", (0, H - 0.09, -2.5), [(0.0, 0.0), (0.2, 0.004), (0.26, 0.03), (0.27, 0.07), (0.26, 0.09)], M["light"], 48))
    p.append(span("MF_HospLedFrame", -0.62, 0.62, H - 0.02, H - 0.012, 2.6, 3.24, M["H"]["ivory"], 0.004))
    p.append(span("MF_HospLed", -0.58, 0.58, H - 0.024, H - 0.019, 2.64, 3.2, M["hled"], 0.002))
    p.append(span("MF_Switch", -0.84, -0.72, 1.15, 1.27, -HD, -HD + 0.01, M["white"], 0.004))
    p.append(span("MF_SwitchKey", -0.81, -0.75, 1.18, 1.24, -HD + 0.01, -HD + 0.016, M["white"], 0.003))
    return [finish(o, 2.0, angle=50) for o in p]


def _sash(M, z0, z1, y0, y1, tag):
    """東の窓のアルミサッシ（引き違い）とガラス・窓台"""
    p, g = [], []
    xi, xo = HW, HW0
    for (a0, a1, b0, b1) in ((z0, z1, y0 - 0.02, y0), (z0, z1, y1, y1 + 0.02)):
        p.append(span(f"MW_Rev{tag}", xi, xo, b0, b1, a0, a1, M["trim"], 0.002))
    for zz in (z0 - 0.02, z1):
        p.append(span(f"MW_RevS{tag}", xi, xo, y0 - 0.02, y1 + 0.02, zz, zz + 0.02, M["trim"], 0.002))
    xs = HW + 0.045
    for (a0, a1, b0, b1) in ((z0, z1, y0, y0 + 0.035), (z0, z1, y1 - 0.035, y1), (z0, z0 + 0.035, y0, y1), (z1 - 0.035, z1, y0, y1)):
        p.append(span(f"MW_Frame{tag}", xs - 0.022, xs + 0.022, b0, b1, a0, a1, M["alu"], 0.003))
    zm = (z0 + z1) / 2
    for k, (c0, c1, off) in enumerate(((z0 + 0.035, zm + 0.02, -0.01), (zm - 0.02, z1 - 0.035, 0.01))):
        xx = xs + off
        for (a0, a1, b0, b1) in ((c0, c1, y0 + 0.035, y0 + 0.065), (c0, c1, y1 - 0.065, y1 - 0.035), (c0, c0 + 0.03, y0 + 0.035, y1 - 0.035), (c1 - 0.03, c1, y0 + 0.035, y1 - 0.035)):
            p.append(span(f"MW_Sash{tag}{k}", xx - 0.011, xx + 0.011, b0, b1, a0, a1, M["alu"], 0.003))
        q = quad(f"MW_Glass{tag}{k}", [(xx, y0 + 0.065, c0 + 0.03), (xx, y0 + 0.065, c1 - 0.03), (xx, y1 - 0.065, c1 - 0.03), (xx, y1 - 0.065, c0 + 0.03)], M["glass"])
        hq.face_toward(q, (-1, 0, 0)); g.append(q)
    p.append(span(f"MW_Sill{tag}", HW - 0.08, HW, y0 - 0.03, y0, z0 - 0.04, z1 + 0.04, M["trim"], 0.006, 3))
    return p, g


def warm_window(M):
    """手前の窓：アルミサッシ・レースのカーテン（閉じている）と厚手のカーテン（両脇に寄せる）・夜の街"""
    z0, z1, y0, y1 = WARM_WIN
    p, g = _sash(M, z0, z1, y0, y1, "W")
    rx = HW - 0.08
    p.append(pipe("MW_Rail", [(rx, y1 + 0.12, z0 - 0.35), (rx, y1 + 0.12, z1 + 0.35)], 0.01, M["white"], 0.02, 10))
    out = [finish(o, 2.0, angle=40) for o in p] + g
    # レース（窓の全面、ゆるいひだ）
    lace = _cloth("MW_Lace", lambda u, v: (rx + 0.03 + 0.02 * math.sin(u * math.pi * 16), 0.9 + v * (y1 + 0.1 - 0.9), z0 - 0.1 + u * (z1 - z0 + 0.2)),
                  48, 4, M["lace"], uvs=3.0, thick=0.001, uvmode="zy")
    out.append(lace)
    for k, zc in enumerate((z0 - 0.15, z1 + 0.15)):
        dr = _cloth(f"MW_Drape{k}", lambda u, v, zc=zc: (rx - 0.02 + 0.035 * math.sin(u * math.pi * 7), 0.1 + v * (y1 + 0.1 - 0.1), zc + (u - 0.5) * 0.28),
                    40, 6, M["drape"], uvmode="zy")
        out.append(dr)
    # 外：夜の街（窓から 4m 外、北は病室の窓の景色と分ける）
    xk = HW0 + 4.0
    sky = quad("MW_Night", [(xk, -0.8, 1.0), (xk, -0.8, -9.0), (xk, 4.2, -9.0), (xk, 4.2, 1.0)], M["night"],
               uv=((0.3, 0), (1, 0), (1, 1), (0.3, 1)))
    hq.face_toward(sky, (-1, 0, 0))
    out.append(sky)
    return out


def hosp_window(M):
    """奥の窓：アルミサッシ・ブラインド（半分）・昼の中庭"""
    z0, z1, y0, y1 = HOSP_WIN
    p, g = _sash(M, z0, z1, y0, y1, "H")
    bx = HW - 0.05
    p.append(span("MW_BlindHead", bx - 0.025, bx + 0.025, y1 - 0.05, y1, z0 - 0.02, z1 + 0.02, M["blind"], 0.006))
    slats = []
    ys = y1 - 0.06
    n = 0
    while ys > y1 - 0.5:
        c, s = math.cos(0.55), math.sin(0.55)
        hw_ = 0.0125
        prof = [(bx - hw_ * c, ys - hw_ * s - 0.001), (bx + hw_ * c, ys + hw_ * s - 0.001), (bx + hw_ * c, ys + hw_ * s + 0.001), (bx - hw_ * c, ys - hw_ * s + 0.001)]
        slats.append(profile_z(f"MW_Slat{n}", prof, z0 + 0.01, z1 - 0.01, M["blind"]))
        ys -= 0.022; n += 1
    p.append(span("MW_BlindBottom", bx - 0.016, bx + 0.016, ys - 0.022, ys - 0.01, z0 + 0.01, z1 - 0.01, M["blind"], 0.004))
    out = [finish(o, 2.0, angle=40) for o in p] + [finish(join(slats, "MW_Slats"), 2.0, angle=20)] + g
    xk = HW0 + 4.0
    sky = quad("MW_Day", [(xk, -1.0, 10.0), (xk, -1.0, 1.0), (xk, 5.25, 1.0), (xk, 5.25, 10.0)], M["sky"],
               uv=((0, 0), (0.45, 0), (0.45, 1), (0, 1)))
    hq.face_toward(sky, (-1, 0, 0))
    out.append(sky)
    return out


def fusuma(M):
    """襖（z = 1.0）：左右の固定の2枚ずつ＋左へ引き開けた1枚、敷居と鴨居、上は小壁。病室側は病院の白い壁"""
    p, flat = [], []
    zc = SPLIT
    zw0, zw1 = zc - 0.05, zc + 0.05
    # 病室側の面（白い壁と腰壁、ステンレスの蹴込み）と、手前の小壁
    for xs in (-1, 1):
        xa, xb = sorted((xs * 1.075, xs * HW))
        p.append(span(f"MU_Back{xs}", xa, xb, 0.0, H, zc + 0.02, zw1, M["hpaint"], 0))
        p.append(span(f"MU_BackWain{xs}", xa, xb, 0.0, 0.9, zw1, zw1 + 0.004, M["hwain"], 0))
        p.append(span(f"MU_BackCove{xs}", xa, xb, 0.0, 0.08, zw1, zw1 + 0.012, M["cove"], 0.003))
    # 鴨居の上の小壁（手前はクロス、病室側は白）と、開口の両脇の柱
    p.append(span("MU_Transom", -HW, HW, 2.06, H, zw0, zc + 0.02, M["wall"], 0))
    p.append(span("MU_TransomBack", -1.1, 1.1, 2.0, H, zc + 0.02, zw1, M["hpaint"], 0))
    for xs in (-1, 1):
        p.append(span(f"MU_Post{xs}", *sorted((xs * 1.04, xs * 1.1)), 0.0, 2.06, zw0 - 0.03, zw1, M["woodD"], 0.006))
    # 鴨居と敷居
    p.append(span("MU_Kamoi", -HW, HW, 1.98, 2.06, zw0 - 0.03, zc + 0.02, M["woodD"], 0.006))
    p.append(span("MU_Shikii", -HW, HW, 0.0, 0.015, zw0 - 0.03, zc + 0.02, M["woodD"], 0.004))
    for k in (-0.035, -0.005):
        p.append(span(f"MU_Groove{k}", -HW, HW, 0.012, 0.016, zc + k - 0.006, zc + k + 0.006, M["black"], 0))
    # 襖の板（縁は黒の漆、引手は真鍮の丸）
    def panel(name, x0, x1, zf, hikite_side):
        q = []
        q.append(span(f"{name}_Paper", x0 + 0.02, x1 - 0.02, 0.02, 1.96, zf - 0.009, zf + 0.009, M["fusuma"], 0))
        for (a0, a1, b0, b1) in ((x0, x1, 0.0, 0.025), (x0, x1, 1.955, 1.98), (x0, x0 + 0.02, 0.0, 1.98), (x1 - 0.02, x1, 0.0, 1.98)):
            q.append(span(f"{name}_Rim", a0, a1, b0, b1, zf - 0.011, zf + 0.011, M["lacq"], 0.003))
        hx = x0 + 0.08 if hikite_side < 0 else x1 - 0.08
        q.append(cyl_between(f"{name}_Hikite", (hx, 0.88, zf - 0.0115), (hx, 0.88, zf - 0.013), 0.035, M["brass"], 24))
        q.append(cyl_between(f"{name}_HikiteIn", (hx, 0.88, zf - 0.013), (hx, 0.88, zf - 0.009), 0.024, M["lacq"], 24))
        return q
    zf = zc - 0.035
    for xs in (-1, 1):
        a = xs * 1.075
        b = xs * (HW - 0.001)
        x0, x1 = sorted((a, b))
        mid = (x0 + x1) / 2
        p += panel(f"MU_P{xs}a", x0, mid, zf, -1)
        p += panel(f"MU_P{xs}b", mid, x1, zf, 1)
    # 引き開けた1枚（手前の溝、左の固定の襖に重ねる）
    p += panel("MU_Open", -1.95, -1.05, zc - 0.058, 1)
    return [finish(o, 1 / 0.9 if o.name.endswith("_Paper") else 1.0, angle=40) for o in p]


# ============================== 手前の家具 ==============================

def warm_bed(M):
    """ベッド（ビルダーの Bed：(-hw+0.85, *, -2.6)、1.0 x 2.0。頭 = 南）：木の枠・マットレス・小花柄の布団・枕・ぬいぐるみ"""
    cx, cz = -HW0 + 0.85, -2.6
    p = []
    p.append(span("WB_Frame", cx - 0.5, cx + 0.5, 0.12, 0.3, cz - 1.0, cz + 1.0, M["wood"], 0.01))
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(span(f"WB_Leg{sx}{sz}", cx + sx * 0.46 - 0.03, cx + sx * 0.46 + 0.03, 0.0, 0.12, cz + sz * 0.96 - 0.03, cz + sz * 0.96 + 0.03, M["wood"], 0.005))
    p.append(span("WB_Head", cx - 0.5, cx + 0.5, 0.12, 0.78, cz - 1.03, cz - 0.98, M["wood"], 0.012, 3))
    p.append(span("WB_HeadShelf", cx - 0.5, cx + 0.5, 0.78, 0.8, cz - 1.12, cz - 0.98, M["wood"], 0.006))
    p.append(span("WB_Mattress", cx - 0.47, cx + 0.47, 0.3, 0.44, cz - 0.97, cz + 0.97, M["pillow"], 0.035, 4))
    out = [finish(o, 1.0, angle=40) for o in p]

    def duv(u, v):
        x = (u - 0.5) * 1.1
        z = cz - 0.45 + v * 1.5
        ax = abs(x)
        wav = 0.012 * math.sin(v * 9 + u * 4) * math.sin(u * math.pi)
        if ax < 0.45:
            y = 0.5 + 0.03 * math.sin(u * math.pi) + wav
        else:
            t = min(1.0, (ax - 0.45) / 0.1)
            y = 0.5 - 0.3 * t ** 1.1 + wav
            x = math.copysign(0.45 + 0.05 * math.sin(t * math.pi / 2) + 0.02, x)
        # 裾は少しめくれている
        if v > 0.93:
            y += 0.02 * (v - 0.93) / 0.07
        return (cx + x, y, z)
    out.append(_cloth("WB_Duvet", duv, 34, 36, M["duvet"], thick=0.03))
    for k, dz in enumerate((-0.78,)):
        pw = pillow(f"WB_Pillow{k}", (cx, 0.5, cz + dz), (0.62, 0.13, 0.36), M["pillow"], seed=31)
        out.append(finish(pw, 2.0, angle=60))
    # うさぎのぬいぐるみ（枕の脇）
    rx, rz = cx + 0.3, cz - 0.55
    body = pillow("WB_RabbitBody", (rx, 0.55, rz), (0.14, 0.16, 0.12), M["rabbit"], seed=33)
    body.data.transform(Matrix.Translation(U(rx, 0.55, rz)) @ Matrix.Rotation(math.radians(80), 4, "X") @ Matrix.Translation(-U(rx, 0.55, rz)))
    rab = [body, lathe("WB_RabbitHead", (rx, 0.61, rz - 0.02), [(0.0, 0.0), (0.05, 0.01), (0.06, 0.05), (0.045, 0.09), (0.0, 0.1)], M["rabbit"], 20)]
    for s in (-1, 1):
        rab.append(lathe(f"WB_RabbitEar{s}", (rx + s * 0.025, 0.69, rz - 0.02), [(0.0, 0.0), (0.016, 0.02), (0.018, 0.08), (0.0, 0.1)], M["rabbit"], 12))
        rab.append(cyl_between(f"WB_RabbitEye{s}", (rx + s * 0.02, 0.66, rz - 0.075), (rx + s * 0.02, 0.66, rz - 0.08), 0.006, M["black"], 8))
    out += [finish(o, 2.0, angle=60) for o in rab]
    return out


def chest(M):
    """チェスト（ビルダー：(hw-0.45, 0.3, -2.2)、0.6 x 0.6 x 0.4）：3段の引き出し・写真立て・鏡・化粧水"""
    cx, cz = HW0 - 0.45, -2.2
    p, flat = [], []
    x0, x1, z0, z1 = cx - 0.3, cx + 0.3, cz - 0.2, cz + 0.2
    xf = x0
    p.append(span("WC_Body", x0 + 0.01, x1, 0.04, 0.6, z0, z1, M["white"], 0.008))
    p.append(span("WC_Top", x0, x1, 0.6, 0.62, z0 - 0.01, z1 + 0.01, M["wood"], 0.006))
    for k in range(3):
        y0 = 0.06 + k * 0.18
        p.append(span(f"WC_Drawer{k}", xf - 0.012, xf + 0.01, y0, y0 + 0.165, z0 + 0.015, z1 - 0.015, M["wood"], 0.006))
        p.append(span(f"WC_Pull{k}", xf - 0.024, xf - 0.012, y0 + 0.07, y0 + 0.095, cz - 0.06, cz + 0.06, M["brass"], 0.004))
    # 写真立て（西 = 部屋側を向く）と卓上ミラー・化粧水
    fx, fz = cx - 0.05, cz + 0.05
    p.append(span("WC_PFrame", fx - 0.012, fx + 0.012, 0.62, 0.82, fz - 0.14, fz + 0.14, M["wood"], 0.006))
    p.append(span("WC_PLeg", fx + 0.012, fx + 0.07, 0.62, 0.75, fz - 0.02, fz + 0.02, M["wood"], 0.003))
    ph = quad("WC_Photo", [(fx - 0.013, 0.64, fz - 0.12), (fx - 0.013, 0.64, fz + 0.12), (fx - 0.013, 0.8, fz + 0.12), (fx - 0.013, 0.8, fz - 0.12)], M["photo"])
    hq.face_toward(ph, (-1, 0, 0)); flat.append(ph)
    mx, mz = cx + 0.05, cz - 0.12
    p.append(lathe("WC_MirrorFoot", (mx, 0.62, mz), [(0.05, 0.0), (0.05, 0.01), (0.012, 0.02), (0.01, 0.06)], M["white"], 20))
    mir = cyl_between("WC_Mirror", (mx - 0.008, 0.76, mz), (mx + 0.008, 0.76, mz), 0.08, M["white"], 32)
    p.append(mir)
    p.append(cyl_between("WC_MirrorGlass", (mx - 0.009, 0.76, mz), (mx - 0.0095, 0.76, mz), 0.072, M["mirror"], 32))
    for k, (bx, bz, hh) in enumerate(((cx + 0.15, cz + 0.12, 0.14), (cx + 0.2, cz + 0.05, 0.1))):
        p.append(lathe(f"WC_Bottle{k}", (bx, 0.62, bz), [(0.0, 0.0), (0.025, 0.0), (0.025, hh), (0.012, hh + 0.02), (0.012, hh + 0.035), (0.0, hh + 0.035)], M["bottle"], 16))
    return [finish(o, 1.0, angle=40) for o in p] + flat


def potted_plant(M):
    objs = lab_room.plant({"pot": M["terracotta"], "soil": M["soil"], "stem": M["stem"], "leaf": M["leaf"]}, seed=1471)
    src = (-lab_room.HW + 0.45, lab_room.HD - 0.45)
    dst = (HW0 - 0.5, -3.6)
    mv = Matrix.Translation(U(dst[0] - src[0], 0, dst[1] - src[1]) - U(0, 0, 0))
    sc = Matrix.Translation(U(dst[0], 0, dst[1])) @ Matrix.Scale(0.8, 4) @ Matrix.Translation(-U(dst[0], 0, dst[1]))
    for o in objs:
        o.data.transform(mv)
        o.data.transform(sc)
    return objs


def wall_items(M):
    """西の壁のコルクボード（-2.0）とカレンダー（-1.05）、扉脇のフックに白衣"""
    out = []
    xw = -HW
    p = [span("WI_CorkFrame", xw, xw + 0.03, 1.33, 1.87, -2.39, -1.61, M["wood"], 0.006)]
    q = quad("WI_Cork", [(xw + 0.031, 1.35, -2.37), (xw + 0.031, 1.35, -1.63), (xw + 0.031, 1.85, -1.63), (xw + 0.031, 1.85, -2.37)], M["cork"])
    hq.face_toward(q, (1, 0, 0)); out.append(q)
    c = quad("WI_Cal", [(xw + 0.004, 1.2, -1.25), (xw + 0.004, 1.2, -0.85), (xw + 0.004, 1.76, -0.85), (xw + 0.004, 1.76, -1.25)], M["cal"])
    hq.face_toward(c, (1, 0, 0)); out.append(c)
    p.append(cyl_between("WI_CalPin", (xw, 1.78, -1.05), (xw + 0.02, 1.78, -1.05), 0.004, M["silver"], 6))
    # 白衣（南の壁、扉の東のフック）
    hx, hz = 1.05, -HD
    p.append(cyl_between("WI_Hook", (hx, 1.72, hz), (hx, 1.72, hz + 0.06), 0.008, M["silver"], 8))
    p.append(pipe("WI_Hanger", [(hx - 0.2, 1.6, hz + 0.08), (hx, 1.68, hz + 0.08), (hx + 0.2, 1.6, hz + 0.08)], 0.006, M["wood"], 0.05, 6))

    def coat(u, v):
        # u: 横（-1〜1）、v: 上（0）→下（1）
        wv = 0.21 + 0.06 * v
        x = hx + (u - 0.5) * 2 * wv
        y = 1.6 - v * 0.95 - 0.04 * (abs(u - 0.5) * 2) ** 2 * (1 - v)
        z = hz + 0.1 + 0.05 * math.sin(v * math.pi * 0.8) + 0.02 * math.sin(u * math.pi * 6) * v
        return (x, y, z)
    out.append(_cloth("WI_Coat", coat, 20, 16, M["coat"], thick=0.02, uvmode="xy"))
    for s in (-1, 1):
        out.append(_cloth(f"WI_Sleeve{s}", lambda u, v, s=s: (hx + s * (0.2 + 0.03 * v) + 0.03 * math.cos(u * math.pi * 2), 1.58 - v * 0.6, hz + 0.1 + 0.03 * math.sin(u * math.pi * 2)),
                          10, 8, M["coat"], thick=0.004, uvmode="xy"))
    return [finish(o, 1.0, angle=40) for o in p] + out


def low_table(M):
    """ローテーブル（ビルダー：(1.3, *, -1.6)、天板 0.9 x 0.6・上面 0.365）とマグ・リモコン、座布団"""
    cx, cz = 1.3, -1.6
    p = [span("WL_Top", cx - 0.45, cx + 0.45, 0.33, 0.365, cz - 0.3, cz + 0.3, M["wood"], 0.012, 3),
         span("WL_Apron", cx - 0.4, cx + 0.4, 0.28, 0.33, cz - 0.25, cz + 0.25, M["wood"], 0.004)]
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(span(f"WL_Leg{sx}{sz}", cx + sx * 0.4 - 0.025, cx + sx * 0.4 + 0.025, 0.0, 0.33, cz + sz * 0.25 - 0.025, cz + sz * 0.25 + 0.025, M["wood"], 0.004))
    p.append(lathe("WL_Mug", (cx + 0.25, 0.365, cz - 0.1), [(0.0, 0.0), (0.035, 0.0), (0.04, 0.09), (0.036, 0.09), (0.032, 0.008), (0.0, 0.008)], M["mug"], 24))
    p.append(pipe("WL_MugHandle", [(cx + 0.29, 0.43, cz - 0.1), (cx + 0.33, 0.41, cz - 0.1), (cx + 0.29, 0.38, cz - 0.1)], 0.006, M["mug"], 0.01, 6))
    p.append(span("WL_Remote", cx + 0.1, cx + 0.14, 0.365, 0.38, cz + 0.05, cz + 0.22, M["black"], 0.006))
    out = [finish(o, 1.0, angle=40) for o in p]
    for k, (x, z) in enumerate(((cx - 0.05, cz - 0.62),)):
        c = pillow(f"WL_Zabuton{k}", (x, 0.04, z), (0.55, 0.08, 0.55), M["cushion"], seed=41)
        out.append(finish(c, 2.0, angle=60))
    return out


def counter(M):
    """台所カウンター（ビルダー：(-hw+0.6, *, -0.2)、1.0 x 0.8・上面 0.88）：流し・水栓・水切り・カセットコンロとケトル"""
    cx, cz = -HW0 + 0.6, -0.2
    p = []
    x0, x1, z0, z1 = cx - 0.5, cx + 0.5, cz - 0.4, cz + 0.4
    p.append(span("KC_Body", x0 + 0.02, x1 - 0.02, 0.08, 0.84, z0 + 0.02, z1 - 0.02, M["white"], 0.008))
    p.append(span("KC_Plinth", x0 + 0.05, x1 - 0.05, 0.0, 0.08, z0 + 0.05, z1 - 0.05, M["black"], 0.004))
    # 天板（ステンレス）に流しの穴
    sx0, sx1, sz0, sz1 = cx - 0.45, cx - 0.12, cz + 0.05, cz + 0.36
    p += train_room.grid_wall("KC_Top", [(sx0, sx1, sz0, sz1)], x0, x1, z0, z1, lambda a, b, c, d: (a, b, 0.84, 0.88, c, d), M["steel"])
    p.append(span("KC_SinkB", sx0, sx1, 0.7, 0.71, sz0, sz1, M["steel"], 0.01))
    for (a0, a1, b0, b1) in ((sx0, sx1, sz0, sz0 + 0.005), (sx0, sx1, sz1 - 0.005, sz1), (sx0, sx0 + 0.005, sz0, sz1), (sx1 - 0.005, sx1, sz0, sz1)):
        p.append(span("KC_SinkW", a0, a1, 0.7, 0.88, b0, b1, M["steel"], 0))
    p.append(pipe("KC_Faucet", [(cx - 0.285, 0.88, cz + 0.38), (cx - 0.285, 1.08, cz + 0.38), (cx - 0.285, 1.1, cz + 0.3), (cx - 0.285, 1.04, cz + 0.24)], 0.012, M["steel"], 0.04, 10))
    p.append(cyl_between("KC_FaucetLever", (cx - 0.285, 1.0, cz + 0.38), (cx - 0.2, 1.02, cz + 0.38), 0.006, M["steel"], 8))
    # 扉と取っ手（南の面 = 部屋側）
    for k in range(2):
        a, b = x0 + 0.03 + k * 0.47, x0 + 0.03 + (k + 1) * 0.47
        p.append(span(f"KC_Door{k}", a + 0.004, b - 0.004, 0.1, 0.8, z0 + 0.008, z0 + 0.02, M["white"], 0.006))
        p.append(span(f"KC_Pull{k}", (a + b) / 2 - 0.06, (a + b) / 2 + 0.06, 0.72, 0.74, z0 - 0.004, z0 + 0.008, M["silver"], 0.004))
    # カセットコンロとケトル（右奥）
    gx, gz = cx + 0.33, cz + 0.2
    p.append(span("KC_Stove", gx - 0.17, gx + 0.17, 0.88, 0.97, gz - 0.14, gz + 0.14, M["black"], 0.01))
    p.append(lathe("KC_Burner", (gx, 0.97, gz), [(0.0, 0.0), (0.06, 0.0), (0.06, 0.01), (0.0, 0.01)], M["silver"], 20))
    p.append(lathe("KC_Kettle", (gx, 0.98, gz), [(0.0, 0.0), (0.09, 0.0), (0.1, 0.05), (0.085, 0.12), (0.05, 0.16), (0.03, 0.17), (0.0, 0.17)], M["kettle"], 32))
    p.append(pipe("KC_KettleHandle", [(gx - 0.05, 1.15, gz), (gx, 1.22, gz), (gx + 0.05, 1.15, gz)], 0.008, M["black"], 0.03, 8))
    p.append(pipe("KC_Spout", [(gx + 0.09, 1.04, gz), (gx + 0.15, 1.1, gz)], 0.012, M["kettle"], 0.01, 8))
    # 水切りかごとマグ2つ（1つは伏せてある）
    dx, dz = cx + 0.05, cz + 0.3
    p.append(span("KC_Rack", dx - 0.12, dx + 0.12, 0.88, 0.9, dz - 0.08, dz + 0.08, M["white"], 0.004))
    for k in range(5):
        xx = dx - 0.1 + k * 0.05
        p.append(pipe(f"KC_RackWire{k}", [(xx, 0.9, dz - 0.08), (xx, 0.98, dz - 0.08), (xx, 0.98, dz + 0.08), (xx, 0.9, dz + 0.08)], 0.002, M["white"], 0.01, 5))
    mug = lathe("KC_Mug", (0, 0, 0), [(0.0, 0.0), (0.035, 0.0), (0.04, 0.09), (0.036, 0.09), (0.032, 0.008), (0.0, 0.008)], M["mug"], 20)
    mug.data.transform(Matrix.Translation(U(dx - 0.05, 0.99, dz)) @ Matrix.Rotation(math.radians(180), 4, "X"))
    p.append(mug)
    return [finish(o, 1.0, angle=40) for o in p]


def fridge(M):
    """小さな2ドア冷蔵庫（カウンターの北、襖の手前）"""
    cx, cz = -HW0 + 0.35, 0.62
    p = [span("KF_Body", cx - 0.25, cx + 0.25, 0.02, 1.1, cz - 0.27, cz + 0.27, M["white"], 0.02, 3)]
    xf = cx + 0.25
    p.append(span("KF_Gap", xf, xf + 0.002, 0.72, 0.735, cz - 0.26, cz + 0.26, M["black"], 0))
    for (y0, y1) in ((0.76, 0.98), (0.2, 0.66)):
        p.append(span(f"KF_Handle{y0}", xf, xf + 0.025, y0, y1, cz + 0.2, cz + 0.23, M["silver"], 0.008))
    # マグネットとメモ
    p.append(span("KF_Memo", xf, xf + 0.002, 0.85, 1.0, cz - 0.15, cz - 0.02, M["pillow"], 0))
    p.append(cyl_between("KF_Magnet", (xf + 0.002, 0.98, cz - 0.085), (xf + 0.012, 0.98, cz - 0.085), 0.012, M["kettle"], 12))
    p.append(lathe("KF_Plant", (cx, 1.1, cz), [(0.0, 0.0), (0.05, 0.0), (0.055, 0.07), (0.0, 0.07)], M["terracotta"], 16))
    for k in range(5):
        a = k * 1.25
        p.append(cyl_between(f"KF_Sprout{k}", (cx, 1.16, cz), (cx + 0.05 * math.cos(a), 1.26, cz + 0.05 * math.sin(a)), 0.012, M["leaf"], 6))
    return [finish(o, 1.0, angle=40) for o in p]


def bag(M):
    """詰めかけのボストンバッグ（ベッドの足元、南の壁際）"""
    bx, bz = -0.95, -3.95
    p = []
    body = pillow("KB_Bag", (bx, 0.17, bz), (0.55, 0.3, 0.3), M["bag"], puff=1.0, seed=51)
    body.data.transform(Matrix.Translation(U(bx, 0.17, bz)) @ Matrix.Scale(1.0, 4) @ Matrix.Translation(-U(bx, 0.17, bz)))
    p.append(body)
    for s in (-1, 1):
        p.append(pipe(f"KB_Handle{s}", [(bx - 0.12, 0.3, bz + s * 0.06), (bx - 0.08, 0.45, bz + s * 0.05), (bx + 0.08, 0.45, bz + s * 0.05), (bx + 0.12, 0.3, bz + s * 0.06)], 0.012, M["leather"], 0.05, 8))
    # 開いた口からのぞく服
    p.append(pillow("KB_Clothes", (bx + 0.05, 0.3, bz), (0.34, 0.08, 0.18), M["clothes"], seed=52))
    p.append(pillow("KB_Clothes2", (bx - 0.1, 0.31, bz + 0.02), (0.2, 0.06, 0.16), M["cushion"], seed=53))
    return [finish(o, 2.0, angle=60) for o in p]


# ============================== 奥の病室 ==============================

def hospital(M):
    Hm = M["H"]
    out = []
    bx, bz = HBED
    beds = join(ward_room.bed(Hm), "HB_Bed")
    beds.data.transform(Matrix.Translation(U(bx, 0, bz)) @ Matrix.Rotation(math.radians(180), 4, "Z"))
    out.append(beds)
    out += ward_room.iv_stand(Hm, bx + 0.75, bz + 0.5, bag=True, seed=3)
    out += ward_room.cabinet(Hm, HCAB[0], HCAB[1], -1, 91)
    out += ward_room.monitor(Hm, bx + 0.78, bz + 1.15, 1)
    # 床頭台の上の花（新しい花。毎日、誰かが替えている）
    vx, vz = HCAB[0] - 0.12, HCAB[1] - 0.12
    vase = lathe("HF_Vase", (vx, 0.825, vz), [(0.0, 0.0), (0.03, 0.0), (0.04, 0.06), (0.025, 0.14), (0.028, 0.16), (0.022, 0.16)], M["bottle"], 20, cap_top=False)
    out.append(finish(vase, 2.0, angle=60))
    rnd = random.Random(1481)
    for k in range(7):
        a = rnd.uniform(0, math.pi * 2); r = rnd.uniform(0.02, 0.07); ht = rnd.uniform(0.18, 0.3)
        tip = (vx + math.cos(a) * r, 0.825 + 0.16 + ht, vz + math.sin(a) * r)
        out.append(finish(cyl_between(f"HF_Stem{k}", (vx, 0.95, vz), tip, 0.003, M["leaf"], 6), 2.0))
        out.append(finish(lathe(f"HF_Bloom{k}", (tip[0], tip[1] - 0.015, tip[2]), [(0.0, 0.0), (0.03, 0.012), (0.035, 0.025), (0.0, 0.02)], M["flower"], 12), 2.0))
    # カーテンレールと、半分引いたカーテン（ベッドの西、z 1.6〜2.6）
    xr = bx - 0.72
    out.append(finish(pipe("HC_Track", [(xr, H - 0.04, bz - 1.2), (xr, H - 0.04, bz + 1.2)], 0.012, Hm["chrome"], 0.05, 8), 2.0))
    top, bot = H - 0.06, 0.35

    def cur(u, v):
        z = (bz - 1.15) + u * 1.0
        return (xr + 0.035 * math.sin(u * math.pi * 11), bot + v * (top - bot), z)
    c = hq.grid_surface("HC_Curtain", 66, 6, cur, M["curtain"])
    me = c.data
    uvl = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = (-co.y * 2, co.z * 2)
    s = c.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.003
    finish(c, keep_uv=True, angle=180)
    _outward(c)
    out.append(c)
    return out


def interior(M):
    out = []
    out += warm_bed(M)
    out += chest(M)
    out += potted_plant(M)
    out += wall_items(M)
    out += low_table(M)
    out += counter(M)
    out += fridge(M)
    out += bag(M)
    rug = quad("WR_Rug", [(-0.4, 0.01, -2.25), (1.2, 0.01, -2.25), (1.2, 0.01, -0.95), (-0.4, 0.01, -0.95)], M["rug"],
               uv=((0, 0), (1.6 * 2, 0), (1.6 * 2, 1.3 * 2), (0, 1.3 * 2)))
    hq.face_toward(rug, (0, 1, 0))
    s = rug.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.012
    out.append(finish(rug, keep_uv=True, angle=40))
    out += hospital(M)
    return out


# ============================== 資料・装置 ==============================

def laptop(M):
    """ノートPC（ビルダーの Laptop 箱 0.32 x 0.03 x 0.24 の中心が原点。画面は奥 = +Z に立つ）"""
    p = [span("LP_Base", -0.16, 0.16, -0.015, 0.005, -0.12, 0.12, M["silver"], 0.006, 3),
         span("LP_Keys", -0.14, 0.14, 0.005, 0.007, -0.02, 0.1, M["black"], 0.002),
         span("LP_Pad", -0.045, 0.045, 0.005, 0.006, -0.1, -0.035, M["black"], 0.002)]
    lid = [span("LP_Lid", -0.16, 0.16, 0.0, 0.225, -0.006, 0.0, M["silver"], 0.004)]
    scr = quad("LP_Screen", [(-0.145, 0.015, -0.0065), (0.145, 0.015, -0.0065), (0.145, 0.21, -0.0065), (-0.145, 0.21, -0.0065)], M["screen"])
    hq.face_toward(scr, (0, 0, -1))
    lid_one = join([finish(o, 2.0, angle=40) for o in lid] + [scr], "LP_LidAll")
    # ヒンジ（z=+0.12, y=0.005）で奥へ 15° 倒す
    lid_one.data.transform(Matrix.Translation(U(0, 0.005, 0.12)) @ Matrix.Rotation(math.radians(15), 4, "X"))
    return [join([finish(o, 2.0, angle=40) for o in p] + [lid_one], "MizunoApart_Laptop")]


def diary(M):
    """開いた日記帳（赤い布の表紙）"""
    cover = finish(span("DY_Cover", -0.13, 0.13, -0.01, -0.005, -0.09, 0.09, M["cover"], 0.003), 2.0)
    q = quad("DY_Pages", [(-0.125, -0.004, -0.085), (0.125, -0.004, -0.085), (0.125, -0.004, 0.085), (-0.125, -0.004, 0.085)], M["diary"])
    hq.face_toward(q, (0, 1, 0))
    one = join([cover, q], "MizunoApart_Diary")
    one.data.transform(Matrix.Rotation(math.radians(-8), 4, "Z"))
    return [one]


def recorder(M):
    """ボイスレコーダー（ビルダーの箱 0.05 x 0.02 x 0.12）"""
    p = [span("RC_Body", -0.022, 0.022, -0.01, 0.006, -0.06, 0.06, M["black"], 0.006, 3),
         span("RC_Lcd", -0.014, 0.014, 0.006, 0.007, 0.0, 0.03, M["screen"], 0.001),
         span("RC_Grille", -0.014, 0.014, 0.006, 0.007, 0.035, 0.055, M["silver"], 0.001)]
    for k in range(3):
        p.append(cyl_between(f"RC_Btn{k}", (-0.01 + k * 0.01, 0.006, -0.03), (-0.01 + k * 0.01, 0.009, -0.03), 0.004, [M["kettle"], M["silver"], M["silver"]][k], 10))
    return [join([finish(o, 4.0, angle=40) for o in p], "MizunoApart_Recorder")]


def door(M):
    return [join(dim_room.door({"woodDark": M["woodD"], "wood": M["wood"], "brass": M["silver"]}), "MizunoApart_Door")]


def _breaker_mats(M):
    return {"mel": M["white"], "grille": M["black"], "hazard": M["hazard"], "sus": M["silver"], "rubber": M["black"], "lever": M["lever"]}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.01), "MizunoApart_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "MizunoApart_Lever")]


PIECES = {"Shell": shell, "Interior": interior, "Laptop": laptop, "Diary": diary, "Recorder": recorder,
          "Door": door, "Breaker": breaker, "Lever": lever}
