using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 人が当たると押しのけられて床を滑る家具（椅子など）。歩く邪魔をしないためのもの。
    /// 押す側は FirstPersonController.ShovePushables：体が動く前に、その一歩で当たる分だけ先にどかす（体は遅くならない）。
    /// ・動くのは床の上の X・Z だけ（浮かない・傾かない・回らない）。押すのをやめればその場で止まる（滑り続けない）
    /// ・壁・机・ほかの家具の手前で止まる（めり込まない）
    /// ・机（Desk）から、置いた時より MaxExtra 以上は離れない（どこまでも押していけないように）。
    ///   止まった後は体の方が椅子をよけて進む（FirstPersonController.CornerNudge）
    /// 物理の力では動かさない（Kinematic）。動きを毎コマ体の一歩に合わせるので、押している間も外れた後も体の速さは変わらない
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PushableProp : MonoBehaviour
    {
        /// <summary>置いた時の机との隙間より、さらに離れてよい距離（m）</summary>
        public const float MaxExtra = 0.6f;

        private const float Skin = 0.004f;        // 壁などの手前に残す隙間
        private const float FloorLift = 0.01f;    // 当たりを調べる箱の底を上げる量（床・敷物の上をこすって止まらないように）
        private const float YieldProbe = 0.03f;   // 動けるかを調べる距離

        [SerializeField] private Transform _desk;

        private Rigidbody _rb;
        private BoxCollider[] _boxes;
        private int _mask = -1;
        private bool _held;
        private Vector2 _deskMin, _deskMax;              // 机の当たり判定の広がり（机から見た X・Z）
        private float _limit = float.PositiveInfinity;   // 机の広がりから椅子の中心までの、離れてよい距離
        private static readonly RaycastHit[] s_hits = new RaycastHit[16];

        /// <summary>そばにある机（ビルダーが置く時に決める）</summary>
        public Transform Desk { get => _desk; set => _desk = value; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.None;   // 位置は毎コマ直接置く（物理のコマを待たない）
            _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
            var list = new System.Collections.Generic.List<BoxCollider>();
            foreach (var b in GetComponentsInChildren<BoxCollider>())
                if (!b.isTrigger) list.Add(b);
            _boxes = list.ToArray();
            MeasureLimit();
        }

        /// <summary>
        /// 床の上を delta だけ動かす（壁・机・家具の手前と、机から離れてよい距離までに抑える）。実際に動いた距離を返す
        /// </summary>
        public float Shove(Vector3 delta)
        {
            if (_rb == null || _held) return 0f;
            delta.y = 0f;
            float len = delta.magnitude;
            if (len < 1e-6f) return 0f;
            Vector3 dir = delta / len;
            len = Allowed(dir, len);
            if (len <= 1e-5f) return 0f;
            Vector3 p = _rb.position + dir * len;
            _rb.position = p;            // 当たり判定はすぐ動く（このコマの体の動きに間に合う）
            transform.position = p;      // 見た目も同じコマで
            return len;
        }

        /// <summary>この向きへ押されて動けるか（壁や机に詰まっている・離れてよい距離の端で、さらに離れる向きの時は動けない）</summary>
        public bool CanYield(Vector3 dir)
        {
            if (_rb == null) return true;
            if (_held) return false;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return true;
            return Allowed(dir.normalized, YieldProbe) > YieldProbe * 0.5f;
        }

        /// <summary>配置ツールで掴んでいる間は押しても動かさない。離したら、その位置を置いた位置として離れてよい距離を測り直す</summary>
        public void SetHeld(bool held)
        {
            if (_rb == null) return;   // プレイ中でない（Awake 前）
            _held = held;
            if (!held)
            {
                _rb.position = transform.position;
                MeasureLimit();
            }
        }

        private float Allowed(Vector3 dir, float len) => Mathf.Min(LeashClamp(dir, len), Clearance(dir, len));

        // ---- 机から離れてよい距離 ----

        private void MeasureLimit()
        {
            _limit = _desk != null && DeskFootprint() ? Gap(_rb.position, out _) + MaxExtra : float.PositiveInfinity;
        }

        /// <summary>dir へ len 動いた時に、机から離れてよい距離を越えない分</summary>
        private float LeashClamp(Vector3 dir, float len)
        {
            if (float.IsInfinity(_limit) || _desk == null) return len;
            Vector3 p = _rb.position;
            float g0 = Gap(p, out _);
            float g1 = Gap(p + dir * len, out _);
            if (g1 <= _limit || g1 <= g0) return len;   // 範囲の中か、机へ近づく（はみ出していても戻る向きは動ける）
            if (g0 >= _limit) return 0f;                 // 端にいて、さらに離れる向き
            // 範囲の端までの距離（机の広がりからの距離は直線に沿って凸なので、二分で求まる）
            float lo = 0f, hi = len;
            for (int i = 0; i < 12; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Gap(p + dir * mid, out _) <= _limit) lo = mid; else hi = mid;
            }
            return lo;
        }

        /// <summary>p から机の広がりまでの床の上の距離と、机から離れる向き（机の広がりの中なら向きは 0）</summary>
        private float Gap(Vector3 p, out Vector3 away)
        {
            Vector3 l = _desk.InverseTransformPoint(p);
            Vector3 nearest = _desk.TransformPoint(new Vector3(
                Mathf.Clamp(l.x, _deskMin.x, _deskMax.x), l.y, Mathf.Clamp(l.z, _deskMin.y, _deskMax.y)));
            away = p - nearest;
            away.y = 0f;
            float d = away.magnitude;
            away = d > 1e-5f ? away / d : Vector3.zero;
            return d;
        }

        /// <summary>机の当たり判定（調べるための判定は除く）の、机から見た X・Z の広がり</summary>
        private bool DeskFootprint()
        {
            _deskMin = new Vector2(float.MaxValue, float.MaxValue);
            _deskMax = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (var c in _desk.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                Vector3 center, half;
                Transform frame;
                if (c is BoxCollider box) { center = box.center; half = box.size * 0.5f; frame = box.transform; }
                else { center = c.bounds.center; half = c.bounds.extents; frame = null; }
                for (int i = 0; i < 8; i++)
                {
                    var corner = center + Vector3.Scale(half, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var l = _desk.InverseTransformPoint(frame != null ? frame.TransformPoint(corner) : corner);
                    _deskMin = Vector2.Min(_deskMin, new Vector2(l.x, l.z));
                    _deskMax = Vector2.Max(_deskMax, new Vector2(l.x, l.z));
                }
                any = true;
            }
            return any;
        }

        // ---- 壁・机・ほかの家具 ----

        /// <summary>dir へ len 動いた時に、ほかの物に当たらない分（自分の当たり判定の箱をそのまま滑らせて調べる）</summary>
        private float Clearance(Vector3 dir, float len)
        {
            float best = len;
            foreach (var box in _boxes)
            {
                if (box == null || !box.enabled) continue;
                var t = box.transform;
                Vector3 s = t.lossyScale;
                Vector3 half = Vector3.Scale(box.size * 0.5f, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
                // 底だけ少し上げる（上の面はそのまま）
                half.y = Mathf.Max(0.001f, half.y - FloorLift);
                Vector3 center = t.TransformPoint(box.center) + Vector3.up * FloorLift;
                int n = Physics.BoxCastNonAlloc(center, half, dir, s_hits, t.rotation, len + Skin, Mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = s_hits[i];
                    var col = h.collider;
                    if (col.attachedRigidbody == _rb || col is CharacterController) continue;   // 自分・押している人
                    if (h.distance <= 0f)
                    {
                        // 最初から重なっている物（机の下に入れてある時など）：深く入る向きだけ止める
                        if (Physics.ComputePenetration(box, t.position, t.rotation, col, col.transform.position, col.transform.rotation,
                                out var sep, out _) && Vector3.Dot(sep, dir) < -0.1f)
                            return 0f;
                        continue;
                    }
                    if (Mathf.Abs(h.normal.y) > 0.7f) continue;   // 床・天井の面
                    best = Mathf.Min(best, Mathf.Max(0f, h.distance - Skin));
                }
            }
            return best;
        }

        /// <summary>この家具が当たる層</summary>
        private int Mask
        {
            get
            {
                if (_mask == -1)
                {
                    _mask = 0;
                    for (int i = 0; i < 32; i++)
                        if (!Physics.GetIgnoreLayerCollision(gameObject.layer, i)) _mask |= 1 << i;
                }
                return _mask;
            }
        }
    }
}
