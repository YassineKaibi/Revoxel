using Unity.Mathematics;

namespace Revoxel.World
{
    // The coarse hydrology grid (D-077): 250 x 250 cells of 32 m (256 voxels) covering the
    // island's voxel x and z from -32,000 to 31,999 (D-050). Structure, not tuning.
    public static class HydroGrid
    {
        public const int CellVoxels = 256;
        public const int Size = 250;
        public const int CellCount = Size * Size;
        public const int OriginVoxel = -32000;

        // One cell's area, km².
        public const float CellAreaKm2 =
            CellVoxels * WorldConstants.VoxelSize * CellVoxels * WorldConstants.VoxelSize / 1e6f;

        public static int Index(int2 cell) => cell.x + Size * cell.y;

        public static int2 Cell(int index) => new int2(index % Size, index / Size);

        public static int2 CellOf(float2 voxel) =>
            (int2)math.floor((voxel - OriginVoxel) / CellVoxels);

        public static float2 Center(int2 cell) => OriginVoxel + ((float2)cell + 0.5f) * CellVoxels;

        public static bool InGrid(int2 cell) => math.all(cell >= 0) && math.all(cell < Size);
    }
}
