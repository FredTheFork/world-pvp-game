# World PvP — Phase 10 production integration (in progress)

Start with [`Documentation/GETTING_STARTED.md`](Documentation/GETTING_STARTED.md). Unity does not edit GitHub in place: open this clone, and look for `Packages/packages-lock.json` (not `packages.lock`) only after Package Manager resolution succeeds.

Phase 9's virtual arena/gameplay-data layer remains in progress. Phase 10 adds a first Supabase Auth/Postgres + Vercel control-plane slice for accounts, profiles, statistics, match metadata/lifecycle, reconnect tickets, moderation, and rate limits. This is not accepted or production-ready: no Supabase project, Vercel deployment, external dedicated-server allocator, Unity compile/build, or end-to-end account-to-game identity/reconnect flow exists yet. Phase 7 combat remains present, with the existing UGS Relay player-host flow still a prototype. The arena radius is a **virtual game boundary**; no physical visit or reward is required.

> **Verification status:** The Supabase/Vercel control-plane code and Node tests are present, but the migration has not been applied to a real Supabase project and no Vercel deployment or production secrets are configured. The Unity account client has not been compiled or exercised. Phase 9 still has no concrete independent gameplay-data provider. The existing UGS Relay flow remains player-hosted prototype code; backend lifecycle/reconnect/result endpoints are not yet connected to dedicated-server admission and live NGO state. No Unity import/compilation, EditMode run, WebGL/dedicated build, database migration run, Vercel deployment, or browser gameplay acceptance has run. Phase 1–9 and Phase 10 production acceptance remain **NOT RUN**. See [`Tests/PHASE9_ACCEPTANCE.md`](Tests/PHASE9_ACCEPTANCE.md) and [`Tests/PHASE10_ACCEPTANCE.md`](Tests/PHASE10_ACCEPTANCE.md).

## Phase 9 virtual gameplay world

- The host-selected WGS84 centre and radius configure a deterministic arena plan on both session create and join. Player points are spread within the virtual boundary, margin-checked, individually sampled, and separated from other planned/live players; safe objective-candidate positions are kept in a separate extensible list.
- Production terrain, semantic features, and persistent collision proxies must be supplied through `WorldGameplayDataSource`, independently of the photorealistic renderer. No provider/dataset is bundled or represented as legally approved. The dedicated server disables visual tiles and refuses to start without approved coverage. The local fallback is prototype-only and does **not** verify water, roads, buildings, cliffs, restricted areas, or reliable safe-spawn avoidance.
- The runtime HUD prominently displays: **“This is a virtual game. Stay aware of your surroundings. Do not enter roads, private property, restricted areas or dangerous locations while playing.”** Real-world travel, dangerous locations, and physical visits never grant gameplay rewards.
- Gameplay source provenance, legal review, collision/provider separation, and all outstanding checks are documented in [`Documentation/ARCHITECTURE_PHASE9.md`](Documentation/ARCHITECTURE_PHASE9.md) and [`Tests/PHASE9_ACCEPTANCE.md`](Tests/PHASE9_ACCEPTANCE.md).

## Phase 7 combat

- The `Weapon` runtime component references an editable `WeaponDefinition` ScriptableObject. Weapon ID, fire rate, damage, metre range, spread, magazine, reload duration, and head/torso/limb multipliers are data-configured. The generated prototype definition is created by the Phase 7 scene builder at `Assets/WorldPvp/Configuration/Phase7_PrototypeRifle.asset`; Unity has not yet generated/imported this asset in this workspace.
- A client reports a `FireCommand` containing only tick, ENU origin, aim direction and weapon ID. It contains no victim, claimed hit, damage or health result. The host validates ownership, alive/match state, equipped ID, tick freshness, origin/direction plausibility, fire rate, magazine and reload state before doing its own hitscan and damage.
- Head, torso, left/right arm and left/right leg server hitboxes are host-generated. The weapon asset controls damage scaling; the host alone changes health/ammo/death and publishes hit/death/match events.
- At zero health the host sets `Alive=false`, disables movement and hitboxes, and broadcasts death. There is **no respawn**: eliminated players spectate until match end. The host freezes the completed match and broadcasts the last-survivor or no-survivor result.
- Client hit markers and damage feedback only react to server-confirmed events. The HUD also presents muzzle flash/sound, impact effects, death/kill-feed presentation, health/ammo and spectator controls.
- Prototype defaults live in the generated data asset: 5 shots/s, 34 base damage, 150 m range, 0.45° spread, 30 rounds, 2.1 s reload, and head/torso/limb scales 2.0/1.0/0.65. These are editable starting values, not combat resolver constants.

Controls: **W/A/S/D** move, **mouse** look, **hold left mouse** fire, **R** reload, **Shift** sprint, **C/Ctrl** crouch, **Space** jump, **Tab** cycle spectator targets when eliminated, **F1/Esc** open or close the battle panel.

