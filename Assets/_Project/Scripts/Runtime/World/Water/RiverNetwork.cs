using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Rivers from the flow network of the hydrology grid (D-080). Cells draining at least
    // ChannelArea carry a river, and so does the path from every lake's spill. Each river
    // cell continues the river of its largest donor; the others end there (confluences).
    // A river runs through its cell centers, smoothed (Chaikin), meandered across its path
    // and cut into segments; widths and depths grow with the area drained, and the level
    // falls to the lowest land at and beside the channel, never rising downstream.
    public static class RiverNetwork
    {
        // After Chaikin the path is cut every half segment and relaxed this many times
        // (about a Gaussian of one segment): Chaikin alone tends to a quadratic B-spline,
        // whose radius at a 90° turn of the grid path is about 11 m, 40° per segment.
        private const int RelaxPasses = 16;

        // Levels are read this many times per segment (the last at the node, across the
        // channel too).
        private const int SegmentSamples = 2;

        // Beyond the bank the level also reads the land this far out, so no ground beside
        // the river is lower than its water (no dikes), meters.
        private const float SideMarginMeters = 12f;

        // The meander fades out over this distance from both ends (pinned at sources,
        // confluences, lakes and mouths), voxels.
        private const float MeanderTaper = HydroGrid.CellVoxels;

        // Widths and depths grow from their values at this drainage (D-080), whatever the
        // channel threshold, km².
        private const float ReferenceAreaKm2 = 1f;

        private enum End : byte
        {
            Sea = 0,
            Lake = 1,
            Confluence = 2,
        }

        private struct Chain
        {
            public int Start;
            public int Count;
            public End End;
            public int Next;
            public int SourceLake;
        }

        public static void Build(
            in TerrainParams p,
            NativeArray<int> receiver,
            NativeArray<float> area,
            NativeArray<bool> ocean,
            NativeList<RiverNode> nodes,
            NativeList<River> rivers,
            NativeList<int> cellStart,
            NativeList<int3> cellRuns
        )
        {
            nodes.Clear();
            rivers.Clear();
            cellStart.Clear();
            cellRuns.Clear();
            var plan = p.WaterPlan;
            var count = HydroGrid.CellCount;
            var isRiver = new NativeArray<bool>(count, Allocator.Temp);
            for (var i = 0; i < count; i++)
            {
                isRiver[i] = !ocean[i] && plan.LakeOf[i] == 0 && area[i] >= p.ChannelArea;
            }

            // Every lake drains: its outlet cell (the lake cell flowing to the spill with the
            // most water) starts a river, and the path from the spill carries one.
            var outlet = new NativeArray<int>(plan.Lakes.Length, Allocator.Temp);
            for (var k = 0; k < outlet.Length; k++)
            {
                outlet[k] = -1;
            }

            for (var i = 0; i < count; i++)
            {
                var k = plan.LakeOf[i] - 1;
                if (k >= 0 && receiver[i] == plan.Lakes[k].Spill)
                {
                    if (outlet[k] < 0 || area[i] > area[outlet[k]])
                    {
                        outlet[k] = i;
                    }
                }
            }

            for (var k = 0; k < outlet.Length; k++)
            {
                var c = plan.Lakes[k].Spill;
                for (var guard = 0; guard < count && c >= 0; guard++)
                {
                    if (ocean[c] || plan.LakeOf[c] != 0 || isRiver[c])
                    {
                        break;
                    }

                    isRiver[c] = true;
                    c = receiver[c];
                }
            }

            // The main donor of each river cell: the river cell (or lake outlet) draining
            // into it with the most water, ties to the lower index.
            var main = new NativeArray<int>(count, Allocator.Temp);
            for (var i = 0; i < count; i++)
            {
                main[i] = -1;
            }

            for (var i = 0; i < count; i++)
            {
                var r = receiver[i];
                if (r < 0 || !isRiver[r])
                {
                    continue;
                }

                var k = plan.LakeOf[i] - 1;
                if (!isRiver[i] && (k < 0 || outlet[k] != i))
                {
                    continue;
                }

                var m = main[r];
                if (m < 0 || area[i] > area[m])
                {
                    main[r] = i;
                }
            }

            // Chains of cells, one per river.
            var chainOf = new NativeArray<int>(count, Allocator.Temp);
            for (var i = 0; i < count; i++)
            {
                chainOf[i] = -1;
            }

            var cells = new NativeList<int>(4096, Allocator.Temp);
            var chains = new NativeList<Chain>(256, Allocator.Temp);
            for (var k = 0; k < outlet.Length; k++)
            {
                if (outlet[k] >= 0)
                {
                    AddChain(
                        outlet[k],
                        k,
                        receiver,
                        ocean,
                        isRiver,
                        main,
                        plan,
                        chainOf,
                        cells,
                        chains
                    );
                }
            }

            for (var i = 0; i < count; i++)
            {
                if (isRiver[i] && main[i] < 0)
                {
                    AddChain(i, -1, receiver, ocean, isRiver, main, plan, chainOf, cells, chains);
                }
            }

            // Downstream rivers first, so a tributary meets the built river it joins.
            var depth = new NativeArray<int>(chains.Length, Allocator.Temp);
            var deepest = 0;
            for (var c = 0; c < chains.Length; c++)
            {
                var t = c;
                var d = 0;
                while (chains[t].End == End.Confluence && d < chains.Length)
                {
                    t = chainOf[chains[t].Next];
                    d++;
                }

                depth[c] = d;
                deepest = math.max(deepest, d);
            }

            var nodeOf = new NativeArray<int>(count, Allocator.Temp);
            for (var i = 0; i < count; i++)
            {
                nodeOf[i] = -1;
            }

            var points = new NativeList<float3>(1024, Allocator.Temp);
            var scratch = new NativeList<float3>(1024, Allocator.Temp);
            var path = new NativeList<RiverNode>(4096, Allocator.Temp);
            for (var d = 0; d <= deepest; d++)
            {
                for (var c = 0; c < chains.Length; c++)
                {
                    if (depth[c] == d)
                    {
                        BuildRiver(
                            p,
                            chains[c],
                            cells,
                            area,
                            nodeOf,
                            nodes,
                            points,
                            scratch,
                            path,
                            rivers
                        );
                    }
                }
            }

            BuildIndex(p, nodes, rivers, cellStart, cellRuns);

            isRiver.Dispose();
            outlet.Dispose();
            main.Dispose();
            chainOf.Dispose();
            cells.Dispose();
            chains.Dispose();
            depth.Dispose();
            nodeOf.Dispose();
            points.Dispose();
            scratch.Dispose();
            path.Dispose();
        }

        private static void AddChain(
            int start,
            int sourceLake,
            NativeArray<int> receiver,
            NativeArray<bool> ocean,
            NativeArray<bool> isRiver,
            NativeArray<int> main,
            in WaterPlan plan,
            NativeArray<int> chainOf,
            NativeList<int> cells,
            NativeList<Chain> chains
        )
        {
            var id = chains.Length;
            var chain = new Chain
            {
                Start = cells.Length,
                SourceLake = sourceLake,
                End = End.Sea,
                Next = -1,
            };
            var cur = start;
            cells.Add(cur);
            if (sourceLake < 0)
            {
                chainOf[cur] = id;
            }

            for (var guard = 0; guard < HydroGrid.CellCount; guard++)
            {
                var next = receiver[cur];
                chain.Next = next;
                if (next < 0 || ocean[next])
                {
                    chain.End = End.Sea;
                    break;
                }

                if (plan.LakeOf[next] != 0)
                {
                    chain.End = End.Lake;
                    break;
                }

                if (isRiver[next] && main[next] == cur && chainOf[next] < 0)
                {
                    cur = next;
                    cells.Add(cur);
                    chainOf[cur] = id;
                    continue;
                }

                chain.End = isRiver[next] ? End.Confluence : End.Sea;
                break;
            }

            chain.Count = cells.Length - chain.Start;
            chains.Add(chain);
        }

        private static float HalfWidth(in TerrainParams p, float area) =>
            math.clamp(
                p.HalfWidthAtStart * math.sqrt(area / ReferenceAreaKm2),
                p.MinHalfWidth,
                p.MaxHalfWidth
            );

        private static float Depth(in TerrainParams p, float area) =>
            math.clamp(
                p.DepthAtStart * math.pow(area / ReferenceAreaKm2, 0.4f),
                p.MinDepth,
                p.MaxDepth
            );

        private static float Land(in TerrainParams p, float2 pos) =>
            TerrainGenerator.ShapedLandHeight(p, (int)math.floor(pos.x), (int)math.floor(pos.y));

        // The lowest land at pos and across the channel: at its edges and beyond the bank.
        private static float LowestAcross(
            in TerrainParams p,
            float2 pos,
            float2 dir,
            float halfWidth
        )
        {
            var side = new float2(-dir.y, dir.x);
            var far = halfWidth + p.BankWidth + SideMarginMeters * WorldConstants.VoxelsPerMeter;
            var lowest = Land(p, pos);
            lowest = math.min(
                lowest,
                math.min(Land(p, pos + side * halfWidth), Land(p, pos - side * halfWidth))
            );
            return math.min(lowest, math.min(Land(p, pos + side * far), Land(p, pos - side * far)));
        }

        private static int LakeAt(in TerrainParams p, float2 pos)
        {
            var cell = HydroGrid.CellOf(pos);
            return HydroGrid.InGrid(cell) ? p.WaterPlan.LakeOf[HydroGrid.Index(cell)] - 1 : -1;
        }

        private static void BuildRiver(
            in TerrainParams p,
            in Chain chain,
            NativeList<int> cells,
            NativeArray<float> area,
            NativeArray<int> nodeOf,
            NativeList<RiverNode> nodes,
            NativeList<float3> points,
            NativeList<float3> scratch,
            NativeList<RiverNode> path,
            NativeList<River> rivers
        )
        {
            // The path through the cell centers (xy) with the area drained (z), then its end.
            points.Clear();
            for (var j = 0; j < chain.Count; j++)
            {
                var cell = cells[chain.Start + j];
                points.Add(new float3(HydroGrid.Center(HydroGrid.Cell(cell)), area[cell]));
            }

            var lastArea = points[points.Length - 1].z;
            var joinLevel = float.MinValue;
            if (chain.End == End.Confluence && nodeOf[chain.Next] >= 0)
            {
                var join = nodes[nodeOf[chain.Next]];
                points.Add(new float3(join.Pos, lastArea));
                joinLevel = join.Level;
            }
            else if (chain.Next >= 0)
            {
                points.Add(new float3(HydroGrid.Center(HydroGrid.Cell(chain.Next)), lastArea));
            }
            else
            {
                var a = points[points.Length - 1];
                var b = points.Length > 1 ? points[points.Length - 2] : a - new float3(1f, 0f, 0f);
                points.Add(new float3(a.xy + (a.xy - b.xy), lastArea));
            }

            var endLake = chain.End == End.Lake ? p.WaterPlan.LakeOf[chain.Next] - 1 : -1;
            if (endLake >= 0)
            {
                joinLevel = p.WaterPlan.Lakes[endLake].Level;
            }

            for (var pass = 0; pass < p.SmoothingIterations; pass++)
            {
                Chaikin(points, scratch);
            }

            Resample(points, scratch, p.RiverStep * 0.5f);
            for (var pass = 0; pass < RelaxPasses; pass++)
            {
                Relax(points, scratch);
            }

            Meander(p, points, scratch, cells[chain.Start]);
            Resample(points, scratch, p.RiverStep);

            // Levels, walking downstream.
            path.Clear();
            var first = points[0];
            var dir0 =
                points.Length > 1
                    ? math.normalizesafe(points[1].xy - first.xy)
                    : new float2(1f, 0f);
            var hw0 = HalfWidth(p, first.z);
            var level =
                chain.SourceLake >= 0
                    ? p.WaterPlan.Lakes[chain.SourceLake].Level
                    : math.max(p.SeaLevel, LowestAcross(p, first.xy, dir0, hw0) - p.RiverInset);
            path.Add(
                new RiverNode
                {
                    Pos = first.xy,
                    Level = level,
                    HalfWidth = hw0,
                    Depth = Depth(p, first.z),
                }
            );
            var leftLake = chain.SourceLake < 0;
            for (var i = 1; i < points.Length; i++)
            {
                var from = path[path.Length - 1];
                var to = points[i];
                var dir = math.normalizesafe(to.xy - from.Pos);
                var hw = HalfWidth(p, to.z);
                var last = i == points.Length - 1;
                var lowest = float.MaxValue;
                var dropAt = 1f;
                for (var s = 1; s <= SegmentSamples; s++)
                {
                    var f = (float)s / SegmentSamples;
                    var q = math.lerp(from.Pos, to.xy, f);
                    var h = s < SegmentSamples ? Land(p, q) : LowestAcross(p, q, dir, hw);
                    if (dropAt >= 1f && h - p.RiverInset < level)
                    {
                        dropAt = (s - 1f) / SegmentSamples;
                    }

                    lowest = math.min(lowest, h);
                }

                leftLake = leftLake || LakeAt(p, to.xy) != chain.SourceLake;
                var newLevel = leftLake
                    ? math.max(p.SeaLevel, math.min(level, lowest - p.RiverInset))
                    : level;
                var done = false;
                if (chain.End == End.Sea && Land(p, to.xy) < p.SeaLevel)
                {
                    newLevel = p.SeaLevel;
                    done = true;
                }
                else if (endLake >= 0 && (last || LakeAt(p, to.xy) == endLake))
                {
                    newLevel = joinLevel;
                    done = true;
                }
                else if (last)
                {
                    if (joinLevel > float.MinValue)
                    {
                        newLevel = joinLevel;
                    }
                    else if (chain.End == End.Sea)
                    {
                        newLevel = p.SeaLevel;
                    }

                    done = true;
                }

                AddStep(p, path, from, to.xy, hw, Depth(p, to.z), newLevel, dropAt);
                level = newLevel;
                if (done)
                {
                    break;
                }
            }

            // Water never falls below what it joins.
            if (joinLevel > float.MinValue)
            {
                for (var n = 0; n < path.Length; n++)
                {
                    var node = path[n];
                    node.Level = math.max(node.Level, joinLevel);
                    path[n] = node;
                }
            }

            var river = new River
            {
                Start = nodes.Length,
                Count = path.Length,
                Bounds = new float4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue),
            };
            for (var n = 0; n < path.Length; n++)
            {
                var node = path[n];
                nodes.Add(node);
                river.Bounds = new float4(
                    math.min(river.Bounds.xy, node.Pos),
                    math.max(river.Bounds.zw, node.Pos)
                );
            }

            river.Bounds += new float4(-p.RiverReach, -p.RiverReach, p.RiverReach, p.RiverReach);
            rivers.Add(river);

            // The node of this river nearest each of its cells, where tributaries join.
            for (var j = chain.SourceLake >= 0 ? 1 : 0; j < chain.Count; j++)
            {
                var cell = cells[chain.Start + j];
                var center = HydroGrid.Center(HydroGrid.Cell(cell));
                var best = float.MaxValue;
                for (var n = river.Start; n < river.Start + river.Count; n++)
                {
                    var d = math.distancesq(nodes[n].Pos, center);
                    if (d < best)
                    {
                        best = d;
                        nodeOf[cell] = n;
                    }
                }
            }
        }

        // One Chaikin pass with both ends pinned: every corner cut at a quarter and three
        // quarters of its segments.
        private static void Chaikin(NativeList<float3> points, NativeList<float3> scratch)
        {
            if (points.Length < 3)
            {
                return;
            }

            scratch.Clear();
            scratch.Add(points[0]);
            for (var i = 0; i + 1 < points.Length; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                if (i > 0)
                {
                    scratch.Add(math.lerp(a, b, 0.25f));
                }

                if (i + 2 < points.Length)
                {
                    scratch.Add(math.lerp(a, b, 0.75f));
                }
            }

            scratch.Add(points[points.Length - 1]);
            points.Clear();
            points.AddRange(scratch.AsArray());
        }

        // One pass of pinned Laplacian smoothing: each inner point moves halfway to the
        // middle of its neighbors (the area drained too, which stays monotonic).
        private static void Relax(NativeList<float3> points, NativeList<float3> scratch)
        {
            if (points.Length < 3)
            {
                return;
            }

            scratch.Clear();
            scratch.Add(points[0]);
            for (var i = 1; i + 1 < points.Length; i++)
            {
                var middle = 0.5f * (points[i - 1] + points[i + 1]);
                scratch.Add(math.lerp(points[i], middle, 0.5f));
            }

            scratch.Add(points[points.Length - 1]);
            points.Clear();
            points.AddRange(scratch.AsArray());
        }

        // Offsets each point across the path by MeanderAmplitude half widths times a noise
        // along the arc, faded out near both ends.
        private static void Meander(
            in TerrainParams p,
            NativeList<float3> points,
            NativeList<float3> scratch,
            int seedCell
        )
        {
            if (points.Length < 3 || p.MeanderAmplitude <= 0f)
            {
                return;
            }

            var total = 0f;
            for (var i = 1; i < points.Length; i++)
            {
                total += math.distance(points[i - 1].xy, points[i].xy);
            }

            scratch.Clear();
            scratch.Add(points[0]);
            var arc = 0f;
            var lane = p.MeanderOffset.y + (seedCell % 4096) * 0.731f;
            for (var i = 1; i + 1 < points.Length; i++)
            {
                arc += math.distance(points[i - 1].xy, points[i].xy);
                var tangent = math.normalizesafe(points[i + 1].xy - points[i - 1].xy);
                var side = new float2(-tangent.y, tangent.x);
                var taper =
                    math.smoothstep(0f, MeanderTaper, arc)
                    * math.smoothstep(0f, MeanderTaper, total - arc);
                var wave = noise.snoise(
                    new float2(arc * p.MeanderFrequency + p.MeanderOffset.x, lane)
                );
                var offset = p.MeanderAmplitude * HalfWidth(p, points[i].z) * wave * taper;
                scratch.Add(new float3(points[i].xy + side * offset, points[i].z));
            }

            scratch.Add(points[points.Length - 1]);
            points.Clear();
            points.AddRange(scratch.AsArray());
        }

        // Points every step along the polyline, the last point kept; a last piece shorter
        // than half a step joins the one before.
        private static void Resample(
            NativeList<float3> points,
            NativeList<float3> scratch,
            float step
        )
        {
            scratch.Clear();
            scratch.Add(points[0]);
            var total = 0f;
            for (var i = 1; i < points.Length; i++)
            {
                total += math.distance(points[i - 1].xy, points[i].xy);
            }

            var target = step;
            var walked = 0f;
            for (var i = 1; i < points.Length && target < total - step * 0.5f; i++)
            {
                var a = points[i - 1];
                var b = points[i];
                var length = math.distance(a.xy, b.xy);
                while (length > 0f && target <= walked + length && target < total - step * 0.5f)
                {
                    scratch.Add(math.lerp(a, b, (target - walked) / length));
                    target += step;
                }

                walked += length;
            }

            scratch.Add(points[points.Length - 1]);
            points.Clear();
            points.AddRange(scratch.AsArray());
        }

        // Adds the segment from from to to. A drop of at least FallMin becomes a waterfall:
        // the level holds to the cliff (where the ground drops), falls over a face of
        // drop / FallSteepness, then holds again.
        private static void AddStep(
            in TerrainParams p,
            NativeList<RiverNode> path,
            in RiverNode from,
            float2 to,
            float halfWidth,
            float depth,
            float newLevel,
            float dropAt
        )
        {
            var drop = from.Level - newLevel;
            if (drop >= p.FallMin)
            {
                var length = math.distance(from.Pos, to);
                var dir = length > 0f ? (to - from.Pos) / length : float2.zero;
                var face = math.min(drop / p.FallSteepness, length * 0.5f);
                var top = math.clamp(dropAt * length, 1f, math.max(1f, length - face - 1f));
                var ft = length > 0f ? top / length : 0f;
                var fb = length > 0f ? math.min(1f, (top + face) / length) : 0f;
                path.Add(
                    new RiverNode
                    {
                        Pos = from.Pos + dir * top,
                        Level = from.Level,
                        HalfWidth = math.lerp(from.HalfWidth, halfWidth, ft),
                        Depth = math.lerp(from.Depth, depth, ft),
                    }
                );
                path.Add(
                    new RiverNode
                    {
                        Pos = from.Pos + dir * (top + face),
                        Level = newLevel,
                        HalfWidth = math.lerp(from.HalfWidth, halfWidth, fb),
                        Depth = math.lerp(from.Depth, depth, fb),
                    }
                );
            }

            path.Add(
                new RiverNode
                {
                    Pos = to,
                    Level = newLevel,
                    HalfWidth = math.max(from.HalfWidth, halfWidth),
                    Depth = math.max(from.Depth, depth),
                }
            );
        }

        // For each grid cell, the runs of consecutive segments (river, first node, last
        // node) whose reach (half width, bank, widest wall) overlaps the cell, river by
        // river in plan order.
        private static void BuildIndex(
            in TerrainParams p,
            NativeList<RiverNode> nodes,
            NativeList<River> rivers,
            NativeList<int> cellStart,
            NativeList<int3> cellRuns
        )
        {
            var count = HydroGrid.CellCount;
            var open = new NativeArray<int3>(count, Allocator.Temp);
            var touched = new NativeList<int>(1024, Allocator.Temp);
            var runCells = new NativeList<int>(4096, Allocator.Temp);
            var runs = new NativeList<int3>(4096, Allocator.Temp);
            for (var r = 0; r < rivers.Length; r++)
            {
                var river = rivers[r];
                touched.Clear();
                for (var n = river.Start; n + 1 < river.Start + river.Count; n++)
                {
                    var a = nodes[n];
                    var b = nodes[n + 1];
                    var reach = math.max(a.HalfWidth, b.HalfWidth) + p.BankWidth + p.MaxWallWidth;
                    var lo = math.max(HydroGrid.CellOf(math.min(a.Pos, b.Pos) - reach), 0);
                    var hi = math.min(
                        HydroGrid.CellOf(math.max(a.Pos, b.Pos) + reach),
                        HydroGrid.Size - 1
                    );
                    for (var z = lo.y; z <= hi.y; z++)
                    for (var x = lo.x; x <= hi.x; x++)
                    {
                        var i = HydroGrid.Index(new int2(x, z));
                        var o = open[i];
                        if (o.x != r + 1)
                        {
                            o = new int3(r + 1, n, n);
                            touched.Add(i);
                        }
                        else if (o.z == n - 1)
                        {
                            o.z = n;
                        }
                        else
                        {
                            runCells.Add(i);
                            runs.Add(new int3(r, o.y, o.z));
                            o = new int3(r + 1, n, n);
                        }

                        open[i] = o;
                    }
                }

                for (var t = 0; t < touched.Length; t++)
                {
                    var i = touched[t];
                    runCells.Add(i);
                    runs.Add(new int3(r, open[i].y, open[i].z));
                    open[i] = int3.zero;
                }
            }

            // Counting sort by cell, stable, so each cell's runs stay in river order.
            cellStart.Resize(count + 1, NativeArrayOptions.ClearMemory);
            for (var k = 0; k < runCells.Length; k++)
            {
                cellStart[runCells[k] + 1]++;
            }

            for (var i = 0; i < count; i++)
            {
                cellStart[i + 1] += cellStart[i];
            }

            cellRuns.Resize(runs.Length, NativeArrayOptions.UninitializedMemory);
            var fill = new NativeArray<int>(count, Allocator.Temp);
            for (var k = 0; k < runCells.Length; k++)
            {
                var i = runCells[k];
                cellRuns[cellStart[i] + fill[i]] = runs[k];
                fill[i]++;
            }

            open.Dispose();
            touched.Dispose();
            runCells.Dispose();
            runs.Dispose();
            fill.Dispose();
        }
    }
}
