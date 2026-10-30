using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// 手帳（Tab）。2つの使い方に分かれている。
    /// ・資料 … 見つけた資料の一覧（章ごと）。選ぶと調べる画面で実物を回して読める。音声記録は再生できる
    /// ・メモ … 資料の本文にラインマーカーを引いて切り取った文のボード。最初は一覧のように並び、
    ///          ドラッグ（Shift＋矢印・右スティック）で上下左右に自由に動かせる。ほかのカードにぶつかると
    ///          重ならずにその境目で止まり、横に貼り付く（ぶつかられた方は動かない）。
    ///          右クリック（Delete）で消す。ダブルクリック（Enter）で引用元の資料の文章をボードの右に開く
    ///          （書き写した所が白く浮き上がり、その位置まで送られる）。R で一覧の並びに戻す。
    ///          読んでいる資料の画面からも直接開ける（ShowOverlay。閉じるとその資料へ戻る）
    /// 付箋（装置の誤答で立つ目印）は資料の一覧に出る。
    /// ※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    public class NotebookUI : MonoBehaviour
    {
        public static NotebookUI Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._shown;

        private enum Tab { Docs, Memo }
        private static readonly string[] ChapterNames = { "序", "1章", "2章", "3章", "終章" };

        /// <summary>残響Id → それを見た部屋（章分けに使う）</summary>
        private static readonly Dictionary<string, string> EchoRoom = new Dictionary<string, string>
        {
            { "study_secret", "study" }, { "argue_saeki", "saeki_home" }, { "saeki_wife", "saeki_home" },
            { "talk_me", "ward" }, { "mizuno_uncle", "mizuno_apart" }, { "talk_mizuno", "mizuno_apart" },
            { "argue_kuroda", "data_room" }, { "kuroda_family", "kuroda_home" },
        };

        private RectTransform _book;
        private Tab _tab = Tab.Docs;
        private int _chapter;
        private bool _shown, _dirty;
        private Button _tabDocs, _tabMemo;
        private Text _tabDocsLabel, _tabMemoLabel, _guide, _memoHint;
        private Image _tabMark;
        private GameObject _docsPage, _memoPage;
        private RectTransform _chapterCol, _docsContent, _memoContent;
        private ScrollRect _docsScroll, _memoScroll;
        private readonly List<Button> _chapterButtons = new List<Button>();
        private readonly List<Selectable> _rows = new List<Selectable>();
        private readonly List<SnippetCard> _cards = new List<SnippetCard>();
        private readonly List<FillCard> _fills = new List<FillCard>();
        private readonly List<BoardItem> _items = new List<BoardItem>();   // ボードの上の物すべて（ぶつかる相手）
        private string _reselectEntry;
        private int _reselectSnippet = -1;
        private string _reselectSlotDoc;         // 描き直した後に選ぶ空欄（黒塗りの資料のエントリId と番号）
        private int _reselectSlot = -1;
        private string _flashDoc;                // 描き直した後に赤く光らせる黒塗りの資料（判定で違った）

        // ---- 資料の画面から開いた時（メモだけを上に重ねる） ----
        private bool _overlay, _ovPrevShown, _ovPrevBook;
        private Tab _ovPrevTab;
        private Action _ovClosed;
        private int _ovOpenedFrame = -1;

        // ---- ボード ----
        private const float CardMaxW = 460f, CardMinW = 120f, BoardGap = 12f, BoardMaxY = 4000f;
        private const float MoveSpeed = 700f;   // キー・スティックで動かす速さ（/秒）
        private Vector2 _grab;                  // ドラッグ：つかんだ所とカードの左上の差
        private Vector2 _dragStart;             // ドラッグを始めた時の置き場所（空欄に落とした時に戻す）
        private FillSlot _dragHover;            // ドラッグ中のカードの下にある空欄
        private SnippetCard _ghost;             // 空欄から引き出している言葉（放すまでの見た目だけ。ボードの物には数えない）
        private Vector2 _ghostGrab;             // 引き出した言葉：つかんだ所とカードの左上の差
        // 拡大・縮小（ホイール。ポインターの下の所を動かさずに寄る・引く）
        private const float ZoomMin = 0.5f, ZoomMax = 1.6f, ZoomStep = 1.12f;   // 上げすぎると文字がにじむ
        private float _zoom = 1f;
        private Text _zoomLabel;

        // ---- 言葉を選ぶ窓（空欄を選んで決定。パッド・キーボード用） ----
        private GameObject _picker;
        private Text _pickerTitle, _pickerEmpty;
        private RectTransform _pickerContent;
        private ScrollRect _pickerScroll;
        private FillSlot _pickerSlot;
        private int _pickerClosedFrame = -1;

        // ---- 引用元の文章ウィンドウ（カードをダブルクリック） ----
        private GameObject _docWin;
        private Text _docTitle, _docBody;
        private RubyText _docRuby;
        private readonly List<Ruby.Span> _docSpans = new List<Ruby.Span>();
        private ScrollRect _docScroll;
        private const float DocWinH = 700f, DocViewTop = 120f, DocViewBottom = 84f;
        private const float DocLineSpacing = 1.45f, DocLineSpacingRuby = 1.75f;   // ルビのある文は行間を広げる
        private MarkerGlyphs _docGlyphs;
        private MemoSnippet _docSnippet;
        private int _docClosedFrame = -1;

        /// <summary>引用元の文章ウィンドウが Esc を受けている（手帳ごと閉じない）</summary>
        public static bool DocWindowBusy => Instance != null &&
            ((Instance._docWin != null && Instance._docWin.activeSelf) || Instance._docClosedFrame == Time.frameCount ||
             (Instance._picker != null && Instance._picker.activeSelf) || Instance._pickerClosedFrame == Time.frameCount);

        /// <summary>手帳を閉じる時（HUD が面倒を見る）</summary>
        public Action RequestClose;

        // ============================== 構築 ==============================

        public void Init(RectTransform book)
        {
            Instance = this;
            _book = book;

            // 見出しのタブ（左上）
            _tabDocs = TabButton(book, "資料", new Vector2(40f, -26f), () => SetTab(Tab.Docs), out _tabDocsLabel);
            _tabMemo = TabButton(book, "メモ", new Vector2(200f, -26f), () => SetTab(Tab.Memo), out _tabMemoLabel);
            _tabMark = UiTheme.Fill(book, "TabMark", UiTheme.Accent);
            _tabMark.rectTransform.anchorMin = _tabMark.rectTransform.anchorMax = new Vector2(0f, 1f);
            _tabMark.rectTransform.pivot = new Vector2(0f, 0.5f);
            var rule = UiTheme.Fill(book, "HeaderRule", UiTheme.WithAlpha(UiTheme.TextSub, 0.25f));
            rule.rectTransform.anchorMin = new Vector2(0f, 1f); rule.rectTransform.anchorMax = new Vector2(1f, 1f);
            rule.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rule.rectTransform.anchoredPosition = new Vector2(0f, -84f);
            rule.rectTransform.sizeDelta = new Vector2(-64f, UiTheme.Hairline);

            BuildDocsPage(book);
            BuildMemoPage(book);
            BuildDocWindow(book);
            BuildPicker(book);

            _guide = UiTheme.Label(book, "Guide", UiTheme.FsSmall, TextAnchor.LowerCenter, UiTheme.TextSub, shadow: false);
            UiTheme.Place(_guide.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(1400f, 30f));

            Notebook.OnChanged += MarkDirty;
            MemoSnippets.OnChanged += MarkDirty;
            FillDocs.OnChanged += MarkDirty;
            SetTab(Tab.Docs);
        }

        private void OnDestroy()
        {
            Notebook.OnChanged -= MarkDirty;
            MemoSnippets.OnChanged -= MarkDirty;
            FillDocs.OnChanged -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        private void MarkDirty() => _dirty = true;

        private Button TabButton(RectTransform parent, string label, Vector2 pos, Action onClick, out Text text)
        {
            var b = UiTheme.MenuItem(parent, label, onClick, 150f, 52f, 30);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            text = b.GetComponentInChildren<Text>();
            text.font = UiTheme.DisplayFont;
            return b;
        }

        private void BuildDocsPage(RectTransform book)
        {
            _docsPage = UiTheme.Rect(book, "DocsPage").gameObject;
            var page = (RectTransform)_docsPage.transform;
            UiTheme.Stretch(page, 32f, 32f, 100f, 60f);

            // 章（左の列）
            _chapterCol = UiTheme.Rect(page, "Chapters");
            _chapterCol.anchorMin = new Vector2(0f, 0f); _chapterCol.anchorMax = new Vector2(0f, 1f);
            _chapterCol.pivot = new Vector2(0f, 1f);
            _chapterCol.anchoredPosition = Vector2.zero;
            _chapterCol.sizeDelta = new Vector2(200f, 0f);
            var v = _chapterCol.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = UiTheme.Sp1; v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = v.childForceExpandHeight = false;
            for (int i = 0; i < ChapterNames.Length; i++)
            {
                int ch = i;
                _chapterButtons.Add(UiTheme.MenuItem(_chapterCol, ChapterNames[i], () => SetChapter(ch), 200f, 50f, 26));
            }
            var colRule = UiTheme.Fill(page, "ColRule", UiTheme.WithAlpha(UiTheme.TextSub, 0.2f));
            colRule.rectTransform.anchorMin = new Vector2(0f, 0f); colRule.rectTransform.anchorMax = new Vector2(0f, 1f);
            colRule.rectTransform.pivot = new Vector2(0f, 0.5f);
            colRule.rectTransform.anchoredPosition = new Vector2(216f, 0f);
            colRule.rectTransform.sizeDelta = new Vector2(UiTheme.Hairline, 0f);

            _docsScroll = MakeScroll(page, new Vector2(236f, 0f), out _docsContent);
        }

        private void BuildMemoPage(RectTransform book)
        {
            _memoPage = UiTheme.Rect(book, "MemoPage").gameObject;
            var page = (RectTransform)_memoPage.transform;
            UiTheme.Stretch(page, 32f, 32f, 100f, 60f);
            // ボード：カードを自由に置く（並びはレイアウトに任せない）。下へはみ出したらホイールで送る
            var view = UiTheme.Fill(page, "Board", new Color(0f, 0f, 0f, 0f), raycast: true);
            UiTheme.Stretch(view.rectTransform);
            view.gameObject.AddComponent<RectMask2D>();
            _memoContent = UiTheme.Rect(view.rectTransform, "Content");
            _memoContent.anchorMin = new Vector2(0f, 1f); _memoContent.anchorMax = new Vector2(1f, 1f);
            _memoContent.pivot = new Vector2(0f, 1f);
            _memoContent.anchoredPosition = Vector2.zero;
            _memoContent.sizeDelta = Vector2.zero;
            _memoScroll = view.gameObject.AddComponent<ScrollRect>();
            _memoScroll.viewport = view.rectTransform;
            _memoScroll.content = _memoContent;
            _memoScroll.horizontal = true;    // 拡大した時は横にも送る（背景をドラッグ）
            _memoScroll.movementType = ScrollRect.MovementType.Clamped;
            _memoScroll.scrollSensitivity = 0f;   // ホイールは拡大・縮小（ZoomByWheel）
            _memoScroll.inertia = false;

            _zoomLabel = UiTheme.Label(page, "Zoom", UiTheme.FsSmall, TextAnchor.LowerRight, UiTheme.TextSub, shadow: false);
            UiTheme.Place(_zoomLabel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 27f), new Vector2(760f, 26f));   // タブの下の罫より上
            UpdateZoomLabel();

            _memoHint = UiTheme.Label(page, "Empty", UiTheme.FsBody, TextAnchor.MiddleCenter, UiTheme.TextSub, shadow: false);
            UiTheme.Stretch(_memoHint.rectTransform);
            _memoHint.lineSpacing = 1.6f;
            _memoHint.text = "まだ何も書き写していない。\n資料を開いて「読む」と、本文をなぞってラインマーカーを引ける。\n引いた文はここに並び、自由に動かして考えをまとめられる。";
        }

        private static ScrollRect MakeScroll(RectTransform page, Vector2 leftTop, out RectTransform content)
        {
            var view = UiTheme.Fill(page, "View", new Color(0f, 0f, 0f, 0f), raycast: true);
            var vrt = view.rectTransform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.pivot = new Vector2(0f, 1f);
            vrt.offsetMin = new Vector2(leftTop.x, 0f);
            vrt.offsetMax = new Vector2(0f, -leftTop.y);
            view.gameObject.AddComponent<RectMask2D>();
            content = UiTheme.Rect(vrt, "Content");
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = UiTheme.Sp2;
            v.padding = new RectOffset(0, 16, 4, 16);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.viewport = vrt;
            sr.content = content;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            sr.inertia = false;
            return sr;
        }

        // ============================== 表示 ==============================

        /// <summary>開く（今いる部屋の章から）</summary>
        public void Show()
        {
            _shown = true;
            _book.gameObject.SetActive(true);
            int cur = StoryScript.ChapterOf(LoopRooms.CurrentRoomId);
            if (cur >= 0) _chapter = cur;
            Rebuild();
            FocusFirst();
        }

        public void Hide()
        {
            _shown = false;
            HideDoc();
            HidePicker(false);
            SetNavigation(true);
        }

        /// <summary>
        /// 資料の画面から「メモ」だけを開く（資料の一覧には切り替えない）。閉じると onClosed が呼ばれ、
        /// 手帳を開いていた時は元のタブ・表示に戻る
        /// </summary>
        public void ShowOverlay(Action onClosed)
        {
            if (_overlay) return;
            _ovPrevShown = _shown;
            _ovPrevBook = _book.gameObject.activeSelf;
            _ovPrevTab = _tab;
            _ovClosed = onClosed;
            _overlay = true;
            _ovOpenedFrame = Time.frameCount;
            _shown = true;
            _book.gameObject.SetActive(true);
            _tabDocs.interactable = false;
            // 今読んでいる資料のメモがあれば、それを選んでおく
            var cur = InspectView.Instance != null ? InspectView.Instance.CurrentEntryId : null;
            var mine = cur != null ? MemoSnippets.ForEntry(cur) : null;
            if (mine != null && mine.Count > 0) _reselectSnippet = mine[mine.Count - 1].id;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            SetTab(Tab.Memo);
        }

        public void CloseOverlay()
        {
            if (!_overlay) return;
            _overlay = false;
            _tabDocs.interactable = true;
            HideDoc();
            HidePicker(false);
            SetNavigation(true);
            _shown = _ovPrevShown;
            SetTab(_ovPrevTab);   // 元のタブへ（手帳を開いていなかった時は選択はしない）
            _book.gameObject.SetActive(_ovPrevBook);
            UiSound.Cancel();
            var cb = _ovClosed;
            _ovClosed = null;
            cb?.Invoke();
        }

        private void SetTab(Tab t)
        {
            _tab = t;
            if (t != Tab.Memo) { HideDoc(); HidePicker(false); }
            _docsPage.SetActive(t == Tab.Docs);
            _memoPage.SetActive(t == Tab.Memo);
            SetNavigation(true);
            var active = t == Tab.Docs ? _tabDocs : _tabMemo;
            var art = (RectTransform)active.transform;
            _tabMark.rectTransform.anchoredPosition = new Vector2(art.anchoredPosition.x + 28f, -80f);
            _tabMark.rectTransform.sizeDelta = new Vector2(60f, 3f);
            _tabDocsLabel.fontStyle = t == Tab.Docs ? FontStyle.Bold : FontStyle.Normal;
            _tabMemoLabel.fontStyle = t == Tab.Memo ? FontStyle.Bold : FontStyle.Normal;
            Rebuild();
            if (_shown) FocusFirst();
        }

        private void SetChapter(int ch)
        {
            _chapter = Mathf.Clamp(ch, 0, ChapterNames.Length - 1);
            Rebuild();
        }

        private void Update()
        {
            if (!_shown) return;
            if (_dirty) { _dirty = false; Rebuild(); }
            HandleKeys();
        }

        private void Rebuild()
        {
            if (_tab == Tab.Docs) RebuildDocs(); else RebuildMemo();
            _tabMemoLabel.text = MemoSnippets.Count > 0 ? $"メモ <size=20><color={UiTheme.Rgb(UiTheme.TextSub)}>{MemoSnippets.Count}</color></size>" : "メモ";
            UpdateGuide();
        }

        private void UpdateGuide()
        {
            if (_tab == Tab.Docs)
            {
                _guide.text = $"{UiTheme.Key("Enter", "A")} 調べる　　{UiTheme.Key("Q", "LB")}{UiTheme.Key("E", "RB")} 資料／メモ　　{UiTheme.Key("Tab", "View")} 閉じる";
                return;
            }
            if (_picker != null && _picker.activeSelf)
            {
                _guide.text = $"{UiTheme.Key("Enter", "A")} 空欄に入れる　　{UiTheme.Key("Esc", "B")} やめる";
                return;
            }
            string fill = _fills.Count > 0 ? $"空欄へドラッグ／空欄で{UiTheme.Key("Enter", "A")} 言葉を入れる　　" : "";
            string tail = _overlay
                ? $"{UiTheme.Key("M", "RB")}{UiTheme.Key("Esc", "B")} 資料に戻る"
                : $"{UiTheme.Key("Q", "LB")}{UiTheme.Key("E", "RB")} 資料／メモ　　{UiTheme.Key("Tab", "View")} 閉じる";
            _guide.text = $"ドラッグ／{UiTheme.Key("Shift＋矢印", "右スティック")} 動かす　　{fill}右クリック／{UiTheme.Key("Delete", "Y")} 消す　　ダブルクリック／{UiTheme.Key("Enter", "A")} 引用元を見る　　{UiTheme.Key("R", "X")} 並べ直す　　{tail}";
        }

        // ---- 資料の一覧 ----

        private struct Row
        {
            public NotebookEntry Entry;
            public string Room, Doc;
            public string Kind;     // 種別の表示（書類・テープ…）
            public int Order;
            public FillDocDef Fill; // 黒塗りのまま（まだ埋まっていない）の資料。開くと黒塗りの本文を読める（線は引けない）
        }

        private List<Row> RowsFor(int chapter, out int[] counts)
        {
            counts = new int[ChapterNames.Length];
            var rows = new List<Row>();
            var entries = Notebook.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (!Classify(e, out var row)) continue;
                int ch = StoryScript.ChapterOf(row.Room);
                if (e.id.StartsWith("finale_")) ch = ChapterNames.Length - 1;
                if (ch < 0) continue;
                counts[ch]++;
                if (ch != chapter) continue;
                row.Order = StoryScript.RoomOrderIndex(row.Room) * 1000 + i;
                rows.Add(row);
            }
            // 黒塗りの資料：見つけてまだ埋まっていないものも並べる（埋まると全文が綴じられ、ふつうの資料として並ぶ）
            foreach (var d in FillDocs.All)
            {
                if (!FillDocs.IsFound(d) || FillDocs.IsSolved(d) || Notebook.Contains(d.EntryId)) continue;
                int ch = StoryScript.ChapterOf(d.RoomId);
                if (ch < 0) continue;
                counts[ch]++;
                if (ch != chapter) continue;
                rows.Add(new Row
                {
                    Entry = new NotebookEntry { id = d.EntryId, title = d.TitleText, body = FillDocs.Redacted(d) },
                    Room = d.RoomId, Doc = d.Id, Kind = "黒塗り", Fill = d,
                    Order = StoryScript.RoomOrderIndex(d.RoomId) * 1000 + 999,
                });
            }
            rows.Sort((a, b) => a.Order.CompareTo(b.Order));
            return rows;
        }

        /// <summary>手帳エントリ → 一覧の行。警報・扉の記録は出さない</summary>
        private static bool Classify(NotebookEntry e, out Row row)
        {
            row = new Row { Entry = e };
            if (e == null || string.IsNullOrEmpty(e.id)) return false;
            if (e.id.StartsWith("attack_") || e.id.StartsWith("unlock_")) return false;
            if (e.id.StartsWith("echo_"))
            {
                row.Room = EchoRoom.TryGetValue(e.id.Substring(5), out var er) ? er : "dim";
                row.Kind = "残響";
                return true;
            }
            if (e.id.StartsWith("finale_")) { row.Room = "core_main"; row.Kind = "記録"; return true; }
            if (!DocCatalog.TryParseEntry(e.id, out var room, out var doc)) return false;
            row.Room = room; row.Doc = doc;
            var info = DocCatalog.Get(room, doc);
            row.Kind = doc == "toy" ? "断片" : info.Audio ? "音声" : KindLabel(info.Kind);
            return true;
        }

        private static string KindLabel(InspectKind k)
        {
            switch (k)
            {
                case InspectKind.Letter: return "手紙";
                case InspectKind.Notebook: return "手記";
                case InspectKind.Card: return "証";
                case InspectKind.Photo: return "写真";
                case InspectKind.Newspaper: return "新聞";
                case InspectKind.Monitor: return "画面";
                case InspectKind.Poster: return "広告";
                case InspectKind.Drawing: return "絵";
                case InspectKind.Helmet: return "装置";
                case InspectKind.None: return "記録";
                default: return "書類";
            }
        }

        private void RebuildDocs()
        {
            var rows = RowsFor(_chapter, out var counts);
            for (int i = 0; i < _chapterButtons.Count; i++)
            {
                var label = _chapterButtons[i].GetComponentInChildren<Text>();
                label.text = counts[i] > 0 ? $"{ChapterNames[i]}  <size=20><color={UiTheme.Rgb(UiTheme.TextSub)}>{counts[i]}</color></size>" : ChapterNames[i];
                _chapterButtons[i].interactable = true;
                label.fontStyle = i == _chapter ? FontStyle.Bold : FontStyle.Normal;
            }
            Clear(_docsContent);
            _rows.Clear();
            if (rows.Count == 0)
            {
                var empty = UiTheme.Label(_docsContent, "Empty", UiTheme.FsBody, TextAnchor.UpperLeft, UiTheme.TextSub, shadow: false);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 60f;
                empty.text = "この章の資料は、まだ見つけていない。";
                return;
            }
            string lastRoom = null;
            foreach (var r in rows)
            {
                if (r.Room != lastRoom)
                {
                    lastRoom = r.Room;
                    var head = UiTheme.Label(_docsContent, "Room", UiTheme.FsSmall, TextAnchor.LowerLeft, UiTheme.TextSub, shadow: false);
                    head.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
                    var room = LoopRooms.Get(r.Room);
                    head.text = "── " + (room != null ? room.Name : r.Room);
                }
                _rows.Add(DocRow(r));
            }
            UiTheme.ChainVertical(_rows);
            if (_reselectEntry != null)
            {
                foreach (var s in _rows)
                    if (s.name == "Row_" + _reselectEntry) { Select(s); break; }
                _reselectEntry = null;
            }
        }

        private Selectable DocRow(Row r)
        {
            var btn = UiTheme.MenuItem(_docsContent, r.Entry.title ?? r.Entry.id, () => OpenEntry(r), 1100f, 54f, 26);
            btn.name = "Row_" + r.Entry.id;
            var label = btn.GetComponentInChildren<Text>();
            label.rectTransform.offsetMin = new Vector2(118f, 0f);
            label.rectTransform.offsetMax = new Vector2(-380f, 0f);
            btn.GetComponent<UiItemFx>().Rebase();
            // 種別（左）
            var kind = UiTheme.Label(btn.transform, "Kind", UiTheme.FsSmall, TextAnchor.MiddleLeft, UiTheme.TextSub, shadow: false);
            UiTheme.Place(kind.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(84f, 40f));
            kind.text = r.Kind;
            // 目印（右）：付箋・未再生・マーカーの数
            var marks = new System.Text.StringBuilder();
            if (Notebook.IsFlagged(r.Entry.id)) marks.Append($"<color={UiTheme.Rgb(UiTheme.Danger)}>付箋</color>　");
            if (r.Fill != null)
            {
                int blanks = FillDocs.Answers(r.Fill).Count, filled = 0;
                for (int i = 0; i < blanks; i++) if (FillDocs.InSlot(r.Fill, i) != null) filled++;
                marks.Append($"<color={UiTheme.Rgb(UiTheme.Accent)}>空欄 {filled}/{blanks}</color>　");
            }
            if (r.Doc != null && DocCatalog.IsAudio(r.Room, r.Doc) && !DocState.Heard(r.Entry.id))
                marks.Append($"<color={UiTheme.Rgb(UiTheme.Accent)}>未再生</color>　");
            int m = MemoSnippets.ForEntry(r.Entry.id).Count;
            if (m > 0) marks.Append($"マーカー {m}");
            var mk = UiTheme.Label(btn.transform, "Marks", UiTheme.FsSmall, TextAnchor.MiddleRight, UiTheme.TextSub, shadow: false);
            UiTheme.Place(mk.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(360f, 40f));
            mk.text = marks.ToString();
            return btn;
        }

        /// <summary>一覧から資料を開く（調べる画面へ。閉じたら一覧へ戻る）</summary>
        private void OpenEntry(Row r, bool read = false)
        {
            var iv = InspectView.Instance;
            if (iv == null) return;
            var req = BuildRequest(r, read);
            _book.gameObject.SetActive(false);
            _reselectEntry = r.Entry.id;
            req.FromNotebook = true;
            req.OnClosed = all => { if (all) RequestClose?.Invoke(); else { _book.gameObject.SetActive(true); Rebuild(); FocusFirst(); } };
            iv.Open(req);
        }

        /// <summary>手帳の行 → 調べる画面を開く内容（戻り先は呼ぶ側で決める）</summary>
        private InspectView.Request BuildRequest(Row r, bool read)
        {
            var info = r.Doc != null ? DocCatalog.Get(r.Room, r.Doc)
                                     : new DocInfo { Kind = InspectKind.Notebook, Ink = InkStyle.Hand, Scale = 1f, Tint = Color.white };
            // 残響・記録（主人公が手帳に書き留めたもの）は手帳のページとして見せる
            if (r.Doc != null && info.Kind == InspectKind.None && r.Doc != "toy")
                info = new DocInfo { Kind = InspectKind.Notebook, Ink = InkStyle.Hand, Scale = 1f, Tint = Color.white };
            AudioClip[] clips = null;
            if (r.Doc != null && info.Audio)
                foreach (var f in FindObjectsByType<LoopFindable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (f.RoomId == r.Room && f.Id == r.Doc)
                    {
                        var rec = f.GetComponent<AudioRecord>();
                        if (rec != null) clips = rec.Clips;
                        break;
                    }
            return new InspectView.Request
            {
                EntryId = r.Entry.id, RoomId = r.Room, DocId = r.Doc ?? r.Entry.id,
                Title = r.Entry.title, Body = r.Entry.body, Info = info, Clips = clips,
                StartInRead = read,
                NoMarker = r.Fill != null,   // 黒塗りの所を書き写さない
            };
        }

        // ---- メモ ----

        private void RebuildMemo()
        {
            Clear(_memoContent);
            _cards.Clear();
            _fills.Clear();
            _items.Clear();
            _rows.Clear();
            // 黒塗りの資料（先に置く。空欄も選べる）
            foreach (var d in FillDocs.OnBoard())
            {
                var fc = FillCard.Create(_memoContent, this, d);
                _fills.Add(fc);
                _items.Add(fc);
                _rows.Add(fc.Button);
            }
            // 言葉のカード（空欄に置いてあるものはボードに出さない）
            var all = MemoSnippets.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!string.IsNullOrEmpty(all[i].slot)) continue;
                var card = SnippetCard.Create(_memoContent, this, all[i], CardMinW, CardMaxW);
                _cards.Add(card);
                _items.Add(card);
                _rows.Add(card.Button);
            }
            _memoHint.gameObject.SetActive(_items.Count == 0);
            LayoutBoard();
            RefreshDoc();
            if (_flashDoc != null)
            {
                foreach (var f in _fills) if (f.Def.EntryId == _flashDoc) f.FlashWrong();
                _flashDoc = null;
            }
            if (_reselectSlotDoc != null)
            {
                Selectable pick = null;
                foreach (var f in _fills)
                {
                    if (f.Def.EntryId != _reselectSlotDoc) continue;
                    foreach (var s in f.Slots) if (s.Index == _reselectSlot && s.Button.interactable) pick = s.Button;
                    if (pick == null) pick = f.Button;
                    ScrollTo(f);
                }
                if (pick != null) Select(pick);
                _reselectSlotDoc = null;
                _reselectSlot = -1;
            }
            else if (_reselectSnippet >= 0)
            {
                foreach (var c in _cards) if (c.Snippet.id == _reselectSnippet) { Select(c.Button); ScrollTo(c); break; }
                _reselectSnippet = -1;
            }
        }

        private float BoardW => _memoContent.rect.width;
        private float ViewH => _memoScroll.viewport.rect.height / _zoom;   // ボードの座標で見えている高さ

        /// <summary>
        /// 置き場所の決まっていない物を置く。黒塗りの資料はボードの右端に上から、言葉のカードは一覧のように
        /// 左上から縦に、空いている所へ。一度置いた物はプレイヤーが動かすまで動かない
        /// </summary>
        private void LayoutBoard()
        {
            var taken = new List<Rect>();
            foreach (var it in _items) if (it.Placed) taken.Add(it.Rect);
            foreach (var f in _fills)
            {
                if (f.Placed) continue;
                f.Pos = FindSlotRight(f.Size, taken);
                f.Placed = true;
                taken.Add(f.Rect);
            }
            int cols = Mathf.Max(1, Mathf.FloorToInt((BoardW + BoardGap) / (CardMaxW + BoardGap)));
            foreach (var c in _cards)
            {
                if (c.Placed) continue;
                c.Pos = FindSlot(c.Size, taken, cols);
                c.Placed = true;
                taken.Add(c.Rect);
            }
            foreach (var it in _items)
            {
                // ボードの幅に収める（画面の大きさが変わった時など）
                it.Pos = new Vector2(Mathf.Clamp(it.Pos.x, 0f, Mathf.Max(0f, BoardW - it.Size.x)),
                                     Mathf.Clamp(it.Pos.y, 0f, BoardMaxY));
                it.Apply();
            }
            UpdateBoardHeight();
        }

        /// <summary>一覧の並び：列ごとに上から空きを探す。どの列にも見える範囲で入らなければ、いちばん左の列の下へ</summary>
        private Vector2 FindSlot(Vector2 size, List<Rect> taken, int cols)
        {
            float viewH = ViewH;
            for (int pass = 0; pass < 2; pass++)
                for (int col = 0; col < (pass == 0 ? cols : 1); col++)
                {
                    float x = col * (CardMaxW + BoardGap), y = 0f;
                    while (y < BoardMaxY)
                    {
                        float blockBottom = BlockBottom(new Rect(x, y, size.x, size.y), taken);
                        if (blockBottom < 0f)
                        {
                            if (pass == 1 || y + size.y <= viewH) return new Vector2(x, y);
                            break;
                        }
                        y = blockBottom + BoardGap;
                        if (pass == 0 && y + size.y > viewH) break;
                    }
                }
            return Vector2.zero;
        }

        /// <summary>黒塗りの資料の置き場所：ボードの右端を上から</summary>
        private Vector2 FindSlotRight(Vector2 size, List<Rect> taken)
        {
            float x = Mathf.Max(0f, BoardW - size.x), y = 0f;
            while (y < BoardMaxY)
            {
                float blockBottom = BlockBottom(new Rect(x, y, size.x, size.y), taken);
                if (blockBottom < 0f) return new Vector2(x, y);
                y = blockBottom + BoardGap;
            }
            return new Vector2(x, 0f);
        }

        /// <summary>r（＋すき間）に重なる物のいちばん下の端。重ならなければ -1</summary>
        private static float BlockBottom(Rect r, List<Rect> taken)
        {
            float blockBottom = -1f;
            foreach (var t in taken)
                if (r.xMin < t.xMax + BoardGap - 0.01f && t.xMin < r.xMax + BoardGap - 0.01f &&
                    r.yMin < t.yMax + BoardGap - 0.01f && t.yMin < r.yMax + BoardGap - 0.01f)
                    blockBottom = Mathf.Max(blockBottom, t.yMax);
            return blockBottom;
        }

        private void UpdateBoardHeight()
        {
            float bottom = 0f;
            foreach (var it in _items) bottom = Mathf.Max(bottom, it.Pos.y + it.Size.y);
            _memoContent.sizeDelta = new Vector2(0f, Mathf.Max(ViewH, bottom + BoardGap));
        }

        /// <summary>
        /// ボードの上の物を動かす。ほかの物にぶつかる所では、その境目で止まって横に貼り付く（相手は動かない）。
        /// 横と縦を分けて少しずつ進めるので、角ではぶつかった方向だけ止まり、もう一方へは滑る。
        /// overFills = 黒塗りの資料の上は通り抜ける（言葉のカードをマウスで空欄へ運んでいる間）
        /// </summary>
        internal void MoveItem(BoardItem item, Vector2 delta, bool overFills = false)
        {
            if (delta.sqrMagnitude < 0.0001f) return;
            var r = item.Rect;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 16f));
            var step = delta / steps;
            for (int k = 0; k < steps; k++)
            {
                r.x += Sweep(r, step.x, item, true, overFills);
                r.y += Sweep(r, step.y, item, false, overFills);
            }
            item.Pos = new Vector2(r.x, r.y);
            item.Apply();
            UpdateBoardHeight();
        }

        /// <summary>r を d だけ動かせる量（ぶつかる物・ボードの端で止める）</summary>
        private float Sweep(Rect r, float d, BoardItem self, bool horizontal, bool overFills)
        {
            const float Eps = 0.01f;
            if (Mathf.Abs(d) < 0.0001f) return 0f;
            foreach (var o in _items)
            {
                if (o == self || (overFills && o is FillCard)) continue;
                var b = o.Rect;
                if (horizontal)
                {
                    if (r.yMin >= b.yMax - Eps || b.yMin >= r.yMax - Eps) continue;   // 縦に重なっていない＝当たらない
                    if (d > 0f && b.xMin >= r.xMax - Eps) d = Mathf.Min(d, Mathf.Max(0f, b.xMin - r.xMax));
                    else if (d < 0f && b.xMax <= r.xMin + Eps) d = Mathf.Max(d, Mathf.Min(0f, b.xMax - r.xMin));
                }
                else
                {
                    if (r.xMin >= b.xMax - Eps || b.xMin >= r.xMax - Eps) continue;
                    if (d > 0f && b.yMin >= r.yMax - Eps) d = Mathf.Min(d, Mathf.Max(0f, b.yMin - r.yMax));
                    else if (d < 0f && b.yMax <= r.yMin + Eps) d = Mathf.Max(d, Mathf.Min(0f, b.yMax - r.yMin));
                }
            }
            // ボードの端
            if (horizontal) return Mathf.Clamp(r.x + d, 0f, Mathf.Max(0f, BoardW - r.width)) - r.x;
            return Mathf.Clamp(r.y + d, 0f, BoardMaxY) - r.y;
        }

        /// <summary>画面の位置 → ボードの座標（左上が原点、下が +y）</summary>
        private bool BoardPoint(Vector2 screen, Camera cam, out Vector2 p)
        {
            bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(_memoContent, screen, cam, out var local);
            p = new Vector2(local.x, -local.y);
            return ok;
        }

        internal void BeginDrag(BoardItem item, PointerEventData e)
        {
            if (!BoardPoint(e.position, e.pressEventCamera, out var p)) return;
            _grab = p - item.Pos;
            _dragStart = item.Pos;
            item.transform.SetAsLastSibling();   // 動かしている間は一番上に
            if (item is SnippetCard sc) Select(sc.Button);
            else if (item is FillCard fc) Select(fc.Button);
        }

        internal void DragItem(BoardItem item, PointerEventData e)
        {
            if (!BoardPoint(e.position, e.pressEventCamera, out var p)) return;
            bool card = item is SnippetCard;
            MoveItem(item, p - _grab - item.Pos, overFills: card);
            ScrollTo(item);
            if (!card) return;
            // 言葉のカード：下にある空欄を光らせる
            var hover = SlotAt(e.position, e.pressEventCamera);
            if (hover != _dragHover)
            {
                if (_dragHover != null) _dragHover.SetHover(false);
                if (hover != null) hover.SetHover(true);
                _dragHover = hover;
            }
        }

        /// <summary>言葉のカードを放した：空欄の上なら入れる。黒塗りの資料に重なったままなら元の場所へ戻す</summary>
        internal void EndDrag(SnippetCard card, PointerEventData e)
        {
            if (_dragHover != null) { _dragHover.SetHover(false); _dragHover = null; }
            var slot = SlotAt(e.position, e.pressEventCamera);
            if (slot != null)
            {
                card.Pos = _dragStart;   // 空欄から戻した時に元の場所へ帰る
                PlaceInto(slot, card.Snippet);
                return;
            }
            foreach (var f in _fills)
                if (card.Rect.Overlaps(f.Rect))
                {
                    card.Pos = _dragStart;
                    card.Apply();
                    UpdateBoardHeight();
                    return;
                }
        }

        // ---- 空欄に置いた言葉をドラッグで引き出す ----

        internal void BeginSlotDrag(FillSlot slot, PointerEventData e)
        {
            var s = slot.Placed;
            if (s == null) return;
            EndGhost();
            _ghost = SnippetCard.Create(_memoContent, this, s, CardMinW, CardMaxW);
            var cg = _ghost.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;   // 下の空欄・ボードに当たりを通す
            cg.alpha = 0.92f;
            _ghost.transform.SetAsLastSibling();
            _ghostGrab = new Vector2(Mathf.Min(36f, _ghost.Size.x * 0.5f), _ghost.Size.y * 0.5f);
            slot.SetLifted(true);
            MoveGhost(e);
            UiSound.Move();
        }

        internal void DragSlotWord(FillSlot slot, PointerEventData e)
        {
            if (_ghost == null) return;
            MoveGhost(e);
            var hover = SlotAt(e.position, e.pressEventCamera);
            if (hover == slot) hover = null;
            if (hover != _dragHover)
            {
                if (_dragHover != null) _dragHover.SetHover(false);
                if (hover != null) hover.SetHover(true);
                _dragHover = hover;
            }
        }

        private void MoveGhost(PointerEventData e)
        {
            if (_ghost == null || !BoardPoint(e.position, e.pressEventCamera, out var p)) return;
            var tl = p - _ghostGrab;
            ((RectTransform)_ghost.transform).anchoredPosition = new Vector2(tl.x, -tl.y);
        }

        private void EndGhost()
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
        }

        /// <summary>引き出した言葉を放した：別の空欄なら移す、同じ空欄なら戻す、ボードなら外して放した所に置く</summary>
        internal void EndSlotDrag(FillSlot slot, PointerEventData e)
        {
            if (_dragHover != null) { _dragHover.SetHover(false); _dragHover = null; }
            var s = slot.Placed;
            Vector2 size = _ghost != null ? _ghost.Size : new Vector2(CardMinW, 60f);
            Vector2 tl = Vector2.zero;
            if (BoardPoint(e.position, e.pressEventCamera, out var p)) tl = p - _ghostGrab;
            EndGhost();
            slot.SetLifted(false);
            if (s == null) return;
            var target = SlotAt(e.position, e.pressEventCamera);
            if (target == slot) return;
            if (target != null) { PlaceInto(target, s); return; }
            _reselectSnippet = s.id;
            FillDocs.Unplace(s, FreeSpotNear(new Rect(tl.x, tl.y, size.x, size.y)));
            UiSound.Cancel();
        }

        /// <summary>r の近くで、ほかの物と重ならない置き場所（左上の角）。r がそのまま空いていればそこ</summary>
        private Vector2 FreeSpotNear(Rect r)
        {
            var taken = new List<Rect>();
            foreach (var it in _items) taken.Add(it.Rect);
            float maxX = Mathf.Max(0f, BoardW - r.width);
            Vector2 Clamp(Vector2 v) => new Vector2(Mathf.Clamp(v.x, 0f, maxX), Mathf.Clamp(v.y, 0f, BoardMaxY));
            bool Free(Vector2 v) => BlockBottom(new Rect(v.x, v.y, r.width, r.height), taken) < 0f;
            var start = Clamp(r.position);
            if (Free(start)) return start;
            for (float rad = 24f; rad <= 900f; rad += 24f)
                for (int k = 0; k < 24; k++)
                {
                    float a = k * Mathf.PI * 2f / 24f;
                    var v = Clamp(start + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad);
                    if (Free(v)) return v;
                }
            int cols = Mathf.Max(1, Mathf.FloorToInt((BoardW + BoardGap) / (CardMaxW + BoardGap)));
            return FindSlot(r.size, taken, cols);
        }

        private FillSlot SlotAt(Vector2 screen, Camera cam)
        {
            foreach (var f in _fills)
            {
                var s = f.SlotAt(screen, cam);
                if (s != null) return s;
            }
            return null;
        }

        /// <summary>見える所までボードを送る（拡大している時は横にも）</summary>
        private void ScrollTo(BoardItem item)
        {
            float z = _zoom;
            var view = _memoScroll.viewport.rect;
            float contentW = _memoContent.rect.width * z, contentH = _memoContent.rect.height * z;
            var ap = _memoContent.anchoredPosition;
            float sx = -ap.x, sy = ap.y;
            float left = item.Pos.x * z, right = (item.Pos.x + item.Size.x) * z;
            float top = item.Pos.y * z, bottom = (item.Pos.y + item.Size.y) * z;
            if (right > sx + view.width) sx = right - view.width;
            if (left < sx) sx = left;
            if (bottom > sy + view.height) sy = bottom - view.height;
            if (top < sy) sy = top;
            _memoContent.anchoredPosition = new Vector2(-Mathf.Clamp(sx, 0f, Mathf.Max(0f, contentW - view.width)),
                                                        Mathf.Clamp(sy, 0f, Mathf.Max(0f, contentH - view.height)));
        }

        // ---- 拡大・縮小 ----

        private Camera BoardCam
        {
            get
            {
                var c = _memoContent != null ? _memoContent.GetComponentInParent<Canvas>() : null;
                return c == null || c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
            }
        }

        /// <summary>ボードの上でホイールを回した：ポインターの下の所を動かさずに拡大・縮小する</summary>
        private void ZoomByWheel()
        {
            float n = UiTheme.WheelNotches();
            if (Mathf.Abs(n) < 0.01f) return;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 screen = mouse.position.ReadValue();
#else
            Vector2 screen = Input.mousePosition;
#endif
            var cam = BoardCam;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_memoScroll.viewport, screen, cam)) return;
            if (_docWin.activeSelf && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_docWin.transform, screen, cam)) return;
            ZoomAt(Mathf.Clamp(_zoom * Mathf.Pow(ZoomStep, n), ZoomMin, ZoomMax), screen, cam);
        }

        private void ZoomAt(float z, Vector2 screen, Camera cam)
        {
            if (Mathf.Abs(z - _zoom) < 0.0001f) return;
            BoardPoint(screen, cam, out var before);
            _zoom = z;
            _memoContent.localScale = new Vector3(z, z, 1f);
            UpdateBoardHeight();
            BoardPoint(screen, cam, out var after);
            // ポインターの下にあった所（before）が、またポインターの下に来るよう送る
            var ap = _memoContent.anchoredPosition;
            ap.x += (after.x - before.x) * z;
            ap.y -= (after.y - before.y) * z;
            var view = _memoScroll.viewport.rect;
            float contentW = _memoContent.rect.width * z, contentH = _memoContent.rect.height * z;
            ap.x = -Mathf.Clamp(-ap.x, 0f, Mathf.Max(0f, contentW - view.width));
            ap.y = Mathf.Clamp(ap.y, 0f, Mathf.Max(0f, contentH - view.height));
            _memoContent.anchoredPosition = ap;
            UpdateZoomLabel();
        }

        private void UpdateZoomLabel()
        {
            if (_zoomLabel != null)
                _zoomLabel.text = $"ホイール 拡大・縮小　　背景をドラッグ 見回す　　{Mathf.RoundToInt(_zoom * 100f)}%";
        }

        /// <summary>全部のカードと黒塗りの資料を一覧の並びに戻す</summary>
        private void ResetBoard()
        {
            if (_items.Count == 0) return;
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var card = sel != null ? sel.GetComponent<SnippetCard>() : null;
            if (card != null) _reselectSnippet = card.Snippet.id;
            _memoContent.anchoredPosition = Vector2.zero;
            FillDocs.ResetPlacement();
            MemoSnippets.ResetPlacement();
            UiSound.Decide();
        }

        // ---- 黒塗りの資料：空欄に言葉を入れる ----

        /// <summary>空欄に言葉のカードを入れる。全部埋まったら判定（違えば赤く光って付箋、正しければ入れ物が開く）</summary>
        internal void PlaceInto(FillSlot slot, MemoSnippet s)
        {
            if (slot == null || s == null || slot.Card.Solved) return;
            var d = slot.Card.Def;
            var wrong = new List<int>();
            var result = FillDocs.Place(s, d, slot.Index, wrong);
            // 次に選ぶ空欄：同じ資料のまだ空いている空欄（無ければ今の空欄）
            _reselectSlotDoc = d.EntryId;
            _reselectSlot = slot.Index;
            int n = FillDocs.Answers(d).Count;
            for (int k = 1; k <= n; k++)
            {
                int i = (slot.Index + k) % n;
                if (FillDocs.InSlot(d, i) == null) { _reselectSlot = i; break; }
            }
            switch (result)
            {
                case FillDocs.Result.Wrong:
                    UiSound.Error();
                    _flashDoc = d.EntryId;
                    bool hinted = false;
                    foreach (int i in wrong) if (d.Hints != null && i < d.Hints.Length && Notebook.Contains(d.Hints[i])) hinted = true;
                    ToastUI.Show(GameText.Get(GameText.UiKey("fill.wrong"), "……意味が通らない。どこかの言葉が違う") +
                                 (hinted ? GameText.Get(GameText.UiKey("wrong.flagged"), "　──手帳に付箋を立てた。関係のある記録があるはずだ")
                                         : GameText.Get(GameText.UiKey("wrong.unread"), "　──まだ読んでいない資料があるのかもしれない")));
                    break;
                case FillDocs.Result.Solved:
                    ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.7f, spatial: false);
                    var lines = new List<string>();
                    foreach (var l in d.CommentText.Replace("\r", "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
                        lines.Add(l.Replace("\n", "").Trim());
                    var room = LoopRooms.Get(d.RoomId);
                    bool here = !LoopRooms.InCorridor && LoopRooms.CurrentRoomId == d.RoomId;
                    lines.Add(here
                        ? string.Format(GameText.Get(GameText.UiKey("fill.opened_here"), "──{0}で、錠の外れる音がした。"), d.ContainerName)
                        : string.Format(GameText.Get(GameText.UiKey("fill.opened_far"), "──{0}の{1}が、開いたはずだ。"), room != null ? room.DisplayName : d.RoomId, d.ContainerName));
                    InspectView.Instance?.Say(lines.ToArray());
                    break;
                default:
                    UiSound.Decide();
                    break;
            }
        }

        /// <summary>空欄のカードをボードへ戻す</summary>
        internal void Unplace(FillSlot slot)
        {
            var s = slot != null ? slot.Placed : null;
            if (s == null) return;
            _reselectSlotDoc = slot.Card.Def.EntryId;
            _reselectSlot = slot.Index;
            FillDocs.Unplace(s);
            UiSound.Cancel();
        }

        /// <summary>解いた黒塗りの資料をボードから外す（資料の一覧に綴じてある）</summary>
        internal void RemoveFill(FillCard card)
        {
            if (!FillDocs.RemoveFromBoard(card.Def))
            {
                UiSound.Error();
                ToastUI.Show(GameText.Get(GameText.UiKey("fill.cant_remove"), "まだ埋まっていない。……この資料は、手放せない"));
                return;
            }
            UiSound.Cancel();
        }

        /// <summary>解いた黒塗りの資料を読む（資料の一覧の全文）</summary>
        internal void OpenFill(FillDocDef d)
        {
            foreach (var e in Notebook.Entries)
                if (e.id == d.EntryId && Classify(e, out var row))
                {
                    if (_overlay)
                    {
                        var iv = InspectView.Instance;
                        var next = iv != null && iv.CurrentEntryId != d.EntryId ? BuildRequest(row, true) : null;
                        CloseOverlay();
                        if (next != null) iv.SwitchTo(next);
                        return;
                    }
                    _reselectSlotDoc = d.EntryId;
                    OpenEntry(row, read: true);
                    return;
                }
        }

        /// <summary>このフレームで決定（Enter／A）が押されたか（マウスのクリックと分ける）</summary>
        internal static bool SubmitPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) ||
                   (gp != null && gp.buttonSouth.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
#endif
        }

        // ---- 言葉を選ぶ窓 ----

        private void BuildPicker(RectTransform book)
        {
            // ボードの右側に重ねる（引用元の文章ウィンドウと同じ形）
            var frame = UiTheme.Fill(book, "Picker", UiTheme.WithAlpha(UiTheme.Accent, 0.55f), raycast: true);
            _picker = frame.gameObject;
            var rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-32f, -100f);
            rt.sizeDelta = new Vector2(640f, DocWinH);
            var fill = UiTheme.Fill(rt, "Fill", UiTheme.Hex(0x050506));
            UiTheme.Stretch(fill.rectTransform, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline);

            var kind = UiTheme.Label(rt, "Kind", UiTheme.FsSmall, TextAnchor.UpperLeft, UiTheme.TextSub, shadow: false);
            UiTheme.Place(kind.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -22f), new Vector2(560f, 28f));
            kind.text = "空欄に入れる言葉";
            _pickerTitle = UiTheme.Label(rt, "Title", 24, TextAnchor.UpperLeft, UiTheme.Text, display: true);
            UiTheme.Place(_pickerTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -50f), new Vector2(560f, 40f));
            var rule = UiTheme.Fill(rt, "Rule", UiTheme.WithAlpha(UiTheme.Accent, 0.7f));
            UiTheme.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(32f, -100f), new Vector2(90f, UiTheme.Hairline));

            var view = UiTheme.Fill(rt, "View", new Color(0f, 0f, 0f, 0f), raycast: true);
            UiTheme.Stretch(view.rectTransform, 16f, 16f, DocViewTop, 64f);
            view.gameObject.AddComponent<RectMask2D>();
            _pickerContent = UiTheme.Rect(view.rectTransform, "Content");
            _pickerContent.anchorMin = new Vector2(0f, 1f); _pickerContent.anchorMax = new Vector2(1f, 1f);
            _pickerContent.pivot = new Vector2(0.5f, 1f);
            _pickerContent.anchoredPosition = Vector2.zero;
            _pickerContent.sizeDelta = Vector2.zero;
            var v = _pickerContent.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            _pickerContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _pickerScroll = view.gameObject.AddComponent<ScrollRect>();
            _pickerScroll.viewport = view.rectTransform;
            _pickerScroll.content = _pickerContent;
            _pickerScroll.horizontal = false;
            _pickerScroll.movementType = ScrollRect.MovementType.Clamped;
            _pickerScroll.scrollSensitivity = 40f;
            _pickerScroll.inertia = false;

            _pickerEmpty = UiTheme.Label(rt, "Empty", UiTheme.FsSmall, TextAnchor.UpperLeft, UiTheme.TextSub, shadow: false);
            UiTheme.Place(_pickerEmpty.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -DocViewTop - 70f), new Vector2(560f, 120f));
            _pickerEmpty.lineSpacing = 1.5f;
            _pickerEmpty.text = "書き写した言葉が無い。\n資料を開いて「読む」と、本文をなぞって言葉を切り取れる。";

            var hint = UiTheme.Label(rt, "Hint", UiTheme.FsSmall, TextAnchor.LowerRight, UiTheme.TextSub, shadow: false);
            UiTheme.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 22f), new Vector2(500f, 30f));
            hint.text = $"{UiTheme.Key("Esc", "B")} やめる";
            _picker.SetActive(false);
        }

        /// <summary>空欄に入れる言葉を選ぶ窓を開く（ボードにある言葉のカードの一覧）</summary>
        internal void OpenPicker(FillSlot slot)
        {
            if (slot == null || slot.Card.Solved) return;
            HideDoc();
            _pickerSlot = slot;
            Clear(_pickerContent);
            var items = new List<Selectable>();
            var cur = slot.Placed;
            if (cur != null)
            {
                var clear = UiTheme.MenuItem(_pickerContent, "（空欄を空ける）", () => { var sl = _pickerSlot; HidePicker(false); Unplace(sl); }, 600f, 52f, UiTheme.FsSmall);
                clear.GetComponentInChildren<Text>().color = UiTheme.TextSub;
                items.Add(clear);
            }
            int words = 0;
            foreach (var s in MemoSnippets.All)
            {
                if (!string.IsNullOrEmpty(s.slot)) continue;
                var snip = s;
                var b = UiTheme.MenuItem(_pickerContent, "「" + FillSlot.Truncate(s.text, 24f) + "」", () =>
                {
                    var sl = _pickerSlot;
                    HidePicker(false);
                    PlaceInto(sl, snip);
                }, 600f, 52f, 22);
                items.Add(b);
                words++;
            }
            UiTheme.ChainVertical(items);
            _pickerEmpty.gameObject.SetActive(words == 0);
            _pickerTitle.text = $"『{slot.Card.Def.TitleText}』";
            _picker.SetActive(true);
            _picker.transform.SetAsLastSibling();
            _pickerContent.anchoredPosition = Vector2.zero;
            UiSound.Decide();
            if (items.Count > 0) Select(items[0]);
            UpdateGuide();
        }

        /// <summary>言葉を選ぶ窓を閉じる。reselect = 開いた空欄を選び直す</summary>
        private void HidePicker(bool reselect)
        {
            if (_picker == null || !_picker.activeSelf) return;
            _picker.SetActive(false);
            _pickerClosedFrame = Time.frameCount;
            Clear(_pickerContent);
            if (reselect && _pickerSlot != null && _pickerSlot.Button != null) Select(_pickerSlot.Button);
            _pickerSlot = null;
            UpdateGuide();
        }

        /// <summary>矢印キーでの選択の移動を止める（Shift＋矢印でカードを動かしている間）</summary>
        private static void SetNavigation(bool on)
        {
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = on;
        }

        // ---- 引用元の文章ウィンドウ ----

        private void BuildDocWindow(RectTransform book)
        {
            // ボードの右側に重ねる（左側のカードは見えたまま・動かせる）。枠は項目の Image、内側を黒で塗る
            var frame = UiTheme.Fill(book, "DocWindow", UiTheme.WithAlpha(UiTheme.Accent, 0.55f), raycast: true);
            _docWin = frame.gameObject;
            var rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-32f, -100f);
            rt.sizeDelta = new Vector2(640f, DocWinH);
            var fill = UiTheme.Fill(rt, "Fill", UiTheme.Hex(0x050506));
            UiTheme.Stretch(fill.rectTransform, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline);

            var kind = UiTheme.Label(rt, "Kind", UiTheme.FsSmall, TextAnchor.UpperLeft, UiTheme.TextSub, shadow: false);
            UiTheme.Place(kind.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -22f), new Vector2(300f, 28f));
            kind.text = "引用元";
            _docTitle = UiTheme.Label(rt, "Title", 26, TextAnchor.UpperLeft, UiTheme.Text, display: true);
            UiTheme.Place(_docTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -50f), new Vector2(520f, 40f));
            var close = UiTheme.MenuItem(rt, "×", () => { HideDoc(); UiSound.Cancel(); }, 64f, 52f, 30);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-12f, -14f);
            var rule = UiTheme.Fill(rt, "Rule", UiTheme.WithAlpha(UiTheme.Accent, 0.7f));
            UiTheme.Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(32f, -100f), new Vector2(90f, UiTheme.Hairline));

            // 本文（長ければホイールで送る）
            var view = UiTheme.Fill(rt, "View", new Color(0f, 0f, 0f, 0f), raycast: true);
            UiTheme.Stretch(view.rectTransform, 32f, 28f, DocViewTop, DocViewBottom);
            view.gameObject.AddComponent<RectMask2D>();
            _docBody = UiTheme.Label(view.rectTransform, "Body", 22, TextAnchor.UpperLeft,
                                     Color.Lerp(UiTheme.TextSub, UiTheme.Text, 0.25f), shadow: false);
            var brt = _docBody.rectTransform;
            brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = Vector2.zero;
            _docBody.lineSpacing = DocLineSpacing;
            _docBody.supportRichText = false;   // 文字の番号を本文とそろえる（マーカーの白い文字）
            _docBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _docBody.verticalOverflow = VerticalWrapMode.Overflow;
            _docBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _docGlyphs = _docBody.gameObject.AddComponent<MarkerGlyphs>();
            _docRuby = RubyText.Attach(_docBody);
            _docScroll = view.gameObject.AddComponent<ScrollRect>();
            _docScroll.viewport = view.rectTransform;
            _docScroll.content = brt;
            _docScroll.horizontal = false;
            _docScroll.movementType = ScrollRect.MovementType.Clamped;
            _docScroll.scrollSensitivity = 40f;
            _docScroll.inertia = false;

            // 下：実物の資料を開く
            var open = UiTheme.MenuItem(rt, "資料を開く", () => { var sn = _docSnippet; HideDoc(); if (sn != null) OpenSource(sn); }, 220f, 52f, UiTheme.FsSmall);
            var ort = (RectTransform)open.transform;
            ort.anchorMin = ort.anchorMax = new Vector2(0f, 0f);
            ort.pivot = new Vector2(0f, 0f);
            ort.anchoredPosition = new Vector2(16f, 16f);
            var hint = UiTheme.Label(rt, "Hint", UiTheme.FsSmall, TextAnchor.LowerRight, UiTheme.TextSub, shadow: false);
            UiTheme.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 26f), new Vector2(340f, 30f));
            hint.text = $"{UiTheme.Key("Esc", "B")} 閉じる";
            _docWin.SetActive(false);
        }

        /// <summary>カードの引用元の資料の文章を開く（書き写した所が白く浮き上がり、その位置まで送る）</summary>
        internal void ShowDoc(MemoSnippet s)
        {
            if (s == null || !SourceText(s.entryId, out var title, out var body)) { UiSound.Error(); return; }
            bool same = _docWin.activeSelf && _docSnippet != null && _docSnippet.entryId == s.entryId;
            _docSnippet = s;
            _docTitle.text = title;
            _docBody.text = Ruby.ParseForDisplay(body, _docSpans);   // 親文字だけの文（メモの文字番号はこれで数える）
            _docBody.lineSpacing = _docSpans.Count > 0 ? DocLineSpacingRuby : DocLineSpacing;
            _docRuby.SetSpans(_docSpans);
            _docWin.SetActive(true);
            _docWin.transform.SetAsLastSibling();
            RefreshDoc();
            StartCoroutine(ScrollDocTo(s.start));
            if (!same) UiSound.Decide();
        }

        private void HideDoc()
        {
            if (_docWin == null || !_docWin.activeSelf) return;
            _docWin.SetActive(false);
            _docSnippet = null;
            _docClosedFrame = Time.frameCount;
        }

        /// <summary>開いている文章の白い文字（その資料のメモ全部）を今のメモに合わせる。元のメモが消えたら閉じる</summary>
        private void RefreshDoc()
        {
            if (_docWin == null || !_docWin.activeSelf || _docSnippet == null) return;
            var mine = MemoSnippets.ForEntry(_docSnippet.entryId);
            if (mine.Count == 0) { HideDoc(); return; }
            if (MemoSnippets.IndexOf(_docSnippet.id) < 0) _docSnippet = mine[0];
            string body = _docBody.text;
            var lit = new HashSet<int>();
            foreach (var m in mine)
                for (int c = m.start; c < m.start + m.length && c < body.Length; c++)
                    if (!char.IsWhiteSpace(body[c])) lit.Add(c);
            _docGlyphs.Set(lit);
        }

        /// <summary>
        /// 本文のその文字の行が見えるよう送る（文字の配置が決まってから）。先頭から見えるなら送らない。
        /// 送る時は窓の上端を行の境目にそろえる（上の行が半分切れないように）
        /// </summary>
        private System.Collections.IEnumerator ScrollDocTo(int index)
        {
            _docBody.rectTransform.anchoredPosition = Vector2.zero;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var gen = _docBody.cachedTextGenerator;
            var lines = gen.lines;
            if (lines.Count == 0) yield break;
            float ppu = _docBody.pixelsPerUnit > 0f ? _docBody.pixelsPerUnit : 1f;
            int li = 0;
            while (li + 1 < lines.Count && lines[li + 1].startCharIdx <= index) li++;
            // 窓の高さを行送りの倍数にする（上端を行の境目にそろえれば、下端でも行が切れない）
            float full = DocWinH - DocViewTop - DocViewBottom;
            if (lines.Count >= 2)
            {
                float pitch = (lines[0].topY - lines[1].topY) / ppu;
                if (pitch > 1f)
                {
                    float h = Mathf.Max(1, Mathf.FloorToInt(full / pitch)) * pitch;
                    var vrt = _docScroll.viewport;
                    vrt.offsetMin = new Vector2(vrt.offsetMin.x, DocViewBottom + (full - h));
                }
            }
            float viewH = _docScroll.viewport.rect.height;
            float top = -lines[li].topY / ppu, bottom = top + lines[li].height / ppu;
            float scroll = 0f;
            if (bottom > viewH - 8f)
            {
                float want = top - viewH * 0.3f;   // その行が窓の上から3割あたりに来る
                for (int k = li; k >= 0; k--)
                {
                    float t = -lines[k].topY / ppu;
                    if (t <= want) { scroll = t; break; }
                }
            }
            // 送りすぎる時（本文の終わりが見えている）も、上端は行の境目にする（下の行が少し切れる方がよい）
            float max = Mathf.Max(0f, _docBody.rectTransform.rect.height - viewH);
            if (scroll > max)
            {
                scroll = 0f;
                for (int k = 0; k < lines.Count; k++)
                {
                    float t = -lines[k].topY / ppu;
                    if (t <= max) scroll = t; else break;
                }
            }
            _docBody.rectTransform.anchoredPosition = new Vector2(0f, scroll);
        }

        /// <summary>手帳エントリの題と本文（読む画面と同じ本文＝音声記録は書き起こし）</summary>
        private static bool SourceText(string entryId, out string title, out string body)
        {
            title = null; body = null;
            foreach (var e in Notebook.Entries)
                if (e.id == entryId) { title = e.title; body = e.body; break; }
            if (body == null)
                foreach (var f in FindObjectsByType<LoopFindable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if ($"{f.RoomId}_{f.Id}" == entryId) { title = string.IsNullOrEmpty(f.Title) ? f.Name : f.Title; body = f.Body; break; }
            if (DocCatalog.TryParseEntry(entryId, out var room, out var doc) && DocCatalog.Get(room, doc).Audio)
                body = DocCatalog.Transcript(room, doc);
            if (body == null) return false;
            body = body.Replace("\r", "");
            // 調べる画面と同じ組み方の本文（メモの文字番号がこれで数えてある）
            if (DocCatalog.TryParseEntry(entryId, out var rRoom, out var rDoc))
                body = DocCatalog.ReadingBody(rRoom, rDoc, body, out _);
            title = title != null ? Ruby.Strip(title) : null;
            title = title ?? entryId;
            return true;
        }

        internal void RemoveSnippet(MemoSnippet s)
        {
            int i = MemoSnippets.IndexOf(s.id);
            if (i + 1 < MemoSnippets.Count) _reselectSnippet = MemoSnippets.All[i + 1].id;
            else if (i - 1 >= 0) _reselectSnippet = MemoSnippets.All[i - 1].id;
            MemoSnippets.Remove(s.id);
            UiSound.Cancel();
        }

        /// <summary>カードの元の資料を「読む」画面で開く</summary>
        internal void OpenSource(MemoSnippet s)
        {
            if (_overlay)
            {
                // 資料の画面から開いている：その資料なら戻るだけ、別の資料ならそちらに替える
                var iv = InspectView.Instance;
                InspectView.Request next = null;
                if (iv != null && iv.CurrentEntryId != s.entryId)
                    foreach (var e in Notebook.Entries)
                        if (e.id == s.entryId && Classify(e, out var row)) { next = BuildRequest(row, true); break; }
                CloseOverlay();
                if (next != null) iv.SwitchTo(next);
                return;
            }
            foreach (var e in Notebook.Entries)
                if (e.id == s.entryId && Classify(e, out var row))
                {
                    _reselectSnippet = s.id;
                    OpenEntry(row, read: true);
                    return;
                }
        }

        // ============================== キー ==============================

        private void HandleKeys()
        {
            if (InspectView.IsOpen && !_overlay) return;
            bool prev = false, next = false, del = false, shift = false, reset = false, back = false;
            Vector2 move = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            if (kb != null)
            {
                prev = kb.qKey.wasPressedThisFrame; next = kb.eKey.wasPressedThisFrame;
                shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                del = kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
                reset = kb.rKey.wasPressedThisFrame;
                back = kb.mKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame;
                if (shift)
                {
                    if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) move.x -= 1f;
                    if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) move.x += 1f;
                    if (kb.upArrowKey.isPressed || kb.wKey.isPressed) move.y += 1f;
                    if (kb.downArrowKey.isPressed || kb.sKey.isPressed) move.y -= 1f;
                }
            }
            if (gp != null)
            {
                prev |= gp.leftShoulder.wasPressedThisFrame; next |= gp.rightShoulder.wasPressedThisFrame;
                del |= gp.buttonNorth.wasPressedThisFrame;
                reset |= gp.buttonWest.wasPressedThisFrame;
                back |= gp.rightShoulder.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame || gp.selectButton.wasPressedThisFrame;
                var rs = gp.rightStick.ReadValue();
                if (rs.magnitude > 0.2f) move += rs;
            }
