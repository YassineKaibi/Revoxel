using Unity.Collections;

namespace Revoxel.World
{
    // Everything MaterialAt needs for one voxel column (D-063), computed once per column.
    public struct TerrainColumn
    {
        public const int MaxPockets = 4;

        // Floored surface height, voxels: y below it is solid.
        public int Surface;

        // Soil voxels under the surface (the surface voxel included); 0 is bare rock.
        public int Soil;

        // Top voxel: surface type, shore type or snow; 0 when the top is bare rock.
        public ushort Top;

        public ushort SoilId;

        // Added to y before cutting strata, voxels.
        public float Fold;

        public float StrataThickness;

        public FixedList32Bytes<ushort> Strata;

        // Buried waste: y from Min (inclusive) to Max (exclusive), voxel id.
        public FixedList64Bytes<PocketSpan> Pockets;

        public BiomeId Biome;
    }

    public struct PocketSpan
    {
        public int Min;
        public int Max;
        public ushort Id;
    }
}
