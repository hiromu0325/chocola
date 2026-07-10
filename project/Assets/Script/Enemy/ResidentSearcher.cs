using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 襲撃者の部屋に「常にいる」襲撃者（雰囲気・ロア用、非殺傷）。
    /// 来訪フェーズ中は本物（EnemySpawner が生成）が部屋を出るため姿を消し、
    /// 終了すると次に来る探索者の姿で再び部屋に立つ。
    /// 閉じ込められたプレイヤーを殺しはしない（仕様：閉じ込め＝死ではない）。
    /// </summary>
    public class ResidentSearcher : MonoBehaviour
    {
        private GameObject _visual;

        private void OnEnable()
        {
            GameEvents.OnGameStarted += Show;
            GameEvents.OnVisitStart += HandleVisitStart;
            GameEvents.OnVisitEnd += HandleVisitEnd;
        }

        private void OnDisable()
        {
            GameEvents.OnGameStarted -= Show;
            GameEvents.OnVisitStart -= HandleVisitStart;
            GameEvents.OnVisitEnd -= HandleVisitEnd;
        }

        private void Start() => Show();

        private void HandleVisitStart(SearcherType t) => Hide();

        /// <summary>来訪終了：本物が部屋に戻り切って（消えて）から常駐の姿を出す</summary>
        private void HandleVisitEnd() => StartCoroutine(ShowWhenSearcherHome());

        private System.Collections.IEnumerator ShowWhenSearcherHome()
        {
            float timeout = 15f;
            while (timeout > 0f && FindFirstObjectByType<SearcherController>() != null)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            Show();
        }

        private void Show()
        {
            Hide();
            var type = PhaseManager.Instance != null ? PhaseManager.Instance.NextSearcherType : SearcherType.Sight;
            // 先に位置確定済みの子を作ってから見た目を組み込む（原点に一瞬出るのを防ぐ）
            _visual = new GameObject("ResidentVisual");
            _visual.transform.SetParent(transform, false);
            _visual.transform.localPosition = Vector3.zero;
            _visual.transform.localRotation = Quaternion.identity;
            EnemySpawner.BuildVisualInto(_visual, type);
        }

        private void Hide()
        {
            if (_visual != null) { Destroy(_visual); _visual = null; }
        }
    }
}
