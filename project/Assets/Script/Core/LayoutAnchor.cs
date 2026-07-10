using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// レイアウト保存の対象マーカー。
    /// このコンポーネントが付いたオブジェクトは
    /// Tools > EscapePrototype > Save Layout で位置・回転・スケールが
    /// Assets/EscapePrototype/Layout/layout.json に保存され、
    /// シーン再構築（Build Prototype Scene）時に自動で復元される。
    /// つまり「エディタで動かす → Save Layout」だけで配置変更が確定する。
    /// </summary>
    public class LayoutAnchor : MonoBehaviour
    {
        [Tooltip("layout.json 上の識別子（一意）。ビルダーが自動設定する")]
        public string Id;
    }
}
