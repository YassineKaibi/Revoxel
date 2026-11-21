using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Chunk pass: fills one chunk from the column heights and zones.
    [BurstCompile]
    public struct ChunkGenJob : IJob
    {
        private const int Size = WorldConstants.ChunkSize;

        public WorldGenParams Params;
        public int3 Origin;
        public NativeArray<byte> Blocks;

        public void Execute()
        {
            for (var lz = 0; lz < Size; lz++)
            for (var lx = 0; lx < Size; lx++)
            {
                var x = Origin.x + lx;
                var z = Origin.z + lz;
                var height = WorldGenerator.ColumnHeight(Params, x, z);
                var zone = WorldGenerator.ColumnZone(Params, x, z, height);
                FillColumn(lx, lz, height, zone);
            }
        }

        private void FillColumn(int lx, int lz, int height, ZoneId zone)
        {
            for (var ly = 0; ly < Size; ly++)
            {
                var y = Origin.y + ly;
                byte block;
                if (y < height)
                {
                    block =
                        y < height - Params.StoneDepth
                            ? Params.Stone
                            : Surface(zone, y == height - 1);
                }
                else
                {
                    block = y < Params.SeaLevel ? Params.Water : (byte)0;
                }

                Blocks[Coords.LocalIndex(lx, ly, lz)] = block;
            }
        }

        private byte Surface(ZoneId zone, bool isTop) =>
            zone switch
            {
                ZoneId.Decharge => Params.CompactedWaste,
                ZoneId.Foret => isTop ? Params.Grass : Params.Dirt,
                _ => Params.Sand,
            };
    }
}
