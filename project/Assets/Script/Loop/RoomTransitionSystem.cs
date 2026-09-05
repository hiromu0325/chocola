using System.Collections;
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
    /// </summary>
    public class RoomTransitionSystem : MonoBehaviour
    {
        public static RoomTransitionSystem Instance { get; private set; }

        [Tooltip("回廊全体のルート")]
        public GameObject CorridorRoot;

        private CanvasGroup _fade;
        private bool _busy;

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
            var home = LoopRooms.Get(LoopProgress.StartRoomId);
            foreach (var r in LoopRooms.All) r.gameObject.SetActive(r == home);
            if (CorridorRoot != null) CorridorRoot.SetActive(home == null);
            LoopRooms.CurrentRoomId = home != null ? home.Id : null;
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
                RoomTitleUI.Instance?.Show(room != null ? room.DisplayName : cur, StoryScript.ChapterLabel(cur));
            }
        }

        private void Start()
        {
            // レジストリはAwakeの実行順に依存するため、シーンから直接収集して確実に登録する
            //（inactiveな部屋のAwakeは走らないので、ここで拾わないと取りこぼす）
            foreach (var r in FindObjectsByType<LoopRoomRoot>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                LoopRooms.Register(r);

            // 開始時は最初の部屋（薄暗い部屋）の中。回廊は非表示
            var tutorial = LoopRooms.Get(LoopProgress.StartRoomId);
            foreach (var r in LoopRooms.All) r.gameObject.SetActive(false);
            if (tutorial != null)
            {
                tutorial.gameObject.SetActive(true);
                LoopRooms.CurrentRoomId = tutorial.Id;
                if (CorridorRoot != null) CorridorRoot.SetActive(false);
            }
        }

        /// <summary>回廊の扉から部屋へ入る（exitSide=trueなら出口側の扉から入り、出口側に出現）</summary>
        public void EnterRoom(string roomId, bool exitSide)
        {
            var room = LoopRooms.Get(roomId);
            if (room == null || _busy) return;
            StartCoroutine(Transition(() =>
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
                    RoomTitleUI.Instance?.Show(room.DisplayName, StoryScript.ChapterLabel(roomId));
            }));
        }

        /// <summary>部屋の扉から回廊へ出る（exitDoor=trueなら反対側の辺の回廊扉へ）</summary>
        public void ExitToCorridor(string roomId, bool exitDoor)
        {
            var room = LoopRooms.Get(roomId);
            if (room == null || _busy) return;
            int side = exitDoor ? (room.Side + 2) % 4 : room.Side;
            Vector3 pos = LoopCorridorLayout.DoorFrontPosition(side, room.Slot);
            StartCoroutine(Transition(() =>
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
            }));
        }

        private IEnumerator Transition(System.Action swap)
        {
            _busy = true;
            yield return Fade(1f, 0.35f);
            swap();
            yield return new WaitForSeconds(0.25f);   // 暗転の「間」
            yield return Fade(0f, 0.45f);
            _busy = false;
        }

        private IEnumerator Fade(float target, float dur)
        {
            float from = _fade.alpha, t = 0f;
            _fade.blocksRaycasts = true;
            while (t < dur)
            {
                t += Time.deltaTime;
                _fade.alpha = Mathf.Lerp(from, target, t / dur);
                yield return null;
            }
            _fade.alpha = target;
            _fade.blocksRaycasts = target > 0.01f;
        }

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
