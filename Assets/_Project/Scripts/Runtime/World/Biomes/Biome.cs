using System;
using UnityEngine;

namespace Revoxel.World
{
    // A climate region of the island (D-062): where it lies in the climate fields, how it
    // shapes the terrain and what its ground is made of (D-063). One asset per biome, named
    // Biome_<Name> (docs/05), listed in the BiomeRegistry. Every number is a placeholder
    // (Q-12).
    [CreateAssetMenu(menuName = "Revoxel/World/Biome", fileName = "Biome_")]
    public sealed class Biome : ScriptableObject
    {
        // TerrainParams packs the strata into a fixed-size list for Burst.
        public const int MaxStrata = 8;

        // TerrainParams packs the reef corals into a fixed-size list for Burst.
        public const int MaxReefCorals = 4;

        // Packed for Burst (D-085).
        public const int MaxTreeSpecies = 4;
        public const int MaxPlants = 4;

        private const string Placeholder = " Placeholder value (Q-12).";
        private const string Range =
            "Range (min, max) the biome claims; outside it the biome loses weight.";

        [SerializeField]
        [Tooltip("Index in the biome registry, dense from 0.")]
        private byte _id;

        [SerializeField]
        [Tooltip("Localization key of the display name, biome.<name>.name.")]
        private string _nameKey = string.Empty;

        [Header("Climate")]
        [SerializeField]
        [Tooltip("Temperature at the terrain altitude, °C. " + Range + Placeholder)]
        private Vector2 _temperatureC = new Vector2(-100f, 100f);

        [SerializeField]
        [Tooltip("Moisture, 0 to 1. " + Range + Placeholder)]
        private Vector2 _moisture = new Vector2(-10f, 10f);

        [SerializeField]
        [Tooltip(
            "Inland, below 0 at sea, 0 at the coast, about 1 at the center. " + Range + Placeholder
        )]
        private Vector2 _inland = new Vector2(-10f, 10f);

        [SerializeField]
        [Tooltip(
            "Altitude of the island shape and ridges above sea level, m. " + Range + Placeholder
        )]
        private Vector2 _altitudeMeters = new Vector2(-1000f, 1000f);

        [SerializeField]
        [Tooltip("Dump site field, 0 to 1. " + Range + Placeholder)]
        private Vector2 _waste = new Vector2(-10f, 10f);

        [SerializeField]
        [Tooltip("Added to the climate distance: lower wins where ranges overlap." + Placeholder)]
        private float _bias;

