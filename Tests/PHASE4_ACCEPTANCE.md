# Phase 4 acceptance — authoritative movement and five-browser gameplay

> **Status at Phase 4 source implementation: NOT RUN.** No Unity compile, EditMode run, WebGL build, live Google/Cesium request, UGS/Relay connection, or browser gameplay test has happened in this workspace. This checklist is an acceptance plan, not evidence of success.
>
> **Regression prerequisite:** the Phase 3 two-tab session-flow acceptance is also still **NOT RUN**. Complete its remaining session/invite/metadata rows while using the current Phase 4 `NetworkPlayer` path, then complete the five-instance gate below. Do not count Unity Editor/native clients as browser acceptance.

## Acceptance target

Run one actual browser WebGL build in **five separate browser instances** (five tabs in separate top-level browsing contexts is acceptable for the local first pass). One instance creates the UGS session; the other four join by its opaque `/join/{code}` invite. All five must reach the same session, see their own player plus exactly four distinct remote players, and observe correct spawn, geographic placement, movement and facing rotation. Players must not be duplicated or disappear during the observation period or a Cesium render-origin shift. Movement must follow the host's server-authored snapshots, not a client-submitted position.

Phase 4 uses a client-hosted Relay session. The Relay host controls NGO server authority, so this is not a trusted dedicated server or cheat-proof production authority.

## Preconditions and automated checks

- [ ] Install Unity `6000.3.24f1` with WebGL Build Support. Resolve the pinned packages from `Packages/manifest.json`; do not fabricate `packages-lock.json`.
- [ ] Link the project to the intended Unity Cloud project. Configure anonymous Authentication, Multiplayer Services Sessions, and Relay. Confirm relevant current Unity quotas/pricing before running multiple instances.
- [ ] Configure distinct restricted Google Map Tiles and Maps JavaScript/Places keys. Check API enablement, referrers, quotas, billing alerts, and the live Google terms/pricing. Google browser keys are public to a determined client.
- [ ] Choose a test area in Google's official visual Photorealistic 3D Maps coverage checker; manual confirmation remains a UI assertion, not a machine-readable Google coverage result.
- [ ] Run `python3 Tests/validate_phase1.py -v` and `python3 Tests/test_webgl_local_server.py -v`.
- [ ] In Unity, resolve all compile/import errors and run **all EditMode tests**, including `NetworkPlayerSimulationTests.cs` and the Phase 1/2/3 suites.
- [ ] Rebuild from **Tools → World PvP → Phase 4 → Build or Rebuild Scene**. Inspect the generated player prefab: it must have `NetworkObject` + `NetworkPlayer`, not the removed owner-position-report component. Confirm NGO tick rate is 30.
- [ ] Build WebGL with **Tools → World PvP → Phase 3 → Build WebGL (Brotli + Threads)**; no Phase 4-specific hosting backend is added.
- [ ] Serve the build over HTTP(S) with required COOP/COEP/Brotli headers. The included local server is static hosting only; Vercel or a similar static host may be used if file/account limits allow.
- [ ] Verify `crossOriginIsolated === true`, no missing Unity/Google assets, no console exceptions, no exposed credential URLs, and UTP WebSockets are configured before connecting.

## Five-instance session and entity-count matrix

Record the real browser/version, OS, hardware, hosting origin, UGS project/region, and test timestamp. Redact keys and any credential-bearing request URLs.

| Check | Browser 1 (creator/host) | Browser 2 | Browser 3 | Browser 4 | Browser 5 | Result |
|---|---|---|---|---|---|---|
| Separate top-level browser instance; unique anonymous profile | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| Invite route contains opaque join code only | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| UGS session ID matches all other instances | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| NGO/Relay network reports started | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| UGS/session player count reaches exactly 5 | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| Local authoritative/predicted player state initialized | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| Four distinct remote player IDs / colored avatars; display-name state uniquely associated | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| No duplicate local or remote avatar | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| No remote player vanishes during observation | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| Session centre, altitude and radius match exactly | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN |

## Movement, reconciliation, interpolation, and geospatial checks

