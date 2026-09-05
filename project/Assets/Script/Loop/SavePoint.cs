using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 専用のセーブPC（記録端末）。最初の部屋に置かれ、調べると手動セーブする。
    /// </summary>
    public class SavePoint : MonoBehaviour, IInteractable, IPromptProvider
    {
        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;

            if (GameManager.Instance != null) GameManager.Instance.SaveNow();
            ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 0.7f);

            if (PuzzleUI.Instance != null && !PuzzleUI.Instance.IsOpen && !PuzzleUI.Instance.BlockReopen)
                PuzzleUI.Instance.ShowDocument(
                    "記録端末",
                    "ここまでの記録を残した。\n\n" +
                    "異形に捕まった時は、この部屋で目を覚ます。\n" +
                    "棚の人形が、身代わりになってくれる限りは──");
        }

        public string GetPrompt() => "[E] 記録する（セーブ）";
        public float GetProgress01() => -1f;
    }
}
