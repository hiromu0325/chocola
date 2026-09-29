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
    ///          右クリック（Delete）で消す。ダブルクリック（Enter）で元の資料を開く。R で一覧の並びに戻す。
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
        private string _reselectEntry;
        private int _reselectSnippet = -1;

        // ---- 資料の画面から開いた時（メモだけを上に重ねる） ----
        private bool _overlay, _ovPrevShown, _ovPrevBook;
        private Tab _ovPrevTab;
        private Action _ovClosed;
        private int _ovOpenedFrame = -1;

        // ---- ボード ----
        private const float CardMaxW = 460f, CardMinW = 200f, BoardGap = 12f, BoardMaxY = 4000f;
        private const float MoveSpeed = 700f;   // キー・スティックで動かす速さ（/秒）
        private Vector2 _grab;                  // ドラッグ：つかんだ所とカードの左上の差

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

            _guide = UiTheme.Label(book, "Guide", UiTheme.FsSmall, TextAnchor.LowerCenter, UiTheme.TextSub, shadow: false);
            UiTheme.Place(_guide.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(1400f, 30f));

            Notebook.OnChanged += MarkDirty;
            MemoSnippets.OnChanged += MarkDirty;
            SetTab(Tab.Docs);
        }

        private void OnDestroy()
        {
            Notebook.OnChanged -= MarkDirty;
            MemoSnippets.OnChanged -= MarkDirty;
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
            _memoScroll.horizontal = false;
            _memoScroll.movementType = ScrollRect.MovementType.Clamped;
            _memoScroll.scrollSensitivity = 40f;
            _memoScroll.inertia = false;

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
            _guide.text = _tab == Tab.Docs
                ? $"{UiTheme.Key("Enter", "A")} 調べる　　{UiTheme.Key("Q", "LB")}{UiTheme.Key("E", "RB")} 資料／メモ　　{UiTheme.Key("Tab", "View")} 閉じる"
                : _overlay
                    ? $"ドラッグ／{UiTheme.Key("Shift＋矢印", "右スティック")} 動かす　　右クリック／{UiTheme.Key("Delete", "Y")} 消す　　ダブルクリック／{UiTheme.Key("Enter", "A")} 元の資料　　{UiTheme.Key("R", "X")} 並べ直す　　{UiTheme.Key("M", "RB")}{UiTheme.Key("Esc", "B")} 資料に戻る"
                    : $"ドラッグ／{UiTheme.Key("Shift＋矢印", "右スティック")} 動かす　　右クリック／{UiTheme.Key("Delete", "Y")} 消す　　ダブルクリック／{UiTheme.Key("Enter", "A")} 元の資料　　{UiTheme.Key("R", "X")} 並べ直す　　{UiTheme.Key("Q", "LB")}{UiTheme.Key("E", "RB")} 資料／メモ　　{UiTheme.Key("Tab", "View")} 閉じる";
        }

        // ---- 資料の一覧 ----

        private struct Row
        {
            public NotebookEntry Entry;
            public string Room, Doc;
            public string Kind;     // 種別の表示（書類・テープ…）
            public int Order;
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
            };
        }

        // ---- メモ ----

        private void RebuildMemo()
        {
            Clear(_memoContent);
            _cards.Clear();
            _rows.Clear();
            _memoHint.gameObject.SetActive(MemoSnippets.Count == 0);
            var all = MemoSnippets.All;
            for (int i = 0; i < all.Count; i++)
            {
                var card = SnippetCard.Create(_memoContent, this, all[i], SourceTitle(all[i].entryId), CardMinW, CardMaxW);
                _cards.Add(card);
                _rows.Add(card.Button);
            }
            LayoutBoard();
            if (_reselectSnippet >= 0)
            {
                foreach (var c in _cards) if (c.Snippet.id == _reselectSnippet) { Select(c.Button); ScrollTo(c); break; }
                _reselectSnippet = -1;
            }
        }

        private float BoardW => _memoContent.rect.width;
        private float ViewH => _memoScroll.viewport.rect.height;

        /// <summary>
        /// 置き場所の決まっていないカード（新しく書き写したもの・並べ直し）を、一覧のように左上から縦に、
        /// 空いている所へ置く。一度置いたカードはプレイヤーが動かすまで動かない
        /// </summary>
        private void LayoutBoard()
        {
            var taken = new List<Rect>();
            foreach (var c in _cards) if (c.Snippet.placed) taken.Add(c.Rect);
            int cols = Mathf.Max(1, Mathf.FloorToInt((BoardW + BoardGap) / (CardMaxW + BoardGap)));
            foreach (var c in _cards)
            {
                if (c.Snippet.placed) continue;
                var pos = FindSlot(c.Size, taken, cols);
                c.Snippet.x = pos.x;
                c.Snippet.y = pos.y;
                c.Snippet.placed = true;
                taken.Add(c.Rect);
            }
            foreach (var c in _cards)
            {
                // ボードの幅に収める（画面の大きさが変わった時など）
                c.Snippet.x = Mathf.Clamp(c.Snippet.x, 0f, Mathf.Max(0f, BoardW - c.Size.x));
                c.Snippet.y = Mathf.Clamp(c.Snippet.y, 0f, BoardMaxY);
                c.Apply();
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
                        var r = new Rect(x, y, size.x, size.y);
                        float blockBottom = -1f;
                        foreach (var t in taken)
                            if (r.xMin < t.xMax + BoardGap - 0.01f && t.xMin < r.xMax + BoardGap - 0.01f &&
                                r.yMin < t.yMax + BoardGap - 0.01f && t.yMin < r.yMax + BoardGap - 0.01f)
                                blockBottom = Mathf.Max(blockBottom, t.yMax);
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

        private void UpdateBoardHeight()
        {
            float bottom = 0f;
            foreach (var c in _cards) bottom = Mathf.Max(bottom, c.Snippet.y + c.Size.y);
            _memoContent.sizeDelta = new Vector2(0f, Mathf.Max(ViewH, bottom + BoardGap));
        }

        /// <summary>
        /// カードを動かす。ほかのカードにぶつかる所では、その境目で止まって横に貼り付く（相手は動かない）。
        /// 横と縦を分けて少しずつ進めるので、角ではぶつかった方向だけ止まり、もう一方へは滑る
        /// </summary>
        internal void MoveCard(SnippetCard card, Vector2 delta)
        {
            if (delta.sqrMagnitude < 0.0001f) return;
            var r = card.Rect;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 16f));
            var step = delta / steps;
            for (int k = 0; k < steps; k++)
            {
                r.x += Sweep(r, step.x, card, horizontal: true);
                r.y += Sweep(r, step.y, card, horizontal: false);
            }
            card.Snippet.x = r.x;
            card.Snippet.y = r.y;
            card.Apply();
            UpdateBoardHeight();
        }

        /// <summary>r を d だけ動かせる量（ぶつかるカード・ボードの端で止める）</summary>
        private float Sweep(Rect r, float d, SnippetCard self, bool horizontal)
        {
            const float Eps = 0.01f;
            if (Mathf.Abs(d) < 0.0001f) return 0f;
            foreach (var o in _cards)
            {
                if (o == self) continue;
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

        internal void BeginDrag(SnippetCard card, PointerEventData e)
        {
            if (!BoardPoint(e.position, e.pressEventCamera, out var p)) return;
            _grab = p - new Vector2(card.Snippet.x, card.Snippet.y);
            card.transform.SetAsLastSibling();   // 動かしている間は一番上に
            Select(card.Button);
        }

        internal void DragCard(SnippetCard card, PointerEventData e)
        {
            if (!BoardPoint(e.position, e.pressEventCamera, out var p)) return;
            MoveCard(card, p - _grab - new Vector2(card.Snippet.x, card.Snippet.y));
            ScrollTo(card);
        }

        /// <summary>カードが見える所までボードを送る</summary>
        private void ScrollTo(SnippetCard card)
        {
            float viewH = ViewH, contentH = _memoContent.rect.height;
            float scroll = _memoContent.anchoredPosition.y;
            float top = card.Snippet.y, bottom = top + card.Size.y;
            if (bottom > scroll + viewH) scroll = bottom - viewH;
            if (top < scroll) scroll = top;
            _memoContent.anchoredPosition = new Vector2(0f, Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentH - viewH)));
        }

        /// <summary>全部のカードを一覧の並びに戻す</summary>
        private void ResetBoard()
        {
            if (MemoSnippets.Count == 0) return;
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var card = sel != null ? sel.GetComponent<SnippetCard>() : null;
            if (card != null) _reselectSnippet = card.Snippet.id;
            _memoContent.anchoredPosition = Vector2.zero;
            MemoSnippets.ResetPlacement();
            UiSound.Decide();
        }

        /// <summary>矢印キーでの選択の移動を止める（Shift＋矢印でカードを動かしている間）</summary>
        private static void SetNavigation(bool on)
        {
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = on;
        }

        private static string SourceTitle(string entryId)
        {
            foreach (var e in Notebook.Entries) if (e.id == entryId) return e.title;
            // 手帳に載っていない資料（途中から始めた時など）は部屋に置いてある資料の名前
            foreach (var f in FindObjectsByType<LoopFindable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if ($"{f.RoomId}_{f.Id}" == entryId) return string.IsNullOrEmpty(f.Title) ? f.Name : f.Title;
            return entryId;
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
            if (_overlay)
            {
                // 資料の画面から開いている：閉じると資料へ戻る（開いた時と同じキーでは閉じない）
                if (back && Time.frameCount != _ovOpenedFrame) { CloseOverlay(); return; }
            }
            else if (prev || next)
            {
                if (_tab == Tab.Docs && prev) SetChapter(_chapter - 1 < 0 ? ChapterNames.Length - 1 : _chapter - 1);
                SetTab(_tab == Tab.Docs ? Tab.Memo : Tab.Docs);
                UiSound.Move();
                return;
            }
            if (_tab != Tab.Memo) return;
            SetNavigation(!shift);   // Shift を押している間は矢印で選択を移さない（カードを動かす）
            if (reset) { ResetBoard(); return; }
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var card = sel != null ? sel.GetComponent<SnippetCard>() : null;
            if (card == null) return;
            if (del) { RemoveSnippet(card.Snippet); return; }
            if (move.sqrMagnitude > 0.0001f)
            {
                if (move.sqrMagnitude > 1f) move.Normalize();
                card.transform.SetAsLastSibling();
                MoveCard(card, new Vector2(move.x, -move.y) * MoveSpeed * Time.unscaledDeltaTime);
                ScrollTo(card);
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
    /// メモの1枚（切り取った文＋元の資料名）。ボードの上で自由に動かせる（ほかのカードとは重ならない）。
    /// 右クリックで消す、ダブルクリックで元の資料へ。大きさは文の長さに合わせる（幅は上限まで、あとは折り返す）
    /// </summary>
    public class SnippetCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public MemoSnippet Snippet;
        public Button Button;
        private NotebookUI _owner;

        // 内側の地は不透明（枠の色の上に重ねるので、透けると枠の色が中まで滲む）
        private static readonly Color CardBg = UiTheme.Hex(0x050506);
        private static readonly Color CardBgOn = UiTheme.Hex(0x121110);
        private static readonly Color CardFrame = UiTheme.WithAlpha(UiTheme.Text, 0.12f);
        private const int PadL = 22, PadR = 20, PadT = 12, PadB = 12;

        public Vector2 Size => ((RectTransform)transform).rect.size;
        /// <summary>ボード上の場所（左上が原点、下が +y）</summary>
        public Rect Rect => new Rect(Snippet.x, Snippet.y, Size.x, Size.y);
        public void Apply() => ((RectTransform)transform).anchoredPosition = new Vector2(Snippet.x, -Snippet.y);

        public static SnippetCard Create(RectTransform parent, NotebookUI owner, MemoSnippet s, string source, float minW, float maxW)
        {
            var btn = UiTheme.MenuItem(parent, "", null, maxW, 60f, 24);
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

            var text = go.transform.Find("Label").GetComponent<Text>();
            text.alignment = TextAnchor.UpperLeft;
            text.lineSpacing = 1.3f;
            text.text = "「" + s.text + "」";
            var lift = text.GetComponent<Shadow>();
            if (lift == null) lift = text.gameObject.AddComponent<Shadow>();
            lift.effectColor = new Color(0f, 0f, 0f, 0.8f);
            lift.effectDistance = new Vector2(1f, -1.5f);
            var src = UiTheme.Label(go.transform, "Source", UiTheme.FsSmall, TextAnchor.UpperRight, UiTheme.TextSub, shadow: false);
            src.text = "── " + source;
            var mark = go.transform.Find("Mark");
            if (mark != null) mark.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            // 幅は文の長さに合わせる（上限を超えたら折り返す）→ 高さは中身から決まる
            float w = Mathf.Clamp(Mathf.Max(text.preferredWidth, src.preferredWidth) + PadL + PadR + 4f, minW, maxW);
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
                    owner.OpenSource(s);
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
            if (e.button == PointerEventData.InputButton.Left) _owner.DragCard(this, e);
        }

        public void OnEndDrag(PointerEventData e) { }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) _owner.RemoveSnippet(Snippet);
            else if (e.button == PointerEventData.InputButton.Left && e.clickCount >= 2) _owner.OpenSource(Snippet);
        }
    }
}
