using Unity.Mathematics;

namespace Revoxel.World
{
    // The only place doing coordinate arithmetic (docs/05 "Code rules").
    public static class Coords
    {
        private const int Size = WorldConstants.ChunkSize;
        private const int Padded = WorldConstants.PaddedSize;

        // Floor division by the chunk size, correct for negative coordinates (the arithmetic
        // shift rounds toward negative infinity), so the infinite world later needs no change.
        public static void WorldToChunk(int3 world, out ChunkCoord chunk, out int3 local)
        {
            chunk = new ChunkCoord(world >> WorldConstants.ChunkShift);
            local = world & WorldConstants.ChunkMask;
        }

        public static ChunkCoord ChunkOf(int3 world) =>
            new ChunkCoord(world >> WorldConstants.ChunkShift);

        public static int3 ChunkOrigin(ChunkCoord chunk) => chunk.Value * Size;

        public static int3 LocalToWorld(ChunkCoord chunk, int3 local) => chunk.Value * Size + local;

        public static int LocalIndex(int3 local) => LocalIndex(local.x, local.y, local.z);

        public static int LocalIndex(int x, int y, int z) => x + Size * (z + Size * y);

        public static int3 LocalFromIndex(int index) =>
            new int3(index % Size, index / (Size * Size), (index / Size) % Size);

        public static bool IsLocalInRange(int3 local) =>
            math.all(local >= 0) && math.all(local < Size);

        // Index into the padded meshing input; x, y and z go from -1 to ChunkSize.
        public static int PaddedIndex(int x, int y, int z) =>
            (x + 1) + Padded * ((z + 1) + Padded * (y + 1));

        // Index of a world column (x, z) for the zone map and corruption (D-033).
        public static int ColumnIndex(int x, int z) => x + WorldConstants.WorldSizeX * z;

        // World block containing a world-space position (floor on each axis).
        public static int3 WorldPositionToBlock(float3 position) => (int3)math.floor(position);
    }
}
