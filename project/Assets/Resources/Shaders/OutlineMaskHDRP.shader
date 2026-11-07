// 調べられる物のアウトライン（InteractOutline の HDRP 版）：対象の形を 1 で塗る（下書き）。
// 0 = 見えている所だけ（カメラの深度と比べる。手前の物に隠れた所は塗らない）、1 = 深度なし
// ポストエフェクトの前（TAA の揺らしが深度と同じ間）に描く
Shader "Hidden/RENASCITA/OutlineMaskHDRP"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" }

        HLSLINCLUDE
        #pragma target 4.5
        #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
        struct Attributes { float3 positionOS : POSITION; };
        struct Varyings { float4 positionCS : SV_POSITION; };
        Varyings vert(Attributes i)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(i.positionOS);   // HDRP のカメラ基準の座標に直してから
            return o;
        }
        float4 frag(Varyings i) : SV_Target { return 1; }
        ENDHLSL

        Pass
        {
            Name "MaskDepth"
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
        Pass
        {
            Name "MaskAlways"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDHLSL
        }
    }
    Fallback Off
}
