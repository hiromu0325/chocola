using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// 回廊⇔仮想部屋の暗転遷移。
    /// 暗転中に「回廊モデルを非表示→部屋モデルを表示＋プレイヤーをワープ」する
    /// （部屋は回廊の物理配置を無視した別位置に存在する）。
    /// 出口扉は入った扉と反対側の辺((side+2)%4)の同スロットの回廊扉へ繋がる。
    ///
    /// 扉を開けると扉板が動き出し（回廊の扉は押して奥へ、部屋の扉は引いて手前へ）、
    /// 開いていく扉の向こうに「奥へ行くほど暗くなる廊下」が一瞬見えたところで暗転する。
    /// 着いた側では開いた扉が背後で閉まる。回廊の扉の奥の廊下は1つだけで、開く扉の裏へ動かして使う
    /// （部屋の扉の外の廊下は部屋ごとに固定で置いてある）。
    /// </summary>
    public class RoomTransitionSystem : MonoBehaviour
    {
        public static RoomTransitionSystem Instance { get; private set; }

        [Tooltip("回廊全体のルート")]
        public GameObject CorridorRoot;
        [Tooltip("回廊の扉の奥に見せる暗い廊下（1つだけ。開く扉の裏へ動かして使う）")]
        public GameObject CorridorPassage;

        // 扉の開閉と暗転の時間（扉が動き出してから暗転し始めるまでの間に、奥の暗い廊下が見える）
        private const float OpenSeconds = 1.05f;
        private const float FadeDelay = 0.3f;
        private const float FadeOutSeconds = 0.7f;
        private const float HoldSeconds = 0.25f;
        private const float FadeInSeconds = 0.5f;
        private const float CloseDelay = 0.15f;
        private const float CloseSeconds = 0.85f;
        private const float ArriveOpen = 0.8f;       // 着いた側の扉は、この開き具合から背後で閉まる
        private const float WallDepth = 0.15f;       // 回廊の内周の壁の厚み（奥の廊下は壁の裏から始まる）

        private CanvasGroup _fade;
        private bool _busy;
        private FirstPersonController _frozen;
        private readonly List<LoopDoor> _corridorDoors = new List<LoopDoor>();
        private readonly List<LoopRoomDoor> _roomDoors = new List<LoopRoomDoor>();

        private void Awake()
        {
            Instance = this;
            BuildFadeOverlay();
        }

        private void OnEnable() => GameEvents.OnWhiteout += HandleWhiteout;
        private void OnDisable() => GameEvents.OnWhiteout -= HandleWhiteout;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// 死亡（ホワイトアウト）時：最初の部屋（薄暗い部屋＝人形の部屋）で目を覚ます。
        /// GameManagerのリスポーン地点も最初の部屋内を指している。
        /// </summary>
        private void HandleWhiteout(float _)
        {
            StopAllCoroutines();
            _busy = false;
            if (_fade != null) { _fade.alpha = 0f; _fade.blocksRaycasts = false; }
            if (_frozen != null) { _frozen.enabled = true; _frozen = null; }
            foreach (var d in _corridorDoors) if (d != null && d.Swing != null) d.Swing.Set(0f);
            foreach (var d in _roomDoors) if (d != null && d.Swing != null) d.Swing.Set(0f);
            PlacePassage(null);
            var home = LoopRooms.Get(LoopProgress.StartRoomId);
            foreach (var r in LoopRooms.All) r.gameObject.SetActive(r == home);
            if (CorridorRoot != null) CorridorRoot.SetActive(home == null);
            LoopRooms.CurrentRoomId = home != null ? home.Id : null;
            ReflectionProbes.RefreshActive();
        }

        private void Update()
        {
            // 開始部屋（薄暗い部屋）の初回タイトル。起床カットシーンが終わってから出す
            if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
            string cur = LoopRooms.CurrentRoomId;
            if (string.IsNullOrEmpty(cur) || StoryProgress.HasVisited(cur)) return;
            if (!StoryProgress.IntroPlayed) return;
            if (CutsceneDirector.Instance != null && CutsceneDirector.Instance.IsPlaying) return;
            if (StoryProgress.MarkVisited(cur))
            {
                var room = LoopRooms.Get(cur);
                RoomTitleUI.Instance?.Show(room != null ? room.Name : cur, room != null ? room.ChapterLabel : null);
            }
        }

        private void Start()
        {
            // レジストリはAwakeの実行順に依存するため、シーンから直接収集して確実に登録する
            //（inactiveな部屋のAwakeは走らないので、ここで拾わないと取りこぼす）
            foreach (var r in FindObjectsByType<LoopRoomRoot>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                LoopRooms.Register(r);
            _corridorDoors.AddRange(FindObjectsByType<LoopDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            _roomDoors.AddRange(FindObjectsByType<LoopRoomDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            PlacePassage(null);

            // 開始時は最初の部屋（薄暗い部屋）の中。回廊は非表示
            var tutorial = LoopRooms.Get(LoopProgress.StartRoomId);
            foreach (var r in LoopRooms.All) r.gameObject.SetActive(false);
            if (tutorial != null)
            {
                tutorial.gameObject.SetActive(true);
                LoopRooms.CurrentRoomId = tutorial.Id;
                if (CorridorRoot != null) CorridorRoot.SetActive(false);
            }
            ReflectionProbes.RefreshActive();
        }

        /// <summary>回廊の扉から部屋へ入る（exitSide=trueなら出口側の扉から入り、出口側に出現）</summary>
        public void EnterRoom(string roomId, bool exitSide)
        {
            var room = LoopRooms.Get(roomId);
            if (room == null || _busy) return;
            var from = FindCorridorDoor(roomId, exitSide);
            var to = FindRoomDoor(roomId, exitSide);
            StartCoroutine(DoorTransition(from != null ? from.Swing : null, true, () =>
            {
                if (CorridorRoot != null) CorridorRoot.SetActive(false);
                foreach (var r in LoopRooms.All) r.gameObject.SetActive(r == room);
                LoopRooms.CurrentRoomId = roomId;
                AttackDebugLog.Log("move", $"部屋へ入室: {roomId}");
                // スポーン地点の向き＝部屋の奥を向く（電車なら車両の奥へ視線が通る）
                var spawn = exitSide ? room.ExitSpawn : room.EntrySpawn;
                TeleportPlayer(spawn != null ? spawn.position : room.transform.position,
                               spawn != null ? spawn.eulerAngles.y : (float?)null);
                // 初入室：部屋名タイトル（ダークソウル風）＋章節ラベル
                if (StoryProgress.MarkVisited(roomId))
                    RoomTitleUI.Instance?.Show(room.Name, room.ChapterLabel);
            }, to != null ? to.Swing : null, false));
        }

        /// <summary>部屋の扉から回廊へ出る（exitDoor=trueなら反対側の辺の回廊扉へ）</summary>
        public void ExitToCorridor(string roomId, bool exitDoor)
        {
            var room = LoopRooms.Get(roomId);
            if (room == null || _busy) return;
            int side = exitDoor ? (room.Side + 2) % 4 : room.Side;
            Vector3 pos = LoopCorridorLayout.DoorFrontPosition(side, room.Slot);
            var from = FindRoomDoor(roomId, exitDoor);
            var to = FindCorridorDoor(roomId, exitDoor);
            StartCoroutine(DoorTransition(from != null ? from.Swing : null, false, () =>
            {
                foreach (var r in LoopRooms.All) r.gameObject.SetActive(false);
                if (CorridorRoot != null) CorridorRoot.SetActive(true);
                LoopRooms.CurrentRoomId = null;
                AttackDebugLog.Log("move", $"回廊へ退出（{roomId}から）");
                if (roomId == LoopProgress.StartRoomId) LoopRooms.TutorialExited = true;
                // 廊下へ出たら廊下沿いを向く。
                //（扉に背を向けると幅3mの廊下では外周壁が目の前に来てしまうため、
                //   扉の正面方向から90度回して通路の伸びる方向へ視線を通す）
                TeleportPlayer(pos, LoopCorridorLayout.DoorYaw(side) + 90f);
                // 初めて回廊に出た時もタイトルを出す
                if (StoryProgress.MarkVisited("corridor"))
                    RoomTitleUI.Instance?.Show("回廊", null);
            }, to != null ? to.Swing : null, true));
        }

        /// <summary>プレイヤーが扉を出入りしている最中か（開き始めから背後で閉まるまで）</summary>
        public bool IsBusy => _busy;
        /// <summary>出入りの最中はプレイヤーは捕まらない（襲撃者の捕獲判定が見る）</summary>
        public static bool PlayerProtected => Instance != null && Instance._busy;

        /// <summary>部屋の扉（入口 / 出口）の開閉</summary>
        public DoorSwing RoomDoorSwing(string roomId, bool exitDoor)
        {
            var d = FindRoomDoor(roomId, exitDoor);
            return d != null ? d.Swing : null;
        }

        /// <summary>部屋に通じる回廊の扉（入口側 / 出口側）の開閉</summary>
        public DoorSwing CorridorDoorSwing(string roomId, bool exitSide)
        {
            var d = FindCorridorDoor(roomId, exitSide);
            return d != null ? d.Swing : null;
        }

        private DoorSwing _npcPassageDoor;

        /// <summary>
        /// 襲撃者が回廊の扉から出てくる間だけ、その扉の裏に暗い廊下を置く。
        /// プレイヤーが出入りしている最中は使えない（false）
        /// </summary>
        public bool ShowPassageFor(DoorSwing door)
        {
            if (_busy || CorridorPassage == null || door == null) return false;
            PlacePassage(door);
            _npcPassageDoor = door;
            return true;
        }

        public void HidePassageFor(DoorSwing door)
        {
            if (_busy || door == null || _npcPassageDoor != door) return;
            PlacePassage(null);
            _npcPassageDoor = null;
        }

        private LoopDoor FindCorridorDoor(string roomId, bool exitSide)
        {
            foreach (var d in _corridorDoors)
                if (d != null && d.RoomId == roomId && d.ExitSide == exitSide) return d;
            return null;
        }

        private LoopRoomDoor FindRoomDoor(string roomId, bool exitDoor)
        {
            foreach (var d in _roomDoors)
                if (d != null && d.RoomId == roomId && d.IsExitDoor == exitDoor) return d;
            return null;
        }

        /// <summary>回廊の扉の奥の暗い廊下を、その扉の裏へ置く（null で片付ける）</summary>
        private void PlacePassage(DoorSwing door)
        {
            if (CorridorPassage == null) return;
            if (door == null) { CorridorPassage.SetActive(false); return; }
            var t = door.transform;
            CorridorPassage.transform.SetPositionAndRotation(t.TransformPoint(0f, 0f, -WallDepth), t.rotation);
            CorridorPassage.SetActive(true);
        }

        /// <summary>
        /// 扉を開けて暗転し、向こう側で扉が背後に閉まる。
        /// from=開ける扉（押す＝回廊の扉 / 引く＝部屋の扉）、to=着いた側の扉
        /// </summary>
        private IEnumerator DoorTransition(DoorSwing from, bool fromCorridor, System.Action swap,
                                           DoorSwing to, bool toCorridor)
        {
            _busy = true;
            _npcPassageDoor = null;
            _fade.blocksRaycasts = true;
            var player = GameObject.FindGameObjectWithTag("Player");
            var cc = player != null ? player.GetComponent<CharacterController>() : null;
            var fpc = player != null ? player.GetComponent<FirstPersonController>() : null;
            if (fpc != null && fpc.enabled) { fpc.enabled = false; _frozen = fpc; }   // 扉を開ける間は立ち止まる

            Vector3 start = player != null ? player.transform.position : Vector3.zero, goal = start;
            if (from != null)
            {
                if (fromCorridor) PlacePassage(from);
                Vector3 at = from.transform.position + Vector3.up * 1.0f;
                ProceduralAudio.PlayAt(ProceduralAudio.DoorLatch(), at, 0.8f);
                ProceduralAudio.PlayAt(ProceduralAudio.DoorCreak(), at, 0.5f);
                if (player != null) goal = StepTarget(from, start, fromCorridor);
            }

            // 扉が開いていく。少し遅れて暗転が始まり、その間だけ奥の暗い廊下が見える
            float t = 0f, total = FadeDelay + FadeOutSeconds;
            while (t < total)
            {
                t += Time.deltaTime;
                if (from != null) from.Set(EaseOut(t / OpenSeconds));
                if (cc != null && cc.enabled && goal != start)
                {
                    // 引いて開ける扉は扉板が当たらない所まで下がり、押して開ける扉は奥へ半歩踏み出す
                    float k = fromCorridor ? Smooth((t - FadeDelay * 0.5f) / FadeOutSeconds) : Smooth(t / 0.45f);
                    Vector3 d = Vector3.Lerp(start, goal, k) - player.transform.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > 1e-8f) cc.Move(d);
                }
                _fade.alpha = Mathf.Clamp01((t - FadeDelay) / FadeOutSeconds);
                yield return null;
            }
            _fade.alpha = 1f;

            swap();
            ReflectionProbes.RefreshActive();                 // 着いた空間の反射を今の照明で撮り直す
            if (from != null) from.Set(0f);                   // 置いてきた扉は閉じておく
            PlacePassage(null);
            if (to != null)
            {
                if (toCorridor) PlacePassage(to);
                to.Set(ArriveOpen);
            }
            if (_frozen != null) { _frozen.enabled = true; _frozen = null; }
            yield return new WaitForSeconds(HoldSeconds);        // 暗転の「間」

            // 明けていく間に、通ってきた扉が背後で閉まる
            float t2 = 0f, end = Mathf.Max(FadeInSeconds, CloseDelay + CloseSeconds);
            while (t2 < end)
            {
                t2 += Time.deltaTime;
                _fade.alpha = 1f - Mathf.Clamp01(t2 / FadeInSeconds);
                if (t2 >= FadeInSeconds) _fade.blocksRaycasts = false;
                if (to != null) to.Set(ArriveOpen * (1f - EaseIn((t2 - CloseDelay) / CloseSeconds)));
                yield return null;
            }
            _fade.alpha = 0f;
            _fade.blocksRaycasts = false;
            if (to != null)
            {
                to.Set(0f);
                ProceduralAudio.PlayAt(ProceduralAudio.DoorShut(), to.transform.position + Vector3.up * 1.0f, 0.8f);
            }
            PlacePassage(null);
            _busy = false;
        }

        /// <summary>扉を開ける時の立ち位置（扉のユニットの +Z が手前＝プレイヤーの側）</summary>
        private static Vector3 StepTarget(DoorSwing door, Vector3 pos, bool pushing)
        {
            var t = door.transform;
            Vector3 local = t.InverseTransformPoint(pos);
            // 扉から離れている（デバッグの移動など）なら動かさない
            if (local.z < 0f || local.z > 3.2f || Mathf.Abs(local.x) > 1.6f) return pos;
            if (pushing)
            {
                if (local.z <= 0.8f) return pos;
                local.z = Mathf.Max(0.8f, local.z - 0.4f);
            }
            else
            {
                if (local.z >= 1.3f) return pos;
                local.z = 1.3f;
            }
            Vector3 w = t.TransformPoint(local);
            w.y = pos.y;
            return w;
        }

        private static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }
        private static float EaseIn(float x) { x = Mathf.Clamp01(x); return x * x; }
        private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>
        /// プレイヤーを移動させる。yawを渡すと視点の向きもそこへ揃える
        /// （部屋に入ったら奥を向く／廊下に出たら扉に背を向ける）。
        /// </summary>
        private static void TeleportPlayer(Vector3 pos, float? yaw = null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = pos;
            if (yaw.HasValue)
            {
                var fpc = player.GetComponent<FirstPersonController>();
                if (fpc != null) fpc.SetLook(yaw.Value);           // 上下も水平に戻す
                else player.transform.rotation = Quaternion.Euler(0f, yaw.Value, 0f);
            }
            if (cc != null) cc.enabled = true;
        }

        /// <summary>最前面の黒フェードオーバーレイ</summary>
        private void BuildFadeOverlay()
        {
            var canvasGo = new GameObject("TransitionFade");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var imgGo = new GameObject("Black");
            imgGo.transform.SetParent(canvasGo.transform, false);
            var img = imgGo.AddComponent<Image>();
            img.color = Color.black;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _fade = canvasGo.AddComponent<CanvasGroup>();
            _fade.alpha = 0f;
            _fade.blocksRaycasts = false;
            _fade.interactable = false;
        }
    }

    /// <summary>回廊のレイアウト定数（ビルダーとランタイムで共有）</summary>
    public static class LoopCorridorLayout
    {
        public const float InnerHalf = 7f;     // 内壁までの距離
        public const float OuterHalf = 10f;    // 外壁までの距離
        public const float WallH = 3f;
        public const int DoorsPerSide = 10;
        public const float DoorW = 1.0f;

        public static float Gap => (InnerHalf * 2f - DoorsPerSide * DoorW) / (DoorsPerSide + 1);

        /// <summary>辺side・スロットslotの扉の「軸に沿った」座標</summary>
        public static float SlotT(int slot) =>
            -InnerHalf + Gap + DoorW * 0.5f + slot * (DoorW + Gap);

        /// <summary>扉の中心（内壁上）のワールド座標。side: 0=N(z+),1=E(x+),2=S(z-),3=W(x-)</summary>
        public static Vector3 DoorPosition(int side, int slot)
        {
            float t = SlotT(slot);
            switch (side)
            {
                case 0: return new Vector3(t, 0f, InnerHalf);
                case 1: return new Vector3(InnerHalf, 0f, -t);
                case 2: return new Vector3(-t, 0f, -InnerHalf);
                default: return new Vector3(-InnerHalf, 0f, t);
            }
        }

        /// <summary>扉の前（回廊側に0.9m離れた）位置。ワープ着地用</summary>
        public static Vector3 DoorFrontPosition(int side, int slot)
        {
            Vector3 p = DoorPosition(side, slot);
            Vector3 outward = side switch
            {
                0 => Vector3.forward,
                1 => Vector3.right,
                2 => Vector3.back,
                _ => Vector3.left,
            };
            return p + outward * 0.9f;
        }

        /// <summary>扉の向き（回廊側を向くYaw）</summary>
        public static float DoorYaw(int side) => side switch { 0 => 0f, 1 => 90f, 2 => 180f, _ => 270f };
    }
}