## Authority and hosting boundary

NGO fixed-tick movement, health, ammo, firing validation, raycasts, damage, death, and match results run on the UGS session host over Relay. Owners still predict/replay their movement inputs; remote players interpolate host snapshots. No client-authored authoritative transforms have been added.

Relay routes traffic; it does not turn a host player into a trusted server. A player-hosted match is not cheat-proof against the host. Phase 8 source adds a dedicated-server bootstrap, server-side validation, configurable 250–500 ms history, 25 m example spatial cells, and review-oriented cumulative violation scoring, but no dedicated deployment has been built or accepted. Hosting and operating a trusted server has a cost and is not promised as free. Streaming telemetry remains client-observed estimate data, not Google billing authority.

Implementation details: [`Documentation/ARCHITECTURE_PHASE7.md`](Documentation/ARCHITECTURE_PHASE7.md), [`Documentation/ARCHITECTURE_PHASE9.md`](Documentation/ARCHITECTURE_PHASE9.md), and the Phase 8 source/acceptance notes. Phase 1–6 architecture and streaming/performance constraints remain in the linked historical documents.

## Phase 10 Supabase + Vercel control plane (in progress)

- `supabase/migrations/202610060001_phase10_core.sql` defines accounts mirrored from Supabase Auth, profiles/avatar URLs, aggregate statistics, matches/members/events/results, server sessions, moderation reports/bans/blocks/mutes, and private atomic rate-limit buckets. SQL/RPCs are source-only and **not applied or database-validated**.
- `WebDeploy/Vercel/api/v1/` contains account/profile/history, match, join ticket, report, relationship, chat, moderation, server lifecycle/heartbeat/session/checkpoint/result/command, and stale-match APIs. The Vercel functions authenticate end users via Supabase and dedicated-server requests via a server-only bearer secret. The Unity account panel can sign up/sign in, edit username/avatar URL, show stored statistics/history, refresh, and sign out when pointed at a deployed API.
- Match records follow `CREATING → LOBBY → LOADING → COUNTDOWN → LIVE → FINISHED → CLOSED`. A trusted server-side lifecycle RPC enforces state transitions; match creation by itself creates only `CREATING` metadata. A dedicated-server allocator/adapter must assign a host and report readiness before a lobby exists.
- Connection tickets are one-use and short-lived; reconnect credentials are hashed in Postgres, rotated, and expire after 10 minutes. Disconnected/eliminated sessions can restore server snapshots while the match is active; eliminated players remain eliminated/spectators. These contracts are not yet called by the Unity/NGO player connection flow, and crash restoration needs a real persistent game-server provider.
- Vercel is the web/control-plane host, **not** a persistent NGO dedicated-server host. Production needs a separate always-on game-server provider and allocator, private server networking, monitoring, and a tested crash/recovery plan. Provider availability, deployment costs and scale limits must be assessed; no unlimited/free hosting is promised.
- Configure Supabase secret/service-role and dedicated-server credentials only as Vercel server-side environment variables. Never put them in Unity, browser JavaScript, static build output, or public env vars. Use Supabase publishable/anon credentials only where needed by clients; the Unity account client does not need one because its calls go through Vercel.
- Rate limits are database-backed and cover user/API actions plus hashed IP buckets for signup/login/join paths. Configure an adequate Vercel Cron plan or a separate scheduler for stale-match cleanup; reconciliation at sub-minute timescales is not provided by a daily Hobby schedule.
- From `WebDeploy/Vercel`, run `npm test`. Passing Node tests are local unit/static contract checks only; they do not validate SQL, Vercel routing, service credentials, browser acceptance, or a live dedicated match. Full deployment and launch gates are in [`Tests/PHASE10_ACCEPTANCE.md`](Tests/PHASE10_ACCEPTANCE.md); architecture notes are in [`Documentation/ARCHITECTURE_PHASE10.md`](Documentation/ARCHITECTURE_PHASE10.md).

## Project setup

