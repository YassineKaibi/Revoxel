using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    // Water in the generated voxels (PLAN-05 Task 2, D-070 to D-072).
    public sealed class WaterGenerationTests
    {
        private const int Size = WorldConstants.ChunkSize;
        private const int RiverSquareHalf = 24;
        private const int LakeStride = 16;
        private const int MatchingLevels = 3;

        // Done line: water is held. Around every second river node and over every lake, no
        // water stands above dry ground beside it; lower water beside it (a rapid, a fall)
        // is allowed.
        [Test]
        public void Water_IsHeldByGround()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            using var squares = new NativeList<int3>(512, Allocator.TempJob);
            for (var n = 0; n < p.WaterPlan.Nodes.Length; n += 2)
            {
                squares.Add(new int3((int2)math.floor(p.WaterPlan.Nodes[n].Pos), RiverSquareHalf));
            }

            var rivers = Survey(p, squares, 1);
            squares.Clear();
            for (var i = 0; i < p.WaterPlan.Lakes.Length; i++)
            {
                var lake = p.WaterPlan.Lakes[i];
                squares.Add(
                    new int3(
                        (int2)math.floor(lake.Center),
                        (int)lake.Radius + 2 * HydroGrid.CellVoxels
                    )
                );
            }

            var lakes = Survey(p, squares, LakeStride);

            Assert.That(rivers.x, Is.GreaterThan(10000), "wet river columns");
            Assert.That(rivers.z, Is.GreaterThan(0), "steps down the rivers");
            Assert.That(lakes.x, Is.GreaterThan(10000), "wet lake columns");
            Assert.That(rivers.y + lakes.y, Is.EqualTo(0), "water standing over dry ground");
        }

        // Every voxel of a chunk at a lake's surface: ground below the floored surface,
        // water up to the lake level, air above.
        [Test]
        public void Water_ChunksFillToTheColumnLevel()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var lake = p.WaterPlan.Lakes[0];
            var voxel = new int3(
                (int)lake.DeepestPoint.x,
                (int)lake.Level - 1,
                (int)lake.DeepestPoint.y
            );
            var coord = Coords.ChunkOf(voxel);
            var voxels = TerrainGenerator.GenerateChunk(p, coord, Allocator.Persistent);
            try
            {
                var origin = Coords.ChunkOrigin(coord);
                var water = 0;
                for (var z = 0; z < Size; z++)
                for (var x = 0; x < Size; x++)
                {
                    var column = WaterMap.Column(p, origin.x + x, origin.z + z);
                    for (var y = 0; y < Size; y++)
                    {
                        var wy = origin.y + y;
                        var id = voxels.Get(Coords.LocalIndex(x, y, z)).Value;
                        if (wy < column.Ground)
                        {
                            Assert.That(
                                id,
                                Is.Not.EqualTo(p.Water).And.Not.EqualTo(VoxelId.Air.Value)
                            );
                        }
                        else
                        {
                            var expected = wy < column.Level ? p.Water : VoxelId.Air.Value;
                            Assert.That(id, Is.EqualTo(expected), $"({x}, {y}, {z})");
                            water += id == p.Water ? 1 : 0;
                        }
                    }
                }

                Assert.That(water, Is.GreaterThan(Size * Size), "lake water in the chunk");
            }
            finally
            {
                voxels.Dispose();
            }
        }

        // Classification stays exact over lakes, rivers and falls: a chunk the column range
        // calls uniform is uniform.
        [Test]
        public void Water_ColumnClassification_MatchesGeneratedChunks()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var columns = new NativeList<int2>(8, Allocator.Temp);
            columns.Add(
                Coords.ColumnOf(
                    Coords.ChunkOf(
                        new int3(
                            (int)p.WaterPlan.Lakes[0].DeepestPoint.x,
                            0,
                            (int)p.WaterPlan.Lakes[0].DeepestPoint.y
                        )
                    )
                )
            );
            for (var n = 0; n + 1 < p.WaterPlan.Nodes.Length && columns.Length < 2; n++)
            {
                if (p.WaterPlan.Nodes[n].Level - p.WaterPlan.Nodes[n + 1].Level >= p.FallMin)
                {
                    var pos = (int2)math.floor(p.WaterPlan.Nodes[n].Pos);
                    columns.Add(Coords.ColumnOf(Coords.ChunkOf(new int3(pos.x, 0, pos.y))));
                }
            }

            using var range = new NativeReference<ColumnRange>(Allocator.TempJob);
            foreach (var column in columns)
            {
                new ColumnHeightJob
                {
                    Params = p,
                    Column = column,
                    Range = range,
                }
                    .Schedule()
                    .Complete();
                for (var cy = 0; cy < WorldConstants.ChunksY; cy++)
                {
                    if (
                        TerrainGenerator.Classify(p, range.Value, cy, out var id) == ChunkKind.Mixed
                    )
                    {
                        continue;
                    }

                    var voxels = TerrainGenerator.GenerateChunk(
                        p,
                        new ChunkCoord(column.x, cy, column.y),
                        Allocator.Persistent
                    );
                    try
                    {
                        Assert.That(voxels.IsUniform, Is.True, $"column {column}, y {cy}");
                        Assert.That(voxels.UniformId, Is.EqualTo(id), $"column {column}, y {cy}");
                    }
                    finally
                    {
                        voxels.Dispose();
                    }
                }
            }

            Assert.That(columns.Length, Is.GreaterThan(1));
            columns.Dispose();
        }

        // Lake beds are the lake bed type, river beds the river bed type.
        [Test]
        public void Water_BedsUnderLakesAndRivers()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            for (var i = 0; i < p.WaterPlan.Lakes.Length; i++)
            {
                var c = (int2)math.floor(p.WaterPlan.Lakes[i].DeepestPoint);
                var column = TerrainGenerator.Column(p, c.x, c.y);
                Assert.That(
                    TerrainGenerator.MaterialAt(p, column, column.Surface - 1),
                    Is.EqualTo(p.LakeBed),
                    $"lake {i}"
                );
            }

            var rivers = 0;
            for (var n = 0; n < p.WaterPlan.Nodes.Length && rivers < 20; n++)
            {
                var c = (int2)math.floor(p.WaterPlan.Nodes[n].Pos);
                var sample = TerrainGenerator.Sample(p, c.x, c.y);
                if (
                    sample.Body != WaterBody.River
                    || (int)math.floor(sample.Height) >= sample.WaterLevel
                )
                {
                    continue;
                }

                rivers++;
                var column = TerrainGenerator.Column(p, c.x, c.y);
                Assert.That(
                    TerrainGenerator.MaterialAt(p, column, column.Surface - 1),
                    Is.EqualTo(p.RiverBed),
                    $"node {n}"
                );
            }

            Assert.That(rivers, Is.GreaterThan(0));
        }

        // Done line: reefs. Coral heads in the reef biome, always under water, topped by the
        // biome's corals.
        [Test]
        public void Reef_CoralHeadsStayUnderTheSurface()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var registry = WorldAssets.LoadBiomeRegistry();
            var reef = -1;
            for (var i = 0; i < registry.Count; i++)
            {
                if (registry.Biomes[i].HasReef)
                {
                    reef = i;
                }
            }

            Assert.That(reef, Is.GreaterThanOrEqualTo(0), "no reef biome");
            var corals = 0;
            for (var j = 0; j < IslandGrid.Size; j++)
            for (var i = 0; i < IslandGrid.Size; i++)
            {
                var sample = IslandGrid.Samples[i + IslandGrid.Size * j];
                if (sample.Biome.Value != reef || sample.Reef < p.Biomes[reef].ReefCoralMin)
                {
                    continue;
                }

                var x = IslandGrid.Coordinate(i);
                var z = IslandGrid.Coordinate(j);
                var column = TerrainGenerator.Column(p, x, z);
                var top = TerrainGenerator.MaterialAt(p, column, column.Surface - 1);
                if (
                    top == p.Biomes[reef].ReefCorals[0]
                    || top == p.Biomes[reef].ReefCorals[p.Biomes[reef].ReefCorals.Length - 1]
                )
                {
                    corals++;
                    Assert.That(
                        column.Surface,
                        Is.LessThan(p.SeaLevel),
                        $"coral above water at ({x}, {z})"
                    );
                }
            }

            Assert.That(corals, Is.GreaterThanOrEqualTo(50), "coral heads on the 32 m grid");
        }

        // A lake's surface sits on the level step (1 m, D-076), so the detail levels whose
        // voxels are at most that size (1/2 to 1/8) draw it at the same height as full
        // resolution.
        [Test]
        public void DetailGen_LakeSurfaceIsTheSameAtEveryLevel()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            using var samples = new NativeArray<TerrainSample>(
                TerrainGenerator.DetailSampleGridCount,
                Allocator.TempJob
            );
            using var padded = new NativeArray<ushort>(
                WorldConstants.PaddedVoxels,
                Allocator.TempJob
            );
            for (var i = 0; i < p.WaterPlan.Lakes.Length; i++)
            {
                var lake = p.WaterPlan.Lakes[i];
                var center = (int2)math.floor(lake.DeepestPoint);
                for (var level = 1; level <= MatchingLevels; level++)
                {
                    var s = 1 << level;
                    var chunk = Size * s;
                    var coord = new ChunkCoord(
                        FloorDiv(center.x, chunk),
                        FloorDiv((int)lake.Level - 1, chunk),
                        FloorDiv(center.y, chunk)
                    );
                    new DetailFillJob
                    {
                        Params = p,
                        Level = level,
                        Coord = coord,
                        SideWalls = false,
                        Samples = samples,
                        Padded = padded,
                    }.Run();
                    var origin = coord.Value * chunk;
                    var x = (center.x - origin.x) / s;
                    var z = (center.y - origin.z) / s;
                    var top = -1;
                    for (var y = Size - 1; y >= 0 && top < 0; y--)
                    {
                        if (padded[Coords.PaddedIndex(x, y, z)] == p.Water)
                        {
                            top = origin.y + (y + 1) * s;
                        }
                    }

                    Assert.That(top, Is.EqualTo((int)lake.Level), $"lake {i}, level {level}");
                }
            }
        }

        // D-076: the ground the water shapes is gentle where the biome is (all but steep
        // wall biomes such as canyons and mountains). Across every third river node and
        // around every lake, the steepest step of shaped dry ground (over 1 m) stays under
        // 40° for nine river sections in ten and under 32° for nine lake shores in ten.
        [Test]
        public void Water_BanksAreGentle()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var rivers = new System.Collections.Generic.List<float>();
            var lakes = new System.Collections.Generic.List<float>();
            for (var n = 0; n + 1 < p.WaterPlan.Nodes.Length; n += 3)
            {
                var a = p.WaterPlan.Nodes[n].Pos;
                var dir = math.normalizesafe(p.WaterPlan.Nodes[n + 1].Pos - a);
                var side = new float2(-dir.y, dir.x);
                foreach (var d in new[] { side, -side })
                {
                    if (!dir.Equals(float2.zero) && SteepestShaped(p, a, d, 1200, out var deg))
                    {
                        rivers.Add(deg);
                    }
                }
            }

            for (var i = 0; i < p.WaterPlan.Lakes.Length; i++)
            {
                for (var k = 0; k < 16; k++)
                {
                    var angle = k * math.PI / 8f;
                    var d = new float2(math.cos(angle), math.sin(angle));
                    var length = (int)p.WaterPlan.Lakes[i].Radius + 1200;
                    if (
                        SteepestShaped(p, p.WaterPlan.Lakes[i].DeepestPoint, d, length, out var deg)
                    )
                    {
                        lakes.Add(deg);
                    }
                }
            }

            Assert.That(rivers.Count, Is.GreaterThan(100), "river sections");
            Assert.That(lakes.Count, Is.GreaterThan(100), "lake sections");
            Assert.That(Percentile90(rivers), Is.LessThan(35f), "river banks, 90th percentile");
            Assert.That(Percentile90(lakes), Is.LessThan(30f), "lake shores, 90th percentile");
        }

        // 2026-11-24 review ("just above the water surface, it jumps by about 2 to 4 voxels
        // straight up"): walking out of every river along its cross-sections, the ground
        // rises at most 2 voxels above the water within 3 voxels of the waterline on at
        // least 95 % of the banks. Banks within 24 m of a waterfall (its gorge walls hold
        // the water above it) are not counted.
        [Test]
        public void Rivers_BanksRiseSmoothlyFromTheWater()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var plan = p.WaterPlan;
            var falls = new System.Collections.Generic.List<float2>();
            for (var n = 0; n + 1 < plan.Nodes.Length; n++)
            {
                if (plan.Nodes[n].Level - plan.Nodes[n + 1].Level >= p.FallMin)
                {
                    falls.Add(plan.Nodes[n].Pos);
                }
            }

            var banks = 0;
            var steep = 0;
            for (var r = 0; r < plan.Rivers.Length; r++)
            {
                var river = plan.Rivers[r];
                for (var n = river.Start + 6; n + 6 < river.Start + river.Count; n += 3)
                {
                    var node = plan.Nodes[n];
                    var nearFall = false;
                    foreach (var fall in falls)
                    {
                        nearFall |=
                            math.distance(fall, node.Pos) < 24f * WorldConstants.VoxelsPerMeter;
                    }

                    if (nearFall)
                    {
                        continue;
                    }

                    var dir = math.normalizesafe(plan.Nodes[n + 1].Pos - node.Pos);
                    var side = new float2(-dir.y, dir.x);
                    for (var s = -1; s <= 1; s += 2)
                    {
                        var surface = int.MinValue;
                        for (var k = 0; k <= 120; k++)
                        {
                            var q = (int2)math.floor(node.Pos + side * (s * k));
                            var column = WaterMap.Column(p, q.x, q.y);
                            if (column.IsWet)
                            {
                                surface = column.Level;
                                continue;
                            }

                            if (surface == int.MinValue)
                            {
                                break;
                            }

                            var beyond = (int2)math.floor(node.Pos + side * (s * (k + 3)));
                            var rise = WaterMap.Column(p, beyond.x, beyond.y).Ground - surface;
                            banks++;
                            steep += rise > 2 ? 1 : 0;
                            break;
                        }
                    }
                }
            }

            Assert.That(banks, Is.GreaterThan(500), "banks");
            Assert.That(
                (float)steep / banks,
                Is.LessThan(0.05f),
                $"steep banks ({steep} of {banks})"
            );
        }

        // Walking out of the water along dir: the steepest step (degrees, over 1 m) between
        // two dry columns where at least one is shaped, in a biome whose river walls are
        // not steep. False when the walk never leaves the water or the steepest step lies
        // in a steep wall biome.
        private static bool SteepestShaped(
            in TerrainParams p,
            float2 from,
            float2 dir,
            int length,
            out float steepest
        )
        {
            const int step = WorldConstants.VoxelsPerMeter;
            const float steepWallRun = 2f;
            steepest = 0f;
            var steepestRun = float.MaxValue;
            var started = false;
            var previous = 0f;
            var previousShaped = false;
            var hasPrevious = false;
            var lastShaped = 0;
            for (var i = 0; i <= length; i += step)
            {
                var q = from + dir * i;
                var x = (int)math.floor(q.x);
                var z = (int)math.floor(q.y);
                var sample = TerrainGenerator.Sample(p, x, z);
                var land = TerrainGenerator.LandHeight(p, x, z, out _);
                if ((int)math.floor(sample.Height) < sample.WaterLevel)
                {
                    hasPrevious = false;
                    continue;
                }

                started = true;
                var shaped = math.abs(land - sample.Height) > 1f;
                lastShaped = shaped ? i : lastShaped;
                if (hasPrevious && (shaped || previousShaped))
                {
                    var deg = math.degrees(math.atan(math.abs(sample.Height - previous) / step));
                    if (deg > steepest)
                    {
                        steepest = deg;
                        steepestRun = p.Biomes[sample.Biome.Value].RiverWallRun;
                    }
                }

                previous = sample.Height;
                previousShaped = shaped;
                hasPrevious = true;
                if (lastShaped > 0 && i - lastShaped > 20 * step)
                {
                    break;
                }
            }

            return started && steepestRun >= steepWallRun;
        }

        private static float Percentile90(System.Collections.Generic.List<float> values)
        {
            values.Sort();
            return values[values.Count * 9 / 10];
        }

        private static int3 Survey(in TerrainParams p, NativeList<int3> squares, int stride)
        {
            using var counts = new NativeReference<int4>(Allocator.TempJob);
            using var leaks = new NativeArray<int2>(8, Allocator.TempJob);
            new WaterSurveyJob
            {
                Params = p,
                Squares = squares.AsArray(),
                Stride = stride,
                Counts = counts,
                Leaks = leaks,
            }.Run();
            var c = counts.Value;
            if (c.y > 0)
            {
                TestContext.WriteLine($"first leaks near {leaks[0]}, {leaks[1]}");
            }

            return c.xyz;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((b - 1 - a) / b);
    }
}
