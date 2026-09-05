using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 部屋に配置された「見つけるべき情報」。調べると手帳に記録され、
    /// その部屋の必須アイテムを全て見つけると次の部屋が解放される（LoopProgress）。
    /// </summary>
    public class LoopFindable : MonoBehaviour, IInteractable, IPromptProvider
    {
        public string RoomId;
        [Tooltip("部屋内で一意のID（LoopRoomRoot.RequiredFindablesと一致させる）")]
        public string Id;
        [Tooltip("調べたときのプロンプト表示名")]
        public string DisplayName = "資料";
        [Tooltip("手帳に記録するタイトル。空なら記録しない")]
        public string NoteTitle;
        [TextArea] public string NoteBody;
        [Tooltip("見つけると光る（発見済みの目印）")]
        public Renderer Highlight;
        [Tooltip("trueなら拾うとオブジェクトが消える（手帳・懐中電灯などの道具）")]
        public bool DisappearOnPickup;
        [Tooltip("拾得トーストに添える操作ヒント（例: F: 点灯）")]
        public string PickupHint;

        public bool Found { get; private set; }

        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private void Start()
        {
            // セーブ復帰やリスポーン時に発見済み状態を復元
            RefreshFound();
        }

        /// <summary>進行データから発見済み状態を復元（つづきから再開時にも呼ばれる）</summary>
        public void RefreshFound()
        {
            if (!Found && LoopProgress.IsFound(RoomId, Id)) MarkFound(silent: true);
        }

        /// <summary>
        /// 手帳が無くて読めない資料か。
        /// 書き留める手段が無いうちは資料を読ませない（＝先に手帳を拾わせる導線）。
        /// 道具（DisappearOnPickup）と、記録の無いものは対象外。
        /// </summary>
        private bool BlockedByNoNotebook =>
            !DisappearOnPickup && !string.IsNullOrEmpty(NoteTitle) && !LoopProgress.NotebookOwned;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;

            if (BlockedByNoNotebook)
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 0.5f);
                ToastUI.Show("書き留めるものがない……手帳を探そう");
                return;
            }

            if (!Found) MarkFound(silent: false);

            // 何度でも読み返せる（内容はPuzzleUIで表示）
            if (PuzzleUI.Instance != null && !string.IsNullOrEmpty(NoteBody) &&
                !PuzzleUI.Instance.IsOpen && !PuzzleUI.Instance.BlockReopen)
                PuzzleUI.Instance.ShowDocument(string.IsNullOrEmpty(NoteTitle) ? DisplayName : NoteTitle, NoteBody);
        }

        private void MarkFound(bool silent)
        {
            Found = true;

            // 資料は手帳へ綴じる（OnInteract側で手帳所持を保証済み。
            // silent=セーブ復帰時は既に綴じられているので通知だけ出さない）
            bool filed = false;
            if (!string.IsNullOrEmpty(NoteTitle))
                filed = Notebook.Add($"{RoomId}_{Id}", NoteTitle, NoteBody);

            if (!silent)
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 0.7f);

                if (DisappearOnPickup)
                    ToastUI.Show($"『{DisplayName}』を手に入れた" +
                                 (string.IsNullOrEmpty(PickupHint) ? "" : $"　[{PickupHint}]"));
                else if (filed)
                    ToastUI.Show($"『{NoteTitle}』を手帳に綴じた");
            }

            LoopProgress.NotifyFound(RoomId, Id);

            // 道具は拾うと消える（発見状態はLoopProgress側に残るので復元しても消えたまま）
            if (DisappearOnPickup) gameObject.SetActive(false);
        }

        public string GetPrompt()
        {
            if (BlockedByNoNotebook) return $"{DisplayName}（書き留めるものがない）";
            return Found ? $"[E] {DisplayName}（記録済み）" : $"[E] {DisplayName}を調べる";
        }
        public float GetProgress01() => -1f;
    }
}
