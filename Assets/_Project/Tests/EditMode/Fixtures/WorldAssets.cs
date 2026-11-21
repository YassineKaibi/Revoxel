using Revoxel.World;
using UnityEditor;

namespace Revoxel.Tests.EditMode
{
    // Paths of the committed World data assets used by tests.
    public static class WorldAssets
    {
        public const string BlocksFolder = "Assets/_Project/Data/Blocks";
        public const string RegistryPath = BlocksFolder + "/BlockRegistry.asset";
        public const string MaterialPath = "Assets/_Project/Art/Materials/Mat_Voxel.mat";

        public static BlockRegistry LoadRegistry() =>
            AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryPath);
    }
}
