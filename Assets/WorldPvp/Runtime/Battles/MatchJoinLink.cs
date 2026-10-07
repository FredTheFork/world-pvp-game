using System;

namespace WorldPvp.Phase1.Battles
{
    /// <summary>
    /// Builds and parses invite URLs containing only an opaque UGS session join code.
    /// Geographic centre, altitude, radius, and other match properties never enter the URL.
    /// </summary>
    public static class MatchJoinLink
    {
        public static bool TryNormalizeJoinCode(string input, out string normalizedCode)
        {
            normalizedCode = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string candidate = input.Trim().ToUpperInvariant();
            if (candidate.Length < 4 || candidate.Length > 32)
            {
                return false;
            }

            for (int i = 0; i < candidate.Length; i++)
            {
                char value = candidate[i];
                bool asciiLetter = value >= 'A' && value <= 'Z';
                bool digit = value >= '0' && value <= '9';
                if (!asciiLetter && !digit)
                {
                    return false;
                }
            }

            normalizedCode = candidate;
            return true;
        }

        /// <summary>Accepts either a bare UGS code or a full /join/{code} URL.</summary>
        public static bool TryParseJoinInput(string input, out string normalizedCode)
        {
            if (TryNormalizeJoinCode(input, out normalizedCode))
            {
                return true;
            }
            return TryExtractJoinCode(input, out normalizedCode);
        }

        public static bool TryBuildShareUrl(string currentUrl, string joinCode, out string shareUrl)
        {
            shareUrl = string.Empty;
            string normalizedCode;
            Uri currentUri;
            if (!TryNormalizeJoinCode(joinCode, out normalizedCode) ||
                !Uri.TryCreate(currentUrl, UriKind.Absolute, out currentUri) ||
                (currentUri.Scheme != Uri.UriSchemeHttp && currentUri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            UriBuilder builder = new UriBuilder(currentUri.Scheme, currentUri.Host);
            if (!currentUri.IsDefaultPort)
            {
                builder.Port = currentUri.Port;
            }

            builder.Path = "/join/" + normalizedCode;
            builder.Query = string.Empty;
            builder.Fragment = string.Empty;
            shareUrl = builder.Uri.AbsoluteUri.TrimEnd('/');
            return true;
        }

        public static bool TryExtractJoinCode(string currentUrl, out string joinCode)
        {
            joinCode = string.Empty;
            Uri uri;
            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            string[] segments = uri.AbsolutePath.Split(
                new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 2 ||
                !string.Equals(segments[0], "join", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TryNormalizeJoinCode(segments[1], out joinCode);
        }
    }
}
