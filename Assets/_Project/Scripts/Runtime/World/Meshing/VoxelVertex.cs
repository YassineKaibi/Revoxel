using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Revoxel.World
{
    // One vertex of a chunk render mesh; matches ChunkMesher.CreateRenderLayout.
    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelVertex
    {
        // Meters, chunk-local.
        public float3 Position;
        public float3 Normal;

        // x: voxel id. y: occlusion 0 to 1, where 1 is open.
        public float2 MaterialAo;

        // Sky light of the corner, 0 to 1 (D-097).
        public float SkyLight;
    }
}
