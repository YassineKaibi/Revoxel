using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Chunk pass: fills one chunk from the column heights and zones, then writes the parts
    // of every tree that reach into it, including trees rooted in neighboring chunks.
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

            PlaceTrees();
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

        private void PlaceTrees()
        {
            var r = Params.LeafRadius;
            for (var z = Origin.z - r; z < Origin.z + Size + r; z++)
            for (var x = Origin.x - r; x < Origin.x + Size + r; x++)
            {
                if (!WorldGenerator.IsColumnInWorld(x, z))
                {
                    continue;
                }

                var height = WorldGenerator.ColumnHeight(Params, x, z);
                var zone = WorldGenerator.ColumnZone(Params, x, z, height);
                if (WorldGenerator.IsTreeColumn(Params, x, z, zone))
                {
                    PlaceTree(new int3(x, height, z));
                }
            }
        }

        // Trunk from the ground up; leaves on the top two trunk layers (radius r, corners
        // cut) and a cap of radius 1 above. Leaves only replace Air; the trunk wins.
        private void PlaceTree(int3 root)
        {
            var trunk = Params.TrunkHeight;
            var r = Params.LeafRadius;
            var top = root.y + trunk - 1;
            for (var y = top - 1; y <= top; y++)
            for (var dz = -r; dz <= r; dz++)
            for (var dx = -r; dx <= r; dx++)
            {
                if (math.abs(dx) == r && math.abs(dz) == r && r > 0)
                {
                    continue;
                }

                SetLeaves(new int3(root.x + dx, y, root.z + dz));
            }

            var cap = math.min(1, r);
            for (var dz = -cap; dz <= cap; dz++)
            for (var dx = -cap; dx <= cap; dx++)
            {
                SetLeaves(new int3(root.x + dx, top + 1, root.z + dz));
            }

            for (var y = root.y; y <= top; y++)
            {
                SetBlock(new int3(root.x, y, root.z), Params.Log);
            }
        }

        private void SetLeaves(int3 world)
        {
            var local = world - Origin;
            if (Coords.IsLocalInRange(local))
            {
                var index = Coords.LocalIndex(local);
                if (Blocks[index] == 0)
                {
                    Blocks[index] = Params.Leaves;
                }
            }
        }

        private void SetBlock(int3 world, byte block)
        {
            var local = world - Origin;
            if (Coords.IsLocalInRange(local))
            {
                Blocks[Coords.LocalIndex(local)] = block;
            }
        }
    }
}
