"""
薄暗い部屋（最初の部屋 dim：幅6 x 奥行7.5 x 天井2.6）を品質重視で作る。

各関数は「ビルダーのユニット原点」を原点にしたオブジェクトのリストを返す。
  Shell      部屋の原点（床の中心）
  Bed        ベッドのグループ原点（頭は -Z 側）
  Desk       机のグループ原点（天板の上面 y=0.75、手前は -Z）
  Lamp       ランプの原点（机の上面）
  Terminal   記録端末（画面は -Z 側）
  DollShelf  人形の棚（壁は +X 側 0.15 の位置）
  Doll       人形1体（足元が原点、正面 +Z）
  Nightstand / Chest / Wardrobe / Chair  追加の家具（床の中心が原点、正面 +Z）
  Flashlight / Notebook / Newspaper      机の上の資料（箱の中心が原点）
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import hq
from hq import U, box, span, lathe, cyl, cyl_between, pillow, finish, join

W, D, H = 6.0, 7.5, 2.6
HW, HD = W / 2, D / 2
FI = 0.075                       # 壁の内面（壁厚0.15の半分）
DOOR_HALF, DOOR_H = 0.55, 2.1


def mats():
    M = {}
    M["wall"] = hq.mat("DIM_Wallpaper", (1.0, 1.0, 1.0), 0.85, tex="wallpaper.png")
    M["carpet"] = hq.mat("DIM_Carpet", (1.0, 1.0, 1.0), 0.95, tex="carpet.png")
    M["ceil"] = hq.mat("DIM_Plaster", (0.95, 0.94, 0.92), 0.9, tex="plaster.png")
    M["wood"] = hq.mat("DIM_Walnut", (1.0, 1.0, 1.0), 0.45, tex="walnut.png")
    M["woodDark"] = hq.mat("DIM_WalnutDark", (0.7, 0.68, 0.66), 0.5, tex="walnut.png")
    M["trim"] = hq.mat("DIM_Trim", (0.6, 0.58, 0.56), 0.45, tex="walnut.png")
    M["quilt"] = hq.mat("DIM_Quilt", (1.0, 1.0, 1.0), 0.9, tex="quilt.png")
    M["linen"] = hq.mat("DIM_Linen", (1.0, 1.0, 1.0), 0.9, tex="linen.png")
    M["mattress"] = hq.mat("DIM_Mattress", (0.97, 0.97, 0.97), 0.9, tex="linen.png")
    M["brass"] = hq.mat("DIM_Brass", (0.78, 0.6, 0.32), 0.3, 1.0)
    M["shade"] = hq.mat("DIM_LampShade", (1.0, 0.95, 0.85), 0.8, tex="linen.png",
                        emit=(1.0, 0.8, 0.5), emit_strength=2.0)
    M["bulb"] = hq.mat("DIM_Bulb", (1, 0.95, 0.85), 0.2, emit=(1.0, 0.85, 0.6), emit_strength=8.0)
    M["black"] = hq.mat("DIM_BlackPlastic", (0.03, 0.03, 0.03), 0.4)
    M["beige"] = hq.mat("DIM_BeigePlastic", (0.70, 0.66, 0.56), 0.55)
    M["key"] = hq.mat("DIM_Keycap", (0.62, 0.58, 0.50), 0.5)
    M["screen"] = hq.mat("DIM_CrtScreen", (1.0, 1.0, 1.0), 0.15, tex="crt_screen.png",
                         emit=(0.3, 1.0, 0.45), emit_strength=1.5)
    M["porcelain"] = hq.mat("DIM_Porcelain", (0.94, 0.92, 0.88), 0.15)
    M["dress"] = hq.mat("DIM_DollDress", (0.32, 0.05, 0.07), 0.7)
    M["lace"] = hq.mat("DIM_Lace", (0.93, 0.91, 0.86), 0.8)
    M["hair"] = hq.mat("DIM_DollHair", (0.04, 0.03, 0.03), 0.35)
    M["eye"] = hq.mat("DIM_DollEye", (0.02, 0.02, 0.025), 0.05)
    M["lip"] = hq.mat("DIM_DollLip", (0.55, 0.12, 0.14), 0.4)
    M["metal"] = hq.mat("DIM_Metal", (0.55, 0.56, 0.58), 0.35, 1.0)
    M["rubber"] = hq.mat("DIM_Rubber", (0.05, 0.05, 0.05), 0.8)
    M["glass"] = hq.mat("DIM_Glass", (0.9, 0.9, 0.85), 0.05, emit=(1.0, 0.95, 0.85), emit_strength=0.6)
    M["frost"] = hq.mat("DIM_FrostGlass", (0.92, 0.9, 0.86), 0.3, emit=(1.0, 0.9, 0.75), emit_strength=1.5)
    M["cover"] = hq.mat("DIM_NoteCover", (0.10, 0.22, 0.16), 0.55)
    M["pages"] = hq.mat("DIM_Pages", (0.90, 0.88, 0.80), 0.9)
    M["ribbon"] = hq.mat("DIM_Ribbon", (0.60, 0.10, 0.10), 0.5)
    M["news"] = hq.mat("DIM_Newspaper", (1.0, 1.0, 1.0), 0.9, tex="newspaper.png")
    M["book1"] = hq.mat("DIM_Book1", (0.30, 0.10, 0.08), 0.6)
    M["book2"] = hq.mat("DIM_Book2", (0.12, 0.16, 0.24), 0.6)
    M["book3"] = hq.mat("DIM_Book3", (0.55, 0.48, 0.34), 0.6)
    M["stain"] = hq.mat("DIM_Stain", (1.0, 1.0, 1.0), 0.9, tex="stain.png", tex_alpha=True)
    M["plate"] = hq.mat("DIM_SwitchPlate", (0.88, 0.86, 0.80), 0.4)
    return M


# ============================== 共通の造形 ==============================

def taper_leg(name, cx, cz, y0, y1, w_top, w_bot, m, bev=0.003):
    """先細りの脚（上が太い）"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    vs = []
    for (y, w) in ((y0, w_bot), (y1, w_top)):
        h = w / 2
        vs.append([bm.verts.new(U(cx + dx * h, y, cz + dz * h)) for dx, dz in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
    b, t = vs
    bm.faces.new(list(reversed(b))); bm.faces.new(t)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((b[i], b[j], t[j], t[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    o = hq._obj(name, me, m)
    hq.bevel(o, bev, 2)
    return o


def plate_xy(name, pts, z0, z1, m, bev=0.003):
    """Unityのxy平面の多角形を z0〜z1 に押し出す（飾り金具・棚受けなど）"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    f = [bm.verts.new(U(x, y, z0)) for x, y in pts]
    b = [bm.verts.new(U(x, y, z1)) for x, y in pts]
    bm.faces.new(f); bm.faces.new(list(reversed(b)))
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((f[i], b[i], b[j], f[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    o = hq._obj(name, me, m)
    hq.bevel(o, bev, 2, 30)
    return o


def profile_run(name, prof, wall, s0, s1, m, hw=HW, hd=HD):
    """壁に沿って断面 prof=[(壁からの出, 高さ)...] を押し出す（巾木・回り縁）。wall=N/S/E/W"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()

    def P(d, y, s):
        if wall == "N": return U(s, y, hd - FI - d)
        if wall == "S": return U(s, y, -hd + FI + d)
        if wall == "E": return U(hw - FI - d, y, s)
        return U(-hw + FI + d, y, s)
    a = [bm.verts.new(P(d, y, s0)) for d, y in prof]
    b = [bm.verts.new(P(d, y, s1)) for d, y in prof]
    n = len(prof)
    for i in range(n - 1):
        bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
    bm.faces.new(a); bm.faces.new(list(reversed(b)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return hq._obj(name, me, m)


def knob(name, c, m, r=0.014, face="-z"):
    """小さな丸い取っ手（前面から突き出す）"""
    sgn = -1 if face == "-z" else 1
    p0 = (c[0], c[1], c[2])
    p1 = (c[0], c[1], c[2] + sgn * 0.012)
    p2 = (c[0], c[1], c[2] + sgn * 0.024)
    a = cyl_between(name + "_s", p0, p1, r * 0.45, m, 16)
    b = cyl_between(name + "_h", p1, p2, r, m, 20)
    hq.bevel(b, r * 0.35, 3, 30)
    return [a, b]


def drawer_front(name, x0, x1, y0, y1, z, m, knob_m, sgn=-1, handles=1):
    """引き出しの前板（縁を面取りした板＋取っ手）。sgn=-1で -Z 向き"""
    t = 0.02
    zz0, zz1 = (z - t, z) if sgn < 0 else (z, z + t)
    parts = [span(name, x0, x1, y0, y1, zz0, zz1, m, bev=0.006, segs=3)]
    cy = (y0 + y1) / 2
    zf = zz0 if sgn < 0 else zz1
    xs = [(x0 + x1) / 2] if handles == 1 else [x0 + (x1 - x0) * 0.25, x0 + (x1 - x0) * 0.75]
    for i, x in enumerate(xs):
        parts += knob(f"{name}_k{i}", (x, cy, zf), knob_m, face="-z" if sgn < 0 else "+z")
    return parts


def done(objs, uv=1.0, rot90=False, angle=35):
    for o in objs:
        finish(o, uv, rot90, angle)
    return objs


# ============================== 部屋の外殻 ==============================

def shell(M):
    out = []
    # 床（カーペット）・天井（漆喰）
    fl = span("Sh_Floor", -HW, HW, -0.12, 0.0, -HD, HD, M["carpet"], bev=0)
    ce = span("Sh_Ceil", -HW, HW, H, H + 0.12, -HD, HD, M["ceil"], bev=0)
    out += done([fl], 1.0) + done([ce], 0.5)
    # 壁（ビルダーの箱と同じ寸法。南北は扉開口）
    walls = []
    for zs in (1, -1):
        z = HD * zs
        seg = HW - DOOR_HALF
        cx = DOOR_HALF + seg / 2
        walls.append(box(f"Sh_WallA{zs}", (-cx, H / 2, z), (seg, H, 0.15), M["wall"], bev=0))
        walls.append(box(f"Sh_WallB{zs}", (cx, H / 2, z), (seg, H, 0.15), M["wall"], bev=0))
        walls.append(box(f"Sh_WallL{zs}", (0, (DOOR_H + H) / 2, z), (1.1, H - DOOR_H, 0.15), M["wall"], bev=0))
    walls.append(box("Sh_WallE", (HW, H / 2, 0), (0.15, H, D), M["wall"], bev=0))
    walls.append(box("Sh_WallW", (-HW, H / 2, 0), (0.15, H, D), M["wall"], bev=0))
    out += done(walls, 1.25)

    # 巾木（上端に丸面の付いた板）と回り縁（段付きの繰形）
    base_prof = [(0, 0), (0.016, 0), (0.016, 0.075), (0.013, 0.083), (0.009, 0.088), (0.004, 0.092), (0, 0.093)]
    crown_prof = [(0, H - 0.085), (0.004, H - 0.085), (0.006, H - 0.078), (0.014, H - 0.07), (0.022, H - 0.06),
                  (0.028, H - 0.05), (0.03, H - 0.035), (0.038, H - 0.028), (0.042, H - 0.012), (0.045, H), (0, H)]
    clear = DOOR_HALF + 0.085
    trims = []
    for wl in "NSEW":
        if wl in "NS":
            s0, s1 = -HW + FI, HW - FI
            trims.append(profile_run(f"Sh_Base{wl}a", base_prof, wl, s0, -clear, M["trim"]))
            trims.append(profile_run(f"Sh_Base{wl}b", base_prof, wl, clear, s1, M["trim"]))
        else:
            s0, s1 = -HD + FI, HD - FI
            trims.append(profile_run(f"Sh_Base{wl}", base_prof, wl, s0, s1, M["trim"]))
        trims.append(profile_run(f"Sh_Crown{wl}", crown_prof, wl, s0 if wl in "EW" else -HW + FI,
                                 s1 if wl in "EW" else HW - FI, M["ceil"]))
    # 扉の額縁（両側と上）と開口の縦枠（扉板との隙間を塞ぐ）
    for zs in (1, -1):
        zin = zs * (HD - FI)
        za, zb = sorted((zin, zin - zs * 0.02))
        cw = 0.085
        trims.append(span(f"Sh_CasL{zs}", -DOOR_HALF - cw, -DOOR_HALF, 0, DOOR_H + cw, za, zb, M["trim"], 0.005))
        trims.append(span(f"Sh_CasR{zs}", DOOR_HALF, DOOR_HALF + cw, 0, DOOR_H + cw, za, zb, M["trim"], 0.005))
        trims.append(span(f"Sh_CasT{zs}", -DOOR_HALF - cw - 0.01, DOOR_HALF + cw + 0.01, DOOR_H + cw - 0.005,
                          DOOR_H + cw + 0.03, *sorted((zin, zin - zs * 0.03)), M["trim"], 0.006))
        # 台座（額縁の足元のブロック）
        for sx in (-1, 1):
            x0, x1 = sorted((sx * DOOR_HALF, sx * (DOOR_HALF + cw + 0.006)))
            trims.append(span(f"Sh_Plinth{zs}{sx}", x0, x1, 0, 0.13, *sorted((zin, zin - zs * 0.026)), M["trim"], 0.004))
        z1, z2 = sorted((zs * (HD + FI), zs * (HD - FI)))
        trims.append(span(f"Sh_JambL{zs}", -DOOR_HALF, -0.46, 0, DOOR_H, z1, z2, M["trim"], 0.002))
        trims.append(span(f"Sh_JambR{zs}", 0.46, DOOR_HALF, 0, DOOR_H, z1, z2, M["trim"], 0.002))
        trims.append(span(f"Sh_JambT{zs}", -DOOR_HALF, DOOR_HALF, DOOR_H - 0.02, DOOR_H, z1, z2, M["trim"], 0.002))
    out += done(trims, 1.0, rot90=False, angle=50)

    # スイッチと コンセント（入口脇・ベッド脇）
    fx = []
    zS = -HD + FI
    fx.append(span("Sh_Switch", 0.78, 0.86, 1.16, 1.28, zS, zS + 0.008, M["plate"], 0.003))
    fx.append(span("Sh_SwitchKey", 0.805, 0.835, 1.2, 1.24, zS + 0.008, zS + 0.013, M["plate"], 0.002))
    xW = -HW + FI
    fx.append(span("Sh_Outlet", xW, xW + 0.008, 0.25, 0.37, -2.45, -2.37, M["plate"], 0.003))
    out += done(fx)

    # 天井の照明（ビルダーの RoomLight と同じ z=±1.875）：乳白ガラスの直付け灯
    for i, z in enumerate((-1.875, 1.875)):
        base = cyl(f"Sh_LampBase{i}", (0, H - 0.03, z), 0.03, 0.17, M["brass"], 48, bev=0.004)
        dome = lathe(f"Sh_LampDome{i}", (0, H - 0.03, z),
                     [(0.15, 0.0), (0.148, -0.03), (0.13, -0.065), (0.09, -0.09), (0.04, -0.1), (0.0, -0.102)],
                     M["frost"], 48)
        fin = lathe(f"Sh_LampFinial{i}", (0, H - 0.132, z), [(0.012, 0), (0.012, 0.01), (0.0, 0.03)], M["brass"], 16)
        out += done([base, dome, fin], angle=60)

    # 染み（天井のベッドの上・北西の壁）
    st = []
    st.append(span("Sh_StainCeil", -2.4, -1.0, H - 0.002, H - 0.001, -2.0, -0.6, M["stain"], 0))
    st.append(span("Sh_StainWall", -HW + FI + 0.001, -HW + FI + 0.002, 1.4, H - 0.09, 1.2, 2.6, M["stain"], 0))
    for o in st:
        hq.apply_mods(o)
        _plane_uv(o)
        hq.smooth(o, 10)
    out += st
    return out


def _plane_uv(o):
    """板1枚の各面に0〜1のUVを張る（デカール用）"""
    me = o.data
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda k: abs(n[k]))
        pts = [l.vert.co for l in f.loops]
        ks = [k for k in range(3) if k != ax]
        mn = [min(p[k] for p in pts) for k in ks]
        mx = [max(p[k] for p in pts) for k in ks]
        for l in f.loops:
            p = l.vert.co
            l[uv].uv = ((p[ks[0]] - mn[0]) / max(1e-6, mx[0] - mn[0]), (p[ks[1]] - mn[1]) / max(1e-6, mx[1] - mn[1]))
    bm.to_mesh(me); bm.free()


# ============================== ベッド ==============================

def bed_frame(M):
    w = M["wood"]
    p = []
    hx, hz = 0.53, 1.03
    # 支柱（頭側は高く、上に丸い笠）
    for sx in (-1, 1):
        p.append(span(f"Bed_PostH{sx}", sx * hx - 0.035, sx * hx + 0.035, 0, 0.93, -hz - 0.035, -hz + 0.035, w, 0.006))
        p.append(lathe(f"Bed_CapH{sx}", (sx * hx, 0.93, -hz), [(0.045, 0), (0.045, 0.012), (0.03, 0.022), (0.012, 0.032), (0, 0.034)], w, 24))
        p.append(span(f"Bed_PostF{sx}", sx * hx - 0.035, sx * hx + 0.035, 0, 0.58, hz - 0.035, hz + 0.035, w, 0.006))
        p.append(lathe(f"Bed_CapF{sx}", (sx * hx, 0.58, hz), [(0.045, 0), (0.045, 0.012), (0.03, 0.022), (0.012, 0.032), (0, 0.034)], w, 24))
        # 側板
        p.append(span(f"Bed_Rail{sx}", sx * hx - 0.02, sx * hx + 0.02, 0.2, 0.4, -hz + 0.035, hz - 0.035, w, 0.006))
    # 頭板：上框・下框・鏡板（3枚の縦の羽目板）
    p.append(span("Bed_HTop", -hx, hx, 0.8, 0.88, -hz - 0.025, -hz + 0.025, w, 0.008))
    p.append(span("Bed_HCap", -hx - 0.04, hx + 0.04, 0.88, 0.905, -hz - 0.04, -hz + 0.04, w, 0.008))
    p.append(span("Bed_HBot", -hx, hx, 0.4, 0.47, -hz - 0.025, -hz + 0.025, w, 0.006))
    for i in range(3):
        x0 = -hx + 0.035 + i * (2 * hx - 0.07) / 3
        x1 = x0 + (2 * hx - 0.07) / 3
        p.append(span(f"Bed_HPanel{i}", x0 + 0.012, x1 - 0.012, 0.47, 0.8, -hz - 0.012, -hz + 0.012, M["woodDark"], 0.01))
        if i > 0:
            p.append(span(f"Bed_HMull{i}", x0 - 0.018, x0 + 0.018, 0.47, 0.8, -hz - 0.022, -hz + 0.022, w, 0.004))
    # 足板
    p.append(span("Bed_FTop", -hx, hx, 0.47, 0.54, hz - 0.025, hz + 0.025, w, 0.006))
    p.append(span("Bed_FPanel", -hx, hx, 0.2, 0.47, hz - 0.012, hz + 0.012, M["woodDark"], 0.008))
    # すのこの受け（見えないが隙間から覗く）
    for sx in (-1, 1):
        p.append(span(f"Bed_Ledger{sx}", sx * (hx - 0.03) - 0.012, sx * (hx - 0.03) + 0.012, 0.36, 0.4, -hz + 0.04, hz - 0.04, w, 0.002))
    for i in range(9):
        z = -0.9 + i * 0.225
        p.append(span(f"Bed_Slat{i}", -hx + 0.02, hx - 0.02, 0.38, 0.4, z - 0.04, z + 0.04, w, 0.002))
    done(p, 1.0, rot90=True)
    frame = join(p, "Bed_Frame")
    mat_ = span("Bed_Mattress", -0.49, 0.49, 0.4, 0.61, -0.98, 0.98, M["mattress"], bev=0.05, segs=5)
    sub = mat_.modifiers.new("Sub", "SUBSURF"); sub.levels = 1
    finish(mat_, 1.0, angle=60)
    pil = pillow("Bed_Pillow", (0.0, 0.665, -0.74), (0.64, 0.15, 0.4), M["linen"], seed=3)
    finish(pil, 1.4, angle=80)
    return frame, mat_, pil


def bed_quilt(M, colliders, frames=50, seed=7):
    """掛け布団：布シミュレーションでベッドにかけてしわを作る。
    起き上がった直後のように、頭側の東（部屋側）の角を斜めに折り返しておく"""
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=58, y_subdivisions=70, size=1.0, location=(0, 0, 0))
    q = bpy.context.active_object
    q.name = "Bed_Quilt"
    q.data.materials.append(M["quilt"])
    me = q.data
    # 折り返しの線（布のローカル x,z。頭側の東の角を三角に折る）
    p0 = Vector((-0.05, -0.875)); d = (Vector((0.725, -0.05)) - p0).normalized()
    params = {}
    for v in me.vertices:
        u, t = v.co.x + 0.5, v.co.y + 0.5            # 0..1
        x, z = (u - 0.5) * 1.45, (t - 0.5) * 1.75
        y = 0.86 + 0.015 * math.sin(u * 7 + seed) * math.sin(t * 5)
        rel = Vector((x, z)) - p0
        side = d.x * rel.y - d.y * rel.x              # 折り線からの符号付き距離（負が角の側）
        if side < 0:
            # 角の側を、半径 R の丸い折り目で裏返して上に重ねる（面が潰れないように）
            R = 0.03
            sd = -side
            along = rel.dot(d)
            nrm = Vector((d.y, -d.x))                 # 角の側を向く法線
            if sd <= math.pi * R:
                perp, lift = R * math.sin(sd / R), R * (1 - math.cos(sd / R))
            else:
                perp, lift = -(sd - math.pi * R), 2 * R
            r = p0 + d * along + nrm * perp
            x, z = r.x, r.y
            y += lift
        params[v.index] = (u, t)
        v.co = U(x + 0.06, y, z + 0.2)
    uvl = me.uv_layers.active.data
    for poly in me.polygons:
        for li in poly.loop_indices:
            u, t = params[me.loops[li].vertex_index]
            uvl[li].uv = (u * 1.45 / 0.45, t * 1.75 / 0.45)      # 柄1枚 = 0.45m
    cloth = q.modifiers.new("Cloth", "CLOTH")
    s = cloth.settings
    # 検証済みの安定な設定（剛性・品質を上げたり衝突の厚みを足すと発散した）
    s.quality = 8; s.mass = 0.4; s.tension_stiffness = 12; s.compression_stiffness = 12
    s.bending_stiffness = 0.6
    cs = cloth.collision_settings
    cs.use_self_collision = True; cs.distance_min = 0.006; cs.self_distance_min = 0.003
    for c in colliders:
        if not any(m.type == "COLLISION" for m in c.modifiers):
            c.modifiers.new("Collision", "COLLISION")
    sc = bpy.context.scene
    sc.frame_start = 1; sc.frame_end = frames + 1
    cloth.point_cache.frame_start = 1; cloth.point_cache.frame_end = frames + 1
    for f in range(1, frames + 1):
        sc.frame_set(f)
    hq.apply_mods(q)
    sc.frame_set(1)
    for c in colliders:
        for m in [m for m in c.modifiers if m.type == "COLLISION"]:
            c.modifiers.remove(m)
    # キルトの縫い目：25cm角ごとに中央がふくらむ（法線方向に押し出す）
    me = q.data
    me.update()
    puff = {}
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            if vi in puff:
                continue
            u, t = params[vi]
            a = abs(math.sin(math.pi * u * 1.45 / 0.25)); b = abs(math.sin(math.pi * t * 1.75 / 0.25))
            puff[vi] = 0.011 * (a * b) ** 0.6
    for v in me.vertices:
        v.co += v.normal * puff.get(v.index, 0.0)
    # 厚み。均等オフセット(use_even_offset)は折り目(ほぼ180°)で頂点が遠くへ飛ぶので使わない
    sol = q.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.032; sol.offset = 0.0
    finish(q, keep_uv=True, angle=180)
    return q


def bed(M, with_quilt=True):
    frame, mat_, pil = bed_frame(M)
    parts = [frame, mat_, pil]
    if with_quilt:
        parts.append(bed_quilt(M, [mat_, pil, frame]))
    return parts


# ============================== 机とランプ ==============================

def desk(M):
    w = M["wood"]
    p = []
    p.append(span("Desk_Top", -0.7, 0.7, 0.715, 0.75, -0.35, 0.35, w, bev=0.008, segs=4))
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(taper_leg(f"Desk_Leg{sx}{sz}", sx * 0.635, sz * 0.285, 0.0, 0.715, 0.05, 0.034, w))
    # 幕板（背と両脇）と引き出し
    p.append(span("Desk_ApronB", -0.61, 0.61, 0.6, 0.715, 0.27, 0.29, w, 0.003))
    for sx in (-1, 1):
        p.append(span(f"Desk_ApronS{sx}", sx * 0.62 - 0.01, sx * 0.62 + 0.01, 0.6, 0.715, -0.26, 0.26, w, 0.003))
    p.append(span("Desk_Rail", -0.61, 0.61, 0.59, 0.605, -0.29, -0.27, w, 0.003))
    p.append(span("Desk_Mid", -0.012, 0.012, 0.605, 0.715, -0.29, -0.27, w, 0.003))
    p += drawer_front("Desk_DrL", -0.6, -0.018, 0.61, 0.708, -0.285, w, M["brass"])
    p += drawer_front("Desk_DrR", 0.018, 0.6, 0.61, 0.708, -0.285, w, M["brass"])
    done(p, 1.0)
    return [join(p, "Desk")]


def lamp(M):
    b = M["brass"]
    p = []
    p.append(lathe("Lamp_Base", (0, 0, 0), [(0.075, 0), (0.076, 0.006), (0.072, 0.013), (0.05, 0.019),
                                             (0.022, 0.026), (0.014, 0.034), (0.011, 0.04)], b, 48))
    p.append(cyl("Lamp_Stem", (0, 0.04, 0), 0.29, 0.0065, b, 20))
    p.append(lathe("Lamp_Knuckle", (0, 0.2, 0), [(0.0065, 0), (0.011, 0.004), (0.011, 0.014), (0.0065, 0.018)], b, 20))
    p.append(lathe("Lamp_Socket", (0, 0.33, 0), [(0.014, 0), (0.016, 0.004), (0.016, 0.04), (0.013, 0.044)], M["black"], 24))
    p.append(cyl_between("Lamp_Cord", (0.0, 0.01, 0.06), (0.05, 0.004, 0.25), 0.003, M["black"], 8))
    done(p, angle=40)
    bulb = lathe("Lamp_Bulb", (0, 0.374, 0), [(0.01, 0), (0.014, 0.01), (0.026, 0.035), (0.028, 0.05),
                                             (0.022, 0.068), (0.01, 0.078), (0.0, 0.08)], M["bulb"], 32)
    finish(bulb, angle=80)
    # 角型の布シェード（四角錐台、上が狭い）＋上下の針金枠
    shade = lathe("Lamp_Shade", (0, 0.32, 0), [(0.17, 0.0), (0.145, 0.2)], M["shade"], 4)
    shade.rotation_euler = (0, 0, math.radians(45))
    bpy.context.view_layer.update()
    shade.data.transform(shade.matrix_basis); shade.matrix_basis.identity()
    bm = bmesh.new(); bm.from_mesh(shade.data)
    caps = [f for f in bm.faces if abs(f.normal.z) > 0.9]
    bmesh.ops.delete(bm, geom=caps, context="FACES_ONLY")
    bm.to_mesh(shade.data); bm.free()
    sol = shade.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.003
    finish(shade, 3.0, angle=30)
    wires = []
    for (y, r) in ((0.32, 0.12), (0.52, 0.1025)):
        h = r
        pts = [(-h, -h), (h, -h), (h, h), (-h, h)]
        for i in range(4):
            a, c = pts[i], pts[(i + 1) % 4]
            wires.append(cyl_between(f"Lamp_Wire{y}{i}", (a[0], y, a[1]), (c[0], y, c[1]), 0.0025, b, 8))
    for i, (dx, dz) in enumerate(((1, 0), (-1, 0))):
        wires.append(cyl_between(f"Lamp_Spider{i}", (0, 0.36, 0), (dx * 0.1, 0.5, dz * 0.1), 0.0018, b, 6))
    done(wires, angle=60)
    body = join(p + wires, "Lamp_Body")
    return [body, bulb, shade]


# ============================== 記録端末（ブラウン管） ==============================

def terminal(M):
    w = M["woodDark"]
    p = []
    # 台：扉付きの小さなキャビネット（前 = -Z）
    p.append(span("Term_Top", -0.32, 0.32, 0.81, 0.84, -0.26, 0.26, w, 0.006))
    p.append(span("Term_Body", -0.3, 0.3, 0.06, 0.81, -0.235, 0.24, w, 0.004))
    p.append(span("Term_Plinth", -0.29, 0.29, 0.0, 0.06, -0.225, 0.23, M["trim"], 0.003))
    p.append(span("Term_Door", -0.27, 0.27, 0.09, 0.78, -0.255, -0.235, w, 0.006))
    p.append(span("Term_DoorPanel", -0.22, 0.22, 0.14, 0.73, -0.262, -0.255, M["wood"], 0.006))
    p += knob("Term_Knob", (0.22, 0.46, -0.262), M["brass"])
    done(p, 1.0, rot90=True)
    cab = join(p, "Term_Cabinet")
    # ブラウン管：前面の枠（角丸）と、後ろに細くなる胴
    q = []
    q.append(span("Crt_Front", -0.22, 0.22, 0.875, 1.2, -0.105, -0.02, M["beige"], 0.02, 4))
    me = bpy.data.meshes.new("Crt_Back")
    bm = bmesh.new()
    rings = []
    for (z, hw_, y0, y1) in ((-0.02, 0.205, 0.885, 1.19), (0.2, 0.13, 0.93, 1.14), (0.235, 0.09, 0.96, 1.1)):
        rings.append([bm.verts.new(U(x, y, z)) for x, y in ((-hw_, y0), (hw_, y0), (hw_, y1), (-hw_, y1))])
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    back = hq._obj("Crt_Back", me, M["beige"])
    hq.bevel(back, 0.02, 3, 30)
    q.append(back)
    q.append(span("Crt_Foot", -0.13, 0.13, 0.84, 0.862, -0.08, 0.16, M["beige"], 0.008))
    q.append(span("Crt_Neck", -0.08, 0.08, 0.86, 0.885, -0.05, 0.12, M["beige"], 0.006))
    # 画面のくぼみ（黒い縁）と電源ボタン・通風口
    q.append(span("Crt_Bezel", -0.185, 0.185, 0.915, 1.17, -0.108, -0.1, M["black"], 0.012))
    q.append(span("Crt_Power", 0.15, 0.18, 0.888, 0.902, -0.109, -0.104, M["black"], 0.002))
    for i in range(7):
        y = 0.97 + i * 0.022
        for sx in (-1, 1):
            q.append(span(f"Crt_Vent{i}{sx}", sx * 0.13 - 0.035, sx * 0.13 + 0.035, y, y + 0.008, 0.19, 0.2, M["black"], 0.002))
    # キーボード（手前）：本体＋キー
    q.append(span("Kb_Body", -0.19, 0.19, 0.84, 0.862, -0.25, -0.12, M["beige"], 0.008))
    rows = [(14, 0.0), (14, 0.004), (13, 0.009), (12, 0.013)]     # 手前(-Z)がスペースキーの列
    for r, (n, off) in enumerate(rows):
        z = -0.222 + r * 0.025
        for i in range(n):
            x = -0.175 + off + i * 0.025
            q.append(span(f"Kb_Key{r}_{i}", x, x + 0.021, 0.862, 0.874, z, z + 0.021, M["key"], 0.004, 2))
    q.append(span("Kb_Space", -0.08, 0.08, 0.862, 0.874, -0.246, -0.227, M["key"], 0.004, 2))
    done(q, 1.0, angle=35)
    crt = join(q, "Term_Crt")
    # ブラウン管の画面（少し膨らんだ面・自発光）
    def scr(u, v):
        x = (u - 0.5) * 0.32
        y = 0.93 + v * 0.225
        bulge = 0.01 * (1 - (2 * u - 1) ** 2) * (1 - (2 * v - 1) ** 2)
        return (x, y, -0.1085 - bulge)
    screen = hq.grid_surface("Term_Screen", 16, 12, scr, M["screen"])
    me = screen.data
    uvl = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = ((-co.x + 0.16) / 0.32, (co.z - 0.93) / 0.225)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        if f.normal.y < 0:          # -Z(Unity) = +Y(Blender) を向かせる
            f.normal_flip()
    bm.to_mesh(me); bm.free()
    hq.smooth(screen, 180)
    cable = cyl_between("Term_Cable", (0.05, 0.95, 0.23), (0.12, 0.84, 0.26), 0.005, M["black"], 8)
    finish(cable, angle=60)
    return [cab, crt, screen, cable]


# ============================== 人形と棚 ==============================

def doll_shelf(M):
    w = M["wood"]
    p = []
    wall = 0.15
    p.append(span("Shelf_Board", -0.14, wall, 1.14, 1.175, -0.76, 0.76, w, 0.008, 4))
    p.append(span("Shelf_Back", wall - 0.018, wall, 1.175, 1.26, -0.76, 0.76, w, 0.004))
    p.append(span("Shelf_Lip", -0.14, -0.128, 1.175, 1.19, -0.74, 0.74, w, 0.003))
    # 装飾的な持ち送り（S字の曲線）
    for i, z in enumerate((-0.55, 0.55)):
        pts = [(wall, 1.14), (wall - 0.2, 1.14)]
        for k in range(1, 12):
            t = k / 12
            x = wall - 0.2 * (1 - t) - 0.015 * math.sin(t * math.pi * 2)
            y = 1.14 - 0.24 * t + 0.03 * math.sin(t * math.pi)
            pts.append((x, y))
        pts.append((wall, 0.9))
        p.append(plate_xy(f"Shelf_Bracket{i}", pts, z - 0.012, z + 0.012, w, 0.004))
    done(p, 1.0)
    return [join(p, "DollShelf")]


def doll(M):
    p = []
    # スカート・胴・襟
    p.append(lathe("Doll_Skirt", (0, 0, 0), [(0.0, 0.0), (0.046, 0.0), (0.048, 0.006), (0.046, 0.02), (0.04, 0.055),
                                              (0.031, 0.09), (0.022, 0.112)], M["dress"], 40))
    p.append(lathe("Doll_Hem", (0, 0.003, 0), [(0.046, 0), (0.05, 0.002), (0.05, 0.01), (0.046, 0.012), (0.044, 0.006), (0.046, 0)],
                   M["lace"], 40, False, False))
    p.append(lathe("Doll_Body", (0, 0.11, 0), [(0.022, 0), (0.021, 0.02), (0.018, 0.035), (0.012, 0.047)], M["dress"], 32))
    p.append(lathe("Doll_Collar", (0, 0.152, 0), [(0.022, 0), (0.02, 0.006), (0.011, 0.01)], M["lace"], 32))
    p.append(cyl("Doll_Neck", (0, 0.155, 0), 0.02, 0.0075, M["porcelain"], 20))
    # 腕（袖）と手
    for sx in (-1, 1):
        p.append(cyl_between(f"Doll_Arm{sx}", (sx * 0.02, 0.148, 0.0), (sx * 0.03, 0.1, 0.012), 0.0085, M["dress"], 16, 0.0075))
        h = lathe(f"Doll_Hand{sx}", (sx * 0.031, 0.088, 0.013), [(0.0, 0.0), (0.006, 0.002), (0.0065, 0.008), (0.0, 0.012)], M["porcelain"], 12)
        p.append(h)
    done(p, 4.0, angle=50)
    body = join(p, "Doll_Dress")
    # 頭（少し縦長の卵形）と髪
    bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=24, radius=0.033, location=U(0, 0.205, 0))
    head = bpy.context.active_object; head.name = "Doll_Head"
    head.scale = (1.0, 0.97, 1.08)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)
    head.data.materials.append(M["porcelain"])
    hq.smooth(head, 180)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=24, radius=0.036, location=U(0, 0.209, -0.002))
    hair = bpy.context.active_object; hair.name = "Doll_Hair"
    hair.scale = (1.02, 1.0, 1.1)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)
    bm = bmesh.new(); bm.from_mesh(hair.data)
    # 顔の部分（正面 +Z=Blender -Y の下側）をくり抜く。前髪は眉の高さで切り揃え
    cut = [v for v in bm.verts if (-v.co.y) > 0.004 and v.co.z < 0.222]
    bmesh.ops.delete(bm, geom=cut, context="VERTS")
    bm.to_mesh(hair.data); bm.free()
    hair.data.materials.append(M["hair"])
    sol = hair.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.002
    # 後ろ髪（肩まで）
    back = lathe("Doll_HairBack", (0, 0.16, -0.012), [(0.02, 0.0), (0.03, 0.02), (0.035, 0.05)], M["hair"], 24)
    bm = bmesh.new(); bm.from_mesh(back.data)
    cut = [v for v in bm.verts if (-v.co.y) > -0.004]
    bmesh.ops.delete(bm, geom=cut, context="VERTS")
    bm.to_mesh(back.data); bm.free()
    sol2 = back.modifiers.new("Solid", "SOLIDIFY"); sol2.thickness = 0.002
    finish(hair, angle=180); finish(back, angle=180)
    face = []
    for sx in (-1, 1):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.0042, location=U(sx * 0.0115, 0.207, 0.029))
        e = bpy.context.active_object; e.name = f"Doll_Eye{sx}"; e.data.materials.append(M["eye"])
        e.scale = (1.0, 0.5, 1.15); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        face.append(e)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.003, location=U(0, 0.19, 0.0315))
    lip = bpy.context.active_object; lip.name = "Doll_Lip"; lip.data.materials.append(M["lip"])
    lip.scale = (1.4, 0.5, 0.7); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    face.append(lip)
    for o in face:
        hq.smooth(o, 180)
    headj = join([head] + face, "Doll_HeadJ")
    hair_j = join([hair, back], "Doll_HairJ")
    return [body, headj, hair_j]


