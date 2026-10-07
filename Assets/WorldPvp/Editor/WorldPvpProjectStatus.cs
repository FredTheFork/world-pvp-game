using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WorldPvp.Phase1.Editor
{
    /// <summary>
    /// Shows that Unity is editing the local git clone, not the GitHub remote.
    /// </summary>
    public static class WorldPvpProjectStatus
    {
        [MenuItem("Tools/World PvP/Project/Show Local Git And Package Status")]
        public static void ShowStatus()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string lockPath = Path.Combine(projectRoot, "Packages", "packages-lock.json");
            var report = new StringBuilder();
            report.AppendLine("Unity edits this folder. GitHub updates only when you commit and push from it.");
            report.AppendLine();
            report.AppendLine("Project: " + projectRoot);
            report.AppendLine("Editor: " + Application.unityVersion);
            report.AppendLine("Expected editor: 6000.3.24f1");
            report.AppendLine(
                File.Exists(lockPath)
                    ? "Package lock: present at Packages/packages-lock.json"
                    : "Package lock: MISSING. The name is packages-lock.json, not packages.lock. Wait for Package Manager, or read the first red resolve error. Do not hand-write it.");
            report.AppendLine();
            report.AppendLine("git remote -v");
            report.AppendLine(RunGit(projectRoot, "remote -v"));
            report.AppendLine("git status --short --branch");
            report.AppendLine(RunGit(projectRoot, "status --short --branch"));
            report.AppendLine();
            report.AppendLine("Do not commit Library/, API keys, or .env files.");

            string text = report.ToString();
            UnityEngine.Debug.Log(text);
            EditorUtility.DisplayDialog("World PvP project status", TrimForDialog(text), "OK");
        }

        private static string RunGit(string projectRoot, string arguments)
        {
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (Process process = Process.Start(start))
                {
                    if (process == null)
                    {
                        return "(could not start git)";
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);
                    string text = string.IsNullOrWhiteSpace(output) ? error : output;
                    return string.IsNullOrWhiteSpace(text) ? "(no output)" : text.Trim();
                }
            }
            catch (System.Exception exception)
            {
                return "(git failed: " + exception.GetType().Name + ")";
            }
        }

        private static string TrimForDialog(string text)
        {
            const int limit = 1800;
            if (text.Length <= limit)
            {
                return text;
            }

            return text.Substring(0, limit) + "\n… full report is in the Console.";
        }
    }
}
