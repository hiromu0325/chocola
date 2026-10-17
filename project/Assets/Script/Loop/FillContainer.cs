using StarterAssets;
using System.Collections;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 黒塗りの資料を埋めると開く入れ物（棚の扉・引き出し・ロッカー）。
    /// 解けた印（LoopProgress の room/〈資料Id〉_solved）を見て開き、中の資料（Inside）はその時から調べられる。
    /// 閉じている間に調べると「鍵がかかっている」とだけ出す（黒塗りの資料を見つけていれば、関係がありそうだと添える）。
    /// ※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    public class FillContainer : MonoBehaviour, IInteractable, IPromptProvider
    {
        public enum Motion { Hinge, Slide }

        public string RoomId;
        [Tooltip("開く鍵になる黒塗りの資料のId（FillDocs）")]
        public string FillId;
        public string DisplayName = "棚";
        public Motion Kind = Motion.Hinge;
        [Tooltip("動く部品（扉板・引き出し）。原点を蝶番の軸／閉じた位置に置く")]
        public Transform Moving;
        public Vector3 OpenEuler = new Vector3(0f, -105f, 0f);   // 蝶番：閉じた向きから回す角度（ローカル）
        public Vector3 OpenOffset = new Vector3(0f, 0f, -0.3f);  // 引き出し：閉じた位置から動かす量（ローカル）
        public float Seconds = 0.9f;
        [Tooltip("中に入っている物。開くまで当たり判定を切る（調べられない）")]
        public GameObject[] Inside;
        [Tooltip("閉じている間だけ調べられる判定（開いたら切る。中の資料を狙えるように）")]
        public Collider LockCollider;

        private bool _init, _open;
        private Quaternion _closedRot;
        private Vector3 _closedPos;
        private Coroutine _anim;
        private float _lastCall = -10f;

        private string SolvedId => FillId + "_solved";
        private bool Solved => LoopProgress.IsFound(RoomId, SolvedId);

        private void Init()
        {
            if (_init || Moving == null) return;
            _init = true;
            _closedRot = Moving.localRotation;
            _closedPos = Moving.localPosition;
        }

        private void Awake() => Init();

        private void OnEnable()
        {
            LoopProgress.OnFound += HandleFound;
            Sync();
        }

        private void OnDisable() => LoopProgress.OnFound -= HandleFound;

        private void HandleFound(string roomId, string id)
        {
            if (roomId != RoomId || id != SolvedId || _open) return;
            if (isActiveAndEnabled) { if (_anim != null) StopCoroutine(_anim); _anim = StartCoroutine(Open()); }
            else Sync();
        }

        /// <summary>今の進行に合わせて、開いた／閉じた形にすぐ合わせる（つづきから・はじめから）</summary>
        public void Sync()
        {
            Init();
            if (_anim != null) { StopCoroutine(_anim); _anim = null; }
            SetPose(Solved ? 1f : 0f);
            SetOpen(Solved);
        }

        public static void SyncAll()
        {
            foreach (var c in FindObjectsByType<FillContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                c.Sync();
        }

        private IEnumerator Open()
        {
            Init();
            var pos = Moving != null ? Moving.position : transform.position;
            ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), pos, 0.8f);
            yield return new WaitForSeconds(0.35f);
            ProceduralAudio.PlayAt(Kind == Motion.Hinge ? ProceduralAudio.Creak() : ProceduralAudio.Drawer(), pos, 0.6f);
            for (float t = 0f; t < Seconds; t += Time.deltaTime)
            {
                float k = t / Seconds;
                SetPose(1f - (1f - k) * (1f - k) * (1f - k));   // 素早く動き出し、ゆっくり止まる
                yield return null;
            }
            SetPose(1f);
            SetOpen(true);
            _anim = null;
        }

        private void SetPose(float k)
        {
            if (Moving == null || !_init) return;
            if (Kind == Motion.Hinge)
                Moving.localRotation = _closedRot * Quaternion.Euler(OpenEuler * k);
            else
                Moving.localPosition = _closedPos + OpenOffset * k;
        }

        private void SetOpen(bool open)
        {
            _open = open;
            if (Inside != null)
                foreach (var go in Inside)
                    if (go != null)
                        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = open;
            if (LockCollider != null) LockCollider.enabled = !open;
        }

        // ---- 閉じている間に調べた時 ----

        public bool CanInteract => !_open && (GameManager.Instance == null || !GameManager.Instance.IsGameEnded);

        public void OnInteract()
        {
            if (Time.time - _lastCall < 0.25f) return;
            _lastCall = Time.time;
            if (_open || InspectView.IsOpen || NotebookUI.IsOpen) return;
            ProceduralAudio.PlayAt(ProceduralAudio.DoorRattle(), transform.position, 0.4f);
            var d = FillDocs.Get(RoomId, FillId);
            string msg = GameText.Get(GameText.UiKey("fill.locked"), "鍵がかかっている。");
            if (d != null && FillDocs.IsFound(d))
                msg += string.Format(GameText.Get(GameText.UiKey("fill.locked_hint"), "　──メモの『{0}』と、関係がありそうだ"), d.TitleText);
            ToastUI.Show(msg);
        }

        public string GetPrompt() => _open ? "" : string.Format(GameText.Get(GameText.UiKey("prompt.doc"), "[E] {0}を調べる"), DisplayName);
        public float GetProgress01() => -1f;
    }
}
