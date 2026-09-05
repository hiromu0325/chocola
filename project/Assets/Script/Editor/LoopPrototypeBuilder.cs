using System.Collections.Generic;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

namespace EscapeProto
{
    /// <summary>
    /// ループ回廊プロトタイプのシーンビルダー。
    /// ・ロの字の正方形回廊：外周は窓（出られない）、内周は各辺10枚の扉。
    ///   壁と天井は白、床は板張り
    /// ・部屋は回廊と物理的に繋がらない「仮想部屋」（オフセット位置に配置）。
    ///   暗転中に回廊⇔部屋のモデルを切替えてワープする（RoomTransitionSystem）
    /// ・チュートリアル部屋から開始 → ブレイカーを上げると扉が開き回廊へ
    /// ・ブレイカーサイクル（15分/チュートリアル後のみ30秒）と襲撃者はBreakerSystemが統括
    ///
    /// 部屋の追加・扉割当は RoomDefs を編集して再生成。
    /// </summary>
    public static class LoopPrototypeBuilder
    {
        private const string ScenePath = "Assets/EscapePrototype/LoopPrototype.unity";
        private const string MatDir = "Assets/EscapePrototype/MoodMaterials";

        private const float InnerHalf = LoopCorridorLayout.InnerHalf;   // 7
        private const float OuterHalf = LoopCorridorLayout.OuterHalf;   // 10
        private const float H = LoopCorridorLayout.WallH;               // 3
        private const int DoorsPerSide = LoopCorridorLayout.DoorsPerSide;

        // 各部屋は回廊から離れた「専用の区画」に個別配置する（重ならないよう120m間隔）
        private static readonly Vector3 RoomOrigin = new Vector3(60f, 0f, 0f);
        private const float RoomSpacing = 120f;

        /// <summary>部屋の定義。寸法は部屋の雰囲気に合わせて個別に持つ</summary>
        private struct RoomDef
        {
            public string id, name;
            public int stage, side, slot;
            public float w, d, h;   // 幅・奥行き・天井高
            public RoomDef(string id, string name, int stage, int side, int slot,
                           float w, float d, float h)
            { this.id = id; this.name = name; this.stage = stage; this.side = side;
              this.slot = slot; this.w = w; this.d = d; this.h = h; }
        }

        // 解放順（仮シナリオv2）:
        //   チュートリアル: 薄暗い部屋 → 電車車内 → 研究所応接室
        //   1章 佐伯: 所長の書斎(起) → 脳神経解析室(転) → 佐伯の自宅(結)
        //   2章 水野: 臨床病棟(起) → CORE前室(転) → 水野のアパート(結)
        //   3章 黒田: データ管理室(起) → SYSTEM ROOM(転) → 黒田の自宅(結)
        //   終章: MAIN CORE ROOM → 息子の部屋
        // 出口は自動で「入口の反対側の辺」の同スロットに繋がる
        private static readonly RoomDef[] RoomDefs =
        {
            // 狭く天井が低い寝室。目覚めの部屋＝セーブ・リスポーン地点
            new RoomDef("dim",   "薄暗い部屋",   0, 2, 4,  6.0f,  7.5f, 2.6f),
            // 電車車内。幅は狭く、前後に長い（中吊り広告の下を通れるよう天井は高め）
            new RoomDef("train", "電車車内",     1, 1, 6,  3.0f, 18.0f, 3.0f),
            // 研究所の応接室。5人分の机が入る広い空間
            new RoomDef("lab",   "研究所応接室", 2, 3, 3, 13.0f, 10.0f, 3.2f),
            // 所長の書斎。1章起（招聘状・経過報告・主任の手帳の切れ端）
            new RoomDef("study",        "所長の書斎",         3, 0, 2,  5.5f,  7.0f, 2.9f),
            // 第8研究室。1章転（研究の正体・異常レポート）
            new RoomDef("analysis",     "脳神経解析室",       4, 1, 1,  8.0f,  9.0f, 3.0f),
            // 回廊に滲み出した記憶の部屋。1章結（佐伯という人間）
            new RoomDef("saeki_home",   "佐伯の自宅",         5, 2, 7,  7.0f,  8.0f, 2.6f),
            // 2章起（患者たちの領域・娘のファイル）
            new RoomDef("ward",         "臨床病棟",           6, 0, 5,  9.0f, 12.0f, 3.2f),
            // 第13前室。2章転（BRAIN DATA一覧・停電の間）
            new RoomDef("core_ante",    "CORE前室",           7, 3, 8,  7.0f,  8.0f, 3.4f),
            // 2章結（水野の原点。奥が病室に滲む）
            new RoomDef("mizuno_apart", "水野のアパート",     8, 2, 1,  5.0f,  9.0f, 2.5f),
            // 3章起（記録層・入退室ログ）
            new RoomDef("data_room",    "データ管理室",       9, 1, 4,  9.0f, 10.0f, 3.0f),
            // 第12研究室。3章転（反転の部屋）
            new RoomDef("system_room",  "SYSTEM ROOM",       10, 0, 8, 10.0f, 10.0f, 4.0f),
            // 3章結（黒田という父親）
            new RoomDef("kuroda_home",  "黒田の自宅",        11, 2, 6,  7.0f,  8.0f, 2.6f),
            // 第13研究室。終章（主任のメッセージ）
            new RoomDef("core_main",    "MAIN CORE ROOM",    12, 0, 0, 12.0f, 12.0f, 5.0f),
            // 終章・隠し（誰の記憶でもない、祈りで作られた部屋）
            new RoomDef("son_room",     "息子の部屋",        13, 3, 0,  4.5f,  5.0f, 2.4f),
        };

        [MenuItem("Tools/EscapePrototype/Loop Prototype/シーンを生成")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MeshCache.Clear();
            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder("Assets/EscapePrototype", "MoodMaterials");

            var corridor = BuildCorridor();
            BuildRooms();
            BuildLighting();
            var player = BuildPlayer();
            BuildManagers(player, corridor);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[LoopPrototype] 生成完了: {ScenePath}");
        }

        // ============================== 回廊（ロの字） ==============================

        private static GameObject BuildCorridor()
        {
            var root = new GameObject("Corridor");
            var t = root.transform;

            var white = GetMat("LP_White", new Color(0.92f, 0.92f, 0.93f), 0.15f);
            var whiteCeil = GetMat("LP_Ceiling", new Color(0.96f, 0.96f, 0.97f), 0.1f);
            var wood = WoodFloorMat();
            var doorMat = GetMat("LP_Door", new Color(0.82f, 0.78f, 0.72f), 0.3f);
            var frameMat = GetMat("LP_DoorFrame", new Color(0.55f, 0.5f, 0.45f), 0.25f);

            float o = OuterHalf, i = InnerHalf;
            float midW = (o + i) * 0.5f, w = o - i;   // 回廊幅3

            // 床（板張り）と天井（白）のリング＝4本のストリップ
            foreach (var (cx, cz, sx, sz) in new[]
            {
                (0f, midW, o * 2f, w),    // N
                (0f, -midW, o * 2f, w),   // S
                (midW, 0f, w, i * 2f),    // E
                (-midW, 0f, w, i * 2f),   // W
            })
            {
                Box(t, "Floor", new Vector3(cx, -0.06f, cz), new Vector3(sx, 0.12f, sz), wood);
                Box(t, "Ceiling", new Vector3(cx, H + 0.06f, cz), new Vector3(sx, 0.12f, sz), whiteCeil);
            }

            // 外周壁（白）＋ 窓（ガラスパネル。外には出られない）
            Box(t, "Wall_Out_N", new Vector3(0f, H * 0.5f, o), new Vector3(o * 2f + 0.15f, H, 0.15f), white);
            Box(t, "Wall_Out_S", new Vector3(0f, H * 0.5f, -o), new Vector3(o * 2f + 0.15f, H, 0.15f), white);
            Box(t, "Wall_Out_E", new Vector3(o, H * 0.5f, 0f), new Vector3(0.15f, H, o * 2f), white);
            Box(t, "Wall_Out_W", new Vector3(-o, H * 0.5f, 0f), new Vector3(0.15f, H, o * 2f), white);
            // 四隅の柱：壁の突き合わせ角をCharacterControllerが斜め圧力で
            // すり抜けて落下するのを物理的に防ぐ
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    Box(t, "CornerPost", new Vector3(o * sx, H * 0.5f, o * sz), new Vector3(0.5f, H, 0.5f), white);
            // 窓は廃止（外の情報を見せない閉塞した回廊にする）

            // 内側ブロック（ロの中央の詰まった部分。面が内周壁になる）
            Box(t, "InnerBlock", new Vector3(0f, H * 0.5f, 0f), new Vector3(i * 2f, H, i * 2f), white);

            // 内周の扉 各辺10枚（部屋割当はRoomDefsから。それ以外はダミー）
            var doorMap = BuildDoorMap();
            var doorsRoot = new GameObject("Doors").transform;
            doorsRoot.SetParent(t, false);
            for (int side = 0; side < 4; side++)
                for (int slot = 0; slot < DoorsPerSide; slot++)
                {
                    doorMap.TryGetValue((side, slot), out var assign);
                    BuildCorridorDoor(doorsRoot, side, slot, assign.roomId, assign.exitSide, doorMat, frameMat);
                }

            return root;
        }

        /// <summary>(side,slot) → (roomId, exitSide)。入口はRoomDefs、出口は反対側の辺の同スロット</summary>
        private static Dictionary<(int, int), (string roomId, bool exitSide)> BuildDoorMap()
        {
            var map = new Dictionary<(int, int), (string, bool)>();
            foreach (var d in RoomDefs)
            {
                map[(d.side, d.slot)] = (d.id, false);
                map[((d.side + 2) % 4, d.slot)] = (d.id, true);
            }
            return map;
        }

        private static void BuildCorridorDoor(Transform parent, int side, int slot,
                                              string roomId, bool exitSide, Material door, Material frame)
        {
            Vector3 pos = LoopCorridorLayout.DoorPosition(side, slot);
            float yaw = LoopCorridorLayout.DoorYaw(side);
            var unit = new GameObject($"Door_{side}_{slot}" + (string.IsNullOrEmpty(roomId) ? "" : $"_{roomId}"));
            unit.transform.SetParent(parent, false);
            unit.transform.localPosition = pos;
            unit.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var ut = unit.transform;

            // 内壁からわずかに回廊側へ出す（+Z=回廊側になるようyaw済み）
            Box(ut, "Panel", new Vector3(0f, 1.05f, 0.09f), new Vector3(0.92f, 2.1f, 0.06f), door);
            Box(ut, "Jamb_L", new Vector3(-0.5f, 1.1f, 0.09f), new Vector3(0.08f, 2.2f, 0.1f), frame);
            Box(ut, "Jamb_R", new Vector3(0.5f, 1.1f, 0.09f), new Vector3(0.08f, 2.2f, 0.1f), frame);
            Box(ut, "Lintel", new Vector3(0f, 2.24f, 0.09f), new Vector3(1.08f, 0.09f, 0.1f), frame);
            Box(ut, "Knob", new Vector3(0.32f, 1.02f, 0.14f), new Vector3(0.05f, 0.12f, 0.05f), frame);

            // パネル・ノブ・枠のどこに視線が当たっても反応するようルートに付ける
            var ld = unit.AddComponent<LoopDoor>();
            ld.RoomId = roomId ?? "";
            ld.ExitSide = exitSide;
        }

        // ※窓は仕様変更により廃止（外周は白壁のみ）

        // ============================== 仮想部屋 ==============================

        private static void BuildRooms()
        {
            var root = new GameObject("Rooms").transform;
            for (int i = 0; i < RoomDefs.Length; i++)
                BuildRoom(root, RoomDefs[i], i);
        }

        /// <summary>部屋ごとのアクセント色（重なり事故や迷子をすぐ見分けられるように）</summary>
        private static Color RoomAccent(string id) => id switch
        {
            "dim" => new Color(0.45f, 0.45f, 0.5f),      // 灰青（薄暗い部屋）
            "study" => new Color(0.6f, 0.42f, 0.25f),    // 焦茶（書斎）
            "train" => new Color(0.3f, 0.6f, 0.75f),     // 青緑（電車）
            "lab" => new Color(0.75f, 0.75f, 0.8f),      // 白銀（研究所）
            "analysis" => new Color(0.35f, 0.55f, 0.8f),     // 青（解析室のモニター光）
            "saeki_home" => new Color(0.72f, 0.55f, 0.35f),  // 夕暮れの橙（佐伯の自宅）
            "ward" => new Color(0.55f, 0.75f, 0.65f),        // 薄緑（病棟）
            "core_ante" => new Color(0.75f, 0.3f, 0.28f),    // 非常灯の赤（CORE前室）
            "mizuno_apart" => new Color(0.8f, 0.65f, 0.5f),  // 暖色（アパート）
            "data_room" => new Color(0.4f, 0.45f, 0.6f),     // 青白（データ管理室）
            "system_room" => new Color(0.35f, 0.7f, 0.85f),  // 淡青のパルス（SYSTEM ROOM）
            "kuroda_home" => new Color(0.65f, 0.5f, 0.35f),  // 食卓の灯（黒田の自宅）
            "core_main" => new Color(0.85f, 0.75f, 0.45f),   // 金色（MAIN CORE）
            "son_room" => new Color(0.85f, 0.85f, 0.8f),     // 白い光（息子の部屋）
            _ => Color.gray,
        };

        private static void BuildRoom(Transform parent, RoomDef def, int index)
        {
            var root = new GameObject($"Room_{def.id}");
            root.transform.SetParent(parent, false);
            // 部屋ごとに専用区画へ配置（重ならないよう十分離す）
            root.transform.position = RoomOrigin + new Vector3(index * RoomSpacing, 0f, 0f);
            var t = root.transform;

            Material wall, floor, ceil, doorMat;
            if (def.id == "train")
            {
                // 通勤電車：クリーム色の化粧板、灰色のリノリウム床、赤い車端扉
                wall = GetMat("LP_TrainWall", new Color(0.90f, 0.87f, 0.78f), 0.35f);
                floor = GetMat("LP_TrainFloor", new Color(0.52f, 0.52f, 0.50f), 0.3f);
                ceil = GetMat("LP_TrainCeiling", new Color(0.92f, 0.90f, 0.83f), 0.3f);
                doorMat = GetMat("LP_TrainDoor", new Color(0.62f, 0.10f, 0.10f), 0.45f);
            }
            else if (IsFacility(def.id))
            {
                // 研究施設：白い壁、緑がかったリノリウム、白い天井、灰色の金属扉
                wall = GetMat("LP_FacilityWall", new Color(0.86f, 0.87f, 0.86f), 0.2f);
                floor = GetMat("LP_Linoleum", new Color(0.5f, 0.55f, 0.52f), 0.45f);
                ceil = GetMat("LP_FacilityCeiling", new Color(0.9f, 0.9f, 0.9f), 0.1f);
                doorMat = GetMat("LP_FacilityDoor", new Color(0.55f, 0.57f, 0.6f), 0.4f);
            }
            else if (IsCore(def.id))
            {
                // コア区画：コンクリートの壁、金属の床、暗い天井、鉄扉
                wall = GetMat("LP_Concrete", new Color(0.42f, 0.42f, 0.43f), 0.1f);
                floor = MetalMat("LP_MetalFloor", new Color(0.28f, 0.29f, 0.31f), 0.4f, 0.6f);
                ceil = GetMat("LP_CoreCeiling", new Color(0.3f, 0.3f, 0.32f), 0.1f);
                doorMat = MetalMat("LP_CoreDoor", new Color(0.35f, 0.36f, 0.38f), 0.5f, 0.7f);
            }
            else if (IsHome(def.id))
            {
                // 住宅：クリーム色の壁紙、板張りの床、木の扉
                wall = GetMat("LP_HomeWall", new Color(0.88f, 0.84f, 0.74f), 0.1f);
                floor = HomeFloorMat();
                ceil = GetMat("LP_HomeCeiling", new Color(0.93f, 0.9f, 0.84f), 0.1f);
                doorMat = GetMat("LP_HomeDoor", new Color(0.55f, 0.4f, 0.28f), 0.3f);
            }
            else
            {
                wall = GetMat("LP_RoomWall", new Color(0.78f, 0.78f, 0.76f), 0.15f);
                floor = GetMat("LP_RoomFloor", new Color(0.5f, 0.48f, 0.45f), 0.15f);
                ceil = GetMat("LP_Ceiling", new Color(0.96f, 0.96f, 0.97f), 0.1f);
                doorMat = GetMat("LP_Door", new Color(0.82f, 0.78f, 0.72f), 0.3f);
            }
            var accent = GetMat("LP_Accent_" + def.id, RoomAccent(def.id), 0.25f);

            float hw = def.w * 0.5f, hd = def.d * 0.5f, h = def.h;
            const float DoorOpenW = 1.1f, DoorOpenH = 2.1f;

            Box(t, "Floor", new Vector3(0f, -0.06f, 0f), new Vector3(def.w, 0.12f, def.d), floor);
            Box(t, "Ceiling", new Vector3(0f, h + 0.06f, 0f), new Vector3(def.w, 0.12f, def.d), ceil);

            // 南北の壁は中央に扉開口を空ける（入口＝南／出口＝北。必ず両方に通れる開口がある）
            foreach (float zSign in new[] { 1f, -1f })
            {
                float z = hd * zSign;
                float segW = hw - DoorOpenW * 0.5f;
                if (segW > 0.05f)
                {
                    float cx = DoorOpenW * 0.5f + segW * 0.5f;
                    Box(t, "Wall_Seg", new Vector3(-cx, h * 0.5f, z), new Vector3(segW, h, 0.15f), wall);
                    Box(t, "Wall_Seg", new Vector3(cx, h * 0.5f, z), new Vector3(segW, h, 0.15f), wall);
                }
                if (h > DoorOpenH + 0.05f)
                    Box(t, "Wall_Lintel", new Vector3(0f, (DoorOpenH + h) * 0.5f, z),
                        new Vector3(DoorOpenW, h - DoorOpenH, 0.15f), wall);
            }
            Box(t, "Wall_E", new Vector3(hw, h * 0.5f, 0f), new Vector3(0.15f, h, def.d), wall);
            Box(t, "Wall_W", new Vector3(-hw, h * 0.5f, 0f), new Vector3(0.15f, h, def.d), wall);

            // 幅木と天井回り縁（箱だけの部屋に「建築」の輪郭を与える）
            if (IsFacility(def.id) || IsCore(def.id) || IsHome(def.id))
            {
                var trim = IsHome(def.id)
                    ? GetMat("LP_HomeTrim", new Color(0.5f, 0.38f, 0.26f), 0.3f)
                    : IsCore(def.id)
                        ? GetMat("LP_CoreTrim", new Color(0.2f, 0.2f, 0.22f), 0.3f)
                        : GetMat("LP_FacilityTrim", new Color(0.45f, 0.47f, 0.5f), 0.3f);
                Trim(t, hw, hd, h, DoorOpenW, trim);
            }

            // 入口扉（南）／出口扉（北）
            bool startRoom = def.id == LoopProgress.StartRoomId;
            RoomDoor(t, "EntryDoor", new Vector3(0f, 0f, -hd + 0.1f), 0f, def.id, false, startRoom, doorMat);
            RoomDoor(t, "ExitDoor", new Vector3(0f, 0f, hd - 0.1f), 180f, def.id, true, startRoom, doorMat);

            // スポーン地点（扉のすぐ内側）
            // スポーンの向き＝入った直後に見る方向。どちらの扉から入っても部屋の奥を向く
            //（電車なら車両の長い方向へ視線が抜ける）
            var entrySpawn = new GameObject("EntrySpawn").transform;
            entrySpawn.SetParent(t, false);
            entrySpawn.localPosition = new Vector3(0f, 0.05f, -hd + 1.2f);
            entrySpawn.localRotation = Quaternion.Euler(0f, 0f, 0f);      // +Z（部屋の奥）を向く
            var exitSpawn = new GameObject("ExitSpawn").transform;
            exitSpawn.SetParent(t, false);
            exitSpawn.localPosition = new Vector3(0f, 0.05f, hd - 1.2f);
            exitSpawn.localRotation = Quaternion.Euler(0f, 180f, 0f);     // -Z（部屋の奥）を向く

            // ブレイカー（東壁。細長い部屋＝電車では、妻面とシートの間の入口脇スペースに置く）
            // 最初の部屋にはブレイカーを置かない（資料2つを読めば扉が開く）
            BreakerSwitch breaker = null;
            if (def.id != LoopProgress.StartRoomId)
            {
                // 電車は車両中央の東壁（シートを分けて空けた場所）に壁付けする。
                // 本体の厚みは0.25なので、内壁面(hw-0.075)に接するよう中心を hw-0.2 に置く
                Vector3 bpos = def.id == "train"
                    ? new Vector3(hw - 0.2f, 0f, 0f)
                    : new Vector3(hw - 0.35f, 0f, 0f);
                breaker = BuildBreaker(t, def.id, bpos);
            }

            // アクセント帯とネームプレート（TextMeshは+Z面が表なので部屋中心を向ける）
            Box(t, "AccentBand_E", new Vector3(hw - 0.02f, 0.95f, 0f), new Vector3(0.06f, 0.22f, def.d - 0.4f), accent);
            Box(t, "AccentBand_W", new Vector3(-hw + 0.02f, 0.95f, 0f), new Vector3(0.06f, 0.22f, def.d - 0.4f), accent);
            float plateY = Mathf.Min(h - 0.35f, DoorOpenH + 0.3f);
            NamePlate(t, def.name, new Vector3(0f, plateY, -hd + 0.25f), 180f, RoomAccent(def.id));
            NamePlate(t, def.name, new Vector3(0f, plateY, hd - 0.25f), 0f, RoomAccent(def.id));

            // 部屋ごとの什器と「見つけるべき情報」
            string[] required;
            switch (def.id)
            {
                case "dim": required = FurnishDim(t, hw, hd, h); break;
                case "study": required = FurnishStudy(t, hw, hd, h); break;
                case "train": required = FurnishTrain(t, hw, hd, h); break;
                case "lab": required = FurnishLab(t, hw, hd, h); break;
                case "analysis": required = FurnishAnalysis(t, hw, hd, h); break;
                case "saeki_home": required = FurnishSaekiHome(t, hw, hd, h); break;
                case "ward": required = FurnishWard(t, hw, hd, h); break;
                case "core_ante": required = FurnishCoreAnte(t, hw, hd, h); break;
                case "mizuno_apart": required = FurnishMizunoApart(t, hw, hd, h); break;
                case "data_room": required = FurnishDataRoom(t, hw, hd, h); break;
                case "system_room": required = FurnishSystemRoom(t, hw, hd, h); break;
                case "kuroda_home": required = FurnishKurodaHome(t, hw, hd, h); break;
                case "core_main": required = FurnishCoreMain(t, hw, hd, h); break;
                case "son_room": required = FurnishSonRoom(t, hw, hd, h); break;
                default: required = new string[0]; break;
            }

            BuildRoomLights(t, def);

            // ルート登録
            var roomRoot = root.AddComponent<LoopRoomRoot>();
            roomRoot.Id = def.id;
            roomRoot.DisplayName = def.name;
            roomRoot.UnlockStage = def.stage;
            roomRoot.Side = def.side;
            roomRoot.Slot = def.slot;
            roomRoot.EntrySpawn = entrySpawn;
            roomRoot.ExitSpawn = exitSpawn;
            roomRoot.Breaker = breaker;
            roomRoot.RequiredFindables = required;

            // 発見アイテムに部屋Idを流し込む
            foreach (var f in root.GetComponentsInChildren<LoopFindable>(true)) f.RoomId = def.id;
        }

