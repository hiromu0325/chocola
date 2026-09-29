using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 隠しミニゲーム「わさび当て」（本筋とは関係ない。説明も何もなく、黒田の自宅の台所の隅に置いてある）。
    /// 寿司下駄に握りが6貫（玉子・穴子・まぐろ・鯛・サーモン・イカ）。並びは毎回ランダムで、どれか1貫だけに
    /// わさびが入っている（玉子と穴子には必ず入っていない）。ネタにレティクルを合わせると「[E] めくる」、
    /// めくってわさびを見つけるか、3回めくると終わり（それ以降はどのネタにも反応しない）。
    /// モデル（Assets/Models/HQ/Sushi/Sushi.fbx）の子：Geta・Shari・Wasabi・Neta_&lt;種類&gt;。
    /// シャリは起動時に6個に増やし、ネタは蝶番（奥の端の下）を原点に持つ。
    /// </summary>
    public class SushiGame : MonoBehaviour
    {
        public enum Kind { Tamago, Anago, Maguro, Tai, Salmon, Ika }

        public const int MaxFlips = 3;
        private const float GetaTop = 0.036f;       // 下駄の上面の高さ（モデルの原点から）
        private const float Pitch = 0.038f;         // 握りの間隔（長手 Z）
        private const float WasabiBase = 0.0185f;   // シャリの底から見たわさびの底（少し沈める）

        private readonly List<SushiNeta> _neta = new List<SushiNeta>();
        private SushiNeta _wasabiUnder;
        private int _flips;
        private bool _over;

        /// <summary>もう終わった（わさびを見つけた、または3回めくった）</summary>
        public bool Over => _over;

        private void Awake()
        {
            var shari = transform.Find("Shari");
            var wasabi = transform.Find("Wasabi");
            if (shari == null || wasabi == null) { enabled = false; return; }

            // シャリを6個に（元の1個は1番目に使う）
            var slots = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                slots[i] = new Vector3(0f, GetaTop, (i - 2.5f) * Pitch);
                var s = i == 0 ? shari : Instantiate(shari.gameObject, transform).transform;
                s.name = "Shari" + i;
                s.localPosition = slots[i];
                s.localRotation = Quaternion.identity;
            }

            // ネタ：並びを混ぜて置く
            var kinds = new List<Kind> { Kind.Tamago, Kind.Anago, Kind.Maguro, Kind.Tai, Kind.Salmon, Kind.Ika };
            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }
            for (int i = 0; i < kinds.Count; i++)
            {
                var t = transform.Find("Neta_" + kinds[i]);
                if (t == null) continue;
                var hinge = t.localPosition;   // 握りの原点から見た蝶番（モデルに書き出した位置）
                var n = t.gameObject.AddComponent<SushiNeta>();
                n.Init(this, kinds[i], slots[i] + new Vector3(hinge.x, hinge.y, 0f), slots[i]);
                _neta.Add(n);
            }

            // わさび：玉子・穴子以外のどれか1貫の下
            var candidates = _neta.FindAll(n => n.Kind != Kind.Tamago && n.Kind != Kind.Anago);
            if (candidates.Count == 0) { wasabi.gameObject.SetActive(false); return; }
            _wasabiUnder = candidates[Random.Range(0, candidates.Count)];
            wasabi.localPosition = _wasabiUnder.Slot + new Vector3(0f, WasabiBase, 0f);
            wasabi.localRotation = Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f);
        }

        public bool CanFlip(SushiNeta n) => !_over && n != null && !n.Flipped;

        public void Flip(SushiNeta n)
        {
            if (!CanFlip(n)) return;
            _flips++;
            bool hit = n == _wasabiUnder;
            if (hit || _flips >= MaxFlips) _over = true;
            n.FlipOpen(hit);
        }
    }
}
