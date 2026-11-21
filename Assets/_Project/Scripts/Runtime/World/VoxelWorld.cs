using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Revoxel.World
{
    // Loaded chunks, keyed by chunk coordinate (D-030, D-050), voxel edits and the dirty
    // queue. Must sit at the world origin: voxel coordinates are world coordinates.
    public sealed class VoxelWorld : MonoBehaviour
    {
        private readonly Dictionary<ChunkCoord, Chunk> _chunks =
            new Dictionary<ChunkCoord, Chunk>();
        private readonly List<Chunk> _dirty = new List<Chunk>();
        private VoxelRegistry _registry;
        private WorldBounds _bounds;

        // Inclusive min and max voxel of a change. Raised once per edit call.
        public event Action<int3, int3> VoxelsChanged;

        public bool IsInitialized => _bounds != null;

        public bool IsLoaded => true;

        // 0 to 1 while loading, for the loading screen.
        public float LoadProgress => 1f;

        public int ViewCount => 0;

        public int DirtyCount => _dirty.Count;

        public WorldBounds Bounds => _bounds;

        public VoxelRegistry Registry => _registry;

        // No chunk is allocated: chunks arrive through InsertChunk.
        public void Initialize(VoxelRegistry registry, WorldBounds bounds)
        {
            if (IsInitialized)
            {
                ReleaseResources();
            }

            _registry = registry;
            _bounds = bounds;
        }

        // Takes ownership of the voxels. Replaces (and disposes) a chunk already there.
        internal void InsertChunk(ChunkCoord coord, ChunkVoxels voxels)
        {
            if (_chunks.TryGetValue(coord, out var old))
            {
                RemoveDirty(old);
                old.Dispose();
            }

            _chunks[coord] = new Chunk(coord, voxels);
        }

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk) =>
            _chunks.TryGetValue(coord, out chunk);

        // Air when out of bounds or not loaded; never throws.
        public VoxelId GetVoxel(int3 voxel)
        {
            Coords.WorldToChunk(voxel, out var coord, out var local);
            return _chunks.TryGetValue(coord, out var chunk)
                ? chunk.Voxels.Get(Coords.LocalIndex(local))
                : VoxelId.Air;
        }

        // False when out of bounds, not loaded, unregistered id or unchanged; never throws.
        public bool TrySetVoxel(int3 voxel, VoxelId id)
        {
            if (!IsInitialized || !_registry.IsRegistered(id))
            {
                return false;
            }

            Coords.WorldToChunk(voxel, out var coord, out var local);
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                return false;
            }

            var index = Coords.LocalIndex(local);
            if (chunk.Voxels.Get(index) == id)
            {
                return false;
            }

            chunk.Voxels.Set(index, id);
            MarkDirtyAround(chunk, local, local);
            VoxelsChanged?.Invoke(voxel, voxel);
            return true;
        }

        // Sets every loaded voxel whose center is within the radius and differs from fill.
        // Returns the number changed; raises one event when it is not zero.
        public int CarveSphere(float3 centerMeters, float radiusMeters, VoxelId fill)
        {
            if (!IsInitialized || !_registry.IsRegistered(fill) || !(radiusMeters > 0f))
            {
                return 0;
            }

            var center = centerMeters * WorldConstants.VoxelsPerMeter;
            var radius = radiusMeters * WorldConstants.VoxelsPerMeter;
            var radiusSq = radius * radius;
            var min = (int3)math.floor(center - radius);
            var max = (int3)math.floor(center + radius);
            var minChunk = Coords.ChunkOf(min).Value;
            var maxChunk = Coords.ChunkOf(max).Value;
            var changedMin = new int3(int.MaxValue);
            var changedMax = new int3(int.MinValue);
            var count = 0;

            for (var cy = minChunk.y; cy <= maxChunk.y; cy++)
            for (var cz = minChunk.z; cz <= maxChunk.z; cz++)
            for (var cx = minChunk.x; cx <= maxChunk.x; cx++)
            {
                if (!_chunks.TryGetValue(new ChunkCoord(cx, cy, cz), out var chunk))
                {
                    continue;
                }

                var origin = Coords.ChunkOrigin(chunk.Coord);
                var localMin = math.max(min - origin, 0);
                var localMax = math.min(max - origin, WorldConstants.ChunkMask);
                var chunkMin = new int3(int.MaxValue);
                var chunkMax = new int3(int.MinValue);
                ref var voxels = ref chunk.Voxels;
                for (var y = localMin.y; y <= localMax.y; y++)
                for (var z = localMin.z; z <= localMax.z; z++)
                for (var x = localMin.x; x <= localMax.x; x++)
                {
                    var local = new int3(x, y, z);
                    if (math.distancesq((float3)(origin + local) + 0.5f, center) > radiusSq)
                    {
                        continue;
                    }

                    var index = Coords.LocalIndex(local);
                    if (voxels.Get(index) == fill)
                    {
                        continue;
                    }

                    voxels.Set(index, fill);
                    chunkMin = math.min(chunkMin, local);
                    chunkMax = math.max(chunkMax, local);
                    count++;
                }

                if (chunkMin.x != int.MaxValue)
                {
                    MarkDirtyAround(chunk, chunkMin, chunkMax);
                    changedMin = math.min(changedMin, origin + chunkMin);
                    changedMax = math.max(changedMax, origin + chunkMax);
                }
            }

            if (count > 0)
            {
                VoxelsChanged?.Invoke(changedMin, changedMax);
            }

            return count;
        }

        public void ReleaseResources()
        {
            foreach (var chunk in _chunks.Values)
            {
                chunk.Dispose();
            }

            _chunks.Clear();
            _dirty.Clear();
            _bounds = null;
        }

        private void Awake()
        {
            VoxelShaderGlobals.SetDefaults();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        // Marks the chunk and every loaded chunk whose padded volume contains a changed
        // voxel: per axis offset 0, plus -1 when the change touches local 0, plus +1 when it
        // touches local 31 (1, 2, 4 or 8 chunks).
        private void MarkDirtyAround(Chunk chunk, int3 localMin, int3 localMax)
        {
            var low = math.select(int3.zero, new int3(-1), localMin == 0);
            var high = math.select(int3.zero, new int3(1), localMax == WorldConstants.ChunkMask);
            for (var dy = low.y; dy <= high.y; dy++)
            for (var dz = low.z; dz <= high.z; dz++)
            for (var dx = low.x; dx <= high.x; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0)
                {
                    MarkDirty(chunk);
                }
                else if (_chunks.TryGetValue(chunk.Coord.Offset(new int3(dx, dy, dz)), out var n))
                {
                    MarkDirty(n);
                }
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

        private void RemoveDirty(Chunk chunk)
        {
            if (chunk.IsDirty)
            {
                chunk.IsDirty = false;
                _dirty.Remove(chunk);
            }
        }
    }
}
