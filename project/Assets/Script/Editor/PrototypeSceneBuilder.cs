#if UNITY_EDITOR
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
    /// 構築内容（新仕様：地下室からの脱出ホラー）：
    /// ・地下室（床/壁/敵の入口通路）＋ 別部屋（日本人形イベント部屋）
    /// ・中央の大きい机：モニター / 手帳 / 陶器人形5体 / 香水
    /// ・古時計、電気スイッチ、懐中電灯（プレイヤー）
    /// ・ギミック×3、脱出ドア、ロッカー、ベッド（下に隠れる）
    /// ・探索者スポーン、巡回点
    /// ・別部屋：金髪人形(DollEvent)＋硝子の目×2
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
            BuildPuzzleDocs();
            BuildPowerRoom();
            BuildSecondFloor();
            BuildHidingSpots();
            BuildLightSwitch();
            BuildDisplays();
            BuildEnemyInfrastructure();
            BuildDollEvent();

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
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.07f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = Color.black;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.03f;

            // これらPointライトが「部屋の電気」。電気スイッチが自動収集する
            CreatePointLight("Light_Center", new Vector3(0f, 2.8f, 0f), new Color(1f, 0.9f, 0.7f), 1.1f, 12f);
            CreatePointLight("Light_West", new Vector3(-5f, 2.5f, 3f), new Color(0.8f, 0.85f, 1f), 0.7f, 9f);
            CreatePointLight("Light_Event", EvCenter + new Vector3(0f, 2.6f, 0f), new Color(0.9f, 0.85f, 0.8f), 0.7f, 8f);
            // 入口通路の赤（雰囲気・敵の入室を示す。Pointなので電気で消える）
            CreatePointLight("Light_Corridor", new Vector3(0f, 2.5f, RoomD * 0.5f + 2.5f), new Color(1f, 0.3f, 0.2f), 0.8f, 7f);
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
            Box(root.transform, "CorridorFloor", new Vector3(0, -0.05f, RoomD * 0.5f + 2f), new Vector3(3f, 0.1f, 4f), floorMat);
            // ※天井は撤去（2階メザニンの吹き抜けスペースを確保）

            float hw = RoomW * 0.5f, hd = RoomD * 0.5f;

            // 南壁（脱出ドアの隙間）
            float gap = 1.6f, seg = (RoomW - gap) * 0.5f;
            Box(root.transform, "Wall_S_L", new Vector3(-(gap * 0.5f + seg * 0.5f), WallH * 0.5f, -hd), new Vector3(seg, WallH, 0.3f), wallMat);
            Box(root.transform, "Wall_S_R", new Vector3(gap * 0.5f + seg * 0.5f, WallH * 0.5f, -hd), new Vector3(seg, WallH, 0.3f), wallMat);
            Box(root.transform, "Wall_S_Top", new Vector3(0f, WallH - 0.35f, -hd), new Vector3(gap, 0.7f, 0.3f), wallMat);

            // 北壁（敵の入口）
            float entryGap = 1.8f, nseg = (RoomW - entryGap) * 0.5f;
            Box(root.transform, "Wall_N_L", new Vector3(-(entryGap * 0.5f + nseg * 0.5f), WallH * 0.5f, hd), new Vector3(nseg, WallH, 0.3f), wallMat);
            Box(root.transform, "Wall_N_R", new Vector3(entryGap * 0.5f + nseg * 0.5f, WallH * 0.5f, hd), new Vector3(nseg, WallH, 0.3f), wallMat);
            Box(root.transform, "Wall_N_Top", new Vector3(0f, WallH - 0.35f, hd), new Vector3(entryGap, 0.7f, 0.3f), wallMat);

            // 西壁
            Box(root.transform, "Wall_W", new Vector3(-hw, WallH * 0.5f, 0f), new Vector3(0.3f, WallH, RoomD), wallMat);

            // 東壁（イベント部屋への扉の隙間 幅1.6m を Z=0 に）
            float eGap = 1.6f, eseg = (RoomD - eGap) * 0.5f;
            Box(root.transform, "Wall_E_N", new Vector3(hw, WallH * 0.5f, eGap * 0.5f + eseg * 0.5f), new Vector3(0.3f, WallH, eseg), wallMat);
            Box(root.transform, "Wall_E_S", new Vector3(hw, WallH * 0.5f, -(eGap * 0.5f + eseg * 0.5f)), new Vector3(0.3f, WallH, eseg), wallMat);
            Box(root.transform, "Wall_E_Top", new Vector3(hw, WallH - 0.35f, 0f), new Vector3(0.3f, 0.7f, eGap), wallMat);

            // 入口通路（袋小路）
            Box(root.transform, "Corr_E", new Vector3(entryGap * 0.5f + 0.15f, WallH * 0.5f, hd + 2f), new Vector3(0.3f, WallH, 4f), wallMat);
            Box(root.transform, "Corr_W", new Vector3(-entryGap * 0.5f - 0.15f, WallH * 0.5f, hd + 2f), new Vector3(0.3f, WallH, 4f), wallMat);
            Box(root.transform, "Corr_End", new Vector3(0f, WallH * 0.5f, hd + 4f), new Vector3(entryGap + 0.6f, WallH, 0.3f), wallMat);

            // 視線切り用の柱
            Box(root.transform, "Pillar", new Vector3(3.5f, WallH * 0.5f, 1f), new Vector3(0.9f, WallH, 0.9f), wallMat);

            // 脱出ドア
            var doorMat = GetMat("ExitDoor", new Color(0.35f, 0.25f, 0.2f));
            var door = Box(root.transform, "ExitDoor", new Vector3(0f, 1.25f, -hd), new Vector3(gap - 0.1f, 2.5f, 0.25f), doorMat);
            door.AddComponent<ExitDoor>();
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
        }

        // ============================== 中央の大きい机 ==============================

        private static void BuildCentralDesk()
        {
            var root = new GameObject("CentralDesk");
            var woodMat = GetMat("Wood", new Color(0.4f, 0.28f, 0.18f));

            Vector3 deskPos = new Vector3(0f, 0f, -1.5f);
            // 大机
            Box(root.transform, "DeskTop", deskPos + new Vector3(0f, 0.85f, 0f), new Vector3(3.2f, 0.1f, 1.4f), woodMat);
            Box(root.transform, "DeskBody", deskPos + new Vector3(0f, 0.42f, 0f), new Vector3(3.0f, 0.85f, 1.2f), woodMat);

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

            // アルバム（別の小机の上：部屋を行き来させる）
            var tableMat = GetMat("SideTable", new Color(0.4f, 0.28f, 0.18f));
            Box(root.transform, "AlbumTable", new Vector3(4.8f, 0.45f, 3.2f), new Vector3(0.8f, 0.9f, 0.6f), tableMat);
            var albumMat = GetMat("Album", new Color(0.5f, 0.3f, 0.55f));
            var album = Box(root.transform, "Album", new Vector3(4.8f, 0.95f, 3.2f), new Vector3(0.45f, 0.08f, 0.34f), albumMat);
            album.AddComponent<DocumentInteract>().Type = DocumentType.Album;

            // 人事ファイル（氏名→生年月日）：書類棚の上（南西）
            var cabMat = GetMat("FileCabinet", new Color(0.3f, 0.32f, 0.35f), 0.4f);
            Box(root.transform, "FileCabinet", new Vector3(-6.6f, 0.6f, -4.0f), new Vector3(0.8f, 1.2f, 0.5f), cabMat);
            var fileMat = GetMat("PersonnelFile", new Color(0.8f, 0.75f, 0.6f));
            var file = Box(root.transform, "PersonnelFile", new Vector3(-6.6f, 1.25f, -4.0f), new Vector3(0.4f, 0.06f, 0.3f), fileMat);
            file.AddComponent<DocumentInteract>().Type = DocumentType.PersonnelFile;

            // 社内PCデスク（南東）：ID=社員番号 / PW=生年月日 でログイン
            // モニターは机の手前・上面に置き、部屋側（+Z）から正面で触れるようにする
            var pcDeskMat = GetMat("PcDesk", new Color(0.35f, 0.3f, 0.26f));
            Box(root.transform, "PcDeskBody", new Vector3(5.5f, 0.4f, -3.7f), new Vector3(1.4f, 0.8f, 0.7f), pcDeskMat);
            Box(root.transform, "PcDeskTop", new Vector3(5.5f, 0.82f, -3.7f), new Vector3(1.5f, 0.08f, 0.8f), pcDeskMat);
            var pcMat = GetMat("PcMonitor", new Color(0.1f, 0.12f, 0.16f), 0.5f);
            Box(root.transform, "PcStand", new Vector3(5.5f, 0.98f, -3.75f), new Vector3(0.1f, 0.25f, 0.1f), pcMat);
            // 画面は机の前縁（部屋側）に大きめに。プレイヤーは北側(+Z)から正対して触れる
            var pc = Box(root.transform, "SocialPC", new Vector3(5.5f, 1.3f, -3.35f), new Vector3(0.8f, 0.55f, 0.1f), pcMat);
            pc.AddComponent<PcDesk>();
        }

        // ============================== 謎解き：配電室（配電盤＋説明書）==============================

        private static void BuildPowerRoom()
        {
            var root = new GameObject("PowerRoom");
            var wallMat = GetMat("PowerWall", new Color(0.2f, 0.22f, 0.26f));
            float hw = RoomW * 0.5f, hd = RoomD * 0.5f;   // 8, 6

            // 北西コーナーを間仕切りして配電室にする（出入口は開放）
            const float zS = 2.0f;    // 南境界
            const float xE = -3.8f;   // 東境界
            const float gapC = -5.8f; // 出入口中心X
            const float gap = 1.6f;

            // 南壁（出入口の隙間つき）
            float leftRight = gapC - gap * 0.5f;
            float leftLen = Mathf.Max(0.1f, leftRight - (-hw));
            Box(root.transform, "PWall_S_L", new Vector3((-hw + leftRight) * 0.5f, WallH * 0.5f, zS), new Vector3(leftLen, WallH, 0.3f), wallMat);
            float rightLeft = gapC + gap * 0.5f;
            float rightLen = Mathf.Max(0.1f, xE - rightLeft);
            Box(root.transform, "PWall_S_R", new Vector3((rightLeft + xE) * 0.5f, WallH * 0.5f, zS), new Vector3(rightLen, WallH, 0.3f), wallMat);
            Box(root.transform, "PWall_S_Top", new Vector3(gapC, WallH - 0.35f, zS), new Vector3(gap, 0.7f, 0.3f), wallMat);

            // 東壁
            Box(root.transform, "PWall_E", new Vector3(xE, WallH * 0.5f, (zS + hd) * 0.5f), new Vector3(0.3f, WallH, hd - zS), wallMat);

            // 配電室の入口表示
            CreatePointLight("Light_Power", new Vector3(-5.8f, 2.6f, 4.0f), new Color(0.7f, 0.8f, 1f), 0.6f, 7f);

            // 施錠ドア（配電室の鍵で解錠）
            var doorRoot = new GameObject("PowerRoomDoor");
            doorRoot.transform.position = new Vector3(gapC, 0f, zS);
            var kd = doorRoot.AddComponent<KeyedDoor>();
            var doorMat = GetMat("SecDoor", new Color(0.3f, 0.32f, 0.38f), 0.5f);
            var door = Box(doorRoot.transform, "Door", new Vector3(0f, 1.25f, 0f), new Vector3(gap - 0.05f, 2.5f, 0.2f), doorMat);
            var lockMat = GetMat("SecLock", new Color(0.9f, 0.2f, 0.2f));
            var lockBox = Box(doorRoot.transform, "Lock", new Vector3(gap * 0.5f - 0.2f, 1.3f, 0.13f), new Vector3(0.12f, 0.18f, 0.06f), lockMat);
            var kdSo = new SerializedObject(kd);
            kdSo.FindProperty("_door").objectReferenceValue = door.transform;
            kdSo.FindProperty("_doorCollider").objectReferenceValue = door.GetComponent<Collider>();
            kdSo.FindProperty("_lockIndicator").objectReferenceValue = lockBox.GetComponent<Renderer>();
            kdSo.ApplyModifiedPropertiesWithoutUndo();

            // 配電盤（配電室の西壁・大きめで正面から触れる。壁から少し張り出す）
            var boardMat = GetMat("Board", new Color(0.2f, 0.2f, 0.22f), 0.3f);
            var board = Box(root.transform, "DistributionBoard", new Vector3(-hw + 0.55f, 1.3f, 4.0f), new Vector3(0.35f, 1.3f, 1.1f), boardMat);
            board.AddComponent<DistributionBoard>();

            // 説明書3種（棚に並べる：部署ページが指定する型番を選ぶ）
            var shelfMat = GetMat("ManualShelf", new Color(0.35f, 0.28f, 0.2f));
            Box(root.transform, "ManualShelf", new Vector3(-5.8f, 0.85f, 5.6f), new Vector3(2.6f, 0.1f, 0.5f), shelfMat);
            string[] models = { "DXR-100", "DXR-200", "DXR-330" };
            for (int i = 0; i < models.Length; i++)
            {
                var mMat = GetMat("Manual_" + models[i], new Color(0.85f, 0.8f, 0.7f));
                var man = Box(root.transform, "Manual_" + models[i], new Vector3(-6.8f + i * 1.0f, 0.95f, 5.6f), new Vector3(0.4f, 0.06f, 0.3f), mMat);
                var di = man.AddComponent<DocumentInteract>();
                di.Type = DocumentType.Manual;
                di.ManualModel = models[i];
            }
        }

        // ============================== 2階：社員個室（鍵の保管場所）==============================

        private const float MezzY = 2.6f;   // 2階の床高さ

        private static void BuildSecondFloor()
        {
            var root = new GameObject("SecondFloor");
            var stepMat = GetMat("Stairs", new Color(0.3f, 0.3f, 0.33f), 0.3f);
            var floorMat = GetMat("MezzFloor", new Color(0.22f, 0.22f, 0.25f));
            var railMat = GetMat("Railing", new Color(0.35f, 0.3f, 0.25f));

            // --- 階段（東中央から北へ登る。1段0.26m×10段=2.6m）---
            var stairs = new GameObject("Stairs"); stairs.transform.SetParent(root.transform, false);
            const int steps = 10; const float rise = 0.26f, depth = 0.4f, zStart = -2.2f;
            for (int i = 0; i < steps; i++)
            {
                float h = (i + 1) * rise;
                Box(stairs.transform, $"Step_{i}", new Vector3(2f, h * 0.5f, zStart + i * depth + 0.2f),
                    new Vector3(1.8f, h, depth), stepMat);
            }

            // --- メザニン床（東半分の北側。x[-1,7], z[1.5,5.7]）---
            Box(root.transform, "MezzFloor", new Vector3(3f, MezzY - 0.1f, 3.6f), new Vector3(8f, 0.2f, 4.2f), floorMat);

            // --- 手すり（落下防止。高さ1m）---
            float ry = MezzY + 0.5f;
            // 南縁（階段の隙間 x[1.1,2.9] を除く）
            Box(root.transform, "Rail_S_L", new Vector3(0.05f, ry, 1.5f), new Vector3(2.1f, 1f, 0.12f), railMat);
            Box(root.transform, "Rail_S_R", new Vector3(4.95f, ry, 1.5f), new Vector3(4.1f, 1f, 0.12f), railMat);
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

            // 戸棚（北壁際）：調べると鍵保管者なら鍵入手
            var cabMat = GetMat("RoomCabinet", new Color(0.4f, 0.3f, 0.22f));
            var cab = Box(roomRoot.transform, "KeyCabinet", new Vector3(cx, yB + 0.5f, zN - 0.4f), new Vector3(1.0f, 1.0f, 0.5f), cabMat);
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
            CreateLocker(new Vector3(-RoomW * 0.5f + 0.7f, 0f, -RoomD * 0.5f + 1.2f), 90f);
            CreateBed(new Vector3(RoomW * 0.5f - 2.0f, 0f, RoomD * 0.5f - 1.8f), 0f);
        }

        private static void CreateLocker(Vector3 pos, float yRot)
        {
            var mat = GetMat("Locker", new Color(0.25f, 0.3f, 0.38f), 0.6f);
            var root = new GameObject("Locker");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

            const float w = 1.0f, h = 2.3f, d = 0.9f;
            Box(root.transform, "Back", new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.06f), mat);
            Box(root.transform, "Left", new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(0.06f, h, d), mat);
            Box(root.transform, "Right", new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(0.06f, h, d), mat);
            Box(root.transform, "Top", new Vector3(0f, h, 0f), new Vector3(w, 0.06f, d), mat);
            Box(root.transform, "FrontPanel", new Vector3(0f, 0.65f, d * 0.5f), new Vector3(w, 1.3f, 0.06f), mat);

            var trigger = root.AddComponent<BoxCollider>();
            trigger.center = new Vector3(0f, 1.2f, d * 0.5f + 0.1f);
            trigger.size = new Vector3(w, 2.2f, 0.3f);
            SetupHidingSpot(root, new Vector3(0f, 0.1f, -0.05f), new Vector3(0f, 0.1f, d * 0.5f + 0.8f));
        }

        private static void CreateBed(Vector3 pos, float yRot)
        {
            var frameMat = GetMat("BedFrame", new Color(0.3f, 0.22f, 0.16f));
            var sheetMat = GetMat("BedSheet", new Color(0.4f, 0.4f, 0.45f));
            var root = new GameObject("Bed");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

            const float w = 1.4f, len = 2.4f;
            // 脚で持ち上げて下に潜れる隙間を作る
            Box(root.transform, "Mattress", new Vector3(0f, 0.55f, 0f), new Vector3(w, 0.25f, len), sheetMat);
            Box(root.transform, "Frame", new Vector3(0f, 0.4f, 0f), new Vector3(w + 0.1f, 0.1f, len + 0.1f), frameMat);
            Box(root.transform, "Leg1", new Vector3(w * 0.5f - 0.1f, 0.18f, len * 0.5f - 0.1f), new Vector3(0.1f, 0.36f, 0.1f), frameMat);
            Box(root.transform, "Leg2", new Vector3(-w * 0.5f + 0.1f, 0.18f, len * 0.5f - 0.1f), new Vector3(0.1f, 0.36f, 0.1f), frameMat);
            Box(root.transform, "Leg3", new Vector3(w * 0.5f - 0.1f, 0.18f, -len * 0.5f + 0.1f), new Vector3(0.1f, 0.36f, 0.1f), frameMat);
            Box(root.transform, "Leg4", new Vector3(-w * 0.5f + 0.1f, 0.18f, -len * 0.5f + 0.1f), new Vector3(0.1f, 0.36f, 0.1f), frameMat);

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
            float hd = RoomD * 0.5f;
            var root = new GameObject("SearcherSystem");

            var entry = new GameObject("EntryPoint");
            entry.transform.SetParent(root.transform, false);
            entry.transform.position = new Vector3(0f, 0.05f, hd + 3f);

            Vector3[] patrolPositions =
            {
                new Vector3(0f, 0.05f, 3.5f),
                new Vector3(-5f, 0.05f, 0.5f),
                new Vector3(0f, 0.05f, -3.5f),
                new Vector3(5f, 0.05f, 0.5f),
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
            spawner.ExitPoint = entry.transform;
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
            CreateEyeItem(new Vector3(-RoomW * 0.5f + 1.5f, 0.35f, RoomD * 0.5f - 1.0f)); // 本部屋ベッド付近
            CreateEyeItem(new Vector3(cx + EvW * 0.5f - 0.8f, 0.35f, EvD * 0.5f - 0.8f)); // イベント部屋奥
        }

        private static void CreateEyeItem(Vector3 pos)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "EyeItem";
            eye.transform.position = pos;
            eye.transform.localScale = Vector3.one * 0.12f;
            var mat = new Material(shader) { color = new Color(0.85f, 0.85f, 0.9f) };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.9f);
            eye.GetComponent<Renderer>().material = mat;
            eye.AddComponent<EyeItem>();
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
    }
}
#endif
