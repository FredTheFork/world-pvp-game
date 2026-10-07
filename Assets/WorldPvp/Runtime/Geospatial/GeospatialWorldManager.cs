using System;
using CesiumForUnity;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Gameplay;
using WorldPvp.Phase1.Player;
using WorldPvp.Phase1.Streaming;

namespace WorldPvp.Phase1.Geospatial
{
    public enum GeospatialWorldState
    {
        AwaitingLocation,
        AwaitingApiKey,
        WaitingForTileCollision,
        WaitingForGameplaySurface,
        Ready,
        Failed
    }

    /// <summary>
    /// The single owner of the selected arena origin, WGS84/local ENU conversions,
    /// metre-based arena queries, gameplay-provider ground lookup, and the virtual boundary.
    /// Photorealistic rendering remains separate; the captured ENU frame stays fixed when
    /// CesiumOriginShift moves Cesium's render origin.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeospatialWorldManager : MonoBehaviour
    {
        private const double BoundaryEpsilonMeters = 0.001;
        private const double MaximumGameplayRadiusMeters = 50000.0;
        private const float BoundaryFeedbackSeconds = 1.5f;
        private const double DefaultVisibilityBufferMeters = 100.0;
        private const double DefaultPreloadBufferMeters = 150.0;

        [Header("Cesium scene references")]
        [SerializeField] private CesiumGeoreference georeference;
        [SerializeField] private Cesium3DTileset photorealisticTileset;
        [SerializeField] private Transform testCharacterRoot;
        [SerializeField] private CesiumGlobeAnchor testCharacterGlobeAnchor;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private PhaseOneTestCharacterController characterMotor;
        [SerializeField] private PhaseOneWorldSettings settings;
        [SerializeField] private ArenaBoundaryVisualizer boundaryVisualizer;
        [SerializeField] private Camera arenaCamera;
        [SerializeField] private WorldStreamingController streamingController;
        [SerializeField] private MapUsageTracker mapUsageTracker;
        [Header("Gameplay terrain/collision — independent of visual tiles")]
        [SerializeField] private GameplayCollisionWorld gameplayCollisionWorld;
        [SerializeField] private GameplayArenaRuntime gameplayArenaRuntime;

        private GeospatialWorldState state = GeospatialWorldState.AwaitingLocation;
        private GeoPosition worldOrigin;
        private double gameplayRadiusMeters;
        private double4x4 worldOriginLocalToEcef = double4x4.identity;
        private double4x4 worldOriginEcefToLocal = double4x4.identity;
        private bool worldOriginFrameReady;
        private bool hasArenaGroundPosition;
        private double arenaGroundAltitudeMeters;
        private string statusMessage = "Choose a location and provide a Google Map Tiles API key.";
        private float nextGroundProbeTime;
        private float loadStartedAt;
        private float lastBoundaryCorrectionTime = float.NegativeInfinity;

        public event Action WorldBecameReady;

        public GeospatialWorldState State { get { return state; } }
        /// <summary>The fixed WGS84 arena origin; it is not Cesium's transient origin-shift location.</summary>
        public GeoPosition WorldOrigin { get { return worldOrigin; } }
        public GeoPosition ArenaCentre { get { return worldOrigin; } }
        public string StatusMessage { get { return statusMessage; } }
        public bool IsReady { get { return state == GeospatialWorldState.Ready; } }
        public bool IsArenaConfigured { get { return worldOriginFrameReady && gameplayRadiusMeters > 0.0; } }
        public bool HasArenaGroundPosition { get { return hasArenaGroundPosition; } }
        public double ArenaGroundAltitudeMeters { get { return arenaGroundAltitudeMeters; } }
        public double GameplayRadiusMeters { get { return gameplayRadiusMeters; } }
        public double VisibilityBufferMeters
        {
            get { return settings != null && GeoPosition.IsFinite(settings.VisibilityBufferMeters)
                    ? Math.Max(0.0, settings.VisibilityBufferMeters)
                    : DefaultVisibilityBufferMeters; }
        }
        public double PreloadBufferMeters
        {
            get { return settings != null && GeoPosition.IsFinite(settings.PreloadBufferMeters)
                    ? Math.Max(0.0, settings.PreloadBufferMeters)
                    : DefaultPreloadBufferMeters; }
        }
        /// <summary>Visible extent is gameplay radius plus the visibility buffer.</summary>
        public double VisibilityRadiusMeters { get { return gameplayRadiusMeters + VisibilityBufferMeters; } }
        /// <summary>Render/preload envelope is the visible extent plus its outer preload buffer.</summary>
        public double RenderRadiusMeters { get { return VisibilityRadiusMeters + PreloadBufferMeters; } }
        public Transform CharacterRoot { get { return testCharacterRoot; } }
        public CesiumGeoreference Georeference { get { return georeference; } }
        public Cesium3DTileset Tileset { get { return photorealisticTileset; } }
        public PhaseOneWorldSettings WorldSettings { get { return settings; } }

        /// <summary>
        /// Current WGS84 anchor position. Use HasCurrentPlayerGeographicPosition or TryGetPlayerGeographicPosition
        /// before consuming the default value when the world is not ready.
        /// </summary>
        public GeoPosition CurrentPlayerGeographicPosition
        {
            get
            {
                GeoPosition position;
                return TryGetPlayerGeographicPosition(out position) ? position : default(GeoPosition);
            }
        }

        public bool HasCurrentPlayerGeographicPosition
        {
            get
            {
                GeoPosition ignored;
                return TryGetPlayerGeographicPosition(out ignored);
            }
        }

        public LocalPosition CurrentPlayerLocalPosition
        {
            get
            {
                GeoPosition geographicPosition;
                return TryGetPlayerGeographicPosition(out geographicPosition)
                    ? GeographicToLocal(geographicPosition)
                    : default(LocalPosition);
            }
        }

        public string BoundaryFeedback
        {
            get
            {
                if (!IsReady || !IsArenaConfigured || !HasCurrentPlayerGeographicPosition)
                {
                    return "Arena boundary feedback is available after the player is placed on the streamed surface.";
                }

                string authorityNotice = GetBoundaryAuthorityNotice();
                if (Time.unscaledTime - lastBoundaryCorrectionTime <= BoundaryFeedbackSeconds)
                {
                    return "Arena edge reached — " + authorityNotice;
                }

                double distance = GetDistanceFromArenaCentre(CurrentPlayerGeographicPosition);
                double remaining = Math.Max(0.0, gameplayRadiusMeters - distance);
                double warningDistance = settings != null && GeoPosition.IsFinite(settings.BoundaryWarningDistanceMeters)
                    ? Math.Max(0.0, settings.BoundaryWarningDistanceMeters)
                    : 25.0;
                if (remaining <= warningDistance)
                {
                    return string.Format(
                        "Near gameplay edge — {0:F1} m remaining. {1}",
                        remaining,
                        authorityNotice);
                }

                return string.Format(
                    "Inside arena — {0:F1} m from centre / {1:F0} m gameplay radius. {2}",
                    distance,
                    gameplayRadiusMeters,
                    authorityNotice);
            }
        }

        private static string GetBoundaryAuthorityNotice()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening
                ? "Phase 4 movement is host-authoritative; Relay host is not a dedicated server."
                : "Local prototype boundary; server authority is not active.";
        }

