using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 探索の進行管理。部屋ごとの必須アイテムを全て見つけると次の部屋が解放される。
    /// 解放順: 薄暗い部屋(dim) → 書斎(study) → 電車車内(train) → 研究所応接室(lab)
    /// </summary>
    public static class LoopProgress
    {
        /// <summary>最初の部屋（チュートリアル。一度出ると再入場不可）</summary>
        public const string StartRoomId = "dim";

        private static readonly HashSet<string> FoundKeys = new HashSet<string>();

        /// <summary>部屋が解放されたときの通知（部屋Id）</summary>
        public static event System.Action<string> OnRoomUnlocked;

        /// <summary>発見・解決の通知（部屋Id, Id）。黒塗りの資料の入れ物はこれで開く</summary>
        public static event System.Action<string, string> OnFound;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            FoundKeys.Clear();
            OnRoomUnlocked = null;
            OnFound = null;
        }

        private static string Key(string roomId, string id) => roomId + "/" + id;

        public static bool IsFound(string roomId, string id) => FoundKeys.Contains(Key(roomId, id));

        /// <summary>手帳を拾ったか（拾うまでTabの手帳UIは開けず、資料も綴じられない）</summary>
        public static bool NotebookOwned => IsFound(StartRoomId, "notebook");

        /// <summary>懐中電灯を拾ったか（拾うまで F で点けられない）</summary>
        public static bool FlashlightOwned => IsFound(StartRoomId, "flashlight");

        /// <summary>カセットレコーダーを拾ったか（拾うまでカセットテープは再生できない）</summary>
        public static bool TapePlayerOwned => IsFound(StartRoomId, "tapeplayer");

        /// <summary>アイテム発見。その部屋の必須が揃えば次の部屋を解放する</summary>
        public static void NotifyFound(string roomId, string id)
        {
            if (!FoundKeys.Add(Key(roomId, id))) return;
            OnFound?.Invoke(roomId, id);

            // 懐中電灯は拾った時点で点ける（以降は常時携行の想定）
            if (id == "flashlight")
            {
                var fl = Object.FindFirstObjectByType<Flashlight>();
                if (fl != null) fl.SetOn(true);
            }

            var room = LoopRooms.Get(roomId);
            if (room == null) return;

            if (!IsRoomComplete(room)) return;

            // 脚本襲撃：この部屋の完了で指定部屋のブレイカーが鳴る。
            // 次の部屋の解放は復旧（または死亡による終了）まで持ち越す
            if (StoryScript.AttackOnComplete.TryGetValue(roomId, out var target) &&
                !IsFound("story", "attack_" + roomId))
            {
                FoundKeys.Add(Key("story", "attack_" + roomId));   // 一度きり
                if (!HorrorSettings.Attacks)
                {
                    // デバッグ（恐怖演出なし）：襲撃を起こさず、そのまま次の部屋を開ける
                    Debug.Log($"[LoopProgress] 恐怖演出なし: {roomId} 完了時の脚本襲撃（{target}）を飛ばす");
                    UnlockNext(room);
                    return;
                }
                StoryProgress.PendingUnlockRoom = roomId;
                // 落とすのは、読んでいる資料や装置の画面を閉じてから（読んでいる最中には始めない）
                StartAttackWhenFree(roomId, target);
                return;
            }

            UnlockNext(room);
        }

        /// <summary>画面を閉じてから襲撃が始まるまでの間（秒）。閉じた瞬間に落とさない</summary>
        public const float AttackDelayAfterClose = 1.0f;

        /// <summary>
        /// 脚本襲撃を、開いている画面（資料・手帳・装置・カットシーン・ポーズ）が全部閉じてから始める。
        /// 閉じて AttackDelayAfterClose 秒たったら落とす。その間にまた画面を開いたら、閉じるまで待ち直す
        /// </summary>
        private static void StartAttackWhenFree(string roomId, string target)
        {
            MonoBehaviour host = UiQueue.Instance != null ? (MonoBehaviour)UiQueue.Instance : BreakerSystem.Instance;
            if (host == null) { BeginAttack(roomId, target); return; }
            host.StartCoroutine(AttackWhenFree(roomId, target));
        }

        private static IEnumerator AttackWhenFree(string roomId, string target)
        {
            float calm = 0f;
            while (calm < AttackDelayAfterClose)
            {
                yield return null;
                calm = UiBusy ? 0f : calm + Time.unscaledDeltaTime;
            }
            BeginAttack(roomId, target);
        }

        /// <summary>プレイヤーが何かの画面を開いている（歩き回っている時だけ false）</summary>
        private static bool UiBusy =>
            UiQueue.GlobalBusy ||
            (LoopPuzzleUI.Instance != null && LoopPuzzleUI.Instance.IsOpen) ||
            (SafeDialUI.Instance != null && SafeDialUI.Instance.IsOpen) ||
            (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing);

        private static void BeginAttack(string roomId, string target)
        {
            if (StoryProgress.PendingUnlockRoom != roomId) return;   // 待つ間に「はじめから」した等
            AttackDebugLog.Log("drop", $"脚本襲撃: {roomId} 完了 → 画面を閉じたので {target} を落とす");
            Notebook.Add("attack_" + roomId, "警報",
                "資料を読み終えて顔を上げた時、どこかでブレイカーの落ちる音がした。\n" +
                "警報が鳴っている。止めなければ、扉は開かない。");
            BreakerSystem.Instance?.ScriptedDrop(target);
        }

        /// <summary>脚本襲撃の解決（ブレイカー復旧 or 死亡による終了）。持ち越した解放を実行</summary>
        public static void NotifyBreakerRestored(string restoredRoomId)
        {
            if (string.IsNullOrEmpty(StoryProgress.PendingUnlockRoom)) return;
            var completed = LoopRooms.Get(StoryProgress.PendingUnlockRoom);
            StoryProgress.PendingUnlockRoom = null;
            if (completed != null) UnlockNext(completed);
        }

        /// <summary>completedRoomの次の段階を解放する</summary>
        private static void UnlockNext(LoopRoomRoot room)
        {
            int next = room.UnlockStage + 1;
            if (LoopRooms.Stage >= next) return;

            var unlocked = FindRoomByStage(next);
            if (unlocked == null)
            {
                // 次の部屋が未実装（仕様が続く場所）。段階だけ進めて静かに終わる
                LoopRooms.Stage = next;
                Debug.Log($"[LoopProgress] {room.DisplayName} 完了。次の部屋は未実装（stage={next}）");
                return;
            }

            LoopRooms.Stage = next;
            Debug.Log($"[LoopProgress] {room.DisplayName} の情報が揃った → {unlocked.DisplayName} が解放");
            Notebook.Add("unlock_" + next, "新しい扉が開いた",
                $"{room.DisplayName}で見つけた情報から、{unlocked.DisplayName}へ通じる扉が開いた。");
            OnRoomUnlocked?.Invoke(unlocked.Id);
        }

        /// <summary>その部屋の必須アイテムが全て見つかっているか</summary>
        public static bool IsRoomComplete(LoopRoomRoot room)
        {
            if (room == null || room.RequiredFindables == null) return false;
            foreach (var req in room.RequiredFindables)
                if (!string.IsNullOrEmpty(req) && !IsFound(room.Id, req)) return false;
            return true;
        }

        /// <summary>未発見の必須アイテム数（HUD/デバッグ用）</summary>
        public static int RemainingIn(LoopRoomRoot room)
        {
            if (room == null || room.RequiredFindables == null) return 0;
            int n = 0;
            foreach (var req in room.RequiredFindables)
                if (!string.IsNullOrEmpty(req) && !IsFound(room.Id, req)) n++;
            return n;
        }

        private static LoopRoomRoot FindRoomByStage(int stage)
        {
            foreach (var r in LoopRooms.All)
                if (r.UnlockStage == stage) return r;
            return null;
        }

        /// <summary>デバッグ: 現在の部屋の必須アイテムを全部発見済みにする</summary>
        public static string DebugCompleteRoom(string roomId)
        {
            var room = LoopRooms.Get(roomId);
            if (room == null) return "unknown room: " + roomId;
            foreach (var req in room.RequiredFindables) NotifyFound(roomId, req);
            return $"{room.DisplayName} complete → stage={LoopRooms.Stage}";
        }

        /// <summary>
        /// 「はじめから」用：回廊の進行に関わる静的状態を全部初期化する。
        /// 静的フィールドはシーンの再読み込み（[R]リスタート／タイトルへ戻る）では消えないので、
        /// 2周目が全解放状態で始まらないよう GameManager.NewGame から呼ぶ。
        /// </summary>
        public static void ResetForNewGame()
        {
            FoundKeys.Clear();
            LoopRooms.Stage = 0;
            LoopRooms.TutorialExited = false;
            StoryProgress.IntroPlayed = false;
            StoryProgress.PendingUnlockRoom = null;
            StoryProgress.ImportVisited(null);
            MemoSnippets.Clear();
            FillDocs.Clear();
            DocState.Clear();
            foreach (var f in Object.FindObjectsByType<LoopFindable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                f.ResetForNewGame();
            FillContainer.SyncAll();   // 黒塗りの資料の入れ物を閉じ直す
            foreach (var e in Object.FindObjectsByType<EchoScene>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                e.ResetForNewGame();
        }

        // ---- セーブ連携 ----
        public static List<string> ExportFound() => new List<string>(FoundKeys);
        public static void ImportFound(List<string> list)
        {
            FoundKeys.Clear();
            if (list != null) foreach (var k in list) FoundKeys.Add(k);
        }

        public static string DebugStatus()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in LoopRooms.All)
                sb.Append($"{r.Id}(stage{r.UnlockStage}) 残り{RemainingIn(r)}  ");
            return sb.ToString();
        }
    }
}
