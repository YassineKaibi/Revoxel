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
    }
}
