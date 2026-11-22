using NUnit.Framework;
using Revoxel.World;
using UnityEditor;
using UnityEngine;

namespace Revoxel.Tests.EditMode
{
    public sealed class VoxelShaderTests
    {
        [Test]
        public void Shader_VoxelCompilesWithoutErrors()
        {
            var shader = Shader.Find(VoxelShaderGlobals.ShaderName);

            Assert.That(shader, Is.Not.Null, $"Shader '{VoxelShaderGlobals.ShaderName}' not found");
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
            {
                Assert.That(
                    message.severity,
                    Is.Not.EqualTo(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),
                    $"{message.message} (line {message.line})"
                );
            }
        }

        [Test]
        public void Material_VoxelUsesVoxelShader()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(WorldAssets.MaterialPath);

            Assert.That(material, Is.Not.Null, $"No material at {WorldAssets.MaterialPath}");
            Assert.That(material.shader.name, Is.EqualTo(VoxelShaderGlobals.ShaderName));
        }

        [Test]
        public void WorldRender_SettingsAsset_IsValid()
        {
            var settings = WorldAssets.LoadRenderSettings();

            Assert.That(settings, Is.Not.Null, $"No settings at {WorldAssets.RenderSettingsPath}");
            Assert.That(settings.TryValidate(out var error), Is.True, error);
            Assert.That(settings.name, Does.StartWith("Placeholder_"));
        }

        [Test]
        public void VoxelShaderGlobals_SetDefaults_UsesSettings()
        {
            var settings = WorldAssets.LoadRenderSettings();
            var table = new Texture2D(10, 3);
            try
            {
                VoxelShaderGlobals.SetDefaults(settings);
                VoxelShaderGlobals.SetMaterialTable(table);

                Assert.That(Shader.GetGlobalFloat(VoxelShaderGlobals.CorruptionId), Is.EqualTo(1f));
                Assert.That(
                    Shader.GetGlobalFloat(VoxelShaderGlobals.AoMinId),
                    Is.EqualTo(settings.AoMin)
                );
                Assert.That(
                    (Color)Shader.GetGlobalVector(VoxelShaderGlobals.CorruptionTintId),
                    Is.EqualTo(settings.CorruptionTint.linear)
                );
                Assert.That(
                    Shader.GetGlobalFloat(VoxelShaderGlobals.MaterialTableWidthId),
                    Is.EqualTo(10f)
                );
                Assert.That(
                    Shader.GetGlobalTexture(VoxelShaderGlobals.MaterialTableId),
                    Is.SameAs(table)
                );

                VoxelShaderGlobals.SetCorruption(0.25f);
                Assert.That(
                    Shader.GetGlobalFloat(VoxelShaderGlobals.CorruptionId),
                    Is.EqualTo(0.25f)
                );
            }
            finally
            {
                Object.DestroyImmediate(table);
                VoxelShaderGlobals.SetDefaults(settings);
            }
        }
    }
}
