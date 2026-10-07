# Phase 7 — Authoritative Combat System

Phase 7 adds an asset-configured hitscan weapon and client presentation to the existing Cesium/ENU arena and host-authoritative NGO movement. It does not replace the Phase 1/2 geospatial model, the Phase 3 UGS Sessions/Relay transport, the Phase 4 input-authority boundary, or Phase 5/6 movement and streaming systems.

## Authority boundary

| Data / decision | Authority |
|---|---|
| Movement/look intent and `FirePressed` input edge | Owning client reports intent |
| `FireCommand` tick, ENU origin, aim direction, weapon ID | Owning client reports intent; none of these is a claimed hit |
| Player spawn/position/yaw/pitch, health, alive/dead state, ammo, reload completion, accepted fire rate, hitboxes, shot spread, raycast, target, hit zone, damage, kill and match result | Session host's NGO server simulation |
| Hit marker, damage indicator, confirmed shot/impact presentation, kill feed and match-result UI | Client presentation of host-broadcast `CombatEvent`s and replicated snapshots |

**Invariant:** the client reports “I fired”; only the server decides whether the shot hit. `FireCommand` has no victim/target field, no hit flag, no damage, and no claimed health result. Clients cannot submit transforms or hit outcomes. `NetworkPlayer` accepts the command only through an ownership-required ServerRpc, cross-checks the sender against the NGO owner, and resolves combat on the server.

This is host authority over Relay, not a trusted dedicated server. A player-host can still cheat against the match. Dedicated authority, lag-compensated historical hit validation, and protection from a malicious host are outside this zero-cost client-hosted architecture.

## Weapon data

`WeaponDefinition` is a `ScriptableObject` at `Assets/WorldPvp/Runtime/Combat/WeaponDefinition.cs`. It stores weapon ID, display name, fire rate, base damage, range in metres, spread, magazine capacity, reload duration, head/torso/limb multipliers, and optional fire/reload clips. `Weapon` is the per-player runtime magazine/cooldown component. The server loads stats from that asset; the client's local fire-rate pacing is only a UX optimization.

The Phase 7 scene builder creates `Assets/WorldPvp/Configuration/Phase7_PrototypeRifle.asset` if absent and attaches its reference to the generated NGO player prefab. The generated starting values are deliberately editable asset defaults, not constants used by the combat resolver:

| Property | Prototype asset default |
|---|---:|
| Fire rate | 5 shots/second |
| Base damage | 34 |
| Range | 150 m |
| Spread cone | 0.45° |
| Magazine | 30 rounds |
| Reload | 2.1 s |
| Head / torso / limb multiplier | 2.0 / 1.0 / 0.65 |

The asset rejects unsafe IDs and out-of-range stats, and enforces head damage greater than torso damage and limb damage below torso damage. Make or tune weapons with **Assets → Create → World PvP → Phase 7 → Weapon Definition**; assign a valid definition to the player prefab's `Weapon` component. The scene builder preserves an existing valid prefab definition, and assigns the prototype asset if the reference is missing or invalid.

## Shot request and validation

`FireCommand` in `CombatModels.cs` serializes exactly the input tick, three arena-local ENU coordinates for the shot origin (metres), three direction components, and the weapon ID. The owner submits it alongside the same tick's `NetworkPlayerInputCommand` with `FirePressed=true`. There is no client timestamp or historical hit claim. The host uses its monotonic realtime for cooldown/reload deadlines and the sequenced fixed-simulation input tick for freshness/replay checks.

The host rejects a request unless all of these checks pass:

1. The NetworkObject is spawned/initialized, the sender is its owner, and the player is alive, has health, is not match-finished, and the host match is active.
2. The equipped server-side `WeaponDefinition` is valid and the submitted weapon ID exactly matches it.
3. The tick is nonzero, newer than the last processed input tick, no more than eight ticks ahead, not already pending, and matched to that tick's fire input. Stale and duplicate commands are rejected.
4. The origin is finite and within 1.25 m of the server-simulated player eye position for the processed tick.
5. The direction is finite and approximately unit length, and within 12° of the host-simulated yaw/pitch. The resolver then uses the host's aim direction, not a client-selected hit direction.
6. The server weapon's rate deadline has elapsed, its magazine has a round, and it is not reloading.

