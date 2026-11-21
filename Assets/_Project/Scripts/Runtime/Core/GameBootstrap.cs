using System.Collections;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace Revoxel.Core
{
    // Lives in Bootstrap.unity. Initializes Localization, then loads Island.unity.
    public sealed class GameBootstrap : MonoBehaviour
    {
        public const string IslandSceneName = "Island";

        private IEnumerator Start()
        {
            var localization = LocalizationSettings.InitializationOperation;
            yield return localization;
            if (localization.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError("[Core] Localization failed to initialize.");
            }

            yield return SceneManager.LoadSceneAsync(IslandSceneName, LoadSceneMode.Single);
        }
    }
}
