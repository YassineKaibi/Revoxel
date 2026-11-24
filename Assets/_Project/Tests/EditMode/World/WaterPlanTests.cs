using NUnit.Framework;
using Revoxel.World;
using Unity.Mathematics;
using UnityEditor;

namespace Revoxel.Tests.EditMode
{
    // The river and lake plan of the fixture seed (PLAN-05 Task 1, D-070, D-071).
    public sealed class WaterPlanTests
    {
        private const string WaterSettingsPath =
            "Assets/_Project/Data/World/Placeholder_WaterSettings.asset";

        private const float LevelEpsilon = 1e-3f;

        [Test]
        public void Water_SettingsAssetIsValidAndUsed()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WaterSettings>(WaterSettingsPath);

            Assert.That(settings, Is.Not.Null, WaterSettingsPath);
            Assert.That(settings.name, Does.StartWith("Placeholder_"));
            Assert.That(settings.TryValidate(out var error), Is.True, error);
            Assert.That(WorldAssets.LoadTerrainSettings().WaterSettings, Is.SameAs(settings));
        }

        // Done line: rivers (D-080). Seed 42 has at least 8, at least 15 km in all.
        [Test]
        public void Rivers_PlannedForFixtureSeed()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var length = 0f;
            for (var r = 0; r < p.WaterPlan.Rivers.Length; r++)
            {
                var river = p.WaterPlan.Rivers[r];
                Assert.That(river.Count, Is.GreaterThanOrEqualTo(2), $"river {r}");
                for (var n = river.Start; n + 1 < river.Start + river.Count; n++)
                {
                    length += math.distance(p.WaterPlan.Nodes[n].Pos, p.WaterPlan.Nodes[n + 1].Pos);
                }
            }

