using System;
using System.Collections.Generic;
using UnityEngine;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Gameplay
{
    /// <summary>
    /// Stable gameplay query layer made from separately sourced semantic terrain and simplified
    /// collision proxies. It intentionally has no dependency on Cesium/Google render tile meshes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayCollisionWorld : MonoBehaviour
    {
        private const int MaximumCollisionProxyCount = 20000;
        private const float MinimumAreaQueryRadiusMeters = 0.25f;
        private const float CollisionSweepStepMeters = 0.25f;
        private const int CollisionSweepRefinementSteps = 10;
        private const double ProxySpatialCellSizeMeters = 25.0;
        private const int MaximumProxyCellsPerFeature = 256;
        private const string PrototypeWarning =
            "[Gameplay data] PROTOTYPE FALLBACK: this arena may use transient photorealistic-tile colliders for legacy ground/simple ray queries only. " +
            "Water, roads, buildings, cliffs, restricted areas, and reliable safe-spawn avoidance are NOT verified. " +
            "Do not use this mode as a production safety or collision system.";

        [Header("Independent gameplay data — keep separate from rendering")]
        [SerializeField] private WorldGameplayDataSource gameplayDataSource;
        [SerializeField] private bool allowPrototypeVisualTileFallback;
        [SerializeField, Min(0f)] private float maximumAcceptedGroundSlopeDegrees = 48f;
        [Range(0f, 1f), SerializeField] private float minimumTerrainConfidence = 0.5f;
        [SerializeField, Min(0.25f)] private float areaAssessmentRadiusMeters = 1.0f;

        private readonly List<GameplayCollisionProxyDefinition> collectedDefinitions =
            new List<GameplayCollisionProxyDefinition>();
        private readonly List<RuntimeProxy> runtimeProxies = new List<RuntimeProxy>();
        private readonly Dictionary<long, List<int>> proxiesBySpatialCell =
            new Dictionary<long, List<int>>();
        private readonly List<int> largeProxyIndices = new List<int>();
        private readonly List<int> proxyCandidateScratch = new List<int>(64);
        private readonly HashSet<int> proxyCandidateSet = new HashSet<int>();
        private GeospatialWorldManager worldManager;
        private PhaseOneWorldSettings settings;
        private GeoPosition arenaCentre;
        private double arenaRadiusMeters;
        private bool arenaConfigured;
        private bool coverageVerified;
        private bool prototypeFallbackCurrentArena;
        private string configurationError = "Gameplay collision data has not been configured for an arena.";

        private struct RuntimeProxy
        {
            public string FeatureId;
            public GameplayFeatureKind FeatureKind;
            public LocalPosition Centre;
            public double HalfEastMeters;
            public double HalfUpMeters;
            public double HalfNorthMeters;
            public double YawRadians;
            public bool BlocksMovement;
            public bool BlocksProjectiles;
        }

        public WorldGameplayDataSource DataSource { get { return gameplayDataSource; } }
        public bool HasIndependentData
        {
            get { return arenaConfigured && coverageVerified && gameplayDataSource != null && gameplayDataSource.LegalReviewApproved; }
        }
        public bool IsPrototypeFallback
        {
            get { return arenaConfigured && !HasIndependentData && prototypeFallbackCurrentArena; }
        }
        public bool WillUsePrototypeVisualFallback
        {
            get { return gameplayDataSource == null && allowPrototypeVisualTileFallback; }
        }
        public string ConfigurationError { get { return configurationError; } }
        public int CollisionProxyCount { get { return runtimeProxies.Count; } }
        public GeoPosition ArenaCentre { get { return arenaCentre; } }
        public double ArenaRadiusMeters { get { return arenaRadiusMeters; } }

        public void ConfigureReferences(
            GeospatialWorldManager manager,
            PhaseOneWorldSettings worldSettings,
            WorldGameplayDataSource dataSource = null,
            bool enablePrototypeVisualTileFallback = false)
        {
            worldManager = manager;
            settings = worldSettings;
            if (dataSource != null)
            {
                gameplayDataSource = dataSource;
            }
            allowPrototypeVisualTileFallback = enablePrototypeVisualTileFallback;
        }

        private void Awake()
        {
            if (worldManager == null)
            {
                worldManager = FindObjectOfType<GeospatialWorldManager>();
            }
            if (settings == null && worldManager != null)
            {
                settings = worldManager.WorldSettings;
            }
        }

        /// <summary>
        /// Prepares the independently sourced gameplay world for one exact arena. If a source is
        /// configured but lacks legal approval or coverage, preparation fails; it never silently
        /// substitutes renderer-derived gameplay features.
        /// </summary>
        public bool ConfigureArena(GeoPosition centre, double radiusMeters, out string error)
        {
            return ConfigureArena(centre, radiusMeters, false, out error);
        }

        public bool ConfigureArena(
            GeoPosition centre,
            double radiusMeters,
            bool requireIndependentData,
            out string error)
        {
            error = string.Empty;
            arenaConfigured = false;
            coverageVerified = false;
            prototypeFallbackCurrentArena = false;
            runtimeProxies.Clear();
            proxiesBySpatialCell.Clear();
            largeProxyIndices.Clear();
            proxyCandidateScratch.Clear();
            proxyCandidateSet.Clear();
            collectedDefinitions.Clear();

            if (worldManager == null || !centre.IsValid || !GeoPosition.IsFinite(radiusMeters) || radiusMeters <= 0.0)
            {
                configurationError = "A valid geospatial manager, centre, and positive radius are required.";
                error = configurationError;
                return false;
            }
            if (!worldManager.IsArenaConfigured || !worldManager.ArenaCentre.Equals(centre) ||
                Math.Abs(worldManager.GameplayRadiusMeters - radiusMeters) > 0.01)
            {
                configurationError = "Configure the exact arena centre and radius on the geospatial manager before gameplay data.";
                error = configurationError;
                return false;
            }

            arenaCentre = centre;
            arenaRadiusMeters = radiusMeters;
            if (gameplayDataSource == null)
            {
                if (requireIndependentData || !allowPrototypeVisualTileFallback)
                {
                    configurationError = "No independent gameplay terrain/feature data source is assigned. Production arena setup is blocked.";
                    error = configurationError;
                    return false;
                }
                arenaConfigured = true;
                prototypeFallbackCurrentArena = true;
                configurationError = PrototypeWarning;
                Debug.LogWarning(PrototypeWarning, this);
                return true;
            }

            if (gameplayDataSource.DerivedFromGoogleMapsContent)
            {
                configurationError = "Gameplay terrain, features, or collision proxies derived from Google Maps Content are not allowed.";
                error = configurationError;
                return false;
            }
            if (gameplayDataSource.CombinedWithGoogleMapsContent && !gameplayDataSource.LegalReviewApproved)
            {
                configurationError = "Combining gameplay data with Google map content requires explicit legal review before arena setup.";
                error = configurationError;
                return false;
            }
            if (!gameplayDataSource.LegalReviewApproved ||
                string.IsNullOrWhiteSpace(gameplayDataSource.ProviderId) ||
                string.IsNullOrWhiteSpace(gameplayDataSource.LicenseAndTermsReference) ||
                string.IsNullOrWhiteSpace(gameplayDataSource.RequiredAttribution))
            {
                configurationError = "Gameplay data source is not ready: provider identity, licence/terms reference, required attribution, and explicit legal review approval are required.";
                error = configurationError;
                return false;
            }

            string coverageReason;
            try
            {
                if (!gameplayDataSource.HasCoverage(centre, radiusMeters, out coverageReason))
                {
                    configurationError = string.IsNullOrWhiteSpace(coverageReason)
                        ? "The approved gameplay-data provider does not cover the complete arena."
                        : coverageReason;
                    error = configurationError;
                    return false;
                }
                gameplayDataSource.CollectCollisionProxies(centre, radiusMeters, collectedDefinitions);
            }
            catch (Exception exception)
            {
                configurationError = "Gameplay data provider failed while preparing the arena (" + exception.GetType().Name + ").";
                error = configurationError;
                return false;
            }

            if (collectedDefinitions.Count > MaximumCollisionProxyCount)
            {
                configurationError = "Gameplay provider returned too many collision proxies for the configured arena budget.";
                error = configurationError;
                return false;
            }

            double maximumProxyCentreDistance = radiusMeters + 500.0;
            for (int i = 0; i < collectedDefinitions.Count; i++)
            {
                GameplayCollisionProxyDefinition definition = collectedDefinitions[i];
                if (!definition.IsValid ||
                    definition.SizeMeters.x > 100000f || definition.SizeMeters.y > 100000f ||
                    definition.SizeMeters.z > 100000f ||
                    Math.Abs(definition.GeographicCentre.AltitudeMeters) > 100000.0)
                {
                    configurationError = "Gameplay provider returned an invalid or out-of-budget collision proxy at index " + i + ".";
                    error = configurationError;
                    runtimeProxies.Clear();
                    return false;
                }

                LocalPosition localCentre;
                try
                {
                    localCentre = worldManager.GeographicToLocal(definition.GeographicCentre);
                }
                catch (Exception exception)
                {
                    configurationError = "Gameplay collision proxy could not be converted to the arena ENU frame (" + exception.GetType().Name + ").";
                    error = configurationError;
                    runtimeProxies.Clear();
                    return false;
                }

                double centreDistance = Math.Sqrt(
                    localCentre.EastMeters * localCentre.EastMeters +
                    localCentre.NorthMeters * localCentre.NorthMeters);
                if (centreDistance > maximumProxyCentreDistance)
                {
                    // Providers may include nearby blockers just beyond the boundary to prevent
                    // shooting/movement from exploiting an arena edge, but far-away data is ignored.
                    continue;
                }

                runtimeProxies.Add(new RuntimeProxy
                {
                    FeatureId = definition.FeatureId ?? string.Empty,
                    FeatureKind = definition.FeatureKind,
                    Centre = localCentre,
                    HalfEastMeters = definition.SizeMeters.x * 0.5,
                    HalfUpMeters = definition.SizeMeters.y * 0.5,
                    HalfNorthMeters = definition.SizeMeters.z * 0.5,
                    YawRadians = definition.YawDegrees * Mathf.Deg2Rad,
                    BlocksMovement = definition.BlocksMovement,
                    BlocksProjectiles = definition.BlocksProjectiles
                });
            }

            BuildProxySpatialIndex();
            coverageVerified = true;
            arenaConfigured = true;
            configurationError = string.Empty;
            return true;
        }

        /// <summary>Strict readiness gate for production/dedicated matches.</summary>
        public bool IsProductionReady(GeoPosition centre, double radiusMeters, out string error)
        {
            if (!HasIndependentData)
            {
                error = string.IsNullOrWhiteSpace(configurationError)
                    ? "An approved independent gameplay-data source is required; prototype tile fallback is not production ready."
                    : configurationError;
                return false;
            }
            if (!arenaCentre.Equals(centre) || Math.Abs(arenaRadiusMeters - radiusMeters) > 0.01)
            {
                error = "Gameplay data is prepared for a different centre or radius than the match metadata.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        /// <summary>Samples only the approved gameplay provider; never queries a visual tileset.</summary>
        public bool TryGetIndependentGroundSample(
            GeoPosition geographicPosition,
            out GameplayGroundSample sample)
        {
            sample = default(GameplayGroundSample);
            if (!HasIndependentData || !geographicPosition.IsValid)
            {
                return false;
            }
            try
            {
                return gameplayDataSource.TrySampleGround(geographicPosition, out sample) &&
                       sample.IsValid && sample.Confidence >= Mathf.Clamp01(minimumTerrainConfidence);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Gameplay data] Terrain query failed safely (" + exception.GetType().Name + ").", this);
                sample = default(GameplayGroundSample);
                return false;
            }
        }

        public bool TryGetIndependentGroundSurface(
            GeoPosition geographicPosition,
            out double altitudeMeters,
            out Vector3 normalEastUpNorth)
        {
            altitudeMeters = 0.0;
            normalEastUpNorth = Vector3.up;
            GameplayGroundSample sample;
            if (!TryGetIndependentGroundSample(geographicPosition, out sample))
            {
                return false;
            }

            normalEastUpNorth = sample.NormalEastUpNorth.normalized;
            if (normalEastUpNorth.y < 0f)
            {
                normalEastUpNorth = -normalEastUpNorth;
            }
            altitudeMeters = sample.EllipsoidAltitudeMeters;
            return GeoPosition.IsFinite(altitudeMeters) && normalEastUpNorth.y > 0f;
        }

        /// <summary>
        /// Gameplay ground query. Its only legacy path is an explicitly enabled prototype-only
        /// transient visual-tile probe; callers must inspect IsPrototypeFallback before release.
        /// </summary>
        public bool TryGetGroundSurface(
            GeoPosition geographicPosition,
            out double altitudeMeters,
            out Vector3 normalEastUpNorth)
        {
            if (TryGetIndependentGroundSurface(geographicPosition, out altitudeMeters, out normalEastUpNorth))
            {
                return true;
            }

            if (IsPrototypeFallback && worldManager != null)
            {
                return worldManager.TryGetVisualGroundSurface(
                    geographicPosition,
                    out altitudeMeters,
                    out normalEastUpNorth);
            }

            altitudeMeters = 0.0;
            normalEastUpNorth = Vector3.up;
            return false;
        }

        /// <summary>Validates a spawn surface, semantic hazards, slope, arena bounds and blockers.</summary>
        public bool TryGetSafeSpawnFoot(
            LocalPosition horizontalCandidate,
            float characterRadiusMeters,
            float groundClearanceMeters,
            float maximumSlopeDegrees,
            out LocalPosition groundedFootPosition,
            out string rejectionReason)
        {
            groundedFootPosition = default(LocalPosition);
            rejectionReason = string.Empty;
            if (worldManager == null || !horizontalCandidate.IsFinite || !arenaConfigured)
            {
                rejectionReason = "Gameplay arena is not configured.";
                return false;
            }
            if (!HasIndependentData && !IsPrototypeFallback)
            {
                rejectionReason = configurationError;
                return false;
            }

            GeoPosition horizontalPosition = worldManager.LocalToGeographic(horizontalCandidate);
            double groundAltitude;
            Vector3 normal;
            if (HasIndependentData)
            {
                GameplayGroundSample groundSample;
                if (!TryGetIndependentGroundSample(horizontalPosition, out groundSample))
                {
                    rejectionReason = "No approved independent gameplay terrain sample is available here.";
                    return false;
                }
                if (groundSample.SurfaceKind != GameplaySurfaceKind.WalkableLand &&
                    groundSample.SurfaceKind != GameplaySurfaceKind.Park)
                {
                    rejectionReason = "The gameplay terrain classification is unknown or unsafe for a spawn.";
                    return false;
                }
                groundAltitude = groundSample.EllipsoidAltitudeMeters;
                normal = groundSample.NormalEastUpNorth.normalized;
                if (normal.y < 0f)
                {
                    normal = -normal;
                }
            }
            else if (!TryGetGroundSurface(horizontalPosition, out groundAltitude, out normal))
            {
                rejectionReason = "No prototype ground sample is available here.";
                return false;
            }

            float slopeLimit = Mathf.Min(
                Mathf.Clamp(maximumSlopeDegrees, 1f, 89f),
                Mathf.Clamp(maximumAcceptedGroundSlopeDegrees, 1f, 89f));
            float minimumNormalUp = Mathf.Cos(slopeLimit * Mathf.Deg2Rad);
            if (normal.y < minimumNormalUp)
            {
                rejectionReason = "Terrain slope exceeds the configured safe-spawn limit.";
                return false;
            }

            if (HasIndependentData)
            {
                GameplayAreaAssessment assessment;
                bool hasAssessment;
                try
                {
                    hasAssessment = gameplayDataSource.TryAssessArea(
                        horizontalPosition,
                        Mathf.Max(areaAssessmentRadiusMeters, characterRadiusMeters),
                        out assessment);
                }
                catch (Exception exception)
                {
                    rejectionReason = "Semantic gameplay-area query failed (" + exception.GetType().Name + ").";
                    return false;
                }

                if (!hasAssessment || !assessment.HasData)
                {
                    rejectionReason = "Semantic gameplay-area data is unavailable here; unknown areas are not treated as safe.";
                    return false;
                }
                if (IsUnsafeAssessment(assessment))
                {
                    rejectionReason = "The candidate intersects water, a building, a road, restricted land, a cliff, or otherwise non-walkable terrain.";
                    return false;
                }
            }

            GeoPosition footGeo = new GeoPosition(
                horizontalPosition.LatitudeDegrees,
                horizontalPosition.LongitudeDegrees,
                groundAltitude + Mathf.Max(0.01f, groundClearanceMeters));
            groundedFootPosition = worldManager.GeographicToLocal(footGeo);
            if (!IsInsideArenaWithMargin(groundedFootPosition, characterRadiusMeters + 1f))
            {
                rejectionReason = "The safe point is outside the arena boundary after applying player clearance.";
                groundedFootPosition = default(LocalPosition);
                return false;
            }

            if (IsCapsuleBlockedByProxy(
                    groundedFootPosition,
                    characterRadiusMeters,
                    settings != null ? settings.CharacterHeightMeters : 1.8f,
                    false))
            {
                rejectionReason = "The candidate overlaps an independent gameplay collision proxy.";
                groundedFootPosition = default(LocalPosition);
                return false;
            }
            return true;
        }

        public bool CanFitCapsule(
            LocalPosition footPosition,
            float radiusMeters,
            float currentHeightMeters,
            float targetHeightMeters)
        {
            if (targetHeightMeters <= currentHeightMeters)
            {
                return true;
            }
            if (worldManager == null || !HasIndependentData)
            {
                return IsPrototypeFallback && worldManager != null && worldManager.CanFitPlayerCapsule(
                    worldManager.LocalToGeographic(footPosition),
                    radiusMeters,
                    currentHeightMeters,
                    targetHeightMeters);
            }

            GeoPosition footGeo = worldManager.LocalToGeographic(footPosition);
            GameplayAreaAssessment area;
            try
            {
                if (!gameplayDataSource.TryAssessArea(footGeo, Mathf.Max(radiusMeters, areaAssessmentRadiusMeters), out area) ||
                    !area.HasData || IsUnsafeAssessment(area))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
            return !IsCapsuleBlockedByProxy(footPosition, radiusMeters, targetHeightMeters, false);
        }

        /// <summary>
        /// Sweeps the authoritative capsule through stable proxy and semantic collision data.
        /// Returns the desired position when clear, or the furthest safe position before a blocker.
        /// </summary>
        public bool TryResolveMovement(
            LocalPosition from,
            LocalPosition desired,
            float radiusMeters,
            float heightMeters,
            out LocalPosition resolved)
        {
            resolved = desired;
            if (!from.IsFinite || !desired.IsFinite || worldManager == null)
            {
                resolved = from;
                return false;
            }
            if (!HasIndependentData)
            {
                return IsPrototypeFallback;
            }

            double eastDelta = desired.EastMeters - from.EastMeters;
            double upDelta = desired.UpMeters - from.UpMeters;
            double northDelta = desired.NorthMeters - from.NorthMeters;
            double distance = Math.Sqrt(eastDelta * eastDelta + upDelta * upDelta + northDelta * northDelta);
            int sampleCount = Math.Max(1, Mathf.CeilToInt((float)(distance / CollisionSweepStepMeters)));
            LocalPosition lastClear = from;
            for (int i = 1; i <= sampleCount; i++)
            {
                double fraction = i / (double)sampleCount;
                LocalPosition sample = Lerp(from, desired, fraction);
                bool blocked = IsCapsuleBlockedByProxy(sample, radiusMeters, heightMeters, false) ||
                               IsUnsafeSemanticArea(sample, radiusMeters);
                if (blocked)
                {
                    double low = (i - 1) / (double)sampleCount;
                    double high = fraction;
                    for (int refinement = 0; refinement < CollisionSweepRefinementSteps; refinement++)
                    {
                        double middle = (low + high) * 0.5;
                        LocalPosition test = Lerp(from, desired, middle);
                        if (IsCapsuleBlockedByProxy(test, radiusMeters, heightMeters, false) ||
                            IsUnsafeSemanticArea(test, radiusMeters))
                        {
                            high = middle;
                        }
                        else
                        {
                            low = middle;
                        }
                    }
                    resolved = Lerp(from, desired, Math.Max(0.0, low - 0.005));
                    return false;
                }
                lastClear = sample;
            }
            resolved = lastClear;
            return true;
        }

        /// <summary>Finds the nearest projectile-blocking stable proxy in ENU metres.</summary>
        public bool TryRaycast(
            LocalPosition origin,
            Vector3 directionEastUpNorth,
            float rangeMeters,
            out float hitDistanceMeters,
            out GameplayFeatureKind featureKind,
            out string featureId)
        {
            hitDistanceMeters = rangeMeters;
            featureKind = GameplayFeatureKind.Terrain;
            featureId = string.Empty;
            if (!HasIndependentData || !origin.IsFinite || rangeMeters <= 0f ||
                directionEastUpNorth.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            Vector3 direction = directionEastUpNorth.normalized;
            bool found = false;
            double nearest = rangeMeters;
            for (int i = 0; i < runtimeProxies.Count; i++)
            {
                RuntimeProxy proxy = runtimeProxies[i];
                if (!proxy.BlocksProjectiles)
                {
                    continue;
                }
                double distance;
                if (TryRayProxy(origin, direction, rangeMeters, proxy, out distance) && distance < nearest)
                {
                    nearest = distance;
                    featureKind = proxy.FeatureKind;
                    featureId = proxy.FeatureId;
                    found = true;
                }
            }
            if (found)
            {
                hitDistanceMeters = (float)nearest;
            }
            return found;
        }

        private bool IsUnsafeSemanticArea(LocalPosition localPosition, float radiusMeters)
        {
            if (!HasIndependentData)
            {
                return false;
            }
            GeoPosition geographicPosition = worldManager.LocalToGeographic(localPosition);
            GameplayAreaAssessment assessment;
            try
            {
                if (!gameplayDataSource.TryAssessArea(
                        geographicPosition,
                        Mathf.Max(MinimumAreaQueryRadiusMeters, radiusMeters, areaAssessmentRadiusMeters),
                        out assessment) || !assessment.HasData)
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
            return IsUnsafeAssessment(assessment);
        }

        private bool IsUnsafeAssessment(GameplayAreaAssessment assessment)
        {
            if (!IsFinite(assessment.Confidence) ||
                assessment.Confidence < Mathf.Clamp01(minimumTerrainConfidence) ||
                !assessment.IsWalkable || assessment.IsWater || assessment.IsBuilding ||
                assessment.IsRestricted || assessment.IsRoad || assessment.IsCliff)
            {
                return true;
            }
            switch (assessment.FeatureKind)
            {
                case GameplayFeatureKind.Building:
                case GameplayFeatureKind.Wall:
                case GameplayFeatureKind.Road:
                case GameplayFeatureKind.Water:
                case GameplayFeatureKind.RestrictedArea:
                    return true;
                default:
                    return false;
            }
        }

        private bool IsInsideArenaWithMargin(LocalPosition position, double marginMeters)
        {
            double east = position.EastMeters;
            double north = position.NorthMeters;
            double usableRadius = Math.Max(0.0, arenaRadiusMeters - marginMeters);
            return east * east + north * north <= usableRadius * usableRadius;
        }

        private void BuildProxySpatialIndex()
        {
            proxiesBySpatialCell.Clear();
            largeProxyIndices.Clear();
            for (int i = 0; i < runtimeProxies.Count; i++)
            {
                RuntimeProxy proxy = runtimeProxies[i];
                double horizontalRadius = Math.Sqrt(
                    proxy.HalfEastMeters * proxy.HalfEastMeters +
                    proxy.HalfNorthMeters * proxy.HalfNorthMeters);
                int minimumEastCell = ToSpatialCell(proxy.Centre.EastMeters - horizontalRadius);
                int maximumEastCell = ToSpatialCell(proxy.Centre.EastMeters + horizontalRadius);
                int minimumNorthCell = ToSpatialCell(proxy.Centre.NorthMeters - horizontalRadius);
                int maximumNorthCell = ToSpatialCell(proxy.Centre.NorthMeters + horizontalRadius);
                long cellCount = ((long)maximumEastCell - minimumEastCell + 1L) *
                                 ((long)maximumNorthCell - minimumNorthCell + 1L);
                if (cellCount < 1L || cellCount > MaximumProxyCellsPerFeature)
                {
                    largeProxyIndices.Add(i);
                    continue;
                }

                for (int eastCell = minimumEastCell; eastCell <= maximumEastCell; eastCell++)
                {
                    for (int northCell = minimumNorthCell; northCell <= maximumNorthCell; northCell++)
                    {
                        long key = SpatialCellKey(eastCell, northCell);
                        List<int> bucket;
                        if (!proxiesBySpatialCell.TryGetValue(key, out bucket))
                        {
                            bucket = new List<int>(4);
                            proxiesBySpatialCell.Add(key, bucket);
                        }
                        bucket.Add(i);
                    }
                }
            }
        }

        private void CollectProxyCandidates(LocalPosition position, double queryRadiusMeters)
        {
            proxyCandidateScratch.Clear();
            proxyCandidateSet.Clear();
            for (int i = 0; i < largeProxyIndices.Count; i++)
            {
                int index = largeProxyIndices[i];
                if (proxyCandidateSet.Add(index))
                {
                    proxyCandidateScratch.Add(index);
                }
            }

            int minimumEastCell = ToSpatialCell(position.EastMeters - queryRadiusMeters);
            int maximumEastCell = ToSpatialCell(position.EastMeters + queryRadiusMeters);
            int minimumNorthCell = ToSpatialCell(position.NorthMeters - queryRadiusMeters);
            int maximumNorthCell = ToSpatialCell(position.NorthMeters + queryRadiusMeters);
            for (int eastCell = minimumEastCell; eastCell <= maximumEastCell; eastCell++)
            {
                for (int northCell = minimumNorthCell; northCell <= maximumNorthCell; northCell++)
                {
                    List<int> bucket;
                    if (!proxiesBySpatialCell.TryGetValue(SpatialCellKey(eastCell, northCell), out bucket))
                    {
                        continue;
                    }
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        int index = bucket[i];
                        if (proxyCandidateSet.Add(index))
                        {
                            proxyCandidateScratch.Add(index);
                        }
                    }
                }
            }
        }

        private static int ToSpatialCell(double eastOrNorthMeters)
        {
            return (int)Math.Floor(eastOrNorthMeters / ProxySpatialCellSizeMeters);
        }

        private static long SpatialCellKey(int eastCell, int northCell)
        {
            unchecked
            {
                return ((long)eastCell << 32) ^ (uint)northCell;
            }
        }

        private bool IsCapsuleBlockedByProxy(
            LocalPosition foot,
            float radiusMeters,
            float heightMeters,
            bool projectileQuery)
        {
            double radius = Math.Max(0.01, radiusMeters);
            double top = foot.UpMeters + Math.Max(heightMeters, radius * 2.0);
            CollectProxyCandidates(foot, radius);
            for (int candidateIndex = 0; candidateIndex < proxyCandidateScratch.Count; candidateIndex++)
            {
                RuntimeProxy proxy = runtimeProxies[proxyCandidateScratch[candidateIndex]];
                if (projectileQuery ? !proxy.BlocksProjectiles : !proxy.BlocksMovement)
                {
                    continue;
                }
                if (top < proxy.Centre.UpMeters - proxy.HalfUpMeters ||
                    foot.UpMeters > proxy.Centre.UpMeters + proxy.HalfUpMeters)
                {
                    continue;
                }

                double eastDelta = foot.EastMeters - proxy.Centre.EastMeters;
                double northDelta = foot.NorthMeters - proxy.Centre.NorthMeters;
                double cosine = Math.Cos(proxy.YawRadians);
                double sine = Math.Sin(proxy.YawRadians);
                double localEast = eastDelta * cosine - northDelta * sine;
                double localNorth = eastDelta * sine + northDelta * cosine;
                double nearestEast = Math.Max(Math.Abs(localEast) - proxy.HalfEastMeters, 0.0);
                double nearestNorth = Math.Max(Math.Abs(localNorth) - proxy.HalfNorthMeters, 0.0);
                if (nearestEast * nearestEast + nearestNorth * nearestNorth <= radius * radius)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryRayProxy(
            LocalPosition origin,
            Vector3 direction,
            float maximumDistance,
            RuntimeProxy proxy,
            out double distance)
        {
            distance = 0.0;
            double eastDelta = origin.EastMeters - proxy.Centre.EastMeters;
            double northDelta = origin.NorthMeters - proxy.Centre.NorthMeters;
            double cosine = Math.Cos(proxy.YawRadians);
            double sine = Math.Sin(proxy.YawRadians);
            double localEast = eastDelta * cosine - northDelta * sine;
            double localNorth = eastDelta * sine + northDelta * cosine;
            double localDirectionEast = direction.x * cosine - direction.z * sine;
            double localDirectionNorth = direction.x * sine + direction.z * cosine;

            double minimum = 0.0;
            double maximum = maximumDistance;
            if (!IntersectSlab(localEast, localDirectionEast, -proxy.HalfEastMeters, proxy.HalfEastMeters, ref minimum, ref maximum) ||
                !IntersectSlab(origin.UpMeters - proxy.Centre.UpMeters, direction.y,
                    -proxy.HalfUpMeters, proxy.HalfUpMeters, ref minimum, ref maximum) ||
                !IntersectSlab(localNorth, localDirectionNorth, -proxy.HalfNorthMeters, proxy.HalfNorthMeters, ref minimum, ref maximum))
            {
                return false;
            }
            distance = minimum;
            return distance <= maximumDistance;
        }

        private static bool IntersectSlab(
            double origin,
            double direction,
            double minimumBound,
            double maximumBound,
            ref double minimumDistance,
            ref double maximumDistance)
        {
            if (Math.Abs(direction) < 0.0000001)
            {
                return origin >= minimumBound && origin <= maximumBound;
            }
            double inverse = 1.0 / direction;
            double first = (minimumBound - origin) * inverse;
            double second = (maximumBound - origin) * inverse;
            if (first > second)
            {
                double swap = first;
                first = second;
                second = swap;
            }
            minimumDistance = Math.Max(minimumDistance, first);
            maximumDistance = Math.Min(maximumDistance, second);
            return minimumDistance <= maximumDistance;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static LocalPosition Lerp(LocalPosition first, LocalPosition second, double fraction)
        {
            return new LocalPosition(
                first.EastMeters + (second.EastMeters - first.EastMeters) * fraction,
                first.UpMeters + (second.UpMeters - first.UpMeters) * fraction,
                first.NorthMeters + (second.NorthMeters - first.NorthMeters) * fraction);
        }
    }
}
