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

        // Done line: rivers. Several, within the fixed lists, each long enough.
        [Test]
        public void Rivers_PlannedForFixtureSeed()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);

            Assert.That(p.Rivers.Length, Is.GreaterThanOrEqualTo(6));
            Assert.That(p.RiverNodes.Length, Is.LessThanOrEqualTo(WaterSettings.MaxRiverNodes));
            for (var r = 0; r < p.Rivers.Length; r++)
            {
                Assert.That(p.Rivers[r].Count, Is.GreaterThan(p.RiverMinSteps), $"river {r}");
            }
        }

        [Test]
        public void Rivers_LevelsNeverRiseDownstream()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            for (var r = 0; r < p.Rivers.Length; r++)
            {
                var river = p.Rivers[r];
                for (var n = river.Start; n < river.Start + river.Count; n++)
                {
                    var node = p.RiverNodes[n];
                    Assert.That(node.Level, Is.GreaterThanOrEqualTo(p.SeaLevel), $"river {r}");
                    if (n > river.Start)
                    {
                        Assert.That(
                            node.Level,
                            Is.LessThanOrEqualTo(p.RiverNodes[n - 1].Level + LevelEpsilon),
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
            for (var r = 0; r < p.Rivers.Length; r++)
            {
                var river = p.Rivers[r];
                var mouth = p.RiverNodes[river.Start + river.Count - 1];
                if (math.abs(mouth.Level - p.SeaLevel) < LevelEpsilon)
                {
                    atSea++;
                    continue;
                }

                var joins = false;
                for (var i = 0; i < p.Lakes.Length && !joins; i++)
                {
                    joins = math.abs(mouth.Level - p.Lakes[i].Level) < LevelEpsilon;
                }

                for (var n = 0; n < p.Rivers[r].Start && !joins; n++)
                {
                    var node = p.RiverNodes[n];
                    joins =
                        math.distance(node.Pos, mouth.Pos) < LevelEpsilon
                        && math.abs(node.Level - mouth.Level) < LevelEpsilon;
                }

                Assert.That(joins, Is.True, $"river {r} ends at {mouth.Pos} in nothing");
            }

            Assert.That(atSea, Is.GreaterThanOrEqualTo(3));
        }

        // Done line: rivers carve canyons. Somewhere the water runs at least 10 m below the
        // land it crosses, and the channel there holds river water.
        [Test]
        public void Rivers_CarveCanyons()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var deepest = 0f;
            var wetInCanyon = 0;
            for (var n = 0; n < p.RiverNodes.Length; n++)
            {
                var node = p.RiverNodes[n];
                var x = (int)math.floor(node.Pos.x);
                var z = (int)math.floor(node.Pos.y);
                var cut = TerrainGenerator.LandHeight(p, x, z, out _) - node.Level;
                deepest = math.max(deepest, cut);
                if (cut < 10f * WorldConstants.VoxelsPerMeter)
                {
                    continue;
                }

                // The node is on the unwandered path; the channel is within the wander.
                if (
                    wetInCanyon == 0
                    && WetWithin(p, x, z, (int)(p.Meander * 1.5f) + 8, WaterBody.River)
                )
                {
                    wetInCanyon++;
                }
            }

            Assert.That(deepest, Is.GreaterThan(10f * WorldConstants.VoxelsPerMeter));
            Assert.That(wetInCanyon, Is.GreaterThan(0));
        }

        // Done line: waterfalls. Drops of at least FallMin between nodes, and the water
        // level at the column above a fall's top and below its foot differs by about that.
        [Test]
        public void Rivers_FallFromCliffs()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var falls = 0;
            for (var n = 0; n + 1 < p.RiverNodes.Length; n++)
            {
                var top = p.RiverNodes[n];
                var foot = p.RiverNodes[n + 1];
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

        // Done line: lakes. On land, at multiples of the level step above sea level, with
        // lake water at the center.
        [Test]
        public void Lakes_SitOnLandAtLevelSteps()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);

            Assert.That(p.Lakes.Length, Is.GreaterThanOrEqualTo(6));
            for (var i = 0; i < p.Lakes.Length; i++)
            {
                var lake = p.Lakes[i];
                Assert.That(lake.Level % p.LakeLevelStep, Is.EqualTo(0f), $"lake {i}");
                Assert.That(lake.Level, Is.GreaterThan(p.SeaLevel), $"lake {i}");
                var x = (int)math.floor(lake.Center.x);
                var z = (int)math.floor(lake.Center.y);
                var column = WaterMap.Column(p, x, z);
                Assert.That(column.Body, Is.EqualTo(WaterBody.Lake), $"lake {i}");
                Assert.That(column.Level, Is.EqualTo((int)lake.Level), $"lake {i}");
                Assert.That(column.DepthMeters, Is.GreaterThanOrEqualTo(1f), $"lake {i}");
            }
        }

        [Test]
        public void Water_PlanIsDeterministic()
        {
            var a = WorldAssets.TerrainParams(Seeds.Fixture);
            var b = WorldAssets.TerrainParams(Seeds.Fixture);
            var other = WorldAssets.TerrainParams(Seeds.Other);

            Assert.That(b.RiverNodes.Length, Is.EqualTo(a.RiverNodes.Length));
            Assert.That(b.Lakes.Length, Is.EqualTo(a.Lakes.Length));
            for (var n = 0; n < a.RiverNodes.Length; n++)
            {
                Assert.That(b.RiverNodes[n].Pos, Is.EqualTo(a.RiverNodes[n].Pos));
                Assert.That(b.RiverNodes[n].Level, Is.EqualTo(a.RiverNodes[n].Level));
            }

            var differs = other.RiverNodes.Length != a.RiverNodes.Length;
            for (var n = 0; !differs && n < a.RiverNodes.Length; n++)
            {
                differs = !other.RiverNodes[n].Pos.Equals(a.RiverNodes[n].Pos);
            }

            Assert.That(differs, Is.True);
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
