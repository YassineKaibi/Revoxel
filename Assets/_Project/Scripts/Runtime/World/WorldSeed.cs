using System;

namespace Revoxel.World
{
    // Integer that fully determines generation.
    public readonly struct WorldSeed : IEquatable<WorldSeed>
    {
        public readonly int Value;

        public WorldSeed(int value)
        {
            Value = value;
        }

        public bool Equals(WorldSeed other) => Value == other.Value;

        public override bool Equals(object obj) => obj is WorldSeed other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString();
    }
}
