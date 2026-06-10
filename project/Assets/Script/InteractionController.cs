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
            RaycastHit hit;
            IInteractable newInteractable = null;

            if (Physics.Raycast(_camera.transform.position, _camera.transform.forward, out hit, _interactDistance, _interactLayer))
            {
                newInteractable = hit.collider.GetComponent<IInteractable>();
            }

            // インタラクト対象が変わった場合
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
