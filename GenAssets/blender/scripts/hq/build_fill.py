"""
黒塗りの資料（穴埋め）まわりのモデルを作って Unity に書き出す。

    blender --background --factory-startup --python GenAssets/blender/scripts/hq/build_fill.py -- [--preview[=出力先]]

先にテクスチャ: python GenAssets/fill/make_textures.py
佐伯の書斎机の引き出し（SaekiHome_Drawer）と、口を開けた机は build_saeki.py が書き出す。
出力（部品ごとに別の物体。座標は Unity の値、入れ物の原点 = 底の中心、正面 = +Z）:
  Lab/Lab_FillCabinet.fbx             LabFill_Body / LabFill_Door（原点 = 左の蝶番）/ LabFill_Visit（面会相談の記録票）
  Ward/Ward_FillLockers.fbx           WardFill_Body / WardFill_Door（真ん中の扉。原点 = 左の蝶番）/ WardFill_Inside
  KurodaHome/KurodaHome_FillBureau.fbx KurodaFill_Body / KurodaFill_Flap（原点 = 下の端の蝶番）/ KurodaFill_Inside
  Fill/Fill_<Guide|DiaryPage|Page303|KMemo2>.fbx  部屋に置く黒塗りの紙（原点 = 紙の中心、文字の上 = +Z）
--preview: 開いた形の確認レンダー（EEVEE）を <出力先>/fill_*.png に（既定は一時フォルダ）
"""
import importlib
import math
import os
import sys
import tempfile

import bpy
from mathutils import Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
HQDIR = os.path.join(REPO, "project", "Assets", "Models", "HQ")

sys.dont_write_bytecode = True
if HERE not in sys.path:
    sys.path.insert(0, HERE)
for name in ("hq", "preview", "fill_props"):
    if name in sys.modules:
        importlib.reload(sys.modules[name])
import hq          # noqa: E402
import fill_props  # noqa: E402
from hq import U   # noqa: E402

hq.TEX_DIR = os.path.join(REPO, "GenAssets", "fill", "tex")


def mats():
    M = {}
    tex = lambda n, f, rough=0.85: hq.mat(n, (1, 1, 1), rough, tex=f)  # noqa: E731
    M["guide"] = tex("FIL_Guide", "guide.png")
    M["diary"] = tex("FIL_DiaryPage", "diarypage.png")
    M["page303"] = tex("FIL_Page303", "page303.png")
    M["kmemo2"] = tex("FIL_KMemo2", "kmemo2.png")
    M["visitlog"] = tex("FIL_VisitLog", "visitlog.png")
    M["env_saeki"] = tex("FIL_EnvSaeki", "env_saeki.png")
    M["env_ward"] = tex("FIL_EnvWard", "env_ward.png")
    M["env_kuroda"] = tex("FIL_EnvKuroda", "env_kuroda.png")
    M["envbody"] = hq.mat("FIL_Envelope", (0.92, 0.88, 0.78), 0.85)
    M["binders"] = tex("FIL_Binders", "binders.png", 0.5)
    M["names"] = tex("FIL_NameCards", "namecards.png", 0.6)
    # 研究所
    M["steel"] = hq.mat("LAB_SteelGrey", (0.55, 0.57, 0.6), 0.45, 0.6)
    M["lblack"] = hq.mat("LAB_BlackPlastic", (0.04, 0.04, 0.045), 0.5)
    M["glass"] = hq.mat("LAB_CabinetGlass", (0.85, 0.9, 0.92), 0.05, alpha=0.2)
    M["alu"] = hq.mat("LAB_Aluminum", (0.75, 0.76, 0.78), 0.3, 1.0)
    M["board"] = hq.mat("INS_Board", (0.46, 0.33, 0.22), 0.6)
    # 病棟
    M["ivory"] = hq.mat("WRD_Ivory", (0.88, 0.86, 0.8), 0.45, 0.3)
    M["wblack"] = hq.mat("WRD_Black", (0.03, 0.03, 0.03), 0.5)
    M["chrome"] = hq.mat("WRD_Chrome", (0.8, 0.8, 0.82), 0.15, 1.0)
    M["wpaper"] = hq.mat("WRD_PaperPlain", (0.95, 0.95, 0.93), 0.9)
    M["towel"] = hq.mat("WRD_Towel", (0.85, 0.9, 0.95), 0.95)
    # 黒田の自宅
    M["kwooddark"] = hq.mat("KUR_WoodDark", (0.28, 0.17, 0.1), 0.45)
    M["kwood"] = hq.mat("KUR_Wood", (0.55, 0.38, 0.24), 0.45)
    M["brass"] = hq.mat("SAE_Brass", (0.75, 0.58, 0.32), 0.3, 1.0)
    M["leather"] = hq.mat("KUR_NotebookBrown", (0.3, 0.18, 0.1), 0.6)
    M["kblack"] = hq.mat("KUR_Black", (0.03, 0.03, 0.03), 0.5)
    M["kpaper"] = hq.mat("KUR_PaperPlain", (0.94, 0.93, 0.9), 0.9)
    return M


