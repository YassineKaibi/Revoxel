using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;

namespace Revoxel.Tests.EditMode
{
    public sealed class LocalizationTests
    {
        private static readonly string[] RequiredCodes = { "en", "fr" };

        [Test]
        public void Localization_LocalesAreFrAndEnOnly()
        {
            var codes = LocalizationEditorSettings
                .GetLocales()
                .Select(locale => locale.Identifier.Code)
                .OrderBy(code => code)
                .ToArray();

            Assert.That(codes, Is.EqualTo(RequiredCodes));
        }

        [Test]
        public void Localization_EveryKeyHasFrenchAndEnglish()
        {
            var collections = LocalizationEditorSettings.GetStringTableCollections();
            Assert.That(collections.Count, Is.GreaterThan(0), "No string table collection found");

            var problems = new List<string>();
            foreach (var collection in collections)
            {
                foreach (var code in RequiredCodes)
                {
                    if (collection.GetTable(code) is not StringTable table)
                    {
                        problems.Add($"{collection.TableCollectionName}: no '{code}' table");
                        continue;
                    }

                    foreach (var shared in collection.SharedData.Entries)
                    {
                        var entry = table.GetEntry(shared.Id);
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
                        {
                            problems.Add(
                                $"{collection.TableCollectionName}/{shared.Key}: '{code}' is empty"
                            );
                        }
                    }
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void Localization_DebugOverlayKeys_ExistAndRetiredKeysAreGone()
        {
            var shared = LocalizationEditorSettings
                .GetStringTableCollection(Revoxel.UI.DebugOverlay.TableName)
                .SharedData;
            var keys = new[]
            {
                Revoxel.UI.DebugOverlay.FrameTimeKey,
                Revoxel.UI.DebugOverlay.LoadedChunksKey,
                Revoxel.UI.DebugOverlay.ChunkViewsKey,
                Revoxel.UI.DebugOverlay.PendingJobsKey,
                Revoxel.UI.DebugOverlay.VoxelMemoryKey,
                Revoxel.UI.DebugOverlay.DetailChunksKey,
                Revoxel.UI.DebugOverlay.DetailMemoryKey,
                Revoxel.UI.DebugOverlay.BiomeKey,
            };

            foreach (var key in keys)
            {
                Assert.That(shared.Contains(key), Is.True, key);
            }

            // Retired by D-054.
            Assert.That(shared.Contains("ui.debug.chunks"), Is.False);
            Assert.That(shared.Contains("ui.debug.dirty_queue"), Is.False);
        }

        // D-065: the overlay's biome line and every biome name, in French and English.
        [Test]
        public void Localization_BiomeNamesAndOverlayKey_Exist()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(
                Revoxel.UI.DebugOverlay.TableName
            );
            var keys = WorldAssets
                .LoadBiomeRegistry()
                .Biomes.Select(biome => biome.NameKey)
                .Append(Revoxel.UI.DebugOverlay.BiomeKey);
            foreach (var key in keys)
            {
                var shared = collection.SharedData.GetEntry(key);
                Assert.That(shared, Is.Not.Null, key);
                foreach (var code in RequiredCodes)
                {
                    var table = (StringTable)collection.GetTable(code);
                    var entry = table.GetEntry(shared.Id);
                    Assert.That(entry?.Value, Is.Not.Null.And.Not.Empty, $"{key} ({code})");
                }
            }
        }
    }
}
