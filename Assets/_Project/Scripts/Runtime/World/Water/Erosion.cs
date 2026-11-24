using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Light erosion of the hydrology grid (D-079): per step, implicit stream power
    // (Braun and Willett 2013, m = 0.5, n = 1) along the flow of the depression-filled
    // surface, then diffusion of the cut (not of the surface: that would lower ridges and
    // refill valleys), clamped between the raw height minus MaxErosion and the raw height. Sea cells never change; land never drops to the sea surface.
    public static class Erosion
    {
        // Erodes height in place, from the raw heights it holds on entry.
        public static void Run(
            in TerrainParams p,
            NativeArray<bool> ocean,
            NativeArray<float> rain,
            NativeArray<float> height
        )
        {
            var count = HydroGrid.CellCount;
            var raw = new NativeArray<float>(height, Allocator.TempJob);
            var spare = new NativeArray<float>(count, Allocator.TempJob);
            var filled = new NativeArray<float>(count, Allocator.TempJob);
            var receiver = new NativeArray<int>(count, Allocator.TempJob);
            var order = new NativeArray<int>(count, Allocator.TempJob);
            var area = new NativeArray<float>(count, Allocator.TempJob);
            Schedule(
                    p,
                    ocean,
                    rain,
                    raw,
                    height,
                    spare,
                    filled,
                    receiver,
                    order,
                    area,
                    default,
                    out var inSpare
                )
                .Complete();
            if (inSpare)
            {
                height.CopyFrom(spare);
            }

            raw.Dispose();
            spare.Dispose();
            filled.Dispose();
            receiver.Dispose();
            order.Dispose();
            area.Dispose();
        }

        // Chains the steps after dependsOn, with no wait between them: each step erodes one
        // buffer and diffuses into the other. inSpare: the result ends in spare (an odd
        // number of steps), otherwise in height. raw is the land before erosion; filled,
        // receiver, order and area are scratch.
        public static JobHandle Schedule(
            in TerrainParams p,
            NativeArray<bool> ocean,
            NativeArray<float> rain,
            NativeArray<float> raw,
            NativeArray<float> height,
            NativeArray<float> spare,
            NativeArray<float> filled,
            NativeArray<int> receiver,
            NativeArray<int> order,
            NativeArray<float> area,
            JobHandle dependsOn,
            out bool inSpare
        )
        {
            var current = height;
            var other = spare;
            var handle = dependsOn;
            for (var step = 0; step < p.ErosionSteps; step++)
            {
                handle = new ErosionStepJob
                {
                    Strength = p.ErosionStrength,
                    ChannelArea = p.ErosionChannelAreaKm2,
                    Height = current,
                    Ocean = ocean,
                    Rain = rain,
                    Filled = filled,
                    Receiver = receiver,
                    Order = order,
                    Area = area,
                }.Schedule(handle);
                handle = new DiffusionJob
                {
                    Diffusion = p.Diffusion,
                    Floor = p.SeaLevel + 1f,
                    MaxErosion = p.MaxErosion,
                    Height = current,
                    Raw = raw,
                    Ocean = ocean,
                    Output = other,
                }.Schedule(HydroGrid.CellCount, HydroSampleJob.BatchSize, handle);
                (current, other) = (other, current);
            }

            inSpare = p.ErosionSteps % 2 == 1;
            return handle;
        }

        // Lowers every land cell toward its receiver, downstream cells first, so each uses
        // its receiver's new height (the implicit scheme, stable at any strength). Cells in
        // pits (filled above their own height) are not eroded.
        public static void StreamPower(
            float strength,
            float channelArea,
            NativeArray<float> height,
            NativeArray<bool> ocean,
            NativeArray<float> filled,
            NativeArray<int> receiver,
            NativeArray<int> order,
            NativeArray<float> area
        )
        {
            for (var k = order.Length - 1; k >= 0; k--)
            {
                var i = order[k];
                var r = receiver[i];
                if (r < 0 || ocean[i] || filled[i] > height[i] + DepressionFill.Epsilon * 0.5f)
                {
                    continue;
                }

                var distance = math.length((float2)(HydroGrid.Cell(i) - HydroGrid.Cell(r)));
                // Only water above the channel threshold cuts: hillslopes are left to the
                // spread of the cut.
                var f = strength * math.sqrt(math.max(0f, area[i] - channelArea)) / distance;
                height[i] = math.min(height[i], (height[i] + f * height[r]) / (1f + f));
            }
        }

        // One cell after diffusion of the cut (raw minus current height): the cut spreads to
        // its neighbors by the 4-neighbor Laplacian times the rate, so valleys widen into
        // soft sides while ground no water cut keeps its shape (cells beyond the grid and sea
        // cells count as uncut). Clamped to the maximum erosion and the floor.
        public static float Diffuse(
            int index,
            float rate,
            float floor,
            float maxErosion,
            NativeArray<float> height,
            NativeArray<float> raw,
            NativeArray<bool> ocean
        )
        {
            if (ocean[index])
            {
                return height[index];
            }

            var c = HydroGrid.Cell(index);
            var cut = raw[index] - height[index];
            var sum = 0f;
            for (var k = 0; k < 4; k++)
            {
                var n = c + DepressionFill.Neighbor(k);
                if (HydroGrid.InGrid(n))
                {
                    var j = HydroGrid.Index(n);
                    sum += ocean[j] ? 0f : raw[j] - height[j];
                }
            }

            var spread = math.clamp(cut + rate * (sum - 4f * cut), 0f, maxErosion);
            return math.max(raw[index] - spread, math.min(raw[index], floor));
        }
    }

    // The sequential part of an erosion step: fill, route, accumulate, stream power.
    [BurstCompile(CompileSynchronously = true)]
    public struct ErosionStepJob : IJob
    {
        public float Strength;
        public float ChannelArea;
        public NativeArray<float> Height;

        [ReadOnly]
        public NativeArray<bool> Ocean;

        [ReadOnly]
        public NativeArray<float> Rain;

        public NativeArray<float> Filled;
        public NativeArray<int> Receiver;
        public NativeArray<int> Order;
        public NativeArray<float> Area;

        public void Execute()
        {
            DepressionFill.Fill(Height, Ocean, DepressionFill.Epsilon, Filled);
            FlowRouting.Receivers(Filled, Ocean, Receiver);
            FlowRouting.Order(Filled, Order);
            FlowRouting.Accumulate(Order, Receiver, Rain, Area);
            Erosion.StreamPower(
                Strength,
                ChannelArea,
                Height,
                Ocean,
                Filled,
                Receiver,
                Order,
                Area
            );
        }
    }

    // Hillslope diffusion of every cell, in parallel, into a second buffer.
    [BurstCompile(CompileSynchronously = true)]
    public struct DiffusionJob : IJobParallelFor
    {
        public float Diffusion;
        public float Floor;
        public float MaxErosion;

        [ReadOnly]
        public NativeArray<float> Height;

        [ReadOnly]
        public NativeArray<float> Raw;

        [ReadOnly]
        public NativeArray<bool> Ocean;

        [WriteOnly]
        public NativeArray<float> Output;

        public void Execute(int index)
        {
            Output[index] = Erosion.Diffuse(
                index,
                Diffusion,
                Floor,
                MaxErosion,
                Height,
                Raw,
                Ocean
            );
        }
    }
}
