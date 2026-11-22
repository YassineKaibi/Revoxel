namespace Revoxel.World
{
    // Counters of the streamed world, for the debug overlay and the tests.
    public struct WorldStats
    {
        public int LoadedChunks;
        public int UniformChunks;
        public int ViewCount;

        // Columns being generated.
        public int PendingGeneration;

        // Dirty chunks plus mesh jobs in flight.
        public int PendingMeshing;

        public long VoxelMemoryBytes;
        public float LastMainThreadMs;

        // Chunks that reached meshing while a neighbor was missing (a seam); always 0.
        public int MeshedWithMissingNeighbors;
    }
}
