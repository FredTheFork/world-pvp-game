using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace WorldPvp.Phase1.Battles
{
    /// <summary>Coarse replicated locomotion and combat-death presentation states.</summary>
    public enum NetworkPlayerMovementState : byte
    {
        Idle,
        Walking,
        Sprinting,
        Jumping,
        Falling,
        Dead,
        Crouching,
        CrouchWalking,
        Landing,
        MatchOver
    }

    /// <summary>
    /// Input intent sampled by a client. It deliberately contains no position or transform values.
    /// Look values are angular deltas in degrees for one fixed simulation step.
    /// </summary>
    public struct NetworkPlayerInputCommand : INetworkSerializable, IEquatable<NetworkPlayerInputCommand>
    {
        public uint Tick;
        public float MoveX;
        public float MoveY;
        public float LookX;
        public float LookY;
        public bool Sprint;
        public bool Crouch;
        public bool JumpPressed;
        public bool FirePressed;
        public bool ReloadPressed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Tick);
            serializer.SerializeValue(ref MoveX);
            serializer.SerializeValue(ref MoveY);
            serializer.SerializeValue(ref LookX);
            serializer.SerializeValue(ref LookY);
            serializer.SerializeValue(ref Sprint);
            serializer.SerializeValue(ref Crouch);
            serializer.SerializeValue(ref JumpPressed);
            serializer.SerializeValue(ref FirePressed);
            serializer.SerializeValue(ref ReloadPressed);
        }

        public bool Equals(NetworkPlayerInputCommand other)
        {
            return Tick == other.Tick &&
                   MoveX.Equals(other.MoveX) && MoveY.Equals(other.MoveY) &&
                   LookX.Equals(other.LookX) && LookY.Equals(other.LookY) &&
                   Sprint == other.Sprint && Crouch == other.Crouch &&
                   JumpPressed == other.JumpPressed && FirePressed == other.FirePressed &&
                   ReloadPressed == other.ReloadPressed;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkPlayerInputCommand && Equals((NetworkPlayerInputCommand)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Tick;
                hash = (hash * 397) ^ MoveX.GetHashCode();
                hash = (hash * 397) ^ MoveY.GetHashCode();
                hash = (hash * 397) ^ LookX.GetHashCode();
                hash = (hash * 397) ^ LookY.GetHashCode();
                hash = (hash * 397) ^ Sprint.GetHashCode();
                hash = (hash * 397) ^ Crouch.GetHashCode();
                hash = (hash * 397) ^ JumpPressed.GetHashCode();
                hash = (hash * 397) ^ FirePressed.GetHashCode();
                hash = (hash * 397) ^ ReloadPressed.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// Server-authored player state in the match's fixed local ENU metre frame. Position remains
    /// geographic through GeospatialWorldManager conversions; it is never copied from a client transform.
    /// </summary>
    public struct NetworkPlayerSnapshot : INetworkSerializable, IEquatable<NetworkPlayerSnapshot>
    {
        public bool Initialized;
        public ulong PlayerId;
        public FixedString64Bytes DisplayName;
        public double EastMeters;
        public double UpMeters;
        public double NorthMeters;
        public double GroundLevelUpMeters;
        public float GroundNormalEast;
        public float GroundNormalUp;
        public float GroundNormalNorth;
        public float YawDegrees;
        public float PitchDegrees;
        public float VelocityEastMetersPerSecond;
        public float VelocityUpMetersPerSecond;
        public float VelocityNorthMetersPerSecond;
        public bool Grounded;
        public bool Crouched;
        public byte LandingTicksRemaining;
        public NetworkPlayerMovementState MovementState;
        public ushort Health;
        public bool Alive;
        public byte Team;
        public FixedString64Bytes EquippedWeaponId;
        public ushort MagazineAmmo;
        public bool IsReloading;
        public bool MatchFinished;
        public uint ServerTick;
        public uint LastProcessedInputTick;
        public uint LastFireInputTick;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Initialized);
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref DisplayName);
            serializer.SerializeValue(ref EastMeters);
            serializer.SerializeValue(ref UpMeters);
            serializer.SerializeValue(ref NorthMeters);
            serializer.SerializeValue(ref GroundLevelUpMeters);
            serializer.SerializeValue(ref GroundNormalEast);
            serializer.SerializeValue(ref GroundNormalUp);
            serializer.SerializeValue(ref GroundNormalNorth);
            serializer.SerializeValue(ref YawDegrees);
            serializer.SerializeValue(ref PitchDegrees);
            serializer.SerializeValue(ref VelocityEastMetersPerSecond);
            serializer.SerializeValue(ref VelocityUpMetersPerSecond);
            serializer.SerializeValue(ref VelocityNorthMetersPerSecond);
            serializer.SerializeValue(ref Grounded);
            serializer.SerializeValue(ref Crouched);
            serializer.SerializeValue(ref LandingTicksRemaining);
            serializer.SerializeValue(ref MovementState);
            serializer.SerializeValue(ref Health);
            serializer.SerializeValue(ref Alive);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref EquippedWeaponId);
            serializer.SerializeValue(ref MagazineAmmo);
            serializer.SerializeValue(ref IsReloading);
            serializer.SerializeValue(ref MatchFinished);
            serializer.SerializeValue(ref ServerTick);
            serializer.SerializeValue(ref LastProcessedInputTick);
            serializer.SerializeValue(ref LastFireInputTick);
        }

        public bool Equals(NetworkPlayerSnapshot other)
        {
            return Initialized == other.Initialized &&
                   PlayerId == other.PlayerId && DisplayName.Equals(other.DisplayName) &&
                   EastMeters.Equals(other.EastMeters) && UpMeters.Equals(other.UpMeters) &&
                   NorthMeters.Equals(other.NorthMeters) && GroundLevelUpMeters.Equals(other.GroundLevelUpMeters) &&
                   GroundNormalEast.Equals(other.GroundNormalEast) && GroundNormalUp.Equals(other.GroundNormalUp) &&
                   GroundNormalNorth.Equals(other.GroundNormalNorth) &&
                   YawDegrees.Equals(other.YawDegrees) && PitchDegrees.Equals(other.PitchDegrees) &&
                   VelocityEastMetersPerSecond.Equals(other.VelocityEastMetersPerSecond) &&
                   VelocityUpMetersPerSecond.Equals(other.VelocityUpMetersPerSecond) &&
                   VelocityNorthMetersPerSecond.Equals(other.VelocityNorthMetersPerSecond) &&
                   Grounded == other.Grounded && Crouched == other.Crouched &&
                   LandingTicksRemaining == other.LandingTicksRemaining && MovementState == other.MovementState &&
                   Health == other.Health && Alive == other.Alive && Team == other.Team &&
                   EquippedWeaponId.Equals(other.EquippedWeaponId) &&
                   MagazineAmmo == other.MagazineAmmo && IsReloading == other.IsReloading &&
                   MatchFinished == other.MatchFinished &&
                   ServerTick == other.ServerTick &&
                   LastProcessedInputTick == other.LastProcessedInputTick &&
                   LastFireInputTick == other.LastFireInputTick;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkPlayerSnapshot && Equals((NetworkPlayerSnapshot)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Initialized.GetHashCode();
                hash = (hash * 397) ^ PlayerId.GetHashCode();
                hash = (hash * 397) ^ DisplayName.GetHashCode();
                hash = (hash * 397) ^ EastMeters.GetHashCode();
                hash = (hash * 397) ^ UpMeters.GetHashCode();
                hash = (hash * 397) ^ NorthMeters.GetHashCode();
                hash = (hash * 397) ^ GroundLevelUpMeters.GetHashCode();
                hash = (hash * 397) ^ GroundNormalEast.GetHashCode();
                hash = (hash * 397) ^ GroundNormalUp.GetHashCode();
                hash = (hash * 397) ^ GroundNormalNorth.GetHashCode();
                hash = (hash * 397) ^ YawDegrees.GetHashCode();
                hash = (hash * 397) ^ PitchDegrees.GetHashCode();
                hash = (hash * 397) ^ VelocityEastMetersPerSecond.GetHashCode();
                hash = (hash * 397) ^ VelocityUpMetersPerSecond.GetHashCode();
                hash = (hash * 397) ^ VelocityNorthMetersPerSecond.GetHashCode();
                hash = (hash * 397) ^ Grounded.GetHashCode();
                hash = (hash * 397) ^ Crouched.GetHashCode();
                hash = (hash * 397) ^ LandingTicksRemaining.GetHashCode();
                hash = (hash * 397) ^ (int)MovementState;
                hash = (hash * 397) ^ Health.GetHashCode();
                hash = (hash * 397) ^ Alive.GetHashCode();
                hash = (hash * 397) ^ Team.GetHashCode();
                hash = (hash * 397) ^ EquippedWeaponId.GetHashCode();
                hash = (hash * 397) ^ MagazineAmmo.GetHashCode();
                hash = (hash * 397) ^ IsReloading.GetHashCode();
                hash = (hash * 397) ^ MatchFinished.GetHashCode();
                hash = (hash * 397) ^ (int)ServerTick;
                hash = (hash * 397) ^ (int)LastProcessedInputTick;
                hash = (hash * 397) ^ (int)LastFireInputTick;
                return hash;
            }
        }
    }

    /// <summary>Deterministic fixed-step kinematic simulation shared by prediction and the session host.</summary>
    public static class NetworkPlayerSimulation
    {
        // NGO NetworkConfig.TickRate is uint. An int constant does not implicitly convert and fails to compile.
        public const uint TickRate = 30;
        public const float TickDeltaSeconds = 1f / TickRate;
        public const float MinimumPitchDegrees = -75f;
        public const float MaximumPitchDegrees = 75f;
        public const float MaximumLookDeltaDegreesPerTick = 120f;

        public static NetworkPlayerSnapshot Step(
            NetworkPlayerSnapshot current,
            NetworkPlayerInputCommand input,
            float deltaSeconds,
            float walkSpeedMetersPerSecond,
            float sprintSpeedMetersPerSecond,
            float crouchSpeedMetersPerSecond,
            float jumpHeightMeters,
            float gravityMetersPerSecondSquared,
            float groundAccelerationMetersPerSecondSquared,
            float groundDecelerationMetersPerSecondSquared,
            float airAccelerationMetersPerSecondSquared,
            float airDecelerationMetersPerSecondSquared,
            float maximumSlopeDegrees,
            bool canStand,
            byte landingPresentationTicks,
            double arenaRadiusMeters,
            float boundaryInsetMeters)
        {
            if (!current.Initialized)
            {
                return current;
            }

            float step = IsFinite(deltaSeconds) ? Mathf.Clamp(deltaSeconds, 0.001f, 0.1f) : TickDeltaSeconds;
            current.ServerTick = unchecked(current.ServerTick + 1);
            if (input.Tick != 0)
            {
                current.LastProcessedInputTick = input.Tick;
            }
            if (!current.Alive || current.Health == 0)
            {
                current.Alive = false;
                current.VelocityEastMetersPerSecond = 0f;
                current.VelocityUpMetersPerSecond = 0f;
                current.VelocityNorthMetersPerSecond = 0f;
                current.MovementState = NetworkPlayerMovementState.Dead;
                return current;
            }
            if (current.MatchFinished)
            {
                current.VelocityEastMetersPerSecond = 0f;
                current.VelocityUpMetersPerSecond = 0f;
                current.VelocityNorthMetersPerSecond = 0f;
                current.MovementState = NetworkPlayerMovementState.MatchOver;
                return current;
            }

            bool landingPresentationActive = current.Grounded && current.LandingTicksRemaining > 0;
            if (landingPresentationActive)
            {
                current.LandingTicksRemaining--;
            }

            bool requestedCrouch = input.Crouch;
            current.Crouched = requestedCrouch || (current.Crouched && !canStand);

            float moveX = ClampUnit(input.MoveX);
            float moveY = ClampUnit(input.MoveY);
            Vector2 move = new Vector2(moveX, moveY);
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            float lookX = ClampFinite(input.LookX, -MaximumLookDeltaDegreesPerTick, MaximumLookDeltaDegreesPerTick);
            float lookY = ClampFinite(input.LookY, -MaximumLookDeltaDegreesPerTick, MaximumLookDeltaDegreesPerTick);
            current.YawDegrees = Mathf.Repeat(current.YawDegrees + lookX, 360f);
            current.PitchDegrees = Mathf.Clamp(
                current.PitchDegrees + lookY,
                MinimumPitchDegrees,
                MaximumPitchDegrees);

            Vector3 groundNormal = GetGroundNormal(current);
            float slopeLimit = Mathf.Clamp(maximumSlopeDegrees, 1f, 89f);
            bool slopeWalkable = Vector3.Angle(groundNormal, Vector3.up) <= slopeLimit;

            float yawRadians = current.YawDegrees * Mathf.Deg2Rad;
            float forwardEast = Mathf.Sin(yawRadians);
            float forwardNorth = Mathf.Cos(yawRadians);
            float rightEast = Mathf.Cos(yawRadians);
            float rightNorth = -Mathf.Sin(yawRadians);
            Vector3 desiredDirection = new Vector3(
                rightEast * move.x + forwardEast * move.y,
                0f,
                rightNorth * move.x + forwardNorth * move.y);

            float requestedSpeed = current.Crouched
                ? Mathf.Max(0f, crouchSpeedMetersPerSecond)
                : input.Sprint
                    ? Mathf.Max(0f, sprintSpeedMetersPerSecond)
                    : Mathf.Max(0f, walkSpeedMetersPerSecond);
            if (desiredDirection.sqrMagnitude > 0.0001f)
            {
                if (current.Grounded && !slopeWalkable)
                {
                    desiredDirection = Vector3.zero;
                }
                else if (current.Grounded)
                {
                    desiredDirection = Vector3.ProjectOnPlane(desiredDirection, groundNormal);
                }
                desiredDirection = desiredDirection.sqrMagnitude > 0.0001f
                    ? desiredDirection.normalized
                    : Vector3.zero;
            }

            Vector2 targetPlanarVelocity = new Vector2(
                desiredDirection.x * requestedSpeed,
                desiredDirection.z * requestedSpeed);
            Vector2 currentPlanarVelocity = new Vector2(
                current.VelocityEastMetersPerSecond,
                current.VelocityNorthMetersPerSecond);
            bool hasMoveInput = move.sqrMagnitude > 0.0001f;
            bool acceleratingIntoDesiredMotion = hasMoveInput && (!current.Grounded || slopeWalkable);
            float acceleration = current.Grounded
                ? acceleratingIntoDesiredMotion
                    ? Mathf.Max(0f, groundAccelerationMetersPerSecondSquared)
                    : Mathf.Max(0f, groundDecelerationMetersPerSecondSquared)
                : hasMoveInput
                    ? Mathf.Max(0f, airAccelerationMetersPerSecondSquared)
                    : Mathf.Max(0f, airDecelerationMetersPerSecondSquared);
            currentPlanarVelocity = Vector2.MoveTowards(
                currentPlanarVelocity,
                targetPlanarVelocity,
                acceleration * step);
            current.VelocityEastMetersPerSecond = currentPlanarVelocity.x;
            current.VelocityNorthMetersPerSecond = currentPlanarVelocity.y;

            float verticalVelocity = current.VelocityUpMetersPerSecond;
            bool jumping = false;
            if (current.Grounded)
            {
                verticalVelocity = slopeWalkable
                    ? -(
                        groundNormal.x * current.VelocityEastMetersPerSecond +
                        groundNormal.z * current.VelocityNorthMetersPerSecond) /
                        Mathf.Max(0.05f, groundNormal.y)
                    : 0f;

                if (input.JumpPressed && !current.Crouched)
                {
                    float jumpHeight = Mathf.Max(0f, jumpHeightMeters);
                    float gravity = Mathf.Max(0.01f, gravityMetersPerSecondSquared);
                    verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
                    current.Grounded = false;
                    current.LandingTicksRemaining = 0;
                    jumping = true;
                }
            }

            if (!current.Grounded)
            {
                if (!jumping)
                {
                    verticalVelocity -= Mathf.Max(0.01f, gravityMetersPerSecondSquared) * step;
                }
            }
            else
            {
                current.GroundLevelUpMeters += verticalVelocity * step;
            }

            current.EastMeters += current.VelocityEastMetersPerSecond * step;
            current.NorthMeters += current.VelocityNorthMetersPerSecond * step;
            current.UpMeters += verticalVelocity * step;
            current.VelocityUpMetersPerSecond = verticalVelocity;

            double groundLevel = IsFinite(current.GroundLevelUpMeters)
                ? current.GroundLevelUpMeters
                : current.UpMeters;
            if (!current.Grounded && current.UpMeters <= groundLevel && verticalVelocity <= 0f)
            {
                current.UpMeters = groundLevel;
                current.VelocityUpMetersPerSecond = 0f;
                current.Grounded = true;
                current.LandingTicksRemaining = landingPresentationTicks;
            }
            else if (current.Grounded)
            {
                current.UpMeters = current.GroundLevelUpMeters;
            }

            double safeRadius = IsFinite(arenaRadiusMeters) ? Math.Max(0.0, arenaRadiusMeters) : 0.0;
            double allowedRadius = Math.Max(0.0, safeRadius - Math.Max(0.0, boundaryInsetMeters));
            double horizontalDistance = Math.Sqrt(
                (current.EastMeters * current.EastMeters) +
                (current.NorthMeters * current.NorthMeters));
            if (horizontalDistance > allowedRadius)
            {
                double scale = horizontalDistance > 0.0 ? allowedRadius / horizontalDistance : 0.0;
                current.EastMeters *= scale;
                current.NorthMeters *= scale;
                current.VelocityEastMetersPerSecond = 0f;
                current.VelocityNorthMetersPerSecond = 0f;
            }

            float planarSpeed = Mathf.Sqrt(
                (current.VelocityEastMetersPerSecond * current.VelocityEastMetersPerSecond) +
                (current.VelocityNorthMetersPerSecond * current.VelocityNorthMetersPerSecond));
            if (!current.Grounded)
            {
                current.MovementState = current.VelocityUpMetersPerSecond > 0f
                    ? NetworkPlayerMovementState.Jumping
                    : NetworkPlayerMovementState.Falling;
            }
            else if (current.LandingTicksRemaining > 0)
            {
                current.MovementState = NetworkPlayerMovementState.Landing;
            }
            else if (current.Crouched)
            {
                current.MovementState = planarSpeed < 0.01f
                    ? NetworkPlayerMovementState.Crouching
                    : NetworkPlayerMovementState.CrouchWalking;
            }
            else if (planarSpeed < 0.01f)
            {
                current.MovementState = NetworkPlayerMovementState.Idle;
            }
            else
            {
                current.MovementState = input.Sprint
                    ? NetworkPlayerMovementState.Sprinting
                    : NetworkPlayerMovementState.Walking;
            }

            return current;
        }

        public static NetworkPlayerSnapshot Interpolate(
            NetworkPlayerSnapshot from,
            NetworkPlayerSnapshot to,
            float amount)
        {
            float t = Mathf.Clamp01(amount);
            NetworkPlayerSnapshot result = to;
            result.EastMeters = Lerp(from.EastMeters, to.EastMeters, t);
            result.UpMeters = Lerp(from.UpMeters, to.UpMeters, t);
            result.NorthMeters = Lerp(from.NorthMeters, to.NorthMeters, t);
            result.GroundLevelUpMeters = Lerp(from.GroundLevelUpMeters, to.GroundLevelUpMeters, t);
            Vector3 normal = Vector3.Lerp(GetGroundNormal(from), GetGroundNormal(to), t).normalized;
            result.GroundNormalEast = normal.x;
            result.GroundNormalUp = normal.y;
            result.GroundNormalNorth = normal.z;
            result.YawDegrees = Mathf.LerpAngle(from.YawDegrees, to.YawDegrees, t);
            result.PitchDegrees = Mathf.Lerp(from.PitchDegrees, to.PitchDegrees, t);
            result.VelocityEastMetersPerSecond = Mathf.Lerp(
                from.VelocityEastMetersPerSecond, to.VelocityEastMetersPerSecond, t);
            result.VelocityUpMetersPerSecond = Mathf.Lerp(
                from.VelocityUpMetersPerSecond, to.VelocityUpMetersPerSecond, t);
            result.VelocityNorthMetersPerSecond = Mathf.Lerp(
                from.VelocityNorthMetersPerSecond, to.VelocityNorthMetersPerSecond, t);
            result.Grounded = t < 0.5f ? from.Grounded : to.Grounded;
            result.Crouched = t < 0.5f ? from.Crouched : to.Crouched;
            result.LandingTicksRemaining = t < 0.5f
                ? from.LandingTicksRemaining
                : to.LandingTicksRemaining;
            result.MovementState = t < 0.5f ? from.MovementState : to.MovementState;
            return result;
        }

        public static NetworkPlayerSnapshot Extrapolate(
            NetworkPlayerSnapshot state,
            float seconds)
        {
            float step = Mathf.Clamp(seconds, 0f, 0.1f);
            state.EastMeters += state.VelocityEastMetersPerSecond * step;
            state.UpMeters += state.VelocityUpMetersPerSecond * step;
            state.NorthMeters += state.VelocityNorthMetersPerSecond * step;
            return state;
        }

        private static Vector3 GetGroundNormal(NetworkPlayerSnapshot state)
        {
            Vector3 normal = new Vector3(
                state.GroundNormalEast,
                state.GroundNormalUp,
                state.GroundNormalNorth);
            return normal.sqrMagnitude > 0.25f ? normal.normalized : Vector3.up;
        }

        private static float ClampUnit(float value)
        {
            return IsFinite(value) ? Mathf.Clamp(value, -1f, 1f) : 0f;
        }

        private static float ClampFinite(float value, float minimum, float maximum)
        {
            return IsFinite(value) ? Mathf.Clamp(value, minimum, maximum) : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double Lerp(double from, double to, float amount)
        {
            return from + ((to - from) * amount);
        }
    }
}
