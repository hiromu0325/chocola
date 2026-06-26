using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 隠れスポット（ロッカー等）
    /// ・インタラクトで中に入る/出る
    /// ・中にいる間：視覚型/動体型から不可視、移動不可（ノイズ0）
    /// ・出入り時に「ガチャッ」という単発ノイズ → 聴覚型はこれに反応する
    ///   （敵タイプに合わせて隠れ方を変える要素）
    /// </summary>
    public class HidingSpot : MonoBehaviour, IInteractable, IPromptProvider
    {
        [Header("配置")]
        [Tooltip("隠れ中のプレイヤー位置")]
        [SerializeField] private Transform _insideAnchor;
        [Tooltip("出た時のプレイヤー位置")]
        [SerializeField] private Transform _exitAnchor;

        [Header("出入りの音（聴覚型に聞こえる半径）")]
        [SerializeField] private float _doorNoiseRadius = 9f;

        public bool IsOccupied { get; private set; }

        // 押下中は毎フレーム OnInteract が呼ばれるため、
        // 呼び出しが途切れてから再度呼ばれた時のみ「新規押下」と判定する
        private float _lastCallTime = -10f;
        private PlayerStatus _player;
        private MonoBehaviour _fpController; // FirstPersonController（移動ロック用）

        public bool CanInteract =>
            GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private void Start()
        {
            _player = FindFirstObjectByType<PlayerStatus>();
            if (_player != null)
                _fpController = _player.GetComponent<FirstPersonController>();
        }

        public void OnInteract()
        {
            bool isNewPress = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNewPress) return;

            if (!CanInteract || _player == null) return;
            // 他のスポットに入っている間はこのスポットを操作できない
            if (_player.IsHidden && _player.CurrentHidingSpot != this) return;

            if (IsOccupied) Exit();
            else Enter();
        }

        private void Enter()
        {
            IsOccupied = true;
            TeleportPlayer(_insideAnchor != null ? _insideAnchor : transform);
            SetMovementLocked(true);
            _player.SetHidden(true, this);
            _player.EmitNoise(_doorNoiseRadius);   // 扉の音！聴覚型はこれを聞いている
            ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 1f);
        }

        private void Exit()
        {
            IsOccupied = false;
            TeleportPlayer(_exitAnchor != null ? _exitAnchor : transform);
            SetMovementLocked(false);
            _player.SetHidden(false, null);
            _player.EmitNoise(_doorNoiseRadius);
            ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 1f);
        }

        /// <summary>リスポーン時などの強制退出（音なし）</summary>
        public void ForceExit()
        {
            if (!IsOccupied) return;
            IsOccupied = false;
            SetMovementLocked(false);
        }

        private void TeleportPlayer(Transform anchor)
        {
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _player.transform.position = anchor.position;
            if (cc != null) cc.enabled = true;
        }

        private void SetMovementLocked(bool locked)
        {
            // 視点操作は残し、移動のみロック（FPC無効化＋CC無効化）
            if (_fpController != null) _fpController.enabled = !locked;
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = !locked;

            // FPC無効だと視点も止まるため、隠れ中用の簡易ルックを切替
            var look = _player.GetComponent<HiddenLook>();
            if (locked)
            {
                if (look == null) look = _player.gameObject.AddComponent<HiddenLook>();
                look.enabled = true;
            }
            else if (look != null)
            {
                look.enabled = false;
            }
        }

        public string GetPrompt() => IsOccupied ? "[E] ロッカーから出る" : "[E] ロッカーに隠れる（扉の音に注意）";
        public float GetProgress01() => -1f;
    }
}
