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
    /// 資料の上に引いたマーカーの線（リボン）。なぞった点をそのまま残し、読む画面を開くたびに同じ所へ描き直す。
    /// 点は本文の基準点からの相対。本文の組み方（文字の大きさ・枠の幅）が引いた時と違う時は線を描かず、
    /// 触れた文字の帯で代わりに見せる
    /// </summary>
    [Serializable]
    public class MarkerStroke
    {
        public int id;
        public string entryId;
        public float width;                          // 線の太さ
        public int fontSize;                         // 引いた時の本文の文字の大きさ（組み方が同じか確かめる）
        public float boxWidth;                       // 〃 本文の枠の幅
        public List<float> xy = new List<float>();   // なぞった点（x, y の交互）
        public List<int> chars = new List<int>();    // 線が触れた文字の範囲（start, length の交互）

        public int PointCount => xy.Count / 2;
        public Vector2 Point(int i) => new Vector2(xy[i * 2], xy[i * 2 + 1]);

        public bool Covers(int index)
        {
            for (int k = 0; k + 1 < chars.Count; k += 2)
                if (index >= chars[k] && index < chars[k] + chars[k + 1]) return true;
            return false;
        }

        public bool Overlaps(int from, int to)
        {
            for (int k = 0; k + 1 < chars.Count; k += 2)
                if (chars[k] < to && from < chars[k] + chars[k + 1]) return true;
            return false;
        }

        /// <summary>[from, to) を触れた範囲から外す</summary>
        public void Cut(int from, int to)
        {
            var next = new List<int>();
            for (int k = 0; k + 1 < chars.Count; k += 2)
            {
                int a = chars[k], b = chars[k] + chars[k + 1];
                if (b <= from || a >= to) { next.Add(a); next.Add(b - a); continue; }
                if (a < from) { next.Add(a); next.Add(from - a); }
                if (b > to) { next.Add(to); next.Add(b - to); }
            }
            chars = next;
        }
    }

    /// <summary>
    /// 資料の本文にラインマーカーを引いて切り取った文の一覧。
    /// 並びはプレイヤーが自由に入れ替えられ（手帳の「メモ」）、セーブに残る。
    /// 引いた線そのもの（MarkerStroke）も資料の上に残し、線を消すとその線だけが拾った文字もメモから消える。
    /// </summary>
    public static class MemoSnippets
    {
        private static List<MemoSnippet> _list = new List<MemoSnippet>();
        private static List<MarkerStroke> _strokes = new List<MarkerStroke>();
        private static int _nextId = 1, _nextStrokeId = 1;

        public static event Action OnChanged;
        public static IReadOnlyList<MemoSnippet> All => _list;
        public static int Count => _list.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _list = new List<MemoSnippet>();
            _strokes = new List<MarkerStroke>();
            _nextId = _nextStrokeId = 1;
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

        /// <summary>メモを1枚消す。その文字に触れていた線は、ほかのメモも拾っていなければ一緒に消す</summary>
        public static void Remove(int snippetId)
        {
            int i = _list.FindIndex(s => s.id == snippetId);
            if (i < 0) return;
            var s = _list[i];
            _list.RemoveAt(i);
            for (int k = _strokes.Count - 1; k >= 0; k--)
            {
                var st = _strokes[k];
                if (st.entryId != s.entryId) continue;
                st.Cut(s.start, s.start + s.length);
                if (!_list.Exists(o => o.entryId == st.entryId && st.Overlaps(o.start, o.start + o.length)))
                    _strokes.RemoveAt(k);
            }
            OnChanged?.Invoke();
        }

        // ---- 線（資料の上に残るマーカー）----

        /// <summary>引いた線を残す（点は本文の基準点からの相対。ranges は触れた文字の [a, b]）</summary>
        public static MarkerStroke AddStroke(string entryId, IList<Vector2> points, float width, int fontSize, float boxWidth,
                                             IEnumerable<(int a, int b)> ranges)
        {
            var st = new MarkerStroke { id = _nextStrokeId++, entryId = entryId, width = width, fontSize = fontSize, boxWidth = boxWidth };
            foreach (var p in points) { st.xy.Add(Mathf.Round(p.x * 10f) / 10f); st.xy.Add(Mathf.Round(p.y * 10f) / 10f); }
            foreach (var (a, b) in ranges) { st.chars.Add(a); st.chars.Add(b - a + 1); }
            _strokes.Add(st);
            OnChanged?.Invoke();
            return st;
        }

        /// <summary>その資料に引いた線（引いた順）</summary>
        public static List<MarkerStroke> StrokesFor(string entryId) => _strokes.FindAll(s => s.entryId == entryId);

        /// <summary>
        /// 線を消す。その線だけが拾っていた文字をメモから外す（メモの1枚が途中で切れる時は2枚に分ける。
        /// 並びの位置はそのまま）。body は資料の本文
        /// </summary>
        public static void RemoveStroke(int strokeId, string body)
        {
            int i = _strokes.FindIndex(s => s.id == strokeId);
            if (i < 0) return;
            var st = _strokes[i];
            _strokes.RemoveAt(i);
            body = body ?? "";

            // この線だけが触れていた（空白でない）文字
            var gone = new HashSet<int>();
            for (int k = 0; k + 1 < st.chars.Count; k += 2)
                for (int c = st.chars[k]; c < st.chars[k] + st.chars[k + 1]; c++)
                    if (c < body.Length && !char.IsWhiteSpace(body[c]) && !_strokes.Exists(o => o.entryId == st.entryId && o.Covers(c)))
                        gone.Add(c);

            for (int k = _list.Count - 1; k >= 0 && gone.Count > 0; k--)
            {
                var s = _list[k];
                if (s.entryId != st.entryId) continue;
                int end = s.start + s.length;
                bool hit = false;
                for (int c = s.start; c < end && !hit; c++) hit = gone.Contains(c);
                if (!hit) continue;
                _list.RemoveAt(k);
                if (end > body.Length) continue;   // 本文が変わっている：まるごと消す

                // 残る文字を、消える文字で区切ったまとまりに分ける（空白は区切りにしない）
                var parts = new List<MemoSnippet>();
                int a = -1, b = -1;
                for (int c = s.start; c <= end; c++)
                {
                    bool cut = c == end || gone.Contains(c);
                    if (!cut)
                    {
                        if (char.IsWhiteSpace(body[c])) continue;
                        if (a < 0) a = c;
                        b = c;
                        continue;
                    }
                    if (a < 0) continue;
                    parts.Add(new MemoSnippet
                    {
                        id = parts.Count == 0 ? s.id : _nextId++, entryId = s.entryId, start = a, length = b - a + 1,
                        text = Clean(body.Substring(a, b - a + 1)),
                    });
                    a = -1;
                }
                _list.InsertRange(k, parts);
            }
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
            _strokes = new List<MarkerStroke>();
            _nextId = _nextStrokeId = 1;
            OnChanged?.Invoke();
        }

        // ---- セーブ連携 ----
        public static List<MemoSnippet> Export() => new List<MemoSnippet>(_list);
        public static List<MarkerStroke> ExportStrokes() => new List<MarkerStroke>(_strokes);

        public static void Import(List<MemoSnippet> list, List<MarkerStroke> strokes)
        {
            _list = list != null ? new List<MemoSnippet>(list) : new List<MemoSnippet>();
            _strokes = strokes != null ? new List<MarkerStroke>(strokes) : new List<MarkerStroke>();
            _nextId = _nextStrokeId = 1;
            foreach (var s in _list) _nextId = Mathf.Max(_nextId, s.id + 1);
            foreach (var s in _strokes) _nextStrokeId = Mathf.Max(_nextStrokeId, s.id + 1);
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