# ============================== 追加の家具 ==============================

def nightstand(M):
    w = M["wood"]
    p = []
    p.append(span("Ns_Top", -0.22, 0.22, 0.53, 0.555, -0.2, 0.2, w, 0.006))
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.append(taper_leg(f"Ns_Leg{sx}{sz}", sx * 0.19, sz * 0.17, 0, 0.53, 0.036, 0.026, w))
    for sx in (-1, 1):
        p.append(span(f"Ns_Side{sx}", sx * 0.19 - 0.01, sx * 0.19 + 0.01, 0.25, 0.53, -0.16, 0.16, w, 0.003))
    p.append(span("Ns_Back", -0.18, 0.18, 0.25, 0.53, -0.18, -0.16, w, 0.003))
    p.append(span("Ns_Shelf", -0.18, 0.18, 0.25, 0.27, -0.17, 0.17, w, 0.003))
    p.append(span("Ns_Mid", -0.18, 0.18, 0.41, 0.425, -0.17, 0.17, w, 0.003))
    p += drawer_front("Ns_Drawer", -0.175, 0.175, 0.43, 0.525, 0.19, w, M["brass"], sgn=1)
    done(p, 1.0)
    stand = join(p, "Nightstand")
    # 上：止まった目覚まし時計と本
    q = []
    q.append(cyl_between("Ns_ClockBody", (0.07, 0.62, -0.03), (0.07, 0.62, 0.01), 0.055, M["metal"], 40))
    q.append(cyl_between("Ns_ClockFace", (0.07, 0.62, 0.0105), (0.07, 0.62, 0.012), 0.047, M["plate"], 40))
    q.append(cyl_between("Ns_Hand1", (0.07, 0.62, 0.013), (0.07, 0.655, 0.013), 0.0018, M["black"], 6))
    q.append(cyl_between("Ns_Hand2", (0.07, 0.62, 0.014), (0.095, 0.605, 0.014), 0.0015, M["black"], 6))
    for sx in (-1, 1):
        q.append(cyl_between(f"Ns_Foot{sx}", (0.07 + sx * 0.03, 0.575, -0.01), (0.07 + sx * 0.045, 0.555, -0.01), 0.005, M["metal"], 8))
        q.append(lathe(f"Ns_Bell{sx}", (0.07 + sx * 0.035, 0.668, -0.01), [(0.022, 0), (0.02, 0.012), (0.0, 0.02)], M["metal"], 24))
    books = []
    y = 0.555
    for i, (bw, bh, bd, mk) in enumerate(((0.17, 0.028, 0.23, "book1"), (0.15, 0.022, 0.21, "book2"))):
        books.append(span(f"Ns_Book{i}", -0.16, -0.16 + bw, y, y + bh, -0.12, -0.12 + bd, M[mk], 0.003))
        books.append(span(f"Ns_BookP{i}", -0.157, -0.163 + bw, y + 0.003, y + bh - 0.003, -0.118, -0.122 + bd + 0.004, M["pages"], 0.001))
        y += bh
    done(q + books, 1.0, angle=40)
    top = join(q + books, "Nightstand_Items")
    return [stand, top]


