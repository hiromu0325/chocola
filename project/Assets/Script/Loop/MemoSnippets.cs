using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>マーカーで切り取った文（手帳の「メモ」に並ぶ1枚）</summary>
    [Serializable]
    public class MemoSnippet
    {
        public int id;
        public string entryId;   // どの資料から（手帳エントリId）
        public int start;        // 資料の本文の何文字目から
        public int length;
        public string text;
    }

    /// <summary>
    /// 資料の本文にラインマーカーを引いて切り取った文の一覧。
    /// 並びはプレイヤーが自由に入れ替えられ（手帳の「メモ」）、セーブに残る。
    /// </summary>
    public static class MemoSnippets
    {
        private static List<MemoSnippet> _list = new List<MemoSnippet>();
        private static int _nextId = 1;

        public static event Action OnChanged;
        public static IReadOnlyList<MemoSnippet> All => _list;
        public static int Count => _list.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _list = new List<MemoSnippet>();
            _nextId = 1;
            OnChanged = null;
        }

        /// <summary>切り取った文を末尾に足す。前後の空白・改行は落とす。重なる範囲の既存のマーカーは1本にまとめる</summary>
        public static MemoSnippet Add(string entryId, string body, int start, int length)
        {
            if (string.IsNullOrEmpty(body) || length <= 0) return null;
            start = Mathf.Clamp(start, 0, body.Length);
            length = Mathf.Clamp(length, 0, body.Length - start);
            // 端の空白・改行を削る
            while (length > 0 && char.IsWhiteSpace(body[start])) { start++; length--; }
            while (length > 0 && char.IsWhiteSpace(body[start + length - 1])) length--;
            if (length <= 0) return null;

            // 重なる・接するマーカーは合わせて1本にする（同じ所を二重に書き写さない）
            int end = start + length;
            int insertAt = _list.Count;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var s = _list[i];
                if (s.entryId != entryId) continue;
                if (s.start <= end && start <= s.start + s.length)
                {
                    start = Mathf.Min(start, s.start);
                    end = Mathf.Max(end, s.start + s.length);
                    insertAt = i;
                    _list.RemoveAt(i);
                }
            }
            var snip = new MemoSnippet
            {
                id = _nextId++, entryId = entryId, start = start, length = end - start,
                text = Clean(body.Substring(start, end - start)),
            };
            _list.Insert(Mathf.Min(insertAt, _list.Count), snip);
            OnChanged?.Invoke();
            return snip;
        }

        /// <summary>メモに並べる時の文面：改行と全角の字下げを詰める</summary>
        private static string Clean(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            bool lastSpace = false;
            foreach (char c in s)
            {
                bool space = c == '\n' || c == '\r';
                if (space) { if (!lastSpace) sb.Append(' '); lastSpace = true; continue; }
                if (c == '　' && lastSpace) continue;
                sb.Append(c);
                lastSpace = false;
            }
            return sb.ToString().Trim();
        }

        public static void Remove(int snippetId)
        {
            int i = _list.FindIndex(s => s.id == snippetId);
            if (i < 0) return;
            _list.RemoveAt(i);
            OnChanged?.Invoke();
        }

        /// <summary>並び替え：from 番目を to 番目へ</summary>
        public static void Move(int from, int to)
        {
            if (from < 0 || from >= _list.Count) return;
            to = Mathf.Clamp(to, 0, _list.Count - 1);
            if (from == to) return;
            var s = _list[from];
            _list.RemoveAt(from);
            _list.Insert(to, s);
            OnChanged?.Invoke();
        }

        public static int IndexOf(int snippetId) => _list.FindIndex(s => s.id == snippetId);

        /// <summary>その資料に引いたマーカー（本文の位置順）</summary>
        public static List<MemoSnippet> ForEntry(string entryId)
        {
            var l = _list.FindAll(s => s.entryId == entryId);
            l.Sort((a, b) => a.start.CompareTo(b.start));
            return l;
        }

        public static void Clear()
        {
            _list = new List<MemoSnippet>();
            _nextId = 1;
            OnChanged?.Invoke();
        }

        // ---- セーブ連携 ----
        public static List<MemoSnippet> Export() => new List<MemoSnippet>(_list);

        public static void Import(List<MemoSnippet> list)
        {
            _list = list != null ? new List<MemoSnippet>(list) : new List<MemoSnippet>();
            _nextId = 1;
            foreach (var s in _list) _nextId = Mathf.Max(_nextId, s.id + 1);
            OnChanged?.Invoke();
        }
    }

    /// <summary>資料の読み方の状態：聞き終えた音声記録・字幕を出した資料（セーブに残る）</summary>
    public static class DocState
    {
        private static HashSet<string> _heard = new HashSet<string>();
        private static HashSet<string> _commented = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _heard = new HashSet<string>();
            _commented = new HashSet<string>();
        }

        /// <summary>音声記録を最後まで聞いたか（聞き終えると書き起こしを読める）</summary>
        public static bool Heard(string entryId) => _heard.Contains(entryId);
        public static void MarkHeard(string entryId) { if (!string.IsNullOrEmpty(entryId)) _heard.Add(entryId); }

        /// <summary>主人公のひと言をもう出したか（2回目以降は出さない）</summary>
        public static bool Commented(string entryId) => _commented.Contains(entryId);
        public static void MarkCommented(string entryId) { if (!string.IsNullOrEmpty(entryId)) _commented.Add(entryId); }

        public static void Clear()
        {
            _heard.Clear();
            _commented.Clear();
        }

        public static List<string> ExportHeard() => new List<string>(_heard);
        public static List<string> ExportCommented() => new List<string>(_commented);
        public static void Import(List<string> heard, List<string> commented)
        {
            _heard = heard != null ? new HashSet<string>(heard) : new HashSet<string>();
            _commented = commented != null ? new HashSet<string>(commented) : new HashSet<string>();
        }
    }
}
