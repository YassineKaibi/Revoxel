using NUnit.Framework;
using UnityEditor;

namespace Revoxel.Tests.EditMode
{
    public sealed class BuildSettingsTests
    {
        private const string BootstrapPath = "Assets/_Project/Scenes/Bootstrap.unity";
        private const string IslandPath = "Assets/_Project/Scenes/Island.unity";

        [Test]
        public void BuildSettings_ContainsBootstrapThenIsland()
        {
            var scenes = EditorBuildSettings.scenes;

            Assert.That(scenes.Length, Is.GreaterThanOrEqualTo(2), "Scene list too short");
            Assert.That(scenes[0].path, Is.EqualTo(BootstrapPath));
            Assert.That(scenes[1].path, Is.EqualTo(IslandPath));
            Assert.That(scenes[0].enabled && scenes[1].enabled, Is.True, "Both scenes enabled");
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(IslandPath), Is.Not.Null);
        }
    }
}
