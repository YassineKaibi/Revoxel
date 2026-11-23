using UnityEngine;

namespace Revoxel.World
{
    // Global look of the voxel shader (PLAN-02 Task 7, D-051). Placeholder values (Q-12).
    [CreateAssetMenu(
        menuName = "Revoxel/World/World Render Settings",
        fileName = "Placeholder_WorldRenderSettings"
    )]
    public sealed class WorldRenderSettings : ScriptableObject
    {
        [SerializeField]
        [Tooltip(
            "sRGB tint of fully corrupted ground, applied to its luminance. Placeholder value (Q-12)."
        )]
        private Color _corruptionTint = new Color(0x8A / 255f, 0x87 / 255f, 0x78 / 255f, 1f);

        [SerializeField]
        [Tooltip("Brightness of a fully occluded inside corner, 0 to 1. Placeholder value (Q-12).")]
        [Range(0f, 1f)]
        private float _aoMin = 0.55f;

        [SerializeField]
        [Tooltip(
            "How far the faces of one-voxel steps are lit as if facing up, 0 (as built) to 1 (like the ground), D-066. Placeholder value (Q-12)."
        )]
        [Range(0f, 1f)]
        private float _stepNormalBend = 0.85f;

        public Color CorruptionTint => _corruptionTint;

        public float AoMin => _aoMin;

        public float StepNormalBend => _stepNormalBend;

        public bool TryValidate(out string error)
        {
            if (_aoMin < 0f || _aoMin > 1f)
            {
                error = "AO floor must be 0 to 1.";
                return false;
            }

            if (_stepNormalBend < 0f || _stepNormalBend > 1f)
            {
                error = "Step normal bend must be 0 to 1.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
