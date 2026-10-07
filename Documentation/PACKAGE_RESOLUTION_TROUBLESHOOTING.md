# Package resolution failure: “Cannot read properties of null (reading 'severity')”

This page is about the failure that stops this project from opening normally and forces Unity into Safe Mode. It is an environment problem, not a gameplay-code problem.

## What the two messages actually mean

```text
Failed to resolve packages: Cannot read properties of null (reading 'severity'). No packages loaded.
[Package Manager Window] Error fetching package list offline.
Cannot read properties of null (reading 'severity')
```

1. **`No packages loaded` is the real failure.** Unity Package Manager aborted before it finished computing the package graph, so nothing was downloaded or loaded. `Library/PackageCache` stays empty.
2. **Every `CS0234` / `CS0246` after that is a consequence.** With no packages loaded there is no `Unity.Netcode`, no `CesiumForUnity`, no `Unity.Mathematics`, no `UnityEngine.InputSystem`, no `Unity.Services.*`, no `Unity.Collections.FixedString64Bytes`. That is one failure, not a hundred. Do not try to fix the C# errors while packages are missing — they disappear together.
3. **`Cannot read properties of null (reading 'severity')` is a bug in the error-reporting path.** The Package Manager window crashed while trying to display the resolution failure (there was no diagnostic object to read a `severity` field from). It hides the real error. It is not itself the cause and there is nothing to fix in this repository for it.
4. **`Error fetching package list offline`** means the Editor could not reach a registry, so the window fell back to offline mode.

The real cause — an unreachable registry, a rejected TLS certificate, a signed-out/offline Hub, a stale cache, or an Editor version that cannot see a pinned package — is written to the Editor log and `upm.log`.

## Step 0 — read the real error

```bash
python3 Tools/diagnose_unity_packages.py
```

Windows launcher if `python3` is not on PATH:

```powershell
py -3 Tools\diagnose_unity_packages.py
```

The script reads this clone, finds the Editor/package logs, extracts the actual resolution error, probes `packages.unity.com`, `download.packages.unity.com`, `unity.pkg.cesium.com`, `github.com` and `objects.githubusercontent.com`, and prints the ordered fix list. Nothing is modified unless you pass `--use-cesium-tarball` or `--restore`.

Finding the log by hand:

| OS | Log |
| --- | --- |
| Windows | `%LOCALAPPDATA%\Unity\Editor\Editor.log` and `upm.log` |
| macOS | `~/Library/Logs/Unity/Editor.log` and `upm.log` |
| Linux | `~/.config/unity3d/Editor.log` and `upm.log` |

Search for `An error occurred while resolving packages:` and the `Project has invalid dependencies:` lines directly beneath it. Those name the exact blocker. Increase `Preferences → General → Package Manager Log Level` to **Debug** and reopen if the log is vague.

## Step 1 — confirm the Editor version

This project is pinned to Unity **6000.3.24f1** (changeset `4e7b9b5b6244`, released 2026-09-10). Open it only with that build:

```text
unityhub://6000.3.24f1/4e7b9b5b6244
```

Package versions resolve against the **Editor's compatible set**. An older or newer Editor can hide a pinned version, and `pinnedPackages` explicitly forbids substituting a different one — that combination produces exactly this failure. If `Library/EditorInstance.json` shows another version, that is the first thing to fix, not the manifest.

## Step 2 — fix connectivity (this is what `offline` is telling you)

Work through these in order:

1. **Unity Hub**: signed in (not “work offline”), account e-mail verified, then fully quit and restart Hub. A stale Hub session is a known cause of package-manager “offline” errors.
2. **OS network status**: Unity asks the operating system whether the machine is online. On Windows, the “Network List Service” must be running and the network icon must not show “no internet”. A wrong system clock also breaks TLS and looks like being offline.
3. **Firewall / proxy / VPN**: allow TCP 443 to `packages.unity.com`, `download.packages.unity.com`, `unity.pkg.cesium.com`, `github.com`, `objects.githubusercontent.com`. The last two matter because Cesium’s registry serves its package tarball as a redirect to a GitHub release asset.
4. **TLS interception** is a documented Cesium failure mode: the log shows `self signed certificate in certificate chain` or `unable to get local issuer certificate` for `unity.pkg.cesium.com`. Install the proxy’s root certificate for the Editor or allow-list the host. Until this is fixed, Cesium cannot resolve regardless of anything in the repository.

## Step 3 — clear the package caches, then reopen

With the Editor **closed**:

```text
delete  <clone>\Library\PackageCache
delete  <clone>\Temp
delete  %LOCALAPPDATA%\Unity\cache        (Windows)
        ~/Library/Caches/unity3d          (macOS)
        ~/.config/unity3d/cache           (Linux)
```