        private void Awake()
        {
            if (settings != null)
            {
                worldOrigin = settings.DefaultOrigin;
                gameplayRadiusMeters = settings.DefaultGameplayRadiusMeters;
            }

            if (gameplayCollisionWorld == null)
            {
                gameplayCollisionWorld = FindObjectOfType<GameplayCollisionWorld>();
            }
            if (gameplayArenaRuntime == null)
            {
                gameplayArenaRuntime = FindObjectOfType<GameplayArenaRuntime>();
            }
            if (photorealisticTileset != null)
            {
                // A real key must be supplied before any Map Tiles request is made.
                photorealisticTileset.enabled = false;
            }

            if (characterController != null)
            {
                characterController.enabled = false;
            }

            if (characterMotor != null)
            {
                characterMotor.SetInputEnabled(false);
            }

            if (arenaCamera == null)
            {
                arenaCamera = Camera.main;
            }

            state = GeospatialWorldState.AwaitingLocation;
        }

        /// <summary>Called by the Editor scene generator; serialized references remain authoritative afterwards.</summary>
        public void ConfigureReferences(
            CesiumGeoreference cesiumGeoreference,
            Cesium3DTileset tileset,
            Transform characterRoot,
            CesiumGlobeAnchor characterAnchor,
            CharacterController controller,
            PhaseOneTestCharacterController motor,
            PhaseOneWorldSettings worldSettings,
            ArenaBoundaryVisualizer visualizer = null,
            Camera mainCamera = null)
        {
            georeference = cesiumGeoreference;
            photorealisticTileset = tileset;
            testCharacterRoot = characterRoot;
            testCharacterGlobeAnchor = characterAnchor;
            characterController = controller;
            characterMotor = motor;
            settings = worldSettings;
            boundaryVisualizer = visualizer;
            arenaCamera = mainCamera != null ? mainCamera : Camera.main;
            if (!worldOriginFrameReady && settings != null)
            {
                worldOrigin = settings.DefaultOrigin;
                gameplayRadiusMeters = settings.DefaultGameplayRadiusMeters;
            }

            if (boundaryVisualizer != null)
            {
                boundaryVisualizer.Configure(this);
            }
        }

        /// <summary>Connects stable gameplay terrain/proxies and the virtual arena planner.</summary>
        public void ConfigureGameplay(
            GameplayCollisionWorld collisionWorld,
            GameplayArenaRuntime arenaRuntime)
        {
            gameplayCollisionWorld = collisionWorld;
            gameplayArenaRuntime = arenaRuntime;
            if (gameplayCollisionWorld != null)
            {
                gameplayCollisionWorld.ConfigureReferences(this, settings);
            }
            if (gameplayArenaRuntime != null)
            {
                gameplayArenaRuntime.ConfigureReferences(this, settings, gameplayCollisionWorld);
            }
        }

