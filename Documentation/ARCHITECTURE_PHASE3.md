# Phase 3 architecture — Battle flow, sessions, and WebGL invites

Phase 3 preserves the Phase 1/2 Cesium + Google Photorealistic 3D Tiles foundation and its WGS84/ellipsoid-altitude ↔ fixed ENU-metre API. The frontend and terrain remain on each client. Unity Gaming Services (UGS) coordinates session membership and Relay connectivity; the match host is one of the players' WebGL clients.

> **Historical scope note:** the `NetworkGeoPlayer` position-report implementation and Phase 3 movement/trust descriptions below are superseded and removed by Phase 4. UGS Sessions, opaque invites, location validation, Cesium loading, Relay, NGO, and WebGL setup remain the Phase 3 foundation. For the current authoritative input/snapshot player system, read [`ARCHITECTURE_PHASE4.md`](ARCHITECTURE_PHASE4.md). The two-tab acceptance at Phase 3 remains **NOT RUN**.

## Components and responsibilities

| Component | Responsibility |
|---|---|
| `PhaseThreeRuntimeHud` | Battle screen, Places/map/coordinate selection, supported radius presets/custom input, explicit Google coverage confirmation, API-key inputs, create/join controls, and match details. Places search displays Google's official Maps attribution asset. |
| `GooglePlacesKeyReader` | Editor environment/local-file key convenience. Browser builds accept/save a separate Maps JavaScript + Places key locally; no API key is included in a match URL. |
| `WorldLocationValidator` | Validates WGS84 and radius; requires a manual check in Google's official visual Photorealistic coverage checker; checks centre terrain availability, exact loaded centre/radius, and a batched 17-sample surrounding render envelope. Fails closed. |
| `GeospatialWorldManager` | Preserves the established WGS84/ENU/georeference origin, metre-based radius, ground sampling, spawn placement, and arena queries. Joining clients call `BeginArena` with the exact metadata recovered from UGS. |
| `MatchSession` | Parses/validates match metadata stored in member-visible UGS session properties. Numeric doubles use invariant round-trip strings. Dynamic current-player count is read from the UGS session. |
| `BattleSessionCoordinator` | Per-tab UGS authentication profile, create/join by opaque code, static match validation, Cesium loading/preflight, Relay network-start wait, NGO player-spawn/first-position wait, and exact centre/radius comparison. |
| `NetworkGeoPlayer` | NGO-spawned player identity and server-written WGS84 position `NetworkVariable`; validates reports against the arena boundary on the client-host; creates remote Cesium-anchored capsule ghosts. |
| `WorldPvpGoogleMaps.jslib` | Browser `sessionStorage` identity, Maps JavaScript Places prediction/details requests using an Autocomplete session token, map-picker overlay, and clipboard bridge. |
| `Assets/WebGLTemplates/WorldPvp` | WebGL loader UI and reference to the Unity instance for browser callbacks. Root hosting is expected. |
| `WebDeploy/Vercel/vercel.json` and `Tools/serve_webgl_local.py` | Static `index.html` fallback for `/join/{code}`, cross-origin isolation headers, and correct precompressed Brotli response headers. |

## Create battle sequence

1. The user selects a place, map point, or numeric WGS84 latitude/longitude/ellipsoid altitude. The radius UI exposes 100, 250, 500, 1,000, and 2,000 m plus custom values. Values `<= 0`, non-finite values, and values `> 2,000 m` are rejected before session creation.
2. The user inspects the chosen centre in the official visual coverage map. No automatic “coverage API” is claimed. The local confirmation is not trusted as a server-verifiable proof.
3. The client starts its local Cesium arena and waits for tile collision/terrain at the centre. `WorldLocationValidator` then checks exact loaded centre/radius and requests batched terrain heights for one centre plus two rings of eight points, using the existing metre-based ENU frame. Missing terrain, timeout, disabled tiles, failed sample, unconfirmed surface coverage, or unsupported input blocks session creation with the required unavailable-coverage message (or a more specific input error).
4. Only after local preflight does the client initialize UGS and sign in anonymously. A private session is created with Relay options, member-visible match properties, a capacity, `Lobby` state, UTC creation time, and game mode. The generated/UGS join code is treated as an opaque bearer invite.
5. The UGS session integration starts NGO through Relay. The coordinator waits for the network to reach `Started`, the local NGO `PlayerPrefab` to be spawned, and a first geographic position to be accepted before it reports `ReadyForGameplay`.
6. The creator enters gameplay and the host updates the member-visible state to `InProgress`.

## Join-by-code sequence

1. A URL such as `https://example.test/join/7H29F` is served by a static rewrite to the Unity WebGL `index.html`. Only the normalized join code is present in its path. Query parameters and fragments are stripped when creating a new invite URL. No coordinates, altitude, radius, capacity, or game state are appended.
2. The `.jslib` reads/generates a per-tab profile in `sessionStorage` and passes it to `AuthenticationService` profile management. Separate tabs therefore select different local anonymous UGS identities, rather than competing with the same cached player profile.
3. On a detected `/join/{code}` route, the client waits for the profile callback and uses the saved local Map Tiles key. If keys are unavailable it leaves the code visible for manual join after keys are entered.
4. The client authenticates, calls `JoinSessionByCodeAsync`, and reads/validates match metadata only after becoming a session member. It rejects an unknown schema/mode, invalid coordinates/radius/capacity/state/code, or inconsistent player capacity.
5. It loads exactly the returned centre, ellipsoid altitude, and radius through `GeospatialWorldManager.BeginArena`, waits for centre terrain, repeats local coverage/terrain preflight, waits for Relay/NGO and the player spawn/position, then compares the loaded values exactly against the UGS doubles before reporting ready.
6. Each owner continues using the existing local character controller. Its `NetworkGeoPlayer` sends WGS84 double coordinates through a server RPC; the session host refuses positions outside `GeospatialWorldManager.IsInsideArena` and publishes accepted positions to the NGO `NetworkVariable`. Other clients convert those absolute coordinates through Cesium globe anchors, so their ghost players survive local origin shifts.

