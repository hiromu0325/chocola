using UnityEngine;

namespace StarterAssets
{
    /// <summary>
    /// インタラクト可能なオブジェクトが実装すべきインターフェース
    /// </summary>
    public interface IInteractable
    {
        void OnInteract();
        bool CanInteract { get; }
    }
}
