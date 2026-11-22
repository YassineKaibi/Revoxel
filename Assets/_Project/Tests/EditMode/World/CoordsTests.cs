using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    public sealed class CoordsTests
    {
        [TestCase(0, 0, 0)]
        [TestCase(31, 0, 31)]
        [TestCase(32, 1, 0)]
        [TestCase(-1, -1, 31)]
        [TestCase(-32, -1, 0)]
        [TestCase(-33, -2, 31)]
        public void Coords_WorldToChunk_PositiveAndNegative(int x, int chunkX, int localX)
        {
            var voxel = new int3(x, 5, -x);

            Coords.WorldToChunk(voxel, out var chunk, out var local);

            Assert.That(chunk.Value.x, Is.EqualTo(chunkX));
            Assert.That(local.x, Is.EqualTo(localX));
            Assert.That(Coords.IsLocalInRange(local), Is.True);
            Assert.That(Coords.LocalToWorld(chunk, local), Is.EqualTo(voxel));
            Assert.That(Coords.ChunkOf(voxel), Is.EqualTo(chunk));
            Assert.That(Coords.ChunkOrigin(chunk) + local, Is.EqualTo(voxel));
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

                Assert.That(index, Is.InRange(0, WorldConstants.VoxelsPerChunk - 1));
                Assert.That(Coords.LocalIndex(x, y, z), Is.EqualTo(index));
                Assert.That(seen.Add(index), Is.True, $"Index {index} used twice");
                Assert.That(Coords.LocalFromIndex(index), Is.EqualTo(local));
            }

            Assert.That(seen.Count, Is.EqualTo(WorldConstants.VoxelsPerChunk));
            Assert.That(WorldConstants.VoxelsPerChunk, Is.EqualTo(32768));
        }

        [Test]
        public void Coords_PaddedIndex_CoversPaddedVolume()
        {
            Assert.That(Coords.PaddedIndex(-1, -1, -1), Is.EqualTo(0));
            Assert.That(Coords.PaddedIndex(32, 32, 32), Is.EqualTo(39303));
            Assert.That(WorldConstants.PaddedVoxels, Is.EqualTo(39304));
        }

        [Test]
        public void Coords_FarEdge_RoundTrip()
        {
            var voxels = new[]
            {
                new int3(0, 0, 0),
                new int3(-32000, 0, -32000),
                new int3(31999, 4095, 31999),
                new int3(-1, 5, -7),
            };
            foreach (var v in voxels)
            {
                var center = Coords.VoxelToWorldPosition(v) + 0.0625f;

                Assert.That(Coords.WorldPositionToVoxel(center), Is.EqualTo(v), $"voxel {v}");
            }
        }

        [Test]
        public void Coords_VoxelToWorldPosition_IsEighthOfAMeter()
        {
            Assert.That(
                Coords.VoxelToWorldPosition(new int3(8, -8, 4)),
                Is.EqualTo(new float3(1f, -1f, 0.5f))
            );
            Assert.That(Coords.ColumnOf(new ChunkCoord(3, 7, -2)), Is.EqualTo(new int2(3, -2)));
        }

        [Test]
        public void WorldBounds_Island_Is8kmBy512m()
        {
            var bounds = WorldBounds.Island();
            var size =
                (float3)(bounds.Max.Value - bounds.Min.Value)
                * WorldConstants.ChunkSize
                * WorldConstants.VoxelSize;

            Assert.That(size, Is.EqualTo(new float3(8000f, 512f, 8000f)));
            Assert.That(bounds.ContainsVoxel(new int3(31999, 4095, 31999)), Is.True);
            Assert.That(bounds.ContainsVoxel(new int3(-32000, 0, -32000)), Is.True);
            Assert.That(bounds.ContainsVoxel(new int3(32000, 0, 0)), Is.False);
            Assert.That(bounds.ContainsVoxel(new int3(0, -1, 0)), Is.False);
            Assert.That(bounds.Contains(new ChunkCoord(999, 127, -1000)), Is.True);
            Assert.That(bounds.Contains(new ChunkCoord(1000, 0, 0)), Is.False);
        }

        [Test]
        public void VoxelId_AirIsZero()
        {
            Assert.That(VoxelId.Air.Value, Is.EqualTo(0));
            Assert.That(VoxelId.Air.IsAir, Is.True);
            Assert.That(new VoxelId(600) == new VoxelId(600), Is.True);
            Assert.That(new VoxelId(600) != VoxelId.Air, Is.True);
            Assert.That(new VoxelId(600).IsAir, Is.False);
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
