using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // Working memory of TreeGrowth, reused for every tree of one job.
    public struct TreeScratch
    {
        public NativeList<float3> Nodes;
        public NativeList<int> Parent;
        public NativeList<float> Radius;
        public NativeList<int> Tips;
        public NativeList<int> Children;
        public NativeList<byte> Trunk;
        public NativeList<float3> Pull;
        public NativeList<float3> Attractors;
        public NativeList<int> Nearest;
        public NativeList<float> NearestDistance;

        public static TreeScratch Create(Allocator allocator) =>
            new TreeScratch
            {
                Nodes = new NativeList<float3>(256, allocator),
                Parent = new NativeList<int>(256, allocator),
                Radius = new NativeList<float>(256, allocator),
                Tips = new NativeList<int>(256, allocator),
                Children = new NativeList<int>(256, allocator),
                Trunk = new NativeList<byte>(256, allocator),
                Pull = new NativeList<float3>(256, allocator),
                Attractors = new NativeList<float3>(256, allocator),
                Nearest = new NativeList<int>(256, allocator),
                NearestDistance = new NativeList<float>(256, allocator),
            };

        public void Clear()
        {
            Nodes.Clear();
            Parent.Clear();
            Radius.Clear();
            Tips.Clear();
            Children.Clear();
            Trunk.Clear();
            Pull.Clear();
            Attractors.Clear();
            Nearest.Clear();
            NearestDistance.Clear();
        }

        public void Dispose()
        {
            Nodes.Dispose();
            Parent.Dispose();
            Radius.Dispose();
            Tips.Dispose();
            Children.Dispose();
            Trunk.Dispose();
            Pull.Dispose();
            Attractors.Dispose();
            Nearest.Dispose();
            NearestDistance.Dispose();
        }
    }

    // Grows the skeleton of one tree from its plan and turns it into parts (D-083): tapered
    // wood capsules and leaf clusters, in world voxel coordinates. A pure function of the
    // plan and the parameters. Broadleaf crowns grow by space colonization toward
    // attractors scattered in a noise-deformed envelope, with radii from the pipe model;
    // conifers grow a leader with whorls of drooping branches carrying needle clusters;
    // palms an arched trunk with drooping fronds. Trunks follow a curved path (lean plus a
    // noise wander) with a flare at the ground; nodes sit on voxel centers (R-21).
    public static class TreeGrowth
    {
        public const int MaxAttractors = 512;
        public const int MaxNodes = 1024;

        // Growth rounds of space colonization (structure, not tuning).
        private const int MaxRounds = 96;

        public static void Grow(
            in TerrainParams p,
            in TreePlan plan,
            ref TreeScratch scratch,
            NativeList<TreePart> parts
        )
        {
            scratch.Clear();
            var s = p.Vegetation.Species[plan.Species];
            var random = Random.CreateFromIndex(plan.Seed ^ 0x6A09E667u);
            var phase = random.NextFloat2(-500f, 500f);
            switch (s.Form)
            {
                case TreeForm.Conifer:
                    GrowConifer(p, plan, s, ref random, phase, ref scratch, parts);
                    break;
                case TreeForm.Palm:
                    GrowPalm(p, plan, s, ref random, phase, ref scratch, parts);
                    break;
                default:
                    GrowBroadleaf(p, plan, s, ref random, phase, ref scratch, parts);
                    break;
            }

            if (s.PropRoots > 0)
            {
                GrowPropRoots(p, plan, s, ref random, phase, parts);
            }
        }

        // A point on the trunk h voxels above its base: the lean grows with height, the
        // wander is a smooth noise that fades to nothing at the ground.
        public static float3 TrunkAt(
            in TreePlan plan,
            in TreeSpeciesParams s,
            float2 phase,
            float h
        )
        {
            var t = math.saturate(h / (plan.Height + 2f));
            var lean = plan.Lean * math.pow(t, 1.4f);
            var wander =
                s.Wander
                * plan.WanderScale
                * plan.Height
                * t
                * new float2(
                    noise.snoise(new float2(t * 1.6f, phase.x)),
                    noise.snoise(new float2(t * 1.6f, phase.y))
                );
            return new float3(
                plan.Base.x + lean.x + wander.x,
                plan.Base.y + h,
                plan.Base.z + lean.y + wander.y
            );
        }

        private static float Flare(in TreeSpeciesParams s, float h) =>
            1f + s.RootFlare * math.exp(-math.max(0f, h - 2f) / s.FlareHeight);

        private static void GrowBroadleaf(
            in TerrainParams p,
            in TreePlan plan,
            in TreeSpeciesParams s,
            ref Random random,
            float2 phase,
            ref TreeScratch scratch,
            NativeList<TreePart> parts
        )
        {
            var maxNodes = math.min(s.MaxNodes, MaxNodes);

            // The bare trunk up to the crown, then a leader on into it (D-089): the leader
            // puts nodes among the attractors, so every crown grows.
            var crownBottom = plan.CrownCenter.y - plan.CrownRadii.y - plan.Base.y;
            var trunkLength =
                math.max(2f, crownBottom)
                + s.Leader * plan.CrownRadii.y * 2f * random.NextFloat(0.7f, 1f);
            trunkLength = math.min(trunkLength, plan.Height + 2f - s.LeafRadius.x);
            var trunkNodes = math.max(2, (int)math.ceil(trunkLength / s.Step));
            for (var i = 0; i <= trunkNodes; i++)
            {
                AddNode(
                    ref scratch,
                    Snap(TrunkAt(plan, s, phase, trunkLength * i / trunkNodes)),
                    i - 1,
                    true
                );
            }

            // Attractors in the crown envelope, its radius moved by a smooth noise so no two
            // crowns share an outline; their count follows the crown's volume.
            var radii = plan.CrownRadii;
            var volume = radii.x * radii.y * radii.z * 2f;
            var count = math.clamp(
                (int)math.round(s.Attractors * math.min(1f, volume / s.CrownVolume)),
                math.min(24, s.Attractors),
                math.min(s.Attractors, MaxAttractors)
            );
            var lobes = random.NextFloat3(-100f, 100f);
            var lowest = plan.Base.y + math.max(2f, crownBottom) - s.Step;
            for (var k = 0; k < count; k++)
            {
                var u = InBall(ref random);
                var lobe = 1f + 0.3f * noise.snoise(math.normalizesafe(u) * 1.7f + lobes);
                var a = plan.CrownCenter + u * radii * math.min(lobe, 1.15f);
                if (a.y < lowest)
                {
                    continue;
                }

                scratch.Attractors.Add(ClampToReach(plan, s, a, s.LeafRadius.y * 1.5f + 2f));
                scratch.Nearest.Add(-1);
                scratch.NearestDistance.Add(float.MaxValue);
            }

            // The pull reaches far enough to start growth from the trunk in any crown.
            var influence = math.max(s.Influence, 0.5f * math.cmax(radii));
            var influence2 = influence * influence;
            // A narrow crown reaches its attractors from closer, so it still branches.
            var kill = math.max(s.Step, math.min(s.Kill, 0.4f * math.min(radii.x, radii.z)));
            var kill2 = kill * kill;
            var checkedNodes = 0;
            var up = new float3(0f, 1f, 0f);
            for (var round = 0; round < MaxRounds; round++)
            {
                var nodes = scratch.Nodes.Length;

                // Nearest node of every live attractor among the nodes added since last
                // round; a node close enough reaches (kills) it.
                for (var a = 0; a < scratch.Attractors.Length; a++)
                {
                    if (scratch.Nearest[a] == -2)
                    {
                        continue;
                    }

                    var target = scratch.Attractors[a];
                    for (var n = checkedNodes; n < nodes; n++)
                    {
                        var d2 = math.distancesq(target, scratch.Nodes[n]);
                        if (d2 < kill2)
                        {
                            scratch.Nearest[a] = -2;
                            break;
                        }

                        if (d2 < scratch.NearestDistance[a] && d2 < influence2)
                        {
                            scratch.NearestDistance[a] = d2;
                            scratch.Nearest[a] = n;
                        }
                    }
                }

                checkedNodes = nodes;
                for (var n = 0; n < nodes; n++)
                {
                    scratch.Pull[n] = float3.zero;
                }

                for (var a = 0; a < scratch.Attractors.Length; a++)
                {
                    var n = scratch.Nearest[a];
                    if (n >= 0)
                    {
                        scratch.Pull[n] += math.normalizesafe(
                            scratch.Attractors[a] - scratch.Nodes[n]
                        );
                    }
                }

                for (var n = 0; n < nodes && scratch.Nodes.Length < maxNodes; n++)
                {
                    var pull = scratch.Pull[n];
                    if (math.lengthsq(pull) < 1e-6f)
                    {
                        continue;
                    }

                    var direction = math.normalizesafe(math.normalizesafe(pull) + up * s.Tropism);
                    var at = scratch.Nodes[n];
                    var next = ClampToReach(
                        plan,
                        s,
                        Snap(at + direction * s.Step),
                        s.LeafRadius.y * 1.5f + 2f
                    );
                    if (math.distancesq(next, at) < 0.5f)
                    {
                        continue;
                    }

                    AddNode(ref scratch, next, n, false);
                }

                if (scratch.Nodes.Length == nodes)
                {
                    break;
                }
            }

            PipeRadii(ref scratch, s, plan);
            for (var i = 1; i < scratch.Nodes.Length; i++)
            {
                var parent = scratch.Parent[i];
                var rb = scratch.Radius[i];
                var ra =
                    scratch.Trunk[i] != 0
                        ? scratch.Radius[parent]
                        : math.min(scratch.Radius[parent], rb * 1.25f);
                AddWood(p, plan, s, parts, scratch.Nodes[parent], scratch.Nodes[i], ra, rb);
            }

            if (s.Leaves == 0)
            {
                return;
            }

            // Leaves on every outer twig (nodes carrying few tips), largest at the tips and
            // on old trees, so the clusters overlap into one crown (D-089).
            var grown = math.lerp(0.75f, 1.1f, plan.Age);
            for (var i = 0; i < scratch.Nodes.Length; i++)
            {
                var tips = scratch.Tips[i];
                var tip = scratch.Children[i] == 0;
                // The leader inside the crown carries leaves too (young narrow crowns).
                var leader = scratch.Trunk[i] != 0 && scratch.Nodes[i].y >= lowest + s.Step;
                if ((scratch.Trunk[i] != 0 && !tip && !leader) || (!leader && tips > 4))
                {
                    continue;
                }

                var radius =
                    math.lerp(s.LeafRadius.x, s.LeafRadius.y, random.NextFloat())
                    * grown
                    * (tip ? 1f : math.lerp(0.95f, 0.7f, (tips - 1f) / 3f));
                var center = scratch.Nodes[i] + new float3(0f, 0.2f * radius, 0f);
                var ellipsoid = radius * new float3(1f, random.NextFloat(0.75f, 0.95f), 1f);
                AddLeaves(p, plan, s, parts, center, ellipsoid);
            }
        }

        private static void GrowConifer(
            in TerrainParams p,
            in TreePlan plan,
            in TreeSpeciesParams s,
            ref Random random,
            float2 phase,
            ref TreeScratch scratch,
            NativeList<TreePart> parts
        )
        {
            var length = plan.Height + 2f;
            var segments = math.max(2, (int)math.ceil(length / s.Step));
            var previous = Snap(TrunkAt(plan, s, phase, 0f));
            var previousRadius = TrunkRadius(plan, s, 0f, length);
            for (var i = 1; i <= segments; i++)
            {
                var h = length * i / segments;
                var at = Snap(TrunkAt(plan, s, phase, h));
                var r = TrunkRadius(plan, s, h, length);
                AddWood(p, plan, s, parts, previous, at, previousRadius, r);
                previous = at;
                previousRadius = r;
            }

            var crownBottom = plan.CrownCenter.y - plan.Base.y;
            var crownHeight = math.max(1f, length - crownBottom);
            var maxBranch = plan.CrownRadii.x;
            var h0 = crownBottom;
            var rotation = random.NextFloat(0f, 2f * math.PI);
            var grown = math.lerp(0.8f, 1.1f, plan.Age);
            while (h0 < length - s.WhorlSpacing * 0.5f)
            {
                var share = math.saturate((length - h0) / crownHeight);
                var branches = math.max(1, s.BranchesPerWhorl + random.NextInt(-1, 2));
                rotation += random.NextFloat(0.4f, 1.2f);
                var origin = TrunkAt(plan, s, phase, h0);
                var trunkRadius = TrunkRadius(plan, s, h0, length);
                var whorlLength = maxBranch * math.pow(share, 0.9f);
                var spacing = s.WhorlSpacing * random.NextFloat(0.75f, 1.25f);

                // A dense core of needles around the trunk between this whorl and the next,
                // so the layers read as one cone (D-089).
                if (s.Leaves != 0 && whorlLength > s.Step)
                {
                    var core = math.max(s.LeafRadius.x, whorlLength * 0.55f);
                    AddLeaves(
                        p,
                        plan,
                        s,
                        parts,
                        origin + new float3(0f, spacing * 0.35f, 0f),
                        new float3(core, math.max(spacing * 0.75f, s.LeafRadius.x), core)
                    );
                }

                for (var b = 0; b < branches; b++)
                {
                    var azimuth =
                        rotation + 2f * math.PI * b / branches + random.NextFloat(-0.3f, 0.3f);
                    var branchLength = whorlLength * random.NextFloat(0.75f, 1.1f);
                    if (branchLength < s.Step * 1.5f)
                    {
                        continue;
                    }

                    var flat = new float3(math.cos(azimuth), 0f, math.sin(azimuth));
                    var steps = math.max(2, (int)math.ceil(branchLength / s.Step));
                    var at = Snap(origin);
                    var radius = math.max(p.Vegetation.MinWoodRadius, trunkRadius * 0.4f);
                    for (var k = 1; k <= steps; k++)
                    {
                        var t = (float)k / steps;
                        var direction = math.normalizesafe(
                            flat + new float3(0f, 0.3f - s.BranchDroop * 1.3f * t, 0f)
                        );
                        var next = ClampToReach(
                            plan,
                            s,
                            Snap(at + direction * (branchLength / steps)),
                            s.LeafRadius.y * 1.5f + 2f
                        );
                        var nextRadius = math.lerp(radius, p.Vegetation.MinWoodRadius, t);
                        AddWood(
                            p,
                            plan,
                            s,
                            parts,
                            at,
                            next,
                            math.lerp(radius, p.Vegetation.MinWoodRadius, (k - 1f) / steps),
                            nextRadius
                        );
                        if (s.Leaves != 0)
                        {
                            var lr =
                                math.lerp(s.LeafRadius.x, s.LeafRadius.y, random.NextFloat())
                                * grown
                                * (1f - 0.35f * t);
                            AddLeaves(
                                p,
                                plan,
                                s,
                                parts,
                                next - new float3(0f, 0.15f * lr, 0f),
                                lr * new float3(1f, 0.75f, 1f)
                            );
                        }

                        at = next;
                    }
                }

                h0 += spacing;
            }

            if (s.Leaves != 0)
            {
                var lr = s.LeafRadius.y * 0.8f;
                AddLeaves(
                    p,
                    plan,
                    s,
                    parts,
                    previous - new float3(0f, lr, 0f),
                    lr * new float3(0.8f, 1.5f, 0.8f)
                );
            }
        }

        private static void GrowPalm(
            in TerrainParams p,
            in TreePlan plan,
            in TreeSpeciesParams s,
            ref Random random,
            float2 phase,
            ref TreeScratch scratch,
            NativeList<TreePart> parts
        )
        {
            var length = plan.Height + 2f;
            var segments = math.max(2, (int)math.ceil(length / s.Step));
            var previous = Snap(TrunkAt(plan, s, phase, 0f));
            var previousRadius = plan.TrunkRadius * Flare(s, 0f);
            for (var i = 1; i <= segments; i++)
            {
                var h = length * i / segments;
                var at = Snap(TrunkAt(plan, s, phase, h));
                var r = math.max(
                    p.Vegetation.MinWoodRadius,
                    plan.TrunkRadius * (1f - 0.25f * h / length) * Flare(s, h)
                );
                AddWood(p, plan, s, parts, previous, at, previousRadius, r);
                previous = at;
                previousRadius = r;
            }

            if (s.Leaves == 0)
            {
                return;
            }

            AddLeaves(p, plan, s, parts, previous, s.LeafRadius.y * new float3(0.9f, 0.7f, 0.9f));
            var rotation = random.NextFloat(0f, 2f * math.PI);
            for (var f = 0; f < s.Fronds; f++)
            {
                var azimuth =
                    rotation + 2f * math.PI * f / s.Fronds + random.NextFloat(-0.25f, 0.25f);
                var flat = new float3(math.cos(azimuth), 0f, math.sin(azimuth));
                var rise = random.NextFloat(0.4f, 1f);
                var frond = s.FrondLength * random.NextFloat(0.8f, 1.1f);
                const int steps = 7;
                var at = previous;
                for (var k = 1; k <= steps; k++)
                {
                    var t = (float)k / steps;
                    var direction = math.normalizesafe(flat + new float3(0f, rise - 1.6f * t, 0f));
                    var next = ClampToReach(
                        plan,
                        s,
                        at + direction * (frond / steps),
                        s.LeafRadius.y + 2f
                    );
                    var lr = math.max(1f, s.LeafRadius.y * (1f - 0.65f * t));
                    AddLeaves(p, plan, s, parts, next, lr * new float3(1f, 0.35f, 1f));
                    at = next;
                }
            }
        }

        // Prop roots (mangroves): arcs from the lower trunk out and down into the ground.
        private static void GrowPropRoots(
            in TerrainParams p,
            in TreePlan plan,
            in TreeSpeciesParams s,
            ref Random random,
            float2 phase,
            NativeList<TreePart> parts
        )
        {
            var rotation = random.NextFloat(0f, 2f * math.PI);
            for (var r = 0; r < s.PropRoots; r++)
            {
                var azimuth =
                    rotation + 2f * math.PI * r / s.PropRoots + random.NextFloat(-0.3f, 0.3f);
                var flat = new float2(math.cos(azimuth), math.sin(azimuth));
                var start = TrunkAt(
                    plan,
                    s,
                    phase,
                    2f + random.NextFloat(0.15f, 0.45f) * s.CrownBase * plan.Height
                );
                var end = new float3(
                    plan.Base.x + flat.x * s.PropRootReach * random.NextFloat(0.7f, 1f),
                    plan.Base.y,
                    plan.Base.z + flat.y * s.PropRootReach * random.NextFloat(0.7f, 1f)
                );
                var control =
                    math.lerp(start, end, 0.5f) + new float3(0f, 0.35f * (start.y - end.y), 0f);
                control.xz = math.lerp(start.xz, end.xz, 0.7f);
                const int steps = 5;
                var at = Snap(start);
                var radius = math.max(p.Vegetation.MinWoodRadius * 1.2f, plan.TrunkRadius * 0.4f);
                for (var k = 1; k <= steps; k++)
                {
                    var t = (float)k / steps;
                    var next = Snap(
                        (1f - t) * (1f - t) * start + 2f * (1f - t) * t * control + t * t * end
                    );
                    next = ClampToReach(plan, s, next, radius + 2f);
                    AddWood(p, plan, s, parts, at, next, radius, radius);
                    at = next;
                }
            }
        }

        // Radii by the pipe model: a node's radius^e is the number of tips it carries, so a
        // parent's radius^e is the sum of its children's; scaled so the trunk has the
        // plan's radius, tapered a little along the bare trunk and flared at the ground.
        private static void PipeRadii(
            ref TreeScratch scratch,
            in TreeSpeciesParams s,
            in TreePlan plan
        )
        {
            var count = scratch.Nodes.Length;
            for (var i = count - 1; i >= 0; i--)
            {
                if (scratch.Tips[i] == 0)
                {
                    scratch.Tips[i] = 1;
                }

                var parent = scratch.Parent[i];
                if (parent >= 0)
                {
                    scratch.Tips[parent] += scratch.Tips[i];
                }
            }

            var inverse = 1f / s.PipeExponent;
            var scale = plan.TrunkRadius / math.pow(scratch.Tips[0], inverse);
            var baseY = plan.Base.y;
            for (var i = 0; i < count; i++)
            {
                var r = scale * math.pow(scratch.Tips[i], inverse);
                if (scratch.Trunk[i] != 0)
                {
                    var h = scratch.Nodes[i].y - baseY;
                    r *= (1f - 0.15f * h / (plan.Height + 2f)) * Flare(s, h);
                }

                scratch.Radius[i] = r;
            }
        }

        private static float TrunkRadius(
            in TreePlan plan,
            in TreeSpeciesParams s,
            float h,
            float length
        ) => plan.TrunkRadius * math.pow(1f - 0.92f * h / length, 0.9f) * Flare(s, h);

        private static void AddNode(ref TreeScratch scratch, float3 at, int parent, bool trunk)
        {
            scratch.Nodes.Add(at);
            scratch.Parent.Add(parent);
            scratch.Radius.Add(0f);
            scratch.Tips.Add(0);
            scratch.Children.Add(0);
            scratch.Trunk.Add((byte)(trunk ? 1 : 0));
            scratch.Pull.Add(float3.zero);
            if (parent >= 0)
            {
                scratch.Children[parent]++;
            }
        }

        private static void AddWood(
            in TerrainParams p,
            in TreePlan plan,
            in TreeSpeciesParams s,
            NativeList<TreePart> parts,
            float3 a,
            float3 b,
            float ra,
            float rb
        )
        {
            var min = p.Vegetation.MinWoodRadius;
            ra = math.max(ra, min);
            rb = math.max(rb, min);
            var r = math.max(ra, rb);
            parts.Add(
                new TreePart
                {
                    A = a,
                    B = b,
                    RA = ra,
                    RB = rb,
                    Outer = s.Bark,
                    Inner = s.Wood,
                    Kind = TreePartKind.Wood,
                    Min = (int3)math.floor(math.min(a, b) - r),
                    Max = (int3)math.ceil(math.max(a, b) + r),
                }
            );
        }

        private static void AddLeaves(
            in TerrainParams p,
            in TreePlan plan,
            in TreeSpeciesParams s,
            NativeList<TreePart> parts,
            float3 center,
            float3 radii
        )
        {
            radii = math.max(radii, 1f);
            center = ClampToReach(
                plan,
                s,
                center,
                math.cmax(radii.xz) * (1f + p.Vegetation.LeafLumps) + 1f
            );
            parts.Add(
                new TreePart
                {
                    A = center,
                    B = radii,
                    RA = math.cmax(radii),
                    RB = 0f,
                    Outer = s.Leaves,
                    Inner = s.Leaves,
                    Kind = TreePartKind.Leaves,
                    Porosity = s.LeafPorosity,
                    Min = (int3)math.floor(center - radii * (1f + p.Vegetation.LeafLumps)),
                    Max = (int3)math.ceil(center + radii * (1f + p.Vegetation.LeafLumps)),
                }
            );
        }

        // Keeps a point (and what it carries, `margin` voxels around it) within the species'
        // reach of the base, horizontally.
        private static float3 ClampToReach(
            in TreePlan plan,
            in TreeSpeciesParams s,
            float3 at,
            float margin
        )
        {
            var offset = at.xz - plan.Base.xz;
            var limit = math.max(0f, s.Reach - margin);
            var length = math.length(offset);
            if (length > limit && length > 0f)
            {
                at.xz = plan.Base.xz + offset * (limit / length);
            }

            return at;
        }

        // Voxel center nearest to a point.
        private static float3 Snap(float3 at) => math.floor(at) + 0.5f;

        private static float3 InBall(ref Random random)
        {
            for (var i = 0; i < 8; i++)
            {
                var u = random.NextFloat3(-1f, 1f);
                if (math.lengthsq(u) <= 1f)
                {
                    return u;
                }
            }

            return random.NextFloat3Direction() * random.NextFloat();
        }
    }
}
