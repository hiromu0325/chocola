"""
隠しミニゲーム「わさび当て」の寿司下駄と握り6貫を作って Unity に書き出す（本筋とは関係ない）。

    blender --background --factory-startup --python GenAssets/blender/scripts/hq/build_sushi.py -- [--preview[=出力先]]

先にテクスチャ: python GenAssets/sushi/make_textures.py
出力: project/Assets/Models/HQ/Sushi/Sushi.fbx（部品ごとに別の物体。座標は Unity の値）
  Geta              寿司下駄。原点 = 底の中心。上面 y = GETA_TOP。長手は Unity の Z、手前（客の側）は +X
  Shari             シャリ1個。原点 = 底の中心（下駄の上面に置く）。長手は X
  Wasabi            わさび。原点 = 底の中心（シャリの上面に少し沈めて置く）
  Neta_<種類>        ネタ（Maguro / Salmon / Tai / Ika / Tamago / Anago）。原点 = めくる時の蝶番
                    （ネタの奥の端の下）。物体の位置 = 握りの原点（シャリの底の中心）から見た蝶番の位置。
                    +Z 軸まわりに正の角度で回すと手前の端が持ち上がる
--preview: 確認レンダー（EEVEE）を <出力先>/sushi.png に（既定は一時フォルダ）
"""
import importlib
import math
import os
import random
import sys
import tempfile

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
OUT = os.path.join(REPO, "project", "Assets", "Models", "HQ", "Sushi", "Sushi.fbx")

sys.dont_write_bytecode = True
if HERE not in sys.path:
    sys.path.insert(0, HERE)
if "hq" in sys.modules:
    importlib.reload(sys.modules["hq"])
import hq  # noqa: E402
from hq import U, span  # noqa: E402

hq.TEX_DIR = os.path.join(REPO, "GenAssets", "sushi", "tex")

# 寸法（m）
GETA_L, GETA_D, GETA_TOP, GETA_T = 0.25, 0.12, 0.036, 0.014   # 長さ(Z)・奥行(X)・上面の高さ・板の厚み
SHARI_L, SHARI_W, SHARI_H = 0.05, 0.024, 0.02
PITCH = 0.038                                                  # 握りの間隔（Z）
WASABI_BASE = SHARI_H - 0.0015                                 # わさびの底（シャリに少し沈める）

# ネタ：長さ・幅・厚み・材質・端の垂れ
NETA = {
    "Maguro": dict(L=0.068, W=0.030, T=0.0075, mat="SUS_Maguro", droop=0.0065),
    "Salmon": dict(L=0.068, W=0.030, T=0.0075, mat="SUS_Salmon", droop=0.0065),
    "Tai":    dict(L=0.066, W=0.029, T=0.0065, mat="SUS_Tai", droop=0.006),
    "Ika":    dict(L=0.066, W=0.029, T=0.0055, mat="SUS_Ika", droop=0.0055),
    "Tamago": dict(L=0.062, W=0.028, T=0.013, mat="SUS_Tamago", droop=0.003),
    "Anago":  dict(L=0.072, W=0.031, T=0.0065, mat="SUS_Anago", droop=0.007),
}


def mats():
    return {
        "geta": hq.mat("SUS_Geta", (1, 1, 1), 0.55, tex="geta.png"),
        "rice": hq.mat("SUS_Rice", (1, 1, 1), 0.6, tex="rice.png"),
        "wasabi": hq.mat("SUS_Wasabi", (1, 1, 1), 0.7, tex="wasabi.png"),
        "nori": hq.mat("SUS_Nori", (1, 1, 1), 0.6, tex="nori.png"),
        "SUS_Maguro": hq.mat("SUS_Maguro", (1, 1, 1), 0.3, tex="maguro.png"),
        "SUS_Salmon": hq.mat("SUS_Salmon", (1, 1, 1), 0.3, tex="salmon.png"),
        "SUS_Tai": hq.mat("SUS_Tai", (1, 1, 1), 0.3, tex="tai.png"),
        "SUS_Ika": hq.mat("SUS_Ika", (1, 1, 1), 0.3, tex="ika.png"),
        "SUS_Tamago": hq.mat("SUS_Tamago", (1, 1, 1), 0.5, tex="tamago.png"),
        "SUS_Anago": hq.mat("SUS_Anago", (1, 1, 1), 0.2, tex="anago.png"),
    }


def sgnpow(x, p):
    return math.copysign(abs(x) ** p, x)


