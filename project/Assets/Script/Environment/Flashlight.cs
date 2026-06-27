using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// 懐中電灯：F キーでON/OFF。暗闇の探索には有効だが、
    /// 探索者の入室直後（無音の40秒間）に点けると目の前に顔が現れ即死する
    /// </summary>
    public class Flashlight : MonoBehaviour
    {
        [SerializeField] private float _range = 12f;
        [SerializeField] private float _angle = 45f;

        private Light _spot;
        private bool _on;

        private void Start()
        {
            var go = new GameObject("FlashlightSpot");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            _spot = go.AddComponent<Light>();
            _spot.type = LightType.Spot;
            _spot.range = _range;
            _spot.spotAngle = _angle;
            _spot.intensity = 3.5f;
            _spot.color = new Color(1f, 0.97f, 0.85f);
            _spot.enabled = false;
        }

        private void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) pressed = true;
            var gp = Gamepad.current;
            if (gp != null && gp.buttonNorth.wasPressedThisFrame) pressed = true;  // Y / △
#else
            if (Input.GetKeyDown(KeyCode.F)) pressed = true;
#endif
            if (pressed) SetOn(!_on);
        }

        public void SetOn(bool on)
        {
            _on = on;
            if (_spot != null) _spot.enabled = on;
            GameEvents.RaiseFlashlightChanged(on);
            ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 0.6f);
        }
    }
}
