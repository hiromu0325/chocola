using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 今やるべきことを1行にまとめる（HUD上部の「目標」）。
    /// 優先順: 終章の工程 → 警報（ブレイカー復旧/逃走） → 手帳 → 部屋の資料・装置 → 退出 → 次の部屋へ
    /// </summary>
    public static class LoopObjective
    {
        private static readonly string[] SideNames = { "北", "東", "南", "西" };

        public static bool IsLoopScene => LoopRooms.Get(LoopProgress.StartRoomId) != null;

        /// <summary>目標の種類（HUDの見た目を変える）</summary>
        public enum Tone { Normal, Warning, Done }

        private static Tone _tone;

        /// <summary>「目標: …」の1行（色付き）。ログや検証用</summary>
        public static string Text()
        {
            string s = Plain(out var tone);
            if (string.IsNullOrEmpty(s)) return "";
            var c = tone == Tone.Warning ? UiTheme.Danger : tone == Tone.Done ? UiTheme.Positive : UiTheme.Accent;
            return $"<color={UiTheme.Rgb(c)}>目標: {s}</color>";
        }

        /// <summary>目標の文（「目標:」も色も付けない）と種類。HUD はこれを使う</summary>
        /// <param name="anyState">true ならプレイ中以外（一時停止など）でも作る</param>
        public static string Plain(out Tone tone, bool anyState = false)
        {
            _tone = Tone.Normal;
            string s = Compute(anyState);
            tone = _tone;
            return s;
        }

        private static string Compute(bool anyState)
        {
            var gm = GameManager.Instance;
            if (gm == null || (!anyState && gm.State != GameState.Playing)) return "";
            string cur = LoopRooms.CurrentRoomId;
            var room = LoopRooms.Get(cur);
            var bs = BreakerSystem.Instance;
            var fin = LoopFinale.Instance;

            // ---- 警報中：復旧が最優先 ----
            if (bs != null && bs.DownRoomId != null)
            {
                var down = LoopRooms.Get(bs.DownRoomId);
                string name = down != null ? down.Name : bs.DownRoomId;
                if (cur == bs.DownRoomId) return Warn("ブレイカーを上げる（東の壁）");
                if (fin != null && fin.Uploading) return Warn($"アップロード中断中──『{name}』のブレイカーを上げて戻る");
                return Warn($"警報の部屋『{name}』{DoorHint(down)}へ行き、ブレイカーを上げる　※異形が徘徊中");
            }

            // ---- 終章 ----
            if (fin != null && fin.Started)
            {
                if (fin.Completed) return Good("アップロード完了");
                if (!fin.AllToysCollected)
                {
                    if (room != null && !LoopProgress.IsFound(cur, "toy"))
                        return Main($"『{room.Name}』で娘のおもちゃを探す（{fin.CollectedCount}/{fin.TotalCount}）");
                    var next = fin.NextToyRoom();
                    return next != null
                        ? Main($"娘のおもちゃを集める（{fin.CollectedCount}/{fin.TotalCount}）──次は『{next.Name}』{DoorHint(next)}")
                        : Main("娘のおもちゃを集める");
                }
                if (fin.Uploading) return Main($"記録端末で[E]長押し──アップロード {Mathf.RoundToInt(fin.UploadProgress * 100f)}%");
                return cur == LoopProgress.StartRoomId
                    ? Main("記録端末で[E]長押し──断片をアップロードする")
                    : Main("最初の部屋『薄暗い部屋』の記録端末へ戻り、断片をアップロードする");
            }

            // ---- 手帳 ----
            if (!LoopProgress.NotebookOwned) return Main("机の上の手帳を手に取る");

            // ---- 部屋の中 ----
            if (room != null)
            {
                int remain = LoopProgress.RemainingIn(room);
                if (remain > 0)
                {
                    bool lockLeft = false;
                    foreach (var l in room.GetComponentsInChildren<LoopLockBase>(true))
                        if (!l.Solved && System.Array.IndexOf(room.RequiredFindables, l.Id) >= 0) { lockLeft = true; break; }
                    int docs = remain - CountUnsolvedLocks(room);
                    // 残りが黒塗りの資料の入れ物の中の資料だけ：埋めるように案内する
                    var fill = FillHint(room, out int insideLeft);
                    if (fill != null && docs - insideLeft <= 0 && !lockLeft) return Main($"{fill}（残り{remain}）");
                    if (docs > 0 && lockLeft) return Main($"『{room.Name}』で資料を読み、装置を解く（残り{remain}）");
                    if (lockLeft) return Main($"『{room.Name}』の装置を解く──答えは資料の中（残り{remain}）");
                    return Main($"『{room.Name}』で資料を探す（残り{remain}）");
                }
                if (!string.IsNullOrEmpty(StoryProgress.PendingUnlockRoom))
                {
                    // 襲撃は画面を閉じてから始まる。まだ落ちていない間は「揃った」とだけ出す
                    var bsys = BreakerSystem.Instance;
                    if (bsys != null && bsys.DownRoomId == null && bsys.PendingRoomId == null) return Good("この部屋の情報は揃った");
                    return Warn("警報が鳴っている。部屋を出て、鳴っている部屋へ向かう");
                }
                var nxt = NextRoom();
                if (nxt == null) return Good("この部屋の情報は揃った。部屋を出る");
                return Main($"部屋を出て『{nxt.Name}』{DoorHint(nxt)}へ向かう");
            }

            // ---- 回廊 ----
            var target = NextRoom();
            if (target == null) return Main("開いている部屋を探す");
            return Main($"『{target.Name}』{DoorHint(target)}の扉へ向かう");
        }

        /// <summary>まだ完了していない、入れる部屋のうち最も段階の進んだもの</summary>
        public static LoopRoomRoot NextRoom()
        {
            LoopRoomRoot best = null;
            foreach (var r in LoopRooms.All)
            {
                if (r.UnlockStage > LoopRooms.Stage) continue;
                if (r.Id == LoopProgress.StartRoomId && LoopRooms.TutorialExited) continue;
                if (LoopProgress.IsRoomComplete(r)) continue;
                if (best == null || r.UnlockStage > best.UnlockStage) best = r;
            }
            return best;
        }

        /// <summary>
        /// 必須の資料が黒塗りの資料の入れ物の中にある時の案内（無ければ null）。insideLeft = 入れ物の中の未発見の必須の数
        /// </summary>
        private static string FillHint(LoopRoomRoot room, out int insideLeft)
        {
            insideLeft = 0;
            string hint = null;
            foreach (var fc in room.GetComponentsInChildren<FillContainer>(true))
            {
                if (fc.Inside == null) continue;
                foreach (var go in fc.Inside)
                {
                    var f = go != null ? go.GetComponent<LoopFindable>() : null;
                    if (f == null || f.Found || System.Array.IndexOf(room.RequiredFindables, f.Id) < 0) continue;
                    insideLeft++;
                    var d = FillDocs.Get(fc.RoomId, fc.FillId);
                    if (d == null || hint != null) continue;
                    if (FillDocs.IsSolved(d)) hint = $"開いた{fc.DisplayName}の中を調べる";
                    else if (FillDocs.IsFound(d)) hint = $"『{d.TitleText}』の黒塗りの所に、資料から切り取った言葉を当てはめる──手帳のメモ";
                    else hint = $"『{room.Name}』で資料を探す";
                }
            }
            return hint;
        }

        private static int CountUnsolvedLocks(LoopRoomRoot room)
        {
            int n = 0;
            foreach (var l in room.GetComponentsInChildren<LoopLockBase>(true))
                if (!l.Solved && System.Array.IndexOf(room.RequiredFindables, l.Id) >= 0) n++;
            return n;
        }

        /// <summary>回廊のどの辺・何番目の扉か（ミニマップと同じ表記）</summary>
        public static string DoorHint(LoopRoomRoot r) =>
            r == null ? "" : $"（{SideNames[r.Side % 4]}側 {r.Slot + 1}番）";

        private static string Main(string s) { _tone = Tone.Normal; return s; }
        private static string Warn(string s) { _tone = Tone.Warning; return s; }
        private static string Good(string s) { _tone = Tone.Done; return s; }
    }
}
