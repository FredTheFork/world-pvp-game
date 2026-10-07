using System;
using CesiumForUnity;
using UnityEngine;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Streaming
{
    /// <summary>
    /// Applies the supported Cesium 1.26 hierarchical streaming/cache controls and, while the
    /// local player is moving, contributes one bounded disabled virtual camera to Cesium's camera
    /// manager. The virtual camera is a prefetch hint, not a guaranteed request or billing cap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldStreamingController : MonoBehaviour
    {
        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private PhaseOneWorldSettings settings;
        [SerializeField] private Camera gameplayCamera;

        private CesiumCameraManager cesiumCameraManager;
        private GameObject predictionCameraObject;
        private Camera predictionCamera;
        private LocalPosition previousPlayerPosition;
        private float previousPositionSampleTime;
        private float nextPredictionUpdateTime;
        private bool hasPreviousPosition;
        private bool predictionCameraRegistered;
        private float currentPredictionAheadMeters;

        public bool IsPredictionCameraActive { get { return predictionCameraRegistered; } }
        public float CurrentPredictionAheadMeters { get { return currentPredictionAheadMeters; } }
        public long ConfiguredTileCacheBytes
        {
            get { return settings != null ? settings.MaximumTileCacheBytes : 256L * 1024L * 1024L; }
        }

        public void Configure(
            GeospatialWorldManager manager,
            PhaseOneWorldSettings worldSettings,
            Camera mainCamera)
        {
            worldManager = manager;
            settings = worldSettings;
            gameplayCamera = mainCamera != null ? mainCamera : Camera.main;
            ApplySettingsToTileset();
        }

        /// <summary>
        /// Uses only public Cesium for Unity 1.26 settings. maximumCachedBytes is a cache target:
        /// Cesium keeps tiles required for rendering even if the resident total exceeds the target.
        /// </summary>
        public void ApplySettingsToTileset()
        {
            Cesium3DTileset tileset = worldManager != null ? worldManager.Tileset : null;
            if (tileset == null || settings == null)
            {
                return;
            }

            tileset.maximumScreenSpaceError = settings.MaximumScreenSpaceError;
            tileset.maximumCachedBytes = settings.MaximumTileCacheBytes;
            tileset.maximumSimultaneousTileLoads = settings.MaximumSimultaneousTileLoads;
            tileset.loadingDescendantLimit = settings.LoadingDescendantLimit;
            tileset.preloadAncestors = settings.PreloadAncestors;
            tileset.preloadSiblings = settings.PreloadSiblings;
            tileset.enableFrustumCulling = true;
        }

        private void Update()
        {
            if (!Application.isPlaying || worldManager == null || settings == null ||
                !settings.MovementPredictionEnabled || !worldManager.IsReady ||
                !worldManager.IsArenaConfigured)
            {
                DetachPredictionCamera();
                hasPreviousPosition = false;
                return;
            }

            if (Time.unscaledTime < nextPredictionUpdateTime)
            {
                return;
            }
            nextPredictionUpdateTime = Time.unscaledTime + settings.PredictionUpdateIntervalSeconds;

            if (gameplayCamera == null)
            {
                gameplayCamera = Camera.main;
            }

            LocalPosition currentPosition = worldManager.CurrentPlayerLocalPosition;
            if (!currentPosition.IsFinite)
            {
                DetachPredictionCamera();
                hasPreviousPosition = false;
                return;
            }

            float now = Time.unscaledTime;
            if (!hasPreviousPosition)
            {
                previousPlayerPosition = currentPosition;
                previousPositionSampleTime = now;
                hasPreviousPosition = true;
                return;
            }

            float elapsed = Mathf.Max(0.001f, now - previousPositionSampleTime);
            float eastDelta = (float)(currentPosition.EastMeters - previousPlayerPosition.EastMeters);
            float northDelta = (float)(currentPosition.NorthMeters - previousPlayerPosition.NorthMeters);
            previousPlayerPosition = currentPosition;
            previousPositionSampleTime = now;

            Vector2 displacement = new Vector2(eastDelta, northDelta);
            float distance = displacement.magnitude;
            float speed = distance / elapsed;
            if (speed < settings.PredictionMinimumSpeedMetersPerSecond ||
                distance < 0.25f || gameplayCamera == null)
            {
                DetachPredictionCamera();
                return;
            }

            try
            {
                EnsurePredictionCamera();
                if (predictionCamera == null)
                {
                    DetachPredictionCamera();
                    return;
                }

                Vector2 directionEnu = displacement / distance;
                float aheadMeters = Mathf.Min(
                    settings.PredictionMaximumAheadMeters,
                    speed * settings.PredictionLeadSeconds);
                LocalPosition predictedPosition = new LocalPosition(
                    currentPosition.EastMeters + (directionEnu.x * aheadMeters),
                    currentPosition.UpMeters,
                    currentPosition.NorthMeters + (directionEnu.y * aheadMeters));

                GeoPosition currentGeographic = worldManager.LocalToGeographic(currentPosition);
                GeoPosition predictedGeographic = worldManager.LocalToGeographic(predictedPosition);
                Vector3 currentWorld = worldManager.GeographicToUnityWorld(currentGeographic);
                Vector3 predictedWorld = worldManager.GeographicToUnityWorld(predictedGeographic);
                Vector3 up = worldManager.CharacterRoot != null
                    ? worldManager.CharacterRoot.up
                    : gameplayCamera.transform.up;
                Vector3 worldDirection = Vector3.ProjectOnPlane(predictedWorld - currentWorld, up).normalized;
                if (worldDirection.sqrMagnitude < 0.5f)
                {
                    DetachPredictionCamera();
                    return;
                }

                Vector3 cameraOffset = gameplayCamera.transform.position - currentWorld;
                Quaternion predictionRotation = Quaternion.LookRotation(worldDirection, up);
                Vector3 currentCameraForward = Vector3.ProjectOnPlane(gameplayCamera.transform.forward, up);
                if (currentCameraForward.sqrMagnitude > 0.01f)
                {
                    float yawDelta = Vector3.SignedAngle(
                        currentCameraForward.normalized,
                        worldDirection,
                        up);
                    predictionRotation = Quaternion.AngleAxis(yawDelta, up) * gameplayCamera.transform.rotation;
                }
                predictionCamera.transform.SetPositionAndRotation(
                    predictedWorld + cameraOffset,
                    predictionRotation);
                predictionCamera.fieldOfView = settings.PredictionCameraFieldOfViewDegrees;
                predictionCamera.aspect = Mathf.Max(0.5f, gameplayCamera.aspect);
                predictionCamera.nearClipPlane = 0.5f;
                predictionCamera.farClipPlane = settings.PredictionCameraFarMeters;
                currentPredictionAheadMeters = aheadMeters;
                AttachPredictionCamera();
            }
            catch (Exception exception)
            {
                DetachPredictionCamera();
                Debug.LogWarning(
                    "[World streaming] Forward preloading paused after a geospatial camera update failed (" +
                    exception.GetType().Name + "). The main Cesium view remains active.",
                    this);
            }
        }

        private void EnsurePredictionCamera()
        {
            if (predictionCamera != null || !Application.isPlaying)
            {
                return;
            }

            GameObject cameraObject = new GameObject("CesiumMovementPrefetchCamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            predictionCamera = cameraObject.AddComponent<Camera>();
            predictionCamera.enabled = false;
            predictionCamera.clearFlags = CameraClearFlags.Nothing;
            predictionCameraObject = cameraObject;

            Cesium3DTileset tileset = worldManager != null ? worldManager.Tileset : null;
            if (tileset != null)
            {
                cesiumCameraManager = CesiumCameraManager.GetOrCreate(tileset.gameObject);
            }
        }

        private void AttachPredictionCamera()
        {
            if (predictionCamera == null)
            {
                return;
            }

            if (cesiumCameraManager == null)
            {
                Cesium3DTileset tileset = worldManager != null ? worldManager.Tileset : null;
                if (tileset != null)
                {
                    cesiumCameraManager = CesiumCameraManager.GetOrCreate(tileset.gameObject);
                }
            }
            if (cesiumCameraManager == null || predictionCameraRegistered)
            {
                return;
            }

            if (!cesiumCameraManager.additionalCameras.Contains(predictionCamera))
            {
                cesiumCameraManager.additionalCameras.Add(predictionCamera);
            }
            predictionCameraRegistered = true;
        }

        private void DetachPredictionCamera()
        {
            if (predictionCameraRegistered && cesiumCameraManager != null && predictionCamera != null)
            {
                cesiumCameraManager.additionalCameras.Remove(predictionCamera);
            }
            predictionCameraRegistered = false;
            currentPredictionAheadMeters = 0f;
        }

        private void OnDisable()
        {
            DetachPredictionCamera();
        }

        private void OnDestroy()
        {
            DetachPredictionCamera();
            if (predictionCameraObject != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(predictionCameraObject);
                }
                else
                {
                    DestroyImmediate(predictionCameraObject);
                }
            }
        }
    }
}
