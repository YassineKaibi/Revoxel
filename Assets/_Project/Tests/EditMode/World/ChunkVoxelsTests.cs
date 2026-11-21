using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;

namespace Revoxel.Tests.EditMode
{
    public sealed class ChunkVoxelsTests
    {
        private static readonly VoxelId Stone = new VoxelId(5);

        [Test]
        public void ChunkVoxels_Uniform_ReadsEverywhere()
        {
            var voxels = ChunkVoxels.CreateUniform(Stone, Allocator.Persistent);
            try
            {
                Assert.That(voxels.IsCreated, Is.True);
                Assert.That(voxels.IsUniform, Is.True);
                Assert.That(voxels.UniformId, Is.EqualTo(Stone));
                Assert.That(voxels.BitsPerIndex, Is.EqualTo(0));
                Assert.That(voxels.Get(0), Is.EqualTo(Stone));
                Assert.That(voxels.Get(WorldConstants.VoxelsPerChunk - 1), Is.EqualTo(Stone));
                Assert.That(voxels.MemoryBytes, Is.LessThan(64));
            }
            finally
            {
                voxels.Dispose();
            }

            Assert.That(voxels.IsCreated, Is.False);
            Assert.That(default(ChunkVoxels).IsCreated, Is.False);
        }

        [Test]
        public void ChunkVoxels_SetGrowsPalette()
        {
            var voxels = ChunkVoxels.CreateUniform(VoxelId.Air, Allocator.Persistent);
            var written = new Dictionary<int, ushort>();
            try
            {
                var steps = new[] { (2, 1), (3, 2), (5, 4), (17, 8), (257, 16) };
                ushort nextId = 1;
                foreach (var (paletteCount, bits) in steps)
                {
                    while (voxels.PaletteCount < paletteCount)
                    {
                        // Spread the writes over the chunk, including the last index.
                        var index = (nextId * 127) % WorldConstants.VoxelsPerChunk;
                        if (nextId == 1)
                        {
                            index = WorldConstants.VoxelsPerChunk - 1;
                        }

                        voxels.Set(index, new VoxelId(nextId));
                        written[index] = nextId;
                        nextId++;
                    }

                    Assert.That(voxels.IsUniform, Is.False);
                    Assert.That(voxels.PaletteCount, Is.EqualTo(paletteCount));
                    Assert.That(voxels.BitsPerIndex, Is.EqualTo(bits), $"palette {paletteCount}");
                    foreach (var pair in written)
                    {
                        Assert.That(voxels.Get(pair.Key).Value, Is.EqualTo(pair.Value));
                    }

                    Assert.That(voxels.Get(1), Is.EqualTo(VoxelId.Air));
                }
            }
            finally
            {
                voxels.Dispose();
            }
        }

        [TestCase(1, 0)]
        [TestCase(16, 4)]
        [TestCase(300, 16)]
        public void ChunkVoxels_DenseRoundTrip(int distinct, int expectedBits)
        {
            using var dense = RandomDense(distinct);
            using var back = new NativeArray<ushort>(WorldConstants.VoxelsPerChunk, Allocator.Temp);
            var voxels = ChunkVoxels.FromDense(dense, Allocator.Persistent);
            try
            {
                voxels.CopyTo(back);

                Assert.That(voxels.BitsPerIndex, Is.EqualTo(expectedBits));
                Assert.That(voxels.PaletteCount, Is.EqualTo(distinct));
                for (var i = 0; i < dense.Length; i++)
                {
                    Assert.That(back[i], Is.EqualTo(dense[i]), $"index {i}");
                    if (i % 1021 == 0)
                    {
                        Assert.That(voxels.Get(i).Value, Is.EqualTo(dense[i]), $"Get {i}");
                    }
                }
            }
            finally
            {
                voxels.Dispose();
            }
        }

        [Test]
        public void ChunkVoxels_FromDense_AllSame_IsUniform()
        {
            var fill = new ushort[WorldConstants.VoxelsPerChunk];
            System.Array.Fill(fill, Stone.Value);
            using var dense = new NativeArray<ushort>(fill, Allocator.Temp);

            var voxels = ChunkVoxels.FromDense(dense, Allocator.Persistent);
            try
            {
                Assert.That(voxels.IsUniform, Is.True);
                Assert.That(voxels.UniformId, Is.EqualTo(Stone));
                Assert.That(voxels.PaletteCount, Is.EqualTo(1));
            }
            finally
            {
                voxels.Dispose();
            }
        }

        [Test]
        public void ChunkVoxels_MemoryOf16TypeChunk_Is16KiB()
        {
            using var dense = RandomDense(16);
            var voxels = ChunkVoxels.FromDense(dense, Allocator.Persistent);
            try
            {
                Assert.That(voxels.BitsPerIndex, Is.EqualTo(4));
                Assert.That(voxels.MemoryBytes, Is.InRange(16384, 16384 + 256));
            }
            finally
            {
                voxels.Dispose();
            }
        }

        // Exactly `distinct` different ids, each present at least once.
        private static NativeArray<ushort> RandomDense(int distinct)
        {
            var random = new Unity.Mathematics.Random(1234);
            var ids = new List<ushort>();
            while (ids.Count < distinct)
            {
                var id = (ushort)random.NextInt(1, 1024);
                if (!ids.Contains(id))
                {
                    ids.Add(id);
                }
            }

            var dense = new NativeArray<ushort>(WorldConstants.VoxelsPerChunk, Allocator.Temp);
            for (var i = 0; i < dense.Length; i++)
            {
                dense[i] = i < distinct ? ids[i] : ids[random.NextInt(0, distinct)];
            }

            return dense;
        }
    }
}
