"""
黒塗りの資料（穴埋め）まわりの品質重視モデル：部屋に置く黒塗りの紙・封筒・開く入れ物。

座標は Unity の値（hq.U で Blender へ写す）。入れ物は「入れ物の原点（底の中心、正面 = +Z）」を原点に作る。
開く部品（扉・ふた）は物体の原点を蝶番の軸に置く（Unity 側で FillContainer がその原点のまわりに回す）。
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import hq
from hq import U, span, box, cyl, cyl_between, quad, finish, join  # noqa: F401


# ============================== 補助 ==============================

def set_origin(o, p):
    """物体の原点を Unity 座標 p に移す（形はそのまま）"""
    o.data.transform(Matrix.Translation(-U(*p)))
    o.location = U(*p)
    return o


def prism_x(name, x0, x1, pts_zy, mat, bev=0.004):
    """Unity の (z, y) 断面の多角形を x0〜x1 に押し出す（斜めの天板の側板など）。pts は表から見て反時計回り"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    a = [bm.verts.new(U(x0, y, z)) for z, y in pts_zy]
    b = [bm.verts.new(U(x1, y, z)) for z, y in pts_zy]
    n = len(pts_zy)
    bm.faces.new(a[::-1])
    bm.faces.new(b)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([a[i], a[j], b[j], b[i]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    o = hq._obj(name, me, mat)
    if bev > 0:
        hq.bevel(o, bev, 2)
    return o


def sheet_poly(name, pts_xz, w, d, y, mat, thick=0.0006, curl=0.0):
    """水平な紙1枚。pts_xz は外形（Unity の x, z。上から見て反時計回り）。UV は w x d の外接長方形に 0〜1
    （u は +x、v は +z が上 = 文字の上が奥）。curl > 0 で四隅を少し反らせる"""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    vs = []
    for x, z in pts_xz:
        lift = curl * ((2 * x / w) ** 2 + (2 * z / d) ** 2) * 0.5
        vs.append(bm.verts.new(U(x, y + lift, z)))
    f = bm.faces.new(vs)
    uv = bm.loops.layers.uv.new("UVMap")
    for l in f.loops:
        co = l.vert.co
        ux, uz = -co.x, -co.y            # Blender → Unity の x, z
        l[uv].uv = ((ux + w / 2) / w, (uz + d / 2) / d)
    bmesh.ops.recalc_face_normals(bm, faces=[f])
    bm.to_mesh(me); bm.free()
    o = hq._obj(name, me, mat)
    hq.face_toward(o, (0, 1, 0))
    if thick > 0:
        # 厚みは上（法線の側）へ付ける。下へ付けると置いた面（机・棚）にめり込む
        sol = o.modifiers.new("Solid", "SOLIDIFY"); sol.thickness = thick; sol.offset = 1
    return finish(o, keep_uv=True, angle=60)


def rect_pts(w, d, n=1):
    """中心原点の長方形の外周（反時計回り）。n = 各辺の分割数（反りを付ける時）"""
    pts = []
    corners = [(-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)]
    for i in range(4):
        (x0, z0), (x1, z1) = corners[i], corners[(i + 1) % 4]
        for k in range(n):
            t = k / n
            pts.append((x0 + (x1 - x0) * t, z0 + (z1 - z0) * t))
    return pts


def torn_edge_pts(w, d, side, seed, teeth=26, depth=0.004):
    """1辺だけちぎれた紙の外周。side = 'left' / 'top'"""
    rnd = random.Random(seed)
    pts = []
    if side == "left":
        pts.append((-w / 2 + depth, -d / 2)); pts.append((w / 2, -d / 2)); pts.append((w / 2, d / 2))
        for k in range(teeth + 1):
            z = d / 2 - d * k / teeth
            pts.append((-w / 2 + rnd.uniform(0, depth * 1.6), z))
    else:
        pts.append((-w / 2, -d / 2)); pts.append((w / 2, -d / 2))
        for k in range(teeth + 1):
            x = w / 2 - w * k / teeth
            pts.append((x, d / 2 - rnd.uniform(0, depth * 2.2)))
    return pts


def envelope(name, c, yaw, front_mat, body_mat, w=0.2, d=0.125, t=0.003, tilt=0.0):
    """封筒（上面に宛名のテクスチャ。縁は封筒の地の色）。c = 底の中心、yaw = 度、
    tilt = 手前（Unity +Z）へ起こす角度（度。立てかける時。宛名が手前を向く）"""
    body = box(name + "_Body", (0, t / 2, 0), (w, t, d), body_mat, bev=0.0012, segs=2)
    finish(body, 1.0)
    face = sheet_poly(name + "_Face", rect_pts(w - 0.002, d - 0.002), w - 0.002, d - 0.002, t + 0.0002, front_mat, thick=0.0)
    parts = [body, face]
    # Blender の +X 軸まわりの正の回転は、上（Unity +Y）を Unity の手前（+Z）へ倒す
    # 先に yaw で回してから起こす（立てかけた封筒は yaw=180 で、宛名の上が上・左右も正しく手前を向く）
    m = Matrix.Translation(U(*c)) @ Matrix.Rotation(math.radians(tilt), 4, "X") @ Matrix.Rotation(math.radians(-yaw), 4, "Z")
    for o in parts:
        o.data.transform(m)
    if tilt:
        rest_on(parts, c[1])
    return parts


def rest_on(objs, y):
    """物の一番低い所を Unity の高さ y にそろえる（置いた面にめり込ませない・浮かせない）"""
    lo = min(v.co.z for o in objs for v in o.data.vertices)
    for o in objs:
        o.data.transform(Matrix.Translation((0, 0, y - lo)))


def uv_atlas_box(o, col, ncols, plain_v=(0.82, 0.9)):
    """箱の前面（+Z）にアトラスの col 列をまるごと、ほかの面には同じ列の無地の帯を張る（バインダーの背と表紙）"""
    me = o.data
    bm = bmesh.new(); bm.from_mesh(me)
    # ほかの部品と同じ名前の UV にする（join で同じ層にまとまるように。verify() だと名前が Float2 になる）
    uv = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    u0, u1 = col / ncols, (col + 1) / ncols
    xs = [v.co.x for v in bm.verts]; zs = [v.co.z for v in bm.verts]
    xmin, xmax, zmin, zmax = min(xs), max(xs), min(zs), max(zs)
    for f in bm.faces:
        n = f.normal
        front = n.y < -0.7            # Unity +Z = Blender -Y
        for l in f.loops:
            co = l.vert.co
            if front:
                fu = (co.x - xmin) / max(1e-6, (xmax - xmin))   # 手前から見て左（Unity +X = Blender -X）が u=0
                fv = (co.z - zmin) / max(1e-6, (zmax - zmin))
                l[uv].uv = (u0 + (u1 - u0) * fu, fv)
            else:
                l[uv].uv = ((u0 + u1) / 2 + (co.x * 7 % 1) * 0.001, plain_v[0] + (plain_v[1] - plain_v[0]) * ((co.z * 3) % 1))
    bm.to_mesh(me); bm.free()


# ============================== 部屋に置く黒塗りの紙 ==============================

def paper_guide(M):
    """来客用の案内状（A4・印刷）"""
    return [sheet_poly("Fill_Guide", rect_pts(0.21, 0.297, 6), 0.21, 0.297, 0.0, M["guide"], curl=0.003)]


def paper_diary(M):
    """佐伯の日記の欠けたページ（A5・左端がちぎれている）"""
    return [sheet_poly("Fill_DiaryPage", torn_edge_pts(0.148, 0.21, "left", 5), 0.148, 0.21, 0.0, M["diary"], curl=0.002)]


def paper_page303(M):
    """303号の観察記録（A4）"""
    return [sheet_poly("Fill_Page303", rect_pts(0.21, 0.297, 6), 0.21, 0.297, 0.0, M["page303"], curl=0.003)]


def paper_kmemo2(M):
    """黒田のメモの破り取られた後半（上の端がちぎれている）"""
    return [sheet_poly("Fill_KMemo2", torn_edge_pts(0.18, 0.152, "top", 7), 0.18, 0.152, 0.0, M["kmemo2"], curl=0.002)]


# ============================== 研究所応接室：ガラス戸の書類棚 ==============================

# 部屋の中の置き場所（ビルダー LoopPrototypeBuilder と、壁の造作を切る外殻スクリプトで同じ値を使う）
LAB_CAB_X = 3.4          # 研究所応接室：書類棚の中心 x（南の壁。後ろのルーバーの羽根を抜く）
WARD_LOCKER_X = -2.0     # 臨床病棟：ロッカー3連の中心 x（南の壁。後ろのバンパーレールと巾木を切る）

LAB_W, LAB_H, LAB_D = 0.9, 1.85, 0.42
LAB_HINGE = (-LAB_W / 2 + 0.004, 0.0, LAB_D / 2)
LAB_SHELVES = (0.5, 0.95, 1.4)
LAB_VISIT = (0.17, LAB_SHELVES[1] + 0.016, 0.0)   # 記録票の置き場所（調べる判定もここ）


def lab_cabinet(M):
    """スチールの書類棚（中は3段の棚。上下の段にバインダー、中段に面会相談の記録票）。
    返す: (本体, 扉, 記録票)。扉の原点 = 左の蝶番"""
    hw, hd = LAB_W / 2, LAB_D / 2
    st, blk = M["steel"], M["lblack"]
    p = []
    p.append(span("LF_Plinth", -hw + 0.02, hw - 0.02, 0.0, 0.06, -hd + 0.02, hd - 0.04, blk, 0.003))
    for sx in (-1, 1):
        x0, x1 = (-hw, -hw + 0.018) if sx < 0 else (hw - 0.018, hw)
        p.append(span(f"LF_Side{sx}", x0, x1, 0.06, LAB_H, -hd, hd - 0.022, st, 0.003))
    p.append(span("LF_Top", -hw, hw, LAB_H - 0.02, LAB_H, -hd, hd - 0.022, st, 0.004))
    p.append(span("LF_Bottom", -hw + 0.018, hw - 0.018, 0.06, 0.08, -hd + 0.01, hd - 0.022, st, 0.002))
    p.append(span("LF_Back", -hw + 0.018, hw - 0.018, 0.08, LAB_H - 0.02, -hd, -hd + 0.012, st, 0.002))
    for i, y in enumerate(LAB_SHELVES):
        p.append(span(f"LF_Shelf{i}", -hw + 0.018, hw - 0.018, y, y + 0.016, -hd + 0.012, hd - 0.03, st, 0.002))
        p.append(span(f"LF_Lip{i}", -hw + 0.018, hw - 0.018, y - 0.012, y + 0.016, hd - 0.042, hd - 0.03, st, 0.002))
    # 前面の枠（扉の当たり）
    p.append(span("LF_FaceL", -hw, -hw + 0.03, 0.06, LAB_H, hd - 0.024, hd - 0.022, st, 0.0))
    p.append(span("LF_FaceR", hw - 0.03, hw, 0.06, LAB_H, hd - 0.024, hd - 0.022, st, 0.0))
    # バインダー（下の段・上の段に立てて並べる。中の段の左に少し）
    rnd = random.Random(301)
    rows = [(0.08, 0.32, -hw + 0.03, hw - 0.03), (LAB_SHELVES[2] + 0.016, 0.33, -hw + 0.03, hw - 0.12),
            (LAB_SHELVES[0] + 0.016, 0.3, -hw + 0.03, hw - 0.03), (LAB_SHELVES[1] + 0.016, 0.3, -hw + 0.03, -0.1)]
    k = 0
    for y0, bh, xa, xb in rows:
        x = xa
        while x + 0.06 < xb:
            bw = rnd.choice((0.055, 0.065, 0.075))
            h = bh * rnd.uniform(0.94, 1.0)
            b = box(f"LF_Binder{k}", (x + bw / 2, y0 + h / 2, -hd + 0.03 + 0.14), (bw - 0.004, h, 0.28), M["binders"], bev=0.004, segs=2)
            hq.apply_mods(b)
            uv_atlas_box(b, k % 8, 8)
            hq.smooth(b, 40)
            p.append(b)
            x += bw
            k += 1
    # 上の段の右端：横に寝かせた2冊
    for j in range(2):
        b = box(f"LF_BinderFlat{j}", (hw - 0.07, LAB_SHELVES[2] + 0.016 + 0.035 + j * 0.07, -hd + 0.17), (0.1, 0.068, 0.28), M["binders"], bev=0.004, segs=2)
        hq.apply_mods(b)
        uv_atlas_box(b, (k + j) % 8, 8)
        hq.smooth(b, 40)
        p.append(b)
    for o in p:
        if not o.name.startswith("LF_Binder"):
            finish(o, 1.0, angle=40)
    bodyo = join(p, "LabFill_Body")

    # 扉：スチールの枠＋ガラス、右に取っ手と鍵穴
    d = []
    z0, z1 = hd - 0.02, hd
    y0, y1 = 0.07, LAB_H - 0.01
    fw = 0.045
    d.append(span("LD_L", -hw + 0.004, -hw + 0.004 + fw, y0, y1, z0, z1, st, 0.004))
    d.append(span("LD_R", hw - 0.004 - fw, hw - 0.004, y0, y1, z0, z1, st, 0.004))
    d.append(span("LD_T", -hw + 0.004, hw - 0.004, y1 - fw, y1, z0, z1, st, 0.004))
    d.append(span("LD_B", -hw + 0.004, hw - 0.004, y0, y0 + fw * 1.4, z0, z1, st, 0.004))
    g = span("LD_Glass", -hw + 0.004 + fw, hw - 0.004 - fw, y0 + fw * 1.4, y1 - fw, z0 + 0.008, z0 + 0.012, M["glass"], 0.0)
    d.append(g)
    hx = hw - 0.004 - fw / 2
    d.append(cyl_between("LD_Handle", (hx, 0.92, z1 + 0.028), (hx, 1.16, z1 + 0.028), 0.008, M["alu"], 16))
    for yy in (0.94, 1.14):
        d.append(cyl_between(f"LD_HandleFoot{yy}", (hx, yy, z1), (hx, yy, z1 + 0.03), 0.006, M["alu"], 12))
    d.append(cyl_between("LD_Lock", (hx, 0.86, z1 - 0.001), (hx, 0.86, z1 + 0.006), 0.011, M["alu"], 20))
    d.append(span("LD_Keyhole", hx - 0.0015, hx + 0.0015, 0.853, 0.867, z1 + 0.006, z1 + 0.0065, blk, 0.0))
    for o in d:
        if o is not g:
            finish(o, 1.0, angle=40)
    dooro = join(d, "LabFill_Door")
    set_origin(dooro, LAB_HINGE)

    # 面会相談の記録票（クリップボード。中の段の手前に寝かせる）
    v = []
    cy = LAB_SHELVES[1] + 0.016   # 目の高さに近い3段目の右
    v.append(span("LV_Board", -0.12, 0.12, 0.0, 0.006, -0.165, 0.165, M["board"], 0.002))
    v.append(sheet_poly("LV_Sheet", rect_pts(0.21, 0.297), 0.21, 0.297, 0.0065, M["visitlog"], thick=0.0))
    v.append(span("LV_Clip", -0.045, 0.045, 0.006, 0.016, 0.135, 0.16, M["alu"], 0.003))
    v.append(cyl_between("LV_ClipRoll", (-0.04, 0.017, 0.15), (0.04, 0.017, 0.15), 0.006, M["alu"], 16))
    for o in v:
        if not o.name.startswith("LV_Sheet"):
            finish(o, 1.0, angle=40)
    visit = join(v, "LabFill_Visit")
    # 手前（+Z）に立つ人から読める向き（クリップ＝上が奥）
    visit.data.transform(Matrix.Translation(U(LAB_VISIT[0], cy, LAB_VISIT[2])) @ Matrix.Rotation(math.radians(188), 4, "Z"))
    return [bodyo, dooro, visit]


# ============================== 臨床病棟：職員ロッカー（真ん中が水野） ==============================

LK_W, LK_H, LK_D = 0.45, 1.8, 0.5
LK_HINGE = (-LK_W / 2 + 0.006, 0.0, LK_D / 2)


def _locker_door(name, x0, x1, M, row=0):
    """ロッカーの扉1枚（スチール。上下にスリット、右に取っ手、上に名札入れ）"""
    z0, z1 = LK_D / 2 - 0.016, LK_D / 2
    y0, y1 = 0.095, LK_H - 0.012
    p = [span(name + "_Panel", x0 + 0.004, x1 - 0.004, y0, y1, z0, z1, M["ivory"], 0.004)]
    cx = (x0 + x1) / 2
    for band in (y1 - 0.12, y0 + 0.08):
        for k in range(5):
            yy = band - k * 0.022
            p.append(span(f"{name}_Slit{band:.2f}_{k}", cx - 0.12, cx + 0.12, yy - 0.004, yy + 0.004, z1, z1 + 0.0015, M["wblack"], 0.0))
    hx = x1 - 0.06
    p.append(span(name + "_HandleBase", hx - 0.022, hx + 0.022, 0.93, 1.07, z1, z1 + 0.006, M["chrome"], 0.003))
    p.append(span(name + "_Handle", hx - 0.012, hx + 0.012, 0.95, 1.05, z1 + 0.006, z1 + 0.02, M["chrome"], 0.004))
    p.append(cyl_between(name + "_Lock", (hx, 1.1, z1), (hx, 1.1, z1 + 0.008), 0.01, M["chrome"], 20))
    p.append(span(name + "_Keyhole", hx - 0.0015, hx + 0.0015, 1.093, 1.107, z1 + 0.008, z1 + 0.0085, M["wblack"], 0.0))
    # 名札入れと名札（名前はアトラス namecards の row 段目。手前から見て左 = +X が文字の左）
    p.append(span(name + "_CardFrame", cx - 0.07, cx + 0.07, y1 - 0.29, y1 - 0.24, z1, z1 + 0.004, M["chrome"], 0.002))
    yb, yt, zc = y1 - 0.284, y1 - 0.246, z1 + 0.0046
    v0, v1 = 1.0 - (row + 1) / 3.0, 1.0 - row / 3.0
    card = quad(name + "_Card", [(cx + 0.062, yb, zc), (cx - 0.062, yb, zc), (cx - 0.062, yt, zc), (cx + 0.062, yt, zc)], M["names"],
                uv=((0, v0), (1, v0), (1, v1), (0, v1)))
    hq.face_toward(card, (0, 0, 1))
    card["keep_uv"] = True
    p.append(card)
    return p


def ward_lockers(M):
    """3連の職員ロッカー。真ん中（水野）だけ開き、中に棚・ハンガー掛け・タオル・封筒。
    返す: (本体, 真ん中の扉, 中の物)。扉の原点 = 左の蝶番（入れ物の原点から見て x = -0.225）"""
    W = LK_W * 3
    hw, hd = W / 2, LK_D / 2
    p = []
    p.append(span("WL_Plinth", -hw + 0.01, hw - 0.01, 0.0, 0.08, -hd + 0.01, hd - 0.03, M["wblack"], 0.003))
    for sx in (-1, 1):
        x0, x1 = (-hw, -hw + 0.012) if sx < 0 else (hw - 0.012, hw)
        p.append(span(f"WL_Side{sx}", x0, x1, 0.08, LK_H, -hd, hd - 0.018, M["ivory"], 0.003))
    for xd in (-LK_W / 2, LK_W / 2):
        p.append(span(f"WL_Div{xd}", xd - 0.005, xd + 0.005, 0.08, LK_H, -hd, hd - 0.018, M["ivory"], 0.0))
    p.append(span("WL_Top", -hw, hw, LK_H - 0.012, LK_H, -hd, hd - 0.018, M["ivory"], 0.004))
    p.append(span("WL_Floor", -hw + 0.012, hw - 0.012, 0.08, 0.09, -hd + 0.01, hd - 0.018, M["ivory"], 0.0))
    p.append(span("WL_Back", -hw + 0.012, hw - 0.012, 0.09, LK_H - 0.012, -hd, -hd + 0.01, M["ivory"], 0.0))
    # 前面の枠（扉の周り）
    p.append(span("WL_FrameT", -hw, hw, LK_H - 0.012, LK_H, hd - 0.018, hd - 0.016, M["ivory"], 0.0))
    # 左右のロッカー（開かない扉）
    p += _locker_door("WL_DoorL", -hw, -LK_W / 2, M, row=2)   # -X 側（手前から見て右）＝森
    p += _locker_door("WL_DoorR", LK_W / 2, hw, M, row=0)    # +X 側（手前から見て左）＝田村
    # 真ん中の中：棚・ハンガーパイプ
    p.append(span("WL_Shelf", -LK_W / 2 + 0.005, LK_W / 2 - 0.005, 1.5, 1.512, -hd + 0.01, hd - 0.04, M["ivory"], 0.002))
    p.append(cyl_between("WL_Rod", (-LK_W / 2 + 0.005, 1.42, -0.02), (LK_W / 2 - 0.005, 1.42, -0.02), 0.009, M["chrome"], 16))
    for o in p:
        finish(o, 1.0, angle=40, keep_uv=bool(o.get("keep_uv")))
    bodyo = join(p, "WardFill_Body")

    dooro = join([finish(o, 1.0, angle=40, keep_uv=bool(o.get("keep_uv"))) for o in _locker_door("WL_DoorM", -LK_W / 2, LK_W / 2, M, row=1)], "WardFill_Door")
    set_origin(dooro, LK_HINGE)

    # 中の物：たたんだタオル（棚の上の奥）と封筒（棚の上の手前）、ハンガー
    q = []
    towel = hq.pillow("WI_Towel", (-0.06, 1.512 + 0.025, -0.1), (0.22, 0.05, 0.16), M["towel"], puff=0.4, res=14, seed=5)
    q.append(finish(towel, 1.0, keep_uv=False, angle=60))
    rest_on([towel], 1.512)
    # 棚の奥の壁（z = -0.24）に上の端をもたせかける（78度に起こすと上の端は中心から 0.013 奥へ出る）
    q += envelope("WI_Env", (0.05, 1.512, -0.24 + 0.0625 * math.cos(math.radians(78)) + 0.004), 180, M["env_ward"], M["envbody"], tilt=78)
    # ハンガーのフック：パイプ（半径 0.009）に刺さらないよう、パイプの上を回って後ろへ垂れる鉤の形
    rc, ry, rz = 0.009 + 0.003 + 0.0015, 1.42, -0.02        # フックの中心線の半径・パイプの中心
    neck = (0.0, 1.38, rz + rc * math.cos(math.radians(-50)))
    hook = [neck]
    for k in range(0, 15):
        a = math.radians(-50 + k * 18)                       # 手前の下（-50度）→ 上 → 後ろの下（202度）
        hook.append((0.0, ry + rc * math.sin(a), rz + rc * math.cos(a)))
    q.append(hq.pipe("WI_HangerHook", hook, 0.003, M["chrome"], 0.004, 8))
    q.append(cyl_between("WI_HangerBar", (-0.17, 1.33, -0.02), (0.17, 1.33, -0.02), 0.004, M["chrome"], 8))
    for sx in (-1, 1):
        q.append(cyl_between(f"WI_HangerArm{sx}", neck, (sx * 0.17, 1.33, -0.02), 0.004, M["chrome"], 8))
    for o in q:
        if not o.name.startswith("WI_Env"):
            finish(o, 1.0, angle=50)
    inside = join(q, "WardFill_Inside")
    return [bodyo, dooro, inside]


# ============================== 黒田の自宅：書き物机（ライティングビューロー） ==============================

BU_W, BU_D = 0.8, 0.45
BU_FLOOR = 0.72                     # 書く台の高さ（ふたの蝶番）
BU_TOP = 1.02                       # 天板の下面
BU_TOPZ = -0.03                     # 斜めのふたの上の端（奥行き方向）
BU_FLAP_T = 0.02                    # ふたの厚み
BU_HINGE = (0.0, BU_FLOOR, BU_D / 2 - BU_FLAP_T)   # 蝶番 = ふたの内側の面の下の端（書く台の前の縁）
BU_LEAN = math.degrees(math.atan2(BU_HINGE[2] - BU_TOPZ, BU_TOP - BU_FLOOR))   # 閉じたふたの傾き（縦から奥へ）
_LEAN = math.radians(BU_LEAN)
# ふたの長さ：閉じた時に外側の上の角が天板の下面（BU_TOP）に当たらない所まで
BU_FLAP_L = (BU_TOP - 0.002 - BU_FLOOR - BU_FLAP_T * math.sin(_LEAN)) / math.cos(_LEAN)
# 閉じたふたの外側の面の上の端（奥行き）。側板の斜めの縁と天板の前の縁をここに合わせる
BU_FLAP_TOPZ = BU_HINGE[2] - BU_FLAP_L * math.sin(_LEAN) + BU_FLAP_T * math.cos(_LEAN)


def kuroda_bureau(M):
    """下に引き出し3段、上に斜めのふた（手前に倒すと書く台になる）。中に小さな整理棚と封筒。
    返す: (本体, ふた, 中の物)。ふたの原点 = 下の端の蝶番"""
    hw, hd = BU_W / 2, BU_D / 2
    wd, wl = M["kwooddark"], M["kwood"]
    p = []
    p.append(span("KB_Plinth", -hw + 0.02, hw - 0.02, 0.0, 0.06, -hd + 0.02, hd - 0.02, wd, 0.004))
    # 側板の斜めの縁は、閉じたふたの外側の面に沿わせる（ふたが側板より前に出ない・奥に沈まない）
    side = [(-hd, 0.06), (hd, 0.06), (hd, BU_FLOOR), (BU_FLAP_TOPZ, BU_TOP), (-hd, BU_TOP)]
    for sx in (-1, 1):
        x0, x1 = (-hw, -hw + 0.02) if sx < 0 else (hw - 0.02, hw)
        p.append(finish(prism_x(f"KB_Side{sx}", x0, x1, side, wd, 0.004), 1.0, angle=40))
    p.append(span("KB_Back", -hw + 0.02, hw - 0.02, 0.06, BU_TOP, -hd, -hd + 0.014, wd, 0.002))
    p.append(span("KB_Top", -hw - 0.01, hw + 0.01, BU_TOP, BU_TOP + 0.03, -hd, BU_FLAP_TOPZ + 0.012, wd, 0.008, 4))
    # 書く台は蝶番の線まで（その前は閉じたふた。開くとふたの内側が書く台と面一に続く）
    p.append(span("KB_Floor", -hw + 0.02, hw - 0.02, BU_FLOOR - 0.02, BU_FLOOR, -hd + 0.014, BU_HINGE[2] - 0.0015, wl, 0.003))   # ふたとの間に 1.5mm
    # 下の引き出し3段
    for i, (y0, y1) in enumerate(((0.07, 0.27), (0.285, 0.485), (0.5, BU_FLOOR - 0.03))):
        p.append(span(f"KB_Drawer{i}", -hw + 0.024, hw - 0.024, y0, y1, hd - 0.02, hd, wl, 0.006))
        for sx in (-1, 1):
            p += [o for o in _knob(f"KB_Knob{i}{sx}", (sx * 0.2, (y0 + y1) / 2, hd), M["brass"])]
    # 中の整理棚（奥。仕切り4枚・中段の棚・小さな引き出し）
    zf = -hd + 0.014 + 0.13
    for x in (-0.25, -0.09, 0.09, 0.25):
        p.append(span(f"KB_Pigeon{x}", x - 0.005, x + 0.005, BU_FLOOR, BU_TOP - 0.004, -hd + 0.014, zf, wl, 0.002))
    p.append(span("KB_PigeonShelf", -hw + 0.02, hw - 0.02, 0.86, 0.87, -hd + 0.014, zf, wl, 0.002))
    for x0, x1 in ((-0.085, 0.085),):
        p.append(span("KB_SmallDrawer", x0, x1, BU_FLOOR + 0.005, 0.855, zf - 0.012, zf, wd, 0.004))
        p += _knob("KB_SmallKnob", (0.0, (BU_FLOOR + 0.86) / 2, zf), M["brass"], r=0.008)
    # 整理棚の紙（差してある書類・はがき）
    rnd = random.Random(77)
    for k, (x0, x1) in enumerate(((-0.37, -0.26), (-0.24, -0.1), (0.1, 0.24), (0.26, 0.37))):
        for j in range(rnd.randint(2, 4)):
            yy = 0.875 + rnd.uniform(0.0, 0.01)
            zz = -hd + 0.03 + j * 0.02
            p.append(span(f"KB_Paper{k}{j}", x0 + 0.01, x1 - 0.01, 0.871, yy + rnd.uniform(0.07, 0.12), zz, zz + 0.002, M["kpaper"], 0.0))
    for o in p:
        finish(o, 1.0, angle=40)
    bodyo = join(p, "KurodaFill_Body")

    # ふた：閉じた形（縦から奥へ BU_LEAN 度傾く）で作る。外は濃い木、内は革張り、上の端に鍵穴
    # 長さは、閉じた時に外側の上の角が天板の下面（BU_TOP）に当たらない所まで
    T, L = BU_FLAP_T, BU_FLAP_L
    f = []
    # 内側の面が z = 0（蝶番の面）、外側が z = +T
    f.append(span("KF_Panel", -hw + 0.022, hw - 0.022, 0.0, L, 0.0, T, wd, 0.005))
    f.append(span("KF_Leather", -hw + 0.06, hw - 0.06, 0.04, L - 0.04, -0.0015, 0.0, M["leather"], 0.0))
    f.append(cyl_between("KF_Escutcheon", (0.0, L - 0.05, T), (0.0, L - 0.05, T + 0.004), 0.016, M["brass"], 24))
    f.append(span("KF_Keyhole", -0.002, 0.002, L - 0.06, L - 0.042, T + 0.004, T + 0.0045, M["kblack"], 0.0))
    f += _knob("KF_Knob", (0.0, L - 0.1, T), M["brass"], r=0.011)
    for o in f:
        finish(o, 1.0, angle=40)
    flap = join(f, "KurodaFill_Flap")
    # 蝶番（原点）のまわりに奥へ傾け、蝶番の位置へ
    # Blender の +X 軸まわりの正の回転は上（+Z）を Unity の手前（+z）へ倒すので、奥へは負の角度
    flap.data.transform(Matrix.Translation(U(*BU_HINGE)) @ Matrix.Rotation(-math.radians(BU_LEAN), 4, "X"))
    set_origin(flap, BU_HINGE)

    # 中の物：書く台の上の封筒
    # 手前（+Z）に立つ人から読める向き（宛名の上が奥）
    inside = join([o for o in envelope("KI_Env", (0.08, BU_FLOOR, 0.06), 174, M["env_kuroda"], M["envbody"])], "KurodaFill_Inside")
    return [bodyo, flap, inside]


def _knob(name, c, m, r=0.013, sgn=1):
    """前へ突き出す丸い取っ手（sgn=1 で +Z、-1 で -Z へ）"""
    p0, p1, p2 = c, (c[0], c[1], c[2] + sgn * 0.01), (c[0], c[1], c[2] + sgn * 0.022)
    a = cyl_between(name + "_s", p0, p1, r * 0.45, m, 16)
    b = cyl_between(name + "_h", p1, p2, r, m, 20)
    hq.bevel(b, r * 0.35, 3, 30)
    return [a, b]


# ============================== 佐伯の自宅：書斎机の引き出し ==============================

DR_X, DR_Y0, DR_Y1, DR_Z = 0.3, 0.63, 0.705, -0.29   # 机の前（-Z）の引き出しの口


def saeki_desk_apron(M):
    """机の幕板（引き出しの口を開けて、奥に引き出しの入る空間を作る）"""
    w = M["wood"]
    return [
        span("HK_ApronL", -0.6, -DR_X, 0.62, 0.715, DR_Z, 0.29, w, 0.004),
        span("HK_ApronR", DR_X, 0.6, 0.62, 0.715, DR_Z, 0.29, w, 0.004),
        span("HK_ApronB", -DR_X, DR_X, 0.62, DR_Y0, DR_Z, 0.29, w, 0.0),
        span("HK_ApronT", -DR_X, DR_X, DR_Y1, 0.715, DR_Z, 0.29, w, 0.0),
        span("HK_ApronBack", -DR_X, DR_X, DR_Y0, DR_Y1, 0.2, 0.29, w, 0.0),
    ]


def saeki_drawer(M):
    """書斎机の引き出し（閉じた位置。手前 = -Z へ引き出す）。中に封をした封筒"""
    wl, w, br = M["woodL"], M["wood"], M["brass"]
    x0, x1 = -DR_X + 0.004, DR_X - 0.004
    p = []
    p.append(span("HD_Front", x0, x1, DR_Y0 + 0.002, DR_Y1 - 0.002, DR_Z - 0.02, DR_Z, wl, 0.006))
    p += _knob("HD_Knob", (0.0, (DR_Y0 + DR_Y1) / 2, DR_Z - 0.02), br, sgn=-1)
    p.append(cyl_between("HD_Escutcheon", (0.09, (DR_Y0 + DR_Y1) / 2, DR_Z - 0.02), (0.09, (DR_Y0 + DR_Y1) / 2, DR_Z - 0.0235), 0.011, br, 20))
    p.append(span("HD_Keyhole", 0.0885, 0.0915, 0.662, 0.674, DR_Z - 0.0240, DR_Z - 0.0235, M["black"], 0.0))
    zb = 0.17
    for sx in (-1, 1):
        xa, xb = (x0 + 0.006, x0 + 0.016) if sx < 0 else (x1 - 0.016, x1 - 0.006)
        p.append(span(f"HD_Side{sx}", xa, xb, DR_Y0 + 0.004, DR_Y1 - 0.01, DR_Z, zb, w, 0.002))
    p.append(span("HD_Back", x0 + 0.006, x1 - 0.006, DR_Y0 + 0.004, DR_Y1 - 0.01, zb - 0.01, zb, w, 0.002))
    p.append(span("HD_Bottom", x0 + 0.006, x1 - 0.006, DR_Y0 + 0.004, DR_Y0 + 0.01, DR_Z, zb, w, 0.0))
    for o in p:
        finish(o, 1.0, angle=40)
    p += envelope("HD_Env", (0.02, DR_Y0 + 0.0102, DR_Z + 0.095), 4, M["env_saeki"], M["envbody"], w=0.19, d=0.11)
    return [join(p, "SaekiHome_Drawer")]
