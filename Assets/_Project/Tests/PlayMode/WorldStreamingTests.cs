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
    public sealed class WorldStreamingTests
    {
        private const float TimeoutSeconds = 180f;
        private const float ChunkMeters = WorldConstants.ChunkSize * WorldConstants.VoxelSize;

        private readonly List<string> _problems = new List<string>();
        private NativeLeakDetectionMode _previousMode;
        private GameObject _focus;

        [SetUp]
        public void SetUp()
        {
            _problems.Clear();
            Application.logMessageReceived += OnLog;
            _previousMode = NativeLeakDetection.Mode;
            NativeLeakDetection.Mode = NativeLeakDetectionMode.Enabled;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            NativeLeakDetection.Mode = _previousMode;
            if (_focus != null)
            {
                Object.Destroy(_focus);
            }
        }

        [UnityTest]
        public IEnumerator World_Streaming_LoadsAroundFocusAndUnloadsBehind()
        {
            yield return UnloadAllScenes();
            var leaksBefore = UnsafeUtility.CheckForLeaks();
            VoxelWorld world = null;
            yield return LoadIslandWithFocus(w => world = w);
            yield return WaitFor(() => world.IsSettled, "the world to settle at the origin");

            var stats = world.Stats;
            Assert.That(stats.LoadedChunks, Is.GreaterThan(0));
            Assert.That(stats.ViewCount, Is.GreaterThan(0));
            Assert.That(world.IsColumnLoaded(int2.zero), Is.True);
            Assert.That(HasView(world, int2.zero), Is.True, "focus column not meshed");
            Assert.That(stats.MeshedWithMissingNeighbors, Is.Zero);

            var settings = world.StreamingSettings;
            _focus.transform.position += new Vector3(40 * ChunkMeters, 0f, 0f);
            yield return null;
            yield return WaitFor(() => world.IsSettled, "the world to settle after the move");

            var focus = new int2(40, 0);
            var limit = settings.LoadRadiusChunks + settings.UnloadHysteresisChunks;
            foreach (var column in world.LoadedColumns())
            {
                var d = column - focus;
                Assert.That(
                    d.x * d.x + d.y * d.y,
                    Is.LessThanOrEqualTo(limit * limit),
                    $"{column} still loaded"
                );
            }

            Assert.That(world.IsColumnLoaded(int2.zero), Is.False);
            Assert.That(HasView(world, focus), Is.True, "new focus column not meshed");

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

        [UnityTest]
        public IEnumerator World_Streaming_FastFocus_NoSeamsNoErrors()
        {
            VoxelWorld world = null;
            yield return LoadIslandWithFocus(w => world = w);
            yield return null;

            for (var i = 0; i < 5; i++)
            {
                _focus.transform.position += new Vector3(40 * ChunkMeters, 0f, 0f);
                yield return null;
            }

            yield return WaitFor(() => world.IsSettled, "the world to settle after teleports");

            Assert.That(world.Stats.MeshedWithMissingNeighbors, Is.Zero);
            Assert.That(HasView(world, new int2(200, 0)), Is.True, "final focus column not meshed");
            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        [UnityTest]
        public IEnumerator World_DestroyWhileStreaming_NoLeaks()
        {
            yield return UnloadAllScenes();
            var leaksBefore = UnsafeUtility.CheckForLeaks();
            VoxelWorld world = null;
            yield return LoadIslandWithFocus(w => world = w);
            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }

            _focus.transform.position += new Vector3(40 * ChunkMeters, 0f, 0f);
            yield return null;
            Assert.That(
                world.Stats.PendingGeneration + world.Stats.PendingMeshing,
                Is.GreaterThan(0)
            );

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

        [UnityTest]
        public IEnumerator World_EditAtChunkBorder_UpdatesMeshAndCollider()
        {
            VoxelWorld world = null;
            yield return LoadIslandWithFocus(w => world = w);
            yield return WaitFor(() => world.IsSettled, "the world to settle");

            // The surface on the chunk border x = 0 (voxel -1 in chunk -1, voxel 0 in chunk 0).
            const int z = 5;
            var top = math.max(TopSolid(world, -1, z), TopSolid(world, 0, z));
            Assert.That(top, Is.GreaterThan(0), "No solid voxel in the test columns");
            var surface = (top + 1) * WorldConstants.VoxelSize;
            var center = new float3(0f, surface, (z + 0.5f) * WorldConstants.VoxelSize);
            Assert.That(
                world.TryGetChunk(Coords.ChunkOf(new int3(-1, top, z)), out var left),
                Is.True
            );
            Assert.That(
                world.TryGetChunk(Coords.ChunkOf(new int3(0, top, z)), out var right),
                Is.True
            );
            var leftVersion = left.MeshVersion;
            var rightVersion = right.MeshVersion;
            var rayLeft = new Ray(new Vector3(-0.0625f, surface + 2f, center.z), Vector3.down);
            var rayRight = new Ray(new Vector3(0.0625f, surface + 2f, center.z), Vector3.down);
            Assert.That(
                Physics.Raycast(rayLeft, out var beforeLeft, 10f),
                Is.True,
                "no collider left"
            );
            Assert.That(
                Physics.Raycast(rayRight, out var beforeRight, 10f),
                Is.True,
                "no collider right"
            );

            // Remember the voxels the carve may change, to put them back.
            var saved = new List<(int3, VoxelId)>();
            for (var y = top - 4; y <= top + 4; y++)
            for (var dz = -4; dz <= 4; dz++)
            for (var x = -4; x <= 3; x++)
            {
                var v = new int3(x, y, z + dz);
                saved.Add((v, world.GetVoxel(v)));
            }

            Assert.That(world.CarveSphere(center, 0.25f, VoxelId.Air), Is.GreaterThan(0));
            yield return null;
            yield return null;

            Assert.That(left.MeshVersion, Is.GreaterThan(leftVersion), "left chunk mesh unchanged");
            Assert.That(
                right.MeshVersion,
                Is.GreaterThan(rightVersion),
                "right chunk mesh unchanged"
            );
            Assert.That(Physics.Raycast(rayLeft, out var afterLeft, 10f), Is.True);
            Assert.That(Physics.Raycast(rayRight, out var afterRight, 10f), Is.True);
            Assert.That(
                afterLeft.point.y,
                Is.LessThan(beforeLeft.point.y - 0.05f),
                "left collider not carved"
            );
            Assert.That(
                afterRight.point.y,
                Is.LessThan(beforeRight.point.y - 0.05f),
                "right collider not carved"
            );

            foreach (var (v, id) in saved)
            {
                world.TrySetVoxel(v, id);
            }

            yield return null;
            yield return null;
            Assert.That(Physics.Raycast(rayLeft, out var restoredLeft, 10f), Is.True);
            Assert.That(Physics.Raycast(rayRight, out var restoredRight, 10f), Is.True);
            Assert.That(restoredLeft.point.y, Is.EqualTo(beforeLeft.point.y).Within(1e-3f));
            Assert.That(restoredRight.point.y, Is.EqualTo(beforeRight.point.y).Within(1e-3f));
            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        // CheckForLeaks resets Unity's leak tracker; reading it while a previous test's world
        // still has jobs allocating on worker threads reports some of those allocations as
        // leaks. The baseline is taken with no world alive.
        private static IEnumerator UnloadAllScenes()
        {
            var empty = SceneManager.CreateScene("LeakBaseline");
            SceneManager.SetActiveScene(empty);
            for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
            }

            yield return null;
            yield return null;
        }

        private IEnumerator LoadIslandWithFocus(System.Action<VoxelWorld> found)
        {
            yield return SceneManager.LoadSceneAsync(
                GameBootstrap.IslandSceneName,
                LoadSceneMode.Single
            );
            var world = Object.FindAnyObjectByType<VoxelWorld>();
            Assert.That(world, Is.Not.Null, "No VoxelWorld in Island");
            _focus = new GameObject("TestFocus");
            _focus.transform.position = new Vector3(0.5f, 100f, 0.5f);
            world.Focus = _focus.transform;
            found(world);
        }

        private static bool HasView(VoxelWorld world, int2 column)
        {
            for (var cy = 0; cy < WorldConstants.ChunksY; cy++)
            {
                if (
                    world.TryGetChunk(new ChunkCoord(column.x, cy, column.y), out var chunk)
                    && chunk.View != null
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static int TopSolid(VoxelWorld world, int x, int z)
        {
            for (var y = WorldConstants.WorldHeightVoxels - 1; y >= 0; y--)
            {
                if (
                    world.Registry.TryGet(world.GetVoxel(new int3(x, y, z)), out var type)
                    && type.IsSolid
                )
                {
                    return y;
                }
            }

            return -1;
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
