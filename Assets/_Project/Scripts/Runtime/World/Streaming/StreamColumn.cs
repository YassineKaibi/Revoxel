using System.Collections.Generic;
using Unity.Mathematics;

namespace Revoxel.World
{
    internal enum ColumnStage : byte
    {
        Heights,
        Chunks,
        Loaded,
    }

    // One chunk column (the unit of streaming) while it generates and once it is loaded.
    // Pooled by VoxelWorld.
    internal sealed class StreamColumn
    {
        public readonly Chunk[] Chunks = new Chunk[WorldConstants.ChunksY];

        // Generated voxels waiting until the whole column is ready.
        public readonly ChunkVoxels[] Pending = new ChunkVoxels[WorldConstants.ChunksY];

        // Chunk heights (cy) that still need a ChunkGenJob (D-053).
        public readonly List<int> MixedQueue = new List<int>(16);

        // Dirty chunks of this column, waiting to be meshed.
        public readonly List<Chunk> Dirty = new List<Chunk>(32);

        public int2 Coord;
        public ColumnStage Stage;
        public int HeightSlot = -1;
        public int MixedInFlight;

        public bool HasJobsInFlight => HeightSlot >= 0 || MixedInFlight > 0;

        public void Reset(int2 coord, ColumnStage stage)
        {
            Coord = coord;
            Stage = stage;
            HeightSlot = -1;
            MixedInFlight = 0;
            MixedQueue.Clear();
            Dirty.Clear();
            for (var i = 0; i < Chunks.Length; i++)
            {
                Chunks[i] = null;
                Pending[i] = default;
            }
        }

        // Frees generated voxels that were never inserted.
        public void DisposePending()
        {
            for (var i = 0; i < Pending.Length; i++)
            {
                if (Pending[i].IsCreated)
                {
                    Pending[i].Dispose();
                }

                Pending[i] = default;
            }
        }
    }
}
