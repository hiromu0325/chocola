"""
MAIN CORE ROOM（core_main）の品質重視モデルを作り直して Unity に書き出す。

    blender --background --factory-startup --python GenAssets/blender/scripts/hq/build_core_main.py

先にテクスチャ: python GenAssets/core_main/make_textures.py
出力: project/Assets/Models/HQ/CoreMain/CoreMain_<部品>.fbx（部品ごとに1メッシュにまとめる）
"""
import importlib
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
OUT = os.path.join(REPO, "project", "Assets", "Models", "HQ", "CoreMain")

sys.dont_write_bytecode = True
if HERE not in sys.path:
    sys.path.insert(0, HERE)
for name in ("hq", "train_room", "dim_room", "study_room", "lab_room", "core_ante_room", "core_main_room"):
    if name in sys.modules:
        importlib.reload(sys.modules[name])
import hq          # noqa: E402
import train_room  # noqa: E402

import core_main_room  # noqa: E402

hq.TEX_DIR = os.path.join(REPO, "GenAssets", "core_main", "tex")


def main():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    M = core_main_room.mats()
    sizes = {}
    for name, fn in core_main_room.PIECES.items():
        objs = fn(M)
        for o in objs:
            o.location = (0, 0, 0)
            o.rotation_euler = (0, 0, 0)
        # 部品ごとに1メッシュ（材質ごとのサブメッシュ）にまとめて、Unity の描画の呼び出しを減らす
        one = hq.join(objs, f"CoreMain_{name}")
        sizes[name] = hq.export([one], os.path.join(OUT, f"CoreMain_{name}.fbx"))
    print(f"[core_main] exported {len(sizes)} models, {sum(sizes.values()) // 1024} KB")
    for k, v in sorted(sizes.items()):
        print(f"[core_main]   {k}: {v // 1024} KB")


if __name__ == "__main__":
    main()
