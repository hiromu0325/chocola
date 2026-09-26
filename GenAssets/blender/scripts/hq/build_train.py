"""
電車車内（train）の品質重視モデルを作り直して Unity に書き出す。

    blender --background --factory-startup --python GenAssets/blender/scripts/hq/build_train.py

先にテクスチャ: python GenAssets/train/make_textures.py
出力: project/Assets/Models/HQ/Train/Train_<部品>.fbx（部品ごとに1メッシュにまとめる）
"""
import importlib
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
OUT = os.path.join(REPO, "project", "Assets", "Models", "HQ", "Train")

sys.dont_write_bytecode = True
if HERE not in sys.path:
    sys.path.insert(0, HERE)
for name in ("hq", "train_room"):
    if name in sys.modules:
        importlib.reload(sys.modules[name])
import hq          # noqa: E402
import train_room  # noqa: E402

hq.TEX_DIR = os.path.join(REPO, "GenAssets", "train", "tex")


def main():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    M = train_room.mats()
    sizes = {}
    for name, fn in train_room.PIECES.items():
        objs = fn(M)
        for o in objs:
            o.location = (0, 0, 0)
            o.rotation_euler = (0, 0, 0)
        # 部品ごとに1メッシュ（材質ごとのサブメッシュ）にまとめて、Unity の描画の呼び出しを減らす
        one = hq.join(objs, f"Train_{name}")
        sizes[name] = hq.export([one], os.path.join(OUT, f"Train_{name}.fbx"))
    print(f"[train] exported {len(sizes)} models, {sum(sizes.values()) // 1024} KB")
    for k, v in sorted(sizes.items()):
        print(f"[train]   {k}: {v // 1024} KB")


if __name__ == "__main__":
    main()
