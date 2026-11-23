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
            else
            {
                error = string.Empty;
                return true;
            }

            return false;
        }
    }
}
