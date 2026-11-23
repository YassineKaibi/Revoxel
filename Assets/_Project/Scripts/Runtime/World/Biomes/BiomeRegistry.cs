using System;
using UnityEngine;

namespace Revoxel.World
{
    // Every biome of the island, looked up by id (D-062). Ids are dense from 0, so a biome's
    // id is its index in TerrainParams.Biomes.
    [CreateAssetMenu(menuName = "Revoxel/World/Biome Registry", fileName = "BiomeRegistry")]
    public sealed class BiomeRegistry : ScriptableObject
    {
        // TerrainParams packs the biomes into a fixed-size list for Burst.
        public const int MaxBiomes = 16;

        [SerializeField]
        private Biome[] _biomes = Array.Empty<Biome>();

        // In id order once validated.
        public Biome[] Biomes => _biomes;

        public int Count => _biomes.Length;

        public bool TryValidate(float subsurfaceDepthMeters, out string error)
        {
            if (_biomes == null || _biomes.Length == 0 || _biomes.Length > MaxBiomes)
            {
                error = $"The registry must list 1 to {MaxBiomes} biomes.";
                return false;
            }

            for (var i = 0; i < _biomes.Length; i++)
            {
                var biome = _biomes[i];
                if (biome == null)
                {
                    error = $"Entry {i} is empty.";
                    return false;
                }

                if (biome.Id.Value != i)
                {
                    error =
                        $"{biome.name}: id {biome.Id.Value} is not its index {i}; ids must be dense from 0.";
                    return false;
                }

                for (var j = 0; j < i; j++)
                {
                    if (_biomes[j].NameKey == biome.NameKey)
                    {
                        error = $"{biome.name}: name key {biome.NameKey} is used twice.";
                        return false;
                    }
                }

                if (!biome.TryValidate(subsurfaceDepthMeters, out error))
                {
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        public bool TryGet(BiomeId id, out Biome biome)
        {
            if (_biomes != null && id.Value < _biomes.Length && _biomes[id.Value] != null)
            {
                biome = _biomes[id.Value];
                return true;
            }

            biome = null;
            return false;
        }

        internal void SetBiomes(Biome[] biomes) => _biomes = biomes;
    }
}
