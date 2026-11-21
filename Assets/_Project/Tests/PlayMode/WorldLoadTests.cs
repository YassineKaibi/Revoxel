using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.Core;
using Revoxel.World;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Revoxel.Tests.PlayMode
{
    public sealed class WorldLoadTests
    {
        private const float TimeoutSeconds = 120f;

        private readonly List<string> _problems = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _problems.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
        }

        [UnityTest]
        public IEnumerator World_LoadAll_NoNativeLeaks()
        {
            var previousMode = NativeLeakDetection.Mode;
            NativeLeakDetection.Mode = NativeLeakDetectionMode.Enabled;
            try
            {
                var leaksBefore = UnsafeUtility.CheckForLeaks();

                yield return LoadIsland();
                var world = Object.FindAnyObjectByType<VoxelWorld>();
                Assert.That(world, Is.Not.Null, "No VoxelWorld in Island");
                yield return WaitFor(() => world.IsLoaded, "the island to load");

                Assert.That(world.Chunks.Count, Is.EqualTo(WorldConstants.ChunkCount));
                Assert.That(world.ViewCount, Is.InRange(1, WorldConstants.ChunkCount - 1));
                Assert.That(
                    world.GetZone(world.SpawnPoint.x, world.SpawnPoint.z),
                    Is.EqualTo(ZoneId.Plage)
                );
                Assert.That(world.GetBlock(world.SpawnPoint - new int3(0, 1, 0)).IsAir, Is.False);
                Assert.That(world.GetBlock(world.SpawnPoint).IsAir, Is.True);

                Object.Destroy(world.gameObject);
                yield return null;
                yield return null;

                Assert.That(
                    UnsafeUtility.CheckForLeaks(),
                    Is.LessThanOrEqualTo(leaksBefore),
                    "Native leaks"
                );
                Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
            }
            finally
            {
                NativeLeakDetection.Mode = previousMode;
            }
        }

        [UnityTest]
        public IEnumerator World_EditAtChunkBorder_UpdatesMeshAndCollider()
        {
            yield return LoadIsland();
            var world = Object.FindAnyObjectByType<VoxelWorld>();
            yield return WaitFor(() => world.IsLoaded, "the island to load");

            // The top block of the spawn column, moved to a chunk border in x.
            var spawn = world.SpawnPoint;
            var x = (spawn.x & ~WorldConstants.ChunkMask) + WorldConstants.ChunkMask;
            var column = new int3(x, 0, spawn.z);
            var top = TopSolid(world, column);
            Assert.That(top.y, Is.GreaterThan(0), "No solid block in the test column");
            var chunk = Coords.ChunkOf(top);
            var neighbor = chunk.Offset(new int3(1, 0, 0));
            world.TryGetChunk(neighbor, out var neighborChunk);
            var neighborVertices =
                neighborChunk.RenderMesh != null ? neighborChunk.RenderMesh.vertexCount : 0;

            var ray = new Ray(new Vector3(top.x + 0.5f, top.y + 5f, top.z + 0.5f), Vector3.down);
            Assert.That(
                Physics.Raycast(ray, out var before, 10f),
                Is.True,
                "No collider before the edit"
            );
            Assert.That(before.point.y, Is.EqualTo(top.y + 1f).Within(1e-3f));

            Assert.That(world.TrySetBlock(top, BlockId.Air), Is.True);
            Assert.That(world.DirtyCount, Is.GreaterThanOrEqualTo(2), "Neighbor not marked dirty");
            yield return null;
            yield return null;

            Assert.That(world.DirtyCount, Is.Zero, "Dirty queue not drained");
            Assert.That(
                Physics.Raycast(ray, out var after, 10f),
                Is.True,
                "No collider after the edit"
            );
            Assert.That(
                after.point.y,
                Is.EqualTo(top.y).Within(1e-3f),
                "Collider still has the removed block"
            );
            Assert.That(
                neighborChunk.RenderMesh.vertexCount,
                Is.Not.EqualTo(neighborVertices),
                "Neighbor chunk mesh did not change"
            );

            Assert.That(world.TrySetBlock(top, new BlockId(9)), Is.True);
            yield return null;
            yield return null;
            Assert.That(Physics.Raycast(ray, out var placed, 10f), Is.True);
            Assert.That(
                placed.point.y,
                Is.EqualTo(top.y + 1f).Within(1e-3f),
                "Placed block has no collider"
            );
            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        private static int3 TopSolid(VoxelWorld world, int3 column)
        {
            for (var y = WorldConstants.WorldHeight - 1; y >= 0; y--)
            {
                var pos = new int3(column.x, y, column.z);
                var block = world.GetBlock(pos);
                if (world.Registry.TryGet(block, out var type) && type.IsSolid)
                {
                    return pos;
                }
            }

            return new int3(column.x, -1, column.z);
        }

        private static IEnumerator LoadIsland()
        {
            var operation = SceneManager.LoadSceneAsync(
                GameBootstrap.IslandSceneName,
                LoadSceneMode.Single
            );
            yield return operation;
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail($"Timed out after {TimeoutSeconds} s waiting for {what}");
                }

                yield return null;
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Log)
            {
                _problems.Add($"{type}: {condition}");
            }
        }
    }
}
