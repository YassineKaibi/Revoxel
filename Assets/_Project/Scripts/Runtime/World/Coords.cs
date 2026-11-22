using Unity.Mathematics;

namespace Revoxel.World
{
    // The only place doing coordinate arithmetic (docs/05 "Code rules"). Voxel coordinates
    // are world coordinates in voxel units; positions are meters.
    public static class Coords
    {
        private const int Size = WorldConstants.ChunkSize;
        private const int Padded = WorldConstants.PaddedSize;

        // Floor division by the chunk size, correct for negative coordinates (the arithmetic
        // shift rounds toward negative infinity).
        public static void WorldToChunk(int3 voxel, out ChunkCoord chunk, out int3 local)
        {
            chunk = new ChunkCoord(voxel >> WorldConstants.ChunkShift);
            local = voxel & WorldConstants.ChunkMask;
        }

        public static ChunkCoord ChunkOf(int3 voxel) =>
            new ChunkCoord(voxel >> WorldConstants.ChunkShift);

        public static int3 ChunkOrigin(ChunkCoord chunk) => chunk.Value * Size;

        public static int3 LocalToWorld(ChunkCoord chunk, int3 local) => chunk.Value * Size + local;

        public static int LocalIndex(int3 local) => LocalIndex(local.x, local.y, local.z);

        public static int LocalIndex(int x, int y, int z) => x + Size * (z + Size * y);

        public static int3 LocalFromIndex(int index) =>
            new int3(
                index & WorldConstants.ChunkMask,
                index >> (2 * WorldConstants.ChunkShift),
                (index >> WorldConstants.ChunkShift) & WorldConstants.ChunkMask
            );

        public static bool IsLocalInRange(int3 local) =>
            math.all(local >= 0) && math.all(local < Size);

        // Index into the padded meshing input; x, y and z go from -1 to ChunkSize.
        public static int PaddedIndex(int x, int y, int z) =>
            (x + 1) + Padded * ((z + 1) + Padded * (y + 1));

        // Min corner of a voxel, in meters. Exact: voxel size is a power of two.
        public static float3 VoxelToWorldPosition(int3 voxel) =>
            (float3)voxel * WorldConstants.VoxelSize;

        // Voxel containing a position in meters (floor on each axis).
        public static int3 WorldPositionToVoxel(float3 position) =>
            (int3)math.floor(position * WorldConstants.VoxelsPerMeter);

        // The chunk column (x, z) a chunk belongs to: the unit of streaming.
        public static int2 ColumnOf(ChunkCoord chunk) => chunk.Value.xz;
    }
}
