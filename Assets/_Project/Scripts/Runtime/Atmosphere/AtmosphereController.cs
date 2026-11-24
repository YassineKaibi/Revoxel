using Revoxel.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR || REVOXEL_DEBUG
using UnityEngine.InputSystem;
#endif

namespace Revoxel.Atmosphere
{
    // Runs the day, the seasons and the weather in Island.unity (D-098 to D-103): moves the
    // sun or moon light, sets fog, ambient probe, skybox and the sky globals, and drives
    // rain and snow around the camera. World time is the only state (game days).
    public sealed class AtmosphereController : MonoBehaviour
    {
        // Below this sun elevation the light is the moon's (D-098).
        private const float MoonSwitchDegrees = -4f;
        private const float NightFullDegrees = -12f;
        private const float DebugTimeScale = 120f;

        [SerializeField]
        private AtmosphereSettings _settings;

        [SerializeField]
        private Light _sun;

        [SerializeField]
        private VoxelWorld _world;

        [SerializeField]
        [Tooltip("Skybox material (Revoxel/Sky).")]
        private Material _skyMaterial;

        [SerializeField]
        [Tooltip("Seed of the weather schedule; the world's seed when a world is set.")]
        private int _seed = 42;

        private double _days;
        private float _timeScale = 1f;
        private float _wetness;
        private float _airTemperature;
        private float _climateTemperature;
        private float _nextClimateQuery;
        private Vector2 _cloudOffset;
        private WeatherState _weather;
        private bool _hasForcedWeather;
        private WeatherKind _forcedWeather;
        private long _forcedPeriod;
        private Precipitation _rain;
        private Precipitation _snow;
        private bool _snowing;

        public double Days => _days;

        public float HourOfDay => (float)(AtmosphereClock.DayFraction(_days) * 24.0);

        public int DayIndex => AtmosphereClock.DayIndex(_days);

        public int Season => AtmosphereClock.Season(_days, _settings.DaysPerSeason);

        public WeatherState Weather => _weather;

        // Snow is falling where the camera is (cold precipitation).
        public bool IsSnowing => _snowing;

        // Air temperature at the camera, °C (D-100).
        public float CameraAirTemperatureC => _airTemperature;

        public AtmosphereSettings Settings => _settings;

        // Air temperature at a position, °C: biome climate and altitude, season, day and
        // weather (spec 6.2; for the survival needs of WP-11).
        public float AirTemperatureAt(Vector3 position)
        {
            var climate = 0f;
            if (_world != null && _world.TryGetClimateTemperature(position, out var c))
            {
                climate = c;
            }

            return climate + Offsets();
        }

        // World time in game days; a save stores this (WP-08).
        public void SetDays(double days)
        {
            _days = math.max(0.0, days);
            _hasForcedWeather = false;
        }

