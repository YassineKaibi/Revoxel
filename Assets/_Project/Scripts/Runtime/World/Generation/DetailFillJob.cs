using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Revoxel.World
{
    // Fills the padded meshing input of one coarse chunk from the generator (PLAN-03
    // Task 2); a GreedyMeshJob depending on it meshes the result.
    [BurstCompile(CompileSynchronously = true)]
    public struct DetailFillJob : IJob
    {
        public TerrainParams Params;
        public int Level;
        public ChunkCoord Coord;
        public bool SideWalls;

        public NativeArray<float> Heights;

        [WriteOnly]
        public NativeArray<ushort> Padded;

        public void Execute()
        {
            TerrainGenerator.FillDetailPadded(Params, Level, Coord, SideWalls, Heights, Padded);
        }
    }
}
