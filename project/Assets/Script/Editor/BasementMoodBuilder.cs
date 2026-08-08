using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EscapeProto
{
    /// <summary>
    /// 参考画像（コンクリ打ちっぱなしの薄暗い地下室）の雰囲気を、Fabから導入した
    /// 無料アセットで再現するムードシーンのビルダー。
    ///
    /// 使い方（Tools > EscapePrototype > Basement Mood）:
    ///   1. 「マテリアルをURPへ変換」… 導入アセットの Standard マテリアルを URP/Lit に一括変換
    ///      （紫色になるのを直す。プロジェクト全体に安全に適用される）
    ///   2. 「ムードシーンを生成」… Assets/EscapePrototype/BasementMood.unity を生成して開く
    ///
    /// 使用アセット: Modular Pipeline Pack / Electrical Substation / Wood Box Free /
    ///               Fans For Metro / Construction Site VOL.1&2 / Free PBR Lamps Pack
    /// </summary>
    public static class BasementMoodBuilder
    {
        private const string ScenePath = "Assets/EscapePrototype/BasementMood.unity";
        private const string MatDir = "Assets/EscapePrototype/MoodMaterials";
        private const string PostProfilePath = "Assets/EscapePrototype/BasementMoodPost.asset";

        // 部屋の寸法（画像に合わせ、低い天井の横長地下室）
        private const float W = 10f, D = 14f, H = 3.0f;

        // ============================== メニュー1: URP変換 ==============================

        [MenuItem("Tools/EscapePrototype/Basement Mood/1. マテリアルをURPへ変換")]
        public static void ConvertMaterialsToUrp()
        {
            // URP付属のコンバーター(Converters.RunInBatchMode)はURP17.3にバグがあり
            // MissingMethodExceptionで落ちるため、Standard→URP/Litの移植を自前で行う。
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) { Debug.LogError("[BasementMood] URP/Lit シェーダーが見つかりません"); return; }

            int converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                string s = mat.shader.name;
                if (s == "SH_AW_PBR_ORM")   // Abandoned World製カスタムシェーダー（ビルトイン用）
                {
                    UpgradeAbandonedWorldToUrpLit(mat, urpLit);
                    converted++;
                    continue;
                }
                if (s != "Standard" && s != "Standard (Specular setup)" &&
                    s != "Autodesk Interactive" && !s.StartsWith("Legacy Shaders/")) continue;

                UpgradeStandardToUrpLit(mat, urpLit);
                converted++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[BasementMood] マテリアル変換が完了しました（{converted}件をURP/Litへ移植）。");
        }

        /// <summary>Abandoned World の SH_AW_PBR_ORM をURP/Litへ移植する。
        /// ORMテクスチャ(R=AO,G=Roughness,B=Metallic)はURPのチャンネル構成と互換が無いため
        /// スカラー値で近似する。</summary>
        private static void UpgradeAbandonedWorldToUrpLit(Material mat, Shader urpLit)
        {
            Texture baseTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Color baseColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Texture normal = mat.HasProperty("_Normal") ? mat.GetTexture("_Normal") : null;
            float rough = mat.HasProperty("_Roughness") ? mat.GetFloat("_Roughness") : 1f;   // 0..2
            float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;  // 0..2

            mat.shader = urpLit;
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Smoothness", Mathf.Clamp01(1f - rough * 0.5f));
            mat.SetFloat("_Metallic", Mathf.Clamp01(metallic * 0.5f));
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(mat);
        }

        /// <summary>Standard系マテリアルのプロパティを退避し、URP/Litに割り当て直す</summary>
        private static void UpgradeStandardToUrpLit(Material mat, Shader urpLit)
        {
            // ---- 旧プロパティを退避 ----
            Texture baseTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Vector2 texScale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 texOffset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
            Color baseColor = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Texture bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            float bumpScale = mat.HasProperty("_BumpScale") ? mat.GetFloat("_BumpScale") : 1f;
            Texture metalMap = mat.HasProperty("_MetallicGlossMap") ? mat.GetTexture("_MetallicGlossMap") : null;
            float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
            // Standardはマップ有無で滑らかさの参照先が変わる（_GlossMapScale / _Glossiness）
            float smoothness = metalMap != null
                ? (mat.HasProperty("_GlossMapScale") ? mat.GetFloat("_GlossMapScale") : 0.5f)
                : (mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f);
            Texture occMap = mat.HasProperty("_OcclusionMap") ? mat.GetTexture("_OcclusionMap") : null;
            Texture emMap = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
            Color emColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
            bool emission = mat.IsKeywordEnabled("_EMISSION");
            float mode = mat.HasProperty("_Mode") ? mat.GetFloat("_Mode") : 0f;   // 0:Opaque 1:Cutout 2:Fade 3:Transparent
            float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;

            // ---- URP/Litへ差し替え ----
            mat.shader = urpLit;
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetTextureScale("_BaseMap", texScale);
            mat.SetTextureOffset("_BaseMap", texOffset);
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (bumpMap != null)
            {
                mat.SetTexture("_BumpMap", bumpMap);
                mat.SetFloat("_BumpScale", bumpScale);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (metalMap != null)
            {
                mat.SetTexture("_MetallicGlossMap", metalMap);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (occMap != null)
            {
                mat.SetTexture("_OcclusionMap", occMap);
                mat.EnableKeyword("_OCCLUSIONMAP");
            }
            if (emission && (emMap != null || emColor.maxColorComponent > 0f))
            {
                mat.SetTexture("_EmissionMap", emMap);
                mat.SetColor("_EmissionColor", emColor);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            // ---- 透過モードの移植 ----
            if (mode >= 2.5f || (mode >= 1.5f && mode < 2.5f))        // Transparent / Fade
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", mode >= 2.5f ? 1f : 0f);   // Transparent=Premultiply / Fade=Alpha
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else if (mode >= 0.5f)                                     // Cutout
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", cutoff);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }

            EditorUtility.SetDirty(mat);
        }

        // ============================== メニュー2: シーン生成 ==============================

        [MenuItem("Tools/EscapePrototype/Basement Mood/2. ムードシーンを生成")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder("Assets/EscapePrototype", "MoodMaterials");

            BuildRoom();
            BuildPipes();
            BuildProps();
            BuildStairs();
            BuildCorridor();
            BuildBoilerRoom();
            BuildStorageRoom();
            BuildLighting();
            BuildPostProcess();
            BuildCamera();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[BasementMood] シーンを生成しました: {ScenePath}\n" +
                      "紫色のオブジェクトがある場合はメニュー1のURP変換を実行してください。");
        }

        // ============================== 部屋（コンクリ躯体） ==============================

        private static void BuildRoom()
        {
            var root = new GameObject("Room");
            float hw = W * 0.5f, hd = D * 0.5f;

            // フラットなグレーのコンクリ（画像の打ちっぱなし躯体。緑に寄せすぎない）
            var wallMat = ConcreteMat("BM_Wall", new Color(0.50f, 0.52f, 0.51f), new Vector2(2.5f, 1f));
            var floorMat = ConcreteMat("BM_Floor", new Color(0.38f, 0.40f, 0.40f), new Vector2(3f, 4f));
            var ceilMat = ConcreteMat("BM_Ceiling", new Color(0.33f, 0.35f, 0.35f), new Vector2(3f, 4f));

            Box(root.transform, "Floor", new Vector3(0f, -0.05f, 0f), new Vector3(W, 0.1f, D), floorMat);
            Box(root.transform, "Ceiling", new Vector3(0f, H + 0.05f, 0f), new Vector3(W, 0.1f, D), ceilMat);
            // 北壁は扉の開口（x 1.05〜2.25）を空けて廊下に繋げる
            Box(root.transform, "Wall_N_L", new Vector3(-1.975f, H * 0.5f, hd), new Vector3(6.05f, H, 0.15f), wallMat);
            Box(root.transform, "Wall_N_R", new Vector3(3.625f, H * 0.5f, hd), new Vector3(2.75f, H, 0.15f), wallMat);
            Box(root.transform, "Wall_N_Lintel", new Vector3(1.65f, 2.65f, hd), new Vector3(1.25f, 0.7f, 0.15f), wallMat);
            Box(root.transform, "Wall_S", new Vector3(0f, H * 0.5f, -hd), new Vector3(W, H, 0.15f), wallMat);
            Box(root.transform, "Wall_W", new Vector3(-hw, H * 0.5f, 0f), new Vector3(0.15f, H, D), wallMat);
            Box(root.transform, "Wall_E", new Vector3(hw, H * 0.5f, 0f), new Vector3(0.15f, H, D), wallMat);

            // 天井の下がり梁（画像左上のスラブ段差の再現）
            Box(root.transform, "Beam_W", new Vector3(-hw + 1.6f, H - 0.18f, 0f), new Vector3(3.2f, 0.36f, D), ceilMat);
            Box(root.transform, "Beam_N", new Vector3(0f, H - 0.18f, hd - 1.0f), new Vector3(W, 0.36f, 2.0f), ceilMat);

            // コンクリパネルの目地（縦線）で壁の単調さを消す
            SeamsX(root.transform, hd - 0.09f, -4f, 5f, 2.2f);
            SeamsZ(root.transform, -hw + 0.09f, -5.5f, 6f, 2.4f);
            SeamsZ(root.transform, hw - 0.09f, -5.5f, 6f, 2.4f);

            // 床のひび割れ（細い黒ずみ線）
            var crackMat = GetMat("BM_Crack", new Color(0.22f, 0.24f, 0.23f), 0.05f);
            Crack(root.transform, new Vector3(0.6f, 0f, -1.5f), 4.5f, 28f, crackMat);
            Crack(root.transform, new Vector3(-1.8f, 0f, 2.6f), 3.0f, -55f, crackMat);
            Crack(root.transform, new Vector3(2.4f, 0f, 3.8f), 2.2f, 75f, crackMat);

            // 正面（北）の鉄扉（開口に収める）
            DoorUnit(root.transform, new Vector3(1.65f, 0f, hd), 0f);
        }

        /// <summary>グレーブルーの鉄扉＋枠＋小窓。openingCenter は床上の扉中心</summary>
        private static void DoorUnit(Transform parent, Vector3 pos, float yRot)
        {
            var doorMat = GetMat("BM_DoorMetal", new Color(0.42f, 0.47f, 0.52f), 0.42f);
            var frameMat = GetMat("BM_DoorFrame", new Color(0.30f, 0.33f, 0.36f), 0.35f);
            var winMat = GetMat("BM_DoorWindow", new Color(0.75f, 0.78f, 0.72f), 0.7f);

            var unit = new GameObject("DoorUnit");
            unit.transform.SetParent(parent, false);
            unit.transform.localPosition = pos;
            unit.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            var t = unit.transform;

            Box(t, "Door", new Vector3(0f, 1.08f, 0f), new Vector3(0.96f, 2.16f, 0.07f), doorMat);
            Box(t, "Jamb_L", new Vector3(-0.545f, 1.13f, 0f), new Vector3(0.09f, 2.26f, 0.12f), frameMat);
            Box(t, "Jamb_R", new Vector3(0.545f, 1.13f, 0f), new Vector3(0.09f, 2.26f, 0.12f), frameMat);
            Box(t, "Lintel", new Vector3(0f, 2.3f, 0f), new Vector3(1.18f, 0.1f, 0.12f), frameMat);
            Box(t, "Window", new Vector3(0.12f, 1.72f, -0.045f), new Vector3(0.30f, 0.34f, 0.012f), winMat);
            Box(t, "Kick", new Vector3(0f, 0.14f, -0.042f), new Vector3(0.9f, 0.28f, 0.012f), frameMat);
            Box(t, "Knob", new Vector3(-0.35f, 1.05f, -0.06f), new Vector3(0.05f, 0.14f, 0.05f), frameMat);
        }

        /// <summary>Z方向の壁（固定x）に縦目地を並べる</summary>
        private static void SeamsZ(Transform parent, float x, float zFrom, float zTo, float step)
        {
            var m = GetMat("BM_Seam", new Color(0.40f, 0.42f, 0.42f), 0.05f);
            for (float z = zFrom; z <= zTo; z += step)
                Box(parent, "Seam", new Vector3(x, H * 0.47f, z), new Vector3(0.05f, H * 0.94f, 0.03f), m);
        }

        /// <summary>X方向の壁（固定z）に縦目地を並べる</summary>
        private static void SeamsX(Transform parent, float z, float xFrom, float xTo, float step)
        {
            var m = GetMat("BM_Seam", new Color(0.40f, 0.42f, 0.42f), 0.05f);
            for (float x = xFrom; x <= xTo; x += step)
                Box(parent, "Seam", new Vector3(x, H * 0.47f, z), new Vector3(0.03f, H * 0.94f, 0.05f), m);
        }

        /// <summary>床のひび（細長い黒ずみ）</summary>
        private static void Crack(Transform parent, Vector3 pos, float length, float yRot, Material mat)
        {
            var c = Box(parent, "Crack", new Vector3(pos.x, 0.006f, pos.z), new Vector3(0.05f, 0.01f, length), mat);
            c.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
        }

        /// <summary>コンクリ風マテリアル。Drywallのアルベドは段ボール色で雰囲気を壊すため
        /// 使わず、フラットなグレー緑＋ノーマルマップの荒れだけで表現する。</summary>
        private static Material ConcreteMat(string name, Color tint, Vector2 tiling)
        {
            var mat = GetMat(name, tint, 0.12f);
            mat.SetTexture("_BaseMap", null);
            var nrm = LoadAsNormalMap(
                "Assets/Construction_Package/Construction_Vol01/Textures/TX_Drywall_01a_NRM.tga");
            if (nrm != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", nrm);
                mat.SetFloat("_BumpScale", 0.6f);
                mat.SetTextureScale("_BaseMap", tiling);
                mat.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>テクスチャをNormalMapとしてインポートし直してから読み込む</summary>
        private static Texture2D LoadAsNormalMap(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && imp.textureType != TextureImporterType.NormalMap)
            {
                imp.textureType = TextureImporterType.NormalMap;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ============================== 天井配管 ==============================

        private static void BuildPipes()
        {
            var root = new GameObject("Pipes");
            var pipeMat = GetMat("BM_Pipe", new Color(0.45f, 0.44f, 0.40f), 0.35f);
            var pipeMat2 = GetMat("BM_Pipe2", new Color(0.35f, 0.36f, 0.38f), 0.4f);

            // 長い配管はシリンダーで確実に通し、パック部品（バルブ・メーター）を要所に添える
            PipeRun(root.transform, new Vector3(-1.2f, H - 0.25f, 0f), D - 0.4f, 0.09f, pipeMat);
            PipeRun(root.transform, new Vector3(-0.9f, H - 0.45f, 0f), D - 0.4f, 0.06f, pipeMat2);
            PipeRun(root.transform, new Vector3(2.3f, H - 0.30f, 0f), D - 0.4f, 0.07f, pipeMat);
            // 左壁沿いの縦管（画像左の壁を降りる管）
            var v = Cylinder(root.transform, "PipeV", new Vector3(-W * 0.5f + 0.25f, H * 0.5f, 2.5f),
                new Vector3(0.14f, H * 0.5f, 0.14f), pipeMat);
            v.transform.localRotation = Quaternion.identity;

            // パックのバルブ・圧力計をアクセントに（見つからなければスキップ）
            Place("Assets/3D Models/Props/Industrial/Pipeline/Update_1.03/Prefabs/Medium/M_PipeValve_White_01.prefab",
                root.transform, new Vector3(-1.2f, H - 0.55f, -2.0f), 0f, 0.35f);
            Place("Assets/3D Models/Props/Industrial/Pipeline/Update_1.03/Prefabs/Manometr.prefab",
                root.transform, new Vector3(-1.2f, H - 0.6f, -1.4f), 0f, 0.2f);
        }

        /// <summary>Z方向に部屋を貫く水平パイプ</summary>
        private static void PipeRun(Transform parent, Vector3 center, float length, float radius, Material mat)
        {
            var go = Cylinder(parent, "PipeRun", center, new Vector3(radius * 2f, length * 0.5f, radius * 2f), mat);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // ============================== プロップ ==============================

        private static void BuildProps()
        {
            var root = new GameObject("Props");
            float hw = W * 0.5f, hd = D * 0.5f;

            // 左壁: 青緑の配電盤キャビネット（画像左の青い機械）＋壁の換気シャッター
            Place("Assets/Electrical Substation/Prefabs/SM_Electrical_Substation.prefab",
                root.transform, new Vector3(-hw + 0.75f, 0f, -3.6f), 90f, 1.9f);
            Place("Assets/Abandoned World/Fan 2/Assets/Prefabs/fan2_shutter.prefab",
                root.transform, new Vector3(-hw + 0.15f, 2.2f, -0.5f), 90f, 0.8f);

            // 左中央: 作業台まわり（画像の机・箱・バケツ）
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_PlywoodTable_01a.prefab",
                root.transform, new Vector3(-2.6f, 0f, 1.4f), 5f, 0.9f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_CardboardBox_2.prefab",
                root.transform, new Vector3(-2.9f, 0.9f, 1.6f), 20f, 0.35f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_Lunchbox_01a.prefab",
                root.transform, new Vector3(-2.3f, 0.9f, 1.2f), 70f, 0.18f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_WoodBox_1.prefab",
                root.transform, new Vector3(-2.5f, 0f, 2.6f), 10f, 0.55f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_PaintBucket_01a.prefab",
                root.transform, new Vector3(-1.7f, 0f, 2.3f), 0f, 0.45f);
            // 赤いガス缶を消火器の代わりに正面壁際へ
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_GasCan_01a.prefab",
                root.transform, new Vector3(-0.6f, 0f, hd - 0.45f), 15f, 0.5f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_TallGasCanister_01a.prefab",
                root.transform, new Vector3(-1.15f, 0f, hd - 0.5f), 0f, 1.0f);
        }

        // ============================== 右側の階段 ==============================

        private static void BuildStairs()
        {
            var root = new GameObject("Stairs");
            var mat = ConcreteMat("BM_Stairs", new Color(0.50f, 0.52f, 0.49f), new Vector2(1f, 1f));
            float hw = W * 0.5f;

            // 右壁沿いを奥(+Z)が上・手前(-Z)が下り口になる10段＋踊り場（画像右側の階段）
            const int steps = 10;
            const float rise = 0.19f, run = 0.30f, width = 1.4f;
            float zTop = 1.6f;   // 踊り場側（奥）
            for (int i = 0; i < steps; i++)
            {
                float h = rise * (steps - i);
                Box(root.transform, $"Step_{i}",
                    new Vector3(hw - width * 0.5f - 0.075f, h - rise * 0.5f, zTop - 1 * (run * i) - run * 0.5f),
                    new Vector3(width, rise, run), mat);
            }
            // 踊り場と、低めのパラペット（高くすると段が隠れる）
            Box(root.transform, "Landing",
                new Vector3(hw - width * 0.5f - 0.075f, rise * steps + 0.05f, zTop + 0.6f),
                new Vector3(width, 0.1f, 1.2f), mat);
            Box(root.transform, "Parapet",
                new Vector3(hw - width - 0.2f, 0.45f, zTop - run * steps * 0.5f),
                new Vector3(0.18f, 0.9f, run * steps + 0.4f), mat);
        }

        // ============================== 追加の部屋（同テイスト） ==============================

        /// <summary>廊下：メインルーム北扉の先（x 0.1〜3.1, z 7〜15）。突き当たりに施錠扉、
        /// 左右の壁に物置・ボイラー室への開口を持つ</summary>
        private static void BuildCorridor()
        {
            var root = new GameObject("Corridor");
            var t = root.transform;
            var wallMat = ConcreteMat("BM_Wall", new Color(0.50f, 0.52f, 0.51f), new Vector2(2.5f, 1f));
            var floorMat = ConcreteMat("BM_Floor", new Color(0.38f, 0.40f, 0.40f), new Vector2(3f, 4f));
            var ceilMat = ConcreteMat("BM_Ceiling", new Color(0.33f, 0.35f, 0.35f), new Vector2(3f, 4f));
            var pipeMat = GetMat("BM_Pipe", new Color(0.45f, 0.44f, 0.40f), 0.35f);

            Box(t, "Floor", new Vector3(1.6f, -0.05f, 11f), new Vector3(3f, 0.1f, 8f), floorMat);
            Box(t, "Ceiling", new Vector3(1.6f, H + 0.05f, 11f), new Vector3(3f, 0.1f, 8f), ceilMat);

            // 西壁（物置への開口 z 12.2〜13.4）／東壁（ボイラー室への開口）— 隣室と共有
            foreach (float x in new[] { 0.1f, 3.1f })
            {
                string side = x < 1.6f ? "W" : "E";
                Box(t, $"Wall_{side}_A", new Vector3(x, H * 0.5f, 9.6f), new Vector3(0.15f, H, 5.2f), wallMat);
                Box(t, $"Wall_{side}_B", new Vector3(x, H * 0.5f, 14.2f), new Vector3(0.15f, H, 1.6f), wallMat);
                Box(t, $"Wall_{side}_Lintel", new Vector3(x, 2.65f, 12.8f), new Vector3(0.15f, 0.7f, 1.2f), wallMat);
            }
            // 突き当たり（北）
            Box(t, "Wall_End", new Vector3(1.6f, H * 0.5f, 15f), new Vector3(3f, H, 0.15f), wallMat);
            DoorUnit(t, new Vector3(1.6f, 0f, 14.9f), 180f);

            // 目地は開口部(z 12.2〜13.4)に浮かないよう手前側だけに
            SeamsZ(t, 0.19f, 8f, 11.5f, 2.4f);
            SeamsZ(t, 3.01f, 8f, 11.5f, 2.4f);

            // 天井配管が廊下の奥へ続いていく
            PipeRun(t, new Vector3(1.05f, H - 0.25f, 11f), 7.6f, 0.09f, pipeMat);
            PipeRun(t, new Vector3(1.35f, H - 0.45f, 11f), 7.6f, 0.06f, pipeMat);

            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_CardboardBox_4.prefab",
                t, new Vector3(2.7f, 0f, 8.3f), 25f, 0.45f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_PaintBucket_02a.prefab",
                t, new Vector3(0.45f, 0f, 10.4f), 0f, 0.35f);

            // 狭い空間なので暗め（ホラー廊下の圧を出す）
            PendantLight(t, new Vector3(1.6f, 0f, 9.6f), 1.5f);
            PendantLight(t, new Vector3(1.6f, 0f, 13.6f), 1.1f);
        }

        /// <summary>ボイラー室（配電室）：廊下の東（x 3.1〜9.1, z 11〜15）。
        /// 配電盤キャビネット・ガスタンク・配管クラスタ＋赤いアクセント灯</summary>
        private static void BuildBoilerRoom()
        {
            var root = new GameObject("BoilerRoom");
            var t = root.transform;
            var wallMat = ConcreteMat("BM_Wall", new Color(0.50f, 0.52f, 0.51f), new Vector2(2.5f, 1f));
            var floorMat = ConcreteMat("BM_Floor", new Color(0.38f, 0.40f, 0.40f), new Vector2(3f, 4f));
            var ceilMat = ConcreteMat("BM_Ceiling", new Color(0.33f, 0.35f, 0.35f), new Vector2(3f, 4f));
            var pipeMat = GetMat("BM_Pipe", new Color(0.45f, 0.44f, 0.40f), 0.35f);
            var pipeMat2 = GetMat("BM_Pipe2", new Color(0.35f, 0.36f, 0.38f), 0.4f);

            Box(t, "Floor", new Vector3(6.1f, -0.05f, 13f), new Vector3(6f, 0.1f, 4f), floorMat);
            Box(t, "Ceiling", new Vector3(6.1f, H + 0.05f, 13f), new Vector3(6f, 0.1f, 4f), ceilMat);
            Box(t, "Wall_N", new Vector3(6.1f, H * 0.5f, 15f), new Vector3(6f, H, 0.15f), wallMat);
            Box(t, "Wall_S", new Vector3(6.1f, H * 0.5f, 11f), new Vector3(6f, H, 0.15f), wallMat);
            Box(t, "Wall_E", new Vector3(9.1f, H * 0.5f, 13f), new Vector3(0.15f, H, 4f), wallMat);
            SeamsX(t, 14.91f, 4f, 8.8f, 2.2f);

            // 機械類：配電キャビネット＋ガス・燃料タンク＋作業台
            Place("Assets/Electrical Substation/Prefabs/SM_Electrical_Substation.prefab",
                t, new Vector3(8.25f, 0f, 13.2f), -90f, 1.9f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_propanetank_01a.prefab",
                t, new Vector3(4.6f, 0f, 14.35f), 10f, 1.1f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_propanetank_02a.prefab",
                t, new Vector3(5.3f, 0f, 14.45f), -15f, 0.9f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_TallGasCanister_01a.prefab",
                t, new Vector3(7.9f, 0f, 11.55f), 0f, 1.0f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_PlywoodTable_01a.prefab",
                t, new Vector3(4.3f, 0f, 11.8f), 90f, 0.9f);
            Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_Lunchbox_01a.prefab",
                t, new Vector3(4.3f, 0.9f, 11.8f), 40f, 0.18f);
            Place("Assets/Abandoned World/Fan 2/Assets/Prefabs/fan2_shutter.prefab",
                t, new Vector3(6.9f, 2.2f, 14.85f), 180f, 0.8f);

            // 天井をX方向に走る太管＋バルブ・圧力計のクラスタ
            var run = Cylinder(t, "PipeRunX", new Vector3(6.1f, H - 0.3f, 14.5f), new Vector3(0.18f, 2.8f, 0.18f), pipeMat);
            run.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var run2 = Cylinder(t, "PipeRunX2", new Vector3(6.1f, H - 0.55f, 14.7f), new Vector3(0.12f, 2.8f, 0.12f), pipeMat2);
            run2.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Cylinder(t, "PipeV", new Vector3(8.6f, H * 0.5f, 14.6f), new Vector3(0.14f, H * 0.5f, 0.14f), pipeMat);
            Place("Assets/3D Models/Props/Industrial/Pipeline/Update_1.03/Prefabs/Medium/M_PipeValve_White_01.prefab",
                t, new Vector3(5.2f, H - 0.75f, 14.5f), 0f, 0.35f);
            Place("Assets/3D Models/Props/Industrial/Pipeline/Update_1.03/Prefabs/Manometr.prefab",
                t, new Vector3(5.8f, H - 0.8f, 14.45f), 0f, 0.2f);

            PendantLight(t, new Vector3(6.1f, 0f, 13f), 3.0f);
            // 機械横の赤いアクセント灯（警告ランプの気配）
            var warn = new GameObject("WarnLight");
            warn.transform.SetParent(t, false);
            warn.transform.position = new Vector3(7.95f, 1.7f, 13.2f);
            var wl = warn.AddComponent<Light>();
            wl.type = LightType.Point;
            wl.color = new Color(1f, 0.22f, 0.12f);
            wl.intensity = 0.9f;   // 天井まで赤く染めない程度のアクセント
            wl.range = 2.4f;
        }

        /// <summary>物置：廊下の西（x -4.9〜0.1, z 11〜15）。木箱・段ボール・梯子。
        /// 照明は暗めの1灯だけにして圧迫感を出す</summary>
        private static void BuildStorageRoom()
        {
            var root = new GameObject("StorageRoom");
            var t = root.transform;
            var wallMat = ConcreteMat("BM_Wall", new Color(0.50f, 0.52f, 0.51f), new Vector2(2.5f, 1f));
            var floorMat = ConcreteMat("BM_Floor", new Color(0.38f, 0.40f, 0.40f), new Vector2(3f, 4f));
            var ceilMat = ConcreteMat("BM_Ceiling", new Color(0.33f, 0.35f, 0.35f), new Vector2(3f, 4f));

            Box(t, "Floor", new Vector3(-2.4f, -0.05f, 13f), new Vector3(5f, 0.1f, 4f), floorMat);
            Box(t, "Ceiling", new Vector3(-2.4f, H + 0.05f, 13f), new Vector3(5f, 0.1f, 4f), ceilMat);
            Box(t, "Wall_N", new Vector3(-2.4f, H * 0.5f, 15f), new Vector3(5f, H, 0.15f), wallMat);
            Box(t, "Wall_S", new Vector3(-2.4f, H * 0.5f, 11f), new Vector3(5f, H, 0.15f), wallMat);
            Box(t, "Wall_W", new Vector3(-4.9f, H * 0.5f, 13f), new Vector3(0.15f, H, 4f), wallMat);
            SeamsX(t, 14.91f, -4.4f, -0.6f, 2.2f);

            // 木箱の山と生活の痕跡
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_WoodBox_1.prefab",
                t, new Vector3(-4.0f, 0f, 14.15f), 8f, 0.55f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_WoodBox_3.prefab",
                t, new Vector3(-4.0f, 0.56f, 14.15f), -12f, 0.45f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_WoodBox_5.prefab",
                t, new Vector3(-3.1f, 0f, 14.3f), 30f, 0.5f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_CardboardBox_1.prefab",
                t, new Vector3(-1.7f, 0f, 14.25f), 15f, 0.4f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_CardboardBox_3.prefab",
                t, new Vector3(-1.15f, 0f, 14.1f), -25f, 0.35f);
            Place("Assets/Abandoned World/Wood Box Free/Prefabs/SM_Coil.prefab",
                t, new Vector3(-0.9f, 0f, 12.0f), 0f, 0.5f);
            var ladder = Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_Ladder_01a.prefab",
                t, new Vector3(-4.5f, 0f, 12.3f), 90f, 1.9f);
            if (ladder != null) ladder.transform.localRotation = Quaternion.Euler(0f, 90f, -8f);   // 壁に立てかける
            var ply = Place("Assets/Construction_Package/Construction_Vol02/Prefabs/SM_Plywood_02a.prefab",
                t, new Vector3(-4.62f, 0f, 13.4f), 90f, 1.5f);
            if (ply != null) ply.transform.localRotation = Quaternion.Euler(-12f, 90f, 0f);       // 板を壁に立てかける

            // 暗めの1灯（Small_roof_lamp）
            var lamp = Place("Assets/New Solution Studio/PBR Lamps Pack/Prefabs/Small_roof_lamp.prefab",
                t, new Vector3(-2.4f, H - 0.35f, 13f), 0f, 0.3f);
            var go = new GameObject("StoragePoint");
            go.transform.SetParent(lamp != null ? lamp.transform : t, true);
            go.transform.position = new Vector3(-2.4f, H - 0.55f, 13f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.88f, 0.7f);   // 物置はより古い電球色
            l.intensity = 1.8f;
            l.range = 7f;
            l.shadows = LightShadows.Soft;
        }

        // ============================== ライティング ==============================

        private static void BuildLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.030f, 0.034f, 0.036f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.045f, 0.055f, 0.055f);
            RenderSettings.fogDensity = 0.05f;

            var root = new GameObject("Lights");

            // 画像のペンダントライト2灯（吊り傘付き）＋ 温白色ポイントライト
            PendantLight(root.transform, new Vector3(0.2f, 0f, -2.2f), 3.2f);
            PendantLight(root.transform, new Vector3(0.0f, 0f, 2.4f), 2.6f);

            // 扉上のスポット（画像の壁に落ちる光だまり）
            var spotGo = new GameObject("DoorSpot");
            spotGo.transform.SetParent(root.transform, false);
            spotGo.transform.position = new Vector3(0.6f, H - 0.15f, D * 0.5f - 1.4f);
            spotGo.transform.rotation = Quaternion.Euler(64f, 180f, 0f);
            var spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.spotAngle = 100f;
            spot.color = new Color(1f, 0.95f, 0.85f);
            spot.intensity = 9f;   // 画像の「壁に落ちる光だまり」をはっきり出す
            spot.range = 8f;
            spot.shadows = LightShadows.Soft;
        }

        private static void PendantLight(Transform parent, Vector3 floorPos, float intensity)
        {
            // 傘付きランプのプレハブを天井から吊るす（無ければライトのみ）
            var lamp = Place("Assets/New Solution Studio/PBR Lamps Pack/Prefabs/Large_round_lamp.prefab",
                parent, new Vector3(floorPos.x, H - 0.55f, floorPos.z), 0f, 0.5f);

            // 発光する電球（ランプの傘が真っ黒に見えるのを防ぐ）
            var bulbMat = GetMat("BM_Bulb", Color.white, 0.1f);
            bulbMat.EnableKeyword("_EMISSION");
            bulbMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            bulbMat.SetColor("_EmissionColor", new Color(1f, 0.92f, 0.75f) * 4f);
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb";
            bulb.transform.SetParent(parent, false);
            bulb.transform.position = new Vector3(floorPos.x, H - 0.72f, floorPos.z);
            bulb.transform.localScale = Vector3.one * 0.13f;
            bulb.GetComponent<Renderer>().sharedMaterial = bulbMat;
            Object.DestroyImmediate(bulb.GetComponent<Collider>());
            if (lamp != null) bulb.transform.SetParent(lamp.transform, true);

            var go = new GameObject("PendantPoint");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(floorPos.x, H - 0.75f, floorPos.z);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.93f, 0.80f);   // 温白色
            l.intensity = intensity;
            l.range = 9f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.9f;
            if (lamp != null) go.transform.SetParent(lamp.transform, true);
        }

        // ============================== ポストプロセス ==============================

        private static void BuildPostProcess()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfilePath);

            var color = profile.Add<ColorAdjustments>();
            color.postExposure.Override(0.25f);
            color.saturation.Override(-22f);
            color.colorFilter.Override(new Color(0.87f, 0.94f, 0.90f));   // 青緑に寄せる

            var lift = profile.Add<LiftGammaGain>();
            lift.lift.Override(new Vector4(0.96f, 1.0f, 1.0f, -0.02f));   // 影を沈めつつ緑青へ
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

        // ============================== カメラ ==============================

        private static void BuildCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            go.transform.position = new Vector3(-1.3f, 1.5f, -5.9f);
            go.transform.rotation = Quaternion.Euler(2.5f, 9f, 0f);   // 右の階段が入るよう少し右へ振る
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        // ============================== 共通ヘルパー ==============================

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

        private static Material GetMat(string name, Color color, float smoothness = 0.2f)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// プレハブを配置する。targetHeight を指定すると、レンダラー境界から高さを
        /// 合わせてスケールし、足元が床（pos.y）に接地するようYを補正する。
        /// アセット未導入で見つからない場合は警告だけ出して null。
        /// </summary>
        private static GameObject Place(string path, Transform parent, Vector3 pos,
                                        float yRot = 0f, float targetHeight = -1f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[BasementMood] プレハブ未検出（スキップ）: {path}");
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
                    float s = targetHeight / b.size.y;
                    go.transform.localScale = go.transform.localScale * s;
                    b = RendererBounds(go);
                    // 足元を床(pos.y)へ接地
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

        /// <summary>EscapePrototype/Models のFBXを配置（PrototypeSceneBuilderと同じ規約）</summary>
        private static GameObject PlaceModel(Transform parent, string modelName, Vector3 pos, float yRot = 0f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/EscapePrototype/Models/HQ/{modelName}.fbx");
            if (prefab == null)
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/EscapePrototype/Models/{modelName}.fbx");
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
            return go;
        }
    }
}
