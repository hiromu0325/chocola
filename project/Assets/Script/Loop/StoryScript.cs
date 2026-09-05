using System.Collections.Generic;

namespace EscapeProto
{
    /// <summary>
    /// ストーリー脚本の定義。
    /// ・部屋の完了（資料を全部読む）→ 即解放 or 脚本襲撃（指定部屋のブレイカーを鳴らす）
    /// ・脚本襲撃はブレイカー復旧（または死亡による終了）で解決し、次の部屋が解放される
    /// ・章節ラベルは初入室時のタイトル表示（ダークソウル風）に使う
    ///
    /// 章構成: チュートリアル3節 → 1章（犠牲者1）→ 2章・3章（犠牲者2・3、未実装）→ 終章
    /// </summary>
    public static class StoryScript
    {
        /// <summary>部屋Id → その部屋の完了時にブレイカーを鳴らす部屋Id（登録が無ければ即解放）</summary>
        public static readonly Dictionary<string, string> AttackOnComplete = new Dictionary<string, string>
        {
            // チュートリアル3節: 研究所で社員名簿を読み終えると「必ず電車の」ブレイカーが鳴る
            { "lab", "train" },
            // 1章結: 佐伯の自宅を読み終えると解析室のブレイカーが鳴る（章末襲撃）
            { "saeki_home", "analysis" },
            // 2章転: CORE前室の一覧を確認すると病棟のブレイカーが鳴る（水野・初登場）
            { "core_ante", "ward" },
            // 3章結: 黒田の自宅を読み終えるとデータ管理室のブレイカーが鳴る（黒田・強化）
            { "kuroda_home", "data_room" },
        };

        /// <summary>部屋Id → 章節ラベル（部屋名タイトルの上に小さく出す）</summary>
        private static readonly Dictionary<string, string> ChapterLabels = new Dictionary<string, string>
        {
            { "dim",          "チュートリアル　1節" },
            { "train",        "チュートリアル　2節" },
            { "lab",          "チュートリアル　3節" },
            { "study",        "1章『佐伯恒一』　─ 起 ─" },
            { "analysis",     "1章『佐伯恒一』　─ 転 ─" },
            { "saeki_home",   "1章『佐伯恒一』　─ 結 ─" },
            { "ward",         "2章『水野美奈』　─ 起 ─" },
            { "core_ante",    "2章『水野美奈』　─ 転 ─" },
            { "mizuno_apart", "2章『水野美奈』　─ 結 ─" },
            { "data_room",    "3章『黒田恒一』　─ 起 ─" },
            { "system_room",  "3章『黒田恒一』　─ 転 ─" },
            { "kuroda_home",  "3章『黒田恒一』　─ 結 ─" },
            { "core_main",    "終章『RENASCITA』" },
            { "son_room",     "終章　─ 誰の記憶でもない部屋 ─" },
        };

        public static string ChapterLabel(string roomId) =>
            roomId != null && ChapterLabels.TryGetValue(roomId, out var s) ? s : null;
    }

    /// <summary>
    /// ストーリーの進行状態（訪問済み部屋・起床イベント・襲撃の解決待ち）。
    /// セーブデータに書き出し／読み戻しされる。
    /// </summary>
    public static class StoryProgress
    {
        /// <summary>初入室のタイトル表示を出した部屋（"corridor"も含む）</summary>
        private static readonly HashSet<string> Visited = new HashSet<string>();

        /// <summary>起床カットシーンを再生済みか</summary>
        public static bool IntroPlayed;

        /// <summary>脚本襲撃の解決待ち。完了済みの部屋Id（復旧したらこの部屋の次が解放される）</summary>
        public static string PendingUnlockRoom;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Visited.Clear();
            IntroPlayed = false;
            PendingUnlockRoom = null;
        }

        /// <summary>初訪問ならtrueを返し、訪問済みとして記録する</summary>
        public static bool MarkVisited(string roomId) =>
            !string.IsNullOrEmpty(roomId) && Visited.Add(roomId);

        public static bool HasVisited(string roomId) => Visited.Contains(roomId);

        // ---- セーブ連携 ----
        public static List<string> ExportVisited() => new List<string>(Visited);
        public static void ImportVisited(List<string> list)
        {
            Visited.Clear();
            if (list != null) foreach (var v in list) Visited.Add(v);
        }
    }
}