def chest(M):
    w = M["wood"]
    p = []
    p.append(span("Ch_Top", -0.46, 0.46, 0.82, 0.85, -0.23, 0.23, w, 0.008, 4))
    p.append(span("Ch_Carcass", -0.44, 0.44, 0.08, 0.82, -0.21, 0.2, w, 0.004))
    p.append(span("Ch_Plinth", -0.44, 0.44, 0.0, 0.08, -0.2, 0.19, M["trim"], 0.004))
    ys = [(0.1, 0.33), (0.35, 0.58), (0.6, 0.8)]
    for i, (y0, y1) in enumerate(ys):
        if i == 2:
            p += drawer_front(f"Ch_DrL{i}", -0.42, -0.005, y0, y1, 0.2, w, M["brass"], sgn=1)
            p += drawer_front(f"Ch_DrR{i}", 0.005, 0.42, y0, y1, 0.2, w, M["brass"], sgn=1)
        else:
            p += drawer_front(f"Ch_Dr{i}", -0.42, 0.42, y0, y1, 0.2, w, M["brass"], sgn=1, handles=2)
    done(p, 1.0)
    body = join(p, "Chest")
    # 上：小箱と伏せた写真立て
    q = []
    q.append(span("Ch_Box", 0.12, 0.34, 0.85, 0.93, -0.12, 0.04, M["woodDark"], 0.006))
    q.append(span("Ch_BoxLid", 0.115, 0.345, 0.93, 0.945, -0.125, 0.045, M["woodDark"], 0.005))
    q.append(span("Ch_Frame", -0.3, -0.12, 0.85, 0.865, -0.1, 0.12, M["trim"], 0.004))
    done(q, 1.0)
    return [body, join(q, "Chest_Items")]


