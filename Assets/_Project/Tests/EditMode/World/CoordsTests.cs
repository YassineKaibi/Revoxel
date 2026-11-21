using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    public sealed class CoordsTests
    {
        private static readonly object[] WorldToChunkCases =
        {
            new object[] { new int3(0, 0, 0), new int3(0, 0, 0), new int3(0, 0, 0) },
            new object[] { new int3(15, 15, 15), new int3(0, 0, 0), new int3(15, 15, 15) },
            new object[] { new int3(16, 0, 0), new int3(1, 0, 0), new int3(0, 0, 0) },
            new object[] { new int3(-1, 0, 0), new int3(-1, 0, 0), new int3(15, 0, 0) },
            new object[] { new int3(-16, 0, 0), new int3(-1, 0, 0), new int3(0, 0, 0) },
            new object[] { new int3(-17, 0, 0), new int3(-2, 0, 0), new int3(15, 0, 0) },
            new object[] { new int3(5, -1, -33), new int3(0, -1, -3), new int3(5, 15, 15) },
        };

        [TestCaseSource(nameof(WorldToChunkCases))]
        public void Coords_WorldToChunk_PositiveAndNegative(
            int3 world,
            int3 expectedChunk,
            int3 expectedLocal
        )
        {
            Coords.WorldToChunk(world, out var chunk, out var local);

            Assert.That(chunk.Value, Is.EqualTo(expectedChunk));
            Assert.That(local, Is.EqualTo(expectedLocal));
            Assert.That(Coords.LocalToWorld(chunk, local), Is.EqualTo(world));
            Assert.That(Coords.ChunkOf(world), Is.EqualTo(chunk));
        }

        [Test]
        public void Coords_LocalIndex_RoundTrip()
        {
            var seen = new HashSet<int>();
            for (var y = 0; y < WorldConstants.ChunkSize; y++)
            for (var z = 0; z < WorldConstants.ChunkSize; z++)
            for (var x = 0; x < WorldConstants.ChunkSize; x++)
            {
                var local = new int3(x, y, z);
                var index = Coords.LocalIndex(local);

                Assert.That(index, Is.InRange(0, WorldConstants.BlocksPerChunk - 1));
                Assert.That(seen.Add(index), Is.True, $"Index {index} used twice");
                Assert.That(Coords.LocalFromIndex(index), Is.EqualTo(local));
            }

            Assert.That(seen.Count, Is.EqualTo(WorldConstants.BlocksPerChunk));
        }

        [Test]
        public void Coords_PaddedIndex_CoversPaddedVolume()
        {
            Assert.That(Coords.PaddedIndex(-1, -1, -1), Is.EqualTo(0));
            Assert.That(
                Coords.PaddedIndex(
                    WorldConstants.ChunkSize,
                    WorldConstants.ChunkSize,
                    WorldConstants.ChunkSize
                ),
                Is.EqualTo(WorldConstants.PaddedBlocks - 1)
            );
        }

        [Test]
        public void Coords_ColumnIndex_RowMajorInX()
        {
            Assert.That(Coords.ColumnIndex(0, 0), Is.EqualTo(0));
            Assert.That(Coords.ColumnIndex(255, 0), Is.EqualTo(255));
            Assert.That(Coords.ColumnIndex(0, 1), Is.EqualTo(WorldConstants.WorldSizeX));
            Assert.That(Coords.ColumnIndex(255, 255), Is.EqualTo(WorldConstants.ColumnCount - 1));
        }

        [Test]
        public void ChunkCoord_EqualityAndHash()
        {
            var a = new ChunkCoord(1, -2, 3);
            var b = new ChunkCoord(new int3(1, -2, 3));

            Assert.That(a == b, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a != new ChunkCoord(1, 2, 3), Is.True);
        }
    }
}
