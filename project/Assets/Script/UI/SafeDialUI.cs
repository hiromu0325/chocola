using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// 壁金庫のダイヤル操作UI（実行時にUGUI生成）。
    /// ・回す：マウスホイール / A・D / ←→ / 十字キー（1目盛りずつ）
    /// ・その番号で数秒“止まる”と、その桁を確定（連続で同じ番号は確定できない）
    /// ・回すたびに「カチッ」。暗証桁の位置だけ音が変わり、音程で順番（低→中→高＝1→3桁目）が分かる
    /// 3桁確定で一致すれば開錠。襲撃フェーズ外・死亡で自動的に閉じる。
    /// </summary>
    public class SafeDialUI : MonoBehaviour
    {
        public static SafeDialUI Instance { get; private set; }

        [Header("操作")]
        [SerializeField] private float _dwellTime = 1.2f;    // この秒数止まると確定
        private const int Count = 10;                        // 0-9

        private Font _font;
        private GameObject _panel;
        private Text _bigNumber, _entryText, _footer;
        private Text[] _ring = new Text[Count];
        private RectTransform _pointer;

        private WallSafe _safe;
        private int _current;
        private int _lastRegistered = -1;
        private float _lastInputTime;
        private bool _registeredThisRest;
        private readonly List<int> _entry = new List<int>();
        private AudioSource _audio;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            Instance = this;
            _font = FontProvider.Get();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 0f;
            BuildUI();
        }

        private void OnEnable()
        {
            GameEvents.OnVisitEnd += ForceClose;
            GameEvents.OnWhiteout += ForceCloseW;
            GameEvents.OnGameOver += ForceClose;
            GameEvents.OnGameClear += ForceClose;
        }
        private void OnDisable()
        {
            GameEvents.OnVisitEnd -= ForceClose;
            GameEvents.OnWhiteout -= ForceCloseW;
            GameEvents.OnGameOver -= ForceClose;
            GameEvents.OnGameClear -= ForceClose;
            if (Instance == this) Instance = null;
        }

        private void ForceCloseW(float _) => ForceClose();
        private void ForceClose() { if (IsOpen) Close(); }

        public void Open(WallSafe safe)
        {
            _safe = safe;
            _entry.Clear();
            _lastRegistered = -1;
            _registeredThisRest = false;
            _current = 0;
            _lastInputTime = Time.time;
            IsOpen = true;
            _panel.SetActive(true);
            RefreshVisual();
            GameManager.Instance?.SetBusy(true);
        }

        private void Close()
        {
            IsOpen = false;
            _panel.SetActive(false);
            GameManager.Instance?.SetBusy(false);
        }

        private void Update()
        {
            if (!IsOpen) return;

            if (WasBack()) { Close(); return; }

            int dir = ReadRotate();
            if (dir != 0)
            {
                _current = ((_current + dir) % Count + Count) % Count;
                _lastInputTime = Time.time;
                _registeredThisRest = false;
                PlayTick(_current);
                RefreshVisual();
            }
            else
            {
                // 止まっている間、dwell 経過で確定
                if (!_registeredThisRest && _entry.Count < 3 &&
                    _current != _lastRegistered &&
                    (Time.time - _lastInputTime) >= _dwellTime)
                {
                    RegisterCurrent();
                }
            }
        }

        private void RegisterCurrent()
        {
            _entry.Add(_current);
            _lastRegistered = _current;
            _registeredThisRest = true;
            _audio.PlayOneShot(ProceduralAudio.Beep(), 0.5f);
            RefreshVisual();

            if (_entry.Count >= 3)
            {
                var arr = _entry.ToArray();
                bool ok = PuzzleState.Instance != null && PuzzleState.Instance.TryOpenSafe(arr);
                if (ok)
                {
                    _audio.PlayOneShot(ProceduralAudio.Unlock(), 1f);
                    Close();
                    if (_safe != null) _safe.ShowStory();
                }
                else
                {
                    _audio.PlayOneShot(ProceduralAudio.DialBuzz(), 0.8f);
                    _entry.Clear();
                    _lastRegistered = -1;
                    _registeredThisRest = false;
                    if (HUDManager.Instance != null)
                        HUDManager.Instance.ShowSubtitle("ガチャッ…違う。番号がリセットされた。", 2.5f);
                    RefreshVisual();
                }
            }
        }

        private void PlayTick(int number)
        {
            int order = PuzzleState.Instance != null ? PuzzleState.Instance.SafeDigitOrder(number) : -1;
            if (order >= 0) _audio.PlayOneShot(ProceduralAudio.DialSpecial(order), 0.9f);
            else _audio.PlayOneShot(ProceduralAudio.DialTick(), 0.8f);
        }

        // ============================== 入力 ==============================

        private float _scrollAccum;
        private int ReadRotate()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) return 1;
                if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) return -1;
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                if (gp.dpad.right.wasPressedThisFrame) return 1;
                if (gp.dpad.left.wasPressedThisFrame) return -1;
            }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                _scrollAccum += mouse.scroll.ReadValue().y;
                if (_scrollAccum >= 60f) { _scrollAccum = 0f; return -1; }   // 上回転で数字を戻す
                if (_scrollAccum <= -60f) { _scrollAccum = 0f; return 1; }
            }
            return 0;
