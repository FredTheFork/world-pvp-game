using System;
using System.Collections.Generic;
using CesiumForUnity;
using Unity.Collections;
using Unity.Netcode;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using WorldPvp.Phase1.Combat;
using WorldPvp.Phase1.Geospatial;
using WorldPvp.Phase1.Gameplay;
using WorldPvp.Phase1.Player;
using WorldPvp.Phase1.Streaming;

namespace WorldPvp.Phase1.Battles
{
    /// <summary>
    /// One NGO player object per session member. Owners transmit sequenced input intent only;
    /// the session host simulates fixed ENU movement and writes snapshots. Owners predict/replay
    /// unacknowledged commands; non-owners render a delayed interpolation buffer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        public const int ServerSimulationTickRate = NetworkPlayerSimulation.TickRate;
        public const int MaximumBufferedInputs = 256;

        private const int MaximumCatchUpTicksPerFrame = 8;
        private const float MaximumAccumulatorSeconds = 0.25f;
        private const float InterpolationDelaySeconds = 0.10f;
        private const float MaximumExtrapolationSeconds = 0.10f;
        private const int MaximumRemoteSnapshots = 32;
        private const float InputRateBurstTokens = 3f;
        private const float MapUsageReportIntervalSeconds = 5f;
        private const float MinimumMapUsageReportIntervalSeconds = 1f;
        private const int MaximumPendingFireCommands = 16;
        private const float MaximumFireOriginDeviationMeters = 1.25f;
        private const float MaximumFireAimDeviationDegrees = 12f;
        private const double MaximumUncompensatedFireRpcGapSeconds = 0.75;
        private const double MaximumShotTimeSkewSeconds = 0.25;
        private const float DefaultViolationScoreDecayPerSecond = 0.25f;
        private const int WorldRaycastBufferCapacity = 128;
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorPropertyId = Shader.PropertyToID("_Color");
        private static Material sharedRemoteAvatarMaterial;

