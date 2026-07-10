using UnityEngine;
using StarterAssets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// しゃがみ／伏せ。C または 左Ctrl でしゃがみ、Z で伏せ（同キー再押下で解除）。
    /// CharacterController の高さ・半径とカメラ高さ・移動速度を姿勢に応じて切り替える。
    /// 頭上が塞がっている場合は立ち上がれない。
    /// </summary>
    public class CrouchController : MonoBehaviour
    {
        public enum Stance { Stand, Crouch, Prone }
        public Stance Current { get; private set; } = Stance.Stand;

        private const float CrouchHeight = 1.0f, ProneHeight = 0.5f;
        private const float CrouchCamY = 0.75f, ProneCamY = 0.32f;
        private const float CrouchSpeedMul = 0.55f, ProneSpeedMul = 0.3f;
        private const float ProneRadius = 0.24f;

        private CharacterController _cc;
        private FirstPersonController _fpc;
        private Transform _camRoot;
        private float _standHeight, _standRadius, _standCamY, _baseMove, _baseSprint;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _fpc = GetComponent<FirstPersonController>();
            _camRoot = transform.Find("PlayerCameraRoot");
            _standHeight = _cc.height;
            _standRadius = _cc.radius;
            _standCamY = _camRoot != null ? _camRoot.localPosition.y : 1.55f;
            if (_fpc != null) { _baseMove = _fpc.MoveSpeed; _baseSprint = _fpc.SprintSpeed; }
        }

        private void Update()
        {
            bool crouchKey = false, proneKey = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                crouchKey = kb.leftCtrlKey.wasPressedThisFrame || kb.cKey.wasPressedThisFrame;
                proneKey = kb.zKey.wasPressedThisFrame;
            }
#else
            crouchKey = Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.C);
            proneKey = Input.GetKeyDown(KeyCode.Z);
#endif
            if (crouchKey) TrySet(Current == Stance.Crouch ? Stance.Stand : Stance.Crouch);
            else if (proneKey) TrySet(Current == Stance.Prone ? Stance.Stand : Stance.Prone);

            // カメラ高さは滑らかに追従
            if (_camRoot != null)
            {
                float target = Current == Stance.Stand ? _standCamY
                             : Current == Stance.Crouch ? CrouchCamY : ProneCamY;
                var lp = _camRoot.localPosition;
                lp.y = Mathf.MoveTowards(lp.y, target, 6f * Time.deltaTime);
                _camRoot.localPosition = lp;
            }
        }

        private void TrySet(Stance next)
        {
            // 体を起こす方向の変更は、頭上に障害物が無いか確認
            float nextHeight = next == Stance.Stand ? _standHeight
                             : next == Stance.Crouch ? CrouchHeight : ProneHeight;
            float curHeight = _cc.height;
            if (nextHeight > curHeight)
            {
                Vector3 origin = transform.position + Vector3.up * (curHeight * 0.5f);
                float castUp = nextHeight - curHeight * 0.5f;
                if (Physics.SphereCast(origin, _standRadius * 0.9f, Vector3.up, out _,
                        castUp, ~0, QueryTriggerInteraction.Ignore))
                    return;   // 立てない
            }

            Current = next;
            _cc.height = nextHeight;
            _cc.radius = next == Stance.Prone ? ProneRadius : _standRadius;
            _cc.center = new Vector3(0f, nextHeight * 0.5f + 0.03f, 0f);

            if (_fpc != null)
            {
                float mul = next == Stance.Stand ? 1f
                          : next == Stance.Crouch ? CrouchSpeedMul : ProneSpeedMul;
                _fpc.MoveSpeed = _baseMove * mul;
                _fpc.SprintSpeed = next == Stance.Stand ? _baseSprint : _baseMove * mul;
            }
        }
    }
}
