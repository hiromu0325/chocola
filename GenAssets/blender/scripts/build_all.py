"""
回廊・14部屋の外殻と、扉・ブレイカーをBlenderで作り直してUnityへ書き出す。

使い方（Blender 5.1）:
  blender --background --python GenAssets/blender/scripts/build_all.py
  またはBlenderのテキストエディタでこのファイルを開いて実行

出力:
  project/Assets/Models/Rooms/Shell_<部屋id>.fbx, Shell_corridor.fbx
  project/Assets/Models/Props/CorridorDoor.fbx, BreakerBox.fbx, RoomDoor_<系統>.fbx
  GenAssets/blender/rooms_and_props.blend（作業用の保存）

座標はUnity側（LoopPrototypeBuilder）の数値をそのまま使う（chocola_kit.U で変換）。
材質名はUnityの LP_* と同じにしてあり、Unity側で既存の材質に差し替わる。
部屋の寸法を変えたら chocola_shell.ROOMS と LoopPrototypeBuilder.RoomDefs を揃えること。
"""
import os
import sys
import importlib

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
MODELS = os.path.join(REPO, "project", "Assets", "Models")

sys.dont_write_bytecode = True     # scripts/ に __pycache__ を作らない
if HERE not in sys.path:
    sys.path.insert(0, HERE)
for name in ("chocola_kit", "chocola_shell", "chocola_parts", "chocola_render"):
    if name in sys.modules:
        importlib.reload(sys.modules[name])
import chocola_kit as kit      # noqa: E402
import chocola_shell as shell  # noqa: E402
import chocola_parts as parts  # noqa: E402


def main():
    kit.clear_scene()
    names = []
    for rid, *_ in shell.ROOMS:
        shell.build_shell(rid)
        names.append(("Rooms", "Shell_" + rid))
    for fn in (parts.corridor, parts.corridor_door, parts.breaker):
        k = fn()
        k.build()
        names.append(("Rooms" if k.name == "Shell_corridor" else "Props", k.name))
    for style in ("facility", "core", "home", "plain", "train"):
        k = parts.room_door(style)
        k.build()
        names.append(("Props", k.name))

    sizes = {}
    for folder, n in names:
        o = bpy.data.objects[n]
        o.location = (0, 0, 0)
        o.rotation_euler = (0, 0, 0)
        sizes[n] = kit.export([n], os.path.join(MODELS, folder, n + ".fbx"))
    blend = os.path.join(REPO, "GenAssets", "blender", "rooms_and_props.blend")
    os.makedirs(os.path.dirname(blend), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=blend, copy=True)
    print(f"[chocola] exported {len(sizes)} models, {sum(sizes.values()) // 1024} KB")
    return sizes


if __name__ == "__main__":
    main()
