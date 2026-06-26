using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 2階の社員個室に置かれた戸棚。OwnerNumber がその個室の社員。
    /// 調べると、その社員が鍵保管者（KeyHolder）なら配電室の鍵を入手できる。
    /// </summary>
    public class KeyCabinet : MonoBehaviour, IInteractable, IPromptProvider
    {
        [Tooltip("この個室の社員番号")]
        public string OwnerNumber;
        [Tooltip("鍵の見た目（入手で出現／取得で消す。任意）")]
        [SerializeField] private GameObject _keyVisual;

        private float _lastCallTime = -10f;

        public bool CanInteract => PuzzleState.Instance == null || PuzzleState.Instance.PuzzlesEnabled;

        private void Start()
        {
            // すでに鍵入手済みなら鍵の見た目は消しておく
            if (_keyVisual != null && PuzzleState.Instance != null && PuzzleState.Instance.HasPowerRoomKey)
                _keyVisual.SetActive(false);
        }

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;
            var ps = PuzzleState.Instance;
            if (ps == null) return;

            if (ps.HasPowerRoomKey)
            {
                if (HUDManager.Instance != null) HUDManager.Instance.ShowSubtitle("配電室の鍵は既に持っている。", 2.5f);
                return;
            }

            if (ps.TryTakeKey(OwnerNumber))
            {
                if (_keyVisual != null) _keyVisual.SetActive(false);
                ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 0.9f);
                if (HUDManager.Instance != null)
                    HUDManager.Instance.ShowSubtitle("配電室の鍵を見つけた！配電室の扉を開けられる。", 4f);
            }
            else
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 0.6f);
                if (HUDManager.Instance != null)
                    HUDManager.Instance.ShowSubtitle("ここには配電室の鍵はない。貸出記録の社員の個室を探せ。", 3.5f);
            }
        }

        public string GetPrompt() => "[E] 戸棚を調べる";
        public float GetProgress01() => -1f;
    }
}
