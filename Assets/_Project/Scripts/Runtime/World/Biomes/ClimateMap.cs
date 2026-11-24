using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Revoxel.World
{
    // The sea-level air temperature of every 32 m cell of the hydrology grid (D-077), baked
    // once per seed into a float texture that the voxel shader reads for snow (D-101).
    public static class ClimateMap
    {
        public const int Size = HydroGrid.Size;
        public const float CellMeters = HydroGrid.CellVoxels * WorldConstants.VoxelSize;
        public const float SizeMeters = Size * CellMeters;
        public const float OriginMeters = HydroGrid.OriginVoxel * WorldConstants.VoxelSize;

        // Samples every cell center in parallel. The caller owns the array.
        public static NativeArray<float> Sample(in TerrainParams p, Allocator allocator)
        {
            var temperatures = new NativeArray<float>(
                HydroGrid.CellCount,
                allocator,
                NativeArrayOptions.UninitializedMemory
            );
            new ClimateMapJob { Params = p, Temperatures = temperatures }
                .Schedule(HydroGrid.CellCount, HydroSampleJob.BatchSize)
                .Complete();
            return temperatures;
        }

        // A bilinear, clamped RFloat texture of the samples; the caller owns it.
        public static Texture2D CreateTexture(NativeArray<float> temperatures)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RFloat, false, true)
            {
                name = "RevoxelClimateMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixelData(temperatures, 0);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct ClimateMapJob : IJobParallelFor
    {
        public TerrainParams Params;

        [WriteOnly]
        public NativeArray<float> Temperatures;

        public void Execute(int index)
        {
            var center = (int2)math.floor(HydroGrid.Center(HydroGrid.Cell(index)));
            Temperatures[index] = TerrainGenerator.SeaTemperatureC(Params, center.x, center.y);
        }
    }
}
