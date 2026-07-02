using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 壁に埋め込まれたダイヤル式の金庫。襲撃（来訪）フェーズ中のみ回せる。
    /// 脱出には不要だが、開けるとストーリーが読める。
    /// 暗証番号の手がかりは「回した時の音」——暗証桁の位置だけSEが変わる（SafeDialUI）。
    /// </summary>
    public class WallSafe : MonoBehaviour, IInteractable, IPromptProvider
    {
        [SerializeField] private Renderer _indicator;
        private float _lastCallTime = -10f;

        private static bool IsRaid =>
            PhaseManager.Instance != null && PhaseManager.Instance.CurrentPhase == GamePhase.Visit;

        public bool CanInteract =>
            GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private void Start() => UpdateIndicator();
        private void OnEnable() => GameEvents.OnSafeOpened += UpdateIndicator;
        private void OnDisable() => GameEvents.OnSafeOpened -= UpdateIndicator;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;
            var ps = PuzzleState.Instance;
            if (ps == null) return;

            if (ps.SafeOpened)
            {
                ShowStory();
                return;
            }
            if (!IsRaid)
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 0.5f);
                if (HUDManager.Instance != null)
                    HUDManager.Instance.ShowSubtitle("ダイヤルは固く動かない。…襲撃の最中だけ回るようだ。", 3f);
                return;
            }
            if (SafeDialUI.Instance != null && !SafeDialUI.Instance.IsOpen)
                SafeDialUI.Instance.Open(this);
        }

        /// <summary>開錠時／再閲覧時に呼ぶストーリー開示</summary>
        public void ShowStory()
        {
            if (PuzzleUI.Instance == null) return;
            PuzzleUI.Instance.ShowDocument("金庫の中身　―　古い手記",
                "「実験体はもう区別がつかない。人形も、私も。\n" +
                "　鐘が鳴るたび“探索者”が来る。あれは元は職員だった。\n" +
                "　陶器の人形が割れるたび、誰かがいなくなる。\n\n" +
                "　もし君がこれを読んでいるなら――\n" +
                "　電源を戻して、ここから出ろ。振り返るな。\n" +
                "　そして、笑い声には決して答えるな。」\n\n" +
                "（脱出には不要だが、この場所の真実が少し分かった）");
        }

        private void UpdateIndicator()
        {
            if (_indicator == null) _indicator = GetComponentInChildren<Renderer>();
            if (_indicator == null) return;
            bool opened = PuzzleState.Instance != null && PuzzleState.Instance.SafeOpened;
            _indicator.material.color = opened ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.55f, 0.45f, 0.15f);
        }

        public string GetPrompt()
        {
            var ps = PuzzleState.Instance;
            if (ps != null && ps.SafeOpened) return "[E] 金庫の手記を読む";
            if (!IsRaid) return "金庫（襲撃中だけダイヤルが回る）";
            return "[E] ダイヤルを回す";
        }
        public float GetProgress01() => -1f;
    }
}
