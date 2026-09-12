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

        [MenuItem("Tools/EscapePrototype/Text/現在のテキストを書き出す（TSV）")]
        public static void ExportFromMenu()
        {
            string report = Export();
            Debug.Log(report);
            EditorUtility.DisplayDialog("テキスト書き出し", report, "OK");
            string tsv = Path.Combine(GameTextImporter.RepoRoot(), @"進行\テキスト", ExportFileName);
            if (File.Exists(tsv)) EditorUtility.RevealInFinder(tsv);
        }

        public const string ExportFileName = "GameText_書き出し.tsv";

        /// <summary>書き出し本体（ダイアログを出さない。自動テスト・バッチから呼べる）</summary>
        public static string Export()
        {
            var rows = Collect(out int roomCount);
            if (rows.Count == 0)
                return "テキストが見つかりません。ループ回廊のシーンを開いてから実行してください。";

            string outDir = Path.Combine(GameTextImporter.RepoRoot(), @"進行\テキスト");
            Directory.CreateDirectory(outDir);
            string tsvPath = Path.Combine(outDir, ExportFileName);
            string docDir = Path.Combine(GameTextImporter.RepoRoot(), @"進行\文書\ja");
            Directory.CreateDirectory(docDir);

            int fileCount = 0, overBudget = 0;
            var sb = new StringBuilder();
            sb.Append("id\tkind\troom\tspeaker\tstatus\tnote\tja\ten\n");
            foreach (var r in rows)
            {
                string cell = r.text ?? "";
                bool longText = cell.Contains("\n") || cell.Length > InlineLimit;
                if (longText)
                {
                    string name = SafeFileName(r.id);
                    File.WriteAllText(Path.Combine(docDir, name + ".txt"), cell, new UTF8Encoding(true));
                    cell = "@file:" + name;
                    fileCount++;
                }
                if ((r.text?.Length ?? 0) > BodyCharBudget) { r.note += "（長い:要確認）"; overBudget++; }
                sb.Append($"{r.id}\t{r.kind}\t{r.room}\t{r.speaker}\t仮\t{r.note}\t{cell}\t\n");
            }
            // ExcelがUTF-8と判別できるようBOM付きで書く
            File.WriteAllText(tsvPath, sb.ToString(), new UTF8Encoding(true));

            return
                $"書き出し完了\n\n" +
                $"　シート: {tsvPath}\n　　{rows.Count}件（部屋 {roomCount}）\n" +
                $"　本文txt: {docDir}\n　　{fileCount}件（長文はこちら。セルは @file: 参照）\n" +
                (overBudget > 0 ? $"　※資料ウィンドウに収まらない恐れ: {overBudget}件（note列に印）\n" : "") +
                $"\nこのTSVをExcelで開き『GameText.xlsx』として保存すると原本になります。";
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
