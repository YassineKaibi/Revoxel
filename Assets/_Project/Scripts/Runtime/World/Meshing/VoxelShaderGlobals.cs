using UnityEngine;

namespace Revoxel.World
{
    // Global inputs of the Revoxel/Voxel shader (D-028, D-044).
    public static class VoxelShaderGlobals
    {
        public const string ShaderName = "Revoxel/Voxel";

        private const float IslandVoxels =
            (WorldConstants.IslandMaxChunkXZ - WorldConstants.IslandMinChunkXZ)
            * WorldConstants.ChunkSize;

        public static readonly int CorruptionTexId = Shader.PropertyToID("_RevoxelCorruptionTex");
        public static readonly int CorruptionTexScaleId = Shader.PropertyToID(
            "_RevoxelCorruptionTexScale"
        );

        // Fully corrupted everywhere until WP-06 drives the texture.
        public static void SetDefaults()
        {
            Shader.SetGlobalTexture(CorruptionTexId, Texture2D.whiteTexture);
            Shader.SetGlobalVector(
                CorruptionTexScaleId,
                new Vector4(1f / IslandVoxels, 1f / IslandVoxels, 0f, 0f)
            );
        }
    }
}
