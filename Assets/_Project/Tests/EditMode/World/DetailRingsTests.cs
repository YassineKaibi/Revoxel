using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    public sealed class DetailRingsTests
    {
        private const int Levels = 6;
        private const int FullHalfWidth = 12;
        private const int RingHalfWidth = 8;

        [Test]
        public void DetailRings_RegionsNestWithMargin()
        {
            foreach (var rings in Walk(2000))
            {
                for (var level = 0; level <= Levels; level++)
                {
                    var min = rings.Min(level);
                    var max = rings.Max(level);
                    if (level < Levels)
                    {
                        Assert.That(
                            math.all((min & 1) == 0) && math.all((max & 1) == 0),
                            Is.True,
                            $"level {level} edges {min} {max} not on the coarser grid"
                        );
                    }

                    if (level == 0)
                    {
                        continue;
                    }

                    var innerMin = rings.Min(level - 1) >> 1;
                    var innerMax = rings.Max(level - 1) >> 1;
                    Assert.That(
                        math.all(innerMin >= min + 1) && math.all(innerMax <= max - 1),
                        Is.True,
                        $"level {level} square {min} {max} does not hold {innerMin} {innerMax}"
                    );
                }
            }
        }

        [Test]
        public void DetailRings_DisplaySetsTileExactlyOnce()
        {
            var count = 0;
            foreach (var rings in Walk(300))
            {
                if (count++ % 30 != 0)
                {
                    continue;
                }

                foreach (var column in SampleColumns(rings))
                {
                    var displays = 0;
                    for (var level = 0; level <= Levels; level++)
                    {
                        displays += rings.IsDisplayed(level, column >> level) ? 1 : 0;
                    }

                    Assert.That(displays, Is.EqualTo(1), $"column {column}");
                    Assert.That(rings.DisplayedLevel(column), Is.GreaterThanOrEqualTo(0));
                }
            }
        }

        [Test]
        public void DetailRings_NeighborLevelsDifferByAtMostOne()
        {
            var count = 0;
            foreach (var rings in Walk(300))
            {
                if (count++ % 30 != 0)
                {
                    continue;
                }

                var outerMin = rings.Min(Levels) << Levels;
                var outerMax = rings.Max(Levels) << Levels;
                foreach (var column in SampleColumns(rings))
                {
                    var level = rings.DisplayedLevel(column);
                    foreach (var step in new[] { new int2(1, 0), new int2(0, 1) })
                    {
                        var next = column + step;
                        if (math.any(next >= outerMax) || math.any(next < outerMin))
                        {
                            continue;
                        }

                        var other = rings.DisplayedLevel(next);
                        Assert.That(math.abs(level - other), Is.LessThanOrEqualTo(1), $"{column}");
                    }
                }
            }
        }

        [Test]
        public void DetailRings_ViewDistance()
        {
            var rings = new DetailRings(Levels, FullHalfWidth, RingHalfWidth);

            Assert.That(rings.ViewDistanceMeters, Is.EqualTo(2048f));
            Assert.That(
                DetailRings.MinRingHalfWidth(FullHalfWidth),
                Is.LessThanOrEqualTo(RingHalfWidth)
            );
        }

        // A focus random walk with teleports; yields the rings after each step.
        private static IEnumerable<DetailRings> Walk(int steps)
        {
            var rings = new DetailRings(Levels, FullHalfWidth, RingHalfWidth);
            var random = new Random(777);
            var focus = new int2(-1001, 403);
            var center = int2.zero;
            var initialized = false;
            for (var i = 0; i < steps; i++)
            {
                focus +=
                    i % 97 == 0
                        ? random.NextInt2(new int2(-3000), new int2(3000))
                        : random.NextInt2(new int2(-2), new int2(3));
                ChunkStreamer.RecenterFullDetail(ref center, focus, initialized);
                initialized = true;
                rings.Update(center);
                yield return rings;
            }
        }

        // Columns of the outer square: a grid every 7 columns plus every column along each
        // level's square edges.
        private static IEnumerable<int2> SampleColumns(DetailRings rings)
        {
            var min = rings.Min(Levels) << Levels;
            var max = rings.Max(Levels) << Levels;
            for (var z = min.y; z < max.y; z += 7)
            for (var x = min.x; x < max.x; x += 7)
            {
                yield return new int2(x, z);
            }

            for (var level = 0; level < Levels; level++)
            {
                var lmin = rings.Min(level) << level;
                var lmax = rings.Max(level) << level;
                for (var t = lmin.x - 1; t <= lmax.x; t++)
                {
                    yield return new int2(t, lmin.y - 1);
                    yield return new int2(t, lmin.y);
                    yield return new int2(t, lmax.y - 1);
                    yield return new int2(t, lmax.y);
                }

                for (var t = lmin.y - 1; t <= lmax.y; t++)
                {
                    yield return new int2(lmin.x - 1, t);
                    yield return new int2(lmin.x, t);
                    yield return new int2(lmax.x - 1, t);
                    yield return new int2(lmax.x, t);
                }
            }
        }
    }
}
