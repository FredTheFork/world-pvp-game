# Phase 9 — virtual real-world gameplay system

> **Readiness: source integration only; NOT COMPILED / NOT ACCEPTED.** The repository now provides the runtime contracts and planner, but no concrete, independently licensed terrain/feature provider or dataset has been selected or integrated. The generated local scene opts into an explicit prototype-only visual-tile fallback. A dedicated production server refuses to start without a legally reviewed, complete independent gameplay-data source. Static Python checks are not Unity compilation or gameplay acceptance.

## Design boundary

- `GeoPosition` and `GeospatialWorldManager` remain the WGS84/ellipsoid-height and fixed ENU authority; the arena radius remains metres in the **virtual game world**.
- `WorldGameplayDataSource` is the provider plug-in boundary. An implementation must report full arena coverage, ground samples, semantic area classifications, and simplified persistent collision proxies. Missing/unknown data is not safe.
- `GameplayCollisionWorld` converts provider proxies to fixed ENU boxes, builds a 25 m spatial-cell index, and owns gameplay ground, capsule, movement-sweep, and projectile-blocker queries. It has no Cesium or renderer dependency.
- `GameplayArenaRuntime` is configured from exact centre/radius/player-count metadata on both session create and join. It generates deterministic, distributed player points; checks terrain/slope, semantic hazards, arena margins and pairwise spacing; rechecks live-player separation before assignment; and provides a separate safe objective-candidate list.
- The existing virtual boundary visualizer follows the arena radius. Objective candidates are virtual positions only; they are not linked to real-world travel, physical visits, or dangerous locations.
- The exact safety message is shown persistently by the runtime HUD:

  > This is a virtual game. Stay aware of your surroundings. Do not enter roads, private property, restricted areas or dangerous locations while playing.

## Provider and legal review gate

Before production, implement and assign a concrete `WorldGameplayDataSource` ScriptableObject. Supply provider identity, licence/terms reference, required attribution, explicit legal-review approval, and verified coverage of the complete requested arena. Ensure the provider's data lineage and derived-data rights cover terrain sampling, semantic classification, persistent proxy generation, multiplayer distribution, and retention.

Do not derive gameplay metadata/proxies from Google Maps Content or photorealistic tile meshes. Do not combine a non-Google gameplay dataset with Google map content until legal review approves that exact use. Google's coverage confirmation remains a manual visual-renderer preflight; the code does not claim a Google machine-readable photorealistic coverage API.

The generated Editor scene enables transient visual-tile ground/simple-ray collision **only as a prototype fallback**. That fallback does not verify water, roads, buildings, cliffs, restricted areas, or safe-spawn avoidance and is not a production safety/collision system. If an independent provider is assigned but fails legal approval or coverage, the system fails instead of falling back silently. Dedicated-server arena startup disables Google rendering and visual-tile physics and requires independent provider coverage before UGS session creation.

## Authority and match rules retained

- Owners submit input intent, not authoritative transforms. The fixed-tick authority remains host/server-side; production deployments must use the trusted dedicated-server path rather than treating a player-hosted Relay allocation as cheat-proof.
- Phase 8's server-side validation, spatial review cells, 250–500 ms lag-history configuration, and review-oriented cumulative violation score remain in force.
- **No respawn:** eliminated players remain spectators through match end. Initial spawn slots are reserved and are not used to return an eliminated player to play.

## Verification

`Tests/validate_phase9.py` checks repository source invariants only. The deterministic generator and provider-backed proxy layer have new EditMode tests, but those tests have not been run. Full completion still needs an approved provider, legal review, Unity import/compile, all EditMode tests, dedicated and WebGL builds, the Phase 3 two-browser regression, the Phase 4 five-browser acceptance, and actual gameplay/operational evidence. See [`../Tests/PHASE9_ACCEPTANCE.md`](../Tests/PHASE9_ACCEPTANCE.md).
