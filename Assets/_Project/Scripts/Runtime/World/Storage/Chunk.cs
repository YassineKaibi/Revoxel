using System;
using UnityEngine;

namespace Revoxel.World
{
    // 32 x 32 x 32 voxels: the unit of storage, meshing and collision (D-050).
    // Owns its voxels; the meshes and the view are owned by VoxelWorld.
    public sealed class Chunk : IDisposable
    {
        private ChunkVoxels _voxels;

        public Chunk(ChunkCoord coord, ChunkVoxels voxels)
        {
            Coord = coord;
            _voxels = voxels;
        }

        public ChunkCoord Coord { get; }

        // By reference: ChunkVoxels is a struct that changes on Set.
        public ref ChunkVoxels Voxels => ref _voxels;

        public bool IsDirty { get; internal set; }

        public Mesh RenderMesh { get; internal set; }

        public Mesh CollisionMesh { get; internal set; }

        public GameObject View { get; internal set; }

        public MeshFilter ViewFilter { get; internal set; }

        public MeshCollider ViewCollider { get; internal set; }

        public void Dispose()
        {
            if (_voxels.IsCreated)
            {
                _voxels.Dispose();
            }
        }
    }
}