def wardrobe(M):
    w = M["wood"]
    p = []
    p.append(span("Wr_Carcass", -0.5, 0.5, 0.09, 1.86, -0.29, 0.27, w, 0.004))
    p.append(span("Wr_Cornice", -0.53, 0.53, 1.86, 1.9, -0.31, 0.3, w, 0.01, 4))
    p.append(span("Wr_Cornice2", -0.515, 0.515, 1.84, 1.862, -0.3, 0.29, w, 0.006))
    p.append(span("Wr_Plinth", -0.51, 0.51, 0.0, 0.09, -0.29, 0.285, M["trim"], 0.005))
    for sx in (-1, 1):
        x0, x1 = sorted((sx * 0.004, sx * 0.49))
        p.append(span(f"Wr_Door{sx}", x0, x1, 0.12, 1.83, 0.27, 0.29, w, 0.006))
        # 扉の鏡板（上下2枚）
        for (y0, y1) in ((0.2, 0.9), (0.98, 1.75)):
            p.append(span(f"Wr_Panel{sx}{y0}", x0 + 0.06, x1 - 0.06, y0, y1, 0.29, 0.296, M["woodDark"], 0.008))
        p += knob(f"Wr_Knob{sx}", (sx * 0.05, 0.98, 0.296), M["brass"], face="+z")
    done(p, 1.0, rot90=True)
    return [join(p, "Wardrobe")]


