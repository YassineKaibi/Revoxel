// Revoxel voxel shader (D-044, D-051). Hand-written HLSL for URP.
// Each vertex carries its voxel id and occlusion (TEXCOORD0). The id indexes the global
// material table (VoxelRegistry.CreateMaterialTable): row 0 base color, row 1 secondary
// color (both linear), row 2 (pattern / 3, scale / 8 m, contrast). The albedo blends
// base toward secondary by a smooth world-space pattern, so adjacent voxels of one type
// read as one continuous material. Occlusion darkens inside corners only. Corruption
// grades the color toward a tinted luminance; one global value until the corruption WP.
// The pattern fades out as a pixel covers a good part of its scale (distant and coarse
// chunks, PLAN-03 Task 5), so far materials read as their average color, not as noise.
// While the main camera is in water the view fogs toward the underwater color (D-073).
// Each vertex also carries its sky light (TEXCOORD1, D-097), which scales the ambient light
// and keeps direct light out of caves; clouds shade the direct light (D-099); the seasons
// tint foliage, wet ground darkens and snow covers top faces where the air is cold (D-101).
Shader "Revoxel/Voxel"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"
            #include "RevoxelAtmosphere.hlsl"

            TEXTURE2D(_RevoxelMaterialTable);
            float _RevoxelMaterialTableWidth;
            float _RevoxelCorruption;
            float4 _RevoxelCorruptionTint;
            float _RevoxelAoMin;
            float _RevoxelUnderwater;
            float4 _RevoxelUnderwaterColor;
            float _RevoxelUnderwaterDensity;
            float4 _RevoxelSkyLight;
            TEXTURE2D(_RevoxelClimateMap);
            SAMPLER(sampler_RevoxelClimateMap);
            float4 _RevoxelClimate;
            float4 _RevoxelClimateOrigin;
            float4 _RevoxelSeason;
            float4 _RevoxelWeather;
            float4 _RevoxelSpringLeaf;
            float4 _RevoxelAutumnLeafA;
            float4 _RevoxelAutumnLeafB;
            float4 _RevoxelAutumnLeafC;
            float4 _RevoxelWinterLeaf;
            float4 _RevoxelDryGrass;
            float4 _RevoxelWinterGrass;
            float4 _RevoxelSnowColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 materialAo : TEXCOORD0;
                float skyLight : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 materialAo : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                float skyLight : TEXCOORD4;
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
                output.materialAo = input.materialAo;
                output.skyLight = input.skyLight;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // Smooth 3D value noise, 0 to 1.
            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 w = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i);
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));
                float nx00 = lerp(n000, n100, w.x);
                float nx10 = lerp(n010, n110, w.x);
                float nx01 = lerp(n001, n101, w.x);
                float nx11 = lerp(n011, n111, w.x);
                return lerp(lerp(nx00, nx10, w.y), lerp(nx01, nx11, w.y), w.z);
            }

            // 0 Flat, 1 Grain (stretched along world Y), 2 Mottle, 3 Patches (soft threshold).
            float Pattern(int pattern, float3 positionWS, float scale)
            {
                if (pattern == 0)
                {
                    return 0.0;
                }

                float3 p = positionWS / max(scale, 0.01);
                if (pattern == 1)
                {
                    p.y *= 0.15;
                }

                float n = ValueNoise(p);
                return pattern == 3 ? smoothstep(0.4, 0.6, n) : n;
            }

            // Foliage colors by season (D-101): 1 deciduous, 2 evergreen, 3 grass.
            half3 SeasonTint(int foliage, half3 albedo, float3 positionWS)
            {
                float spring = _RevoxelSeason.x;
                float autumn = _RevoxelSeason.y;
                float winter = _RevoxelSeason.z;
                float dry = _RevoxelSeason.w;
                half luminance = dot(albedo, half3(0.2126, 0.7152, 0.0722));
                if (foliage == 1)
                {
                    float n = ValueNoise(positionWS * 0.2);
                    half3 fall = lerp(_RevoxelAutumnLeafA.rgb, _RevoxelAutumnLeafB.rgb, smoothstep(0.3, 0.5, n));
                    fall = lerp(fall, _RevoxelAutumnLeafC.rgb, smoothstep(0.6, 0.8, n));
                    fall *= 0.6 + 0.8 * ValueNoise(positionWS * 1.3);
                    albedo = lerp(albedo, saturate(albedo * _RevoxelSpringLeaf.rgb), spring);
                    albedo = lerp(albedo, fall, autumn * smoothstep(0.15, 0.4, autumn + n * 0.3));
                    albedo = lerp(albedo, _RevoxelWinterLeaf.rgb * (0.8 + luminance), winter);
                }
                else if (foliage == 2)
                {
                    albedo *= 1.0 - 0.18 * winter;
                }
                else if (foliage == 3)
                {
                    half3 dried = _RevoxelDryGrass.rgb * (0.7 + 0.6 * ValueNoise(positionWS * 0.5));
                    albedo = lerp(albedo, dried, dry * 0.75 + autumn * 0.3);
                    albedo = lerp(albedo, _RevoxelWinterGrass.rgb, winter * 0.7);
                    albedo = lerp(albedo, saturate(albedo * _RevoxelSpringLeaf.rgb), spring * 0.6);
                }

                return albedo;
            }

            // Snow on top faces where the air is cold (D-101), 0 to 1.
            float SnowCover(float3 positionWS, float3 normalWS, float sky)
            {
                if (_RevoxelClimate.w < 0.5 || normalWS.y < 0.5)
                {
                    return 0.0;
                }

                float2 uv = (positionWS.xz - _RevoxelClimateOrigin.xy) * _RevoxelClimate.z;
                float sea = SAMPLE_TEXTURE2D_LOD(_RevoxelClimateMap, sampler_RevoxelClimateMap, uv, 0).r;
                float air = sea - _RevoxelClimate.y * max(0.0, positionWS.y - _RevoxelClimate.x) + _RevoxelWeather.y;
                air += (ValueNoise(positionWS * 0.6) - 0.5) * 2.0;
                float start = _RevoxelWeather.z;
                float full = _RevoxelWeather.w;
                return saturate((start - air) / max(start - full, 0.01)) * smoothstep(0.55, 0.85, sky);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int id = (int)round(input.materialAo.x);
                id = clamp(id, 0, (int)_RevoxelMaterialTableWidth - 1);
                half3 baseColor = LOAD_TEXTURE2D(_RevoxelMaterialTable, int2(id, 0)).rgb;
                half3 secondary = LOAD_TEXTURE2D(_RevoxelMaterialTable, int2(id, 1)).rgb;
                float4 material = LOAD_TEXTURE2D(_RevoxelMaterialTable, int2(id, 2));
                int pattern = (int)round(material.r * 3.0);
                float scale = material.g * 8.0;
                float contrast = material.b;

                // Pixel footprint in meters relative to the pattern scale: 0.25 starts the
                // fade, 0.75 ends it (sampling the noise coarser than that aliases).
                float footprint = max(length(ddx(input.positionWS)), length(ddy(input.positionWS)));
                contrast *= 1.0 - smoothstep(0.25, 0.75, footprint / max(scale, 0.01));

                half3 albedo = lerp(baseColor, secondary, Pattern(pattern, input.positionWS, scale) * contrast);
                float4 season = LOAD_TEXTURE2D(_RevoxelMaterialTable, int2(id, 3));
                int foliage = (int)round(season.r * 3.0);
                float sky = saturate(input.skyLight);
                float3 normalWS = normalize(input.normalWS);
                albedo = SeasonTint(foliage, albedo, input.positionWS);
                float wet = _RevoxelWeather.x * smoothstep(0.6, 0.95, sky) * (foliage == 0 ? 1.0 : 0.5);
                albedo *= 1.0 - 0.35 * wet;
                albedo = lerp(albedo, _RevoxelSnowColor.rgb, SnowCover(input.positionWS, normalWS, sky));
                half luminance = dot(albedo, half3(0.2126, 0.7152, 0.0722));
                albedo = lerp(albedo, luminance * _RevoxelCorruptionTint.rgb, _RevoxelCorruption);
                half occlusion = lerp(_RevoxelAoMin, 1.0, saturate(input.materialAo.y));

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 direct = mainLight.color * (ndotl * mainLight.shadowAttenuation * mainLight.distanceAttenuation);
                direct *= saturate(sky * 4.0) * RevoxelCloudShadow(input.positionWS, mainLight.direction);
                half3 ambient = SampleSH(normalWS) * lerp(_RevoxelSkyLight.x, 1.0, pow(sky, max(_RevoxelSkyLight.y, 0.01)));

                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);
                    ambient *= ao.indirectAmbientOcclusion;
                    direct *= ao.directAmbientOcclusion;
                #endif

                half3 color = albedo * occlusion * (direct + ambient);
                if (_RevoxelUnderwater > 0.5)
                {
                    float distance = length(input.positionWS - GetCameraPositionWS());
                    float fog = 1.0 - exp(-_RevoxelUnderwaterDensity * distance);
                    half3 lit = mainLight.color * saturate(mainLight.direction.y * 0.5 + 0.5) + SampleSH(half3(0.0, 1.0, 0.0));
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

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
