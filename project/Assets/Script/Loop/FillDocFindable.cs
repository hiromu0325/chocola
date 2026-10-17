using StarterAssets;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 部屋に置いた黒塗りの資料。調べると実物を見られ（黒塗りの所は ■）、手帳のメモのボードに置かれる。
    /// 資料の一覧には綴じない（解いた時に全文を綴じる）。解く前はマーカーを引けない。
    /// ※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    public class FillDocFindable : MonoBehaviour, IInteractable, IPromptProvider
    {
        public string RoomId;
        public string Id;
        public string DisplayName = "黒塗りの資料";

        private float _lastCall = -10f;

        private FillDocDef Def => FillDocs.Get(RoomId, Id);
        public bool Found => LoopProgress.IsFound(RoomId, Id);
        private string Name => GameText.Get(GameText.DocKey(RoomId, Id) + ".name", DisplayName);

        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        public void OnInteract()
        {
            if (Time.time - _lastCall < 0.25f) return;
            _lastCall = Time.time;
            var d = Def;
            if (d == null) return;

            if (!LoopProgress.NotebookOwned)
            {
                ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 0.5f);
                ToastUI.Show(GameText.Get(GameText.UiKey("need_notebook"), "書き留めるものがない……手帳を探そう"));
                return;
            }
            if (InspectView.IsOpen || NotebookUI.IsOpen) return;

            bool solved = FillDocs.IsSolved(d);
            if (InspectView.Instance != null)
                InspectView.Instance.Open(new InspectView.Request
                {
                    EntryId = d.EntryId, RoomId = RoomId, DocId = Id,
                    Title = d.TitleText,
                    Body = solved ? FillDocs.Full(d) : FillDocs.Redacted(d),
                    Info = DocCatalog.Get(RoomId, Id),
                    NoMarker = !solved,   // 解く前の文には線を引かない（黒塗りの所を書き写さない）
                });

            if (Found) return;
            ProceduralAudio.PlayAt(ProceduralAudio.Solve(), transform.position, 0.7f);
            LoopProgress.NotifyFound(RoomId, Id);
            FillDocs.NotifyFound(d);
            ToastUI.Show(string.Format(GameText.Get(GameText.UiKey("fill.found"), "『{0}』をメモのボードに置いた　──黒塗りの所に、言葉を当てはめられそうだ"), d.TitleText));
        }

        public string GetPrompt()
        {
            if (!LoopProgress.NotebookOwned)
                return string.Format(GameText.Get(GameText.UiKey("prompt.no_notebook"), "{0}（書き留めるものがない）"), Name);
            return Found
                ? string.Format(GameText.Get(GameText.UiKey("prompt.doc_done"), "[E] {0}（記録済み）"), Name)
                : string.Format(GameText.Get(GameText.UiKey("prompt.doc"), "[E] {0}を調べる"), Name);
        }

        public float GetProgress01() => -1f;
    }
}
