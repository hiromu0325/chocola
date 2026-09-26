using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 襲撃（ブレイカー降下）の照明。
    /// ・予兆（BreakerSystem.PendingRoomId）：部屋と回廊の照明がちらつき、だんだん激しくなる
    /// ・降下中（DownRoomId）：蛍光灯・電球・天井灯などの照明をすべて消す（光源も器具の発光も）
    /// ・復旧：蛍光灯が点く時のように、照明が1つずつばらばらの間合いで瞬いてから明るくなり、
    ///         器具の発光と部屋全体の明るさ（環境光）もゆっくり戻る（約2.5秒）
    /// 残すもの：非常灯・誘導灯・窓の外の光・コア・おもちゃの光・扉の表示灯（警報の赤）・懐中電灯。
    /// 画面（PC・モニター）は ScreenStaticOnOutage が受け持ち、ちらつきは FlickerDark に合わせる。
    /// ※クラス名とファイル名の一致が必須（シーン保存時のスクリプト解決）
    /// </summary>
    public class OutageLighting : MonoBehaviour
    {
        /// <summary>予兆のちらつきで今まさに暗くなっている瞬間か（画面の乱れを合わせる）</summary>
        public static bool FlickerDark { get; private set; }

        /// <summary>消す光源（ビルダーが付けた名前）</summary>
        private static readonly HashSet<string> LightNames = new HashSet<string>
        {
            "RoomLight", "CorridorLight", "LampLight", "KitchenLight", "WarmLight", "ColdLight",
        };

        /// <summary>消す照明器具の材質（名前の一部）。非常灯・誘導灯・空・コアなどは含めない</summary>
        private static readonly string[] FixtureKeys =
        {
            "Bulb", "LampShade", "CeilingLight", "Downlight", "LightCover", "LightRing", "LightStrip", "Lightbox",
            "FluorescentPanel", "LampGlass", "CorridorLamp", "PendantShade", "FrostGlass", "BlueStrip", "BlueLight",
            "GoldLight", "AdLab", "LedBar", "LedPanel", "HospLed", "LedAmber", "LedGreen", "LedBlue",
        };

        private enum Mode { Normal, Prelude, Blackout, Restoring }

        /// <summary>復旧で明かりが戻りきるまでの目安（光源ごとの遅れ＋瞬き＋明るくなる時間）</summary>
        private const float RestoreStagger = 1.1f, RestoreBlink = 0.4f, RestoreRamp = 1.1f;

        private readonly List<(Light l, float intensity, bool enabled)> _lights = new List<(Light, float, bool)>();
        private readonly List<(Renderer r, int slot, Material on, Material off)> _fixtures =
            new List<(Renderer, int, Material, Material)>();
        private readonly Dictionary<Material, Material> _offCache = new Dictionary<Material, Material>();
        private Mode _mode = Mode.Normal;
        private bool _scanned;
        private float _nextFlicker;
        private bool _flickerOn = true;
        private float _flickerLevel = 1f;
        // 復旧の演出
        private float _restoreStart;
        private float[] _restoreDelay;
        private AmbientTrio _darkAmbient, _ambientTarget;
        private bool _ambientCaptured;

        private void OnDisable()
        {
            if (_mode == Mode.Restoring) FinishRestore();
            else if (_mode != Mode.Normal) Restore();
            _mode = Mode.Normal;
            FlickerDark = false;
        }

        private void OnDestroy()
        {
            foreach (var m in _offCache.Values) if (m != null) Destroy(m);
        }

        private void Update()
        {
            var bs = BreakerSystem.Instance;
            Mode want = bs == null ? Mode.Normal
                : bs.DownRoomId != null ? Mode.Blackout
                : bs.PendingRoomId != null ? Mode.Prelude
                : Mode.Normal;
            // 復旧の途中は、次の予兆・降下が来るまで演出を続ける
            if (_mode == Mode.Restoring && want == Mode.Normal) want = Mode.Restoring;

            if (want != _mode)
            {
                AttackDebugLog.Log("lights", $"照明: {_mode}→{want}（光源{_lights.Count}・器具{_fixtures.Count}）");
                if (_mode == Mode.Restoring) FinishRestore();          // 復旧の途中で次の襲撃が来た
                if (want == Mode.Normal)
                {
                    // 停電していたなら徐々に明かりを戻す。予兆だけで終わった（取り消し）なら即座に戻す
                    if (_mode == Mode.Blackout) { BeginRestore(); want = Mode.Restoring; }
                    else Restore();
                }
                else
                {
                    if (!_scanned) Scan();
                    if (want == Mode.Blackout) { SetAll(0f, off: true); ReflectionProbes.RefreshActive(); }
                }
                _mode = want;
            }

            FlickerDark = false;
            if (_mode == Mode.Blackout) { _darkAmbient = AmbientTrio.Current; _ambientCaptured = true; }
            if (_mode == Mode.Restoring) { TickRestore(); return; }
            if (_mode != Mode.Prelude) return;

            // 予兆：終わりに近いほど暗い時間が長く、ちらつきが速くなる
            float k = Mathf.Clamp01(bs.PreludeProgress);
            if (!HorrorSettings.Flicker)
            {
                // 恐怖演出の軽減（デバッグ）：点滅させず、ゆっくり暗くなるだけ
                SetAll(Mathf.Lerp(1f, 0.35f, k), off: false);
                return;
            }
            if (Time.time >= _nextFlicker)
            {
                _nextFlicker = Time.time + Random.Range(0.03f, Mathf.Lerp(0.35f, 0.08f, k));
                _flickerOn = Random.value > Mathf.Lerp(0.12f, 0.6f, k);
                _flickerLevel = _flickerOn ? Random.Range(0.75f, 1.1f) : Random.Range(0f, 0.18f);
                SetAll(_flickerLevel, off: !_flickerOn);
            }
            FlickerDark = !_flickerOn;
        }

        /// <summary>全部屋（非表示の部屋も含む）と回廊の照明を拾う</summary>
        private void Scan()
        {
            _lights.Clear();
            _fixtures.Clear();
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (l != null && LightNames.Contains(l.gameObject.name)) _lights.Add((l, l.intensity, l.enabled));
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r == null || r.GetComponentInParent<LoopSearcher>() != null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (IsFixture(mats[i])) _fixtures.Add((r, i, mats[i], OffOf(mats[i])));
            }
            _scanned = true;
        }

        private static bool IsFixture(Material m)
        {
            if (m == null) return false;
            string n = m.name;
            foreach (var key in FixtureKeys)
                if (n.Contains(key)) return true;
            return false;
        }

        /// <summary>発光を止めた同じ材質（灯っていない器具の見た目）</summary>
        private Material OffOf(Material on)
        {
            if (_offCache.TryGetValue(on, out var off)) return off;
            off = new Material(on) { name = on.name + " (Off)" };
            off.DisableKeyword("_EMISSION");
            if (off.HasProperty("_EmissionColor")) off.SetColor("_EmissionColor", Color.black);
            off.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            // 灯っていないガラス・笠は少し沈んだ色にする
            if (off.HasProperty("_BaseColor")) off.SetColor("_BaseColor", off.GetColor("_BaseColor") * 0.55f);
            _offCache[on] = off;
            return off;
        }

        /// <summary>光源の明るさを元の level 倍にし、器具の発光を off で消す／戻す</summary>
        private void SetAll(float level, bool off)
        {
            if (!_scanned) Scan();
            foreach (var (l, intensity, enabled) in _lights)
            {
                if (l == null) continue;
                l.intensity = intensity * level;
                l.enabled = enabled && level > 0.001f;
            }
            foreach (var (r, slot, on, offMat) in _fixtures)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (slot >= mats.Length) continue;
                var want = off ? offMat : on;
                if (mats[slot] == want) continue;
                mats[slot] = want;
                r.sharedMaterials = mats;
            }
        }

        // ============= 復旧：徐々に明かりが戻る =============

        private void BeginRestore()
        {
            if (!_scanned) Scan();
            _restoreStart = Time.time;
            _restoreDelay = new float[_lights.Count];
            for (int i = 0; i < _restoreDelay.Length; i++) _restoreDelay[i] = Random.Range(0f, RestoreStagger);
            // 環境光：BreakerSystem が元に戻した値を目標に、停電中の暗さから少しずつ戻す
            _ambientTarget = AmbientTrio.Current;
            if (_ambientCaptured) _darkAmbient.Apply();
            // 器具は消えた見た目の材質のまま、発光を少しずつ強める（最後に元の材質へ戻す）
            foreach (var (r, slot, on, off) in _fixtures)
                if (off != null && on != null && on.IsKeywordEnabled("_EMISSION")) off.EnableKeyword("_EMISSION");
        }

        private void TickRestore()
        {
            float t = Time.time - _restoreStart;
            bool calm = !HorrorSettings.Flicker;   // 恐怖演出の軽減（デバッグ）：瞬かずに明るくなる
            // 光源：それぞれの遅れのあと、点灯管のように2〜3回瞬いてから明るくなる
            for (int i = 0; i < _lights.Count; i++)
            {
                var (l, intensity, enabled) = _lights[i];
                if (l == null) continue;
                float local = t - _restoreDelay[i];
                float level;
                if (local < 0f) level = 0f;
                else if (local < RestoreBlink)
                    level = calm ? Mathf.Lerp(0.05f, 0.35f, local / RestoreBlink)
                        : ((int)(local * 16f + i) % 3 == 0) ? 0.45f : 0.05f;
                else level = Mathf.SmoothStep(0.35f, 1f, (local - RestoreBlink) / RestoreRamp);
                l.intensity = intensity * level;
                l.enabled = enabled && level > 0.001f;
            }
            // 器具の発光：全体でゆっくり（はじめは少し瞬く）
            float k = Mathf.Clamp01((t - 0.2f) / (RestoreStagger + RestoreRamp));
            if (!calm && t < 0.2f + RestoreBlink) k *= ((int)(t * 13f) % 2 == 0) ? 1f : 0.3f;
            var done = new HashSet<Material>();
            foreach (var (r, slot, on, off) in _fixtures)
            {
                if (off == null || on == null || !done.Add(off)) continue;
                if (off.HasProperty("_EmissionColor") && on.HasProperty("_EmissionColor"))
                    off.SetColor("_EmissionColor", on.GetColor("_EmissionColor") * k);
                if (off.HasProperty("_BaseColor") && on.HasProperty("_BaseColor"))
                    off.SetColor("_BaseColor", on.GetColor("_BaseColor") * Mathf.Lerp(0.55f, 1f, k));
            }
            // 環境光
            float a = Mathf.SmoothStep(0f, 1f, t / (RestoreStagger + RestoreBlink + RestoreRamp));
            if (_ambientCaptured) AmbientTrio.Lerp(_darkAmbient, _ambientTarget, a).Apply();

            if (t >= RestoreStagger + RestoreBlink + RestoreRamp)
            {
                FinishRestore();
                _mode = Mode.Normal;
                AttackDebugLog.Log("lights", "照明: 復旧の演出おわり");
            }
        }

        /// <summary>復旧の演出を終える（途中で次の襲撃が来た時も）。明かりを完全に戻し、消えた器具の材質を元に戻す</summary>
        private void FinishRestore()
        {
            if (_ambientCaptured) _ambientTarget.Apply();
            _ambientCaptured = false;
            ReflectionProbes.RefreshActive();   // 明かりが戻った部屋を映し直す
            SetAll(1f, off: false);
            foreach (var off in _offCache.Values)
            {
                if (off == null) continue;
                off.DisableKeyword("_EMISSION");
                if (off.HasProperty("_EmissionColor")) off.SetColor("_EmissionColor", Color.black);
            }
            foreach (var kv in _offCache)
                if (kv.Value != null && kv.Key != null && kv.Value.HasProperty("_BaseColor") && kv.Key.HasProperty("_BaseColor"))
                    kv.Value.SetColor("_BaseColor", kv.Key.GetColor("_BaseColor") * 0.55f);
            _lights.Clear();
            _fixtures.Clear();
            _scanned = false;
        }

        private void Restore()
        {
            SetAll(1f, off: false);
            _lights.Clear();
            _fixtures.Clear();
            _scanned = false;
            FlickerDark = false;
        }
    }
}
