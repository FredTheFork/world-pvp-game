using System;

namespace WorldPvp.Phase1.Battles
{
    /// <summary>Server-side categories for suspicious input. Evidence is advisory, never an instant ban.</summary>
    public enum ServerViolationKind : byte
    {
        InvalidPacketSequence,
        ClientTickJump,
        InvalidInputData,
        InputRate,
        MovementSpeed,
        Teleport,
        FireRate,
        Ammo,
        ImpossibleRotation,
        ShotOrigin,
        ShotDirection,
        ArenaEscape
    }

    /// <summary>
    /// A per-player, server-only evidence accumulator. Scores decay with server time and intentionally
    /// do not disconnect or ban players; crossing a review threshold only emits a server log entry.
    /// </summary>
    public sealed class ServerViolationScore
    {
        private readonly int[] evidenceCounts = new int[Enum.GetValues(typeof(ServerViolationKind)).Length];
        private float score;
        private double lastUpdatedAt = double.NaN;

        public float CurrentScore { get { return score; } }

        public int EvidenceCount(ServerViolationKind kind)
        {
            int index = (int)kind;
            return index >= 0 && index < evidenceCounts.Length ? evidenceCounts[index] : 0;
        }

        /// <summary>Apply time-based decay and return the current score.</summary>
        public float Advance(double serverTimeSeconds, float decayPointsPerSecond)
        {
            if (double.IsNaN(serverTimeSeconds) || double.IsInfinity(serverTimeSeconds) || serverTimeSeconds < 0.0)
            {
                return score;
            }

            if (double.IsNaN(lastUpdatedAt))
            {
                lastUpdatedAt = serverTimeSeconds;
                return score;
            }

            if (serverTimeSeconds <= lastUpdatedAt)
            {
                return score;
            }

            double elapsed = serverTimeSeconds - lastUpdatedAt;
            float decay = IsFinite(decayPointsPerSecond) ? Math.Max(0f, decayPointsPerSecond) : 0f;
            score = Math.Max(0f, score - (float)elapsed * decay);
            lastUpdatedAt = serverTimeSeconds;
            return score;
        }

        /// <summary>
        /// Apply decay, add bounded evidence, and return the resulting score. This class never bans,
        /// kicks, or mutates gameplay state as a side effect of a single suspicious observation.
        /// </summary>
        public float AddEvidence(
            ServerViolationKind kind,
            float points,
            double serverTimeSeconds,
            float decayPointsPerSecond)
        {
            Advance(serverTimeSeconds, decayPointsPerSecond);
            int index = (int)kind;
            if (index < 0 || index >= evidenceCounts.Length || !IsFinite(points) || points <= 0f)
            {
                return score;
            }

            if (evidenceCounts[index] < int.MaxValue)
            {
                evidenceCounts[index]++;
            }
            score = Math.Min(100f, score + Math.Min(25f, points));
            return score;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
