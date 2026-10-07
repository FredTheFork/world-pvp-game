# Phase 6 — World Streaming + Performance Engineering

**Source implementation status: implemented; Unity compilation and runtime acceptance are NOT RUN.** This phase adds bounded streaming controls, a movement-aware prefetch view, local performance diagnostics, and map-usage estimates while retaining the existing Cesium / WGS84 / ENU and NGO architecture. Nothing here is evidence that a WebGL build, live Google tiles, terrain collision, Relay session, or browser gameplay has passed.

## World streaming and Cesium 1.26.0

The dependency remains pinned at `com.cesium.unity` **1.26.0**. That release's public `Cesium3DTileset` API provides the controls used here:

- `maximumScreenSpaceError` (starting value 16 px) controls hierarchical refinement by projected screen-space error, rather than specifying a fixed metre ring.
- `maximumCachedBytes` is set to an initial **256 MiB cache target**. Cesium unloads unneeded tiles toward the target, but explicitly keeps tiles required for rendering even when the resident total is above it. This is not a hard process-RAM, GPU-memory, or total-tile ceiling.
- `maximumSimultaneousTileLoads` starts at **4**, and `loadingDescendantLimit` at **16**, bounding the concurrent load queue and refinement fan-out. These constrain concurrency/work, not lifetime request totals or Google billing.
- Frustum culling stays enabled. `preloadAncestors` is enabled; `preloadSiblings` stays disabled by default to avoid a broad, unmeasured prefetch footprint.

`WorldStreamingController` configures these supported properties before enabling the root tileset. Cesium continues to stream its externally authored tile hierarchy for the current camera view and unload/cache behavior. The arena's `PreloadBufferMeters` and camera far plane remain a presentation/render envelope, **not** a radial cache cutoff.

### Movement-aware preloading

When the local anchored player's measured ENU displacement exceeds the configured speed threshold, the controller places one **disabled virtual camera** ahead of the player and registers it with Cesium's `CesiumCameraManager.additionalCameras`. Cesium 1.26 documents that additional cameras are considered for culling/LOD even when disabled. The camera is removed when the player stops or the world is not ready.

Initial tunables are a 2-second lead, at most 24 m of positional lead, a 150 m virtual-camera far plane, a 55-degree FOV, a 0.5-second update period, and a 1 m/s minimum detected speed. This is a short frustum hint; it does not request a hand-built tile list, guarantee a tile is loaded before arrival, or override Cesium's request queue. Cache and concurrency settings bound the work, while actual traffic must be benchmarked.

### Distance bands

The configurable starting benchmark bands are **0–100 m very high**, **100–300 m high**, **300–750 m medium**, and **750 m+ low/horizon**. The telemetry uses these boundaries to bin estimated visible Cesium triangles by the nearest renderer bounds. They are not guaranteed Cesium LOD rings or exact selection distances: Google owns the tile hierarchy, and Cesium's selection is based on projected screen-space error, view/frustum, camera FOV/resolution, and available content. The initial global screen-space-error threshold is 16 px; tune it only from recorded browser measurements, not by unbounded quality escalation.

## Explicit desktop-Web performance targets

Defaults in `PhaseOneWorldSettings` are **first-pass benchmark targets**, not pass results or universal guarantees:

| Metric | Initial target | What the runtime actually reports / caveat |
|---|---:|---|
| Render rate | 60 FPS; P95 frame time ≤16.7 ms | Rolling 120-frame average FPS and P95 frame time; render FPS remains independent of the 30 Hz NGO simulation tick. |
| GPU frame time | Keep within a 16.7 ms frame budget; seek margin | `FrameTimingManager` sample where supported. Unity WebGL does not expose GPU frame timing, so the Web HUD shows “unavailable,” not zero. |
| Unity memory | Reserved Unity allocator ≤1,024 MiB | `Profiler.GetTotalAllocatedMemoryLong` / `GetTotalReservedMemoryLong`; these are not total browser-process RAM and may not include all Cesium-native/browser allocations. |
| GPU memory | 512 MiB target | `Profiler.GetAllocatedMemoryForGraphicsDriver` where supported in editor/development native players. Actual Web browser GPU memory is unavailable to this Unity runtime and must be measured with browser/OS tools. |
| Cesium tile memory | 256 MiB cache target | Configured `maximumCachedBytes` only. The required visible set may exceed it; the pinned API has no public current resident tile-byte counter. Live tile GameObject count is only a proxy. |
| Map bandwidth | 5 Mbit/s sustained starting target | Optional browser Resource Timing transfer-size estimate. Cross-origin timing can hide byte counts; if hidden/partial, the HUD says unavailable/partial instead of inventing a rate. |
| Visible Cesium geometry | 1.5 million estimated triangles | Estimated from active tile renderers whose bounds intersect the gameplay-camera frustum; this is not the renderer's exact GPU-visible primitive count. |
| Streaming queue | 4 simultaneous tile loads; 16 loading descendants | Cesium concurrency/refinement limits, not a total request/billing cap. |
| Per-match map warning | 1,000 observed renderer request starts | Local warning only. This cannot stop Cesium's native HTTP requests and is not a quota or a spend limit. |

The in-game diagnostics panel is shown during a map/match scope and toggled with **F2**. It displays performance/memory estimates, triangle-distance bins, cache target, local per-player counters, duration, observed transfer rates when measurable, and a gross first-tier price estimate. The host panel aggregates the latest client reports; a client panel labels its own local estimate and does not pretend to show remote totals. Browser/native limitations and estimate caveats are kept visible.

