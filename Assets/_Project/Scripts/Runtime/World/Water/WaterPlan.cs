using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // The island's water, computed once per seed (D-077, D-078): persistent arrays held in
    // TerrainParams and read by every generation job. Never written after the build.
    // TerrainParams.Create allocates them and only the creator disposes them; copies of
    // TerrainParams share them.
    public struct WaterPlan
    {
        [ReadOnly]
        public NativeArray<RiverNode> Nodes;

        [ReadOnly]
        public NativeArray<River> Rivers;

        [ReadOnly]
        public NativeArray<Lake> Lakes;

        // Shaped minus raw land per hydrology cell, voxels (D-079): erosion, and small
        // depressions filled flat.
        [ReadOnly]
        public NativeArray<float> Delta;

        // The lake of each hydrology cell: 0 for none, lake index + 1 (D-081).
        [ReadOnly]
        public NativeArray<ushort> LakeOf;

        // The river segments near each hydrology cell (D-080): the runs of cell i are
        // CellRuns[CellStart[i]] to CellRuns[CellStart[i + 1] - 1], each (river, first node,
        // last node) of consecutive segments, river by river.
        [ReadOnly]
        public NativeArray<int> CellStart;

        [ReadOnly]
        public NativeArray<int3> CellRuns;

        public bool IsCreated => Nodes.IsCreated;

        // The change to the land at a voxel position: Catmull-Rom bicubic over the 4 x 4
        // nearest cell centers (continuous, with a continuous slope); 0 without a plan.
        public float DeltaAt(float2 voxel)
        {
            if (Delta.Length == 0)
            {
                return 0f;
            }

            var u = (voxel - HydroGrid.OriginVoxel) / HydroGrid.CellVoxels - 0.5f;
            var cell = (int2)math.floor(u);
            var t = u - cell;
            var wx = Weights(t.x);
            var wz = Weights(t.y);
            var sum = 0f;
            for (var j = 0; j < 4; j++)
            {
                var row = 0f;
                for (var i = 0; i < 4; i++)
                {
                    var c = math.clamp(cell + new int2(i - 1, j - 1), 0, HydroGrid.Size - 1);
                    row += wx[i] * Delta[HydroGrid.Index(c)];
                }

                sum += wz[j] * row;
            }

            return sum;
        }

        private static float4 Weights(float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f
                * new float4(
                    -t + 2f * t2 - t3,
                    2f - 5f * t2 + 3f * t3,
                    t + 4f * t2 - 3f * t3,
                    -t2 + t3
                );
        }

        public void Dispose()
        {
            if (Nodes.IsCreated)
            {
                Nodes.Dispose();
            }

            if (Rivers.IsCreated)
            {
                Rivers.Dispose();
            }

            if (Lakes.IsCreated)
            {
                Lakes.Dispose();
            }

            if (Delta.IsCreated)
            {
                Delta.Dispose();
            }

            if (LakeOf.IsCreated)
            {
                LakeOf.Dispose();
            }

            if (CellStart.IsCreated)
            {
                CellStart.Dispose();
            }

            if (CellRuns.IsCreated)
            {
                CellRuns.Dispose();
            }

            this = default;
        }
    }
}
