using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;

namespace Revoxel.Tests.EditMode
{
    // Per-voxel sky light (D-097).
    public sealed class SkyLightTests
    {
        private const int Canopy = 115;

        [Test]
        public void SkyLight_OpenLit_SealedDark_OverhangFallsOff()
        {
            // Ground at 10 everywhere; a roof at 30 over local x 10 and beyond.
            using var light = Run((x, z) => x >= 10 ? 30 : 10, (x, z) => x >= 10 ? 30 : 10);

            Assert.That(At(light, 0, 20, 16), Is.EqualTo(SkyLight.Full), "open");
            Assert.That(At(light, 10, 20, 16), Is.EqualTo(SkyLight.Full - SkyLight.Decay));
            Assert.That(At(light, 20, 20, 16), Is.EqualTo(SkyLight.Full - 11 * SkyLight.Decay));
            Assert.That(At(light, 31, 20, 16), Is.EqualTo(0), "deep under the roof");
            for (var x = 10; x < 31; x++)
            {
                Assert.That(At(light, x + 1, 20, 16), Is.LessThanOrEqualTo(At(light, x, 20, 16)));
            }

            Assert.That(At(light, 25, 30, 16), Is.EqualTo(SkyLight.Full), "on the roof");
            Assert.That(At(light, 0, 5, 16), Is.EqualTo(0), "inside the ground");

            using var sealedCave = Run((x, z) => 30, (x, z) => 30);
            Assert.That(At(sealedCave, 16, 20, 16), Is.EqualTo(0), "sealed");
        }

        [Test]
        public void SkyLight_LeavesFilter_PlantsPass()
        {
            var registry = WorldAssets.LoadRegistry();
            using var classes = registry.CreateSkyClasses(Allocator.Temp);
            var stone = Id("Voxel_Stone");
            var leaves = Id("Voxel_Leaves");
            var grass = Id("Voxel_TallGrass");
            Assert.That(classes[stone], Is.EqualTo(SkyClass.Block));
            Assert.That(classes[leaves], Is.EqualTo(SkyClass.Filter));
            Assert.That(classes[grass], Is.EqualTo(SkyClass.Pass));
            Assert.That(classes[Id("Voxel_Water")], Is.EqualTo(SkyClass.Pass));
            Assert.That(classes[VoxelId.Air.Value], Is.EqualTo(SkyClass.Pass));

            // Stone up to y 9 everywhere; grass on (2, 2), a leaf at y 20 over (5, 5).
            var dense = new NativeArray<ushort>(WorldConstants.VoxelsPerChunk, Allocator.Temp);
            for (var z = 0; z < 32; z++)
            for (var x = 0; x < 32; x++)
            for (var y = 0; y < 10; y++)
            {
                dense[Coords.LocalIndex(x, y, z)] = stone;
            }

            dense[Coords.LocalIndex(2, 10, 2)] = grass;
            dense[Coords.LocalIndex(2, 11, 2)] = grass;
            dense[Coords.LocalIndex(5, 20, 5)] = leaves;
            using var voxels = ChunkVoxels.FromDense(dense, Allocator.Temp);
            dense.Dispose();
            using var topAny = new NativeArray<byte>(SkyLight.ColumnCells, Allocator.Temp);
            using var topFull = new NativeArray<byte>(SkyLight.ColumnCells, Allocator.Temp);
            SkyLight.ChunkTops(voxels, classes, topAny, topFull);

            var plant = SkyLight.ColumnIndex(2, 2);
            var leaf = SkyLight.ColumnIndex(5, 5);
            Assert.That(topAny[plant], Is.EqualTo(10));
            Assert.That(topFull[plant], Is.EqualTo(10));
            Assert.That(topAny[leaf], Is.EqualTo(21));
            Assert.That(topFull[leaf], Is.EqualTo(10));
            Assert.That(SkyLight.Seed(15, topAny[leaf], topFull[leaf], Canopy), Is.EqualTo(Canopy));
            Assert.That(
                SkyLight.Seed(10, topAny[plant], topFull[plant], Canopy),
                Is.EqualTo(SkyLight.Full)
            );
        }

        [Test]
        public void GreedyMesh_SkyLight_CornersCarryLight()
        {
            using var fixture = new MeshFixture();
            var light = new NativeArray<byte>(WorldConstants.PaddedVoxels, Allocator.TempJob);
            for (var y = -1; y <= 32; y++)
            for (var z = -1; z <= 32; z++)
            for (var x = -1; x <= 32; x++)
            {
                light[Coords.PaddedIndex(x, y, z)] = x < 16 ? SkyLight.Full : (byte)0;
            }

            var floor = fixture.MeshPaddedWithLight(
                p => p.y == 0 && Coords.IsLocalInRange(p) ? MeshFixture.Stone : MeshFixture.Air,
                light
            );
            light.Dispose();
            var full = fixture.MeshChunk(p => p.y == 0 ? MeshFixture.Stone : MeshFixture.Air);

            var top = 0;
            foreach (var vertex in floor.Vertices)
            {
                if (vertex.Normal.y < 0.5f)
                {
                    continue;
                }

                top++;
                var x = vertex.Position.x * WorldConstants.VoxelsPerMeter;
                if (x < 15.5f)
                {
                    Assert.That(vertex.SkyLight, Is.EqualTo(1f), $"{vertex.Position}");
                }
                else if (x > 16.5f)
                {
                    Assert.That(vertex.SkyLight, Is.EqualTo(0f), $"{vertex.Position}");
                }
                else
                {
                    Assert.That(vertex.SkyLight, Is.InRange(0.4f, 0.6f), $"{vertex.Position}");
                }
            }

            // Open ground still merges into one top quad; the shaded floor splits.
            var fullTop = 0;
            foreach (var vertex in full.Vertices)
            {
                fullTop += vertex.Normal.y > 0.5f ? 1 : 0;
                Assert.That(vertex.SkyLight, Is.EqualTo(1f));
            }

            Assert.That(fullTop, Is.EqualTo(4));
            Assert.That(top, Is.GreaterThan(4));
        }

        private static ushort Id(string asset) =>
            AssetDatabase
                .LoadAssetAtPath<VoxelType>($"{WorldAssets.VoxelsFolder}/{asset}.asset")
                .Id.Value;

        private static int At(NativeArray<byte> light, int x, int y, int z) =>
            light[Coords.PaddedIndex(x, y, z)];

        // Runs the light job on a patch whose heights come from functions of padded local
        // x and z (relative to the chunk's lowest voxel).
        private static NativeArray<byte> Run(
            System.Func<int, int, int> any,
            System.Func<int, int, int> full
        )
        {
            var patchAny = new NativeArray<int>(SkyLight.PatchCells, Allocator.TempJob);
            var patchFull = new NativeArray<int>(SkyLight.PatchCells, Allocator.TempJob);
            for (var pz = 0; pz < SkyLight.PatchSize; pz++)
            for (var px = 0; px < SkyLight.PatchSize; px++)
            {
                var x = px - 1 - SkyLight.Reach;
                var z = pz - 1 - SkyLight.Reach;
                patchAny[px + SkyLight.PatchSize * pz] = any(x, z);
                patchFull[px + SkyLight.PatchSize * pz] = full(x, z);
            }

            var light = new NativeArray<byte>(WorldConstants.PaddedVoxels, Allocator.TempJob);
            new SkyLightJob
            {
                PatchAny = patchAny,
                PatchFull = patchFull,
                Canopy = Canopy,
                Light = light,
            }.Run();
            patchAny.Dispose();
            patchFull.Dispose();
            return light;
        }
    }
}
