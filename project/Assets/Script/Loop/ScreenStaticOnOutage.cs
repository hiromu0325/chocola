using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// ブレイカーが落ちている間、全部屋のPC・モニター・テレビの画面を砂嵐にする。
    /// 画面は材質名で見分け、落ちた瞬間に砂嵐の材質へ差し替え、上がったら元に戻す。
    /// 砂嵐は照明に左右されない発光（Unlit）なので、暗転した部屋で画面だけがざらざらと光る。
    /// 停電中だけ、画面の位置から砂嵐の音（3D）も鳴る。音源は画面の子に置くので、
    /// 表示中の部屋の画面だけが鳴る（近くに並ぶ画面は1つの音源にまとめる）。
    /// ※クラス名とファイル名の一致が必須（シーン保存時のスクリプト解決）
    /// </summary>
    public class ScreenStaticOnOutage : MonoBehaviour
    {
        [Tooltip("砂嵐の材質（URP Unlit）。ノイズの模様は実行時に作って動かす")]
        public Material StaticMaterial;

        /// <summary>砂嵐にする画面の材質名（部屋の接頭辞付きのHQ材質と、箱の部屋の従来材質）</summary>
        private static readonly HashSet<string> ScreenNames = new HashSet<string>
        {
            "DIM_CrtScreen", "CMN_ScreenMessage", "KUR_TvScreen",
            "SYS_ScreenStatus", "SYS_ScreenConnectome", "SYS_ScreenRestore", "SYS_ScreenWave",
            "DAT_CrtScreen", "MZA_LaptopScreen", "MZA_TvScreen", "MZA_ScreenVitals",
            "CAN_DeadScreen", "CAN_ScreenList", "CAN_ScreenMail",
            "WRD_ScreenVitals", "WRD_ScreenChart", "WRD_TvScreen",
            "ANA_ScreenConnectome", "ANA_ScreenCT", "ANA_ScreenSpectrum", "ANA_ScreenAnomaly",
            "ANA_ScreenEEG", "ANA_ScreenMRI", "LAB_ScreenEEG", "LAB_ScreenMRI",
            "LP_AnalysisScreen", "LP_CircuitScreen", "LP_WardMonitor", "LP_DeadScreen", "LP_LaptopScreen", "LP_CrtScreen",
            "LP_SystemScreen", "LP_CoreTerminal", "LP_Prop_Screen", "LP_SaveScreen",
        };

        private readonly List<(Renderer r, int slot, Material orig)> _swapped = new List<(Renderer, int, Material)>();
        private readonly List<GameObject> _hiss = new List<GameObject>();

        /// <summary>この距離より近い画面は同じ音源にまとめる（机に並ぶモニター群が重なって大きくならないように）</summary>
        private const float MergeDistance = 2.0f;
        private Material _mat;
        private Texture2D _noise;
        private bool _on;
        private float _next;

        public bool IsOn => _on;

        public static bool IsScreen(Material m)
        {
            if (m == null) return false;
            string n = m.name.Replace(" (Instance)", "");
            return ScreenNames.Contains(n);
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_noise != null) Destroy(_noise);
        }

        private void Update()
        {
            var bs = BreakerSystem.Instance;
            bool down = bs != null && bs.DownRoomId != null;
            if (down != _on)
            {
                if (down) Apply(); else Restore();
                _on = down;
            }
            if (!_on || _mat == null || Time.time < _next) return;
            // 毎コマ模様の位置を跳ばして「ざらざら動く」砂嵐にする。明るさも細かく揺らす
            _next = Time.time + 1f / 30f;
            _mat.mainTextureOffset = new Vector2(Random.value, Random.value);
            float b = Random.Range(0.72f, 1.0f);
            _mat.color = new Color(b, b, b * 1.02f);
        }

        /// <summary>全部屋（非表示の部屋も含む）の画面を砂嵐に差し替える</summary>
        private void Apply()
        {
            if (!EnsureMaterial()) return;
            _swapped.Clear();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials;
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!IsScreen(mats[i])) continue;
                    _swapped.Add((r, i, mats[i]));
                    mats[i] = _mat;
                    any = true;
                }
                if (any) r.sharedMaterials = mats;
            }
            SpawnHiss();
            AttackDebugLog.Log("static", $"停電：画面{_swapped.Count}枚を砂嵐に");
        }

        /// <summary>元の画面に戻す（途中で別の材質に替わった画面はそのまま）</summary>
        private void Restore()
        {
            foreach (var (r, slot, orig) in _swapped)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (slot >= mats.Length || mats[slot] != _mat) continue;
                mats[slot] = orig;
                r.sharedMaterials = mats;
            }
            _swapped.Clear();
            foreach (var go in _hiss) if (go != null) Destroy(go);
            _hiss.Clear();
        }

        /// <summary>画面ごと（近いものはまとめて）に砂嵐の音源を置く</summary>
        private void SpawnHiss()
        {
            var placed = new List<(Transform room, Vector3 pos)>();
            var clip = ProceduralAudio.StaticHiss();
            foreach (var (r, slot, _) in _swapped)
            {
                if (r == null) continue;
                Vector3 pos = ScreenCenter(r, slot);
                var room = r.GetComponentInParent<LoopRoomRoot>(true);
                Transform roomT = room != null ? room.transform : null;
                bool near = false;
                foreach (var (pr, pp) in placed)
                    if (pr == roomT && (pp - pos).sqrMagnitude < MergeDistance * MergeDistance) { near = true; break; }
                if (near) continue;
                placed.Add((roomT, pos));

                var go = new GameObject("ScreenStaticHiss");
                go.transform.SetParent(r.transform, false);
                go.transform.position = pos;
                var src = go.AddComponent<AudioSource>();
                src.clip = clip;
                src.loop = true;
                src.playOnAwake = true;          // 部屋が表示された時に鳴り出す
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 0.7f;
                src.maxDistance = 12f;
                src.volume = 0.32f;
                src.pitch = Random.Range(0.94f, 1.06f);
                src.timeSamples = Random.Range(0, clip.samples);   // 音源どうしの波形をずらす
                if (go.activeInHierarchy) src.Play();
                _hiss.Add(go);
            }
        }

        /// <summary>その材質の面（サブメッシュ）の中心。非表示の部屋でも求まるようメッシュの値から計算する</summary>
        private static Vector3 ScreenCenter(Renderer r, int slot)
        {
            var mf = r.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return r.transform.position;
            Bounds b = mesh.bounds;
            if (slot < mesh.subMeshCount)
            {
                var sb = mesh.GetSubMesh(slot).bounds;
                if (sb.size.sqrMagnitude > 1e-8f) b = sb;
            }
            return r.transform.TransformPoint(b.center);
        }

        private bool EnsureMaterial()
        {
            if (_mat != null) return true;
            if (StaticMaterial == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) return false;
                StaticMaterial = new Material(sh);
            }
            _mat = new Material(StaticMaterial) { name = "ScreenStatic (Runtime)" };
            _noise = MakeNoise(128, 128);
            _mat.mainTexture = _noise;
            _mat.mainTextureScale = new Vector2(0.8f, 0.8f);
            return true;
        }

        /// <summary>
        /// 砂嵐の模様：粒は横に少しにじみ（走査線方向）、行ごとに明るさがばらつく。
        /// 繰り返しで貼るので、位置を跳ばしても継ぎ目は見えない
        /// </summary>
        private static Texture2D MakeNoise(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "ScreenStaticNoise",
            };
            var px = new Color32[w * h];
            var rng = new System.Random(20260926);
            for (int y = 0; y < h; y++)
            {
                float row = 0.8f + 0.35f * (float)rng.NextDouble();
                if (rng.NextDouble() < 0.06) row *= 1.35f;     // ときどき明るい横筋
                float prev = (float)rng.NextDouble();
                for (int x = 0; x < w; x++)
                {
                    float v = Mathf.Lerp(prev, (float)rng.NextDouble(), 0.75f);
                    prev = v;
                    byte c = (byte)Mathf.Clamp(v * v * row * 255f, 0f, 255f);
                    px[y * w + x] = new Color32(c, c, c, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
