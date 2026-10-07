using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using WorldPvp.Phase1.Combat;

namespace WorldPvp.Phase1.Battles
{
    /// <summary>A signed horizontal grid cell in the arena's metre-based ENU frame.</summary>
    public struct SpatialCellKey : IEquatable<SpatialCellKey>
    {
        public readonly int EastCell;
        public readonly int NorthCell;

        public SpatialCellKey(int eastCell, int northCell)
        {
            EastCell = eastCell;
            NorthCell = northCell;
        }

        public static SpatialCellKey FromMeters(double eastMeters, double northMeters, float cellSizeMeters)
        {
            float size = Mathf.Max(1f, cellSizeMeters);
            return new SpatialCellKey(
                (int)Math.Floor(eastMeters / size),
                (int)Math.Floor(northMeters / size));
        }

        public bool IsWithinCellRadius(SpatialCellKey other, int cellRadius)
        {
            int radius = Math.Max(0, cellRadius);
            return Math.Abs(EastCell - other.EastCell) <= radius &&
                   Math.Abs(NorthCell - other.NorthCell) <= radius;
        }

        public bool Equals(SpatialCellKey other)
        {
            return EastCell == other.EastCell && NorthCell == other.NorthCell;
        }

        public override bool Equals(object obj)
        {
            return obj is SpatialCellKey && Equals((SpatialCellKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked { return (EastCell * 397) ^ NorthCell; }
        }

        public static bool operator ==(SpatialCellKey left, SpatialCellKey right) { return left.Equals(right); }
        public static bool operator !=(SpatialCellKey left, SpatialCellKey right) { return !left.Equals(right); }
    }

    /// <summary>
    /// Server-side registry, spatial cell tracking, and NGO observer culling. Player NetworkObjects
    /// are spawned without default observers; ordinary players receive only the local 3x3 cell area.
    /// Eliminated players remain spectators and can observe living players until match end.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhaseEightServerAuthority : MonoBehaviour
    {
        [SerializeField, Min(5f)] private float interestCellSizeMeters = 25f;
        [SerializeField, Min(0)] private int interestCellRadius = 1;
        [SerializeField, Range(0.05f, 1f)] private float interestRefreshSeconds = 0.25f;
        [SerializeField, Range(0.25f, 0.5f)] private float lagCompensationWindowSeconds = 0.4f;
        [SerializeField, Min(0.01f)] private float violationScoreDecayPerSecond = 0.25f;
        [SerializeField, Min(0.05f)] private float violationReviewThreshold = 12f;
        [SerializeField, Min(0.1f)] private float maximumRotationDegreesPerSecond = 1080f;
        [SerializeField, Range(1, 32)] private int maximumFutureClientTicks = 8;
        [SerializeField, Range(8, 256)] private int maximumQueuedInputs = 96;
        [SerializeField, Min(1)] private int matchDurationSeconds = 600;
        [SerializeField, Range(2, 32)] private int maximumPlayers = 8;

        private readonly List<NetworkPlayer> serverPlayers = new List<NetworkPlayer>(32);
        private readonly HashSet<ulong> pendingConnectionApprovals = new HashSet<ulong>();
        private NetworkManager subscribedNetworkManager;
        private readonly Dictionary<ulong, SpatialCellKey> playerCells = new Dictionary<ulong, SpatialCellKey>(32);
        private readonly Dictionary<ulong, HashSet<ulong>> visibleTargetsByViewer =
            new Dictionary<ulong, HashSet<ulong>>(32);
        private readonly List<ulong> disconnectedViewers = new List<ulong>(8);
        private bool interestRefreshPending = true;
        private float nextInterestRefreshTime;
        private bool dedicatedServerSession;

        public float InterestCellSizeMeters { get { return Mathf.Max(5f, interestCellSizeMeters); } }
        public int InterestCellRadius { get { return Mathf.Max(0, interestCellRadius); } }
        public float LagCompensationWindowSeconds
        {
            get { return Mathf.Clamp(lagCompensationWindowSeconds, 0.25f, 0.5f); }
        }
        public float ViolationScoreDecayPerSecond { get { return Mathf.Max(0.01f, violationScoreDecayPerSecond); } }
        public float ViolationReviewThreshold { get { return Mathf.Max(0.05f, violationReviewThreshold); } }
        public float MaximumRotationDegreesPerSecond { get { return Mathf.Max(0.1f, maximumRotationDegreesPerSecond); } }
        public int MaximumFutureClientTicks { get { return Mathf.Clamp(maximumFutureClientTicks, 1, 32); } }
        public int MaximumQueuedInputs { get { return Mathf.Clamp(maximumQueuedInputs, 8, 256); } }
        public int MatchDurationSeconds { get { return Mathf.Max(1, matchDurationSeconds); } }
        public int MaximumPlayers { get { return Mathf.Clamp(maximumPlayers, 2, 32); } }
        public bool IsDedicatedServerSession { get { return dedicatedServerSession; } }
        public int RegisteredPlayerCount { get { return serverPlayers.Count; } }

        public void ConfigureRuntime(float cellSizeMeters, int durationSeconds, int playerLimit)
        {
            interestCellSizeMeters = Mathf.Clamp(cellSizeMeters, 5f, 500f);
            matchDurationSeconds = Mathf.Max(1, durationSeconds);
            maximumPlayers = Mathf.Clamp(playerLimit, 2, 32);
            interestRefreshPending = true;

            if (!dedicatedServerSession)
            {
                return;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
            {
                throw new InvalidOperationException("A NetworkManager is required to configure dedicated-server admission.");
            }
            manager.NetworkConfig.ConnectionApproval = true;
            manager.ConnectionApprovalCallback = ApproveDedicatedClient;
            if (subscribedNetworkManager != manager)
            {
                UnsubscribeNetworkManager();
                subscribedNetworkManager = manager;
                subscribedNetworkManager.OnClientConnectedCallback += OnDedicatedClientConnected;
                subscribedNetworkManager.OnClientDisconnectCallback += OnDedicatedClientDisconnected;
            }
        }

        public void ConfigureDedicatedSession(bool isDedicatedServer)
        {
            dedicatedServerSession = isDedicatedServer;
            interestRefreshPending = true;
        }

        private void ApproveDedicatedClient(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            NetworkManager manager = NetworkManager.Singleton;
            int connectedCount = manager != null ? manager.ConnectedClientsList.Count : 0;
            int maximum = MaximumPlayers;
            bool hasCapacity = connectedCount + pendingConnectionApprovals.Count < maximum;
            if (hasCapacity)
            {
                pendingConnectionApprovals.Add(request.ClientNetworkId);
            }

            response.Approved = hasCapacity;
            response.CreatePlayerObject = hasCapacity;
            response.Pending = false;
            response.Reason = hasCapacity ? string.Empty : "This dedicated match is full.";
        }

        private void OnDedicatedClientConnected(ulong clientId)
        {
            pendingConnectionApprovals.Remove(clientId);
            interestRefreshPending = true;
        }

        private void OnDedicatedClientDisconnected(ulong clientId)
        {
            pendingConnectionApprovals.Remove(clientId);
            interestRefreshPending = true;
        }

        private void OnDestroy()
        {
            UnsubscribeNetworkManager();
        }

        private void UnsubscribeNetworkManager()
        {
            if (subscribedNetworkManager != null)
            {
                subscribedNetworkManager.OnClientConnectedCallback -= OnDedicatedClientConnected;
                subscribedNetworkManager.OnClientDisconnectCallback -= OnDedicatedClientDisconnected;
                subscribedNetworkManager = null;
            }
        }

        public void ConfigureLagCompensationWindow(float seconds)
        {
            lagCompensationWindowSeconds = Mathf.Clamp(seconds, 0.25f, 0.5f);
            for (int i = 0; i < serverPlayers.Count; i++)
            {
                if (serverPlayers[i] != null)
                {
                    serverPlayers[i].ConfigureLagCompensationHistory(LagCompensationWindowSeconds);
                }
            }
        }

        public SpatialCellKey CellFor(double eastMeters, double northMeters)
        {
            return SpatialCellKey.FromMeters(eastMeters, northMeters, InterestCellSizeMeters);
        }

        public void RegisterServerPlayer(NetworkPlayer player)
        {
            if (player == null || !player.IsSpawned || !player.IsServer)
            {
                return;
            }

            for (int i = 0; i < serverPlayers.Count; i++)
            {
                if (serverPlayers[i] == player ||
                    (serverPlayers[i] != null && serverPlayers[i].OwnerClientId == player.OwnerClientId))
                {
                    serverPlayers[i] = player;
                    UpdatePlayerCell(player);
                    interestRefreshPending = true;
                    return;
                }
            }

            serverPlayers.Add(player);
            UpdatePlayerCell(player);
            interestRefreshPending = true;

            CombatMatchController matchController = FindObjectOfType<CombatMatchController>();
            if (matchController != null)
            {
                matchController.ServerRegisterParticipant(player);
            }
        }

        public void UnregisterServerPlayer(NetworkPlayer player)
        {
            if (player == null)
            {
                return;
            }

            ulong clientId = player.OwnerClientId;
            for (int i = serverPlayers.Count - 1; i >= 0; i--)
            {
                if (serverPlayers[i] == null || serverPlayers[i] == player ||
                    serverPlayers[i].OwnerClientId == clientId)
                {
                    serverPlayers.RemoveAt(i);
                }
            }
            playerCells.Remove(clientId);
            foreach (HashSet<ulong> visibleTargets in visibleTargetsByViewer.Values)
            {
                visibleTargets.Remove(clientId);
            }
            visibleTargetsByViewer.Remove(clientId);
            interestRefreshPending = true;
        }

        public NetworkPlayer GetServerPlayerByIndex(int index)
        {
            return index >= 0 && index < serverPlayers.Count ? serverPlayers[index] : null;
        }

        public SpatialCellKey GetTrackedCell(ulong clientId)
        {
            SpatialCellKey key;
            return playerCells.TryGetValue(clientId, out key) ? key : new SpatialCellKey(int.MinValue, int.MinValue);
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer)
            {
                return;
            }

            PruneDisconnectedPlayers(manager);
            if (!interestRefreshPending && Time.unscaledTime < nextInterestRefreshTime)
            {
                return;
            }

            nextInterestRefreshTime = Time.unscaledTime + Mathf.Clamp(interestRefreshSeconds, 0.05f, 1f);
            interestRefreshPending = false;
            RefreshPlayerCellsAndObservers(manager);
        }

        private void PruneDisconnectedPlayers(NetworkManager manager)
        {
            for (int i = serverPlayers.Count - 1; i >= 0; i--)
            {
                NetworkPlayer player = serverPlayers[i];
                if (player == null || !player.IsSpawned ||
                    !manager.ConnectedClients.ContainsKey(player.OwnerClientId))
                {
                    ulong clientId = player != null ? player.OwnerClientId : ulong.MaxValue;
                    serverPlayers.RemoveAt(i);
                    playerCells.Remove(clientId);
                    interestRefreshPending = true;
                }
            }

            disconnectedViewers.Clear();
            foreach (KeyValuePair<ulong, HashSet<ulong>> pair in visibleTargetsByViewer)
            {
                if (!manager.ConnectedClients.ContainsKey(pair.Key))
                {
                    disconnectedViewers.Add(pair.Key);
                }
            }
            for (int i = 0; i < disconnectedViewers.Count; i++)
            {
                visibleTargetsByViewer.Remove(disconnectedViewers[i]);
            }
        }

        private void RefreshPlayerCellsAndObservers(NetworkManager manager)
        {
            for (int i = 0; i < serverPlayers.Count; i++)
            {
                UpdatePlayerCell(serverPlayers[i]);
            }

            for (int viewerIndex = 0; viewerIndex < serverPlayers.Count; viewerIndex++)
            {
                NetworkPlayer viewer = serverPlayers[viewerIndex];
                if (viewer == null || !viewer.IsSpawned || !viewer.HasAuthoritativeState)
                {
                    continue;
                }

                NetworkPlayerSnapshot viewerState = viewer.CurrentState;
                bool isSpectator = !viewerState.Alive || viewerState.Health == 0;
                SpatialCellKey viewerCell = GetTrackedCell(viewer.OwnerClientId);
                HashSet<ulong> visible;
                if (!visibleTargetsByViewer.TryGetValue(viewer.OwnerClientId, out visible))
                {
                    visible = new HashSet<ulong>();
                    visibleTargetsByViewer.Add(viewer.OwnerClientId, visible);
                }

                if (!visible.Contains(viewer.OwnerClientId))
                {
                    NetworkObject viewerObject = viewer.GetComponent<NetworkObject>();
                    if (viewerObject != null && viewerObject.IsSpawned)
                    {
                        viewerObject.NetworkShow(viewer.OwnerClientId);
                        visible.Add(viewer.OwnerClientId);
                    }
                }

                for (int targetIndex = 0; targetIndex < serverPlayers.Count; targetIndex++)
                {
                    NetworkPlayer target = serverPlayers[targetIndex];
                    if (target == null || !target.IsSpawned || target == viewer)
                    {
                        continue;
                    }

                    NetworkPlayerSnapshot targetState = target.CurrentState;
                    SpatialCellKey targetCell = GetTrackedCell(target.OwnerClientId);
                    bool shouldObserve = isSpectator
                        ? targetState.Alive && targetState.Health > 0
                        : viewerCell.IsWithinCellRadius(targetCell, InterestCellRadius);
                    bool isVisible = visible.Contains(target.OwnerClientId);
                    if (shouldObserve && !isVisible)
                    {
                        NetworkObject networkObject = target.GetComponent<NetworkObject>();
                        if (networkObject != null && networkObject.IsSpawned)
                        {
                            networkObject.NetworkShow(viewer.OwnerClientId);
                            visible.Add(target.OwnerClientId);
                        }
                    }
                    else if (!shouldObserve && isVisible)
                    {
                        NetworkObject networkObject = target.GetComponent<NetworkObject>();
                        if (networkObject != null && networkObject.IsSpawned)
                        {
                            networkObject.NetworkHide(viewer.OwnerClientId);
                        }
                        visible.Remove(target.OwnerClientId);
                    }
                }
            }
        }

        private void UpdatePlayerCell(NetworkPlayer player)
        {
            if (player == null || !player.HasAuthoritativeState)
            {
                return;
            }
            NetworkPlayerSnapshot state = player.CurrentState;
            playerCells[player.OwnerClientId] = CellFor(state.EastMeters, state.NorthMeters);
        }
    }
}
