using System;

namespace Revoxel.World
{
    // One 0.125 m cell of the grid (D-050). 0 is Air.
    public readonly struct VoxelId : IEquatable<VoxelId>
    {
        public static readonly VoxelId Air = new VoxelId(0);

        public readonly ushort Value;

        public VoxelId(ushort value)
        {
            Value = value;
        }

        public bool IsAir => Value == 0;

        public bool Equals(VoxelId other) => Value == other.Value;

        public override bool Equals(object obj) => obj is VoxelId other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString();

        public static bool operator ==(VoxelId a, VoxelId b) => a.Value == b.Value;

        public static bool operator !=(VoxelId a, VoxelId b) => a.Value != b.Value;
    }
}
