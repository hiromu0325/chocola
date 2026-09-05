using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 転記型ギミック：資料に書かれた数字をそのまま入れる（金庫のダイヤル・PCのパスワード等）。
    /// 例）招聘状の日付「2015年3月25日」→ 0325
    /// </summary>
    public class LoopCodeLock : LoopLockBase
    {
        public string Title = "ダイヤル";
        [TextArea] public string Body = "4桁を合わせる";
        public int Length = 4;
        public string Answer = "0000";

        protected override void Begin()
        {
            PuzzleUI.Instance.ShowKeypad(Title, Body, Length, code =>
            {
                if (string.IsNullOrEmpty(code)) return;   // 中止
                if (code == Answer) Succeed();
                else Wrong("番号が違う。");
            });
        }
    }
}
