"""
薄暗い部屋（dim）の品質重視モデルを作り直して Unity に書き出す。

    blender --background --factory-startup --python GenAssets/blender/scripts/hq/build_dim.py

先にテクスチャ: python GenAssets/dim/make_textures.py
出力: project/Assets/Models/HQ/Dim/Dim_<部品>.fbx（各部品はビルダーのユニット原点が原点）
"""
import importlib
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
OUT = os.path.join(REPO, "project", "Assets", "Models", "HQ", "Dim")

sys.dont_write_bytecode = True
if HERE not in sys.path:
    sys.path.insert(0, HERE)
for name in ("hq", "dim_room"):
    if name in sys.modules:
        importlib.reload(sys.modules[name])
import hq         # noqa: E402
import dim_room   # noqa: E402

hq.TEX_DIR = os.path.join(REPO, "GenAssets", "dim", "tex")


def main():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    M = dim_room.mats()
    groups = {}
    for name, fn in dim_room.PIECES.items():
        if name == "Bed":
            continue
        groups[name] = fn(M)
    groups["Bed"] = dim_room.bed(M, with_quilt=True)
    # 外殻は1つのメッシュにまとめる（描画の呼び出しを減らす）
    groups["Shell"] = [hq.join(groups["Shell"], "Dim_Shell")]
    sizes = {}
    for name, objs in groups.items():
        for o in objs:
            o.location = (0, 0, 0)
            o.rotation_euler = (0, 0, 0)
        sizes[name] = hq.export(objs, os.path.join(OUT, f"Dim_{name}.fbx"))
    print(f"[dim] exported {len(sizes)} models, {sum(sizes.values()) // 1024} KB")
    for k, v in sorted(sizes.items()):
        print(f"[dim]   {k}: {v // 1024} KB")


if __name__ == "__main__":
    main()
