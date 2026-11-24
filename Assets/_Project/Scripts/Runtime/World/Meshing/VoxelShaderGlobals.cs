using UnityEngine;

namespace Revoxel.World
{
    // Global inputs of the Revoxel/Voxel shader (D-044, D-051).
    public static class VoxelShaderGlobals
    {
        public const string ShaderName = "Revoxel/Voxel";

        public static readonly int MaterialTableId = Shader.PropertyToID("_RevoxelMaterialTable");
        public static readonly int MaterialTableWidthId = Shader.PropertyToID(
            "_RevoxelMaterialTableWidth"
        );
        public static readonly int CorruptionId = Shader.PropertyToID("_RevoxelCorruption");
        public static readonly int CorruptionTintId = Shader.PropertyToID("_RevoxelCorruptionTint");
        public static readonly int AoMinId = Shader.PropertyToID("_RevoxelAoMin");

        // Water look (D-073), read by Revoxel/Water; the underwater fog also by Revoxel/Voxel.
        public const string WaterShaderName = "Revoxel/Water";
        public static readonly int WaterScatterId = Shader.PropertyToID("_RevoxelWaterScatter");
        public static readonly int WaterAbsorptionId = Shader.PropertyToID(
            "_RevoxelWaterAbsorption"
        );
        public static readonly int PollutedWaterScatterId = Shader.PropertyToID(
            "_RevoxelPollutedWaterScatter"
        );
        public static readonly int PollutedWaterAbsorptionId = Shader.PropertyToID(
            "_RevoxelPollutedWaterAbsorption"
        );
        public static readonly int WaterSurfaceId = Shader.PropertyToID("_RevoxelWaterSurface");
        public static readonly int WaterFlowId = Shader.PropertyToID("_RevoxelWaterFlow");
        public static readonly int WaterFoamColorId = Shader.PropertyToID("_RevoxelWaterFoamColor");
        public static readonly int UnderwaterId = Shader.PropertyToID("_RevoxelUnderwater");
        public static readonly int UnderwaterColorId = Shader.PropertyToID(
            "_RevoxelUnderwaterColor"
        );
        public static readonly int UnderwaterDensityId = Shader.PropertyToID(
            "_RevoxelUnderwaterDensity"
        );

        // The table from VoxelRegistry.CreateMaterialTable; the caller owns it.
        public static void SetMaterialTable(Texture2D table)
        {
            Shader.SetGlobalTexture(MaterialTableId, table);
            Shader.SetGlobalFloat(MaterialTableWidthId, table.width);
        }

        // One value for the whole world until the corruption WP streams a field (D-051).
        public static void SetCorruption(float corruption)
        {
            Shader.SetGlobalFloat(CorruptionId, Mathf.Clamp01(corruption));
        }

        // Clean (corruption 0, D-093) until the corruption WP streams a field, with the
        // settings' tint and occlusion floor. The tint is passed as a vector in linear space,
        // so no color space conversion applies.
        public static void SetDefaults(WorldRenderSettings settings)
        {
            SetCorruption(0f);
            Shader.SetGlobalVector(CorruptionTintId, settings.CorruptionTint.linear);
            Shader.SetGlobalFloat(AoMinId, settings.AoMin);
            Shader.SetGlobalVector(WaterScatterId, settings.WaterScatter.linear);
            Shader.SetGlobalVector(WaterAbsorptionId, settings.WaterAbsorptionPerMeter);
            Shader.SetGlobalVector(PollutedWaterScatterId, settings.PollutedWaterScatter.linear);
            Shader.SetGlobalVector(
                PollutedWaterAbsorptionId,
                settings.PollutedWaterAbsorptionPerMeter
            );

            // (refraction, wave frequency per meter, wave strength, wave speed m/s)
            Shader.SetGlobalVector(
                WaterSurfaceId,
                new Vector4(
                    settings.WaterRefraction,
                    1f / settings.WaterWaveScaleMeters,
                    settings.WaterWaveStrength,
                    settings.WaterWaveSpeed
                )
            );

            // (fall speed m/s, foam depth m, smoothness, pollution share)
            Shader.SetGlobalVector(
                WaterFlowId,
                new Vector4(
                    settings.WaterFallSpeed,
                    settings.WaterFoamDepthMeters,
                    settings.WaterSmoothness,
                    settings.WaterPollutionShare
                )
            );
            Shader.SetGlobalVector(WaterFoamColorId, settings.WaterFoamColor.linear);
            Shader.SetGlobalVector(UnderwaterColorId, settings.UnderwaterColor.linear);
            Shader.SetGlobalFloat(UnderwaterDensityId, settings.UnderwaterDensityPerMeter);
            SetUnderwater(false);
        }

        // Underwater fog on (the main camera is in a liquid) or off.
        public static void SetUnderwater(bool underwater)
        {
            Shader.SetGlobalFloat(UnderwaterId, underwater ? 1f : 0f);
        }
    }
}
