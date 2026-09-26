"""
Zファイティングの検査：書き出した品質重視モデル（Assets/Models/HQ/**/*.fbx）を1つずつ読み込み、
同じ向きの面が同じ平面上（ずれ tol 以内）で重なっている所を探して、場所と面積を書き出す。

    blender --background --factory-startup --python GenAssets/blender/scripts/hq/zfight_check.py -- [絞り込み文字列]

出力: GenAssets/blender/zfight_report.txt（重なり面積の大きい順。座標は Unity の値）
同じモデルの中の重なりだけを見る（別のモデルどうし・Unity側の箱との重なりは対象外）。
"""
import glob
import math
import os
import sys

import bmesh
import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
HQ = os.path.join(REPO, "project", "Assets", "Models", "HQ")
OUT = os.path.join(REPO, "GenAssets", "blender", "zfight_report.txt")

TOL = 0.0004          # 平面のずれがこれ以内なら同一平面とみなす（m）
MIN_AREA = 0.00005    # これより小さい重なりは無視（0.5cm²）


def tri_area2d(p):
    return 0.5 * abs((p[1][0] - p[0][0]) * (p[2][1] - p[0][1]) - (p[2][0] - p[0][0]) * (p[1][1] - p[0][1]))


def poly_area(poly):
    a = 0.0
    for i in range(len(poly)):
        x0, y0 = poly[i]; x1, y1 = poly[(i + 1) % len(poly)]
        a += x0 * y1 - x1 * y0
    return abs(a) * 0.5


def clip(subject, clipper):
    """凸多角形どうしの交差（Sutherland–Hodgman）。clipper は反時計回り"""
    def inside(p, a, b):
        return (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0]) >= -1e-12

    def inter(p, q, a, b):
        x1, y1 = p; x2, y2 = q; x3, y3 = a; x4, y4 = b
        den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4)
        if abs(den) < 1e-18:
            return q
        t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den
        return (x1 + t * (x2 - x1), y1 + t * (y2 - y1))

    out = subject
    for i in range(len(clipper)):
        a, b = clipper[i], clipper[(i + 1) % len(clipper)]
        inp, out = out, []
        if not inp:
            break
        for j in range(len(inp)):
            p, q = inp[j], inp[(j + 1) % len(inp)]
            if inside(q, a, b):
                if not inside(p, a, b):
                    out.append(inter(p, q, a, b))
                out.append(q)
            elif inside(p, a, b):
                out.append(inter(p, q, a, b))
    return out


def ccw(t):
    a = (t[1][0] - t[0][0]) * (t[2][1] - t[0][1]) - (t[2][0] - t[0][0]) * (t[1][1] - t[0][1])
    return t if a > 0 else [t[0], t[2], t[1]]


def check_mesh(verts, tris):
    """verts (N,3)、tris (M,3) → [(面積, 中心)]"""
    P = verts[tris]                                   # (M,3,3)
    n = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0])
    ln = np.linalg.norm(n, axis=1)
    ok = ln > 1e-10
    P, n, ln = P[ok], n[ok], ln[ok]
    n = n / ln[:, None]
    d = np.einsum("ij,ij->i", n, P[:, 0])
    # 向きをそろえた法線で束ねる（同じ向きの面どうしだけが争う）
    key_n = np.round(n * 200).astype(int)
    order = np.lexsort((d, key_n[:, 2], key_n[:, 1], key_n[:, 0]))
    # 反対向きの面の索引（重なりが反対向きの面にふさがれていたら、外からは見えない = 数えない）
    opp = {}
    for idx in range(len(n)):
        opp.setdefault(tuple(key_n[idx]), []).append(idx)
    found = []
    i = 0
    M = len(order)
    while i < M:
        j = i + 1
        kn = tuple(key_n[order[i]])
        while j < M and tuple(key_n[order[j]]) == kn:
            j += 1
        grp = order[i:j]
        # 同じ向きの中で、平面の位置 d が近いものどうし
        gd = d[grp]
        s = np.argsort(gd)
        grp, gd = grp[s], gd[s]
        a = 0
        while a < len(grp):
            b = a + 1
            while b < len(grp) and gd[b] - gd[a] <= TOL:
                b += 1
            if b - a > 1:
                back = opp.get(tuple(-key_n[grp[a]]), [])
                back = [k for k in back if abs(-d[k] - gd[a]) <= TOL * 2]
                found += overlaps(P[grp[a:b]], n[grp[a]], P[back] if back else None)
            a = b
        i = j
    return found


