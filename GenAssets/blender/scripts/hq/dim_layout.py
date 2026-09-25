"""
薄暗い部屋の配置（ビルダーの座標と同じ。Unity座標・yaw度）。
プレビュー撮影と、Unity側の配置の確認用。FBXは各パーツを原点のまま書き出す。
"""
import math

import bpy

from hq import U

HW, HD = 3.0, 3.75

# name: (Unityの位置, Unityのyaw)
LAYOUT = {
    "Shell": ((0, 0, 0), 0),
    "Bed": ((-HW + 1.2, 0, -1.0), 0),
    "Desk": ((HW - 1.4, 0, 0.6), 0),
    "Lamp": ((HW - 1.4 + 0.6, 0.75, 0.6 + 0.25), 0),
    "Terminal": ((-HW + 0.9, 0, HD - 1.0), -45),
    "DollShelf": ((HW - 0.225, 0, 2.2), 0),
    "Nightstand": ((-HW + 0.36, 0, -1.8), 0),
    "Chest": ((-2.2, 0, -HD + 0.075 + 0.235), 0),
    "Wardrobe": ((HW - 0.075 - 0.315, 0, -2.4), -90),
    "Chair": ((HW - 1.5, 0, 0.02), 8),
    "Newspaper": ((HW - 1.4 - 0.28, 0.76, 0.6), 0),
    "Flashlight": ((HW - 1.4 + 0.15, 0.79, 0.5), 0),
    "Notebook": ((HW - 1.4 + 0.42, 0.785, 0.78), 0),
}
DOLLS = [((HW - 0.225, 1.175, 2.2 - 0.56 + i * 0.28), -90) for i in range(5)]


def place(objs, pos, yaw):
    for o in objs:
        o.location = U(*pos)
        o.rotation_euler = (0, 0, math.radians(-yaw))


def reset(objs):
    for o in objs:
        o.location = (0, 0, 0)
        o.rotation_euler = (0, 0, 0)
