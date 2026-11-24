using UnityEngine;

namespace Revoxel.World
{
    // Rivers and lakes (PLAN-05, D-077 to D-081): the hydrology grid, erosion, lakes in
    // basins and rivers along the flow, and how they shape the land. Read by
    // TerrainParams.Create, which plans them once per seed. Every value is a placeholder
    // (Q-12); lengths in meters.
    [CreateAssetMenu(
        menuName = "Revoxel/World/Water Settings",
        fileName = "Placeholder_WaterSettings"
    )]
    public sealed class WaterSettings : ScriptableObject
    {
        private const string Placeholder = " Placeholder value (Q-12).";

        [Header("Hydrology grid (D-077)")]
        [SerializeField]
        [Tooltip(
            "Rain on the driest ground, against 1 on the wettest (moisture weights it)."
                + Placeholder
        )]
        [Range(0f, 1f)]
        private float _precipitationDry = 0.3f;

        [SerializeField]
        [Tooltip("Erosion steps on the grid (D-079)." + Placeholder)]
        private int _erosionSteps = 12;

        [SerializeField]
        [Tooltip("Stream power strength per step, per square root of km² drained." + Placeholder)]
        private float _erosionStrength = 1f;

        [SerializeField]
        [Tooltip(
            "Only drainage above this cuts by stream power; hillslopes are left alone, km²."
                + Placeholder
        )]
        private float _erosionChannelAreaKm2 = 0.1f;

        [SerializeField]
        [Tooltip(
            "Spread of the cut per step, share of the 4-neighbor Laplacian, at most 0.25."
                + Placeholder
        )]
        [Range(0f, 0.25f)]
        private float _diffusion = 0.03f;

        [SerializeField]
        [Tooltip("Deepest erosion of any cell, m." + Placeholder)]
        private float _maxErosionMeters = 30f;

        [SerializeField]
        [Tooltip(
            "A depression this deep or more can hold a lake; shallower ones are filled flat, m."
                + Placeholder
        )]
        private float _lakeMinDepthMeters = 3f;

        [SerializeField]
        [Tooltip(
            "A depression this many grid cells or more can hold a lake; smaller ones are filled flat."
                + Placeholder
        )]
        private int _lakeMinCells = 24;

        [SerializeField]
        [Tooltip("A lake's surface sits this far below its spill point, m." + Placeholder)]
        private float _lakeMarginMeters = 0.25f;

        [Header("Rivers (D-080)")]
        [SerializeField]
        [Tooltip("A cell draining this much or more carries a river, km²." + Placeholder)]
        private float _channelAreaKm2 = 0.3f;

        [SerializeField]
        [Tooltip(
            "Half width of a river draining 1 km²; it grows with the square root of the area, m."
                + Placeholder
        )]
        private float _halfWidthAtStartMeters = 3f;

        [SerializeField]
        [Tooltip("Narrowest half width of any river (lake outlets included), m." + Placeholder)]
        private float _minHalfWidthMeters = 1.5f;

        [SerializeField]
        [Tooltip("Widest half width of any river, m." + Placeholder)]
        private float _maxHalfWidthMeters = 20f;

        [SerializeField]
        [Tooltip(
            "Depth of a river draining 1 km²; it grows with the area to the power 0.4, m."
                + Placeholder
        )]
        private float _depthAtStartMeters = 1f;

        [SerializeField]
        [Tooltip("Shallowest river, m." + Placeholder)]
        private float _minDepthMeters = 0.5f;

        [SerializeField]
        [Tooltip("Deepest river, m." + Placeholder)]
        private float _maxDepthMeters = 4f;

        [SerializeField]
        [Tooltip("Chaikin passes smoothing a river's path through the grid cells." + Placeholder)]
        [Range(0, 6)]
        private int _smoothingIterations = 2;

        [SerializeField]
        [Tooltip("Meander offset across the path, in half widths." + Placeholder)]
        private float _meanderAmplitude = 1.5f;

        [SerializeField]
        [Tooltip("Length of a meander along the river, m." + Placeholder)]
        private float _meanderScaleMeters = 120f;

        [SerializeField]
        [Tooltip("Length of a river segment, m." + Placeholder)]
        private float _segmentMeters = 8f;

        [SerializeField]
        [Tooltip(
            "The water sits this far below the lowest land at and beside the channel, m."
                + Placeholder
        )]
        private float _riverInsetMeters = 0.5f;

        [SerializeField]
        [Tooltip("A drop of at least this within a segment becomes a waterfall, m." + Placeholder)]
        private float _fallMinMeters = 2f;

        [SerializeField]
        [Tooltip("Height of a fall per meter of its face." + Placeholder)]
        private float _fallSteepness = 4f;

        [Header("Banks")]
        [SerializeField]
        [Tooltip("Width of the bank from the water's edge to untouched ground, m." + Placeholder)]
        private float _bankWidthMeters = 4f;

