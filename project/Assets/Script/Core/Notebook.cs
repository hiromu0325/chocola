using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>手帳の1項目</summary>
    [Serializable]
    public class NotebookEntry
    {
        public string id;
        public string title;
        public string body;
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

        /// <summary>情報を追記（同idは上書き更新）。新規に追記できたら true</summary>
        public static bool Add(string id, string title, string body)
        {
            if (_index.TryGetValue(id, out int i))
            {
                _entries[i].title = title;
                _entries[i].body = body;
                OnChanged?.Invoke();
                return false;
            }
            _index[id] = _entries.Count;
            _entries.Add(new NotebookEntry { id = id, title = title, body = body });
            OnChanged?.Invoke();
            return true;
        }

        public static void Clear()
        {
            _entries = new List<NotebookEntry>();
            _index = new Dictionary<string, int>();
            OnChanged?.Invoke();
        }

        public static List<NotebookEntry> ToList() => new List<NotebookEntry>(_entries);

        public static void LoadFrom(List<NotebookEntry> list)
        {
            Clear();
            if (list == null) return;
            foreach (var e in list)
                if (e != null && !string.IsNullOrEmpty(e.id)) Add(e.id, e.title, e.body);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _entries = new List<NotebookEntry>();
            _index = new Dictionary<string, int>();
            OnChanged = null;
        }
    }
}
