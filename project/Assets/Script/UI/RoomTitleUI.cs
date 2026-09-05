using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 部屋の名称を画面中央に大きく表示する（ダークソウル風）。
    /// 半透明の黒帯＋上下の細いライン＋大きな部屋名＋その上に小さな章節ラベル。
    /// 初めて部屋に入った時に RoomTransitionSystem から呼ばれる。
    /// 表示中に次の要求が来た場合はキューして順に出す。
    /// </summary>
    public class RoomTitleUI : MonoBehaviour
    {
        public static RoomTitleUI Instance { get; private set; }

        [Tooltip("表示を維持する秒数（フェード除く）")]
        public float HoldSeconds = 2.2f;

        private CanvasGroup _group;
        private Text _main;
        private Text _sub;

        /// <summary>この帯が表示中か（UiQueueが次のUIを待たせる判定に使う）</summary>
        public bool IsShowing { get; private set; }

        private bool _hurry;

        /// <summary>後ろにUIがつかえている時：残りの表示を早送りして道を空ける</summary>
        public void Hurry() => _hurry = true;

        private void Awake()
        {
            Instance = this;
            Build();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// タイトルを表示する。sub（章節ラベル）はnull可。
        /// 資料ウィンドウ等が開いている間はUiQueueで待機し、閉じてから表示される。
        /// </summary>
        public void Show(string main, string sub)
        {
            if (string.IsNullOrEmpty(main)) return;
            if (UiQueue.Instance != null)
                UiQueue.Instance.Enqueue(
                    () => StartCoroutine(PlayOnce(main, sub)),
                    () => IsShowing,
                    "title:" + main);
            else
                StartCoroutine(PlayOnce(main, sub));
        }

        private IEnumerator PlayOnce(string main, string sub)
        {
            IsShowing = true;
            _hurry = false;
            // 暗転遷移の明け際に重なるよう、わずかに待ってから出す
            yield return new WaitForSeconds(0.4f);

            _main.text = main;
            bool hasSub = !string.IsNullOrEmpty(sub);
            _sub.gameObject.SetActive(hasSub);
            if (hasSub) _sub.text = sub;

            ProceduralAudio.PlayAt(ProceduralAudio.Click(),
                Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.25f, spatial: false);

            yield return FadeTo(1f, 0.6f);

            // 保持時間（Hurry要求が来たら残りを打ち切る）
            for (float t = 0f; t < HoldSeconds && !_hurry; t += Time.unscaledDeltaTime)
                yield return null;

            yield return FadeTo(0f, _hurry ? 0.3f : 0.9f);
            IsShowing = false;
        }

        private IEnumerator FadeTo(float target, float dur)
        {
            float from = _group.alpha, t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t / dur));
                yield return null;
            }
            _group.alpha = target;
        }

        private void Build()
        {
            var font = FontProvider.Get();

            var canvasGo = new GameObject("RoomTitleCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;   // 資料UIやカットシーン帯より下
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // 中央の黒帯（画面中央やや上）
            var band = new GameObject("Band");
            band.transform.SetParent(canvasGo.transform, false);
            var bandImg = band.AddComponent<Image>();
            bandImg.color = new Color(0f, 0f, 0f, 0.62f);
            bandImg.raycastTarget = false;
            var brt = bandImg.rectTransform;
            brt.anchorMin = new Vector2(0f, 0.5f);
            brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0f, 60f);
            brt.sizeDelta = new Vector2(0f, 150f);

            // 上下の細い金色ライン（帯の内側）
            foreach (float y in new[] { 71f, -71f })
            {
                var line = new GameObject("Line");
                line.transform.SetParent(band.transform, false);
                var img = line.AddComponent<Image>();
                img.color = new Color(0.78f, 0.70f, 0.48f, 0.9f);
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, y);
                rt.sizeDelta = new Vector2(760f, 2f);
            }

            // 章節ラベル（小・上）
            var subGo = new GameObject("Chapter");
            subGo.transform.SetParent(band.transform, false);
            _sub = subGo.AddComponent<Text>();
            _sub.font = font;
            _sub.fontSize = 24;
            _sub.alignment = TextAnchor.MiddleCenter;
            _sub.color = new Color(0.85f, 0.80f, 0.65f);
            _sub.raycastTarget = false;
            var srt = _sub.rectTransform;
            srt.anchorMin = new Vector2(0f, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.anchoredPosition = new Vector2(0f, 42f);
            srt.sizeDelta = new Vector2(0f, 32f);

            // 部屋名（大・中央）
            var mainGo = new GameObject("Title");
            mainGo.transform.SetParent(band.transform, false);
            _main = mainGo.AddComponent<Text>();
            _main.font = font;
            _main.fontSize = 58;
            _main.alignment = TextAnchor.MiddleCenter;
            _main.color = new Color(0.96f, 0.94f, 0.88f);
            _main.raycastTarget = false;
            var mrt = _main.rectTransform;
            mrt.anchorMin = new Vector2(0f, 0.5f);
            mrt.anchorMax = new Vector2(1f, 0.5f);
            mrt.anchoredPosition = new Vector2(0f, -14f);
            mrt.sizeDelta = new Vector2(0f, 76f);
        }
    }
}
