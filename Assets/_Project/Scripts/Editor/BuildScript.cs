using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Revoxel.EditorTools
{
    public static class BuildScript
    {
        public const string DefaultWindowsOutput = "Builds/Windows/Revoxel.exe";

        // Output path arguments, in order of precedence: -buildOutput (unity build
        // --execute-method), -customBuildPath (GameCI unity-builder).
        private static readonly string[] OutputArguments = { "-buildOutput", "-customBuildPath" };

        // Builds every enabled scene of the Build Settings list (Bootstrap, Island)
        // to a Windows x64 Mono player. Exits the editor with code 1 on failure.
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = OutputPath(Environment.GetCommandLineArgs()),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    $"[Build] Windows build {report.summary.result}: {report.summary.totalErrors} error(s)."
                );
                EditorApplication.Exit(1);
            }
        }

        public static string OutputPath(string[] args)
        {
            foreach (var name in OutputArguments)
            {
                for (var i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == name && !string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        return args[i + 1];
                    }
                }
            }

            return DefaultWindowsOutput;
        }

        private static string[] EnabledScenes()
        {
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }

            return scenes.ToArray();
        }
    }
}
