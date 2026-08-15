using System.Collections.Generic;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

        // 解放順: 薄暗い部屋 → 書斎 → 電車車内 → 研究所応接室
        // 出口は自動で「入口の反対側の辺」の同スロットに繋がる
        private static readonly RoomDef[] RoomDefs =
        {
            // 狭く天井が低い寝室。圧迫感のある薄暗い部屋
            new RoomDef("dim",   "薄暗い部屋",   0, 2, 4,  6.0f,  7.5f, 2.6f),
            // 落ち着いた書斎。天井は少し高く、奥行きは控えめ
            new RoomDef("study", "書斎",         1, 0, 2,  5.5f,  7.0f, 2.9f),
            // 電車車内。幅は狭く、前後に長い（中吊り広告の下を通れるよう天井は高め）
            new RoomDef("train", "電車車内",     2, 1, 6,  3.0f, 18.0f, 3.0f),
            // 研究所の応接室。5人分の机が入る広い空間
            new RoomDef("lab",   "研究所応接室", 3, 3, 3, 13.0f, 10.0f, 3.2f),
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
            _ => Color.gray,
        };

        private static void BuildRoom(Transform parent, RoomDef def, int index)
        {
            var root = new GameObject($"Room_{def.id}");
            root.transform.SetParent(parent, false);
            // 部屋ごとに専用区画へ配置（重ならないよう十分離す）
            root.transform.position = RoomOrigin + new Vector3(index * RoomSpacing, 0f, 0f);
            var t = root.transform;

            var wall = GetMat("LP_RoomWall", new Color(0.78f, 0.78f, 0.76f), 0.15f);
            var floor = GetMat("LP_RoomFloor", new Color(0.5f, 0.48f, 0.45f), 0.15f);
            var ceil = GetMat("LP_Ceiling", new Color(0.96f, 0.96f, 0.97f), 0.1f);
            var doorMat = GetMat("LP_Door", new Color(0.82f, 0.78f, 0.72f), 0.3f);
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

            // 入口扉（南）／出口扉（北）
            bool startRoom = def.id == LoopProgress.StartRoomId;
            RoomDoor(t, "EntryDoor", new Vector3(0f, 0f, -hd + 0.1f), 0f, def.id, false, startRoom, doorMat);
            RoomDoor(t, "ExitDoor", new Vector3(0f, 0f, hd - 0.1f), 180f, def.id, true, startRoom, doorMat);

            // スポーン地点（扉のすぐ内側）
            var entrySpawn = new GameObject("EntrySpawn").transform;
            entrySpawn.SetParent(t, false);
            entrySpawn.localPosition = new Vector3(0f, 0.05f, -hd + 1.2f);
            var exitSpawn = new GameObject("ExitSpawn").transform;
            exitSpawn.SetParent(t, false);
            exitSpawn.localPosition = new Vector3(0f, 0.05f, hd - 1.2f);

            // ブレイカー（東壁。細長い部屋では入口寄りに置く）
            var breaker = BuildBreaker(t, def.id, new Vector3(hw - 0.35f, 0f, def.d > 12f ? -hd + 3f : 0f));

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

        /// <summary>部屋の照明。長い部屋には複数灯、薄暗い部屋は暗めにする</summary>
        private static void BuildRoomLights(Transform t, RoomDef def)
        {
            int count = Mathf.Clamp(Mathf.CeilToInt(def.d / 6f), 1, 4);
            bool dim = def.id == "dim";
            for (int i = 0; i < count; i++)
            {
                float z = count == 1 ? 0f : -def.d * 0.5f + def.d * (i + 0.5f) / count;
                var go = new GameObject("RoomLight");
                go.transform.SetParent(t, false);
                go.transform.localPosition = new Vector3(0f, def.h - 0.35f, z);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = dim ? new Color(0.85f, 0.82f, 0.78f) : new Color(1f, 0.96f, 0.9f);
                l.intensity = dim ? 0.5f : 2.0f;       // 薄暗い部屋はランプが主役
                l.range = dim ? 6f : 12f;
                l.shadows = LightShadows.Soft;
            }
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

            // 懐中電灯（机の上）
            Findable(desk.transform, "flashlight", "懐中電灯", new Vector3(0.15f, 0.79f, -0.1f), metal,
                new Vector3(0.07f, 0.07f, 0.28f),
                "懐中電灯",
                "机の上にあった懐中電灯。まだ電池は残っている。\n[F] で点灯できる。");

            return new[] { "news", "flashlight" };
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

            return new[] { "document" };
        }

        /// <summary>電車車内：ロングシート・吊革／天井の中吊り広告。
        /// 前後の妻面は扉開口を塞がないよう左右に分割する（＝必ず出口がある）</summary>
        private static string[] FurnishTrain(Transform t, float hw, float hd, float h)
        {
            var seat = GetMat("LP_TrainSeat", new Color(0.2f, 0.35f, 0.5f), 0.15f);
            var metal = GetMat("LP_Metal", new Color(0.45f, 0.47f, 0.5f), 0.5f);
            var adMat = GetMat("LP_TrainAd", new Color(0.96f, 0.95f, 0.9f), 0.1f);

            float seatLen = hd * 2f - 3.0f;   // 妻面まわりは空ける
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f;
                float x = sx * (hw - 0.32f);
                Box(t, "Seat", new Vector3(x, 0.42f, 0f), new Vector3(0.55f, 0.12f, seatLen), seat);
                Box(t, "SeatBack", new Vector3(sx * (hw - 0.09f), 0.85f, 0f), new Vector3(0.12f, 0.9f, seatLen), seat);
                Box(t, "Handrail", new Vector3(sx * (hw - 0.75f), h - 0.5f, 0f), new Vector3(0.05f, 0.05f, seatLen), metal);
                // 吊革（前後に並べる）
                int straps = Mathf.FloorToInt(seatLen / 1.4f);
                for (int k = 0; k < straps; k++)
                {
                    float z = -seatLen * 0.5f + (k + 0.5f) * (seatLen / straps);
                    Box(t, "Strap", new Vector3(sx * (hw - 0.75f), h - 0.72f, z), new Vector3(0.025f, 0.4f, 0.025f), metal);
                    Box(t, "StrapRing", new Vector3(sx * (hw - 0.75f), h - 0.98f, z), new Vector3(0.11f, 0.11f, 0.025f), metal);
                }
            }

            // 妻面（前後）：中央の扉開口を避けて左右にパネルを置く＝通り抜けられる
            const float openW = 1.15f;
            float panelW = hw - openW * 0.5f;
            if (panelW > 0.1f)
                foreach (float zSign in new[] { 1f, -1f })
                    foreach (float xSign in new[] { -1f, 1f })
                        Box(t, "EndPanel", new Vector3(xSign * (openW * 0.5f + panelW * 0.5f), (h - 0.3f) * 0.5f,
                                zSign * (hd - 0.5f)), new Vector3(panelW, h - 0.3f, 0.1f), metal);

            // 天井から吊り下げられた中吊り広告（発見アイテム）。
            // 頭上を通れるよう、広告の下端がプレイヤーの身長(1.8m)より上に来る高さに吊る
            var ad = new GameObject("HangingAd"); ad.transform.SetParent(t, false);
            ad.transform.localPosition = new Vector3(0f, 0f, -1.5f);
            // 高さ0.7の広告。下端が2.2m以上になるよう吊る（プレイヤーの頭上を確実に空ける）
            const float adH = 0.7f;
            float adY = Mathf.Max(h - 0.45f, 2.2f + adH * 0.5f);
            float wireLen = Mathf.Max(0.06f, h - (adY + adH * 0.5f));
            Box(ad.transform, "Wire_L", new Vector3(-0.55f, adY + adH * 0.5f + wireLen * 0.5f, 0f),
                new Vector3(0.02f, wireLen, 0.02f), metal);
            Box(ad.transform, "Wire_R", new Vector3(0.55f, adY + adH * 0.5f + wireLen * 0.5f, 0f),
                new Vector3(0.02f, wireLen, 0.02f), metal);
            var panel = Findable(ad.transform, "ad", "中吊り広告", new Vector3(0f, adY, 0f), adMat,
                new Vector3(1.4f, adH, 0.03f),
                "中吊り広告",
                "《小川脳神経総合研究所》\n\n" +
                "　　脳神経 × AI 開発\n\n" +
                "「記憶は、置き換えられる。」\n\n" +
                "被験者募集中 — 詳しくは当研究所まで");

            // 広告の文字は片面（入口側＝-Z面）のみ。両面に置くと透けて重なり読めなくなる。
            // TextMeshは-Z側から見たとき yaw=0 で正しく読める（180だと鏡文字）
            TextPlate(panel.transform, "小川脳神経総合研究所",
                new Vector3(0f, 0.21f, -0.03f), 0f, new Color(0.16f, 0.24f, 0.52f), 0.020f);
            TextPlate(panel.transform, "脳神経×AI開発",
                new Vector3(0f, 0.0f, -0.03f), 0f, new Color(0.12f, 0.2f, 0.45f), 0.028f);
            TextPlate(panel.transform, "「記憶は、置き換えられる。」",
                new Vector3(0f, -0.22f, -0.03f), 0f, new Color(0.35f, 0.2f, 0.2f), 0.014f);

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
            for (int i = 0; i < deskPos.Length; i++)
            {
                var d = Desk(t, $"ResearcherDesk_{i + 1}", deskPos[i], deskMat);
                TextPlate(d.transform, $"研究員 {i + 1}", new Vector3(0f, 0.78f, -0.34f), 0f,
                    new Color(0.25f, 0.25f, 0.3f), 0.022f);
                // 3番目の机に研究概要を置く
                if (i == 2)
                    Findable(d.transform, "summary", "研究概要", new Vector3(0f, 0.76f, 0.05f), paper,
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

            // 研究メンバー表（北壁のボード。写真4人分・うち2人は判読不能）
            var mb = new GameObject("MemberBoard"); mb.transform.SetParent(t, false);
            mb.transform.localPosition = new Vector3(0f, 0f, hd - 0.3f);
            var plate = Findable(mb.transform, "members", "研究メンバー表", new Vector3(0f, 1.6f, 0f), board,
                new Vector3(2.2f, 1.2f, 0.06f),
                "研究メンバー表",
                "《研究メンバー》\n\n" +
                "　所長　小川 誠一（写真あり）\n" +
                "　主任　［判読不能］\n" +
                "　研究員　三嶋 律（写真あり）\n" +
                "　研究員　［判読不能］\n\n" +
                "2人分の顔写真は薬品で溶かされたように\n黒く滲んでいる。");
            // 顔写真4枚（2枚は黒く潰れている）
            var photo = GetMat("LP_Photo", new Color(0.75f, 0.74f, 0.7f), 0.1f);
            var blacked = GetMat("LP_PhotoBlacked", new Color(0.08f, 0.07f, 0.07f), 0.05f);
            for (int i = 0; i < 4; i++)
            {
                float x = -0.75f + i * 0.5f;
                Box(plate.transform, $"Photo_{i}", new Vector3(x, 0.18f, -0.04f),
                    new Vector3(0.34f, 0.4f, 0.02f), (i == 1 || i == 3) ? blacked : photo);
            }
            TextPlate(plate.transform, "研 究 メ ン バ ー", new Vector3(0f, -0.42f, -0.04f), 180f,
                new Color(0.2f, 0.2f, 0.25f), 0.03f);

            return new[] { "summary", "members" };
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
            if (roomId == LoopProgress.StartRoomId)
            {
                // 最初の部屋は静かな状態から始まり、資料（新聞・懐中電灯）を
                // 見つけた時点で落ちる（LoopProgressが降ろす）。警報は小さめ
                sw.StartsDown = false;
                sw.AlarmVolume = 0.35f;
                sw.AlarmMaxDistance = 15f;
            }
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

            var respawn = new GameObject("RespawnPoint");
            respawn.transform.SetParent(root.transform, false);
            respawn.transform.position = new Vector3(0f, 0.1f, -(InnerHalf + OuterHalf) * 0.5f);

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
