using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Revoxel.Tests.EditMode
{
    public sealed class VoxelWorldTests
    {
        private static readonly VoxelId Stone = new VoxelId(5);
        private static readonly VoxelId Plank = new VoxelId(9);

        private static readonly int3[] OutsidePositions =
        {
            new int3(-32001, 0, 0),
            new int3(32000, 0, 0),
            new int3(0, -1, 0),
            new int3(0, 4096, 0),
            new int3(0, 0, 0),
        };

        private readonly List<(int3 Min, int3 Max)> _events = new List<(int3, int3)>();
        private GameObject _host;
        private VoxelWorld _world;

        [SetUp]
        public void SetUp()
        {
            _events.Clear();
            _host = new GameObject("VoxelWorldTests");
            _world = _host.AddComponent<VoxelWorld>();
            _world.Initialize(WorldAssets.LoadRegistry(), WorldBounds.Island());
            _world.VoxelsChanged += (min, max) => _events.Add((min, max));
        }

        [TearDown]
        public void TearDown()
        {
            _world.ReleaseResources();
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void World_GetVoxel_OutOfBoundsReturnsAir()
        {
            Insert(Stone, new int3(-1000, 0, 0));
            Insert(Stone, new int3(999, 127, 0));

            foreach (var position in OutsidePositions)
            {
                Assert.That(_world.GetVoxel(position), Is.EqualTo(VoxelId.Air), $"{position}");
            }

            Assert.That(_world.GetVoxel(new int3(-32000, 0, 0)), Is.EqualTo(Stone));
            Assert.That(_world.GetVoxel(new int3(31999, 4095, 31)), Is.EqualTo(Stone));
        }

        [Test]
        public void World_TrySetVoxel_OutOfBoundsReturnsFalse()
        {
            foreach (var position in OutsidePositions)
            {
                Assert.That(_world.TrySetVoxel(position, Stone), Is.False, $"{position}");
            }

            Assert.That(_events, Is.Empty);
            Assert.That(_world.DirtyCount, Is.EqualTo(0));
        }

        [Test]
        public void World_TrySetVoxel_SameValueReturnsFalseAndNoEvent()
        {
            Insert(Stone, new int3(0, 0, 0));

            Assert.That(_world.TrySetVoxel(new int3(3, 3, 3), Stone), Is.False);
            Assert.That(_events, Is.Empty);
            Assert.That(_world.DirtyCount, Is.EqualTo(0));

            Assert.That(_world.TrySetVoxel(new int3(3, 3, 3), VoxelId.Air), Is.True);
            Assert.That(_world.GetVoxel(new int3(3, 3, 3)), Is.EqualTo(VoxelId.Air));
            Assert.That(_events, Is.EqualTo(new[] { (new int3(3, 3, 3), new int3(3, 3, 3)) }));
        }

        [Test]
        public void World_TrySetVoxel_UnknownIdReturnsFalse()
        {
            Insert(Stone, new int3(0, 0, 0));

            Assert.That(_world.TrySetVoxel(new int3(3, 3, 3), new VoxelId(600)), Is.False);
            Assert.That(_world.GetVoxel(new int3(3, 3, 3)), Is.EqualTo(Stone));
            Assert.That(_world.TryGetChunk(new ChunkCoord(0, 0, 0), out var chunk), Is.True);
            Assert.That(chunk.Voxels.IsUniform, Is.True);
            Assert.That(_events, Is.Empty);
        }

        [TestCase(16, 16, 16, 1)]
        [TestCase(31, 16, 16, 2)]
        [TestCase(31, 31, 31, 8)]
        [TestCase(0, 0, 0, 2)]
        public void World_TrySetVoxel_OnBorderMarksNeighborDirty(int x, int y, int z, int dirty)
        {
            // Loaded: chunk x 0 to 1, y 0 to 1, z -1 to 1. For the corner (0, 0, 0) only
            // (0, 0, 0) and (0, 0, -1) of its 8 candidates are loaded: x -1 is not, and
            // y -1 is outside the island.
            Insert(Stone, new int3(0, 0, -1), new int3(1, 1, 1));

            Assert.That(_world.TrySetVoxel(new int3(x, y, z), Plank), Is.True);

            Assert.That(_world.DirtyCount, Is.EqualTo(dirty));
        }

        [Test]
        public void World_CarveSphere_ChangesExpectedVoxels()
        {
            Insert(Stone, new int3(-1, 0, -1), new int3(1, 1, 1));
            var center = new float3(0.5f, 0.5f, 0.5f);

            var count = _world.CarveSphere(center, 0.5f, VoxelId.Air);

            var expected = 0;
            var min = new int3(int.MaxValue);
            var max = new int3(int.MinValue);
            for (var y = 0; y < 16; y++)
            for (var z = -8; z < 16; z++)
            for (var x = -8; x < 16; x++)
            {
                var v = new int3(x, y, z);
                var inside = math.distancesq(((float3)v + 0.5f) * 0.125f, center) <= 0.25f;
                if (inside)
                {
                    expected++;
                    min = math.min(min, v);
                    max = math.max(max, v);
                }

                Assert.That(
                    _world.GetVoxel(v),
                    Is.EqualTo(inside ? VoxelId.Air : Stone),
                    $"voxel {v}"
                );
            }

            Assert.That(count, Is.EqualTo(expected));
            Assert.That(count, Is.GreaterThan(200));
            Assert.That(_events, Is.EqualTo(new[] { (min, max) }));
            Assert.That(_world.CarveSphere(center, 0.5f, VoxelId.Air), Is.EqualTo(0));
            Assert.That(_events.Count, Is.EqualTo(1));
        }

        [Test]
        public void World_CarveAcrossUnloadedChunks_ChangesOnlyLoaded()
        {
            // Chunk (0, 0, 0) is loaded, (-1, 0, 0) is not; the sphere straddles x = 0.
            Insert(Stone, new int3(0, 0, 0));
            var center = new float3(0f, 2f, 2f);

            var count = _world.CarveSphere(center, 0.5f, VoxelId.Air);

            var expected = 0;
            for (var y = 0; y < 32; y++)
            for (var z = 0; z < 32; z++)
            for (var x = -8; x < 8; x++)
            {
                var v = new int3(x, y, z);
                if (x >= 0 && math.distancesq(((float3)v + 0.5f) * 0.125f, center) <= 0.25f)
                {
                    expected++;
                }
            }

            Assert.That(expected, Is.GreaterThan(0));
            Assert.That(count, Is.EqualTo(expected));
            Assert.That(
                _world.CarveSphere(new float3(-2f, 2f, 2f), 0.5f, VoxelId.Air),
                Is.EqualTo(0)
            );
            Assert.That(_world.CarveSphere(center, 0.5f, new VoxelId(600)), Is.EqualTo(0));
            Assert.That(_events.Count, Is.EqualTo(1));
        }

        // D-074: the water query reads the loaded voxels: in water below the top of the
        // liquid, with the surface there; out of it above; carving the top lowers the
        // surface; no answer where nothing is loaded.
        [Test]
        public void World_TrySampleWater_ReadsLoadedVoxels()
        {
            var water = new VoxelId(1);
            var coord = new ChunkCoord(0, 4, 0);
            var dense = new NativeArray<ushort>(WorldConstants.VoxelsPerChunk, Allocator.Temp);
            for (var i = 0; i < dense.Length; i++)
            {
                var y = Coords.LocalFromIndex(i).y;
                dense[i] =
                    y < 4 ? Stone.Value
                    : y < 20 ? water.Value
                    : VoxelId.Air.Value;
            }

            _world.InsertChunk(coord, ChunkVoxels.FromDense(dense, Allocator.Persistent));
            dense.Dispose();
            var baseY = 4 * WorldConstants.ChunkSize * WorldConstants.VoxelSize;
            var surface = baseY + 20 * WorldConstants.VoxelSize;

            Assert.That(
                _world.TrySampleWater(new float3(0.5f, baseY + 1f, 0.5f), out var inside),
                Is.True
            );
            Assert.That(inside.InWater, Is.True);
            Assert.That(inside.SurfaceMeters, Is.EqualTo(surface).Within(1e-4f));
            Assert.That(inside.DepthMeters, Is.EqualTo(surface - baseY - 1f).Within(1e-4f));
            Assert.That(
                inside.Submersion(baseY + 1f, 4f),
                Is.EqualTo((surface - baseY - 1f) / 4f).Within(1e-4f)
            );

            Assert.That(
                _world.TrySampleWater(new float3(0.5f, surface + 0.5f, 0.5f), out var above),
                Is.True
            );
            Assert.That(above.InWater, Is.False);
            Assert.That(above.Submersion(surface, 2f), Is.EqualTo(0f));

            Assert.That(
                _world.TrySetVoxel(new int3(0, 4 * WorldConstants.ChunkSize + 19, 0), VoxelId.Air),
                Is.True
            );
            _world.TrySampleWater(new float3(0.05f, baseY + 1f, 0.05f), out var carved);
            Assert.That(
                carved.SurfaceMeters,
                Is.EqualTo(surface - WorldConstants.VoxelSize).Within(1e-4f)
            );

            Assert.That(_world.TrySampleWater(new float3(100f, baseY, 100f), out _), Is.False);
        }

        [Test]
        public void SkyLight_HeightsFollowEdits()
        {
            // Stone up to y 63 in column (0, 0), Air above to y 95.
            Insert(Stone, new int3(0, 0, 0), new int3(0, 1, 0));
            Insert(VoxelId.Air, new int3(0, 2, 0));
            var leaves = UnityEditor
                .AssetDatabase.LoadAssetAtPath<VoxelType>(
                    $"{WorldAssets.VoxelsFolder}/Voxel_Leaves.asset"
                )
                .Id;

            AssertSky(64, 64);
            Assert.That(_world.TrySetVoxel(new int3(5, 63, 5), VoxelId.Air), Is.True);
            AssertSky(63, 63);
            Assert.That(_world.TrySetVoxel(new int3(5, 70, 5), leaves), Is.True);
            AssertSky(71, 63);
            Assert.That(_world.TrySetVoxel(new int3(5, 80, 5), Plank), Is.True);
            AssertSky(81, 81);
            _world.CarveSphere(
                new float3(5.5f, 80.5f, 5.5f) * WorldConstants.VoxelSize,
                0.2f,
                VoxelId.Air
            );
            AssertSky(71, 63);
            Assert.That(_world.TryGetSkyHeights(6, 5, out var any, out var full), Is.True);
            Assert.That((any, full), Is.EqualTo((64, 64)), "the neighbor column is unchanged");
        }

        private void AssertSky(int any, int full)
        {
            Assert.That(_world.TryGetSkyHeights(5, 5, out var a, out var f), Is.True);
            Assert.That((a, f), Is.EqualTo((any, full)));
        }

        // Inserts uniform chunks for every chunk coordinate from min to max (inclusive)
        // that is inside the island.
        private void Insert(VoxelId id, int3 min, int3? max = null)
        {
            var last = max ?? min;
            var bounds = WorldBounds.Island();
            for (var y = min.y; y <= last.y; y++)
            for (var z = min.z; z <= last.z; z++)
            for (var x = min.x; x <= last.x; x++)
            {
                var coord = new ChunkCoord(x, y, z);
                if (bounds.Contains(coord))
                {
                    _world.InsertChunk(coord, ChunkVoxels.CreateUniform(id, Allocator.Persistent));
                }
            }
        }
    }
}
