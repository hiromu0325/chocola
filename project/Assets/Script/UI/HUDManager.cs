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
    /// ・クロスヘア / インタラクトプロンプト / ギミック進捗バー
    /// ・上部ステータス（フェーズ・残機・解除数）
    /// ・メモ帳パネル（Tab）
    /// ・ジャンプスケア演出（画面・音・カメラシェイク）
    /// ・ゲームオーバー / クリア画面（Rでリスタート）
    /// </summary>
    public class HUDManager : MonoBehaviour
    {
        public static HUDManager Instance { get; private set; }

        private Font _font;
        private Text _topText, _promptText, _memoText, _endText;
        private GameObject _memoPanel, _endPanel, _scarePanel;
        private RawImage _scareFace;
        private Image _scareFlash;
        private RectTransform _progressFill;
        private GameObject _progressRoot;
        private InteractionController _interaction;
        private bool _memoOpen;
        private bool _gameEnded;

        private void Awake()
        {
            Instance = this;
            _font = FontProvider.Get();
            BuildCanvas();
        }

        private void Start()
        {
            _interaction = FindFirstObjectByType<InteractionController>();
        }

        private void OnEnable()
        {
            GameEvents.OnJumpScare += PlayJumpScare;
            GameEvents.OnGameOver += ShowGameOver;
            GameEvents.OnGameClear += ShowGameClear;
        }

        private void OnDisable()
        {
            GameEvents.OnJumpScare -= PlayJumpScare;
            GameEvents.OnGameOver -= ShowGameOver;
            GameEvents.OnGameClear -= ShowGameClear;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            UpdateTopBar();
            UpdatePrompt();
            HandleKeys();
        }

        // ============================== 入力 ==============================

        private void HandleKeys()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.tabKey.wasPressedThisFrame) ToggleMemo();
            if (_gameEnded && kb.rKey.wasPressedThisFrame) GameManager.Instance?.RestartGame();
#else
            if (Input.GetKeyDown(KeyCode.Tab)) ToggleMemo();
            if (_gameEnded && Input.GetKeyDown(KeyCode.R)) GameManager.Instance?.RestartGame();
