#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// Blenderから書き出したFBXの取り込み検証（バッチ実行用）。
    /// 各モデルを一時的にインスタンス化し、Rendererバウンズ・向き・マテリアルを
    /// JSONでログ出力する。シーンには何も残さない。
    /// </summary>
    public static class ModelImportValidator
    {
        public static void Validate()
        {
            string[] models = { "OfficeDesk", "Locker", "Bed" };
            var sb = new StringBuilder();
            sb.Append("{\"models\":[");
            for (int i = 0; i < models.Length; i++)
            {
                string path = $"Assets/EscapePrototype/Models/{models[i]}.fbx";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (i > 0) sb.Append(',');
                if (prefab == null)
                {
                    sb.Append($"{{\"name\":\"{models[i]}\",\"loaded\":false}}");
                    continue;
                }
                var go = (GameObject)Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
                var renderers = go.GetComponentsInChildren<Renderer>();
                var b = new Bounds(Vector3.zero, Vector3.zero);
                bool first = true;
                var mats = new StringBuilder();
                foreach (var r in renderers)
                {
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                    foreach (var m in r.sharedMaterials)
                        if (m != null) mats.Append($"\"{m.name}\",");
                }
                string matList = mats.ToString().TrimEnd(',');
                var rootRot = prefab.transform.rotation.eulerAngles;
                var rootScale = prefab.transform.localScale;
                sb.Append($"{{\"name\":\"{models[i]}\",\"loaded\":true,");
                sb.Append($"\"size\":[{b.size.x:F3},{b.size.y:F3},{b.size.z:F3}],");
                sb.Append($"\"min\":[{b.min.x:F3},{b.min.y:F3},{b.min.z:F3}],");
                sb.Append($"\"rootRotation\":[{rootRot.x:F1},{rootRot.y:F1},{rootRot.z:F1}],");
                sb.Append($"\"rootScale\":[{rootScale.x:F3},{rootScale.y:F3},{rootScale.z:F3}],");
                sb.Append($"\"renderers\":{renderers.Length},\"materials\":[{matList}]}}");
                Object.DestroyImmediate(go);
            }
            sb.Append("]}");
            string outPath = Path.Combine(Path.GetTempPath(), "model_import_report.json");
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[ModelImportValidator] " + sb + "\nreport: " + outPath);
        }

        /// <summary>レイアウト保存の動作テスト①：ユーザーの配置変更を再現して Save Layout する</summary>
        public static void LayoutTest_MoveAndSave()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/EscapePrototype/PrototypeScene.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);
            var bed = GameObject.Find("Bed");
            if (bed != null) bed.transform.position += new Vector3(-1.5f, 0f, -2.0f);
            var desk = GameObject.Find("OfficeDesk_A");
            if (desk != null) desk.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            PrototypeSceneBuilder.SaveLayout();
            Debug.Log("[LayoutTest] Bed を (-1.5,0,-2) 移動・OfficeDesk_A を 90°回転して保存した");
        }

        /// <summary>レイアウト保存の動作テスト②：再構築後に配置が復元されたか検証する</summary>
        public static void LayoutTest_Verify()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/EscapePrototype/PrototypeScene.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);
            var bed = GameObject.Find("Bed");
            var desk = GameObject.Find("OfficeDesk_A");
            string r = "{" +
                (bed != null ? $"\"bedPos\":[{bed.transform.position.x:F2},{bed.transform.position.y:F2},{bed.transform.position.z:F2}]," : "\"bedPos\":null,") +
                (desk != null ? $"\"deskRotY\":{desk.transform.eulerAngles.y:F1}" : "\"deskRotY\":null") + "}";
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "layout_test.json"), r);
            Debug.Log("[LayoutTest] " + r);
        }

        /// <summary>
        /// 差し替え後のシーンで、対象オブジェクトのスクリーンショットを撮る（バッチ用）。
        /// 一時カメラ＋ディレクショナルライトで撮影し、シーンは保存しない。
        /// </summary>
        public static void CaptureSwapped()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/EscapePrototype/PrototypeScene.unity",
                UnityEditor.SceneManagement.OpenSceneMode.Single);

            var lightGo = new GameObject("TempLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            RenderSettings.fog = false;

            var camGo = new GameObject("TempCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.3f, 0.32f);

            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.65f);
            string outDir = Path.GetTempPath();
            // 部屋の内側の開けた位置からカメラ座標を明示指定（壁へのめり込み防止）
            Capture(cam, new Vector3(-4.5f, 1.6f, -3.6f), new Vector3(-4.5f, 1.25f, -6f), outDir + "gim_exitdoor.png");
            Capture(cam, new Vector3(2.6f, 2.4f, -3.4f), new Vector3(6f, 1.2f, 0f), outDir + "gim_stairs.png");
            Capture(cam, new Vector3(-11.4f, 1.5f, -1.6f), new Vector3(-13.8f, 1.4f, 0f), outDir + "gim_safe.png");
            Capture(cam, new Vector3(-6.0f, 1.6f, 1.6f), new Vector3(-8f, 1.25f, 0f), outDir + "gim_searcherdoor.png");
            Capture(cam, new Vector3(0.4f, 1.6f, 3.5f), new Vector3(0f, 1.25f, 6f), outDir + "gim_utilitydoor.png");
            Capture(cam, new Vector3(1.1f, 1.7f, 7.5f), new Vector3(1.5f, 1.3f, 9.65f), outDir + "gim_board.png");
            Capture(cam, new Vector3(-5.4f, 1.9f, -1.4f), new Vector3(-7.5f, 1.6f, -0.5f), outDir + "gim_clock.png");
            Capture(cam, new Vector3(5.3f, 1.7f, -7.3f), new Vector3(5.5f, 1.2f, -8.95f), outDir + "gim_pc.png");
            Capture(cam, new Vector3(-2.5f, 2.0f, -4.8f), new Vector3(1.5f, 0.8f, -1.5f), outDir + "gim_room_wide.png");
            Debug.Log("[ModelImportValidator] captures saved to " + outDir);
        }

        private static void Capture(Camera cam, Vector3 camPos, Vector3 lookAt, string file)
        {
            cam.transform.position = camPos;
            cam.transform.LookAt(lookAt);

            var rt = new RenderTexture(800, 800, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(800, 800, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 800, 800), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
#endif