def chair(M):
    w = M["wood"]
    p = []
    # 座面・脚・背（背は -Z 側）
    seat = span("Chr_Seat", -0.21, 0.21, 0.43, 0.46, -0.2, 0.2, w, 0.01, 4)
    p.append(seat)
    for sx in (-1, 1):
        p.append(taper_leg(f"Chr_LegF{sx}", sx * 0.18, 0.17, 0, 0.43, 0.032, 0.026, w))
        p.append(span(f"Chr_LegB{sx}", sx * 0.18 - 0.017, sx * 0.18 + 0.017, 0, 0.9, -0.19, -0.155, w, 0.005))
        p.append(span(f"Chr_Stretch{sx}", sx * 0.18 - 0.01, sx * 0.18 + 0.01, 0.14, 0.165, -0.16, 0.15, w, 0.003))
    p.append(span("Chr_BackTop", -0.2, 0.2, 0.8, 0.88, -0.19, -0.16, w, 0.008))
    for i in range(3):
        x = -0.08 + i * 0.08
        p.append(span(f"Chr_Spindle{i}", x - 0.012, x + 0.012, 0.46, 0.8, -0.178, -0.168, w, 0.004))
    p.append(span("Chr_Apron", -0.18, 0.18, 0.39, 0.43, 0.15, 0.17, w, 0.003))
    done(p, 1.0)
    return [join(p, "Chair")]


