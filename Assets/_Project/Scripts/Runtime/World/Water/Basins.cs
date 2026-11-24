using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Which depressions of the eroded grid hold lakes (D-081) and which are filled flat
    // (D-079): a depression is a 4-connected group of cells the fill raised by more than
    // half a voxel; it is a lake when it is at least minDepth deep and minCells large.
    public static class Basins
    {
        private const float InDepression = 0.5f;

        // lakeOf: lake index (0 to the returned count - 1) or -1. final: the lake cells keep
        // their eroded height, other depressions take the filled height, the rest the
        // eroded height.
        public static int Resolve(
            NativeArray<float> eroded,
            NativeArray<float> filled,
            float minDepth,
            int minCells,
            NativeArray<int> lakeOf,
            NativeArray<float> final
        )
        {
            var group = new NativeArray<int>(HydroGrid.CellCount, Allocator.Temp);
            var stack = new NativeList<int>(256, Allocator.Temp);
            var members = new NativeList<int>(256, Allocator.Temp);
            var lakes = 0;
            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                group[i] = -1;
                lakeOf[i] = -1;
                final[i] = eroded[i];
            }

            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                if (group[i] >= 0 || filled[i] - eroded[i] <= InDepression)
                {
                    continue;
                }

                members.Clear();
                stack.Clear();
                stack.Add(i);
                group[i] = i;
                var deepest = 0f;
                while (stack.Length > 0)
                {
                    var at = stack[stack.Length - 1];
                    stack.RemoveAt(stack.Length - 1);
                    members.Add(at);
                    deepest = math.max(deepest, filled[at] - eroded[at]);
                    var c = HydroGrid.Cell(at);
                    for (var k = 0; k < 4; k++)
                    {
                        var n = c + DepressionFill.Neighbor(k);
                        if (!HydroGrid.InGrid(n))
                        {
                            continue;
                        }

                        var j = HydroGrid.Index(n);
                        if (group[j] < 0 && filled[j] - eroded[j] > InDepression)
                        {
                            group[j] = i;
                            stack.Add(j);
                        }
                    }
                }

                var isLake = deepest >= minDepth && members.Length >= minCells;
                for (var m = 0; m < members.Length; m++)
                {
                    var cell = members[m];
                    if (isLake)
                    {
                        lakeOf[cell] = lakes;
                    }
                    else
                    {
                        final[cell] = filled[cell];
                    }
                }

                lakes += isLake ? 1 : 0;
            }

            group.Dispose();
            stack.Dispose();
            members.Dispose();
            return lakes;
        }

        // The lakes of the basins Resolve found (D-081): level at the spill height (the
        // lowest filled height of the basin) minus the margin, rounded down to the level
        // step; the spill cell is where the flow, followed from the basin's lowest filled
        // cell, first leaves it (it may enter a lower lake). A basin whose level would not
        // stand above the sea, or would leave less than minDepth of water, is filled flat
        // instead and drops out (lakeOf -1, final = filled), and lake indices close up.
        public static void Lakes(
            in TerrainParams p,
            int count,
            NativeArray<int> lakeOf,
            NativeArray<float> eroded,
            NativeArray<float> filled,
            NativeArray<int> receiver,
            NativeArray<float> final,
            NativeList<Lake> lakes
        )
        {
            var lowest = new NativeArray<int>(count, Allocator.Temp);
            var deepest = new NativeArray<int>(count, Allocator.Temp);
            var cells = new NativeArray<int>(count, Allocator.Temp);
            var bounds = new NativeArray<int4>(count, Allocator.Temp);
            for (var k = 0; k < count; k++)
            {
                lowest[k] = -1;
                deepest[k] = -1;
                bounds[k] = new int4(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
            }

            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                var k = lakeOf[i];
                if (k < 0)
                {
                    continue;
                }

                cells[k]++;
                var c = HydroGrid.Cell(i);
                bounds[k] = new int4(math.min(bounds[k].xy, c), math.max(bounds[k].zw, c));
                if (lowest[k] < 0 || filled[i] < filled[lowest[k]])
                {
                    lowest[k] = i;
                }

                if (deepest[k] < 0 || eroded[i] < eroded[deepest[k]])
                {
                    deepest[k] = i;
                }
            }

            var index = new NativeArray<int>(count, Allocator.Temp);
            for (var k = 0; k < count; k++)
            {
                var level =
                    math.floor((filled[lowest[k]] - p.LakeMargin) / p.LakeLevelStep)
                    * p.LakeLevelStep;
                var spill = lowest[k];
                for (
                    var steps = 0;
                    spill >= 0 && lakeOf[spill] == k && steps < HydroGrid.CellCount;
                    steps++
                )
                {
                    spill = receiver[spill];
                }

                // The level rounds down: a basin left with less than the minimum depth of
                // water at its deepest cell is filled flat instead.
                if (level <= p.SeaLevel || spill < 0 || level - eroded[deepest[k]] < p.LakeMinDepth)
                {
                    index[k] = -1;
                    continue;
                }

                index[k] = lakes.Length;
                lakes.Add(
                    new Lake
                    {
                        Level = level,
                        Spill = spill,
                        Deepest = deepest[k],
                        Cells = cells[k],
                        Bounds = bounds[k],
                    }
                );
            }

            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                var k = lakeOf[i];
                if (k < 0)
                {
                    continue;
                }

                lakeOf[i] = index[k];
                if (index[k] < 0)
                {
                    final[i] = filled[i];
                }
            }

            lowest.Dispose();
            deepest.Dispose();
            cells.Dispose();
            bounds.Dispose();
            index.Dispose();
        }
    }
}
