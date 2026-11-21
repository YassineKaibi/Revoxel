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
    }
}
