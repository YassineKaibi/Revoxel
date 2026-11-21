using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;

namespace Revoxel.Tests.EditMode
{
    public sealed class WorldGenTests
    {
        private static readonly ChunkCoord[] SampleChunks =
        {
            new ChunkCoord(8, 1, 8),
            new ChunkCoord(8, 2, 8),
            new ChunkCoord(2, 1, 8),
            new ChunkCoord(8, 1, 2),
            new ChunkCoord(12, 1, 12),
        };

        [Test]
        public void Gen_SettingsAsset_IsValid()
        {
            var settings = WorldAssets.LoadGenSettings();

            Assert.That(settings, Is.Not.Null, $"No settings at {WorldAssets.GenSettingsPath}");
            Assert.That(settings.name, Does.StartWith("Placeholder_"));
            Assert.That(settings.TryValidate(out var error), Is.True, error);
        }

        [Test]
        public void Gen_SameSeedSameChunk_SameBytes()
        {
            var p = WorldAssets.Params(Seeds.Fixture);
            foreach (var coord in SampleChunks)
            {
                using var first = Generate(p, coord);
                using var second = Generate(WorldAssets.Params(Seeds.Fixture), coord);

                Assert.That(second.ToArray(), Is.EqualTo(first.ToArray()), $"Chunk {coord}");
            }
        }

        [Test]
        public void Gen_DifferentSeeds_Differ()
        {
            var a = WorldAssets.Params(Seeds.Fixture);
            var b = WorldAssets.Params(Seeds.Other);
            var differing = 0;
            foreach (var coord in SampleChunks)
            {
                using var first = Generate(a, coord);
                using var second = Generate(b, coord);
                if (!System.Linq.Enumerable.SequenceEqual(first, second))
                {
                    differing++;
                }
            }

            Assert.That(differing, Is.GreaterThan(0));
        }

        [Test]
        public void Gen_ZoneMap_HasAllThreeZones()
        {
            var p = WorldAssets.Params(Seeds.Fixture);
            using var zones = new NativeArray<byte>(WorldConstants.ColumnCount, Allocator.TempJob);
            using var heights = new NativeArray<int>(WorldConstants.ColumnCount, Allocator.TempJob);
            WorldGenerator.ScheduleColumns(p, zones, heights).Complete();

            var counts = new int[4];
            foreach (var zone in zones)
            {
                counts[zone]++;
            }

            Assert.That(counts[(int)ZoneId.Plage], Is.GreaterThan(0), "Plage");
            Assert.That(counts[(int)ZoneId.Foret], Is.GreaterThan(0), "Forêt");
            Assert.That(counts[(int)ZoneId.Decharge], Is.GreaterThan(0), "Décharge");
            Assert.That(counts[(int)ZoneId.None], Is.GreaterThan(0), "sea");

            // The world edge is open sea: the island fits inside the world.
            Assert.That(zones[Coords.ColumnIndex(0, 0)], Is.EqualTo((byte)ZoneId.None));
            Assert.That(zones[Coords.ColumnIndex(255, 255)], Is.EqualTo((byte)ZoneId.None));
            Assert.That(zones[Coords.ColumnIndex(128, 128)], Is.EqualTo((byte)ZoneId.Decharge));
        }

        [Test]
        public void Gen_ChunkOutsideIsland_IsOnlyWaterAndAir()
        {
            var p = WorldAssets.Params(Seeds.Fixture);
            using var blocks = Generate(p, new ChunkCoord(-1, 1, -1));

            foreach (var block in blocks)
            {
                Assert.That(block == 0 || block == p.Water, Is.True, $"Unexpected block {block}");
            }
        }

        public static NativeArray<byte> Generate(WorldGenParams p, ChunkCoord coord)
        {
            var blocks = new NativeArray<byte>(WorldConstants.BlocksPerChunk, Allocator.TempJob);
            WorldGenerator.GenerateChunk(p, coord, blocks);
            return blocks;
        }
    }
}
