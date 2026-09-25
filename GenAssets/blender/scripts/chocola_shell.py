import bpy, math
from mathutils import Vector
from chocola_kit import Kit, U, mat

FI = 0.075          # 壁厚の半分（内面の位置）
DOOR_HALF = 0.55    # 扉開口の半幅（Unity側 DoorOpenW=1.1）
DOOR_H = 2.1

def base(k, w, d, h, wall, floor, ceil):
    """ビルダーと同じ寸法の床・天井・壁（南北は中央に扉開口）"""
    hw, hd = w/2, d/2
    k.box(0, -0.06, 0, w, 0.12, d, floor)
    k.box(0, h+0.06, 0, w, 0.12, d, ceil)
    for zs in (1, -1):
        z = hd*zs; seg = hw - DOOR_HALF; cx = DOOR_HALF + seg/2
        k.box(-cx, h/2, z, seg, h, 0.15, wall); k.box(cx, h/2, z, seg, h, 0.15, wall)
        k.box(0, (DOOR_H+h)/2, z, 1.1, h-DOOR_H, 0.15, wall)
    k.box(hw, h/2, 0, 0.15, h, d, wall); k.box(-hw, h/2, 0, 0.15, h, d, wall)

def patch(k, hw, hd, wall, s0, s1, y0, y1, dep, m):
    """壁の内面に貼る板。s=壁に沿った座標（南北はx、東西はz）"""
    if s1 - s0 < 0.005 or y1 - y0 < 0.001: return
    if wall == "E": k.span(hw-FI-dep, hw-FI, y0, y1, s0, s1, m)
    elif wall == "W": k.span(-hw+FI, -hw+FI+dep, y0, y1, s0, s1, m)
    elif wall == "N": k.span(s0, s1, y0, y1, hd-FI-dep, hd-FI, m)
    else: k.span(s0, s1, y0, y1, -hd+FI, -hd+FI+dep, m)

def wall_len(hw, hd, wall):
    return (-hw+FI, hw-FI) if wall in "NS" else (-hd+FI, hd-FI)

def strips(k, hw, hd, y0, y1, dep, m, clear, walls="NSEW", zlim=None):
    """四周の帯（巾木・腰壁・回り縁など）。南北は扉まわり(|x|<clear)を避ける"""
    for wl in walls:
        s0, s1 = wall_len(hw, hd, wl)
        if wl in "EW" and zlim: s0, s1 = max(s0, zlim[0]), min(s1, zlim[1])
        if wl in "NS" and y0 < DOOR_H + 0.16 and clear > 0:
            patch(k, hw, hd, wl, s0, -clear, y0, y1, dep, m)
            patch(k, hw, hd, wl, clear, s1, y0, y1, dep, m)
        else:
            patch(k, hw, hd, wl, s0, s1, y0, y1, dep, m)

def repeat(k, hw, hd, step, y0, y1, width, dep, m, clear, walls="NSEW", zlim=None, offset=0.0):
    """壁に沿って等間隔に縦材（柱型・目地・框）を並べる"""
    for wl in walls:
        s0, s1 = wall_len(hw, hd, wl)
        if wl in "EW" and zlim: s0, s1 = max(s0, zlim[0]), min(s1, zlim[1])
        s = s0 + offset + step
        while s < s1 - step*0.3:
            if not (wl in "NS" and abs(s) < clear + width and y0 < DOOR_H + 0.16):
                patch(k, hw, hd, wl, s - width/2, s + width/2, y0, y1, dep, m)
            s += step

def casings(k, hd, cw, dep, m, head=None, liner=True):
    """扉開口の室内側の枠（両脇と上）＋開口の見付け"""
    head = cw if head is None else head
    for zs in (1, -1):
        zin = zs*(hd-FI); zout = zin - zs*dep; za, zb = sorted((zin, zout))
        k.span(DOOR_HALF, DOOR_HALF+cw, 0, DOOR_H+head, za, zb, m)
        k.span(-DOOR_HALF-cw, -DOOR_HALF, 0, DOOR_H+head, za, zb, m)
        k.span(-DOOR_HALF-cw, DOOR_HALF+cw, DOOR_H, DOOR_H+head, za, zb, m)
        if liner:
            # 縦枠は扉板の端(±0.46)まで。開口1.1に対して扉板は0.92なので、
            # 薄い見付けだけだと扉の両脇に壁の向こうが透ける隙間ができる（コア扉は片側0.47なので1cm重なる）
            z1, z2 = sorted((zs*(hd+FI), zs*(hd-FI)))
            k.span(0.46, DOOR_HALF, 0, DOOR_H, z1, z2, m)
            k.span(-DOOR_HALF, -0.46, 0, DOOR_H, z1, z2, m)
            k.span(-DOOR_HALF, DOOR_HALF, DOOR_H-0.02, DOOR_H, z1, z2, m)

