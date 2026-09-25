"""確認レンダー（EEVEE）。カメラ・光源はUnity座標で指定"""
import bpy
from mathutils import Vector

from hq import U


def lights(specs):
    """specs=[(名前, Unity位置, 色, 強さW, 半径)]"""
    for o in list(bpy.data.objects):
        if o.name.startswith("PL_"):
            bpy.data.objects.remove(o, do_unlink=True)
    for name, pos, col, energy, radius in specs:
        ld = bpy.data.lights.new("PL_" + name, "POINT")
        ld.color = col; ld.energy = energy; ld.shadow_soft_size = radius
        lo = bpy.data.objects.new("PL_" + name, ld)
        bpy.context.scene.collection.objects.link(lo)
        lo.location = U(*pos)


def shot(path, cam_u, look_u, lens=18, res=(1280, 720), world=(0.02, 0.02, 0.022), exposure=0.0):
    sc = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    sc.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else "BLENDER_EEVEE_NEXT"
    cam = bpy.data.objects.get("PCam")
    if cam is None:
        cd = bpy.data.cameras.new("PCam")
        cam = bpy.data.objects.new("PCam", cd)
        sc.collection.objects.link(cam)
    cam.data.lens = lens
    cam.data.clip_start = 0.02
    cam.location = U(*cam_u)
    cam.rotation_euler = (U(*look_u) - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    if sc.world is None:
        sc.world = bpy.data.worlds.new("W")
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (*world, 1)
    bg.inputs["Strength"].default_value = 1.0
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.view_settings.view_transform = "AgX"
    sc.view_settings.exposure = exposure
    try:
        sc.eevee.use_shadows = True
        sc.eevee.use_raytracing = True
    except Exception:
        pass
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path