# ============================== 机の上の資料 ==============================

def flashlight(M):
    """箱 0.07x0.07x0.28（長手 z）。机に寝かせる"""
    p = []
    p.append(cyl_between("Fl_Body", (0, 0, 0.13), (0, 0, -0.06), 0.017, M["black"], 32))
    p.append(cyl_between("Fl_Neck", (0, 0, -0.06), (0, 0, -0.085), 0.017, M["black"], 32, 0.028))
    p.append(cyl_between("Fl_Head", (0, 0, -0.085), (0, 0, -0.135), 0.028, M["black"], 32))
    p.append(cyl_between("Fl_Bezel", (0, 0, -0.135), (0, 0, -0.14), 0.029, M["metal"], 32))
    p.append(cyl_between("Fl_Cap", (0, 0, 0.13), (0, 0, 0.14), 0.016, M["metal"], 24))
    for i in range(10):
        z = 0.1 - i * 0.012
        p.append(cyl_between(f"Fl_Grip{i}", (0, 0, z), (0, 0, z - 0.006), 0.0185, M["rubber"], 24))
    p.append(span("Fl_Switch", -0.006, 0.006, 0.015, 0.022, -0.03, -0.005, M["rubber"], 0.002))
    done(p, 8.0, angle=40)
    lens = cyl_between("Fl_Lens", (0, 0, -0.1395), (0, 0, -0.141), 0.024, M["glass"], 32)
    finish(lens, angle=60)
    body = join(p, "Flashlight")
    # 箱の底(y=-0.035)に接地するよう持ち上げる：頭の半径0.029
    for o in (body, lens):
        o.data.transform(__import__("mathutils").Matrix.Translation(U(0, -0.035 + 0.029, 0) - U(0, 0, 0)))
    return [body, lens]


