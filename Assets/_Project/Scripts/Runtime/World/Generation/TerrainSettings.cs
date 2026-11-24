using System;
using UnityEngine;

namespace Revoxel.World
{
    // The island generator's global values (PLAN-04): island shape, climate fields, biome
    // blending (D-062) and subsurface (D-063). Per-biome values live in the Biome assets.
    // Every value is a placeholder (Q-12); lengths in meters.
    [CreateAssetMenu(
        menuName = "Revoxel/World/Terrain Settings",
        fileName = "Placeholder_TerrainSettings"
    )]
    public sealed class TerrainSettings : ScriptableObject
    {
        // TerrainParams packs the pocket types into a fixed-size list for Burst.
        public const int MaxPocketTypes = 15;

        private const string Placeholder = " Placeholder value (Q-12).";

        [Header("Island")]
        [SerializeField]
        [Tooltip("Sea level above the island floor, m." + Placeholder)]
        private float _seaLevelMeters = 64f;

        [SerializeField]
        [Tooltip("Radius where the inland field is 0 before the coast noise, m." + Placeholder)]
        private float _islandRadiusMeters = 3000f;

        [SerializeField]
        [Tooltip("Land never reaches this distance from the origin, m." + Placeholder)]
        private float _edgeRadiusMeters = 3850f;

        [SerializeField]
        [Tooltip("Coast noise amplitude, in inland units." + Placeholder)]
        private float _coastNoise = 0.3f;

        [SerializeField]
        [Tooltip("Feature size of the coast noise, m." + Placeholder)]
        private float _coastScaleMeters = 1400f;

        [SerializeField]
        [Tooltip("Domain warp of the large shapes, m." + Placeholder)]
        private float _warpMeters = 350f;

        [SerializeField]
        [Tooltip("Feature size of the domain warp, m." + Placeholder)]
        private float _warpScaleMeters = 1600f;

        [SerializeField]
        [Tooltip("Base height above sea level at inland 1, m." + Placeholder)]
        private float _landRiseMeters = 40f;

        [SerializeField]
        [Tooltip("Depth of the sea floor off the shelf, m." + Placeholder)]
        private float _seaDepthMeters = 40f;

        [SerializeField]
        [Tooltip("Width of the coastal shelf, in inland units." + Placeholder)]
        private float _shelfWidth = 0.12f;

        [Header("Mountains and hills")]
        [SerializeField]
        [Tooltip("Height of the mountain ridges at full relief, m." + Placeholder)]
        private float _mountainHeightMeters = 240f;

        [SerializeField]
        [Tooltip("Feature size of the ridges, m." + Placeholder)]
        private float _ridgeScaleMeters = 900f;

        [SerializeField]
        [Tooltip("Ridge noise octaves, 1 to 8." + Placeholder)]
        private int _ridgeOctaves = 4;

        [SerializeField]
        [Tooltip("Feature size of the relief field that places the ranges, m." + Placeholder)]
        private float _reliefScaleMeters = 2200f;

        [SerializeField]
        [Tooltip("Relief noise (0 to 1) where mountains start." + Placeholder)]
        private float _reliefThreshold = 0.55f;

        [SerializeField]
        [Tooltip("Relief noise range over which mountains rise to full height." + Placeholder)]
        private float _reliefRamp = 0.15f;

        [SerializeField]
        [Tooltip(
            "Share of the mountain height that is a smooth massif under the ridges, 0 to 1."
                + Placeholder
        )]
        [Range(0f, 1f)]
        private float _massifShare = 0.4f;

        [SerializeField]
        [Tooltip("Feature size of the hills, m." + Placeholder)]
        private float _hillScaleMeters = 500f;

        [SerializeField]
        [Tooltip("Hill noise octaves, 1 to 8." + Placeholder)]
        private int _hillOctaves = 4;

        [SerializeField]
        [Tooltip("Amplitude factor between octaves." + Placeholder)]
        private float _gain = 0.5f;

        [SerializeField]
        [Tooltip("Frequency factor between octaves." + Placeholder)]
        private float _lacunarity = 2f;

        [SerializeField]
        [Tooltip("Height of one terrace step, m." + Placeholder)]
        private float _terraceStepMeters = 6f;

        [SerializeField]
        [Tooltip("Share of a terrace step taken by the riser, 0 to 1." + Placeholder)]
        [Range(0.05f, 1f)]
        private float _terraceRiser = 0.2f;

        [Header("Climate")]
        [SerializeField]
        [Tooltip("Coldest sea-level temperature, °C." + Placeholder)]
        private float _temperatureMinC = 6f;