        /// <summary>
        /// 部屋の照明。部屋の系統ごとに色・強さ・器具を変える。
        /// 天井直下に強いポイントライトを置くと天井が白飛びして雰囲気が死ぬので、
        /// 光源は器具の少し下に置き、器具（発光パネル／ペンダント）を別に見せる。
        /// </summary>
        private static void BuildRoomLights(Transform t, RoomDef def)
        {
            int count = Mathf.Clamp(Mathf.CeilToInt(def.d / 5f), 1, 4);
            Color color; float intensity; float range;
            switch (def.id)
            {
                case "dim":          color = new Color(0.85f, 0.82f, 0.78f); intensity = 0.5f; range = 6f; break;
                case "core_ante":    color = new Color(0.9f, 0.5f, 0.45f);   intensity = 0.25f; range = 6f; break;
                case "system_room":  color = new Color(0.6f, 0.75f, 0.95f);  intensity = 0.45f; range = 9f; break;
                case "core_main":    color = new Color(1f, 0.9f, 0.7f);      intensity = 0.35f; range = 10f; break;
                case "analysis":     color = new Color(0.85f, 0.9f, 1f);     intensity = 0.7f; range = 9f; break;
                case "ward":         color = new Color(0.8f, 0.95f, 0.85f);  intensity = 0.65f; range = 9f; break;
                case "data_room":    color = new Color(0.75f, 0.82f, 1f);    intensity = 0.6f; range = 9f; break;
                case "saeki_home":   color = new Color(1f, 0.85f, 0.65f);    intensity = 0.55f; range = 8f; break;
                case "kuroda_home":  color = new Color(1f, 0.85f, 0.6f);     intensity = 0.5f; range = 8f; break;
                case "mizuno_apart": color = new Color(1f, 0.9f, 0.75f);     intensity = 0.5f; range = 7f; break;
                case "son_room":     color = new Color(1f, 0.97f, 0.9f);     intensity = 0.7f; range = 7f; break;
                default:             color = new Color(1f, 0.96f, 0.9f);     intensity = 2.0f; range = 12f; break;
            }

            bool facility = IsFacility(def.id) || def.id == "core_ante" || def.id == "data_room";
            bool home = IsHome(def.id);
            for (int i = 0; i < count; i++)
            {
                float z = count == 1 ? 0f : -def.d * 0.5f + def.d * (i + 0.5f) / count;
                var go = new GameObject("RoomLight");
                go.transform.SetParent(t, false);
                go.transform.localPosition = new Vector3(0f, def.h - 0.7f, z);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = color;
                l.intensity = intensity;
                l.range = range;
                l.shadows = LightShadows.Soft;

                // 器具の見た目
                if (facility)
                {
                    // 蛍光灯パネル（暗い部屋ほど弱く光る。停電中の前室は消灯）
                    bool off = def.id == "core_ante";
                    var panel = off
                        ? GetMat("LP_FluorescentOff", new Color(0.55f, 0.55f, 0.55f), 0.3f)
                        : EmissiveMat("LP_FluorescentPanel_" + def.id, new Color(0.9f, 0.92f, 0.95f), color * 1.6f);
                    Deco(t, "CeilingPanel", new Vector3(0f, def.h - 0.03f, z), new Vector3(1.2f, 0.05f, 0.35f), panel);
                    Deco(t, "CeilingPanelFrame", new Vector3(0f, def.h - 0.02f, z), new Vector3(1.3f, 0.04f, 0.45f),
                        GetMat("LP_FacilityTrim", new Color(0.45f, 0.47f, 0.5f), 0.3f));
                }
                else if (home)
                {
                    // ペンダント照明（コード＋丸いシェード）
                    // シェードは光源より弱く光らせる（強くすると白い球にしか見えない）
                    var shade = EmissiveMat("LP_PendantShade_" + def.id, new Color(0.75f, 0.68f, 0.55f), color * 0.55f);
                    Deco(t, "PendantCord", new Vector3(0f, def.h - 0.15f, z), new Vector3(0.015f, 0.3f, 0.015f),
                        GetMat("LP_CoreTrim", new Color(0.2f, 0.2f, 0.22f), 0.3f));
                    var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    dome.name = "PendantShade"; dome.transform.SetParent(t, false);
                    dome.transform.localPosition = new Vector3(0f, def.h - 0.34f, z);
                    dome.transform.localScale = new Vector3(0.32f, 0.16f, 0.32f);
                    dome.GetComponent<Renderer>().sharedMaterial = shade;
                    Object.DestroyImmediate(dome.GetComponent<Collider>());
                }
            }
        }

        // ---- 部屋の系統（材質・照明・幅木の選択に使う）----
        private static bool IsFacility(string id) => id == "analysis" || id == "ward" || id == "data_room";
        private static bool IsCore(string id) => id == "core_ante" || id == "system_room" || id == "core_main";
        private static bool IsHome(string id) => id == "saeki_home" || id == "kuroda_home" || id == "mizuno_apart" || id == "son_room";

        /// <summary>住宅の板張り床（回廊と同じ木目テクスチャを暗めに）</summary>
        private static Material HomeFloorMat()
        {
            var mat = GetMat("LP_HomeWood", new Color(0.55f, 0.42f, 0.3f), 0.3f);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EscapePrototype/Textures/floor_wood.png");
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>幅木（床際）と回り縁（天井際）を四周に回す。扉開口は避ける</summary>
        private static void Trim(Transform t, float hw, float hd, float h, float doorW, Material mat)
        {
            const float bh = 0.1f, bt = 0.03f;
            float segW = hw - doorW * 0.5f;
            foreach (float zSign in new[] { 1f, -1f })
            {
                float z = zSign * (hd - 0.075f - bt * 0.5f);
                if (segW > 0.05f)
                {
                    float cx = doorW * 0.5f + segW * 0.5f;
                    Deco(t, "Baseboard", new Vector3(-cx, bh * 0.5f, z), new Vector3(segW, bh, bt), mat);
                    Deco(t, "Baseboard", new Vector3(cx, bh * 0.5f, z), new Vector3(segW, bh, bt), mat);
                }
                Deco(t, "Cornice", new Vector3(0f, h - 0.03f, z), new Vector3(hw * 2f, 0.06f, bt), mat);
            }
            foreach (float xSign in new[] { 1f, -1f })
            {
                float x = xSign * (hw - 0.075f - bt * 0.5f);
                Deco(t, "Baseboard", new Vector3(x, bh * 0.5f, 0f), new Vector3(bt, bh, hd * 2f), mat);
                Deco(t, "Cornice", new Vector3(x, h - 0.03f, 0f), new Vector3(bt, 0.06f, hd * 2f), mat);
            }
        }

        /// <summary>窓（暗いガラス＋枠＋任意でカーテン）。yaw=0で-Z面に向く板を壁面に貼る</summary>
        private static void Window(Transform t, Vector3 pos, float yaw, float w, float h, bool curtain, Color? curtainColor = null)
        {
            var unit = new GameObject("Window");
            unit.transform.SetParent(t, false);
            unit.transform.localPosition = pos;
            unit.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var glass = MetalMat("LP_NightGlass", new Color(0.04f, 0.05f, 0.07f), 0.9f, 0.3f);
            var frame = GetMat("LP_WindowFrame", new Color(0.85f, 0.85f, 0.85f), 0.4f);
            Deco(unit.transform, "Glass", new Vector3(0f, 0f, 0f), new Vector3(w, h, 0.02f), glass);
            Deco(unit.transform, "Frame_T", new Vector3(0f, h * 0.5f, 0f), new Vector3(w + 0.08f, 0.06f, 0.05f), frame);
            Deco(unit.transform, "Frame_B", new Vector3(0f, -h * 0.5f, 0f), new Vector3(w + 0.08f, 0.06f, 0.05f), frame);
            Deco(unit.transform, "Frame_L", new Vector3(-w * 0.5f, 0f, 0f), new Vector3(0.06f, h, 0.05f), frame);
            Deco(unit.transform, "Frame_R", new Vector3(w * 0.5f, 0f, 0f), new Vector3(0.06f, h, 0.05f), frame);
            Deco(unit.transform, "Mullion", new Vector3(0f, 0f, 0f), new Vector3(0.04f, h, 0.05f), frame);
            if (curtain)
            {
                var cm = GetMat("LP_Curtain", curtainColor ?? new Color(0.75f, 0.68f, 0.55f), 0.05f);
                Deco(unit.transform, "CurtainRail", new Vector3(0f, h * 0.5f + 0.12f, -0.08f), new Vector3(w + 0.5f, 0.03f, 0.03f), frame);
                // 両端に寄せた開いたカーテン
                Deco(unit.transform, "Curtain_L", new Vector3(-w * 0.5f - 0.05f, -0.1f, -0.1f), new Vector3(0.35f, h + 0.5f, 0.08f), cm);
                Deco(unit.transform, "Curtain_R", new Vector3(w * 0.5f + 0.05f, -0.1f, -0.1f), new Vector3(0.35f, h + 0.5f, 0.08f), cm);
            }
        }

        /// <summary>床のラグ／マット（薄い板）</summary>
        private static void Rug(Transform t, Vector3 pos, Vector2 size, Color color)
        {
            Deco(t, "Rug", pos + new Vector3(0f, 0.008f, 0f), new Vector3(size.x, 0.016f, size.y),
                GetMat("LP_Rug_" + ColorUtility.ToHtmlStringRGB(color), color, 0.05f));
        }

        /// <summary>天井ダクト（施設・コア用）。z軸方向に走る角ダクト</summary>
        private static void Duct(Transform t, Vector3 pos, float length, float size, Material mat)
        {
            Deco(t, "Duct", pos, new Vector3(size, size, length), mat);
            for (float z = -length * 0.5f + 0.4f; z < length * 0.5f; z += 1.2f)
                Deco(t, "DuctHanger", pos + new Vector3(0f, size * 0.5f + 0.1f, z), new Vector3(0.03f, 0.2f, 0.03f), mat);
        }

        // ============================== 部屋ごとの什器と発見アイテム ==============================

        /// <summary>薄暗い部屋：ベッド・机・ランプ／新聞記事・懐中電灯（机の上）</summary>
        private static string[] FurnishDim(Transform t, float hw, float hd, float h)
        {
            var wood = GetMat("LP_Furniture", new Color(0.4f, 0.3f, 0.22f), 0.2f);
            var cloth = GetMat("LP_Bedding", new Color(0.55f, 0.52f, 0.5f), 0.1f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            var metal = GetMat("LP_Metal", new Color(0.45f, 0.47f, 0.5f), 0.5f);

            // ベッド（西壁沿い）
            var bed = new GameObject("Bed"); bed.transform.SetParent(t, false);
            bed.transform.localPosition = new Vector3(-hw + 1.2f, 0f, -1.0f);
            Box(bed.transform, "Frame", new Vector3(0f, 0.22f, 0f), new Vector3(1.1f, 0.44f, 2.1f), wood);
            Box(bed.transform, "Mattress", new Vector3(0f, 0.52f, 0f), new Vector3(1.0f, 0.18f, 2.0f), cloth);
            Box(bed.transform, "Pillow", new Vector3(0f, 0.66f, -0.75f), new Vector3(0.6f, 0.12f, 0.35f), cloth);

            // 机（東壁沿い）＋ランプ
            var desk = Desk(t, "Desk", new Vector3(hw - 1.4f, 0f, 0.6f), wood);
            var lamp = new GameObject("Lamp"); lamp.transform.SetParent(desk.transform, false);
            lamp.transform.localPosition = new Vector3(0.5f, 0.75f, 0.2f);
            Box(lamp.transform, "Base", new Vector3(0f, 0.03f, 0f), new Vector3(0.16f, 0.06f, 0.16f), metal);
            Box(lamp.transform, "Pole", new Vector3(0f, 0.2f, 0f), new Vector3(0.04f, 0.34f, 0.04f), metal);
            var shade = GetMat("LP_LampShade", new Color(0.95f, 0.85f, 0.6f), 0.2f);
            shade.EnableKeyword("_EMISSION");
            shade.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            shade.SetColor("_EmissionColor", new Color(1f, 0.88f, 0.6f) * 2.5f);
            Box(lamp.transform, "Shade", new Vector3(0f, 0.42f, 0f), new Vector3(0.24f, 0.14f, 0.24f), shade);
            var ll = new GameObject("LampLight"); ll.transform.SetParent(lamp.transform, false);
            ll.transform.localPosition = new Vector3(0f, 0.38f, 0f);
            var lightC = ll.AddComponent<Light>();
            lightC.type = LightType.Point; lightC.color = new Color(1f, 0.88f, 0.65f);
            lightC.intensity = 2.2f; lightC.range = 6f;

            // 新聞記事（机の上）
            Findable(desk.transform, "news", "新聞記事", new Vector3(-0.28f, 0.76f, 0f), paper,
                new Vector3(0.42f, 0.02f, 0.3f),
                "新聞記事（切り抜き）",
                "《地域面》\n小川脳神経総合研究所、臨床試験を再開\n\n" +
                "……同研究所は「脳神経とAIの融合」を掲げ、\n記憶領域への介入実験を進めていたとされる。\n" +
                "関係者によれば、被験者の一部に\n『眠りから覚めない』症例が報告されており……\n\n" +
                "（記事の下半分は破り取られている）");

            // 懐中電灯（机の上。拾うと消える道具）
            var flGo = Findable(desk.transform, "flashlight", "懐中電灯", new Vector3(0.15f, 0.79f, -0.1f), metal,
                new Vector3(0.07f, 0.07f, 0.28f), null, null);
            var flF = flGo.GetComponent<LoopFindable>();
            flF.DisappearOnPickup = true;
            flF.PickupHint = "F: 点灯";

            // 手帳（机の上。拾うと消える道具。拾うまで資料は読めずTabも開けない）
            // 木の机に埋もれないよう、濃い緑の表紙＋白い小口＋赤い栞で見分けやすくする
            var noteMat = GetMat("LP_NotebookItem", new Color(0.10f, 0.22f, 0.16f), 0.32f);
            var pageMat = GetMat("LP_NotebookPages", new Color(0.90f, 0.88f, 0.80f), 0.1f);
            var markMat = GetMat("LP_NotebookMark", new Color(0.65f, 0.12f, 0.12f), 0.2f);
            var nbGo = Findable(desk.transform, "notebook", "手帳", new Vector3(0.42f, 0.785f, 0.18f), noteMat,
                new Vector3(0.21f, 0.055f, 0.16f), null, null);
            var nbF = nbGo.GetComponent<LoopFindable>();
            nbF.DisappearOnPickup = true;
            nbF.PickupHint = "Tab: 開く";
            // 小口（表紙よりわずかに薄く短い白い束）と栞
            Deco(nbGo.transform, "Pages", new Vector3(0.008f, 0f, 0f), new Vector3(0.196f, 0.038f, 0.148f), pageMat);
            Deco(nbGo.transform, "Bookmark", new Vector3(0.045f, 0.03f, -0.055f), new Vector3(0.02f, 0.006f, 0.2f), markMat);

            // ---- セーブPC（記録端末。北西の隅の小さな台の上）----
            var saveMat = GetMat("LP_SavePc", new Color(0.3f, 0.32f, 0.34f), 0.4f);
            var screenMat = GetMat("LP_SaveScreen", new Color(0.1f, 0.3f, 0.15f), 0.2f);
            screenMat.EnableKeyword("_EMISSION");
            screenMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            screenMat.SetColor("_EmissionColor", new Color(0.25f, 1f, 0.4f) * 1.6f);
            var savePc = new GameObject("SavePc");
            savePc.transform.SetParent(t, false);
            savePc.transform.localPosition = new Vector3(-hw + 0.9f, 0f, hd - 1.0f);
            savePc.transform.localRotation = Quaternion.Euler(0f, 135f, 0f);
            Box(savePc.transform, "Stand", new Vector3(0f, 0.42f, 0f), new Vector3(0.62f, 0.84f, 0.5f), wood);
            Box(savePc.transform, "Body", new Vector3(0f, 1.02f, 0.02f), new Vector3(0.44f, 0.36f, 0.34f), saveMat);
            Box(savePc.transform, "Screen", new Vector3(0f, 1.02f, -0.16f), new Vector3(0.34f, 0.26f, 0.02f), screenMat);
            Box(savePc.transform, "Keyboard", new Vector3(0f, 0.86f, -0.16f), new Vector3(0.36f, 0.03f, 0.14f), saveMat);
            var saveLightGo = new GameObject("ScreenGlow");
            saveLightGo.transform.SetParent(savePc.transform, false);
            saveLightGo.transform.localPosition = new Vector3(0f, 1.05f, -0.3f);
            var saveLight = saveLightGo.AddComponent<Light>();
            saveLight.type = LightType.Point;
            saveLight.color = new Color(0.35f, 1f, 0.5f);
            saveLight.intensity = 0.8f;
            saveLight.range = 2.5f;
            savePc.AddComponent<SavePoint>();

            // ---- 陶器人形の棚（東壁の北側。残機の数だけ人形が並ぶ）----
            var shelfMat = GetMat("LP_DollShelf", new Color(0.35f, 0.28f, 0.2f), 0.2f);
            var dollMat = GetMat("LP_Doll", new Color(0.94f, 0.93f, 0.9f), 0.55f);
            var shelfGo = new GameObject("DollShelf");
            shelfGo.transform.SetParent(t, false);
            shelfGo.transform.localPosition = new Vector3(hw - 0.35f, 0f, 2.2f);
            Box(shelfGo.transform, "Board", new Vector3(0f, 1.15f, 0f), new Vector3(0.3f, 0.05f, 1.5f), shelfMat);
            Box(shelfGo.transform, "Bracket", new Vector3(0.1f, 1.02f, -0.55f), new Vector3(0.08f, 0.24f, 0.08f), shelfMat);
            Box(shelfGo.transform, "Bracket", new Vector3(0.1f, 1.02f, 0.55f), new Vector3(0.08f, 0.24f, 0.08f), shelfMat);
            var dollsRoot = new GameObject("Dolls");
            dollsRoot.transform.SetParent(shelfGo.transform, false);
            for (int i = 0; i < 5; i++)
            {
                var doll = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                doll.name = $"Doll_{i}";
                doll.transform.SetParent(dollsRoot.transform, false);
                doll.transform.localPosition = new Vector3(0f, 1.3f, -0.56f + i * 0.28f);
                doll.transform.localScale = new Vector3(0.1f, 0.12f, 0.1f);
                doll.GetComponent<Renderer>().sharedMaterial = dollMat;
                Object.DestroyImmediate(doll.GetComponent<Collider>());
            }
            var shelfComp = shelfGo.AddComponent<DollShelf>();
            shelfComp.DollsRoot = dollsRoot.transform;

            // ---- 起床カットシーン（ベッドから起き上がる）----
            BuildIntroCutscene(t, new Vector3(-hw + 1.2f, 0f, -1.0f), hd);

            return new[] { "news", "flashlight", "notebook" };
        }

        /// <summary>書斎：机／文書（机の上）</summary>
        private static string[] FurnishStudy(Transform t, float hw, float hd, float h)
        {
            var wood = GetMat("LP_FurnitureDark", new Color(0.3f, 0.21f, 0.14f), 0.25f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);

            // 書き物机（部屋中央やや奥）＋椅子
            var desk = Desk(t, "Desk", new Vector3(0f, 0f, 1.4f), wood);
            Box(t, "Chair", new Vector3(0f, 0.25f, 0.35f), new Vector3(0.5f, 0.5f, 0.5f), wood);
            // 本棚（両壁。天井高に合わせる）
            float shelfH = Mathf.Min(2.1f, h - 0.4f);
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1f : 1f) * (hw - 0.3f);
                Box(t, "Bookshelf", new Vector3(x, shelfH * 0.5f, -1.2f), new Vector3(0.45f, shelfH, 2.6f), wood);
            }

            Findable(desk.transform, "document", "文書", new Vector3(0f, 0.76f, 0f), paper,
                new Vector3(0.4f, 0.02f, 0.3f),
                "研究文書（写し）",
                "《被験体経過報告 抜粋》\n\n" +
                "・被験体は自身を『研究員』と認識している\n" +
                "・記憶の再生時、同一の空間を反復して認知する\n" +
                "  （回廊状の構造として観測される）\n" +
                "・移植したAI領域が宿主の記憶を再構成している\n" +
                "  可能性がある\n\n" +
                "報告者名の欄は黒く塗り潰されている。");

            // ---- 1章 起「佐伯恒一」: 招聘状・主任の手帳の切れ端・伏せられた家族写真 ----

            // 招聘状（サイドテーブルの上）→ 記憶回復①「私は二宮秀樹」
            var side = new GameObject("SideTable");
            side.transform.SetParent(t, false);
            side.transform.localPosition = new Vector3(hw - 0.75f, 0f, -2.6f);
            Box(side.transform, "Top", new Vector3(0f, 0.55f, 0f), new Vector3(0.6f, 0.05f, 0.5f), wood);
            Box(side.transform, "Leg", new Vector3(0f, 0.27f, 0f), new Vector3(0.1f, 0.54f, 0.1f), wood);
            Findable(side.transform, "invite", "封書", new Vector3(0f, 0.59f, 0f), paper,
                new Vector3(0.3f, 0.02f, 0.22f),
                "招聘状",
                "《招聘状》　2015年3月25日\n\n" +
                "二宮秀樹 様\n\n" +
                "貴殿の情報科学における業績、ならびに\n" +
                "調査業でのご経験を高く評価し、当研究所の\n" +
                "特別研究員としてお迎えしたく存じます。\n\n" +
                "　小川脳神経総合研究所　所長\n\n" +
                "──二宮秀樹。\n" +
                "その名前を目でなぞった瞬間、胸の奥が軋んだ。\n" +
                "私だ。私の名前だ。情報科学と、探偵。");

            // 主任の手帳の切れ端（机の上・端）
            Findable(desk.transform, "scrap", "手帳の切れ端", new Vector3(-0.42f, 0.755f, -0.12f), paper,
                new Vector3(0.24f, 0.01f, 0.12f),
                "主任の手帳の切れ端",
                "《手帳の切れ端》\n\n" +
                "　4/2　佐伯と面談。\n" +
                "　　　 例の件、佐伯にだけは話しておく。\n" +
                "　　　 彼なら気づいている。\n\n" +
                "　4/9　佐伯より報告。「今は結論を出すな」と\n" +
                "　　　 伝えた。時間が要る。\n\n" +
                "破り取られたページ。所長と佐伯──\n" +
                "二人だけが共有していた「例の件」とは。");

            // 伏せられた家族写真（棚の上。読める演出・進行必須ではない）
            var frameMat = GetMat("LP_PhotoFrame", new Color(0.25f, 0.2f, 0.15f), 0.3f);
            Findable(t, "photo", "伏せられた写真立て", new Vector3(hw - 0.55f, 2.14f, -1.2f), frameMat,
                new Vector3(0.2f, 0.03f, 0.15f),
                "伏せられた写真立て",
                "写真立てが、裏返しに伏せられている。\n\n" +
                "起こしてみると、若い頃の所長らしき男性と、\n" +
                "小さな男の子が写っていた。\n" +
                "男の子の顔のところだけ、何度も指でなぞったように\n" +
                "色が薄くなっている。\n\n" +
                "……どうして、伏せてあったんだろう。");

            // 残響〈主任と佐伯の密談〉──主人公の記憶（廊下から見かけただけ。声は聞こえない）
            Echo(t, "study_secret", "残響：主任と佐伯の密談（私の記憶）",
                "書斎の隅で、二つの影が額を寄せ合っている。\n" +
                "声は、聞こえない。廊下からただ見かけただけの記憶だから。\n" +
                "所長と、名簿の男・佐伯。二人は何を話していた？",
                2.0f, 1f, 0f,   // 無音の場面
                (new Vector3(-hw + 0.9f, 0f, 0.4f), 110f, 1.0f, 3f, 1f, false),
                (new Vector3(-hw + 1.5f, 0f, -0.3f), -50f, 0.95f, -3f, 1f, false));

            // 壁の金庫（1章起・転記型）: 招聘状の日付 0325。中身は解析室キャビネットの鍵
            var safeMat = MetalMat("LP_SafeBody", new Color(0.3f, 0.32f, 0.34f), 0.5f, 0.7f);
            var safe = Lock<LoopCodeLock>(t, "WallSafe", new Vector3(-1.7f, 1.1f, hd - 0.25f),
                new Vector3(0.6f, 0.5f, 0.3f), safeMat, "study", "safekey", "壁の金庫", "study_invite");
            safe.Title = "金庫のダイヤル";
            safe.Body = "4桁の数字を合わせる。\nダイヤルの脇に小さく「私の日付」と刻んである。";
            safe.Length = 4; safe.Answer = "0325";
            safe.SuccessNoteTitle = "金庫の中身：解析室キャビネットの鍵";
            safe.SuccessNoteBody =
                "金庫の中には鍵がひとつ。\n" +
                "タグに「第8研究室　キャビネット」。\n\n" +
                "──私の日付。招聘状の日付で開いた。\n" +
                "この金庫を設定したのは、私を招いた人だ。";
            var dial = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dial.name = "Dial"; dial.transform.SetParent(safe.transform, false);
            dial.transform.localPosition = new Vector3(0f, 0f, -0.17f);
            dial.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            dial.transform.localScale = new Vector3(0.14f, 0.02f, 0.14f);
            dial.GetComponent<Renderer>().sharedMaterial = GetMat("LP_SafeDial", new Color(0.75f, 0.72f, 0.65f), 0.6f);
            Object.DestroyImmediate(dial.GetComponent<Collider>());

            return new[] { "document", "invite", "scrap", "safekey" };
        }

