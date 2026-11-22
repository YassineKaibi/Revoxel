namespace Revoxel.World
{
    // Counters of the distance detail, for the debug overlay and the tests.
    public struct DetailStats
    {
        // Coarse cells known (wanted, or shown while being replaced).
        public int Cells;
        public int ShownCells;
        public int Views;

        // Busy job slots plus cells waiting for a job.
        public int PendingJobs;

        public long MeshBytes;
        public float LastMainThreadMs;
    }
}
