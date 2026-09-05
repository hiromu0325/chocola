using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 字幕の表示。SubtitleTrack のバインド先。
    /// UIは実行時に自前で構築するので、シーンには空のGameObjectに付けるだけでよい。
    /// </summary>
    public class CutsceneSubtitleView : MonoBehaviour
    {
        [Tooltip("字幕の縦位置（画面下からの割合）")]
        [Range(0f, 0.5f)] public float BottomMargin = 0.12f;
        public int FontSize = 30;

        private Canvas _canvas;
        private CanvasGroup _group;
        private Text _line;
        private Text _speaker;
        private Image _plate;

        private void Awake() => Build();

        /// <summary>SubtitleMixerから毎フレーム呼ばれる。weight=0で非表示</summary>
        public void Render(string text, string speaker, Color color, float weight)
        {
            if (_group == null) return;

            if (string.IsNullOrEmpty(text) || weight <= 0.001f)
            {
                _group.alpha = 0f;
                return;
            }

            _group.alpha = Mathf.Clamp01(weight);
            _line.text = text;
            _line.color = color;

            bool hasSpeaker = !string.IsNullOrEmpty(speaker);
            _speaker.gameObject.SetActive(hasSpeaker);
            if (hasSpeaker) _speaker.text = speaker;

            // 文字量に合わせて背景プレートの高さを合わせる
            float h = _line.preferredHeight + (hasSpeaker ? 34f : 8f) + 24f;
            _plate.rectTransform.sizeDelta = new Vector2(0f, Mathf.Max(70f, h));
        }

        /// <summary>カットシーン終了・スキップ時に消す</summary>
        public void Clear()
        {
            if (_group != null) _group.alpha = 0f;
        }

        private void Build()
        {
            var font = FontProvider.Get();

            var canvasGo = new GameObject("SubtitleCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 400;   // レターボックス(450)より下、通常HUDより上
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // 背景プレート（読みやすさのための半透明帯）
            var plateGo = new GameObject("Plate");
            plateGo.transform.SetParent(canvasGo.transform, false);
            _plate = plateGo.AddComponent<Image>();
            _plate.color = new Color(0f, 0f, 0f, 0.55f);
            _plate.raycastTarget = false;
            var prt = _plate.rectTransform;
            prt.anchorMin = new Vector2(0.5f, BottomMargin);
            prt.anchorMax = new Vector2(0.5f, BottomMargin);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.sizeDelta = new Vector2(1300f, 90f);

            // 話者名
            var spGo = new GameObject("Speaker");
            spGo.transform.SetParent(plateGo.transform, false);
            _speaker = spGo.AddComponent<Text>();
            _speaker.font = font;
            _speaker.fontSize = Mathf.RoundToInt(FontSize * 0.78f);
            _speaker.alignment = TextAnchor.UpperLeft;
            _speaker.color = new Color(1f, 0.85f, 0.45f);
            _speaker.raycastTarget = false;
            var srt = _speaker.rectTransform;
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0f, -8f);
            srt.sizeDelta = new Vector2(-56f, 30f);
            spGo.SetActive(false);

            // 本文
            var lineGo = new GameObject("Line");
            lineGo.transform.SetParent(plateGo.transform, false);
            _line = lineGo.AddComponent<Text>();
            _line.font = font;
            _line.fontSize = FontSize;
            _line.alignment = TextAnchor.MiddleCenter;
            _line.color = Color.white;
            _line.horizontalOverflow = HorizontalWrapMode.Wrap;
            _line.verticalOverflow = VerticalWrapMode.Overflow;
            _line.raycastTarget = false;
            var lrt = _line.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(28f, 14f);
            lrt.offsetMax = new Vector2(-28f, -34f);
        }
    }
}
