using UnityEngine;

namespace EscapeProto
{
    /// <summary>ループ回廊プロトタイプのMCPデバッグAPI（execute_codeから使用）</summary>
    public static class DebugLoop
    {
        /// <summary>進行度を設定（入れる部屋が増える）</summary>
        public static string Stage(int n)
        {
            LoopRooms.Stage = n;
            return "stage=" + n + " accessible=" + string.Join(",", LoopRooms.Accessible().ConvertAll(r => r.Id));
        }

        /// <summary>ブレイカーを即降下させる</summary>
        public static string Drop() => BreakerSystem.Instance != null ? BreakerSystem.Instance.DebugDrop() : "no system";

        /// <summary>降下中のブレイカーを即復旧させる</summary>
        public static string Raise() => BreakerSystem.Instance != null ? BreakerSystem.Instance.DebugRaise() : "no system";

        /// <summary>部屋へ暗転遷移（入口側から）</summary>
        public static string Enter(string roomId)
        {
            if (!LoopRooms.CanPlayerEnter(roomId)) return "locked/unknown: " + roomId;
            RoomTransitionSystem.Instance?.EnterRoom(roomId, false);
            return "entering " + roomId;
        }

        /// <summary>現在の部屋から回廊へ出る（出口側から）</summary>
        public static string Exit()
        {
            if (LoopRooms.InCorridor) return "already in corridor";
            RoomTransitionSystem.Instance?.ExitToCorridor(LoopRooms.CurrentRoomId, true);
            return "exiting";
        }

        public static string Status()
        {
            string sys = BreakerSystem.Instance != null ? BreakerSystem.Instance.DebugStatus() : "no BreakerSystem";
            return sys + "\n探索: " + LoopProgress.DebugStatus();
        }

        /// <summary>指定した部屋の必須アイテムを全発見扱いにして次を解放</summary>
        public static string Complete(string roomId) => LoopProgress.DebugCompleteRoom(roomId);

        /// <summary>全部屋を解放（Stage最大化）</summary>
        public static string UnlockAll()
        {
            int max = 0;
            foreach (var r in LoopRooms.All) max = System.Math.Max(max, r.UnlockStage);
            LoopRooms.Stage = max;
            return "stage=" + max;
        }

        /// <summary>
        /// 恐怖演出の強さ（0=通常 1=軽減 2=なし）。今起きている襲撃にもすぐ効かせる：
        /// 軽減・なしでは居る異形を退場させ、なしでは落ちているブレイカーを上げて包囲も解く
        /// </summary>
        public static string Horror(int level)
        {
            HorrorSettings.Level = (HorrorLevel)Mathf.Clamp(level, 0, 2);
            var bs = BreakerSystem.Instance;
            if (!HorrorSettings.Searchers)
                foreach (var s in Object.FindObjectsByType<LoopSearcher>(FindObjectsSortMode.None))
                    if (!s.IsRetreating) s.Retreat();
            if (!HorrorSettings.Attacks && bs != null)
            {
                if (bs.Siege) bs.SetSiege(false);
                if (bs.DownRoomId != null) bs.DebugRaise();
            }
            return "恐怖演出=" + HorrorSettings.Label(HorrorSettings.Level);
        }

        /// <summary>章の途中（部屋ごと）から始める。Id は部屋Id か finale_toys / finale_upload</summary>
        public static string StartAt(string pointId) => DebugStart.StartAt(pointId);

        /// <summary>開始地点の一覧</summary>
        public static string Points()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in DebugStart.Points()) sb.Append($"{p.Id}  [{p.Chapter}] {p.Label}\n");
            return sb.ToString();
        }

        /// <summary>鳴っている警報音源の一覧（音が残っていないかの確認用）</summary>
        public static string Alarms()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var src in UnityEngine.Object.FindObjectsByType<AudioSource>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (src != null && src.isPlaying) sb.Append(src.gameObject.name + " ");
            return sb.Length == 0 ? "鳴っている音源なし" : "鳴動中: " + sb;
        }
    }
}
