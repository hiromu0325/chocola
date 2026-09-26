"""
調べる画面（手に取って回して見る）の実物 14 種を品質重視で作る。

座標はアイテムのローカル（Unity の値、m）。表は -Z（見る人の方を向く）、上は +Y、見る人の右が +X。
文字はモデルにしない。Unity 側（InspectPrint）が「印字面」の長方形に World Space のキャンバスを置くので、
印字面は平らな長方形に保つ。紙の反り・波・折れは印字面の外に出すか、奥（+Z）へ引っ込む向きだけにする。

各関数 fn(M) は (部品のリスト, 印字面のリスト) を返す。印字面は
  {"center": 面から法線の向きに EPS 浮かせた点, "size": [幅, 高さ], "normal": 表の向き, "up": 文字の上}
ビルダー（build_inspect.py）が部品を1メッシュにまとめ、外接箱の中心を原点に移して書き出す。
UV は実寸の箱投影（1m = 1。Unity の紙・革・段ボールのテクスチャがこの前提）。写真の面だけ 0〜1。

  Sheet      A4 の紙1枚（端の反り・右下の折れ）
  Report     ホチキス留めの A4 4枚（少しずれて扇状）
  Folder     書類の入ったマニラフォルダー（見出しのタブとラベル、表紙にクリップで留めた紙）
  Letter     長形の封筒（開いたふた）と三つ折りを開いた便箋
  Notebook   開いた革の手帳（ページの束、綴じ目の糸、縁のステッチ）。印字面は左右のページ
  Card       透明ケースに入った職員証（写真・ストラップ・金具）
  Photo      木の写真立て（ガラス・セピアの写真・裏板・脚）
  Newspaper  四つ折りの新聞（折り目の重なり、開いた端の紙の層）
  Monitor    小型の液晶モニター（細い黒縁・首・台）
  Cassette   カセットテープ（スモークの殻・ねじ・窓から見えるハブと巻いたテープ・ラベル）
  Recorder   IC レコーダー（金属の前板・スピーカーの穴・小さな液晶・ボタン）
  Poster     中吊り広告（厚紙、上の2つのハトメ）
  Clipboard  クリップボード（ばね金具と挟んだ紙）
  Drawing    子どもの絵の画用紙（ゆるい波、上の角にマスキングテープ）
"""
import bisect
import math
import random

import bmesh
import bpy
from mathutils import Vector

import hq
from hq import U, finish, join

EPS = 0.0003          # 印字面を紙から浮かせる量（文字のちらつき防止）


# ============================== 材質 ==============================

def mats():
    """プレビュー用の色。Unity 側は材質名（INS_*）で差し替える"""
    M = {}

    def m(name, *a, **k):
        M[name] = hq.mat(name, *a, **k)
    m("INS_Paper", (0.86, 0.86, 0.83), 0.85)
    m("INS_PaperCream", (0.86, 0.79, 0.64), 0.85)
    m("INS_Newsprint", (0.70, 0.68, 0.62), 0.9)
    m("INS_Manila", (0.80, 0.63, 0.38), 0.8)
    m("INS_Board", (0.33, 0.20, 0.11), 0.65)
    m("INS_Leather", (0.10, 0.055, 0.035), 0.55)
    m("INS_Metal", (0.78, 0.78, 0.80), 0.28, 1.0)
    m("INS_PlasticBlack", (0.018, 0.018, 0.02), 0.35)
    m("INS_PlasticSmoke", (0.05, 0.042, 0.038), 0.12, alpha=0.55)
    m("INS_PlasticClear", (0.9, 0.93, 0.96), 0.05, alpha=0.2)
    m("INS_Tape", (0.13, 0.065, 0.03), 0.35)
    m("INS_Label", (0.88, 0.85, 0.76), 0.8)
    m("INS_Screen", (0.008, 0.01, 0.012), 0.12)
    m("INS_Wood", (0.32, 0.17, 0.08), 0.45)
    m("INS_Glass", (0.85, 0.9, 0.92), 0.02, alpha=0.12)
    m("INS_Photo", (0.55, 0.42, 0.27), 0.5)
    m("INS_Cardboard", (0.62, 0.52, 0.36), 0.85)
    m("INS_Rubber", (0.035, 0.035, 0.04), 0.75)
    return M


# ============================== 小道具（数値） ==============================

def sstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def outside(x, lo, hi, edge_lo, edge_hi):
    """平らな範囲 [lo, hi] の外への距離を、紙の端で 1 になる割合で（端より外は 1 を超える）"""
    if x < lo:
        return (lo - x) / (lo - edge_lo)
    if x > hi:
        return (x - hi) / (edge_hi - hi)
    return 0.0


def segs(*parts):
    """格子の線の位置。parts = (始め, 終わり, 分割数) の並び"""
    out = set()
    for a, b, n in parts:
        for i in range(n + 1):
            out.add(round(a + (b - a) * i / n, 7))
    return sorted(out)


def fold(u, v, a, n, ang, R, sign=-1.0):
    """折り曲げ（長さを保つ）。(u,v)=平面の点、a=折り線上の点、n=めくれる側への単位ベクトル（平面内）、
    ang=曲げる角度、R=曲げの半径。戻り値 (u', v', w)：w は面から浮く量（sign=-1 で見る人の方 = -Z）"""
    s = (u - a[0]) * n[0] + (v - a[1]) * n[1]
    if s <= 0:
        return u, v, 0.0
    L = R * ang
    if s <= L:
        phi = s / R
        ip, op = R * math.sin(phi), R * (1 - math.cos(phi))
    else:
        ip = R * math.sin(ang) + (s - L) * math.cos(ang)
        op = R * (1 - math.cos(ang)) + (s - L) * math.sin(ang)
    d = ip - s
    return u + d * n[0], v + d * n[1], sign * op


def fold_cuts(a, n, ang, R, k=6):
    """折り曲げの丸みの所に格子の線を足す（bisect 用）"""
    L = R * ang
    return [((a[0] + n[0] * L * i / k, a[1] + n[1] * L * i / k), n) for i in range(k + 1)]


def rect(center, size, normal=(0, 0, -1), up=(0, 1, 0)):
    """印字面。center は面の上の点（ここから法線の向きに EPS 浮かせる）"""
    n = Vector(normal).normalized()
    c = Vector(center) + n * EPS
    return {"center": [c.x, c.y, c.z], "size": [size[0], size[1]], "normal": [n.x, n.y, n.z], "up": list(up)}


def unity(b):
    return Vector((-b[0], b[2], -b[1]))


# ============================== 形状 ==============================

def surf(name, us, vs, fn, material, thick=0.0, offset=-1.0, cuts=(), back=None, rim=None, even=True):
    """(u,v) の格子を fn(u,v) → Unity 座標 に写した面。反時計回りの (u,v) が表（-Z）を向く。
    cuts=[((u,v), (nu,nv))] の直線で格子を切ってから写す（折り目をまっすぐ通すため）。
    thick>0 で厚み（offset=-1 で表の裏側へ、0 で両側へ）。back / rim は裏・縁の材質。
    even=False は強く曲がった所で厚みが暴れる時用（Solidify の均一な厚みを切る）"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    grid = [[bm.verts.new((u, v, 0.0)) for v in vs] for u in us]
    for i in range(len(us) - 1):
        for j in range(len(vs) - 1):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    for (pc, pn) in cuts:
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-7, plane_co=(pc[0], pc[1], 0.0), plane_no=(pn[0], pn[1], 0.0))
    for v in bm.verts:
        v.co = U(*fn(v.co.x, v.co.y))
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    o = hq._obj(name, me, material)
    if thick > 0:
        sol = o.modifiers.new("Solid", "SOLIDIFY")
        sol.thickness = thick
        sol.offset = offset
        sol.use_even_offset = even
        sol.use_quality_normals = True
        if back is not None:
            o.data.materials.append(back)
            sol.material_offset = 1
        if rim is not None:
            if rim is back:
                sol.material_offset_rim = 1
            else:
                o.data.materials.append(rim)
                sol.material_offset_rim = len(o.data.materials) - 1
    return o


def plate(name, pts, org, ex, ey, ez, t0, t1, material, bev=0.0, segs_=2, angle=30):
    """平面の多角形 pts=[(a,b)] を org + a*ex + b*ey に置き、ez の向きに t0〜t1 の厚みを付ける"""
    org, ex, ey, ez = (Vector(v) for v in (org, ex, ey, ez))
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    f = [bm.verts.new(U(*(org + ex * a + ey * b + ez * t0))) for a, b in pts]
    k = [bm.verts.new(U(*(org + ex * a + ey * b + ez * t1))) for a, b in pts]
    bm.faces.new(f)
    bm.faces.new(list(reversed(k)))
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((f[i], k[i], k[j], f[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    o = hq._obj(name, me, material)
    if bev > 0:
        hq.bevel(o, bev, segs_, angle)
    return o


def pxy(name, pts, z0, z1, material, c=(0.0, 0.0), bev=0.0, segs_=2, angle=30):
    """Unity の xy 平面の多角形（中心 c）を z0〜z1 に押し出す"""
    return plate(name, pts, (c[0], c[1], 0.0), (1, 0, 0), (0, 1, 0), (0, 0, 1), z0, z1, material, bev, segs_, angle)


def revolve(name, base, axis, prof, material, seg=32, closed=False):
    """軸 axis まわりの回転体。prof=[(半径, 軸方向の高さ)]。closed=True で断面が閉じた輪（ドーナツ状）。
    半径 0 の点は軸上の1点にまとめる"""
    a = Vector(axis).normalized()
    t = Vector((1, 0, 0)) if abs(a.x) < 0.9 else Vector((0, 1, 0))
    e1 = a.cross(t).normalized()
    e2 = a.cross(e1)
    base = Vector(base)
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for r, h in prof:
        if r < 1e-7:
            rings.append([bm.verts.new(U(*(base + a * h)))])
        else:
            rings.append([bm.verts.new(U(*(base + a * h + (e1 * math.cos(2 * math.pi * i / seg) +
                                                           e2 * math.sin(2 * math.pi * i / seg)) * r)))
                          for i in range(seg)])
    n = len(prof)
    for k in range(n if closed else n - 1):
        r0, r1 = rings[k], rings[(k + 1) % n]
        for i in range(seg):
            j = (i + 1) % seg
            if len(r0) == 1 and len(r1) == 1:
                continue
            if len(r0) == 1:
                bm.faces.new((r0[0], r1[j], r1[i]))
            elif len(r1) == 1:
                bm.faces.new((r0[i], r0[j], r1[0]))
            else:
                bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    if not closed:
        if len(rings[0]) > 1:
            bm.faces.new(list(reversed(rings[0])))
        if len(rings[-1]) > 1:
            bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    return hq._obj(name, me, material)


def wire(name, pts, r, material, seg=12):
    """Unity 座標の点列（すでに滑らか）に沿った丸い針金"""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = r
    cu.bevel_resolution = max(1, seg // 4 - 1)
    cu.use_fill_caps = True
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        v = U(*p)
        sp.points[i].co = (v.x, v.y, v.z, 1.0)
    co = bpy.data.objects.new(name + "_c", cu)
    bpy.context.scene.collection.objects.link(co)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(co.evaluated_get(dg), depsgraph=dg)
    bpy.data.objects.remove(co, do_unlink=True)
    bpy.data.curves.remove(cu)
    return hq._obj(name, me, material)


def ribbon(name, path, wdir, w, material, thick, offset=0.0, nu=4, fillet=0.0):
    """Unity 座標の折れ線 path に沿った帯（幅の向き wdir、幅 w）。紐・テープ・ストラップ"""
    pts = hq.fillet_path(path, fillet, k=6) if fillet > 0 else [Vector(p) for p in path]
    clean = [pts[0]]
    for p in pts[1:]:
        if (p - clean[-1]).length > 1e-6:
            clean.append(p)
    L = [0.0]
    for p0, p1 in zip(clean, clean[1:]):
        L.append(L[-1] + (p1 - p0).length)
    wd = Vector(wdir).normalized()

    def fn(u, s):
        i = min(max(bisect.bisect_left(L, s - 1e-9), 0), len(L) - 1)
        p = clean[i]
        return tuple(p + wd * u)
    return surf(name, segs((-w / 2, w / 2, nu)), L, fn, material, thick, offset)


def capsules(name, pieces, r_side, r_up, material, up=(0, 0, -1), n=6):
    """糸目・縫い目：両端のとがった細長い粒をまとめて1つのメッシュに。pieces=[(p0, p1)]、up は面の外向き"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    upv = Vector(up).normalized()
    for p0, p1 in pieces:
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        ax = d.normalized()
        e1 = ax.cross(upv).normalized()
        e2 = e1.cross(ax).normalized()
        if e2.dot(upv) < 0:
            e2 = -e2
        tip0 = bm.verts.new(U(*p0))
        tip1 = bm.verts.new(U(*p1))
        rings = []
        for f in (0.2, 0.5, 0.8):
            c = p0 + d * f
            rs = 1.0 if f == 0.5 else 0.85
            rings.append([bm.verts.new(U(*(c + e1 * math.cos(2 * math.pi * i / n) * r_side * rs +
                                            e2 * math.sin(2 * math.pi * i / n) * r_up * rs))) for i in range(n)])
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((tip0, rings[0][j], rings[0][i]))
            for a, b in zip(rings, rings[1:]):
                bm.faces.new((a[i], a[j], b[j], b[i]))
            bm.faces.new((rings[-1][i], rings[-1][j], tip1))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    return hq._obj(name, me, material)


