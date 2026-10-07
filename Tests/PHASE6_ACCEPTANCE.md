# Phase 6 — World Streaming + Performance Engineering Acceptance

**Status at Phase 6 source implementation: NOT RUN.** Source changes and static checks are not proof of Unity/Cesium compatibility, live tile delivery, tile eviction, map billing accuracy, or browser performance. No public launch is approved by this checklist. Unity compilation, EditMode tests, WebGL build, live terrain, and gameplay in browser instances remain unverified. Phase 3 two-tab, Phase 4 five-browser, and Phase 5 terrain/gameplay acceptance remain NOT RUN.

## Current source profile (initial benchmark defaults, not results)

| Control / target | Starting value |
|---|---:|
| Cesium maximum screen-space error | 16 px (global projected-error limit) |
| Cesium maximum cached bytes | 256 MiB soft target; required visible tiles may exceed it |
| Concurrent tile loads | 4 |
| Loading descendants | 16 |
| Ancestor preloading | On |
| Sibling preloading | Off |
| Movement predictor | ENU displacement, 2 s look-ahead, capped at 24 m, 150 m far plane, 55° FOV, 0.5 s updates, 1 m/s minimum |
| Benchmark distance bins | 0–100 m very high; 100–300 m high; 300–750 m medium; 750 m+ horizon |
| Render targets | 60 FPS; P95 ≤16.7 ms |
| Unity reserved-memory target | ≤1,024 MiB; Unity allocator only, not total browser process |
| GPU memory target | ≤512 MiB; Web runtime cannot measure it directly |
| Sustained map-bandwidth target | ≤5 Mbit/s where browser transfer sizes are exposed |
| Estimated visible Cesium triangles | ≤1.5 million |
| Map warning | 1,000 observed renderer request starts/match; warning only, cannot stop Cesium |
| Map price input | $6 / 1,000 first-tier events, gross before shared free cap/tier adjustments; re-verified 2026-10-06 |

The distance bins are configurable **measurement anchors**, not guaranteed tile-selection distances. Cesium's hierarchy is selected by its projected screen-space error, camera/frustum, tile metadata, and available content. The bins should be used to record the actual scene and triangles seen at each range before retuning the global SSE.

## Automated / editor checks

| Check | Status | Acceptance criterion |
|---|---|---|
| Phase 6 static source suite | PASS — 8 tests (2026-10-06) | Pinned Cesium controls, bounded predictor, telemetry caveats, Web instrumentation, and unrun-status assertions pass. Static only; not a Unity compile. |
| Existing Phase 1 static suite | PASS — 20 tests (2026-10-06) | No geospatial, Cesium URL, or acceptance regression detected by the repository checks. |
| Existing Web local server/header tests | PASS — 3 tests (2026-10-06) | Local Python static host routing, COOP/COEP, and Brotli behavior pass; this does not test Vercel or gameplay. |
| Unity Package Manager import / C# compilation | NOT RUN | Open with Unity 6000.3.24f1, allow pinned package resolution, and compile without errors. Do not fabricate `packages-lock.json`. |
| Unity EditMode tests | NOT RUN | Run existing geospatial/network simulation tests plus any Phase 6 tests. |
| WebGL build | NOT RUN | Use Tools → World PvP → Phase 6 → Build Desktop WebGL Baseline; verify threaded Cesium native code, frame timing counters, Brotli output, COOP/COEP/CORP headers. |
| Actual Chrome desktop gameplay | NOT RUN | Load the generated Web build and exercise map, movement, telemetry, Relay and browser controls. |
| Actual Edge desktop gameplay | NOT RUN | Repeat on a separate supported browser instance and record version. |
| Actual Firefox desktop gameplay | NOT RUN | Repeat and record version; verify Cesium and cross-origin Resource Timing separately. |

## Cesium streaming / movement tests

Run only after Unity import and compilation succeed. Use a restricted test Map Tiles key and monitor Google Cloud usage while testing.

