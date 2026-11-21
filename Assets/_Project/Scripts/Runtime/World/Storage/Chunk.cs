using UnityEngine;

namespace Revoxel.World
{
    // 32 x 32 x 32 voxels: the unit of storage, meshing and collision (D-050).
    // The meshes and the view are owned by VoxelWorld.
    public sealed class Chunk
    {
        public Chunk(ChunkCoord coord)
        {
            Coord = coord;
        }

        public ChunkCoord Coord { get; }

        public bool IsDirty { get; internal set; }

        public Mesh RenderMesh { get; internal set; }

        public Mesh CollisionMesh { get; internal set; }

        public GameObject View { get; internal set; }

        public MeshFilter ViewFilter { get; internal set; }

        public MeshCollider ViewCollider { get; internal set; }
    }
}
