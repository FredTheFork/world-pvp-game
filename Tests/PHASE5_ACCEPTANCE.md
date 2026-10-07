# Phase 5 — Player Movement + Character Gameplay Acceptance

**Status at Phase 5 source implementation: NOT RUN.** This checklist describes required Unity and browser tests; it is not evidence that the tests passed. Unity compilation, EditMode execution, live Cesium/Google terrain, and browser gameplay remain unverified. The earlier Phase 3 two-tab regression and Phase 4 five-browser test also remain pending.

Phase 5 is layered on the Phase 4 input-command architecture. Owners send sequenced input intent only; the session host is authoritative for fixed-tick ENU movement. There are no client-authored authoritative position/transform reports. The local owner predicts and replays unacknowledged inputs; other clients interpolate host snapshots. The UGS/Relay session host is a player client, not a dedicated or cheat-proof production server.

## Automated / Unity checks

| Check | Status | Notes |
|---|---|---|
| Unity import and C# compilation | NOT RUN | Resolve `Packages/manifest.json` in the pinned Unity editor first; do not fabricate `packages-lock.json`. |
| EditMode `NetworkPlayerSimulationTests` | NOT RUN | Includes acceleration/deceleration, sprint normalization, crouch/stand clearance flag, slope projection, jump/landing, arena bounds, look, and interpolation. |
| WebGL build | NOT RUN | Confirm first-person camera/arms, runtime overlay, NGO Relay WebSocket transport, and browser input. |
| Phase 3 two-tab session regression | NOT RUN | Required before claiming Phase 3 acceptance. |
| Phase 4 five-browser authoritative gameplay | NOT RUN | Five separate browser instances; native/Editor clients do not count. |

## Browser gameplay acceptance — five separate browser instances

Run only after Unity compile, EditMode tests, and the Phase 3 two-tab regression have passed. Record build URL, browser/version, session host tab, client tabs, date, tested coordinates, and console/network errors.

| Test | Status | Pass condition |
|---|---|---|
| First-person spawn | NOT RUN | Gameplay camera is at configured eye height, local body is not visible through the camera, and the first-person arms are present. |
| Walk / accelerate / decelerate | NOT RUN | W/A/S/D accelerates toward the configured walk speed and slows using the configured deceleration without depending on render FPS. |
| Sprint / crouch | NOT RUN | Shift selects sprint speed; C or Ctrl crouches, uses the crouch speed/height, and remains crouched where streamed geometry prevents standing. |
| Jump / gravity / landing | NOT RUN | Space applies the configured jump height; gravity returns the player to a sampled ground surface and presents the landing state. |
| Surface following | NOT RUN | Host and clients traverse flat ground, slopes, steps, roads, parks, and urban block geometry without persistent sinking, hovering, foot sliding caused by origin changes, or loss of geographic alignment. Record locations and observed collider coverage. |
| Prediction / reconciliation | NOT RUN | Owner prediction remains responsive; authoritative snapshots reconcile without accepting client positions; remote players interpolate. Network tick remains 30 Hz independent of FPS. |
| Geographic debug HUD | NOT RUN | Latitude, longitude, and ellipsoid altitude track the anchored player and agree with the chosen location to expected display precision. |
| ENU minimap | NOT RUN | Arena boundary is centred on the fixed session coordinate frame; local player, known network players, and headings update from session ENU state; no second Google map renderer is used. |
| Locomotion presentation | NOT RUN | Idle, walk, run, jump, fall, land, and death presentation states are visible on the local or remote avatar as applicable. Death is only a visual state; no damage/weapon system is included. |
| Boundary / georeference | NOT RUN | Arena bounds remain host-enforced and the WGS84-to-ENU mapping remains stable during Cesium render-origin shifts. |

## Honesty and scope notes

- Do not claim the terrain/browser acceptance row until it has been exercised in actual browser gameplay against loaded Cesium tile colliders. The ground query is opportunistic; absent collision data is not a terrain guarantee.
- A host running NGO server authority through Relay is still a client-hosted session, not a trusted dedicated server and not cheat-proof against the host.
- The `fire` field remains future-facing intent only. This phase does not implement firing, weapons, damage, or combat.
- Phase 5 locomotion settings are tunable; the acceptance pass must record the values used. A change to pinned packages is outside this phase unless separately justified and documented.
