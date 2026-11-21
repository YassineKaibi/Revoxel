using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.Tests.EditMode
{
    public sealed class ChunkMeshTests
    {
        private const int Size = WorldConstants.ChunkSize;

        private MeshFixture _fixture;

        [SetUp]
        public void SetUp() => _fixture = new MeshFixture();

        [TearDown]
        public void TearDown() => _fixture.Dispose();

        [Test]
        public void Mesh_SingleBlock_Has6Faces()
        {
            var result = _fixture.MeshChunk(p =>
                p.Equals(new int3(8, 8, 8)) ? MeshFixture.Stone : MeshFixture.Air
            );

            Assert.That(result.RenderVertices, Is.EqualTo(24));
            Assert.That(result.RenderIndices, Is.EqualTo(36));
            Assert.That(result.CollisionVertices, Is.EqualTo(24));
        }

        [Test]
        public void Mesh_TwoAdjacentBlocks_Has10Faces()
        {
            var result = _fixture.MeshChunk(p =>
                p.y == 8 && p.z == 8 && (p.x == 8 || p.x == 9) ? MeshFixture.Stone : MeshFixture.Air
            );

            Assert.That(result.RenderVertices, Is.EqualTo(40));
            Assert.That(result.RenderIndices, Is.EqualTo(60));
        }

        [Test]
        public void Mesh_FullChunk_NoNeighbors_HasOnlyOuterFaces()
        {
            var result = _fixture.MeshChunk(_ => MeshFixture.Stone);

            Assert.That(result.RenderVertices, Is.EqualTo(6 * Size * Size * 4));
            Assert.That(result.RenderIndices, Is.EqualTo(6 * Size * Size * 6));
        }

        [Test]
        public void Mesh_FullChunk_SolidNeighbors_HasNoFaces()
        {
            var result = _fixture.MeshPadded(_ => MeshFixture.Stone);

            Assert.That(result.RenderVertices, Is.Zero);
            Assert.That(result.RenderIndices, Is.Zero);
            Assert.That(result.CollisionVertices, Is.Zero);
        }

        [Test]
        public void Mesh_EmptyChunk_HasNoFaces()
        {
            var result = _fixture.MeshChunk(_ => MeshFixture.Air);

            Assert.That(result.RenderVertices, Is.Zero);
            Assert.That(result.CollisionVertices, Is.Zero);
        }

        [Test]
        public void Mesh_Checkerboard_FitsUInt16()
        {
            var result = _fixture.MeshChunk(p =>
                ((p.x + p.y + p.z) & 1) == 0 ? MeshFixture.Stone : MeshFixture.Air
            );

            Assert.That(result.RenderVertices, Is.EqualTo(49152));
            Assert.That(result.RenderIndexFormat, Is.EqualTo(IndexFormat.UInt16));
            Assert.That(result.Indices, Has.All.LessThan(49152));
        }

        [Test]
        public void Mesh_WaterNextToWater_NoFaceBetween()
        {
            var result = _fixture.MeshChunk(p =>
                p.y == 8 && p.z == 8 && (p.x == 8 || p.x == 9) ? MeshFixture.Water : MeshFixture.Air
            );

            Assert.That(result.RenderVertices, Is.EqualTo(40));
        }

        [Test]
        public void Mesh_WaterNotInCollisionMesh()
        {
            var result = _fixture.MeshChunk(p =>
                p.Equals(new int3(8, 8, 8)) ? MeshFixture.Water : MeshFixture.Air
            );

            Assert.That(result.RenderVertices, Is.EqualTo(24));
            Assert.That(result.CollisionVertices, Is.Zero);
            Assert.That(result.CollisionIndices, Is.Zero);
        }

        [Test]
        public void Mesh_StoneUnderWater_HasFaceTowardWater()
        {
            // Stone at y = 8 with water above: the stone top is drawn and collides,
            // the water bottom is hidden by the opaque stone.
            var result = _fixture.MeshChunk(p =>
                p.x == 8 && p.z == 8 && p.y == 8 ? MeshFixture.Stone
                : p.x == 8 && p.z == 8 && p.y == 9 ? MeshFixture.Water
                : MeshFixture.Air
            );

            Assert.That(result.RenderVertices, Is.EqualTo(4 * (6 + 5)));
            Assert.That(result.CollisionVertices, Is.EqualTo(24));
        }

        [Test]
        public void Mesh_VertexColors_AreLinearConverted()
        {
            var result = _fixture.MeshChunk(p =>
                p.Equals(new int3(0, 0, 0)) ? MeshFixture.Stone : MeshFixture.Air
            );

            Color32 expectedCorrupted = MeshFixture.StoneCorruptedSrgb.linear;
            Color32 expectedRestored = MeshFixture.StoneRestoredSrgb.linear;
            Assert.That(expectedCorrupted.r, Is.Not.EqualTo((byte)(0.5f * 255)));
            Assert.That(
                expectedCorrupted.r,
                Is.EqualTo(Mathf.RoundToInt(Mathf.GammaToLinearSpace(0.5f) * 255))
            );
            foreach (var vertex in result.Vertices)
            {
                Assert.That(vertex.Corrupted, Is.EqualTo(expectedCorrupted));
                Assert.That(vertex.Restored, Is.EqualTo(expectedRestored));
            }
        }

        [Test]
        public void Mesh_FaceWinding_MatchesNormals()
        {
            var result = _fixture.MeshChunk(p =>
                p.Equals(new int3(3, 4, 5)) ? MeshFixture.Stone : MeshFixture.Air
            );

            for (var i = 0; i < result.Indices.Length; i += 3)
            {
                var a = result.Vertices[result.Indices[i]];
                var b = result.Vertices[result.Indices[i + 1]];
                var c = result.Vertices[result.Indices[i + 2]];
                var cross = math.cross(b.Position - a.Position, c.Position - a.Position);
                Assert.That(
                    math.dot(math.normalize(cross), a.Normal),
                    Is.EqualTo(1f).Within(1e-5f)
                );
            }
        }

        [Test]
        public void Pad_CopiesFaceNeighborLayers()
        {
            using var center = new NativeArray<byte>(
                WorldConstants.BlocksPerChunk,
                Allocator.TempJob
            );
            using var posX = Filled(MeshFixture.Stone);
            using var air = Filled(MeshFixture.Air);
            using var padded = new NativeArray<byte>(
                WorldConstants.PaddedBlocks,
                Allocator.TempJob
            );
            new ChunkPadJob
            {
                Center = center,
                PosX = posX,
                NegX = air,
                PosY = air,
                NegY = air,
                PosZ = air,
                NegZ = air,
                Padded = padded,
            }.Run();

            Assert.That(padded[Coords.PaddedIndex(Size, 3, 7)], Is.EqualTo(MeshFixture.Stone));
            Assert.That(padded[Coords.PaddedIndex(-1, 3, 7)], Is.EqualTo(MeshFixture.Air));
            Assert.That(padded[Coords.PaddedIndex(Size, Size, 7)], Is.EqualTo(MeshFixture.Air));
            Assert.That(padded[Coords.PaddedIndex(5, 5, 5)], Is.EqualTo(MeshFixture.Air));
        }

        private static NativeArray<byte> Filled(byte value)
        {
            var array = new NativeArray<byte>(WorldConstants.BlocksPerChunk, Allocator.TempJob);
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = value;
            }

            return array;
        }
    }
}
