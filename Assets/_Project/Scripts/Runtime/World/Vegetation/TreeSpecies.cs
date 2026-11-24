using UnityEngine;

namespace Revoxel.World
{
    // How a species builds its skeleton (D-083).
    public enum TreeForm : byte
    {
        // Space colonization toward attractors in a crown envelope (oak, birch, shrubs).
        Broadleaf = 0,

        // A leader with whorls of drooping branches (pines).
        Conifer = 1,

        // An arched trunk with a crown of fronds.
        Palm = 2,
    }

    // One tree species (D-083, D-085): the ranges its trees are drawn from, how its skeleton
    // grows and what it is made of. One asset per species, named Species_<Name>, listed in
    // the vegetation settings; biomes pick species from that list. Every number is a
    // placeholder (Q-12); lengths in meters.
    [CreateAssetMenu(menuName = "Revoxel/World/Tree Species", fileName = "Species_")]
    public sealed class TreeSpecies : ScriptableObject
    {
        private const string Placeholder = " Placeholder value (Q-12).";

        [SerializeField]
        private TreeForm _form;

        [Header("Size")]
        [SerializeField]
        [Tooltip(
            "Height of a tree (min, max), m: saplings near the min, old trees near the max."
                + Placeholder
        )]
        private Vector2 _heightMeters = new Vector2(8f, 14f);

        [SerializeField]
        [Tooltip("Trunk radius at the base, above the flare (min, max), m." + Placeholder)]
        private Vector2 _trunkRadiusMeters = new Vector2(0.2f, 0.35f);

        [SerializeField]
        [Tooltip("Largest horizontal offset of the top, share of the height." + Placeholder)]
        [Range(0f, 1f)]
        private float _lean = 0.12f;

        [SerializeField]
        [Tooltip("Sideways wander of the trunk, share of the height." + Placeholder)]
        [Range(0f, 0.5f)]
        private float _wander = 0.05f;

        [SerializeField]
        [Tooltip("Share of the height below the crown (bare trunk)." + Placeholder)]
        [Range(0f, 1f)]
        private float _crownBase = 0.35f;

        [SerializeField]
        [Tooltip("Crown radius (min, max), share of the height." + Placeholder)]
        private Vector2 _crownRadius = new Vector2(0.25f, 0.4f);

        [SerializeField]
        [Tooltip(
            "How far the trunk runs on into the crown as a leader, share of the crown's height: low for oaks that fork, 1 for birches."
                + Placeholder
        )]
        [Range(0f, 1f)]
        private float _leader = 0.5f;

        [Header("Growth (broadleaf)")]
        [SerializeField]
        [Tooltip("Attractors scattered in the crown envelope." + Placeholder)]
        private int _attractors = 120;

        [SerializeField]
        [Tooltip("Length of one skeleton segment, m." + Placeholder)]
        private float _stepMeters = 0.4f;

        [SerializeField]
        [Tooltip("An attractor pulls the nearest node within this distance, m." + Placeholder)]
        private float _influenceMeters = 2.5f;

        [SerializeField]
        [Tooltip("An attractor is reached once a node comes this close, m." + Placeholder)]
        private float _killMeters = 0.8f;