| Observation | Expected | Result / evidence |
|---|---|---|
| Each browser's spawn | Every player, including the host, receives a unique deterministic safe ENU point distributed inside the exact virtual arena. Points pass the configured terrain/slope/feature checks and minimum separation; no player is assigned the arena centre by default. Production requires approved independent gameplay data; if the explicitly labelled prototype tile fallback is used, water/road/building/cliff/restricted-area avoidance is not verified. | NOT RUN |
| Geographic agreement | WGS84 player placements correspond across all five clients; changing Cesium render origin does not change the match ENU state or visibly teleport players | NOT RUN |
| Move forward/back/strafe | Other clients see the correct player move in the expected direction; no client sends a position/transform field | NOT RUN |
| Sprint | Other clients see a higher but bounded authoritative speed | NOT RUN |
| Jump/fall/land | Host snapshot shows jump/fall/grounded transitions; no client-declared grounded result | NOT RUN |
| Look/rotation | Other clients see each player's facing marker rotate in the same direction and wrap smoothly through north/zero yaw | NOT RUN |
| Render-rate variation | Movement remains approximately the same at different render FPS; simulation/command step remains 30 Hz | NOT RUN |
| Prediction/ack/replay | Owner diagnostics show input tick increasing, acknowledgements catching up, and the pending list draining under normal network conditions; no persistent large correction | NOT RUN |
| Remote rendering | Remote motion interpolates between snapshots; it does not snap on every network update; any extrapolation remains bounded | NOT RUN |
| Arena boundary | Host simulation prevents crossing outside the configured metre-based gameplay radius (with the player inset) | NOT RUN |
| Late join | A fifth player can join after the other four; all five see one representation of each joined player and no already-connected player disappears | NOT RUN |
| Leave/rejoin | One browser leaves and (if session rules permit) rejoins; counts and remote avatars clean up once, without duplicates | NOT RUN |
| Fire input bit | Left click may be sent/recorded as future-facing input only. It does not fire, damage, or alter health; **this acceptance does not implement firing** | NOT RUN |

## Suggested execution sequence

1. [ ] Start the static host and open Browser 1. Enter/save both browser-restricted Google keys. Select a confirmed covered location and supported arena radius, then create a battle. Wait for world readiness, preflight, Authentication, UGS session, Relay/NGO start, and the host player snapshot.
2. [ ] Copy the opaque invite. Open it in Browser 2. Confirm it has a unique tab-scoped anonymous profile and resolves the same UGS session. Wait until the exact session centre/radius is loaded and the player state is initialized.
3. [ ] Repeat with Browsers 3–5. Do not proceed until the UGS/session count reaches five and every browser reports a started network and local spawn readiness.
4. [ ] Enter gameplay in all instances. Verify all five clients render four remote IDs, then test one player at a time: forward, reverse, strafe, sprint, jump, turn, and stop. Observe the same actor in each other browser.
5. [ ] Compare the HUD's 30 Hz tick/ack/pending diagnostics while moving. Capture a short video or screenshots that include each client, the session ID/count, and player state; redact keys and private request data.
6. [ ] Observe idle players and active movement for at least five minutes. Record every duplicate, snap, freeze, missing player, input backlog, browser tab suspension, relay disconnect, terrain issue, or render-origin discontinuity.
7. [ ] Trigger a Cesium origin shift safely (or move far enough for the configured threshold if practical). Confirm player geographic placement is stable. Avoid changing the session centre or bypassing the arena check.
8. [ ] Leave clients one at a time; confirm remote representations disappear only for the player that left. Rejoin if service/session capacity allows.
9. [ ] Compare provider usage/quotas and record resource/hosting cost. Stop the local static server after test completion.

## Acceptance decision

| Evidence | Record |
|---|---|
| Phase 3 two-tab regression completed | NOT RUN |
| Unity version / package lock hash | NOT RUN |
| EditMode suite result | NOT RUN |
| WebGL build result and size | NOT RUN |
| Browser/OS versions and host hardware | NOT RUN |
| Static hosting origin / COOP-COEP / Brotli result | NOT RUN |
| UGS session ID / Relay region / protocol | NOT RUN |
| Unique player IDs and five-client count | NOT RUN |
| Four remote avatars in each of five browsers | NOT RUN |
| Correct spawn and geographic placement | NOT RUN |
| Input tick / acknowledgement / pending evidence | NOT RUN |
| Movement, rotation, jump and interpolation evidence | NOT RUN |
| Cesium origin-shift result | NOT RUN |
| Console/network errors and reproduction notes | NOT RUN |
| Google / UGS usage and any charges | NOT RUN |
| Decision | **NOT ACCEPTED until every required observation is completed and recorded** |
