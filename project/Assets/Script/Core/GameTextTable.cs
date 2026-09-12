using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 1つのテキストID（例: doc/core_ante.manual.body）と、その各言語の文面。
    /// values は GameTextTable.Languages と同じ並び順。
    /// </summary>
    [Serializable]
    public class GameTextEntry
    {
        public string id;
        [TextArea] public string[] values;
    }

    /// <summary>
    /// ゲーム内テキストの実体（Resources/GameText.asset）。
    /// Excel（進行/テキスト/GameText.xlsx）から GameTextImporter が生成する。手で編集しない。
    ///
    /// 【設計】
    /// ・テキストはシーンに焼き込まず、コンポーネントは自分のID（部屋Id＋Idから自動導出）で引く
    ///   → 文章を直してもシーン再生成が不要。シーン差分も出ない
    /// ・言語は列で増やす。実行時に GameText.Language を切り替えるだけで全文が入れ替わる
    /// </summary>
    public class GameTextTable : ScriptableObject
    {
        [Tooltip("言語コードの並び（先頭が既定言語＝未訳時のフォールバック先）")]
        public string[] Languages = { "ja" };
        [Tooltip("Excelの取り込み元と日時（追跡用）")]
        public string SourceInfo;
        public List<GameTextEntry> Entries = new List<GameTextEntry>();

        private Dictionary<string, int> _index;

        /// <summary>id → 行番号の索引を作る（初回アクセス時）</summary>
        public void BuildIndex()
        {
            _index = new Dictionary<string, int>(Entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e != null && !string.IsNullOrEmpty(e.id)) _index[e.id] = i;
            }
        }

        public int LanguageIndex(string lang)
        {
            if (Languages == null) return -1;
            for (int i = 0; i < Languages.Length; i++)
                if (string.Equals(Languages[i], lang, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>
        /// 引く。langIndex に文面が無ければ既定言語（0番）へフォールバックする。
        /// 見つからなければ false（呼び出し側が組み込みテキストを使う）
        /// </summary>
        public bool TryGet(string id, int langIndex, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(id)) return false;
            if (_index == null) BuildIndex();
            if (!_index.TryGetValue(id, out int row)) return false;
            var vals = Entries[row].values;
            if (vals == null || vals.Length == 0) return false;
            if (langIndex >= 0 && langIndex < vals.Length && !string.IsNullOrEmpty(vals[langIndex]))
            {
                value = vals[langIndex];
                return true;
            }
            if (!string.IsNullOrEmpty(vals[0])) { value = vals[0]; return true; }   // 未訳→既定言語
            return false;
        }

        public IEnumerable<string> AllIds()
        {
            foreach (var e in Entries) if (e != null && !string.IsNullOrEmpty(e.id)) yield return e.id;
        }
    }
}