def ceiling_grid(k, hw, hd, h, step, m, wid=0.024, thick=0.014):
    x = -hw + step
    while x < hw - 0.1:
        k.span(x-wid/2, x+wid/2, h-thick, h, -hd+FI, hd-FI, m); x += step
    z = -hd + step
    while z < hd - 0.1:
        k.span(-hw+FI, hw-FI, h-thick, h, z-wid/2, z+wid/2, m); z += step

def crown(k, hw, hd, h, m, walls="NSEW", zlim=None, big=False):
    a, b = (0.08, 0.05) if big else (0.06, 0.035)
    strips(k, hw, hd, h-a, h, 0.018, m, 0, walls, zlim)
    strips(k, hw, hd, h-b, h, 0.04 if big else 0.032, m, 0, walls, zlim)

# ============================== 系統ごとの造作 ==============================

def facility(k, rid, w, d, h):
    hw, hd = w/2, d/2
    base(k, w, d, h, "LP_FacilityWall", "LP_Linoleum", "LP_FacilityCeiling")
    clear = DOOR_HALF + 0.08
    casings(k, hd, 0.08, 0.03, "LP_FacilityTrim")
    strips(k, hw, hd, 0.0, 0.1, 0.02, "LP_ShellCove", clear)                 # ゴムの巾木
    strips(k, hw, hd, 0.1, 0.95, 0.012, "LP_ShellWainscot", clear)           # 腰壁の保護パネル
    strips(k, hw, hd, 0.95, 1.05, 0.045, "LP_ShellRail", clear)              # 手すり（ストレッチャー当て）
    strips(k, hw, hd, h-0.05, h, 0.02, "LP_FacilityTrim", 0)                 # 見切り
    for sx in (1, -1):                                                       # 出隅の保護（四隅）
        for sz in (1, -1):
            k.span(sx*(hw-FI)-0.03, sx*(hw-FI)+0.03, 0.1, 1.6, sz*(hd-FI)-0.03, sz*(hd-FI)+0.03, "LP_ShellRail")
    ceiling_grid(k, hw, hd, h, 0.6, "LP_ShellGrid")                           # システム天井
    ins, wid = 0.35, 0.1                                                     # 床の縁取り
    k.span(hw-ins-wid, hw-ins, 0, 0.006, -hd+ins, hd-ins, "LP_FloorSeam")
    k.span(-hw+ins, -hw+ins+wid, 0, 0.006, -hd+ins, hd-ins, "LP_FloorSeam")
    k.span(-hw+ins, hw-ins, 0, 0.006, hd-ins-wid, hd-ins, "LP_FloorSeam")
    k.span(-hw+ins, hw-ins, 0, 0.006, -hd+ins, -hd+ins+wid, "LP_FloorSeam")

def core(k, rid, w, d, h):
    hw, hd = w/2, d/2
    base(k, w, d, h, "LP_Concrete", "LP_MetalFloor", "LP_CoreCeiling")
    clear = DOOR_HALF + 0.14
    casings(k, hd, 0.14, 0.06, "LP_CoreTrim", head=0.16)                     # 厚い鉄の扉枠
    strips(k, hw, hd, 0.0, 0.18, 0.03, "LP_CoreTrim", clear)                 # 鋼板の幅木
    repeat(k, hw, hd, 1.2, 0.18, h, 0.014, 0.006, "LP_ShellSeam", clear)     # 打ち放しの縦目地
    for yy in (1.8, 3.6):
        if yy < h - 0.3: strips(k, hw, hd, yy-0.007, yy+0.007, 0.006, "LP_ShellSeam", clear)
    for wl in "NSEW":                                                        # セパ穴
        s0, s1 = wall_len(hw, hd, wl)
        s = s0 + 0.3
        while s < s1 - 0.25:
            yy = 0.45
            while yy < h - 0.3:
                if not (wl in "NS" and abs(s) < clear + 0.1 and yy < DOOR_H + 0.3):
                    patch(k, hw, hd, wl, s-0.018, s+0.018, yy-0.018, yy+0.018, 0.007, "LP_ShellSeam")
                yy += 0.9
            s += 0.6
    x = -hw + 1.0                                                            # 鋼板の継ぎ目とボルト
    while x < hw - 0.3:
        k.span(x-0.006, x+0.006, 0, 0.005, -hd+FI, hd-FI, "LP_ShellSeam"); x += 1.0
    z = -hd + 1.0
    while z < hd - 0.3:
        k.span(-hw+FI, hw-FI, 0, 0.005, z-0.006, z+0.006, "LP_ShellSeam"); z += 1.0
    x = -hw + 1.0
    while x < hw - 0.3:
        z = -hd + 1.0
        while z < hd - 0.3:
            for dx, dz in ((0.06, 0.06), (-0.06, 0.06), (0.06, -0.06), (-0.06, -0.06)):
                k.span(x+dx-0.015, x+dx+0.015, 0, 0.012, z+dz-0.015, z+dz+0.015, "LP_ShellBolt")
            z += 1.0
        x += 1.0
    for sx in (1, -1):                                                       # 天井の鉄骨梁（照明は中央なので避けて両側に）
        bx = sx * w * 0.3
        k.span(bx-0.1, bx+0.1, h-0.3, h, -hd+FI, hd-FI, "LP_CoreTrim")
        k.span(bx-0.16, bx+0.16, h-0.32, h-0.3, -hd+FI, hd-FI, "LP_CoreTrim")   # 下フランジ
    for zs in (1, -1):
        k.span(-hw+FI, hw-FI, h-0.3, h, zs*(hd-FI)-0.2*zs-0.1, zs*(hd-FI)-0.2*zs+0.1, "LP_CoreTrim")

