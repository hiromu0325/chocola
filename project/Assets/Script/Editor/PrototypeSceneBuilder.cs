#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarterAssets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// ワンクリックでプレイ可能なプロトタイプシーンを構築する。
    /// メニュー: Tools > EscapePrototype > Build Prototype Scene
    ///
    /// 構築内容（新レイアウト：コンセプトアート準拠）：
    /// ・地下室ホール（中央の大机：モニター / 手帳 / 陶器人形5体 / 香水）
    /// ・西（左）：襲撃者の部屋 — 壁金庫あり。ドアは襲撃中のみ開く。常に襲撃者が居る
    /// ・北（奥）：用具室 — 配電盤＋説明書＋掃除用具（施錠：配電室の鍵）
    /// ・東（右）：壁沿いの階段 → 2階 社員個室（鍵の保管場所）
    /// ・南（後ろ）：社員の事務所 — 社内PC / 人事ファイル / アルバム
    /// ・古時計、電気スイッチ、懐中電灯、脱出ドア、ロッカー、ベッド
    /// ・別部屋（東奥）：金髪人形(DollEvent)＋硝子の目×2
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        private const string RootDir = "Assets/EscapePrototype";
        private const string MatDir = RootDir + "/Materials";

        private const float RoomW = 16f;   // X
        private const float RoomD = 12f;   // Z
        private const float WallH = 3.2f;

        // 別部屋（イベント部屋）：東側にドアでつながる
        private const float EvW = 7f;
        private const float EvD = 6f;
        private static readonly Vector3 EvCenter = new Vector3(RoomW * 0.5f + EvW * 0.5f, 0f, 0f);

        // 襲撃者の部屋（西）：金庫あり。ドアは襲撃中のみ開く
        private const float SrW = 6f;
        private const float SrD = 6f;
        private static readonly Vector3 SrCenter = new Vector3(-(RoomW * 0.5f + SrW * 0.5f), 0f, 0f);

        // 用具室（北奥）：配電盤＋説明書
        private const float UtW = 6f;
        private const float UtD = 4f;
        private static readonly Vector3 UtCenter = new Vector3(0f, 0f, RoomD * 0.5f + UtD * 0.5f);

        // 社員の事務所（南裏）：PC・資料
        private const float OfW = 8f;
        private const float OfD = 4f;
        private static readonly Vector3 OfCenter = new Vector3(3f, 0f, -(RoomD * 0.5f + OfD * 0.5f));

        // 主要な出入口（南壁）：脱出ドアと事務所ドアの中心X
        private const float ExitGapX = -4.5f;
        private const float OfficeGapX = 2f;

        /// <summary>
        /// バランス調整用の ScriptableObject を Resources/ に生成（既存ならロード）。
        /// メニュー: Tools > EscapePrototype > Create Game Balance Config
        /// </summary>
        [MenuItem("Tools/EscapePrototype/Create Game Balance Config")]
        public static GameBalanceConfig CreateOrLoadConfig()
        {
            const string dir = "Assets/Resources";
            const string path = dir + "/GameBalanceConfig.asset";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var cfg = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(path);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<GameBalanceConfig>();
                AssetDatabase.CreateAsset(cfg, path);
                AssetDatabase.SaveAssets();
                Debug.Log("[PrototypeSceneBuilder] GameBalanceConfig を作成しました: " + path +
                          "\nInspector で来訪時間・移動速度などを調整できます。");
            }
            Selection.activeObject = cfg;
            return cfg;
        }

        [MenuItem("Tools/EscapePrototype/Build Prototype Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureFolders();
            CreateOrLoadConfig();   // バランス設定アセットを用意（全システムが参照）
            BuildLighting();
            BuildRoom();
            BuildEventRoom();
            var player = BuildPlayer(new Vector3(-5f, 0.1f, -3.5f));
            BuildManagers(player);
            BuildCentralDesk();
            BuildSearcherRoom();
            BuildUtilityRoom();
            BuildOffice();
            BuildPuzzleDocs();
            BuildSecondFloor();
            BuildWallSafe();
            BuildHidingSpots();
            BuildLightSwitch();
            BuildDisplays();
            BuildEnemyInfrastructure();
            BuildDollEvent();

            // 各部屋を自己完結した1つのルートにまとめ、ピボットを部屋中心に置き直して
            // 個別Prefab化する（後からHierarchyでドラッグしてレイアウトし直せる）。
            OrganizeAndPrefab();

            string scenePath = RootDir + "/PrototypeScene.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();
            Debug.Log("[PrototypeSceneBuilder] 構築完了: " + scenePath +
                      "\n再生してプレイ。WASD移動 / Shift走 / E長押し解除 / Eインタラクト / F懐中電灯 / Tab手帳 / 数字キー選択肢");
        }

        // ============================== 環境 ==============================

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(RootDir))
                AssetDatabase.CreateFolder("Assets", "EscapePrototype");
            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder(RootDir, "Materials");
        }

        private static Material GetMat(string name, Color color, float smoothness = 0.2f)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void BuildLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            //RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.07f);
            RenderSettings.ambientLight = new Color(1.0f, 1.0f, 1.0f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = Color.black;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.03f;

            // これらPointライトが「部屋の電気」。電気スイッチが自動収集する
            CreatePointLight("Light_Center", new Vector3(0f, 2.8f, 0f), new Color(1f, 0.9f, 0.7f), 1.1f, 12f);
            CreatePointLight("Light_West", new Vector3(-5f, 2.5f, 3f), new Color(0.8f, 0.85f, 1f), 0.7f, 9f);
            CreatePointLight("Light_Event", EvCenter + new Vector3(0f, 2.6f, 0f), new Color(0.9f, 0.85f, 0.8f), 0.7f, 8f);
            // 襲撃者の部屋の赤（雰囲気・敵の在室を示す。Pointなので電気で消える）
            CreatePointLight("Light_Searcher", SrCenter + new Vector3(0f, 2.6f, 0f), new Color(1f, 0.3f, 0.2f), 0.8f, 8f);
            // 用具室・事務所
            CreatePointLight("Light_Utility", UtCenter + new Vector3(0f, 2.6f, 0f), new Color(0.7f, 0.8f, 1f), 0.6f, 7f);
            CreatePointLight("Light_Office", OfCenter + new Vector3(0f, 2.6f, 0f), new Color(1f, 0.9f, 0.75f), 0.7f, 8f);
        }

        private static void CreatePointLight(string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = color; l.intensity = intensity;
            l.range = range; l.shadows = LightShadows.Soft;
        }

        private static void BuildRoom()
        {
            var root = new GameObject("Room");
            var floorMat = GetMat("Floor", new Color(0.16f, 0.15f, 0.14f));
            var wallMat = GetMat("Wall", new Color(0.28f, 0.26f, 0.24f));

            Box(root.transform, "Floor", new Vector3(0, -0.05f, 0), new Vector3(RoomW, 0.1f, RoomD), floorMat);
            // ※天井は撤去（2階メザニンの吹き抜けスペースを確保）

            float hw = RoomW * 0.5f, hd = RoomD * 0.5f;
            const float gap = 1.6f;

            // 南壁（西から：脱出ドアの隙間 / 事務所ドアの隙間）
            SouthWallWithGaps(root.transform, wallMat, -hd, gap);

            // 北壁（用具室への出入口 幅1.6m を X=0 に）
            float nseg = (RoomW - gap) * 0.5f;
            Box(root.transform, "Wall_N_L", new Vector3(-(gap * 0.5f + nseg * 0.5f), WallH * 0.5f, hd), new Vector3(nseg, WallH, 0.3f), wallMat);
            Box(root.transform, "Wall_N_R", new Vector3(gap * 0.5f + nseg * 0.5f, WallH * 0.5f, hd), new Vector3(nseg, WallH, 0.3f), wallMat);
            Box(root.transform, "Wall_N_Top", new Vector3(0f, WallH - 0.35f, hd), new Vector3(gap, 0.7f, 0.3f), wallMat);

            // 西壁（襲撃者の部屋への出入口 幅1.6m を Z=0 に）
            float wseg = (RoomD - gap) * 0.5f;
            Box(root.transform, "Wall_W_N", new Vector3(-hw, WallH * 0.5f, gap * 0.5f + wseg * 0.5f), new Vector3(0.3f, WallH, wseg), wallMat);
            Box(root.transform, "Wall_W_S", new Vector3(-hw, WallH * 0.5f, -(gap * 0.5f + wseg * 0.5f)), new Vector3(0.3f, WallH, wseg), wallMat);
            Box(root.transform, "Wall_W_Top", new Vector3(-hw, WallH - 0.35f, 0f), new Vector3(0.3f, 0.7f, gap), wallMat);

            // 東壁（イベント部屋への扉の隙間 幅1.6m を Z=0 に）
            float eseg = (RoomD - gap) * 0.5f;
            Box(root.transform, "Wall_E_N", new Vector3(hw, WallH * 0.5f, gap * 0.5f + eseg * 0.5f), new Vector3(0.3f, WallH, eseg), wallMat);
            Box(root.transform, "Wall_E_S", new Vector3(hw, WallH * 0.5f, -(gap * 0.5f + eseg * 0.5f)), new Vector3(0.3f, WallH, eseg), wallMat);
            Box(root.transform, "Wall_E_Top", new Vector3(hw, WallH - 0.35f, 0f), new Vector3(0.3f, 0.7f, gap), wallMat);

            // 視線切り用の柱
            Box(root.transform, "Pillar", new Vector3(3.5f, WallH * 0.5f, 1f), new Vector3(0.9f, WallH, 0.9f), wallMat);

            // 脱出ドア（南壁 西寄り）
            var doorMat = GetMat("ExitDoor", new Color(0.35f, 0.25f, 0.2f));
            var door = Box(root.transform, "ExitDoor", new Vector3(ExitGapX, 1.25f, -hd), new Vector3(gap - 0.1f, 2.5f, 0.25f), doorMat);
            door.AddComponent<ExitDoor>();
        }

        /// <summary>南壁：脱出ドア(ExitGapX)と事務所ドア(OfficeGapX)の2つの隙間つき</summary>
        private static void SouthWallWithGaps(Transform parent, Material wallMat, float z, float gap)
        {
            float hw = RoomW * 0.5f;
            float g1L = ExitGapX - gap * 0.5f, g1R = ExitGapX + gap * 0.5f;
            float g2L = OfficeGapX - gap * 0.5f, g2R = OfficeGapX + gap * 0.5f;

            // 3セグメント：[-hw, g1L] [g1R, g2L] [g2R, hw]
            Seg(parent, wallMat, "Wall_S_A", -hw, g1L, z);
            Seg(parent, wallMat, "Wall_S_B", g1R, g2L, z);
            Seg(parent, wallMat, "Wall_S_C", g2R, hw, z);
            Box(parent, "Wall_S_Top1", new Vector3(ExitGapX, WallH - 0.35f, z), new Vector3(gap, 0.7f, 0.3f), wallMat);
            Box(parent, "Wall_S_Top2", new Vector3(OfficeGapX, WallH - 0.35f, z), new Vector3(gap, 0.7f, 0.3f), wallMat);
        }

        private static void Seg(Transform parent, Material mat, string name, float x0, float x1, float z)
        {
            float len = x1 - x0;
            if (len <= 0.05f) return;
            Box(parent, name, new Vector3((x0 + x1) * 0.5f, WallH * 0.5f, z), new Vector3(len, WallH, 0.3f), mat);
        }

        private static void BuildEventRoom()
        {
            var root = new GameObject("EventRoom");
            var floorMat = GetMat("Floor", new Color(0.16f, 0.15f, 0.14f));
            var wallMat = GetMat("EventWall", new Color(0.22f, 0.2f, 0.24f));

            float cx = EvCenter.x;
            float hw = EvW * 0.5f, hd = EvD * 0.5f;

            Box(root.transform, "EvFloor", new Vector3(cx, -0.05f, 0f), new Vector3(EvW, 0.1f, EvD), floorMat);
            Box(root.transform, "EvCeiling", new Vector3(cx, WallH + 0.05f, 0f), new Vector3(EvW, 0.1f, EvD), wallMat);
            Box(root.transform, "EvWall_E", new Vector3(cx + hw, WallH * 0.5f, 0f), new Vector3(0.3f, WallH, EvD), wallMat);
            Box(root.transform, "EvWall_N", new Vector3(cx, WallH * 0.5f, hd), new Vector3(EvW, WallH, 0.3f), wallMat);
            Box(root.transform, "EvWall_S", new Vector3(cx, WallH * 0.5f, -hd), new Vector3(EvW, WallH, 0.3f), wallMat);
        }

        // ============================== プレイヤー ==============================

        private static GameObject BuildPlayer(Vector3 pos)
        {
            var player = new GameObject("Player");
            player.transform.position = pos;
            player.tag = "Player";

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.93f, 0f);

            // 入力は StarterAssetsInputs が直接デバイスを読む（PlayerInput不要）
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

            var fpc = player.AddComponent<FirstPersonController>();
            fpc.CinemachineCameraTarget = camRoot;
            fpc.MoveSpeed = 2.4f; fpc.SprintSpeed = 4.6f; fpc.RotationSpeed = 1.0f;
            fpc.SpeedChangeRate = 30f;   // 出だしを機敏に（押した瞬間に歩き出す）

            player.AddComponent<PlayerStatus>();

            // 懐中電灯（カメラに追従させる）
            camRoot.gameObject.AddComponent<Flashlight>();

            var interaction = player.AddComponent<InteractionController>();
            var so = new SerializedObject(interaction);
            so.FindProperty("_camera").objectReferenceValue = cam;
            so.FindProperty("_interactDistance").floatValue = 3.5f;
            so.FindProperty("_interactLayer").intValue = ~0;
            so.ApplyModifiedPropertiesWithoutUndo();

            return player;
        }

