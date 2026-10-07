# Phase 3 acceptance checklist — WebGL Battle and two browser tabs

**Status at Phase 3 source implementation: NOT RUN.** No Unity package resolution/import, C# compilation, WebGL build, live Google/Cesium requests, UGS Authentication/session creation, Relay/WSS connection, or two-tab test has been executed in this workspace. **No successful two-tab run is claimed.** Do not mark acceptance passed from static source checks alone.

> Phase 4 replaces Phase 3's position-report player component with the current input-command `NetworkPlayer`. This two-tab test remains an outstanding regression prerequisite for the UGS/session/invite flow. Exercise it using the current Phase 4 code, then continue to the separate five-browser movement gate in [`PHASE4_ACCEPTANCE.md`](PHASE4_ACCEPTANCE.md). Historical `NetworkGeoPlayer` wording below describes the superseded Phase 3 design, not current source.

## Release gate

The release gate passes only after two separate browser tabs open the same opaque `/join/{code}` invite, connect as two members of one UGS session, load exactly the same WGS84 latitude/longitude/ellipsoid altitude and radius, initialize their NGO player states, and enter gameplay. Both tabs must demonstrate the current server-simulated remote capsule following the other tab's input-driven geographic movement. The Relay session host retains authority and is **not cheat-proof** or a dedicated trusted server.

## Prerequisites

