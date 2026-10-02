using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 隠しミニゲーム「わさび当て」（本筋とは関係ない。説明も何もない）。寿司下駄は1ゲームに1個だけで、
    /// 起動時に全部屋の隠し場所（Resources/SushiSpots.json。部屋ごとに3か所）のどこか1か所へ移る。
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

        /// <summary>今回の置き場所（デバッグ用。"部屋Id #番号"）</summary>
        public static string Where { get; private set; } = "";

        // ---- 隠し場所（Tools/EscapePrototype/Hidden/寿司の隠し場所を探す が作る） ----
        [System.Serializable] public class Spot { public string room; public float[] pos; public float yaw; public float hidden; public int near; }
        [System.Serializable] private class SpotList { public Spot[] spots; }

        private void Start()
        {
            // 部屋は Awake で登録されるので、移すのは全部が揃った Start で
            var json = Resources.Load<TextAsset>("SushiSpots");
            var list = json != null ? JsonUtility.FromJson<SpotList>(json.text) : null;
            if (list?.spots == null || list.spots.Length == 0) return;
            var usable = new List<int>();
            for (int i = 0; i < list.spots.Length; i++)
                if (list.spots[i].pos != null && list.spots[i].pos.Length == 3 && LoopRooms.Get(list.spots[i].room) != null) usable.Add(i);
            if (usable.Count == 0) return;
            int pick = usable[Random.Range(0, usable.Count)];
            var spot = list.spots[pick];
            var room = LoopRooms.Get(spot.room);
            // 部屋の子にする（表示されている部屋だけが有効なので、部屋と一緒に出たり消えたりする）
            transform.SetParent(room.transform, false);
            transform.localPosition = new Vector3(spot.pos[0], spot.pos[1], spot.pos[2]);
            transform.localRotation = Quaternion.Euler(0f, spot.yaw, 0f);
            Where = $"{spot.room} #{pick}";
            if (Debug.isDebugBuild) Debug.Log($"[Sushi] 今回の置き場所: {Where}");
        }

        /// <summary>i 番目の握りの原点（シャリの底の中心。モデルの原点から）</summary>
        public static Vector3 SlotPos(int i) => new Vector3(0f, GetaTop, (i - 2.5f) * Pitch);

        /// <summary>
        /// モデル（Sushi.fbx を置いたもの）のシャリを6個に増やし、ネタを order の順に並べる。
        /// 置いたばかりのモデルに1回だけ呼ぶ（ネタの蝶番の位置はモデルに書き出した位置から読む）。エディタの確認用にも使う
        /// </summary>
        public static bool Arrange(Transform root, IList<Kind> order)
        {
            var shari = root.Find("Shari");
            if (shari == null) return false;
            for (int i = 0; i < 6; i++)
            {
                var s = i == 0 ? shari : Instantiate(shari.gameObject, root).transform;
                s.name = "Shari" + i;
                s.localPosition = SlotPos(i);
                s.localRotation = Quaternion.identity;
            }
            for (int i = 0; i < order.Count && i < 6; i++)
            {
                var t = root.Find("Neta_" + order[i]);
                if (t == null) continue;
                var hinge = t.localPosition;   // 握りの原点から見た蝶番（モデルに書き出した位置）
                t.localPosition = SlotPos(i) + new Vector3(hinge.x, hinge.y, 0f);
                t.localRotation = Quaternion.identity;
            }
            return true;
        }

        private void Awake()
        {
            var wasabi = transform.Find("Wasabi");
            // ネタ：並びを混ぜて置く
            var kinds = new List<Kind> { Kind.Tamago, Kind.Anago, Kind.Maguro, Kind.Tai, Kind.Salmon, Kind.Ika };
            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }
            if (wasabi == null || !Arrange(transform, kinds)) { enabled = false; return; }
            for (int i = 0; i < kinds.Count; i++)
            {
                var t = transform.Find("Neta_" + kinds[i]);
                if (t == null) continue;
                var n = t.gameObject.AddComponent<SushiNeta>();
                n.Init(this, kinds[i], SlotPos(i));
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