#if ENABLE_INPUT_SYSTEM
        private static InputActionAsset FindStarterAssetsActions()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:InputActionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("StarterAssets"))
                    return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            }
            return null;
        }
#endif

        // ============================== マネージャー ==============================

        private static void BuildManagers(GameObject player)
        {
            var root = new GameObject("Managers");

            // 12時（来訪直後）の再開地点＝南の机付近（安全地帯）
            var respawn = new GameObject("RespawnPoint");
            respawn.transform.SetParent(root.transform, false);
            respawn.transform.position = new Vector3(-5f, 0.1f, -3.5f);

            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(root.transform, false);
            var gm = gmGo.AddComponent<GameManager>();
            var so = new SerializedObject(gm);
            so.FindProperty("_player").objectReferenceValue = player.transform;
            so.FindProperty("_respawnPoint").objectReferenceValue = respawn.transform;
            so.FindProperty("_dolls").intValue = 5;
            so.ApplyModifiedPropertiesWithoutUndo();

            var pmGo = new GameObject("PhaseManager");
            pmGo.transform.SetParent(root.transform, false);
            pmGo.AddComponent<PhaseManager>();

            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(root.transform, false);
            hudGo.AddComponent<HUDManager>();

            // タイトル / ポーズ / オプションのメニュー
            var menuGo = new GameObject("MenuManager");
            menuGo.transform.SetParent(root.transform, false);
            menuGo.AddComponent<MenuManager>();

            // 謎解き状態とUI
            var puzzleStateGo = new GameObject("PuzzleState");
            puzzleStateGo.transform.SetParent(root.transform, false);
            puzzleStateGo.AddComponent<PuzzleState>();

            var puzzleUiGo = new GameObject("PuzzleUI");
            puzzleUiGo.transform.SetParent(root.transform, false);
            puzzleUiGo.AddComponent<PuzzleUI>();

            // 壁金庫のダイヤルUI
            var dialGo = new GameObject("SafeDialUI");
            dialGo.transform.SetParent(root.transform, false);
            dialGo.AddComponent<SafeDialUI>();
        }

        // ============================== 中央の大きい机 ==============================

        private static void BuildCentralDesk()
        {
            var root = new GameObject("CentralDesk");
            var woodMat = GetMat("Wood", new Color(0.4f, 0.28f, 0.18f));

            Vector3 deskPos = new Vector3(0f, 0f, -1.5f);
            // 大机：Blender製ローポリ（天板0.85m→机上の小物基準0.9mへY微調整）＋当たり
            var deskModel = PlaceModel(root.transform, "CentralDesk", deskPos);
            if (deskModel != null)
            {
                deskModel.transform.localScale = new Vector3(1f, 0.9f / 0.85f, 1f);
                var col = root.AddComponent<BoxCollider>();
                col.center = deskPos + new Vector3(0f, 0.45f, 0f);
                col.size = new Vector3(3.2f, 0.9f, 1.4f);
            }
            else
            {
                Box(root.transform, "DeskTop", deskPos + new Vector3(0f, 0.85f, 0f), new Vector3(3.2f, 0.1f, 1.4f), woodMat);
                Box(root.transform, "DeskBody", deskPos + new Vector3(0f, 0.42f, 0f), new Vector3(3.0f, 0.85f, 1.2f), woodMat);
            }

            // 陶器人形5体（机上 中央奥）
            var rack = new GameObject("DollRack");
            rack.transform.SetParent(root.transform, false);
            rack.transform.position = deskPos + new Vector3(0f, 0.9f, 0.4f);
            rack.AddComponent<DollRack>();

            // 手帳（机上 左）
            var memoMat = GetMat("Memo", new Color(0.9f, 0.88f, 0.75f));
            var memo = Box(root.transform, "MemoNote", deskPos + new Vector3(-1.1f, 0.92f, -0.2f), new Vector3(0.3f, 0.05f, 0.4f), memoMat);
            memo.AddComponent<MemoNote>();

            // 香水（机上 右）
            var perfMat = GetMat("Perfume", new Color(0.6f, 0.8f, 0.9f), 0.8f);
            var perfume = Cylinder(root.transform, "Perfume", deskPos + new Vector3(1.0f, 0.99f, -0.1f), new Vector3(0.12f, 0.12f, 0.12f), perfMat);
            perfume.AddComponent<PerfumeItem>();

            // モニター（机上 中央。スタンド付き、部屋の南＝プレイヤー側を向く）
            var monitor = new GameObject("Monitor");
            monitor.transform.SetParent(root.transform, false);
            monitor.transform.position = deskPos + new Vector3(0f, 1.4f, 0.45f);
            monitor.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            monitor.AddComponent<MonitorDisplay>();
            Box(root.transform, "MonitorStand", deskPos + new Vector3(0f, 1.0f, 0.45f), new Vector3(0.1f, 0.3f, 0.1f), woodMat);
        }

        // ============================== ギミック ==============================

        // ============================== 謎解き：資料（社員証・名簿・アルバム・人事ファイル・PC）==============================

        private static void BuildPuzzleDocs()
        {
            var root = new GameObject("PuzzleDocs");
            Vector3 desk = new Vector3(0f, 0f, -1.5f);

            // 社員証（中央机の上・左寄り）
            var cardMat = GetMat("Card", new Color(0.85f, 0.85f, 0.9f), 0.6f);
            var card = Box(root.transform, "EmployeeCard", desk + new Vector3(-0.6f, 0.92f, 0.25f), new Vector3(0.3f, 0.04f, 0.2f), cardMat);
            card.AddComponent<DocumentInteract>().Type = DocumentType.EmployeeCard;

            // 社員名簿（番号→部署）（机の上・右寄り）
            var rosterMat = GetMat("Roster", new Color(0.9f, 0.88f, 0.78f));
            var roster = Box(root.transform, "DepartmentRoster", desk + new Vector3(0.55f, 0.92f, 0.25f), new Vector3(0.35f, 0.05f, 0.28f), rosterMat);
            roster.AddComponent<DocumentInteract>().Type = DocumentType.DepartmentRoster;

            // アルバム（事務所・西寄りの小机の上）：小机はBlender製ローポリ
            var albumTable = new GameObject("AlbumTable");
            albumTable.transform.SetParent(root.transform, false);
            albumTable.transform.position = new Vector3(0.3f, 0f, -9.4f);
            if (PlaceModel(albumTable.transform, "SideTable", Vector3.zero) != null)
            {
                AddBoxCollider(albumTable, new Vector3(0f, 0.45f, 0f), new Vector3(0.8f, 0.9f, 0.6f));
            }
            else
            {
                var tableMat = GetMat("SideTable", new Color(0.4f, 0.28f, 0.18f));
                Box(albumTable.transform, "Top", new Vector3(0f, 0.45f, 0f), new Vector3(0.8f, 0.9f, 0.6f), tableMat);
            }
            var albumMat = GetMat("Album", new Color(0.5f, 0.3f, 0.55f));
            var album = Box(root.transform, "Album", new Vector3(0.3f, 0.95f, -9.4f), new Vector3(0.45f, 0.08f, 0.34f), albumMat);
            album.AddComponent<DocumentInteract>().Type = DocumentType.Album;

            // 人事ファイル（氏名→生年月日）：事務所・中央の書類棚（Blender製ローポリ）の上
            var fileCab = new GameObject("FileCabinet");
            fileCab.transform.SetParent(root.transform, false);
            fileCab.transform.position = new Vector3(2.9f, 0f, -9.5f);
            if (PlaceModel(fileCab.transform, "FileCabinet", Vector3.zero) != null)
            {
                AddBoxCollider(fileCab, new Vector3(0f, 0.6f, 0f), new Vector3(0.5f, 1.2f, 0.6f));
            }
            else
            {
                var cabMat = GetMat("FileCabinet", new Color(0.3f, 0.32f, 0.35f), 0.4f);
                Box(fileCab.transform, "Body", new Vector3(0f, 0.6f, 0f), new Vector3(0.8f, 1.2f, 0.5f), cabMat);
            }
            var fileMat = GetMat("PersonnelFile", new Color(0.8f, 0.75f, 0.6f));
            var file = Box(root.transform, "PersonnelFile", new Vector3(2.9f, 1.25f, -9.5f), new Vector3(0.4f, 0.06f, 0.3f), fileMat);
            file.AddComponent<DocumentInteract>().Type = DocumentType.PersonnelFile;

            // 社内PCデスク（事務所・東寄り）：ID=社員番号 / PW=生年月日 でログイン
            // モニターは机の手前・上面に置き、部屋側（+Z）から正面で触れるようにする
            var pcDeskMat = GetMat("PcDesk", new Color(0.35f, 0.3f, 0.26f));
            Box(root.transform, "PcDeskBody", new Vector3(5.5f, 0.4f, -8.9f), new Vector3(1.4f, 0.8f, 0.7f), pcDeskMat);
            Box(root.transform, "PcDeskTop", new Vector3(5.5f, 0.82f, -8.9f), new Vector3(1.5f, 0.08f, 0.8f), pcDeskMat);
            var pcMat = GetMat("PcMonitor", new Color(0.1f, 0.12f, 0.16f), 0.5f);
            Box(root.transform, "PcStand", new Vector3(5.5f, 0.98f, -8.95f), new Vector3(0.1f, 0.25f, 0.1f), pcMat);
            // 画面は机の前縁（部屋側）に大きめに。プレイヤーは北側(+Z)から正対して触れる
            var pc = Box(root.transform, "SocialPC", new Vector3(5.5f, 1.3f, -8.55f), new Vector3(0.8f, 0.55f, 0.1f), pcMat);
            pc.AddComponent<PcDesk>();
        }

        // ============================== 襲撃者の部屋（西・金庫あり・襲撃中のみ開く）==============================

        private static void BuildSearcherRoom()
        {
            var root = new GameObject("SearcherRoom");
            var floorMat = GetMat("Floor", new Color(0.16f, 0.15f, 0.14f));
            var wallMat = GetMat("SearcherWall", new Color(0.24f, 0.18f, 0.18f));

            float cx = SrCenter.x;                 // -11
            float hw = SrW * 0.5f, hd = SrD * 0.5f;

            Box(root.transform, "SrFloor", new Vector3(cx, -0.05f, 0f), new Vector3(SrW, 0.1f, SrD), floorMat);
            Box(root.transform, "SrCeiling", new Vector3(cx, WallH + 0.05f, 0f), new Vector3(SrW, 0.1f, SrD), wallMat);
            Box(root.transform, "SrWall_W", new Vector3(cx - hw, WallH * 0.5f, 0f), new Vector3(0.3f, WallH, SrD), wallMat);
            Box(root.transform, "SrWall_N", new Vector3(cx, WallH * 0.5f, hd), new Vector3(SrW, WallH, 0.3f), wallMat);
            Box(root.transform, "SrWall_S", new Vector3(cx, WallH * 0.5f, -hd), new Vector3(SrW, WallH, 0.3f), wallMat);

            // ドア（襲撃中のみ床下へスライドして開く）
            var doorRoot = new GameObject("SearcherRoomDoor");
            doorRoot.transform.position = new Vector3(-RoomW * 0.5f, 0f, 0f);
            var srd = doorRoot.AddComponent<SearcherRoomDoor>();
            var doorMat = GetMat("SearcherDoor", new Color(0.35f, 0.15f, 0.15f), 0.4f);
            var door = Box(doorRoot.transform, "Door", new Vector3(0f, 1.25f, 0f), new Vector3(0.28f, 2.5f, 1.55f), doorMat);
            var srdSo = new SerializedObject(srd);
            srdSo.FindProperty("_door").objectReferenceValue = door.transform;
            srdSo.FindProperty("_doorCollider").objectReferenceValue = door.GetComponent<Collider>();
            srdSo.ApplyModifiedPropertiesWithoutUndo();

            // 常駐の襲撃者（来訪中は本物が外に出るため姿を消す）
            var resident = new GameObject("ResidentSearcher");
            resident.transform.SetParent(root.transform, false);
            resident.transform.position = new Vector3(cx - 1.2f, 0f, -1.2f);
            resident.transform.rotation = Quaternion.Euler(0f, 90f, 0f);   // ドアの方を向く
            resident.AddComponent<ResidentSearcher>();

            // 閉じ込められた時のためのロッカー（北壁際・南向き）
            CreateLocker("Locker_Searcher", new Vector3(cx + 1.6f, 0f, hd - 0.75f), 180f);
        }

        // ============================== 用具室（北奥・配電盤＋説明書。施錠：配電室の鍵）==============================

        private static void BuildUtilityRoom()
        {
            var root = new GameObject("UtilityRoom");
            var floorMat = GetMat("Floor", new Color(0.16f, 0.15f, 0.14f));
            var wallMat = GetMat("PowerWall", new Color(0.2f, 0.22f, 0.26f));

            float cz = UtCenter.z;                 // 8
            float hw = UtW * 0.5f, hd = UtD * 0.5f;
            float zN = cz + hd;                    // 10

            Box(root.transform, "UtFloor", new Vector3(0f, -0.05f, cz), new Vector3(UtW, 0.1f, UtD), floorMat);
            Box(root.transform, "UtCeiling", new Vector3(0f, WallH + 0.05f, cz), new Vector3(UtW, 0.1f, UtD), wallMat);
            Box(root.transform, "UtWall_W", new Vector3(-hw, WallH * 0.5f, cz), new Vector3(0.3f, WallH, UtD), wallMat);
            Box(root.transform, "UtWall_E", new Vector3(hw, WallH * 0.5f, cz), new Vector3(0.3f, WallH, UtD), wallMat);
            Box(root.transform, "UtWall_N", new Vector3(0f, WallH * 0.5f, zN), new Vector3(UtW, WallH, 0.3f), wallMat);

            // 施錠ドア（配電室の鍵で解錠。メインルーム北壁の出入口）
            const float gap = 1.6f;
            var doorRoot = new GameObject("UtilityRoomDoor");
            doorRoot.transform.position = new Vector3(0f, 0f, RoomD * 0.5f);
            var kd = doorRoot.AddComponent<KeyedDoor>();
            var doorMat = GetMat("SecDoor", new Color(0.3f, 0.32f, 0.38f), 0.5f);
            var door = Box(doorRoot.transform, "Door", new Vector3(0f, 1.25f, 0f), new Vector3(gap - 0.05f, 2.5f, 0.2f), doorMat);
            var lockMat = GetMat("SecLock", new Color(0.9f, 0.2f, 0.2f));
            var lockBox = Box(doorRoot.transform, "Lock", new Vector3(gap * 0.5f - 0.2f, 1.3f, -0.13f), new Vector3(0.12f, 0.18f, 0.06f), lockMat);
            var kdSo = new SerializedObject(kd);
            kdSo.FindProperty("_door").objectReferenceValue = door.transform;
            kdSo.FindProperty("_doorCollider").objectReferenceValue = door.GetComponent<Collider>();
            kdSo.FindProperty("_lockIndicator").objectReferenceValue = lockBox.GetComponent<Renderer>();
            kdSo.ApplyModifiedPropertiesWithoutUndo();

            // 配電盤（北壁の東寄り・正面＝南から触れる）
            var boardMat = GetMat("Board", new Color(0.2f, 0.2f, 0.22f), 0.3f);
            var board = Box(root.transform, "DistributionBoard", new Vector3(1.5f, 1.3f, zN - 0.35f), new Vector3(1.1f, 1.3f, 0.35f), boardMat);
            board.AddComponent<DistributionBoard>();

            // 説明書3種（北壁の西寄りの棚：部署ページが指定する型番を選ぶ）
            var shelfMat = GetMat("ManualShelf", new Color(0.35f, 0.28f, 0.2f));
            Box(root.transform, "ManualShelf", new Vector3(-1.6f, 0.85f, zN - 0.45f), new Vector3(2.6f, 0.1f, 0.5f), shelfMat);
            string[] models = { "DXR-100", "DXR-200", "DXR-330" };
            for (int i = 0; i < models.Length; i++)
            {
                var mMat = GetMat("Manual_" + models[i], new Color(0.85f, 0.8f, 0.7f));
                var man = Box(root.transform, "Manual_" + models[i], new Vector3(-2.6f + i * 1.0f, 0.95f, zN - 0.45f), new Vector3(0.4f, 0.06f, 0.3f), mMat);
                var di = man.AddComponent<DocumentInteract>();
                di.Type = DocumentType.Manual;
                di.ManualModel = models[i];
            }

            // 用具（雰囲気の小物）：Blender製ローポリ
            CreateDecor(root.transform, "Bucket", "Bucket", new Vector3(2.4f, 0f, cz - hd + 0.6f), 0f,
                        new Vector3(0f, 0.17f, 0f), new Vector3(0.4f, 0.34f, 0.4f));
            CreateDecor(root.transform, "Broom", "Broom", new Vector3(-2.65f, 0f, cz - hd + 0.5f), 0f,
                        new Vector3(0f, 0.75f, 0f), new Vector3(0.15f, 1.5f, 0.15f));
        }

        // ============================== 社員の事務所（南裏・PCと資料）==============================

        private static void BuildOffice()
        {
            var root = new GameObject("Office");
            var floorMat = GetMat("Floor", new Color(0.16f, 0.15f, 0.14f));
            var wallMat = GetMat("RoomWall", new Color(0.26f, 0.25f, 0.28f));

            float cxO = OfCenter.x, czO = OfCenter.z;   // 3, -8
            float hw = OfW * 0.5f, hd = OfD * 0.5f;

            Box(root.transform, "OfFloor", new Vector3(cxO, -0.05f, czO), new Vector3(OfW, 0.1f, OfD), floorMat);
            Box(root.transform, "OfCeiling", new Vector3(cxO, WallH + 0.05f, czO), new Vector3(OfW, 0.1f, OfD), wallMat);
            Box(root.transform, "OfWall_W", new Vector3(cxO - hw, WallH * 0.5f, czO), new Vector3(0.3f, WallH, OfD), wallMat);
            Box(root.transform, "OfWall_E", new Vector3(cxO + hw, WallH * 0.5f, czO), new Vector3(0.3f, WallH, OfD), wallMat);
            Box(root.transform, "OfWall_S", new Vector3(cxO, WallH * 0.5f, czO - hd), new Vector3(OfW, WallH, 0.3f), wallMat);

            // 事務机（雰囲気の小物）：Blender製ローポリ＋バウンズ相当の当たり
            // 引き出し面が部屋の内側（南＝プレイヤー側）を向くよう180°回転
            CreateOfficeDesk(root.transform, "OfficeDesk_A", new Vector3(0.3f, 0f, -7.3f), 180f);
            CreateOfficeDesk(root.transform, "OfficeDesk_B", new Vector3(5.6f, 0f, -6.9f), 180f);

            // 事務椅子（各机の南側・机に向く）
            CreateDecor(root.transform, "Chair", "OfficeChair_A", new Vector3(0.3f, 0f, -8.3f), 0f,
                        new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.9f, 0.5f));
            CreateDecor(root.transform, "Chair", "OfficeChair_B", new Vector3(5.6f, 0f, -7.9f), 0f,
                        new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.9f, 0.5f));

            // 開架棚（南壁際・開口が部屋側=北向き）
            CreateDecor(root.transform, "Shelf", "OfficeShelf", new Vector3(6.2f, 0f, -9.55f), 0f,
                        new Vector3(0f, 0.9f, 0f), new Vector3(1.0f, 1.8f, 0.4f));

            // 事務机Aの上のモニター（装飾・画面はプレイヤー側=南向き）
            CreateDecor(root.transform, "Monitor", "OfficeMonitor", new Vector3(0.3f, 0.82f, -7.45f), 180f,
                        Vector3.zero, Vector3.zero);
        }

        /// <summary>装飾用モデル配置：モデル＋任意の不可視コライダー（sizeがゼロならコライダー無し）</summary>
        private static void CreateDecor(Transform parent, string modelName, string name,
                                        Vector3 pos, float yRot, Vector3 colCenter, Vector3 colSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
            if (PlaceModel(go.transform, modelName, Vector3.zero) != null && colSize != Vector3.zero)
                AddBoxCollider(go, colCenter, colSize);
        }

        private static void CreateOfficeDesk(Transform parent, string name, Vector3 pos, float yRot)
        {
            var desk = new GameObject(name);
            desk.transform.SetParent(parent, false);
            desk.transform.position = pos;
            desk.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
            var model = PlaceModel(desk.transform, "OfficeDesk", Vector3.zero);
            if (model != null)
            {
                AddBoxCollider(desk, new Vector3(0f, 0.42f, 0f), new Vector3(1.6f, 0.84f, 0.8f));
            }
            else
            {
                var deskMat = GetMat("PcDesk", new Color(0.35f, 0.3f, 0.26f));
                Box(desk.transform, "Top", new Vector3(0f, 0.42f, 0f), new Vector3(1.6f, 0.84f, 0.8f), deskMat);
            }
        }

        // ============================== 壁金庫（襲撃中のみ・ストーリー用）==============================

        private static void BuildWallSafe()
        {
            var root = new GameObject("WallSafe");
            // 襲撃者の部屋の西壁に埋め込む（盤面は部屋側＝東向き）
            root.transform.position = new Vector3(SrCenter.x - SrW * 0.5f + 0.18f, 1.4f, 0f);
            root.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

            var bodyMat = GetMat("SafeBody", new Color(0.14f, 0.14f, 0.16f), 0.35f);
            var trimMat = GetMat("SafeTrim", new Color(0.28f, 0.26f, 0.2f), 0.5f);
            var dialMat = GetMat("SafeDial", new Color(0.55f, 0.45f, 0.15f), 0.7f);

            // 埋め込み筐体（壁面から少しだけ手前に出る）
            Box(root.transform, "SafeFrame", new Vector3(0f, 0f, -0.02f), new Vector3(0.9f, 0.9f, 0.28f), trimMat);
            Box(root.transform, "SafeDoor", new Vector3(0f, 0f, -0.16f), new Vector3(0.74f, 0.74f, 0.06f), bodyMat);

            // ダイヤル（円盤）＝状態インジケータ
            var dial = Cylinder(root.transform, "Dial", new Vector3(0.05f, 0.05f, -0.22f), new Vector3(0.34f, 0.05f, 0.34f), dialMat);
            dial.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 盤面が手前(-Z)を向く

            // ハンドル
            Box(root.transform, "SafeHandle", new Vector3(-0.24f, -0.02f, -0.22f), new Vector3(0.06f, 0.28f, 0.06f), trimMat);

            var safe = root.AddComponent<WallSafe>();
            var so = new SerializedObject(safe);
            so.FindProperty("_indicator").objectReferenceValue = dial.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ============================== 2階：社員個室（鍵の保管場所）==============================

        private const float MezzY = 2.6f;   // 2階の床高さ

        private static void BuildSecondFloor()
        {
            var root = new GameObject("SecondFloor");
            var stepMat = GetMat("Stairs", new Color(0.3f, 0.3f, 0.33f), 0.3f);
            var floorMat = GetMat("MezzFloor", new Color(0.22f, 0.22f, 0.25f));
            var railMat = GetMat("Railing", new Color(0.35f, 0.3f, 0.25f));

            // --- 階段（東壁沿いを北へ登る。1段0.26m×10段=2.6m。コンセプトアートの右手階段）---
            var stairs = new GameObject("Stairs"); stairs.transform.SetParent(root.transform, false);
            const int steps = 10; const float rise = 0.26f, depth = 0.4f, zStart = -2.2f;
            const float stairX = 6f;   // 東壁との間にイベント部屋扉への通路(約1m)を残す
            for (int i = 0; i < steps; i++)
            {
                float h = (i + 1) * rise;
                Box(stairs.transform, $"Step_{i}", new Vector3(stairX, h * 0.5f, zStart + i * depth + 0.2f),
                    new Vector3(1.8f, h, depth), stepMat);
            }

            // --- メザニン床（東半分の北側。x[-1,7], z[1.5,5.7]）---
            Box(root.transform, "MezzFloor", new Vector3(3f, MezzY - 0.1f, 3.6f), new Vector3(8f, 0.2f, 4.2f), floorMat);

            // --- 手すり（落下防止。高さ1m）---
            float ry = MezzY + 0.5f;
            // 南縁（階段の隙間 x[5.1,6.9] を除く）
            Box(root.transform, "Rail_S_L", new Vector3(2.05f, ry, 1.5f), new Vector3(6.1f, 1f, 0.12f), railMat);
            // 西縁・東縁
            Box(root.transform, "Rail_W", new Vector3(-1f, ry, 3.6f), new Vector3(0.12f, 1f, 4.2f), railMat);
            Box(root.transform, "Rail_E", new Vector3(7f, ry, 3.6f), new Vector3(0.12f, 1f, 4.2f), railMat);

            // 2階の明かり
            CreatePointLight("Light_2F", new Vector3(3f, MezzY + 2.2f, 4f), new Color(0.8f, 0.82f, 0.9f), 0.7f, 10f);

            // --- 4つの個室（RoomEmployeeNumbers と対応）---
            float[] cxs = { 0f, 2f, 4f, 6f };
            for (int i = 0; i < PuzzleState.RoomEmployeeNumbers.Length && i < cxs.Length; i++)
                BuildPrivateRoom(root.transform, PuzzleState.RoomEmployeeNumbers[i], cxs[i]);
        }

        private static void BuildPrivateRoom(Transform parent, string employeeNumber, float cx)
        {
            string name = EmployeeName(employeeNumber);
            var wallMat = GetMat("RoomWall", new Color(0.26f, 0.25f, 0.28f));
            var roomRoot = new GameObject($"PrivateRoom_{employeeNumber}");
            roomRoot.transform.SetParent(parent, false);

            const float RW = 2.0f, RD = 2.6f, RH = 2.4f, t = 0.12f;
            float zS = 2.8f, zN = zS + RD;                 // 2.8 〜 5.4
            float yB = MezzY, yC = yB + RH * 0.5f;          // 壁の中心高さ

            // 北・東・西壁
            Box(roomRoot.transform, "W_N", new Vector3(cx, yC, zN), new Vector3(RW, RH, t), wallMat);
            Box(roomRoot.transform, "W_E", new Vector3(cx + RW * 0.5f, yC, zS + RD * 0.5f), new Vector3(t, RH, RD), wallMat);
            Box(roomRoot.transform, "W_W", new Vector3(cx - RW * 0.5f, yC, zS + RD * 0.5f), new Vector3(t, RH, RD), wallMat);
            // 南壁（ドアの隙間 幅1.0）
            float doorGap = 1.0f, seg = (RW - doorGap) * 0.5f;
            Box(roomRoot.transform, "W_S_L", new Vector3(cx - (doorGap * 0.5f + seg * 0.5f), yC, zS), new Vector3(seg, RH, t), wallMat);
            Box(roomRoot.transform, "W_S_R", new Vector3(cx + (doorGap * 0.5f + seg * 0.5f), yC, zS), new Vector3(seg, RH, t), wallMat);
            Box(roomRoot.transform, "W_S_Top", new Vector3(cx, yB + RH - 0.3f, zS), new Vector3(doorGap, 0.6f, t), wallMat);

            // 表札（ドア上部・部屋手前=南向き）
            Nameplate(roomRoot.transform, $"個室\n{name}", new Vector3(cx, yB + RH - 0.15f, zS - 0.1f));

            // 戸棚（北壁際）：調べると鍵保管者なら鍵入手。見た目はBlender製ローポリ、
            // インタラクト用コライダーはルートに付ける（レイキャストが KeyCabinet を掴めるように）
            var cab = new GameObject("KeyCabinet");
            cab.transform.SetParent(roomRoot.transform, false);
            cab.transform.position = new Vector3(cx, yB, zN - 0.4f);
            cab.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // 扉がドア側（南）を向く
            if (PlaceModel(cab.transform, "Cabinet", Vector3.zero) == null)
            {
                var cabMat = GetMat("RoomCabinet", new Color(0.4f, 0.3f, 0.22f));
                var body = Box(cab.transform, "Body", new Vector3(0f, 0.5f, 0f), new Vector3(1.0f, 1.0f, 0.5f), cabMat);
                Object.DestroyImmediate(body.GetComponent<Collider>());
            }
            AddBoxCollider(cab, new Vector3(0f, 0.5f, 0f), new Vector3(1.0f, 1.0f, 0.5f));
            cab.AddComponent<KeyCabinet>().OwnerNumber = employeeNumber;
        }

        private static string EmployeeName(string number)
        {
            foreach (var e in PuzzleState.Employees) if (e.number == number) return e.name;
            return number;
        }

        private static void Nameplate(Transform parent, string text, Vector3 localPos)
        {
            var go = new GameObject("Nameplate");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // 文字が南(-Z)を向く
            go.AddComponent<RoomNameplate>().Label = text;  // TextMeshは実行時に生成
        }

        // ============================== 隠れ場所（ロッカー / ベッド下）==============================

        private static void BuildHidingSpots()
        {
            CreateLocker("Locker_Hall", new Vector3(-RoomW * 0.5f + 0.7f, 0f, -RoomD * 0.5f + 1.2f), 90f);
            CreateBed(new Vector3(RoomW * 0.5f - 2.0f, 0f, RoomD * 0.5f - 1.8f), 0f);
        }

        private static void CreateLocker(string name, Vector3 pos, float yRot)
        {
            var root = new GameObject(name);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

            const float w = 1.0f, h = 2.3f, d = 0.9f;
            // 見た目：Blender製ローポリ。当たりは旧プリミティブと同配置の不可視コライダー
            //（HidingSpot が中の空洞を前提にするため、単一の箱にはしない）
            var model = PlaceModel(root.transform, "Locker", Vector3.zero);
            if (model != null)
            {
                AddBoxCollider(root, new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.06f));
                AddBoxCollider(root, new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(0.06f, h, d));
                AddBoxCollider(root, new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(0.06f, h, d));
                AddBoxCollider(root, new Vector3(0f, h, 0f), new Vector3(w, 0.06f, d));
                AddBoxCollider(root, new Vector3(0f, 0.65f, d * 0.5f), new Vector3(w, 1.3f, 0.06f));
            }
            else
            {
                var mat = GetMat("Locker", new Color(0.25f, 0.3f, 0.38f), 0.6f);
                Box(root.transform, "Back", new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.06f), mat);
                Box(root.transform, "Left", new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(0.06f, h, d), mat);
                Box(root.transform, "Right", new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(0.06f, h, d), mat);
                Box(root.transform, "Top", new Vector3(0f, h, 0f), new Vector3(w, 0.06f, d), mat);
                Box(root.transform, "FrontPanel", new Vector3(0f, 0.65f, d * 0.5f), new Vector3(w, 1.3f, 0.06f), mat);
            }

            var trigger = root.AddComponent<BoxCollider>();
            trigger.center = new Vector3(0f, 1.2f, d * 0.5f + 0.1f);
            trigger.size = new Vector3(w, 2.2f, 0.3f);
            SetupHidingSpot(root, new Vector3(0f, 0.1f, -0.05f), new Vector3(0f, 0.1f, d * 0.5f + 0.8f));
        }

        private static void CreateBed(Vector3 pos, float yRot)
        {
            var root = new GameObject("Bed");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

            const float w = 1.4f, len = 2.4f;
            // 見た目：Blender製ローポリ。当たりは旧プリミティブと同配置
            //（脚で持ち上がっており、床下に潜れる隙間が要る）
            var model = PlaceModel(root.transform, "Bed", Vector3.zero);
            if (model != null)
            {
                AddBoxCollider(root, new Vector3(0f, 0.55f, 0f), new Vector3(w, 0.25f, len));
                AddBoxCollider(root, new Vector3(0f, 0.4f, 0f), new Vector3(w + 0.1f, 0.1f, len + 0.1f));
            }
            else
            {
                var frameMat = GetMat("BedFrame", new Color(0.3f, 0.22f, 0.16f));
                var sheetMat = GetMat("BedSheet", new Color(0.4f, 0.4f, 0.45f));
                Box(root.transform, "Mattress", new Vector3(0f, 0.55f, 0f), new Vector3(w, 0.25f, len), sheetMat);
                Box(root.transform, "Frame", new Vector3(0f, 0.4f, 0f), new Vector3(w + 0.1f, 0.1f, len + 0.1f), frameMat);
                Box(root.transform, "Leg1", new Vector3(w * 0.5f - 0.1f, 0.18f, len * 0.5f - 0.1f), new Vector3(0.1f, 0.36f, 0.1f), frameMat);
                Box(root.transform, "Leg2", new Vector3(-w * 0.5f + 0.1f, 0.18f, len * 0.5f - 0.1f), new Vector3(0.1f, 0.36f, 0.1f), frameMat);
                Box(root.transform, "Leg3", new Vector3(w * 0.5f - 0.1f, 0.18f, -len * 0.5f + 0.1f), new Vector3(0.1f, 0.36f, 0.1f), frameMat);
                Box(root.transform, "Leg4", new Vector3(-w * 0.5f + 0.1f, 0.18f, -len * 0.5f + 0.1f), new Vector3(0.1f, 0.36f, 0.1f), frameMat);
            }

            // ベッド脇のインタラクト判定
            var trigger = root.AddComponent<BoxCollider>();
            trigger.center = new Vector3(w * 0.5f + 0.2f, 0.4f, 0f);
            trigger.size = new Vector3(0.5f, 0.8f, len);
            // 中＝ベッド下、出る＝脇
            SetupHidingSpot(root, new Vector3(0f, 0.05f, 0f), new Vector3(w * 0.5f + 0.7f, 0.1f, 0f));
        }

        private static void SetupHidingSpot(GameObject root, Vector3 insideLocal, Vector3 exitLocal)
        {
            var spot = root.AddComponent<HidingSpot>();
            var inside = new GameObject("InsideAnchor");
            inside.transform.SetParent(root.transform, false);
            inside.transform.localPosition = insideLocal;
            var exit = new GameObject("ExitAnchor");
            exit.transform.SetParent(root.transform, false);
            exit.transform.localPosition = exitLocal;

            var so = new SerializedObject(spot);
            so.FindProperty("_insideAnchor").objectReferenceValue = inside.transform;
            so.FindProperty("_exitAnchor").objectReferenceValue = exit.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ============================== 電気スイッチ ==============================

        private static void BuildLightSwitch()
        {
            var mat = GetMat("Switch", new Color(0.85f, 0.85f, 0.8f), 0.3f);
            // 南壁の脱出ドア脇
            var sw = Box(null, "LightSwitch", new Vector3(1.4f, 1.3f, -RoomD * 0.5f + 0.2f), new Vector3(0.2f, 0.3f, 0.08f), mat);
            sw.AddComponent<RoomLightController>(); // ライトは未指定→Pointを自動収集
        }

        // ============================== ディスプレイ（古時計）==============================

        private static void BuildDisplays()
        {
            // 古時計（西壁際に立てる。隠れながらでも針が見える位置）
            var clock = new GameObject("GrandfatherClock");
            clock.transform.position = new Vector3(-RoomW * 0.5f + 0.5f, 0f, -0.5f);
            clock.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // 文字盤が部屋側(+X)を向く
            clock.AddComponent<GrandfatherClock>();
        }

        // ============================== 探索者まわり ==============================

        private static void BuildEnemyInfrastructure()
        {
            var root = new GameObject("SearcherSystem");

            // 出現・帰還地点＝襲撃者の部屋の中（西）
            var entry = new GameObject("EntryPoint");
            entry.transform.SetParent(root.transform, false);
            entry.transform.position = SrCenter + new Vector3(-0.5f, 0.05f, 0f);

            // 帰還地点：部屋の入口すぐ内側（壁ずり移動でもドアの隙間を直線で通れる位置）
            var exit = new GameObject("ExitPoint");
            exit.transform.SetParent(root.transform, false);
            exit.transform.position = new Vector3(-9.5f, 0.05f, 0f);

            // 巡回点：先頭はドアの正面（出現位置から一直線に部屋を出られるように）
            Vector3[] patrolPositions =
            {
                new Vector3(-5.5f, 0.05f, 0f),
                new Vector3(0f, 0.05f, 3.5f),
                new Vector3(5f, 0.05f, 0.5f),
                new Vector3(0f, 0.05f, -3.5f),
            };
            var points = new Transform[patrolPositions.Length];
            for (int i = 0; i < patrolPositions.Length; i++)
            {
                var p = new GameObject($"Patrol_{i}");
                p.transform.SetParent(root.transform, false);
                p.transform.position = patrolPositions[i];
                points[i] = p.transform;
            }

            var spawner = root.AddComponent<EnemySpawner>();
            spawner.EntryPoint = entry.transform;
            spawner.ExitPoint = exit.transform;
            spawner.PatrolPoints = points;
        }

        // ============================== 日本人形イベント ==============================

        private static void BuildDollEvent()
        {
            float cx = EvCenter.x;
            var root = new GameObject("DollEvent");
            root.transform.position = new Vector3(cx + 1.5f, 0f, 0f);

            var ev = root.AddComponent<DollEvent>();

            // 人形本体（金髪・和装・閉眼）
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var bodyMat = new Material(shader) { color = new Color(0.7f, 0.1f, 0.25f) }; // 赤い和服
            var skinMat = new Material(shader) { color = new Color(0.9f, 0.85f, 0.8f) };

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "DollBody";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            body.transform.localScale = new Vector3(0.4f, 0.5f, 0.4f);
            body.GetComponent<Renderer>().material = bodyMat;
            // 本体コライダー（インタラクト用・非トリガー）
            var di = body.AddComponent<DollInteract>();
            di.SetEvent(ev);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "DollHead";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            head.transform.localScale = Vector3.one * 0.32f;
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.GetComponent<Renderer>().material = skinMat;

            // 髪（金髪。黒化対象）
            var hair1 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hair1.name = "Hair_Top";
            hair1.transform.SetParent(root.transform, false);
            hair1.transform.localPosition = new Vector3(0f, 1.12f, -0.02f);
            hair1.transform.localScale = new Vector3(0.36f, 0.32f, 0.38f);
            Object.DestroyImmediate(hair1.GetComponent<Collider>());
            var hairMat1 = new Material(shader) { color = new Color(0.92f, 0.82f, 0.35f) };
            hair1.GetComponent<Renderer>().material = hairMat1;

            var hair2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hair2.name = "Hair_Back";
            hair2.transform.SetParent(root.transform, false);
            hair2.transform.localPosition = new Vector3(0f, 0.75f, -0.14f);
            hair2.transform.localScale = new Vector3(0.32f, 0.7f, 0.1f);
            Object.DestroyImmediate(hair2.GetComponent<Collider>());
            var hairMat2 = new Material(shader) { color = new Color(0.92f, 0.82f, 0.35f) };
            hair2.GetComponent<Renderer>().material = hairMat2;

            // 目（閉眼＝暗い。開眼で発光）
            var eyeL = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eyeL.name = "Eye_L";
            eyeL.transform.SetParent(root.transform, false);
            eyeL.transform.localPosition = new Vector3(-0.08f, 1.08f, 0.15f);
            eyeL.transform.localScale = Vector3.one * 0.07f;
            Object.DestroyImmediate(eyeL.GetComponent<Collider>());
            var eyeMatL = new Material(shader) { color = new Color(0.1f, 0.08f, 0.08f) };
            eyeL.GetComponent<Renderer>().material = eyeMatL;

            var eyeR = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eyeR.name = "Eye_R";
            eyeR.transform.SetParent(root.transform, false);
            eyeR.transform.localPosition = new Vector3(0.08f, 1.08f, 0.15f);
            eyeR.transform.localScale = Vector3.one * 0.07f;
            Object.DestroyImmediate(eyeR.GetComponent<Collider>());
            var eyeMatR = new Material(shader) { color = new Color(0.1f, 0.08f, 0.08f) };
            eyeR.GetComponent<Renderer>().material = eyeMatR;

            // DollEvent に renderers を割当
            var so = new SerializedObject(ev);
            var hairProp = so.FindProperty("_hairRenderers");
            hairProp.arraySize = 2;
            hairProp.GetArrayElementAtIndex(0).objectReferenceValue = hair1.GetComponent<Renderer>();
            hairProp.GetArrayElementAtIndex(1).objectReferenceValue = hair2.GetComponent<Renderer>();
            var eyeProp = so.FindProperty("_eyeRenderers");
            eyeProp.arraySize = 2;
            eyeProp.GetArrayElementAtIndex(0).objectReferenceValue = eyeL.GetComponent<Renderer>();
            eyeProp.GetArrayElementAtIndex(1).objectReferenceValue = eyeR.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            // 部屋侵入トリガー（イベント部屋全体）
            var zoneGo = new GameObject("EventTriggerZone");
            zoneGo.transform.position = EvCenter;
            var zoneCol = zoneGo.AddComponent<BoxCollider>();
            zoneCol.isTrigger = true;
            zoneCol.size = new Vector3(EvW - 0.6f, WallH, EvD - 0.6f);
            zoneCol.center = new Vector3(0f, WallH * 0.5f, 0f);
            var zone = zoneGo.AddComponent<DollTriggerZone>();
            zone.SetEvent(ev);

            // 硝子の目×2（1つは本部屋、1つはイベント部屋に隠す）
            CreateEyeItem("EyeItem_Hall", new Vector3(-RoomW * 0.5f + 1.5f, 0.35f, RoomD * 0.5f - 1.0f)); // 本部屋ベッド付近
            CreateEyeItem("EyeItem_Event", new Vector3(cx + EvW * 0.5f - 0.8f, 0.35f, EvD * 0.5f - 0.8f)); // イベント部屋奥
        }

        private static void CreateEyeItem(string name, Vector3 pos)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = name;
            eye.transform.position = pos;
            eye.transform.localScale = Vector3.one * 0.12f;
            var mat = new Material(shader) { color = new Color(0.85f, 0.85f, 0.9f) };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.9f);
            eye.GetComponent<Renderer>().material = mat;
            eye.AddComponent<EyeItem>();
        }

        // ============================== 部屋のPrefab化 ==============================

        /// <summary>
        /// 各部屋の壁・ドア・照明・小物を1つの部屋ルート配下にまとめ、ピボットを部屋中心へ
        /// 置き直してから個別Prefab（Assets/EscapePrototype/Prefabs/Rooms/）として保存する。
        /// 座標は一切変えず、階層とピボットだけを整理するのでゲーム挙動は不変。
        /// これで後から各Prefabインスタンスをドラッグ／回転してレイアウトし直せる。
        /// </summary>
        private static void OrganizeAndPrefab()
        {
            // メインホール：脱出ドアと柱は既に Room 配下。残りの床置き小物・照明を取り込む
            var room = GameObject.Find("Room");
            Absorb(room, "LightSwitch", "GrandfatherClock", "Locker_Hall", "Bed", "Light_Center", "Light_West", "EyeItem_Hall");
            Recenter(room, Vector3.zero);

            var ev = GameObject.Find("EventRoom");
            Absorb(ev, "DollEvent", "EventTriggerZone", "EyeItem_Event", "Light_Event");
            Recenter(ev, EvCenter);

            var sr = GameObject.Find("SearcherRoom");
            Absorb(sr, "SearcherRoomDoor", "WallSafe", "Locker_Searcher", "Light_Searcher");
            Recenter(sr, SrCenter);

            var ut = GameObject.Find("UtilityRoom");
            Absorb(ut, "UtilityRoomDoor", "Light_Utility");
            Recenter(ut, new Vector3(0f, 0f, UtCenter.z));

            // 事務所：机は既に Office 配下。PuzzleDocs 側の資料と照明を取り込む
            var of = GameObject.Find("Office");
            Absorb(of, "AlbumTable", "Album", "FileCabinet", "PersonnelFile",
                       "PcDeskBody", "PcDeskTop", "PcStand", "SocialPC", "Light_Office");
            Recenter(of, OfCenter);

            var sf = GameObject.Find("SecondFloor");
            Absorb(sf, "Light_2F");
            Recenter(sf, new Vector3(3f, MezzY, 3.6f));

            SaveRoomPrefabs(new[] { room, ev, sr, ut, of, sf });
        }

        /// <summary>指定名のトップレベルGameObjectを、ワールド位置を保ったまま部屋ルート配下へ移す。</summary>
        private static void Absorb(GameObject roomRoot, params string[] names)
        {
            if (roomRoot == null) return;
            foreach (var n in names)
            {
                var go = GameObject.Find(n);
                if (go != null && go != roomRoot)
                    go.transform.SetParent(roomRoot.transform, true);
            }
        }

        /// <summary>子のワールド位置を保ったまま、ルートのピボットだけを center へ移動する。</summary>
        private static void Recenter(GameObject root, Vector3 center)
        {
            if (root == null) return;
            var kids = new List<Transform>();
            foreach (Transform c in root.transform) kids.Add(c);
            foreach (var c in kids) c.SetParent(null, true);
            root.transform.position = center;
            foreach (var c in kids) c.SetParent(root.transform, true);
        }

        private static void SaveRoomPrefabs(GameObject[] roots)
        {
            const string prefabDir = RootDir + "/Prefabs";
            const string roomsDir = prefabDir + "/Rooms";
            if (!AssetDatabase.IsValidFolder(prefabDir)) AssetDatabase.CreateFolder(RootDir, "Prefabs");
            if (!AssetDatabase.IsValidFolder(roomsDir)) AssetDatabase.CreateFolder(prefabDir, "Rooms");

            foreach (var r in roots)
            {
                if (r == null) continue;
                string path = $"{roomsDir}/{r.name}.prefab";
                // シーンのインスタンスは残したまま、Prefabアセットへ接続する
                PrefabUtility.SaveAsPrefabAssetAndConnect(r, path, InteractionMode.AutomatedAction);
            }
            Debug.Log($"[PrototypeSceneBuilder] 部屋Prefabを {roomsDir} に保存しました（{roots.Length}件）");
        }

        // ============================== プリミティブヘルパー ==============================

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static GameObject Cylinder(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// Blender製FBXモデル（Assets/EscapePrototype/Models/）を配置する。
        /// FBXにはコライダーが無いので、当たりが必要な場合は呼び出し側で
        /// AddBoxCollider により旧プリミティブと同配置の不可視コライダーを付ける。
        /// モデルが見つからない場合は null（呼び出し側でプリミティブにフォールバック）。
        /// </summary>
        private static GameObject PlaceModel(Transform parent, string modelName, Vector3 localPos, float yRot = 0f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RootDir}/Models/{modelName}.fbx");
            if (prefab == null)
            {
                Debug.LogWarning($"[PrototypeSceneBuilder] モデル未検出: {modelName}.fbx（プリミティブで代替）");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            return go;
        }

        private static void AddBoxCollider(GameObject root, Vector3 center, Vector3 size)
        {
            var c = root.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
        }
    }
}
#endif
