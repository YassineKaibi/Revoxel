using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

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
            var grass = Id(settings.Grass).Value;
            var seaLevel = (int)math.round(settings.SeaLevelMeters * WorldConstants.VoxelsPerMeter);
            var chunkYs = new List<int>();
            for (var cy = 0; cy <= seaLevel / WorldConstants.ChunkSize + 2; cy++)
            {
                chunkYs.Add(cy);
            }

            chunkYs.Add(WorldConstants.ChunksY - 1);
            foreach (var cy in chunkYs)
            {
                var coord = new ChunkCoord(999, cy, 999);
                using var dense = Dense(p, coord);
                for (var i = 0; i < dense.Length; i++)
                {
                    var y = Coords.LocalFromIndex(i).y + cy * WorldConstants.ChunkSize;
                    var expected = y < seaLevel ? water : VoxelId.Air.Value;

                    Assert.That(dense[i], Is.Not.EqualTo(grass));
                    Assert.That(dense[i], Is.EqualTo(expected), $"chunk {coord}, voxel y {y}");
                }
            }
        }

        [Test]
        public void Terrain_SoilDepth_Varies()
        {
            var steep = Object.Instantiate(WorldAssets.LoadTerrainSettings());
            try
            {
                var so = new SerializedObject(steep);
                so.FindProperty("_baseScaleMeters").floatValue = 60f;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(steep.TryValidate(out var error), Is.True, error);
                var p = TerrainParams.Create(new WorldSeed(Seeds.Fixture), steep);

                var depths = new HashSet<int>();
                long steepSum = 0,
                    steepCount = 0,
                    flatSum = 0,
                    flatCount = 0;
                for (var gz = 0; gz < 64; gz++)
                for (var gx = 0; gx < 64; gx++)
                {
                    var x = gx * 37;
                    var z = gz * 37;
                    var h = TerrainGenerator.SurfaceHeight(p, x, z);
                    if (h <= p.SeaLevel + p.SandAbove)
                    {
                        continue;
                    }

                    var soil = TerrainGenerator.SoilDepth(p, x, z);
                    depths.Add(soil);
                    var rise = math.max(
                        math.max(
                            math.abs(TerrainGenerator.SurfaceHeight(p, x + 1, z) - h),
                            math.abs(TerrainGenerator.SurfaceHeight(p, x - 1, z) - h)
                        ),
                        math.max(
                            math.abs(TerrainGenerator.SurfaceHeight(p, x, z + 1) - h),
                            math.abs(TerrainGenerator.SurfaceHeight(p, x, z - 1) - h)
                        )
                    );
                    if (rise >= 2)
                    {
                        steepSum += soil;
                        steepCount++;
                    }
                    else
                    {
                        flatSum += soil;
                        flatCount++;
                    }
                }

                Assert.That(depths.Count, Is.GreaterThanOrEqualTo(4));
                Assert.That(steepCount, Is.GreaterThan(10), "too few steep columns sampled");
                Assert.That(flatCount, Is.GreaterThan(10), "too few flat columns sampled");
                Assert.That(
                    (double)steepSum / steepCount,
                    Is.LessThan((double)flatSum / flatCount)
                );
            }
            finally
            {
                Object.DestroyImmediate(steep);
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