#endif
        }

        public void ToggleMemo()
        {
            _memoOpen = !_memoOpen;
            _memoPanel.SetActive(_memoOpen);
        }

        // ============================== 更新 ==============================

        private void UpdateTopBar()
        {
            var pm = PhaseManager.Instance;
            var gm = GameManager.Instance;
            string phase = "";
            if (pm != null)
            {
                switch (pm.CurrentPhase)
                {
                    case GamePhase.Exploration: phase = $"<color=#7CFC8C>探索中</color> {MonitorDisplay.FormatTime(pm.PhaseRemaining)}"; break;
                    case GamePhase.Warning: phase = $"<color=#FFC832>警告！</color> {MonitorDisplay.FormatTime(pm.PhaseRemaining)}"; break;
                    case GamePhase.Visit: phase = $"<color=#FF4030>来訪中…隠れろ</color> {MonitorDisplay.FormatTime(pm.PhaseRemaining)}"; break;
                }
            }
            int lives = gm != null ? gm.Lives : 0;
            _topText.text = $"{phase}    人形: {new string('●', Mathf.Max(0, lives))}    " +
                            $"ギミック: {GimmickBase.SolvedCount}/{GimmickBase.TotalCount}";
        }

        private void UpdatePrompt()
        {
            string prompt = "";
            float progress = -1f;

            var target = _interaction != null ? _interaction.GetCurrentInteractable() : null;
            if (target is IPromptProvider provider)
            {
                prompt = provider.GetPrompt();
                progress = provider.GetProgress01();
            }
            else if (target != null && target.CanInteract)
            {
                prompt = "[E] 使う";
            }

            _promptText.text = prompt;
            bool showBar = progress >= 0f && progress > 0.001f;
            _progressRoot.SetActive(showBar);
            if (showBar)
                _progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }

        // ============================== ジャンプスケア ==============================

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
            if (cam != null && !_camBaseCaptured)
            {
                _camBaseLocal = cam.localPosition;
                _camBaseCaptured = true;
            }
            Vector3 camLocal = _camBaseCaptured ? _camBaseLocal : Vector3.zero;

            float dur = 0.55f + intensity * 0.35f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = 1f - t / dur;
                _scareFlash.color = new Color(0.6f, 0f, 0f, 0.55f * k);
                _scareFace.color = new Color(1f, 1f, 1f, Mathf.Clamp01(k * 2f));
                _scareFace.rectTransform.localScale = Vector3.one * (1f + Random.value * 0.12f * intensity);
                if (cam != null)
                    cam.localPosition = camLocal + (Vector3)Random.insideUnitCircle * 0.06f * intensity * k;
                yield return null;
            }
            if (cam != null) cam.localPosition = camLocal;
            _scarePanel.SetActive(false);
            _scareCoroutine = null;
        }

        private void ShowGameOver()
        {
            _gameEnded = true;
            _endPanel.SetActive(true);
            _endText.text = "<color=#FF3020>GAME OVER</color>\n人形はすべて壊れた…\n\n[R] リスタート";
        }

        private void ShowGameClear()
        {
            _gameEnded = true;
            _endPanel.SetActive(true);
            _endText.text = "<color=#60FF80>ESCAPED!</color>\n脱出に成功した！\n\n[R] リスタート";
        }

        // ============================== UI構築 ==============================

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

            // クロスヘア
            var cross = MakeImage(canvasGo.transform, "Crosshair", new Color(1f, 1f, 1f, 0.7f));
            SetRect(cross.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5, 5));

            // 上部ステータス
            _topText = MakeText(canvasGo.transform, "TopBar", 30, TextAnchor.UpperCenter);
            SetRect(_topText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(1400, 50));

            // プロンプト
            _promptText = MakeText(canvasGo.transform, "Prompt", 28, TextAnchor.MiddleCenter);
            SetRect(_promptText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(1200, 44));

            // 進捗バー
            _progressRoot = new GameObject("ProgressBar");
            _progressRoot.transform.SetParent(canvasGo.transform, false);
            var barBg = _progressRoot.AddComponent<Image>();
            barBg.color = new Color(0f, 0f, 0f, 0.6f);
            SetRect(barBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -135), new Vector2(420, 18));
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_progressRoot.transform, false);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = new Color(0.95f, 0.7f, 0.2f);
            _progressFill = fillImg.rectTransform;
            _progressFill.anchorMin = Vector2.zero;
            _progressFill.anchorMax = new Vector2(0f, 1f);
            _progressFill.offsetMin = new Vector2(2, 2);
            _progressFill.offsetMax = new Vector2(-2, -2);
            _progressFill.pivot = new Vector2(0f, 0.5f);
            _progressRoot.SetActive(false);

            // メモパネル
            _memoPanel = new GameObject("MemoPanel");
            _memoPanel.transform.SetParent(canvasGo.transform, false);
            var memoBg = _memoPanel.AddComponent<Image>();
            memoBg.color = new Color(0.07f, 0.06f, 0.05f, 0.93f);
            SetRect(memoBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 620));
            _memoText = MakeText(_memoPanel.transform, "MemoText", 26, TextAnchor.UpperLeft);
            SetRect(_memoText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 540));
            _memoText.text = BuildMemoText();
            _memoPanel.SetActive(false);

            // ジャンプスケアパネル
            _scarePanel = new GameObject("ScarePanel");
            _scarePanel.transform.SetParent(canvasGo.transform, false);
            _scareFlash = _scarePanel.AddComponent<Image>();
            _scareFlash.color = Color.clear;
            _scareFlash.raycastTarget = false;
            SetRect(_scareFlash.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var faceGo = new GameObject("Face");
            faceGo.transform.SetParent(_scarePanel.transform, false);
            _scareFace = faceGo.AddComponent<RawImage>();
            _scareFace.texture = BuildScareFaceTexture();
            _scareFace.raycastTarget = false;
            SetRect(_scareFace.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 560));
            _scarePanel.SetActive(false);

            // 終了パネル
            _endPanel = new GameObject("EndPanel");
            _endPanel.transform.SetParent(canvasGo.transform, false);
            var endBg = _endPanel.AddComponent<Image>();
            endBg.color = new Color(0f, 0f, 0f, 0.85f);
            SetRect(endBg.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _endText = MakeText(_endPanel.transform, "EndText", 52, TextAnchor.MiddleCenter);
            SetRect(_endText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 400));
            _endPanel.SetActive(false);
        }

        private static string BuildMemoText()
        {
            return "■ 古びたメモ帳 ■\n\n" +
                   $"◆ {GameEvents.GetEnemyName(EnemyType.Sight)}\n{GameEvents.GetEnemyHint(EnemyType.Sight)}\n\n" +
                   $"◆ {GameEvents.GetEnemyName(EnemyType.Sound)}\n{GameEvents.GetEnemyHint(EnemyType.Sound)}\n\n" +
                   $"◆ {GameEvents.GetEnemyName(EnemyType.Motion)}\n{GameEvents.GetEnemyHint(EnemyType.Motion)}\n\n" +
                   "…奴らは時間きっかりにやって来る。\nモニターを見ろ。時計を見ろ。急ぐな。\n（Tabで閉じる）";
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
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            // 視認性用の縁取り
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        private static Image MakeImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = anchoredPos;
            if (size != Vector2.zero) rt.sizeDelta = size;
            else { rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
        }

        /// <summary>不気味な顔テクスチャをプロシージャル生成</summary>
        private static Texture2D BuildScareFaceTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - size * 0.5f) / (size * 0.5f);
                    float ny = (y - size * 0.5f) / (size * 0.5f);
                    float r = Mathf.Sqrt(nx * nx + ny * ny);

                    // 楕円の顔（暗い肌色〜黒）
                    Color c = Color.clear;
                    if (r < 0.95f)
                    {
                        float shade = Mathf.Clamp01(1f - r) * 0.25f;
                        c = new Color(shade, shade * 0.85f, shade * 0.8f, 1f);
                    }

                    // 目（白目＋小さな黒点）
                    c = DrawEye(c, nx, ny, -0.35f, 0.25f);
                    c = DrawEye(c, nx, ny, 0.35f, 0.25f);

                    // 裂けた口
                    if (ny < -0.25f && ny > -0.45f)
                    {
                        float mouth = Mathf.Abs(nx) - (0.55f - Mathf.Abs(ny + 0.35f) * 2.2f);
                        float jag = Mathf.PerlinNoise(x * 0.25f, 0f) * 0.08f;
                        if (mouth + jag < 0f)
                            c = new Color(0.05f, 0f, 0f, 1f);
                    }

                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static Color DrawEye(Color baseColor, float nx, float ny, float cx, float cy)
        {
            float dx = (nx - cx) / 0.22f;
            float dy = (ny - cy) / 0.30f;
            float d = dx * dx + dy * dy;
            if (d < 1f) baseColor = new Color(0.95f, 0.93f, 0.9f, 1f);
            if (d < 0.06f) baseColor = new Color(0.02f, 0f, 0f, 1f);
            return baseColor;
        }
    }
}
