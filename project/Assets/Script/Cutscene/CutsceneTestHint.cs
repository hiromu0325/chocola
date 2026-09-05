using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// カットシーンテストシーンの操作ヒントと再生コントロール。
    /// カットシーンが終わると「[R] もう一度再生」を表示する（テスト用）。
    /// </summary>
    public class CutsceneTestHint : MonoBehaviour
    {
        private Text _text;
        private bool _finished;

        private void Start()
        {
            Build();
            if (CutsceneDirector.Instance != null)
                CutsceneDirector.Instance.Finished += () => { _finished = true; Refresh(); };
            Refresh();
        }

        private void Update()
        {
            if (!_finished) return;
            if (!ReplayPressed()) return;

            var cd = CutsceneDirector.Instance;
            if (cd == null || cd.Director == null) return;
            _finished = false;
            Refresh();
            cd.Play(cd.Director);
        }

        private static bool ReplayPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.rKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.R);
#endif
        }

        private void Refresh()
        {
            if (_text == null) return;
            _text.text = _finished
                ? "カットシーン終了  —  [R] もう一度再生"
                : "カットシーン再生中  —  [Esc / Space] 長押しでスキップ";
        }

        private void Build()
        {
            var canvasGo = new GameObject("HintCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var go = new GameObject("Hint");
            go.transform.SetParent(canvasGo.transform, false);
            _text = go.AddComponent<Text>();
            _text.font = FontProvider.Get();
            _text.fontSize = 22;
            _text.alignment = TextAnchor.UpperCenter;
            _text.color = new Color(1f, 1f, 1f, 0.7f);
            _text.raycastTarget = false;
            var rt = _text.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -14f);
            rt.sizeDelta = new Vector2(0f, 34f);
        }
    }
}
