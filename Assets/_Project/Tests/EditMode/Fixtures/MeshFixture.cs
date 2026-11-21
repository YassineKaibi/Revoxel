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
        public const byte Air = 0;
        public const byte Stone = 1;
        public const byte Water = 2;

        public static readonly Color StoneCorruptedSrgb = new Color(0.5f, 0.25f, 0.75f, 1f);
        public static readonly Color StoneRestoredSrgb = new Color(0.2f, 0.6f, 0.9f, 1f);

        private NativeArray<BlockRenderData> _renderData;

        public MeshFixture()
        {
            _renderData = new NativeArray<BlockRenderData>(
                WorldConstants.MaxBlockTypes,
                Allocator.Persistent
            );
            _renderData[Stone] = BlockRenderData.FromSrgb(
                true,
                true,
                StoneCorruptedSrgb,
                StoneRestoredSrgb
            );
            _renderData[Water] = BlockRenderData.FromSrgb(false, false, Color.blue, Color.cyan);
        }

        public struct Result
        {
            public int RenderVertices;
            public int RenderIndices;
            public IndexFormat RenderIndexFormat;
            public int CollisionVertices;
            public int CollisionIndices;
            public VoxelVertex[] Vertices;
            public int[] Indices;
        }

        // Builds the padded input from a function over x, y, z in -1..16, then meshes it.
        public Result MeshPadded(Func<int3, byte> blockAt)
        {
            using var padded = new NativeArray<byte>(
                WorldConstants.PaddedBlocks,
                Allocator.TempJob
            );
            var p = padded;
            for (var y = -1; y <= WorldConstants.ChunkSize; y++)
            for (var z = -1; z <= WorldConstants.ChunkSize; z++)
            for (var x = -1; x <= WorldConstants.ChunkSize; x++)
            {
                p[Coords.PaddedIndex(x, y, z)] = blockAt(new int3(x, y, z));
            }

            return Mesh(padded);
        }

        // Chunk interior only; every neighbor layer is Air.
        public Result MeshChunk(Func<int3, byte> blockAt) =>
            MeshPadded(pos => Coords.IsLocalInRange(pos) ? blockAt(pos) : Air);

        public Result Mesh(NativeArray<byte> padded)
        {
            var meshes = UnityEngine.Mesh.AllocateWritableMeshData(2);
            using var renderLayout = ChunkMesher.CreateRenderLayout(Allocator.TempJob);
            using var collisionLayout = ChunkMesher.CreateCollisionLayout(Allocator.TempJob);
            try
            {
                new ChunkMeshJob
                {
                    PaddedBlocks = padded,
                    RenderData = _renderData,
                    RenderLayout = renderLayout,
                    CollisionLayout = collisionLayout,
                    RenderMesh = meshes[0],
                    CollisionMesh = meshes[1],
                }.Run();

                var render = meshes[0];
                var collision = meshes[1];
                var result = new Result
                {
                    RenderVertices = render.vertexCount,
                    RenderIndices = render.GetSubMesh(0).indexCount,
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

                return result;
            }
            finally
            {
                meshes.Dispose();
            }
        }

        public void Dispose()
        {
            if (_renderData.IsCreated)
            {
                _renderData.Dispose();
            }
        }
    }
}
