using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// ルビ（読み仮名）を Text の上に重ねて置く。Text 自体はルビを持たないので、組み終わった字の位置
    /// （cachedTextGenerator）を見て、親文字の上に小さな Text を並べる。
    /// ・親文字の語全体の上に中央でまとめる（グループルビ）。ルビが親文字より長い時は前後に半字ずつまで
    ///   はみ出してよく、それでも入らなければ字を小さくする
    /// ・親文字が行をまたいだ時は、行ごとに親文字の字数で読みを分けて置く
    /// ・ルビの層は Text の子（Text と一緒に動く＝送っても付いてくる）。Text の枠や文字が変わったら組み直す
    /// 行間はルビが入るだけ空けておくこと（呼ぶ側で Text.lineSpacing を広げる）
    /// </summary>
    public class RubyText : MonoBehaviour
    {
        public Text Text;
        /// <summary>ルビの大きさの既定（親文字の何倍）。本来は半分だが、画面では小さすぎて読みにくいので少し大きめ</summary>
        public const float DefaultScale = 0.58f;
        /// <summary>ルビの大きさ（親文字の何倍）</summary>
        public float Scale = DefaultScale;

        private static readonly List<Ruby.Span> TmpSpans = new List<Ruby.Span>();

        /// <summary>
        /// 印付きの文（｜漢字《かんじ》）を Text に入れる。ルビがあれば字の上に重ね、無ければ（オプションで切っている時も）文だけ入れる。
        /// decorate は印を外した文に色のタグなどを足す処理（足したタグの分、ルビの位置を後ろへずらす）。
        /// ルビを出したら true。呼ぶ側は字の上に RoomAbove だけ余白を空ける（複数行なら行間も広げる）
        /// </summary>
        public static bool SetMarked(Text text, string marked, System.Func<string, string> decorate = null)
        {
            if (text == null) return false;
            string plain = Ruby.ParseForDisplay(marked ?? "", TmpSpans);
            string shown = decorate != null ? decorate(plain) : plain;
            if (TmpSpans.Count > 0 && shown != plain) Remap(plain, shown, TmpSpans);
            text.text = shown;
            var rt = text.GetComponent<RubyText>();
            if (TmpSpans.Count == 0)
            {
                if (rt != null) rt.SetSpans(null);
                return false;
            }
            if (rt == null) rt = Attach(text);
            rt.SetSpans(TmpSpans);
            return true;
        }

        /// <summary>ルビを乗せるのに要る、字の上の余白（キャンバスの単位）</summary>
        public static float RoomAbove(Text text) => text == null ? 0f : Mathf.Ceil(text.fontSize * DefaultScale * 1.3f);

        /// <summary>タグを足した文での位置に直す（Text はタグの文字も幅0の字として番号に数える）</summary>
        private static void Remap(string plain, string shown, List<Ruby.Span> spans)
        {
            var map = new int[plain.Length + 1];
            int i = 0, j = 0;
            while (i < plain.Length && j < shown.Length)
            {
                if (shown[j] == plain[i]) { map[i++] = j++; continue; }
                if (shown[j] == '<')
                {
                    int close = shown.IndexOf('>', j);
                    j = close < 0 ? j + 1 : close + 1;
                    continue;
                }
                j++;
            }
            for (; i <= plain.Length; i++) map[i] = shown.Length;
            for (int k = 0; k < spans.Count; k++)
            {
                var sp = spans[k];
                int last = Mathf.Clamp(sp.Start + sp.Length - 1, 0, plain.Length - 1);
                sp.Start = map[Mathf.Clamp(sp.Start, 0, plain.Length)];
                sp.Length = Mathf.Max(1, map[last] + 1 - sp.Start);
                spans[k] = sp;
            }
        }
        /// <summary>ルビの色（a=0 なら本文と同じ色）</summary>
        public Color Color = new Color(0f, 0f, 0f, 0f);

        private RectTransform _layer;
        private readonly List<Text> _pool = new List<Text>();
        private readonly List<Ruby.Span> _spans = new List<Ruby.Span>();
        private int _dirty;
        private string _laidText;
        private Vector2 _laidSize;
        private int _laidFont;

        public bool HasRuby => _spans.Count > 0;

        /// <summary>Text にルビの層を付ける（層は Text の子＝文字の上に描き、Text と一緒に動く）</summary>
        public static RubyText Attach(Text text)
        {
            var rt = text.gameObject.GetComponent<RubyText>();
            if (rt != null) return rt;
            var layer = UiTheme.Rect(text.rectTransform, "Ruby");
            rt = text.gameObject.AddComponent<RubyText>();
            rt.Text = text;
            rt._layer = layer;
            rt.SyncLayer();
            return rt;
        }

        /// <summary>出すルビ（Text.text での位置）。空なら消す</summary>
        public void SetSpans(IList<Ruby.Span> spans)
        {
            _spans.Clear();
            if (spans != null) _spans.AddRange(spans);
            _dirty = 2;   // 文字の配置が決まってから
            if (_spans.Count == 0) HideAll();
        }

        /// <summary>層は Text の枠いっぱい。基準点も Text と同じにする（字の位置は Text の基準点から測るので）</summary>
        private void SyncLayer()
        {
            if (_layer == null || Text == null) return;
            _layer.anchorMin = Vector2.zero; _layer.anchorMax = Vector2.one;
            _layer.pivot = Text.rectTransform.pivot;
            _layer.offsetMin = _layer.offsetMax = Vector2.zero;
        }

        private void OnEnable() => _dirty = 2;

        private void LateUpdate()
        {
            if (Text == null || _layer == null) return;
            if (_spans.Count == 0) return;
            // 文字・枠・大きさが変わったら組み直す
            var size = Text.rectTransform.rect.size;
            if (_laidText != Text.text || _laidSize != size || _laidFont != Text.fontSize) _dirty = Mathf.Max(_dirty, 1);
            if (_dirty <= 0 || --_dirty > 0) return;
            Layout();
        }

        private void HideAll()
        {
            foreach (var l in _pool) if (l != null) l.gameObject.SetActive(false);
        }

        private void Layout()
        {
            SyncLayer();
            HideAll();
            _laidText = Text.text;
            _laidSize = Text.rectTransform.rect.size;
            _laidFont = Text.fontSize;
            var gen = Text.cachedTextGenerator;
            var chars = gen.characters;
            var lines = gen.lines;
            if (lines.Count == 0 || chars.Count == 0) { _dirty = 1; return; }   // まだ組まれていない
            string s = Text.text ?? "";
            int n = Mathf.Min(chars.Count, s.Length);
            float ppu = Text.pixelsPerUnit > 0f ? Text.pixelsPerUnit : 1f;
            int fs = Mathf.Max(8, Mathf.RoundToInt(Text.fontSize * Scale));
            int used = 0;
            foreach (var sp in _spans)
            {
                int end = Mathf.Min(sp.Start + sp.Length, n);
                if (sp.Start < 0 || sp.Start >= end) continue;
                // 行ごとに分ける
                var segs = new List<(int a, int b, int line)>();
                int li = LineOf(lines, sp.Start);
                int segStart = sp.Start;
                for (int i = sp.Start; i < end; i++)
                {
                    int l = LineOf(lines, i);
                    if (l != li) { segs.Add((segStart, i, li)); segStart = i; li = l; }
                }
                segs.Add((segStart, end, li));
                // 読みを行ごとの親文字の数で分ける
                int total = end - sp.Start, given = 0;
                for (int k = 0; k < segs.Count; k++)
                {
                    var (a, b, line) = segs[k];
                    int take = k == segs.Count - 1 ? sp.Reading.Length - given
                                                   : Mathf.RoundToInt(sp.Reading.Length * (float)(b - a) / total);
                    take = Mathf.Clamp(take, 0, sp.Reading.Length - given);
                    string part = sp.Reading.Substring(given, take);
                    given += take;
                    if (part.Length == 0) continue;
                    float x0 = chars[a].cursorPos.x / ppu;
                    float x1 = (chars[b - 1].cursorPos.x + chars[b - 1].charWidth) / ppu;
                    // 親文字の字面の上端（描いた字の頂点から測る。ルビはその少し上に乗せる）
                    float glyphTop = GlyphTop(gen.verts, chars[a].cursorPos.x, chars[b - 1].cursorPos.x + chars[b - 1].charWidth,
                                              lines[line].topY, lines[line].height) / ppu;
                    float baseW = Mathf.Max(1f, x1 - x0);
                    int size = fs;
                    float room = baseW + fs;   // 前後に半字ずつまで
                    if (part.Length * size > room) size = Mathf.Max(8, Mathf.FloorToInt(room / part.Length));
                    var label = Get(used++);
                    label.fontSize = size;
                    label.text = part;
                    label.color = Color.a > 0f ? Color : Text.color;
                    var lrt = label.rectTransform;
                    lrt.anchorMin = lrt.anchorMax = _layer.pivot;
                    lrt.pivot = new Vector2(0.5f, 0f);
                    lrt.sizeDelta = new Vector2(Mathf.Max(baseW, part.Length * size) + 4f, size * 1.4f);
                    lrt.anchoredPosition = new Vector2((x0 + x1) * 0.5f, glyphTop + size * 0.05f);
                    label.gameObject.SetActive(true);
                }
            }
        }

        /// <summary>
        /// x0〜x1 の範囲にあるその行の字の、頂点のいちばん上（生成した画素の座標）。
        /// 空白は頂点を持たないので字の番号では引けず、位置で探す。見つからなければ行の上から 14%（字の上の空き）
        /// </summary>
        private static float GlyphTop(IList<UIVertex> verts, float x0, float x1, float lineTop, float lineH)
        {
            float top = float.MinValue;
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].position;
                if (p.x < x0 - 1f || p.x > x1 + 1f || p.y > lineTop + 1f || p.y < lineTop - lineH - 1f) continue;
                if (p.y > top) top = p.y;
            }
            return top > float.MinValue ? top : lineTop - lineH * 0.14f;
        }

        private static int LineOf(IList<UILineInfo> lines, int charIdx)
        {
            int li = 0;
            while (li + 1 < lines.Count && lines[li + 1].startCharIdx <= charIdx) li++;
            return li;
        }

        private Text Get(int i)
        {
            while (_pool.Count <= i)
            {
                var t = UiTheme.Label(_layer, "Rt", 12, TextAnchor.LowerCenter, Text.color, shadow: false);
                t.font = Text.font;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.supportRichText = false;
                t.raycastTarget = false;
                _pool.Add(t);
            }
            var label = _pool[i];
            label.font = Text.font;
            return label;
        }
    }
}
