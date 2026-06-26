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
    }

    /// <summary>
    /// セーブ/ロード：JSONを Application.persistentDataPath に保存。
    /// チェックポイント（ギミック解除・来訪生存・リスポーン）で自動保存される。
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "escape_save.json";
        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists() => File.Exists(FilePath);

        public static void Save(SaveData data)
        {
            if (data == null) return;
            data.valid = true;
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 保存に失敗: {e.Message}");
            }
        }

        public static SaveData Load()
        {
            if (!Exists()) return null;
            try
            {
                var json = File.ReadAllText(FilePath);
                var data = JsonUtility.FromJson<SaveData>(json);
                return (data != null && data.valid) ? data : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 読み込みに失敗: {e.Message}");
                return null;
            }
        }

        public static void Delete()
        {
            try { if (Exists()) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogWarning($"[SaveSystem] 削除に失敗: {e.Message}"); }
        }
    }
}
