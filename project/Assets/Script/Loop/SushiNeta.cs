using System.Collections;
using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeProto
{
    /// <summary>
    /// わさび当ての握りのネタ1枚（SushiGame が起動時に付ける）。レティクルを合わせると「[E] めくる」。
    /// めくると蝶番（奥の端）を軸に手前が持ち上がり、奥へ倒れる。ゲームが終わったら反応しない（案内も出さない）。
    /// ※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    public class SushiNeta : MonoBehaviour, IInteractable, IPromptProvider
    {
        private const float OpenAngle = 125f;   // めくった角度（+Z 軸まわり。手前の端が上がって奥へ倒れる）

        public SushiGame.Kind Kind { get; private set; }
        /// <summary>この握りの原点（シャリの底の中心。モデルの原点から）</summary>
        public Vector3 Slot { get; private set; }
        public bool Flipped { get; private set; }

        private SushiGame _game;

        /// <summary>並べ終わったネタに付ける（位置は SushiGame.Arrange が置いた所のまま）</summary>
        public void Init(SushiGame game, SushiGame.Kind kind, Vector3 slot)
        {
            _game = game;
            Kind = kind;
            Slot = slot;
            // 当たり判定：ネタの形の外接箱。幅は握りの間隔いっぱい（隣との間に狙えない隙間を作らない）、上に少し厚め
            var mf = GetComponent<MeshFilter>();
            _col = gameObject.AddComponent<BoxCollider>();
            _col.isTrigger = true;   // 床にも置かれるので、歩く人の邪魔をしない（狙う判定はトリガーも拾う）
            if (mf != null && mf.sharedMesh != null)
            {
                var b = mf.sharedMesh.bounds;
                _col.center = b.center + new Vector3(0f, 0.004f, 0f);
                _col.size = new Vector3(b.size.x + 0.006f, b.size.y + 0.012f, 0.037f);
            }
        }

        private BoxCollider _col;
        private int _aimFrame = -1;
        private bool _aimed;

        /// <summary>
        /// レティクル（画面の中心の線）がこのネタに当たっている時だけ。調べる判定の太い補助判定（半径20cm）では
        /// 隣のネタまで拾ってしまうので、寿司は中心の線だけで決める
        /// </summary>
        public bool CanInteract => _game != null && _game.CanFlip(this) && Aimed();

        private bool Aimed()
        {
            if (_aimFrame == Time.frameCount) return _aimed;
            _aimFrame = Time.frameCount;
            var cam = Camera.main;
            _aimed = cam != null && _col != null &&
                     Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 3f, ~0, QueryTriggerInteraction.Collide) &&
                     hit.collider == _col;
            return _aimed;
        }

        public void OnInteract()
        {
            // 押した瞬間だけ（押したまま隣のネタへ動かしても続けてめくらない）
            if (!PressedThisFrame()) return;
            _game.Flip(this);
        }

        public string GetPrompt() => CanInteract ? "[E] めくる" : "";
        public float GetProgress01() => -1f;

        public void FlipOpen(bool wasabi)
        {
            Flipped = true;
            StartCoroutine(Open(wasabi));
        }

        private IEnumerator Open(bool wasabi)
        {
            ProceduralAudio.PlayAt(ProceduralAudio.DialTick(), transform.position, 0.35f);
            var from = transform.localRotation;
            var to = Quaternion.Euler(0f, 0f, OpenAngle);
            const float seconds = 0.4f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                k = 1f - (1f - k) * (1f - k) * (1f - k);   // 素早く持ち上がり、ゆっくり止まる
                transform.localRotation = Quaternion.Slerp(from, to, k);
                yield return null;
            }
            transform.localRotation = to;
            if (wasabi) ProceduralAudio.PlayAt(ProceduralAudio.Bell(), transform.position, 0.45f);
        }

        private static bool PressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            return (kb != null && kb.eKey.wasPressedThisFrame) ||
                   (gp != null && (gp.buttonWest.wasPressedThisFrame || gp.rightTrigger.wasPressedThisFrame));
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }
    }
}
