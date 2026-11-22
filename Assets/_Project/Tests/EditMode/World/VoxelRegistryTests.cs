using System.Collections.Generic;
using NUnit.Framework;
using Revoxel.World;
using UnityEditor;
using UnityEngine;

namespace Revoxel.Tests.EditMode
{
    public sealed class VoxelRegistryTests
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
        public void VoxelRegistry_IdsUniqueAndAirIsZero()
        {
            var asset = WorldAssets.LoadRegistry();
            Assert.That(asset, Is.Not.Null, $"No registry at {WorldAssets.RegistryPath}");
            Assert.That(asset.TryValidate(out var assetError), Is.True, assetError);
            Assert.That(asset.TryGet(VoxelId.Air, out var air), Is.True);
            Assert.That(air.IsSolid || air.IsOpaque, Is.False);

            var valid = Registry(Air(), Type(5, "voxel.stone.name", true));
            Assert.That(valid.TryValidate(out var validError), Is.True, validError);

            AssertInvalid(Registry(Air(), Type(5, "voxel.a.name"), Type(5, "voxel.b.name")));
            AssertInvalid(Registry(Type(5, "voxel.stone.name")));
            AssertInvalid(Registry(Type(0, "voxel.air.name", true)));
            AssertInvalid(Registry(Air(), null));
            AssertInvalid(Registry(Air(), Type(1024, "voxel.big.name")));
            AssertInvalid(Registry(Air(), Type(3, string.Empty)));
        }

        [Test]
        public void VoxelRegistry_ContainsEveryVoxelAsset()
        {
            var registry = WorldAssets.LoadRegistry();
            var guids = AssetDatabase.FindAssets("t:VoxelType", new[] { WorldAssets.VoxelsFolder });

            Assert.That(guids.Length, Is.GreaterThanOrEqualTo(10));
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.LoadAssetAtPath<VoxelType>(path);

                Assert.That(type.name, Does.StartWith("Voxel_"), path);
                Assert.That(registry.TryGet(type.Id, out var listed), Is.True, path);
                Assert.That(listed, Is.SameAs(type), path);
                Assert.That(type.NameKey, Does.Match(@"^voxel\.[a-z_]+\.name$"), path);
            }
        }

        [Test]
        public void VoxelRegistry_MaterialTable_EncodesLinearColors()
        {
            var srgb = new Color(0.5f, 0.25f, 0.75f, 1f);
            var colored = Type(2, "voxel.colored.name", true);
            colored.Configure(
                2,
                "voxel.colored.name",
                true,
                true,
                srgb,
                srgb * 0.8f,
                SurfacePattern.Flat,
                1f,
                0f
            );
            var grain = Type(7, "voxel.grain.name", true);
            grain.Configure(
                7,
                "voxel.grain.name",
                true,
                true,
                Color.white,
                Color.gray,
                SurfacePattern.Grain,
                0.5f,
                0.2f
            );
            var registry = Registry(Air(), colored, grain);

            var table = registry.CreateMaterialTable();
            _created.Add(table);

            Assert.That(table.width, Is.EqualTo(8));
            Assert.That(table.height, Is.EqualTo(3));
            Assert.That(table.format, Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(table.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(table.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(table.isDataSRGB, Is.False);
            AssertPixel(table, 2, 0, srgb.linear);
            AssertPixel(table, 2, 1, (srgb * 0.8f).linear);

            var data = (Color32)table.GetPixel(7, 2);
            Assert.That(data.r, Is.EqualTo((byte)Mathf.Round(1f / 3f * 255f)));
            Assert.That(data.g, Is.EqualTo((byte)Mathf.Round(0.5f / 8f * 255f)));
            Assert.That(data.b, Is.EqualTo((byte)Mathf.Round(0.2f * 255f)));
            Assert.That(data.a, Is.EqualTo(255));
        }

        [Test]
        public void VoxelRegistry_RenderData_HasOneEntryPerPossibleId()
        {
            var registry = Registry(Air(), Type(5, "voxel.stone.name", true));

            using var data = registry.CreateRenderData(Unity.Collections.Allocator.Temp);

            Assert.That(data.Length, Is.EqualTo(WorldConstants.MaxVoxelTypes));
            Assert.That(data[5].IsSolid && data[5].IsOpaque, Is.True);
            Assert.That(data[0].IsSolid || data[0].IsOpaque, Is.False);
            Assert.That(data[6].IsSolid || data[6].IsOpaque, Is.False);
            Assert.That(registry.IsRegistered(new VoxelId(5)), Is.True);
            Assert.That(registry.IsRegistered(new VoxelId(600)), Is.False);
        }

        private static void AssertPixel(Texture2D table, int x, int y, Color expected)
        {
            Assert.That(
                (Color32)table.GetPixel(x, y),
                Is.EqualTo((Color32)expected),
                $"({x}, {y})"
            );
        }

        private static void AssertInvalid(VoxelRegistry registry)
        {
            Assert.That(registry.TryValidate(out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        private VoxelType Air() => Type(0, "voxel.air.name");

        private VoxelType Type(ushort id, string nameKey, bool solid = false)
        {
            var type = ScriptableObject.CreateInstance<VoxelType>();
            type.Configure(
                id,
                nameKey,
                solid,
                solid,
                Color.white,
                Color.gray,
                SurfacePattern.Flat,
                1f,
                0f
            );
            _created.Add(type);
            return type;
        }

        private VoxelRegistry Registry(params VoxelType[] types)
        {
            var registry = ScriptableObject.CreateInstance<VoxelRegistry>();
            registry.SetTypes(types);
            _created.Add(registry);
            return registry;
        }
    }
}
