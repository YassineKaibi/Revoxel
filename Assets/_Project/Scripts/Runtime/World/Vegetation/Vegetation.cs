using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Trees and ground plants in the generator (PLAN-06). Burst-callable, pure functions of
    // the parameters and the coordinates.
    //
    // Full resolution (D-084): the height job of a chunk column gathers every tree whose
    // reach overlaps the column, grows it and keeps the parts that overlap the column; the
    // column's chunk jobs voxelize those parts over the terrain (only at or above each
    // column's ground: wood over everything, leaves before it). A tree spanning several
    // columns is grown once per column, always the same way, so chunks meet without seams.
    // Ground plants (D-087) are per voxel column. Distance detail (D-088) draws each tree's
    // plan at the coarse voxel size: trunk, and a crown envelope.
    public static class Vegetation
    {
        // The parts of every tree reaching into a chunk column, and their height range
        // (min inclusive, max exclusive; empty as (MaxValue, MinValue)).
        public static void CollectColumn(
            in TerrainParams p,
            int2 column,
            NativeList<TreePart> parts,
            out int2 heights
        )
        {
            parts.Clear();
            heights = new int2(int.MaxValue, int.MinValue);
            if (!p.Vegetation.HasTrees)
            {
                return;
            }

            var min = column * WorldConstants.ChunkSize;
            var max = min + WorldConstants.ChunkMask;
            var reach = (int)math.ceil(p.Vegetation.MaxReach);
            var anchors = new NativeList<TreeAnchor>(64, Allocator.Temp);
            var tree = new NativeList<TreePart>(512, Allocator.Temp);
            var scratch = TreeScratch.Create(Allocator.Temp);
            TreeAnchors.Gather(p, min - reach, max + reach, anchors);
            for (var i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                var species = p.Vegetation.Species[anchor.Species];
                var gap = math.max(min - anchor.Column, anchor.Column - max);
                if (math.any(gap > (int)math.ceil(species.Reach)))
                {
                    continue;
                }

                if (!TreeAnchors.TryPlan(p, anchor, out var plan))
                {
                    continue;
                }

                tree.Clear();
                TreeGrowth.Grow(p, plan, ref scratch, tree);
                for (var k = 0; k < tree.Length; k++)
                {
                    var part = tree[k];
                    if (
                        part.Max.x < min.x
                        || part.Min.x > max.x
                        || part.Max.z < min.y
                        || part.Min.z > max.y
                    )
                    {
                        continue;
                    }

                    parts.Add(part);
                    heights = new int2(
                        math.min(heights.x, part.Min.y),
                        math.max(heights.y, part.Max.y + 1)
                    );
                }
            }

            scratch.Dispose();
            tree.Dispose();
            anchors.Dispose();
        }

        // Every tree whose anchor column lies in [min, max], grown (for tests and probes).
        public static void CollectTrees(
            in TerrainParams p,
            int2 min,
            int2 max,
            NativeList<TreePlan> plans
        )
        {
            plans.Clear();
            var anchors = new NativeList<TreeAnchor>(64, Allocator.Temp);
            TreeAnchors.Gather(p, min, max, anchors);
            for (var i = 0; i < anchors.Length; i++)
            {
                if (TreeAnchors.TryPlan(p, anchors[i], out var plan))
                {
                    plans.Add(plan);
                }
            }

            anchors.Dispose();
        }

        // Voxelizes the parts into a chunk whose min corner is origin: leaves first, then
        // wood over them; nothing below a column's ground (samples, the chunk's sample grid)
        // unless allAbove says the whole chunk lies above the ground.
        public static void Rasterize(
            in TerrainParams p,
            NativeArray<TreePart> parts,
            int3 origin,
            NativeArray<TerrainSample> samples,
            bool allAbove,
            NativeArray<ushort> dense
        )
        {
            if (parts.Length == 0)
            {
                return;
            }

            const int size = WorldConstants.ChunkSize;
            var ground = new NativeArray<int>(size * size, Allocator.Temp);
            for (var z = 0; z < size; z++)
            for (var x = 0; x < size; x++)
            {
                ground[x + size * z] = allAbove
                    ? int.MinValue
                    : (int)
                        math.floor(
                            samples[(x + 1) + TerrainGenerator.SampleGridSize * (z + 1)].Height
                        );
            }

            for (var pass = 0; pass < 2; pass++)
            {
                var kind = pass == 0 ? TreePartKind.Leaves : TreePartKind.Wood;
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (part.Kind != kind)
                    {
                        continue;
                    }

                    var lo = math.max(part.Min, origin) - origin;
                    var hi = math.min(part.Max, origin + WorldConstants.ChunkMask) - origin;
                    if (math.any(lo > hi))
                    {
                        continue;
                    }

                    for (var y = lo.y; y <= hi.y; y++)
                    for (var z = lo.z; z <= hi.z; z++)
                    for (var x = lo.x; x <= hi.x; x++)
                    {
                        var wy = origin.y + y;
                        if (wy < ground[x + size * z])
                        {
                            continue;
                        }

                        var at = new float3(origin.x + x, wy, origin.z + z) + 0.5f;
                        var index = Coords.LocalIndex(x, y, z);
                        var id = Voxel(p, part, at, dense[index]);
                        if (id != 0)
                        {
                            dense[index] = id;
                        }
                    }
                }
            }

            ground.Dispose();
        }

        // The id a part puts at a voxel center, or 0 for none. Wood: inside the tapered
        // capsule, the core inside the bark layer; bark never replaces another part's core.
        // Leaves: inside the ellipsoid shrunk by a lumpy noise, minus noise holes outside
        // the cluster's middle.
        public static ushort Voxel(in TerrainParams p, in TreePart part, float3 at, ushort current)
        {
            ref readonly var v = ref p.Vegetation;
            if (part.Kind == TreePartKind.Wood)
            {
                var ab = part.B - part.A;
                var length2 = math.lengthsq(ab);
                var t = length2 > 0f ? math.saturate(math.dot(at - part.A, ab) / length2) : 0f;
                var r = math.lerp(part.RA, part.RB, t);
                var d2 = math.distancesq(at, part.A + ab * t);
                if (d2 > r * r)
                {
                    return 0;
                }

                var core = r - v.Bark;
                if (core >= 0.5f && d2 <= core * core)
                {
                    return part.Inner;
                }

                return current == part.Inner ? (ushort)0 : part.Outer;
            }

            var q = (at - part.A) / part.B;
            var d = math.length(q);
            if (d >= 1f + v.LeafLumps)
            {
                return 0;
            }

            // The edge moves in and out with a smooth noise (lumps, D-089); the middle
            // stays dense and only the outer shell has holes.
            var n = noise.snoise(at * v.LeafFrequency + v.LeafOffset);
            if (d >= 1f + v.LeafLumps * n)
            {
                return 0;
            }

            if (d > 0.7f && part.Porosity > 0f)
            {
                var hole = noise.snoise(at * (v.LeafFrequency * 1.9f) + v.LeafOffset.zxy + 37f);
                if (hole < -0.55f + 1.1f * part.Porosity)
                {
                    return 0;
                }
            }

            return part.Outer;
        }

        // A ground plant's bend: none, or the face neighbor its stem leans into.
        public const int BendNone = 0;

        // The column offset of bend direction 1 to 4 (+x, -x, +z, -z).
        public static int2 BendOffset(int bend) =>
            bend switch
            {
                1 => new int2(1, 0),
                2 => new int2(-1, 0),
                3 => new int2(0, 1),
                4 => new int2(0, -1),
                _ => int2.zero,
            };

        // The ground plant standing on a column, if any (D-087): its stem and head and its
        // height in voxels (0 for none). Plants are tried in order, each in clumps from a
        // smooth noise; they need soil (not bare rock, snow, or shore sand unless allowed),
        // and only plants that stand in water grow under it, rising above its surface. A
        // stem may bend (D-089): from bendAt voxels up it continues in the face neighbor
        // `bend` points to (one voxel lower there, so the blade stays face-connected).
        // slope: the column's slope, or NaN to compute it when needed.
        public static int PlantAt(
            in TerrainParams p,
            in TerrainSample sample,
            int x,
            int z,
            float slope,
            out ushort stem,
            out ushort head,
            out int bend,
            out int bendAt
        )
        {
            stem = 0;
            head = 0;
            bend = BendNone;
            bendAt = 0;
            ref readonly var v = ref p.Vegetation;
            var biome = sample.Biome.Value;
            if (biome >= v.Biomes.Length)
            {
                return 0;
            }

            var b = v.Biomes[biome];
            if (b.PlantCount == 0)
            {
                return 0;
            }

            var h = (int)math.floor(sample.Height);
            var depth = sample.WaterLevel - h;
            var random = Random.CreateFromIndex(
                math.hash(new int3(x, z, (int)(v.Seed ^ 0x3C6EF372u))) | 1u
            );
            for (var k = 0; k < b.PlantCount; k++)
            {
                var plant = v.Plants[b.PlantStart + k];
                var chance = random.NextFloat();
                var size = random.NextFloat();
                var offset = new float2(91.7f * (k + 1), 53.1f * (k + 1)) + v.LeafOffset.xz;
                var clump = math.smoothstep(
                    0.3f,
                    0.75f,
                    0.5f
                        + 0.5f
                            * noise.snoise(
                                new float2(x + 0.5f, z + 0.5f) * plant.ClumpFrequency + offset
                            )
                );
                if (chance >= plant.Cover * clump)
                {
                    continue;
                }

                var height = (int)math.round(math.lerp(plant.Height.x, plant.Height.y, size));
                if (depth > 0)
                {
                    if (depth > plant.MaxWaterDepth || depth + 2 > plant.Height.y)
                    {
                        continue;
                    }

                    height = math.max(height, depth + 2);
                }

                if (math.isnan(slope))
                {
                    slope = TerrainGenerator.FineSlope(p, x, z);
                }

                var column = TerrainGenerator.Column(p, x, z, sample, slope);
                var top = column.Top;
                if (top == 0 || top == p.Snow)
                {
                    return 0;
                }

                if (depth <= 0 && top == p.Biomes[biome].Shore && !plant.OnShore)
                {
                    continue;
                }

                stem = plant.Stem;
                head = plant.Head;
                var bendChance = random.NextFloat();
                var direction = random.NextInt(1, 5);
                var at = random.NextInt(1, math.max(2, height - 1));
                if (height >= 3 && bendChance < plant.Bend)
                {
                    bend = direction;
                    bendAt = math.max(at, math.max(1, depth + 1));
                    if (bendAt >= height)
                    {
                        bend = BendNone;
                        bendAt = 0;
                    }
                }

                return height;
            }

            return 0;
        }

        // Distance detail (D-088): trees drawn from their plans into the padded input of a
        // level-L coarse chunk (origin in voxels, s voxels per coarse voxel), only in cells
        // the terrain leaves empty and never in side-wall padding.
        public static void FillDetail(
            in TerrainParams p,
            int level,
            int3 origin,
            bool sideWalls,
            NativeArray<TerrainSample> samples,
            NativeArray<ushort> padded
        )
        {
            ref readonly var v = ref p.Vegetation;
            if (level < 1 || level > v.DetailMaxLevel || !v.HasTrees)
            {
                return;
            }

            const int size = WorldConstants.ChunkSize;
            const int grid = TerrainGenerator.DetailSampleGridSize;
            var s = 1 << level;
            var half = s >> 1;
            var min = origin.xz - s;
            var max = origin.xz + (size + 1) * s - 1;
            var reach = (int)math.ceil(v.MaxReach);
            var anchors = new NativeList<TreeAnchor>(256, Allocator.Temp);
            TreeAnchors.Gather(p, min - reach, max + reach, anchors);
            var top = origin.y + (size + 1) * s;
            for (var i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                var species = v.Species[anchor.Species];
                var gap = math.max(min - anchor.Column, anchor.Column - max);
                if (math.any(gap > (int)math.ceil(species.Reach)))
                {
                    continue;
                }

                if (!TreeAnchors.TryPlan(p, anchor, out var plan))
                {
                    continue;
                }

                if (plan.Base.y > top || plan.Base.y + species.Top < origin.y - s)
                {
                    continue;
                }

                var spread =
                    math.max(plan.CrownRadii.x, plan.CrownRadii.z) * 1.2f + plan.TrunkRadius + s;
                var tip = plan.Base.xz + plan.Lean;
                var lo = new float3(
                    math.min(math.min(plan.Base.x, tip.x), plan.CrownCenter.x) - spread,
                    plan.Base.y,
                    math.min(math.min(plan.Base.z, tip.y), plan.CrownCenter.z) - spread
                );
                var hi = new float3(
                    math.max(math.max(plan.Base.x, tip.x), plan.CrownCenter.x) + spread,
                    math.max(
                        plan.Base.y + 2f + plan.Height,
                        plan.CrownCenter.y + plan.CrownRadii.y * 1.2f
                    ) + s,
                    math.max(math.max(plan.Base.z, tip.y), plan.CrownCenter.z) + spread
                );
                var c0 = math.max((int3)math.floor((lo - origin) / s), -1);
                var c1 = math.min((int3)math.floor((hi - origin) / s), size);
                for (var z = c0.z; z <= c1.z; z++)
                for (var x = c0.x; x <= c1.x; x++)
                {
                    if (sideWalls && (x < 0 || x >= size || z < 0 || z >= size))
                    {
                        continue;
                    }

                    var ground = (int)math.floor(samples[(x + 2) + grid * (z + 2)].Height);
                    for (var y = c0.y; y <= c1.y; y++)
                    {
                        var y0 = origin.y + y * s;
                        if (y0 + half < ground)
                        {
                            continue;
                        }

                        var at = new float3(origin.x + x * s, y0, origin.z + z * s) + half;
                        var id = DetailVoxel(p, plan, species, at, s, level);
                        if (id != 0)
                        {
                            padded[Coords.PaddedIndex(x, y, z)] = id;
                        }
                    }
                }
            }

            anchors.Dispose();
        }

        // The y range (min inclusive, max exclusive) trees take in a coarse cell, voxels.
        public static int2 DetailHeights(in TerrainParams p, int2 cell, int level)
        {
            ref readonly var v = ref p.Vegetation;
            var heights = new int2(int.MaxValue, int.MinValue);
            if (level < 1 || level > v.DetailMaxLevel || !v.HasTrees)
            {
                return heights;
            }

            var s = 1 << level;
            var min = cell * (WorldConstants.ChunkSize * s) - s;
            var max = min + (WorldConstants.ChunkSize + 2) * s - 1;
            var reach = (int)math.ceil(v.MaxReach);
            var anchors = new NativeList<TreeAnchor>(256, Allocator.Temp);
            TreeAnchors.Gather(p, min - reach, max + reach, anchors);
            for (var i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                var species = v.Species[anchor.Species];
                var gap = math.max(min - anchor.Column, anchor.Column - max);
                if (math.any(gap > (int)math.ceil(species.Reach)))
                {
                    continue;
                }

                if (TreeAnchors.TryPlan(p, anchor, out var plan))
                {
                    heights = new int2(
                        math.min(heights.x, (int)math.floor(plan.Base.y)),
                        math.max(heights.y, (int)math.ceil(plan.Base.y + species.Top) + 1)
                    );
                }
            }

            anchors.Dispose();
            return heights;
        }

        // A tree's plan at a coarse voxel center: the trunk (thickened to half a coarse
        // voxel, drawn up to 1 m voxels) and the crown envelope, a little lumpy.
        private static ushort DetailVoxel(
            in TerrainParams p,
            in TreePlan plan,
            in TreeSpeciesParams s,
            float3 at,
            int size,
            int level
        )
        {
            var leaves = s.Leaves;
            var top = new float3(
                plan.Base.x + plan.Lean.x,
                plan.Base.y + 2f + plan.Height,
                plan.Base.z + plan.Lean.y
            );
            var trunkTop = s.Form == TreeForm.Broadleaf ? plan.CrownCenter : top;
            if (level <= 3)
            {
                var ab = trunkTop - plan.Base;
                var t = math.saturate(
                    math.dot(at - plan.Base, ab) / math.max(math.lengthsq(ab), 1e-3f)
                );
                var r = math.max(plan.TrunkRadius * (1f - 0.5f * t), 0.5f * size);
                if (math.distancesq(at, plan.Base + ab * t) <= r * r)
                {
                    return s.Bark;
                }
            }

            if (leaves == 0)
            {
                return 0;
            }

            var lumps =
                0.15f
                * noise.snoise(at * (p.Vegetation.LeafFrequency * 0.5f) + p.Vegetation.LeafOffset);
            switch (s.Form)
            {
                case TreeForm.Conifer:
                {
                    var t = (at.y - plan.CrownCenter.y) / plan.CrownRadii.y;
                    if (t < 0f || t > 1f)
                    {
                        return 0;
                    }

                    var axis = math.lerp(plan.CrownCenter.xz, top.xz, t);
                    var r = plan.CrownRadii.x * (1f - t) * (0.85f + lumps);
                    return math.distancesq(at.xz, axis) <= r * r ? leaves : (ushort)0;
                }

                default:
                {
                    var q = (at - plan.CrownCenter) / (plan.CrownRadii * 0.9f);
                    return math.length(q) < 1f + lumps ? leaves : (ushort)0;
                }
            }
        }
    }
}
