using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    public enum ChunkKind : byte
    {
        Mixed = 0,
        UniformAir = 1,
        UniformStone = 2,
        UniformWater = 3,
    }

    // The placeholder terrain (PLAN-02 Task 5): pure functions of the parameters and the
    // voxel or chunk coordinate, Burst-callable. A column (x, z) is solid for y below its
    // surface height; the top layers are soil (grass over dirt, or sand near the sea), the
    // rest stone; empty voxels below sea level are water.
    public static class TerrainGenerator
    {
        // Heights of a chunk's 32 x 32 columns plus a one-column border, for slopes.
        public const int HeightGridSize = WorldConstants.ChunkSize + 2;
        public const int HeightGridCount = HeightGridSize * HeightGridSize;

        // Surface height in voxels: voxels with y below it are solid.
        public static int SurfaceHeight(in TerrainParams p, int x, int z) =>
            (int)math.floor(SmoothHeight(p, x, z));

        // The continuous height field the surface is floored from, in voxels.
        public static float SmoothHeight(in TerrainParams p, int x, int z)
        {
            var pos = new float2(x + 0.5f, z + 0.5f);
            var coast = math.smoothstep(
                p.IslandRadius,
                p.IslandRadius + p.CoastFalloff,
                math.length(pos)
            );
            return (1f - coast) * (p.SeaLevel + p.LandHeight * Fbm(p, pos));
        }

        public static int SoilDepth(in TerrainParams p, int x, int z) =>
            SoilDepth(
                p,
                x,
                z,
                Slope(
                    SmoothHeight(p, x + 1, z),
                    SmoothHeight(p, x - 1, z),
                    SmoothHeight(p, x, z + 1),
                    SmoothHeight(p, x, z - 1)
                )
            );

        // Soil from its own noise, thinned by the slope (voxel rise per voxel). The slope
        // comes from the continuous height field, so soil does not jump between neighbors
        // whose floored heights differ by one.
        public static int SoilDepth(in TerrainParams p, int x, int z, float slope)
        {
            var pos = new float2(x + 0.5f, z + 0.5f) * p.SoilFrequency + p.SoilOffset;
            var s = 0.5f + 0.5f * noise.snoise(pos);
            var depth = math.lerp(p.SoilMin, p.SoilMax, s);
            var factor = math.saturate(1f - slope / p.SoilZeroSlope);
            return (int)math.round(depth * factor);
        }

        // Steepest central difference of the continuous heights around a column.
        public static float Slope(float east, float west, float north, float south) =>
            math.max(math.abs(east - west), math.abs(north - south)) * 0.5f;

        // What a chunk at height cy is, given the range of floored surface heights over its
        // column. Uniform kinds need no per-voxel work.
        public static ChunkKind Classify(
            in TerrainParams p,
            int2 heightRange,
            int cy,
            out VoxelId id
        )
        {
            var yMin = cy * WorldConstants.ChunkSize;
            var yMax = yMin + WorldConstants.ChunkMask;
            if (yMin >= math.max(heightRange.y, p.SeaLevel))
            {
                id = VoxelId.Air;
                return ChunkKind.UniformAir;
            }

            if (yMax < heightRange.x - p.MaxSoil)
            {
                id = new VoxelId(p.Stone);
                return ChunkKind.UniformStone;
            }

            if (yMin >= heightRange.y && yMax < p.SeaLevel)
            {
                id = new VoxelId(p.Water);
                return ChunkKind.UniformWater;
            }

            id = VoxelId.Air;
            return ChunkKind.Mixed;
        }

        // Continuous heights of the columns of the chunk column whose min corner is
        // originXZ, with a border of one: index (x + 1) + HeightGridSize * (z + 1) for x, z
        // from -1 to 32.
        public static void ComputeHeights(
            in TerrainParams p,
            int2 originXZ,
            NativeArray<float> heights
        )
        {
            for (var z = -1; z <= WorldConstants.ChunkSize; z++)
            for (var x = -1; x <= WorldConstants.ChunkSize; x++)
            {
                heights[(x + 1) + HeightGridSize * (z + 1)] = SmoothHeight(
                    p,
                    originXZ.x + x,
                    originXZ.y + z
                );
            }
        }

        // Lowest and highest floored surface over the chunk column's own 32 x 32 columns.
        public static int2 HeightRange(NativeArray<float> heights)
        {
            var range = new int2(int.MaxValue, int.MinValue);
            for (var z = 0; z < WorldConstants.ChunkSize; z++)
            for (var x = 0; x < WorldConstants.ChunkSize; x++)
            {
                var h = (int)math.floor(heights[(x + 1) + HeightGridSize * (z + 1)]);
                range = new int2(math.min(range.x, h), math.max(range.y, h));
            }

            return range;
        }

        public static void FillChunk(
            in TerrainParams p,
            ChunkCoord coord,
            NativeArray<ushort> dense
        )
        {
            var heights = new NativeArray<float>(HeightGridCount, Allocator.Temp);
            ComputeHeights(p, Coords.ChunkOrigin(coord).xz, heights);
            FillFromHeights(p, coord, heights, dense);
            heights.Dispose();
        }

        public static void FillFromHeights(
            in TerrainParams p,
            ChunkCoord coord,
            NativeArray<float> heights,
            NativeArray<ushort> dense
        )
        {
            var origin = Coords.ChunkOrigin(coord);
            for (var z = 0; z < WorldConstants.ChunkSize; z++)
            for (var x = 0; x < WorldConstants.ChunkSize; x++)
            {
                var i = (x + 1) + HeightGridSize * (z + 1);
                var h = (int)math.floor(heights[i]);
                var slope = Slope(
                    heights[i + 1],
                    heights[i - 1],
                    heights[i + HeightGridSize],
                    heights[i - HeightGridSize]
                );
                var soil = SoilDepth(p, origin.x + x, origin.z + z, slope);
                var sandy = h <= p.SeaLevel + p.SandAbove;
                for (var y = 0; y < WorldConstants.ChunkSize; y++)
                {
                    var wy = origin.y + y;
                    ushort id;
                    if (wy < h)
                    {
                        var depth = h - 1 - wy;
                        if (depth >= soil)
                        {
                            id = p.Stone;
                        }
                        else if (sandy)
                        {
                            id = p.Sand;
                        }
                        else
                        {
                            id = depth == 0 ? p.Grass : p.Dirt;
                        }
                    }
                    else
                    {
                        id = wy < p.SeaLevel ? p.Water : VoxelId.Air.Value;
                    }

                    dense[Coords.LocalIndex(x, y, z)] = id;
                }
            }
        }

        public static ChunkVoxels GenerateChunk(
            in TerrainParams p,
            ChunkCoord coord,
            Allocator allocator
        )
        {
            var dense = new NativeArray<ushort>(WorldConstants.VoxelsPerChunk, Allocator.Temp);
            FillChunk(p, coord, dense);
            var voxels = ChunkVoxels.FromDense(dense, allocator);
            dense.Dispose();
            return voxels;
        }

        // fBm of simplex noise, 0 to 1.
        private static float Fbm(in TerrainParams p, float2 pos)
        {
            var frequency = p.BaseFrequency;
            var amplitude = 1f;
            var sum = 0f;
            var norm = 0f;
            for (var i = 0; i < p.Octaves; i++)
            {
                var octaveOffset = p.HeightOffset + new float2(i * 31.7f, i * 47.3f);
                sum += amplitude * noise.snoise(pos * frequency + octaveOffset);
                norm += amplitude;
                amplitude *= p.Gain;
                frequency *= p.Lacunarity;
            }

            return math.saturate(0.5f + 0.5f * sum / norm);
        }
    }
}
