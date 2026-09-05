using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using StarterAssets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace EscapeProto
{
    /// <summary>
    /// HUD（実行時にUGUIをコード生成。アセット不要）
    /// ・ステータス（フェーズ・残り人形・解除数・電気・懐中電灯・香水）
    /// ・手帳パネル / 死亡ホワイトアウト / 会話ダイアログ / ゲーム終了画面
    /// </summary>
    public class HUDManager : MonoBehaviour
    {
        public static HUDManager Instance { get; private set; }

        private Font _font;
        private Text _topText, _stateText, _promptText, _endText, _dialogText, _subtitle;
        private GameObject _memoPanel, _endPanel, _scarePanel, _dialogPanel;
        private MemoBoard _memoBoard;
        private RawImage _scareFace;
        private Image _scareFlash, _whiteout;
        private RectTransform _progressFill;
        private GameObject _progressRoot;
        private InteractionController _interaction;
        private PlayerStatus _player;

        private bool _memoOpen, _gameEnded;
        private Action<int> _dialogCallback;
        private int _dialogChoiceCount;

        private void Awake()
        {
            Instance = this;
            _font = FontProvider.Get();
            BuildCanvas();
        }

        private void Start()
        {
            _interaction = FindFirstObjectByType<InteractionController>();
            _player = FindFirstObjectByType<PlayerStatus>();
        }

        private void OnEnable()
        {
            GameEvents.OnJumpScare += PlayJumpScare;
            GameEvents.OnWhiteout += PlayWhiteout;
            GameEvents.OnGameOver += ShowGameOver;
            GameEvents.OnGameClear += ShowGameClear;
            GameEvents.OnLaughterEventStart += OnEventStart;
            GameEvents.OnLaughterEventEnd += OnEventEnd;
        }
        private void OnDisable()
        {
            GameEvents.OnJumpScare -= PlayJumpScare;
            GameEvents.OnWhiteout -= PlayWhiteout;
            GameEvents.OnGameOver -= ShowGameOver;
            GameEvents.OnGameClear -= ShowGameClear;
            GameEvents.OnLaughterEventStart -= OnEventStart;
            GameEvents.OnLaughterEventEnd -= OnEventEnd;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            UpdateTopBar();
            UpdatePrompt();
            HandleKeys();
        }

        // ============= 入力 =============
        private void HandleKeys()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            if (kb == null && gp == null) return;
            bool tab = kb != null && kb.tabKey.wasPressedThisFrame;
            bool restart = kb != null && kb.rKey.wasPressedThisFrame;
            if (gp != null)
            {
                if (gp.selectButton.wasPressedThisFrame) tab = true;       // 手帳
                if (gp.startButton.wasPressedThisFrame) restart = true;    // 終了画面でリスタート
            }
            if (tab) ToggleMemo();
            // ページ送りは右クリックのみ（末尾まで行くとループ）。左クリックはキーワードのドラッグ用
            if (_memoOpen && !_dialogPanel.activeSelf)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.rightButton.wasPressedThisFrame) _memoBoard.NextSpreadLooped();
            }
            if (_gameEnded && restart) GameManager.Instance?.RestartGame();
            if (_dialogPanel.activeSelf)
            {
                bool one = kb != null && kb.digit1Key.wasPressedThisFrame;
                bool two = kb != null && kb.digit2Key.wasPressedThisFrame;
                bool three = kb != null && kb.digit3Key.wasPressedThisFrame;
                if (gp != null)
                {
                    one |= gp.buttonSouth.wasPressedThisFrame;   // A
                    two |= gp.buttonEast.wasPressedThisFrame;    // B
                    three |= gp.buttonWest.wasPressedThisFrame;  // X
                }
                if (one) PickDialog(0);
                else if (two) PickDialog(1);
                else if (three) PickDialog(2);
            }
#else
            if (Input.GetKeyDown(KeyCode.Tab)) ToggleMemo();
            if (_memoOpen && !_dialogPanel.activeSelf && Input.GetMouseButtonDown(1))
                _memoBoard.NextSpreadLooped();
            if (_gameEnded && Input.GetKeyDown(KeyCode.R)) GameManager.Instance?.RestartGame();
            if (_dialogPanel.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) PickDialog(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) PickDialog(1);
                else if (Input.GetKeyDown(KeyCode.Alpha3)) PickDialog(2);
            }