def notebook(M):
    """箱 0.21x0.055x0.16。背は -X 側（Unity）。底=-0.0275"""
    y0 = -0.0275
    p = []
    p.append(span("Nb_CoverB", -0.105, 0.105, y0, y0 + 0.004, -0.08, 0.08, M["cover"], 0.0015, 2))
    p.append(span("Nb_CoverT", -0.105, 0.105, y0 + 0.028, y0 + 0.032, -0.08, 0.08, M["cover"], 0.0015, 2))
    p.append(cyl_between("Nb_Spine", (-0.103, y0 + 0.016, -0.08), (-0.103, y0 + 0.016, 0.08), 0.016, M["cover"], 24))
    p.append(span("Nb_Pages", -0.1, 0.101, y0 + 0.004, y0 + 0.028, -0.077, 0.077, M["pages"], 0.001))
    p.append(span("Nb_Ribbon", -0.02, -0.01, y0 + 0.006, y0 + 0.0065, 0.077, 0.13, M["ribbon"], 0.0))
    p.append(span("Nb_Band", 0.07, 0.078, y0 - 0.0005, y0 + 0.0325, -0.081, 0.081, M["black"], 0.001))
    done(p, 6.0, angle=45)
    return [join(p, "Notebook")]


def newspaper(M):
    """箱 0.42x0.02x0.3。二つ折りの切り抜きを机に置く（底=-0.01）"""
    def f(u, v):
        x = (u - 0.5) * 0.4
        z = (v - 0.5) * 0.29
        y = -0.009 + 0.004 * math.sin(u * math.pi) + 0.006 * max(0.0, (u - 0.8) / 0.2) ** 2
        return (x, y, z)
    top = hq.grid_surface("News_Top", 24, 16, f, M["news"])
    me = top.data
    uvl = me.uv_layers.new(name="UVMap").data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl[li].uv = ((-co.x + 0.2) / 0.4, (-co.y + 0.145) / 0.29)
    bm = bmesh.new(); bm.from_mesh(me)
    for fc in bm.faces:
        if fc.normal.z < 0:
            fc.normal_flip()
    bm.to_mesh(me); bm.free()
    sol = top.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = 0.0008
    finish(top, keep_uv=True, angle=180)
    return [top]


