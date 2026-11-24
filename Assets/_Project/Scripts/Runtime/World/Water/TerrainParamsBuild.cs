using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // A water plan being built in the background (D-077): the parallel grid sample, the
    // erosion steps and the hydrology build chained as jobs, with no main-thread wait
    // between them. Complete waits for them, hands the plan to the parameters and frees the
    // rest; Dispose waits and frees everything when the build is abandoned. Arrays are
    // persistent: a build may run for more frames than temporary job memory allows.
    public sealed class TerrainParamsBuild : IDisposable
    {
        private const Allocator Memory = Allocator.Persistent;

        private TerrainParams _params;
        private JobHandle _handle;
        private bool _pending;
        private bool _water;

        private NativeArray<float> _land;
        private NativeArray<float> _rain;
        private NativeArray<bool> _ocean;
        private NativeArray<float> _eroded;
        private NativeArray<float> _spare;
        private NativeArray<float> _filled;
        private NativeArray<int> _basin;
        private NativeArray<float> _final;
        private NativeArray<int> _receiver;
        private NativeArray<int> _order;
        private NativeArray<float> _area;
        private NativeArray<float> _delta;
        private NativeArray<ushort> _lakeOf;
        private NativeList<Lake> _lakes;
        private NativeList<RiverNode> _nodes;
        private NativeList<River> _rivers;
        private NativeList<int> _cellStart;
        private NativeList<int3> _cellRuns;

        internal TerrainParamsBuild(TerrainParams parameters, bool water)
        {
            _params = parameters;
            _water = water;
            _pending = true;

            // The job system checks every container, the plan's own included: the build
            // reads an empty plan.
            _params.WaterPlan = TerrainParams.EmptyPlan(Memory);
            if (!water)
            {
                return;
            }

            var count = HydroGrid.CellCount;
            _land = new NativeArray<float>(count, Memory);
            _rain = new NativeArray<float>(count, Memory);
            _ocean = new NativeArray<bool>(count, Memory);
            _eroded = new NativeArray<float>(count, Memory);
            _spare = new NativeArray<float>(count, Memory);
            _filled = new NativeArray<float>(count, Memory);
            _basin = new NativeArray<int>(count, Memory);
            _final = new NativeArray<float>(count, Memory);
            _receiver = new NativeArray<int>(count, Memory);
            _order = new NativeArray<int>(count, Memory);
            _area = new NativeArray<float>(count, Memory);
            _delta = new NativeArray<float>(count, Memory);
            _lakeOf = new NativeArray<ushort>(count, Memory);
            _lakes = new NativeList<Lake>(64, Memory);
            _nodes = new NativeList<RiverNode>(4096, Memory);
            _rivers = new NativeList<River>(64, Memory);
            _cellStart = new NativeList<int>(count + 1, Memory);
            _cellRuns = new NativeList<int3>(4096, Memory);

            var handle = new HydroSampleJob
            {
                Params = _params,
                Land = _land,
                Rain = _rain,
                Ocean = _ocean,
            }.Schedule(count, HydroSampleJob.BatchSize);
            handle = new CopyJob { From = _land, To = _eroded }.Schedule(handle);
            handle = Erosion.Schedule(
                _params,
                _ocean,
                _rain,
                _land,
                _eroded,
                _spare,
                _filled,
                _receiver,
                _order,
                _area,
                handle,
                out var inSpare
            );
            _handle = new HydrologyBuildJob
            {
                Params = _params,
                Land = _land,
                Rain = _rain,
                Ocean = _ocean,
                Eroded = inSpare ? _spare : _eroded,
                Filled = _filled,
                Basin = _basin,
                Final = _final,
                Receiver = _receiver,
                Order = _order,
                Area = _area,
                Delta = _delta,
                LakeOf = _lakeOf,
                Lakes = _lakes,
                Nodes = _nodes,
                Rivers = _rivers,
                CellStart = _cellStart,
                CellRuns = _cellRuns,
            }.Schedule(handle);
            JobHandle.ScheduleBatchedJobs();
        }

        public bool IsCompleted => !_pending || _handle.IsCompleted;

        // Waits for the build and returns the parameters with their plan; the caller owns
        // them (TerrainParams.Dispose). Call once.
        public TerrainParams Complete()
        {
            if (!_pending)
            {
                throw new InvalidOperationException("The build was already completed or disposed.");
            }

            _handle.Complete();
            _pending = false;
            var p = _params;
            if (_water)
            {
                p.WaterPlan.Dispose();
                p.WaterPlan = new WaterPlan
                {
                    Nodes = _nodes.ToArray(Allocator.Persistent),
                    Rivers = _rivers.ToArray(Allocator.Persistent),
                    Lakes = _lakes.ToArray(Allocator.Persistent),
                    Delta = _delta,
                    LakeOf = _lakeOf,
                    CellStart = _cellStart.ToArray(Allocator.Persistent),
                    CellRuns = _cellRuns.ToArray(Allocator.Persistent),
                };
                _delta = default;
                _lakeOf = default;
                FreeBuffers();
            }

            _params = default;
            return p;
        }

        // Waits for an unfinished build and frees all of it.
        public void Dispose()
        {
            if (!_pending)
            {
                return;
            }

            _handle.Complete();
            _pending = false;
            _params.WaterPlan.Dispose();
            _params = default;
            if (_water)
            {
                _delta.Dispose();
                _lakeOf.Dispose();
                FreeBuffers();
            }
        }

        private void FreeBuffers()
        {
            _land.Dispose();
            _rain.Dispose();
            _ocean.Dispose();
            _eroded.Dispose();
            _spare.Dispose();
            _filled.Dispose();
            _basin.Dispose();
            _final.Dispose();
            _receiver.Dispose();
            _order.Dispose();
            _area.Dispose();
            _lakes.Dispose();
            _nodes.Dispose();
            _rivers.Dispose();
            _cellStart.Dispose();
            _cellRuns.Dispose();
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    internal struct CopyJob : IJob
    {
        [ReadOnly]
        public NativeArray<float> From;

        [WriteOnly]
        public NativeArray<float> To;

        public void Execute()
        {
            To.CopyFrom(From);
        }
    }

    // Everything after erosion (D-077 to D-081): fill, small pits flat, lakes, flow,
    // rivers and their index.
    [BurstCompile(CompileSynchronously = true)]
    internal struct HydrologyBuildJob : IJob
    {
        public TerrainParams Params;

        [ReadOnly]
        public NativeArray<float> Land;

        [ReadOnly]
        public NativeArray<float> Rain;

        [ReadOnly]
        public NativeArray<bool> Ocean;

        [ReadOnly]
        public NativeArray<float> Eroded;

        public NativeArray<float> Filled;
        public NativeArray<int> Basin;
        public NativeArray<float> Final;
        public NativeArray<int> Receiver;
        public NativeArray<int> Order;
        public NativeArray<float> Area;
        public NativeArray<float> Delta;
        public NativeArray<ushort> LakeOf;
        public NativeList<Lake> Lakes;
        public NativeList<RiverNode> Nodes;
        public NativeList<River> Rivers;
        public NativeList<int> CellStart;
        public NativeList<int3> CellRuns;

        public void Execute()
        {
            DepressionFill.Fill(Eroded, Ocean, DepressionFill.Epsilon, Filled);
            var basins = Basins.Resolve(
                Eroded,
                Filled,
                Params.LakeMinDepth,
                Params.LakeMinCells,
                Basin,
                Final
            );
            FlowRouting.Receivers(Filled, Ocean, Receiver);
            Lakes.Clear();
            Basins.Lakes(Params, basins, Basin, Eroded, Filled, Receiver, Final, Lakes);
            FlowRouting.Order(Filled, Order);
            FlowRouting.Accumulate(Order, Receiver, Rain, Area);
            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                Delta[i] = Final[i] - Land[i];
                LakeOf[i] = (ushort)(Basin[i] + 1);
            }

            // The rivers read the shaped land: the parameters with what is built so far.
            var p = Params;
            var plan = p.WaterPlan;
            plan.Lakes = Lakes.AsArray();
            plan.Delta = Delta;
            plan.LakeOf = LakeOf;
            p.WaterPlan = plan;
            RiverNetwork.Build(p, Receiver, Area, Ocean, Nodes, Rivers, CellStart, CellRuns);
        }
    }
}
