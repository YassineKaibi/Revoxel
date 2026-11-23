using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // The surface height and water level ranges of one coarse cell (PLAN-03 Task 2,
    // D-070), from which the distance detail decides which of the cell's chunks to mesh.
    [BurstCompile(CompileSynchronously = true)]
    public struct DetailHeightJob : IJob
    {
        public TerrainParams Params;
        public int Level;
        public int2 Cell;

        [WriteOnly]
        public NativeReference<ColumnRange> Range;

        public void Execute()
        {
            Range.Value = TerrainGenerator.DetailRange(Params, Cell, Level);
        }
    }
}
