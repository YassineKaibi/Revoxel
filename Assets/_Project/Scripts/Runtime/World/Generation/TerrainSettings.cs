using UnityEngine;

namespace Revoxel.World
{
    // Placeholder terrain (PLAN-02 Task 5), a stand-in for WP-04: an island of fBm hills
    // with varied soil depth. Every value is a placeholder (Q-12); lengths in meters.
    [CreateAssetMenu(
        menuName = "Revoxel/World/Terrain Settings",
        fileName = "Placeholder_TerrainSettings"
    )]
    public sealed class TerrainSettings : ScriptableObject
    {
        private const string Placeholder = "Placeholder value (Q-12).";

        [SerializeField]
        [Tooltip("Sea level above the island floor, meters. " + Placeholder)]
        private float _seaLevelMeters = 64f;

        [SerializeField]
        [Tooltip("Distance from the origin where the coast starts falling, meters. " + Placeholder)]
        private float _islandRadiusMeters = 3600f;

        [SerializeField]
        [Tooltip("Width of the coastal falloff to the sea floor, meters. " + Placeholder)]
        private float _coastFalloffMeters = 400f;

        [SerializeField]
        [Tooltip("Height of the hills above sea level at full noise, meters. " + Placeholder)]
        private float _landHeightMeters = 120f;

        [SerializeField]
        [Tooltip("fBm octaves, 1 to 8. " + Placeholder)]
        private int _octaves = 4;

        [SerializeField]
        [Tooltip("Feature size of the first octave, meters. " + Placeholder)]
        private float _baseScaleMeters = 600f;

        [SerializeField]
        [Tooltip("Amplitude factor between octaves. " + Placeholder)]
        private float _gain = 0.5f;

        [SerializeField]
        [Tooltip("Frequency factor between octaves. " + Placeholder)]
        private float _lacunarity = 2f;

        [SerializeField]
        [Tooltip("Thinnest soil on flat ground, meters. " + Placeholder)]
        private float _soilMinMeters = 0.25f;

        [SerializeField]
        [Tooltip("Thickest soil on flat ground, meters. " + Placeholder)]
        private float _soilMaxMeters = 2f;

        [SerializeField]
        [Tooltip("Feature size of the soil depth noise, meters. " + Placeholder)]
        private float _soilScaleMeters = 40f;

        [SerializeField]
        [Tooltip("Slope (voxel rise per voxel) at which soil reaches 0. " + Placeholder)]
        private float _soilZeroSlope = 1.5f;

        [SerializeField]
        [Tooltip("Surfaces up to this height above sea level are sand, meters. " + Placeholder)]
        private float _sandAboveSeaMeters = 1.5f;

        [SerializeField]
        private VoxelType _water;

        [SerializeField]
        private VoxelType _sand;

        [SerializeField]
        private VoxelType _dirt;

        [SerializeField]
        private VoxelType _grass;

        [SerializeField]
        private VoxelType _stone;

        public float SeaLevelMeters => _seaLevelMeters;

        public float IslandRadiusMeters => _islandRadiusMeters;

        public float CoastFalloffMeters => _coastFalloffMeters;

        public float LandHeightMeters => _landHeightMeters;

        public int Octaves => _octaves;

        public float BaseScaleMeters => _baseScaleMeters;

        public float Gain => _gain;

        public float Lacunarity => _lacunarity;

        public float SoilMinMeters => _soilMinMeters;

        public float SoilMaxMeters => _soilMaxMeters;

        public float SoilScaleMeters => _soilScaleMeters;

        public float SoilZeroSlope => _soilZeroSlope;

        public float SandAboveSeaMeters => _sandAboveSeaMeters;

        public VoxelType Water => _water;

        public VoxelType Sand => _sand;

        public VoxelType Dirt => _dirt;

        public VoxelType Grass => _grass;

        public VoxelType Stone => _stone;

        public bool TryValidate(out string error)
        {
            const float worldHeight = WorldConstants.WorldHeightVoxels * WorldConstants.VoxelSize;
            if (
                _water == null
                || _sand == null
                || _dirt == null
                || _grass == null
                || _stone == null
            )
            {
                error = "A voxel type reference is missing.";
            }
            else if (_seaLevelMeters <= 0f || _seaLevelMeters + _landHeightMeters >= worldHeight)
            {
                error = $"Sea level plus land height must fit in {worldHeight} m.";
            }
            else if (
                _landHeightMeters <= 0f
                || _islandRadiusMeters <= 0f
                || _coastFalloffMeters <= 0f
            )
            {
                error = "Land height, island radius and coastal falloff must be positive.";
            }
            else if (_octaves < 1 || _octaves > 8)
            {
                error = "Octaves must be 1 to 8.";
            }
            else if (
                _baseScaleMeters <= 0f
                || _soilScaleMeters <= 0f
                || _gain <= 0f
                || _lacunarity <= 0f
            )
            {
                error = "Scales, gain and lacunarity must be positive.";
            }
            else if (_soilMinMeters < 0f || _soilMaxMeters < _soilMinMeters)
            {
                error = "Soil depth range is invalid.";
            }
            else if (_soilZeroSlope <= 0f || _sandAboveSeaMeters < 0f)
            {
                error = "Soil zero slope must be positive and sand height not negative.";
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
