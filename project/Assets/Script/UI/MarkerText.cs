using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// ラインマーカーを引ける本文。蛍光ペンのように自由になぞれる：
    /// なぞった軌跡（太さのある筆）に少しでも触れた文字はすべて拾い、本文の中で続いている文字
    /// （間が空白・改行だけのものも続きとみなす）を1つのまとまりとして、手帳の「メモ」に書き写す（MemoSnippets）。
    /// 斜めになぞっても、行をまたいでも、触れた所だけが塗られる。引いたマーカーはクリックすると消せる。
    /// 文字の位置は Text の cachedTextGenerator から取る（リッチテキストは使わない＝文字の番号がずれない）。
    /// ※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    public class MarkerText : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Text Text;
        public string EntryId;
        public Color Marker = new Color(0.72f, 0.56f, 0.25f, 0.42f);
        /// <summary>書き写した／消した時（トーストや音を出す）</summary>
        public Action<MemoSnippet> OnAdded;
        public Action OnRemoved;

        private string _body = "";
        private RectTransform _layer;
        private readonly List<Image> _pool = new List<Image>();
        private bool _dragging, _moved;
        private int _dirtyFrames;
        private int _clickIndex = -1;                                  // 押した所の文字（クリックでマーカーを消す）
        private readonly SortedSet<int> _touched = new SortedSet<int>(); // なぞった軌跡に触れた文字
        private readonly List<Rect> _rects = new List<Rect>();          // 文字ごとの枠（本文の基準点からの相対）
        private Vector2 _pressLocal, _lastLocal;

        /// <summary>筆の太さ（半径）。行の高さの約3割＝少しでも文字に被れば拾う</summary>
        private float BrushRadius => Mathf.Max(3f, Text.fontSize * 0.3f);

        public static MarkerText Create(RectTransform parent, Text text)
        {
            // 塗りの層は文字の下（同じ大きさ・同じ基準点）
            var layer = UiTheme.Rect(parent, "Markers");
            var trt = text.rectTransform;
            layer.anchorMin = trt.anchorMin; layer.anchorMax = trt.anchorMax; layer.pivot = trt.pivot;
            layer.anchoredPosition = trt.anchoredPosition; layer.sizeDelta = trt.sizeDelta;
            layer.SetSiblingIndex(trt.GetSiblingIndex());
            text.raycastTarget = true;
            text.supportRichText = false;
            var m = text.gameObject.AddComponent<MarkerText>();
            m.Text = text;
            m._layer = layer;
            return m;
        }

        public void SetBody(string entryId, string body)
        {
            EntryId = entryId;
            _body = (body ?? "").Replace("\r", "");
            Text.text = _body;
            _touched.Clear();
            _rects.Clear();
            _dirtyFrames = 2;   // 文字の配置が決まってから塗る
        }

        private void OnEnable()
        {
            MemoSnippets.OnChanged += MarkDirty;
            _dirtyFrames = 2;
        }
        private void OnDisable() => MemoSnippets.OnChanged -= MarkDirty;
        private void MarkDirty() => _dirtyFrames = Mathf.Max(_dirtyFrames, 1);

        private void LateUpdate()
        {
            if (_dirtyFrames <= 0) return;
            if (--_dirtyFrames > 0) return;
            Redraw();
        }

        // ============================== 操作 ==============================

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            if (!ToLocal(e.position, e.pressEventCamera, out var p)) return;
            BuildRects();
            _touched.Clear();
            _dragging = true;
            _moved = false;
            _pressLocal = _lastLocal = p;
            _clickIndex = CharAt(e.position, e.pressEventCamera);
            TouchAt(p);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging || !ToLocal(e.position, e.pressEventCamera, out var p)) return;
            if (!_moved && (p - _pressLocal).magnitude < BrushRadius * 0.6f) return;   // 手ぶれはクリックのまま
            _moved = true;
            TouchSegment(_lastLocal, p);
            _lastLocal = p;
            Redraw();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (!_moved)
            {
                // ただのクリック：マーカーの上なら消す
                var hit = SnippetAt(_clickIndex);
                if (hit != null) { MemoSnippets.Remove(hit.id); OnRemoved?.Invoke(); }
                _touched.Clear();
                Redraw();
                return;
            }
            // 触れた文字を、本文の中で続いているまとまりごとに書き写す
            MemoSnippet last = null;
            foreach (var (a, b) in Groups(_touched, joinBlank: true))
            {
                var snip = MemoSnippets.Add(EntryId, _body, a, b - a + 1);
                if (snip != null) last = snip;
            }
            _touched.Clear();
            Redraw();
            if (last != null) OnAdded?.Invoke(last);
        }

        /// <summary>
        /// 文字番号の集まり → 続いている範囲 [a, b] の並び。
        /// joinBlank = 間が空白・改行だけなら続きとみなす（行をまたいでなぞった時に1つのまとまりにする）
        /// </summary>
        private IEnumerable<(int a, int b)> Groups(SortedSet<int> set, bool joinBlank)
        {
            int a = -1, b = -1;
            foreach (int i in set)
            {
                if (a < 0) { a = b = i; continue; }
                if (i == b + 1 || (joinBlank && OnlyBlank(b + 1, i))) { b = i; continue; }
                yield return (a, b);
                a = b = i;
            }
            if (a >= 0) yield return (a, b);
        }

        private bool OnlyBlank(int from, int to)
        {
            for (int i = from; i < to; i++)
                if (i < _body.Length && !char.IsWhiteSpace(_body[i])) return false;
            return true;
        }

        // ---- 筆の当たり ----

        private bool ToLocal(Vector2 screen, Camera cam, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Text.rectTransform, screen, cam, out local);

        /// <summary>文字ごとの枠を作る（改行は枠なし）。本文を置き直した後の最初のクリックで作る</summary>
        private void BuildRects()
        {
            _rects.Clear();
            var gen = Text.cachedTextGenerator;
            var lines = gen.lines;
            var chars = gen.characters;
            int n = Mathf.Min(chars.Count, _body.Length);
            if (lines.Count == 0) return;
            float ppu = Ppu;
            int li = 0;
            for (int i = 0; i < n; i++)
            {
                while (li + 1 < lines.Count && lines[li + 1].startCharIdx <= i) li++;
                if (_body[i] == '\n') { _rects.Add(Rect.zero); continue; }
                float x0 = chars[i].cursorPos.x / ppu;
                float w = Mathf.Max(chars[i].charWidth / ppu, 1f);
                float top = lines[li].topY / ppu, h = lines[li].height / ppu;
                _rects.Add(new Rect(x0, top - h, w, h));
            }
        }

        /// <summary>筆（点）に少しでも重なる文字を拾う</summary>
        private void TouchAt(Vector2 p)
        {
            float r = BrushRadius;
            for (int i = 0; i < _rects.Count; i++)
            {
                var rc = _rects[i];
                if (rc.width <= 0f) continue;
                float dx = Mathf.Max(rc.xMin - p.x, 0f, p.x - rc.xMax);
                float dy = Mathf.Max(rc.yMin - p.y, 0f, p.y - rc.yMax);
                if (dx * dx + dy * dy <= r * r) _touched.Add(i);
            }
        }

        /// <summary>筆を a から b へ動かした間に触れた文字（素早くなぞっても抜けないよう細かく刻む）</summary>
        private void TouchSegment(Vector2 a, Vector2 b)
        {
            float step = BrushRadius * 0.5f;
            int n = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / step));
            for (int k = 1; k <= n; k++) TouchAt(Vector2.Lerp(a, b, (float)k / n));
        }

        private MemoSnippet SnippetAt(int index)
        {
            if (index < 0) return null;
            foreach (var s in MemoSnippets.ForEntry(EntryId))
                if (index >= s.start && index < s.start + s.length) return s;
            return null;
        }

        // ============================== 文字の位置 ==============================

        private float Ppu => Text.pixelsPerUnit > 0f ? Text.pixelsPerUnit : 1f;

        /// <summary>画面の位置 → 本文の文字番号（行の外なら一番近い行の端）。文字が無ければ -1</summary>
        private int CharAt(Vector2 screen, Camera cam)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Text.rectTransform, screen, cam, out var local))
                return -1;
            var gen = Text.cachedTextGenerator;
            var lines = gen.lines;
            var chars = gen.characters;
            if (lines.Count == 0 || chars.Count == 0) return -1;
            float y = local.y * Ppu, x = local.x * Ppu;

            int li = lines.Count - 1;
            for (int i = 0; i < lines.Count; i++)
            {
                float bottom = lines[i].topY - lines[i].height - lines[i].leading;
                if (y >= bottom) { li = i; break; }
            }
            int start = lines[li].startCharIdx;
            int end = li + 1 < lines.Count ? lines[li + 1].startCharIdx : Mathf.Min(chars.Count, _body.Length);
            end = Mathf.Min(end, Mathf.Min(chars.Count, _body.Length));
            if (end <= start) return Mathf.Clamp(start, 0, _body.Length - 1);
            int best = start;
            for (int c = start; c < end; c++)
            {
                if (_body[c] == '\n') break;
                best = c;
                if (x < chars[c].cursorPos.x + chars[c].charWidth * 0.5f) break;
            }
            return Mathf.Clamp(best, 0, _body.Length - 1);
        }

        // ============================== 塗り ==============================

        private void Redraw()
        {
            if (Text == null || _layer == null) return;
            int used = 0;
            foreach (var s in MemoSnippets.ForEntry(EntryId))
                used = Paint(s.start, s.start + s.length, Marker, used);
            if (_dragging && _moved)
            {
                // なぞっている途中：触れた文字だけをその場で塗る
                var live = new Color(Marker.r, Marker.g, Marker.b, Marker.a * 0.75f);
                foreach (var (a, b) in Groups(_touched, joinBlank: false))
                    used = Paint(a, b + 1, live, used);
            }
            for (int i = used; i < _pool.Count; i++) _pool[i].gameObject.SetActive(false);
        }

        /// <summary>[from, to) を行ごとの帯で塗る。蛍光ペンのように行の下寄り 7 割だけ</summary>
        private int Paint(int from, int to, Color color, int used)
        {
            var gen = Text.cachedTextGenerator;
            var lines = gen.lines;
            var chars = gen.characters;
            int n = Mathf.Min(chars.Count, _body.Length);
            from = Mathf.Clamp(from, 0, n); to = Mathf.Clamp(to, 0, n);
            if (to <= from) return used;
            float ppu = Ppu;
            for (int li = 0; li < lines.Count; li++)
            {
                int ls = lines[li].startCharIdx;
                int le = li + 1 < lines.Count ? lines[li + 1].startCharIdx : n;
                int a = Mathf.Max(from, ls), b = Mathf.Min(to, le);
                // 行末の改行は塗らない
                while (b > a && (_body[b - 1] == '\n')) b--;
                if (b <= a) continue;
                float x0 = chars[a].cursorPos.x;
                float x1 = chars[b - 1].cursorPos.x + chars[b - 1].charWidth;
                float h = lines[li].height;
                float top = lines[li].topY - h * 0.3f;
                var img = Get(used++);
                img.color = color;
                var rt = img.rectTransform;
                rt.anchoredPosition = new Vector2(x0 / ppu, top / ppu);
                rt.sizeDelta = new Vector2((x1 - x0) / ppu, h * 0.7f / ppu);
            }
            return used;
        }

        private Image Get(int i)
        {
            while (_pool.Count <= i)
            {
                var img = UiTheme.Fill(_layer, "Mark", Marker);
                var rt = img.rectTransform;
                // 位置は本文の基準点からの相対（Text の生成座標と同じ）
                rt.anchorMin = rt.anchorMax = Text.rectTransform.pivot;
                rt.pivot = new Vector2(0f, 1f);
                _pool.Add(img);
            }
            _pool[i].gameObject.SetActive(true);
            return _pool[i];
        }
    }
}
