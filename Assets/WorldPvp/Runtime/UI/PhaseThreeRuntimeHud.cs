using System;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using WorldPvp.Phase1.Battles;
using WorldPvp.Phase1.Backend;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;
using WorldPvp.Phase1.Gameplay;
using WorldPvp.Phase1.Player;

namespace WorldPvp.Phase1.UI
{
    /// <summary>
    /// Phase 3 session discovery and multiplayer gameplay IMGUI screen with Phase 7 combat input gating. Places search and map selection use
    /// the browser's Google Maps JavaScript library through a WebGL .jslib bridge; coordinate entry
    /// remains available in Editor and offline builds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhaseThreeRuntimeHud : MonoBehaviour
    {
        private const string MapTilesPrefsKey = "worldpvp.phase3.google-map-tiles-key";
        private const string PlacesPrefsKey = "worldpvp.phase3.google-maps-places-key";
        private const string CoverageCheckerUrl = "https://developers.google.com/maps/documentation/javascript/3d/coverage";
        private const int DefaultMaximumPlayers = 8;

        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private PhaseOneTestCharacterController characterMotor;
        [SerializeField] private PhaseOneWorldSettings settings;
        [SerializeField] private BattleSessionCoordinator coordinator;
        [SerializeField] private PhaseTenAccountClient phaseTenAccountClient;

        private enum LocationTab
        {
            Search,
            Map,
            Coordinates
        }

        [Serializable]
        private sealed class PlacesPrediction
        {
            public string placeId;
            public string description;
        }

        [Serializable]
        private sealed class PlacesPredictionResponse
        {
            public PlacesPrediction[] predictions;
            public string error;
        }

        [Serializable]
        private sealed class PlaceDetailsResponse
        {
            public double latitude;
            public double longitude;
            public string displayName;
            public string formattedAddress;
            public string error;
        }

        [Serializable]
        private sealed class MapPickResponse
        {
            public double latitude;
            public double longitude;
            public string error;
        }

        private string latitudeText = "51.5073";
        private string longitudeText = "-0.1657";
        private string altitudeText = "0";
        private string radiusText = "500";
        private string searchText = string.Empty;
        private string joinCodeText = string.Empty;
        private string mapTilesApiKey = string.Empty;
        private string googleMapsPlacesApiKey = string.Empty;
        private string placeLabel = string.Empty;
        private string locationMessage = string.Empty;
        private string localUiMessage = string.Empty;
        private string authenticationProfile = string.Empty;
        private string accountEmailText = string.Empty;
        private string accountPasswordText = string.Empty;
        private string accountUsernameText = string.Empty;
        private string accountAvatarUrlText = string.Empty;
        private bool createAccountMode = true;
        private Texture2D googleMapsAttributionLogo;
        private GUIStyle safetyNoticeStyle;
        private GameplayArenaRuntime gameplayArenaRuntime;
        private PlacesPrediction[] predictions = Array.Empty<PlacesPrediction>();
        private LocationTab selectedTab = LocationTab.Search;
        private int maximumPlayers = DefaultMaximumPlayers;
        private bool photorealisticCoverageConfirmed;
        private bool panelVisible = true;
        private bool profileReady;
        private bool browserProfileRequested;
        private bool inviteRouteDetected;
        private bool automaticJoinStarted;
        private bool hadReadySession;
        private Vector2 scrollPosition;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void WorldPvp_RequestBrowserProfile(string receiverObjectName);
        [DllImport("__Internal")] private static extern void WorldPvp_RequestPlacesPredictions(
            string input, string apiKey, string receiverObjectName);
        [DllImport("__Internal")] private static extern void WorldPvp_ResolvePlace(
            string placeId, string apiKey, string receiverObjectName);
        [DllImport("__Internal")] private static extern void WorldPvp_OpenMapPicker(
            string apiKey, double latitude, double longitude, string receiverObjectName);
        [DllImport("__Internal")] private static extern void WorldPvp_CopyText(string text);
        [DllImport("__Internal")] private static extern void WorldPvp_SetInvitePath(string joinCode);
        [DllImport("__Internal")] private static extern void WorldPvp_ClearInvitePath();
#endif

        public void Configure(
            GeospatialWorldManager manager,
            PhaseOneTestCharacterController motor,
            PhaseOneWorldSettings worldSettings,
            BattleSessionCoordinator sessionCoordinator)
        {
            worldManager = manager;
            characterMotor = motor;
            settings = worldSettings;
            coordinator = sessionCoordinator;
            phaseTenAccountClient = phaseTenAccountClient != null
                ? phaseTenAccountClient
                : FindObjectOfType<PhaseTenAccountClient>();
            gameplayArenaRuntime = FindObjectOfType<GameplayArenaRuntime>();
            ApplySettingsDefaults();
        }

        private void Awake()
        {
            if (phaseTenAccountClient == null)
            {
                phaseTenAccountClient = FindObjectOfType<PhaseTenAccountClient>();
            }
            ApplySettingsDefaults();
            googleMapsAttributionLogo = Resources.Load<Texture2D>("GoogleMaps_Attribution_White");
            mapTilesApiKey = PlayerPrefs.GetString(MapTilesPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(mapTilesApiKey))
            {
                mapTilesApiKey = GoogleMapTilesKeyReader.TryReadLocalKey();
            }

            googleMapsPlacesApiKey = PlayerPrefs.GetString(PlacesPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(googleMapsPlacesApiKey))
            {
                googleMapsPlacesApiKey = GooglePlacesKeyReader.TryReadLocalKey();
            }

            panelVisible = true;
            ApplyInputMode();
        }

        private void Start()
        {
            if (coordinator != null)
            {
                coordinator.StateChanged += OnCoordinatorStateChanged;
            }
            if (phaseTenAccountClient != null)
            {
                phaseTenAccountClient.StateChanged += OnAccountStateChanged;
            }

            string inviteCode;
            if (MatchJoinLink.TryExtractJoinCode(Application.absoluteURL, out inviteCode))
            {
                joinCodeText = inviteCode;
                inviteRouteDetected = true;
                selectedTab = LocationTab.Coordinates;
                localUiMessage = "Opaque invite code detected in /join/{code}. Session details are resolved from UGS after joining.";
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // The WebGL template requests the tab profile after createUnityInstance resolves,
            // because SendMessage is not available from the earliest Unity Start callbacks.
#else
            authenticationProfile = "editor_" + Guid.NewGuid().ToString("N").Substring(0, 16);
            if (coordinator != null)
            {
                coordinator.SetAuthenticationProfile(authenticationProfile);
            }
            profileReady = true;
#endif
        }

        private void OnDestroy()
        {
            if (coordinator != null)
            {
                coordinator.StateChanged -= OnCoordinatorStateChanged;
            }
            if (phaseTenAccountClient != null)
            {
                phaseTenAccountClient.StateChanged -= OnAccountStateChanged;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                panelVisible = !panelVisible;
                ApplyInputMode();
            }
            else if (!panelVisible && keyboard.escapeKey.wasPressedThisFrame)
            {
                panelVisible = true;
                ApplyInputMode();
            }
        }

        private void OnGUI()
        {
            if (worldManager == null || coordinator == null)
            {
                DrawNotice("Phase 9 scene references are missing. Rebuild the generated World PvP scene from the Tools menu.");
                DrawSafetyNotice();
                return;
            }

            if (gameplayArenaRuntime == null)
            {
                gameplayArenaRuntime = FindObjectOfType<GameplayArenaRuntime>();
            }
            if (coordinator.State != BattleFlowState.InGameplay || panelVisible)
            {
                DrawStatusStrip();
            }
            if (panelVisible)
            {
                DrawBattlePanel();
            }
            DrawSafetyNotice();
        }

        /// <summary>Called by the WebGL template after the Unity instance is available to JavaScript.</summary>
        public void OnBrowserRuntimeReady()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (browserProfileRequested)
            {
                return;
            }
            browserProfileRequested = true;
            WorldPvp_RequestBrowserProfile(gameObject.name);
#endif
        }

        /// <summary>Called by the WebGL bridge with a unique sessionStorage identity for this tab.</summary>
        public void OnBrowserProfileReady(string profile)
        {
            authenticationProfile = profile ?? string.Empty;
            if (coordinator != null)
            {
                coordinator.SetAuthenticationProfile(authenticationProfile);
            }
            profileReady = !string.IsNullOrWhiteSpace(authenticationProfile);
            if (!profileReady)
            {
                localUiMessage = "Could not prepare a unique per-tab Unity Authentication profile.";
            }
            TryAutomaticInviteJoin();
        }

        /// <summary>Callback from the Places Autocomplete JavaScript bridge.</summary>
        public void OnPlacesPredictionsJson(string json)
        {
            PlacesPredictionResponse response = ParseJson<PlacesPredictionResponse>(json);
            if (response == null)
            {
                locationMessage = "Google Places returned an unreadable response.";
                predictions = Array.Empty<PlacesPrediction>();
                return;
            }

            if (!string.IsNullOrWhiteSpace(response.error))
            {
                locationMessage = response.error;
                predictions = Array.Empty<PlacesPrediction>();
                return;
            }

            predictions = response.predictions ?? Array.Empty<PlacesPrediction>();
            locationMessage = predictions.Length == 0
                ? "No matching places. Try a nearby town, address, or landmark."
                : "Choose a result to use its WGS84 latitude and longitude.";
        }

        /// <summary>Callback from the Places details lookup.</summary>
        public void OnPlaceDetailsJson(string json)
        {
            PlaceDetailsResponse response = ParseJson<PlaceDetailsResponse>(json);
            if (response == null)
            {
                locationMessage = "Google Places returned an unreadable place detail.";
                return;
            }
            if (!string.IsNullOrWhiteSpace(response.error))
            {
                locationMessage = response.error;
                return;
            }
            if (!GeoPosition.IsFinite(response.latitude) || !GeoPosition.IsFinite(response.longitude) ||
                response.latitude < -90.0 || response.latitude > 90.0 ||
                response.longitude < -180.0 || response.longitude > 180.0)
            {
                locationMessage = "Google Places returned invalid coordinates.";
                return;
            }

            SetCoordinates(response.latitude, response.longitude, 0.0);
            placeLabel = string.IsNullOrWhiteSpace(response.displayName)
                ? response.formattedAddress
                : response.displayName;
            selectedTab = LocationTab.Coordinates;
            photorealisticCoverageConfirmed = false;
            locationMessage = "Place selected. Review the exact coordinates and confirm coverage before creating a battle.";
        }

        /// <summary>Callback from the Google Maps clickable map overlay.</summary>
        public void OnMapPickJson(string json)
        {
            MapPickResponse response = ParseJson<MapPickResponse>(json);
            if (response == null)
            {
                locationMessage = "The map picker returned an unreadable result.";
                return;
            }
            if (!string.IsNullOrWhiteSpace(response.error))
            {
                locationMessage = response.error;
                return;
            }
            if (!GeoPosition.IsFinite(response.latitude) || !GeoPosition.IsFinite(response.longitude) ||
                response.latitude < -90.0 || response.latitude > 90.0 ||
                response.longitude < -180.0 || response.longitude > 180.0)
            {
                locationMessage = "The map picker returned invalid coordinates.";
                return;
            }

            SetCoordinates(response.latitude, response.longitude, 0.0);
            placeLabel = "Map selection";
            selectedTab = LocationTab.Coordinates;
            photorealisticCoverageConfirmed = false;
            locationMessage = "Map centre selected. Check the official photorealistic coverage map before creating a battle.";
        }

        private void DrawSafetyNotice()
        {
            const float margin = 12f;
            float width = Mathf.Min(1080f, Mathf.Max(260f, Screen.width - margin * 2f));
            const float height = 96f;
            Rect rect = new Rect(margin, Screen.height - height - margin, width, height);
            Color previousColor = GUI.color;
            GUI.color = new Color(0.66f, 0.12f, 0.08f, 0.97f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = previousColor;

            if (safetyNoticeStyle == null)
            {
                safetyNoticeStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    wordWrap = true,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(4, 4, 2, 2)
                };
                safetyNoticeStyle.normal.textColor = Color.white;
            }

            string message = GameplayArenaRuntime.SafetyWarning;
            if (gameplayArenaRuntime != null && gameplayArenaRuntime.IsPrototypeFallback)
            {
                message += "\n" + gameplayArenaRuntime.GameplayDataNotice;
            }
            if (gameplayArenaRuntime != null && !string.IsNullOrWhiteSpace(gameplayArenaRuntime.GameplayDataAttribution))
            {
                message += "\nGameplay data attribution: " + gameplayArenaRuntime.GameplayDataAttribution;
            }
            GUI.Label(new Rect(rect.x + 12f, rect.y + 5f, rect.width - 24f, rect.height - 10f), message, safetyNoticeStyle);
        }

        private void DrawStatusStrip()
        {
            Rect rect = new Rect(12f, 12f, Mathf.Min(900f, Screen.width - 24f), 224f);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f));
            GUILayout.Label("WORLD PvP  /  PHASE 7 COMBAT GAMEPLAY");
            GUILayout.Label("Flow: " + coordinator.State + " — " + coordinator.StatusMessage);
            GUILayout.Label("World: " + worldManager.State + " — " + worldManager.StatusMessage);

