using Revoxel.World;
using UnityEditor;

namespace Revoxel.Tests.EditMode
{
    // Paths of the committed World data assets used by tests.
    public static class WorldAssets
    {
        public const string VoxelsFolder = "Assets/_Project/Data/Voxels";
        public const string RegistryPath = VoxelsFolder + "/VoxelRegistry.asset";
        public const string MaterialPath = "Assets/_Project/Art/Materials/Mat_Voxel.mat";

        public const string TerrainSettingsPath =
            "Assets/_Project/Data/World/Placeholder_TerrainSettings.asset";

        public static VoxelRegistry LoadRegistry() =>
            AssetDatabase.LoadAssetAtPath<VoxelRegistry>(RegistryPath);

        public static TerrainSettings LoadTerrainSettings() =>
            AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainSettingsPath);

        public static TerrainParams TerrainParams(int seed) =>
            World.TerrainParams.Create(new WorldSeed(seed), LoadTerrainSettings());
    }
}
