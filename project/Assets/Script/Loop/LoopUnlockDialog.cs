using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 部屋が解放されたときのダイアログ表示。
    /// 資料を読んだ直後は資料ウィンドウが開いているため、閉じるのを待ってから出す。
    /// </summary>
    public class LoopUnlockDialog : MonoBehaviour
    {
        private readonly Queue<string> _pending = new Queue<string>();
        private bool _showing;

        private void OnEnable() => LoopProgress.OnRoomUnlocked += HandleUnlocked;
        private void OnDisable() => LoopProgress.OnRoomUnlocked -= HandleUnlocked;

        private void HandleUnlocked(string roomId)
        {
            var room = LoopRooms.Get(roomId);
            _pending.Enqueue(room != null ? room.DisplayName : "新しい部屋");
            if (!_showing) StartCoroutine(ShowQueued());
        }

        private IEnumerator ShowQueued()
        {
            _showing = true;
            while (_pending.Count > 0)
            {
                string name = _pending.Dequeue();

                // 資料ウィンドウが閉じるまで待つ（読了 → 解放通知 の順に見せる）
                while (PuzzleUI.Instance == null ||
                       PuzzleUI.Instance.IsOpen || PuzzleUI.Instance.BlockReopen)
                    yield return null;
                yield return new WaitForSeconds(0.35f);

                ProceduralAudio.PlayAt(ProceduralAudio.Unlock(),
                    Camera.main != null ? Camera.main.transform.position : Vector3.zero, 0.8f, spatial: false);

                PuzzleUI.Instance.ShowDocument("扉が開いた",
                    $"見つけた情報が繋がった。\n\n" +
                    $"廊下の扉のひとつ —— <color=#FFE060>{name}</color> —— が開くようになった。\n\n" +
                    "廊下を回り、その部屋を探そう。");

                // このダイアログが閉じられるまで次を出さない
                while (PuzzleUI.Instance != null &&
                       (PuzzleUI.Instance.IsOpen || PuzzleUI.Instance.BlockReopen))
                    yield return null;
            }
            _showing = false;
        }
    }
}
