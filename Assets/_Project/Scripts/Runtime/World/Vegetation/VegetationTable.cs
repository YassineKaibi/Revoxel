using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // One TreeSpecies converted for Burst (D-085); lengths in voxels.
    public struct TreeSpeciesParams
    {
        public TreeForm Form;
        public float2 Height;
        public float2 TrunkRadius;
        public float Lean;
        public float Wander;
        public float CrownBase;
        public float2 CrownRadius;
        public float Leader;

        // Crown volume of the largest tree, voxels³: attractors scale with a crown's volume.
        public float CrownVolume;
        public int Attractors;
        public float Step;
        public float Influence;
        public float Kill;
        public float Tropism;
        public int MaxNodes;
        public float PipeExponent;
        public float WhorlSpacing;
        public int BranchesPerWhorl;
        public float BranchDroop;
        public int Fronds;
        public float FrondLength;
        public float RootFlare;
        public float FlareHeight;
        public int PropRoots;
        public float PropRootReach;
        public float2 LeafRadius;
        public float LeafPorosity;
        public ushort Bark;
        public ushort Wood;

        // 0 for a leafless tree.
        public ushort Leaves;
        public float MaxSlope;
        public float MinTemperatureC;
        public float MaxWaterDepth;
        public bool GrowsOnShore;
        public float Spacing;

        // Farthest any voxel of the tree lies from its anchor, horizontally (D-084), and
        // highest above its base.
        public float Reach;
        public float Top;

        public static TreeSpeciesParams Create(TreeSpecies s)
        {
            const float v = WorldConstants.VoxelsPerMeter;
            var height = (float2)s.HeightMeters * v;
            var leaf = (float2)s.LeafRadiusMeters * v;
            // Old trees spread up to 1.25 times their species' crown share, 1.3 times on one axis.
            var crown = s.CrownRadius.y * height.y * 1.25f * 1.3f;
            var p = new TreeSpeciesParams
            {
                Form = s.Form,
                Height = height,
                TrunkRadius = (float2)s.TrunkRadiusMeters * v,
                Lean = s.Lean,
                Wander = s.Wander,
                CrownBase = s.CrownBase,
                CrownRadius = s.CrownRadius,
                Leader = s.Leader,
                Attractors = s.Attractors,
                Step = s.StepMeters * v,
                Influence = s.InfluenceMeters * v,
                Kill = s.KillMeters * v,
                Tropism = s.Tropism,
                MaxNodes = s.MaxNodes,
                PipeExponent = s.PipeExponent,
                WhorlSpacing = s.WhorlSpacingMeters * v,
                BranchesPerWhorl = s.BranchesPerWhorl,
                BranchDroop = s.BranchDroop,
                Fronds = s.Fronds,
                FrondLength = s.FrondLengthMeters * v,
                RootFlare = s.RootFlare,
                FlareHeight = s.FlareHeightMeters * v,
                PropRoots = s.PropRoots,
                PropRootReach = s.PropRootReachMeters * v,
                LeafRadius = leaf,
                LeafPorosity = s.LeafPorosity,
                Bark = s.Bark.Id.Value,
                Wood = s.Wood.Id.Value,
                Leaves = s.Leaves != null ? s.Leaves.Id.Value : (ushort)0,
                MaxSlope = s.MaxSlope,
                MinTemperatureC = s.MinTemperatureC,
                MaxWaterDepth = s.MaxWaterDepthMeters * v,
                GrowsOnShore = s.GrowsOnShore,
                Spacing = s.SpacingMeters * v,
            };

            // Growth clamps every node inside these (TreeGrowth), so a column that gathers
            // the anchors within Reach sees every tree that reaches into it. Plans vary a
            // tree's height by up to 10 % and its wander by up to 1.6 times (TreeAnchors).
            var tallest = height.y * TreeAnchors.MaxHeightScale;
            var spread = math.max(crown, math.max(p.FrondLength, p.PropRootReach));
            p.Reach =
                tallest * (p.Lean + p.Wander * TreeAnchors.MaxWanderScale)
                + spread
                + leaf.y * 1.5f
                + p.Step
                + 4f;
            p.Top =
                tallest * (1f + p.Wander * TreeAnchors.MaxWanderScale)
                + leaf.y * 1.5f
                + p.Step
                + 4f;
            var full = s.CrownRadius.y * height.y;
            p.CrownVolume = math.max(1f, full * full * (1f - p.CrownBase) * height.y * 0.5f);
            return p;
        }
    }

    // Vegetation of one biome (D-085): tree cover, up to four species with cumulative
    // weights, and its ground plants in the plant table.
    public struct BiomeVegetation
    {
        public float TreeCover;
        public int TreeCount;
        public int4 Trees;

        // Cumulative, the last one 1.
        public float4 TreeWeights;
        public int PlantStart;
        public int PlantCount;

        // Tallest plant of the biome, voxels.
        public int MaxPlantHeight;
    }

    // One BiomePlant converted for Burst (D-087); lengths in voxels.
    public struct GroundPlantParams
    {
        public ushort Stem;
        public ushort Head;
        public float Cover;
        public float ClumpFrequency;
        public int2 Height;
        public int MaxWaterDepth;
        public bool OnShore;
        public float Bend;
    }

    // The vegetation tables in TerrainParams (D-085): persistent read-only arrays like the
    // water plan (D-078), allocated by TerrainParams.Create and freed by its creator.
    public struct VegetationTable
    {
        [ReadOnly]
        public NativeArray<TreeSpeciesParams> Species;

        // Indexed by biome id.
        [ReadOnly]
        public NativeArray<BiomeVegetation> Biomes;

        [ReadOnly]
        public NativeArray<GroundPlantParams> Plants;

        // Side of a tree candidate cell, voxels.
        public int Cell;
        public float MinWoodRadius;
        public float Bark;
        public float LeafFrequency;
        public float LeafLumps;
        public int DetailMaxLevel;

        // Largest reach, spacing and top over all species, and the tallest plant, voxels.
        public float MaxReach;
        public float MaxSpacing;
        public float MaxTop;
        public int MaxPlantHeight;
        public uint Seed;
        public float3 LeafOffset;

        public bool HasTrees => Species.IsCreated && Species.Length > 0 && MaxReach > 0f;

        public static VegetationTable Create(
            VegetationSettings settings,
            BiomeRegistry biomes,
            uint seed,
            Allocator allocator
        )
        {
            const float v = WorldConstants.VoxelsPerMeter;
            var table = new VegetationTable
            {
                Cell = math.max(4, (int)math.round(settings.CellMeters * v)),
                MinWoodRadius = settings.MinWoodRadiusVoxels,
                Bark = settings.BarkVoxels,
                LeafFrequency = 1f / (settings.LeafNoiseMeters * v),
                LeafLumps = settings.LeafLumps,
                DetailMaxLevel = settings.DetailMaxLevel,
                Seed = seed,
            };
            var random = Random.CreateFromIndex(seed ^ 0x5EEDu);
            table.LeafOffset = random.NextFloat3(-1000f, 1000f);

            var species = settings.Species;
            table.Species = new NativeArray<TreeSpeciesParams>(species.Length, allocator);
            for (var i = 0; i < species.Length; i++)
            {
                var s = TreeSpeciesParams.Create(species[i]);
                table.Species[i] = s;
                table.MaxReach = math.max(table.MaxReach, s.Reach);
                table.MaxSpacing = math.max(table.MaxSpacing, s.Spacing);
                table.MaxTop = math.max(table.MaxTop, s.Top);
            }

            var count = biomes.Count;
            var plantCount = 0;
            for (var i = 0; i < count; i++)
            {
                plantCount += biomes.Biomes[i].Plants?.Length ?? 0;
            }

            table.Biomes = new NativeArray<BiomeVegetation>(count, allocator);
            table.Plants = new NativeArray<GroundPlantParams>(plantCount, allocator);
            var next = 0;
            for (var i = 0; i < count; i++)
            {
                var biome = biomes.Biomes[i];
                var b = new BiomeVegetation { TreeCover = biome.TreeCover, PlantStart = next };
                var trees = biome.Trees;
                var total = 0f;
                for (var k = 0; k < trees.Length; k++)
                {
                    total += biome.TreeWeights[k];
                }

                var sum = 0f;
                for (var k = 0; k < trees.Length; k++)
                {
                    sum += biome.TreeWeights[k];
                    b.Trees[k] = settings.IndexOf(trees[k]);
                    b.TreeWeights[k] = k == trees.Length - 1 ? 1f : sum / total;
                }

                b.TreeCount = trees.Length;
                foreach (var plant in biome.Plants)
                {
                    var plantParams = new GroundPlantParams
                    {
                        Stem = plant.Stem.Id.Value,
                        Head = plant.Head != null ? plant.Head.Id.Value : (ushort)0,
                        Cover = plant.Cover,
                        ClumpFrequency = 1f / (plant.ClumpMeters * v),
                        Height = new int2(
                            math.max(1, (int)math.round(plant.HeightMeters.x * v)),
                            math.max(1, (int)math.round(plant.HeightMeters.y * v))
                        ),
                        MaxWaterDepth = (int)math.round(plant.MaxWaterDepthMeters * v),
                        OnShore = plant.OnShore,
                        Bend = plant.Bend,
                    };
                    table.Plants[next++] = plantParams;
                    b.MaxPlantHeight = math.max(b.MaxPlantHeight, plantParams.Height.y);
                }

                b.PlantCount = next - b.PlantStart;
                table.MaxPlantHeight = math.max(table.MaxPlantHeight, b.MaxPlantHeight);
                table.Biomes[i] = b;
            }

            return table;
        }

        public static VegetationTable Empty(Allocator allocator) =>
            new VegetationTable
            {
                Species = new NativeArray<TreeSpeciesParams>(0, allocator),
                Biomes = new NativeArray<BiomeVegetation>(0, allocator),
                Plants = new NativeArray<GroundPlantParams>(0, allocator),
                Cell = 16,
            };

        public void Dispose()
        {
            if (Species.IsCreated)
            {
                Species.Dispose();
            }

            if (Biomes.IsCreated)
            {
                Biomes.Dispose();
            }

            if (Plants.IsCreated)
            {
                Plants.Dispose();
            }

            this = default;
        }
    }

    public enum TreePartKind : byte
    {
        // A tapered capsule from A (radius RA) to B (radius RB): bark over a wood core.
        Wood = 0,

        // An ellipsoid at A with radii B, lumpy and porous.
        Leaves = 1,
    }

    // One piece of a grown tree, in world voxel coordinates (D-083).
    public struct TreePart
    {
        public float3 A;
        public float3 B;
        public float RA;
        public float RB;

        // Bark and wood for wood; the leaf type twice for leaves.
        public ushort Outer;
        public ushort Inner;
        public TreePartKind Kind;
        public float Porosity;

        // Inclusive voxel bounds.
        public int3 Min;
        public int3 Max;
    }

    // A tree before its skeleton is grown (D-084): everything that follows from its anchor,
    // in voxels. Distance detail draws trees from this alone (D-088).
    public struct TreePlan
    {
        public int Species;
        public uint Seed;

        // 0 a sapling to 1 an old tree (D-089), and how far its trunk wanders against its
        // species' wander.
        public float Age;
        public float WanderScale;

        // Anchor column center at the base height (the lowest ground under the trunk).
        public float3 Base;
        public float Height;
        public float TrunkRadius;

        // Horizontal offset of the top from the base.
        public float2 Lean;

        // Crown ellipsoid (broadleaf), cone base (conifer) or frond disk (palm).
        public float3 CrownCenter;
        public float3 CrownRadii;
    }
}
