using System;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.Tests.EditMode
{
    // Hand-built chunks for the meshing tests (docs/05 "Fixtures").
    public sealed class MeshFixture : IDisposable
    {
        public const ushort Air = 0;
        public const ushort Stone = 1;
        public const ushort Water = 2;
        public const ushort Dirt = 3;
        public const ushort Liquid = 4;

        private NativeArray<VoxelRenderData> _renderData;

        public MeshFixture()
        {
            _renderData = new NativeArray<VoxelRenderData>(
                WorldConstants.MaxVoxelTypes,
                Allocator.Persistent
            );
            _renderData[Stone] = new VoxelRenderData { IsSolid = true, IsOpaque = true };
            _renderData[Dirt] = new VoxelRenderData { IsSolid = true, IsOpaque = true };
            _renderData[Water] = new VoxelRenderData { IsSolid = false, IsOpaque = false };
            _renderData[Liquid] = new VoxelRenderData
            {
                IsSolid = false,
                IsOpaque = false,
                IsLiquid = true,
            };
        }

        public struct Result
        {
            public int RenderVertices;
            public int RenderIndices;
            public int LiquidIndices;
            public IndexFormat RenderIndexFormat;
            public int CollisionVertices;
            public int CollisionIndices;
            public VoxelVertex[] Vertices;
            public int[] Indices;
        }

        // Builds the padded input from a function over x, y, z in -1..32, then meshes it.
        public Result MeshPadded(Func<int3, ushort> voxelAt, bool skipLiquidSides = false)
        {
            using var padded = Pad(voxelAt);
            return Mesh(padded, skipLiquidSides);
        }

        // Chunk interior only; every neighbor layer is Air.
        public Result MeshChunk(Func<int3, ushort> voxelAt) =>
            MeshPadded(pos => Coords.IsLocalInRange(pos) ? voxelAt(pos) : Air);

        public Result Mesh(NativeArray<ushort> padded, bool skipLiquidSides = false)
        {
            var meshes = UnityEngine.Mesh.AllocateWritableMeshData(2);
            try
            {
                Run(padded, meshes, skipLiquidSides);
                var render = meshes[0];
                var collision = meshes[1];
                var result = new Result
                {
                    RenderVertices = render.vertexCount,
                    RenderIndices = render.GetSubMesh(0).indexCount,
                    LiquidIndices = render.subMeshCount > 1 ? render.GetSubMesh(1).indexCount : 0,
                    RenderIndexFormat = render.indexFormat,
                    CollisionVertices = collision.vertexCount,
                    CollisionIndices = collision.GetSubMesh(0).indexCount,
                    Vertices = render.GetVertexData<VoxelVertex>().ToArray(),
                };
                result.Indices = new int[result.RenderIndices];
                if (render.indexFormat == IndexFormat.UInt16)
                {
                    var indices = render.GetIndexData<ushort>();
                    for (var i = 0; i < result.Indices.Length; i++)
                    {
                        result.Indices[i] = indices[i];
                    }
                }
                else
                {
                    var indices = render.GetIndexData<uint>();
                    for (var i = 0; i < result.Indices.Length; i++)
                    {
                        result.Indices[i] = (int)indices[i];
                    }
                }

                return result;
            }
            finally
            {
                meshes.Dispose();
            }
        }

        // Meshes a chunk (neighbors Air) and applies the result to two Mesh objects.
        public void MeshInto(
            Func<int3, ushort> voxelAt,
            UnityEngine.Mesh render,
            UnityEngine.Mesh collision
        )
        {
            using var padded = Pad(pos => Coords.IsLocalInRange(pos) ? voxelAt(pos) : Air);
            var meshes = UnityEngine.Mesh.AllocateWritableMeshData(2);
            Run(padded, meshes);
            UnityEngine.Mesh.ApplyAndDisposeWritableMeshData(
                meshes,
                new[] { render, collision },
                MeshUpdateFlags.DontRecalculateBounds
            );
            render.bounds = ChunkMesher.ChunkLocalBounds;
            collision.bounds = ChunkMesher.ChunkLocalBounds;
        }

        public void Dispose()
        {
            if (_renderData.IsCreated)
            {
                _renderData.Dispose();
            }
        }

        private static NativeArray<ushort> Pad(Func<int3, ushort> voxelAt)
        {
            var padded = new NativeArray<ushort>(WorldConstants.PaddedVoxels, Allocator.TempJob);
            for (var y = -1; y <= WorldConstants.ChunkSize; y++)
            for (var z = -1; z <= WorldConstants.ChunkSize; z++)
            for (var x = -1; x <= WorldConstants.ChunkSize; x++)
            {
                padded[Coords.PaddedIndex(x, y, z)] = voxelAt(new int3(x, y, z));
            }

            return padded;
        }

        private void Run(
            NativeArray<ushort> padded,
            UnityEngine.Mesh.MeshDataArray meshes,
            bool skipLiquidSides = false
        )
        {
            using var renderLayout = ChunkMesher.CreateRenderLayout(Allocator.TempJob);
            using var collisionLayout = ChunkMesher.CreateCollisionLayout(Allocator.TempJob);
            new GreedyMeshJob
            {
                Padded = padded,
                RenderData = _renderData,
                RenderLayout = renderLayout,
                CollisionLayout = collisionLayout,
                RenderMesh = meshes[0],
                CollisionMesh = meshes[1],
                SkipLiquidSides = skipLiquidSides,
            }.Run();
        }
    }
}