            Assert.That(p.WaterPlan.Rivers.Length, Is.GreaterThanOrEqualTo(8));
            Assert.That(length * WorldConstants.VoxelSize, Is.GreaterThanOrEqualTo(15000f));
        }

        // Width grows with the water drained: never narrower downstream, the mouth at least
        // as wide as the source, and every node within the settings' bounds.
        [Test]
        public void Rivers_WidenDownstream()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            for (var r = 0; r < p.WaterPlan.Rivers.Length; r++)
            {
                var river = p.WaterPlan.Rivers[r];
                for (var n = river.Start; n < river.Start + river.Count; n++)
                {
                    var node = p.WaterPlan.Nodes[n];
                    Assert.That(
                        node.HalfWidth,
                        Is.InRange(p.MinHalfWidth - 1e-3f, p.MaxHalfWidth + 1e-3f),
                        $"river {r}"
                    );
                    Assert.That(
                        node.Depth,
                        Is.InRange(p.MinDepth - 1e-3f, p.MaxDepth + 1e-3f),
                        $"river {r}"
                    );
                    if (n > river.Start)
                    {
                        Assert.That(
                            node.HalfWidth,
                            Is.GreaterThanOrEqualTo(p.WaterPlan.Nodes[n - 1].HalfWidth - 1e-3f),
                            $"river {r} node {n - river.Start}"
                        );
                    }
                }
            }
        }

        // Review Focus 3: no kinks. Consecutive segments turn by at most 35°, except at the
        // nodes of a fall and at a river's last node (a confluence or a mouth).
        [Test]
        public void Rivers_AreSmooth()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var turns = 0;
            for (var r = 0; r < p.WaterPlan.Rivers.Length; r++)
            {
                var river = p.WaterPlan.Rivers[r];
                for (var n = river.Start + 1; n + 2 < river.Start + river.Count; n++)
                {
                    var a = p.WaterPlan.Nodes[n - 1];
                    var b = p.WaterPlan.Nodes[n];
                    var c = p.WaterPlan.Nodes[n + 1];
                    if (a.Level - b.Level >= p.FallMin || b.Level - c.Level >= p.FallMin)
                    {
                        continue;
                    }

                    var u = b.Pos - a.Pos;
                    var v = c.Pos - b.Pos;
                    if (math.lengthsq(u) < 1f || math.lengthsq(v) < 1f)
                    {
                        continue;
                    }

                    var cos = math.dot(math.normalize(u), math.normalize(v));
                    var degrees = math.degrees(math.acos(math.clamp(cos, -1f, 1f)));
                    Assert.That(
                        degrees,
                        Is.LessThanOrEqualTo(35f),
                        $"river {r} node {n - river.Start} at {b.Pos}"
                    );
                    turns++;
                }
            }

            Assert.That(turns, Is.GreaterThan(100));
        }

        // Review Focus 2 (the 2026-11-23 screenshot): rivers lie in their valleys, not on
        // dikes. Dry columns within 40 m of a river stand above the shaped land by more than
        // a voxel on under 0.2 % of the samples and never by more than 8 voxels, away from
        // falls and river ends (24 m).
        [Test]
        public void Rivers_FollowValleyFloors()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var plan = p.WaterPlan;
            var v = WorldConstants.VoxelsPerMeter;
            var excluded = new System.Collections.Generic.List<float2>();
            for (var r = 0; r < plan.Rivers.Length; r++)
            {
                var river = plan.Rivers[r];
                excluded.Add(plan.Nodes[river.Start].Pos);
                excluded.Add(plan.Nodes[river.Start + river.Count - 1].Pos);
                for (var n = river.Start; n + 1 < river.Start + river.Count; n++)
                {
                    if (plan.Nodes[n].Level - plan.Nodes[n + 1].Level >= p.FallMin)
                    {
                        excluded.Add(plan.Nodes[n].Pos);
                        excluded.Add(plan.Nodes[n + 1].Pos);
                    }
                }
            }

            var dry = 0;
            var raised = 0;
            var highest = 0;
            for (var r = 0; r < plan.Rivers.Length; r++)
            {
                var river = plan.Rivers[r];
                for (var n = river.Start; n + 1 < river.Start + river.Count; n += 2)
                {
                    var a = plan.Nodes[n].Pos;
                    var dir = math.normalizesafe(plan.Nodes[n + 1].Pos - a);
                    var side = new float2(-dir.y, dir.x);
                    if (dir.Equals(float2.zero) || Near(excluded, a, 24f * v + 40f * v))
                    {
                        continue;
                    }

                    for (var k = -40; k <= 40; k++)
                    {
                        var q = (int2)math.floor(a + side * (k * v));
                        var column = WaterMap.Column(p, q.x, q.y);
                        var shaped = TerrainGenerator.ShapedLandHeight(p, q.x, q.y);
                        if (column.IsWet || shaped < p.SeaLevel)
                        {
                            continue;
                        }

                        dry++;
                        var up = column.Ground - (int)math.floor(shaped);
                        raised += up > 1 ? 1 : 0;
                        highest = math.max(highest, up);
                    }
                }
            }

            Assert.That(dry, Is.GreaterThan(10000), "dry samples");
            Assert.That(
                (float)raised / dry,
                Is.LessThan(0.002f),
                $"raised share ({raised} of {dry})"
            );
            Assert.That(highest, Is.LessThanOrEqualTo(8), "highest raise, voxels");
        }

        // D-080: every lake drains. One river starts in each lake, at its level, at least
        // the minimum half width.
        [Test]
        public void Lakes_AllHaveAnOutlet()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var plan = p.WaterPlan;
            Assert.That(plan.Lakes.Length, Is.GreaterThan(0));
            for (var k = 0; k < plan.Lakes.Length; k++)
            {
                var outlets = 0;
                for (var r = 0; r < plan.Rivers.Length; r++)
                {
                    var source = plan.Nodes[plan.Rivers[r].Start];
                    var cell = HydroGrid.CellOf(source.Pos);
                    if (
                        plan.LakeOf[HydroGrid.Index(cell)] == k + 1
                        && math.abs(source.Level - plan.Lakes[k].Level) < LevelEpsilon
                    )
                    {
                        Assert.That(
                            source.HalfWidth,
                            Is.GreaterThanOrEqualTo(p.MinHalfWidth - 1e-3f),
                            $"lake {k}"
                        );
                        outlets++;
                    }
                }

                Assert.That(outlets, Is.EqualTo(1), $"outlets of lake {k}");
            }
        }

        // Review Focus 5: mouths meet the sea. A river ending at sea level ends where the
        // shaped land is below the sea, and along its last segment the columns are wet or at
        // most 1 m above the sea.
        [Test]
        public void Rivers_MouthsMeetTheSea()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var plan = p.WaterPlan;
            var mouths = 0;
            for (var r = 0; r < plan.Rivers.Length; r++)
            {
                var river = plan.Rivers[r];
                var last = plan.Nodes[river.Start + river.Count - 1];
                if (math.abs(last.Level - p.SeaLevel) >= LevelEpsilon || river.Count < 2)
                {
                    continue;
                }

                // A tributary joining an estuary at sea level ends on that river, not at sea.
                var joins = false;
                for (var n = 0; n < river.Start && !joins; n++)
                {
                    joins = math.distancesq(plan.Nodes[n].Pos, last.Pos) < LevelEpsilon;
                }

                if (joins)
                {
                    continue;
                }

                mouths++;
                var at = (int2)math.floor(last.Pos);
                Assert.That(
                    TerrainGenerator.ShapedLandHeight(p, at.x, at.y),
                    Is.LessThan(p.SeaLevel),
                    $"river {r} mouth"
                );
                var before = plan.Nodes[river.Start + river.Count - 2].Pos;
                for (var s = 0; s <= 8; s++)
                {
                    var q = (int2)math.floor(math.lerp(before, last.Pos, s / 8f));
                    var column = WaterMap.Column(p, q.x, q.y);
                    Assert.That(
                        column.IsWet || column.Ground <= p.SeaLevel + WorldConstants.VoxelsPerMeter,
                        Is.True,
                        $"river {r} at {q}"
                    );
                }
            }

            Assert.That(mouths, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void Rivers_LevelsNeverRiseDownstream()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            for (var r = 0; r < p.WaterPlan.Rivers.Length; r++)
            {
                var river = p.WaterPlan.Rivers[r];
                for (var n = river.Start; n < river.Start + river.Count; n++)
                {
                    var node = p.WaterPlan.Nodes[n];
                    Assert.That(node.Level, Is.GreaterThanOrEqualTo(p.SeaLevel), $"river {r}");
                    if (n > river.Start)
                    {
                        Assert.That(
                            node.Level,
                            Is.LessThanOrEqualTo(p.WaterPlan.Nodes[n - 1].Level + LevelEpsilon),
                            $"river {r} node {n - river.Start}"
                        );
                    }
                }
            }
        }

        // Each river ends at the sea, in a lake (at its level) or on an earlier river (at
        // that river's level there); several reach the sea themselves.
        [Test]
        public void Rivers_EndAtTheSeaALakeOrAnotherRiver()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var atSea = 0;
            for (var r = 0; r < p.WaterPlan.Rivers.Length; r++)
            {
                var river = p.WaterPlan.Rivers[r];
                var mouth = p.WaterPlan.Nodes[river.Start + river.Count - 1];
                if (math.abs(mouth.Level - p.SeaLevel) < LevelEpsilon)
                {
                    atSea++;
                    continue;
                }

                var joins = false;
                for (var i = 0; i < p.WaterPlan.Lakes.Length && !joins; i++)
                {
                    joins = math.abs(mouth.Level - p.WaterPlan.Lakes[i].Level) < LevelEpsilon;
                }

                for (var n = 0; n < p.WaterPlan.Rivers[r].Start && !joins; n++)
                {
                    var node = p.WaterPlan.Nodes[n];
                    joins =
                        math.distance(node.Pos, mouth.Pos) < LevelEpsilon
                        && math.abs(node.Level - mouth.Level) < LevelEpsilon;
                }

                Assert.That(joins, Is.True, $"river {r} ends at {mouth.Pos} in nothing");
            }

            Assert.That(atSea, Is.GreaterThanOrEqualTo(3));
        }

        // Done line: rivers carve canyons (D-079): somewhere a river runs at least 10 m
        // below the raw land, in the valley erosion made, with river water there.
        [Test]
        public void Rivers_RunInValleys()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var deepest = 0f;
            var wetInValley = 0;
            for (var n = 0; n < p.WaterPlan.Nodes.Length; n++)
            {
                var node = p.WaterPlan.Nodes[n];
                var x = (int)math.floor(node.Pos.x);
                var z = (int)math.floor(node.Pos.y);
                var cut = TerrainGenerator.LandHeight(p, x, z, out _) - node.Level;
                deepest = math.max(deepest, cut);
                if (
                    cut >= 10f * WorldConstants.VoxelsPerMeter
                    && wetInValley == 0
                    && WetWithin(p, x, z, (int)node.HalfWidth, WaterBody.River)
                )
                {
                    wetInValley++;
                }
            }

            Assert.That(deepest, Is.GreaterThan(10f * WorldConstants.VoxelsPerMeter));
            Assert.That(wetInValley, Is.GreaterThan(0));
        }

        // Done line: waterfalls. Drops of at least FallMin between nodes, and the water
        // level at the column above a fall's top and below its foot differs by about that.
        [Test]
        public void Rivers_FallFromCliffs()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var falls = 0;
            for (var n = 0; n + 1 < p.WaterPlan.Nodes.Length; n++)
            {
                var top = p.WaterPlan.Nodes[n];
                var foot = p.WaterPlan.Nodes[n + 1];
                if (
                    top.Level - foot.Level >= p.FallMin
                    && math.distance(top.Pos, foot.Pos) < p.RiverStep * 0.5f
                )
                {
                    falls++;
                }
            }

            Assert.That(falls, Is.GreaterThanOrEqualTo(10));
        }

        // D-081: lakes fill real basins. Seed 42 has at least three; every lake cell lies
        // below the level (plus the margin and one level step), every cell beside the lake
        // stands at or above it, and the level sits on the 1 m step.
        [Test]
        public void Lakes_FillRealBasins()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var plan = p.WaterPlan;
            Assert.That(plan.Lakes.Length, Is.GreaterThanOrEqualTo(3));
            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                var k = plan.LakeOf[i] - 1;
                if (k < 0)
                {
                    continue;
                }

                var lake = plan.Lakes[k];
                var cell = HydroGrid.Cell(i);
                Assert.That(
                    Surface(p, cell),
                    Is.LessThan(lake.Level + p.LakeMargin + p.LakeLevelStep),
                    $"lake {k} cell {cell}"
                );
                for (var n = 0; n < 4; n++)
                {
                    var c = cell + DepressionFill.Neighbor(n);
                    if (HydroGrid.InGrid(c) && plan.LakeOf[HydroGrid.Index(c)] == 0)
                    {
                        Assert.That(
                            Surface(p, c),
                            Is.GreaterThanOrEqualTo(lake.Level - 1e-3f),
                            $"rim of lake {k} at {c}"
                        );
                    }
                }
            }

            for (var k = 0; k < plan.Lakes.Length; k++)
            {
                var lake = plan.Lakes[k];
                Assert.That(lake.Level % p.LakeLevelStep, Is.EqualTo(0f));
                Assert.That(lake.Level, Is.GreaterThan(p.SeaLevel));
                // The outlet may enter a lower lake, never this one.
                Assert.That(plan.LakeOf[lake.Spill], Is.Not.EqualTo(k + 1), $"outlet of lake {k}");
            }
        }

        // The deepest cell of every lake holds lake water at least 1 m deep.
        [Test]
        public void Lakes_HaveWaterAtTheirDeepestCell()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            for (var k = 0; k < p.WaterPlan.Lakes.Length; k++)
            {
                var lake = p.WaterPlan.Lakes[k];
                var at = (int2)math.floor(lake.DeepestPoint);
                var column = WaterMap.Column(p, at.x, at.y);
                Assert.That(column.Body, Is.EqualTo(WaterBody.Lake), $"lake {k}");
                Assert.That(column.Level, Is.EqualTo((int)lake.Level), $"lake {k}");
                Assert.That(column.DepthMeters, Is.GreaterThanOrEqualTo(1f), $"lake {k}");
            }
        }

        // Review Focus 1: no seed drowns its interior. Seeds 42 to 46: lakes cover at most
        // 8 % of the land cells, and every seed has a lake.
        [Test]
        public void Lakes_StayAReasonableShare_SeveralSeeds()
        {
            var settings = WorldAssets.LoadTerrainSettings();
            for (var seed = 42; seed <= 46; seed++)
            {
                var p = TerrainParams.Create(new WorldSeed(seed), settings);
                try
                {
                    var land = 0;
                    var lake = 0;
                    for (var i = 0; i < HydroGrid.CellCount; i++)
                    {
                        var center = (int2)math.floor(HydroGrid.Center(HydroGrid.Cell(i)));
                        if (TerrainGenerator.LandHeight(p, center.x, center.y, out _) >= p.SeaLevel)
                        {
                            land++;
                            lake += p.WaterPlan.LakeOf[i] > 0 ? 1 : 0;
                        }
                    }

                    Assert.That(
                        p.WaterPlan.Lakes.Length,
                        Is.GreaterThanOrEqualTo(1),
                        $"seed {seed}"
                    );
                    Assert.That((float)lake / land, Is.LessThanOrEqualTo(0.08f), $"seed {seed}");
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        // The shaped land at a cell's center.
        private static float Surface(in TerrainParams p, int2 cell)
        {
            var center = (int2)math.floor(HydroGrid.Center(cell));
            return TerrainGenerator.ShapedLandHeight(p, center.x, center.y);
        }

        [Test]
        public void Water_PlanIsDeterministic()
        {
            // Fresh builds: the shared cache would compare a plan with itself.
            var settings = WorldAssets.LoadTerrainSettings();
            var a = TerrainParams.Create(new WorldSeed(Seeds.Fixture), settings);
            var b = TerrainParams.Create(new WorldSeed(Seeds.Fixture), settings);
            var other = TerrainParams.Create(new WorldSeed(Seeds.Other), settings);
            try
            {
                Assert.That(b.WaterPlan.Nodes.Length, Is.EqualTo(a.WaterPlan.Nodes.Length));
                Assert.That(b.WaterPlan.Lakes.Length, Is.EqualTo(a.WaterPlan.Lakes.Length));
                for (var n = 0; n < a.WaterPlan.Nodes.Length; n++)
                {
                    Assert.That(b.WaterPlan.Nodes[n].Pos, Is.EqualTo(a.WaterPlan.Nodes[n].Pos));
                    Assert.That(b.WaterPlan.Nodes[n].Level, Is.EqualTo(a.WaterPlan.Nodes[n].Level));
                }

                var differs = other.WaterPlan.Nodes.Length != a.WaterPlan.Nodes.Length;
                for (var n = 0; !differs && n < a.WaterPlan.Nodes.Length; n++)
                {
                    differs = !other.WaterPlan.Nodes[n].Pos.Equals(a.WaterPlan.Nodes[n].Pos);
                }

                Assert.That(differs, Is.True);
            }
            finally
            {
                a.Dispose();
                b.Dispose();
                other.Dispose();
            }
        }

        private static bool Near(
            System.Collections.Generic.List<float2> points,
            float2 q,
            float distance
        )
        {
            foreach (var point in points)
            {
                if (math.distancesq(point, q) < distance * distance)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WetWithin(in TerrainParams p, int x, int z, int radius, WaterBody body)
        {
            for (var dz = -radius; dz <= radius; dz += 2)
            for (var dx = -radius; dx <= radius; dx += 2)
            {
                var column = WaterMap.Column(p, x + dx, z + dz);
                if (column.IsWet && column.Body == body)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
