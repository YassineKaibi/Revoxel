using Revoxel.World;

namespace Revoxel.Tests.EditMode
{
    // The fixture-seed island sampled every 32 m (250 x 250 columns), computed once per test
    // run and shared by the biome tests. Only tests may sample the whole island (PLAN-04).
    public static class IslandGrid
    {
        public const int Step = 256;
        public const int Size = 250;

        private static TerrainSample[] _samples;

        public static int Coordinate(int index) => (index - Size / 2) * Step;

        public static TerrainSample[] Samples
        {
            get
            {
                if (_samples != null)
                {
                    return _samples;
                }

                var p = WorldAssets.TerrainParams(Seeds.Fixture);
                var samples = new TerrainSample[Size * Size];
                for (var j = 0; j < Size; j++)
                for (var i = 0; i < Size; i++)
                {
                    samples[i + Size * j] = TerrainGenerator.Sample(
                        p,
                        Coordinate(i),
                        Coordinate(j)
                    );
                }

                _samples = samples;
                return _samples;
            }
        }
    }
}
