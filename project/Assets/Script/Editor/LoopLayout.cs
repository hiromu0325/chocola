using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// 回廊のシーン（LoopPrototypeBuilder が作る）で、手で動かした物の位置の記録。
    /// FloorPlaceTool で置いた物を Assets/EscapePrototype/Layout/loop_layout.json に残し、シーンを生成し直した時に置き直す。
    /// 物はシーンの上からの道のり（同じ名前が並ぶ所は何番目か）で見分け、位置と向きは親から見た値で持つ
    /// （部屋の置き場所が変わっても、部屋の中での位置は変わらない）。
    /// プレイ中に動かした物は、プレイを止めた後のシーンにも同じ位置を当てる
    /// </summary>
    public static class LoopLayout
    {
        public const string FilePath = "Assets/EscapePrototype/Layout/loop_layout.json";
        private const string PlayMovedKey = "EscapeProto.LoopLayout.MovedInPlay";

        [System.Serializable] private class Item { public string id; public Vector3 pos; public Vector3 rot; }
        [System.Serializable] private class Data { public List<Item> items = new List<Item>(); }

        /// <summary>シーンの上からの道のり（例：Rooms/Room_dim/Desk、同じ名前の2つ目は Chair#1）</summary>
        public static string IdOf(Transform t)
        {
            var parts = new List<string>();
            for (var p = t; p != null; p = p.parent)
            {
                int k = 0;
                var siblings = p.parent != null ? null : p.gameObject.scene.GetRootGameObjects();
                if (p.parent != null)
                {
                    for (int i = 0; i < p.parent.childCount; i++)
                    {
                        var s = p.parent.GetChild(i);
                        if (s == p) break;
                        if (s.name == p.name) k++;
                    }
                }
                else
                {
                    foreach (var s in siblings)
                    {
                        if (s.transform == p) break;
                        if (s.name == p.name) k++;
                    }
                }
                parts.Insert(0, k > 0 ? $"{p.name}#{k}" : p.name);
            }
            return string.Join("/", parts);
        }

        /// <summary>記録の道のりから物を探す（無ければ null）</summary>
        public static Transform Find(Scene scene, string id)
        {
            var parts = id.Split('/');
            Transform cur = null;
            for (int d = 0; d < parts.Length; d++)
            {
                string name = parts[d];
                int k = 0, hash = name.LastIndexOf('#');
                if (hash > 0 && int.TryParse(name.Substring(hash + 1), out int n)) { k = n; name = name.Substring(0, hash); }
                Transform next = null;
                int seen = 0;
                if (cur == null)
                {
                    foreach (var r in scene.GetRootGameObjects())
                        if (r.name == name && seen++ == k) { next = r.transform; break; }
                }
                else
                {
                    for (int i = 0; i < cur.childCount; i++)
                    {
                        var c = cur.GetChild(i);
                        if (c.name == name && seen++ == k) { next = c; break; }
                    }
                }
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        /// <summary>今の位置と向きを記録する（同じ物の記録は上書き）。記録の道のりを返す</summary>
        public static string Record(Transform t)
        {
            string id = IdOf(t);
            var data = Load();
            var item = data.items.Find(x => x.id == id);
            if (item == null) data.items.Add(item = new Item { id = id });
            item.pos = t.localPosition;
            item.rot = t.localEulerAngles;
            Save(data);
            if (Application.isPlaying)
            {
                var moved = new HashSet<string>(SessionState.GetString(PlayMovedKey, "").Split('\n'));
                moved.Add(id);
                moved.Remove("");
                SessionState.SetString(PlayMovedKey, string.Join("\n", moved));
            }
            return id;
        }

        /// <summary>記録をシーンへ当てる（only を渡せばその道のりだけ）。当てた数を返す</summary>
        public static int Apply(Scene scene, ICollection<string> only = null)
        {
            var data = Load();
            int applied = 0;
            var missing = new List<string>();
            foreach (var item in data.items)
            {
                if (only != null && !only.Contains(item.id)) continue;
                var t = Find(scene, item.id);
                if (t == null) { missing.Add(item.id); continue; }
                if (!Application.isPlaying) Undo.RecordObject(t, "配置の記録を当てる");
                t.localPosition = item.pos;
                t.localEulerAngles = item.rot;
                applied++;
            }
            if (applied > 0 || missing.Count > 0)
                Debug.Log($"[LoopLayout] 手で置いた位置を当てた: {applied}件" +
                          (missing.Count > 0 ? $"（見つからない: {string.Join(", ", missing)}）" : ""));
            return applied;
        }

        [MenuItem("Tools/EscapePrototype/配置/選んだ物の配置の記録を消す")]
        private static void ForgetSelected()
        {
            var data = Load();
            int removed = 0;
            foreach (var t in Selection.transforms)
                removed += data.items.RemoveAll(x => x.id == IdOf(t));
            Save(data);
            Debug.Log($"[LoopLayout] 記録を消した: {removed}件（次にシーンを生成し直すと、ビルダーの位置に戻る）");
        }

        [MenuItem("Tools/EscapePrototype/配置/配置の記録を今のシーンに当てる")]
        private static void ApplyToOpenScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (Apply(scene) > 0 && !Application.isPlaying) EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>プレイ中に動かした物を、プレイを止めた後のシーンにも当てる</summary>
        [InitializeOnLoadMethod]
        private static void HookPlayMode()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode) return;
                string moved = SessionState.GetString(PlayMovedKey, "");
                SessionState.EraseString(PlayMovedKey);
                if (string.IsNullOrEmpty(moved)) return;
                var scene = SceneManager.GetActiveScene();
                if (Apply(scene, new HashSet<string>(moved.Split('\n'))) > 0) EditorSceneManager.MarkSceneDirty(scene);
            };
        }

        private static Data Load()
        {
            if (!File.Exists(FilePath)) return new Data();
            return JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }

        private static void Save(Data data)
        {
            data.items.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            if (!Application.isPlaying) AssetDatabase.ImportAsset(FilePath);
        }
    }
}
