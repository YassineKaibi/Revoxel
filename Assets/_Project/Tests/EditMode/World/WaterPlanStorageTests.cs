using NUnit.Framework;
using Revoxel.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Revoxel.Tests.EditMode
{
    // The water plan in native arrays (D-078).
    public sealed class WaterPlanStorageTests
    {
        // Two generation jobs reading one plan at once: the read-only arrays nested in
        // TerrainParams must not trip the job safety system, and give the same answers.
        [Test]
        public void WaterPlan_NestedArraysWorkInParallelJobs()
        {
            var p = WorldAssets.TerrainParams(Seeds.Fixture);
            using var a = new NativeReference<ColumnRange>(Allocator.TempJob);
            using var b = new NativeReference<ColumnRange>(Allocator.TempJob);
            var first = new ColumnHeightJob
            {
                Params = p,
                Column = new int2(0, 0),
                Range = a,
            }.Schedule();
            var second = new ColumnHeightJob
            {
                Params = p,
                Column = new int2(5, 3),
                Range = b,
            }.Schedule();
            JobHandle.CombineDependencies(first, second).Complete();

            using var alone = new NativeReference<ColumnRange>(Allocator.TempJob);
            new ColumnHeightJob
            {
                Params = p,
                Column = new int2(5, 3),
                Range = alone,
            }.Run();
            Assert.That(b.Value.Height, Is.EqualTo(alone.Value.Height));
            Assert.That(b.Value.Water, Is.EqualTo(alone.Value.Water));
            Assert.That(a.Value.Height.y, Is.GreaterThan(0));
        }

        [Test]
        public void TerrainParams_DisposeFreesThePlan()
        {
            var p = TerrainParams.Create(
                new WorldSeed(Seeds.Fixture),
                WorldAssets.LoadTerrainSettings()
            );
            Assert.That(p.WaterPlan.IsCreated, Is.True);

            p.Dispose();

            Assert.That(p.WaterPlan.IsCreated, Is.False);
            Assert.That(p.WaterPlan.Nodes.IsCreated, Is.False);
        }
    }
}
