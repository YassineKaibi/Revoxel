using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.World
{
    // How a voxel type treats sky light (D-097): solid opaque voxels block it, leaves filter
    // it, everything else (Air, water, ground plants) lets it pass.
    public static class SkyClass
    {
        public const byte Pass = 0;
        public const byte Filter = 1;
        public const byte Block = 2;
    }

    // Per-voxel sky light (D-097). Every loaded chunk column keeps two sky heights per voxel
    // column, in voxels: above its highest blocker or filter (any) and above its highest
    // blocker (full); 0 when there is none. A voxel at height y is lit 1 when y >= any, by
    // the canopy value when y >= full, and 0 below a blocker; the light then spreads
    // sideways per level with a linear falloff over Reach voxels.
    public static class SkyLight
    {
        // Structure: the patch around a chunk must stay inside its eight neighbor columns.
        public const int Reach = 16;
        public const int Inner = WorldConstants.PaddedSize;
        public const int PatchSize = Inner + 2 * Reach;
        public const int PatchCells = PatchSize * PatchSize;
        public const int ColumnCells = WorldConstants.ChunkSize * WorldConstants.ChunkSize;
        public const byte Full = 255;
        public const int Decay = Full / Reach;

        // Corner light is stored in 16 steps.
        public const int Steps = 15;

        public static int ColumnIndex(int x, int z) => x + WorldConstants.ChunkSize * z;

        // Patch cells hold padded local x and z from -1 - Reach to ChunkSize + Reach.
        public static int PatchIndex(int localX, int localZ) =>
            (localX + 1 + Reach) + PatchSize * (localZ + 1 + Reach);

        // The light of a voxel column at one level, before the sideways spread.
        public static int Seed(int y, int any, int full, int canopy) =>
            y >= any ? Full
            : y >= full ? canopy
            : 0;

        // A buffer of full light for meshes without sky data (coarse chunks, tests).
        public static NativeArray<byte> CreateFullLight(Allocator allocator)
        {
            var light = new NativeArray<byte>(
                WorldConstants.PaddedVoxels,
                allocator,
                NativeArrayOptions.UninitializedMemory
            );
            for (var i = 0; i < light.Length; i++)
            {
                light[i] = Full;
            }

            return light;
        }

        // The local heights (0 to 32) above the highest filter-or-blocker and the highest
        // blocker of each voxel column of a chunk; 0 when there is none.
        public static void ChunkTops(
            in ChunkVoxels voxels,
            NativeArray<byte> skyClasses,
            NativeArray<byte> topAny,
            NativeArray<byte> topFull
        )
        {
            const int size = WorldConstants.ChunkSize;
            if (voxels.IsUniform)
            {
                var c = skyClasses[voxels.UniformId.Value];
                var any = (byte)(c != SkyClass.Pass ? size : 0);
                var full = (byte)(c == SkyClass.Block ? size : 0);
                for (var i = 0; i < ColumnCells; i++)
                {
                    topAny[i] = any;
                    topFull[i] = full;
                }

                return;
            }

            for (var z = 0; z < size; z++)
            for (var x = 0; x < size; x++)
            {
                byte any = 0;
                byte full = 0;
                for (var y = size - 1; y >= 0; y--)
                {
                    var c = skyClasses[voxels.Get(Coords.LocalIndex(x, y, z)).Value];
                    if (c == SkyClass.Pass)
                    {
                        continue;
                    }

                    if (any == 0)
                    {
                        any = (byte)(y + 1);
                    }

                    if (c == SkyClass.Block)
                    {
                        full = (byte)(y + 1);
                        break;
                    }
                }

                var i = ColumnIndex(x, z);
                topAny[i] = any;
                topFull[i] = full;
            }
        }

        // ChunkTops for a dense chunk (Coords.LocalIndex order).
        public static void DenseTops(
            NativeArray<ushort> dense,
            NativeArray<byte> skyClasses,
            NativeArray<byte> topAny,
            NativeArray<byte> topFull
        )
        {
            const int size = WorldConstants.ChunkSize;
            for (var z = 0; z < size; z++)
            for (var x = 0; x < size; x++)
            {
                byte any = 0;
                byte full = 0;
                for (var y = size - 1; y >= 0; y--)
                {
                    var c = skyClasses[dense[Coords.LocalIndex(x, y, z)]];
                    if (c == SkyClass.Pass)
                    {
                        continue;
                    }

                    if (any == 0)
                    {
                        any = (byte)(y + 1);
                    }

                    if (c == SkyClass.Block)
                    {
                        full = (byte)(y + 1);
                        break;
                    }
                }

                var i = ColumnIndex(x, z);
                topAny[i] = any;
                topFull[i] = full;
            }
        }

        // Spreads the light of one level over the patch: the larger of a cell's seed and a
        // neighbor's light minus Decay per voxel step (an L1 distance), in four sweeps.
        public static void Spread(NativeArray<int> grid)
        {
            for (var z = 0; z < PatchSize; z++)
            {
                var row = z * PatchSize;
                for (var x = 1; x < PatchSize; x++)
                {
                    grid[row + x] = math.max(grid[row + x], grid[row + x - 1] - Decay);
                }

                for (var x = PatchSize - 2; x >= 0; x--)
                {
                    grid[row + x] = math.max(grid[row + x], grid[row + x + 1] - Decay);
                }
            }

            for (var x = 0; x < PatchSize; x++)
            {
                for (var z = 1; z < PatchSize; z++)
                {
                    var i = x + z * PatchSize;
                    grid[i] = math.max(grid[i], grid[i - PatchSize] - Decay);
                }

                for (var z = PatchSize - 2; z >= 0; z--)
                {
                    var i = x + z * PatchSize;
                    grid[i] = math.max(grid[i], grid[i + PatchSize] - Decay);
                }
            }
        }
    }

    // Finds the sky tops of a freshly generated chunk (chained after its ChunkGenJob): from
    // the job's dense scratch when the result is mixed, from its id when uniform. Reads no
    // container nested in the result.
    [BurstCompile]
    public struct SkyTopsJob : IJob
    {
        [ReadOnly]
        public NativeReference<ChunkVoxels> Voxels;

        // The ChunkGenJob's dense output (valid when the result is mixed).
        [ReadOnly]
        public NativeArray<ushort> Dense;

        [ReadOnly]
        public NativeArray<byte> SkyClasses;

        public NativeArray<byte> TopAny;

        public NativeArray<byte> TopFull;

        public void Execute()
        {
            var voxels = Voxels.Value;
            if (voxels.IsUniform)
            {
                SkyLight.ChunkTops(voxels, SkyClasses, TopAny, TopFull);
                return;
            }

            SkyLight.DenseTops(Dense, SkyClasses, TopAny, TopFull);
        }
    }

    // Copies the sky heights of a chunk's 3 x 3 columns into its patch, relative to the
    // chunk's lowest voxel. Runs on the main thread with Run().
    [BurstCompile]
    public struct SkyPatchJob : IJob
    {
        // Nine columns, index (dx + 1) + 3 * (dz + 1), each SkyLight.ColumnCells of any
        // heights followed by as many full heights. A missing column holds zeros (open).
        [ReadOnly]
        public NativeArray<ushort> Columns;

        public int OriginY;

        [WriteOnly]
        public NativeArray<int> PatchAny;

        [WriteOnly]
        public NativeArray<int> PatchFull;

        public void Execute()
        {
            const int size = WorldConstants.ChunkSize;
            for (var pz = 0; pz < SkyLight.PatchSize; pz++)
            for (var px = 0; px < SkyLight.PatchSize; px++)
            {
                var lx = px - 1 - SkyLight.Reach;
                var lz = pz - 1 - SkyLight.Reach;
                var dx =
                    lx < 0 ? -1
                    : lx >= size ? 1
                    : 0;
                var dz =
                    lz < 0 ? -1
                    : lz >= size ? 1
                    : 0;
                var column = (dx + 1) + 3 * (dz + 1);
                var cell = SkyLight.ColumnIndex(lx - dx * size, lz - dz * size);
                var start = column * 2 * SkyLight.ColumnCells;
                var i = px + SkyLight.PatchSize * pz;
                PatchAny[i] = Columns[start + cell] - OriginY;
                PatchFull[i] = Columns[start + SkyLight.ColumnCells + cell] - OriginY;
            }
        }
    }

    // The sky light of every voxel of a padded chunk (D-097), 0 to 255, in PaddedIndex
    // order. Heights are relative to the chunk's lowest voxel.
    [BurstCompile]
    public struct SkyLightJob : IJob
    {
        [ReadOnly]
        public NativeArray<int> PatchAny;

        [ReadOnly]
        public NativeArray<int> PatchFull;

        // Seed light under filters only, 0 to 255.
        public int Canopy;

        [WriteOnly]
        public NativeArray<byte> Light;

        public void Execute()
        {
            var maxAny = int.MinValue;
            var minFull = int.MaxValue;
            for (var i = 0; i < SkyLight.PatchCells; i++)
            {
                maxAny = math.max(maxAny, PatchAny[i]);
                minFull = math.min(minFull, PatchFull[i]);
            }

            var grid = new NativeArray<int>(
                SkyLight.PatchCells,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory
            );
            const int last = WorldConstants.ChunkSize;
            for (var y = -1; y <= last; y++)
            {
                if (y >= maxAny || y < minFull)
                {
                    var value = (byte)(y >= maxAny ? SkyLight.Full : 0);
                    for (var z = -1; z <= last; z++)
                    for (var x = -1; x <= last; x++)
                    {
                        Light[Coords.PaddedIndex(x, y, z)] = value;
                    }

                    continue;
                }

                for (var i = 0; i < SkyLight.PatchCells; i++)
                {
                    grid[i] = SkyLight.Seed(y, PatchAny[i], PatchFull[i], Canopy);
                }

                SkyLight.Spread(grid);
                for (var z = -1; z <= last; z++)
                for (var x = -1; x <= last; x++)
                {
                    Light[Coords.PaddedIndex(x, y, z)] = (byte)
                        math.clamp(grid[SkyLight.PatchIndex(x, z)], 0, SkyLight.Full);
                }
            }

            grid.Dispose();
        }
    }
}
