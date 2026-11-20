using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.Core;
using Revoxel.UI;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Revoxel.Tests.PlayMode
{
    public sealed class BootstrapTests
    {
        private const string BootstrapSceneName = "Bootstrap";
        private const float TimeoutSeconds = 30f;

        private readonly List<string> _unexpectedLogs = new List<string>();
        private readonly List<string> _loadedScenes = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _unexpectedLogs.Clear();
            _loadedScenes.Clear();
            Application.logMessageReceived += OnLogMessage;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLogMessage;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [UnityTest]
        public IEnumerator Bootstrap_LoadsIsland_WithoutErrors()
        {
            yield return LoadBootstrapAndWaitForIsland();
            yield return null;

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(GameBootstrap.IslandSceneName)
            );
            Assert.That(_unexpectedLogs, Is.Empty, string.Join("\n", _unexpectedLogs));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Localization_SwitchLocale_ChangesDebugText()
        {
            yield return LoadBootstrapAndWaitForIsland();
            var overlay = Object.FindAnyObjectByType<DebugOverlay>();
            Assert.That(overlay, Is.Not.Null, "DebugOverlay not found in Island");
            yield return WaitFor(() => overlay.CurrentText.Length > 0, "the first overlay text");

            var firstLocale = LocalizationSettings.SelectedLocale;
            var firstText = overlay.CurrentText;

            overlay.ToggleLocale();
            var otherLocale = LocalizationSettings.SelectedLocale;
            Assert.That(otherLocale, Is.Not.EqualTo(firstLocale), "Locale did not change");
            var expected = LocalizationSettings.StringDatabase.GetLocalizedString(
                DebugOverlay.TableName,
                DebugOverlay.HelloKey,
                otherLocale
            );
            Assert.That(expected, Is.Not.EqualTo(firstText), "Both tables hold the same text");
            yield return WaitFor(() => overlay.CurrentText != firstText, "the switched text");
            Assert.That(overlay.CurrentText, Is.EqualTo(expected));

            overlay.ToggleLocale();
            Assert.That(LocalizationSettings.SelectedLocale, Is.EqualTo(firstLocale));
            yield return WaitFor(() => overlay.CurrentText == firstText, "the original text");
        }

        private IEnumerator LoadBootstrapAndWaitForIsland()
        {
            SceneManager.LoadScene(BootstrapSceneName, LoadSceneMode.Single);
            yield return WaitFor(
                () =>
                    _loadedScenes.Contains(BootstrapSceneName)
                    && _loadedScenes.Contains(GameBootstrap.IslandSceneName),
                "Bootstrap then Island to load"
            );
        }

        private static IEnumerator WaitFor(Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail($"Timed out after {TimeoutSeconds} s waiting for {what}");
                }

                yield return null;
            }
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Log)
            {
                _unexpectedLogs.Add($"{type}: {condition}");
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Island only counts once Bootstrap has loaded in this test.
            if (scene.name == BootstrapSceneName || _loadedScenes.Contains(BootstrapSceneName))
            {
                _loadedScenes.Add(scene.name);
            }
        }
    }
}
