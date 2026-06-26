using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 配電盤：①解除コード入力 → ②復旧手順コード入力 → ③長押しで時間をかけて復旧。
    /// 復旧作業は boardRepairDuration 秒の長押しが必要（途中で来訪が来ると中断＝緊張）。
    /// </summary>
    public class DistributionBoard : MonoBehaviour, IInteractable, IPromptProvider
    {
        [SerializeField] private Renderer _indicator;

        private float _lastCallTime = -10f;   // キーパッド開く用デバウンス
        private float _lastHoldTime = -10f;   // 長押し作業の継続判定
        private float _repairProgress;
        private float _repairDuration = 12f;
        private float _clickTimer;
        private AudioSource _audio;

        public bool CanInteract =>
            PuzzleState.Instance == null ||
            (PuzzleState.Instance.PuzzlesEnabled && !PuzzleState.Instance.PowerRestored);

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f; _audio.maxDistance = 15f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
        }

        private void Start()
        {
            var cfg = GameBalanceConfig.Instance;
            if (cfg != null) _repairDuration = Mathf.Max(0.5f, cfg.boardRepairDuration);
            UpdateIndicator();
        }

        private void OnEnable()
        {
            GameEvents.OnPanelUnlocked += UpdateIndicator;
            GameEvents.OnRepairCodeAccepted += UpdateIndicator;
            GameEvents.OnPowerRestored += UpdateIndicator;
        }
        private void OnDisable()
        {
            GameEvents.OnPanelUnlocked -= UpdateIndicator;
            GameEvents.OnRepairCodeAccepted -= UpdateIndicator;
            GameEvents.OnPowerRestored -= UpdateIndicator;
        }

        /// <summary>InteractionController から押下中毎フレーム呼ばれる</summary>
        public void OnInteract()
        {
            var ps = PuzzleState.Instance;
            if (ps == null || PuzzleUI.Instance == null) return;
            if (ps.PowerRestored) return;

            // ステージ③：復旧コード受理済み → 長押し作業（毎フレーム継続を記録）
            if (ps.RepairCodeAccepted)
            {
                _lastHoldTime = Time.time;
                return;
            }

            // ステージ①②：キーパッドUIを開く（単発押下のみ）
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;
            if (PuzzleUI.Instance.IsOpen || PuzzleUI.Instance.BlockReopen) return;

            if (!ps.PanelUnlocked)
            {
                PuzzleUI.Instance.ShowKeypad(
                    "配電盤　キーパッド",
                    "社内PCの部署ページで確認した\nキーパッド解除コード（4桁）を入力",
                    4,
                    code =>
                    {
                        if (ps.TryUnlockPanel(code))
                        {
                            ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 0.8f);
                            if (HUDManager.Instance != null)
                                HUDManager.Instance.ShowSubtitle("パネルが解除された。指定型番の説明書の手順で復旧せよ。", 4f);
                        }
                        else
                        {
                            ProceduralAudio.PlayAt(ProceduralAudio.Beep(), transform.position, 0.8f);
                            PuzzleUI.Instance.ShowDocument("解除失敗",
                                "コードが違います。\n社内PCの部署専用ページで\nキーパッド解除コードを確認してください。");
                        }
                    });
            }
            else
            {
                PuzzleUI.Instance.ShowKeypad(
                    "配電盤　復旧手順",
                    "部署ページが指定する型番の説明書にある\n復旧手順コード（3桁）を入力",
                    3,
                    code =>
                    {
                        if (ps.TryAcceptRepairCode(code))
                        {
                            ProceduralAudio.PlayAt(ProceduralAudio.Beep(), transform.position, 0.8f);
                            if (HUDManager.Instance != null)
                                HUDManager.Instance.ShowSubtitle("手順を確認。配電盤を長押しして復旧作業を進めろ（時間がかかる）。", 5f);
                        }
                        else
                        {
                            ProceduralAudio.PlayAt(ProceduralAudio.Beep(), transform.position, 0.8f);
                            PuzzleUI.Instance.ShowDocument("復旧失敗",
                                "コードが違います。\n部署ページが指定する型番の\n説明書の復旧手順コードを確認してください。");
                        }
                    });
            }
        }

        private void Update()
        {
            var ps = PuzzleState.Instance;
            if (ps == null || !ps.RepairCodeAccepted || ps.PowerRestored) return;

            // 長押し継続中のみ作業が進む
            bool holding = (Time.time - _lastHoldTime) < 0.15f && CanInteract;
            if (!holding) return;

            _repairProgress += Time.deltaTime;

            _clickTimer -= Time.deltaTime;
            if (_clickTimer <= 0f)
            {
                _audio.PlayOneShot(ProceduralAudio.Click(), 0.7f);
                _clickTimer = 0.35f;
            }

            if (_repairProgress >= _repairDuration)
            {
                ps.CompleteRepair();
                _audio.PlayOneShot(ProceduralAudio.Unlock(), 1f);
                if (RoomLightController.Instance != null && !RoomLightController.Instance.LightsOn)
                    RoomLightController.Instance.Toggle();
                if (HUDManager.Instance != null)
                    HUDManager.Instance.ShowSubtitle("電力が復旧した。脱出口の電子錠が開いた。", 4f);
            }
        }

        private void UpdateIndicator()
        {
            if (_indicator == null) _indicator = GetComponentInChildren<Renderer>();
            if (_indicator == null) return;
            var ps = PuzzleState.Instance;
            Color c = new Color(0.9f, 0.2f, 0.2f);                                   // 未解除：赤
            if (ps != null && ps.PowerRestored) c = new Color(0.2f, 0.9f, 0.3f);     // 復旧：緑
            else if (ps != null && ps.RepairCodeAccepted) c = new Color(0.95f, 0.85f, 0.2f); // 作業可：黄
            else if (ps != null && ps.PanelUnlocked) c = new Color(0.9f, 0.6f, 0.1f);// 解除済み：橙
            _indicator.material.color = c;
        }

        public string GetPrompt()
        {
            var ps = PuzzleState.Instance;
            if (ps == null) return "[E] 配電盤";
            if (ps.PowerRestored) return "配電盤（復旧済み）";
            if (!ps.PanelUnlocked) return "[E] 配電盤のキーパッドを操作";
            if (!ps.RepairCodeAccepted) return "[E] 配電盤の復旧手順を入力";
            return "[E] 長押しで復旧作業（時間がかかる）";
        }

        public float GetProgress01()
        {
            var ps = PuzzleState.Instance;
            if (ps != null && ps.RepairCodeAccepted && !ps.PowerRestored)
                return Mathf.Clamp01(_repairProgress / _repairDuration);
            return -1f;
        }
    }
}
