using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 部屋側の扉。回廊へ戻る（出口扉なら入った扉と反対側の辺の回廊へワープ）。
    /// チュートリアル部屋の扉は、部屋のブレイカーが上がるまで開かない。
    /// 部屋の扉はすべて引いて手前（室内）へ開く（開閉の動きは DoorSwing）。
    /// ※クラス名とファイル名の一致が必須（シーン保存時のスクリプト解決）
    /// </summary>
    public class LoopRoomDoor : MonoBehaviour, IInteractable, IPromptProvider
    {
        public string RoomId;
        public bool IsExitDoor;
        [Tooltip("trueなら自室のブレイカーが上がるまで開かない（チュートリアル用）")]
        public bool RequiresBreakerUp;
        [Tooltip("扉板の開閉（引いて室内へ開く）")]
        public DoorSwing Swing;

        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private bool Locked
        {
            get
            {
                if (!RequiresBreakerUp) return false;
                var room = LoopRooms.Get(RoomId);
                if (room == null) return false;
                // ①必要な資料を全て見つける → ②落ちたブレイカーを上げる、の順に解錠される
                if (!LoopProgress.IsRoomComplete(room)) return true;
                return room.Breaker != null && !room.Breaker.IsUp;
            }
        }

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;

            if (Locked)
            {
                if (Swing != null) Swing.Rattle();
                else ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 0.6f);
                return;
            }
            RoomTransitionSystem.Instance?.ExitToCorridor(RoomId, IsExitDoor);
        }

        public string GetPrompt()
        {
            if (!Locked) return "[E] 廊下へ出る";
            var room = LoopRooms.Get(RoomId);
            if (room != null && !LoopProgress.IsRoomComplete(room))
            {
                // 手帳を持たないうちは、まずそれが目的だと分かるようにする
                if (RoomId == LoopProgress.StartRoomId && !LoopProgress.NotebookOwned)
                    return "鍵がかかっている…（手帳を持って行こう）";
                return "鍵がかかっている…（まだ調べていないものがある）";
            }
            return "扉が開かない…（ブレイカーを上げる）";
        }

        public float GetProgress01() => -1f;
    }
}
