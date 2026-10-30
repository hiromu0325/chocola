using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 部屋が解放された時の演出。ブレイカー復旧と同じ、画面中央の帯（RoomTitleUI）に「扉が開いた」と部屋の名前を出し、
    /// 帯が出る瞬間に解錠の音を鳴らす（閉じる操作は要らない）。
    /// 表示はUiQueue経由：資料ウィンドウを読んでいる間・他の帯や窓の表示中は待機し、閉じられてから順番に出る。
    /// </summary>
    public class LoopUnlockDialog : MonoBehaviour
    {
        private void OnEnable() => LoopProgress.OnRoomUnlocked += HandleUnlocked;
        private void OnDisable() => LoopProgress.OnRoomUnlocked -= HandleUnlocked;

        private void HandleUnlocked(string roomId)
        {
            var room = LoopRooms.Get(roomId);
            string name = room != null ? room.Name : "新しい部屋";
            RoomTitleUI.Instance?.Show("扉が開いた", $"──『{name}』への扉が、回廊のどこかで開いた──", ProceduralAudio.Unlock());
        }
    }
}
