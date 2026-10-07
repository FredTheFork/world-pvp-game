# Tests

- `PHASE1_ACCEPTANCE.md`: historical live Google/Cesium acceptance for the original world proof; still **NOT RUN**.
- `PHASE2_ACCEPTANCE.md`: live metre-based arena matrix at representative locations/radii; still **NOT RUN** until exercised in Unity.
- `PHASE3_ACCEPTANCE.md`: outstanding two-tab UGS session/invite/WebGL regression prerequisite; **NOT RUN**.
- `PHASE4_ACCEPTANCE.md`: required five-real-browser movement/spawn/rotation/geospatial/interpolation acceptance; **NOT RUN**.
- `PHASE5_ACCEPTANCE.md`: current first-person movement, terrain, geographic HUD, minimap, locomotion presentation, and five-browser regression checklist; **NOT RUN**.
- `PHASE6_ACCEPTANCE.md`: Cesium streaming, movement-prefetch, desktop-browser performance, map-usage estimates, and Google Cloud cost-control acceptance; **NOT RUN**.
- `PHASE7_ACCEPTANCE.md`: weapon data, server-authoritative firing/damage, death/spectating, match end, Unity and actual-browser acceptance; **NOT RUN**.
- `PHASE9_ACCEPTANCE.md` and `Documentation/ARCHITECTURE_PHASE9.md`: virtual arena planning, independent gameplay-data/collision provenance, safety UI, production readiness, no-respawn, and actual-browser acceptance; **NOT RUN / NOT ACCEPTED**.
- `validate_phase9.py`: Unity-independent static checks for deterministic distributed spawns, the exact safety message, provider/collision separation, dedicated-server fail-closed readiness, and updated spawn acceptance; it is not a Unity compile, licensed-data review, or gameplay test.
- `validate_phase6.py`: Unity-independent static checks for the pinned Cesium streaming API usage and telemetry limitations; it is not a Unity compile, a Google billing test, or browser acceptance.
- `validate_phase7.py`: Unity-independent static checks for data-backed weapon stats, intent-only fire payload, host validation/damage, death/spectator flow, scene wiring, and honest acceptance status; it is not a Unity compile or combat test.
- `validate_phase1.py`: Unity-independent static repository checks for the retained geospatial, session, WebGL, and authoritative movement source invariants. It is not a Unity compile or runtime test.
- `test_webgl_local_server.py`: HTTP smoke tests for local `/join/{code}` fallback, COOP/COEP, and Brotli response headers (`python3 Tests/test_webgl_local_server.py -v`). These test the Python static host only—not Vercel or gameplay.
- `Assets/WorldPvp/Tests/EditMode`: Unity Test Framework tests for WGS84/ENU, arena geometry, deterministic virtual spawn generation, provider-backed safe-spawn checks, stable collision proxies, session parsing/validation, fixed-step network movement, and Phase 7 weapon data/fire intent/aim/spread. Unity compilation and Test Runner execution are still unverified.
