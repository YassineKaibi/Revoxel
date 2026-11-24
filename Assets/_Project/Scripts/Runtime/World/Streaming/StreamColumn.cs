using System.Collections.Generic;
using Unity.Collections;
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

        // From the height job, while the column's chunks generate (D-084): its range and
        // the parts of the trees reaching into it.
        public ColumnRange Range;
        public NativeArray<TreePart> Parts;
        public int HeightSlot = -1;
        public int MixedInFlight;

        // Drawn (under DistanceDetail; always true in effect otherwise).
        public bool Shown;

        // Sky heights per voxel column, in voxels (D-097): above the highest voxel that
        // blocks or filters sky light, and above the highest that blocks it; 0 for none.
        // Indexed by SkyLight.ColumnIndex. Kept by the column object across reuse.
        public NativeArray<ushort> SkyAny;
        public NativeArray<ushort> SkyFull;

        public bool HasJobsInFlight => HeightSlot >= 0 || MixedInFlight > 0;

        public void Reset(int2 coord, ColumnStage stage)
        {
            Coord = coord;
            Stage = stage;
            HeightSlot = -1;
            MixedInFlight = 0;
            Shown = false;
            MixedQueue.Clear();
            Dirty.Clear();
            if (!SkyAny.IsCreated)
            {
                SkyAny = new NativeArray<ushort>(SkyLight.ColumnCells, Allocator.Persistent);
                SkyFull = new NativeArray<ushort>(SkyLight.ColumnCells, Allocator.Persistent);
            }
            else
            {
                for (var i = 0; i < SkyLight.ColumnCells; i++)
                {
                    SkyAny[i] = 0;
                    SkyFull[i] = 0;
                }
            }

            for (var i = 0; i < Chunks.Length; i++)
            {
                Chunks[i] = null;
                Pending[i] = default;
            }
        }

        public void DisposeParts()
        {
            if (Parts.IsCreated)
            {
                Parts.Dispose();
            }

            Parts = default;
        }

        // Raises the sky heights by a chunk's tops (SkyLight.ChunkTops) at chunk height cy.
        public void AddSkyTops(int cy, NativeArray<byte> topAny, NativeArray<byte> topFull)
        {
            var baseY = cy * WorldConstants.ChunkSize;
            for (var i = 0; i < SkyLight.ColumnCells; i++)
            {
                if (topAny[i] > 0)
                {
                    SkyAny[i] = (ushort)System.Math.Max(SkyAny[i], baseY + topAny[i]);
                }

                if (topFull[i] > 0)
                {
                    SkyFull[i] = (ushort)System.Math.Max(SkyFull[i], baseY + topFull[i]);
                }
            }
        }

        // Raises every sky height to the top of a uniform chunk that blocks or filters.
        public void AddUniformSky(int cy, byte skyClass)
        {
            if (skyClass == SkyClass.Pass)
            {
                return;
            }

            var top = (ushort)((cy + 1) * WorldConstants.ChunkSize);
            for (var i = 0; i < SkyLight.ColumnCells; i++)
            {
                SkyAny[i] = (ushort)System.Math.Max(SkyAny[i], top);
                if (skyClass == SkyClass.Block)
                {
                    SkyFull[i] = (ushort)System.Math.Max(SkyFull[i], top);
                }
            }
        }

        public void DisposeSky()
        {
            if (SkyAny.IsCreated)
            {
                SkyAny.Dispose();
                SkyFull.Dispose();
            }

            SkyAny = default;
            SkyFull = default;
        }

        // Frees generated voxels that were never inserted, and the tree parts.
        public void DisposePending()
        {
            DisposeParts();
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
