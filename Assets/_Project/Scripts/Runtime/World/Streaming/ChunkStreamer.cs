using System.Collections.Generic;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Which chunk columns the world wants around the full-detail center, and which it
    // drops (D-057: a square, not a circle). No allocation per call once a half-width is
    // cached.
    public sealed class ChunkStreamer
    {
        // The center is recentred when the focus is farther than this (Chebyshev, columns).
        public const int RecenterDistance = 2;

        private readonly List<int2> _offsets = new List<int2>();
        private int _offsetsHalfWidth = -1;

        // Fills the list (cleared first) with every column of
        // [center - halfWidth, center + halfWidth), nearest to the point `center` first.
        public void ComputeWanted(int2 center, int halfWidth, List<int2> wantedNearestFirst)
        {
            EnsureOffsets(halfWidth);
            wantedNearestFirst.Clear();
            for (var i = 0; i < _offsets.Count; i++)
            {
                wantedNearestFirst.Add(center + _offsets[i]);
            }
        }

        public bool ShouldUnload(int2 column, int2 center, int halfWidth, int hysteresis)
        {
            var keep = halfWidth + hysteresis;
            return math.any(column < center - keep) || math.any(column >= center + keep);
        }

        // The full-detail center: even, within RecenterDistance of the focus column. Returns
        // whether it changed.
        public static bool RecenterFullDetail(ref int2 center, int2 focusColumn, bool initialized)
        {
            if (initialized && math.cmax(math.abs(focusColumn - center)) <= RecenterDistance)
            {
                return false;
            }

            var next = NearestEven(focusColumn);
            var changed = !initialized || !next.Equals(center);
            center = next;
            return changed;
        }

        // The even integer nearest to v (ties go up).
        public static int2 NearestEven(int2 v) => ((v + 1) >> 1) << 1;

        private void EnsureOffsets(int halfWidth)
        {
            if (halfWidth == _offsetsHalfWidth)
            {
                return;
            }

            _offsets.Clear();
            for (var dz = -halfWidth; dz < halfWidth; dz++)
            for (var dx = -halfWidth; dx < halfWidth; dx++)
            {
                _offsets.Add(new int2(dx, dz));
            }

            _offsets.Sort(CompareByDistance);
            _offsetsHalfWidth = halfWidth;
        }

        // By the distance of the column's center (offset + 0.5) to the region center; in
        // doubled units to stay integer.
        private static int CompareByDistance(int2 a, int2 b)
        {
            var da = a * 2 + 1;
            var db = b * 2 + 1;
            var byDistance = math.dot(da, da).CompareTo(math.dot(db, db));
            if (byDistance != 0)
            {
                return byDistance;
            }

            return a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);
        }
    }
}
