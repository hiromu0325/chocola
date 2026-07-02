using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EscapeProto
{
    /// <summary>
    /// 地下室ホラー向けのポストプロセスを実行時に構成（アセット不要・コード生成）。
    /// ACESトーンマップ＋低彩度の寒色グレーディング＋周辺減光＋発光ブルーム＋
    /// フィルムグレイン＋わずかな色収差で、画面を不穏に仕上げる。
    /// AfterSceneLoad で自動生成するためシーン再ビルドは不要。
    /// </summary>
    public class AtmospherePostFX : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<AtmospherePostFX>() != null) return;
            var go = new GameObject("AtmospherePostFX");
            go.AddComponent<AtmospherePostFX>();
        }

        private void Start()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderPostProcessing = true;
                    data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                    data.dithering = true;
                }
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-0.12f);
            color.contrast.Override(14f);
            color.saturation.Override(-24f);
            color.colorFilter.Override(new Color(0.82f, 0.90f, 1f));

            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(-16f);
            wb.tint.Override(5f);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.6f);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(0.78f, 0.85f, 1f));

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(Color.black);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.38f);
            grain.response.Override(0.8f);

            var ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.12f);

            var volGo = new GameObject("GlobalVolume");
            volGo.transform.SetParent(transform, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = profile;
        }
    }
}