def cut(o, cutters, op="DIFFERENCE"):
    """ブーリアン（先に付けた面取りなどの後に掛ける）。cutters は消す"""
    for c in cutters:
        m = o.modifiers.new("Bool", "BOOLEAN")
        m.operation = op
        m.object = c
        m.solver = "EXACT"
        m.use_self = True                    # まとめた刃どうしが重なっていても正しく
        c.hide_set(True)
        c.hide_render = True
    hq.apply_mods(o)
    for c in cutters:
        bpy.data.objects.remove(c, do_unlink=True)
    bpy.context.view_layer.update()          # 消した物が view_layer に残って join で None になるのを防ぐ
    return o


def one(objs, name):
    """ブーリアンの刃などを1つにまとめる"""
    return join(objs, name) if len(objs) > 1 else objs[0]


def mat_index(o, material):
    mats_ = o.data.materials
    for i, m in enumerate(mats_):
        if m == material:
            return i
    mats_.append(material)
    return len(mats_) - 1


def paint(o, material, test):
    """面の中心と法線（Unity 座標）で test(c, n) が真の面に材質を塗る"""
    idx = mat_index(o, material)
    for p in o.data.polygons:
        if test(unity(p.center), unity(p.normal)):
            p.material_index = idx


def uv_rect(o, material, x0, x1, y0, y1):
    """その材質の面だけ、Unity の xy で 0〜1 の UV を張り直す（写真の面）"""
    idx = mat_index(o, material)
    me = o.data
    uvl = me.uv_layers.active or me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        if p.material_index != idx:
            continue
        for li in p.loop_indices:
            c = unity(me.vertices[me.loops[li].vertex_index].co)
            uvl.data[li].uv = ((c.x - x0) / (x1 - x0), (c.y - y0) / (y1 - y0))


def xform(o, fn):
    """メッシュの各点を Unity 座標の関数で動かす（回転・平行移動など）"""
    for v in o.data.vertices:
        v.co = U(*fn(unity(v.co)))
    o.data.update()
    return o


def rot_z(p, deg, pivot=(0.0, 0.0)):
    a = math.radians(deg)
    x, y = p[0] - pivot[0], p[1] - pivot[1]
    return (pivot[0] + x * math.cos(a) - y * math.sin(a), pivot[1] + x * math.sin(a) + y * math.cos(a))


def rr(w, h, r, k=6):
    return hq.rrect(w, h, r, k)


def resample(pts, step, closed=True):
    """折れ線を等間隔の点に（縫い目の並び用）"""
    P = [Vector(p) for p in pts] + ([Vector(pts[0])] if closed else [])
    L = [0.0]
    for a, b in zip(P, P[1:]):
        L.append(L[-1] + (b - a).length)
    out = []
    s = 0.0
    while s < L[-1] - 1e-9:
        i = min(bisect.bisect_right(L, s) - 1, len(P) - 2)
        t = (s - L[i]) / max(1e-12, L[i + 1] - L[i])
        out.append(P[i].lerp(P[i + 1], t))
        s += step
    return out, L[-1]


# ============================== 1. 紙1枚 ==============================

A4 = (0.21, 0.297)


def sheet(M):
    """A4 の紙1枚（厚み 0.8mm）。上下の端が奥へゆるく反り、左右の端は小さく波打つ。右下の角が手前に折れている"""
    W, H = A4
    T = 0.0008
    hx, hy = W / 2, H / 2
    fx, fy = 0.09, 0.13                      # 平らな範囲（＝印字面）
    Lc = 0.026                               # 折れた角の大きさ
    a = (hx - Lc, -hy)
    n = (1 / math.sqrt(2), -1 / math.sqrt(2))
    ang, R = math.radians(38), 0.003

    def fn(u, v):
        tx = outside(u, -fx, fx, -hx, hx)
        ty = outside(v, -fy, fy, -hy, hy)
        z = (0.0024 if v > 0 else 0.0015) * ty * ty
        z += 0.0010 * tx * tx * (0.55 + 0.45 * math.sin(v * 31 + (1.3 if u > 0 else 0.2)))
        uu, vv, w = fold(u, v, a, n, ang, R)
        return (uu, vv, z + w)
    us = segs((-hx, -fx, 6), (-fx, fx, 6), (fx, hx, 7))
    vs = segs((-hy, -fy, 8), (-fy, fy, 8), (fy, hy, 8))
    o = surf("ISheet", us, vs, fn, M["INS_Paper"], T, cuts=fold_cuts(a, n, ang, R))
    finish(o, 1.0, angle=50)
    return [o], [rect((0, 0, 0), (2 * fx, 2 * fy))]


# ============================== 2. ホチキス留めの報告書 ==============================

def report(M):
    """A4 4枚を左上でホチキス留め。下の紙はホチキスを軸に 1〜2° 回ってずれ、留め具から離れるほど紙のすき間が開く"""
    W, H = A4
    T, GAP = 0.00035, 0.00008
    hx, hy = W / 2, H / 2
    fx, fy = 0.09, 0.13
    st = (-hx + 0.0105, hy - 0.0105)         # ホチキスの位置

    def Z(x, y):
        """束に共通の反り（下の端が奥へ、右の端が波打つ）"""
        tx = outside(x, -fx, fx, -hx, hx)
        ty = outside(y, -fy, fy, -hy, hy)
        z = (0.0022 if y < 0 else 0.0005) * ty * ty
        z += (0.0012 * (0.6 + 0.4 * math.sin(y * 27)) if x > 0 else 0.0004) * tx * tx
        return z

    def spread(x, y):
        d = math.hypot(x - st[0], y - st[1])
        return 1.0 + 2.5 * sstep((d - 0.03) / 0.3)

    out = []
    us = segs((-hx, -fx, 7), (-fx, fx, 6), (fx, hx, 7))
    vs = segs((-hy, -fy, 8), (-fy, fy, 8), (fy, hy, 7))
    for i, (deg, dx, dy) in enumerate(((0, 0, 0), (-1.3, 0.0007, -0.0005), (0.9, -0.0006, -0.0009), (-1.9, 0.0012, 0.0002))):
        def fn(u, v, i=i, deg=deg, dx=dx, dy=dy):
            x, y = rot_z((u, v), deg, st)
            x, y = x + dx, y + dy
            return (x, y, Z(x, y) + i * (T + GAP) * spread(x, y))
        o = surf(f"IRep{i}", us, vs, fn, M["INS_Paper"], T)
        out.append(finish(o, 1.0, angle=50))
    # ホチキスの針：表の山と、裏で内へ折れた足
    d = Vector((1, 1, 0)).normalized()
    c = Vector((st[0], st[1], 0))
    r = 0.0003
    zf = Z(*st) - r
    zb = Z(*st) + 3 * (T + GAP) * spread(*st) + T + r
    half = 0.006
    pts = []
    for s, z in ((-0.0012, zb), (-half, zb), (-half, zf), (half, zf), (half, zb), (0.0012, zb)):
        pts.append(tuple(c + d * s + Vector((0, 0, z))))
    stp = hq.pipe("IRepStaple", pts, r, M["INS_Metal"], bend=0.0004, seg=8)
    out.append(finish(stp, 1.0, angle=60))
    return out, [rect((0, 0, 0), (2 * fx, 2 * fy))]


# ============================== 3. マニラフォルダー ==============================

