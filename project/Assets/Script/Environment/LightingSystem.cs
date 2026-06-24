using UnityEngine;
using StarterAssets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// 部屋の電気スイッチ（インタラクトでON/OFF）
    /// 電気を消すと視覚型の探索者から見つかりにくくなる
    /// </summary>
    public class RoomLightController : MonoBehaviour, IInteractable, IPromptProvider
    {
        public static RoomLightController Instance { get; private set; }

        [Header("制御するライト群（空ならシーン内のPoint/Spotを自動収集）")]
        [SerializeField] private Light[] _roomLights;
        [SerializeField] private bool _lightsOn = true;

        public bool LightsOn => _lightsOn;

        private float _lastCallTime = -10f;
        private float[] _baseIntensities;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private void Awake() { Instance = this; }

        private void Start()
        {
            if (_roomLights == null || _roomLights.Length == 0)
            {
                var all = FindObjectsByType<Light>(FindObjectsSortMode.None);
                var list = new System.Collections.Generic.List<Light>();
                foreach (var l in all)
                    if (l.type == LightType.Point)   // 懐中電灯(Spot)や敵の眼光は対象外
                        list.Add(l);
                _roomLights = list.ToArray();
            }
            _baseIntensities = new float[_roomLights.Length];
            for (int i = 0; i < _roomLights.Length; i++)
                _baseIntensities[i] = _roomLights[i] != null ? _roomLights[i].intensity : 0f;
            Apply();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void OnInteract()
        {
            bool isNewPress = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNewPress) return;
            Toggle();
        }

        public void Toggle()
        {
            _lightsOn = !_lightsOn;
            Apply();
            GameEvents.RaiseRoomLightsChanged(_lightsOn);
            ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 1f);
        }

        private void Apply()
        {
            if (_roomLights == null) return;
            for (int i = 0; i < _roomLights.Length; i++)
            {
                if (_roomLights[i] == null) continue;
                _roomLights[i].intensity = _lightsOn ? _baseIntensities[i] : 0f;
            }
        }

        public string GetPrompt() => _lightsOn ? "[E] 電気を消す" : "[E] 電気を点ける";
        public float GetProgress01() => -1f;
    }

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
            if (GameManager.Instance != null && GameManager.Instance.IsGameEnded) return;
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) pressed = true;
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
