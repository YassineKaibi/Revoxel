using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using UnityEditor;
using UnityEngine;

namespace Revoxel.Tests.EditMode
{
    public sealed class BlockRegistryTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
            {
                Object.DestroyImmediate(obj);
            }

            _created.Clear();
        }

        [Test]
        public void BlockRegistry_IdsUniqueAndAirIsZero()
        {
            var registry = WorldAssets.LoadRegistry();
            Assert.That(registry, Is.Not.Null, $"No registry at {WorldAssets.RegistryPath}");
            Assert.That(registry.TryValidate(out var error), Is.True, error);

            var duplicate = CreateRegistry(Block(0, false), Block(1, true), Block(1, true));
            Assert.That(duplicate.TryValidate(out error), Is.False);
            Assert.That(error, Does.Contain("twice"));

            var noAir = CreateRegistry(Block(1, true), Block(2, true));
            Assert.That(noAir.TryValidate(out error), Is.False);
            Assert.That(error, Does.Contain("Air"));

            var solidZero = CreateRegistry(Block(0, true));
            Assert.That(solidZero.TryValidate(out _), Is.False);

            var empty = CreateRegistry(Block(0, false), null);
            Assert.That(empty.TryValidate(out _), Is.False);
        }

        [Test]
        public void BlockRegistry_ContainsEveryBlockAsset()
        {
            var registry = WorldAssets.LoadRegistry();
            var guids = AssetDatabase.FindAssets("t:BlockType", new[] { WorldAssets.BlocksFolder });
            Assert.That(guids.Length, Is.GreaterThan(0));

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var block = AssetDatabase.LoadAssetAtPath<BlockType>(path);
                Assert.That(block.name, Does.StartWith("Block_"), path);
                Assert.That(registry.TryGet(block.Id, out var listed), Is.True, path);
                Assert.That(listed, Is.SameAs(block), path);
            }

            Assert.That(registry.Count, Is.EqualTo(guids.Length));
        }

        private BlockType Block(byte id, bool solid)
        {
            var block = ScriptableObject.CreateInstance<BlockType>();
            block.Configure(id, $"block.test{id}.name", solid, solid, Color.gray, Color.white);
            _created.Add(block);
            return block;
        }

        private BlockRegistry CreateRegistry(params BlockType[] blocks)
        {
            var registry = ScriptableObject.CreateInstance<BlockRegistry>();
            registry.SetBlocks(blocks);
            _created.Add(registry);
            return registry;
        }
    }
}
