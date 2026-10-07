# Phase 5 — Player Movement + Character Gameplay

Phase 5 extends the Phase 4 NGO input-command simulation without replacing the Phase 1/2 Cesium and metre-based geospatial model or the Phase 3 UGS Sessions/Relay flow.

## Authority and coordinate frame

- Clients send sequenced movement/look intent, sprint/crouch/jump flags, and the retained future-facing fire bit. The command has no position or transform fields.
- The client-hosted UGS session host accepts owner-authenticated inputs and simulates the player at the existing fixed **30 Hz** tick. Render FPS is independent.
- The owner predicts locally, removes acknowledged commands, and replays the pending input buffer after authoritative snapshots. Non-owners render delayed/interpolated snapshots with bounded extrapolation.
- Authoritative position and ground support stay in the immutable session-local ENU metre frame. `GeospatialWorldManager` remains the WGS84/ENU and Cesium-world conversion boundary, so Cesium render-origin shifts do not redefine the arena.
- Relay routes traffic; it does not turn a host client into a dedicated server. The host is not cheat-proof against itself. No client-authored authoritative transforms are introduced.

## Tunable locomotion

`PhaseOneWorldSettings` now exposes walk, sprint, crouch, jump height, gravity, ground acceleration/deceleration, air acceleration/deceleration, standing/crouched capsule dimensions, capsule radius/skin/step offset, maximum slope, terrain snap/sample intervals, landing presentation duration, and first-person camera tuning. Defaults are approximately 4.5 m/s walk, 7 m/s sprint, 2.2 m/s crouch, 1.2 m jump height, and a 1.8 m standing capsule; these are defaults, not constants enforced by the simulator.

`NetworkPlayerSimulation.Step` is a deterministic fixed-step kinematic model shared by server simulation and prediction/replay. It normalizes diagonal input, approaches target planar velocity with configurable acceleration/deceleration, uses separate air control, projects grounded intent along the replicated ENU ground normal, applies jump impulse/gravity, handles crouched speed/state, and clamps the player to the arena radius with capsule-radius inset.

At the configured terrain-sample interval, the host—and the local predicting client for immediate local support—uses the current streamed Cesium collider to sample ellipsoid altitude and transform its normal into session ENU. The host keeps that support in snapshots. Small changes are stepped/snapped; excessive upward steps and over-limit slope transitions are blocked, and larger downward changes are treated as drops. Falling players are snapped to sampled ground on descent and enter a short replicated landing state. Crouch-to-stand is denied when the sampled collider query does not provide capsule clearance.

Ground collision is deliberately opportunistic: if the relevant Cesium tile collider is not loaded or available, the system retains the last known support rather than inventing flat ground. This is not proof that every road, park, roof, stair, or urban surface has usable collision; that requirement needs live browser testing.

## First-person view and procedural locomotion presentation

The generated scene now uses a first-person camera pivot at the tunable eye height, with local body mesh hidden and simple first-person arm visuals. Crouch height and movement states drive view height/bob. Remote players are lightweight procedural capsule-based figures; their visual limbs/body animate from replicated states without importing animation assets.

Presentation states include idle, walking, sprinting/running, crouching/crouch-walking, jumping, falling, landing, and dead. The dead pose is a presentation path for an `Alive == false`/zero-health snapshot only. This phase provides no way to deal damage and does not implement death gameplay, weapons, or combat.

## Geographic debug HUD and minimap

`PhaseFiveGameplayHud` is visible during gameplay. Its coordinate readout converts the player's WGS84 anchor and shows latitude, longitude, and ellipsoid altitude. Its compact north-up minimap is drawn locally by Unity IMGUI; it does not render or request a second Google map. The arena ring is centred on the session ENU origin, while local/known players and their headings use the same network snapshot ENU positions and yaw values.

## Verification state

Source-level tests were extended for acceleration/deceleration, crouch speed/clearance state, slope projection, jump/landing, bounds, interpolation, and existing command-intent invariants. They have not yet been run in Unity. The static checks do not prove C# compilation or gameplay. See [`Tests/PHASE5_ACCEPTANCE.md`](../Tests/PHASE5_ACCEPTANCE.md). Phase 3's two-browser-tab test, Phase 4's five-browser gameplay acceptance, and Phase 5 terrain/browser acceptance remain NOT RUN until actually exercised.

The `fire` input still records/acknowledges intent only. A Relay-routed client-hosted session remains distinct from trusted dedicated production authority; no dedicated-server or free-hosting guarantee is implied.

## Phase 6 extension — streaming and performance

Phase 6 adds a Cesium 1.26.0 streaming controller, bounded movement-prediction camera, runtime performance overlay, and local/host-aggregated map-usage estimates. It does not change the fixed ENU authority model or turn Relay into a dedicated server. See [`Documentation/PHASE6_STREAMING_PERFORMANCE.md`](PHASE6_STREAMING_PERFORMANCE.md) for supported Cesium controls, benchmark targets, Google billing/quota caveats, and desktop Web profile. See [`Tests/PHASE6_ACCEPTANCE.md`](../Tests/PHASE6_ACCEPTANCE.md) for the required live acceptance; Unity/browser/gameplay tests remain NOT RUN.
