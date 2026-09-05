using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 部屋が解放されたときのダイアログ表示。
    /// 表示はUiQueue経由：資料ウィンドウを読んでいる間・他のポップアップ表示中は
    /// 待機し、閉じられてから順番に出る。
    /// </summary>
    public class LoopUnlockDialog : MonoBehaviour
    {
        private void OnEnable() => LoopProgress.OnRoomUnlocked += HandleUnlocked;
        private void OnDisable() => LoopProgress.OnRoomUnlocked -= HandleUnlocked;

        private void HandleUnlocked(string roomId)
        {
            var room = LoopRooms.Get(roomId);
            string name = room != null ? room.DisplayName : "新しい部屋";

            if (UiQueue.Instance == null) return;
            UiQueue.Instance.Enqueue(
                () =>
                {
                    ProceduralAudio.PlayAt(ProceduralAudio.Unlock(),
                        Camera.main != null ? Camera.main.transform.position : Vector3.zero,
                        0.8f, spatial: false);
                    PuzzleUI.Instance?.ShowDocument("扉が開いた",
                        "見つけた情報が繋がった。\n\n" +
                        $"廊下の扉のひとつ —— <color=#FFE060>{name}</color> —— が開くようになった。\n\n" +
                        "廊下を回り、その部屋を探そう。");
                },
                () => PuzzleUI.Instance != null && PuzzleUI.Instance.IsOpen,
                "unlock:" + roomId);
        }
    }
}
