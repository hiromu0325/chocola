using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// 現在シーンに入っている全フレーバーテキストを、Excelで開けるTSVとして書き出す（移行の第一歩）。
    ///
    /// ・短い文（見出し・名前・選択肢）はセルに直接
    /// ・長い本文は 進行/文書/ja/&lt;id&gt;.txt に書き出し、セルには "@file:&lt;id&gt;" を置く
    ///   → 長文は今まで通りテキストエディタで書ける。Excelのセルに長文を詰め込まない
    /// ・書き出したTSVをExcelで開き、名前を付けて GameText.xlsx に保存すれば原本になる
    ///
    /// 差し替え作業は「ja列（または@fileの本文）を上書きするだけ」。IDは触らない。
    /// </summary>
    public static class GameTextExporter
    {
        /// <summary>この長さを超える／改行を含む文面は個別のtxtに逃がす</summary>
        private const int InlineLimit = 40;
        /// <summary>資料ウィンドウに収まる目安（実測: 約33字×14行）。超えたら注意列に印を付ける</summary>
        private const int BodyCharBudget = 33 * 14;

        private class Row
        {
            public string id, kind, room, speaker, text;
            public string note = "";
        }

        [MenuItem("Tools/EscapePrototype/Text/現在のテキストを書き出す（章ごとのExcel）")]
        public static void ExportFromMenu()
        {
            string report = Export();
            Debug.Log(report);
            EditorUtility.DisplayDialog("テキスト書き出し", report, "OK");
            string book = Path.Combine(GameTextImporter.RepoRoot(), TextFolderRelative, ExportBookName);
            if (File.Exists(book)) EditorUtility.RevealInFinder(book);
        }

        public const string TextFolderRelative = @"進行\テキスト";
        /// <summary>書き出し先。原本（GameText.xlsx）は絶対に上書きしない</summary>
        public const string ExportBookName = "GameText_書き出し.xlsx";
        public const string ExportFileName = "GameText_書き出し.tsv";
        /// <summary>管理列（言語列より前に並ぶ）</summary>
        private static readonly string[] MetaHeader = { "id", "kind", "room", "speaker", "status", "note" };

        /// <summary>
        /// 書き出し本体（ダイアログを出さない）。章ごとのシートに分けた .xlsx と、
        /// 差分確認用の TSV を出す。
        ///
        /// 【既存の訳を消さない】原本 GameText.xlsx があれば読み込み、
        /// 既に載っているIDはその文面（全言語・status・note）を引き継ぐ。
        /// シーンにしか無い新しいIDだけを、組み込み文を入れた新規行として足す。
        /// </summary>
        public static string Export()
        {
            var rows = Collect(out int roomCount);
            if (rows.Count == 0)
                return "テキストが見つかりません。ループ回廊のシーンを開いてから実行してください。";

            string outDir = Path.Combine(GameTextImporter.RepoRoot(), TextFolderRelative);
            Directory.CreateDirectory(outDir);
            string docDir = Path.Combine(GameTextImporter.RepoRoot(), @"進行\文書\ja");
            Directory.CreateDirectory(docDir);

            // ---- 既存の原本を読む（あれば） ----
            var existing = LoadExistingBook(out var languages, out string existingNote);
            if (languages == null || languages.Length == 0) languages = new[] { "ja", "en" };
            int jaCol = Array.IndexOf(languages, "ja");
            if (jaCol < 0) jaCol = 0;

            // ---- 行を組み立てる ----
            int fileCount = 0, overBudget = 0, kept = 0, added = 0;
            var built = new List<(Row row, string[] cells)>();
            foreach (var r in rows)
            {
                var cells = new string[MetaHeader.Length + languages.Length];
                cells[0] = r.id; cells[1] = r.kind; cells[2] = r.room; cells[3] = r.speaker;

                if (existing != null && existing.TryGetValue(r.id, out var old))
                {
                    // 既に原本にある行 → 文面も status も note もそのまま残す
                    cells[4] = old.status; cells[5] = old.note;
                    for (int i = 0; i < languages.Length; i++)
                        cells[MetaHeader.Length + i] = i < old.values.Length ? old.values[i] : "";
                    kept++;
                }
                else
                {
                    // 新しいID → 組み込み文を ja に入れる。長文は個別txtへ逃がす
                    string cell = r.text ?? "";
                    if (cell.Contains("\n") || cell.Length > InlineLimit)
                    {
                        string name = SafeFileName(r.id);
                        File.WriteAllText(Path.Combine(docDir, name + ".txt"), cell, new UTF8Encoding(true));
                        cell = "@file:" + name;
                        fileCount++;
                    }
                    if ((r.text?.Length ?? 0) > BodyCharBudget) { r.note += "（長い:要確認）"; overBudget++; }
                    cells[4] = "仮";
                    cells[5] = (existing != null ? "★新規 " : "") + r.note;
                    cells[MetaHeader.Length + jaCol] = cell;
                    added++;
                }
                built.Add((r, cells));
            }

            // ---- 章ごとのシートに分ける ----
            var header = new List<string>(MetaHeader);
            header.AddRange(languages);
            var widths = new List<double> { 34, 7, 13, 9, 7, 18 };
            for (int i = 0; i < languages.Length; i++) widths.Add(52);

            var sheets = new List<XlsxWriter.Sheet>();
            var counts = new List<string>();
            for (int ch = 0; ch <= StoryScript.ChapterTabNames.Length; ch++)
            {
                bool common = ch == StoryScript.ChapterTabNames.Length;   // 最後は部屋に属さない定型文
                var sheetRows = new List<string[]> { header.ToArray() };
                foreach (var (row, cells) in built)
                {
                    int rowCh = string.IsNullOrEmpty(row.room) ? -1 : StoryScript.ChapterOf(row.room);
                    if (common ? rowCh >= 0 : rowCh != ch) continue;
                    sheetRows.Add(cells);
                }
                if (sheetRows.Count <= 1) continue;
                string name = common ? "共通" : StoryScript.ChapterTabNames[ch];
                sheets.Add(new XlsxWriter.Sheet { Name = name, Rows = sheetRows, ColumnWidths = widths.ToArray() });
                counts.Add($"{name} {sheetRows.Count - 1}件");
            }

            string bookPath = Path.Combine(outDir, ExportBookName);
            XlsxWriter.Write(bookPath, sheets);

            // 差分が見えるようTSVも出す（Gitで中身の変化を追える。取り込みにも使える）
            var tsv = new StringBuilder();
            tsv.Append(string.Join("\t", header)).Append('\n');
            foreach (var (_, cells) in built)
                tsv.Append(string.Join("\t", Array.ConvertAll(cells, c => (c ?? "").Replace("\n", "\\n").Replace("\t", " ")))).Append('\n');
            string tsvPath = Path.Combine(outDir, ExportFileName);
            File.WriteAllText(tsvPath, tsv.ToString(), new UTF8Encoding(true));

            return
                "書き出し完了\n\n" +
                $"　Excel: {bookPath}\n　　{string.Join(" / ", counts)}\n" +
                $"　　計 {rows.Count}件（部屋 {roomCount}）　言語 {string.Join(", ", languages)}\n" +
                (existing != null
                    ? $"　　原本から引き継ぎ {kept}件／新規 {added}件（note列に★新規）\n"
                    : "　　（原本がまだ無いので、全件を新規として書き出しました）\n") +
                $"　本文txt: {docDir}　{fileCount}件（長文。セルは @file: 参照）\n" +
                (overBudget > 0 ? $"　※資料ウィンドウに収まらない恐れ: {overBudget}件（note列に印）\n" : "") +
                existingNote +
                $"\n原本（{GameTextImporter.DefaultBookRelative}）は上書きしていません。\n" +
                "この書き出しを原本として使う場合は、名前を GameText.xlsx に変えてください。";
        }

        /// <summary>原本 GameText.xlsx を読み、id → （各言語の文面・status・note）にする</summary>
        private static Dictionary<string, (string[] values, string status, string note)> LoadExistingBook(
            out string[] languages, out string note)
        {
            languages = null; note = "";
            string path = GameTextImporter.DefaultBookPath();
            if (!File.Exists(path)) return null;
            try
            {
                var rows = GameTextImporter.ReadSheet(path);
                if (rows.Count < 2) return null;
                var header = Array.ConvertAll(rows[0], h => (h ?? "").Replace("\uFEFF", "").Trim());
                int idCol = Array.FindIndex(header, h => string.Equals(h, "id", StringComparison.OrdinalIgnoreCase));
                if (idCol < 0) { note = "　※原本に id 列が無いので引き継げませんでした\n"; return null; }
                int statusCol = Array.FindIndex(header, h => string.Equals(h, "status", StringComparison.OrdinalIgnoreCase));
                int noteCol = Array.FindIndex(header, h => string.Equals(h, "note", StringComparison.OrdinalIgnoreCase));

                var meta = new HashSet<string>(MetaHeader, StringComparer.OrdinalIgnoreCase) { "memo", "max", "種別", "部屋", "話者", "状態", "備考" };
                var langCols = new List<(string lang, int col)>();
                for (int c = 0; c < header.Length; c++)
                    if (!string.IsNullOrEmpty(header[c]) && !meta.Contains(header[c])) langCols.Add((header[c], c));
                if (langCols.Count == 0) { note = "　※原本に言語列が無いので引き継げませんでした\n"; return null; }
                languages = langCols.ConvertAll(l => l.lang).ToArray();

                var map = new Dictionary<string, (string[], string, string)>(StringComparer.Ordinal);
                for (int r = 1; r < rows.Count; r++)
                {
                    var row = rows[r];
                    string id = idCol < row.Length ? (row[idCol] ?? "").Replace("\uFEFF", "").Trim() : null;
                    if (string.IsNullOrEmpty(id) || id.StartsWith("#") || map.ContainsKey(id)) continue;
                    var vals = new string[langCols.Count];
                    for (int i = 0; i < langCols.Count; i++)
                        vals[i] = langCols[i].col < row.Length ? row[langCols[i].col] : "";
                    map[id] = (vals,
                        statusCol >= 0 && statusCol < row.Length ? row[statusCol] : "",
                        noteCol >= 0 && noteCol < row.Length ? row[noteCol] : "");
                }
                return map;
            }
            catch (Exception e)
            {
                note = $"　※原本を読めませんでした（{e.Message}）。引き継ぎ無しで書き出します\n";
                return null;
            }
        }

        [MenuItem("Tools/EscapePrototype/Text/差し替え状況を確認する")]
        public static void ReportFromMenu()
        {
            string r = Report();
            Debug.Log(r);
            EditorUtility.DisplayDialog("差し替え状況", r, "OK");
        }

        /// <summary>突き合わせ本体（ダイアログを出さない）</summary>
        public static string Report()
        {
            var rows = Collect(out _);
            var table = AssetDatabase.LoadAssetAtPath<GameTextTable>(GameTextImporter.AssetPath);
            if (table == null)
                return "GameText.asset がまだありません。先に『テキストを取り込む』を実行してください。";
            table.BuildIndex();
            var inTable = new HashSet<string>(table.AllIds(), StringComparer.Ordinal);
            var inScene = new HashSet<string>(rows.Select(r => r.id), StringComparer.Ordinal);

            var missing = rows.Where(r => !inTable.Contains(r.id)).Select(r => r.id).ToList();
            var unused = inTable.Where(id => !inScene.Contains(id) && !id.StartsWith("ui/")).ToList();

            var sb = new StringBuilder();
            sb.Append($"シーン内のテキスト: {rows.Count}件\n表に載っている: {inTable.Count}件\n言語: {string.Join(", ", table.Languages)}\n\n");
            for (int li = 0; li < table.Languages.Length; li++)
            {
                int filled = table.Entries.Count(e => e.values != null && li < e.values.Length && !string.IsNullOrEmpty(e.values[li]));
                sb.Append($"　[{table.Languages[li]}] 入力済み {filled}/{table.Entries.Count}\n");
            }
            if (missing.Count > 0)
                sb.Append($"\n★表に無い（組み込み文のまま） {missing.Count}件:\n　{string.Join("\n　", missing.Take(15))}\n");
            if (unused.Count > 0)
                sb.Append($"\n★シーンで使われていない {unused.Count}件:\n　{string.Join("\n　", unused.Take(15))}\n");
            if (missing.Count == 0 && unused.Count == 0) sb.Append("\n過不足なし。");
            return sb.ToString();
        }

        /// <summary>
        /// コード側の定型文（トースト・操作プロンプト）。シーンを歩いても拾えないのでここに列挙する。
        /// {0} などは差し込み位置。翻訳時も同じ数だけ残すこと。
        /// ※ここを増やしたらコード側の GameText.Get(GameText.UiKey(...), 既定文) と対にする
        /// </summary>
        private static readonly (string key, string text)[] UiTexts =
        {
            ("pickup",            "『{0}』を手に入れた{1}"),
            ("filed",             "『{0}』を手帳に綴じた"),
            ("echo_seen",         "残響を見た──『{0}』を手帳に記録した"),
            ("need_notebook",     "書き留めるものがない……手帳を探そう"),
            ("prompt.doc",        "[E] {0}を調べる"),
            ("prompt.doc_done",   "[E] {0}（記録済み）"),
            ("prompt.no_notebook", "{0}（書き留めるものがない）"),
            ("prompt.lock",       "[E] {0}を操作する"),
            ("prompt.lock_done",  "[E] {0}（解決済み）"),
            ("lock_done",         "{0}（解決済み）"),
            ("lock_solved",       "{0}を解いた"),
            ("wrong",             "違うようだ。"),
            ("wrong.flagged",     "　──手帳に付箋を立てた。関係のある記録があるはずだ"),
            ("wrong.reread",      "　──手帳の付箋を読み直そう"),
            ("wrong.unread",      "　──まだ読んでいない資料があるのかもしれない"),
            ("step_of",           "\n\n手順 {0} / {1}"),
        };

        // ============================== 収集 ==============================

        /// <summary>シーン内の全フレーバーテキストをID付きで集める（書き出しと突き合わせで共用）</summary>
        private static List<Row> Collect(out int roomCount)
        {
            var rows = new List<Row>();
            var rooms = UnityEngine.Object.FindObjectsByType<LoopRoomRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OrderBy(r => r.UnlockStage).ToList();
            roomCount = rooms.Count;

            foreach (var room in rooms)
            {
                string rk = GameText.RoomKey(room.Id);
                Add(rows, rk + ".name", "room", room.Id, "", room.DisplayName);
                var chapter = StoryScript.ChapterLabel(room.Id);
                if (!string.IsNullOrEmpty(chapter)) Add(rows, rk + ".chapter", "room", room.Id, "", chapter);

                // 資料・おもちゃ
                foreach (var f in room.GetComponentsInChildren<LoopFindable>(true))
                {
                    if (string.IsNullOrEmpty(f.Id)) continue;
                    string k = GameText.DocKey(room.Id, f.Id);
                    string kind = f.Id == "toy" ? "toy" : "doc";
                    Add(rows, k + ".name", kind, room.Id, "", f.DisplayName);
                    Add(rows, k + ".title", kind, room.Id, "", f.NoteTitle);
                    Add(rows, k + ".body", kind, room.Id, "", f.NoteBody);
                    Add(rows, k + ".hint", kind, room.Id, "", f.PickupHint);
                }

                // 残響（台詞は音声なのでテキストは記録文のみ）
                foreach (var e in room.GetComponentsInChildren<EchoScene>(true))
                {
                    if (string.IsNullOrEmpty(e.EchoId)) continue;
                    string k = GameText.EchoKey(e.EchoId);
                    Add(rows, k + ".title", "echo", room.Id, "", e.NoteTitle);
                    Add(rows, k + ".body", "echo", room.Id, "", e.NoteBody);
                }

                // 装置（問題文・成果文・失敗時の文言・選択肢）
                foreach (var l in room.GetComponentsInChildren<LoopLockBase>(true))
                {
                    if (string.IsNullOrEmpty(l.Id)) continue;
                    string k = GameText.LockKey(room.Id, l.Id);
                    Add(rows, k + ".name", "lock", room.Id, "", l.DisplayName);
                    Add(rows, k + ".success_title", "lock", room.Id, "", l.SuccessNoteTitle);
                    Add(rows, k + ".success_body", "lock", room.Id, "", l.SuccessNoteBody);
                    Add(rows, k + ".require", "lock", room.Id, "", l.RequireMessage);

                    switch (l)
                    {
                        case LoopCodeLock c:
                            Add(rows, k + ".title", "lock", room.Id, "", c.Title);
                            Add(rows, k + ".body", "lock", room.Id, "", c.Body);
                            break;
                        case LoopChoiceLock c:
                            Add(rows, k + ".title", "lock", room.Id, "", c.Title);
                            Add(rows, k + ".body", "lock", room.Id, "", c.Body);
                            Add(rows, k + ".notenough", "lock", room.Id, "", c.NotEnoughMessage);
                            for (int i = 0; i < (c.Options?.Length ?? 0); i++)
                                Add(rows, $"{k}.option.{i}", "lock", room.Id, "", c.Options[i]);
                            break;
                        case LoopSequenceLock c:
                            Add(rows, k + ".title", "lock", room.Id, "", c.Title);
                            Add(rows, k + ".body", "lock", room.Id, "", c.Body);
                            for (int i = 0; i < (c.Steps?.Length ?? 0); i++)
                                Add(rows, $"{k}.step.{i}", "lock", room.Id, "", c.Steps[i]);
                            break;
                        case LoopCircuitLock c:
                            Add(rows, k + ".title", "lock", room.Id, "", c.Title);
                            Add(rows, k + ".body", "lock", room.Id, "", c.Body);
                            break;
                        case LoopAuditLock c:
                            Add(rows, k + ".title", "lock", room.Id, "", c.Title);
                            Add(rows, k + ".body", "lock", room.Id, "", c.Body);
                            for (int i = 0; i < (c.Rows?.Length ?? 0); i++)
                                Add(rows, $"{k}.row.{i}", "lock", room.Id, "", c.Rows[i]);
                            break;
                    }
                }
            }

            // コード側の定型文
            foreach (var (key, text) in UiTexts)
                Add(rows, GameText.UiKey(key), "ui", "", "", text);
            return rows;
        }

        private static void Add(List<Row> rows, string id, string kind, string room, string speaker, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (rows.Any(r => r.id == id)) return;   // 同IDは1回だけ
            rows.Add(new Row { id = id, kind = kind, room = room, speaker = speaker, text = text });
        }

        private static string SafeFileName(string id)
        {
            var sb = new StringBuilder();
            foreach (char c in id) sb.Append(c == '/' ? '_' : Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