        /// <summary>
        /// 電車車内（コンセプトアート impl_train_* 準拠）：
        /// クリーム色の化粧板／深紅のロングシート＋銀の袖仕切り／白い輪の吊革／網棚／
        /// 暗い窓／側面ドア／灰色の床＋黄色ライン／蛍光灯／赤い車端扉（窓付き）／中吊り広告。
        /// 前後の妻面は扉開口を塞がないよう左右に分割する（＝必ず出口がある）
        /// </summary>
        private static string[] FurnishTrain(Transform t, float hw, float hd, float h)
        {
            var seat = GetMat("LP_TrainSeat", new Color(0.55f, 0.09f, 0.11f), 0.12f);
            var seatDark = GetMat("LP_TrainSeatDark", new Color(0.42f, 0.06f, 0.08f), 0.1f);
            var chrome = MetalMat("LP_TrainChrome", new Color(0.80f, 0.81f, 0.83f), 0.85f, 1f);
            var chromeDark = MetalMat("LP_TrainChromeDark", new Color(0.45f, 0.46f, 0.48f), 0.85f, 1f);
            var cream = GetMat("LP_TrainWall", new Color(0.90f, 0.87f, 0.78f), 0.35f);
            var strapWhite = GetMat("LP_TrainStrap", new Color(0.93f, 0.91f, 0.85f), 0.25f);
            var grille = GetMat("LP_TrainGrille", new Color(0.22f, 0.22f, 0.23f), 0.2f);
            var glassDark = MetalMat("LP_TrainWindow", new Color(0.05f, 0.06f, 0.08f), 0.92f, 0.3f);
            var yellow = GetMat("LP_TrainYellowLine", new Color(0.92f, 0.78f, 0.15f), 0.2f);
            var tube = EmissiveMat("LP_TrainTube", new Color(0.95f, 0.95f, 0.9f), new Color(0.9f, 0.95f, 1f) * 2.4f);
            var poster = GetMat("LP_TrainPoster", new Color(0.35f, 0.32f, 0.3f), 0.15f);

            // ---- 中吊り広告（生成画像・日本語焼き込み） ----
            const float adW = 1.15f, adH = 0.78f;
            var adMat = GetMat("LP_TrainAd", Color.white, 0.1f);
            var adTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Arts/Generated/ad_poster_jp.png");
            if (adTex != null)
            {
                var fit = new Vector2(1f / (adW * UvPerMeter), 1f / (adH * UvPerMeter));
                adMat.SetTexture("_BaseMap", adTex);
                adMat.SetTextureScale("_BaseMap", fit);
                adMat.SetColor("_BaseColor", Color.white);
                adMat.EnableKeyword("_EMISSION");
                adMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                adMat.SetTexture("_EmissionMap", adTex);
                adMat.SetTextureScale("_EmissionMap", fit);
                adMat.SetColor("_EmissionColor", Color.white * 0.4f);
                EditorUtility.SetDirty(adMat);
            }

            // ---- レイアウト ----
            // 壁は厚み0.15で中心が±hw にあるので、内壁面は ±(hw-0.075)。壁付けの什器は内壁面基準で置く
            float wallHw = hw;
            hw -= 0.075f;
            // 側面ドア（見た目のみ）を z=±doorZ に置き、その間と外側にシートを分割配置する
            const float doorZ = 4.5f, doorW = 1.3f;
            const float endClear = 1.5f;                       // 妻面まわりは通路として空ける
            float centerZ0 = -doorZ + doorW * 0.5f + 0.35f;    // 中央区間の範囲
            float centerZ1 = doorZ - doorW * 0.5f - 0.35f;
            // 東側（+X）の車両中央には配電盤を据えるので、中央のシートを左右に分けて壁を空ける
            const float breakerGapHalf = 0.5f;
            float[][] segsPlain =
            {
                new[] { -hd + endClear, -doorZ - doorW * 0.5f - 0.35f },   // 後端側
                new[] { centerZ0, centerZ1 },                              // 中央
                new[] { doorZ + doorW * 0.5f + 0.35f, hd - endClear },     // 前端側
            };
            float[][] segsBreakerSide =
            {
                new[] { -hd + endClear, -doorZ - doorW * 0.5f - 0.35f },
                new[] { centerZ0, -breakerGapHalf },                       // 中央（配電盤の手前まで）
                new[] { breakerGapHalf, centerZ1 },                        // 中央（配電盤の先から）
                new[] { doorZ + doorW * 0.5f + 0.35f, hd - endClear },
            };
            const float seatDepth = 0.55f;
            float railY = 2.35f;      // 吊革レール（頭上をクリアする高さ）
            float rackY = 1.95f;      // 網棚

            foreach (float sx in new[] { -1f, 1f })
            {
                float wallX = sx * hw;
                float seatX = sx * (hw - seatDepth * 0.5f - 0.05f);
                var segs = sx > 0f ? segsBreakerSide : segsPlain;

                foreach (var seg in segs)
                {
                    float z0 = seg[0], z1 = seg[1], len = z1 - z0, zc = (z0 + z1) * 0.5f;
                    if (len < 0.8f) continue;

                    // シート：座面（深紅）・背もたれ・下部のヒーターグリル・座面下の暗い箱
                    Box(t, "Seat", new Vector3(seatX, 0.42f, zc), new Vector3(seatDepth, 0.13f, len), seat);
                    Box(t, "SeatBase", new Vector3(seatX, 0.18f, zc), new Vector3(seatDepth - 0.05f, 0.36f, len), grille);
                    Box(t, "SeatBack", new Vector3(sx * (hw - 0.09f), 0.82f, zc), new Vector3(0.14f, 0.7f, len), seatDark);
                    // 座面前縁の黄色ライン（床）
                    Deco(t, "YellowLine", new Vector3(sx * (hw - seatDepth - 0.12f), 0.003f, zc), new Vector3(0.05f, 0.006f, len), yellow);

                    // 袖仕切り（両端）：クリーム色の板＋銀のパイプ握り
                    foreach (float ze in new[] { z0, z1 })
                    {
                        float zs = ze + (ze == z0 ? 0.03f : -0.03f);
                        Box(t, "SleevePanel", new Vector3(seatX, 0.72f, zs), new Vector3(seatDepth + 0.05f, 0.75f, 0.06f), cream);
                        Deco(t, "SleeveRail", new Vector3(sx * (hw - seatDepth - 0.10f), 1.02f, zs), new Vector3(0.035f, 0.035f, 0.035f), chrome);
                        Deco(t, "SleevePipe", new Vector3(sx * (hw - seatDepth - 0.10f), 0.86f, zs), new Vector3(0.03f, 0.35f, 0.03f), chrome);
                        // 仕切りの上から天井への握り棒（ドア脇）
                        Deco(t, "Pole", new Vector3(sx * (hw - seatDepth - 0.10f), (1.05f + h) * 0.5f, zs), new Vector3(0.03f, h - 1.05f, 0.03f), chrome);
                    }
                    Deco(t, "SleeveTopBar", new Vector3(sx * (hw - seatDepth * 0.5f - 0.05f), 1.05f, z0 + 0.03f), new Vector3(seatDepth, 0.03f, 0.03f), chrome);
                    Deco(t, "SleeveTopBar", new Vector3(sx * (hw - seatDepth * 0.5f - 0.05f), 1.05f, z1 - 0.03f), new Vector3(seatDepth, 0.03f, 0.03f), chrome);

                    // 窓（暗いガラス）。長い区間は複数枚に分割
                    int nWin = Mathf.Max(1, Mathf.RoundToInt(len / 1.9f));
                    float winW = (len - 0.25f * (nWin + 1)) / nWin;
                    for (int w = 0; w < nWin; w++)
                    {
                        float wz = z0 + 0.25f + winW * 0.5f + w * (winW + 0.25f);
                        Deco(t, "WindowFrame", new Vector3(sx * (hw - 0.03f), 1.62f, wz), new Vector3(0.03f, 0.98f, winW + 0.08f), chrome);
                        Deco(t, "Window", new Vector3(sx * (hw - 0.045f), 1.62f, wz), new Vector3(0.02f, 0.9f, winW), glassDark);
                    }

                    // 網棚（銀のパイプ3本＋ブラケット）
                    for (int r = 0; r < 3; r++)
                        Deco(t, "RackPipe", new Vector3(sx * (hw - 0.12f - r * 0.14f), rackY + r * 0.02f, zc), new Vector3(0.025f, 0.025f, len), chrome);
                    for (float bz = z0 + 0.3f; bz < z1 - 0.2f; bz += 1.2f)
                        Deco(t, "RackBracket", new Vector3(sx * (hw - 0.22f), rackY - 0.02f, bz), new Vector3(0.36f, 0.02f, 0.03f), chromeDark);

                    // 窓上の小さな広告枠
                    for (float pz = z0 + 0.6f; pz < z1 - 0.5f; pz += 1.9f)
                        Deco(t, "SidePoster", new Vector3(sx * (hw - 0.03f), 2.32f, pz), new Vector3(0.02f, 0.32f, 0.5f), poster);

                    // 吊革レール（銀）と白い輪の吊革
                    float railX = sx * (hw - 0.85f);
                    Deco(t, "StrapRail", new Vector3(railX, railY, zc), new Vector3(0.035f, 0.035f, len), chrome);
                    int straps = Mathf.Max(1, Mathf.FloorToInt(len / 0.5f));
                    for (int k = 0; k < straps; k++)
                    {
                        float z = z0 + (k + 0.5f) * (len / straps);
                        Deco(t, "Strap", new Vector3(railX, railY - 0.16f, z), new Vector3(0.02f, 0.3f, 0.02f), strapWhite);
                        Deco(t, "StrapRing", new Vector3(railX, railY - 0.36f, z), new Vector3(0.12f, 0.12f, 0.02f), strapWhite);
                        // 輪の穴（中を暗く見せる小さな板）
                        Deco(t, "StrapHole", new Vector3(railX, railY - 0.36f, z), new Vector3(0.07f, 0.07f, 0.024f), grille);
                    }
                    // レールを天井へ吊る支柱
                    for (float hz = z0 + 0.4f; hz < z1; hz += 1.6f)
                        Deco(t, "RailHanger", new Vector3(railX, (railY + h) * 0.5f, hz), new Vector3(0.025f, h - railY, 0.025f), chrome);
                }

                // 側面ドア（見た目のみ・銀の両開き＋縦長の窓）と上の路線図
                foreach (float dz in new[] { -doorZ, doorZ })
                {
                    Deco(t, "SideDoor", new Vector3(sx * (hw - 0.02f), 1.0f, dz), new Vector3(0.03f, 2.0f, doorW), chrome);
                    Deco(t, "SideDoorSeam", new Vector3(sx * (hw - 0.04f), 1.0f, dz), new Vector3(0.02f, 1.98f, 0.02f), grille);
                    foreach (float ws in new[] { -0.32f, 0.32f })
                        Deco(t, "SideDoorWindow", new Vector3(sx * (hw - 0.05f), 1.45f, dz + ws), new Vector3(0.02f, 0.75f, 0.42f), glassDark);
                    Deco(t, "RouteMap", new Vector3(sx * (hw - 0.03f), 2.3f, dz), new Vector3(0.02f, 0.28f, 1.1f), strapWhite);
                }
            }

            // ---- 天井：蛍光灯（発光）と丸い換気口 ----
            for (float z = -hd + 1.6f; z < hd - 1.0f; z += 2.4f)
            {
                Deco(t, "FluorescentTube", new Vector3(0f, h - 0.03f, z), new Vector3(0.12f, 0.04f, 1.2f), tube);
                Deco(t, "TubeHousing", new Vector3(0f, h - 0.02f, z), new Vector3(0.22f, 0.03f, 1.3f), cream);
            }
            for (float z = -hd + 2.8f; z < hd - 1.0f; z += 4.8f)
            {
                var vent = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                vent.name = "Vent"; vent.transform.SetParent(t, false);
                vent.transform.localPosition = new Vector3(0f, h - 0.02f, z);
                vent.transform.localScale = new Vector3(0.45f, 0.01f, 0.45f);
                vent.GetComponent<Renderer>().sharedMaterial = grille;
                Object.DestroyImmediate(vent.GetComponent<Collider>());
            }

            // ---- 妻面（前後）：中央の扉開口を避けて左右にクリーム色のパネル＝通り抜けられる ----
            const float openW = 1.15f;
            float panelW = wallHw - openW * 0.5f;
            if (panelW > 0.1f)
                foreach (float zSign in new[] { 1f, -1f })
                    foreach (float xSign in new[] { -1f, 1f })
                    {
                        float px = xSign * (openW * 0.5f + panelW * 0.5f);
                        float pz = zSign * (hd - 0.5f);
                        Box(t, "EndPanel", new Vector3(px, (h - 0.3f) * 0.5f, pz), new Vector3(panelW, h - 0.3f, 0.1f), cream);
                        // 妻面の小窓と、扉脇の握り棒
                        Deco(t, "EndWindow", new Vector3(px, 1.6f, pz - zSign * 0.06f), new Vector3(Mathf.Max(0.3f, panelW - 0.3f), 0.7f, 0.02f), glassDark);
                        Deco(t, "EndPole", new Vector3(xSign * (openW * 0.5f + 0.08f), 1.25f, pz - zSign * 0.12f), new Vector3(0.03f, 1.6f, 0.03f), chrome);
                    }

            // 車端扉（LoopRoomDoor：赤い扉）の上に縦長の窓と路線図を重ねる。窓はコライダー無しなので操作を邪魔しない
            foreach (float zSign in new[] { -1f, 1f })
            {
                // 扉パネル（厚0.08、z=±(hd-0.1)）の部屋側の面
                float faceZ = zSign * (hd - 0.10f - 0.045f);
                Deco(t, "EndDoorWindow", new Vector3(0f, 1.45f, faceZ), new Vector3(0.36f, 0.85f, 0.01f), glassDark);
                Deco(t, "EndDoorWindowFrame", new Vector3(0f, 1.45f, faceZ + zSign * 0.003f), new Vector3(0.42f, 0.91f, 0.008f), chrome);
                Deco(t, "EndDoorSign", new Vector3(0f, 2.45f, faceZ), new Vector3(0.9f, 0.22f, 0.01f), strapWhite);
            }

            // ---- 中吊り広告（発見アイテム）：頭上をクリアする高さに吊る ----
            var ad = new GameObject("HangingAd"); ad.transform.SetParent(t, false);
            ad.transform.localPosition = new Vector3(0f, 0f, -1.5f);
            float adY = Mathf.Max(h - 0.45f, 2.2f + adH * 0.5f);
            float wireLen = Mathf.Max(0.06f, h - (adY + adH * 0.5f));
            Box(ad.transform, "Wire_L", new Vector3(-0.55f, adY + adH * 0.5f + wireLen * 0.5f, 0f), new Vector3(0.02f, wireLen, 0.02f), chrome);
            Box(ad.transform, "Wire_R", new Vector3(0.55f, adY + adH * 0.5f + wireLen * 0.5f, 0f), new Vector3(0.02f, wireLen, 0.02f), chrome);
            var panel = Findable(ad.transform, "ad", "中吊り広告", new Vector3(0f, adY, 0f), adMat,
                new Vector3(adW, adH, 0.03f),
                "中吊り広告",
                "《小川脳神経総合研究所》\n\n" +
                "　　脳神経 × AI 開発\n\n" +
                "「記憶は、置き換えられる。」\n\n" +
                "被験者募集中 — 詳しくは当研究所まで");
            TextPlate(panel.transform, "「記憶は、置き換えられる。」",
                new Vector3(0f, -0.30f, -0.03f), 0f, new Color(1f, 0.86f, 0.86f), 0.014f);
            // 他の中吊り（読めないダミー）を前後に
            foreach (float z in new[] { 3.5f, -6.0f, 6.5f })
            {
                Deco(t, "DummyAd", new Vector3(0f, adY, z), new Vector3(1.0f, 0.6f, 0.02f), poster);
                Deco(t, "DummyAdWire", new Vector3(-0.45f, adY + 0.3f + wireLen * 0.5f + 0.09f, z), new Vector3(0.015f, wireLen + 0.18f, 0.015f), chrome);
                Deco(t, "DummyAdWire", new Vector3(0.45f, adY + 0.3f + wireLen * 0.5f + 0.09f, z), new Vector3(0.015f, wireLen + 0.18f, 0.015f), chrome);
            }

            return new[] { "ad" };
        }

