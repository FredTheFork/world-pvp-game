using UnityEngine;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Configuration
{
    /// <summary>
    /// Tunable values for the Cesium world, arena, and host-simulated Phase 5 locomotion.
    /// No credentials belong here. Unity world units and gameplay distances are metres.
    /// </summary>
    [CreateAssetMenu(menuName = "World PvP/Phase 2/World Settings", fileName = "PhaseOneWorldSettings")]
    public sealed class PhaseOneWorldSettings : ScriptableObject
    {
        [Header("Default geographic origin (WGS84)")]
        [SerializeField] private double defaultLatitudeDegrees = 51.5073;
        [SerializeField] private double defaultLongitudeDegrees = -0.1657;
        [Tooltip("Cesium height is metres above the WGS84 ellipsoid, not terrain or mean sea level.")]
        [SerializeField] private double defaultOriginHeightEllipsoidMeters = 0.0;

        [Header("Arena dimensions (real metres)")]
        [Min(1f)] [SerializeField] private float defaultGameplayRadiusMeters = 500f;
        [Min(0f)] [SerializeField] private float visibilityBufferMeters = 100f;
        [Min(0f)] [SerializeField] private float preloadBufferMeters = 150f;
        [Min(0f)] [SerializeField] private float boundaryWarningDistanceMeters = 25f;
        [Tooltip("Cesium moves its local frame after the anchored player exceeds this distance from the current origin.")]
        [Min(100f)] [SerializeField] private float originShiftThresholdMeters = 750f;

        [Header("Prototype fallback only — visual-tile ground probe (not production gameplay data)")]
        [Tooltip("Initial visual-prototype height while the explicitly enabled fallback waits for a ground sample.")]
        [Min(1f)] [SerializeField] private float initialSpawnHeightAboveOriginMeters = 450f;
        [Tooltip("Only used by the explicitly labelled prototype fallback; never a production terrain provider.")]
        [Min(1000f)] [SerializeField] private float groundProbeStartAboveEllipsoidMeters = 16000f;
        [Tooltip("Only used by the explicitly labelled prototype fallback; visual tile colliders are not stable gameplay collision data.")]
        [Min(1000f)] [SerializeField] private float groundProbeDistanceMeters = 30000f;
        [Min(0.01f)] [SerializeField] private float groundSpawnClearanceMeters = 0.12f;
        [Min(0.05f)] [SerializeField] private float groundProbeIntervalSeconds = 0.5f;
        [Min(5f)] [SerializeField] private float groundProbeTimeoutSeconds = 120f;

        [Header("Character movement (host-authoritative, metres / seconds)")]
        [Min(0.1f)] [SerializeField] private float walkSpeedMetersPerSecond = 4.5f;
        [Min(0.1f)] [SerializeField] private float sprintSpeedMetersPerSecond = 7.0f;
        [Min(0.1f)] [SerializeField] private float crouchSpeedMetersPerSecond = 2.2f;
        [Min(0.1f)] [SerializeField] private float jumpHeightMeters = 1.2f;
        [Min(0.1f)] [SerializeField] private float gravityMetersPerSecondSquared = 22f;
        [Min(0.1f)] [SerializeField] private float groundAccelerationMetersPerSecondSquared = 24f;
        [Min(0.1f)] [SerializeField] private float groundDecelerationMetersPerSecondSquared = 30f;
        [Min(0.1f)] [SerializeField] private float airAccelerationMetersPerSecondSquared = 7f;
        [Min(0.1f)] [SerializeField] private float airDecelerationMetersPerSecondSquared = 3f;
        [Min(0.1f)] [SerializeField] private float characterHeightMeters = 1.8f;
        [Min(0.1f)] [SerializeField] private float crouchHeightMeters = 1.15f;
        [Min(0.05f)] [SerializeField] private float characterRadiusMeters = 0.30f;
        [Min(0.01f)] [SerializeField] private float characterSkinWidthMeters = 0.08f;
        [Min(0.05f)] [SerializeField] private float stepOffsetMeters = 0.30f;
        [Range(1f, 89f)] [SerializeField] private float maximumSlopeDegrees = 48f;
        [Min(0.01f)] [SerializeField] private float groundSnapDistanceMeters = 0.25f;
        [Min(0.01f)] [SerializeField] private float terrainSampleIntervalSeconds = 0.03333334f;
        [Min(0f)] [SerializeField] private float landingPresentationDurationSeconds = 0.18f;

        [Header("First-person view / presentation")]
        [Min(0.1f)] [SerializeField] private float firstPersonEyeHeightMeters = 1.62f;
        [Min(0.1f)] [SerializeField] private float crouchEyeHeightMeters = 1.00f;
        [Range(50f, 110f)] [SerializeField] private float firstPersonFieldOfViewDegrees = 78f;
        [Min(0.001f)] [SerializeField] private float mouseSensitivityDegreesPerPixel = 0.12f;
        [Range(-75f, 75f)] [SerializeField] private float initialCameraPitchDegrees = 0f;
        [Min(0f)] [SerializeField] private float walkingCameraBobMeters = 0.025f;
        [Min(0f)] [SerializeField] private float runningCameraBobMeters = 0.045f;
        [Min(0f)] [SerializeField] private float crouchingCameraBobMeters = 0.012f;
        [Min(0.1f)] [SerializeField] private float cameraBobFrequencyHertz = 2.0f;

        [Header("Minimap")]
        [Min(10f)] [SerializeField] private float minimapMinimumRangeMeters = 150f;

        [Header("Phase 6 — Cesium hierarchical streaming (desktop Web starting profile)")]
        [Tooltip("Cesium refines the hierarchy by projected screen-space error. This is not a metre-radius tile cutoff.")]
        [Range(1f, 64f)] [SerializeField] private float maximumScreenSpaceError = 16f;
        [Tooltip("Cesium tile cache target (16–2048 MiB). Required-for-rendering tiles may keep total use above this soft cap.")]
        [Range(16f, 2048f)] [SerializeField] private float maximumTileCacheMiB = 256f;
        [Range(1, 16)] [SerializeField] private int maximumSimultaneousTileLoads = 4;
        [Range(1, 64)] [SerializeField] private int loadingDescendantLimit = 16;
        [SerializeField] private bool preloadAncestors = true;
        [SerializeField] private bool preloadSiblings;

        [Header("Phase 6 — configurable distance bands for benchmark/telemetry")]
        [Tooltip("Initial near-detail measurement band. Cesium does not guarantee an exact tile LOD ring at this distance.")]
        [Min(1f)] [SerializeField] private float veryHighDetailDistanceMeters = 100f;
        [Tooltip("Initial high-detail measurement band boundary; benchmark against real Cesium tiles.")]
        [Min(1f)] [SerializeField] private float highDetailDistanceMeters = 300f;
        [Tooltip("Initial medium-detail measurement band boundary; farther geometry is the low/horizon band.")]
        [Min(1f)] [SerializeField] private float mediumDetailDistanceMeters = 750f;

        [Header("Phase 6 — movement-aware, bounded forward preloading")]
        [SerializeField] private bool movementPredictionEnabled = true;
        [Min(0.1f)] [SerializeField] private float predictionMinimumSpeedMetersPerSecond = 1.0f;
        [Range(0.25f, 5f)] [SerializeField] private float predictionLeadSeconds = 2.0f;
        [Range(5f, 60f)] [SerializeField] private float predictionMaximumAheadMeters = 24f;
        [Range(30f, 250f)] [SerializeField] private float predictionCameraFarMeters = 150f;
        [Range(35f, 90f)] [SerializeField] private float predictionCameraFieldOfViewDegrees = 55f;
        [Range(0.1f, 2f)] [SerializeField] private float predictionUpdateIntervalSeconds = 0.5f;

        [Header("Phase 6 — explicit desktop-Web performance budgets (benchmark targets)")]
        [Range(30, 120)] [SerializeField] private int targetFramesPerSecond = 60;
        [Range(8f, 50f)] [SerializeField] private float p95FrameTimeBudgetMilliseconds = 16.7f;
        [Min(256f)] [SerializeField] private float unityReservedMemoryBudgetMiB = 1024f;
        [Min(128f)] [SerializeField] private float gpuMemoryBudgetMiB = 512f;
        [Min(0.5f)] [SerializeField] private float sustainedMapBandwidthBudgetMbps = 5f;
        [Min(100000f)] [SerializeField] private int visibleCesiumTriangleBudget = 1500000;
        [Tooltip("Telemetry warning only. Cesium's native request path cannot be hard-stopped by this client-side counter.")]
        [Min(1)] [SerializeField] private int mapRequestWarningPerMatch = 1000;
        [Min(0.1f)] [SerializeField] private float performanceSampleIntervalSeconds = 1f;

        [Header("Phase 6 — indicative Google Maps billing inputs (verify before release)")]
        [Min(0f)] [SerializeField] private float estimatedMapPriceUsdPerThousandEvents = 6f;
        [Min(0)] [SerializeField] private int estimatedMonthlyFreeMapEvents = 1000;

        [Header("Phase 7 — combat rules")]
        [Range(1, 1000)] [SerializeField] private int maximumPlayerHealth = 100;

        public GeoPosition DefaultOrigin
        {
            get
            {
                return new GeoPosition(
                    defaultLatitudeDegrees,
                    defaultLongitudeDegrees,
                    defaultOriginHeightEllipsoidMeters);
            }
        }

        public float DefaultGameplayRadiusMeters { get { return defaultGameplayRadiusMeters; } }
        public float VisibilityBufferMeters { get { return visibilityBufferMeters; } }
        public float PreloadBufferMeters { get { return preloadBufferMeters; } }
        public float BoundaryWarningDistanceMeters { get { return boundaryWarningDistanceMeters; } }
        public float OriginShiftThresholdMeters { get { return originShiftThresholdMeters; } }
        public float InitialSpawnHeightAboveOriginMeters { get { return initialSpawnHeightAboveOriginMeters; } }
        public float GroundProbeStartAboveEllipsoidMeters { get { return groundProbeStartAboveEllipsoidMeters; } }
        public float GroundProbeDistanceMeters { get { return groundProbeDistanceMeters; } }
        public float GroundSpawnClearanceMeters { get { return groundSpawnClearanceMeters; } }
        public float GroundProbeIntervalSeconds { get { return groundProbeIntervalSeconds; } }
        public float GroundProbeTimeoutSeconds { get { return groundProbeTimeoutSeconds; } }
        public float WalkSpeedMetersPerSecond { get { return walkSpeedMetersPerSecond; } }
        public float SprintSpeedMetersPerSecond { get { return sprintSpeedMetersPerSecond; } }
        public float CrouchSpeedMetersPerSecond { get { return crouchSpeedMetersPerSecond; } }
        public float JumpHeightMeters { get { return jumpHeightMeters; } }
        public float GravityMetersPerSecondSquared { get { return gravityMetersPerSecondSquared; } }
        public float GroundAccelerationMetersPerSecondSquared { get { return groundAccelerationMetersPerSecondSquared; } }
        public float GroundDecelerationMetersPerSecondSquared { get { return groundDecelerationMetersPerSecondSquared; } }
        public float AirAccelerationMetersPerSecondSquared { get { return airAccelerationMetersPerSecondSquared; } }
        public float AirDecelerationMetersPerSecondSquared { get { return airDecelerationMetersPerSecondSquared; } }
        public float CharacterHeightMeters { get { return Mathf.Max(characterHeightMeters, characterRadiusMeters * 2f); } }
        public float CrouchHeightMeters { get { return Mathf.Max(crouchHeightMeters, characterRadiusMeters * 2f); } }
        public float CharacterRadiusMeters { get { return characterRadiusMeters; } }
        public float CharacterSkinWidthMeters { get { return characterSkinWidthMeters; } }
        public float StepOffsetMeters { get { return Mathf.Min(stepOffsetMeters, CharacterHeightMeters); } }
        public float MaximumSlopeDegrees { get { return maximumSlopeDegrees; } }
        public float GroundSnapDistanceMeters { get { return groundSnapDistanceMeters; } }
        public float TerrainSampleIntervalSeconds { get { return terrainSampleIntervalSeconds; } }
        public float LandingPresentationDurationSeconds { get { return landingPresentationDurationSeconds; } }
        public float FirstPersonEyeHeightMeters { get { return Mathf.Min(firstPersonEyeHeightMeters, CharacterHeightMeters); } }
        public float CrouchEyeHeightMeters { get { return Mathf.Min(crouchEyeHeightMeters, CrouchHeightMeters); } }
        public float FirstPersonFieldOfViewDegrees { get { return firstPersonFieldOfViewDegrees; } }
        public float MouseSensitivityDegreesPerPixel { get { return mouseSensitivityDegreesPerPixel; } }
        public float InitialCameraPitchDegrees { get { return initialCameraPitchDegrees; } }
        public float WalkingCameraBobMeters { get { return walkingCameraBobMeters; } }
        public float RunningCameraBobMeters { get { return runningCameraBobMeters; } }
        public float CrouchingCameraBobMeters { get { return crouchingCameraBobMeters; } }
        public float CameraBobFrequencyHertz { get { return cameraBobFrequencyHertz; } }
        public float MinimapMinimumRangeMeters { get { return minimapMinimumRangeMeters; } }

        public float MaximumScreenSpaceError { get { return Mathf.Clamp(maximumScreenSpaceError, 1f, 64f); } }
        public long MaximumTileCacheBytes
        {
            get { return (long)(Mathf.Clamp(maximumTileCacheMiB, 16f, 2048f) * 1024f * 1024f); }
        }
        public uint MaximumSimultaneousTileLoads { get { return (uint)Mathf.Clamp(maximumSimultaneousTileLoads, 1, 16); } }
        public uint LoadingDescendantLimit { get { return (uint)Mathf.Clamp(loadingDescendantLimit, 1, 64); } }
        public bool PreloadAncestors { get { return preloadAncestors; } }
        public bool PreloadSiblings { get { return preloadSiblings; } }
        public float VeryHighDetailDistanceMeters { get { return Mathf.Max(1f, veryHighDetailDistanceMeters); } }
        public float HighDetailDistanceMeters
        {
            get { return Mathf.Max(VeryHighDetailDistanceMeters, highDetailDistanceMeters); }
        }
        public float MediumDetailDistanceMeters
        {
            get { return Mathf.Max(HighDetailDistanceMeters, mediumDetailDistanceMeters); }
        }
        public bool MovementPredictionEnabled { get { return movementPredictionEnabled; } }
        public float PredictionMinimumSpeedMetersPerSecond { get { return Mathf.Max(0.1f, predictionMinimumSpeedMetersPerSecond); } }
        public float PredictionLeadSeconds { get { return Mathf.Clamp(predictionLeadSeconds, 0.25f, 5f); } }
        public float PredictionMaximumAheadMeters { get { return Mathf.Clamp(predictionMaximumAheadMeters, 5f, 60f); } }
        public float PredictionCameraFarMeters { get { return Mathf.Clamp(predictionCameraFarMeters, 30f, 250f); } }
        public float PredictionCameraFieldOfViewDegrees { get { return Mathf.Clamp(predictionCameraFieldOfViewDegrees, 35f, 90f); } }
        public float PredictionUpdateIntervalSeconds { get { return Mathf.Clamp(predictionUpdateIntervalSeconds, 0.1f, 2f); } }
        public int TargetFramesPerSecond { get { return Mathf.Clamp(targetFramesPerSecond, 30, 120); } }
        public float P95FrameTimeBudgetMilliseconds { get { return Mathf.Clamp(p95FrameTimeBudgetMilliseconds, 8f, 50f); } }
        public float UnityReservedMemoryBudgetMiB { get { return Mathf.Max(256f, unityReservedMemoryBudgetMiB); } }
        public float GpuMemoryBudgetMiB { get { return Mathf.Max(128f, gpuMemoryBudgetMiB); } }
        public float SustainedMapBandwidthBudgetMbps { get { return Mathf.Max(0.5f, sustainedMapBandwidthBudgetMbps); } }
        public int VisibleCesiumTriangleBudget { get { return Mathf.Max(100000, visibleCesiumTriangleBudget); } }
        public int MapRequestWarningPerMatch { get { return Mathf.Max(1, mapRequestWarningPerMatch); } }
        public float PerformanceSampleIntervalSeconds { get { return Mathf.Clamp(performanceSampleIntervalSeconds, 0.25f, 5f); } }
        public float EstimatedMapPriceUsdPerThousandEvents { get { return Mathf.Max(0f, estimatedMapPriceUsdPerThousandEvents); } }
        public int EstimatedMonthlyFreeMapEvents { get { return Mathf.Max(0, estimatedMonthlyFreeMapEvents); } }
        public ushort MaximumPlayerHealth { get { return (ushort)Mathf.Clamp(maximumPlayerHealth, 1, ushort.MaxValue); } }
    }
}