        private NetworkVariable<NetworkPlayerSnapshot> authoritativeState =
            new NetworkVariable<NetworkPlayerSnapshot>(
                default(NetworkPlayerSnapshot),
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly Queue<QueuedServerInput> serverInputQueue =
            new Queue<QueuedServerInput>();
        private readonly List<NetworkPlayerInputCommand> pendingOwnerInputs =
            new List<NetworkPlayerInputCommand>(64);
        private readonly List<ReceivedSnapshot> remoteSnapshots =
            new List<ReceivedSnapshot>(MaximumRemoteSnapshots);

        private GeospatialWorldManager worldManager;
        private PhaseOneWorldSettings worldSettings;
        private GameplayArenaRuntime gameplayArenaRuntime;
        private GameplayCollisionWorld gameplayCollisionWorld;
        private PhaseOneTestCharacterController localMotor;
        private NetworkPlayerSnapshot serverState;
        private NetworkPlayerSnapshot predictedState;
        private GameObject remoteAvatar;
        private CesiumGlobeAnchor remoteAvatarAnchor;
        private Transform remoteFacingMarker;
        private ProceduralPlayerPresentation remotePresentation;
        private MapUsageTracker mapUsageTracker;
        private Weapon weapon;
        private PhaseEightServerAuthority serverAuthority;
        private readonly LagCompensationHistory lagCompensationHistory =
            new LagCompensationHistory(LagCompensationHistory.DefaultWindowSeconds);
        private readonly ServerViolationScore violationScore = new ServerViolationScore();
        private bool registeredWithServerAuthority;
        private readonly Dictionary<uint, ReceivedFireCommand> pendingFireCommands =
            new Dictionary<uint, ReceivedFireCommand>(MaximumPendingFireCommands);
        private readonly RaycastHit[] worldRaycastHits = new RaycastHit[WorldRaycastBufferCapacity];

        public static event Action<CombatEvent> CombatEventReceived;

        private bool serverInitialized;
        private bool spawnFailureLogged;
        private bool ownerInitialized;
        private uint lastReceivedInputTick;
        private uint nextOwnerInputTick;
        private uint lastAcknowledgedInputTick;
        private uint lastAppliedServerTick;
        private bool hasAppliedServerTick;
        private float availableInputRateTokens = InputRateBurstTokens;
        private float lastInputRateRefillTime;
        private float availableFireIntentTokens = InputRateBurstTokens;
        private double lastFireIntentRefillAt;
        private float nextViolationReviewTime;
        private float nextMapUsageReportTime;
        private float nextAcceptedMapUsageReportTime;
        private uint nextMapUsageSequence;
        private uint lastAcceptedMapUsageSequence;
        private bool hasAcceptedMapUsageSequence;

        private float serverAccumulator;
        private float ownerAccumulator;
        private float latestMoveX;
        private float latestMoveY;
        private float bufferedLookX;
        private float bufferedLookY;
        private bool latestSprint;
        private bool latestCrouch;
        private bool bufferedJump;
        private bool bufferedFire;
        private bool latestFireHeld;
        private bool bufferedReload;
        private float nextGroundSampleTime;

        private struct ReceivedSnapshot
        {
            public NetworkPlayerSnapshot State;
            public float ReceivedAt;

            public ReceivedSnapshot(NetworkPlayerSnapshot state, float receivedAt)
            {
                State = state;
                ReceivedAt = receivedAt;
            }
        }

        private struct QueuedServerInput
        {
            public NetworkPlayerInputCommand Command;
            public double ReceivedAtServerTime;
            public float EstimatedOneWayLatencySeconds;

            public QueuedServerInput(
                NetworkPlayerInputCommand command,
                double receivedAtServerTime,
                float estimatedOneWayLatencySeconds)
            {
                Command = command;
                ReceivedAtServerTime = receivedAtServerTime;
                EstimatedOneWayLatencySeconds = estimatedOneWayLatencySeconds;
            }
        }

        private struct ReceivedFireCommand
        {
            public FireCommand Command;
            public double ReceivedAtServerTime;

            public ReceivedFireCommand(FireCommand command, double receivedAtServerTime)
            {
                Command = command;
                ReceivedAtServerTime = receivedAtServerTime;
            }
        }

        public bool HasAuthoritativeState
        {
            get { return IsSpawned && authoritativeState.Value.Initialized; }
        }

        public bool HasGameplaySpawnReady
        {
            get
            {
                return HasAuthoritativeState && (!IsOwner ||
                    (ownerInitialized && localMotor != null && worldManager != null && worldManager.IsReady));
            }
        }

        public NetworkPlayerSnapshot CurrentState
        {
            get
            {
                return IsOwner && !IsServer && ownerInitialized
                    ? predictedState
                    : authoritativeState.Value;
            }
        }

        public uint LastReceivedInputTick { get { return lastReceivedInputTick; } }
        public uint NextOwnerInputTick { get { return nextOwnerInputTick; } }
        public uint LastAcknowledgedInputTick { get { return lastAcknowledgedInputTick; } }
        public int PendingOwnerInputCount { get { return pendingOwnerInputs.Count; } }
        public int BufferedRemoteSnapshotCount { get { return remoteSnapshots.Count; } }
        public int HistoricalSnapshotCount { get { return lagCompensationHistory.Count; } }
        public float ServerViolationScore { get { return IsServer ? violationScore.CurrentScore : 0f; } }
        public bool HasRemoteAvatar { get { return remoteAvatar != null; } }

        public bool TryGetHistoricalSnapshot(double serverTimeSeconds, out NetworkPlayerSnapshot snapshot)
        {
            if (!IsServer)
            {
                snapshot = default(NetworkPlayerSnapshot);
                return false;
            }
            return lagCompensationHistory.TrySample(serverTimeSeconds, out snapshot);
        }

        public void ConfigureLagCompensationHistory(float windowSeconds)
        {
            lagCompensationHistory.ConfigureWindow(windowSeconds);
        }
        public bool IsServerAuthoritativeInstance { get { return IsServer; } }
        public PhaseOneTestCharacterController LocalMotor { get { return localMotor; } }
        public Weapon EquippedWeapon
        {
            get
            {
                if (weapon == null)
                {
                    weapon = GetComponent<Weapon>();
                }
                return weapon;
            }
        }

        public override void OnNetworkSpawn()
        {
            authoritativeState.OnValueChanged += OnAuthoritativeStateChanged;
            ResolveSceneReferences();
            lagCompensationHistory.Clear();
            pendingFireCommands.Clear();
            registeredWithServerAuthority = false;
            if (weapon != null)
            {
                weapon.ResetClientFirePacing();
            }

            if (IsOwner && localMotor != null)
            {
                localMotor.SetNetworkControlled(true);
            }

            if (IsServer)
            {
                TryInitializeServerState();
            }

            OnAuthoritativeStateChanged(default(NetworkPlayerSnapshot), authoritativeState.Value);
            TryInitializeOwnerFromSnapshot();
        }

        public override void OnNetworkDespawn()
        {
            authoritativeState.OnValueChanged -= OnAuthoritativeStateChanged;
            if (IsServer && serverAuthority != null)
            {
                serverAuthority.UnregisterServerPlayer(this);
            }
            registeredWithServerAuthority = false;
            DestroyRemoteAvatar();
            pendingFireCommands.Clear();
            serverInputQueue.Clear();
            pendingOwnerInputs.Clear();
            remoteSnapshots.Clear();
            lagCompensationHistory.Clear();

            if (IsOwner && localMotor != null)
            {
                localMotor.SetInputEnabled(false);
                localMotor.SetSpectatorCameraDetached(false);
                localMotor.SetNetworkControlled(false);
            }

            worldManager = null;
            worldSettings = null;
            gameplayArenaRuntime = null;
            gameplayCollisionWorld = null;
            localMotor = null;
            mapUsageTracker = null;
            serverAuthority = null;
            nextMapUsageReportTime = 0f;
            nextAcceptedMapUsageReportTime = 0f;
            nextMapUsageSequence = 0;
            lastAcceptedMapUsageSequence = 0;
            hasAcceptedMapUsageSequence = false;
        }

        private void Update()
        {
            if (!IsSpawned)
            {
                return;
            }

            ResolveSceneReferences();

            if (IsServer && !serverInitialized)
            {
                TryInitializeServerState();
            }

            // Sample the host owner's command before pumping its fixed-step queue, so host movement
            // gets the same immediate authoritative step instead of an avoidable extra tick of delay.
            if (IsOwner)
            {
                TryInitializeOwnerFromSnapshot();
                if (ownerInitialized)
                {
                    SampleOwnerInputAndPredict(Time.unscaledDeltaTime);
                }
            }

            if (IsServer && serverInitialized)
            {
                if (!registeredWithServerAuthority)
                {
                    RegisterWithServerAuthority();
                }
                AdvanceViolationScore();
                if (weapon != null && weapon.ServerTick(Time.realtimeSinceStartupAsDouble))
                {
                    CopyWeaponStateToSnapshot();
                    authoritativeState.Value = serverState;
                }
                PumpServerSimulation(Time.unscaledDeltaTime);
            }

            if (IsOwner)
            {
                UpdateMapUsageReporting();
            }

            if (!IsOwner)
            {
                RenderInterpolatedRemotePlayer();
            }
        }

        private void ResolveSceneReferences()
        {
            if (worldManager == null)
            {
                worldManager = FindObjectOfType<GeospatialWorldManager>();
            }
            if (worldSettings == null && worldManager != null)
            {
                worldSettings = worldManager.WorldSettings;
            }
            if (gameplayArenaRuntime == null)
            {
                gameplayArenaRuntime = FindObjectOfType<GameplayArenaRuntime>();
            }
            if (gameplayCollisionWorld == null)
            {
                gameplayCollisionWorld = FindObjectOfType<GameplayCollisionWorld>();
            }
            if (mapUsageTracker == null)
            {
                mapUsageTracker = FindObjectOfType<MapUsageTracker>();
            }
            if (serverAuthority == null)
            {
                serverAuthority = FindObjectOfType<PhaseEightServerAuthority>();
            }
            if (weapon == null)
            {
                weapon = GetComponent<Weapon>();
            }
            if (IsOwner && localMotor == null)
            {
                localMotor = FindObjectOfType<PhaseOneTestCharacterController>();
                if (localMotor != null)
                {
                    localMotor.SetNetworkControlled(true);
                    if (ownerInitialized)
                    {
                        ApplyOwnerPose(IsServer ? serverState : predictedState);
                    }
                }
            }
            if (IsOwner && localMotor != null && !localMotor.IsNetworkControlled)
            {
                localMotor.SetNetworkControlled(true);
            }
        }

        private bool TryInitializeServerState()
        {
            if (serverInitialized || !IsServer || worldManager == null || worldSettings == null ||
                gameplayArenaRuntime == null || gameplayCollisionWorld == null ||
                !worldManager.IsReady || !worldManager.IsArenaConfigured || !worldManager.HasArenaGroundPosition)
            {
                return false;
            }

            LocalPosition spawnPosition;
            float spawnYaw;
            if (!TryBuildAuthoritativeSpawn(out spawnPosition, out spawnYaw))
            {
                return false;
            }

            GeoPosition spawnGeographic = worldManager.LocalToGeographic(spawnPosition);
            double sampledGroundHeight;
            Vector3 groundNormal;
            if (!gameplayCollisionWorld.TryGetGroundSurface(
                    spawnGeographic,
                    out sampledGroundHeight,
                    out groundNormal))
            {
                return false;
            }
            GeoPosition clearancePosition = new GeoPosition(
                spawnGeographic.LatitudeDegrees,
                spawnGeographic.LongitudeDegrees,
                sampledGroundHeight + worldSettings.GroundSpawnClearanceMeters);
            LocalPosition surfaceSpawn = worldManager.GeographicToLocal(clearancePosition);
            double groundLevelUp = surfaceSpawn.UpMeters;
            spawnPosition = new LocalPosition(
                spawnPosition.EastMeters,
                groundLevelUp,
                spawnPosition.NorthMeters);

            if (weapon != null)
            {
                weapon.InitializeServerState();
            }
            WeaponDefinition weaponDefinition = weapon != null ? weapon.Definition : null;
            bool hasEquippedWeapon = weapon != null && weapon.IsEquipped && weaponDefinition != null;

            NetworkPlayerSnapshot initial = new NetworkPlayerSnapshot
            {
                Initialized = true,
                PlayerId = OwnerClientId,
                DisplayName = new FixedString64Bytes("Player " + (OwnerClientId + 1).ToString()),
                EastMeters = spawnPosition.EastMeters,
                UpMeters = spawnPosition.UpMeters,
                NorthMeters = spawnPosition.NorthMeters,
                GroundLevelUpMeters = groundLevelUp,
                GroundNormalEast = groundNormal.x,
                GroundNormalUp = groundNormal.y,
                GroundNormalNorth = groundNormal.z,
                YawDegrees = spawnYaw,
                PitchDegrees = worldSettings.InitialCameraPitchDegrees,
                VelocityEastMetersPerSecond = 0f,
                VelocityUpMetersPerSecond = 0f,
                VelocityNorthMetersPerSecond = 0f,
                Grounded = true,
                MovementState = NetworkPlayerMovementState.Idle,
                Health = worldSettings.MaximumPlayerHealth,
                Alive = true,
                Team = (byte)(OwnerClientId % 2UL),
                EquippedWeaponId = hasEquippedWeapon
                    ? new FixedString64Bytes(weaponDefinition.WeaponId)
                    : default(FixedString64Bytes),
                MagazineAmmo = hasEquippedWeapon ? weapon.MagazineAmmo : (ushort)0,
                IsReloading = false,
                MatchFinished = false,
                ServerTick = 0,
                LastProcessedInputTick = 0,
                LastFireInputTick = 0
            };

            serverState = initial;
            serverInitialized = true;
            lastReceivedInputTick = 0;
            lastAcknowledgedInputTick = 0;
            availableInputRateTokens = InputRateBurstTokens;
            lastInputRateRefillTime = Time.unscaledTime;
            availableFireIntentTokens = InputRateBurstTokens;
            lastFireIntentRefillAt = Time.realtimeSinceStartupAsDouble;
            nextGroundSampleTime = Time.unscaledTime + worldSettings.TerrainSampleIntervalSeconds;
            if (serverAuthority != null)
            {
                ConfigureLagCompensationHistory(serverAuthority.LagCompensationWindowSeconds);
            }
            authoritativeState.Value = serverState;
            RecordServerHistory(Time.realtimeSinceStartupAsDouble);
            RegisterWithServerAuthority();
            if (IsOwner)
            {
                InitializeLocalPrediction(serverState);
            }
            else
            {
                BufferRemoteSnapshot(serverState);
            }
            return true;
        }

        private bool TryBuildAuthoritativeSpawn(out LocalPosition spawnPosition, out float yawDegrees)
        {
            spawnPosition = default(LocalPosition);
            yawDegrees = 0f;
            if (worldManager == null || !worldManager.IsReady || gameplayArenaRuntime == null ||
                gameplayCollisionWorld == null)
            {
                return false;
            }

            string error;
            if (!gameplayArenaRuntime.TryAssignSpawn(
                    OwnerClientId,
                    out spawnPosition,
                    out yawDegrees,
                    out error))
            {
                if (!string.IsNullOrEmpty(error) && !spawnFailureLogged)
                {
                    Debug.LogWarning("[Gameplay spawn] Player " + OwnerClientId + " was not placed: " + error, this);
                    spawnFailureLogged = true;
                }
                return false;
            }
            spawnFailureLogged = false;
            return spawnPosition.IsFinite;
        }

        private void PumpServerSimulation(float frameDeltaSeconds)
        {
            float clampedDelta = IsFinite(frameDeltaSeconds)
                ? Mathf.Clamp(frameDeltaSeconds, 0f, MaximumAccumulatorSeconds)
                : 0f;
            serverAccumulator = Mathf.Min(serverAccumulator + clampedDelta, MaximumAccumulatorSeconds);

            int ticks = 0;
            while (serverAccumulator >= NetworkPlayerSimulation.TickDeltaSeconds &&
                   ticks < MaximumCatchUpTicksPerFrame)
            {
                ProcessAuthoritativeInputTick();
                serverAccumulator -= NetworkPlayerSimulation.TickDeltaSeconds;
                ticks++;
            }

            // A backgrounded WebGL tab must not trigger a large unbounded catch-up burst.
            if (ticks == MaximumCatchUpTicksPerFrame &&
                serverAccumulator > NetworkPlayerSimulation.TickDeltaSeconds * 2f)
            {
                serverAccumulator = NetworkPlayerSimulation.TickDeltaSeconds * 2f;
            }
        }

        private void ProcessAuthoritativeInputTick()
        {
            if (!serverInitialized)
            {
                return;
            }

            NetworkPlayerSnapshot previous = serverState;
            QueuedServerInput acceptedInput = default(QueuedServerInput);
            NetworkPlayerInputCommand command = new NetworkPlayerInputCommand
            {
                Tick = 0,
                Crouch = serverState.Crouched
            };
            bool hadInput = serverInputQueue.Count > 0;
            if (hadInput)
            {
                acceptedInput = serverInputQueue.Dequeue();
                command = acceptedInput.Command;
            }

            serverState = SimulateWithGroundSupport(serverState, command);
            serverState = ValidateAndClampServerSimulation(previous, serverState);

            if (!serverState.MatchFinished && serverState.Alive && serverState.Health > 0)
            {
                if (hadInput && command.ReloadPressed && weapon != null)
                {
                    if (weapon.ServerTryBeginReload(Time.realtimeSinceStartupAsDouble))
                    {
                        CopyWeaponStateToSnapshot();
                    }
                }

                ReceivedFireCommand receivedFireCommand;
                if (hadInput && command.FirePressed &&
                    pendingFireCommands.TryGetValue(command.Tick, out receivedFireCommand))
                {
                    pendingFireCommands.Remove(command.Tick);
                    double rpcGap = Math.Abs(receivedFireCommand.ReceivedAtServerTime - acceptedInput.ReceivedAtServerTime);
                    if (rpcGap > MaximumUncompensatedFireRpcGapSeconds)
                    {
                        RecordViolation(ServerViolationKind.ClientTickJump, 1.5f,
                            "Fire RPC did not arrive near its matching accepted input tick.");
                    }
                    else
                    {
                        ResolveServerFireCommand(receivedFireCommand.Command, command, acceptedInput);
                    }
                }
            }
            else if (hadInput)
            {
                pendingFireCommands.Remove(command.Tick);
            }

            CopyWeaponStateToSnapshot();
            RemoveStaleFireCommands(serverState.LastProcessedInputTick);
            authoritativeState.Value = serverState;
            RecordServerHistory(Time.realtimeSinceStartupAsDouble);
            if (IsOwner)
            {
                predictedState = serverState;
                ownerInitialized = true;
                lastAcknowledgedInputTick = serverState.LastProcessedInputTick;
                lastAppliedServerTick = serverState.ServerTick;
                hasAppliedServerTick = true;
                ApplyOwnerPose(serverState);
            }
            else
            {
                BufferRemoteSnapshot(serverState);
            }
        }

        private void RegisterWithServerAuthority()
        {
            if (!IsServer || !IsSpawned || !serverInitialized || registeredWithServerAuthority)
            {
                return;
            }
            if (serverAuthority == null)
            {
                serverAuthority = FindObjectOfType<PhaseEightServerAuthority>();
            }
            if (serverAuthority == null)
            {
                return;
            }

            ConfigureLagCompensationHistory(serverAuthority.LagCompensationWindowSeconds);
            serverAuthority.RegisterServerPlayer(this);
            registeredWithServerAuthority = true;
            CombatMatchController matchController = FindObjectOfType<CombatMatchController>();
            if (matchController != null)
            {
                matchController.ServerRegisterParticipant(this);
            }
        }

        private void RecordServerHistory(double serverTimeSeconds)
        {
            if (IsServer && serverInitialized)
            {
                lagCompensationHistory.Record(serverTimeSeconds, serverState);
            }
        }

        private void AdvanceViolationScore()
        {
            float decay = serverAuthority != null
                ? serverAuthority.ViolationScoreDecayPerSecond
                : DefaultViolationScoreDecayPerSecond;
            violationScore.Advance(Time.realtimeSinceStartupAsDouble, decay);
        }

        private void RecordViolation(ServerViolationKind kind, float points, string detail)
        {
            if (!IsServer)
            {
                return;
            }

            double serverTime = Time.realtimeSinceStartupAsDouble;
            float decay = serverAuthority != null
                ? serverAuthority.ViolationScoreDecayPerSecond
                : DefaultViolationScoreDecayPerSecond;
            float previousScore = violationScore.Advance(serverTime, decay);
            float currentScore = violationScore.AddEvidence(kind, points, serverTime, decay);
            float reviewThreshold = serverAuthority != null
                ? serverAuthority.ViolationReviewThreshold
                : 12f;

            if (previousScore < reviewThreshold && currentScore >= reviewThreshold &&
                Time.unscaledTime >= nextViolationReviewTime)
            {
                nextViolationReviewTime = Time.unscaledTime + 10f;
                Debug.LogWarning(
                    "[Phase 8 anti-cheat] Player " + OwnerClientId +
                    " crossed the review-only ViolationScore threshold (" + currentScore.ToString("F1") +
                    "). Latest evidence: " + kind + ". No automatic ban or kick was applied. " + detail,
                    this);
            }
        }

        private NetworkPlayerSnapshot ValidateAndClampServerSimulation(
            NetworkPlayerSnapshot previous,
            NetworkPlayerSnapshot candidate)
        {
            if (!previous.Initialized || !candidate.Initialized)
            {
                return candidate;
            }

            uint simulatedServerTick = candidate.ServerTick;
            uint processedInputTick = candidate.LastProcessedInputTick;
            if (!GeoPosition.IsFinite(candidate.EastMeters, candidate.UpMeters, candidate.NorthMeters))
            {
                RecordViolation(ServerViolationKind.InvalidInputData, 4f,
                    "Authoritative simulation produced a non-finite position; previous state was restored.");
                candidate = previous;
                candidate.ServerTick = simulatedServerTick;
                candidate.LastProcessedInputTick = processedInputTick;
                candidate.VelocityEastMetersPerSecond = 0f;
                candidate.VelocityUpMetersPerSecond = 0f;
                candidate.VelocityNorthMetersPerSecond = 0f;
                return candidate;
            }

            if (!IsFinite(candidate.VelocityEastMetersPerSecond) ||
                !IsFinite(candidate.VelocityUpMetersPerSecond) ||
                !IsFinite(candidate.VelocityNorthMetersPerSecond))
            {
                RecordViolation(ServerViolationKind.InvalidInputData, 3f,
                    "Authoritative simulation produced a non-finite velocity; velocity was reset.");
                candidate.VelocityEastMetersPerSecond = 0f;
                candidate.VelocityUpMetersPerSecond = 0f;
                candidate.VelocityNorthMetersPerSecond = 0f;
            }

            float maximumPlanarSpeed = worldSettings != null
                ? Mathf.Max(0f, worldSettings.SprintSpeedMetersPerSecond) + 0.75f
                : 8f;
            float planarSpeed = Mathf.Sqrt(
                candidate.VelocityEastMetersPerSecond * candidate.VelocityEastMetersPerSecond +
                candidate.VelocityNorthMetersPerSecond * candidate.VelocityNorthMetersPerSecond);
            if (planarSpeed > maximumPlanarSpeed)
            {
                RecordViolation(ServerViolationKind.MovementSpeed, 2f,
                    "Authoritative planar velocity exceeded the configured sprint speed; velocity was clamped.");
                float scale = maximumPlanarSpeed / Mathf.Max(0.0001f, planarSpeed);
                candidate.VelocityEastMetersPerSecond *= scale;
                candidate.VelocityNorthMetersPerSecond *= scale;
            }

            double eastDelta = candidate.EastMeters - previous.EastMeters;
            double northDelta = candidate.NorthMeters - previous.NorthMeters;
            double horizontalDistance = Math.Sqrt(eastDelta * eastDelta + northDelta * northDelta);
            double maximumDisplacement = maximumPlanarSpeed * NetworkPlayerSimulation.TickDeltaSeconds + 0.25;
            if (horizontalDistance > maximumDisplacement)
            {
                RecordViolation(ServerViolationKind.Teleport, 4f,
                    "Authoritative horizontal displacement exceeded the fixed-tick movement envelope.");
                double ratio = maximumDisplacement / Math.Max(0.0001, horizontalDistance);
                candidate.EastMeters = previous.EastMeters + eastDelta * ratio;
                candidate.NorthMeters = previous.NorthMeters + northDelta * ratio;
                candidate.VelocityEastMetersPerSecond = 0f;
                candidate.VelocityNorthMetersPerSecond = 0f;
            }

            float gravity = worldSettings != null
                ? Mathf.Max(0.1f, worldSettings.GravityMetersPerSecondSquared)
                : 9.81f;
            float jumpHeight = worldSettings != null
                ? Mathf.Max(0f, worldSettings.JumpHeightMeters)
                : 1f;
            double maximumVerticalDisplacement =
                (Math.Sqrt(2.0 * gravity * jumpHeight) + 2.0) * NetworkPlayerSimulation.TickDeltaSeconds + 0.25;
            double verticalDelta = candidate.UpMeters - previous.UpMeters;
            if (Math.Abs(verticalDelta) > maximumVerticalDisplacement)
            {
                RecordViolation(ServerViolationKind.Teleport, 3f,
                    "Authoritative vertical displacement exceeded the jump/fall envelope.");
                candidate.UpMeters = previous.UpMeters + Math.Sign(verticalDelta) * maximumVerticalDisplacement;
                candidate.VelocityUpMetersPerSecond = 0f;
            }

            if (gameplayCollisionWorld != null && gameplayCollisionWorld.HasIndependentData && worldSettings != null)
            {
                LocalPosition previousPosition = new LocalPosition(
                    previous.EastMeters, previous.UpMeters, previous.NorthMeters);
                LocalPosition desiredPosition = new LocalPosition(
                    candidate.EastMeters, candidate.UpMeters, candidate.NorthMeters);
                LocalPosition resolvedPosition;
                if (!gameplayCollisionWorld.TryResolveMovement(
                        previousPosition,
                        desiredPosition,
                        worldSettings.CharacterRadiusMeters,
                        candidate.Crouched ? worldSettings.CrouchHeightMeters : worldSettings.CharacterHeightMeters,
                        out resolvedPosition))
                {
                    candidate.EastMeters = resolvedPosition.EastMeters;
                    candidate.UpMeters = resolvedPosition.UpMeters;
                    candidate.NorthMeters = resolvedPosition.NorthMeters;
                    candidate.VelocityEastMetersPerSecond = 0f;
                    candidate.VelocityNorthMetersPerSecond = 0f;
                    if (resolvedPosition.Equals(previousPosition))
                    {
                        candidate.VelocityUpMetersPerSecond = 0f;
                    }
                    candidate.Grounded = previous.Grounded;
                }
            }

            if (!IsFinite(candidate.YawDegrees) || !IsFinite(candidate.PitchDegrees))
            {
                RecordViolation(ServerViolationKind.ImpossibleRotation, 3f,
                    "Authoritative view rotation was non-finite; previous rotation was restored.");
                candidate.YawDegrees = previous.YawDegrees;
                candidate.PitchDegrees = previous.PitchDegrees;
            }
            else
            {
                float maximumRotationThisTick = (serverAuthority != null
                    ? serverAuthority.MaximumRotationDegreesPerSecond
                    : 1080f) * NetworkPlayerSimulation.TickDeltaSeconds + 4f;
                float yawDelta = Mathf.DeltaAngle(previous.YawDegrees, candidate.YawDegrees);
                float pitchDelta = candidate.PitchDegrees - previous.PitchDegrees;
                if (Mathf.Abs(yawDelta) > maximumRotationThisTick ||
                    Mathf.Abs(pitchDelta) > maximumRotationThisTick)
                {
                    RecordViolation(ServerViolationKind.ImpossibleRotation, 1.5f,
                        "Authoritative per-tick rotation exceeded the configured limit.");
                    candidate.YawDegrees = Mathf.Repeat(
                        previous.YawDegrees + Mathf.Clamp(yawDelta, -maximumRotationThisTick, maximumRotationThisTick),
                        360f);
                    candidate.PitchDegrees = Mathf.Clamp(
                        previous.PitchDegrees + Mathf.Clamp(pitchDelta, -maximumRotationThisTick, maximumRotationThisTick),
                        NetworkPlayerSimulation.MinimumPitchDegrees,
                        NetworkPlayerSimulation.MaximumPitchDegrees);
                }
            }

            if (worldManager != null && worldManager.IsArenaConfigured)
            {
                double allowedRadius = Math.Max(0.0,
                    worldManager.GameplayRadiusMeters - (worldSettings != null
                        ? Math.Max(0f, worldSettings.CharacterRadiusMeters)
                        : 0.5f));
                double radius = Math.Sqrt(
                    candidate.EastMeters * candidate.EastMeters +
                    candidate.NorthMeters * candidate.NorthMeters);
                if (radius > allowedRadius + 0.01)
                {
                    RecordViolation(ServerViolationKind.ArenaEscape, 3f,
                        "Authoritative player state exceeded the arena boundary and was projected inside.");
                    double ratio = allowedRadius / Math.Max(0.0001, radius);
                    candidate.EastMeters *= ratio;
                    candidate.NorthMeters *= ratio;
                    candidate.VelocityEastMetersPerSecond = 0f;
                    candidate.VelocityNorthMetersPerSecond = 0f;
                }
            }

            return candidate;
        }


        private FireCommand BuildFireCommand(uint inputTick, float aimYawDegrees, float aimPitchDegrees)
        {
            if (inputTick == 0u || worldManager == null || !worldManager.IsReady ||
                weapon == null || !weapon.IsEquipped || weapon.Definition == null)
            {
                return default(FireCommand);
            }

            Camera viewCamera = Camera.main;
            if (viewCamera == null)
            {
                return default(FireCommand);
            }

            try
            {
                GeoPosition originGeographic = worldManager.UnityWorldToGeographic(viewCamera.transform.position);
                LocalPosition origin = worldManager.GeographicToLocal(originGeographic);
                Vector3 direction = CombatMath.AimDirectionFromYawPitch(aimYawDegrees, aimPitchDegrees);
                if (!CombatMath.TryNormalizeDirection(direction, out direction))
                {
                    return default(FireCommand);
                }

                return new FireCommand
                {
                    Tick = inputTick,
                    OriginEastMeters = origin.EastMeters,
                    OriginUpMeters = origin.UpMeters,
                    OriginNorthMeters = origin.NorthMeters,
                    DirectionEast = direction.x,
                    DirectionUp = direction.y,
                    DirectionNorth = direction.z,
                    WeaponId = new FixedString64Bytes(weapon.Definition.WeaponId)
                };
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Combat] A local fire intent could not be expressed in the session ENU frame (" +
                    exception.GetType().Name + "). No shot was submitted.",
                    this);
                return default(FireCommand);
            }
        }

