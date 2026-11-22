using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // The surface height range of one coarse cell (PLAN-03 Task 2), from which the
    // distance detail decides which of the cell's chunks to mesh.
    [BurstCompile(CompileSynchronously = true)]
    public struct DetailHeightJob : IJob
    {
        public TerrainParams Params;
        public int Level;
        public int2 Cell;

        [WriteOnly]
        public NativeReference<int2> HeightRange;

        public void Execute()
        {
            HeightRange.Value = TerrainGenerator.DetailHeightRange(Params, Cell, Level);
        }
    }
}
