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
    /// ループ回廊のギミック専用UI（実行時にUGUI生成）。
    /// ・Circuit  : 記憶回路（BioShock風のタイル回転＋"欠損"の傾向記号を選ぶ）
    /// ・Checklist: 表から矛盾行にチェックを入れて確定（入退室ログの突き合わせ）
    /// 手帳（Tab）は開いたまま重ねて参照できる（HUD側は別Canvas）。
    /// </summary>
    public class LoopPuzzleUI : MonoBehaviour
    {
        public static LoopPuzzleUI Instance { get; private set; }

        public enum Pipe { None, Straight, Corner, Tee, Cross }

        /// <summary>記憶回路の盤面定義（ビルダー／ロックが組み立てる）</summary>
        [Serializable]
        public class CircuitLevel
        {
            public int Size = 5;
            public int InRow = 2, OutRow = 2;
            public Pipe[] Pipes;        // Size*Size（行優先）
            public int[] Symbols;       // 0..2 の傾向記号。欠損セルは -1（プレイヤーが選ぶ）
            public int[] Answers;       // 欠損セルの正解記号（同じ index）。それ以外は -1
            public int[] Rotations;     // 初期回転（0..3）
            public string[] SymbolNames = { "感情", "言語", "行動" };
        }

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        private static readonly string[] SymbolGlyph = { "○", "△", "□" };
        private static readonly Color[] SymbolColor =
        {
            new Color(1f, 0.55f, 0.5f), new Color(0.55f, 0.8f, 1f), new Color(0.6f, 1f, 0.6f),
        };

        private Font _font;
        private GameObject _panel;
        private Text _title, _body, _footer, _message;
        private RectTransform _content;
        private Button _submit, _close;
        private Action<bool> _onDone;

        // ---- Circuit ----
        private CircuitLevel _level;
        private int[] _rot, _sym;
        private Image[] _cellBg;
        private Text[] _cellPipe, _cellSym;
        private bool _wasHeld;

        // ---- Checklist ----
        private bool[] _checked;
        private Image[] _rowBg;
        private int[] _correctSet;
        private int _needCount;

        public bool IsOpen => _panel != null && _panel.activeSelf;

        private void Awake()
        {
            Instance = this;
            _font = FontProvider.Get();
            Build();
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ============================== 公開API ==============================

        /// <summary>記憶回路。onDone(true)=光が通った / false=閉じた</summary>
        public void ShowCircuit(string title, string body, CircuitLevel level, Action<bool> onDone, Action<int> onWrongCell)
        {
            _onDone = onDone;
            _onWrongCell = onWrongCell;
            _level = level;
            _title.text = title;
            _body.text = body;
            _footer.text = "左クリック: 回転　　右クリック: 欠損（?）の傾向を切替　　[決定] で光を流す　　[Esc] 閉じる　[Tab] 手帳";
            _message.text = "";
            ClearContent();
            BuildCircuit();
            Open();
        }
        private Action<int> _onWrongCell;

        /// <summary>チェックリスト。correct=正解の行インデックス集合。onDone(true)=一致</summary>
        public void ShowChecklist(string title, string body, string[] rows, int[] correct, Action<bool> onDone, Action onWrong)
        {
            _onDone = onDone;
            _onWrongCell = _ => onWrong?.Invoke();
            _level = null;
            _title.text = title;
            _body.text = body;
            _footer.text = $"矛盾している行をクリックでチェック（{correct.Length}行）　[決定] で確定　[Esc] 閉じる　[Tab] 手帳";
            _message.text = "";
            ClearContent();
            BuildChecklist(rows, correct);
            Open();
        }

        // ============================== 開閉 ==============================

        private void Open()
        {
            _panel.SetActive(true);
            GameManager.Instance?.SetBusy(true);
            var es = EventSystem.current;
            if (es != null) es.SetSelectedGameObject(null);
        }

        private void Close(bool solved)
        {
            _panel.SetActive(false);
            GameManager.Instance?.SetBusy(false);
            var cb = _onDone; _onDone = null;
            cb?.Invoke(solved);
        }

        private void Update()
        {
            if (!IsOpen) return;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close(false);
#else
            if (Input.GetKeyDown(KeyCode.Escape)) Close(false);
#endif
        }

        // ============================== Circuit ==============================

        private const float CellSize = 84f, CellGap = 6f;

        private void BuildCircuit()
        {
            int n = _level.Size;
            _rot = (int[])_level.Rotations.Clone();
            _sym = (int[])_level.Symbols.Clone();
            _cellBg = new Image[n * n];
            _cellPipe = new Text[n * n];
            _cellSym = new Text[n * n];
            float total = n * CellSize + (n - 1) * CellGap;
            float x0 = -total * 0.5f + CellSize * 0.5f;
            float y0 = total * 0.5f - CellSize * 0.5f;

            // 入口／出口の目印
            MakeLabel(_content, "入力\n提供体", new Vector2(x0 - CellSize, y0 - _level.InRow * (CellSize + CellGap)), 22, new Color(1f, 0.85f, 0.6f));
            MakeLabel(_content, "出力\n対象者", new Vector2(x0 + total + CellSize * 0.35f, y0 - _level.OutRow * (CellSize + CellGap)), 22, new Color(1f, 0.85f, 0.6f));

            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    int i = r * n + c;
                    var pos = new Vector2(x0 + c * (CellSize + CellGap), y0 - r * (CellSize + CellGap));
                    var go = new GameObject($"Cell_{r}_{c}");
                    go.transform.SetParent(_content, false);
                    var img = go.AddComponent<Image>();
                    img.color = _level.Pipes[i] == Pipe.None ? new Color(0.1f, 0.1f, 0.12f, 0.9f) : new Color(0.16f, 0.17f, 0.22f, 1f);
                    SetRect(img.rectTransform, Center, Center, pos, new Vector2(CellSize, CellSize));
                    _cellBg[i] = img;

                    var pipe = MakeLabel(go.transform, "", Vector2.zero, 54, new Color(0.85f, 0.9f, 1f));
                    SetRect(pipe.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    _cellPipe[i] = pipe;

                    var sym = MakeLabel(go.transform, "", new Vector2(CellSize * 0.32f, CellSize * 0.32f), 22, Color.white);
                    sym.fontStyle = FontStyle.Bold;
                    _cellSym[i] = sym;

                    int idx = i;
                    var click = go.AddComponent<CellClick>();
                    click.OnLeft = () => RotateCell(idx);
                    click.OnRight = () => CycleSymbol(idx);
                    RefreshCell(i);
                }

            // 傾向記号の凡例
            string legend = "傾向記号: ";
            for (int s = 0; s < 3; s++)
                legend += $"<color=#{ColorUtility.ToHtmlStringRGB(SymbolColor[s])}>{SymbolGlyph[s]} {_level.SymbolNames[s]}</color>　";
            MakeLabel(_content, legend + "　<color=#FFE060>?</color> 欠損（右クリックで選ぶ）", new Vector2(0f, -total * 0.5f - 40f), 22, Color.white);
        }

        private void RotateCell(int i)
        {
            if (_level.Pipes[i] == Pipe.None) return;
            _rot[i] = (_rot[i] + 1) % 4;
            _message.text = "";
            ProceduralAudio.PlayAt(ProceduralAudio.Click(), Vector3.zero, 0.4f, false);
            RefreshCell(i);
        }

        private void CycleSymbol(int i)
        {
            if (_level.Answers[i] < 0) return;   // 欠損セルだけ選べる
            _sym[i] = (_sym[i] + 1) % 3;
            _message.text = "";
            ProceduralAudio.PlayAt(ProceduralAudio.DialTick(), Vector3.zero, 0.5f, false);
            RefreshCell(i);
        }

        private void RefreshCell(int i)
        {
            var p = _level.Pipes[i];
            _cellPipe[i].text = PipeGlyph(p);
            _cellPipe[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f * _rot[i]);
            bool gap = _level.Answers[i] >= 0;
            if (p == Pipe.None) { _cellSym[i].text = ""; return; }
            if (gap && _sym[i] < 0) { _cellSym[i].text = "?"; _cellSym[i].color = new Color(1f, 0.88f, 0.4f); }
            else
            {
                _cellSym[i].text = SymbolGlyph[_sym[i]];
                _cellSym[i].color = SymbolColor[_sym[i]];
            }
            if (gap) _cellBg[i].color = new Color(0.3f, 0.26f, 0.18f, 1f);   // 欠損＝琥珀の枠色
        }

        private static string PipeGlyph(Pipe p)
        {
            switch (p)
            {
                case Pipe.Straight: return "━";
                case Pipe.Corner: return "┗";
                case Pipe.Tee: return "┻";
                case Pipe.Cross: return "╋";
                default: return "";
            }
        }

        /// <summary>方向: 0=上 1=右 2=下 3=左。基準形（回転0）の開口</summary>
        private static bool[] BaseOpenings(Pipe p)
        {
            switch (p)
            {
                case Pipe.Straight: return new[] { false, true, false, true };
                case Pipe.Corner: return new[] { true, true, false, false };     // ┗ = 上と右
                case Pipe.Tee: return new[] { true, true, false, true };         // ┻ = 上・右・左
                case Pipe.Cross: return new[] { true, true, true, true };
                default: return new[] { false, false, false, false };
            }
        }

        private bool Open(int i, int dir)
        {
            var b = BaseOpenings(_level.Pipes[i]);
            // 時計回りに rot 回転 → 基準の方向 (dir - rot)
            return b[((dir - _rot[i]) % 4 + 4) % 4];
        }

        private void SubmitCircuit()
        {
            int n = _level.Size;
            var visited = new bool[n * n];
            var stack = new Stack<(int cell, int from)>();
            int start = _level.InRow * n;
            if (!Open(start, 3)) { Fail("入力に繋がっていない。", start); return; }
            stack.Push((start, 3));
            var lit = new List<int>();
            while (stack.Count > 0)
            {
                var (cell, from) = stack.Pop();
                if (visited[cell]) continue;
                visited[cell] = true;
                lit.Add(cell);
                // 欠損セルは傾向が合っていないと光が濁って止まる
                if (_level.Answers[cell] >= 0 && _sym[cell] != _level.Answers[cell])
                {
                    Highlight(lit, false);
                    Fail(_sym[cell] < 0 ? "欠損に傾向が入っていない。光が止まった。" : "補完した記憶が周囲と噛み合わず、光が濁って止まった。", cell);
                    return;
                }
                int r = cell / n, c = cell % n;
                if (c == n - 1 && r == _level.OutRow && Open(cell, 1))
                {
                    Highlight(lit, true);
                    _message.text = "<color=#A0FFB0>光が通った。記憶が補完された。</color>";
                    ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), Vector3.zero, 0.8f, false);
                    _submit.interactable = false;
                    StartCoroutine(CloseAfter(1.2f, true));
                    return;
                }
                for (int d = 0; d < 4; d++)
                {
                    if (d == from || !Open(cell, d)) continue;
                    int nr = r + (d == 2 ? 1 : d == 0 ? -1 : 0);
                    int nc = c + (d == 1 ? 1 : d == 3 ? -1 : 0);
                    if (nr < 0 || nc < 0 || nr >= n || nc >= n) continue;
                    int ni = nr * n + nc;
                    int back = (d + 2) % 4;
                    if (Open(ni, back)) stack.Push((ni, back));
                }
            }
            Highlight(lit, false);
            Fail("経路が出力まで繋がっていない。", -1);
        }

        private void Highlight(List<int> cells, bool ok)
        {
            foreach (var i in cells)
                _cellBg[i].color = ok ? new Color(0.2f, 0.45f, 0.3f, 1f) : new Color(0.35f, 0.25f, 0.2f, 1f);
        }

        private void Fail(string msg, int cell)
        {
            _message.text = "<color=#FFB0A0>" + msg + "</color>";
            ProceduralAudio.PlayAt(ProceduralAudio.DialBuzz(), Vector3.zero, 0.5f, false);
            if (cell >= 0) _cellBg[cell].color = new Color(0.6f, 0.15f, 0.15f, 1f);
            if (cell >= 0 && _level.Answers[cell] >= 0) _onWrongCell?.Invoke(cell);
        }

        private System.Collections.IEnumerator CloseAfter(float sec, bool solved)
        {
            yield return new WaitForSecondsRealtime(sec);
            _submit.interactable = true;
            Close(solved);
        }

        // ============================== Checklist ==============================

        private void BuildChecklist(string[] rows, int[] correct)
        {
            _correctSet = correct;
            _needCount = correct.Length;
            _checked = new bool[rows.Length];
            _rowBg = new Image[rows.Length];
            float rowH = 44f;
            float y0 = (rows.Length - 1) * 0.5f * rowH;
            for (int i = 0; i < rows.Length; i++)
            {
                var go = new GameObject($"Row_{i}");
                go.transform.SetParent(_content, false);
                var img = go.AddComponent<Image>();
                img.color = new Color(0.16f, 0.17f, 0.22f, 1f);
                SetRect(img.rectTransform, Center, Center, new Vector2(0f, y0 - i * rowH), new Vector2(960f, rowH - 4f));
                _rowBg[i] = img;
                var t = MakeLabel(go.transform, "　" + rows[i], Vector2.zero, 24, Color.white);
                t.alignment = TextAnchor.MiddleLeft;
                SetRect(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                int idx = i;
                var click = go.AddComponent<CellClick>();
                click.OnLeft = () => ToggleRow(idx);
                click.OnRight = () => ToggleRow(idx);
            }
        }

        private void ToggleRow(int i)
        {
            _checked[i] = !_checked[i];
            _rowBg[i].color = _checked[i] ? new Color(0.55f, 0.35f, 0.2f, 1f) : new Color(0.16f, 0.17f, 0.22f, 1f);
            _message.text = "";
            ProceduralAudio.PlayAt(ProceduralAudio.Click(), Vector3.zero, 0.4f, false);
        }

        private void SubmitChecklist()
        {
            int count = 0; bool allCorrect = true;
            for (int i = 0; i < _checked.Length; i++)
            {
                if (!_checked[i]) continue;
                count++;
                if (Array.IndexOf(_correctSet, i) < 0) allCorrect = false;
            }
            if (count == _needCount && allCorrect)
            {
                _message.text = "<color=#A0FFB0>照合完了。矛盾が確定した。</color>";
                ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), Vector3.zero, 0.8f, false);
                _submit.interactable = false;
                StartCoroutine(CloseAfter(1.0f, true));
            }
            else
            {
                _message.text = count != _needCount
                    ? $"<color=#FFB0A0>チェックは{_needCount}行のはずだ。</color>"
                    : "<color=#FFB0A0>その組み合わせでは、矛盾にならない。</color>";
                ProceduralAudio.PlayAt(ProceduralAudio.DialBuzz(), Vector3.zero, 0.5f, false);
                _onWrongCell?.Invoke(-1);
            }
        }

        // ============================== UI構築 ==============================

        private void Build()
        {
            var canvasGo = new GameObject("LoopPuzzleCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 52;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("Panel");
            _panel.transform.SetParent(canvasGo.transform, false);
            var bg = _panel.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.07f, 0.97f);
            SetRect(bg.rectTransform, Center, Center, Vector2.zero, new Vector2(1240, 860));

            _title = MakeLabel(_panel.transform, "", new Vector2(0, 385), 36, new Color(1f, 0.85f, 0.6f));
            SetRect(_title.rectTransform, Center, Center, new Vector2(0, 385), new Vector2(1180, 50));
            _body = MakeLabel(_panel.transform, "", new Vector2(0, 325), 24, new Color(0.9f, 0.9f, 0.92f));
            SetRect(_body.rectTransform, Center, Center, new Vector2(0, 325), new Vector2(1180, 70));

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(_panel.transform, false);
            _content = contentGo.AddComponent<RectTransform>();
            SetRect(_content, Center, Center, new Vector2(0, -20), new Vector2(1180, 560));

            _message = MakeLabel(_panel.transform, "", new Vector2(0, -330), 26, Color.white);
            SetRect(_message.rectTransform, Center, Center, new Vector2(0, -330), new Vector2(1180, 40));

            _submit = MakeButton(_panel.transform, "決定", new Vector2(-140, -385), new Vector2(240, 56), () =>
            {
                if (_level != null) SubmitCircuit(); else SubmitChecklist();
            });
            _close = MakeButton(_panel.transform, "閉じる", new Vector2(140, -385), new Vector2(240, 56), () => Close(false));

            _footer = MakeLabel(_panel.transform, "", new Vector2(0, -415), 20, new Color(0.7f, 0.7f, 0.75f));
            SetRect(_footer.rectTransform, Center, Center, new Vector2(0, -415), new Vector2(1180, 30));

            _panel.SetActive(false);
        }

        private void ClearContent()
        {
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _submit.interactable = true;
        }

        private Text MakeLabel(Transform parent, string text, Vector2 pos, int size, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font; t.fontSize = size; t.text = text; t.color = color;
            t.alignment = TextAnchor.MiddleCenter; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            SetRect(t.rectTransform, Center, Center, pos, new Vector2(200, 60));
            return t;
        }

        private Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.17f, 0.22f, 1f);
            SetRect(img.rectTransform, Center, Center, pos, size);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.45f, 0.55f, 0.7f, 1f);
            colors.pressedColor = new Color(0.25f, 0.35f, 0.5f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick());
            var t = MakeLabel(go.transform, label, Vector2.zero, 28, Color.white);
            SetRect(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return btn;
        }

        private static void SetRect(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.anchoredPosition = pos;
            if (size != Vector2.zero) rt.sizeDelta = size;
            else { rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        }

        /// <summary>セル／行のクリック（左右）を受ける</summary>
        private class CellClick : MonoBehaviour, IPointerClickHandler
        {
            public Action OnLeft, OnRight;
            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Right) OnRight?.Invoke();
                else OnLeft?.Invoke();
            }
        }
    }
}
