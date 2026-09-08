using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 専用のセーブPC（記録端末）。最初の部屋に置かれ、調べると手動セーブする。
    /// 終章で断片が揃った後は「[E]長押しでアップロード」に変わる（LoopFinale）。
    /// </summary>
    public class SavePoint : MonoBehaviour, IInteractable, IPromptProvider
    {
        private float _lastCallTime = -10f;
        private float _lastHoldTime = -10f;
        private float _clickTimer;
        private AudioSource _audio;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private static bool UploadMode =>
            LoopFinale.Instance != null && LoopFinale.Instance.AllToysCollected && !LoopFinale.Instance.Completed;

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f; _audio.maxDistance = 12f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
        }

        public void OnInteract()
        {
            // 終章：押している間だけアップロードが進む（毎フレーム呼ばれる）
            if (UploadMode) { _lastHoldTime = Time.time; return; }

            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;

            if (GameManager.Instance != null) GameManager.Instance.SaveNow();
            ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 0.7f);

            if (PuzzleUI.Instance != null && !PuzzleUI.Instance.IsOpen && !PuzzleUI.Instance.BlockReopen)
                PuzzleUI.Instance.ShowDocument(
                    "記録端末",
                    "ここまでの記録を残した。\n\n" +
                    "異形に捕まった時は、この部屋で目を覚ます。\n" +
                    "棚の人形が、身代わりになってくれる限りは──");
        }

        private void Update()
        {
            if (!UploadMode) return;
            bool holding = Time.time - _lastHoldTime < 0.15f;
            if (!holding) return;
            bool progressed = LoopFinale.Instance.UploadTick(Time.deltaTime);
            _clickTimer -= Time.deltaTime;
            if (_clickTimer <= 0f)
            {
                _audio.PlayOneShot(progressed ? ProceduralAudio.Click() : ProceduralAudio.Beep(), progressed ? 0.5f : 0.35f);
                _clickTimer = progressed ? 0.4f : 1.0f;
            }
        }

        public string GetPrompt()
        {
            if (UploadMode)
            {
                var f = LoopFinale.Instance;
                var bs = BreakerSystem.Instance;
                if (bs != null && bs.DownRoomId != null) return "記録端末（電源断──ブレイカーを復旧するまで進まない）";
                return f.Uploading
                    ? $"[E] 長押しでアップロード（{Mathf.RoundToInt(f.UploadProgress * 100f)}%）"
                    : "[E] 長押しで断片をアップロードする";
            }
            return "[E] 記録する（セーブ）";
        }

        public float GetProgress01() =>
            UploadMode && LoopFinale.Instance.Uploading ? LoopFinale.Instance.UploadProgress : -1f;
    }
}
