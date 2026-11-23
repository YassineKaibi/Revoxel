using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // TerrainSettings and the biomes converted to voxels plus the seeded noise offsets: the
    // blittable input of the generation jobs. Generation is a pure function of these and the
    // chunk coordinate (D-030). Lengths are in voxels unless the name says otherwise.
    public struct TerrainParams
    {
        public int SeaLevel;
        public float IslandRadius;
        public float EdgeRadius;
        public float CoastNoise;
        public float CoastFrequency;
        public float Warp;
        public float WarpFrequency;
        public float LandRise;
        public float SeaDepth;
        public float ShelfWidth;
        public float MountainHeight;
        public float RidgeFrequency;
        public int RidgeOctaves;
        public float ReliefFrequency;
        public float ReliefThreshold;
        public float ReliefRamp;
        public float MassifShare;
        public float HillFrequency;
        public int HillOctaves;
        public float Gain;
        public float Lacunarity;
        public float TerraceStep;
        public float TerraceRiser;
        public float TemperatureMinC;
        public float TemperatureMaxC;
        public float TemperatureFrequency;
        public float TemperatureGradient;
        public float LapseRateCPerVoxel;
        public float SnowTemperatureC;
        public float SnowMaxSlope;
        public float MoistureFrequency;
        public float CoastMoisture;
        public float WasteFrequency;
        public float WasteThreshold;
        public float BlendTemperatureC;
        public float BlendMoisture;
        public float BlendInland;
        public float BlendAltitudeMeters;
        public float BlendWaste;
        public float BorderJitter;
        public float BorderFrequency;
        public float PatchFrequency;
        public float PatchEdge;
        public float PatchEdgeFrequency;
        public int SubsurfaceDepth;
        public float SoilFrequency;
        public float SoilZeroSlope;
        public int ShoreHeight;
        public float Fold;
        public float FoldFrequency;
        public float StrataJitter;
        public float StrataThinning;
        public float Wobble;
        public float WobbleFrequency;
        public int PocketCell;
        public int PocketCellHeight;
        public float2 PocketRadius;
        public ushort Water;
        public ushort Stone;
        public ushort Snow;
        public uint HashSeed;
        public FixedList32Bytes<ushort> PocketTypes;
        public FixedList4096Bytes<BiomeParams> Biomes;

        // Seeded noise offsets, one per field.
        public float2 WarpOffsetX;
        public float2 WarpOffsetZ;
        public float2 CoastOffset;
        public float2 ReliefOffset;
        public float2 RidgeOffset;
        public float2 HillOffset;
        public float2 TemperatureOffset;
        public float2 MoistureOffset;
        public float2 WasteOffset;
        public float2 BorderOffset;
        public float2 PatchOffset;
        public float2 PatchEdgeOffset;
        public float2 SoilOffset;
        public float2 FoldOffset;
        public float2 WobbleOffset;
        public float2 ThinningOffset;

        // Noise offsets stay small so float precision holds at the island edge.
        private const float OffsetRange = 1000f;

        public static TerrainParams Create(WorldSeed seed, TerrainSettings settings)
        {
            const float v = WorldConstants.VoxelsPerMeter;
            var random = Random.CreateFromIndex((uint)seed.Value);
            var p = new TerrainParams
            {
                SeaLevel = (int)math.round(settings.SeaLevelMeters * v),
                IslandRadius = settings.IslandRadiusMeters * v,
                EdgeRadius = settings.EdgeRadiusMeters * v,
                CoastNoise = settings.CoastNoise,
                CoastFrequency = Frequency(settings.CoastScaleMeters),
                Warp = settings.WarpMeters * v,
                WarpFrequency = Frequency(settings.WarpScaleMeters),
                LandRise = settings.LandRiseMeters * v,
                SeaDepth = settings.SeaDepthMeters * v,
                ShelfWidth = settings.ShelfWidth,
                MountainHeight = settings.MountainHeightMeters * v,
                RidgeFrequency = Frequency(settings.RidgeScaleMeters),
                RidgeOctaves = settings.RidgeOctaves,
                ReliefFrequency = Frequency(settings.ReliefScaleMeters),
                ReliefThreshold = settings.ReliefThreshold,
                ReliefRamp = settings.ReliefRamp,
                MassifShare = settings.MassifShare,
                HillFrequency = Frequency(settings.HillScaleMeters),
                HillOctaves = settings.HillOctaves,
                Gain = settings.Gain,
                Lacunarity = settings.Lacunarity,
                TerraceStep = settings.TerraceStepMeters * v,
                TerraceRiser = settings.TerraceRiser,
                TemperatureMinC = settings.TemperatureMinC,
                TemperatureMaxC = settings.TemperatureMaxC,
                TemperatureFrequency = Frequency(settings.TemperatureScaleMeters),
                TemperatureGradient = settings.TemperatureGradient,
                LapseRateCPerVoxel = settings.LapseRateCPerKm / (1000f * v),
                SnowTemperatureC = settings.SnowTemperatureC,
                SnowMaxSlope = settings.SnowMaxSlope,
                MoistureFrequency = Frequency(settings.MoistureScaleMeters),
                CoastMoisture = settings.CoastMoisture,
                WasteFrequency = Frequency(settings.WasteScaleMeters),
                WasteThreshold = settings.WasteThreshold,
                BlendTemperatureC = settings.BlendTemperatureC,
                BlendMoisture = settings.BlendMoisture,
                BlendInland = settings.BlendInland,
                BlendAltitudeMeters = settings.BlendAltitudeMeters,
                BlendWaste = settings.BlendWaste,
                BorderJitter = settings.BorderJitter,
                BorderFrequency = Frequency(settings.BorderScaleMeters),
                PatchFrequency = Frequency(settings.PatchScaleMeters),
                PatchEdge = settings.PatchEdge,
                PatchEdgeFrequency = Frequency(settings.PatchEdgeScaleMeters),
                SubsurfaceDepth = (int)math.round(settings.SubsurfaceDepthMeters * v),
                SoilFrequency = Frequency(settings.SoilScaleMeters),
                SoilZeroSlope = settings.SoilZeroSlope,
                ShoreHeight = (int)math.round(settings.ShoreHeightMeters * v),
                Fold = settings.FoldMeters * v,
                FoldFrequency = Frequency(settings.FoldScaleMeters),
                StrataJitter = settings.StrataJitter,
                StrataThinning = settings.StrataThinning,
                Wobble = settings.WobbleMeters * v,
                WobbleFrequency = Frequency(settings.WobbleScaleMeters),
                PocketCell = math.max(1, (int)math.round(settings.PocketCellMeters * v)),
                PocketCellHeight = math.max(
                    1,
                    (int)math.round(settings.PocketCellHeightMeters * v)
                ),
                PocketRadius = (float2)settings.PocketRadiusMeters * v,
                Water = settings.Water.Id.Value,
                Stone = settings.Stone.Id.Value,
                Snow = settings.Snow.Id.Value,
                HashSeed = random.NextUInt(),
                WarpOffsetX = random.NextFloat2(-OffsetRange, OffsetRange),
                WarpOffsetZ = random.NextFloat2(-OffsetRange, OffsetRange),
                CoastOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                ReliefOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                RidgeOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                HillOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                TemperatureOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                MoistureOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                WasteOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                BorderOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                PatchOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                SoilOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                FoldOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                WobbleOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                ThinningOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                PatchEdgeOffset = random.NextFloat2(-OffsetRange, OffsetRange),
            };

            foreach (var type in settings.PocketTypes)
            {
                p.PocketTypes.Add(type.Id.Value);
            }

            foreach (var biome in settings.Biomes.Biomes)
            {
                p.Biomes.Add(BiomeParams.Create(biome));
            }

            return p;
        }

        private static float Frequency(float scaleMeters) =>
            1f / (scaleMeters * WorldConstants.VoxelsPerMeter);
    }
}
