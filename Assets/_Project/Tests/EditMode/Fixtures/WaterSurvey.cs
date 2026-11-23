using Revoxel.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    // Counts, over squares of columns, where generated water is held and where it is not
    // (PLAN-05 Task 2). A wet column (ground below its level) is held on each side when the
    // neighbor's top (its water level when wet, else its ground) is at least as high.
    // A lower wet neighbor is a step down the river (a rapid or a fall); a lower dry
    // neighbor is a wall of water standing over dry ground: a leak. Leaks lists where the
    // leaks of more than one voxel are. Burst, so the survey
    // samples what streaming generates.
    [BurstCompile(CompileSynchronously = true)]
    public struct WaterSurveyJob : IJob
    {
        public TerrainParams Params;

        // Square centers (x, z) and half-widths (w), voxels.
        [ReadOnly]
        public NativeArray<int3> Squares;

        public int Stride;

        // Wet columns checked, leaks, steps down to lower water, leaks more than one voxel
        // high (a one-voxel leak is a water edge a voxel above its bank, from rounding).
        public NativeReference<int4> Counts;

        // Up to Leaks.Length leak positions, for the failure message.
        public NativeArray<int2> Leaks;

        public void Execute()
        {
            var counts = int4.zero;
            for (var i = 0; i < Squares.Length; i++)
            {
                var square = Squares[i];
                for (var z = square.y - square.z; z <= square.y + square.z; z += Stride)
                for (var x = square.x - square.z; x <= square.x + square.z; x += Stride)
                {
                    var sample = TerrainGenerator.Sample(Params, x, z);
                    var ground = (int)math.floor(sample.Height);
                    if (ground >= sample.WaterLevel)
                    {
                        continue;
                    }

                    counts.x++;
                    Side(x + 1, z, sample.WaterLevel, ref counts);
                    Side(x - 1, z, sample.WaterLevel, ref counts);
                    Side(x, z + 1, sample.WaterLevel, ref counts);
                    Side(x, z - 1, sample.WaterLevel, ref counts);
                }
            }

            Counts.Value = counts;
        }

        private void Side(int x, int z, int level, ref int4 counts)
        {
            var sample = TerrainGenerator.Sample(Params, x, z);
            var ground = (int)math.floor(sample.Height);
            var wet = ground < sample.WaterLevel;
            var top = wet ? sample.WaterLevel : ground;
            if (top >= level)
            {
                return;
            }

            if (wet)
            {
                counts.z++;
                return;
            }

            counts.y++;
            if (level - top < 2)
            {
                return;
            }

            if (counts.w < Leaks.Length)
            {
                Leaks[counts.w] = new int2(x, z);
            }

            counts.w++;
        }
    }
}
