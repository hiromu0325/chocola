using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 終章の工程。
    /// ①息子の部屋に到達（stage 14）すると、各部屋に「娘の記憶の断片＝思い出のおもちゃ」が1つずつ現れる
    /// ②すべて集めて最初の部屋の記録端末で[E]長押し→アップロード
    /// ③アップロード中はブレイカーが絶えず降下し（包囲モード）、復旧しないと進まない
    /// ④100%でクリア
    /// </summary>
    public class LoopFinale : MonoBehaviour
    {
        public static LoopFinale Instance { get; private set; }

        [Tooltip("アップロードに必要な長押しの合計秒数（ブレイカー降下中は進まない）")]
        public float UploadSeconds = 60f;
        [Tooltip("包囲モードのブレイカー降下周期（秒）")]
        public float SiegeCycleSeconds = 25f;
        [Tooltip("この段階に到達すると終章が始まる（息子の部屋の完了）")]
        public int TriggerStage = 14;

        public bool Started { get; private set; }
        public bool Uploading { get; private set; }
        public bool Completed { get; private set; }
        public float UploadProgress { get; private set; }

        private readonly List<LoopRoomRoot> _toyRooms = new List<LoopRoomRoot>();
        public int TotalCount => _toyRooms.Count;
        public int CollectedCount
        {
            get { int n = 0; foreach (var r in _toyRooms) if (LoopProgress.IsFound(r.Id, "toy")) n++; return n; }
        }
        public bool AllToysCollected => Started && TotalCount > 0 && CollectedCount >= TotalCount;

        private void Awake() => Instance = this;
        private void OnEnable() => GameEvents.OnGameStarted += ResetState;
        private void OnDisable() => GameEvents.OnGameStarted -= ResetState;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>「はじめから」「つづきから」で状態を戻す（クリア後にタイトルから再開しても残らないように）</summary>
        private void ResetState()
        {
            Started = false; Uploading = false; Completed = false; UploadProgress = 0f;
            _toyRooms.Clear();
            if (BreakerSystem.Instance != null && BreakerSystem.Instance.Siege) BreakerSystem.Instance.SetSiege(false);
            foreach (var f in FindObjectsByType<LoopFindable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (f.Id == "toy") f.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Started) return;
            if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
            if (LoopRooms.Stage < TriggerStage) return;
            Begin();
        }

        /// <summary>終章開始：おもちゃを各部屋に出現させる</summary>
        public void Begin()
        {
            if (Started) return;
            Started = true;
            _toyRooms.Clear();
            foreach (var f in FindObjectsByType<LoopFindable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (f.Id != "toy") continue;
                var room = LoopRooms.Get(f.RoomId);
                if (room == null) continue;
                _toyRooms.Add(room);
                // 既に拾った断片（セーブ復帰）は出さない
                f.gameObject.SetActive(!LoopProgress.IsFound(f.RoomId, "toy"));
            }
            _toyRooms.Sort((a, b) => a.UnlockStage.CompareTo(b.UnlockStage));

            Notebook.Add("finale_start", "最後の工程",
                "息子の部屋を出た瞬間、回廊の空気が変わった。\n" +
                "各部屋に、娘のおもちゃがひとつずつ置かれている──\n" +
                "彼女の記憶の断片だ。すべて集めて、最初の部屋の記録端末から\n" +
                "リナシータへ送る。それが、父親にしかできない最後の工程。");
            RoomTitleUI.Instance?.Show("記憶の断片", "──各部屋に、娘のおもちゃが現れた──");
            ToastUI.Show($"娘のおもちゃを集めよう（0/{TotalCount}）");
            Debug.Log($"[LoopFinale] 開始: 断片 {TotalCount} 個");
        }

        /// <summary>おもちゃを拾った通知（LoopFindable側から。進捗トーストのため）</summary>
        public void NotifyToyCollected()
        {
            int c = CollectedCount;
            if (c >= TotalCount)
            {
                RoomTitleUI.Instance?.Show("断片が揃った", "──最初の部屋の記録端末へ──");
                Notebook.Add("finale_toys", "断片が揃った",
                    $"{TotalCount}個の断片が揃った。最初の部屋の記録端末で、リナシータへ送る。\n" +
                    "送っている間、この回廊は静かではいてくれないだろう。");
            }
            else ToastUI.Show($"娘のおもちゃ（{c}/{TotalCount}）");
        }

        /// <summary>次に拾いに行く部屋（段階順で未回収のもの）</summary>
        public LoopRoomRoot NextToyRoom()
        {
            foreach (var r in _toyRooms) if (!LoopProgress.IsFound(r.Id, "toy")) return r;
            return null;
        }

        /// <summary>記録端末の長押し中に毎フレーム呼ぶ。進んだら true</summary>
        public bool UploadTick(float dt)
        {
            if (!AllToysCollected || Completed) return false;
            if (!Uploading)
            {
                Uploading = true;
                BreakerSystem.Instance?.SetSiege(true, SiegeCycleSeconds);
                RoomTitleUI.Instance?.Show("アップロード開始", "──断片をリナシータへ。端末から手を離すな──");
                Debug.Log("[LoopFinale] アップロード開始（包囲モード）");
            }
            var bs = BreakerSystem.Instance;
            if (bs != null && bs.DownRoomId != null) return false;   // 電源が落ちている間は進まない
            UploadProgress = Mathf.Clamp01(UploadProgress + dt / Mathf.Max(1f, UploadSeconds));
            if (UploadProgress >= 1f) Complete();
            return true;
        }

        private void Complete()
        {
            if (Completed) return;
            Completed = true;
            Uploading = false;
            BreakerSystem.Instance?.SetSiege(false);
            Notebook.Add("finale_done", "アップロード完了",
                "断片はすべて送られた。\n端末の画面に、娘の名前が一度だけ表示されて、消えた。");
            Debug.Log("[LoopFinale] アップロード完了 → クリア");
            GameManager.Instance?.NotifyEscaped();
        }
    }
}
