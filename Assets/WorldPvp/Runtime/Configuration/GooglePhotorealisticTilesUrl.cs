using System;
using System.IO;
using UnityEngine;

namespace WorldPvp.Phase1.Configuration
{
    /// <summary>Builds the official Google Map Tiles API root URL without logging or persisting the key.</summary>
    public static class GooglePhotorealisticTilesUrl
    {
        public const string RootTilesetUrlTemplate =
            "https://tile.googleapis.com/v1/3dtiles/root.json?key={0}";

        public static string BuildRootTilesetUrl(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("A non-empty Map Tiles API key is required.", "apiKey");
            }

            return string.Format(
                RootTilesetUrlTemplate,
                Uri.EscapeDataString(apiKey.Trim()));
        }
    }

    /// <summary>
    /// Reads only local developer input. A pasted runtime key is never written to disk.
    /// </summary>
    public static class GoogleMapTilesKeyReader
    {
        public const string EnvironmentVariableName = "GOOGLE_MAP_TILES_API_KEY";
        public const string LocalFileName = "google-tiles-key.local.txt";

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
                // A pasted runtime key remains the fallback; never log key material.
            }
#endif

            return string.Empty;
        }
    }
}
