"""
息子の部屋（son_room：幅4.5 x 奥行5 x 天井2.4）を品質重視で作る。
誰の記憶でもない、祈りで作られた明るい子供部屋：白い木の床・空色に雲と星の壁紙・白い窓枠と薄いカーテン・
窓から差す白い光の筋・朝の空・小さな木のベッド（星と月の掛け布団・枕・犬のぬいぐるみ）・天井のモビール・
道路の遊びマットと木の汽車・積み木・おもちゃの棚（絵本・ロボット・ボール・車）・小さな椅子と黒いランドセル・
学習机（棚・スタンド・地球儀・揃えた小さな運動靴・クレヨン）・壁のクレヨン画・身長計。

  Shell      部屋の原点。床・壁・天井・照明・窓・カーテン・光の筋・外の景色・壁の絵・身長計
  Interior   部屋の原点。ベッド・モビール・マットと汽車・積み木・棚・椅子とランドセル
  Desk       学習机（ユニットの原点 = ビルダーの Desk、天板の上面 0.75、座る側 = +Z）
  Plan       資料の見た目（箱の中心が原点）
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

W, D, H = 4.5, 5.0, 2.4
HW0, HD0 = W / 2, D / 2
HW, HD = HW0 - 0.075, HD0 - 0.075
DOOR_HALF, DOOR_H = 0.55, 2.1
WIN = (0.85, 1.95, 0.95, 1.95)                 # 東の窓（z0, z1, y0, y1）
BED = (-HW0 + 0.8, 0.8)                        # 小さなベッド（頭 = 北）
DESK = (HW0 - 1.0, -1.2)


def mats():
    M = {}
    M["floor"] = hq.mat("SON_Floor", (1, 1, 1), 0.45, tex="floor.png")
    M["wall"] = hq.mat("SON_Wallpaper", (1, 1, 1), 0.85, tex="wallpaper.png")
    M["ceil"] = hq.mat("SON_Ceiling", (0.97, 0.97, 0.96), 0.9)
    M["white"] = hq.mat("SON_WhiteWood", (0.95, 0.94, 0.9), 0.4)
    M["wood"] = hq.mat("SON_Wood", (1.2, 1.15, 1.05), 0.45, tex="../../lab/tex/oak.png")
    M["quilt"] = hq.mat("SON_Quilt", (1, 1, 1), 0.9, tex="quilt.png")
    M["pillow"] = hq.mat("SON_Pillow", (0.96, 0.95, 0.92), 0.9)
    M["dog"] = hq.mat("SON_PlushDog", (0.85, 0.75, 0.6), 0.95)
    M["curtain"] = hq.mat("SON_Curtain", (1, 1, 1), 0.9, tex="curtain.png")
    M["sky"] = hq.mat("SON_SkyMorning", (1, 1, 1), 0.9, tex="sky_morning.png", emit=(1, 1, 1), emit_strength=1.2)
    M["glass"] = hq.mat("SON_WindowGlass", (0.8, 0.82, 0.85), 0.05, alpha=0.1)
    M["beam"] = hq.mat("SON_LightBeam", (1, 1, 0.97), 0.9, emit=(1, 0.98, 0.92), emit_strength=0.6, alpha=0.08)
    M["lamp"] = hq.mat("SON_CeilingLight", (1.0, 0.98, 0.94), 0.3, emit=(1.0, 0.96, 0.88), emit_strength=3.0)
    M["mat"] = hq.mat("SON_Playmat", (1, 1, 1), 0.95, tex="playmat.png")
    M["drawings"] = hq.mat("SON_Drawings", (1, 1, 1), 0.9, tex="drawings.png")
    M["chart"] = hq.mat("SON_HeightChart", (1, 1, 1), 0.6, tex="height_chart.png")
    M["plan"] = hq.mat("SON_Plan", (1, 1, 1), 0.9, tex="plan.png")
    M["red"] = hq.mat("SON_ToyRed", (0.8, 0.22, 0.2), 0.35)
    M["blue"] = hq.mat("SON_ToyBlue", (0.2, 0.42, 0.8), 0.35)
    M["yellow"] = hq.mat("SON_ToyYellow", (0.92, 0.76, 0.2), 0.35)
    M["green"] = hq.mat("SON_ToyGreen", (0.3, 0.65, 0.35), 0.35)
    M["grey"] = hq.mat("SON_ToyGrey", (0.62, 0.64, 0.68), 0.35, 0.4)
    M["black"] = hq.mat("SON_Black", (0.03, 0.03, 0.035), 0.5)
    M["randoseru"] = hq.mat("SON_Randoseru", (0.05, 0.05, 0.06), 0.3)
    M["shoe"] = hq.mat("SON_ShoeRed", (0.75, 0.3, 0.25), 0.4)
    M["sole"] = hq.mat("SON_ShoeSole", (0.94, 0.93, 0.9), 0.6)
    M["globe"] = hq.mat("SON_Globe", (0.3, 0.55, 0.8), 0.3)
    M["string"] = hq.mat("SON_String", (0.9, 0.9, 0.9), 0.8)
    M["paper"] = hq.mat("SON_PaperPlain", (0.94, 0.93, 0.9), 0.9)
    M["alu"] = hq.mat("SON_Aluminum", (0.8, 0.81, 0.82), 0.3, 1.0)
    M["lever"] = hq.mat("SON_LeverRed", (0.75, 0.12, 0.1), 0.45)
    M["hazard"] = hq.mat("SON_Hazard", (1, 1, 1), 0.5, tex="../../lab/tex/hazard.png")
    return M


def _outward(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()


def _cloth(name, fn, nu, nv, mat, uvs=2.0, thick=0.003, uvmode="zy"):
    o = hq.grid_surface(name, nu, nv, fn, mat)
    me = o.data
    uvl = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = (-co.y * uvs, co.z * uvs) if uvmode == "zy" else (co.x * uvs, co.y * uvs + co.z * uvs * 0.5)
    s = o.modifiers.new("Solid", "SOLIDIFY"); s.thickness = thick
    finish(o, keep_uv=True, angle=60)
    _outward(o)
    return o


# ============================== 外殻 ==============================

def shell(M):
    out = []
    out.append(finish(span("NS_Floor", -HW0, HW0, -0.12, 0.0, -HD0, HD0, M["floor"], bev=0), 1.0, rot90=True))
    walls = []
    for zs in (-1, 1):
        walls += train_room.grid_wall(f"NS_WallNS{zs}", [(-DOOR_HALF, DOOR_HALF, 0.0, DOOR_H)], -HW0, HW0, 0.0, H,
                                      lambda a, b, c, d, zs=zs: (a, b, c, d, *sorted((zs * HD, zs * HD0))), M["wall"])
    walls.append(span("NS_WallW", -HW0, -HW, 0.0, H, -HD0, HD0, M["wall"], bev=0))
    walls += train_room.grid_wall("NS_WallE", [WIN], -HD0, HD0, 0.0, H, lambda a, b, c, d: (HW, HW0, c, d, a, b), M["wall"])
    out += [finish(o, 1 / 0.6, angle=30) for o in walls]
    out.append(finish(span("NS_Ceil", -HW0, HW0, H, H + 0.1, -HD0, HD0, M["ceil"], bev=0), 1.0))
    p = []
    for zs in (-1, 1):
        for (x0, x1) in ((-HW, -DOOR_HALF - 0.07), (DOOR_HALF + 0.07, HW)):
            p.append(span(f"NT_Base{zs}{x0:.0f}", x0, x1, 0, 0.07, *sorted((zs * HD, zs * (HD - 0.012))), M["white"], 0.003))
            p.append(span(f"NT_Crown{zs}{x0:.0f}", x0, x1, H - 0.035, H, *sorted((zs * HD, zs * (HD - 0.02))), M["white"], 0.004))
        zin = zs * HD
        for xs in (-1, 1):
            p.append(span(f"NT_Cas{zs}{xs}", *sorted((xs * DOOR_HALF, xs * (DOOR_HALF + 0.07))), 0, DOOR_H + 0.07, *sorted((zin, zin - zs * 0.018)), M["white"], 0.004))
            p.append(span(f"NT_Jamb{zs}{xs}", *sorted((xs * 0.46, xs * DOOR_HALF)), 0, DOOR_H, *sorted((zs * HD0, zs * HD)), M["white"], 0.002))
        p.append(span(f"NT_CasT{zs}", -DOOR_HALF - 0.07, DOOR_HALF + 0.07, DOOR_H, DOOR_H + 0.07, *sorted((zin, zin - zs * 0.018)), M["white"], 0.004))
        p.append(span(f"NT_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, *sorted((zs * HD0, zs * HD)), M["white"], 0.002))
    for xs in (-1, 1):
        a, b = sorted((xs * HW, xs * (HW - 0.012)))
        p.append(span(f"NT_BaseEW{xs}", a, b, 0, 0.07, -HD, HD, M["white"], 0.003))
        a2, b2 = sorted((xs * HW, xs * (HW - 0.02)))
        p.append(span(f"NT_CrownEW{xs}", a2, b2, H - 0.035, H, -HD, HD, M["white"], 0.004))
    # 天井の丸い照明（ビルダーの RoomLight の上）
    p.append(lathe("NT_Light", (0, H - 0.08, 0), [(0.0, 0.0), (0.16, 0.004), (0.21, 0.03), (0.22, 0.08)], M["lamp"], 48))
    out += [finish(o, 1.0, angle=40) for o in p]
    out += window(M)
    out += wall_items(M)
    return out


def window(M):
    """東の窓：白い木の枠と十字の桟・窓台・薄いカーテン（タッセルでまとめる）・光の筋・朝の空"""
    z0, z1, y0, y1 = WIN
    p, g = [], []
    xi, xo = HW, HW0
    for (a0, a1, b0, b1) in ((z0, z1, y0 - 0.02, y0), (z0, z1, y1, y1 + 0.02)):
        p.append(span("NW_Rev", xi, xo, b0, b1, a0, a1, M["white"], 0.002))
    for zz in (z0 - 0.02, z1):
        p.append(span("NW_RevS", xi, xo, y0 - 0.02, y1 + 0.02, zz, zz + 0.02, M["white"], 0.002))
    for (a0, a1, b0, b1) in ((z0 - 0.07, z0, y0 - 0.07, y1 + 0.07), (z1, z1 + 0.07, y0 - 0.07, y1 + 0.07), (z0 - 0.07, z1 + 0.07, y1, y1 + 0.07)):
        p.append(span("NW_Casing", xi - 0.018, xi, b0, b1, a0, a1, M["white"], 0.004))
    p.append(span("NW_Sill", xi - 0.1, xi, y0 - 0.03, y0, z0 - 0.1, z1 + 0.1, M["white"], 0.006, 3))
    xs = HW + 0.05
    zm, ym = (z0 + z1) / 2, (y0 + y1) / 2
    for (a0, a1, b0, b1) in ((z0, z1, y0, y0 + 0.045), (z0, z1, y1 - 0.045, y1), (z0, z0 + 0.045, y0, y1), (z1 - 0.045, z1, y0, y1),
                             (zm - 0.018, zm + 0.018, y0, y1), (z0, z1, ym - 0.018, ym + 0.018)):
        p.append(span("NW_Sash", xs - 0.018, xs + 0.018, b0, b1, a0, a1, M["white"], 0.004))
    q = quad("NW_Glass", [(xs, y0, z1), (xs, y0, z0), (xs, y1, z0), (xs, y1, z1)], M["glass"])
    hq.face_toward(q, (-1, 0, 0)); g.append(q)
    rx = HW - 0.08
    p.append(pipe("NW_Rod", [(rx, y1 + 0.12, z0 - 0.35), (rx, y1 + 0.12, z1 + 0.35)], 0.01, M["wood"], 0.02, 10))
    for zz in (z0 - 0.36, z1 + 0.36):
        p.append(lathe(f"NW_Finial{zz:.1f}", (rx, y1 + 0.1, zz), [(0.0, 0.0), (0.02, 0.005), (0.02, 0.035), (0.0, 0.04)], M["wood"], 16))
    out = [finish(o, 1.0, angle=40) for o in p] + g
    # 薄いカーテン：両脇でタッセルにまとめ、裾が広がる
    for k, (zc, sgn) in enumerate(((z0 - 0.18, -1), (z1 + 0.18, 1))):
        def fn(u, v, zc=zc, sgn=sgn):
            y = 0.35 + v * (y1 + 0.1 - 0.35)
            pinch = 1.0 - 0.65 * math.exp(-((y - 1.25) / 0.18) ** 2)     # タッセルでくびれる
            wid = 0.32 * pinch * (1.0 + 0.3 * (1 - v))
            z = zc + (u - 0.5) * wid + sgn * 0.04 * (1 - v)
            x = rx - 0.02 + 0.03 * math.sin(u * math.pi * 8) * pinch
            return (x, y, z)
        out.append(_cloth(f"NW_Curtain{k}", fn, 40, 20, M["curtain"]))
        p2 = finish(lathe(f"NW_Tassel{k}", (rx - 0.03, 1.2, zc), [(0.0, 0.0), (0.03, 0.02), (0.035, 0.08), (0.02, 0.1), (0.0, 0.11)], M["yellow"], 16), 2.0)
        out.append(p2)
    # 窓から床へ差す光の筋（半透明の板2枚）
    for k, off in enumerate((-0.25, 0.25)):
        zc = (z0 + z1) / 2 + off
        a = (HW - 0.02, y1 - 0.1, zc - 0.18)
        b = (HW - 0.02, y1 - 0.1, zc + 0.18)
        c = (HW - 1.6, 0.02, zc + 0.3)
        d = (HW - 1.6, 0.02, zc - 0.3)
        bq = quad(f"NW_Beam{k}", [a, b, c, d], M["beam"])
        out.append(bq)
    # 外の朝の空（4m 外）
    xk = HW0 + 4.0
    sky = quad("NW_Sky", [(xk, -0.8, 9.0), (xk, -0.8, -7.0), (xk, 4.2, -7.0), (xk, 4.2, 9.0)], M["sky"])
    hq.face_toward(sky, (-1, 0, 0))
    out.append(sky)
    return out


def wall_items(M):
    """北の壁のクレヨン画3枚（ビルダーの Drawing：x -1.7 + 0.45i、y 1.5/1.6/1.5）と西の壁の身長計"""
    out, p = [], []
    zn = HD - 0.003
    for i in range(3):
        x, y = -1.7 + i * 0.45, 1.5 + (i % 2) * 0.1
        u0 = i / 3
        # 見る人（北の壁を +Z に見る）の左は -X
        q = quad(f"ND_Draw{i}", [(x - 0.15, y - 0.12, zn), (x + 0.15, y - 0.12, zn), (x + 0.15, y + 0.12, zn), (x - 0.15, y + 0.12, zn)], M["drawings"],
                 uv=((u0, 0), (u0 + 1 / 3, 0), (u0 + 1 / 3, 1), (u0, 1)))
        hq.face_toward(q, (0, 0, -1))
        q.data.transform(Matrix.Translation(U(x, y, zn)) @ Matrix.Rotation(math.radians((i - 1) * 3), 4, "Y") @ Matrix.Translation(-U(x, y, zn)))
        out.append(q)
        for sx in (-1, 1):
            p.append(span(f"ND_Tape{i}{sx}", x + sx * 0.13 - 0.025, x + sx * 0.13 + 0.025, y + 0.1, y + 0.13, zn - 0.001, zn, M["paper"], 0))
    xw = -HW + 0.003
    # 見る人（西の壁を -X に見る）の左は -Z
    ch = quad("ND_Chart", [(xw, 0.55, 1.85), (xw, 0.55, 2.05), (xw, 1.8, 2.05), (xw, 1.8, 1.85)], M["chart"])
    hq.face_toward(ch, (1, 0, 0))
    out.append(ch)
    return out + [finish(o, 2.0) for o in p]


# ============================== 家具とおもちゃ ==============================

def bed(M):
    """小さな木のベッド（ビルダーの SmallBed：0.8 x 1.5、頭 = 北）と星の掛け布団・枕・犬のぬいぐるみ"""
    cx, cz = BED
    p = []
    p.append(span("NB_Frame", cx - 0.4, cx + 0.4, 0.12, 0.3, cz - 0.75, cz + 0.75, M["wood"], 0.012))
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(span(f"NB_Leg{sx}{sz}", cx + sx * 0.37 - 0.03, cx + sx * 0.37 + 0.03, 0.0, 0.12, cz + sz * 0.72 - 0.03, cz + sz * 0.72 + 0.03, M["wood"], 0.006))
    # 丸い頭板と足板（profile を X に押し出して）
    for zs, top in ((1, 0.85), (-1, 0.6)):
        prof = [(cx - 0.42, 0.1), (cx + 0.42, 0.1)] + [(cx + 0.42 * math.cos(math.pi * k / 16), top - 0.12 + 0.12 * math.sin(math.pi * k / 16)) for k in range(17)]
        bd = profile_z(f"NB_Board{zs}", prof, cz + zs * 0.75, cz + zs * 0.79, M["white"])
        p.append(bd)
    p.append(span("NB_Mattress", cx - 0.38, cx + 0.38, 0.3, 0.44, cz - 0.73, cz + 0.73, M["pillow"], 0.035, 4))
    out = [finish(o, 1.0, angle=40) for o in p]

    def qf(u, v):
        x = (u - 0.5) * 0.9
        z = cz - 0.72 + v * 1.1
        ax = abs(x)
        wav = 0.01 * math.sin(v * 8 + u * 3) * math.sin(u * math.pi)
        if ax < 0.37:
            y = 0.47 + 0.025 * math.sin(u * math.pi) + wav
        else:
            t = min(1.0, (ax - 0.37) / 0.08)
            y = 0.47 - 0.26 * t ** 1.1 + wav
            x = math.copysign(0.37 + 0.04 * math.sin(t * math.pi / 2) + 0.02, x)
        if v > 0.92:
            y += 0.03 * (v - 0.92) / 0.08                    # 上端の折り返し
        return (cx + x, y, z)
    out.append(_cloth("NB_Quilt", qf, 30, 30, M["quilt"], thick=0.03, uvmode="top"))
    out.append(finish(pillow("NB_Pillow", (cx, 0.5, cz + 0.55), (0.46, 0.11, 0.28), M["pillow"], seed=2801), 2.0, angle=60))
    # 犬のぬいぐるみ（枕の脇）
    dx, dz = cx + 0.22, cz + 0.4
    dog = [pillow("NB_DogBody", (dx, 0.53, dz), (0.16, 0.12, 0.2), M["dog"], seed=2802),
           lathe("NB_DogHead", (dx, 0.56, dz - 0.12), [(0.0, 0.0), (0.05, 0.01), (0.06, 0.05), (0.045, 0.09), (0.0, 0.1)], M["dog"], 20)]
    for s in (-1, 1):
        ear = pillow(f"NB_DogEar{s}", (dx + s * 0.055, 0.62, dz - 0.12), (0.03, 0.02, 0.07), M["black"], seed=2803)
        dog.append(ear)
        dog.append(cyl_between(f"NB_DogEye{s}", (dx + s * 0.02, 0.62, dz - 0.175), (dx + s * 0.02, 0.62, dz - 0.18), 0.006, M["black"], 8))
    dog.append(cyl_between("NB_DogNose", (dx, 0.6, dz - 0.18), (dx, 0.6, dz - 0.19), 0.01, M["black"], 8))
    out += [finish(o, 2.0, angle=60) for o in dog]
    # 天井のモビール（ベッドの上：輪と、星・月・飛行機）
    mx, mz = cx + 0.1, cz + 0.2
    mob = [cyl_between("NM_String", (mx, H, mz), (mx, 1.9, mz), 0.002, M["string"], 6)]
    ring = lathe("NM_Ring", (0, 0, 0), [(0.22 + 0.006 * math.cos(t / 6 * math.pi * 2), 0.006 * math.sin(t / 6 * math.pi * 2)) for t in range(7)], M["wood"], 32, cap_top=False, cap_bottom=False)
    ring.data.transform(Matrix.Translation(U(mx, 1.9, mz)))
    mob.append(ring)
    for k, m in enumerate((M["yellow"], M["blue"], M["red"], M["yellow"], M["green"])):
        a = math.pi * 2 * k / 5
        hx, hz = mx + math.cos(a) * 0.22, mz + math.sin(a) * 0.22
        hy = 1.9 - 0.12 - (k % 2) * 0.08
        mob.append(cyl_between(f"NM_Hang{k}", (hx, 1.9, hz), (hx, hy + 0.04, hz), 0.0015, M["string"], 4))
        if k % 2 == 0:
            prof = [(0.045 * math.cos(-math.pi / 2 + j * math.pi / 5) * (1 if j % 2 == 0 else 0.45), 0.045 * math.sin(-math.pi / 2 + j * math.pi / 5) * (1 if j % 2 == 0 else 0.45)) for j in range(10)]
            st = profile_z(f"NM_Star{k}", [(hx + a_, hy + b_) for a_, b_ in prof], hz - 0.008, hz + 0.008, m)
            mob.append(st)
        else:
            mob.append(span(f"NM_PlaneBody{k}", hx - 0.05, hx + 0.05, hy - 0.012, hy + 0.012, hz - 0.012, hz + 0.012, m, 0.01))
            mob.append(span(f"NM_PlaneWing{k}", hx - 0.01, hx + 0.02, hy - 0.004, hy + 0.004, hz - 0.06, hz + 0.06, m, 0.003))
    out += [finish(o, 2.0, angle=50) for o in mob]
    return out


def floor_toys(M):
    """遊びのマット（ビルダーの Rug：(0.2, *, 0.2)、1.8 x 1.6）・木の汽車・積み木3つ（(0.3 + 0.18i, *, -0.6)）"""
    out = []
    rug = quad("NF_Mat", [(-0.7, 0.008, -0.6), (1.1, 0.008, -0.6), (1.1, 0.008, 1.0), (-0.7, 0.008, 1.0)], M["mat"])
    hq.face_toward(rug, (0, 1, 0))
    s = rug.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.008
    out.append(finish(rug, keep_uv=True, angle=40))
    p = []
    # 木の汽車（マットの道路の上、3両）
    tx, tz = -0.25, 0.47
    for k, (m, L) in enumerate(((M["red"], 0.16), (M["blue"], 0.14), (M["yellow"], 0.14))):
        x0 = tx + k * 0.19
        p.append(span(f"NF_Car{k}", x0, x0 + L, 0.03, 0.09, tz - 0.045, tz + 0.045, m, 0.01, 3))
        if k == 0:
            p.append(span("NF_Cab", x0, x0 + 0.06, 0.09, 0.15, tz - 0.045, tz + 0.045, m, 0.008, 3))
            p.append(cyl("NF_Chimney", (x0 + 0.12, 0.09, tz), 0.05, 0.018, M["black"], 12))
        for wx in (x0 + 0.03, x0 + L - 0.03):
            for s_ in (-1, 1):
                p.append(cyl_between(f"NF_Wheel{k}{wx:.2f}{s_}", (wx, 0.028, tz + s_ * 0.045), (wx, 0.028, tz + s_ * 0.055), 0.022, M["black"], 16))
        if k > 0:
            p.append(cyl_between(f"NF_Link{k}", (x0 - 0.03, 0.05, tz), (x0, 0.05, tz), 0.006, M["black"], 6))
    # 積み木（ビルダーの ToyBlock）
    for i, m in enumerate((M["red"], M["blue"], M["yellow"])):
        bx = 0.3 + i * 0.18
        rot = (i - 1) * 0.3
        b = span(f"NF_Block{i}", bx - 0.06, bx + 0.06, 0.0, 0.12, -0.66, -0.54, m, 0.012, 3)
        b.data.transform(Matrix.Translation(U(bx, 0, -0.6)) @ Matrix.Rotation(rot, 4, "Z") @ Matrix.Translation(-U(bx, 0, -0.6)))
        p.append(b)
    p.append(lathe("NF_Arch", (0.52, 0.12, -0.6), [(0.0, 0.0), (0.05, 0.0), (0.05, 0.05), (0.0, 0.08)], M["green"], 24))
    # ボール
    p.append(lathe("NF_Ball", (-0.4, 0.0, -0.2), [(0.1 * math.sin(math.pi * k / 12), 0.1 * (1 - math.cos(math.pi * k / 12))) for k in range(13)], M["red"], 32))
    return out + [finish(o, 2.0, angle=45) for o in p]


def shelf(M):
    """おもちゃの棚（ビルダーの ToyShelf：(-hw+0.3, *, -1.2)、0.35 x 1.2 x 1.0、前 = +X）"""
    cx, cz = -HW0 + 0.3, -1.2
    p = []
    x0, x1 = cx - 0.175, cx + 0.175
    for zz in (cz - 0.5, cz + 0.48):
        p.append(span("NS_Side", x0, x1, 0.0, 1.2, zz, zz + 0.02, M["white"], 0.006))
    p.append(span("NS_Back", x0, x0 + 0.012, 0.02, 1.2, cz - 0.48, cz + 0.48, M["white"], 0.003))
    for y in (0.0, 0.4, 0.8, 1.18):
        p.append(span(f"NS_Board{y}", x0, x1, y, y + 0.02, cz - 0.48, cz + 0.48, M["white"], 0.006))
    # 下段：絵本（背を前に）
    rnd = random.Random(2811)
    z = cz - 0.46
    cols = [M["red"], M["blue"], M["yellow"], M["green"], M["pillow"]]
    while z < cz - 0.05:
        w = rnd.uniform(0.012, 0.03); hh = rnd.uniform(0.22, 0.32)
        p.append(span(f"NS_Book{z:.2f}", x0 + 0.03, x0 + 0.29, 0.02, 0.02 + hh, z, z + w, rnd.choice(cols), 0.004))
        z += w + 0.002
    # 下段の右：ボールとバケツ
    p.append(lathe("NS_Bucket", (cx, 0.02, cz + 0.25), [(0.0, 0.0), (0.09, 0.0), (0.11, 0.18), (0.105, 0.18), (0.085, 0.01)], M["blue"], 24, cap_top=False))
    # 中段：ロボット（箱を組む）と恐竜（簡単な形）
    rx, rz = cx + 0.02, cz - 0.2
    p += [span("NS_RobotBody", rx - 0.06, rx + 0.06, 0.48, 0.62, rz - 0.07, rz + 0.07, M["grey"], 0.01),
          span("NS_RobotHead", rx - 0.045, rx + 0.045, 0.62, 0.7, rz - 0.05, rz + 0.05, M["grey"], 0.01),
          span("NS_RobotLegs", rx - 0.05, rx + 0.05, 0.42, 0.48, rz - 0.05, rz + 0.05, M["black"], 0.006),
          cyl_between("NS_RobotAnt", (rx, 0.7, rz), (rx, 0.76, rz), 0.004, M["red"], 6)]
    for s in (-1, 1):
        p.append(cyl_between(f"NS_RobotEye{s}", (rx + 0.046, 0.665, rz + s * 0.02), (rx + 0.05, 0.665, rz + s * 0.02), 0.01, M["yellow"], 10))
    dx, dz = cx + 0.02, cz + 0.2
    p += [pillow("NS_DinoBody", (dx, 0.5, dz), (0.1, 0.1, 0.2), M["green"], seed=2812),
          cyl_between("NS_DinoNeck", (dx, 0.52, dz - 0.07), (dx, 0.66, dz - 0.14), 0.025, M["green"], 10, r1=0.018),
          cyl_between("NS_DinoTail", (dx, 0.5, dz + 0.08), (dx, 0.45, dz + 0.22), 0.025, M["green"], 10, r1=0.005)]
    for s in (-1, 1):
        for zz in (dz - 0.05, dz + 0.05):
            p.append(cyl(f"NS_DinoLeg{s}{zz:.2f}", (dx + s * 0.03, 0.42, zz), 0.06, 0.016, M["green"], 8))
    # 上段：車とブロックの箱
    p.append(span("NS_CarBody", cx - 0.05, cx + 0.05, 0.84, 0.9, cz - 0.3, cz - 0.14, M["red"], 0.012))
    p.append(span("NS_CarTop", cx - 0.04, cx + 0.04, 0.9, 0.94, cz - 0.26, cz - 0.18, M["red"], 0.01))
    for zz in (cz - 0.27, cz - 0.17):
        for s in (-1, 1):
            p.append(cyl_between(f"NS_CarWheel{zz:.2f}{s}", (cx + s * 0.05, 0.84, zz), (cx + s * 0.06, 0.84, zz), 0.018, M["black"], 12))
    p.append(span("NS_BlockBox", x0 + 0.03, x1 - 0.02, 0.82, 1.02, cz + 0.05, cz + 0.4, M["yellow"], 0.008))
    return [finish(o, 1.0, angle=45) for o in p]


def chair(M):
    """小さな木の椅子（ビルダーの SmallChair：(hw-1.0, *, -0.5)、座 0.3）と、背に掛けた黒いランドセル"""
    cx, cz = HW0 - 1.0, -0.5
    p = []
    p.append(span("NC_Seat", cx - 0.16, cx + 0.16, 0.28, 0.31, cz - 0.15, cz + 0.15, M["wood"], 0.008, 3))
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(dim_room.taper_leg(f"NC_Leg{sx}{sz}", cx + sx * 0.13, cz + sz * 0.12, 0.0, 0.28, 0.03, 0.025, M["wood"]))
    # 背（北側 = +Z、机は南）
    for sx in (-1, 1):
        p.append(span(f"NC_Post{sx}", cx + sx * 0.13 - 0.014, cx + sx * 0.13 + 0.014, 0.28, 0.6, cz + 0.12, cz + 0.145, M["wood"], 0.004))
    p.append(span("NC_BackRail", cx - 0.15, cx + 0.15, 0.48, 0.6, cz + 0.12, cz + 0.145, M["wood"], 0.01))
    # ランドセル（背もたれの後ろに掛ける）
    rz = cz + 0.24
    p.append(span("NC_Rand", cx - 0.12, cx + 0.12, 0.28, 0.58, rz - 0.07, rz + 0.08, M["randoseru"], 0.05, 4))
    p.append(span("NC_RandFlap", cx - 0.125, cx + 0.125, 0.36, 0.59, rz + 0.08, rz + 0.1, M["randoseru"], 0.04, 4))
    p.append(span("NC_RandClasp", cx - 0.02, cx + 0.02, 0.37, 0.41, rz + 0.1, rz + 0.106, M["alu"], 0.004))
    for s in (-1, 1):
        p.append(pipe(f"NC_RandStrap{s}", [(cx + s * 0.08, 0.58, rz - 0.07), (cx + s * 0.1, 0.62, cz + 0.13), (cx + s * 0.1, 0.5, cz + 0.12)], 0.012, M["randoseru"], 0.03, 6))
    return [finish(o, 1.0, angle=45) for o in p]


def interior(M):
    return bed(M) + floor_toys(M) + shelf(M) + chair(M)


# ============================== 学習机 ==============================

def desk(M):
    """学習机（原点 = ビルダーの Desk：天板 1.4 x 0.7・上面 0.75。座る側 = +Z（北）、棚は南の奥）"""
    p, flat = [], []
    p.append(span("KD_Top", -0.7, 0.7, 0.72, 0.75, -0.35, 0.35, M["wood"], 0.01, 3))
    for sx in (-1, 1):
        p.append(span(f"KD_Side{sx}", *sorted((sx * 0.66, sx * 0.7)), 0.0, 0.72, -0.33, 0.33, M["white"], 0.006))
    p.append(span("KD_Modesty", -0.66, 0.66, 0.3, 0.72, -0.33, -0.31, M["white"], 0.004))
    p.append(span("KD_Drawer", -0.3, 0.3, 0.62, 0.715, 0.3, 0.33, M["white"], 0.006))
    p.append(span("KD_Pull", -0.08, 0.08, 0.655, 0.675, 0.33, 0.345, M["yellow"], 0.006))
    # 棚（奥、天板の上 0.5）
    p.append(span("KD_HutchBack", -0.68, 0.68, 0.75, 1.25, -0.35, -0.33, M["white"], 0.004))
    for sx in (-1, 1):
        p.append(span(f"KD_HutchSide{sx}", *sorted((sx * 0.66, sx * 0.68)), 0.75, 1.25, -0.35, -0.12, M["white"], 0.004))
    p.append(span("KD_HutchShelf", -0.66, 0.66, 1.0, 1.02, -0.33, -0.12, M["white"], 0.004))
    p.append(span("KD_HutchTop", -0.7, 0.7, 1.25, 1.27, -0.36, -0.1, M["wood"], 0.006))
    # 棚の上：地球儀と絵本
    gx, gz = 0.45, -0.22
    p.append(lathe("KD_GlobeBase", (gx, 1.27, gz), [(0.06, 0.0), (0.06, 0.015), (0.015, 0.03)], M["wood"], 20))
    p.append(lathe("KD_Globe", (gx, 1.31, gz), [(0.1 * math.sin(math.pi * k / 12), 0.1 * (1 - math.cos(math.pi * k / 12))) for k in range(13)], M["globe"], 32))
    arc = pipe("KD_GlobeArc", [(gx, 1.3, gz + 0.11), (gx, 1.41, gz + 0.11), (gx, 1.52, gz)], 0.004, M["yellow"], 0.08, 6)
    p.append(arc)
    for k in range(5):
        x = -0.6 + k * 0.035
        p.append(span(f"KD_HBook{k}", x, x + 0.03, 1.02, 1.22 + (k % 2) * 0.02, -0.32, -0.15, [M["red"], M["blue"], M["green"], M["yellow"], M["red"]][k], 0.004))
    # スタンド（アーム）
    lx, lz = -0.55, -0.2
    p.append(lathe("KD_LampBase", (lx, 0.75, lz), [(0.06, 0), (0.06, 0.02), (0.015, 0.03)], M["blue"], 20))
    p.append(pipe("KD_LampArm", [(lx, 0.78, lz), (lx + 0.02, 1.0, lz - 0.02), (lx + 0.18, 1.1, lz + 0.05)], 0.008, M["blue"], 0.03, 8))
    shade = lathe("KD_LampShade", (0, 0, 0), [(0.0, 0.0), (0.02, 0.0), (0.06, 0.08), (0.056, 0.08)], M["blue"], 20, cap_top=False)
    shade.data.transform(Matrix.Translation(U(lx + 0.2, 1.1, lz + 0.06)) @ Matrix.Rotation(math.radians(200), 4, "X"))
    p.append(shade)
    # 揃えた小さな運動靴（ビルダーの SmallShoe：(-0.35 ± 0.08, 0.78, 0.1)、つま先 = 北）
    for dx in (-0.08, 0.08):
        sx_, sz_ = -0.35 + dx, 0.1
        sole = profile_z(f"KD_Sole{dx}", [(sx_ - 0.035, 0.75), (sx_ + 0.035, 0.75), (sx_ + 0.035, 0.765), (sx_ - 0.035, 0.765)], sz_ - 0.085, sz_ + 0.085, M["sole"])
        p.append(sole)
        up = pillow(f"KD_Upper{dx}", (sx_, 0.79, sz_ - 0.01), (0.065, 0.05, 0.15), M["shoe"], seed=2821)
        p.append(up)
        p.append(lathe(f"KD_Toe{dx}", (sx_, 0.765, sz_ + 0.06), [(0.0, 0.0), (0.03, 0.0), (0.028, 0.02), (0.0, 0.03)], M["sole"], 16))
        p.append(span(f"KD_Velcro{dx}", sx_ - 0.034, sx_ + 0.034, 0.8, 0.808, sz_ - 0.01, sz_ + 0.02, M["pillow"], 0.003))
    # クレヨンの箱
    cxb, czb = 0.05, -0.02
    p.append(span("KD_CrayonBox", cxb - 0.08, cxb + 0.08, 0.75, 0.765, czb - 0.05, czb + 0.05, M["yellow"], 0.004))
    for k, m in enumerate((M["red"], M["blue"], M["green"], M["yellow"], M["black"])):
        x = cxb - 0.06 + k * 0.03
        p.append(cyl_between(f"KD_Crayon{k}", (x, 0.772, czb - 0.04), (x, 0.772, czb + 0.04), 0.005, m, 8))
    for o in p:
        finish(o, 1.0, angle=45)
    return [join(p + flat, "SonRoom_Desk")]


def plan(M):
    """書きかけの治療計画書（古びた紙、端が少し丸まる）"""
    w, d = 0.21, 0.297
    def fn(u, v):
        x = (u - 0.5) * w
        z = (v - 0.5) * d
        y = -0.009 + 0.006 * max(0.0, u - 0.85) / 0.15 + 0.004 * max(0.0, 0.1 - v) / 0.1
        return (x, y, z)
    o = hq.grid_surface("PL_Sheet", 12, 16, fn, M["plan"])
    me = o.data
    uvl = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = ((-co.x + w / 2) / w, (-co.y + d / 2) / d)
    s = o.modifiers.new("Solid", "SOLIDIFY"); s.thickness = 0.0008
    finish(o, keep_uv=True, angle=70)
    _outward(o)
    # 座る側（北 = +Z）から読める向き：文字の上 = -Z
    o.data.transform(Matrix.Rotation(math.radians(180 - 8), 4, "Z"))
    return [o]


def door(M):
    return [join(dim_room.door({"woodDark": M["white"], "wood": M["wood"], "brass": M["alu"]}), "SonRoom_Door")]


def _breaker_mats(M):
    return {"mel": M["white"], "grille": M["black"], "hazard": M["hazard"], "sus": M["alu"], "rubber": M["black"], "lever": M["lever"]}


def breaker(M):
    return [join(train_room.breaker(_breaker_mats(M), back=0.275, conduit_top=H - 0.02), "SonRoom_Breaker")]


def lever(M):
    return [join(train_room.lever(_breaker_mats(M)), "SonRoom_Lever")]


PIECES = {"Shell": shell, "Interior": interior, "Desk": desk, "Plan": plan, "Door": door, "Breaker": breaker, "Lever": lever}
