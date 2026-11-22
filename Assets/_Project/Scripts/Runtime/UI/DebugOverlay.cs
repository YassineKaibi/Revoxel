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
    // memory; PLAN-03 Task 5). IMGUI on purpose, so the UI framework choice stays open.
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
        private VoxelWorld _world;

        [SerializeField]
        private DistanceDetail _detail;

        private string _frameTimeLabel = string.Empty;
        private string _loadedChunksLabel = string.Empty;
        private string _chunkViewsLabel = string.Empty;
        private string _pendingJobsLabel = string.Empty;
        private string _voxelMemoryLabel = string.Empty;
        private string _detailChunksLabel = string.Empty;
        private string _detailMemoryLabel = string.Empty;
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
                + $"{_detailMemoryLabel}: {detail.MeshBytes / BytesPerMiB:F1} MiB";
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
