using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // D8 flow routing and accumulation on the hydrology grid (D-080): each land cell sends
    // all its water to the steepest of its 8 neighbors; drainage accumulates downstream.
    public static class FlowRouting
    {
        // The steepest downhill neighbor of every land cell (diagonals at distance √2),
        // or -1 for ocean cells and cells with no lower neighbor.
        public static void Receivers(
            NativeArray<float> surface,
            NativeArray<bool> ocean,
            NativeArray<int> receiver
        )
        {
            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                receiver[i] = -1;
                if (ocean[i])
                {
                    continue;
                }

                var c = HydroGrid.Cell(i);
                var steepest = 0f;
                for (var k = 0; k < 8; k++)
                {
                    var offset = DepressionFill.Neighbor(k);
                    var n = c + offset;
                    if (!HydroGrid.InGrid(n))
                    {
                        continue;
                    }

                    var j = HydroGrid.Index(n);
                    var slope = (surface[i] - surface[j]) / math.length((float2)offset);
                    if (slope > steepest)
                    {
                        steepest = slope;
                        receiver[i] = j;
                    }
                }
            }
        }

        // Every cell, highest surface first (ties by index): water is passed on in this
        // order, each cell after all the cells that drain into it.
        public static void Order(NativeArray<float> surface, NativeArray<int> order)
        {
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            order.Sort(new Higher { Surface = surface });
        }

        // area = own rain plus everything that drains through the cell, km².
        public static void Accumulate(
            NativeArray<int> order,
            NativeArray<int> receiver,
            NativeArray<float> rain,
            NativeArray<float> area
        )
        {
            for (var i = 0; i < area.Length; i++)
            {
                area[i] = rain[i];
            }

            for (var k = 0; k < order.Length; k++)
            {
                var i = order[k];
                var r = receiver[i];
                if (r >= 0)
                {
                    area[r] += area[i];
                }
            }
        }

        private struct Higher : System.Collections.Generic.IComparer<int>
        {
            [ReadOnly]
            public NativeArray<float> Surface;

            public int Compare(int a, int b)
            {
                var c = Surface[b].CompareTo(Surface[a]);
                return c != 0 ? c : a.CompareTo(b);
            }
        }
    }
}
