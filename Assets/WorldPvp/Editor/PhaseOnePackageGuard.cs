using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace WorldPvp.Phase1.Editor
{
    /// <summary>Warns at Editor load if direct toolchain pins drift from the Phase 1 baseline.</summary>
    [InitializeOnLoad]
    public static class PhaseOnePackageGuard
    {
        private const string ExpectedEditor = "6000.3.24f1";
        private static readonly Dictionary<string, string> ExpectedPackages =
            new Dictionary<string, string>
            {
                { "com.cesium.unity", "1.26.0" },
                { "com.unity.inputsystem", "1.19.0" },
                { "com.unity.mathematics", "1.3.2" },
                { "com.unity.netcode.gameobjects", "2.13.3" },
                { "com.unity.render-pipelines.core", "17.3.0" },
                { "com.unity.render-pipelines.universal", "17.3.0" },
                { "com.unity.services.authentication", "3.8.0" },
                { "com.unity.services.multiplayer", "2.3.3" },
                { "com.unity.shadergraph", "17.3.0" },
                { "com.unity.splines", "2.9.1" },
                { "com.unity.test-framework", "1.4.6" },
                { "com.unity.transport", "2.7.4" },
                { "com.unity.ugui", "2.0.0" }
            };

        static PhaseOnePackageGuard()
        {
            EditorApplication.delayCall += CheckPins;
        }

        private static void CheckPins()
        {
            if (!Application.isEditor)
            {
                return;
            }

            if (Application.unityVersion != ExpectedEditor)
            {
                Debug.LogError(
                    "[Pinned toolchain] This project is pinned to Unity " + ExpectedEditor +
                    ", but the running Editor is " + Application.unityVersion +
                    ". Do not silently upgrade/downgrade; use the pinned Editor or make a reviewed toolchain change.");
            }

            string manifestPath = Path.Combine(Application.dataPath, "../Packages/manifest.json");
            if (!File.Exists(manifestPath))
            {
                Debug.LogError("[Pinned toolchain] Packages/manifest.json is missing.");
                return;
            }

            string manifest = File.ReadAllText(manifestPath);
            foreach (KeyValuePair<string, string> expected in ExpectedPackages)
            {
                string pattern = "\"" + Regex.Escape(expected.Key) +
                                "\"\\s*:\\s*\"" + Regex.Escape(expected.Value) + "\"";
                if (!Regex.IsMatch(manifest, pattern))
                {
                    Debug.LogError(
                        "[Pinned toolchain] Direct package pin drift: expected " +
                        expected.Key + " " + expected.Value + " in Packages/manifest.json.");
                }
            }

            int pinsProperty = manifest.IndexOf("\"pinnedPackages\"");
            int pinsStart = pinsProperty < 0 ? -1 : manifest.IndexOf('[', pinsProperty);
            int pinsEnd = pinsStart < 0 ? -1 : manifest.IndexOf(']', pinsStart);
            if (pinsStart < 0 || pinsEnd < 0)
            {
                Debug.LogError("[Pinned toolchain] UPM pinnedPackages array is missing.");
            }
            else
            {
                string pinnedList = manifest.Substring(pinsStart, pinsEnd - pinsStart + 1);
                foreach (KeyValuePair<string, string> expected in ExpectedPackages)
                {
                    string pinPattern = "\"" + Regex.Escape(expected.Key) + "\"";
                    if (!Regex.IsMatch(pinnedList, pinPattern))
                    {
                        Debug.LogError(
                            "[Pinned toolchain] Package is not explicitly pinned in pinnedPackages: " + expected.Key + ".");
                    }
                }
            }

            string lockPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/packages-lock.json"));
            if (!File.Exists(lockPath))
            {
                Debug.LogWarning(
                    "[Pinned toolchain] Packages/packages-lock.json does not exist yet. " +
                    "There is no file named packages.lock. Unity writes packages-lock.json only after Package Manager " +
                    "resolution succeeds. If the Console has a Package Manager error, that error is the blocker — " +
                    "do not hand-write the lock file. After it appears, commit it from this same project folder.");
            }
            else
            {
                Debug.Log("[Pinned toolchain] Package lock is present: " + lockPath);
            }
        }
    }
}