        private void Awake()
        {
            if (_settings == null || !_settings.TryValidate(out var error))
            {
                Debug.LogError("[Atmosphere] Settings missing or invalid.", this);
                enabled = false;
                return;
            }

            _days = _settings.StartHour / 24.0;
            if (_world != null)
            {
                _seed = _world.Seed;
            }

            if (_skyMaterial != null)
            {
                RenderSettings.skybox = _skyMaterial;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            if (_sun != null)
            {
                RenderSettings.sun = _sun;
            }

            AtmosphereShaderGlobals.SetSeasonColors(_settings);
            var radius = _settings.PrecipitationRadiusMeters;
            if (_settings.RainMaterial != null)
            {
                _rain = new Precipitation(
                    transform,
                    "Rain",
                    _settings.RainMaterial,
                    false,
                    radius,
                    _settings.RainRate
                );
            }

            if (_settings.SnowMaterial != null)
            {
                _snow = new Precipitation(
                    transform,
                    "Snow",
                    _settings.SnowMaterial,
                    true,
                    radius,
                    _settings.SnowRate
                );
            }

            Apply(0f);
        }

        private void OnDestroy()
        {
            _rain?.Destroy();
            _snow?.Destroy();
            AtmosphereShaderGlobals.SetNeutral();
        }

        private void Update()
        {
#if UNITY_EDITOR || REVOXEL_DEBUG
            DebugKeys();
#endif
            var deltaDays = Time.deltaTime * _timeScale / (_settings.DayLengthMinutes * 60.0);
            _days += deltaDays;
            Apply((float)deltaDays);
        }

        private float Offsets() =>
            AtmosphereClock.SeasonOffsetC(
                _days,
                _settings.DaysPerSeason,
                _settings.SummerOffsetC,
                _settings.WinterOffsetC
            )
            + AtmosphereClock.DayOffsetC(_days, _settings.DaySwingC)
            + _weather.TemperatureOffsetC;

        private void Apply(float deltaDays)
        {
            _weather = WeatherSchedule.At(_seed, _days, _settings);
            if (_hasForcedWeather)
            {
                if (WeatherSchedule.Period(_days, _settings.WeatherPeriodHours) != _forcedPeriod)
                {
                    _hasForcedWeather = false;
                }
                else
                {
                    _weather = WeatherState.From(
                        _settings.Weathers[(int)_forcedWeather],
                        _forcedWeather
                    );
                }
            }

            var sun = AtmosphereClock.SunDirection(
                _days,
                _settings.DaysPerSeason,
                _settings.LatitudeDegrees,
                _settings.AxialTiltDegrees
            );
            var moon = AtmosphereClock.MoonDirection(sun);
            var elevation = AtmosphereClock.ElevationDegrees(sun);
            EvaluateSky(
                elevation,
                out var zenith,
                out var horizon,
                out var sunColor,
                out var sunIntensity
            );

            // Clouds grey the sky and dim the light.
            var cover = _weather.CloudCover;
            var dim = 1f - _weather.LightDim;
            var grey = Color.Lerp(horizon, zenith, 0.4f).grayscale;
            var overcast =
                new Color(grey, grey, grey * 1.04f, 1f) * (1f - 0.55f * _weather.CloudDarkness);
            zenith = Color.Lerp(zenith, overcast, cover * 0.85f) * dim;
            horizon = Color.Lerp(horizon, overcast, cover * 0.7f) * dim;

            var night = math.saturate(
                (MoonSwitchDegrees - elevation) / (MoonSwitchDegrees - NightFullDegrees)
            );
            ApplyLight(sun, moon, elevation, sunColor, sunIntensity * dim, night);

            // Wind drifts the clouds; time runs faster while skipping, so do they.
            var windMeters = _weather.WindMetersPerSecond * deltaDays * 86400f;
            _cloudOffset += new Vector2(windMeters * 0.8f, windMeters * 0.6f);
            if (math.abs(_cloudOffset.x) > 1e5f)
            {
                _cloudOffset = Vector2.zero;
            }

            Shader.SetGlobalVector(
                AtmosphereShaderGlobals.SunDirectionId,
                new Vector4(sun.x, sun.y, sun.z, 1f - night)
            );
            Shader.SetGlobalVector(
                AtmosphereShaderGlobals.MoonDirectionId,
                new Vector4(moon.x, moon.y, moon.z, night)
            );
            Shader.SetGlobalVector(
                AtmosphereShaderGlobals.SkyZenithId,
                AtmosphereShaderGlobals.Linear(zenith)
            );
            Shader.SetGlobalVector(
                AtmosphereShaderGlobals.SkyHorizonId,
                AtmosphereShaderGlobals.Linear(horizon)
            );
            var sunLinear = AtmosphereShaderGlobals.Linear(sunColor) * (sunIntensity * dim);
            sunLinear.w = (1f - cover * 0.9f) * (1f - night);
            Shader.SetGlobalVector(AtmosphereShaderGlobals.SunColorId, sunLinear);
            Shader.SetGlobalVector(
                AtmosphereShaderGlobals.CloudsId,
                new Vector4(
                    cover,
                    _weather.CloudDarkness,
                    _settings.CloudHeightMeters,
                    1f / _settings.CloudScaleMeters
                )
            );
            Shader.SetGlobalVector(
                AtmosphereShaderGlobals.CloudOffsetId,
                new Vector4(
                    _cloudOffset.x,
                    _cloudOffset.y,
                    _settings.CloudShadowStrength,
                    _settings.StarBrightness * night * (1f - cover)
                )
            );
            var starAngle = (float)(AtmosphereClock.DayFraction(_days) * 360.0);
            var poleTilt = Quaternion.AngleAxis(90f - _settings.LatitudeDegrees, Vector3.right);
            var stars = Matrix4x4.Rotate(
                Quaternion.Inverse(poleTilt * Quaternion.AngleAxis(starAngle, Vector3.up))
            );
            Shader.SetGlobalMatrix(AtmosphereShaderGlobals.StarRotationId, stars);
            Shader.SetGlobalVector(
                AtmosphereShaderGlobals.SeasonId,
                AtmosphereClock.SeasonWeights(_days, _settings.DaysPerSeason)
            );

            ApplyFogAndAmbient(zenith, horizon, night, dim);
            UpdateTemperatureAndPrecipitation(deltaDays);
            var snowOffset =
                AtmosphereClock.SeasonOffsetC(
                    _days,
                    _settings.DaysPerSeason,
                    _settings.SummerOffsetC,
                    _settings.WinterOffsetC
                ) + _weather.TemperatureOffsetC;
            Shader.SetGlobalVector(
                AtmosphereShaderGlobals.WeatherId,
                new Vector4(_wetness, snowOffset, _settings.SnowStartC, _settings.SnowFullC)
            );
        }

        private void ApplyLight(
            float3 sun,
            float3 moon,
            float elevation,
            Color sunColor,
            float sunIntensity,
            float night
        )
        {
            if (_sun == null)
            {
                return;
            }

            float3 direction;
            float intensity;
            Color color;
            float shadow;
            if (elevation > MoonSwitchDegrees)
            {
                direction = sun;
                intensity = sunIntensity;
                color = sunColor;
                shadow = _weather.ShadowStrength;
            }
            else
            {
                direction = moon;
                intensity =
                    _settings.MoonIntensity
                    * night
                    * math.saturate(moon.y * 3f)
                    * (1f - _weather.LightDim);
                color = _settings.MoonColor;
                shadow = _settings.MoonShadowStrength * _weather.ShadowStrength;
            }

            _sun.transform.rotation = Quaternion.LookRotation(-(Vector3)direction, Vector3.up);
            _sun.color = color;
            _sun.intensity = intensity;
            _sun.shadowStrength = shadow;
        }

        private void ApplyFogAndAmbient(Color zenith, Color horizon, float night, float dim)
        {
            RenderSettings.fogColor = horizon;
            RenderSettings.fogDensity = _weather.FogDensity;

            var ambient = new SphericalHarmonicsL2();
            var scale = _settings.AmbientScale;
            var nightAmbient = _settings.NightAmbient.linear * (night * dim);
            var sky = zenith.linear * scale + nightAmbient;
            var equator = horizon.linear * scale + nightAmbient;
            var ground = equator * 0.35f;
            ambient.AddAmbientLight(equator);
            ambient.AddDirectionalLight(Vector3.up, sky - equator, 1f);
            ambient.AddDirectionalLight(Vector3.down, ground - equator, 1f);
            RenderSettings.ambientProbe = ambient;
        }

        private void UpdateTemperatureAndPrecipitation(float deltaDays)
        {
            var main = Camera.main;
            var position = main != null ? main.transform.position : Vector3.zero;
            if (_world != null && Time.unscaledTime >= _nextClimateQuery && _world.IsWaterPlanReady)
            {
                _nextClimateQuery = Time.unscaledTime + 1f;
                if (_world.TryGetClimateTemperature(position, out var climate))
                {
                    _climateTemperature = climate;
                }
            }

            _airTemperature = _climateTemperature + Offsets();
            _snowing = _airTemperature < 0f && _weather.Precipitation > 0f;

            // No rain or snow while something blocks the sky above the camera.
            var covered = false;
            if (_world != null)
            {
                var voxel = Coords.WorldPositionToVoxel(position);
                covered =
                    _world.TryGetSkyHeights(voxel.x, voxel.z, out _, out var full)
                    && voxel.y < full;
            }

            var amount = covered ? 0f : _weather.Precipitation;
            _rain?.Update(position, _snowing ? 0f : amount, _weather.WindMetersPerSecond);
            _snow?.Update(position, _snowing ? amount : 0f, _weather.WindMetersPerSecond);

            var hours = deltaDays * 24f;
            if (!_snowing && _weather.Precipitation > 0.1f)
            {
                _wetness = math.min(1f, _wetness + hours / _settings.WetnessSoakHours);
            }
            else
            {
                _wetness = math.max(0f, _wetness - hours / _settings.WetnessDryHours);
            }
        }

        // Sky colors and sun light at a sun elevation, between the keys.
        private void EvaluateSky(
            float elevation,
            out Color zenith,
            out Color horizon,
            out Color sun,
            out float intensity
        )
        {
            var keys = _settings.SkyKeys;
            var i = 0;
            while (i < keys.Length - 2 && elevation > keys[i + 1].ElevationDegrees)
            {
                i++;
            }

            var a = keys[i];
            var b = keys[i + 1];
            var t = math.saturate(
                (elevation - a.ElevationDegrees) / (b.ElevationDegrees - a.ElevationDegrees)
            );
            zenith = Color.Lerp(a.Zenith, b.Zenith, t);
            horizon = Color.Lerp(a.Horizon, b.Horizon, t);
            sun = Color.Lerp(a.Sun, b.Sun, t);
            intensity = math.lerp(a.SunIntensity, b.SunIntensity, t);
        }

#if UNITY_EDITOR || REVOXEL_DEBUG
        // T: time runs 120 times faster while held. Y: next weather. U: next season (D-102).
        private void DebugKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                _timeScale = 1f;
                return;
            }

            _timeScale = keyboard.tKey.isPressed ? DebugTimeScale : 1f;
            if (keyboard.yKey.wasPressedThisFrame)
            {
                var current = _hasForcedWeather ? _forcedWeather : _weather.Kind;
                _forcedWeather = (WeatherKind)(((int)current + 1) % _settings.Weathers.Length);
                _forcedPeriod = WeatherSchedule.Period(_days, _settings.WeatherPeriodHours);
                _hasForcedWeather = true;
            }

            if (keyboard.uKey.wasPressedThisFrame)
            {
                var wasForced = _hasForcedWeather;
                var forced = _forcedWeather;
                SetDays(_days + _settings.DaysPerSeason);
                if (wasForced)
                {
                    _forcedWeather = forced;
                    _forcedPeriod = WeatherSchedule.Period(_days, _settings.WeatherPeriodHours);
                    _hasForcedWeather = true;
                }
            }
        }
#endif
    }
}
