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

    // Floored surface heights (min, max) and water levels (min, max) over a set of
    // columns, voxels: what chunk classification needs (D-053, D-070); and the heights
    // trees and ground plants take (min inclusive, max exclusive, D-084).
    public struct ColumnRange
    {
        public int2 Height;
        public int2 Water;
        public int2 Vegetation;

        public static ColumnRange Empty =>
            new ColumnRange
            {
                Height = new int2(int.MaxValue, int.MinValue),
                Water = new int2(int.MaxValue, int.MinValue),
                Vegetation = new int2(int.MaxValue, int.MinValue),
            };

        public bool HasVegetation => Vegetation.x < Vegetation.y;

        public void AddVegetation(int2 heights)
        {
            if (heights.x < heights.y)
            {
                Vegetation = new int2(
                    math.min(Vegetation.x, heights.x),
                    math.max(Vegetation.y, heights.y)
                );
            }
        }

        // True when vegetation takes any height from yMin to yMax (inclusive).
        public bool VegetationOverlaps(int yMin, int yMax) =>
            HasVegetation && Vegetation.x <= yMax && Vegetation.y > yMin;

        public void Add(int height, int water)
        {
            Height = new int2(math.min(Height.x, height), math.max(Height.y, height));
            Water = new int2(math.min(Water.x, water), math.max(Water.y, water));
        }

        // Heights widened by `by` each way. Water levels are not: every path computes them
        // with the same code from the same samples, and the sea surface must stay exact so
        // the chunk above it remains uniform Air.
        public ColumnRange WidenedHeights(int by) =>
            new ColumnRange
            {
                Height = Height + new int2(-by, by),
                Water = Water,
                Vegetation = HasVegetation ? Vegetation + new int2(-by, by) : Vegetation,
            };
    }

    // The island generator (PLAN-04): pure functions of the parameters and the voxel or
    // chunk coordinate, Burst-callable. The terrain is a height field (D-061): a column
    // (x, z) is solid for y below its surface height.
    //
    // Per column: climate fields (inland, altitude, temperature, moisture, dump sites) give
    // biome weights (D-062); the height blends every biome's shape with those weights, plus
    // mountain ridges; the column's biome is the heaviest one with a noise-moved border.
    // Under the surface (D-063): the biome's soil, then folded strata and buried waste
    // pockets down to the subsurface depth, then Stone. Reef biomes raise coral heads from
    // the sea floor (D-072); lakes and rivers then cut or bank the land (Hydrology, D-070,
    // D-071). Empty voxels below the column's water level are water.
    public static class TerrainGenerator
    {
        // Samples of a chunk's 32 x 32 columns plus a one-column border, for slopes.
        public const int SampleGridSize = WorldConstants.ChunkSize + 2;
        public const int SampleGridCount = SampleGridSize * SampleGridSize;

        private const int CoastOctaves = 3;
        private const int ReliefOctaves = 2;
        private const int MoistureOctaves = 2;
        private const int BorderOctaves = 3;
        private const int PatchOctaves = 3;

        // Height above a column's own plants that a bent stem from a neighbor may reach.
        public const int BendMargin = 4;

        // Shape of the coral head noise (structure, not tuning): a coarse and a fine octave,
        // and the noise band over which a head rises from the floor.
        private const float ReefMinWeight = 0.01f;
        private const float ReefCoarse = 0.7f;
        private const float ReefFineRatio = 2.7f;
        private const float ReefFineShift = 17.3f;
        private const float ReefEdge = 0.2f;

        // Surface height in voxels: voxels with y below it are solid.
        public static int SurfaceHeight(in TerrainParams p, int x, int z) =>
            (int)math.floor(SmoothHeight(p, x, z));

        // The continuous height field the surface is floored from, in voxels.
        public static float SmoothHeight(in TerrainParams p, int x, int z)
        {
            var fields = ComputeFields(p, x, z);
            var weights = new FixedList128Bytes<float>();
            ComputeWeights(p, fields, ref weights);
            return Shape(p, fields, weights, out _, out _, out _, out _);
        }

        // The land after erosion and filled pits, before lakes and rivers: the raw land plus
        // the plan's interpolated change (D-079).
        public static float ShapedLandHeight(in TerrainParams p, int x, int z) =>
            LandHeight(p, x, z, out _) + p.WaterPlan.DeltaAt(new float2(x + 0.5f, z + 0.5f));

        // The moisture field (0 dry to 1 wet) of a column and its raw land height, voxels:
        // what the hydrology grid samples (D-077).
        public static float LandMoisture(in TerrainParams p, int x, int z, out float land)
        {
            var fields = ComputeFields(p, x, z);
            var weights = new FixedList128Bytes<float>();
            ComputeWeights(p, fields, ref weights);
            land = Clamp(
                Reef(p, fields.Pos, weights, HeightFrom(p, fields, weights), out _, out _)
            );
            return fields.Moisture;
        }

        // The height of the land before lakes and rivers shape it, voxels, and the inland
        // field: what Hydrology.Plan reads.
        public static float LandHeight(in TerrainParams p, int x, int z, out float inland)
        {
            var fields = ComputeFields(p, x, z);
            var weights = new FixedList128Bytes<float>();
            ComputeWeights(p, fields, ref weights);
            inland = fields.Inland;
            var land = HeightFrom(p, fields, weights);
            return Clamp(Reef(p, fields.Pos, weights, land, out _, out _));
        }

        // Height, climate and biome of one column.
        public static TerrainSample Sample(in TerrainParams p, int x, int z)
        {
            var fields = ComputeFields(p, x, z);
            var weights = new FixedList128Bytes<float>();
            ComputeWeights(p, fields, ref weights);
            var height = Shape(
                p,
                fields,
                weights,
                out var level,
                out var body,
                out var reef,
                out var reefBiome
            );
            return new TerrainSample
            {
                Height = height,
                TemperatureC =
                    fields.SeaTemperatureC
                    - p.LapseRateCPerVoxel * math.max(0f, height - p.SeaLevel),
                SeaTemperatureC = fields.SeaTemperatureC,
                Moisture = fields.Moisture,
                Inland = fields.Inland,
                AltitudeMeters = fields.AltitudeMeters,
                Waste = fields.Waste,
                Biome = PickBiome(p, fields.Pos, weights),
                WaterLevel = (int)math.floor(level),
                Body = body,
                Reef = reef,
                ReefBiome = reefBiome,
            };
        }

        // The biome of a column without its height (what tree candidates need, D-084), and
        // the normalized weights of every biome there.
        public static BiomeId BiomeAt(
            in TerrainParams p,
            int x,
            int z,
            ref FixedList128Bytes<float> weights
        )
        {
            var fields = ComputeFields(p, x, z);
            ComputeWeights(p, fields, ref weights);
            return PickBiome(p, fields.Pos, weights);
        }

        // Normalized weight of every biome at a column (D-062), in id order.
        public static void BiomeWeights(
            in TerrainParams p,
            int x,
            int z,
            ref FixedList128Bytes<float> weights
        )
        {
            var fields = ComputeFields(p, x, z);
            ComputeWeights(p, fields, ref weights);
        }

        public static int SoilDepth(in TerrainParams p, int x, int z) => Column(p, x, z).Soil;

        // Steepest central difference of the continuous heights around a column.
        public static float Slope(float east, float west, float north, float south) =>
            math.max(math.abs(east - west), math.abs(north - south)) * 0.5f;

        // The column's ground, with the slope from its four neighbors.
        public static TerrainColumn Column(in TerrainParams p, int x, int z)
        {
            return Column(p, x, z, Sample(p, x, z), FineSlope(p, x, z));
        }

        // The column's ground (D-063): soil thinned by slope, surface, shore or snow on top,
        // the strata fold and the buried pockets crossing the subsurface band.
        public static TerrainColumn Column(
            in TerrainParams p,
            int x,
            int z,
            in TerrainSample sample,
            float slope
        )
        {
            var biome = p.Biomes[sample.Biome.Value];
            var pos = new float2(x + 0.5f, z + 0.5f);
            var h = (int)math.floor(sample.Height);
            var s = 0.5f + 0.5f * noise.snoise(pos * p.SoilFrequency + p.SoilOffset);
            var soil = (int)
                math.round(
                    math.lerp(biome.SoilMin, biome.SoilMax, s)
                        * math.saturate(1f - slope / p.SoilZeroSlope)
                );
            ushort top;
            ushort soilId;
            if (h < sample.WaterLevel && sample.Body != WaterBody.Sea)
            {
                top = sample.Body == WaterBody.River ? p.RiverBed : p.LakeBed;
                soilId = top;
                soil = math.max(soil, 1);
            }
            else if (h <= p.SeaLevel + p.ShoreHeight)
            {
                top = biome.Shore;
                soilId = biome.Shore;
                var reef = p.Biomes[sample.ReefBiome.Value];
                if (
                    h < p.SeaLevel
                    && reef.ReefCorals.Length > 0
                    && sample.Reef >= reef.ReefCoralMin
                )
                {
                    var n = reef.ReefCorals.Length;
                    top = reef.ReefCorals[math.clamp((int)(Patch(p, pos) * n), 0, n - 1)];
                    soilId = reef.ReefRock;
                    soil = math.max(soil, 1);
                }
            }
            else
            {
                top =
                    biome.AlternateShare > 0f && Patch(p, pos) < biome.AlternateShare
                        ? biome.AlternateSurface
                        : biome.Surface;
                soilId = biome.Soil;
                if (sample.TemperatureC < p.SnowTemperatureC && slope < p.SnowMaxSlope)
                {
                    top = p.Snow;
                    soil = math.max(soil, 1);
                }
            }

            var column = new TerrainColumn
            {
                Surface = h,
                Soil = soil,
                Top = soil > 0 ? top : VoxelId.Air.Value,
                SoilId = soilId,
                Fold =
                    p.Fold * noise.snoise(pos * p.FoldFrequency + p.FoldOffset)
                    + p.Wobble * noise.snoise(pos * p.WobbleFrequency + p.WobbleOffset),
                StrataThickness =
                    biome.StrataThickness
                    * (
                        1f
                        + p.StrataThinning * noise.snoise(pos * p.FoldFrequency + p.ThinningOffset)
                    ),
                Strata = biome.Strata,
                Biome = sample.Biome,
            };
            if (biome.PocketChance > 0f)
            {
                AddPockets(p, x, z, h, biome.PocketChance, ref column);
            }

            return column;
        }

        // The voxel at height y of a column, for y below its surface.
        public static ushort MaterialAt(in TerrainParams p, in TerrainColumn column, int y)
        {
            var depth = column.Surface - 1 - y;
            if (depth < column.Soil)
            {
                return depth == 0 ? column.Top : column.SoilId;
            }

            if (depth >= p.SubsurfaceDepth)
            {
                return p.Stone;
            }

            for (var i = 0; i < column.Pockets.Length; i++)
            {
                var pocket = column.Pockets[i];
                if (y >= pocket.Min && y < pocket.Max)
                {
                    return pocket.Id;
                }
            }

            // Strata of varied thickness: stratum k spans [Boundary(k), Boundary(k + 1)).
            var u = y - p.SeaLevel + column.Fold;
            var t = column.StrataThickness;
            var k = (int)math.floor(u / t);
            if (u < StrataBoundary(p, k, t))
            {
                k--;
            }
            else if (u >= StrataBoundary(p, k + 1, t))
            {
                k++;
            }

            return StratumMaterial(p, column.Strata, k);
        }

        // Bottom of stratum k in fold space: k thicknesses, moved by up to half the jitter.
        private static float StrataBoundary(in TerrainParams p, int k, float thickness)
        {
            var r =
                (math.hash(new int2(k, (int)(p.HashSeed ^ 0x9E3779B9u))) & 0xFFFF) * (1f / 65536f);
            return thickness * (k + p.StrataJitter * (r - 0.5f));
        }

        // The stratum's type from the palette by hash; a type equal to the stratum below
        // moves on through the palette, so layers rarely merge into one thick band.
        private static ushort StratumMaterial(
            in TerrainParams p,
            in FixedList32Bytes<ushort> strata,
            int k
        )
        {
            var n = strata.Length;
            var pick = (int)(math.hash(new int2(k, (int)p.HashSeed)) % (uint)n);
            var id = strata[pick];
            if (n > 1)
            {
                var below = strata[(int)(math.hash(new int2(k - 1, (int)p.HashSeed)) % (uint)n)];
                for (var i = 1; i < n && id == below; i++)
                {
                    id = strata[(pick + i) % n];
                }
            }

            return id;
        }

        // What a chunk at height cy is, given the ranges of floored surface heights and
        // water levels over its column. Uniform kinds need no per-voxel work; below the
        // subsurface band every column is Stone (D-053, D-063, D-070).
        public static ChunkKind Classify(
            in TerrainParams p,
            in ColumnRange range,
            int cy,
            out VoxelId id
        )
        {
            var yMin = cy * WorldConstants.ChunkSize;
            var yMax = yMin + WorldConstants.ChunkMask;
            var vegetation = range.VegetationOverlaps(yMin, yMax);
            if (yMin >= math.max(range.Height.y, range.Water.y) && !vegetation)
            {
                id = VoxelId.Air;
                return ChunkKind.UniformAir;
            }

            if (yMax < range.Height.x - p.SubsurfaceDepth)
            {
                id = new VoxelId(p.Stone);
                return ChunkKind.UniformStone;
            }

            if (yMin >= range.Height.y && yMax < range.Water.x && !vegetation)
            {
                id = new VoxelId(p.Water);
                return ChunkKind.UniformWater;
            }

            id = VoxelId.Air;
            return ChunkKind.Mixed;
        }

        // Samples of the columns of the chunk column whose min corner is originXZ, with a
        // border of one: index (x + 1) + SampleGridSize * (z + 1) for x, z from -1 to 32.
        public static void ComputeSamples(
            in TerrainParams p,
            int2 originXZ,
            NativeArray<TerrainSample> samples
        )
        {
            for (var z = -1; z <= WorldConstants.ChunkSize; z++)
            for (var x = -1; x <= WorldConstants.ChunkSize; x++)
            {
                samples[(x + 1) + SampleGridSize * (z + 1)] = Sample(
                    p,
                    originXZ.x + x,
                    originXZ.y + z
                );
            }
        }

        // Floored surfaces and water levels over the chunk column's own 32 x 32 columns,
        // with the height its ground plants may take (D-087).
        public static ColumnRange Range(in TerrainParams p, NativeArray<TerrainSample> samples)
        {
            var range = ColumnRange.Empty;
            for (var z = 0; z < WorldConstants.ChunkSize; z++)
            for (var x = 0; x < WorldConstants.ChunkSize; x++)
            {
                var sample = samples[(x + 1) + SampleGridSize * (z + 1)];
                var h = (int)math.floor(sample.Height);
                range.Add(h, sample.WaterLevel);
                range.AddVegetation(PlantHeights(p, sample, h));
            }

            return range;
        }

        // The heights a column's ground plants may take, from its biome's tallest plant.
        public static int2 PlantHeights(in TerrainParams p, in TerrainSample sample, int h)
        {
            var biomes = p.Vegetation.Biomes;
            var biome = sample.Biome.Value;
            if (biome >= biomes.Length || biomes[biome].PlantCount == 0)
            {
                return new int2(int.MaxValue, int.MinValue);
            }

            // A margin for stems bending in from a neighbor standing a few voxels higher.
            return new int2(h, h + biomes[biome].MaxPlantHeight + BendMargin);
        }

        // A chunk generated outside the streaming jobs: its column's tree parts are always
        // collected in Burst (VegetationCollectJob), as the streaming does (R-21).
        public static void FillChunk(
            in TerrainParams p,
            ChunkCoord coord,
            NativeArray<ushort> dense
        )
        {
            var samples = new NativeArray<TerrainSample>(SampleGridCount, Allocator.Temp);
            ComputeSamples(p, Coords.ChunkOrigin(coord).xz, samples);
            var parts = VegetationCollectJob.Collect(p, Coords.ColumnOf(coord), out _);
            FillFromSamples(p, coord, samples, parts.AsArray(), dense);
            parts.Dispose();
            samples.Dispose();
        }

        // Terrain, water, ground plants and the column's tree parts.
        public static void FillFromSamples(
            in TerrainParams p,
            ChunkCoord coord,
            NativeArray<TerrainSample> samples,
            NativeArray<TreePart> parts,
            NativeArray<ushort> dense
        )
        {
            var origin = Coords.ChunkOrigin(coord);
            for (var z = 0; z < WorldConstants.ChunkSize; z++)
            for (var x = 0; x < WorldConstants.ChunkSize; x++)
            {
                var i = (x + 1) + SampleGridSize * (z + 1);
                var sample = samples[i];
                var h = (int)math.floor(sample.Height);
                var column = default(TerrainColumn);
                if (h > origin.y)
                {
                    var slope = Slope(
                        samples[i + 1].Height,
                        samples[i - 1].Height,
                        samples[i + SampleGridSize].Height,
                        samples[i - SampleGridSize].Height
                    );
                    column = Column(p, origin.x + x, origin.z + z, sample, slope);
                }

                for (var y = 0; y < WorldConstants.ChunkSize; y++)
                {
                    var wy = origin.y + y;
                    var id =
                        wy < h ? MaterialAt(p, column, wy)
                        : wy < sample.WaterLevel ? p.Water
                        : VoxelId.Air.Value;
                    dense[Coords.LocalIndex(x, y, z)] = id;
                }
            }

            FillPlants(p, origin, samples, dense);
            Vegetation.Rasterize(p, parts, origin, samples, false, dense);
        }

        // Ground plants (D-087, D-089) of the chunk's columns and of the one-column border
        // around them, whose stems may bend into the chunk. Every chunk decides a column's
        // plant the same way, so a bent blade crosses chunk borders whole.
        private static void FillPlants(
            in TerrainParams p,
            int3 origin,
            NativeArray<TerrainSample> samples,
            NativeArray<ushort> dense
        )
        {
            const int size = WorldConstants.ChunkSize;
            if (p.Vegetation.MaxPlantHeight == 0)
            {
                return;
            }

            var plants = new NativeArray<int4>(SampleGridCount, Allocator.Temp);
            var ids = new NativeArray<int>(SampleGridCount, Allocator.Temp);
            var any = false;
            for (var z = -1; z <= size; z++)
            for (var x = -1; x <= size; x++)
            {
                var i = (x + 1) + SampleGridSize * (z + 1);
                var sample = samples[i];
                var h = (int)math.floor(sample.Height);
                var range = PlantHeights(p, sample, h);
                if (range.x >= range.y || range.y <= origin.y || h >= origin.y + size + 1)
                {
                    continue;
                }

                var inside = x >= 0 && x < size && z >= 0 && z < size;
                var slope = inside
                    ? Slope(
                        samples[i + 1].Height,
                        samples[i - 1].Height,
                        samples[i + SampleGridSize].Height,
                        samples[i - SampleGridSize].Height
                    )
                    : float.NaN;
                var height = Vegetation.PlantAt(
                    p,
                    sample,
                    origin.x + x,
                    origin.z + z,
                    slope,
                    out var stem,
                    out var head,
                    out var bend,
                    out var bendAt
                );
                if (height > 0)
                {
                    plants[i] = new int4(h, height, bend, bendAt);
                    ids[i] = stem | (head << 16);
                    any = true;
                }
            }

            if (any)
            {
                for (var z = 0; z < size; z++)
                for (var x = 0; x < size; x++)
                {
                    var i = (x + 1) + SampleGridSize * (z + 1);
                    var ground = (int)math.floor(samples[i].Height);
                    var own = plants[i];
                    if (own.y > 0)
                    {
                        var top = own.z != Vegetation.BendNone ? own.x + own.w : own.x + own.y;
                        WriteStem(origin, x, z, own.x, top, own.x + own.y, ids[i], ground, dense);
                    }

                    // Stems bending in from the four face neighbors.
                    for (var d = 1; d <= 4; d++)
                    {
                        var from = new int2(x, z) - Vegetation.BendOffset(d);
                        var j = (from.x + 1) + SampleGridSize * (from.y + 1);
                        var other = plants[j];
                        if (other.y > 0 && other.z == d)
                        {
                            WriteStem(
                                origin,
                                x,
                                z,
                                other.x + other.w - 1,
                                other.x + other.y,
                                other.x + other.y,
                                ids[j],
                                ground,
                                dense
                            );
                        }
                    }
                }
            }

            ids.Dispose();
            plants.Dispose();
        }

        // Stem voxels from y0 to y1 (exclusive) of a column, the head at the plant's top,
        // only at or above the column's ground and inside the chunk.
        private static void WriteStem(
            int3 origin,
            int x,
            int z,
            int y0,
            int y1,
            int plantTop,
            int ids,
            int ground,
            NativeArray<ushort> dense
        )
        {
            var stem = (ushort)(ids & 0xFFFF);
            var head = (ushort)(ids >> 16);
            var lo = math.max(math.max(y0, ground), origin.y);
            var hi = math.min(y1, origin.y + WorldConstants.ChunkSize);
            for (var wy = lo; wy < hi; wy++)
            {
                dense[Coords.LocalIndex(x, wy - origin.y, z)] =
                    wy == plantTop - 1 && head != 0 ? head : stem;
            }
        }

        // Coarse levels (D-055 to D-057, PLAN-03 Task 2). A level-L chunk is 32^3 coarse
        // voxels of s = 2^L voxels each; its coordinate is in level-L chunk units. A coarse
        // column samples the terrain at its center voxel column (origin + s / 2); a coarse
        // voxel at height y0 is solid when y0 + s / 2 is below the surface there, and takes
        // the material of the highest solid voxel it holds, the surface voxel for the top
        // solid coarse voxel of its column (D-056). Non-solid coarse voxels are water below
        // the water level there. Level 0 gives exactly the full-resolution voxels.

        // Samples of the padded coarse columns plus one more ring, for slopes.
        public const int DetailSampleGridSize = WorldConstants.PaddedSize + 2;
        public const int DetailSampleGridCount = DetailSampleGridSize * DetailSampleGridSize;

        // Floored surfaces over the cell's 32 x 32 coarse column centers, widened by one
        // coarse voxel each way, and their water levels (voxel units).
        public static ColumnRange DetailRange(in TerrainParams p, int2 cell, int level)
        {
            var s = 1 << level;
            var origin = cell * (WorldConstants.ChunkSize * s) + (s >> 1);
            var range = ColumnRange.Empty;
            for (var z = 0; z < WorldConstants.ChunkSize; z++)
            for (var x = 0; x < WorldConstants.ChunkSize; x++)
            {
                var sample = Sample(p, origin.x + x * s, origin.y + z * s);
                range.Add((int)math.floor(sample.Height), sample.WaterLevel);
            }

            range.AddVegetation(Vegetation.DetailHeights(p, cell, level));
            return range.WidenedHeights(s);
        }

        // False when the chunk can show nothing: above both the highest surface and the
        // highest water, water with water above it everywhere, or more than wallDepth
        // coarse voxels (plus the subsurface band) below the lowest surface. Chunks just
        // below the surface are meshed for their side walls.
        public static bool DetailChunkNeedsMesh(
            in TerrainParams p,
            in ColumnRange range,
            int cy,
            int level,
            int wallDepth
        )
        {
            var s = 1 << level;
            var yMin = cy * WorldConstants.ChunkSize * s;
            var yMax = yMin + WorldConstants.ChunkSize * s - 1;
            if (range.VegetationOverlaps(yMin, yMax))
            {
                return true;
            }

            if (yMin >= math.max(range.Height.y, range.Water.y))
            {
                return false;
            }

            if (yMin >= range.Height.y && yMax < range.Water.x)
            {
                // Water: only a chunk under the lowest water surface shows its top.
                return yMax + 1 + (s >> 1) >= range.Water.x;
            }

            return yMax >= range.Height.x - p.SubsurfaceDepth - wallDepth * s;
        }

        // Fills the 34^3 meshing input of a level-L chunk straight from the generator. With
        // sideWalls, solid cells of the x and z padding become Air, so the chunk draws walls
        // on its four sides that close any gap toward another level (D-057). samples is a
        // DetailSampleGridCount scratch.
        public static void FillDetailPadded(
            in TerrainParams p,
            int level,
            ChunkCoord coord,
            bool sideWalls,
            NativeArray<TerrainSample> samples,
            NativeArray<ushort> padded
        )
        {
            const int size = WorldConstants.ChunkSize;
            const int grid = DetailSampleGridSize;
            var s = 1 << level;
            var half = s >> 1;
            var origin = coord.Value * (size * s);
            for (var z = -2; z <= size + 1; z++)
            for (var x = -2; x <= size + 1; x++)
            {
                samples[(x + 2) + grid * (z + 2)] = Sample(
                    p,
                    origin.x + x * s + half,
                    origin.z + z * s + half
                );
            }

            for (var z = -1; z <= size; z++)
            for (var x = -1; x <= size; x++)
            {
                var i = (x + 2) + grid * (z + 2);
                var sample = samples[i];
                var h = (int)math.floor(sample.Height);
                var wall = sideWalls && (x < 0 || x >= size || z < 0 || z >= size);
                var column = default(TerrainColumn);
                var topMaterial = VoxelId.Air.Value;
                if (!wall && h > origin.y - s)
                {
                    var wx = origin.x + x * s + half;
                    var wz = origin.z + z * s + half;
                    var slope =
                        Slope(
                            samples[i + 1].Height,
                            samples[i - 1].Height,
                            samples[i + grid].Height,
                            samples[i - grid].Height
                        ) / s;
                    column = Column(p, wx, wz, sample, slope);

                    // The top solid coarse voxel is the surface voxel itself (D-056), whose
                    // soil and snow follow the full-resolution slope, not the coarse one.
                    var topY = FloorDiv(h - half - origin.y - 1, s);
                    var interior = x >= 0 && x < size && z >= 0 && z < size;
                    topMaterial =
                        level > 0 && interior && topY >= 0 && topY < size
                            ? MaterialAt(p, Column(p, wx, wz, sample, FineSlope(p, wx, wz)), h - 1)
                            : MaterialAt(p, column, h - 1);
                }

                for (var y = -1; y <= size; y++)
                {
                    var y0 = origin.y + y * s;
                    ushort id;
                    if (y0 + half < h)
                    {
                        var top = y0 + s + half >= h;
                        id =
                            wall ? VoxelId.Air.Value
                            : top ? topMaterial
                            : MaterialAt(p, column, y0 + s - 1);
                    }
                    else
                    {
                        id = y0 + half < sample.WaterLevel ? p.Water : VoxelId.Air.Value;
                    }

                    padded[Coords.PaddedIndex(x, y, z)] = id;
                }
            }

            Vegetation.FillDetail(p, level, origin, sideWalls, samples, padded);
        }

        public static float FineSlope(in TerrainParams p, int x, int z) =>
            Slope(
                SmoothHeight(p, x + 1, z),
                SmoothHeight(p, x - 1, z),
                SmoothHeight(p, x, z + 1),
                SmoothHeight(p, x, z - 1)
            );

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

        // The climate fields of a column, before biome shaping.
        private struct Fields
        {
            public float2 Pos;
            public float Inland;
            public float Base;
            public float Mountain;
            public float AltitudeMeters;
            public float SeaTemperatureC;
            public float ClimateTemperatureC;
            public float Moisture;
            public float Waste;
        }

        private static Fields ComputeFields(in TerrainParams p, int x, int z)
        {
            var pos = new float2(x + 0.5f, z + 0.5f);
            var warped =
                pos
                + p.Warp
                    * new float2(
                        noise.snoise(pos * p.WarpFrequency + p.WarpOffsetX),
                        noise.snoise(pos * p.WarpFrequency + p.WarpOffsetZ)
                    );

            // Inland: 1 at the center, 0 at the coast, negative at sea; never land beyond the
            // edge radius.
            var inland =
                (p.IslandRadius - math.length(warped)) / p.IslandRadius
                + p.CoastNoise * Fbm(p, warped, p.CoastFrequency, CoastOctaves, p.CoastOffset);
            inland = math.min(inland, (p.EdgeRadius - math.length(pos)) / p.IslandRadius);

            // Land rises inland; offshore a shelf falls to the sea floor.
            var baseHeight =
                inland >= 0f
                    ? p.LandRise * inland
                    : -p.SeaDepth * math.smoothstep(0f, p.ShelfWidth, -inland);

            // Mountain ranges where the relief field is high, never at the coast.
            var reliefNoise =
                0.5f + 0.5f * Fbm(p, warped, p.ReliefFrequency, ReliefOctaves, p.ReliefOffset);
            var relief =
                math.smoothstep(p.ReliefThreshold, p.ReliefThreshold + p.ReliefRamp, reliefNoise)
                * math.smoothstep(0f, p.ShelfWidth * 2f, inland);
            var mountain =
                relief > 0f
                    ? relief
                        * p.MountainHeight
                        * math.lerp(Ridged(p, warped), 1f, p.MassifShare * relief)
                    : 0f;

            // Temperature at sea level: noise plus a north (cold) to south (warm) gradient.
            var t =
                (1f - p.TemperatureGradient)
                    * noise.snoise(pos * p.TemperatureFrequency + p.TemperatureOffset)
                + p.TemperatureGradient * math.clamp(-pos.y / p.EdgeRadius, -1f, 1f);
            var seaTemperature = math.lerp(
                p.TemperatureMinC,
                p.TemperatureMaxC,
                math.saturate(0.5f + 0.5f * t)
            );
            var altitude = baseHeight + mountain;
            var moisture = math.saturate(
                0.5f
                    + 0.5f * Fbm(p, warped, p.MoistureFrequency, MoistureOctaves, p.MoistureOffset)
                    + p.CoastMoisture * (1f - math.saturate(math.abs(inland) / (p.ShelfWidth * 2f)))
            );
            var wasteNoise = 0.5f + 0.5f * noise.snoise(pos * p.WasteFrequency + p.WasteOffset);
            return new Fields
            {
                Pos = pos,
                Inland = inland,
                Base = baseHeight,
                Mountain = mountain,
                AltitudeMeters = altitude * WorldConstants.VoxelSize,
                SeaTemperatureC = seaTemperature,
                ClimateTemperatureC =
                    seaTemperature - p.LapseRateCPerVoxel * math.max(0f, altitude),
                Moisture = moisture,
                Waste = math.saturate((wasteNoise - p.WasteThreshold) / (1f - p.WasteThreshold)),
            };
        }

        // exp(-(distance - smallest)), normalized; distance = bias + sum over the fields of
        // (distance outside the biome's range / blend width)^2 (D-062).
        private static void ComputeWeights(
            in TerrainParams p,
            in Fields f,
            ref FixedList128Bytes<float> weights
        )
        {
            weights.Clear();
            var smallest = float.MaxValue;
            for (var i = 0; i < p.Biomes.Length; i++)
            {
                var b = p.Biomes[i];
                var d =
                    b.Bias
                    + Square(Outside(f.ClimateTemperatureC, b.TemperatureC) / p.BlendTemperatureC)
                    + Square(Outside(f.Moisture, b.Moisture) / p.BlendMoisture)
                    + Square(Outside(f.Inland, b.Inland) / p.BlendInland)
                    + Square(Outside(f.AltitudeMeters, b.AltitudeMeters) / p.BlendAltitudeMeters)
                    + Square(Outside(f.Waste, b.Waste) / p.BlendWaste);
                weights.Add(d);
                smallest = math.min(smallest, d);
            }

            var sum = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                var w = math.exp(smallest - weights[i]);
                weights[i] = w;
                sum += w;
            }

            for (var i = 0; i < weights.Length; i++)
            {
                weights[i] /= sum;
            }
        }

        private static float HeightFrom(
            in TerrainParams p,
            in Fields f,
            in FixedList128Bytes<float> weights
        )
        {
            var hill = Fbm(p, f.Pos, p.HillFrequency, p.HillOctaves, p.HillOffset);
            var shape = 0f;
            var terrace = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                var b = p.Biomes[i];
                shape += w * (b.BaseInfluence * f.Base + b.Offset + b.HillAmplitude * hill);
                terrace += w * b.Terrace;
            }

            var height = shape + f.Mountain;
            if (terrace > 0f)
            {
                var q = height / p.TerraceStep;
                var step = math.floor(q);
                var riser = math.smoothstep(
                    0.5f - 0.5f * p.TerraceRiser,
                    0.5f + 0.5f * p.TerraceRiser,
                    q - step
                );
                height = math.lerp(height, (step + riser) * p.TerraceStep, terrace);
            }

            return p.SeaLevel + height;
        }

        // Land, reef heads, then lakes and rivers: the final continuous height.
        private static float Shape(
            in TerrainParams p,
            in Fields f,
            in FixedList128Bytes<float> weights,
            out float waterLevel,
            out WaterBody body,
            out float reef,
            out BiomeId reefBiome
        )
        {
            var land = Clamp(
                Reef(p, f.Pos, weights, HeightFrom(p, f, weights), out reef, out reefBiome)
                    + p.WaterPlan.DeltaAt(f.Pos)
            );
            // River valley walls as steep as the biomes say (D-076), blended by weight.
            var wallRun = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                wallRun += weights[i] * p.Biomes[i].RiverWallRun;
            }

            Hydrology.Apply(p, f.Pos, land, wallRun, out var height, out waterLevel, out body);
            return Clamp(height);
        }

        private static float Clamp(float height) =>
            math.clamp(
                height,
                WorldConstants.ChunkSize,
                WorldConstants.WorldHeightVoxels - WorldConstants.ChunkSize
            );

        // Coral heads (D-072): where a reef biome weighs, patches of the floor rise by up to
        // the biome's reef height, never closer to the sea surface than its clearance.
        private static float Reef(
            in TerrainParams p,
            float2 pos,
            in FixedList128Bytes<float> weights,
            float height,
            out float cover,
            out BiomeId reefBiome
        )
        {
            cover = 0f;
            reefBiome = default;
            var add = 0f;
            var cap = float.MaxValue;
            for (var i = 0; i < weights.Length; i++)
            {
                var b = p.Biomes[i];
                var w = weights[i];
                if (b.ReefHeight <= 0f || w < ReefMinWeight)
                {
                    continue;
                }

                var q = pos * b.ReefFrequency + p.ReefOffset;
                var n =
                    0.5f
                    + 0.5f
                        * (
                            ReefCoarse * noise.snoise(q)
                            + (1f - ReefCoarse) * noise.snoise(q * ReefFineRatio + ReefFineShift)
                        );
                var head = math.smoothstep(1f - b.ReefCover, 1f - b.ReefCover + ReefEdge, n);
                add += w * b.ReefHeight * head;
                if (w * head > cover)
                {
                    cover = w * head;
                    reefBiome = new BiomeId((byte)i);
                }

                cap = math.min(cap, p.SeaLevel - b.ReefClearance);
            }

            return add > 0f ? math.max(height, math.min(height + add, cap)) : height;
        }

        // The heaviest biome; where the second comes close, a smooth noise moves the border
        // so it is ragged rather than a contour line (D-062).
        private static BiomeId PickBiome(
            in TerrainParams p,
            float2 pos,
            in FixedList128Bytes<float> weights
        )
        {
            var first = 0;
            var second = -1;
            for (var i = 1; i < weights.Length; i++)
            {
                if (weights[i] > weights[first])
                {
                    second = first;
                    first = i;
                }
                else if (second < 0 || weights[i] > weights[second])
                {
                    second = i;
                }
            }

            if (second >= 0)
            {
                var share = weights[second] / (weights[first] + weights[second]);
                if (share > 0.5f - p.BorderJitter)
                {
                    var n = Fbm(p, pos, p.BorderFrequency, BorderOctaves, p.BorderOffset);
                    if (share + p.BorderJitter * n > 0.5f)
                    {
                        first = second;
                    }
                }
            }

            return new BiomeId((byte)first);
        }

        // Alternate surface patches: irregular shapes from three octaves, and a fine noise
        // that frays their edges over about a meter instead of a clean contour.
        private static float Patch(in TerrainParams p, float2 pos) =>
            0.5f
            + 0.5f * Fbm(p, pos, p.PatchFrequency, PatchOctaves, p.PatchOffset)
            + p.PatchEdge * noise.snoise(pos * p.PatchEdgeFrequency + p.PatchEdgeOffset);

        // Buried waste (D-063): an ellipsoid in some cells of a 3D grid, kept inside the
        // subsurface band and at least two voxels below the surface.
        private static void AddPockets(
            in TerrainParams p,
            int x,
            int z,
            int surface,
            float chance,
            ref TerrainColumn column
        )
        {
            var yMin = surface - p.SubsurfaceDepth;
            var yMax = surface - 3;
            if (yMax < yMin || p.PocketTypes.Length == 0)
            {
                return;
            }

            var cell = new int2(FloorDiv(x, p.PocketCell), FloorDiv(z, p.PocketCell));
            var cellOrigin = (float2)(cell * p.PocketCell);
            for (
                var cy = FloorDiv(yMin, p.PocketCellHeight);
                cy <= FloorDiv(yMax, p.PocketCellHeight);
                cy++
            )
            {
                var hash = math.hash(new int4(cell.x, cy, cell.y, (int)p.HashSeed));
                if ((hash & 0xFFFF) * (1f / 65536f) >= chance)
                {
                    continue;
                }

                var random = Random.CreateFromIndex(hash);
                var r = new float3(
                    random.NextFloat(p.PocketRadius.x, p.PocketRadius.y),
                    random.NextFloat(p.PocketRadius.x, p.PocketRadius.y),
                    random.NextFloat(p.PocketRadius.x, p.PocketRadius.y)
                );
                var center = new float3(
                    cellOrigin.x + random.NextFloat(r.x, p.PocketCell - r.x),
                    cy * p.PocketCellHeight + random.NextFloat(r.y, p.PocketCellHeight - r.y),
                    cellOrigin.y + random.NextFloat(r.z, p.PocketCell - r.z)
                );
                var id = p.PocketTypes[(int)(random.NextUInt() % (uint)p.PocketTypes.Length)];
                var dx = (x + 0.5f - center.x) / r.x;
                var dz = (z + 0.5f - center.z) / r.z;
                var q = 1f - dx * dx - dz * dz;
                if (q <= 0f)
                {
                    continue;
                }

                var halfHeight = r.y * math.sqrt(q);
                var lo = math.max(yMin, (int)math.ceil(center.y - halfHeight - 0.5f));
                var hi = math.min(yMax + 1, (int)math.floor(center.y + halfHeight - 0.5f) + 1);
                if (lo < hi && column.Pockets.Length < TerrainColumn.MaxPockets)
                {
                    column.Pockets.Add(
                        new PocketSpan
                        {
                            Min = lo,
                            Max = hi,
                            Id = id,
                        }
                    );
                }
            }
        }

        // fBm of simplex noise, -1 to 1.
        private static float Fbm(
            in TerrainParams p,
            float2 pos,
            float frequency,
            int octaves,
            float2 offset
        )
        {
            var amplitude = 1f;
            var sum = 0f;
            var norm = 0f;
            for (var i = 0; i < octaves; i++)
            {
                var octaveOffset = offset + new float2(i * 31.7f, i * 47.3f);
                sum += amplitude * noise.snoise(pos * frequency + octaveOffset);
                norm += amplitude;
                amplitude *= p.Gain;
                frequency *= p.Lacunarity;
            }

            return sum / norm;
        }

        // Ridged multifractal, 0 to 1: sharp crests where the noise crosses zero, finer
        // octaves weighted by the coarser ones so valleys stay smooth.
        private static float Ridged(in TerrainParams p, float2 pos)
        {
            var frequency = p.RidgeFrequency;
            var amplitude = 1f;
            var weight = 1f;
            var sum = 0f;
            var norm = 0f;
            for (var i = 0; i < p.RidgeOctaves; i++)
            {
                var octaveOffset = p.RidgeOffset + new float2(i * 31.7f, i * 47.3f);
                var n = 1f - math.abs(noise.snoise(pos * frequency + octaveOffset));
                n *= n * weight;
                weight = math.saturate(n * 2f);
                sum += amplitude * n;
                norm += amplitude;
                amplitude *= p.Gain;
                frequency *= p.Lacunarity;
            }

            return sum / norm;
        }

        private static float Outside(float value, float2 range) =>
            math.max(0f, math.max(range.x - value, value - range.y));

        private static float Square(float value) => value * value;

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((b - 1 - a) / b);
    }
}
