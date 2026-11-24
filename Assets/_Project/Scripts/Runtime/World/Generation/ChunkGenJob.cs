using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Revoxel.World
{
    // Generates one chunk. Fast path: from the column samples, a chunk entirely above the
    // surface, the water and the vegetation is uniform Air, one entirely below the
    // subsurface band uniform Stone, one between the surface and the water level uniform
    // Water, with no per-voxel work. A chunk above the ground, the water and the plants
    // that only trees reach (KnownRange from the column's height job) is filled from its
    // tree parts without sampling the terrain.
    // Result is allocated with Allocator.Persistent and owned by the caller. Compiled
    // synchronously so the editor never mixes managed and Burst chunks (float rounding).
    [BurstCompile(CompileSynchronously = true)]
    public struct ChunkGenJob : IJob
    {
        public TerrainParams Params;
        public ChunkCoord Coord;

        // The parts of the trees reaching into the chunk's column (ColumnHeightJob).
        [ReadOnly]
        public NativeArray<TreePart> Parts;

        // The column's range from its height job, when HasKnownRange.
        public ColumnRange KnownRange;
        public bool HasKnownRange;

        // 32,768 entries, reused between jobs that do not overlap.
        public NativeArray<ushort> Scratch;

        [WriteOnly]
        public NativeReference<ChunkVoxels> Result;

        public void Execute()
        {
            var origin = Coords.ChunkOrigin(Coord);
            if (
                HasKnownRange
                && origin.y
                    >= KnownRange.Height.y
                        + Params.Vegetation.MaxPlantHeight
                        + TerrainGenerator.BendMargin
                && origin.y >= KnownRange.Water.y
            )
            {
                for (var i = 0; i < Scratch.Length; i++)
                {
                    Scratch[i] = VoxelId.Air.Value;
                }

                Vegetation.Rasterize(Params, Parts, origin, default, true, Scratch);
                Result.Value = ChunkVoxels.FromDense(Scratch, Allocator.Persistent);
                return;
            }

            var samples = new NativeArray<TerrainSample>(
                TerrainGenerator.SampleGridCount,
                Allocator.Temp
            );
            TerrainGenerator.ComputeSamples(Params, origin.xz, samples);
            var range = TerrainGenerator.Range(Params, samples);
            for (var i = 0; i < Parts.Length; i++)
            {
                range.AddVegetation(new Unity.Mathematics.int2(Parts[i].Min.y, Parts[i].Max.y + 1));
            }

            var kind = TerrainGenerator.Classify(Params, range, Coord.Value.y, out var id);
            if (kind != ChunkKind.Mixed)
            {
                Result.Value = ChunkVoxels.CreateUniform(id, Allocator.Persistent);
            }
            else
            {
                TerrainGenerator.FillFromSamples(Params, Coord, samples, Parts, Scratch);
                Result.Value = ChunkVoxels.FromDense(Scratch, Allocator.Persistent);
            }

            samples.Dispose();
        }
    }
}
