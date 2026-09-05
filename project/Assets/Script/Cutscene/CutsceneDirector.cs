using System;
using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// カットシーン再生の統括。
    /// ・PlayableDirector（Timeline）の再生／停止
    /// ・再生中はプレイヤー操作を止め、レターボックス（上下の黒帯）を出す
    /// ・[Esc]または[Space]長押しでスキップ（進捗リング付き）
    /// ・終了時に元の状態へ戻し <see cref="Finished"/> を通知する
    ///
    /// ゲーム本編からは <c>CutsceneDirector.Instance.Play(director)</c> で再生する。
    /// </summary>
    public class CutsceneDirector : MonoBehaviour
    {
        public static CutsceneDirector Instance { get; private set; }

        [Header("再生対象（未指定ならPlay時に渡す）")]
        public PlayableDirector Director;
        [Tooltip("シーン開始と同時に再生する（テストシーン用）")]
        public bool PlayOnStart;
        [Tooltip("スキップに必要な長押し秒数")]
        public float SkipHoldSeconds = 0.8f;
        [Tooltip("上下の黒帯の高さ（画面比）")]
        [Range(0f, 0.25f)] public float LetterboxRatio = 0.12f;

        /// <summary>カットシーン終了時（スキップ含む）</summary>
        public event Action Finished;

        public bool IsPlaying { get; private set; }

        private CutsceneSubtitleView _subtitles;
        private CanvasGroup _overlay;
        private RectTransform _barTop, _barBottom;
        private Image _skipFill;
        private Text _skipLabel;
        private float _holdTimer;

        // プレイヤー操作の復帰用
        private GameObject _playerCamGo;   // 無効化後もSetActiveできるようキャッシュ
        private FirstPersonController _fpc;
        private StarterAssetsInputs _inputs;
        private InteractionController _interaction;
        private bool _prevCursorLocked;

        private void Awake()
        {
            Instance = this;
            BuildOverlay();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            _subtitles = FindFirstObjectByType<CutsceneSubtitleView>();
            if (PlayOnStart && Director != null) Play(Director);
        }

        // ============================== 再生制御 ==============================

        public void Play(PlayableDirector director)
        {
            if (director == null || IsPlaying) return;
            Director = director;
            IsPlaying = true;
            _holdTimer = 0f;

            SetPlayerControl(false);
            StartCoroutine(FadeBars(1f, 0.35f));

            director.stopped += HandleStopped;
            director.time = 0d;
            director.Play();
        }

        /// <summary>途中で打ち切る（スキップ／中断）</summary>
        public void Stop()
        {
            if (!IsPlaying || Director == null) return;
            // 最終状態を反映させてから止める（カメラや小物が中途半端な姿勢で残らない）
            Director.time = Director.duration;
            Director.Evaluate();
            Director.Stop();   // stopped イベント経由で後始末される
        }

        private void HandleStopped(PlayableDirector d)
        {
            if (d != null) d.stopped -= HandleStopped;
            if (!IsPlaying) return;
            IsPlaying = false;

            if (_subtitles != null) _subtitles.Clear();
            StartCoroutine(FinishRoutine());
        }

        private IEnumerator FinishRoutine()
        {
            yield return FadeBars(0f, 0.4f);
            SetPlayerControl(true);
            Finished?.Invoke();
        }

        // ============================== スキップ入力 ==============================

        private void Update()
        {
            if (!IsPlaying) return;

            bool held = SkipHeld();
            _holdTimer = held ? _holdTimer + Time.unscaledDeltaTime : 0f;

            if (_skipFill != null)
                _skipFill.fillAmount = Mathf.Clamp01(_holdTimer / Mathf.Max(0.05f, SkipHoldSeconds));
            if (_skipLabel != null)
                _skipLabel.color = new Color(1f, 1f, 1f, held ? 1f : 0.55f);

            if (_holdTimer >= SkipHoldSeconds) Stop();
        }

        private static bool SkipHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.isPressed || kb.spaceKey.isPressed)) return true;
            var gp = Gamepad.current;
            if (gp != null && (gp.buttonEast.isPressed || gp.startButton.isPressed)) return true;
            return false;
#else
            return Input.GetKey(KeyCode.Escape) || Input.GetKey(KeyCode.Space);
