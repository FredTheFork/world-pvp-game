#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WorldPvp.Phase1.Editor
{
    /// <summary>Build command for the pinned Unity 6.3/Linux dedicated-server target.</summary>
    public static class PhaseEightDedicatedServerBuildMenu
    {
        [MenuItem("Tools/World PvP/Phase 8/Build Linux Dedicated Server")]
        public static void BuildLinuxDedicatedServer()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("Enable the generated World PvP arena scene in Build Settings first.");
            }

            const string outputDirectory = "Builds/WorldPvpDedicatedServer";
            const string outputPath = outputDirectory + "/WorldPvpDedicatedServer.x86_64";
            Directory.CreateDirectory(outputDirectory);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.StrictMode
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report == null || report.summary.result != BuildResult.Succeeded)
            {
                string result = report != null ? report.summary.result.ToString() : "No build report";
                throw new InvalidOperationException("Phase 8 Linux dedicated-server build failed: " + result);
            }

            Debug.Log(
                "[Phase 8] Linux dedicated-server player built to " + outputPath +
                ". This build has not been deployed or browser-acceptance tested; supply server-only environment variables before launching it.");
        }
    }
}
#endif
