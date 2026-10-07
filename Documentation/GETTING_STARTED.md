# Getting World PvP running

This is the operating map. The phase documents describe individual systems. This file says what has to exist, where it runs, and what to do on the machine that has Unity.

## Unity does not edit GitHub directly

The Unity Editor only reads and writes a folder on disk. There is no mode that opens `FredTheFork/world-pvp-game` on GitHub and saves back to the remote without a local clone.

The correct connection is one local clone:

```text
GitHub  FredTheFork/world-pvp-game
   ^  git pull / git push
   |
local clone   <-- Unity Hub opens this folder (the one that contains Assets, Packages, ProjectSettings)
   ^
Unity Editor edits those files
```

Do not make a second copy for Unity. Do not point Hub at `Assets`. After Unity generates files, commit and push from that same folder. That is the GitHub connection.

Unity Version Control (Plastic) is a different history. Do not turn it on for this repository.

The current fixes are on an `arena/...` working branch (check `git branch --show-current`, or the
branch named on the pull request), not necessarily on `main`. In the folder Unity has open:

```bash
git fetch origin
git checkout <the branch named on the pull request>
git pull origin <same branch>
```

Then return to Unity and let it import. If Unity already has the project open, focus the Editor so it reimports.

## Why there is no `packages.lock`

The file Unity writes is:

```text
Packages/packages-lock.json
```

It is not named `packages.lock`, and it is not listed as an asset in the Project window until it exists. Package Manager does not show it as a package entry. Look on disk, in the clone root, next to `manifest.json`.

Unity creates that file only after dependency resolution succeeds. Installing the Editor is not enough. Opening the project and waiting for the Package Manager progress bar is the step that creates it. If resolution fails, the file is absent and the Console has a Package Manager error. Do not hand-write the lock file. A fabricated lock will not match registry hashes and will not make a failed download succeed.

If resolution fails — for example the modal says `Failed to resolve packages:` with `Cannot read properties of null (reading 'severity')`, or the window says `Error fetching package list offline` — follow [`PACKAGE_RESOLUTION_TROUBLESHOOTING.md`](PACKAGE_RESOLUTION_TROUBLESHOOTING.md) and run `python3 Tools/diagnose_unity_packages.py` before changing anything. That message is a Package Manager UI crash while reporting the real error; the real error is in the Editor log.

Pinned direct versions are in `Packages/manifest.json`. Cesium comes from `https://unity.pkg.cesium.com`. The Unity packages come from Unity's registry. The Editor needs network access to both on the first resolve. `com.cesium.unity` 1.26.0 is published on that Cesium registry.

After the file appears:

```bash
git add Packages/packages-lock.json
git status
git commit -m "Lock Unity package resolution"
git push origin <the branch you are on>
```

Also commit the `.meta` files Unity generates under `Assets/`. Do not commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Build/`, or any real API key.

Check the folder without opening Unity:

```bash
python3 Tools/check_unity_open.py
```

## Architecture that can actually run

Four separate runtimes. Mixing them up is how this project gets called "deployed" before anyone can play.

```text
Browser or Unity Editor
  |  WebGL player, Cesium globe, input, HUD
  |  Google Map Tiles key stays in the player (restricted key, not a secret)
  v
Unity Gaming Services
  Authentication (anonymous, per browser tab)
  Multiplayer Services session + join code
  Relay (packet router only, WebSockets on WebGL)
  v
NGO session host
  Today: one of the players. Fixed-tick movement, hits, damage, death.
  Relay does not make that player a trusted server.
  v
Later, not instead of the above:
  Vercel  — website + short HTTPS API. Not the game loop.
  Supabase — accounts, match records, bans, rate limits. Not the map, not the simulation.
  Dedicated game server — persistent NGO process on its own host. Vercel cannot be that host.