#endif
        }

        // ============================== プレイヤー操作の停止／復帰 ==============================

        private void SetPlayerControl(bool enabled)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                if (_fpc == null) _fpc = player.GetComponent<FirstPersonController>();
                if (_inputs == null) _inputs = player.GetComponent<StarterAssetsInputs>();
                if (_interaction == null) _interaction = player.GetComponent<InteractionController>();

                if (_fpc != null) _fpc.enabled = enabled;
                if (_interaction != null) _interaction.enabled = enabled;
                if (_inputs != null)
                {
                    if (!enabled)
                    {
                        _prevCursorLocked = _inputs.cursorLocked;
                        _inputs.move = Vector2.zero;
                        _inputs.look = Vector2.zero;
                        _inputs.sprint = false;
                    }
                    _inputs.cursorInputForLook = enabled;
                    _inputs.cursorLocked = enabled ? _prevCursorLocked : _inputs.cursorLocked;
                }
            }

            // カットシーン中はカメラを奪うので、プレイヤーのカメラは切っておく。
            // 注意: 無効化した後は GetComponentInChildren では見つからなくなる
            // （非アクティブは検索対象外）ため、参照を必ずキャッシュしておく
            if (_playerCamGo == null && player != null)
            {
                var playerCam = player.GetComponentInChildren<Camera>(true);
                if (playerCam != null) _playerCamGo = playerCam.gameObject;
            }
            if (_playerCamGo != null) _playerCamGo.SetActive(enabled);

            Cursor.visible = !enabled ? false : Cursor.visible;
        }

        // ============================== オーバーレイUI ==============================

        private void BuildOverlay()
        {
            var font = FontProvider.Get();

            var canvasGo = new GameObject("CutsceneOverlay");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 450;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _overlay = canvasGo.AddComponent<CanvasGroup>();
            _overlay.alpha = 0f;
            _overlay.blocksRaycasts = false;
            _overlay.interactable = false;

            _barTop = MakeBar(canvasGo.transform, "BarTop", true);
            _barBottom = MakeBar(canvasGo.transform, "BarBottom", false);

            // スキップ表示（右下）
            var skipRoot = new GameObject("Skip");
            skipRoot.transform.SetParent(canvasGo.transform, false);
            var srt = skipRoot.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(1f, 0f);
            srt.anchorMax = new Vector2(1f, 0f);
            srt.pivot = new Vector2(1f, 0f);
            srt.anchoredPosition = new Vector2(-40f, 40f);
            srt.sizeDelta = new Vector2(300f, 40f);

            var bg = new GameObject("Bar");
            bg.transform.SetParent(skipRoot.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0.18f);
            bgImg.raycastTarget = false;
            var brt = bgImg.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.sizeDelta = new Vector2(0f, 6f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(bg.transform, false);
            _skipFill = fill.AddComponent<Image>();
            _skipFill.color = new Color(1f, 0.9f, 0.6f, 0.95f);
            _skipFill.raycastTarget = false;
            _skipFill.type = Image.Type.Filled;
            _skipFill.fillMethod = Image.FillMethod.Horizontal;
            _skipFill.fillAmount = 0f;
            var frt = _skipFill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(skipRoot.transform, false);
            _skipLabel = labelGo.AddComponent<Text>();
            _skipLabel.font = font;
            _skipLabel.fontSize = 20;
            _skipLabel.alignment = TextAnchor.LowerRight;
            _skipLabel.text = "[Esc / Space] 長押しでスキップ";
            _skipLabel.color = new Color(1f, 1f, 1f, 0.55f);
            _skipLabel.raycastTarget = false;
            var lrt = _skipLabel.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(0f, 10f);
            lrt.offsetMax = Vector2.zero;
        }

        private static RectTransform MakeBar(Transform parent, string name, bool top)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.sizeDelta = new Vector2(0f, 0f);
            return rt;
        }

        /// <summary>黒帯を出し入れする（0=収納, 1=展開）</summary>
        private IEnumerator FadeBars(float target, float duration)
        {
            float height = Screen.height * LetterboxRatio;
            float startAlpha = _overlay.alpha;
            float startH = _barTop.sizeDelta.y;
            float endH = height * target;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / duration);
                _overlay.alpha = Mathf.Lerp(startAlpha, target > 0f ? 1f : 0f, k);
                float h = Mathf.Lerp(startH, endH, k);
                _barTop.sizeDelta = new Vector2(0f, h);
                _barBottom.sizeDelta = new Vector2(0f, h);
                yield return null;
            }
            _overlay.alpha = target > 0f ? 1f : 0f;
            _barTop.sizeDelta = new Vector2(0f, endH);
            _barBottom.sizeDelta = new Vector2(0f, endH);
        }
    }
}
