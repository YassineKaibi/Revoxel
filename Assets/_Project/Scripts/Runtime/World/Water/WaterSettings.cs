using UnityEngine;

namespace Revoxel.World
{
    // Rivers and lakes (PLAN-05, D-070, D-071): how many, where they start, how they run
    // and how they cut the land. Read by TerrainParams.Create, which plans them once per
    // seed. Every value is a placeholder (Q-12); lengths in meters.
    [CreateAssetMenu(
        menuName = "Revoxel/World/Water Settings",
        fileName = "Placeholder_WaterSettings"
    )]
    public sealed class WaterSettings : ScriptableObject
    {
        // TerrainParams holds the plan in fixed-size lists for Burst.
        public const int MaxRiverCount = 16;
        public const int MaxLakeCount = 20;
        public const int MaxRiverNodes = 340;

        private const string Placeholder = " Placeholder value (Q-12).";

        [Header("Rivers")]
        [SerializeField]
        [Tooltip("Most rivers planned on the island." + Placeholder)]
        [Range(0, MaxRiverCount)]
        private int _maxRivers = 12;

        [SerializeField]
        [Tooltip("Spring positions tried as river sources." + Placeholder)]
        private int _springCandidates = 256;

        [SerializeField]
        [Tooltip("Lowest spring, above sea level, m." + Placeholder)]
        private float _springMinAltitudeMeters = 40f;

        [SerializeField]
        [Tooltip("Springs lie at least this far inland, in inland units." + Placeholder)]
        private float _springMinInland = 0.2f;

        [SerializeField]
        [Tooltip("Two sources lie at least this far apart, m." + Placeholder)]
        private float _sourceSpacingMeters = 500f;

        [SerializeField]
        [Tooltip("Length of one traced step, m." + Placeholder)]
        private float _riverStepMeters = 48f;

        [SerializeField]
        [Tooltip(
            "A river not at the sea, a lake or another river after this many steps is dropped."
                + Placeholder
        )]
        private int _riverMaxSteps = 100;

        [SerializeField]
        [Tooltip("Rivers shorter than this many steps are dropped." + Placeholder)]
        private int _riverMinSteps = 8;

        [SerializeField]
        [Tooltip("Largest turn per step, degrees." + Placeholder)]
        [Range(5f, 80f)]
        private float _riverTurnDegrees = 20f;

        [SerializeField]
        [Tooltip(
            "Height a step may climb per meter it gains toward the coast (keeps rivers going to the sea)."
                + Placeholder
        )]
        private float _riverOutwardBias = 0.05f;

        [SerializeField]
        [Tooltip(
            "Rivers that would cut deeper than this through a rise are dropped, m." + Placeholder
        )]
        private float _riverMaxCutMeters = 24f;

        [SerializeField]
        [Tooltip(
            "Extra cost per meter of ground above the water when a river picks its way."
                + Placeholder
        )]
        private float _riverRisePenalty = 8f;

        [SerializeField]
        [Tooltip("Water surface below the lowest ground of a step, m." + Placeholder)]
        private float _riverInsetMeters = 0.75f;

        [SerializeField]
        [Tooltip("Half width of the channel at the source (x) and the mouth (y), m." + Placeholder)]
        private Vector2 _riverHalfWidthMeters = new Vector2(4f, 18f);

        [SerializeField]
        [Tooltip("Depth of the channel at the source (x) and the mouth (y), m." + Placeholder)]
        private Vector2 _riverDepthMeters = new Vector2(1f, 2.5f);

        [SerializeField]
        [Tooltip("A drop of at least this between two steps is a waterfall, m." + Placeholder)]
        private float _fallMinMeters = 2f;

        [SerializeField]
        [Tooltip("Height of a waterfall face per meter of its length." + Placeholder)]
        private float _fallSteepness = 4f;

        [SerializeField]
        [Tooltip("A river this close to an earlier one joins it, m." + Placeholder)]
        private float _joinDistanceMeters = 60f;

