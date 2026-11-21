using UnityEngine;

namespace Revoxel.World
{
    // Every numeric generation parameter (PLAN-02 "Generation"). All values are placeholders
    // until Q-12 settles them; the asset is named Placeholder_WorldGenSettings.
    [CreateAssetMenu(
        menuName = "Revoxel/World/World Gen Settings",
        fileName = "Placeholder_WorldGenSettings"
    )]
    public sealed class WorldGenSettings : ScriptableObject
    {
        private const string Placeholder = "Placeholder value (Q-12). ";

        [Header("Blocks")]
        [SerializeField]
        private BlockType _water;

        [SerializeField]
        private BlockType _sand;

        [SerializeField]
        private BlockType _dirt;

        [SerializeField]
        private BlockType _grass;

        [SerializeField]
        private BlockType _stone;

        [SerializeField]
        private BlockType _log;

        [SerializeField]
        private BlockType _leaves;

        [SerializeField]
        private BlockType _compactedWaste;

        [Header("Island shape")]
        [SerializeField]
        [Tooltip(Placeholder + "Distance from the center to the shore, in blocks.")]
        private float _islandRadius = 104f;

        [SerializeField]
        [Tooltip(Placeholder + "Frequency of the coastline noise, per block.")]
        private float _coastNoiseScale = 0.02f;

        [SerializeField]
        [Tooltip(Placeholder + "Coastline noise strength, as a fraction of the radius.")]
        private float _coastNoiseAmplitude = 0.15f;

        [SerializeField]
        [Tooltip(Placeholder + "Water fills every block below this height.")]
        private int _seaLevel = 20;

        [SerializeField]
        [Tooltip(Placeholder + "Height gained from the shore to the center, in blocks.")]
        private float _landHeight = 14f;

        [SerializeField]
        [Tooltip(Placeholder + "Frequency of the hill noise, per block.")]
        private float _hillNoiseScale = 0.04f;

        [SerializeField]
        [Tooltip(Placeholder + "Hill noise strength, in blocks.")]
        private float _hillNoiseAmplitude = 4f;

        [SerializeField]
        [Tooltip(Placeholder + "Deepest sea floor, in blocks below sea level.")]
        private int _seaFloorDepth = 6;

        [SerializeField]
        [Tooltip(Placeholder + "Blocks of surface material above the stone.")]
        private int _stoneDepth = 4;

        [Header("Zones (distance from the center, in blocks)")]
        [SerializeField]
        [Tooltip(Placeholder + "Décharge inside this radius.")]
        private float _dechargeRadius = 32f;

        [SerializeField]
        [Tooltip(Placeholder + "Forêt inside this radius, Plage beyond it up to the shore.")]
        private float _foretRadius = 80f;

        [Header("Trees (Forêt only)")]
        [SerializeField]
        [Tooltip(Placeholder + "Chance that a Forêt column grows a tree, 0 to 1.")]
        private float _treeDensity = 0.02f;

        [SerializeField]
        [Tooltip(Placeholder + "Trunk height in blocks.")]
        private int _trunkHeight = 4;

        [SerializeField]
        [Tooltip(Placeholder + "Horizontal radius of the leaves in blocks.")]
        private int _leafRadius = 2;

        [Header("Runtime")]
        [SerializeField]
        [Tooltip(Placeholder + "Dirty chunks remeshed per frame (PLAN-02 \"Edits\").")]
        private int _remeshBudgetPerFrame = 4;

        public BlockType Water => _water;
        public BlockType Sand => _sand;
        public BlockType Dirt => _dirt;
        public BlockType Grass => _grass;
        public BlockType Stone => _stone;
        public BlockType Log => _log;
        public BlockType Leaves => _leaves;
        public BlockType CompactedWaste => _compactedWaste;
        public float IslandRadius => _islandRadius;
        public float CoastNoiseScale => _coastNoiseScale;
        public float CoastNoiseAmplitude => _coastNoiseAmplitude;
        public int SeaLevel => _seaLevel;
        public float LandHeight => _landHeight;
        public float HillNoiseScale => _hillNoiseScale;
        public float HillNoiseAmplitude => _hillNoiseAmplitude;
        public int SeaFloorDepth => _seaFloorDepth;
        public int StoneDepth => _stoneDepth;
        public float DechargeRadius => _dechargeRadius;
        public float ForetRadius => _foretRadius;
        public float TreeDensity => _treeDensity;
        public int TrunkHeight => _trunkHeight;
        public int LeafRadius => _leafRadius;
        public int RemeshBudgetPerFrame => _remeshBudgetPerFrame;

        public bool TryValidate(out string error)
        {
            if (
                _water == null
                || _sand == null
                || _dirt == null
                || _grass == null
                || _stone == null
                || _log == null
                || _leaves == null
                || _compactedWaste == null
            )
            {
                error = "a block reference is empty";
                return false;
            }

            if (_seaLevel <= _seaFloorDepth || _seaLevel >= WorldConstants.WorldHeight)
            {
                error = "sea level must be above the sea floor depth and below the world height";
                return false;
            }

            if (
                !(
                    0f < _dechargeRadius
                    && _dechargeRadius < _foretRadius
                    && _foretRadius < _islandRadius
                )
            )
            {
                error = "radii must grow: 0 < Décharge < Forêt < island";
                return false;
            }

            if (_islandRadius * (1f + _coastNoiseAmplitude) > WorldConstants.WorldSizeX * 0.5f)
            {
                error = "the island with its coast noise does not fit in the world";
                return false;
            }

            if (_treeDensity < 0f || _treeDensity > 1f || _trunkHeight < 1 || _leafRadius < 0)
            {
                error = "tree density must be 0 to 1, trunk at least 1, leaf radius at least 0";
                return false;
            }

            if (_leafRadius >= WorldConstants.ChunkSize)
            {
                error = "leaf radius must be smaller than a chunk";
                return false;
            }

            if (_stoneDepth < 1 || _remeshBudgetPerFrame < 1)
            {
                error = "stone depth and remesh budget must be at least 1";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void OnValidate()
        {
            if (!TryValidate(out var error))
            {
                Debug.LogWarning($"[World] World gen settings '{name}' are invalid: {error}", this);
            }
        }
    }
}
