using Revoxel.World;
using UnityEngine;
using UnityEngine.Localization;

namespace Revoxel.UI
{
    // Full-screen cover with a localized title and a progress bar until the world first
    // settles around the player.
    // IMGUI on purpose, like DebugOverlay: the UI framework is chosen in WP-04.
    public sealed class LoadingScreen : MonoBehaviour
    {
        public const string TitleKey = "ui.loading.title";

        [SerializeField]
        private VoxelWorld _world;

        [SerializeField]
        private LocalizedString _titleText = new LocalizedString(DebugOverlay.TableName, TitleKey);

        private string _title = string.Empty;
        private GUIStyle _titleStyle;
        private bool _settled;

        public bool IsShowing => _world != null && !_settled && !_world.IsLoaded;

        private void OnEnable()
        {
            _titleText.StringChanged += OnTitleChanged;
            if (_world != null)
            {
                _world.FirstSettled += OnFirstSettled;
            }
        }

        private void OnDisable()
        {
            _titleText.StringChanged -= OnTitleChanged;
            if (_world != null)
            {
                _world.FirstSettled -= OnFirstSettled;
            }
        }

        private void OnTitleChanged(string value) => _title = value;

        private void OnFirstSettled() => _settled = true;

        private void OnGUI()
        {
            if (!IsShowing)
            {
                return;
            }

            _titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Max(16, Screen.height / 20),
            };

            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.Box(screen, GUIContent.none);
            GUI.Box(screen, GUIContent.none);
            var center = screen.center;
            GUI.Label(
                new Rect(0f, center.y - Screen.height * 0.15f, Screen.width, Screen.height * 0.1f),
                _title,
                _titleStyle
            );

            var barWidth = Screen.width * 0.4f;
            var barHeight = Mathf.Max(8f, Screen.height * 0.02f);
            var bar = new Rect(center.x - barWidth * 0.5f, center.y, barWidth, barHeight);
            GUI.Box(bar, GUIContent.none);
            GUI.Box(
                new Rect(bar.x, bar.y, bar.width * _world.LoadProgress, bar.height),
                GUIContent.none
            );
        }
    }
}
