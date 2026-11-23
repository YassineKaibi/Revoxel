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
        public static readonly int StepNormalBendId = Shader.PropertyToID("_RevoxelStepNormalBend");

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

        // Fully corrupted, with the settings' tint and occlusion floor. The tint is passed
        // as a vector in linear space, so no color space conversion applies.
        public static void SetDefaults(WorldRenderSettings settings)
        {
            SetCorruption(1f);
            Shader.SetGlobalVector(CorruptionTintId, settings.CorruptionTint.linear);
            Shader.SetGlobalFloat(AoMinId, settings.AoMin);
            Shader.SetGlobalFloat(StepNormalBendId, settings.StepNormalBend);
        }
    }
}
