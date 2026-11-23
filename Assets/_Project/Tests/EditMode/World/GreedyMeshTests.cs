using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.Tests.EditMode
{
    public sealed class GreedyMeshTests
    {
        private const float V = WorldConstants.VoxelSize;
        private MeshFixture _fixture;

        [SetUp]
        public void SetUp() => _fixture = new MeshFixture();

        [TearDown]
        public void TearDown() => _fixture.Dispose();

        [Test]
        public void Mesh_SingleVoxel_Has6Faces()
        {
            var r = _fixture.MeshChunk(p =>
                At(p, 16, 16, 16) ? MeshFixture.Stone : MeshFixture.Air
            );

            Assert.That(r.RenderVertices, Is.EqualTo(24));
            Assert.That(r.RenderIndices, Is.EqualTo(36));
            Assert.That(r.CollisionVertices, Is.EqualTo(24));
            Assert.That(r.CollisionIndices, Is.EqualTo(36));
            Assert.That(r.RenderIndexFormat, Is.EqualTo(IndexFormat.UInt16));
        }

        [Test]
        public void Mesh_TwoAdjacentSameType_MergeTo6Quads()
        {
            var r = _fixture.MeshChunk(p =>
                p.y == 16 && p.z == 16 && (p.x == 16 || p.x == 17)
                    ? MeshFixture.Stone
                    : MeshFixture.Air
            );

            Assert.That(r.RenderVertices, Is.EqualTo(24));
            Assert.That(r.CollisionVertices, Is.EqualTo(24));
        }

        [Test]
        public void Mesh_TwoAdjacentDifferentTypes_DoNotMerge()
        {
            var r = _fixture.MeshChunk(p =>
                p.y == 16 && p.z == 16 && p.x == 16 ? MeshFixture.Stone
                : p.y == 16 && p.z == 16 && p.x == 17 ? MeshFixture.Dirt
                : MeshFixture.Air
            );

            Assert.That(r.RenderVertices, Is.EqualTo(40));
            Assert.That(r.CollisionVertices, Is.EqualTo(24));
        }

        [Test]
        public void Mesh_FullChunk_NoNeighbors_Has6Quads()
        {
            var r = _fixture.MeshChunk(_ => MeshFixture.Stone);

            Assert.That(r.RenderVertices, Is.EqualTo(24));
            Assert.That(r.CollisionVertices, Is.EqualTo(24));
        }

        [Test]
        public void Mesh_FullChunk_SolidNeighbors_HasNoFaces()
        {
            var r = _fixture.MeshPadded(_ => MeshFixture.Stone);

            Assert.That(r.RenderVertices, Is.EqualTo(0));
            Assert.That(r.RenderIndices, Is.EqualTo(0));
            Assert.That(r.CollisionVertices, Is.EqualTo(0));
        }

        [Test]
        public void Mesh_EmptyChunk_HasNoFaces()
        {
            var r = _fixture.MeshPadded(_ => MeshFixture.Air);

            Assert.That(r.RenderVertices, Is.EqualTo(0));
            Assert.That(r.CollisionVertices, Is.EqualTo(0));
        }

        [Test]
        public void Mesh_Checkerboard_UsesUInt32()
        {
            var r = _fixture.MeshChunk(p =>
                (p.x + p.y + p.z) % 2 == 0 ? MeshFixture.Stone : MeshFixture.Air
            );

            Assert.That(r.RenderVertices, Is.EqualTo(393216));
            Assert.That(r.RenderIndices, Is.EqualTo(589824));
            Assert.That(r.RenderIndexFormat, Is.EqualTo(IndexFormat.UInt32));
            Assert.That(r.Indices[r.Indices.Length - 1], Is.LessThan(393216));
        }

        [Test]
        public void Mesh_WaterNextToWater_NoFaceBetween()
        {
            var r = _fixture.MeshChunk(p =>
                p.y == 0 && p.z == 0 && p.x <= 1 ? MeshFixture.Water : MeshFixture.Air
            );

            Assert.That(r.RenderVertices, Is.EqualTo(24));
            foreach (var vertex in r.Vertices)
            {
                Assert.That(vertex.Position.x, Is.Not.EqualTo(V).Within(1e-5f));
            }
        }

        [Test]
        public void Mesh_WaterNotInCollisionMesh()
        {
            var r = _fixture.MeshChunk(p => p.y < 4 ? MeshFixture.Water : MeshFixture.Air);

            Assert.That(r.RenderVertices, Is.GreaterThan(0));
            Assert.That(r.CollisionVertices, Is.EqualTo(0));
        }

        [Test]
        public void Mesh_FaceWinding_MatchesNormals()
        {
            // An L shape and a slab with a hole: faces in every direction, merged and not.
            var r = _fixture.MeshChunk(p =>
                (p.y == 3 && p.x < 20 && p.z < 20 && !(p.x == 5 && p.z == 5))
                || (p.x == 10 && p.z == 10 && p.y < 8)
                    ? MeshFixture.Stone
                    : MeshFixture.Air
            );

            Assert.That(r.RenderIndices % 3, Is.EqualTo(0));
            for (var t = 0; t < r.RenderIndices; t += 3)
            {
                var a = r.Vertices[r.Indices[t]];
                var b = r.Vertices[r.Indices[t + 1]];
                var c = r.Vertices[r.Indices[t + 2]];
                var cross = math.normalize(
                    math.cross(b.Position - a.Position, c.Position - a.Position)
                );

                Assert.That(math.dot(cross, a.Normal), Is.GreaterThan(0.99f), $"triangle {t / 3}");
                Assert.That(a.Normal, Is.EqualTo(b.Normal));
                Assert.That(a.Normal, Is.EqualTo(c.Normal));
            }
        }

        [Test]
        public void Mesh_FlatSurface_HasNoOcclusion()
        {
            var r = _fixture.MeshChunk(p => p.y == 0 ? MeshFixture.Stone : MeshFixture.Air);

            var top = 0;
            foreach (var vertex in r.Vertices)
            {
                if (vertex.Normal.y > 0.5f)
                {
                    top++;
                    Assert.That(vertex.MaterialAo.y, Is.EqualTo(1f));
                    Assert.That(vertex.MaterialAo.x, Is.EqualTo(MeshFixture.Stone));
                }
            }

            Assert.That(top, Is.EqualTo(4));
        }

        [Test]
        public void Mesh_InsideCorner_IsOccluded()
        {
            // Slab at y 0, a wall row two voxels high at y 1 and 2, z 16 on top of it.
            var r = _fixture.MeshChunk(p =>
                p.y == 0 || (p.y >= 1 && p.y <= 2 && p.z == 16)
                    ? MeshFixture.Stone
                    : MeshFixture.Air
            );

            var touching = 0;
            foreach (var vertex in r.Vertices)
            {
                var onSlabTop = vertex.Normal.y > 0.5f && math.abs(vertex.Position.y - V) < 1e-5f;
                var atWall =
                    math.abs(vertex.Position.z - 16 * V) < 1e-5f
                    || math.abs(vertex.Position.z - 17 * V) < 1e-5f;
                if (onSlabTop && atWall)
                {
                    touching++;
                    Assert.That(vertex.MaterialAo.y, Is.LessThan(1f), $"{vertex.Position}");
                }
            }

            Assert.That(touching, Is.GreaterThan(0));
        }

        // D-066: a one-voxel step (a row one voxel high on the slab) draws no dark line on
        // the slab next to it.
        [Test]
        public void Mesh_OneVoxelStep_IsNotOccluded()
        {
            var r = _fixture.MeshChunk(p =>
                p.y == 0 || (p.y == 1 && p.z == 16) ? MeshFixture.Stone : MeshFixture.Air
            );

            var touching = 0;
            foreach (var vertex in r.Vertices)
            {
                var onSlabTop = vertex.Normal.y > 0.5f && math.abs(vertex.Position.y - V) < 1e-5f;
                var atStep =
                    math.abs(vertex.Position.z - 16 * V) < 1e-5f
                    || math.abs(vertex.Position.z - 17 * V) < 1e-5f;
                if (onSlabTop && atStep)
                {
                    touching++;
                    Assert.That(vertex.MaterialAo.y, Is.EqualTo(1f), $"{vertex.Position}");
                }
            }

            Assert.That(touching, Is.GreaterThan(0));
        }

        // D-066: the side faces of a one-voxel step carry the step flag; those of a wall two
        // voxels high, and of a floating voxel, do not.
        [Test]
        public void Mesh_OneVoxelStep_SideFacesAreMarked()
        {
            var step = _fixture.MeshChunk(p =>
                p.y == 0 || (p.y == 1 && p.z == 16) ? MeshFixture.Stone : MeshFixture.Air
            );
            var wall = _fixture.MeshChunk(p =>
                p.y == 0 || (p.y >= 1 && p.y <= 2 && p.z == 16)
                    ? MeshFixture.Stone
                    : MeshFixture.Air
            );
            var single = _fixture.MeshChunk(p =>
                p.x == 16 && p.y == 16 && p.z == 16 ? MeshFixture.Stone : MeshFixture.Air
            );

            // The row's two long faces (toward -z and +z, from y 1 to 2 voxels) stand on the
            // slab; its end faces look at the Air padding, with no ground beyond them.
            var marked = 0;
            for (var q = 0; q < step.Vertices.Length; q += 4)
            {
                var first = step.Vertices[q];
                var minY = math.min(
                    math.min(first.Position.y, step.Vertices[q + 1].Position.y),
                    math.min(step.Vertices[q + 2].Position.y, step.Vertices[q + 3].Position.y)
                );
                var riser = math.abs(first.Normal.z) > 0.5f && minY > V - 1e-5f;
                var expected = MeshFixture.Stone + (riser ? GreedyMeshJob.StepFlag : 0);
                for (var c = 0; c < 4; c++)
                {
                    var vertex = step.Vertices[q + c];
                    Assert.That(vertex.MaterialAo.x, Is.EqualTo(expected), $"{vertex.Position}");
                }

                marked += riser ? 1 : 0;
            }

            // Two long faces, split where the chunk's end columns change their occlusion.
            Assert.That(marked, Is.GreaterThanOrEqualTo(2));
            foreach (var vertex in wall.Vertices)
            {
                Assert.That(vertex.MaterialAo.x, Is.EqualTo(MeshFixture.Stone), "two-voxel wall");
            }

            foreach (var vertex in single.Vertices)
            {
                Assert.That(vertex.MaterialAo.x, Is.EqualTo(MeshFixture.Stone), "floating voxel");
            }
        }

        [Test]
        public void Mesh_OutsideCorner_NotOccluded()
        {
            // Upper level two voxels high for z below 16, lower level one voxel high beyond.
            var r = _fixture.MeshChunk(p =>
                p.y == 0 || (p.y == 1 && p.z < 16) ? MeshFixture.Stone : MeshFixture.Air
            );

            var edge = 0;
            foreach (var vertex in r.Vertices)
            {
                var onUpperTop =
                    vertex.Normal.y > 0.5f && math.abs(vertex.Position.y - 2 * V) < 1e-5f;
                if (onUpperTop && math.abs(vertex.Position.z - 16 * V) < 1e-5f)
                {
                    edge++;
                    Assert.That(vertex.MaterialAo.y, Is.EqualTo(1f), $"{vertex.Position}");
                }
            }

            Assert.That(edge, Is.GreaterThan(0));
        }

        [Test]
        public void Pad_CopiesEdgeAndCornerNeighbors()
        {
            var stone = new VoxelId(MeshFixture.Stone);
            var dirt = new VoxelId(MeshFixture.Dirt);
            using var neighborhood = new NativeArray<ChunkVoxels>(27, Allocator.TempJob);
            using var padded = new NativeArray<ushort>(
                WorldConstants.PaddedVoxels,
                Allocator.TempJob
            );
            var n = neighborhood;
            for (var i = 0; i < 27; i++)
            {
                n[i] = ChunkVoxels.CreateUniform(VoxelId.Air, Allocator.Persistent);
            }

            try
            {
                n[Neighbor(1, 1, 1)] = ChunkVoxels.CreateUniform(stone, Allocator.Persistent);
                n[Neighbor(1, 0, 0)] = ChunkVoxels.CreateUniform(dirt, Allocator.Persistent);
                var center = n[Neighbor(0, 0, 0)];
                center.Set(Coords.LocalIndex(3, 4, 5), stone);
                n[Neighbor(0, 0, 0)] = center;

                new ChunkPadJob { Neighborhood = neighborhood, Padded = padded }.Run();

                Assert.That(padded[Coords.PaddedIndex(32, 32, 32)], Is.EqualTo(MeshFixture.Stone));
                Assert.That(padded[Coords.PaddedIndex(32, 5, 7)], Is.EqualTo(MeshFixture.Dirt));
                Assert.That(padded[Coords.PaddedIndex(32, 0, 31)], Is.EqualTo(MeshFixture.Dirt));
                Assert.That(padded[Coords.PaddedIndex(32, 32, 5)], Is.EqualTo(MeshFixture.Air));
                Assert.That(padded[Coords.PaddedIndex(-1, -1, -1)], Is.EqualTo(MeshFixture.Air));
                Assert.That(padded[Coords.PaddedIndex(3, 4, 5)], Is.EqualTo(MeshFixture.Stone));
                Assert.That(padded[Coords.PaddedIndex(4, 4, 5)], Is.EqualTo(MeshFixture.Air));
            }
            finally
            {
                for (var i = 0; i < 27; i++)
                {
                    n[i].Dispose();
                }
            }
        }

        [Test]
        public void Collider_BakedCollisionMesh_IsHitFromOutside()
        {
            var renderMesh = new Mesh();
            var collisionMesh = new Mesh();
            var host = new GameObject("GreedyMeshTests");
            try
            {
                _fixture.MeshInto(
                    p => At(p, 2, 2, 2) ? MeshFixture.Stone : MeshFixture.Air,
                    renderMesh,
                    collisionMesh
                );
                using var ids = new NativeArray<EntityId>(1, Allocator.TempJob);
                var idsWrite = ids;
                idsWrite[0] = collisionMesh.GetEntityId();
                new ColliderBakeJob { MeshIds = ids }
                    .Schedule(1, 1)
                    .Complete();

                var meshCollider = host.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = collisionMesh;
                Physics.SyncTransforms();

                var down = Physics.Raycast(
                    new Vector3(2.5f * V, 10f, 2.5f * V),
                    Vector3.down,
                    out var hit,
                    20f
                );
                Assert.That(down, Is.True, "Ray from above missed the voxel");
                Assert.That(hit.point.y, Is.EqualTo(3f * V).Within(1e-4f));
                Assert.That(hit.normal, Is.EqualTo(Vector3.up));

                var side = Physics.Raycast(
                    new Vector3(-5f, 2.5f * V, 2.5f * V),
                    Vector3.right,
                    out hit,
                    20f
                );
                Assert.That(side, Is.True, "Ray from the side missed the voxel");
                Assert.That(hit.point.x, Is.EqualTo(2f * V).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(renderMesh);
                Object.DestroyImmediate(collisionMesh);
            }
        }

        private static bool At(int3 p, int x, int y, int z) => p.x == x && p.y == y && p.z == z;

        private static int Neighbor(int dx, int dy, int dz) =>
            (dx + 1) + 3 * ((dz + 1) + 3 * (dy + 1));
    }
}
