using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 最初の部屋にある陶器人形の棚。残機の数だけ人形が並び、死亡すると1体消える。
    /// 調べると人形の用途（身代わり）を説明するポップアップが出る。
    /// ビルダーが "Dolls" 子オブジェクトの下に人形ビジュアルを人数分作る。
    /// </summary>
    public class DollShelf : MonoBehaviour, IInteractable, IPromptProvider
    {
        [Tooltip("人形ビジュアルの親（子0..N-1が各人形）")]
        public Transform DollsRoot;

        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private void OnEnable()
        {
            Sync();
            GameEvents.OnDollsChanged += HandleDollsChanged;
        }

        private void OnDisable() => GameEvents.OnDollsChanged -= HandleDollsChanged;

        private void HandleDollsChanged(int _) => Sync();

        /// <summary>表示する人形の数を残機に合わせる</summary>
        private void Sync()
        {
            if (DollsRoot == null) return;
            int dolls = GameManager.Instance != null ? GameManager.Instance.Dolls : DollsRoot.childCount;
            for (int i = 0; i < DollsRoot.childCount; i++)
                DollsRoot.GetChild(i).gameObject.SetActive(i < dolls);
        }

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;

            int dolls = GameManager.Instance != null ? GameManager.Instance.Dolls : 0;
            if (PuzzleUI.Instance != null && !PuzzleUI.Instance.IsOpen && !PuzzleUI.Instance.BlockReopen)
                PuzzleUI.Instance.ShowDocument(
                    "陶器の人形",
                    $"棚に白い陶器の人形が並んでいる。残り {dolls} 体。\n\n" +
                    "異形に捕まった時、人形が1体、身代わりに砕ける。\n" +
                    "そのたびに自分はこの部屋で目を覚ます。\n\n" +
                    "全て砕けた後に捕まれば……もう、目は覚めない。");
        }

        public string GetPrompt() => "[E] 人形を調べる";
        public float GetProgress01() => -1f;
    }
}