| Test | Status | Pass condition |
|---|---|---|
| Hierarchical view-driven loading | NOT RUN | Start at a small covered arena and confirm Cesium requests terrain for the current camera views instead of enumerating/downloading the planet. Confirm frustum culling remains enabled. |
| Cache target and unload behavior | NOT RUN | Inspect tiles while moving/turning between covered areas. Confirm unneeded content is evicted toward the 256 MiB target where Cesium permits; record that currently required render tiles can exceed it. Never label it a hard memory cap. |
| Concurrency/refinement limits | NOT RUN | Confirm runtime Cesium settings show 4 maximum simultaneous loads and 16 loading descendants; monitor load behavior and gaps. |
| Forward preloading | NOT RUN | While the local player moves, verify the additional disabled prediction camera appears in `CesiumCameraManager.additionalCameras`, stays at or below 24 m positional lead with a 150 m far plane, and is removed when motion stops. Confirm it does not render a second scene. |
| Stop / turn / origin shift | NOT RUN | Confirm the prefetch camera follows measured ENU movement, is removed during no movement/world-not-ready states, and remains aligned after Cesium origin shifts. |
| Streaming boundedness | NOT RUN | Exercise repeated movement and camera changes. Check tile cache target, active tile objects, load concurrency, requests/player/match, and project quota metrics; no whole-globe prefetch or runaway camera is accepted. |
| Distance-bin benchmark | NOT RUN | Collect screenshot/profile at 0–100, 100–300, 300–750 and >750 m using the same browser, camera, resolution and location. Record projected detail, visible triangle estimate, load time and request count; compare to the target profile. |

## Performance / telemetry tests

Use the desktop browser targets first. Record machine, CPU/GPU, browser/version, resolution, quality settings, tested location, network conditions, session duration and profiling build type. Repeat on at least one integrated-GPU and one discrete-GPU desktop before changing defaults.

| Metric/test | Status | Pass condition / qualification |
|---|---|---|
| FPS / P95 frame time | NOT RUN | Target 60 FPS and P95 ≤16.7 ms in active gameplay; report average/P95 across repeated runs, not a subjective “feels good.” |
| CPU / GPU frame timing | NOT RUN | Validate CPU data. GPU timing is unavailable in Unity WebGL and must be recorded using browser/OS GPU tools; do not interpret `n/a` as zero. |
| Unity RAM / browser RAM | NOT RUN | Record Unity allocated/reserved counters and browser process memory separately. Target Unity reserved ≤1,024 MiB; understand profiler totals exclude some native/browser memory. |
| GPU memory | NOT RUN | Target ≤512 MiB on tested desktop hardware, measured externally for Web. The Web app cannot read actual browser GPU VRAM; do not substitute `SystemInfo.graphicsMemorySize`'s Web fallback. |
| Tile memory | NOT RUN | Report configured Cesium cache target (soft), tracked live tile-object estimate and external process memory. The public Cesium 1.26 API used here does not expose exact live resident tile bytes. |
| Map bandwidth | NOT RUN | Compare Resource Timing counts/transfer sizes against DevTools Network for `tile.googleapis.com`. If transfer sizes are hidden, report unavailable/partial and retain DevTools totals separately. |
| Visible triangles | NOT RUN | Compare the AABB/frustum-based Cesium triangle estimate with Unity Profiler/rendering statistics. Budget is 1.5M estimated visible Cesium triangles. |
| Prediction cost | NOT RUN | Compare predictor on/off in equivalent routes. Confirm the quality/loading gain is worth the extra tile/transfer and memory cost; disable or retune if it exceeds budgets. |

## Map usage / billing tests

