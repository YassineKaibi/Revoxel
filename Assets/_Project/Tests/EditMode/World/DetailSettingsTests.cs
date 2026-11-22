using NUnit.Framework;
using Revoxel.World;
using UnityEditor;

namespace Revoxel.Tests.EditMode
{
    public sealed class DetailSettingsTests
    {
        public const string DetailSettingsPath =
            "Assets/_Project/Data/World/Placeholder_DistanceDetailSettings.asset";

        [Test]
        public void DetailSettings_AssetIsValid()
        {
            var settings = AssetDatabase.LoadAssetAtPath<DistanceDetailSettings>(
                DetailSettingsPath
            );

            Assert.That(settings, Is.Not.Null, $"No settings at {DetailSettingsPath}");
            Assert.That(settings.name, Does.StartWith("Placeholder_"));
            Assert.That(settings.TryValidate(out var error), Is.True, error);
            Assert.That(
                settings.TryValidateWith(WorldAssets.LoadStreamingSettings(), out error),
                Is.True,
                error
            );

            // D-055: six coarse levels, about 2 km.
            Assert.That(settings.Levels, Is.EqualTo(6));
            Assert.That(settings.ViewDistanceMeters, Is.InRange(2000f, 2500f));
        }
    }
}
