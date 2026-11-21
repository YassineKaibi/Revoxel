using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Mathematics;
using UnityEngine;

namespace Revoxel.Tests.EditMode
{
    public sealed class VoxelWorldTests
    {
        private static readonly BlockId Stone = new BlockId(5);

        private GameObject _host;
        private VoxelWorld _world;
        private readonly List<(int3 pos, BlockId old, BlockId now)> _events =
            new List<(int3, BlockId, BlockId)>();

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("VoxelWorldTests");
            _world = _host.AddComponent<VoxelWorld>();
            _world.Initialize(WorldAssets.LoadRegistry(), WorldBounds.Island());
            _events.Clear();
            _world.BlockChanged += (pos, old, now) => _events.Add((pos, old, now));
        }

        [TearDown]
        public void TearDown()
        {
            _world.ReleaseResources();
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void World_Initialize_AllocatesEveryIslandChunk()
        {
            Assert.That(_world.Chunks.Count, Is.EqualTo(WorldConstants.ChunkCount));
            Assert.That(_world.DirtyCount, Is.Zero);
        }

        [TestCase(-1, 0, 0)]
        [TestCase(0, -1, 0)]
        [TestCase(0, 0, -1)]
        [TestCase(256, 0, 0)]
        [TestCase(0, 0, 256)]
        [TestCase(0, 64, 0)]
        [TestCase(-1000, -1000, -1000)]
        public void World_GetBlock_OutOfBoundsReturnsAir(int x, int y, int z)
        {
            Assert.That(_world.GetBlock(new int3(x, y, z)), Is.EqualTo(BlockId.Air));
        }

        [TestCase(-1, 0, 0)]
        [TestCase(0, -1, 0)]
        [TestCase(0, 0, -1)]
        [TestCase(256, 0, 0)]
        [TestCase(0, 0, 256)]
        [TestCase(0, 64, 0)]
        public void World_TrySetBlock_OutOfBoundsReturnsFalse(int x, int y, int z)
        {
            Assert.That(_world.TrySetBlock(new int3(x, y, z), Stone), Is.False);
            Assert.That(_world.DirtyCount, Is.Zero);
            Assert.That(_events, Is.Empty);
        }

        [Test]
        public void World_TrySetBlock_SetsAndRaisesEvent()
        {
            var pos = new int3(100, 20, 37);

            Assert.That(_world.TrySetBlock(pos, Stone), Is.True);

            Assert.That(_world.GetBlock(pos), Is.EqualTo(Stone));
            Assert.That(_events, Is.EqualTo(new[] { (pos, BlockId.Air, Stone) }));
        }

        [Test]
        public void World_TrySetBlock_SameValueReturnsFalseAndNoEvent()
        {
            var pos = new int3(3, 3, 3);

            Assert.That(_world.TrySetBlock(pos, BlockId.Air), Is.False);
            Assert.That(_events, Is.Empty);
            Assert.That(_world.DirtyCount, Is.Zero);

            Assert.That(_world.TrySetBlock(pos, Stone), Is.True);
            _events.Clear();
            Assert.That(_world.TrySetBlock(pos, Stone), Is.False);
            Assert.That(_events, Is.Empty);
        }

        [Test]
        public void World_TrySetBlock_OnBorderMarksNeighborDirty()
        {
            // Interior: only its own chunk.
            Assert.That(_world.TrySetBlock(new int3(20, 20, 20), Stone), Is.True);
            AssertDirty(new ChunkCoord(1, 1, 1));

            // x = 15 marks chunk x + 1.
            ResetWorld();
            Assert.That(_world.TrySetBlock(new int3(31, 20, 20), Stone), Is.True);
            AssertDirty(new ChunkCoord(1, 1, 1), new ChunkCoord(2, 1, 1));

            // x = 0 marks chunk x - 1.
            ResetWorld();
            Assert.That(_world.TrySetBlock(new int3(16, 20, 20), Stone), Is.True);
            AssertDirty(new ChunkCoord(1, 1, 1), new ChunkCoord(0, 1, 1));

            // Corner marks all three face neighbors.
            ResetWorld();
            Assert.That(_world.TrySetBlock(new int3(31, 31, 31), Stone), Is.True);
            AssertDirty(
                new ChunkCoord(1, 1, 1),
                new ChunkCoord(2, 1, 1),
                new ChunkCoord(1, 2, 1),
                new ChunkCoord(1, 1, 2)
            );

            // World edge: the missing neighbor is skipped without error.
            ResetWorld();
            Assert.That(_world.TrySetBlock(new int3(0, 0, 0), Stone), Is.True);
            AssertDirty(new ChunkCoord(0, 0, 0));
        }

        private void ResetWorld()
        {
            _world.Initialize(WorldAssets.LoadRegistry(), WorldBounds.Island());
        }

        private void AssertDirty(params ChunkCoord[] expected)
        {
            var dirty = new List<ChunkCoord>();
            foreach (var chunk in _world.Chunks.Values)
            {
                if (chunk.IsDirty)
                {
                    dirty.Add(chunk.Coord);
                }
            }

            Assert.That(dirty, Is.EquivalentTo(expected));
            Assert.That(_world.DirtyCount, Is.EqualTo(expected.Length));
        }
    }
}
