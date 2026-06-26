using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 手帳：探索者の特徴と対策が書かれている。インタラクト/Tabで開閉
    /// </summary>
    public class MemoNote : MonoBehaviour, IInteractable, IPromptProvider
    {
        private float _lastCallTime = -10f;
        public bool CanInteract => true;

        public void OnInteract()
        {
            bool isNewPress = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNewPress) return;
            if (HUDManager.Instance != null) HUDManager.Instance.ToggleMemo();
        }

        public string GetPrompt() => "[E] 手帳を読む（Tabでも開閉）";
        public float GetProgress01() => -1f;
    }
}