        /// <summary>Connects the Phase 6 streaming controls and local map-usage observers.</summary>
        public void ConfigurePhaseSix(
            WorldStreamingController controller,
            MapUsageTracker usageTracker)
        {
            streamingController = controller;
            mapUsageTracker = usageTracker;
            if (mapUsageTracker != null)
            {
                mapUsageTracker.Configure(this, settings);
            }
            if (streamingController != null)
            {
                streamingController.Configure(this, settings, arenaCamera);
            }
        }

        /// <summary>
        /// Configures a geographic arena independently of streaming. The selected WGS84 location becomes
        /// both the gameplay centre and the fixed ENU origin. The saved ECEF matrices are intentionally
        /// not replaced when CesiumOriginShift later moves the render origin.
        /// </summary>
        public bool SetArenaDefinition(GeoPosition centre, double radiusMeters)
        {
            if (!centre.IsValid || !GeoPosition.IsFinite(radiusMeters) ||
                radiusMeters <= 0.0 || radiusMeters > MaximumGameplayRadiusMeters)
            {
                return false;
            }

            if (georeference == null)
            {
                return false;
            }

            worldOriginFrameReady = false;
            try
            {
                georeference.Initialize();
                Vector3 worldScale = georeference.transform.lossyScale;
                if (Mathf.Abs(worldScale.x - 1f) > 0.0001f ||
                    Mathf.Abs(worldScale.y - 1f) > 0.0001f ||
                    Mathf.Abs(worldScale.z - 1f) > 0.0001f)
                {
                    Debug.LogError("[Geospatial] CesiumGeoreference Transform scale must be one so Unity units remain real metres.", this);
                    return false;
                }
                // A Unity world unit must remain one real metre for the ENU gameplay contract.
                georeference.scale = 1.0;
                georeference.SetOriginLongitudeLatitudeHeight(
                    centre.LongitudeDegrees,
                    centre.LatitudeDegrees,
                    centre.AltitudeMeters);

                worldOrigin = centre;
                gameplayRadiusMeters = radiusMeters;
                worldOriginLocalToEcef = georeference.localToEcefMatrix;
                worldOriginEcefToLocal = math.inverse(worldOriginLocalToEcef);
                worldOriginFrameReady = true;
                hasArenaGroundPosition = false;
                arenaGroundAltitudeMeters = 0.0;

                if (boundaryVisualizer != null)
                {
                    boundaryVisualizer.Refresh();
                }
                ApplyRenderEnvelope();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[Geospatial] Arena-frame setup failed (" + exception.GetType().Name + ").",
                    this);
                worldOriginFrameReady = false;
                return false;
            }
        }

        /// <summary>
        /// Starts a local arena at a WGS84 location. Gameplay terrain comes from the independent
        /// provider; an explicitly enabled prototype fallback may probe a transient visual-tile collider.
        /// </summary>
        public bool BeginArena(GeoPosition centre, double radiusMeters, string apiKey)
        {
            return BeginArena(centre, radiusMeters, apiKey, true);
        }

        /// <summary>
        /// Starts an arena with an optional photorealistic visual renderer. Dedicated servers pass
        /// false: gameplay terrain/features must come from the approved independent provider and no
        /// Google visual-tile collider is queried for game decisions.
        /// </summary>
        public bool BeginArena(
            GeoPosition centre,
            double radiusMeters,
            string apiKey,
            bool enablePhotorealisticRenderer)
        {
            if (!centre.IsValid)
            {
                return Fail("Latitude must be -90…90, longitude -180…180, and all values must be finite.");
            }

            if (!GeoPosition.IsFinite(radiusMeters) || radiusMeters <= 0.0 ||
                radiusMeters > MaximumGameplayRadiusMeters)
            {
                return Fail("Gameplay radius must be greater than zero and no more than 50,000 real metres.");
            }

            if (settings == null || georeference == null ||
                (enablePhotorealisticRenderer && photorealisticTileset == null) ||
                testCharacterRoot == null || testCharacterGlobeAnchor == null ||
                characterController == null || characterMotor == null || gameplayCollisionWorld == null)
            {
                return Fail("The generated scene is missing geospatial or independent gameplay-collision references. Rebuild the World PvP scene.");
            }

            characterMotor.SetInputEnabled(false);
            characterController.enabled = false;
            if (photorealisticTileset != null)
            {
                photorealisticTileset.enabled = false;
            }
            state = GeospatialWorldState.AwaitingLocation;
            statusMessage = "Applying geographic arena origin…";

            if (!SetArenaDefinition(centre, radiusMeters))
            {
                return Fail("The geographic arena origin could not be configured. Check the Unity Console.");
            }

            string gameplayDataError;
            if (!gameplayCollisionWorld.ConfigureArena(
                    centre,
                    radiusMeters,
                    !enablePhotorealisticRenderer,
                    out gameplayDataError))
            {
                return Fail("Gameplay terrain/collision is unavailable for this arena: " + gameplayDataError);
            }

            try
            {
                testCharacterGlobeAnchor.detectTransformChanges = true;
                testCharacterGlobeAnchor.longitudeLatitudeHeight = new double3(
                    centre.LongitudeDegrees,
                    centre.LatitudeDegrees,
                    centre.AltitudeMeters + settings.InitialSpawnHeightAboveOriginMeters);
                characterMotor.PrepareForGroundProbe();
                testCharacterGlobeAnchor.Sync();

                if (!enablePhotorealisticRenderer)
                {
                    loadStartedAt = Time.unscaledTime;
                    nextGroundProbeTime = Time.unscaledTime;
                    state = GeospatialWorldState.WaitingForGameplaySurface;
                    statusMessage = "Dedicated-server mode: waiting for independently sourced gameplay terrain; visual tiles are disabled.";
                    return true;
                }

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    state = GeospatialWorldState.AwaitingApiKey;
                    statusMessage = "Arena origin set. Enter a restricted Map Tiles API key to start visual streaming.";
                    return true;
                }

                photorealisticTileset.tilesetSource = CesiumDataSource.FromUrl;
                photorealisticTileset.url = GooglePhotorealisticTilesUrl.BuildRootTilesetUrl(apiKey);
                photorealisticTileset.showCreditsOnScreen = true;
                // Visual tile colliders are created only for the explicitly labelled local prototype.
                // Approved gameplay terrain/proxies are separate and do not depend on this setting.
                photorealisticTileset.createPhysicsMeshes = gameplayCollisionWorld.IsPrototypeFallback;

                // Google tiles remain view/frustum streamed. The render radius configures an
                // envelope and camera far plane; it is not a hard radial tile-cache cutoff.
                photorealisticTileset.enableFrustumCulling = true;
                if (mapUsageTracker != null)
                {
                    mapUsageTracker.BindTileset(photorealisticTileset);
                    mapUsageTracker.RecordRootTilesetLoadAttempt();
                }
                if (streamingController != null)
                {
                    streamingController.ApplySettingsToTileset();
                }
                ApplyRenderEnvelope();
                photorealisticTileset.enabled = true;

                loadStartedAt = Time.unscaledTime;
                nextGroundProbeTime = Time.unscaledTime + 0.5f;
                state = GeospatialWorldState.WaitingForGameplaySurface;
                statusMessage = gameplayCollisionWorld.HasIndependentData
                    ? "Visual tiles are streaming separately; waiting for the independent gameplay terrain sample…"
                    : "PROTOTYPE fallback: waiting for a transient visual-tile ground probe. Hazard avoidance is not verified.";
                return true;
            }
            catch (Exception exception)
            {
                // Do not log request URLs: they contain the client API key.
                Debug.LogError(
                    "[Phase 2] Cesium world setup failed (" + exception.GetType().Name + "). " +
                    "Check the Console and Google Cloud API configuration; the API key has been withheld.",
                    this);
                return Fail("World setup failed. Check the Unity Console and Google Cloud configuration.");
            }
        }

        /// <summary>Convenience entry point that uses the ScriptableObject's default arena radius.</summary>
        public bool BeginLocation(GeoPosition origin, string apiKey)
        {
            double radius = settings != null ? settings.DefaultGameplayRadiusMeters : 500.0;
            return BeginArena(origin, radius, apiKey);
        }

        private void Update()
        {
            if ((state != GeospatialWorldState.WaitingForTileCollision &&
                 state != GeospatialWorldState.WaitingForGameplaySurface) ||
                Time.unscaledTime < nextGroundProbeTime)
            {
                return;
            }

            nextGroundProbeTime = Time.unscaledTime + settings.GroundProbeIntervalSeconds;
            GeoPosition groundPosition = default(GeoPosition);
            bool foundGround = false;
            if (gameplayCollisionWorld != null && gameplayCollisionWorld.HasIndependentData)
            {
                double groundAltitude;
                Vector3 groundNormal;
                if (gameplayCollisionWorld.TryGetIndependentGroundSurface(
                        worldOrigin,
                        out groundAltitude,
                        out groundNormal))
                {
                    groundPosition = new GeoPosition(
                        worldOrigin.LatitudeDegrees,
                        worldOrigin.LongitudeDegrees,
                        groundAltitude);
                    foundGround = true;
                }
            }
            else if (gameplayCollisionWorld != null && gameplayCollisionWorld.IsPrototypeFallback)
            {
                RaycastHit groundHit;
                if (TryFindCesiumGround(worldOrigin, out groundHit))
                {
                    groundPosition = UnityWorldToGeographic(groundHit.point);
                    foundGround = true;
                }
            }

            if (foundGround)
            {
                arenaGroundAltitudeMeters = groundPosition.AltitudeMeters;
                hasArenaGroundPosition = true;
                if (boundaryVisualizer != null)
                {
                    boundaryVisualizer.Refresh();
                }
                PlaceCharacterOnGround(groundPosition);
                return;
            }

            if (Time.unscaledTime - loadStartedAt >= settings.GroundProbeTimeoutSeconds)
            {
                state = GeospatialWorldState.Failed;
                statusMessage = gameplayCollisionWorld != null && gameplayCollisionWorld.HasIndependentData
                    ? "No independent gameplay terrain sample is available at the arena centre. The renderer is not used as a gameplay-data substitute."
                    : "No ground sample is available. Prototype tile fallback may be missing; production requires approved independent terrain and feature data.";
            }
        }

        /// <summary>
        /// Converts WGS84 geographic coordinates into metres in the selected arena's fixed ENU frame.
        /// X is East, Y is Up, Z is North. The result is independent of Cesium's current shifted origin.
        /// </summary>
        public LocalPosition GeographicToLocal(GeoPosition geographicPosition)
        {
            EnsureArenaFrame();
            if (!geographicPosition.IsValid)
            {
                throw new ArgumentException("A valid WGS84 position is required.", "geographicPosition");
            }

            double3 ecef = ToEarthCenteredEarthFixed(geographicPosition);
            double3 local = math.mul(worldOriginEcefToLocal, new double4(ecef, 1.0)).xyz;
            return new LocalPosition(local.x, local.y, local.z);
        }

        /// <summary>
        /// Converts metres in the selected arena's fixed ENU frame into WGS84 coordinates.
        /// X is East, Y is Up, Z is North. The result is independent of Cesium's current shifted origin.
        /// </summary>
        public GeoPosition LocalToGeographic(LocalPosition localPosition)
        {
            EnsureArenaFrame();
            if (!localPosition.IsFinite)
            {
                throw new ArgumentException("A finite ENU local position is required.", "localPosition");
            }

            double3 ecef = math.mul(
                worldOriginLocalToEcef,
                new double4(localPosition.EastMeters, localPosition.UpMeters, localPosition.NorthMeters, 1.0)).xyz;
            double3 longitudeLatitudeHeight =
                georeference.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(ecef);
            return new GeoPosition(
                longitudeLatitudeHeight.y,
                longitudeLatitudeHeight.x,
                longitudeLatitudeHeight.z);
        }

        /// <summary>Transforms WGS84 into the current Cesium/Unity world frame.</summary>
        public Vector3 GeographicToUnityWorld(GeoPosition geographicPosition)
        {
            EnsureArenaFrame();
            if (!geographicPosition.IsValid)
            {
                throw new ArgumentException("A valid WGS84 position is required.", "geographicPosition");
            }

            double3 ecef = ToEarthCenteredEarthFixed(geographicPosition);
            double3 unityInGeoreferenceParent =
                georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            return georeference.transform.TransformPoint(ToVector3(unityInGeoreferenceParent));
        }

        /// <summary>Transforms a Unity world position into WGS84 using the current Cesium origin.</summary>
        public GeoPosition UnityWorldToGeographic(Vector3 unityWorldPosition)
        {
            EnsureArenaFrame();
            Vector3 inGeoreferenceParent = georeference.transform.InverseTransformPoint(unityWorldPosition);
            double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(
                new double3(inGeoreferenceParent.x, inGeoreferenceParent.y, inGeoreferenceParent.z));
            double3 longitudeLatitudeHeight =
                georeference.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(ecef);
            return new GeoPosition(
                longitudeLatitudeHeight.y,
                longitudeLatitudeHeight.x,
                longitudeLatitudeHeight.z);
        }

        /// <summary>
        /// Converts an arena-local ENU direction into the current Unity render frame. The endpoint
        /// is transformed through the fixed WGS84 frame, so this remains valid after Cesium origin shifts.
        /// </summary>
        public Vector3 LocalDirectionToUnityWorld(LocalPosition localOrigin, Vector3 directionEastUpNorth)
        {
            EnsureArenaFrame();
            if (!localOrigin.IsFinite ||
                float.IsNaN(directionEastUpNorth.x) || float.IsInfinity(directionEastUpNorth.x) ||
                float.IsNaN(directionEastUpNorth.y) || float.IsInfinity(directionEastUpNorth.y) ||
                float.IsNaN(directionEastUpNorth.z) || float.IsInfinity(directionEastUpNorth.z) ||
                directionEastUpNorth.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException("A finite local ENU origin and non-zero direction are required.");
            }

            Vector3 normalized = directionEastUpNorth.normalized;
            const double sampleDistanceMeters = 10.0;
            LocalPosition endpoint = new LocalPosition(
                localOrigin.EastMeters + normalized.x * sampleDistanceMeters,
                localOrigin.UpMeters + normalized.y * sampleDistanceMeters,
                localOrigin.NorthMeters + normalized.z * sampleDistanceMeters);
            Vector3 worldOrigin = GeographicToUnityWorld(LocalToGeographic(localOrigin));
            Vector3 worldEndpoint = GeographicToUnityWorld(LocalToGeographic(endpoint));
            Vector3 worldDirection = worldEndpoint - worldOrigin;
            if (worldDirection.sqrMagnitude < 0.000001f)
            {
                throw new InvalidOperationException("The local ENU direction could not be represented in Unity world space.");
            }
            return worldDirection.normalized;
        }

        /// <summary>
        /// Returns ellipsoid terrain altitude from the selected gameplay provider. The prototype
        /// fallback is only a synchronous current-renderer-collider query, not a terrain guarantee.
        /// </summary>
        public double GetGroundHeight(GeoPosition geographicPosition)
        {
            double height;
            if (!TryGetGroundHeight(geographicPosition, out height))
            {
                throw new InvalidOperationException(
                    "Ground height is unavailable because no approved gameplay terrain sample (or enabled prototype ground probe) is available.");
            }
            return height;
        }

        public bool TryGetGroundHeight(GeoPosition geographicPosition, out double altitudeMeters)
        {
            Vector3 ignoredNormal;
            return TryGetGroundSurface(geographicPosition, out altitudeMeters, out ignoredNormal);
        }

        /// <summary>
        /// Returns the selected gameplay terrain height and normal in the session's local
        /// east/up/north basis. Missing provider data is reported rather than treated as flat ground;
        /// a renderer collider is queried only by an explicitly enabled prototype fallback.
        /// </summary>
        public bool TryGetGroundSurface(
            GeoPosition geographicPosition,
            out double altitudeMeters,
            out Vector3 normalEastUpNorth)
        {
            if (gameplayCollisionWorld != null)
            {
                return gameplayCollisionWorld.TryGetGroundSurface(
                    geographicPosition,
                    out altitudeMeters,
                    out normalEastUpNorth);
            }
            return TryGetVisualGroundSurface(geographicPosition, out altitudeMeters, out normalEastUpNorth);
        }

        /// <summary>
        /// Prototype-only Cesium/Google visual-tile collider probe. Gameplay queries should call
        /// TryGetGroundSurface, which selects the independent gameplay provider and only reaches this
        /// method when the scene has explicitly enabled its prototype fallback.
        /// </summary>
        public bool TryGetVisualGroundSurface(
            GeoPosition geographicPosition,
            out double altitudeMeters,
            out Vector3 normalEastUpNorth)
        {
            altitudeMeters = 0.0;
            normalEastUpNorth = Vector3.up;
            if (!geographicPosition.IsValid || georeference == null)
            {
                return false;
            }

            RaycastHit hit;
            if (!TryFindCesiumGround(geographicPosition, out hit))
            {
                return false;
            }

            GeoPosition groundPosition = UnityWorldToGeographic(hit.point);
            altitudeMeters = groundPosition.AltitudeMeters;
            Vector3 up = GetGeodeticDirection(groundPosition, true);
            Vector3 north = GetNorthDirection(groundPosition);
            Vector3 east = Vector3.Cross(up, north).normalized;
            Vector3 worldNormal = hit.normal.normalized;
            normalEastUpNorth = new Vector3(
                Vector3.Dot(worldNormal, east),
                Vector3.Dot(worldNormal, up),
                Vector3.Dot(worldNormal, north));
            if (normalEastUpNorth.y < 0f)
            {
                normalEastUpNorth = -normalEastUpNorth;
            }
            if (normalEastUpNorth.sqrMagnitude > 0.25f)
            {
                normalEastUpNorth.Normalize();
            }
            else
            {
                normalEastUpNorth = Vector3.up;
            }

            return GeoPosition.IsFinite(altitudeMeters);
        }

        /// <summary>
        /// Legacy prototype-only test against currently loaded physics colliders. Competitive
        /// gameplay should use GameplayCollisionWorld's stable proxy/semantic capsule query.
        /// </summary>
        public bool CanFitPlayerCapsule(
            GeoPosition footPosition,
            float radiusMeters,
            float currentHeightMeters,
            float targetHeightMeters)
        {
            if (!footPosition.IsValid || targetHeightMeters <= currentHeightMeters)
            {
                return true;
            }
            if (georeference == null || targetHeightMeters <= radiusMeters * 2f)
            {
                return false;
            }

            float radius = Mathf.Max(0.05f, radiusMeters * 0.92f);
            Vector3 up = GetGeodeticDirection(footPosition, true);
            Vector3 footWorld = GeographicToUnityWorld(footPosition);
            Vector3 bottom = footWorld + up * (radiusMeters + 0.05f);
            Vector3 top = footWorld + up * (targetHeightMeters - radius - 0.02f);
            Collider[] overlaps = Physics.OverlapCapsule(
                bottom,
                top,
                radius,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlaps.Length; i++)
            {
                Collider overlap = overlaps[i];
                if (overlap == null || overlap == characterController ||
                    (testCharacterRoot != null &&
                     (overlap.transform == testCharacterRoot || overlap.transform.IsChildOf(testCharacterRoot))))
                {
                    continue;
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// Straight-line 3D distance between two WGS84 positions, in real metres. Arena membership
        /// uses a separate horizontal ENU distance to the selected centre.
        /// </summary>
        public double DistanceBetweenPlayers(GeoPosition first, GeoPosition second)
        {
            EnsureArenaFrame();
            if (!first.IsValid || !second.IsValid)
            {
                throw new ArgumentException("Both player positions must be valid WGS84 coordinates.");
            }

            return math.length(ToEarthCenteredEarthFixed(first) - ToEarthCenteredEarthFixed(second));
        }

        /// <summary>Horizontal distance from the arena centre, ignoring vertical displacement.</summary>
        public double GetDistanceFromArenaCentre(GeoPosition position)
        {
            LocalPosition local = GeographicToLocal(position);
            return Math.Sqrt(
                (local.EastMeters * local.EastMeters) +
                (local.NorthMeters * local.NorthMeters));
        }

        public double GetDistanceFromArenaCentre(LocalPosition position)
        {
            if (!position.IsFinite)
            {
                throw new ArgumentException("A finite ENU local position is required.", "position");
            }

            return Math.Sqrt(
                (position.EastMeters * position.EastMeters) +
                (position.NorthMeters * position.NorthMeters));
        }

        public double GetDistanceFromArenaCentre()
        {
            if (!HasCurrentPlayerGeographicPosition)
            {
                return double.NaN;
            }
            return GetDistanceFromArenaCentre(CurrentPlayerGeographicPosition);
        }

        /// <summary>
        /// General WGS84 arena-membership query. Phase 4's movement authority uses the equivalent
        /// metre-based circular clamp directly on its fixed ENU snapshot, never a client-submitted pose.
        /// </summary>
        public bool IsInsideArena(GeoPosition position)
        {
            if (!position.IsValid || !IsArenaConfigured)
            {
                return false;
            }
            return GetDistanceFromArenaCentre(position) <= gameplayRadiusMeters + BoundaryEpsilonMeters;
        }

        public bool IsInsideArena(LocalPosition position)
        {
            if (!position.IsFinite || !IsArenaConfigured)
            {
                return false;
            }
            return GetDistanceFromArenaCentre(position) <= gameplayRadiusMeters + BoundaryEpsilonMeters;
        }

        /// <summary>
        /// Local prototype guard called after character movement. It clamps the capsule centre just
        /// inside the gameplay radius; it is not server enforcement and does not rely on the drawn ring.
        /// </summary>
        public bool EnforceLocalArenaBoundary()
        {
            if (!IsReady || !IsArenaConfigured || testCharacterRoot == null || testCharacterGlobeAnchor == null)
            {
                return false;
            }

            GeoPosition playerPosition;
            if (!TryGetPlayerGeographicPosition(out playerPosition))
            {
                return false;
            }

            LocalPosition currentLocal = GeographicToLocal(playerPosition);
            double distance = GetDistanceFromArenaCentre(currentLocal);
            double inset = characterController != null ? Math.Max(0.0, characterController.radius) : 0.0;
            double allowedRadius = Math.Max(0.0, gameplayRadiusMeters - inset);
            if (distance <= allowedRadius + BoundaryEpsilonMeters)
            {
                return false;
            }

            double scale = distance > 0.0 ? allowedRadius / distance : 0.0;
            LocalPosition constrainedLocal = new LocalPosition(
                currentLocal.EastMeters * scale,
                currentLocal.UpMeters,
                currentLocal.NorthMeters * scale);
            GeoPosition constrainedGeographic = LocalToGeographic(constrainedLocal);
            Vector3 constrainedWorldPosition = GeographicToUnityWorld(constrainedGeographic);

            bool controllerWasEnabled = characterController != null && characterController.enabled;
            if (controllerWasEnabled)
            {
                characterController.enabled = false;
            }
            testCharacterRoot.position = constrainedWorldPosition;
            if (controllerWasEnabled)
            {
                characterController.enabled = true;
            }

            Physics.SyncTransforms();
            testCharacterGlobeAnchor.Sync();
            lastBoundaryCorrectionTime = Time.unscaledTime;
            return true;
        }

        public bool TryGetPlayerGeographicPosition(out GeoPosition position)
        {
            position = default(GeoPosition);
            if (testCharacterGlobeAnchor == null || !IsReady || !testCharacterGlobeAnchor.isActiveAndEnabled)
            {
                return false;
            }

            // Cesium's longitudeLatitudeHeight vector order is longitude, latitude, ellipsoid altitude.
            double3 longitudeLatitudeHeight = testCharacterGlobeAnchor.longitudeLatitudeHeight;
            position = new GeoPosition(
                longitudeLatitudeHeight.y,
                longitudeLatitudeHeight.x,
                longitudeLatitudeHeight.z);
            return position.IsValid;
        }

        private bool TryFindCesiumGround(GeoPosition geographicPosition, out RaycastHit nearestHit)
        {
            nearestHit = default(RaycastHit);
            if (!worldOriginFrameReady || georeference == null || photorealisticTileset == null || settings == null ||
                !geographicPosition.IsValid || !photorealisticTileset.enabled)
            {
                return false;
            }

            GeoPosition rayStartPosition = new GeoPosition(
                geographicPosition.LatitudeDegrees,
                geographicPosition.LongitudeDegrees,
                settings.GroundProbeStartAboveEllipsoidMeters);
            Vector3 rayStart = GeographicToUnityWorld(rayStartPosition);
            double3 rayStartEcef = ToEarthCenteredEarthFixed(rayStartPosition);
            double3 ecefUp = georeference.ellipsoid.GeodeticSurfaceNormal(rayStartEcef);
            double3 parentFrameUp = georeference.TransformEarthCenteredEarthFixedDirectionToUnity(ecefUp);
            Vector3 worldUp = georeference.transform.TransformDirection(ToVector3(parentFrameUp)).normalized;
            if (worldUp.sqrMagnitude < 0.5f)
            {
                return false;
            }

            RaycastHit[] hits = Physics.RaycastAll(
                rayStart,
                -worldUp,
                settings.GroundProbeDistanceMeters,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || !BelongsToTileset(collider.transform))
                {
                    continue;
                }

                if (hits[i].distance < nearestDistance)
                {
                    nearestDistance = hits[i].distance;
                    nearestHit = hits[i];
                    found = true;
                }
            }

            return found;
        }

        private bool BelongsToTileset(Transform candidate)
        {
            return photorealisticTileset != null &&
                   (candidate == photorealisticTileset.transform ||
                    candidate.IsChildOf(photorealisticTileset.transform));
        }

        private void PlaceCharacterOnGround(GeoPosition groundPosition)
        {
            Vector3 up = GetGeodeticDirection(groundPosition, true);
            Vector3 north = GetNorthDirection(groundPosition);
            float clearance = Mathf.Max(
                settings.GroundSpawnClearanceMeters,
                characterController.skinWidth + 0.02f);

            characterController.enabled = false;
            testCharacterGlobeAnchor.longitudeLatitudeHeight = new double3(
                groundPosition.LongitudeDegrees,
                groundPosition.LatitudeDegrees,
                groundPosition.AltitudeMeters + clearance);
            testCharacterGlobeAnchor.Sync();
            characterMotor.ResetForGroundSpawn();
            testCharacterRoot.rotation = Quaternion.LookRotation(north, up);
            testCharacterGlobeAnchor.Sync();
            characterController.enabled = !characterMotor.IsNetworkControlled;
            characterMotor.SetInputEnabled(false);

            state = GeospatialWorldState.Ready;
            statusMessage = gameplayCollisionWorld != null && gameplayCollisionWorld.HasIndependentData
                ? "Independent gameplay terrain is available. The virtual arena is ready; real-world travel is not required."
                : "Prototype ground sample found. Semantic hazards are not verified; production play is blocked without independent gameplay data.";
            Action ready = WorldBecameReady;
            if (ready != null)
            {
                ready.Invoke();
            }
        }

        private Vector3 GetGeodeticDirection(GeoPosition position, bool up)
        {
            double latitudeRadians = position.LatitudeDegrees * (Math.PI / 180.0);
            double longitudeRadians = position.LongitudeDegrees * (Math.PI / 180.0);
            double sinLatitude = Math.Sin(latitudeRadians);
            double cosLatitude = Math.Cos(latitudeRadians);
            double sinLongitude = Math.Sin(longitudeRadians);
            double cosLongitude = Math.Cos(longitudeRadians);
            double3 ecefDirection = up
                ? new double3(cosLatitude * cosLongitude, cosLatitude * sinLongitude, sinLatitude)
                : new double3(-sinLatitude * cosLongitude, -sinLatitude * sinLongitude, cosLatitude);
            double3 parentFrameDirection = georeference.TransformEarthCenteredEarthFixedDirectionToUnity(ecefDirection);
            return georeference.transform.TransformDirection(ToVector3(parentFrameDirection)).normalized;
        }

        private Vector3 GetNorthDirection(GeoPosition position)
        {
            return GetGeodeticDirection(position, false);
        }

        private double3 ToEarthCenteredEarthFixed(GeoPosition position)
        {
            return georeference.ellipsoid.LongitudeLatitudeHeightToCenteredFixed(
                new double3(
                    position.LongitudeDegrees,
                    position.LatitudeDegrees,
                    position.AltitudeMeters));
        }

        private void ApplyRenderEnvelope()
        {
            if (arenaCamera == null)
            {
                arenaCamera = Camera.main;
            }
            if (arenaCamera == null || !IsArenaConfigured)
            {
                return;
            }

            // Cesium streams according to the camera frustum, not a hard circular cache.
            double farPlane = Math.Max(1200.0, RenderRadiusMeters * 2.0);
            arenaCamera.farClipPlane = (float)Math.Min(farPlane, 100000.0);
        }

        private void EnsureArenaFrame()
        {
            if (!worldOriginFrameReady || georeference == null)
            {
                throw new InvalidOperationException("Configure an arena origin before using geospatial conversions.");
            }
        }

        private static Vector3 ToVector3(double3 value)
        {
            return new Vector3((float)value.x, (float)value.y, (float)value.z);
        }

        private bool Fail(string message)
        {
            state = GeospatialWorldState.Failed;
            statusMessage = message;
            if (characterController != null)
            {
                characterController.enabled = false;
            }
            if (characterMotor != null)
            {
                characterMotor.SetInputEnabled(false);
            }
            return false;
        }
    }
}
