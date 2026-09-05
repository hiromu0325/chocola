using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 手帳の見開き描画＋証拠つなぎボード。
    /// ・手帳の本文から「キーワード」を検出し、当たり判定つきのチップとして描画する
    ///   （ホバーで赤い枠）。キーワードの括りは MemoKeywordConfig で指定できる
    /// ・チップからチップへドラッグすると赤い線で関係を結べる（同じ組をもう一度で解除）。
    ///   接続はチップの上（近傍）で離したときだけ成立する。
    ///   チップは出現箇所ごとに区別される（＝別の資料に出た同じワード同士も結べる）
    /// ・表示順・見出し書式・色は MemoLayout（Resources/MemoBoardLayout.txt）で制御する
    /// ・右クリックでページ送り（最後まで行くと先頭へループ）
    /// </summary>
    public class MemoBoard : MonoBehaviour
    {
        private const int LinesPerPage = 17;
        private const int FontSize = 25;
        private const float LineH = 33f;
        private const float PageW = 560f;
        /// <summary>チップ外で離したとき接続を成立させる最大距離（ボード座標）</summary>
        private const float SnapRadius = 55f;

        private struct MemoLine { public string text; public Color color; public string entryId; }

        private Font _font;
        private RectTransform _board;        // 手帳パネル全体（線の座標基準）
        private RectTransform _leftPage, _rightPage, _lineLayer;
        private Text _pageLabel;

        private readonly List<List<MemoLine>> _pages = new List<List<MemoLine>>();
        private int _spread;

        // ---- タブ（章ごとの資料 ＋ 証拠メモ）----
        // 0=序(チュートリアル) 1=1章 2=2章 3=3章 4=終章 5=証拠メモ
        private const int EvidenceTab = 5;
        private int _tab = 0;
        private readonly Button[] _tabs = new Button[6];
        private static readonly string[] TabNames = { "序", "1章", "2章", "3章", "終章", "メモ" };
        private static readonly string[] ChapterTitles =
        {
            "序　── 目覚め ──",
            "1章　佐伯恒一　── 本人を本人たらしめるものは何か ──",
            "2章　水野美奈　── 善意はどこまで許されるのか ──",
            "3章　黒田恒一　── 正しいことと救うことは同じではない ──",
            "終章　RENASCITA",
        };

        /// <summary>部屋Id → 章（0..4）。未知の部屋は -1</summary>
        private static readonly Dictionary<string, int> RoomChapter = new Dictionary<string, int>
        {
            { "dim", 0 }, { "train", 0 }, { "lab", 0 },
            { "study", 1 }, { "analysis", 1 }, { "saeki_home", 1 },
            { "ward", 2 }, { "core_ante", 2 }, { "mizuno_apart", 2 },
            { "data_room", 3 }, { "system_room", 3 }, { "kuroda_home", 3 },
            { "core_main", 4 }, { "son_room", 4 },
        };
        /// <summary>章内での部屋の並び（起・転・結の順）</summary>
        private static readonly string[] RoomOrder =
        {
            "dim", "train", "lab", "study", "analysis", "saeki_home", "ward", "core_ante", "mizuno_apart",
            "data_room", "system_room", "kuroda_home", "core_main", "son_room",
        };
        /// <summary>残響Id → それを見た部屋</summary>
        private static readonly Dictionary<string, string> EchoRoom = new Dictionary<string, string>
        {
            { "study_secret", "study" }, { "argue_saeki", "saeki_home" }, { "saeki_wife", "saeki_home" },
            { "talk_me", "ward" }, { "mizuno_uncle", "mizuno_apart" }, { "talk_mizuno", "mizuno_apart" },
            { "argue_kuroda", "data_room" }, { "kuroda_family", "kuroda_home" },
        };

        /// <summary>手帳エントリId → 属する部屋Id（章分けと見出しに使う）。証拠メモは null</summary>
        private static string RoomOfEntry(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("echo_"))
                return EchoRoom.TryGetValue(id.Substring(5), out var er) ? er : "dim";
            if (id.StartsWith("attack_"))
                return RoomChapter.ContainsKey(id.Substring(7)) ? id.Substring(7) : "dim";
            if (id.StartsWith("unlock_"))
            {
                // unlock_<stage>: 解放された部屋の"前"の部屋の記録として、直前の部屋に置く
                if (int.TryParse(id.Substring(7), out int stage) && stage - 1 >= 0 && stage - 1 < RoomOrder.Length)
                    return RoomOrder[stage - 1];
                return "dim";
            }
            foreach (var r in RoomOrder)
                if (id.StartsWith(r + "_")) return r;
            return null;
        }

        private static int ChapterOf(string id)
        {
            var room = RoomOfEntry(id);
            return room != null && RoomChapter.TryGetValue(room, out int ch) ? ch : -1;
        }

        private static string RoomDisplayName(string roomId)
        {
            var r = LoopRooms.Get(roomId);
            return r != null ? r.DisplayName : roomId;
        }
        // ノードID（エントリid:ワード:出現順） → チップ。出現箇所ごとに独立したノードなので
        // 「アルバムの佐藤」と「人事ファイルの佐藤」のような同一ワード同士も結べる
        private readonly Dictionary<string, MemoKeywordChip> _chips = new Dictionary<string, MemoKeywordChip>();
        private readonly List<MemoKeywordChip> _allChips = new List<MemoKeywordChip>();
        // ノードIDの出現順カウンタ（見開きの描画ごとにリセット）
        private readonly Dictionary<string, int> _chipOccurrence = new Dictionary<string, int>();

        // 結ばれた関係（ノードIDのペア。セッション中保持）
        private static readonly HashSet<string> Connections = new HashSet<string>();
        private static string PairKey(string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? a + "\n" + b : b + "\n" + a;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Connections.Clear();
            _keywordCache = null;
            _entityMap = null;
        }

        private MemoKeywordChip _dragFrom;
        private RectTransform _dragLine;

        // ============================== 構築 ==============================

        public void Init(RectTransform boardPanel, Font font)
        {
            _font = font;
            _board = boardPanel;

            _lineLayer = MakeLayer("ConnectionLines");
            _leftPage = MakeLayer("PageLeft");
            _rightPage = MakeLayer("PageRight");
            SetRect(_leftPage, new Vector2(-592f, 282f), new Vector2(PageW, 600f));
            SetRect(_rightPage, new Vector2(32f, 282f), new Vector2(PageW, 600f));
            SetRect(_lineLayer, Vector2.zero, Vector2.zero);
            _lineLayer.anchorMin = Vector2.zero; _lineLayer.anchorMax = Vector2.one;
            _lineLayer.offsetMin = Vector2.zero; _lineLayer.offsetMax = Vector2.zero;

            // タブ（左上）。章ごとの資料と証拠メモを切り替える
            for (int i = 0; i < _tabs.Length; i++)
            {
                int tab = i;
                _tabs[i] = MakeTab("Tab_" + TabNames[i], TabNames[i], new Vector2(-592f + i * 118f, 336f),
                    () => SetTab(tab));
            }
            UpdateTabVisual();

            var labelGo = new GameObject("PageLabel");
            labelGo.transform.SetParent(_board, false);
            _pageLabel = labelGo.AddComponent<Text>();
            _pageLabel.font = _font; _pageLabel.fontSize = 22;
            _pageLabel.alignment = TextAnchor.LowerCenter;
            _pageLabel.color = new Color(0.7f, 0.7f, 0.75f);
            var lr = _pageLabel.rectTransform;
            lr.anchorMin = new Vector2(0.5f, 0f); lr.anchorMax = new Vector2(0.5f, 0f);
            lr.anchoredPosition = new Vector2(0f, 14f); lr.sizeDelta = new Vector2(1200f, 36f);
        }

        private Button MakeTab(string name, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_board, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.23f, 0.2f, 0.9f);
            var rt = img.rectTransform;
            SetRect(rt, pos, new Vector2(112f, 40f));
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);
            var txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.AddComponent<Text>();
            txt.font = _font;
            txt.fontSize = 22;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = label;
            txt.color = Color.white;
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            return btn;
        }

        private void SetTab(int tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            _spread = 0;
            UpdateTabVisual();
            RebuildAndShow();
        }

        /// <summary>今いる部屋の章を開く（手帳を開いた瞬間に呼ぶ）</summary>
        public void JumpToCurrentChapter()
        {
            var room = LoopRooms.CurrentRoomId;
            if (room != null && RoomChapter.TryGetValue(room, out int ch) && ch != _tab)
            {
                _tab = ch;
                _spread = 0;
                UpdateTabVisual();
            }
        }

        private void UpdateTabVisual()
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                bool active = _tab == i;
                bool hasEntries = false, flagged = false;
                foreach (var e in Notebook.Entries)
                {
                    int ch = ChapterOf(e.id);
                    bool inTab = i == EvidenceTab ? ch < 0 : ch == i;
                    if (!inTab) continue;
                    hasEntries = true;
                    if (Notebook.IsFlagged(e.id)) flagged = true;
                }
                var img = _tabs[i].GetComponent<Image>();
                img.color = active ? new Color(0.55f, 0.45f, 0.25f, 0.95f)
                          : hasEntries ? new Color(0.25f, 0.23f, 0.2f, 0.9f)
                          : new Color(0.16f, 0.15f, 0.14f, 0.7f);
                var label = _tabs[i].GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = flagged ? TabNames[i] + " <color=#FFB040>!</color>" : TabNames[i];
                    label.supportRichText = true;
                    label.color = hasEntries || active ? Color.white : new Color(0.55f, 0.55f, 0.55f);
                }
            }
        }

        private RectTransform MakeLayer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_board, false);
            return go.AddComponent<RectTransform>();
        }

        private static void SetRect(RectTransform rt, Vector2 topLeftPos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeftPos;
            if (size != Vector2.zero) rt.sizeDelta = size;
        }

        // ============================== ページ生成 ==============================

        public void RebuildAndShow()
        {
            BuildPages();
            _spread = Mathf.Clamp(_spread, 0, MaxSpread());
            RenderSpread();
        }

        public void NextSpreadLooped()
        {
            _spread = _spread >= MaxSpread() ? 0 : _spread + 1;   // 末尾まで行ったら先頭へループ
            RenderSpread();
        }

        private int MaxSpread() => Mathf.Max(0, (_pages.Count - 1) / 2);

        private void BuildPages()
        {
            _pages.Clear();
            var layout = MemoLayout.Get();
            var lines = new List<MemoLine>();
            // 現在のタブに属するエントリだけを対象にし、章内は部屋（起・転・結）の順に並べる
            var tabEntries = new List<NotebookEntry>();
            foreach (var e in layout.SortEntries(Notebook.Entries))
            {
                int ch = ChapterOf(e.id);
                if (_tab == EvidenceTab ? ch < 0 : ch == _tab) tabEntries.Add(e);
            }
            if (_tab != EvidenceTab)
            {
                int Order(NotebookEntry e) => System.Array.IndexOf(RoomOrder, RoomOfEntry(e.id));
                // 安定ソート（同じ部屋の中は発見順を保つ）
                var indexed = new List<(int order, int idx, NotebookEntry e)>();
                for (int i = 0; i < tabEntries.Count; i++) indexed.Add((Order(tabEntries[i]), i, tabEntries[i]));
                indexed.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.idx.CompareTo(b.idx));
                tabEntries.Clear();
                foreach (var t in indexed) tabEntries.Add(t.e);
            }

            var chapterColor = new Color(1f, 0.85f, 0.55f);
            var roomColor = new Color(0.75f, 0.7f, 0.6f);
            var flagColor = new Color(1f, 0.7f, 0.3f);

            if (_tab != EvidenceTab)
            {
                lines.Add(new MemoLine { text = ChapterTitles[_tab], color = chapterColor });
                lines.Add(new MemoLine { text = "", color = Color.white });
            }

            if (tabEntries.Count == 0)
            {
                if (_tab != EvidenceTab)
                {
                    lines.Add(new MemoLine { text = "この章の資料は、まだ見つかっていない。", color = Color.white });
                    lines.Add(new MemoLine { text = "部屋で見つけた資料は、", color = Color.white });
                    lines.Add(new MemoLine { text = "ここで読み返せる。", color = Color.white });
                }
                else
                {
                    lines.Add(new MemoLine { text = "まだ何も書かれていない。", color = Color.white });
                    lines.Add(new MemoLine { text = "手がかりを見つけると、", color = Color.white });
                    lines.Add(new MemoLine { text = "ここに書き留められる。", color = Color.white });
                }
            }
            else
            {
                string lastRoom = null;
                foreach (var e in tabEntries)
                {
                    var rule = layout.RuleFor(e.id);
                    if (rule.hide) continue;

                    // 部屋の見出し（章タブのみ）
                    string room = RoomOfEntry(e.id);
                    if (_tab != EvidenceTab && room != null && room != lastRoom)
                    {
                        lines.Add(new MemoLine { text = "──　" + RoomDisplayName(room) + "　──", color = roomColor });
                        lastRoom = room;
                    }
                    // 付箋（ギミックの誤答で立つ。読み直すべき資料の目印）
                    if (Notebook.IsFlagged(e.id))
                        lines.Add(new MemoLine { text = "▶ 付箋：ここに手がかりがある", color = flagColor, entryId = e.id });
                    if (rule.lines.Count > 0)
                    {
                        // line 命令あり：全文をテンプレートで組む（1行ずつ文章と色を完全指定）
                        foreach (var def in rule.lines)
                        {
                            Color c = def.hasColor ? def.color : rule.bodyColor;
                            string expanded = MemoLayout.Rule.Expand(def.template, e);
                            // {body} 等で複数行に展開された場合はそれぞれ折り返して積む
                            foreach (var raw in expanded.Split('\n'))
                                Wrap(lines, raw, layout.WrapChars, c, e.id);
                        }
                    }
                    else
                    {
                        // line 命令なし：従来どおり 見出し＋本文 の自動描画
                        lines.Add(new MemoLine { text = rule.FormatTitle(e), color = rule.titleColor, entryId = e.id });
                        foreach (var raw in e.body.Split('\n'))
                            Wrap(lines, raw, layout.WrapChars, rule.bodyColor, e.id);
                    }
                    for (int b = 0; b < rule.blankAfter; b++)
                        lines.Add(new MemoLine { text = "", color = Color.white });
                }
            }
            for (int i = 0; i < lines.Count; i += LinesPerPage)
            {
                int n = Mathf.Min(LinesPerPage, lines.Count - i);
                _pages.Add(lines.GetRange(i, n));
            }
            if (_pages.Count == 0) _pages.Add(new List<MemoLine>());
        }

        private static void Wrap(List<MemoLine> acc, string line, int wrapChars, Color color, string entryId)
        {
            if (line.Length <= wrapChars)
            {
                acc.Add(new MemoLine { text = line, color = color, entryId = entryId });
                return;
            }
            for (int p = 0; p < line.Length; p += wrapChars)
                acc.Add(new MemoLine
                {
                    text = line.Substring(p, Mathf.Min(wrapChars, line.Length - p)),
                    color = color,
                    entryId = entryId
                });
        }

        // ============================== 見開き描画 ==============================

        private void RenderSpread()
        {
            ClearChildren(_leftPage); ClearChildren(_rightPage); ClearChildren(_lineLayer);
            _chips.Clear(); _allChips.Clear(); _chipOccurrence.Clear();
            _dragFrom = null; _dragLine = null;

            int li = _spread * 2, ri = li + 1;
            if (li < _pages.Count) RenderPage(_leftPage, _pages[li]);
            if (ri < _pages.Count) RenderPage(_rightPage, _pages[ri]);

            int shownTo = Mathf.Min(ri + 1, _pages.Count);
            _pageLabel.text = $"— {li + 1}〜{shownTo} / {_pages.Count} ページ —　右クリック:ページ送り(ループ)　" +
                              "ドラッグで結ぶ(ワードの上で離す／同じ組をもう一度で解除)　[Tab]閉じる";
            RedrawConnections();
        }

        private static void ClearChildren(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) Destroy(rt.GetChild(i).gameObject);
        }

        private void RenderPage(RectTransform page, List<MemoLine> lines)
        {
            var keywords = CollectKeywords();
            float y = 0f;
            foreach (var line in lines)
            {
                float x = 0f;
                foreach (var (text, isKeyword) in Segment(line.text, keywords))
                {
                    float w = EstimateWidth(text);
                    if (isKeyword)
                        MakeChip(page, text, new Vector2(x, y), w, line.entryId);
                    else
                        MakeText(page, text, new Vector2(x, y), w, line.color);
                    x += w;
                }
                y -= LineH;
            }
        }

        private Text MakeText(RectTransform parent, string text, Vector2 pos, float w, Color color)
        {
            var go = new GameObject("T");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = FontSize; t.text = text;
            t.color = color; t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(Mathf.Max(w, 10f), LineH);
            return t;
        }

        private void MakeChip(RectTransform parent, string keyword, Vector2 pos, float w, string entryId)
        {
            var go = new GameObject("Chip_" + keyword);
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.02f);   // ほぼ透明（当たり判定用）
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos + new Vector2(-2f, 2f);
            rt.sizeDelta = new Vector2(w + 4f, LineH);

            var label = MakeText(rt, keyword, new Vector2(2f, -2f), w, new Color(1f, 0.88f, 0.45f));
            label.raycastTarget = false;

            // 赤い枠（ホバー時のみ表示。上下左右の細線4本）
            var frame = new GameObject("Frame");
            frame.transform.SetParent(rt, false);
            var frt = frame.AddComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            MakeEdge(frt, new Vector2(0.5f, 1f), new Vector2(0f, 2f), true);
            MakeEdge(frt, new Vector2(0.5f, 0f), new Vector2(0f, 2f), true);
            MakeEdge(frt, new Vector2(0f, 0.5f), new Vector2(2f, 0f), false);
            MakeEdge(frt, new Vector2(1f, 0.5f), new Vector2(2f, 0f), false);
            frame.SetActive(false);

            // ノードID＝「どのエントリの、どのワードの、何回目の出現か」。
            // エントリ基準なのでページの組み替えや資料の追加でズレない
            string occKey = entryId + ":" + keyword;
            _chipOccurrence.TryGetValue(occKey, out int occ);
            _chipOccurrence[occKey] = occ + 1;
            string nodeId = occKey + ":" + occ;

            var chip = go.AddComponent<MemoKeywordChip>();
            chip.Setup(this, keyword, EntityOf(keyword), nodeId, frame);
            _chips[nodeId] = chip;
            _allChips.Add(chip);
        }

        private static void MakeEdge(RectTransform frame, Vector2 anchor, Vector2 thickness, bool horizontal)
        {
            var go = new GameObject("Edge");
            go.transform.SetParent(frame, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 0.15f, 0.15f, 0.95f);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            if (horizontal)
            {
                rt.anchorMin = new Vector2(0f, anchor.y); rt.anchorMax = new Vector2(1f, anchor.y);
                rt.sizeDelta = new Vector2(0f, thickness.y);
            }
            else
            {
                rt.anchorMin = new Vector2(anchor.x, 0f); rt.anchorMax = new Vector2(anchor.x, 1f);
                rt.sizeDelta = new Vector2(thickness.x, 0f);
            }
            rt.anchoredPosition = Vector2.zero;
        }

        // ============================== キーワード ==============================

        private static List<string> _keywordCache;
        private static Dictionary<string, string> _entityMap;   // キーワード → エンティティID

        /// <summary>
        /// キーワード表を構築。括りは MemoKeywordConfig（Resources）で指定できる。
        /// 既定：同一人物の情報（氏名・社員番号・生年月日・特徴）は同じ括り、
        /// 型番と復旧コードは同一装置、部署は複数人にまたがるため独立の括り。
        /// 手動指定（customGroups / extraKeywords）は自動の括りより優先される。
        /// </summary>
        private static List<string> CollectKeywords()
        {
            if (_keywordCache != null) return _keywordCache;
            _entityMap = new Dictionary<string, string>();
            var cfg = MemoKeywordConfig.Instance;   // 無ければ既定動作

            void Map(string kw, string entity)
            {
                if (!string.IsNullOrEmpty(kw) && !_entityMap.ContainsKey(kw)) _entityMap[kw] = entity;
            }

            // 手動指定の括りを最優先で登録
            if (cfg != null && cfg.customGroups != null)
            {
                foreach (var g in cfg.customGroups)
                {
                    if (g == null || g.keywords == null || g.keywords.Length == 0) continue;
                    string ent = "custom:" + (string.IsNullOrEmpty(g.groupId) ? g.keywords[0] : g.groupId);
                    foreach (var kw in g.keywords) Map(kw, ent);
                }
            }
            if (cfg != null && cfg.extraKeywords != null)
                foreach (var kw in cfg.extraKeywords) Map(kw, "kw:" + kw);

            bool autoEmp = cfg == null || cfg.autoEmployeeKeywords;
            bool groupEmp = cfg == null || cfg.groupEmployeeFields;
            bool dept = cfg == null || cfg.departmentKeywords;
            bool autoMdl = cfg == null || cfg.autoModelKeywords;
            bool groupMdl = cfg == null || cfg.groupModelWithCode;

            if (autoEmp)
            {
                foreach (var e in PuzzleState.Employees)
                {
                    string ent = "emp:" + e.number;
                    Map(e.name, groupEmp ? ent : "kw:" + e.name);
                    Map(e.number, groupEmp ? ent : "kw:" + e.number);
                    Map(e.birthdate, groupEmp ? ent : "kw:" + e.birthdate);
                    Map(e.feature, groupEmp ? ent : "kw:" + e.feature);
                    if (dept) Map(e.department, "dept:" + e.department);
                }
            }
            if (autoMdl)
            {
                foreach (var m in PuzzleState.Models)
                {
                    string ent = "mdl:" + m.model;
                    Map(m.model, groupMdl ? ent : "kw:" + m.model);
                    Map(m.code, groupMdl ? ent : "kw:" + m.code);
                }
            }

            // キーワード化を止める語
            if (cfg != null && cfg.excludedWords != null)
                foreach (var w in cfg.excludedWords)
                    if (!string.IsNullOrEmpty(w)) _entityMap.Remove(w);

            _keywordCache = new List<string>(_entityMap.Keys);
            // 長い語を先に照合（「1234」より「12345」を優先）
            _keywordCache.Sort((a, b) => b.Length.CompareTo(a.Length));
            return _keywordCache;
        }

        private static string EntityOf(string keyword)
        {
            CollectKeywords();
            return _entityMap.TryGetValue(keyword, out var e) ? e : keyword;
        }

        /// <summary>行をキーワード／平文のセグメント列に分解</summary>
        private static IEnumerable<(string text, bool keyword)> Segment(string line, List<string> keywords)
        {
            int pos = 0;
            while (pos < line.Length)
            {
                int bestIdx = -1; string bestKw = null;
                foreach (var kw in keywords)
                {
                    int i = line.IndexOf(kw, pos, System.StringComparison.Ordinal);
                    if (i >= 0 && (bestIdx < 0 || i < bestIdx)) { bestIdx = i; bestKw = kw; }
                }
                if (bestIdx < 0) { yield return (line.Substring(pos), false); yield break; }
                if (bestIdx > pos) yield return (line.Substring(pos, bestIdx - pos), false);
                yield return (bestKw, true);
                pos = bestIdx + bestKw.Length;
            }
        }

        private static float EstimateWidth(string s)
        {
            float w = 0f;
            foreach (char c in s) w += c < 0x2E80 ? 0.56f : 1.04f;
            return w * FontSize;
        }

        // ============================== 接続（ドラッグ） ==============================

        public void BeginDragFrom(MemoKeywordChip chip)
        {
            _dragFrom = chip;
            _dragLine = MakeLine();
        }

        public void DragTo(Vector2 screenPos, Camera cam)
        {
            if (_dragFrom == null || _dragLine == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_board, screenPos, cam, out var local);
            LayoutLine(_dragLine, ChipCenterLocal(_dragFrom), local);
        }

        public void EndDrag(GameObject dropTarget, Vector2 screenPos, Camera cam)
        {
            if (_dragLine != null) { Destroy(_dragLine.gameObject); _dragLine = null; }
            if (_dragFrom == null) return;

            // ドロップ先のチップ。チップの上で離していなければ、ごく近く（SnapRadius内）の
            // チップにだけスナップする。それ以外はキャンセル（意図しないワードと繋がらない）
            var target = dropTarget != null ? dropTarget.GetComponentInParent<MemoKeywordChip>() : null;
            if (target == null)
                target = NearestOtherChip(screenPos, cam, _dragFrom, SnapRadius);

            if (target != null && CanLink(_dragFrom, target))
            {
                string key = PairKey(_dragFrom.NodeId, target.NodeId);
                if (!Connections.Add(key)) Connections.Remove(key);   // 同じ組をもう一度結ぶと解除
                RedrawConnections();
            }
            _dragFrom = null;
        }

        /// <summary>
        /// 2つのチップを結べるか。まったく同じ出現箇所（同一ノード）同士だけ不可。
        /// 別の資料に出た同じワード同士や、同じ括り（同一人物の情報）同士も結べる。
        /// MemoKeywordConfig.blockSameGroupLinks がONのときだけ括り内の接続を禁止する。
        /// </summary>
        private static bool CanLink(MemoKeywordChip a, MemoKeywordChip b)
        {
            if (a == null || b == null || a.NodeId == b.NodeId) return false;
            var cfg = MemoKeywordConfig.Instance;
            if (cfg != null && cfg.blockSameGroupLinks && a.EntityId == b.EntityId) return false;
            return true;
        }

        /// <summary>離した位置から maxDist 以内で最も近い、結線可能なチップを返す</summary>
        private MemoKeywordChip NearestOtherChip(Vector2 screenPos, Camera cam, MemoKeywordChip from, float maxDist)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_board, screenPos, cam, out var local);
            MemoKeywordChip best = null;
            float bestSq = maxDist * maxDist;
            foreach (var chip in _allChips)
            {
                if (chip == null || !CanLink(from, chip)) continue;
                float sq = (ChipCenterLocal(chip) - local).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = chip; }
            }
            return best;
        }

        private void RedrawConnections()
        {
            ClearChildren(_lineLayer);
            foreach (var pair in Connections)
            {
                var ab = pair.Split('\n');
                if (!_chips.TryGetValue(ab[0], out var ca) || !_chips.TryGetValue(ab[1], out var cb)) continue;
                var line = MakeLine();
                LayoutLine(line, ChipCenterLocal(ca), ChipCenterLocal(cb));
            }
        }

        private RectTransform MakeLine()
        {
            var go = new GameObject("Link");
            go.transform.SetParent(_lineLayer, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.95f, 0.2f, 0.2f, 0.85f);
            img.raycastTarget = false;
            return img.rectTransform;
        }

        private void LayoutLine(RectTransform line, Vector2 a, Vector2 b)
        {
            var d = b - a;
            line.anchorMin = line.anchorMax = new Vector2(0.5f, 0.5f);
            line.pivot = new Vector2(0f, 0.5f);
            line.anchoredPosition = a;
            line.sizeDelta = new Vector2(d.magnitude, 3f);
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        private Vector2 ChipCenterLocal(MemoKeywordChip chip)
        {
            var rt = (RectTransform)chip.transform;
            Vector3 world = rt.TransformPoint(rt.rect.center);
            Vector3 local = _board.InverseTransformPoint(world);
            return new Vector2(local.x, local.y);
        }
    }

    /// <summary>手帳キーワードのチップ（ホバーで赤枠、ドラッグで関係線）</summary>
    public class MemoKeywordChip : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public string Keyword { get; private set; }
        /// <summary>所属エンティティ（同一人物の氏名/番号/生年月日/特徴は同じIDになる）</summary>
        public string EntityId { get; private set; }
        /// <summary>出現箇所ごとの一意ID（エントリid:ワード:出現順）。線の端点になる</summary>
        public string NodeId { get; private set; }
        private MemoBoard _board;
        private GameObject _frame;

        public void Setup(MemoBoard board, string keyword, string entityId, string nodeId, GameObject frame)
        {
            _board = board; Keyword = keyword; EntityId = entityId; NodeId = nodeId; _frame = frame;
        }

        public void OnPointerEnter(PointerEventData e) => _frame.SetActive(true);
        public void OnPointerExit(PointerEventData e) => _frame.SetActive(false);
        public void OnBeginDrag(PointerEventData e) => _board.BeginDragFrom(this);
        public void OnDrag(PointerEventData e) => _board.DragTo(e.position, e.pressEventCamera);
        public void OnEndDrag(PointerEventData e) =>
            _board.EndDrag(e.pointerCurrentRaycast.gameObject, e.position, e.pressEventCamera);
    }
}
