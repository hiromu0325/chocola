using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 照合型ギミック：入退室ログの突き合わせ。表から"あり得ない行"にチェックして確定する。
    /// 根拠: 議事録の会議日 × 照合ログ「主任は当日、学会で北海道にいた」。
    /// </summary>
    public class LoopAuditLock : LoopLockBase
    {
        public string Title = "入退室ログ照合";
        [TextArea] public string Body = "";
        public string[] Rows;
        public int[] CorrectRows;

        protected override void Begin()
        {
            if (LoopPuzzleUI.Instance == null) return;
            LoopPuzzleUI.Instance.ShowChecklist(Title, Body, Rows, CorrectRows,
                solved => { if (solved) Succeed("矛盾が確定した"); },
                () => Wrong("その組み合わせでは矛盾にならない。"));
        }

        public static string[] DefaultRows() => new[]
        {
            "04/16 09:02  黒田  第10研究室 入室",
            "04/16 13:40  水野  臨床病棟   入室",
            "04/17 22:15  佐伯  第8研究室  入室",
            "04/18 08:55  水野  臨床病棟   入室",
            "04/18 23:41  主任  第12研究室 入室",
            "04/18 23:58  主任  第12研究室 退室",
            "04/19 00:12  主任  第12研究室 入室",
            "04/19 07:30  黒田  データ管理室 入室",
            "04/19 09:10  佐伯  第8研究室  入室",
            "04/20 18:05  主任  第1研究室  入室",
        };
        public static int[] DefaultCorrect() => new[] { 4, 5, 6 };
    }
}
