using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Revoxel.World
{
    // Copies a chunk and the touching layer of each face neighbor into the 18 x 18 x 18
    // meshing input (D-031). Edges and corners stay Air: face culling never reads them.
    // A missing neighbor is passed as an all-Air chunk.
    [BurstCompile]
    public struct ChunkPadJob : IJob
    {
        private const int Size = WorldConstants.ChunkSize;
        private const int Last = WorldConstants.ChunkSize - 1;

        [ReadOnly]
        public NativeArray<byte> Center;

        [ReadOnly]
        public NativeArray<byte> PosX;

        [ReadOnly]
        public NativeArray<byte> NegX;

        [ReadOnly]
        public NativeArray<byte> PosY;

        [ReadOnly]
        public NativeArray<byte> NegY;

        [ReadOnly]
        public NativeArray<byte> PosZ;

        [ReadOnly]
        public NativeArray<byte> NegZ;

        [WriteOnly]
        public NativeArray<byte> Padded;

        public void Execute()
        {
            for (var i = 0; i < WorldConstants.PaddedBlocks; i++)
            {
                Padded[i] = 0;
            }

            for (var y = 0; y < Size; y++)
            for (var z = 0; z < Size; z++)
            for (var x = 0; x < Size; x++)
            {
                Padded[Coords.PaddedIndex(x, y, z)] = Center[Coords.LocalIndex(x, y, z)];
            }

            for (var a = 0; a < Size; a++)
            for (var b = 0; b < Size; b++)
            {
                // a and b run over the two axes of each face layer.
                Padded[Coords.PaddedIndex(Size, a, b)] = PosX[Coords.LocalIndex(0, a, b)];
                Padded[Coords.PaddedIndex(-1, a, b)] = NegX[Coords.LocalIndex(Last, a, b)];
                Padded[Coords.PaddedIndex(a, Size, b)] = PosY[Coords.LocalIndex(a, 0, b)];
                Padded[Coords.PaddedIndex(a, -1, b)] = NegY[Coords.LocalIndex(a, Last, b)];
                Padded[Coords.PaddedIndex(a, b, Size)] = PosZ[Coords.LocalIndex(a, b, 0)];
                Padded[Coords.PaddedIndex(a, b, -1)] = NegZ[Coords.LocalIndex(a, b, Last)];
            }
        }
    }
}
