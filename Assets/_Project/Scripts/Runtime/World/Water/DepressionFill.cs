using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Priority-flood depression filling with epsilon (Barnes, Lehman and Mulla 2014):
    // every cell ends at least as high as before and strictly higher than some neighbor on
    // its way to the sea, so flow routing never stalls in a pit or on a flat (D-077).
    public static class DepressionFill
    {
        // Rise per cell across filled flats and pits, voxels.
        public const float Epsilon = 0.01f;

        // Seeds are the ocean cells and the grid's edge cells, which keep their height.
        public static void Fill(
            NativeArray<float> height,
            NativeArray<bool> ocean,
            float epsilon,
            NativeArray<float> filled
        )
        {
            var visited = new NativeArray<bool>(HydroGrid.CellCount, Allocator.Temp);
            var heap = new MinHeap(HydroGrid.CellCount);
            for (var i = 0; i < HydroGrid.CellCount; i++)
            {
                var c = HydroGrid.Cell(i);
                var edge =
                    c.x == 0 || c.y == 0 || c.x == HydroGrid.Size - 1 || c.y == HydroGrid.Size - 1;
                if (ocean[i] || edge)
                {
                    filled[i] = height[i];
                    visited[i] = true;
                    heap.Push(height[i], i);
                }
            }

            while (heap.Count > 0)
            {
                var (h, i) = heap.Pop();
                var c = HydroGrid.Cell(i);
                for (var k = 0; k < 8; k++)
                {
                    var n = c + Neighbor(k);
                    if (!HydroGrid.InGrid(n))
                    {
                        continue;
                    }

                    var j = HydroGrid.Index(n);
                    if (visited[j])
                    {
                        continue;
                    }

                    visited[j] = true;
                    filled[j] = math.max(height[j], h + epsilon);
                    heap.Push(filled[j], j);
                }
            }

            heap.Dispose();
            visited.Dispose();
        }

        internal static int2 Neighbor(int k) =>
            k switch
            {
                0 => new int2(1, 0),
                1 => new int2(-1, 0),
                2 => new int2(0, 1),
                3 => new int2(0, -1),
                4 => new int2(1, 1),
                5 => new int2(-1, 1),
                6 => new int2(1, -1),
                _ => new int2(-1, -1),
            };

        // Binary min-heap of (key, cell); ties broken by cell index, so the fill is
        // deterministic.
        private struct MinHeap
        {
            private NativeList<float> _keys;
            private NativeList<int> _items;

            public MinHeap(int capacity)
            {
                _keys = new NativeList<float>(capacity, Allocator.Temp);
                _items = new NativeList<int>(capacity, Allocator.Temp);
            }

            public int Count => _keys.Length;

            public void Push(float key, int item)
            {
                _keys.Add(key);
                _items.Add(item);
                var i = _keys.Length - 1;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (!Less(i, parent))
                    {
                        break;
                    }

                    Swap(i, parent);
                    i = parent;
                }
            }

            public (float, int) Pop()
            {
                var top = (_keys[0], _items[0]);
                var last = _keys.Length - 1;
                Swap(0, last);
                _keys.RemoveAt(last);
                _items.RemoveAt(last);
                var i = 0;
                while (true)
                {
                    var left = 2 * i + 1;
                    var right = left + 1;
                    var smallest = i;
                    if (left < _keys.Length && Less(left, smallest))
                    {
                        smallest = left;
                    }

                    if (right < _keys.Length && Less(right, smallest))
                    {
                        smallest = right;
                    }

                    if (smallest == i)
                    {
                        break;
                    }

                    Swap(i, smallest);
                    i = smallest;
                }

                return top;
            }

            public void Dispose()
            {
                _keys.Dispose();
                _items.Dispose();
            }

            private bool Less(int a, int b) =>
                _keys[a] < _keys[b] || (_keys[a] == _keys[b] && _items[a] < _items[b]);

            private void Swap(int a, int b)
            {
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
                (_items[a], _items[b]) = (_items[b], _items[a]);
            }
        }
    }
}
