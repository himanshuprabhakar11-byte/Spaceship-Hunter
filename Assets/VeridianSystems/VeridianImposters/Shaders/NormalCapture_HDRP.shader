// This shader is specifically designed for Unity's High Definition Render Pipeline (HDRP).
// It uses preprocessor directives to prevent compilation errors in non-HDRP projects.
Shader "Hidden/AdvancedBillboardCreator/NormalCapture_HDRP"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Tags { "LightMode" = "ForwardOnly" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            // This preprocessor directive checks if the project is using HDRP.
            // The code inside this block will only be compiled if the HDRP package is installed.
            #if defined(UNITY_PIPELINE_HDRP)

                #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
                #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

                struct Attributes
                {
                    float4 positionOS   : POSITION;
                    float3 normalOS     : NORMAL;
                };

                struct Varyings
                {
                    float4 positionCS   : SV_POSITION;
                    float3 normalWS     : TEXCOORD0;
                };

                Varyings vert(Attributes input)
                {
                    Varyings output;
                    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                    return output;
                }

                float4 frag(Varyings input) : SV_Target
                {
                    float3 normal = normalize(input.normalWS);
                    float3 encodedNormal = normal * 0.5 + 0.5;
                    return float4(encodedNormal, 1.0);
                }

            #else // Fallback for non-HDRP projects

                // This is a minimal, "dummy" shader that will compile in any pipeline.
                // It ensures that no errors are thrown when the HDRP package is not present.
                struct Attributes
                {
                    float4 positionOS : POSITION;
                };

                struct Varyings
                {
                    float4 positionCS : SV_POSITION;
                };

                Varyings vert(Attributes input)
                {
                    Varyings output;
                    output.positionCS = float4(input.positionOS.xyz, 1.0);
                    return output;
                }

                float4 frag(Varyings input) : SV_Target
                {
                    // Return a default normal color (blue-ish)
                    return float4(0.5, 0.5, 1.0, 1.0);
                }

            #endif

            ENDHLSL
        }
    }
    Fallback Off
}