        [SerializeField]
        [Tooltip("Pull of new growth upward, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _tropism = 0.2f;

        [SerializeField]
        [Tooltip("Most skeleton nodes of one tree." + Placeholder)]
        private int _maxNodes = 240;

        [SerializeField]
        [Tooltip(
            "Pipe model exponent: a parent's radius^e is the sum of its children's (2 is da Vinci's rule)."
                + Placeholder
        )]
        private float _pipeExponent = 2.4f;

        [Header("Growth (conifer)")]
        [SerializeField]
        [Tooltip("Height between two whorls of branches, m." + Placeholder)]
        private float _whorlSpacingMeters = 0.6f;

        [SerializeField]
        [Tooltip("Branches per whorl." + Placeholder)]
        private int _branchesPerWhorl = 5;

        [SerializeField]
        [Tooltip("How far branches bend down toward their tips, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _branchDroop = 0.35f;

        [Header("Growth (palm)")]
        [SerializeField]
        [Tooltip("Fronds at the top." + Placeholder)]
        private int _fronds = 9;

        [SerializeField]
        [Tooltip("Length of a frond, m." + Placeholder)]
        private float _frondLengthMeters = 2.6f;

        [Header("Base")]
        [SerializeField]
        [Tooltip("Extra trunk radius at the ground, share of the trunk radius." + Placeholder)]
        private float _rootFlare = 0.6f;

        [SerializeField]
        [Tooltip("Height over which the flare fades, m." + Placeholder)]
        private float _flareHeightMeters = 0.5f;

        [SerializeField]
        [Tooltip("Prop roots arching from the trunk to the ground (mangroves)." + Placeholder)]
        private int _propRoots;

        [SerializeField]
        [Tooltip("How far prop roots reach from the trunk, m." + Placeholder)]
        private float _propRootReachMeters = 1.5f;

        [Header("Leaves")]
        [SerializeField]
        [Tooltip("Radius of a leaf cluster (min, max), m." + Placeholder)]
        private Vector2 _leafRadiusMeters = new Vector2(0.45f, 0.8f);

        [SerializeField]
        [Tooltip("Share of a leaf cluster left open, 0 to 1." + Placeholder)]
        [Range(0f, 0.8f)]
        private float _leafPorosity = 0.2f;

        [Header("Materials")]
        [SerializeField]
        [Tooltip("Outer voxel layer of trunks and branches.")]
        private VoxelType _bark;

        [SerializeField]
        [Tooltip("Inside of trunks and branches.")]
        private VoxelType _wood;

        [SerializeField]
        [Tooltip("Leaf clusters; empty for a leafless tree.")]
        private VoxelType _leaves;

        [Header("Where it grows")]
        [SerializeField]
        [Tooltip("Steepest ground, rise per run." + Placeholder)]
        private float _maxSlope = 0.6f;

        [SerializeField]
        [Tooltip("Coldest surface temperature (the tree line), °C." + Placeholder)]
        private float _minTemperatureC = -2f;

        [SerializeField]
        [Tooltip("Deepest water it stands in, m; 0 for dry ground only." + Placeholder)]
        private float _maxWaterDepthMeters;

        [SerializeField]
        [Tooltip("Grows on shore sand.")]
        private bool _growsOnShore;

        [SerializeField]
        [Tooltip("No other tree stands closer to its trunk, m." + Placeholder)]
        private float _spacingMeters = 4f;

        public TreeForm Form => _form;

        public Vector2 HeightMeters => _heightMeters;

        public Vector2 TrunkRadiusMeters => _trunkRadiusMeters;

        public float Lean => _lean;

        public float Wander => _wander;

        public float CrownBase => _crownBase;

        public Vector2 CrownRadius => _crownRadius;

        public float Leader => _leader;

        public int Attractors => _attractors;

        public float StepMeters => _stepMeters;

        public float InfluenceMeters => _influenceMeters;

        public float KillMeters => _killMeters;

        public float Tropism => _tropism;

        public int MaxNodes => _maxNodes;

        public float PipeExponent => _pipeExponent;

        public float WhorlSpacingMeters => _whorlSpacingMeters;

        public int BranchesPerWhorl => _branchesPerWhorl;

        public float BranchDroop => _branchDroop;

        public int Fronds => _fronds;

        public float FrondLengthMeters => _frondLengthMeters;

        public float RootFlare => _rootFlare;

        public float FlareHeightMeters => _flareHeightMeters;

        public int PropRoots => _propRoots;

        public float PropRootReachMeters => _propRootReachMeters;

        public Vector2 LeafRadiusMeters => _leafRadiusMeters;

        public float LeafPorosity => _leafPorosity;

        public VoxelType Bark => _bark;

        public VoxelType Wood => _wood;

        public VoxelType Leaves => _leaves;

        public float MaxSlope => _maxSlope;

        public float MinTemperatureC => _minTemperatureC;

        public float MaxWaterDepthMeters => _maxWaterDepthMeters;

        public bool GrowsOnShore => _growsOnShore;

        public float SpacingMeters => _spacingMeters;

        public bool TryValidate(out string error)
        {
            if (_bark == null || _wood == null)
            {
                error = $"{name}: bark and wood types are required.";
            }
            else if (
                _heightMeters.x <= 0f
                || _heightMeters.y < _heightMeters.x
                || _trunkRadiusMeters.x <= 0f
                || _trunkRadiusMeters.y < _trunkRadiusMeters.x
                || _crownRadius.x <= 0f
                || _crownRadius.y < _crownRadius.x
            )
            {
                error =
                    $"{name}: height, trunk radius and crown ranges must be positive (min, max).";
            }
            else if (
                _stepMeters < WorldConstants.VoxelSize
                || _influenceMeters <= _killMeters
                || _killMeters < _stepMeters
                || _attractors < 1
                || _attractors > TreeGrowth.MaxAttractors
                || _maxNodes < 8
                || _maxNodes > TreeGrowth.MaxNodes
                || _pipeExponent < 1f
            )
            {
                error =
                    $"{name}: growth needs a step of at least a voxel, kill distance at least the step and below the influence, 1 to {TreeGrowth.MaxAttractors} attractors, 8 to {TreeGrowth.MaxNodes} nodes and a pipe exponent of at least 1.";
            }
            else if (
                _whorlSpacingMeters < WorldConstants.VoxelSize
                || _branchesPerWhorl < 1
                || _branchesPerWhorl > 12
                || _fronds < 1
                || _fronds > 24
                || _frondLengthMeters <= 0f
                || _propRoots < 0
                || _propRoots > 12
                || _propRootReachMeters <= 0f
                || _flareHeightMeters <= 0f
                || _rootFlare < 0f
            )
            {
                error = $"{name}: whorls, fronds, prop roots or the flare are out of range.";
            }
            else if (
                _leafRadiusMeters.x < WorldConstants.VoxelSize
                || _leafRadiusMeters.y < _leafRadiusMeters.x
            )
            {
                error = $"{name}: leaf radius must be a range of at least a voxel.";
            }
            else if (_maxSlope <= 0f || _maxWaterDepthMeters < 0f || _spacingMeters <= 0f)
            {
                error = $"{name}: slope and spacing must be positive, water depth not negative.";
            }
            else
            {
                error = string.Empty;
                return true;
            }

            return false;
        }
    }
}
