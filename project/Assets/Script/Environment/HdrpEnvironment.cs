using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace EscapeProto
{
    /// <summary>
    /// HDRP の画面づくり（HDRP の時だけ自動で作られる）。見た目は2通りで、標準は Enhanced。開発中だけ F9 で Match と見比べられる。
    /// ・Match（URP 版と同じ）：固定露出・環境光（RenderSettings の3色）・霧（RenderSettings の指数の霧）を HDRP の設定に写す
    /// ・Enhanced（強化）：体積の霧（ライトが霧を照らし、影が霧に落ちる）・漂う塵・細かな凹凸の影
    /// 色調・ブルーム・周辺減光・粒子・色収差は、どちらも AtmospherePostFX と同じ考えの値。
    /// ライトには毎フレーム HDRP の付属データを付ける（実行中に作られるライトも、明るさを URP の値のまま使うため）。
    /// 卓上・壁の灯りと懐中電灯は、どちらの見た目でも本物の影を落とす（画面から作る接触影は、見る位置で形が変わるので使わない）
    /// </summary>
    public class HdrpEnvironment : MonoBehaviour
    {
        public enum Look { Match, Enhanced }

        public static Look Mode = Look.Enhanced;
        public static HdrpEnvironment Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            Ensure();
            // 場面を読み込み直した時（章の途中から始める・やり直し）も作り直す
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Ensure();

        private static void Ensure()
        {
            if (!HdrpCompat.Active || FindFirstObjectByType<HdrpEnvironment>() != null) return;
            new GameObject("HdrpEnvironment").AddComponent<HdrpEnvironment>();
        }

        private VolumeProfile _profile;
        private GradientSky _sky;
        private Fog _fog;
        private MicroShadowing _micro;
        private ScreenSpaceReflection _ssr;
        private GlobalIllumination _ssgi;
        private LocalVolumetricFog _dust;
        private readonly HashSet<int> _lightsDone = new HashSet<int>();
        private Look _applied = (Look)(-1);
        private Camera _cam;

        private void Awake()
        {
            Instance = this;
            BuildProfile();
            BuildDust();
        }

        private void OnEnable() => RenderPipelineManager.beginContextRendering += BeforeRender;
        private void OnDisable() => RenderPipelineManager.beginContextRendering -= BeforeRender;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void BuildProfile()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var exposure = _profile.Add<Exposure>(true);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(HdrpCompat.MatchEV100);

            var env = _profile.Add<VisualEnvironment>(true);
            env.skyType.Override((int)SkyType.Gradient);
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            _sky = _profile.Add<GradientSky>(true);
            _sky.gradientDiffusion.Override(1f);

            _fog = _profile.Add<Fog>(true);
            _fog.colorMode.Override(FogColorMode.ConstantColor);
            // 下の高さより下は一様な濃さ（部屋はどこにあっても一様な霧の中）
            _fog.baseHeight.Override(10000f);
            _fog.maximumHeight.Override(20000f);
            _fog.maxFogDistance.Override(5000f);

            var shadows = _profile.Add<HDShadowSettings>(true);
            shadows.maxShadowDistance.Override(60f);

            // SSAO は使わない方針（AO はテクスチャに焼いてある）
            var ao = _profile.Add<ScreenSpaceAmbientOcclusion>(true);
            ao.intensity.Override(0f);

            _micro = _profile.Add<MicroShadowing>(true);
            _ssr = _profile.Add<ScreenSpaceReflection>(true);
            _ssgi = _profile.Add<GlobalIllumination>(true);

            // 色調（AtmospherePostFX の URP の値に合わせる）
            var tone = _profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var color = _profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-0.12f);
            color.contrast.Override(14f);
            color.saturation.Override(-24f);
            color.colorFilter.Override(new Color(0.82f, 0.90f, 1f));
            var wb = _profile.Add<WhiteBalance>(true);
            wb.temperature.Override(-16f);
            wb.tint.Override(5f);
            var bloom = _profile.Add<Bloom>(true);
            bloom.intensity.Override(0.2f);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(0.78f, 0.85f, 1f));
            var vignette = _profile.Add<Vignette>(true);
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(Color.black);
            var grain = _profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.38f);
            grain.response.Override(0.8f);
            var ca = _profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.12f);

            var volGo = new GameObject("HdrpVolume");
            volGo.transform.SetParent(transform, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = _profile;
        }

        /// <summary>漂う塵：カメラの周りだけにある薄い霧。揺らぎの図をゆっくり流す</summary>
        private void BuildDust()
        {
            var go = new GameObject("HdrpDust");
            go.transform.SetParent(transform, false);
            _dust = go.AddComponent<LocalVolumetricFog>();
            var p = _dust.parameters;
            p.albedo = new Color(0.85f, 0.86f, 0.88f);
            p.meanFreePath = 14f;
            p.size = new Vector3(14f, 7f, 14f);
            p.positiveFade = p.negativeFade = new Vector3(0.3f, 0.3f, 0.3f);
            p.volumeMask = DustNoise(32);
            p.textureTiling = new Vector3(3f, 3f, 3f);
            p.textureScrollingSpeed = new Vector3(0.012f, 0.006f, 0.009f);
            p.blendingMode = LocalVolumetricFogBlendingMode.Additive;
            p.falloffMode = LocalVolumetricFogFalloffMode.Linear;
            _dust.parameters = p;
        }

        private void Update()
        {
            if (Debug.isDebugBuild && Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
                Mode = Mode == Look.Match ? Look.Enhanced : Look.Match;

            if (_applied != Mode) ApplyMode();
            MirrorRenderSettings();
            UpdateCamera();
            PlaceFlashlight();
            if (Time.unscaledTime >= _nextMaterialScan)
            {
                _nextMaterialScan = Time.unscaledTime + 1f;
                FixRuntimeMaterials();
            }
        }

        private float _nextMaterialScan;
        private readonly HashSet<int> _materialsChecked = new HashSet<int>();
        private readonly List<Material> _mats = new List<Material>();

        /// <summary>ゲームが実行中に作る URP の材質（人形・時計・異形など）を見つけて HDRP の材質に替える</summary>
        private void FixRuntimeMaterials()
        {
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                r.GetSharedMaterials(_mats);
                foreach (var m in _mats)
                {
                    if (m == null || !_materialsChecked.Add(m.GetInstanceID())) continue;
                    if (HdrpCompat.IsUrp(m)) HdrpCompat.Fix(m);
                }
            }
        }

        private void ApplyMode()
        {
            _applied = Mode;
            bool enh = Mode == Look.Enhanced;
            _fog.enableVolumetricFog.Override(enh);
            _fog.albedo.Override(new Color(0.82f, 0.84f, 0.86f));
            _fog.anisotropy.Override(0.2f);   // 灯りの方を見た時のまぶしさを抑える
            _fog.globalLightProbeDimmer.Override(0.7f);
            _fog.depthExtent.Override(32f);
            _fog.denoisingMode.Override(FogDenoisingMode.Both);
            // 霧の品質は「中」（「高」は RTX 3060・1080p で1コマ 7ms ほど重く、見た目の差はほとんど無い）
            _fog.quality.Override((int)ScalableSettingLevelParameter.Level.Medium);
            _micro.enable.Override(enh);
            // 画面上の反射は、塗装の扉や木の床に色の粒が出るので使わない（反射は反射プローブに任せる）
            _ssr.enabled.Override(false);
            // 画面上の間接光は、暗い部屋ではほとんど差が出ないのに 1コマ 6ms ほど重いので使わない
            _ssgi.enable.Override(false);
            _dust.gameObject.SetActive(enh);
            _lightsDone.Clear();
            _cam = null;   // アンチエイリアスを替え直す
            _flashlightPlaced = false;
        }

        private bool _flashlightPlaced;

        /// <summary>
        /// 強化：懐中電灯の光源を目の位置から手の位置（右下）へ下げる。目の位置だと光の芯を正面から見るので霧が一点に光るだけになるが、
        /// ずらすと光の筋が斜めに見える
        /// </summary>
        private void PlaceFlashlight()
        {
            if (_flashlightPlaced) return;
            var fl = FindFirstObjectByType<Flashlight>();
            var spot = fl != null ? fl.transform.Find("FlashlightSpot") : null;
            if (spot == null) return;
            bool enh = Mode == Look.Enhanced;
            spot.localPosition = enh ? new Vector3(0.22f, -0.24f, 0.2f) : new Vector3(0f, 0f, 0.1f);
            // 手の位置から、視線の先（約4m）を照らす
            spot.localRotation = enh ? Quaternion.LookRotation(new Vector3(-0.22f, 0.24f, 4f)) : Quaternion.identity;
            _flashlightPlaced = true;
        }

        /// <summary>ゲームが動かす RenderSettings（停電で暗くなる環境光・霧）を、毎フレーム HDRP の空と霧に写す</summary>
        private void MirrorRenderSettings()
        {
            bool flat = RenderSettings.ambientMode == AmbientMode.Flat;
            Color top = flat ? RenderSettings.ambientLight : RenderSettings.ambientSkyColor;
            Color mid = flat ? RenderSettings.ambientLight : RenderSettings.ambientEquatorColor;
            Color bot = flat ? RenderSettings.ambientLight : RenderSettings.ambientGroundColor;
            SetIfChanged(_sky.top, top.linear * HdrpCompat.InvPi);
            SetIfChanged(_sky.middle, mid.linear * HdrpCompat.InvPi);
            SetIfChanged(_sky.bottom, bot.linear * HdrpCompat.InvPi);

            bool enh = Mode == Look.Enhanced;
            if (!_fog.enabled.overrideState || _fog.enabled.value != RenderSettings.fog) _fog.enabled.Override(RenderSettings.fog);
            float density = Mathf.Max(RenderSettings.fogDensity, 1e-4f);
            // 強化では霧を部屋の大きさで光の筋が見える濃さにする（URP の霧の濃さのままだと薄すぎる）
            float mfp = enh ? Mathf.Min(1f / density, 22f) : 1f / density;
            if (!Mathf.Approximately(_fog.meanFreePath.value, mfp)) _fog.meanFreePath.Override(mfp);
            SetIfChanged(_fog.color, RenderSettings.fogColor.linear * HdrpCompat.InvPi);
        }

        private static void SetIfChanged(ColorParameter p, Color c)
        {
            if (!p.overrideState || p.value != c) p.Override(c);
        }

        private void UpdateCamera()
        {
            var cam = Camera.main;
            if (cam == _cam || cam == null) return;
            _cam = cam;
            if (!cam.TryGetComponent<HDAdditionalCameraData>(out var data)) data = cam.gameObject.AddComponent<HDAdditionalCameraData>();
            // 強化：体積の霧と塵は時間方向に均す前提なので TAA。URP と同じ見た目の時は SMAA
            data.antialiasing = Mode == Look.Enhanced
                ? HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing
                : HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.High;
            data.dithering = true;
        }

        private readonly List<(Light light, HDAdditionalLightData hd)> _newLights = new List<(Light, HDAdditionalLightData)>();

        /// <summary>描く直前（カリングの前）：付属データの無いライトに付け、卓上・壁の灯りと懐中電灯に影を落とさせる</summary>
        private void BeforeRender(ScriptableRenderContext ctx, List<Camera> cams)
        {
            _newLights.Clear();
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var hd = HdrpCompat.EnsureLightData(light);
                if (hd != null && _lightsDone.Add(light.GetInstanceID())) _newLights.Add((light, hd));
            }
            // 部屋や回廊を表示した直後は、床の当たり判定がまだ物理に入っていないので先に同期する
            if (_newLights.Count > 0) Physics.SyncTransforms();
            foreach (var (light, hd) in _newLights)
            {
                // 天井の灯り（天井まで 0.9m 以内か、床から 1.7m より上）と、それ以外（卓上・壁の灯り・懐中電灯）を分ける
                var p = light.transform.position;
                bool ceiling = false;
                if (light.type != LightType.Directional)
                {
                    // 床が見つからない時は決めずに、次のコマでやり直す
                    if (!Physics.Raycast(p, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        _lightsDone.Remove(light.GetInstanceID());
                        continue;
                    }
                    // 天井まで近い（食卓の上の吊り下げ灯は下が食卓に当たるので、上も見る）か、床から高い
                    // （棚の下の卓上灯を天井の灯りと見なさないよう、当たった物が天井ほど広い時だけ）
                    bool nearCeiling = Physics.Raycast(p, Vector3.up, out var up, 0.9f, ~0, QueryTriggerInteraction.Ignore)
                        && up.collider.bounds.size.x > 2f && up.collider.bounds.size.z > 2f;
                    ceiling = nearCeiling || hit.distance > 1.7f;
                }
                // 卓上・壁の灯りと懐中電灯は本物の影を落とす（URP 版では影なしで、光が机の天板を抜けて下の床まで照らしていた）。
                // 届く範囲が 3m 未満の小さな光（画面や玩具のほのかな光・扉の表示灯）は影なしのまま
                if (!ceiling && light.type != LightType.Directional && light.range >= 3f && light.shadows == LightShadows.None)
                    light.shadows = LightShadows.Soft;
                // 包んでいる笠や電球そのものは影に入れない（天井の灯りは URP 版と同じく天井が笠の影で暗くならないように）
                if (light.shadows != LightShadows.None)
                    hd.shadowNearPlane = Mathf.Max(hd.shadowNearPlane, ceiling ? 0.3f : FixtureClearance(light));
                hd.volumetricDimmer = 1f;
                hd.volumetricShadowDimmer = 1f;
            }
            var cam = Camera.main;
            if (_dust != null && cam != null) _dust.transform.position = cam.transform.position;
        }

        /// <summary>
        /// 灯りを包む笠・電球を影に入れない近さ：光源を囲む小さな見た目（光源から 0.3m 以内に収まる物）の、光源から一番遠い所まで。
        /// 台座まで1つの形になっている胴体のような大きな物は数えない（それを数えると、下の机の天板まで影に入らなくなる）
        /// </summary>
        private static float FixtureClearance(Light light)
        {
            var p = light.transform.position;
            var root = light.transform.parent != null ? light.transform.parent : light.transform;
            float near = 0.1f;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                if (!b.Contains(p)) continue;
                var far = Vector3.Max(b.max - p, p - b.min);
                float reach = Mathf.Max(far.x, Mathf.Max(far.y, far.z));
                if (reach <= 0.3f) near = Mathf.Max(near, reach + 0.02f);
            }
            return near;
        }

        /// <summary>塵の揺らぎ（3D の値ノイズを重ねたもの。A が濃さ）</summary>
        private static Texture3D DustNoise(int n)
        {
            var tex = new Texture3D(n, n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n * n];
            var rnd = new System.Random(7);
            var lattice = new float[8 * 8 * 8];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rnd.NextDouble();
            float Sample(float x, float y, float z, int period)
            {
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
                float fx = x - x0, fy = y - y0, fz = z - z0;
                fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
                float V(int a, int b, int c) =>
                    lattice[((a % period + period) % period) + 8 * (((b % period + period) % period) + 8 * ((c % period + period) % period))];
                float x00 = Mathf.Lerp(V(x0, y0, z0), V(x0 + 1, y0, z0), fx);
                float x10 = Mathf.Lerp(V(x0, y0 + 1, z0), V(x0 + 1, y0 + 1, z0), fx);
                float x01 = Mathf.Lerp(V(x0, y0, z0 + 1), V(x0 + 1, y0, z0 + 1), fx);
                float x11 = Mathf.Lerp(V(x0, y0 + 1, z0 + 1), V(x0 + 1, y0 + 1, z0 + 1), fx);
                return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
            }
            for (int z = 0; z < n; z++)
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (float)x / n, v = (float)y / n, w = (float)z / n;
                float d = 0.6f * Sample(u * 4, v * 4, w * 4, 4) + 0.4f * Sample(u * 8, v * 8, w * 8, 8);
                // むらは弱く（濃い所だけが光の筋に見えないように）
                d = Mathf.Clamp01(0.55f + (d - 0.5f) * 0.9f);
                px[x + n * (y + n * z)] = new Color32(255, 255, 255, (byte)(d * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
