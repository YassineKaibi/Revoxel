using Unity.Mathematics;

namespace Revoxel.World
{
    // Limits the world to a box of chunks (D-030). Min is inclusive, Max exclusive.
    // A streaming world later replaces this with a load radius around the player.
    public sealed class WorldBounds
    {
        public WorldBounds(ChunkCoord min, ChunkCoord max)
        {
            Min = min;
            Max = max;
        }

        public ChunkCoord Min { get; }

        public ChunkCoord Max { get; }

        public static WorldBounds Island() =>
            new WorldBounds(
                new ChunkCoord(0, 0, 0),
                new ChunkCoord(
                    WorldConstants.ChunksX,
                    WorldConstants.ChunksY,
                    WorldConstants.ChunksZ
                )
            );

        public bool Contains(ChunkCoord chunk) =>
            math.all(chunk.Value >= Min.Value) && math.all(chunk.Value < Max.Value);

        public bool ContainsBlock(int3 world) => Contains(Coords.ChunkOf(world));
    }
}
