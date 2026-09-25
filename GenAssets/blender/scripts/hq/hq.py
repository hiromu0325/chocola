"""
品質重視のモデリング補助（ローポリの箱積みではなく、面取り・スムーズ陰影・回転体・布シミュレーションを使う）。

座標はUnityの値で指定する（ビルダーの数値をそのまま使えるように）。
  Unity (x, y, z)  →  Blender (-x, -z, y)
各部品は「ユニットの原点」を原点にして作り、FBXに書き出すとUnity側の同じ位置に来る。
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

TEX_DIR = None          # プレビュー用のテクスチャ置き場（build_*.py が設定）


def U(x, y, z):
    return Vector((-x, -z, y))


def S(sx, sy, sz):
    """Unityの寸法 → Blenderの寸法"""
    return Vector((sx, sz, sy))


# ============================== 材質 ==============================

def mat(name, color=(0.6, 0.6, 0.6), rough=0.6, metal=0.0, tex=None, emit=None, emit_strength=0.0,
        alpha=None, tex_alpha=False):
    """プレビュー用の材質。Unity側では同名の材質に差し替える（色・テクスチャはUnityの表が正）"""
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit is not None:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emit_strength
    if alpha is not None:
        bsdf.inputs["Alpha"].default_value = alpha
    if tex and TEX_DIR:
        path = os.path.join(TEX_DIR, tex)
        if os.path.exists(path):
            img = bpy.data.images.load(path, check_existing=True)
            tn = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
            tn.image = img
            mix = next((n for n in nt.nodes if n.type == "MIX" and n.label == "tint"), None)
            if mix is None:
                mix = nt.nodes.new("ShaderNodeMix"); mix.label = "tint"
                mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"
            mix.inputs["Factor"].default_value = 1.0
            mix.inputs[7].default_value = (*color, 1.0)
            nt.links.new(tn.outputs["Color"], mix.inputs[6])
            nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
            if tex_alpha:
                nt.links.new(tn.outputs["Alpha"], bsdf.inputs["Alpha"])
                try:
                    m.surface_render_method = "BLENDED"
                except Exception:
                    m.blend_method = "BLEND"
    return m


# ============================== 形状 ==============================

def _obj(name, me, material):
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    if material is not None:
        o.data.materials.append(material if isinstance(material, bpy.types.Material) else bpy.data.materials[material])
    return o


def bevel(o, width=0.004, segs=3, angle=40, harden=True, profile=0.5):
    m = o.modifiers.new("Bevel", "BEVEL")
    m.width = width; m.segments = segs; m.limit_method = "ANGLE"
    m.angle_limit = math.radians(angle); m.harden_normals = harden; m.profile = profile
    return o


def box(name, c, s, material, bev=0.004, segs=3):
    """Unity座標の中心 c・寸法 s の箱（面取り付き）"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=S(*s), verts=bm.verts)
    bmesh.ops.translate(bm, vec=U(*c), verts=bm.verts)
    bm.to_mesh(me); bm.free()
    o = _obj(name, me, material)
    if bev > 0:
        bevel(o, min(bev, min(s) * 0.45), segs)
    return o


def span(name, x0, x1, y0, y1, z0, z1, material, bev=0.004, segs=3):
    return box(name, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2),
               (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)), material, bev, segs)


def lathe(name, base, profile, material, seg=32, cap_top=True, cap_bottom=True):
    """Unity Y軸回りの回転体。profile=[(半径, 高さ), ...] 下から上へ。base=底の中心(Unity)"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for (r, h) in profile:
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            ring.append(bm.verts.new(U(base[0] + r * math.cos(a), base[1] + h, base[2] + r * math.sin(a))))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    if cap_bottom and profile[0][0] > 1e-5:
        bm.faces.new(list(reversed(rings[0])))
    if cap_top and profile[-1][0] > 1e-5:
        bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return _obj(name, me, material)


def cyl(name, base, height, r, material, seg=32, r_top=None, bev=0.0):
    rt = r if r_top is None else r_top
    o = lathe(name, base, [(r, 0), (rt, height)], material, seg)
    if bev > 0:
        bevel(o, bev, 2, 30)
    return o


def cyl_between(name, p0, p1, r, material, seg=24, r1=None):
    """Unity座標の2点を結ぶ円柱（任意の向き）"""
    a, b = U(*p0), U(*p1)
    d = b - a
    o = lathe(name, (0, 0, 0), [(r, 0), (r if r1 is None else r1, d.length)], material, seg)
    # lathe は Blender +Z 方向に伸びる → d 方向へ回す
    q = Vector((0, 0, 1)).rotation_difference(d.normalized())
    o.data.transform(Matrix.Translation(a) @ q.to_matrix().to_4x4())
    return o


def grid_surface(name, nu, nv, fn, material):
    """(u,v)∈[0,1]^2 → Unity座標 のパラメトリック面（両面ではない）"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    vs = [[bm.verts.new(U(*fn(i / nu, j / nv))) for j in range(nv + 1)] for i in range(nu + 1)]
    for i in range(nu):
        for j in range(nv):
            bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
    bm.to_mesh(me); bm.free()
    return _obj(name, me, material)


