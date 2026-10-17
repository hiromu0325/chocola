using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// デバッグ：章の途中（部屋ごと）から始める。
    /// 選んだ部屋より前の部屋をすべて完了済み（資料・装置・脚本襲撃の解決済み、手帳にも綴じた状態）にし、
    /// 起床カットシーンを飛ばして、その部屋の中から始める。
    /// プレイ中に選んだ時は、襲撃や異形の途中状態を残さないようシーンを読み直してから始める。
    ///   DebugStart.Points() … 開始地点の一覧　／　DebugStart.StartAt("saeki_home") … 開始
    /// </summary>
    public static class DebugStart
    {
        /// <summary>終章：各部屋のおもちゃ（記憶の断片）を集めるところから</summary>
        public const string FinaleToys = "finale_toys";
        /// <summary>終章：断片が揃い、最初の部屋の記録端末でアップロードするところから</summary>
        public const string FinaleUpload = "finale_upload";

        public struct Point
        {
            public string Id;        // 部屋Id または FinaleToys / FinaleUpload
            public string Chapter;   // 章の短い名前（序・1章…）
            public string Label;     // 表示名
        }

        /// <summary>シーンの読み直し後に始める開始地点（読み直しでは静的フィールドは消えない）</summary>
        public static string Pending { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Pending = null;

        /// <summary>開始地点の一覧（部屋の解放順＋終章の2段階）</summary>
        public static List<Point> Points()
        {
            var list = new List<Point>();
            var rooms = new List<LoopRoomRoot>(LoopRooms.All);
            rooms.Sort((a, b) => a.UnlockStage.CompareTo(b.UnlockStage));
            foreach (var r in rooms)
            {
                int ch = StoryScript.ChapterOf(r.Id);
                string label = StoryScript.ChapterLabel(r.Id);
                list.Add(new Point
                {
                    Id = r.Id,
                    Chapter = ch >= 0 ? StoryScript.ChapterTabNames[ch] : "?",
                    Label = (label != null ? label.Replace("　", " ") + "  " : "") + r.DisplayName,
                });
            }
            string fin = StoryScript.ChapterTabNames[StoryScript.ChapterTabNames.Length - 1];
            list.Add(new Point { Id = FinaleToys, Chapter = fin, Label = "終章 記憶の断片を集める" });
            list.Add(new Point { Id = FinaleUpload, Chapter = fin, Label = "終章 記録端末でアップロード（包囲）" });
            return list;
        }

        /// <summary>開始する。タイトル画面なら今すぐ、プレイ中ならシーンを読み直してから</summary>
        public static string StartAt(string pointId)
        {
            var gm = GameManager.Instance;
            if (gm == null) return "no GameManager";
            if (!IsKnown(pointId)) return "unknown point: " + pointId;
            if (gm.State == GameState.Title) return Apply(pointId);
            Pending = pointId;
            gm.RestartGame();
            return "reloading → " + pointId;
        }

        /// <summary>読み直し後（タイトル画面になってから）DebugPanel が呼ぶ</summary>
        public static string ApplyPending()
        {
            string id = Pending;
            Pending = null;
            return id != null ? Apply(id) : "no pending";
        }

        private static bool IsKnown(string id) =>
            id == FinaleToys || id == FinaleUpload || LoopRooms.Get(id) != null;

        private static string Apply(string pointId)
        {
            var gm = GameManager.Instance;
            var rts = RoomTransitionSystem.Instance;
            if (gm == null || rts == null) return "no GameManager / RoomTransitionSystem";

            // 進んだ段階（この段階の部屋が開いていて、それより前の部屋は完了済み）
            int maxStage = 0;
            foreach (var r in LoopRooms.All) maxStage = Mathf.Max(maxStage, r.UnlockStage);
            var target = LoopRooms.Get(pointId);
            int stage = target != null ? target.UnlockStage : maxStage + 1;

            gm.NewGame();                          // 進行・手帳・人形を初期化してプレイ開始
            StoryProgress.IntroPlayed = true;      // 起床カットシーンは飛ばす

            // 前の部屋を完了済みに：必須の資料・装置、脚本襲撃（解決済み）、訪問済み（部屋名タイトルを出さない）
            var found = new List<string>();
            foreach (var r in LoopRooms.All)
            {
                if (r.UnlockStage >= stage) continue;
                if (r.RequiredFindables != null)
                    foreach (var req in r.RequiredFindables)
                        if (!string.IsNullOrEmpty(req)) found.Add(r.Id + "/" + req);
                if (StoryScript.AttackOnComplete.ContainsKey(r.Id)) found.Add("story/attack_" + r.Id);
                StoryProgress.MarkVisited(r.Id);
            }
            if (pointId == FinaleUpload)
                foreach (var f in Object.FindObjectsByType<LoopFindable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (f.Id == "toy") found.Add(f.RoomId + "/toy");
            LoopProgress.ImportFound(found);
            LoopRooms.Stage = stage;
            if (stage > 0)
            {
                LoopRooms.TutorialExited = true;
                StoryProgress.MarkVisited("corridor");
            }

            // 見つけた資料・解いた装置を手帳へ綴じ、拾った道具を消す（通知は出さない）
            foreach (var f in Object.FindObjectsByType<LoopFindable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                f.RefreshFound();
            foreach (var l in Object.FindObjectsByType<LoopLockBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l.Solved && !string.IsNullOrEmpty(l.SuccessTitle))
                    Notebook.Add($"{l.RoomId}_{l.Id}", l.SuccessTitle, l.SuccessBody);
            if (LoopProgress.IsFound(LoopProgress.StartRoomId, "flashlight"))
            {
                var fl = Object.FindFirstObjectByType<Flashlight>();
                if (fl != null) fl.SetOn(true);
            }

            // 置く場所：部屋の中（断片集めは息子の部屋を出た回廊、アップロードは最初の部屋）
            if (target != null) rts.DebugPlace(target.Id, inRoom: true);
            else if (pointId == FinaleToys)
            {
                LoopRoomRoot last = null;
                foreach (var r in LoopRooms.All) if (last == null || r.UnlockStage > last.UnlockStage) last = r;
                if (last != null) rts.DebugPlace(last.Id, inRoom: false);
            }
            else rts.DebugPlace(LoopProgress.StartRoomId, inRoom: true);

            string msg = $"debug start: {pointId} stage={LoopRooms.Stage} found={found.Count} 恐怖演出={HorrorSettings.Label(HorrorSettings.Level)}";
            Debug.Log("[DebugStart] " + msg);
            return msg;
        }
    }
}
