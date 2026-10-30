using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>手帳のメモのボードに置く物（言葉のカード・黒塗りの資料）。置き場所は左上の角（x は右、y は下が正）</summary>
    public abstract class BoardItem : MonoBehaviour
    {
        public abstract Vector2 Pos { get; set; }
        public abstract bool Placed { get; set; }
        public Vector2 Size => ((RectTransform)transform).rect.size;
        public Rect Rect => new Rect(Pos.x, Pos.y, Size.x, Size.y);
        public void Apply() => ((RectTransform)transform).anchoredPosition = new Vector2(Pos.x, -Pos.y);
    }

    /// <summary>
    /// 黒塗りの資料（ボードの上の1枚）。黒塗りの所が空欄（FillSlot）になっていて、言葉のカードを運んで埋める。
    /// 枠をドラッグ（Shift＋矢印・右スティック）で動かせる。解いた後は全文が読め、右クリック（Delete）でボードから外せる
    /// （資料の一覧に綴じてある）。ダブルクリック（Enter）で資料として開く。
    /// ※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    public class FillCard : BoardItem, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler,
                            ISelectHandler, IDeselectHandler
    {
        public const float Width = 640f;
        private const int Fs = 22;                 // 本文の文字の大きさ
        private const float LineH = 42f;           // 行送り（空欄の枠が入る高さ）
        private const float PadX = 28f, BodyTop = 104f, PadB = 18f;

        private static readonly Color Bg = UiTheme.Hex(0x050506);
        private static readonly Color BgOn = UiTheme.Hex(0x121110);
        private static readonly Color Frame = UiTheme.WithAlpha(UiTheme.Accent, 0.35f);
        private static readonly Color FrameOn = UiTheme.WithAlpha(UiTheme.Accent, 0.9f);
        internal static readonly Color BodyColor = Color.Lerp(UiTheme.TextSub, UiTheme.Text, 0.25f);

        public FillDocDef Def;
        public Button Button;
        public readonly List<FillSlot> Slots = new List<FillSlot>();
        private NotebookUI _owner;
        private Image _frame, _bg;
        private Text _footer;
        private bool _selected;
        private Coroutine _flash;

        public override Vector2 Pos
        {
            get { var p = FillDocs.Pos(Def); return new Vector2(p.x, p.y); }
            set { var p = FillDocs.Pos(Def); p.x = value.x; p.y = value.y; }
        }
        public override bool Placed
        {
            get => FillDocs.Pos(Def).placed;
            set => FillDocs.Pos(Def).placed = value;
        }

        public bool Solved => FillDocs.IsSolved(Def);

        public static FillCard Create(RectTransform parent, NotebookUI owner, FillDocDef def)
        {
            var go = new GameObject("Fill_" + def.EntryId);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            var frame = go.AddComponent<Image>();
            frame.color = Frame;
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = frame;
            var card = go.AddComponent<FillCard>();
            card.Def = def;
            card.Button = btn;
            card._owner = owner;
            card._frame = frame;
            card.Build();
            btn.onClick.AddListener(() =>
            {
                // Enter／A：解いた資料なら読む（空欄は空欄の方で選ぶ）
                if (card.Solved && NotebookUI.SubmitPressed()) owner.OpenFill(def);
            });
            return card;
        }

        private void Build()
        {
            var rt = (RectTransform)transform;
            bool solved = Solved;
            _bg = UiTheme.Fill(rt, "Fill", Bg);
            UiTheme.Stretch(_bg.rectTransform, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline);

            var kind = UiTheme.Label(rt, "Kind", UiTheme.FsSmall, TextAnchor.UpperLeft, solved ? UiTheme.Positive : UiTheme.Accent, shadow: false, sharp: true);
            Put(kind.rectTransform, PadX, 16f, Width - PadX * 2f, 28f);
            kind.text = solved ? "黒塗りの資料　── 埋まった（資料の一覧に綴じた）" : "黒塗りの資料";
            var title = UiTheme.Label(rt, "Title", 26, TextAnchor.UpperLeft, UiTheme.Text, display: true, shadow: false, sharp: true);
            Put(title.rectTransform, PadX, 46f, Width - PadX * 2f, 38f);
            title.text = Def.TitleText;
            var rule = UiTheme.Fill(rt, "Rule", UiTheme.WithAlpha(UiTheme.Accent, 0.6f));
            Put(rule.rectTransform, PadX, 92f, 90f, UiTheme.Hairline);

            // 本文：行ごとに、地の文と空欄を左から並べる（空欄の幅は答えか置いた言葉の長い方。行に入るだけ広げる）
            float maxEm = (Width - PadX * 2f) / Fs;
            float y = BodyTop;
            foreach (var line in Lines(Def.TemplateText, maxEm, (i, answer) => SlotEm(i, answer, maxEm)))
            {
                if (line.Count == 0) { y += LineH * 0.45f; continue; }
                float x = PadX;
                foreach (var part in line)
                {
                    if (part.Blank)
                    {
                        var slot = FillSlot.Create(rt, this, part.Index, part.Text, Fs, LineH, SlotEm(part.Index, part.Text, maxEm));
                        var srt = (RectTransform)slot.transform;
                        srt.anchoredPosition = new Vector2(x, -(y + (LineH - srt.sizeDelta.y) * 0.5f));
                        x += srt.sizeDelta.x + 4f;
                        Slots.Add(slot);
                        continue;
                    }
                    var t = UiTheme.Label(rt, "Text", Fs, TextAnchor.MiddleLeft, BodyColor, shadow: false, sharp: true);
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    t.text = part.Text;
                    float w = t.preferredWidth;
                    Put(t.rectTransform, x, y, w + 2f, LineH);
                    x += w;
                }
                y += LineH;
            }

            _footer = UiTheme.Label(rt, "Footer", UiTheme.FsSmall, TextAnchor.UpperLeft, UiTheme.TextSub, shadow: false, sharp: true);
            Put(_footer.rectTransform, PadX, y + 14f, Width - PadX * 2f, 30f);
            _footer.text = solved ? "右クリック／Delete でボードから外す　　ダブルクリック／Enter で読む"
                                  : "切り取った言葉のカードを、黒塗りの所へ運ぶ　　全部埋めると確かめる";
            rt.sizeDelta = new Vector2(Width, y + 14f + 30f + PadB);
            Slots.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        private static void Put(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        /// <summary>
        /// 空欄の幅（字数）。答えの長さ（2〜12字）と、置いてある言葉の長さの大きい方。
        /// 置いた言葉が長ければ空欄を広げて全文を見せる（1行に入らない長さなら1行いっぱいにし、字を小さくして入れる）
        /// </summary>
        private int SlotEm(int index, string answer, float maxEm)
        {
            float em = Mathf.Clamp(Mathf.Ceil(FillDocs.Em(answer)), 2f, 12f);
            var placed = Solved ? null : FillDocs.InSlot(Def, index);
            if (placed != null) em = Mathf.Max(em, Mathf.Ceil(FillDocs.Em(placed.text) * (Fs - 2f) / Fs + 0.6f));
            return Mathf.Clamp(Mathf.RoundToInt(em), 2, Mathf.FloorToInt(maxEm - 0.6f));
        }

        /// <summary>
        /// 文面を行に分ける。書いてある改行のほか、枠に入らない行は日本語の切れ目で折り返す（空欄の途中では切らない）。
        /// slotEm(空欄の通し番号, 答え) = その空欄の幅（字数）
        /// </summary>
        private static List<List<FillPart>> Lines(string template, float maxEm, System.Func<int, string, int> slotEm)
        {
            var lines = new List<List<FillPart>>();
            int global = 0;   // 空欄の資料全体の通し番号（行ごとに Parse すると 0 から振り直されるので数え直す）
            foreach (var raw in template.Split('\n'))
            {
                var parts = FillDocs.Parse(raw);
                // 空欄を私用領域の文字（空欄の幅のぶん並べる）に置き換えて折り返し、元に戻す
                var flat = new System.Text.StringBuilder();
                var answers = new Dictionary<int, FillPart>();
                int localBlank = 0;
                foreach (var p in parts)
                {
                    if (!p.Blank) { flat.Append(p.Text); continue; }
                    var gp = p;
                    gp.Index = global;
                    answers[localBlank] = gp;
                    flat.Append((char)(0xE000 + localBlank), slotEm(global, p.Text));
                    localBlank++;
                    global++;
                }
                foreach (var row in JaLineBreak.Wrap(flat.ToString(), maxEm).Split('\n'))
                {
                    var line = new List<FillPart>();
                    var sb = new System.Text.StringBuilder();
                    int last = -1;
                    foreach (char c in row)
                    {
                        if (c >= 0xE000 && c < 0xE100)
                        {
                            int k = c - 0xE000;
                            if (k == last) continue;
                            if (sb.Length > 0) { line.Add(new FillPart { Text = sb.ToString() }); sb.Clear(); }
                            line.Add(answers[k]);
                            last = k;
                            continue;
                        }
                        last = -1;
                        sb.Append(c);
                    }
                    if (sb.Length > 0) line.Add(new FillPart { Text = sb.ToString() });
                    lines.Add(line);
                }
            }
            return lines;
        }

        /// <summary>画面の位置にある空欄（解いた資料には無い）</summary>
        public FillSlot SlotAt(Vector2 screen, Camera cam)
        {
            if (Solved) return null;
            foreach (var s in Slots)
                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)s.transform, screen, cam)) return s;
            return null;
        }

        /// <summary>判定で違った時：枠を赤く光らせる（どの空欄が違ったかは見せない）</summary>
        public void FlashWrong()
        {
            if (_flash != null) StopCoroutine(_flash);
            _flash = StartCoroutine(Flash());
        }

        private IEnumerator Flash()
        {
            for (float t = 0f; t < 0.9f; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Abs(Mathf.Sin(t / 0.9f * Mathf.PI * 2f));
                _frame.color = Color.Lerp(_selected ? FrameOn : Frame, UiTheme.Danger, k);
                yield return null;
            }
            _frame.color = _selected ? FrameOn : Frame;
            _flash = null;
        }

        public void OnSelect(BaseEventData e) { _selected = true; _frame.color = FrameOn; _bg.color = BgOn; }
        public void OnDeselect(BaseEventData e) { _selected = false; _frame.color = Frame; _bg.color = Bg; }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _owner.BeginDrag(this, e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _owner.DragItem(this, e);
        }

        public void OnEndDrag(PointerEventData e) { }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) _owner.RemoveFill(this);
            else if (e.button == PointerEventData.InputButton.Left && e.clickCount >= 2 && Solved) _owner.OpenFill(Def);
        }
    }

    /// <summary>
    /// 黒塗りの資料の空欄1つ。言葉のカードをドロップして置く。クリック（Enter／A）で言葉を選ぶ窓を開き、
    /// 右クリック（Delete／Y）で置いたカードをボードへ戻す。置いた言葉はドラッグで引き出せる
    /// （ボードに放せば外れ、別の空欄に放せば移る）。空の空欄をドラッグすると資料ごと動く。
    /// 解いた後は答えを白く浮き上がらせて見せるだけ
    /// </summary>
    public class FillSlot : MonoBehaviour, IPointerClickHandler, ISelectHandler, IDeselectHandler,
                            IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private static readonly Color Bar = UiTheme.Hex(0x2A2723);          // 黒塗り（黒い地の上で見える濃い灰）
        private static readonly Color BarFilled = UiTheme.Hex(0x16140F);
        private static readonly Color Edge = UiTheme.WithAlpha(UiTheme.Text, 0.14f);
        private static readonly Color EdgeOn = UiTheme.WithAlpha(UiTheme.Accent, 0.95f);

        public FillCard Card;
        public int Index;
        public string Answer;
        public Button Button;
        private Image _edge, _fill;
        private Text _label;
        private bool _selected, _hover, _dragWord;

        public MemoSnippet Placed => FillDocs.InSlot(Card.Def, Index);

        public static FillSlot Create(RectTransform parent, FillCard card, int index, string answer, int fs, float lineH, int em)
        {
            var go = new GameObject($"Slot_{index}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(em * fs + 14f, lineH - 8f);
            var edge = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = edge;
            var fill = UiTheme.Fill(rt, "Fill", Bar);
            UiTheme.Stretch(fill.rectTransform, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline);
            var label = UiTheme.Label(rt, "Label", fs - 2, TextAnchor.MiddleCenter, Color.white, shadow: false, sharp: true);
            UiTheme.Stretch(label.rectTransform, 4f, 4f, 0f, 0f);
            // 空欄はふつう言葉に合わせて広がる。1行に入らない長い言葉だけ、字を小さくし折り返して全文を入れる
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 11;
            label.resizeTextMaxSize = fs - 2;
            label.lineSpacing = 0.95f;

            var slot = go.AddComponent<FillSlot>();
            slot.Card = card;
            slot.Index = index;
            slot.Answer = answer;
            slot.Button = btn;
            slot._edge = edge;
            slot._fill = fill;
            slot._label = label;
            btn.onClick.AddListener(() =>
            {
                if (!card.Solved && NotebookUI.SubmitPressed()) NotebookUI.Instance?.OpenPicker(slot);
            });
            slot.Refresh();
            return slot;
        }

        public void Refresh()
        {
            if (Card.Solved)
            {
                // 解いた：黒塗りが消え、答えが白く浮き上がる
                _edge.color = new Color(0f, 0f, 0f, 0f);
                _fill.color = new Color(0f, 0f, 0f, 0f);
                _label.text = Answer;
                _label.alignment = TextAnchor.MiddleLeft;
                _label.color = Color.white;
                var sh = _label.GetComponent<Shadow>();
                if (sh == null) sh = _label.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
                sh.effectDistance = new Vector2(1f, -1.5f);
                Button.interactable = false;
                return;
            }
            var s = Placed;
            _fill.color = s != null ? BarFilled : Bar;
            _label.text = s != null ? s.text : "";   // 全文（空欄の幅は言葉に合わせてある）
            _label.color = Color.white;
            UpdateEdge();
        }

        /// <summary>置いた言葉をドラッグで引き出している間は、空欄の文字を薄くする</summary>
        public void SetLifted(bool on)
        {
            if (Card.Solved) return;
            _label.color = on ? UiTheme.WithAlpha(Color.white, 0.25f) : Color.white;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            var ui = NotebookUI.Instance;
            if (ui == null) return;
            _dragWord = !Card.Solved && Placed != null;
            if (_dragWord) ui.BeginSlotDrag(this, e);
            else ui.BeginDrag(Card, e);   // 空の空欄・解いた資料は資料ごと動かす
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            var ui = NotebookUI.Instance;
            if (ui == null) return;
            if (_dragWord) ui.DragSlotWord(this, e);
            else ui.DragItem(Card, e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !_dragWord) return;
            _dragWord = false;
            NotebookUI.Instance?.EndSlotDrag(this, e);
        }

        private void UpdateEdge() => _edge.color = _selected || _hover ? EdgeOn : Edge;

        /// <summary>ドラッグ中のカードがこの上にある</summary>
        public void SetHover(bool on) { _hover = on; if (!Card.Solved) UpdateEdge(); }

        public void OnSelect(BaseEventData e) { _selected = true; if (!Card.Solved) UpdateEdge(); }
        public void OnDeselect(BaseEventData e) { _selected = false; if (!Card.Solved) UpdateEdge(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (Card.Solved) return;
            if (e.button == PointerEventData.InputButton.Right) NotebookUI.Instance?.Unplace(this);
            else if (e.button == PointerEventData.InputButton.Left) NotebookUI.Instance?.OpenPicker(this);
        }

        /// <summary>空欄の幅に入るだけ見せる（入らなければ末尾を … にする）</summary>
        public static string Truncate(string s, float maxEm)
        {
            if (FillDocs.Em(s) <= maxEm) return s;
            var sb = new System.Text.StringBuilder();
            float w = 1f;   // … の分
            foreach (char c in s)
            {
                w += JaLineBreak.Em(c);
                if (w > maxEm) break;
                sb.Append(c);
            }
            return sb.Append('…').ToString();
        }
    }
}