The global cache is rebuilt on demand; deleting it only costs download time. Reopen the clone with 6000.3.24f1 and let Package Manager finish. Watch the Console for the **first** package error, not the C# errors.

## Step 4 — if only the Cesium registry is blocked

`com.cesium.unity` is the only package from a third-party registry (`https://unity.pkg.cesium.com`). If `packages.unity.com` works and `unity.pkg.cesium.com` does not, install Cesium from its published tarball instead:

1. Download (about 596 MB):
   `https://github.com/CesiumGS/cesium-unity/releases/download/v1.26.0/com.cesium.unity-1.26.0.tgz`
   Registry shasum for 1.26.0: `D454711FBE38ED70A4A044D50941C76DB43009E1`
2. Point the manifest at the local file (writes `Packages/manifest.json.bak` first):

   ```bash
   python3 Tools/diagnose_unity_packages.py --use-cesium-tarball <path to com.cesium.unity-1.26.0.tgz>
   ```

   Or do it by hand: remove the `com.cesium.unity` scoped-registry entry, then use **Window → Package Manager → + → Install package from tarball**.
3. Reopen the project. Revert later, once the registry is reachable:

   ```bash
   python3 Tools/diagnose_unity_packages.py --restore
   ```

The tarball path is a workaround for a blocked registry. The pinned registry install remains the normal path, and `Tests/validate_phase1.py` checks for the registry pin, so restore the manifest afterwards.

## Why the pinned versions are not the problem

Verified against the live registries on 2026-10-07:

- `com.cesium.unity` **1.26.0** is published on `unity.pkg.cesium.com` (released 2026-10-01).
- Unity registry pins exist: `com.unity.netcode.gameobjects` 2.13.3, `com.unity.transport` 2.7.4, `com.unity.mathematics` 1.3.2, `com.unity.inputsystem` 1.19.0, `com.unity.splines` 2.9.1, `com.unity.test-framework` 1.4.6, `com.unity.services.authentication` 3.8.0, `com.unity.services.multiplayer` 2.3.3.
- `com.unity.ugui` **2.0.0** is the built-in Unity 6 package (the registry only carries the deprecated `3.0.0-exp.x` line), so that pin is correct as written.
- `com.unity.render-pipelines.*` / `com.unity.shadergraph` **17.3.0** is the Unity 6.3 SRP line.
- The Editor pin `6000.3.24f1` exists and matches `ProjectSettings/ProjectVersion.txt`.

That means an unreachable registry / offline Editor is far more likely than a bad version. If your log names a specific package as invalid, that named package is the one to change — say so and keep the rest pinned.

## Code fixes that were needed anyway

These were genuine source bugs independent of packages, fixed on this branch:

| File | Problem | Fix |
| --- | --- | --- |
| `Assets/WorldPvp/Runtime/Geospatial/LocalPosition.cs` | `CS0102`: the type declared both a property `IsFinite` and a method `IsFinite(double)` | private helper renamed to `IsFiniteValue`; the public `IsFinite` property is unchanged |
| `Assets/WorldPvp/Runtime/Battles/NetworkPlayer.cs` | `CS0246` for `PhaseOneWorldSettings` (declared in `WorldPvp.Phase1.Configuration`) | added `using WorldPvp.Phase1.Configuration;` |
| `Assets/WorldPvp/Runtime/Combat/LagCompensationHistory.cs` | `CS0246` for `NetworkPlayerSnapshot` / `NetworkPlayerSimulation` (declared in `WorldPvp.Phase1.Battles`) | added `using WorldPvp.Phase1.Battles;` |
| `Assets/WorldPvp/Runtime/Combat/LagCompensationMath.cs` | `CS0246` for `NetworkPlayerSnapshot` | added `using WorldPvp.Phase1.Battles;` |

Everything else in the pasted error wall is the missing-packages cascade.

## Done looks like

1. `Packages/packages-lock.json` exists on disk (it does not exist until a resolve succeeds).
2. The Console has no red errors; Safe Mode is no longer offered.
3. Commit the lock file and the `.meta` files Unity generates:

   ```bash
   git add Packages/packages-lock.json
   git status
   ```

4. Then run **Window → General → Test Runner → EditMode → Run All**.

## Do not

- Hand-write or fabricate `Packages/packages-lock.json`.
- Delete or regenerate `Packages/manifest.json` to “reset” the project — it is the reviewed pin set, and the failure is not in it.
- Upgrade the Editor to “fix” this; the project is pinned on purpose.
