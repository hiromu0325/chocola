"""
調べる画面の実物 14 種（hq/inspect_items.py）を作って Unity に書き出す。

    blender --background --factory-startup --python GenAssets/blender/scripts/hq/build_inspect.py -- [Sheet,Report,...] [--preview[=出力先]]

出力:
  project/Assets/Models/HQ/Inspect/Inspect_<名前>.fbx
      1メッシュ（材質ごとのサブメッシュ）。外接箱の中心が原点、表は -Z、上は +Y
  GenAssets/blender/scripts/hq/inspect_print_areas.json と project/Assets/Resources/InspectPrintAreas.json
      印字面 {"items": [{"name", "rects": [{"center","size","normal","up"}], "bounds": [x,y,z]}]}（InspectPrint.cs が読む）
      名前を絞って作ったときは、その名前の分だけ差し替える
  --preview[=出力先]: 確認レンダー（EEVEE、斜め前から。印字面には文字の行の見本を重ねる）<出力先>/<名前>.png
      出力先を省くと一時フォルダの inspect_previews。--check を足すと正面・横・裏下からも（<出力先>/check/）
"""
import importlib
import json
import math
import os
import sys
import tempfile

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
OUT = os.path.join(REPO, "project", "Assets", "Models", "HQ", "Inspect")
JSON_OUT = [os.path.join(HERE, "inspect_print_areas.json"),
            os.path.join(REPO, "project", "Assets", "Resources", "InspectPrintAreas.json")]

sys.dont_write_bytecode = True
if HERE not in sys.path:
    sys.path.insert(0, HERE)
for name in ("hq", "inspect_items"):
    if name in sys.modules:
        importlib.reload(sys.modules[name])
import hq             # noqa: E402
import inspect_items  # noqa: E402


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names, preview, check = None, None, False
    for a in argv:
        if a == "--check":
            check = True
        elif a.startswith("--preview"):
            preview = a.split("=", 1)[1] if "=" in a else os.path.join(tempfile.gettempdir(), "inspect_previews")
        elif a.strip():
            names = [s.strip() for s in a.split(",") if s.strip()]
    return names, preview, check


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.curves, bpy.data.lights, bpy.data.cameras):
        for d in list(coll):
            if d.users == 0:
                coll.remove(d)


def unity_bounds(o):
    xs, ys, zs = [], [], []
    for v in o.data.vertices:
        xs.append(-v.co.x); ys.append(v.co.z); zs.append(-v.co.y)
    return Vector((min(xs), min(ys), min(zs))), Vector((max(xs), max(ys), max(zs)))


def recenter(o, rects):
    """外接箱の中心を原点へ。印字面も同じだけずらす。戻り値は外接箱の寸法"""
    lo, hi = unity_bounds(o)
    c = (lo + hi) / 2
    o.data.transform(Matrix.Translation(-hq.U(*c)))
    o.data.update()
    for r in rects:
        r["center"] = [r["center"][k] - c[k] for k in range(3)]
    return hi - lo


