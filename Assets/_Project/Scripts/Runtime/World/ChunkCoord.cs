using System;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Integer position of a chunk, in chunk units.
    public readonly struct ChunkCoord : IEquatable<ChunkCoord>
    {
        public readonly int3 Value;

        public ChunkCoord(int3 value)
        {
            Value = value;
        }

        public ChunkCoord(int x, int y, int z)
        {
            Value = new int3(x, y, z);
        }

        public ChunkCoord Offset(int3 delta) => new ChunkCoord(Value + delta);

        public bool Equals(ChunkCoord other) => Value.Equals(other.Value);

        public override bool Equals(object obj) => obj is ChunkCoord other && Equals(other);

        public override int GetHashCode() => (int)math.hash(Value);

        public override string ToString() => $"({Value.x}, {Value.y}, {Value.z})";

        public static bool operator ==(ChunkCoord a, ChunkCoord b) => a.Equals(b);

        public static bool operator !=(ChunkCoord a, ChunkCoord b) => !a.Equals(b);
    }
}
