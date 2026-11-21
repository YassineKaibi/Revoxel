using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Revoxel.Tests.EditMode
{
    public sealed class CompilerSettingsTests
    {
        private const string ProjectRoot = "Assets/_Project";
        private const string WarningsAsErrors = "-warnaserror+";

        // D-038: each project assembly carries its own csc.rsp with warnings as errors.
        [Test]
        public void Compiler_EveryProjectAssemblyHasWarningsAsErrors()
        {
            var asmdefs = Directory.GetFiles(ProjectRoot, "*.asmdef", SearchOption.AllDirectories);
            Assert.That(asmdefs.Length, Is.GreaterThan(0), "No asmdef found");

            var problems = new List<string>();
            foreach (var asmdef in asmdefs)
            {
                var rsp = Path.Combine(Path.GetDirectoryName(asmdef) ?? ProjectRoot, "csc.rsp");
                if (!File.Exists(rsp) || !File.ReadAllText(rsp).Contains(WarningsAsErrors))
                {
                    problems.Add($"{asmdef}: missing {rsp} with {WarningsAsErrors}");
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
