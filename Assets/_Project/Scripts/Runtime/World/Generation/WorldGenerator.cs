using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Deterministic island generation (PLAN-02 "Generation"). Every block is a pure function
    // of the seed and its position: a chunk can be generated alone, in any order, which is
    // what streaming an infinite world needs later (D-030). No UnityEngine.Random.
    public static class WorldGenerator
    {
        // The hills fade in over the first quarter of the way from the shore to the center,
        // so the shore stays at sea level. Shape constant, not tuning.
        private const float HillFadeIn = 4f;

        // Directions tried around the island when looking for the spawn point.
        private const int SpawnAttempts = 16;

        // Height of a column: the number of solid blocks from y = 0, so the top block is at
        // height - 1. Columns below sea level are under water.
        public static int ColumnHeight(in WorldGenParams p, int x, int z)
        {
            var pos = new float2(x + 0.5f, z + 0.5f);
            var distance = math.distance(pos, p.Center) / p.IslandRadius;
            distance +=
                noise.snoise((pos + p.CoastOffset) * p.CoastNoiseScale) * p.CoastNoiseAmplitude;
            var land = 1f - distance;
            var hill =
                noise.snoise((pos + p.HillOffset) * p.HillNoiseScale)
                * p.HillNoiseAmplitude
                * math.saturate(land * HillFadeIn);
            var raw = p.SeaLevel + land * p.LandHeight + hill;
            return math.clamp(
                (int)math.floor(raw),
                p.SeaLevel - p.SeaFloorDepth,
                p.MaxSurfaceHeight
            );
        }

        // Zones by distance from the center (D-012); columns under water belong to none.
        // Borders are hard (PLAN-02 open question, default hard).
        public static ZoneId ColumnZone(in WorldGenParams p, int x, int z, int height)
        {
            if (height < p.SeaLevel)
            {
                return ZoneId.None;
            }

            var r = math.distance(new float2(x + 0.5f, z + 0.5f), p.Center);
            if (r < p.DechargeRadius)
            {
                return ZoneId.Decharge;
            }

            return r < p.ForetRadius ? ZoneId.Foret : ZoneId.Plage;
        }

        public static bool IsTreeColumn(in WorldGenParams p, int x, int z, ZoneId zone)
        {
            if (zone != ZoneId.Foret)
            {
                return false;
            }

            var hash = math.hash(new uint3((uint)x, (uint)z, p.TreeSeed));
            return (hash & 0xFFFFFFu) / 16777216f < p.TreeDensity;
        }

        public static void GenerateChunk(
            in WorldGenParams p,
            ChunkCoord coord,
            NativeArray<byte> blocks
        )
        {
            CreateChunkJob(p, coord, blocks).Run();
        }

        public static JobHandle ScheduleChunk(
            in WorldGenParams p,
            ChunkCoord coord,
            NativeArray<byte> blocks,
            JobHandle dependency = default
        ) => CreateChunkJob(p, coord, blocks).Schedule(dependency);

        // Fills the zone map and height map of the demo island (256 x 256 columns).
        public static JobHandle ScheduleColumns(
            in WorldGenParams p,
            NativeArray<byte> zoneMap,
            NativeArray<int> heightMap,
            JobHandle dependency = default
        ) =>
            new ColumnJob
            {
                Params = p,
                ZoneMap = zoneMap,
                HeightMap = heightMap,
            }.Schedule(WorldConstants.ColumnCount, WorldConstants.WorldSizeX, dependency);

        // A dry Plage column at the shore, chosen from the seed: walk out from the center in
        // a seeded direction and keep the last dry Plage column before the sea. Returns the
        // position of the first Air block above the ground.
        public static int3 FindSpawnPoint(in WorldGenParams p)
        {
            var maxRadius = math.length(p.Center);
            for (var attempt = 0; attempt < SpawnAttempts; attempt++)
            {
                var angle = p.SpawnAngle + attempt * (2f * math.PI / SpawnAttempts);
                var dir = new float2(math.cos(angle), math.sin(angle));
                var found = false;
                var best = int3.zero;
                for (var r = 0f; r < maxRadius; r += 1f)
                {
                    var column = (int2)math.floor(p.Center + dir * r);
                    if (!IsColumnInWorld(column.x, column.y))
                    {
                        break;
                    }

                    var height = ColumnHeight(p, column.x, column.y);
                    var zone = ColumnZone(p, column.x, column.y, height);
                    if (zone == ZoneId.Plage && height > p.SeaLevel)
                    {
                        best = new int3(column.x, height, column.y);
                        found = true;
                    }
                    else if (found && height < p.SeaLevel)
                    {
                        break;
                    }
                }

                if (found)
                {
                    return best;
                }
            }

            var center = (int2)p.Center;
            return new int3(center.x, ColumnHeight(p, center.x, center.y), center.y);
        }

        public static bool IsColumnInWorld(int x, int z) =>
            x >= 0 && z >= 0 && x < WorldConstants.WorldSizeX && z < WorldConstants.WorldSizeZ;

        private static ChunkGenJob CreateChunkJob(
            in WorldGenParams p,
            ChunkCoord coord,
            NativeArray<byte> blocks
        ) =>
            new ChunkGenJob
            {
                Params = p,
                Origin = Coords.ChunkOrigin(coord),
                Blocks = blocks,
            };
    }
}
