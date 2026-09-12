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

        // ---- 表示文はテキスト表（多言語）から引く。表に無ければ上のフィールドをそのまま使う ----
        // IDは部屋Id＋Idから導出するので、シーンに新しいフィールドを持たせる必要が無い
        private string TextKey => GameText.DocKey(RoomId, Id);
        public string Name => GameText.Get(TextKey + ".name", DisplayName);
        public string Title => GameText.Get(TextKey + ".title", NoteTitle);
        public string Body => GameText.Get(TextKey + ".body", NoteBody);
        public string Hint => GameText.Get(TextKey + ".hint", PickupHint);

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
                ToastUI.Show(GameText.Get(GameText.UiKey("need_notebook"), "書き留めるものがない……手帳を探そう"));
                return;
            }

            // 先に資料を開いてから発見扱いにする。
            // 逆順だと「発見→部屋完了→『扉が開いた』ダイアログ」が同じフレームで先に開き、
            // 肝心の資料ウィンドウが表示されない（UiQueueは開いているUIが閉じるまで待つ）
            if (PuzzleUI.Instance != null && !string.IsNullOrEmpty(Body) &&
                !PuzzleUI.Instance.IsOpen && !PuzzleUI.Instance.BlockReopen)
                PuzzleUI.Instance.ShowDocument(string.IsNullOrEmpty(Title) ? Name : Title, Body);

            if (!Found) MarkFound(silent: false);
        }

        /// <summary>「はじめから」用：未発見に戻し、拾って消えたものを元の場所に戻す（終章の断片は隠したまま）</summary>
        public void ResetForNewGame()
        {
            Found = false;
            if (DisappearOnPickup) gameObject.SetActive(Id != "toy");
        }

        private void MarkFound(bool silent)
        {
            Found = true;

            // 資料は手帳へ綴じる（OnInteract側で手帳所持を保証済み。
            // silent=セーブ復帰時は既に綴じられているので通知だけ出さない）
            bool filed = false;
            if (!string.IsNullOrEmpty(Title))
                filed = Notebook.Add($"{RoomId}_{Id}", Title, Body);

            if (!silent)
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 0.7f);

                if (DisappearOnPickup)
                    ToastUI.Show(string.Format(GameText.Get(GameText.UiKey("pickup"), "『{0}』を手に入れた{1}"),
                                 Name, string.IsNullOrEmpty(Hint) ? "" : $"　[{Hint}]"));
                else if (filed)
                    ToastUI.Show(string.Format(GameText.Get(GameText.UiKey("filed"), "『{0}』を手帳に綴じた"), Title));
            }

            LoopProgress.NotifyFound(RoomId, Id);

            // 道具は拾うと消える（発見状態はLoopProgress側に残るので復元しても消えたまま）
            if (DisappearOnPickup) gameObject.SetActive(false);
            // 終章の断片（娘のおもちゃ）は集めた数を知らせる
            if (Id == "toy" && !silent && LoopFinale.Instance != null) LoopFinale.Instance.NotifyToyCollected();
        }

        public string GetPrompt()
        {
            if (BlockedByNoNotebook)
                return string.Format(GameText.Get(GameText.UiKey("prompt.no_notebook"), "{0}（書き留めるものがない）"), Name);
            return Found
                ? string.Format(GameText.Get(GameText.UiKey("prompt.doc_done"), "[E] {0}（記録済み）"), Name)
                : string.Format(GameText.Get(GameText.UiKey("prompt.doc"), "[E] {0}を調べる"), Name);
        }
        public float GetProgress01() => -1f;
    }
}
