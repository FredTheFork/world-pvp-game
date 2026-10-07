using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using WorldPvp.Phase1.Combat;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;
using WorldPvp.Phase1.Gameplay;

namespace WorldPvp.Phase1.Battles
{
    /// <summary>
    /// Starts the dedicated server path when built with UNITY_SERVER or ENABLE_UCS_SERVER. Server-only
    /// UGS assemblies are intentionally discovered by reflection so the existing WebGL runtime asmdef
    /// does not reference server Authentication/Multiplayer assemblies directly.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class PhaseEightDedicatedServerBootstrap : MonoBehaviour
    {
        private const string MultiplayerServerServiceTypeName =
            "Unity.Services.Multiplayer.MultiplayerServerService, Unity.Services.Multiplayer.Server";
        private const string ServerAuthenticationServiceTypeName =
            "Unity.Services.Authentication.Server.ServerAuthenticationService, Unity.Services.Authentication.Server";
        private const float WorldReadyTimeoutSeconds = 180f;

        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private PhaseEightServerAuthority serverAuthority;
        [SerializeField] private CombatMatchController matchController;
        [SerializeField] private GameplayCollisionWorld gameplayCollisionWorld;
        [SerializeField] private GameplayArenaRuntime gameplayArenaRuntime;
        [SerializeField] private bool launchInEditorWhenServerSymbolEnabled = true;

        private IServerSession serverSession;
        private bool bootstrapStarted;
        private bool shuttingDown;

        public event Action<IServerSession> DedicatedSessionStarted;
        public IServerSession ServerSession { get { return serverSession; } }

        public void Configure(
            GeospatialWorldManager manager,
            PhaseEightServerAuthority authority,
            CombatMatchController controller,
            GameplayCollisionWorld collisionWorld = null,
            GameplayArenaRuntime arenaRuntime = null)
        {
            worldManager = manager;
            serverAuthority = authority;
            matchController = controller;
            gameplayCollisionWorld = collisionWorld != null ? collisionWorld : FindObjectOfType<GameplayCollisionWorld>();
            gameplayArenaRuntime = arenaRuntime != null ? arenaRuntime : FindObjectOfType<GameplayArenaRuntime>();
        }

        private void Start()
        {
#if UNITY_SERVER || ENABLE_UCS_SERVER
            if (Application.isEditor && !launchInEditorWhenServerSymbolEnabled)
            {
                return;
            }
            if (!bootstrapStarted)
            {
                bootstrapStarted = true;
                StartDedicatedServerAsync();
            }
#else
            // A normal WebGL/client build never authenticates as a server and never loads service-account data.
            enabled = false;
#endif
        }

#if UNITY_SERVER || ENABLE_UCS_SERVER
        private async void StartDedicatedServerAsync()
        {
            try
            {
                ResolveSceneReferences();
                ValidateSceneReferences();

                ServerRuntimeConfiguration configuration = ReadServerRuntimeConfiguration();
                ApplyServerRuntimeConfiguration(configuration);
                if (gameplayArenaRuntime == null)
                {
                    throw new InvalidOperationException("The dedicated scene has no virtual gameplay-arena planner.");
                }
                string arenaMetadataError;
                if (!gameplayArenaRuntime.ConfigureMatch(
                        configuration.Centre,
                        configuration.RadiusMeters,
                        configuration.MaximumPlayers,
                        out arenaMetadataError))
                {
                    throw new InvalidOperationException(arenaMetadataError);
                }
                if (!worldManager.BeginArena(
                        configuration.Centre,
                        configuration.RadiusMeters,
                        string.Empty,
                        false))
                {
                    throw new InvalidOperationException("The independent gameplay arena could not be configured: " + worldManager.StatusMessage);
                }

                Debug.Log("[Phase 8] Dedicated server configured its WGS84/ENU arena with Google visual tiles disabled; waiting for independently sourced gameplay terrain.");
                float worldWaitStartedAt = Time.realtimeSinceStartup;
                while (!worldManager.IsReady)
                {
                    if (worldManager.State == GeospatialWorldState.Failed)
                    {
                        throw new InvalidOperationException("The dedicated server's Cesium world failed: " + worldManager.StatusMessage);
                    }
                    if (Time.realtimeSinceStartup - worldWaitStartedAt >= WorldReadyTimeoutSeconds)
                    {
                        throw new TimeoutException("The dedicated server did not obtain independent gameplay terrain before the timeout.");
                    }
                    await Task.Delay(100);
                }

                if (gameplayCollisionWorld == null)
                {
                    throw new InvalidOperationException("The dedicated scene is missing GameplayCollisionWorld.");
                }
                string gameplayDataError;
                if (!gameplayCollisionWorld.IsProductionReady(
                        configuration.Centre,
                        configuration.RadiusMeters,
                        out gameplayDataError))
                {
                    throw new InvalidOperationException(gameplayDataError);
                }
                string planError;
                if (!gameplayArenaRuntime.TryPrepareArena(out planError))
                {
                    throw new InvalidOperationException("Safe virtual arena preparation failed: " + planError);
                }

                await InitializeServicesAndAuthenticateAsync();
                serverSession = await CreateServerSessionAsync(configuration);
                if (serverSession == null)
                {
                    throw new InvalidOperationException("MultiplayerServerService returned no server session.");
                }

                // Session creation alone does not prove NGO is listening. Explicitly start the server Relay handler.
                await serverSession.Network.StartRelayNetworkAsync(new RelayNetworkOptions());
                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsServer || !manager.IsListening)
                {
                    throw new InvalidOperationException(
                        "The Multiplayer Services Relay start completed, but NGO is not listening as a server.");
                }

                serverAuthority.ConfigureDedicatedSession(true);
                Debug.Log(
                    "PHASE8_DEDICATED_SESSION_READY id=" + serverSession.Id +
                    " code=" + serverSession.Code +
                    " maxPlayers=" + serverSession.MaxPlayers +
                    " network=Relay/NGO-server. Publish the join code through a trusted matchmaker/backend; do not place service-account credentials in a client.");
                Action<IServerSession> callback = DedicatedSessionStarted;
                if (callback != null)
                {
                    callback(serverSession);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[Phase 8] Dedicated server startup failed (" + exception.GetType().Name + "): " + exception.Message,
                    this);
                NetworkManager manager = NetworkManager.Singleton;
                if (manager != null && manager.IsListening)
                {
                    manager.Shutdown();
                }
            }
        }

