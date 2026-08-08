using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 配電室の施錠ドア：配電室の鍵（HasPowerRoomKey）を持っていればインタラクトで解錠。
    /// 解錠で床下へスライドして通行可能になる。
    /// </summary>
    public class KeyedDoor : MonoBehaviour, IInteractable, IPromptProvider
    {
        [SerializeField] private Transform _door;
        [SerializeField] private Collider _doorCollider;
        [SerializeField] private Renderer _lockIndicator;

        private Vector3 _closedPos;
        private bool _open;
        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private void Awake()
        {
            if (_door == null) _door = transform;
            _closedPos = _door.localPosition;
            // 事前生成NavMesh運用時、閉扉中の経路をObstacleカービングで塞ぐ
            NavMeshDoorBlocker.Attach(_doorCollider);
        }

        private void Start()
        {
            if (PuzzleState.Instance != null && PuzzleState.Instance.PowerRestored) { OpenInstant(); return; }
            // 鍵入手済みでセーブ復帰した場合は開けておく
            if (PuzzleState.Instance != null && PuzzleState.Instance.HasPowerRoomKey) OpenInstant();
            else UpdateIndicator();
        }

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew || _open) return;

            if (PuzzleState.Instance != null && PuzzleState.Instance.HasPowerRoomKey)
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), _door.position, 1f);
                _open = true;
                UpdateIndicator();
                StartCoroutine(SlideDown());
            }
            else
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Click(), _door.position, 0.7f);
            }
        }

        private void OpenInstant()
        {
            _open = true;
            if (_doorCollider != null) _doorCollider.enabled = false;
            _door.localPosition = _closedPos + Vector3.down * 3.0f;
            UpdateIndicator();
        }

        private System.Collections.IEnumerator SlideDown()
        {
            if (_doorCollider != null) _doorCollider.enabled = false;
            Vector3 target = _closedPos + Vector3.down * 3.0f;
            float t = 0f;
            while (t < 1.4f)
            {
                t += Time.deltaTime;
                _door.localPosition = Vector3.Lerp(_closedPos, target, t / 1.4f);
                yield return null;
            }
            _door.localPosition = target;
        }

        private void UpdateIndicator()
        {
            if (_lockIndicator == null) return;
            _lockIndicator.material.color = _open ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.9f, 0.2f, 0.2f);
        }

        public string GetPrompt()
        {
            if (_open) return "";
            bool hasKey = PuzzleState.Instance != null && PuzzleState.Instance.HasPowerRoomKey;
            return hasKey ? "[E] 鍵で配電室を解錠" : "配電室（施錠：配電室の鍵が必要）";
        }
        public float GetProgress01() => -1f;
    }
}
