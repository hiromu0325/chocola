using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// 懐中電灯：F キーでON/OFF。暗闇の探索には有効だが、
    /// 探索者の入室直後（無音の40秒間）に点けると目の前に顔が現れ即死する。
    /// ループ回廊では、最初の部屋の机で拾うまで点けられない（LoopProgress.FlashlightOwned）
    /// </summary>
    public class Flashlight : MonoBehaviour
    {
        [SerializeField] private float _range = 16f;
        [SerializeField] private float _angle = 50f;
        [SerializeField] private float _intensity = 8f;

        private Light _spot;
        private bool _on;

        /// <summary>
        /// 使えるか。ループ回廊（最初の部屋が登録されているシーン）では拾ってから。
        /// 施設マップなど回廊の無いシーンでは最初から使える
        /// </summary>
        private static bool Owned => LoopRooms.Get(LoopProgress.StartRoomId) == null || LoopProgress.FlashlightOwned;

        private void Start()
        {
            var go = new GameObject("FlashlightSpot");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            _spot = go.AddComponent<Light>();
            _spot.type = LightType.Spot;
            _spot.range = _range;
            _spot.spotAngle = _angle;
            _spot.innerSpotAngle = _angle * 0.55f;   // 中心は明るく、縁はやわらかく落ちる
            _spot.intensity = _intensity;
            _spot.color = new Color(1f, 0.97f, 0.85f);
            _spot.enabled = false;
        }

        private void Update()
        {
            // 持っていないのに点いている（はじめからやり直した時など）なら消す
            if (_on && !Owned) { _on = false; if (_spot != null) _spot.enabled = false; GameEvents.RaiseFlashlightChanged(false); }
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
            if (!pressed) return;
            if (!Owned)
            {
                ToastUI.Show(GameText.Get(GameText.UiKey("no_flashlight"), "懐中電灯を持っていない（最初の部屋の机にあったはず）"));
                return;
            }
            SetOn(!_on);
        }

        public void SetOn(bool on)
        {
            if (on && !Owned) return;   // 拾う前は点かない（襲撃などで自動で点ける時も）
            _on = on;
            if (_spot != null) _spot.enabled = on;
            GameEvents.RaiseFlashlightChanged(on);
            ProceduralAudio.PlayAt(ProceduralAudio.Switch(), transform.position, 0.5f);
        }
    }
}
