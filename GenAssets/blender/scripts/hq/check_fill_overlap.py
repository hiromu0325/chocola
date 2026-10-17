"""
黒塗りの資料の入れ物・紙が、部屋のモデル（壁の造作・家具）や自分の部品にめり込んでいないかを調べる。

    blender --background --factory-startup --python GenAssets/blender/scripts/hq/check_fill_overlap.py

部屋の外殻・家具と、入れ物（閉じた形・開いた形）・紙をビルダーと同じ位置に並べ、三角形どうしの交差（BVH）を数える。
置いた面に載せているだけの紙・封筒は、触れている面を交差と数えないよう、確かめる時だけ 0.3mm 持ち上げる。
"""
import importlib
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True
if HERE not in sys.path:
    sys.path.insert(0, HERE)
for name in ("hq", "train_room", "dim_room", "study_room", "fill_props", "lab_room", "ward_room", "saeki_room", "kuroda_room"):
    if name in sys.modules:
        importlib.reload(sys.modules[name])
import hq  # noqa: E402
from hq import U  # noqa: E402
import fill_props as fp  # noqa: E402
import lab_room, ward_room, saeki_room, kuroda_room  # noqa: E402,E401

LIFT = 0.0003   # 載せているだけの物を持ち上げる量（接している面は交差に数えない）


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)


def place(objs, pos, yaw=0.0):
    """Unity の位置・yaw（度）で置く（物の原点はそのまま、メッシュを動かす）"""
    m = Matrix.Translation(U(*pos)) @ Matrix.Rotation(math.radians(-yaw), 4, "Z")
    for o in objs:
        o.matrix_world = m @ o.matrix_world
    return objs


def bake(o):
    """修飾子を当て、ワールド座標で BVH を作る（FromObject は物の座標のままなので使わない）"""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    me = ev.to_mesh()
    mw = o.matrix_world
    verts = [mw @ v.co for v in me.vertices]
    polys = [list(p.vertices) for p in me.polygons]
    ev.to_mesh_clear()
    return BVHTree.FromPolygons(verts, polys, epsilon=0.0)


def overlap(a, b):
    ta, tb = bake(a), bake(b)
    return len(ta.overlap(tb))


def lift(objs, d=LIFT):
    for o in objs:
        o.matrix_world = Matrix.Translation((0, 0, d)) @ o.matrix_world


def open_pose(part, kind, euler_or_offset):
    """Unity の FillContainer と同じ開き方。蝶番の角度の向きは、扉の重心が手前（Unity +Z）へ出る方を選ぶ"""
    if kind == "slide":
        x, y, z = euler_or_offset
        part.matrix_world = part.matrix_world @ Matrix.Translation(U(x, y, z) - U(0, 0, 0))
        return
    axis, deg = euler_or_offset
    base = part.matrix_world.copy()
    best = None
    for sgn in (1, -1):
        part.matrix_world = base @ Matrix.Rotation(math.radians(sgn * deg), 4, axis)
        bpy.context.view_layer.update()
        c = sum((part.matrix_world @ v.co for v in part.data.vertices), Vector()) / len(part.data.vertices)
        if best is None or c.y < best[0]:     # Blender -Y = Unity の手前（入れ物の原点から見て）
            best = (c.y, sgn)
    part.matrix_world = base @ Matrix.Rotation(math.radians(best[1] * deg), 4, axis)


def report(label, movers, statics, results):
    for m in movers:
        for s in statics:
            if m is s:
                continue
            n = overlap(m, s)
            if n:
                results.append(f"  NG {label}: {m.name} × {s.name}  交差 {n} 組")


