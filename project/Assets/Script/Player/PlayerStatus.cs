using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// プレイヤーの「敵から知覚される状態」を一元管理する
    /// ・移動速度 → 動体型の検知 / ノイズレベル
    /// ・ノイズ半径 → 聴覚型の検知
    /// ・隠れ状態 → 視覚型/動体型からの遮蔽
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerStatus : MonoBehaviour
    {
        [Header("ノイズが聞こえる半径（m）")]
        [SerializeField] private float _walkNoiseRadius = 6f;
        [SerializeField] private float _sprintNoiseRadius = 14f;
        [Tooltip("この速度未満は「静止」とみなす（動体型対策）")]
        [SerializeField] private float _stillThreshold = 0.3f;

        private CharacterController _cc;
        private StarterAssetsInputs _inputs;

        /// <summary>現在の水平移動速度（m/s）</summary>
        public float CurrentSpeed { get; private set; }
        /// <summary>動いているか（動体型の検知対象か）</summary>
        public bool IsMoving => !IsHidden && CurrentSpeed > _stillThreshold;
        /// <summary>ロッカー等に隠れているか（視線が通らない）</summary>
        public bool IsHidden { get; private set; }
        /// <summary>現在の足音が聞こえる半径。静止/隠れ中は0</summary>
        public float CurrentNoiseRadius { get; private set; }
        /// <summary>現在入っている隠れスポット</summary>
        public HidingSpot CurrentHidingSpot { get; private set; }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _inputs = GetComponent<StarterAssetsInputs>();
        }

        private void Update()
        {
            var v = _cc.velocity;
            v.y = 0f;
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

        /// <summary>単発ノイズを発生させる（ロッカー開閉、ジャンプスケア絶叫など）</summary>
        public void EmitNoise(float radius)
        {
            GameEvents.RaiseNoiseEmitted(transform.position, radius);
        }

        /// <summary>HidingSpot から呼ばれる</summary>
        public void SetHidden(bool hidden, HidingSpot spot)
        {
            IsHidden = hidden;
            CurrentHidingSpot = hidden ? spot : null;
        }

        /// <summary>リスポーン時などの強制解除</summary>
        public void ForceExitHiding()
        {
            if (CurrentHidingSpot != null)
                CurrentHidingSpot.ForceExit();
            IsHidden = false;
            CurrentHidingSpot = null;
        }
    }
}