        private void ResolveServerFireCommand(
            FireCommand command,
            NetworkPlayerInputCommand input,
            QueuedServerInput acceptedInput)
        {
            CombatMatchController matchController = FindObjectOfType<CombatMatchController>();
            if (!IsServer || matchController == null || !matchController.IsServerMatchActive ||
                !serverState.Initialized || !serverState.Alive || serverState.Health == 0 ||
                serverState.MatchFinished || !input.FirePressed)
            {
                return;
            }

            if (command.Tick == 0 || command.Tick != input.Tick ||
                !GeoPosition.IsFinite(command.OriginEastMeters, command.OriginUpMeters, command.OriginNorthMeters))
            {
                RecordViolation(ServerViolationKind.InvalidInputData, 1.5f, "Malformed shot tick or origin.");
                return;
            }
            if (weapon == null || !weapon.IsEquipped || weapon.Definition == null ||
                !weapon.Definition.IsValid ||
                !string.Equals(command.WeaponId.ToString(), weapon.Definition.WeaponId, StringComparison.Ordinal))
            {
                RecordViolation(ServerViolationKind.Ammo, 1.25f, "Shot requested for a missing or non-equipped weapon.");
                return;
            }

            double serverNow = Time.realtimeSinceStartupAsDouble;
            double historyWindow = lagCompensationHistory.HistoryWindowSeconds;
            double rewindSeconds = Math.Min(historyWindow, Math.Max(0.0, acceptedInput.EstimatedOneWayLatencySeconds));
            double shotTime = acceptedInput.ReceivedAtServerTime - rewindSeconds;
            shotTime = Math.Max(serverNow - historyWindow, Math.Min(serverNow, shotTime));

            NetworkPlayerSnapshot shooterAtShot;
            if (!lagCompensationHistory.TrySample(shotTime, out shooterAtShot))
            {
                RecordViolation(ServerViolationKind.ClientTickJump, 1.5f,
                    "Shot time fell outside retained authoritative history.");
                return;
            }
            if (!shooterAtShot.Alive || shooterAtShot.Health == 0 || shooterAtShot.MatchFinished)
            {
                return;
            }

            LocalPosition authoritativeOrigin = new LocalPosition(
                shooterAtShot.EastMeters,
                shooterAtShot.UpMeters + (shooterAtShot.Crouched
                    ? worldSettings.CrouchEyeHeightMeters
                    : worldSettings.FirstPersonEyeHeightMeters),
                shooterAtShot.NorthMeters);
            double originEastDelta = command.OriginEastMeters - authoritativeOrigin.EastMeters;
            double originUpDelta = command.OriginUpMeters - authoritativeOrigin.UpMeters;
            double originNorthDelta = command.OriginNorthMeters - authoritativeOrigin.NorthMeters;
            double originDeviationSquared =
                (originEastDelta * originEastDelta) +
                (originUpDelta * originUpDelta) +
                (originNorthDelta * originNorthDelta);
            if (!GeoPosition.IsFinite(originDeviationSquared) ||
                originDeviationSquared > MaximumFireOriginDeviationMeters * MaximumFireOriginDeviationMeters)
            {
                RecordViolation(ServerViolationKind.ShotOrigin, 3f, "Submitted shot origin disagreed with historical eye position.");
                return;
            }

            Vector3 submittedDirection;
            if (!CombatMath.TryNormalizeDirection(command.Direction, out submittedDirection))
            {
                RecordViolation(ServerViolationKind.ShotDirection, 2f, "Submitted shot direction was non-finite or non-unit.");
                return;
            }
            Vector3 authoritativeDirection = CombatMath.AimDirectionFromYawPitch(
                shooterAtShot.YawDegrees,
                shooterAtShot.PitchDegrees);
            if (!CombatMath.IsDirectionWithinTolerance(
                    submittedDirection,
                    authoritativeDirection,
                    MaximumFireAimDeviationDegrees))
            {
                RecordViolation(ServerViolationKind.ImpossibleRotation, 2f,
                    "Shot direction exceeded the historical server-authored view angle.");
                return;
            }

            WeaponFireFailure fireFailure;
            if (!weapon.ServerTryConsumeRound(acceptedInput.ReceivedAtServerTime, out fireFailure))
            {
                if (fireFailure == WeaponFireFailure.FireRate)
                {
                    RecordViolation(ServerViolationKind.FireRate, 1.5f, "Server-side weapon cooldown rejected a shot.");
                }
                else if (fireFailure == WeaponFireFailure.EmptyMagazine || fireFailure == WeaponFireFailure.Reloading ||
                         fireFailure == WeaponFireFailure.NotEquipped)
                {
                    RecordViolation(ServerViolationKind.Ammo, 0.75f, "Server-side magazine/reload state rejected a shot.");
                }
                return;
            }

            serverState.LastFireInputTick = command.Tick;
            CopyWeaponStateToSnapshot();
            uint spreadSeed = CreateSpreadSeed(OwnerClientId, command.Tick, weapon.Definition.WeaponId);
            Vector3 shotDirection = CombatMath.ApplySpread(
                authoritativeDirection,
                weapon.Definition.SpreadDegrees,
                spreadSeed);

            LocalPosition impactPosition;
            bool hasImpact;
            CombatHitboxZone hitboxZone;
            NetworkPlayer victim = PerformLagCompensatedRaycast(
                authoritativeOrigin,
                shotDirection,
                weapon.Definition.RangeMeters,
                shotTime,
                out impactPosition,
                out hasImpact,
                out hitboxZone);

            bool playerHit = victim != null;
            ushort damage = 0;
            ushort remainingHealth = 0;
            bool killed = false;
            if (victim != null)
            {
                damage = weapon.Definition.DamageForZone(hitboxZone);
                victim.ServerApplyDamage(damage, out remainingHealth, out killed);
                if (!killed && remainingHealth == 0)
                {
                    playerHit = false;
                    damage = 0;
                }
            }

            CombatEvent shotEvent = CreateCombatEvent(
                CombatEventType.ShotResolved,
                command,
                authoritativeOrigin,
                shotDirection,
                impactPosition,
                hasImpact,
                playerHit,
                victim,
                hitboxZone,
                damage,
                remainingHealth,
                killed);
            BroadcastCombatEventClientRpc(shotEvent);

            if (killed && victim != null)
            {
                matchController.ServerAwardKill(OwnerClientId, serverState.DisplayName);
                CombatEvent deathEvent = CreateCombatEvent(
                    CombatEventType.PlayerDied,
                    command,
                    authoritativeOrigin,
                    shotDirection,
                    impactPosition,
                    hasImpact,
                    true,
                    victim,
                    hitboxZone,
                    damage,
                    remainingHealth,
                    true);
                BroadcastCombatEventClientRpc(deathEvent);
                matchController.ServerEvaluateAfterPlayerDeath();
            }
        }

