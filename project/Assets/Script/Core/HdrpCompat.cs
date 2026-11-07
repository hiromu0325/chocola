using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace EscapeProto
{
    /// <summary>
    /// URP から HDRP への橋渡し。URP 向けに書いたゲームのコード（ライトの明るさ・RenderSettings の環境光と霧・URP の材質）を、
    /// 書き換えずに HDRP で同じ明るさに出すための換算をまとめる。
    /// 明るさの基準：URP は光の強さ I をそのまま拡散に掛け、HDRP は I/π を掛ける（物理単位）。
    /// 露出を EV100 = log2(1/(1.2π)) に固定すると画面に π 倍で出るので、ライトの値は URP のまま使える。
    /// 露出が掛かる色（環境光・霧・自己発光・無照明の色）は 1/π にして渡す。
    /// HDR の色（自己発光・霧・空）は線形のまま、普通の色（基本色・無照明の色）は sRGB で持つ
    /// </summary>
    public static class HdrpCompat
    {
        public static bool Active => GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset;

        /// <summary>URP と同じ明るさになる固定露出</summary>
        public static readonly float MatchEV100 = Mathf.Log(1f / (1.2f * Mathf.PI), 2f);

        public const float InvPi = 1f / Mathf.PI;

        private const string UrpLit = "Universal Render Pipeline/Lit";
        private const string UrpUnlit = "Universal Render Pipeline/Unlit";

        /// <summary>URP の材質か（変換の対象か）</summary>
        public static bool IsUrp(Material m) =>
            m != null && m.shader != null && (m.shader.name == UrpLit || m.shader.name == UrpUnlit);

        /// <summary>
        /// URP の Lit／Unlit の材質を、同じ見た目の HDRP の材質に書き換える（HDRP でない時は何もしない）。
        /// mask：金属(R)・AO(G)・細部(B)・滑らかさ(A) を詰めた HDRP のマスク（エディタの変換で作る。無ければ数値だけ写す）
        /// </summary>
        public static void Fix(Material m, Texture mask = null, bool evenIfInactive = false)
        {
            if (m == null || m.shader == null || (!Active && !evenIfInactive)) return;
            if (m.shader.name == UrpLit) ToLit(m, mask);
            else if (m.shader.name == UrpUnlit) ToUnlit(m);
        }

        private static void ToLit(Material m, Texture mask)
        {
            // シェーダーを替えると同じ名前の値しか引き継がれないので、先に URP の値を読む
            Color baseColor = m.GetColor("_BaseColor");
            Texture baseMap = m.GetTexture("_BaseMap");
            Vector2 tiling = m.GetTextureScale("_BaseMap"), offset = m.GetTextureOffset("_BaseMap");
            Texture bump = m.GetTexture("_BumpMap");
            float bumpScale = m.GetFloat("_BumpScale");
            float metallic = m.GetFloat("_Metallic"), smooth = m.GetFloat("_Smoothness");
            bool metalMap = m.GetTexture("_MetallicGlossMap") != null;
            float occ = m.HasProperty("_OcclusionStrength") ? m.GetFloat("_OcclusionStrength") : 1f;
            bool emissive = m.IsKeywordEnabled("_EMISSION");
            Color emission = m.GetColor("_EmissionColor");
            Texture emissionMap = m.GetTexture("_EmissionMap");
            bool transparent = m.GetFloat("_Surface") > 0.5f;
            float blend = m.GetFloat("_Blend");
            bool clip = m.GetFloat("_AlphaClip") > 0.5f;
            float cutoff = m.GetFloat("_Cutoff");
            bool twoSided = m.GetFloat("_Cull") < 0.5f;

            m.shader = Shader.Find("HDRP/Lit");
            m.SetColor("_BaseColor", baseColor);
            m.SetTexture("_BaseColorMap", baseMap);
            m.SetTextureScale("_BaseColorMap", tiling);
            m.SetTextureOffset("_BaseColorMap", offset);
            m.SetTexture("_NormalMap", bump);
            m.SetFloat("_NormalScale", bumpScale);

            m.SetTexture("_MaskMap", mask);
            if (mask != null)
            {
                // URP：金属は図の R（図が無ければ数値）、滑らかさは図の A × 数値、AO は 1 から図へ強さの分だけ寄せる
                m.SetFloat("_MetallicRemapMin", 0f);
                m.SetFloat("_MetallicRemapMax", metalMap ? 1f : metallic);
                m.SetFloat("_SmoothnessRemapMin", 0f);
                m.SetFloat("_SmoothnessRemapMax", smooth);
                m.SetFloat("_AORemapMin", 1f - occ);
                m.SetFloat("_AORemapMax", 1f);
            }
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);

            m.SetFloat("_UseEmissiveIntensity", 0f);
            m.SetColor("_EmissiveColor", emissive ? emission * InvPi : Color.black);
            m.SetTexture("_EmissiveColorMap", emissive ? emissionMap : null);

            m.SetFloat("_SurfaceType", transparent ? 1f : 0f);
            // URP：0 アルファ・1 乗算済み・2 加算・3 乗算 → HDRP：0 アルファ・1 加算・4 乗算済み
            m.SetFloat("_BlendMode", blend == 1f ? 4f : blend == 2f ? 1f : 0f);
            m.SetFloat("_AlphaCutoffEnable", clip ? 1f : 0f);
            m.SetFloat("_AlphaCutoff", cutoff);
            m.SetFloat("_DoubleSidedEnable", twoSided ? 1f : 0f);
            m.renderQueue = -1;
            HDMaterial.ValidateMaterial(m);
        }

        private static void ToUnlit(Material m)
        {
            Color c = m.GetColor("_BaseColor");
            Texture map = m.GetTexture("_BaseMap");
            Vector2 tiling = m.GetTextureScale("_BaseMap"), offset = m.GetTextureOffset("_BaseMap");
            bool transparent = m.GetFloat("_Surface") > 0.5f;
            float blend = m.GetFloat("_Blend");
            bool twoSided = m.HasProperty("_Cull") && m.GetFloat("_Cull") < 0.5f;

            m.shader = Shader.Find("HDRP/Unlit");
            m.SetColor("_UnlitColor", UnlitColor(c));
            m.SetTexture("_UnlitColorMap", map);
            m.SetTextureScale("_UnlitColorMap", tiling);
            m.SetTextureOffset("_UnlitColorMap", offset);
            m.SetFloat("_SurfaceType", transparent ? 1f : 0f);
            m.SetFloat("_BlendMode", blend == 1f ? 4f : blend == 2f ? 1f : 0f);
            m.SetFloat("_DoubleSidedEnable", twoSided ? 1f : 0f);
            m.renderQueue = -1;
            HDMaterial.ValidateMaterial(m);
        }

        /// <summary>
        /// 自己発光の色を URP の値で決める（URP の _EmissionColor と、HDRP が描画に使う _EmissiveColor の両方に入れる）。
        /// 変換した材質の _EmissionColor には URP の時の値が残っているので、戻す時はそれを渡せばよい
        /// </summary>
        public static void SetEmission(Material m, Color urp)
        {
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", urp);
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", urp * InvPi);
        }

        /// <summary>無照明の色：画面で URP と同じ明るさになるよう、線形で 1/π にして sRGB に戻す（アルファはそのまま）</summary>
        public static Color UnlitColor(Color c)
        {
            var lin = c.linear * InvPi;
            var g = lin.gamma;
            g.a = c.a;
            return g;
        }

        /// <summary>
        /// ライトに HDRP の付属データを付ける（明るさは URP の値のまま。付けずに HDRP に任せると既定の明るさに戻されてしまう）
        /// </summary>
        public static HDAdditionalLightData EnsureLightData(Light light)
        {
            if (light == null) return null;
            if (light.TryGetComponent<HDAdditionalLightData>(out var hd)) return hd;
            float intensity = light.intensity;
            bool temp = light.useColorTemperature;
            hd = light.gameObject.AddComponent<HDAdditionalLightData>();
            light.lightUnit = light.type == LightType.Directional ? LightUnit.Lux : LightUnit.Candela;
            light.intensity = intensity;
            light.useColorTemperature = temp;
            return hd;
        }
    }
}
