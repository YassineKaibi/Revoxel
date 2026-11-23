using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // The ranges of surface heights and water levels over one chunk column (D-053, D-070),
    // heights widened by one voxel on each side so a rounding difference between this job
    // and ChunkGenJob never makes a chunk look uniform when it is not. Streaming classifies
    // the column's chunks with it.
    [BurstCompile(CompileSynchronously = true)]
    public struct ColumnHeightJob : IJob
    {
        public TerrainParams Params;
        public int2 Column;

        [WriteOnly]
        public NativeReference<ColumnRange> Range;

        public void Execute()
        {
            var origin = Column * WorldConstants.ChunkSize;
            var range = ColumnRange.Empty;
            for (var z = 0; z < WorldConstants.ChunkSize; z++)
            for (var x = 0; x < WorldConstants.ChunkSize; x++)
            {
                var sample = TerrainGenerator.Sample(Params, origin.x + x, origin.y + z);
                range.Add((int)math.floor(sample.Height), sample.WaterLevel);
            }

            Range.Value = range.WidenedHeights(1);
        }
    }
}
