using Revoxel.World;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Revoxel.UI
{
    // Debug-only overlay in Island.unity: one localized line and a button that
    // switches the selected locale between the two available ones (fr, en), plus the
    // world counters (frame time, chunk views, remesh queue; D-045, D-046).
    // IMGUI on purpose, so the UI framework choice stays open for WP-04.
    public sealed class DebugOverlay : MonoBehaviour
    {
        public const string TableName = "UI";
        public const string HelloKey = "ui.debug.hello";
        public const string SwitchLanguageKey = "ui.debug.switch_language";
        public const string FrameTimeKey = "ui.debug.frame_time";
        public const string ChunksKey = "ui.debug.chunks";
        public const string DirtyQueueKey = "ui.debug.dirty_queue";

        // The counters text is rebuilt at this interval, not every frame.
        private const float CountersRefreshSeconds = 0.25f;
        private const float FrameTimeSmoothing = 0.1f;

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
        private LocalizedString _chunksText = new LocalizedString(TableName, ChunksKey);

        [SerializeField]
        private LocalizedString _dirtyQueueText = new LocalizedString(TableName, DirtyQueueKey);

        [SerializeField]
        private VoxelWorld _world;

        private string _frameTimeLabel = string.Empty;
        private string _chunksLabel = string.Empty;
        private string _dirtyQueueLabel = string.Empty;
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
            _chunksText.StringChanged += OnChunksChanged;
            _dirtyQueueText.StringChanged += OnDirtyQueueChanged;
#endif
        }

        private void OnDisable()
        {
            _helloText.StringChanged -= OnHelloChanged;
            _switchLanguageText.StringChanged -= OnSwitchLanguageChanged;
#if UNITY_EDITOR || REVOXEL_DEBUG
            _frameTimeText.StringChanged -= OnFrameTimeChanged;
            _chunksText.StringChanged -= OnChunksChanged;
            _dirtyQueueText.StringChanged -= OnDirtyQueueChanged;
#endif
        }

        private void OnHelloChanged(string value) => _hello = value;

        private void OnSwitchLanguageChanged(string value) => _switchLanguage = value;

#if UNITY_EDITOR || REVOXEL_DEBUG
        private void OnFrameTimeChanged(string value) => _frameTimeLabel = value;

        private void OnChunksChanged(string value) => _chunksLabel = value;

        private void OnDirtyQueueChanged(string value) => _dirtyQueueLabel = value;

        private void Update()
        {
            var frameTime = Time.unscaledDeltaTime * 1000f;
            _smoothedFrameTime = Mathf.Lerp(_smoothedFrameTime, frameTime, FrameTimeSmoothing);
            if (_world == null || Time.unscaledTime < _nextCountersRefresh)
            {
                return;
            }

            _nextCountersRefresh = Time.unscaledTime + CountersRefreshSeconds;
            _counters =
                $"{_frameTimeLabel}: {_smoothedFrameTime:F1} ms\n"
                + $"{_chunksLabel}: {_world.ViewCount}\n"
                + $"{_dirtyQueueLabel}: {_world.DirtyCount}";
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
