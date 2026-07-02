using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 社内PC：ID=社員番号 / PW=生年月日 でログイン。
    /// 成功で部署専用ページ（配電盤キーパッド解除コード＋正しい説明書の型番）を表示。
    /// </summary>
    public class PcDesk : MonoBehaviour, IInteractable, IPromptProvider
    {
        [SerializeField] private Renderer _indicator;
        private float _lastCallTime = -10f;
        private string _pendingId;

        public bool CanInteract => PuzzleState.Instance == null || PuzzleState.Instance.PuzzlesEnabled;

        private void Start() => UpdateIndicator();
        private void OnEnable() => GameEvents.OnPcAccessed += UpdateIndicator;
        private void OnDisable() => GameEvents.OnPcAccessed -= UpdateIndicator;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;
            var ps = PuzzleState.Instance;
            if (ps == null || PuzzleUI.Instance == null) return;
            if (PuzzleUI.Instance.IsOpen || PuzzleUI.Instance.BlockReopen) return;

            if (ps.PcAccessed) { ShowMenu(ps); return; }

            // ログイン手順①：社員番号
            PuzzleUI.Instance.ShowKeypad(
                "社内PC ログイン　①社員ID",
                "社員証に記載の社員番号を入力（4桁）",
                4,
                id =>
                {
                    _pendingId = id;
                    // ログイン手順②：パスワード（生年月日）
                    PuzzleUI.Instance.ShowKeypad(
                        "社内PC ログイン　②パスワード",
                        "パスワードは本人の生年月日（8桁　例: 19900101）",
                        8,
                        pw =>
                        {
                            if (ps.TryPcLogin(_pendingId, pw))
                            {
                                ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 1f);
                                ShowMenu(ps);
                            }
                            else
                            {
                                ProceduralAudio.PlayAt(ProceduralAudio.Beep(), transform.position, 0.8f);
                                PuzzleUI.Instance.ShowDocument("ログイン失敗",
                                    "社員番号またはパスワードが違います。\n\n" +
                                    "・社員番号 … 社員証\n" +
                                    "・パスワード … 持ち主の生年月日（人事ファイル）\n\n" +
                                    "番号→部署→顔→氏名→生年月日 の順に照合し直してください。");
                            }
                        });
                });
        }

        /// <summary>ログイン後のメニュー：部署ページ／鍵の貸出記録</summary>
        private void ShowMenu(PuzzleState ps)
        {
            PuzzleUI.Instance.ShowSelection(
                "社内PC　メニュー",
                "閲覧する項目を選択してください。",
                new[] { "部署専用ページ（配電盤の復旧情報）", "配電室 鍵の貸出記録" },
                idx =>
                {
                    if (idx == 0)
                    {
                        PuzzleUI.Instance.ShowDocument($"{ps.TargetDepartment} 専用ページ", ps.BuildDeptPage());
                        string model = ps.ActiveModel != null ? ps.ActiveModel.model : "??";
                        Notebook.Add("pc_dept", "部署ページ（配電盤）",
                            $"キーパッド解除コード: {ps.KeypadPassword}\n復旧は 型番 {model} の説明書を参照");
                    }
                    else if (idx == 1)
                    {
                        PuzzleUI.Instance.ShowDocument("配電室 鍵 貸出記録", ps.BuildKeyLendingRecord());
                        if (ps.KeyHolder != null)
                            Notebook.Add("pc_key", "鍵の貸出記録",
                                $"配電室の鍵: {ps.KeyHolder.name}（{ps.KeyHolder.number}）が貸出中\n保管場所: 2階 本人の個室");
                    }
                });
        }

        private void UpdateIndicator()
        {
            if (_indicator == null) _indicator = GetComponentInChildren<Renderer>();
            if (_indicator == null) return;
            bool on = PuzzleState.Instance != null && PuzzleState.Instance.PcAccessed;
            _indicator.material.color = on ? new Color(0.2f, 0.7f, 0.9f) : new Color(0.1f, 0.15f, 0.2f);
        }

        public string GetPrompt() =>
            (PuzzleState.Instance != null && PuzzleState.Instance.PcAccessed)
                ? "[E] 社内PC（部署ページを表示）"
                : "[E] 社内PCにログイン";
        public float GetProgress01() => -1f;
    }
}