        [SerializeField]
        [Tooltip("Banks stand this much above the water they hold, m." + Placeholder)]
        private float _bankLipMeters = 0.125f;

        [SerializeField]
        [Tooltip("The first meters of a river's cut form a gentle shore, m." + Placeholder)]
        private float _shoreCutMeters = 4f;

        [SerializeField]
        [Tooltip("Horizontal run per meter of a river's shore." + Placeholder)]
        private float _shoreRun = 8f;

        [SerializeField]
        [Tooltip("Widest a canyon wall or levee gets, m." + Placeholder)]
        private float _maxWallWidthMeters = 100f;

        [Header("Lakes")]
        [SerializeField]
        [Tooltip("Lake levels are rounded down to a multiple of this, m." + Placeholder)]
        private float _lakeLevelStepMeters = 1f;

        [Header("Beds")]
        [SerializeField]
        [Tooltip("Ground under river water.")]
        private VoxelType _riverBed;

        [SerializeField]
        [Tooltip("Ground under lake water.")]
        private VoxelType _lakeBed;

        public float PrecipitationDry => _precipitationDry;

        public int ErosionSteps => _erosionSteps;

        public float ErosionStrength => _erosionStrength;

        public float ErosionChannelAreaKm2 => _erosionChannelAreaKm2;

        public float Diffusion => _diffusion;

        public float MaxErosionMeters => _maxErosionMeters;

        public float LakeMinDepthMeters => _lakeMinDepthMeters;

        public int LakeMinCells => _lakeMinCells;

        public float LakeMarginMeters => _lakeMarginMeters;

        public float ChannelAreaKm2 => _channelAreaKm2;

        public float HalfWidthAtStartMeters => _halfWidthAtStartMeters;

        public float MinHalfWidthMeters => _minHalfWidthMeters;

        public float MaxHalfWidthMeters => _maxHalfWidthMeters;

        public float DepthAtStartMeters => _depthAtStartMeters;

        public float MinDepthMeters => _minDepthMeters;

        public float MaxDepthMeters => _maxDepthMeters;

        public int SmoothingIterations => _smoothingIterations;

        public float MeanderAmplitude => _meanderAmplitude;

        public float MeanderScaleMeters => _meanderScaleMeters;

        public float SegmentMeters => _segmentMeters;

        public float RiverInsetMeters => _riverInsetMeters;

        public float FallMinMeters => _fallMinMeters;

        public float FallSteepness => _fallSteepness;

        public float BankWidthMeters => _bankWidthMeters;

        public float BankLipMeters => _bankLipMeters;

        public float ShoreCutMeters => _shoreCutMeters;

        public float ShoreRun => _shoreRun;

        public float MaxWallWidthMeters => _maxWallWidthMeters;

        public float LakeLevelStepMeters => _lakeLevelStepMeters;

        public VoxelType RiverBed => _riverBed;

        public VoxelType LakeBed => _lakeBed;

        public bool TryValidate(out string error)
        {
            if (_riverBed == null || _lakeBed == null)
            {
                error = "River and lake beds are required.";
            }
            else if (
                _erosionSteps < 0
                || _erosionStrength < 0f
                || _erosionChannelAreaKm2 < 0f
                || _maxErosionMeters < 0f
                || _lakeMinDepthMeters <= 0f
                || _lakeMinCells < 1
                || _lakeMarginMeters < 0f
            )
            {
                error =
                    "Erosion values must not be negative; lake depth and size must be positive.";
            }
            else if (
                _channelAreaKm2 <= 0f
                || _minHalfWidthMeters <= 0f
                || _maxHalfWidthMeters < _minHalfWidthMeters
                || _halfWidthAtStartMeters <= 0f
                || _minDepthMeters <= 0f
                || _maxDepthMeters < _minDepthMeters
                || _depthAtStartMeters <= 0f
            )
            {
                error = "Channel area, widths and depths must be positive, max not below min.";
            }
            else if (
                _segmentMeters < 2f
                || _meanderAmplitude < 0f
                || _meanderScaleMeters <= 0f
                || _riverInsetMeters < 0f
                || _fallMinMeters <= 0f
                || _fallSteepness <= 0f
            )
            {
                error =
                    "Segments of at least 2 m; meander, inset and fall values must be positive.";
            }
            else if (
                _bankWidthMeters <= 0f
                || _bankLipMeters < 0f
                || _shoreCutMeters < 0f
                || _shoreRun <= 0f
                || _maxWallWidthMeters <= _bankWidthMeters + _shoreCutMeters * _shoreRun
            )
            {
                error =
                    "Bank values must be positive; the widest wall wider than the bank and the shore.";
            }
            else if (_lakeLevelStepMeters < WorldConstants.VoxelSize)
            {
                error = "The lake level step must be at least a voxel.";
            }
            else
            {
                error = string.Empty;
                return true;
            }

            return false;
        }

        private void OnValidate()
        {
            if (!TryValidate(out var error))
            {
                Debug.LogWarning($"[World] {name}: {error}", this);
            }
        }
    }
}
