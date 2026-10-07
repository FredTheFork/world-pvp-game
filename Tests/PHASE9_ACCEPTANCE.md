# Phase 9 acceptance — virtual real-world gameplay system

> **Status: NOT ACCEPTED / NOT RUN.** Phase 9 source integration now contains a provider boundary, deterministic arena/spawn planner, separate stable proxy queries, and a strict dedicated-server readiness gate. This repository does **not** yet contain a concrete independent terrain/feature provider or licensed gameplay dataset. The generated local scene explicitly enables a prototype-only visual-tile ground fallback. No Unity compile, EditMode run, WebGL build, dedicated-server build, actual-browser gameplay, provider-licence review, or live match acceptance has been performed. Do not describe the system as production-ready or Phase 9 accepted.

## Virtual arena and spawn plan

- [ ] Create a match and record its exact WGS84 centre, ellipsoid altitude, virtual radius, and player cap in the UGS session metadata.
- [ ] Join from a second browser and confirm both instances configure the exact same centre/radius on create and join paths.
- [ ] In a production test, use an independently licensed `WorldGameplayDataSource` whose provider ID, licence/terms reference, attribution, legal-review approval, and complete arena coverage are recorded.
- [ ] Verify that the generated arena boundary uses the configured virtual radius, that spawn points are distributed across the usable area, and that no player is assigned the centre merely because they are the host.
- [ ] Verify spawn point determinism for the exact centre/radius, WGS84/ENU agreement across instances, character clearance from the boundary, pairwise separation, and a second live-player separation check before a joining player is assigned.
- [ ] Verify each candidate fails when terrain data is missing, the surface is not walkable, slope exceeds the configured threshold, or semantic assessment reports water, road, building, cliff, restricted land, or unknown area.
- [ ] Verify the extensible objective-candidate list contains at least one safe candidate separate from player spawn points; confirm objective candidates are virtual coordinates and do not cause real-world visits or rewards.
- [ ] Verify all failure paths report why a safe plan could not be created; they must never silently fall back to the centre or treat missing semantic data as safe.

## Data, collision, renderer, and provenance separation

- [ ] Dedicated-server startup runs with photorealistic visual tiles disabled and no Google Map Tiles key, and succeeds only with the approved independent provider and collision data.
- [ ] Remove the provider, remove legal approval, invalidate coverage, or make a terrain/feature query unknown; verify production startup fails before UGS session creation and never switches to visual-tile collision.
- [ ] Confirm persistent gameplay collision boxes are collected only through the provider interface. Movement, safe-spawn, capsule-clearance, and projectile-blocking queries use the stable gameplay collision world—not dynamic photorealistic tile meshes.
- [ ] Confirm visual tile physics meshes are disabled when an approved provider is active. Visual renderer loading, caching, credits, tile telemetry, and Google billing remain separate systems.
- [ ] Review the selected provider's terms, data-source lineage, derived-data rights, attribution, retention, and downstream use before activation. Do not derive gameplay terrain/features/proxies from Google Maps Content. Do not combine a non-Google gameplay dataset with Google map content without legal review.
- [ ] In a local prototype build only, verify the UI explicitly says that transient visual-tile ground collision does **not** verify water, roads, buildings, cliffs, restricted areas, or safe-spawn avoidance. Prototype mode is not a production safety system.

## Safety and match rules

- [ ] Verify this exact prominent message appears in the player UI before and during gameplay: **“This is a virtual game. Stay aware of your surroundings. Do not enter roads, private property, restricted areas or dangerous locations while playing.”**
- [ ] Verify radius controls only the virtual arena. No gameplay objective, score, reward, or acceptance check requires or incentivizes travel to the corresponding real-world coordinates.
- [ ] Verify the match remains virtual and can be played without physical travel to the selected location.
- [ ] Preserve Phase 8: fixed-tick authoritative simulation; no client-authored authoritative transforms; server-side validation, spatial review cells, and the configured 250–500 ms lag-history window remain unchanged.
- [ ] Verify **no respawn**: an eliminated player stays in spectator mode through match end, and a reserved initial spawn is not reused to return an eliminated player to play.

## Required verification before acceptance

1. [ ] Run `python3 Tests/validate_phase1.py -v`, `python3 Tests/validate_phase6.py -v`, `python3 Tests/validate_phase7.py -v`, `python3 Tests/validate_phase9.py -v`, and `python3 Tests/test_webgl_local_server.py -v`.
2. [ ] Resolve the pinned Unity packages in the declared Unity version; compile all runtime/editor/test assemblies and run every EditMode test, including `GeographicArenaGeneratorTests` and `GameplayCollisionWorldTests`.
3. [ ] Build both WebGL clients and the dedicated-server target. Record logs, build hashes/sizes, package-lock hash, provider asset version, licence/review record, and server configuration with credentials redacted.
4. [ ] Repeat Phase 3 two-browser and Phase 4 five-browser acceptance in actual browsers. Test late joins, player movement near unsafe features, lag, tile-origin shifts, disconnects, elimination/spectating, and match end.
5. [ ] Record real provider/data usage and costs, Unity service usage/cost, Google Maps usage/cost, browser/device versions, and any failures. Do not promise unlimited or zero-cost operation.
6. [ ] Complete all required rows above with evidence before changing this file's status to accepted.