def gem_clip(name, cx, y_top, z_front, z_back, M, L=0.036, W=0.0095, r=0.00045):
    """ゼムクリップ。上の曲がり（T2）が紙の縁を越え、外側の大きな輪が表、内側の舌が裏に回る。
    z_front(y) / z_back(y) は針金の中心の z"""
    ro = W / 2 - r
    ri = 0.0017
    t2c, t2r = (ro - ri) / 2, (ro + ri) / 2           # 上の曲がり：内側左の足(-ri) → 外側右の足(+ro)
    s_t2 = t2r
    s3 = L - r - ro
    s1 = L * 0.68
    pts = []                                         # (t, s, 表か)

    def arc(ct, cs, rad, a0, a1, side, n=12):
        for i in range(n + 1):
            a = a0 + (a1 - a0) * i / n
            pts.append((ct + rad * math.cos(a), cs + rad * math.sin(a), side))
    pts.append((ri, 0.008, 0))                        # 裏の舌の端
    arc(0.0, s1, ri, 0.0, math.pi, 0)                 # 舌の下の曲がり（裏）: +ri → -ri
    arc(t2c, s_t2, t2r, math.pi, 2 * math.pi, None, 16)   # 上の曲がり（紙の縁の上で裏→表）
    arc(0.0, s3, ro, 0.0, math.pi, 1)                 # 外側の下の曲がり（表）: +ro → -ro
    pts.append((-ro, 0.0095, 1))                      # 表の外側の足の端
    out = []
    n_t2 = 17
    for k, (t, s, side) in enumerate(pts):
        y = y_top - s
        if side is None:
            j = k - 14                                # 上の曲がりの中の位置（0〜16）
            w = sstep(j / (n_t2 - 1))
            z = z_back(y) * (1 - w) + z_front(y) * w
        else:
            z = z_front(y) if side == 1 else z_back(y)
        out.append((cx + t, y, z))
    return wire(name, out, r, M["INS_Metal"], seg=10)


def folder(M):
    """マニラフォルダー（閉じた状態、厚み 6mm）。底で U 字に折れ、裏表紙の上に見出しのタブ（ラベル付き）。
    中に紙の束。表紙の上にクリップで紙1枚が留めてある（その紙が印字面）"""
    W = 0.235
    hw = W / 2
    yb, yF, yB = -0.155, 0.138, 0.143        # 底の外側・表紙の上端・裏表紙の上端（タブを除く）
    tc, zo = 0.0005, 0.003                   # 表紙の厚み・外面の z（±）
    out = []
    # 表紙〜底の折り目〜裏表紙を1枚の面で（外面を作って内側へ厚みを付ける）
    L1 = yB - (yb + zo)
    Lb = math.pi * zo
    L2 = yF - (yb + zo)
    S = L1 + Lb + L2

    def prof(s):
        if s <= L1:
            return yB - s, zo
        if s <= L1 + Lb:
            ph = (s - L1) / zo
            return (yb + zo) - zo * math.sin(ph), zo * math.cos(ph)
        return yb + zo + (s - L1 - Lb), -zo

    def fn(u, s):
        y, z = prof(s)
        # 底の近くの折り筋（2本）：表紙・裏表紙とも内側へ少しへこむ
        for ys in (yb + zo + 0.007, yb + zo + 0.014):
            z -= math.copysign(0.00022, z) * math.exp(-((y - ys) / 0.0007) ** 2) if abs(z) > zo * 0.99 else 0.0
        return (u, y, z)
    us = segs((-hw, hw, 24))
    ss = segs((0, L1 - 0.03, 6), (L1 - 0.03, L1, 16), (L1, L1 + Lb, 14), (L1 + Lb, L1 + Lb + 0.03, 16), (L1 + Lb + 0.03, S, 6))
    cov = surf("IFldCover", us, ss, fn, M["INS_Manila"], tc)
    out.append(finish(cov, 1.0, angle=50))
    # 見出しのタブ（裏表紙の上端に続く）とラベル
    tx0, tx1, th = 0.018, 0.104, 0.012
    tab = [(tx0, 0.0), (tx1, 0.0)]
    for i in range(7):                                    # 右肩（斜め＋丸み）
        a = math.radians(-30 + 120 * i / 6)
        tab.append((tx1 - 0.004 - 0.003 + 0.003 * math.cos(a), th - 0.003 + 0.003 * math.sin(a)))
    for i in range(7):
        a = math.radians(90 + 120 * i / 6)
        tab.append((tx0 + 0.004 + 0.003 + 0.003 * math.cos(a), th - 0.003 + 0.003 * math.sin(a)))
    t = pxy("IFldTab", tab, zo - tc, zo, M["INS_Manila"], c=(0.0, yB))
    out.append(finish(t, 1.0, angle=40))
    lab = pxy("IFldLabel", rr(0.07, 0.0078, 0.001), zo - tc - 0.00016, zo - tc - 0.00002, M["INS_Paper"],
              c=((tx0 + tx1) / 2, yB + 0.0058))
    out.append(finish(lab, 1.0, angle=40))
    # 中の紙の束（少しずつずれて、上・右の縁から層が見える）
    rnd = random.Random(31)
    n = 7
    for k in range(n):
        z0 = -(zo - tc) + 0.0001 + k * 0.00064
        w, h = 0.205 + rnd.uniform(-0.002, 0.002), 0.281 + rnd.uniform(-0.002, 0.001)
        cx, cy = rnd.uniform(-0.002, 0.004), yb + zo + 0.0012 + h / 2 + rnd.uniform(0.0, 0.002)
        o = hq.box(f"IFldStack{k}", (0, 0, 0), (w, h, 0.0005), M["INS_Paper"], bev=0.00018, segs=1)
        deg = rnd.uniform(-0.5, 0.5)

        def place(p, cx=cx, cy=cy, z0=z0, deg=deg):
            x, y = rot_z((p.x, p.y), deg)
            return (x + cx, y + cy, p.z + z0 + 0.00025)
        xform(o, place)
        out.append(finish(o, 1.0, angle=40))
    # 表紙に留めた紙（下の角が少し浮く）
    T = 0.0003
    sx, sy0, sy1 = 0.002, -0.138, 0.132
    shw = 0.1
    fx0, fx1, fy0, fy1 = sx - 0.088, sx + 0.088, sy0 + 0.012, sy1 - 0.036
    zs = -zo - 0.00005 - T

    def fs(u, v):
        x, y = sx + u, v
        tx = outside(x, fx0, fx1, sx - shw, sx + shw)
        ty = outside(y, fy0, sy1, sy0, sy1 + 0.001)
        lift = 0.0012 * ty * ty * (1.0 + 0.6 * max(0.0, (x - sx) / shw)) + 0.0004 * tx * tx
        return (x, y, zs - lift)
    us = segs((-shw, fx0 - sx, 5), (fx0 - sx, fx1 - sx, 6), (fx1 - sx, shw, 5))
    vs = segs((sy0, fy0, 6), (fy0, sy1, 10))
    sh = surf("IFldSheet", us, vs, fs, M["INS_Paper"], T)
    out.append(finish(sh, 1.0, angle=50))
    # クリップ（表紙の上端を挟む）
    y_top = yF + 0.0035
    sheet_top = sy1

    def zf(y):
        on = sstep((sheet_top + 0.0006 - y) / 0.0012)     # 紙の上では紙の厚みの分だけ手前
        return -zo - 0.00045 - 0.00035 * on
    clip = gem_clip("IFldClip", -0.052, y_top, zf, lambda y: -(zo - tc) + 0.00045, M)
    out.append(finish(clip, 1.0, angle=60))
    return out, [rect(((fx0 + fx1) / 2, (fy0 + fy1) / 2, zs), (fx1 - fx0, fy1 - fy0))]


# ============================== 4. 封筒と便箋 ==============================

def letter(M):
    """長形3号の封筒（ふたが開いている）を後ろに、三つ折りの跡が残る便箋を手前に"""
    out = []
    # 便箋
    W, H, T = 0.18, 0.25, 0.0003
    hx, hy = W / 2, H / 2
    fx, fy = 0.078, 0.113
    creases = (-H / 6, H / 6)

    def fl(u, v):
        tx = outside(u, -fx, fx, -hx, hx)
        ty = outside(v, -fy, fy, -hy, hy)
        z = 0.0
        for yc in creases:                                          # 折り目の谷（奥へ）
            z += 0.0006 * math.exp(-((v - yc) / 0.0022) ** 2)
            z += 0.0012 * math.exp(-((v - yc) / 0.0035) ** 2) * tx * tx   # 端では折り目の跡が強い
        z -= (0.0019 if v > 0 else 0.0013) * ty * ty                  # 上下の端は手前へ（折り癖）
        z += 0.0008 * tx * tx * math.sin(v * 38 + (0.4 if u > 0 else 2.1))
        return (u, v, z)
    vs = segs((-hy, -fy, 7), (-fy, creases[0] - 0.009, 4), (creases[0] - 0.009, creases[0] + 0.009, 18),
              (creases[0] + 0.009, creases[1] - 0.009, 4), (creases[1] - 0.009, creases[1] + 0.009, 18),
              (creases[1] + 0.009, fy, 4), (fy, hy, 7))
    us = segs((-hx, -fx, 6), (-fx, fx, 8), (fx, hx, 6))
    lt = surf("ILetSheet", us, vs, fl, M["INS_PaperCream"], T)
    out.append(finish(lt, 1.0, angle=50))
    # 封筒（前と後ろの2枚が縁でくっついた袋。上が開いていて、後ろの紙から続くふたが上へ開く。裏の中央に貼り合わせ）
    ew, eh = 0.12, 0.235
    ehx, ehy = ew / 2, eh / 2
    zc = 0.004                               # 便箋の端の波・折り目の谷より奥
    deg, ox, oy = 10.0, -0.047, 0.047
    tp = 0.00012

    def puff(a, b):
        pa = max(0.0, 1 - (abs(a) / ehx) ** 4)
        pb = max(0.0, 1 - (abs(b) / ehy) ** 4)
        p = 0.00015 + 0.0005 * pa * pb
        p += 0.0006 * sstep((b - (ehy - 0.035)) / 0.035) * pa ** 0.5      # 口のまわりは開く
        return p

    def place(a, b, z):
        x, y = rot_z((a, b), deg)
        return (x + ox, y + oy, z)
    ua = segs((-ehx, ehx, 16))
    vb = segs((-ehy, ehy - 0.035, 16), (ehy - 0.035, ehy, 8))
    ef = surf("IEnvFront", ua, vb, lambda a, b: place(a, b, zc - puff(a, b)), M["INS_Paper"], tp)
    eb = surf("IEnvBack", ua, vb, lambda a, b: place(a, b, zc + puff(a, b)), M["INS_Paper"], tp)
    # 裏の中央の貼り合わせ（センター貼り）
    es = surf("IEnvSeam", segs((-0.007, 0.007, 4)), segs((-ehy + 0.001, ehy - 0.001, 16)),
              lambda a, b: place(a, b, zc + puff(a, b) + tp + 0.00003), M["INS_Paper"], tp)
    # ふた：付け根で奥へ折れて（丸み）、上へ台形に開く。上の角は丸い
    fh, ft, fr = 0.03, math.radians(24), 0.0015
    top_w = 0.62

    def ff(a, s):
        k = s / fh
        half = ehx * (1 - (1 - top_w) * k)
        # 上の角の丸み：上端の近くでは幅を丸く絞る
        rc = 0.005
        if s > fh - rc:
            dy = s - (fh - rc)
            half -= rc - math.sqrt(max(0.0, rc * rc - dy * dy))
        x = a / ehx * half
        bend = fr * ft
        if s < bend:
            ph = s / fr
            yb_, zb_ = fr * math.sin(ph), fr * (1 - math.cos(ph))
        else:
            yb_ = fr * math.sin(ft) + (s - bend) * math.cos(ft)
            zb_ = fr * (1 - math.cos(ft)) + (s - bend) * math.sin(ft)
        return place(x, ehy + yb_, zc + puff(a / ehx * half, ehy) + zb_)
    efl = surf("IEnvFlap", segs((-ehx, ehx, 16)), segs((0, fr * ft, 3), (fr * ft, fh - 0.005, 6), (fh - 0.005, fh, 5)), ff,
               M["INS_Paper"], tp)
    for o in (ef, eb, es, efl):
        out.append(finish(o, 1.0, angle=50))
    return out, [rect((0, 0, 0), (2 * fx, 2 * fy))]