- [ ] Unity `6000.3.24f1` is installed with WebGL Build Support; packages resolve from the pinned `Packages/manifest.json` without errors. Unity creates (not hand-authored) `Packages/packages-lock.json`.
- [ ] Unity Editor compile finishes with no C# or assembly-definition errors.
- [ ] The project is linked to the intended Unity Cloud project. Anonymous Unity Authentication and Multiplayer Services Sessions/Relay are enabled/configured for this project.
- [ ] Google Cloud has Map Tiles API, Maps JavaScript API, and Places API (New) enabled; two separate restricted keys are configured, quotas/budget alerts exist, and the Play/Browser referrer restrictions include the intended local or deployment origin.
- [ ] The selected test centre has Google Photorealistic 3D coverage confirmed in the official [visual checker](https://developers.google.com/maps/documentation/javascript/3d/coverage). The local confirmation checkbox is not a Google API result.

## Automated checks (before manual acceptance)

- [ ] Run `python3 Tests/validate_phase1.py -v`; all repository/static checks pass.
- [ ] In Unity Test Runner, run all EditMode tests, including Phase 3 opaque invite parsing and radius/capacity tests.
- [ ] Verify Phase 2 geospatial tests still pass. Its six-location/radius manual matrix remains separately marked **NOT RUN** until observed in Unity (`Tests/PHASE2_ACCEPTANCE.md`).
- [ ] In the Unity Console, verify the web build does not log API keys, invite URLs, or coordinate details. Review any Cesium/API request error without copying full credential-bearing request URLs.

## Local WebGL build and hosting

1. [ ] Run **Tools → World PvP → Phase 3 → Build or Rebuild Scene** once, then resolve all compile errors.
2. [ ] Run **Tools → World PvP → Phase 3 → Build WebGL (Brotli + Threads)**. Confirm the Unity report says success and the output is under `Build/WebGL/`.
3. [ ] Run `python3 Tools/serve_webgl_local.py` from the project root. Open `http://127.0.0.1:8000/` and verify the game loads; it must be served, not opened as `file://`.
4. [ ] Confirm in the browser that `crossOriginIsolated === true`, Unity's Brotli build files load with HTTP 200 and correct `Content-Encoding: br`, Google Maps/Places requests pass CORS/COEP, and the Unity canvas remains responsive.
5. [ ] Deploy only if desired: copy static config with `python3 Tools/prepare_webgl_deployment.py Build/WebGL`, check host size/plan limits, and verify the same route/header behavior on the deployed origin. No deployed URL is currently configured.

## Two-tab session and gameplay acceptance

Use two tabs in the same browser profile first. `sessionStorage` must give them separate anonymous Authentication profiles even though browser key storage is shared for the same origin.

| Observation | Tab A (creator/host) | Tab B (invited client) | Result / evidence |
|---|---|---|---|
| URL once battle is ready | `/join/________` | same `/join/________` | NOT RUN |
| URL contains code only (no centre/radius/query detail) | | | NOT RUN |
| Browser profile / UGS player identity differs | | | NOT RUN |
| UGS Session ID | | | must match; NOT RUN |
| Opaque join code | | | must match; NOT RUN |
| UGS current-player count | | | must reach 2; NOT RUN |
| Relay / NGO state | | | must report started; NOT RUN |
| NetworkPlayer state / first authoritative snapshot | | | each tab; NOT RUN |
| Session latitude (degrees) | | | exact strings/values must match; NOT RUN |
| Session longitude (degrees) | | | exact strings/values must match; NOT RUN |
| Ellipsoid altitude (metres) | | | exact strings/values must match; NOT RUN |
| Arena radius (metres) | | | exact value must match and be `<= 2,000`; NOT RUN |
| Gameplay entry | | | both enter; NOT RUN |
| Move one local capsule | | observe remote capsule | remote position follows geographically; NOT RUN |

Steps:

1. [ ] In Tab A, enter/save both restricted Google keys. Search a place, click a map point, or enter coordinates; choose a supported radius and player capacity.
2. [ ] Open Google's official visual Photorealistic coverage checker, inspect the centre and surrounding area, return to Unity, and tick the manual confirmation.
3. [ ] Click **Preflight and Create Battle**. Wait for centre terrain, coverage/17-point height preflight, authentication, session creation, Relay/NGO startup, NGO player spawn, and the first server-authoritative player snapshot. Any failed terrain or sample must block creation; confirm the exact unavailable-coverage message.
4. [ ] Confirm the host address bar changes, without a reload, to a root route `/join/{opaque-code}`. The invite text field contains only the host's opaque UGS join code; copy it.
5. [ ] Open the copied URL in Tab B. Confirm it is the same route with no coordinates/radius in path/query/fragment. The joiner should auto-join after per-tab authentication/key readiness; if automatic joining is blocked by missing keys, enter/save the same Map Tiles key and press **Join** using the code still in the UI.
6. [ ] Confirm Tab B resolves the code through UGS, creates a distinct UGS player, joins the existing session, loads the session's exact centre/altitude/radius, passes local preflight, and waits for Relay/NGO + its player object/first position. Tab A must show current-player count 2.
7. [ ] Compare both tabs' UGS session ID, code, WGS84 latitude, longitude, ellipsoid altitude, and radius. They must be identical; do not accept rounded screen output if precise values differ.
8. [ ] Click **Enter gameplay** on both. Move Tab A a short distance inside the boundary; observe the colored remote capsule moving in Tab B. Repeat from B to A.
9. [ ] Leave in both tabs. Verify session cleanup, locks/cursor state, and route behavior; note whether the host-created session closes as expected.
10. [ ] Record browser/OS, build target/size, UGS project, Google API console usage, Unity Console errors, network states, and evidence/screenshots below (never capture unredacted browser key URLs or secrets).

## Radius and fail-closed validation matrix

Automated checks validate that common presets are accepted and values above 2,000 m are rejected. Live terrain/coverage preflight must still be observed for representative valid and invalid locations/radii.

| Case | Expected result | Observed result |
|---|---|---|
| 100 m / 250 m / 500 m / 1,000 m / 2,000 m at confirmed covered location | valid input; preflight still required | NOT RUN |
| custom `2,000 m` | accepted maximum | NOT RUN |
| custom `2,000.01 m`, zero, negative, NaN, infinity | rejected before session create/join | NOT RUN |
| Invalid latitude/longitude or invalid numeric coordinate | rejected with location/input error | NOT RUN |
| Centre terrain unavailable / no tile collision | battle not created; unavailable-coverage message | NOT RUN |
| One of the 17 Cesium height samples fails/times out | battle not created/joined; unavailable-coverage message | NOT RUN |
| Manual Google surface-coverage confirmation unchecked | battle not created; unavailable-coverage message | NOT RUN |
| Join code malformed/not found/closed/full | join rejected; no gameplay-ready state | NOT RUN |
| Session metadata schema/radius/mode/capacity invalid | join rejected before gameplay | NOT RUN |

## Trust and release boundary

- UGS Relay routes messages; it is not a dedicated authoritative simulation server.
- The host-authored `MatchSession` properties, manual coverage flag, and session state are not cheat-proof.
- Current Phase 4 movement relays input intent and host-authored ENU state; the Relay session host is still client-controlled and not cheat-proof. The former Phase 3 WGS84 position-report implementation has been removed. No weapon/combat, damage, or dedicated trusted server is present.
- Google browser API keys are public by design. Do not mark them as secrets; restrict APIs/referrers/quotas and configure budgets.
- A static-host deploy may exceed free upload limits; Vercel Hobby also has plan/usage conditions. Relay and Google usage may incur charges beyond current free quotas. Recheck provider terms/pricing.

## Record observations

| Observation | Notes / result |
|---|---|
| Unity version / resolved package lock hash | NOT RUN |
| WebGL build output size / compression | NOT RUN |
| Browser / OS / `crossOriginIsolated` | NOT RUN |
| Hosting URL / COOP-COEP / Brotli headers | NOT RUN |
| Google Maps/Places/Cesium CORS/COEP requests | NOT RUN |
| UGS project/auth profile distinction/session ID | NOT RUN |
| Relay region/protocol/network start | NOT RUN |
| Exact match metadata shown on each tab | NOT RUN |
| NGO player spawn and remote WGS84 avatar movement | NOT RUN |
| Google Maps/Places/Photorealistic tile usage and charges | NOT RUN |
| Known issues / errors / reproduction steps | NOT RUN |
