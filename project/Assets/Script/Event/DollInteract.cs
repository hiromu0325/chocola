using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>人形本体：インタラクトで目を渡そうとする（罠）</summary>
    public class DollInteract : MonoBehaviour, IInteractable, IPromptProvider
    {
        [SerializeField] private DollEvent _event;
        public void SetEvent(DollEvent e) => _event = e;

        private float _lastCallTime = -10f;
        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;
            if (_event != null) _event.TryGiveEyes();
        }

        public string GetPrompt() => "[E] 人形に近づく…";
        public float GetProgress01() => -1f;
    }
}
