using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // A tree candidate of the anchor grid (D-084).
    public struct TreeAnchor
    {
        public int2 Column;
        public int Species;
        public uint Seed;
        public uint Priority;
        public float Spacing;
        public bool Accepted;
    }

    // Where trees stand (D-084): one candidate per cell of a fixed grid at a hashed column,
    // kept with the chance the biomes' tree cover gives there, then thinned: a candidate is
    // dropped when an accepted candidate of higher priority stands closer than the larger
    // of their spacings. Everything is a pure function of the seed and the cell, so every
    // chunk, column or coarse cell that looks at a tree finds the same one.
    public static class TreeAnchors
    {
        // Kept anchors whose column lies in [min, max] (inclusive voxel rectangle).
        public static void Gather(
            in TerrainParams p,
            int2 min,
            int2 max,
            NativeList<TreeAnchor> kept
        )
        {
            kept.Clear();
            ref readonly var v = ref p.Vegetation;
            if (!v.HasTrees || v.Biomes.Length == 0)
            {
                return;
            }

            var cell = v.Cell;
            var margin = (int)math.ceil(v.MaxSpacing / cell);
            var c0 = FloorDiv(min, cell);
            var c1 = FloorDiv(max, cell);
            var g0 = c0 - margin;
            var width = c1 - c0 + 1 + 2 * margin;
            var candidates = new NativeArray<TreeAnchor>(width.x * width.y, Allocator.Temp);
            for (var z = 0; z < width.y; z++)
            for (var x = 0; x < width.x; x++)
            {
                candidates[x + width.x * z] = Candidate(p, g0 + new int2(x, z));
            }

            for (var z = margin; z < width.y - margin; z++)
            for (var x = margin; x < width.x - margin; x++)
            {
                var a = candidates[x + width.x * z];
                if (!a.Accepted || math.any(a.Column < min) || math.any(a.Column > max))
                {
                    continue;
                }

                var keep = true;
                for (var dz = -margin; dz <= margin && keep; dz++)
                for (var dx = -margin; dx <= margin; dx++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }

                    var b = candidates[(x + dx) + width.x * (z + dz)];
                    if (!b.Accepted || !Outranks(b, a))
                    {
                        continue;
                    }

                    var spacing = math.max(a.Spacing, b.Spacing);
                    if (math.distancesq(a.Column, b.Column) < spacing * spacing)
                    {
                        keep = false;
                        break;
                    }
                }

                if (keep)
                {
                    kept.Add(a);
                }
            }

            candidates.Dispose();
        }

        // The candidate of one grid cell.
        public static TreeAnchor Candidate(in TerrainParams p, int2 cell)
        {
            ref readonly var v = ref p.Vegetation;
            var hash = math.hash(new int3(cell.x, cell.y, (int)(v.Seed ^ 0xA2C79E15u)));
            var random = Random.CreateFromIndex(hash);
            var column = cell * v.Cell + (int2)math.floor(random.NextFloat2(0.15f, 0.85f) * v.Cell);
            var anchor = new TreeAnchor
            {
                Column = column,
                Priority = random.NextUInt(),
                Species = -1,
            };
            var chance = random.NextFloat();
            var pick = random.NextFloat();
            anchor.Seed = random.NextUInt() | 1u;
            var weights = new FixedList128Bytes<float>();
            var biome = TerrainGenerator.BiomeAt(p, column.x, column.y, ref weights);
            if (biome.Value >= v.Biomes.Length)
            {
                return anchor;
            }

            var cover = 0f;
            for (var i = 0; i < weights.Length && i < v.Biomes.Length; i++)
            {
                cover += weights[i] * v.Biomes[i].TreeCover;
            }

            var b = v.Biomes[biome.Value];
            if (b.TreeCount == 0 || chance >= cover)
            {
                return anchor;
            }

            var k = 0;
            while (k < b.TreeCount - 1 && pick > b.TreeWeights[k])
            {
                k++;
            }

            anchor.Species = b.Trees[k];
            anchor.Spacing = v.Species[anchor.Species].Spacing;
            anchor.Accepted = true;
            return anchor;
        }

        // A plan varies its species' height by up to this factor, and its trunk wander by
        // up to this factor (structure: VegetationTable sizes the reach from them).
        public const float MaxHeightScale = 1.1f;
        public const float MaxWanderScale = 1.6f;

        // The tree an anchor grows, or false where its species cannot stand: under water
        // deeper than it tolerates, on shore sand, above its tree line, on steep ground.
        // Its age sets its proportions (D-089): a sapling is short with a thin trunk and a
        // narrow crown starting near the ground; an old tree is tall, stout, with a wide
        // crown high on its trunk. Its crown's shape, its lean and its trunk's wander vary
        // per tree besides.
        public static bool TryPlan(in TerrainParams p, in TreeAnchor anchor, out TreePlan plan)
        {
            plan = default;
            var s = p.Vegetation.Species[anchor.Species];
            var random = Random.CreateFromIndex(anchor.Seed);

            // Ages spread over the whole range, a few more young trees than old ones.
            var age = math.pow(random.NextFloat(), 1.15f);
            var height =
                math.lerp(s.Height.x, s.Height.y, age)
                * random.NextFloat(2f - MaxHeightScale, MaxHeightScale);
            var trunk = math.max(
                1f,
                math.lerp(s.TrunkRadius.x, s.TrunkRadius.y, math.pow(age, 1.3f))
                    * random.NextFloat(0.85f, 1.2f)
            );
            var c = anchor.Column;
            var sample = TerrainGenerator.Sample(p, c.x, c.y);
            var ground = (int)math.floor(sample.Height);
            var depth = sample.WaterLevel - ground;
            if (depth > s.MaxWaterDepth)
            {
                return false;
            }

            if (ground <= p.SeaLevel + p.ShoreHeight && !s.GrowsOnShore && depth <= 0)
            {
                return false;
            }

            if (sample.TemperatureC < s.MinTemperatureC)
            {
                return false;
            }

            var foot = (int)math.ceil(trunk * (1f + s.RootFlare)) + 1;
            var east = TerrainGenerator.SurfaceHeight(p, c.x + foot, c.y);
            var west = TerrainGenerator.SurfaceHeight(p, c.x - foot, c.y);
            var north = TerrainGenerator.SurfaceHeight(p, c.x, c.y + foot);
            var south = TerrainGenerator.SurfaceHeight(p, c.x, c.y - foot);
            var slope = math.max(math.abs(east - west), math.abs(north - south)) / (2f * foot);
            if (slope > s.MaxSlope)
            {
                return false;
            }

            var lowest = math.min(ground, math.min(math.min(east, west), math.min(north, south)));
            var angle = random.NextFloat(0f, 2f * math.PI);
            var lean = s.Lean * height * random.NextFloat(0.1f, 1f) * math.lerp(0.6f, 1.2f, age);
            plan = new TreePlan
            {
                Species = anchor.Species,
                Seed = anchor.Seed,
                Age = age,
                WanderScale = random.NextFloat(0.4f, MaxWanderScale),
                Base = new float3(c.x + 0.5f, lowest - 2, c.y + 0.5f),
                Height = height,
                TrunkRadius = trunk,
                Lean = lean * new float2(math.cos(angle), math.sin(angle)),
            };

            // Young crowns are narrow and start low; old ones spread and rise.
            var share =
                math.lerp(s.CrownRadius.x, s.CrownRadius.y, random.NextFloat())
                * math.lerp(0.65f, 1.25f, age);
            var crown = share * height;
            var crownBase = math.saturate(
                s.CrownBase * math.lerp(0.35f, 1.15f, age) * random.NextFloat(0.8f, 1.2f)
            );
            var aspect = random.NextFloat(0.75f, 1.3f);
            var top = plan.Base.y + 2f + height;
            var bottom = plan.Base.y + 2f + crownBase * height;
            switch (s.Form)
            {
                case TreeForm.Conifer:
                {
                    plan.CrownCenter = new float3(
                        plan.Base.x + plan.Lean.x * crownBase,
                        bottom,
                        plan.Base.z + plan.Lean.y * crownBase
                    );
                    plan.CrownRadii = new float3(crown * aspect, top - bottom, crown * aspect);
                    break;
                }

                case TreeForm.Palm:
                {
                    var spread = s.FrondLength * 0.8f;
                    plan.CrownCenter = new float3(
                        plan.Base.x + plan.Lean.x,
                        top - s.FrondLength * 0.15f,
                        plan.Base.z + plan.Lean.y
                    );
                    plan.CrownRadii = new float3(spread, s.FrondLength * 0.3f, spread);
                    break;
                }

                default:
                {
                    // The crown fills the tree from its base to its top.
                    var ry = math.max(s.LeafRadius.x, (top - bottom) * 0.5f);
                    var share2 = (height - ry) / height;
                    plan.CrownCenter = new float3(
                        plan.Base.x + plan.Lean.x * share2,
                        top - ry,
                        plan.Base.z + plan.Lean.y * share2
                    );
                    plan.CrownRadii = new float3(
                        crown * aspect,
                        ry,
                        crown * aspect * random.NextFloat(0.75f, 1.15f)
                    );
                    break;
                }
            }

            return true;
        }

        private static bool Outranks(in TreeAnchor b, in TreeAnchor a) =>
            b.Priority > a.Priority
            || (
                b.Priority == a.Priority
                && (
                    b.Column.x > a.Column.x || (b.Column.x == a.Column.x && b.Column.y > a.Column.y)
                )
            );

        private static int2 FloorDiv(int2 a, int b) => new int2(FloorDiv(a.x, b), FloorDiv(a.y, b));

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((b - 1 - a) / b);
    }
}
