# Phase 4 — server-authoritative input movement

Phase 4 preserves Phase 1/2's Cesium + Google geographic foundation and Phase 3's UGS Sessions, Relay, WebSockets, opaque join routes, and browser identity flow. It replaces the Phase 3 owner-reported WGS84 position stream with sequenced input commands and a session-host simulation.

> **Source status:** Phase 4 wiring and static checks are implemented in the workspace. No Unity import/compile, EditMode run, WebGL build, live UGS/Relay session, two-tab regression, or five-browser gameplay test has run. Treat the runtime acceptance state as **NOT RUN**. The client-hosted Relay session is not a trusted dedicated server and is not cheat-proof against its host.

## Runtime components

| Component | Phase 4 responsibility |
|---|---|
| `NetworkPlayerInputCommand` in `NetworkPlayerSimulation.cs` | NGO-serializable `Tick`, movement axes, look deltas, sprint, jump edge, and a future-facing fire edge. Contains no player position, transform, velocity, or outcome. |
| `NetworkPlayerSnapshot` | Server-authored identity/display name, fixed local ENU position (metres), yaw/pitch, velocity, ground/movement state, health/alive/team placeholders, server simulation tick, and last processed input/fire ticks. |
| `NetworkPlayerSimulation` | Shared deterministic kinematic step used by client prediction and the session host: movement, clamped look, gravity/jump, grounded state, movement classification, and circular arena bound. Tick interval is 1/30 s. |
| `NetworkPlayer` | Owner-side frame-rate input sampling, 30 Hz command generation, local prediction, acknowledgement/replay, and reliable owner-authenticated input RPC. The session host queues and simulates commands, then writes a server-only `NetworkVariable<NetworkPlayerSnapshot>`. Non-owners buffer snapshots and render interpolated ENU poses. |
| `PhaseOneTestCharacterController` | Existing local camera/motor adapter. Its old `Update`/`CharacterController.Move` loop is disabled while a session `NetworkPlayer` controls the local root. Predicted/authoritative ENU poses are converted through `GeospatialWorldManager` and written back as WGS84 to `CesiumGlobeAnchor`. |
| `PhaseOneSceneBuilder` | Rebuilds the generated player prefab with `NetworkObject` + `NetworkPlayer` and sets `NetworkConfig.TickRate` to 30 Hz. |
| `PhaseThreeRuntimeHud` | Preserves the Phase 3 Battle/Join UI and adds local simulation/ack diagnostics. It explicitly labels the Relay host and future fire input limitations. |

## Tick and command flow

1. Unity NGO's `NetworkConfig.TickRate` is set to `NetworkPlayer.ServerSimulationTickRate` (30). This does not couple game simulation to render FPS.
2. Each local owner samples keyboard/mouse at `Update` frame rate and accumulates elapsed unscaled time. At each 1/30-second input step it creates a monotonically sequenced command; mouse deltas are accumulated/split across due steps. Held movement/sprint values repeat, while jump/fire are edge-triggered.
3. A guest predicts the fixed-step movement locally immediately, records the command in a bounded pending-input list, applies the predicted ENU pose to its Cesium anchor, and sends the command with an ownership-checked reliable `ServerRpc`. It never sends its position or transform. The host owner enters the same server command queue directly.
4. The Relay session host validates sequence order, applies a per-player 30-command/second receive bucket with a small burst for network jitter, and simulates at most one accepted command per fixed 30 Hz step. Movement, speed, look, jump/gravity, grounded state, and arena radius are recomputed from command intent and host-side world settings. The client cannot declare the resulting location, velocity, grounded state, health, or alive state.
5. The host publishes a snapshot with `LastProcessedInputTick`. The owning guest removes acknowledged commands, resets prediction to the authoritative snapshot, replays remaining unacknowledged commands in order, and reapplies the resulting pose. Pending inputs are bounded to 256 commands; an unusually long transport outage can therefore discard older prediction history and requires runtime stress validation.
6. Non-owners buffer up to 32 snapshots, render 100 ms behind receipt time, interpolate position/velocity and shortest-path yaw on render frames, and extrapolate for at most 100 ms when a newer snapshot is late.

Input `FirePressed` is serialized, accepted with its command, and recorded as `LastFireInputTick` for future phases. It does not spawn a projectile, change health, trigger animation, or otherwise implement firing/combat in Phase 4.

## State, geospatial placement, and terrain

- The authoritative network position is a double-precision east/up/north coordinate in the immutable arena ENU frame owned by `GeospatialWorldManager`; network movement remains metre-based.
- The host converts ENU to WGS84 through `LocalToGeographic`, and clients write each resulting geographic location to `CesiumGlobeAnchor`. The transform is never the source of truth. Cesium origin shifting may change render-space transforms but must not change the fixed arena-local state.
- **Spawn policy superseded by Phase 9:** `GameplayArenaRuntime` plans deterministic distributed points inside the exact virtual radius for every player, including the host; it validates terrain/slope/semantics through the gameplay-data interface, applies boundary and pairwise margins, and rechecks live-player separation before assignment. The former host-centre/guest-offset expectation is retired. The generated scene currently has an explicitly labelled prototype-only visual-tile ground fallback; production requires an approved independent provider and is not ready without one.
- Networked owners disable the legacy local `CharacterController.Move` loop. With approved data, authoritative movement and projectile obstruction use stable gameplay collision proxies and semantic terrain queries, not dynamic photorealistic tile meshes. The prototype fallback cannot verify water, roads, buildings, cliffs, or restricted areas and must not be represented as production-quality collision or safety.
- Snapshot fields include identity/display name, `PlayerId`, ENU position, yaw/pitch, ENU velocity, grounded/movement state, health/alive/team, server tick, and input acknowledgements. Health/alive/team are server-written placeholders for later phases; clients have no authority to set them.
- The circular arena clamp is part of host-side movement simulation. Existing preflight/location rules and exact WGS84 session centre/radius remain unchanged.

## Authority and trust boundary

The Relay allocation is created for a UGS Multiplayer Services session whose creator runs NGO's server role. Guests send input commands and the host publishes movement snapshots. This prevents ordinary guest clients from directly writing position state through this component, but the host machine still owns the simulation and can be modified or abused by its operator. It is **not** a dedicated trusted server, anti-cheat, or production security boundary. A paid/dedicated server deployment is outside any no-hosting-fee promise.

No weapon, projectile, damage, hit validation, respawn, matchmaking, or moderation logic is implemented here. The `fire` bit is forward-compatible data only.

## Network and browser setup

The existing Phase 3 service path remains in place: anonymous Unity Authentication, UGS Multiplayer Services Sessions, Relay, NGO, Unity Transport, and UTP WebSockets in WebGL. Opaque `/join/{code}` links still carry no coordinates. The five-instance browser acceptance requires five separate real WebGL browser instances, not five native Editor/player builds. Google Map Tiles and Google Places keys remain separate, browser-restricted keys, subject to API-specific quotas and billing.

## Verification

Run the source/static checks with:

```bash
python3 Tests/validate_phase1.py -v
python3 Tests/test_webgl_local_server.py -v
```

Then, in the pinned Unity Editor after package resolution, run the EditMode suite including `NetworkPlayerSimulationTests.cs`, rebuild the scene, and make a WebGL build. None of those Unity steps has been performed in this workspace.

Manual release criteria, including the outstanding Phase 3 two-tab regression and the Phase 4 five-browser test, are recorded in [`../Tests/PHASE4_ACCEPTANCE.md`](../Tests/PHASE4_ACCEPTANCE.md). **Do not mark the phase accepted until the browser rows are observed and recorded.**