        [SerializeField]
        [Tooltip("Warmest sea-level temperature, °C." + Placeholder)]
        private float _temperatureMaxC = 26f;

        [SerializeField]
        [Tooltip("Feature size of the temperature field, m." + Placeholder)]
        private float _temperatureScaleMeters = 2600f;

        [SerializeField]
        [Tooltip(
            "Share of the temperature from a north (cold) to south (warm) gradient, 0 to 1."
                + Placeholder
        )]
        [Range(0f, 1f)]
        private float _temperatureGradient = 0.35f;

        [SerializeField]
        [Tooltip("Temperature drop per kilometer of altitude, °C." + Placeholder)]
        private float _lapseRateCPerKm = 30f;

        [SerializeField]
        [Tooltip("Surfaces colder than this are snow, °C." + Placeholder)]
        private float _snowTemperatureC;

        [SerializeField]
        [Tooltip(
            "Snow does not hold on slopes steeper than this (voxel rise per voxel)." + Placeholder
        )]
        private float _snowMaxSlope = 1.2f;

        [SerializeField]
        [Tooltip("Feature size of the moisture field, m." + Placeholder)]
        private float _moistureScaleMeters = 2000f;

        [SerializeField]
        [Tooltip("Moisture added at the coast, fading inland." + Placeholder)]
        private float _coastMoisture = 0.2f;

        [SerializeField]
        [Tooltip("Feature size of the dump site field, m." + Placeholder)]
        private float _wasteScaleMeters = 700f;

        [SerializeField]
        [Tooltip("Dump site noise (0 to 1) where waste lands start." + Placeholder)]
        private float _wasteThreshold = 0.68f;

        [Header("Biome blending")]
        [SerializeField]
        [Tooltip("Blend width of temperature, °C." + Placeholder)]
        private float _blendTemperatureC = 2f;

        [SerializeField]
        [Tooltip("Blend width of moisture." + Placeholder)]
        private float _blendMoisture = 0.08f;

        [SerializeField]
        [Tooltip("Blend width of inland." + Placeholder)]
        private float _blendInland = 0.03f;

        [SerializeField]
        [Tooltip("Blend width of altitude, m." + Placeholder)]
        private float _blendAltitudeMeters = 15f;

        [SerializeField]
        [Tooltip("Blend width of the dump site field." + Placeholder)]
        private float _blendWaste = 0.08f;

        [SerializeField]
        [Tooltip(
            "How far a smooth noise moves the biome border, in weight share (0 to 0.5)."
                + Placeholder
        )]
        [Range(0f, 0.5f)]
        private float _borderJitter = 0.2f;

        [SerializeField]
        [Tooltip("Feature size of the border noise, m." + Placeholder)]
        private float _borderScaleMeters = 14f;

        [SerializeField]
        [Tooltip("Feature size of the alternate surface patches, m." + Placeholder)]
        private float _patchScaleMeters = 24f;

        [SerializeField]
        [Tooltip(
            "How much a fine noise frays the patch edges, in patch noise units." + Placeholder
        )]
        private float _patchEdge = 0.06f;

        [SerializeField]
        [Tooltip("Feature size of the patch edge noise, m." + Placeholder)]
        private float _patchEdgeScaleMeters = 1f;

        [Header("Subsurface")]
        [SerializeField]
        [Tooltip(
            "Soil, strata and pockets lie within this depth below the surface; Stone below, m."
                + Placeholder
        )]
        private float _subsurfaceDepthMeters = 8f;

        [SerializeField]
        [Tooltip("Feature size of the soil depth noise, m." + Placeholder)]
        private float _soilScaleMeters = 40f;

        [SerializeField]
        [Tooltip("Slope (voxel rise per voxel) at which soil reaches 0." + Placeholder)]
        private float _soilZeroSlope = 1.5f;

        [SerializeField]
        [Tooltip(
            "Surfaces up to this height above sea level take the biome's shore type, m."
                + Placeholder
        )]
        private float _shoreHeightMeters = 1.5f;

        [SerializeField]
        [Tooltip("Amplitude of the strata fold, m." + Placeholder)]
        private float _foldMeters = 60f;

        [SerializeField]
        [Tooltip("Feature size of the strata fold, m." + Placeholder)]
        private float _foldScaleMeters = 900f;

        [SerializeField]
        [Tooltip(
            "How far strata boundaries move, in thicknesses (0 to 0.9); layers vary from 1 - jitter to 1 + jitter thick."
                + Placeholder
        )]
        [Range(0f, 0.9f)]
        private float _strataJitter = 0.6f;