# ============================== 5. 開いた手帳 ==============================

def strip_y(name, top, bot, y0, y1, side, material):
    """xz の断面（上の線 top と下の線 bot、同じ数の点）を y0〜y1 に押し出す。side=-1 で左右反転"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()

    def V(x, y, z):
        return bm.verts.new(U(side * x, y, z))
    T0 = [V(x, y0, z) for x, z in top]
    T1 = [V(x, y1, z) for x, z in top]
    B0 = [V(x, y0, z) for x, z in bot]
    B1 = [V(x, y1, z) for x, z in bot]
    n = len(top)
    for i in range(n - 1):
        bm.faces.new((T0[i], T0[i + 1], T1[i + 1], T1[i]))
        bm.faces.new((B0[i], B1[i], B1[i + 1], B0[i + 1]))
        bm.faces.new((T0[i], B0[i], B0[i + 1], T0[i + 1]))
        bm.faces.new((T1[i], T1[i + 1], B1[i + 1], B1[i]))
    bm.faces.new((T0[0], T1[0], B1[0], B0[0]))
    bm.faces.new((T0[-1], B0[-1], B1[-1], T1[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    return hq._obj(name, me, material)


def notebook(M):
    """開いた小さな革の手帳。表紙（濃い革、縁にステッチ）の上に左右のページの束。綴じ目に向かってページが沈み、
    真ん中に綴じ糸。印字面は左ページ（rects[0]）と右ページ（rects[1]）"""
    out = []
    pw, ph = 0.105, 0.148
    Hp, N, g, c0 = 0.003, 7, 0.016, 0.15
    rnd = random.Random(5)

    def c(x):
        t = min(1.0, max(0.0, x / g))
        return c0 + (1 - c0) * (1 - (1 - t) ** 3)
    xin = [0.0006 + (g - 0.0006) * (i / 16) ** 1.5 for i in range(17)]
    for side in (-1, 1):
        for k in range(N):
            Ht, Hb = Hp * (1 - k / N), Hp * (1 - (k + 1) / N)
            gap = 0.1 * Hp / N
            xo = pw + 0.0008 + (0.0 if k == 0 else rnd.uniform(-0.0005, 0.0002))
            xs = xin + [g + (xo - g) * i / 3 for i in (1, 2, 3)]
            top = [(x, -Ht * c(x)) for x in xs]
            bot = [(x, -(Hb + gap) * c(x)) for x in xs]
            j0 = 0.0 if k == 0 else rnd.uniform(-0.0004, 0.0003)
            j1 = 0.0 if k == 0 else rnd.uniform(-0.0003, 0.0004)
            o = strip_y(f"INbPg{side}_{k}", top, bot, -ph / 2 + j0, ph / 2 + j1, side, M["INS_PaperCream"])
            hq.bevel(o, 0.00012, 2, 40)
            out.append(finish(o, 1.0, angle=30))
    # 表紙（見開きで1枚の革）
    cw, chh = 2 * pw + 0.012, ph + 0.009
    cv = pxy("INbCover", rr(cw, chh, 0.006, 8), 0.0, 0.0024, M["INS_Leather"], bev=0.0008, segs_=3, angle=40)
    out.append(finish(cv, 1.0, angle=40))
    # 背（開いた表紙の裏側の丸み）
    sp = surf("INbSpine", segs((-0.012, 0.012, 16)), segs((-chh / 2 + 0.0015, chh / 2 - 0.0015, 2)),
              lambda u, v: (u, v, 0.0024 + 0.0011 * math.cos(u / 0.012 * math.pi / 2) ** 2), M["INS_Leather"], 0.0005)
    out.append(finish(sp, 1.0, angle=40))
    # 縁のステッチ（表紙の縁から 2.2mm）
    path = [(x, y, 0.0) for x, y in rr(cw - 0.0044, chh - 0.0044, 0.006 - 0.0022, 8)]
    ps, _ = resample(path, 0.0034)
    pieces = []
    lift = Vector((0, 0, -0.00005))
    for i, p in enumerate(ps):
        # 次の点の向きへ 2.2mm の糸目
        d = (ps[(i + 1) % len(ps)] - p).normalized()
        pieces.append((tuple(p + lift), tuple(p + d * 0.0022 + lift)))
    st = capsules("INbStitch", pieces, 0.00032, 0.00018, M["INS_PaperCream"])
    out.append(finish(st, 1.0, angle=80))
    # 綴じ糸（ページの谷の底。谷の影の中で白く見える）
    z = -Hp * c0 - 0.00012
    th = [((0, y, z), (0, y + 0.017, z)) for y in (-0.066, -0.037, -0.008, 0.021, 0.05)]
    tt = capsules("INbThread", th, 0.00028, 0.00022, M["INS_PaperCream"])
    out.append(finish(tt, 1.0, angle=80))
    pr = []
    x0, x1 = g + 0.004, pw - 0.006
    for side in (-1, 1):
        pr.append(rect((side * (x0 + x1) / 2, 0, -Hp), (x1 - x0, ph - 0.014)))
    return out, pr


# ============================== 6. 職員証 ==============================

def card(M):
    """透明の硬質ケースに入った職員証（横長）。左に顔写真、右 6 割が印字面。上の穴にストラップと金属のクリップ"""
    out = []
    cw, chh, ct = 0.086, 0.054, 0.0008
    c = pxy("ICard", rr(cw, chh, 0.003, 6), 0.0, ct, M["INS_Paper"], bev=0.00022, segs_=2, angle=40)
    hq.apply_mods(c)
    # 写真の範囲で表の面を切って INS_Photo を塗る
    px0, px1, py0, py1 = -0.0375, -0.0135, -0.019, 0.013
    bm = bmesh.new()
    bm.from_mesh(c.data)
    for co, no in (((px0, 0, 0), (1, 0, 0)), ((px1, 0, 0), (1, 0, 0)), ((0, py0, 0), (0, 1, 0)), ((0, py1, 0), (0, 1, 0))):
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-7, plane_co=U(*co), plane_no=U(*no) - U(0, 0, 0))
    bm.to_mesh(c.data)
    bm.free()
    finish(c, 1.0, angle=40)
    paint(c, M["INS_Photo"], lambda p, n: n.z < -0.9 and px0 < p.x < px1 and py0 < p.y < py1)
    uv_rect(c, M["INS_Photo"], px0, px1, py0, py1)
    out.append(c)
    # ケース（中身の入った透明の板。上に紐を通す長穴）
    hy0, hy1 = -0.0305, 0.0357
    hc = (0.0, (hy0 + hy1) / 2)
    holder = pxy("ICardHolder", rr(0.0935, hy1 - hy0, 0.0042, 8), -0.0011, 0.0019, M["INS_PlasticClear"], c=hc,
                 bev=0.0005, segs_=3, angle=40)
    hq.apply_mods(holder)
    slot_y = 0.0312
    slot = pxy("ICardSlotCut", rr(0.0145, 0.0036, 0.0017, 6), -0.004, 0.005, M["INS_PlasticClear"], c=(0.0, slot_y))
    cut(holder, [slot])
    out.append(finish(holder, 1.0, angle=40))
    # ストラップ（前→長穴→後ろ→前の帯に重ねてスナップで留める）
    zh0, zh1 = -0.0011, 0.0019
    ts = 0.0006
    path = [(0, 0.066, 0.0), (0, 0.058, -0.00035), (0, 0.047, -0.00046), (0, 0.037, zh0 - 0.00045),
            (0, slot_y + 0.0006, zh0 - 0.00045), (0, slot_y, zh0 + 0.0003), (0, slot_y, zh1 - 0.0003),
            (0, slot_y + 0.0006, zh1 + 0.00045), (0, 0.037, zh1 + 0.00045), (0, 0.046, 0.00046), (0, 0.052, 0.00046)]
    strap = ribbon("ICardStrap", path, (1, 0, 0), 0.011, M["INS_PlasticBlack"], ts, 0.0, nu=4, fillet=0.0012)
    out.append(finish(strap, 1.0, angle=45))
    for zs in (-1, 1):
        z0 = zs * (0.00046 + ts / 2)
        dome = revolve(f"ICardSnap{zs}", (0, 0.049, z0), (0, 0, zs),
                       [(0.0, 0.0), (0.0038, 0.0), (0.0038, 0.00035), (0.0031, 0.0008), (0.0016, 0.001), (0.0, 0.00105)],
                       M["INS_Metal"], 24)
        out.append(finish(dome, 1.0, angle=35))
    # 金属のクリップ（ワニ口）：ストラップの端をかしめる筒、台の板、前の顎、軸
    crimp = hq.box("ICardCrimp", (0, 0.0645, 0.0), (0.0132, 0.0065, 0.0024), M["INS_Metal"], bev=0.0006, segs=3)
    out.append(finish(crimp, 1.0, angle=40))
    base = pxy("ICardClipBase", rr(0.0125, 0.024, 0.0045, 6), 0.0002, 0.0008, M["INS_Metal"], c=(0.0, 0.078),
               bev=0.0002, segs_=2)
    out.append(finish(base, 1.0, angle=40))

    def jaw(u, s):
        # 軸（s=0.35）より下はつまみで手前へ反り、上は先で台に当たる
        y = 0.0665 + s * 0.022
        if s < 0.35:
            z = -0.0012 - 0.0022 * ((0.35 - s) / 0.35) ** 1.4
        else:
            z = -0.0012 + 0.0011 * ((s - 0.35) / 0.65) ** 1.2
        z -= 0.00035 * (1 - (u / 0.0062) ** 2)
        return (u, y, z)
    jw = surf("ICardJaw", segs((-0.0062, 0.0062, 8)), segs((0, 1, 16)), jaw, M["INS_Metal"], 0.0005)
    out.append(finish(jw, 1.0, angle=50))
    pin = hq.cyl_between("ICardPin", (-0.0068, 0.0742, -0.0004), (0.0068, 0.0742, -0.0004), 0.0007, M["INS_Metal"], 12)
    out.append(finish(pin, 1.0, angle=40))
    return out, [rect((0.016, 0.0, 0.0), (0.047, 0.047))]


# ============================== 7. 写真立て ==============================

def frame_ring_prof(name, W, H, prof, material):
    """額縁：外周からの距離 d と奥行き z の断面 prof（閉じた並び）を長方形に沿って回す（角は留め継ぎ）"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    loops = []
    for d, z in prof:
        hx, hy = W / 2 - d, H / 2 - d
        loops.append([bm.verts.new(U(x, y, z)) for x, y in ((hx, hy), (-hx, hy), (-hx, -hy), (hx, -hy))])
    n = len(loops)
    for k in range(n):
        a, b = loops[k], loops[(k + 1) % n]
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    return hq._obj(name, me, material)


