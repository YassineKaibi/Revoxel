using System;

namespace Revoxel.World
{
    // Index of a biome in the BiomeRegistry, dense from 0 (D-062).
    public readonly struct BiomeId : IEquatable<BiomeId>
    {
        public readonly byte Value;

        public BiomeId(byte value)
        {
            Value = value;
        }

        public bool Equals(BiomeId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is BiomeId other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString();

        public static bool operator ==(BiomeId a, BiomeId b) => a.Value == b.Value;

        public static bool operator !=(BiomeId a, BiomeId b) => a.Value != b.Value;
    }
}
