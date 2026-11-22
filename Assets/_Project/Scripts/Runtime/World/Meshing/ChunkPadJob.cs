using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Revoxel.World
{
    // Copies a chunk and one layer of each of its 26 neighbors (faces, edges and corners,
    // which occlusion needs) into the 34 x 34 x 34 meshing input. Runs on the main thread
    // with Run(), so the mesh jobs never read live chunk storage.
    [BurstCompile]
    public struct ChunkPadJob : IJob
    {
        // 27 entries, index (dx + 1) + 3 * ((dz + 1) + 3 * (dy + 1)); missing neighbors
        // are a uniform Air instance.
        [ReadOnly]
        public NativeArray<ChunkVoxels> Neighborhood;

        [WriteOnly]
        public NativeArray<ushort> Padded;

        public void Execute()
        {
            const int size = WorldConstants.ChunkSize;
            for (var y = -1; y <= size; y++)
            {
                var dy =
                    y < 0 ? -1
                    : y >= size ? 1
                    : 0;
                var ly = y - dy * size;
                for (var z = -1; z <= size; z++)
                {
                    var dz =
                        z < 0 ? -1
                        : z >= size ? 1
                        : 0;
                    var lz = z - dz * size;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var source = Neighborhood[(dx + 1) + 3 * ((dz + 1) + 3 * (dy + 1))];
                        var xMin =
                            dx < 0 ? -1
                            : dx == 0 ? 0
                            : size;
                        var xMax =
                            dx < 0 ? -1
                            : dx == 0 ? size - 1
                            : size;
                        if (source.IsUniform)
                        {
                            var id = source.UniformId.Value;
                            for (var x = xMin; x <= xMax; x++)
                            {
                                Padded[Coords.PaddedIndex(x, y, z)] = id;
                            }

                            continue;
                        }

                        for (var x = xMin; x <= xMax; x++)
                        {
                            var lx = x - dx * size;
                            Padded[Coords.PaddedIndex(x, y, z)] = source
                                .Get(Coords.LocalIndex(lx, ly, lz))
                                .Value;
                        }
                    }
                }
            }
        }
    }
}
