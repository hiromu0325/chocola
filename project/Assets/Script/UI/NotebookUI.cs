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
    /// ・メモ … 資料の本文にラインマーカーを引いて切り取った文の一覧。ドラッグ（または Shift＋↑↓）で並び替え、
    ///          右クリック（Delete）で消す。ダブルクリック（Enter）で元の資料のその場所を開く
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
        private Image _insertLine;
        private string _reselectEntry;
        private int _reselectSnippet = -1;

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
            _memoScroll = MakeScroll(page, Vector2.zero, out _memoContent);
            _memoHint = UiTheme.Label(page, "Empty", UiTheme.FsBody, TextAnchor.MiddleCenter, UiTheme.TextSub, shadow: false);
            UiTheme.Stretch(_memoHint.rectTransform);
            _memoHint.lineSpacing = 1.6f;
            _memoHint.text = "まだ何も書き写していない。\n資料を開いて「読む」と、本文をなぞってラインマーカーを引ける。\n引いた文はここに並び、自由に並べ替えて考えをまとめられる。";
            _insertLine = UiTheme.Fill(_memoContent, "Insert", UiTheme.Accent);
            _insertLine.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _insertLine.rectTransform.anchorMin = new Vector2(0f, 1f); _insertLine.rectTransform.anchorMax = new Vector2(1f, 1f);
            _insertLine.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _insertLine.rectTransform.sizeDelta = new Vector2(0f, 3f);
            _insertLine.gameObject.SetActive(false);
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
            _insertLine.gameObject.SetActive(false);
        }

        private void SetTab(Tab t)
        {
            _tab = t;
            _docsPage.SetActive(t == Tab.Docs);
            _memoPage.SetActive(t == Tab.Memo);
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
                : $"ドラッグ／{UiTheme.Key("Shift＋↑↓")} 並べ替え　　右クリック／{UiTheme.Key("Delete")} 消す　　ダブルクリック／{UiTheme.Key("Enter", "A")} 元の資料　　{UiTheme.Key("Q", "LB")}{UiTheme.Key("E", "RB")} 資料／メモ　　{UiTheme.Key("Tab", "View")} 閉じる";
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
            _book.gameObject.SetActive(false);
            _reselectEntry = r.Entry.id;
            iv.Open(new InspectView.Request
            {
                EntryId = r.Entry.id, RoomId = r.Room, DocId = r.Doc ?? r.Entry.id,
                Title = r.Entry.title, Body = r.Entry.body, Info = info, Clips = clips,
                FromNotebook = true, StartInRead = read,
                OnClosed = all => { if (all) RequestClose?.Invoke(); else { _book.gameObject.SetActive(true); Rebuild(); FocusFirst(); } },
            });
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
                var card = SnippetCard.Create(_memoContent, this, all[i], i, SourceTitle(all[i].entryId));
                _cards.Add(card);
                _rows.Add(card.Button);
            }
            UiTheme.ChainVertical(_rows);
            _insertLine.transform.SetAsLastSibling();
            if (_reselectSnippet >= 0)
            {
                foreach (var c in _cards) if (c.Snippet.id == _reselectSnippet) { Select(c.Button); break; }
                _reselectSnippet = -1;
            }
        }

        private static string SourceTitle(string entryId)
        {
            foreach (var e in Notebook.Entries) if (e.id == entryId) return e.title;
            // 手帳に載っていない資料（途中から始めた時など）は部屋に置いてある資料の名前
            foreach (var f in FindObjectsByType<LoopFindable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if ($"{f.RoomId}_{f.Id}" == entryId) return string.IsNullOrEmpty(f.Title) ? f.Name : f.Title;
            return entryId;
        }

        /// <summary>カードをドラッグ中：差し込む位置に線を出す。戻り値＝差し込む番号</summary>
        internal int DragOver(Vector2 screen, Camera cam)
        {
            int index = _cards.Count;
            float lineY = 0f;
            for (int i = 0; i < _cards.Count; i++)
            {
                var rt = (RectTransform)_cards[i].transform;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, cam, out var local);
                if (local.y > 0f) { index = i; break; }   // カードの中心より上
            }
            if (_cards.Count > 0)
            {
                var refCard = (RectTransform)_cards[Mathf.Min(index, _cards.Count - 1)].transform;
                float top = refCard.anchoredPosition.y + refCard.rect.height * (1f - refCard.pivot.y);
                float bottom = refCard.anchoredPosition.y - refCard.rect.height * refCard.pivot.y;
                lineY = index < _cards.Count ? top + UiTheme.Sp1 : bottom - UiTheme.Sp1;
            }
            _insertLine.gameObject.SetActive(true);
            _insertLine.rectTransform.anchoredPosition = new Vector2(0f, lineY);
            _insertLine.transform.SetAsLastSibling();
            return index;
        }

        internal void DragEnd(int from, int to)
        {
            _insertLine.gameObject.SetActive(false);
            if (to > from) to--;
            if (to != from)
            {
                _reselectSnippet = MemoSnippets.All[from].id;
                MemoSnippets.Move(from, to);
                UiSound.Decide();
            }
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
            if (InspectView.IsOpen) return;
            bool prev = false, next = false, up = false, down = false, del = false, shift = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            if (kb != null)
            {
                prev = kb.qKey.wasPressedThisFrame; next = kb.eKey.wasPressedThisFrame;
                shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                up = kb.upArrowKey.wasPressedThisFrame; down = kb.downArrowKey.wasPressedThisFrame;
                del = kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
            }
            if (gp != null)
            {
                prev |= gp.leftShoulder.wasPressedThisFrame; next |= gp.rightShoulder.wasPressedThisFrame;
                del |= gp.buttonNorth.wasPressedThisFrame;
                if (gp.buttonWest.isPressed) { shift = true; up |= gp.dpad.up.wasPressedThisFrame; down |= gp.dpad.down.wasPressedThisFrame; }
            }
#else
            prev = Input.GetKeyDown(KeyCode.Q); next = Input.GetKeyDown(KeyCode.E);
            shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            up = Input.GetKeyDown(KeyCode.UpArrow); down = Input.GetKeyDown(KeyCode.DownArrow);
            del = Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace);