def triangulate_ngons(o):
    """5角以上の面（へこんだ多角形を含む）は Blender 側で三角形に（Unity の分割に任せない）"""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    ng = [f for f in bm.faces if len(f.verts) > 4]
    if ng:
        bmesh.ops.triangulate(bm, faces=ng, quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(o.data)
    bm.free()


def rnd(v, k=5):
    return [round(x, k) for x in v]


# ============================== 確認レンダー ==============================

def overlay_mat(name, size, screen):
    """印字面の見本：文字の行のような横線（行の長さはまちまち）。画面は緑に光る"""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nd, ln = nt.nodes, nt.links
    bsdf = next(n for n in nd if n.type == "BSDF_PRINCIPLED")
    tc = nd.new("ShaderNodeTexCoord")
    sep = nd.new("ShaderNodeSeparateXYZ")
    ln.new(tc.outputs["UV"], sep.inputs[0])
    rows = max(3.0, size[1] / 0.0055)

    def math_node(op, a, b=None):
        n = nd.new("ShaderNodeMath")
        n.operation = op
        for i, x in enumerate((a, b)):
            if x is None:
                continue
            if isinstance(x, (int, float)):
                n.inputs[i].default_value = x
            else:
                ln.new(x, n.inputs[i])
        return n.outputs[0]
    vy = math_node("MULTIPLY", sep.outputs[1], rows)
    frac = math_node("FRACT", vy)
    rowm = math_node("LESS_THAN", frac, 0.42)
    idx = math_node("FLOOR", vy)
    wn = nd.new("ShaderNodeTexWhiteNoise")
    wn.noise_dimensions = "1D"
    ln.new(idx, wn.inputs["W"])
    lenv = math_node("MULTIPLY_ADD", wn.outputs["Value"], 0.6)
    nt.nodes[-1].inputs[2].default_value = 0.35
    lenm = math_node("LESS_THAN", sep.outputs[0], lenv)
    mask = math_node("MULTIPLY", rowm, lenm)
    alpha = math_node("MULTIPLY", mask, 0.85)
    ln.new(alpha, bsdf.inputs["Alpha"])
    if screen:
        bsdf.inputs["Base Color"].default_value = (0.3, 0.9, 0.55, 1)
        bsdf.inputs["Emission Color"].default_value = (0.35, 1.0, 0.6, 1)
        bsdf.inputs["Emission Strength"].default_value = 3.0
    else:
        bsdf.inputs["Base Color"].default_value = (0.02, 0.02, 0.025, 1)
        bsdf.inputs["Roughness"].default_value = 0.7
    return m


def add_overlays(rects, screen):
    for i, r in enumerate(rects):
        c, n, up = Vector(r["center"]), Vector(r["normal"]), Vector(r["up"])
        right = Vector((n.y * up.z - n.z * up.y, n.z * up.x - n.x * up.z, n.x * up.y - n.y * up.x))   # normal x up（Unity の値で）
        w, h = r["size"]
        pts = [c - right * w / 2 - up * h / 2, c + right * w / 2 - up * h / 2, c + right * w / 2 + up * h / 2, c - right * w / 2 + up * h / 2]
        q = hq.quad(f"PV_Print{i}", [tuple(p) for p in pts], overlay_mat(f"PV_PrintMat{i}", (w, h), screen))
        hq.face_toward(q, tuple(n))


VIEWS = {"": (-0.7, 0.42, -1.0)}                                   # 斜め前・少し上（Unity）
CHECK_VIEWS = {"_front": (0.0, 0.0, -1.0), "_side": (-1.0, 0.12, -0.18), "_back": (0.75, -0.4, 1.0)}


def render(name, size, rects, outdir, check=False):
    """確認レンダー。check=True で正面・真横ぎみ・裏下からの3枚も（<出力先>/check/）"""
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.eevee.taa_render_samples = 64
    try:
        sc.eevee.use_raytracing = True
    except Exception:
        pass
    add_overlays(rects, name in ("Monitor", "Recorder"))
    r = 0.5 * size.length
    cd = bpy.data.cameras.new("PV_Cam")
    cd.lens = 50
    cd.clip_start = 0.005
    cam = bpy.data.objects.new("PV_Cam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    for ln_name, frm, energy, ang in (("Key", (-0.75, 0.75, -0.45), 3.4, 6), ("Fill", (0.9, 0.2, -0.5), 0.8, 20),
                                      ("Rim", (0.35, 0.6, 1.0), 2.2, 6)):
        ld = bpy.data.lights.new("PV_" + ln_name, "SUN")
        ld.energy = energy
        ld.angle = math.radians(ang)
        lo = bpy.data.objects.new("PV_" + ln_name, ld)
        sc.collection.objects.link(lo)
        travel = -hq.U(*Vector(frm).normalized())
        lo.rotation_euler = travel.to_track_quat("-Z", "Y").to_euler()
    if sc.world is None:
        sc.world = bpy.data.worlds.new("PV_World")
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.09, 0.095, 0.1, 1)
    bg.inputs["Strength"].default_value = 1.0
    sc.render.resolution_x = sc.render.resolution_y = 900
    sc.render.resolution_percentage = 100
    sc.view_settings.view_transform = "AgX"
    sc.render.film_transparent = False
    views = dict(VIEWS)
    if check:
        views.update(CHECK_VIEWS)
    for suffix, dv in views.items():
        d = Vector(dv).normalized()
        cam.location = hq.U(*(d * r * 2.75))
        cam.rotation_euler = (-cam.location).to_track_quat("-Z", "Y").to_euler()
        folder = outdir if suffix == "" else os.path.join(outdir, "check")
        os.makedirs(folder, exist_ok=True)
        sc.render.filepath = os.path.join(folder, f"{name}{suffix}.png")
        bpy.ops.render.render(write_still=True)


# ============================== 本体 ==============================

def main():
    names, preview, check = parse_args()
    M = inspect_items.mats()
    for m in M.values():
        if m.node_tree and m.node_tree.nodes.get("Principled BSDF") and \
                m.node_tree.nodes["Principled BSDF"].inputs["Alpha"].default_value < 1.0:
            m.use_backface_culling = True
    data = {}
    if os.path.exists(JSON_OUT[0]):
        try:
            with open(JSON_OUT[0], encoding="utf-8") as fp:
                data = {it["name"]: it for it in json.load(fp).get("items", [])}
        except Exception:
            data = {}
    total = 0
    for name, fn in inspect_items.ITEMS.items():
        if names and name not in names:
            continue
        clear_scene()
        objs, rects = fn(M)
        for o in objs:
            o.location = (0, 0, 0)
            o.rotation_euler = (0, 0, 0)
        obj = hq.join(objs, f"Inspect_{name}")
        triangulate_ngons(obj)
        size = recenter(obj, rects)
        kb = hq.export([obj], os.path.join(OUT, f"Inspect_{name}.fbx")) // 1024
        total += kb
        data[name] = {"name": name, "rects": [{"center": rnd(r["center"]), "size": rnd(r["size"]), "normal": rnd(r["normal"], 4),
                                               "up": rnd(r["up"], 4)} for r in rects], "bounds": rnd(size, 4)}
        nf = len(obj.data.polygons)
        print(f"[inspect] {name}: {kb} KB, {nf} faces, bounds {tuple(round(v, 4) for v in size)}, "
              f"mats {[m.name for m in obj.data.materials]}, rects {len(rects)}")
        if preview:
            render(name, size, data[name]["rects"], preview, check)
    items = [data[n] for n in inspect_items.ITEMS if n in data]
    text = json.dumps({"items": items}, ensure_ascii=False, indent=1)
    for p in JSON_OUT:
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as fp:
            fp.write(text + "\n")
    print(f"[inspect] exported, {total} KB -> {OUT}")


if __name__ == "__main__":
    main()
