using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    public sealed class DetailGenerationTests
    {
        private const int Size = WorldConstants.ChunkSize;
        private const int WallDepth = 8;

        [Test]
        public void DetailGen_Level0MatchesFullResolution()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var cache = new Dictionary<ChunkCoord, ushort[]>();
            foreach (var coord in SurfaceChunks(p, 0))
            {
                var padded = FillManaged(p, 0, coord, false);
                var origin = Coords.ChunkOrigin(coord);
                for (var y = -1; y <= Size; y++)
                for (var z = -1; z <= Size; z++)
                for (var x = -1; x <= Size; x++)
                {
                    var voxel = origin + new int3(x, y, z);
                    Coords.WorldToChunk(voxel, out var chunk, out var local);
                    if (!cache.TryGetValue(chunk, out var dense))
                    {
                        using var array = new NativeArray<ushort>(
                            WorldConstants.VoxelsPerChunk,
                            Allocator.Temp
                        );
                        TerrainGenerator.FillChunk(p, chunk, array);
                        dense = array.ToArray();
                        cache.Add(chunk, dense);
                    }

                    Assert.That(
                        padded[Coords.PaddedIndex(x, y, z)],
                        Is.EqualTo(dense[Coords.LocalIndex(local)]),
                        $"chunk {coord}, padded ({x}, {y}, {z})"
                    );
                }
            }
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(6)]
        public void DetailGen_SolidMatchesSurfaceAtCoarseCenters(int level)
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var s = 1 << level;
            foreach (var coord in SurfaceChunks(p, level))
            {
                // Managed on both sides: Burst may round the noise differently (R-21).
                var padded = FillManaged(p, level, coord, false);
                var origin = coord.Value * (Size * s);
                for (var z = 0; z < Size; z++)
                for (var x = 0; x < Size; x++)
                {
                    var h = TerrainGenerator.SurfaceHeight(
                        p,
                        origin.x + x * s + s / 2,
                        origin.z + z * s + s / 2
                    );
                    for (var y = 0; y < Size; y++)
                    {
                        var id = padded[Coords.PaddedIndex(x, y, z)];
                        var expected = origin.y + y * s + s / 2 < h;
                        Assert.That(
                            IsSolid(p, id),
                            Is.EqualTo(expected),
                            $"level {level}, chunk {coord}, ({x}, {y}, {z}), surface {h}"
                        );
                    }
                }
            }
        }

        [Test]
        public void DetailGen_TopCoarseVoxelKeepsSurfaceMaterial()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var checkedColumns = 0;
            var cache = new Dictionary<(int, ChunkCoord), ushort[]>();
            for (var level = 1; level <= 6; level++)
            {
                var s = 1 << level;
                foreach (var cell in new[] { new int2(0, 0), new int2(1, -1) })
                {
                    var originXZ = cell * (Size * s);
                    for (var z = 0; z < Size; z += 3)
                    for (var x = 0; x < Size; x += 3)
                    {
                        var wx = originXZ.x + x * s + s / 2;
                        var wz = originXZ.y + z * s + s / 2;
                        var h = TerrainGenerator.SurfaceHeight(p, wx, wz);
                        if (h <= p.SeaLevel)
                        {
                            continue;
                        }

                        // The top solid coarse voxel: the highest y0 with y0 + s/2 < h.
                        var top = (h - 1 - s / 2) / s;
                        var coord = new ChunkCoord(cell.x, top / Size, cell.y);
                        if (!cache.TryGetValue((level, coord), out var padded))
                        {
                            padded = FillManaged(p, level, coord, false);
                            cache.Add((level, coord), padded);
                        }

                        var id = padded[Coords.PaddedIndex(x, top % Size, z)];
                        var soil = TerrainGenerator.SoilDepth(p, wx, wz);
                        var sandy = h <= p.SeaLevel + p.SandAbove;
                        var surface = TerrainGenerator.MaterialAt(p, h - 1, h, soil, sandy);
                        Assert.That(id, Is.EqualTo(surface), $"level {level} column ({wx}, {wz})");
                        checkedColumns++;
                    }
                }
            }

            Assert.That(checkedColumns, Is.GreaterThan(100), "too few land columns checked");
        }

        [Test]
        public void DetailGen_SideWallsOpenOnlyTheSides()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            foreach (var level in new[] { 0, 2, 5 })
            {
                foreach (var coord in SurfaceChunks(p, level))
                {
                    var open = FillJob(p, level, coord, false);
                    var walls = FillJob(p, level, coord, true);
                    for (var y = -1; y <= Size; y++)
                    for (var z = -1; z <= Size; z++)
                    for (var x = -1; x <= Size; x++)
                    {
                        var i = Coords.PaddedIndex(x, y, z);
                        var side = x < 0 || x >= Size || z < 0 || z >= Size;
                        var expected = side && IsSolid(p, open[i]) ? VoxelId.Air.Value : open[i];
                        Assert.That(
                            walls[i],
                            Is.EqualTo(expected),
                            $"level {level} ({x}, {y}, {z})"
                        );
                    }
                }
            }
        }

        [Test]
        public void DetailGen_SkippedChunksHaveNoSurface()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var skipped = 0;
            for (var level = 1; level <= 6; level++)
            {
                for (var cz = -1; cz <= 0; cz++)
                for (var cx = -1; cx <= 0; cx++)
                {
                    var cell = new int2(cx, cz);
                    var range = TerrainGenerator.DetailHeightRange(p, cell, level);
                    for (var cy = 0; cy < WorldConstants.ChunksY >> level; cy++)
                    {
                        if (TerrainGenerator.DetailChunkNeedsMesh(p, range, cy, level, WallDepth))
                        {
                            continue;
                        }

                        skipped++;
                        var padded = FillJob(p, level, new ChunkCoord(cx, cy, cz), false);
                        AssertNoFaces(p, padded, $"level {level} chunk ({cx}, {cy}, {cz})");
                    }
                }
            }

            Assert.That(skipped, Is.GreaterThan(0));
        }

        [Test]
        public void DetailGen_Deterministic()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            var q = WorldAssets.TerrainParams(Seeds.Other);
            var differs = false;
            foreach (var coord in SurfaceChunks(p, 3))
            {
                var a = FillJob(p, 3, coord, true);
                var b = FillJob(p, 3, coord, true);
                Assert.That(a, Is.EqualTo(b), $"chunk {coord}");
                differs |= !Equal(a, FillJob(q, 3, coord, true));
            }

            Assert.That(differs, Is.True);
        }

        // A chunk with no voxel that could get a face: every interior voxel is Air, or water
        // with water or an opaque voxel on every side, or solid with solid on every side.
        private static void AssertNoFaces(in TerrainParams p, ushort[] padded, string what)
        {
            for (var y = 0; y < Size; y++)
            for (var z = 0; z < Size; z++)
            for (var x = 0; x < Size; x++)
            {
                var a = padded[Coords.PaddedIndex(x, y, z)];
                if (a == VoxelId.Air.Value)
                {
                    continue;
                }

                for (var axis = 0; axis < 3; axis++)
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    var n = new int3(x, y, z);
                    n[axis] += sign;
                    var b = padded[Coords.PaddedIndex(n.x, n.y, n.z)];
                    if (!IsSolid(p, b) && a != b)
                    {
                        Assert.Fail($"{what}: face at ({x}, {y}, {z})");
                    }
                }
            }
        }

        private static bool IsSolid(in TerrainParams p, ushort id) =>
            id != VoxelId.Air.Value && id != p.Water;

        private static bool Equal(ushort[] a, ushort[] b)
        {
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        // The surface chunk at the origin, the one below, and three neighbors, at a level.
        private static ChunkCoord[] SurfaceChunks(in TerrainParams p, int level)
        {
            var s = 1 << level;
            var cy = TerrainGenerator.SurfaceHeight(p, s / 2, s / 2) / (Size * s);
            return new[]
            {
                new ChunkCoord(0, cy, 0),
                new ChunkCoord(0, cy - 1, 0),
                new ChunkCoord(1, cy, 0),
                new ChunkCoord(-1, cy, 0),
                new ChunkCoord(0, cy, -1),
            };
        }

        private static ushort[] FillManaged(
            in TerrainParams p,
            int level,
            ChunkCoord coord,
            bool walls
        )
        {
            using var heights = new NativeArray<float>(
                TerrainGenerator.DetailHeightGridCount,
                Allocator.Temp
            );
            using var padded = new NativeArray<ushort>(WorldConstants.PaddedVoxels, Allocator.Temp);
            TerrainGenerator.FillDetailPadded(p, level, coord, walls, heights, padded);
            return padded.ToArray();
        }

        private static ushort[] FillJob(in TerrainParams p, int level, ChunkCoord coord, bool walls)
        {
            using var heights = new NativeArray<float>(
                TerrainGenerator.DetailHeightGridCount,
                Allocator.TempJob
            );
            using var padded = new NativeArray<ushort>(
                WorldConstants.PaddedVoxels,
                Allocator.TempJob
            );
            new DetailFillJob
            {
                Params = p,
                Level = level,
                Coord = coord,
                SideWalls = walls,
                Heights = heights,
                Padded = padded,
            }.Run();
            return padded.ToArray();
        }
    }
}
