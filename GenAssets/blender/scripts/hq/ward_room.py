"""
臨床病棟（ward：幅9 x 奥行12 x 天井3.2）を品質重視で作る。
6床の大部屋：長尺ビニルの床・ミントの腰壁とバンパー・吸音板の天井と埋込み照明・
窓（アルミサッシ・ブラインド・中庭の木と向かいの病棟）・U字のカーテンレールと間仕切りカーテン・
電動ベッド（整えられたシーツと毛布）・床頭台・点滴スタンド・ベッドサイドモニター・医療ガスの配管口・
ナースステーション・ホワイトボード・車椅子・非常口の誘導灯。

  Shell      部屋の原点。床・壁（東西に窓の開口）・腰壁・天井・照明・窓とブラインド・外の景色・掲示物
  Beds       部屋の原点。6床ぶん（ベッド・寝具・名札・床頭台・点滴スタンド・モニター・カーテン）と車椅子
  Nurse      ナースステーションの机（ユニットの原点、天板の上面 y=0.75、座る側 = -Z）
  Obs / GirlFile   資料の見た目（箱の中心が原点）
  Door / Breaker / Lever
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import hq
from hq import U, span, lathe, cyl, cyl_between, pipe, quad, profile_z, rrect_face, frame_ring, finish, join, pillow
import train_room
import dim_room
import lab_room

W, D, H = 9.0, 12.0, 3.2
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
WIN_Y0, WIN_Y1 = 1.0, 2.25
BED_X = HW0 - 1.6
BED_ZS = [-HD0 + 2.5 + i * 3.2 for i in range(3)]          # -3.5, -0.3, 2.9
LIGHT_ZS = (-4.0, 0.0, 4.0)
NURSE_X = 1.45                                             # ナースステーションの中心（z = -HD0 + 1.6）
# 名札：(sx, i) → アトラスのコマ（0: 301 佐々木, 1: 302 田中, 2: 303 滲み, 3: 空床）
CARD = {(1, 0): 0, (1, 1): 1, (1, 2): 2}


def windows():
    """[(sx, z0, z1)]。東の中央は配電盤（z=0、±0.25）と医療ガスの口を避けて細い窓"""
    out = []
    for sx in (-1, 1):
        for i, z in enumerate(BED_ZS):
            if sx > 0 and i == 1:
                out.append((sx, -1.2, -0.35))
            else:
                out.append((sx, z - 0.7, z + 0.7))
    return out


def mats():
    M = {}
    M["floor"] = hq.mat("WRD_Vinyl", (1, 1, 1), 0.5, tex="vinyl_floor.png")
    M["paint"] = hq.mat("WRD_Paint", (1, 1, 1), 0.8, tex="paint.png")
    M["wain"] = hq.mat("WRD_Wainscot", (1, 1, 1), 0.7, tex="wainscot.png")
    M["tile"] = hq.mat("WRD_CeilingTile", (1, 1, 1), 0.9, tex="../../lab/tex/ceiling_tile.png")
    M["grid"] = hq.mat("WRD_Grid", (0.93, 0.93, 0.93), 0.4)
    M["bumper"] = hq.mat("WRD_Bumper", (0.78, 0.76, 0.7), 0.5)
    M["cove"] = hq.mat("WRD_Cove", (0.42, 0.44, 0.42), 0.6)
    M["alu"] = hq.mat("WRD_Aluminum", (0.8, 0.81, 0.82), 0.3, 1.0)
    M["sus"] = hq.mat("WRD_Stainless", (0.82, 0.83, 0.85), 0.2, 1.0)
    M["chrome"] = hq.mat("WRD_Chrome", (0.9, 0.9, 0.9), 0.1, 1.0)
    M["ivory"] = hq.mat("WRD_Ivory", (0.9, 0.89, 0.84), 0.4)
    M["grey"] = hq.mat("WRD_GreyPlastic", (0.55, 0.57, 0.58), 0.45)
    M["black"] = hq.mat("WRD_Black", (0.03, 0.03, 0.035), 0.5)
    M["rubber"] = hq.mat("WRD_Rubber", (0.05, 0.05, 0.05), 0.8)
    M["laminate"] = hq.mat("WRD_Laminate", (1.15, 1.1, 1.05), 0.45, tex="../../lab/tex/oak.png")
    M["sheet"] = hq.mat("WRD_Sheet", (1, 1, 1), 0.9, tex="sheet.png")
    M["blanket"] = hq.mat("WRD_Blanket", (1, 1, 1), 0.9, tex="blanket.png")
    M["curtain"] = hq.mat("WRD_Curtain", (1, 1, 1), 0.9, tex="curtain.png")
    M["net"] = hq.mat("WRD_CurtainNet", (1, 1, 1), 0.9, tex="curtain_net.png", tex_alpha=True)
    M["mattress"] = hq.mat("WRD_Mattress", (0.55, 0.66, 0.74), 0.5)
    M["sky"] = hq.mat("WRD_SkyDay", (1, 1, 1), 0.9, tex="sky_day.png", emit=(0.9, 0.95, 1.0), emit_strength=1.0)
    M["glass"] = hq.mat("WRD_WindowGlass", (0.7, 0.75, 0.78), 0.05, alpha=0.12)
    M["blind"] = hq.mat("WRD_Blind", (0.88, 0.88, 0.86), 0.4, 0.3)
    M["cord"] = hq.mat("WRD_Cord", (0.85, 0.85, 0.82), 0.8)
    M["led"] = hq.mat("WRD_LedPanel", (0.95, 0.97, 1.0), 0.3, emit=(0.95, 0.97, 1.0), emit_strength=5.0)
    M["vitals"] = hq.mat("WRD_ScreenVitals", (1, 1, 1), 0.2, tex="screen_vitals.png", emit=(0.4, 1.0, 0.6), emit_strength=0.8)
    M["chart"] = hq.mat("WRD_ScreenChart", (1, 1, 1), 0.2, tex="screen_chart.png", emit=(0.9, 0.95, 1.0), emit_strength=0.8)
    M["cards"] = hq.mat("WRD_BedCards", (1, 1, 1), 0.5, tex="bed_cards.png")
    M["wb"] = hq.mat("WRD_Whiteboard", (1, 1, 1), 0.1, tex="whiteboard.png")
    M["obs"] = hq.mat("WRD_ObsSheet", (1, 1, 1), 0.9, tex="obs_sheet.png")
    M["girl"] = hq.mat("WRD_GirlFile", (1, 1, 1), 0.9, tex="girl_file.png")
    M["sign"] = hq.mat("WRD_SignNurse", (1, 1, 1), 0.5, tex="sign_nurse.png")
    M["poster"] = hq.mat("WRD_PosterVisit", (1, 1, 1), 0.6, tex="poster_visit.png")
    M["label"] = hq.mat("WRD_LabelSanitizer", (1, 1, 1), 0.5, tex="label_sanitizer.png")
    M["exit"] = hq.mat("WRD_ExitSign", (0.1, 0.6, 0.3), 0.3, emit=(0.2, 1.0, 0.45), emit_strength=3.0)
    M["o2"] = hq.mat("WRD_O2Green", (0.1, 0.5, 0.25), 0.4)
    M["suction"] = hq.mat("WRD_SuctionYellow", (0.85, 0.7, 0.1), 0.4)
    M["callred"] = hq.mat("WRD_CallRed", (0.7, 0.1, 0.08), 0.4)
    M["ivbag"] = hq.mat("WRD_IvBag", (0.85, 0.9, 0.92), 0.05, alpha=0.5)
    M["seat"] = hq.mat("WRD_SeatBlue", (0.12, 0.2, 0.36), 0.5)
    M["towel"] = hq.mat("WRD_Towel", (0.9, 0.92, 0.94), 0.95)
    M["tissue"] = hq.mat("WRD_Tissue", (0.95, 0.95, 0.93), 0.9)
    M["tvscreen"] = hq.mat("WRD_TvScreen", (0.02, 0.025, 0.03), 0.05)
    M["paper"] = hq.mat("WRD_PaperPlain", (0.92, 0.91, 0.87), 0.9)
    M["clip"] = hq.mat("WRD_Clipboard", (0.55, 0.4, 0.25), 0.5)
    M["folder"] = hq.mat("WRD_Folder", (0.5, 0.62, 0.72), 0.6)
    M["clockface"] = hq.mat("WRD_ClockFace", (0.95, 0.95, 0.93), 0.3)
    M["lever"] = hq.mat("WRD_LeverRed", (0.75, 0.12, 0.1), 0.45)
    M["hazard"] = hq.mat("WRD_Hazard", (1, 1, 1), 0.5, tex="../../lab/tex/hazard.png")
    return M


# ============================== 外殻 ==============================

def shell(M):
    out = []
    out.append(finish(span("WS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["floor"], bev=0), 0.5, rot90=True))
    walls = []
    wins = windows()
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"WS_WallNS{zs}", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["paint"])
    for sx in (-1, 1):
        ops = [(z0, z1, WIN_Y0, WIN_Y1) for s, z0, z1 in wins if s == sx]
        walls += train_room.grid_wall(f"WS_WallEW{sx}", ops, -HD0, HD0, 0.0, H,
                                      lambda a, b, c, d, sx=sx: (*sorted((sx * HW, sx * HW0)), c, d, a, b), M["paint"])
    out += [finish(o, 1.0, angle=30) for o in walls]
    out += wainscot(M)
    out += ceiling(M)
    for sx, z0, z1 in wins:
        out += window(M, sx, z0, z1, raised=(sx > 0 and z0 > 2.0))
    out += backdrop(M)
    out += wall_items(M)
    return out


def wainscot(M):
    """腰壁（高さ 0.9 のミントの塩ビ壁紙）・バンパーレール・ソフト巾木"""
    p = []
    top = 0.9
    runs = []
    for zs in (-1, 1):
        runs += [("NS", zs, -HW, -DOOR_HALF - 0.06), ("NS", zs, DOOR_HALF + 0.06, HW)]
    for sx in (-1, 1):
        runs.append(("EW", sx, -HD, HD))
    for k, (kind, s, a, b) in enumerate(runs):
        if kind == "NS":
            p.append(span(f"WW_Wain{k}", a, b, 0.0, top, *sorted((s * HD, s * (HD - 0.004))), M["wain"], 0))
            p.append(span(f"WW_Cove{k}", a, b, 0.0, 0.08, *sorted((s * HD, s * (HD - 0.012))), M["cove"], 0.003))
            p.append(span(f"WW_BumpBase{k}", a, b, 0.84, 0.96, *sorted((s * HD, s * (HD - 0.015))), M["grey"], 0.004))
            p.append(span(f"WW_Bump{k}", a + 0.02, b - 0.02, 0.855, 0.945, *sorted((s * (HD - 0.015), s * (HD - 0.04))), M["bumper"], 0.03, 4))
        else:
            # 東の配電盤（z=±0.25、y 0.95〜）の前はバンパーを切る
            segs = [(a, b)] if s < 0 else [(a, -0.3), (0.3, b)]
            p.append(span(f"WW_Wain{k}", *sorted((s * HW, s * (HW - 0.004))), 0.0, top, a, b, M["wain"], 0))
            p.append(span(f"WW_Cove{k}", *sorted((s * HW, s * (HW - 0.012))), 0.0, 0.08, a, b, M["cove"], 0.003))
            for j, (c, e) in enumerate(segs):
                p.append(span(f"WW_BumpBase{k}{j}", *sorted((s * HW, s * (HW - 0.015))), 0.84, 0.96, c, e, M["grey"], 0.004))
                p.append(span(f"WW_Bump{k}{j}", *sorted((s * (HW - 0.015), s * (HW - 0.04))), 0.855, 0.945, c + 0.02, e - 0.02, M["bumper"], 0.03, 4))
    # 扉の枠（アルミ）
    for zs in (-1, 1):
        zin = zs * HD
        for xs in (-1, 1):
            p.append(span(f"WW_Frame{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.05))), 0, DOOR_H + 0.05,
                          *sorted((zin, zin - zs * 0.015)), M["alu"], 0.003))
            p.append(span(f"WW_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), M["alu"], 0.002))
        p.append(span(f"WW_FrameT{zs}", -DOOR_HALF - 0.05, DOOR_HALF + 0.05, DOOR_H, DOOR_H + 0.05, *sorted((zin, zin - zs * 0.015)), M["alu"], 0.003))
        p.append(span(f"WW_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), M["alu"], 0.002))
    return [finish(o, 1.0, angle=40) for o in p]


def ceiling(M):
    """600角の吸音板と T バー・埋込みの LED ベースライト3台・U字のカーテンレール・感知器・吹出口"""
    out = []
    ce = span("WC_Ceil", -HW0, HW0, H, H + 0.1, -HD0, HD0, M["tile"], bev=0)
    finish(ce, 1 / 0.6)
    out.append(ce)
    g = []
    x = -HW + 0.6
    while x < HW - 0.1:
        g.append(span(f"WC_GridX{x:.1f}", x - 0.012, x + 0.012, H - 0.012, H, -HD, HD, M["grid"], 0.002)); x += 0.6
    z = -HD + 0.6
    while z < HD - 0.1:
        g.append(span(f"WC_GridZ{z:.1f}", -HW, HW, H - 0.012, H, z - 0.012, z + 0.012, M["grid"], 0.002)); z += 0.6
    # 埋込みベースライト（1.2 x 0.6、乳白の拡散板と白い枠）
    for i, zc in enumerate(LIGHT_ZS):
        g.append(span(f"WC_LightFrame{i}", -0.62, 0.62, H - 0.02, H - 0.012, zc - 0.32, zc + 0.32, M["ivory"], 0.004))
        g.append(span(f"WC_LightPanel{i}", -0.58, 0.58, H - 0.024, H - 0.019, zc - 0.28, zc + 0.28, M["led"], 0.002))
    # 感知器・スプリンクラー・角形の吹出口
    for (x, z) in ((-1.8, -2.0), (1.8, 2.0), (0.0, -5.0), (0.0, 5.0)):
        g.append(lathe(f"WC_Det{x}{z}", (x, H - 0.05, z), [(0.0, 0.0), (0.04, 0.0), (0.05, 0.02), (0.055, 0.05)], M["ivory"], 24))
    for (x, z) in ((-1.2, -2.0), (1.2, -2.0), (-1.2, 2.0), (1.2, 2.0)):
        g.append(cyl(f"WC_Spr{x}{z}", (x, H - 0.04, z), 0.04, 0.012, M["chrome"], 12))
        g.append(cyl(f"WC_SprRose{x}{z}", (x, H - 0.006, z), 0.006, 0.035, M["chrome"], 16))
    for (x, z) in ((-1.8, -4.4), (1.8, 4.4)):
        for k in range(4):
            s = 0.29 - k * 0.06
            g.append(span(f"WC_Diff{x}{z}{k}", x - s, x + s, H - 0.012 - k * 0.012, H - k * 0.012, z - s, z + s, M["ivory"], 0.003))
    # U字のカーテンレール（ベッドの通路側に沿って、両端は窓側の壁まで）
    for sx in (-1, 1):
        for bz in BED_ZS:
            xa = sx * (BED_X - 0.75)
            xw = sx * (HW - 0.12)
            pts = [(xw, H - 0.04, bz - 1.3), (xa, H - 0.04, bz - 1.3), (xa, H - 0.04, bz + 1.3), (xw, H - 0.04, bz + 1.3)]
            g.append(pipe(f"WC_Track{sx}{bz}", pts, 0.012, M["alu"], 0.18, 8))
            for zz in (bz - 1.3, bz, bz + 1.3):
                g.append(cyl(f"WC_TrackHang{sx}{bz}{zz}", (xa, H - 0.04, zz), 0.04, 0.006, M["alu"], 8))
    out += [finish(o, 2.0, angle=40) for o in g]
    return out


def window(M, sx, z0, z1, raised=False):
    """アルミサッシ（引き違い）・窓台・ガラス・ベネチアンブラインド"""
    p, glass = [], []
    y0, y1 = WIN_Y0, WIN_Y1
    xi, xo = sx * HW, sx * HW0
    xa, xb = sorted((xi, xo))
    p.append(span("WN_RevealB", xa, xb, y0 - 0.02, y0, z0, z1, M["ivory"], 0.002))
    p.append(span("WN_RevealT", xa, xb, y1, y1 + 0.02, z0, z1, M["ivory"], 0.002))
    for zz in (z0 - 0.02, z1):
        p.append(span("WN_RevealS", xa, xb, y0 - 0.02, y1 + 0.02, zz, zz + 0.02, M["ivory"], 0.002))
    p.append(span("WN_Sill", *sorted((xi, xi - sx * 0.1)), y0 - 0.03, y0, z0 - 0.05, z1 + 0.05, M["ivory"], 0.006, 3))
    # サッシ：外枠と、左右2枚の障子（引き違いで前後にずれる）
    xs = sx * (HW + 0.045)
    for (a0, a1, b0, b1) in ((z0, z1, y0, y0 + 0.04), (z0, z1, y1 - 0.04, y1), (z0, z0 + 0.04, y0, y1), (z1 - 0.04, z1, y0, y1)):
        p.append(span("WN_Frame", *sorted((xs - 0.025, xs + 0.025)), b0, b1, a0, a1, M["alu"], 0.003))
    zm = (z0 + z1) / 2
    for k, (c0, c1, off) in enumerate(((z0 + 0.04, zm + 0.02, -0.01), (zm - 0.02, z1 - 0.04, 0.01))):
        xx = xs + sx * off
        for (a0, a1, b0, b1) in ((c0, c1, y0 + 0.04, y0 + 0.075), (c0, c1, y1 - 0.075, y1 - 0.04), (c0, c0 + 0.035, y0 + 0.04, y1 - 0.04), (c1 - 0.035, c1, y0 + 0.04, y1 - 0.04)):
            p.append(span("WN_Sash", *sorted((xx - 0.012, xx + 0.012)), b0, b1, a0, a1, M["alu"], 0.003))
        g = quad(f"WN_Glass{k}", [(xx, y0 + 0.075, c0 + 0.035), (xx, y0 + 0.075, c1 - 0.035), (xx, y1 - 0.075, c1 - 0.035), (xx, y1 - 0.075, c0 + 0.035)], M["glass"])
        hq.face_toward(g, (-sx, 0, 0)); glass.append(g)
    p.append(span("WN_Latch", *sorted((xs - sx * 0.03, xs - sx * 0.015)), (y0 + y1) / 2 - 0.03, (y0 + y1) / 2 + 0.03, zm - 0.02, zm + 0.02, M["chrome"], 0.004))
    # ブラインド（ヘッドボックス・羽根・昇降コード・ボトムレール）
    bx = sx * (HW - 0.05)
    p.append(span("WN_BlindHead", *sorted((bx - 0.025, bx + 0.025)), y1 - 0.05, y1, z0 - 0.02, z1 + 0.02, M["blind"], 0.006))
    drop = 0.12 if raised else 0.72
    ys = y1 - 0.06
    n = 0
    slats = []
    tilt = 0.55
    hw_ = 0.0125
    while ys > y1 - 0.06 - drop:
        if raised:
            prof = [(bx - hw_, ys - 0.0015), (bx + hw_, ys - 0.0015), (bx + hw_, ys + 0.0015), (bx - hw_, ys + 0.0015)]
        else:
            c, s = math.cos(tilt) * sx, math.sin(tilt)
            prof = [(bx - hw_ * c, ys - hw_ * s - 0.001), (bx + hw_ * c, ys + hw_ * s - 0.001), (bx + hw_ * c, ys + hw_ * s + 0.001), (bx - hw_ * c, ys - hw_ * s + 0.001)]
        slats.append(profile_z(f"WN_Slat{n}", prof, z0 + 0.01, z1 - 0.01, M["blind"]))
        ys -= 0.006 if raised else 0.022
        n += 1
    bot = ys - 0.01
    p.append(span("WN_BlindBottom", *sorted((bx - 0.016, bx + 0.016)), bot - 0.012, bot, z0 + 0.01, z1 - 0.01, M["blind"], 0.004))
    for zz in (z0 + 0.15, zm, z1 - 0.15):
        p.append(cyl_between(f"WN_Ladder{zz:.2f}", (bx, y1 - 0.05, zz), (bx, bot, zz), 0.0015, M["cord"], 6))
    p.append(cyl_between("WN_Pull", (bx - sx * 0.015, y1 - 0.05, z1 - 0.06), (bx - sx * 0.015, y0 + 0.25, z1 - 0.06), 0.002, M["cord"], 6))
    p.append(lathe("WN_Tassel", (bx - sx * 0.015, y0 + 0.2, z1 - 0.06), [(0.0, 0.0), (0.008, 0.01), (0.006, 0.05), (0.002, 0.055)], M["ivory"], 12))
    sl = join(slats, f"WN_Slats{sx}{z0:.1f}")
    out = [finish(o, 2.0, angle=40) for o in p] + [finish(sl, 2.0, angle=20)]
    return out + glass


def backdrop(M):
    """窓の外（東西とも 4m 外に 20m x 6.25m の背景板）"""
    out = []
    for sx in (-1, 1):
        xk = sx * (HW0 + 4.0)
        zl = 10.0 * sx          # 見る人の左（東の壁は +Z、西の壁は -Z）
        q = quad(f"WB_Sky{sx}", [(xk, -1.0, zl), (xk, -1.0, -zl), (xk, 5.25, -zl), (xk, 5.25, zl)], M["sky"])
        hq.face_toward(q, (-sx, 0, 0))
        out.append(q)
    return out


def wall_items(M):
    """医療ガスの配管口・非常口の誘導灯・時計・掲示・手指消毒・ホワイトボード・ナースステーションの吊り看板"""
    p, flat = [], []
    # 医療ガス（酸素・吸引）とナースコールの壁パネル：各ベッドの頭側の外壁
    for sx in (-1, 1):
        for bz in BED_ZS:
            zc = bz - 1.05
            xw = sx * HW
            xf = xw - sx * 0.02
            p.append(span("WG_Panel", *sorted((xw, xf)), 1.28, 1.56, zc - 0.13, zc + 0.13, M["ivory"], 0.006))
            for k, (dz, m) in enumerate(((-0.07, M["o2"]), (0.0, M["suction"]))):
                p.append(cyl_between(f"WG_Out{k}", (xf, 1.45, zc + dz), (xf - sx * 0.02, 1.45, zc + dz), 0.022, m, 20))
                p.append(cyl_between(f"WG_OutC{k}", (xf - sx * 0.02, 1.45, zc + dz), (xf - sx * 0.028, 1.45, zc + dz), 0.012, M["chrome"], 16))
            p.append(span("WG_Call", *sorted((xf, xf - sx * 0.012)), 1.33, 1.37, zc + 0.05, zc + 0.1, M["callred"], 0.004))
            # ナースコールのコード（壁から垂れて輪になる）
            p.append(pipe("WG_CallCord", [(xf - sx * 0.01, 1.35, zc + 0.075), (xf - sx * 0.03, 1.0, zc + 0.12), (xf - sx * 0.05, 0.8, zc + 0.2)], 0.004, M["grey"], 0.06, 6))
    # 非常口の誘導灯（北の扉の上）
    zN = HD
    p.append(span("WE_ExitBox", -0.22, 0.22, 2.3, 2.5, zN - 0.05, zN, M["ivory"], 0.004))
    ex = quad("WE_Exit", [(0.2, 2.31, zN - 0.052), (-0.2, 2.31, zN - 0.052), (-0.2, 2.49, zN - 0.052), (0.2, 2.49, zN - 0.052)], M["exit"])
    hq.face_toward(ex, (0, 0, -1)); flat.append(ex)
    # 時計（南の壁、扉の西）
    cx, cy, zS = -1.6, 2.35, -HD
    p.append(cyl_between("WK_Rim", (cx, cy, zS), (cx, cy, zS + 0.05), 0.17, M["grey"], 48))
    p.append(cyl_between("WK_Face", (cx, cy, zS + 0.05), (cx, cy, zS + 0.052), 0.15, M["clockface"], 48))
    for k in range(12):
        a = math.pi * 2 * k / 12
        x, y = cx + math.sin(a) * 0.125, cy + math.cos(a) * 0.125
        p.append(span(f"WK_Tick{k}", x - 0.004, x + 0.004, y - 0.012, y + 0.012, zS + 0.052, zS + 0.054, M["black"], 0))
    p.append(cyl_between("WK_H", (cx, cy, zS + 0.056), (cx - 0.05, cy - 0.04, zS + 0.056), 0.004, M["black"], 6))
    p.append(cyl_between("WK_M", (cx, cy, zS + 0.058), (cx + 0.02, cy + 0.11, zS + 0.058), 0.003, M["black"], 6))
    # 面会のご案内（北の壁、扉の東）と手指消毒（両扉の脇）
    zp = HD - 0.004
    po = quad("WP_Poster", [(1.1, 1.2, zp), (1.55, 1.2, zp), (1.55, 1.8, zp), (1.1, 1.8, zp)], M["poster"])
    hq.face_toward(po, (0, 0, -1)); flat.append(po)
    for (x, zs) in ((-0.85, 1), (0.85, -1)):
        zw = zs * HD
        zf = zw - zs * 0.1
        p.append(span("WH_Body", x - 0.06, x + 0.06, 1.05, 1.3, *sorted((zw, zf)), M["ivory"], 0.015, 3))
        p.append(span("WH_Push", x - 0.035, x + 0.035, 1.12, 1.2, *sorted((zf, zf - zs * 0.015)), M["grey"], 0.008))
        p.append(span("WH_Nozzle", x - 0.008, x + 0.008, 1.03, 1.06, *sorted((zf - zs * 0.02, zf - zs * 0.05)), M["grey"], 0.003))
        lb = quad("WH_Label", [(x - 0.05 * zs, 1.22, zf - zs * 0.001), (x + 0.05 * zs, 1.22, zf - zs * 0.001), (x + 0.05 * zs, 1.28, zf - zs * 0.001), (x - 0.05 * zs, 1.28, zf - zs * 0.001)], M["label"])
        hq.face_toward(lb, (0, 0, -zs)); flat.append(lb)
    # ホワイトボード（南の壁、ナースステーションの後ろ）
    zb = -HD
    x0, x1, y0, y1 = 1.05, 2.75, 1.1, 2.05
    p.append(span("WW_WbFrame", x0 - 0.03, x1 + 0.03, y0 - 0.03, y1 + 0.03, zb, zb + 0.02, M["alu"], 0.004))
    p.append(span("WW_WbTray", x0 + 0.1, x1 - 0.1, y0 - 0.06, y0 - 0.03, zb, zb + 0.07, M["alu"], 0.004))
    for k, (mx, c) in enumerate(((1.4, M["black"]), (1.5, M["callred"]), (1.6, M["o2"]))):
        p.append(cyl_between(f"WW_Marker{k}", (mx, y0 - 0.02, zb + 0.04), (mx + 0.12, y0 - 0.02, zb + 0.04), 0.009, c, 10))
    wb = quad("WW_Wb", [(x1, y0, zb + 0.021), (x0, y0, zb + 0.021), (x0, y1, zb + 0.021), (x1, y1, zb + 0.021)], M["wb"])
    hq.face_toward(wb, (0, 0, 1)); flat.append(wb)
    # ナースステーションの吊り看板（ステーションの上、両面）
    sxc, szc = NURSE_X, -HD0 + 1.2
    for xx in (sxc - 0.45, sxc + 0.45):
        p.append(cyl_between(f"WS_SignWire{xx}", (xx, H, szc), (xx, 2.52, szc), 0.0015, M["chrome"], 6))
    p.append(span("WS_SignBoard", sxc - 0.52, sxc + 0.52, 2.3, 2.52, szc - 0.012, szc + 0.012, M["ivory"], 0.004))
    for zs in (-1, 1):
        zf = szc + zs * 0.0125
        a, b = (sxc - 0.5, sxc + 0.5) if zs < 0 else (sxc + 0.5, sxc - 0.5)
        s = quad(f"WS_Sign{zs}", [(a, 2.31, zf), (b, 2.31, zf), (b, 2.51, zf), (a, 2.51, zf)], M["sign"])
        hq.face_toward(s, (0, 0, zs)); flat.append(s)
    return [finish(o, 2.0, angle=40) for o in p] + flat


# ============================== ベッドまわり ==============================

def bed(M):
    """電動ベッド（原点、頭側 = -Z）：キャスター付きの台枠・ボード・サイドレール・マットレス・寝具"""
    p = []
    # 台枠とキャスター
    p.append(span("BF_Base", -0.42, 0.42, 0.14, 0.2, -0.95, 0.95, M["grey"], 0.01))
    for sx in (-1, 1):
        p.append(span(f"BF_Rail{sx}", sx * 0.46 - 0.025, sx * 0.46 + 0.025, 0.46, 0.53, -1.0, 1.0, M["ivory"], 0.012))
        for sz in (-1, 1):
            cx, cz = sx * 0.4, sz * 0.9
            p.append(span(f"BF_Leg{sx}{sz}", cx - 0.025, cx + 0.025, 0.14, 0.47, cz - 0.025, cz + 0.025, M["grey"], 0.006))
            p.append(cyl(f"BF_CastStem{sx}{sz}", (cx, 0.09, cz), 0.05, 0.012, M["chrome"], 12))
            p.append(span(f"BF_CastFork{sx}{sz}", cx - 0.03, cx + 0.03, 0.06, 0.1, cz - 0.03, cz + 0.03, M["grey"], 0.008))
            p.append(cyl_between(f"BF_Wheel{sx}{sz}", (cx - 0.018, 0.05, cz + 0.02), (cx + 0.018, 0.05, cz + 0.02), 0.05, M["rubber"], 20))
    # ボード（木目の化粧板に灰色の縁）
    for zs, top in ((-1, 0.98), (1, 0.86)):
        z = zs * 1.02
        p.append(span(f"BF_Board{zs}", -0.47, 0.47, 0.42, top, z - 0.02, z + 0.02, M["laminate"], 0.02, 4))
        p.append(span(f"BF_BoardEdge{zs}", -0.5, 0.5, top - 0.035, top + 0.01, z - 0.03, z + 0.03, M["grey"], 0.015, 4))
        for sx in (-1, 1):
            p.append(span(f"BF_BoardPost{zs}{sx}", sx * 0.5 - 0.03, sx * 0.5 + 0.03, 0.42, top + 0.01, z - 0.03, z + 0.03, M["grey"], 0.012))
    # 床板（マットレスの下の灰色のデッキ）とマットレス
    p.append(span("BF_Deck", -0.44, 0.44, 0.53, 0.56, -0.98, 0.98, M["grey"], 0.004))
    p.append(span("BF_Mattress", -0.45, 0.45, 0.56, 0.7, -0.97, 0.97, M["mattress"], 0.04, 4))
    # サイドレール（頭側の半分、両側。パイプの枠と縦桟）
    for sx in (-1, 1):
        x = sx * 0.49
        pts = [(x, 0.53, -0.85), (x, 0.95, -0.85), (x, 0.95, -0.05), (x, 0.53, -0.05)]
        p.append(pipe(f"BF_SideRail{sx}", pts, 0.012, M["ivory"], 0.06, 10))
        p.append(pipe(f"BF_SideRailMid{sx}", [(x, 0.8, -0.85), (x, 0.8, -0.05)], 0.009, M["ivory"], 0.02, 8))
    # 操作リモコン（サイドレールに掛ける）
    p.append(span("BF_Remote", 0.505, 0.525, 0.78, 0.9, -0.5, -0.44, M["ivory"], 0.006))
    p.append(pipe("BF_RemoteCord", [(0.515, 0.78, -0.47), (0.52, 0.62, -0.5), (0.47, 0.55, -0.6)], 0.003, M["grey"], 0.04, 6))
    out = [finish(o, 1.0, angle=40) for o in p]
    out += bedding(M)
    return out


def bedding(M):
    """整えられたシーツ・毛布（上端を折り返す）・枕"""
    out = []
    # シーツ（マットレスを包み、側面に垂れる）
    def sheet_fn(u, v):
        x = (u - 0.5) * 1.0
        z = -0.98 + v * 1.96
        ax = abs(x)
        if ax < 0.43:
            y = 0.702
        else:
            t = min(1.0, (ax - 0.43) / 0.07)
            y = 0.702 - 0.14 * t ** 1.3
            x = math.copysign(0.43 + 0.03 * math.sin(t * math.pi / 2) + 0.004, x)
        return (x, y, z)
    sh = hq.grid_surface("BS_Sheet", 30, 40, sheet_fn, M["sheet"])
    out.append(_uv_plane(sh, 2.0))
    # 毛布：z -0.35〜0.98、上端 0.12 を折り返す。両側は垂れる
    def blanket_fn(u, v, lift=0.0, z0=-0.35, z1=0.98):
        x = (u - 0.5) * 1.04
        z = z0 + v * (z1 - z0)
        ax = abs(x)
        wav = 0.004 * math.sin(v * 17 + u * 5)
        if ax < 0.44:
            y = 0.725 + lift + wav * 0.3
        else:
            t = min(1.0, (ax - 0.44) / 0.08)
            y = 0.725 + lift - 0.2 * t ** 1.2 + wav
            x = math.copysign(0.44 + 0.035 * math.sin(t * math.pi / 2) + 0.01, x)
        return (x, y, z)
    bl = hq.grid_surface("BS_Blanket", 30, 36, blanket_fn, M["blanket"])
    out.append(_uv_plane(bl, 2.0, thick=0.012))
    fold = hq.grid_surface("BS_Fold", 30, 4, lambda u, v: blanket_fn(u, v, 0.014, -0.35, -0.2), M["sheet"])
    out.append(_uv_plane(fold, 2.0, thick=0.008))
    pw = pillow("BS_Pillow", (0.0, 0.76, -0.78), (0.6, 0.12, 0.34), M["sheet"], seed=5)
    out.append(_uv_plane(pw, 2.0))
    return out


def _uv_plane(o, scale, thick=0.004):
    """布の面：上から見た位置でUV（実寸）、必要なら厚み"""
    me = o.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uvl = me.uv_layers[0].data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = (co.x * scale, co.y * scale + co.z * scale * 0.5)
    if thick > 0:
        s = o.modifiers.new("Solid", "SOLIDIFY"); s.thickness = thick; s.offset = 1.0
    finish(o, keep_uv=True, angle=60)
    _outward(o)
    return o


def _outward(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()


def cabinet(M, x, z, sx, seed):
    """床頭台（小さなテレビ・引き出し・物入れ・タオル掛け）。前 = 通路側（-sx）"""
    rnd = random.Random(seed)
    p = []
    w, d = 0.45, 0.45
    xf = x - sx * d / 2                     # 前面
    xa, xb = sorted((x - d / 2, x + d / 2))
    p.append(span("BC_Body", xa, xb, 0.06, 0.8, z - w / 2, z + w / 2, M["ivory"], 0.01))
    p.append(span("BC_Top", xa - 0.01, xb + 0.01, 0.8, 0.825, z - w / 2 - 0.01, z + w / 2 + 0.01, M["laminate"], 0.008))
    p.append(span("BC_Plinth", xa + 0.02, xb - 0.02, 0.0, 0.06, z - w / 2 + 0.02, z + w / 2 - 0.02, M["grey"], 0.004))
    for (y0, y1, nm) in ((0.62, 0.77, "Drawer"), (0.1, 0.58, "Door")):
        p.append(span(f"BC_{nm}", *sorted((xf, xf - sx * 0.012)), y0, y1, z - w / 2 + 0.015, z + w / 2 - 0.015, M["laminate"], 0.006))
        p.append(span(f"BC_{nm}Pull", *sorted((xf - sx * 0.012, xf - sx * 0.03)), y1 - 0.04 if nm == "Drawer" else 0.48, (y1 - 0.02) if nm == "Drawer" else 0.54,
                      z - 0.06, z + 0.06, M["grey"], 0.005))
    p.append(pipe("BC_TowelBar", [(xf - sx * 0.01, 0.7, z + w / 2), (xf - sx * 0.01, 0.7, z + w / 2 + 0.04), (xa + d / 2, 0.7, z + w / 2 + 0.04), (x + sx * 0.18, 0.7, z + w / 2)], 0.006, M["chrome"], 0.02, 8))
    # テレビ（台に載った小さな液晶。画面は消えている）
    tz = z - 0.05
    p.append(span("BC_TvStand", x - 0.05, x + 0.05, 0.825, 0.835, tz - 0.08, tz + 0.08, M["black"], 0.004))
    p.append(span("BC_TvNeck", x - 0.01, x + 0.01, 0.835, 0.88, tz - 0.02, tz + 0.02, M["black"], 0.003))
    p.append(span("BC_Tv", *sorted((x - sx * 0.01, x + sx * 0.025)), 0.87, 1.08, tz - 0.17, tz + 0.17, M["black"], 0.008))
    scr = quad("BC_TvScreen", [(x - sx * 0.0105, 0.885, tz + 0.16), (x - sx * 0.0105, 0.885, tz - 0.16), (x - sx * 0.0105, 1.065, tz - 0.16), (x - sx * 0.0105, 1.065, tz + 0.16)], M["tvscreen"])
    hq.face_toward(scr, (-sx, 0, 0))
    # ティッシュの箱と吸い飲み
    kx = x - sx * 0.12
    p.append(span("BC_Tissue", kx - 0.06, kx + 0.06, 0.825, 0.9, z + 0.1, z + 0.22, M["tissue"], 0.01))
    p.append(pillow("BC_TissuePull", (kx, 0.91, z + 0.16), (0.04, 0.02, 0.06), M["tissue"], seed=seed))
    if rnd.random() < 0.6:
        p.append(lathe("BC_Cup", (x + sx * 0.12, 0.825, z + 0.15), [(0.0, 0.0), (0.035, 0.0), (0.04, 0.09), (0.036, 0.09), (0.0, 0.004)], M["ivory"], 20, cap_top=False))
    # たたんだタオル（タオル掛けに）
    tw = span("BC_Towel", x - 0.11, x + 0.11, 0.5, 0.71, z + w / 2 + 0.025, z + w / 2 + 0.055, M["towel"], 0.012, 3)
    p.append(tw)
    out = [finish(o, 2.0, angle=40) for o in p]
    return out + [scr]


def iv_stand(M, x, z, bag, seed):
    """点滴スタンド（5本脚のキャスター・ポール・フック）と、空になりかけた点滴バッグ"""
    p = []
    for k in range(5):
        a = math.pi * 2 * k / 5 + seed
        tip = (x + 0.28 * math.cos(a), 0.06, z + 0.28 * math.sin(a))
        p.append(pipe(f"BI_Leg{k}", [(x, 0.1, z), (x + 0.1 * math.cos(a), 0.08, z + 0.1 * math.sin(a)), tip], 0.012, M["chrome"], 0.05, 8))
        p.append(cyl_between(f"BI_Wheel{k}", (tip[0] - 0.012, 0.025, tip[2]), (tip[0] + 0.012, 0.025, tip[2]), 0.025, M["rubber"], 12))
    p.append(cyl("BI_Pole", (x, 0.08, z), 1.9, 0.012, M["chrome"], 12))
    p.append(lathe("BI_Knob", (x, 1.1, z), [(0.0, 0.0), (0.025, 0.005), (0.025, 0.03), (0.0, 0.035)], M["grey"], 16))
    for k in range(4):
        a = math.pi / 2 * k + 0.4
        tip = (x + 0.12 * math.cos(a), 1.98, z + 0.12 * math.sin(a))
        p.append(pipe(f"BI_Hook{k}", [(x, 1.95, z), (x + 0.1 * math.cos(a), 1.96, z + 0.1 * math.sin(a)), tip, (tip[0], 1.94, tip[2])], 0.004, M["chrome"], 0.02, 6))
    flat = []
    if bag:
        a = 0.4
        hx, hz = x + 0.12 * math.cos(a), z + 0.12 * math.sin(a)
        b = pillow("BI_Bag", (hx, 1.8, hz), (0.13, 0.03, 0.2), M["ivbag"], puff=0.7, seed=seed)
        b.data.transform(Matrix.Translation(U(hx, 1.8, hz)) @ Matrix.Rotation(math.radians(90), 4, "X") @ Matrix.Translation(-U(hx, 1.8, hz)))
        flat.append(b)
        p.append(pipe("BI_Tube", [(hx, 1.7, hz), (hx + 0.02, 1.3, hz + 0.05), (hx + 0.1, 0.9, hz + 0.12), (hx + 0.05, 0.75, hz + 0.3)], 0.003, M["ivbag"], 0.1, 6))
        p.append(cyl("BI_Chamber", (hx, 1.62, hz), 0.07, 0.012, M["ivbag"], 12))
    return [finish(o, 2.0, angle=40) for o in p] + [finish(o, 2.0, angle=60) for o in flat]


def monitor(M, x, z, sx):
    """ベッドサイドモニター（移動式のスタンド）。画面は通路側（-sx）"""
    p = []
    for k in range(5):
        a = math.pi * 2 * k / 5 + 0.3
        tip = (x + 0.25 * math.cos(a), 0.06, z + 0.25 * math.sin(a))
        p.append(pipe(f"BM_Leg{k}", [(x, 0.1, z), tip], 0.013, M["grey"], 0.05, 8))
        p.append(cyl_between(f"BM_Wheel{k}", (tip[0] - 0.012, 0.025, tip[2]), (tip[0] + 0.012, 0.025, tip[2]), 0.025, M["rubber"], 12))
    p.append(cyl("BM_Pole", (x, 0.08, z), 1.0, 0.018, M["grey"], 12))
    p.append(span("BM_Basket", x - 0.12, x + 0.12, 0.75, 0.8, z - 0.1, z + 0.1, M["grey"], 0.006))
    p.append(span("BM_Body", *sorted((x + sx * 0.08, x - sx * 0.1)), 1.08, 1.34, z - 0.16, z + 0.16, M["ivory"], 0.02, 3))
    p.append(span("BM_Bezel", *sorted((x - sx * 0.1, x - sx * 0.104)), 1.1, 1.32, z - 0.15, z + 0.15, M["black"], 0.004))
    p.append(span("BM_Handle", *sorted((x - sx * 0.02, x + sx * 0.02)), 1.34, 1.37, z - 0.08, z + 0.08, M["grey"], 0.008))
    xs = x - sx * 0.1045
    zl = z + 0.13 * sx               # 見る人の左（通路側から見る）
    scr = quad("BM_Screen", [(xs, 1.12, zl), (xs, 1.12, z - 0.13 * sx), (xs, 1.3, z - 0.13 * sx), (xs, 1.3, zl)], M["vitals"])
    hq.face_toward(scr, (-sx, 0, 0))
    p.append(pipe("BM_Cable", [(x + sx * 0.08, 1.15, z + 0.1), (x + sx * 0.2, 0.9, z + 0.25), (x + sx * 0.35, 0.72, z + 0.3)], 0.005, M["grey"], 0.08, 6))
    return [finish(o, 2.0, angle=40) for o in p] + [scr]


def curtains(M, sx, bz):
    """間仕切りカーテン：通路側を半分（z +0.2〜+1.3）、両端は窓側へ寄せて束ねる。上部はメッシュ"""
    out = []
    xa = sx * (BED_X - 0.75)
    xw = sx * (HW - 0.12)
    top, net_bot, bot = H - 0.06, H - 0.45, 0.35

    def sheet(name, path, pleats, amp):
        """path(u) → (x, z, 法線x, 法線z)。ひだのある布（下部＝布、上部＝メッシュ）"""
        def fab(u, v):
            x, z, nx, nz = path(u)
            w = amp * math.sin(u * math.pi * pleats)
            y = bot + v * (net_bot - bot)
            return (x + nx * w, y, z + nz * w)

        def net(u, v):
            x, z, nx, nz = path(u)
            w = amp * 0.6 * math.sin(u * math.pi * pleats)
            y = net_bot + v * (top - net_bot)
            return (x + nx * w, y, z + nz * w)
        res = []
        for nm, fn, mat, nv in ((name, fab, M["curtain"], 6), (name + "Net", net, M["net"], 2)):
            o = hq.grid_surface(nm, max(8, pleats * 6), nv, fn, mat)
            me = o.data
            uvl = me.uv_layers.new(name="UVMap").data
            L = 0.0
            for poly in me.polygons:
                for li in poly.loop_indices:
                    co = me.vertices[me.loops[li].vertex_index].co
                    uvl[li].uv = ((co.y - co.x) * 2, co.z * 2)
            sol = o.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.003
            finish(o, keep_uv=True, angle=180)
            _outward(o)
            res.append(o)
        return res

    # 通路側：z +0.2〜+1.3（半分だけ引かれている）
    z0, z1 = bz + 0.2, bz + 1.3
    out += sheet(f"BCu_A{sx}{bz}", lambda u: (xa, z0 + (z1 - z0) * u, 1.0, 0.0), 11, 0.035)
    # 両端：窓側の壁に寄せて束ねる（幅 0.3）
    for k, zz in enumerate((bz - 1.3, bz + 1.3)):
        xw0, xw1 = xw - sx * 0.3, xw
        out += sheet(f"BCu_E{k}{sx}{bz}", lambda u, zz=zz, xw0=xw0, xw1=xw1: (xw0 + (xw1 - xw0) * u, zz, 0.0, 1.0), 6, 0.04)
    return out


def bed_card(M, cx, cz, cell):
    """足元のボードの名札（+Z 面）"""
    cu, cv = (cell % 2) * 0.5, 1 - (cell // 2 + 1) * 0.5
    zf = cz + 1.02 + 0.021
    q = quad("BN_Card", [(cx + 0.12, 0.68, zf), (cx - 0.12, 0.68, zf), (cx - 0.12, 0.8, zf), (cx + 0.12, 0.8, zf)], M["cards"],
             uv=((cu, cv), (cu + 0.5, cv), (cu + 0.5, cv + 0.5), (cu, cv + 0.5)))
    hq.face_toward(q, (0, 0, 1))
    frame = span("BN_Holder", cx - 0.13, cx + 0.13, 0.67, 0.81, zf - 0.001, zf + 0.004, M["alu"], 0.003)
    # 枠の前面が名札を隠さないよう、枠は縁だけ（中央を名札より奥に）
    frame.data.transform(Matrix.Translation(U(0, 0, -0.004) - U(0, 0, 0)))
    return [finish(frame, 2.0), q]


def wheelchair(M, cx, cz, yaw):
    """車椅子（大きな後輪・ハンドリム・キャスター・パイプの枠・座面と背のシート・フットレスト）"""
    p = []
    wr, hr = 0.3, 0.27
    for sx in (-1, 1):
        x = sx * 0.3
        wc = (x, wr, 0.12)
        # 後輪：タイヤ（トーラス）・リム・スポーク・ハンドリム
        tire = lathe(f"WCh_Tire{sx}", (0, 0, 0), [(wr - 0.012 + 0.012 * math.cos(t / 8 * math.pi * 2), 0.012 * math.sin(t / 8 * math.pi * 2)) for t in range(9)],
                     M["rubber"], 48, cap_top=False, cap_bottom=False)
        rim = lathe(f"WCh_Rim{sx}", (0, 0, 0), [(wr - 0.035, -0.008), (wr - 0.02, -0.008), (wr - 0.02, 0.008), (wr - 0.035, 0.008)], M["alu"], 48)
        hand = lathe(f"WCh_HandRim{sx}", (0, -0.035 * sx, 0), [(hr + 0.008 * math.cos(t / 8 * math.pi * 2), 0.008 * math.sin(t / 8 * math.pi * 2)) for t in range(9)],
                     M["chrome"], 48, cap_top=False, cap_bottom=False)
        hub = cyl(f"WCh_Hub{sx}", (0, -0.03, 0), 0.06, 0.03, M["grey"], 16)
        parts = [tire, rim, hand, hub]
        for k in range(18):
            a = math.pi * 2 * k / 18
            parts.append(cyl_between(f"WCh_Spoke{k}", (0, -0.02 + 0.04 * (k % 2), 0), (math.cos(a) * (wr - 0.035), 0, math.sin(a) * (wr - 0.035)), 0.0015, M["chrome"], 4))
        for o in parts:
            # 回転体は Unity Y 軸回り → 車軸を Unity X 軸へ（Unity の z-y 面の回転 = Blender の -Y... X 軸回り）
            o.data.transform(Matrix.Translation(U(*wc)) @ Matrix.Rotation(math.radians(90), 4, "Y"))
            _outward(o)
            p.append(o)
        # キャスター
        cz_ = -0.38
        p.append(cyl(f"WCh_CastStem{sx}", (sx * 0.24, 0.1, cz_), 0.35, 0.01, M["chrome"], 10))
        p.append(span(f"WCh_CastFork{sx}", sx * 0.24 - 0.025, sx * 0.24 + 0.025, 0.07, 0.11, cz_ + 0.01, cz_ + 0.06, M["chrome"], 0.004))
        p.append(cyl_between(f"WCh_Caster{sx}", (sx * 0.24 - 0.015, 0.065, cz_ + 0.04), (sx * 0.24 + 0.015, 0.065, cz_ + 0.04), 0.065, M["rubber"], 20))
        # 枠：側面のパイプ・押し手・肘掛け・フットレスト
        fx = sx * 0.24
        p.append(pipe(f"WCh_Side{sx}", [(fx, 0.45, cz_), (fx, 0.47, 0.18), (fx, 0.95, 0.24)], 0.011, M["chrome"], 0.06, 8))
        p.append(pipe(f"WCh_Low{sx}", [(fx, 0.45, cz_), (fx, 0.12, cz_ - 0.1), (fx - sx * 0.02, 0.1, cz_ - 0.2)], 0.01, M["chrome"], 0.05, 8))
        p.append(pipe(f"WCh_Grip{sx}", [(fx, 0.95, 0.24), (fx, 0.95, 0.3)], 0.016, M["black"], 0.01, 10))
        p.append(pipe(f"WCh_ArmTube{sx}", [(fx, 0.47, -0.2), (fx, 0.66, -0.2), (fx, 0.68, 0.12), (fx, 0.47, 0.16)], 0.01, M["chrome"], 0.05, 8))
        p.append(span(f"WCh_ArmPad{sx}", fx - 0.025, fx + 0.025, 0.67, 0.7, -0.16, 0.1, M["black"], 0.01))
        p.append(span(f"WCh_Foot{sx}", *sorted((fx, fx - sx * 0.14)), 0.1, 0.115, cz_ - 0.26, cz_ - 0.12, M["black"], 0.005))
    for z in (-0.3, 0.12):
        p.append(cyl_between(f"WCh_Cross{z}", (-0.24, 0.45, z), (0.24, 0.45, z), 0.009, M["chrome"], 8))
    # 座面と背のシート（たわませる）
    seat = hq.grid_surface("WCh_Seat", 10, 8, lambda u, v: ((u - 0.5) * 0.46, 0.47 - 0.025 * math.sin(math.pi * u), -0.36 + v * 0.5), M["seat"])
    back = hq.grid_surface("WCh_Back", 10, 8, lambda u, v: ((u - 0.5) * 0.46, 0.52 + v * 0.38, 0.19 + v * 0.05 + 0.03 * math.sin(math.pi * u)), M["seat"])
    for o in (seat, back):
        s = o.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.006
    p += [seat, back]
    for o in p:
        finish(o, 2.0, angle=45)
    one = join(p, "WCh")
    one.data.transform(Matrix.Translation(U(cx, 0, cz)) @ Matrix.Rotation(math.radians(-yaw), 4, "Z"))
    return [one]


def beds(M):
    """6床ぶん（部屋の原点）と車椅子"""
    out = []
    proto = join(bed(M), "BedProto")
    for sx in (-1, 1):
        for i, bz in enumerate(BED_ZS):
            bx = sx * BED_X
            b = proto.copy(); b.data = proto.data.copy(); b.name = f"Bed{sx}{i}"
            bpy.context.scene.collection.objects.link(b)
            b.data.transform(Matrix.Translation(U(bx, 0, bz) - U(0, 0, 0)))
            out.append(b)
            out += bed_card(M, bx, bz, CARD.get((sx, i), 3))
            out += cabinet(M, bx + sx * 0.9, bz - 0.55, sx, 70 + i + (sx > 0) * 10)
            # 301号（東の手前）はナースステーションに近いので、点滴スタンドをベッドの中ほどへ
            ivz = bz + 0.1 if (sx > 0 and i == 0) else bz - 0.7
            ivx = bx - sx * (0.62 if (sx > 0 and i == 0) else 0.72)
            out += iv_stand(M, ivx, ivz, bag=(i + (sx > 0)) % 2 == 0, seed=i * 1.3 + sx)
            # モニターは頭側の窓寄り（通路側に置くと、ベッドの上の資料を隠す）
            out += monitor(M, bx + sx * 0.92, bz - 1.32, sx)
            out += curtains(M, sx, bz)
    bpy.data.objects.remove(proto, do_unlink=True)
    out += wheelchair(M, -1.7, HD0 - 0.75, 200)
    return out


# ============================== ナースステーション ==============================

def nurse(M):
    """ナースステーションの机（原点、天板 1.4 x 0.7・上面 0.75、座る側 = -Z、通路側 = +Z に受付の腰壁）
    （以前は幅1.6で x=1.8 にあり、301号のベッドと重なっていた → x=1.45、幅1.4）"""
    p = []
    p.append(span("NS_Top", -0.7, 0.7, 0.72, 0.75, -0.35, 0.35, M["laminate"], 0.008, 3))
    for sx in (-1, 1):
        p.append(span(f"NS_Ped{sx}", *sorted((sx * 0.32, sx * 0.7)), 0.0, 0.72, -0.33, 0.33, M["ivory"], 0.008))
        for k in range(3):
            y0 = 0.05 + k * 0.22
            xa, xb = (0.34, 0.68) if sx > 0 else (-0.68, -0.34)
            p.append(span(f"NS_Drw{sx}{k}", xa, xb, y0, y0 + 0.2, -0.345, -0.33, M["laminate"], 0.005))
            p.append(span(f"NS_Pull{sx}{k}", (xa + xb) / 2 - 0.06, (xa + xb) / 2 + 0.06, y0 + 0.15, y0 + 0.17, -0.36, -0.345, M["grey"], 0.004))
    # 受付の腰壁（+Z 側）とカウンター
    p.append(span("NS_Front", -0.75, 0.75, 0.0, 1.05, 0.35, 0.4, M["wain"], 0.006))
    p.append(span("NS_Counter", -0.78, 0.78, 1.05, 1.08, 0.3, 0.62, M["laminate"], 0.008, 3))
    p.append(span("NS_Kick", -0.75, 0.75, 0.0, 0.1, 0.4, 0.41, M["cove"], 0.003))
    # パソコン（画面は座る側 = -Z を向く）・キーボード・マウス
    p.append(span("NS_MonStand", 0.07, 0.27, 0.75, 0.76, 0.05, 0.2, M["black"], 0.004))
    p.append(span("NS_MonNeck", 0.15, 0.19, 0.76, 0.9, 0.13, 0.16, M["black"], 0.004))
    p.append(span("NS_Mon", -0.1, 0.44, 0.86, 1.18, 0.1, 0.13, M["black"], 0.008))
    scr = quad("NS_Screen", [(-0.08, 0.875, 0.099), (0.42, 0.875, 0.099), (0.42, 1.165, 0.099), (-0.08, 1.165, 0.099)], M["chart"])
    hq.face_toward(scr, (0, 0, -1))
    p.append(span("NS_Kb", -0.03, 0.39, 0.75, 0.765, -0.2, -0.06, M["ivory"], 0.005))
    p.append(span("NS_Mouse", 0.45, 0.5, 0.75, 0.77, -0.15, -0.08, M["ivory"], 0.012))
    # 電話・ペン立て・ファイル立て
    p.append(span("NS_Phone", -0.24, -0.04, 0.75, 0.79, 0.12, 0.32, M["ivory"], 0.015))
    p.append(span("NS_Handset", -0.23, -0.19, 0.79, 0.82, 0.13, 0.31, M["ivory"], 0.012))
    p.append(lathe("NS_PenCup", (0.6, 0.75, -0.25), [(0.0, 0.0), (0.035, 0.0), (0.035, 0.1), (0.032, 0.1), (0.0, 0.005)], M["grey"], 16, cap_top=False))
    for k in range(4):
        a = k * 1.3
        p.append(cyl_between(f"NS_Pen{k}", (0.6 + 0.01 * math.cos(a), 0.78, -0.25 + 0.01 * math.sin(a)), (0.6 + 0.03 * math.cos(a), 0.9, -0.25 + 0.03 * math.sin(a)), 0.004,
                             [M["black"], M["callred"], M["o2"], M["black"]][k], 6))
    for k in range(5):
        x = 0.47 + k * 0.045
        p.append(span(f"NS_Binder{k}", x, x + 0.04, 0.75, 1.05, 0.02, 0.3, [M["folder"], M["o2"], M["folder"], M["callred"], M["folder"]][k], 0.004))
    for o in p:
        finish(o, 1.0, angle=40)
    return [join(p + [scr], "Ward_Nurse")]


# ============================== 資料 ==============================

def _sheet(name, w, d, y, mat, uv=((0, 0), (1, 0), (1, 1), (0, 1))):
    q = quad(name, [(-w / 2, y, -d / 2), (w / 2, y, -d / 2), (w / 2, y, d / 2), (-w / 2, y, d / 2)], mat, uv=uv)
    hq.face_toward(q, (0, 1, 0))
    return q


def obs(M):
    """クリップボード（板・金具・観察記録の紙）"""
    p = [span("OB_Board", -0.12, 0.12, -0.01, -0.004, -0.165, 0.165, M["clip"], 0.004)]
    p.append(span("OB_Clip", -0.05, 0.05, -0.004, 0.008, 0.12, 0.16, M["chrome"], 0.004))
    p.append(cyl_between("OB_Roll", (-0.045, 0.008, 0.14), (0.045, 0.008, 0.14), 0.006, M["chrome"], 10))
    for o in p:
        finish(o, 2.0, angle=40)
    s = _sheet("OB_Sheet", 0.21, 0.297, -0.0035, M["obs"])
    s.data.transform(Matrix.Translation(U(0, 0, -0.01) - U(0, 0, 0)))
    one = join(p + [s], "Ward_Obs")
    one.data.transform(Matrix.Rotation(math.radians(-6), 4, "Z"))
    return [one]


def girl_file(M):
    """開いた水色のファイル（左頁：滲んだ写真、右頁：特記事項）"""
    p = [span("GF_Cover", -0.155, 0.155, -0.01, -0.007, -0.11, 0.11, M["folder"], 0.002)]
    s = _sheet("GF_Pages", 0.296, 0.208, -0.006, M["girl"])
    one = join([finish(p[0], 2.0), s], "Ward_GirlFile")
    # 通路側（西）から読める向き：文字の上 = +X（Unity の yaw +90° に近い）
    one.data.transform(Matrix.Rotation(math.radians(-90 + 8), 4, "Z"))
    return [one]


# ============================== 扉・配電盤 ==============================

def door(M):
    objs = lab_room.door({"oak": M["laminate"], "alu": M["alu"], "glass": M["glass"], "sus": M["sus"]})
    return [join(objs, "Ward_Door")]


def _breaker_mats(M):
    return {"mel": M["ivory"], "grille": M["black"], "hazard": M["hazard"], "sus": M["grey"], "rubber": M["rubber"], "lever": M["lever"]}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.01), "Ward_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "Ward_Lever")]


PIECES = {"Shell": shell, "Beds": beds, "Nurse": nurse, "Obs": obs, "GirlFile": girl_file,
          "Door": door, "Breaker": breaker, "Lever": lever}
