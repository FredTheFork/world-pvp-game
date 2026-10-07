using System;
using UnityEngine;
using UnityEngine.Profiling;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Streaming
{
    /// <summary>
    /// Low-rate runtime telemetry for the desktop-Web baseline. FPS/frame time and Unity allocator
    /// totals are sampled locally; unavailable Web GPU/RAM/native Cesium counters are reported as
    /// unavailable rather than inferred from hardware capacity or the configured cache target.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldPerformanceTelemetry : MonoBehaviour
    {
        private const int FrameSampleCapacity = 120;

        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private PhaseOneWorldSettings settings;
        [SerializeField] private MapUsageTracker mapUsageTracker;
        [SerializeField] private WorldStreamingController streamingController;
        [SerializeField] private Camera gameplayCamera;

        private readonly float[] frameTimeSamplesMilliseconds = new float[FrameSampleCapacity];
        private readonly float[] sortedFrameTimeSamplesMilliseconds = new float[FrameSampleCapacity];
        private readonly FrameTiming[] latestFrameTiming = new FrameTiming[1];
        private int frameSampleWriteIndex;
        private int frameSampleCount;
        private float frameSampleMillisecondsTotal;
        private float nextSnapshotTime;
        private double averageFps;
        private double p95FrameTimeMilliseconds;
        private double cpuFrameTimeMilliseconds = -1.0;
        private double gpuFrameTimeMilliseconds = -1.0;
        private long unityAllocatedMemoryBytes;
        private long unityReservedMemoryBytes;
        private long graphicsDriverAllocatedMemoryBytes = -1L;
        private long estimatedVisibleCesiumTriangles;
        private long veryHighBandTriangles;
        private long highBandTriangles;
        private long mediumBandTriangles;
        private long horizonBandTriangles;
        private int liveTileObjectCount;
        private long configuredCesiumCacheCapBytes;
        private float latestSampleTime;

        public double AverageFramesPerSecond { get { return averageFps; } }
        public double P95FrameTimeMilliseconds { get { return p95FrameTimeMilliseconds; } }
        public double CpuFrameTimeMilliseconds { get { return cpuFrameTimeMilliseconds; } }
        public double GpuFrameTimeMilliseconds { get { return gpuFrameTimeMilliseconds; } }
        public bool HasGpuFrameTiming { get { return gpuFrameTimeMilliseconds >= 0.0; } }
        public long UnityAllocatedMemoryBytes { get { return unityAllocatedMemoryBytes; } }
        public long UnityReservedMemoryBytes { get { return unityReservedMemoryBytes; } }
        public long GraphicsDriverAllocatedMemoryBytes { get { return graphicsDriverAllocatedMemoryBytes; } }
        public long EstimatedVisibleCesiumTriangles { get { return estimatedVisibleCesiumTriangles; } }
        public long VeryHighBandTriangles { get { return veryHighBandTriangles; } }
        public long HighBandTriangles { get { return highBandTriangles; } }
        public long MediumBandTriangles { get { return mediumBandTriangles; } }
        public long HorizonBandTriangles { get { return horizonBandTriangles; } }
        public int LiveTileObjectCount { get { return liveTileObjectCount; } }
        public long ConfiguredCesiumCacheCapBytes { get { return configuredCesiumCacheCapBytes; } }
        public float LatestSampleTime { get { return latestSampleTime; } }
        public int TargetFramesPerSecond { get { return settings != null ? settings.TargetFramesPerSecond : 60; } }
        public float P95FrameTimeBudgetMilliseconds { get { return settings != null ? settings.P95FrameTimeBudgetMilliseconds : 16.7f; } }
        public float UnityMemoryBudgetMiB { get { return settings != null ? settings.UnityReservedMemoryBudgetMiB : 1024f; } }
        public float GpuMemoryBudgetMiB { get { return settings != null ? settings.GpuMemoryBudgetMiB : 512f; } }
        public float MapBandwidthBudgetMbps { get { return settings != null ? settings.SustainedMapBandwidthBudgetMbps : 5f; } }
        public int VisibleTriangleBudget { get { return settings != null ? settings.VisibleCesiumTriangleBudget : 1500000; } }
        public float SampleIntervalSeconds { get { return settings != null ? settings.PerformanceSampleIntervalSeconds : 1f; } }
        public bool IsP95FrameBudgetExceeded
        {
            get { return frameSampleCount > 0 && p95FrameTimeMilliseconds > P95FrameTimeBudgetMilliseconds; }
        }
        public bool IsUnityMemoryBudgetExceeded
        {
            get
            {
                return unityReservedMemoryBytes > 0L &&
                       unityReservedMemoryBytes > UnityMemoryBudgetMiB * 1024.0 * 1024.0;
            }
        }
        public bool IsVisibleTriangleBudgetExceeded
        {
            get { return estimatedVisibleCesiumTriangles > 0L && estimatedVisibleCesiumTriangles > VisibleTriangleBudget; }
        }
        public bool IsMapBandwidthBudgetExceeded
        {
            get
            {
                return mapUsageTracker != null && mapUsageTracker.HasFreshBandwidthSample &&
                       mapUsageTracker.EstimatedBandwidthMbps > MapBandwidthBudgetMbps;
            }
        }

        public void Configure(
            GeospatialWorldManager manager,
            PhaseOneWorldSettings worldSettings,
            MapUsageTracker usageTracker,
            WorldStreamingController streamController,
            Camera mainCamera)
        {
            worldManager = manager;
            settings = worldSettings;
            mapUsageTracker = usageTracker;
            streamingController = streamController;
            gameplayCamera = mainCamera != null ? mainCamera : Camera.main;
            if (Application.isPlaying && settings != null)
            {
                Application.targetFrameRate = settings.TargetFramesPerSecond;
            }
        }

        private void Start()
        {
            if (settings != null)
            {
                Application.targetFrameRate = settings.TargetFramesPerSecond;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            RecordFrameDuration(Time.unscaledDeltaTime);
            FrameTimingManager.CaptureFrameTimings();
            if (Time.unscaledTime < nextSnapshotTime)
            {
                return;
            }

            nextSnapshotTime = Time.unscaledTime + SampleIntervalSeconds;
            CaptureSnapshot();
        }

        private void CaptureSnapshot()
        {
            CalculateFpsWindow();
            CaptureFrameTimingCounters();
            unityAllocatedMemoryBytes = SafeGetTotalAllocatedMemory();
            unityReservedMemoryBytes = SafeGetTotalReservedMemory();
            graphicsDriverAllocatedMemoryBytes = SafeGetGraphicsDriverAllocatedMemory();
            if (gameplayCamera == null)
            {
                gameplayCamera = Camera.main;
            }

            if (mapUsageTracker != null)
            {
                float veryHigh = settings != null ? settings.VeryHighDetailDistanceMeters : 100f;
                float high = settings != null ? settings.HighDetailDistanceMeters : 300f;
                float medium = settings != null ? settings.MediumDetailDistanceMeters : 750f;
                mapUsageTracker.EstimateVisibleCesiumTriangles(
                    gameplayCamera,
                    veryHigh,
                    high,
                    medium,
                    out estimatedVisibleCesiumTriangles,
                    out veryHighBandTriangles,
                    out highBandTriangles,
                    out mediumBandTriangles,
                    out horizonBandTriangles,
                    out liveTileObjectCount);
            }

            configuredCesiumCacheCapBytes = streamingController != null
                ? streamingController.ConfiguredTileCacheBytes
                : (worldManager != null && worldManager.Tileset != null
                    ? Math.Max(0L, worldManager.Tileset.maximumCachedBytes)
                    : 0L);
            latestSampleTime = Time.unscaledTime;
        }

        private void RecordFrameDuration(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
            {
                return;
            }

            float milliseconds = deltaSeconds * 1000f;
            if (frameSampleCount == FrameSampleCapacity)
            {
                frameSampleMillisecondsTotal -= frameTimeSamplesMilliseconds[frameSampleWriteIndex];
            }
            else
            {
                frameSampleCount++;
            }

            frameTimeSamplesMilliseconds[frameSampleWriteIndex] = milliseconds;
            frameSampleMillisecondsTotal += milliseconds;
            frameSampleWriteIndex = (frameSampleWriteIndex + 1) % FrameSampleCapacity;
        }

        private void CalculateFpsWindow()
        {
            if (frameSampleCount <= 0)
            {
                averageFps = 0.0;
                p95FrameTimeMilliseconds = 0.0;
                return;
            }

            double totalSeconds = frameSampleMillisecondsTotal / 1000.0;
            averageFps = totalSeconds > 0.0 ? frameSampleCount / totalSeconds : 0.0;
            Array.Copy(frameTimeSamplesMilliseconds, sortedFrameTimeSamplesMilliseconds, frameSampleCount);
            Array.Sort(sortedFrameTimeSamplesMilliseconds, 0, frameSampleCount);
            int p95Index = Mathf.Clamp(
                Mathf.CeilToInt(frameSampleCount * 0.95f) - 1,
                0,
                frameSampleCount - 1);
            p95FrameTimeMilliseconds = sortedFrameTimeSamplesMilliseconds[p95Index];
        }

        private void CaptureFrameTimingCounters()
        {
            cpuFrameTimeMilliseconds = -1.0;
            gpuFrameTimeMilliseconds = -1.0;
            uint timingCount = FrameTimingManager.GetLatestTimings((uint)latestFrameTiming.Length, latestFrameTiming);
            if (timingCount == 0)
            {
                return;
            }

            FrameTiming timing = latestFrameTiming[0];
            if (timing.cpuFrameTime > 0.0)
            {
                cpuFrameTimeMilliseconds = timing.cpuFrameTime;
            }

            // Unity 6 WebGL does not expose GPU frame timing; never turn that unsupported counter into 0 ms.
            if (Application.platform != RuntimePlatform.WebGLPlayer && timing.gpuFrameTime > 0.0)
            {
                gpuFrameTimeMilliseconds = timing.gpuFrameTime;
            }
        }

        private static long SafeGetTotalAllocatedMemory()
        {
            try
            {
                return Math.Max(0L, Profiler.GetTotalAllocatedMemoryLong());
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        private static long SafeGetTotalReservedMemory()
        {
            try
            {
                return Math.Max(0L, Profiler.GetTotalReservedMemoryLong());
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        private static long SafeGetGraphicsDriverAllocatedMemory()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return -1L;
#else
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            try
            {
                return Math.Max(0L, Profiler.GetAllocatedMemoryForGraphicsDriver());
            }
            catch (Exception)
            {
                return -1L;
            }
#else
            return -1L;
#endif
#endif
        }
    }
}
