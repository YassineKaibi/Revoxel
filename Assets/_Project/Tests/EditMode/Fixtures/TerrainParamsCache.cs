using NUnit.Framework;

namespace Revoxel.Tests.EditMode
{
    // Frees the shared TerrainParams of WorldAssets after every EditMode test ran (D-078).
    [SetUpFixture]
    public sealed class TerrainParamsCache
    {
        [OneTimeTearDown]
        public void DisposeSharedParams()
        {
            WorldAssets.DisposeCache();
        }
    }
}