def home(k, rid, w, d, h):
    hw, hd = w/2, d/2
    base(k, w, d, h, "LP_HomeWall", "LP_HomeWood", "LP_HomeCeiling")
    clear = DOOR_HALF + 0.09
    casings(k, hd, 0.09, 0.022, "LP_HomeTrim", head=0.11)
    walls, zlim = "NSEW", None
    if rid == "mizuno_apart":          # 奥（z>1.0）は病室に滲む。住宅の造作は手前だけ
        walls, zlim = "SEW", (-hd+FI, 1.0 - 0.06)
    strips(k, hw, hd, 0.0, 0.09, 0.016, "LP_HomeTrim", clear, walls, zlim)          # 巾木
    strips(k, hw, hd, 0.09, 0.1, 0.008, "LP_HomeTrim", clear, walls, zlim)          # 巾木の上端
    if rid != "mizuno_apart": crown(k, hw, hd, h, "LP_HomeTrim")                    # アパートは回り縁なし
    if rid == "saeki_home":            # 腰板（研究者の落ち着いた家）
        strips(k, hw, hd, 0.1, 0.85, 0.01, "LP_HomeTrim", clear)
        strips(k, hw, hd, 0.85, 0.9, 0.03, "LP_HomeTrim", clear)
        repeat(k, hw, hd, 0.6, 0.1, 0.85, 0.035, 0.018, "LP_HomeTrim", clear)
    elif rid == "kuroda_home":         # 長押（和の家）
        strips(k, hw, hd, 1.85, 1.95, 0.03, "LP_HomeTrim", clear)
        strips(k, hw, hd, 0.86, 0.9, 0.012, "LP_HomeTrim", clear)
    elif rid == "son_room":            # 子供部屋の色帯
        strips(k, hw, hd, 1.05, 1.2, 0.006, "LP_KidBand", clear)
        strips(k, hw, hd, 1.2, 1.22, 0.012, "LP_HomeTrim", clear)

def study(k, rid, w, d, h):
    """所長の書斎：濃い木の腰板（框と鏡板）と格天井"""
    hw, hd = w/2, d/2
    base(k, w, d, h, "LP_HomeWall", "LP_HomeWood", "LP_HomeCeiling")
    clear = DOOR_HALF + 0.1
    casings(k, hd, 0.1, 0.025, "LP_StudyPanelDark", head=0.13)
    strips(k, hw, hd, 0.14, 1.05, 0.012, "LP_StudyPanel", clear)                  # 鏡板
    strips(k, hw, hd, 0.0, 0.14, 0.03, "LP_StudyPanelDark", clear)                # 下框（巾木）
    strips(k, hw, hd, 1.02, 1.1, 0.035, "LP_StudyPanelDark", clear)               # 笠木
    repeat(k, hw, hd, 0.7, 0.14, 1.02, 0.06, 0.024, "LP_StudyPanelDark", clear)   # 束
    crown(k, hw, hd, h, "LP_StudyPanelDark", big=True)
    step = 1.4                                                                    # 格天井
    x = -hw + step
    while x < hw - 0.3:
        k.span(x-0.07, x+0.07, h-0.12, h, -hd+FI, hd-FI, "LP_StudyPanelDark"); x += step
    z = -hd + step
    while z < hd - 0.3:
        k.span(-hw+FI, hw-FI, h-0.12, h, z-0.07, z+0.07, "LP_StudyPanelDark"); z += step