        private NetworkPlayer PerformLagCompensatedRaycast(
            LocalPosition originLocal,
            Vector3 directionEnu,
            float rangeMeters,
            double shotTime,
            out LocalPosition impactLocal,
            out bool hasImpact,
            out CombatHitboxZone selectedZone)
        {
            impactLocal = default(LocalPosition);
            hasImpact = false;
            selectedZone = CombatHitboxZone.None;
            if (worldManager == null || !worldManager.IsReady || rangeMeters <= 0f ||
                !CombatMath.TryNormalizeDirection(directionEnu, out directionEnu))
            {
                return null;
            }

            try
            {
                Vector3 originMeters = new Vector3(
                    (float)originLocal.EastMeters,
                    (float)originLocal.UpMeters,
                    (float)originLocal.NorthMeters);
                float nearestWorldDistance = float.PositiveInfinity;
                RaycastHit nearestWorldHit = default(RaycastHit);
                bool stableGameplayProxyHit = false;
                if (gameplayCollisionWorld != null && gameplayCollisionWorld.HasIndependentData)
                {
                    float proxyDistance;
                    GameplayFeatureKind ignoredKind;
                    string ignoredFeatureId;
                    if (gameplayCollisionWorld.TryRaycast(
                            originLocal,
                            directionEnu,
                            rangeMeters,
                            out proxyDistance,
                            out ignoredKind,
                            out ignoredFeatureId))
                    {
                        nearestWorldDistance = proxyDistance;
                        stableGameplayProxyHit = true;
                    }
                }
                else if (gameplayCollisionWorld != null && gameplayCollisionWorld.IsPrototypeFallback)
                {
                    // Explicitly prototype-only: visual-tile colliders may be queried here, but are
                    // never presented as stable/approved gameplay feature data.
                    Vector3 originWorld = worldManager.GeographicToUnityWorld(
                        worldManager.LocalToGeographic(originLocal));
                    Vector3 directionWorld = worldManager.LocalDirectionToUnityWorld(originLocal, directionEnu).normalized;
                    Physics.SyncTransforms();
                    int hitCount = Physics.RaycastNonAlloc(
                        new Ray(originWorld, directionWorld),
                        worldRaycastHits,
                        rangeMeters,
                        Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < hitCount; i++)
                    {
                        RaycastHit candidate = worldRaycastHits[i];
                        Collider collider = candidate.collider;
                        if (collider == null || collider.GetComponentInParent<NetworkPlayer>() != null ||
                            collider.GetComponentInParent<NetworkPlayerHitbox>() != null ||
                            candidate.distance >= nearestWorldDistance)
                        {
                            continue;
                        }
                        nearestWorldDistance = candidate.distance;
                        nearestWorldHit = candidate;
                    }
                }

                NetworkPlayer nearestPlayer = null;
                float nearestPlayerDistance = rangeMeters;
                CombatHitboxZone nearestZone = CombatHitboxZone.None;
                int candidateCount = serverAuthority != null
                    ? serverAuthority.RegisteredPlayerCount
                    : (NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsList.Count : 0);
                for (int i = 0; i < candidateCount; i++)
                {
                    NetworkPlayer candidatePlayer;
                    if (serverAuthority != null)
                    {
                        candidatePlayer = serverAuthority.GetServerPlayerByIndex(i);
                    }
                    else
                    {
                        NetworkClient client = NetworkManager.Singleton.ConnectedClientsList[i];
                        NetworkObject playerObject = client != null ? client.PlayerObject : null;
                        candidatePlayer = playerObject != null ? playerObject.GetComponent<NetworkPlayer>() : null;
                    }

                    if (candidatePlayer == null || candidatePlayer == this || !candidatePlayer.IsSpawned)
                    {
                        continue;
                    }

                    NetworkPlayerSnapshot historicalTarget;
                    if (!candidatePlayer.TryGetHistoricalSnapshot(shotTime, out historicalTarget))
                    {
                        continue;
                    }

                    float candidateDistance;
                    CombatHitboxZone candidateZone;
                    if (LagCompensationMath.TryIntersectPlayer(
                            originMeters,
                            directionEnu,
                            rangeMeters,
                            historicalTarget,
                            worldSettings.CharacterHeightMeters,
                            out candidateDistance,
                            out candidateZone) && candidateDistance < nearestPlayerDistance)
                    {
                        nearestPlayerDistance = candidateDistance;
                        nearestPlayer = candidatePlayer;
                        nearestZone = candidateZone;
                    }
                }

                if (nearestPlayer != null && nearestPlayerDistance <= nearestWorldDistance)
                {
                    impactLocal = new LocalPosition(
                        originLocal.EastMeters + directionEnu.x * nearestPlayerDistance,
                        originLocal.UpMeters + directionEnu.y * nearestPlayerDistance,
                        originLocal.NorthMeters + directionEnu.z * nearestPlayerDistance);
                    hasImpact = true;
                    selectedZone = nearestZone;
                    return nearestPlayer;
                }

                if (nearestWorldDistance < float.PositiveInfinity)
                {
                    if (stableGameplayProxyHit)
                    {
                        impactLocal = new LocalPosition(
                            originLocal.EastMeters + directionEnu.x * nearestWorldDistance,
                            originLocal.UpMeters + directionEnu.y * nearestWorldDistance,
                            originLocal.NorthMeters + directionEnu.z * nearestWorldDistance);
                    }
                    else
                    {
                        impactLocal = worldManager.GeographicToLocal(
                            worldManager.UnityWorldToGeographic(nearestWorldHit.point));
                    }
                    hasImpact = true;
                }
                return null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Combat] Historical server hit detection failed safely (" + exception.GetType().Name + "). The shot did no damage.",
                    this);
                return null;
            }
        }

        private void ServerApplyDamage(ushort damage, out ushort remainingHealth, out bool killed)
        {
            remainingHealth = serverState.Health;
            killed = false;
            if (!IsServer || !serverInitialized || !serverState.Alive || serverState.Health == 0 ||
                serverState.MatchFinished || damage == 0)
            {
                return;
            }

            int health = Mathf.Max(0, serverState.Health - damage);
            serverState.Health = (ushort)health;
            remainingHealth = serverState.Health;
            if (serverState.Health == 0)
            {
                serverState.Alive = false;
                serverState.MovementState = NetworkPlayerMovementState.Dead;
                serverState.VelocityEastMetersPerSecond = 0f;
                serverState.VelocityUpMetersPerSecond = 0f;
                serverState.VelocityNorthMetersPerSecond = 0f;
                serverInputQueue.Clear();
                pendingFireCommands.Clear();
                killed = true;
            }

            authoritativeState.Value = serverState;
            RecordServerHistory(Time.realtimeSinceStartupAsDouble);
        }

        private CombatEvent CreateCombatEvent(
            CombatEventType eventType,
            FireCommand command,
            LocalPosition origin,
            Vector3 direction,
            LocalPosition impact,
            bool hasImpact,
            bool playerHit,
            NetworkPlayer victim,
            CombatHitboxZone zone,
            ushort damage,
            ushort remainingHealth,
            bool killed)
        {
            return new CombatEvent
            {
                Type = eventType,
                FireTick = command.Tick,
                ShooterId = OwnerClientId,
                VictimId = victim != null ? victim.OwnerClientId : 0UL,
                Hit = playerHit,
                Killed = killed,
                HasImpact = hasImpact,
                HitboxZone = zone,
                Damage = damage,
                RemainingHealth = remainingHealth,
                WeaponId = weapon != null && weapon.Definition != null
                    ? new FixedString64Bytes(weapon.Definition.WeaponId)
                    : default(FixedString64Bytes),
                ShooterName = serverState.DisplayName,
                VictimName = victim != null ? victim.serverState.DisplayName : default(FixedString64Bytes),
                OriginEastMeters = origin.EastMeters,
                OriginUpMeters = origin.UpMeters,
                OriginNorthMeters = origin.NorthMeters,
                ImpactEastMeters = impact.EastMeters,
                ImpactUpMeters = impact.UpMeters,
                ImpactNorthMeters = impact.NorthMeters,
                DirectionEast = direction.x,
                DirectionUp = direction.y,
                DirectionNorth = direction.z
            };
        }

        private static uint CreateSpreadSeed(ulong ownerClientId, uint tick, string weaponId)
        {
            uint hash = unchecked((uint)(ownerClientId ^ (ownerClientId >> 32))) ^ tick ^ 0x85EBCA6Bu;
            if (weaponId != null)
            {
                for (int i = 0; i < weaponId.Length; i++)
                {
                    hash = (hash * 16777619u) ^ weaponId[i];
                }
            }
            return hash == 0u ? 1u : hash;
        }

        private void CopyWeaponStateToSnapshot()
        {
            if (weapon == null || weapon.Definition == null || !weapon.IsEquipped)
            {
                serverState.EquippedWeaponId = default(FixedString64Bytes);
                serverState.MagazineAmmo = 0;
                serverState.IsReloading = false;
                return;
            }

            serverState.EquippedWeaponId = new FixedString64Bytes(weapon.Definition.WeaponId);
            serverState.MagazineAmmo = weapon.MagazineAmmo;
            serverState.IsReloading = weapon.IsReloading;
        }

        private void RemoveStaleFireCommands(uint processedInputTick)
        {
            if (pendingFireCommands.Count == 0)
            {
                return;
            }

            List<uint> stale = null;
            foreach (KeyValuePair<uint, ReceivedFireCommand> entry in pendingFireCommands)
            {
                if (entry.Key == processedInputTick || IsSequenceNewer(processedInputTick, entry.Key))
                {
                    if (stale == null)
                    {
                        stale = new List<uint>();
                    }
                    stale.Add(entry.Key);
                }
            }
            if (stale == null)
            {
                return;
            }
            for (int i = 0; i < stale.Count; i++)
            {
                pendingFireCommands.Remove(stale[i]);
            }
        }

        internal void ServerSetMatchFinished()
        {
            if (!IsServer || !serverInitialized || serverState.MatchFinished)
            {
                return;
            }

            serverState.MatchFinished = true;
            if (serverState.Alive && serverState.Health > 0)
            {
                serverState.MovementState = NetworkPlayerMovementState.MatchOver;
            }
            serverState.VelocityEastMetersPerSecond = 0f;
            serverState.VelocityUpMetersPerSecond = 0f;
            serverState.VelocityNorthMetersPerSecond = 0f;
            serverInputQueue.Clear();
            pendingFireCommands.Clear();
            authoritativeState.Value = serverState;
            RecordServerHistory(Time.realtimeSinceStartupAsDouble);
        }

        internal void BroadcastCombatEvent(CombatEvent combatEvent)
        {
            if (IsServer && IsSpawned)
            {
                BroadcastCombatEventClientRpc(combatEvent);
            }
        }

        [ClientRpc]
        private void BroadcastCombatEventClientRpc(CombatEvent combatEvent)
        {
            Action<CombatEvent> handler = CombatEventReceived;
            if (handler != null)
            {
                handler.Invoke(combatEvent);
            }
        }

        private NetworkPlayerSnapshot Simulate(
            NetworkPlayerSnapshot state,
            NetworkPlayerInputCommand command)
        {
            bool canStand = CanStandAfterCrouch(state, command);
            return NetworkPlayerSimulation.Step(
                state,
                command,
                NetworkPlayerSimulation.TickDeltaSeconds,
                worldSettings.WalkSpeedMetersPerSecond,
                worldSettings.SprintSpeedMetersPerSecond,
                worldSettings.CrouchSpeedMetersPerSecond,
                worldSettings.JumpHeightMeters,
                worldSettings.GravityMetersPerSecondSquared,
                worldSettings.GroundAccelerationMetersPerSecondSquared,
                worldSettings.GroundDecelerationMetersPerSecondSquared,
                worldSettings.AirAccelerationMetersPerSecondSquared,
                worldSettings.AirDecelerationMetersPerSecondSquared,
                worldSettings.MaximumSlopeDegrees,
                canStand,
                GetLandingPresentationTicks(),
                worldManager.GameplayRadiusMeters,
                worldSettings.CharacterRadiusMeters);
        }

        private NetworkPlayerSnapshot SimulateWithGroundSupport(
            NetworkPlayerSnapshot previous,
            NetworkPlayerInputCommand command)
        {
            NetworkPlayerSnapshot next = Simulate(previous, command);
            if (worldManager == null || worldSettings == null ||
                Time.unscaledTime < nextGroundSampleTime)
            {
                return next;
            }

            nextGroundSampleTime = Time.unscaledTime + worldSettings.TerrainSampleIntervalSeconds;
            return ResolveGroundSupport(previous, next);
        }

        private bool CanStandAfterCrouch(
            NetworkPlayerSnapshot state,
            NetworkPlayerInputCommand command)
        {
            if (!state.Crouched || command.Crouch || worldManager == null || worldSettings == null ||
                !worldManager.IsReady)
            {
                return true;
            }

            LocalPosition localFoot = new LocalPosition(
                state.EastMeters,
                state.UpMeters,
                state.NorthMeters);
            if (gameplayCollisionWorld != null)
            {
                return gameplayCollisionWorld.CanFitCapsule(
                    localFoot,
                    worldSettings.CharacterRadiusMeters,
                    worldSettings.CrouchHeightMeters,
                    worldSettings.CharacterHeightMeters);
            }

            GeoPosition footPosition = worldManager.LocalToGeographic(localFoot);
            return worldManager.CanFitPlayerCapsule(
                footPosition,
                worldSettings.CharacterRadiusMeters,
                worldSettings.CrouchHeightMeters,
                worldSettings.CharacterHeightMeters);
        }

        private NetworkPlayerSnapshot ResolveGroundSupport(
            NetworkPlayerSnapshot previous,
            NetworkPlayerSnapshot candidate)
        {
            if (!candidate.Initialized || worldManager == null || worldSettings == null)
            {
                return candidate;
            }

            GeoPosition current = worldManager.LocalToGeographic(new LocalPosition(
                candidate.EastMeters,
                candidate.UpMeters,
                candidate.NorthMeters));
            double groundAltitude;
            Vector3 groundNormal;
            if (!worldManager.TryGetGroundSurface(current, out groundAltitude, out groundNormal))
            {
                // Missing streamed collision is not interpreted as a flat terrain sample.
                return candidate;
            }

            GeoPosition ground = new GeoPosition(
                current.LatitudeDegrees,
                current.LongitudeDegrees,
                groundAltitude + worldSettings.GroundSpawnClearanceMeters);
            LocalPosition groundLocal = worldManager.GeographicToLocal(ground);
            double sampledGroundUp = groundLocal.UpMeters;
            float heightDelta = (float)(sampledGroundUp - previous.GroundLevelUpMeters);
            float horizontalDistance = Mathf.Sqrt(
                Mathf.Pow((float)(candidate.EastMeters - previous.EastMeters), 2f) +
                Mathf.Pow((float)(candidate.NorthMeters - previous.NorthMeters), 2f));
            float slopeAngle = Vector3.Angle(groundNormal, Vector3.up);
            float pathSlopeAngle = horizontalDistance > 0.001f
                ? Mathf.Atan2(Mathf.Abs(heightDelta), horizontalDistance) * Mathf.Rad2Deg
                : slopeAngle;
            float maxStep = worldSettings.StepOffsetMeters;
            float snapDistance = worldSettings.GroundSnapDistanceMeters;

            candidate.GroundNormalEast = groundNormal.x;
            candidate.GroundNormalUp = groundNormal.y;
            candidate.GroundNormalNorth = groundNormal.z;
            candidate.GroundLevelUpMeters = sampledGroundUp;

            if (previous.Grounded && candidate.Grounded)
            {
                bool stepTooHigh = heightDelta > maxStep &&
                                   pathSlopeAngle > worldSettings.MaximumSlopeDegrees + 1f;
                bool steepTerrain = slopeAngle > worldSettings.MaximumSlopeDegrees &&
                                    Mathf.Abs(heightDelta) > snapDistance;
                if ((stepTooHigh || steepTerrain) && heightDelta >= 0f)
                {
                    candidate.EastMeters = previous.EastMeters;
                    candidate.UpMeters = previous.UpMeters;
                    candidate.NorthMeters = previous.NorthMeters;
                    candidate.GroundLevelUpMeters = previous.GroundLevelUpMeters;
                    candidate.GroundNormalEast = previous.GroundNormalEast;
                    candidate.GroundNormalUp = previous.GroundNormalUp;
                    candidate.GroundNormalNorth = previous.GroundNormalNorth;
                    candidate.VelocityEastMetersPerSecond = 0f;
                    candidate.VelocityUpMetersPerSecond = 0f;
                    candidate.VelocityNorthMetersPerSecond = 0f;
                    candidate.Grounded = true;
                    candidate.MovementState = candidate.Crouched
                        ? NetworkPlayerMovementState.Crouching
                        : NetworkPlayerMovementState.Idle;
                    return candidate;
                }

                if (heightDelta < -maxStep)
                {
                    // A drop beyond step height is a fall; the host still owns the landing sample.
                    candidate.Grounded = false;
                    candidate.VelocityUpMetersPerSecond = 0f;
                    candidate.MovementState = NetworkPlayerMovementState.Falling;
                    return candidate;
                }

                candidate.UpMeters = sampledGroundUp;
                candidate.Grounded = true;
                candidate.VelocityUpMetersPerSecond = horizontalDistance > 0.001f
                    ? (float)((sampledGroundUp - previous.UpMeters) /
                        NetworkPlayerSimulation.TickDeltaSeconds)
                    : 0f;
                return candidate;
            }

            if (candidate.VelocityUpMetersPerSecond <= 0f &&
                candidate.UpMeters <= sampledGroundUp + snapDistance)
            {
                bool landed = !previous.Grounded;
                candidate.UpMeters = sampledGroundUp;
                candidate.VelocityUpMetersPerSecond = 0f;
                candidate.Grounded = true;
                if (landed)
                {
                    candidate.LandingTicksRemaining = GetLandingPresentationTicks();
                    candidate.MovementState = candidate.LandingTicksRemaining > 0
                        ? NetworkPlayerMovementState.Landing
                        : NetworkPlayerMovementState.Idle;
                }
            }
            else
            {
                candidate.Grounded = false;
            }
            return candidate;
        }

        private byte GetLandingPresentationTicks()
        {
            if (worldSettings == null || worldSettings.LandingPresentationDurationSeconds <= 0f)
            {
                return 0;
            }
            int ticks = Mathf.CeilToInt(
                worldSettings.LandingPresentationDurationSeconds * NetworkPlayer.ServerSimulationTickRate);
            return (byte)Mathf.Clamp(ticks, 0, byte.MaxValue);
        }

        private void AcceptServerInput(NetworkPlayerInputCommand command)
        {
            if (!IsServer || !serverInitialized || serverState.MatchFinished ||
                !serverState.Alive || serverState.Health == 0)
            {
                return;
            }
            if (command.Tick == 0 || !IsSequenceNewer(command.Tick, lastReceivedInputTick))
            {
                RecordViolation(ServerViolationKind.InvalidPacketSequence, 1.5f,
                    "Movement input carried a duplicate, stale, or zero sequence.");
                return;
            }

            uint ticksAhead = unchecked(command.Tick - lastReceivedInputTick);
            int maximumFutureTicks = serverAuthority != null
                ? serverAuthority.MaximumFutureClientTicks
                : 8;
            if (ticksAhead > (uint)maximumFutureTicks)
            {
                RecordViolation(ServerViolationKind.ClientTickJump, 2f,
                    "Client input tick jumped beyond the server's bounded future window.");
                return;
            }

            int maximumQueuedInputs = serverAuthority != null
                ? Math.Min(MaximumBufferedInputs, serverAuthority.MaximumQueuedInputs)
                : 96;
            if (serverInputQueue.Count >= maximumQueuedInputs)
            {
                RecordViolation(ServerViolationKind.InputRate, 1f, "Server input queue reached its configured bound.");
                return;
            }

            float now = Time.unscaledTime;
            float elapsed = Mathf.Max(0f, now - lastInputRateRefillTime);
            availableInputRateTokens = Mathf.Min(
                InputRateBurstTokens,
                availableInputRateTokens + elapsed * ServerSimulationTickRate);
            lastInputRateRefillTime = now;
            if (availableInputRateTokens < 1f)
            {
                RecordViolation(ServerViolationKind.InputRate, 0.75f,
                    "Movement input exceeded the server receive token bucket.");
                return;
            }

            float maximumLookDelta = (serverAuthority != null
                ? serverAuthority.MaximumRotationDegreesPerSecond
                : 1080f) * NetworkPlayerSimulation.TickDeltaSeconds + 4f;
            bool invalidInput = false;
            if (!IsFinite(command.MoveX))
            {
                command.MoveX = 0f;
                invalidInput = true;
            }
            else
            {
                if (Mathf.Abs(command.MoveX) > 1.05f) invalidInput = true;
                command.MoveX = Mathf.Clamp(command.MoveX, -1f, 1f);
            }
            if (!IsFinite(command.MoveY))
            {
                command.MoveY = 0f;
                invalidInput = true;
            }
            else
            {
                if (Mathf.Abs(command.MoveY) > 1.05f) invalidInput = true;
                command.MoveY = Mathf.Clamp(command.MoveY, -1f, 1f);
            }
            if (!IsFinite(command.LookX))
            {
                command.LookX = 0f;
                invalidInput = true;
            }
            if (!IsFinite(command.LookY))
            {
                command.LookY = 0f;
                invalidInput = true;
            }
            if (invalidInput)
            {
                RecordViolation(ServerViolationKind.InvalidInputData, 1f,
                    "Movement input contained non-finite or out-of-range values; axes were sanitized.");
            }

            bool rotationClamped = Mathf.Abs(command.LookX) > maximumLookDelta ||
                                   Mathf.Abs(command.LookY) > maximumLookDelta;
            command.LookX = Mathf.Clamp(command.LookX, -maximumLookDelta, maximumLookDelta);
            command.LookY = Mathf.Clamp(command.LookY, -maximumLookDelta, maximumLookDelta);
            Vector2 lookDelta = new Vector2(command.LookX, command.LookY);
            float lookMagnitude = lookDelta.magnitude;
            if (lookMagnitude > maximumLookDelta)
            {
                rotationClamped = true;
                lookDelta *= maximumLookDelta / Mathf.Max(0.0001f, lookMagnitude);
                command.LookX = lookDelta.x;
                command.LookY = lookDelta.y;
            }
            if (rotationClamped)
            {
                RecordViolation(ServerViolationKind.ImpossibleRotation, 1.5f,
                    "Per-tick view rotation exceeded the configured server limit; delta was clamped.");
            }

            availableInputRateTokens -= 1f;
            lastReceivedInputTick = command.Tick;
            double receivedAt = Time.realtimeSinceStartupAsDouble;
            float oneWayLatency = EstimateOneWayLatencySeconds(OwnerClientId);
            serverInputQueue.Enqueue(new QueuedServerInput(command, receivedAt, oneWayLatency));
        }

        [ServerRpc(RequireOwnership = true)]
        private void SubmitInputCommandServerRpc(NetworkPlayerInputCommand command)
        {
            AcceptServerInput(command);
        }

        [ServerRpc(RequireOwnership = true)]
        private void SubmitFireCommandServerRpc(FireCommand command, ServerRpcParams rpcParams = default(ServerRpcParams))
        {
            AcceptServerFireCommand(command, rpcParams.Receive.SenderClientId);
        }

        private void AcceptServerFireCommand(FireCommand command, ulong senderClientId)
        {
            if (!IsServer || !IsSpawned || !serverInitialized)
            {
                return;
            }
            if (senderClientId != OwnerClientId)
            {
                RecordViolation(ServerViolationKind.InvalidPacketSequence, 2f,
                    "Fire RPC sender did not own this player object.");
                return;
            }
            if (serverState.MatchFinished || !serverState.Alive || serverState.Health == 0)
            {
                return;
            }
            if (command.Tick == 0 || !GeoPosition.IsFinite(
                    command.OriginEastMeters,
                    command.OriginUpMeters,
                    command.OriginNorthMeters))
            {
                RecordViolation(ServerViolationKind.ShotOrigin, 2f,
                    "Fire RPC contained a zero tick or non-finite origin.");
                return;
            }

            Vector3 normalizedDirection;
            if (!CombatMath.TryNormalizeDirection(command.Direction, out normalizedDirection))
            {
                RecordViolation(ServerViolationKind.ShotDirection, 2f,
                    "Fire RPC direction was non-finite or outside the unit-vector tolerance.");
                return;
            }
            if (weapon == null || !weapon.IsEquipped || weapon.Definition == null ||
                !weapon.Definition.IsValid ||
                !string.Equals(command.WeaponId.ToString(), weapon.Definition.WeaponId, StringComparison.Ordinal))
            {
                RecordViolation(ServerViolationKind.Ammo, 1.25f,
                    "Fire RPC selected a weapon the authoritative player does not have equipped.");
                return;
            }
            if (!IsSequenceNewer(command.Tick, serverState.LastProcessedInputTick))
            {
                RecordViolation(ServerViolationKind.InvalidPacketSequence, 1.5f,
                    "Fire RPC sequence was already processed or was stale.");
                return;
            }
            if (pendingFireCommands.ContainsKey(command.Tick))
            {
                RecordViolation(ServerViolationKind.InvalidPacketSequence, 1f,
                    "Duplicate fire RPC sequence was received.");
                return;
            }
            if (pendingFireCommands.Count >= MaximumPendingFireCommands)
            {
                RecordViolation(ServerViolationKind.InputRate, 1f,
                    "Pending fire RPC table reached its configured bound.");
                return;
            }

            bool hasMatchingQueuedInput = false;
            double matchingInputReceivedAt = 0.0;
            foreach (QueuedServerInput queued in serverInputQueue)
            {
                if (queued.Command.Tick == command.Tick && queued.Command.FirePressed)
                {
                    hasMatchingQueuedInput = true;
                    matchingInputReceivedAt = queued.ReceivedAtServerTime;
                    break;
                }
            }

            uint nextExpectedInputTick = unchecked(lastReceivedInputTick + 1u);
            if (nextExpectedInputTick == 0u)
            {
                nextExpectedInputTick = 1u;
            }
            if (!hasMatchingQueuedInput && command.Tick != nextExpectedInputTick)
            {
                RecordViolation(ServerViolationKind.InvalidPacketSequence, 1.5f,
                    "Fire RPC did not match a queued or next expected input tick.");
                return;
            }

            double receivedAt = Time.realtimeSinceStartupAsDouble;
            if (hasMatchingQueuedInput &&
                Math.Abs(receivedAt - matchingInputReceivedAt) > MaximumUncompensatedFireRpcGapSeconds)
            {
                RecordViolation(ServerViolationKind.ClientTickJump, 1.5f,
                    "Fire RPC arrived too far from its matching movement input.");
                return;
            }
            if (!TryConsumeServerFireIntentToken(receivedAt))
            {
                return;
            }

            pendingFireCommands.Add(command.Tick, new ReceivedFireCommand(command, receivedAt));
        }

        private bool TryConsumeServerFireIntentToken(double serverTime)
        {
            float fireRate = weapon != null && weapon.Definition != null
                ? weapon.Definition.FireRateShotsPerSecond
                : 1f;
            if (!IsFinite(fireRate) || fireRate <= 0f)
            {
                RecordViolation(ServerViolationKind.FireRate, 2f,
                    "Authoritative weapon fire rate configuration is invalid.");
                return false;
            }

            double elapsed = Math.Max(0.0, serverTime - lastFireIntentRefillAt);
            availableFireIntentTokens = Mathf.Min(
                InputRateBurstTokens,
                availableFireIntentTokens + (float)elapsed * fireRate * 1.25f);
            lastFireIntentRefillAt = serverTime;
            if (availableFireIntentTokens < 1f)
            {
                RecordViolation(ServerViolationKind.FireRate, 1f,
                    "Fire intent RPCs exceeded the server-side rate token bucket.");
                return false;
            }
            availableFireIntentTokens -= 1f;
            return true;
        }

        private float EstimateOneWayLatencySeconds(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.NetworkConfig == null || manager.NetworkConfig.NetworkTransport == null)
            {
                return 0f;
            }

            try
            {
                ulong roundTripMilliseconds = manager.NetworkConfig.NetworkTransport.GetCurrentRtt(clientId);
                float oneWaySeconds = (float)(roundTripMilliseconds / 2000.0);
                return Mathf.Clamp(oneWaySeconds, 0f, lagCompensationHistory.HistoryWindowSeconds);
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        private void UpdateMapUsageReporting()
        {
            if (mapUsageTracker == null || !mapUsageTracker.HasActiveMatchScope ||
                Time.unscaledTime < nextMapUsageReportTime)
            {
                return;
            }

            nextMapUsageReportTime = Time.unscaledTime + MapUsageReportIntervalSeconds;
            MapUsageTracker.UsageCounters counters = mapUsageTracker.GetLocalCounters();
            if (IsServer)
            {
                // The host can update its local aggregate directly; client reports below are
                // non-authoritative, self-reported telemetry and are never movement state.
                mapUsageTracker.RecordPlayerUsageSnapshot(OwnerClientId, counters, true);
                return;
            }

            if (!IsClient)
            {
                return;
            }

            // Keep the local browser's HUD scoped to this player's own telemetry; the host's
            // aggregate remains on the host and is not implicitly presented as a shared feed.
            mapUsageTracker.RecordPlayerUsageSnapshot(OwnerClientId, counters, true);

            unchecked
            {
                nextMapUsageSequence++;
                if (nextMapUsageSequence == 0)
                {
                    nextMapUsageSequence = 1;
                }
            }
            SubmitMapUsageSnapshotServerRpc(
                nextMapUsageSequence,
                counters.RootTilesetLoadAttempts,
                counters.BrowserObservedRootRequestStarts,
                counters.BrowserObservedRendererRequestStarts,
                counters.CesiumTileGameObjectCreations,
                counters.BrowserTransferBytes,
                counters.BrowserTransferSizedEntries,
                counters.BrowserObservedMapResourceEntries,
                counters.SessionMilliseconds);
        }

        [ServerRpc(RequireOwnership = true)]
        private void SubmitMapUsageSnapshotServerRpc(
            uint sequence,
            long rootTilesetLoadAttempts,
            long browserRootRequestStarts,
            long browserRendererRequestStarts,
            long cesiumTileGameObjectCreations,
            long browserTransferBytes,
            long browserTransferSizedEntries,
            long browserObservedMapResourceEntries,
            long sessionMilliseconds)
        {
            if (!IsServer || mapUsageTracker == null || !mapUsageTracker.HasActiveMatchScope ||
                (hasAcceptedMapUsageSequence && !IsSequenceNewer(sequence, lastAcceptedMapUsageSequence)) ||
                Time.unscaledTime < nextAcceptedMapUsageReportTime)
            {
                return;
            }

            // Refuse absurd telemetry totals to avoid overflow or a misleading host-side dashboard.
            // This does not make self-reported usage trustworthy or control Google's billing.
            if (rootTilesetLoadAttempts > 100000L || browserRootRequestStarts > 100000L ||
                browserRendererRequestStarts > 10000000L || cesiumTileGameObjectCreations > 10000000L ||
                browserTransferBytes > 1099511627776L || browserTransferSizedEntries > 10000000L ||
                browserObservedMapResourceEntries > 10000000L || sessionMilliseconds > 604800000L)
            {
                return;
            }

            MapUsageTracker.UsageCounters previous;
            if (mapUsageTracker.TryGetPlayerUsageSnapshot(OwnerClientId, out previous) &&
                (rootTilesetLoadAttempts < previous.RootTilesetLoadAttempts ||
                 browserRootRequestStarts < previous.BrowserObservedRootRequestStarts ||
                 browserRendererRequestStarts < previous.BrowserObservedRendererRequestStarts ||
                 cesiumTileGameObjectCreations < previous.CesiumTileGameObjectCreations ||
                 browserTransferBytes < previous.BrowserTransferBytes ||
                 sessionMilliseconds < previous.SessionMilliseconds))
            {
                return;
            }

            nextAcceptedMapUsageReportTime = Time.unscaledTime + MinimumMapUsageReportIntervalSeconds;
            lastAcceptedMapUsageSequence = sequence;
            hasAcceptedMapUsageSequence = true;
            mapUsageTracker.RecordPlayerUsageSnapshot(
                OwnerClientId,
                new MapUsageTracker.UsageCounters
                {
                    RootTilesetLoadAttempts = rootTilesetLoadAttempts,
                    BrowserObservedRootRequestStarts = browserRootRequestStarts,
                    BrowserObservedRendererRequestStarts = browserRendererRequestStarts,
                    CesiumTileGameObjectCreations = cesiumTileGameObjectCreations,
                    BrowserTransferBytes = browserTransferBytes,
                    BrowserTransferSizedEntries = browserTransferSizedEntries,
                    BrowserObservedMapResourceEntries = browserObservedMapResourceEntries,
                    SessionMilliseconds = sessionMilliseconds
                },
                false);
        }

        private void SampleOwnerInputAndPredict(float frameDeltaSeconds)
        {
            if (!ownerInitialized || !IsOwner || localMotor == null || worldManager == null || !worldManager.IsReady)
            {
                return;
            }

            if (!predictedState.Alive || predictedState.Health == 0 || predictedState.MatchFinished)
            {
                latestMoveX = 0f;
                latestMoveY = 0f;
                latestSprint = false;
                latestCrouch = false;
                latestFireHeld = false;
                bufferedLookX = 0f;
                bufferedLookY = 0f;
                bufferedJump = false;
                bufferedFire = false;
                bufferedReload = false;
                ownerAccumulator = 0f;
                localMotor.SetInputEnabled(false);
                return;
            }

            if (!localMotor.InputEnabled)
            {
                latestMoveX = 0f;
                latestMoveY = 0f;
                latestSprint = false;
                latestCrouch = false;
                latestFireHeld = false;
                bufferedLookX = 0f;
                bufferedLookY = 0f;
                bufferedJump = false;
                bufferedFire = false;
                bufferedReload = false;
                ownerAccumulator = 0f;
                localMotor.SetNetworkViewAngles(predictedState.YawDegrees, predictedState.PitchDegrees);
                ApplyOwnerPose(predictedState);
                return;
            }

            SampleInputAtRenderRate();
            localMotor.SetNetworkViewAngles(
                predictedState.YawDegrees + bufferedLookX,
                predictedState.PitchDegrees + bufferedLookY);

            float clampedDelta = IsFinite(frameDeltaSeconds)
                ? Mathf.Clamp(frameDeltaSeconds, 0f, MaximumAccumulatorSeconds)
                : 0f;
            ownerAccumulator = Mathf.Min(ownerAccumulator + clampedDelta, MaximumAccumulatorSeconds);
            int ticksDue = Mathf.Min(
                Mathf.FloorToInt(ownerAccumulator / NetworkPlayerSimulation.TickDeltaSeconds),
                MaximumCatchUpTicksPerFrame);
            if (ticksDue <= 0)
            {
                if (!IsServer)
                {
                    ApplyOwnerRenderPose(ownerAccumulator);
                }
                return;
            }

            float lookXPerTick = bufferedLookX / ticksDue;
            float lookYPerTick = bufferedLookY / ticksDue;
            bool jumpForFirstTick = bufferedJump;
            bool reloadForFirstTick = bufferedReload;
            bool hasClientAmmo = weapon != null && predictedState.MagazineAmmo > 0 &&
                                 !predictedState.IsReloading;
            if (reloadForFirstTick || !hasClientAmmo)
            {
                bufferedFire = false;
            }
            bool fireForFirstTick = !reloadForFirstTick && hasClientAmmo &&
                                    (bufferedFire || latestFireHeld) &&
                                    weapon.ShouldRequestShot(Time.unscaledTime);
            bufferedLookX = 0f;
            bufferedLookY = 0f;
            bufferedJump = false;
            bufferedReload = false;
            if (fireForFirstTick)
            {
                bufferedFire = false;
            }

            for (int i = 0; i < ticksDue; i++)
            {
                nextOwnerInputTick = unchecked(nextOwnerInputTick + 1);
                if (nextOwnerInputTick == 0)
                {
                    nextOwnerInputTick = 1;
                }

                NetworkPlayerInputCommand command = new NetworkPlayerInputCommand
                {
                    Tick = nextOwnerInputTick,
                    MoveX = latestMoveX,
                    MoveY = latestMoveY,
                    LookX = lookXPerTick,
                    LookY = lookYPerTick,
                    Sprint = latestSprint,
                    Crouch = latestCrouch,
                    JumpPressed = i == 0 && jumpForFirstTick,
                    FirePressed = i == 0 && fireForFirstTick,
                    ReloadPressed = i == 0 && reloadForFirstTick
                };
                command = ClampOwnerCommand(command);

                if (IsServer)
                {
                    // Host input follows the same command validation, but has no network round trip.
                    if (command.FirePressed)
                    {
                        float expectedYaw = Mathf.Repeat(serverState.YawDegrees + command.LookX, 360f);
                        float expectedPitch = Mathf.Clamp(
                            serverState.PitchDegrees + command.LookY,
                            NetworkPlayerSimulation.MinimumPitchDegrees,
                            NetworkPlayerSimulation.MaximumPitchDegrees);
                        AcceptServerFireCommand(
                            BuildFireCommand(command.Tick, expectedYaw, expectedPitch),
                            OwnerClientId);
                    }
                    AcceptServerInput(command);
                }
                else
                {
                    predictedState = SimulateWithGroundSupport(predictedState, command);
                    AddPendingOwnerInput(command);
                    ApplyOwnerPose(predictedState);
                    if (command.FirePressed)
                    {
                        SubmitFireCommandServerRpc(BuildFireCommand(
                            command.Tick,
                            predictedState.YawDegrees,
                            predictedState.PitchDegrees));
                    }
                    SubmitInputCommandServerRpc(command);
                }
            }

            ownerAccumulator -= ticksDue * NetworkPlayerSimulation.TickDeltaSeconds;
            if (!IsServer)
            {
                ApplyOwnerRenderPose(ownerAccumulator);
            }
        }

        private NetworkPlayerInputCommand ClampOwnerCommand(NetworkPlayerInputCommand command)
        {
            command.MoveX = IsFinite(command.MoveX) ? Mathf.Clamp(command.MoveX, -1f, 1f) : 0f;
            command.MoveY = IsFinite(command.MoveY) ? Mathf.Clamp(command.MoveY, -1f, 1f) : 0f;
            float maximumLookDelta = (serverAuthority != null
                ? serverAuthority.MaximumRotationDegreesPerSecond
                : 1080f) * NetworkPlayerSimulation.TickDeltaSeconds + 4f;
            command.LookX = IsFinite(command.LookX)
                ? Mathf.Clamp(command.LookX, -maximumLookDelta, maximumLookDelta)
                : 0f;
            command.LookY = IsFinite(command.LookY)
                ? Mathf.Clamp(command.LookY, -maximumLookDelta, maximumLookDelta)
                : 0f;
            Vector2 look = Vector2.ClampMagnitude(new Vector2(command.LookX, command.LookY), maximumLookDelta);
            command.LookX = look.x;
            command.LookY = look.y;
            return command;
        }

        private void ApplyOwnerRenderPose(float extrapolationSeconds)
        {
            if (!ownerInitialized || !predictedState.Initialized)
            {
                return;
            }
            ApplyOwnerPose(NetworkPlayerSimulation.Extrapolate(predictedState, extrapolationSeconds));
        }

        private void SampleInputAtRenderRate()
        {
            Keyboard keyboard = Keyboard.current;
            latestMoveX = 0f;
            latestMoveY = 0f;
            latestSprint = false;
            latestCrouch = false;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed) latestMoveX -= 1f;
                if (keyboard.dKey.isPressed) latestMoveX += 1f;
                if (keyboard.sKey.isPressed) latestMoveY -= 1f;
                if (keyboard.wKey.isPressed) latestMoveY += 1f;
                latestSprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                latestCrouch = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed ||
                               keyboard.cKey.isPressed;
                bufferedJump |= keyboard.spaceKey.wasPressedThisFrame;
                bufferedReload |= keyboard.rKey.wasPressedThisFrame;
            }

            latestFireHeld = false;
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 look = mouse.delta.ReadValue();
                float sensitivity = worldSettings != null
                    ? worldSettings.MouseSensitivityDegreesPerPixel
                    : 0.12f;
                bufferedLookX += look.x * sensitivity;
                bufferedLookY -= look.y * sensitivity;
                latestFireHeld = mouse.leftButton.isPressed;
                bufferedFire |= mouse.leftButton.wasPressedThisFrame;
            }
        }

        private void AddPendingOwnerInput(NetworkPlayerInputCommand command)
        {
            pendingOwnerInputs.Add(command);
            if (pendingOwnerInputs.Count > MaximumBufferedInputs)
            {
                // Keep the newest window; the next authoritative snapshot still resets and replays it.
                pendingOwnerInputs.RemoveRange(0, pendingOwnerInputs.Count - MaximumBufferedInputs);
            }
        }

        private void TryInitializeOwnerFromSnapshot()
        {
            if (!IsOwner || ownerInitialized || worldManager == null || !worldManager.IsReady ||
                localMotor == null || !authoritativeState.Value.Initialized)
            {
                return;
            }

            InitializeLocalPrediction(authoritativeState.Value);
        }

        private void InitializeLocalPrediction(NetworkPlayerSnapshot snapshot)
        {
            if (!snapshot.Initialized)
            {
                return;
            }

            predictedState = snapshot;
            ownerInitialized = true;
            nextOwnerInputTick = snapshot.LastProcessedInputTick;
            lastAcknowledgedInputTick = snapshot.LastProcessedInputTick;
            lastAppliedServerTick = snapshot.ServerTick;
            hasAppliedServerTick = true;
            pendingOwnerInputs.Clear();
            ApplyOwnerPose(predictedState);
        }

        private void ApplyOwnerAuthoritativeSnapshot(NetworkPlayerSnapshot snapshot)
        {
            if (!snapshot.Initialized || worldManager == null || !worldManager.IsReady || localMotor == null)
            {
                return;
            }

            if (!ownerInitialized)
            {
                InitializeLocalPrediction(snapshot);
                return;
            }

            if (hasAppliedServerTick && snapshot.ServerTick != lastAppliedServerTick &&
                !IsSequenceNewer(snapshot.ServerTick, lastAppliedServerTick))
            {
                return;
            }

            hasAppliedServerTick = true;
            lastAppliedServerTick = snapshot.ServerTick;
            lastAcknowledgedInputTick = snapshot.LastProcessedInputTick;
            for (int i = pendingOwnerInputs.Count - 1; i >= 0; i--)
            {
                if (IsSequenceAtOrBefore(pendingOwnerInputs[i].Tick, snapshot.LastProcessedInputTick))
                {
                    pendingOwnerInputs.RemoveAt(i);
                }
            }

            NetworkPlayerSnapshot replayed = snapshot;
            for (int i = 0; i < pendingOwnerInputs.Count; i++)
            {
                if (IsSequenceNewer(pendingOwnerInputs[i].Tick, snapshot.LastProcessedInputTick))
                {
                    replayed = Simulate(replayed, pendingOwnerInputs[i]);
                }
            }

            predictedState = replayed;
            ApplyOwnerPose(predictedState);
        }

        private void ApplyOwnerPose(NetworkPlayerSnapshot state)
        {
            if (localMotor == null || worldManager == null || !worldManager.IsReady || !state.Initialized)
            {
                return;
            }

            localMotor.ApplyNetworkPose(
                new LocalPosition(state.EastMeters, state.UpMeters, state.NorthMeters),
                state.YawDegrees,
                state.PitchDegrees,
                state.Crouched);
            localMotor.SetNetworkAnimationState(state);
            if (!state.Alive || state.Health == 0 || state.MatchFinished)
            {
                localMotor.SetInputEnabled(false);
            }
        }

        private void OnAuthoritativeStateChanged(NetworkPlayerSnapshot previous, NetworkPlayerSnapshot current)
        {
            if (!current.Initialized)
            {
                return;
            }

            if (IsServer)
            {
                serverState = current;
                serverInitialized = true;
                if (serverAuthority == null)
                {
                    serverAuthority = FindObjectOfType<PhaseEightServerAuthority>();
                }
                if (serverAuthority != null)
                {
                    ConfigureLagCompensationHistory(serverAuthority.LagCompensationWindowSeconds);
                }
                RecordServerHistory(Time.realtimeSinceStartupAsDouble);
                if (!registeredWithServerAuthority)
                {
                    RegisterWithServerAuthority();
                }
                if (IsOwner)
                {
                    predictedState = current;
                    ownerInitialized = true;
                    ApplyOwnerPose(current);
                }
                else
                {
                    BufferRemoteSnapshot(current);
                }
                return;
            }

            if (IsOwner)
            {
                ApplyOwnerAuthoritativeSnapshot(current);
            }
            else
            {
                BufferRemoteSnapshot(current);
            }
        }

        private void BufferRemoteSnapshot(NetworkPlayerSnapshot snapshot)
        {
            if (!snapshot.Initialized)
            {
                return;
            }
            if (remoteSnapshots.Count > 0)
            {
                NetworkPlayerSnapshot last = remoteSnapshots[remoteSnapshots.Count - 1].State;
                if (snapshot.ServerTick == last.ServerTick || !IsSequenceNewer(snapshot.ServerTick, last.ServerTick))
                {
                    return;
                }
            }

            remoteSnapshots.Add(new ReceivedSnapshot(snapshot, Time.unscaledTime));
            while (remoteSnapshots.Count > MaximumRemoteSnapshots)
            {
                remoteSnapshots.RemoveAt(0);
            }
        }

        private void RenderInterpolatedRemotePlayer()
        {
            if (remoteSnapshots.Count == 0 || worldManager == null || !worldManager.IsReady ||
                worldManager.Georeference == null)
            {
                return;
            }

            float renderTime = Time.unscaledTime - InterpolationDelaySeconds;
            while (remoteSnapshots.Count > 2 && remoteSnapshots[1].ReceivedAt <= renderTime)
            {
                remoteSnapshots.RemoveAt(0);
            }

            NetworkPlayerSnapshot renderState;
            if (remoteSnapshots.Count == 1 || renderTime <= remoteSnapshots[0].ReceivedAt)
            {
                renderState = remoteSnapshots[0].State;
            }
            else if (renderTime <= remoteSnapshots[remoteSnapshots.Count - 1].ReceivedAt)
            {
                int upperIndex = 1;
                while (upperIndex < remoteSnapshots.Count && remoteSnapshots[upperIndex].ReceivedAt < renderTime)
                {
                    upperIndex++;
                }
                upperIndex = Mathf.Clamp(upperIndex, 1, remoteSnapshots.Count - 1);
                ReceivedSnapshot from = remoteSnapshots[upperIndex - 1];
                ReceivedSnapshot to = remoteSnapshots[upperIndex];
                float duration = Mathf.Max(0.001f, to.ReceivedAt - from.ReceivedAt);
                float amount = (renderTime - from.ReceivedAt) / duration;
                renderState = NetworkPlayerSimulation.Interpolate(from.State, to.State, amount);
            }
            else
            {
                ReceivedSnapshot latest = remoteSnapshots[remoteSnapshots.Count - 1];
                float extrapolation = Mathf.Min(
                    MaximumExtrapolationSeconds,
                    Mathf.Max(0f, renderTime - latest.ReceivedAt));
                renderState = NetworkPlayerSimulation.Extrapolate(latest.State, extrapolation);
            }

            ApplyRemotePose(renderState);
        }

        private void ApplyRemotePose(NetworkPlayerSnapshot state)
        {
            if (worldManager == null || !worldManager.IsReady || !state.Initialized)
            {
                return;
            }
            EnsureRemoteAvatar(state);
            if (remoteAvatarAnchor == null)
            {
                return;
            }

            GeoPosition geographicPosition = worldManager.LocalToGeographic(new LocalPosition(
                state.EastMeters,
                state.UpMeters,
                state.NorthMeters));
            remoteAvatarAnchor.longitudeLatitudeHeight = new double3(
                geographicPosition.LongitudeDegrees,
                geographicPosition.LatitudeDegrees,
                geographicPosition.AltitudeMeters);
            remoteAvatarAnchor.Sync();
            if (remoteFacingMarker != null)
            {
                remoteFacingMarker.localRotation = Quaternion.Euler(0f, state.YawDegrees, 0f);
            }
            if (remotePresentation != null)
            {
                remotePresentation.SetState(state);
            }
        }

        private void EnsureRemoteAvatar(NetworkPlayerSnapshot state)
        {
            if (remoteAvatar != null || worldManager == null || worldManager.Georeference == null)
            {
                return;
            }

            remoteAvatar = new GameObject("RemotePlayer_" + state.PlayerId);
            remoteAvatar.transform.SetParent(worldManager.Georeference.transform, false);
            remoteAvatarAnchor = remoteAvatar.AddComponent<CesiumGlobeAnchor>();
            remoteAvatarAnchor.detectTransformChanges = true;
            remoteAvatarAnchor.adjustOrientationForGlobeWhenMoving = true;

            float height = worldSettings != null ? worldSettings.CharacterHeightMeters : 1.8f;
            GameObject modelObject = new GameObject("ProceduralCharacterVisual");
            modelObject.transform.SetParent(remoteAvatar.transform, false);
            Transform modelRoot = modelObject.transform;
            float shoulderHeight = height * 0.57f;
            float headHeight = height * 0.86f;
            float legHeight = height * 0.22f;

            Color playerColor = PlayerColor(state.PlayerId);
            Color limbColor = Color.Lerp(playerColor, Color.black, 0.22f);
            Transform torso = CreateRemotePart(
                "Torso", PrimitiveType.Capsule, modelRoot,
                new Vector3(0f, height * 0.51f, 0f), new Vector3(0.42f, height * 0.25f, 0.32f), playerColor);
            Transform head = CreateRemotePart(
                "Head", PrimitiveType.Sphere, modelRoot,
                new Vector3(0f, headHeight, 0f), Vector3.one * 0.30f, Color.Lerp(playerColor, Color.white, 0.35f));
            Transform leftArm = CreateRemotePart(
                "LeftArm", PrimitiveType.Capsule, modelRoot,
                new Vector3(-0.34f, shoulderHeight, 0f), new Vector3(0.14f, height * 0.22f, 0.14f), limbColor);
            Transform rightArm = CreateRemotePart(
                "RightArm", PrimitiveType.Capsule, modelRoot,
                new Vector3(0.34f, shoulderHeight, 0f), new Vector3(0.14f, height * 0.22f, 0.14f), limbColor);
            Transform leftLeg = CreateRemotePart(
                "LeftLeg", PrimitiveType.Capsule, modelRoot,
                new Vector3(-0.15f, legHeight, 0f), new Vector3(0.17f, height * 0.25f, 0.17f), limbColor);
            Transform rightLeg = CreateRemotePart(
                "RightLeg", PrimitiveType.Capsule, modelRoot,
                new Vector3(0.15f, legHeight, 0f), new Vector3(0.17f, height * 0.25f, 0.17f), limbColor);

            ProceduralPlayerPresentation presentation = remoteAvatar.AddComponent<ProceduralPlayerPresentation>();
            presentation.ConfigureRemote(
                modelRoot, torso, head, leftArm, rightArm, leftLeg, rightLeg, worldSettings);
            remotePresentation = presentation;

            GameObject facing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            facing.name = "FacingIndicator";
            facing.transform.SetParent(remoteAvatar.transform, false);
            facing.transform.localPosition = new Vector3(0f, height * 0.77f, 0.38f);
            facing.transform.localScale = new Vector3(0.16f, 0.12f, 0.42f);
            RemovePrimitiveCollider(facing);
            ApplyColor(facing, Color.white);
            remoteFacingMarker = facing.transform;
        }

        private static Transform CreateRemotePart(
            string partName,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            RemovePrimitiveCollider(part);
            ApplyColor(part, color);
            return part.transform;
        }

        private static void RemovePrimitiveCollider(GameObject primitive)
        {
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
            }
        }

        private static void ApplyColor(GameObject target, Color color)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            if (sharedRemoteAvatarMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }
                if (shader != null)
                {
                    sharedRemoteAvatarMaterial = new Material(shader);
                    sharedRemoteAvatarMaterial.name = "WorldPvp_RemoteAvatar_Runtime";
                }
            }

            if (sharedRemoteAvatarMaterial != null)
            {
                renderer.sharedMaterial = sharedRemoteAvatarMaterial;
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetColor(BaseColorPropertyId, color);
                properties.SetColor(LegacyColorPropertyId, color);
                renderer.SetPropertyBlock(properties);
            }
            else
            {
                renderer.material.color = color;
            }
        }

        private static Color PlayerColor(ulong playerId)
        {
            float hue = (((playerId + 1UL) * 0.137f) % 1.0f + 1.0f) % 1.0f;
            return Color.HSVToRGB(hue, 0.72f, 0.95f);
        }

        private void DestroyRemoteAvatar()
        {
            if (remoteAvatar != null)
            {
                UnityEngine.Object.Destroy(remoteAvatar);
            }
            remoteAvatar = null;
            remoteAvatarAnchor = null;
            remoteFacingMarker = null;
            remotePresentation = null;
        }

        private static bool IsSequenceNewer(uint candidate, uint reference)
        {
            return candidate != reference && unchecked((int)(candidate - reference)) > 0;
        }

        private static bool IsSequenceAtOrBefore(uint candidate, uint reference)
        {
            return candidate == reference || IsSequenceNewer(reference, candidate);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnDestroy()
        {
            DestroyRemoteAvatar();
        }
    }
}
