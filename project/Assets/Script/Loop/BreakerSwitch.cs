using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 各部屋にあるブレイカー。BreakerSystemが降下対象を選び、プレイヤーが上げる。
    /// レバーの見た目（子Transform）が上下し、降下中はブザー音を鳴らす。
    /// </summary>
    public class BreakerSwitch : MonoBehaviour, IInteractable, IPromptProvider
    {
        public string RoomId;
        [Tooltip("レバーの見た目（上下に動かす）")]
        public Transform Lever;
        [Tooltip("警報の音量（チュートリアルは小さめに設定される）")]
        public float AlarmVolume = 1f;
        [Tooltip("警報の可聴距離")]
        public float AlarmMaxDistance = 60f;
        [Tooltip("trueなら降りた状態で開始する（チュートリアル：上げる操作を教える）")]
        public bool StartsDown;

        public bool IsUp { get; private set; }

        private AudioSource _alarm;
        private Vector3 _leverBase;   // レバーの基準位置（上下はここからの相対）
        private GameObject _warnLamp; // 降下中の赤い警告ランプ（見た目で判別できるように）
        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private void Awake()
        {
            if (Lever != null) _leverBase = Lever.localPosition;

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
            SetUp(!StartsDown, silent: true);
        }

        public void SetUp(bool up, bool silent = false)
        {
            IsUp = up;
            // 降下は大きく下げて一目で分かるように（+0.12 / -0.35）
            if (Lever != null)
                Lever.localPosition = _leverBase + Vector3.up * (up ? 0.12f : -0.35f);
            if (_warnLamp != null) _warnLamp.SetActive(!up);
            if (up) { if (_alarm != null && _alarm.isPlaying) _alarm.Stop(); }
            // 部屋モデルが非表示中はPlayできないため、表示時（OnEnable）にも再開する
            else if (_alarm != null && !silent && isActiveAndEnabled) _alarm.Play();
        }

        private void OnEnable()
        {
            // 部屋が表示された時、降下中なら鳴らし直す（非表示中はPlayできないため）
            if (!IsUp && _alarm != null && !_alarm.isPlaying) _alarm.Play();
            else if (IsUp && _alarm != null && _alarm.isPlaying) _alarm.Stop();
        }

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew || IsUp) return;

            SetUp(true);
            ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 1f);
            BreakerSystem.Instance?.NotifyRaised(RoomId);
        }

        public string GetPrompt() => IsUp ? "" : "[E] ブレイカーを上げる";
        public float GetProgress01() => -1f;
    }
}
