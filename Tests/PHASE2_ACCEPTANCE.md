# Phase 2 acceptance checklist — geospatial arenas

**Status at Phase 2 implementation: NOT RUN.** Unity Editor execution, Cesium/Google streaming, Play Mode, and the location/radius matrix have not been verified in this workspace. Static checks and authored Edit Mode tests are not substitutes for this manual acceptance.

## Scope and prerequisites

- Open the project with Unity `6000.3.24f1`; allow the pinned packages to resolve. Unity must generate `Packages/packages-lock.json`—do not hand-author it.
- Run `Tools → World PvP → Phase 2 → Build or Rebuild Scene` if the scene is missing or still contains only the original Phase 1 objects (the rebuild replaces the generated scene).
- Use a valid restricted Google Map Tiles API key, active billing, internet access, and locations with 3D Tiles coverage. Keep Google's required attribution visible.
- Phase 2 adds geographic/local coordinate conversion, arena metrics, boundary feedback, Cesium origin shifting, and a local prototype guard only. It does **not** add multiplayer/server code. The future authoritative server must evaluate the WGS84 input with `GeospatialWorldManager.IsInsideArena` / `GetDistanceFromArenaCentre`; the visible ring and local client clamp are not server enforcement.

## Automated checks

- [ ] Run `python3 Tests/validate_phase1.py -v` from the project root; all repository checks pass.
- [ ] In Unity Test Runner, run all EditMode tests. The manager tests cover 100 m, 500 m, and 1,000 m arenas at both Hyde Park and the Harefield-area origin, ENU round-trips, radius membership, render-buffer arithmetic, and invariance when the Cesium render origin changes.
- [ ] Resolve any compiler/test errors before starting the live matrix.

## Live location × gameplay-radius matrix

For each row, load the WGS84 centre and radius, wait for tile collision, record the initial player WGS84/ENU values, walk to the perimeter, turn around, and record them again. The character should remain geographically anchored to the selected site throughout movement and any Cesium origin shift.

| Location | Centre (lat, lon) | Gameplay radius | Initial / perimeter / return WGS84 observed | Inside/outside guard observed | Result |
|---|---|---:|---|---|---|
| Hyde Park, London | `51.5073, -0.1657` | 100 m | | | NOT RUN |
| Hyde Park, London | `51.5073, -0.1657` | 500 m | | | NOT RUN |
| Hyde Park, London | `51.5073, -0.1657` | 1,000 m | | | NOT RUN |
| Harefield area | `51.6030, -0.4840` | 100 m | | | NOT RUN |
| Harefield area | `51.6030, -0.4840` | 500 m | | | NOT RUN |
| Harefield area | `51.6030, -0.4840` | 1,000 m | | | NOT RUN |

For every row:

- [ ] The gameplay radius is reported in real metres and does not change when the camera/visual scale changes.
- [ ] The 100 m / 500 m / 1,000 m test ends up on the intended geographic location; reloading another location moves the same anchored test character.
- [ ] The inner ring marks the gameplay radius; the additional rings are outside it. With default buffers the visibility radius is gameplay + 100 m and the render/preload envelope is gameplay + 250 m.
- [ ] Walk toward the edge until the local prototype guard constrains the capsule centre just inside the gameplay radius. The HUD shows remaining distance / boundary feedback; the rendered ring is not doing the enforcement.
- [ ] Move outward in the 1,000 m case far enough to cross the configured 750 m Cesium origin-shift threshold, then verify WGS84 anchor and arena-centred ENU coordinates remain continuous and the boundary still uses the original arena centre.
- [ ] Walk inward again; player geographic coordinates track the real site rather than remaining fixed to the current Cesium rendering origin.

Cesium tiles are loaded according to the camera frustum and Cesium's LOD/cache policy. The displayed render radius is a planning envelope and drives the camera far plane; it is **not** represented as a hard radial tile-streaming cutoff.

## Record observations

| Observation | Notes / result |
|---|---|
| Cesium/Unity version and Console errors | |
| Location coverage / attribution | |
| ENU drift across origin shift | |
| Tile collider replacement while standing | |
| Boundary overshoot / clamp behavior | |
| Camera/visible envelope quality | |
| Failed radius/location combinations and repro | |

## Release boundary

The Phase 2 local prototype gate can pass only after the Unity matrix above is run and recorded. Multiplayer release remains blocked until a server owns or validates player movement and rejects positions outside `IsInsideArena`; do not present the client HUD, line renderers, or local `CharacterController` clamp as authoritative enforcement.