def mesh_obj(name, verts, faces, uvs, material):
    """頂点（Unity 座標）・面・面ごとの UV から物体を作る"""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(U(*v)) for v in verts], [], faces)
    me.update()
    uv = me.uv_layers.new(name="UVMap")
    li = 0
    for fi, f in enumerate(me.polygons):
        for k in range(f.loop_total):
            uv.data[f.loop_start + k].uv = uvs[fi][k]
    o = hq._obj(name, me, material)
    return o


# ============================== 下駄 ==============================

def geta(M):
    p = []
    hl, hd = GETA_L / 2, GETA_D / 2
    p.append(span("SG_Board", -hd, hd, GETA_TOP - GETA_T, GETA_TOP, -hl, hl, M["geta"], 0.003, 3))
    for s in (-1, 1):
        z = s * (hl - 0.028)
        p.append(span(f"SG_Foot{s}", -hd + 0.004, hd - 0.004, 0.0, GETA_TOP - GETA_T + 0.001, z - 0.009, z + 0.009, M["geta"], 0.002, 2))
    objs = [hq.finish(o, 2.0, rot90=True, angle=40) for o in p]
    return hq.join(objs, "Geta")


# ============================== シャリ・わさび ==============================

def shari(M, seed=5):
    """少し角ばった俵形（超楕円体）。底は平ら、手で握ったわずかな凹凸"""
    rnd = random.Random(seed)
    a, c = SHARI_L / 2, SHARI_W / 2
    b = SHARI_H / 1.25
    nu, nv = 40, 20
    verts = []
    for j in range(nv + 1):
        th = -math.pi / 2 + math.pi * j / nv
        for i in range(nu):
            ph = 2 * math.pi * i / nu
            ct, st = sgnpow(math.cos(th), 0.55), sgnpow(math.sin(th), 0.55)
            x = a * ct * sgnpow(math.cos(ph), 0.5)
            z = c * ct * sgnpow(math.sin(ph), 0.5)
            y = b * st
            y = y * 0.25 if y < 0 else y
            j_ = 0.00035 if 0 < j < nv else 0.0
            verts.append((x + rnd.uniform(-j_, j_), y + b * 0.25 + rnd.uniform(-j_, j_) * 0.6, z + rnd.uniform(-j_, j_)))
    faces, uvs = [], []
    for j in range(nv):
        for i in range(nu):
            i1 = (i + 1) % nu
            f = (j * nu + i, j * nu + i1, (j + 1) * nu + i1, (j + 1) * nu + i)
            faces.append(f)
            uvs.append([(0, 0)] * 4)
    o = mesh_obj("Shari", verts, faces, uvs, M["rice"])
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.to_mesh(o.data); bm.free()
    hq.box_uv(o, 25.0)
    hq.smooth(o, 80)
    return o


def wasabi(M, seed=9):
    """すりおろしのひと塊（少し平たい、表面はでこぼこ）"""
    rnd = random.Random(seed)
    a, c, h = 0.0092, 0.0066, 0.003   # 1.8 x 1.3cm（めくった時に離れていても分かる大きさ）
    nu, nv = 24, 8
    verts = []
    for j in range(nv + 1):
        th = math.pi / 2 * j / nv
        for i in range(nu):
            ph = 2 * math.pi * i / nu
            r = 1 + rnd.uniform(-0.12, 0.12) * (j / nv)
            verts.append((a * math.cos(th) * math.cos(ph) * r, h * math.sin(th) * (1 + rnd.uniform(-0.15, 0.15)), c * math.cos(th) * math.sin(ph) * r))
    faces, uvs = [], []
    for j in range(nv):
        for i in range(nu):
            i1 = (i + 1) % nu
            faces.append((j * nu + i, j * nu + i1, (j + 1) * nu + i1, (j + 1) * nu + i))
            uvs.append([(0, 0)] * 4)
    faces.append(tuple(reversed(range(nu))))   # 底
    uvs.append([(0, 0)] * nu)
    o = mesh_obj("Wasabi", verts, faces, uvs, M["wasabi"])
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.to_mesh(o.data); bm.free()
    hq.box_uv(o, 50.0)
    hq.smooth(o, 80)
    return o


# ============================== ネタ ==============================

def shari_top(x):
    """シャリの上面の高さ（長手 x の位置で）。端に向かって丸く下がる"""
    t = min(1.0, abs(x) / (SHARI_L / 2))
    return SHARI_H * (1 - t ** 5 * 0.55)


