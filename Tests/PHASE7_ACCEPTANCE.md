# Phase 7 — Combat System Acceptance

**Status at Phase 7 source implementation: NOT RUN.** Combat source, generated weapon-asset wiring, and static checks are not proof of Unity compilation or a real shot. No Unity EditMode Test Runner run, WebGL build, live Cesium/Google test, or browser multiplayer combat acceptance is claimed here. Phase 1/2 manual geospatial, Phase 3 two-tab, Phase 4 five-browser, Phase 5 terrain/gameplay, and Phase 6 performance/billing acceptances remain pending as recorded in their own checklists.

The Phase 7 runtime authority is the UGS session host running NGO server simulation over Relay. This is not a dedicated server and is not cheat-proof against the host. Actual browser gameplay is required for browser acceptance; native/Editor clients do not count.

## Automated checks

| Check | Status | What it proves / does not prove |
|---|---|---|
| `python3 Tests/validate_phase7.py -v` | PASS — 10 tests (2026-10-06) | Static source/data-flow checks for the Phase 7 authority boundary and prefab wiring only; not a C# compiler or runtime test. |
| `python3 Tests/validate_phase1.py -v` | PASS — 20 tests (2026-10-06) | Retained repository/geospatial/session invariants at source level; not Cesium or gameplay acceptance. |
| `python3 Tests/validate_phase6.py -v` | PASS — 8 tests (2026-10-06) | Phase 6 source/telemetry guardrails only; not live streaming/performance/billing acceptance. |
| `python3 Tests/test_webgl_local_server.py -v` | PASS — 3 tests (2026-10-06) | Local Python host routing/header smoke checks only; not Vercel or gameplay. |
| Unity Package Manager import / C# compile | NOT RUN | Open using pinned Unity `6000.3.24f1`, resolve existing pinned direct packages, and fix all compile/import errors. Let Unity create `Packages/packages-lock.json`; do not fabricate it. |
| Unity EditMode Test Runner | NOT RUN | Run all tests, including `CombatSystemTests` and `NetworkPlayerSimulationTests`; record Unity/package versions and results. |
| Asset/prefab generation | NOT RUN | In Unity, build/rebuild Phase 7 scene and confirm the valid prototype `WeaponDefinition` asset is referenced by the player prefab's `Weapon`, the match controller is configured, and the combat HUD is present. Do not overwrite custom generated-scene edits without a backup. |
| Desktop WebGL build | NOT RUN | Build using the documented Phase 3 WebGL menu, inspect console/build output, Brotli and cross-origin-isolation headers. This is not a gameplay pass. |

## Combat test matrix — run in actual browser instances after compilation

Use restricted test Map Tiles / Places keys, watch Google Cloud and Unity Gaming Services usage, and do not treat client telemetry as provider billing data. Record location, build hash, browser/version, network conditions, host identity, participant count, console errors, and reproducible steps. Do not record or publish API keys, full tile URLs, coordinates, or join codes in public logs.

| Test | Status | Pass condition |
|---|---|---|
| Clean two-browser match | NOT RUN | Host creates a covered arena; second real browser joins through the opaque UGS/Relay flow; both enter gameplay and receive server snapshots. Existing Phase 3 two-tab acceptance must still pass separately. |
| Five-browser regression | NOT RUN | Complete the Phase 4 target of five separate real browser instances in the same supported lobby, and verify the Phase 7 combat integration did not regress movement/interpolation or input authority. Do not substitute Editor/native clients. |
| Asset-backed weapon | NOT RUN | Inspect the generated `Phase7_PrototypeRifle.asset`, change a test copy's rate/damage/range/spread/magazine/reload/hit scaling, rebuild/re-enter, and confirm server behavior follows the asset rather than combat-script constants. Restore defaults after. |
| Confirmed miss/impact | NOT RUN | Fire into empty space and then into a collidable world surface. Ammo/fire rate are host controlled; impact presentation follows the host `ShotResolved` event; no health changes and no client-claimed hit is accepted. |
| Confirmed player hit | NOT RUN | Aim from one browser at another. The victim's replicated health changes only from the host; the shooter receives a hit marker only after the server confirms; victim receives source/damage feedback only after the server event. |
| Payload rejection | NOT RUN | With a test harness, submit stale/duplicate/future ticks, mismatched weapon ID, non-finite/out-of-range origin, non-unit direction, excessive aim deviation, commands while dead/reloading/empty, and calls from a non-owner. Observe no false damage/result. Confirm client payload still has no target/hit/damage fields. |
| Fire-rate/ammo/reload | NOT RUN | Hold fire and fire at boundary rates; verify host enforces the asset rate, exact magazine capacity, empty-magazine rejection, reload duration, and no firing during reload. Client-side pacing is not accepted as proof. |
| Six hit zones | NOT RUN | Confirm host hitboxes exist for Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg; verify configured multipliers affect host health damage and hit zone is reported by the server. Check limbs are reduced, torso normal, head high. |
| Death / no respawn | NOT RUN | Reduce health to zero. Verify `Alive=false` in host state, `Dead` presentation, server input rejection, disabled host hitboxes, a broadcast death/kill-feed event, and no respawn/reinitialization. Victim spectates and can cycle living players with Tab until match end. |
| Last survivor / disconnect | NOT RUN | With multiple participants observed, kill one and confirm the host freezes remaining movement/fire and broadcasts a winner. Repeat with a disconnect. Check no-winner result if all participants are eliminated. |
| Match result UI | NOT RUN | Verify host-broadcast winner/no-survivor result, kill feed, ammo/health HUD, remote death animation, muzzle/sound/effect presentation and spectator view on actual clients. |
| Trust boundary | NOT RUN | Review traffic and server state: clients send only input/fire intent; no client transform, victim, hit result, damage, health or death is accepted as authoritative. Record the host-client cheat limitation. |

## Existing phase gates — status is not inherited as passed

- Phase 1 Google/Cesium geospatial acceptance: **NOT RUN** — `Tests/PHASE1_ACCEPTANCE.md`.
- Phase 2 metre-based arena/geospatial acceptance: **NOT RUN** — `Tests/PHASE2_ACCEPTANCE.md`.
- Phase 3 two-tab UGS Sessions/Relay/browser acceptance: **NOT RUN** — `Tests/PHASE3_ACCEPTANCE.md`.
- Phase 4 five-real-browser networking/movement acceptance: **NOT RUN** — `Tests/PHASE4_ACCEPTANCE.md`.
- Phase 5 Cesium collision, movement and HUD browser acceptance: **NOT RUN** — `Tests/PHASE5_ACCEPTANCE.md`.
- Phase 6 streaming, telemetry, performance and Google billing reconciliation: **NOT RUN** — `Tests/PHASE6_ACCEPTANCE.md`.
- Phase 7 combat/browser acceptance: **NOT RUN** until all applicable checks above have actually been run and recorded.

See [`Documentation/ARCHITECTURE_PHASE7.md`](../Documentation/ARCHITECTURE_PHASE7.md) for the implementation and server validation details. Do not describe source completion, static tests, or local hosting as proof of Unity, live provider, multiplayer browser, or combat acceptance.
