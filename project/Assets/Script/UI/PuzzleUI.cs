using System;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// 謎解き用UI（実行時にUGUI生成）。資料の閲覧・氏名選択・コード入力を提供。
    /// パネルを開いている間はプレイヤー操作を停止し、カーソルを表示。
    /// コード入力は画面のテンキー（マウスクリック）とキーボードの両方に対応。
    /// </summary>
    public class PuzzleUI : MonoBehaviour
    {
        public static PuzzleUI Instance { get; private set; }

        private enum Mode { None, Document, Selection, Keypad }
        private Mode _mode = Mode.None;

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        private Font _font;
        private GameObject _panel;
        private Text _titleText, _bodyText, _footerText, _inputDisplay;
        private GameObject _keypadPad;     // テンキー（Keypadモードのみ表示）
        private GameObject _optionsPad;    // 選択肢ボタン（Selectionモードのみ表示）
        private GameObject _closeButton;    // 閉じる（Document/Selection）

        private string[] _options;
        private Action<int> _onSelect;
        private Action<string> _onSubmit;
        private int _keypadLength;
        private string _keypadInput = "";

        // 開いたEがまだ押されている間は閉じない／閉じたEがまだ押されている間は再オープンさせない
        private bool _waitReleaseToClose;
        private bool _blockReopen;

        public bool IsOpen => _mode != Mode.None;
        /// <summary>閉じた直後（Eが離されるまで）の再オープン抑止フラグ</summary>
        public bool BlockReopen => _blockReopen;

        private void Awake()
        {
            Instance = this;
            _font = FontProvider.Get();
            BuildUI();
        }

        private void OnEnable()
        {
            GameEvents.OnWhiteout += ForceCloseDeath;
            GameEvents.OnGameOver += ForceClose;
            GameEvents.OnGameClear += ForceClose;
            GameEvents.OnVisitStart += ForceCloseVisit;
        }
        private void OnDisable()
        {
            GameEvents.OnWhiteout -= ForceCloseDeath;
            GameEvents.OnGameOver -= ForceClose;
            GameEvents.OnGameClear -= ForceClose;
            GameEvents.OnVisitStart -= ForceCloseVisit;
            if (Instance == this) Instance = null;
        }

        private void ForceCloseDeath(float _) => ForceClose();
        private void ForceCloseVisit(SearcherType _) => ForceClose();
        private void ForceClose() { if (IsOpen) Close(); }

        // ============================== 公開API ==============================

        public void ShowDocument(string title, string body)
        {
            _mode = Mode.Document;
            _titleText.text = title;
            _bodyText.text = body;
            _footerText.text = "[E] / [Esc] / 「閉じる」で閉じる";
            ApplyModeLayout();
            Open();
        }

        public void ShowSelection(string title, string body, string[] options, Action<int> onSelect)
        {
            _mode = Mode.Selection;
            _options = options; _onSelect = onSelect;
            _titleText.text = title;
            _bodyText.text = body;
            _footerText.text = "ボタンをクリック、または数字キーで選択 / [Esc] 中止";
            BuildSelectionButtons(options);
            ApplyModeLayout();
            Open();
        }

        private void PickOption(int idx)
        {
            var cb = _onSelect;
            Close();
            cb?.Invoke(idx);
        }

        private void BuildSelectionButtons(string[] options)
        {
            for (int i = _optionsPad.transform.childCount - 1; i >= 0; i--)
                Destroy(_optionsPad.transform.GetChild(i).gameObject);

            int n = options != null ? options.Length : 0;
            float startY = (n - 1) * 0.5f * 84f;   // 中央寄せ
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                float y = startY - i * 84f;
                MakeButton(_optionsPad.transform, $"{i + 1}. {options[i]}", new Vector2(0, y),
                    new Vector2(640, 68), 28, () => PickOption(idx));
            }
        }

        public void ShowKeypad(string title, string body, int length, Action<string> onSubmit)
        {
            _mode = Mode.Keypad;
            _keypadLength = length; _onSubmit = onSubmit; _keypadInput = "";
            _titleText.text = title;
            _bodyText.text = body;
            _footerText.text = "テンキー（クリック）または数字キーで入力　[Esc]中止";
            ApplyModeLayout();
            RefreshKeypadDisplay();
            Open();
        }

        /// <summary>モードに応じてパネル内の表示を切り替える</summary>
        private void ApplyModeLayout()
        {
            bool keypad = _mode == Mode.Keypad;
            bool selection = _mode == Mode.Selection;
            _keypadPad.SetActive(keypad);
            _optionsPad.SetActive(selection);
            _inputDisplay.gameObject.SetActive(keypad);
            _closeButton.SetActive(_mode == Mode.Document || selection);

            // 本文の領域：Keypad/Selectionは上部に小さく、その他は大きく
            if (keypad || selection)
                SetRect(_bodyText.rectTransform, Center, Center, new Vector2(0, 248), new Vector2(1000, 150));
            else
                SetRect(_bodyText.rectTransform, Center, Center, new Vector2(0, 40), new Vector2(1000, 520));

            // ゲームパッド操作用：先頭ボタンを選択
            if (keypad) SelectFirst(_keypadPad);
            else if (selection) SelectFirst(_optionsPad);
            else SelectFirst(_closeButton);
        }

        // ============================== 開閉 ==============================

        private void Open()
        {
            _panel.SetActive(true);
            _waitReleaseToClose = true;   // 開いたときのE押下では閉じない
            GameManager.Instance?.SetBusy(true);
        }

        private void Close()
        {
            _mode = Mode.None;
            _panel.SetActive(false);
            _options = null; _onSelect = null; _onSubmit = null; _keypadInput = "";
            _blockReopen = true;          // 閉じたE押下では再オープンさせない
            GameManager.Instance?.SetBusy(false);
        }

        private void OnCloseButton()
        {
            if (_mode == Mode.Selection) { var c = _onSelect; Close(); c?.Invoke(-1); }
            else Close();
        }

        // ============================== キーパッド操作（ボタン＆キー共通）==============================

        private void KeypadAppend(int d)
        {
            if (_mode != Mode.Keypad || _keypadInput.Length >= _keypadLength) return;
            _keypadInput += d.ToString();
            RefreshKeypadDisplay();
        }

        private void KeypadBackspace()
        {
            if (_mode != Mode.Keypad || _keypadInput.Length == 0) return;
            _keypadInput = _keypadInput.Substring(0, _keypadInput.Length - 1);
            RefreshKeypadDisplay();
        }

        private void KeypadClear()
        {
            if (_mode != Mode.Keypad) return;
            _keypadInput = "";
            RefreshKeypadDisplay();
        }

        private void KeypadSubmit()
        {
            if (_mode != Mode.Keypad) return;
            var cb = _onSubmit; var val = _keypadInput;
            Close();
            cb?.Invoke(val);
        }

        private void RefreshKeypadDisplay()
        {
            string shown = _keypadInput.PadRight(_keypadLength, '＿');
            _inputDisplay.text = $"<color=#FFE060>{string.Join("  ", shown.ToCharArray())}</color>";
        }

        // ============================== 入力（キーボード）==============================

        private void Update()
        {
            // Eが離されたらガードを解除（押しっぱなしによる即閉じ／即再オープンを防ぐ）
            bool eHeld = IsInteractHeld();
            if (_waitReleaseToClose && !eHeld) _waitReleaseToClose = false;
            if (_blockReopen && !eHeld) _blockReopen = false;

            if (_mode == Mode.None) return;

            if (WasEsc())
            {
                var cancel = _onSelect;
                bool wasSelection = _mode == Mode.Selection;
                Close();
                if (wasSelection) cancel?.Invoke(-1);
                return;
            }

            switch (_mode)
            {
                case Mode.Document:
                    if (!_waitReleaseToClose && WasInteract()) Close();
                    break;

                case Mode.Selection:
                {
                    int d = PressedDigit();
                    if (d >= 1 && _options != null && d <= _options.Length)
                    {
                        var cb = _onSelect; int idx = d - 1;
                        Close();
                        cb?.Invoke(idx);
                    }
                    break;
                }

                case Mode.Keypad:
                {
                    int d = PressedDigit();
                    if (d >= 0) KeypadAppend(d);
                    if (WasBackspace()) KeypadBackspace();
                    if (WasEnter()) KeypadSubmit();
                    break;
                }
            }
        }

        private int PressedDigit()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; if (kb == null) return -1;
            if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) return 0;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) return 1;
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) return 2;
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) return 3;
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) return 4;
            if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) return 5;
            if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) return 6;
            if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) return 7;
            if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame) return 8;
            if (kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame) return 9;
            return -1;
