using NUnit.Framework;
using UnityEngine;
using WorldPvp.Phase1.Battles;

namespace WorldPvp.Phase1.Tests
{
    public sealed class NetworkPlayerSimulationTests
    {
        private const float WalkSpeed = 4.5f;
        private const float SprintSpeed = 7f;
        private const float CrouchSpeed = 2.2f;
        private const float JumpHeight = 1.2f;
        private const float Gravity = 22f;
        private const float FastAcceleration = 1000f;
        private const float GroundDeceleration = 30f;
        private const float AirAcceleration = 7f;
        private const float AirDeceleration = 3f;
        private const float MaximumSlopeDegrees = 48f;
        private const byte LandingTicks = 5;

        [Test]
        public void InputCommandAdvancesFixedStepAndAcknowledgementWithoutPositionFields()
        {
            NetworkPlayerSnapshot initial = CreateState();
            NetworkPlayerInputCommand input = new NetworkPlayerInputCommand
            {
                Tick = 7,
                MoveY = 1f
            };

            NetworkPlayerSnapshot result = Step(initial, input, 100.0);

            Assert.That(result.ServerTick, Is.EqualTo(1));
            Assert.That(result.LastProcessedInputTick, Is.EqualTo(7));
            Assert.That(result.NorthMeters, Is.EqualTo(WalkSpeed / NetworkPlayerSimulation.TickRate).Within(0.0001));
            Assert.That(result.EastMeters, Is.EqualTo(0.0).Within(0.0001));
            Assert.That(result.MovementState, Is.EqualTo(NetworkPlayerMovementState.Walking));
        }

