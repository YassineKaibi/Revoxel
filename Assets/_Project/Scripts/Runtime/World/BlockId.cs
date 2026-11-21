using System;

namespace Revoxel.World
{
    // One cell of the voxel grid. 0 is Air.
    public readonly struct BlockId : IEquatable<BlockId>
    {
        public static readonly BlockId Air = new BlockId(0);

        public readonly byte Value;

        public BlockId(byte value)
        {
            Value = value;
        }

        public bool IsAir => Value == 0;

        public bool Equals(BlockId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is BlockId other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString();

        public static bool operator ==(BlockId a, BlockId b) => a.Value == b.Value;

        public static bool operator !=(BlockId a, BlockId b) => a.Value != b.Value;
    }
}