def main():
    results = []

    # ---------------- 研究所応接室 ----------------
    clear()
    M = lab_room.mats()
    room = lab_room.shell(M) + lab_room.interior(M)
    FM = __import__("build_fill").mats()
    body, door, visit = fp.lab_cabinet(FM)
    cab = [body, door, visit]
    place(cab, (fp.LAB_CAB_X, 0, -5.0 + 0.075 + 0.014 + 0.21))
    paper = fp.paper_guide(FM)
    place(paper, (-3.15, 1.04, -5.0 + 0.42), 172)
    lift(paper); lift([visit]); lift([body, door, visit])
    bpy.context.view_layer.update()
    report("研究所・閉", [body, door, visit] + paper, room, results)
    report("研究所・閉（棚の部品）", [door, visit], [body], results)
    open_pose(door, "hinge", ("Z", 100))
    bpy.context.view_layer.update()
    report("研究所・開", [door], room + [body, visit], results)

    # ---------------- 臨床病棟 ----------------
    clear()
    M = ward_room.mats()
    room = ward_room.shell(M) + ward_room.beds(M)
    nurse = ward_room.nurse(M)
    place(nurse, (1.45, 0, -6.0 + 1.6))
    FM = __import__("build_fill").mats()
    body, door, inside = fp.ward_lockers(FM)
    lk = [body, door, inside]
    place(lk, (fp.WARD_LOCKER_X, 0, -6.0 + 0.075 + 0.006 + 0.25))
    paper = fp.paper_page303(FM)
    place(paper, (1.45 - 0.145, 0.75, -6.0 + 1.6 - 0.18), 2)
    obs = ward_room.obs(M)
    place(obs, (1.45 - 0.45, 0.76, -6.0 + 1.6))
    nurse = nurse + obs
    lift(paper); lift([inside]); lift([body, door, inside])
    bpy.context.view_layer.update()
    report("病棟・閉", [body, door, inside] + paper, room + nurse, results)
    report("病棟・閉（ロッカーの部品）", [door, inside], [body], results)
    open_pose(door, "hinge", ("Z", 100))
    bpy.context.view_layer.update()
    report("病棟・開", [door], room + nurse + [body, inside], results)

    # ---------------- 黒田の自宅 ----------------
    clear()
    M = kuroda_room.mats()
    room = kuroda_room.shell(M) + kuroda_room.interior(M)
    dining = kuroda_room.dining(M)
    place(dining, (0, 0, 0.6))
    FM = __import__("build_fill").mats()
    body, flap, inside = fp.kuroda_bureau(FM)
    paper = fp.paper_kmemo2(FM)
    place(paper, (0, 1.05, -0.12), 180)       # 書き物机の中の位置（机と一緒に置く）
    bu = [body, flap, inside] + paper
    place(bu, (3.5 - 0.075 - 0.014 - 0.225, 0, 1.7), -90)
    lift(paper); lift([inside]); lift([body, flap, inside] + paper)
    bpy.context.view_layer.update()
    report("黒田・閉", [body, flap, inside] + paper, room + dining, results)
    report("黒田・閉（机の部品）", [flap, inside] + paper, [body], results)
    open_pose(flap, "hinge", ("X", 90 + fp.BU_LEAN))
    bpy.context.view_layer.update()
    report("黒田・開", [flap], room + dining + [body, inside], results)

    # ---------------- 佐伯の自宅（書斎机） ----------------
    clear()
    M = saeki_room.mats()
    desk_pos = (-3.5 + 1.2, 0, -1.8)
    desk = saeki_room.desk(M)
    chair = saeki_room.chair(M)
    place(chair, (0, 0, -0.8))
    drawer = saeki_room.drawer(M)
    plog = saeki_room.plog(M)
    place(plog, (-0.2, 0.76, 0.05))
    unsent = saeki_room.unsent(M)
    place(unsent, (0.35, 0.755, -0.15))
    desk = desk + plog + unsent
    paper = fp.paper_diary(__import__("build_fill").mats())
    place(paper, (-0.55, 0.75, 0.08), 8)
    allp = desk + chair + drawer + paper
    place(allp, desk_pos)
    lift(paper)
    room = saeki_room.shell(M) + saeki_room.interior(M)
    bpy.context.view_layer.update()
    report("佐伯・閉", drawer + paper, desk + chair + room, results)
    report("佐伯・閉（机の上）", paper, plog + unsent, results)
    for d in drawer:
        open_pose(d, "slide", (0, 0, -0.3))
    bpy.context.view_layer.update()
    report("佐伯・開", drawer, desk + chair + room, results)

    print("[overlap] ===== 結果 =====")
    if results:
        for r in results:
            print("[overlap]" + r)
    else:
        print("[overlap]   めり込み無し")


if __name__ == "__main__":
    main()