1. Open with the pinned Unity **`6000.3.24f1`** editor and install WebGL Build Support.
2. Resolve the versions in `Packages/manifest.json`. Phases 7–10 make no package upgrades. Let Unity create/update `Packages/packages-lock.json` (that exact name, not `packages.lock`); none has been fabricated. If Package Manager fails to resolve (`Failed to resolve packages: Cannot read properties of null (reading 'severity')`, `Error fetching package list offline`), that is an environment failure, not a source error: run `python3 Tools/diagnose_unity_packages.py` and follow [`Documentation/PACKAGE_RESOLUTION_TROUBLESHOOTING.md`](Documentation/PACKAGE_RESOLUTION_TROUBLESHOOTING.md) — every `CS0234`/`CS0246` is a consequence until the packages load. See [`Documentation/GETTING_STARTED.md`](Documentation/GETTING_STARTED.md).
3. Fix all import, assembly-definition and C# compilation errors, then run Unity EditMode tests. Neither task has been run here.
4. Link a Unity Cloud project and configure anonymous Unity Authentication, Multiplayer Services Sessions, and Relay. The session host runs server-side NGO authority in the player client; Relay only routes packets.
5. Use **Tools → World PvP → Phase 9 → Build or Rebuild Virtual Gameplay Scene** (or the Phase 7/Phase 10 aliases). The builder wires the combat player prefab, gameplay-data/collision layer, virtual arena planner, match and safety HUDs. The local build explicitly enables prototype-only visual-tile fallback; production must assign an approved independent provider. Rebuilding replaces the generated arena scene; back up any custom edits first.
6. Create/tune a weapon with **Assets → Create → World PvP → Phase 7 → Weapon Definition**, assign a valid definition to the player prefab's `Weapon`, and verify on the host. Unity asset generation and prefab validation have not been run in this workspace.

Pinned packages and rationale: [`Documentation/DEPENDENCIES.md`](Documentation/DEPENDENCIES.md). Google key setup and restrictions: [`Documentation/GOOGLE_MAP_TILES_SETUP.md`](Documentation/GOOGLE_MAP_TILES_SETUP.md).

## Google, Unity services, and cost limits

- Google Places search uses a **separate API key** from Google Map Tiles. Restrict browser-visible keys to required APIs/referrers and set quotas/alerts; a browser key is not a secret.
- Photorealistic coverage confirmation is manual against Google's visual coverage checker; this project does not claim a machine-readable Google photorealistic coverage endpoint.
- Google, Unity Gaming Services, and static hosting have quotas and plan limits. Do not promise unlimited free usage. Map-usage telemetry here is self-reported client observation, not Google billable-event truth; the Phase 6 cost/usage document explains the reconciliation and launch gate.
- Phase 8 includes source for a dedicated server, but no server is deployed or verified. A trusted dedicated server changes the hosting/cost model; do not promise zero-cost or unlimited service.

## Verification workflow

As of 2026-10-06, Unity-independent Phase 1, Phase 6, Phase 7, and Phase 9 static suites pass (20, 8, 10, and 7 tests respectively); the local Python WebGL host smoke suite passes 3 tests; and the Vercel Node unit/static contract suite passes 8 tests. These inspect source, pure validation logic, or local HTTP hosting only. They do not prove C# compilation, Unity Test Runner, SQL migration validity, a live Supabase database, Vercel deployment, approved data licensing, Cesium collision, Relay, or gameplay.

Unity-independent source checks:

```bash
python3 Tests/validate_phase1.py -v
python3 Tests/validate_phase6.py -v
python3 Tests/validate_phase7.py -v
python3 Tests/validate_phase9.py -v
python3 Tests/test_webgl_local_server.py -v
cd WebDeploy/Vercel && npm test
find WebDeploy/Vercel/api -type f -name '*.js' -print0 | xargs -0 -n1 node --check
```

Then in Unity: **Window → General → Test Runner → EditMode → Run All** (includes the Phase 7 combat tests and new `GeographicArenaGeneratorTests` / `GameplayCollisionWorldTests`; none has been run). Build the configured WebGL and dedicated-server targets, serve browser builds with required cross-origin-isolation/Brotli headers, and test **actual browser gameplay**. Native/Editor clients do not count as browser acceptance.

- Phase 1 and Phase 2: live WGS84/Cesium/arena acceptance pending.
- Phase 3: two-browser-tab UGS Sessions/Relay/invite regression pending.
- Phase 4: five-browser host-authoritative movement/interpolation acceptance pending.
- Phase 5: terrain collision/locomotion/HUD/minimap browser acceptance pending.
- Phase 6: streaming, performance, telemetry and Google billing reconciliation pending. Cache/request warnings are not hard caps.
- Phase 7: Unity Test Runner, compile, WebGL and multiplayer combat acceptance pending.
- Phase 8: dedicated-server build/deployment, authority, history, spatial review and operations acceptance pending.
- Phase 9: independent licensed data provider, legal review, Unity compile/tests, production collision, safety and actual-browser acceptance pending.

See [`Tests/PHASE9_ACCEPTANCE.md`](Tests/PHASE9_ACCEPTANCE.md) for the Phase 9 matrix, [`Tests/PHASE4_ACCEPTANCE.md`](Tests/PHASE4_ACCEPTANCE.md) for the revised spawn expectation, and the earlier phase acceptance checklists. Passing static Python checks is not evidence of Unity compilation, legal data approval, live Cesium collision, provider usage accuracy, or gameplay.
