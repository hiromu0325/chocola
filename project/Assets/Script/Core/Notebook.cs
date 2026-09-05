using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>手帳エントリの表示用変数（レイアウトDSLの {key} が参照する）</summary>
    [Serializable]
    public class NotebookVar
    {
        public string key;
        public string value;
    }

    /// <summary>手帳の1項目</summary>
    [Serializable]
    public class NotebookEntry
    {
        public string id;
        public string title;
        public string body;
        public List<NotebookVar> vars = new List<NotebookVar>();

        public string GetVar(string key)
        {
            if (vars != null)
                foreach (var v in vars)
                    if (v != null && v.key == key) return v.value;
            return null;
        }
    }

    /// <summary>
    /// 手帳の中身。最初は空。情報を見つけると Add で追記される。
    /// Tabでいつでも開ける（HUDManagerが描画）。セーブに保存され継続できる。
    /// </summary>
    public static class Notebook
    {
        private static List<NotebookEntry> _entries = new List<NotebookEntry>();
        private static Dictionary<string, int> _index = new Dictionary<string, int>();

        public static event Action OnChanged;
        public static IReadOnlyList<NotebookEntry> Entries => _entries;
        public static int Count => _entries.Count;

        /// <summary>
        /// 情報を追記（同idは上書き更新）。新規に追記できたら true。
        /// vars はレイアウトDSL（MemoBoardLayout.txt）の {key} で参照できる表示用変数。
        /// </summary>
        public static bool Add(string id, string title, string body,
            params (string key, string value)[] vars)
        {
            var varList = new List<NotebookVar>();
            if (vars != null)
                foreach (var (key, value) in vars)
                    varList.Add(new NotebookVar { key = key, value = value });

            if (_index.TryGetValue(id, out int i))
            {
                _entries[i].title = title;
                _entries[i].body = body;
                _entries[i].vars = varList;
                OnChanged?.Invoke();
                return false;
            }
            _index[id] = _entries.Count;
            _entries.Add(new NotebookEntry { id = id, title = title, body = body, vars = varList });
            OnChanged?.Invoke();
            return true;
        }

        public static void Clear()
        {
            _entries = new List<NotebookEntry>();
            _index = new Dictionary<string, int>();
            OnChanged?.Invoke();
        }

        // ---- 付箋（ギミックの誤答時に「読み直すべき資料」へ立てる目印。答えは書かない）----
        private static HashSet<string> _flagged = new HashSet<string>();
        public static bool IsFlagged(string id) => !string.IsNullOrEmpty(id) && _flagged.Contains(id);
        public static bool HasAnyFlag => _flagged.Count > 0;

        /// <summary>資料に付箋を立てる（存在しないidは無視）。立てられたらtrue</summary>
        public static bool Flag(string id)
        {
            if (string.IsNullOrEmpty(id) || !_index.ContainsKey(id)) return false;
            if (!_flagged.Add(id)) return false;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>付箋を外す（資料を開いて読み直したとき）</summary>
        public static void Unflag(string id)
        {
            if (_flagged.Remove(id)) OnChanged?.Invoke();
        }

        public static bool Contains(string id) => !string.IsNullOrEmpty(id) && _index.ContainsKey(id);

        public static List<NotebookEntry> ToList() => new List<NotebookEntry>(_entries);

        public static void LoadFrom(List<NotebookEntry> list)
        {
            Clear();
            if (list == null) return;
            foreach (var e in list)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                Add(e.id, e.title, e.body);
                // セーブに保存されていた表示用変数もそのまま引き継ぐ
                if (e.vars != null && _index.TryGetValue(e.id, out int i))
                    _entries[i].vars = new List<NotebookVar>(e.vars);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _entries = new List<NotebookEntry>();
            _index = new Dictionary<string, int>();
            _flagged = new HashSet<string>();
            OnChanged = null;
        }
    }
}
