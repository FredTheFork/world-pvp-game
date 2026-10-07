using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using WorldPvp.Phase1.Combat;
using WorldPvp.Phase1.Geospatial;
using WorldPvp.Phase1.Gameplay;
using WorldPvp.Phase1.Streaming;

namespace WorldPvp.Phase1.Battles
{
    public enum BattleFlowState
    {
        Idle,
        LoadingWorld,
        ValidatingLocation,
        Authenticating,
        CreatingSession,
        JoiningSession,
        ConnectingNetwork,
        ReadyForGameplay,
        InGameplay,
        Failed
    }

    /// <summary>
    /// Orchestrates local preflight, anonymous per-tab authentication, UGS session creation/join,
    /// Relay-backed NGO connection, exact match-world loading, and the gameplay-ready gate.
    /// The UGS session host is a player client, not a dedicated authoritative game server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleSessionCoordinator : MonoBehaviour
    {
        private const int DefaultMaximumPlayers = 8;
        private const int WorldLoadTimeoutSeconds = 150;
        private const int NetworkStartTimeoutSeconds = 60;

        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private WorldLocationValidator locationValidator;
        [SerializeField] private MapUsageTracker mapUsageTracker;
        [SerializeField] private CombatMatchController combatMatchController;
        [SerializeField] private GameplayArenaRuntime gameplayArenaRuntime;

        private ISession activeSession;
        private MatchSession activeMatch;
        private BattleFlowState flowState = BattleFlowState.Idle;
        private string statusMessage = "Choose Create Battle or join an opaque session code.";
        private string authenticationProfile = string.Empty;
        private bool isBusy;

        public event Action StateChanged;

        public BattleFlowState State { get { return flowState; } }
        public string StatusMessage { get { return statusMessage; } }
        public bool IsBusy { get { return isBusy; } }
        public bool HasSession { get { return activeSession != null && activeMatch != null; } }
        public bool CanEnterGameplay
        {
            get
            {
                return activeSession != null && activeMatch != null &&
                       (flowState == BattleFlowState.ReadyForGameplay || flowState == BattleFlowState.InGameplay);
            }
        }
        public int CurrentPlayerCount { get { return activeSession != null ? activeSession.PlayerCount : 0; } }
        public int ConnectedNetworkPlayerCount
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager != null && manager.IsListening ? manager.ConnectedClients.Count : 0;
            }
        }
        public NetworkPlayer LocalNetworkPlayer
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                NetworkClient client = manager != null ? manager.LocalClient : null;
                NetworkObject playerObject = client != null ? client.PlayerObject : null;
                return playerObject != null ? playerObject.GetComponent<NetworkPlayer>() : null;
            }
        }
        public NetworkState CurrentNetworkState
        {
            get { return activeSession != null ? activeSession.Network.State : NetworkState.Stopped; }
        }
        public ISession ActiveUgsSession { get { return activeSession; } }
        public GameplayArenaRuntime GameplayArenaRuntime { get { return gameplayArenaRuntime; } }

        public MatchSession ActiveMatch
        {
            get
            {
                return activeMatch != null && activeSession != null
                    ? activeMatch.WithCurrentPlayerCount(activeSession.PlayerCount)
                    : activeMatch;
            }
        }

        public void Configure(
            GeospatialWorldManager manager,
            WorldLocationValidator validator,
            MapUsageTracker usageTracker = null,
            CombatMatchController matchController = null,
            GameplayArenaRuntime arenaRuntime = null)
        {
            worldManager = manager;
            locationValidator = validator;
            mapUsageTracker = usageTracker;
            combatMatchController = matchController;
            gameplayArenaRuntime = arenaRuntime != null ? arenaRuntime : FindObjectOfType<GameplayArenaRuntime>();
        }

        /// <summary>
        /// Must be called before any UGS initialization. Web builds supply a unique sessionStorage
        /// profile per tab so two tabs are two distinct anonymous UGS players.
        /// </summary>
        public void SetAuthenticationProfile(string profile)
        {
            if (isBusy || activeSession != null)
            {
                return;
            }

            string normalized = NormalizeProfile(profile);
            if (normalized.Length == 0)
            {
                statusMessage = "A valid per-browser authentication profile could not be prepared.";
                flowState = BattleFlowState.Failed;
                RaiseStateChanged();
                return;
            }

            authenticationProfile = normalized;
            statusMessage = "Player profile ready.";
            if (flowState == BattleFlowState.Failed)
            {
                flowState = BattleFlowState.Idle;
            }
            RaiseStateChanged();
        }

        public async void CreateBattle(
            GeoPosition centre,
            double radiusMeters,
            string mapTilesApiKey,
            bool photorealisticCoverageConfirmed,
            int maximumPlayers = DefaultMaximumPlayers)
        {
            if (isBusy || activeSession != null)
            {
                return;
            }

            isBusy = true;
            activeMatch = null;
            if (combatMatchController != null)
            {
                combatMatchController.ResetMatch();
            }
            SetState(BattleFlowState.LoadingWorld, "Loading the selected geographic centre before creating any session…");
            try
            {
                string draftError;
                if (!TryValidateDraft(centre, radiusMeters, maximumPlayers, mapTilesApiKey, out draftError))
                {
                    SetState(BattleFlowState.Failed, draftError);
                    return;
                }

                if (worldManager == null || locationValidator == null)
                {
                    SetState(BattleFlowState.Failed, "The Phase 3 battle scene is missing its validator or geospatial manager.");
                    return;
                }

                if (gameplayArenaRuntime == null)
                {
                    gameplayArenaRuntime = FindObjectOfType<GameplayArenaRuntime>();
                }
                if (gameplayArenaRuntime == null)
                {
                    SetState(BattleFlowState.Failed, "The scene has no virtual gameplay-arena planner. Rebuild the World PvP scene.");
                    return;
                }
                string arenaPlanError;
                if (!gameplayArenaRuntime.ConfigureMatch(centre, radiusMeters, maximumPlayers, out arenaPlanError))
                {
                    SetState(BattleFlowState.Failed, arenaPlanError);
                    return;
                }

                if (mapUsageTracker != null)
                {
                    // Attribute preflight tile work to the match before the host creates its UGS session.
                    mapUsageTracker.BeginProvisionalMatchScope();
                }
                if (!worldManager.BeginArena(centre, radiusMeters, mapTilesApiKey))
                {
                    if (mapUsageTracker != null)
                    {
                        mapUsageTracker.EndMatchScope();
                    }
                    SetState(BattleFlowState.Failed, worldManager.StatusMessage);
                    return;
                }

                await WaitForWorldReadyAsync();
                SetState(BattleFlowState.ValidatingLocation, "Checking centre terrain and the surrounding render envelope…");
                WorldLocationValidationResult validation = await locationValidator.ValidateAsync(
                    centre,
                    radiusMeters,
                    photorealisticCoverageConfirmed);
                if (!validation.Succeeded)
                {
                    if (!string.IsNullOrEmpty(validation.TechnicalDetail))
                    {
                        Debug.LogWarning("[Phase 3 coverage preflight] " + validation.TechnicalDetail, this);
                    }
                    if (mapUsageTracker != null)
                    {
                        mapUsageTracker.EndMatchScope();
                    }
                    SetState(BattleFlowState.Failed, validation.UserMessage);
                    return;
                }

                if (!gameplayArenaRuntime.TryPrepareArena(out arenaPlanError))
                {
                    if (mapUsageTracker != null)
                    {
                        mapUsageTracker.EndMatchScope();
                    }
                    SetState(BattleFlowState.Failed, "Virtual gameplay-arena preflight failed: " + arenaPlanError);
                    return;
                }

                SetState(BattleFlowState.Authenticating, "Signing in to Unity Gaming Services…");
                EnsureNetworkTransportReady();
                await EnsureAuthenticatedAsync();

                DateTimeOffset createdAtUtc = DateTimeOffset.UtcNow;
                Dictionary<string, SessionProperty> properties = MatchSession.CreateMemberProperties(
                    centre,
                    radiusMeters,
                    maximumPlayers,
                    MatchSessionState.Lobby,
                    createdAtUtc,
                    MatchSession.DefaultGameMode,
                    photorealisticCoverageConfirmed);

                SessionOptions options = new SessionOptions
                {
                    Name = "World PvP Battle",
                    MaxPlayers = maximumPlayers,
                    IsPrivate = true,
                    IsLocked = false,
                    SessionProperties = properties
                }.WithRelayNetwork();

                SetState(BattleFlowState.CreatingSession, "Creating a private, join-code session with Relay networking…");
                activeSession = await MultiplayerService.Instance.CreateSessionAsync(options);
                if (!MatchSession.TryRead(activeSession, out activeMatch, out string metadataError))
                {
                    await ReleaseSessionQuietlyAsync();
                    SetState(BattleFlowState.Failed, metadataError);
                    return;
                }
                if (mapUsageTracker != null)
                {
                    mapUsageTracker.BindProvisionalScopeToMatch(activeSession.Id, activeSession.PlayerCount);
                }

                SetState(BattleFlowState.ConnectingNetwork, "Waiting for the Relay-backed NGO network connection and player spawn…");
                await WaitForNetworkStartedAsync(activeSession);
                await WaitForGameplayPlayerSpawnAsync();
                EnsureExactLoadedArena(activeMatch);

                SetState(
                    BattleFlowState.ReadyForGameplay,
                    "Battle ready. Share the opaque join link; enter gameplay when ready.");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[Phase 3] Battle creation failed (" + exception.GetType().Name + "). " +
                    "Check Unity project linking, UGS configuration, Relay, and the Unity Console; keys and invite URLs are withheld.",
                    this);
                await ReleaseSessionQuietlyAsync();
                SetState(
                    BattleFlowState.Failed,
                    "Battle creation failed. Check the Unity Services project link, Relay setup, and Unity Console.");
            }
            finally
            {
                isBusy = false;
                RaiseStateChanged();
            }
        }

        public async void JoinBattleByCode(string inputCode, string mapTilesApiKey)
        {
            if (isBusy || activeSession != null)
            {
                return;
            }

            string joinCode;
            if (!MatchJoinLink.TryParseJoinInput(inputCode, out joinCode))
            {
                SetState(BattleFlowState.Failed, "Enter a valid opaque battle join code.");
                return;
            }
            if (string.IsNullOrWhiteSpace(mapTilesApiKey))
            {
                SetState(BattleFlowState.Failed, "Enter a restricted Google Map Tiles API key for this browser before joining.");
                return;
            }
            if (string.IsNullOrWhiteSpace(authenticationProfile))
            {
                SetState(BattleFlowState.Failed, "The browser authentication profile is not ready yet.");
                return;
            }

            isBusy = true;
            activeMatch = null;
            if (combatMatchController != null)
            {
                combatMatchController.ResetMatch();
            }
            SetState(BattleFlowState.Authenticating, "Signing in to Unity Gaming Services…");
            try
            {
                EnsureNetworkTransportReady();
                await EnsureAuthenticatedAsync();

                SetState(BattleFlowState.JoiningSession, "Resolving the opaque code and joining the UGS lobby…");
                activeSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode);
                if (!MatchSession.TryRead(activeSession, out activeMatch, out string metadataError))
                {
                    await ReleaseSessionQuietlyAsync();
                    SetState(BattleFlowState.Failed, metadataError);
                    return;
                }
                if (mapUsageTracker != null)
                {
                    mapUsageTracker.BeginMatchScope(activeSession.Id, activeSession.PlayerCount);
                }

                if (worldManager == null || locationValidator == null)
                {
                    throw new InvalidOperationException("The Phase 3 battle scene is missing its validator or geospatial manager.");
                }

                GeoPosition exactCentre = activeMatch.Centre;
                double exactRadiusMeters = activeMatch.RadiusMeters;
                SetState(BattleFlowState.LoadingWorld, "Loading the exact session centre, altitude, radius, and virtual arena plan…");
                if (gameplayArenaRuntime == null)
                {
                    gameplayArenaRuntime = FindObjectOfType<GameplayArenaRuntime>();
                }
                if (gameplayArenaRuntime == null)
                {
                    throw new InvalidOperationException("The scene has no virtual gameplay-arena planner.");
                }
                string arenaPlanError;
                if (!gameplayArenaRuntime.ConfigureMatch(
                        exactCentre,
                        exactRadiusMeters,
                        activeMatch.MaximumPlayers,
                        out arenaPlanError))
                {
                    throw new InvalidOperationException(arenaPlanError);
                }
                if (!worldManager.BeginArena(exactCentre, exactRadiusMeters, mapTilesApiKey))
                {
                    throw new InvalidOperationException(worldManager.StatusMessage);
                }

                await WaitForWorldReadyAsync();
                SetState(BattleFlowState.ValidatingLocation, "Verifying this browser can load the session's centre and surroundings…");
                WorldLocationValidationResult validation = await locationValidator.ValidateAsync(
                    exactCentre,
                    exactRadiusMeters,
                    activeMatch.CoverageMapConfirmed);
                if (!validation.Succeeded)
                {
                    if (!string.IsNullOrEmpty(validation.TechnicalDetail))
                    {
                        Debug.LogWarning("[Phase 3 coverage preflight] " + validation.TechnicalDetail, this);
                    }
                    SetState(BattleFlowState.Failed, validation.UserMessage);
                    await ReleaseSessionQuietlyAsync();
                    return;
                }

                if (!gameplayArenaRuntime.TryPrepareArena(out arenaPlanError))
                {
                    SetState(BattleFlowState.Failed, "Virtual gameplay-arena preflight failed: " + arenaPlanError);
                    await ReleaseSessionQuietlyAsync();
                    return;
                }

                SetState(BattleFlowState.ConnectingNetwork, "Waiting for Relay / NGO connection and the network player spawn…");
                await WaitForNetworkStartedAsync(activeSession);
                await WaitForGameplayPlayerSpawnAsync();
                EnsureExactLoadedArena(activeMatch);
                SetState(
                    BattleFlowState.ReadyForGameplay,
                    "Connected to the shared battle. The exact session arena is loaded on this browser.");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[Phase 3] Joining the battle failed (" + exception.GetType().Name + "). " +
                    "Check the join code, Unity Services project link, Map Tiles key, Relay, and Unity Console.",
                    this);
                await ReleaseSessionQuietlyAsync();
                SetState(
                    BattleFlowState.Failed,
                    "Could not join this battle. Check the invite, Map Tiles key, Unity Services, and Unity Console.");
            }
            finally
            {
                isBusy = false;
                RaiseStateChanged();
            }
        }

        public async void EnterGameplay()
        {
            if (!CanEnterGameplay || isBusy)
            {
                return;
            }

            isBusy = true;
            try
            {
                if (activeSession.IsHost)
                {
                    IHostSession hostSession = activeSession.AsHost();
                    hostSession.SetProperty(
                        MatchSession.StateProperty,
                        new SessionProperty(MatchSessionState.InProgress.ToString(), VisibilityPropertyOptions.Member));
                    await hostSession.SavePropertiesAsync();
                    activeMatch = activeMatch.WithState(MatchSessionState.InProgress);
                }

                if (activeSession.IsHost && combatMatchController != null)
                {
                    combatMatchController.BeginMatch();
                }
                SetState(BattleFlowState.InGameplay, "Gameplay ready. WASD moves, mouse looks, hold left mouse to fire, R reloads, Shift sprints, C/Ctrl crouches, Space jumps; Tab cycles spectators when eliminated, F1 reopens the battle panel.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Phase 3] Could not update the host-authored session state (" + exception.GetType().Name + ").",
                    this);
                // The network and local gameplay are already ready. A lobby-property write is not a
                // reason to discard the loaded battle, but it must not be represented as server authority.
                if (activeSession != null && activeSession.IsHost && combatMatchController != null)
                {
                    combatMatchController.BeginMatch();
                }
                SetState(BattleFlowState.InGameplay, "Gameplay ready; the host-authored session state update did not persist.");
            }
            finally
            {
                isBusy = false;
                RaiseStateChanged();
            }
        }

        public async void LeaveBattle()
        {
            if (isBusy)
            {
                return;
            }

            isBusy = true;
            try
            {
                if (activeSession != null && activeSession.IsHost)
                {
                    IHostSession hostSession = activeSession.AsHost();
                    hostSession.SetProperty(
                        MatchSession.StateProperty,
                        new SessionProperty(MatchSessionState.Closed.ToString(), VisibilityPropertyOptions.Member));
                    await hostSession.SavePropertiesAsync();
                }
                await ReleaseSessionQuietlyAsync();
                SetState(BattleFlowState.Idle, "You left the battle. Create a new battle or join another opaque code.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Phase 3] Leaving the session failed (" + exception.GetType().Name + ").", this);
                await ReleaseSessionQuietlyAsync();
                SetState(BattleFlowState.Idle, "The local client left the battle.");
            }
            finally
            {
                isBusy = false;
                RaiseStateChanged();
            }
        }

        private static void EnsureNetworkTransportReady()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                throw new InvalidOperationException("The scene has no NGO NetworkManager.");
            }

            UnityTransport transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                throw new InvalidOperationException("The NGO NetworkManager has no Unity Transport component.");
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // Browsers cannot open UDP sockets. UGS Multiplayer Services selects Relay WSS for
            // WebGL by default; UTP must use its WebSocket network interface on this client.
            transport.UseWebSockets = true;
