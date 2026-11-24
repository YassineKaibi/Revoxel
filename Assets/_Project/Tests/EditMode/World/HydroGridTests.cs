using NUnit.Framework;
using Revoxel.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    // The coarse hydrology grid: geometry, depression filling, flow routing and
    // accumulation (D-077, plan Task 2).
    public sealed class HydroGridTests
    {
        [Test]
        public void HydroGrid_CoversTheIslandAtCellSize()
        {
            Assert.That(HydroGrid.Size, Is.EqualTo(250));
            Assert.That(HydroGrid.CellVoxels, Is.EqualTo(256));
            Assert.That(HydroGrid.CellOf(new float2(-32000f, -32000f)), Is.EqualTo(new int2(0, 0)));
            Assert.That(
                HydroGrid.CellOf(new float2(31999f, 31999f)),
                Is.EqualTo(new int2(249, 249))
            );
            var cell = new int2(17, 203);
            Assert.That(HydroGrid.CellOf(HydroGrid.Center(cell)), Is.EqualTo(cell));
            Assert.That(HydroGrid.Cell(HydroGrid.Index(cell)), Is.EqualTo(cell));
            Assert.That(HydroGrid.InGrid(new int2(-1, 0)), Is.False);
            Assert.That(HydroGrid.InGrid(new int2(249, 249)), Is.True);
        }

        // Seed 42: from every land cell, following receivers reaches the sea, the filled
        // surface strictly falling on the way.
        [Test]
        public void DepressionFill_EveryLandCellDrainsToTheSea()
        {
            using var grid = SampledGrid.Create(Seeds.Fixture);
            using var filled = new NativeArray<float>(HydroGrid.CellCount, Allocator.TempJob);
            using var receiver = new NativeArray<int>(HydroGrid.CellCount, Allocator.TempJob);
            DepressionFill.Fill(grid.Land, grid.Ocean, DepressionFill.Epsilon, filled);
            FlowRouting.Receivers(filled, grid.Ocean, receiver);

            var land = 0;
            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                if (grid.Ocean[i])
                {
                    continue;
                }

                land++;
                var at = i;
                for (var steps = 0; !grid.Ocean[at]; steps++)
                {
                    var next = receiver[at];
                    Assert.That(
                        next,
                        Is.GreaterThanOrEqualTo(0),
                        $"cell {HydroGrid.Cell(at)} has nowhere to go"
                    );
                    Assert.That(
                        filled[next],
                        Is.LessThan(filled[at]),
                        $"cell {HydroGrid.Cell(at)}"
                    );
                    Assert.That(steps, Is.LessThan(HydroGrid.CellCount));
                    at = next;
                }
            }

            Assert.That(land, Is.GreaterThan(10000));
        }

        // A hand-built slope with one pit: only the pit rises, to the spill height plus a
        // few epsilons.
        [Test]
        public void DepressionFill_LeavesDrainingCellsAlone()
        {
            var height = new NativeArray<float>(HydroGrid.CellCount, Allocator.TempJob);
            var ocean = new NativeArray<bool>(HydroGrid.CellCount, Allocator.TempJob);
            var filled = new NativeArray<float>(HydroGrid.CellCount, Allocator.TempJob);
            try
            {
                for (var i = 0; i < HydroGrid.CellCount; i++)
                {
                    var c = HydroGrid.Cell(i);
                    height[i] = 1000f + 10f * c.x;
                    ocean[i] = c.x == 0;
                }

                var pit = new[] { new int2(100, 100), new int2(101, 100), new int2(100, 101) };
                foreach (var c in pit)
                {
                    height[HydroGrid.Index(c)] = 900f;
                }

                DepressionFill.Fill(height, ocean, DepressionFill.Epsilon, filled);

                for (var i = 0; i < HydroGrid.CellCount; i++)
                {
                    var c = HydroGrid.Cell(i);
                    var isPit = System.Array.IndexOf(pit, c) >= 0;
                    if (isPit)
                    {
                        // The lowest rim of the pit is the cell west of it, at x = 99.
                        Assert.That(
                            filled[i],
                            Is.GreaterThan(1990f)
                                .And.LessThan(1990f + 10f * DepressionFill.Epsilon),
                            $"{c}"
                        );
                    }
                    else
                    {
                        Assert.That(filled[i], Is.EqualTo(height[i]), $"{c}");
                    }
                }
            }
            finally
            {
                height.Dispose();
                ocean.Dispose();
                filled.Dispose();
            }
        }

        // Every land cell passes its water on: a receiver holds at least what flows into it
        // plus its own rain, and the sea receives all the rain that fell on land.
        [Test]
        public void Accumulation_GrowsDownstreamAndConserves()
        {
            using var grid = SampledGrid.Create(Seeds.Fixture);
            using var filled = new NativeArray<float>(HydroGrid.CellCount, Allocator.TempJob);
            using var receiver = new NativeArray<int>(HydroGrid.CellCount, Allocator.TempJob);
            using var order = new NativeArray<int>(HydroGrid.CellCount, Allocator.TempJob);
            using var area = new NativeArray<float>(HydroGrid.CellCount, Allocator.TempJob);
            DepressionFill.Fill(grid.Land, grid.Ocean, DepressionFill.Epsilon, filled);
            FlowRouting.Receivers(filled, grid.Ocean, receiver);
            FlowRouting.Order(filled, order);
            FlowRouting.Accumulate(order, receiver, grid.Rain, area);

            double landRain = 0;
            double atSea = 0;
            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                if (grid.Ocean[i])
                {
                    atSea += area[i];
                    continue;
                }

                landRain += grid.Rain[i];
                var r = receiver[i];
                Assert.That(
                    area[r],
                    Is.GreaterThanOrEqualTo(area[i] + (grid.Ocean[r] ? 0f : grid.Rain[r]) - 1e-4f)
                );
            }

            Assert.That(landRain, Is.GreaterThan(0));
            Assert.That(atSea, Is.EqualTo(landRain).Within(landRain * 0.001));
        }

        // A parallel Burst sample equals a managed one, cell for cell, on a few cells.
        [Test]
        public void HydroSample_ParallelMatchesManaged()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            using var grid = SampledGrid.Create(Seeds.Fixture);
            foreach (var cell in new[] { new int2(125, 125), new int2(60, 190), new int2(3, 3) })
            {
                var i = HydroGrid.Index(cell);
                var center = (int2)math.floor(HydroGrid.Center(cell));
                var moisture = TerrainGenerator.LandMoisture(p, center.x, center.y, out var land);
                Assert.That(grid.Land[i], Is.EqualTo(land).Within(0.5f), $"{cell}");
                Assert.That(grid.Rain[i] > 0f, Is.EqualTo(!grid.Ocean[i]), $"{cell}");
                Assert.That(moisture, Is.InRange(0f, 1f));
            }
        }
    }

    // The fixture seed's grid, sampled by the parallel job.
    internal struct SampledGrid : System.IDisposable
    {
        public NativeArray<float> Land;
        public NativeArray<float> Rain;
        public NativeArray<bool> Ocean;

        public static SampledGrid Create(int seed)
        {
            var grid = new SampledGrid
            {
                Land = new NativeArray<float>(HydroGrid.CellCount, Allocator.TempJob),
                Rain = new NativeArray<float>(HydroGrid.CellCount, Allocator.TempJob),
                Ocean = new NativeArray<bool>(HydroGrid.CellCount, Allocator.TempJob),
            };
            new HydroSampleJob
            {
                Params = WorldAssets.TerrainParams(seed),
                Land = grid.Land,
                Rain = grid.Rain,
                Ocean = grid.Ocean,
            }
                .Schedule(HydroGrid.CellCount, HydroSampleJob.BatchSize)
                .Complete();
            return grid;
        }

        public void Dispose()
        {
            Land.Dispose();
            Rain.Dispose();
            Ocean.Dispose();
        }
    }
}
