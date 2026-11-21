namespace Revoxel.World
{
    // Structural sizes of the demo world (D-011). These are the only literals allowed in
    // World code; they are structure, not tuning.
    public static class WorldConstants
    {
        public const int ChunkSize = 16;
        public const int ChunkShift = 4;
        public const int ChunkMask = ChunkSize - 1;

        public const int WorldSizeX = 256;
        public const int WorldSizeZ = 256;
        public const int WorldHeight = 64;

        public const int ChunksX = WorldSizeX / ChunkSize;
        public const int ChunksY = WorldHeight / ChunkSize;
        public const int ChunksZ = WorldSizeZ / ChunkSize;
        public const int ChunkCount = ChunksX * ChunksY * ChunksZ;

        public const int BlocksPerChunk = ChunkSize * ChunkSize * ChunkSize;
        public const int ColumnCount = WorldSizeX * WorldSizeZ;

        // The meshing input: the chunk plus one layer from each neighbor (D-031).
        public const int PaddedSize = ChunkSize + 2;
        public const int PaddedBlocks = PaddedSize * PaddedSize * PaddedSize;

        // Block ids are bytes, so a lookup table indexed by id has 256 entries.
        public const int MaxBlockTypes = 256;
    }
}
