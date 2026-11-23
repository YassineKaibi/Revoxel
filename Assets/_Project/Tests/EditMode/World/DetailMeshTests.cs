using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.Tests.EditMode
{
    public sealed class DetailMeshTests
    {
        private const float V = WorldConstants.VoxelSize;
        private const int WallDepth = 8;

        private readonly List<Object> _created = new List<Object>();
        private NativeArray<VoxelRenderData> _renderData;
        private bool _hitBackfaces;

        [SetUp]
        public void SetUp()
        {
            _renderData = WorldAssets.LoadRegistry().CreateRenderData(Allocator.Persistent);
            _hitBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = false;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
            {
                Object.DestroyImmediate(obj);
            }

            _created.Clear();
            _renderData.Dispose();
            Physics.queriesHitBackfaces = _hitBackfaces;
        }

        [Test]
        public void GreedyMesh_SkipCollision_WritesNoCollision()
        {
            using var padded = new NativeArray<ushort>(
                WorldConstants.PaddedVoxels,
                Allocator.TempJob
            );
            var write = padded;
            write[Coords.PaddedIndex(3, 4, 5)] = WorldAssets.LoadTerrainSettings().Stone.Id.Value;
            foreach (var skip in new[] { false, true })
            {
                var meshes = Mesh.AllocateWritableMeshData(2);
                try
                {
                    RunMesh(padded, meshes, skip);
                    Assert.That(meshes[0].vertexCount, Is.EqualTo(24), $"skip {skip}");
                    Assert.That(meshes[1].vertexCount, Is.EqualTo(skip ? 0 : 24), $"skip {skip}");
                    Assert.That(meshes[1].GetSubMesh(0).indexCount, Is.EqualTo(skip ? 0 : 36));
                }
                finally
                {
                    meshes.Dispose();
                }
            }
        }

        // A 2 x 2 block of fine cells on the -x side of x = 0 and coarse cells on the +x side,
        // meshed as the game does (level 0 like VoxelWorld: real padding, no walls). Rays from
        // cameras above the fine side to surface points across the border must all land on
        // the terrain surface, never slip under it through a gap.
        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(3, 4)]
        [TestCase(5, 6)]
        public void DetailMesh_LevelBorder_NoCracks(int fine, int coarse)
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            BuildBorder(p, fine, coarse, true);
            var failures = ProbeBorder(p, fine, coarse);

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        // The probe of the test above finds the gaps when coarse chunks draw no walls, so its
        // passing means something.
        [Test]
        public void DetailMesh_WithoutWalls_ProbeFindsGaps()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var found = 0;
            foreach (var (fine, coarse) in new[] { (0, 1), (1, 2), (3, 4), (5, 6) })
            {
                BuildBorder(p, fine, coarse, false);
                found += ProbeBorder(p, fine, coarse).Count;
                foreach (var obj in _created)
                {
                    Object.DestroyImmediate(obj);
                }

                _created.Clear();
            }

            Assert.That(found, Is.GreaterThan(0));
        }

        [Test]
        public void DetailMesh_CoarseViewMatchesWorldScale()
        {
            var parent = new GameObject("DetailMeshTests");
            _created.Add(parent);
            var pool = new DetailViewPool(parent.transform, null);
            try
            {
                var coord = new ChunkCoord(1, 2, -1);
                var view = pool.Get(3, coord);
                view.Mesh.bounds = ChunkMesher.ChunkLocalBounds;
                var expectedMin = new Vector3(1f, 2f, -1f) * (32f * 8f * V);

                Assert.That(view.GameObject.transform.position, Is.EqualTo(expectedMin));
                Assert.That(view.GameObject.transform.lossyScale, Is.EqualTo(Vector3.one * 8f));
                Assert.That(view.Renderer.enabled, Is.False, "views start hidden");
                var t = view.GameObject.transform;
                var max = t.TransformPoint(view.Mesh.bounds.max);
                Assert.That(t.TransformPoint(view.Mesh.bounds.min), Is.EqualTo(expectedMin));
                Assert.That(max - expectedMin, Is.EqualTo(Vector3.one * (32f * 8f * V)));
            }
            finally
            {
                pool.Dispose();
            }
        }

        private void BuildBorder(in TerrainParams p, int fine, int coarse, bool coarseWalls)
        {
            for (var cz = -1; cz <= 0; cz++)
            {
                for (var cx = -2; cx <= -1; cx++)
                {
                    BuildCell(p, fine, new int2(cx, cz), fine > 0);
                }

                for (var cx = 0; cx <= 1; cx++)
                {
                    BuildCell(p, coarse, new int2(cx, cz), coarseWalls);
                }
            }

            Physics.SyncTransforms();
        }

        private List<string> ProbeBorder(in TerrainParams p, int fine, int coarse)
        {
            var failures = new List<string>();
            var fineWidth = 4f * (1 << fine);
            var coarseWidth = 2f * fineWidth;
            var coarseVoxel = (1 << coarse) * V;
            var tolerance = 2f * coarseVoxel + 0.05f;
            foreach (var camX in new[] { -0.5f * fineWidth, -fineWidth })
            foreach (var camZ in new[] { -0.4f * fineWidth, 0.3f * fineWidth })
            foreach (var lift in new[] { math.max(1.7f, 0.1f * fineWidth), 0.5f * fineWidth + 2f })
            {
                var camera = new Vector3(camX, SurfaceMeters(p, camX, camZ) + lift, camZ);
                foreach (var tx in new[] { -0.2f, -0.05f, 0.02f, 0.1f, 0.25f, 0.5f })
                foreach (var tz in new[] { -0.5f, 0f, 0.5f })
                {
                    var x = tx * (tx < 0f ? fineWidth : coarseWidth);
                    var z = tz * fineWidth;
                    var target = new Vector3(x, SurfaceMeters(p, x, z) - coarseVoxel, z);
                    var ray = target - camera;
                    var what = $"camera {camera}, target {target}";
                    if (
                        !Physics.Raycast(
                            camera,
                            ray.normalized,
                            out var hit,
                            ray.magnitude + 2f * coarseWidth
                        )
                    )
                    {
                        failures.Add($"{what}: no hit");
                        continue;
                    }

                    var surface = SurfaceMeters(p, hit.point.x, hit.point.z);
                    if (hit.point.y < surface - tolerance)
                    {
                        failures.Add($"{what}: hit {hit.point} under the surface {surface}");
                    }
                }
            }

            return failures;
        }

        private void BuildCell(in TerrainParams p, int level, int2 cell, bool walls)
        {
            var range = TerrainGenerator.DetailHeightRange(p, cell, level);
            using var samples = new NativeArray<TerrainSample>(
                TerrainGenerator.DetailSampleGridCount,
                Allocator.TempJob
            );
            using var padded = new NativeArray<ushort>(
                WorldConstants.PaddedVoxels,
                Allocator.TempJob
            );
            for (var cy = 0; cy < WorldConstants.ChunksY >> level; cy++)
            {
                if (!TerrainGenerator.DetailChunkNeedsMesh(p, range, cy, level, WallDepth))
                {
                    continue;
                }

                var coord = new ChunkCoord(cell.x, cy, cell.y);
                new DetailFillJob
                {
                    Params = p,
                    Level = level,
                    Coord = coord,
                    SideWalls = walls,
                    Samples = samples,
                    Padded = padded,
                }.Run();
                var meshes = Mesh.AllocateWritableMeshData(2);
                RunMesh(padded, meshes, true);
                if (meshes[0].vertexCount == 0)
                {
                    meshes.Dispose();
                    continue;
                }

                var mesh = new Mesh();
                var collision = new Mesh();
                _created.Add(mesh);
                _created.Add(collision);
                Mesh.ApplyAndDisposeWritableMeshData(
                    meshes,
                    new[] { mesh, collision },
                    MeshUpdateFlags.DontRecalculateBounds
                );
                mesh.bounds = ChunkMesher.ChunkLocalBounds;
                var go = new GameObject($"L{level} {coord}");
                _created.Add(go);
                var origin = DetailViewPool.Origin(level, coord);
                go.transform.position = new Vector3(origin.x, origin.y, origin.z);
                go.transform.localScale = Vector3.one * (1 << level);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
        }

        private void RunMesh(NativeArray<ushort> padded, Mesh.MeshDataArray meshes, bool skip)
        {
            using var renderLayout = ChunkMesher.CreateRenderLayout(Allocator.TempJob);
            using var collisionLayout = ChunkMesher.CreateCollisionLayout(Allocator.TempJob);
            new GreedyMeshJob
            {
                Padded = padded,
                RenderData = _renderData,
                RenderLayout = renderLayout,
                CollisionLayout = collisionLayout,
                RenderMesh = meshes[0],
                CollisionMesh = meshes[1],
                SkipCollision = skip,
            }.Run();
        }

        // The continuous surface height at a position, meters.
        private static float SurfaceMeters(in TerrainParams p, float x, float z) =>
            TerrainGenerator.SmoothHeight(p, (int)math.floor(x / V), (int)math.floor(z / V)) * V;
    }
}
