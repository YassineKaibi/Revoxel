using Unity.Mathematics;

namespace Revoxel.World
{
    // The generated water of one voxel column (D-074): the level water fills below, which
    // body it belongs to and the ground under it.
    public struct WaterColumn
    {
        // Water fills the non-solid voxels below this height, voxels.
        public int Level;

        // Floored ground surface, voxels.
        public int Ground;

        public WaterBody Body;

        // Ground below the level: the column holds water.
        public bool IsWet => Ground < Level;

        public float DepthMeters => math.max(0, Level - Ground) * WorldConstants.VoxelSize;

        public float SurfaceMeters => Level * WorldConstants.VoxelSize;
    }

    // What the loaded world holds at a point (D-074): for swimming and, later, for the
    // player's breath and the drift of floating matter.
    public struct WaterSample
    {
        public bool InWater;

        // Top of the liquid above the point, meters.
        public float SurfaceMeters;

        // From the point up to the surface, meters.
        public float DepthMeters;

        public WaterBody Body;

        // Share, 0 to 1, of a vertical body from bottomMeters up heightMeters tall that is
        // below the surface (only meaningful when InWater at the bottom).
        public float Submersion(float bottomMeters, float heightMeters) =>
            InWater && heightMeters > 0f
                ? math.saturate((SurfaceMeters - bottomMeters) / heightMeters)
                : 0f;
    }

    // Water queries on the generator (D-074), pure and Burst-callable like BiomeMap;
    // positions are voxel columns. Edits are not included: VoxelWorld.TrySampleWater reads
    // the loaded voxels.
    public static class WaterMap
    {
        public static WaterColumn Column(in TerrainParams p, int x, int z)
        {
            var sample = TerrainGenerator.Sample(p, x, z);
            return new WaterColumn
            {
                Level = sample.WaterLevel,
                Ground = (int)math.floor(sample.Height),
                Body = sample.Body,
            };
        }

        // Inside generated water at a voxel: below the column's level and above its ground.
        public static bool IsWater(in TerrainParams p, int3 voxel)
        {
            var column = Column(p, voxel.x, voxel.z);
            return voxel.y >= column.Ground && voxel.y < column.Level;
        }
    }
}