        [SerializeField]
        [Tooltip("Sideways wander of the channel around the traced path, m." + Placeholder)]
        private float _meanderMeters = 20f;

        [SerializeField]
        [Tooltip("Feature size of the wander, m." + Placeholder)]
        private float _meanderScaleMeters = 160f;

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
        private float _maxWallWidthMeters = 150f;

        [Header("Lakes")]
        [SerializeField]
        [Tooltip("Most lakes planned on the island." + Placeholder)]
        [Range(0, MaxLakeCount)]
        private int _maxLakes = 14;

        [SerializeField]
        [Tooltip("Lake positions tried." + Placeholder)]
        private int _lakeCandidates = 1024;

        [SerializeField]
        [Tooltip("Lake radius (min, max), m." + Placeholder)]
        private Vector2 _lakeRadiusMeters = new Vector2(100f, 400f);

        [SerializeField]
        [Tooltip("Lakes lie at least this far inland, in inland units." + Placeholder)]
        private float _lakeMinInland = 0.12f;

        [SerializeField]
        [Tooltip("Lowest lake surface above sea level, m." + Placeholder)]
        private float _lakeMinAltitudeMeters = 6f;

        [SerializeField]
        [Tooltip("Largest height difference around a lake site, m." + Placeholder)]
        private float _lakeMaxReliefMeters = 16f;

        [SerializeField]
        [Tooltip("Depth at the lake center, m." + Placeholder)]
        private float _lakeDepthMeters = 8f;

        [SerializeField]
        [Tooltip("How much the shore wanders from a circle, share of the radius." + Placeholder)]
        [Range(0f, 0.6f)]
        private float _lakeShapeNoise = 0.35f;

        [SerializeField]
        [Tooltip("Feature size of the shore wander, m." + Placeholder)]
        private float _lakeShapeScaleMeters = 180f;

        [SerializeField]
        [Tooltip(
            "Lake surfaces sit this far below the lowest ground around them, m." + Placeholder
        )]
        private float _lakeRimMarginMeters = 1f;

        [SerializeField]
        [Tooltip(
            "Lake surfaces sit on multiples of this height; detail levels with voxels up to this size draw them exactly, m."
                + Placeholder
        )]
        private float _lakeLevelStepMeters = 1f;

