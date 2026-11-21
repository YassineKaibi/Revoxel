using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.World
{
    // Vertex layouts and helpers shared by the mesh pipeline and the tests.
    public static class ChunkMesher
    {
        private const float ChunkMeters = WorldConstants.ChunkSize * WorldConstants.VoxelSize;

        // A chunk is a 4 m cube in its own space.
        public static readonly Bounds ChunkLocalBounds = new Bounds(
            Vector3.one * (ChunkMeters * 0.5f),
            Vector3.one * ChunkMeters
        );

        public static NativeArray<VertexAttributeDescriptor> CreateRenderLayout(Allocator allocator)
        {
            var layout = new NativeArray<VertexAttributeDescriptor>(3, allocator);
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
                VertexAttribute.TexCoord0,
                VertexAttributeFormat.Float32,
                2
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