#endif
        }

        public void ToggleMemo()
        {
            // ループ回廊：手帳は最初の部屋の机で拾うまで開けない
            //（回廊の部屋が登録されていないシーン＝施設マップ等では従来どおり）
            if (!_memoOpen &&
                LoopRooms.Get(LoopProgress.StartRoomId) != null && !LoopProgress.NotebookOwned)
            {
                ToastUI.Show("手帳を持っていない（最初の部屋の机にあったはず）");
                return;
            }

            _memoOpen = !_memoOpen;
            if (_memoOpen)
            {
                _memoBoard.JumpToCurrentChapter();   // 今いる部屋の章から開く
                _memoBoard.RebuildAndShow();
            }
            _memoPanel.SetActive(_memoOpen);
        }

        // ============= ステータス =============
        private void UpdateTopBar()
        {
            var pm = PhaseManager.Instance;
            var gm = GameManager.Instance;
            // ※来訪の接近/滞在をUI文字で警告しない設計。
            //   到達は古時計の鐘が告げ、誰が来るかはエントランスのモニター映像を目で見て判断する
            string phase = (pm != null && pm.EventActive) ? "<color=#E060FF>…笑い声がする</color>" : "";
            int dolls = gm != null ? gm.Dolls : 0;
            _topText.text = $"{phase}    人形:{new string('●', Mathf.Max(0, dolls))}    {ObjectiveText()}";

            // 環境ステータス（電気/懐中電灯/香水）
            string lights = (RoomLightController.Instance == null || RoomLightController.Instance.LightsOn)
                ? "電気:点" : "<color=#888>電気:消</color>";
            string flash = (_player != null && _player.FlashlightOn) ? "<color=#FFE060>懐中電灯:点</color>" : "懐中電灯:消";
            string scent = (_player != null && _player.IsScentMasked) ? "<color=#80D0FF>消臭中</color>" : "";
            _stateText.text = $"{lights}   {flash}   {scent}";
        }

        private static string ObjectiveText()
        {
            var ps = PuzzleState.Instance;
            if (ps == null) return "";
            if (!ps.PcAccessed) return "<color=#FFD060>目標:社員情報を集めPCにログイン</color>";
            if (!ps.HasPowerRoomKey) return "<color=#FFD060>目標:貸出記録の社員の個室(2階)で鍵を探す</color>";
            if (!ps.PowerRestored) return "<color=#FFD060>目標:配電室を開け配電盤を復旧</color>";
            return "<color=#7CFC8C>目標:脱出口へ向かえ</color>";
        }

        private void UpdatePrompt()
        {
            string prompt = ""; float progress = -1f;
            var target = _interaction != null ? _interaction.GetCurrentInteractable() : null;
            if (target is IPromptProvider p) { prompt = p.GetPrompt(); progress = p.GetProgress01(); }
            else if (target != null && target.CanInteract) prompt = "[E] 使う";

            _promptText.text = prompt;
            bool showBar = progress >= 0f && progress > 0.001f;
            _progressRoot.SetActive(showBar);
            if (showBar) _progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }

        // ============= 会話ダイアログ =============
        public void ShowDialogue(string speaker, string body, string[] choices, Action<int> onChoice)
        {
            _dialogCallback = onChoice;
            _dialogChoiceCount = choices != null ? choices.Length : 0;
            string c = "";
            if (choices != null)
                for (int i = 0; i < choices.Length; i++)
                    c += $"\n[{i + 1}] {choices[i]}";
            _dialogText.text = $"<color=#FFD0E0>{speaker}</color>\n\n{body}\n{c}";
            _dialogPanel.SetActive(true);
        }

        public void HideDialogue()
        {
            _dialogPanel.SetActive(false);
            _dialogCallback = null;
        }

        private void PickDialog(int idx)
        {
            if (idx >= _dialogChoiceCount) return;
            var cb = _dialogCallback;
            HideDialogue();
            cb?.Invoke(idx);
        }

        /// <summary>画面下部に字幕を表示（数秒で消える）</summary>
        public void ShowSubtitle(string text, float seconds = 3f)
        {
            StopCoroutine(nameof(SubtitleRoutine));
            StartCoroutine(SubtitleRoutine(text, seconds));
        }
        private IEnumerator SubtitleRoutine(string text, float seconds)
        {
            _subtitle.text = text;
            yield return new WaitForSeconds(seconds);
            if (_subtitle.text == text) _subtitle.text = "";
        }

        // ============= イベント演出 =============
        private void OnEventStart()
        {
            ProceduralAudio.PlayAt(ProceduralAudio.Laugh(), Camera.main != null
                ? Camera.main.transform.position : Vector3.zero, 0.9f, false);
            ShowSubtitle("…どこかで女の子が笑っている。時計が止まった。", 5f);
        }
        private void OnEventEnd() => HideDialogue();

        // ============= ジャンプスケア =============
        private Coroutine _scareCoroutine;
        private Vector3 _camBaseLocal;
        private bool _camBaseCaptured;

        private void PlayJumpScare(float intensity)
        {
            if (_scareCoroutine != null) StopCoroutine(_scareCoroutine);
            _scareCoroutine = StartCoroutine(ScareRoutine(intensity));
        }

        private IEnumerator ScareRoutine(float intensity)
        {
            _scarePanel.SetActive(true);
            ProceduralAudio.PlayAt(ProceduralAudio.Scream(), Camera.main != null
                ? Camera.main.transform.position : Vector3.zero, intensity, false);

            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null && !_camBaseCaptured) { _camBaseLocal = cam.localPosition; _camBaseCaptured = true; }
            Vector3 camLocal = _camBaseCaptured ? _camBaseLocal : Vector3.zero;

            float dur = 0.55f + intensity * 0.35f, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime; float k = 1f - t / dur;
                _scareFlash.color = new Color(0.6f, 0f, 0f, 0.55f * k);
                _scareFace.color = new Color(1f, 1f, 1f, Mathf.Clamp01(k * 2f));
                _scareFace.rectTransform.localScale = Vector3.one * (1f + UnityEngine.Random.value * 0.12f * intensity);
                if (cam != null) cam.localPosition = camLocal + (Vector3)UnityEngine.Random.insideUnitCircle * 0.06f * intensity * k;
                yield return null;
            }
            if (cam != null) cam.localPosition = camLocal;
            _scarePanel.SetActive(false);
            _scareCoroutine = null;
        }

        // ============= 死亡ホワイトアウト =============
        private void PlayWhiteout(float intensity)
        {
            StopCoroutine(nameof(WhiteoutRoutine));
            StartCoroutine(WhiteoutRoutine());
        }
        private IEnumerator WhiteoutRoutine()
        {
            // 一瞬で白へ → ゆっくり戻る（再開しない＝GameOver時はそのまま白を残す）
            _whiteout.gameObject.SetActive(true);
            ProceduralAudio.PlayAt(ProceduralAudio.Scream(), Camera.main != null
                ? Camera.main.transform.position : Vector3.zero, 0.7f, false);
            float t = 0f;
            while (t < 0.15f) { t += Time.deltaTime; _whiteout.color = new Color(1, 1, 1, t / 0.15f); yield return null; }
            _whiteout.color = Color.white;

            // ゲームオーバーなら白いまま終了演出へ任せる
            if (_gameEnded) yield break;
            yield return new WaitForSeconds(1.0f);
            t = 0f;
            while (t < 0.6f) { t += Time.deltaTime; _whiteout.color = new Color(1, 1, 1, 1f - t / 0.6f); yield return null; }
            _whiteout.gameObject.SetActive(false);
        }

        private void ShowGameOver()
        {
            _gameEnded = true;
            _endPanel.SetActive(true);
            _endText.text = "<color=#FF3020>そして誰もいなくなった</color>\n陶器の人形はすべて砕けた…\n\n[R] リスタート";
        }
        private void ShowGameClear()
        {
            _gameEnded = true;
            _endPanel.SetActive(true);
            _endText.text = "<color=#60FF80>ESCAPED</color>\n地下室から脱出した\n\n[R] リスタート";
        }

        // ============= UI構築 =============
        private void BuildCanvas()
        {
            var canvasGo = new GameObject("HUDCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            var cross = MakeImage(canvasGo.transform, "Crosshair", new Color(1, 1, 1, 0.7f));
            SetRect(cross.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5, 5));

            _topText = MakeText(canvasGo.transform, "TopBar", 30, TextAnchor.UpperCenter);
            SetRect(_topText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(1500, 50));

            _stateText = MakeText(canvasGo.transform, "StateBar", 26, TextAnchor.UpperCenter);
            SetRect(_stateText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -58), new Vector2(1500, 40));

            _promptText = MakeText(canvasGo.transform, "Prompt", 28, TextAnchor.MiddleCenter);
            SetRect(_promptText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(1300, 44));

            _subtitle = MakeText(canvasGo.transform, "Subtitle", 30, TextAnchor.LowerCenter);
            SetRect(_subtitle.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 120), new Vector2(1500, 80));
            _subtitle.color = new Color(0.9f, 0.85f, 0.9f);

            _progressRoot = new GameObject("ProgressBar");
            _progressRoot.transform.SetParent(canvasGo.transform, false);
            var barBg = _progressRoot.AddComponent<Image>();
            barBg.color = new Color(0, 0, 0, 0.6f);
            SetRect(barBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -135), new Vector2(420, 18));
            var fillGo = new GameObject("Fill"); fillGo.transform.SetParent(_progressRoot.transform, false);
            var fillImg = fillGo.AddComponent<Image>(); fillImg.color = new Color(0.95f, 0.7f, 0.2f);
            _progressFill = fillImg.rectTransform;
            _progressFill.anchorMin = Vector2.zero; _progressFill.anchorMax = new Vector2(0, 1);
            _progressFill.offsetMin = new Vector2(2, 2); _progressFill.offsetMax = new Vector2(-2, -2);
            _progressFill.pivot = new Vector2(0, 0.5f);
            _progressRoot.SetActive(false);

            // 手帳（見開き2ページ。左右クリックでページ送り）
            // ※専用キャンバス（sortingOrder=60）に載せ、PuzzleUI（50）より前面に出す。
            //   社内PCのテンキー等を開いたままTabで手帳を重ねて確認できる（数字入力はキーボードで可能）
            var memoCanvasGo = new GameObject("MemoCanvas");
            memoCanvasGo.transform.SetParent(transform, false);
            var memoCanvas = memoCanvasGo.AddComponent<Canvas>();
            memoCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            memoCanvas.sortingOrder = 60;
            var memoScaler = memoCanvasGo.AddComponent<CanvasScaler>();
            memoScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            memoScaler.referenceResolution = new Vector2(1920, 1080);
            memoCanvasGo.AddComponent<GraphicRaycaster>();

            _memoPanel = new GameObject("MemoPanel"); _memoPanel.transform.SetParent(memoCanvasGo.transform, false);
            var memoBg = _memoPanel.AddComponent<Image>(); memoBg.color = new Color(0.07f, 0.06f, 0.05f, 0.94f);
            SetRect(memoBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1260, 720));

            var memoTitle = MakeText(_memoPanel.transform, "MemoTitle", 30, TextAnchor.UpperCenter);
            SetRect(memoTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -18), new Vector2(600, 44));
            memoTitle.text = "■ 手帳 ■";
            memoTitle.color = new Color(1f, 0.85f, 0.6f);

            // 中央の綴じ線
            var spineGo = new GameObject("Spine"); spineGo.transform.SetParent(_memoPanel.transform, false);
            var spine = spineGo.AddComponent<Image>(); spine.color = new Color(0.35f, 0.3f, 0.24f, 0.9f);
            SetRect(spine.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -8), new Vector2(3, 580));

            // ページ描画・キーワードチップ・関係線は MemoBoard が担当
            _memoBoard = _memoPanel.AddComponent<MemoBoard>();
            _memoBoard.Init((RectTransform)_memoPanel.transform, _font);

            _memoPanel.SetActive(false);

            // 会話ダイアログ
            _dialogPanel = new GameObject("DialogPanel"); _dialogPanel.transform.SetParent(canvasGo.transform, false);
            var dbg = _dialogPanel.AddComponent<Image>(); dbg.color = new Color(0.04f, 0.02f, 0.06f, 0.95f);
            SetRect(dbg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 240), new Vector2(1200, 360));
            _dialogText = MakeText(_dialogPanel.transform, "DialogText", 30, TextAnchor.UpperLeft);
            SetRect(_dialogText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1120, 300));
            _dialogPanel.SetActive(false);

            // ジャンプスケア
            _scarePanel = new GameObject("ScarePanel"); _scarePanel.transform.SetParent(canvasGo.transform, false);
            _scareFlash = _scarePanel.AddComponent<Image>(); _scareFlash.color = Color.clear; _scareFlash.raycastTarget = false;
            SetRect(_scareFlash.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var faceGo = new GameObject("Face"); faceGo.transform.SetParent(_scarePanel.transform, false);
            _scareFace = faceGo.AddComponent<RawImage>(); _scareFace.texture = BuildScareFaceTexture(); _scareFace.raycastTarget = false;
            SetRect(_scareFace.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 560));
            _scarePanel.SetActive(false);

            // ホワイトアウト
            var whiteGo = new GameObject("Whiteout"); whiteGo.transform.SetParent(canvasGo.transform, false);
            _whiteout = whiteGo.AddComponent<Image>(); _whiteout.color = Color.clear; _whiteout.raycastTarget = false;
            SetRect(_whiteout.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            whiteGo.SetActive(false);

            // 終了画面
            _endPanel = new GameObject("EndPanel"); _endPanel.transform.SetParent(canvasGo.transform, false);
            var endBg = _endPanel.AddComponent<Image>(); endBg.color = new Color(0, 0, 0, 0.85f);
            SetRect(endBg.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _endText = MakeText(_endPanel.transform, "EndText", 52, TextAnchor.MiddleCenter);
            SetRect(_endText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300, 400));
            _endPanel.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        private Text MakeText(Transform parent, string name, int size, TextAnchor anchor)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font; text.fontSize = size; text.alignment = anchor;
            text.color = Color.white; text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.9f); outline.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        private static Image MakeImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = color; img.raycastTarget = false;
            return img;
        }

        private static void SetRect(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.anchoredPosition = pos;
            if (size != Vector2.zero) rt.sizeDelta = size;
            else { rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        }

        private static Texture2D BuildScareFaceTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - size * 0.5f) / (size * 0.5f), ny = (y - size * 0.5f) / (size * 0.5f);
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    Color c = Color.clear;
                    if (r < 0.95f) { float s = Mathf.Clamp01(1f - r) * 0.25f; c = new Color(s, s * 0.85f, s * 0.8f, 1f); }
                    c = DrawEye(c, nx, ny, -0.35f, 0.25f);
                    c = DrawEye(c, nx, ny, 0.35f, 0.25f);
                    if (ny < -0.25f && ny > -0.45f)
                    {
                        float mouth = Mathf.Abs(nx) - (0.55f - Mathf.Abs(ny + 0.35f) * 2.2f);
                        float jag = Mathf.PerlinNoise(x * 0.25f, 0f) * 0.08f;
                        if (mouth + jag < 0f) c = new Color(0.05f, 0f, 0f, 1f);
                    }
                    px[y * size + x] = c;
                }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        private static Color DrawEye(Color b, float nx, float ny, float cx, float cy)
        {
            float dx = (nx - cx) / 0.22f, dy = (ny - cy) / 0.30f, d = dx * dx + dy * dy;
            if (d < 1f) b = new Color(0.95f, 0.93f, 0.9f, 1f);
            if (d < 0.06f) b = new Color(0.02f, 0f, 0f, 1f);
            return b;
        }
    }
}
