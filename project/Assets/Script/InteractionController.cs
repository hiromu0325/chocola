using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
    /// <summary>
    /// インタラクション制御
    /// Raycastで対象を検出し、ボタン入力でインタラクト実行
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private Camera _camera;
        [SerializeField] private float _interactDistance = 3f;
        [SerializeField] private LayerMask _interactLayer;

        [Header("Input")]
        [SerializeField] private string _interactActionName = "Interact";

        private IInteractable _currentInteractable;
        private bool _interactInput;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
        private InputAction _interactAction;
#endif

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;

#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
            if (_playerInput != null)
            {
                _interactAction = _playerInput.actions[_interactActionName];
            }
#endif
        }

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
#if ENABLE_INPUT_SYSTEM
            if (_interactAction != null)
            {
                _interactInput = _interactAction.IsPressed();
            }
#else
            _interactInput = Input.GetKeyDown(KeyCode.E);
#endif

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
