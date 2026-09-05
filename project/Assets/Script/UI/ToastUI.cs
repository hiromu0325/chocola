using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 画面下部に一瞬だけ出る小さな通知（トースト）。
    /// 「手帳に綴じた」「◯◯を手に入れた」など、操作を止めない軽い情報表示に使う。
    /// モーダルではないのでUiQueueとは独立して動く（資料を読んでいる最中でも出る）。
    /// </summary>
    public class ToastUI : MonoBehaviour
    {
        public static ToastUI Instance { get; private set; }

        private readonly Queue<string> _queue = new Queue<string>();
        private bool _showing;
        private CanvasGroup _group;
        private Text _text;
        private Image _plate;

        private void Awake()
        {
            Instance = this;
            Build();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>トーストを表示（ToastUIがシーンに無ければ何もしない）</summary>
        public static void Show(string message)
        {
            if (Instance == null || string.IsNullOrEmpty(message)) return;
            Instance._queue.Enqueue(message);
            if (!Instance._showing) Instance.StartCoroutine(Instance.Pump());
        }

        private IEnumerator Pump()
        {
            _showing = true;
            while (_queue.Count > 0)
            {
                string msg = _queue.Dequeue();
                _text.text = msg;
                // 文字量に合わせて背景の幅を調整
                float w = Mathf.Clamp(_text.preferredWidth + 56f, 260f, 900f);
                _plate.rectTransform.sizeDelta = new Vector2(w, 46f);

                ProceduralAudio.PlayAt(ProceduralAudio.Click(),
                    Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.2f, spatial: false);

                yield return FadeTo(1f, 0.2f);
                for (float t = 0f; t < 1.7f; t += Time.unscaledDeltaTime) yield return null;
                yield return FadeTo(0f, 0.35f);
                for (float t = 0f; t < 0.08f; t += Time.unscaledDeltaTime) yield return null;
            }
            _showing = false;
        }

        private IEnumerator FadeTo(float target, float dur)
        {
            float from = _group.alpha;
            for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
            {
                _group.alpha = Mathf.Lerp(from, target, t / dur);
                yield return null;
            }
            _group.alpha = target;
        }

        private void Build()
        {
            var canvasGo = new GameObject("ToastCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 420;   // 資料UIより上（読んでいる最中の「綴じた」も見える）
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var plateGo = new GameObject("Plate");
            plateGo.transform.SetParent(canvasGo.transform, false);
            _plate = plateGo.AddComponent<Image>();
            _plate.color = new Color(0.05f, 0.05f, 0.06f, 0.82f);
            _plate.raycastTarget = false;
            var prt = _plate.rectTransform;
            prt.anchorMin = new Vector2(0.5f, 0f);
            prt.anchorMax = new Vector2(0.5f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, 130f);
            prt.sizeDelta = new Vector2(420f, 46f);

            // 左端の飾り線（手帳っぽい金の縦線）
            var bar = new GameObject("Accent");
            bar.transform.SetParent(plateGo.transform, false);
            var barImg = bar.AddComponent<Image>();
            barImg.color = new Color(0.85f, 0.72f, 0.4f, 0.95f);
            barImg.raycastTarget = false;
            var brt = barImg.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = new Vector2(6f, 0f);
            brt.sizeDelta = new Vector2(3f, -12f);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(plateGo.transform, false);
            _text = textGo.AddComponent<Text>();
            _text.font = FontProvider.Get();
            _text.fontSize = 24;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.color = new Color(0.95f, 0.93f, 0.87f);
            _text.raycastTarget = false;
            var trt = _text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(20f, 0f);
            trt.offsetMax = new Vector2(-12f, 0f);
        }
    }
}
