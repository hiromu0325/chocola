using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 手帳の表示レイアウトスクリプト。
    /// Resources/MemoBoardLayout.txt に書いた小さなDSLで、エントリの表示順・
    /// 見出し書式・色・空行・非表示を制御できる（文法はデフォルトファイルの
    /// コメント参照）。Lua等の外部インタプリタに依存せず、テキストを編集する
    /// だけで表示を調整できる。ファイルが無ければ既定の見た目で表示する。
    /// </summary>
    public class MemoLayout
    {
        /// <summary>line 命令1つ分（表示1行のテンプレート）</summary>
        public class LineDef
        {
            public string template = "";
            public bool hasColor;
            public Color color;
        }

        /// <summary>[idパターン] ブロック1つ分の表示ルール</summary>
        public class Rule
        {
            public string pattern = "*";
            public string titleTemplate = "【{title}】";
            public Color titleColor = new Color(1f, 0.84f, 0.6f);
            public Color bodyColor = Color.white;
            public bool hide;
            public int blankAfter = 1;
            /// <summary>line 命令の列。1つでもあれば title/body の自動描画を使わず全文をこれで組む</summary>
            public readonly List<LineDef> lines = new List<LineDef>();

            public string FormatTitle(NotebookEntry e) =>
                Expand(titleTemplate, e);

            /// <summary>{title} {id} {body} と vars の {key} を展開する</summary>
            public static string Expand(string template, NotebookEntry e)
            {
                if (string.IsNullOrEmpty(template)) return "";
                string s = template
                    .Replace("{title}", e.title ?? "")
                    .Replace("{id}", e.id ?? "")
                    .Replace("{body}", e.body ?? "");
                if (e.vars != null)
                    foreach (var v in e.vars)
                        if (v != null && !string.IsNullOrEmpty(v.key))
                            s = s.Replace("{" + v.key + "}", v.value ?? "");
                return s;
            }
        }

        public int WrapChars { get; private set; } = 22;

        private readonly List<string> _order = new List<string>();
        private readonly List<Rule> _rules = new List<Rule>();
        private readonly Rule _default = new Rule();

        public const string ResourcePath = "MemoBoardLayout";
        private static MemoLayout _cached;

        public static MemoLayout Get()
        {
            if (_cached != null) return _cached;
            var text = Resources.Load<TextAsset>(ResourcePath);
            _cached = Parse(text != null ? text.text : "");
            return _cached;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _cached = null;

        /// <summary>
        /// DSLを解釈する。行頭 # はコメント（色指定に # を使うため行内コメントは不可）。
        /// [パターン] 以降の行はそのルールに属し、それより前の行は全体設定。
        /// </summary>
        public static MemoLayout Parse(string src)
        {
            var layout = new MemoLayout();
            Rule current = null;

            foreach (var raw in (src ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    current = new Rule { pattern = line.Substring(1, line.Length - 2).Trim() };
                    layout._rules.Add(current);
                    continue;
                }

                int sp = line.IndexOf(' ');
                string key = sp < 0 ? line : line.Substring(0, sp);
                string val = sp < 0 ? "" : line.Substring(sp + 1).Trim();

                if (current == null)
                {
                    switch (key)
                    {
                        case "wrap":
                            if (int.TryParse(val, out int w) && w > 4) layout.WrapChars = w;
                            break;
                        case "order":
                            foreach (var p in val.Split(new[] { ' ', ',' },
                                         StringSplitOptions.RemoveEmptyEntries))
                                layout._order.Add(p);
                            break;
                    }
                }
                else
                {
                    switch (key)
                    {
                        case "title": current.titleTemplate = val; break;
                        case "title-color": TryColor(val, ref current.titleColor); break;
                        case "body-color": TryColor(val, ref current.bodyColor); break;
                        case "blank":
                            if (int.TryParse(val, out int b))
                                current.blankAfter = Mathf.Clamp(b, 0, 5);
                            break;
                        case "hide": current.hide = true; break;
                        case "line":
                        {
                            // line [#RRGGBB] <テンプレート>  … 1表示行を完全指定
                            var def = new LineDef();
                            if (val.Length > 0 && val[0] == '#')
                            {
                                int end = val.IndexOf(' ');
                                string colorTok = end < 0 ? val : val.Substring(0, end);
                                if (ColorUtility.TryParseHtmlString(colorTok, out var c))
                                {
                                    def.hasColor = true;
                                    def.color = c;
                                    val = end < 0 ? "" : val.Substring(end + 1).Trim();
                                }
                            }
                            def.template = val;
                            current.lines.Add(def);
                            break;
                        }
                    }
                }
            }
            return layout;
        }

        private static void TryColor(string v, ref Color target)
        {
            if (ColorUtility.TryParseHtmlString(v, out var c)) target = c;
        }

        /// <summary>id に最初に一致した [パターン] ルール。無ければ既定ルール</summary>
        public Rule RuleFor(string id)
        {
            foreach (var r in _rules)
                if (Match(r.pattern, id)) return r;
            return _default;
        }

        /// <summary>order 指定に従った表示順（未指定のエントリは末尾に元の順で並ぶ・安定）</summary>
        public List<NotebookEntry> SortEntries(IReadOnlyList<NotebookEntry> entries)
        {
            var list = new List<NotebookEntry>(entries);
            if (_order.Count == 0) return list;

            var indexed = new List<(NotebookEntry e, int key, int idx)>(list.Count);
            for (int i = 0; i < list.Count; i++)
                indexed.Add((list[i], OrderIndex(list[i].id), i));
            indexed.Sort((a, b) => a.key != b.key ? a.key.CompareTo(b.key) : a.idx.CompareTo(b.idx));

            var result = new List<NotebookEntry>(indexed.Count);
            foreach (var t in indexed) result.Add(t.e);
            return result;
        }

        private int OrderIndex(string id)
        {
            for (int i = 0; i < _order.Count; i++)
                if (Match(_order[i], id)) return i;
            return int.MaxValue;
        }

        /// <summary>パターン照合：完全一致、末尾 * は前方一致、"*" は全一致</summary>
        private static bool Match(string pattern, string id)
        {
            if (string.IsNullOrEmpty(pattern) || id == null) return false;
            if (pattern == "*") return true;
            if (pattern[pattern.Length - 1] == '*')
                return id.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal);
            return id == pattern;
        }
    }
}
