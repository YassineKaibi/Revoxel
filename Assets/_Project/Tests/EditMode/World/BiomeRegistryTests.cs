using System.Linq;
using NUnit.Framework;
using Revoxel.World;
using UnityEditor;
using UnityEngine;

namespace Revoxel.Tests.EditMode
{
    public sealed class BiomeRegistryTests
    {
        private static readonly string[] SpecBiomes =
        {
            "biome.coast.name",
            "biome.reef.name",
            "biome.plains.name",
            "biome.forest.name",
            "biome.wetland.name",
            "biome.canyon.name",
            "biome.mountains.name",
            "biome.waste_lands.name",
        };

        [Test]
        public void BiomeRegistry_AssetIsValidAndDense()
        {
            var registry = WorldAssets.LoadBiomeRegistry();
            var settings = WorldAssets.LoadTerrainSettings();

            Assert.That(registry, Is.Not.Null, WorldAssets.BiomeRegistryPath);
            Assert.That(settings.Biomes, Is.SameAs(registry));
            Assert.That(
                registry.TryValidate(settings.SubsurfaceDepthMeters, out var error),
                Is.True,
                error
            );
            for (var i = 0; i < registry.Count; i++)
            {
                Assert.That(registry.Biomes[i].Id.Value, Is.EqualTo(i));
                Assert.That(registry.TryGet(new BiomeId((byte)i), out var biome), Is.True);
                Assert.That(biome, Is.SameAs(registry.Biomes[i]));
            }

            var assets = AssetDatabase
                .FindAssets("t:Biome", new[] { WorldAssets.BiomesFolder })
                .Select(guid =>
                    AssetDatabase.LoadAssetAtPath<Biome>(AssetDatabase.GUIDToAssetPath(guid))
                )
                .ToArray();
            Assert.That(assets.Length, Is.EqualTo(registry.Count));
            foreach (var biome in assets)
            {
                Assert.That(biome.name, Does.StartWith("Biome_"));
                Assert.That(registry.Biomes, Does.Contain(biome), biome.name);
            }
        }

        [Test]
        public void BiomeRegistry_ListIsTheSpecProposal()
        {
            var keys = WorldAssets.LoadBiomeRegistry().Biomes.Select(b => b.NameKey).ToArray();

            Assert.That(keys, Is.EqualTo(SpecBiomes));
        }

        [Test]
        public void BiomeRegistry_RejectsBadData()
        {
            var depth = WorldAssets.LoadTerrainSettings().SubsurfaceDepthMeters;
            var source = WorldAssets.LoadBiomeRegistry().Biomes;
            var copies = source.Select(Object.Instantiate).ToArray();
            var registry = ScriptableObject.CreateInstance<BiomeRegistry>();
            try
            {
                registry.SetBiomes(copies);
                Assert.That(registry.TryValidate(depth, out var error), Is.True, error);

                Expect(registry, depth, copies[2], "_id", so => so.intValue = 1, "index");
                Expect(
                    registry,
                    depth,
                    copies[0],
                    "_nameKey",
                    so => so.stringValue = "",
                    "name key"
                );
                Expect(
                    registry,
                    depth,
                    copies[3],
                    "_surface",
                    so => so.objectReferenceValue = null,
                    "required"
                );
                Expect(
                    registry,
                    depth,
                    copies[4],
                    "_soilDepthMeters",
                    so => so.vector2Value = new Vector2(0.5f, depth + 1f),
                    "soil depth"
                );
                Expect(registry, depth, copies[5], "_strata", so => so.arraySize = 0, "strata");

                registry.SetBiomes(new[] { copies[0], copies[0] });
                Assert.That(registry.TryValidate(depth, out _), Is.False, "duplicate id");
                registry.SetBiomes(new[] { copies[1] });
                Assert.That(registry.TryValidate(depth, out _), Is.False, "ids not from 0");
                registry.SetBiomes(new Biome[0]);
                Assert.That(registry.TryValidate(depth, out _), Is.False, "empty registry");
            }
            finally
            {
                Object.DestroyImmediate(registry);
                foreach (var copy in copies)
                {
                    Object.DestroyImmediate(copy);
                }
            }
        }

        // D-072: the reef biome has coral heads; a reef without corals or rock is refused.
        [Test]
        public void Biome_ReefNeedsCoralsAndRock()
        {
            var depth = WorldAssets.LoadTerrainSettings().SubsurfaceDepthMeters;
            Biome reef = null;
            foreach (var biome in WorldAssets.LoadBiomeRegistry().Biomes)
            {
                if (biome.HasReef)
                {
                    reef = biome;
                }
            }

            Assert.That(reef, Is.Not.Null, "no biome has a reef");
            Assert.That(reef.NameKey, Is.EqualTo("biome.reef.name"));
            var copy = Object.Instantiate(reef);
            try
            {
                var so = new SerializedObject(copy);
                so.FindProperty("_reefCorals").arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(copy.TryValidate(depth, out var error), Is.False);
                Assert.That(error, Does.Contain("reef"));

                so.FindProperty("_reefCorals").arraySize = 1;
                so.FindProperty("_reefRock").objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(copy.TryValidate(depth, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        // Breaks one field of one biome, expects the registry to fail with a message
        // containing the hint, then restores the field.
        private static void Expect(
            BiomeRegistry registry,
            float depth,
            Biome biome,
            string field,
            System.Action<SerializedProperty> breakIt,
            string hint
        )
        {
            var backup = Object.Instantiate(biome);
            try
            {
                var so = new SerializedObject(biome);
                breakIt(so.FindProperty(field));
                so.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(registry.TryValidate(depth, out var error), Is.False, field);
                Assert.That(error, Does.Contain(hint), field);
            }
            finally
            {
                EditorUtility.CopySerialized(backup, biome);
                Object.DestroyImmediate(backup);
            }
        }
    }
}
