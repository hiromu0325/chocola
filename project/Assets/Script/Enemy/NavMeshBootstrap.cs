using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EscapeProto
{
    /// <summary>
    /// パッケージ（com.unity.ai.navigation の NavMeshSurface）を使わずに、
    /// コアの UnityEngine.AI だけでシーン全体の NavMesh を用意する。
    ///
    /// [事前生成モード]（推奨。_prebakedNavMesh が設定されている場合）
    /// ・Tools > EscapePrototype > NavMesh を事前生成 で焼いたデータを Start() で登録するだけ。
    ///   実行時ベイクは一切行わない。
    /// ・事前生成はドアを除外して焼いてあり、閉扉中は NavMeshDoorBlocker の
    ///   NavMeshObstacle カービングが通行を塞ぐ（開閉に自動追従）。
    /// ・NavMeshBakeExclude を付けたオブジェクトはベイクから除外される
    ///   （小物がメッシュを分断して敵が通れない問題への対処）。
    ///
    /// [実行時ベイクモード]（フォールバック。事前生成データ未設定の場合）
    /// ・Start() で一度ベイクし、GameEvents.OnVisitStart のたびに再ベイクする
    ///   → 開いたドアを通れる経路がNavMeshに含まれるようになる。
    /// ・トリガーコライダー（IsTrigger）は NavMeshBuilder.CollectSources が
    ///   PhysicsColliders モードで自然に除外する。
    /// </summary>
    public class NavMeshBootstrap : MonoBehaviour
    {
        public static NavMeshBootstrap Instance { get; private set; }

        [Header("事前生成NavMesh（未設定なら実行時ベイク）")]
        [Tooltip("Tools > EscapePrototype > NavMesh を事前生成 で作成したデータ")]
        [SerializeField] private NavMeshData _prebakedNavMesh;

        [Header("ベイク範囲（ワールド原点中心）")]
        [SerializeField] private Vector3 _boundsCenter = new Vector3(0f, 0f, 0f);
        [SerializeField] private Vector3 _boundsSize = new Vector3(60f, 20f, 60f);

        private NavMeshData _navMeshData;
        private NavMeshDataInstance _navMeshInstance;
        private readonly List<NavMeshBuildSource> _sources = new List<NavMeshBuildSource>();
        private bool _hasBaked;

        /// <summary>現在NavMeshが有効か（SearcherController側のフォールバック判定に使用）</summary>
        public bool IsReady => _hasBaked;

        /// <summary>事前生成データで動作中か（ドア封鎖のNavMeshDoorBlockerが参照）</summary>
        public bool UsesPrebaked => _prebakedNavMesh != null;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            GameEvents.OnVisitStart += HandleVisitStart;
        }

        private void OnDisable()
        {
            GameEvents.OnVisitStart -= HandleVisitStart;
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (_prebakedNavMesh != null)
            {
                // 事前生成モード：登録するだけ。ドアの通行はNavMeshDoorBlockerが制御する
                _navMeshInstance = NavMesh.AddNavMeshData(_prebakedNavMesh);
                _hasBaked = true;
                return;
            }
            Bake();
        }

        private void HandleVisitStart(SearcherType _)
        {
            if (UsesPrebaked) return;   // 事前生成モードでは焼き直し不要（ドアはObstacleで開閉）

            // ドアが開いた直後（コライダー無効化済み）に焼き直す。
            // SearcherRoomDoor.HandleVisitStart も同じ OnVisitStart 購読なので、
            // 購読順に依存しないよう1フレーム後に焼く。
            if (isActiveAndEnabled) StartCoroutine(RebakeNextFrame());
        }

        private System.Collections.IEnumerator RebakeNextFrame()
        {
            yield return null;
            Bake();
        }

        /// <summary>NavMeshを（再）ベイクする。数十ms程度を想定。</summary>
        public void Bake()
        {
            var settings = NavMesh.GetSettingsByID(0);

            var bounds = new Bounds(_boundsCenter, _boundsSize);
            _sources.Clear();
            // NavMeshBakeExclude が付いたオブジェクト（と子）はベイクから除外する
            var markups = new List<NavMeshBuildMarkup>();
            foreach (var ex in FindObjectsByType<NavMeshBakeExclude>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                markups.Add(new NavMeshBuildMarkup { root = ex.transform, ignoreFromBuild = true });
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders,
                0, markups, _sources);

            if (_navMeshData == null)
            {
                _navMeshData = NavMeshBuilder.BuildNavMeshData(
                    settings, _sources, bounds, transform.position, transform.rotation);
                _navMeshInstance = NavMesh.AddNavMeshData(_navMeshData);
            }
            else
            {
                NavMeshBuilder.UpdateNavMeshData(_navMeshData, settings, _sources, bounds);
            }

            _hasBaked = true;
        }

        private void OnDestroy()
        {
            if (_navMeshInstance.valid) _navMeshInstance.Remove();
        }
    }
}