#else
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) return 1;
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) return -1;
            float s = Input.mouseScrollDelta.y;
            if (s > 0f) return -1; if (s < 0f) return 1;
            return 0;
#endif
        }

        private bool WasBack()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) return true;
            var gp = Gamepad.current;
            return gp != null && gp.buttonEast.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.E);
#endif
        }

        // ============================== 表示 ==============================

        private void RefreshVisual()
        {
            _bigNumber.text = _current.ToString();
            for (int i = 0; i < Count; i++)
            {
                bool cur = i == _current;
                _ring[i].color = cur ? new Color(1f, 0.9f, 0.4f) : new Color(0.6f, 0.6f, 0.65f);
                _ring[i].fontSize = cur ? 44 : 32;
            }
            // ポインタを現在値へ向ける
            float ang = 90f - _current * (360f / Count);
            _pointer.localRotation = Quaternion.Euler(0, 0, ang - 90f);

            string e = "";
            for (int i = 0; i < 3; i++) e += (i < _entry.Count ? _entry[i].ToString() : "＿") + (i < 2 ? "  " : "");
            _entryText.text = $"入力: <color=#FFE060>{e}</color>";
            _footer.text = "ホイール/A・D/←→で回す　数秒止めて確定　[Esc]閉じる\n" +
                           "回すと音が鳴る——桁の位置だけ音が違う（低→中→高＝1→3桁目）";
        }

        // ============================== UI構築 ==============================

        private void BuildUI()
        {
            var canvasGo = new GameObject("SafeDialCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 55;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("SafePanel");
            _panel.transform.SetParent(canvasGo.transform, false);
            var bg = _panel.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.06f, 0.96f);
            SetRect(bg.rectTransform, Half, Half, Vector2.zero, new Vector2(760, 760));

            var title = MakeText(_panel.transform, "Title", 38, TextAnchor.UpperCenter);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(700, 60));
            title.color = new Color(0.85f, 0.8f, 0.6f);
            title.text = "壁金庫　ダイヤル錠";

            // ダイヤル盤（数字リング）
            var dial = new GameObject("Dial");
            dial.transform.SetParent(_panel.transform, false);
            var dialRt = dial.AddComponent<RectTransform>();
            SetRect(dialRt, Half, Half, new Vector2(0, 40), new Vector2(460, 460));
            var ring = dial.AddComponent<Image>();
            ring.color = new Color(0.12f, 0.12f, 0.14f, 1f);

            float r = 175f;
            for (int i = 0; i < Count; i++)
            {
                float ang = (90f - i * (360f / Count)) * Mathf.Deg2Rad;
                var t = MakeText(dial.transform, "N" + i, 32, TextAnchor.MiddleCenter);
                SetRect(t.rectTransform, Half, Half,
                    new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r), new Vector2(70, 70));
                t.text = i.ToString();
                _ring[i] = t;
            }

            // 中央の大きな現在値
            _bigNumber = MakeText(dial.transform, "Big", 120, TextAnchor.MiddleCenter);
            SetRect(_bigNumber.rectTransform, Half, Half, Vector2.zero, new Vector2(200, 200));
            _bigNumber.color = new Color(1f, 0.95f, 0.7f);

            // ポインタ（上向きの目印）
            var pg = new GameObject("Pointer");
            pg.transform.SetParent(dial.transform, false);
            var pimg = pg.AddComponent<Image>();
            pimg.color = new Color(0.9f, 0.3f, 0.25f);
            _pointer = pimg.rectTransform;
            SetRect(_pointer, Half, Half, new Vector2(0, r + 26f), new Vector2(20, 34));

            _entryText = MakeText(_panel.transform, "Entry", 40, TextAnchor.MiddleCenter);
            SetRect(_entryText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 118), new Vector2(700, 60));

            _footer = MakeText(_panel.transform, "Footer", 22, TextAnchor.LowerCenter);
            SetRect(_footer.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 24), new Vector2(720, 84));
            _footer.color = new Color(0.7f, 0.7f, 0.72f);

            _panel.SetActive(false);
        }

        private static readonly Vector2 Half = new Vector2(0.5f, 0.5f);

        private Text MakeText(Transform parent, string name, int size, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font; text.fontSize = size; text.alignment = anchor;
            text.color = Color.white; text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void SetRect(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.anchoredPosition = pos;
            if (size != Vector2.zero) rt.sizeDelta = size;
            else { rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        }
    }
}