def dim(k, rid, w, d, h):
    """薄暗い部屋：古い漆喰の寝室。雨染み"""
    hw, hd = w/2, d/2
    base(k, w, d, h, "LP_RoomWall", "LP_RoomFloor", "LP_Ceiling")
    clear = DOOR_HALF + 0.08
    casings(k, hd, 0.08, 0.02, "LP_DoorFrame", head=0.1)
    strips(k, hw, hd, 0.0, 0.08, 0.015, "LP_DoorFrame", clear)
    strips(k, hw, hd, h-0.05, h, 0.02, "LP_DoorFrame", 0)
    for wl, s, y, sw, sh in (("E", 1.6, h-0.35, 0.9, 0.5), ("E", 2.1, h-0.55, 0.4, 0.4), ("W", -2.2, h-0.3, 1.2, 0.45),
                             ("W", -1.4, h-0.6, 0.5, 0.35), ("N", 1.8, h-0.4, 0.7, 0.55), ("S", -2.0, 1.0, 0.5, 0.9)):
        patch(k, hw, hd, wl, s-sw/2, s+sw/2, y-sh/2, y+sh/2, 0.004, "LP_ShellStain")
    k.span(1.2, 2.4, h-0.004, h, 1.5, 2.8, "LP_ShellStain")                        # 天井の雨染み

def lab(k, rid, w, d, h):
    """研究所応接室：オフィスのシステム天井と巾木"""
    hw, hd = w/2, d/2
    base(k, w, d, h, "LP_RoomWall", "LP_RoomFloor", "LP_Ceiling")
    clear = DOOR_HALF + 0.08
    casings(k, hd, 0.08, 0.03, "LP_FacilityTrim")
    strips(k, hw, hd, 0.0, 0.08, 0.018, "LP_ShellCove", clear)
    strips(k, hw, hd, 0.95, 1.0, 0.015, "LP_HomeTrim", clear)
    strips(k, hw, hd, h-0.04, h, 0.02, "LP_FacilityTrim", 0)
    ceiling_grid(k, hw, hd, h, 0.6, "LP_ShellGrid")

def train(k, rid, w, d, h):
    """通勤電車：天井の面取り（肩の曲面）と両側の照明帯"""
    hw, hd = w/2, d/2
    base(k, w, d, h, "LP_TrainWall", "LP_TrainFloor", "LP_TrainCeiling")
    casings(k, hd, 0.07, 0.03, "LP_TrainChrome")
    c = 0.42
    for sx in (1, -1):
        xw = sx*(hw-FI); xi = xw - sx*c
        k.prism_z([(xw, h-c), (xw, h), (xi, h)] if sx > 0 else [(xw, h), (xw, h-c), (xi, h)], -hd+FI, hd-FI, "LP_TrainCeiling")
        xm = xw - sx*c*0.5; ym = h - c*0.5                                          # 面取りの中ほどに照明帯
        k.box(xm - sx*0.03, ym - 0.03, 0, 0.1, 0.03, d - 0.6, "LP_TrainLightStrip")
    strips(k, hw, hd, 0.0, 0.06, 0.01, "LP_TrainChrome", DOOR_HALF+0.07)

STYLES = {"analysis": facility, "ward": facility, "data_room": facility,
          "core_ante": core, "system_room": core, "core_main": core,
          "saeki_home": home, "kuroda_home": home, "mizuno_apart": home, "son_room": home,
          "study": study, "dim": dim, "lab": lab, "train": train}

ROOMS = [("dim",6.0,7.5,2.6),("train",3.0,18.0,3.0),("lab",13.0,10.0,3.2),("study",5.5,7.0,2.9),
         ("analysis",8.0,9.0,3.0),("saeki_home",7.0,8.0,2.6),("ward",9.0,12.0,3.2),("core_ante",7.0,8.0,3.4),
         ("mizuno_apart",5.0,9.0,2.5),("data_room",9.0,10.0,3.0),("system_room",10.0,10.0,4.0),
         ("kuroda_home",7.0,8.0,2.6),("core_main",12.0,12.0,5.0),("son_room",4.5,5.0,2.4)]

def build_shell(rid):
    w, d, h = next((r[1], r[2], r[3]) for r in ROOMS if r[0] == rid)
    k = Kit("Shell_" + rid)
    STYLES[rid](k, rid, w, d, h)
    tris = k.tris()
    return k.build(), tris