        [Test]
        public void AccelerationAndDecelerationAreFixedStepAndTunable()
        {
            NetworkPlayerSnapshot initial = CreateState();
            NetworkPlayerSnapshot accelerating = StepWithAcceleration(
                initial,
                new NetworkPlayerInputCommand { Tick = 1, MoveY = 1f },
                100.0,
                6f,
                12f);
            Assert.That(accelerating.VelocityNorthMetersPerSecond,
                Is.EqualTo(6f / NetworkPlayerSimulation.TickRate).Within(0.0001f));
            Assert.That(accelerating.VelocityNorthMetersPerSecond, Is.LessThan(WalkSpeed));

            NetworkPlayerSnapshot decelerating = StepWithAcceleration(
                accelerating,
                new NetworkPlayerInputCommand { Tick = 2 },
                100.0,
                6f,
                12f);
            Assert.That(decelerating.VelocityNorthMetersPerSecond, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(decelerating.NorthMeters, Is.GreaterThanOrEqualTo(accelerating.NorthMeters));
        }

        [Test]
        public void DiagonalMovementIsNormalizedAndSprintIsServerSimulated()
        {
            NetworkPlayerInputCommand input = new NetworkPlayerInputCommand
            {
                Tick = 1,
                MoveX = 1f,
                MoveY = 1f,
                Sprint = true
            };

            NetworkPlayerSnapshot result = Step(CreateState(), input, 100.0);
            float planarDistance = Mathf.Sqrt(
                (float)(result.EastMeters * result.EastMeters + result.NorthMeters * result.NorthMeters));

            Assert.That(planarDistance, Is.EqualTo(SprintSpeed / NetworkPlayerSimulation.TickRate).Within(0.0001));
            Assert.That(result.MovementState, Is.EqualTo(NetworkPlayerMovementState.Sprinting));
        }

        [Test]
        public void CrouchUsesItsOwnSpeedAndCannotStandWhenClearanceIsDenied()
        {
            NetworkPlayerInputCommand crouchInput = new NetworkPlayerInputCommand
            {
                Tick = 1,
                MoveY = 1f,
                Sprint = true,
                Crouch = true
            };
            NetworkPlayerSnapshot crouched = Step(CreateState(), crouchInput, 100.0);

            Assert.That(crouched.Crouched, Is.True);
            Assert.That(crouched.VelocityNorthMetersPerSecond, Is.EqualTo(CrouchSpeed).Within(0.0001f));
            Assert.That(crouched.MovementState, Is.EqualTo(NetworkPlayerMovementState.CrouchWalking));

            NetworkPlayerInputCommand standInput = new NetworkPlayerInputCommand { Tick = 2, MoveY = 1f };
            NetworkPlayerSnapshot blocked = StepWithOptions(crouched, standInput, 100.0, false, 0f, GroundDeceleration);
            Assert.That(blocked.Crouched, Is.True);
            Assert.That(blocked.MovementState, Is.EqualTo(NetworkPlayerMovementState.CrouchWalking));

            NetworkPlayerSnapshot stood = StepWithOptions(blocked, standInput, 100.0, true, FastAcceleration, GroundDeceleration);
            Assert.That(stood.Crouched, Is.False);
        }

        [Test]
        public void GroundNormalProjectsMotionAlongWalkableSlopeAndRejectsTooSteepGround()
        {
            NetworkPlayerSnapshot slopeState = CreateState();
            float radians = 30f * Mathf.Deg2Rad;
            slopeState.GroundNormalEast = Mathf.Sin(radians);
            slopeState.GroundNormalUp = Mathf.Cos(radians);
            NetworkPlayerSnapshot onSlope = Step(slopeState,
                new NetworkPlayerInputCommand { Tick = 1, MoveX = 1f }, 100.0);
            Assert.That(onSlope.Grounded, Is.True);
            Assert.That(onSlope.VelocityUpMetersPerSecond, Is.LessThan(0f));

            NetworkPlayerSnapshot steepState = CreateState();
            steepState.GroundNormalEast = 0.9f;
            steepState.GroundNormalUp = 0.435f;
            NetworkPlayerSnapshot blocked = Step(steepState,
                new NetworkPlayerInputCommand { Tick = 1, MoveX = 1f }, 100.0);
            Assert.That(blocked.Grounded, Is.True);
            Assert.That(blocked.EastMeters, Is.EqualTo(0.0).Within(0.0001));
            Assert.That(blocked.MovementState, Is.EqualTo(NetworkPlayerMovementState.Idle));
        }

        [Test]
        public void JumpUsesFixedStepGravityAndPresentsLandingState()
        {
            NetworkPlayerSnapshot initial = CreateState();
            NetworkPlayerSnapshot airborne = Step(
                initial,
                new NetworkPlayerInputCommand { Tick = 1, JumpPressed = true },
                100.0);

            Assert.That(airborne.Grounded, Is.False);
            Assert.That(airborne.MovementState, Is.EqualTo(NetworkPlayerMovementState.Jumping));
            Assert.That(airborne.UpMeters, Is.GreaterThan(initial.UpMeters));

            NetworkPlayerSnapshot landing = airborne;
            for (uint tick = 2; tick < 90 && !landing.Grounded; tick++)
            {
                landing = Step(
                    landing,
                    new NetworkPlayerInputCommand { Tick = tick },
                    100.0);
            }

            Assert.That(landing.Grounded, Is.True);
            Assert.That(landing.UpMeters, Is.EqualTo(landing.GroundLevelUpMeters).Within(0.0001));
            Assert.That(landing.VelocityUpMetersPerSecond, Is.EqualTo(0f));
            Assert.That(landing.MovementState, Is.EqualTo(NetworkPlayerMovementState.Landing));
            Assert.That(landing.LandingTicksRemaining, Is.EqualTo(LandingTicks));
        }

        [Test]
        public void ArenaRadiusIsEnforcedByTheSimulator()
        {
            NetworkPlayerSnapshot initial = CreateState();
            initial.EastMeters = 4.9;
            NetworkPlayerSnapshot result = Step(
                initial,
                new NetworkPlayerInputCommand { Tick = 1, MoveX = 1f },
                5.0);

            Assert.That(result.EastMeters, Is.EqualTo(4.7).Within(0.0001));
            Assert.That(result.NorthMeters, Is.EqualTo(0.0).Within(0.0001));
            Assert.That(result.VelocityEastMetersPerSecond, Is.EqualTo(0f));
            Assert.That(result.MovementState, Is.EqualTo(NetworkPlayerMovementState.Idle));
        }

        [Test]
        public void LookDeltasAreClampedAndFireIntentCannotClaimServerApprovedShot()
        {
            NetworkPlayerSnapshot initial = CreateState();
            initial.LastFireInputTick = 3;
            NetworkPlayerSnapshot result = Step(
                initial,
                new NetworkPlayerInputCommand { Tick = 9, LookX = 1000f, LookY = 1000f, FirePressed = true },
                100.0);

            Assert.That(result.YawDegrees, Is.EqualTo(NetworkPlayerSimulation.MaximumLookDeltaDegreesPerTick).Within(0.001));
            Assert.That(result.PitchDegrees, Is.EqualTo(NetworkPlayerSimulation.MaximumPitchDegrees).Within(0.001));
            Assert.That(result.LastProcessedInputTick, Is.EqualTo(9));
            Assert.That(result.LastFireInputTick, Is.EqualTo(3),
                "Only authoritative fire resolution may advance the accepted-shot marker.");
            Assert.That(result.Health, Is.EqualTo(100));
            Assert.That(result.Alive, Is.True);
        }

        [Test]
        public void RemoteInterpolationUsesShortestYawPathAndInterpolatesEnuPosition()
        {
            NetworkPlayerSnapshot from = CreateState();
            from.EastMeters = 0.0;
            from.YawDegrees = 359f;
            NetworkPlayerSnapshot to = CreateState();
            to.EastMeters = 10.0;
            to.YawDegrees = 1f;

            NetworkPlayerSnapshot midpoint = NetworkPlayerSimulation.Interpolate(from, to, 0.5f);

            Assert.That(midpoint.EastMeters, Is.EqualTo(5.0).Within(0.0001));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, midpoint.YawDegrees)), Is.LessThan(0.001f));
        }

        [Test]
        public void RemoteExtrapolationIsBoundedToOneHundredMilliseconds()
        {
            NetworkPlayerSnapshot initial = CreateState();
            initial.VelocityEastMetersPerSecond = 2f;

            NetworkPlayerSnapshot result = NetworkPlayerSimulation.Extrapolate(initial, 10f);

            Assert.That(result.EastMeters, Is.EqualTo(0.2).Within(0.0001));
        }

        private static NetworkPlayerSnapshot Step(
            NetworkPlayerSnapshot state,
            NetworkPlayerInputCommand command,
            double arenaRadius)
        {
            return StepWithOptions(state, command, arenaRadius, true, FastAcceleration, GroundDeceleration);
        }

        private static NetworkPlayerSnapshot StepWithAcceleration(
            NetworkPlayerSnapshot state,
            NetworkPlayerInputCommand command,
            double arenaRadius,
            float acceleration,
            float deceleration)
        {
            return StepWithOptions(state, command, arenaRadius, true, acceleration, deceleration);
        }

        private static NetworkPlayerSnapshot StepWithOptions(
            NetworkPlayerSnapshot state,
            NetworkPlayerInputCommand command,
            double arenaRadius,
            bool canStand,
            float acceleration,
            float deceleration)
        {
            return NetworkPlayerSimulation.Step(
                state,
                command,
                NetworkPlayerSimulation.TickDeltaSeconds,
                WalkSpeed,
                SprintSpeed,
                CrouchSpeed,
                JumpHeight,
                Gravity,
                acceleration,
                deceleration,
                AirAcceleration,
                AirDeceleration,
                MaximumSlopeDegrees,
                canStand,
                LandingTicks,
                arenaRadius,
                0.3f);
        }

        private static NetworkPlayerSnapshot CreateState()
        {
            return new NetworkPlayerSnapshot
            {
                Initialized = true,
                PlayerId = 1,
                DisplayName = new Unity.Collections.FixedString64Bytes("Test Player"),
                EastMeters = 0.0,
                UpMeters = 0.0,
                NorthMeters = 0.0,
                GroundLevelUpMeters = 0.0,
                GroundNormalUp = 1f,
                YawDegrees = 0f,
                PitchDegrees = 18f,
                Grounded = true,
                Health = 100,
                Alive = true
            };
        }
    }
}
