using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Revoxel.World
{
    // Generates one chunk. Fast path: from the column samples, a chunk entirely above the
    // surface and sea level is uniform Air, one entirely below the subsurface band uniform
    // Stone, one between the surface and sea level uniform Water, with no per-voxel work.
    // Result is allocated with Allocator.Persistent and owned by the caller. Compiled
    // synchronously so the editor never mixes managed and Burst chunks (float rounding).
    [BurstCompile(CompileSynchronously = true)]
    public struct ChunkGenJob : IJob
    {
        public TerrainParams Params;
        public ChunkCoord Coord;

        // 32,768 entries, reused between jobs that do not overlap.
        public NativeArray<ushort> Scratch;

        [WriteOnly]
        public NativeReference<ChunkVoxels> Result;

        public void Execute()
        {
            var samples = new NativeArray<TerrainSample>(
                TerrainGenerator.SampleGridCount,
                Allocator.Temp
            );
            TerrainGenerator.ComputeSamples(Params, Coords.ChunkOrigin(Coord).xz, samples);
            var range = TerrainGenerator.HeightRange(samples);
            var kind = TerrainGenerator.Classify(Params, range, Coord.Value.y, out var id);
            if (kind != ChunkKind.Mixed)
            {
                Result.Value = ChunkVoxels.CreateUniform(id, Allocator.Persistent);
            }
            else
            {
                TerrainGenerator.FillFromSamples(Params, Coord, samples, Scratch);
                Result.Value = ChunkVoxels.FromDense(Scratch, Allocator.Persistent);
            }

            samples.Dispose();
        }
    }
}
