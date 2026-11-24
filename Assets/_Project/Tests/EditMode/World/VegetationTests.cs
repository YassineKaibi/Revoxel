using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    // Trees and ground plants (PLAN-06, D-083 to D-088).
    public sealed class VegetationTests
    {
        private const int Size = WorldConstants.ChunkSize;

        // Squares of 32 m on a 400 m grid over the island, scanned once.
        private const int ScanStepVoxels = 400 * WorldConstants.VoxelsPerMeter;
        private const int ScanSquareVoxels = 32 * WorldConstants.VoxelsPerMeter;

        private static List<Square> _scan;

        private struct Square
        {
            public int2 Min;
            public BiomeId Biome;
            public TreePlan[] Trees;
        }

        [Test]
        public void Vegetation_SettingsAndSpeciesAreValidAndUsed()
        {
            var terrain = WorldAssets.LoadTerrainSettings();
            var settings = terrain.VegetationSettings;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.name, Does.StartWith("Placeholder_"));
            Assert.That(settings.TryValidate(out var error), Is.True, error);
            Assert.That(terrain.TryValidate(out error), Is.True, error);
            Assert.That(settings.Species.Length, Is.GreaterThanOrEqualTo(8));
            var used = new HashSet<TreeSpecies>();
            foreach (var biome in terrain.Biomes.Biomes)
            {
                foreach (var species in biome.Trees)
                {
                    Assert.That(settings.IndexOf(species), Is.GreaterThanOrEqualTo(0), biome.name);
                    used.Add(species);
                }
            }

            foreach (var species in settings.Species)
            {
                Assert.That(species.name, Does.StartWith("Species_"));
                Assert.That(used, Does.Contain(species), $"{species.name} grows in no biome");
            }

            // Ground plants are passable (D-091); everything else they stand on is solid.
            foreach (var biome in terrain.Biomes.Biomes)
            {
                foreach (var plant in biome.Plants)
                {
                    Assert.That(plant.Stem.IsSolid, Is.False, $"{biome.name}: {plant.Stem.name}");
                    Assert.That(plant.Head == null || !plant.Head.IsSolid, Is.True, biome.name);
                }
            }

            var forms = settings.Species.Select(s => s.Form).Distinct().ToArray();
            Assert.That(
                forms,
                Is.EquivalentTo(new[] { TreeForm.Broadleaf, TreeForm.Conifer, TreeForm.Palm })
            );
        }

        [Test]
        public void Trees_FollowTheirBiomes()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var registry = WorldAssets.LoadBiomeRegistry();
            var settings = WorldAssets.LoadTerrainSettings().VegetationSettings;
            var forestArea = 0;
            var forestTrees = 0;
            var plainsArea = 0;
            var plainsTrees = 0;
            var species = new HashSet<int>();
            foreach (var square in Scan(p))
            {
                foreach (var plan in square.Trees)
                {
                    var weights = new FixedList128Bytes<float>();
                    var column = (int2)math.floor(plan.Base.xz);
                    var biome = TerrainGenerator.BiomeAt(p, column.x, column.y, ref weights);
                    var allowed = registry.Biomes[biome.Value].Trees.Select(settings.IndexOf);
                    Assert.That(
                        allowed,
                        Does.Contain(plan.Species),
                        $"tree at {column} in {registry.Biomes[biome.Value].name}"
                    );
                    species.Add(plan.Species);
                }

                var name = registry.Biomes[square.Biome.Value].NameKey;
                if (name == "biome.forest.name")
                {
                    forestArea++;
                    forestTrees += square.Trees.Length;
                }
                else if (name == "biome.plains.name")
                {
                    plainsArea++;
                    plainsTrees += square.Trees.Length;
                }
            }

            // Trees per hectare (a square is 0.1024 ha).
            Assert.That(forestArea, Is.GreaterThan(0), "no forest square scanned");
            var forest = forestTrees / (forestArea * 0.1024f);
            Assert.That(forest, Is.GreaterThan(60f), "forest density");
            if (plainsArea > 0)
            {
                Assert.That(
                    plainsTrees / (plainsArea * 0.1024f),
                    Is.LessThan(forest),
                    "plains denser than forest"
                );
            }

            Assert.That(species.Count, Is.GreaterThanOrEqualTo(4), "species found");
        }

        [Test]
        public void Trees_StandOnLandTheySuit()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var count = 0;
            foreach (var square in Scan(p))
            {
                foreach (var plan in square.Trees)
                {
                    var s = p.Vegetation.Species[plan.Species];
                    var column = (int2)math.floor(plan.Base.xz);
                    var sample = TerrainGenerator.Sample(p, column.x, column.y);
                    var ground = (int)math.floor(sample.Height);
                    Assert.That(
                        sample.WaterLevel - ground,
                        Is.LessThanOrEqualTo(s.MaxWaterDepth),
                        $"tree in water at {column}"
                    );
                    Assert.That(
                        sample.TemperatureC,
                        Is.GreaterThanOrEqualTo(s.MinTemperatureC),
                        $"tree above its tree line at {column}"
                    );
                    Assert.That(
                        plan.Base.y,
                        Is.LessThanOrEqualTo(ground - 2),
                        $"trunk not sunk at {column}"
                    );
                    if (!s.GrowsOnShore && sample.WaterLevel <= ground)
                    {
                        Assert.That(
                            ground,
                            Is.GreaterThan(p.SeaLevel + p.ShoreHeight),
                            $"tree on the shore at {column}"
                        );
                    }

                    count++;
                }
            }

            Assert.That(count, Is.GreaterThan(50));
        }

        [Test]
        public void Trees_NoTwoAlike()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var signatures = new Dictionary<int, HashSet<string>>();
            using var parts = new NativeList<TreePart>(512, Allocator.Temp);
            var scratch = TreeScratch.Create(Allocator.Temp);
            try
            {
                foreach (var plan in Scan(p).SelectMany(s => s.Trees).Take(80))
                {
                    parts.Clear();
                    TreeGrowth.Grow(p, plan, ref scratch, parts);
                    var shape = new float3(0f);
                    for (var i = 0; i < parts.Length; i++)
                    {
                        shape += parts[i].A - plan.Base;
                    }

                    var signature = $"{parts.Length}:{math.round(shape * 10f)}";
                    if (!signatures.TryGetValue(plan.Species, out var set))
                    {
                        set = new HashSet<string>();
                        signatures.Add(plan.Species, set);
                    }

                    Assert.That(
                        set.Add(signature),
                        Is.True,
                        $"two trees of species {plan.Species} alike"
                    );
                }
            }
            finally
            {
                scratch.Dispose();
            }

            Assert.That(signatures.Values.Sum(s => s.Count), Is.GreaterThan(20));
        }

        [Test]
        public void Trees_TrunksAreWideAndCurved()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var trees = 0;
            var curved = 0;
            foreach (var plan in Scan(p).SelectMany(s => s.Trees))
            {
                var s = p.Vegetation.Species[plan.Species];
                if (s.Height.y < 40f)
                {
                    continue; // shrubs
                }

                Assert.That(
                    plan.TrunkRadius * 2f,
                    Is.GreaterThanOrEqualTo(2f),
                    "trunk narrower than two voxels"
                );
                var phase = Random.CreateFromIndex(plan.Seed ^ 0x6A09E667u).NextFloat2(-500f, 500f);
                var bottom = TreeGrowth.TrunkAt(plan, s, phase, 0f);
                var top = TreeGrowth.TrunkAt(plan, s, phase, plan.Height);
                var deviation = 0f;
                for (var k = 1; k < 8; k++)
                {
                    var t = k / 8f;
                    var at = TreeGrowth.TrunkAt(plan, s, phase, plan.Height * t);
                    deviation = math.max(
                        deviation,
                        math.distance(at.xz, math.lerp(bottom.xz, top.xz, t))
                    );
                }

                trees++;
                curved += deviation >= 1f ? 1 : 0;
            }

            Assert.That(trees, Is.GreaterThan(20));
            Assert.That(
                curved,
                Is.GreaterThan(trees / 2),
                "most trunks should bend off a straight line"
            );
        }

        [Test]
        public void Trees_SizesAndProportionsVary()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var checkedSpecies = 0;
            foreach (var group in Scan(p).SelectMany(s => s.Trees).GroupBy(t => t.Species))
            {
                var trees = group.OrderBy(t => t.Age).ToArray();
                if (trees.Length < 12 || p.Vegetation.Species[group.Key].Form == TreeForm.Palm)
                {
                    continue;
                }

                var heights = trees.Select(t => t.Height).ToArray();
                var mean = heights.Average();
                var deviation = math.sqrt(heights.Select(h => (h - mean) * (h - mean)).Average());
                Assert.That(
                    deviation / mean,
                    Is.GreaterThan(0.2f),
                    $"species {group.Key}: heights too alike"
                );

                // Saplings are not scaled-down old trees: slimmer crowns starting lower.
                var quarter = trees.Length / 4;
                var young = trees.Take(quarter).ToArray();
                var old = trees.Skip(trees.Length - quarter).ToArray();
                Assert.That(
                    young.Average(CrownBaseShare),
                    Is.LessThan(old.Average(CrownBaseShare)),
                    $"species {group.Key}: crown base"
                );
                Assert.That(
                    young.Average(CrownShare),
                    Is.LessThan(old.Average(CrownShare)),
                    $"species {group.Key}: crown width"
                );
                // Shrub stems all sit at the thinnest trunk.
                if (p.Vegetation.Species[group.Key].Height.y >= 40f)
                {
                    Assert.That(
                        young.Average(t => t.TrunkRadius),
                        Is.LessThan(old.Average(t => t.TrunkRadius) * 0.7f),
                        $"species {group.Key}: trunk"
                    );
                }

                checkedSpecies++;
            }

            Assert.That(checkedSpecies, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void Trees_CrownsAreDenseAndWhole()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var checkedTrees = 0;
            using var parts = new NativeList<TreePart>(512, Allocator.Temp);
            var scratch = TreeScratch.Create(Allocator.Temp);
            try
            {
                foreach (var plan in Scan(p).SelectMany(s => s.Trees))
                {
                    var s = p.Vegetation.Species[plan.Species];
                    if (
                        s.Form != TreeForm.Broadleaf
                        || s.Leaves == 0
                        || plan.Height < 32f
                        || checkedTrees >= 30
                    )
                    {
                        continue;
                    }

                    parts.Clear();
                    TreeGrowth.Grow(p, plan, ref scratch, parts);
                    var branches = 0;
                    var clusters = 0;
                    foreach (var part in parts)
                    {
                        branches += part.Kind == TreePartKind.Wood ? 1 : 0;
                        clusters += part.Kind == TreePartKind.Leaves ? 1 : 0;
                    }

                    // No tree is a trunk with one ball on top (D-089).
                    Assert.That(
                        clusters,
                        Is.GreaterThanOrEqualTo(8),
                        $"tree at {plan.Base}: leaf clusters (species {plan.Species}, height {plan.Height}, age {plan.Age}, crown {plan.CrownRadii}, center {plan.CrownCenter}, parts {parts.Length})"
                    );
                    Assert.That(branches, Is.GreaterThan(20), $"tree at {plan.Base}: branches");
                    LeafStats(p, plan, parts, out var leaves, out var largest);
                    var r = plan.CrownRadii;
                    var envelope = 4f / 3f * math.PI * r.x * r.y * r.z;
                    Assert.That(
                        leaves / envelope,
                        Is.GreaterThan(0.3f),
                        $"tree at {plan.Base}: crown density"
                    );
                    Assert.That(
                        largest,
                        Is.GreaterThan(leaves * 0.85f),
                        $"tree at {plan.Base}: crown in pieces"
                    );
                    checkedTrees++;
                }
            }
            finally
            {
                scratch.Dispose();
            }

            Assert.That(checkedTrees, Is.GreaterThanOrEqualTo(10));
        }

        [Test]
        public void Plants_StemsBendAcrossColumns()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var registry = WorldAssets.LoadBiomeRegistry();
            var square = Scan(p)
                .First(s => registry.Biomes[s.Biome.Value].NameKey == "biome.plains.name");
            var tall = 0;
            var bent = 0;
            for (var z = 0; z < ScanSquareVoxels; z++)
            for (var x = 0; x < ScanSquareVoxels; x += 2)
            {
                var wx = square.Min.x + x;
                var wz = square.Min.y + z;
                var sample = TerrainGenerator.Sample(p, wx, wz);
                var height = Vegetation.PlantAt(
                    p,
                    sample,
                    wx,
                    wz,
                    float.NaN,
                    out _,
                    out _,
                    out var bend,
                    out var bendAt
                );
                if (height < 3)
                {
                    continue;
                }

                tall++;
                if (bend != Vegetation.BendNone)
                {
                    bent++;
                    Assert.That(bendAt, Is.InRange(1, height - 1));
                }
            }

            Assert.That(tall, Is.GreaterThan(20));
            Assert.That(bent, Is.GreaterThan(tall / 5), "stems should not all stand straight");
            Assert.That(bent, Is.LessThan(tall), "some stems stand straight");
        }

        [Test]
        public void Trees_WoodIsFaceConnectedAndWithinReach()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var forms = new HashSet<TreeForm>();
            var checkedTrees = 0;
            using var parts = new NativeList<TreePart>(512, Allocator.Temp);
            var scratch = TreeScratch.Create(Allocator.Temp);
            try
            {
                foreach (var plan in Scan(p).SelectMany(s => s.Trees))
                {
                    var s = p.Vegetation.Species[plan.Species];
                    if (checkedTrees >= 24 && forms.Contains(s.Form))
                    {
                        continue;
                    }

                    parts.Clear();
                    TreeGrowth.Grow(p, plan, ref scratch, parts);
                    AssertWithinReach(plan, s, parts);
                    AssertWoodConnected(p, plan, parts);
                    forms.Add(s.Form);
                    checkedTrees++;
                }
            }
            finally
            {
                scratch.Dispose();
            }

            Assert.That(checkedTrees, Is.GreaterThanOrEqualTo(10));
        }

        [Test]
        public void Trees_ColumnsAgreeAcrossBorders()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var square = Forest(p);
            var column = Coords.ColumnOf(Coords.ChunkOf(new int3(square.Min.x, 0, square.Min.y)));
            var shared = 0;
            for (var d = 0; d < 4; d++)
            {
                var a = column + new int2(d, 0);
                var b = a + new int2(1, 0);
                using var partsA = VegetationCollectJob.Collect(p, a, out _);
                using var partsB = VegetationCollectJob.Collect(p, b, out _);
                var bMin = b * Size;
                var bMax = bMin + Size - 1;
                var inB = new HashSet<string>();
                foreach (var part in partsB)
                {
                    inB.Add(Key(part));
                }

                foreach (var part in partsA)
                {
                    if (
                        part.Max.x < bMin.x
                        || part.Min.x > bMax.x
                        || part.Max.z < bMin.y
                        || part.Min.z > bMax.y
                    )
                    {
                        continue;
                    }

                    shared++;
                    Assert.That(
                        inB,
                        Does.Contain(Key(part)),
                        $"part of column {a} missing from {b}"
                    );
                }
            }

            Assert.That(shared, Is.GreaterThan(0), "no tree crosses a column border");
        }

        [Test]
        public void Trees_Deterministic()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var q = WorldAssets.TerrainParams(Seeds.Other);
            var square = Forest(p);
            var column = Coords.ColumnOf(Coords.ChunkOf(new int3(square.Min.x, 0, square.Min.y)));
            using var a = VegetationCollectJob.Collect(p, column, out var ha);
            using var b = VegetationCollectJob.Collect(p, column, out var hb);
            using var c = VegetationCollectJob.Collect(q, column, out _);

            Assert.That(a.Length, Is.GreaterThan(0));
            Assert.That(ha, Is.EqualTo(hb));
            Assert.That(
                a.AsArray().Select(Key).ToArray(),
                Is.EqualTo(b.AsArray().Select(Key).ToArray())
            );
            Assert.That(
                a.AsArray().Select(Key).ToArray(),
                Is.Not.EqualTo(c.AsArray().Select(Key).ToArray())
            );
        }

        [Test]
        public void Plants_GrowOnSoilInClumps()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var registry = WorldAssets.LoadBiomeRegistry();
            var plants = 0;
            var columns = 0;
            var inWater = 0;
            foreach (var square in Scan(p))
            {
                for (var z = 0; z < ScanSquareVoxels; z += 3)
                for (var x = 0; x < ScanSquareVoxels; x += 3)
                {
                    var wx = square.Min.x + x;
                    var wz = square.Min.y + z;
                    var sample = TerrainGenerator.Sample(p, wx, wz);
                    var slope = TerrainGenerator.Slope(
                        TerrainGenerator.SmoothHeight(p, wx + 1, wz),
                        TerrainGenerator.SmoothHeight(p, wx - 1, wz),
                        TerrainGenerator.SmoothHeight(p, wx, wz + 1),
                        TerrainGenerator.SmoothHeight(p, wx, wz - 1)
                    );
                    var column = TerrainGenerator.Column(p, wx, wz, sample, slope);
                    var height = Vegetation.PlantAt(
                        p,
                        sample,
                        wx,
                        wz,
                        slope,
                        out var stem,
                        out _,
                        out var bend,
                        out var bendAt
                    );
                    columns++;
                    if (height == 0)
                    {
                        continue;
                    }

                    plants++;
                    var b = registry.Biomes[sample.Biome.Value];
                    Assert.That(
                        b.Plants.Select(pl => pl.Stem.Id.Value),
                        Does.Contain(stem),
                        $"{b.name} at ({wx}, {wz})"
                    );
                    Assert.That(
                        column.Top,
                        Is.Not.EqualTo(VoxelId.Air.Value),
                        "plant on bare rock"
                    );
                    Assert.That(column.Top, Is.Not.EqualTo(p.Snow), "plant on snow");
                    var ground = (int)math.floor(sample.Height);
                    if (sample.WaterLevel > ground)
                    {
                        inWater++;
                        Assert.That(
                            ground + height,
                            Is.GreaterThan(sample.WaterLevel),
                            "a water plant under the surface"
                        );
                    }

                    Assert.That(height, Is.LessThanOrEqualTo(p.Vegetation.MaxPlantHeight));
                }
            }

            Assert.That(plants, Is.GreaterThan(columns / 200), "too few plants");
            Assert.That(plants, Is.LessThan(columns / 4), "too many plants");
            Assert.That(inWater, Is.LessThan(plants));
        }

        [Test]
        public void Vegetation_ColumnClassification_MatchesGeneratedChunks()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var square = Forest(p);
            var column = Coords.ColumnOf(Coords.ChunkOf(new int3(square.Min.x, 0, square.Min.y)));
            var tree = 0;
            using var range = new NativeReference<ColumnRange>(Allocator.TempJob);
            using var parts = new NativeList<TreePart>(Allocator.TempJob);
            for (var d = 0; d < 3; d++)
            {
                var c = column + new int2(d, d);
                new ColumnHeightJob
                {
                    Params = p,
                    Column = c,
                    Range = range,
                    Parts = parts,
                }
                    .Schedule()
                    .Complete();
                var surfaceTop = range.Value.Height.y;
                for (var cy = 0; cy < WorldConstants.ChunksY; cy++)
                {
                    var kind = TerrainGenerator.Classify(p, range.Value, cy, out var id);
                    var voxels = TerrainGenerator.GenerateChunk(
                        p,
                        new ChunkCoord(c.x, cy, c.y),
                        Allocator.Persistent
                    );
                    try
                    {
                        if (kind != ChunkKind.Mixed)
                        {
                            Assert.That(voxels.IsUniform, Is.True, $"column {c}, y {cy}");
                            Assert.That(voxels.UniformId, Is.EqualTo(id), $"column {c}, y {cy}");
                        }
                        else if (cy * Size >= surfaceTop && !voxels.IsUniform)
                        {
                            tree++;
                        }
                    }
                    finally
                    {
                        voxels.Dispose();
                    }
                }
            }

            Assert.That(tree, Is.GreaterThan(0), "no chunk above the ground holds a tree");
        }

        [Test]
        public void Vegetation_StreamingJobsMatchGenerateChunk()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var square = Forest(p);
            var column = Coords.ColumnOf(Coords.ChunkOf(new int3(square.Min.x, 0, square.Min.y)));
            using var range = new NativeReference<ColumnRange>(Allocator.TempJob);
            using var parts = new NativeList<TreePart>(Allocator.TempJob);
            using var scratch = new NativeArray<ushort>(
                WorldConstants.VoxelsPerChunk,
                Allocator.TempJob
            );
            using var result = new NativeReference<ChunkVoxels>(Allocator.TempJob);
            new ColumnHeightJob
            {
                Params = p,
                Column = column,
                Range = range,
                Parts = parts,
            }
                .Schedule()
                .Complete();
            var vegetation = VegetationIds.Of(p);
            var compared = 0;
            for (var cy = 0; cy < WorldConstants.ChunksY; cy++)
            {
                if (TerrainGenerator.Classify(p, range.Value, cy, out _) != ChunkKind.Mixed)
                {
                    continue;
                }

                var coord = new ChunkCoord(column.x, cy, column.y);
                new ChunkGenJob
                {
                    Params = p,
                    Coord = coord,
                    Parts = parts.AsArray(),
                    KnownRange = range.Value,
                    HasKnownRange = true,
                    Scratch = scratch,
                    Result = result,
                }
                    .Schedule()
                    .Complete();
                using var actual = new NativeArray<ushort>(
                    WorldConstants.VoxelsPerChunk,
                    Allocator.Temp
                );
                result.Value.CopyTo(actual);
                result.Value.Dispose();
                using var expected = new NativeArray<ushort>(
                    WorldConstants.VoxelsPerChunk,
                    Allocator.Temp
                );
                TerrainGenerator.FillChunk(p, coord, expected);
                var differing = 0;
                var first = string.Empty;
                for (var i = 0; i < actual.Length; i++)
                {
                    // Far from the origin Burst and managed code round the strata noise
                    // differently (R-21); only trees and plants are compared here.
                    var tree = vegetation.Contains(actual[i]) || vegetation.Contains(expected[i]);
                    if (tree && actual[i] != expected[i])
                    {
                        if (differing < 6)
                        {
                            first += $" {Coords.LocalFromIndex(i)}: {actual[i]} vs {expected[i]};";
                        }

                        differing++;
                    }
                }

                Assert.That(differing, Is.LessThan(64), $"chunk {coord}:{first}");
                compared++;
            }

            Assert.That(compared, Is.GreaterThan(2));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void DetailGen_TreesShowAtCoarseLevels(int level)
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var square = Forest(p);
            var s = 1 << level;
            var cell = new int2(FloorDiv(square.Min.x, Size * s), FloorDiv(square.Min.y, Size * s));
            var leaves = new HashSet<ushort>();
            for (var i = 0; i < p.Vegetation.Species.Length; i++)
            {
                leaves.Add(p.Vegetation.Species[i].Leaves);
                leaves.Add(p.Vegetation.Species[i].Bark);
            }

            leaves.Remove(0);
            var range = TerrainGenerator.DetailRange(p, cell, level);
            Assert.That(range.HasVegetation, Is.True);
            var found = 0;
            using var samples = new NativeArray<TerrainSample>(
                TerrainGenerator.DetailSampleGridCount,
                Allocator.TempJob
            );
            using var padded = new NativeArray<ushort>(
                WorldConstants.PaddedVoxels,
                Allocator.TempJob
            );
            for (var cy = 0; cy < WorldConstants.ChunksY >> level; cy++)
            {
                if (!TerrainGenerator.DetailChunkNeedsMesh(p, range, cy, level, 2))
                {
                    continue;
                }

                new DetailFillJob
                {
                    Params = p,
                    Level = level,
                    Coord = new ChunkCoord(cell.x, cy, cell.y),
                    SideWalls = true,
                    Samples = samples,
                    Padded = padded,
                }.Run();
                for (var y = 0; y < Size; y++)
                for (var z = 0; z < Size; z++)
                for (var x = 0; x < Size; x++)
                {
                    found += leaves.Contains(padded[Coords.PaddedIndex(x, y, z)]) ? 1 : 0;
                }
            }

            Assert.That(found, Is.GreaterThan(0), $"no tree at level {level}");
        }

        private static void AssertWithinReach(
            in TreePlan plan,
            in TreeSpeciesParams s,
            NativeList<TreePart> parts
        )
        {
            foreach (var part in parts)
            {
                var corners = new[]
                {
                    new float2(part.Min.x, part.Min.z),
                    new float2(part.Max.x + 1, part.Min.z),
                    new float2(part.Min.x, part.Max.z + 1),
                    new float2(part.Max.x + 1, part.Max.z + 1),
                };
                foreach (var corner in corners)
                {
                    Assert.That(
                        math.distance(corner, plan.Base.xz),
                        Is.LessThanOrEqualTo(s.Reach + 2f),
                        "a part beyond the species' reach"
                    );
                }
            }
        }

        // Every wood voxel of one tree reaches the lowest wood layer through face neighbors.
        private static void AssertWoodConnected(
            in TerrainParams p,
            in TreePlan plan,
            NativeList<TreePart> parts
        )
        {
            var min = new int3(int.MaxValue);
            var max = new int3(int.MinValue);
            foreach (var part in parts)
            {
                if (part.Kind == TreePartKind.Wood)
                {
                    min = math.min(min, part.Min);
                    max = math.max(max, part.Max);
                }
            }

            var size = max - min + 1;
            var wood = new bool[size.x * size.y * size.z];
            int Index(int3 v) => v.x + size.x * (v.z + size.z * v.y);
            var total = 0;
            foreach (var part in parts)
            {
                if (part.Kind != TreePartKind.Wood)
                {
                    continue;
                }

                for (var y = part.Min.y; y <= part.Max.y; y++)
                for (var z = part.Min.z; z <= part.Max.z; z++)
                for (var x = part.Min.x; x <= part.Max.x; x++)
                {
                    var v = new int3(x, y, z);
                    var i = Index(v - min);
                    if (!wood[i] && Vegetation.Voxel(p, part, (float3)v + 0.5f, 0) != 0)
                    {
                        wood[i] = true;
                        total++;
                    }
                }
            }

            var seen = new bool[wood.Length];
            var queue = new Queue<int3>();
            for (var z = 0; z < size.z; z++)
            for (var x = 0; x < size.x; x++)
            {
                var v = new int3(x, 0, z);
                if (wood[Index(v)])
                {
                    seen[Index(v)] = true;
                    queue.Enqueue(v);
                }
            }

            if (queue.Count == 0)
            {
                // The lowest layer of the bounds may hold no wood: start from the lowest that does.
                for (var y = 1; y < size.y && queue.Count == 0; y++)
                for (var z = 0; z < size.z; z++)
                for (var x = 0; x < size.x; x++)
                {
                    var v = new int3(x, y, z);
                    if (wood[Index(v)])
                    {
                        seen[Index(v)] = true;
                        queue.Enqueue(v);
                    }
                }
            }

            var reached = queue.Count;
            var steps = new[]
            {
                new int3(1, 0, 0),
                new int3(-1, 0, 0),
                new int3(0, 1, 0),
                new int3(0, -1, 0),
                new int3(0, 0, 1),
                new int3(0, 0, -1),
            };
            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                foreach (var step in steps)
                {
                    var n = v + step;
                    if (math.any(n < 0) || math.any(n >= size))
                    {
                        continue;
                    }

                    var i = Index(n);
                    if (wood[i] && !seen[i])
                    {
                        seen[i] = true;
                        reached++;
                        queue.Enqueue(n);
                    }
                }
            }

            Assert.That(reached, Is.EqualTo(total), $"loose wood in the tree at {plan.Base}");
        }

        private static float CrownBaseShare(TreePlan t) =>
            (t.CrownCenter.y - t.CrownRadii.y - t.Base.y - 2f) / t.Height;

        private static float CrownShare(TreePlan t) => t.CrownRadii.x / t.Height;

        // Leaf voxels of one tree and the size of its largest face-connected group.
        private static void LeafStats(
            in TerrainParams p,
            in TreePlan plan,
            NativeList<TreePart> parts,
            out int total,
            out int largest
        )
        {
            var min = new int3(int.MaxValue);
            var max = new int3(int.MinValue);
            foreach (var part in parts)
            {
                if (part.Kind == TreePartKind.Leaves)
                {
                    min = math.min(min, part.Min);
                    max = math.max(max, part.Max);
                }
            }

            var size = max - min + 1;
            var leaf = new bool[size.x * size.y * size.z];
            int Index(int3 v) => v.x + size.x * (v.z + size.z * v.y);
            total = 0;
            foreach (var part in parts)
            {
                if (part.Kind != TreePartKind.Leaves)
                {
                    continue;
                }

                for (var y = part.Min.y; y <= part.Max.y; y++)
                for (var z = part.Min.z; z <= part.Max.z; z++)
                for (var x = part.Min.x; x <= part.Max.x; x++)
                {
                    var v = new int3(x, y, z);
                    var i = Index(v - min);
                    if (!leaf[i] && Vegetation.Voxel(p, part, (float3)v + 0.5f, 0) != 0)
                    {
                        leaf[i] = true;
                        total++;
                    }
                }
            }

            var seen = new bool[leaf.Length];
            largest = 0;
            var steps = new[]
            {
                new int3(1, 0, 0),
                new int3(-1, 0, 0),
                new int3(0, 1, 0),
                new int3(0, -1, 0),
                new int3(0, 0, 1),
                new int3(0, 0, -1),
            };
            var queue = new Queue<int3>();
            for (var i = 0; i < leaf.Length; i++)
            {
                if (!leaf[i] || seen[i])
                {
                    continue;
                }

                var start = new int3(i % size.x, i / (size.x * size.z), (i / size.x) % size.z);
                seen[i] = true;
                queue.Enqueue(start);
                var count = 0;
                while (queue.Count > 0)
                {
                    var v = queue.Dequeue();
                    count++;
                    foreach (var step in steps)
                    {
                        var n = v + step;
                        if (math.any(n < 0) || math.any(n >= size))
                        {
                            continue;
                        }

                        var j = Index(n);
                        if (leaf[j] && !seen[j])
                        {
                            seen[j] = true;
                            queue.Enqueue(n);
                        }
                    }
                }

                largest = math.max(largest, count);
            }
        }

        private static string Key(TreePart part) =>
            $"{part.Kind}:{part.A}:{part.B}:{part.RA}:{part.RB}:{part.Outer}";

        private static Square Forest(in TerrainParams p)
        {
            var registry = WorldAssets.LoadBiomeRegistry();
            var best = default(Square);
            foreach (var square in Scan(p))
            {
                if (
                    registry.Biomes[square.Biome.Value].NameKey == "biome.forest.name"
                    && (best.Trees == null || square.Trees.Length > best.Trees.Length)
                )
                {
                    best = square;
                }
            }

            Assert.That(best.Trees, Is.Not.Null.And.Not.Empty, "no forest with trees");
            return best;
        }

        // The fixture seed's squares, scanned once per run (managed code; slow but small).
        private static List<Square> Scan(in TerrainParams p)
        {
            if (_scan != null)
            {
                return _scan;
            }

            _scan = new List<Square>();
            using var plans = new NativeList<TreePlan>(64, Allocator.Temp);
            const int half = WorldConstants.IslandMaxChunkXZ * Size * 3 / 4;
            for (var z = -half; z < half; z += ScanStepVoxels)
            for (var x = -half; x < half; x += ScanStepVoxels)
            {
                var weights = new FixedList128Bytes<float>();
                var center = new int2(x, z) + ScanSquareVoxels / 2;
                if (TerrainGenerator.SurfaceHeight(p, center.x, center.y) <= p.SeaLevel)
                {
                    continue;
                }

                var biome = TerrainGenerator.BiomeAt(p, center.x, center.y, ref weights);
                var min = new int2(x, z);
                Vegetation.CollectTrees(p, min, min + ScanSquareVoxels - 1, plans);
                _scan.Add(
                    new Square
                    {
                        Min = min,
                        Biome = biome,
                        Trees = plans.AsArray().ToArray(),
                    }
                );
            }

            return _scan;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((b - 1 - a) / b);
    }
}
