using System;
using System.Collections.Generic;
using UnityEngine;
using WorldPvp.Phase1.Battles;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Gameplay
{
    public enum ArenaObjectiveCandidateKind : byte
    {
        ControlPoint,
        SupplyCache,
        ScanningPost
    }

    [Serializable]
    public struct ArenaObjectiveCandidate
    {
        public int Index;
        public ArenaObjectiveCandidateKind Kind;
        public LocalPosition Position;

        public ArenaObjectiveCandidate(int index, ArenaObjectiveCandidateKind kind, LocalPosition position)
        {
            Index = index;
            Kind = kind;
            Position = position;
        }
    }

    /// <summary>
    /// Creates and assigns the deterministic virtual arena plan for the centre/radius in match
    /// metadata. Points are all virtual; visiting the corresponding real-world coordinates grants no
    /// game benefit and is never part of match acceptance.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayArenaRuntime : MonoBehaviour
    {
        public const string SafetyWarning =
            "This is a virtual game. Stay aware of your surroundings. Do not enter roads, private property, restricted areas or dangerous locations while playing.";

        private const int MaximumSupportedPlayers = 64;
        private const float SpawnBoundaryInsetMeters = 2.0f;
        private const float MinimumSpawnSeparationMeters = 18.0f;
        private const float ObjectiveSeparationMultiplier = 1.5f;

        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private PhaseOneWorldSettings settings;
        [SerializeField] private GameplayCollisionWorld collisionWorld;
        [SerializeField, Min(0.25f)] private float minimumPlayerSpawnSeparationMeters = MinimumSpawnSeparationMeters;
        [SerializeField, Range(1, 3)] private int maximumObjectiveCandidates = 3;

        private readonly List<ArenaSpawnPoint> spawnPoints = new List<ArenaSpawnPoint>();
        private readonly List<ArenaObjectiveCandidate> objectiveCandidates = new List<ArenaObjectiveCandidate>();
        private readonly Dictionary<ulong, int> assignedSpawnIndices = new Dictionary<ulong, int>();
        private GeoPosition configuredCentre;
        private double configuredRadiusMeters;
        private int configuredMaximumPlayers;
        private bool matchConfigured;
        private bool planPrepared;
        private string lastError = "Choose a geographic arena before generating its gameplay plan.";
        private PhaseEightServerAuthority serverAuthority;

        public string LastError { get { return lastError; } }
        public GeoPosition ArenaCentre { get { return configuredCentre; } }
        public double ArenaRadiusMeters { get { return configuredRadiusMeters; } }
        public int MaximumPlayers { get { return configuredMaximumPlayers; } }
        public bool IsPlanPrepared { get { return planPrepared; } }
        public int SpawnPointCount { get { return spawnPoints.Count; } }
        public int ObjectiveCandidateCount { get { return objectiveCandidates.Count; } }
        public IList<ArenaSpawnPoint> SpawnPoints { get { return spawnPoints.AsReadOnly(); } }
        public IList<ArenaObjectiveCandidate> ObjectiveCandidates { get { return objectiveCandidates.AsReadOnly(); } }
        public bool IsPrototypeFallback
        {
            get
            {
                return collisionWorld != null &&
                       (collisionWorld.IsPrototypeFallback || collisionWorld.WillUsePrototypeVisualFallback);
            }
        }
        public string GameplayDataAttribution
        {
            get
            {
                return collisionWorld != null && collisionWorld.DataSource != null
                    ? collisionWorld.DataSource.RequiredAttribution
                    : string.Empty;
            }
        }
        public string GameplayDataNotice
        {
            get
            {
                if (collisionWorld == null)
                {
                    return "Gameplay collision world is missing; arena gameplay is unavailable.";
                }
                return collisionWorld.IsPrototypeFallback || collisionWorld.WillUsePrototypeVisualFallback
                    ? "PROTOTYPE ONLY: transient visual-tile colliders are not independent gameplay data; water, roads, buildings, cliffs, restricted areas, and reliable safe-spawn avoidance are not verified."
                    : collisionWorld.ConfigurationError;
            }
        }

        public void ConfigureReferences(
            GeospatialWorldManager manager,
            PhaseOneWorldSettings worldSettings,
            GameplayCollisionWorld gameplayCollisionWorld)
        {
            worldManager = manager;
            settings = worldSettings;
            collisionWorld = gameplayCollisionWorld;
        }

        private void Awake()
        {
            if (worldManager == null)
            {
                worldManager = FindObjectOfType<GeospatialWorldManager>();
            }
            if (collisionWorld == null)
            {
                collisionWorld = FindObjectOfType<GameplayCollisionWorld>();
            }
            if (settings == null && worldManager != null)
            {
                settings = worldManager.WorldSettings;
            }
            serverAuthority = FindObjectOfType<PhaseEightServerAuthority>();
        }

        /// <summary>Stores the exact session metadata. World queries are prepared after BeginArena.</summary>
        public bool ConfigureMatch(GeoPosition centre, double radiusMeters, int maximumPlayers, out string error)
        {
            error = string.Empty;
            if (!centre.IsValid || !GeoPosition.IsFinite(radiusMeters) || radiusMeters <= 0.0 ||
                maximumPlayers < 2 || maximumPlayers > MaximumSupportedPlayers)
            {
                error = "Arena metadata must contain a valid WGS84 centre, positive radius, and between 2 and " +
                        MaximumSupportedPlayers + " players.";
                lastError = error;
                return false;
            }

            configuredCentre = centre;
            configuredRadiusMeters = radiusMeters;
            configuredMaximumPlayers = maximumPlayers;
            matchConfigured = true;
            planPrepared = false;
            spawnPoints.Clear();
            objectiveCandidates.Clear();
            assignedSpawnIndices.Clear();
            lastError = "Arena metadata configured; waiting for terrain and gameplay data.";
            return true;
        }

        /// <summary>Fails closed if the complete plan cannot be produced from available data.</summary>
        public bool TryPrepareArena(out string error)
        {
            error = string.Empty;
            if (planPrepared)
            {
                return true;
            }
            if (!matchConfigured || worldManager == null || settings == null || collisionWorld == null ||
                !worldManager.IsArenaConfigured || !worldManager.IsReady ||
                !configuredCentre.Equals(worldManager.ArenaCentre) ||
                Math.Abs(configuredRadiusMeters - worldManager.GameplayRadiusMeters) > 0.01)
            {
                error = "The exact match arena is not ready for safe gameplay planning.";
                lastError = error;
                return false;
            }
            if (!collisionWorld.IsProductionReady(configuredCentre, configuredRadiusMeters, out error) &&
                !collisionWorld.IsPrototypeFallback)
            {
                lastError = error;
                return false;
            }

            LocalPosition arenaCentreLocal;
            try
            {
                arenaCentreLocal = worldManager.GeographicToLocal(configuredCentre);
            }
            catch (Exception exception)
            {
                error = "Could not convert the arena centre to the fixed ENU frame (" + exception.GetType().Name + ").";
                lastError = error;
                return false;
            }

            float playerRadius = settings.CharacterRadiusMeters;
            float minimumSeparation = Mathf.Max(minimumPlayerSpawnSeparationMeters, playerRadius * 4f);
            int seed = GeographicArenaGenerator.CreateDeterministicSeed(configuredCentre, configuredRadiusMeters);
            if (!GeographicArenaGenerator.TryGenerateSpawnPoints(
                    arenaCentreLocal,
                    configuredRadiusMeters,
                    playerRadius,
                    SpawnBoundaryInsetMeters,
                    minimumSeparation,
                    configuredMaximumPlayers,
                    seed,
                    TrySampleSafeSpawn,
                    out List<ArenaSpawnPoint> generatedSpawns,
                    out error))
            {
                lastError = error;
                return false;
            }

            spawnPoints.Clear();
            spawnPoints.AddRange(generatedSpawns);
            int requestedObjectives = Mathf.Clamp(maximumObjectiveCandidates, 1, 3);
            requestedObjectives = Mathf.Min(requestedObjectives, Mathf.Max(1, configuredMaximumPlayers / 3));
            bool objectivePlanReady = false;
            string objectiveError = string.Empty;
            for (int count = requestedObjectives; count >= 1; count--)
            {
                if (GeographicArenaGenerator.TryGenerateSpawnPoints(
                        arenaCentreLocal,
                        configuredRadiusMeters,
                        playerRadius,
                        SpawnBoundaryInsetMeters,
                        minimumSeparation * ObjectiveSeparationMultiplier,
                        count,
                        seed ^ 0x5A17C9E3,
                        TrySampleSafeObjective,
                        out List<ArenaSpawnPoint> generatedObjectives,
                        out objectiveError))
                {
                    objectiveCandidates.Clear();
                    for (int i = 0; i < generatedObjectives.Count; i++)
                    {
                        ArenaObjectiveCandidateKind kind = (ArenaObjectiveCandidateKind)(i % 3);
                        objectiveCandidates.Add(new ArenaObjectiveCandidate(i, kind, generatedObjectives[i].FootPosition));
                    }
                    objectivePlanReady = true;
                    break;
                }
            }
            if (!objectivePlanReady)
            {
                spawnPoints.Clear();
                error = "Safe player spawns were found, but no separate verified objective candidate could be placed. " +
                        (string.IsNullOrEmpty(objectiveError) ? "" : objectiveError);
                lastError = error;
                return false;
            }

            planPrepared = true;
            lastError = collisionWorld.IsPrototypeFallback
                ? "Prototype arena plan prepared. Terrain ground is sampled from transient visual-tile collision; semantic hazards are not verified."
                : "Arena plan prepared from the approved independent gameplay-data source.";
            return true;
        }

        /// <summary>
        /// Reserves one unique, data-validated spawn for a player. Existing live server players are
        /// checked again so a moving player cannot occupy a pending arrival point.
        /// </summary>
        public bool TryAssignSpawn(
            ulong clientId,
            out LocalPosition spawnPosition,
            out float yawDegrees,
            out string error)
        {
            spawnPosition = default(LocalPosition);
            yawDegrees = 0f;
            error = string.Empty;
            if (!planPrepared && !TryPrepareArena(out error))
            {
                return false;
            }

            int existingIndex;
            if (assignedSpawnIndices.TryGetValue(clientId, out existingIndex))
            {
                if (existingIndex >= 0 && existingIndex < spawnPoints.Count)
                {
                    spawnPosition = spawnPoints[existingIndex].FootPosition;
                    yawDegrees = spawnPoints[existingIndex].YawDegrees;
                    return true;
                }
                error = "A previous spawn reservation for this player is invalid.";
                return false;
            }

            if (serverAuthority == null)
            {
                serverAuthority = FindObjectOfType<PhaseEightServerAuthority>();
            }
            float minimumSeparation = Mathf.Max(minimumPlayerSpawnSeparationMeters, settings.CharacterRadiusMeters * 4f);
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                bool alreadyAssigned = false;
                foreach (KeyValuePair<ulong, int> assignment in assignedSpawnIndices)
                {
                    if (assignment.Key != clientId && assignment.Value == i)
                    {
                        alreadyAssigned = true;
                        break;
                    }
                }
                if (alreadyAssigned || IsTooCloseToLivePlayer(clientId, spawnPoints[i].FootPosition, minimumSeparation))
                {
                    continue;
                }

                assignedSpawnIndices[clientId] = i;
                spawnPosition = spawnPoints[i].FootPosition;
                yawDegrees = spawnPoints[i].YawDegrees;
                return true;
            }

            error = "No unused verified spawn remains that is separated from all live players. The player is not placed at the centre.";
            lastError = error;
            return false;
        }

        private bool TrySampleSafeSpawn(LocalPosition candidate, out LocalPosition grounded)
        {
            string rejection;
            return collisionWorld.TryGetSafeSpawnFoot(
                candidate,
                settings.CharacterRadiusMeters,
                settings.GroundSpawnClearanceMeters,
                settings.MaximumSlopeDegrees,
                out grounded,
                out rejection);
        }

        private bool TrySampleSafeObjective(LocalPosition candidate, out LocalPosition grounded)
        {
            string rejection;
            if (!collisionWorld.TryGetSafeSpawnFoot(
                    candidate,
                    settings.CharacterRadiusMeters,
                    settings.GroundSpawnClearanceMeters,
                    settings.MaximumSlopeDegrees,
                    out grounded,
                    out rejection))
            {
                return false;
            }

            float requiredDistance = Mathf.Max(minimumPlayerSpawnSeparationMeters, settings.CharacterRadiusMeters * 4f) *
                                     ObjectiveSeparationMultiplier;
            double requiredSquared = requiredDistance * requiredDistance;
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (HorizontalDistanceSquared(grounded, spawnPoints[i].FootPosition) < requiredSquared)
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsTooCloseToLivePlayer(ulong joiningClientId, LocalPosition candidate, float minimumDistance)
        {
            if (serverAuthority == null)
            {
                return false;
            }

            double minimumSquared = minimumDistance * minimumDistance;
            int playerCount = serverAuthority.RegisteredPlayerCount;
            for (int i = 0; i < playerCount; i++)
            {
                NetworkPlayer player = serverAuthority.GetServerPlayerByIndex(i);
                if (player == null || player.OwnerClientId == joiningClientId || !player.IsSpawned)
                {
                    continue;
                }
                NetworkPlayerSnapshot state = player.CurrentState;
                if (!state.Initialized || !state.Alive || state.MatchFinished)
                {
                    continue;
                }
                LocalPosition current = new LocalPosition(state.EastMeters, state.UpMeters, state.NorthMeters);
                if (HorizontalDistanceSquared(candidate, current) < minimumSquared)
                {
                    return true;
                }
            }
            return false;
        }

        private static double HorizontalDistanceSquared(LocalPosition first, LocalPosition second)
        {
            double east = first.EastMeters - second.EastMeters;
            double north = first.NorthMeters - second.NorthMeters;
            return east * east + north * north;
        }
    }
}
