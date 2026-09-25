import bpy, bmesh, math
from mathutils import Vector, Matrix

# ---- Unity座標で作る（Unity = (-bx, bz, -by) の逆）。ビルダーの数値をそのまま写せる ----
def U(ux, uy, uz): return Vector((-ux, -uz, uy))

# LP_材質の見本色（Blenderの確認レンダー用。Unity側は同名の既存マテリアルに差し替わる）
PALETTE = {
 "LP_FacilityWall":(0.86,0.87,0.86),"LP_FacilityCeiling":(0.9,0.9,0.9),"LP_Linoleum":(0.5,0.55,0.52),
 "LP_FacilityTrim":(0.45,0.47,0.5),"LP_FacilityDoor":(0.55,0.57,0.6),"LP_FloorSeam":(0.3,0.33,0.32),
 "LP_Concrete":(0.42,0.42,0.43),"LP_MetalFloor":(0.28,0.29,0.31),"LP_CoreCeiling":(0.3,0.3,0.32),
 "LP_CoreTrim":(0.2,0.2,0.22),"LP_CoreDoor":(0.35,0.36,0.38),"LP_HazardYellow":(0.85,0.7,0.1),
 "LP_HomeWall":(0.88,0.84,0.74),"LP_HomeWood":(0.55,0.42,0.3),"LP_HomeCeiling":(0.93,0.9,0.84),
 "LP_HomeTrim":(0.5,0.38,0.26),"LP_HomeDoor":(0.55,0.4,0.28),
 "LP_RoomWall":(0.78,0.78,0.76),"LP_RoomFloor":(0.5,0.48,0.45),"LP_Ceiling":(0.96,0.96,0.97),"LP_Door":(0.82,0.78,0.72),
 "LP_TrainWall":(0.9,0.87,0.78),"LP_TrainFloor":(0.52,0.52,0.5),"LP_TrainCeiling":(0.92,0.9,0.83),"LP_TrainDoor":(0.62,0.1,0.1),
 "LP_TrainChrome":(0.75,0.76,0.78),"LP_White":(0.92,0.92,0.93),"LP_WoodFloor":(0.72,0.6,0.48),"LP_DoorFrame":(0.55,0.5,0.45),
 "LP_Metal":(0.5,0.52,0.55),"LP_Breaker":(0.35,0.38,0.42),"LP_BreakerLever":(0.85,0.25,0.2),
 # シェル用に新設する材質（Unity側の表で色を定義）
 "LP_ShellWainscot":(0.72,0.76,0.74),"LP_ShellRail":(0.62,0.66,0.66),"LP_ShellCove":(0.22,0.24,0.25),
 "LP_ShellGrid":(0.8,0.8,0.8),"LP_ShellSeam":(0.3,0.3,0.31),"LP_ShellBolt":(0.45,0.46,0.48),
 "LP_ShellStain":(0.55,0.53,0.48),"LP_StudyPanel":(0.28,0.18,0.11),"LP_StudyPanelDark":(0.2,0.12,0.07),
 "LP_KidBand":(0.55,0.72,0.82),"LP_ShellGlass":(0.08,0.1,0.12),"LP_ShellSign":(0.15,0.55,0.3),
 "LP_TrainLightStrip":(1.0,0.97,0.9),
 # 回廊・扉・ブレイカー用
 "LP_CorridorWainscot":(0.62,0.6,0.55),"LP_CorridorLamp":(1.0,0.97,0.9),
 "LP_DoorGlass":(0.12,0.14,0.16),"LP_Brass":(0.7,0.55,0.3),
}
# 確認レンダーで光らせる材質（Unity側は ShellMat() の表で発光を設定）
EMISSIVE = {"LP_TrainLightStrip": 4.0, "LP_CorridorLamp": 3.0}
def mat(name):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name); m.use_nodes = True
    bsdf = [n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"][0]
    c = PALETTE.get(name, (0.6,0.6,0.6))
    bsdf.inputs["Base Color"].default_value = (*c, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.7
    if name in ("LP_Metal","LP_TrainChrome","LP_ShellBolt","LP_CoreDoor"): bsdf.inputs["Metallic"].default_value = 0.6
    if name == "LP_ShellSign":
        bsdf.inputs["Emission Color"].default_value = (*c,1.0); bsdf.inputs["Emission Strength"].default_value = 3.0
    if name in EMISSIVE:
        bsdf.inputs["Emission Color"].default_value = (*c,1.0); bsdf.inputs["Emission Strength"].default_value = EMISSIVE[name]
    return m

class Kit:
    """Unity座標で部品を足していき、build()で1オブジェクトにする。UVはUnityの箱と同じ 0.5/m の箱投影"""
    def __init__(self, name):
        self.name = name; self.bm = bmesh.new(); self.mats = []
    def slot(self, m):
        if m not in self.mats: self.mats.append(m)
        return self.mats.index(m)
    def _face(self, vs, m):
        f = self.bm.faces.new(vs); f.material_index = self.slot(m); return f
    def box(self, cx, cy, cz, sx, sy, sz, m):
        """Unityの Box(pos, size) と同じ指定"""
        c = U(cx, cy, cz); h = Vector((sx/2, sz/2, sy/2))
        x0,x1,y0,y1,z0,z1 = c.x-h.x, c.x+h.x, c.y-h.y, c.y+h.y, c.z-h.z, c.z+h.z
        vs = [self.bm.verts.new(v) for v in [(x0,y0,z0),(x1,y0,z0),(x1,y1,z0),(x0,y1,z0),(x0,y0,z1),(x1,y0,z1),(x1,y1,z1),(x0,y1,z1)]]
        for idx in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]:
            self._face([vs[i] for i in idx], m)
    def span(self, x0, x1, y0, y1, z0, z1, m):
        """Unity座標の範囲指定の箱"""
        self.box((x0+x1)/2, (y0+y1)/2, (z0+z1)/2, abs(x1-x0), abs(y1-y0), abs(z1-z0), m)
    def cyl(self, p0, p1, r, m, seg=8, r1=None):
        """Unity座標の2点を結ぶ円柱（r1で円錐台）"""
        a, b = U(*p0), U(*p1); d = b - a; L = d.length
        if L < 1e-6: return
        q = Vector((0,0,1)).rotation_difference(d.normalized())
        r1 = r if r1 is None else r1
        def ring(rad, z):
            return [self.bm.verts.new(a + q @ Vector((rad*math.cos(2*math.pi*i/seg), rad*math.sin(2*math.pi*i/seg), z))) for i in range(seg)]
        v0 = ring(r, 0); v1 = ring(r1, L)
        for i in range(seg):
            j = (i+1) % seg; self._face([v0[i], v0[j], v1[j], v1[i]], m)
        self._face(list(reversed(v0)), m); self._face(v1, m)
    def prism_x(self, profile_yz, x0, x1, m):
        """Unity YZ平面の輪郭をX方向に押し出す（回り縁などの断面）。profile=[(uy,uz)...]"""
        v0 = [self.bm.verts.new(U(x0, y, z)) for (y, z) in profile_yz]
        v1 = [self.bm.verts.new(U(x1, y, z)) for (y, z) in profile_yz]
        n = len(v0)
        for i in range(n):
            j = (i+1) % n; self._face([v0[i], v0[j], v1[j], v1[i]], m)
        self._face(list(reversed(v0)), m); self._face(v1, m)
    def prism_z(self, profile_xy, z0, z1, m):
        """Unity XY平面の輪郭をZ方向に押し出す。profile=[(ux,uy)...]"""
        v0 = [self.bm.verts.new(U(x, y, z0)) for (x, y) in profile_xy]
        v1 = [self.bm.verts.new(U(x, y, z1)) for (x, y) in profile_xy]
        n = len(v0)
        for i in range(n):
            j = (i+1) % n; self._face([v0[i], v0[j], v1[j], v1[i]], m)
        self._face(list(reversed(v0)), m); self._face(v1, m)
    def tris(self):
        return sum(len(f.verts)-2 for f in self.bm.faces)
    def build(self):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        self.bm.normal_update()
        uvl = self.bm.loops.layers.uv.new("UVMap")
        for f in self.bm.faces:
            n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
            for lp in f.loops:
                p = lp.vert.co; ux, uy, uz = -p.x, p.z, -p.y
                if ax == 2: uv = (ux, uz)        # 床・天井
                elif ax == 1: uv = (ux, uy)      # 南北の壁
                else: uv = (uz, uy)              # 東西の壁
                lp[uvl].uv = (uv[0]*0.5, uv[1]*0.5)
        old = bpy.data.objects.get(self.name)
        if old:
            me = old.data; bpy.data.objects.remove(old, do_unlink=True)
            if me and me.users == 0: bpy.data.meshes.remove(me)
        mesh = bpy.data.meshes.new(self.name + "Mesh"); self.bm.to_mesh(mesh); self.bm.free()
        for p in mesh.polygons: p.use_smooth = False
        obj = bpy.data.objects.new(self.name, mesh); bpy.context.scene.collection.objects.link(obj)
        for mn in self.mats: obj.data.materials.append(mat(mn))
        return obj

def clear_scene():
    for o in list(bpy.data.objects):
        if o.type in ("MESH", "EMPTY"): bpy.data.objects.remove(o, do_unlink=True)
    for me in list(bpy.data.meshes):
        if me.users == 0: bpy.data.meshes.remove(me)

def export(obj_names, path):
    """ルート無しで複数メッシュを1つのFBXに（Unity側では子として並ぶ）"""
    import os
    for o in bpy.data.objects: o.select_set(False)
    objs = [bpy.data.objects[n] for n in obj_names]
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH","EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", use_mesh_modifiers=True, add_leaf_bones=False, path_mode="AUTO")
    return os.path.getsize(path)
