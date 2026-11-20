using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Revoxel.UI
{
    // Debug-only overlay in Island.unity: one localized line and a button that
    // switches the selected locale between the two available ones (fr, en).
    // IMGUI on purpose, so the UI framework choice stays open for WP-04.
    public sealed class DebugOverlay : MonoBehaviour
    {
        public const string TableName = "UI";
        public const string HelloKey = "ui.debug.hello";
        public const string SwitchLanguageKey = "ui.debug.switch_language";

        [SerializeField]
        private LocalizedString _helloText = new LocalizedString(TableName, HelloKey);

        [SerializeField]
        private LocalizedString _switchLanguageText = new LocalizedString(
            TableName,
            SwitchLanguageKey
        );

        private string _hello = string.Empty;
        private string _switchLanguage = string.Empty;

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
        }

        private void OnDisable()
        {
            _helloText.StringChanged -= OnHelloChanged;
            _switchLanguageText.StringChanged -= OnSwitchLanguageChanged;
        }

        private void OnHelloChanged(string value) => _hello = value;

        private void OnSwitchLanguageChanged(string value) => _switchLanguage = value;

        private void OnGUI()
        {
            GUILayout.Label(_hello);
            if (GUILayout.Button(_switchLanguage))
            {
                ToggleLocale();
            }
        }
    }
}
