using Revoxel.Atmosphere;
using Revoxel.World;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Revoxel.UI
{
    // Debug-only overlay in Island.unity: one localized line and a button that
    // switches the selected locale between the two available ones (fr, en), plus the
    // world counters (frame time, loaded chunks, chunk views, pending jobs, voxel memory;
    // D-045, D-046, D-054) and the distance detail counters (distant chunks, distant mesh
    // memory; PLAN-03 Task 5), the biome under the camera (D-065) and the water the camera
    // is in, with its depth (D-074), and the time, season, weather and air temperature
    // (D-102). IMGUI on purpose, so the UI framework choice stays open.
    public sealed class DebugOverlay : MonoBehaviour
    {
        public const string TableName = "UI";
        public const string HelloKey = "ui.debug.hello";
        public const string SwitchLanguageKey = "ui.debug.switch_language";
        public const string FrameTimeKey = "ui.debug.frame_time";
        public const string LoadedChunksKey = "ui.debug.loaded_chunks";
        public const string ChunkViewsKey = "ui.debug.chunk_views";
        public const string PendingJobsKey = "ui.debug.pending_jobs";
        public const string VoxelMemoryKey = "ui.debug.voxel_memory";
        public const string DetailChunksKey = "ui.debug.detail_chunks";
        public const string DetailMemoryKey = "ui.debug.detail_memory";
        public const string BiomeKey = "ui.debug.biome";
        public const string WaterKey = "ui.debug.water";
        public const string TimeKey = "ui.debug.time";
        public const string SeasonKey = "ui.debug.season";
        public const string WeatherKey = "ui.debug.weather";
        public const string TemperatureKey = "ui.debug.temperature";

        // Indexed by AtmosphereClock season.
        public static readonly string[] SeasonKeys =
        {
            "season.spring.name",
            "season.summer.name",
            "season.autumn.name",
            "season.winter.name",
        };

        // Indexed by WeatherKind, then snow.
        public static readonly string[] WeatherKeys =
        {
            "weather.clear.name",
            "weather.cloudy.name",
            "weather.overcast.name",
            "weather.rain.name",
            "weather.storm.name",
            "weather.fog.name",
            "weather.snow.name",
        };

        // Names of the water bodies, indexed by WaterBody.
        public static readonly string[] WaterBodyKeys =
        {
            "water.sea.name",
            "water.lake.name",
            "water.river.name",
        };

        // The counters text is rebuilt at this interval, not every frame.
        private const float CountersRefreshSeconds = 0.25f;
        private const float FrameTimeSmoothing = 0.1f;
        private const float BytesPerMiB = 1024f * 1024f;

        [SerializeField]
        private LocalizedString _helloText = new LocalizedString(TableName, HelloKey);

        [SerializeField]
        private LocalizedString _switchLanguageText = new LocalizedString(
            TableName,
            SwitchLanguageKey
        );

#if UNITY_EDITOR || REVOXEL_DEBUG
        [SerializeField]
        private LocalizedString _frameTimeText = new LocalizedString(TableName, FrameTimeKey);

        [SerializeField]
        private LocalizedString _loadedChunksText = new LocalizedString(TableName, LoadedChunksKey);

        [SerializeField]
        private LocalizedString _chunkViewsText = new LocalizedString(TableName, ChunkViewsKey);

        [SerializeField]
        private LocalizedString _pendingJobsText = new LocalizedString(TableName, PendingJobsKey);

        [SerializeField]
        private LocalizedString _voxelMemoryText = new LocalizedString(TableName, VoxelMemoryKey);

        [SerializeField]
        private LocalizedString _detailChunksText = new LocalizedString(TableName, DetailChunksKey);

        [SerializeField]
        private LocalizedString _detailMemoryText = new LocalizedString(TableName, DetailMemoryKey);

        [SerializeField]
        private LocalizedString _biomeText = new LocalizedString(TableName, BiomeKey);

        [SerializeField]
        private LocalizedString _waterText = new LocalizedString(TableName, WaterKey);

        [SerializeField]
        private LocalizedString _timeText = new LocalizedString(TableName, TimeKey);

        [SerializeField]
        private LocalizedString _seasonText = new LocalizedString(TableName, SeasonKey);

        [SerializeField]
        private LocalizedString _weatherText = new LocalizedString(TableName, WeatherKey);

        [SerializeField]
        private LocalizedString _temperatureText = new LocalizedString(TableName, TemperatureKey);

        [SerializeField]
        private VoxelWorld _world;

        [SerializeField]
        [Tooltip("Found in the scene when empty.")]
        private AtmosphereController _atmosphere;

        [SerializeField]
        private DistanceDetail _detail;

        private string _frameTimeLabel = string.Empty;
        private string _loadedChunksLabel = string.Empty;
        private string _chunkViewsLabel = string.Empty;
        private string _pendingJobsLabel = string.Empty;
        private string _voxelMemoryLabel = string.Empty;
        private string _detailChunksLabel = string.Empty;
        private string _detailMemoryLabel = string.Empty;
        private string _biomeLabel = string.Empty;
        private string _biomeName = string.Empty;
        private int _biomeNameId = -1;
        private string _waterLabel = string.Empty;
        private readonly string[] _waterBodyNames = new string[3];
        private string _timeLabel = string.Empty;
        private string _seasonLabel = string.Empty;
        private string _weatherLabel = string.Empty;
        private string _temperatureLabel = string.Empty;
        private readonly string[] _seasonNames = new string[4];
        private readonly string[] _weatherNames = new string[7];
        private float _smoothedFrameTime;
        private float _nextCountersRefresh;
#endif

        private string _hello = string.Empty;
        private string _switchLanguage = string.Empty;
        private string _counters = string.Empty;

        public string CurrentText => _hello;

        public void ToggleLocale()
        {
            var current = LocalizationSettings.SelectedLocale;
            var locales = LocalizationSettings.AvailableLocales.Locales;
            for (var i = 0; i < locales.Count; i++)
            {
                if (locales[i] != current)
                {
                    LocalizationSettings.SelectedLocale = locales[i];
                    return;
                }
            }
        }

        private void OnEnable()
        {
            _helloText.StringChanged += OnHelloChanged;
            _switchLanguageText.StringChanged += OnSwitchLanguageChanged;
#if UNITY_EDITOR || REVOXEL_DEBUG
            _frameTimeText.StringChanged += OnFrameTimeChanged;
            _loadedChunksText.StringChanged += OnLoadedChunksChanged;
            _chunkViewsText.StringChanged += OnChunkViewsChanged;
            _pendingJobsText.StringChanged += OnPendingJobsChanged;
            _voxelMemoryText.StringChanged += OnVoxelMemoryChanged;
            _detailChunksText.StringChanged += OnDetailChunksChanged;
            _detailMemoryText.StringChanged += OnDetailMemoryChanged;
            _biomeText.StringChanged += OnBiomeChanged;
            _waterText.StringChanged += OnWaterChanged;
            _timeText.StringChanged += OnTimeChanged;
            _seasonText.StringChanged += OnSeasonChanged;
            _weatherText.StringChanged += OnWeatherChanged;
            _temperatureText.StringChanged += OnTemperatureChanged;
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
            if (_atmosphere == null)
            {
                _atmosphere = FindAnyObjectByType<AtmosphereController>();
            }
#endif
        }

        private void OnDisable()
        {
            _helloText.StringChanged -= OnHelloChanged;
            _switchLanguageText.StringChanged -= OnSwitchLanguageChanged;
#if UNITY_EDITOR || REVOXEL_DEBUG
            _frameTimeText.StringChanged -= OnFrameTimeChanged;
            _loadedChunksText.StringChanged -= OnLoadedChunksChanged;
            _chunkViewsText.StringChanged -= OnChunkViewsChanged;
            _pendingJobsText.StringChanged -= OnPendingJobsChanged;
            _voxelMemoryText.StringChanged -= OnVoxelMemoryChanged;
            _detailChunksText.StringChanged -= OnDetailChunksChanged;
            _detailMemoryText.StringChanged -= OnDetailMemoryChanged;
            _biomeText.StringChanged -= OnBiomeChanged;
            _waterText.StringChanged -= OnWaterChanged;
            _timeText.StringChanged -= OnTimeChanged;
            _seasonText.StringChanged -= OnSeasonChanged;
            _weatherText.StringChanged -= OnWeatherChanged;
            _temperatureText.StringChanged -= OnTemperatureChanged;
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
#endif
        }

        private void OnHelloChanged(string value) => _hello = value;

        private void OnSwitchLanguageChanged(string value) => _switchLanguage = value;

#if UNITY_EDITOR || REVOXEL_DEBUG
        private void OnFrameTimeChanged(string value) => _frameTimeLabel = value;

        private void OnLoadedChunksChanged(string value) => _loadedChunksLabel = value;

        private void OnChunkViewsChanged(string value) => _chunkViewsLabel = value;

        private void OnPendingJobsChanged(string value) => _pendingJobsLabel = value;

        private void OnVoxelMemoryChanged(string value) => _voxelMemoryLabel = value;

        private void OnDetailChunksChanged(string value) => _detailChunksLabel = value;

        private void OnDetailMemoryChanged(string value) => _detailMemoryLabel = value;

        private void OnBiomeChanged(string value) => _biomeLabel = value;

        private void OnWaterChanged(string value) => _waterLabel = value;

        private void OnTimeChanged(string value) => _timeLabel = value;

        private void OnSeasonChanged(string value) => _seasonLabel = value;

        private void OnWeatherChanged(string value) => _weatherLabel = value;

        private void OnTemperatureChanged(string value) => _temperatureLabel = value;

        private static string Lookup(string[] cache, string[] keys, int index)
        {
            return cache[index] ??= LocalizationSettings.StringDatabase.GetLocalizedString(
                TableName,
                keys[index]
            );
        }

        // Day and time, season, weather and air temperature at the camera (D-102).
        private string AtmosphereText()
        {
            if (_atmosphere == null || !_atmosphere.isActiveAndEnabled)
            {
                return string.Empty;
            }

            var hour = _atmosphere.HourOfDay;
            var h = (int)hour;
            var m = (int)((hour - h) * 60f);
            var weather = _atmosphere.Weather;
            var weatherIndex = _atmosphere.IsSnowing ? WeatherKeys.Length - 1 : (int)weather.Kind;
            return $"\n{_timeLabel}: {_atmosphere.DayIndex + 1}, {h:00}:{m:00}\n"
                + $"{_seasonLabel}: {Lookup(_seasonNames, SeasonKeys, _atmosphere.Season)}\n"
                + $"{_weatherLabel}: {Lookup(_weatherNames, WeatherKeys, weatherIndex)}\n"
                + $"{_temperatureLabel}: {_atmosphere.CameraAirTemperatureC:F1} °C";
        }

        // Biome and water names are looked up again in the new language.
        private void OnLocaleChanged(Locale locale)
        {
            _biomeNameId = -1;
            for (var i = 0; i < _waterBodyNames.Length; i++)
            {
                _waterBodyNames[i] = null;
            }

            System.Array.Clear(_seasonNames, 0, _seasonNames.Length);
            System.Array.Clear(_weatherNames, 0, _weatherNames.Length);
        }

        // The water the camera is in and its depth, or nothing when dry.
        private string WaterText()
        {
            var camera = Camera.main;
            if (
                camera == null
                || !_world.IsWaterPlanReady
                || !_world.TrySampleWater(camera.transform.position, out var water)
                || !water.InWater
            )
            {
                return string.Empty;
            }

            var body = (int)water.Body;
            _waterBodyNames[body] ??= LocalizationSettings.StringDatabase.GetLocalizedString(
                TableName,
                WaterBodyKeys[body]
            );
            return $"{_waterBodyNames[body]}, {water.DepthMeters:F1} m";
        }

        // The localized name of the biome under the camera, looked up only when it changes.
        private string BiomeName()
        {
            var camera = Camera.main;
            if (
                camera == null
                || !_world.IsWaterPlanReady
                || !_world.TrySampleBiome(camera.transform.position, out var sample)
                || !_world.TryGetBiome(sample.Biome, out var biome)
            )
            {
                return string.Empty;
            }

            if (_biomeNameId != sample.Biome.Value)
            {
                _biomeNameId = sample.Biome.Value;
                _biomeName = LocalizationSettings.StringDatabase.GetLocalizedString(
                    TableName,
                    biome.NameKey
                );
            }

            return _biomeName;
        }

        private void Update()
        {
            var frameTime = Time.unscaledDeltaTime * 1000f;
            _smoothedFrameTime = Mathf.Lerp(_smoothedFrameTime, frameTime, FrameTimeSmoothing);
            if (_world == null || Time.unscaledTime < _nextCountersRefresh)
            {
                return;
            }

            _nextCountersRefresh = Time.unscaledTime + CountersRefreshSeconds;
            var stats = _world.Stats;
            var detail = _detail != null ? _detail.Stats : default;
            var pending = stats.PendingGeneration + stats.PendingMeshing + detail.PendingJobs;
            _counters =
                $"{_frameTimeLabel}: {_smoothedFrameTime:F1} ms\n"
                + $"{_loadedChunksLabel}: {stats.LoadedChunks}\n"
                + $"{_chunkViewsLabel}: {stats.ViewCount}\n"
                + $"{_pendingJobsLabel}: {pending}\n"
                + $"{_voxelMemoryLabel}: {stats.VoxelMemoryBytes / BytesPerMiB:F1} MiB\n"
                + $"{_detailChunksLabel}: {detail.Views}\n"
                + $"{_detailMemoryLabel}: {detail.MeshBytes / BytesPerMiB:F1} MiB\n"
                + $"{_biomeLabel}: {BiomeName()}\n"
                + $"{_waterLabel}: {WaterText()}"
                + AtmosphereText();
        }
#endif

        private void OnGUI()
        {
            GUILayout.Label(_hello);
            if (_counters.Length > 0)
            {
                GUILayout.Label(_counters);
            }

            if (GUILayout.Button(_switchLanguage))
            {
                ToggleLocale();
            }
        }
    }
}
