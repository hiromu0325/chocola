using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 手帳の見開き描画＋証拠つなぎボード。
    /// ・手帳の本文から「キーワード」（氏名・社員番号・生年月日・部署・特徴・型番など）を
    ///   検出し、当たり判定つきのチップとして描画する（ホバーで赤い枠）
    /// ・チップからチップへドラッグすると赤い線で関係を結べる（再ドラッグで解除）
    /// ・右クリックでページ送り（最後まで行くと先頭へループ）
    /// </summary>
    public class MemoBoard : MonoBehaviour
    {
        private const int LinesPerPage = 17;
        private const int WrapChars = 22;
        private const int FontSize = 25;
        private const float LineH = 33f;
        private const float PageW = 560f;

        private struct MemoLine { public string text; public bool title; }

        private Font _font;
        private RectTransform _board;        // 手帳パネル全体（線の座標基準）
        private RectTransform _leftPage, _rightPage, _lineLayer;
        private Text _pageLabel;

        private readonly List<List<MemoLine>> _pages = new List<List<MemoLine>>();
        private int _spread;
        // エンティティID → 代表チップ（同一人物の氏名/番号/生年月日/特徴は同じエンティティ）
        private readonly Dictionary<string, MemoKeywordChip> _chips = new Dictionary<string, MemoKeywordChip>();
        private readonly List<MemoKeywordChip> _allChips = new List<MemoKeywordChip>();

        // 結ばれた関係（セッション中保持）
        private static readonly HashSet<string> Connections = new HashSet<string>();
        private static string PairKey(string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? a + "\n" + b : b + "\n" + a;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Connections.Clear();

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
            var lines = new List<MemoLine>();
            if (Notebook.Count == 0)
            {
                lines.Add(new MemoLine { text = "まだ何も書かれていない。" });
                lines.Add(new MemoLine { text = "資料を調べたり手がかりを見つけると、" });
                lines.Add(new MemoLine { text = "ここに書き留められる。" });
            }
            else
            {
                foreach (var e in Notebook.Entries)
                {
                    lines.Add(new MemoLine { text = $"【{e.title}】", title = true });
                    foreach (var raw in e.body.Split('\n')) Wrap(lines, raw);
                    lines.Add(new MemoLine { text = "" });
                }
            }
            for (int i = 0; i < lines.Count; i += LinesPerPage)
            {
                int n = Mathf.Min(LinesPerPage, lines.Count - i);
                _pages.Add(lines.GetRange(i, n));
            }
            if (_pages.Count == 0) _pages.Add(new List<MemoLine>());
        }

        private static void Wrap(List<MemoLine> acc, string line)
        {
            if (line.Length <= WrapChars) { acc.Add(new MemoLine { text = line }); return; }
            for (int p = 0; p < line.Length; p += WrapChars)
                acc.Add(new MemoLine { text = line.Substring(p, Mathf.Min(WrapChars, line.Length - p)) });
        }

        // ============================== 見開き描画 ==============================

        private void RenderSpread()
        {
            ClearChildren(_leftPage); ClearChildren(_rightPage); ClearChildren(_lineLayer);
            _chips.Clear(); _allChips.Clear();
            _dragFrom = null; _dragLine = null;

            int li = _spread * 2, ri = li + 1;
            if (li < _pages.Count) RenderPage(_leftPage, _pages[li]);
            if (ri < _pages.Count) RenderPage(_rightPage, _pages[ri]);

            int shownTo = Mathf.Min(ri + 1, _pages.Count);
            _pageLabel.text = $"— {li + 1}〜{shownTo} / {_pages.Count} ページ —　右クリック:ページ送り(ループ)　" +
                              "ドラッグで結ぶ(離すと最寄りに接続／同じ組をもう一度で解除)　[Tab]閉じる";
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
                        MakeChip(page, text, new Vector2(x, y), w);
                    else
                        MakeText(page, text, new Vector2(x, y), w,
                            line.title ? new Color(1f, 0.84f, 0.6f) : Color.white);
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

        private void MakeChip(RectTransform parent, string keyword, Vector2 pos, float w)
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

            var chip = go.AddComponent<MemoKeywordChip>();
            chip.Setup(this, keyword, EntityOf(keyword), frame);
            if (!_chips.ContainsKey(chip.EntityId)) _chips[chip.EntityId] = chip;   // エンティティの初出を代表にする
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
        /// キーワード表を構築。同一人物に属する情報（氏名・社員番号・生年月日・容姿の特徴）は
        /// すべて同じエンティティとして扱う。型番と復旧コードも同一装置として同一視。
        /// 部署は複数人にまたがるため独立エンティティ。
        /// </summary>
        private static List<string> CollectKeywords()
        {
            if (_keywordCache != null) return _keywordCache;
            _entityMap = new Dictionary<string, string>();

            void Map(string kw, string entity)
            {
                if (!string.IsNullOrEmpty(kw) && !_entityMap.ContainsKey(kw)) _entityMap[kw] = entity;
            }

            foreach (var e in PuzzleState.Employees)
            {
                string ent = "emp:" + e.number;
                Map(e.name, ent);
                Map(e.number, ent);
                Map(e.birthdate, ent);
                Map(e.feature, ent);
                Map(e.department, "dept:" + e.department);
            }
            foreach (var m in PuzzleState.Models)
            {
                string ent = "mdl:" + m.model;
                Map(m.model, ent);
                Map(m.code, ent);
            }
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

            // ドロップ先のチップ。無ければ／同一エンティティなら、離した位置に最も近い
            // 「別エンティティの」チップへ必ず接続する（＝繋がらないは発生しない）
            var target = dropTarget != null ? dropTarget.GetComponentInParent<MemoKeywordChip>() : null;
            if (target == null || target.EntityId == _dragFrom.EntityId)
                target = NearestOtherChip(screenPos, cam, _dragFrom.EntityId);

            if (target != null)
            {
                string key = PairKey(_dragFrom.EntityId, target.EntityId);
                if (!Connections.Add(key)) Connections.Remove(key);   // 同じ組をもう一度結ぶと解除
                RedrawConnections();
            }
            _dragFrom = null;
        }

        /// <summary>離した位置に最も近い、指定エンティティ以外のチップを返す</summary>
        private MemoKeywordChip NearestOtherChip(Vector2 screenPos, Camera cam, string excludeEntity)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_board, screenPos, cam, out var local);
            MemoKeywordChip best = null;
            float bestSq = float.MaxValue;
            foreach (var chip in _allChips)
            {
                if (chip == null || chip.EntityId == excludeEntity) continue;
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
        private MemoBoard _board;
        private GameObject _frame;

        public void Setup(MemoBoard board, string keyword, string entityId, GameObject frame)
        {
            _board = board; Keyword = keyword; EntityId = entityId; _frame = frame;
        }

        public void OnPointerEnter(PointerEventData e) => _frame.SetActive(true);
        public void OnPointerExit(PointerEventData e) => _frame.SetActive(false);
        public void OnBeginDrag(PointerEventData e) => _board.BeginDragFrom(this);
        public void OnDrag(PointerEventData e) => _board.DragTo(e.position, e.pressEventCamera);
        public void OnEndDrag(PointerEventData e) =>
            _board.EndDrag(e.pointerCurrentRaycast.gameObject, e.position, e.pressEventCamera);
    }
}
