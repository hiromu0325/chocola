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

        /// <summary>アイテム発見。その部屋の必須が揃えば次の部屋を解放する</summary>
        public static void NotifyFound(string roomId, string id)
        {
            if (!FoundKeys.Add(Key(roomId, id))) return;

            var room = LoopRooms.Get(roomId);
            if (room == null) return;

            if (!IsRoomComplete(room)) return;

            // 最初の部屋：必要な資料（新聞・懐中電灯）が揃って初めてブレイカーが落ちる。
            // これを上げると扉が開き、回廊へ出られる
            if (roomId == StartRoomId && room.Breaker != null && room.Breaker.IsUp)
            {
                room.Breaker.SetUp(false);
                Debug.Log("[LoopProgress] 最初の部屋のブレイカーが落ちた（上げると扉が開く）");
            }

            // 次の段階を解放（この部屋のUnlockStage+1 が次の部屋のUnlockStage）
            int next = room.UnlockStage + 1;
            if (LoopRooms.Stage < next)
            {
                LoopRooms.Stage = next;
                var unlocked = FindRoomByStage(next);
                string name = unlocked != null ? unlocked.DisplayName : "新しい部屋";
                Debug.Log($"[LoopProgress] {room.DisplayName} の情報が揃った → {name} が解放");
                Notebook.Add("unlock_" + next, "新しい扉が開いた",
                    $"{room.DisplayName}で見つけた情報から、{name}へ通じる扉が開いた。");
                OnRoomUnlocked?.Invoke(unlocked != null ? unlocked.Id : null);
            }
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

        public static string DebugStatus()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in LoopRooms.All)
                sb.Append($"{r.Id}(stage{r.UnlockStage}) 残り{RemainingIn(r)}  ");
            return sb.ToString();
        }
    }
}