        /// <summary>研究所応接室：研究員の机5人分／研究概要・研究メンバー表</summary>
        private static string[] FurnishLab(Transform t, float hw, float hd, float h)
        {
            var deskMat = GetMat("LP_LabDesk", new Color(0.62f, 0.6f, 0.58f), 0.3f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            var board = GetMat("LP_Board", new Color(0.9f, 0.89f, 0.86f), 0.1f);

            // 研究員の机 5人分（3+2の島）
            var deskPos = new[]
            {
                new Vector3(-hw + 1.8f, 0f, 2.4f), new Vector3(-hw + 1.8f, 0f, 0.4f), new Vector3(-hw + 1.8f, 0f, -1.6f),
                new Vector3(hw - 1.8f, 0f, 1.6f), new Vector3(hw - 1.8f, 0f, -0.8f),
            };
            var deskNames = new[] { "佐伯", "水野", "黒田", "", "" };
            for (int i = 0; i < deskPos.Length; i++)
            {
                // Blender製ワークステーション＋椅子（無ければ従来の机）
                var d = Prop(t, "Workstation", deskPos[i]) ?? Desk(t, $"ResearcherDesk_{i + 1}", deskPos[i], deskMat);
                d.name = $"ResearcherDesk_{i + 1}";
                Prop(t, "OfficeChair", deskPos[i] + new Vector3(0f, 0f, -0.8f), (i * 37) % 30 - 15f);
                if (!string.IsNullOrEmpty(deskNames[i]))
                    TextPlate(d.transform, deskNames[i], new Vector3(0f, 0.78f, -0.34f), 0f,
                        new Color(0.25f, 0.25f, 0.3f), 0.022f);
                // 3番目の机に研究概要を置く（キーボードとモニター台を避けて端に）
                if (i == 2)
                    Findable(d.transform, "summary", "研究概要", new Vector3(-0.45f, 0.76f, 0.0f), paper,
                        new Vector3(0.4f, 0.02f, 0.3f),
                        "研究概要",
                        "《小川脳神経総合研究所 研究概要》\n\n" +
                        "目的: 損傷した記憶領域をAIで補完し、\n" +
                        "　　　人格の連続性を維持する\n\n" +
                        "手順:\n" +
                        "　1. 被験体の記憶を回廊構造として写像\n" +
                        "　2. 欠損区画にAI生成の記憶を移植\n" +
                        "　3. 覚醒後、齟齬の有無を観察\n\n" +
                        "※第4項以降は破棄されている");
            }

            // 研究メンバー表（北壁のボード。写真4人分・うち3人は判読不能）
            var mb = new GameObject("MemberBoard"); mb.transform.SetParent(t, false);
            mb.transform.localPosition = new Vector3(0f, 0f, hd - 0.3f);
            var plate = Findable(mb.transform, "members", "社員名簿", new Vector3(0f, 1.6f, 0f), board,
                new Vector3(2.7f, 1.2f, 0.06f),
                "社員名簿",
                "《社員名簿》\n\n" +
                "　佐伯 恒一　主任研究員補佐（写真あり）\n" +
                "　水野 美奈　臨床研究員　　［顔・判読不能］\n" +
                "　黒田 恒一　上席研究員　　［顔・判読不能］\n" +
                "　──── 　所長　　　　　［顔・判読不能］\n\n" +
                "3人分の顔写真は薬品で溶かされたように\n" +
                "黒く滲んでいる。無事なのは佐伯、ただ一人。\n" +
                "……この顔、どこかで。");
            // 顔写真4枚（佐伯以外の3枚は黒く潰れている）
            var photo = GetMat("LP_Photo", new Color(0.75f, 0.74f, 0.7f), 0.1f);
            var blacked = GetMat("LP_PhotoBlacked", new Color(0.08f, 0.07f, 0.07f), 0.05f);
            for (int i = 0; i < 4; i++)
            {
                float x = -0.75f + i * 0.5f;
                Box(plate.transform, $"Photo_{i}", new Vector3(x, 0.18f, -0.04f),
                    new Vector3(0.34f, 0.4f, 0.02f), i == 0 ? photo : blacked);
            }
            TextPlate(plate.transform, "社 員 名 簿", new Vector3(0f, -0.42f, -0.04f), 180f,
                new Color(0.2f, 0.2f, 0.25f), 0.03f);

            return new[] { "summary", "members" };
        }

        // ============================== 1章 佐伯恒一 ==============================

        /// <summary>第8研究室 脳神経解析室（1章転）：モニター群・作業台・職員証・異常レポート</summary>
        private static string[] FurnishAnalysis(Transform t, float hw, float hd, float h)
        {
            var deskMat = GetMat("LP_LabDesk", new Color(0.62f, 0.6f, 0.58f), 0.3f);
            var dark = GetMat("LP_AnalysisRack", new Color(0.2f, 0.21f, 0.24f), 0.35f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            var cardMat = GetMat("LP_IdCard", new Color(0.85f, 0.87f, 0.9f), 0.3f);
            var screen = EmissiveMat("LP_AnalysisScreen", new Color(0.12f, 0.22f, 0.38f),
                                     new Color(0.25f, 0.55f, 1f) * 0.8f);
            var trim = GetMat("LP_FacilityTrim", new Color(0.45f, 0.47f, 0.5f), 0.3f);
            var glass = GlassMat("LP_PartitionGlass", new Color(0.7f, 0.85f, 0.95f, 0.18f));

            // 北壁のモニターバンク（脳断層画像の光＝部屋の主光源）。画面は分割して並べる
            for (int i = 0; i < 3; i++)
            {
                float x = -2.2f + i * 2.2f;
                Box(t, "MonitorRack", new Vector3(x, 1.3f, hd - 0.35f), new Vector3(1.8f, 2.2f, 0.3f), dark);
                foreach (float sx in new[] { -0.4f, 0.4f })
                    foreach (float sy in new[] { 1.2f, 1.85f })
                        Deco(t, "MonitorScreen", new Vector3(x + sx, sy, hd - 0.48f), new Vector3(0.7f, 0.5f, 0.02f), screen);
            }

            // 東側：研究員のワークステーション2台（配電盤を避けて前後に）。Blender製、無ければ箱
            foreach (float z in new[] { -2.8f, 2.8f })
            {
                if (Prop(t, "Workstation", new Vector3(hw - 1.2f, 0f, z)) == null)
                {
                    var ws = Desk(t, "Workstation", new Vector3(hw - 1.2f, 0f, z), deskMat);
                    Box(ws.transform, "Tower", new Vector3(0.5f, 0.25f, 0.1f), new Vector3(0.2f, 0.45f, 0.45f), dark);
                    Box(ws.transform, "Monitor", new Vector3(-0.1f, 1.0f, 0.2f), new Vector3(0.55f, 0.36f, 0.05f), dark);
                    Deco(ws.transform, "MonitorFace", new Vector3(-0.1f, 1.0f, 0.17f), new Vector3(0.5f, 0.3f, 0.01f), screen);
                    Deco(ws.transform, "Keyboard", new Vector3(-0.1f, 0.765f, -0.15f), new Vector3(0.4f, 0.02f, 0.14f), dark);
                }
                if (Prop(t, "OfficeChair", new Vector3(hw - 1.2f, 0f, z - 0.8f), z > 0 ? -20f : 10f) == null)
                    Box(t, "Chair", new Vector3(hw - 1.2f, 0.24f, z - 0.75f), new Vector3(0.45f, 0.48f, 0.45f), dark);
            }

            // 南西に観察用のガラスパーティション（奥の小部屋が透けて見える）
            Deco(t, "PartitionFrame", new Vector3(-hw + 2.4f, 1.3f, -hd + 2.0f), new Vector3(0.06f, 2.6f, 0.06f), trim);
            Deco(t, "PartitionGlass", new Vector3(-hw + 1.2f, 1.3f, -hd + 2.0f), new Vector3(2.3f, 2.4f, 0.03f), glass);
            Deco(t, "PartitionRail", new Vector3(-hw + 1.2f, 2.55f, -hd + 2.0f), new Vector3(2.4f, 0.08f, 0.08f), trim);

            // 天井ダクトとケーブルトレイ
            Duct(t, new Vector3(-2.0f, h - 0.35f, 0f), hd * 2f - 0.6f, 0.4f, trim);
            Deco(t, "CableTray", new Vector3(2.2f, h - 0.15f, 0f), new Vector3(0.3f, 0.06f, hd * 2f - 0.6f), dark);
            var glow = new GameObject("MonitorGlow");
            glow.transform.SetParent(t, false);
            glow.transform.localPosition = new Vector3(0f, 1.8f, hd - 1.2f);
            var gl = glow.AddComponent<Light>();
            gl.type = LightType.Point; gl.color = new Color(0.4f, 0.6f, 1f);
            gl.intensity = 1.6f; gl.range = 8f;

            // 中央の作業台：脳模型と計測ヘッドギア
            var table = new GameObject("WorkTable"); table.transform.SetParent(t, false);
            table.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            Box(table.transform, "Top", new Vector3(0f, 0.85f, 0f), new Vector3(2.2f, 0.08f, 1.1f), deskMat);
            Box(table.transform, "Base", new Vector3(0f, 0.4f, 0f), new Vector3(1.8f, 0.8f, 0.9f), dark);
            var brain = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            brain.name = "BrainModel"; brain.transform.SetParent(table.transform, false);
            brain.transform.localPosition = new Vector3(-0.6f, 1.05f, 0f);
            brain.transform.localScale = new Vector3(0.3f, 0.26f, 0.34f);
            brain.GetComponent<Renderer>().sharedMaterial = GetMat("LP_BrainModel", new Color(0.8f, 0.6f, 0.6f), 0.4f);
            Object.DestroyImmediate(brain.GetComponent<Collider>());
            Deco(table.transform, "HeadGear", new Vector3(0.55f, 0.98f, 0.1f), new Vector3(0.4f, 0.18f, 0.4f), dark);

            // 西壁の施錠キャビネット
            for (int i = 0; i < 3; i++)
                Box(t, "Cabinet", new Vector3(-hw + 0.35f, 1.0f, -2.0f + i * 1.4f), new Vector3(0.5f, 2.0f, 1.2f), dark);

            // 職員証（作業台の上＝佐伯のIDがなぜかここに残されている）
            Findable(table.transform, "idcard", "職員証", new Vector3(-0.05f, 0.9f, 0.35f), cardMat,
                new Vector3(0.13f, 0.01f, 0.09f),
                "職員証（佐伯恒一）",
                "《職員証》　小川脳神経総合研究所\n\n" +
                "　氏名: 佐伯 恒一\n" +
                "　役職: 主任研究員補佐\n" +
                "　担当: 補完アルゴリズム\n\n" +
                "名簿で唯一顔が残っていた男。\n" +
                "そして──あの異形と、同じ顔。");

            // 研究計画書・補完仕様（作業台の上）
            Findable(table.transform, "plan", "研究計画書", new Vector3(-0.1f, 0.9f, -0.3f), paper,
                new Vector3(0.4f, 0.02f, 0.3f),
                "リナシータ研究計画書",
                "《RENASCITA 研究計画書 表紙》\n\n" +
                "対象: 遷延性意識障害（いわゆる植物状態）\n" +
                "方針: 損傷した脳の記憶・人格野を\n" +
                "　　　外部より補完し、意識の再構成を促す\n\n" +
                "──リナシータ。イタリア語で「再誕」。\n" +
                "この響きを、私は知っている。\n" +
                "祈るように口にしたことが、ある。");
            Findable(table.transform, "spec", "アルゴリズム仕様", new Vector3(0.75f, 0.9f, -0.25f), paper,
                new Vector3(0.32f, 0.02f, 0.24f),
                "補完アルゴリズム仕様（佐伯）",
                "《補完アルゴリズム 概略》　担当: 佐伯\n\n" +
                "記憶は「傾向」で分類される。\n" +
                "　○ 感情　△ 言語　□ 行動\n\n" +
                "欠損した記憶は、残された記憶の傾向から\n" +
                "「その人らしい記憶」を推定して埋める。\n" +
                "　→ 規則: 欠損区画には、経路上の両隣と\n" +
                "　　 同じ傾向を置く。異なる傾向を置くと\n" +
                "　　 光が濁り、補完は失敗する。\n\n" +
                "欄外の書き込み:\n" +
                "「補完された記憶は、本当に本人のものか？」\n" +
                "「本人を本人たらしめるものは、何だ」");

            // 記憶回路端末（西のモニターラック）。仕様を読んだ人だけが迷わず通せる
            var circuit = Lock<LoopCircuitLock>(t, "CircuitTerminal", new Vector3(-2.2f, 1.2f, hd - 0.49f),
                new Vector3(0.9f, 0.6f, 0.04f), EmissiveMat("LP_CircuitScreen", new Color(0.35f, 0.25f, 0.1f), new Color(1f, 0.7f, 0.3f) * 0.8f),
                "analysis", "circuit", "記憶回路端末", "analysis_spec");
            circuit.Title = "記憶回路　── 補完テスト ──";
            circuit.Body = "提供体の記憶（左）を対象者（右）へ通す。回転で経路を作り、欠損区画（?）には傾向を補完すること。";
            circuit.Level = LoopCircuitLock.Level5x5();
            circuit.SuccessNoteTitle = "記憶回路：補完テスト成功";
            circuit.SuccessNoteBody =
                "欠損区画に両隣と同じ傾向を置くと、光は通った。\n" +
                "──仕様どおりだ。私はこの手順を、何度もやった気がする。\n\n" +
                "通した記憶は、異常レポートの言う\n" +
                "「存在しないはずの記憶」として登録された。";
            circuit.Lamp = circuit.GetComponent<Renderer>();
            TextPlate(t, "MEMORY CIRCUIT", new Vector3(-2.2f, 1.62f, hd - 0.49f), 180f, new Color(1f, 0.75f, 0.4f), 0.022f);

            // 異常レポート（モニターの前。読了で1章転の核心）
            Findable(t, "anomaly", "異常レポート", new Vector3(0f, 1.5f, hd - 0.5f), screen,
                new Vector3(0.6f, 0.4f, 0.04f),
                "異常レポート（佐伯→所長）",
                "《異常報告》　報告者: 佐伯\n\n" +
                "・存在しないはずの記憶が生成されている\n" +
                "・システムが研究員自身の脳情報で\n" +
                "　学習している可能性がある\n" +
                "・至急、運用の停止を進言する\n\n" +
                "所長からの返信は、一行だけ。\n" +
                "「今は結論を出すな」\n\n" +
                "……私は、ここで働いていた。思い出してきた。");

            return new[] { "idcard", "plan", "spec", "anomaly", "circuit" };
        }

        /// <summary>佐伯の自宅（1章結）：二人分のコーヒー・妻の手紙・残響〈口論・佐伯視点〉</summary>
        private static string[] FurnishSaekiHome(Transform t, float hw, float hd, float h)
        {
            var wood = GetMat("LP_Furniture", new Color(0.4f, 0.3f, 0.22f), 0.2f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            var cup = GetMat("LP_CoffeeCup", new Color(0.92f, 0.9f, 0.86f), 0.5f);

            // 夕暮れの橙の光（カーテン越し）
            var dusk = new GameObject("DuskLight");
            dusk.transform.SetParent(t, false);
            dusk.transform.localPosition = new Vector3(hw - 0.8f, 1.8f, 1.5f);
            var dl = dusk.AddComponent<Light>();
            dl.type = LightType.Point; dl.color = new Color(1f, 0.6f, 0.35f);
            dl.intensity = 1.8f; dl.range = 7f;

            // ダイニングテーブル：誰も座らない二脚と、湯気の立たない二杯
            var dining = new GameObject("DiningTable"); dining.transform.SetParent(t, false);
            dining.transform.localPosition = new Vector3(1.4f, 0f, 1.2f);
            Box(dining.transform, "Top", new Vector3(0f, 0.72f, 0f), new Vector3(1.3f, 0.05f, 0.9f), wood);
            Box(dining.transform, "Leg", new Vector3(0f, 0.36f, 0f), new Vector3(0.12f, 0.72f, 0.12f), wood);
            if (Prop(dining.transform, "DiningChair", new Vector3(0f, 0f, -0.75f), 0f) == null)
                Box(dining.transform, "Chair_A", new Vector3(0f, 0.24f, -0.75f), new Vector3(0.45f, 0.48f, 0.45f), wood);
            if (Prop(dining.transform, "DiningChair", new Vector3(0f, 0f, 0.75f), 180f) == null)
                Box(dining.transform, "Chair_B", new Vector3(0f, 0.24f, 0.75f), new Vector3(0.45f, 0.48f, 0.45f), wood);
            foreach (float z in new[] { -0.25f, 0.25f })
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                c.name = "CoffeeCup"; c.transform.SetParent(dining.transform, false);
                c.transform.localPosition = new Vector3(0f, 0.78f, z);
                c.transform.localScale = new Vector3(0.09f, 0.05f, 0.09f);
                c.GetComponent<Renderer>().sharedMaterial = cup;
                Object.DestroyImmediate(c.GetComponent<Collider>());
            }

            // 整然とした本棚（背表紙の色が几帳面に揃っている）と書き物机
            Box(t, "Bookshelf", new Vector3(-hw + 0.3f, 1.05f, 1.6f), new Vector3(0.45f, 2.1f, 2.4f), wood);
            var bookColors = new[] { new Color(0.35f, 0.25f, 0.2f), new Color(0.2f, 0.3f, 0.35f), new Color(0.5f, 0.45f, 0.35f) };
            for (int row = 0; row < 4; row++)
                for (int k = 0; k < 9; k++)
                    Deco(t, "Book", new Vector3(-hw + 0.38f, 0.35f + row * 0.48f, 0.5f + k * 0.25f),
                        new Vector3(0.28f, 0.3f + (k % 3) * 0.04f, 0.2f),
                        GetMat($"LP_Book_{k % 3}", bookColors[k % 3], 0.15f));
            var desk = Desk(t, "Desk", new Vector3(-hw + 1.2f, 0f, -1.8f), wood);
            if (Prop(t, "DiningChair", new Vector3(-hw + 1.2f, 0f, -2.6f), 0f) == null)
                Box(t, "DeskChair", new Vector3(-hw + 1.2f, 0.24f, -2.55f), new Vector3(0.45f, 0.48f, 0.45f), wood);
            Deco(desk.transform, "DeskLamp", new Vector3(0.5f, 0.95f, 0.2f), new Vector3(0.2f, 0.12f, 0.2f),
                EmissiveMat("LP_DeskLampShade", new Color(0.3f, 0.35f, 0.3f), new Color(1f, 0.85f, 0.6f) * 0.8f));

            // 東の窓（夕暮れの光がここから差す）、ラグ、ソファ、壁の時計
            Window(t, new Vector3(hw - 0.08f, 1.5f, 1.5f), 90f, 1.4f, 1.2f, true, new Color(0.7f, 0.6f, 0.5f));
            Rug(t, new Vector3(1.4f, 0f, 1.2f), new Vector2(2.4f, 2.0f), new Color(0.45f, 0.3f, 0.25f));
            var sofa = new GameObject("Sofa"); sofa.transform.SetParent(t, false);
            sofa.transform.localPosition = new Vector3(-2.0f, 0f, hd - 0.55f);
            var sofaMat = GetMat("LP_Sofa", new Color(0.35f, 0.32f, 0.3f), 0.1f);
            Box(sofa.transform, "Seat", new Vector3(0f, 0.22f, 0f), new Vector3(1.8f, 0.44f, 0.8f), sofaMat);
            Box(sofa.transform, "Back", new Vector3(0f, 0.6f, 0.3f), new Vector3(1.8f, 0.5f, 0.2f), sofaMat);
            Box(sofa.transform, "Arm", new Vector3(-0.85f, 0.5f, 0f), new Vector3(0.1f, 0.3f, 0.8f), sofaMat);
            Box(sofa.transform, "Arm", new Vector3(0.85f, 0.5f, 0f), new Vector3(0.1f, 0.3f, 0.8f), sofaMat);
            var clockGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            clockGo.name = "WallClock"; clockGo.transform.SetParent(t, false);
            clockGo.transform.localPosition = new Vector3(-1.0f, 2.0f, hd - 0.09f);
            clockGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            clockGo.transform.localScale = new Vector3(0.36f, 0.015f, 0.36f);
            clockGo.GetComponent<Renderer>().sharedMaterial = cup;
            Object.DestroyImmediate(clockGo.GetComponent<Collider>());

            Findable(dining.transform, "letter", "書きかけの手紙", new Vector3(0.4f, 0.755f, 0f), paper,
                new Vector3(0.24f, 0.01f, 0.16f),
                "妻の書きかけの手紙",
                "《便箋》\n\n" +
                "「お母さんへ。恒一さんのことで相談があります。\n" +
                "　最近、帰りがとても遅いんです。\n" +
                "　何を聞いても『大丈夫だ』としか──\n" +
                "　昨日は夜中に、書斎で一人で\n" +
                "　誰かに謝っているのが聞こえました」\n\n" +
                "手紙は、そこで終わっている。\n" +
                "投函されることは、なかったんだろう。");
            Findable(desk.transform, "plog", "私的ログ", new Vector3(-0.2f, 0.76f, 0.05f), paper,
                new Vector3(0.3f, 0.02f, 0.22f),
                "佐伯の私的ログ（最終頁）",
                "《私的記録 最終頁》\n\n" +
                "「所長は動かない。黒田さんは正論しか言わない。\n" +
                "　なら、私が直接あの人と話すしかない。\n" +
                "　悪い人ではないんだ。ただ、追い詰められている。\n" +
                "　今夜、解析室で会う約束をした」\n\n" +
                "最後の行は、筆圧が乱れている。\n\n" +
                "「……何をするつもりですか？」\n\n" +
                "記録は、そこで途切れていた。");
            Findable(desk.transform, "unsent", "未送信メモ", new Vector3(0.35f, 0.755f, -0.15f), paper,
                new Vector3(0.2f, 0.01f, 0.14f),
                "宛先のないメモ",
                "《走り書き》\n\n" +
                "「あなたに伝えたいことがある。\n" +
                "　ここから先へ行けば、もう戻れなくなる。\n" +
                "　娘さんのことは、別の道を一緒に探せるはずだ」\n\n" +
                "宛名は書かれていない。\n" +
                "──いや。書けなかったのか。\n" +
                "この「あなた」とは、誰のことだ。");

            // 残響〈口論・佐伯視点〉＝冷静な対話に見える
            Echo(t, "argue_saeki", "残響：口論（佐伯の記憶）",
                "居間の隅で、二つの影が距離を取って向かい合っている。\n" +
                "声は低く抑えられ、時折、片方が静かにうなずく。\n" +
                "激した言葉はどこにもない。──冷静な、議論。\n" +
                "佐伯の記憶の中では、そうだった。",
                2.2f, 0.85f, 0.3f,
                (new Vector3(-1.2f, 0f, -0.6f), 90f, 1.0f, 2f, 1f, false),
                (new Vector3(0.6f, 0f, -0.6f), -90f, 1.0f, -2f, 0.8f, false));

            // 残響〈二人分の食器〉＝妻の日常
            Echo(t, "saeki_wife", "残響：二人分の食器（妻の記憶）",
                "テーブルの脇に、女性の影が立っている。\n" +
                "カップを二つ置き、一つを向かいの席へ。\n" +
                "そのまま、じっと待っている。誰も、座らない。\n" +
                "口論も研究も知らない家族の時間が、ここで止まっている。",
                1.4f, 1.15f, 0.16f,
                (new Vector3(2.3f, 0f, 1.2f), -90f, 0.92f, 1f, 1f, false));

            return new[] { "letter", "plog", "unsent" };
        }

        // ============================== 2章 水野美奈 ==============================

        /// <summary>臨床病棟（2章起）：空のベッド列・娘のファイル・残響〈最後の会話・主人公視点〉</summary>
        private static string[] FurnishWard(Transform t, float hw, float hd, float h)
        {
            var frameM = MetalMat("LP_WardFrame", new Color(0.75f, 0.77f, 0.8f), 0.6f, 0.8f);
            var sheet = GetMat("LP_WardSheet", new Color(0.92f, 0.93f, 0.95f), 0.1f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            var monitorBody = GetMat("LP_WardMonitorBody", new Color(0.8f, 0.82f, 0.82f), 0.3f);
            var monitor = EmissiveMat("LP_WardMonitor", new Color(0.05f, 0.1f, 0.07f),
                                      new Color(0.2f, 1f, 0.35f) * 0.9f);
            var curtainM = GetMat("LP_WardCurtain", new Color(0.72f, 0.82f, 0.76f), 0.05f);
            var blanket = GetMat("LP_WardBlanket", new Color(0.7f, 0.78f, 0.8f), 0.1f);

            // ベッド2列×3（すべて空。シーツは整えられ、間仕切りカーテンは半分引かれている）
            for (int col = 0; col < 2; col++)
                for (int i = 0; i < 3; i++)
                {
                    float sx = col == 0 ? -1f : 1f;
                    float x = sx * (hw - 1.6f);
                    float z = -hd + 2.5f + i * 3.2f;
                    var bed = new GameObject($"Bed_{col}_{i}");
                    bed.transform.SetParent(t, false);
                    bed.transform.localPosition = new Vector3(x, 0f, z);
                    // Blender製の病院ベッド（頭側＝-Z）。無ければ従来の箱で代替
                    if (Prop(bed.transform, "HospitalBed", Vector3.zero) == null)
                    {
                        Box(bed.transform, "Frame", new Vector3(0f, 0.3f, 0f), new Vector3(1.0f, 0.6f, 2.1f), frameM);
                        Box(bed.transform, "Sheet", new Vector3(0f, 0.66f, 0f), new Vector3(0.95f, 0.12f, 2.0f), sheet);
                        Deco(bed.transform, "Blanket", new Vector3(0f, 0.735f, 0.3f), new Vector3(0.9f, 0.03f, 1.3f), blanket);
                        Box(bed.transform, "Pillow", new Vector3(0f, 0.76f, -0.8f), new Vector3(0.5f, 0.08f, 0.3f), sheet);
                        Deco(bed.transform, "HeadBoard", new Vector3(0f, 0.85f, -1.02f), new Vector3(1.0f, 0.5f, 0.04f), frameM);
                    }
                    // 点滴スタンドと生体モニター（緑の光点だけが瞬く）
                    if (Prop(bed.transform, "IVStand", new Vector3(sx * -0.72f, 0f, -0.7f), 0f, false) == null)
                    {
                        Deco(bed.transform, "IvPole", new Vector3(sx * -0.6f, 0.9f, -0.7f), new Vector3(0.03f, 1.8f, 0.03f), frameM);
                        Deco(bed.transform, "IvBag", new Vector3(sx * -0.6f, 1.6f, -0.7f), new Vector3(0.1f, 0.18f, 0.05f), sheet);
                    }
                    Deco(bed.transform, "MonitorArm", new Vector3(sx * -0.62f, 0.7f, 0.6f), new Vector3(0.03f, 0.9f, 0.03f), frameM);
                    Deco(bed.transform, "Monitor", new Vector3(sx * -0.62f, 1.2f, 0.6f), new Vector3(0.28f, 0.22f, 0.2f), monitorBody);
                    Deco(bed.transform, "MonitorFace", new Vector3(sx * -0.76f, 1.2f, 0.6f), new Vector3(0.01f, 0.16f, 0.16f), monitor);
                    // 間仕切りカーテン（天井レールから。通路側を半分だけ隠す）
                    Deco(bed.transform, "CurtainRail", new Vector3(sx * -0.75f, h - 0.25f, 0f), new Vector3(0.03f, 0.03f, 2.6f), frameM);
                    Deco(bed.transform, "Curtain", new Vector3(sx * -0.75f, (h - 0.3f + 0.4f) * 0.5f, 0.75f), new Vector3(0.05f, h - 0.7f, 1.1f), curtainM);
                    // ベッド脇の窓（ブラインド）：外周側の壁
                    Window(t, new Vector3(sx * (hw - 0.08f), 1.7f, z), sx > 0 ? 90f : -90f, 1.4f, 1.0f, false);
                }
            // 天井ダクト（施設の気配）
            Duct(t, new Vector3(0f, h - 0.4f, 0f), hd * 2f - 0.8f, 0.35f, frameM);

            // 奥の窓際のベッドにだけ、柔らかい陽だまり
            var sun = new GameObject("Sunbeam");
            sun.transform.SetParent(t, false);
            sun.transform.localPosition = new Vector3(hw - 1.6f, 2.0f, hd - 3.1f);
            var sl = sun.AddComponent<Light>();
            sl.type = LightType.Point; sl.color = new Color(1f, 0.9f, 0.7f);
            sl.intensity = 2.0f; sl.range = 5f;

            // ナースデスク（入口近く。スポーンの正面を塞がないよう脇に寄せる）
            var nurse = Prop(t, "Workstation", new Vector3(1.8f, 0f, -hd + 1.6f))
                        ?? Desk(t, "NurseDesk", new Vector3(1.8f, 0f, -hd + 1.6f), frameM);
            Prop(t, "OfficeChair", new Vector3(1.8f, 0f, -hd + 0.85f));
            Findable(nurse.transform, "obs", "観察記録", new Vector3(-0.45f, 0.76f, 0.0f), paper,
                new Vector3(0.36f, 0.02f, 0.26f),
                "水野の患者観察記録",
                "《患者観察記録》　記録者: 水野\n\n" +
                "・301号 佐々木さん　今日は目元が和らいでいた\n" +
                "・302号 田中さん　娘さんの面会。手を握ると脈が上がる\n" +
                "・303号 ──\n\n" +
                "どの患者も、番号ではなく名前で呼ばれている。\n" +
                "欄外には小さな似顔絵まで。\n" +
                "……この人は、患者を「症例」と思っていない。");

            // 患者番号の無い少女のファイル（陽だまりのベッドの上）
            Findable(t, "girlfile", "少女のファイル", new Vector3(hw - 1.6f, 0.75f, hd - 3.1f), paper,
                new Vector3(0.3f, 0.02f, 0.22f),
                "患者番号の無いファイル",
                "《診療記録》　患者番号: ──\n\n" +
                "写真は水に濡れたように滲んで、顔が見えない。\n" +
                "年齢の欄も、名前の欄も、読めない。\n\n" +
                "特記事項だけが、はっきり残っている。\n" +
                "「ご家族: 父。毎日面会に来られる」\n\n" +
                "──娘だ。\n" +
                "私には、娘がいる。ここに、預けた。");

            // 残響〈最後の会話・主人公視点〉＝穏やかな場面
            Echo(t, "talk_me", "残響：会話（私の記憶）",
                "ベッドの傍らで、二つの影が静かに話している。\n" +
                "「……娘を、助けたいんです」\n" +
                "片方の影がそう言うと、もう片方──小柄な影は\n" +
                "微笑むように、ゆっくりうなずいた。\n" +
                "温かい場面だ。私はそう、覚えている。",
                1.8f, 1.0f, 0.28f,
                (new Vector3(hw - 3.0f, 0f, hd - 4.0f), 60f, 1.0f, 2f, 0.8f, false),
                (new Vector3(hw - 2.4f, 0f, hd - 4.8f), -120f, 0.92f, -2f, 1f, false));

            return new[] { "obs", "girlfile" };
        }

        /// <summary>CORE前室（2章転）：隔壁扉・死んだ端末・BRAIN DATA一覧・未送信メッセージ</summary>
        private static string[] FurnishCoreAnte(Transform t, float hw, float hd, float h)
        {
            var metal = MetalMat("LP_CoreMetal", new Color(0.35f, 0.37f, 0.4f), 0.5f, 0.7f);
            var dark = GetMat("LP_AnalysisRack", new Color(0.2f, 0.21f, 0.24f), 0.35f);
            var deadScreen = GetMat("LP_DeadScreen", new Color(0.05f, 0.05f, 0.07f), 0.6f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            var warn = EmissiveMat("LP_CoreWarn", new Color(0.6f, 0.15f, 0.12f), new Color(1f, 0.2f, 0.15f) * 1.2f);

            // 正面（北壁）: MAIN COREへ続く分厚い隔壁（出口扉を枠のように囲む。通行は塞がない）
            foreach (float xs in new[] { -1.2f, 1.2f })
                Box(t, "BulkheadPillar", new Vector3(xs, 1.5f, hd - 0.45f), new Vector3(1.0f, 3.0f, 0.5f), metal);
            Box(t, "BulkheadLintel", new Vector3(0f, 2.85f, hd - 0.45f), new Vector3(3.4f, 0.5f, 0.5f), metal);
            Deco(t, "BulkheadLamp", new Vector3(0f, 2.55f, hd - 0.68f), new Vector3(0.5f, 0.12f, 0.05f), warn);
            TextPlate(t, "MAIN CORE", new Vector3(0f, 2.35f, hd - 0.72f), 180f, new Color(0.9f, 0.3f, 0.25f), 0.04f);

            // 非常灯の赤だけが壁を舐める（器具は壁の高い位置に）
            foreach (float z in new[] { -2.0f, 2.0f })
            {
                var em = new GameObject("EmergencyLight");
                em.transform.SetParent(t, false);
                em.transform.localPosition = new Vector3(hw - 0.6f, h - 0.6f, z);
                var el = em.AddComponent<Light>();
                el.type = LightType.Point; el.color = new Color(1f, 0.25f, 0.18f);
                el.intensity = 1.6f; el.range = 8f;
                Deco(t, "EmergencyLamp", new Vector3(hw - 0.12f, h - 0.5f, z), new Vector3(0.12f, 0.16f, 0.3f), warn);
                Deco(t, "EmergencyLampBase", new Vector3(hw - 0.09f, h - 0.5f, z), new Vector3(0.06f, 0.22f, 0.4f), dark);
            }
            // 天井のダクト2本と壁面のケーブルトレイ、床の注意ライン、隅の配管
            Duct(t, new Vector3(-1.8f, h - 0.4f, 0f), hd * 2f - 0.6f, 0.45f, dark);
            Duct(t, new Vector3(1.8f, h - 0.4f, 0f), hd * 2f - 0.6f, 0.45f, dark);
            Deco(t, "CableTray", new Vector3(-hw + 0.25f, 2.3f, 0f), new Vector3(0.3f, 0.06f, hd * 2f - 0.4f), dark);
            var hazard = GetMat("LP_HazardYellow", new Color(0.85f, 0.7f, 0.15f), 0.2f);
            for (float x = -2.2f; x <= 2.2f; x += 0.8f)
                Deco(t, "HazardStripe", new Vector3(x, 0.004f, hd - 1.4f), new Vector3(0.4f, 0.008f, 0.6f), hazard);
            foreach (float x in new[] { -hw + 0.3f, hw - 0.3f })
            {
                var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pipe.name = "Pipe"; pipe.transform.SetParent(t, false);
                pipe.transform.localPosition = new Vector3(x, h * 0.5f, -hd + 0.4f);
                pipe.transform.localScale = new Vector3(0.18f, h * 0.5f, 0.18f);
                pipe.GetComponent<Renderer>().sharedMaterial = metal;
                Object.DestroyImmediate(pipe.GetComponent<Collider>());
            }
            // 隔壁のリベット
            foreach (float xs in new[] { -1.2f, 1.2f })
                for (int k = 0; k < 4; k++)
                    Deco(t, "Rivet", new Vector3(xs, 0.5f + k * 0.7f, hd - 0.72f), new Vector3(0.08f, 0.08f, 0.04f), dark);

            // 端末デスク（モニターは全て暗転＝画面マテリアルを消灯に差し替え）と倒れた椅子
            for (int i = 0; i < 3; i++)
            {
                var tpos = new Vector3(-hw + 1.2f, 0f, -2.2f + i * 1.9f);
                if (Prop(t, "Workstation", tpos, 0f, true, deadScreen) == null)
                {
                    var d = Desk(t, $"Terminal_{i}", tpos, dark);
                    Box(d.transform, "Screen", new Vector3(0f, 1.0f, 0.2f), new Vector3(0.5f, 0.32f, 0.05f), deadScreen);
                }
            }
            Prop(t, "OfficeChair", new Vector3(-hw + 1.2f, 0f, 1.6f - 0.8f), -25f);
            var fallen = Prop(t, "OfficeChair", new Vector3(-hw + 2.2f, 0.3f, -0.5f), 0f, false);
            if (fallen != null)
                fallen.transform.localRotation = Quaternion.Euler(0f, 30f, 100f);
            else
            {
                fallen = new GameObject("FallenChair");
                fallen.transform.SetParent(t, false);
                fallen.transform.localPosition = new Vector3(-hw + 2.2f, 0.25f, -0.5f);
                fallen.transform.localRotation = Quaternion.Euler(0f, 30f, 100f);
                Box(fallen.transform, "Seat", Vector3.zero, new Vector3(0.45f, 0.5f, 0.45f), dark);
            }

            // 資料（v2: 手順書・一覧・発見メモ・未送信メッセージ）
            Findable(t, "manual", "運用手順書", new Vector3(-hw + 0.75f, 0.76f, -2.2f), paper,
                new Vector3(0.34f, 0.02f, 0.26f),
                "リナシータ運用手順書",
                "《運用手順書 抜粋》\n\n" +
                "3-1. 補完には「提供体」の脳情報を用いる。\n" +
                "　　 提供体は健常な成人であること。\n" +
                "3-2. 提供体の記憶・人格情報は登録後、\n" +
                "　　 システム内で保持され続ける。\n" +
                "3-3. 復電時は 記憶野 → 言語野 → 自己認識野 の\n" +
                "　　 順に給電すること。順序を誤ると\n" +
                "　　 人格モデルが不安定化する。\n\n" +
                "……「提供体」。\n" +
                "健常な、成人。それは、どこから？");

            // 配電盤（2章転・順序転記型）: 手順書3-3の順に給電する。3本目で脚本襲撃（水野初登場）
            var panel = Lock<LoopSequenceLock>(t, "PowerPanel", new Vector3(-hw + 0.2f, 1.3f, hd - 1.9f),
                new Vector3(0.14f, 0.9f, 0.7f), metal, "core_ante", "power", "配電盤", "core_ante_manual");
            panel.Title = "配電盤　── 系統別給電 ──";
            panel.Body = "非常電源から各系統へ復電する。給電する系統を順に選ぶこと。";
            panel.Steps = new[] { "言語野", "自己認識野", "記憶野" };
            panel.CorrectOrder = new[] { 2, 0, 1 };
            panel.SuccessNoteTitle = "配電盤：系統復電 完了";
            panel.SuccessNoteBody =
                "記憶野、言語野、自己認識野──手順書どおりの順で給電した。\n" +
                "蛍光灯が一本ずつ点いていく。\n\n" +
                "……仕様を守った。守ったのに。\n" +
                "何かが、目を覚ました気がする。";
            var lamps = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                var lamp = Deco(panel.transform, $"Lamp_{i}", new Vector3(-0.08f, 0.3f - i * 0.22f, 0.15f), new Vector3(0.02f, 0.1f, 0.1f),
                    EmissiveMat("LP_PanelLampOff", new Color(0.5f, 0.1f, 0.1f), new Color(0.6f, 0.1f, 0.1f)));
                lamps[i] = lamp.GetComponent<Renderer>();
                TextPlate(panel.transform, panel.Steps[i], new Vector3(-0.09f, 0.3f - i * 0.22f, -0.15f), 90f, new Color(0.9f, 0.9f, 0.9f), 0.012f);
            }
            panel.StepLamps = lamps;
            Findable(t, "brainlist", "登録一覧", new Vector3(-hw + 0.18f, 1.4f, 0.5f), deadScreen,
                new Vector3(0.06f, 0.5f, 0.8f),
                "BRAIN DATA 登録一覧",
                "《登録一覧》　※非常電源で表示\n\n" +
                "　BRAIN DATA:01　登録者: ▓▓▓▓\n" +
                "　BRAIN DATA:02　登録者: ▓▓▓▓\n" +
                "　BRAIN DATA:03　登録者: ▓▓▓▓\n\n" +
                "登録者の欄は、削除されている。\n" +
                "3件。──なぜだろう。\n" +
                "この数字に、指先が冷たくなる。");
            Findable(t, "wmemo", "発見メモ", new Vector3(-hw + 0.75f, 0.76f, 1.6f), paper,
                new Vector3(0.26f, 0.02f, 0.18f),
                "水野の発見メモ",
                "《メモ》　筆跡: 水野\n\n" +
                "「照合結果。脳情報の一部が、うちの研究員の\n" +
                "　ものと一致する。そんなはずない。\n" +
                "　所長に報告 → 『そのデータには触るな』と。\n" +
                "　どうして？　どうしてみんな、何も言わないの」");
            Findable(t, "unsentmsg", "未送信メッセージ", new Vector3(-hw + 0.18f, 1.4f, 2.4f), deadScreen,
                new Vector3(0.06f, 0.5f, 0.8f),
                "未送信メッセージ（水野）",
                "《送信トレイ》　宛先: 所長\n\n" +
                "「主任、あの人を止めてください。\n" +
                "　私はもう、黙っていられません。\n" +
                "　あの人は間違っています。でも──」\n\n" +
                "本文は、そこで途切れている。\n" +
                "送信されなかった、最後の言葉。\n\n" +
                "「あの人」。……主任が、誰かを使っている？");

            return new[] { "manual", "brainlist", "wmemo", "unsentmsg", "power" };
        }

        /// <summary>水野のアパート（2章結）：暖かい部屋の奥が病室に滲む。残響〈黒い影〉〈叔父の病室〉</summary>
        private static string[] FurnishMizunoApart(Transform t, float hw, float hd, float h)
        {
            var wood = GetMat("LP_Furniture", new Color(0.4f, 0.3f, 0.22f), 0.2f);
            var cloth = GetMat("LP_AptCloth", new Color(0.75f, 0.6f, 0.5f), 0.1f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            var plant = GetMat("LP_Plant", new Color(0.3f, 0.5f, 0.28f), 0.15f);
            var frameM = MetalMat("LP_WardFrame", new Color(0.75f, 0.77f, 0.8f), 0.6f, 0.8f);
            var sheet = GetMat("LP_WardSheet", new Color(0.92f, 0.93f, 0.95f), 0.1f);
            var fusuma = GetMat("LP_Fusuma", new Color(0.85f, 0.8f, 0.68f), 0.15f);

            float split = 1.0f;   // ここから奥が「病室」に変わる

            // ---- 手前: 生活感のあるワンルーム（暖色） ----
            var warm = new GameObject("WarmLight");
            warm.transform.SetParent(t, false);
            warm.transform.localPosition = new Vector3(0f, h - 0.4f, -2.5f);
            var wl = warm.AddComponent<Light>();
            wl.type = LightType.Point; wl.color = new Color(1f, 0.8f, 0.55f);
            wl.intensity = 1.6f; wl.range = 6f;

            var bed = new GameObject("Bed"); bed.transform.SetParent(t, false);
            bed.transform.localPosition = new Vector3(-hw + 0.85f, 0f, -2.6f);
            Box(bed.transform, "Frame", new Vector3(0f, 0.2f, 0f), new Vector3(1.0f, 0.4f, 2.0f), wood);
            Box(bed.transform, "Blanket", new Vector3(0f, 0.48f, 0f), new Vector3(0.95f, 0.16f, 1.9f), cloth);
            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "PlantPot"; pot.transform.SetParent(t, false);
            pot.transform.localPosition = new Vector3(hw - 0.5f, 0.15f, -3.6f);
            pot.transform.localScale = new Vector3(0.3f, 0.15f, 0.3f);
            pot.GetComponent<Renderer>().sharedMaterial = wood;
            Object.DestroyImmediate(pot.GetComponent<Collider>());
            Deco(t, "Plant", new Vector3(hw - 0.5f, 0.65f, -3.6f), new Vector3(0.5f, 0.7f, 0.5f), plant);
            // 小さなチェストの上に写真立て、東の窓にカーテン、床にラグ、壁にコルクボード
            Box(t, "Chest", new Vector3(hw - 0.45f, 0.3f, -2.2f), new Vector3(0.6f, 0.6f, 0.4f), wood);
            Deco(t, "PhotoStand", new Vector3(hw - 0.55f, 0.7f, -2.2f), new Vector3(0.14f, 0.18f, 0.04f), paper);
            Window(t, new Vector3(hw - 0.08f, 1.5f, -1.2f), 90f, 1.0f, 1.1f, true, new Color(0.85f, 0.75f, 0.7f));
            Rug(t, new Vector3(0.4f, 0f, -1.6f), new Vector2(1.6f, 1.3f), new Color(0.6f, 0.5f, 0.45f));
            Deco(t, "CorkBoard", new Vector3(-hw + 0.09f, 1.6f, -2.0f), new Vector3(0.03f, 0.5f, 0.7f),
                GetMat("LP_Cork", new Color(0.7f, 0.55f, 0.35f), 0.1f));
            foreach (float dz in new[] { -0.2f, 0.15f })
                Deco(t, "Pin", new Vector3(-hw + 0.11f, 1.6f + dz * 0.5f, -2.0f + dz), new Vector3(0.01f, 0.16f, 0.12f), paper);

            // ローテーブルと日記
            var lowT = new GameObject("LowTable"); lowT.transform.SetParent(t, false);
            lowT.transform.localPosition = new Vector3(0.4f, 0f, -1.6f);
            Box(lowT.transform, "Top", new Vector3(0f, 0.34f, 0f), new Vector3(0.9f, 0.05f, 0.6f), wood);
            Findable(lowT.transform, "diary", "日記帳", new Vector3(-0.15f, 0.38f, 0f), paper,
                new Vector3(0.24f, 0.02f, 0.18f),
                "水野の日記",
                "《日記》\n\n" +
                "3月2日\n" +
                "「誕生日。同期の子がケーキをくれた。29歳」\n\n" +
                "9月14日\n" +
                "「叔父さんの命日。今年も花を持っていった。\n" +
                "　叔父さんが倒れてから、ずっと決めていた。\n" +
                "　眠っている人を起こす仕事をするって」\n\n" +
                "「最近、あの人の質問が怖い。\n" +
                "　『提供体はどこから来るんですか』って、\n" +
                "　どうしてそんなことばかり聞くんだろう」\n\n" +
                "最終頁。\n" +
                "「……信じたい。あの人はきっと、\n" +
                "　娘さんを助けたいだけの、普通のお父さんのはず」\n\n" +
                "日記のどこにも、「あの人」の名前だけが無い。");

            // 台所カウンターとボイスレコーダー
            var counter = new GameObject("Counter"); counter.transform.SetParent(t, false);
            counter.transform.localPosition = new Vector3(-hw + 0.6f, 0f, -0.2f);
            Box(counter.transform, "Top", new Vector3(0f, 0.85f, 0f), new Vector3(1.0f, 0.06f, 0.8f), fusuma);
            Box(counter.transform, "Base", new Vector3(0f, 0.42f, 0f), new Vector3(0.95f, 0.8f, 0.75f), wood);
            // 水野のノートPC（2章結・転記型）: パスワードは"叔父の"命日 0914
            var laptop = Lock<LoopCodeLock>(counter.transform, "Laptop", new Vector3(-0.28f, 0.9f, -0.15f),
                new Vector3(0.32f, 0.03f, 0.24f), GetMat("LP_Laptop", new Color(0.2f, 0.2f, 0.22f), 0.5f),
                "mizuno_apart", "pcfile", "水野のノートPC", "mizuno_apart_diary");
            laptop.Title = "水野のノートPC";
            laptop.Body = "ログインパスワード（4桁）\nヒント欄: 「忘れられない日」";
            laptop.Length = 4; laptop.Answer = "0914";
            laptop.SuccessNoteTitle = "水野のPC：第12研究室 入室情報";
            laptop.SuccessNoteBody =
                "デスクトップに、ひとつだけファイル。\n" +
                "「第12研究室 入室ID: MIZ-0417 / 仮パス: 8821」\n" +
                "作成日は、彼女の最後の日。\n\n" +
                "外部の窓口に持っていくつもりだったのだろう。\n" +
                "この鍵で、SYSTEM ROOM に入れる。";
            Deco(laptop.transform, "Lid", new Vector3(0f, 0.11f, 0.11f), new Vector3(0.32f, 0.22f, 0.015f),
                EmissiveMat("LP_LaptopScreen", new Color(0.15f, 0.2f, 0.3f), new Color(0.4f, 0.55f, 0.9f) * 0.6f));
            Findable(counter.transform, "recorder", "ボイスレコーダー", new Vector3(0.1f, 0.91f, 0.1f),
                GetMat("LP_Recorder", new Color(0.2f, 0.2f, 0.22f), 0.5f),
                new Vector3(0.05f, 0.02f, 0.12f),
                "最後の録音",
                "《録音 再生》\n\n" +
                "「──佐伯さん、電話に出ない。\n" +
                "　黒田さんも会議中。主任も出ない。\n" +
                "　決めた。今夜、外部の窓口に全部話す。\n" +
                "　荷物まとめて、いったん実家に──」\n\n" +
                "ドアの開く音。\n\n" +
                "「……あれ。どうして、ここが──あ、」\n\n" +
                "録音は、そこで終わっている。");

            // ---- 境界: 襖（片側だけ開いている） ----
            Box(t, "FusumaWall_L", new Vector3(-hw * 0.5f - 0.55f, h * 0.5f, split), new Vector3(hw - 1.1f + 0.05f, h, 0.1f), fusuma);
            Box(t, "FusumaWall_R", new Vector3(hw * 0.5f + 0.55f, h * 0.5f, split), new Vector3(hw - 1.1f + 0.05f, h, 0.1f), fusuma);
            Box(t, "FusumaLintel", new Vector3(0f, (2.0f + h) * 0.5f, split), new Vector3(2.2f, h - 2.0f, 0.1f), fusuma);
            Deco(t, "FusumaOpen", new Vector3(-1.35f, 1.0f, split + 0.06f), new Vector3(0.9f, 2.0f, 0.04f), fusuma);

            // ---- 奥: 病院の個室（寒色） ----
            var cold = new GameObject("ColdLight");
            cold.transform.SetParent(t, false);
            cold.transform.localPosition = new Vector3(0f, h - 0.4f, hd - 1.6f);
            var cl2 = cold.AddComponent<Light>();
            cl2.type = LightType.Point; cl2.color = new Color(0.65f, 0.8f, 1f);
            cl2.intensity = 1.8f; cl2.range = 6f;

            // 病室側だけ床が白いタイルに変わり、壁も病院の白になる（記憶の滲みの境界）
            var tile = GetMat("LP_HospitalTile", new Color(0.86f, 0.88f, 0.88f), 0.5f);
            var hwall = GetMat("LP_FacilityWall", new Color(0.86f, 0.87f, 0.86f), 0.2f);
            float backLen = hd - split - 0.1f;
            Deco(t, "TileFloor", new Vector3(0f, 0.005f, split + 0.05f + backLen * 0.5f), new Vector3(hw * 2f - 0.15f, 0.01f, backLen), tile);
            foreach (float xs in new[] { -1f, 1f })
                Deco(t, "HospitalWallSkin", new Vector3(xs * (hw - 0.085f), h * 0.5f, split + 0.05f + backLen * 0.5f),
                    new Vector3(0.02f, h - 0.02f, backLen), hwall);
            var hbed = new GameObject("HospitalBedUnit"); hbed.transform.SetParent(t, false);
            hbed.transform.localPosition = new Vector3(0.8f, 0f, hd - 1.7f);
            // 頭側を奥（北）へ向ける＝180度回転
            if (Prop(hbed.transform, "HospitalBed", Vector3.zero, 180f) == null)
            {
                Box(hbed.transform, "Frame", new Vector3(0f, 0.3f, 0f), new Vector3(1.0f, 0.6f, 2.1f), frameM);
                Box(hbed.transform, "Sheet", new Vector3(0f, 0.66f, 0f), new Vector3(0.95f, 0.12f, 2.0f), sheet);
                Deco(hbed.transform, "HeadBoard", new Vector3(0f, 0.85f, 1.02f), new Vector3(1.0f, 0.5f, 0.04f), frameM);
            }
            if (Prop(hbed.transform, "IVStand", new Vector3(-0.72f, 0f, 0.7f), 0f, false) == null)
            {
                Deco(hbed.transform, "IvPole", new Vector3(-0.6f, 0.9f, 0.7f), new Vector3(0.03f, 1.8f, 0.03f), frameM);
                Deco(hbed.transform, "IvBag", new Vector3(-0.6f, 1.6f, 0.7f), new Vector3(0.1f, 0.18f, 0.05f), sheet);
            }
            Deco(hbed.transform, "CurtainRail", new Vector3(-0.75f, h - 0.25f, 0f), new Vector3(0.03f, 0.03f, 2.4f), frameM);
            Deco(hbed.transform, "Curtain", new Vector3(-0.75f, (h - 0.3f + 0.4f) * 0.5f, -0.6f), new Vector3(0.05f, h - 0.7f, 1.0f),
                GetMat("LP_WardCurtain", new Color(0.72f, 0.82f, 0.76f), 0.05f));
            Window(t, new Vector3(hw - 0.08f, 1.6f, hd - 1.7f), 90f, 1.2f, 1.0f, false);
            Deco(t, "BedsideCabinet", new Vector3(-0.6f, 0.3f, hd - 0.6f), new Vector3(0.5f, 0.6f, 0.45f), frameM);

            // 残響〈叔父の病室〉幼い水野
            Echo(t, "mizuno_uncle", "残響：病室（幼い水野の記憶）",
                "病院のベッドの脇に、小さな影が立っている。\n" +
                "眠ったままの誰かに、ずっと話しかけている。\n" +
                "返事はない。それでも、毎日。\n" +
                "──彼女の原点が、ここにある。",
                1.2f, 1.35f, 0.14f,
                (new Vector3(-0.9f, 0f, hd - 2.3f), 60f, 0.55f, 2f, 1f, false));

            // 残響〈最後の会話・水野視点〉＝顔のない黒い影（正体はまだ明かさない）
            Echo(t, "talk_mizuno", "残響：会話（水野の記憶）",
                "部屋の中で、小柄な影が出口を背にして立ちすくんでいる。\n" +
                "向かいには──顔のない、真っ黒な影。\n" +
                "闇より濃い輪郭だけがそこにあり、\n" +
                "顔があるべき場所には、何もない。\n\n" +
                "病棟で見たのと、同じ場面のはずだ。\n" +
                "なのに、どうして。彼女はこんなに、怯えている。",
                2.6f, 0.6f, 0.22f,
                (new Vector3(1.7f, 0f, -2.9f), -15f, 0.92f, -3f, 1f, false),
                (new Vector3(1.4f, 0f, -1.5f), 170f, 1.12f, 2f, 0.15f, true));

            return new[] { "diary", "recorder", "pcfile" };
        }

        // ============================== 3章 黒田恒一 ==============================

        /// <summary>データ管理室（3章起）：サーバーラック・紙ファイル・残響〈口論・黒田視点〉</summary>
        private static string[] FurnishDataRoom(Transform t, float hw, float hd, float h)
        {
            var rack = GetMat("LP_ServerRack", new Color(0.12f, 0.13f, 0.15f), 0.4f);
            var led = EmissiveMat("LP_ServerLed", new Color(0.1f, 0.3f, 0.25f), new Color(0.3f, 1f, 0.7f) * 1.2f);
            var ledAmber = EmissiveMat("LP_ServerLedAmber", new Color(0.3f, 0.2f, 0.05f), new Color(1f, 0.6f, 0.15f) * 1.2f);
            var rackFace = GetMat("LP_RackFace", new Color(0.2f, 0.21f, 0.23f), 0.5f);
            var cab = GetMat("LP_FileCabinet", new Color(0.5f, 0.52f, 0.55f), 0.3f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            var crt = EmissiveMat("LP_CrtScreen", new Color(0.08f, 0.15f, 0.12f), new Color(0.4f, 0.9f, 0.6f) * 0.7f);
            var trim = GetMat("LP_FacilityTrim", new Color(0.45f, 0.47f, 0.5f), 0.3f);

            // サーバーラック2列（暗い柱に小さなLEDが無数に瞬く）
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 4; i++)
                {
                    float sx = row == 0 ? 1f : -1f;
                    float x = -sx * 1.6f;
                    float z = -hd + 2.4f + i * 1.7f;
                    // Blender製ラック（扉＝+Z）を通路側へ向ける。無ければ箱＋LEDで代替
                    if (Prop(t, "ServerRack", new Vector3(x, 0f, z), sx > 0 ? 90f : -90f) != null) continue;
                    Box(t, $"Rack_{row}_{i}", new Vector3(x, 1.05f, z), new Vector3(0.8f, 2.1f, 1.0f), rack);
                    // 通路側の面：ユニットの段（横板）とLED列
                    for (int u = 0; u < 8; u++)
                    {
                        float y = 0.25f + u * 0.24f;
                        Deco(t, "RackUnit", new Vector3(x + sx * 0.41f, y, z), new Vector3(0.02f, 0.02f, 0.9f), rackFace);
                        for (int d = 0; d < 4; d++)
                            Deco(t, "Led", new Vector3(x + sx * 0.415f, y + 0.09f, z - 0.35f + d * 0.22f),
                                new Vector3(0.012f, 0.03f, 0.03f), ((u + d + i) % 5 == 0) ? ledAmber : led);
                    }
                }
            // フリーアクセス床の目地と天井ダクト
            var seam = GetMat("LP_FloorSeam", new Color(0.4f, 0.43f, 0.42f), 0.3f);
            for (float x = -hw + 0.6f; x < hw; x += 0.6f)
                Deco(t, "FloorSeam", new Vector3(x, 0.003f, 0f), new Vector3(0.015f, 0.006f, hd * 2f - 0.2f), seam);
            for (float z = -hd + 0.6f; z < hd; z += 0.6f)
                Deco(t, "FloorSeam", new Vector3(0f, 0.003f, z), new Vector3(hw * 2f - 0.2f, 0.006f, 0.015f), seam);
            Duct(t, new Vector3(0f, h - 0.35f, 0f), hd * 2f - 0.6f, 0.4f, trim);

            // 壁一面の紙ファイルキャビネット（番号順）
            for (int i = 0; i < 4; i++)
                Box(t, "FileCabinet", new Vector3(-hw + 0.35f, 1.1f, -hd + 1.8f + i * 1.7f), new Vector3(0.5f, 2.2f, 1.4f), cab);

            // 管理者デスク：CRTと入退室記録、読みかけの眼鏡（椅子は引かれたまま）
            var admin = Desk(t, "AdminDesk", new Vector3(hw - 1.5f, 0f, hd - 1.6f), cab);
            Prop(t, "OfficeChair", new Vector3(hw - 1.3f, 0f, hd - 2.5f), 15f);
            Box(admin.transform, "CrtBody", new Vector3(0f, 0.95f, 0.18f), new Vector3(0.45f, 0.4f, 0.4f), rack);
            Deco(admin.transform, "CrtScreen", new Vector3(0f, 0.95f, -0.04f), new Vector3(0.36f, 0.3f, 0.02f), crt);
            Deco(admin.transform, "Glasses", new Vector3(-0.15f, 0.765f, -0.22f), new Vector3(0.14f, 0.02f, 0.05f), cab);

            Findable(admin.transform, "minutes", "議事録", new Vector3(0.45f, 0.76f, -0.05f), paper,
                new Vector3(0.32f, 0.02f, 0.24f),
                "停止要求の議事録",
                "《緊急会議 議事録》　起案: 黒田\n" +
                "日時: 4月18日〜19日（2日間）\n" +
                "出席: 黒田・佐伯・水野\n" +
                "欠席: 所長（学会出張のため札幌に滞在。\n" +
                "　　　 19日夜まで戻らず、電話で参加）\n\n" +
                "「システムは登録された脳情報を\n" +
                "　単なるデータではなく、人格モデルとして\n" +
                "　扱い始めている。これは治療ではない。\n" +
                "　直ちに運用を停止すべきだ」\n\n" +
                "・所長（電話）: 保留\n" +
                "・佐伯: 検証に時間を要すると発言\n\n" +
                "……黒田。名簿の上席研究員。\n" +
                "この施設で、最初に「止めろ」と言った人。");

            // 入退室ログ照合端末（3章起・照合型）: CRT本体を操作する
            var audit = Lock<LoopAuditLock>(admin.transform, "AuditTerminal", new Vector3(0f, 0.95f, -0.05f),
                new Vector3(0.38f, 0.32f, 0.03f), crt, "data_room", "audit", "入退室ログ端末", "data_room_minutes");
            audit.Title = "入退室ログ照合　4/16 〜 4/20";
            audit.Body = "記録のうち「あり得ない行」を特定せよ。";
            audit.Rows = LoopAuditLock.DefaultRows();
            audit.CorrectRows = LoopAuditLock.DefaultCorrect();
            audit.SuccessNoteTitle = "照合結果：主任IDの移動記録は本人ではない";
            audit.SuccessNoteBody =
                "4月18日深夜から19日未明、主任IDで第12研究室へ3回の入室。\n" +
                "だが議事録によれば、所長はその2日間、札幌にいた。\n\n" +
                "CRTが一行だけ吐き出した。\n" +
                "「……主任じゃない。」\n\n" +
                "なら、誰が主任のIDを使った。";
            Findable(admin.transform, "kmemo", "黒田のメモ", new Vector3(-0.5f, 0.76f, 0.12f), paper,
                new Vector3(0.26f, 0.02f, 0.18f),
                "黒田のメモ（一部欠損）",
                "《個人メモ》　筆跡: 黒田\n\n" +
                "「主任は3人を犠牲にする覚悟を決めた。\n" +
                "　止められるのは私しかいない──」\n\n" +
                "後半は破り取られて読めない。\n\n" +
                "3人を、犠牲に？\n" +
                "主任が？　……そうか。そういうこと、なのか。");
            Findable(t, "scribble", "走り書き", new Vector3(-hw + 0.62f, 1.55f, hd - 3.5f), paper,
                new Vector3(0.18f, 0.14f, 0.02f),
                "キャビネットの走り書き",
                "ファイルの間に挟まれた付箋。\n\n" +
                "「主任を信用しすぎないほうがいい。\n" +
                "　あの人は優しすぎる。\n" +
                "　優しさは、時に何も止められない」\n\n" +
                "──筆跡は黒田。\n" +
                "「信用するな」ではなく「しすぎるな」。\n" +
                "この言い回しが、妙に引っかかる。");

            // 残響〈口論・黒田視点〉＝同じ場面が「怒鳴られている」ように見える
            Echo(t, "argue_kuroda", "残響：口論（黒田の記憶）",
                "……この場面を、知っている。佐伯の家で見た、あの口論だ。\n" +
                "だが、まるで違う。\n" +
                "向かいの影は一回り大きく、覆いかぶさるように迫り、\n" +
                "割れた声を張り上げている。もう片方の影は\n" +
                "壁際まで退がって、縮こまっている。\n\n" +
                "同じ場面の、はずだ。\n" +
                "──記憶は、こんなにも歪む。\n" +
                "なら。私がここまで見てきた記憶は、どうなんだ。",
                9f, 0.62f, 0.5f,
                (new Vector3(0f, 0f, 0.4f), 120f, 1.28f, 10f, 1.6f, false),
                (new Vector3(1.0f, 0f, 1.3f), -60f, 0.88f, -8f, 0.4f, false));

            return new[] { "minutes", "kmemo", "scribble", "audit" };
        }

        /// <summary>SYSTEM ROOM（3章転）：中枢コア・NINOMIYA HIDEKIの反転</summary>
        private static string[] FurnishSystemRoom(Transform t, float hw, float hd, float h)
        {
            var rack = GetMat("LP_ServerRack", new Color(0.12f, 0.13f, 0.15f), 0.4f);
            // コアは「暗いガラス管の中で淡く脈打つ」見え方に。全面発光の筒にはしない
            var coreGlass = GlassMat("LP_SystemCoreGlass", new Color(0.5f, 0.75f, 0.95f, 0.25f));
            var coreM = EmissiveMat("LP_SystemCore", new Color(0.08f, 0.18f, 0.3f), new Color(0.3f, 0.7f, 1f) * 0.9f);
            var coreBand = MetalMat("LP_SystemCoreBand", new Color(0.15f, 0.16f, 0.18f), 0.6f, 0.8f);
            var cable = GetMat("LP_Cable", new Color(0.08f, 0.08f, 0.1f), 0.3f);
            var screen = EmissiveMat("LP_SystemScreen", new Color(0.1f, 0.2f, 0.32f), new Color(0.25f, 0.55f, 1f) * 0.7f);
            var strip = EmissiveMat("LP_BlueStrip", new Color(0.2f, 0.4f, 0.6f), new Color(0.3f, 0.6f, 1f) * 1.2f);

            // 中央の円筒コア：内側の発光芯＋外側のガラス管＋金属の帯／台座／天井キャップ
            void Cyl(string name, Vector3 pos, Vector3 scale, Material m, bool collider)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                c.name = name; c.transform.SetParent(t, false);
                c.transform.localPosition = pos; c.transform.localScale = scale;
                c.GetComponent<Renderer>().sharedMaterial = m;
                if (!collider) Object.DestroyImmediate(c.GetComponent<Collider>());
            }
            Cyl("CoreInner", new Vector3(0f, 1.9f, 1.0f), new Vector3(1.0f, 1.3f, 1.0f), coreM, false);
            Cyl("CoreGlass", new Vector3(0f, 1.9f, 1.0f), new Vector3(2.0f, 1.35f, 2.0f), coreGlass, true);
            Cyl("CoreBase", new Vector3(0f, 0.25f, 1.0f), new Vector3(2.6f, 0.25f, 2.6f), coreBand, true);
            Cyl("CoreCap", new Vector3(0f, h - 0.3f, 1.0f), new Vector3(2.6f, 0.3f, 2.6f), coreBand, false);
            foreach (float y in new[] { 1.2f, 2.6f })
                Cyl("CoreBand", new Vector3(0f, y, 1.0f), new Vector3(2.1f, 0.05f, 2.1f), coreBand, false);
            var coreGlow = new GameObject("CoreGlow");
            coreGlow.transform.SetParent(t, false);
            coreGlow.transform.localPosition = new Vector3(0f, 2.0f, 1.0f);
            var cg = coreGlow.AddComponent<Light>();
            cg.type = LightType.Point; cg.color = new Color(0.4f, 0.7f, 1f);
            cg.intensity = 1.6f; cg.range = 11f;

            // コアから放射状に伸びるケーブル（壁際で立ち上がって天井へ）
            for (int i = 0; i < 8; i++)
            {
                var arm = new GameObject($"CableArm_{i}");
                arm.transform.SetParent(t, false);
                arm.transform.localPosition = new Vector3(0f, 0f, 1.0f);
                // 扉の軸（南北）を避けた斜め方向へ。立ち上がりが入口の正面に立たないようにする
                arm.transform.localRotation = Quaternion.Euler(0f, i * 45f + 22.5f, 0f);
                float reach = (i % 2 == 0) ? 3.9f : 3.4f;
                Deco(arm.transform, "Cable", new Vector3(0f, 0.06f, 1.3f + (reach - 1.3f) * 0.5f), new Vector3(0.18f, 0.1f, reach - 1.3f), cable);
                Deco(arm.transform, "CableRiser", new Vector3(0f, h * 0.5f, reach), new Vector3(0.16f, h, 0.16f), cable);
            }

            // 壁のモニター群と、壁際を回る青いラインライト
            for (int i = 0; i < 4; i++)
            {
                Deco(t, "WallMonitorFrame", new Vector3(-hw + 0.1f, 1.7f, -3.0f + i * 2.0f), new Vector3(0.06f, 1.1f, 1.6f), rack);
                Deco(t, "WallMonitor", new Vector3(-hw + 0.14f, 1.7f, -3.0f + i * 2.0f), new Vector3(0.02f, 0.95f, 1.45f), screen);
            }
            foreach (float xs in new[] { -1f, 1f })
                Deco(t, "BlueStrip", new Vector3(xs * (hw - 0.1f), 0.25f, 0f), new Vector3(0.03f, 0.04f, hd * 2f - 0.4f), strip);
            Duct(t, new Vector3(-3.2f, h - 0.4f, 0f), hd * 2f - 0.6f, 0.45f, rack);
            Duct(t, new Vector3(3.2f, h - 0.4f, 0f), hd * 2f - 0.6f, 0.45f, rack);

            // 操作卓（ひとつだけ照明が落ち、椅子が引かれたまま。スポーン正面は空ける）
            var console = Prop(t, "Workstation", new Vector3(1.7f, 0f, -hd + 1.7f))
                          ?? Desk(t, "Console", new Vector3(1.7f, 0f, -hd + 1.7f), rack);
            if (Prop(t, "OfficeChair", new Vector3(2.3f, 0f, -hd + 2.5f), 35f) == null)
                Box(t, "ConsoleChair", new Vector3(2.3f, 0.24f, -hd + 2.5f), new Vector3(0.45f, 0.48f, 0.45f), rack);
            var spot = new GameObject("ConsoleLight");
            spot.transform.SetParent(t, false);
            spot.transform.localPosition = new Vector3(1.7f, 2.2f, -hd + 1.7f);
            var sl2 = spot.AddComponent<Light>();
            sl2.type = LightType.Point; sl2.color = new Color(1f, 0.95f, 0.85f);
            sl2.intensity = 1.5f; sl2.range = 4f;

            Findable(console.transform, "devlog", "開発記録", new Vector3(-0.4f, 0.76f, 0f),
                GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f),
                new Vector3(0.32f, 0.02f, 0.24f),
                "開発記録・治療手順",
                "《治療手順 全文》\n\n" +
                "1. 提供体の脳情報を採取し、BRAIN DATAとして登録\n" +
                "2. 対象者（患者）の欠損領域へ写像・補完\n" +
                "3. 対象者の人格が再構成されるまで反復\n\n" +
                "備考: 提供体の脳情報は生体からの直接採取に限る。\n" +
                "──直接、採取。\n" +
                "登録は、3件だった。");
            Findable(console.transform, "gaplog", "照合ログ", new Vector3(0.3f, 0.76f, 0.1f),
                GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f),
                new Vector3(0.28f, 0.02f, 0.2f),
                "黒田の照合ログ",
                "《入退室照合 手書きの検算》　筆跡: 黒田\n\n" +
                "「深夜帯に第12へ3回の入室。使用IDは主任。\n" +
                "　だが主任は当日、学会で北海道にいた。\n" +
                "　IDでは説明できない移動記録がある。\n\n" +
                "　……主任じゃない。\n" +
                "　なら、誰だ。誰が主任のIDを──」\n\n" +
                "ページの端が、強く握り潰された跡。");
            Findable(t, "restored", "復元ファイル", new Vector3(-hw + 0.22f, 1.7f, 1.0f), screen,
                new Vector3(0.06f, 0.5f, 0.8f),
                "復元された操作記録",
                "《BRAIN DATA 登録操作 復元ログ》\n\n" +
                "　DATA:01 登録実行者──NINOMIYA HIDEKI\n" +
                "　DATA:02 登録実行者──NINOMIYA HIDEKI\n" +
                "　DATA:03 登録実行者──NINOMIYA HIDEKI\n\n" +
                "………………。\n\n" +
                "違う。何かの間違いだ。だって私は、私は──\n\n" +
                "──思い出した。\n" +
                "解析室の夜。「何をするつもりですか」の声。\n" +
                "病棟の廊下。アパートのドア。\n" +
                "黒い影には、顔が無かったんじゃない。\n" +
                "あれは、「私から見た私」だったんだ。\n\n" +
                "私が、3人を殺した。\n" +
                "娘を助けるための「提供体」に、するために。");

            return new[] { "devlog", "gaplog", "restored" };
        }

        /// <summary>黒田の自宅（3章結）：父の席だけ空いた食卓・子供の絵・残響〈食卓〉</summary>
        private static string[] FurnishKurodaHome(Transform t, float hw, float hd, float h)
        {
            var wood = GetMat("LP_Furniture", new Color(0.4f, 0.3f, 0.22f), 0.2f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            var fridge = GetMat("LP_Fridge", new Color(0.88f, 0.9f, 0.9f), 0.5f);
            var dish = GetMat("LP_CoffeeCup", new Color(0.92f, 0.9f, 0.86f), 0.5f);
            var crayon = GetMat("LP_Crayon", new Color(0.95f, 0.9f, 0.75f), 0.1f);

            // 台所の小さな灯りだけが食卓を照らす
            var lamp = new GameObject("KitchenLight");
            lamp.transform.SetParent(t, false);
            lamp.transform.localPosition = new Vector3(0f, h - 0.5f, 0.6f);
            var kl = lamp.AddComponent<Light>();
            kl.type = LightType.Point; kl.color = new Color(1f, 0.85f, 0.6f);
            kl.intensity = 1.7f; kl.range = 6f;

            // 食卓：四人分の食器。父の席だけ料理に手がつけられていない
            var table = new GameObject("DiningTable"); table.transform.SetParent(t, false);
            table.transform.localPosition = new Vector3(0f, 0f, 0.6f);
            Box(table.transform, "Top", new Vector3(0f, 0.72f, 0f), new Vector3(1.6f, 0.05f, 1.1f), wood);
            Box(table.transform, "Leg", new Vector3(0f, 0.36f, 0f), new Vector3(0.14f, 0.72f, 0.14f), wood);
            var seats = new[]
            {
                new Vector3(-0.55f, 0f, -0.85f), new Vector3(0.55f, 0f, -0.85f),
                new Vector3(-0.55f, 0f, 0.85f), new Vector3(0.55f, 0f, 0.85f),
            };
            bool propChairs = true;
            for (int i = 0; i < 4; i++)
            {
                // 南側の2脚は背を南（yaw 0）、北側は背を北（yaw 180）に向ける
                if (Prop(table.transform, "DiningChair", seats[i], seats[i].z < 0f ? 0f : 180f) == null)
                {
                    propChairs = false;
                    Box(table.transform, $"Chair_{i}", seats[i] + new Vector3(0f, 0.24f, 0f), new Vector3(0.45f, 0.48f, 0.45f), wood);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                var pl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pl.name = $"Dish_{i}"; pl.transform.SetParent(table.transform, false);
                pl.transform.localPosition = new Vector3(seats[i].x * 0.7f, 0.77f, seats[i].z * 0.35f);
                pl.transform.localScale = new Vector3(0.2f, 0.015f, 0.2f);
                pl.GetComponent<Renderer>().sharedMaterial = dish;
                Object.DestroyImmediate(pl.GetComponent<Collider>());
            }

            // ペンダントの下のラグと、座布団
            Rug(t, new Vector3(0f, 0f, 0.6f), new Vector2(2.6f, 2.2f), new Color(0.5f, 0.38f, 0.3f));
            if (!propChairs)
            {
                var cushion = GetMat("LP_Cushion", new Color(0.55f, 0.35f, 0.3f), 0.05f);
                for (int i = 0; i < 4; i++)
                    Deco(table.transform, "Cushion", seats[i] + new Vector3(0f, 0.5f, 0f), new Vector3(0.4f, 0.05f, 0.4f), cushion);
            }

            // 台所：カウンターとシンク、吊り戸棚、換気扇
            var counterMat = GetMat("LP_KitchenCounter", new Color(0.8f, 0.8f, 0.78f), 0.4f);
            var sink = MetalMat("LP_Sink", new Color(0.7f, 0.72f, 0.74f), 0.7f, 0.9f);
            Box(t, "KitchenCounter", new Vector3(-hw + 0.45f, 0.43f, 1.2f), new Vector3(0.7f, 0.86f, 2.2f), counterMat);
            Deco(t, "CounterTop", new Vector3(-hw + 0.45f, 0.875f, 1.2f), new Vector3(0.72f, 0.03f, 2.24f), sink);
            Deco(t, "SinkBasin", new Vector3(-hw + 0.45f, 0.86f, 1.6f), new Vector3(0.5f, 0.02f, 0.6f),
                GetMat("LP_SinkBasin", new Color(0.4f, 0.42f, 0.44f), 0.6f));
            Deco(t, "Faucet", new Vector3(-hw + 0.2f, 1.05f, 1.6f), new Vector3(0.03f, 0.3f, 0.03f), sink);
            Box(t, "UpperCabinet", new Vector3(-hw + 0.3f, 1.9f, 1.2f), new Vector3(0.4f, 0.7f, 2.2f), counterMat);
            Deco(t, "RangeHood", new Vector3(-hw + 0.35f, 1.55f, 0.4f), new Vector3(0.5f, 0.2f, 0.6f), sink);

            // 窓（東）とテレビ台（南西）
            Window(t, new Vector3(hw - 0.08f, 1.5f, -1.8f), 90f, 1.2f, 1.1f, true, new Color(0.75f, 0.7f, 0.6f));
            Box(t, "TvStand", new Vector3(-1.8f, 0.22f, -hd + 0.45f), new Vector3(1.4f, 0.44f, 0.5f), wood);
            Box(t, "Tv", new Vector3(-1.8f, 0.8f, -hd + 0.45f), new Vector3(1.1f, 0.65f, 0.08f),
                GetMat("LP_DeadScreen", new Color(0.05f, 0.05f, 0.07f), 0.6f));

            // 冷蔵庫と子供の絵
            Box(t, "Fridge", new Vector3(-hw + 0.5f, 0.9f, 2.8f), new Vector3(0.7f, 1.8f, 0.7f), fridge);
            Deco(t, "FridgeHandle", new Vector3(-hw + 0.87f, 1.1f, 3.05f), new Vector3(0.03f, 0.5f, 0.04f), sink);
            var draw = Findable(t, "drawing", "子供の絵", new Vector3(-hw + 0.87f, 1.2f, 2.8f), crayon,
                new Vector3(0.03f, 0.28f, 0.22f),
                "冷蔵庫の絵",
                "クレヨンで描かれた家族の絵。\n" +
                "お父さんは真ん中で、一番大きい。\n\n" +
                "たどたどしい字で、こう書いてある。\n\n" +
                "「おとうさんは　ただしい」\n\n" +
                "………。\n" +
                "その「正しい人」を、私は。");

            // サイドボードの個人記録・最後の対峙の記録
            var sideb = new GameObject("Sideboard"); sideb.transform.SetParent(t, false);
            sideb.transform.localPosition = new Vector3(hw - 0.55f, 0f, -1.8f);
            Box(sideb.transform, "Body", new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.9f, 1.6f), wood);
            Findable(sideb.transform, "rules", "個人記録", new Vector3(0f, 0.93f, -0.3f), paper,
                new Vector3(0.28f, 0.02f, 0.2f),
                "黒田の個人記録",
                "《手帳》　筆跡: 黒田\n\n" +
                "「規則は人を縛るためにあるんじゃない。\n" +
                "　守るためにある。\n" +
                "　それを娘たちに教えられる父親でいたい」\n\n" +
                "「正しいことと、救うことは同じではない。\n" +
                "　それでも私は、正しい方を選ぶ。\n" +
                "　誰かが選ばなければならないからだ」");
            Findable(sideb.transform, "lastrec", "対峙の記録", new Vector3(0f, 0.93f, 0.35f), paper,
                new Vector3(0.26f, 0.02f, 0.18f),
                "最後の対峙の記録（破損）",
                "《ICレコーダー 復元断片》\n\n" +
                "黒田:「もう分かっているんです。あなたでしょう」\n" +
                "　　 「──あなたは娘を救いたいんじゃない。\n" +
                "　　　 娘を失うことを、受け入れられないだけだ」\n\n" +
                "（長い沈黙）\n\n" +
                "黒田:「……そうか。その顔が、答えか」\n\n" +
                "記録はここで破損している。\n" +
                "彼は最後まで、逃げなかった。");

            // 残響の判定（3章結・照合型）: 口論を両方の視点で見た人だけが第三の答えに辿り着く
            var verdict = Lock<LoopChoiceLock>(sideb.transform, "Verdict", new Vector3(0f, 0.93f, 0.02f),
                new Vector3(0.2f, 0.03f, 0.15f), GetMat("LP_KurodaNotebook", new Color(0.15f, 0.15f, 0.2f), 0.3f),
                "kuroda_home", "verdict", "黒田の手帳（白紙のページ）", "echo_argue_kuroda");
            verdict.Title = "黒田の手帳　── 白紙のページ ──";
            verdict.Body = "あの口論で、怒鳴っていたのは誰だったのか。\n私は、答えを書き込まなければならない。";
            verdict.Options = new[] { "佐伯", "黒田", "どちらでもない──記憶は主観で歪む" };
            verdict.CorrectIndex = 2;
            verdict.RequireNotes = new[] { "echo_argue_saeki", "echo_argue_kuroda" };
            verdict.NotEnoughMessage = "まだ判断できない。あの口論を、両方の側から見ていない。";
            verdict.SuccessNoteTitle = "判定：どちらも、怒鳴ってはいなかった";
            verdict.SuccessNoteBody =
                "佐伯の記憶では冷静な対話。黒田の記憶では一方的な怒鳴り声。\n" +
                "同じ場面のはずなのに。\n\n" +
                "──記憶は主観で歪む。\n" +
                "なら、私がここまで「見てきた」ものは、どこまで本当だ。\n" +
                "終章で、私はこの目で娘の記憶を選り分けることになる。";

            // 残響〈食卓〉家族の夕食（父の席だけ空いている）
            Echo(t, "kuroda_family", "残響：食卓（黒田の家族の記憶）",
                "食卓に、小さな影がふたつ。\n" +
                "楽しそうに揺れて、笑い声のような響きが漏れる。\n" +
                "母親らしき影が、料理を並べていく。\n" +
                "父の席だけが、いつまでも空いたまま。\n\n" +
                "──この団らんを、私が終わらせた。",
                4.5f, 1.4f, 0.24f,
                (new Vector3(-0.55f, 0f, -0.25f), 20f, 0.55f, 3f, 1.2f, false),
                (new Vector3(0.55f, 0f, -0.25f), -20f, 0.6f, -4f, 1.4f, false),
                (new Vector3(0.55f, 0f, 1.45f), 180f, 0.95f, 2f, 0.7f, false));

            return new[] { "rules", "lastrec", "verdict" };
        }

        // ============================== 終章 RENASCITA ==============================

        /// <summary>MAIN CORE ROOM（終章）：球形コアと主任のメッセージ</summary>
        private static string[] FurnishCoreMain(Transform t, float hw, float hd, float h)
        {
            // 球は「暗い殻の内側で金色に脈打つ」見え方。全面発光にすると平板になる
            var coreShell = GlassMat("LP_MainCoreShell", new Color(0.6f, 0.5f, 0.3f, 0.3f));
            var coreM = EmissiveMat("LP_MainCore", new Color(0.35f, 0.28f, 0.15f), new Color(1f, 0.85f, 0.45f) * 1.0f);
            var cableBase = GetMat("LP_CablePillarBase", new Color(0.12f, 0.14f, 0.18f), 0.3f);
            var cable = EmissiveMat("LP_CablePillar", new Color(0.1f, 0.18f, 0.28f), new Color(0.35f, 0.6f, 0.9f) * 0.7f);
            var rack = GetMat("LP_ServerRack", new Color(0.12f, 0.13f, 0.15f), 0.4f);
            var screen = EmissiveMat("LP_CoreTerminal", new Color(0.4f, 0.35f, 0.25f), new Color(1f, 0.9f, 0.6f) * 0.9f);
            var goldStrip = EmissiveMat("LP_GoldStrip", new Color(0.5f, 0.42f, 0.25f), new Color(1f, 0.85f, 0.45f) * 1.2f);

            // 巨大な球形コア（内側の金の芯＋半透明の殻）と、それを支える台座リング
            void Sph(string name, Vector3 pos, float scale, Material m, bool collider)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = name; s.transform.SetParent(t, false);
                s.transform.localPosition = pos; s.transform.localScale = Vector3.one * scale;
                s.GetComponent<Renderer>().sharedMaterial = m;
                if (!collider) Object.DestroyImmediate(s.GetComponent<Collider>());
            }
            Sph("MainCoreInner", new Vector3(0f, 2.6f, 2.5f), 2.6f, coreM, false);
            Sph("MainCoreShell", new Vector3(0f, 2.6f, 2.5f), 3.6f, coreShell, true);
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "CoreCradle"; ring.transform.SetParent(t, false);
            ring.transform.localPosition = new Vector3(0f, 0.35f, 2.5f);
            ring.transform.localScale = new Vector3(4.2f, 0.35f, 4.2f);
            ring.GetComponent<Renderer>().sharedMaterial = rack;
            var glow = new GameObject("CoreGlow");
            glow.transform.SetParent(t, false);
            glow.transform.localPosition = new Vector3(0f, 3.0f, 1.6f);
            var gl2 = glow.AddComponent<Light>();
            gl2.type = LightType.Point; gl2.color = new Color(1f, 0.85f, 0.55f);
            gl2.intensity = 2.4f; gl2.range = 16f;

            // 床から天井へ伸びる光の柱（暗い柱の中に細い光の筋）
            for (int i = 0; i < 6; i++)
            {
                float ang = (i * 60f + 30f) * Mathf.Deg2Rad;   // 祭壇の正面（南）を空ける
                float r = 4.2f;
                var p = new Vector3(Mathf.Sin(ang) * r, h * 0.5f, 2.5f + Mathf.Cos(ang) * r * 0.7f);
                Deco(t, "CablePillar", p, new Vector3(0.32f, h, 0.32f), cableBase);
                Deco(t, "CablePillarLight", p, new Vector3(0.12f, h - 0.1f, 0.34f), cable);
                Deco(t, "CablePillarLight", p, new Vector3(0.34f, h - 0.1f, 0.12f), cable);
            }
            // 床の光の輪（祭壇と球を結ぶ導線）
            for (int i = 0; i < 24; i++)
            {
                float ang = i * 15f;
                var seg = new GameObject("FloorRingSeg");
                seg.transform.SetParent(t, false);
                seg.transform.localPosition = new Vector3(0f, 0f, 2.5f);
                seg.transform.localRotation = Quaternion.Euler(0f, ang, 0f);
                Deco(seg.transform, "Strip", new Vector3(0f, 0.004f, 2.6f), new Vector3(0.05f, 0.008f, 0.62f), goldStrip);
            }
            // 天井の大ダクトと、壁沿いのケーブル束
            Duct(t, new Vector3(0f, h - 0.5f, 0f), hd * 2f - 0.8f, 0.7f, rack);
            foreach (float xs in new[] { -1f, 1f })
                Deco(t, "WallCables", new Vector3(xs * (hw - 0.2f), 0.5f, 0f), new Vector3(0.25f, 0.4f, hd * 2f - 0.4f), rack);

            // 祭壇のような操作端末（コアの前にひとつだけ。二段の段差の上）
            Deco(t, "Step1", new Vector3(0f, 0.05f, -0.8f), new Vector3(3.0f, 0.1f, 2.2f), rack);
            Deco(t, "Step2", new Vector3(0f, 0.15f, -0.8f), new Vector3(2.2f, 0.1f, 1.6f), rack);
            var altar = new GameObject("AltarTerminal"); altar.transform.SetParent(t, false);
            altar.transform.localPosition = new Vector3(0f, 0.2f, -0.8f);
            Box(altar.transform, "Stand", new Vector3(0f, 0.5f, 0f), new Vector3(0.9f, 1.0f, 0.6f), rack);
            Deco(altar.transform, "StandStrip", new Vector3(0f, 0.5f, -0.31f), new Vector3(0.6f, 0.02f, 0.01f), goldStrip);
            Findable(altar.transform, "message", "操作端末", new Vector3(0f, 1.15f, 0f), screen,
                new Vector3(0.7f, 0.45f, 0.06f),
                "主任のメッセージ",
                "《再生メッセージ》　小川 暁\n\n" +
                "「二宮さん。ここまで来たなら、もう思い出しましたね。\n" +
                "　あなたは3人を手にかけ、脳情報を登録した。\n" +
                "　私はそれに気づきながら、告発しなかった。\n" +
                "　……私にも、救えなかった息子がいたからです。\n\n" +
                "　3人分でも、娘さんは戻らなかった。\n" +
                "　欠けていた最後のひとつは、\n" +
                "　『娘さんを知っている記憶』──あなた自身だ。\n" +
                "　あなたは自ら望み、私が接続した。\n\n" +
                "　その回廊も部屋も、リナシータが\n" +
                "　あなたと3人の記憶から作った空間です。\n" +
                "　私は彼らを消さない。それが私の償いです。\n\n" +
                "　最後の工程──どの記憶が本当に娘さんの\n" +
                "　ものかを選り分けることは、\n" +
                "　父親のあなたにしか、できない。\n\n" +
                "　全ての部屋を、もう一度回ってきなさい」");

            return new[] { "message" };
        }

        /// <summary>息子の部屋（終章・隠し）：誰の記憶でもない、祈りで作られた部屋</summary>
        private static string[] FurnishSonRoom(Transform t, float hw, float hd, float h)
        {
            var wood = GetMat("LP_Furniture", new Color(0.4f, 0.3f, 0.22f), 0.2f);
            var cloth = GetMat("LP_Bedding", new Color(0.55f, 0.52f, 0.5f), 0.1f);
            var paper = GetMat("LP_Paper", new Color(0.85f, 0.83f, 0.75f), 0.1f);
            var shoe = GetMat("LP_SmallShoe", new Color(0.75f, 0.3f, 0.25f), 0.2f);

            // 窓から差し込む柔らかい白い光
            var sun = new GameObject("WhiteLight");
            sun.transform.SetParent(t, false);
            sun.transform.localPosition = new Vector3(hw - 0.6f, 1.8f, 0.5f);
            var wl2 = sun.AddComponent<Light>();
            wl2.type = LightType.Point; wl2.color = new Color(1f, 0.98f, 0.92f);
            wl2.intensity = 2.2f; wl2.range = 6f;

            // 小さなベッドと積み木
            var bed = new GameObject("SmallBed"); bed.transform.SetParent(t, false);
            bed.transform.localPosition = new Vector3(-hw + 0.8f, 0f, 0.8f);
            Box(bed.transform, "Frame", new Vector3(0f, 0.18f, 0f), new Vector3(0.8f, 0.36f, 1.5f), wood);
            Box(bed.transform, "Mattress", new Vector3(0f, 0.42f, 0f), new Vector3(0.75f, 0.12f, 1.4f), cloth);
            var blockColors = new[] { new Color(0.8f, 0.3f, 0.3f), new Color(0.3f, 0.5f, 0.8f), new Color(0.85f, 0.75f, 0.3f) };
            for (int i = 0; i < 3; i++)
                Deco(t, $"ToyBlock_{i}", new Vector3(0.3f + i * 0.18f, 0.06f, -0.6f), new Vector3(0.12f, 0.12f, 0.12f),
                    GetMat($"LP_Toy_{i}", blockColors[i], 0.2f));
            // ラグ、東の窓（白いカーテン越しの光）、壁のクレヨン画、玩具の棚、小さな椅子
            Rug(t, new Vector3(0.2f, 0f, 0.2f), new Vector2(1.8f, 1.6f), new Color(0.55f, 0.65f, 0.7f));
            Window(t, new Vector3(hw - 0.08f, 1.45f, 1.4f), 90f, 1.1f, 1.0f, true, new Color(0.92f, 0.92f, 0.9f));
            var crayon = GetMat("LP_Crayon", new Color(0.95f, 0.9f, 0.75f), 0.1f);
            for (int i = 0; i < 3; i++)   // 北壁の、出口扉の左側に並ぶ
                Deco(t, "Drawing", new Vector3(-1.7f + i * 0.45f, 1.5f + (i % 2) * 0.1f, hd - 0.09f), new Vector3(0.3f, 0.24f, 0.01f), crayon);
            Box(t, "ToyShelf", new Vector3(-hw + 0.3f, 0.6f, -1.2f), new Vector3(0.35f, 1.2f, 1.0f), wood);
            for (int i = 0; i < 3; i++)
                Deco(t, "ShelfToy", new Vector3(-hw + 0.3f, 0.3f + i * 0.4f + 0.08f, -1.4f + i * 0.2f), new Vector3(0.14f, 0.14f, 0.14f),
                    GetMat($"LP_Toy_{i}", blockColors[i], 0.2f));
            Box(t, "SmallChair", new Vector3(hw - 1.0f, 0.15f, -0.5f), new Vector3(0.3f, 0.3f, 0.3f), wood);

            // 机の上：丁寧に揃えられた小さな運動靴と、治療計画書
            var desk = Desk(t, "Desk", new Vector3(hw - 1.0f, 0f, -1.2f), wood);
            foreach (float dx in new[] { -0.08f, 0.08f })
                Deco(desk.transform, "SmallShoe", new Vector3(-0.35f + dx, 0.78f, 0.1f), new Vector3(0.07f, 0.06f, 0.17f), shoe);
            Findable(desk.transform, "plan", "古いファイル", new Vector3(0.3f, 0.76f, 0f), paper,
                new Vector3(0.32f, 0.02f, 0.24f),
                "書きかけの治療計画書",
                "《治療計画書（初版・手書き）》\n\n" +
                "対象: 小川 ▓▓（当時7歳）\n" +
                "起案: 小川 暁\n\n" +
                "「必ず、もう一度あの声を聞く」\n\n" +
                "余白いっぱいの計算式。何百回も消した跡。\n" +
                "計画書は、完成しないまま古びていた。\n\n" +
                "──この部屋には、残響がひとつも無い。\n" +
                "誰の記憶でもないからだ。\n" +
                "ここは記憶ではなく、祈りで作られた部屋。\n\n" +
                "（試験実装はここまで。この先──娘の断片収集と\n" +
                "　アップロードは、次の実装で続く）");

            return new[] { "plan" };
        }

        // ============================== 残響（人物残像）ビルダー ==============================

        /// <summary>
        /// 人物残像（残響）を組み立てる。対話なし・接近で再生・踏み込むと霧散。
        /// 視点差ペアは同じ場面をsway/pitch/大きさの差分だけ変えて2部屋に置く。
        /// figures: (位置, 向きyaw, 身長スケール, 傾きlean, 揺れ倍率, 黒い影か)
        /// </summary>
        private static void Echo(Transform room, string id, string title, string body,
                                 float sway, float pitch, float volume,
                                 params (Vector3 pos, float yaw, float height, float lean, float swayMul, bool dark)[] figures)
        {
            var root = new GameObject("Echo_" + id);
            root.transform.SetParent(room, false);
            // 中心＝人物の重心（接近判定と声の位置）
            var center = Vector3.zero;
            foreach (var f in figures) center += f.pos;
            center /= Mathf.Max(1, figures.Length);
            root.transform.localPosition = center;

            var white = GlassMat("LP_EchoWhite", new Color(0.82f, 0.88f, 1f, 0.34f));
            var darkM = GlassMat("LP_EchoDark", new Color(0.02f, 0.02f, 0.03f, 0.9f));
            var list = new List<Transform>();
            var sways = new List<float>();
            foreach (var f in figures)
            {
                var fig = new GameObject("Figure");
                fig.transform.SetParent(root.transform, false);
                fig.transform.localPosition = f.pos - center;
                fig.transform.localRotation = Quaternion.Euler(0f, f.yaw, f.lean);
                var m = f.dark ? darkM : white;
                float s = f.height;
                EchoPart(fig.transform, PrimitiveType.Capsule,
                    new Vector3(0f, 0.85f * s, 0f), new Vector3(0.4f * s, 0.62f * s, 0.32f * s), m);
                EchoPart(fig.transform, PrimitiveType.Sphere,
                    new Vector3(0f, 1.62f * s, 0f), new Vector3(0.26f * s, 0.28f * s, 0.26f * s), m);
                list.Add(fig.transform);
                sways.Add(f.swayMul);
            }

            var e = root.AddComponent<EchoScene>();
            e.EchoId = id;
            e.NoteTitle = title;
            e.NoteBody = body;
            e.SwayAmount = sway;
            e.VoicePitch = pitch;
            e.VoiceVolume = volume;
            e.Figures = list.ToArray();
            e.FigureSway = sways.ToArray();
            EchoVoice(e);
        }

        /// <summary>台詞ごとのピッチ（黒い影＝主人公の声を低く歪ませる等）。無い残響は全て1.0</summary>
        private static readonly Dictionary<string, float[]> EchoLinePitch = new Dictionary<string, float[]>
        {
            { "talk_mizuno", new[] { 0.78f, 1f, 0.78f, 1f } },   // 顔のない影の声だけ低く
            { "argue_kuroda", new[] { 1f, 0.92f, 1f, 0.92f } },   // 怒鳴る佐伯を少し太く
        };

        /// <summary>
        /// Irodori-TTSで生成した台詞（Assets/Audio/Echo/&lt;id&gt;_01.wav…）を残響に流し込む。
        /// 見つからなければ台詞なし＝こもった話し声のまま。無音指定（volume 0）の残響には付けない。
        /// </summary>
        private static void EchoVoice(EchoScene e)
        {
            if (e.VoiceVolume <= 0.001f) return;
            var clips = new List<AudioClip>();
            for (int i = 1; i < 20; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/Echo/{e.EchoId}_{i:00}.wav");
                if (clip == null) break;
                clips.Add(clip);
            }
            if (clips.Count == 0) return;
            e.Lines = clips.ToArray();
            e.VoiceVolume = 0.8f;   // 台詞は聞き取れる音量にする（話し声ループより大きめ）
            if (EchoLinePitch.TryGetValue(e.EchoId, out var pitches)) e.LinePitch = pitches;
        }

        /// <summary>残響のシルエット部品（コライダー無し＝干渉しない）</summary>
        private static void EchoPart(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var p = GameObject.CreatePrimitive(type);
            p.name = type.ToString();
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(p.GetComponent<Collider>());
        }

        /// <summary>
        /// 起床カットシーン（Timeline）を組み立てる。
        /// カメラがベッドの枕元（仰向け）から起き上がり、入口スポーンの目線へ移る。
        /// LoopIntroがゲーム開始時に一度だけ再生する。
        /// </summary>
        private static void BuildIntroCutscene(Transform dimRoot, Vector3 bedLocal, float hd)
        {
            if (!AssetDatabase.IsValidFolder("Assets/EscapePrototype/Cutscenes"))
                AssetDatabase.CreateFolder("Assets/EscapePrototype", "Cutscenes");
            const string path = "Assets/EscapePrototype/Cutscenes/LoopIntro.playable";
            AssetDatabase.DeleteAsset(path);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = "LoopIntro";
            timeline.editorSettings.frameRate = 60f;
            AssetDatabase.CreateAsset(timeline, path);

            // 3キーの起き上がり: 仰向け → 上体を起こす → 立ち上がって入口スポーンの目線へ。
            // 注意: Animator自身のTransformをカーブで動かすとルートモーション扱いになり
            // 再生モードで相対累積されてしまう。必ず子（Rig）のパスに対してカーブを書く
            var clip = new AnimationClip { name = "WakeUp", frameRate = 60f };
            void Curve(string prop, float a, float b, float c)
            {
                var cv = new AnimationCurve(
                    new Keyframe(0f, a, 0f, 0f),
                    new Keyframe(2.6f, b, 0f, 0f),
                    new Keyframe(5.2f, c, 0f, 0f));
                clip.SetCurve("Rig", typeof(Transform), prop, cv);
            }
            var eye = new Vector3(0f, 1.65f, -hd + 1.2f);   // 入口スポーンの目線
            Curve("localPosition.x", bedLocal.x, bedLocal.x + 0.3f, eye.x);
            Curve("localPosition.y", 0.85f, 1.2f, eye.y);
            Curve("localPosition.z", bedLocal.z - 0.65f, bedLocal.z - 0.4f, eye.z);
            Curve("localEulerAnglesRaw.x", -75f, -15f, 0f);
            Curve("localEulerAnglesRaw.y", 30f, 15f, 0f);
            Curve("localEulerAnglesRaw.z", -22f, -6f, 0f);
            AssetDatabase.AddObjectToAsset(clip, timeline);

            var track = timeline.CreateTrack<AnimationTrack>(null, "Camera");
            var tc = track.CreateClip(clip);
            tc.start = 0.0;
            tc.duration = 5.2;
            tc.displayName = "WakeUp";
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            // カットシーン用カメラ（普段は非アクティブ。再生中だけ有効）。
            // Animatorは親、Camera本体は子Rig（ルートモーション化を避ける構造）
            var camGo = new GameObject("IntroCamera");
            camGo.transform.SetParent(dimRoot, false);
            camGo.AddComponent<Animator>();
            var rig = new GameObject("Rig");
            rig.transform.SetParent(camGo.transform, false);
            var cam = rig.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            rig.AddComponent<AudioListener>();
            camGo.SetActive(false);

            var dirGo = new GameObject("IntroDirector");
            dirGo.transform.SetParent(dimRoot, false);
            var director = dirGo.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.None;
            foreach (var t2 in timeline.GetOutputTracks())
                if (t2 is AnimationTrack) director.SetGenericBinding(t2, camGo);

            var intro = dimRoot.gameObject.AddComponent<LoopIntro>();
            intro.Director = director;
            intro.IntroCamera = camGo;
        }

        /// <summary>脚付きの机（天板の上面がy=0.75）</summary>
        private static GameObject Desk(Transform parent, string name, Vector3 pos, Material mat)
        {
            var desk = new GameObject(name);
            desk.transform.SetParent(parent, false);
            desk.transform.localPosition = pos;
            Box(desk.transform, "Top", new Vector3(0f, 0.72f, 0f), new Vector3(1.4f, 0.06f, 0.7f), mat);
            foreach (var (dx, dz) in new[] { (-0.62f, -0.28f), (0.62f, -0.28f), (-0.62f, 0.28f), (0.62f, 0.28f) })
                Box(desk.transform, "Leg", new Vector3(dx, 0.35f, dz), new Vector3(0.07f, 0.7f, 0.07f), mat);
            return desk;
        }

        /// <summary>調べられる情報アイテム（LoopFindable付き）</summary>
        private static GameObject Findable(Transform parent, string id, string displayName, Vector3 pos,
                                           Material mat, Vector3 size, string noteTitle, string noteBody)
        {
            var go = Box(parent, "Find_" + id, pos, size, mat);
            // 調べるためのレイ（QueryTriggerInteraction.Collide）は通すが、
            // 通行の邪魔にはしない＝トリガーコライダーにする
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            var f = go.AddComponent<LoopFindable>();
            f.Id = id;
            f.DisplayName = displayName;
            f.NoteTitle = noteTitle;
            f.NoteBody = noteBody;
            f.Highlight = go.GetComponent<Renderer>();
            return go;
        }

        /// <summary>
        /// ギミック（LoopLockBase派生）付きの箱。調べられるようトリガーコライダーにし、
        /// 部屋Id・必須Id・表示名・根拠資料Idを流し込む。細かい設定は戻り値で行う
        /// </summary>
        private static T Lock<T>(Transform parent, string name, Vector3 pos, Vector3 size, Material mat,
                                 string roomId, string id, string displayName, string hintDocId) where T : LoopLockBase
        {
            var go = Box(parent, name, pos, size, mat);
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            var lockComp = go.AddComponent<T>();
            lockComp.RoomId = roomId;
            lockComp.Id = id;
            lockComp.DisplayName = displayName;
            lockComp.HintDocId = hintDocId;
            return lockComp;
        }

        /// <summary>小さな3Dテキスト（案内・掲示用）</summary>
        private static void TextPlate(Transform parent, string text, Vector3 pos, float yaw,
                                      Color color, float size)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 48;
            tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            var font = FontProvider.Get();
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }

