# Phase 1 manual acceptance checklist

**Status at scaffold creation: NOT RUN.** A Unity 6.3 Editor, live internet, a valid Map Tiles API key, and a covered location are required. Do not mark Phase 1 complete until all required rows below are checked on a real Unity run.

## Setup

- [ ] Open `world-pvp-game` in Unity `6000.3.24f1`; Package Manager finishes without errors.
- [ ] `Packages/packages-lock.json` is generated, reviewed, and committed before collaborative development.
- [ ] `Tools → World PvP → Phase 2 → Build or Rebuild Scene` succeeds (or the scene was created automatically).
- [ ] `Phase1_GoogleWorld` uses URP; Console contains no project-code compile errors.
- [ ] Google Cloud project has billing, Map Tiles API enabled, and a restricted key (see `Documentation/GOOGLE_MAP_TILES_SETUP.md`).

## Live proof

- [ ] Press Play; location panel opens; no request is sent while the key is blank.
- [ ] Select Hyde Park (`51.5073, -0.1657`, origin height `0 m` above WGS84 ellipsoid) and enter the key.
- [ ] Google Photorealistic 3D Tiles stream in; Google/data attribution is visible and not covered.
- [ ] Character starts disabled/in a safe held state, then appears on a Cesium tile collision surface rather than falling through while tiles load.
- [ ] `W` moves forward, `S` backward, `A` left, `D` right; movement is in local metres and respects the character controller.
- [ ] Mouse changes the camera view and the camera follows the capsule.
- [ ] Left Shift sprints; Space jumps; release returns to walking.
- [ ] The on-screen player latitude/longitude/ellipsoid height changes plausibly while walking; after stopping, the player remains on the selected real-world site and the Cesium globe anchor stays attached.
- [ ] F1/Escape opens the location panel; loading another valid covered coordinate moves the same local test character to the new place.

## Surface observations (record, don't assume)

| Surface | Observed result / notes |
|---|---|
| Flat terrain | |
| Road / pavement | |
| Slope | |
| Hill / elevation change | |
| Building roof/wall edge | |
| Steps / stairs | |
| Tile refinement while standing still | |
| Coverage gap / missing tiles | |

**Phase 1 gate:** If tile collision is unstable on these basic tests, record the Cesium version, coordinates, Console output, and repro before adding any multiplayer or combat work. This phase is the required technical proof for the final geospatial arena game, not a visual mock-up.
