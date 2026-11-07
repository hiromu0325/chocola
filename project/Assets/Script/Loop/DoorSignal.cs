using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 回廊の扉の上の表示灯。部屋へ行けるようになった扉が暖かい色で灯り、扉の前を照らす。
    /// 灯り方で行き先が分かるようにする（右下の地図を見なくても、回廊を見渡すだけで次の扉が分かる）。
    /// ・施錠中／部屋の無い扉：消灯
    /// ・次に行く部屋（目標が向かわせている部屋）：強く灯る
    /// ・入れて、まだ用のある部屋：弱く灯る
    /// ・用の済んだ部屋：消灯（入ることはできる）
    /// ・回廊が表示されていない間に灯るようになった扉は、次に回廊へ出た時に瞬いてから灯る
    /// ・ブレイカーが落ちている間：警報の部屋の扉だけ赤く点滅し、ほかは全部消える
    /// ※クラス名とファイル名の一致が必須（シーン保存時のスクリプト解決）
    /// </summary>
    public class DoorSignal : MonoBehaviour
    {
        public string RoomId;
        public Renderer Lens;
        public Light Glow;
        public Material OffMaterial, OnMaterial, AlarmMaterial;

        private enum State { Unknown, Off, Dim, On, Alarm }

        /// <summary>まだ用のある部屋の扉の明るさ（次に行く部屋の扉に対して）</summary>
        private const float DimLevel = 0.28f;
        private Material _dimMaterial;

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
                    if ((s == State.On || s == State.Dim) && _state == State.Off) _flickerUntil = Time.time + 0.9f;
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
            if (!LoopRooms.IsUnlocked(RoomId)) return State.Off;
            var target = LoopObjective.TargetRoom();
            if (target != null && target.Id == RoomId) return State.On;
            return HasBusiness() ? State.Dim : State.Off;
        }

        /// <summary>まだ用のある部屋か（終章では、娘のおもちゃがまだ残っている部屋）</summary>
        private bool HasBusiness()
        {
            var room = LoopRooms.Get(RoomId);
            if (room == null) return true;
            var fin = LoopFinale.Instance;
            if (fin != null && fin.Started)
                return !fin.Completed && RoomId != "son_room" && !LoopProgress.IsFound(RoomId, "toy");
            return !LoopProgress.IsRoomComplete(room);
        }

        /// <summary>弱く灯る時のレンズ（点灯の材質の光り方だけを弱めた複製）</summary>
        private Material DimMaterial()
        {
            if (_dimMaterial != null || OnMaterial == null) return _dimMaterial != null ? _dimMaterial : OnMaterial;
            _dimMaterial = new Material(OnMaterial) { name = OnMaterial.name + "_Dim" };
            foreach (var prop in new[] { "_EmissionColor", "_EmissiveColor" })
                if (_dimMaterial.HasProperty(prop)) _dimMaterial.SetColor(prop, _dimMaterial.GetColor(prop) * DimLevel);
            return _dimMaterial;
        }

        private void OnDestroy()
        {
            if (_dimMaterial != null) Destroy(_dimMaterial);
        }

        private void Apply()
        {
            bool lit;
            switch (_state)
            {
                case State.On:
                case State.Dim:
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
            var m = !lit ? OffMaterial : _state == State.Alarm ? AlarmMaterial : _state == State.Dim ? DimMaterial() : OnMaterial;
            if (Lens != null && m != null && Lens.sharedMaterial != m) Lens.sharedMaterial = m;
            if (Glow != null)
            {
                if (_glowIntensity < 0f) _glowIntensity = Glow.intensity;
                if (Glow.enabled != lit) Glow.enabled = lit;
                if (lit)
                {
                    bool alarm = _state == State.Alarm;
                    Glow.color = alarm ? AlarmColor : OnColor;
                    Glow.intensity = _glowIntensity * (alarm ? 1.6f : _state == State.Dim ? DimLevel : 1f);
                }
            }
        }
    }
}
