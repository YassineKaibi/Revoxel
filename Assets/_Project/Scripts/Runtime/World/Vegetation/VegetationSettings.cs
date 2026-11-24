using System;
using UnityEngine;

namespace Revoxel.World
{
    // Trees and ground plants (PLAN-06, D-083 to D-088): the species list, the tree
    // candidate grid and how trees are voxelized. Biomes say which species and plants grow
    // where. Read by TerrainParams.Create. Every value is a placeholder (Q-12); lengths in
    // meters.
    [CreateAssetMenu(
        menuName = "Revoxel/World/Vegetation Settings",
        fileName = "Placeholder_VegetationSettings"
    )]
    public sealed class VegetationSettings : ScriptableObject
    {
        // Species ids are indices in the list; packed for Burst.
        public const int MaxSpecies = 32;

        // Tallest ground plant; streaming classifies chunks with it (D-087).
        public const float MaxPlantHeightMeters = 3f;

        private const string Placeholder = " Placeholder value (Q-12).";

        [SerializeField]
        [Tooltip("Every species; biomes pick from these.")]
        private TreeSpecies[] _species = Array.Empty<TreeSpecies>();

        [SerializeField]
        [Tooltip(
            "Side of a tree candidate cell: at most one tree stands in each (D-084), m."
                + Placeholder
        )]
        private float _cellMeters = 2f;

        [SerializeField]
        [Tooltip(
            "Thinnest wood radius, voxels; 0.85 keeps every branch face-connected (D-083)."
                + Placeholder
        )]
        private float _minWoodRadiusVoxels = 0.85f;

        [SerializeField]
        [Tooltip("Bark layer over the wood core, voxels." + Placeholder)]
        private float _barkVoxels = 1.25f;

        [SerializeField]
        [Tooltip("Feature size of the leaf cluster noise (lumps and holes), m." + Placeholder)]
        private float _leafNoiseMeters = 0.6f;

        [SerializeField]
        [Tooltip(
            "How much the noise moves a leaf cluster's edge, share of its radius." + Placeholder
        )]
        [Range(0f, 0.8f)]
        private float _leafLumps = 0.3f;

        [SerializeField]
        [Tooltip(
            "Coarsest distance detail level that draws trees, 1 to 6 (D-088); 0 for none."
                + Placeholder
        )]
        [Range(0, 6)]
        private int _detailMaxLevel = 5;

        [SerializeField]
        [Tooltip(
            "Voxel memory trees and plants may add in the full-detail square, MiB (D-052). Their work runs in the generation jobs, inside the streaming budget."
                + Placeholder
        )]
        private int _memoryBudgetMiB = 160;

        public TreeSpecies[] Species => _species;

        public float CellMeters => _cellMeters;

        public float MinWoodRadiusVoxels => _minWoodRadiusVoxels;

        public float BarkVoxels => _barkVoxels;

        public float LeafNoiseMeters => _leafNoiseMeters;

        public float LeafLumps => _leafLumps;

        public int DetailMaxLevel => _detailMaxLevel;

        public int MemoryBudgetMiB => _memoryBudgetMiB;

        public int IndexOf(TreeSpecies species) => Array.IndexOf(_species, species);

        public bool TryValidate(out string error)
        {
            if (
                _species == null
                || _species.Length == 0
                || _species.Length > MaxSpecies
                || Array.IndexOf(_species, null) >= 0
            )
            {
                error = $"Species must list 1 to {MaxSpecies} species and no empty entry.";
                return false;
            }

            foreach (var species in _species)
            {
                if (!species.TryValidate(out error))
                {
                    return false;
                }
            }

            if (_cellMeters < 0.5f || _minWoodRadiusVoxels < 0.5f || _barkVoxels < 0f)
            {
                error =
                    "The candidate cell must be at least 0.5 m, the thinnest wood half a voxel, the bark not negative.";
            }
            else if (_leafNoiseMeters <= 0f || _memoryBudgetMiB < 1)
            {
                error = "Leaf noise size and memory budget must be positive.";
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