| Test | Status | Pass condition / qualification |
|---|---|---|
| Root attempt counter | NOT RUN | Each Unity root tileset enable/load attempt increments the local attempt counter; compare with browser-observed root resource entries separately. |
| Renderer request observation | NOT RUN | In Chrome/Edge/Firefox, compare observed renderer resource entries with DevTools. Confirm missing cross-origin entries, retries, caching and failures explain any difference. This is not Google billing. |
| Tile-object proxy | NOT RUN | Compare Cesium `OnTileGameObjectCreated` count with observed network activity and note that objects are not requests and cached/recreated behavior may differ. |
| Per-player counts | NOT RUN | Each connected player's NGO-owned object reports its local counters to the host. Verify no telemetry field enters input commands, ENU movement, health, transforms or authoritative snapshots. |
| Per-match aggregate | NOT RUN | Host shows contributing/reported clients and summed self-reported counters; compare to the independent per-browser logs. Missing and untrusted clients must be shown as a limitation. |
| Session duration / local ledger | NOT RUN | Confirm duration is recorded through leave/close and the local PlayerPrefs ledger remains bounded to the most recent 20 sessions. Clearing browser storage must be treated as loss of local history. |
| Cost estimate | NOT RUN | Cost uses renderer request-start proxy × configured first-tier price and is labelled gross/approximate. Do not count it as Google billable events or allocate a free cap per match. Reconcile against Cloud billing/usage. |
| Warning behavior | NOT RUN | At configured warning threshold, the HUD warns but continues streaming. Confirm nobody mistakes it for a hard stop. |
| Privacy / secret handling | NOT RUN | Confirm no root URL, API key, tile URL, join code, geographic coordinates or full Resource Timing name is logged or persisted by the tracker. |

## Google Cloud launch gate

As checked against official Google pages on **2026-10-06**, the current global table lists a 1,000-event monthly free usage cap and $6.00/1,000 first global price band for Map Tiles API: Photorealistic 3D Tiles; Google defines the event as a request that returns a 3D tile. The Usage and Billing page lists 10,000 root tileset queries/day, a 12,000/minute tile renderer rate limit, and unlimited renderer-originating tile requests/day. Usage/pricing can change and are billing-account/location dependent. The configured local estimate is therefore a **starting input only**.

Before public release, an operator must:

1. Verify pricing SKU, billing account location, monthly free usage already consumed across all projects, rate/daily quotas and billing events in Google Cloud Console.
2. Enable Cloud usage/billing export or approved reporting and compare measured browser/client estimates against Google's source-of-truth reports.
3. Set conservative Google project quotas where supported, billing alerts and a staffed usage-review process. A budget alert alone is not a hard spend cap; renderer traffic is not controlled by this Unity warning.
4. Decide and implement a trusted central aggregation/control path if public per-match limits are required. Relay-hosted player reports are untrusted and there is no Google billing API credential in this client.
5. Keep the release blocked if reliable project-level usage monitoring and cost controls are not in place.

References: [Google usage and billing](https://developers.google.com/maps/documentation/tile/usage-and-billing), [Google SKU event definition](https://developers.google.com/maps/billing-and-pricing/sku-details#map-tiles-photo-3d-ent-sku), [Google current pricing](https://developers.google.com/maps/billing-and-pricing/pricing#maps-3d-tiles-pricing).

## Browser platform acceptance

- Verify Unity 6000.3.24f1 Web target uses threads and that the generated output is served with required `Cross-Origin-Opener-Policy: same-origin`, `Cross-Origin-Embedder-Policy: require-corp`, and appropriate `Cross-Origin-Resource-Policy`/Brotli headers.
- Test **actual gameplay** in separate desktop Chrome, Edge, and Firefox browser instances, not Editor/native clients. Capture browser version, console errors, network failures, frame/memory stats, and Cloud usage.
- Repeat Phase 3 two-tab regression and Phase 4 five-browser acceptance after Phase 6 source integration. Verify multiplayer still sends inputs and host-simulates 30 Hz movement; map-usage telemetry is not authoritative gameplay state.
- Mobile browser acceptance is not part of this initial target.

## Acceptance statuses that remain unclaimed

Unity compile/import, Unity EditMode, WebGL build, live terrain/collision, Phase 3 two-tab browser test, Phase 4 five-browser gameplay, Phase 5 terrain/gameplay, all Phase 6 live streaming/performance/billing tests: **NOT RUN** until carried out and recorded.