#endif
        }

        private async Task EnsureAuthenticatedAsync()
        {
            if (string.IsNullOrWhiteSpace(authenticationProfile))
            {
                throw new InvalidOperationException("A per-client authentication profile is required.");
            }

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                InitializationOptions options = new InitializationOptions();
                options.SetProfile(authenticationProfile);
                await UnityServices.InitializeAsync(options);
            }
            else if (!string.Equals(
                         AuthenticationService.Instance.Profile,
                         authenticationProfile,
                         StringComparison.Ordinal))
            {
                if (AuthenticationService.Instance.IsSignedIn)
                {
                    AuthenticationService.Instance.SignOut();
                }
                AuthenticationService.Instance.SwitchProfile(authenticationProfile);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        private async Task WaitForWorldReadyAsync()
        {
            if (worldManager == null)
            {
                throw new InvalidOperationException("The geospatial manager is missing.");
            }

            float startedAt = Time.realtimeSinceStartup;
            while (worldManager.State != GeospatialWorldState.Ready)
            {
                if (worldManager.State == GeospatialWorldState.Failed)
                {
                    throw new InvalidOperationException(worldManager.StatusMessage);
                }
                if (Time.realtimeSinceStartup - startedAt >= WorldLoadTimeoutSeconds)
                {
                    throw new TimeoutException("The streamed Cesium terrain did not become ready before the timeout.");
                }
                await Task.Delay(100);
            }
        }

        private static async Task WaitForGameplayPlayerSpawnAsync()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                throw new InvalidOperationException("The NGO NetworkManager disappeared before player spawn.");
            }

            float startedAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startedAt < NetworkStartTimeoutSeconds)
            {
                NetworkClient localClient = networkManager.LocalClient;
                NetworkObject playerObject = localClient != null ? localClient.PlayerObject : null;
                if (playerObject != null && playerObject.IsSpawned)
                {
                    NetworkPlayer player = playerObject.GetComponent<NetworkPlayer>();
                    if (player == null)
                    {
                        throw new InvalidOperationException("The spawned NGO PlayerPrefab has no NetworkPlayer component.");
                    }
                    if (player.HasGameplaySpawnReady)
                    {
                        return;
                    }
                }

                await Task.Delay(100);
            }

            throw new TimeoutException("NGO did not initialize the server-authoritative player snapshot before the timeout.");
        }

        private static async Task WaitForNetworkStartedAsync(ISession session)
        {
            if (session == null)
            {
                throw new InvalidOperationException("No UGS session is available for the network connection.");
            }

            IClientSessionNetwork network = session.Network;
            if (network.State == NetworkState.Started)
            {
                return;
            }

            TaskCompletionSource<bool> started = new TaskCompletionSource<bool>();
            Action<NetworkState> stateChanged = state =>
            {
                if (state == NetworkState.Started)
                {
                    started.TrySetResult(true);
                }
                else if (state == NetworkState.Stopped)
                {
                    // Wait for a possible automatic start still in progress; StartFailed or timeout
                    // reports the actionable failure if the session cannot connect.
                }
            };
            Action<SessionError> startFailed = error =>
                started.TrySetException(new InvalidOperationException("Relay network start failed: " + error));

            network.StateChanged += stateChanged;
            network.StartFailed += startFailed;
            try
            {
                if (network.State == NetworkState.Started)
                {
                    return;
                }

                Task timeout = Task.Delay(TimeSpan.FromSeconds(NetworkStartTimeoutSeconds));
                Task completed = await Task.WhenAny(started.Task, timeout);
                if (completed != started.Task)
                {
                    throw new TimeoutException("The Relay / NGO network did not start before the timeout.");
                }

                await started.Task;
            }
            finally
            {
                network.StateChanged -= stateChanged;
                network.StartFailed -= startFailed;
            }
        }

        private void EnsureExactLoadedArena(MatchSession match)
        {
            if (match == null || worldManager == null || !worldManager.IsReady ||
                !worldManager.WorldOrigin.Equals(match.Centre) ||
                worldManager.GameplayRadiusMeters != match.RadiusMeters)
            {
                throw new InvalidOperationException(
                    "The loaded WGS84 centre/altitude or radius differs from the validated UGS session metadata.");
            }
        }

        private async Task ReleaseSessionQuietlyAsync()
        {
            if (combatMatchController != null)
            {
                combatMatchController.ResetMatch();
            }
            if (mapUsageTracker != null)
            {
                mapUsageTracker.EndMatchScope();
            }
            ISession session = activeSession;
            activeSession = null;
            activeMatch = null;
            if (session != null)
            {
                try
                {
                    await session.LeaveAsync();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[Phase 3] Session cleanup failed (" + exception.GetType().Name + ").",
                        this);
                }
            }
        }

        private static bool TryValidateDraft(
            GeoPosition centre,
            double radiusMeters,
            int maximumPlayers,
            string mapTilesApiKey,
            out string error)
        {
            error = string.Empty;
            if (!centre.IsValid)
            {
                error = "Latitude/longitude/ellipsoid altitude must form a valid WGS84 position.";
                return false;
            }
            if (!MatchSession.IsSupportedRadius(radiusMeters))
            {
                error = WorldLocationValidator.UnsupportedRadiusMessage;
                return false;
            }
            if (!MatchSession.IsSupportedPlayerCount(maximumPlayers))
            {
                error = "Maximum players must be from 2 through 32.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(mapTilesApiKey))
            {
                error = "Enter a restricted Google Map Tiles API key before creating a battle.";
                return false;
            }
            return true;
        }

        private static string NormalizeProfile(string profile)
        {
            if (string.IsNullOrWhiteSpace(profile))
            {
                return string.Empty;
            }

            string candidate = profile.Trim();
            if (candidate.Length > 30)
            {
                candidate = candidate.Substring(0, 30);
            }

            for (int i = 0; i < candidate.Length; i++)
            {
                char value = candidate[i];
                bool asciiLetter = (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
                bool digit = value >= '0' && value <= '9';
                if (!asciiLetter && !digit && value != '-' && value != '_')
                {
                    return string.Empty;
                }
            }
            return candidate;
        }

        private void SetState(BattleFlowState state, string message)
        {
            flowState = state;
            statusMessage = message ?? string.Empty;
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            Action changed = StateChanged;
            if (changed != null)
            {
                changed.Invoke();
            }
        }
    }
}