        private void ResolveSceneReferences()
        {
            if (worldManager == null)
            {
                worldManager = FindObjectOfType<GeospatialWorldManager>();
            }
            if (serverAuthority == null)
            {
                serverAuthority = FindObjectOfType<PhaseEightServerAuthority>();
            }
            if (matchController == null)
            {
                matchController = FindObjectOfType<CombatMatchController>();
            }
            if (gameplayCollisionWorld == null)
            {
                gameplayCollisionWorld = FindObjectOfType<GameplayCollisionWorld>();
            }
            if (gameplayArenaRuntime == null)
            {
                gameplayArenaRuntime = FindObjectOfType<GameplayArenaRuntime>();
            }
        }

        private void ValidateSceneReferences()
        {
            if (worldManager == null || serverAuthority == null || matchController == null ||
                gameplayCollisionWorld == null || gameplayArenaRuntime == null)
            {
                throw new InvalidOperationException(
                    "The server scene is missing GeospatialWorldManager, PhaseEightServerAuthority, CombatMatchController, GameplayCollisionWorld, or GameplayArenaRuntime.");
            }
            if (NetworkManager.Singleton == null)
            {
                throw new InvalidOperationException("The generated server scene has no NGO NetworkManager.");
            }
        }

        private void ApplyServerRuntimeConfiguration(ServerRuntimeConfiguration configuration)
        {
            serverAuthority.ConfigureDedicatedSession(true);
            serverAuthority.ConfigureLagCompensationWindow(configuration.LagCompensationWindowSeconds);
            serverAuthority.ConfigureRuntime(
                configuration.SpatialCellSizeMeters,
                configuration.MatchDurationSeconds,
                configuration.MaximumPlayers);
            if (matchController != null)
            {
                matchController.ResetMatch();
            }
        }

        private async Task InitializeServicesAndAuthenticateAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            Type authenticationServiceType = Type.GetType(ServerAuthenticationServiceTypeName, false);
            if (authenticationServiceType == null)
            {
                throw new InvalidOperationException(
                    "ServerAuthenticationService is absent. Build with UNITY_SERVER or ENABLE_UCS_SERVER and preserve Unity.Services.Authentication.Server.dll.");
            }

