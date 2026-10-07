using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CesiumForUnity;
using UnityEngine;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Streaming
{
    /// <summary>
    /// Records per-browser and host-aggregated map-usage observations. Cesium 1.26 exposes tile
    /// GameObject creation but not a public HTTP request/billing-event stream; browser Resource
    /// Timing is therefore an optional request-start proxy, never a Google billing authority.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapUsageTracker : MonoBehaviour
    {
        private const string LedgerPreferenceKey = "worldpvp.phase6.map-usage.v1";
        private const int MaximumSavedSessions = 20;
        private const float BrowserPollIntervalSeconds = 0.5f;
        private const float LedgerSaveIntervalSeconds = 30f;
        private const double BytesPerMegabit = 1000000.0 / 8.0;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int WorldPvpMapUsageTelemetryStart();
        [DllImport("__Internal")] private static extern int WorldPvpMapUsageTelemetryGetRootRequestStarts();
        [DllImport("__Internal")] private static extern int WorldPvpMapUsageTelemetryGetRendererRequestStarts();
        [DllImport("__Internal")] private static extern double WorldPvpMapUsageTelemetryGetTransferBytes();
        [DllImport("__Internal")] private static extern int WorldPvpMapUsageTelemetryGetSizedEntryCount();
        [DllImport("__Internal")] private static extern int WorldPvpMapUsageTelemetryGetResourceEntryCount();
#endif

        [Serializable]
        public struct UsageCounters
        {
            public long RootTilesetLoadAttempts;
            public long BrowserObservedRootRequestStarts;
            public long BrowserObservedRendererRequestStarts;
            public long CesiumTileGameObjectCreations;
            public long BrowserTransferBytes;
            public long BrowserTransferSizedEntries;
            public long BrowserObservedMapResourceEntries;
            public long SessionMilliseconds;
        }

        [Serializable]
        private sealed class LedgerContainer
        {
            public List<LedgerEntry> sessions = new List<LedgerEntry>();
        }

        [Serializable]
        private sealed class LedgerEntry
        {
            public string recordId;
            public string matchScopeId;
            public string startedUtc;
            public string updatedUtc;
            public bool completed;
            public long rootLoadAttempts;
            public long observedRootRequests;
            public long observedRendererRequests;
            public long cesiumTileGameObjectCreations;
            public long transferBytesWithVisibleTiming;
            public long transferSizedEntries;
            public long observedMapResourceEntries;
            public long reportingPlayers;
            public long sessionMilliseconds;
            public double estimatedGrossCostUsdAtFirstTier;
            public bool costEstimateAvailable;
        }

        private sealed class TrackedTile
        {
            public GameObject Root;
            public TrackedRenderer[] Renderers;
        }

        private sealed class TrackedRenderer
        {
            public Renderer Renderer;
            public MeshFilter MeshFilter;
            public long TriangleCount;
        }

        [SerializeField] private GeospatialWorldManager worldManager;
        [SerializeField] private PhaseOneWorldSettings settings;
        [SerializeField] private Cesium3DTileset tileset;

        private readonly List<TrackedTile> trackedTiles = new List<TrackedTile>(256);
        private readonly Dictionary<ulong, UsageCounters> latestPlayerCounters =
            new Dictionary<ulong, UsageCounters>();

        private bool browserResourceTimingSupported;
        private bool scopeActive;
        private string matchScopeId = string.Empty;
        private string recordId = string.Empty;
        private DateTimeOffset scopeStartedUtc;
        private float scopeStartedRealtime;
        private float nextBrowserPollTime;
        private float nextLedgerSaveTime;
        private long totalRootTilesetLoadAttempts;
        private long totalTileGameObjectCreations;
        private long totalBrowserRootRequestStarts;
        private long totalBrowserRendererRequestStarts;
        private long totalBrowserTransferBytes;
        private long totalBrowserTransferSizedEntries;
        private long totalBrowserMapResourceEntries;
        private long scopeRootAttemptBaseline;
        private long scopeTileObjectBaseline;
        private long scopeBrowserRootBaseline;
        private long scopeBrowserRendererBaseline;
        private long scopeBrowserBytesBaseline;
        private long scopeBrowserSizedEntriesBaseline;
        private long scopeBrowserResourceEntriesBaseline;
        private long rateSampleBytesBaseline;
        private long rateSampleSizedEntriesBaseline;
        private long rateSampleResourceEntriesBaseline;
        private float rateSampleTime;
        private float lastBandwidthSampleTime;
        private float estimatedBandwidthMbps;
        private bool hasBandwidthSample;
        private bool bandwidthSamplePartial;
        private bool hasLocalNetworkPlayerId;
        private ulong localNetworkPlayerId;
        private int expectedPlayerCount;

        public bool BrowserResourceTimingSupported { get { return browserResourceTimingSupported; } }
        public bool HasActiveMatchScope { get { return scopeActive; } }
        public string MatchScopeId { get { return matchScopeId; } }
        public int ReportingPlayerCount { get { return latestPlayerCounters.Count; } }
        public bool HasFreshBandwidthSample
        {
            get
            {
                return hasBandwidthSample &&
                       Time.unscaledTime - lastBandwidthSampleTime <= 5f;
            }
        }
        public bool BandwidthSampleIsPartial { get { return bandwidthSamplePartial; } }
        public double EstimatedBandwidthMbps { get { return estimatedBandwidthMbps; } }
        public double SessionDurationSeconds
        {
            get
            {
                return scopeActive
                    ? Math.Max(0.0, Time.realtimeSinceStartup - scopeStartedRealtime)
                    : 0.0;
            }
        }

        public long LocalRootTilesetLoadAttempts { get { return GetLocalCounters().RootTilesetLoadAttempts; } }
        public long LocalObservedRootRequestStarts { get { return GetLocalCounters().BrowserObservedRootRequestStarts; } }
        public long LocalObservedRendererRequestStarts { get { return GetLocalCounters().BrowserObservedRendererRequestStarts; } }
        public long LocalCesiumTileGameObjectCreations { get { return GetLocalCounters().CesiumTileGameObjectCreations; } }
        public long LocalTransferBytesWithVisibleTiming { get { return GetLocalCounters().BrowserTransferBytes; } }
        public long TrackedTileGameObjectCount { get { return CountLiveTrackedTiles(); } }
        public double LocalEstimatedGrossCostUsdAtFirstTier
        {
            get { return EstimateGrossCostUsd(GetLocalCounters().BrowserObservedRendererRequestStarts); }
        }
        public double MatchEstimatedGrossCostUsdAtFirstTier
        {
            get { return EstimateGrossCostUsd(GetMatchCounters().BrowserObservedRendererRequestStarts); }
        }
        public long MatchObservedRendererRequestStarts
        {
            get { return GetMatchCounters().BrowserObservedRendererRequestStarts; }
        }
        public long MatchObservedRootRequestStarts
        {
            get { return GetMatchCounters().BrowserObservedRootRequestStarts; }
        }
        public long MatchRootTilesetLoadAttempts
        {
            get { return GetMatchCounters().RootTilesetLoadAttempts; }
        }
        public long MatchCesiumTileGameObjectCreations
        {
            get { return GetMatchCounters().CesiumTileGameObjectCreations; }
        }
        public long MatchTransferBytesWithVisibleTiming
        {
            get { return GetMatchCounters().BrowserTransferBytes; }
        }
        public long MatchSessionMilliseconds
        {
            get { return GetMatchCounters().SessionMilliseconds; }
        }
        public bool HasMatchCostEstimate
        {
            get { return browserResourceTimingSupported; }
        }
        public int ConfiguredMapRequestWarningPerMatch
        {
            get { return settings != null ? settings.MapRequestWarningPerMatch : 1000; }
        }
        public float ConfiguredPriceUsdPerThousandEvents
        {
            get { return settings != null ? settings.EstimatedMapPriceUsdPerThousandEvents : 6f; }
        }
        public int ConfiguredMonthlyFreeEventThreshold
        {
            get { return settings != null ? settings.EstimatedMonthlyFreeMapEvents : 1000; }
        }
        public bool IsMatchRequestWarningExceeded
        {
            get
            {
                return browserResourceTimingSupported &&
                       MatchObservedRendererRequestStarts >= ConfiguredMapRequestWarningPerMatch;
            }
        }

        private void OnEnable()
        {
            StartBrowserResourceTiming();
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (Time.unscaledTime >= nextBrowserPollTime)
            {
                nextBrowserPollTime = Time.unscaledTime + BrowserPollIntervalSeconds;
                PollBrowserResourceTiming();
            }

            if (scopeActive && Time.unscaledTime >= nextLedgerSaveTime)
            {
                nextLedgerSaveTime = Time.unscaledTime + LedgerSaveIntervalSeconds;
                SaveCurrentScopeToLedger(false);
            }
        }

        private void OnApplicationQuit()
        {
            EndMatchScope();
        }

        private void OnDestroy()
        {
            EndMatchScope();
            BindTileset(null);
        }

        public void Configure(GeospatialWorldManager manager, PhaseOneWorldSettings worldSettings)
        {
            worldManager = manager;
            settings = worldSettings;
            if (manager != null)
            {
                BindTileset(manager.Tileset);
            }
        }

        public void BindTileset(Cesium3DTileset value)
        {
            if (tileset == value)
            {
                return;
            }

            if (tileset != null)
            {
                tileset.OnTileGameObjectCreated -= OnTileGameObjectCreated;
            }
            tileset = value;
            if (tileset != null)
            {
                tileset.OnTileGameObjectCreated += OnTileGameObjectCreated;
            }
        }

        /// <summary>Starts a provisional local scope before a host's preflight root tileset request.</summary>
        public void BeginProvisionalMatchScope()
        {
            BeginMatchScope("pending-" + Guid.NewGuid().ToString("N"), 0);
        }

        /// <summary>
        /// Starts a match scope (typically after joining, before enabling Cesium). The ID is local
        /// session metadata only; neither the join code nor location is persisted by this tracker.
        /// </summary>
        public void BeginMatchScope(string sessionId, int expectedPlayers)
        {
            string normalized = string.IsNullOrWhiteSpace(sessionId)
                ? "local-" + Guid.NewGuid().ToString("N")
                : sessionId.Trim();

            if (scopeActive)
            {
                EndMatchScope();
            }

            StartBrowserResourceTiming();
            PollBrowserResourceTiming();
            scopeActive = true;
            matchScopeId = normalized.Length > 128 ? normalized.Substring(0, 128) : normalized;
            recordId = Guid.NewGuid().ToString("N");
            scopeStartedUtc = DateTimeOffset.UtcNow;
            scopeStartedRealtime = Time.realtimeSinceStartup;
            scopeRootAttemptBaseline = totalRootTilesetLoadAttempts;
            scopeTileObjectBaseline = totalTileGameObjectCreations;
            scopeBrowserRootBaseline = totalBrowserRootRequestStarts;
            scopeBrowserRendererBaseline = totalBrowserRendererRequestStarts;
            scopeBrowserBytesBaseline = totalBrowserTransferBytes;
            scopeBrowserSizedEntriesBaseline = totalBrowserTransferSizedEntries;
            scopeBrowserResourceEntriesBaseline = totalBrowserMapResourceEntries;
            latestPlayerCounters.Clear();
            hasLocalNetworkPlayerId = false;
            nextLedgerSaveTime = Time.unscaledTime + LedgerSaveIntervalSeconds;
            expectedPlayerCount = Math.Max(0, expectedPlayers);
        }

        /// <summary>Attaches the provisional preflight usage to the UGS session without resetting it.</summary>
        public void BindProvisionalScopeToMatch(string sessionId, int expectedPlayers)
        {
            if (!scopeActive)
            {
                BeginMatchScope(sessionId, expectedPlayers);
                return;
            }

            matchScopeId = string.IsNullOrWhiteSpace(sessionId) ? matchScopeId : sessionId.Trim();
            if (expectedPlayers > 0)
            {
                expectedPlayerCount = expectedPlayers;
            }
            SaveCurrentScopeToLedger(false);
        }

        public void SetExpectedPlayerCount(int expectedPlayers)
        {
            if (expectedPlayers > 0)
            {
                expectedPlayerCount = expectedPlayers;
            }
        }

        public void RecordRootTilesetLoadAttempt()
        {
            totalRootTilesetLoadAttempts++;
        }

        /// <summary>
        /// The NetworkPlayer owner sends only telemetry counters here. These values do not feed
        /// authoritative transforms, movement, match outcomes, or Google billing.
        /// </summary>
        public void RecordPlayerUsageSnapshot(ulong clientId, UsageCounters counters, bool localPlayer)
        {
            if (!scopeActive)
            {
                return;
            }

            counters = NormalizeCounters(counters);
            latestPlayerCounters[clientId] = counters;
            if (localPlayer)
            {
                localNetworkPlayerId = clientId;
                hasLocalNetworkPlayerId = true;
            }
        }

        public UsageCounters GetLocalCounters()
        {
            long elapsedMilliseconds = scopeActive
                ? (long)(Math.Max(0.0, Time.realtimeSinceStartup - scopeStartedRealtime) * 1000.0)
                : 0L;
            return new UsageCounters
            {
                RootTilesetLoadAttempts = Math.Max(0L, totalRootTilesetLoadAttempts - scopeRootAttemptBaseline),
                BrowserObservedRootRequestStarts = Math.Max(0L, totalBrowserRootRequestStarts - scopeBrowserRootBaseline),
                BrowserObservedRendererRequestStarts = Math.Max(0L, totalBrowserRendererRequestStarts - scopeBrowserRendererBaseline),
                CesiumTileGameObjectCreations = Math.Max(0L, totalTileGameObjectCreations - scopeTileObjectBaseline),
                BrowserTransferBytes = Math.Max(0L, totalBrowserTransferBytes - scopeBrowserBytesBaseline),
                BrowserTransferSizedEntries = Math.Max(0L, totalBrowserTransferSizedEntries - scopeBrowserSizedEntriesBaseline),
                BrowserObservedMapResourceEntries = Math.Max(0L, totalBrowserMapResourceEntries - scopeBrowserResourceEntriesBaseline),
                SessionMilliseconds = elapsedMilliseconds
            };
        }

        public UsageCounters GetMatchCounters()
        {
            if (latestPlayerCounters.Count == 0)
            {
                return GetLocalCounters();
            }

            UsageCounters total = default(UsageCounters);
            foreach (KeyValuePair<ulong, UsageCounters> entry in latestPlayerCounters)
            {
                AddCounters(ref total, entry.Value);
            }

            // Until the host's own first network report arrives, retain its local counters once.
            if (!hasLocalNetworkPlayerId || !latestPlayerCounters.ContainsKey(localNetworkPlayerId))
            {
                AddCounters(ref total, GetLocalCounters());
            }
            return total;
        }

        public int ExpectedPlayerCount { get { return expectedPlayerCount; } }

        public bool TryGetPlayerUsageSnapshot(ulong clientId, out UsageCounters counters)
        {
            return latestPlayerCounters.TryGetValue(clientId, out counters);
        }

        public void EstimateVisibleCesiumTriangles(
            Camera viewCamera,
            float veryHighDistanceMeters,
            float highDistanceMeters,
            float mediumDistanceMeters,
            out long totalTriangles,
            out long veryHighTriangles,
            out long highTriangles,
            out long mediumTriangles,
            out long horizonTriangles,
            out int liveTileObjects)
        {
            totalTriangles = 0L;
            veryHighTriangles = 0L;
            highTriangles = 0L;
            mediumTriangles = 0L;
            horizonTriangles = 0L;
            liveTileObjects = 0;
            if (viewCamera == null)
            {
                return;
            }

            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(viewCamera);
            Vector3 cameraPosition = viewCamera.transform.position;
            for (int tileIndex = trackedTiles.Count - 1; tileIndex >= 0; tileIndex--)
            {
                TrackedTile tile = trackedTiles[tileIndex];
                if (tile == null || tile.Root == null)
                {
                    trackedTiles.RemoveAt(tileIndex);
                    continue;
                }
                if (!tile.Root.activeInHierarchy)
                {
                    continue;
                }

                liveTileObjects++;
                TrackedRenderer[] renderers = tile.Renderers;
                if (renderers == null)
                {
                    renderers = CaptureRenderers(tile.Root);
                    tile.Renderers = renderers;
                }

                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    TrackedRenderer tracked = renderers[rendererIndex];
                    Renderer renderer = tracked.Renderer;
                    if (renderer == null || tracked.MeshFilter == null ||
                        !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        !GeometryUtility.TestPlanesAABB(frustumPlanes, renderer.bounds))
                    {
                        continue;
                    }

                    long triangles = tracked.TriangleCount;
                    if (triangles <= 0L)
                    {
                        triangles = CountTriangles(tracked.MeshFilter.sharedMesh);
                        tracked.TriangleCount = triangles;
                    }
                    if (triangles <= 0L)
                    {
                        continue;
                    }

                    totalTriangles += triangles;
                    Vector3 closest = renderer.bounds.ClosestPoint(cameraPosition);
                    float distance = Vector3.Distance(cameraPosition, closest);
                    if (distance < veryHighDistanceMeters)
                    {
                        veryHighTriangles += triangles;
                    }
                    else if (distance < highDistanceMeters)
                    {
                        highTriangles += triangles;
                    }
                    else if (distance < mediumDistanceMeters)
                    {
                        mediumTriangles += triangles;
                    }
                    else
                    {
                        horizonTriangles += triangles;
                    }
                }
            }
        }

        private void OnTileGameObjectCreated(GameObject tileObject)
        {
            if (tileObject == null)
            {
                return;
            }

            totalTileGameObjectCreations++;
            trackedTiles.Add(new TrackedTile
            {
                Root = tileObject,
                Renderers = CaptureRenderers(tileObject)
            });
        }

        private static TrackedRenderer[] CaptureRenderers(GameObject tileObject)
        {
            Renderer[] renderers = tileObject.GetComponentsInChildren<Renderer>(true);
            List<TrackedRenderer> captured = new List<TrackedRenderer>(renderers.Length);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter == null)
                {
                    continue;
                }
                captured.Add(new TrackedRenderer
                {
                    Renderer = renderer,
                    MeshFilter = meshFilter,
                    TriangleCount = CountTriangles(meshFilter.sharedMesh)
                });
            }
            return captured.ToArray();
        }

        private static long CountTriangles(Mesh mesh)
        {
            if (mesh == null)
            {
                return 0L;
            }

            long total = 0L;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                total += (long)mesh.GetIndexCount(submesh) / 3L;
            }
            return total;
        }

        private int CountLiveTrackedTiles()
        {
            int count = 0;
            for (int i = trackedTiles.Count - 1; i >= 0; i--)
            {
                TrackedTile tile = trackedTiles[i];
                if (tile == null || tile.Root == null)
                {
                    trackedTiles.RemoveAt(i);
                }
                else if (tile.Root.activeInHierarchy)
                {
                    count++;
                }
            }
            return count;
        }

        private void StartBrowserResourceTiming()
        {
            if (!Application.isPlaying || browserResourceTimingSupported)
            {
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                browserResourceTimingSupported = WorldPvpMapUsageTelemetryStart() != 0;
                PollBrowserResourceTiming();
            }
            catch (Exception exception)
            {
                browserResourceTimingSupported = false;
                Debug.LogWarning(
                    "[Map usage] Browser Resource Timing could not start (" + exception.GetType().Name + "). " +
                    "Request counts and bandwidth remain unavailable; no request URL or API key is logged.",
                    this);
            }
#else
            browserResourceTimingSupported = false;
#endif
        }

        private void PollBrowserResourceTiming()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!browserResourceTimingSupported)
            {
                return;
            }

            try
            {
                totalBrowserRootRequestStarts = Math.Max(0, WorldPvpMapUsageTelemetryGetRootRequestStarts());
                totalBrowserRendererRequestStarts = Math.Max(0, WorldPvpMapUsageTelemetryGetRendererRequestStarts());
                totalBrowserTransferBytes = Math.Max(0L, (long)WorldPvpMapUsageTelemetryGetTransferBytes());
                totalBrowserTransferSizedEntries = Math.Max(0, WorldPvpMapUsageTelemetryGetSizedEntryCount());
                totalBrowserMapResourceEntries = Math.Max(0, WorldPvpMapUsageTelemetryGetResourceEntryCount());
                UpdateBandwidthEstimate();
            }
            catch (Exception)
            {
                browserResourceTimingSupported = false;
                hasBandwidthSample = false;
            }
