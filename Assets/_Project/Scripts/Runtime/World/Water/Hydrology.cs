using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Which water fills a column (D-070).
    public enum WaterBody : byte
    {
        Sea = 0,
        Lake = 1,
        River = 2,
    }

    // A point of a river's centerline, its water surface, half width and depth there,
    // voxels.
    public struct RiverNode
    {
        public float2 Pos;
        public float Level;
        public float HalfWidth;
        public float Depth;
    }

    // A river: its nodes in TerrainParams.RiverNodes, source first, and the box (min x, min
    // z, max x, max z, voxels) outside which it changes nothing.
    public struct River
    {
        public int Start;
        public int Count;
        public float4 Bounds;
    }

    // A lake in a basin of the hydrology grid (D-081): its surface level (voxels), the
    // cell its water spills through, its deepest cell, its cell count and the box of its
    // cells (min x, min z, max x, max z).
    public struct Lake
    {
        public float Level;
        public int Spill;
        public int Deepest;
        public int Cells;
        public int4 Bounds;

        // Middle of the box, voxels.
        public float2 Center => (HydroGrid.Center(Bounds.xy) + HydroGrid.Center(Bounds.zw)) * 0.5f;

        // Half the larger side of the box, voxels.
        public float Radius => (math.cmax(Bounds.zw - Bounds.xy) + 1) * HydroGrid.CellVoxels * 0.5f;

        // Center of the deepest cell, voxels.
        public float2 DeepestPoint => HydroGrid.Center(HydroGrid.Cell(Deepest));
    }

    // How lakes and rivers shape a column (PLAN-05, D-070, D-080, D-081). The plan comes
    // from the hydrology grid (Basins, RiverNetwork); Apply shapes each column from it:
    // inside the water a bed below the level, around it a bank that cuts the land down to
    // the level (canyon walls widen with the cut) or raises it to the level where the land
    // is lower, so every water surface is held by ground at least as high.
    public static class Hydrology
    {
        // Lakes are searched this many cells around the column (their floors reach this far).
        private const int LakeSearch = 2;

        // Lake water reaches at most this far beyond the lake's cells, voxels (half a cell:
        // the rim cells' centers stand at or above the level).
        private const float LakeShoreReach = HydroGrid.CellVoxels * 0.5f;

        // A bank holds the water within this many voxels upstream of its nearest point:
        // the nearest points of neighbor columns lie closer than that along the river.
        private const float BankHold = 3f;

        // Segments of a river blend their cuts over about this distance (8 m), voxels.
        private const float BlendDistance = 64f;

        // A water's floor falls away from its flat bank by FloorFall voxels per voxel (27°):
        // it holds the water beside it (the flank of a fall, a junction) and is below any
        // cut a few meters further.
        private const float FloorFall = 0.5f;

        private enum Zone : byte
        {
            None = 0,
            Bank = 2,
            Water = 3,
        }

        // Shapes the column at pos (voxel column center) whose land height is land.
        public static void Apply(
            in TerrainParams p,
            float2 pos,
            float land,
            float wallRun,
            out float height,
            out float level,
            out WaterBody body
        )
        {
            var shaping = new Shaping
            {
                Carve = land,
                Floor = float.MinValue,
                WetLevel = p.SeaLevel,
            };

            ShapeByLakes(p, pos, land, ref shaping);

            ShapeByRivers(p, pos, land, wallRun, ref shaping);

            if (shaping.Wet)
            {
                height = shaping.WetHeight;
                level = shaping.WetLevel;
                body = shaping.WetBody;
                return;
            }

            height = math.max(shaping.Carve, shaping.Floor);
            level = p.SeaLevel;
            body = WaterBody.Sea;
        }

        // Lakes (D-081). A column within LakeShoreReach of a lake's cells is lake water
        // where its ground lies below the level, so the shore follows the terrain. Beyond that
        // reach the lake adds a floor at its level plus the lip, falling away with the
        // distance: every dry column next to the water stands above it, a sill where the
        // fine land dips between grid samples. The reach is continuous, so the water and the
        // floor meet without a seam.
        private static void ShapeByLakes(
            in TerrainParams p,
            float2 pos,
            float land,
            ref Shaping shaping
        )
        {
            var plan = p.WaterPlan;
            if (plan.Lakes.Length == 0)
            {
                return;
            }

            var home = HydroGrid.CellOf(pos);
            var ids = new FixedList64Bytes<int>();
            var distances = new FixedList64Bytes<float>();
            for (var dz = -LakeSearch; dz <= LakeSearch; dz++)
            for (var dx = -LakeSearch; dx <= LakeSearch; dx++)
            {
                var c = home + new int2(dx, dz);
                if (!HydroGrid.InGrid(c))
                {
                    continue;
                }

                var k = plan.LakeOf[HydroGrid.Index(c)] - 1;
                if (k < 0)
                {
                    continue;
                }

                var offset = math.abs(pos - HydroGrid.Center(c)) - HydroGrid.CellVoxels * 0.5f;
                var distance = math.length(math.max(offset, 0f));
                var slot = ids.IndexOf(k);
                if (slot >= 0)
                {
                    distances[slot] = math.min(distances[slot], distance);
                }
                else if (ids.Length < ids.Capacity)
                {
                    ids.Add(k);
                    distances.Add(distance);
                }
            }

            for (var n = 0; n < ids.Length; n++)
            {
                var lake = plan.Lakes[ids[n]];
                var within = distances[n] <= LakeShoreReach;
                var wet = within && math.floor(land) < math.floor(lake.Level);
                Combine(
                    p,
                    wet ? Zone.Water : Zone.None,
                    land,
                    wet ? -1f : math.max(0f, distances[n] - LakeShoreReach),
                    lake.Level,
                    lake.Level + p.BankLip,
                    WaterBody.Lake,
                    land,
                    ref shaping
                );
            }
        }

        // Each river segment near the column shapes it, so where rivers meet or bend the
        // banks of all hold their water. The segments come from the column's grid cell
        // (WaterPlan.CellRuns), river by river; in them only segments whose start node is
        // within a segment plus the reach on both axes count.
        private static void ShapeByRivers(
            in TerrainParams p,
            float2 pos,
            float land,
            float wallRun,
            ref Shaping shaping
        )
        {
            var plan = p.WaterPlan;
            var home = HydroGrid.CellOf(pos);
            if (plan.CellStart.Length == 0 || !HydroGrid.InGrid(home))
            {
                return;
            }

            var i = HydroGrid.Index(home);
            var near = p.RiverStep + p.RiverReach;
            var current = -1;

            // The segments' cuts are blended by nearness (a soft nearest), per river: the
            // nearest stretch shapes the bank and bends blend over BlendDistance, so a lower
            // stretch never carves under the water of the stretch beside the column.
            var cut = new SoftNearest { Nearest = float.MaxValue };
            for (var k = plan.CellStart[i]; k < plan.CellStart[i + 1]; k++)
            {
                var run = plan.CellRuns[k];
                if (run.x != current)
                {
                    Flush(ref shaping, ref cut);
                    current = run.x;
                }

                for (var n = run.y; n <= run.z; n++)
                {
                    var a = plan.Nodes[n].Pos;
                    if (math.abs(pos.x - a.x) > near || math.abs(pos.y - a.y) > near)
                    {
                        continue;
                    }

                    var ab = plan.Nodes[n + 1].Pos - a;
                    var lengthSq = math.lengthsq(ab);
                    var s = lengthSq > 0f ? math.saturate(math.dot(pos - a, ab) / lengthSq) : 0f;
                    var dSq = math.distancesq(pos, a + ab * s);
                    ShapeByRiver(p, run.x, dSq, n, s, land, wallRun, ref shaping, ref cut);
                }
            }

            Flush(ref shaping, ref cut);
        }

        private static void Flush(ref Shaping shaping, ref SoftNearest cut)
        {
            if (cut.Weight > 0f)
            {
                shaping.Carve = math.min(shaping.Carve, cut.Sum / cut.Weight);
            }

            cut = new SoftNearest { Nearest = float.MaxValue };
        }

        // The column shaped by one segment of a river, from the squared distance to its
        // nearest point on the segment (node + t). Every segment near the column shapes it
        // and the results combine (the lowest cut, the highest floor), so the ground stays
        // continuous where the nearest stretch of a bending river changes.
        private static void ShapeByRiver(
            in TerrainParams p,
            int riverIndex,
            float distanceSq,
            int node,
            float t,
            float land,
            float wallRun,
            ref Shaping shaping,
            ref SoftNearest cut
        )
        {
            var river = p.WaterPlan.Rivers[riverIndex];
            var a = p.WaterPlan.Nodes[node];
            var c = p.WaterPlan.Nodes[node + 1];
            var nearestLevel = math.lerp(a.Level, c.Level, t);
            var distance = math.sqrt(distanceSq);
            var halfWidth = math.lerp(a.HalfWidth, c.HalfWidth, t);
            var depth = math.lerp(a.Depth, c.Depth, t);
            var u = math.saturate(distance / halfWidth);

            // A bank holds the water beside it, which may lie a little upstream (a rapid,
            // the face of a fall): it takes the level that far up the river.
            var bankLevel = UpstreamLevel(p, river, node, t, BankHold) + p.BankLip;
            var z = Profile(
                p,
                wallRun,
                distance - halfWidth,
                nearestLevel,
                bankLevel,
                math.lerp(nearestLevel - depth, nearestLevel + p.BankLip, u * u),
                land,
                out var h
            );
            // Water and floors combine as for any water; the cut joins the river's blend
            // (no change counts as the land).
            var carves = z == Zone.Bank && h <= land;
            Combine(
                p,
                carves ? Zone.None : z,
                h,
                distance - halfWidth,
                nearestLevel,
                bankLevel,
                WaterBody.River,
                land,
                ref shaping
            );
            if (z != Zone.Water)
            {
                cut.Add(distance, carves ? h : land);
            }
        }

        // A blend weighted by exp(-(distance - nearest) / BlendDistance), kept stable as
        // nearer values arrive.
        private struct SoftNearest
        {
            public float Nearest;
            public float Weight;
            public float Sum;

            public void Add(float distance, float value)
            {
                if (Weight == 0f)
                {
                    Nearest = distance;
                    Weight = 1f;
                    Sum = value;
                    return;
                }

                if (distance < Nearest)
                {
                    var k = math.exp((distance - Nearest) / BlendDistance);
                    Weight = Weight * k + 1f;
                    Sum = Sum * k + value;
                    Nearest = distance;
                    return;
                }

                var w = math.exp((Nearest - distance) / BlendDistance);
                Weight += w;
                Sum += w * value;
            }
        }

        // The water level a distance upstream of node + t along the river (levels never
        // rise downstream, so it bounds every level within that distance).
        private static float UpstreamLevel(
            in TerrainParams p,
            in River river,
            int node,
            float t,
            float distance
        )
        {
            for (var n = node; n >= river.Start; n--)
            {
                var a = p.WaterPlan.Nodes[n];
                var c = p.WaterPlan.Nodes[n + 1];
                var length = math.distance(a.Pos, c.Pos);
                var along = (n == node ? t : 1f) * length;
                if (along >= distance)
                {
                    return math.lerp(
                        a.Level,
                        c.Level,
                        (along - distance) / math.max(length, 1e-3f)
                    );
                }

                distance -= along;
            }

            return p.WaterPlan.Nodes[river.Start].Level;
        }

        // The ground at signed distance edge from the water's edge (negative inside) of
        // a lake's or a river's water at level. Inside, the bed rises from its depth toward the edge; where it
        // comes above the water it is dry and stands at the bank level at once, like the
        // banks outside, so the water next to it is always held.
        private static Zone Profile(
            in TerrainParams p,
            float wallRun,
            float edge,
            float level,
            float bankLevel,
            float bed,
            float land,
            out float height
        )
        {
            if (edge < 0f)
            {
                // A hollow deeper than the bed stays and fills with water. At sea the sea
                // holds everything.
                var dry =
                    math.floor(bed) >= math.floor(level)
                    && level > p.SeaLevel
                    && land >= p.SeaLevel;
                if (dry)
                {
                    height = math.max(bed, bankLevel);
                    return Zone.Bank;
                }

                height = math.min(land, bed);
                return Zone.Water;
            }

            if (land >= bankLevel)
            {
                var cut = land - bankLevel;

                // A river's first meters of cut form a gentle shore; what is deeper rises
                // as valley walls beyond it, as steep as the biome's wall run (canyons).
                var rise = math.min(cut, p.ShoreCut);
                var shoreWidth = p.BankWidth + rise * p.ShoreRun;
                var wallWidth = math.min((cut - rise) * wallRun, p.MaxWallWidth - shoreWidth);
                if (edge >= shoreWidth + wallWidth)
                {
                    height = land;
                    return Zone.None;
                }

                height = bankLevel + rise * math.smoothstep(0f, shoreWidth, edge);
                if (wallWidth > 0f)
                {
                    height +=
                        (cut - rise) * math.smoothstep(shoreWidth, shoreWidth + wallWidth, edge);
                }

                return Zone.Bank;
            }

            // The land is below the water: at sea the sea holds it; inland a bank at the
            // level, then the water's floor as a levee down to the land.
            if (level <= p.SeaLevel || land < p.SeaLevel)
            {
                height = land;
                return Zone.None;
            }

            // Beyond the flat bank the water's floor (Combine) slopes down to the land.
            if (edge < p.BankWidth)
            {
                height = bankLevel;
                return Zone.Bank;
            }

            height = land;
            return Zone.None;
        }

        // What the lakes and rivers have said about a column so far.
        private struct Shaping
        {
            // Some water covers the column: its bed (the deepest) and level (the highest).
            public bool Wet;
            public float WetHeight;
            public float WetLevel;
            public WaterBody WetBody;

            // Otherwise: the lowest ground any bank cuts to (the land if none), and the
            // highest ground some water needs to be held.
            public float Carve;
            public float Floor;
        }

        // Water wins: two waters keep the higher level over the deeper bed, so neither ends
        // in a wall. Dry columns take the lowest cut, but never less than the floor of any
        // water nearby: its bank level beside it, falling away at FloorFall, so the ground
        // stays continuous where the banks of two waters meet
        // and a levee slopes down where the land is lower than the water. Banks raised
        // above the land (a flat bank, a dry channel edge) are floors too.
        private static void Combine(
            in TerrainParams p,
            Zone zone,
            float height,
            float edge,
            float level,
            float bankLevel,
            WaterBody body,
            float land,
            ref Shaping shaping
        )
        {
            if (zone == Zone.Water)
            {
                if (!shaping.Wet || level > shaping.WetLevel)
                {
                    shaping.WetLevel = level;
                    shaping.WetBody = body;
                }

                shaping.WetHeight = shaping.Wet ? math.min(shaping.WetHeight, height) : height;
                shaping.Wet = true;
                return;
            }

            if (level > p.SeaLevel && land >= p.SeaLevel)
            {
                var x = math.max(0f, edge - p.BankWidth);
                var floor = bankLevel - FloorFall * x;
                shaping.Floor = math.max(shaping.Floor, floor);
            }

            if (zone == Zone.None)
            {
                return;
            }

            if (height > land)
            {
                shaping.Floor = math.max(shaping.Floor, height);
            }
            else
            {
                shaping.Carve = math.min(shaping.Carve, height);
            }
        }
    }
}
