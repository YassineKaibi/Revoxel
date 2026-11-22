using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    public sealed class ChunkStreamerTests
    {
        [Test]
        public void Streamer_WantedColumns_FillSquareNearestFirst()
        {
            var streamer = new ChunkStreamer();
            var wanted = new List<int2>();
            var center = new int2(-8, 12);

            streamer.ComputeWanted(center, 3, wanted);

            Assert.That(wanted.Count, Is.EqualTo(36));
            var previous = 0;
            var seen = new HashSet<int2>();
            foreach (var column in wanted)
            {
                Assert.That(
                    math.all(column >= center - 3) && math.all(column < center + 3),
                    Is.True
                );
                var d = (column - center) * 2 + 1;
                var distance = math.dot(d, d);
                Assert.That(distance, Is.GreaterThanOrEqualTo(previous));
                Assert.That(seen.Add(column), Is.True, $"{column} twice");
                previous = distance;
            }

            streamer.ComputeWanted(center + 2, 3, wanted);
            Assert.That(wanted.Count, Is.EqualTo(36), "the list is cleared, not appended");
            Assert.That(math.cmax(math.abs(wanted[0] - (center + 2))), Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void Streamer_Unload_UsesSquareHysteresis()
        {
            var streamer = new ChunkStreamer();
            var center = new int2(100, -50);

            Assert.That(streamer.ShouldUnload(center, center, 12, 2), Is.False);
            Assert.That(streamer.ShouldUnload(center + new int2(13, 0), center, 12, 2), Is.False);
            Assert.That(streamer.ShouldUnload(center + new int2(13, 13), center, 12, 2), Is.False);
            Assert.That(
                streamer.ShouldUnload(center + new int2(-14, -14), center, 12, 2),
                Is.False
            );
            Assert.That(streamer.ShouldUnload(center + new int2(14, 0), center, 12, 2), Is.True);
            Assert.That(streamer.ShouldUnload(center + new int2(0, -15), center, 12, 2), Is.True);
        }

        [Test]
        public void FullDetail_Recenter_KeepsFocusInsideAndEven()
        {
            var random = new Random(2026);
            var focus = new int2(-3, 7);
            var center = int2.zero;
            var initialized = false;
            for (var i = 0; i < 2000; i++)
            {
                focus +=
                    i % 250 == 0
                        ? random.NextInt2(new int2(-400), new int2(400))
                        : random.NextInt2(new int2(-1), new int2(2));
                ChunkStreamer.RecenterFullDetail(ref center, focus, initialized);
                initialized = true;
                Assert.That(center.x % 2 == 0 && center.y % 2 == 0, Is.True, $"{center} odd");
                Assert.That(
                    math.cmax(math.abs(focus - center)),
                    Is.LessThanOrEqualTo(ChunkStreamer.RecenterDistance),
                    $"focus {focus}, center {center}"
                );
            }
        }

        [Test]
        public void FullDetail_Recenter_SmallMovesDoNotRecenter()
        {
            var center = int2.zero;
            ChunkStreamer.RecenterFullDetail(ref center, new int2(-5, 9), false);
            var first = center;
            for (var i = 0; i < 20; i++)
            {
                var focus = new int2(-5 + (i & 1), 9 - (i & 1));
                Assert.That(ChunkStreamer.RecenterFullDetail(ref center, focus, true), Is.False);
            }

            Assert.That(center, Is.EqualTo(first));
        }

        [Test]
        public void Streaming_SettingsAsset_IsValid()
        {
            var settings = WorldAssets.LoadStreamingSettings();

            Assert.That(
                settings,
                Is.Not.Null,
                $"No settings at {WorldAssets.StreamingSettingsPath}"
            );
            Assert.That(settings.TryValidate(out var error), Is.True, error);
            Assert.That(settings.name, Does.StartWith("Placeholder_"));
            Assert.That(settings.MainThreadBudgetMs, Is.GreaterThan(0f));
            Assert.That(settings.VoxelMemoryBudgetMiB, Is.GreaterThan(0));
        }
    }
}
