using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.World
{
    // Vertex layouts and helpers shared by VoxelWorld and the tests.
    public static class ChunkMesher
    {
        public static readonly Bounds ChunkLocalBounds = new Bounds(
            Vector3.one * (WorldConstants.ChunkSize * 0.5f),
            Vector3.one * WorldConstants.ChunkSize
        );

        public static NativeArray<VertexAttributeDescriptor> CreateRenderLayout(Allocator allocator)
        {
            var layout = new NativeArray<VertexAttributeDescriptor>(4, allocator);
            layout[0] = new VertexAttributeDescriptor(
                VertexAttribute.Position,
                VertexAttributeFormat.Float32,
                3
            );
            layout[1] = new VertexAttributeDescriptor(
                VertexAttribute.Normal,
                VertexAttributeFormat.Float32,
                3
            );
            layout[2] = new VertexAttributeDescriptor(
                VertexAttribute.Color,
                VertexAttributeFormat.UNorm8,
                4
            );
            layout[3] = new VertexAttributeDescriptor(
                VertexAttribute.TexCoord0,
                VertexAttributeFormat.UNorm8,
                4
            );
            return layout;
        }

        public static NativeArray<VertexAttributeDescriptor> CreateCollisionLayout(
            Allocator allocator
        )
        {
            var layout = new NativeArray<VertexAttributeDescriptor>(1, allocator);
            layout[0] = new VertexAttributeDescriptor(
                VertexAttribute.Position,
                VertexAttributeFormat.Float32,
                3
            );
            return layout;
        }
    }
}
