using Unity.Mathematics;

namespace Revoxel.World
{
    // Limits the world to a box of chunks (D-030, D-050). Min is inclusive, Max exclusive.
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
                new ChunkCoord(WorldConstants.IslandMinChunkXZ, 0, WorldConstants.IslandMinChunkXZ),
                new ChunkCoord(
                    WorldConstants.IslandMaxChunkXZ,
                    WorldConstants.ChunksY,
                    WorldConstants.IslandMaxChunkXZ
                )
            );

        public bool Contains(ChunkCoord chunk) =>
            math.all(chunk.Value >= Min.Value) && math.all(chunk.Value < Max.Value);

        public bool ContainsVoxel(int3 voxel) => Contains(Coords.ChunkOf(voxel));
    }
}
