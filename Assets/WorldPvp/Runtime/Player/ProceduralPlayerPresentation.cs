using UnityEngine;
using WorldPvp.Phase1.Battles;
using WorldPvp.Phase1.Configuration;

namespace WorldPvp.Phase1.Player
{
    /// <summary>
    /// Small asset-free locomotion/death presentation used by the first-person client and
    /// interpolated remote avatars. It consumes replicated states; it does not simulate gameplay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralPlayerPresentation : MonoBehaviour
    {
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Transform firstPersonLeftArm;
        [SerializeField] private Transform firstPersonRightArm;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Transform torso;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftArm;
        [SerializeField] private Transform rightArm;
        [SerializeField] private Transform leftLeg;
        [SerializeField] private Transform rightLeg;
        [SerializeField] private PhaseOneWorldSettings settings;
        private NetworkPlayerSnapshot currentState;
        private NetworkPlayerMovementState previousMovementState;
        private bool configuredFirstPerson;
        private bool configuredRemote;
        private float landingElapsedSeconds;
        private float deathPoseBlend;
        private Vector3 torsoBasePosition;
        private Vector3 headBasePosition;
        private Vector3 firstPersonLeftBasePosition;
        private Vector3 firstPersonRightBasePosition;
        private Quaternion firstPersonLeftBaseRotation;
        private Quaternion firstPersonRightBaseRotation;
        private Quaternion leftArmBaseRotation;
        private Quaternion rightArmBaseRotation;
        private Quaternion leftLegBaseRotation;
        private Quaternion rightLegBaseRotation;

        public void ConfigureFirstPerson(
            Transform viewPivot,
            Transform leftHandVisual,
            Transform rightHandVisual,
            PhaseOneWorldSettings worldSettings)
        {
            cameraPivot = viewPivot;
            firstPersonLeftArm = leftHandVisual;
            firstPersonRightArm = rightHandVisual;
            settings = worldSettings;
            configuredFirstPerson = cameraPivot != null;

            if (firstPersonLeftArm != null)
            {
                firstPersonLeftBasePosition = firstPersonLeftArm.localPosition;
                firstPersonLeftBaseRotation = firstPersonLeftArm.localRotation;
            }
            if (firstPersonRightArm != null)
            {
                firstPersonRightBasePosition = firstPersonRightArm.localPosition;
                firstPersonRightBaseRotation = firstPersonRightArm.localRotation;
            }
        }

        public void ConfigureRemote(
            Transform animatedRoot,
            Transform torsoVisual,
            Transform headVisual,
            Transform leftArmVisual,
            Transform rightArmVisual,
            Transform leftLegVisual,
            Transform rightLegVisual,
            PhaseOneWorldSettings worldSettings)
        {
            modelRoot = animatedRoot;
            torso = torsoVisual;
            head = headVisual;
            leftArm = leftArmVisual;
            rightArm = rightArmVisual;
            leftLeg = leftLegVisual;
            rightLeg = rightLegVisual;
            settings = worldSettings;
            configuredRemote = modelRoot != null;

            if (torso != null) torsoBasePosition = torso.localPosition;
            if (head != null) headBasePosition = head.localPosition;
            if (leftArm != null) leftArmBaseRotation = leftArm.localRotation;
            if (rightArm != null) rightArmBaseRotation = rightArm.localRotation;
            if (leftLeg != null) leftLegBaseRotation = leftLeg.localRotation;
            if (rightLeg != null) rightLegBaseRotation = rightLeg.localRotation;
        }

        public void SetState(NetworkPlayerSnapshot state)
        {
            if (!state.Initialized)
            {
                return;
            }
            if (state.MovementState != previousMovementState)
            {
                if (state.MovementState == NetworkPlayerMovementState.Landing)
                {
                    landingElapsedSeconds = 0f;
                }
                if (state.MovementState == NetworkPlayerMovementState.Dead)
                {
                    deathPoseBlend = 0f;
                }
                previousMovementState = state.MovementState;
            }
            currentState = state;
        }

        private void Update()
        {
            if (!currentState.Initialized)
            {
                return;
            }

            float delta = Mathf.Max(0f, Time.unscaledDeltaTime);
            if (currentState.MovementState == NetworkPlayerMovementState.Landing)
            {
                landingElapsedSeconds += delta;
            }
            else
            {
                landingElapsedSeconds = 0f;
            }

            if (configuredFirstPerson)
            {
                UpdateFirstPersonView(delta);
            }
            if (configuredRemote)
            {
                UpdateRemoteAvatar(delta);
            }
        }

        private void UpdateFirstPersonView(float delta)
        {
            // The view rig is unparented while the local owner spectates after a server death.
            // Its world pose is then controlled by PhaseSevenCombatHud, not locomotion bob.
            if (cameraPivot == null || cameraPivot.parent == null)
            {
                return;
            }

            float eyeHeight = settings != null
                ? currentState.Crouched
                    ? settings.CrouchEyeHeightMeters
                    : settings.FirstPersonEyeHeightMeters
                : currentState.Crouched ? 1.0f : 1.62f;
            float speed = PlanarSpeed(currentState);
            float frequency = settings != null ? settings.CameraBobFrequencyHertz : 2f;
            float amplitude = 0f;
            switch (currentState.MovementState)
            {
                case NetworkPlayerMovementState.Walking:
                    amplitude = settings != null ? settings.WalkingCameraBobMeters : 0.025f;
                    break;
                case NetworkPlayerMovementState.Sprinting:
                    amplitude = settings != null ? settings.RunningCameraBobMeters : 0.045f;
                    frequency *= 1.45f;
                    break;
                case NetworkPlayerMovementState.CrouchWalking:
                    amplitude = settings != null ? settings.CrouchingCameraBobMeters : 0.012f;
                    frequency *= 0.75f;
                    break;
            }

            float bob = speed > 0.1f
                ? Mathf.Sin(Time.unscaledTime * frequency * Mathf.PI * 2f) * amplitude
                : 0f;
            float landingDip = GetLandingPulse(landingElapsedSeconds, settings);
            float targetY = eyeHeight + bob - landingDip;
            Vector3 pivotPosition = cameraPivot.localPosition;
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, delta) * 16f);
            pivotPosition.y = Mathf.Lerp(pivotPosition.y, targetY, blend);
            cameraPivot.localPosition = pivotPosition;

