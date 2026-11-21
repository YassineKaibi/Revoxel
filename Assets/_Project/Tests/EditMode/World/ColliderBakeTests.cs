using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.Tests.EditMode
{
    public sealed class ColliderBakeTests
    {
        [Test]
        public void Collider_BakedCollisionMesh_IsHitFromOutside()
        {
            using var fixture = new MeshFixture();
            var renderMesh = new Mesh();
            var collisionMesh = new Mesh();
            var host = new GameObject("ColliderBakeTests");
            try
            {
                fixture.MeshInto(
                    pos => pos.Equals(new int3(2, 2, 2)) ? MeshFixture.Stone : MeshFixture.Air,
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
                    new Vector3(2.5f, 10f, 2.5f),
                    Vector3.down,
                    out var hit,
                    20f
                );
                Assert.That(down, Is.True, "Ray from above missed the block");
                Assert.That(hit.point.y, Is.EqualTo(3f).Within(1e-4f));
                Assert.That(hit.normal, Is.EqualTo(Vector3.up));

                var side = Physics.Raycast(
                    new Vector3(-5f, 2.5f, 2.5f),
                    Vector3.right,
                    out hit,
                    20f
                );
                Assert.That(side, Is.True, "Ray from the side missed the block");
                Assert.That(hit.point.x, Is.EqualTo(2f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(renderMesh);
                Object.DestroyImmediate(collisionMesh);
            }
        }
    }
}
