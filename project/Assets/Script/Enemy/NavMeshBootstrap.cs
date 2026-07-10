using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EscapeProto
{
    /// <summary>
    /// パッケージ（com.unity.ai.navigation の NavMeshSurface）を使わずに、
    /// コアの UnityEngine.AI だけでシーン全体の NavMesh を実行時ビルドする。
    ///
    /// ・Start() で一度ベイクする（ゲーム開始直後、通常のドア閉状態で焼く）
    /// ・GameEvents.OnVisitStart のたびに再ベイクする
    ///   → 襲撃者部屋のドア（SearcherRoomDoor）は来訪開始時に開き、コライダーが
    ///     無効化される。来訪開始"後"に焼き直すことで、開いたドアを通れる経路が
    ///     NavMesh に含まれるようになる。来訪終了後の帰宅（Leaving）もこの
    ///     「ドアが開いた状態」のメッシュのまま歩けるので、そのまま維持でよい
    ///     （次の来訪開始時にまた焼き直される）。
    /// ・トリガーコライダー（IsTrigger）は NavMeshBuilder.CollectSources が
    ///   PhysicsColliders モードで自然に除外する。
    /// </summary>
    public class NavMeshBootstrap : MonoBehaviour
    {
        public static NavMeshBootstrap Instance { get; private set; }

        [Header("ベイク範囲（ワールド原点中心）")]
        [SerializeField] private Vector3 _boundsCenter = new Vector3(0f, 0f, 0f);
        [SerializeField] private Vector3 _boundsSize = new Vector3(60f, 20f, 60f);

        private NavMeshData _navMeshData;
        private NavMeshDataInstance _navMeshInstance;
        private readonly List<NavMeshBuildSource> _sources = new List<NavMeshBuildSource>();
        private bool _hasBaked;

        /// <summary>現在NavMeshが有効か（SearcherController側のフォールバック判定に使用）</summary>
        public bool IsReady => _hasBaked;

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
            Bake();
        }

        private void HandleVisitStart(SearcherType _)
        {
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
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders,
                0, new List<NavMeshBuildMarkup>(), _sources);

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
