using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // The tree parts of one chunk column, in Burst for callers outside the streaming jobs
    // (TerrainGenerator.FillChunk, tests), so every caller grows the same trees (R-21).
    [BurstCompile(CompileSynchronously = true)]
    public struct VegetationCollectJob : IJob
    {
        public TerrainParams Params;
        public int2 Column;

        public NativeList<TreePart> Parts;

        [WriteOnly]
        public NativeReference<int2> Heights;

        public void Execute()
        {
            Vegetation.CollectColumn(Params, Column, Parts, out var heights);
            Heights.Value = heights;
        }

        // Runs the job now; the caller disposes the list.
        public static NativeList<TreePart> Collect(
            in TerrainParams p,
            int2 column,
            out int2 heights
        )
        {
            var parts = new NativeList<TreePart>(64, Allocator.TempJob);
            using var range = new NativeReference<int2>(Allocator.TempJob);
            new VegetationCollectJob
            {
                Params = p,
                Column = column,
                Parts = parts,
                Heights = range,
            }
                .Schedule()
                .Complete();
            heights = range.Value;
            return parts;
        }
    }
}
