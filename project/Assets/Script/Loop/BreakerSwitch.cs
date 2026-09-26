using System.Collections;
using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 各部屋にあるブレイカー。BreakerSystemが降下対象を選び、プレイヤーが上げる。
    /// レバーは軸で回り、上げる（入）と斜め上、落ちる（切）と斜め下を向く。降下中はブザー音を鳴らす。
    /// </summary>
    public class BreakerSwitch : MonoBehaviour, IInteractable, IPromptProvider
    {
        public string RoomId;
        [Tooltip("レバー（回転の軸に置いた親。Rotates=false の時は上下に動かす）")]
        public Transform Lever;
        [Tooltip("レバーを軸で回す（上げる = UpAngle、落ちる = DownAngle。ユニットの Z 軸まわり）")]
        public bool Rotates;
        public float UpAngle = 35f;
        public float DownAngle = 145f;
        [Tooltip("警報の音量（チュートリアルは小さめに設定される）")]
        public float AlarmVolume = 1f;
        [Tooltip("警報の可聴距離")]
        public float AlarmMaxDistance = 60f;
        [Tooltip("trueなら降りた状態で開始する（チュートリアル：上げる操作を教える）")]
        public bool StartsDown;

        public bool IsUp { get; private set; }

        private AudioSource _alarm;
        private Vector3 _leverBase;   // レバーの基準位置（上下はここからの相対）
        private Quaternion _leverRot0 = Quaternion.identity;
        private bool _captured;
        private float _angle = float.NaN;
        private Coroutine _swing;
        private GameObject _warnLamp; // 降下中の赤い警告ランプ（見た目で判別できるように）
        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private void Awake()
        {
            Capture();

            // 降下中の警報音（3D・ループ）
            _alarm = gameObject.AddComponent<AudioSource>();
            _alarm.clip = ProceduralAudio.Alarm();
            _alarm.loop = true;
            // 既定でONのため、部屋がアクティブになる瞬間に一瞬鳴ってしまう。必ず切る
            _alarm.playOnAwake = false;
            _alarm.spatialBlend = 1f;
            _alarm.volume = AlarmVolume;
            _alarm.maxDistance = AlarmMaxDistance;
            _alarm.rolloffMode = AudioRolloffMode.Linear;

            // 降下中だけ光る赤ランプ（レバー位置だけでは判別しづらいため）
            _warnLamp = new GameObject("WarnLamp");
            _warnLamp.transform.SetParent(transform, false);
            _warnLamp.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            var l = _warnLamp.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.15f, 0.1f);
            l.intensity = 1.6f;
            l.range = 4f;
            _warnLamp.SetActive(false);

            // 最初の部屋のブレイカーは、必要な資料を見つけるまで静かなまま（LoopProgressが降ろす）
            SetUp(!StartsDown, silent: true, animate: false);
        }

        /// <summary>上げる／落とす。animate=true なら表示中はレバーが倒れ込む（上げる 0.25秒、落ちる 0.12秒）</summary>
        public void SetUp(bool up, bool silent = false, bool animate = true)
        {
            IsUp = up;
            if (Lever != null)
            {
                if (Rotates)
                {
                    float target = up ? UpAngle : DownAngle;
                    if (_swing != null) { StopCoroutine(_swing); _swing = null; }
                    if (animate && isActiveAndEnabled && !float.IsNaN(_angle))
                        _swing = StartCoroutine(Swing(target, up ? 0.25f : 0.12f));
                    else SetAngle(target);
                }
                // 旧来の見た目：降下は大きく下げて一目で分かるように（+0.12 / -0.35）
                else { Capture(); Lever.localPosition = _leverBase + Vector3.up * (up ? 0.12f : -0.35f); }
            }
            if (_warnLamp != null) _warnLamp.SetActive(!up);
            if (up) { if (_alarm != null && _alarm.isPlaying) _alarm.Stop(); }
            // 部屋モデルが非表示中はPlayできないため、表示時（OnEnable）にも再開する
            else if (_alarm != null && !silent && isActiveAndEnabled) PlayAlarm();
        }

        private void OnEnable()
        {
            // 部屋が表示された時、降下中なら鳴らし直す（非表示中はPlayできないため）
            if (!IsUp && _alarm != null && !_alarm.isPlaying) PlayAlarm();
            else if (IsUp && _alarm != null && _alarm.isPlaying) _alarm.Stop();
        }

        /// <summary>警報を鳴らす（恐怖演出の軽減中は音量を下げる）</summary>
        private void PlayAlarm()
        {
            _alarm.volume = AlarmVolume * HorrorSettings.Volume;
            _alarm.Play();
        }

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew || IsUp)
            {
                if (IsUp) AttackDebugLog.Log("breaker", $"操作: {RoomId} は既に上がっている（何もしない）");
                return;
            }

            AttackDebugLog.Log("breaker", $"操作: {RoomId} を上げる");
            SetUp(true);
            ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 1f);
            BreakerSystem.Instance?.NotifyRaised(RoomId);
        }

        /// <summary>レバーの置かれた姿勢（回転・上下の基準）を覚える</summary>
        private void Capture()
        {
            if (_captured || Lever == null) return;
            _leverBase = Lever.localPosition;
            _leverRot0 = Lever.localRotation;
            _captured = true;
        }

        private void SetAngle(float a)
        {
            Capture();
            _angle = a;
            Lever.localRotation = _leverRot0 * Quaternion.Euler(0f, 0f, a);
        }

        private IEnumerator Swing(float target, float seconds)
        {
            float from = _angle, t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / seconds);
                SetAngle(Mathf.Lerp(from, target, k * k));   // 最後に勢いよく倒れ込む
                yield return null;
            }
            SetAngle(target);
            ProceduralAudio.PlayAt(ProceduralAudio.DoorShut(), Lever.position, 0.45f);   // ガチャン
            _swing = null;
        }

        public string GetPrompt() => IsUp ? "" : "[E] ブレイカーを上げる";
        public float GetProgress01() => -1f;
    }
}