## Match metadata and URL boundary

`MatchSession` stores these properties in UGS session metadata, with `VisibilityPropertyOptions.Member`:

- Schema version.
- Centre latitude and longitude in degrees.
- Centre altitude in metres above the WGS84 ellipsoid.
- Radius in real metres.
- Maximum players.
- Session state (`Lobby`, `InProgress`, `Closed`).
- Creation timestamp in UTC round-trip form.
- Game mode and manual coverage-confirmation flag.

Session ID, join code, and current-player count come from the UGS session object. Exact location/radius do **not** go in any invite path, query, or fragment. A join code is still a bearer credential: anyone who obtains it may attempt to join while the UGS session permits it. Avoid sending invite URLs to analytics/logging systems; share them only with intended players.

Member visibility limits metadata exposure through unrelated public session queries, but it does not make a client-hosted session tamper-proof. The creator/host writes the metadata and state. The client-host is also the authority for the current WGS84 position checks. A malicious host can forge properties, and a malicious client may manipulate its locally simulated movement or reports within what the host accepts. This prototype has no trusted matchmaker/custom backend or dedicated authoritative game server. Do not mistake Relay for a simulation server; Relay routes traffic.

## Coverage preflight and its limits

Google's official [Photorealistic 3D Maps coverage checker](https://developers.google.com/maps/documentation/javascript/3d/coverage) is a visual tool. No public machine-readable endpoint for this implementation's coverage decision was verified; accordingly a human confirmation is required.

Cesium's batched `SampleHeightMostDetailed` validates loaded terrain-height availability. It does **not** establish that every sample has Photorealistic surface mesh/texture, that all 3D buildings/trees are complete, or that future streamed tiles cannot fail. The 17 points are the centre and two eight-point rings at 50% and 100% of the configured render/preload envelope (gameplay radius plus the existing visibility and preload buffers). This is a practical fail-closed preflight, not a formal coverage guarantee. A single failed/missing sample rejects the location; users can choose another centre/radius. The local client rechecks before joining and before displaying gameplay-ready state.

## Browser/host requirements

- Build WebGL with threads enabled and Brotli compression through `PhaseThreeWebBuildMenu`.
- Serve generated files over HTTP(S), never `file://`.
- Threaded WebAssembly requires a cross-origin isolated page. The provided static config/server add `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy: require-corp`.
- Unity's Brotli files need `Content-Encoding: br` and the matching MIME type. The Vercel config and Python helper supply these.
- Unity WebGL clients need browser-compatible WSS for Relay. The C# coordinator sets UTP `UseWebSockets` in a WebGL player. The MPS SDK documents WebGL's Relay protocol default as WSS; deployment/actual compatibility still requires a live test.
- External Google Maps/Places and Google tile requests must remain usable under the browser's CORS/COEP policies. The Places JS loader requests CORS; this must be verified in the target browser with actual restricted keys.
- Root-level hosting is assumed. The HTML template uses a root base and the route is `/join/{code}`.

## Fees and limits

No custom game server is required for this client-hosted MVP, but the other services are not unlimited-free guarantees:

- Google Cloud requires API/key/billing configuration. The [current global Google Maps Platform pricing list](https://developers.google.com/maps/billing-and-pricing/pricing) shows different SKUs/free usage caps for Photorealistic 3D Tiles, Maps JavaScript, Places autocomplete, and Place Details. Its current global list identifies Photorealistic 3D Tiles at a 1,000-event free monthly cap and then metered rates; per-SKU triggers and terms can change. Restrict keys, set quotas/budget alerts, and recheck Google's live price list before use.
- Unity's [Gaming Services pricing page](https://unity.com/products/gaming-services/pricing) currently lists Relay allowances (including a first-50-average-monthly-CCU allowance and bounded bandwidth allowance). Usage beyond the free allowance can be charged. Confirm the current account terms and configure billing notifications/limits.
- Vercel Hobby is free for personal, non-commercial use but has deployment limits; Unity WebGL/Cesium compressed files may exceed host upload-size caps. The preparation script reports files above 100 MiB. An optional domain is not included. For local acceptance, run the Python static server on the same machine as the two tabs; no custom always-on service is needed.

## Dependency and verification boundary

Direct package versions remain explicitly pinned in `Packages/manifest.json`. Unity must resolve transitive dependencies and generate `Packages/packages-lock.json`; never synthesize a lock file. Do not call this implementation compiled or accepted until the pinned packages resolve cleanly in Unity 6000.3.24f1 and the Unity console, EditMode tests, live tile requests, UGS session/Relay startup, WebGL host headers, and two-tab checklist have all been verified.