        [SerializeField]
        [Tooltip(
            "How much strata thin and thicken across the island (share of the thickness, 0 to 0.5), on the fold's scale."
                + Placeholder
        )]
        [Range(0f, 0.5f)]
        private float _strataThinning = 0.1f;

        [SerializeField]
        [Tooltip("Amplitude of the strata wobble, m." + Placeholder)]
        private float _wobbleMeters = 1.5f;

        [SerializeField]
        [Tooltip("Feature size of the strata wobble, m." + Placeholder)]
        private float _wobbleScaleMeters = 30f;

        [SerializeField]
        [Tooltip("Horizontal size of a waste pocket cell, m." + Placeholder)]
        private float _pocketCellMeters = 8f;

        [SerializeField]
        [Tooltip("Vertical size of a waste pocket cell, m." + Placeholder)]
        private float _pocketCellHeightMeters = 4f;

        [SerializeField]
        [Tooltip("Pocket radii (min, max), m; below half the cell size." + Placeholder)]
        private Vector2 _pocketRadiusMeters = new Vector2(0.5f, 1.75f);

        [Header("Types")]
        [SerializeField]
        private VoxelType _water;

        [SerializeField]
        [Tooltip("Deep rock below the subsurface band.")]
        private VoxelType _stone;

        [SerializeField]
        private VoxelType _snow;

        [SerializeField]
        [Tooltip("Materials of buried waste pockets.")]
        private VoxelType[] _pocketTypes = Array.Empty<VoxelType>();

        [SerializeField]
        private BiomeRegistry _biomes;

        [SerializeField]
        [Tooltip("Rivers and lakes (PLAN-05).")]
        private WaterSettings _waterSettings;

        public float SeaLevelMeters => _seaLevelMeters;

        public float IslandRadiusMeters => _islandRadiusMeters;

        public float EdgeRadiusMeters => _edgeRadiusMeters;

        public float CoastNoise => _coastNoise;

        public float CoastScaleMeters => _coastScaleMeters;

        public float WarpMeters => _warpMeters;

        public float WarpScaleMeters => _warpScaleMeters;

        public float LandRiseMeters => _landRiseMeters;

        public float SeaDepthMeters => _seaDepthMeters;

        public float ShelfWidth => _shelfWidth;

        public float MountainHeightMeters => _mountainHeightMeters;

        public float RidgeScaleMeters => _ridgeScaleMeters;

        public int RidgeOctaves => _ridgeOctaves;

        public float ReliefScaleMeters => _reliefScaleMeters;

        public float ReliefThreshold => _reliefThreshold;

        public float ReliefRamp => _reliefRamp;

        public float MassifShare => _massifShare;

        public float HillScaleMeters => _hillScaleMeters;

        public int HillOctaves => _hillOctaves;

        public float Gain => _gain;

        public float Lacunarity => _lacunarity;

        public float TerraceStepMeters => _terraceStepMeters;

        public float TerraceRiser => _terraceRiser;

        public float TemperatureMinC => _temperatureMinC;

        public float TemperatureMaxC => _temperatureMaxC;

        public float TemperatureScaleMeters => _temperatureScaleMeters;

        public float TemperatureGradient => _temperatureGradient;

        public float LapseRateCPerKm => _lapseRateCPerKm;

        public float SnowTemperatureC => _snowTemperatureC;

        public float SnowMaxSlope => _snowMaxSlope;

        public float MoistureScaleMeters => _moistureScaleMeters;

        public float CoastMoisture => _coastMoisture;

        public float WasteScaleMeters => _wasteScaleMeters;

        public float WasteThreshold => _wasteThreshold;

        public float BlendTemperatureC => _blendTemperatureC;

        public float BlendMoisture => _blendMoisture;

        public float BlendInland => _blendInland;

        public float BlendAltitudeMeters => _blendAltitudeMeters;

        public float BlendWaste => _blendWaste;

        public float BorderJitter => _borderJitter;

        public float BorderScaleMeters => _borderScaleMeters;

        public float PatchScaleMeters => _patchScaleMeters;

        public float PatchEdge => _patchEdge;

        public float PatchEdgeScaleMeters => _patchEdgeScaleMeters;

        public float SubsurfaceDepthMeters => _subsurfaceDepthMeters;

        public float SoilScaleMeters => _soilScaleMeters;

        public float SoilZeroSlope => _soilZeroSlope;

        public float ShoreHeightMeters => _shoreHeightMeters;

        public float FoldMeters => _foldMeters;

        public float FoldScaleMeters => _foldScaleMeters;

        public float StrataJitter => _strataJitter;

        public float StrataThinning => _strataThinning;

        public float WobbleMeters => _wobbleMeters;

