using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.World
{
    // Culled-face meshing of one chunk (D-031). Reads the padded 18 x 18 x 18 input and
    // writes two meshes: the render mesh (every visible face, with both colors) and the
    // collision mesh (faces of solid blocks toward non-solid ones, positions only).
    [BurstCompile]
    public struct ChunkMeshJob : IJob
    {
        public const int MaxUInt16Vertices = 65535;

        private const int Size = WorldConstants.ChunkSize;
        private const int Padded = WorldConstants.PaddedSize;
        private const float Half = WorldConstants.ChunkSize * 0.5f;

        [ReadOnly]
        public NativeArray<byte> PaddedBlocks;

        [ReadOnly]
        public NativeArray<BlockRenderData> RenderData;

        [ReadOnly]
        public NativeArray<VertexAttributeDescriptor> RenderLayout;

        [ReadOnly]
        public NativeArray<VertexAttributeDescriptor> CollisionLayout;

        public Mesh.MeshData RenderMesh;
        public Mesh.MeshData CollisionMesh;

        public void Execute()
        {
            var renderFaces = 0;
            var collisionFaces = 0;
            for (var y = 0; y < Size; y++)
            for (var z = 0; z < Size; z++)
            for (var x = 0; x < Size; x++)
            {
                var index = Coords.PaddedIndex(x, y, z);
                var a = PaddedBlocks[index];
                if (a == 0)
                {
                    continue;
                }

                for (var dir = 0; dir < 6; dir++)
                {
                    var b = PaddedBlocks[index + Stride(dir)];
                    if (EmitsRenderFace(a, b))
                    {
                        renderFaces++;
                    }

                    if (EmitsCollisionFace(a, b))
                    {
                        collisionFaces++;
                    }
                }
            }

            var renderWide = renderFaces * 4 > MaxUInt16Vertices;
            var collisionWide = collisionFaces * 4 > MaxUInt16Vertices;
            Prepare(ref RenderMesh, RenderLayout, renderFaces, renderWide);
            Prepare(ref CollisionMesh, CollisionLayout, collisionFaces, collisionWide);

            var renderVertices = RenderMesh.GetVertexData<VoxelVertex>();
            var collisionVertices = CollisionMesh.GetVertexData<float3>();
            var render16 = renderWide ? default : RenderMesh.GetIndexData<ushort>();
            var render32 = renderWide ? RenderMesh.GetIndexData<uint>() : default;
            var collision16 = collisionWide ? default : CollisionMesh.GetIndexData<ushort>();
            var collision32 = collisionWide ? CollisionMesh.GetIndexData<uint>() : default;
            var renderFace = 0;
            var collisionFace = 0;
            for (var y = 0; y < Size; y++)
            for (var z = 0; z < Size; z++)
            for (var x = 0; x < Size; x++)
            {
                var index = Coords.PaddedIndex(x, y, z);
                var a = PaddedBlocks[index];
                if (a == 0)
                {
                    continue;
                }

                var origin = new float3(x, y, z);
                var data = RenderData[a];
                for (var dir = 0; dir < 6; dir++)
                {
                    var b = PaddedBlocks[index + Stride(dir)];
                    var corners = Corners(dir);
                    if (EmitsRenderFace(a, b))
                    {
                        var normal = Normal(dir);
                        var v = renderFace * 4;
                        for (var i = 0; i < 4; i++)
                        {
                            renderVertices[v + i] = new VoxelVertex
                            {
                                Position = origin + Corner(corners, i),
                                Normal = normal,
                                Restored = data.Restored,
                                Corrupted = data.Corrupted,
                            };
                        }

                        WriteQuad(renderWide, render16, render32, renderFace, v);
                        renderFace++;
                    }

                    if (EmitsCollisionFace(a, b))
                    {
                        var v = collisionFace * 4;
                        for (var i = 0; i < 4; i++)
                        {
                            collisionVertices[v + i] = origin + Corner(corners, i);
                        }

                        WriteQuad(collisionWide, collision16, collision32, collisionFace, v);
                        collisionFace++;
                    }
                }
            }

            Finish(ref RenderMesh, renderFaces);
            Finish(ref CollisionMesh, collisionFaces);
        }

        // A face of A toward B is drawn when A is not Air and B is not opaque, except between
        // two blocks of the same non-opaque type (water to water).
        private bool EmitsRenderFace(byte a, byte b) => !RenderData[b].IsOpaque && a != b;

        private bool EmitsCollisionFace(byte a, byte b) =>
            RenderData[a].IsSolid && !RenderData[b].IsSolid;

        private static void Prepare(
            ref Mesh.MeshData mesh,
            NativeArray<VertexAttributeDescriptor> layout,
            int faces,
            bool wide
        )
        {
            mesh.SetVertexBufferParams(faces * 4, layout);
            mesh.SetIndexBufferParams(faces * 6, wide ? IndexFormat.UInt32 : IndexFormat.UInt16);
        }

        private static void WriteQuad(
            bool wide,
            NativeArray<ushort> indices16,
            NativeArray<uint> indices32,
            int face,
            int v
        )
        {
            var i = face * 6;
            if (wide)
            {
                indices32[i] = (uint)v;
                indices32[i + 1] = (uint)(v + 1);
                indices32[i + 2] = (uint)(v + 2);
                indices32[i + 3] = (uint)v;
                indices32[i + 4] = (uint)(v + 2);
                indices32[i + 5] = (uint)(v + 3);
            }
            else
            {
                indices16[i] = (ushort)v;
                indices16[i + 1] = (ushort)(v + 1);
                indices16[i + 2] = (ushort)(v + 2);
                indices16[i + 3] = (ushort)v;
                indices16[i + 4] = (ushort)(v + 2);
                indices16[i + 5] = (ushort)(v + 3);
            }
        }

        private static void Finish(ref Mesh.MeshData mesh, int faces)
        {
            mesh.subMeshCount = 1;
            var descriptor = new SubMeshDescriptor(0, faces * 6)
            {
                bounds = new Bounds(new Vector3(Half, Half, Half), new Vector3(Size, Size, Size)),
                vertexCount = faces * 4,
            };
            mesh.SetSubMesh(
                0,
                descriptor,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            );
        }

        // Directions: 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z.
        private static int Stride(int dir) =>
            dir switch
            {
                0 => 1,
                1 => -1,
                2 => Padded * Padded,
                3 => -Padded * Padded,
                4 => Padded,
                _ => -Padded,
            };

        private static float3 Normal(int dir) =>
            dir switch
            {
                0 => new float3(1, 0, 0),
                1 => new float3(-1, 0, 0),
                2 => new float3(0, 1, 0),
                3 => new float3(0, -1, 0),
                4 => new float3(0, 0, 1),
                _ => new float3(0, 0, -1),
            };

        // Four corners of the face, packed 3 bits each (bit 0 x, bit 1 y, bit 2 z), in the
        // order that makes (v1 - v0) x (v2 - v0) point along the normal: clockwise seen from
        // outside, which is Unity's front face.
        private static int Corners(int dir) =>
            dir switch
            {
                0 => Pack(1, 3, 7, 5),
                1 => Pack(4, 6, 2, 0),
                2 => Pack(2, 6, 7, 3),
                3 => Pack(4, 0, 1, 5),
                4 => Pack(5, 7, 6, 4),
                _ => Pack(0, 2, 3, 1),
            };

        private static int Pack(int c0, int c1, int c2, int c3) =>
            c0 | (c1 << 3) | (c2 << 6) | (c3 << 9);

        private static float3 Corner(int packed, int i)
        {
            var c = (packed >> (i * 3)) & 7;
            return new float3(c & 1, (c >> 1) & 1, (c >> 2) & 1);
        }
    }
}
