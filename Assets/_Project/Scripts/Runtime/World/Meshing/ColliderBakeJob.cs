using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Revoxel.World
{
    // Cooks the collision meshes of a batch of chunks off the main thread; the main thread
    // then assigns them to MeshCollider.sharedMesh, which reuses the cooked data as long as
    // the collider uses the default cooking options (PLAN-02 "Colliders").
    public struct ColliderBakeJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<EntityId> MeshIds;

        public void Execute(int index)
        {
            Physics.BakeMesh(MeshIds[index], false);
        }
    }
}