        public float WobbleScaleMeters => _wobbleScaleMeters;

        public float PocketCellMeters => _pocketCellMeters;

        public float PocketCellHeightMeters => _pocketCellHeightMeters;

        public Vector2 PocketRadiusMeters => _pocketRadiusMeters;

        public VoxelType Water => _water;

        public VoxelType Stone => _stone;

        public VoxelType Snow => _snow;

        public VoxelType[] PocketTypes => _pocketTypes;

        public BiomeRegistry Biomes => _biomes;

        public WaterSettings WaterSettings => _waterSettings;

        public bool TryValidate(out string error)
        {
            const float worldHeight = WorldConstants.WorldHeightVoxels * WorldConstants.VoxelSize;
            const float islandHalfWidth =
                WorldConstants.IslandMaxChunkXZ
                * WorldConstants.ChunkSize
                * WorldConstants.VoxelSize;
            if (
                _water == null
                || _stone == null
                || _snow == null
                || _biomes == null
                || _waterSettings == null
            )
            {
                error =
                    "Water, stone, snow, the biome registry and the water settings are required.";
            }
            else if (
                _pocketTypes == null
                || _pocketTypes.Length == 0
                || _pocketTypes.Length > MaxPocketTypes
                || Array.IndexOf(_pocketTypes, null) >= 0
            )
            {
                error = $"Pocket types must list 1 to {MaxPocketTypes} types and no empty entry.";
            }
            else if (
                _seaLevelMeters <= _seaDepthMeters
                || _seaLevelMeters + _landRiseMeters + _mountainHeightMeters >= worldHeight
            )
            {
                error =
                    $"The sea floor must stay above 0 and sea level plus land rise plus mountains below {worldHeight} m.";
            }
            else if (
                _islandRadiusMeters <= 0f
                || _edgeRadiusMeters <= _islandRadiusMeters
                || _edgeRadiusMeters >= islandHalfWidth
            )
            {
                error =
                    $"Island radius must be positive and below the edge radius, itself below {islandHalfWidth} m.";
            }
            else if (_ridgeOctaves < 1 || _ridgeOctaves > 8 || _hillOctaves < 1 || _hillOctaves > 8)
            {
                error = "Octaves must be 1 to 8.";
            }
            else if (
                _coastScaleMeters <= 0f
                || _warpScaleMeters <= 0f
                || _ridgeScaleMeters <= 0f
                || _reliefScaleMeters <= 0f
                || _hillScaleMeters <= 0f
                || _temperatureScaleMeters <= 0f
                || _moistureScaleMeters <= 0f
                || _wasteScaleMeters <= 0f
                || _borderScaleMeters <= 0f
                || _patchScaleMeters <= 0f
                || _patchEdgeScaleMeters <= 0f
                || _patchEdge < 0f
                || _soilScaleMeters <= 0f
                || _foldScaleMeters <= 0f
                || _wobbleScaleMeters <= 0f
                || _gain <= 0f
                || _lacunarity <= 0f
            )
            {
                error = "Scales, gain and lacunarity must be positive.";
            }
            else if (
                _blendTemperatureC <= 0f
                || _blendMoisture <= 0f
                || _blendInland <= 0f
                || _blendAltitudeMeters <= 0f
                || _blendWaste <= 0f
            )
            {
                error = "Blend widths must be positive.";
            }
            else if (
                _temperatureMaxC < _temperatureMinC
                || _shelfWidth <= 0f
                || _terraceStepMeters <= 0f
                || _reliefRamp <= 0f
            )
            {
                error = "Temperature range, shelf width, terrace step or relief ramp is invalid.";
            }
            else if (_subsurfaceDepthMeters < 1f || _soilZeroSlope <= 0f || _shoreHeightMeters < 0f)
            {
                error =
                    "Subsurface depth must be at least 1 m, soil zero slope positive, shore height not negative.";
            }
            else if (
                _pocketCellMeters <= 0f
                || _pocketCellHeightMeters <= 0f
                || _pocketRadiusMeters.x <= 0f
                || _pocketRadiusMeters.y < _pocketRadiusMeters.x
                || _pocketRadiusMeters.y * 2f
                    >= Mathf.Min(_pocketCellMeters, _pocketCellHeightMeters)
            )
            {
                error = "Pocket radii must be positive and below half the pocket cell size.";
            }
            else if (!_biomes.TryValidate(_subsurfaceDepthMeters, out error))
            {
                error = $"{_biomes.name}: {error}";
            }
            else if (!_waterSettings.TryValidate(out error))
            {
                error = $"{_waterSettings.name}: {error}";
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
