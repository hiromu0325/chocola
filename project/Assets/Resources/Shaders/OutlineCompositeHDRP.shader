// 調べられる物のアウトライン（InteractOutline の HDRP 版）：下書き（_OutlineMask）の縁だけを重ねる。
// 形のすぐ外側（_OutlineWidth 画素）に真鍮の線、そのさらに外側（_HaloWidth 画素）に薄い黒の縁
// （明るい所でも暗い所でも線が読めるように）。ポストエフェクトの後に重ねる（色調補正でくすまない）
Shader "Hidden/RENASCITA/OutlineCompositeHDRP"
{
    HLSLINCLUDE
    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    TEXTURE2D_X(_OutlineMask);
    float4 _OutlineColor;
    float _OutlineWidth;   // 線の太さ（画素）
    float _HaloWidth;      // 外側の黒い縁の太さ（画素）
    float _HaloAlpha;

    // 下書きは画面と同じ大きさの扱い（RTHandle の確保は最大の大きさなので、使っている範囲へ縮めて読む）
    float Mask(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_OutlineMask, s_linear_clamp_sampler, uv * _RTHandleScale.xy, 0).r; }

    float4 Composite(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 px = _ScreenSize.zw;
        float2 uv = input.positionCS.xy * px;
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
        float l = saturate(line_ - c);                   // 形のすぐ外側
        float h = saturate(halo - line_) * _HaloAlpha;   // そのさらに外側
        float3 col = _OutlineColor.rgb * saturate(l * 3.0);   // 線は真鍮、縁は黒へなめらかに
        float alpha = max(l * _OutlineColor.a, h);
        return float4(col, alpha);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "Composite"
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma fragment Composite
            ENDHLSL
        }
    }
    Fallback Off
}
