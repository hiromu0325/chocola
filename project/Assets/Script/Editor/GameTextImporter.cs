using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// Excel（.xlsx）／TSV のテキストシートを読み、実行時用の Resources/GameText.asset を生成する。
    ///
    /// 【シートの形】1行目が見出し。id 列は必須。
    ///   id | kind | room | speaker | status | note | ja | en | …
    ///   ・id 以外の既知の管理列（kind/room/speaker/status/note/max）は取り込まない
    ///   ・それ以外の見出しは「言語コードの列」として扱う → 列を足すだけで言語が増える
    ///   ・セルが "@file:名前" なら 進行/文書/&lt;言語&gt;/名前.txt（無ければ 進行/文書/名前.txt）を本文にする
    ///   ・複数シートは全部読んで連結する（章ごとにシートを分けられる）
    ///
    /// 外部ライブラリは使わない（.xlsx は zip + XML なので ZipArchive と XDocument で読む）。
    /// </summary>
    public static class GameTextImporter
    {
        public const string AssetPath = "Assets/Resources/GameText.asset";
        /// <summary>Excel原本の既定の場所（リポジトリ直下からの相対）</summary>
        public const string DefaultBookRelative = @"進行\テキスト\GameText.xlsx";
        public const string DocFolderRelative = @"進行\文書";

        /// <summary>取り込まない管理列（小文字で比較）</summary>
        private static readonly HashSet<string> MetaColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "id", "kind", "room", "speaker", "status", "note", "memo", "max", "種別", "部屋", "話者", "状態", "備考" };

        [MenuItem("Tools/EscapePrototype/Text/テキストを取り込む（Excel → GameText.asset）")]
        public static void ImportFromMenu()
        {
            string path = DefaultBookPath();
            if (!File.Exists(path))
            {
                path = EditorUtility.OpenFilePanel("テキストシートを選ぶ", RepoRoot(), "xlsx,tsv,csv,txt");
                if (string.IsNullOrEmpty(path)) return;
            }
            var report = Import(path);
            Debug.Log(report);
            EditorUtility.DisplayDialog("テキスト取り込み", report, "OK");
        }

        [MenuItem("Tools/EscapePrototype/Text/テキストシートを選んで取り込む")]
        public static void ImportPickFromMenu()
        {
            string path = EditorUtility.OpenFilePanel("テキストシートを選ぶ", RepoRoot(), "xlsx,tsv,csv,txt");
            if (string.IsNullOrEmpty(path)) return;
            var report = Import(path);
            Debug.Log(report);
            EditorUtility.DisplayDialog("テキスト取り込み", report, "OK");
        }

        public static string DefaultBookPath() => Path.Combine(RepoRoot(), DefaultBookRelative);
        public static string RepoRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        private static string DocFolder() => Path.Combine(RepoRoot(), DocFolderRelative);

        /// <summary>取り込み本体。結果レポート（人が読む文字列）を返す</summary>
        public static string Import(string sheetPath)
        {
            List<string[]> rows;
            try { rows = ReadSheet(sheetPath); }
            catch (Exception e) { return $"[失敗] シートを読めない: {e.Message}"; }
            if (rows.Count < 2) return "[失敗] シートに行がない（1行目が見出し、2行目以降がデータ）";

            // ---- 見出しを解析 ----
            var header = rows[0];
            for (int i = 0; i < header.Length; i++) header[i] = Clean(header[i]);
            int idCol = Array.FindIndex(header, h => string.Equals(h, "id", StringComparison.OrdinalIgnoreCase));
            if (idCol < 0) return "[失敗] 見出しに『id』列がない";

            var langCols = new List<(string lang, int col)>();
            for (int c = 0; c < header.Length; c++)
            {
                string h = header[c];
                if (string.IsNullOrEmpty(h) || MetaColumns.Contains(h)) continue;
                langCols.Add((h, c));
            }
            if (langCols.Count == 0) return "[失敗] 言語の列が1つも無い（見出しに ja / en などを置く）";

            // ---- 行を取り込む ----
            var entries = new List<GameTextEntry>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var dupes = new List<string>();
            var fileMisses = new List<string>();
            var untranslated = new int[langCols.Count];
            int blankRows = 0;

            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                string id = idCol < row.Length ? Clean(row[idCol]) : null;
                if (string.IsNullOrEmpty(id)) { blankRows++; continue; }
                if (id.StartsWith("#")) continue;                      // コメント行
                if (seen.ContainsKey(id)) { dupes.Add($"{id}（{seen[id] + 1}行目と{r + 1}行目）"); continue; }
                seen[id] = r;

                var values = new string[langCols.Count];
                for (int i = 0; i < langCols.Count; i++)
                {
                    int c = langCols[i].col;
                    string v = c < row.Length ? row[c] : null;
                    v = ResolveFileRef(v, langCols[i].lang, id, fileMisses);
                    values[i] = Normalize(v);
                    if (string.IsNullOrEmpty(values[i])) untranslated[i]++;
                }
                entries.Add(new GameTextEntry { id = id, values = values });
            }

            // ---- ScriptableObject を書き出す ----
            var table = AssetDatabase.LoadAssetAtPath<GameTextTable>(AssetPath);
            bool isNew = table == null;
            if (isNew) table = ScriptableObject.CreateInstance<GameTextTable>();
            table.Languages = langCols.Select(l => l.lang).ToArray();
            table.Entries = entries;
            table.SourceInfo = $"{Path.GetFileName(sheetPath)} / {DateTime.Now:yyyy-MM-dd HH:mm}";
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Resources"));
            if (isNew) AssetDatabase.CreateAsset(table, AssetPath);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ---- レポート ----
            var sb = new StringBuilder();
            sb.Append($"取り込み完了: {entries.Count}件 / 言語 {string.Join(", ", table.Languages)}\n");
            sb.Append($"　出力: {AssetPath}\n　元: {sheetPath}\n");
            for (int i = 0; i < langCols.Count; i++)
                if (untranslated[i] > 0)
                    sb.Append($"　未入力 [{langCols[i].lang}]: {untranslated[i]}件\n");
            if (dupes.Count > 0)
                sb.Append($"　★重複ID {dupes.Count}件（後の行を無視）: {string.Join(" / ", dupes.Take(5))}\n");
            if (fileMisses.Count > 0)
                sb.Append($"　★@file が見つからない {fileMisses.Count}件: {string.Join(" / ", fileMisses.Take(5))}\n");
            if (blankRows > 0) sb.Append($"　（空行 {blankRows} を読み飛ばし）\n");
            return sb.ToString();
        }

        /// <summary>"@file:名前" を 進行/文書/&lt;lang&gt;/名前.txt（無ければ 進行/文書/名前.txt）の中身に置き換える</summary>
        private static string ResolveFileRef(string value, string lang, string id, List<string> misses)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string v = value.Trim();
            if (!v.StartsWith("@file:")) return value;
            string name = v.Substring(6).Trim();
            if (!name.EndsWith(".txt")) name += ".txt";
            string localized = Path.Combine(DocFolder(), lang, name);
            string plain = Path.Combine(DocFolder(), name);
            string path = File.Exists(localized) ? localized : File.Exists(plain) ? plain : null;
            if (path == null) { misses.Add($"{id} → {name}"); return null; }
            return File.ReadAllText(path).TrimEnd();
        }

        /// <summary>BOM・ゼロ幅文字・前後の空白を落とす（見出しやIDの比較用）</summary>
        private static string Clean(string s) =>
            string.IsNullOrEmpty(s) ? s : s.Replace("﻿", "").Replace("​", "").Trim();

        /// <summary>改行を \n に統一。Excelのセル内改行・CRLF の揺れを吸収する</summary>
        private static string Normalize(string s) =>
            string.IsNullOrEmpty(s) ? s : s.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();

        // ============================== シート読み取り ==============================

        public static List<string[]> ReadSheet(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".xlsx") return ReadXlsx(path);
            return ReadDelimited(path, ext == ".csv" ? ',' : '\t');
        }

        private static List<string[]> ReadDelimited(string path, char sep)
        {
            var rows = new List<string[]>();
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                rows.Add(line.Split(sep).Select(c => c.Replace("\\n", "\n")).ToArray());
            return rows;
        }

        // ---- .xlsx（zip + XML）を外部ライブラリ無しで読む ----

        private static readonly XNamespace NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        private static List<string[]> ReadXlsx(string path)
        {
            var rows = new List<string[]>();
            // Excelで開いたままでも読めるよう共有で開く
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var shared = ReadSharedStrings(zip);
                foreach (var sheetPath in SheetPaths(zip))
                {
                    var entry = zip.GetEntry(sheetPath);
                    if (entry == null) continue;
                    using (var s = entry.Open())
                    {
                        var doc = XDocument.Load(s);
                        bool firstRowOfSheet = true;
                        foreach (var row in doc.Descendants(NsMain + "row"))
                        {
                            var cells = new List<string>();
                            foreach (var c in row.Elements(NsMain + "c"))
                            {
                                int col = ColumnIndex((string)c.Attribute("r"));
                                while (cells.Count < col) cells.Add("");
                                cells.Add(CellValue(c, shared));
                            }
                            // 2枚目以降の見出し行は読み飛ばす（章ごとにシートを分けられるように）
                            if (rows.Count > 0 && firstRowOfSheet &&
                                cells.Count > 0 && string.Equals(cells[0]?.Trim(), "id", StringComparison.OrdinalIgnoreCase))
                            { firstRowOfSheet = false; continue; }
                            firstRowOfSheet = false;
                            rows.Add(cells.ToArray());
                        }
                    }
                }
            }
            return rows;
        }

        /// <summary>ブック内の全シートのXMLパスを、ブックでの並び順で返す</summary>
        private static List<string> SheetPaths(ZipArchive zip)
        {
            var result = new List<string>();
            var wb = zip.GetEntry("xl/workbook.xml");
            var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (wb == null || rels == null)
            {
                foreach (var e in zip.Entries)
                    if (e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml"))
                        result.Add(e.FullName);
                result.Sort(StringComparer.Ordinal);
                return result;
            }
            var relMap = new Dictionary<string, string>();
            using (var s = rels.Open())
                foreach (var r in XDocument.Load(s).Root.Elements())
                    relMap[(string)r.Attribute("Id")] = (string)r.Attribute("Target");
            using (var s = wb.Open())
                foreach (var sh in XDocument.Load(s).Descendants(NsMain + "sheet"))
                {
                    string rid = (string)sh.Attribute(NsRel + "id");
                    if (rid == null || !relMap.TryGetValue(rid, out var target)) continue;
                    result.Add(target.StartsWith("/") ? target.TrimStart('/')
                             : target.StartsWith("xl/") ? target : "xl/" + target);
                }
            return result;
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return list;
            using (var s = entry.Open())
                foreach (var si in XDocument.Load(s).Descendants(NsMain + "si"))
                {
                    // <si><t>…</t></si> か、書式付きの <si><r><t>…</t></r><r><t>…</t></r></si>
                    var sb = new StringBuilder();
                    foreach (var t in si.Descendants(NsMain + "t"))
                    {
                        // ルビ（<rPh>）は本文ではないので除く
                        if (t.Parent != null && t.Parent.Name == NsMain + "rPh") continue;
                        sb.Append(t.Value);
                    }
                    list.Add(sb.ToString());
                }
            return list;
        }

        private static string CellValue(XElement c, List<string> shared)
        {
            string t = (string)c.Attribute("t");
            if (t == "s")
            {
                var v = c.Element(NsMain + "v");
                if (v != null && int.TryParse(v.Value, out int i) && i >= 0 && i < shared.Count) return shared[i];
                return "";
            }
            if (t == "inlineStr")
            {
                var isEl = c.Element(NsMain + "is");
                return isEl == null ? "" : string.Concat(isEl.Descendants(NsMain + "t").Select(x => x.Value));
            }
            var val = c.Element(NsMain + "v");
            return val?.Value ?? "";
        }

        /// <summary>セル参照 "AB12" → 列番号（0始まり）</summary>
        private static int ColumnIndex(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return 0;
            int n = 0;
            foreach (char ch in cellRef)
            {
                if (ch >= 'A' && ch <= 'Z') n = n * 26 + (ch - 'A' + 1);
                else if (ch >= 'a' && ch <= 'z') n = n * 26 + (ch - 'a' + 1);
                else break;
            }
            return Math.Max(0, n - 1);
        }
    }
}
