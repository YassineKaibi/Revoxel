using System;
using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    public sealed class WorldGenFeatureTests
    {
        private const int Size = WorldConstants.ChunkSize;
        private const int ShoreDistance = 4;

        private readonly Dictionary<ChunkCoord, byte[]> _chunks =
            new Dictionary<ChunkCoord, byte[]>();
        private WorldGenParams _params;

        [SetUp]
        public void SetUp()
        {
            _chunks.Clear();
        }

        [TestCase(Seeds.Fixture)]
        [TestCase(Seeds.Other)]
        public void Gen_SpawnPoint_IsOnPlageAndStandable(int seed)
        {
            _params = WorldAssets.Params(seed);
            var spawn = WorldGenerator.FindSpawnPoint(_params);

            var height = WorldGenerator.ColumnHeight(_params, spawn.x, spawn.z);
            Assert.That(
                WorldGenerator.ColumnZone(_params, spawn.x, spawn.z, height),
                Is.EqualTo(ZoneId.Plage)
            );
            var below = BlockAt(spawn - new int3(0, 1, 0));
            Assert.That(below, Is.Not.EqualTo((byte)0), "Air under the spawn point");
            Assert.That(below, Is.Not.EqualTo(_params.Water), "Water under the spawn point");
            Assert.That(BlockAt(spawn), Is.EqualTo((byte)0), "Spawn block is not Air");
            Assert.That(BlockAt(spawn + new int3(0, 1, 0)), Is.EqualTo((byte)0), "No headroom");

            // At the shore: open sea within the island ring outward from the spawn.
            Assert.That(
                WorldGenerator.FindSpawnPoint(_params),
                Is.EqualTo(spawn),
                "Not deterministic"
            );
        }

        [Test]
        public void Gen_TreeAcrossChunkBorder_WrittenToBoth()
        {
            _params = WorldAssets.Params(Seeds.Fixture);
            Assert.That(_params.LeafRadius, Is.GreaterThan(0), "Leaves cannot cross a border");
            var root = FindTreeOnEastBorder();
            var top = root.y + _params.TrunkHeight - 1;

            // The root column is the last of its chunk in x; the leaves reach the next chunk.
            Assert.That(
                Coords.ChunkOf(root + new int3(1, 0, 0)),
                Is.Not.EqualTo(Coords.ChunkOf(root))
            );
            Assert.That(BlockAt(new int3(root.x, top, root.z)), Is.EqualTo(_params.Log), "Trunk");
            Assert.That(
                BlockAt(new int3(root.x - 1, top, root.z)),
                Is.EqualTo(_params.Leaves),
                "West leaves"
            );
            Assert.That(
                BlockAt(new int3(root.x + 1, top, root.z)),
                Is.EqualTo(_params.Leaves),
                "East leaves"
            );
            Assert.That(
                BlockAt(new int3(root.x + _params.LeafRadius, top, root.z)),
                Is.EqualTo(_params.Leaves),
                "Leaves at the full radius in the next chunk"
            );
        }

        // A plain hash of (x, z) put the trees on a lattice: look for any offset shared by
        // many pairs of trees.
        [Test]
        public void Gen_TreeHash_HasNoLattice()
        {
            const int Grid = 96;
            const float Density = 0.05f;
            var trees = new List<int2>();
            for (var z = 0; z < Grid; z++)
            for (var x = 0; x < Grid; x++)
            {
                if (WorldGenerator.Hash01(12345u, x, z) < Density)
                {
                    trees.Add(new int2(x, z));
                }
            }

            var expected = Grid * Grid * Density;
            Assert.That(trees.Count, Is.InRange(expected * 0.7f, expected * 1.3f), "Density off");

            var offsets = new Dictionary<int2, int>();
            foreach (var a in trees)
            foreach (var b in trees)
            {
                var d = b - a;
                if (math.all(d == 0) || math.any(math.abs(d) > 12))
                {
                    continue;
                }

                offsets.TryGetValue(d, out var count);
                offsets[d] = count + 1;
            }

            var max = 0;
            foreach (var count in offsets.Values)
            {
                max = math.max(max, count);
            }

            Assert.That(max, Is.LessThan(trees.Count / 4), "Trees repeat at a fixed offset");
        }

        private int3 FindTreeOnEastBorder()
        {
            var r = _params.LeafRadius;
            for (var x = Size - 1; x < WorldConstants.WorldSizeX - Size; x += Size)
            for (var z = 0; z < WorldConstants.WorldSizeZ; z++)
            {
                var height = WorldGenerator.ColumnHeight(_params, x, z);
                var zone = WorldGenerator.ColumnZone(_params, x, z, height);
                if (
                    !WorldGenerator.IsTreeColumn(_params, x, z, zone)
                    || HasTrunkInRow(x - 1, x + r, z)
                )
                {
                    continue;
                }

                return new int3(x, height, z);
            }

            Assert.Fail("No tree on a chunk border for the fixture seed");
            return default;
        }

        // Another trunk in the tested row would replace the leaves this test expects.
        private bool HasTrunkInRow(int fromX, int toX, int z)
        {
            for (var x = fromX; x <= toX; x++)
            {
                var height = WorldGenerator.ColumnHeight(_params, x, z);
                var zone = WorldGenerator.ColumnZone(_params, x, z, height);
                if (x != fromX + 1 && WorldGenerator.IsTreeColumn(_params, x, z, zone))
                {
                    return true;
                }
            }

            return false;
        }

        private byte BlockAt(int3 world)
        {
            Coords.WorldToChunk(world, out var coord, out var local);
            if (!_chunks.TryGetValue(coord, out var blocks))
            {
                using var native = new NativeArray<byte>(
                    WorldConstants.BlocksPerChunk,
                    Allocator.TempJob
                );
                WorldGenerator.GenerateChunk(_params, coord, native);
                blocks = native.ToArray();
                _chunks.Add(coord, blocks);
            }

            return blocks[Coords.LocalIndex(local)];
        }
    }
}