# ============================== 扉 ==============================

def door(M):
    """部屋の扉（RoomDoor ユニットの原点。扉板の中心は y=1.05、部屋側は +Z）。
    4枚の鏡板・真鍮の丸ノブと座金・蝶番"""
    w = M["woodDark"]
    t = 0.02                                   # 扉板の厚みの半分
    p = []
    x0, x1 = -0.458, 0.458
    # 框（かまち）と桟
    p.append(span("Door_StileL", x0, x0 + 0.12, 0.0, 2.1, -t, t, w, 0.004))
    p.append(span("Door_StileR", x1 - 0.12, x1, 0.0, 2.1, -t, t, w, 0.004))
    for (y0, y1, nm) in ((1.96, 2.1, "Top"), (0.98, 1.14, "Lock"), (0.0, 0.22, "Bot")):
        p.append(span(f"Door_Rail{nm}", x0 + 0.12, x1 - 0.12, y0, y1, -t, t, w, 0.004))
    # 中桟は錠前の横框で分ける（横框と重ねると表面がちらつく）
    p.append(span("Door_MullB", -0.035, 0.035, 0.22, 0.98, -t, t, w, 0.004))
    p.append(span("Door_MullT", -0.035, 0.035, 1.14, 1.96, -t, t, w, 0.004))
    # 鏡板（周囲を面取りして一段下げる）
    for (px0, px1) in ((x0 + 0.12, -0.035), (0.035, x1 - 0.12)):
        for (py0, py1) in ((0.22, 0.98), (1.14, 1.96)):
            p.append(span(f"Door_Panel{px0:.2f}{py0:.2f}", px0 + 0.004, px1 - 0.004, py0 + 0.004, py1 - 0.004,
                          -t + 0.008, t - 0.008, M["wood"], 0.012, 4))
            # 押縁（パネルの縁取り）
            for zs in (-1, 1):
                zz0, zz1 = sorted((zs * (t - 0.008), zs * (t - 0.001)))
                # 縦の押縁は上下の押縁の間だけ（四隅で重ねない）
                for (a0, a1, b0, b1) in ((px0, px1, py0, py0 + 0.014), (px0, px1, py1 - 0.014, py1),
                                         (px0, px0 + 0.014, py0 + 0.014, py1 - 0.014),
                                         (px1 - 0.014, px1, py0 + 0.014, py1 - 0.014)):
                    p.append(span("Door_Bead", a0, a1, b0, b1, zz0, zz1, w, 0.003))
    done(p, 1.0, rot90=True)
    leaf = join(p, "Door_Leaf")
    q = []
    for zs in (-1, 1):
        zf = zs * t
        # 座金（丸）とノブ
        q.append(cyl_between(f"Door_Rose{zs}", (0.36, 1.02, zf), (0.36, 1.02, zf + zs * 0.008), 0.03, M["brass"], 32))
        q.append(cyl_between(f"Door_Neck{zs}", (0.36, 1.02, zf + zs * 0.008), (0.36, 1.02, zf + zs * 0.04), 0.009, M["brass"], 20))
        kb = lathe(f"Door_Knob{zs}", (0, 0, 0), [(0.0, 0.0), (0.02, 0.004), (0.028, 0.016), (0.026, 0.03), (0.016, 0.04), (0.0, 0.043)],
                   M["brass"], 32)
        import mathutils
        rot = mathutils.Euler((math.radians(90 * zs), 0, 0)).to_matrix().to_4x4()
        kb.data.transform(mathutils.Matrix.Translation(U(0.36, 1.02, zf + zs * 0.036)) @ rot)
        q.append(kb)
        # 鍵穴の座
        q.append(span(f"Door_Esc{zs}", 0.345, 0.375, 0.88, 0.94, *sorted((zf, zf + zs * 0.004)), M["brass"], 0.002))
    for y in (0.25, 1.05, 1.85):                # 蝶番（吊り元 = -X 側）
        q.append(cyl_between(f"Door_Hinge{y}", (x0 - 0.004, y - 0.05, 0.0), (x0 - 0.004, y + 0.05, 0.0), 0.007, M["brass"], 16))
    done(q, 1.0, angle=50)
    hw_ = join(q, "Door_Hardware")
    return [leaf, hw_]


# ============================== 一括 ==============================

PIECES = {
    "Shell": shell, "Bed": bed, "Desk": desk, "Lamp": lamp, "Terminal": terminal,
    "DollShelf": doll_shelf, "Doll": doll, "Nightstand": nightstand, "Chest": chest,
    "Wardrobe": wardrobe, "Chair": chair, "Flashlight": flashlight, "Notebook": notebook,
    "Newspaper": newspaper, "Door": door,
}