#else
            for (int i = 0; i <= 9; i++)
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i)) return i;
            return -1;
#endif
        }

        private bool WasEsc()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            var gp = Gamepad.current;
            return gp != null && gp.buttonEast.wasPressedThisFrame;   // B / ○ で戻る
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        /// <summary>ゲームパッド操作用：先頭ボタンを選択状態にする</summary>
        private void SelectFirst(GameObject root)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return;
            Button btn = root != null ? root.GetComponentInChildren<Button>(false) : null;
            es.SetSelectedGameObject(btn != null ? btn.gameObject : null);
        }
        private bool WasInteract()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; return kb != null && kb.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }
        private bool IsInteractHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.isPressed) return true;
            var gp = Gamepad.current;
            return gp != null && (gp.buttonWest.isPressed || gp.rightTrigger.ReadValue() > 0.5f);
#else
            return Input.GetKey(KeyCode.E);
#endif
        }
        private bool WasEnter()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
        }
        private bool WasBackspace()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; return kb != null && kb.backspaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Backspace);
#endif
        }

        // ============================== UI構築 ==============================

        private void BuildUI()
        {
            var canvasGo = new GameObject("PuzzleCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;   // HUDより前、メニューより後ろ
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            _panel = new GameObject("PuzzlePanel");
            _panel.transform.SetParent(canvasGo.transform, false);
            var bg = _panel.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.05f, 0.07f, 0.96f);
            SetRect(bg.rectTransform, Center, Center, Vector2.zero, new Vector2(1100, 760));

            _titleText = MakeText(_panel.transform, "Title", 40, TextAnchor.UpperCenter);
            SetRect(_titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(1040, 70));
            _titleText.color = new Color(1f, 0.85f, 0.6f);

            _bodyText = MakeText(_panel.transform, "Body", 30, TextAnchor.UpperLeft);
            SetRect(_bodyText.rectTransform, Center, Center, new Vector2(0, 40), new Vector2(1000, 520));

            // 入力表示（Keypad）
            _inputDisplay = MakeText(_panel.transform, "InputDisplay", 60, TextAnchor.MiddleCenter);
            SetRect(_inputDisplay.rectTransform, Center, Center, new Vector2(0, 150), new Vector2(760, 96));
            var inputBg = _inputDisplay.gameObject.AddComponent<Outline>();
            inputBg.effectColor = new Color(0, 0, 0, 0.8f); inputBg.effectDistance = new Vector2(2, -2);

            _footerText = MakeText(_panel.transform, "Footer", 24, TextAnchor.LowerCenter);
            SetRect(_footerText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(1040, 60));
            _footerText.color = new Color(0.7f, 0.7f, 0.75f);

            BuildKeypadPad();

            _optionsPad = new GameObject("OptionsPad");
            _optionsPad.transform.SetParent(_panel.transform, false);
            var optRt = _optionsPad.AddComponent<RectTransform>();
            SetRect(optRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            _closeButton = MakeButton(_panel.transform, "閉じる", new Vector2(0, -320), new Vector2(240, 60), 30, OnCloseButton).gameObject;

            _panel.SetActive(false);
            _keypadPad.SetActive(false);
            _optionsPad.SetActive(false);
            _inputDisplay.gameObject.SetActive(false);
            _closeButton.SetActive(false);
        }

        private void BuildKeypadPad()
        {
            _keypadPad = new GameObject("KeypadPad");
            _keypadPad.transform.SetParent(_panel.transform, false);
            var rt = _keypadPad.AddComponent<RectTransform>();
            SetRect(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); // パネル全面（子は中心基準で配置）

            var size = new Vector2(86, 86);
            // 1〜9（3×3）
            for (int i = 0; i < 9; i++)
            {
                int row = i / 3, col = i % 3;
                int digit = i + 1;
                float x = (col - 1) * 112f;
                float y = 40f - row * 96f;
                MakeButton(_keypadPad.transform, digit.ToString(), new Vector2(x, y), size, 40, () => KeypadAppend(digit));
            }
            // C / 0 / ⌫
            MakeButton(_keypadPad.transform, "C", new Vector2(-112f, -248f), size, 34, KeypadClear);
            MakeButton(_keypadPad.transform, "0", new Vector2(0f, -248f), size, 40, () => KeypadAppend(0));
            MakeButton(_keypadPad.transform, "⌫", new Vector2(112f, -248f), size, 38, KeypadBackspace);
            // 決定
            MakeButton(_keypadPad.transform, "決定", new Vector2(0f, -330f), new Vector2(312, 60), 32, KeypadSubmit);
        }

        private Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, int fontSize, Action onClick)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.17f, 0.22f, 1f);
            SetRect(img.rectTransform, Center, Center, pos, size);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.45f, 0.55f, 0.7f, 1f);
            colors.pressedColor = new Color(0.25f, 0.35f, 0.5f, 1f);
            colors.selectedColor = colors.highlightedColor;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var t = MakeText(go.transform, "L", fontSize, TextAnchor.MiddleCenter);
            SetRect(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            t.text = label;
            return btn;
        }

        private Text MakeText(Transform parent, string name, int size, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font; text.fontSize = size; text.alignment = anchor;
            text.color = Color.white; text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void SetRect(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.anchoredPosition = pos;
            if (size != Vector2.zero) rt.sizeDelta = size;
            else { rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }
    }
}
