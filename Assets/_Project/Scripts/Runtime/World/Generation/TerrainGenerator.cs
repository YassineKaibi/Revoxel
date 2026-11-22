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
                    var id =
                        wy < h ? MaterialAt(p, wy, h, soil, sandy)
                        : wy < p.SeaLevel ? p.Water
                        : VoxelId.Air.Value;
                    dense[Coords.LocalIndex(x, y, z)] = id;
                }
            }
        }

        // The material of a solid voxel at height y of a column with this surface, soil depth
        // and sandiness: grass on top, dirt below it, stone under the soil, sand near the sea.
        public static ushort MaterialAt(
            in TerrainParams p,
            int y,
            int surface,
            int soil,
            bool sandy
        )
        {
            var depth = surface - 1 - y;
            if (depth >= soil)
            {
                return p.Stone;
            }

            if (sandy)
            {
                return p.Sand;
            }

            return depth == 0 ? p.Grass : p.Dirt;
        }

        // Coarse levels (D-055 to D-057, PLAN-03 Task 2). A level-L chunk is 32^3 coarse
        // voxels of s = 2^L voxels each; its coordinate is in level-L chunk units. A coarse
        // column samples the terrain at its center voxel column (origin + s / 2); a coarse
        // voxel at height y0 is solid when y0 + s / 2 is below the surface there, and takes
        // the material of the highest solid voxel it holds, the surface voxel for the top
        // solid coarse voxel of its column (D-056). Non-solid coarse voxels are water below
        // sea level. Level 0 gives exactly the full-resolution voxels.

        // Heights of the padded coarse columns plus one more ring, for slopes.
        public const int DetailHeightGridSize = WorldConstants.PaddedSize + 2;
        public const int DetailHeightGridCount = DetailHeightGridSize * DetailHeightGridSize;

        // Lowest and highest floored surface over the cell's 32 x 32 coarse column centers,
        // widened by one coarse voxel each way (voxel units).
        public static int2 DetailHeightRange(in TerrainParams p, int2 cell, int level)
        {
            var s = 1 << level;
            var origin = cell * (WorldConstants.ChunkSize * s) + (s >> 1);
            var range = new int2(int.MaxValue, int.MinValue);
            for (var z = 0; z < WorldConstants.ChunkSize; z++)
            for (var x = 0; x < WorldConstants.ChunkSize; x++)
            {
                var h = SurfaceHeight(p, origin.x + x * s, origin.y + z * s);
                range = new int2(math.min(range.x, h), math.max(range.y, h));
            }

            return range + new int2(-s, s);
        }

        // False when the chunk can show nothing: above both the highest surface and sea
        // level, water with water above it, or more than wallDepth coarse voxels (plus the
        // deepest soil) below the lowest surface. Chunks just below the surface are meshed
        // for their side walls.
        public static bool DetailChunkNeedsMesh(
            in TerrainParams p,
            int2 heightRange,
            int cy,
            int level,
            int wallDepth
        )
        {
            var s = 1 << level;
            var yMin = cy * WorldConstants.ChunkSize * s;
            var yMax = yMin + WorldConstants.ChunkSize * s - 1;
            if (yMin >= math.max(heightRange.y, p.SeaLevel))
            {
                return false;
            }

            if (yMin >= heightRange.y && yMax < p.SeaLevel)
            {
                // Water: only the chunk under the sea surface shows its top.
                return yMax + 1 + (s >> 1) >= p.SeaLevel;
            }

            return yMax >= heightRange.x - p.MaxSoil - wallDepth * s;
        }

        // Fills the 34^3 meshing input of a level-L chunk straight from the generator. With
        // sideWalls, solid cells of the x and z padding become Air, so the chunk draws walls
        // on its four sides that close any gap toward another level (D-057). heights is a
        // DetailHeightGridCount scratch.
        public static void FillDetailPadded(
            in TerrainParams p,
            int level,
            ChunkCoord coord,
            bool sideWalls,
            NativeArray<float> heights,
            NativeArray<ushort> padded
        )
        {
            const int size = WorldConstants.ChunkSize;
            const int grid = DetailHeightGridSize;
            var s = 1 << level;
            var half = s >> 1;
            var origin = coord.Value * (size * s);
            for (var z = -2; z <= size + 1; z++)
            for (var x = -2; x <= size + 1; x++)
            {
                heights[(x + 2) + grid * (z + 2)] = SmoothHeight(
                    p,
                    origin.x + x * s + half,
                    origin.z + z * s + half
                );
            }

            for (var z = -1; z <= size; z++)
            for (var x = -1; x <= size; x++)
            {
                var i = (x + 2) + grid * (z + 2);
                var h = (int)math.floor(heights[i]);
                var slope =
                    Slope(heights[i + 1], heights[i - 1], heights[i + grid], heights[i - grid]) / s;
                var soil = SoilDepth(p, origin.x + x * s + half, origin.z + z * s + half, slope);
                var sandy = h <= p.SeaLevel + p.SandAbove;
                var wall = sideWalls && (x < 0 || x >= size || z < 0 || z >= size);
                for (var y = -1; y <= size; y++)
                {
                    var y0 = origin.y + y * s;
                    ushort id;
                    if (y0 + half < h)
                    {
                        var top = y0 + s + half >= h;
                        id = wall
                            ? VoxelId.Air.Value
                            : MaterialAt(p, top ? h - 1 : y0 + s - 1, h, soil, sandy);
                    }
                    else
                    {
                        id = y0 + half < p.SeaLevel ? p.Water : VoxelId.Air.Value;
                    }

                    padded[Coords.PaddedIndex(x, y, z)] = id;
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