        [Header("Shape")]
        [SerializeField]
        [Tooltip("How much of the island base height the biome keeps, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _baseInfluence = 1f;

        [SerializeField]
        [Tooltip("Height added to the base, m." + Placeholder)]
        private float _offsetMeters;

        [SerializeField]
        [Tooltip("Amplitude of the hills noise, m." + Placeholder)]
        private float _hillAmplitudeMeters = 4f;

        [SerializeField]
        [Tooltip("How strongly heights are cut into terraces, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _terrace;

        [Header("Ground")]
        [SerializeField]
        private VoxelType _surface;

        [SerializeField]
        [Tooltip("Surface in patches; the share sets how much of the ground it covers.")]
        private VoxelType _alternateSurface;

        [SerializeField]
        [Tooltip("Share of the ground covered by the alternate surface, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _alternateShare;

        [SerializeField]
        [Tooltip("Surface and soil near sea level and under water.")]
        private VoxelType _shore;

        [SerializeField]
        private VoxelType _soil;

        [SerializeField]
        [Tooltip("Soil depth (min, max) on flat ground, m." + Placeholder)]
        private Vector2 _soilDepthMeters = new Vector2(0.5f, 2f);

        [SerializeField]
        [Tooltip(
            "Rock layers below the soil; a stratum picks one by hash. Repeat a type to weight it."
        )]
        private VoxelType[] _strata = Array.Empty<VoxelType>();

        [SerializeField]
        [Tooltip("Thickness of one stratum, m." + Placeholder)]
        private float _strataThicknessMeters = 2f;

        [SerializeField]
        [Tooltip("Chance that a pocket cell holds buried waste, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _wastePocketChance;

        [Header("Rivers (D-076)")]
        [SerializeField]
        [Tooltip(
            "Horizontal run per meter of a river valley's walls below its shore: low for canyons."
                + Placeholder
        )]
        private float _riverWallRun = 3f;

        [Header("Reef (D-072)")]
        [SerializeField]
        [Tooltip(
            "Height of the coral heads rising from the sea floor, m; 0 for none." + Placeholder
        )]
        private float _reefHeightMeters;

        [SerializeField]
        [Tooltip("Feature size of the coral heads, m." + Placeholder)]
        private float _reefScaleMeters = 20f;

        [SerializeField]
        [Tooltip("Share of the floor covered by coral heads, 0 to 1." + Placeholder)]
        [Range(0f, 1f)]
        private float _reefCover = 0.4f;

        [SerializeField]
        [Tooltip("Water kept above the highest coral head, m." + Placeholder)]
        private float _reefClearanceMeters = 1f;

        [SerializeField]
        [Tooltip(
            "Share of a head's full height above which its top is coral, 0 to 1." + Placeholder
        )]
        [Range(0f, 1f)]
        private float _reefCoralMin = 0.3f;

        [SerializeField]
        [Tooltip("Coral types on the heads, picked in patches.")]
        private VoxelType[] _reefCorals = Array.Empty<VoxelType>();

        [SerializeField]
        [Tooltip("Rock under the coral of a head.")]
        private VoxelType _reefRock;

        [Header("Vegetation (D-085)")]
        [SerializeField]
        [Tooltip(
            "Chance that a tree candidate cell holds a tree before spacing thins them, 0 to 1."
                + Placeholder
        )]
        [Range(0f, 1f)]
        private float _treeCover;

        [SerializeField]
        [Tooltip("Species that grow here, from the vegetation settings.")]
        private TreeSpecies[] _trees = Array.Empty<TreeSpecies>();

        [SerializeField]
        [Tooltip("Relative weight of each species, same order." + Placeholder)]
        private float[] _treeWeights = Array.Empty<float>();

        [SerializeField]
        [Tooltip("Ground plants, tried in order on each column.")]
        private BiomePlant[] _plants = Array.Empty<BiomePlant>();

        public BiomeId Id => new BiomeId(_id);

        public string NameKey => _nameKey;

        public Vector2 TemperatureC => _temperatureC;

        public Vector2 Moisture => _moisture;

        public Vector2 Inland => _inland;

        public Vector2 AltitudeMeters => _altitudeMeters;

        public Vector2 Waste => _waste;

        public float Bias => _bias;

        public float BaseInfluence => _baseInfluence;

        public float OffsetMeters => _offsetMeters;

        public float HillAmplitudeMeters => _hillAmplitudeMeters;

        public float Terrace => _terrace;

        public VoxelType Surface => _surface;

        public VoxelType AlternateSurface => _alternateSurface;

        public float AlternateShare => _alternateShare;

        public VoxelType Shore => _shore;

        public VoxelType Soil => _soil;

        public Vector2 SoilDepthMeters => _soilDepthMeters;

        public VoxelType[] Strata => _strata;

        public float StrataThicknessMeters => _strataThicknessMeters;

        public float WastePocketChance => _wastePocketChance;

        public float RiverWallRun => _riverWallRun;

        public float ReefHeightMeters => _reefHeightMeters;

        public float ReefScaleMeters => _reefScaleMeters;

        public float ReefCover => _reefCover;

        public float ReefClearanceMeters => _reefClearanceMeters;

        public float ReefCoralMin => _reefCoralMin;

        public VoxelType[] ReefCorals => _reefCorals;

        public VoxelType ReefRock => _reefRock;

        public bool HasReef => _reefHeightMeters > 0f;

        public float TreeCover => _treeCover;

        public TreeSpecies[] Trees => _trees;

        public float[] TreeWeights => _treeWeights;

        public BiomePlant[] Plants => _plants;

        public bool TryValidate(float subsurfaceDepthMeters, out string error)
        {
            if (string.IsNullOrEmpty(_nameKey))
            {
                error = $"{name}: name key is empty.";
            }
            else if (_surface == null || _shore == null || _soil == null)
            {
                error = $"{name}: surface, shore and soil types are required.";
            }
            else if (_alternateShare > 0f && _alternateSurface == null)
            {
                error = $"{name}: an alternate share needs an alternate surface.";
            }
            else if (_strata == null || _strata.Length == 0 || _strata.Length > MaxStrata)
            {
                error = $"{name}: strata must list 1 to {MaxStrata} types.";
            }
            else if (Array.IndexOf(_strata, null) >= 0)
            {
                error = $"{name}: a stratum type is missing.";
            }
            else if (
                _temperatureC.x > _temperatureC.y
                || _moisture.x > _moisture.y
                || _inland.x > _inland.y
                || _altitudeMeters.x > _altitudeMeters.y
                || _waste.x > _waste.y
            )
            {
                error = $"{name}: a climate range has its min above its max.";
            }
            else if (
                _soilDepthMeters.x < 0f
                || _soilDepthMeters.y < _soilDepthMeters.x
                || _soilDepthMeters.y >= subsurfaceDepthMeters
            )
            {
                error =
                    $"{name}: soil depth must be a range from 0 up to below the subsurface depth ({subsurfaceDepthMeters} m).";
            }
            else if (_strataThicknessMeters < WorldConstants.VoxelSize || _hillAmplitudeMeters < 0f)
            {
                error = $"{name}: strata thinner than a voxel or negative hill amplitude.";
            }
            else if (_riverWallRun <= 0f)
            {
                error = $"{name}: the river wall run must be positive.";
            }
            else if (
                _reefHeightMeters < 0f
                || (
                    HasReef
                    && (
                        _reefScaleMeters <= 0f
                        || _reefClearanceMeters < 0f
                        || _reefRock == null
                        || _reefCorals == null
                        || _reefCorals.Length == 0
                        || _reefCorals.Length > MaxReefCorals
                        || Array.IndexOf(_reefCorals, null) >= 0
                    )
                )
            )
            {
                error =
                    $"{name}: a reef needs a positive scale, a clearance, a rock and 1 to {MaxReefCorals} corals.";
            }
            else if (!TryValidateVegetation(out error))
            {
                error = $"{name}: {error}";
            }
            else
            {
                error = string.Empty;
                return true;
            }

            return false;
        }

        private bool TryValidateVegetation(out string error)
        {
            var trees = _trees ?? Array.Empty<TreeSpecies>();
            var weights = _treeWeights ?? Array.Empty<float>();
            var plants = _plants ?? Array.Empty<BiomePlant>();
            if (trees.Length > MaxTreeSpecies || Array.IndexOf(trees, null) >= 0)
            {
                error = $"trees must list at most {MaxTreeSpecies} species and no empty entry.";
                return false;
            }

            if (weights.Length != trees.Length || Array.Exists(weights, w => !(w > 0f)))
            {
                error = "every tree species needs a positive weight.";
                return false;
            }

            if (_treeCover > 0f && trees.Length == 0)
            {
                error = "a tree cover needs at least one species.";
                return false;
            }

            if (plants.Length > MaxPlants || Array.IndexOf(plants, null) >= 0)
            {
                error = $"plants must list at most {MaxPlants} plants.";
                return false;
            }

            foreach (var plant in plants)
            {
                if (!plant.TryValidate(out error))
                {
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
    }
}
