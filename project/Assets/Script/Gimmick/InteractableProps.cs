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

        public void OnInteract()
        {
            bool isNewPress = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNewPress) return;

            if (GimmickBase.AllSolved)
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
                _statusRenderer.material.color = GimmickBase.AllSolved
                    ? new Color(0.2f, 0.9f, 0.3f)
                    : new Color(0.35f, 0.25f, 0.2f);
        }

        public string GetPrompt() => GimmickBase.AllSolved
            ? "[E] 脱出する！"
            : $"脱出ドア（施錠中：ギミック {GimmickBase.SolvedCount}/{GimmickBase.TotalCount}）";

        public float GetProgress01() => -1f;
    }

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

    /// <summary>隠れ中の簡易視点操作（FirstPersonController停止中の代替）</summary>
    public class HiddenLook : MonoBehaviour
    {
        private StarterAssetsInputs _inputs;
        private Transform _cameraRoot;
        private float _pitch;

        private void Awake()
        {
            _inputs = GetComponent<StarterAssetsInputs>();
            var fpc = GetComponent<FirstPersonController>();
            if (fpc != null && fpc.CinemachineCameraTarget != null)
                _cameraRoot = fpc.CinemachineCameraTarget.transform;
        }

        private void LateUpdate()
        {
            if (_inputs == null || _cameraRoot == null) return;
            if (_inputs.look.sqrMagnitude < 0.01f) return;

            _pitch += _inputs.look.y * 1.0f;
            _pitch = Mathf.Clamp(_pitch, -80f, 80f);
            transform.Rotate(Vector3.up * _inputs.look.x * 1.0f);
            _cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