def neta_under(x, L, droop):
    """ネタの裏面の高さ：シャリの上に乗り、はみ出た所は少し垂れる"""
    base = shari_top(min(abs(x), SHARI_L / 2 - 0.002)) + 0.0003
    over = max(0.0, abs(x) - SHARI_L / 2 + 0.006) / (L / 2 - SHARI_L / 2 + 0.006)
    return base - droop * over ** 1.6


def slab(name, L, W, T, droop, material, seed, tamago=False):
    """ネタ1枚：角の丸い断面（超楕円）を長手に並べ、シャリに沿わせて曲げる。両端はそぎ切りで薄く。
    UV：u = 長さ（0..1）、v = 断面の周（0.5 = 上面の中央）"""
    rnd = random.Random(seed)
    nl, nc = 28, 32
    p = 0.3 if tamago else 0.42        # 断面の角ばり（小さいほど四角）
    verts, rows = [], []
    wob = [rnd.uniform(-1, 1) for _ in range(4)]
    for i in range(nl + 1):
        u = i / nl
        x = -L / 2 + L * u
        e = abs(2 * u - 1)
        taper = 1 - (0.0 if tamago else 0.45) * e ** 3        # 端ほど薄い（そぎ切り）
        w = W * (1 - 0.1 * e ** 2) * (1 + 0.02 * math.sin(u * 6 + wob[0]))
        t = T * taper
        yc = neta_under(x, L, droop) + t / 2
        row = []
        for k in range(nc):
            a = -math.pi / 2 + 2 * math.pi * k / nc
            z = (w / 2) * sgnpow(math.cos(a), p)
            y = (t / 2) * sgnpow(math.sin(a), p)
            # 上面は幅方向にわずかに丸く、裏は平ら
            if y > 0:
                y += (t * 0.12) * (1 - (2 * z / w) ** 2)
            z += 0.0004 * math.sin(u * 9 + wob[1]) * (1 if y > 0 else 0)
            row.append(len(verts))
            verts.append((x, yc + y, z))
        rows.append(row)
    faces, uvs = [], []
    for i in range(nl):
        for k in range(nc):
            k1 = (k + 1) % nc
            faces.append((rows[i][k], rows[i][k1], rows[i + 1][k1], rows[i + 1][k]))
            v0, v1 = k / nc, (k + 1) / nc
            uvs.append([(i / nl, v0), (i / nl, v1), ((i + 1) / nl, v1), ((i + 1) / nl, v0)])
    # 両端のふた（切り口）
    for i, u in ((0, 0.0), (nl, 1.0)):
        c = Vector((0, 0, 0))
        for vi in rows[i]:
            c += Vector(verts[vi])
        c /= nc
        ci = len(verts)
        verts.append(tuple(c))
        for k in range(nc):
            k1 = (k + 1) % nc
            f = (rows[i][k1], rows[i][k], ci) if i == 0 else (rows[i][k], rows[i][k1], ci)
            faces.append(f)
            uvs.append([(u, 0.5)] * 3)
    o = mesh_obj(name, verts, faces, uvs, material)
    hq.smooth(o, 55)
    return o


def nori_band(neta_obj, L, W, T, material):
    """玉子の海苔の帯（中央 1cm。ネタの上と横を包む）"""
    verts, faces, uvs = [], [], []
    nl, nc = 4, 20
    rows = []
    for i in range(nl + 1):
        x = -0.0055 + 0.011 * i / nl
        t = T
        yc = neta_under(x, L, 0.003) + t / 2
        row = []
        for k in range(nc + 1):
            a = -0.15 + (math.pi + 0.3) * k / nc          # 右の横 → 上 → 左の横
            z = (W / 2 + 0.0006) * sgnpow(math.cos(a), 0.3)
            y = (t / 2 + 0.0006) * sgnpow(math.sin(a), 0.3)
            if y > 0:
                y += (t * 0.12) * (1 - (2 * z / (W + 0.0012)) ** 2)
            row.append(len(verts))
            verts.append((x, yc + y, z))
        rows.append(row)
    for i in range(nl):
        for k in range(nc):
            faces.append((rows[i][k], rows[i][k + 1], rows[i + 1][k + 1], rows[i + 1][k]))
            uvs.append([(i / nl, k / nc), (i / nl, (k + 1) / nc), ((i + 1) / nl, (k + 1) / nc), ((i + 1) / nl, k / nc)])
    o = mesh_obj("Nori", verts, faces, uvs, material)
    # 帯は薄い板なので裏にも面を（Solidify）
    m = o.modifiers.new("Solid", "SOLIDIFY")
    m.thickness = 0.0005
    m.offset = -1
    hq.apply_mods(o)
    hq.smooth(o, 60)
    return o


