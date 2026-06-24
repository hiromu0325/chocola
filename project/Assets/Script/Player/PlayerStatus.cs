using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// プレイヤーの「探索者から知覚される状態」を一元管理
    /// ・移動速度 → 足音ノイズ（聴覚型）
    /// ・隠れ状態 → 視覚型/嗅覚型の遮蔽
    /// ・香水による消臭 → 嗅覚型対策
    /// ・懐中電灯/部屋の電気 → 視覚型の可視条件、入室40秒の死亡判定
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerStatus : MonoBehaviour
    {
        [Header("ノイズ半径（m）")]
        [SerializeField] private float _walkNoiseRadius = 6f;
        [SerializeField] private float _sprintNoiseRadius = 14f;
        [SerializeField] private float _stillThreshold = 0.3f;

        private CharacterController _cc;
        private StarterAssetsInputs _inputs;

        public float CurrentSpeed { get; private set; }
        public bool IsMoving => !IsHidden && CurrentSpeed > _stillThreshold;
        public bool IsHidden { get; private set; }
        public float CurrentNoiseRadius { get; private set; }
        public HidingSpot CurrentHidingSpot { get; private set; }

        /// <summary>香水で消臭中か（嗅覚型に検知されない）</summary>
        public bool IsScentMasked => Time.time < _scentMaskUntil;
        /// <summary>懐中電灯が点いているか</summary>
        public bool FlashlightOn { get; private set; }

        private float _scentMaskUntil = -1f;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _inputs = GetComponent<StarterAssetsInputs>();
        }

        private void Start()
        {
            // ScriptableObject の設定値で移動速度を上書き
            var cfg = GameBalanceConfig.Instance;
            if (cfg != null)
            {
                var fpc = GetComponent<FirstPersonController>();
                if (fpc != null)
                {
                    fpc.MoveSpeed = cfg.playerMoveSpeed;
                    fpc.SprintSpeed = cfg.playerSprintSpeed;
                }
            }
        }

        private void OnEnable()
        {
            GameEvents.OnPerfumeUsed += HandlePerfume;
            GameEvents.OnFlashlightChanged += HandleFlashlight;
        }

        private void OnDisable()
        {
            GameEvents.OnPerfumeUsed -= HandlePerfume;
            GameEvents.OnFlashlightChanged -= HandleFlashlight;
        }

        private void HandlePerfume(float dur) => _scentMaskUntil = Time.time + dur;
        private void HandleFlashlight(bool on) => FlashlightOn = on;

        private void Update()
        {
            var v = _cc.velocity; v.y = 0f;
            CurrentSpeed = _cc.enabled ? v.magnitude : 0f;

            if (IsHidden || CurrentSpeed <= _stillThreshold)
            {
                CurrentNoiseRadius = 0f;
            }
            else
            {
                bool sprinting = _inputs != null && _inputs.sprint;
                CurrentNoiseRadius = sprinting ? _sprintNoiseRadius : _walkNoiseRadius;
            }
        }

        public void EmitNoise(float radius) => GameEvents.RaiseNoiseEmitted(transform.position, radius);

        public void SetHidden(bool hidden, HidingSpot spot)
        {
            IsHidden = hidden;
            CurrentHidingSpot = hidden ? spot : null;
        }

        public void ForceExitHiding()
        {
            if (CurrentHidingSpot != null) CurrentHidingSpot.ForceExit();
            IsHidden = false;
            CurrentHidingSpot = null;
        }
    }
}
