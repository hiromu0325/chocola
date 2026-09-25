using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 回廊の扉の上の表示灯。部屋へ行けるようになった扉だけが暖かい色で灯り、扉の前を照らす。
    /// ・施錠中／部屋の無い扉：消灯
    /// ・入れる：点灯（回廊が表示されていない間に入れるようになった扉は、次に回廊へ出た時に瞬いてから灯る）
    /// ・ブレイカーが落ちている間：警報の部屋の扉だけ赤く点滅し、ほかは全部消える
    /// ※クラス名とファイル名の一致が必須（シーン保存時のスクリプト解決）
    /// </summary>
    public class DoorSignal : MonoBehaviour
    {
        public string RoomId;
        public Renderer Lens;
        public Light Glow;
        public Material OffMaterial, OnMaterial, AlarmMaterial;

        private enum State { Unknown, Off, On, Alarm }

        private static readonly Color OnColor = new Color(1f, 0.78f, 0.5f);
        private static readonly Color AlarmColor = new Color(1f, 0.15f, 0.1f);

        private State _state = State.Unknown;
        private float _next, _flickerUntil, _seed;
        private float _glowIntensity = -1f;

        public bool IsLit { get; private set; }

        private void Awake() => _seed = Random.value * 10f;

        private void OnEnable() => _next = 0f;

        private void Update()
        {
            if (Time.time >= _next)
            {
                _next = Time.time + 0.2f;
                var s = Evaluate();
                if (s != _state)
                {
                    // 見ていない間に解錠された扉は、回廊に出た時に瞬いてから灯る（最初の表示は瞬かない）
                    if (s == State.On && _state == State.Off) _flickerUntil = Time.time + 0.9f;
                    _state = s;
                }
            }
            Apply();
        }

        private State Evaluate()
        {
            if (string.IsNullOrEmpty(RoomId)) return State.Off;
            var bs = BreakerSystem.Instance;
            if (bs != null && bs.DownRoomId != null) return RoomId == bs.DownRoomId ? State.Alarm : State.Off;
            return LoopRooms.IsUnlocked(RoomId) ? State.On : State.Off;
        }

        private void Apply()
        {
            bool lit;
            switch (_state)
            {
                case State.On:
                    lit = Time.time >= _flickerUntil || Mathf.PerlinNoise(Time.time * 16f, _seed) > 0.5f;
                    break;
                case State.Alarm:
                    lit = Mathf.Repeat(Time.time, 0.8f) < 0.45f;
                    break;
                default:
                    lit = false;
                    break;
            }
            IsLit = lit;
            var m = !lit ? OffMaterial : (_state == State.Alarm ? AlarmMaterial : OnMaterial);
            if (Lens != null && m != null && Lens.sharedMaterial != m) Lens.sharedMaterial = m;
            if (Glow != null)
            {
                if (_glowIntensity < 0f) _glowIntensity = Glow.intensity;
                if (Glow.enabled != lit) Glow.enabled = lit;
                if (lit)
                {
                    bool alarm = _state == State.Alarm;
                    Glow.color = alarm ? AlarmColor : OnColor;
                    Glow.intensity = _glowIntensity * (alarm ? 1.6f : 1f);
                }
            }
        }
    }
}
