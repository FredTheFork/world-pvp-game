using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using WorldPvp.Phase1.Battles;

namespace WorldPvp.Phase1.Combat
{
    /// <summary>
    /// Server-authored match gate and replicated scoreboard. It supports the existing Relay client-host
    /// test flow and the Phase 8 dedicated-server flow. Eliminated players are never respawned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatMatchController : NetworkBehaviour
    {
        private const float EvaluationIntervalSeconds = 0.5f;
        private readonly NetworkList<MatchScoreEntry> scoreEntries = new NetworkList<MatchScoreEntry>();
        private readonly NetworkVariable<ushort> synchronizedSecondsRemaining = new NetworkVariable<ushort>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private bool serverMatchActive;
        private bool serverMatchEnded;
        private bool hasObservedMultiplePlayers;
        private double serverMatchEndsAt;
        private float nextEvaluationTime;
        private NetworkManager subscribedNetworkManager;

        public bool IsServerMatchActive { get { return serverMatchActive; } }
        public bool IsServerMatchEnded { get { return serverMatchEnded; } }
        public ushort MatchRemainingSeconds { get { return synchronizedSecondsRemaining.Value; } }
        public NetworkList<MatchScoreEntry> ScoreEntries { get { return scoreEntries; } }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BindNetworkManagerCallbacks();
        }

        public override void OnNetworkDespawn()
        {
            UnbindNetworkManagerCallbacks();
            base.OnNetworkDespawn();
        }

        private void OnEnable()
        {
            BindNetworkManagerCallbacks();
        }

        private void OnDisable()
        {
            UnbindNetworkManagerCallbacks();
        }

        public void BeginMatch()
        {
            BindNetworkManagerCallbacks();
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || !manager.IsListening || !IsSpawned)
            {
                return;
            }

