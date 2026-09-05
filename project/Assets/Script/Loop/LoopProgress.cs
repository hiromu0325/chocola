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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            FoundKeys.Clear();
            OnRoomUnlocked = null;
        }

        private static string Key(string roomId, string id) => roomId + "/" + id;

        public static bool IsFound(string roomId, string id) => FoundKeys.Contains(Key(roomId, id));

        /// <summary>手帳を拾ったか（拾うまでTabの手帳UIは開けず、資料も綴じられない）</summary>
        public static bool NotebookOwned => IsFound(StartRoomId, "notebook");

        /// <summary>アイテム発見。その部屋の必須が揃えば次の部屋を解放する</summary>
        public static void NotifyFound(string roomId, string id)
        {
            if (!FoundKeys.Add(Key(roomId, id))) return;

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
                StoryProgress.PendingUnlockRoom = roomId;
                Notebook.Add("attack_" + roomId, "警報",
                    "資料を読み終えた瞬間、どこかでブレイカーの落ちる音がした。\n" +
                    "警報が鳴り響いている。止めなければ、扉は開かない。");
                BreakerSystem.Instance?.ScriptedDrop(target);
                return;
            }

            UnlockNext(room);
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