def export(objs, rel):
    for o in objs:
        pass   # 原点は fill_props が決めた位置のまま（扉・ふたは蝶番）
    return hq.export(objs, os.path.join(HQDIR, rel))


def preview(out_dir, sets):
    """開いた形で並べて撮る（扉・ふたを開く角度は Unity の FillContainer と同じ）"""
    import preview as pv
    pv.lights([("Key", (1.5, 2.6, 2.0), (1.0, 0.95, 0.88), 220, 0.6),
               ("Fill", (-2.0, 2.0, 1.5), (0.8, 0.85, 1.0), 120, 1.0)])
    for name, objs, cam, look in sets:
        for o in bpy.data.objects:
            if o.type == "MESH":
                o.hide_render = o not in objs
        pv.shot(os.path.join(out_dir, f"fill_{name}.png"), cam, look, lens=30)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    want_preview = None
    for a in argv:
        if a.startswith("--preview"):
            want_preview = a.split("=", 1)[1] if "=" in a else tempfile.gettempdir()
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    M = mats()
    sizes = {}
    lab = fill_props.lab_cabinet(M)
    sizes["Lab_FillCabinet"] = export(lab, "Lab/Lab_FillCabinet.fbx")
    ward = fill_props.ward_lockers(M)
    sizes["Ward_FillLockers"] = export(ward, "Ward/Ward_FillLockers.fbx")
    kur = fill_props.kuroda_bureau(M)
    sizes["KurodaHome_FillBureau"] = export(kur, "KurodaHome/KurodaHome_FillBureau.fbx")
    papers = {}
    for name, fn in (("Guide", fill_props.paper_guide), ("DiaryPage", fill_props.paper_diary),
                     ("Page303", fill_props.paper_page303), ("KMemo2", fill_props.paper_kmemo2)):
        objs = fn(M)
        papers[name] = objs
        sizes[f"Fill_{name}"] = export(objs, f"Fill/Fill_{name}.fbx")
    print(f"[fill] exported {len(sizes)} models, {sum(sizes.values()) // 1024} KB")
    for k, v in sorted(sizes.items()):
        print(f"[fill]   {k}: {v // 1024} KB")

    if want_preview:
        # 開いた形（Unity の FillContainer.OpenEuler と同じ角度。Blender では Unity の軸の向きが変わる）
        lab[1].rotation_euler = (0, 0, math.radians(100))            # Unity y = -100
        ward[1].rotation_euler = (0, 0, math.radians(100))
        kur[1].rotation_euler = (-math.radians(90 + fill_props.BU_LEAN), 0, 0)
        # 並べて撮る（入れ物ごとに少し離す）
        for o in lab: o.location.x += 0.0
        sets = [
            ("lab", lab, (1.4, 1.5, 2.2), (0.0, 1.0, 0.0)),
            ("ward", ward, (1.2, 1.5, 2.0), (0.0, 1.1, 0.0)),
            ("kuroda", kur, (1.0, 1.3, 1.5), (0.0, 0.75, 0.0)),
            ("papers", [o for v in papers.values() for o in v], (0.0, 0.55, -0.05), (0.0, 0.0, 0.0)),
        ]
        # 紙は横に並べる
        x = -0.36
        for v in papers.values():
            for o in v:
                o.location = U(x, 0, 0)
            x += 0.24
        preview(want_preview, sets)
        print("[fill] preview ->", want_preview)


if __name__ == "__main__":
    main()
