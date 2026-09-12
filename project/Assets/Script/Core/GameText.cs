using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ゲーム内テキストの実行時解決。IDを渡すと現在の言語の文面を返す。
    ///
    ///   GameText.Get("doc/core_ante.manual.body", 組み込みテキスト)
    ///
    /// ・テキスト表（Resources/GameText.asset）はExcelから生成される
    /// ・表にIDが無い／空欄なら、呼び出し側が渡した組み込みテキストをそのまま使う
    ///   （移行途中でも絶対に文章が消えない。差し替え済みのIDから順に効く）
    /// ・言語切替は Language に言語コードを入れるだけ。次のアクセスから全文が入れ替わる
    /// ・ROM同梱後の校正用に、実行ファイルの隣の GameText/&lt;lang&gt;.tsv があれば上書き読み込みする
    /// </summary>
    public static class GameText
    {
        public const string AssetName = "GameText";
        /// <summary>外部上書きTSVを置くフォルダ名（実行ファイルの隣）</summary>
        public const string OverrideFolder = "GameText";

        private static GameTextTable _table;
        private static bool _loaded;
        private static string _language = "ja";
        private static int _langIndex = 0;
        private static Dictionary<string, string> _override;   // 外部TSVによる上書き（現在の言語のみ）

        /// <summary>表に無くて組み込みテキストで代替したID（差し替え漏れの検出用）</summary>
        public static readonly HashSet<string> MissingIds = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _table = null; _loaded = false; _override = null;
            _language = "ja"; _langIndex = 0;
            MissingIds.Clear();
        }

        /// <summary>現在の言語コード（"ja" / "en" …）。設定すると即座に全文が切り替わる</summary>
        public static string Language
        {
            get => _language;
            set
            {
                if (_language == value) return;
                _language = value;
                _override = null;
                EnsureLoaded();
                _langIndex = _table != null ? _table.LanguageIndex(_language) : -1;
                LoadOverride();
            }
        }

        /// <summary>表に載っている言語コード（設定画面の選択肢用）</summary>
        public static string[] AvailableLanguages
        {
            get { EnsureLoaded(); return _table != null ? _table.Languages : new[] { "ja" }; }
        }

        public static GameTextTable Table { get { EnsureLoaded(); return _table; } }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _table = Resources.Load<GameTextTable>(AssetName);
            if (_table != null)
            {
                _table.BuildIndex();
                _langIndex = _table.LanguageIndex(_language);
                LoadOverride();
            }
        }

        /// <summary>
        /// IDの文面を返す。表に無ければ builtIn（コード側の組み込みテキスト）をそのまま返す。
        /// </summary>
        public static string Get(string id, string builtIn = null)
        {
            if (string.IsNullOrEmpty(id)) return builtIn;
            EnsureLoaded();
            if (_override != null && _override.TryGetValue(id, out var ov)) return ov;
            if (_table != null && _table.TryGet(id, _langIndex, out var v)) return v;
            if (!string.IsNullOrEmpty(builtIn)) MissingIds.Add(id);
            return builtIn;
        }

        /// <summary>配列要素用（選択肢・表の行など）。id は "…option" で、末尾に .0 .1 … が付く</summary>
        public static string GetIndexed(string id, int index, string builtIn = null) =>
            Get(id + "." + index, builtIn);

        /// <summary>配列まるごと。表に1件も無ければ builtIn をそのまま返す</summary>
        public static string[] GetArray(string id, string[] builtIn)
        {
            if (builtIn == null || builtIn.Length == 0) return builtIn;
            var result = new string[builtIn.Length];
            for (int i = 0; i < builtIn.Length; i++) result[i] = GetIndexed(id, i, builtIn[i]);
            return result;
        }

        public static bool Has(string id)
        {
            EnsureLoaded();
            return _table != null && _table.TryGet(id, _langIndex, out _);
        }

        /// <summary>
        /// 実行ファイルの隣の GameText/&lt;lang&gt;.tsv（1列目=ID、2列目=文面）を読み、表より優先する。
        /// ROMを配ったあとに文章だけ直したいとき用。無ければ何もしない。
        /// </summary>
        private static void LoadOverride()
        {
            _override = null;
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", OverrideFolder);
                string path = Path.Combine(dir, _language + ".tsv");
                if (!File.Exists(path)) return;
                var map = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    // 改行は \n で書く（1行1エントリ）
                    map[line.Substring(0, tab).Trim()] = line.Substring(tab + 1).Replace("\\n", "\n");
                }
                _override = map;
                Debug.Log($"[GameText] 外部テキストを読み込み: {path}（{map.Count}件）");
            }
            catch (System.Exception e) { Debug.LogWarning($"[GameText] 外部テキストの読み込み失敗: {e.Message}"); }
        }

        // ============================== IDの組み立て（コンポーネントから使う） ==============================
        // 命名は「種別/キー.フィールド」。キーは既存の部屋Id・Idから自動導出するので、
        // シーンに新しいフィールドを増やさずに済む（＝シーン差分ゼロで多言語化できる）

        public static string DocKey(string roomId, string id) => $"doc/{roomId}.{id}";
        public static string EchoKey(string echoId) => $"echo/{echoId}";
        public static string LockKey(string roomId, string id) => $"lock/{roomId}.{id}";
        public static string RoomKey(string roomId) => $"room/{roomId}";
        public static string UiKey(string name) => $"ui/{name}";
    }
}