            MatchSession match = coordinator.ActiveMatch;
            if (match != null)
            {
                GUILayout.Label(string.Format(
                    CultureInfo.InvariantCulture,
                    "Battle {0}  ·  Players {1}/{2}  ·  Relay/NGO {3}  ·  Radius {4:F0} m",
                    match.JoinCode,
                    coordinator.CurrentPlayerCount,
                    match.MaximumPlayers,
                    coordinator.CurrentNetworkState,
                    match.RadiusMeters));
                NetworkPlayer localPlayer = coordinator.LocalNetworkPlayer;
                if (localPlayer != null && localPlayer.HasAuthoritativeState)
                {
                    NetworkPlayerSnapshot playerState = localPlayer.CurrentState;
                    GUILayout.Label(string.Format(
                        CultureInfo.InvariantCulture,
                        "NGO clients {0}  ·  Sim {1} Hz  ·  sim tick {2}  ·  cmd {3}  ·  ack {4}  ·  pending {5}",
                        coordinator.ConnectedNetworkPlayerCount,
                        NetworkPlayer.ServerSimulationTickRate,
                        playerState.ServerTick,
                        localPlayer.NextOwnerInputTick,
                        localPlayer.LastAcknowledgedInputTick,
                        localPlayer.PendingOwnerInputCount));
                    GUILayout.Label(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}  ·  {1}  ·  ENU {2:F1}, {3:F1}, {4:F1} m  ·  speed {5:F1} m/s",
                        playerState.DisplayName.ToString(),
                        playerState.MovementState,
                        playerState.EastMeters,
                        playerState.UpMeters,
                        playerState.NorthMeters,
                        Mathf.Sqrt(playerState.VelocityEastMetersPerSecond * playerState.VelocityEastMetersPerSecond +
                                   playerState.VelocityNorthMetersPerSecond * playerState.VelocityNorthMetersPerSecond)));
                }
                GUILayout.Label(panelVisible
                    ? "Use the battle panel to copy the opaque invite URL or enter gameplay."
                    : "WASD move · Mouse look · Hold LMB fire · R reload · Shift sprint · C/Ctrl crouch · Space jump · Tab spectate · F1/Esc settings");
            }
            else
            {
                GUILayout.Label(panelVisible
                    ? "Choose Google Places search, a map click, or exact WGS84 coordinates."
                    : "F1 or Escape reopens the Create / Join panel.");
            }
            GUILayout.EndArea();
        }

        private void DrawBattlePanel()
        {
            float panelY = 202f;
            float panelHeight = Mathf.Max(260f, Mathf.Min(690f, Screen.height - panelY - 12f));
            float panelWidth = Mathf.Min(620f, Screen.width - 24f);
            Rect panel = new Rect(12f, panelY, panelWidth, panelHeight);
            GUI.Box(panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, panel.height - 20f));
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            if (coordinator.HasSession)
            {
                DrawActiveSessionPanel();
            }
            else
            {
                DrawCreateJoinPanel();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawAccountPanel()
        {
            GUILayout.Label("PHASE 10 ACCOUNT — Supabase Auth / persistent profile");
            if (phaseTenAccountClient == null)
            {
                GUILayout.Label("Account API client is not wired. Rebuild the generated scene and configure Supabase/Vercel.");
                return;
            }

            bool previousEnabled = GUI.enabled;
            if (phaseTenAccountClient.IsSignedIn)
            {
                PhaseTenAccountClient.ProfileDto profile = phaseTenAccountClient.Profile;
                PhaseTenAccountClient.StatisticsDto stats = phaseTenAccountClient.Statistics;
                if (profile != null && string.IsNullOrEmpty(accountUsernameText)) accountUsernameText = profile.username ?? string.Empty;
                if (profile != null && string.IsNullOrEmpty(accountAvatarUrlText)) accountAvatarUrlText = profile.avatar_url ?? string.Empty;
                GUILayout.Label("Signed in: " + (profile != null && !string.IsNullOrEmpty(profile.username)
                    ? profile.username
                    : phaseTenAccountClient.Email));
                if (stats != null)
                {
                    GUILayout.Label(string.Format(
                        CultureInfo.InvariantCulture,
                        "Matches {0}  ·  Kills {1}  ·  Deaths {2}  ·  Wins {3}",
                        stats.matches_played,
                        stats.kills,
                        stats.deaths,
                        stats.wins));
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Refresh profile", GUILayout.Height(25f))) phaseTenAccountClient.RefreshProfile();
                if (GUILayout.Button("Sign out", GUILayout.Height(25f)))
                {
                    phaseTenAccountClient.SignOut();
                    accountPasswordText = string.Empty;
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Username");
                accountUsernameText = GUILayout.TextField(accountUsernameText, GUILayout.Height(24f));
                GUILayout.Label("Avatar URL (optional HTTPS image URL)");
                accountAvatarUrlText = GUILayout.TextField(accountAvatarUrlText, GUILayout.Height(24f));
                GUI.enabled = !phaseTenAccountClient.IsBusy;
                if (GUILayout.Button("Save profile", GUILayout.Height(26f)))
                {
                    phaseTenAccountClient.UpdateProfile(accountUsernameText, accountAvatarUrlText);
                }
                GUI.enabled = previousEnabled;
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(createAccountMode ? "• Create account" : "Create account", GUILayout.Height(25f))) createAccountMode = true;
                if (GUILayout.Button(!createAccountMode ? "• Sign in" : "Sign in", GUILayout.Height(25f))) createAccountMode = false;
                GUILayout.EndHorizontal();
                GUILayout.Label("Email");
                accountEmailText = GUILayout.TextField(accountEmailText, GUILayout.Height(24f));
                if (createAccountMode)
                {
                    GUILayout.Label("Username (3–24 letters, numbers, or underscores)");
                    accountUsernameText = GUILayout.TextField(accountUsernameText, GUILayout.Height(24f));
                }
                GUILayout.Label("Password (10–128 characters)");
                accountPasswordText = GUILayout.PasswordField(accountPasswordText, '\u2022', GUILayout.Height(24f));
                GUI.enabled = previousEnabled && !phaseTenAccountClient.IsBusy;
                if (GUILayout.Button(createAccountMode ? "Create persistent account" : "Sign in", GUILayout.Height(30f)))
                {
                    if (createAccountMode)
                    {
                        phaseTenAccountClient.SignUp(accountEmailText, accountPasswordText, accountUsernameText);
                    }
                    else
                    {
                        phaseTenAccountClient.SignIn(accountEmailText, accountPasswordText);
                    }
                    accountPasswordText = string.Empty;
                }
                GUI.enabled = previousEnabled;
            }
            if (!string.IsNullOrWhiteSpace(phaseTenAccountClient.StatusMessage))
            {
                GUILayout.Label(phaseTenAccountClient.StatusMessage);
            }
            GUILayout.Label("Account identity/statistics are not yet linked to the player-hosted UGS prototype. Production match admission and stat writes require the separate dedicated-server allocation path.");
            GUI.enabled = previousEnabled;
        }

        private void DrawCreateJoinPanel()
        {
            DrawAccountPanel();
            GUILayout.Space(10f);
            GUILayout.Label("CREATE BATTLE");
            GUILayout.Label("Choose a location, verify Google's photorealistic coverage map, then preflight before creating a session.");
            if (gameplayArenaRuntime != null && !string.IsNullOrWhiteSpace(gameplayArenaRuntime.GameplayDataNotice))
            {
                GUILayout.Label(gameplayArenaRuntime.GameplayDataNotice);
            }
            if (gameplayArenaRuntime != null && !string.IsNullOrWhiteSpace(gameplayArenaRuntime.GameplayDataAttribution))
            {
                GUILayout.Label("Gameplay data attribution: " + gameplayArenaRuntime.GameplayDataAttribution);
            }
            GUILayout.Space(5f);

            GUILayout.BeginHorizontal();
            DrawTabButton("Search", LocationTab.Search);
            DrawTabButton("Map selection", LocationTab.Map);
            DrawTabButton("Coordinates", LocationTab.Coordinates);
            GUILayout.EndHorizontal();

            if (selectedTab == LocationTab.Search)
            {
                DrawSearchTab();
            }
            else if (selectedTab == LocationTab.Map)
            {
                DrawMapTab();
            }
            else
            {
                DrawCoordinatesTab();
            }

            GUILayout.BeginHorizontal();
            GUI.enabled = false;
            GUILayout.Button("Use my location (coming soon)", GUILayout.Height(26f));
            GUI.enabled = true;
            GUILayout.Label("Location permission is intentionally deferred.");
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("ARENA RADIUS — strict maximum 2,000 m");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("100 m", GUILayout.Height(28f))) SetRadius(100.0);
            if (GUILayout.Button("250 m", GUILayout.Height(28f))) SetRadius(250.0);
            if (GUILayout.Button("500 m", GUILayout.Height(28f))) SetRadius(500.0);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1 km", GUILayout.Height(28f))) SetRadius(1000.0);
            if (GUILayout.Button("2 km", GUILayout.Height(28f))) SetRadius(2000.0);
            GUILayout.Label("Custom", GUILayout.Width(64f));
            radiusText = GUILayout.TextField(radiusText, GUILayout.Height(25f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Maximum players (including host)", GUILayout.Width(225f));
            if (GUILayout.Button("2", GUILayout.Width(44f))) maximumPlayers = 2;
            if (GUILayout.Button("4", GUILayout.Width(44f))) maximumPlayers = 4;
            if (GUILayout.Button("8", GUILayout.Width(44f))) maximumPlayers = 8;
            if (GUILayout.Button("16", GUILayout.Width(48f))) maximumPlayers = 16;
            if (GUILayout.Button("32", GUILayout.Width(48f))) maximumPlayers = 32;
            GUILayout.Label(maximumPlayers.ToString(CultureInfo.InvariantCulture));
            GUILayout.EndHorizontal();

            GUILayout.Space(7f);
            GUILayout.Label("GOOGLE API KEYS — separate restricted keys; browser keys are visible to clients");
            GUILayout.Label("Map Tiles API key (Photorealistic 3D Tiles)");
            mapTilesApiKey = GUILayout.PasswordField(mapTilesApiKey, '\u2022', GUILayout.Height(25f));
            GUILayout.Label("Google Maps JavaScript + Places API key (not the Map Tiles-only key)");
            googleMapsPlacesApiKey = GUILayout.PasswordField(googleMapsPlacesApiKey, '\u2022', GUILayout.Height(25f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Read editor-local keys", GUILayout.Height(25f)))
            {
                mapTilesApiKey = GoogleMapTilesKeyReader.TryReadLocalKey();
                googleMapsPlacesApiKey = GooglePlacesKeyReader.TryReadLocalKey();
                localUiMessage = "Editor-local key files and environment variables checked.";
            }
            if (GUILayout.Button("Save keys in this browser", GUILayout.Height(25f)))
            {
                SaveKeysLocally();
                localUiMessage = "Keys saved in this browser's local PlayerPrefs; no key was added to the invite URL.";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(7f);
            GUILayout.Label("PHOTOREALISTIC COVERAGE CHECK — required before session creation");
            if (GUILayout.Button("Open Google's visual coverage checker", GUILayout.Height(26f)))
            {
                Application.OpenURL(CoverageCheckerUrl);
            }
            photorealisticCoverageConfirmed = GUILayout.Toggle(
                photorealisticCoverageConfirmed,
                "I checked this centre in Google's official 3D coverage map and confirmed Photorealistic coverage.");
            GUILayout.Label("The checkbox is a manual confirmation: Google documents a visual checker, not a public coverage API. Runtime terrain/surrounding samples also fail closed.");

            GUILayout.Space(6f);
            bool canCreate = profileReady && !coordinator.IsBusy;
            GUI.enabled = canCreate;
            if (GUILayout.Button("Preflight and Create Battle", GUILayout.Height(36f)))
            {
                SaveKeysLocally();
                GeoPosition centre;
                double radius;
                if (!TryReadDraft(out centre, out radius))
                {
                    // TryReadDraft writes its error to localUiMessage.
                }
                else
                {
                    coordinator.CreateBattle(
                        centre,
                        radius,
                        mapTilesApiKey,
                        photorealisticCoverageConfirmed,
                        maximumPlayers);
                }
            }
            GUI.enabled = true;
            if (!profileReady)
            {
                GUILayout.Label("Preparing a unique anonymous Unity Authentication profile for this browser tab…");
            }

            GUILayout.Space(10f);
            GUILayout.Label("JOIN BATTLE");
            GUILayout.Label("Paste the shared /join/{opaque-code} URL or enter its code. Centre/radius are resolved from UGS only after joining.");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Join code", GUILayout.Width(90f));
            joinCodeText = GUILayout.TextField(joinCodeText, GUILayout.Height(26f));
            GUI.enabled = profileReady && !coordinator.IsBusy;
            if (GUILayout.Button("Join", GUILayout.Width(72f), GUILayout.Height(26f)))
            {
                SaveKeysLocally();
                coordinator.JoinBattleByCode(joinCodeText, mapTilesApiKey);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(locationMessage)) GUILayout.Label(locationMessage);
            if (!string.IsNullOrEmpty(localUiMessage)) GUILayout.Label(localUiMessage);
            if (coordinator.State == BattleFlowState.Failed) GUILayout.Label("Status: " + coordinator.StatusMessage);

            GUILayout.Space(7f);
            GUILayout.Label("MVP networking is client-hosted through UGS Relay/NGO. Session properties are host-authored, not cheat-proof server authority.");
        }

        private void DrawSearchTab()
        {
            GUILayout.Label("Search a place with Google Places (requires the separate Maps JavaScript / Places key below).");
            GUILayout.BeginHorizontal();
            searchText = GUILayout.TextField(searchText, GUILayout.Height(27f));
            if (GUILayout.Button("Search Places", GUILayout.Width(118f), GUILayout.Height(27f)))
            {
                RequestPlacesPredictions();
            }
            GUILayout.EndHorizontal();
            DrawGoogleMapsAttribution();

            for (int i = 0; i < predictions.Length; i++)
            {
                PlacesPrediction prediction = predictions[i];
                if (prediction == null || string.IsNullOrWhiteSpace(prediction.placeId)) continue;
                if (GUILayout.Button(string.IsNullOrWhiteSpace(prediction.description) ? prediction.placeId : prediction.description, GUILayout.Height(25f)))
                {
                    ResolvePlace(prediction.placeId);
                }
            }
            if (!string.IsNullOrEmpty(placeLabel)) GUILayout.Label("Selected place: " + placeLabel);
            if (!string.IsNullOrEmpty(locationMessage)) GUILayout.Label(locationMessage);
        }

        private void DrawMapTab()
        {
            GUILayout.Label("Open a Google map and click the exact centre. The selected result returns latitude/longitude only; altitude defaults to 0 m ellipsoid.");
            if (GUILayout.Button("Open map and select a centre", GUILayout.Height(34f)))
            {
                OpenMapPicker();
            }
            GUILayout.Label("Map selection uses Google Maps JavaScript. Configure Maps JavaScript API and Places API (New) on the separate restricted key.");
            if (!string.IsNullOrEmpty(placeLabel))
            {
                DrawGoogleMapsAttribution();
                GUILayout.Label("Selected: " + placeLabel);
            }
            if (!string.IsNullOrEmpty(locationMessage)) GUILayout.Label(locationMessage);
        }

        private void DrawCoordinatesTab()
        {
            GUILayout.Label("Exact WGS84 coordinates: latitude/longitude in degrees; altitude in metres above the WGS84 ellipsoid.");
            string before = latitudeText + "|" + longitudeText + "|" + altitudeText;
            DrawCoordinateRow("Latitude", ref latitudeText);
            DrawCoordinateRow("Longitude", ref longitudeText);
            DrawCoordinateRow("Altitude (ellipsoid m)", ref altitudeText);
            string after = latitudeText + "|" + longitudeText + "|" + altitudeText;
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                photorealisticCoverageConfirmed = false;
                placeLabel = string.Empty;
            }
            if (!string.IsNullOrEmpty(placeLabel))
            {
                DrawGoogleMapsAttribution();
                GUILayout.Label("Location source: " + placeLabel);
            }
        }

        private void DrawActiveSessionPanel()
        {
            MatchSession match = coordinator.ActiveMatch;
            if (match == null)
            {
                GUILayout.Label("Session metadata is not currently available.");
                return;
            }

            GUILayout.Label("BATTLE SESSION");
            GUILayout.Label("State: " + match.State + "  ·  UGS network: " + coordinator.CurrentNetworkState);
            GUILayout.Label("Session ID: " + match.Id);
            GUILayout.Label("Opaque join code: " + match.JoinCode);
            GUILayout.Label(string.Format(
                CultureInfo.InvariantCulture,
                "Exact session centre: {0:R}° lat, {1:R}° lon, {2:R} m ellipsoid",
                match.Centre.LatitudeDegrees,
                match.Centre.LongitudeDegrees,
                match.Centre.AltitudeMeters));
            GUILayout.Label(string.Format(
                CultureInfo.InvariantCulture,
                "Radius: {0:R} m  ·  Players: {1}/{2}  ·  Mode: {3}  ·  Created UTC: {4:O}",
                match.RadiusMeters,
                coordinator.CurrentPlayerCount,
                match.MaximumPlayers,
                match.GameMode,
                match.CreatedAtUtc));
            if (gameplayArenaRuntime != null && !string.IsNullOrWhiteSpace(gameplayArenaRuntime.GameplayDataAttribution))
            {
                GUILayout.Label("Gameplay data attribution: " + gameplayArenaRuntime.GameplayDataAttribution);
            }

            string shareUrl;
            if (MatchJoinLink.TryBuildShareUrl(Application.absoluteURL, match.JoinCode, out shareUrl))
            {
                GUILayout.Label("Invite URL (contains only the opaque code):");
                GUILayout.TextField(shareUrl, GUILayout.Height(25f));
                if (GUILayout.Button("Copy invite URL", GUILayout.Height(30f)))
                {
                    CopyToClipboard(shareUrl);
                    localUiMessage = "Opaque invite URL copied. Its path contains only the UGS join code.";
                }
            }
            else
            {
                GUILayout.Label("Invite path: /join/" + match.JoinCode + " (absolute URL is available after running a web build on a host).");
            }

            GUILayout.Space(8f);
            if (coordinator.State == BattleFlowState.ReadyForGameplay)
            {
                if (GUILayout.Button("Enter gameplay", GUILayout.Height(38f)))
                {
                    coordinator.EnterGameplay();
                    panelVisible = false;
                    ApplyInputMode();
                }
            }
            else if (coordinator.State == BattleFlowState.InGameplay)
            {
                if (GUILayout.Button("Resume gameplay", GUILayout.Height(34f)))
                {
                    panelVisible = false;
                    ApplyInputMode();
                }
            }

            if (GUILayout.Button("Leave battle", GUILayout.Height(28f)))
            {
                coordinator.LeaveBattle();
                panelVisible = true;
                if (characterMotor != null) characterMotor.SetInputEnabled(false);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Every client runs a local Cesium surface check and loads this exact centre/radius before the gameplay-ready gate.");
            GUILayout.Label("W/A/S/D move · mouse look · hold left mouse to fire · R reload · Shift sprint · C/Ctrl crouch · Space jump · Tab spectate when eliminated. The in-game overlay shows WGS84 coordinates and the ENU arena minimap.");
            GUILayout.Label("Owners predict/replay input; remote players interpolate host snapshots. FireCommand carries intent only: the session host validates it and alone decides hits, damage, ammo, and death.");
            GUILayout.Label("Relay routes traffic to a player-hosted NGO authority, not a trusted dedicated server; the host is not cheat-proof against itself.");
        }

        private void DrawTabButton(string label, LocationTab tab)
        {
            string buttonLabel = selectedTab == tab ? "• " + label : label;
            if (GUILayout.Button(buttonLabel, GUILayout.Height(28f)))
            {
                selectedTab = tab;
            }
        }

        private void DrawCoordinateRow(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(190f));
            value = GUILayout.TextField(value, GUILayout.Height(25f));
            GUILayout.EndHorizontal();
        }

        private void DrawGoogleMapsAttribution()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (googleMapsAttributionLogo != null)
            {
                GUILayout.Label(
                    new GUIContent(googleMapsAttributionLogo, "Google Maps"),
                    GUILayout.Width(98f),
                    GUILayout.Height(18f));
            }
            else
            {
                // Google Maps text attribution is permitted where a logo asset is unavailable.
                GUILayout.Label("Google Maps");
            }
            GUILayout.EndHorizontal();
        }

        private bool TryReadDraft(out GeoPosition centre, out double radius)
        {
            centre = default(GeoPosition);
            radius = 0.0;
            double latitude;
            double longitude;
            double altitude;
            if (!TryParseFinite(latitudeText, out latitude) ||
                !TryParseFinite(longitudeText, out longitude) ||
                !TryParseFinite(altitudeText, out altitude) ||
                !TryParseFinite(radiusText, out radius))
            {
                localUiMessage = "Enter finite numeric latitude, longitude, altitude, and radius values.";
                return false;
            }

            centre = new GeoPosition(latitude, longitude, altitude);
            if (!centre.IsValid)
            {
                localUiMessage = "Latitude must be -90…90 and longitude -180…180 degrees.";
                return false;
            }
            if (!MatchSession.IsSupportedRadius(radius))
            {
                localUiMessage = WorldLocationValidator.UnsupportedRadiusMessage;
                return false;
            }
            localUiMessage = string.Empty;
            return true;
        }

        private void RequestPlacesPredictions()
        {
            SaveKeysLocally();
            if (string.IsNullOrWhiteSpace(searchText))
            {
                locationMessage = "Enter a place, address, or landmark to search.";
                return;
            }
            if (string.IsNullOrWhiteSpace(googleMapsPlacesApiKey))
            {
                locationMessage = "Enter a separate, referrer-restricted Google Maps JavaScript / Places API key.";
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            predictions = Array.Empty<PlacesPrediction>();
            locationMessage = "Searching Google Places…";
            WorldPvp_RequestPlacesPredictions(searchText.Trim(), googleMapsPlacesApiKey.Trim(), gameObject.name);
#else
            locationMessage = "Google Places browser search is enabled in a WebGL build. In Editor, enter WGS84 coordinates directly.";
#endif
        }

        private void ResolvePlace(string placeId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SaveKeysLocally();
            predictions = Array.Empty<PlacesPrediction>();
            locationMessage = "Resolving the selected place coordinates…";
            WorldPvp_ResolvePlace(placeId, googleMapsPlacesApiKey.Trim(), gameObject.name);
#else
            locationMessage = "Place details are available in a WebGL build. Enter WGS84 coordinates directly in Editor.";
#endif
        }

        private void OpenMapPicker()
        {
            SaveKeysLocally();
            if (string.IsNullOrWhiteSpace(googleMapsPlacesApiKey))
            {
                locationMessage = "Enter a separate Google Maps JavaScript / Places API key before opening the map picker.";
                return;
            }

            GeoPosition current;
            if (!TryReadDraftCentre(out current))
            {
                current = settings != null ? settings.DefaultOrigin : new GeoPosition(51.5073, -0.1657, 0.0);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            WorldPvp_OpenMapPicker(
                googleMapsPlacesApiKey.Trim(),
                current.LatitudeDegrees,
                current.LongitudeDegrees,
                gameObject.name);
#else
            locationMessage = "The clickable Google Maps picker is available in a WebGL build. Use the Coordinates tab in Editor.";
#endif
        }

        private bool TryReadDraftCentre(out GeoPosition centre)
        {
            centre = default(GeoPosition);
            double latitude;
            double longitude;
            double altitude;
            if (!TryParseFinite(latitudeText, out latitude) ||
                !TryParseFinite(longitudeText, out longitude) ||
                !TryParseFinite(altitudeText, out altitude))
            {
                return false;
            }
            centre = new GeoPosition(latitude, longitude, altitude);
            return centre.IsValid;
        }

        private void SetCoordinates(double latitude, double longitude, double altitude)
        {
            latitudeText = latitude.ToString("R", CultureInfo.InvariantCulture);
            longitudeText = longitude.ToString("R", CultureInfo.InvariantCulture);
            altitudeText = altitude.ToString("R", CultureInfo.InvariantCulture);
        }

        private void SetRadius(double radiusMeters)
        {
            radiusText = radiusMeters.ToString("F0", CultureInfo.InvariantCulture);
        }

        private void ApplySettingsDefaults()
        {
            if (settings == null)
            {
                return;
            }
            GeoPosition origin = settings.DefaultOrigin;
            latitudeText = origin.LatitudeDegrees.ToString("R", CultureInfo.InvariantCulture);
            longitudeText = origin.LongitudeDegrees.ToString("R", CultureInfo.InvariantCulture);
            altitudeText = origin.AltitudeMeters.ToString("R", CultureInfo.InvariantCulture);
            radiusText = settings.DefaultGameplayRadiusMeters.ToString("F0", CultureInfo.InvariantCulture);
        }

        private void SaveKeysLocally()
        {
            if (!string.IsNullOrWhiteSpace(mapTilesApiKey))
            {
                PlayerPrefs.SetString(MapTilesPrefsKey, mapTilesApiKey.Trim());
            }
            if (!string.IsNullOrWhiteSpace(googleMapsPlacesApiKey))
            {
                PlayerPrefs.SetString(PlacesPrefsKey, googleMapsPlacesApiKey.Trim());
            }
            PlayerPrefs.Save();
        }

        private void TryAutomaticInviteJoin()
        {
            if (!inviteRouteDetected || automaticJoinStarted || !profileReady ||
                string.IsNullOrWhiteSpace(mapTilesApiKey) || coordinator == null)
            {
                return;
            }

            automaticJoinStarted = true;
            SaveKeysLocally();
            coordinator.JoinBattleByCode(joinCodeText, mapTilesApiKey);
        }

        private void OnAccountStateChanged()
        {
            if (phaseTenAccountClient != null && phaseTenAccountClient.Profile != null)
            {
                accountUsernameText = phaseTenAccountClient.Profile.username ?? string.Empty;
                accountAvatarUrlText = phaseTenAccountClient.Profile.avatar_url ?? string.Empty;
            }
        }

        private void OnCoordinatorStateChanged()
        {
            if (coordinator != null && coordinator.State == BattleFlowState.ReadyForGameplay)
            {
                hadReadySession = true;
                panelVisible = true;
                MatchSession match = coordinator.ActiveMatch;
                if (match != null)
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    // Keep both ready browser tabs on the same opaque, shareable route without a reload.
                    WorldPvp_SetInvitePath(match.JoinCode);
#endif
                }
            }
            else if (coordinator != null && coordinator.State == BattleFlowState.Idle && hadReadySession)
            {
                hadReadySession = false;
#if UNITY_WEBGL && !UNITY_EDITOR
                WorldPvp_ClearInvitePath();
#endif
            }
            ApplyInputMode();
        }

        private void ApplyInputMode()
        {
            NetworkPlayer localPlayer = coordinator != null ? coordinator.LocalNetworkPlayer : null;
            bool combatAllowsInput = localPlayer == null || !localPlayer.HasAuthoritativeState ||
                                     (localPlayer.CurrentState.Alive && localPlayer.CurrentState.Health > 0 &&
                                      !localPlayer.CurrentState.MatchFinished);
            bool gameplayInput = !panelVisible && coordinator != null && coordinator.CanEnterGameplay && combatAllowsInput;
            if (characterMotor != null)
            {
                characterMotor.SetInputEnabled(gameplayInput);
            }
            else
            {
                Cursor.lockState = gameplayInput ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !gameplayInput;
            }
        }

        private void CopyToClipboard(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WorldPvp_CopyText(text);
#else
            GUIUtility.systemCopyBuffer = text;
#endif
        }

        private static bool TryParseFinite(string text, out double value)
        {
            return double.TryParse(
                       text,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   GeoPosition.IsFinite(value);
        }

        private static T ParseJson<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void DrawNotice(string message)
        {
            GUI.Box(new Rect(12f, 12f, Mathf.Min(720f, Screen.width - 24f), 58f), message);
        }

    }
}