#else
            prev = Input.GetKeyDown(KeyCode.Q); next = Input.GetKeyDown(KeyCode.E);
            shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            del = Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace);
            reset = Input.GetKeyDown(KeyCode.R);
            back = Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab);
            if (shift)
            {
                if (Input.GetKey(KeyCode.LeftArrow)) move.x -= 1f;
                if (Input.GetKey(KeyCode.RightArrow)) move.x += 1f;
                if (Input.GetKey(KeyCode.UpArrow)) move.y += 1f;
                if (Input.GetKey(KeyCode.DownArrow)) move.y -= 1f;
            }
#endif
            bool closeDoc = false;
#if ENABLE_INPUT_SYSTEM
            closeDoc = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame);
#else
            closeDoc = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (_picker.activeSelf)
            {
                // 言葉を選ぶ窓：閉じるキーだけ受ける（選択の移動・決定は窓の項目が受ける）
                if (closeDoc || back) { HidePicker(true); UiSound.Cancel(); }
                return;
            }
            if (closeDoc && _docWin.activeSelf) { HideDoc(); UiSound.Cancel(); return; }
            if (_overlay)
            {
                // 資料の画面から開いている：閉じると資料へ戻る（開いた時と同じキーでは閉じない）
                if (back && Time.frameCount != _ovOpenedFrame) { CloseOverlay(); return; }
            }
            else if (prev || next)
            {
                if (_tab == Tab.Docs && prev) SetChapter(_chapter - 1 < 0 ? ChapterNames.Length - 1 : _chapter - 1);
                SetTab(_tab == Tab.Docs ? Tab.Memo : Tab.Docs);
                ProceduralAudio.PlayAt(ProceduralAudio.Page(), Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.45f, spatial: false);
                return;
            }
            if (_tab != Tab.Memo) return;
            ZoomByWheel();
            SetNavigation(!shift);   // Shift を押している間は矢印で選択を移さない（カードを動かす）
            if (reset) { ResetBoard(); return; }
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var item = sel != null ? sel.GetComponentInParent<BoardItem>() : null;
            if (item == null) return;
            if (del)
            {
                var slot = sel.GetComponent<FillSlot>();
                if (item is SnippetCard sc) RemoveSnippet(sc.Snippet);
                else if (slot != null) Unplace(slot);
                else if (item is FillCard fc) RemoveFill(fc);
                return;
            }
            if (move.sqrMagnitude > 0.0001f)
            {
                // 空欄を選んでいる時は、その黒塗りの資料ごと動かす
                if (move.sqrMagnitude > 1f) move.Normalize();
                item.transform.SetAsLastSibling();
                MoveItem(item, new Vector2(move.x, -move.y) * MoveSpeed * Time.unscaledDeltaTime);
                ScrollTo(item);
            }
        }

        // ============================== 補助 ==============================

        private void FocusFirst()
        {
            if (!_shown) return;
            if (_rows.Count > 0 && (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null ||
                                    !EventSystem.current.currentSelectedGameObject.activeInHierarchy))
                Select(_rows[0]);
        }

        private static void Select(Selectable s)
        {
            if (EventSystem.current == null || s == null) return;
            UiSound.MuteMoveUntil = Time.unscaledTime + 0.1f;
            EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        private static void Clear(RectTransform content)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var c = content.GetChild(i);
                c.SetParent(null);
                Destroy(c.gameObject);
            }
        }
    }

    /// <summary>
    /// メモの1枚（切り取った文だけ。出典は書かない）。ボードの上で自由に動かせる（ほかのカードとは重ならない）。
    /// 右クリックで消す、ダブルクリックで引用元の文章を開く。大きさは文の長さに合わせる（幅は上限まで、あとは改行）
    /// </summary>
    public class SnippetCard : BoardItem, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public MemoSnippet Snippet;
        public Button Button;
        private NotebookUI _owner;

        // 内側の地は不透明（枠の色の上に重ねるので、透けると枠の色が中まで滲む）
        private static readonly Color CardBg = UiTheme.Hex(0x050506);
        private static readonly Color CardBgOn = UiTheme.Hex(0x121110);
        private static readonly Color CardFrame = UiTheme.WithAlpha(UiTheme.Text, 0.12f);
        // 余白：Text は行の上に行間ぶんの空きを持つので、上の余白はその分だけ小さくして上下をそろえる
        private const int PadL = 20, PadR = 20, PadT = 8, PadB = 14;
        private const float LineSpacing = 1.12f;   // 行送りは約1.5文字（日本語の読みやすい幅）

        /// <summary>ボード上の場所（左上が原点、下が +y）</summary>
        public override Vector2 Pos
        {
            get => new Vector2(Snippet.x, Snippet.y);
            set { Snippet.x = value.x; Snippet.y = value.y; }
        }
        public override bool Placed
        {
            get => Snippet.placed;
            set => Snippet.placed = value;
        }

        public static SnippetCard Create(RectTransform parent, NotebookUI owner, MemoSnippet s, float minW, float maxW)
        {
            var btn = UiTheme.MenuItem(parent, "", null, maxW, 60f, 24, sharp: true);   // ボードの拡大・縮小に合わせた細かさで描く
            var go = btn.gameObject;
            go.name = "Card_" + s.id;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            // 地は黒、細い枠。文字は白（読む画面でマーカーを引いて浮き上がった文字と同じ見た目）。
            // 項目の Image を枠の色にして、内側を黒で塗る（どの解像度でも枠が同じ太さで出る）
            var frame = go.GetComponent<Image>();
            frame.color = CardFrame;
            var bg = UiTheme.Fill(go.transform, "Fill", CardBg);
            bg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UiTheme.Stretch(bg.rectTransform, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline);
            bg.transform.SetAsFirstSibling();
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(PadL, PadR, PadT, PadB);
            v.spacing = UiTheme.Sp1;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 改行は自分で入れる（語の途中で切らない。JaLineBreak）。Text には折り返させない
            var text = go.transform.Find("Label").GetComponent<Text>();
            text.alignment = TextAnchor.UpperLeft;
            text.lineSpacing = LineSpacing;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.text = JaLineBreak.Wrap("「" + s.text + "」", (maxW - PadL - PadR) / text.fontSize);
            var lift = text.GetComponent<Shadow>();
            if (lift == null) lift = text.gameObject.AddComponent<Shadow>();
            lift.effectColor = new Color(0f, 0f, 0f, 0.8f);
            lift.effectDistance = new Vector2(1f, -1.5f);
            var mark = go.transform.Find("Mark");
            if (mark != null) mark.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            // 幅はいちばん長い行に合わせる → 高さは中身から決まる
            float w = Mathf.Clamp(text.preferredWidth + PadL + PadR, minW, maxW + 40f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = -1f;   // 高さは中身から（項目の既定の高さを使わない）
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

            var fx = go.GetComponent<UiItemFx>();
            fx.ShiftText = false;   // 並びはボードが決めるので文字はずらさない
            // 選択中：文字は白のまま、枠が真鍮色になり地が少し明るくなる（＋左の細罫）
            fx.TextNormal = fx.TextOn = Color.white;
            fx.OnFx = k =>
            {
                frame.color = Color.Lerp(CardFrame, UiTheme.WithAlpha(UiTheme.Accent, 0.85f), k);
                bg.color = Color.Lerp(CardBg, CardBgOn, k);
            };
            var card = go.AddComponent<SnippetCard>();
            card.Snippet = s;
            card.Button = btn;
            card._owner = owner;
            btn.onClick.AddListener(() =>
            {
#if ENABLE_INPUT_SYSTEM
                var kb = Keyboard.current;
                var gp = Gamepad.current;
                if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) ||
                    (gp != null && gp.buttonSouth.wasPressedThisFrame))
                    owner.ShowDoc(s);
#endif
            });
            return card;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _owner.BeginDrag(this, e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _owner.DragItem(this, e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _owner.EndDrag(this, e);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) _owner.RemoveSnippet(Snippet);
            else if (e.button == PointerEventData.InputButton.Left && e.clickCount >= 2) _owner.ShowDoc(Snippet);
        }
    }
}