def pillow(name, c, s, material, puff=1.0, res=24, seed=0):
    """枕・クッション：中央がふくらみ縁が薄い。s=(幅x, 厚みy, 奥行z)"""
    import random
    rnd = random.Random(seed)
    hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    top, bot = [], []
    n = res
    noise = [[rnd.uniform(-1, 1) for _ in range(n + 1)] for _ in range(n + 1)]
    for i in range(n + 1):
        rt, rb = [], []
        for j in range(n + 1):
            u, v = i / n * 2 - 1, j / n * 2 - 1
            # 角が少し内に寄る（詰め物で布が引っ張られる）
            pull = 1 - 0.06 * (1 - abs(u)) * (abs(v) ** 6) - 0.06 * (1 - abs(v)) * (abs(u) ** 6)
            x, z = u * hx * pull, v * hz * pull
            t = max(0.0, (1 - abs(u) ** 2.6) * (1 - abs(v) ** 2.6)) ** 0.55 * puff
            wr = 0.004 * noise[i][j] * t
            rt.append(bm.verts.new(U(c[0] + x, c[1] + hy * t + wr, c[2] + z)))
            rb.append(bm.verts.new(U(c[0] + x, c[1] - hy * t * 0.85, c[2] + z)))
        top.append(rt); bot.append(rb)
    for i in range(n):
        for j in range(n):
            bm.faces.new((top[i][j], top[i][j + 1], top[i + 1][j + 1], top[i + 1][j]))
            bm.faces.new((bot[i][j], bot[i + 1][j], bot[i + 1][j + 1], bot[i][j + 1]))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    o = _obj(name, me, material)
    sub = o.modifiers.new("Sub", "SUBSURF"); sub.levels = 1; sub.render_levels = 1
    return o


# ============================== 仕上げ ==============================

def apply_mods(o):
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    o.modifiers.clear()
    old = o.data
    o.data = me
    if old.users == 0:
        bpy.data.meshes.remove(old)


def box_uv(o, scale=1.0, rot90=False, name="UVMap"):
    """実寸の箱投影UV（1m = scale）。rot90=Trueで木目の向きを90度回す"""
    me = o.data
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.get(name) or bm.loops.layers.uv.new(name)
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda k: abs(n[k]))
        for l in f.loops:
            p = l.vert.co
            if ax == 2:   u, v = p.x, p.y          # 上下面（Blender Z）
            elif ax == 0: u, v = p.y, p.z          # Blender X 面
            else:         u, v = p.x, p.z          # Blender Y 面
            if rot90: u, v = v, u
            l[uv].uv = (u * scale, v * scale)
    bm.to_mesh(me); bm.free()


def smooth(o, angle=35):
    me = o.data
    for p in me.polygons:
        p.use_smooth = True
    try:
        me.set_sharp_from_angle(angle=math.radians(angle))
    except Exception:
        pass


def finish(o, uv_scale=1.0, rot90=False, angle=35, keep_uv=False):
    apply_mods(o)
    if not keep_uv:
        box_uv(o, uv_scale, rot90)
    smooth(o, angle)
    return o


def join(objs, name):
    objs = [o for o in objs if o is not None]
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name; o.data.name = name
    return o


def remove(prefixes):
    for o in list(bpy.data.objects):
        if any(o.name.startswith(p) for p in prefixes):
            bpy.data.objects.remove(o, do_unlink=True)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)


def export(objs, path):
    """原点そのままでFBXに（Unity側では子として並ぶ）"""
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             bake_space_transform=True, axis_forward="-Z", axis_up="Y",
                             use_mesh_modifiers=True, add_leaf_bones=False, path_mode="AUTO",
                             mesh_smooth_type="OFF", use_tspace=True)
    return os.path.getsize(path)
