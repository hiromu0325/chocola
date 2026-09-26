using System.Collections;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 扉板の開閉の姿勢。Leaf（蝶番の軸に置いた親）を回す。引き戸は滑らせる。
    /// 暗転遷移（RoomTransitionSystem）が毎フレーム Set(開き具合) を呼んで動かす。
    /// 回廊の扉は押して奥の廊下へ、部屋の扉は引いて手前（室内）へ開く（OpenAngle の符号で向きが決まる）。
    /// 施錠中に触られたら Rattle() で小さく揺れて、ガチャッと鳴る。
    /// ※クラス名とファイル名の一致が必須（シーン保存時のスクリプト解決）
    /// </summary>
    public class DoorSwing : MonoBehaviour
    {
        [Tooltip("回す（滑らせる）扉板の親。蝶番の軸の位置に置く")]
        public Transform Leaf;
        [Tooltip("全開の角度（度）。符号が開く向き")]
        public float OpenAngle = 80f;
        [Tooltip("引き戸（回さずに SlideOffset だけ滑らせる）")]
        public bool Sliding;
        public Vector3 SlideOffset;

        private bool _captured;
        private Vector3 _pos0;
        private Quaternion _rot0;
        private float _open;
        private Coroutine _rattle;

        public float Open01 => _open;

        private void Awake() => Capture();

        private void Capture()
        {
            if (_captured || Leaf == null) return;
            _pos0 = Leaf.localPosition;
            _rot0 = Leaf.localRotation;
            _captured = true;
        }

        /// <summary>
        /// 動かす扉板を差し替える（回廊の扉が、入れるようになって部屋の入口の扉の見た目に変わる時）。
        /// 今の扉板は閉じた姿勢に戻してから切り替える
        /// </summary>
        public void UseLeaf(Transform leaf, float openAngle)
        {
            if (leaf == Leaf) return;
            if (_rattle != null) { StopCoroutine(_rattle); _rattle = null; }
            if (Leaf != null && _captured) { Leaf.localPosition = _pos0; Leaf.localRotation = _rot0; }
            Leaf = leaf;
            OpenAngle = openAngle;
            _captured = false;
            _open = 0f;
            Capture();
        }

        /// <summary>開き具合（0=閉、1=全開）。非表示の扉でも姿勢だけは変えられる</summary>
        public void Set(float open01)
        {
            if (Leaf == null) return;
            Capture();
            if (_rattle != null) { StopCoroutine(_rattle); _rattle = null; }
            _open = Mathf.Clamp01(open01);
            if (Sliding) Leaf.localPosition = _pos0 + SlideOffset * _open;
            else Leaf.localRotation = _rot0 * Quaternion.Euler(0f, OpenAngle * _open, 0f);
        }

        /// <summary>鍵が掛かっている：開く向きへわずかに動いては戻る</summary>
        public void Rattle()
        {
            if (Leaf == null || !isActiveAndEnabled || _open > 0.001f) return;
            Capture();
            if (_rattle != null) StopCoroutine(_rattle);
            _rattle = StartCoroutine(RattleRoutine());
        }

        private IEnumerator RattleRoutine()
        {
            ProceduralAudio.PlayAt(ProceduralAudio.DoorRattle(), transform.position + Vector3.up * 1.0f, 0.75f);
            float t = 0f, dur = 0.34f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float a = Mathf.Abs(Mathf.Sin(t * 58f)) * (1f - t / dur);
                if (Sliding) Leaf.localPosition = _pos0 + SlideOffset.normalized * a * 0.006f;
                else Leaf.localRotation = _rot0 * Quaternion.Euler(0f, Mathf.Sign(OpenAngle) * a * 0.8f, 0f);
                yield return null;
            }
            if (Sliding) Leaf.localPosition = _pos0; else Leaf.localRotation = _rot0;
            _rattle = null;
        }
    }
}