Only then does the host consume a round, derive deterministic spread from owner/tick/weapon ID, transform the ENU ray into the current Cesium Unity render frame, and call the physics raycast up to the asset-configured range. The nearest blocking world collider or living server hitbox determines the impact. A miss or terrain/obstacle impact is still a server-resolved shot, but does no player damage.

## Hitboxes, damage, and death

For each initialized player, the host creates six non-networked server physics trigger boxes: `Head`, `Torso`, `LeftArm`, `RightArm`, `LeftLeg`, and `RightLeg`. They follow the server-owned ENU snapshot through the geospatial manager and are disabled for dead or match-finished players. Client-rendered avatar meshes are not authoritative hitboxes.

The weapon asset supplies damage scaling. At the prototype defaults, torso damage is 34, head damage is 68, and any limb hit is 22 after rounding. The host applies damage directly to the victim's server-owned snapshot. At zero health it sets `Alive=false`, changes movement presentation to `Dead`, zeros velocity, removes hitboxes, and broadcasts a separate `PlayerDied` event. Dead input is rejected server-side; there is no respawn path.

The eliminated owner detaches only the local camera rig. It follows a living player in spectator mode, cycles targets with Tab, or watches the death location if nobody is alive. It remains a spectator through match end. When one survivor remains after multiple clients have been connected (or a disconnect leaves one survivor), the host freezes all initialized participants and broadcasts the winner; if there is no survivor, it broadcasts a no-winner result. Match evaluation waits while any currently connected player's snapshot is still initializing, so a joining player is not mistaken for a disconnect.

## Client presentation

`PhaseSevenCombatHud` consumes only host-broadcast `ShotResolved`, `PlayerDied`, and `MatchEnded` events plus replicated player snapshots. It provides:

- small muzzle-flash and procedural/asset fire audio on confirmed shots;
- pooled server-confirmed impact and blood-coloured hit effects with a hit sound;
- a hit marker only for a server-confirmed hit by the local shooter;
- a red damage flash and source/damage label only for a server-confirmed hit on the local player;
- an elimination kill feed, health/ammo/reload HUD, and match result overlay;
- a no-respawn spectator camera and Tab target cycling.

Remote procedural avatars smoothly transition to a fallen/dead presentation from their replicated server-dead state. Cosmetic presentation never determines damage, health, hit markers, or match results.

## Source map

- `Assets/WorldPvp/Runtime/Combat/CombatModels.cs` — NGO fire/event data, hit zones and aim/spread math.
- `Assets/WorldPvp/Runtime/Combat/WeaponDefinition.cs` — editable, validated weapon data asset.
- `Assets/WorldPvp/Runtime/Combat/Weapon.cs` — server magazine, rate limit and reload timer; local-only fire pacing.
- `Assets/WorldPvp/Runtime/Combat/NetworkPlayerHitbox.cs` — owner/zone tag for server physics volumes.
- `Assets/WorldPvp/Runtime/Battles/NetworkPlayer.cs` — fire intent RPC checks, host hitscan/damage/death, network snapshots and events.
- `Assets/WorldPvp/Runtime/Combat/CombatMatchController.cs` — host-only round end, freeze, winner/no-survivor broadcast.
- `Assets/WorldPvp/Runtime/UI/PhaseSevenCombatHud.cs` — client-only combat UI, effects/audio, event feedback and spectating.
- `Assets/WorldPvp/Editor/PhaseSevenCombatAssetBuilder.cs` — creates the prototype weapon asset; `PhaseOneSceneBuilder.cs` wires it into the player prefab, match coordinator, and HUD.
- `Assets/WorldPvp/Tests/EditMode/CombatSystemTests.cs` — intended Unity EditMode tests for weapon data, cooldown/ammo/reload, intent payload, aim validation and spread.

## Verification boundary

Passing Python source checks only validates repository invariants. It does not compile Unity C#, validate NGO RPC serialization, confirm Cesium collider interaction, or prove a hit in live gameplay. Unity import/compiler, EditMode Test Runner, WebGL build, live Google/Cesium terrain, and browser multiplayer remain **NOT RUN** until actually executed. The host-authoritative browser gameplay requirement must be accepted in actual browser instances; native/Editor tests are not substitutes. See [`Tests/PHASE7_ACCEPTANCE.md`](../Tests/PHASE7_ACCEPTANCE.md).
