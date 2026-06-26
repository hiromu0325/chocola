using System;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>社員レコード（社員番号・部署・氏名・顔の特徴・生年月日）</summary>
    [Serializable]
    public class EmployeeRecord
    {
        public string number;
        public string department;
        public string name;
        public string feature;     // 集合写真／社員証の顔の特徴
        public string birthdate;   // 生年月日（PCパスワード）YYYYMMDD
        public EmployeeRecord(string num, string dept, string nm, string feat, string birth)
        { number = num; department = dept; name = nm; feature = feat; birthdate = birth; }
    }

    /// <summary>配電盤の型番と復旧手順コード（説明書に記載）</summary>
    [Serializable]
    public class DistributionModel
    {
        public string model;
        public string code;     // 復旧手順コード（数字3桁）
        public DistributionModel(string m, string c) { model = m; code = c; }
    }

    /// <summary>
    /// 謎解きの状態管理。
    /// ① 社員証→名簿→アルバム→人事ファイル で社員番号と生年月日を割り出す
    /// ② 社内PCにログイン → 部署ページ（解除コード＋型番）／配電室の鍵 貸出記録 を閲覧
    /// ③ 貸出記録の社員の「2階 個室」で配電室の鍵を見つける
    /// ④ 配電室の施錠ドアを鍵で開ける
    /// ⑤ 配電盤：解除コード→復旧コード→長押しで時間をかけて復旧 → 脱出可能
    /// </summary>
    public class PuzzleState : MonoBehaviour
    {
        public static PuzzleState Instance { get; private set; }

        // ===== データセット =====
        public static readonly EmployeeRecord[] Employees =
        {
            new EmployeeRecord("1021", "開発部", "佐藤 健一", "黒縁のメガネ",   "19880712"),
            new EmployeeRecord("1045", "開発部", "鈴木 彩花", "長い三つ編み",   "19920305"),
            new EmployeeRecord("1062", "開発部", "高橋 望",   "あごひげ",       "19850920"),
            new EmployeeRecord("2013", "総務部", "田中 美咲", "丸メガネ",       "19910128"),
            new EmployeeRecord("2034", "総務部", "伊藤 大輔", "坊主頭",         "19870604"),
            new EmployeeRecord("2058", "総務部", "渡辺 玲奈", "右頬のほくろ",   "19900415"),
            new EmployeeRecord("3011", "営業部", "山本 翔太", "金髪",           "19931111"),
            new EmployeeRecord("3027", "営業部", "中村 由美", "赤いフレームのメガネ", "19890822"),
            new EmployeeRecord("3049", "営業部", "小林 誠",   "太い眉",         "19860217"),
        };

        /// <summary>2階に個室を持つ社員（鍵保管者はこの中から選ばれる）。ビルダーと共有</summary>
        public static readonly string[] RoomEmployeeNumbers = { "1021", "2034", "2058", "3011" };

        /// <summary>資料：社員番号帯→部署の対応表</summary>
        public static readonly string[] DeptTable =
        {
            "1000番台（1000〜1999） → 開発部",
            "2000番台（2000〜2999） → 総務部",
            "3000番台（3000〜3999） → 営業部",
        };

        public static readonly DistributionModel[] Models =
        {
            new DistributionModel("DXR-100", "472"),
            new DistributionModel("DXR-200", "195"),
            new DistributionModel("DXR-330", "836"),
        };

        // ===== ランタイム状態 =====
        public EmployeeRecord TargetEmployee { get; private set; }  // 社員証の持ち主（PCログイン）
        public EmployeeRecord KeyHolder { get; private set; }       // 配電室の鍵を持つ社員（2階個室）
        public DistributionModel ActiveModel { get; private set; }  // 正しい説明書
        public string KeypadPassword { get; private set; }          // 配電盤キーパッド解除コード（4桁）

        public bool PcAccessed { get; private set; }
        public bool HasPowerRoomKey { get; private set; }
        public bool PanelUnlocked { get; private set; }
        public bool RepairCodeAccepted { get; private set; }
        public bool PowerRestored { get; private set; }

        public string TargetDepartment => TargetEmployee != null ? TargetEmployee.department : "";

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>謎解き可能か（プレイ中・来訪/イベント中でない）</summary>
        public bool PuzzlesEnabled
        {
            get
            {
                if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return false;
                var pm = PhaseManager.Instance;
                if (pm != null && (pm.CurrentPhase == GamePhase.Visit || pm.EventActive)) return false;
                return true;
            }
        }

        public void NewGame()
        {
            TargetEmployee = Employees[UnityEngine.Random.Range(0, Employees.Length)];
            KeyHolder = FindEmployee(RoomEmployeeNumbers[UnityEngine.Random.Range(0, RoomEmployeeNumbers.Length)]) ?? Employees[0];
            ActiveModel = Models[UnityEngine.Random.Range(0, Models.Length)];
            KeypadPassword = UnityEngine.Random.Range(0, 10000).ToString("D4");
            PcAccessed = false;
            HasPowerRoomKey = false;
            PanelUnlocked = false;
            RepairCodeAccepted = false;
            PowerRestored = false;
        }

        public void LoadFrom(SaveData d)
        {
            TargetEmployee = FindEmployee(d.targetEmployeeNumber) ?? Employees[0];
            KeyHolder = FindEmployee(d.keyHolderNumber) ?? FindEmployee(RoomEmployeeNumbers[0]);
            ActiveModel = FindModel(d.distributionModel) ?? Models[0];
            KeypadPassword = string.IsNullOrEmpty(d.keypadPassword) ? "0000" : d.keypadPassword;
            PcAccessed = d.pcAccessed;
            HasPowerRoomKey = d.hasPowerRoomKey;
            PanelUnlocked = d.panelUnlocked;
            RepairCodeAccepted = d.repairCodeAccepted;
            PowerRestored = d.powerRestored;
        }

        public void WriteTo(SaveData d)
        {
            d.targetEmployeeNumber = TargetEmployee != null ? TargetEmployee.number : "";
            d.keyHolderNumber = KeyHolder != null ? KeyHolder.number : "";
            d.distributionModel = ActiveModel != null ? ActiveModel.model : "";
            d.keypadPassword = KeypadPassword;
            d.pcAccessed = PcAccessed;
            d.hasPowerRoomKey = HasPowerRoomKey;
            d.panelUnlocked = PanelUnlocked;
            d.repairCodeAccepted = RepairCodeAccepted;
            d.powerRestored = PowerRestored;
        }

        /// <summary>PCログイン：ID=社員番号, PW=生年月日</summary>
        public bool TryPcLogin(string id, string password)
        {
            if (PcAccessed) return true;
            if (TargetEmployee != null &&
                Normalize(id) == TargetEmployee.number &&
                Normalize(password) == TargetEmployee.birthdate)
            {
                PcAccessed = true;
                GameEvents.RaisePcAccessed();
                return true;
            }
            return false;
        }

        /// <summary>2階個室の戸棚を調べる：その個室の社員が鍵保管者なら入手</summary>
        public bool TryTakeKey(string roomEmployeeNumber)
        {
            if (HasPowerRoomKey) return false;
            if (KeyHolder != null && Normalize(roomEmployeeNumber) == KeyHolder.number)
            {
                HasPowerRoomKey = true;
                GameEvents.RaiseKeyFound();
                return true;
            }
            return false;
        }

        /// <summary>配電盤キーパッド：部署ページの解除コード</summary>
        public bool TryUnlockPanel(string code)
        {
            if (PanelUnlocked) return true;
            if (Normalize(code) == Normalize(KeypadPassword))
            {
                PanelUnlocked = true;
                GameEvents.RaisePanelUnlocked();
                return true;
            }
            return false;
        }

        /// <summary>復旧手順コード受理（パネル解除後）。受理後は長押し作業で復旧</summary>
        public bool TryAcceptRepairCode(string code)
        {
            if (RepairCodeAccepted) return true;
            if (!PanelUnlocked) return false;
            if (ActiveModel != null && Normalize(code) == Normalize(ActiveModel.code))
            {
                RepairCodeAccepted = true;
                GameEvents.RaiseRepairCodeAccepted();
                return true;
            }
            return false;
        }

        /// <summary>長押し作業が完了 → 電力復旧</summary>
        public void CompleteRepair()
        {
            if (PowerRestored || !RepairCodeAccepted) return;
            PowerRestored = true;
            GameEvents.RaisePowerRestored();
        }

        /// <summary>PC部署専用ページの本文</summary>
        public string BuildDeptPage()
        {
            string dept = TargetDepartment;
            string code = KeypadPassword;
            string model = ActiveModel != null ? ActiveModel.model : "??";
            return $"《 {dept} 専用ページ 》\n\n" +
                   "■ 配電盤 緊急復旧情報\n\n" +
                   $"・キーパッド解除コード: <color=#FFE060>{code}</color>\n" +
                   $"・復旧手順マニュアル: <color=#FFE060>型番 {model}</color>\n" +
                   "　（配電室の棚にある説明書を参照）\n\n" +
                   "※ 配電室は施錠中。鍵の所在は『鍵 貸出記録』を参照。";
        }

        /// <summary>PC 鍵 貸出記録の本文</summary>
        public string BuildKeyLendingRecord()
        {
            if (KeyHolder == null) return "記録なし";
            return "《 設備管理 — 鍵 貸出記録 》\n\n" +
                   "■ 配電室（電源室）の鍵\n\n" +
                   $"　貸出中: <color=#FFE060>{KeyHolder.name}</color>" +
                   $"（{KeyHolder.number} / {KeyHolder.department}）\n" +
                   "　返却記録: なし\n\n" +
                   "保管場所: <color=#FFE060>2階 社員個室（本人の部屋）</color>\n" +
                   "本人の個室の戸棚に保管されている可能性が高い。";
        }

        private static string Normalize(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(" ", "").Trim();
        private static EmployeeRecord FindEmployee(string num)
        { foreach (var e in Employees) if (e.number == num) return e; return null; }
        private static DistributionModel FindModel(string m)
        { foreach (var x in Models) if (x.model == m) return x; return null; }
    }
}
