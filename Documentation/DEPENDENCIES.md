# Pinned toolchain — Phase 4

| Component | Pinned version | Purpose |
|---|---:|---|
| Unity Editor | `6000.3.24f1` | Unity 6.3 LTS editor baseline |
| Universal Render Pipeline | `17.3.0` | Render pipeline for Unity 6.3 |
| URP Core | `17.3.0` | URP rendering dependency |
| Shader Graph | `17.3.0` | URP shader dependency; also satisfies Cesium's minimum |
| Cesium for Unity | `1.26.0` | Cesium georeference, Google 3D Tiles streaming, globe anchors |
| Input System | `1.19.0` | Keyboard and mouse controls |
| Mathematics | `1.3.2` | Cesium `double3` geographic values |
| Splines | `2.9.1` | Cesium assembly's Unity.Splines reference; released for Unity 6000.3 |
| Unity Test Framework | `1.4.6` | Edit Mode acceptance-unit tests |
| uGUI | `2.0.0` | Cesium's default attribution/EventSystem component dependency |
| Netcode for GameObjects | `2.13.3` | NGO player objects, owner-authenticated input RPC, server-written movement snapshots |
| Unity Transport | `2.7.4` | Relay-capable transport; WebGL WSS/WebSocket support |
| UGS Authentication | `3.8.0` | Anonymous sign-in with isolated per-tab profiles |
| UGS Multiplayer Services | `2.3.3` | Sessions, opaque join-code resolution, and Relay integration |

`Packages/manifest.json` declares direct package versions, `pinnedPackages`, lockfile support, and `resolutionStrategy: lowest`. Unity's resolver creates the complete `Packages/packages-lock.json` on the first successful package resolution. That file must be committed as soon as it exists; it has intentionally **not** been hand-authored. The Editor `PhaseOnePackageGuard` checks direct versions against the selected Editor and package manifest. Do not upgrade pins as part of unrelated work.

The runtime and EditMode assembly definitions directly reference `Unity.Collections` for NGO's `FixedString64Bytes` network state. It is expected to resolve as an NGO package dependency; this adds no package version or intentional dependency upgrade. Confirm the resolved dependency and assembly name in Unity's generated lockfile/import.

## Compatibility notes

- Unity 6.3 core graphics packages remain on the Editor's `17.3.0` package stream. Cesium and the Phase 2 geospatial APIs are preserved.
- NGO + Unity Transport are used through the UGS Multiplayer Services Sessions integration. Unity's WebGL browser target cannot use UDP; the runtime configures `UnityTransport.UseWebSockets` and uses Relay WSS behavior. This API combination has not yet been compiled against the resolved Unity project; verify with Unity after UPM resolution.
- Session coordinates are member-visible client-host-authored properties; adding UGS Authentication/Multiplayer does not create a trusted authoritative backend.
- The user-facing zero-hosting-fee preference is approached with static WebGL plus UGS Relay, not promised. Relay/Google billing and static-host limits are documented in [`ARCHITECTURE_PHASE4.md`](ARCHITECTURE_PHASE4.md) and the README.

## Version/source references

- [Unity 6000.3.24f1 release notes](https://unity.com/releases/editor/whats-new/6000.3.24f1)
- [Unity 6.3 URP package catalog](https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.render-pipelines.universal.html) and [Shader Graph package catalog](https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.shadergraph.html): core graphics packages match the Editor stream (`17.3.0`).
- [Unity 6.3 Splines package catalog](https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.splines.html): `2.9.1` is released for Unity `6000.3`.
- [Unity 6.3 uGUI package catalog](https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.ugui.html): Unity `6000.3` uses the `2.0` package line.
- [Unity 6000.3.11f1 release notes](https://unity.com/releases/editor/whats-new/6000.3.11f1): Input System `1.19.0` is in the Unity 6.3 package update stream.
- [Mathematics 1.3 changelog](https://docs.unity3d.com/Packages/com.unity.mathematics@1.3/changelog/CHANGELOG.html) and [Test Framework 1.4.6 release](https://github.com/needle-mirror/com.unity.test-framework/releases/tag/1.4.6).
- [Cesium for Unity v1.26.0 package manifest](https://raw.githubusercontent.com/CesiumGS/cesium-unity/v1.26.0/package.json) and [release page](https://github.com/CesiumGS/cesium-unity/releases/tag/v1.26.0).
- [Unity NGO package API 2.13](https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.13/api/Unity.Netcode.NetworkManager.html) and [Transport WebGL support 2.7.4](https://docs.unity3d.com/Packages/com.unity.transport@2.7/manual/websockets.html).
- [Unity Multiplayer Services network connection docs](https://docs.unity.com/en-us/mps-sdk/manage-session-network-connection) and [MPS SDK install/migration docs](https://docs.unity.com/en-us/mps-sdk/install-and-upgrade).
- [Unity package manifest and lock-file documentation](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-manifestPrj.html).