            float swing = currentState.MovementState == NetworkPlayerMovementState.Dead
                ? 1f
                : speed > 0.1f
                    ? Mathf.Sin(Time.unscaledTime * frequency * Mathf.PI * 2f)
                    : 0f;
            float armSwingDegrees = currentState.MovementState == NetworkPlayerMovementState.Sprinting ? 24f : 13f;
            if (currentState.MovementState == NetworkPlayerMovementState.Jumping)
            {
                armSwingDegrees = -22f;
            }
            else if (currentState.MovementState == NetworkPlayerMovementState.Falling)
            {
                armSwingDegrees = 28f;
            }
            else if (currentState.MovementState == NetworkPlayerMovementState.Dead)
            {
                armSwingDegrees = 70f;
            }

            AnimateFirstPersonArm(
                firstPersonLeftArm,
                firstPersonLeftBasePosition,
                firstPersonLeftBaseRotation,
                swing,
                armSwingDegrees,
                -1f);
            AnimateFirstPersonArm(
                firstPersonRightArm,
                firstPersonRightBasePosition,
                firstPersonRightBaseRotation,
                swing,
                armSwingDegrees,
                1f);
        }

        private void UpdateRemoteAvatar(float deltaSeconds)
        {
            float blendStep = Mathf.Max(0f, deltaSeconds) * 5f;
            if (currentState.MovementState == NetworkPlayerMovementState.Dead)
            {
                deathPoseBlend = Mathf.MoveTowards(deathPoseBlend, 1f, blendStep);
                modelRoot.localRotation = Quaternion.Slerp(
                    Quaternion.identity,
                    Quaternion.Euler(0f, 0f, 82f),
                    deathPoseBlend);
                modelRoot.localScale = Vector3.Lerp(
                    Vector3.one,
                    new Vector3(0.95f, 0.42f, 0.95f),
                    deathPoseBlend);
                return;
            }

            deathPoseBlend = Mathf.MoveTowards(deathPoseBlend, 0f, blendStep);
            modelRoot.localRotation = Quaternion.identity;
            float crouchScale = currentState.Crouched ? 0.68f : 1f;
            float landingSquash = 0f;
            if (currentState.MovementState == NetworkPlayerMovementState.Landing)
            {
                float duration = settings != null ? settings.LandingPresentationDurationSeconds : 0.18f;
                float normalized = duration > 0f ? Mathf.Clamp01(landingElapsedSeconds / duration) : 1f;
                landingSquash = Mathf.Sin(normalized * Mathf.PI) * 0.13f;
            }
            float bob = 0f;
            if (currentState.MovementState == NetworkPlayerMovementState.Idle)
            {
                bob = Mathf.Sin(Time.unscaledTime * 2.3f) * 0.012f;
            }
            else if (currentState.MovementState == NetworkPlayerMovementState.Walking ||
                     currentState.MovementState == NetworkPlayerMovementState.CrouchWalking)
            {
                bob = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 8.2f)) * 0.035f;
            }
            else if (currentState.MovementState == NetworkPlayerMovementState.Sprinting)
            {
                bob = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 11.5f)) * 0.055f;
            }

            modelRoot.localScale = new Vector3(1f, crouchScale * (1f - landingSquash), 1f);
            if (torso != null)
            {
                torso.localPosition = torsoBasePosition + Vector3.up * bob;
            }
            if (head != null)
            {
                head.localPosition = headBasePosition + Vector3.up * bob;
            }

            float cycleRate = currentState.MovementState == NetworkPlayerMovementState.Sprinting ? 11.5f : 8.2f;
            float cycle = Mathf.Sin(Time.unscaledTime * cycleRate);
            float limbSwing = currentState.MovementState == NetworkPlayerMovementState.Sprinting ? 42f : 28f;
            if (currentState.MovementState == NetworkPlayerMovementState.Jumping)
            {
                SetLimbPose(58f, -58f, 24f, -24f);
            }
            else if (currentState.MovementState == NetworkPlayerMovementState.Falling)
            {
                SetLimbPose(-45f, 45f, -20f, 20f);
            }
            else if (currentState.MovementState == NetworkPlayerMovementState.Landing)
            {
                SetLimbPose(18f, -18f, -28f, 28f);
            }
            else if (currentState.Crouched)
            {
                SetLimbPose(-12f, 12f, 8f, -8f);
            }
            else if (currentState.MovementState == NetworkPlayerMovementState.Walking ||
                     currentState.MovementState == NetworkPlayerMovementState.Sprinting ||
                     currentState.MovementState == NetworkPlayerMovementState.CrouchWalking)
            {
                SetLimbPose(
                    cycle * limbSwing,
                    -cycle * limbSwing,
                    -cycle * limbSwing * 0.72f,
                    cycle * limbSwing * 0.72f);
            }
            else
            {
                SetLimbPose(0f, 0f, 0f, 0f);
            }
        }

        private void SetLimbPose(float leftArmZ, float rightArmZ, float leftLegZ, float rightLegZ)
        {
            if (leftArm != null) leftArm.localRotation = leftArmBaseRotation * Quaternion.Euler(leftArmZ, 0f, 0f);
            if (rightArm != null) rightArm.localRotation = rightArmBaseRotation * Quaternion.Euler(rightArmZ, 0f, 0f);
            if (leftLeg != null) leftLeg.localRotation = leftLegBaseRotation * Quaternion.Euler(leftLegZ, 0f, 0f);
            if (rightLeg != null) rightLeg.localRotation = rightLegBaseRotation * Quaternion.Euler(rightLegZ, 0f, 0f);
        }

        private static void AnimateFirstPersonArm(
            Transform arm,
            Vector3 basePosition,
            Quaternion baseRotation,
            float cycle,
            float swingDegrees,
            float side)
        {
            if (arm == null)
            {
                return;
            }
            arm.localPosition = basePosition + new Vector3(0f, Mathf.Abs(cycle) * 0.012f, cycle * 0.008f);
            arm.localRotation = baseRotation * Quaternion.Euler(cycle * swingDegrees, 0f, side * 3f);
        }

        private static float PlanarSpeed(NetworkPlayerSnapshot state)
        {
            return Mathf.Sqrt(
                state.VelocityEastMetersPerSecond * state.VelocityEastMetersPerSecond +
                state.VelocityNorthMetersPerSecond * state.VelocityNorthMetersPerSecond);
        }

        private static float GetLandingPulse(float elapsed, PhaseOneWorldSettings worldSettings)
        {
            float duration = worldSettings != null ? worldSettings.LandingPresentationDurationSeconds : 0.18f;
            if (duration <= 0f)
            {
                return 0f;
            }
            float t = Mathf.Clamp01(elapsed / duration);
            return Mathf.Sin(t * Mathf.PI) * 0.035f;
        }
    }
}
