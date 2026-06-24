using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 香水：インタラクトで使用し、一定時間プレイヤーの臭いを消す（嗅覚型対策）
    /// 中央の机に置かれている
    /// </summary>
    public class PerfumeItem : MonoBehaviour, IInteractable, IPromptProvider
    {
        [Header("消臭の持続時間（秒）")]
        [SerializeField] private float _maskDuration = 40f;
        [Tooltip("再使用までのクールダウン")]
        [SerializeField] private float _cooldown = 3f;

        private float _lastCallTime = -10f;
        private float _lastUseTime = -100f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        public void OnInteract()
        {
            bool isNewPress = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNewPress) return;
            if (Time.time - _lastUseTime < _cooldown) return;

            _lastUseTime = Time.time;
            GameEvents.RaisePerfumeUsed(_maskDuration);
            ProceduralAudio.PlayAt(ProceduralAudio.Beep(), transform.position, 0.5f);
        }

        public string GetPrompt()
        {
            float remain = _maskDuration - (Time.time - _lastUseTime);
            if (remain > 0f)
                return $"香水（消臭中 残り{Mathf.CeilToInt(remain)}秒）";
            return "[E] 香水を使う（臭いを消す）";
        }

        public float GetProgress01() => -1f;
    }
}
