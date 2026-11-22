using System.Collections.Generic;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Which chunk columns the world wants around a focus column, and which it drops.
    // Distances are Euclidean in columns. No allocation per call once a radius is cached.
    public sealed class ChunkStreamer
    {
        private readonly List<int2> _offsets = new List<int2>();
        private int _offsetsRadius = -1;

        // Fills the list (cleared first) with every column within the radius, nearest first.
        public void ComputeWanted(int2 focusColumn, int radius, List<int2> wantedNearestFirst)
        {
            EnsureOffsets(radius);
            wantedNearestFirst.Clear();
            for (var i = 0; i < _offsets.Count; i++)
            {
                wantedNearestFirst.Add(focusColumn + _offsets[i]);
            }
        }

        public bool ShouldUnload(int2 column, int2 focusColumn, int radius, int hysteresis)
        {
            var d = column - focusColumn;
            var keep = radius + hysteresis;
            return d.x * d.x + d.y * d.y > keep * keep;
        }

        private void EnsureOffsets(int radius)
        {
            if (radius == _offsetsRadius)
            {
                return;
            }

            _offsets.Clear();
            for (var dz = -radius; dz <= radius; dz++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dz * dz <= radius * radius)
                {
                    _offsets.Add(new int2(dx, dz));
                }
            }

            _offsets.Sort(CompareByDistance);
            _offsetsRadius = radius;
        }

        private static int CompareByDistance(int2 a, int2 b)
        {
            var byDistance = (a.x * a.x + a.y * a.y).CompareTo(b.x * b.x + b.y * b.y);
            if (byDistance != 0)
            {
                return byDistance;
            }

            return a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);
        }
    }
}
