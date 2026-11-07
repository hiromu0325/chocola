using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 部屋に入った時の部屋の名前や、「扉が開いた」「ブレイカーを復旧した」などの知らせを、
    /// 左上（目標が出ていればそのすぐ下）に小さく1行で出す。
    /// 画面の中央はふさがない（入った直後こそ部屋を見回したいので）。歩く・見回すのも止めない。
    /// 初めて部屋に入った時に RoomTransitionSystem から呼ばれる。
    /// 表示中に次の要求が来た場合は UiQueue で待たせて順に出す。
    /// </summary>
    public class RoomTitleUI : MonoBehaviour
    {
        public static RoomTitleUI Instance { get; private set; }

        /// <summary>表示を維持する秒数（フェード除く）。中央をふさがないので、読み終えられるだけ長めに出す</summary>
        private const float HoldSeconds = 4f;

        private const float PlateTop = 14f;   // 文の上端から暗幕の上端まで
        private const float Gap = 10f;        // 目標の暗幕との間

        private CanvasGroup _group;
        private RectTransform _line;
        private Image _plate, _rule;
        private Text _text;
        private float _y = float.NaN;

        /// <summary>知らせが表示中か（UiQueueが次のUIを待たせる判定に使う）</summary>
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

        private void LateUpdate()
        {
            // 資料を調べている・手帳を開いている間は消して早送りする（下から透けて見えないように）
            if (IsShowing && (InspectView.IsOpen || NotebookUI.IsOpen))
            {
                _hurry = true;
                _group.alpha = 0f;
            }
            // 目標が出ている間はその下へ、出ていなければ目標の位置へ（目標が出入りしても重ならないよう、なめらかに寄せる）
            float want = TopY();
            _y = float.IsNaN(_y) || _group.alpha < 0.01f ? want : Mathf.Lerp(_y, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
            _line.anchoredPosition = new Vector2(UiTheme.SafeX, _y);
        }

        private static float TopY()
        {
            float bottom = HUDManager.Instance != null ? HUDManager.Instance.ObjectiveBottom : float.NaN;
            return float.IsNaN(bottom) ? -UiTheme.SafeY : bottom - Gap - PlateTop;
        }

        /// <summary>
        /// 知らせを表示する。sub（章節ラベルや説明）はnull可。
        /// 資料ウィンドウ等が開いている間はUiQueueで待機し、閉じてから表示される。
        /// </summary>
        public void Show(string main, string sub) => Show(main, sub, null);

        /// <summary>出る瞬間に鳴らす音を指定して表示する（扉が開いた時の解錠の音など。null なら小さなクリック音）</summary>
        public void Show(string main, string sub, AudioClip sound, float volume = 0.8f)
        {
            if (string.IsNullOrEmpty(main)) return;
            if (UiQueue.Instance != null)
                UiQueue.Instance.Enqueue(
                    () => StartCoroutine(PlayOnce(main, sub, sound, volume)),
                    () => IsShowing,
                    "title:" + main + sub);
            else
                StartCoroutine(PlayOnce(main, sub, sound, volume));
        }

        private IEnumerator PlayOnce(string main, string sub, AudioClip sound, float volume)
        {
            IsShowing = true;
            _hurry = false;
            // 暗転遷移の明け際に重なるよう、わずかに待ってから出す
            yield return new WaitForSeconds(0.4f);

            SetLine(main, sub);
            ProceduralAudio.PlayAt(sound != null ? sound : ProceduralAudio.Click(),
                Camera.main != null ? Camera.main.transform.position : Vector3.zero, sound != null ? volume : 0.25f, spatial: false);

            yield return FadeTo(1f, 0.25f);

            // 保持時間（Hurry要求が来たら残りを打ち切る）
            for (float t = 0f; t < HoldSeconds && !_hurry; t += Time.unscaledDeltaTime)
                yield return null;

            yield return FadeTo(0f, _hurry ? 0.3f : 0.8f);
            IsShowing = false;
        }

        /// <summary>名前は主テキスト、説明は小さく薄く、同じ行に続ける（帯用の飾りの「──」は外す）</summary>
        private void SetLine(string main, string sub)
        {
            sub = string.IsNullOrEmpty(sub) ? "" : sub.Trim('─', ' ', '　');
            int n = Ruby.Strip(main).Length;
            string open = $"<size={UiTheme.FsSmall}><color={UiTheme.Rgb(UiTheme.TextSub)}>";
            // 間の空白も小さい方に含める（足すのはタグだけにして、ルビの位置の付け替えを崩さない）
            bool ruby = RubyText.SetMarked(_text, sub.Length > 0 ? main + "　" + sub : main, s =>
                n < s.Length ? s.Substring(0, n) + open + s.Substring(n) + "</color></size>" : s);
            float pad = ruby ? RubyText.RoomAbove(_text) : 0f;
            _text.rectTransform.anchoredPosition = new Vector2(34f, -pad);
            _rule.rectTransform.anchoredPosition = new Vector2(0f, -pad - UiTheme.FsHud * 0.6f);
            float w = _text.preferredWidth, h = _text.preferredHeight;
            _plate.rectTransform.sizeDelta = new Vector2(34f + w + 48f, h + pad + PlateTop * 2f);
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
            // HUD・ミニマップより上、資料UI（50）・手帳（60）より下（読んでいる資料に重ならない）
            var canvas = UiTheme.Canvas(transform, "RoomTitleCanvas", 45, raycast: false);
            _group = canvas.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // 目標と同じ形：暗幕＋左の短い罫＋1行の文
            _line = UiTheme.Rect(canvas.transform, "Notice");
            UiTheme.Place(_line, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(UiTheme.SafeX, -UiTheme.SafeY), new Vector2(1200f, 40f));
            _plate = UiTheme.Fill(_line, "Scrim", UiTheme.WithAlpha(Color.black, 0.7f));   // 回廊の白壁の前でも読める濃さ（目標と同じ）
            UiTheme.Place(_plate.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-24f, PlateTop), new Vector2(400f, 60f));
            _rule = UiTheme.Fill(_line, "Rule", UiTheme.Accent);
            UiTheme.Place(_rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, -UiTheme.FsHud * 0.6f), new Vector2(24f, 2f));
            _text = UiTheme.Label(_line, "Text", UiTheme.FsHud, TextAnchor.UpperLeft);
            UiTheme.Place(_text.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, 0f), new Vector2(1600f, 40f));
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }
}
