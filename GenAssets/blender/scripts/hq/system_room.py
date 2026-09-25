"""
SYSTEM ROOM（system_room：幅10 x 奥行10 x 天井4.0）を品質重視で作る。
暗い床と紺灰の吸音パネルの壁、中央に RENASCITA CORE：台座（通気口・ボルト・銘板）・ガラス管・
光る芯（神経の繊維）・3つの BRAIN DATA の光点・金属の帯とリブ・天井のキャップ。
コアから放射状に床を這うケーブル束8本が壁際で立ち上がり、天井を這ってキャップへ戻る。
西の壁の大型モニター4面（状態・結線図・操作記録の復元・同期波形）、壁際の青いライン、操作卓（3画面）。

  Shell      部屋の原点。床（コアを囲む光の輪）・壁・天井・照明・ライン・ケーブル・壁のモニター
  Core       部屋の原点。コア一式
  Console    操作卓（ユニットの原点、天板の上面 0.75、座る側 = -Z、画面はコアの方 = +Z の奥）
  DevLog / GapLog   資料の見た目（箱の中心が原点）
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

W, D, H = 10.0, 10.0, 4.0
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
CORE = (0.0, 1.0)
LIGHT_ZS = (-2.5, 2.5)
MON_ZS = (-3.0, -1.0, 1.0, 3.0)


def arms():
    """ケーブルの腕（ビルダーの CableArm と同じ：yaw = 45i + 22.5、立ち上がりまでの距離 3.9 / 3.4）"""
    out = []
    for i in range(8):
        a = math.radians(i * 45 + 22.5)
        reach = 3.9 if i % 2 == 0 else 3.4
        out.append((math.sin(a), math.cos(a), reach))
    return out


def mats():
    M = {}
    M["floor"] = hq.mat("SYS_Floor", (1, 1, 1), 0.4, tex="dark_floor.png")
    M["wall"] = hq.mat("SYS_WallPanel", (1, 1, 1), 0.9, tex="wall_panel.png")
    M["ceil"] = hq.mat("SYS_Ceiling", (0.06, 0.065, 0.075), 0.9)
    M["metal"] = hq.mat("SYS_MetalDark", (0.14, 0.15, 0.17), 0.35, 0.8)
    M["metalL"] = hq.mat("SYS_MetalLight", (0.55, 0.57, 0.6), 0.3, 0.9)
    M["glass"] = hq.mat("SYS_CoreGlass", (0.5, 0.75, 0.95), 0.05, alpha=0.18)
    M["inner"] = hq.mat("SYS_CoreInner", (1, 1, 1), 0.3, tex="core_inner.png", emit=(1, 1, 1), emit_strength=2.0)
    M["node"] = hq.mat("SYS_DataNode", (0.7, 0.9, 1.0), 0.2, emit=(0.6, 0.85, 1.0), emit_strength=8.0)
    M["ring"] = hq.mat("SYS_LightRing", (0.2, 0.4, 0.6), 0.3, emit=(0.3, 0.6, 1.0), emit_strength=4.0)
    M["strip"] = hq.mat("SYS_BlueStrip", (0.2, 0.4, 0.6), 0.3, emit=(0.3, 0.6, 1.0), emit_strength=4.0)
    M["down"] = hq.mat("SYS_Downlight", (0.9, 0.93, 1.0), 0.3, emit=(0.85, 0.9, 1.0), emit_strength=6.0)
    M["cable"] = hq.mat("SYS_Cable", (0.035, 0.035, 0.04), 0.35)
    M["cable2"] = hq.mat("SYS_CableBlue", (0.08, 0.14, 0.26), 0.35)
    M["black"] = hq.mat("SYS_Black", (0.02, 0.02, 0.025), 0.4)
    M["plate"] = hq.mat("SYS_CorePlate", (1, 1, 1), 0.4, tex="core_plate.png")
    M["status"] = hq.mat("SYS_ScreenStatus", (1, 1, 1), 0.2, tex="screen_status.png", emit=(1, 1, 1), emit_strength=0.8)
    M["connect"] = hq.mat("SYS_ScreenConnectome", (1, 1, 1), 0.2, tex="../../analysis/tex/screen_connectome.png", emit=(1, 1, 1), emit_strength=0.8)
    M["restore"] = hq.mat("SYS_ScreenRestore", (1, 1, 1), 0.2, tex="screen_restore.png", emit=(1, 1, 1), emit_strength=0.8)
    M["wave"] = hq.mat("SYS_ScreenWave", (1, 1, 1), 0.2, tex="screen_wave.png", emit=(1, 1, 1), emit_strength=0.8)
    M["desk"] = hq.mat("SYS_DeskTop", (0.2, 0.21, 0.23), 0.5, 0.2)
    M["devlog"] = hq.mat("SYS_DevLog", (1, 1, 1), 0.9, tex="devlog.png")
    M["gaplog"] = hq.mat("SYS_GapLog", (1, 1, 1), 0.9, tex="gaplog.png")
    M["binder"] = hq.mat("SYS_Binder", (0.1, 0.12, 0.16), 0.5)
    M["chrome"] = hq.mat("SYS_Chrome", (0.85, 0.85, 0.86), 0.2, 1.0)
    M["led_g"] = hq.mat("SYS_LedGreen", (0.1, 0.4, 0.2), 0.3, emit=(0.3, 1.0, 0.5), emit_strength=5.0)
    M["lever"] = hq.mat("SYS_LeverRed", (0.75, 0.12, 0.1), 0.45)
    M["hazard"] = hq.mat("SYS_Hazard", (1, 1, 1), 0.5, tex="../../lab/tex/hazard.png")
    M["steelplate"] = hq.mat("SYS_Steel", (1, 1, 1), 0.5, 0.3, tex="../../core_ante/tex/steel_plate.png")
    M["wglass"] = hq.mat("SYS_WiredGlass", (0.08, 0.1, 0.11), 0.05)
    return M


def _outward(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()


def _cyl_uv(o, cx, cz, y0, y1, u_rep=1.0):
    """円筒の側面に巻いたUV（u = 角度、v = 高さ）"""
    me = o.data
    uvl = (me.uv_layers[0] if me.uv_layers else me.uv_layers.new(name="UVMap")).data
    c = U(cx, 0, cz)
    for poly in me.polygons:
        us = []
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            a = (math.atan2(co.y - c.y, co.x - c.x) / (2 * math.pi)) % 1.0
            us.append(a)
        # 継ぎ目をまたぐ面は u をそろえる
        if max(us) - min(us) > 0.5:
            us = [u + 1.0 if u < 0.5 else u for u in us]
        for li, u in zip(poly.loop_indices, us):
            co = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = (u * u_rep, (co.z - y0) / (y1 - y0))


# ============================== 外殻 ==============================

def shell(M):
    out = []
    out.append(finish(span("SS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["floor"], bev=0), 1 / 1.2))
    walls = []
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"SS_WallNS{zs}", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["wall"])
    for xs in (-1, 1):
        walls.append(span(f"SS_WallEW{xs}", *sorted((xs * HW, xs * HW0)), 0.0, H, -HD0, HD0, M["wall"], bev=0))
    out += [finish(o, 1 / 1.2, angle=30) for o in walls]
    out.append(finish(span("SS_Ceil", -HW0, HW0, H, H + 0.1, -HD0, HD0, M["ceil"], bev=0), 1.0))
    p = []
    # コアを囲む光の輪（床に埋め込み）
    p.append(lathe("SS_FloorRing", (CORE[0], 0.0, CORE[1]), [(1.55, 0.0), (1.62, 0.0), (1.62, 0.004), (1.55, 0.004)], M["ring"], 96))
    p.append(lathe("SS_FloorRingRim", (CORE[0], 0.0, CORE[1]), [(1.5, 0.0), (1.67, 0.0), (1.67, 0.002), (1.5, 0.002)], M["metal"], 96, cap_top=False, cap_bottom=False))
    # 壁際の青いライン（東西、南北）と黒い巾木
    for xs in (-1, 1):
        a, b = sorted((xs * HW, xs * (HW - 0.03)))
        p.append(span(f"SS_StripChannel{xs}", a, b, 0.2, 0.3, -HD, HD, M["metal"], 0.004))
        a2, b2 = sorted((xs * (HW - 0.03), xs * (HW - 0.034)))
        p.append(span(f"SS_Strip{xs}", a2, b2, 0.235, 0.265, -HD + 0.05, HD - 0.05, M["strip"], 0))
        p.append(span(f"SS_Base{xs}", *sorted((xs * HW, xs * (HW - 0.012))), 0.0, 0.1, -HD, HD, M["black"], 0.003))
    for zs in (-1, 1):
        for (x0, x1) in ((-HW, -DOOR_HALF - 0.08), (DOOR_HALF + 0.08, HW)):
            p.append(span(f"SS_BaseNS{zs}{x0:.0f}", x0, x1, 0.0, 0.1, *sorted((zs * HD, zs * (HD - 0.012))), M["black"], 0.003))
            p.append(span(f"SS_StripNS{zs}{x0:.0f}", x0, x1, 0.235, 0.265, *sorted((zs * (HD - 0.03), zs * (HD - 0.034))), M["strip"], 0))
            p.append(span(f"SS_ChanNS{zs}{x0:.0f}", x0, x1, 0.2, 0.3, *sorted((zs * HD, zs * (HD - 0.03))), M["metal"], 0.004))
        zin = zs * HD
        for xs in (-1, 1):
            p.append(span(f"SS_Frame{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.08))), 0, DOOR_H + 0.08, *sorted((zin, zin - zs * 0.03)), M["metal"], 0.006))
            p.append(span(f"SS_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), M["metal"], 0.002))
        p.append(span(f"SS_FrameT{zs}", -DOOR_HALF - 0.08, DOOR_HALF + 0.08, DOOR_H, DOOR_H + 0.08, *sorted((zin, zin - zs * 0.03)), M["metal"], 0.006))
        p.append(span(f"SS_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), M["metal"], 0.002))
    # 天井の丸いダウンライト（ビルダーの RoomLight の上）と操作卓のスポット
    for i, zc in enumerate(LIGHT_ZS):
        p.append(lathe(f"SS_Down{i}", (0, H - 0.03, zc), [(0.0, 0.0), (0.09, 0.0), (0.1, 0.03)], M["down"], 32, cap_top=False))
        p.append(lathe(f"SS_DownRim{i}", (0, H - 0.01, zc), [(0.1, 0.0), (0.13, 0.0), (0.13, 0.01)], M["metalL"], 32, cap_top=False, cap_bottom=False))
    sx, sz = 1.7, -HD0 + 1.7
    p.append(cyl(f"SS_SpotStem", (sx, H - 0.25, sz), 0.25, 0.012, M["metal"], 12))
    p.append(lathe("SS_Spot", (sx, H - 0.45, sz), [(0.06, 0.0), (0.08, 0.02), (0.08, 0.16), (0.05, 0.2), (0.0, 0.2)], M["metal"], 24))
    p.append(lathe("SS_SpotLens", (sx, H - 0.452, sz), [(0.0, 0.0), (0.065, 0.0), (0.065, 0.002)], M["down"], 24, cap_top=False))
    out += [finish(o, 2.0, angle=45) for o in p]
    out += cables(M)
    out += wall_monitors(M)
    return out


def cables(M):
    """コアの台座から放射状に床を這い、壁際で立ち上がって天井を這い、キャップへ戻るケーブル束"""
    p = []
    cx, cz = CORE
    rnd = random.Random(2001)
    for i, (dx, dz, reach) in enumerate(arms()):
        nx, nz = dz, -dx                        # 腕に直交する方向
        for k in range(3):
            off = (k - 1) * 0.06
            r = 0.035 if k != 1 else 0.045
            m = M["cable"] if k != 2 else M["cable2"]
            bx, bz = cx + dx * 1.3 + nx * off, cz + dz * 1.3 + nz * off
            ex, ez = cx + dx * reach + nx * off, cz + dz * reach + nz * off
            pts = [(cx + dx * 1.1 + nx * off, 0.35, cz + dz * 1.1 + nz * off), (bx, r + 0.01, bz)]
            for t in (0.35, 0.7):
                pts.append((bx + (ex - bx) * t + rnd.uniform(-0.03, 0.03), r, bz + (ez - bz) * t + rnd.uniform(-0.03, 0.03)))
            pts += [(ex - dx * 0.12, r, ez - dz * 0.12), (ex, 0.25, ez), (ex, H - 0.3, ez)]
            # 天井を這ってキャップへ
            pts += [(ex - dx * 0.2, H - 0.06, ez - dz * 0.2), (cx + dx * (reach * 0.5), H - 0.1 - 0.08 * (k % 2), cz + dz * (reach * 0.5)),
                    (cx + dx * 1.4, H - 0.08, cz + dz * 1.4), (cx + dx * 1.1, H - 0.3, cz + dz * 1.1)]
            p.append(pipe(f"SC_Cable{i}{k}", pts, r, m, 0.18, 10))
        ex, ez = cx + dx * reach, cz + dz * reach
        # 立ち上がりの留め金具
        for y in (0.8, 1.8, 2.8):
            p.append(lathe(f"SC_Clamp{i}{y}", (ex, y, ez), [(0.11, 0.0), (0.11, 0.05)], M["metalL"], 16, cap_top=False, cap_bottom=False))
        # 床のケーブル押さえ（中ほど）
        mx, mz = cx + dx * (1.3 + (reach - 1.3) * 0.5), cz + dz * (1.3 + (reach - 1.3) * 0.5)
        q = [(mx - nx * 0.16 - dx * 0.1, mz - nz * 0.16 - dz * 0.1), (mx + nx * 0.16 - dx * 0.1, mz + nz * 0.16 - dz * 0.1),
             (mx + nx * 0.16 + dx * 0.1, mz + nz * 0.16 + dz * 0.1), (mx - nx * 0.16 + dx * 0.1, mz - nz * 0.16 + dz * 0.1)]
        cover = hq.grid_surface(f"SC_Cover{i}", 8, 2, lambda u, v, q=q: (
            q[0][0] + (q[1][0] - q[0][0]) * u + (q[3][0] - q[0][0]) * v, 0.1 * math.sin(math.pi * u) ** 0.6 + 0.002,
            q[0][1] + (q[1][1] - q[0][1]) * u + (q[3][1] - q[0][1]) * v), M["metal"])
        s = cover.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.006
        finish(cover, 2.0, angle=50)
        _outward(cover)
        p.append(cover)
    return [finish(o, 2.0, angle=50) for o in p]


def wall_monitors(M):
    """西の壁の大型モニター4面（ビルダーの WallMonitor：z = -3, -1, 1, 3、中心 y 1.7）"""
    p, flat = [], []
    mats_ = [M["status"], M["connect"], M["restore"], M["wave"]]
    xw = -HW
    for k, z in enumerate(MON_ZS):
        p.append(span(f"SM_Frame{k}", xw, xw + 0.07, 1.14, 2.26, z - 0.82, z + 0.82, M["black"], 0.01))
        p.append(span(f"SM_Bracket{k}", xw, xw + 0.03, 1.5, 1.9, z - 0.3, z + 0.3, M["metal"], 0.004))
        xs = xw + 0.071
        # 見る人（+X 側から -X を見る）の左は -Z
        scr = quad(f"SM_Screen{k}", [(xs, 1.19, z - 0.76), (xs, 1.19, z + 0.76), (xs, 2.21, z + 0.76), (xs, 2.21, z - 0.76)], mats_[k])
        hq.face_toward(scr, (1, 0, 0))
        flat.append(scr)
        p.append(pipe(f"SM_Cable{k}", [(xw + 0.03, 1.14, z + 0.6), (xw + 0.03, 0.8, z + 0.62), (xw + 0.03, 0.3, z + 0.62)], 0.01, M["cable"], 0.05, 6))
    return [finish(o, 1.0, angle=40) for o in p] + flat


# ============================== コア ==============================

def core(M):
    cx, cz = CORE
    p, flat = [], []
    # 台座（r 1.3、高さ 0.5）：段付き・通気口・ボルト・銘板
    p.append(lathe("CR_Base", (cx, 0.0, cz), [(1.32, 0.0), (1.32, 0.06), (1.3, 0.08), (1.3, 0.44), (1.24, 0.5), (1.02, 0.5)], M["metal"], 96, cap_bottom=False))
    for k in range(24):
        a = 2 * math.pi * k / 24
        x, z = cx + math.cos(a) * 1.305, cz + math.sin(a) * 1.305
        if k % 6 == 5:
            continue
        p.append(cyl_between(f"CR_Bolt{k}", (cx + math.cos(a) * 1.3, 0.46, cz + math.sin(a) * 1.3), (cx + math.cos(a) * 1.3, 0.49, cz + math.sin(a) * 1.3), 0.018, M["metalL"], 8))
        if k % 3 == 0:
            # 通気口（縦の細いスリット）
            for j in range(5):
                aa = a + (j - 2) * 0.03
                p.append(span(f"CR_Vent{k}{j}", cx + math.cos(aa) * 1.3 - 0.006, cx + math.cos(aa) * 1.3 + 0.006, 0.14, 0.38,
                              cz + math.sin(aa) * 1.3 - 0.006, cz + math.sin(aa) * 1.3 + 0.006, M["black"], 0))
    # 銘板（南 = 入口の方）
    pz = cz - 1.312
    pl = quad("CR_Plate", [(cx - 0.4, 0.2, pz), (cx + 0.4, 0.2, pz), (cx + 0.4, 0.4, pz), (cx - 0.4, 0.4, pz)], M["plate"])
    # 南面は -Z を向く：見る人（-Z 側から +Z を見る）の左は -X
    hq.face_toward(pl, (0, 0, -1))
    flat.append(pl)
    # ガラス管（r 1.0、y 0.5〜3.3）と光る芯（r 0.5）
    gl = lathe("CR_Glass", (cx, 0.5, cz), [(1.0, 0.0), (1.0, 2.8)], M["glass"], 96, cap_top=False, cap_bottom=False)
    s = gl.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.01
    finish(gl, 1.0, angle=180)
    _outward(gl)
    flat.append(gl)
    inner = lathe("CR_Inner", (cx, 0.55, cz), [(0.0, 0.0), (0.45, 0.0), (0.5, 0.05), (0.5, 2.6), (0.45, 2.65), (0.0, 2.65)], M["inner"], 64)
    _cyl_uv(inner, cx, cz, 0.55, 3.2, 2.0)
    finish(inner, keep_uv=True, angle=60)
    flat.append(inner)
    # 芯の中の縦の繊維（細い光の管）
    rnd = random.Random(2011)
    for k in range(10):
        a = rnd.uniform(0, math.pi * 2); r = rnd.uniform(0.52, 0.62)
        p.append(cyl(f"CR_Fiber{k}", (cx + math.cos(a) * r, 0.5, cz + math.sin(a) * r), 2.8, 0.006, M["ring"], 6))
    # 3つの BRAIN DATA の光点（芯の周り、高さを変えて）
    for k, (ang, y) in enumerate(((90, 1.45), (210, 2.05), (330, 2.65))):
        a = math.radians(ang)
        nx, nz = cx + math.cos(a) * 0.75, cz + math.sin(a) * 0.75
        node = lathe(f"CR_Node{k}", (nx, y - 0.08, nz), [(0.0, 0.0), (0.05, 0.01), (0.075, 0.05), (0.075, 0.11), (0.05, 0.15), (0.0, 0.16)], M["node"], 24)
        flat.append(finish(node, 2.0, angle=60))
        ring = lathe(f"CR_NodeRing{k}", (0, 0, 0), [(0.12 + 0.006 * math.cos(t / 6 * math.pi * 2), 0.006 * math.sin(t / 6 * math.pi * 2)) for t in range(7)],
                     M["metalL"], 32, cap_top=False, cap_bottom=False)
        ring.data.transform(Matrix.Translation(U(nx, y, nz)) @ Matrix.Rotation(math.radians(70), 4, "X") @ Matrix.Rotation(a, 4, "Z"))
        p.append(ring)
        p.append(cyl_between(f"CR_NodeLink{k}", (cx + math.cos(a) * 0.5, y, cz + math.sin(a) * 0.5), (nx, y, nz), 0.008, M["ring"], 8))
    # 金属の帯（y 1.2 / 2.6）と縦のリブ6本
    for y in (1.2, 2.6):
        p.append(lathe(f"CR_Band{y}", (cx, y - 0.05, cz), [(1.0, 0.0), (1.06, 0.0), (1.07, 0.02), (1.07, 0.08), (1.06, 0.1), (1.0, 0.1)], M["metalL"], 96, cap_top=False, cap_bottom=False))
    for k in range(6):
        a = 2 * math.pi * k / 6 + math.pi / 6
        p.append(span(f"CR_Rib{k}", cx + math.cos(a) * 1.0 - 0.03, cx + math.cos(a) * 1.0 + 0.03, 0.5, 3.3, cz + math.sin(a) * 1.0 - 0.03, cz + math.sin(a) * 1.0 + 0.03, M["metal"], 0.008))
    # キャップ（r 1.3、y 3.3〜4.0）とケーブルの差込口
    p.append(lathe("CR_Cap", (cx, 3.3, cz), [(1.0, 0.0), (1.24, 0.0), (1.3, 0.06), (1.3, 0.62), (1.34, 0.7)], M["metal"], 96, cap_top=False))
    p.append(lathe("CR_CapUnder", (cx, 3.3, cz), [(0.0, 0.0), (1.0, 0.0)], M["black"], 64, cap_top=False, cap_bottom=False))
    for i, (dx, dz, reach) in enumerate(arms()):
        p.append(cyl_between(f"CR_Port{i}", (cx + dx * 1.2, 3.3, cz + dz * 1.2), (cx + dx * 1.2, 3.22, cz + dz * 1.2), 0.07, M["metalL"], 16))
        p.append(cyl_between(f"CR_BasePort{i}", (cx + dx * 1.05, 0.5, cz + dz * 1.05), (cx + dx * 1.05, 0.56, cz + dz * 1.05), 0.07, M["metalL"], 16))
    # 台座の周りの点検用ステップ（南）
    p.append(span("CR_Step", cx - 0.5, cx + 0.5, 0.0, 0.12, cz - 1.55, cz - 1.3, M["metal"], 0.01))
    return [finish(o, 1.0, angle=45) for o in p] + flat


# ============================== 操作卓 ==============================

def console(M):
    """操作卓（原点、天板 1.4 x 0.7・上面 0.75）。座る人は -Z、画面3台は奥（+Z）に並んでコアの方へ背を向ける"""
    p, flat = [], []
    prof = [(-0.7, 0.72), (0.7, 0.72), (0.7, 0.75), (-0.7, 0.75)]
    p.append(span("CN_Top", -0.7, 0.7, 0.72, 0.75, -0.35, 0.35, M["desk"], 0.01, 3))
    p.append(span("CN_Front", -0.7, 0.7, 0.66, 0.72, -0.37, -0.33, M["metal"], 0.008))
    for xs in (-1, 1):
        p.append(span(f"CN_Side{xs}", *sorted((xs * 0.66, xs * 0.7)), 0.0, 0.72, -0.33, 0.33, M["metal"], 0.006))
    p.append(span("CN_Back", -0.66, 0.66, 0.1, 0.72, 0.3, 0.33, M["metal"], 0.004))
    p.append(span("CN_Kick", -0.66, 0.66, 0.0, 0.1, -0.2, 0.33, M["black"], 0.004))
    # 3画面（アームで奥に並ぶ。中央はまっすぐ、左右は少し内向き）
    screens = [M["wave"], M["status"], M["connect"]]
    for k, (mx, yaw) in enumerate(((-0.5, 15), (0.0, 0), (0.5, -15))):
        mon = []
        mon.append(span(f"CN_Mon{k}", -0.25, 0.25, 0.9, 1.22, -0.015, 0.015, M["black"], 0.008))
        sq = quad(f"CN_Scr{k}", [(-0.235, 0.915, -0.0155), (0.235, 0.915, -0.0155), (0.235, 1.205, -0.0155), (-0.235, 1.205, -0.0155)], screens[k])
        hq.face_toward(sq, (0, 0, -1))
        mon.append(sq)
        mon.append(cyl(f"CN_MonArm{k}", (0, 0.75, 0.08), 0.18, 0.015, M["metalL"], 12))
        mon.append(span(f"CN_MonHinge{k}", -0.04, 0.04, 0.95, 1.05, 0.015, 0.08, M["metalL"], 0.006))
        mon.append(lathe(f"CN_MonFoot{k}", (0, 0.75, 0.08), [(0.08, 0.0), (0.08, 0.012), (0.02, 0.02)], M["metalL"], 20))
        for o in mon:
            if o is not sq:
                finish(o, 1.0, angle=40)
        mo = join(mon, f"CN_MonUnit{k}")
        mo.data.transform(Matrix.Translation(U(mx, 0, 0.2)) @ Matrix.Rotation(math.radians(-yaw), 4, "Z"))
        flat.append(mo)
    p.append(span("CN_Kb", -0.25, 0.25, 0.75, 0.765, -0.28, -0.12, M["black"], 0.006))
    p.append(span("CN_Mouse", 0.34, 0.39, 0.75, 0.77, -0.24, -0.17, M["black"], 0.012))
    p.append(cyl_between("CN_Led", (0.62, 0.7, -0.371), (0.62, 0.7, -0.375), 0.006, M["led_g"], 8))
    for o in p:
        finish(o, 1.0, angle=40)
    return [join(p + flat, "SystemRoom_Console")]


# ============================== 資料 ==============================

def devlog(M):
    """開いたリング綴じの記録簿（黒い表紙）"""
    p = [finish(span("DV_Cover", -0.17, 0.17, -0.01, -0.006, -0.12, 0.12, M["binder"], 0.003), 2.0)]
    for k in range(3):
        rg = lathe(f"DV_Ring{k}", (0, 0, 0), [(0.012, 0.0), (0.012, 0.004)], M["chrome"], 12, cap_top=False, cap_bottom=False)
        rg.data.transform(Matrix.Translation(U(0, -0.002, -0.08 + k * 0.08)))
        p.append(finish(rg, 2.0))
    q = quad("DV_Pages", [(-0.166, -0.005, -0.116), (0.166, -0.005, -0.116), (0.166, -0.005, 0.116), (-0.166, -0.005, 0.116)], M["devlog"])
    hq.face_toward(q, (0, 1, 0))
    one = join(p + [q], "SystemRoom_DevLog")
    one.data.transform(Matrix.Rotation(math.radians(5), 4, "Z"))
    return [one]


def gaplog(M):
    """握り潰された跡のある照合ログ（波打つ紙）"""
    w, d = 0.2, 0.143
    rnd = random.Random(2021)
    noise = [[rnd.uniform(-1, 1) for _ in range(9)] for _ in range(9)]

    def fn(u, v):
        x = (u - 0.5) * w
        z = (v - 0.5) * d
        # 机の天板（箱の底 -0.01）より下へは沈めない（めり込むとまだらに見える）
        y = -0.0092 + 0.0035 * (noise[int(u * 8)][int(v * 8)] + 1.0) * (0.3 + u) / 1.3
        return (x, y, z)
    o = hq.grid_surface("GP_Sheet", 16, 12, fn, M["gaplog"])
    me = o.data
    uvl = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = ((-co.x + w / 2) / w, (-co.y + d / 2) / d)
    s = o.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.0008
    finish(o, keep_uv=True, angle=70)
    _outward(o)
    o.data.transform(Matrix.Rotation(math.radians(-14), 4, "Z"))
    return [o]


# ============================== 扉・配電盤 ==============================

def door(M):
    Mc = {"steel": M["steelplate"], "dark": M["metal"], "glass": M["wglass"], "chrome": M["chrome"], "hazard": M["hazard"]}
    objs = core_ante_room.door(Mc)
    return [join(objs, "SystemRoom_Door")]


def _breaker_mats(M):
    return {"mel": M["metalL"], "grille": M["black"], "hazard": M["hazard"], "sus": M["chrome"], "rubber": M["black"], "lever": M["lever"]}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.01), "SystemRoom_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "SystemRoom_Lever")]


PIECES = {"Shell": shell, "Core": core, "Console": console, "DevLog": devlog, "GapLog": gaplog,
          "Door": door, "Breaker": breaker, "Lever": lever}
