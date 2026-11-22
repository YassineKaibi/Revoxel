using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.Core;
using Revoxel.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Revoxel.Tests.PlayMode
{
    public sealed class DebugToolsTests
    {
        private const float TimeoutSeconds = 180f;
        private const float VoxelSize = WorldConstants.VoxelSize;

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
        public IEnumerator DebugCamera_Walking_StandsOnTerrainAtEyeHeight()
        {
            VoxelWorld world = null;
            DebugFlyCamera flyCamera = null;
            yield return LoadIsland((w, c) => (world, flyCamera) = (w, c));

            Assert.That(
                flyCamera.GetComponent<Camera>().fieldOfView,
                Is.EqualTo(
                        Camera.HorizontalToVerticalFieldOfView(
                            flyCamera.HorizontalFov,
                            flyCamera.GetComponent<Camera>().aspect
                        )
                    )
                    .Within(0.01f),
                "horizontal field of view not applied"
            );

            flyCamera.SetWalking(true);
            yield return WaitFor(() => flyCamera.IsGrounded, "the body to land");
            for (var i = 0; i < 30; i++)
            {
                yield return null;
            }

            // The capsule rests on the highest voxel under its circle.
            var p = flyCamera.transform.position;
            var cx = (int)math.floor(p.x / VoxelSize);
            var cz = (int)math.floor(p.z / VoxelSize);
            var lowest = int.MaxValue;
            var highest = int.MinValue;
            for (var dz = -3; dz <= 3; dz++)
            for (var dx = -3; dx <= 3; dx++)
            {
                var top = TopSolid(world, cx + dx, cz + dz);
                lowest = math.min(lowest, top);
                highest = math.max(highest, top);
            }

            Assert.That(lowest, Is.GreaterThan(0), "no solid voxel under the camera");
            var feet = p.y - flyCamera.EyeHeight;
            Assert.That(feet, Is.GreaterThanOrEqualTo((lowest + 1) * VoxelSize - 0.05f));
            Assert.That(feet, Is.LessThanOrEqualTo((highest + 1) * VoxelSize + 0.1f));
            Assert.That(flyCamera.IsGrounded, Is.True);

            flyCamera.SetWalking(false);
            Assert.That(flyCamera.IsWalking, Is.False);
            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        [UnityTest]
        public IEnumerator DebugScaleFigure_StandsOnTheGroundWhereAimed()
        {
            VoxelWorld world = null;
            DebugFlyCamera flyCamera = null;
            yield return LoadIsland((w, c) => (world, flyCamera) = (w, c));
            var scale = flyCamera.GetComponent<DebugScaleFigure>();
            Assert.That(scale, Is.Not.Null, "no DebugScaleFigure on the camera");

            flyCamera.transform.rotation = Quaternion.Euler(60f, 30f, 0f);
            Assert.That(scale.PlaceAtAim(), Is.True, "nothing aimed at");
            var first = scale.Figure;
            var f = first.position;
            var cell = new int3(
                (int)math.floor(f.x / VoxelSize),
                (int)math.round(f.y / VoxelSize),
                (int)math.floor(f.z / VoxelSize)
            );
            Assert.That(world.GetVoxel(cell), Is.EqualTo(VoxelId.Air), "figure inside the ground");
            Assert.That(
                world.GetVoxel(cell - new int3(0, 1, 0)),
                Is.Not.EqualTo(VoxelId.Air),
                "figure floating"
            );
            Assert.That(
                Vector3.Distance(f, flyCamera.transform.position),
                Is.LessThan(10f),
                "figure far from the aimed ground"
            );

            flyCamera.transform.rotation = Quaternion.Euler(60f, 120f, 0f);
            Assert.That(scale.PlaceAtAim(), Is.True);
            Assert.That(scale.Figure, Is.SameAs(first), "second press made a new figure");
            Assert.That(scale.Figure.position, Is.Not.EqualTo(f), "figure did not move");
            Assert.That(_problems, Is.Empty, string.Join("\n", _problems));
        }

        private static IEnumerator LoadIsland(System.Action<VoxelWorld, DebugFlyCamera> found)
        {
            yield return SceneManager.LoadSceneAsync(
                GameBootstrap.IslandSceneName,
                LoadSceneMode.Single
            );
            var world = Object.FindAnyObjectByType<VoxelWorld>();
            var flyCamera = Object.FindAnyObjectByType<DebugFlyCamera>();
            Assert.That(world, Is.Not.Null, "No VoxelWorld in Island");
            Assert.That(flyCamera, Is.Not.Null, "No DebugFlyCamera in Island");
            world.Focus = flyCamera.transform;
            yield return null;
            yield return WaitFor(() => world.IsSettled, "the world to settle");
            found(world, flyCamera);
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
