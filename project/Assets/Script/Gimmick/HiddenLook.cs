using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>隠れ中の簡易視点操作（FirstPersonController停止中の代替）</summary>
    public class HiddenLook : MonoBehaviour
    {
        private StarterAssetsInputs _inputs;
        private Transform _cameraRoot;
        private float _pitch;

        private void Awake()
        {
            _inputs = GetComponent<StarterAssetsInputs>();
            var fpc = GetComponent<FirstPersonController>();
            if (fpc != null && fpc.CinemachineCameraTarget != null)
                _cameraRoot = fpc.CinemachineCameraTarget.transform;
        }

        private void LateUpdate()
        {
            if (_inputs == null || _cameraRoot == null) return;
            if (_inputs.look.sqrMagnitude < 0.01f) return;

            _pitch += _inputs.look.y * 1.0f;
            _pitch = Mathf.Clamp(_pitch, -80f, 80f);
            transform.Rotate(Vector3.up * _inputs.look.x * 1.0f);
            _cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
