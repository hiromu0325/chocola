using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 回廊側の扉。部屋が割り当てられていれば（解錠済みのとき）暗転遷移で部屋へ入る。
    /// ExitSide=true の扉は部屋の「出口側」（反対側の辺）で、入ると出口付近に出現する。
    /// 部屋未割り当てのダミー扉は開かない。
    /// </summary>
    public class LoopDoor : MonoBehaviour, IInteractable, IPromptProvider
    {
        public string RoomId;
        public bool ExitSide;

        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;

            if (string.IsNullOrEmpty(RoomId) || !LoopRooms.CanPlayerEnter(RoomId))
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 0.6f);
                return;
            }
            RoomTransitionSystem.Instance?.EnterRoom(RoomId, ExitSide);
        }

        public string GetPrompt()
        {
            if (string.IsNullOrEmpty(RoomId)) return "開かない";
            // 襲撃中は警報の部屋以外どこにも入れない
            var bs = BreakerSystem.Instance;
            if (bs != null && bs.DownRoomId != null && RoomId != bs.DownRoomId)
                return "開かない…（警報が鳴っている）";
            var room = LoopRooms.Get(RoomId);
            if (!LoopRooms.IsUnlocked(RoomId)) return "施錠されている";
            string name = room != null ? room.DisplayName : RoomId;
            return $"[E] {name}へ入る";
        }

        public float GetProgress01() => -1f;
    }

    // ※LoopRoomDoor（MonoBehaviour）はシーン保存時のスクリプト解決のため
    //   LoopRoomDoor.cs（クラス名と同名ファイル）にある。
}
