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
            var detail = world.GetComponent<DistanceDetail>();
            _focus.transform.position += new Vector3(40 * ChunkMeters, 0f, 0f);
            yield return null;
            yield return WaitFor(
                () => world.IsSettled && (detail == null || detail.IsSettled),
                "the world to settle after the move"
            );

            // The full-detail square plus its margin column and the hysteresis (D-057).
            // Columns the distance detail just hid unload over the next frames.
            var focus = new int2(40, 0);
            var center = world.FullDetailCenter;
            var limit = settings.LoadRadiusChunks + 1 + settings.UnloadHysteresisChunks;
            yield return WaitFor(
                () => AllLoadedWithin(world, center, limit),
                "hidden columns to unload"
            );
            foreach (var column in world.LoadedColumns())
            {
                Assert.That(
                    math.all(column >= center - limit) && math.all(column < center + limit),
                    Is.True,
                    $"{column} still loaded, center {center}"
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
            yield return WaitFor(() => world.IsStreaming, "the water plan");
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

        // Task 5b (D-077): the water plan builds in the background. No frame from the load
        // on takes 100 ms more than the median of the frames while it builds; it is ready
        // within 5 s, and streaming (and the loading progress) waits for it.
        [UnityTest]
        public IEnumerator World_BuildsTheWaterPlanWithoutBlocking()
        {
            var frames = new List<float>();
            var load = SceneManager.LoadSceneAsync(
                GameBootstrap.IslandSceneName,
                LoadSceneMode.Single
            );
            var last = Time.realtimeSinceStartup;
            while (!load.isDone)
            {
                yield return null;
            }

            var world = Object.FindAnyObjectByType<VoxelWorld>();
            Assert.That(world, Is.Not.Null, "No VoxelWorld in Island");
            var start = Time.realtimeSinceStartup;
            last = start;
            while (!world.IsWaterPlanReady)
            {
                Assert.That(world.IsStreaming, Is.False, "streaming before the plan");
                Assert.That(world.LoadProgress, Is.Zero, "progress before the plan");
                Assert.That(
                    Time.realtimeSinceStartup - start,
                    Is.LessThan(5f),
                    "the plan within 5 s"
                );
                yield return null;
                var now = Time.realtimeSinceStartup;
                frames.Add(now - last);
                last = now;
            }

            yield return null;
            Assert.That(world.IsStreaming, Is.True);
            if (frames.Count >= 3)
            {
                var sorted = new List<float>(frames);
                sorted.Sort();
                var median = sorted[sorted.Count / 2];
                Assert.That(
                    sorted[sorted.Count - 1] - median,
                    Is.LessThan(0.1f),
                    $"frames {string.Join(", ", frames)}"
                );
            }

            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        // A query before the plan is built waits for it and answers as after.
        [UnityTest]
        public IEnumerator World_QueryBeforePlanReady_Completes()
        {
            yield return SceneManager.LoadSceneAsync(
                GameBootstrap.IslandSceneName,
                LoadSceneMode.Single
            );
            var world = Object.FindAnyObjectByType<VoxelWorld>();
            Assert.That(world, Is.Not.Null, "No VoxelWorld in Island");
            yield return null;
            var points = new[] { new int2(0, 0), new int2(4000, -2500), new int2(-9000, 6000) };
            var before = new float[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                Assert.That(
                    world.TryGetSurfaceHeightMeters(points[i].x, points[i].y, out before[i]),
                    Is.True
                );
            }

            Assert.That(world.IsWaterPlanReady, Is.True, "the query completed the build");
            yield return WaitFor(() => world.IsStreaming, "streaming");
            for (var i = 0; i < points.Length; i++)
            {
                Assert.That(
                    world.TryGetSurfaceHeightMeters(points[i].x, points[i].y, out var after),
                    Is.True
                );
                Assert.That(after, Is.EqualTo(before[i]), $"{points[i]}");
            }

            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        // Destroying the world while its plan builds waits for the jobs and frees them.
        [UnityTest]
        public IEnumerator World_DestroyDuringBuild_NoLeaks()
        {
            yield return UnloadAllScenes();
            var leaksBefore = UnsafeUtility.CheckForLeaks();
            yield return SceneManager.LoadSceneAsync(
                GameBootstrap.IslandSceneName,
                LoadSceneMode.Single
            );
            var world = Object.FindAnyObjectByType<VoxelWorld>();
            Assert.That(world, Is.Not.Null, "No VoxelWorld in Island");
            yield return null;
            Assert.That(world.IsWaterPlanReady, Is.False, "the plan is still building");
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

        // PLAN-04 Task 4: the world's biome query is the generator's, and its biome resolves
        // to a Biome asset.
        [UnityTest]
        public IEnumerator World_BiomeQuery_MatchesGenerator()
        {
            yield return UnloadAllScenes();
            VoxelWorld world = null;
            yield return LoadIslandWithFocus(w => world = w);
            yield return null;

            foreach (
                var position in new[]
                {
                    new float3(0.5f, 80f, 0.5f),
                    new float3(-1234.5f, 90f, 2345.5f),
                }
            )
            {
                Assert.That(world.TrySampleBiome(position, out var sample), Is.True);
                var voxel = Coords.WorldPositionToVoxel(position);
                var expected = TerrainGenerator.Sample(world.TerrainParameters, voxel.x, voxel.z);

                Assert.That(sample.Biome, Is.EqualTo(expected.Biome));
                Assert.That(sample.Height, Is.EqualTo(expected.Height));
                Assert.That(world.TryGetBiome(sample.Biome, out var biome), Is.True);
                Assert.That(biome.Id, Is.EqualTo(sample.Biome));
            }

            Assert.That(world.TryGetBiome(new BiomeId(200), out _), Is.False);
            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        // PLAN-05 Task 4 (D-074): over loaded ground, the voxel water query agrees with the
        // generated water: in water below the level with the surface at it, dry above. A
        // lake column is used (lake levels are exact, so Burst and managed agree).
        [UnityTest]
        public IEnumerator World_WaterQuery_MatchesGenerator()
        {
            yield return UnloadAllScenes();
            VoxelWorld world = null;
            yield return LoadIslandWithFocus(w => world = w);
            yield return WaitFor(() => world.IsSettled, "the world to settle");

            // Around the deepest cell of the plan's first lake (D-081).
            Assert.That(world.TerrainParameters.WaterPlan.Lakes.Length, Is.GreaterThan(0));
            var lake = (int2)math.floor(world.TerrainParameters.WaterPlan.Lakes[0].DeepestPoint);
            var found = false;
            var at = int2.zero;
            var column = default(WaterColumn);
            const int searchMeters = 80;
            for (var z = -searchMeters; z <= searchMeters && !found; z += 2)
            for (var x = -searchMeters; x <= searchMeters && !found; x += 2)
            {
                var vx = lake.x + x * WorldConstants.VoxelsPerMeter;
                var vz = lake.y + z * WorldConstants.VoxelsPerMeter;
                Assert.That(world.TryGetWaterColumn(vx, vz, out var c), Is.True);
                if (c.IsWet && c.Body == WaterBody.Lake && c.DepthMeters > 1f)
                {
                    found = true;
                    at = new int2(vx, vz);
                    column = c;
                }
            }

            Assert.That(
                found,
                Is.True,
                "no lake water within 80 m of the first lake's deepest cell"
            );
            Assert.That(
                column.Level,
                Is.EqualTo(WaterMap.Column(world.TerrainParameters, at.x, at.y).Level)
            );
            // Voxel samples need the lake's chunks: stream there first.
            _focus.transform.position = new Vector3(
                (at.x + 0.5f) * WorldConstants.VoxelSize,
                column.SurfaceMeters + 2f,
                (at.y + 0.5f) * WorldConstants.VoxelSize
            );
            yield return null;
            yield return WaitFor(() => world.IsSettled, "the world to settle at the lake");
            var below = new float3(
                (at.x + 0.5f) * WorldConstants.VoxelSize,
                (column.Level - 4) * WorldConstants.VoxelSize,
                (at.y + 0.5f) * WorldConstants.VoxelSize
            );
            Assert.That(world.TrySampleWater(below, out var water), Is.True);
            Assert.That(water.InWater, Is.True);
            Assert.That(water.Body, Is.EqualTo(WaterBody.Lake));
            Assert.That(water.SurfaceMeters, Is.EqualTo(column.SurfaceMeters).Within(1e-4f));
            Assert.That(water.DepthMeters, Is.EqualTo(4 * WorldConstants.VoxelSize).Within(1e-4f));

            var above = below + new float3(0f, 1f, 0f);
            Assert.That(world.TrySampleWater(above, out var air), Is.True);
            Assert.That(air.InWater, Is.False);
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

        private static bool AllLoadedWithin(VoxelWorld world, int2 center, int limit)
        {
            foreach (var column in world.LoadedColumns())
            {
                if (math.any(column < center - limit) || math.any(column >= center + limit))
                {
                    return false;
                }
            }

            return true;
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
