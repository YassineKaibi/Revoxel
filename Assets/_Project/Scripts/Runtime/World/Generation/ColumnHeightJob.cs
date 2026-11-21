using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // The range of surface heights over one chunk column (D-053), widened by one voxel on
    // each side so a rounding difference between this job and ChunkGenJob never makes a
    // chunk look uniform when it is not. Streaming classifies the column's chunks with it.
    [BurstCompile(CompileSynchronously = true)]
    public struct ColumnHeightJob : IJob
    {
        public TerrainParams Params;
        public int2 Column;

        [WriteOnly]
        public NativeReference<int2> HeightRange;

        public void Execute()
        {
            var origin = Column * WorldConstants.ChunkSize;
            var range = new int2(int.MaxValue, int.MinValue);
            for (var z = 0; z < WorldConstants.ChunkSize; z++)
            for (var x = 0; x < WorldConstants.ChunkSize; x++)
            {
                var h = TerrainGenerator.SurfaceHeight(Params, origin.x + x, origin.y + z);
                range = new int2(math.min(range.x, h), math.max(range.y, h));
            }

            HeightRange.Value = range + new int2(-1, 1);
        }
    }
}
