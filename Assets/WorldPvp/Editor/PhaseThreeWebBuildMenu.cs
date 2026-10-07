using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WorldPvp.Phase1.Editor
{
    /// <summary>
    /// Builds the generated scene as a threaded Unity Web target for Cesium + UGS Relay WebSockets.
    /// Hosting headers and .br response metadata are supplied by the deployment server, not this build.
    /// </summary>
    public static class PhaseThreeWebBuildMenu
    {
        private const string OutputDirectory = "Build/WebGL";
        private const string TemplateName = "PROJECT:WorldPvp";

        [MenuItem("Tools/World PvP/Phase 3/Build WebGL (Brotli + Threads)")]
        [MenuItem("Tools/World PvP/Phase 6/Build Desktop WebGL Baseline (Brotli + Threads + Frame Stats)")]
        public static void BuildWebGl()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog(
                    "Web build unavailable",
                    "Wait for Unity imports and script compilation to finish, then run the WebGL build again.",
                    "OK");
                return;
            }

            if (!Directory.Exists(Path.Combine(Application.dataPath, "WebGLTemplates/WorldPvp")))
            {
                Debug.LogError("[Phase 3] The project WebGL template Assets/WebGLTemplates/WorldPvp is missing.");
                return;
            }

            EditorBuildSettingsScene[] enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .ToArray();
            if (enabledScenes.Length == 0)
            {
                Debug.LogError("[Phase 3] Build Settings has no enabled scenes. Rebuild the generated world scene first.");
                return;
            }

            Directory.CreateDirectory(OutputDirectory);
            PlayerSettings.WebGL.template = TemplateName;
            PlayerSettings.WebGL.threadsSupport = true;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = enabledScenes.Select(scene => scene.path).ToArray(),
                locationPathName = OutputDirectory,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log(
                    "[Phase 3] WebGL build succeeded at " + OutputDirectory +
                    ". Before serving it, apply COOP/COEP headers and Content-Encoding: br to compressed files.");
            }
            else
            {
                Debug.LogError(
                    "[Phase 3] WebGL build failed with result " + report.summary.result +
                    ". Review the Unity Console and install the WebGL Build Support module for Unity 6000.3.24f1.");
            }
        }
    }
}