#endif
        }

        private void UpdateBandwidthEstimate()
        {
            float now = Time.unscaledTime;
            if (rateSampleTime <= 0f)
            {
                rateSampleTime = now;
                rateSampleBytesBaseline = totalBrowserTransferBytes;
                rateSampleSizedEntriesBaseline = totalBrowserTransferSizedEntries;
                rateSampleResourceEntriesBaseline = totalBrowserMapResourceEntries;
                return;
            }

            float elapsed = now - rateSampleTime;
            if (elapsed < 1f)
            {
                return;
            }

            long byteDelta = Math.Max(0L, totalBrowserTransferBytes - rateSampleBytesBaseline);
            long sizedDelta = Math.Max(0L, totalBrowserTransferSizedEntries - rateSampleSizedEntriesBaseline);
            long resourceDelta = Math.Max(0L, totalBrowserMapResourceEntries - rateSampleResourceEntriesBaseline);
            if (sizedDelta > 0L)
            {
                estimatedBandwidthMbps = (byteDelta / elapsed) / BytesPerMegabit;
                bandwidthSamplePartial = sizedDelta < resourceDelta;
                hasBandwidthSample = true;
                lastBandwidthSampleTime = now;
            }
            else if (resourceDelta > 0L)
            {
                // Entries exist, but the browser did not expose transfer sizes (commonly due to
                // cross-origin timing restrictions). Do not present a fabricated zero-Mbps result.
                estimatedBandwidthMbps = 0.0;
                bandwidthSamplePartial = true;
                hasBandwidthSample = false;
            }

            // Advance the window even during idle periods so the next burst is not averaged over
            // minutes of inactivity. Bandwidth freshness uses a separate timestamp above.
            rateSampleTime = now;
            rateSampleBytesBaseline = totalBrowserTransferBytes;
            rateSampleSizedEntriesBaseline = totalBrowserTransferSizedEntries;
            rateSampleResourceEntriesBaseline = totalBrowserMapResourceEntries;
        }

        public void EndMatchScope()
        {
            if (!scopeActive)
            {
                return;
            }

            PollBrowserResourceTiming();
            if (hasLocalNetworkPlayerId)
            {
                RecordPlayerUsageSnapshot(localNetworkPlayerId, GetLocalCounters(), true);
            }
            SaveCurrentScopeToLedger(true);
            scopeActive = false;
            matchScopeId = string.Empty;
            recordId = string.Empty;
            latestPlayerCounters.Clear();
            hasLocalNetworkPlayerId = false;
        }

        private void SaveCurrentScopeToLedger(bool completed)
        {
            if (!scopeActive)
            {
                return;
            }

            UsageCounters local = GetLocalCounters();
            UsageCounters match = GetMatchCounters();
            if (local.RootTilesetLoadAttempts <= 0L &&
                local.BrowserObservedRootRequestStarts <= 0L &&
                local.BrowserObservedRendererRequestStarts <= 0L &&
                local.CesiumTileGameObjectCreations <= 0L &&
                match.RootTilesetLoadAttempts <= 0L &&
                match.BrowserObservedRootRequestStarts <= 0L &&
                match.BrowserObservedRendererRequestStarts <= 0L &&
                match.CesiumTileGameObjectCreations <= 0L)
            {
                return;
            }

            LedgerContainer ledger = LoadLedger();
            LedgerEntry entry = null;
            for (int i = 0; i < ledger.sessions.Count; i++)
            {
                if (string.Equals(ledger.sessions[i].recordId, recordId, StringComparison.Ordinal))
                {
                    entry = ledger.sessions[i];
                    break;
                }
            }
            if (entry == null)
            {
                entry = new LedgerEntry
                {
                    recordId = recordId,
                    startedUtc = scopeStartedUtc.ToUniversalTime().ToString("O")
                };
                ledger.sessions.Add(entry);
            }

            entry.matchScopeId = matchScopeId;
            entry.updatedUtc = DateTimeOffset.UtcNow.ToUniversalTime().ToString("O");
            entry.completed = completed;
            entry.rootLoadAttempts = match.RootTilesetLoadAttempts;
            entry.observedRootRequests = match.BrowserObservedRootRequestStarts;
            entry.observedRendererRequests = match.BrowserObservedRendererRequestStarts;
            entry.cesiumTileGameObjectCreations = match.CesiumTileGameObjectCreations;
            entry.transferBytesWithVisibleTiming = match.BrowserTransferBytes;
            entry.transferSizedEntries = match.BrowserTransferSizedEntries;
            entry.observedMapResourceEntries = match.BrowserObservedMapResourceEntries;
            entry.reportingPlayers = latestPlayerCounters.Count;
            entry.sessionMilliseconds = (long)(Math.Max(0.0, Time.realtimeSinceStartup - scopeStartedRealtime) * 1000.0);
            entry.costEstimateAvailable = browserResourceTimingSupported;
            entry.estimatedGrossCostUsdAtFirstTier = browserResourceTimingSupported
                ? EstimateGrossCostUsd(match.BrowserObservedRendererRequestStarts)
                : 0.0;

            while (ledger.sessions.Count > MaximumSavedSessions)
            {
                ledger.sessions.RemoveAt(0);
            }

            PlayerPrefs.SetString(LedgerPreferenceKey, JsonUtility.ToJson(ledger));
            PlayerPrefs.Save();
        }

        private static LedgerContainer LoadLedger()
        {
            string json = PlayerPrefs.GetString(LedgerPreferenceKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    LedgerContainer parsed = JsonUtility.FromJson<LedgerContainer>(json);
                    if (parsed != null)
                    {
                        if (parsed.sessions == null)
                        {
                            parsed.sessions = new List<LedgerEntry>();
                        }
                        return parsed;
                    }
                }
                catch (Exception)
                {
                    // A damaged local preference is treated as an empty, local-only ledger.
                }
            }
            return new LedgerContainer();
        }

        private double EstimateGrossCostUsd(long observedRendererRequests)
        {
            if (!browserResourceTimingSupported || observedRendererRequests <= 0L)
            {
                return 0.0;
            }

            float pricePerThousand = settings != null
                ? settings.EstimatedMapPriceUsdPerThousandEvents
                : 6f;
            return Math.Max(0.0, observedRendererRequests) * Math.Max(0.0, pricePerThousand) / 1000.0;
        }

        private void OnValidate()
        {
            if (settings == null && worldManager != null)
            {
                settings = worldManager.WorldSettings;
            }
        }

        private static UsageCounters NormalizeCounters(UsageCounters counters)
        {
            counters.RootTilesetLoadAttempts = Math.Max(0L, counters.RootTilesetLoadAttempts);
            counters.BrowserObservedRootRequestStarts = Math.Max(0L, counters.BrowserObservedRootRequestStarts);
            counters.BrowserObservedRendererRequestStarts = Math.Max(0L, counters.BrowserObservedRendererRequestStarts);
            counters.CesiumTileGameObjectCreations = Math.Max(0L, counters.CesiumTileGameObjectCreations);
            counters.BrowserTransferBytes = Math.Max(0L, counters.BrowserTransferBytes);
            counters.BrowserTransferSizedEntries = Math.Max(0L, counters.BrowserTransferSizedEntries);
            counters.BrowserObservedMapResourceEntries = Math.Max(0L, counters.BrowserObservedMapResourceEntries);
            counters.SessionMilliseconds = Math.Max(0L, counters.SessionMilliseconds);
            return counters;
        }

        private static void AddCounters(ref UsageCounters total, UsageCounters value)
        {
            total.RootTilesetLoadAttempts += value.RootTilesetLoadAttempts;
            total.BrowserObservedRootRequestStarts += value.BrowserObservedRootRequestStarts;
            total.BrowserObservedRendererRequestStarts += value.BrowserObservedRendererRequestStarts;
            total.CesiumTileGameObjectCreations += value.CesiumTileGameObjectCreations;
            total.BrowserTransferBytes += value.BrowserTransferBytes;
            total.BrowserTransferSizedEntries += value.BrowserTransferSizedEntries;
            total.BrowserObservedMapResourceEntries += value.BrowserObservedMapResourceEntries;
            total.SessionMilliseconds = Math.Max(total.SessionMilliseconds, value.SessionMilliseconds);
        }

    }
}
