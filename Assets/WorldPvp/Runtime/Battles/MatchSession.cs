using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Services.Multiplayer;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Battles
{
    public enum MatchSessionState
    {
        Lobby,
        InProgress,
        Closed
    }

    /// <summary>
    /// Validated match metadata resolved from a UGS Multiplayer Services session.
    /// Coordinates/radius are stored as member-visible session properties, never in an invite URL.
    /// These properties are written by the client-hosted session owner and are not server-authoritative.
    /// </summary>
    public sealed class MatchSession
    {
        public const int SchemaVersion = 1;
        public const double MaximumArenaRadiusMeters = 2000.0;
        public const string DefaultGameMode = "Battle";

        public const string SchemaProperty = "wpv_schema";
        public const string CentreLatitudeProperty = "wpv_centre_lat";
        public const string CentreLongitudeProperty = "wpv_centre_lon";
        public const string CentreAltitudeProperty = "wpv_centre_alt_m";
        public const string RadiusProperty = "wpv_radius_m";
        public const string MaximumPlayersProperty = "wpv_max_players";
        public const string StateProperty = "wpv_state";
        public const string CreatedUtcProperty = "wpv_created_utc";
        public const string GameModeProperty = "wpv_game_mode";
        public const string CoverageConfirmedProperty = "wpv_coverage_confirmed";

        public string Id { get; private set; }
        public string JoinCode { get; private set; }
        public GeoPosition Centre { get; private set; }
        public double RadiusMeters { get; private set; }
        public int MaximumPlayers { get; private set; }
        public int CurrentPlayers { get; private set; }
        public MatchSessionState State { get; private set; }
        public DateTimeOffset CreatedAtUtc { get; private set; }
        public string GameMode { get; private set; }
        public bool CoverageMapConfirmed { get; private set; }

        private MatchSession(
            string id,
            string joinCode,
            GeoPosition centre,
            double radiusMeters,
            int maximumPlayers,
            int currentPlayers,
            MatchSessionState state,
            DateTimeOffset createdAtUtc,
            string gameMode,
            bool coverageMapConfirmed)
        {
            Id = id;
            JoinCode = joinCode;
            Centre = centre;
            RadiusMeters = radiusMeters;
            MaximumPlayers = maximumPlayers;
            CurrentPlayers = currentPlayers;
            State = state;
            CreatedAtUtc = createdAtUtc;
            GameMode = gameMode;
            CoverageMapConfirmed = coverageMapConfirmed;
        }

        public static bool IsSupportedRadius(double radiusMeters)
        {
            return GeoPosition.IsFinite(radiusMeters) &&
                   radiusMeters > 0.0 &&
                   radiusMeters <= MaximumArenaRadiusMeters;
        }

        public static bool IsSupportedPlayerCount(int playerCount)
        {
            return playerCount >= 2 && playerCount <= 32;
        }

        /// <summary>
        /// Creates the initial member-visible UGS session properties. Individual numeric values use
        /// round-trip invariant formatting so joining clients recover the exact same doubles.
        /// </summary>
        public static Dictionary<string, SessionProperty> CreateMemberProperties(
            GeoPosition centre,
            double radiusMeters,
            int maximumPlayers,
            MatchSessionState state,
            DateTimeOffset createdAtUtc,
            string gameMode,
            bool coverageMapConfirmed)
        {
            if (!centre.IsValid)
            {
                throw new ArgumentException("A valid WGS84 match centre is required.", "centre");
            }
            if (!IsSupportedRadius(radiusMeters))
            {
                throw new ArgumentOutOfRangeException(
                    "radiusMeters",
                    "Match radius must be greater than zero and no more than 2,000 metres.");
            }
            if (!IsSupportedPlayerCount(maximumPlayers))
            {
                throw new ArgumentOutOfRangeException("maximumPlayers", "Maximum players must be from 2 through 32.");
            }
            if (string.IsNullOrWhiteSpace(gameMode))
            {
                throw new ArgumentException("A game mode is required.", "gameMode");
            }

            Dictionary<string, SessionProperty> properties = new Dictionary<string, SessionProperty>
            {
                { SchemaProperty, MemberProperty(SchemaVersion.ToString(CultureInfo.InvariantCulture)) },
                { CentreLatitudeProperty, MemberProperty(FormatDouble(centre.LatitudeDegrees)) },
                { CentreLongitudeProperty, MemberProperty(FormatDouble(centre.LongitudeDegrees)) },
                { CentreAltitudeProperty, MemberProperty(FormatDouble(centre.AltitudeMeters)) },
                { RadiusProperty, MemberProperty(FormatDouble(radiusMeters)) },
                { MaximumPlayersProperty, MemberProperty(maximumPlayers.ToString(CultureInfo.InvariantCulture)) },
                { StateProperty, MemberProperty(state.ToString()) },
                { CreatedUtcProperty, MemberProperty(createdAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)) },
                { GameModeProperty, MemberProperty(gameMode.Trim()) },
                { CoverageConfirmedProperty, MemberProperty(coverageMapConfirmed ? "true" : "false") }
            };
            return properties;
        }

        /// <summary>
        /// Resolves and validates backend metadata only after the caller has joined the UGS session.
        /// Member visibility prevents unrelated session queries from exposing the exact location.
        /// </summary>
        public static bool TryRead(ISession session, out MatchSession match, out string error)
        {
            match = null;
            error = string.Empty;
            if (session == null)
            {
                error = "The battle session could not be resolved.";
                return false;
            }

            string schemaText;
            string latitudeText;
            string longitudeText;
            string altitudeText;
            string radiusText;
            string maximumPlayersText;
            string stateText;
            string createdText;
            string gameMode;
            string coverageText;
            if (!TryReadProperty(session, SchemaProperty, out schemaText) ||
                !TryReadProperty(session, CentreLatitudeProperty, out latitudeText) ||
                !TryReadProperty(session, CentreLongitudeProperty, out longitudeText) ||
                !TryReadProperty(session, CentreAltitudeProperty, out altitudeText) ||
                !TryReadProperty(session, RadiusProperty, out radiusText) ||
                !TryReadProperty(session, MaximumPlayersProperty, out maximumPlayersText) ||
                !TryReadProperty(session, StateProperty, out stateText) ||
                !TryReadProperty(session, CreatedUtcProperty, out createdText) ||
                !TryReadProperty(session, GameModeProperty, out gameMode) ||
                !TryReadProperty(session, CoverageConfirmedProperty, out coverageText))
            {
                error = "This invite does not contain complete World PvP battle metadata.";
                return false;
            }

            int schema;
            double latitude;
            double longitude;
            double altitude;
            double radius;
            int maximumPlayers;
            MatchSessionState state;
            DateTimeOffset createdAtUtc;
            if (!int.TryParse(schemaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out schema) ||
                schema != SchemaVersion ||
                !TryParseFiniteDouble(latitudeText, out latitude) ||
                !TryParseFiniteDouble(longitudeText, out longitude) ||
                !TryParseFiniteDouble(altitudeText, out altitude) ||
                !TryParseFiniteDouble(radiusText, out radius) ||
                !int.TryParse(maximumPlayersText, NumberStyles.Integer, CultureInfo.InvariantCulture, out maximumPlayers) ||
                !Enum.TryParse(stateText, true, out state) ||
                !Enum.IsDefined(typeof(MatchSessionState), state) ||
                !DateTimeOffset.TryParse(
                    createdText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out createdAtUtc))
            {
                error = "This battle contains invalid or unsupported session metadata.";
                return false;
            }

            GeoPosition centre = new GeoPosition(latitude, longitude, altitude);
            if (!centre.IsValid)
            {
                error = "This battle contains an invalid WGS84 centre.";
                return false;
            }
            if (!IsSupportedRadius(radius))
            {
                error = "This battle radius is unsupported. The maximum is 2,000 metres.";
                return false;
            }
            if (!IsSupportedPlayerCount(maximumPlayers) || maximumPlayers != session.MaxPlayers)
            {
                error = "This battle has inconsistent player-capacity metadata.";
                return false;
            }
            if (session.PlayerCount > session.MaxPlayers)
            {
                error = "This battle has an invalid current player count.";
                return false;
            }
            if (state == MatchSessionState.Closed || session.IsLocked)
            {
                error = "This battle is closed to new players.";
                return false;
            }
            if (!string.Equals(gameMode, DefaultGameMode, StringComparison.Ordinal))
            {
                error = "This battle uses an unsupported game mode.";
                return false;
            }
            if (!bool.TryParse(coverageText, out bool coverageMapConfirmed) || !coverageMapConfirmed)
            {
                error = WorldLocationCoverageMessage;
                return false;
            }

            string normalizedCode;
            if (string.IsNullOrWhiteSpace(session.Id) ||
                !MatchJoinLink.TryNormalizeJoinCode(session.Code, out normalizedCode))
            {
                error = "The session ID or opaque join code is invalid.";
                return false;
            }

            match = new MatchSession(
                session.Id,
                normalizedCode,
                centre,
                radius,
                maximumPlayers,
                session.PlayerCount,
                state,
                createdAtUtc.ToUniversalTime(),
                gameMode,
                coverageMapConfirmed);
            return true;
        }

        public MatchSession WithCurrentPlayerCount(int currentPlayers)
        {
            return new MatchSession(
                Id,
                JoinCode,
                Centre,
                RadiusMeters,
                MaximumPlayers,
                currentPlayers,
                State,
                CreatedAtUtc,
                GameMode,
                CoverageMapConfirmed);
        }

        public MatchSession WithState(MatchSessionState state)
        {
            return new MatchSession(
                Id,
                JoinCode,
                Centre,
                RadiusMeters,
                MaximumPlayers,
                CurrentPlayers,
                state,
                CreatedAtUtc,
                GameMode,
                CoverageMapConfirmed);
        }

        private const string WorldLocationCoverageMessage =
            "Photorealistic coverage unavailable here. Choose another location.";

        private static SessionProperty MemberProperty(string value)
        {
            return new SessionProperty(value, VisibilityPropertyOptions.Member);
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool TryParseFiniteDouble(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                   GeoPosition.IsFinite(value);
        }

        private static bool TryReadProperty(ISession session, string key, out string value)
        {
            value = string.Empty;
            SessionProperty property;
            if (!session.Properties.TryGetValue(key, out property) || property == null ||
                property.Value == null)
            {
                return false;
            }

            value = property.Value;
            return true;
        }
    }
}
