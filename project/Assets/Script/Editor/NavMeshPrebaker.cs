using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace EscapeProto
{
    /// <summary>
    /// NavMeshの事前生成ツール。
    /// シーンを開いた状態で Tools > EscapePrototype > NavMesh を事前生成 を実行すると、
    /// NavMeshBootstrap のベイク範囲でNavMeshDataを構築してアセットに保存し、
    /// NavMeshBootstrap の事前生成スロットに割り当てる（以後、実行時ベイクは行われない）。
    ///
    /// ・NavMeshBakeExclude が付いたオブジェクト（と子）はベイクから除外される
    /// ・SearcherRoomDoor / KeyedDoor のドアは除外して焼き、実行時は
    ///   NavMeshDoorBlocker（NavMeshObstacleカービング）が閉扉中の通行を塞ぐ
    /// </summary>
    public static class NavMeshPrebaker
    {
        private const string AssetPath = "Assets/EscapePrototype/PrebakedNavMesh.asset";

        [MenuItem("Tools/EscapePrototype/NavMesh を事前生成（Prebake）")]
        public static void Bake()
        {
            var bootstrap = Object.FindFirstObjectByType<NavMeshBootstrap>();
            if (bootstrap == null)
            {
                EditorUtility.DisplayDialog("NavMesh事前生成",
                    "シーンに NavMeshBootstrap がありません。先にプロトタイプシーンを構築してください。", "OK");
                return;
            }

            var so = new SerializedObject(bootstrap);
            var bounds = new Bounds(
                so.FindProperty("_boundsCenter").vector3Value,
                so.FindProperty("_boundsSize").vector3Value);

            // 除外マークアップ：NavMeshBakeExclude ＋ 各ドア（実行時はObstacleで塞ぐ）
            var markups = new List<NavMeshBuildMarkup>();
            foreach (var ex in Object.FindObjectsByType<NavMeshBakeExclude>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                markups.Add(new NavMeshBuildMarkup { root = ex.transform, ignoreFromBuild = true });
            foreach (var d in Object.FindObjectsByType<SearcherRoomDoor>(FindObjectsSortMode.None))
                markups.Add(new NavMeshBuildMarkup { root = d.transform, ignoreFromBuild = true });
            foreach (var d in Object.FindObjectsByType<KeyedDoor>(FindObjectsSortMode.None))
                markups.Add(new NavMeshBuildMarkup { root = d.transform, ignoreFromBuild = true });

            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders,
                0, markups, sources);

            var data = NavMeshBuilder.BuildNavMeshData(
                NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null)
            {
                EditorUtility.DisplayDialog("NavMesh事前生成", "NavMeshDataの構築に失敗しました。", "OK");
                return;
            }
            data.name = "PrebakedNavMesh";

            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath) != null)
                AssetDatabase.DeleteAsset(AssetPath);
            AssetDatabase.CreateAsset(data, AssetPath);
            AssetDatabase.SaveAssets();

            so.FindProperty("_prebakedNavMesh").objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);

            Debug.Log($"[NavMeshPrebaker] 事前生成完了: ソース数={sources.Count}, " +
                      $"除外={markups.Count}件, 保存先={AssetPath}");
        }

        [MenuItem("Tools/EscapePrototype/NavMesh 事前生成データを解除")]
        public static void ClearPrebaked()
        {
            var bootstrap = Object.FindFirstObjectByType<NavMeshBootstrap>();
            if (bootstrap == null) return;
            var so = new SerializedObject(bootstrap);
            so.FindProperty("_prebakedNavMesh").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
            Debug.Log("[NavMeshPrebaker] 事前生成データの割り当てを解除しました（実行時ベイクに戻ります）");
        }
    }
}
