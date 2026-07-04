using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 襲撃者の部屋のドア。来訪（襲撃）フェーズの間だけ開き、終了すると閉まる。
    /// 中に閉じ込められても死なないが、部屋には常に襲撃者がいる（ResidentSearcher）。
    /// 金庫（WallSafe）は襲撃中しか回せないため、襲撃中にこの部屋へ忍び込むことになる。
    /// </summary>
    public class SearcherRoomDoor : MonoBehaviour, IInteractable, IPromptProvider
    {
        [SerializeField] private Transform _door;
        [SerializeField] private Collider _doorCollider;

        private Vector3 _closedPos;
        private bool _open;
        private Coroutine _anim;
        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private void Awake()
        {
            if (_door == null) _door = transform;
            _closedPos = _door.localPosition;
        }

        private void OnEnable()
        {
            GameEvents.OnVisitStart += HandleVisitStart;
            GameEvents.OnVisitEnd += HandleVisitEnd;
        }

        private void OnDisable()
        {
            GameEvents.OnVisitStart -= HandleVisitStart;
            GameEvents.OnVisitEnd -= HandleVisitEnd;
        }

        private void HandleVisitStart(SearcherType t) => SetOpen(true);

        /// <summary>来訪終了：探索者が部屋へ戻り切ってから閉める（閉め出し防止）</summary>
        private void HandleVisitEnd() => StartCoroutine(CloseWhenSearcherHome());

        private System.Collections.IEnumerator CloseWhenSearcherHome()
        {
            float timeout = 15f;
            while (timeout > 0f && FindFirstObjectByType<SearcherController>() != null)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            SetOpen(false);
        }

        private void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(Slide(open));
        }

        private System.Collections.IEnumerator Slide(bool open)
        {
            // 開く＝床下へスライド（KeyedDoorと同じ演出）
            if (open && _doorCollider != null) _doorCollider.enabled = false;
            ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), _door.position, 0.9f);

            Vector3 from = _door.localPosition;
            Vector3 to = open ? _closedPos + Vector3.down * 2.7f : _closedPos;
            const float dur = 1.4f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                _door.localPosition = Vector3.Lerp(from, to, t / dur);
                yield return null;
            }
            _door.localPosition = to;

            if (!open && _doorCollider != null) _doorCollider.enabled = true;
            _anim = null;
        }

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew || _open) return;
            ProceduralAudio.PlayAt(ProceduralAudio.Click(), _door.position, 0.7f);
        }

        public string GetPrompt() => _open ? "" : "襲撃者の部屋（襲撃の間だけ開く）";
        public float GetProgress01() => -1f;
    }
}
