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
        private const string Placeholder = " Placeholder value (Q-12).";

        [SerializeField]
        [Tooltip(
            "sRGB tint of fully corrupted ground, applied to its luminance. Placeholder value (Q-12)."
        )]
        private Color _corruptionTint = new Color(0x8A / 255f, 0x87 / 255f, 0x78 / 255f, 1f);

        [SerializeField]
        [Tooltip("Brightness of a fully occluded inside corner, 0 to 1. Placeholder value (Q-12).")]
        [Range(0f, 1f)]
        private float _aoMin = 0.55f;

        [Header("Water (D-073)")]
        [SerializeField]
        [Tooltip("sRGB color light scatters to inside clean water." + Placeholder)]
        private Color _waterScatter = new Color(0.04f, 0.22f, 0.26f, 1f);

        [SerializeField]
        [Tooltip("Share of red, green and blue light clean water absorbs per meter." + Placeholder)]
        private Vector3 _waterAbsorptionPerMeter = new Vector3(0.45f, 0.09f, 0.07f);

        [SerializeField]
        [Tooltip("sRGB scatter color of fully polluted water." + Placeholder)]
        private Color _pollutedWaterScatter = new Color(0.2f, 0.22f, 0.13f, 1f);

        [SerializeField]
        [Tooltip("Absorption per meter of fully polluted water." + Placeholder)]
        private Vector3 _pollutedWaterAbsorptionPerMeter = new Vector3(0.9f, 0.75f, 0.7f);

        [SerializeField]
        [Tooltip("How much of the corruption grade water takes, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _waterPollutionShare = 0.6f;

        [SerializeField]
        [Tooltip(
            "Screen offset of what is seen through the surface per unit of wave slope."
                + Placeholder
        )]
        private float _waterRefraction = 0.04f;

        [SerializeField]
        [Tooltip("Size of the surface waves, m." + Placeholder)]
        private float _waterWaveScaleMeters = 2.5f;

        [SerializeField]
        [Tooltip("Slope of the surface waves." + Placeholder)]
        private float _waterWaveStrength = 0.12f;

        [SerializeField]
        [Tooltip("Drift speed of the surface waves, m/s." + Placeholder)]
        private float _waterWaveSpeed = 0.35f;

        [SerializeField]
        [Tooltip("Speed of the streaks running down a waterfall, m/s." + Placeholder)]
        private float _waterFallSpeed = 3f;

        [SerializeField]
        [Tooltip("Water shallower than this shows foam at the shore, m." + Placeholder)]
        private float _waterFoamDepthMeters = 0.3f;

        [SerializeField]
        [Tooltip("sRGB foam color." + Placeholder)]
        private Color _waterFoamColor = new Color(0.92f, 0.96f, 1f, 1f);

        [SerializeField]
        [Tooltip("Smoothness of the surface, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _waterSmoothness = 0.93f;

        [SerializeField]
        [Tooltip("sRGB fog color seen under water." + Placeholder)]
        private Color _underwaterColor = new Color(0.03f, 0.18f, 0.22f, 1f);

        [SerializeField]
        [Tooltip("Underwater fog density per meter." + Placeholder)]
        private float _underwaterDensityPerMeter = 0.15f;

        public Color CorruptionTint => _corruptionTint;

        public float AoMin => _aoMin;

        public Color WaterScatter => _waterScatter;

        public Vector3 WaterAbsorptionPerMeter => _waterAbsorptionPerMeter;

        public Color PollutedWaterScatter => _pollutedWaterScatter;

        public Vector3 PollutedWaterAbsorptionPerMeter => _pollutedWaterAbsorptionPerMeter;

        public float WaterPollutionShare => _waterPollutionShare;

        public float WaterRefraction => _waterRefraction;

        public float WaterWaveScaleMeters => _waterWaveScaleMeters;

        public float WaterWaveStrength => _waterWaveStrength;

        public float WaterWaveSpeed => _waterWaveSpeed;

        public float WaterFallSpeed => _waterFallSpeed;

        public float WaterFoamDepthMeters => _waterFoamDepthMeters;

        public Color WaterFoamColor => _waterFoamColor;

        public float WaterSmoothness => _waterSmoothness;

        public Color UnderwaterColor => _underwaterColor;

        public float UnderwaterDensityPerMeter => _underwaterDensityPerMeter;

        public bool TryValidate(out string error)
        {
            if (_aoMin < 0f || _aoMin > 1f)
            {
                error = "AO floor must be 0 to 1.";
                return false;
            }

            if (
                Negative(_waterAbsorptionPerMeter)
                || Negative(_pollutedWaterAbsorptionPerMeter)
                || _waterRefraction < 0f
                || _waterWaveScaleMeters <= 0f
                || _waterWaveStrength < 0f
                || _waterFoamDepthMeters <= 0f
                || _underwaterDensityPerMeter < 0f
                || _waterSmoothness < 0f
                || _waterSmoothness > 1f
                || _waterPollutionShare < 0f
                || _waterPollutionShare > 1f
            )
            {
                error =
                    "Water absorption, refraction, wave strength and fog density must not be negative; wave scale and foam depth positive; smoothness and pollution share 0 to 1.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool Negative(Vector3 v) => v.x < 0f || v.y < 0f || v.z < 0f;
    }
}
