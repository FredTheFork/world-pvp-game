using System.Collections.Generic;
using UnityEngine;
using WorldPvp.Phase1.Battles;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Combat
{
    /// <summary>
    /// Bounded server-time history for shot-time player-state reconstruction. Entries are local ENU
    /// gameplay coordinates (metres), never client-authored world transforms.
    /// </summary>
    public sealed class LagCompensationHistory
    {
        public const float DefaultWindowSeconds = 0.4f;
        public const float MinimumWindowSeconds = 0.25f;
        public const float MaximumWindowSeconds = 0.5f;

        private struct Entry
        {
            public double ServerTime;
            public NetworkPlayerSnapshot Snapshot;

            public Entry(double serverTime, NetworkPlayerSnapshot snapshot)
            {
                ServerTime = serverTime;
                Snapshot = snapshot;
            }
        }

        private readonly List<Entry> entries = new List<Entry>(32);
        private float historyWindowSeconds;

        public LagCompensationHistory(float requestedWindowSeconds = DefaultWindowSeconds)
        {
            ConfigureWindow(requestedWindowSeconds);
        }

        public int Count { get { return entries.Count; } }
        public float HistoryWindowSeconds { get { return historyWindowSeconds; } }

        public void ConfigureWindow(float requestedWindowSeconds)
        {
            if (float.IsNaN(requestedWindowSeconds) || float.IsInfinity(requestedWindowSeconds))
            {
                requestedWindowSeconds = DefaultWindowSeconds;
            }

            historyWindowSeconds = Mathf.Clamp(
                requestedWindowSeconds,
                MinimumWindowSeconds,
                MaximumWindowSeconds);
        }

        public void Clear()
        {
            entries.Clear();
        }

        /// <summary>Record one authoritative snapshot and prune data older than the configured window.</summary>
        public void Record(double serverTimeSeconds, NetworkPlayerSnapshot snapshot)
        {
            if (!IsFinite(serverTimeSeconds) || serverTimeSeconds < 0.0 || !snapshot.Initialized ||
                !GeoPosition.IsFinite(snapshot.EastMeters, snapshot.NorthMeters, snapshot.UpMeters))
            {
                return;
            }

            if (entries.Count > 0)
            {
                Entry last = entries[entries.Count - 1];
                if (serverTimeSeconds < last.ServerTime)
                {
                    return;
                }

                if (serverTimeSeconds == last.ServerTime)
                {
                    entries[entries.Count - 1] = new Entry(serverTimeSeconds, snapshot);
                    Prune(serverTimeSeconds);
                    return;
                }
            }

            entries.Add(new Entry(serverTimeSeconds, snapshot));
            Prune(serverTimeSeconds);
        }

        /// <summary>
        /// Reconstruct the authoritative player state at a server shot time. A small (100 ms) edge
        /// allowance supports the latest fixed-tick snapshot; requests outside it fail closed.
        /// </summary>
        public bool TrySample(double shotTimeSeconds, out NetworkPlayerSnapshot snapshot)
        {
            snapshot = default(NetworkPlayerSnapshot);
            if (entries.Count == 0 || !IsFinite(shotTimeSeconds))
            {
                return false;
            }

            Entry first = entries[0];
            Entry last = entries[entries.Count - 1];
            const double edgeAllowanceSeconds = 0.1;

            if (shotTimeSeconds < first.ServerTime)
            {
                if (first.ServerTime - shotTimeSeconds > edgeAllowanceSeconds)
                {
                    return false;
                }
                snapshot = first.Snapshot;
                return true;
            }

            if (shotTimeSeconds >= last.ServerTime)
            {
                if (shotTimeSeconds - last.ServerTime > edgeAllowanceSeconds)
                {
                    return false;
                }
                snapshot = last.Snapshot;
                return true;
            }

            int low = 0;
            int high = entries.Count - 1;
            while (low + 1 < high)
            {
                int middle = low + ((high - low) / 2);
                if (entries[middle].ServerTime <= shotTimeSeconds)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            Entry from = entries[low];
            Entry to = entries[high];
            double duration = to.ServerTime - from.ServerTime;
            float blend = duration <= 0.0
                ? 0f
                : Mathf.Clamp01((float)((shotTimeSeconds - from.ServerTime) / duration));
            snapshot = InterpolateShotState(from.Snapshot, to.Snapshot, blend);
            return snapshot.Initialized;
        }

        private void Prune(double newestTime)
        {
            double cutoff = newestTime - historyWindowSeconds;
            // Keep one sample immediately before the cutoff so shot times at the window edge can be
            // interpolated rather than clamped to a newer transform.
            while (entries.Count > 2 && entries[1].ServerTime < cutoff)
            {
                entries.RemoveAt(0);
            }
        }

        private static NetworkPlayerSnapshot InterpolateShotState(
            NetworkPlayerSnapshot from,
            NetworkPlayerSnapshot to,
            float blend)
        {
            NetworkPlayerSnapshot result = NetworkPlayerSimulation.Interpolate(from, to, blend);

            // Discrete combat transitions take effect at their recorded server timestamp. Do not
            // make a player appear dead or a match finished for a shot that predates that transition.
            if (from.Alive && from.Health > 0 && (!to.Alive || to.Health == 0))
            {
                result.Alive = true;
                result.Health = from.Health;
            }
            else if (!from.Alive || from.Health == 0)
            {
                result.Alive = false;
                result.Health = 0;
            }

            if (from.MatchFinished && !to.MatchFinished)
            {
                result.MatchFinished = true;
            }
            else if (!from.MatchFinished && to.MatchFinished)
            {
                result.MatchFinished = false;
            }

            result.Crouched = blend < 1f ? from.Crouched : to.Crouched;
            result.YawDegrees = Mathf.LerpAngle(from.YawDegrees, to.YawDegrees, blend);
            result.PitchDegrees = Mathf.Lerp(from.PitchDegrees, to.PitchDegrees, blend);
            return result;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
