namespace Revoxel.World
{
    // Structural sizes of the world (D-050). These are the only literals allowed in
    // World code; they are structure, not tuning.
    public static class WorldConstants
    {
        // A voxel is 0.125 m: eight per meter.
        public const float VoxelSize = 0.125f;
        public const int VoxelsPerMeter = 8;

        public const int ChunkSize = 32;
        public const int ChunkShift = 5;
        public const int ChunkMask = ChunkSize - 1;
        public const int VoxelsPerChunk = ChunkSize * ChunkSize * ChunkSize;

        // The meshing input: the chunk plus one layer from each of its 26 neighbors.
        public const int PaddedSize = ChunkSize + 2;
        public const int PaddedVoxels = PaddedSize * PaddedSize * PaddedSize;

        // The island: chunk x and z from -1,000 to 999, chunk y from 0 to 127
        // (8,000 m by 512 m, centered on the origin).
        public const int IslandMinChunkXZ = -1000;
        public const int IslandMaxChunkXZ = 1000;
        public const int ChunksY = 128;
        public const int WorldHeightVoxels = ChunksY * ChunkSize;

        // Voxel ids are ushort; a lookup table indexed by id has this many entries.
        public const int MaxVoxelTypes = 1024;

        // Coarse detail levels L = 1 to 7 at most: a level-L chunk column holds
        // ChunksY >> L chunks (D-055, D-057).
        public const int MaxDetailLevels = 7;
    }
}