            object authenticationService = GetStaticInstance(authenticationServiceType);
            Type authenticationApiType = Type.GetType(
                "Unity.Services.Authentication.Server.IServerAuthenticationService, Unity.Services.Authentication.Server",
                false);
            if (authenticationApiType == null)
            {
                throw new InvalidOperationException("IServerAuthenticationService is absent from the server Authentication assembly.");
            }
            string keyId = Environment.GetEnvironmentVariable("WORLD_PVP_SERVER_SERVICE_ACCOUNT_KEY_ID");
            string keySecret = Environment.GetEnvironmentVariable("WORLD_PVP_SERVER_SERVICE_ACCOUNT_KEY_SECRET");
            Task authenticationTask;
            if (!string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(keySecret))
            {
                MethodInfo signIn = authenticationApiType.GetMethod(
                    "SignInWithServiceAccountAsync",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(string) },
                    null);
                if (signIn == null)
                {
                    throw new MissingMethodException(authenticationApiType.FullName, "SignInWithServiceAccountAsync(string,string)");
                }
                authenticationTask = signIn.Invoke(authenticationService, new object[] { keyId, keySecret }) as Task;
            }
            else if (string.Equals(
                         Environment.GetEnvironmentVariable("WORLD_PVP_USE_SERVER_IDENTITY"),
                         "true",
                         StringComparison.OrdinalIgnoreCase))
            {
                MethodInfo signIn = authenticationApiType.GetMethod(
                    "SignInFromServerAsync",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);
                if (signIn == null)
                {
                    throw new MissingMethodException(authenticationApiType.FullName, "SignInFromServerAsync()");
                }
                authenticationTask = signIn.Invoke(authenticationService, null) as Task;
            }
            else
            {
                throw new InvalidOperationException(
                    "Set server-only service account environment variables, or explicitly enable WORLD_PVP_USE_SERVER_IDENTITY on a supported hosted server.");
            }

            if (authenticationTask == null)
            {
                throw new InvalidOperationException("Server Authentication did not return an asynchronous sign-in task.");
            }
            await authenticationTask;
        }

        private async Task<IServerSession> CreateServerSessionAsync(ServerRuntimeConfiguration configuration)
        {
            Type serviceType = Type.GetType(MultiplayerServerServiceTypeName, false);
            if (serviceType == null)
            {
                throw new InvalidOperationException(
                    "MultiplayerServerService is absent. Build with UNITY_SERVER or ENABLE_UCS_SERVER and preserve Unity.Services.Multiplayer.Server.dll.");
            }

            object service = GetStaticInstance(serviceType);
            Type serverApiType = Type.GetType(
                "Unity.Services.Multiplayer.IMultiplayerServerService, Unity.Services.Multiplayer.Server",
                false);
            if (serverApiType == null)
            {
                throw new InvalidOperationException("IMultiplayerServerService is absent from the server Multiplayer assembly.");
            }
            MethodInfo createMethod = serverApiType.GetMethod(
                "CreateSessionAsync",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(SessionOptions) },
                null);
            if (createMethod == null)
            {
                throw new MissingMethodException(serverApiType.FullName, "CreateSessionAsync(SessionOptions)");
            }

            DateTimeOffset createdAtUtc = DateTimeOffset.UtcNow;
            Dictionary<string, SessionProperty> properties = MatchSession.CreateMemberProperties(
                configuration.Centre,
                configuration.RadiusMeters,
                configuration.MaximumPlayers,
                MatchSessionState.Lobby,
                createdAtUtc,
                MatchSession.DefaultGameMode,
                true);
            SessionOptions options = new SessionOptions
            {
                Name = "World PvP Dedicated Match",
                MaxPlayers = configuration.MaximumPlayers,
                IsPrivate = true,
                IsLocked = false,
                SessionProperties = properties
            };

            Task<IServerSession> createTask = createMethod.Invoke(service, new object[] { options }) as Task<IServerSession>;
            if (createTask == null)
            {
                throw new InvalidOperationException("MultiplayerServerService returned an unexpected CreateSessionAsync task type.");
            }
            return await createTask;
        }

        private static object GetStaticInstance(Type serviceType)
        {
            PropertyInfo instanceProperty = serviceType.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static);
            if (instanceProperty == null)
            {
                throw new MissingMemberException(serviceType.FullName, "Instance");
            }
            object instance = instanceProperty.GetValue(null, null);
            if (instance == null)
            {
                throw new InvalidOperationException(serviceType.FullName + ".Instance returned null.");
            }
            return instance;
        }

        private static ServerRuntimeConfiguration ReadServerRuntimeConfiguration()
        {
            PhaseOneWorldSettings settings = FindObjectOfType<PhaseOneWorldSettings>();
            GeoPosition defaultCentre = settings != null ? settings.DefaultOrigin : default(GeoPosition);
            double latitude = ReadRequiredDouble("WORLD_PVP_CENTRE_LATITUDE");
            double longitude = ReadRequiredDouble("WORLD_PVP_CENTRE_LONGITUDE");
            double altitude = ReadOptionalDouble("WORLD_PVP_CENTRE_ALTITUDE_METERS", defaultCentre.AltitudeMeters);
            double radius = ReadRequiredDouble("WORLD_PVP_ARENA_RADIUS_METERS");
            int maximumPlayers = ReadOptionalInt("WORLD_PVP_MAX_PLAYERS", 8);
            int matchDuration = ReadOptionalInt("WORLD_PVP_MATCH_DURATION_SECONDS", 600);
            float lagWindow = ReadOptionalFloat("WORLD_PVP_LAG_COMPENSATION_SECONDS", 0.4f);
            float cellSize = ReadOptionalFloat("WORLD_PVP_SPATIAL_CELL_METERS", 25f);
            bool coverageConfirmed = string.Equals(
                Environment.GetEnvironmentVariable("WORLD_PVP_COVERAGE_CONFIRMED"),
                "true",
                StringComparison.OrdinalIgnoreCase);

            GeoPosition centre = new GeoPosition(latitude, longitude, altitude);
            if (!centre.IsValid || !MatchSession.IsSupportedRadius(radius) ||
                !MatchSession.IsSupportedPlayerCount(maximumPlayers) || matchDuration < 1 || !coverageConfirmed)
            {
                throw new InvalidOperationException(
                    "Dedicated server configuration is invalid. Supply a valid virtual arena, 2–32 players, duration, and WORLD_PVP_COVERAGE_CONFIRMED=true after manually verifying visual-map coverage outside the server.");
            }

            return new ServerRuntimeConfiguration(
                centre,
                radius,
                maximumPlayers,
                matchDuration,
                Mathf.Clamp(lagWindow, 0.25f, 0.5f),
                Mathf.Clamp(cellSize, 5f, 500f));
        }

        private static double ReadRequiredDouble(string name)
        {
            string text = Environment.GetEnvironmentVariable(name);
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidOperationException(name + " must be set to a finite invariant-culture number.");
            }
            return value;
        }

        private static double ReadOptionalDouble(string name, double fallback)
        {
            string text = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new InvalidOperationException(name + " must be a finite invariant-culture number.");
            }
            return value;
        }

        private static int ReadOptionalInt(string name, int fallback)
        {
            string text = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidOperationException(name + " must be an integer.");
            }
            return value;
        }

        private static float ReadOptionalFloat(string name, float fallback)
        {
            string text = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }
            float value;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidOperationException(name + " must be a finite invariant-culture number.");
            }
            return value;
        }

        private async void OnApplicationQuit()
        {
            await ShutdownServerSessionAsync();
        }

        private async Task ShutdownServerSessionAsync()
        {
            if (shuttingDown)
            {
                return;
            }
            shuttingDown = true;
            if (serverSession == null)
            {
                return;
            }

            try
            {
                await serverSession.Network.StopNetworkAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Phase 8] Server network shutdown reported " + exception.GetType().Name + ".");
            }
            try
            {
                await serverSession.LeaveAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Phase 8] Server session cleanup reported " + exception.GetType().Name + ".");
            }
            serverSession = null;
        }

        private sealed class ServerRuntimeConfiguration
        {
            public readonly GeoPosition Centre;
            public readonly double RadiusMeters;
            public readonly int MaximumPlayers;
            public readonly int MatchDurationSeconds;
            public readonly float LagCompensationWindowSeconds;
            public readonly float SpatialCellSizeMeters;

            public ServerRuntimeConfiguration(
                GeoPosition centre,
                double radiusMeters,
                int maximumPlayers,
                int matchDurationSeconds,
                float lagCompensationWindowSeconds,
                float spatialCellSizeMeters)
            {
                Centre = centre;
                RadiusMeters = radiusMeters;
                MaximumPlayers = maximumPlayers;
                MatchDurationSeconds = matchDurationSeconds;
                LagCompensationWindowSeconds = lagCompensationWindowSeconds;
                SpatialCellSizeMeters = spatialCellSizeMeters;
            }
        }
#endif
    }
}
