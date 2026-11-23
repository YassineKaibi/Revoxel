using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Revoxel.World;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    public sealed class SubsurfaceTests
    {
        [Test]
        public void Subsurface_SoilVariesAndThinsOnSlopes()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var depths = new HashSet<int>();
            long steepSum = 0,
                steepCount = 0,
                flatSum = 0,
                flatCount = 0;
            foreach (var c in LandColumns(p, 100))
            {
                var column = TerrainGenerator.Column(p, c.x, c.y);
                if (column.Surface <= p.SeaLevel + p.ShoreHeight)
                {
                    continue;
                }

                depths.Add(column.Soil);
                var h = column.Surface;
                var rise = math.max(
                    math.max(
                        math.abs(TerrainGenerator.SurfaceHeight(p, c.x + 1, c.y) - h),
                        math.abs(TerrainGenerator.SurfaceHeight(p, c.x - 1, c.y) - h)
                    ),
                    math.max(
                        math.abs(TerrainGenerator.SurfaceHeight(p, c.x, c.y + 1) - h),
                        math.abs(TerrainGenerator.SurfaceHeight(p, c.x, c.y - 1) - h)
                    )
                );
                if (rise >= 2)
                {
                    steepSum += column.Soil;
                    steepCount++;
                }
                else if (rise == 0)
                {
                    flatSum += column.Soil;
                    flatCount++;
                }
            }

            Assert.That(depths.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(steepCount, Is.GreaterThan(10), "too few steep columns sampled");
            Assert.That(flatCount, Is.GreaterThan(10), "too few flat columns sampled");
            Assert.That((double)steepSum / steepCount, Is.LessThan((double)flatSum / flatCount));
        }

        [Test]
        public void Subsurface_StrataAreFoldedNotFlat()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var minFold = float.MaxValue;
            var maxFold = float.MinValue;
            var thickest = 0f;
            var tilted = 0;
            var pairs = 0;
            foreach (var c in LandColumns(p, 400))
            {
                var a = TerrainGenerator.Column(p, c.x, c.y);
                var b = TerrainGenerator.Column(p, c.x + 32, c.y);
                minFold = math.min(minFold, a.Fold);
                maxFold = math.max(maxFold, a.Fold);
                thickest = math.max(thickest, a.StrataThickness);
                pairs++;
                tilted += math.abs(a.Fold - b.Fold) > 0.5f ? 1 : 0;
            }

            Assert.That(maxFold - minFold, Is.GreaterThan(4f * thickest), "strata barely fold");
            Assert.That(tilted, Is.GreaterThan(pairs * 8 / 10), "strata are flat over 4 m");
        }

        // Done line: no two areas repeat the same layering. A column's layering is the list
        // of material runs from its surface down through the subsurface band; land columns
        // 300 m apart over the whole island are compared.
        [Test]
        public void Subsurface_NoTwoAreasRepeatTheLayering()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var full = new HashSet<string>();
            var strata = new HashSet<string>();
            var count = 0;
            foreach (var c in LandColumns(p, 300))
            {
                var column = TerrainGenerator.Column(p, c.x, c.y);
                full.Add(Layering(p, column, 0));
                strata.Add(Layering(p, column, column.Soil));
                count++;
            }

            Assert.That(count, Is.GreaterThanOrEqualTo(200), "too few land columns");
            Assert.That(full.Count, Is.EqualTo(count), "two areas repeat the same layering");
            Assert.That(strata.Count, Is.GreaterThanOrEqualTo(count * 9 / 10), "strata repeat");
        }

        [Test]
        public void Subsurface_WastePocketsAreBuriedInTheBand()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var waste = WorldAssets
                .LoadTerrainSettings()
                .PocketTypes.Select(t => t.Id.Value)
                .ToArray();
            var columnsPerBiome = new int[p.Biomes.Length];
            var pocketsPerBiome = new int[p.Biomes.Length];
            foreach (var c in LandColumns(p, 40))
            {
                var column = TerrainGenerator.Column(p, c.x, c.y);
                columnsPerBiome[column.Biome.Value]++;
                if (column.Pockets.Length == 0)
                {
                    continue;
                }

                pocketsPerBiome[column.Biome.Value]++;
                for (var i = 0; i < column.Pockets.Length; i++)
                {
                    var pocket = column.Pockets[i];
                    Assert.That(pocket.Min, Is.LessThan(pocket.Max));
                    Assert.That(
                        pocket.Min,
                        Is.GreaterThanOrEqualTo(column.Surface - p.SubsurfaceDepth)
                    );
                    Assert.That(
                        pocket.Max - 1,
                        Is.LessThanOrEqualTo(column.Surface - 3),
                        "pocket near the surface"
                    );
                    Assert.That(waste, Does.Contain(pocket.Id));
                    var deepest = pocket.Max - 1;
                    if (column.Surface - 1 - deepest >= column.Soil)
                    {
                        Assert.That(
                            TerrainGenerator.MaterialAt(p, column, deepest),
                            Is.EqualTo(pocket.Id)
                        );
                    }
                }
            }

            var wasteLands = BiomeIndex("biome.waste_lands.name");
            var plains = BiomeIndex("biome.plains.name");
            Assert.That(pocketsPerBiome.Sum(), Is.GreaterThan(0), "no pocket found");
            Assert.That(columnsPerBiome[wasteLands], Is.GreaterThan(50));
            Assert.That(columnsPerBiome[plains], Is.GreaterThan(50));
            Assert.That(
                (double)pocketsPerBiome[wasteLands] / columnsPerBiome[wasteLands],
                Is.GreaterThan((double)pocketsPerBiome[plains] / columnsPerBiome[plains])
            );
        }

        [Test]
        public void Subsurface_WasteLandsAreLayeredTrash()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var waste = new HashSet<ushort>(
                WorldAssets.LoadTerrainSettings().PocketTypes.Select(t => t.Id.Value)
            );
            var wasteLands = BiomeIndex("biome.waste_lands.name");
            var columns = 0;
            var layered = 0;
            foreach (var c in LandColumns(p, 40))
            {
                var column = TerrainGenerator.Column(p, c.x, c.y);
                if (column.Biome.Value != wasteLands)
                {
                    continue;
                }

                var found = new HashSet<ushort>();
                for (var depth = column.Soil; depth < p.SubsurfaceDepth; depth++)
                {
                    var id = TerrainGenerator.MaterialAt(p, column, column.Surface - 1 - depth);
                    if (waste.Contains(id))
                    {
                        found.Add(id);
                    }
                }

                columns++;
                layered += found.Count >= 3 ? 1 : 0;
            }

            Assert.That(columns, Is.GreaterThan(50));
            Assert.That(
                layered,
                Is.GreaterThanOrEqualTo(columns * 8 / 10),
                $"{layered} of {columns}"
            );
        }

        [Test]
        public void Subsurface_BelowTheBandIsStone()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var checkedColumns = 0;
            foreach (var c in LandColumns(p, 300))
            {
                var column = TerrainGenerator.Column(p, c.x, c.y);
                var bottom = column.Surface - p.SubsurfaceDepth;
                for (var y = math.max(0, bottom - 40); y < bottom; y++)
                {
                    Assert.That(
                        TerrainGenerator.MaterialAt(p, column, y),
                        Is.EqualTo(p.Stone),
                        $"({c.x}, {y}, {c.y})"
                    );
                }

                checkedColumns++;
            }

            Assert.That(checkedColumns, Is.GreaterThan(100));
        }

        // Material runs from depth `from` to the bottom of the band, as "id x count" items.
        private static string Layering(in TerrainParams p, in TerrainColumn column, int from)
        {
            var text = new StringBuilder();
            var run = -1;
            var length = 0;
            for (var depth = from; depth < p.SubsurfaceDepth; depth++)
            {
                int id = TerrainGenerator.MaterialAt(p, column, column.Surface - 1 - depth);
                if (id != run && length > 0)
                {
                    text.Append(run).Append('x').Append(length).Append(' ');
                    length = 0;
                }

                run = id;
                length++;
            }

            return text.Append(run).Append('x').Append(length).ToString();
        }

        private static int BiomeIndex(string nameKey) =>
            System.Array.FindIndex(WorldAssets.LoadBiomeRegistry().Biomes, b =>
                b.NameKey == nameKey
            );

        // Land columns (surface above sea level) on a grid of the given spacing, meters,
        // over the whole island, in row order.
        private static IEnumerable<int2> LandColumns(TerrainParams p, int spacingMeters)
        {
            var step = spacingMeters * WorldConstants.VoxelsPerMeter;
            for (var z = -32000 + step / 2; z < 32000; z += step)
            for (var x = -32000 + step / 2; x < 32000; x += step)
            {
                if (TerrainGenerator.SurfaceHeight(p, x, z) > p.SeaLevel)
                {
                    yield return new int2(x, z);
                }
            }
        }
    }
}
