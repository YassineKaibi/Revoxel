using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.World
{
    // Meshes a batch of chunks: pads each chunk on the main thread (Burst, a few microseconds),
    // meshes them in parallel jobs into one MeshDataArray, applies the meshes, cooks the
    // collision meshes in a parallel job and assigns them to the chunk views.
    // Buffers are allocated once and reused, so a runtime batch allocates no managed memory.
    internal sealed class ChunkMeshBatch : IDisposable
    {
        private readonly VoxelWorld _world;
        private readonly Material _material;
        private readonly List<Chunk> _chunks = new List<Chunk>();
        private readonly List<NativeArray<byte>> _pads = new List<NativeArray<byte>>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Chunk> _baked = new List<Chunk>();

        private NativeArray<BlockRenderData> _renderData;
        private NativeArray<VertexAttributeDescriptor> _renderLayout;
        private NativeArray<VertexAttributeDescriptor> _collisionLayout;
        private NativeArray<byte> _air;
        private NativeArray<JobHandle> _handles;
        private NativeArray<EntityId> _bakeIds;
        private Mesh.MeshDataArray _meshData;
        private bool _hasMeshData;
        private JobHandle _meshHandle;
        private JobHandle _bakeHandle;

        public ChunkMeshBatch(VoxelWorld world, BlockRegistry registry, Material material)
        {
            _world = world;
            _material = material;
            _renderData = registry.CreateRenderData(Allocator.Persistent);
            _renderLayout = ChunkMesher.CreateRenderLayout(Allocator.Persistent);
            _collisionLayout = ChunkMesher.CreateCollisionLayout(Allocator.Persistent);
            _air = new NativeArray<byte>(WorldConstants.BlocksPerChunk, Allocator.Persistent);
        }

        public enum Stage
        {
            Idle,
            Meshing,
            Baking,
        }

        public Stage Current { get; private set; } = Stage.Idle;

        public int Count => _chunks.Count;

        public bool IsMeshingComplete => _meshHandle.IsCompleted;

        public bool IsBakingComplete => _bakeHandle.IsCompleted;

        public void Schedule(List<Chunk> chunks)
        {
            Debug.Assert(Current == Stage.Idle, "[World] Mesh batch scheduled twice");
            _chunks.Clear();
            _chunks.AddRange(chunks);
            EnsureCapacity(_chunks.Count);

            _meshData = Mesh.AllocateWritableMeshData(_chunks.Count * 2);
            _hasMeshData = true;
            for (var i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];
                var coord = chunk.Coord;
                new ChunkPadJob
                {
                    Center = chunk.Blocks,
                    PosX = BlocksOrAir(coord.Offset(new int3(1, 0, 0))),
                    NegX = BlocksOrAir(coord.Offset(new int3(-1, 0, 0))),
                    PosY = BlocksOrAir(coord.Offset(new int3(0, 1, 0))),
                    NegY = BlocksOrAir(coord.Offset(new int3(0, -1, 0))),
                    PosZ = BlocksOrAir(coord.Offset(new int3(0, 0, 1))),
                    NegZ = BlocksOrAir(coord.Offset(new int3(0, 0, -1))),
                    Padded = _pads[i],
                }.Run();
                chunk.IsDirty = false;

                _handles[i] = new ChunkMeshJob
                {
                    PaddedBlocks = _pads[i],
                    RenderData = _renderData,
                    RenderLayout = _renderLayout,
                    CollisionLayout = _collisionLayout,
                    RenderMesh = _meshData[i * 2],
                    CollisionMesh = _meshData[i * 2 + 1],
                }.Schedule();
            }

            _meshHandle = JobHandle.CombineDependencies(_handles.GetSubArray(0, _chunks.Count));
            JobHandle.ScheduleBatchedJobs();
            Current = Stage.Meshing;
        }

        // Completes meshing, applies the meshes and schedules collider cooking.
        public void ApplyMeshes()
        {
            _meshHandle.Complete();
            _meshes.Clear();
            foreach (var chunk in _chunks)
            {
                if (chunk.RenderMesh == null)
                {
                    chunk.RenderMesh = new Mesh { name = "ChunkRender" };
                    chunk.CollisionMesh = new Mesh { name = "ChunkCollision" };
                }

                _meshes.Add(chunk.RenderMesh);
                _meshes.Add(chunk.CollisionMesh);
            }

            Mesh.ApplyAndDisposeWritableMeshData(
                _meshData,
                _meshes,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            );
            _hasMeshData = false;

            _baked.Clear();
            foreach (var chunk in _chunks)
            {
                chunk.RenderMesh.bounds = ChunkMesher.ChunkLocalBounds;
                chunk.CollisionMesh.bounds = ChunkMesher.ChunkLocalBounds;
                if (chunk.CollisionMesh.vertexCount > 0)
                {
                    _bakeIds[_baked.Count] = chunk.CollisionMesh.GetEntityId();
                    _baked.Add(chunk);
                }
            }

            _bakeHandle = new ColliderBakeJob
            {
                MeshIds = _bakeIds.GetSubArray(0, _baked.Count),
            }.Schedule(_baked.Count, 1);
            JobHandle.ScheduleBatchedJobs();
            Current = Stage.Baking;
        }

        // Completes cooking and updates the chunk views. Empty chunks get no view.
        public void FinishColliders()
        {
            _bakeHandle.Complete();
            foreach (var chunk in _chunks)
            {
                var hasRender = chunk.RenderMesh.vertexCount > 0;
                var hasCollision = chunk.CollisionMesh.vertexCount > 0;
                if (chunk.View == null)
                {
                    if (!hasRender && !hasCollision)
                    {
                        continue;
                    }

                    CreateView(chunk);
                }

                chunk.ViewFilter.sharedMesh = chunk.RenderMesh;
                chunk.ViewCollider.sharedMesh = null;
                if (hasCollision)
                {
                    chunk.ViewCollider.sharedMesh = chunk.CollisionMesh;
                }
            }

            _chunks.Clear();
            Current = Stage.Idle;
        }

        // Frees the padded buffers above the given count (the load batch needs one per chunk).
        public void TrimPads(int keep)
        {
            for (var i = _pads.Count - 1; i >= keep; i--)
            {
                _pads[i].Dispose();
                _pads.RemoveAt(i);
            }
        }

        public void Dispose()
        {
            _meshHandle.Complete();
            _bakeHandle.Complete();
            if (_hasMeshData)
            {
                _meshData.Dispose();
                _hasMeshData = false;
            }

            TrimPads(0);
            DisposeIfCreated(ref _renderData);
            DisposeIfCreated(ref _renderLayout);
            DisposeIfCreated(ref _collisionLayout);
            DisposeIfCreated(ref _air);
            DisposeIfCreated(ref _handles);
            DisposeIfCreated(ref _bakeIds);
            _chunks.Clear();
            Current = Stage.Idle;
        }

        private NativeArray<byte> BlocksOrAir(ChunkCoord coord) =>
            _world.TryGetChunk(coord, out var neighbor) ? neighbor.Blocks : _air;

        private void EnsureCapacity(int count)
        {
            while (_pads.Count < count)
            {
                _pads.Add(new NativeArray<byte>(WorldConstants.PaddedBlocks, Allocator.Persistent));
            }

            if (!_handles.IsCreated || _handles.Length < count)
            {
                DisposeIfCreated(ref _handles);
                DisposeIfCreated(ref _bakeIds);
                _handles = new NativeArray<JobHandle>(count, Allocator.Persistent);
                _bakeIds = new NativeArray<EntityId>(count, Allocator.Persistent);
            }
        }

        private void CreateView(Chunk chunk)
        {
            var c = chunk.Coord.Value;
            var view = new GameObject($"Chunk {c.x} {c.y} {c.z}");
            view.layer = _world.gameObject.layer;
            // Block coordinates are world coordinates: VoxelWorld sits at the origin.
            var origin = Coords.ChunkOrigin(chunk.Coord);
            view.transform.SetPositionAndRotation(
                new Vector3(origin.x, origin.y, origin.z),
                Quaternion.identity
            );
            view.transform.SetParent(_world.transform, true);
            chunk.ViewFilter = view.AddComponent<MeshFilter>();
            view.AddComponent<MeshRenderer>().sharedMaterial = _material;
            chunk.ViewCollider = view.AddComponent<MeshCollider>();
            chunk.View = view;
        }

        private static void DisposeIfCreated<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }
    }
}
