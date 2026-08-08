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

        public bool Found { get; private set; }

        private float _lastCallTime = -10f;

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        private void Start()
        {
            // セーブ復帰やリスポーン時に発見済み状態を復元
            if (LoopProgress.IsFound(RoomId, Id)) MarkFound(silent: true);
        }

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;

            if (!Found) MarkFound(silent: false);

            // 何度でも読み返せる（内容はPuzzleUIで表示）
            if (PuzzleUI.Instance != null && !string.IsNullOrEmpty(NoteBody) &&
                !PuzzleUI.Instance.IsOpen && !PuzzleUI.Instance.BlockReopen)
                PuzzleUI.Instance.ShowDocument(string.IsNullOrEmpty(NoteTitle) ? DisplayName : NoteTitle, NoteBody);
        }

        private void MarkFound(bool silent)
        {
            Found = true;
            if (Highlight != null && Highlight.sharedMaterial != null)
                Highlight.transform.localScale = Highlight.transform.localScale;   // 見た目の変化は任意

            if (!string.IsNullOrEmpty(NoteTitle))
                Notebook.Add($"{RoomId}_{Id}", NoteTitle, NoteBody);

            if (!silent)
                ProceduralAudio.PlayAt(ProceduralAudio.Unlock(), transform.position, 0.7f);

            LoopProgress.NotifyFound(RoomId, Id);
        }

        public string GetPrompt() => Found ? $"[E] {DisplayName}（記録済み）" : $"[E] {DisplayName}を調べる";
        public float GetProgress01() => -1f;
    }
}
