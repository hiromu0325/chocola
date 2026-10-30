using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>セーブデータ（JSONで永続化）</summary>
    [Serializable]
    public class SaveData
    {
        public bool valid;
        public int dolls;                                   // 残り陶器人形
        public List<string> solvedGimmicks = new List<string>(); // 解除済みギミック名
        public string savedAt;                              // 保存日時（表示用）
        public string place;                                // 保存した場所（記録を選ぶ画面に出す）

        // ---- 謎解き進行 ----
        public string targetEmployeeNumber;                 // 社員証の持ち主（同じ謎を継続）
        public string keyHolderNumber;                      // 配電室の鍵を保管する社員（2階個室）
        public string distributionModel;                    // 正しい説明書の型番（ランダム選択を固定）
        public string keypadPassword;                       // 配電盤キーパッド解除コード（ランダム固定）
        public bool pcAccessed;                             // PCログイン済み
        public bool hasPowerRoomKey;                        // 配電室の鍵を入手済み
        public bool panelUnlocked;                          // 配電盤パネル解除済み
        public bool repairCodeAccepted;                     // 復旧手順コード受理済み
        public bool powerRestored;                          // 配電盤復旧済み

        // ---- 壁の金庫（ストーリー用・脱出には不要）----
        public string safeCombo;                            // ダイヤル3桁（ランダム固定）
        public bool safeOpened;                             // 金庫を開錠済み

        // ---- ループ回廊（ストーリープロトタイプ）----
        public int loopStage;                               // 部屋の解放段階
        public List<string> loopFound = new List<string>(); // 発見済み資料キー（room/id）
        public List<string> loopVisited = new List<string>(); // 初入室タイトルを表示済みの部屋
        public bool loopIntroPlayed;                        // 起床カットシーン再生済み
        public List<MemoSnippet> memoSnippets = new List<MemoSnippet>(); // マーカーで切り取った文（並び順のまま）
        public List<MarkerStroke> memoStrokes = new List<MarkerStroke>(); // 資料の上に引いたマーカーの線
        public List<FillBoardPos> fillBoard = new List<FillBoardPos>(); // 黒塗りの資料のボード上の置き場所
        public List<string> docsHeard = new List<string>();     // 最後まで聞いた音声記録
        public List<string> docsCommented = new List<string>(); // 主人公のひと言を出した資料

        // ---- 手帳（見つけた情報の追記）----
        public List<NotebookEntry> notes = new List<NotebookEntry>();
    }

    /// <summary>
    /// 記録の種類。マニュアル＝記録端末・一時停止の「記録する」、
    /// オート＝段階が進んだ時（扉が開いた・終章が始まった）・タイトルへ戻る時・捕まって戻った後
    /// </summary>
    public enum SaveSlot { Manual, Auto }

    /// <summary>
    /// セーブ/ロード：JSONを Application.persistentDataPath に保存。マニュアルとオートを別のファイルに持ち、
    /// 「つづきから」の記録を選ぶ画面でどちらから始めるか選ぶ。全滅・クリアで両方消す。
    /// </summary>
    public static class SaveSystem
    {
        private const string ManualFile = "escape_save.json";       // 前の版の記録もこの名前（マニュアルとして読む）
        private const string AutoFile = "escape_autosave.json";
        private static string PathOf(SaveSlot slot) =>
            Path.Combine(Application.persistentDataPath, slot == SaveSlot.Auto ? AutoFile : ManualFile);

        public static bool Exists(SaveSlot slot) => File.Exists(PathOf(slot));
        public static bool Exists() => Exists(SaveSlot.Manual) || Exists(SaveSlot.Auto);

        public static void Save(SaveData data, SaveSlot slot)
        {
            if (data == null) return;
            data.valid = true;
            try
            {
                File.WriteAllText(PathOf(slot), JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 保存に失敗: {e.Message}");
            }
        }

        public static SaveData Load(SaveSlot slot)
        {
            if (!Exists(slot)) return null;
            try
            {
                var json = File.ReadAllText(PathOf(slot));
                var data = JsonUtility.FromJson<SaveData>(json);
                return (data != null && data.valid) ? data : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 読み込みに失敗: {e.Message}");
                return null;
            }
        }

        /// <summary>新しい方の記録（どちらも無ければ null）</summary>
        public static SaveData LoadLatest(out SaveSlot slot)
        {
            var manual = Load(SaveSlot.Manual);
            var auto = Load(SaveSlot.Auto);
            bool autoNewer = auto != null && (manual == null ||
                File.GetLastWriteTimeUtc(PathOf(SaveSlot.Auto)) > File.GetLastWriteTimeUtc(PathOf(SaveSlot.Manual)));
            slot = autoNewer ? SaveSlot.Auto : SaveSlot.Manual;
            return autoNewer ? auto : manual;
        }

        /// <summary>両方の記録を消す（全滅・クリア）</summary>
        public static void Delete()
        {
            foreach (SaveSlot slot in Enum.GetValues(typeof(SaveSlot)))
            {
                try { if (Exists(slot)) File.Delete(PathOf(slot)); }
                catch (Exception e) { Debug.LogWarning($"[SaveSystem] 削除に失敗: {e.Message}"); }
            }
        }
    }
}
