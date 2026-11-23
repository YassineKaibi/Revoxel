using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Revoxel.World
{
    // Meshes one padded chunk (D-051). A voxel A gets a render face toward its neighbor B
    // when A is not Air, B is not opaque and A differs from B; a collision face when A is
    // solid and B is not. Per direction and slice, faces with the same voxel id and the
    // same four corner occlusion values are merged greedily into rectangles; collision
    // faces merge by solidity only. Occlusion comes from the layer in front of the face,
    // so it appears only at concave corners.
    [BurstCompile]
    public struct GreedyMeshJob : IJob
    {
        private const int Size = WorldConstants.ChunkSize;
        private const int IdBits = 10;
        private const uint IdMask = (1u << IdBits) - 1u;

        [ReadOnly]
        public NativeArray<ushort> Padded;

        [ReadOnly]
        public NativeArray<VoxelRenderData> RenderData;

        [ReadOnly]
        public NativeArray<VertexAttributeDescriptor> RenderLayout;

        [ReadOnly]
        public NativeArray<VertexAttributeDescriptor> CollisionLayout;

        public Mesh.MeshData RenderMesh;
        public Mesh.MeshData CollisionMesh;

        // Coarse detail chunks have no collider: the collision mesh is written empty.
        public bool SkipCollision;

        public void Execute()
        {
            var vertices = new NativeList<VoxelVertex>(1024, Allocator.Temp);
            var indices = new NativeList<int>(1536, Allocator.Temp);
            var collisionVertices = new NativeList<float3>(1024, Allocator.Temp);
            var collisionIndices = new NativeList<int>(1536, Allocator.Temp);
            var renderMask = new NativeArray<uint>(Size * Size, Allocator.Temp);
            var collisionMask = new NativeArray<uint>(Size * Size, Allocator.Temp);

            for (var axis = 0; axis < 3; axis++)
            {
                var u = (axis + 1) % 3;
                var v = (axis + 2) % 3;
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    for (var d = 0; d < Size; d++)
                    {
                        BuildMasks(axis, u, v, sign, d, renderMask, collisionMask);
                        EmitRender(axis, u, v, sign, d, renderMask, ref vertices, ref indices);
                        if (SkipCollision)
                        {
                            continue;
                        }

                        EmitCollision(
                            axis,
                            u,
                            v,
                            sign,
                            d,
                            collisionMask,
                            ref collisionVertices,
                            ref collisionIndices
                        );
                    }
                }
            }

            WriteRender(vertices, indices);
            WriteCollision(collisionVertices, collisionIndices);
        }

        private void BuildMasks(
            int axis,
            int u,
            int v,
            int sign,
            int d,
            NativeArray<uint> renderMask,
            NativeArray<uint> collisionMask
        )
        {
            var normal = int3.zero;
            normal[axis] = sign;
            var du = int3.zero;
            du[u] = 1;
            var dv = int3.zero;
            dv[v] = 1;
            for (var j = 0; j < Size; j++)
            for (var i = 0; i < Size; i++)
            {
                var pos = int3.zero;
                pos[axis] = d;
                pos[u] = i;
                pos[v] = j;
                var a = At(pos);
                var front = pos + normal;
                var b = At(front);
                var cell = i + Size * j;

                collisionMask[cell] = Solid(a) && !Solid(b) ? 1u : 0u;
                if (a == 0 || Opaque(b) || a == b)
                {
                    renderMask[cell] = 0u;
                    continue;
                }

                // Corners 0 to 3 at (-u, -v), (+u, -v), (+u, +v), (-u, +v).
                var ao0 = Occlusion(front, -du, -dv);
                var ao1 = Occlusion(front, du, -dv);
                var ao2 = Occlusion(front, du, dv);
                var ao3 = Occlusion(front, -du, dv);
                renderMask[cell] =
                    a
                    | (ao0 << IdBits)
                    | (ao1 << (IdBits + 2))
                    | (ao2 << (IdBits + 4))
                    | (ao3 << (IdBits + 6));
            }
        }

        // 3 is open, 0 fully occluded (both sides opaque).
        private uint Occlusion(int3 front, int3 sideU, int3 sideV)
        {
            var side1 = Opaque(At(front + sideU)) ? 1u : 0u;
            var side2 = Opaque(At(front + sideV)) ? 1u : 0u;
            var corner = Opaque(At(front + sideU + sideV)) ? 1u : 0u;
            return side1 == 1u && side2 == 1u ? 0u : 3u - (side1 + side2 + corner);
        }

        private void EmitRender(
            int axis,
            int u,
            int v,
            int sign,
            int d,
            NativeArray<uint> mask,
            ref NativeList<VoxelVertex> vertices,
            ref NativeList<int> indices
        )
        {
            var normal = float3.zero;
            normal[axis] = sign;
            for (var j = 0; j < Size; j++)
            for (var i = 0; i < Size; )
            {
                var key = mask[i + Size * j];
                if (key == 0u)
                {
                    i++;
                    continue;
                }

                Grow(mask, key, i, j, out var w, out var h);
                var ao = new float4(
                    (key >> IdBits) & 3u,
                    (key >> (IdBits + 2)) & 3u,
                    (key >> (IdBits + 4)) & 3u,
                    (key >> (IdBits + 6)) & 3u
                );
                var id = key & IdMask;
                var start = vertices.Length;
                for (var c = 0; c < 4; c++)
                {
                    vertices.Add(
                        new VoxelVertex
                        {
                            Position = Corner(axis, u, v, sign, d, i, j, w, h, c),
                            Normal = normal,
                            MaterialAo = new float2(id, ao[c] / 3f),
                        }
                    );
                }

                AddQuad(ref indices, start, sign, ao.x + ao.z < ao.y + ao.w);
                i += w;
            }
        }

        private void EmitCollision(
            int axis,
            int u,
            int v,
            int sign,
            int d,
            NativeArray<uint> mask,
            ref NativeList<float3> vertices,
            ref NativeList<int> indices
        )
        {
            for (var j = 0; j < Size; j++)
            for (var i = 0; i < Size; )
            {
                if (mask[i + Size * j] == 0u)
                {
                    i++;
                    continue;
                }

                Grow(mask, 1u, i, j, out var w, out var h);
                var start = vertices.Length;
                for (var c = 0; c < 4; c++)
                {
                    vertices.Add(Corner(axis, u, v, sign, d, i, j, w, h, c));
                }

                AddQuad(ref indices, start, sign, false);
                i += w;
            }
        }

        // Widest run of key along u from (i, j), then as many full rows along v as match.
        // Clears the merged cells.
        private static void Grow(
            NativeArray<uint> mask,
            uint key,
            int i,
            int j,
            out int w,
            out int h
        )
        {
            w = 1;
            while (i + w < Size && mask[i + w + Size * j] == key)
            {
                w++;
            }

            h = 1;
            while (j + h < Size)
            {
                var row = Size * (j + h);
                var full = true;
                for (var k = 0; k < w; k++)
                {
                    if (mask[i + k + row] != key)
                    {
                        full = false;
                        break;
                    }
                }

                if (!full)
                {
                    break;
                }

                h++;
            }

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                mask[i + x + Size * (j + y)] = 0u;
            }
        }

        // Corners 0 to 3 of the merged rectangle at (i, j) to (i + w, j + h), in meters.
        private static float3 Corner(
            int axis,
            int u,
            int v,
            int sign,
            int d,
            int i,
            int j,
            int w,
            int h,
            int c
        )
        {
            var p = float3.zero;
            p[axis] = sign > 0 ? d + 1 : d;
            p[u] = c == 1 || c == 2 ? i + w : i;
            p[v] = c >= 2 ? j + h : j;
            return p * WorldConstants.VoxelSize;
        }

        // Unity treats clockwise triangles as front faces; with u x v along the axis, the
        // order 0-1-2 faces +axis. flip uses the 1-3 diagonal (brighter corners).
        private static void AddQuad(ref NativeList<int> indices, int start, int sign, bool flip)
        {
            var a = flip ? 1 : 0;
            var b = flip ? 2 : 1;
            var c = flip ? 3 : 2;
            var e = flip ? 0 : 3;
            if (sign > 0)
            {
                indices.Add(start + a);
                indices.Add(start + b);
                indices.Add(start + c);
                indices.Add(start + a);
                indices.Add(start + c);
                indices.Add(start + e);
            }
            else
            {
                indices.Add(start + a);
                indices.Add(start + c);
                indices.Add(start + b);
                indices.Add(start + a);
                indices.Add(start + e);
                indices.Add(start + c);
            }
        }

        private void WriteRender(NativeList<VoxelVertex> vertices, NativeList<int> indices)
        {
            RenderMesh.SetVertexBufferParams(vertices.Length, RenderLayout);
            vertices.AsArray().CopyTo(RenderMesh.GetVertexData<VoxelVertex>());
            WriteIndices(RenderMesh, vertices.Length, indices);
        }

        private void WriteCollision(NativeList<float3> vertices, NativeList<int> indices)
        {
            CollisionMesh.SetVertexBufferParams(vertices.Length, CollisionLayout);
            vertices.AsArray().CopyTo(CollisionMesh.GetVertexData<float3>());
            WriteIndices(CollisionMesh, vertices.Length, indices);
        }

        private static void WriteIndices(
            Mesh.MeshData mesh,
            int vertexCount,
            NativeList<int> indices
        )
        {
            var wide = vertexCount > ushort.MaxValue;
            mesh.SetIndexBufferParams(
                indices.Length,
                wide ? IndexFormat.UInt32 : IndexFormat.UInt16
            );
            if (wide)
            {
                var data = mesh.GetIndexData<uint>();
                for (var k = 0; k < indices.Length; k++)
                {
                    data[k] = (uint)indices[k];
                }
            }
            else
            {
                var data = mesh.GetIndexData<ushort>();
                for (var k = 0; k < indices.Length; k++)
                {
                    data[k] = (ushort)indices[k];
                }
            }

            mesh.subMeshCount = 1;
            mesh.SetSubMesh(
                0,
                new SubMeshDescriptor(0, indices.Length),
                MeshUpdateFlags.DontRecalculateBounds
                    | MeshUpdateFlags.DontValidateIndices
                    | MeshUpdateFlags.DontNotifyMeshUsers
            );
        }

        private uint At(int3 p) => Padded[Coords.PaddedIndex(p.x, p.y, p.z)];

        private bool Opaque(uint id) => RenderData[(int)id].IsOpaque;

        private bool Solid(uint id) => RenderData[(int)id].IsSolid;
    }
}
