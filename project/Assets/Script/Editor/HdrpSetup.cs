using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// URP で作った場面・材質・ライトを HDRP へ変換する（シーンを生成し直した後や、URP の材質を足した時に流す）。順番：
    /// 1 ConvertLights（URP のまま。HDRP に任せると明るさが既定値に戻されるので、先に付属データを付ける）
    /// 2 ConvertProbes 3 ConvertMaterials（金属・AO・滑らかさを HDRP のマスクに詰め直す）
    /// 4 Activate（HDRP の描画設定を作って切り替える）
    /// </summary>
    public static class HdrpSetup
    {
        private const string Root = "Assets/HDRP";
        private const string MaskDir = Root + "/Masks";
        private const string AssetPath = Root + "/HDRP_Asset.asset";

        public static string RunAll()
        {
            var log = new List<string>
            {
                ConvertLights(),
                ConvertProbes(),
                ConvertMaterials(),
                Activate(),
            };
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        /// <summary>
        /// シーンを生成し直した後に呼ぶ：ライト・反射プローブ・URP の値で作られた材質だけを HDRP 用に直す
        /// （描画設定の切り替えは済んでいるので Activate は呼ばない。保存は呼ぶ側）
        /// </summary>
        public static string ConvertBuilt() =>
            string.Join("／", ConvertLights(), ConvertProbes(), ConvertMaterials());

        // ============================== ライト ==============================

        public static string ConvertLights()
        {
            int scene = 0, prefab = 0;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (light.GetComponent<HDAdditionalLightData>() != null) continue;
                HdrpCompat.EnsureLightData(light);
                EditorUtility.SetDirty(light);
                EditorSceneManager.MarkSceneDirty(light.gameObject.scene);
                scene++;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || go.GetComponentInChildren<Light>(true) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                {
                    if (light.GetComponent<HDAdditionalLightData>() != null) continue;
                    HdrpCompat.EnsureLightData(light);
                    changed = true;
                    prefab++;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            return $"ライト：シーン {scene}・プレハブ {prefab}";
        }

        // ============================== 反射プローブ ==============================

        public static string ConvertProbes()
        {
            int n = 0;
            foreach (var p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!p.TryGetComponent<HDAdditionalReflectionData>(out var hd)) hd = p.gameObject.AddComponent<HDAdditionalReflectionData>();
                // URP と同じく、照明が変わった時だけスクリプトから撮り直す
                hd.mode = ProbeSettings.Mode.Realtime;
                hd.realtimeMode = ProbeSettings.RealtimeMode.OnDemand;
                var inf = hd.influenceVolume;
                inf.shape = InfluenceShape.Box;
                inf.boxSize = p.size;
                var b = Vector3.one * p.blendDistance;
                inf.boxBlendDistancePositive = b;
                inf.boxBlendDistanceNegative = b;
                EditorUtility.SetDirty(hd);
                EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
                n++;
            }
            return $"反射プローブ：{n}";
        }

        // ============================== 材質 ==============================

        public static string ConvertMaterials()
        {
            Directory.CreateDirectory(MaskDir);
            int lit = 0, masks = 0, unlit = 0;
            var pending = new List<(Material mat, string maskPath)>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat")) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!HdrpCompat.IsUrp(m)) continue;
                string maskPath = null;
                if (m.shader.name.EndsWith("/Lit"))
                {
                    lit++;
                    maskPath = BuildMask(m, guid);
                    if (maskPath != null) masks++;
                }
                else unlit++;
                pending.Add((m, maskPath));
            }
            AssetDatabase.Refresh();
            foreach (var (m, maskPath) in pending)
            {
                if (maskPath != null)
                {
                    var imp = (TextureImporter)AssetImporter.GetAtPath(maskPath);
                    imp.sRGBTexture = false;
                    imp.mipmapEnabled = true;
                    imp.SaveAndReimport();
                }
                var mask = maskPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath) : null;
                HdrpCompat.Fix(m, mask, evenIfInactive: true);
                HDShaderUtils.ResetMaterialKeywords(m);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            return $"材質：Lit {lit}（マスク {masks}）・Unlit {unlit}";
        }

        /// <summary>
        /// 金属(R)・AO(G)・細部(B=1)・滑らかさ(A) を1枚に詰める。URP の金属の図も AO の図も無ければ作らない（数値で足りる）。
        /// 図はシェーダーが読むのと同じ値（線形）で読み出す
        /// </summary>
        private static string BuildMask(Material m, string guid)
        {
            var metal = m.GetTexture("_MetallicGlossMap");
            var occ = m.GetTexture("_OcclusionMap");
            if (metal == null && occ == null) return null;
            int w = Mathf.Min(2048, Mathf.Max(metal != null ? metal.width : 1, occ != null ? occ.width : 1));
            int h = Mathf.Min(2048, Mathf.Max(metal != null ? metal.height : 1, occ != null ? occ.height : 1));
            var metalPx = metal != null ? Readback(metal, w, h) : null;
            var occPx = occ != null ? Readback(occ, w, h) : null;
            var outPx = new Color32[w * h];
            for (int i = 0; i < outPx.Length; i++)
            {
                byte r = metalPx != null ? metalPx[i].r : (byte)255;
                byte a = metalPx != null ? metalPx[i].a : (byte)255;
                byte g = occPx != null ? occPx[i].g : (byte)255;
                outPx[i] = new Color32(r, g, 255, a);
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(outPx);
            tex.Apply();
            string path = $"{MaskDir}/{m.name}_{guid.Substring(0, 8)}_mask.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return path;
        }

        private static Color32[] Readback(Texture src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            Object.DestroyImmediate(t);
            return px;
        }

        // ============================== 描画設定 ==============================

        public static string Activate()
        {
            Directory.CreateDirectory(Root);
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            var s = asset.currentPlatformRenderPipelineSettings;
            s.supportedLitShaderMode = RenderPipelineSettings.SupportedLitShaderMode.DeferredOnly;
            s.supportVolumetrics = true;
            s.supportSSR = true;
            s.supportSSGI = true;
            s.supportSSAO = true;
            s.supportMotionVectors = true;
            // デカール・半透明の裏面パス・LOD のクロスフェードは使っていない。
            // 残すと半透明の Lit（窓ガラス・レース等）の Forward がビルドで
            // "out of memory during compilation"（FXC）になり、描かれなくなる
            s.supportDecals = false;
            s.supportTransparentBackface = false;
            s.supportCustomPass = true;
            asset.currentPlatformRenderPipelineSettings = s;
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
                QualitySettings.enableLODCrossFade = false;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
            return $"描画設定：{AssetPath}（品質 {QualitySettings.names.Length} 段すべて）";
        }
    }
}
