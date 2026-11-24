using System;
using UnityEngine;

namespace Revoxel.World
{
    // A ground plant of a biome (D-087): one-voxel-wide stems standing on the soil in
    // clumps, with an optional head voxel on top (a flower). Every number is a placeholder
    // (Q-12); lengths in meters.
    [Serializable]
    public sealed class BiomePlant
    {
        private const string Placeholder = " Placeholder value (Q-12).";

        [SerializeField]
        private VoxelType _stem;

        [SerializeField]
        [Tooltip("Top voxel of the stem (a flower head); empty for none.")]
        private VoxelType _head;

        [SerializeField]
        [Tooltip(
            "Share of the columns it grows on where its clumps are thickest, 0 to 1." + Placeholder
        )]
        [Range(0f, 1f)]
        private float _cover = 0.1f;

        [SerializeField]
        [Tooltip("Size of its clumps, m." + Placeholder)]
        private float _clumpMeters = 6f;

        [SerializeField]
        [Tooltip("Height of a stem (min, max), m." + Placeholder)]
        private Vector2 _heightMeters = new Vector2(0.125f, 0.375f);

        [SerializeField]
        [Tooltip(
            "Deepest water it stands in, m; 0 for dry ground only (reeds stand in water)."
                + Placeholder
        )]
        private float _maxWaterDepthMeters;

        [SerializeField]
        [Tooltip("Grows on shore sand.")]
        private bool _onShore;

        [SerializeField]
        [Tooltip(
            "Chance that a stem three voxels or taller bends over into a neighboring column, 0 to 1 (D-089)."
                + Placeholder
        )]
        [Range(0f, 1f)]
        private float _bend;

        public VoxelType Stem => _stem;

        public VoxelType Head => _head;

        public float Cover => _cover;

        public float ClumpMeters => _clumpMeters;

        public Vector2 HeightMeters => _heightMeters;

        public float MaxWaterDepthMeters => _maxWaterDepthMeters;

        public bool OnShore => _onShore;

        public float Bend => _bend;

        public bool TryValidate(out string error)
        {
            if (_stem == null)
            {
                error = "a ground plant needs a stem type.";
            }
            else if (_stem.IsSolid || (_head != null && _head.IsSolid))
            {
                error = $"ground plants are not solid (D-091): {_stem.name} or its head is.";
            }
            else if (
                _clumpMeters <= 0f
                || _heightMeters.x < WorldConstants.VoxelSize
                || _heightMeters.y < _heightMeters.x
                || _heightMeters.y > VegetationSettings.MaxPlantHeightMeters
                || _maxWaterDepthMeters < 0f
            )
            {
                error =
                    $"a ground plant needs a positive clump size, a height from a voxel to {VegetationSettings.MaxPlantHeightMeters} m and no negative water depth.";
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
