using System;
using System.IO;
using UnityEngine;

namespace WorldPvp.Phase1.Configuration
{
    /// <summary>
    /// Reads an optional editor-only local Google Maps JavaScript / Places key.
    /// Web clients enter and locally persist their own restricted key in the Phase 3 UI.
    /// </summary>
    public static class GooglePlacesKeyReader
    {
        public const string EnvironmentVariableName = "GOOGLE_PLACES_API_KEY";
        public const string LocalFileName = "google-places-key.local.txt";

        public static string TryReadLocalKey()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment.Trim();
            }

#if UNITY_EDITOR
            try
            {
                string localPath = Path.Combine(
                    Application.dataPath,
                    "WorldPvp",
                    "StreamingAssets",
                    LocalFileName);
                if (File.Exists(localPath))
                {
                    string fromFile = File.ReadAllText(localPath);
                    return string.IsNullOrWhiteSpace(fromFile) ? string.Empty : fromFile.Trim();
                }
            }
            catch (Exception)
            {
                // A key entered in the runtime UI is the fallback; never log key material.
            }
#endif
            return string.Empty;
        }
    }
}
