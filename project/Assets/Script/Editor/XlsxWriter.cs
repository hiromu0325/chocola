using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// Excelファイル（.xlsx）を外部ライブラリ無しで書き出す最小実装。
    /// .xlsx は zip + XML なので、必要な部品だけを組み立てる。
    ///
    /// ・1ブックに複数シート（章ごとのタブ）
    /// ・文字列は inlineStr で埋める（共有文字列表を作らない＝実装が単純で壊れにくい）
    /// ・1行目は固定（スクロールしても見出しが残る）、列幅も指定する
    /// </summary>
    public static class XlsxWriter
    {
        public class Sheet
        {
            public string Name;
            public List<string[]> Rows = new List<string[]>();
            /// <summary>列幅（文字数の目安）。省略した列は既定幅</summary>
            public double[] ColumnWidths;
        }

        public static void Write(string path, IList<Sheet> sheets)
        {
            if (sheets == null || sheets.Count == 0) throw new ArgumentException("シートが無い");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) File.Delete(path);

            using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var names = UniqueSheetNames(sheets);

                var ct = new StringBuilder();
                ct.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                ct.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
                ct.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
                ct.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
                ct.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
                ct.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
                for (int i = 0; i < sheets.Count; i++)
                    ct.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                ct.Append("</Types>");
                Add(zip, "[Content_Types].xml", ct.ToString());

                Add(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");

                var wb = new StringBuilder();
                wb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                wb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
                wb.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
                for (int i = 0; i < sheets.Count; i++)
                    wb.Append($"<sheet name=\"{Esc(names[i])}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                wb.Append("</sheets></workbook>");
                Add(zip, "xl/workbook.xml", wb.ToString());

                var rels = new StringBuilder();
                rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                for (int i = 0; i < sheets.Count; i++)
                    rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
                rels.Append($"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
                rels.Append("</Relationships>");
                Add(zip, "xl/_rels/workbook.xml.rels", rels.ToString());

                Add(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Yu Gothic\"/></font></fonts>" +
                    "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill>" +
                    "<fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                    "</styleSheet>");

                for (int i = 0; i < sheets.Count; i++)
                    Add(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
            }
        }

        private static string SheetXml(Sheet sheet)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            // 見出し行を固定
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
            sb.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>");
            sb.Append("</sheetView></sheetViews>");
            if (sheet.ColumnWidths != null && sheet.ColumnWidths.Length > 0)
            {
                sb.Append("<cols>");
                for (int c = 0; c < sheet.ColumnWidths.Length; c++)
                    sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{sheet.ColumnWidths[c].ToString(System.Globalization.CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                var row = sheet.Rows[r];
                sb.Append($"<row r=\"{r + 1}\">");
                for (int c = 0; c < row.Length; c++)
                {
                    string v = row[c];
                    if (string.IsNullOrEmpty(v)) continue;   // 空セルは書かない（ファイルが小さくなる）
                    sb.Append($"<c r=\"{ColName(c)}{r + 1}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(v)}</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static void Add(ZipArchive zip, string entryName, string content)
        {
            var e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var s = e.Open())
            using (var w = new StreamWriter(s, new UTF8Encoding(false)))
                w.Write(content);
        }

        /// <summary>列番号（0始まり）→ "A" "B" … "AA"</summary>
        public static string ColName(int index)
        {
            var sb = new StringBuilder();
            for (int n = index + 1; n > 0; n = (n - 1) / 26)
                sb.Insert(0, (char)('A' + (n - 1) % 26));
            return sb.ToString();
        }

        /// <summary>XMLエスケープ。Excelが扱えない制御文字も落とす</summary>
        private static string Esc(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '"') sb.Append("&quot;");
                else if (c == '\n' || c == '\t') sb.Append(c);
                else if (c == '\r') { /* CRは落とす（LFに統一） */ }
                else if (c < 0x20) { /* 制御文字は落とす */ }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Excelのシート名の制約（31文字・禁止文字・重複不可）に合わせる</summary>
        private static List<string> UniqueSheetNames(IList<Sheet> sheets)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            for (int i = 0; i < sheets.Count; i++)
            {
                string n = sheets[i].Name ?? ("Sheet" + (i + 1));
                foreach (char bad in new[] { ':', '\\', '/', '?', '*', '[', ']' }) n = n.Replace(bad, '_');
                if (n.Length > 31) n = n.Substring(0, 31);
                if (n.Length == 0) n = "Sheet" + (i + 1);
                string baseName = n;
                int k = 2;
                while (!used.Add(n)) n = (baseName.Length > 28 ? baseName.Substring(0, 28) : baseName) + "_" + k++;
                result.Add(n);
            }
            return result;
        }
    }
}
