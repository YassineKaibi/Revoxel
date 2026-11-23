using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    public sealed class BiomeMapTests
    {
        private const int MinSamplesPerBiome = 100;

        // Done line: every biome of the list appears for the fixture seed.
        [Test]
        public void Biomes_EveryBiomeAppears_FixtureSeed()
        {
            var registry = WorldAssets.LoadBiomeRegistry();
            var counts = new int[registry.Count];
            foreach (var sample in IslandGrid.Samples)
            {
                counts[sample.Biome.Value]++;
            }

            for (var i = 0; i < counts.Length; i++)
            {
                Assert.That(
                    counts[i],
                    Is.GreaterThanOrEqualTo(MinSamplesPerBiome),
                    $"{registry.Biomes[i].name}: {counts[i]} samples of 32 m"
                );
            }
        }

        // Done line: borders blend. Along transects across every kind of border, weights
        // change smoothly from one voxel column to the next, the height never jumps a meter
        // between neighbors (steep ground is fine, a seam is not), and where the column
        // biome changes at least two biomes carry weight.
        [Test]
        public void Biomes_BordersBlend_HeightsAndWeightsContinuous()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var weights = new FixedList128Bytes<float>();
            var previous = new FixedList128Bytes<float>();
            var pairs = new HashSet<(int, int)>();
            var switches = 0;
            foreach (var start in BorderStarts(300))
            {
                var hasPrevious = false;
                var previousHeight = 0f;
                var previousBiome = default(BiomeId);
                for (var k = 0; k < 2 * IslandGrid.Step; k++)
                {
                    var x = start.x + k;
                    TerrainGenerator.BiomeWeights(p, x, start.y, ref weights);
                    var sample = TerrainGenerator.Sample(p, x, start.y);
                    var sum = 0f;
                    for (var b = 0; b < weights.Length; b++)
                    {
                        sum += weights[b];
                        if (hasPrevious)
                        {
                            Assert.That(
                                math.abs(weights[b] - previous[b]),
                                Is.LessThan(0.05f),
                                $"weight of biome {b} at ({x}, {start.y})"
                            );
                        }
                    }

                    Assert.That(sum, Is.EqualTo(1f).Within(1e-4f));
                    if (hasPrevious)
                    {
                        Assert.That(
                            math.abs(sample.Height - previousHeight),
                            Is.LessThan(8f),
                            $"height step at ({x}, {start.y})"
                        );
                        if (sample.Biome != previousBiome)
                        {
                            switches++;
                            pairs.Add(
                                (
                                    System.Math.Min((int)sample.Biome.Value, previousBiome.Value),
                                    System.Math.Max((int)sample.Biome.Value, previousBiome.Value)
                                )
                            );
                            Assert.That(
                                SecondWeight(weights),
                                Is.GreaterThan(0.1f),
                                $"no blend at the border at ({x}, {start.y})"
                            );
                        }
                    }

                    previous = weights;
                    previousHeight = sample.Height;
                    previousBiome = sample.Biome;
                    hasPrevious = true;
                }
            }

            Assert.That(switches, Is.GreaterThan(100), "too few borders crossed");
            Assert.That(pairs.Count, Is.GreaterThanOrEqualTo(8), "too few kinds of border");
        }

        // Done line: borders blend. Material borders interleave instead of following a
        // contour line: many 64 m transects across a border cross it three times or more.
        [Test]
        public void Biomes_BordersAreRagged()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var transects = 0;
            var ragged = 0;
            foreach (var start in BorderStarts(300))
            {
                var crossings = 0;
                var previous = TerrainGenerator.Sample(p, start.x, start.y).Biome;
                for (var k = 4; k < 2 * IslandGrid.Step; k += 4)
                {
                    var biome = TerrainGenerator.Sample(p, start.x + k, start.y).Biome;
                    crossings += biome != previous ? 1 : 0;
                    previous = biome;
                }

                transects++;
                ragged += crossings >= 3 ? 1 : 0;
            }

            Assert.That(transects, Is.GreaterThan(100));
            Assert.That(ragged, Is.GreaterThanOrEqualTo(transects / 4), $"{ragged} of {transects}");
        }

        [Test]
        public void Island_CoastIsIrregularAndInsideBounds()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var settings = WorldAssets.LoadTerrainSettings();
            var edge = settings.EdgeRadiusMeters * WorldConstants.VoxelsPerMeter;
            var minCoast = float.MaxValue;
            var maxCoast = 0f;
            for (var a = 0; a < 360; a += 2)
            {
                var direction = new float2(math.cos(math.radians(a)), math.sin(math.radians(a)));
                var coast = 0f;
                for (var r = 4000f; r < edge + 800f; r += 64f)
                {
                    var c = (int2)math.floor(direction * r);
                    if (TerrainGenerator.SurfaceHeight(p, c.x, c.y) > p.SeaLevel)
                    {
                        coast = r;
                    }
                }

                minCoast = math.min(minCoast, coast);
                maxCoast = math.max(maxCoast, coast);
                Assert.That(coast, Is.LessThan(edge), $"land beyond the edge at {a}°");
            }

            Assert.That(
                (maxCoast - minCoast) * WorldConstants.VoxelSize,
                Is.GreaterThan(400f),
                "the coast is too round"
            );
            foreach (var sample in IslandGrid.Samples)
            {
                Assert.That(sample.Height, Is.GreaterThan(0f));
                Assert.That(sample.Height, Is.LessThan(WorldConstants.WorldHeightVoxels));
            }

            // The outer ring of chunk columns is sea.
            for (
                var i = WorldConstants.IslandMinChunkXZ;
                i < WorldConstants.IslandMaxChunkXZ;
                i += 37
            )
            {
                var far = WorldConstants.IslandMaxChunkXZ * WorldConstants.ChunkSize - 1;
                var near = i * WorldConstants.ChunkSize;
                Assert.That(TerrainGenerator.SurfaceHeight(p, near, far), Is.LessThan(p.SeaLevel));
                Assert.That(TerrainGenerator.SurfaceHeight(p, far, near), Is.LessThan(p.SeaLevel));
                Assert.That(TerrainGenerator.SurfaceHeight(p, near, -far), Is.LessThan(p.SeaLevel));
                Assert.That(TerrainGenerator.SurfaceHeight(p, -far, near), Is.LessThan(p.SeaLevel));
            }
        }

        [Test]
        public void Climate_TemperatureFallsWithAltitude()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var settings = WorldAssets.LoadTerrainSettings();
            var sea = settings.SeaLevelMeters;
            foreach (
                var xz in new[]
                {
                    new float2(0f, 0f),
                    new float2(-1500f, 900f),
                    new float2(2500f, -2500f),
                }
            )
            {
                var low = BiomeMap.TemperatureAt(p, new float3(xz.x, sea, xz.y));
                var high = BiomeMap.TemperatureAt(p, new float3(xz.x, sea + 200f, xz.y));
                var under = BiomeMap.TemperatureAt(p, new float3(xz.x, sea - 20f, xz.y));

                Assert.That(low - high, Is.EqualTo(settings.LapseRateCPerKm * 0.2f).Within(1e-3f));
                Assert.That(under, Is.EqualTo(low).Within(1e-4f), "no lapse below sea level");
                Assert.That(low, Is.InRange(settings.TemperatureMinC, settings.TemperatureMaxC));
            }
        }

        [Test]
        public void BiomeMap_SampleMatchesSurfaceHeight()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            for (var i = 0; i < 100; i++)
            {
                var x = (i * 7919) % 50000 - 25000;
                var z = (i * 104729) % 50000 - 25000;
                var sample = BiomeMap.Sample(p, x, z);

                Assert.That(
                    (int)math.floor(sample.Height),
                    Is.EqualTo(TerrainGenerator.SurfaceHeight(p, x, z))
                );
                Assert.That(BiomeMap.BiomeAt(p, x, z), Is.EqualTo(sample.Biome));
                Assert.That(sample.Biome.Value, Is.LessThan(p.Biomes.Length));
                Assert.That(sample.Moisture, Is.InRange(0f, 1f));
                Assert.That(sample.Waste, Is.InRange(0f, 1f));
            }
        }

        // Starting points of transects 64 m long along x, one per neighbor pair of the 32 m
        // grid whose biomes differ (spread over the island, at most max).
        private static List<int2> BorderStarts(int max)
        {
            var samples = IslandGrid.Samples;
            var all = new List<int2>();
            for (var j = 0; j < IslandGrid.Size; j++)
            for (var i = 1; i < IslandGrid.Size - 1; i++)
            {
                if (
                    samples[i + IslandGrid.Size * j].Biome
                    != samples[i + 1 + IslandGrid.Size * j].Biome
                )
                {
                    all.Add(
                        new int2(
                            IslandGrid.Coordinate(i) - IslandGrid.Step / 2,
                            IslandGrid.Coordinate(j)
                        )
                    );
                }
            }

            var starts = new List<int2>();
            var stride = math.max(1, all.Count / max);
            for (var k = 0; k < all.Count && starts.Count < max; k += stride)
            {
                starts.Add(all[k]);
            }

            return starts;
        }

        private static float SecondWeight(in FixedList128Bytes<float> weights)
        {
            var first = 0f;
            var second = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                if (weights[i] > first)
                {
                    second = first;
                    first = weights[i];
                }
                else if (weights[i] > second)
                {
                    second = weights[i];
                }
            }

            return second;
        }
    }
}
