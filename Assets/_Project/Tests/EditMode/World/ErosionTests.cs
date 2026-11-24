using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    // Light erosion on the hydrology grid and its interpolation to voxels (D-079).
    public sealed class ErosionTests
    {
        [Test]
        public void Erosion_OnlyLowersBoundedAndKeepsTheSea()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            using var grid = SampledGrid.Create(Seeds.Fixture);
            using var eroded = new NativeArray<float>(grid.Land, Allocator.TempJob);
            Erosion.Run(p, grid.Ocean, grid.Rain, eroded);

            var deepest = 0f;
            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                if (grid.Ocean[i])
                {
                    Assert.That(
                        eroded[i],
                        Is.EqualTo(grid.Land[i]),
                        $"sea cell {HydroGrid.Cell(i)}"
                    );
                    continue;
                }

                Assert.That(
                    eroded[i],
                    Is.LessThanOrEqualTo(grid.Land[i] + 1e-3f),
                    $"{HydroGrid.Cell(i)}"
                );
                Assert.That(
                    eroded[i],
                    Is.GreaterThanOrEqualTo(grid.Land[i] - p.MaxErosion - 1e-3f)
                );
                deepest = math.max(deepest, grid.Land[i] - eroded[i]);
            }

            Assert.That(deepest, Is.GreaterThanOrEqualTo(8f * WorldConstants.VoxelsPerMeter));
        }

        // Valleys form where water collects: cells draining at least 1 km² are cut at least
        // three times as deep, on average, as cells draining under 0.1 km².
        [Test]
        public void Erosion_CarvesValleysAlongFlow()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            using var grid = SampledGrid.Create(Seeds.Fixture);
            using var eroded = new NativeArray<float>(grid.Land, Allocator.TempJob);
            Erosion.Run(p, grid.Ocean, grid.Rain, eroded);
            using var filled = new NativeArray<float>(HydroGrid.CellCount, Allocator.TempJob);
            using var receiver = new NativeArray<int>(HydroGrid.CellCount, Allocator.TempJob);
            using var order = new NativeArray<int>(HydroGrid.CellCount, Allocator.TempJob);
            using var area = new NativeArray<float>(HydroGrid.CellCount, Allocator.TempJob);
            DepressionFill.Fill(eroded, grid.Ocean, DepressionFill.Epsilon, filled);
            FlowRouting.Receivers(filled, grid.Ocean, receiver);
            FlowRouting.Order(filled, order);
            FlowRouting.Accumulate(order, receiver, grid.Rain, area);

            double big = 0,
                small = 0;
            int bigCount = 0,
                smallCount = 0;
            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                if (grid.Ocean[i])
                {
                    continue;
                }

                var cut = grid.Land[i] - eroded[i];
                if (area[i] >= 1f)
                {
                    big += cut;
                    bigCount++;
                }
                else if (area[i] < 0.1f)
                {
                    small += cut;
                    smallCount++;
                }
            }

            Assert.That(bigCount, Is.GreaterThan(50));
            Assert.That(big / bigCount, Is.GreaterThanOrEqualTo(3.0 * small / smallCount));
        }

        // Neighbor columns differ by under a voxel, and cell centers give the cell's value:
        // no facets or creases at 32 m.
        [Test]
        public void ErosionDelta_InterpolatesSmoothly()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var random = new Random(7);
            for (var t = 0; t < 200; t++)
            {
                var start = random.NextFloat2(-20000f, 20000f);
                var dir = random.NextFloat2Direction();
                var previous = p.WaterPlan.DeltaAt(start);
                for (var k = 1; k < 256; k++)
                {
                    var here = p.WaterPlan.DeltaAt(start + dir * k);
                    Assert.That(
                        math.abs(here - previous),
                        Is.LessThan(1f),
                        $"transect {t}, step {k}"
                    );
                    previous = here;
                }
            }

            foreach (var cell in new[] { new int2(125, 125), new int2(80, 160), new int2(170, 90) })
            {
                var i = HydroGrid.Index(cell);
                Assert.That(
                    p.WaterPlan.DeltaAt(HydroGrid.Center(cell)),
                    Is.EqualTo(p.WaterPlan.Delta[i]).Within(1e-3f)
                );
            }
        }

        [Test]
        public void Erosion_IsDeterministic()
        {
            var settings = WorldAssets.LoadTerrainSettings();
            var a = TerrainParams.Create(new WorldSeed(Seeds.Fixture), settings);
            var b = TerrainParams.Create(new WorldSeed(Seeds.Fixture), settings);
            var other = TerrainParams.Create(new WorldSeed(Seeds.Other), settings);
            try
            {
                Assert.That(b.WaterPlan.Delta.Length, Is.EqualTo(HydroGrid.CellCount));
                var differs = false;
                for (var i = 0; i < HydroGrid.CellCount; i++)
                {
                    Assert.That(b.WaterPlan.Delta[i], Is.EqualTo(a.WaterPlan.Delta[i]));
                    differs |= other.WaterPlan.Delta[i] != a.WaterPlan.Delta[i];
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
    }
}
