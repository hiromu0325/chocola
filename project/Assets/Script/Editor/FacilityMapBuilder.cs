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
    /// 本番用マップのビルダー（間取り図: 1F=メイン/襲撃襲来場所/隔壁×2/AI移植室/
    /// 施設隔離システム室/電気室(配電盤)/実験室/倉庫/事務所/Exit/階段、2F=私室群+上級研究員私室）。
    ///
    /// ◆ 部屋の比率変更: Rooms1F / Rooms2F / DoorList の座標を書き換えて
    ///    Tools > EscapePrototype > Facility Map > 本番マップを生成 を再実行するだけ。
    ///    壁・扉開口・天井・照明・ラベルはすべて矩形定義から自動生成される。
    ///
    /// 見た目は BasementMood と同テイスト（コンクリ躯体＋暗い電球＋フォグ＋ポスプロ）。
    /// ゲームロジック（スポナー・パズル等）の配線は未接続。ジオメトリとムードのみ。
    /// </summary>
    public static class FacilityMapBuilder
    {
        private const string ScenePath = "Assets/EscapePrototype/FacilityMap.unity";
        private const string MatDir = "Assets/EscapePrototype/MoodMaterials";
        private const string PostProfilePath = "Assets/EscapePrototype/FacilityMapPost.asset";

        private const float H = 3.0f;        // 天井高
        private const float WallT = 0.15f;   // 壁厚（各部屋が自分の壁を持つ＝境界は2枚背中合わせ）
        private const float DoorW = 1.2f;    // 開口幅
        private const float DoorH = 2.2f;    // 開口高
        private const float F2 = 3.15f;      // 2Fの床レベル

        // ================= 部屋レイアウト定義（比率調整はここを編集） =================

        private class Room
        {
            public string name;
            public float x0, z0, x1, z1;   // 床の矩形（m）
            public float lampIntensity;    // 0でランプ無し
            public Room(string n, float ax0, float az0, float ax1, float az1, float lamp)
            { name = n; x0 = ax0; z0 = az0; x1 = ax1; z1 = az1; lampIntensity = lamp; }
            public float CX => (x0 + x1) * 0.5f;
            public float CZ => (z0 + z1) * 0.5f;
        }

        private struct Door
        {
            public float x, z, w;
            public bool alongX;   // true=南北の壁にある開口（幅はX方向）
            public bool keyed;    // true=配電室の鍵で開く既存KeyedDoor（それ以外はGimmickDoor）
            public Door(float ax, float az, bool ax_, float aw = DoorW, bool locked = false)
            { x = ax; z = az; alongX = ax_; w = aw; keyed = locked; }
        }

        private static readonly Room[] Rooms1F =
        {
            new Room("Main",        0f,   8f, 10f, 18f, 3.2f),   // メイン
            new Room("Arrival",     2f,  18f,  8f, 21f, 1.4f),   // 襲撃襲来場所
            new Room("BulkheadN",   8f,  18f, 13f, 21f, 1.0f),   // 隔壁（北）
            new Room("AIRoom",     13f,  18f, 20f, 22f, 2.2f),   // AI移植室
            new Room("BulkheadC",  10f,  12f, 13f, 18f, 1.0f),   // 隔壁（中央）
            new Room("Isolation",  13f,  10f, 20f, 18f, 2.8f),   // 施設隔離システム室
            new Room("Electric",   10f,   4f, 13f, 12f, 1.8f),   // 電気室（配電盤）
            new Room("Lab",        13f,   0f, 20f, 10f, 2.8f),   // 実験室
            new Room("Warehouse",   3f,   0f, 10f,  8f, 1.6f),   // 倉庫
            new Room("Office",     -4f,   2f,  0f, 10f, 2.0f),   // 事務所
        };

        // 開口（絶対座標で1回だけ定義。両側の部屋が同じ位置を切り抜く）
        private static readonly Door[] DoorList1F =
        {
            new Door(5f,    18f, true),    // メイン ↔ 襲撃襲来場所
            new Door(8f,    19.5f, false), // 襲撃襲来場所 ↔ 隔壁N
            new Door(13f,   19.5f, false), // 隔壁N ↔ AI移植室
            new Door(10f,   15f, false),   // メイン ↔ 隔壁C
            new Door(13f,   15f, false),   // 隔壁C ↔ 施設隔離システム室
            new Door(16.5f, 10f, true),    // 施設隔離システム室 ↔ 実験室
            new Door(10f,   9.5f, false, DoorW, true),   // メイン ↔ 電気室（配電室の鍵で開くKeyedDoor）
            new Door(13f,   8f, false),    // 電気室 ↔ 実験室
            new Door(8.2f,  8f, true),     // メイン ↔ 倉庫（階段の東端より外側）
            new Door(0f,    9f, false),    // メイン ↔ 事務所
        };

        // 2F はメイン区画（x0〜10, z8〜18）の上に載る
        private static readonly Room[] Rooms2F =
        {
            // 階段ホール＝2F南端。階段は東(x7)から西(x2)へ登り、西端が踊り場。
            // 吹き抜けの北側(z10.2〜11.2)が廊下扉までの通路になる
            new Room("StairHall2F", 0.4f,  8.8f, 9.6f, 11.2f, 1.2f),
            new Room("Corridor2F",  4f,   11.2f, 6f,   16f,   1.2f),  // 中央廊下
            new Room("PrivateW1",   0.4f, 11.2f, 4f,   13.6f, 1.5f),  // 私室（西1）
            new Room("PrivateW2",   0.4f, 13.6f, 4f,   16f,   1.5f),  // 私室（西2）
            new Room("PrivateE1",   6f,   11.2f, 9.6f, 13.6f, 1.5f),  // 私室（東1）
            new Room("PrivateE2",   6f,   13.6f, 9.6f, 16f,   1.5f),  // 私室（東2）
            new Room("SeniorRoom",  0.4f, 16f,   9.6f, 19.6f, 2.2f),  // 上級研究員私室
        };

        private static readonly Door[] DoorList2F =
        {
            new Door(5f, 11.2f, true,  1.2f),  // 階段ホール ↔ 廊下
            new Door(4f, 12.4f, false, 1.0f),  // 私室W1 ↔ 廊下
            new Door(4f, 14.8f, false, 1.0f),  // 私室W2 ↔ 廊下
            new Door(6f, 12.4f, false, 1.0f),  // 私室E1 ↔ 廊下
            new Door(6f, 14.8f, false, 1.0f),  // 私室E2 ↔ 廊下
            new Door(5f, 16f,   true,  1.2f),  // 廊下 ↔ 上級研究員私室
        };

        // 2F床の吹き抜け（階段穴）。階段は x7.0→x2.0 を西向きに登る。西側(x0.4〜2.0)が踊り場
        private static readonly Vector4 StairHole = new Vector4(2.0f, 8.8f, 7.0f, 10.2f);   // x0,z0,x1,z1

        // ================= メニュー =================

        [MenuItem("Tools/EscapePrototype/Facility Map/本番マップを生成")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MeshCache.Clear();   // 前回シーンのメッシュ参照を持ち越さない
            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder("Assets/EscapePrototype", "MoodMaterials");

            var f1 = new GameObject("Floor1").transform;
            foreach (var r in Rooms1F) BuildRoom(f1, r, DoorList1F, 0f, hasCeiling: !IsUnder2F(r));
            BuildStairs(f1);
            BuildExit(f1);
            Furnish1F(f1);

            var f2 = new GameObject("Floor2").transform;
            foreach (var r in Rooms2F) BuildRoom(f2, r, DoorList2F, F2, hasCeiling: true);
            Furnish2F(f2);

            // ギミック扉（DoorList定義の全開口。階段は扉定義が無いので付かない）
            BuildGimmickDoors(f1, Rooms1F, DoorList1F, 0f);
            BuildGimmickDoors(f2, Rooms2F, DoorList2F, F2);
            BuildGimmicks();

            BuildGlobalLighting();
            BuildPostProcess();
            var player = BuildPlayer();
            BuildManagers(player);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[FacilityMap] 生成完了: {ScenePath}（部屋の比率は Rooms1F/Rooms2F/DoorList を編集して再生成）");
        }

        /// <summary>メイン区画（2Fが上に載る範囲）か。※現状メインにも天井（＝2Fの下地）を張る</summary>
        private static bool IsUnder2F(Room r) => false;

        /// <summary>穴(hole: x0,z0,x1,z1)を避けてスラブを最大4枚で張る</summary>
        private static void SlabWithHole(Transform parent, string name, float yCenter,
                                         Room r, Vector4 hole, Material mat)
        {
            void Slab(float ax0, float az0, float ax1, float az1)
            {
                if (ax1 - ax0 < 0.05f || az1 - az0 < 0.05f) return;
                Box(parent, name, new Vector3((ax0 + ax1) * 0.5f, yCenter, (az0 + az1) * 0.5f),
                    new Vector3(ax1 - ax0, 0.12f, az1 - az0), mat);
            }
            Slab(r.x0, r.z0, r.x1, hole.y);            // 南帯
            Slab(r.x0, hole.w, r.x1, r.z1);            // 北帯
            Slab(r.x0, hole.y, hole.x, hole.w);        // 西
            Slab(hole.z, hole.y, r.x1, hole.w);        // 東
        }

        // ================= 部屋の生成 =================

        private static void BuildRoom(Transform parent, Room r, Door[] doors, float y, bool hasCeiling)
        {
            var root = new GameObject(r.name).transform;
            root.SetParent(parent, false);

            var wallMat = ConcreteMat("BM_Wall", new Color(0.50f, 0.52f, 0.51f));
            var floorMat = ConcreteMat("BM_Floor", new Color(0.38f, 0.40f, 0.40f));
            var ceilMat = ConcreteMat("BM_Ceiling", new Color(0.33f, 0.35f, 0.35f));

            float w = r.x1 - r.x0, d = r.z1 - r.z0;

            // 床（2F階段ホールは吹き抜け穴を避けて張る）
            if (r.name == "StairHall2F")
                SlabWithHole(root, "Floor", y - 0.06f, r, StairHole, floorMat);
            else
                Box(root, "Floor", new Vector3(r.CX, y - 0.06f, r.CZ), new Vector3(w, 0.12f, d), floorMat);

            // 天井（メインは階段が2Fへ抜けるため同じ穴を空ける）
            if (hasCeiling)
            {
                if (r.name == "Main")
                    SlabWithHole(root, "Ceiling", y + H + 0.06f, r, StairHole, ceilMat);
                else
                    Box(root, "Ceiling", new Vector3(r.CX, y + H + 0.06f, r.CZ), new Vector3(w, 0.12f, d), ceilMat);
            }

            // 壁4面（開口は絶対座標のDoorListから自動で切り抜く）
            WallX(root, r, r.z1, true, doors, y, wallMat);    // 北
            WallX(root, r, r.z0, false, doors, y, wallMat);   // 南
            WallZ(root, r, r.x0, false, doors, y, wallMat);   // 西
            WallZ(root, r, r.x1, true, doors, y, wallMat);    // 東

            // 照明（60m²超の大部屋は自動で2灯に）
            if (r.lampIntensity > 0f)
            {
                bool big = w * d > 30f;
                if (w * d > 60f)
                {
                    Pendant(root, new Vector3(r.CX, y, r.z0 + d * 0.28f), r.lampIntensity * 0.9f, big);
                    Pendant(root, new Vector3(r.CX, y, r.z1 - d * 0.28f), r.lampIntensity * 0.9f, big);
                }
                else
                {
                    Pendant(root, new Vector3(r.CX, y, r.CZ), r.lampIntensity, big);
                }
            }
        }

        /// <summary>南北方向の壁（X方向に伸びる）。boundary上の開口を切り抜いてセグメント生成</summary>
        private static void WallX(Transform parent, Room r, float z, bool north, Door[] doors, float y, Material mat)
        {
            float zc = north ? z - WallT * 0.5f : z + WallT * 0.5f;   // 部屋の内側に寄せる
            var gaps = new List<(float a, float b)>();
            foreach (var dr in doors)
                if (dr.alongX && Mathf.Abs(dr.z - z) < 0.11f && dr.x > r.x0 && dr.x < r.x1)
                    gaps.Add((dr.x - dr.w * 0.5f, dr.x + dr.w * 0.5f));
            Segments(parent, gaps, r.x0, r.x1, (a, b) =>
            {
                Box(parent, "WallX", new Vector3((a + b) * 0.5f, y + H * 0.5f, zc),
                    new Vector3(b - a, H, WallT), mat);
            });
            foreach (var g in gaps)   // 開口上の欄間
                Box(parent, "LintelX", new Vector3((g.a + g.b) * 0.5f, y + (DoorH + H) * 0.5f, zc),
                    new Vector3(g.b - g.a, H - DoorH, WallT), mat);
        }

        /// <summary>東西方向の壁（Z方向に伸びる）</summary>
        private static void WallZ(Transform parent, Room r, float x, bool east, Door[] doors, float y, Material mat)
        {
            float xc = east ? x - WallT * 0.5f : x + WallT * 0.5f;
            var gaps = new List<(float a, float b)>();
            foreach (var dr in doors)
                if (!dr.alongX && Mathf.Abs(dr.x - x) < 0.11f && dr.z > r.z0 && dr.z < r.z1)
                    gaps.Add((dr.z - dr.w * 0.5f, dr.z + dr.w * 0.5f));
            Segments(parent, gaps, r.z0, r.z1, (a, b) =>
            {
                Box(parent, "WallZ", new Vector3(xc, y + H * 0.5f, (a + b) * 0.5f),
                    new Vector3(WallT, H, b - a), mat);
            });
            foreach (var g in gaps)
                Box(parent, "LintelZ", new Vector3(xc, y + (DoorH + H) * 0.5f, (g.a + g.b) * 0.5f),
                    new Vector3(WallT, H - DoorH, g.b - g.a), mat);
        }

        /// <summary>from〜to を gaps で分割し、各セグメントで emit を呼ぶ</summary>
        private static void Segments(Transform parent, List<(float a, float b)> gaps,
                                     float from, float to, System.Action<float, float> emit)
        {
            gaps.Sort((p, q) => p.a.CompareTo(q.a));
            float cur = from;
            foreach (var g in gaps)
            {
                if (g.a - cur > 0.05f) emit(cur, g.a);
                cur = Mathf.Max(cur, g.b);
            }
            if (to - cur > 0.05f) emit(cur, to);
        }

        // ================= 階段・Exit・什器 =================

        private static void BuildStairs(Transform parent)
        {
            var root = new GameObject("Stairs").transform;
            root.SetParent(parent, false);
            var mat = ConcreteMat("BM_Stairs", new Color(0.50f, 0.52f, 0.49f));

            // メイン南壁沿いを東(x7.0)から西(x2.0)へ登る。西壁側(x0.4〜2.0)が2F踊り場。
            // 西壁の事務所ドア・Exitの前は完全に空く。
            const int steps = 16;
            const float rise = F2 / steps;
            const float xFrom = 7.0f, xTo = 2.0f;               // 東→西
            const float run = (xFrom - xTo) / steps;
            const float zC = 9.5f, width = 1.4f;                // z8.8〜10.2
            for (int i = 0; i < steps; i++)
            {
                // 段は床まで詰めた中実のコンクリ塊（下から見ても浮かない）
                float top = rise * (i + 1);
                Box(root, $"Step_{i}",
                    new Vector3(xFrom - run * (i + 0.5f), top * 0.5f, zC),
                    new Vector3(run, top, width), mat);
            }

            // 北側パラペット（1F床から2F腰高まで。吹き抜けの転落防止を兼ねる）
            float guardTop = F2 + 0.95f;
            Box(root, "Parapet_N", new Vector3((xFrom + xTo) * 0.5f, guardTop * 0.5f, 10.2f + 0.08f),
                new Vector3(xFrom - xTo, guardTop, 0.16f), mat);
            // 2F吹き抜けの東縁ガード（階段の登り口の真上）
            Box(root, "Guard_E", new Vector3(xFrom + 0.06f, F2 + 0.45f, zC),
                new Vector3(0.12f, 0.9f, width + 0.2f), mat);
        }

        private static void BuildExit(Transform parent)
        {
            // メイン西壁（x=0, z=15.5）の脱出ドア＋緑のEXITサイン
            var root = new GameObject("Exit").transform;
            root.SetParent(parent, false);
            DoorUnit(root, new Vector3(0.12f, 0f, 15.5f), 90f);

            var signMat = GetMat("BM_ExitSign", new Color(0.1f, 0.9f, 0.35f), 0.3f);
            signMat.EnableKeyword("_EMISSION");
            signMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            signMat.SetColor("_EmissionColor", new Color(0.1f, 0.9f, 0.35f) * 2.2f);
            Box(root, "ExitSign", new Vector3(0.3f, 2.45f, 15.5f), new Vector3(0.08f, 0.22f, 0.7f), signMat);

            var l = new GameObject("ExitGlow").AddComponent<Light>();
            l.transform.SetParent(root, false);
            l.transform.position = new Vector3(0.55f, 2.4f, 15.5f);
            l.type = LightType.Point;
            l.color = new Color(0.2f, 0.95f, 0.4f);
            l.intensity = 0.8f;
            l.range = 3f;
        }

        private static void Furnish1F(Transform parent)
        {
            var root = new GameObject("Props1F").transform;
            root.SetParent(parent, false);
            var pipeMat = GetMat("BM_Pipe", new Color(0.45f, 0.44f, 0.40f), 0.35f);

            // メイン: 天井配管＋作業台まわり
            // ※配管は階段ゾーン(x2〜7, z8.8〜10.2)の頭上を横切らないよう z10.6以北に限定
            PipeRun(root, new Vector3(3.5f, H - 0.25f, 14.2f), 7.2f, 0.09f, pipeMat, alongX: false);
            PipeRun(root, new Vector3(5f, H - 0.30f, 16.5f), 9.6f, 0.07f, pipeMat, alongX: true);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_PlywoodTable_01a.prefab",
                root, new Vector3(6.5f, 0f, 12f), 15f, 0.9f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_CardboardBox_2.prefab",
                root, new Vector3(6.2f, 0.9f, 12.2f), 40f, 0.35f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_GasCan_01a.prefab",
                root, new Vector3(8.9f, 0f, 17.3f), 10f, 0.5f);

            // 電気室: 配電盤（キャビネット）＋配管＋赤アクセント
            Place("Assets/Electrical Substation/Prefabs/SM_Electrical_Substation.prefab",
                root, new Vector3(11.5f, 0f, 5.0f), 180f, 1.9f);
            PipeRun(root, new Vector3(12.6f, H - 0.3f, 8f), 7.6f, 0.09f, pipeMat, alongX: false);
            var warn = new GameObject("ElecWarnLight").AddComponent<Light>();
            warn.transform.SetParent(root, false);
            warn.transform.position = new Vector3(11.5f, 1.7f, 5.6f);
            warn.type = LightType.Point;
            warn.color = new Color(1f, 0.22f, 0.12f);
            warn.intensity = 0.9f;
            warn.range = 2.4f;

            // 実験室: 作業台×2＋ボンベ
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_PlywoodTable_01a.prefab",
                root, new Vector3(15.5f, 0f, 4f), 90f, 0.9f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_PlywoodTable_01a.prefab",
                root, new Vector3(18f, 0f, 6.5f), 0f, 0.9f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_TallGasCanister_01a.prefab",
                root, new Vector3(19.3f, 0f, 1.0f), 0f, 1.0f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_Lunchbox_01a.prefab",
                root, new Vector3(15.5f, 0.9f, 4f), 60f, 0.18f);

            // 施設隔離システム室: 機械＋青緑アクセント（システム稼働の気配）
            Place("Assets/Abandoned World/Fan 2/Assets/Prefabs/fan2_shutter.prefab",
                root, new Vector3(19.85f, 2.2f, 14f), -90f, 0.8f);
            PipeRun(root, new Vector3(16.5f, H - 0.3f, 17.4f), 6.6f, 0.12f, pipeMat, alongX: true);
            var sys = new GameObject("IsolationSysLight").AddComponent<Light>();
            sys.transform.SetParent(root, false);
            sys.transform.position = new Vector3(18.5f, 1.2f, 12f);
            sys.type = LightType.Point;
            sys.color = new Color(0.2f, 0.8f, 0.9f);
            sys.intensity = 1.1f;
            sys.range = 4f;

            // AI移植室: 青白いアクセント
            var ai = new GameObject("AIRoomLight").AddComponent<Light>();
            ai.transform.SetParent(root, false);
            ai.transform.position = new Vector3(16.5f, 1.8f, 20f);
            ai.type = LightType.Point;
            ai.color = new Color(0.55f, 0.7f, 1f);
            ai.intensity = 1.3f;
            ai.range = 5f;

            // 倉庫: 箱の山＋梯子
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_WoodBox_1.prefab",
                root, new Vector3(4.0f, 0f, 1.0f), 8f, 0.55f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_WoodBox_3.prefab",
                root, new Vector3(4.0f, 0.56f, 1.0f), -14f, 0.45f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_CardboardBox_1.prefab",
                root, new Vector3(5.2f, 0f, 0.9f), 25f, 0.4f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_Coil.prefab",
                root, new Vector3(9.0f, 0f, 1.2f), 0f, 0.5f);
            var ladder = Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_Ladder_01a.prefab",
                root, new Vector3(3.4f, 0f, 6.8f), 180f, 1.9f);
            if (ladder != null) ladder.transform.localRotation = Quaternion.Euler(8f, 180f, 0f);

            // 事務所: 机＋段ボール
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_PlywoodTable_01a.prefab",
                root, new Vector3(-2.5f, 0f, 6f), 90f, 0.9f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_CardboardBox_4.prefab",
                root, new Vector3(-3.4f, 0f, 2.8f), 30f, 0.45f);

            // 襲撃襲来場所: 換気ファン（襲撃者の入口の気配）
            Place("Assets/Abandoned World/Fan 2/Assets/Prefabs/fan2.prefab",
                root, new Vector3(5f, 2.1f, 20.8f), 180f, 1.0f);
        }

        private static void Furnish2F(Transform parent)
        {
            var root = new GameObject("Props2F").transform;
            root.SetParent(parent, false);
            // 私室に箱をひとつずつ（生活痕。詳細な家具は本実装時に）
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_CardboardBox_3.prefab",
                root, new Vector3(8.8f, F2, 9.4f), 20f, 0.35f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_CardboardBox_1.prefab",
                root, new Vector3(1.2f, F2, 15.2f), -30f, 0.4f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_PlywoodTable_01a.prefab",
                root, new Vector3(5f, F2, 18.6f), 0f, 0.9f);   // 上級研究員私室の机
        }

        // ================= ギミック扉 =================

        /// <summary>DoorList定義の全開口にギミック扉を配置。keyed指定は既存のKeyedDoor（要・配電室の鍵）</summary>
        private static void BuildGimmickDoors(Transform parent, Room[] rooms, Door[] doors, float y)
        {
            var root = new GameObject("GimmickDoors").transform;
            root.SetParent(parent, false);
            var mat = GetMat("BM_DoorMetal", new Color(0.42f, 0.47f, 0.52f), 0.42f);

            foreach (var d in doors)
            {
                string id = DoorId(rooms, d);
                Vector3 size = d.alongX
                    ? new Vector3(d.w - 0.06f, DoorH - 0.05f, 0.1f)
                    : new Vector3(0.1f, DoorH - 0.05f, d.w - 0.06f);
                var panel = Box(root, "GDoor_" + id,
                    new Vector3(d.x, y + (DoorH - 0.05f) * 0.5f, d.z), size, mat);

                if (d.keyed)
                {
                    var kd = panel.AddComponent<KeyedDoor>();
                    var so = new SerializedObject(kd);
                    so.FindProperty("_door").objectReferenceValue = panel.transform;
                    so.FindProperty("_doorCollider").objectReferenceValue = panel.GetComponent<Collider>();
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    var gd = panel.AddComponent<GimmickDoor>();
                    gd.Id = id;
                    gd.Door = panel.transform;
                    gd.DoorCollider = panel.GetComponent<Collider>();
                }
            }
        }

        /// <summary>開口の両側の部屋名から扉IDを作る（例: Main_Arrival）</summary>
        private static string DoorId(Room[] rooms, Door d)
        {
            string a = null, b = null;
            foreach (var r in rooms)
            {
                bool touches = d.alongX
                    ? (Mathf.Abs(r.z1 - d.z) < 0.11f || Mathf.Abs(r.z0 - d.z) < 0.11f) && d.x > r.x0 && d.x < r.x1
                    : (Mathf.Abs(r.x1 - d.x) < 0.11f || Mathf.Abs(r.x0 - d.x) < 0.11f) && d.z > r.z0 && d.z < r.z1;
                if (!touches) continue;
                if (a == null) a = r.name;
                else { b = r.name; break; }
            }
            return b == null ? (a ?? "door") : $"{a}_{b}";
        }

        // ================= 既存ギミックの配置 =================

        /// <summary>プロトタイプで実装済みの謎解きギミック一式をこの間取りに配置する。
        /// 流れ: 社員証(メイン)→名簿/PC(事務所)→アルバム/人事(実験室)→説明書(倉庫)
        ///       →鍵キャビネット(2F私室)→配電室KeyedDoor→配電盤(電気室)→脱出(Exit)。
        /// 金庫(ストーリー用)はAI移植室。</summary>
        private static void BuildGimmicks()
        {
            var root = new GameObject("Gimmicks").transform;
            var paperMat = GetMat("BM_Paper", new Color(0.88f, 0.85f, 0.75f), 0.1f);
            var deskMat = GetMat("BM_Desk", new Color(0.32f, 0.26f, 0.2f), 0.2f);

            // --- 資料（DocumentInteract）---
            Document(root, "Doc_Card", new Vector3(6.5f, 0.95f, 12f), DocumentType.EmployeeCard, null, paperMat);
            Document(root, "Doc_Roster", new Vector3(-2.3f, 0.95f, 6f), DocumentType.DepartmentRoster, null, paperMat);
            Document(root, "Doc_Album", new Vector3(15.5f, 0.95f, 4f), DocumentType.Album, null, paperMat);
            Document(root, "Doc_HR", new Vector3(18f, 0.95f, 6.5f), DocumentType.PersonnelFile, null, paperMat);
            // 説明書3種（倉庫の棚）
            Box(root, "ManualShelf", new Vector3(6f, 0.85f, 7.55f), new Vector3(2.6f, 0.08f, 0.5f), deskMat);
            string[] models = { "DXR-100", "DXR-200", "DXR-330" };
            for (int i = 0; i < models.Length; i++)
                Document(root, "Manual_" + models[i], new Vector3(5.1f + i * 0.9f, 0.95f, 7.55f),
                    DocumentType.Manual, models[i], paperMat);

            // --- 社内PC（事務所）---
            var pcRoot = new GameObject("PcDesk");
            pcRoot.transform.SetParent(root, false);
            pcRoot.transform.localPosition = new Vector3(-3.3f, 0f, 8.6f);
            Box(pcRoot.transform, "Desk", new Vector3(0f, 0.4f, 0f), new Vector3(1.2f, 0.8f, 0.6f), deskMat);
            Box(pcRoot.transform, "Monitor", new Vector3(0f, 1.05f, 0.05f), new Vector3(0.55f, 0.38f, 0.07f),
                GetMat("BM_Monitor", new Color(0.08f, 0.1f, 0.12f), 0.6f));
            pcRoot.AddComponent<PcDesk>();

            // --- 鍵キャビネット（2F私室×4。鍵保管者の部屋だけ正解）---
            var cabMat = GetMat("BM_Cabinet", new Color(0.45f, 0.38f, 0.3f), 0.25f);
            (string num, Vector3 pos)[] cabs =
            {
                ("1021", new Vector3(2.2f, F2, 12.4f)),
                ("2034", new Vector3(2.2f, F2, 15.2f)),
                ("2058", new Vector3(7.8f, F2, 12.4f)),
                ("3011", new Vector3(7.8f, F2, 15.2f)),
            };
            foreach (var (num, pos) in cabs)
            {
                var cab = Box(root, $"KeyCabinet_{num}", pos + new Vector3(0f, 0.5f, 0f),
                    new Vector3(0.9f, 1.0f, 0.45f), cabMat);
                cab.AddComponent<KeyCabinet>().OwnerNumber = num;
            }

            // --- 配電盤（電気室・東壁）---
            var board = Box(root, "DistributionBoard", new Vector3(12.55f, 0.85f, 10.5f),
                new Vector3(0.35f, 1.5f, 1.1f), GetMat("BM_Board", new Color(0.3f, 0.34f, 0.36f), 0.45f));
            board.AddComponent<DistributionBoard>();

            // --- 壁金庫（AI移植室・北壁。ストーリー用）---
            var safeRoot = Box(root, "WallSafe", new Vector3(16.5f, 1.35f, 21.7f),
                new Vector3(0.9f, 0.9f, 0.3f), GetMat("BM_Safe", new Color(0.25f, 0.27f, 0.3f), 0.5f));
            var indicator = Box(safeRoot.transform, "Indicator", new Vector3(0.25f, 0.25f, -0.17f),
                new Vector3(0.1f, 0.1f, 0.04f), GetMat("BM_SafeLamp", new Color(0.8f, 0.2f, 0.2f), 0.4f));
            var safe = safeRoot.AddComponent<WallSafe>();
            var safeSo = new SerializedObject(safe);
            safeSo.FindProperty("_indicator").objectReferenceValue = indicator.GetComponent<Renderer>();
            safeSo.ApplyModifiedPropertiesWithoutUndo();

            // --- 脱出ドア（Exitのドアユニットに機能を付与）---
            var exitUnit = GameObject.Find("Exit/DoorUnit");
            if (exitUnit != null) exitUnit.AddComponent<ExitDoor>();
        }

        private static void Document(Transform parent, string name, Vector3 pos,
                                     DocumentType type, string manualModel, Material mat)
        {
            var doc = Box(parent, name, pos, new Vector3(0.32f, 0.05f, 0.24f), mat);
            var di = doc.AddComponent<DocumentInteract>();
            di.Type = type;
            if (!string.IsNullOrEmpty(manualModel)) di.ManualModel = manualModel;
        }

        // ================= マネージャー =================

        private static void BuildManagers(GameObject player)
        {
            var root = new GameObject("Managers");

            var respawn = new GameObject("RespawnPoint");
            respawn.transform.SetParent(root.transform, false);
            respawn.transform.position = new Vector3(5f, 0.1f, 13f);

            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(root.transform, false);
            var gm = gmGo.AddComponent<GameManager>();
            var so = new SerializedObject(gm);
            so.FindProperty("_player").objectReferenceValue = player.transform;
            so.FindProperty("_respawnPoint").objectReferenceValue = respawn.transform;
            so.FindProperty("_dolls").intValue = 5;
            so.ApplyModifiedPropertiesWithoutUndo();

            NewChild(root, "PuzzleState").AddComponent<PuzzleState>();
            NewChild(root, "HUD").AddComponent<HUDManager>();
            NewChild(root, "MenuManager").AddComponent<MenuManager>();
            NewChild(root, "PuzzleUI").AddComponent<PuzzleUI>();
            NewChild(root, "SafeDialUI").AddComponent<SafeDialUI>();
            // デバッグ: タイトルをスキップして即プレイ（MCP検証用）
        }

        private static GameObject NewChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        // ================= ライティング・ポスプロ・カメラ =================

        private static void BuildGlobalLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.030f, 0.034f, 0.036f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.045f, 0.055f, 0.055f);
            RenderSettings.fogDensity = 0.05f;
        }

        /// <summary>吊り電球（BasementMoodと同テイスト）。big=falseなら影なしの軽量ライト</summary>
        private static void Pendant(Transform parent, Vector3 floorPos, float intensity, bool big)
        {
            var lamp = Place(big
                    ? "Assets/New Solution Studio/PBR Lamps Pack/Prefabs/Large_round_lamp.prefab"
                    : "Assets/New Solution Studio/PBR Lamps Pack/Prefabs/Small_roof_lamp.prefab",
                parent, new Vector3(floorPos.x, floorPos.y + H - 0.55f, floorPos.z), 0f, big ? 0.5f : 0.3f);

            var bulbMat = GetMat("BM_Bulb", Color.white, 0.1f);
            bulbMat.EnableKeyword("_EMISSION");
            bulbMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            bulbMat.SetColor("_EmissionColor", new Color(1f, 0.92f, 0.75f) * 4f);
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb";
            bulb.transform.SetParent(lamp != null ? lamp.transform : parent, true);
            bulb.transform.position = new Vector3(floorPos.x, floorPos.y + H - 0.72f, floorPos.z);
            bulb.transform.localScale = Vector3.one * 0.12f;
            bulb.GetComponent<Renderer>().sharedMaterial = bulbMat;
            Object.DestroyImmediate(bulb.GetComponent<Collider>());

            var go = new GameObject("PendantPoint");
            go.transform.SetParent(lamp != null ? lamp.transform : parent, true);
            go.transform.position = new Vector3(floorPos.x, floorPos.y + H - 0.78f, floorPos.z);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.93f, 0.80f);
            l.intensity = intensity;
            l.range = big ? 10f : 7f;
            l.shadows = big ? LightShadows.Soft : LightShadows.None;   // 小部屋は影なしで軽量化
        }

        private static void BuildPostProcess()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfilePath);

            var color = profile.Add<ColorAdjustments>();
            color.postExposure.Override(0.4f);
            color.saturation.Override(-22f);
            color.colorFilter.Override(new Color(0.90f, 0.94f, 0.93f));   // 緑被りを抑える

            var lift = profile.Add<LiftGammaGain>();
            lift.lift.Override(new Vector4(0.96f, 1.0f, 1.0f, -0.02f));
            lift.gamma.Override(new Vector4(0.98f, 1.0f, 0.99f, -0.05f));

            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.42f);
            vignette.smoothness.Override(0.45f);

            var grain = profile.Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.30f);

            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.5f);

            AssetDatabase.SaveAssets();

            var volGo = new GameObject("PostVolume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        /// <summary>FPSプレイヤー（PrototypeSceneBuilderと同じ操作系＋ギミック操作＋MCPデバッグ歩行）</summary>
        private static GameObject BuildPlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(5f, 0.1f, 13f);   // メイン中央（階段より北）からスタート
            player.tag = "Player";

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.93f, 0f);
            cc.stepOffset = 0.35f;   // 階段1段0.2mを確実に登れる段差許容

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
            camRoot.AddComponent<Flashlight>();   // Fキー

            var interaction = player.AddComponent<InteractionController>();
            var iso = new SerializedObject(interaction);
            iso.FindProperty("_camera").objectReferenceValue = cam;
            iso.FindProperty("_interactDistance").floatValue = 3.5f;
            iso.FindProperty("_interactLayer").intValue = ~0;
            iso.ApplyModifiedPropertiesWithoutUndo();

            player.AddComponent<DebugPlayerDriver>();   // MCPからの歩行検証用

            return player;
        }

        // ================= 共通ヘルパー（BasementMoodと同テイスト） =================

        /// <summary>1UV=2mの均一密度でUVを張った箱。スケール変形Cubeと違い、
        /// どの寸法の壁・床でもテクスチャ／ノーマルの密度が揃う（UV歪み対策）</summary>
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

        private const float UvPerMeter = 0.5f;   // 2mで1リピート
        private static readonly Dictionary<Vector3, Mesh> MeshCache = new Dictionary<Vector3, Mesh>();

        /// <summary>面ごとにワールド寸法ベースのUVを持つ箱メッシュ（同寸法はキャッシュ共有）</summary>
        private static Mesh BoxMeshWorldUV(Vector3 s)
        {
            if (MeshCache.TryGetValue(s, out var cached) && cached != null) return cached;

            Vector3 h = s * 0.5f;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // 面: 原点中心、right/up 方向の張り出しで四隅を作る
            void Face(Vector3 center, Vector3 right, Vector3 up, Vector3 n, float uSize, float vSize)
            {
                int i0 = verts.Count;
                verts.Add(center - right - up);
                verts.Add(center - right + up);
                verts.Add(center + right + up);
                verts.Add(center + right - up);
                for (int i = 0; i < 4; i++) norms.Add(n);
                float u = uSize * UvPerMeter, v = vSize * UvPerMeter;
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, v));
                uvs.Add(new Vector2(u, v)); uvs.Add(new Vector2(u, 0));
                tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }

            Face(new Vector3(0, 0, -h.z), new Vector3(h.x, 0, 0), new Vector3(0, h.y, 0), Vector3.back, s.x, s.y);     // -Z
            Face(new Vector3(0, 0, h.z), new Vector3(-h.x, 0, 0), new Vector3(0, h.y, 0), Vector3.forward, s.x, s.y);  // +Z
            Face(new Vector3(-h.x, 0, 0), new Vector3(0, 0, -h.z), new Vector3(0, h.y, 0), Vector3.left, s.z, s.y);    // -X
            Face(new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), new Vector3(0, h.y, 0), Vector3.right, s.z, s.y);     // +X
            Face(new Vector3(0, h.y, 0), new Vector3(h.x, 0, 0), new Vector3(0, 0, h.z), Vector3.up, s.x, s.z);        // +Y
            Face(new Vector3(0, -h.y, 0), new Vector3(h.x, 0, 0), new Vector3(0, 0, -h.z), Vector3.down, s.x, s.z);    // -Y

            var mesh = new Mesh { name = $"BoxUV_{s.x:0.##}x{s.y:0.##}x{s.z:0.##}" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            MeshCache[s] = mesh;
            return mesh;
        }

        private static void PipeRun(Transform parent, Vector3 center, float length, float radius,
                                    Material mat, bool alongX)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "PipeRun";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            go.transform.localRotation = alongX ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static Material ConcreteMat(string name, Color tint)
        {
            var mat = GetMat(name, tint, 0.12f);
            var path = "Assets/Construction_Package/Construction_Vol01/Textures/TX_Drywall_01a_NRM.tga";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && imp.textureType != TextureImporterType.NormalMap)
            {
                imp.textureType = TextureImporterType.NormalMap;
                imp.SaveAndReimport();
            }
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (nrm != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", nrm);
                mat.SetFloat("_BumpScale", 0.6f);
                mat.EnableKeyword("_NORMALMAP");
            }
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

        private static void DoorUnit(Transform parent, Vector3 pos, float yRot)
        {
            var doorMat = GetMat("BM_DoorMetal", new Color(0.42f, 0.47f, 0.52f), 0.42f);
            var frameMat = GetMat("BM_DoorFrame", new Color(0.30f, 0.33f, 0.36f), 0.35f);
            var unit = new GameObject("DoorUnit");
            unit.transform.SetParent(parent, false);
            unit.transform.localPosition = pos;
            unit.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            var t = unit.transform;
            Box(t, "Door", new Vector3(0f, 1.08f, 0f), new Vector3(0.96f, 2.16f, 0.07f), doorMat);
            Box(t, "Jamb_L", new Vector3(-0.545f, 1.13f, 0f), new Vector3(0.09f, 2.26f, 0.12f), frameMat);
            Box(t, "Jamb_R", new Vector3(0.545f, 1.13f, 0f), new Vector3(0.09f, 2.26f, 0.12f), frameMat);
            Box(t, "Lintel", new Vector3(0f, 2.3f, 0f), new Vector3(1.18f, 0.1f, 0.12f), frameMat);
        }

        private static GameObject Place(string path, Transform parent, Vector3 pos,
                                        float yRot = 0f, float targetHeight = -1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[FacilityMap] プレハブ未検出（スキップ）: {path}");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            if (targetHeight > 0f)
            {
                var b = RendererBounds(go);
                if (b.size.y > 0.0001f)
                {
                    go.transform.localScale *= targetHeight / b.size.y;
                    b = RendererBounds(go);
                    go.transform.position += new Vector3(0f, pos.y - b.min.y, 0f);
                }
            }
            return go;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
