using System;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 手帳キーワード（つなぎボードのチップ）の「括り」設定。
    /// どの語をチップにするか、どの語同士を同じ括り（グループ）とみなすかを指定できる。
    /// 既定では異なるワードなら括りが同じでも線で結べる（同一人物の氏名↔生年月日を
    /// 繋ぐのが本来の用途）。blockSameGroupLinks をONにすると括り内の接続を禁止できる。
    /// アセットは Resources/MemoKeywordConfig.asset に置く
    /// （Tools > EscapePrototype > Create Memo Keyword Config で生成）。
    /// アセットが無い場合は既定の自動括りで動作する。
    /// </summary>
    [CreateAssetMenu(fileName = "MemoKeywordConfig",
                     menuName = "EscapePrototype/Memo Keyword Config", order = 2)]
    public class MemoKeywordConfig : ScriptableObject
    {
        [Serializable]
        public class KeywordGroup
        {
            [Tooltip("グループ名（省略時は先頭のワードが名前になる。画面には表示されない）")]
            public string groupId;
            [Tooltip("同じ括りにするワード。手帳本文と完全一致した箇所がチップになる")]
            public string[] keywords;
        }

        [Header("自動キーワード（社員名簿・配電盤型番から生成）")]
        [Tooltip("社員の 氏名/社員番号/生年月日/特徴 をキーワードにする")]
        public bool autoEmployeeKeywords = true;
        [Tooltip("ON: 同一社員の情報を1つの括りにする（互いに結べない）／OFF: ワードごとに独立")]
        public bool groupEmployeeFields = true;
        [Tooltip("部署名もキーワードにする（部署は複数人にまたがるため常に独立の括り）")]
        public bool departmentKeywords = true;
        [Tooltip("配電盤の型番・復旧コードをキーワードにする")]
        public bool autoModelKeywords = true;
        [Tooltip("ON: 型番とその復旧コードを1つの括りにする／OFF: 独立")]
        public bool groupModelWithCode = true;

        [Header("接続ルール")]
        [Tooltip("ON: 同じ括りに属するワード同士を線で結べなくする（既定OFF＝どのワード同士でも結べる）")]
        public bool blockSameGroupLinks = false;

        [Header("手動指定（自動の括りより優先）")]
        [Tooltip("自分で決める括り。ここに載せたワードは自動の括りから外れてこのグループに属する")]
        public KeywordGroup[] customGroups;
        [Tooltip("追加の単独キーワード（それぞれ独立の括り）")]
        public string[] extraKeywords;
        [Tooltip("キーワードにしない語（チップ化を止める）")]
        public string[] excludedWords;

        public const string ResourcePath = "MemoKeywordConfig";
        private static MemoKeywordConfig _instance;
        private static bool _searched;

        /// <summary>Resourcesの単一アセット。無ければ null（呼び出し側が既定動作にフォールバック）</summary>
        public static MemoKeywordConfig Instance
        {
            get
            {
                if (_instance == null && !_searched)
                {
                    _instance = Resources.Load<MemoKeywordConfig>(ResourcePath);
                    _searched = true;
                }
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instance = null; _searched = false; }
    }
}
