using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    public sealed class ChunkStreamerTests
    {
        [Test]
        public void Streamer_WantedColumns_AreWithinRadiusNearestFirst()
        {
            var streamer = new ChunkStreamer();
            var wanted = new List<int2>();
            var focus = new int2(-7, 12);

            streamer.ComputeWanted(focus, 3, wanted);

            var expected = 0;
            for (var dz = -3; dz <= 3; dz++)
            for (var dx = -3; dx <= 3; dx++)
            {
                expected += dx * dx + dz * dz <= 9 ? 1 : 0;
            }

            Assert.That(wanted.Count, Is.EqualTo(expected));
            Assert.That(wanted[0], Is.EqualTo(focus));
            var previous = 0;
            var seen = new HashSet<int2>();
            foreach (var column in wanted)
            {
                var d = column - focus;
                var distance = d.x * d.x + d.y * d.y;
                Assert.That(distance, Is.LessThanOrEqualTo(9));
                Assert.That(distance, Is.GreaterThanOrEqualTo(previous));
                Assert.That(seen.Add(column), Is.True, $"{column} twice");
                previous = distance;
            }

            streamer.ComputeWanted(focus + 1, 3, wanted);
            Assert.That(wanted.Count, Is.EqualTo(expected), "the list is cleared, not appended");
            Assert.That(wanted[0], Is.EqualTo(focus + 1));
        }

        [Test]
        public void Streamer_Unload_UsesHysteresis()
        {
            var streamer = new ChunkStreamer();
            var focus = new int2(100, -50);

            Assert.That(streamer.ShouldUnload(focus, focus, 12, 2), Is.False);
            Assert.That(streamer.ShouldUnload(focus + new int2(12, 0), focus, 12, 2), Is.False);
            Assert.That(streamer.ShouldUnload(focus + new int2(0, -13), focus, 12, 2), Is.False);
            Assert.That(streamer.ShouldUnload(focus + new int2(14, 0), focus, 12, 2), Is.False);
            Assert.That(streamer.ShouldUnload(focus + new int2(-15, 0), focus, 12, 2), Is.True);
            Assert.That(streamer.ShouldUnload(focus + new int2(11, 11), focus, 12, 2), Is.True);
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
