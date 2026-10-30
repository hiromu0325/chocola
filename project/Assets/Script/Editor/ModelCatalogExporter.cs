#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// 実装済みモデルの一覧（撮影画像＋数値）を書き出す。作り直しの計画・進み具合の確認用。
    /// Prefabs/Models 以下のプレハブを1つずつプレビュー用の場に置いて斜め上から撮り、
    /// 開いているシーンに置かれている数・三角形の数・大きさ・材質の数・元の FBX を catalog.json にまとめる。
    /// 仮の形（箱・カプセル）のまま動いている物（異形・残響の人影・終章のおもちゃなど）も撮る。
    /// 使い方：ModelCatalogExporter.Export("Temp/ModelCatalog")（ループ回廊のシーンを開いた状態で）
    /// </summary>
    public static class ModelCatalogExporter
    {
        private const string Root = "Assets/Prefabs/Models";

        [Serializable] private class Entry
        {
            public string id, name, group, kind, prefab, fbx, image, note;
            public int placed, tris, verts, materials, meshes;
            public float sx, sy, sz;
            public string modified;
        }
        [Serializable] private class Catalog { public string scene, exported; public List<Entry> items = new List<Entry>(); }

        public static string Export(string outDir, int size = 512)
        {
            Directory.CreateDirectory(Path.Combine(outDir, "img"));
            var placed = CountPlaced();
            var cat = new Catalog { scene = SceneManager.GetActiveScene().path, exported = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };

            var stage = EditorSceneManager.NewPreviewScene();
            var rig = BuildRig(stage, out var cam);
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;
                    string group = Path.GetFileName(Path.GetDirectoryName(path));
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stage);
                    var e = Measure(go, prefab.name, group, "model");
                    e.prefab = path;
                    e.placed = placed.TryGetValue(path, out var n) ? n : 0;
                    e.fbx = SourceFbx(go);
                    if (!string.IsNullOrEmpty(e.fbx)) e.modified = File.GetLastWriteTime(e.fbx).ToString("yyyy-MM-dd");
                    e.image = "img/" + e.id + ".jpg";
                    if (prefab.name.EndsWith("_Shell") && group != "Corridor") ShootInside(cam, go, Path.Combine(outDir, e.image), size, RoomHint(path, stage));
                    else Shoot(cam, go, Path.Combine(outDir, e.image), size, group == "Corridor" || prefab.name.EndsWith("_Interior"));
                    UnityEngine.Object.DestroyImmediate(go);
                    cat.items.Add(e);
                }

                // 仮の形のまま動いている物
                for (int t = 0; t < 3; t++)
                {
                    var go = new GameObject("Searcher" + t);
                    SceneManager.MoveGameObjectToScene(go, stage);
                    EnemySpawner.BuildVisualInto(go, (SearcherType)t);
                    AddPlaceholder(cat, cam, outDir, size, go, "Searcher_" + (SearcherType)t, "襲撃のたびに1体（3種から選ぶ）", 0);
                }
                AddFromScene(cat, cam, outDir, size, stage, "Figure", "Echo_Figure", "残響の人影（白14・黒1）");
                AddFromScene(cat, cam, outDir, size, stage, "MemoryToy", "Finale_MemoryToy", "終章のおもちゃ（13部屋に1つずつ。中身は全部同じ箱）");
                AddFromScene(cat, cam, outDir, size, stage, "CircuitTerminal", "Analysis_CircuitTerminal", "記憶回路の端末（板1枚）");
                AddFromScene(cat, cam, outDir, size, stage, "Find_summary", "Lab_Summary", "研究概要書（紙の板1枚）");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rig);
                EditorSceneManager.ClosePreviewScene(stage);
            }

            File.WriteAllText(Path.Combine(outDir, "catalog.json"), JsonUtility.ToJson(cat, true), new UTF8Encoding(false));
            return $"{cat.items.Count} items -> {outDir}";
        }

        /// <summary>開いているシーンに置かれたプレハブの数（いちばん外側のプレハブで数える）</summary>
        private static Dictionary<string, int> CountPlaced()
        {
            var counts = new Dictionary<string, int>();
            var seen = new HashSet<int>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var inst = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                    if (inst == null || !seen.Add(inst.GetInstanceID())) continue;
                    string p = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(inst);
                    counts[p] = counts.TryGetValue(p, out var c) ? c + 1 : 1;
                }
            return counts;
        }

        private static void AddPlaceholder(Catalog cat, Camera cam, string outDir, int size, GameObject go, string id, string note, int placed)
        {
            var e = Measure(go, id, "Placeholder", "placeholder");
            e.note = note;
            e.placed = placed;
            e.image = "img/" + e.id + ".jpg";
            Shoot(cam, go, Path.Combine(outDir, e.image), size, false);
            UnityEngine.Object.DestroyImmediate(go);
            cat.items.Add(e);
        }

        /// <summary>開いているシーンの同じ名前の物を数え、1つを複製して撮る</summary>
        private static void AddFromScene(Catalog cat, Camera cam, string outDir, int size, Scene stage, string objName, string id, string note)
        {
            GameObject first = null;
            int count = 0;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == objName) { count++; if (first == null) first = t.gameObject; }
            if (first == null) return;
            var go = UnityEngine.Object.Instantiate(first);
            go.SetActive(true);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            go.transform.SetPositionAndRotation(Vector3.zero, first.transform.rotation);
            SceneManager.MoveGameObjectToScene(go, stage);
            AddPlaceholder(cat, cam, outDir, size, go, id, note, count);
        }

        private static Entry Measure(GameObject go, string name, string group, string kind)
        {
            var e = new Entry { id = name, name = name, group = group, kind = kind };
            var mats = new HashSet<Material>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                e.meshes++;
                e.verts += m.vertexCount;
                for (int s = 0; s < m.subMeshCount; s++) e.tris += (int)(m.GetIndexCount(s) / 3);
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh;
                if (m == null) continue;
                e.meshes++;
                e.verts += m.vertexCount;
                for (int s = 0; s < m.subMeshCount; s++) e.tris += (int)(m.GetIndexCount(s) / 3);
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var mt in r.sharedMaterials) if (mt != null) mats.Add(mt);
            e.materials = mats.Count;
            var b = Bounds(go);
            e.sx = Round(b.size.x); e.sy = Round(b.size.y); e.sz = Round(b.size.z);
            return e;
        }

        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;

        private static string SourceFbx(GameObject go)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                string p = AssetDatabase.GetAssetPath(mf.sharedMesh);
                if (p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        private static Bounds Bounds(GameObject go)
        {
            bool any = false;
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r.GetComponent<TextMesh>() != null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // ============================== 撮影 ==============================

        private static GameObject BuildRig(Scene stage, out Camera cam)
        {
            var rig = new GameObject("CatalogRig");
            SceneManager.MoveGameObjectToScene(rig, stage);
            var camGo = new GameObject("Cam");
            camGo.transform.SetParent(rig.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.scene = stage;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.165f, 0.18f);
            cam.fieldOfView = 30f;
            cam.enabled = false;
            AddLight(rig, "Key", Quaternion.Euler(40f, -35f, 0f), new Color(1f, 0.96f, 0.9f), 1.25f);
            AddLight(rig, "Fill", Quaternion.Euler(20f, 145f, 0f), new Color(0.75f, 0.82f, 0.95f), 0.55f);
            AddLight(rig, "Top", Quaternion.Euler(85f, 0f, 0f), Color.white, 0.35f);
            return rig;
        }

        private static void AddLight(GameObject rig, string name, Quaternion rot, Color c, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(rig.transform, false);
            go.transform.rotation = rot;
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }

        /// <summary>斜め前の上から全体が収まる距離で撮る（部屋の外殻・室内一式は高めから見下ろす）</summary>
        private static void Shoot(Camera cam, GameObject go, string file, int size, bool high)
        {
            var b = Bounds(go);
            float radius = Mathf.Max(0.05f, b.extents.magnitude);
            var dir = high ? new Vector3(-0.45f, 0.85f, -0.55f).normalized : new Vector3(-0.55f, 0.42f, -0.72f).normalized;
            float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
            cam.transform.position = b.center + dir * dist;
            cam.transform.LookAt(b.center);
            cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 1.5f);
            cam.farClipPlane = dist + radius * 2f;
            Render(cam, file, size);
        }

        /// <summary>部屋の外殻：外からは天井しか見えないので、中の隅（目の高さ）から向かいの隅を広角で撮る</summary>
        private static void ShootInside(Camera cam, GameObject go, string file, int size, Bounds? hint)
        {
            var b = Bounds(go);
            if (hint.HasValue)
            {
                // 外殻には窓の外の景色の板も入っていて大きく測れるので、横の広さは室内一式（家具の置き場所）で見る
                var h = hint.Value;
                b = new Bounds(new Vector3(h.center.x, b.center.y, h.center.z), new Vector3(h.size.x, b.size.y, h.size.z));
            }
            float fov = cam.fieldOfView;
            cam.fieldOfView = 78f;
            var floor = b.center.y - b.extents.y;
            float k = hint.HasValue ? 0.82f : 0.3f;   // 目安が無い時は景色の板の分だけ外れないよう中央寄りから
            cam.transform.position = new Vector3(b.center.x - b.extents.x * k, floor + 1.55f, b.center.z - b.extents.z * k);
            cam.transform.LookAt(new Vector3(b.center.x + b.extents.x * 0.3f, floor + 1.1f, b.center.z + b.extents.z * 0.3f));
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = b.size.magnitude * 2f;
            Render(cam, file, size);
            cam.fieldOfView = fov;
        }

        /// <summary>同じ部屋の室内一式（_Interior）の広がり。無ければ null</summary>
        private static Bounds? RoomHint(string shellPath, Scene stage)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(shellPath.Replace("_Shell.prefab", "_Interior.prefab"));
            if (p == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p, stage);
            var b = Bounds(go);
            UnityEngine.Object.DestroyImmediate(go);
            return b;
        }

        private static void Render(Camera cam, string file, int size)
        {
            var desc = new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGB32, 24) { msaaSamples = 8, sRGB = true };
            var rt = RenderTexture.GetTemporary(desc);
            var plain = RenderTexture.GetTemporary(new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGB32, 0) { sRGB = true });
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            Graphics.Blit(rt, plain);
            var prev = RenderTexture.active;
            RenderTexture.active = plain;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(file, tex.EncodeToJPG(88));
            UnityEngine.Object.DestroyImmediate(tex);
            RenderTexture.ReleaseTemporary(rt);
            RenderTexture.ReleaseTemporary(plain);
        }
    }
}
#endif
