"""
CORE前室（core_ante：幅7 x 奥行8 x 天井3.4）を品質重視で作る。
停電した地下の前室：エポキシ塗りの床と注意の縞・塗装したブロック壁・むき出しのコンクリート天井・
亜鉛めっきの角ダクト2本・壁のケーブルラック・消火配管・消えた蛍光灯・赤い非常灯・冷却水の配管と弁／
MAIN CORE へ続く分厚い鋼の隔壁（ボルト・縞・警告灯・カード読取機）・暗転した制御卓3台・壁の表示器2台・
系統別給電盤。

  Shell      部屋の原点。床・壁・天井・ダクト・ケーブルラック・配管・照明・非常灯・隔壁・掲示
  Interior   部屋の原点。制御卓3台（西の壁際）・壁の表示器2台・卓へ下りるケーブル
  PowerPanel 給電盤の箱（ユニットの原点、前面 = +X、壁は -X 側 0.125）
  Manual / Memo    資料の見た目（箱の中心が原点）
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

W, D, H = 7.0, 8.0, 3.4
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
CONSOLE_ZS = [-2.2 + i * 1.9 for i in range(3)]          # 制御卓（x = -HW0 + 1.2）
CONSOLE_X = -HW0 + 1.2
EMERG_ZS = (-2.0, 2.0)
UNSENT_Z = -3.05
PANEL_Z = HD0 - 1.25                                      # 給電盤（以前は hd-1.9 で、制御卓の真後ろに隠れていた）


def mats():
    M = {}
    M["floor"] = hq.mat("CAN_Epoxy", (1, 1, 1), 0.45, tex="epoxy_floor.png")
    M["hazard"] = hq.mat("CAN_Hazard", (1, 1, 1), 0.5, tex="hazard.png")
    M["block"] = hq.mat("CAN_Block", (1, 1, 1), 0.85, tex="block_wall.png")
    M["concrete"] = hq.mat("CAN_Concrete", (1, 1, 1), 0.9, tex="../../analysis/tex/concrete_dark.png")
    M["galv"] = hq.mat("CAN_Galvanized", (1, 1, 1), 0.4, 0.8, tex="../../analysis/tex/galvanized.png")
    M["steel"] = hq.mat("CAN_Steel", (1, 1, 1), 0.5, 0.3, tex="steel_plate.png")
    M["dark"] = hq.mat("CAN_SteelDark", (0.16, 0.17, 0.19), 0.5, 0.5)
    M["grey"] = hq.mat("CAN_Grey", (0.42, 0.44, 0.46), 0.5, 0.3)
    M["beige"] = hq.mat("CAN_Beige", (0.62, 0.6, 0.54), 0.5)
    M["chrome"] = hq.mat("CAN_Chrome", (0.85, 0.85, 0.86), 0.2, 1.0)
    M["rubber"] = hq.mat("CAN_Rubber", (0.04, 0.04, 0.04), 0.8)
    M["black"] = hq.mat("CAN_Black", (0.02, 0.02, 0.025), 0.5)
    M["red"] = hq.mat("CAN_RedPipe", (0.55, 0.07, 0.05), 0.5)
    M["blue"] = hq.mat("CAN_BluePipe", (0.16, 0.3, 0.5), 0.5, 0.3)
    M["emerg"] = hq.mat("CAN_Emergency", (0.7, 0.1, 0.08), 0.3, emit=(1.0, 0.18, 0.12), emit_strength=6.0)
    M["tube"] = hq.mat("CAN_TubeOff", (0.72, 0.73, 0.72), 0.3)
    M["dead"] = hq.mat("CAN_DeadScreen", (0.02, 0.022, 0.026), 0.05)
    M["crack"] = hq.mat("CAN_ScreenCrack", (1, 1, 1), 0.1, tex="screen_crack.png")
    M["list"] = hq.mat("CAN_ScreenList", (1, 1, 1), 0.2, tex="screen_list.png", emit=(0.5, 0.7, 0.9), emit_strength=0.6)
    M["mail"] = hq.mat("CAN_ScreenMail", (1, 1, 1), 0.2, tex="screen_mail.png", emit=(0.6, 0.7, 0.9), emit_strength=0.6)
    M["cpanel"] = hq.mat("CAN_ConsolePanel", (1, 1, 1), 0.5, tex="console_panel.png")
    M["core"] = hq.mat("CAN_PlateCore", (1, 1, 1), 0.4, tex="plate_core.png")
    M["sign"] = hq.mat("CAN_SignRestricted", (1, 1, 1), 0.5, tex="sign_restricted.png")
    M["plabel"] = hq.mat("CAN_PipeLabel", (1, 1, 1), 0.5, tex="pipe_label.png")
    M["panel_label"] = hq.mat("CAN_PanelLabel", (1, 1, 1), 0.5, tex="panel_label.png")
    M["manual"] = hq.mat("CAN_Manual", (1, 1, 1), 0.9, tex="manual.png")
    M["memo"] = hq.mat("CAN_Memo", (1, 1, 1), 0.9, tex="wmemo.png")
    M["binder"] = hq.mat("CAN_Binder", (0.12, 0.2, 0.36), 0.5)
    M["cable"] = hq.mat("CAN_Cable", (0.03, 0.03, 0.035), 0.4)
    M["cable2"] = hq.mat("CAN_CableGrey", (0.45, 0.47, 0.5), 0.4)
    M["glass"] = hq.mat("CAN_WiredGlass", (0.08, 0.1, 0.11), 0.05)
    M["led_r"] = hq.mat("CAN_LedRed", (0.5, 0.05, 0.05), 0.3, emit=(1.0, 0.1, 0.08), emit_strength=4.0)
    M["btn_r"] = hq.mat("CAN_ButtonRed", (0.45, 0.06, 0.05), 0.4)
    M["btn_g"] = hq.mat("CAN_ButtonGreen", (0.08, 0.3, 0.12), 0.4)
    M["btn_a"] = hq.mat("CAN_ButtonAmber", (0.55, 0.35, 0.05), 0.4)
    M["lever"] = hq.mat("CAN_LeverRed", (0.75, 0.12, 0.1), 0.45)
    return M


# ============================== 外殻 ==============================

def shell(M):
    out = []
    out.append(finish(span("AS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["floor"], bev=0), 0.5))
    walls = []
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"AS_WallNS{zs}", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["block"])
    for xs in (-1, 1):
        walls.append(span(f"AS_WallEW{xs}", *sorted((xs * HW, xs * HW0)), 0.0, H, -HD0, HD0, M["block"], bev=0))
    out += [finish(o, 1 / 1.2, angle=30) for o in walls]
    out.append(finish(span("AS_Ceil", -HW0, HW0, H, H + 0.1, -HD0, HD0, M["concrete"], bev=0), 0.5))
    out += base_and_frames(M)
    out += ducts(M)
    out += cable_ladder(M)
    out += pipes(M)
    out += lights(M)
    out += bulkhead(M)
    out += signs(M)
    return out


def base_and_frames(M):
    """黒い巾木・扉の鋼製枠・床の注意の縞（隔壁の手前）"""
    p = []
    for zs in (-1, 1):
        for (x0, x1) in ((-HW, -DOOR_HALF - 0.08), (DOOR_HALF + 0.08, HW)):
            p.append(span(f"AB_Base{zs}{x0:.0f}", x0, x1, 0, 0.12, *sorted((zs * HD, zs * (HD - 0.01))), M["black"], 0.003))
        zin = zs * HD
        for xs in (-1, 1):
            p.append(span(f"AB_Frame{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.08))), 0, DOOR_H + 0.08,
                          *sorted((zin, zin - zs * 0.03)), M["dark"], 0.006))
            p.append(span(f"AB_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), M["dark"], 0.002))
        p.append(span(f"AB_FrameT{zs}", -DOOR_HALF - 0.08, DOOR_HALF + 0.08, DOOR_H, DOOR_H + 0.08, *sorted((zin, zin - zs * 0.03)), M["dark"], 0.006))
        p.append(span(f"AB_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), M["dark"], 0.002))
    for xs in (-1, 1):
        p.append(span(f"AB_BaseEW{xs}", *sorted((xs * HW, xs * (HW - 0.01))), 0, 0.12, -HD, HD, M["black"], 0.003))
    out = [finish(o, 1.0, angle=40) for o in p]
    # 床の注意の縞（隔壁の手前 z 2.9〜3.15、柱の足元にも）
    stripes = []
    for (x0, x1, z0, z1) in ((-1.8, 1.8, 2.9, 3.15),):
        q = quad("AB_HazardFloor", [(x0, 0.002, z0), (x1, 0.002, z0), (x1, 0.002, z1), (x0, 0.002, z1)], M["hazard"],
                 uv=((0, 0), ((x1 - x0), 0), ((x1 - x0), (z1 - z0) / 0.125), (0, (z1 - z0) / 0.125)))
        hq.face_toward(q, (0, 1, 0))
        stripes.append(q)
    return out + stripes


def ducts(M):
    """亜鉛めっきの角ダクト2本（x=±1.8、中心 y=H-0.4）。継ぎ目のフランジ・吊りボルト・北端は天井へ曲がる"""
    p = []
    s = 0.225
    yc = H - 0.4
    for x in (-1.8, 1.8):
        z0, z1 = -HD + 0.1, 2.9
        p.append(span(f"AD_Duct{x}", x - s, x + s, yc - s, yc + s, z0, z1, M["galv"], 0.004))
        p.append(span(f"AD_Up{x}", x - s, x + s, yc - s, H, z1 - 0.01, z1 + 0.38, M["galv"], 0.004))
        z = z0 + 0.4
        while z < z1 - 0.2:
            p.append(span(f"AD_Flange{x}{z:.1f}", x - s - 0.02, x + s + 0.02, yc - s - 0.02, yc + s + 0.02, z - 0.015, z + 0.015, M["galv"], 0.004))
            for xs in (-1, 1):
                p.append(cyl(f"AD_Rod{x}{z:.1f}{xs}", (x + xs * (s + 0.05), yc - s - 0.03, z), H - (yc - s - 0.03), 0.006, M["chrome"], 8))
            p.append(span(f"AD_Strut{x}{z:.1f}", x - s - 0.08, x + s + 0.08, yc - s - 0.07, yc - s - 0.03, z - 0.02, z + 0.02, M["dark"], 0.003))
            z += 1.2
        # 吹出口のグリル（部屋の中ほど、下向き）
        for zg in (-1.0, 1.0):
            p.append(span(f"AD_Grille{x}{zg}", x - 0.15, x + 0.15, yc - s - 0.012, yc - s, zg - 0.2, zg + 0.2, M["grey"], 0.003))
            for k in range(7):
                zz = zg - 0.17 + k * 0.057
                p.append(span(f"AD_Vane{x}{zg}{k}", x - 0.14, x + 0.14, yc - s - 0.02, yc - s - 0.012, zz - 0.005, zz + 0.005, M["dark"], 0))
    return [finish(o, 1.0, angle=40) for o in p]


def cable_ladder(M):
    """西の壁の高い位置のケーブルラダー（y=2.3）とケーブル束、制御卓への垂直の電線管"""
    p = []
    x0, x1 = -HW, -HW + 0.3
    y = 2.3
    for xx in (x0 + 0.02, x1):
        p.append(span(f"AL_Rail{xx:.2f}", xx - 0.01, xx + 0.01, y - 0.05, y + 0.03, -HD + 0.05, HD - 0.05, M["galv"], 0.003))
    z = -HD + 0.2
    while z < HD - 0.1:
        p.append(span(f"AL_Rung{z:.1f}", x0 + 0.02, x1, y - 0.03, y - 0.01, z - 0.012, z + 0.012, M["galv"], 0.002))
        z += 0.3
    z = -HD + 0.6
    while z < HD - 0.3:
        p.append(pipe(f"AL_Bracket{z:.1f}", [(x0, y - 0.05, z), (x0 + 0.1, y - 0.2, z), (x1, y - 0.05, z)], 0.008, M["dark"], 0.02, 6))
        z += 1.2
    rnd = random.Random(1201)
    for k in range(9):
        xx = x0 + 0.05 + (k % 5) * 0.05
        yy = y + 0.01 + (k // 5) * 0.03
        r = rnd.uniform(0.008, 0.016)
        pts = [(xx, yy, -HD + 0.1)]
        for zz in (-2.0, 0.0, 2.0):
            pts.append((xx + rnd.uniform(-0.01, 0.01), yy + 0.004, zz))
        pts.append((xx, yy, HD - 0.1))
        p.append(pipe(f"AL_Cable{k}", pts, r, M["cable"] if k % 3 else M["cable2"], 0.3, 8))
    # 各制御卓へ下りる電線管
    for z in CONSOLE_ZS:
        zc = z + 0.25
        p.append(pipe(f"AL_Drop{z}", [(x0 + 0.15, y, zc), (x0 + 0.15, y - 0.2, zc), (x0 + 0.05, y - 0.3, zc), (x0 + 0.05, 1.0, zc)], 0.018, M["grey"], 0.06, 10))
    return [finish(o, 2.0, angle=40) for o in p]


def pipes(M):
    """南の隅の冷却水の縦配管（フランジ・弁のハンドル・ラベル帯）と、天井の消火配管"""
    p, flat = [], []
    for xs in (-1, 1):
        x, z = xs * (HW0 - 0.3), -HD0 + 0.4
        p.append(cyl(f"AP_Pipe{xs}", (x, 0.0, z), H, 0.09, M["blue"], 24))
        for y in (0.4, 2.9):
            p.append(cyl(f"AP_Flange{xs}{y}", (x, y - 0.02, z), 0.04, 0.13, M["blue"], 24))
            for k in range(6):
                a = math.pi * 2 * k / 6
                p.append(cyl(f"AP_Bolt{xs}{y}{k}", (x + 0.115 * math.cos(a), y - 0.03, z + 0.115 * math.sin(a)), 0.06, 0.01, M["chrome"], 8))
        # ラベル帯（円柱に巻いた札。管の径より少し大きく）
        band = lathe(f"AP_Label{xs}", (x, 1.6, z), [(0.092, 0.0), (0.092, 0.12)], M["plabel"], 32, cap_top=False, cap_bottom=False)
        me = band.data
        uvl = me.uv_layers.new(name="UVMap").data
        for poly in me.polygons:
            for li in poly.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                a = math.atan2(co.y - U(x, 0, z).y, co.x - U(x, 0, z).x)
                uvl[li].uv = ((a / (2 * math.pi)) * 1.8 % 1.0, (co.z - 1.6) / 0.12)
        flat.append(finish(band, keep_uv=True, angle=60))
    # 西の配管の仕切弁（ハンドル車）
    x, z = -(HW0 - 0.3), -HD0 + 0.4
    p.append(cyl_between("AP_ValveBody", (x, 1.15, z), (x + 0.2, 1.15, z), 0.06, M["blue"], 16))
    p.append(cyl_between("AP_ValveStem", (x + 0.2, 1.15, z), (x + 0.32, 1.15, z), 0.012, M["chrome"], 8))
    wheel = lathe("AP_Wheel", (0, 0, 0), [(0.13 + 0.012 * math.cos(t / 8 * math.pi * 2), 0.012 * math.sin(t / 8 * math.pi * 2)) for t in range(9)],
                  M["red"], 32, cap_top=False, cap_bottom=False)
    wheel.data.transform(Matrix.Translation(U(x + 0.32, 1.15, z)) @ Matrix.Rotation(math.radians(90), 4, "Y"))
    p.append(wheel)
    for k in range(4):
        a = math.pi / 2 * k + 0.3
        p.append(cyl_between(f"AP_Spoke{k}", (x + 0.32, 1.15, z), (x + 0.32, 1.15 + 0.13 * math.sin(a), z + 0.13 * math.cos(a)), 0.008, M["red"], 6))
    # 消火配管（天井、x=0.6 を南北に）とスプリンクラー
    xs = 0.6
    p.append(cyl_between("AP_Sprink", (xs, H - 0.15, -HD + 0.05), (xs, H - 0.15, HD - 0.05), 0.03, M["red"], 16))
    for zz in (-2.6, -0.9, 0.9, 2.6):
        p.append(pipe(f"AP_SprHang{zz}", [(xs, H, zz), (xs, H - 0.12, zz)], 0.005, M["chrome"], 0.01, 6))
        p.append(cyl(f"AP_SprHead{zz}", (xs, H - 0.24, zz), 0.07, 0.012, M["chrome"], 12))
        p.append(cyl(f"AP_SprRose{zz}", (xs, H - 0.245, zz), 0.006, 0.03, M["chrome"], 16))
    return [finish(o, 2.0, angle=40) for o in p if o not in flat] + flat


def lights(M):
    """消えた蛍光灯（x=0、z=±2。1本は外れて垂れている）と東の壁の赤い非常灯"""
    p = []
    for i, z in enumerate((-2.0, 2.0)):
        y = H - 0.45
        for xs in (-0.5, 0.5):
            p.append(cyl_between(f"AF_Chain{i}{xs}", (xs, H, z), (xs, y + 0.06, z), 0.004, M["chrome"], 6))
        p.append(span(f"AF_Body{i}", -0.65, 0.65, y, y + 0.06, z - 0.1, z + 0.1, M["grey"], 0.01))
        p.append(span(f"AF_Refl{i}", -0.64, 0.64, y - 0.012, y, z - 0.09, z + 0.09, M["beige"], 0.004))
        for k, zz in enumerate((z - 0.05, z + 0.05)):
            if i == 1 and k == 1:
                # 外れて片側だけで垂れている管
                p.append(cyl_between(f"AF_TubeHang{i}", (0.6, y - 0.03, zz), (-0.35, y - 0.55, zz + 0.12), 0.013, M["tube"], 12))
                continue
            p.append(cyl_between(f"AF_Tube{i}{k}", (-0.6, y - 0.03, zz), (0.6, y - 0.03, zz), 0.013, M["tube"], 12))
            for ex in (-0.62, 0.62):
                p.append(cyl_between(f"AF_Cap{i}{k}{ex}", (ex - 0.015, y - 0.03, zz), (ex + 0.015, y - 0.03, zz), 0.015, M["grey"], 12))
        # 保護の金網（数本の針金）
        for xx in (-0.45, -0.15, 0.15, 0.45):
            p.append(pipe(f"AF_Guard{i}{xx}", [(xx, y, z - 0.1), (xx, y - 0.07, z - 0.07), (xx, y - 0.07, z + 0.07), (xx, y, z + 0.1)], 0.002, M["chrome"], 0.02, 5))
    # 非常灯：壁の腕金・赤いドーム・金網
    for z in EMERG_ZS:
        xw = HW
        y = H - 0.5
        p.append(span(f"AE_Base{z}", xw - 0.02, xw, y - 0.12, y + 0.12, z - 0.12, z + 0.12, M["dark"], 0.006))
        p.append(span(f"AE_Arm{z}", xw - 0.1, xw - 0.02, y - 0.03, y + 0.03, z - 0.03, z + 0.03, M["dark"], 0.006))
        dome = lathe(f"AE_Dome{z}", (0, 0, 0), [(0.0, -0.001), (0.07, 0.0), (0.075, 0.05), (0.06, 0.1), (0.03, 0.125), (0.0, 0.13)], M["emerg"], 32)
        dome.data.transform(Matrix.Translation(U(xw - 0.1, y, z)) @ Matrix.Rotation(math.radians(90), 4, "Y"))
        p.append(dome)
        for k in range(4):
            a = math.pi / 4 + math.pi / 2 * k
            p.append(pipe(f"AE_Cage{z}{k}", [(xw - 0.1, y + 0.085 * math.sin(a), z + 0.085 * math.cos(a)),
                                              (xw - 0.2, y + 0.07 * math.sin(a), z + 0.07 * math.cos(a)),
                                              (xw - 0.245, y, z)], 0.003, M["chrome"], 0.03, 5))
        p.append(pipe(f"AE_Conduit{z}", [(xw - 0.01, y + 0.12, z), (xw - 0.01, H, z)], 0.012, M["grey"], 0.02, 8))
    return [finish(o, 2.0, angle=45) for o in p]


def bulkhead(M):
    """MAIN CORE へ続く隔壁（柱 x ±0.7〜±1.7、z 3.3〜3.8、高さ 3.0。まぐさ y 2.6〜3.1）"""
    p, flat = [], []
    z0, z1 = HD0 - 0.7, HD0 - 0.2
    for xs in (-1, 1):
        xa, xb = sorted((xs * 0.7, xs * 1.7))
        p.append(span(f"AK_Pillar{xs}", xa, xb, 0.0, 3.0, z0, HD, M["steel"], 0.04, 3))
        # 面のリブ（縦の補強板）とボルトの列
        for rx in (xs * 0.95, xs * 1.45):
            p.append(span(f"AK_Rib{xs}{rx}", rx - 0.04, rx + 0.04, 0.1, 2.55, z0 - 0.05, z0, M["steel"], 0.012))
        for k in range(9):
            y = 0.25 + k * 0.28
            for bx in (xs * 0.8, xs * 1.6):
                p.append(cyl_between(f"AK_Bolt{xs}{k}{bx}", (bx, y, z0), (bx, y, z0 - 0.02), 0.018, M["dark"], 6))
        # 内側の見込みに注意の縞
        xi = xs * 0.7
        q = quad(f"AK_Haz{xs}", [(xi - xs * 0.001, 0.0, z0 if xs > 0 else HD), (xi - xs * 0.001, 0.0, HD if xs > 0 else z0),
                                 (xi - xs * 0.001, 2.6, HD if xs > 0 else z0), (xi - xs * 0.001, 2.6, z0 if xs > 0 else HD)], M["hazard"],
                 uv=((0, 0), ((HD - z0), 0), ((HD - z0), 2.6 / 0.125), (0, 2.6 / 0.125)))
        hq.face_toward(q, (-xs, 0, 0))
        flat.append(q)
        # 足元の縞（柱の正面、高さ 0.3）
        q2 = quad(f"AK_HazFoot{xs}", [(xa, 0.0, z0 - 0.001), (xb, 0.0, z0 - 0.001), (xb, 0.3, z0 - 0.001), (xa, 0.3, z0 - 0.001)], M["hazard"],
                  uv=((0, 0), ((xb - xa), 0), ((xb - xa), 0.3 / 0.125), (0, 0.3 / 0.125)))
        hq.face_toward(q2, (0, 0, -1))
        flat.append(q2)
    p.append(span("AK_Lintel", -1.7, 1.7, 2.6, 3.1, z0, HD, M["steel"], 0.04, 3))
    for k in range(11):
        x = -1.5 + k * 0.3
        if abs(x) < 0.7:
            continue
        for y in (2.7, 2.95):
            p.append(cyl_between(f"AK_LBolt{k}{y}", (x, y, z0), (x, y, z0 - 0.02), 0.018, M["dark"], 6))
    # 敷居（縞鋼板）
    p.append(span("AK_Sill", -0.7, 0.7, 0.0, 0.02, z0, HD, M["dark"], 0.004))
    for k in range(10):
        zz = z0 + 0.03 + k * 0.058
        p.append(span(f"AK_Tread{k}", -0.68, 0.68, 0.02, 0.026, zz - 0.006, zz + 0.006, M["grey"], 0.002))
    # 警告灯（まぐさの下、赤く光る帯）とMAIN CORE の札
    p.append(span("AK_LampHousing", -0.3, 0.3, 2.48, 2.6, z0 - 0.08, z0, M["dark"], 0.01))
    p.append(span("AK_LampLens", -0.27, 0.27, 2.5, 2.58, z0 - 0.09, z0 - 0.08, M["emerg"], 0.006))
    # 札はまぐさの正面に（扉の上の壁だと、部屋名の浮き文字 y=2.4 と重なって読めない）
    p.append(span("AK_PlateBack", -0.62, 0.62, 2.66, 2.96, z0 - 0.02, z0, M["dark"], 0.006))
    pl = quad("AK_Plate", [(-0.6, 2.68, z0 - 0.021), (0.6, 2.68, z0 - 0.021), (0.6, 2.94, z0 - 0.021), (-0.6, 2.94, z0 - 0.021)], M["core"])
    hq.face_toward(pl, (0, 0, -1))
    flat.append(pl)
    # カード読取機（右の柱。赤いランプだけが点く）
    cx = 1.2
    p.append(span("AK_Reader", cx - 0.07, cx + 0.07, 1.2, 1.42, z0 - 0.04, z0, M["black"], 0.01))
    p.append(span("AK_ReaderSlot", cx - 0.05, cx + 0.05, 1.25, 1.3, z0 - 0.045, z0 - 0.04, M["grey"], 0.003))
    p.append(cyl_between("AK_ReaderLed", (cx, 1.37, z0 - 0.04), (cx, 1.37, z0 - 0.048), 0.008, M["led_r"], 10))
    for k in range(12):
        bx, by = cx - 0.04 + (k % 3) * 0.04, 1.02 + (k // 3) * 0.04
        p.append(span(f"AK_Key{k}", bx - 0.015, bx + 0.015, by - 0.015, by + 0.015, z0 - 0.05, z0 - 0.04, M["grey"], 0.004))
    p.append(span("AK_Keypad", cx - 0.08, cx + 0.08, 0.95, 1.18, z0 - 0.04, z0, M["black"], 0.008))
    return [finish(o, 1.0, angle=40) for o in p] + flat


def signs(M):
    """東の壁の区画表示（南の扉寄り）と給電盤の上のラベル"""
    out = []
    xw = HW - 0.004
    q = quad("AG_Sign", [(xw, 1.45, -2.2), (xw, 1.45, -3.3), (xw, 1.8, -3.3), (xw, 1.8, -2.2)], M["sign"])
    hq.face_toward(q, (-1, 0, 0))
    out.append(q)
    return out


# ============================== 制御卓・表示器 ==============================

def console(M, cx, cz, idx):
    """制御卓（天板 1.4 x 0.7・上面 0.75、座る側 = -Z）。奥に傾斜した操作盤と暗転した2画面"""
    rnd = random.Random(1300 + idx)
    p, flat = [], []
    x0, x1 = cx - 0.7, cx + 0.7
    p.append(span("AC_Top", x0, x1, 0.72, 0.75, cz - 0.35, cz + 0.35, M["grey"], 0.008, 3))
    for xx in (x0 + 0.02, x1 - 0.02):
        p.append(span(f"AC_Side{xx:.1f}", xx - 0.02, xx + 0.02, 0.0, 0.72, cz - 0.33, cz + 0.33, M["dark"], 0.006))
    p.append(span("AC_Modesty", x0 + 0.04, x1 - 0.04, 0.15, 0.72, cz + 0.3, cz + 0.33, M["dark"], 0.004))
    # 傾斜パネル（z 0.12→0.35、y 0.75→1.0）
    prof = [(0, 0), (0, 0.25)]
    slope = profile_z("AC_Slope", [(cz + 0.12, 0.75), (cz + 0.35, 1.0), (cz + 0.35, 0.75)], x0 + 0.05, x1 - 0.05, M["dark"])
    # profile_z は (x, y) を z に押し出すので x と z を入れ替える
    for v in slope.data.vertices:
        ux, uy, uz = -v.co.x, v.co.z, -v.co.y
        v.co = U(uz, uy, ux)
    _outward(slope)
    p.append(slope)
    # 傾斜面の印刷（ラベル）と、ボタン・トグル・つまみ
    a = math.atan2(0.25, 0.23)
    def on_slope(u, v, lift=0.0):
        # u: x、v: 0〜1（手前→奥）
        z = cz + 0.12 + 0.23 * v
        y = 0.75 + 0.25 * v
        return (u, y + lift * math.cos(a), z - lift * math.sin(a))
    pts = [on_slope(x1 - 0.08, 0.05, 0.002), on_slope(x0 + 0.08, 0.05, 0.002), on_slope(x0 + 0.08, 0.95, 0.002), on_slope(x1 - 0.08, 0.95, 0.002)]
    # 座る人（-Z 側から +Z を見る）の左は -X
    pts = [on_slope(x0 + 0.08, 0.05, 0.002), on_slope(x1 - 0.08, 0.05, 0.002), on_slope(x1 - 0.08, 0.95, 0.002), on_slope(x0 + 0.08, 0.95, 0.002)]
    lab = quad("AC_Print", pts, M["cpanel"])
    hq.face_toward(lab, (0, math.cos(a), -math.sin(a)))
    flat.append(lab)
    for i in range(8):
        u = x0 + 0.08 + (i + 0.5) * (1.24 / 8)
        for j, v in enumerate((0.3, 0.55, 0.8)):
            c = on_slope(u, v, 0.0)
            m = [M["btn_r"], M["btn_g"], M["btn_a"]][(i + j + idx) % 3]
            if j == 2:
                tip = on_slope(u, v, 0.03)
                p.append(cyl_between(f"AC_Knob{i}{j}", c, tip, 0.012, M["black"], 12))
            elif (i + j) % 2 == 0:
                tip = on_slope(u, v, 0.012)
                p.append(cyl_between(f"AC_Btn{i}{j}", c, tip, 0.01, m, 12))
            else:
                base = on_slope(u, v, 0.006)
                p.append(cyl_between(f"AC_TogB{i}{j}", c, base, 0.01, M["chrome"], 10))
                ang = rnd.choice((-0.5, 0.5))
                tip = (base[0], base[1] + 0.03 * math.cos(ang), base[2] - 0.03 * math.sin(ang) * 0.5 + 0.015 * ang)
                p.append(cyl_between(f"AC_Tog{i}{j}", base, tip, 0.003, M["chrome"], 6))
    # 2画面（傾斜パネルの上の棚。画面は -Z を向く、1枚は割れている）
    p.append(span("AC_Shelf", x0 + 0.05, x1 - 0.05, 1.0, 1.02, cz + 0.2, cz + 0.35, M["dark"], 0.004))
    for k, mx in enumerate((cx - 0.33, cx + 0.33)):
        p.append(span(f"AC_Mon{k}", mx - 0.28, mx + 0.28, 1.04, 1.44, cz + 0.22, cz + 0.33, M["beige"], 0.02, 3))
        p.append(span(f"AC_MonFoot{k}", mx - 0.1, mx + 0.1, 1.02, 1.04, cz + 0.22, cz + 0.33, M["beige"], 0.004))
        cracked = (idx == 1 and k == 0)
        scr = quad(f"AC_Screen{k}", [(mx - 0.24, 1.08, cz + 0.219), (mx + 0.24, 1.08, cz + 0.219), (mx + 0.24, 1.4, cz + 0.219), (mx - 0.24, 1.4, cz + 0.219)],
                   M["crack"] if cracked else M["dead"])
        hq.face_toward(scr, (0, 0, -1))
        flat.append(scr)
    # キーボードと受話器、コーヒーの跡のあるマグ（倒れている）
    p.append(span("AC_Kb", cx + 0.05, cx + 0.5, 0.75, 0.765, cz - 0.2, cz - 0.05, M["beige"], 0.005))
    if idx == 0:
        mug = lathe("AC_Mug", (0, 0, 0), [(0.0, 0.0), (0.04, 0.0), (0.042, 0.1), (0.038, 0.1), (0.0, 0.006)], M["beige"], 20, cap_top=False)
        mug.data.transform(Matrix.Translation(U(cx + 0.45, 0.79, cz + 0.02)) @ Matrix.Rotation(math.radians(90), 4, "X") @ Matrix.Rotation(0.7, 4, "Z"))
        p.append(mug)
    if idx == 2:
        for k in range(3):
            y = 0.75 + k * 0.035
            p.append(span(f"AC_Binder{k}", cx + 0.1, cx + 0.42, y, y + 0.033, cz - 0.3 + k * 0.01, cz - 0.08 + k * 0.01, M["binder"], 0.004))
    # 床へ垂れるケーブル
    p.append(pipe(f"AC_Cable{idx}", [(cx - 0.4, 0.8, cz + 0.33), (cx - 0.45, 0.4, cz + 0.4), (cx - 0.6, 0.02, cz + 0.45), (-HW + 0.05, 0.02, cz + 0.3)], 0.01, M["cable"], 0.1, 8))
    return [finish(o, 1.0, angle=40) for o in p] + flat


def _outward(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()


def wall_display(M, z, mat):
    """西の壁の表示器（前面 = +X、中心 y=1.4）"""
    p = []
    xw = -HW
    p.append(span("AW_Body", xw, xw + 0.06, 1.1, 1.7, z - 0.45, z + 0.45, M["black"], 0.012))
    xs = xw + 0.061
    # 見る人（+X 側から -X を見る）の左は -Z
    scr = quad("AW_Screen", [(xs, 1.14, z - 0.41), (xs, 1.14, z + 0.41), (xs, 1.66, z + 0.41), (xs, 1.66, z - 0.41)], mat)
    hq.face_toward(scr, (1, 0, 0))
    p.append(pipe("AW_Cable", [(xw + 0.02, 1.1, z + 0.3), (xw + 0.02, 0.9, z + 0.32), (xw + 0.02, 0.12, z + 0.32)], 0.008, M["cable"], 0.05, 6))
    return [finish(o, 1.0, angle=40) for o in p] + [scr]


def interior(M):
    out = []
    for i, z in enumerate(CONSOLE_ZS):
        out += console(M, CONSOLE_X, z, i)
    out += wall_display(M, 0.5, M["list"])
    # 未送信メッセージの表示器は南の壁寄りへ（以前は z=2.4 で給電盤と重なっていた。給電盤は北へ寄せて前を空けた）
    out += wall_display(M, UNSENT_Z, M["mail"])
    return out


def power_panel(M):
    """系統別給電盤（ユニットの原点 = ビルダーの PowerPanel の箱の中心。前面 = +X、壁は -X 側 0.125）。
    ランプと系統名の札はビルダー側（状態で色が変わる）"""
    p, flat = [], []
    xf, xb = 0.07, -0.125
    p.append(span("PP_Box", xb, xf - 0.01, -0.45, 0.45, -0.35, 0.35, M["grey"], 0.012, 3))
    p.append(span("PP_Door", xf - 0.012, xf, -0.43, 0.43, -0.33, 0.33, M["grey"], 0.008))
    for y in (-0.3, 0.3):
        p.append(cyl_between(f"PP_Hinge{y}", (xf, y - 0.03, -0.335), (xf, y + 0.03, -0.335), 0.007, M["chrome"], 10))
    p.append(cyl_between("PP_Lock", (xf, -0.2, 0.28), (xf + 0.015, -0.2, 0.28), 0.014, M["chrome"], 16))
    # ランプの座（ビルダーのランプ x=0.08、y=0.3-0.22i、z=0.15 の後ろ）と札の枠
    for i in range(3):
        y = 0.3 - i * 0.22
        p.append(cyl_between(f"PP_LampRing{i}", (xf, y, 0.15), (xf + 0.006, y, 0.15), 0.06, M["chrome"], 20))
        p.append(span(f"PP_LabelFrame{i}", xf, xf + 0.004, y - 0.04, y + 0.04, -0.27, -0.03, M["dark"], 0.003))
    lab = quad("PP_Label", [(xf + 0.001, -0.42, -0.3), (xf + 0.001, -0.42, 0.3), (xf + 0.001, -0.3, 0.3), (xf + 0.001, -0.3, -0.3)], M["panel_label"])
    hq.face_toward(lab, (1, 0, 0))
    flat.append(lab)
    # 天井へ上がる電線管
    for dz in (-0.2, 0.2):
        p.append(pipe(f"PP_Conduit{dz}", [(xb + 0.06, 0.45, dz), (xb + 0.06, H - 1.3 + 0.05, dz)], 0.02, M["grey"], 0.02, 10))
    return [join([finish(o, 2.0, angle=40) for o in p] + flat, "CoreAnte_PowerPanel")]


# ============================== 資料 ==============================

def _sheet(name, w, d, y, mat):
    q = quad(name, [(-w / 2, y, -d / 2), (w / 2, y, -d / 2), (w / 2, y, d / 2), (-w / 2, y, d / 2)], mat)
    hq.face_toward(q, (0, 1, 0))
    return q


def manual(M):
    """開いたリングファイルの運用手順書（紺の表紙）"""
    p = [finish(span("MN_Cover", -0.17, 0.17, -0.01, -0.006, -0.12, 0.12, M["binder"], 0.003), 2.0)]
    for k in range(3):
        p.append(finish(lathe(f"MN_Ring{k}", (0, 0, 0), [(0.012, 0.0), (0.012, 0.004)], M["chrome"], 12, cap_top=False, cap_bottom=False), 2.0))
        p[-1].data.transform(Matrix.Translation(U(0, -0.002, -0.08 + k * 0.08)))
    s = _sheet("MN_Pages", 0.332, 0.232, -0.005, M["manual"])
    one = join(p + [s], "CoreAnte_Manual")
    one.data.transform(Matrix.Rotation(math.radians(-4), 4, "Z"))
    return [one]


def memo(M):
    one = _sheet("MM_Memo", 0.2, 0.14, -0.009, M["memo"])
    s = one.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.0008
    finish(one, keep_uv=True, angle=60)
    one.data.transform(Matrix.Rotation(math.radians(12), 4, "Z"))
    return [one]


# ============================== 扉・配電盤 ==============================

def door(M):
    """重い鋼製扉：縁の枠・小さな網入りガラス・押し棒・蝶番・足元の縞"""
    t = 0.035
    p, flat = [], []
    win = (-0.12, 0.12, 1.45, 1.8)
    p += train_room.grid_wall("CDr_Leaf", [win], -0.458, 0.458, 0.0, 2.1, lambda a, b, c, d: (a, b, c, d, -t, t), M["steel"])
    for zs in (-1, 1):
        zf = zs * t
        # 縁の枠：縦の枠は上下の枠の間だけ（四隅で重ねない）
        for (a0, a1, b0, b1) in ((-0.458, 0.458, 2.03, 2.1), (-0.458, 0.458, 0.0, 0.07), (-0.458, -0.39, 0.07, 2.03), (0.39, 0.458, 0.07, 2.03)):
            p.append(span("CDr_Rim", a0, a1, b0, b1, *sorted((zf, zf + zs * 0.012)), M["steel"], 0.006))
        for (a0, a1, b0, b1) in ((-0.15, 0.15, 1.42, 1.45), (-0.15, 0.15, 1.8, 1.83), (-0.15, -0.12, 1.45, 1.8), (0.12, 0.15, 1.45, 1.8)):
            p.append(span("CDr_WinFrame", a0, a1, b0, b1, *sorted((zf, zf + zs * 0.01)), M["dark"], 0.004))
        g = quad(f"CDr_Glass{zs}", [(-0.12, 1.45, zs * 0.005), (0.12, 1.45, zs * 0.005), (0.12, 1.8, zs * 0.005), (-0.12, 1.8, zs * 0.005)], M["glass"])
        hq.face_toward(g, (0, 0, zs)); flat.append(g)
        # 押し棒（両面）
        for xx in (-0.3, 0.3):
            p.append(span(f"CDr_BarFoot{zs}{xx}", xx - 0.03, xx + 0.03, 0.98, 1.08, *sorted((zf + zs * 0.012, zf + zs * 0.07)), M["dark"], 0.008))
        p.append(cyl_between(f"CDr_Bar{zs}", (-0.34, 1.03, zf + zs * 0.07), (0.34, 1.03, zf + zs * 0.07), 0.018, M["chrome"], 16))
        kq = quad(f"CDr_Kick{zs}", [(-0.39 * zs, 0.07, zf + zs * 0.013), (0.39 * zs, 0.07, zf + zs * 0.013), (0.39 * zs, 0.3, zf + zs * 0.013), (-0.39 * zs, 0.3, zf + zs * 0.013)],
                  M["hazard"], uv=((0, 0), (0.78, 0), (0.78, 0.23 / 0.125), (0, 0.23 / 0.125)))
        hq.face_toward(kq, (0, 0, zs)); flat.append(kq)
    for y in (0.3, 1.05, 1.8):
        p.append(cyl_between(f"CDr_Hinge{y}", (0.466, y - 0.07, 0.0), (0.466, y + 0.07, 0.0), 0.012, M["dark"], 12))
    for o in p:
        finish(o, 1.0, angle=40)
    return [join(p + flat, "CoreAnte_Door")]


def _breaker_mats(M):
    return {"mel": M["grey"], "grille": M["black"], "hazard": M["hazard"], "sus": M["chrome"], "rubber": M["rubber"], "lever": M["lever"]}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.01), "CoreAnte_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "CoreAnte_Lever")]


PIECES = {"Shell": shell, "Interior": interior, "PowerPanel": power_panel, "Manual": manual, "Memo": memo,
          "Door": door, "Breaker": breaker, "Lever": lever}
