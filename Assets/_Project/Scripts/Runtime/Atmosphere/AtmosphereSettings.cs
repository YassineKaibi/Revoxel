using System;
using UnityEngine;

namespace Revoxel.Atmosphere
{
    // The look and effects of one weather (D-100). Placeholder values (Q-12).
    [Serializable]
    public struct WeatherProfile
    {
        [Tooltip("Share of the sky the clouds cover, 0 to 1.")]
        public float CloudCover;

        [Tooltip("How dark the clouds are, 0 to 1.")]
        public float CloudDarkness;

        [Tooltip("Exponential squared fog density, per meter.")]
        public float FogDensity;

        [Tooltip("Share of the sun and sky light taken away, 0 to 1.")]
        public float LightDim;

        [Tooltip("Strength of the sun's shadows, 0 to 1.")]
        public float ShadowStrength;

        [Tooltip("Wind speed, m/s (cloud drift and slanted rain).")]
        public float WindMetersPerSecond;

        [Tooltip("Rain or snow, 0 none to 1 the heaviest.")]
        public float Precipitation;

        [Tooltip("Added to the air temperature, °C.")]
        public float TemperatureOffsetC;

        [Tooltip("Chance weights in spring, summer, autumn and winter.")]
        public Vector4 SeasonWeights;

        public float Weight(int season) => Mathf.Max(0f, SeasonWeights[season]);
    }

    // Sky and light colors at one sun elevation (D-099). Colors are sRGB.
    [Serializable]
    public struct SkyKey
    {
        public float ElevationDegrees;
        public Color Zenith;
        public Color Horizon;
        public Color Sun;
        public float SunIntensity;
    }

    // Time, sky, weather and season settings (D-098 to D-101). Placeholder values (Q-12).
    [CreateAssetMenu(
        menuName = "Revoxel/Atmosphere/Atmosphere Settings",
        fileName = "Placeholder_AtmosphereSettings"
    )]
    public sealed class AtmosphereSettings : ScriptableObject
    {
        private const string Placeholder = " Placeholder value (Q-12).";

        [Header("Time (D-098)")]
        [SerializeField]
        [Tooltip("Real minutes per game day." + Placeholder)]
        private float _dayLengthMinutes = 24f;

        [SerializeField]
        [Tooltip("Game days per season; a year has four seasons." + Placeholder)]
        private int _daysPerSeason = 5;

        [SerializeField]
        [Tooltip("Hour of day a new world starts at." + Placeholder)]
        [Range(0f, 24f)]
        private float _startHour = 8f;

        [SerializeField]
        [Tooltip("Latitude of the island, degrees north." + Placeholder)]
        [Range(-80f, 80f)]
        private float _latitudeDegrees = 38f;

        [SerializeField]
        [Tooltip("Axial tilt, degrees (Earth's 23.44)." + Placeholder)]
        [Range(0f, 45f)]
        private float _axialTiltDegrees = 23.44f;

        [Header("Temperature (D-100)")]
        [SerializeField]
        [Tooltip("Air temperature offset at midsummer, °C." + Placeholder)]
        private float _summerOffsetC = 7f;

        [SerializeField]
        [Tooltip("Air temperature offset at midwinter, °C." + Placeholder)]
        private float _winterOffsetC = -8f;

        [SerializeField]
        [Tooltip("Half the day's temperature swing, °C (warmest at 15:00)." + Placeholder)]
        private float _daySwingC = 4f;

        [Header("Sky and light (D-099)")]
        [SerializeField]
        [Tooltip("Sky colors and sun light by sun elevation, in rising order." + Placeholder)]
        private SkyKey[] _skyKeys = Array.Empty<SkyKey>();

        [SerializeField]
        [Tooltip("sRGB color of the moonlight." + Placeholder)]
        private Color _moonColor = new Color(0.55f, 0.65f, 0.9f, 1f);

        [SerializeField]
        [Tooltip("Intensity of the moonlight with the moon high." + Placeholder)]
        private float _moonIntensity = 0.12f;

