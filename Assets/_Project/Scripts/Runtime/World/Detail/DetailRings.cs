using Unity.Mathematics;

namespace Revoxel.World
{
    // Nested squares of the detail levels (D-057). Level 0 is the full-detail square of
    // VoxelWorld, in full-resolution columns. Level L (1 to Levels) is a square of level-L
    // cells (one cell is 2^L x 2^L full-resolution columns) whose edges lie on the level
    // L + 1 grid. Each square holds the finer one with at least one cell of margin, so a
    // level L - 1 cell never touches a level L + 1 cell. A coarse level's half-width is the
    // ring half-width, widened where needed to hold the finer square (a large full-detail
    // square widens only the first coarse levels). Level L draws its square minus the
    // finer square; every column of the outermost square is drawn by exactly one level.
    public sealed class DetailRings
    {
        private readonly int2[] _centers;
        private readonly int[] _halfWidths;
        private bool _initialized;

        public DetailRings(int levels, int fullHalfWidth, int ringHalfWidth)
        {
            Levels = levels;
            FullHalfWidth = fullHalfWidth;
            RingHalfWidth = ringHalfWidth;
            _centers = new int2[levels + 1];
            _halfWidths = new int[levels + 1];
            _halfWidths[0] = fullHalfWidth;
            for (var level = 1; level <= levels; level++)
            {
                _halfWidths[level] = math.max(
                    ringHalfWidth,
                    MinRingHalfWidth(_halfWidths[level - 1])
                );
            }
        }

        public int Levels { get; }

        public int FullHalfWidth { get; }

        public int RingHalfWidth { get; }

        // Half-width of the outermost square, meters.
        public float ViewDistanceMeters =>
            HalfWidth(Levels) * (1 << Levels) * WorldConstants.ChunkSize * WorldConstants.VoxelSize;

        // The smallest even half-width that keeps one cell of margin around a finer square of
        // this half-width (in finer cells, even).
        public static int MinRingHalfWidth(int finerHalfWidth) => (finerHalfWidth / 2 + 3) & ~1;

        public int2 Center(int level) => _centers[level];

        public int HalfWidth(int level) => _halfWidths[level];

        // Inclusive min of the level's square, in level units.
        public int2 Min(int level) => _centers[level] - HalfWidth(level);

        // Exclusive max of the level's square, in level units.
        public int2 Max(int level) => _centers[level] + HalfWidth(level);

        // Takes the full-detail center (even) and recentres coarser squares that would no
        // longer hold the finer one with a cell of margin. Returns whether any square moved.
        public bool Update(int2 fullDetailCenter)
        {
            var moved = !_initialized || !_centers[0].Equals(fullDetailCenter);
            _centers[0] = fullDetailCenter;
            for (var level = 1; level <= Levels; level++)
            {
                var innerMin = Min(level - 1) >> 1;
                var innerMax = Max(level - 1) >> 1;
                var min = Min(level);
                var max = Max(level);
                if (_initialized && math.all(innerMin >= min + 1) && math.all(innerMax <= max - 1))
                {
                    continue;
                }

                _centers[level] = ChunkStreamer.NearestEven(_centers[level - 1] >> 1);
                moved = true;
            }

            _initialized = true;
            return moved;
        }

        public bool InRegion(int level, int2 cell) =>
            math.all(cell >= Min(level)) && math.all(cell < Max(level));

        // Inside the level's square and, for level 1 and up, outside the finer square.
        public bool IsDisplayed(int level, int2 cell)
        {
            if (!InRegion(level, cell))
            {
                return false;
            }

            if (level == 0)
            {
                return true;
            }

            var innerMin = Min(level - 1) >> 1;
            var innerMax = Max(level - 1) >> 1;
            return math.any(cell < innerMin) || math.any(cell >= innerMax);
        }

        // The level that draws a full-resolution column, or -1 outside the view.
        public int DisplayedLevel(int2 column)
        {
            for (var level = 0; level <= Levels; level++)
            {
                if (InRegion(level, column >> level))
                {
                    return level;
                }
            }

            return -1;
        }
    }
}
