// Revoxel water shader (D-073). Hand-written HLSL for URP, drawn on the liquid submesh of
// every chunk (GreedyMeshJob). What lies behind the surface comes from the camera opaque
// texture, offset by the waves (refraction) and dimmed per color by the water it crosses,
// measured with the camera depth texture (absorption); the light the water scatters
// fills what it absorbed. The surface mirrors the sky (reflection probe, or the ambient
// light when no probe is baked) and the sun by the Fresnel term. Top faces carry drifting
// waves; side faces (the face of a waterfall, a rapid) carry streaks running down. Shallow
// water foams at the shore. Corruption grades the water toward a murkier, greyer look by a
// share of the global value. Under water (the main camera in a liquid) the view fogs.
Shader "Revoxel/Water"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // The surface is composited over the opaque texture, so it writes its own color.
            // Seen from below (under water) the back faces draw too.
            Blend Off
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            float _RevoxelCorruption;
            float4 _RevoxelWaterScatter;
            float4 _RevoxelWaterAbsorption;
            float4 _RevoxelPollutedWaterScatter;
            float4 _RevoxelPollutedWaterAbsorption;
            float4 _RevoxelWaterSurface;
            float4 _RevoxelWaterFlow;
            float4 _RevoxelWaterFoamColor;
            float _RevoxelUnderwater;
            float4 _RevoxelUnderwaterColor;
            float _RevoxelUnderwaterDensity;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            // Smooth 2D value noise, 0 to 1, and its gradient.
            float Noise(float2 p, out float2 gradient)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 w = f * f * (3.0 - 2.0 * f);
                float2 dw = 6.0 * f * (1.0 - f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                gradient = dw * float2(
                    lerp(b - a, d - c, w.y),
                    lerp(c - a, d - b, w.x));
                return lerp(lerp(a, b, w.x), lerp(c, d, w.x), w.y);
            }

            // Slope of the waves at a point of the surface: two layers drifting apart.
            float2 WaveSlope(float2 xz, float frequency, float speed)
            {
                float t = _Time.y * speed * frequency;
                float2 g1;
                float2 g2;
                Noise(xz * frequency + float2(t, 0.37 * t), g1);
                Noise(xz * frequency * 2.3 + float2(-0.61 * t, 0.83 * t) + 11.7, g2);
                return (g1 + 0.5 * g2 * 2.3) * frequency;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float refraction = _RevoxelWaterSurface.x;
                float waveFrequency = _RevoxelWaterSurface.y;
                float waveStrength = _RevoxelWaterSurface.z;
                float waveSpeed = _RevoxelWaterSurface.w;
                float fallSpeed = _RevoxelWaterFlow.x;
                float foamDepth = _RevoxelWaterFlow.y;
                float smoothness = _RevoxelWaterFlow.z;
                float pollution = saturate(_RevoxelCorruption * _RevoxelWaterFlow.w);

                float3 geometric = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                bool top = geometric.y > 0.5;
                bool side = abs(geometric.y) < 0.5;

                // Waves on top faces; streaks running down the face of a fall.
                float3 normal = geometric;
                float streaks = 0.0;
                if (top)
                {
                    float2 slope = WaveSlope(input.positionWS.xz, waveFrequency, waveSpeed) * waveStrength;
                    normal = normalize(float3(-slope.x, 1.0, -slope.y));
                }
                else if (side)
                {
                    float along = dot(input.positionWS.xz, float2(-geometric.z, geometric.x));
                    float2 g;
                    float n = Noise(float2(along * 3.0, (input.positionWS.y + _Time.y * fallSpeed) * 0.6), g);
                    streaks = smoothstep(0.55, 0.85, n);
                    float3 across = float3(-geometric.z, 0.0, geometric.x);
                    normal = normalize(geometric + 0.15 * g.x * across);
                }

                if (!frontFace)
                {
                    normal = -normal;
                }

                // What is seen through the surface, and how much water it crosses.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float surfaceDepth = -TransformWorldToView(input.positionWS).z;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float2 offset = (normal.xz - geometric.xz) * refraction * saturate((sceneDepth - surfaceDepth) * 0.5);
                float2 refractedUV = screenUV + offset;
                float refractedDepth = LinearEyeDepth(SampleSceneDepth(refractedUV), _ZBufferParams);
                if (refractedDepth < surfaceDepth)
                {
                    refractedUV = screenUV;
                    refractedDepth = sceneDepth;
                }

                // Eye depth difference, stretched to the distance along the view ray.
                float3 cameraForward = -UNITY_MATRIX_V[2].xyz;
                float rayScale = 1.0 / max(dot(-viewDir, cameraForward), 0.05);
                float thickness = max(refractedDepth - surfaceDepth, 0.0) * rayScale;

                // Looking up from under water, the far side is air: the fog does the work.
                bool fromBelow = !frontFace || _RevoxelUnderwater > 0.5;
                if (fromBelow)
                {
                    thickness = 0.0;
                }

                float3 absorption = lerp(_RevoxelWaterAbsorption.rgb, _RevoxelPollutedWaterAbsorption.rgb, pollution);
                float3 scatterColor = lerp(_RevoxelWaterScatter.rgb, _RevoxelPollutedWaterScatter.rgb, pollution);
                float3 transmittance = exp(-absorption * thickness);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 ambient = SampleSH(half3(0.0, 1.0, 0.0));
                half3 lit = mainLight.color * (shadow * saturate(mainLight.direction.y * 0.5 + 0.5)) + ambient;

                half3 behind = SampleSceneColor(refractedUV);
                half3 water = behind * transmittance + scatterColor * lit * (1.0 - transmittance);

                // Sky and sun on the surface.
                half3 reflected = reflect(-viewDir, normal);
                half3 sky = max(GlossyEnvironmentReflection(reflected, 1.0 - smoothness, 1.0), SampleSH(reflected));
                half3 halfDir = normalize(mainLight.direction + viewDir);
                half specular = pow(saturate(dot(normal, halfDir)), lerp(16.0, 1024.0, smoothness)) * 4.0;
                half fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(normal, viewDir)), 5.0);
                if (fromBelow)
                {
                    fresnel *= 0.25;
                    sky = scatterColor * lit;
                }

                half3 color = lerp(water, sky, fresnel) + mainLight.color * (specular * shadow * fresnel);

                // Foam where the water is shallow, and on falling water.
                float2 foamGradient;
                float foamNoise = Noise(input.positionWS.xz * 2.0 + _Time.y * 0.2, foamGradient);
                float shore = fromBelow ? 0.0 : (1.0 - saturate(thickness / foamDepth)) * smoothstep(0.3, 0.7, foamNoise);
                float foam = saturate(shore + streaks * 0.8);
                color = lerp(color, _RevoxelWaterFoamColor.rgb * lit, foam);

                if (_RevoxelUnderwater > 0.5)
                {
                    float distance = length(input.positionWS - GetCameraPositionWS());
                    float fog = 1.0 - exp(-_RevoxelUnderwaterDensity * distance);
                    color = lerp(color, _RevoxelUnderwaterColor.rgb * lit, fog);
                }
                else
                {
                    color = MixFog(color, input.fogFactor);
                }

                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
