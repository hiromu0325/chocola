using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// ラインマーカーを引ける本文。マウスで文字の上をドラッグすると、その範囲が
    /// 手帳の「メモ」に書き写される（MemoSnippets）。引いたマーカーは本文の上に残り、クリックすると消せる。
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
        private int _dragFrom = -1, _dragTo = -1;
        private bool _dragging, _moved;
        private int _dirtyFrames;

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
            _dragFrom = _dragTo = -1;
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
            int i = CharAt(e.position, e.pressEventCamera);
            _dragFrom = _dragTo = i;
            _dragging = i >= 0;
            _moved = false;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging) return;
            int i = CharAt(e.position, e.pressEventCamera);
            if (i >= 0 && i != _dragTo) { _dragTo = i; _moved = true; Redraw(); }
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            int a = Mathf.Min(_dragFrom, _dragTo), b = Mathf.Max(_dragFrom, _dragTo);
            _dragFrom = _dragTo = -1;
            if (!_moved || b <= a)
            {
                // ただのクリック：マーカーの上なら消す
                var hit = SnippetAt(a);
                if (hit != null) { MemoSnippets.Remove(hit.id); OnRemoved?.Invoke(); }
                Redraw();
                return;
            }
            var snip = MemoSnippets.Add(EntryId, _body, a, b - a + 1);
            Redraw();
            if (snip != null) OnAdded?.Invoke(snip);
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
                int a = Mathf.Min(_dragFrom, _dragTo), b = Mathf.Max(_dragFrom, _dragTo) + 1;
                used = Paint(a, b, new Color(Marker.r, Marker.g, Marker.b, Marker.a * 0.75f), used);
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