def photo(M):
    """木の写真立て（縦長 0.13 x 0.18）。丸みのある額縁、ガラス、セピアの写真、裏板、留め金、折りたたみの脚"""
    out = []
    W, H = 0.13, 0.18
    zb = 0.006                                   # 額縁の裏面
    prof = [(0.0, zb), (0.0, -0.0015), (0.0004, -0.0030), (0.0014, -0.0040), (0.0035, -0.0047), (0.0065, -0.0049),
            (0.0095, -0.0044), (0.0118, -0.0031), (0.0128, -0.0018), (0.0133, -0.0008), (0.0135, 0.0005),
            (0.0095, 0.0005), (0.0095, zb)]
    fr = frame_ring_prof("IPhFrame", W, H, prof, M["INS_Wood"])
    out.append(finish(fr, 1.0, angle=35))
    gw, gh = W - 2 * 0.0095 - 0.001, H - 2 * 0.0095 - 0.001
    gl = hq.box("IPhGlass", (0, 0, 0.0015), (gw, gh, 0.0018), M["INS_Glass"], bev=0.0003, segs=1)
    out.append(finish(gl, 1.0, angle=40))
    ph = surf("IPhPhoto", segs((-gw / 2, gw / 2, 1)), segs((-gh / 2, gh / 2, 1)), lambda u, v: (u, v, 0.0025),
              M["INS_Photo"], 0.0003, back=M["INS_Paper"], rim=M["INS_Paper"])
    finish(ph, 1.0, angle=40)
    uv_rect(ph, M["INS_Photo"], -gw / 2, gw / 2, -gh / 2, gh / 2)
    out.append(ph)
    bd = hq.box("IPhBack", (0, 0, 0.00435), (gw, gh, 0.0029), M["INS_Board"], bev=0.0004, segs=1)
    out.append(finish(bd, 1.0, angle=40))
    # 裏板を押さえる回転留め金（4か所）
    for (x, y, deg) in ((-gw / 2, 0.03, 0), (gw / 2, -0.03, 180), (0.025, gh / 2, 270), (-0.025, -gh / 2, 90)):
        a = math.radians(deg)
        t = pxy(f"IPhTurn{deg}", rr(0.011, 0.0035, 0.0017, 5), zb, zb + 0.0006, M["INS_Metal"], bev=0.00015)
        xform(t, lambda p, a=a, x=x, y=y: (p.x * math.cos(a) - p.y * math.sin(a) + x, p.x * math.sin(a) + p.y * math.cos(a) + y, p.z))
        out.append(finish(t, 1.0, angle=40))
        sc = revolve(f"IPhTurnScrew{deg}", (x - 0.0035 * math.cos(a), y - 0.0035 * math.sin(a), zb + 0.0006), (0, 0, 1),
                     [(0.0, 0.0), (0.0011, 0.0), (0.0009, 0.0004), (0.0, 0.0005)], M["INS_Metal"], 16)
        out.append(finish(sc, 1.0, angle=50))
    # 脚（蝶番で裏板に付き、下と奥へ開く）
    hy_, th, Lg = 0.03, math.radians(23), 0.117
    org = Vector((0, hy_, zb - 0.0002))
    ey = Vector((0, -math.cos(th), math.sin(th)))
    ez = Vector((0, math.sin(th), math.cos(th)))
    # 脚の形（a=横、b=蝶番から先への長さ）：上 30mm → 先 22mm に細り、先の角が丸い
    leg = [(-0.015, 0.0), (0.015, 0.0)]
    for i in range(7):
        a = math.radians(90 * i / 6)
        leg.append((0.007 + 0.004 * math.cos(a), Lg - 0.004 + 0.004 * math.sin(a)))
    for i in range(7):
        a = math.radians(90 + 90 * i / 6)
        leg.append((-0.007 + 0.004 * math.cos(a), Lg - 0.004 + 0.004 * math.sin(a)))
    lg = plate("IPhLeg", leg, org, (1, 0, 0), ey, ez, 0.0, 0.0025, M["INS_Board"], bev=0.0005, segs_=2)
    out.append(finish(lg, 1.0, angle=40))
    hinge = hq.cyl_between("IPhHinge", (-0.014, hy_ + 0.0012, zb + 0.0009), (0.014, hy_ + 0.0012, zb + 0.0009), 0.001, M["INS_Metal"], 12)
    out.append(finish(hinge, 1.0, angle=40))
    leaf = hq.box("IPhHingeLeaf", (0, hy_ + 0.0055, zb), (0.028, 0.008, 0.0004), M["INS_Metal"], bev=0.00012, segs=1)
    out.append(finish(leaf, 1.0, angle=40))
    leaf2 = plate("IPhHingeLeaf2", rr(0.028, 0.008, 0.001, 3), org + ey * 0.005, (1, 0, 0), ey, ez, 0.0025, 0.0029, M["INS_Metal"])
    out.append(finish(leaf2, 1.0, angle=40))
    return out, []


# ============================== 8. 新聞 ==============================

def newspaper(M):
    """四つ折りの新聞（横 0.272 x 縦 0.203）。紙の組（4組を二つ折り）を左の端で二つ折りにし、さらに上の端で二つ折りにした形。
    上と左は丸い折り目、右と下は紙の端が層になって少しずつずれて開く。表の面が印字面"""
    out = []
    W, Hh = 0.272, 0.203
    hx = W / 2
    K, tl, sp, r_in = 4, 0.00018, 0.0005, 0.00026
    r2 = [r_in + (K - 1 - k) * sp for k in range(K)]
    Z2m = r2[0] + tl / 2
    X2c = -hx + Z2m
    rho0 = Z2m + r_in
    rho_out = rho0 + Z2m
    y_c = Hh / 2 - rho_out
    AF, DL = 0.203, 0.0045
    a1, a2, AE = AF - DL, AF + DL, 2 * AF
    rnd = random.Random(8)

    def F(X2, a, Z2):
        e = min(a, AE - a)
        rb, rr_ = 1 - sstep(e / 0.018), sstep((X2 - 0.104) / 0.032)
        spread = 1 + rb * (0.45 + 0.3 * math.sin(X2 * 70 + 0.5)) + rr_ * (0.35 + 0.25 * math.sin(a * 55 + 1.2))
        rho = r_in * 0.6 + (rho0 - Z2 - r_in * 0.6) * spread
        if a <= a1:
            return (X2, y_c - (a1 - a), -rho)
        if a >= a2:
            return (X2, y_c - (a - a2), rho)
        ph = math.pi * (a - a1) / (a2 - a1)
        return (X2, y_c + rho * math.sin(ph), -rho * math.cos(ph))

    us = segs((0, 0.06, 6), (0.06, 0.38, 6), (0.38, 0.45, 5), (0.45, 0.55, 14), (0.55, 0.62, 5), (0.62, 0.94, 6), (0.94, 1.0, 6))
    vs = segs((0, 0.02, 6), (0.02, a1 - 0.012, 6), (a1 - 0.012, a1, 4), (a1, a2, 16), (a2, a2 + 0.012, 4),
              (a2 + 0.012, AE - 0.02, 6), (AE - 0.02, AE, 6))
    for k in range(K):
        r = r2[k]
        # 開いた端（右・下）は紙ごとに少しずつずれる（表の紙の端より内側の紙が出ている所もある）
        xf = hx + (0.0 if k == 0 else rnd.uniform(-0.0015, 0.0025))
        xb = hx + rnd.uniform(-0.0015, 0.0025)
        ja = 0.0 if k == 0 else rnd.uniform(-0.0022, 0.0012)
        jb = rnd.uniform(-0.0022, 0.0012)

        def G(s, r=r, xf=xf, xb=xb):
            if s <= 0.45:
                t = s / 0.45
                return xf + (X2c - xf) * t, -r
            if s >= 0.55:
                t = (s - 0.55) / 0.45
                return X2c + (xb - X2c) * t, r
            ph = math.pi * (s - 0.45) / 0.1
            return X2c - r * math.sin(ph), -r * math.cos(ph)

        def fn(s, a, ja=ja, jb=jb, G=G):
            X2, Z2 = G(s)
            aa = ja + a * (AE - ja - jb) / AE
            return F(X2, aa, Z2)
        o = surf(f"INews{k}", us, vs, fn, M["INS_Newsprint"], tl, 0.0)
        out.append(finish(o, 1.0, angle=40))
    x0, x1 = X2c + 0.007, 0.104 - 0.004
    y0, y1 = y_c - a1 + 0.02, y_c - 0.006
    return out, [rect(((x0 + x1) / 2, (y0 + y1) / 2, -rho_out), (x1 - x0, y1 - y0))]


# ============================== 9. モニター ==============================

