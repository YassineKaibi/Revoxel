using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Samples the raw land and the rain of every hydrology cell at its center, in parallel
    // (D-077): each cell depends only on the seed. Rain is the cell's area, km², weighted by
    // moisture from PrecipitationDry (dry) to 1 (wet); ocean cells get none.
    [BurstCompile(CompileSynchronously = true)]
    public struct HydroSampleJob : IJobParallelFor
    {
        public const int BatchSize = 64;

        public TerrainParams Params;

        [WriteOnly]
        public NativeArray<float> Land;

        [WriteOnly]
        public NativeArray<float> Rain;

        [WriteOnly]
        public NativeArray<bool> Ocean;

        public void Execute(int index)
        {
            var center = (int2)math.floor(HydroGrid.Center(HydroGrid.Cell(index)));
            var moisture = TerrainGenerator.LandMoisture(Params, center.x, center.y, out var land);
            var ocean = land < Params.SeaLevel;
            Land[index] = land;
            Ocean[index] = ocean;
            Rain[index] = ocean
                ? 0f
                : math.lerp(Params.PrecipitationDry, 1f, moisture) * HydroGrid.CellAreaKm2;
        }
    }
}
