using System.Collections.Generic;
using System.Text;

namespace EscapeProto
{
    /// <summary>
    /// ルビ（読み仮名）の書き方。青空文庫と同じで、文章の中に直接書く。
    ///   ｜親文字《よみ》   … 「｜」（全角）から「《」までが親文字（仮名・英字が混じる語、親文字の始まりが紛らわしい時）
    ///   漢字《よみ》       … 「《」の直前に続く漢字だけが親文字
    /// 文章は印を付けたまま持ち、表示する時に Parse で「親文字だけの文」と「ルビの位置（親文字だけの文での番号）」に分ける。
    /// ルビを出せない所は Strip で印を外す（GameText.Get は既定で外して返す。印付きは GameText.GetMarked）。
    /// メモの文字番号などは、必ず親文字だけの文で数える。
    /// </summary>
    public static class Ruby
    {
        public struct Span
        {
            public int Start, Length;   // 親文字だけの文での位置
            public string Base, Reading;
        }

        public static bool HasMarkup(string s) => !string.IsNullOrEmpty(s) && s.IndexOf('《') >= 0;

        /// <summary>ルビの印を外す（親文字だけ残す）</summary>
        public static string Strip(string s) => HasMarkup(s) ? Parse(s, null) : s;

        /// <summary>印付きの文 → 親文字だけの文。spans（null 可）にルビの位置を入れる</summary>
        public static string Parse(string s, List<Span> spans)
        {
            spans?.Clear();
            if (!HasMarkup(s)) return s ?? "";
            var sb = new StringBuilder(s.Length);
            int pipeAt = -1;   // 「｜」の位置（sb の番号）。無ければ -1
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '｜' && ClosingOnLine(s, i + 1))
                {
                    pipeAt = sb.Length;
                    continue;
                }
                if (c == '《')
                {
                    int close = s.IndexOf('》', i + 1);
                    int nl = s.IndexOf('\n', i + 1);
                    if (close > i + 1 && (nl < 0 || close < nl))
                    {
                        int start = pipeAt;
                        if (start < 0)
                        {
                            start = sb.Length;
                            while (start > 0 && IsKanji(sb[start - 1])) start--;
                        }
                        int len = sb.Length - start;
                        if (len > 0)
                        {
                            spans?.Add(new Span { Start = start, Length = len, Base = sb.ToString(start, len), Reading = s.Substring(i + 1, close - i - 1) });
                            pipeAt = -1;
                            i = close;
                            continue;
                        }
                    }
                }
                if (c == '\n') pipeAt = -1;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>印付きの文を親文字だけの文と出すルビに分ける。オプションでルビを切っている時はルビを出さない</summary>
        public static string ParseForDisplay(string s, List<Span> spans)
        {
            string b = Parse(s, spans);
            if (!GameSettings.RubyOn) spans?.Clear();
            return b;
        }

        private static bool ClosingOnLine(string s, int from)
        {
            for (int i = from; i < s.Length; i++)
            {
                if (s[i] == '\n') return false;
                if (s[i] == '《') return true;
            }
            return false;
        }

        public static bool IsKanji(char c) =>
            (c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿') ||
            c == '々' || c == '〆' || c == '〇' || c == 'ヵ' || c == 'ヶ';
    }
}
