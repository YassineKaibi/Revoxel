using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
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

    // A point of a river's centerline and its water surface there, voxels.
    public struct RiverNode
    {
        public float2 Pos;
        public float Level;
    }

    // A river: its nodes in TerrainParams.RiverNodes, source first, and the box (min x, min
    // z, max x, max z, voxels) outside which it changes nothing.
    public struct River
    {
        public int Start;
        public int Count;
        public float4 Bounds;
    }

    // A lake: surface level and center depth in voxels; Reach bounds what it changes.
    public struct Lake
    {
        public float2 Center;
        public float Radius;
        public float Level;
        public float Depth;
        public float Reach;
    }

    // Rivers and lakes (PLAN-05, D-070, D-071). Plan runs once per seed, in a Burst job,
    // from the land height alone: lakes on gentle inland ground, then rivers traced from
    // lake outlets and springs down toward the coast, each a polyline whose water level
    // never rises downstream. Apply then shapes each column from the plan: inside the
    // water a bowl or channel below the level, around it a bank that cuts the land down to
    // the level (canyon walls widen with the cut) or raises it to the level where the land
    // is lower (a levee), so every water surface is held by ground at least as high.
    public static class Hydrology
    {
        private const int RimSamples = 8;
        private const int LakeRimSamples = 32;

        // Lakes lower the land fully inside this share of their radius, then less and less
        // to the rim.
        private const float LakeBlendStart = 0.3f;

        // A bank holds the water within this many voxels upstream of its nearest point:
        // the nearest points of neighbor columns lie closer than that along the river.
        private const float BankHold = 3f;
        private const int StepSamples = 8;
        private const int TurnChoices = 3;

        // Segments of a river blend their cuts over about this distance (8 m), voxels.
        private const float BlendDistance = 64f;

        // A water's floor falls away from its flat bank by FloorFall voxels per voxel (27°):
        // it holds the water beside it (the flank of a fall, a junction) and is below any
        // cut a few meters further.
        private const float FloorFall = 0.5f;

        // A lake changes the ground up to this many bank widths beyond its rim (its floor).
        private const float LakeFloorReach = 4f;

        // Largest length of the wander offset (two noises, each at most about 1).
        private const float Wander = 1.5f;

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

            for (var i = 0; i < p.Lakes.Length; i++)
            {
                var lake = p.Lakes[i];
                var d = math.distance(pos, lake.Center);
                if (d >= lake.Reach)
                {
                    continue;
                }

                // The land is lowered toward a bowl near the middle, less and less toward
                // the rim, where it is untouched: the shore is where that ground meets the
                // level, so it follows the terrain. A noise moves the blend inward to
                // wander the shore further.
                var inner =
                    lake.Radius
                    * (
                        1f
                        - p.LakeShape
                            * (
                                0.5f
                                + 0.5f
                                    * noise.snoise(pos * p.LakeShapeFrequency + p.LakeShapeOffset)
                            )
                    );
                var t = d / inner;
                var lakeBank = lake.Level + p.BankLip;
                var zone = Zone.None;
                var shaped = land;
                if (t < 1f)
                {
                    var bowl = lake.Level - lake.Depth * (1f - t * t);
                    var blend = 1f - math.smoothstep(LakeBlendStart, 1f, t);
                    shaped = land - blend * math.max(0f, land - bowl);
                    zone = math.floor(shaped) < math.floor(lake.Level) ? Zone.Water : Zone.Bank;
                }

                Combine(
                    p,
                    zone,
                    shaped,
                    d - lake.Radius,
                    lake.Level,
                    lakeBank,
                    WaterBody.Lake,
                    land,
                    ref shaping
                );
            }

            // Each river segment near the column shapes it, so where rivers meet or bend
            // the banks of all hold their water. Only rivers whose box holds the column
            // are searched, and in them only segments whose start node is within the
            // longest segment (a step, or a step and the join distance) plus the reach on
            // both axes. The wander is computed only for columns some river can reach
            // (boxes include it).
            var hasWander = false;
            var q = pos;
            var near = p.RiverStep + p.JoinDistance + p.RiverReach;
            for (var r = 0; r < p.Rivers.Length; r++)
            {
                var river = p.Rivers[r];
                var b = river.Bounds;
                if (pos.x < b.x || pos.y < b.y || pos.x > b.z || pos.y > b.w)
                {
                    continue;
                }

                if (!hasWander)
                {
                    q =
                        pos
                        + p.Meander
                            * new float2(
                                noise.snoise(pos * p.MeanderFrequency + p.MeanderOffsetX),
                                noise.snoise(pos * p.MeanderFrequency + p.MeanderOffsetZ)
                            );
                    hasWander = true;
                }

                // The segments' cuts are blended by nearness (a soft nearest): the nearest
                // stretch shapes the bank and bends blend over BlendDistance, so a lower
                // stretch never carves under the water of the stretch beside the column.
                var cut = new SoftNearest { Nearest = float.MaxValue };
                var last = river.Start + river.Count - 1;
                for (var n = river.Start; n < last; n++)
                {
                    var a = p.RiverNodes[n].Pos;
                    if (math.abs(q.x - a.x) > near || math.abs(q.y - a.y) > near)
                    {
                        continue;
                    }

                    var ab = p.RiverNodes[n + 1].Pos - a;
                    var lengthSq = math.lengthsq(ab);
                    var s = lengthSq > 0f ? math.saturate(math.dot(q - a, ab) / lengthSq) : 0f;
                    var dSq = math.distancesq(q, a + ab * s);
                    ShapeByRiver(p, r, dSq, n, s, land, wallRun, ref shaping, ref cut);
                }

                if (cut.Weight > 0f)
                {
                    shaping.Carve = math.min(shaping.Carve, cut.Sum / cut.Weight);
                }
            }

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
            var river = p.Rivers[riverIndex];
            var a = p.RiverNodes[node];
            var c = p.RiverNodes[node + 1];
            var nearestLevel = math.lerp(a.Level, c.Level, t);
            var share = (node - river.Start + t) / math.max(1, river.Count - 1);
            var distance = math.sqrt(distanceSq);
            var halfWidth = math.lerp(p.RiverHalfWidth.x, p.RiverHalfWidth.y, share);
            var depth = math.lerp(p.RiverDepth.x, p.RiverDepth.y, share);
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
                var a = p.RiverNodes[n];
                var c = p.RiverNodes[n + 1];
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

            return p.RiverNodes[river.Start].Level;
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

        // Plans the lakes and rivers of p in place (D-071). Pure function of p.
        public static void Plan(ref TerrainParams p)
        {
            p.Lakes.Clear();
            p.Rivers.Clear();
            p.RiverNodes.Clear();
            p.RiverReach = p.RiverHalfWidth.y + p.BankWidth + p.MaxWallWidth + p.Meander * Wander;
            var random = Random.CreateFromIndex(p.HashSeed ^ 0x5BD1E995u);
            var outlets = new NativeList<float4>(WaterSettings.MaxLakeCount, Allocator.Temp);
            PlanLakes(ref p, ref random, outlets);

            var sources = new NativeList<float4>(64, Allocator.Temp);
            var sourceLakes = new NativeList<int>(64, Allocator.Temp);
            for (var i = 0; i < outlets.Length; i++)
            {
                if (random.NextFloat() < p.LakeOutletShare)
                {
                    sources.Add(outlets[i]);
                    sourceLakes.Add(i);
                }
            }

            AddSprings(p, ref random, sources, sourceLakes);
            var path = new NativeList<RiverNode>(p.RiverMaxSteps * 3 + 4, Allocator.Temp);
            for (var i = 0; i < sources.Length && p.Rivers.Length < p.MaxRivers; i++)
            {
                var source = sources[i];
                if (!Trace(p, source, sourceLakes[i], path))
                {
                    continue;
                }

                if (p.RiverNodes.Length + path.Length > WaterSettings.MaxRiverNodes)
                {
                    continue;
                }

                var river = new River
                {
                    Start = p.RiverNodes.Length,
                    Count = path.Length,
                    Bounds = new float4(
                        float.MaxValue,
                        float.MaxValue,
                        float.MinValue,
                        float.MinValue
                    ),
                };
                for (var n = 0; n < path.Length; n++)
                {
                    var node = path[n];
                    p.RiverNodes.Add(node);
                    river.Bounds = new float4(
                        math.min(river.Bounds.xy, node.Pos),
                        math.max(river.Bounds.zw, node.Pos)
                    );
                }

                river.Bounds += new float4(
                    -p.RiverReach,
                    -p.RiverReach,
                    p.RiverReach,
                    p.RiverReach
                );
                p.Rivers.Add(river);
            }

            outlets.Dispose();
            sources.Dispose();
            sourceLakes.Dispose();
            path.Dispose();
        }

        // Lakes on gentle inland ground; the level sits on the lowest rim sample, rounded
        // down to the level step. outlets gets (rim point, level, outlet angle) per lake.
        private static void PlanLakes(
            ref TerrainParams p,
            ref Random random,
            NativeList<float4> outlets
        )
        {
            for (var c = 0; c < p.LakeCandidates && p.Lakes.Length < p.MaxLakes; c++)
            {
                var center = InDisk(ref random, p.IslandRadius);
                var radius = random.NextFloat(p.LakeRadius.x, p.LakeRadius.y);
                var centerHeight = TerrainGenerator.LandHeight(
                    p,
                    (int)math.floor(center.x),
                    (int)math.floor(center.y),
                    out var inland
                );
                if (inland < p.LakeMinInland)
                {
                    continue;
                }

                var low = centerHeight;
                var high = centerHeight;
                var lowAngle = 0f;
                for (var k = 0; k < LakeRimSamples; k++)
                {
                    var angle = k * (2f * math.PI / LakeRimSamples);
                    var rim = center + radius * new float2(math.cos(angle), math.sin(angle));
                    var h = TerrainGenerator.LandHeight(
                        p,
                        (int)math.floor(rim.x),
                        (int)math.floor(rim.y),
                        out _
                    );
                    if (h < low)
                    {
                        low = h;
                        lowAngle = angle;
                    }

                    high = math.max(high, h);
                }

                var level = math.floor((low - p.LakeRimMargin) / p.LakeLevelStep) * p.LakeLevelStep;
                if (high - low > p.LakeMaxRelief || level < p.SeaLevel + p.LakeMinAltitude)
                {
                    continue;
                }

                var reach = radius + LakeFloorReach * p.BankWidth;
                var clear = true;
                for (var i = 0; i < p.Lakes.Length && clear; i++)
                {
                    // Shores apart: the water and gentle shore of two lakes never touch.
                    var apart = radius + p.Lakes[i].Radius + 2f * p.BankWidth;
                    clear = math.distance(center, p.Lakes[i].Center) > apart;
                }

                if (!clear)
                {
                    continue;
                }

                p.Lakes.Add(
                    new Lake
                    {
                        Center = center,
                        Radius = radius,
                        Level = level,
                        Depth = p.LakeDepth,
                        Reach = reach,
                    }
                );
                var outlet = center + radius * new float2(math.cos(lowAngle), math.sin(lowAngle));
                outlets.Add(new float4(outlet, level, lowAngle));
            }
        }

        // Springs high and inland, highest first, kept apart from every other source and
        // outside the lakes. Each is (point, level, angle of the steepest way down).
        private static void AddSprings(
            in TerrainParams p,
            ref Random random,
            NativeList<float4> sources,
            NativeList<int> sourceLakes
        )
        {
            var first = sources.Length;
            for (var c = 0; c < p.SpringCandidates; c++)
            {
                var pos = InDisk(ref random, p.IslandRadius);
                var h = TerrainGenerator.LandHeight(
                    p,
                    (int)math.floor(pos.x),
                    (int)math.floor(pos.y),
                    out var inland
                );
                if (inland < p.SpringMinInland || h < p.SeaLevel + p.SpringMinAltitude)
                {
                    continue;
                }

                var clear = true;
                for (var i = 0; i < sources.Length && clear; i++)
                {
                    clear = math.distance(pos, sources[i].xy) > p.SourceSpacing;
                }

                for (var i = 0; i < p.Lakes.Length && clear; i++)
                {
                    clear = math.distance(pos, p.Lakes[i].Center) > p.Lakes[i].Reach;
                }

                if (!clear)
                {
                    continue;
                }

                var lowest = float.MaxValue;
                var angle = 0f;
                for (var k = 0; k < RimSamples; k++)
                {
                    var a = k * (2f * math.PI / RimSamples);
                    var q = pos + p.RiverStep * new float2(math.cos(a), math.sin(a));
                    var hq = TerrainGenerator.LandHeight(
                        p,
                        (int)math.floor(q.x),
                        (int)math.floor(q.y),
                        out _
                    );
                    if (hq < lowest)
                    {
                        lowest = hq;
                        angle = a;
                    }
                }

                // Insertion by height, highest first, after the lake outlets.
                var entry = new float4(pos, h - p.RiverInset, angle);
                var at = sources.Length;
                while (at > first && sources[at - 1].z < entry.z)
                {
                    at--;
                }

                sources.Add(entry);
                sourceLakes.Add(-1);
                for (var i = sources.Length - 1; i > at; i--)
                {
                    sources[i] = sources[i - 1];
                }

                sources[at] = entry;
            }
        }

        // Traces one river from source (point, level, heading angle) into path. Each step
        // takes the lowest of five headings (with a pull toward the coast), the level falls
        // to the lowest ground the step crosses, and a large drop becomes a waterfall. Ends
        // at the sea, in another lake or on an earlier river; false when it never does or
        // stays too short.
        private static bool Trace(
            in TerrainParams p,
            float4 source,
            int sourceLake,
            NativeList<RiverNode> path
        )
        {
            path.Clear();
            var pos = source.xy;
            var level = math.max(source.z, p.SeaLevel);
            var heading = new float2(math.cos(source.w), math.sin(source.w));
            path.Add(new RiverNode { Pos = pos, Level = level });
            for (var step = 0; step < p.RiverMaxSteps; step++)
            {
                var bestScore = float.MaxValue;
                var bestDir = heading;
                for (var k = -TurnChoices; k <= TurnChoices; k++)
                {
                    var dir = Rotate(heading, k * p.RiverTurn);
                    var q = pos + dir * p.RiverStep;
                    var outward = math.normalizesafe(q);
                    // The ground a step and two steps ahead: rivers follow valleys rather
                    // than cut through the next rise.
                    // Ground above the water would have to be cut: headings into rises
                    // cost more than their height alone.
                    var ahead = 0.5f * (LandAt(p, q) + LandAt(p, q + dir * p.RiverStep));
                    var score =
                        ahead
                        + p.RiverRisePenalty * math.max(0f, ahead - level)
                        - p.RiverOutwardBias * p.RiverStep * math.dot(dir, outward);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestDir = dir;
                    }
                }

                var next = pos + bestDir * p.RiverStep;
                // The lowest channel ground over the step sets the new level; a fall starts
                // before the first point where that ground is lower than the water, so the
                // water above the fall never stands over lower ground.
                var lowest = float.MaxValue;
                var highest = float.MinValue;
                var dropAt = 1f;
                for (var s = 1; s <= StepSamples; s++)
                {
                    var f = (float)s / StepSamples;
                    var h = ChannelLand(p, math.lerp(pos, next, f), bestDir);
                    if (dropAt >= 1f && h - p.RiverInset < level)
                    {
                        dropAt = (s - 1f) / StepSamples;
                    }

                    lowest = math.min(lowest, h);
                    highest = math.max(highest, h);
                }

                // A river that would cut deeper than its limit through a rise is dropped:
                // real rivers go around, and other springs are tried.
                if (highest - math.min(level, lowest - p.RiverInset) > p.RiverMaxCut)
                {
                    return false;
                }

                var atSea = LandAt(p, next) < p.SeaLevel;
                var newLevel = atSea
                    ? p.SeaLevel
                    : math.max(p.SeaLevel, math.min(level, lowest - p.RiverInset));
                var end = next;
                var floor = float.MinValue;
                var done = atSea;
                if (!done && JoinsRiver(p, next, out var join))
                {
                    end = join.Pos;
                    floor = join.Level;
                    done = true;
                }
                else if (!done && EntersLake(p, next, sourceLake, out var lake))
                {
                    floor = lake.Level;
                    done = true;
                }

                // Into a river or a lake the water falls to its level.
                newLevel = floor > float.MinValue ? floor : newLevel;
                AddStep(p, path, pos, end, level, newLevel, dropAt);
                if (done)
                {
                    if (floor > float.MinValue)
                    {
                        // Water never falls below what it joins.
                        for (var n = 0; n < path.Length; n++)
                        {
                            var node = path[n];
                            node.Level = math.max(node.Level, floor);
                            path[n] = node;
                        }
                    }

                    return step + 1 >= p.RiverMinSteps;
                }

                pos = next;
                heading = bestDir;
                level = newLevel;
            }

            return false;
        }

        // Adds the step from from to to. A drop of at least FallMin becomes a waterfall: the
        // level holds to the cliff (where the ground drops most), falls over a face of
        // drop / FallSteepness, then holds again.
        private static void AddStep(
            in TerrainParams p,
            NativeList<RiverNode> path,
            float2 from,
            float2 to,
            float level,
            float newLevel,
            float dropAt
        )
        {
            var drop = level - newLevel;
            if (drop >= p.FallMin)
            {
                var length = math.distance(from, to);
                var dir = length > 0f ? (to - from) / length : float2.zero;
                var face = math.min(drop / p.FallSteepness, length * 0.5f);
                var top = math.clamp(dropAt * length, 1f, length - face - 1f);
                path.Add(new RiverNode { Pos = from + dir * top, Level = level });
                path.Add(new RiverNode { Pos = from + dir * (top + face), Level = newLevel });
            }

            path.Add(new RiverNode { Pos = to, Level = newLevel });
        }

        private static bool JoinsRiver(in TerrainParams p, float2 pos, out RiverNode join)
        {
            join = default;
            var best = p.JoinDistance * p.JoinDistance;
            var found = false;
            for (var i = 0; i < p.RiverNodes.Length; i++)
            {
                var d = math.distancesq(pos, p.RiverNodes[i].Pos);
                if (d < best)
                {
                    best = d;
                    join = p.RiverNodes[i];
                    found = true;
                }
            }

            return found;
        }

        private static bool EntersLake(
            in TerrainParams p,
            float2 pos,
            int sourceLake,
            out Lake lake
        )
        {
            // Within the shore of a lake (its widest radius plus a bank and a channel), a
            // river flows into it rather than past it.
            var shore = p.BankWidth + p.RiverHalfWidth.y;
            for (var i = 0; i < p.Lakes.Length; i++)
            {
                var reach = p.Lakes[i].Radius + shore;
                if (i != sourceLake && math.distance(pos, p.Lakes[i].Center) < reach)
                {
                    lake = p.Lakes[i];
                    return true;
                }
            }

            lake = default;
            return false;
        }

        // The lowest land under the channel drawn for the path point c (Apply moves the
        // channel by the wander, so it lies near c minus the wander there) at its center
        // and its widest edges: the level is taken from where the water really is, so a
        // river never sits above the land beside it.
        private static float ChannelLand(in TerrainParams p, float2 c, float2 dir)
        {
            var center =
                c
                - p.Meander
                    * new float2(
                        noise.snoise(c * p.MeanderFrequency + p.MeanderOffsetX),
                        noise.snoise(c * p.MeanderFrequency + p.MeanderOffsetZ)
                    );
            var side = new float2(-dir.y, dir.x) * p.RiverHalfWidth.y;
            return math.min(
                LandAt(p, center),
                math.min(LandAt(p, center + side), LandAt(p, center - side))
            );
        }

        private static float LandAt(in TerrainParams p, float2 pos) =>
            TerrainGenerator.LandHeight(p, (int)math.floor(pos.x), (int)math.floor(pos.y), out _);

        private static float2 Rotate(float2 v, float angle)
        {
            math.sincos(angle, out var s, out var c);
            return new float2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private static float2 InDisk(ref Random random, float radius)
        {
            var angle = random.NextFloat(0f, 2f * math.PI);
            var r = radius * math.sqrt(random.NextFloat());
            return r * new float2(math.cos(angle), math.sin(angle));
        }
    }

    // Runs Hydrology.Plan in Burst, so every caller (streaming, tools, tests) gets the same
    // plan whatever path computes it (R-21).
    [BurstCompile(CompileSynchronously = true)]
    public struct HydrologyPlanJob : IJob
    {
        public NativeReference<TerrainParams> Params;

        public void Execute()
        {
            var p = Params.Value;
            Hydrology.Plan(ref p);
            Params.Value = p;
        }
    }
}
