using System;

namespace Revoxel.World
{
    // 32 x 32 x 32 voxels: the unit of storage, meshing and collision (D-050).
    // Owns its voxels; the view is owned by VoxelWorld. Pooled while streaming.
    public sealed class Chunk : IDisposable
    {
        private ChunkVoxels _voxels;

        internal Chunk() { }

        public ChunkCoord Coord { get; private set; }

        // By reference: ChunkVoxels is a struct that changes on Set.
        public ref ChunkVoxels Voxels => ref _voxels;

        public bool IsDirty { get; internal set; }

        // Bumped on every change, unload and reuse: a mesh result applies only if it was
        // made from the current version.
        public int Version { get; internal set; }

        // Bumped each time a new mesh is applied.
        public int MeshVersion { get; internal set; }

        // Null when the chunk has no faces.
        public ChunkView View { get; internal set; }

        internal bool HasCollider { get; set; }

        internal StreamColumn Column { get; set; }

        public bool IsUniformAir => _voxels.IsUniform && _voxels.UniformId.IsAir;

        internal void Reset(ChunkCoord coord, ChunkVoxels voxels, StreamColumn column)
        {
            Coord = coord;
            _voxels = voxels;
            Column = column;
            IsDirty = false;
            HasCollider = false;
            View = null;
            Version++;
        }

        public void Dispose()
        {
            if (_voxels.IsCreated)
            {
                _voxels.Dispose();
            }

            _voxels = default;
            Version++;
        }
    }
}
