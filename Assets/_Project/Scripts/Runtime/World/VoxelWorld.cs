using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Revoxel.World
{
    // All chunks, keyed by chunk coordinate (D-030). Owns block edits and the dirty queue.
    public sealed class VoxelWorld : MonoBehaviour
    {
        private static readonly int3[] FaceNeighbors =
        {
            new int3(1, 0, 0),
            new int3(-1, 0, 0),
            new int3(0, 1, 0),
            new int3(0, -1, 0),
            new int3(0, 0, 1),
            new int3(0, 0, -1),
        };

        [SerializeField]
        private BlockRegistry _registry;

        private readonly Dictionary<ChunkCoord, Chunk> _chunks =
            new Dictionary<ChunkCoord, Chunk>();
        private readonly List<Chunk> _dirty = new List<Chunk>();
        private WorldBounds _bounds;

        // (world position, old block, new block)
        public event Action<int3, BlockId, BlockId> BlockChanged;

        public bool IsInitialized => _bounds != null;

        public WorldBounds Bounds => _bounds;

        public BlockRegistry Registry => _registry;

        public int DirtyCount => _dirty.Count;

        public IReadOnlyDictionary<ChunkCoord, Chunk> Chunks => _chunks;

        // Allocates every chunk inside the bounds, all Air and not dirty.
        public void Initialize(BlockRegistry registry, WorldBounds bounds)
        {
            if (IsInitialized)
            {
                ReleaseResources();
            }

            _registry = registry;
            _bounds = bounds;
            for (var y = bounds.Min.Value.y; y < bounds.Max.Value.y; y++)
            for (var z = bounds.Min.Value.z; z < bounds.Max.Value.z; z++)
            for (var x = bounds.Min.Value.x; x < bounds.Max.Value.x; x++)
            {
                var coord = new ChunkCoord(x, y, z);
                _chunks.Add(coord, new Chunk(coord));
            }
        }

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk) =>
            _chunks.TryGetValue(coord, out chunk);

        // Air when out of bounds or not loaded; never throws.
        public BlockId GetBlock(int3 world)
        {
            Coords.WorldToChunk(world, out var coord, out var local);
            return _chunks.TryGetValue(coord, out var chunk) ? chunk.Get(local) : BlockId.Air;
        }

        // False when out of bounds, not loaded or unchanged; never throws.
        public bool TrySetBlock(int3 world, BlockId id)
        {
            Coords.WorldToChunk(world, out var coord, out var local);
            if (!CanEdit() || !_chunks.TryGetValue(coord, out var chunk))
            {
                return false;
            }

            var old = chunk.Get(local);
            if (old == id)
            {
                return false;
            }

            chunk.Set(local, id);
            MarkDirty(chunk);
            for (var axis = 0; axis < 3; axis++)
            {
                if (local[axis] == 0)
                {
                    MarkDirty(coord.Offset(FaceNeighbors[axis * 2 + 1]));
                }
                else if (local[axis] == WorldConstants.ChunkMask)
                {
                    MarkDirty(coord.Offset(FaceNeighbors[axis * 2]));
                }
            }

            BlockChanged?.Invoke(world, old, id);
            return true;
        }

        public void ReleaseResources()
        {
            foreach (var chunk in _chunks.Values)
            {
                DestroyChunkObjects(chunk);
                chunk.Dispose();
            }

            _chunks.Clear();
            _dirty.Clear();
            _bounds = null;
        }

        private bool CanEdit() => IsInitialized;

        private void MarkDirty(ChunkCoord coord)
        {
            if (_chunks.TryGetValue(coord, out var chunk))
            {
                MarkDirty(chunk);
            }
        }

        private void MarkDirty(Chunk chunk)
        {
            if (!chunk.IsDirty)
            {
                chunk.IsDirty = true;
                _dirty.Add(chunk);
            }
        }

        private static void DestroyChunkObjects(Chunk chunk)
        {
            DestroyUnityObject(chunk.View);
            DestroyUnityObject(chunk.RenderMesh);
            DestroyUnityObject(chunk.CollisionMesh);
            chunk.View = null;
            chunk.RenderMesh = null;
            chunk.CollisionMesh = null;
        }

        private static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }
    }
}
