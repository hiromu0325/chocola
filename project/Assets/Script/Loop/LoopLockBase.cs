using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ループ回廊のギミック共通部。方針「操作は簡単、答えは資料の中」。
    /// ・正解すると LoopFindable と同じく "その部屋の必須ID" として発見扱いになる
    ///   （＝資料と同列に、部屋の完了条件へ組み込める）
    /// ・誤答にペナルティは無い。代わりに根拠資料（HintDocId）へ付箋を立て、
    ///   手帳へ戻す。答えそのものは絶対に出さない
    /// ・正解時は手帳に「成果」を1件追記する（SuccessNoteTitle/Body）
    /// </summary>
    public abstract class LoopLockBase : MonoBehaviour, IInteractable, IPromptProvider
    {
        public string RoomId;
        [Tooltip("部屋内で一意のID（LoopRoomRoot.RequiredFindablesと一致させると進行条件になる）")]
        public string Id;
        public string DisplayName = "装置";
        [Tooltip("誤答時に付箋を立てる手帳エントリID（例: analysis_spec）")]
        public string HintDocId;
        [Tooltip("正解時に手帳へ追記する見出し（空なら追記しない）")]
        public string SuccessNoteTitle;
        [TextArea] public string SuccessNoteBody;
        [Tooltip("先に見つけていないと操作できない必須ID（roomId/id）。空なら無条件")]
        public string RequireFound;
        public string RequireMessage = "まだ、必要なものが揃っていない。";

        protected float _lastCallTime = -10f;
        protected int _wrongCount;

        public bool Solved => LoopProgress.IsFound(RoomId, Id);
        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;
            if (PuzzleUI.Instance == null || PuzzleUI.Instance.IsOpen || PuzzleUI.Instance.BlockReopen) return;
            if (LoopPuzzleUI.Instance != null && LoopPuzzleUI.Instance.IsOpen) return;

            if (Solved) { OnAlreadySolved(); return; }

            if (!string.IsNullOrEmpty(RequireFound))
            {
                var parts = RequireFound.Split('/');
                if (parts.Length == 2 && !LoopProgress.IsFound(parts[0], parts[1]))
                {
                    ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 0.5f);
                    ToastUI.Show(RequireMessage);
                    return;
                }
            }
            Begin();
        }

        /// <summary>UIを開いて挑戦を始める</summary>
        protected abstract void Begin();

        /// <summary>解決済みで触ったとき（既定: 成果の資料を読み返す）</summary>
        protected virtual void OnAlreadySolved()
        {
            if (!string.IsNullOrEmpty(SuccessNoteTitle))
                PuzzleUI.Instance.ShowDocument(SuccessNoteTitle, SuccessNoteBody);
            else
                ToastUI.Show($"{DisplayName}（解決済み）");
        }

        /// <summary>誤答：ペナルティ無し。根拠資料へ付箋を立てて手帳へ戻す</summary>
        protected void Wrong(string message = null)
        {
            _wrongCount++;
            ProceduralAudio.PlayAt(ProceduralAudio.Beep(), transform.position, 0.6f);
            bool flagged = Notebook.Flag(HintDocId);
            string msg = message ?? "違うようだ。";
            if (Notebook.Contains(HintDocId))
                msg += flagged ? "　──手帳に付箋を立てた。関係のある記録があるはずだ" : "　──手帳の付箋を読み直そう";
            else
                msg += "　──まだ読んでいない資料があるのかもしれない";
            ToastUI.Show(msg);
        }

        /// <summary>正解：発見扱い＋手帳に成果を追記</summary>
        protected void Succeed(string toast = null)
        {
            ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 0.8f);
            Notebook.Unflag(HintDocId);
            if (!string.IsNullOrEmpty(SuccessNoteTitle))
                Notebook.Add($"{RoomId}_{Id}", SuccessNoteTitle, SuccessNoteBody);
            ToastUI.Show(toast ?? $"{DisplayName}を解いた");
            LoopProgress.NotifyFound(RoomId, Id);
            OnSolved();
        }

        /// <summary>正解後の見た目の更新など</summary>
        protected virtual void OnSolved() { }

        public virtual string GetPrompt() => Solved ? $"[E] {DisplayName}（解決済み）" : $"[E] {DisplayName}を操作する";
        public float GetProgress01() => -1f;
    }
}
