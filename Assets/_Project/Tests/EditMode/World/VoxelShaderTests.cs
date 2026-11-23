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
        public void Shader_WaterCompilesWithoutErrors()
        {
            var shader = Shader.Find(VoxelShaderGlobals.WaterShaderName);

            Assert.That(
                shader,
                Is.Not.Null,
                $"Shader '{VoxelShaderGlobals.WaterShaderName}' not found"
            );
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
        public void Material_WaterUsesWaterShaderAndIsTransparent()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(WorldAssets.WaterMaterialPath);

            Assert.That(material, Is.Not.Null, $"No material at {WorldAssets.WaterMaterialPath}");
            Assert.That(material.shader.name, Is.EqualTo(VoxelShaderGlobals.WaterShaderName));
            Assert.That(
                material.renderQueue,
                Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent)
            );
        }

        // D-073: the water look and the underwater fog come from the settings.
        [Test]
        public void VoxelShaderGlobals_SetDefaults_SetsWaterAndUnderwater()
        {
            var settings = WorldAssets.LoadRenderSettings();
            try
            {
                VoxelShaderGlobals.SetDefaults(settings);

                Assert.That(
                    (Color)Shader.GetGlobalVector(VoxelShaderGlobals.WaterScatterId),
                    Is.EqualTo(settings.WaterScatter.linear)
                );
                Assert.That(
                    (Vector3)Shader.GetGlobalVector(VoxelShaderGlobals.WaterAbsorptionId),
                    Is.EqualTo(settings.WaterAbsorptionPerMeter)
                );
                Assert.That(
                    Shader.GetGlobalVector(VoxelShaderGlobals.WaterSurfaceId).y,
                    Is.EqualTo(1f / settings.WaterWaveScaleMeters).Within(1e-6f)
                );
                Assert.That(
                    Shader.GetGlobalFloat(VoxelShaderGlobals.UnderwaterDensityId),
                    Is.EqualTo(settings.UnderwaterDensityPerMeter)
                );
                Assert.That(Shader.GetGlobalFloat(VoxelShaderGlobals.UnderwaterId), Is.EqualTo(0f));

                VoxelShaderGlobals.SetUnderwater(true);
                Assert.That(Shader.GetGlobalFloat(VoxelShaderGlobals.UnderwaterId), Is.EqualTo(1f));
            }
            finally
            {
                VoxelShaderGlobals.SetDefaults(settings);
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
