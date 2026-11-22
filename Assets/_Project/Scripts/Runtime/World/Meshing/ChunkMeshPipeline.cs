using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.World
{
    // Meshes chunks in jobs: the caller fills Neighborhood with the chunk and its 26
    // neighbors, Schedule pads them on the main thread (Burst, Run) into a slot's own
    // buffer and schedules a GreedyMeshJob; ApplyFinished applies finished results to the
    // chunk views. Slots and their buffers are reused, so meshing allocates no managed
    // memory once warm.
    internal sealed class ChunkMeshPipeline : IDisposable
    {
        private const MeshUpdateFlags ApplyFlags =
            MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        // Unity frees writable mesh data allocations after 4 frames, with a warning.
        private const int MaxResultAgeFrames = 3;

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Mesh[] _targets = new Mesh[2];
        private NativeArray<VoxelRenderData> _renderData;
        private NativeArray<VertexAttributeDescriptor> _renderLayout;
        private NativeArray<VertexAttributeDescriptor> _collisionLayout;
        private NativeArray<ChunkVoxels> _neighborhood;

        public ChunkMeshPipeline(VoxelRegistry registry, int capacity)
        {
            _renderData = registry.CreateRenderData(Allocator.Persistent);
            _renderLayout = ChunkMesher.CreateRenderLayout(Allocator.Persistent);
            _collisionLayout = ChunkMesher.CreateCollisionLayout(Allocator.Persistent);
            _neighborhood = new NativeArray<ChunkVoxels>(27, Allocator.Persistent);
            for (var i = 0; i < capacity; i++)
            {
                _slots.Add(new Slot());
            }
        }

        // Index (dx + 1) + 3 * ((dz + 1) + 3 * (dy + 1)); read by the pad job in Schedule.
        public NativeArray<ChunkVoxels> Neighborhood => _neighborhood;

        public int Busy { get; private set; }

        public int Capacity => _slots.Count;

        // Whether a mesh job is in flight for a chunk of the column. A slot whose chunk was
        // reused elsewhere may answer true for a frame; callers only wait longer.
        public bool HasJobForColumn(int2 column)
        {
            foreach (var slot in _slots)
            {
                if (slot.Busy && Coords.ColumnOf(slot.Chunk.Coord).Equals(column))
                {
                    return true;
                }
            }

            return false;
        }

        // Pads the chunk from Neighborhood and schedules its mesh job. Urgent chunks (edits)
        // may grow the slots beyond the capacity and are completed by the next ApplyFinished.
        public void Schedule(Chunk chunk, bool urgent, int frame)
        {
            var slot = FreeSlot();
            if (slot == null)
            {
                slot = new Slot();
                _slots.Add(slot);
            }

            if (!slot.Padded.IsCreated)
            {
                slot.Padded = new NativeArray<ushort>(
                    WorldConstants.PaddedVoxels,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );
            }

            new ChunkPadJob { Neighborhood = _neighborhood, Padded = slot.Padded }.Run();
            slot.Data = Mesh.AllocateWritableMeshData(2);
            slot.HasData = true;
            slot.Chunk = chunk;
            slot.Version = chunk.Version;
            slot.Urgent = urgent;
            slot.ScheduledFrame = frame;
            slot.Busy = true;
            Busy++;
            slot.Handle = new GreedyMeshJob
            {
                Padded = slot.Padded,
                RenderData = _renderData,
                RenderLayout = _renderLayout,
                CollisionLayout = _collisionLayout,
                RenderMesh = slot.Data[0],
                CollisionMesh = slot.Data[1],
            }.Schedule();
        }

        // Applies finished results until the budget runs out, at least one per call. Urgent
        // results and results 3 frames old are completed (waiting if needed) and applied
        // whatever the budget. Stale results (chunk changed, unloaded or reused since
        // scheduling) are dropped. Chunks whose mesh changed are added to `applied`.
        public void ApplyFinished(
            ChunkViewPool views,
            List<Chunk> applied,
            Stopwatch watch,
            long budgetTicks,
            int frame
        )
        {
            var appliedAny = false;
            for (var pass = 0; pass < 2; pass++)
            {
                var urgentPass = pass == 0;
                foreach (var slot in _slots)
                {
                    if (!slot.Busy || slot.Urgent != urgentPass)
                    {
                        continue;
                    }

                    // Writable mesh data may live 4 frames: older results are completed
                    // (waiting if needed) and applied whatever the budget.
                    var expiring = frame - slot.ScheduledFrame >= MaxResultAgeFrames;
                    if (!urgentPass && !expiring)
                    {
                        if (
                            !slot.Handle.IsCompleted
                            || (appliedAny && watch.ElapsedTicks > budgetTicks)
                        )
                        {
                            continue;
                        }
                    }

                    Finish(slot, views, applied);
                    appliedAny = true;
                }
            }
        }

        public void Dispose()
        {
            foreach (var slot in _slots)
            {
                slot.Handle.Complete();
                if (slot.HasData)
                {
                    slot.Data.Dispose();
                    slot.HasData = false;
                }

                if (slot.Padded.IsCreated)
                {
                    slot.Padded.Dispose();
                }

                slot.Busy = false;
                slot.Chunk = null;
            }

            _slots.Clear();
            Busy = 0;
            DisposeIfCreated(ref _renderData);
            DisposeIfCreated(ref _renderLayout);
            DisposeIfCreated(ref _collisionLayout);
            DisposeIfCreated(ref _neighborhood);
        }

        private void Finish(Slot slot, ChunkViewPool views, List<Chunk> applied)
        {
            slot.Handle.Complete();
            slot.Busy = false;
            Busy--;
            var chunk = slot.Chunk;
            slot.Chunk = null;
            if (chunk.Version != slot.Version)
            {
                slot.Data.Dispose();
                slot.HasData = false;
                return;
            }

            var empty = slot.Data[0].vertexCount == 0 && slot.Data[1].vertexCount == 0;
            if (empty)
            {
                slot.Data.Dispose();
                slot.HasData = false;
                if (chunk.View != null)
                {
                    views.Release(chunk.View);
                    chunk.View = null;
                }
            }
            else
            {
                chunk.View ??= views.Get(chunk.Coord);
                _targets[0] = chunk.View.RenderMesh;
                _targets[1] = chunk.View.CollisionMesh;
                Mesh.ApplyAndDisposeWritableMeshData(slot.Data, _targets, ApplyFlags);
                slot.HasData = false;
                chunk.View.RenderMesh.bounds = ChunkMesher.ChunkLocalBounds;
                chunk.View.CollisionMesh.bounds = ChunkMesher.ChunkLocalBounds;
                chunk.View.Collider.sharedMesh = null;
                _targets[0] = null;
                _targets[1] = null;
            }

            chunk.HasCollider = false;
            chunk.MeshVersion++;
            applied.Add(chunk);
        }

        private Slot FreeSlot()
        {
            foreach (var slot in _slots)
            {
                if (!slot.Busy)
                {
                    return slot;
                }
            }

            return null;
        }

        private static void DisposeIfCreated<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }

        private sealed class Slot
        {
            public NativeArray<ushort> Padded;
            public Mesh.MeshDataArray Data;
            public bool HasData;
            public JobHandle Handle;
            public Chunk Chunk;
            public int Version;
            public bool Busy;
            public bool Urgent;
            public int ScheduledFrame;
        }
    }
}
