using Unity.Mathematics;

namespace Revoxel.World
{
    // Unmanaged copy of the generation inputs, built once from the seed and the settings,
    // passed by value to Burst jobs.
    public struct WorldGenParams
    {
        public uint TreeSeed;
        public float2 Center;
        public float2 CoastOffset;
        public float2 HillOffset;
        public float SpawnAngle;
        public float IslandRadius;
        public float CoastNoiseScale;
        public float CoastNoiseAmplitude;
        public int SeaLevel;
        public float LandHeight;
        public float HillNoiseScale;
        public float HillNoiseAmplitude;
        public int SeaFloorDepth;
        public int StoneDepth;
        public float DechargeRadius;
        public float ForetRadius;
        public float TreeDensity;
        public int TrunkHeight;
        public int LeafRadius;
        public int MaxSurfaceHeight;
        public byte Water;
        public byte Sand;
        public byte Dirt;
        public byte Grass;
        public byte Stone;
        public byte Log;
        public byte Leaves;
        public byte CompactedWaste;

        // Noise has no seed parameter, so the seed picks 2D offsets into the noise field.
        public static WorldGenParams Create(WorldSeed seed, WorldGenSettings settings)
        {
            var random = Random.CreateFromIndex((uint)seed.Value);
            return new WorldGenParams
            {
                TreeSeed = random.NextUInt(),
                Center = new float2(WorldConstants.WorldSizeX, WorldConstants.WorldSizeZ) * 0.5f,
                CoastOffset = random.NextFloat2(-1000f, 1000f),
                HillOffset = random.NextFloat2(-1000f, 1000f),
                SpawnAngle = random.NextFloat(0f, 2f * math.PI),
                IslandRadius = settings.IslandRadius,
                CoastNoiseScale = settings.CoastNoiseScale,
                CoastNoiseAmplitude = settings.CoastNoiseAmplitude,
                SeaLevel = settings.SeaLevel,
                LandHeight = settings.LandHeight,
                HillNoiseScale = settings.HillNoiseScale,
                HillNoiseAmplitude = settings.HillNoiseAmplitude,
                SeaFloorDepth = settings.SeaFloorDepth,
                StoneDepth = settings.StoneDepth,
                DechargeRadius = settings.DechargeRadius,
                ForetRadius = settings.ForetRadius,
                TreeDensity = settings.TreeDensity,
                TrunkHeight = settings.TrunkHeight,
                LeafRadius = settings.LeafRadius,
                // Room for the tallest tree (trunk, then one layer of leaves above it).
                MaxSurfaceHeight = WorldConstants.WorldHeight - settings.TrunkHeight - 2,
                Water = settings.Water.Id.Value,
                Sand = settings.Sand.Id.Value,
                Dirt = settings.Dirt.Id.Value,
                Grass = settings.Grass.Id.Value,
                Stone = settings.Stone.Id.Value,
                Log = settings.Log.Id.Value,
                Leaves = settings.Leaves.Id.Value,
                CompactedWaste = settings.CompactedWaste.Id.Value,
            };
        }
    }
}
