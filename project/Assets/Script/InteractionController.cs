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

        /// <summary>今の対象を保つ球の太さ（選ぶ時の 0.2 より少し太く）</summary>
        private const float KeepRadius = 0.32f;
        /// <summary>狙いから外れても、この秒数は今の対象を保つ（頭の揺れで案内がちらつかないように）</summary>
        private const float LoseGrace = 0.12f;

        private IInteractable _currentInteractable;
        private float _lostAt = -1f;
        private static readonly RaycastHit[] KeepHits = new RaycastHit[16];
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
            IInteractable precise = null, loose = null;
            Vector3 origin = _camera.transform.position;
            Vector3 dir = _camera.transform.forward;

            // ① 中心レイ（精密）：当たったコライダー（または親）の IInteractable
            bool centerHit = Physics.Raycast(origin, dir, out RaycastHit hit, _interactDistance, _interactLayer,
                QueryTriggerInteraction.Collide);
            if (centerHit)
                precise = hit.collider.GetComponentInParent<IInteractable>();

            // ② 外れた／非インタラクト物だった場合は少し太い球判定で補助（小物・狙いのズレに強く）
            if (precise == null &&
                Physics.SphereCast(origin, 0.2f, dir, out RaycastHit shit, _interactDistance, _interactLayer,
                    QueryTriggerInteraction.Collide))
            {
                loose = shit.collider.GetComponentInParent<IInteractable>();
            }

            // 選び直しでちらつかないように：真ん中で狙った物にはすぐ切り替えるが、球の判定だけで隣の物が掛かった時は、
            // 今の物がまだ狙いの近くにあれば保つ。狙いから外れても少しの間は保つ
            IInteractable newInteractable;
            bool alive = IsAlive(_currentInteractable);
            if (precise != null) newInteractable = precise;
            else if (alive && StillNear(_currentInteractable, origin, dir, centerHit ? hit.distance : _interactDistance))
                newInteractable = _currentInteractable;
            else newInteractable = loose;

            if (newInteractable == null && alive)
            {
                if (_lostAt < 0f) _lostAt = Time.unscaledTime;
                if (Time.unscaledTime - _lostAt < LoseGrace) newInteractable = _currentInteractable;
            }
            else _lostAt = -1f;

            if (newInteractable != _currentInteractable)
            {
                _currentInteractable = newInteractable;
                OnInteractableChanged();
            }
        }

        /// <summary>まだ調べられる状態で場にあるか（拾って消えた・隠れた物は保たない）</summary>
        private static bool IsAlive(IInteractable it) =>
            it is Component c && c != null && c.gameObject.activeInHierarchy;

        /// <summary>今の対象が狙いの少し太い球に掛かっていて、真ん中で見ている物より奥（壁の向こう）ではないか</summary>
        private bool StillNear(IInteractable it, Vector3 origin, Vector3 dir, float centerDistance)
        {
            int n = Physics.SphereCastNonAlloc(origin, KeepRadius, dir, KeepHits, _interactDistance, _interactLayer,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var h = KeepHits[i];
                if (h.distance > centerDistance + 0.25f) continue;
                if (ReferenceEquals(h.collider.GetComponentInParent<IInteractable>(), it)) return true;
            }
            return false;
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
