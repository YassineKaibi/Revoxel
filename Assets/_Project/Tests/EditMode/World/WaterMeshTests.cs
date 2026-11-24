using NUnit.Framework;
using Revoxel.World;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    // Liquid faces in the water submesh (PLAN-05 Task 3, D-073).
    public sealed class WaterMeshTests
    {
        private const float V = WorldConstants.VoxelSize;

        private MeshFixture _fixture;

        [SetUp]
        public void SetUp() => _fixture = new MeshFixture();

        [TearDown]
        public void TearDown() => _fixture.Dispose();

        [Test]
        public void Mesh_Liquid_GoesToTheWaterSubmesh()
        {
            var r = _fixture.MeshChunk(p =>
                p.x < 4 && p.z < 4 && p.y == 0 ? MeshFixture.Liquid : MeshFixture.Air
            );

            Assert.That(r.RenderIndices, Is.EqualTo(0), "voxel submesh");
            Assert.That(r.LiquidIndices, Is.EqualTo(36), "six merged faces");
            Assert.That(r.RenderVertices, Is.EqualTo(24));
            Assert.That(r.CollisionVertices, Is.EqualTo(0));
        }

        // A pool: liquid over a stone floor, against a stone wall. The liquid has faces
        // only toward air; the stone keeps its faces toward the liquid.
        [Test]
        public void Mesh_Liquid_FacesOnlyTowardAir()
        {
            var r = _fixture.MeshChunk(p =>
                p.y == 0 && p.x < 5 && p.z == 0 ? MeshFixture.Stone
                : p.x == 4 && p.y == 1 && p.z == 0 ? MeshFixture.Stone
                : p.y == 1 && p.x < 4 && p.z == 0 ? MeshFixture.Liquid
                : MeshFixture.Air
            );

            // Liquid: top, west end, south and north sides; not the bottom, not toward the
            // wall at x = 4.
            Assert.That(r.LiquidIndices, Is.EqualTo(4 * 6));
            for (var i = 0; i < r.Vertices.Length; i++)
            {
                if (r.Vertices[i].MaterialAo.x != MeshFixture.Liquid)
                {
                    continue;
                }

                var n = r.Vertices[i].Normal;
                Assert.That(n.y, Is.Not.EqualTo(-1f), "liquid face toward the floor");
                Assert.That(n.x, Is.Not.EqualTo(1f), "liquid face toward the wall");
            }

            // The floor under the liquid and the wall's side toward it stay drawn.
            var stoneTop = false;
            var wallSide = false;
            foreach (var vertex in r.Vertices)
            {
                if (vertex.MaterialAo.x != MeshFixture.Stone)
                {
                    continue;
                }

                stoneTop |= vertex.Normal.y == 1f && vertex.Position.x < 4 * V;
                wallSide |= vertex.Normal.x == -1f && math.abs(vertex.Position.x - 4 * V) < 1e-5f;
            }

            Assert.That(stoneTop, Is.True, "floor under the liquid");
            Assert.That(wallSide, Is.True, "wall side toward the liquid");
        }

        // Coarse chunks with side walls drop liquid faces toward the x and z padding
        // (D-057, D-073), and keep the top and bottom.
        [Test]
        public void Mesh_Liquid_SkipSidesDropsBorderFaces()
        {
            static ushort Slab(int3 p) =>
                Coords.IsLocalInRange(p) && p.y < 4 ? MeshFixture.Liquid : MeshFixture.Air;

            var kept = _fixture.MeshPadded(Slab);
            var skipped = _fixture.MeshPadded(Slab, skipLiquidSides: true);

            Assert.That(kept.LiquidIndices, Is.EqualTo(6 * 6));
            Assert.That(skipped.LiquidIndices, Is.EqualTo(2 * 6));
        }
    }
}