## `MapUsageTracker` scope and limits

`MapUsageTracker` records and retains up to 20 local usage summaries in `PlayerPrefs` (browser builds use Unity's browser-side preference storage). It records:

- root-tileset **enable/load attempts** in Unity;
- root and renderer resource-request observations when browser Resource Timing is available;
- Cesium tile GameObject creation counts as a separate successful-object proxy;
- local map-session duration, browser transfer-size samples where exposed, and a gross cost estimate;
- requests per local player and a Relay/NGO-host aggregate of each connected player's **self-reported** counters per match.

It deliberately does not persist API keys, full request URLs, join codes, player coordinates, or browser resource names. The JavaScript observer filters to Google's Photorealistic 3D Tiles path and returns only counters/byte totals. Native Unity/editor runs do not have the browser observer.

**None of those counters is a Google billing feed.** Cesium 1.26 exposes `OnTileGameObjectCreated`, but not a public per-HTTP-request, response-status, transfer-byte, tile-unload, or Google billing-event stream. Browser Resource Timing entries can be incomplete, cross-origin transfer sizes may be zero, and entries do not prove that a response returned a billable tile. Tile GameObject creations are not request counts. A cost estimate uses observed renderer request starts × the configured price per thousand; it excludes root attempts, cannot know whether entries were billable, and does not allocate the shared free cap or volume tiers. Match totals depend on untrusted client-reported counters and are suitable for diagnostics, not enforcement.

The current configured price inputs are **$6.00 per 1,000 billable events** in the first global volume tier and a **1,000 monthly free-event threshold**. The latter is account/billing-aggregate usage, not an allowance that this code can assign to one player or match. Google currently defines the Photorealistic 3D Tiles billable event as a request that returns a 3D tile. Verify SKU, account location, current pricing, free usage, billing aggregation, and actual event metrics before release. Current Google docs also list a **10,000/day root tileset query** quota, **12,000/minute renderer** rate limit, and no daily cap on renderer-originating tile queries. In-app warning counters cannot enforce those project-wide quotas.

**Do not publicly launch based on this client tracker alone.** Before any public launch, enable the correct Google Cloud quotas/usage monitoring, conservative project limits where available, billing alerts and review procedures; reconcile browser estimates against Google's billing/usage metrics; and decide whether a trusted server-side or serverless collector is needed. Google budget alerts are not a hard spend cap, and this implementation does not read the Google Cloud billing API.

## Desktop web platform profile

The project uses Unity **6000.3.24f1** and Cesium for Unity **1.26.0**. Cesium's supported-platforms documentation says WebGL and WebGPU are supported on Unity 6+, and requires **Enable Native C/C++ Multithreading**. The existing Web build script enables Web threads and Brotli; Phase 6 also enables Unity frame timing stats. The Vercel deployment config already sends COOP `same-origin`, COEP `require-corp`, and CORP `cross-origin` headers for WebAssembly multithreading and Brotli response types.

Initial web acceptance is desktop **Chrome, Edge, and Firefox**. Mobile is out of scope for the first target. Browser API availability, the Cesium 1.26 Web binary, WebGL build, Vercel headers under the actual deployed origin, cross-origin request observation, memory, and five-browser gameplay have not been run in this workspace; confirm all in an actual build. No Unity/Cesium package was upgraded.

## Preserved multiplayer/geospatial scope

The streaming changes do not change WGS84/ENU alignment, arena units, NGO host simulation, fixed 30 Hz movement, owner prediction/reconciliation, or remote interpolation. Clients still submit intent rather than authoritative positions. The map telemetry RPC carries counters only and does not feed gameplay state. Relay's session host remains a client-hosted authority, not a trusted dedicated server; the future-facing `fire` field still does not implement combat.

## Versioned references checked for this phase (2026-10-06)

- [Cesium for Unity 1.26.0 `Cesium3DTileset` API](https://cesium.com/learn/cesium-unity/ref-doc/classCesiumForUnity_1_1Cesium3DTileset.html)
- [Cesium for Unity 1.26.0 `CesiumCameraManager` API](https://cesium.com/learn/cesium-unity/ref-doc/classCesiumForUnity_1_1CesiumCameraManager.html)
- [Cesium for Unity supported platforms](https://github.com/CesiumGS/cesium-unity/blob/main/Documentation~/supported-platforms.md)
- [Unity 6.3 frame timing statistics](https://docs.unity3d.com/6000.3/Documentation/Manual/frame-timing-manager-enable.html)
- [Google Map Tiles API usage and billing](https://developers.google.com/maps/documentation/tile/usage-and-billing)
- [Google Photorealistic 3D Tiles SKU billable event](https://developers.google.com/maps/billing-and-pricing/sku-details#map-tiles-photo-3d-ent-sku)
- [Google global pricing table](https://developers.google.com/maps/billing-and-pricing/pricing#maps-3d-tiles-pricing)
- [Google Photorealistic 3D Tiles root-session behavior](https://developers.google.com/maps/documentation/tile/3d-tiles)

## Verification

On 2026-10-06, the Unity-independent Phase 6 suite passed **8/8**, the existing Phase 1 repository suite passed **20/20**, the local WebGL server suite passed **3/3**, and the browser telemetry JavaScript passed `node --check`. See [`Tests/PHASE6_ACCEPTANCE.md`](../Tests/PHASE6_ACCEPTANCE.md). These checks cannot certify Unity compilation, Cesium streaming, actual tile eviction, browser Resource Timing coverage, Web GPU memory, or gameplay. Phase 1–5 live acceptance statuses remain unchanged and unclaimed.
