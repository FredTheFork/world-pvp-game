# Phase 2 architecture — geospatial world and arenas

## Scope

Phase 2 extends the Cesium/Google real-world foundation into a reusable, metre-based arena world system. It does not add networking, combat, matchmaking, persistence, or a dedicated server. Google Photorealistic 3D Tiles remain view-dependent visual data; their changing tile colliders are still a prototype ground-query source, not authoritative gameplay geometry.

## Coordinate contract

- `GeoPosition`: WGS84 latitude/longitude in degrees plus altitude in metres above the WGS84 ellipsoid.
- `LocalPosition`: a separate double-precision metre vector relative to the selected arena's fixed `WorldOrigin`: X = East, Y = Up, Z = North (ENU).
- Unity world units remain metres; the manager sets `CesiumGeoreference.scale` to `1.0` when it selects an arena.
- The arena centre is geographic. Gameplay membership is the horizontal ENU distance `sqrt(East² + North²)` from `WorldOrigin`; altitude is not part of arena radius.
- `DistanceBetweenPlayers` is documented as straight-line 3D ECEF distance. It is not the arena-membership calculation.

`GeospatialWorldManager` is the only project owner for `LocalToGeographic`, `GeographicToLocal`, Cesium/Unity coordinate transforms, `GetGroundHeight`, player distance, arena-centre distance, and `IsInsideArena`. Other runtime components consume the manager API rather than implementing geographic math independently.

## Fixed game origin vs Cesium render origin

When an arena is selected, the manager positions `CesiumGeoreference` at the chosen WGS84 centre and captures its local-to-ECEF and ECEF-to-local matrices. These immutable matrices define the match's ENU frame. `CesiumOriginShift` is attached to the anchored test-player root with a default 750 m threshold; Cesium may subsequently move its rendering origin to preserve precision. The manager deliberately keeps the captured arena matrices, so arena-local positions and membership remain relative to the original centre.

Geographically placed scene content that must survive origin shifts has a `CesiumGlobeAnchor`: the test character and the boundary-ring root do. The rings are rooted at the sampled arena surface and continue to use local East/North axes. The camera follows the anchored character. Re-selecting an arena intentionally captures a new fixed ENU frame.

## Arena size, buffers, and rendering

- `GameplayRadiusMeters` is the only radius used for `IsInsideArena`.
- `VisibilityRadiusMeters = gameplay radius + VisibilityBufferMeters`.
- `RenderRadiusMeters = visibility radius + PreloadBufferMeters`.
- Defaults: 100 m visibility buffer, 150 m preload buffer, 25 m boundary-warning distance, and 750 m Cesium origin-shift threshold.
- The camera far plane is configured from the render envelope (with a 1,200 m minimum and a 2× envelope margin). Cesium still requests tiles from the camera frustum, screen-space error, and cache policy; the radius is not a hard circular stream cutoff.
- Three anchored rings communicate gameplay, visibility, and preload extents. The graphics are not enforcement.

## Boundary authority

`IsInsideArena(GeoPosition)` and `GetDistanceFromArenaCentre` expose the deterministic geographic query future server movement/validation code must call. The current `PhaseOneTestCharacterController` invokes `EnforceLocalArenaBoundary` after local movement; the manager clamps the capsule centre just inside the gameplay radius and supplies HUD feedback. This is an offline prototype guard only. No network server exists in this phase, so no server authority is claimed. Multiplayer/public release must remain blocked until the server validates or simulates authoritative player positions against `IsInsideArena`.

## Ground height

`GetGroundHeight` samples the nearest currently loaded Cesium tileset collider with a geographic downward ray. It returns ellipsoid altitude, not mean-sea-level height. `TryGetGroundHeight` is the non-throwing form when tile collision may be unavailable. The result can change as Google tile LOD/collision changes; use a deliberate production ground source before relying on it for combat or anti-cheat.

## Tests and acceptance

Unity EditMode tests exercise geographic/local round-trips and arena radii of 100 m, 500 m, and 1,000 m at Hyde Park and the Harefield-area origin. They also simulate a changed Cesium render origin while asserting the fixed arena frame remains stable. `Tests/PHASE2_ACCEPTANCE.md` contains the live six-combination tile/anchor/boundary matrix. Its status remains **NOT RUN** until Unity execution is recorded.
