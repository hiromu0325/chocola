using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 脱出ドア：全ギミック解除後にインタラクトでゲームクリア
    /// </summary>
    public class ExitDoor : MonoBehaviour, IInteractable, IPromptProvider
    {
        [SerializeField] private Renderer _statusRenderer;

        // InteractionController は押下中毎フレーム OnInteract を呼ぶため、
        // 呼び出し間隔が空いた時のみ「新規押下」とみなす
        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private static bool PowerOn => PuzzleState.Instance != null && PuzzleState.Instance.PowerRestored;

        public void OnInteract()
        {
            bool isNewPress = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNewPress) return;

            if (PowerOn)
            {
                GameManager.Instance?.NotifyEscaped();
            }
            else
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 1f);
            }
        }

        private void Update()
        {
            if (_statusRenderer == null) _statusRenderer = GetComponentInChildren<Renderer>();
            if (_statusRenderer != null)
                _statusRenderer.material.color = PowerOn
                    ? new Color(0.2f, 0.9f, 0.3f)
                    : new Color(0.35f, 0.25f, 0.2f);
        }

        public string GetPrompt() => PowerOn
            ? "[E] 脱出する！"
            : "脱出ドア（電子錠：電力復旧が必要）";

        public float GetProgress01() => -1f;
    }
}
