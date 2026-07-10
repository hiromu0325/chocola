using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 階段の当たり判定を見た目（このオブジェクト）に追従させるコンポーネント。
    /// 段ごとのBoxColliderを子「StepColliders」として生成するため、
    /// 階段モデルをエディタで移動・回転してもコリジョンが一緒に付いてくる。
    /// 段数などのパラメータを変えた場合は、右クリック→「Rebuild Colliders」
    /// または Tools > EscapePrototype > Rebuild Stair Colliders で再生成する。
    /// </summary>
    public class StairColliderSync : MonoBehaviour
    {
        [Header("段のパラメータ（見た目のモデルと一致させる）")]
        public int Steps = 10;
        public float Rise = 0.26f;    // 蹴上（1段の高さ）
        public float Depth = 0.4f;    // 踏み面（1段の奥行き）
        public float Width = 1.8f;    // 階段の幅
        [Tooltip("ローカル+Z方向に向かって上る場合 true。逆なら false")]
        public bool AscendPlusZ = true;

        private const string RootName = "StepColliders";

        [ContextMenu("Rebuild Colliders")]
        public void Rebuild()
        {
            var old = transform.Find(RootName);
            if (old != null)
            {
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(transform, false);

            float zStart = -Steps * Depth * 0.5f;
            for (int i = 0; i < Steps; i++)
            {
                float h = (i + 1) * Rise;
                int idx = AscendPlusZ ? i : (Steps - 1 - i);
                var c = root.AddComponent<BoxCollider>();
                c.center = new Vector3(0f, h * 0.5f, zStart + idx * Depth + Depth * 0.5f);
                c.size = new Vector3(Width, h, Depth);
            }
        }

        private void Awake()
        {
            // 実行時にコリジョンが無ければ自動生成（保険）
            if (transform.Find(RootName) == null) Rebuild();
        }
    }
}
