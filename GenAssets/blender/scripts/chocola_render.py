import bpy, math
from mathutils import Vector
from chocola_kit import U

def interior(path, show, cam_u, look_u, lights_u, lens=14, energy=120, color=(1.0,0.95,0.88), res=(1280,720)):
    """室内から撮る。show=表示するオブジェクト名、cam/look/lightsはUnity座標"""
    sc = bpy.context.scene
    for o in bpy.data.objects:
        if o.type == "MESH": o.hide_render = o.name not in show
    for o in list(bpy.data.objects):
        if o.name.startswith("RLight"): bpy.data.objects.remove(o, do_unlink=True)
    for i, p in enumerate(lights_u):
        ld = bpy.data.lights.new(f"RLight{i}", "POINT"); ld.energy = energy; ld.color = color
        ld.shadow_soft_size = 0.3
        lo = bpy.data.objects.new(f"RLight{i}", ld); sc.collection.objects.link(lo); lo.location = U(*p)
    cam = bpy.data.objects.get("RCam")
    if cam is None:
        cam = bpy.data.objects.new("RCam", bpy.data.cameras.new("RCam")); sc.collection.objects.link(cam)
    cam.data.lens = lens; cam.data.clip_start = 0.05; cam.data.clip_end = 200
    cam.location = U(*cam_u)
    cam.rotation_euler = (U(*look_u) - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    w = sc.world or bpy.data.worlds.new("World"); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    if bg: bg.inputs["Color"].default_value = (0.02,0.02,0.025,1); bg.inputs["Strength"].default_value = 1.0
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = path
    try: sc.eevee.use_shadows = True
    except Exception: pass
    bpy.ops.render.render(write_still=True)
    return path
