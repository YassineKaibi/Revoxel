using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    public sealed class TerrainTests
    {
        [Test]
        public void Terrain_SettingsAsset_IsValid()
        {
            var settings = WorldAssets.LoadTerrainSettings();

            Assert.That(settings, Is.Not.Null, $"No settings at {WorldAssets.TerrainSettingsPath}");
            Assert.That(settings.TryValidate(out var error), Is.True, error);
            Assert.That(settings.name, Does.StartWith("Placeholder_"));
        }

        [Test]
        public void Terrain_SameSeedSameChunk_SameVoxels()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            foreach (var coord in SurfaceChunks(p))
            {
                using var a = Dense(p, coord);
                using var b = Dense(p, coord);

                Assert.That(a.ToArray(), Is.EqualTo(b.ToArray()), $"chunk {coord}");
            }
        }

        [Test]
        public void Terrain_DifferentSeeds_Differ()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var q = WorldAssets.TerrainParams(Seeds.Other);
            var differs = false;
            foreach (var coord in SurfaceChunks(p))
            {
                using var a = Dense(p, coord);
                using var b = Dense(q, coord);
                differs |= !a.ArraysEqual(b);
            }

            Assert.That(differs, Is.True);
        }

        [Test]
        public void Terrain_ChunkHighAboveSurface_IsUniformAir()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var voxels = TerrainGenerator.GenerateChunk(
                p,
                new ChunkCoord(0, 127, 0),
                Allocator.Persistent
            );
            try
            {
                Assert.That(voxels.IsUniform, Is.True);
                Assert.That(voxels.UniformId, Is.EqualTo(VoxelId.Air));
                Assert.That(voxels.MemoryBytes, Is.LessThan(64));
            }
            finally
            {
                voxels.Dispose();
            }
        }

        [Test]
        public void Terrain_DeepChunk_IsUniformStone()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var voxels = TerrainGenerator.GenerateChunk(
                p,
                new ChunkCoord(0, 0, 0),
                Allocator.Persistent
            );
            try
            {
                Assert.That(voxels.IsUniform, Is.True);
                Assert.That(
                    voxels.UniformId,
                    Is.EqualTo(Id(WorldAssets.LoadTerrainSettings().Stone))
                );
            }
            finally
            {
                voxels.Dispose();
            }
        }

        [Test]
        public void Terrain_OutsideIsland_IsSea()
        {
            var settings = WorldAssets.LoadTerrainSettings();
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var water = Id(settings.Water).Value;
            var seaLevel = (int)math.round(settings.SeaLevelMeters * WorldConstants.VoxelsPerMeter);
            var chunkYs = new List<int>();
            for (var cy = 0; cy <= seaLevel / WorldConstants.ChunkSize + 2; cy++)
            {
                chunkYs.Add(cy);
            }

            chunkYs.Add(WorldConstants.ChunksY - 1);
            var origin = new int2(999, 999) * WorldConstants.ChunkSize;
            foreach (var cy in chunkYs)
            {
                var coord = new ChunkCoord(999, cy, 999);
                using var dense = Dense(p, coord);
                for (var i = 0; i < dense.Length; i++)
                {
                    var local = Coords.LocalFromIndex(i);
                    var y = local.y + cy * WorldConstants.ChunkSize;
                    var floor = TerrainGenerator.SurfaceHeight(
                        p,
                        origin.x + local.x,
                        origin.y + local.z
                    );

                    Assert.That(floor, Is.GreaterThan(0).And.LessThan(seaLevel), "sea floor");
                    if (y < floor)
                    {
                        Assert.That(dense[i], Is.Not.EqualTo(water), $"chunk {coord}, y {y}");
                        Assert.That(dense[i], Is.Not.EqualTo(VoxelId.Air.Value));
                    }
                    else
                    {
                        var expected = y < seaLevel ? water : VoxelId.Air.Value;
                        Assert.That(dense[i], Is.EqualTo(expected), $"chunk {coord}, voxel y {y}");
                    }
                }
            }
        }

        [Test]
        public void Terrain_ColumnClassification_MatchesGeneratedChunks()
        {
            var settings = WorldAssets.LoadTerrainSettings();
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var columns = new[] { new int2(0, 0), new int2(999, 999), new int2(-850, 300) };
            using var range = new NativeReference<int2>(Allocator.TempJob);
            foreach (var column in columns)
            {
                new ColumnHeightJob
                {
                    Params = p,
                    Column = column,
                    HeightRange = range,
                }
                    .Schedule()
                    .Complete();
                var uniform = 0;
                for (var cy = 0; cy < WorldConstants.ChunksY; cy++)
                {
                    var kind = TerrainGenerator.Classify(p, range.Value, cy, out var id);
                    if (kind == ChunkKind.Mixed)
                    {
                        continue;
                    }

                    uniform++;
                    var voxels = TerrainGenerator.GenerateChunk(
                        p,
                        new ChunkCoord(column.x, cy, column.y),
                        Allocator.Persistent
                    );
                    try
                    {
                        Assert.That(voxels.IsUniform, Is.True, $"column {column}, y {cy}");
                        Assert.That(voxels.UniformId, Is.EqualTo(id), $"column {column}, y {cy}");
                    }
                    finally
                    {
                        voxels.Dispose();
                    }
                }

                Assert.That(uniform, Is.GreaterThan(110), $"column {column}");
            }

            Assert.That(Id(settings.Water).IsAir, Is.False);
        }

        [Test]
        public void Terrain_ChunkGenJob_MatchesGenerateChunk()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            using var scratch = new NativeArray<ushort>(
                WorldConstants.VoxelsPerChunk,
                Allocator.TempJob
            );
            using var result = new NativeReference<ChunkVoxels>(Allocator.TempJob);
            foreach (var coord in SurfaceChunks(p))
            {
                new ChunkGenJob
                {
                    Params = p,
                    Coord = coord,
                    Scratch = scratch,
                    Result = result,
                }
                    .Schedule()
                    .Complete();
                var voxels = result.Value;
                using var expected = Dense(p, coord);
                using var actual = new NativeArray<ushort>(
                    WorldConstants.VoxelsPerChunk,
                    Allocator.Temp
                );
                voxels.CopyTo(actual);
                voxels.Dispose();

                var differing = 0;
                for (var i = 0; i < actual.Length; i++)
                {
                    differing += actual[i] != expected[i] ? 1 : 0;
                }

                // Burst may round noise differently from managed code by a hair; a surface
                // voxel may flip, nothing more.
                Assert.That(differing, Is.LessThan(64), $"chunk {coord}");
            }
        }

        private static VoxelId Id(VoxelType type) => type.Id;

        private static NativeArray<ushort> Dense(in TerrainParams p, ChunkCoord coord)
        {
            var dense = new NativeArray<ushort>(WorldConstants.VoxelsPerChunk, Allocator.Temp);
            TerrainGenerator.FillChunk(p, coord, dense);
            return dense;
        }

        // Five chunks around the surface at the origin.
        private static ChunkCoord[] SurfaceChunks(in TerrainParams p)
        {
            var cy = TerrainGenerator.SurfaceHeight(p, 0, 0) >> WorldConstants.ChunkShift;
            return new[]
            {
                new ChunkCoord(0, cy, 0),
                new ChunkCoord(0, cy - 1, 0),
                new ChunkCoord(0, cy + 1, 0),
                new ChunkCoord(1, cy, 0),
                new ChunkCoord(0, cy, -1),
            };
        }
    }
}
