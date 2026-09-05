using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 照合型ギミック：選択肢から1つ選ぶ（黒田の信条／残響の判定 等）。
    /// RequireNotes に手帳エントリIDを並べると、それらを全部読む（見る）までは
    /// 判断させない（「まだ判断材料が足りない」）。
    /// </summary>
    public class LoopChoiceLock : LoopLockBase
    {
        public string Title = "選択";
        [TextArea] public string Body = "";
        public string[] Options;
        public int CorrectIndex;
        [Tooltip("判断に必要な手帳エントリID（残響の記録など）。揃うまで選ばせない")]
        public string[] RequireNotes;
        public string NotEnoughMessage = "まだ、判断できるだけの材料が無い。";

        protected override void Begin()
        {
            if (RequireNotes != null)
                foreach (var n in RequireNotes)
                    if (!string.IsNullOrEmpty(n) && !Notebook.Contains(n))
                    {
                        ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 0.5f);
                        ToastUI.Show(NotEnoughMessage);
                        return;
                    }

            PuzzleUI.Instance.ShowSelection(Title, Body, Options, idx =>
            {
                if (idx < 0) return;   // 中止
                if (idx == CorrectIndex) Succeed();
                else Wrong("……違う。");
            });
        }
    }
}
