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
    /// ワンクリックでプレイ可能なプロトタイプシーンを構築するエディタツール
    /// メニュー: Tools > EscapePrototype > Build Prototype Scene
    ///
    /// 構築内容：
    /// 部屋（床/壁/入口通路）、プレイヤー（StarterAssets FPC＋既存InteractionController）、
    /// ギミック×3、脱出ドア、ロッカー×2、テーブル＋人形（残機）、メモ帳、
    /// 監視モニター、壁時計、敵スポーン地点/巡回ポイント、各マネージャー、HUD
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        private const string RootDir = "Assets/EscapePrototype";
        private const string MatDir = RootDir + "/Materials";

        // 部屋サイズ
        private const float RoomW = 16f;   // X
        private const float RoomD = 12f;   // Z
        private const float WallH = 3.2f;

        [MenuItem("Tools/EscapePrototype/Build Prototype Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureFolders();

            BuildLighting();
            BuildRoom();
            var player = BuildPlayer(new Vector3(-5f, 0.1f, -3.5f));
            BuildManagers(player);
            BuildGimmicks();
            BuildLockers();
            BuildFurniture();
            BuildDisplays();
            BuildEnemyInfrastructure();

            string scenePath = RootDir + "/PrototypeScene.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[PrototypeSceneBuilder] 構築完了: {scenePath}\n" +
                      "再生してプレイしてください。WASD移動 / Shift走る / E長押しでギミック解除 / Tabメモ");
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
            RenderSettings.ambientLight = new Color(0.06f, 0.06f, 0.09f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = Color.black;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.03f;

            CreatePointLight("Light_Center", new Vector3(0f, 2.8f, 0f), new Color(1f, 0.9f, 0.7f), 1.1f, 12f);
            CreatePointLight("Light_Corner", new Vector3(-6f, 2.5f, 4f), new Color(0.7f, 0.8f, 1f), 0.7f, 9f);
            CreatePointLight("Light_Corridor", new Vector3(0f, 2.5f, RoomD * 0.5f + 2.5f), new Color(1f, 0.3f, 0.2f), 0.8f, 7f);
        }

        private static void CreatePointLight(string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.Soft;
        }

        private static void BuildRoom()
        {
            var root = new GameObject("Room");
            var floorMat = GetMat("Floor", new Color(0.16f, 0.15f, 0.14f));
            var wallMat = GetMat("Wall", new Color(0.28f, 0.26f, 0.24f));

            // 床
            Box(root.transform, "Floor", new Vector3(0, -0.05f, 0), new Vector3(RoomW, 0.1f, RoomD), floorMat);
            // 通路の床
            Box(root.transform, "CorridorFloor", new Vector3(0, -0.05f, RoomD * 0.5f + 2f), new Vector3(3f, 0.1f, 4f), floorMat);
            // 天井
            Box(root.transform, "Ceiling", new Vector3(0, WallH + 0.05f, 0), new Vector3(RoomW, 0.1f, RoomD), wallMat);

            float hw = RoomW * 0.5f, hd = RoomD * 0.5f;

            // 南壁（脱出ドア用の隙間 幅1.6m）
            float gap = 1.6f;
            float seg = (RoomW - gap) * 0.5f;
            Box(root.transform, "Wall_S_L", new Vector3(-(gap * 0.5f + seg * 0.5f), WallH * 0.5f, -hd), new Vector3(seg, WallH, 0.3f), wallMat);
            Box(root.transform, "Wall_S_R", new Vector3(gap * 0.5f + seg * 0.5f, WallH * 0.5f, -hd), new Vector3(seg, WallH, 0.3f), wallMat);
            Box(root.transform, "Wall_S_Top", new Vector3(0f, WallH - 0.35f, -hd), new Vector3(gap, 0.7f, 0.3f), wallMat);

            // 北壁（敵の入口 幅1.8m 開口。奥は暗い通路）
            float entryGap = 1.8f;
            float nseg = (RoomW - entryGap) * 0.5f;
            Box(root.transform, "Wall_N_L", new Vector3(-(entryGap * 0.5f + nseg * 0.5f), WallH * 0.5f, hd), new Vector3(nseg, WallH, 0.3f), wallMat);
            Box(root.transform, "Wall_N_R", new Vector3(entryGap * 0.5f + nseg * 0.5f, WallH * 0.5f, hd), new Vector3(nseg, WallH, 0.3f), wallMat);
            Box(root.transform, "Wall_N_Top", new Vector3(0f, WallH - 0.35f, hd), new Vector3(entryGap, 0.7f, 0.3f), wallMat);

            // 東西の壁
            Box(root.transform, "Wall_E", new Vector3(hw, WallH * 0.5f, 0f), new Vector3(0.3f, WallH, RoomD), wallMat);
            Box(root.transform, "Wall_W", new Vector3(-hw, WallH * 0.5f, 0f), new Vector3(0.3f, WallH, RoomD), wallMat);

            // 入口通路（袋小路）
            Box(root.transform, "Corr_E", new Vector3(entryGap * 0.5f + 0.15f, WallH * 0.5f, hd + 2f), new Vector3(0.3f, WallH, 4f), wallMat);
            Box(root.transform, "Corr_W", new Vector3(-entryGap * 0.5f - 0.15f, WallH * 0.5f, hd + 2f), new Vector3(0.3f, WallH, 4f), wallMat);
            Box(root.transform, "Corr_End", new Vector3(0f, WallH * 0.5f, hd + 4f), new Vector3(entryGap + 0.6f, WallH, 0.3f), wallMat);

            // 部屋中央の遮蔽物（柱）：視線切りに使える
            Box(root.transform, "Pillar", new Vector3(2.5f, WallH * 0.5f, 1f), new Vector3(0.9f, WallH, 0.9f), wallMat);

            // 脱出ドア
            var doorMat = GetMat("ExitDoor", new Color(0.35f, 0.25f, 0.2f));
            var door = Box(root.transform, "ExitDoor", new Vector3(0f, 1.25f, -hd), new Vector3(gap - 0.1f, 2.5f, 0.25f), doorMat);
            door.AddComponent<ExitDoor>();
        }

        // ============================== プレイヤー ==============================

        private static GameObject BuildPlayer(Vector3 pos)
        {
            var player = new GameObject("Player");
            player.transform.position = pos;
            player.tag = "Player";

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.93f, 0f);

            var inputs = player.AddComponent<StarterAssetsInputs>();
            inputs.cursorLocked = true;
            inputs.cursorInputForLook = true;

#if ENABLE_INPUT_SYSTEM
            var pi = player.AddComponent<PlayerInput>();
            var actionsAsset = FindStarterAssetsActions();
            if (actionsAsset != null)
            {
                pi.actions = actionsAsset;
                pi.defaultActionMap = "Player";
                pi.notificationBehavior = PlayerNotifications.SendMessages;
            }
            else
            {
                Debug.LogError("[PrototypeSceneBuilder] StarterAssets.inputactions が見つかりません。" +
                               "Player の PlayerInput に手動で割り当ててください。");
            }
#endif

            // カメラルート＋カメラ
            var camRoot = new GameObject("PlayerCameraRoot");
            camRoot.transform.SetParent(player.transform, false);
            camRoot.transform.localPosition = new Vector3(0f, 1.55f, 0f);

            var camGo = new GameObject("MainCamera");
            camGo.transform.SetParent(camRoot.transform, false);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.08f;
            cam.fieldOfView = 70f;
            camGo.AddComponent<AudioListener>();

            var fpc = player.AddComponent<FirstPersonController>();
            fpc.CinemachineCameraTarget = camRoot;
            fpc.MoveSpeed = 2.4f;     // ホラー向けに少し遅め
            fpc.SprintSpeed = 4.6f;
            fpc.RotationSpeed = 1.0f;

            player.AddComponent<PlayerStatus>();

            // 既存のインタラクトシステム
            var interaction = player.AddComponent<InteractionController>();
            var so = new SerializedObject(interaction);
            so.FindProperty("_camera").objectReferenceValue = cam;
            so.FindProperty("_interactDistance").floatValue = 3f;
            so.FindProperty("_interactLayer").intValue = ~0; // Everything
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

            var respawn = new GameObject("RespawnPoint");
            respawn.transform.SetParent(root.transform, false);
            respawn.transform.position = new Vector3(-5f, 0.1f, -3.5f);

            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(root.transform, false);
            var gm = gmGo.AddComponent<GameManager>();
            var so = new SerializedObject(gm);
            so.FindProperty("_player").objectReferenceValue = player.transform;
            so.FindProperty("_respawnPoint").objectReferenceValue = respawn.transform;
            so.FindProperty("_lives").intValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();

            var pmGo = new GameObject("PhaseManager");
            pmGo.transform.SetParent(root.transform, false);
            pmGo.AddComponent<PhaseManager>();

            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(root.transform, false);
            hudGo.AddComponent<HUDManager>();
        }

        // ============================== ギミック ==============================

        private static void BuildGimmicks()
        {
            var root = new GameObject("Gimmicks");
            var mat = GetMat("Gimmick", new Color(0.85f, 0.25f, 0.2f), 0.5f);
            float hw = RoomW * 0.5f, hd = RoomD * 0.5f;

            // 1. 配電盤（西壁）
            var g1 = Box(root.transform, "Gimmick_配電盤", new Vector3(-hw + 0.35f, 1.4f, 2.5f), new Vector3(0.25f, 0.9f, 0.7f), mat);
            AddGimmick(g1, "配電盤", 10f);

            // 2. バルブ（東壁）
            var g2 = Cylinder(root.transform, "Gimmick_バルブ", new Vector3(hw - 0.4f, 1.2f, -2.5f), new Vector3(0.5f, 0.12f, 0.5f), mat);
            g2.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            AddGimmick(g2, "バルブ", 14f);

            // 3. 暗号装置（柱の裏側＝視線が切れる位置）
            var g3 = Box(root.transform, "Gimmick_暗号装置", new Vector3(2.5f, 0.95f, 2.0f), new Vector3(0.6f, 0.5f, 0.45f), mat);
            AddGimmick(g3, "暗号装置", 18f);
        }

        private static void AddGimmick(GameObject go, string displayName, float seconds)
        {
            var gimmick = go.AddComponent<GimmickBase>();
            var so = new SerializedObject(gimmick);
            so.FindProperty("_displayName").stringValue = displayName;
            so.FindProperty("_requiredSeconds").floatValue = seconds;
            so.FindProperty("_statusRenderer").objectReferenceValue = go.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ============================== ロッカー ==============================

        private static void BuildLockers()
        {
            CreateLocker(new Vector3(-RoomW * 0.5f + 0.7f, 0f, -RoomD * 0.5f + 1.2f), 90f);   // 西南
            CreateLocker(new Vector3(RoomW * 0.5f - 0.7f, 0f, RoomD * 0.5f - 1.6f), -90f);    // 東北
        }

        private static void CreateLocker(Vector3 pos, float yRot)
        {
            var mat = GetMat("Locker", new Color(0.25f, 0.3f, 0.38f), 0.6f);
            var root = new GameObject("Locker");
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

            const float w = 1.0f, h = 2.3f, d = 0.9f;
            // 背面・側面・天面・腰高の前面パネル（上半分が開いていて中から外が見える）
            Box(root.transform, "Back", new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.06f), mat);
            Box(root.transform, "Left", new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(0.06f, h, d), mat);
            Box(root.transform, "Right", new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(0.06f, h, d), mat);
            Box(root.transform, "Top", new Vector3(0f, h, 0f), new Vector3(w, 0.06f, d), mat);
            Box(root.transform, "FrontPanel", new Vector3(0f, 0.65f, d * 0.5f), new Vector3(w, 1.3f, 0.06f), mat);

            // インタラクト用の当たり（前面パネル全体）＋ HidingSpot
            var spot = root.AddComponent<HidingSpot>();
            var trigger = root.AddComponent<BoxCollider>();
            trigger.center = new Vector3(0f, 1.2f, d * 0.5f + 0.1f);
            trigger.size = new Vector3(w, 2.2f, 0.3f);

            var inside = new GameObject("InsideAnchor");
            inside.transform.SetParent(root.transform, false);
            inside.transform.localPosition = new Vector3(0f, 0.1f, -0.05f);

            var exit = new GameObject("ExitAnchor");
            exit.transform.SetParent(root.transform, false);
            exit.transform.localPosition = new Vector3(0f, 0.1f, d * 0.5f + 0.8f);

            var so = new SerializedObject(spot);
            so.FindProperty("_insideAnchor").objectReferenceValue = inside.transform;
            so.FindProperty("_exitAnchor").objectReferenceValue = exit.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ============================== 家具・小物 ==============================

        private static void BuildFurniture()
        {
            var root = new GameObject("Furniture");
            var woodMat = GetMat("Wood", new Color(0.4f, 0.28f, 0.18f));

            // テーブル
            Box(root.transform, "TableTop", new Vector3(-3f, 0.8f, 3.5f), new Vector3(2.2f, 0.08f, 1.0f), woodMat);
            Box(root.transform, "TableLegs", new Vector3(-3f, 0.4f, 3.5f), new Vector3(2.0f, 0.8f, 0.8f), woodMat);

            // 人形ラック（テーブル上）
            var rack = new GameObject("DollRack");
            rack.transform.SetParent(root.transform, false);
            rack.transform.position = new Vector3(-3f, 0.85f, 3.5f);
            rack.AddComponent<DollRack>();

            // メモ帳（テーブル端）
            var memoMat = GetMat("Memo", new Color(0.9f, 0.88f, 0.75f));
            var memo = Box(root.transform, "MemoNote", new Vector3(-2.2f, 0.87f, 3.3f), new Vector3(0.28f, 0.04f, 0.36f), memoMat);
            memo.AddComponent<MemoNote>();
        }

        // ============================== モニター・時計 ==============================

        private static void BuildDisplays()
        {
            float hd = RoomD * 0.5f;

            // 監視モニター（西壁寄りの南壁）
            var monitor = new GameObject("Monitor");
            monitor.transform.position = new Vector3(-4.5f, 1.8f, -hd + 0.25f);
            monitor.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // 南壁から部屋側を向く
            monitor.AddComponent<MonitorDisplay>();

            // 壁時計（北壁＝入口の横。隠れながらでも見える位置）
            var clock = new GameObject("WallClock");
            clock.transform.position = new Vector3(4f, 2.3f, hd - 0.25f);
            clock.transform.rotation = Quaternion.identity; // 北壁から部屋側（-Z）を向く
            clock.AddComponent<WallClock>();
        }

        // ============================== 敵まわり ==============================

        private static void BuildEnemyInfrastructure()
        {
            float hd = RoomD * 0.5f;
            var root = new GameObject("EnemySystem");

            var entry = new GameObject("EntryPoint");
            entry.transform.SetParent(root.transform, false);
            entry.transform.position = new Vector3(0f, 0.05f, hd + 3f);

            var points = new Transform[4];
            Vector3[] patrolPositions =
            {
                new Vector3(0f, 0.05f, 3.5f),
                new Vector3(-5f, 0.05f, 0.5f),
                new Vector3(0f, 0.05f, -3.5f),
                new Vector3(5f, 0.05f, 0.5f),
            };
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

        // ============================== プリミティブヘルパー ==============================

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static GameObject Cylinder(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}
#endif
