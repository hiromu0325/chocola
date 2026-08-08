using UnityEditor;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>手帳キーワード括り設定アセットの生成メニュー</summary>
    public static class MemoConfigTools
    {
        private const string AssetPath = "Assets/Resources/MemoKeywordConfig.asset";

        [MenuItem("Tools/EscapePrototype/Create Memo Keyword Config")]
        public static void CreateConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MemoKeywordConfig>(AssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }
            var config = ScriptableObject.CreateInstance<MemoKeywordConfig>();
            AssetDatabase.CreateAsset(config, AssetPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            Debug.Log($"[MemoConfigTools] 作成しました: {AssetPath}");
        }
    }
}