def overlaps(T, nrm, B=None):
    """同一平面の三角形の集まりの中で、重なっている組の面積と中心。
    B = 同じ平面で反対向きの三角形（これに覆われた重なりは物の内側で見えないので数えない）"""
    # 平面の2D座標系
    u = np.cross(nrm, [0, 0, 1] if abs(nrm[2]) < 0.9 else [1, 0, 0]); u /= np.linalg.norm(u)
    v = np.cross(nrm, u)
    T2 = np.stack([T @ u, T @ v], axis=-1)            # (K,3,2)
    B2 = np.stack([B @ u, B @ v], axis=-1) if B is not None else None
    lo, hi = T2.min(axis=1), T2.max(axis=1)
    res = []
    K = len(T2)
    if K > 2500:
        return res                                     # 巨大な平面（床など）は隣接だけで重ならないので省く
    for i in range(K):
        cand = np.where((lo[i + 1:, 0] < hi[i, 0] - 1e-6) & (hi[i + 1:, 0] > lo[i, 0] + 1e-6) &
                        (lo[i + 1:, 1] < hi[i, 1] - 1e-6) & (hi[i + 1:, 1] > lo[i, 1] + 1e-6))[0] + i + 1
        if len(cand) == 0:
            continue
        ti = ccw([tuple(p) for p in T2[i]])
        for j in cand:
            tj = ccw([tuple(p) for p in T2[j]])
            poly = clip(ti, tj)
            if len(poly) >= 3:
                ar = poly_area(poly)
                if ar > MIN_AREA and B2 is not None:
                    # 反対向きの面に覆われている分を差し引く
                    covered = 0.0
                    for tb in B2:
                        q = clip(poly, ccw([tuple(p) for p in tb]))
                        if len(q) >= 3:
                            covered += poly_area(q)
                    ar -= covered
                if ar > MIN_AREA:
                    c = (T[i].mean(axis=0) + T[j].mean(axis=0)) / 2
                    res.append((ar, c, nrm))
    return res


def load(f):
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    bpy.ops.import_scene.fbx(filepath=f)
    out = []
    for o in bpy.context.scene.objects:
        if o.type != "MESH":
            continue
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.transform(o.matrix_world)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        verts = np.array([v.co[:] for v in bm.verts])
        bm.verts.index_update()
        tris = np.array([[v.index for v in fc.verts] for fc in bm.faces], dtype=int)
        bm.free()
        if len(tris):
            out.append((verts, tris))
    return out


def room_bounds(folder):
    """部屋の外殻（<部屋>_Shell.fbx）から、室内の範囲（壁の内面・天井）を Unity 座標で求める"""
    shells = glob.glob(os.path.join(folder, "*_Shell.fbx"))
    if not shells or "Corridor" in folder:
        return None
    allv = np.concatenate([v for v, _ in load(shells[0])])
    ux, uy, uz = -allv[:, 0], allv[:, 2], -allv[:, 1]
    return (np.abs(ux).max() - 0.15, uy.max() - 0.12, np.abs(uz).max() - 0.15)


def hidden(c, nrm, bounds, at_origin):
    """外から見えない面か：床下を向く面、部屋の外殻の外側・天井裏を向く面"""
    ux, uy, uz = -c[0], c[2], -c[1]
    nx, ny, nz = -nrm[0], nrm[2], -nrm[1]
    if uy <= 0.003 and ny < -0.5:
        return True                                    # 床に接する底面
    if bounds is None or not at_origin:
        return False
    hw, top, hd = bounds
    if abs(ux) >= hw - 0.004 and nx * ux > 0.5 * abs(ux):
        return True                                    # 東西の壁の内面より外を向く
    if abs(uz) >= hd - 0.004 and nz * uz > 0.5 * abs(uz):
        return True                                    # 南北の壁の内面より外を向く
    if uy >= top - 0.004 and ny > 0.5:
        return True                                    # 天井裏
    return False


# 部屋の原点に置く部品（室内の範囲で見えない面を判定できる）
AT_ORIGIN = ("_Shell", "_Interior", "_Beds", "_Nurse", "_Core", "_Altar", "_Dining", "_Lamps", "_Racks", "_Walls")


def main():
    filt = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv and len(sys.argv) > sys.argv.index("--") + 1 else ""
    files = sorted(glob.glob(os.path.join(HQ, "**", "*.fbx"), recursive=True))
    files = [f for f in files if filt in f]
    lines = []
    total = 0
    bounds_cache = {}
    for f in files:
        folder = os.path.dirname(f)
        if folder not in bounds_cache:
            bounds_cache[folder] = room_bounds(folder)
        bounds = bounds_cache[folder]
        at_origin = any(k in os.path.basename(f) for k in AT_ORIGIN)
        hits = []
        for verts, tris in load(f):
            hits += check_mesh(verts, tris)
        hits = [(a, c) for a, c, nrm in hits if not hidden(c, nrm, bounds, at_origin)]
        if hits:
            area = sum(a for a, _ in hits)
            total += len(hits)
            rel = os.path.relpath(f, HQ)
            lines.append(f"{rel}: {len(hits)}か所  合計 {area * 1e4:.1f} cm²")
            # 近い場所はまとめて、大きい順に上位10か所
            hits.sort(key=lambda h: -h[0])
            shown = []
            for a, c in hits:
                uc = (-c[0], c[2], -c[1])               # Blender → Unity
                if any(abs(uc[0] - s[0]) < 0.05 and abs(uc[1] - s[1]) < 0.05 and abs(uc[2] - s[2]) < 0.05 for s in shown):
                    continue
                shown.append(uc)
                lines.append(f"    {a * 1e4:7.2f} cm²  Unity({uc[0]:.3f}, {uc[1]:.3f}, {uc[2]:.3f})")
                if len(shown) >= 10:
                    break
        print(f"[zfight] {os.path.relpath(f, HQ)}: {len(hits)}")
    with open(OUT, "w", encoding="utf-8") as fp:
        fp.write(f"Zファイティング候補（同一モデル内、同じ向きの面が {TOL * 1000:.1f}mm 以内で重なる所）: 計{total}か所\n\n")
        fp.write("\n".join(lines) + "\n")
    print(f"[zfight] total {total} -> {OUT}")


if __name__ == "__main__":
    main()
