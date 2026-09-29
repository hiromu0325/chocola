// 調べられる物のアウトライン（InteractOutline）：対象の形を 1 で塗る（下書き）。
// 0 = 見えている所だけ（カメラの深度と比べる。手前の物に隠れた所は塗らない）、1 = 深度なし
Shader "Hidden/RENASCITA/OutlineMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings { float4 positionCS : SV_POSITION; };
        Varyings vert(Attributes i)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
            return o;
        }
        half4 frag(Varyings i) : SV_Target { return 1; }
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
}
