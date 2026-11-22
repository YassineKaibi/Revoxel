using System.Collections.Generic;
using Unity.Mathematics;

namespace Revoxel.World
{
    internal enum DetailCellState : byte
    {
        // Waiting for its height job.
        New,

        // Height job in flight.
        Heights,

        // Chunk fill and mesh jobs queued or in flight.
        Chunks,

        // Every chunk that can show something is meshed.
        Ready,
    }

    // One coarse chunk column (a cell of a detail level) while it generates, while it is
    // ready, and while it is shown. Pooled by DistanceDetail.
    internal sealed class DetailCell
    {
        // Chunk heights (cy) still to fill and mesh.
        public readonly List<int> Queue = new List<int>(8);

        public readonly List<DetailView> Views = new List<DetailView>(4);

        public int Level;
        public int2 Coord;
        public DetailCellState State;
        public bool Wanted;
        public bool Shown;
        public int JobsInFlight;
        public long MeshBytes;

        // Bumped on reuse: a job result applies only to the cell version it was made for.
        public int Version;

        // Squared distance to the full-detail center, in full-resolution columns.
        public float SortKey;

        public bool HasJobsInFlight => State == DetailCellState.Heights || JobsInFlight > 0;

        public void Reset(int level, int2 coord)
        {
            Level = level;
            Coord = coord;
            State = DetailCellState.New;
            Wanted = true;
            Shown = false;
            JobsInFlight = 0;
            MeshBytes = 0;
            Version++;
            Queue.Clear();
            Views.Clear();
        }
    }
}
