using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// MCPからプレイヤーを歩かせて到達確認するためのドライバ。
    /// `EscapeProto.DebugPlayerDriver.Instance.WalkTo(x, z)` → 毎フレームCharacterControllerで
    /// 直進歩行（衝突判定あり＝壁抜けしない）。`Where()` で位置と状態を確認。
    /// ※クラス名とファイル名の一致が必要（シーン保存時のスクリプト解決）
    /// </summary>
    public class DebugPlayerDriver : MonoBehaviour
    {
        public static DebugPlayerDriver Instance { get; private set; }

        private CharacterController _cc;
        private FirstPersonController _fpc;
        private Vector3? _target;
        private Vector3 _checkPos;
        private float _checkTimer;

        /// <summary>idle / walking / arrived / stuck</summary>
        public string Status { get; private set; } = "idle";

        private void Awake()
        {
            Instance = this;
            _cc = GetComponent<CharacterController>();
            _fpc = GetComponent<FirstPersonController>();
        }

        public string WalkTo(float x, float z)
        {
            _target = new Vector3(x, transform.position.y, z);
            Status = "walking";
            _checkPos = transform.position;
            _checkTimer = 0f;
            if (_fpc != null) _fpc.enabled = false;   // 入力移動と競合しないよう一時停止
            return $"walking to ({x:0.0}, {z:0.0})";
        }

        public string Stop()
        {
            _target = null;
            Status = "idle";
            if (_fpc != null) _fpc.enabled = true;
            return "stopped";
        }

        public string Where() =>
            $"pos=({transform.position.x:0.00}, {transform.position.y:0.00}, {transform.position.z:0.00}) status={Status}";

        private void Update()
        {
            if (_target == null || _cc == null) return;

            Vector3 to = _target.Value - transform.position;
            to.y = 0f;
            if (to.magnitude < 0.35f)
            {
                _target = null;
                Status = "arrived";
                if (_fpc != null) _fpc.enabled = true;
                return;
            }

            Vector3 dir = to.normalized;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(dir), 540f * Time.deltaTime);
            _cc.Move((dir * 3.0f + Vector3.down * 9f) * Time.deltaTime);

            // 1.5秒ごとに進捗を確認し、ほぼ動けていなければ stuck
            _checkTimer += Time.deltaTime;
            if (_checkTimer >= 1.5f)
            {
                if ((transform.position - _checkPos).magnitude < 0.15f)
                {
                    _target = null;
                    Status = $"stuck at ({transform.position.x:0.0}, {transform.position.z:0.0})";
                    if (_fpc != null) _fpc.enabled = true;
                    return;
                }
                _checkPos = transform.position;
                _checkTimer = 0f;
            }
        }
    }
}