def monitor(M):
    """小型の液晶モニター（0.38 x 0.26）。細い黒縁と少し引っ込んだ画面、背中のふくらみ、首と台。画面が印字面"""
    out = []
    PW, PH = 0.38, 0.26
    z0, z1 = -0.011, 0.009
    body = pxy("IMonBody", rr(PW, PH, 0.006, 6), z0, z1, M["INS_PlasticBlack"], bev=0.0025, segs_=3, angle=40)
    hq.apply_mods(body)
    sx0, sx1, sy0, sy1 = -0.1825, 0.1825, -0.113, 0.122
    rec = hq.span("IMonRecess", sx0, sx1, sy0, sy1, -0.02, -0.0098, M["INS_PlasticBlack"], bev=0)
    cut(body, [rec])
    out.append(finish(body, 1.0, angle=40))
    zs = -0.0104
    scr = hq.span("IMonScreen", sx0 + 0.0001, sx1 - 0.0001, sy0 + 0.0001, sy1 - 0.0001, zs, -0.0095, M["INS_Screen"], bev=0)
    out.append(finish(scr, 1.0, angle=40))
    # 背中のふくらみ（放熱の溝つき）
    bk = pxy("IMonBack", rr(0.25, 0.17, 0.02, 8), 0.006, 0.030, M["INS_PlasticBlack"], c=(0.0, -0.015), bev=0.006, segs_=4, angle=30)
    hq.apply_mods(bk)
    vents = [hq.span(f"IMonVent{i}", -0.075 + i * 0.015 - 0.0025, -0.075 + i * 0.015 + 0.0025, 0.022, 0.05, 0.027, 0.04,
                     M["INS_PlasticBlack"], bev=0) for i in range(11)]
    cut(bk, [one(vents, "IMonVents")])
    out.append(finish(bk, 1.0, angle=40))
    # 首・蝶番・台
    nk = pxy("IMonNeck", rr(0.05, 0.194, 0.006, 6), 0.027, 0.040, M["INS_PlasticBlack"], c=(0.0, -0.115), bev=0.002, segs_=3)
    out.append(finish(nk, 1.0, angle=40))
    hg = hq.cyl_between("IMonHinge", (-0.03, -0.04, 0.036), (0.03, -0.04, 0.036), 0.0095, M["INS_PlasticBlack"], 32)
    hq.bevel(hg, 0.0015, 2, 40)
    out.append(finish(hg, 1.0, angle=40))
    yb0, yb1 = -0.2235, -0.2115
    ft = plate("IMonFoot", rr(0.2, 0.15, 0.035, 10), (0.0, 0.0, 0.035), (1, 0, 0), (0, 0, 1), (0, 1, 0), yb0 + 0.0015, yb1,
               M["INS_PlasticBlack"], bev=0.003, segs_=3, angle=30)
    out.append(finish(ft, 1.0, angle=40))
    for sx in (-1, 1):
        for sz in (-1, 1):
            f = hq.cyl(f"IMonPad{sx}{sz}", (sx * 0.08, yb0, 0.035 + sz * 0.052), 0.0016, 0.008, M["INS_Rubber"], 20)
            out.append(finish(f, 1.0, angle=40))
    # 電源ボタン・ランプ（下の縁の右）、底の操作ボタン
    pb = hq.cyl_between("IMonPower", (0.168, -0.1215, z0 + 0.0002), (0.168, -0.1215, z0 - 0.0006), 0.0026, M["INS_Rubber"], 24)
    hq.bevel(pb, 0.0003, 2, 40)
    out.append(finish(pb, 1.0, angle=40))
    led = hq.cyl_between("IMonLed", (0.158, -0.1215, z0 + 0.0002), (0.158, -0.1215, z0 - 0.0002), 0.0007, M["INS_Screen"], 12)
    out.append(finish(led, 1.0, angle=40))
    for i in range(4):
        b = hq.box(f"IMonKey{i}", (0.09 + i * 0.016, -PH / 2 - 0.0004, -0.002), (0.008, 0.0016, 0.004), M["INS_Rubber"], bev=0.0005, segs=2)
        out.append(finish(b, 1.0, angle=40))
    return out, [rect((0.0, (sy0 + sy1) / 2, zs), (sx1 - sx0 - 0.0002, sy1 - sy0 - 0.0002))]


# ============================== 10. カセットテープ ==============================

def cassette(M):
    """カセットテープ（100 x 64 x 12mm）。スモークの殻（下の台形の張り出し、合わせ目の溝、窓、軸の穴、ヘッドの開口、
    つめの切り欠き）、5本のねじ、中の巻いたテープとハブとローラー、表のラベル（上の書き込み欄が印字面）"""
    out = []
    W, H = 0.1004, 0.0638
    hx, hy = W / 2, H / 2
    zt = 0.0045
    hub_y, hub_x = 0.003, 0.02125
    shell = pxy("ICasShell", rr(W, H, 0.0022, 5), -zt, zt, M["INS_PlasticSmoke"], bev=0.0006, segs_=2, angle=40)
    hq.apply_mods(shell)
    trap = [(-0.0378, -hy), (0.0378, -hy), (0.0322, -0.0196), (-0.0322, -0.0196)]
    bumps = []
    for zs in (-1, 1):
        b = pxy(f"ICasBump{zs}", trap, *sorted((zs * 0.0043, zs * 0.006)), M["INS_PlasticSmoke"], bev=0.0004, segs_=2, angle=40)
        hq.apply_mods(b)
        bumps.append(b)
    cut(shell, [one(bumps, "ICasBumps")], "UNION")
    cutters = []
    # 合わせ目の溝
    ring = hq.frame_ring("ICasSeam", lambda u, v, d: (u, v, d), W + 0.004, H + 0.004, 0.003, W - 0.0008, H - 0.0008, 0.0018,
                         -0.0002, 0.0002, M["INS_PlasticSmoke"])
    cutters.append(ring)
    # 窓のくぼみ（表裏）・軸の穴
    for zs in (-1, 1):
        cutters.append(pxy(f"ICasWin{zs}", rr(0.056, 0.017, 0.004, 6), *sorted((zs * 0.0041, zs * 0.008)), M["INS_PlasticSmoke"],
                           c=(0.0, hub_y)))
    for sx in (-1, 1):
        cutters.append(hq.cyl_between(f"ICasHole{sx}", (sx * hub_x, hub_y, -0.01), (sx * hub_x, hub_y, 0.01), 0.0045,
                                      M["INS_PlasticSmoke"], 32))
    # ヘッドの開口（下の縁）とつめの切り欠き（上の縁の裏）
    for (x, w) in ((0.0, 0.013), (-0.0265, 0.006), (0.0265, 0.006), (-0.0155, 0.005), (0.0155, 0.005)):
        cutters.append(hq.span(f"ICasOpen{x}", x - w / 2, x + w / 2, -hy - 0.002, -hy + 0.0034, -0.0035, 0.0035, M["INS_PlasticSmoke"], bev=0))
    for sx in (-1, 1):
        cutters.append(hq.span(f"ICasTab{sx}", *sorted((sx * 0.0355, sx * 0.0425)), hy - 0.0034, hy + 0.002, 0.0012, 0.006,
                               M["INS_PlasticSmoke"], bev=0))
    # ねじの座ぐり
    screws = [(-0.0462, 0.0279, -zt), (0.0462, 0.0279, -zt), (-0.0462, -0.0279, -zt), (0.0462, -0.0279, -zt), (0.0, -0.0228, -0.006)]
    for i, (x, y, z) in enumerate(screws):
        cutters.append(hq.cyl_between(f"ICasBore{i}", (x, y, z - 0.002), (x, y, z + 0.0005), 0.0017, M["INS_PlasticSmoke"], 24))
    cut(shell, cutters)
    out.append(finish(shell, 1.0, angle=35))
    # ねじ（十字の溝つきの頭と、殻の中へ伸びる軸）
    for i, (x, y, z) in enumerate(screws):
        zh = z + 0.0005
        hd = revolve(f"ICasScrew{i}", (x, y, zh), (0, 0, -1),
                     [(0.0, 0.0), (0.0014, 0.0), (0.0014, 0.00025), (0.0011, 0.00045), (0.0006, 0.00053), (0.0, 0.00055)],
                     M["INS_Metal"], 20)
        xs_ = [hq.box(f"ICasX{i}{k}", (x, y, zh - 0.0005), (0.0019, 0.00042, 0.0006) if k == 0 else (0.00042, 0.0019, 0.0006),
                      M["INS_Metal"], bev=0) for k in (0, 1)]
        cut(hd, [one(xs_, f"ICasXs{i}")])
        out.append(finish(hd, 1.0, angle=35))
        sh = hq.cyl_between(f"ICasShaft{i}", (x, y, zh), (x, y, zh + 0.0075), 0.0008, M["INS_Metal"], 12)
        out.append(finish(sh, 1.0, angle=40))
    # 中身：巻いたテープ・ハブ（歯つき）・ローラー・ピン・テープの通り道・押さえのフェルト
    for sx, R in ((-1, 0.0185), (1, 0.0135)):
        c = (sx * hub_x, hub_y, 0.0)
        pk = revolve(f"ICasPack{sx}", c, (0, 0, 1), [(0.01055, -0.0019), (R, -0.0019), (R, 0.0019), (0.01055, 0.0019)],
                     M["INS_Tape"], 48, closed=True)
        out.append(finish(pk, 1.0, angle=40))
        hb = revolve(f"ICasHub{sx}", c, (0, 0, 1), [(0.0047, -0.0021), (0.0105, -0.0021), (0.0105, 0.0021), (0.0047, 0.0021)],
                     M["INS_PlasticBlack"], 32, closed=True)
        out.append(finish(hb, 1.0, angle=40))
        for k in range(6):
            a = math.radians(60 * k + 15)
            tooth = pxy(f"ICasTooth{sx}{k}", [(-0.0006, 0.0), (0.0006, 0.0), (0.0005, 0.0014), (-0.0005, 0.0014)], -0.0021, 0.0021,
                        M["INS_PlasticBlack"])
            xform(tooth, lambda p, a=a, c=c: (c[0] + (p.x * math.cos(a) - (p.y + 0.0035) * math.sin(a)),
                                               c[1] + (p.x * math.sin(a) + (p.y + 0.0035) * math.cos(a)), p.z))
            out.append(finish(tooth, 1.0, angle=40))
    for sx in (-1, 1):
        ro = revolve(f"ICasRoller{sx}", (sx * 0.034, -0.0262, 0.0), (0, 0, 1), [(0.0, -0.0025), (0.0018, -0.0025), (0.0018, 0.0025), (0.0, 0.0025)],
                     M["INS_PlasticBlack"], 20)
        out.append(finish(ro, 1.0, angle=40))
        for (px, py, pr) in ((sx * 0.034, -0.0262, 0.0006), (sx * 0.0405, -0.0275, 0.0009)):
            pn = hq.cyl_between(f"ICasPin{sx}{px}", (px, py, -0.0038), (px, py, 0.0038), pr, M["INS_Metal"], 12)
            out.append(finish(pn, 1.0, angle=40))
    ty = -0.0262 - 0.0018 - 0.00005
    path = [(-hub_x - 0.0185 * math.cos(math.radians(25)), hub_y - 0.0185 * math.sin(math.radians(25)), 0),
            (-0.0362, -0.0232, 0), (-0.0345, ty, 0), (0.0345, ty, 0), (0.0362, -0.0232, 0),
            (hub_x + 0.0135 * math.cos(math.radians(20)), hub_y - 0.0135 * math.sin(math.radians(20)), 0)]
    tp = ribbon("ICasTapePath", path, (0, 0, 1), 0.0038, M["INS_Tape"], 0.00012, 0.0, nu=2, fillet=0.0016)
    out.append(finish(tp, 1.0, angle=50))
    pad = hq.box("ICasPad", (0.0, ty + 0.0014, 0.0), (0.0055, 0.0025, 0.003), M["INS_Rubber"], bev=0.0003, segs=1)
    out.append(finish(pad, 1.0, angle=40))
    spr = hq.box("ICasPadSpring", (0.0, ty + 0.0032, 0.0), (0.016, 0.0004, 0.0032), M["INS_Metal"], bev=0.0001, segs=1)
    out.append(finish(spr, 1.0, angle=40))
    # ラベル（表。窓の所は切り抜き）
    lx, ly0, ly1 = 0.0435, -0.0165, 0.0285
    lb = pxy("ICasLabel", rr(2 * lx, ly1 - ly0, 0.0015, 4), -zt - 0.00014, -zt - 0.00002, M["INS_Label"], c=(0.0, (ly0 + ly1) / 2))
    wc = pxy("ICasLabelCut", rr(0.06, 0.02, 0.005, 6), -0.006, -0.003, M["INS_Label"], c=(0.0, hub_y))
    cut(lb, [wc])
    out.append(finish(lb, 1.0, angle=40))
    zl = -zt - 0.00014
    return out, [rect((0.0, 0.0209, zl), (0.083, 0.0128))]


