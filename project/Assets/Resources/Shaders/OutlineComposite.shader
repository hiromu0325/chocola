// 調べられる物のアウトライン（InteractOutline）：下書き（_BlitTexture）の縁だけを重ねる。
// 形のすぐ外側（_OutlineWidth 画素）に真鍮の線、そのさらに外側（_HaloWidth 画素）に薄い黒の縁
// （明るい所でも暗い所でも線が読めるように）。ポストエフェクトの後に重ねる
Shader "Hidden/RENASCITA/OutlineComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineColor;
            float4 _MaskTexel;     // (1/幅, 1/高さ, 幅, 高さ)
            float _OutlineWidth;   // 線の太さ（画素）
            float _HaloWidth;      // 外側の黒い縁の太さ（画素）
            float _HaloAlpha;

            float Mask(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).r; }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 px = _MaskTexel.xy;
                float c = Mask(uv);
                float line_ = 0, halo = 0;
                [unroll] for (int k = 0; k < 16; k++)
                {
                    float a = k * (6.2831853 / 16.0);
                    float2 d = float2(cos(a), sin(a)) * px;
                    line_ = max(line_, Mask(uv + d * _OutlineWidth));
                    line_ = max(line_, Mask(uv + d * _OutlineWidth * 0.5));
                    halo = max(halo, Mask(uv + d * (_OutlineWidth + _HaloWidth)));
                }
                float l = saturate(line_ - c);          // 形のすぐ外側
                float h = saturate(halo - line_) * _HaloAlpha;   // そのさらに外側
                half3 col = _OutlineColor.rgb * saturate(l * 3.0);   // 線は真鍮、縁は黒へなめらかに
                float alpha = max(l * _OutlineColor.a, h);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
