using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using WorldPvp.Phase1.Battles;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Player
{
    /// <summary>
    /// Anchored first-person view adapter and optional offline capsule motor. Network sessions
    /// disable its local movement loop; NetworkPlayer applies predicted/authoritative ENU poses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhaseOneTestCharacterController : MonoBehaviour
    {
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private CesiumGlobeAnchor globeAnchor;
        [SerializeField] private PhaseOneWorldSettings settings;
        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private Renderer firstPersonBodyRenderer;
        [SerializeField] private ProceduralPlayerPresentation presentation;

        private bool inputEnabled;
        private bool networkControlled;
        private bool crouched;
        private bool spectatorCameraDetached;
        private float yawDegrees;
        private float pitchDegrees;
        private float verticalVelocity;
        private Vector3 planarVelocity;

        public bool InputEnabled { get { return inputEnabled; } }
        public bool IsNetworkControlled { get { return networkControlled; } }
        public float CurrentPlanarSpeedMetersPerSecond { get { return planarVelocity.magnitude; } }
        public bool IsSpectatorCameraDetached { get { return spectatorCameraDetached; } }
        public Transform CameraPivot { get { return cameraPivot; } }
        public Camera ViewCamera
        {
            get
            {
                if (cameraPivot != null)
                {
                    Camera childCamera = cameraPivot.GetComponentInChildren<Camera>(true);
                    if (childCamera != null)
                    {
                        return childCamera;
                    }
                }
                return Camera.main;
            }
        }

        public void Configure(
            CharacterController controller,
            Transform pivot,
            CesiumGlobeAnchor anchor,
            PhaseOneWorldSettings worldSettings,
            GeospatialWorldManager manager)
        {
            characterController = controller;
            cameraPivot = pivot;
            globeAnchor = anchor;
            settings = worldSettings;
            worldManager = manager;
        }

        public void ConfigurePresentation(
            Renderer localBodyRenderer,
            ProceduralPlayerPresentation playerPresentation)
        {
            firstPersonBodyRenderer = localBodyRenderer;
            presentation = playerPresentation;
            // The generated player is viewed from its own eyes. Remote clients build separate
            // procedural avatars from authoritative snapshots, so this local mesh stays hidden.
            if (firstPersonBodyRenderer != null)
            {
                firstPersonBodyRenderer.enabled = false;
            }
        }

        /// <summary>
        /// Network sessions disable this local Update/CharacterController motor. A NetworkPlayer
        /// supplies fixed-step predicted or server-authoritative positions in the session ENU frame.
        /// </summary>
        public void SetNetworkControlled(bool controlled)
        {
            if (!controlled)
            {
                SetSpectatorCameraDetached(false);
            }
            networkControlled = controlled;
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;

            if (characterController != null)
            {
                bool shouldEnable = !controlled && worldManager != null && worldManager.IsReady;
                characterController.enabled = shouldEnable;
            }
        }

        public void ApplyNetworkPose(LocalPosition localPosition, float yaw, float pitch)
        {
            ApplyNetworkPose(localPosition, yaw, pitch, false);
        }

        public void ApplyNetworkPose(LocalPosition localPosition, float yaw, float pitch, bool isCrouched)
        {
            if (worldManager == null || globeAnchor == null || !localPosition.IsFinite)
            {
                return;
            }

            GeoPosition geographicPosition = worldManager.LocalToGeographic(localPosition);
            globeAnchor.longitudeLatitudeHeight = new double3(
                geographicPosition.LongitudeDegrees,
                geographicPosition.LatitudeDegrees,
                geographicPosition.AltitudeMeters);
            globeAnchor.Sync();
            crouched = isCrouched;
            SetNetworkViewAngles(yaw, pitch);
        }

        public void SetNetworkAnimationState(NetworkPlayerSnapshot state)
        {
            if (presentation != null)
            {
                presentation.SetState(state);
            }
        }

        /// <summary>Applies immediate look feedback before the next fixed input packet is sent.</summary>
        public void SetNetworkViewAngles(float yaw, float pitch)
        {
            yawDegrees = Mathf.Repeat(yaw, 360f);
            pitchDegrees = Mathf.Clamp(
                pitch,
                NetworkPlayerSimulation.MinimumPitchDegrees,
                NetworkPlayerSimulation.MaximumPitchDegrees);
            transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            if (cameraPivot != null && !spectatorCameraDetached)
            {
                cameraPivot.localRotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
            }
        }

        /// <summary>Detaches only the view rig for server-dead spectating; gameplay root/state stay server-owned.</summary>
        public void SetSpectatorCameraDetached(bool detached)
        {
            if (cameraPivot == null || spectatorCameraDetached == detached)
            {
                return;
            }

            if (detached)
            {
                cameraPivot.SetParent(null, true);
                spectatorCameraDetached = true;
                return;
            }

            cameraPivot.SetParent(transform, false);
            cameraPivot.localPosition = Vector3.up *
                (settings != null ? settings.FirstPersonEyeHeightMeters : 1.62f);
            cameraPivot.localRotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
            spectatorCameraDetached = false;
        }

        public void SetSpectatorCameraPose(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (!spectatorCameraDetached || cameraPivot == null)
            {
                return;
            }
            cameraPivot.SetPositionAndRotation(worldPosition, worldRotation);
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;

            if (enabled)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                planarVelocity = Vector3.zero;
            }
        }

        public void PrepareForGroundProbe()
        {
            verticalVelocity = 0f;
            yawDegrees = 0f;
            // At the temporary elevated spawn, a steep downward view makes the target
            // tile visible so Cesium can create the collision mesh required by the raycast.
            pitchDegrees = 68f;
            transform.localRotation = Quaternion.identity;

            if (cameraPivot != null)
            {
                cameraPivot.localPosition = Vector3.up *
                    (settings != null ? settings.FirstPersonEyeHeightMeters : 1.62f);
                cameraPivot.localRotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
            }

            if (globeAnchor != null)
            {
                globeAnchor.Sync();
            }
        }

        public void ResetForGroundSpawn()
        {
            verticalVelocity = 0f;
            planarVelocity = Vector3.zero;
            crouched = false;
            yawDegrees = 0f;
            pitchDegrees = settings != null ? settings.InitialCameraPitchDegrees : 0f;
            transform.localRotation = Quaternion.identity;

            if (cameraPivot != null)
            {
                cameraPivot.localPosition = Vector3.up *
                    (settings != null ? settings.FirstPersonEyeHeightMeters : 1.62f);
                cameraPivot.localRotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
            }

            if (globeAnchor != null)
            {
                globeAnchor.Sync();
            }
        }

        private void Update()
        {
            if (networkControlled || !inputEnabled || characterController == null || !characterController.enabled)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || settings == null)
            {
                return;
            }

            UpdateMouseLook();
            Vector3 up = transform.up;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector2 input = ReadMovement(keyboard);
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            bool sprinting = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            bool requestCrouch = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed ||
                                 keyboard.cKey.isPressed;
            UpdateLocalCapsuleCrouch(requestCrouch);
            float speed = crouched
                ? settings.CrouchSpeedMetersPerSecond
                : sprinting
                    ? settings.SprintSpeedMetersPerSecond
                    : settings.WalkSpeedMetersPerSecond;
            Vector3 targetPlanarVelocity = (right * input.x + forward * input.y) * speed;
            float acceleration = input.sqrMagnitude > 0.0001f
                ? settings.GroundAccelerationMetersPerSecondSquared
                : settings.GroundDecelerationMetersPerSecondSquared;
            planarVelocity = Vector3.MoveTowards(
                planarVelocity,
                targetPlanarVelocity,
                acceleration * Time.deltaTime);

            if (characterController.isGrounded)
            {
                if (verticalVelocity < 0f)
                {
                    verticalVelocity = -2f;
                }

                if (keyboard.spaceKey.wasPressedThisFrame && !crouched)
                {
                    verticalVelocity = Mathf.Sqrt(
                        2f * settings.JumpHeightMeters * settings.GravityMetersPerSecondSquared);
                }
            }
            else
            {
                verticalVelocity -= settings.GravityMetersPerSecondSquared * Time.deltaTime;
            }

            Vector3 velocity = planarVelocity + (up * verticalVelocity);
            characterController.Move(velocity * Time.deltaTime);

            if (globeAnchor != null)
            {
                globeAnchor.Sync();
            }
            if (worldManager != null)
            {
                worldManager.EnforceLocalArenaBoundary();
            }
        }

        private void UpdateLocalCapsuleCrouch(bool requestCrouch)
        {
            if (characterController == null || settings == null)
            {
                crouched = requestCrouch;
                return;
            }

            if (!requestCrouch && crouched && worldManager != null)
            {
                GeoPosition position = worldManager.UnityWorldToGeographic(transform.position);
                if (!worldManager.CanFitPlayerCapsule(
                        position,
                        settings.CharacterRadiusMeters,
                        settings.CrouchHeightMeters,
                        settings.CharacterHeightMeters))
                {
                    requestCrouch = true;
                }
            }

            crouched = requestCrouch;
            float height = crouched ? settings.CrouchHeightMeters : settings.CharacterHeightMeters;
            characterController.height = height;
            characterController.center = Vector3.up * (height * 0.5f);
        }

        private void UpdateMouseLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            Vector2 delta = mouse.delta.ReadValue();
            float sensitivity = settings.MouseSensitivityDegreesPerPixel;
            yawDegrees += delta.x * sensitivity;
            pitchDegrees = Mathf.Clamp(
                pitchDegrees - delta.y * sensitivity,
                NetworkPlayerSimulation.MinimumPitchDegrees,
                NetworkPlayerSimulation.MaximumPitchDegrees);

            transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            if (cameraPivot != null)
            {
                cameraPivot.localRotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
            }
        }

        private static Vector2 ReadMovement(Keyboard keyboard)
        {
            float horizontal = 0f;
            float vertical = 0f;

            if (keyboard.aKey.isPressed) horizontal -= 1f;
            if (keyboard.dKey.isPressed) horizontal += 1f;
            if (keyboard.sKey.isPressed) vertical -= 1f;
            if (keyboard.wKey.isPressed) vertical += 1f;

            return new Vector2(horizontal, vertical);
        }
    }
}