            serverMatchEnded = false;
            hasObservedMultiplePlayers = hasObservedMultiplePlayers || manager.ConnectedClientsList.Count >= 2;
            serverMatchActive = true;
            PhaseEightServerAuthority authority = FindObjectOfType<PhaseEightServerAuthority>();
            int duration = authority != null ? authority.MatchDurationSeconds : 600;
            serverMatchEndsAt = Time.realtimeSinceStartupAsDouble + Math.Max(1, duration);
            synchronizedSecondsRemaining.Value = (ushort)Math.Min(ushort.MaxValue, Math.Max(1, duration));
            nextEvaluationTime = Time.unscaledTime;
            EvaluateMatchEnd();
        }

        public void ResetMatch()
        {
            serverMatchActive = false;
            serverMatchEnded = false;
            hasObservedMultiplePlayers = false;
            serverMatchEndsAt = 0.0;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsServer && IsSpawned)
            {
                scoreEntries.Clear();
                synchronizedSecondsRemaining.Value = 0;
            }
        }

        public void ServerRegisterParticipant(NetworkPlayer player)
        {
            if (!IsServer || !IsSpawned || player == null || !player.HasAuthoritativeState)
            {
                return;
            }

            int existingIndex = FindScoreEntry(player.OwnerClientId);
            MatchScoreEntry entry = new MatchScoreEntry
            {
                PlayerId = player.OwnerClientId,
                Kills = existingIndex >= 0 ? scoreEntries[existingIndex].Kills : 0u,
                DisplayName = player.CurrentState.DisplayName
            };
            if (existingIndex >= 0)
            {
                scoreEntries[existingIndex] = entry;
            }
            else
            {
                scoreEntries.Add(entry);
            }
        }

        public void ServerAwardKill(ulong playerId, Unity.Collections.FixedString64Bytes displayName)
        {
            if (!IsServer || !IsSpawned || !serverMatchActive || serverMatchEnded)
            {
                return;
            }

            int index = FindScoreEntry(playerId);
            MatchScoreEntry entry = index >= 0
                ? scoreEntries[index]
                : new MatchScoreEntry { PlayerId = playerId, DisplayName = displayName, Kills = 0u };
            if (entry.Kills < uint.MaxValue)
            {
                entry.Kills++;
            }
            if (index >= 0)
            {
                scoreEntries[index] = entry;
            }
            else
            {
                scoreEntries.Add(entry);
            }
        }

        public uint GetKillsForPlayer(ulong playerId)
        {
            int index = FindScoreEntry(playerId);
            return index >= 0 ? scoreEntries[index].Kills : 0u;
        }

        public void ServerEvaluateAfterPlayerDeath()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsServer && serverMatchActive && !serverMatchEnded)
            {
                EvaluateMatchEnd();
            }
        }

        private void Update()
        {
            BindNetworkManagerCallbacks();
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || !manager.IsListening || !IsSpawned)
            {
                return;
            }

            TryStartDedicatedMatch(manager);
            if (serverMatchActive && !serverMatchEnded)
            {
                PublishRemainingTime();
                if (Time.realtimeSinceStartupAsDouble >= serverMatchEndsAt)
                {
                    List<NetworkPlayer> timedOutParticipants = GetInitializedConnectedPlayers(manager);
                    FinishServerMatch(timedOutParticipants, SelectScoreWinner(timedOutParticipants));
                    return;
                }
                if (manager.ConnectedClientsList.Count >= 2)
                {
                    hasObservedMultiplePlayers = true;
                }
            }

            if (!serverMatchActive || serverMatchEnded || Time.unscaledTime < nextEvaluationTime)
            {
                return;
            }
            nextEvaluationTime = Time.unscaledTime + EvaluationIntervalSeconds;
            EvaluateMatchEnd();
        }

        private void TryStartDedicatedMatch(NetworkManager manager)
        {
            if (serverMatchActive || serverMatchEnded || manager.ConnectedClientsList.Count < 2)
            {
                return;
            }

            PhaseEightServerAuthority authority = FindObjectOfType<PhaseEightServerAuthority>();
            if (authority == null || !authority.IsDedicatedServerSession)
            {
                return;
            }

            List<NetworkPlayer> participants = GetInitializedConnectedPlayers(manager);
            if (participants.Count == manager.ConnectedClientsList.Count && participants.Count >= 2)
            {
                hasObservedMultiplePlayers = true;
                BeginMatch();
            }
        }

        private void PublishRemainingTime()
        {
            double remaining = Math.Max(0.0, serverMatchEndsAt - Time.realtimeSinceStartupAsDouble);
            ushort seconds = (ushort)Math.Min(ushort.MaxValue, Math.Ceiling(remaining));
            if (synchronizedSecondsRemaining.Value != seconds)
            {
                synchronizedSecondsRemaining.Value = seconds;
            }
        }

        private void BindNetworkManagerCallbacks()
        {
            NetworkManager current = NetworkManager.Singleton;
            if (current == subscribedNetworkManager)
            {
                return;
            }

            UnbindNetworkManagerCallbacks();
            subscribedNetworkManager = current;
            if (subscribedNetworkManager != null)
            {
                subscribedNetworkManager.OnClientConnectedCallback += OnClientConnected;
                subscribedNetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        private void UnbindNetworkManagerCallbacks()
        {
            if (subscribedNetworkManager != null)
            {
                subscribedNetworkManager.OnClientConnectedCallback -= OnClientConnected;
                subscribedNetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                subscribedNetworkManager = null;
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (serverMatchActive && !serverMatchEnded && manager != null && manager.IsServer &&
                manager.ConnectedClientsList.Count >= 2)
            {
                hasObservedMultiplePlayers = true;
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || !serverMatchActive || serverMatchEnded)
            {
                return;
            }

            if (manager.ConnectedClientsList.Count >= 2)
            {
                hasObservedMultiplePlayers = true;
            }
            EvaluateMatchEnd();
        }

        private void EvaluateMatchEnd()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || !manager.IsListening || !IsSpawned ||
                !serverMatchActive || serverMatchEnded)
            {
                return;
            }

            List<NetworkPlayer> participants = GetInitializedConnectedPlayers(manager);
            if (manager.ConnectedClientsList.Count >= 2 || participants.Count >= 2)
            {
                hasObservedMultiplePlayers = true;
            }
            if (!hasObservedMultiplePlayers || participants.Count != manager.ConnectedClientsList.Count)
            {
                // Do not mistake a joining player with an uninitialized snapshot for a disconnect.
                return;
            }

            NetworkPlayer winner = null;
            int aliveCount = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                NetworkPlayerSnapshot state = participants[i].CurrentState;
                if (state.Alive && state.Health > 0)
                {
                    winner = participants[i];
                    aliveCount++;
                }
            }

            // No respawns: the round ends at the last survivor, or when a disconnect leaves one.
            bool finishedByLastSurvivor = participants.Count >= 2 && aliveCount <= 1;
            bool finishedByDisconnect = participants.Count == 1 && aliveCount == 1;
            bool noSurvivor = participants.Count == 0;
            if (!finishedByLastSurvivor && !finishedByDisconnect && !noSurvivor)
            {
                return;
            }

            FinishServerMatch(participants, aliveCount == 1 ? winner : null);
        }

        private static List<NetworkPlayer> GetInitializedConnectedPlayers(NetworkManager manager)
        {
            List<NetworkPlayer> result = new List<NetworkPlayer>(manager.ConnectedClientsList.Count);
            for (int i = 0; i < manager.ConnectedClientsList.Count; i++)
            {
                NetworkClient client = manager.ConnectedClientsList[i];
                NetworkObject playerObject = client != null ? client.PlayerObject : null;
                if (playerObject == null || !playerObject.IsSpawned)
                {
                    continue;
                }

                NetworkPlayer player = playerObject.GetComponent<NetworkPlayer>();
                if (player != null && player.HasAuthoritativeState)
                {
                    result.Add(player);
                }
            }
            return result;
        }

        private NetworkPlayer SelectScoreWinner(List<NetworkPlayer> participants)
        {
            NetworkPlayer best = null;
            uint bestKills = 0;
            bool bestAlive = false;
            ushort bestHealth = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                NetworkPlayer candidate = participants[i];
                if (candidate == null || !candidate.HasAuthoritativeState)
                {
                    continue;
                }

                NetworkPlayerSnapshot state = candidate.CurrentState;
                uint kills = GetKillsForPlayer(candidate.OwnerClientId);
                bool alive = state.Alive && state.Health > 0;
                bool isBetter = best == null || kills > bestKills ||
                                (kills == bestKills && alive && !bestAlive) ||
                                (kills == bestKills && alive == bestAlive && state.Health > bestHealth) ||
                                (kills == bestKills && alive == bestAlive && state.Health == bestHealth &&
                                 candidate.OwnerClientId < best.OwnerClientId);
                if (isBetter)
                {
                    best = candidate;
                    bestKills = kills;
                    bestAlive = alive;
                    bestHealth = state.Health;
                }
            }
            return best;
        }

        private int FindScoreEntry(ulong playerId)
        {
            for (int i = 0; i < scoreEntries.Count; i++)
            {
                if (scoreEntries[i].PlayerId == playerId)
                {
                    return i;
                }
            }
            return -1;
        }

        private void FinishServerMatch(List<NetworkPlayer> participants, NetworkPlayer winner)
        {
            if (serverMatchEnded)
            {
                return;
            }
            serverMatchActive = false;
            serverMatchEnded = true;
            synchronizedSecondsRemaining.Value = 0;

            NetworkPlayer broadcaster = winner;
            for (int i = 0; i < participants.Count; i++)
            {
                NetworkPlayer player = participants[i];
                if (player == null)
                {
                    continue;
                }
                player.ServerSetMatchFinished();
                if (broadcaster == null)
                {
                    broadcaster = player;
                }
            }

            if (broadcaster == null)
            {
                return;
            }

            NetworkPlayerSnapshot winnerState = winner != null ? winner.CurrentState : default(NetworkPlayerSnapshot);
            CombatEvent matchEvent = new CombatEvent
            {
                Type = CombatEventType.MatchEnded,
                HasWinner = winner != null,
                WinnerId = winner != null ? winner.OwnerClientId : 0UL,
                WinnerName = winner != null
                    ? winnerState.DisplayName
                    : default(Unity.Collections.FixedString64Bytes)
            };
            broadcaster.BroadcastCombatEvent(matchEvent);
        }
    }
}
