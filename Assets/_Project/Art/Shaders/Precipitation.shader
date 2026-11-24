// Revoxel rain and snow particles (D-100): unlit, alpha blended, lit by the ambient light
// and the main light. _Shape 0 draws a streak (stretched billboards), 1 a soft flake.
Shader "Revoxel/Precipitation"
{
    Properties
    {
        _Shape("Shape (0 streak, 1 flake)", Float) = 0
        _Color("Color", Color) = (0.75, 0.8, 0.85, 0.35)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Shape;
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.color = input.color;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 c = input.uv * 2.0 - 1.0;
                float alpha = _Shape > 0.5
                    ? pow(saturate(1.0 - length(c)), 1.5)
                    : saturate(1.0 - abs(c.y)) * smoothstep(1.0, 0.2, abs(c.x));
                Light mainLight = GetMainLight();
                half3 lit = SampleSH(half3(0.0, 1.0, 0.0)) + mainLight.color * 0.35;
                half3 color = MixFog(_Color.rgb * input.color.rgb * lit, input.fogFactor);
                return half4(color, alpha * _Color.a * input.color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
