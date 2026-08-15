using UnityEngine;
using UnityEngine.AI;

namespace EscapeProto
{
    /// <summary>
    /// 事前生成NavMesh運用時のドア封鎖。
    /// 事前生成メッシュはドアを除外して焼く（通路が常に繋がっている）ため、
    /// 閉まっている間は NavMeshObstacle のカービングで経路を塞ぎ、
    /// ドアコライダーの有効状態に追従して開閉する。
    /// 実行時ベイク運用（事前生成データ未設定）ではドア開閉時に焼き直されるので何もしない。
    /// </summary>
    public class NavMeshDoorBlocker : MonoBehaviour
    {
        private Collider _door;
        private NavMeshObstacle _obstacle;

        /// <summary>ドアコライダーのオブジェクトに封鎖コンポーネントを取り付ける</summary>
        public static void Attach(Collider doorCollider)
        {
            if (doorCollider == null) return;
            if (doorCollider.GetComponent<NavMeshDoorBlocker>() != null) return;
            var blocker = doorCollider.gameObject.AddComponent<NavMeshDoorBlocker>();
            blocker._door = doorCollider;
        }

        private void Start()
        {
            // NavMeshBootstrap.Instance は全 Awake 完了後に確定しているのでここで判定
            if (NavMeshBootstrap.Instance == null || !NavMeshBootstrap.Instance.UsesPrebaked)
            {
                enabled = false;
                return;
            }

            _obstacle = gameObject.AddComponent<NavMeshObstacle>();
            _obstacle.carving = true;
            _obstacle.carveOnlyStationary = false;   // ドアはスライド移動する
            if (_door is BoxCollider box)
            {
                _obstacle.shape = NavMeshObstacleShape.Box;
                _obstacle.center = box.center;
                _obstacle.size = box.size;
            }
            _obstacle.enabled = _door.enabled;
        }

        private void Update()
        {
            if (_obstacle != null && _obstacle.enabled != _door.enabled)
                _obstacle.enabled = _door.enabled;
        }
    }
}
