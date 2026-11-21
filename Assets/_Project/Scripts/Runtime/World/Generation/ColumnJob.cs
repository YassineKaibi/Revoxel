using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Revoxel.World
{
    // Column pass over the demo island: zone and height per column (x + 256 * z).
    [BurstCompile]
    public struct ColumnJob : IJobParallelFor
    {
        public WorldGenParams Params;

        [WriteOnly]
        public NativeArray<byte> ZoneMap;

        [WriteOnly]
        public NativeArray<int> HeightMap;

        public void Execute(int index)
        {
            var x = index % WorldConstants.WorldSizeX;
            var z = index / WorldConstants.WorldSizeX;
            var height = WorldGenerator.ColumnHeight(Params, x, z);
            HeightMap[index] = height;
            ZoneMap[index] = (byte)WorldGenerator.ColumnZone(Params, x, z, height);
        }
    }
}