#endif
            if (prev || next)
            {
                if (_tab == Tab.Docs && prev) SetChapter(_chapter - 1 < 0 ? ChapterNames.Length - 1 : _chapter - 1);
                SetTab(_tab == Tab.Docs ? Tab.Memo : Tab.Docs);
                UiSound.Move();
                return;
            }
            if (_tab != Tab.Memo) return;
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var card = sel != null ? sel.GetComponent<SnippetCard>() : null;
            if (card == null) return;
            int i = MemoSnippets.IndexOf(card.Snippet.id);
            if (del) { RemoveSnippet(card.Snippet); return; }
            if (shift && (up || down))
            {
                int to = Mathf.Clamp(i + (up ? -1 : 1), 0, MemoSnippets.Count - 1);
                if (to != i) { _reselectSnippet = card.Snippet.id; MemoSnippets.Move(i, to); UiSound.Move(); }
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
                if (c.name == "Insert") continue;
                c.SetParent(null);
                Destroy(c.gameObject);
            }
        }
    }

    /// <summary>メモの1枚（切り取った文＋元の資料名）。ドラッグで並べ替え、右クリックで消す、ダブルクリックで元の資料へ</summary>
    public class SnippetCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public MemoSnippet Snippet;
        public Button Button;
        private NotebookUI _owner;
        private int _index, _dropAt;
        private CanvasGroup _cg;

        // 内側の地は不透明（枠の色の上に重ねるので、透けると枠の色が中まで滲む）
        private static readonly Color CardBg = UiTheme.Hex(0x050506);
        private static readonly Color CardBgOn = UiTheme.Hex(0x121110);
        private static readonly Color CardFrame = UiTheme.WithAlpha(UiTheme.Text, 0.12f);

        public static SnippetCard Create(RectTransform parent, NotebookUI owner, MemoSnippet s, int index, string source)
        {
            var btn = UiTheme.MenuItem(parent, "", null, 1300f, 60f, 24);
            var go = btn.gameObject;
            go.name = "Card_" + s.id;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = -1f;
            // 地は黒、細い枠。文字は白（読む画面でマーカーを引いて浮き上がった文字と同じ見た目）。
            // 項目の Image を枠の色にして、内側を黒で塗る（どの解像度でも枠が同じ太さで出る）
            var frame = go.GetComponent<Image>();
            frame.color = CardFrame;
            var bg = UiTheme.Fill(go.transform, "Fill", CardBg);
            bg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UiTheme.Stretch(bg.rectTransform, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline, UiTheme.Hairline);
            bg.transform.SetAsFirstSibling();
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 24, 12, 12);
            v.spacing = UiTheme.Sp1;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;

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

            var fx = go.GetComponent<UiItemFx>();
            fx.ShiftText = false;   // 並びはレイアウトが決めるので文字はずらさない
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
            card._index = index;
            card._cg = go.AddComponent<CanvasGroup>();
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
            if (e.button != PointerEventData.InputButton.Left) return;
            _dropAt = _index;
            _cg.alpha = 0.55f;
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            _dropAt = _owner.DragOver(e.position, e.pressEventCamera);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            _cg.alpha = 1f;
            _owner.DragEnd(_index, _dropAt);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) _owner.RemoveSnippet(Snippet);
            else if (e.button == PointerEventData.InputButton.Left && e.clickCount >= 2) _owner.OpenSource(Snippet);
        }
    }
}