        private static GameObject RoomDoor(Transform parent, string name, Vector3 pos, float yaw,
                                           string roomId, bool isExit, bool requiresBreaker, Material mat)
        {
            var unit = new GameObject(name);
            unit.transform.SetParent(parent, false);
            unit.transform.localPosition = pos;
            unit.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var panel = Box(unit.transform, "Panel", new Vector3(0f, 1.05f, 0f), new Vector3(0.92f, 2.1f, 0.08f), mat);
            var door = panel.AddComponent<LoopRoomDoor>();
            door.RoomId = roomId;
            door.IsExitDoor = isExit;
            door.RequiresBreakerUp = requiresBreaker;   // チュートリアルは両扉ともブレイカー必須
            return unit;
        }

        private static BreakerSwitch BuildBreaker(Transform parent, string roomId, Vector3 pos)
        {
            var boxMat = GetMat("LP_Breaker", new Color(0.35f, 0.38f, 0.42f), 0.4f);
            var leverMat = GetMat("LP_BreakerLever", new Color(0.85f, 0.25f, 0.2f), 0.3f);
            var unit = new GameObject("Breaker_" + roomId);
            unit.transform.SetParent(parent, false);
            unit.transform.localPosition = pos;
            Box(unit.transform, "Body", new Vector3(0f, 1.35f, 0f), new Vector3(0.25f, 0.8f, 0.5f), boxMat);
            var lever = Box(unit.transform, "Lever", new Vector3(-0.16f, 1.35f, 0f), new Vector3(0.1f, 0.22f, 0.12f), leverMat);
            // 本体でもレバーでも視線が通るよう、コンポーネントはユニットのルートに付ける
            //（子コライダーからGetComponentInParentで解決される）
            var sw = unit.AddComponent<BreakerSwitch>();
            sw.RoomId = roomId;
            sw.Lever = lever.transform;
            return sw;
        }

