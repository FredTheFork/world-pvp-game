using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace WorldPvp.Phase1.Combat
{
    public enum CombatHitboxZone : byte
    {
        None,
        Head,
        Torso,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg
    }

    public enum CombatEventType : byte
    {
        ShotResolved,
        PlayerDied,
        MatchEnded
    }

    /// <summary>
    /// Client-authored fire intent only. Origin is arena-local ENU metres and direction is a unit
    /// vector in the same basis. It deliberately carries no target, hit, damage, or health result.
    /// </summary>
    public struct FireCommand : INetworkSerializable
    {
        public uint Tick;
        public double OriginEastMeters;
        public double OriginUpMeters;
        public double OriginNorthMeters;
        public float DirectionEast;
        public float DirectionUp;
        public float DirectionNorth;
        public FixedString64Bytes WeaponId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref OriginEastMeters);
            serializer.SerializeValue(ref OriginUpMeters);
            serializer.SerializeValue(ref OriginNorthMeters);
            serializer.SerializeValue(ref DirectionEast);
            serializer.SerializeValue(ref DirectionUp);
            serializer.SerializeValue(ref DirectionNorth);
            serializer.SerializeValue(ref WeaponId);
        }

        public Vector3 Direction
        {
            get { return new Vector3(DirectionEast, DirectionUp, DirectionNorth); }
        }
    }

    /// <summary>Server-authored public scoreboard row, replicated by the scene match object.</summary>
    public struct MatchScoreEntry : INetworkSerializable, System.IEquatable<MatchScoreEntry>
    {
        public ulong PlayerId;
        public uint Kills;
        public FixedString64Bytes DisplayName;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref DisplayName);
        }

        public bool Equals(MatchScoreEntry other)
        {
            return PlayerId == other.PlayerId && Kills == other.Kills && DisplayName.Equals(other.DisplayName);
        }

        public override bool Equals(object obj)
        {
            return obj is MatchScoreEntry && Equals((MatchScoreEntry)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = PlayerId.GetHashCode();
                hash = (hash * 397) ^ (int)Kills;
                hash = (hash * 397) ^ DisplayName.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>Server-authored feedback replicated after authoritative firing/damage resolution.</summary>
    public struct CombatEvent : INetworkSerializable
    {
        public CombatEventType Type;
        public uint FireTick;
        public ulong ShooterId;
        public ulong VictimId;
        public ulong WinnerId;
        public bool HasWinner;
        public bool Hit;
        public bool Killed;
        public bool HasImpact;
        public CombatHitboxZone HitboxZone;
        public ushort Damage;
        public ushort RemainingHealth;
        public FixedString64Bytes WeaponId;
        public FixedString64Bytes ShooterName;
        public FixedString64Bytes VictimName;
        public FixedString64Bytes WinnerName;
        public double OriginEastMeters;
        public double OriginUpMeters;
        public double OriginNorthMeters;
        public double ImpactEastMeters;
        public double ImpactUpMeters;
        public double ImpactNorthMeters;
        public float DirectionEast;
        public float DirectionUp;
        public float DirectionNorth;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Type);
            serializer.SerializeValue(ref FireTick);
            serializer.SerializeValue(ref ShooterId);
            serializer.SerializeValue(ref VictimId);
            serializer.SerializeValue(ref WinnerId);
            serializer.SerializeValue(ref HasWinner);
            serializer.SerializeValue(ref Hit);
            serializer.SerializeValue(ref Killed);
            serializer.SerializeValue(ref HasImpact);
            serializer.SerializeValue(ref HitboxZone);
            serializer.SerializeValue(ref Damage);
            serializer.SerializeValue(ref RemainingHealth);
            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(ref ShooterName);
            serializer.SerializeValue(ref VictimName);
            serializer.SerializeValue(ref WinnerName);
            serializer.SerializeValue(ref OriginEastMeters);
            serializer.SerializeValue(ref OriginUpMeters);
            serializer.SerializeValue(ref OriginNorthMeters);
            serializer.SerializeValue(ref ImpactEastMeters);
            serializer.SerializeValue(ref ImpactUpMeters);
            serializer.SerializeValue(ref ImpactNorthMeters);
            serializer.SerializeValue(ref DirectionEast);
            serializer.SerializeValue(ref DirectionUp);
            serializer.SerializeValue(ref DirectionNorth);
        }

        public Vector3 Direction
        {
            get { return new Vector3(DirectionEast, DirectionUp, DirectionNorth); }
        }
    }

    /// <summary>Shared, allocation-free validation and spread math for client aim and server shots.</summary>
    public static class CombatMath
    {
        public static Vector3 AimDirectionFromYawPitch(float yawDegrees, float pitchDegrees)
        {
            if (!IsFinite(yawDegrees) || !IsFinite(pitchDegrees))
            {
                return Vector3.zero;
            }

            float yaw = yawDegrees * Mathf.Deg2Rad;
            float pitch = pitchDegrees * Mathf.Deg2Rad;
            float horizontal = Mathf.Cos(pitch);
            return new Vector3(
                Mathf.Sin(yaw) * horizontal,
                -Mathf.Sin(pitch),
                Mathf.Cos(yaw) * horizontal).normalized;
        }

        public static bool TryNormalizeDirection(Vector3 direction, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (!IsFinite(direction.x) || !IsFinite(direction.y) || !IsFinite(direction.z))
            {
                return false;
            }

            float magnitudeSquared = direction.sqrMagnitude;
            if (magnitudeSquared < 0.81f || magnitudeSquared > 1.21f)
            {
                return false;
            }
            normalized = direction / Mathf.Sqrt(magnitudeSquared);
            return true;
        }

        public static bool IsDirectionWithinTolerance(Vector3 submitted, Vector3 authoritative, float degrees)
        {
            Vector3 normalizedSubmitted;
            Vector3 normalizedAuthoritative;
            if (!TryNormalizeDirection(submitted, out normalizedSubmitted) ||
                !TryNormalizeDirection(authoritative, out normalizedAuthoritative) ||
                !IsFinite(degrees) || degrees < 0f || degrees > 90f)
            {
                return false;
            }

            float minimumDot = Mathf.Cos(degrees * Mathf.Deg2Rad);
            return Vector3.Dot(normalizedSubmitted, normalizedAuthoritative) >= minimumDot;
        }

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>Deterministic cone spread derived only by the server from owner/tick/weapon seed.</summary>
        public static Vector3 ApplySpread(Vector3 direction, float spreadDegrees, uint seed)
        {
            Vector3 forward;
            if (!TryNormalizeDirection(direction, out forward) ||
                !IsFinite(spreadDegrees) || spreadDegrees <= 0f)
            {
                return forward;
            }

            float radius = Mathf.Tan(Mathf.Clamp(spreadDegrees, 0f, 20f) * Mathf.Deg2Rad);
            uint random = seed == 0u ? 0x9E3779B9u : seed;
            float radial = Mathf.Sqrt(NextUnit(ref random)) * radius;
            float angle = NextUnit(ref random) * Mathf.PI * 2f;
            Vector3 right = Vector3.Cross(forward, Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.98f
                ? Vector3.up
                : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(right, forward).normalized;
            Vector3 result = forward +
                             right * (Mathf.Cos(angle) * radial) +
                             up * (Mathf.Sin(angle) * radial);
            return result.normalized;
        }

        private static float NextUnit(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFFu) / 16777216f;
        }
    }
}
