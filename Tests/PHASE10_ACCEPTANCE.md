# Phase 10 acceptance — production, scale, and launch

**Status: NOT ACCEPTED / NOT RUN.** Repository source tests do not prove a deployed Supabase/Vercel system, Unity compilation, authentication flows, a live dedicated game server, restore after process loss, security review, or browser gameplay.

## Current implementation boundary

The repository now contains a first control-plane slice for the selected Supabase Auth/Postgres + Vercel approach: a versioned SQL migration, API handlers, account/profile display, statistics/result tables, lifecycle constraints, one-use connection tickets, rotating 10-minute reconnect credentials, report/block/mute/host-action/moderation APIs, server callbacks, persistent rate-limit buckets, stale-match reconciliation, and automated Node tests.

The existing UGS Relay player-host flow is still a prototype. It is not bound to Supabase player IDs, does not issue the new tickets, does not persist match results to this database, and does not provide production reconnect restoration. The new match API returns `CREATING` and waits for a trusted external dedicated-server allocator to move it to `LOBBY`. A live allocation adapter, dedicated-server connection approval/ticket consumption, session/UGS identifier mapping, server snapshot checkpoints, match lifecycle callbacks, and reconnect restoration in the NGO player prefab remain implementation work. The backend endpoint contracts are a control-plane foundation, not a claim that these paths already work end-to-end.

Vercel hosts the static browser build and request-scoped HTTPS functions. It is not the long-running Unity dedicated-server host. A separate persistent game-server provider and allocation system are required. As of this project's date, Unity's direct support for Multiplay Game Server Hosting ended 2026-03-31; provider selection/terms/cost need review before deployment.

## 10.1 Accounts and profiles

- [ ] Create a Supabase project and apply `supabase/migrations/202610060001_phase10_core.sql` to a disposable staging project first.
- [ ] Configure Supabase Auth signup, email confirmation, password policy, redirect allowlist, and production rate/CAPTCHA controls.
- [ ] Deploy Vercel API and configure only server-side Supabase secret key, rate-limit HMAC secret, public origin, moderator allowlist, dedicated-server token, and cron token.
- [ ] In an actual browser: sign up, confirm email, sign in, refresh, sign out, and sign back in from a second browser/device.
- [ ] Validate unique usernames, allowed avatar URLs, profile edits, and persistence after reload.
- [ ] Confirm refresh-token rotation and that access/reconnect tokens expire; verify secrets never appear in bundle, HTML, browser storage, network responses, or logs.
- [ ] Implement and prove identity mapping from Supabase user ID to a server-authenticated NGO player. The prototype Relay client identity does not establish this mapping.

## 10.2 Persistent data and lifecycle

- [ ] Apply and inspect the migration in staging; run SQL/linter/RLS checks against a real Supabase instance.
- [ ] Test a direct browser publishable-key request against every table and RPC: it must be denied except approved Auth actions.
- [ ] Verify backend-only profile/statistics reads, database constraints, privacy retention, backup and restore procedures.
- [ ] Create a match; confirm it remains `CREATING` until trusted server allocation and readiness move it to `LOBBY`.
- [ ] Exercise every valid transition and every invalid/skip/backward transition using concurrent server calls.
- [ ] Move `LOBBY → LOADING → COUNTDOWN → LIVE → FINISHED → CLOSED`; prove each transition is server-controlled and audited.
- [ ] Record results twice and confirm match results and aggregate statistics increment only once.
- [ ] Confirm no endpoint or table stores map tiles, Google-derived geometry, photogrammetry, or a global map database.

## 10.3 Disconnect, reconnect, and recovery

- [ ] Implement client ticket acquisition, server ticket consumption, and connection approval before deploying the new invite code flow.
- [ ] Test player network loss, tab refresh, browser close/reopen within the 10-minute reconnect window, and token replay; tickets must be single-use and match-scoped.
- [ ] Test reconnect while alive and while eliminated. Reconnected eliminated players must remain spectators; no path may respawn them.
- [ ] Test player-host disconnect only in the old prototype; production matches must not depend on a player host.
- [ ] Persist server-authoritative player snapshots on the chosen checkpoint interval and verify field size/schema and privacy.
- [ ] Kill/restart the dedicated process. Verify the allocator can reassign it, restore the latest snapshot, resume the same match, or close it safely without duplicate results.
- [ ] Test network partition/recovery and delayed/duplicated requests.
- [ ] Configure and verify the stale-match scheduler. Vercel Hobby Cron is daily-only; minute-level server health reconciliation requires a plan/provider that supports that frequency or a separate scheduler. No schedule has been enabled in the repository.

## 10.4 Moderation and abuse

- [ ] Exercise report creation, moderator review/resolution, timed ban/lift, block/unblock, mute/unmute, host kick, lobby lock/unlock, maximum-player changes, and end-match request.
- [ ] Verify the dedicated server receives kick/end commands and denies banned/kicked accounts at admission/reconnect.
- [ ] Test per-IP and per-user limits for authentication, match creation, join, chat, API operations, and reports; verify limits fail closed if the database limiter is unavailable.
- [ ] Test invalid/oversized JSON, malformed IDs, unauthorized Origin, expired access tokens, forged server token, replayed connection/reconnect tickets, SQL error responses, and request floods.
- [ ] Confirm violations remain review evidence; a single movement/fire anomaly never automatically bans.
- [ ] Complete privacy, safety, legal, incident response, retention, moderation staffing, and abuse appeal policy reviews.

## Automated repository checks

From `WebDeploy/Vercel`:

```bash
npm test
```

The Node tests cover pure validation/lifecycle/security contracts only. They do not execute Vercel Functions on the platform or apply the migration. Unity EditMode, WebGL, dedicated-server builds, and actual browser instances remain required.

## Launch gates

Do not call Phase 10 production-ready until a staging Supabase project, Vercel deployment, distinct server hosting/allocation system, verified Supabase-authenticated client, server connection ticket flow, result persistence, reconnect/recovery, monitoring/alerts, and the real-browser acceptance matrix have all passed and evidence has been recorded.