# ============================== 11. IC レコーダー ==============================

def recorder(M):
    """IC レコーダー（40 x 110 x 15mm）。黒い樹脂の胴に金属の前板。上にスピーカーの穴、小さな液晶（印字面）、
    その下にボタン。上の端にマイク2つとイヤホンの穴、側面にホールドと音量"""
    out = []
    W, H, D = 0.04, 0.11, 0.015
    zf = -D / 2
    body = pxy("IRecBody", rr(W, H, 0.007, 8), -D / 2, D / 2, M["INS_PlasticBlack"], bev=0.0016, segs_=3, angle=40)
    hq.apply_mods(body)
    # 裏：電池ぶたの合わせ目と、スピーカーの穴（丸く 19 個）
    bc = [hq.frame_ring("IRecBatCut", lambda u, v, d: (u, v - 0.024, d), 0.0304, 0.0504, 0.004, 0.0296, 0.0496, 0.0036,
                        D / 2 - 0.0003, D / 2 + 0.001, M["INS_PlasticBlack"])]
    for ring_r, cnt in ((0.0, 1), (0.0023, 6), (0.0046, 12)):
        for i in range(cnt):
            a = 2 * math.pi * i / cnt
            x, y = ring_r * math.cos(a), 0.034 + ring_r * math.sin(a)
            bc.append(hq.cyl_between(f"IRecBackHole{ring_r}{i}", (x, y, D / 2 - 0.0007), (x, y, D / 2 + 0.001), 0.00045,
                                     M["INS_PlasticBlack"], 10))
    cut(body, [one(bc, "IRecBackCuts")])
    out.append(finish(body, 1.0, angle=40))
    pz0, pz1 = zf - 0.0006, zf + 0.0001
    pl = pxy("IRecPlate", rr(0.036, 0.106, 0.0055, 8), pz0, pz1, M["INS_Metal"], bev=0.00025, segs_=2, angle=40)
    hq.apply_mods(pl)
    cutters = []
    # スピーカーの穴（互い違いの格子）
    k = 0
    for j in range(7):
        y = 0.0305 + j * 0.0024
        off = 0.0012 if j % 2 else 0.0
        for i in range(-5, 6):
            x = i * 0.0024 + off
            if abs(x) > 0.0125:
                continue
            cutters.append(hq.cyl_between(f"IRecHole{k}", (x, y, pz0 - 0.001), (x, y, pz1 + 0.0002), 0.00052, M["INS_Metal"], 10))
            k += 1
    lx0, lx1, ly0, ly1 = -0.0145, 0.0145, 0.004, 0.024
    cutters.append(hq.span("IRecLcdCut", lx0, lx1, ly0, ly1, pz0 - 0.001, pz1 + 0.0002, M["INS_Metal"], bev=0))
    # ボタンの穴
    btns = []
    for sx in (-1, 1):
        cutters.append(pxy(f"IRecMenuCut{sx}", rr(0.0083, 0.0042, 0.0021, 6), pz0 - 0.001, pz1 + 0.0002, M["INS_Metal"], c=(sx * 0.0085, -0.0035)))
        btns.append(pxy(f"IRecMenu{sx}", rr(0.0075, 0.0034, 0.0017, 6), pz0 - 0.0005, zf + 0.0006, M["INS_Rubber"], c=(sx * 0.0085, -0.0035),
                        bev=0.0003, segs_=2, angle=40))
    cy = -0.0165
    cutters.append(hq.cyl_between("IRecRingCut", (0, cy, pz0 - 0.001), (0, cy, pz1 + 0.0002), 0.0091, M["INS_Metal"], 40))
    ring = revolve("IRecRing", (0, cy, zf + 0.0006), (0, 0, -1),
                   [(0.0053, 0.0), (0.0087, 0.0), (0.0087, 0.0013), (0.0082, 0.0017), (0.0058, 0.0017), (0.0053, 0.0013)],
                   M["INS_Rubber"], 48, closed=True)
    btns.append(ring)
    play = revolve("IRecPlay", (0, cy, zf + 0.0006), (0, 0, -1),
                   [(0.0, 0.0), (0.0044, 0.0), (0.0044, 0.0012), (0.0038, 0.0017), (0.002, 0.00185), (0.0, 0.0019)], M["INS_Rubber"], 40)
    btns.append(play)
    for (x, shape) in ((-0.009, "sq"), (0.009, "rd")):
        y = -0.0315
        if shape == "sq":
            cutters.append(pxy("IRecStopCut", rr(0.0072, 0.0072, 0.0015, 4), pz0 - 0.001, pz1 + 0.0002, M["INS_Metal"], c=(x, y)))
            btns.append(pxy("IRecStop", rr(0.0064, 0.0064, 0.0012, 4), pz0 - 0.0005, zf + 0.0006, M["INS_Rubber"], c=(x, y), bev=0.0003, segs_=2, angle=40))
        else:
            cutters.append(hq.cyl_between("IRecRecCut", (x, y, pz0 - 0.001), (x, y, pz1 + 0.0002), 0.0039, M["INS_Metal"], 32))
            btns.append(revolve("IRecRec", (x, y, zf + 0.0006), (0, 0, -1),
                                [(0.0, 0.0), (0.0034, 0.0), (0.0034, 0.0012), (0.0028, 0.0016), (0.0, 0.0018)], M["INS_Rubber"], 32))
    cut(pl, [one(cutters, "IRecCutters")])
    out.append(finish(pl, 1.0, angle=40))
    for b in btns:
        out.append(finish(b, 1.0, angle=40))
    lcd = hq.span("IRecLcd", lx0 + 0.0001, lx1 - 0.0001, ly0 + 0.0001, ly1 - 0.0001, pz0 + 0.0003, zf + 0.0005, M["INS_Screen"], bev=0)
    out.append(finish(lcd, 1.0, angle=40))
    # 上の端：マイク（金属の網の筒）とイヤホンの穴
    for sx in (-1, 1):
        mc = hq.lathe(f"IRecMic{sx}", (sx * 0.0092, H / 2 - 0.0006, 0.0),
                      [(0.0022, 0.0), (0.0022, 0.0014), (0.0019, 0.002), (0.0, 0.0021)], M["INS_Metal"], 24)
        out.append(finish(mc, 1.0, angle=40))
    jack = hq.lathe("IRecJack", (0.0, H / 2 - 0.0004, 0.0), [(0.0014, 0.0), (0.0021, 0.0), (0.0021, 0.0006), (0.0014, 0.0006)],
                    M["INS_Metal"], 24, cap_top=False, cap_bottom=False)
    bm = bmesh.new()
    bm.from_mesh(jack.data)
    bmesh.ops.bridge_loops(bm, edges=[e for e in bm.edges if e.is_boundary])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(jack.data)
    bm.free()
    out.append(finish(jack, 1.0, angle=40))
    # 側面：ホールドのつまみ（左）・音量（右）
    hold = plate("IRecHold", rr(0.0075, 0.0035, 0.0012, 4), (-W / 2, 0.018, 0.0), (0, 1, 0), (0, 0, 1), (-1, 0, 0), -0.0004, 0.0007,
                 M["INS_Rubber"], bev=0.0002)
    out.append(finish(hold, 1.0, angle=40))
    for y in (0.012, 0.026):
        vb = plate(f"IRecVol{y}", rr(0.0105, 0.0032, 0.0014, 4), (W / 2, y, 0.0), (0, 1, 0), (0, 0, 1), (1, 0, 0), -0.0004, 0.0006,
                   M["INS_Rubber"], bev=0.0002)
        out.append(finish(vb, 1.0, angle=40))
    return out, [rect((0.0, (ly0 + ly1) / 2, pz0 + 0.0003), (lx1 - lx0 - 0.001, ly1 - ly0 - 0.001))]


# ============================== 12. 中吊り広告 ==============================