        // ============================== ライティング・プレイヤー・マネージャー ==============================

        private static void BuildLighting()
        {
            // 白い明るめの空間（ホラーの「昼の白さ」）。フォグは薄く
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.44f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.55f, 0.56f, 0.58f);
            RenderSettings.fogDensity = 0.015f;

            var root = new GameObject("CorridorLights").transform;
            float mid = (InnerHalf + OuterHalf) * 0.5f;
            foreach (var pos in new[]
            {
                new Vector3(0f, H - 0.3f, mid), new Vector3(0f, H - 0.3f, -mid),
                new Vector3(mid, H - 0.3f, 0f), new Vector3(-mid, H - 0.3f, 0f),
                new Vector3(mid, H - 0.3f, mid) * 0.98f, new Vector3(-mid, H - 0.3f, mid) * 0.98f,
                new Vector3(mid, H - 0.3f, -mid) * 0.98f, new Vector3(-mid, H - 0.3f, -mid) * 0.98f,
            })
            {
                var go = new GameObject("CorridorLight");
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.98f, 0.95f);
                l.intensity = 1.4f;
                l.range = 10f;
            }
        }

        private static GameObject BuildPlayer()
        {
            var player = new GameObject("Player");
            // 最初の部屋（薄暗い部屋）の入口付近から開始。部屋区画はindex 0
            var first = RoomDefs[0];
            player.transform.position = RoomOrigin + new Vector3(0f, 0.1f, -first.d * 0.5f + 1.6f);
            player.tag = "Player";

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.93f, 0f);
            cc.stepOffset = 0.35f;

            var inputs = player.AddComponent<StarterAssetsInputs>();
            inputs.cursorLocked = true; inputs.cursorInputForLook = true;

            var camRoot = new GameObject("PlayerCameraRoot");
            camRoot.transform.SetParent(player.transform, false);
            camRoot.transform.localPosition = new Vector3(0f, 1.55f, 0f);

            var camGo = new GameObject("MainCamera");
            camGo.transform.SetParent(camRoot.transform, false);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.08f; cam.fieldOfView = 70f;
            camGo.AddComponent<AudioListener>();
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            var fpc = player.AddComponent<FirstPersonController>();
            fpc.CinemachineCameraTarget = camRoot;
            fpc.MoveSpeed = 2.4f; fpc.SprintSpeed = 4.6f; fpc.RotationSpeed = 1.0f;
            fpc.SpeedChangeRate = 30f;
            fpc.GroundLayers = ~0;

            player.AddComponent<PlayerStatus>();
            player.AddComponent<CrouchController>();
            camRoot.AddComponent<Flashlight>();

            var interaction = player.AddComponent<InteractionController>();
            var iso = new SerializedObject(interaction);
            iso.FindProperty("_camera").objectReferenceValue = cam;
            iso.FindProperty("_interactDistance").floatValue = 3.5f;
            iso.FindProperty("_interactLayer").intValue = ~0;
            iso.ApplyModifiedPropertiesWithoutUndo();

            player.AddComponent<DebugPlayerDriver>();
            return player;
        }

        private static void BuildManagers(GameObject player, GameObject corridor)
        {
            var root = new GameObject("Managers");

            // リスポーンは最初の部屋（薄暗い部屋＝人形の部屋）の入口スポーンと同じ位置
            var respawn = new GameObject("RespawnPoint");
            respawn.transform.SetParent(root.transform, false);
            respawn.transform.position = RoomOrigin + new Vector3(0f, 0.1f, -RoomDefs[0].d * 0.5f + 1.2f);

            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(root.transform, false);
            var gm = gmGo.AddComponent<GameManager>();
            var so = new SerializedObject(gm);
            so.FindProperty("_player").objectReferenceValue = player.transform;
            so.FindProperty("_respawnPoint").objectReferenceValue = respawn.transform;
            so.FindProperty("_dolls").intValue = 5;
            so.ApplyModifiedPropertiesWithoutUndo();

            New(root, "PuzzleState").AddComponent<PuzzleState>();
            New(root, "HUD").AddComponent<HUDManager>();
            New(root, "MenuManager").AddComponent<MenuManager>();
            New(root, "PuzzleUI").AddComponent<PuzzleUI>();
            New(root, "BreakerSystem").AddComponent<BreakerSystem>();
            New(root, "UnlockDialog").AddComponent<LoopUnlockDialog>();
            New(root, "UiQueue").AddComponent<UiQueue>();
            New(root, "LoopPuzzleUI").AddComponent<LoopPuzzleUI>();
            New(root, "RoomTitle").AddComponent<RoomTitleUI>();
            New(root, "Toast").AddComponent<ToastUI>();
            var cd = New(root, "CutsceneDirector").AddComponent<CutsceneDirector>();
            cd.PlayOnStart = false;   // 起床カットシーンはLoopIntroが起動する

            var rts = New(root, "RoomTransition").AddComponent<RoomTransitionSystem>();
            rts.CorridorRoot = corridor;
        }

        private static GameObject New(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        /// <summary>部屋名の3Dテキストプレート</summary>
        private static void NamePlate(Transform parent, string text, Vector3 pos, float yaw, Color color)
        {
            var go = new GameObject("NamePlate");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 48;
            tm.characterSize = 0.055f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            var font = FontProvider.Get();
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }

        // ============================== 共通ヘルパー ==============================

        private static Material WoodFloorMat()
        {
            var mat = GetMat("LP_WoodFloor", new Color(0.72f, 0.6f, 0.48f), 0.25f);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EscapePrototype/Textures/floor_wood.png");
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>装飾用の箱（コライダー無し）。天井の格子や細い部材など通行に関係ないもの用</summary>
        private static GameObject Deco(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = Box(parent, name, pos, size, mat);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>金属マテリアル（メタリック値付き）</summary>
        private static Material MetalMat(string name, Color color, float smoothness, float metallic)
        {
            var m = GetMat(name, color, smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>自発光マテリアル</summary>
        private static Material EmissiveMat(string name, Color baseColor, Color emission)
        {
            var m = GetMat(name, baseColor, 0.2f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", emission);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>透明ガラス（URP Lit / Transparent）</summary>
        private static Material GlassMat(string name, Color color)
        {
            var m = GetMat(name, color, 0.95f);
            m.SetFloat("_Surface", 1f);   // Transparent
            m.SetFloat("_Blend", 0f);     // Alpha
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material GetMat(string name, Color color, float smoothness = 0.2f)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private const float UvPerMeter = 0.5f;
        private static readonly Dictionary<Vector3, Mesh> MeshCache = new Dictionary<Vector3, Mesh>();

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMeshWorldUV(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        private static Mesh BoxMeshWorldUV(Vector3 s)
        {
            if (MeshCache.TryGetValue(s, out var cached) && cached != null) return cached;
            Vector3 h = s * 0.5f;
            var verts = new List<Vector3>(); var norms = new List<Vector3>();
            var uvs = new List<Vector2>(); var tris = new List<int>();
            void Face(Vector3 center, Vector3 right, Vector3 up, Vector3 n, float uSize, float vSize)
            {
                int i0 = verts.Count;
                verts.Add(center - right - up); verts.Add(center - right + up);
                verts.Add(center + right + up); verts.Add(center + right - up);
                for (int i = 0; i < 4; i++) norms.Add(n);
                float u = uSize * UvPerMeter, v = vSize * UvPerMeter;
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, v));
                uvs.Add(new Vector2(u, v)); uvs.Add(new Vector2(u, 0));
                tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
            Face(new Vector3(0, 0, -h.z), new Vector3(h.x, 0, 0), new Vector3(0, h.y, 0), Vector3.back, s.x, s.y);
            Face(new Vector3(0, 0, h.z), new Vector3(-h.x, 0, 0), new Vector3(0, h.y, 0), Vector3.forward, s.x, s.y);
            Face(new Vector3(-h.x, 0, 0), new Vector3(0, 0, -h.z), new Vector3(0, h.y, 0), Vector3.left, s.z, s.y);
            Face(new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), new Vector3(0, h.y, 0), Vector3.right, s.z, s.y);
            Face(new Vector3(0, h.y, 0), new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), Vector3.up, s.x, s.z);
            Face(new Vector3(0, -h.y, 0), new Vector3(h.x, 0, 0), new Vector3(0, 0, -h.z), Vector3.down, s.x, s.z);
            var mesh = new Mesh { name = $"BoxUV_{s.x:0.##}x{s.y:0.##}x{s.z:0.##}" };
            mesh.SetVertices(verts); mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            MeshCache[s] = mesh;
            return mesh;
        }

        // ============================== Blender製ローポリ什器（Assets/Models/Props/*.fbx） ==============================

        /// <summary>
        /// Blenderで作った什器FBXを配置する。
        /// ・埋め込みマテリアル（sRGB変換で明るくなる）を名前ベースの調整済みURPマテリアルへ差し替え
        /// ・レンダラー境界からローカルAABBを求めてBoxColliderを付ける（回転しても正しい）
        /// ・FBXが無ければnull（呼び出し側は従来の箱で代替する）
        /// モデルの向き: Unity +Z＝正面（椅子は膝側、机はモニター側、ラックは扉、ベッドは足側）
        /// </summary>
        private static GameObject Prop(Transform parent, string fbx, Vector3 pos, float yaw = 0f,
                                       bool collider = true, Material screenOverride = null)
        {
            var go = Place($"Assets/Models/Props/{fbx}.fbx", parent, pos, yaw);
            if (go == null) return null;
            go.name = fbx;
            var rs = go.GetComponentsInChildren<Renderer>();
            foreach (var r in rs)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (screenOverride != null && mats[i].name.StartsWith("Emit_Screen")) mats[i] = screenOverride;
                    else mats[i] = PropMat(mats[i]);
                }
                r.sharedMaterials = mats;
            }
            if (collider)
            {
                // 境界ボックスだと天板やベッドの上に置いた資料まで箱の中に入ってしまい、
                // 視線のレイが資料に届かなくなる。形状どおりのメッシュコライダーにする
                //（静的な什器なので非convexでよい。歩行の衝突も形状どおりになる）
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                }
            }
            return go;
        }

        /// <summary>FBX埋め込みマテリアル名 → URPの調整済みマテリアル（色・質感・発光を意図通りに）</summary>
        private static Material PropMat(Material src)
        {
            string n = src.name;
            switch (n)
            {
                case "RackBody":     return GetMat("LP_Prop_RackBody", new Color(0.12f, 0.13f, 0.15f), 0.4f);
                case "RackDoor":     return MetalMat("LP_Prop_RackDoor", new Color(0.2f, 0.21f, 0.23f), 0.45f, 0.3f);
                case "BlackPlastic": return GetMat("LP_Prop_BlackPlastic", new Color(0.08f, 0.08f, 0.09f), 0.4f);
                case "ChairFabric":  return GetMat("LP_Prop_ChairFabric", new Color(0.18f, 0.2f, 0.26f), 0.05f);
                case "Chrome":       return MetalMat("LP_Prop_Chrome", new Color(0.75f, 0.76f, 0.78f), 0.85f, 0.8f);
                case "BedFrame":     return MetalMat("LP_Prop_BedFrame", new Color(0.78f, 0.8f, 0.82f), 0.6f, 0.6f);
                case "Mattress":     return GetMat("LP_Prop_Mattress", new Color(0.93f, 0.94f, 0.95f), 0.1f);
                case "Blanket":      return GetMat("LP_Prop_Blanket", new Color(0.62f, 0.72f, 0.78f), 0.05f);
                case "Pillow":       return GetMat("LP_Prop_Pillow", new Color(0.97f, 0.97f, 0.97f), 0.05f);
                case "ChairWood":    return GetMat("LP_Prop_ChairWood", new Color(0.42f, 0.3f, 0.2f), 0.3f);
                case "ChairCushion": return GetMat("LP_Prop_ChairCushion", new Color(0.55f, 0.35f, 0.3f), 0.05f);
                case "DeskLaminate": return GetMat("LP_Prop_DeskLaminate", new Color(0.62f, 0.6f, 0.58f), 0.4f);
                case "DeskFrame":    return MetalMat("LP_Prop_DeskFrame", new Color(0.35f, 0.36f, 0.38f), 0.5f, 0.4f);
                case "IvBag":        return GetMat("LP_Prop_IvBag", new Color(0.85f, 0.9f, 0.95f), 0.7f);
                case "IvLabel":      return GetMat("LP_Prop_IvLabel", new Color(0.95f, 0.95f, 0.92f), 0.2f);
                case "Emit_Green":   return EmissiveMat("LP_Prop_LedGreen", new Color(0.1f, 0.3f, 0.25f), new Color(0.3f, 1f, 0.7f) * 1.2f);
                case "Emit_Amber":   return EmissiveMat("LP_Prop_LedAmber", new Color(0.3f, 0.2f, 0.05f), new Color(1f, 0.6f, 0.15f) * 1.2f);
                case "Emit_Screen":  return EmissiveMat("LP_Prop_Screen", new Color(0.12f, 0.22f, 0.38f), new Color(0.25f, 0.55f, 1f) * 0.8f);
                default:             return GetMat("LP_Prop_" + n, src.color, 0.3f);
            }
        }

        private static GameObject Place(string path, Transform parent, Vector3 pos,
                                        float yRot = 0f, float targetHeight = -1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            if (targetHeight > 0f)
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    if (b.size.y > 0.0001f)
                    {
                        go.transform.localScale *= targetHeight / b.size.y;
                        b = rs[0].bounds;
                        foreach (var r in rs) b.Encapsulate(r.bounds);
                        go.transform.position += new Vector3(0f, pos.y - b.min.y, 0f);   // 床(y0)に接地
                    }
                }
            }
            return go;
        }
    }
}