        [SerializeField]
        [Tooltip("Share of the lakes that feed a river, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _lakeOutletShare = 0.6f;

        [Header("Beds")]
        [SerializeField]
        [Tooltip("Ground under river water.")]
        private VoxelType _riverBed;

        [SerializeField]
        [Tooltip("Ground under lake water.")]
        private VoxelType _lakeBed;

        public int MaxRivers => _maxRivers;

        public int SpringCandidates => _springCandidates;

        public float SpringMinAltitudeMeters => _springMinAltitudeMeters;

        public float SpringMinInland => _springMinInland;

        public float SourceSpacingMeters => _sourceSpacingMeters;

        public float RiverStepMeters => _riverStepMeters;

        public int RiverMaxSteps => _riverMaxSteps;

        public int RiverMinSteps => _riverMinSteps;

        public float RiverTurnDegrees => _riverTurnDegrees;

        public float RiverOutwardBias => _riverOutwardBias;

        public float RiverMaxCutMeters => _riverMaxCutMeters;

        public float RiverRisePenalty => _riverRisePenalty;

        public float RiverInsetMeters => _riverInsetMeters;

        public Vector2 RiverHalfWidthMeters => _riverHalfWidthMeters;

        public Vector2 RiverDepthMeters => _riverDepthMeters;

        public float FallMinMeters => _fallMinMeters;

        public float FallSteepness => _fallSteepness;

        public float JoinDistanceMeters => _joinDistanceMeters;

        public float MeanderMeters => _meanderMeters;

        public float MeanderScaleMeters => _meanderScaleMeters;

        public float BankWidthMeters => _bankWidthMeters;

        public float BankLipMeters => _bankLipMeters;

        public float ShoreCutMeters => _shoreCutMeters;

        public float ShoreRun => _shoreRun;

        public float MaxWallWidthMeters => _maxWallWidthMeters;

        public int MaxLakes => _maxLakes;

        public int LakeCandidates => _lakeCandidates;

        public Vector2 LakeRadiusMeters => _lakeRadiusMeters;

        public float LakeMinInland => _lakeMinInland;

        public float LakeMinAltitudeMeters => _lakeMinAltitudeMeters;

        public float LakeMaxReliefMeters => _lakeMaxReliefMeters;

        public float LakeDepthMeters => _lakeDepthMeters;

        public float LakeShapeNoise => _lakeShapeNoise;

        public float LakeShapeScaleMeters => _lakeShapeScaleMeters;

        public float LakeRimMarginMeters => _lakeRimMarginMeters;

        public float LakeLevelStepMeters => _lakeLevelStepMeters;

        public float LakeOutletShare => _lakeOutletShare;

        public VoxelType RiverBed => _riverBed;

        public VoxelType LakeBed => _lakeBed;

        public bool TryValidate(out string error)
        {
            if (_riverBed == null || _lakeBed == null)
            {
                error = "River and lake beds are required.";
            }
            else if (
                _maxRivers < 0
                || _maxRivers > MaxRiverCount
                || _maxLakes < 0
                || _maxLakes > MaxLakeCount
            )
            {
                error = $"At most {MaxRiverCount} rivers and {MaxLakeCount} lakes.";
            }
            else if (
                _springCandidates < 0
                || _lakeCandidates < 0
                || _riverMaxSteps < 1
                || _riverMinSteps < 1
                || _riverMinSteps > _riverMaxSteps
            )
            {
                error =
                    "Candidate counts must not be negative and step counts must be 1 or more, min not above max.";
            }
            else if (
                _riverStepMeters < 4f
                || _riverHalfWidthMeters.x <= 0f
                || _riverHalfWidthMeters.y < _riverHalfWidthMeters.x
                || _riverDepthMeters.x <= 0f
                || _riverDepthMeters.y < _riverDepthMeters.x
            )
            {
                error =
                    "Step of at least 4 m; positive half widths and depths, mouth not below source.";
            }
            else if (_riverHalfWidthMeters.y * 2f >= _riverStepMeters * 4f)
            {
                error = "Rivers must be narrower than two steps.";
            }
            else if (
                _bankWidthMeters <= 0f
                || _maxWallWidthMeters < _bankWidthMeters
                || _fallMinMeters <= 0f
                || _fallSteepness <= 0f
                || _joinDistanceMeters < 0f
                || _meanderMeters < 0f
                || _meanderScaleMeters <= 0f
                || _riverInsetMeters < 0f
                || _riverMaxCutMeters <= 0f
                || _riverRisePenalty < 0f
                || _bankLipMeters < 0f
                || _shoreCutMeters < 0f
                || _shoreRun <= 0f
            )
            {
                error =
                    "Bank, wall, levee, fall and meander values must be positive (wall width at least the bank).";
            }
            else if (_maxWallWidthMeters <= _bankWidthMeters + _shoreCutMeters * _shoreRun)
            {
                error = "The widest wall must be wider than the bank and the shore.";
            }
            else if (
                _lakeRadiusMeters.x <= 0f
                || _lakeRadiusMeters.y < _lakeRadiusMeters.x
                || _lakeDepthMeters <= 0f
                || _lakeShapeScaleMeters <= 0f
                || _lakeLevelStepMeters < WorldConstants.VoxelSize
                || _lakeMaxReliefMeters <= 0f
                || _lakeRimMarginMeters < 0f
            )
            {
                error = "Lake radius, depth, shape scale, relief and level step must be positive.";
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
