using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// ラインマーカーを引ける本文。蛍光ペンのように自由になぞれる：
    /// なぞった軌跡はそのまま太い線（リボン）として資料の上に残り（MarkerStroke）、
    /// 線に少しでも被った文字はすべて拾って、本文の中で続いている文字（間が空白・改行だけのものも続きとみなす）を
    /// 1つのまとまりとして手帳の「メモ」に書き写す（MemoSnippets）。拾った文字は白く浮き上がる（MarkerGlyphs）。
    /// 線をクリックすると、その線と、その線だけが拾っていた文字のメモが消える。
    /// 文字の位置は Text の cachedTextGenerator から取る（リッチテキストは使わない＝文字の番号がずれない）。
    /// ※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    public class MarkerText : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Text Text;
        public string EntryId;
        /// <summary>線の色（a = 紙に重ねる濃さ）。本文の文字と同系色にして、拾った文字だけを白く浮き上がらせる</summary>
        public Color Marker = new Color(0.9f, 0.88f, 0.84f, 0.3f);
        /// <summary>本文の下の紙（パネル）の色。線はこの上に Marker を重ねた色で不透明に塗る（線が重なっても濃くならない）</summary>
        public Color Paper = UiTheme.Panel;
        /// <summary>書き写した／消した時（トーストや音を出す）</summary>
        public Action<MemoSnippet> OnAdded;
        public Action OnRemoved;

        private string _body = "";
        private RectTransform _layer, _boxes;
        private MarkerRibbon _ribbon;
        private MarkerGlyphs _glyphs;
        private readonly HashSet<int> _lit = new HashSet<int>();        // 白く浮き上がらせる文字
        private readonly List<Image> _pool = new List<Image>();
        private readonly List<Vector2> _live = new List<Vector2>();     // なぞっている線の点
        private readonly HashSet<int> _boxed = new HashSet<int>();      // 線ではなく帯で塗っている文字（クリックで消す）
        private bool _dragging, _moved;
        private int _dirtyFrames;
        private int _clickIndex = -1;                                  // 押した所の文字（クリックでマーカーを消す）
        private readonly SortedSet<int> _touched = new SortedSet<int>(); // なぞった軌跡に触れた文字
        private readonly List<Rect> _rects = new List<Rect>();          // 文字ごとの枠（本文の基準点からの相対）
        private Vector2 _pressLocal, _lastLocal;

        /// <summary>線の太さ。字の高さの約9割（蛍光ペン）</summary>
        private float RibbonWidth => Mathf.Max(6f, Text.fontSize * 0.88f);
        private float BrushRadius => RibbonWidth * 0.5f;

        private Color RibbonColor
        {
            get
            {
                var c = Color.Lerp(Paper, Marker, Marker.a);
                c.a = 1f;
                return c;
            }
        }

        public static MarkerText Create(RectTransform parent, Text text)
        {
            // 塗りの層は文字の下（同じ大きさ・同じ基準点）
            var layer = UiTheme.Rect(parent, "Markers");
            var trt = text.rectTransform;
            layer.anchorMin = trt.anchorMin; layer.anchorMax = trt.anchorMax; layer.pivot = trt.pivot;
            layer.anchoredPosition = trt.anchoredPosition; layer.sizeDelta = trt.sizeDelta;
            layer.SetSiblingIndex(trt.GetSiblingIndex());
            // 下から：文字の帯（なぞり中の当たり・古いマーカー）→ 線（リボン）
            var boxes = UiTheme.Rect(layer, "Boxes");
            UiTheme.Stretch(boxes);
            boxes.pivot = trt.pivot;
            var rgo = new GameObject("Ribbon", typeof(RectTransform), typeof(CanvasRenderer));
            rgo.transform.SetParent(layer, false);
            var ribbon = rgo.AddComponent<MarkerRibbon>();
            ribbon.raycastTarget = false;
            UiTheme.Stretch(ribbon.rectTransform);
            ribbon.rectTransform.pivot = trt.pivot;
            text.raycastTarget = true;
            text.supportRichText = false;
            var m = text.gameObject.AddComponent<MarkerText>();
            m.Text = text;
            m._layer = layer;
            m._boxes = boxes;
            m._ribbon = ribbon;
            m._glyphs = text.gameObject.AddComponent<MarkerGlyphs>();
            return m;
        }

        public void SetBody(string entryId, string body)
        {
            EntryId = entryId;
            _body = (body ?? "").Replace("\r", "");
            Text.text = _body;
            _touched.Clear();
            _rects.Clear();
            _live.Clear();
            _dragging = false;
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
            _live.Clear();
            _dragging = true;
            _moved = false;
            _pressLocal = _lastLocal = p;
            _clickIndex = CharAt(e.position, e.pressEventCamera);
            _live.Add(p);
            TouchAt(p);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging || !ToLocal(e.position, e.pressEventCamera, out var p)) return;
            if (!_moved && (p - _pressLocal).magnitude < BrushRadius * 0.6f) return;   // 手ぶれはクリックのまま
            _moved = true;
            TouchSegment(_lastLocal, p);
            _lastLocal = p;
            if ((p - _live[_live.Count - 1]).sqrMagnitude >= 4f && _live.Count < 4000) _live.Add(p);
            Redraw();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (!_moved)
            {
                // ただのクリック：線の上ならその線を消す（古いマーカーは帯の上で）
                var st = StrokeAt(_pressLocal);
                if (st != null) { MemoSnippets.RemoveStroke(st.id, _body); OnRemoved?.Invoke(); }
                else if (_boxed.Contains(_clickIndex))
                {
                    var hit = SnippetAt(_clickIndex);
                    if (hit != null) { MemoSnippets.Remove(hit.id); OnRemoved?.Invoke(); }
                }
                _live.Clear();
                _touched.Clear();
                Redraw();
                return;
            }
            if (_live[_live.Count - 1] != _lastLocal) _live.Add(_lastLocal);

            // 線が触れた文字を、本文の中で続いているまとまりごとに書き写し、線そのものも残す
            MemoSnippet last = null;
            var kept = new List<(int a, int b)>();
            foreach (var (a, b) in Groups(_touched, joinBlank: true))
            {
                var snip = MemoSnippets.Add(EntryId, _body, a, b - a + 1);
                if (snip == null) continue;
                last = snip;
                kept.Add((a, b));
            }
            if (kept.Count > 0)   // 文字に触れなかった線（余白だけ）は残さない
                MemoSnippets.AddStroke(EntryId, _live, RibbonWidth, Text.fontSize, Text.rectTransform.rect.width, kept);
            _live.Clear();
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

        /// <summary>画面の位置 → 本文の基準点からの相対（本文の枠の少し外までに収める）</summary>
        private bool ToLocal(Vector2 screen, Camera cam, out Vector2 local)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Text.rectTransform, screen, cam, out local)) return false;
            var r = Text.rectTransform.rect;
            float m = BrushRadius + 8f;
            local = new Vector2(Mathf.Clamp(local.x, r.xMin - m, r.xMax + m), Mathf.Clamp(local.y, r.yMin - m, r.yMax + m));
            return true;
        }

        /// <summary>字面は行の下寄り 7 割（行の上は行間の余白。帯で塗る時と同じ範囲）</summary>
        private const float GlyphTop = 0.3f;

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
                // 字面だけ。行間の余白に線が掛かっただけでは拾わない
                float x0 = chars[i].cursorPos.x / ppu;
                float w = Mathf.Max(chars[i].charWidth / ppu, 1f);
                float h = lines[li].height / ppu;
                float bottom = lines[li].topY / ppu - h;
                _rects.Add(new Rect(x0, bottom, w, h * (1f - GlyphTop)));
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

        /// <summary>その点に掛かっている線（後から引いた線を優先）。今の組み方で描けている線だけ</summary>
        private MarkerStroke StrokeAt(Vector2 p)
        {
            var list = MemoSnippets.StrokesFor(EntryId);
            for (int k = list.Count - 1; k >= 0; k--)
            {
                var st = list[k];
                if (!Drawable(st)) continue;
                float r = st.width * 0.5f + 2f;
                int n = st.PointCount;
                if (n == 1 && (st.Point(0) - p).sqrMagnitude <= r * r) return st;
                for (int i = 0; i + 1 < n; i++)
                    if (DistToSegment(p, st.Point(i), st.Point(i + 1)) <= r) return st;
            }
            return null;
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>引いた時と本文の組み方が同じ（＝線を同じ所に描ける）か</summary>
        private bool Drawable(MarkerStroke st) =>
            st.fontSize == Text.fontSize && Mathf.Abs(st.boxWidth - Text.rectTransform.rect.width) < 0.5f && st.PointCount > 0;

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
            _boxed.Clear();
            _ribbon.Lines.Clear();

            // 残っている線
            var drawn = new List<MarkerStroke>();
            foreach (var st in MemoSnippets.StrokesFor(EntryId))
            {
                if (!Drawable(st)) continue;
                drawn.Add(st);
                var line = new MarkerRibbon.Line { Width = st.width, Color = RibbonColor };
                for (int i = 0; i < st.PointCount; i++) line.Points.Add(st.Point(i));
                _ribbon.Lines.Add(line);
            }
            // 線で描けていないメモ（線の無い古いマーカー・組み方が変わった線）は文字の帯で塗る
            _lit.Clear();
            foreach (var s in MemoSnippets.ForEntry(EntryId))
            {
                int end = Mathf.Min(s.start + s.length, _body.Length);
                for (int c = s.start; c < end; c++)
                    if (!char.IsWhiteSpace(_body[c])) _lit.Add(c);
                if (drawn.Count == 0)
                {
                    for (int c = s.start; c < end; c++) _boxed.Add(c);
                    used = Paint(s.start, end, Marker, used);
                    continue;
                }
                int a = -1;
                for (int c = s.start; c <= end; c++)
                {
                    bool open = c < end && !char.IsWhiteSpace(_body[c]) && !drawn.Exists(st => st.Covers(c));
                    if (open) { if (a < 0) a = c; _boxed.Add(c); continue; }
                    if (a >= 0) { used = Paint(a, c, Marker, used); a = -1; }
                }
            }
            if (_dragging && _moved)
            {
                // なぞっている途中：線に触れた文字がその場で浮き上がる（どこまで拾うかが分かる）
                foreach (int c in _touched)
                    if (c < _body.Length && !char.IsWhiteSpace(_body[c])) _lit.Add(c);
                var line = new MarkerRibbon.Line { Width = RibbonWidth, Color = Color.Lerp(RibbonColor, Marker, 0.12f) };
                line.Color.a = 1f;
                line.Points.AddRange(_live);
                line.Points.Add(_lastLocal);
                _ribbon.Lines.Add(line);
            }
            for (int i = used; i < _pool.Count; i++) _pool[i].gameObject.SetActive(false);
            _ribbon.Refresh();
            _glyphs.Set(_lit);
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
                float top = lines[li].topY - h * GlyphTop;
                var img = Get(used++);
                img.color = color;
                var rt = img.rectTransform;
                rt.anchoredPosition = new Vector2(x0 / ppu, top / ppu);
                rt.sizeDelta = new Vector2((x1 - x0) / ppu, h * (1f - GlyphTop) / ppu);
            }
            return used;
        }

        private Image Get(int i)
        {
            while (_pool.Count <= i)
            {
                var img = UiTheme.Fill(_boxes, "Mark", Marker);
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