```

### 1. Local world proof — Editor

Needs: Unity `6000.3.24f1` with the WebGL Build Support module, this clone, a successful package resolve, a clean Console, and a Google Map Tiles API key when you want photorealistic tiles.

Does not need: a domain, Supabase, Vercel, or a dedicated server.

The generated scene is not in git yet. After scripts compile, `PhaseOneSceneBuilder` creates `Assets/WorldPvp/Scenes/Phase1_GoogleWorld.unity`. If that fails, use **Tools → World PvP → Phase 9 → Build or Rebuild Virtual Gameplay Scene**. Rebuilding replaces the generated scene.

The tileset stays disabled until a Map Tiles key is supplied. Put it in the HUD field, or in the ignored file `Assets/WorldPvp/StreamingAssets/google-tiles-key.local.txt`. Never commit that file. Places search uses a second key. See `Documentation/GOOGLE_MAP_TILES_SETUP.md`.

Play Mode in the Editor is the local world proof. It is not browser acceptance and it is not multiplayer.

### 2. Browser Relay prototype — "play with others" that this repo can reach

Needs everything in layer 1, plus:

- A Unity Cloud project linked in the Editor (**Edit → Project Settings → Services**), with Authentication and Multiplayer / Relay enabled for that project.
- A WebGL build from **Tools → World PvP → Phase 3 → Build WebGL (Brotli + Threads)**. Output is `Build/WebGL`.
- A static server that sends cross-origin isolation headers. Threaded WebGL will not start from `file://`.

```bash
python3 Tools/serve_webgl_local.py --root Build/WebGL --port 8000
```

Open `http://127.0.0.1:8000/` in two real browser windows. Create a battle in one, join with the opaque `/join/{code}` link in the other. Editor Play Mode does not count as that test.

The session host is a player. That player is the NGO authority for movement and combat. A modified host can cheat. That is a prototype limit, not a bug in Relay.

### 3. Accounts and match records — control plane

`WebDeploy/Vercel` is the HTTPS API. `supabase/migrations/202610060001_phase10_core.sql` is the schema. Neither has been applied to a live project.

Vercel serves the site and short API calls. Supabase Auth issues player tokens. Postgres stores profiles, match metadata, reconnect tickets, and moderation. Unity calls Vercel, not the database. The Supabase secret key and the dedicated-server token stay in Vercel environment variables, never in the Unity project or the browser.

This layer is not wired to the live NGO session yet. Creating a Supabase match record does not start Relay, and a Relay session does not write a Supabase row. Do not treat a green `npm test` as a deployed game. Those tests are local contract checks:

```bash
cd WebDeploy/Vercel && npm test
```

When you do create a development Supabase project, apply the migration only there. Do not point it at a production database. Use `sb_publishable_…` and `sb_secret_…` keys. The secret stays server-side.

Vercel project root, when you deploy, is `WebDeploy/Vercel`. That folder does not yet contain a Unity WebGL build. Measure `Build/WebGL` before choosing a plan: Hobby static uploads are small, and a Cesium WebGL build can exceed them. A custom domain comes after a working `vercel.app` URL, not before.

### 4. Trusted multiplayer — not this repository's first playable target

A dedicated NGO server is a long-running process. Vercel functions are not that process. Unity Multiplay hosting is not the assumed host. A later allocator must start the server, move the match from `CREATING` to `LOBBY`, and call the server-only Vercel endpoints with `WORLD_PVP_SERVER_TOKEN`.

Until that exists, the honest multiplayer target is layer 2: two browsers, UGS Relay, player host.

Vercel Hobby cron is daily. It is not a match-cleanup loop. Do not rely on it for stale live matches.

## Exact Editor gate

1. Install Unity Hub and editor `6000.3.24f1` (changeset `4e7b9b5b6244`), including **WebGL Build Support**. Hub link: `unityhub://6000.3.24f1/4e7b9b5b6244`. If the version is not in the default list, use Hub's archive install. Do not upgrade the project to a newer editor.
2. Hub → Add project from disk → the clone root (contains `Assets`, `Packages`, `ProjectSettings`).
3. Wait until Package Manager is idle. Confirm `Packages/packages-lock.json` exists on disk.
4. Wait until the bottom-right spinner stops. Open **Window → General → Console**. The gate is no red compile errors. The first red error is the one to fix. Do not paste API keys from the Console.
5. **Window → General → Test Runner → EditMode → Run All**.
6. Enter Play Mode only after the generated scene is open. Add a Map Tiles key only when you want tiles. Do not put a key in git.

Inside the Editor, **Tools → World PvP → Project → Show Local Git And Package Status** prints the lock-file path, the git remote, and whether this folder is the clone Unity is editing.

## What is still not "perfect"

No Unity import, EditMode run, WebGL build, live tile load, Relay session, Supabase migration, or Vercel deployment has been executed in the environment that maintains this repository. Static Python and Node checks pass. They do not prove the Editor.

Do not buy a domain or commit credentials to get past the compile gate. Google, Unity Gaming Services, Supabase, and Vercel all have quotas. Nothing here is an unlimited free service.
