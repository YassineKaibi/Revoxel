using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Biome and climate queries for other systems (PLAN-04 Task 2): purification meters,
    // temperature, creatures and animals ask here rather than reading generator internals.
    // Pure and Burst-callable, like the generator; positions are voxel columns unless the
    // name says meters.
    public static class BiomeMap
    {
        public static TerrainSample Sample(in TerrainParams p, int x, int z) =>
            TerrainGenerator.Sample(p, x, z);

        // The biome a column belongs to: the one its ground is made of.
        public static BiomeId BiomeAt(in TerrainParams p, int x, int z) =>
            TerrainGenerator.Sample(p, x, z).Biome;

        // Every biome's weight at a column, in id order, summing to 1.
        public static void Weights(
            in TerrainParams p,
            int x,
            int z,
            ref FixedList128Bytes<float> weights
        ) => TerrainGenerator.BiomeWeights(p, x, z, ref weights);

        // Air temperature at a point, °C: the column's sea-level temperature minus the lapse
        // rate times the height above sea level (weather, seasons and day come later).
        public static float TemperatureAt(in TerrainParams p, float3 positionMeters)
        {
            var voxel = Coords.WorldPositionToVoxel(positionMeters);
            var sample = TerrainGenerator.Sample(p, voxel.x, voxel.z);
            var above = math.max(0f, positionMeters.y * WorldConstants.VoxelsPerMeter - p.SeaLevel);
            return sample.SeaTemperatureC - p.LapseRateCPerVoxel * above;
        }
    }
}