        [SerializeField]
        [Tooltip("Strength of the moon's shadows, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _moonShadowStrength = 0.5f;

        [SerializeField]
        [Tooltip("sRGB ambient light added at night (starlight)." + Placeholder)]
        private Color _nightAmbient = new Color(0.03f, 0.035f, 0.05f, 1f);

        [SerializeField]
        [Tooltip("Scale of the ambient light taken from the sky colors." + Placeholder)]
        private float _ambientScale = 1f;

        [SerializeField]
        [Tooltip("Brightness of the stars on a clear night." + Placeholder)]
        private float _starBrightness = 1.5f;

        [Header("Clouds (D-099)")]
        [SerializeField]
        [Tooltip("Height of the cloud layer, m." + Placeholder)]
        private float _cloudHeightMeters = 1800f;

        [SerializeField]
        [Tooltip("Size of the cloud features, m." + Placeholder)]
        private float _cloudScaleMeters = 2200f;

        [SerializeField]
        [Tooltip("How much the clouds darken the sunlight on the ground, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _cloudShadowStrength = 0.65f;

        [Header("Weather (D-100)")]
        [SerializeField]
        [Tooltip("Game hours of one weather period." + Placeholder)]
        private float _weatherPeriodHours = 6f;

        [SerializeField]
        [Tooltip(
            "Game hours at the end of a period over which the next weather blends in." + Placeholder
        )]
        private float _weatherBlendHours = 0.75f;

        [SerializeField]
        [Tooltip("Clear, Cloudy, Overcast, Rain, Storm, Fog, in that order." + Placeholder)]
        private WeatherProfile[] _weathers = Array.Empty<WeatherProfile>();

        [SerializeField]
        [Tooltip("Game hours for wet ground to dry." + Placeholder)]
        private float _wetnessDryHours = 3f;

        [SerializeField]
        [Tooltip("Game hours of rain to soak the ground." + Placeholder)]
        private float _wetnessSoakHours = 0.5f;

        [Header("Precipitation (D-100)")]
        [SerializeField]
        [Tooltip("Material of the rain particles (Revoxel/Precipitation).")]
        private Material _rainMaterial;

        [SerializeField]
        [Tooltip("Material of the snow particles (Revoxel/Precipitation).")]
        private Material _snowMaterial;

        [SerializeField]
        [Tooltip("Particles fall in a box of this half width around the camera, m." + Placeholder)]
        private float _precipitationRadiusMeters = 16f;

        [SerializeField]
        [Tooltip("Rain particles per second at full precipitation." + Placeholder)]
        private float _rainRate = 5000f;

        [SerializeField]
        [Tooltip("Snow particles per second at full precipitation." + Placeholder)]
        private float _snowRate = 1500f;

        [Header("Seasons (D-101)")]
        [SerializeField]
        [Tooltip("sRGB tint of deciduous leaves in spring." + Placeholder)]
        private Color _springLeafTint = new Color(1.05f, 1.15f, 0.8f, 1f);

        [SerializeField]
        [Tooltip("sRGB autumn leaf colors: yellow, orange, red." + Placeholder)]
        private Color[] _autumnLeafColors =
        {
            new Color(0.85f, 0.68f, 0.18f, 1f),
            new Color(0.86f, 0.42f, 0.12f, 1f),
            new Color(0.62f, 0.16f, 0.1f, 1f),
        };

        [SerializeField]
        [Tooltip("sRGB color of deciduous leaves in winter." + Placeholder)]
        private Color _winterLeafColor = new Color(0.4f, 0.3f, 0.2f, 1f);

        [SerializeField]
        [Tooltip("sRGB color of dry grass in late summer and autumn." + Placeholder)]
        private Color _dryGrassColor = new Color(0.72f, 0.64f, 0.34f, 1f);

        [SerializeField]
        [Tooltip("sRGB color of grass in winter." + Placeholder)]
        private Color _winterGrassColor = new Color(0.45f, 0.45f, 0.3f, 1f);

        [SerializeField]
        [Tooltip("sRGB color of snow." + Placeholder)]
        private Color _snowColor = new Color(0.92f, 0.94f, 0.98f, 1f);

        [SerializeField]
        [Tooltip("Snow starts to cover top faces below this air temperature, °C." + Placeholder)]
        private float _snowStartC = 1f;

        [SerializeField]
        [Tooltip("Top faces are fully white below this air temperature, °C." + Placeholder)]
        private float _snowFullC = -2f;

        public float DayLengthMinutes => _dayLengthMinutes;
        public int DaysPerSeason => _daysPerSeason;
        public float StartHour => _startHour;
        public float LatitudeDegrees => _latitudeDegrees;
        public float AxialTiltDegrees => _axialTiltDegrees;
        public float SummerOffsetC => _summerOffsetC;
        public float WinterOffsetC => _winterOffsetC;
        public float DaySwingC => _daySwingC;
        public SkyKey[] SkyKeys => _skyKeys;
        public Color MoonColor => _moonColor;
        public float MoonIntensity => _moonIntensity;
        public float MoonShadowStrength => _moonShadowStrength;
        public Color NightAmbient => _nightAmbient;
        public float AmbientScale => _ambientScale;
        public float StarBrightness => _starBrightness;
        public float CloudHeightMeters => _cloudHeightMeters;
        public float CloudScaleMeters => _cloudScaleMeters;
        public float CloudShadowStrength => _cloudShadowStrength;
        public float WeatherPeriodHours => _weatherPeriodHours;
        public float WeatherBlendHours => _weatherBlendHours;
        public WeatherProfile[] Weathers => _weathers;
        public float WetnessDryHours => _wetnessDryHours;
        public float WetnessSoakHours => _wetnessSoakHours;
        public Material RainMaterial => _rainMaterial;
        public Material SnowMaterial => _snowMaterial;
        public float PrecipitationRadiusMeters => _precipitationRadiusMeters;
        public float RainRate => _rainRate;
        public float SnowRate => _snowRate;
        public Color SpringLeafTint => _springLeafTint;
        public Color[] AutumnLeafColors => _autumnLeafColors;
        public Color WinterLeafColor => _winterLeafColor;
        public Color DryGrassColor => _dryGrassColor;
        public Color WinterGrassColor => _winterGrassColor;
        public Color SnowColor => _snowColor;
        public float SnowStartC => _snowStartC;
        public float SnowFullC => _snowFullC;

        public bool TryValidate(out string error)
        {
            if (_dayLengthMinutes <= 0f || _daysPerSeason < 1)
            {
                error = "Day length must be positive and a season at least one day.";
                return false;
            }

            if (_skyKeys == null || _skyKeys.Length < 2)
            {
                error = "At least two sky keys are needed.";
                return false;
            }

            for (var i = 1; i < _skyKeys.Length; i++)
            {
                if (_skyKeys[i].ElevationDegrees <= _skyKeys[i - 1].ElevationDegrees)
                {
                    error = "Sky keys must rise in elevation.";
                    return false;
                }
            }

            var kinds = Enum.GetValues(typeof(WeatherKind)).Length;
            if (_weathers == null || _weathers.Length != kinds)
            {
                error = $"There must be one weather profile per weather ({kinds}).";
                return false;
            }

            var seasonWeights = Vector4.zero;
            foreach (var w in _weathers)
            {
                if (
                    w.CloudCover < 0f
                    || w.CloudCover > 1f
                    || w.FogDensity < 0f
                    || w.Precipitation < 0f
                    || w.Precipitation > 1f
                    || w.ShadowStrength < 0f
                    || w.ShadowStrength > 1f
                )
                {
                    error =
                        "Cloud cover, precipitation and shadow strength are 0 to 1, fog not negative.";
                    return false;
                }

                seasonWeights += Vector4.Max(w.SeasonWeights, Vector4.zero);
            }

            if (
                seasonWeights.x <= 0f
                || seasonWeights.y <= 0f
                || seasonWeights.z <= 0f
                || seasonWeights.w <= 0f
            )
            {
                error = "Every season needs a weather with a positive weight.";
                return false;
            }

            if (
                _weatherPeriodHours <= 0f
                || _weatherBlendHours < 0f
                || _weatherBlendHours > _weatherPeriodHours
                || _wetnessDryHours <= 0f
                || _wetnessSoakHours <= 0f
                || _cloudScaleMeters <= 0f
                || _snowStartC <= _snowFullC
                || _autumnLeafColors == null
                || _autumnLeafColors.Length != 3
            )
            {
                error =
                    "Weather period, wetness hours and cloud scale must be positive, the blend within the period, snow start above full, three autumn colors.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
