"""
全部屋の配電盤（Breaker）とレバー（Lever）だけを作り直して Unity に書き出す。
配電盤とレバーは部屋ごとに材質を変えた共通の形（train_room.breaker / train_room.lever）なので、
形を変えた時はこれで一括で書き出す（部屋の他の部品は触らない）。

    blender --background --factory-startup --python GenAssets/blender/scripts/hq/build_breakers.py
"""
import importlib
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
HQ = os.path.join(REPO, "project", "Assets", "Models", "HQ")

sys.dont_write_bytecode = True
if HERE not in sys.path:
    sys.path.insert(0, HERE)

# (部屋のモジュール, 書き出し名 = HqModel の部屋名)。最初の部屋（dim）には配電盤が無い
ROOMS = [
    ("train_room", "Train"), ("lab_room", "Lab"), ("study_room", "Study"), ("analysis_room", "Analysis"),
    ("saeki_room", "SaekiHome"), ("ward_room", "Ward"), ("core_ante_room", "CoreAnte"),
    ("mizuno_room", "MizunoApart"), ("data_room", "DataRoom"), ("system_room", "SystemRoom"),
    ("kuroda_room", "KurodaHome"), ("core_main_room", "CoreMain"), ("son_room", "SonRoom"),
]


def main():
    for name in ["hq", "train_room", "dim_room", "lab_room", "core_ante_room", "study_room"] + [m for m, _ in ROOMS]:
        if name in sys.modules:
            importlib.reload(sys.modules[name])
    import hq
    total = 0
    for mod_name, pascal in ROOMS:
        mod = importlib.import_module(mod_name)
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        M = mod.mats()
        for part in ("Breaker", "Lever"):
            objs = mod.PIECES[part](M)
            for o in objs:
                o.location = (0, 0, 0)
                o.rotation_euler = (0, 0, 0)
            one = hq.join(objs, f"{pascal}_{part}")
            total += hq.export([one], os.path.join(HQ, pascal, f"{pascal}_{part}.fbx"))
        print(f"[breakers] {pascal}")
    print(f"[breakers] exported {len(ROOMS) * 2} models, {total // 1024} KB")


if __name__ == "__main__":
    main()