def neta(M, kind, seed):
    d = NETA[kind]
    o = slab(f"Neta_{kind}", d["L"], d["W"], d["T"], d["droop"], M[d["mat"]], seed, tamago=(kind == "Tamago"))
    if kind == "Tamago":
        band = nori_band(o, d["L"], d["W"], d["T"], M["nori"])
        o = hq.join([o, band], f"Neta_{kind}")
    # 原点を蝶番（奥の端の下）へ：メッシュを蝶番基準に直し、物体の位置を蝶番に置く
    hx = -d["L"] / 2 + 0.004
    pivot = (hx, neta_under(hx, d["L"], d["droop"]), 0.0)
    o.data.transform(Matrix.Translation(-U(*pivot)))
    o.data.update()
    o.location = U(*pivot)
    return o, pivot


# ============================== 確認レンダー ==============================

def preview(objs, outdir):
    """下駄に6貫を並べ、ひとつはめくってわさびを見せた確認レンダー"""
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.eevee.taa_render_samples = 64
    kinds = list(NETA.keys())
    shown = []
    base = objs["Geta"]
    shown.append(base)
    for i, kind in enumerate(kinds):
        z = (i - 2.5) * PITCH
        sh = objs["Shari"].copy(); sh.data = objs["Shari"].data
        sc.collection.objects.link(sh)
        sh.location = U(0, GETA_TOP, z)
        n = objs[f"Neta_{kind}"].copy(); n.data = objs[f"Neta_{kind}"].data
        sc.collection.objects.link(n)
        pv = objs[f"pivot_{kind}"]
        n.location = U(pv[0], pv[1] + GETA_TOP, z)
        if kind == "Salmon":
            n.rotation_mode = "XYZ"
            # Unity の +Z 軸まわり 125°（SushiNeta.OpenAngle）= Blender の +Y 軸まわり
            n.rotation_euler = (0, math.radians(125), 0)
            w = objs["Wasabi"].copy(); w.data = objs["Wasabi"].data
            sc.collection.objects.link(w)
            w.location = U(0, GETA_TOP + WASABI_BASE, z)
    for o in (objs["Shari"], objs["Wasabi"]) + tuple(objs[f"Neta_{k}"] for k in kinds):
        o.hide_render = True
    cd = bpy.data.cameras.new("PV_Cam"); cd.lens = 60; cd.clip_start = 0.005
    cam = bpy.data.objects.new("PV_Cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    eye = U(0.42, 0.33, 0.12)
    cam.location = eye
    cam.rotation_euler = (U(0, 0.03, 0) - eye).to_track_quat("-Z", "Y").to_euler()
    for nm, frm, en in (("Key", (0.6, 1.0, 0.4), 3.5), ("Fill", (0.3, 0.4, -1.0), 1.0)):
        ld = bpy.data.lights.new("PV_" + nm, "SUN"); ld.energy = en; ld.angle = math.radians(8)
        lo = bpy.data.objects.new("PV_" + nm, ld); sc.collection.objects.link(lo)
        lo.rotation_euler = (-U(*Vector(frm).normalized())).to_track_quat("-Z", "Y").to_euler()
    world = bpy.data.worlds.new("PV_World"); sc.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.05, 0.055, 1)
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    os.makedirs(outdir, exist_ok=True)
    sc.render.filepath = os.path.join(outdir, "sushi.png")
    bpy.ops.render.render(write_still=True)
    print("[sushi] preview:", sc.render.filepath)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    prev = None
    for a in argv:
        if a.startswith("--preview"):
            prev = a.split("=", 1)[1] if "=" in a else os.path.join(tempfile.gettempdir(), "sushi_preview")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    M = mats()
    objs = {"Geta": geta(M), "Shari": shari(M), "Wasabi": wasabi(M)}
    for i, kind in enumerate(NETA):
        o, pv = neta(M, kind, 100 + i)
        objs[f"Neta_{kind}"] = o
        objs[f"pivot_{kind}"] = pv
    for o in (objs["Geta"], objs["Shari"], objs["Wasabi"]):
        o.location = (0, 0, 0)
    export = [objs["Geta"], objs["Shari"], objs["Wasabi"]] + [objs[f"Neta_{k}"] for k in NETA]
    size = hq.export(export, OUT)
    print(f"[sushi] exported {OUT} ({size // 1024} KB)")
    for k in NETA:
        print(f"[sushi] pivot {k}: {tuple(round(v, 4) for v in objs['pivot_' + k])}")
    if prev:
        preview(objs, prev)


if __name__ == "__main__":
    main()
