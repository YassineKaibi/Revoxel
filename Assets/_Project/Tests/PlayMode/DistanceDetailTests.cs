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
    public sealed class DistanceDetailTests
    {
        private const float TimeoutSeconds = 240f;
        private const float ColumnMeters = WorldConstants.ChunkSize * WorldConstants.VoxelSize;

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
        public IEnumerator Detail_Settles_CoversViewExactlyOnce()
        {
            DistanceDetail detail = null;
            yield return LoadIsland((_, d) => detail = d);
            yield return WaitFor(() => detail.IsSettled, "the distance detail to settle");

            var rings = detail.Rings;
            foreach (
                var column in Samples(
                    rings.Min(rings.Levels) << rings.Levels,
                    rings.Max(rings.Levels) << rings.Levels,
                    rings
                )
            )
            {
                Assert.That(detail.CoverageAt(column), Is.EqualTo(1), $"column {column}");
            }

            var stats = detail.Stats;
            Assert.That(stats.Views, Is.GreaterThan(0));
            Assert.That(stats.ShownCells, Is.GreaterThan(0));
            Assert.That(stats.MeshBytes, Is.GreaterThan(0));
            Assert.That(
                Camera.main.farClipPlane,
                Is.GreaterThanOrEqualTo(detail.ViewDistanceMeters)
            );
            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        [UnityTest]
        public IEnumerator Detail_FocusWalk_NoHolesNoOverlaps()
        {
            DistanceDetail detail = null;
            yield return LoadIsland((_, d) => detail = d);
            yield return WaitFor(() => detail.IsSettled, "the distance detail to settle");

            var rings = detail.Rings;
            var samples = new List<int2>(
                Samples(new int2(-1200, -1200), new int2(1400, 1200), rings)
            );
            var covered = new bool[samples.Count];
            Check(detail, samples, covered, true, "settled");

            for (var frame = 0; frame < 80; frame++)
            {
                _focus.transform.position += new Vector3(ColumnMeters, 0f, 0f);
                yield return null;
                Check(detail, samples, covered, false, $"walk frame {frame}");
            }

            _focus.transform.position += new Vector3(40 * ColumnMeters, 0f, 0f);
            var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            var frames = 0;
            do
            {
                yield return null;
                Check(detail, samples, covered, false, $"teleport frame {frames++}");
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("Timed out waiting for the detail to settle after the teleport");
                }
            } while (!detail.IsSettled);

            foreach (
                var column in Samples(
                    rings.Min(rings.Levels) << rings.Levels,
                    rings.Max(rings.Levels) << rings.Levels,
                    rings
                )
            )
            {
                Assert.That(
                    detail.CoverageAt(column),
                    Is.EqualTo(1),
                    $"column {column} after settle"
                );
            }

            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        // After a move, the column under the focus is drawn at full resolution soon after it
        // is ready: coarse cells refine one level at a time instead of waiting for their
        // whole area (about 160 frames before that change, 10 to 30 after it, in batch mode).
        [UnityTest]
        public IEnumerator Detail_AfterMove_FocusColumnShowsSoonAfterReady()
        {
            const int MaxFramesAfterReady = 60;
            VoxelWorld world = null;
            DistanceDetail detail = null;
            yield return LoadIsland((w, d) => (world, detail) = (w, d));
            yield return WaitFor(() => detail.IsSettled, "the distance detail to settle");

            foreach (var move in new[] { 50, -50, 120 })
            {
                var column =
                    Coords.ColumnOf(
                        Coords.ChunkOf(Coords.WorldPositionToVoxel(_focus.transform.position))
                    ) + new int2(move, 0);
                _focus.transform.position = new Vector3(
                    (column.x + 0.5f) * ColumnMeters,
                    100f,
                    (column.y + 0.5f) * ColumnMeters
                );
                var readyFrame = -1;
                var frame = 0;
                var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (!world.IsColumnShown(column))
                {
                    yield return null;
                    frame++;
                    if (readyFrame < 0 && world.IsColumnReady(column))
                    {
                        readyFrame = frame;
                    }

                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Assert.Fail($"Timed out waiting for column {column} to show");
                    }
                }

                var waited = frame - math.max(readyFrame, 0);
                Assert.That(
                    waited,
                    Is.LessThanOrEqualTo(MaxFramesAfterReady),
                    $"move {move}: column {column} ready at frame {readyFrame}, shown at {frame}"
                );
                yield return WaitFor(() => detail.IsSettled, "the detail to settle after a move");
            }

            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        [UnityTest]
        public IEnumerator Detail_DestroyWhileGenerating_NoLeaks()
        {
            yield return UnloadAllScenes();
            var leaksBefore = UnsafeUtility.CheckForLeaks();
            VoxelWorld world = null;
            DistanceDetail detail = null;
            yield return LoadIsland((w, d) => (world, detail) = (w, d));
            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }

            _focus.transform.position += new Vector3(300 * ColumnMeters, 0f, 0f);
            yield return null;
            yield return null;
            Assert.That(detail.Stats.PendingJobs, Is.GreaterThan(0));

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

        // No sampled column is drawn twice; a column drawn last frame and still inside the
        // view is drawn this frame.
        private static void Check(
            DistanceDetail detail,
            List<int2> samples,
            bool[] covered,
            bool first,
            string when
        )
        {
            var rings = detail.Rings;
            var min = rings.Min(rings.Levels) << rings.Levels;
            var max = rings.Max(rings.Levels) << rings.Levels;
            for (var i = 0; i < samples.Count; i++)
            {
                var column = samples[i];
                var coverage = detail.CoverageAt(column);
                Assert.That(
                    coverage,
                    Is.LessThanOrEqualTo(1),
                    $"{when}: column {column} drawn twice"
                );
                var inView = math.all(column >= min) && math.all(column < max);
                if (!first && covered[i] && inView)
                {
                    Assert.That(coverage, Is.EqualTo(1), $"{when}: column {column} went empty");
                }

                covered[i] = coverage == 1;
            }
        }

        // A grid every 23 columns plus every column of the row z = 5 and along each level's
        // square edges.
        private static IEnumerable<int2> Samples(int2 min, int2 max, DetailRings rings)
        {
            for (var z = min.y; z < max.y; z += 23)
            for (var x = min.x; x < max.x; x += 23)
            {
                yield return new int2(x, z);
            }

            for (var x = min.x; x < max.x; x++)
            {
                yield return new int2(x, 5);
            }

            for (var level = 0; level < rings.Levels; level++)
            {
                var lmin = rings.Min(level) << level;
                var lmax = rings.Max(level) << level;
                for (var t = lmin.x - 1; t <= lmax.x; t++)
                {
                    yield return new int2(t, lmin.y - 1);
                    yield return new int2(t, lmin.y);
                    yield return new int2(t, lmax.y - 1);
                    yield return new int2(t, lmax.y);
                }

                for (var t = lmin.y - 1; t <= lmax.y; t++)
                {
                    yield return new int2(lmin.x - 1, t);
                    yield return new int2(lmin.x, t);
                    yield return new int2(lmax.x - 1, t);
                    yield return new int2(lmax.x, t);
                }
            }
        }

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

        private IEnumerator LoadIsland(System.Action<VoxelWorld, DistanceDetail> found)
        {
            yield return SceneManager.LoadSceneAsync(
                GameBootstrap.IslandSceneName,
                LoadSceneMode.Single
            );
            var world = Object.FindAnyObjectByType<VoxelWorld>();
            Assert.That(world, Is.Not.Null, "No VoxelWorld in Island");
            var detail = world.GetComponent<DistanceDetail>();
            Assert.That(detail, Is.Not.Null, "No DistanceDetail on the VoxelWorld");
            _focus = new GameObject("TestFocus");
            _focus.transform.position = new Vector3(0.5f, 100f, 0.5f);
            world.Focus = _focus.transform;
            found(world, detail);
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
