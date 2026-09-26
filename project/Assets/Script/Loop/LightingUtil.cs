using UnityEngine;
using UnityEngine.Rendering;

namespace EscapeProto
{
    /// <summary>
    /// 環境光の3色（上・横・下。Trilight）。停電の暗転と復旧で3色まとめて動かす。
    /// Flat の時は Sky だけが使われるので、どちらの設定でも同じ扱いでよい
    /// </summary>
    public struct AmbientTrio
    {
        public Color Sky, Equator, Ground;

        public static AmbientTrio Current => new AmbientTrio
        {
            Sky = RenderSettings.ambientSkyColor,
            Equator = RenderSettings.ambientEquatorColor,
            Ground = RenderSettings.ambientGroundColor,
        };

        /// <summary>停電中の非常用の暗さ（赤みを帯びた闇）</summary>
        public static AmbientTrio Emergency => new AmbientTrio
        {
            Sky = new Color(0.10f, 0.03f, 0.03f),
            Equator = new Color(0.075f, 0.022f, 0.022f),
            Ground = new Color(0.05f, 0.015f, 0.015f),
        };

        public void Apply()
        {
            RenderSettings.ambientSkyColor = Sky;
            RenderSettings.ambientEquatorColor = Equator;
            RenderSettings.ambientGroundColor = Ground;
        }

        public static AmbientTrio Lerp(AmbientTrio a, AmbientTrio b, float t) => new AmbientTrio
        {
            Sky = Color.Lerp(a.Sky, b.Sky, t),
            Equator = Color.Lerp(a.Equator, b.Equator, t),
            Ground = Color.Lerp(a.Ground, b.Ground, t),
        };
    }

    /// <summary>
    /// 反射プローブの撮り直し。プローブは焼かずにリアルタイム（スクリプトから撮り直す）にしてあり、
    /// 照明の状態が変わった時（部屋の出入り・停電・復旧の完了）だけ撮り直す。
    /// 光を焼き込まないので、照明の点灯・消灯に反射も追従する
    /// </summary>
    public static class ReflectionProbes
    {
        /// <summary>表示中の空間（回廊または今いる部屋）の反射プローブを撮り直す</summary>
        public static void RefreshActive()
        {
            foreach (var p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            {
                if (p == null || !p.isActiveAndEnabled) continue;
                if (p.mode != ReflectionProbeMode.Realtime || p.refreshMode != ReflectionProbeRefreshMode.ViaScripting) continue;
                p.RenderProbe();
            }
        }
    }
}
