using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 日本語の折り返し位置を決めて改行を入れる（Text は文字単位で機械的に折り返すので、語の途中で切れる）。
    /// 行の長さをそろえつつ、読点・句点の後 → 空白・矢印の前後 → ひらがなから漢字・カタカナへ変わる所（文節の切れ目）
    /// の順に切れ目を選ぶ。行頭に来てはいけない文字（、。」ー 小さい仮名など）・行末に来てはいけない文字（「（）と、
    /// 英数字の途中では切らない
    /// </summary>
    public static class JaLineBreak
    {
        private const string NoStart = "、。，．・：；？！ー―…‥」』）］｝〕〉》】ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ々ゝゞヽヾ,.:;?!)]}%";
        private const string NoEnd = "「『（［｛〔〈《【([{";
        private const string AfterPunct = "、。，．！？」』）";

        /// <summary>1文字の幅（文字の大きさ単位。全角＝1、半角＝0.55）</summary>
        public static float Em(char c)
        {
            if (c == ' ') return 0.3f;
            return c < 0x2000 || (c >= 0xFF61 && c <= 0xFF9F) ? 0.55f : 1f;
        }

        /// <summary>maxEm（文字の大きさ単位）に収まるよう、自然な所で改行を入れた文を返す</summary>
        public static string Wrap(string s, float maxEm)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            int n = s.Length;
            var cum = new float[n + 1];   // cum[i] = 先頭から i 文字の幅
            for (int i = 0; i < n; i++) cum[i + 1] = cum[i] + Em(s[i]);
            if (cum[n] <= maxEm) return s;

            var lines = new List<string>();
            int start = 0;
            while (cum[n] - cum[start] > maxEm)
            {
                float rest = cum[n] - cum[start];
                int count = Mathf.CeilToInt(rest / maxEm);
                float target = rest / count;   // 残りを均等に分けた長さ
                int best = -1;
                float bestScore = float.MaxValue;
                for (int i = start + 1; i < n; i++)
                {
                    float w = cum[i] - cum[start];
                    if (w > maxEm) break;
                    if (!CanBreak(s, i)) continue;
                    if (cum[n] - cum[i] > maxEm * (count - 1) + 0.01f) continue;   // 残りが収まらなくなる所では切らない
                    float score = Priority(s, i) * 1.5f + Mathf.Abs(w - target) * 0.6f;
                    if (score < bestScore) { bestScore = score; best = i; }
                }
                if (best < 0)
                {
                    // どこも条件に合わない：入るだけ入れて、禁則だけは守る
                    best = start + 1;
                    for (int i = start + 1; i < n && cum[i] - cum[start] <= maxEm; i++)
                        if (CanBreak(s, i)) best = i;
                }
                lines.Add(s.Substring(start, best - start).TrimEnd(' ', '　'));
                start = best;
                while (start < n && (s[start] == ' ' || s[start] == '　')) start++;
            }
            if (start < n) lines.Add(s.Substring(start));
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(lines[i]);
            }
            return sb.ToString();
        }

        /// <summary>s[i-1] と s[i] の間で切ってよいか</summary>
        private static bool CanBreak(string s, int i)
        {
            char a = s[i - 1], b = s[i];
            if (NoStart.IndexOf(b) >= 0 || NoEnd.IndexOf(a) >= 0) return false;
            if (IsAsciiWord(a) && IsAsciiWord(b)) return false;
            return true;
        }

        /// <summary>切れ目の良さ（小さいほど良い）</summary>
        private static float Priority(string s, int i)
        {
            char a = s[i - 1], b = s[i];
            if (AfterPunct.IndexOf(a) >= 0) return 0f;                              // 読点・句点の後
            if (a == ' ' || a == '　' || b == ' ' || b == '　') return 0.5f;         // 空白
            if (a == '→' || a == '⇒') return 0.8f;                                  // 矢印の後
            if (b == '→' || b == '⇒') return 1.5f;
            if (b == '「' || b == '『' || b == '（') return 0.8f;                     // かっこの前
            bool ha = IsHiragana(a), hb = IsHiragana(b);
            if (ha && !hb) return 1f;                                                // ひらがな→漢字など（文節の切れ目）
            if (!ha && hb) return 4f;                                                // 漢字→送り仮名・助詞（語の途中）
            if (ha && hb) return 2.5f;
            return 3f;                                                               // 漢字・カタカナの続き
        }

        private static bool IsHiragana(char c) => c >= 0x3041 && c <= 0x309F;
        private static bool IsAsciiWord(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '%' || c == '.';
    }
}
