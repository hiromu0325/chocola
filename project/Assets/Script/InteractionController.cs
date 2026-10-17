using UnityEngine;

namespace StarterAssets
{
    /// <summary>
    /// インタラクション制御
    /// Raycastで対象を検出し、ボタン入力でインタラクト実行
    /// 入力は StarterAssetsInputs（キーボードE/ゲームパッドX）経由で取得
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private Camera _camera;
        [SerializeField] private float _interactDistance = 3f;
        [SerializeField] private LayerMask _interactLayer;

        private IInteractable _currentInteractable;
        private bool _interactInput;
        private StarterAssetsInputs _inputs;
        private bool _waitRelease = true;   // 一度離すまで受け付けない（資料などを閉じた E がそのまま「調べる」にならないように）

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;
            _inputs = GetComponent<StarterAssetsInputs>();
        }

        // 操作が戻った時（資料・手帳・装置の画面を閉じた時）は、押しっぱなしの E をいったん離すまで受け付けない
        private void OnEnable() => _waitRelease = true;

        private void Update()
        {
            DetectInteractable();
            HandleInteractInput();
        }

        private void DetectInteractable()
        {
            IInteractable newInteractable = null;
            Vector3 origin = _camera.transform.position;
            Vector3 dir = _camera.transform.forward;

            // ① 中心レイ（精密）：当たったコライダー（または親）の IInteractable
            if (Physics.Raycast(origin, dir, out RaycastHit hit, _interactDistance, _interactLayer,
                    QueryTriggerInteraction.Collide))
            {
                newInteractable = hit.collider.GetComponentInParent<IInteractable>();
            }

            // ② 外れた／非インタラクト物だった場合は少し太い球判定で補助（小物・狙いのズレに強く）
            if (newInteractable == null &&
                Physics.SphereCast(origin, 0.2f, dir, out RaycastHit shit, _interactDistance, _interactLayer,
                    QueryTriggerInteraction.Collide))
            {
                newInteractable = shit.collider.GetComponentInParent<IInteractable>();
            }

            if (newInteractable != _currentInteractable)
            {
                _currentInteractable = newInteractable;
                OnInteractableChanged();
            }
        }

        private void HandleInteractInput()
        {
            _interactInput = _inputs != null && _inputs.interact;
            // 画面が開いている間・閉じたフレームに押されていた E は、離すまで次の「調べる」に使わない
            if (EscapeProto.InspectView.IsOpen || EscapeProto.InspectView.ClosedThisFrame || EscapeProto.NotebookUI.IsOpen ||
                (EscapeProto.PuzzleUI.Instance != null && EscapeProto.PuzzleUI.Instance.IsOpen) ||
                (EscapeProto.LoopPuzzleUI.Instance != null && EscapeProto.LoopPuzzleUI.Instance.IsOpen))
            {
                if (_interactInput) _waitRelease = true;
                return;
            }
            if (_waitRelease)
            {
                if (!_interactInput) _waitRelease = false;
                return;
            }

            if (_interactInput && _currentInteractable != null && _currentInteractable.CanInteract)
            {
                _currentInteractable.OnInteract();
            }
        }

        private void OnInteractableChanged()
        {
            if (_currentInteractable != null)
            {
                Debug.Log($"[InteractionController] Interactable detected: {_currentInteractable}");
            }
        }

        public IInteractable GetCurrentInteractable()
        {
            return _currentInteractable;
        }
    }
}
