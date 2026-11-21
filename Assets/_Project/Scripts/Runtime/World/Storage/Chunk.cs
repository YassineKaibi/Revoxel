using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Revoxel.World
{
    // 16 x 16 x 16 blocks: the unit of storage, meshing and collision (D-030).
    // Owns its block data; the meshes and the view are owned by VoxelWorld.
    public sealed class Chunk : IDisposable
    {
        public Chunk(ChunkCoord coord)
        {
            Coord = coord;
            Blocks = new NativeArray<byte>(
                WorldConstants.BlocksPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory
            );
        }

        public ChunkCoord Coord { get; }

        public NativeArray<byte> Blocks { get; private set; }

        public bool IsDirty { get; internal set; }

        public Mesh RenderMesh { get; internal set; }

        public Mesh CollisionMesh { get; internal set; }

        public GameObject View { get; internal set; }

        public MeshFilter ViewFilter { get; internal set; }

        public MeshCollider ViewCollider { get; internal set; }

        public BlockId Get(int3 local) => new BlockId(Blocks[Coords.LocalIndex(local)]);

        public void Set(int3 local, BlockId id)
        {
            var blocks = Blocks;
            blocks[Coords.LocalIndex(local)] = id.Value;
        }

        public void Dispose()
        {
            if (Blocks.IsCreated)
            {
                Blocks.Dispose();
            }
        }
    }
}
