using UnityEngine;
using System.Collections;

namespace StarterAssets
{
    /// <summary>
    /// インタラクト可能なオブジェクト（トグル機能付き）
    /// ドア開閉など、Bool反転でアニメーションを切り替える
    /// </summary>
    public class InteractableObject : MonoBehaviour, IInteractable
    {
        /// <summary>
        /// インタラクトの状態
        /// </summary>
        public enum InteractionState
        {
            Idle,           // 待機中（インタラクト可能）
            Interacting,    // アニメーション再生中
            Interacted      // インタラクト完了
        }

        [Header("Interaction")]
        [SerializeField] private bool _canInteract = true;
        [SerializeField] private float _interactCooldown = 0.5f;

        [Header("Animation")]
        [SerializeField] private Animator _animator;
        [SerializeField] private string _triggerName = "Interact";
        [SerializeField] private string _boolResetName = "IsInteracting";  // Bool パラメータ（フラグ）
        [SerializeField] private string _toggleBoolName = "IsDoorOpen";  // ドア開閉制御用 Bool ← 追加
        [SerializeField] private bool _useBoolReset = true;  // Bool でリセット制御するか
        [SerializeField] private float _animationDuration = 1f;  // アニメーション継続時間

        [Header("Animation Detection")]
        [SerializeField] private AnimationDetectionMode _detectionMode = AnimationDetectionMode.TimerBased;
        [SerializeField] private bool _autoDetectAnimationLength = true;  // 自動検出

        [Header("Collision")]
        [SerializeField] private Collider[] _collidersToDisable;
        [SerializeField] private bool _disableColliderOnInteract = true;
        [SerializeField] private bool _restoreColliderOnComplete = false;  // アニメーション終了後に復元

        private float _lastInteractTime = -1f;
        private InteractionState _currentState = InteractionState.Idle;
        private Coroutine _resetCoroutine;
        private bool _isDoorOpen = false;  // ドアの開閉状態 ← 追加

        public enum AnimationDetectionMode
        {
            TimerBased,     // 時間ベース（_animationDuration を使用）
            AnimationEvent  // AnimationEvent で手動制御
        }

        public InteractionState CurrentState => _currentState;
        public bool IsInteracting => _currentState == InteractionState.Interacting;

        public bool CanInteract
        {
            get => _canInteract &&
                   _currentState == InteractionState.Idle &&
                   (Time.time - _lastInteractTime) > _interactCooldown;
        }

        private void OnValidate()
        {
            if (_animator == null)
                _animator = GetComponent<Animator>();

            // 自動検出が有効な場合
            if (_autoDetectAnimationLength && _animator != null)
            {
                try
                {
                    AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
                    if (stateInfo.length > 0)
                    {
                        _animationDuration = stateInfo.length;
                    }
                }
                catch
                {
                    // OnValidate 時は Animator が完全初期化されていない場合がある
                }
            }
        }

        private void Start()
        {
            // 初期状態を Idle に設定
            _currentState = InteractionState.Idle;
            _isDoorOpen = false;  // 初期状態は閉じている
            ResetAnimationState();
        }

        public void OnInteract()
        {
            if (!CanInteract) return;

            _lastInteractTime = Time.time;
            _currentState = InteractionState.Interacting;

            // Bool を反転 ← 追加：これでドアが開く/閉じるを切り替え
            _isDoorOpen = !_isDoorOpen;

            // アニメーション再生
            if (_animator != null)
            {
                _animator.SetTrigger(_triggerName);

                // Bool パラメータで開く/閉じるを切り替え ← 追加
                if (!string.IsNullOrEmpty(_toggleBoolName))
                {
                    _animator.SetBool(_toggleBoolName, _isDoorOpen);
                }

                // Bool パラメータを使用する場合は ON に
                if (_useBoolReset && !string.IsNullOrEmpty(_boolResetName))
                {
                    _animator.SetBool(_boolResetName, true);
                }
            }

            // コリジョン変更
            if (_disableColliderOnInteract && _collidersToDisable.Length > 0)
            {
                DisableColliders();
            }

            Debug.Log($"[InteractableObject] Interacted with {gameObject.name} - Door {(_isDoorOpen ? "Open" : "Close")} - State: Interacting");

            // アニメーション終了を待つ
            if (_detectionMode == AnimationDetectionMode.TimerBased)
            {
                // 時間ベースのリセット
                if (_resetCoroutine != null)
                    StopCoroutine(_resetCoroutine);

                _resetCoroutine = StartCoroutine(ResetAfterDelay(_animationDuration));
            }
            // AnimationEvent の場合は OnAnimationComplete() を呼び出してもらう
        }

        /// <summary>
        /// アニメーション終了後に呼び出される（AnimationEvent 用）
        /// Inspector → Animator → Animation Clip → Animation Event で設定
        /// </summary>
        public void OnAnimationComplete()
        {
            Debug.Log($"[InteractableObject] Animation completed for {gameObject.name}");
            ResetToIdle();
        }

        /// <summary>
        /// 指定時間後にリセット
        /// </summary>
        private IEnumerator ResetAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ResetToIdle();
        }

        /// <summary>
        /// Idle 状態にリセット（再インタラクト可能に）
        /// </summary>
        private void ResetToIdle()
        {
            if (_currentState == InteractionState.Idle) return;

            _currentState = InteractionState.Idle;
            ResetAnimationState();

            // コライダー復元
            if (_restoreColliderOnComplete && _collidersToDisable.Length > 0)
            {
                EnableColliders();
            }

            Debug.Log($"[InteractableObject] Reset to Idle: {gameObject.name} - Can interact again");
        }

        /// <summary>
        /// アニメーション状態をリセット
        /// </summary>
        private void ResetAnimationState()
        {
            if (_animator == null) return;

            // Bool パラメータをリセット
            if (_useBoolReset && !string.IsNullOrEmpty(_boolResetName))
            {
                _animator.SetBool(_boolResetName, false);
            }
        }

        /// <summary>
        /// コライダーを無効化
        /// </summary>
        public void DisableColliders()
        {
            if (_collidersToDisable.Length > 0)
            {
                foreach (var collider in _collidersToDisable)
                {
                    if (collider != null)
                        collider.enabled = false;
                }
                Debug.Log($"[InteractableObject] Colliders disabled: {gameObject.name}");
            }
        }

        /// <summary>
        /// コライダーを有効化
        /// </summary>
        public void EnableColliders()
        {
            if (_collidersToDisable.Length > 0)
            {
                foreach (var collider in _collidersToDisable)
                {
                    if (collider != null)
                        collider.enabled = true;
                }
                Debug.Log($"[InteractableObject] Colliders enabled: {gameObject.name}");
            }
        }

        /// <summary>
        /// 強制的にリセット（デバッグ用）
        /// </summary>
        public void ForceReset()
        {
            if (_resetCoroutine != null)
                StopCoroutine(_resetCoroutine);

            ResetToIdle();
            Debug.Log($"[InteractableObject] Force reset: {gameObject.name}");
        }

        /// <summary>
        /// ドアが開いているか取得
        /// </summary>
        public bool GetIsDoorOpen() => _isDoorOpen;
    }
}