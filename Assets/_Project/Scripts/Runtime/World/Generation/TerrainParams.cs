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

        // Rivers and lakes (PLAN-05, D-070, D-071), voxels; planned by Hydrology.Plan.
        public float PrecipitationDry;
        public int ErosionSteps;
        public float ErosionStrength;
        public float ErosionChannelAreaKm2;
        public float Diffusion;
        public float MaxErosion;
        public float LakeMinDepth;
        public int LakeMinCells;
        public float LakeMargin;
        public float ChannelArea;
        public float HalfWidthAtStart;
        public float MinHalfWidth;
        public float MaxHalfWidth;
        public float DepthAtStart;
        public float MinDepth;
        public float MaxDepth;
        public int SmoothingIterations;
        public float MeanderAmplitude;
        public float MeanderWavelengthWidths;
        public float MeanderMinWavelength;
        public float MeanderMin;
        public float DepthVariation;
        public float BendDepthShift;

        // Length of a river segment, voxels.
        public float RiverStep;
        public float RiverInset;

        // Length of a river's mouth at a lake (voxels) and how much further it lowers land.
        public float FlareLength;
        public float MouthSpread;
        public float FallMin;
        public float FallSteepness;
        public float BankWidth;
        public float BankLip;
        public float ShoreCut;
        public float ShoreRun;
        public float MaxWallWidth;

        // Farthest a river changes the ground from its path, voxels.
        public float RiverReach;
        public float LakeLevelStep;
        public ushort RiverBed;
        public ushort LakeBed;

        // The planned lakes and rivers (D-078); owned by whoever called Create.
        public WaterPlan WaterPlan;

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
        public float2 ReefOffset;
        public float2 MeanderOffset;

        // Noise offsets stay small so float precision holds at the island edge.
        private const float OffsetRange = 1000f;

        // Builds the parameters and their water plan, waiting for the plan's jobs.
        public static TerrainParams Create(WorldSeed seed, TerrainSettings settings) =>
            Schedule(seed, settings).Complete();

        // Builds the parameters now and schedules the water plan's jobs (D-077); the
        // caller completes the build when it is done (or disposes it).
        public static TerrainParamsBuild Schedule(WorldSeed seed, TerrainSettings settings) =>
            new TerrainParamsBuild(Settle(seed, settings), settings.WaterSettings != null);

        // Every value but the water plan.
        private static TerrainParams Settle(WorldSeed seed, TerrainSettings settings)
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
                ReefOffset = random.NextFloat2(-OffsetRange, OffsetRange),
                MeanderOffset = random.NextFloat2(-OffsetRange, OffsetRange),
            };

            foreach (var type in settings.PocketTypes)
            {
                p.PocketTypes.Add(type.Id.Value);
            }

            foreach (var biome in settings.Biomes.Biomes)
            {
                p.Biomes.Add(BiomeParams.Create(biome));
            }

            var water = settings.WaterSettings;
            if (water != null)
            {
                SetWater(ref p, water);
            }

            return p;
        }

        internal static WaterPlan EmptyPlan(Allocator allocator) =>
            new WaterPlan
            {
                Nodes = new NativeArray<RiverNode>(0, allocator),
                Rivers = new NativeArray<River>(0, allocator),
                Lakes = new NativeArray<Lake>(0, allocator),
                Delta = new NativeArray<float>(0, allocator),
                LakeOf = new NativeArray<ushort>(0, allocator),
                CellStart = new NativeArray<int>(0, allocator),
                CellRuns = new NativeArray<int3>(0, allocator),
            };

        // Frees the water plan. Only the caller of Create disposes; copies share the plan.
        public void Dispose()
        {
            WaterPlan.Dispose();
        }

        private static void SetWater(ref TerrainParams p, WaterSettings w)
        {
            const float v = WorldConstants.VoxelsPerMeter;
            p.PrecipitationDry = w.PrecipitationDry;
            p.ErosionSteps = w.ErosionSteps;
            p.ErosionStrength = w.ErosionStrength;
            p.ErosionChannelAreaKm2 = w.ErosionChannelAreaKm2;
            p.Diffusion = w.Diffusion;
            p.MaxErosion = w.MaxErosionMeters * v;
            p.LakeMinDepth = w.LakeMinDepthMeters * v;
            p.LakeMinCells = w.LakeMinCells;
            p.LakeMargin = w.LakeMarginMeters * v;
            p.ChannelArea = w.ChannelAreaKm2;
            p.HalfWidthAtStart = w.HalfWidthAtStartMeters * v;
            p.MinHalfWidth = w.MinHalfWidthMeters * v;
            p.MaxHalfWidth = w.MaxHalfWidthMeters * v;
            p.DepthAtStart = w.DepthAtStartMeters * v;
            p.MinDepth = w.MinDepthMeters * v;
            p.MaxDepth = w.MaxDepthMeters * v;
            p.SmoothingIterations = w.SmoothingIterations;
            p.MeanderAmplitude = w.MeanderAmplitude;
            p.MeanderWavelengthWidths = w.MeanderWavelengthWidths;
            p.MeanderMinWavelength = w.MeanderMinWavelengthMeters * v;
            p.MeanderMin = w.MeanderMinMeters * v;
            p.DepthVariation = w.DepthVariation;
            p.BendDepthShift = w.BendDepthShift;
            p.RiverStep = w.SegmentMeters * v;
            p.RiverInset = w.RiverInsetMeters * v;
            p.FlareLength = w.FlareMeters * v;
            p.MouthSpread = w.MouthSpread;
            p.FallMin = w.FallMinMeters * v;
            p.FallSteepness = w.FallSteepness;
            p.BankWidth = w.BankWidthMeters * v;
            p.BankLip = w.BankLipMeters * v;
            p.ShoreCut = w.ShoreCutMeters * v;
            p.ShoreRun = w.ShoreRun;
            p.MaxWallWidth = w.MaxWallWidthMeters * v;
            p.RiverReach = p.MaxHalfWidth + p.BankWidth + p.MaxWallWidth;
            p.LakeLevelStep = math.max(1f, math.round(w.LakeLevelStepMeters * v));
            p.RiverBed = w.RiverBed != null ? w.RiverBed.Id.Value : p.Stone;
            p.LakeBed = w.LakeBed != null ? w.LakeBed.Id.Value : p.Stone;
        }

        private static float Frequency(float scaleMeters) =>
            1f / (scaleMeters * WorldConstants.VoxelsPerMeter);
    }
}
