using NUnit.Framework;
using Revoxel.Atmosphere;
using Revoxel.World;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;

namespace Revoxel.Tests.EditMode
{
    // Time, sun, weather and climate (D-098, D-100, D-101).
    public sealed class AtmosphereTests
    {
        private const string SettingsPath =
            "Assets/_Project/Data/Atmosphere/Placeholder_AtmosphereSettings.asset";

        private static AtmosphereSettings Settings() =>
            AssetDatabase.LoadAssetAtPath<AtmosphereSettings>(SettingsPath);

        [Test]
        public void AtmosphereSettings_PlaceholderValidates()
        {
            var settings = Settings();
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.TryValidate(out var error), Is.True, error);
            Assert.That(settings.RainMaterial, Is.Not.Null);
            Assert.That(settings.SnowMaterial, Is.Not.Null);
        }

        [Test]
        public void Atmosphere_SunPath_DayNightAndSeasons()
        {
            const int days = 5;
            const float latitude = 38f;
            const float tilt = 23.44f;

            // Midsummer (a quarter into summer) and midwinter, at noon and midnight.
            var summer = 6.5;
            var winter = 16.5;
            float Elevation(double d) =>
                AtmosphereClock.ElevationDegrees(
                    AtmosphereClock.SunDirection(d, days, latitude, tilt)
                );

            Assert.That(Elevation(summer), Is.EqualTo(90f - latitude + tilt).Within(1.5f));
            Assert.That(Elevation(winter), Is.EqualTo(90f - latitude - tilt).Within(1.5f));
            Assert.That(Elevation(summer + 0.5), Is.LessThan(0f), "midnight");
            Assert.That(
                Elevation(winter - 0.25),
                Is.LessThan(Elevation(summer - 0.25)),
                "short winter mornings"
            );

            var morning = AtmosphereClock.SunDirection(summer - 0.15, days, latitude, tilt);
            Assert.That(morning.x, Is.GreaterThan(0f), "the sun rises in the east");
            Assert.That(
                AtmosphereClock.SunDirection(summer, days, latitude, tilt).z,
                Is.LessThan(0f),
                "noon sun in the south"
            );

            Assert.That(AtmosphereClock.Season(0.0, days), Is.EqualTo(AtmosphereClock.Spring));
            Assert.That(AtmosphereClock.Season(summer, days), Is.EqualTo(AtmosphereClock.Summer));
            Assert.That(AtmosphereClock.Season(12.0, days), Is.EqualTo(AtmosphereClock.Autumn));
            Assert.That(AtmosphereClock.Season(winter, days), Is.EqualTo(AtmosphereClock.Winter));
            Assert.That(
                AtmosphereClock.Season(20.0, days),
                Is.EqualTo(AtmosphereClock.Spring),
                "the year wraps"
            );
            Assert.That(
                AtmosphereClock.SeasonOffsetC(7.5, days, 7f, -8f),
                Is.GreaterThan(AtmosphereClock.SeasonOffsetC(17.5, days, 7f, -8f))
            );
            Assert.That(
                AtmosphereClock.SeasonWeights(12.5, days).y,
                Is.EqualTo(1f).Within(1e-4f),
                "midautumn"
            );
            Assert.That(
                AtmosphereClock.SeasonWeights(17.5, days).z,
                Is.EqualTo(1f).Within(1e-4f),
                "midwinter"
            );
        }

        [Test]
        public void Weather_DeterministicAndContinuous()
        {
            var settings = Settings();
            var kinds = new bool[settings.Weathers.Length];
            var previous = WeatherSchedule.At(Seeds.Fixture, 0.0, settings);
            var differs = false;
            for (var d = 0.0; d < 40.0; d += 0.002)
            {
                var a = WeatherSchedule.At(Seeds.Fixture, d, settings);
                var b = WeatherSchedule.At(Seeds.Fixture, d, settings);
                Assert.That(b.CloudCover, Is.EqualTo(a.CloudCover));
                Assert.That(b.Kind, Is.EqualTo(a.Kind));
                Assert.That(
                    math.abs(a.CloudCover - previous.CloudCover),
                    Is.LessThan(0.15f),
                    $"day {d}"
                );
                Assert.That(
                    math.abs(a.FogDensity - previous.FogDensity),
                    Is.LessThan(0.002f),
                    $"day {d}"
                );
                kinds[(int)a.Kind] = true;
                differs |= WeatherSchedule.At(Seeds.Other, d, settings).Kind != a.Kind;
                previous = a;
            }

            var seen = 0;
            foreach (var k in kinds)
            {
                seen += k ? 1 : 0;
            }

            Assert.That(seen, Is.GreaterThanOrEqualTo(4), "weathers seen in 40 days");
            Assert.That(differs, Is.True, "another seed has other weather");
        }

        [Test]
        public void ClimateMap_MatchesGenerator()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            using var temperatures = ClimateMap.Sample(p, Allocator.TempJob);
            for (var i = 0; i < HydroGrid.CellCount; i += 997)
            {
                var center = (int2)math.floor(HydroGrid.Center(HydroGrid.Cell(i)));
                var sample = TerrainGenerator.Sample(p, center.x, center.y);
                Assert.That(
                    temperatures[i],
                    Is.EqualTo(sample.SeaTemperatureC).Within(1e-3f),
                    $"cell {i}"
                );
            }

            var texture = ClimateMap.CreateTexture(temperatures);
            try
            {
                Assert.That(texture.width, Is.EqualTo(HydroGrid.Size));
                Assert.That(
                    texture.GetPixel(10, 20).r,
                    Is.EqualTo(temperatures[HydroGrid.Index(new int2(10, 20))]).Within(1e-3f)
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
