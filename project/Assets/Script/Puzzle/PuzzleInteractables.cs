using System.Collections.Generic;
using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>閲覧できる資料の種類</summary>
    public enum DocumentType
    {
        EmployeeCard,       // 社員証（番号＋顔の特徴）
        DepartmentRoster,   // 名簿：番号→部署 対応表
        Album,              // アルバム：部署ごとの氏名↔特徴
        PersonnelFile,      // 人事ファイル：氏名→生年月日
        Manual              // 配電盤 説明書（型番ごと・復旧手順コード）
    }

    /// <summary>資料を読むインタラクト。内容は PuzzleState から動的生成</summary>
    public class DocumentInteract : MonoBehaviour, IInteractable, IPromptProvider
    {
        public DocumentType Type;
        [Tooltip("Manual の場合に対応する型番（例: DXR-100）")]
        public string ManualModel;

        private float _lastCallTime = -10f;

        public bool CanInteract => PuzzleState.Instance == null || PuzzleState.Instance.PuzzlesEnabled;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;
            if (PuzzleUI.Instance == null || PuzzleState.Instance == null) return;
            if (PuzzleUI.Instance.IsOpen || PuzzleUI.Instance.BlockReopen) return;

            PuzzleUI.Instance.ShowDocument(TitleFor(), BodyFor());
            RecordNote();
        }

        /// <summary>読んだ資料の要点を手帳に追記</summary>
        private void RecordNote()
        {
            var ps = PuzzleState.Instance;
            switch (Type)
            {
                case DocumentType.EmployeeCard:
                    if (ps.TargetEmployee != null)
                        Notebook.Add("card", "社員証",
                            $"社員番号: {ps.TargetEmployee.number}\n顔の特徴: {ps.TargetEmployee.feature}\n（氏名・生年月日は不明）",
                            ("number", ps.TargetEmployee.number),
                            ("feature", ps.TargetEmployee.feature));
                    break;
                case DocumentType.DepartmentRoster:
                {
                    string s = "社員番号の帯 → 部署\n";
                    foreach (var row in PuzzleState.DeptTable) s += $"・{row}\n";
                    Notebook.Add("roster", "社員名簿", s.TrimEnd());
                    break;
                }
                case DocumentType.Album:
                {
                    var sb = new System.Text.StringBuilder("部署ごとの 顔の特徴 → 氏名\n");
                    string cur = null;
                    foreach (var e in PuzzleState.Employees)
                    {
                        if (e.department != cur) { cur = e.department; sb.Append($"\n[{cur}] "); }
                        sb.Append($"{e.name}({e.feature}) ");
                    }
                    Notebook.Add("album", "アルバム", sb.ToString().TrimEnd());
                    break;
                }
                case DocumentType.PersonnelFile:
                {
                    var sb = new System.Text.StringBuilder("氏名 → 生年月日（PWの手がかり）\n");
                    foreach (var e in PuzzleState.Employees) sb.Append($"{e.name}:{e.birthdate}  ");
                    Notebook.Add("hr", "人事ファイル", sb.ToString().TrimEnd());
                    break;
                }
                case DocumentType.Manual:
                {
                    var m = FindModel(ManualModel);
                    if (m != null)
                        Notebook.Add("manual_" + m.model, $"説明書 {m.model}",
                            $"型番 {m.model} の復旧手順コード: {Spaced(m.code)}",
                            ("model", m.model),
                            ("code", Spaced(m.code)));
                    break;
                }
            }
        }

        private string TitleFor()
        {
            switch (Type)
            {
                case DocumentType.EmployeeCard: return "■ 社員証 ■";
                case DocumentType.DepartmentRoster: return "■ 社員名簿（部署対応表）■";
                case DocumentType.Album: return "■ 社員アルバム（集合写真）■";
                case DocumentType.PersonnelFile: return "■ 人事ファイル（生年月日）■";
                case DocumentType.Manual: return $"■ 配電盤 取扱説明書　{ManualModel} ■";
            }
            return "資料";
        }

        private string BodyFor()
        {
            var ps = PuzzleState.Instance;
            switch (Type)
            {
                case DocumentType.EmployeeCard:
                {
                    var t = ps.TargetEmployee;
                    if (t == null) return "（判読不能）";
                    return $"社員番号: <color=#FFE060>{t.number}</color>\n" +
                           $"顔写真の特徴: <color=#FFE060>{t.feature}</color>\n\n" +
                           "氏名・生年月日の記載なし。\n" +
                           "番号→部署→顔→氏名→生年月日 と辿り、\n社内PCのログイン情報を割り出せ。";
                }
                case DocumentType.DepartmentRoster:
                {
                    string s = "社員番号の帯から所属部署が分かる。\n\n";
                    foreach (var row in PuzzleState.DeptTable) s += $"・{row}\n";
                    return s;
                }
                case DocumentType.Album:
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("各部署の集合写真。顔の特徴と氏名が並ぶ。");
                    string current = null;
                    foreach (var e in PuzzleState.Employees)
                    {
                        if (e.department != current)
                        {
                            current = e.department;
                            sb.AppendLine($"\n【{current}】");
                        }
                        sb.AppendLine($"　{e.name}（{e.feature}）");
                    }
                    return sb.ToString();
                }
                case DocumentType.PersonnelFile:
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("社員の氏名と生年月日の一覧。");
                    sb.AppendLine("（社内PCのパスワードは本人の生年月日）\n");
                    foreach (var e in PuzzleState.Employees)
                        sb.AppendLine($"　{e.name} … {FormatBirth(e.birthdate)}");
                    return sb.ToString();
                }
                case DocumentType.Manual:
                {
                    var model = FindModel(ManualModel);
                    if (model == null) return "（この説明書は破損している）";
                    return $"型番: <color=#FFE060>{model.model}</color>\n\n" +
                           "■ 停電からの復旧手順\n" +
                           "1. キーパッドを解除する\n" +
                           "2. 下記の復旧手順コードを入力\n\n" +
                           $"復旧手順コード: <color=#FF8080>{Spaced(model.code)}</color>\n\n" +
                           "※ 部署ページが指定する型番の説明書のみ有効。";
                }
            }
            return "";
        }

        private static string FormatBirth(string yyyymmdd)
        {
            if (string.IsNullOrEmpty(yyyymmdd) || yyyymmdd.Length != 8) return yyyymmdd;
            return $"{yyyymmdd.Substring(0, 4)}/{yyyymmdd.Substring(4, 2)}/{yyyymmdd.Substring(6, 2)}";
        }

        private static string Spaced(string code) => string.Join(" ", code.ToCharArray());

        private static DistributionModel FindModel(string m)
        {
            foreach (var x in PuzzleState.Models) if (x.model == m) return x;
            return null;
        }

        public string GetPrompt()
        {
            switch (Type)
            {
                case DocumentType.EmployeeCard: return "[E] 社員証を調べる";
                case DocumentType.DepartmentRoster: return "[E] 社員名簿を読む";
                case DocumentType.Album: return "[E] アルバムを見る";
                case DocumentType.PersonnelFile: return "[E] 人事ファイルを調べる";
                case DocumentType.Manual: return $"[E] 説明書（{ManualModel}）を読む";
            }
            return "[E] 調べる";
        }
        public float GetProgress01() => -1f;
    }
}
