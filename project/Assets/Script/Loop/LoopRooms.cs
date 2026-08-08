using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ループ回廊プロトタイプの部屋レジストリと進行度。
    /// 部屋は物理的には回廊と繋がらない「仮想部屋」で、回廊の扉(辺side・スロットslot)に
    /// 割り当てられる。出口は必ず反対側の辺((side+2)%4)の同スロットの扉に接続される。
    /// </summary>
    public static class LoopRooms
    {
        /// <summary>ゲーム進行度。この値以上のunlockStageの部屋には入れない</summary>
        public static int Stage = 0;

        /// <summary>プレイヤーの現在地（nullなら回廊）</summary>
        public static string CurrentRoomId;

        /// <summary>チュートリアル部屋を一度出たか（出ると再入場できない）</summary>
        public static bool TutorialExited;

        public static bool InCorridor => string.IsNullOrEmpty(CurrentRoomId);

        private static readonly Dictionary<string, LoopRoomRoot> Rooms = new Dictionary<string, LoopRoomRoot>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Stage = 0;
            CurrentRoomId = null;
            TutorialExited = false;
            Rooms.Clear();
        }

        public static void Register(LoopRoomRoot room) => Rooms[room.Id] = room;
        public static void Unregister(LoopRoomRoot room) { if (Rooms.TryGetValue(room.Id, out var r) && r == room) Rooms.Remove(room.Id); }

        public static LoopRoomRoot Get(string id) =>
            !string.IsNullOrEmpty(id) && Rooms.TryGetValue(id, out var r) ? r : null;

        public static IEnumerable<LoopRoomRoot> All => Rooms.Values;

        public static bool IsUnlocked(string id)
        {
            var r = Get(id);
            return r != null && r.UnlockStage <= Stage;
        }

        /// <summary>プレイヤーが入れるか。チュートリアルは一度出ると再入場不可</summary>
        public static bool CanPlayerEnter(string id)
        {
            if (id == LoopProgress.StartRoomId && TutorialExited) return false;
            return IsUnlocked(id);
        }

        /// <summary>侵入可能（解錠済み）の部屋一覧</summary>
        public static List<LoopRoomRoot> Accessible()
        {
            var list = new List<LoopRoomRoot>();
            foreach (var r in Rooms.Values)
                if (r.UnlockStage <= Stage) list.Add(r);
            return list;
        }
    }

    // ※LoopRoomRoot（MonoBehaviour）はシーン保存時のスクリプト解決のため
    //   LoopRoomRoot.cs（クラス名と同名ファイル）にある。
}
