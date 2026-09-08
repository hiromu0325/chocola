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
    /// → 退出 → 次の部屋、を最終部屋まで繰り返し、続けて終章
    /// （各部屋のおもちゃ回収 → 記録端末で長押しアップロード。包囲の降下は復旧しながら）まで通す。
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
        private bool _navOk;

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

            var gm = GameManager.Instance;
            if (gm == null) { Log("FAIL: GameManager なし"); yield return Finish(); yield break; }
            gm.NewGame();
            yield return WaitUntil(() => gm.State == GameState.Playing, 5f, "Playing");
            Log($"NewGame → state={gm.State} loc={LoopRooms.CurrentRoomId}");

            yield return WaitUntil(() => StoryProgress.IntroPlayed, 5f, "intro start");
            yield return new WaitForSeconds(1.5f);
            if (CutsceneDirector.Instance != null && CutsceneDirector.Instance.IsPlaying) CutsceneDirector.Instance.Stop();
            yield return WaitUntil(() => CutsceneDirector.Instance == null || !CutsceneDirector.Instance.IsPlaying, 5f, "intro end");
            Log("起床カットシーン: 終了");

            var rooms = new List<LoopRoomRoot>(LoopRooms.All);
            rooms.Sort((a, b) => a.UnlockStage.CompareTo(b.UnlockStage));
            var bs = BreakerSystem.Instance;
            bool aborted = false;

            // ================= 本編：14部屋 =================
            foreach (var room in rooms)
            {
                if (room.UnlockStage > LoopRooms.Stage)
                {
                    Log($"FAIL: {room.Id}(stage{room.UnlockStage}) が解放されていない（現在stage={LoopRooms.Stage}）→ 中断");
                    aborted = true; break;
                }
                yield return GoTo(room);
                if (!_navOk) { aborted = true; break; }
                Log($"── {room.DisplayName}({room.Id}) 入室  必須={room.RequiredFindables.Length}　目標:「{Strip(LoopObjective.Text())}」");

                var order = new List<string>(room.RequiredFindables);
                order.Sort((a, b) => a == "notebook" ? -1 : b == "notebook" ? 1 : 0);
                foreach (var req in order)
                {
                    if (string.IsNullOrEmpty(req) || LoopProgress.IsFound(room.Id, req)) continue;
                    yield return Acquire(room, req);
                }

                if (!LoopProgress.IsRoomComplete(room))
                {
                    Log($"FAIL: {room.Id} が完了にならない（残り{LoopProgress.RemainingIn(room)}）→ 中断");
                    aborted = true; break;
                }

                if (StoryScript.AttackOnComplete.TryGetValue(room.Id, out var target))
                {
                    yield return new WaitForSeconds(0.5f);
                    if (bs == null || bs.DownRoomId != target)
                        Log($"FAIL: 脚本襲撃が起きていない（期待={target} 実際={bs?.DownRoomId ?? "-"}）");
                    else
                    {
                        Log($"   ⚡ 脚本襲撃 → {target} 降下、異形={FindObjectsByType<LoopSearcher>(FindObjectsSortMode.None).Length}体　目標:「{Strip(LoopObjective.Text())}」");
                        yield return RaiseBreaker(target);
                        Log($"   復旧 → down={bs.DownRoomId ?? "-"} stage={LoopRooms.Stage}");
                    }
                }
                else if (bs != null && bs.DownRoomId != null)
                {
                    Log($"   (想定外の降下 {bs.DownRoomId} → 復旧して続行)");
                    yield return RaiseBreaker(bs.DownRoomId);
                }

                int expect = room.UnlockStage + 1;
                bool last = room == rooms[rooms.Count - 1];
                Log($"   完了 → stage={LoopRooms.Stage}" + (last ? "（最終部屋）" : LoopRooms.Stage >= expect ? "" : $"  FAIL: 次(stage{expect})が解放されない"));
                if (!last && LoopRooms.Stage < expect) { aborted = true; break; }
                if (gm.IsRespawning) yield return WaitUntil(() => !gm.IsRespawning, 10f, "respawn");
            }

            // ================= 終章 =================
            if (!aborted)
            {
                var fin = LoopFinale.Instance;
                yield return WaitUntil(() => fin != null && fin.Started, 4f, "finale start");
                if (fin == null || !fin.Started) Log("FAIL: 終章が始まらない（LoopFinale）");
                else
                {
                    Log($"══ 終章開始: 断片 {fin.TotalCount} 個　目標:「{Strip(LoopObjective.Text())}」");
                    // おもちゃ回収（段階順）
                    for (var next = fin.NextToyRoom(); next != null; next = fin.NextToyRoom())
                    {
                        yield return GoTo(next);
                        if (!_navOk) { aborted = true; break; }
                        var toy = FindById<LoopFindable>(next.transform, "toy");
                        if (toy == null || !toy.gameObject.activeSelf) { Log($"FAIL: {next.Id} のおもちゃが出現していない"); aborted = true; break; }
                        yield return Reachable(toy.gameObject, $"{next.Id}/toy");
                        toy.OnInteract();
                        yield return null;
                        ClosePuzzleUi();
                        yield return new WaitForSeconds(0.3f);
                        Log($"   ✓ {next.Id}: {toy.DisplayName}（{fin.CollectedCount}/{fin.TotalCount}）");
                    }
                    if (!aborted && !fin.AllToysCollected) { Log("FAIL: 断片が揃わない"); aborted = true; }

                    if (!aborted)
                    {
                        // 最初の部屋の記録端末で長押し
                        var dim = LoopRooms.Get(LoopProgress.StartRoomId);
                        yield return GoTo(dim);
                        var pc = dim.GetComponentInChildren<SavePoint>(true);
                        if (pc == null) { Log("FAIL: 記録端末が無い"); }
                        else
                        {
                            Log($"── アップロード開始　目標:「{Strip(LoopObjective.Text())}」 prompt=「{pc.GetPrompt()}」");
                            float start = Time.realtimeSinceStartup; int drops = 0; string lastDown = null; float dropAt = 0f;
                            while (!fin.Completed && Time.realtimeSinceStartup - start < 240f)
                            {
                                pc.OnInteract();   // 押しっぱなし相当
                                if (bs != null && bs.DownRoomId != null)
                                {
                                    if (lastDown != bs.DownRoomId)
                                    {
                                        lastDown = bs.DownRoomId; drops++; dropAt = Time.realtimeSinceStartup;
                                        Log($"   ⚡ 包囲降下#{drops}: {bs.DownRoomId}  進捗={fin.UploadProgress:P0}  目標:「{Strip(LoopObjective.Text())}」");
                                    }
                                    // 「行って上げて戻る」を3秒で模擬
                                    if (Time.realtimeSinceStartup - dropAt > 3f) { yield return RaiseBreaker(bs.DownRoomId); lastDown = null; }
                                }
                                yield return null;
                            }
                            Log($"── アップロード {(fin.Completed ? "完了" : "未完了")} 降下{drops}回  state={gm.State} prompt=「{pc.GetPrompt()}」");
                        }
                    }
                }
            }

            Log($"── 終了: stage={LoopRooms.Stage} loc={LoopRooms.CurrentRoomId ?? "corridor"} state={gm.State} 手帳={Notebook.Count}件 dolls={gm.Dolls}");
            yield return Finish();
        }

        /// <summary>必須ID1つを実経路で獲得する</summary>
        private IEnumerator Acquire(LoopRoomRoot room, string req)
        {
            var findable = FindById<LoopFindable>(room.transform, req);
            if (findable != null)
            {
                yield return Reachable(findable.gameObject, $"{room.Id}/{req}");
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
                    yield return Reachable(lockComp.gameObject, $"{room.Id}/{req}");
                    var m = typeof(LoopLockBase).GetMethod("Succeed", BindingFlags.NonPublic | BindingFlags.Instance);
                    m.Invoke(lockComp, new object[] { null });
                    ClosePuzzleUi();
                    yield return new WaitForSeconds(0.3f);
                }
                else Log($"FAIL: {room.Id}/{req} に対応するオブジェクトが部屋内に無い");
            }
            Log(LoopProgress.IsFound(room.Id, req) ? $"   ✓ {req}" : $"FAIL: {room.Id}/{req} を調べたが発見扱いにならない");
        }

        /// <summary>視線が届くか（4方向の目線からレイ）。届かなければFAILを記録</summary>
        private IEnumerator Reachable(GameObject target, string label)
        {
            Physics.SyncTransforms();
            var col = target.GetComponent<Collider>();
            if (col == null) { Log($"FAIL: {label} にコライダーが無い"); yield break; }
            Vector3 c = col.bounds.center;
            foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                Vector3 eye = c - dir * 1.3f; eye.y = 1.55f;
                if (Physics.Raycast(eye, (c - eye).normalized, out var hit, 3.5f, ~0, QueryTriggerInteraction.Collide) &&
                    (hit.collider == col || hit.collider.transform.IsChildOf(target.transform)))
                    yield break;
            }
            Log($"FAIL: {label} に視線が届かない（什器に埋まっている？）");
        }

        private IEnumerator RaiseBreaker(string roomId)
        {
            var tr = LoopRooms.Get(roomId);
            if (tr == null || tr.Breaker == null) { Log($"FAIL: {roomId} のブレイカーが無い"); yield break; }
            tr.Breaker.SetUp(true);
            BreakerSystem.Instance.NotifyRaised(roomId);
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>部屋へ移動（回廊経由）。成功で _navOk=true</summary>
        private IEnumerator GoTo(LoopRoomRoot room)
        {
            _navOk = true;
            if (LoopRooms.CurrentRoomId == room.Id) yield break;
            if (!LoopRooms.InCorridor)
            {
                yield return WaitUntil(() => !TransitionBusy(), 4f, "transition idle");
                RoomTransitionSystem.Instance.ExitToCorridor(LoopRooms.CurrentRoomId, true);
                yield return WaitUntil(() => LoopRooms.InCorridor, 6f, "exit to corridor");
            }
            yield return WaitUntil(() => !TransitionBusy(), 4f, "transition idle");
            yield return null;
            if (!LoopRooms.CanPlayerEnter(room.Id))
            {
                Log($"FAIL: {room.Id} に入れない（down={BreakerSystem.Instance?.DownRoomId ?? "-"} stage={LoopRooms.Stage}）→ 中断");
                _navOk = false; yield break;
            }
            RoomTransitionSystem.Instance.EnterRoom(room.Id, false);
            yield return WaitUntil(() => LoopRooms.CurrentRoomId == room.Id, 6f, "enter " + room.Id);
            if (LoopRooms.CurrentRoomId != room.Id) { Log($"FAIL: {room.Id} に入室できなかった → 中断"); _navOk = false; yield break; }
            yield return WaitUntil(() => !TransitionBusy(), 4f, "transition idle");
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

        private static string Strip(string rich) =>
            System.Text.RegularExpressions.Regex.Replace(rich ?? "", "<.*?>", "");
    }
}
#endif
