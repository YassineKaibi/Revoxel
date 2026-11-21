using Unity.Mathematics;

namespace Revoxel.World
{
    // TerrainSettings converted to voxels plus the seeded noise offsets: the blittable
    // input of the generation jobs. Generation is a pure function of these and the chunk
    // coordinate (D-030).
    public struct TerrainParams
    {
        public int SeaLevel;
        public float IslandRadius;
        public float CoastFalloff;
        public float LandHeight;
        public int Octaves;
        public float BaseFrequency;
        public float Gain;
        public float Lacunarity;
        public float SoilMin;
        public float SoilMax;
        public int MaxSoil;
        public float SoilFrequency;
        public float SoilZeroSlope;
        public int SandAbove;
        public float2 HeightOffset;
        public float2 SoilOffset;
        public ushort Water;
        public ushort Sand;
        public ushort Dirt;
        public ushort Grass;
        public ushort Stone;

        // Noise offsets stay small so float precision holds at the island edge.
        private const float OffsetRange = 1000f;

        public static TerrainParams Create(WorldSeed seed, TerrainSettings settings)
        {
            const float v = WorldConstants.VoxelsPerMeter;
            var random = Random.CreateFromIndex((uint)seed.Value);
            var soilMax = settings.SoilMaxMeters * v;
            return new TerrainParams
            {
                SeaLevel = (int)math.round(settings.SeaLevelMeters * v),
                IslandRadius = settings.IslandRadiusMeters * v,
                CoastFalloff = settings.CoastFalloffMeters * v,
                LandHeight = settings.LandHeightMeters * v,
                Octaves = settings.Octaves,
                BaseFrequency = 1f / (settings.BaseScaleMeters * v),
                Gain = settings.Gain,
                Lacunarity = settings.Lacunarity,
                SoilMin = settings.SoilMinMeters * v,
                SoilMax = soilMax,
                MaxSoil = (int)math.ceil(soilMax) + 1,
                SoilFrequency = 1f / (settings.SoilScaleMeters * v),
                SoilZeroSlope = settings.SoilZeroSlope,
                SandAbove = (int)math.round(settings.SandAboveSeaMeters * v),
                HeightOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                SoilOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                Water = settings.Water.Id.Value,
                Sand = settings.Sand.Id.Value,
                Dirt = settings.Dirt.Id.Value,
                Grass = settings.Grass.Id.Value,
                Stone = settings.Stone.Id.Value,
            };
        }
    }
}
