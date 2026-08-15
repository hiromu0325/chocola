using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ギミック解除（E長押し）で開くスライド扉。各部屋の出入口に置く（階段には無い）。
    /// 解除時間は GameBalanceConfig.gimmickSolveTimeMultiplier に追従する。
    /// デバッグ用に <see cref="DebugOpen"/> / <see cref="DebugOpenAll"/> で即開可能（MCPから使う）。
    /// </summary>
    public class GimmickDoor : MonoBehaviour, IInteractable, IPromptProvider
    {
        [Tooltip("識別子（デバッグAPIで指定する名前）")]
        public string Id;
        public Transform Door;
        public Collider DoorCollider;
        [Tooltip("基準解除時間（秒）。GameBalanceConfigの倍率が掛かる")]
        public float BaseSolveSeconds = 3f;

        private static readonly List<GimmickDoor> All = new List<GimmickDoor>();

        private float _progress;
        private bool _open;
        private float _lastCallTime = -10f;
        private Vector3 _closedPos;

        public bool IsOpen => _open;
        public bool CanInteract => !_open &&
            (GameManager.Instance == null || !GameManager.Instance.IsGameEnded);

        private void Awake()
        {
            if (Door == null) Door = transform;
            _closedPos = Door.localPosition;
            All.Add(this);
        }

        private void OnDestroy() => All.Remove(this);

        public void OnInteract()
        {
            if (_open) return;
            _lastCallTime = Time.time;

            float mult = GameBalanceConfig.Instance != null
                ? GameBalanceConfig.Instance.gimmickSolveTimeMultiplier : 1f;
            float duration = Mathf.Max(0.1f, BaseSolveSeconds * mult);
            _progress += Time.deltaTime / duration;
            if (_progress >= 1f) Open(false);
        }

        private void Open(bool instant)
        {
            if (_open) return;
            _open = true;
            _progress = 1f;
            if (DoorCollider != null) DoorCollider.enabled = false;

            if (instant)
            {
                Door.localPosition = _closedPos + Vector3.down * 2.6f;
            }
            else
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), Door.position, 0.9f);
                StartCoroutine(Slide());
            }
        }

        private System.Collections.IEnumerator Slide()
        {
            Vector3 from = Door.localPosition;
            Vector3 to = _closedPos + Vector3.down * 2.6f;
            const float dur = 1.2f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                Door.localPosition = Vector3.Lerp(from, to, t / dur);
                yield return null;
            }
            Door.localPosition = to;
        }

        public string GetPrompt() => _open ? "" : $"[E長押し] 隔壁ロックを解除 <{Id}>";
        public float GetProgress01() => _open || _progress <= 0f ? -1f : Mathf.Clamp01(_progress);

        // ============= デバッグAPI（MCPのexecute_codeから使用） =============

        /// <summary>Idが一致する扉を即開する</summary>
        public static bool DebugOpen(string id)
        {
            foreach (var d in All)
                if (d != null && d.Id == id && !d._open) { d.Open(true); return true; }
            return false;
        }

        /// <summary>全扉を即開し、開けた数を返す</summary>
        public static int DebugOpenAll()
        {
            int n = 0;
            foreach (var d in All)
                if (d != null && !d._open) { d.Open(true); n++; }
            return n;
        }

        /// <summary>扉一覧と開閉状態</summary>
        public static string DebugList()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var d in All)
                if (d != null) sb.Append($"{d.Id}:{(d._open ? "open" : "closed")}  ");
            return sb.ToString();
        }
    }
}
