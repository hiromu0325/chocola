using UnityEngine;

namespace EscapeProto
{
    /// <summary>人形の部屋への侵入を検知するトリガーゾーン</summary>
    public class DollTriggerZone : MonoBehaviour
    {
        [SerializeField] private DollEvent _event;
        public void SetEvent(DollEvent e) => _event = e;

        private void OnTriggerEnter(Collider other)
        {
            if (_event == null) return;
            if (other.CompareTag("Player") || other.GetComponent<PlayerStatus>() != null)
                _event.OnPlayerEntered();
        }
    }
}