def poster(M):
    """電車の中吊り広告（横 0.36 x 縦 0.26、厚紙 1.5mm）。表は印刷の紙、縁と裏は板紙。上に2つのハトメ。左下の角が少し奥へ曲がる"""
    out = []
    W, H, T = 0.36, 0.26, 0.0015
    hx, hy = W / 2, H / 2
    Lc = 0.011
    a = (-hx + Lc, -hy)
    n = (-1 / math.sqrt(2), -1 / math.sqrt(2))
    ang, R = math.radians(12), 0.01

    a2, n2, ang2, R2 = (hx - 0.026, hy), (1 / math.sqrt(2), 1 / math.sqrt(2)), math.radians(9), 0.015

    def fn(u, v):
        u1, v1, w1 = fold(u, v, a, n, ang, R, sign=1.0)
        u2, v2, w2 = fold(u1, v1, a2, n2, ang2, R2, sign=1.0)
        return u2, v2, w1 + w2
    us = segs((-hx, -hx + 0.03, 10), (-hx + 0.03, hx - 0.03, 8), (hx - 0.03, hx, 10))
    vs = segs((-hy, -hy + 0.03, 10), (-hy + 0.03, hy - 0.03, 8), (hy - 0.03, hy, 10))
    bd = surf("IPosBoard", us, vs, fn, M["INS_Paper"], T, cuts=fold_cuts(a, n, ang, R, 8) + fold_cuts(a2, n2, ang2, R2, 8),
              back=M["INS_Cardboard"], rim=M["INS_Cardboard"])
    hq.bevel(bd, 0.00035, 2, 50)
    hq.apply_mods(bd)
    gy = hy - 0.013
    holes = [hq.cyl_between(f"IPosHole{sx}", (sx * 0.125, gy, -0.004), (sx * 0.125, gy, 0.006), 0.0031, M["INS_Paper"], 32) for sx in (-1, 1)]
    cut(bd, [one(holes, "IPosHoles")])
    out.append(finish(bd, 1.0, angle=50))
    for sx in (-1, 1):
        g = revolve(f"IPosGrommet{sx}", (sx * 0.125, gy, 0.0), (0, 0, 1),
                    [(0.00282, -0.0003), (0.0033, -0.00046), (0.0048, -0.00046), (0.0055, -0.0003), (0.0057, 0.0),
                     (0.00312, 0.0), (0.00312, T), (0.0051, T), (0.0053, T + 0.0002), (0.0051, T + 0.0004), (0.00282, T + 0.0004)],
                    M["INS_Metal"], 40, closed=True)
        out.append(finish(g, 1.0, angle=40))
    x0, x1, y0, y1 = -hx + 0.006, hx - 0.006, -hy + 0.006, gy - 0.0115
    return out, [rect(((x0 + x1) / 2, (y0 + y1) / 2, 0.0), (x1 - x0, y1 - y0))]


# ============================== 13. クリップボード ==============================

def clipboard(M):
    """クリップボード（硬質板 0.23 x 0.32）。上にばね金具（台・かしめ・耳・軸・リブ入りの顎・つり穴）、紙1枚を挟む"""
    out = []
    BW, BH, BT = 0.23, 0.32, 0.003
    bd = pxy("IClbBoard", rr(BW, BH, 0.008, 8), 0.0, BT, M["INS_Board"], bev=0.0008, segs_=3, angle=40)
    out.append(finish(bd, 1.0, angle=40))
    # 紙（下の角が少し浮く）
    T = 0.0003
    sx0, sx1, sy0, sy1 = -0.1, 0.1, -0.147, 0.123
    fx0, fx1, fy0, fy1 = -0.088, 0.088, -0.135, 0.092
    zs = -0.00005 - T

    def fs(u, v):
        tx = outside(u, fx0, fx1, sx0, sx1)
        ty = outside(v, fy0, fy1, sy0, sy1) if v < fy0 else 0.0
        lift = 0.0016 * ty * ty * (0.6 + 0.4 * abs(u) / 0.1) + 0.0004 * tx * tx * (v < 0.0)
        return (u, v, zs - lift)
    sh = surf("IClbSheet", segs((sx0, fx0, 5), (fx0, fx1, 6), (fx1, sx1, 5)), segs((sy0, fy0, 6), (fy0, sy1, 8)), fs, M["INS_Paper"], T)
    out.append(finish(sh, 1.0, angle=50))
    # 金具
    by = 0.139
    base = pxy("IClbBase", rr(0.1, 0.03, 0.004, 6), -0.0008, 0.00002, M["INS_Metal"], c=(0.0, by), bev=0.0003, segs_=2)
    out.append(finish(base, 1.0, angle=40))
    for sx in (-1, 1):
        rv = revolve(f"IClbRivet{sx}", (sx * 0.038, by + 0.006, -0.0008), (0, 0, -1),
                     [(0.0, 0.0), (0.0026, 0.0), (0.0024, 0.0004), (0.0016, 0.0008), (0.0, 0.0009)], M["INS_Metal"], 24)
        out.append(finish(rv, 1.0, angle=40))
        ear = plate(f"IClbEar{sx}", rr(0.009, 0.0075, 0.0022, 5), (sx * 0.0474, 0.1275, -0.0042), (0, 1, 0), (0, 0, 1), (1, 0, 0),
                    -0.0004, 0.0004, M["INS_Metal"], bev=0.00015)
        out.append(finish(ear, 1.0, angle=40))
    pin = hq.cyl_between("IClbPin", (-0.0482, 0.1275, -0.0048), (0.0482, 0.1275, -0.0048), 0.0011, M["INS_Metal"], 16)
    out.append(finish(pin, 1.0, angle=40))
    jp = [(0.153, -0.0118), (0.143, -0.0088), (0.1345, -0.0072), (0.1275, -0.0064), (0.119, -0.0052), (0.108, -0.0027),
          (0.1005, -0.0011), (0.0968, -0.00048), (0.0945, -0.0009), (0.0932, -0.0017)]
    jpth = hq.fillet_path([(0.0, y, z) for y, z in jp], 0.004, k=4)
    L = [0.0]
    for p0, p1 in zip(jpth, jpth[1:]):
        L.append(L[-1] + (p1 - p0).length)

    sL = L[-1]

    def jaw(u, s):
        i = min(max(bisect.bisect_left(L, s - 1e-9), 0), len(L) - 1)
        p = jpth[i]
        # プレスした板の丸い角（上端・先の両方）
        rc, half = 0.007, 0.045
        for d in (s, sL - s):
            if d < rc:
                half = min(half, 0.045 - rc + math.sqrt(max(0.0, rc * rc - (rc - d) ** 2)))
        x = u / 0.045 * half
        rib = sum(math.exp(-((x - c) / 0.003) ** 2) for c in (-0.02, 0.02))
        arch = 0.0009 * (1 - (x / 0.045) ** 2) * (1 - sstep((s - (sL - 0.014)) / 0.012))
        z = p.z - 0.00055 * rib - arch
        return (x, p.y, z)
    jw = surf("IClbJaw", segs((-0.045, 0.045, 30)), L, jaw, M["INS_Metal"], 0.0006, even=False)
    hq.apply_mods(jw)
    hole = pxy("IClbHangCut", rr(0.022, 0.007, 0.0035, 6), -0.02, -0.004, M["INS_Metal"], c=(0.0, 0.1445))
    cut(jw, [hole])
    out.append(finish(jw, 1.0, angle=50))
    return out, [rect(((fx0 + fx1) / 2, (fy0 + fy1) / 2, zs), (fx1 - fx0, fy1 - fy0))]


# ============================== 14. 子どもの絵 ==============================

def drawing(M):
    """画用紙（横 0.3 x 縦 0.21）。全体がゆるく波打ち（印字面の中は奥へ 0.3mm まで）、端は大きく反る。
    上の2つの角にマスキングテープ（壁からはがした端が奥へ丸まる、ちぎれた端はぎざぎざ）"""
    out = []
    W, H, T = 0.3, 0.21, 0.0005
    hx, hy = W / 2, H / 2
    fx, fy0, fy1 = 0.138, -0.093, 0.085       # 印字面（上はテープを避けて広めに空ける）

    def z_of(u, v):
        tx = outside(u, -fx, fx, -hx, hx)
        ty = outside(v, fy0, fy1, -hy, hy)
        w = 0.5 + 0.5 * math.sin(u * 21 + 0.6) * math.cos(v * 17 - 0.4)
        z = 0.0003 * w
        z += (0.0016 if v > 0 else -0.0011) * ty * ty
        z += 0.0012 * tx * tx * math.sin(v * 24 + (0.7 if u > 0 else 2.4))
        return z

    us = segs((-hx, -fx, 6), (-fx, fx, 20), (fx, hx, 6))
    vs = segs((-hy, fy0, 6), (fy0, fy1, 14), (fy1, hy, 7))
    pp = surf("IDrwPaper", us, vs, lambda u, v: (u, v, z_of(u, v)), M["INS_Paper"], T)
    out.append(finish(pp, 1.0, angle=50))
    rnd = random.Random(21)
    tl, tw, tt = 0.034, 0.016, 0.00012
    for sx in (-1, 1):
        corner = (sx * hx, hy)
        dia = Vector((-sx, -1)).normalized()             # 角から内向き
        perp = Vector((-dia.y, dia.x))
        ctr = Vector(corner) + dia * 0.004
        jag = [rnd.uniform(-0.0008, 0.0008) for _ in range(9)]

        def ft(a, b, ctr=ctr, dia=dia, perp=perp, jag=jag):
            # ちぎれた端（両端）のぎざぎざ
            j = jag[int(round((b / tw + 0.5) * 8))]
            if a <= -tl / 2 + 1e-9:
                a += j
            elif a >= tl / 2 - 1e-9:
                a -= j * 0.8
            p = ctr + dia * a + perp * b
            x, y = p.x, p.y
            cx, cy = max(-hx, min(hx, x)), max(-hy, min(hy, y))
            d_out = math.hypot(x - cx, y - cy)
            z = z_of(cx, cy) - 0.00006 - tt
            if d_out > 0:
                z += 0.06 * d_out + 7.0 * d_out * d_out      # 壁から離れた端が奥へ丸まる
            return (x, y, z)
        tp = surf(f"IDrwTape{sx}", segs((-tl / 2, tl / 2, 18)), segs((-tw / 2, tw / 2, 8)), ft, M["INS_Label"], tt)
        out.append(finish(tp, 1.0, angle=50))
    return out, [rect((0, (fy0 + fy1) / 2, 0), (2 * fx, fy1 - fy0))]


ITEMS = {"Sheet": sheet, "Report": report, "Folder": folder, "Letter": letter, "Notebook": notebook, "Card": card,
         "Photo": photo, "Newspaper": newspaper, "Monitor": monitor, "Cassette": cassette, "Recorder": recorder,
         "Poster": poster, "Clipboard": clipboard, "Drawing": drawing}
