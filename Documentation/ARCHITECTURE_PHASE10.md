# Phase 10 — production control plane and launch architecture

**Implementation status: in progress; not production-ready or accepted.** The selected deployment boundary is Supabase Auth + PostgreSQL for identity/persistence, Vercel Functions for the trusted HTTPS control plane and static WebGL hosting, and a separately operated persistent Unity dedicated game-server process for real-time simulation. The backend code never contains project credentials. Production values belong in Vercel/Supabase/server-host environment secrets.

## Important hosting boundary

Vercel can host the web build and request-scoped API functions. It is not the place to run a continuously available Unity headless process with durable match simulation. The real-time NGO game server therefore needs a separate dedicated-server host/provider. Unity's direct support for Multiplay Game Server Hosting ended on 2026-03-31; Unity licensed the hosting software to Rocket Science Group. That change and the available server-hosting contract need a deliberate provider decision before deployment. See [Unity's current Multiplay Hosting notice](https://docs.unity.com/en-us/multiplay-hosting/guides/manage-servers) and [Vercel's backend model](https://vercel.com/docs/frameworks/backend).

Vercel's API is the control plane, not the game loop:

- **Vercel:** static WebGL delivery, short HTTPS API calls, account/profile/match metadata/moderation endpoints, rate-limit enforcement, trusted server callbacks.
- **Supabase Auth:** email/password identity and expiring access JWTs; refresh credentials are rotated by Auth.
- **Supabase Postgres:** compact accounts, match/session metadata, aggregate statistics, results, reports, bans, user block/mute state, audit events, and rate-limit windows. It does not hold a map database or Google tile/imagery content.
- **Separate dedicated game server:** persistent authoritative fixed-tick NGO simulation, reconnect restoration, player admission, match phase reports, snapshots, and results. Only this server uses the server-only Vercel token. It is not the existing player-hosted Relay prototype.

Supabase's publishable key is public and must be protected by least-privilege policies. The Supabase secret/service-role key bypasses RLS and is server-only. The migration denies direct `anon` and `authenticated` table privileges; Unity/WebGL calls Vercel APIs, not the privileged Data API. See [Supabase API key guidance](https://supabase.com/docs/guides/getting-started/api-keys) and [RLS guidance](https://supabase.com/docs/guides/database/postgres/row-level-security).

## Phase 10 data model

`supabase/migrations/202610060001_phase10_core.sql` adds:

- `users` tied to `auth.users`, case-insensitively unique usernames, optional HTTPS avatar URL.
- `statistics` with matches, kills, deaths, and wins.
- `matches`, `match_members`, `match_results`, and `match_events`.
- `sessions` with server-only hashed, one-use 60-second connection tickets, rotating 10-minute reconnect credentials, status, and a size-bounded server-owned reconnect snapshot.
- `reports`, `bans`, `user_blocks`, `user_mutes`, `chat_messages`, and a private atomic rate-limit table.

Only centre/radius and match metadata are stored. No tiles, derived Google content, photogrammetry, mesh data, or global map dataset are stored.

## Match lifecycle and authority

The only allowed directed sequence is:

`CREATING → LOBBY → LOADING → COUNTDOWN → LIVE → FINISHED → CLOSED`

A trusted Vercel/server credential invokes the SQL transition function. User/host endpoints cannot set match state directly. The server can heartbeat, consume connection tickets, rotate reconnect tickets, persist a bounded player snapshot on disconnect, poll host commands, and submit a final result exactly once. Host `END_MATCH` requests an end; the dedicated server remains responsible for the terminal transition/result. Direct client edits to Supabase tables are denied.

The initial match-creation API deliberately returns a `CREATING` record. It does **not** claim a lobby is playable until a dedicated-server allocator and server callback move it to `LOBBY`. Allocation and live NGO hosting are not supplied by Supabase/Vercel.

## Disconnect, reconnect, and elimination

Session state and match-member state represent different concerns. A player's session may be `DISCONNECTED` while the member is still `ELIMINATED`; redeeming a reconnect credential restores connection/snapshot state without changing an eliminated member back to `ACTIVE`. The trusted server marks elimination explicitly, and the SQL guard requires a live match. Reconnect redemption is available only for a member with a disconnected/eliminated record while the match is `LOBBY`, `LOADING`, `COUNTDOWN`, or `LIVE`; tickets are single-use and rotated. After `FINISHED`/`CLOSED`, redemption is denied. There is no respawn path.

`server_save_player_snapshots` accepts a bounded batch of up to 32 per-player state objects (16 KB each) from the trusted game server while the match is active. This is a persistence primitive, not crash recovery by itself: the separate game-server allocator must store/restore the process/world state and rebind network identities. Server adapters still need to call the checkpoint and lifecycle APIs at the configured interval. The Unity/NGO player prefab and current Relay host do not yet consume these credentials or restore a snapshot.

## Auth, account and abuse controls

Vercel proxies signup, signin, and refresh to Supabase Auth, validates player access tokens with Auth's user endpoint, and exposes profile/statistics, match, reporting, relationship, host-action, and chat endpoints. Access to privileged Postgres actions uses a server-only Supabase secret key. Rate limits are atomically persisted in Postgres and fail closed if that service is unavailable; they are applied by IP and authenticated user/identity for authentication, API operations, match creation/join, reports, and chat. A server-held secret is separate from player access/reconnect tokens and is never returned to a browser.

The server must enforce the same ban, kick, lock, player-capacity, and reconnect rules on admission. An abuse score from Phase 8 is evidence for review, not an automatic one-event ban. Reports, blocks, and mutes are moderation inputs; they do not themselves ban a player.

## Files and local checks

- `WebDeploy/Vercel/api/v1/` — Vercel Functions control plane.
- `WebDeploy/Vercel/api/_lib/` — request validation, Supabase/Auth calls, rate limits, safe errors, token boundaries.
- `WebDeploy/Vercel/package.json` — dependency-free Node 20 API tests and runtime declaration.
- `WebDeploy/Vercel/.env.example` — variable names only; no credentials.
- `supabase/migrations/` — reviewed SQL migration.
- `Tests/PHASE10_ACCEPTANCE.md` — deployment and runtime evidence matrix.

Run API unit tests from `WebDeploy/Vercel` with `npm test`. These tests do not execute the migration against a real Supabase project, validate Vercel deployment configuration, or prove Unity/server connectivity. No project URL, key, production account, Supabase database, Vercel deployment, dedicated host, Unity build, or browser acceptance is configured in this workspace.
