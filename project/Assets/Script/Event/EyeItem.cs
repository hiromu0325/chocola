using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>落ちている『硝子の目』：拾うと人形に渡せるようになる（だが渡すと死ぬ）</summary>
    public class EyeItem : MonoBehaviour, IInteractable, IPromptProvider
    {
        private float _lastCallTime = -10f;
        public bool CanInteract => true;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;

            var ev = FindFirstObjectByType<DollEvent>();
            if (ev != null) ev.AddEye();
            Destroy(gameObject);
        }

        public string GetPrompt() => "[E] 硝子の目を拾う";
        public float GetProgress01() => -1f;
    }
}
