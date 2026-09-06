#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 通しプレイの自動検証（エディタ専用・MCPの execute_code から起動）。
    /// NewGame → 起床 → 各部屋に順に入り、必須の資料/装置を実際の経路で発見
    /// （資料は OnInteract、装置は正解時の Succeed）→ 脚本襲撃はブレイカーを上げて解決
    /// → 退出 → 次の部屋、を最終部屋まで繰り返し、詰まり・エラー・未実装を報告する。
    ///   開始: LoopPlaythroughTest.Begin()   進捗: LoopPlaythroughTest.Report   完了: Done
    /// </summary>
    public class LoopPlaythroughTest : MonoBehaviour
    {
        public static string Report = "";
        public static bool Running, Done;

        private static readonly StringBuilder Sb = new StringBuilder();
        private static int _errors;
        private static readonly List<string> ErrorMsgs = new List<string>();
        private float _t0;

        public static string Begin()
        {
            if (Running) return "already running";
            Sb.Clear(); ErrorMsgs.Clear(); _errors = 0; Report = ""; Done = false; Running = true;
            var go = new GameObject("LoopPlaythroughTest");
            DontDestroyOnLoad(go);
            go.AddComponent<LoopPlaythroughTest>();
            return "started";
        }

        private void Awake()
        {
            Application.logMessageReceived += OnLog;
            _t0 = Time.realtimeSinceStartup;
            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            Running = false;
        }

        private void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            _errors++;
            if (ErrorMsgs.Count < 30) ErrorMsgs.Add($"{type}: {condition}");
        }

        private void Log(string s)
        {
            Sb.Append($"[{Time.realtimeSinceStartup - _t0,6:0.0}s] {s}\n");
            Report = Sb.ToString();
        }

        private IEnumerator Run()
        {
            Application.runInBackground = true;
            Time.timeScale = 1f;

            // ---- 開始 ----
            var gm = GameManager.Instance;
            if (gm == null) { Log("FAIL: GameManager なし"); yield return Finish(); yield break; }
            gm.NewGame();
            yield return WaitUntil(() => gm.State == GameState.Playing, 5f, "Playing");
            Log($"NewGame → state={gm.State} loc={LoopRooms.CurrentRoomId}");

            // 起床カットシーン（始まったら少し見てからスキップ）
            yield return WaitUntil(() => StoryProgress.IntroPlayed, 5f, "intro start");
            yield return new WaitForSeconds(1.5f);
            if (CutsceneDirector.Instance != null && CutsceneDirector.Instance.IsPlaying) CutsceneDirector.Instance.Stop();
            yield return WaitUntil(() => CutsceneDirector.Instance == null || !CutsceneDirector.Instance.IsPlaying, 5f, "intro end");
            Log("起床カットシーン: 終了");

            // ---- 部屋を段階順に ----
            var rooms = new List<LoopRoomRoot>(LoopRooms.All);
            rooms.Sort((a, b) => a.UnlockStage.CompareTo(b.UnlockStage));
            var bs = BreakerSystem.Instance;

            foreach (var room in rooms)
            {
                // 入室可能か
                if (room.UnlockStage > LoopRooms.Stage)
                {
                    Log($"FAIL: {room.Id}(stage{room.UnlockStage}) が解放されていない（現在stage={LoopRooms.Stage}）→ 中断");
                    break;
                }
                if (LoopRooms.CurrentRoomId != room.Id)
                {
                    if (!LoopRooms.InCorridor)
                    {
                        yield return WaitUntil(() => !TransitionBusy(), 4f, "transition idle");
                        RoomTransitionSystem.Instance.ExitToCorridor(LoopRooms.CurrentRoomId, true);
                        yield return WaitUntil(() => LoopRooms.InCorridor, 6f, "exit to corridor");
                    }
                    // 暗転（フェードイン0.45秒）が終わるまで EnterRoom は無視されるので、遷移の完了を待つ
                    yield return WaitUntil(() => !TransitionBusy(), 4f, "transition idle");
                    yield return null;
                    if (!LoopRooms.CanPlayerEnter(room.Id))
                    {
                        Log($"FAIL: {room.Id} に入れない（down={bs?.DownRoomId ?? "-"} stage={LoopRooms.Stage}）→ 中断");
                        break;
                    }
                    RoomTransitionSystem.Instance.EnterRoom(room.Id, false);
                    yield return WaitUntil(() => LoopRooms.CurrentRoomId == room.Id, 6f, "enter " + room.Id);
                    if (LoopRooms.CurrentRoomId != room.Id) { Log($"FAIL: {room.Id} に入室できなかった → 中断"); break; }
                    yield return WaitUntil(() => !TransitionBusy(), 4f, "transition idle");
                }
                Log($"── {room.DisplayName}({room.Id}) 入室  必須={room.RequiredFindables.Length}");

                // 手帳を最初に（無いと資料を綴じられない）
                var order = new List<string>(room.RequiredFindables);
                order.Sort((a, b) => a == "notebook" ? -1 : b == "notebook" ? 1 : 0);

                foreach (var req in order)
                {
                    if (string.IsNullOrEmpty(req) || LoopProgress.IsFound(room.Id, req)) continue;
                    var findable = FindById<LoopFindable>(room.transform, req);
                    if (findable != null)
                    {
                        // 実際の調べる経路（手帳へ綴じ→PuzzleUIで表示）
                        findable.OnInteract();
                        yield return null;
                        ClosePuzzleUi();
                        yield return new WaitForSeconds(0.3f);
                    }
                    else
                    {
                        var lockComp = FindById<LoopLockBase>(room.transform, req);
                        if (lockComp != null)
                        {
                            // 装置は「正解した」経路を直接踏む（入力UIは別途手動で検証）
                            var m = typeof(LoopLockBase).GetMethod("Succeed", BindingFlags.NonPublic | BindingFlags.Instance);
                            m.Invoke(lockComp, new object[] { null });
                            ClosePuzzleUi();
                            yield return new WaitForSeconds(0.3f);
                        }
                        else
                        {
                            Log($"FAIL: {room.Id}/{req} に対応するオブジェクトが部屋内に無い");
                        }
                    }
                    if (!LoopProgress.IsFound(room.Id, req))
                        Log($"FAIL: {room.Id}/{req} を調べたが発見扱いにならない");
                    else
                        Log($"   ✓ {req}");
                }

                if (!LoopProgress.IsRoomComplete(room))
                {
                    Log($"FAIL: {room.Id} が完了にならない（残り{LoopProgress.RemainingIn(room)}）→ 中断");
                    break;
                }

                // 脚本襲撃
                if (StoryScript.AttackOnComplete.TryGetValue(room.Id, out var target))
                {
                    yield return new WaitForSeconds(0.5f);
                    if (bs == null || bs.DownRoomId != target)
                        Log($"FAIL: 脚本襲撃が起きていない（期待={target} 実際={bs?.DownRoomId ?? "-"}）");
                    else
                    {
                        Log($"   ⚡ 脚本襲撃 → {target} のブレイカー降下、異形={(FindObjectsByType<LoopSearcher>(FindObjectsSortMode.None).Length)}体");
                        // 復旧（BreakerSwitch を上げたのと同じ経路）
                        var tr = LoopRooms.Get(target);
                        tr.Breaker.SetUp(true);
                        bs.NotifyRaised(target);
                        yield return new WaitForSeconds(0.5f);
                        Log($"   復旧 → down={bs.DownRoomId ?? "-"} stage={LoopRooms.Stage}");
                    }
                }
                else if (bs != null && bs.DownRoomId != null)
                {
                    // 想定外の降下（チュートリアルの時限降下など）は上げて続行
                    Log($"   (想定外の降下 {bs.DownRoomId} → 復旧して続行)");
                    var tr = LoopRooms.Get(bs.DownRoomId);
                    tr.Breaker.SetUp(true);
                    bs.NotifyRaised(tr.Id);
                    yield return new WaitForSeconds(0.3f);
                }

                int expect = room.UnlockStage + 1;
                bool last = room == rooms[rooms.Count - 1];
                Log($"   完了 → stage={LoopRooms.Stage}" + (last ? "（最終部屋）" : LoopRooms.Stage >= expect ? "" : $"  FAIL: 次(stage{expect})が解放されない"));
                if (!last && LoopRooms.Stage < expect) break;
                if (gm.IsRespawning) yield return WaitUntil(() => !gm.IsRespawning, 10f, "respawn");
            }

            Log($"── 終了: stage={LoopRooms.Stage} loc={LoopRooms.CurrentRoomId ?? "corridor"} state={gm.State} 手帳={Notebook.Count}件 dolls={gm.Dolls}");
            yield return Finish();
        }

        private IEnumerator Finish()
        {
            Log($"エラー/例外ログ: {_errors}件");
            foreach (var e in ErrorMsgs) Log("   ! " + e);
            Done = true;
            Running = false;
            yield return null;
            Destroy(gameObject);
        }

        private IEnumerator WaitUntil(System.Func<bool> cond, float timeout, string what)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!cond() && Time.realtimeSinceStartup < end) yield return null;
            if (!cond()) Log($"FAIL: timeout({timeout}s) waiting {what}");
        }

        private static T FindById<T>(Transform root, string id) where T : Component
        {
            foreach (var c in root.GetComponentsInChildren<T>(true))
            {
                var f = c as LoopFindable; if (f != null && f.Id == id) return c;
                var l = c as LoopLockBase; if (l != null && l.Id == id) return c;
            }
            return null;
        }

        private static readonly FieldInfo BusyField =
            typeof(RoomTransitionSystem).GetField("_busy", BindingFlags.NonPublic | BindingFlags.Instance);

        private static bool TransitionBusy() =>
            RoomTransitionSystem.Instance != null && BusyField != null && (bool)BusyField.GetValue(RoomTransitionSystem.Instance);

        private static void ClosePuzzleUi()
        {
            var ui = PuzzleUI.Instance;
            if (ui == null || !ui.IsOpen) return;
            typeof(PuzzleUI).GetMethod("Close", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(ui, null);
        }
    }
}
#endif
