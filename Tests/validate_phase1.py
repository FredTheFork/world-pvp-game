#!/usr/bin/env python3
"""Unity-independent static checks for the retained geospatial foundation and multiplayer source flow."""

from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXPECTED_EDITOR = "6000.3.24f1"
EXPECTED_PACKAGES = {
    "com.cesium.unity": "1.26.0",
    "com.unity.inputsystem": "1.19.0",
    "com.unity.mathematics": "1.3.2",
    "com.unity.netcode.gameobjects": "2.13.3",
    "com.unity.render-pipelines.core": "17.3.0",
    "com.unity.render-pipelines.universal": "17.3.0",
    "com.unity.services.authentication": "3.8.0",
    "com.unity.services.multiplayer": "2.3.3",
    "com.unity.shadergraph": "17.3.0",
    "com.unity.splines": "2.9.1",
    "com.unity.test-framework": "1.4.6",
    "com.unity.transport": "2.7.4",
    "com.unity.ugui": "2.0.0",
}
REQUIRED_PATHS = [
    "Assets",
    "Packages/manifest.json",
    "ProjectSettings/ProjectVersion.txt",
    "ProjectSettings/ProjectSettings.asset",
    "Documentation/ARCHITECTURE_PHASE2.md",
    "Documentation/ARCHITECTURE_PHASE3.md",
    "Documentation/ARCHITECTURE_PHASE4.md",
    "Documentation/ARCHITECTURE_PHASE5.md",
    "Tests/PHASE1_ACCEPTANCE.md",
    "Tests/PHASE2_ACCEPTANCE.md",
    "Tests/PHASE3_ACCEPTANCE.md",
    "Tests/PHASE4_ACCEPTANCE.md",
    "Tests/PHASE5_ACCEPTANCE.md",
    "Assets/WorldPvp/Runtime/Geospatial/GeoPosition.cs",
    "Assets/WorldPvp/Runtime/Geospatial/LocalPosition.cs",
    "Assets/WorldPvp/Runtime/Geospatial/GeospatialWorldManager.cs",
    "Assets/WorldPvp/Runtime/Geospatial/ArenaBoundaryVisualizer.cs",
    "Assets/WorldPvp/Runtime/Configuration/PhaseOneWorldSettings.cs",
    "Assets/WorldPvp/Runtime/Player/PhaseOneTestCharacterController.cs",
    "Assets/WorldPvp/Runtime/Player/ProceduralPlayerPresentation.cs",
    "Assets/WorldPvp/Runtime/UI/PhaseFiveGameplayHud.cs",
    "Assets/WorldPvp/Runtime/UI/PhaseOneRuntimeHud.cs",
    "Assets/WorldPvp/Editor/PhaseOneSceneBuilder.cs",
    "Assets/WorldPvp/Editor/PhaseThreeWebBuildMenu.cs",
    "Assets/WorldPvp/Runtime/Battles/MatchJoinLink.cs",
    "Assets/WorldPvp/Runtime/Battles/MatchSession.cs",
    "Assets/WorldPvp/Runtime/Battles/NetworkPlayerSimulation.cs",
    "Assets/WorldPvp/Runtime/Battles/NetworkPlayer.cs",
    "Assets/WorldPvp/Runtime/Battles/WorldLocationValidator.cs",
    "Assets/WorldPvp/Runtime/Battles/BattleSessionCoordinator.cs",
    "Assets/WorldPvp/Runtime/UI/PhaseThreeRuntimeHud.cs",
    "Assets/Plugins/WebGL/WorldPvpGoogleMaps.jslib",
    "Assets/WorldPvp/Resources/GoogleMaps_Attribution_White.png",
    "Assets/WebGLTemplates/WorldPvp/index.html",
    "WebDeploy/Vercel/vercel.json",
    "Tools/serve_webgl_local.py",
    "Assets/WorldPvp/Tests/EditMode/GeospatialWorldManagerArenaTests.cs",
    "Assets/WorldPvp/Tests/EditMode/NetworkPlayerSimulationTests.cs",
]


class ProjectScaffoldTests(unittest.TestCase):
    def test_repository_skeleton_exists(self) -> None:
        for relative in REQUIRED_PATHS:
            with self.subTest(path=relative):
                self.assertTrue((ROOT / relative).exists(), f"Missing {relative}")

    def test_editor_version_is_pinned(self) -> None:
        version_file = (ROOT / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
        self.assertIn(f"m_EditorVersion: {EXPECTED_EDITOR}", version_file)

    def test_direct_packages_and_pins_match_baseline(self) -> None:
        manifest = json.loads((ROOT / "Packages/manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["dependencies"], EXPECTED_PACKAGES)
        self.assertEqual(set(manifest["pinnedPackages"]), set(EXPECTED_PACKAGES))
        self.assertTrue(manifest["enableLockFile"])
        self.assertEqual(manifest["resolutionStrategy"], "lowest")

    def test_new_input_system_is_selected(self) -> None:
        settings = (ROOT / "ProjectSettings/ProjectSettings.asset").read_text(encoding="utf-8")
        self.assertRegex(settings, r"(?m)^\s*activeInputHandler:\s*1\s*$")

    def test_geo_and_local_positions_are_separate_and_enu(self) -> None:
        geo = (ROOT / "Assets/WorldPvp/Runtime/Geospatial/GeoPosition.cs").read_text(encoding="utf-8")
        local = (ROOT / "Assets/WorldPvp/Runtime/Geospatial/LocalPosition.cs").read_text(encoding="utf-8")
        self.assertIn("struct GeoPosition", geo)
        self.assertIn("LatitudeDegrees", geo)
        self.assertIn("LongitudeDegrees", geo)
        self.assertIn("AltitudeMeters", geo)
        self.assertIn("struct LocalPosition", local)
        self.assertIn("X/East, Y/Up, Z/North (ENU)", local)
        self.assertIn("EastMeters", local)
        self.assertIn("UpMeters", local)
        self.assertIn("NorthMeters", local)
        self.assertFalse((ROOT / "Assets/WorldPvp/Runtime/Geospatial/GeoCoordinate.cs").exists())

    def test_world_manager_owns_required_geospatial_apis(self) -> None:
        manager = (ROOT / "Assets/WorldPvp/Runtime/Geospatial/GeospatialWorldManager.cs").read_text(encoding="utf-8")
        required = (
            "WorldOrigin",
            "CurrentPlayerGeographicPosition",
            "LocalToGeographic(",
            "GeographicToLocal(",
            "GetGroundHeight(",
            "DistanceBetweenPlayers(",
            "GetDistanceFromArenaCentre(",
            "IsInsideArena(",
            "EnforceLocalArenaBoundary(",
            "VisibilityRadiusMeters",
            "RenderRadiusMeters",
            "CesiumOriginShift",
        )
        for api in required:
            with self.subTest(api=api):
                self.assertIn(api, manager)
        self.assertIn("worldOriginLocalToEcef", manager)
        self.assertIn("worldOriginEcefToLocal", manager)
        self.assertIn("BoundaryEpsilonMeters", manager)

    def test_arena_buffers_and_origin_shift_are_configured_in_metres(self) -> None:
        settings = (ROOT / "Assets/WorldPvp/Runtime/Configuration/PhaseOneWorldSettings.cs").read_text(encoding="utf-8")
        builder = (ROOT / "Assets/WorldPvp/Editor/PhaseOneSceneBuilder.cs").read_text(encoding="utf-8")
        visualizer = (ROOT / "Assets/WorldPvp/Runtime/Geospatial/ArenaBoundaryVisualizer.cs").read_text(encoding="utf-8")
        for setting in ("defaultGameplayRadiusMeters", "visibilityBufferMeters", "preloadBufferMeters", "originShiftThresholdMeters"):
            with self.subTest(setting=setting):
                self.assertIn(setting, settings)
        self.assertIn("AddComponent<CesiumOriginShift>()", builder)
        self.assertIn("originShift.distance = settings.OriginShiftThresholdMeters", builder)
        self.assertIn("Gameplay radius", visualizer)
        self.assertIn("visibility extent", visualizer)
        self.assertIn("preload", visualizer)

    def test_ground_anchor_and_local_boundary_are_not_just_a_visual_ring(self) -> None:
        manager = (ROOT / "Assets/WorldPvp/Runtime/Geospatial/GeospatialWorldManager.cs").read_text(encoding="utf-8")
        motor = (ROOT / "Assets/WorldPvp/Runtime/Player/PhaseOneTestCharacterController.cs").read_text(encoding="utf-8")
        hud = (ROOT / "Assets/WorldPvp/Runtime/UI/PhaseOneRuntimeHud.cs").read_text(encoding="utf-8")
        self.assertIn("CesiumGlobeAnchor", manager)
        self.assertIn("Physics.RaycastAll", manager)
        self.assertIn("worldManager.EnforceLocalArenaBoundary()", motor)
        self.assertIn("BoundaryFeedback", hud)
        self.assertIn("server authority is not active", manager)
        self.assertIn("Phase 4 movement is host-authoritative", manager)

    def test_phase_two_tests_cover_all_required_sizes_and_locations(self) -> None:
        tests = (ROOT / "Assets/WorldPvp/Tests/EditMode/GeospatialWorldManagerArenaTests.cs").read_text(encoding="utf-8")
        acceptance = (ROOT / "Tests/PHASE2_ACCEPTANCE.md").read_text(encoding="utf-8")
        self.assertIn("100.0", tests)
        self.assertIn("500.0", tests)
        self.assertIn("1000.0", tests)
        self.assertIn("51.5073", tests)
        self.assertIn("51.6030", tests)
        self.assertIn("FixedArenaLocalFrameSurvivesCesiumRenderOriginShift", tests)
        for radius in ("100 m", "500 m", "1,000 m"):
            self.assertIn(radius, acceptance)
        self.assertIn("Hyde Park", acceptance)
        self.assertIn("Harefield", acceptance)

    def test_phase_three_radius_fail_closed_coverage_and_session_fields_are_present(self) -> None:
        match = (ROOT / "Assets/WorldPvp/Runtime/Battles/MatchSession.cs").read_text(encoding="utf-8")
        validator = (ROOT / "Assets/WorldPvp/Runtime/Battles/WorldLocationValidator.cs").read_text(encoding="utf-8")
        hud = (ROOT / "Assets/WorldPvp/Runtime/UI/PhaseThreeRuntimeHud.cs").read_text(encoding="utf-8")
        self.assertIn("MaximumArenaRadiusMeters = 2000.0", match)
        self.assertIn("wpv_centre_lat", match)
        self.assertIn("wpv_centre_lon", match)
        self.assertIn("wpv_centre_alt_m", match)
        self.assertIn("wpv_radius_m", match)
        self.assertIn("VisibilityPropertyOptions.Member", match)
        self.assertIn("Photorealistic coverage unavailable here. Choose another location.", validator)
        self.assertIn("SampleHeightMostDetailed", validator)
        self.assertIn("I checked this centre in Google's official 3D coverage map", hud)
        self.assertIn("Use my location (coming soon)", hud)
        for radius in ("100.0", "250.0", "500.0", "1000.0", "2000.0"):
            with self.subTest(radius=radius):
                self.assertIn(radius, (ROOT / "Assets/WorldPvp/Tests/EditMode/PhaseThreeMatchSessionTests.cs").read_text(encoding="utf-8"))

    def test_phase_three_invites_are_opaque_and_have_static_join_routing(self) -> None:
        invite = (ROOT / "Assets/WorldPvp/Runtime/Battles/MatchJoinLink.cs").read_text(encoding="utf-8")
        session = (ROOT / "Assets/WorldPvp/Runtime/Battles/MatchSession.cs").read_text(encoding="utf-8")
        coordinator = (ROOT / "Assets/WorldPvp/Runtime/Battles/BattleSessionCoordinator.cs").read_text(encoding="utf-8")
        js = (ROOT / "Assets/Plugins/WebGL/WorldPvpGoogleMaps.jslib").read_text(encoding="utf-8")
        self.assertIn('builder.Path = "/join/" + normalizedCode', invite)
        self.assertIn("builder.Query = string.Empty", invite)
        self.assertIn("TryParseJoinInput(inputCode", coordinator)
        self.assertIn("JoinSessionByCodeAsync(joinCode)", coordinator)
        self.assertIn("TryRead(activeSession", coordinator)
        self.assertIn('window.sessionStorage.getItem("worldpvp-tab-profile")', js)
        self.assertIn('window.sessionStorage.setItem("worldpvp-tab-profile", profile)', js)
        hud = (ROOT / "Assets/WorldPvp/Runtime/UI/PhaseThreeRuntimeHud.cs").read_text(encoding="utf-8")
        template = (ROOT / "Assets/WebGLTemplates/WorldPvp/index.html").read_text(encoding="utf-8")
        self.assertIn("public void OnBrowserRuntimeReady()", hud)
        self.assertIn('instance.SendMessage("PhaseOneWorldSystems", "OnBrowserRuntimeReady", "")', template)
        self.assertIn("WorldPvp_ClearInvitePath", js)
        self.assertIn('"/join/:code"', (ROOT / "WebDeploy/Vercel/vercel.json").read_text(encoding="utf-8"))
        self.assertNotIn("latitude=", invite)
        self.assertNotIn("radius=", invite)

    def test_phase_three_webgl_and_relay_setup_is_present(self) -> None:
        web_build = (ROOT / "Assets/WorldPvp/Editor/PhaseThreeWebBuildMenu.cs").read_text(encoding="utf-8")
        coordinator = (ROOT / "Assets/WorldPvp/Runtime/Battles/BattleSessionCoordinator.cs").read_text(encoding="utf-8")
        network_player = (ROOT / "Assets/WorldPvp/Runtime/Battles/NetworkPlayer.cs").read_text(encoding="utf-8")
        simulation = (ROOT / "Assets/WorldPvp/Runtime/Battles/NetworkPlayerSimulation.cs").read_text(encoding="utf-8")
        template = (ROOT / "Assets/WebGLTemplates/WorldPvp/index.html").read_text(encoding="utf-8")
        deploy = (ROOT / "WebDeploy/Vercel/vercel.json").read_text(encoding="utf-8")
        js = (ROOT / "Assets/Plugins/WebGL/WorldPvpGoogleMaps.jslib").read_text(encoding="utf-8")
        hud = (ROOT / "Assets/WorldPvp/Runtime/UI/PhaseThreeRuntimeHud.cs").read_text(encoding="utf-8")
        builder = (ROOT / "Assets/WorldPvp/Editor/PhaseOneSceneBuilder.cs").read_text(encoding="utf-8")
        self.assertIn("threadsSupport = true", web_build)
        self.assertIn("WebGLCompressionFormat.Brotli", web_build)
        self.assertIn("transport.UseWebSockets = true", coordinator)
        self.assertIn(".WithRelayNetwork()", coordinator)
        self.assertIn("NetworkVariable<NetworkPlayerSnapshot>", network_player)
        self.assertIn("[ServerRpc(RequireOwnership = true)]", network_player)
        self.assertIn("NetworkPlayerSimulation.Step", network_player)
        self.assertIn("pendingOwnerInputs", network_player)
        self.assertIn("BufferRemoteSnapshot", network_player)
        self.assertIn("NetworkPlayerSimulation.Interpolate", network_player)
        self.assertIn("TickRate = NetworkPlayer.ServerSimulationTickRate", builder)
        self.assertIn("FirePressed", simulation)
        self.assertNotIn("NetworkGeoPlayer", builder + coordinator + network_player)
        self.assertIn("Content-Encoding", deploy)
        self.assertIn("Cross-Origin-Embedder-Policy", deploy)
        self.assertIn("window.worldPvpUnityInstance = instance", template)
        self.assertIn("new placesLibrary.AutocompleteSessionToken()", js)
        self.assertIn("prediction.toPlace()", js)
        self.assertIn('Resources.Load<Texture2D>("GoogleMaps_Attribution_White")', hud)
        self.assertNotIn("GUILayout.EndVerticalIfNeeded", hud)

    def test_phase_four_acceptance_is_a_five_browser_test_and_unverified(self) -> None:
        checklist = (ROOT / "Tests/PHASE4_ACCEPTANCE.md").read_text(encoding="utf-8")
        self.assertIn("Status at Phase 4 source implementation: NOT RUN", checklist)
        self.assertIn("five separate browser instances", checklist)
        self.assertIn("Phase 3 two-tab regression", checklist)
        self.assertIn("does not implement firing", checklist)
        self.assertIn("Relay host", checklist)

    def test_phase_four_simulation_and_state_have_no_client_position_input(self) -> None:
        simulation = (ROOT / "Assets/WorldPvp/Runtime/Battles/NetworkPlayerSimulation.cs").read_text(encoding="utf-8")
        network_player = (ROOT / "Assets/WorldPvp/Runtime/Battles/NetworkPlayer.cs").read_text(encoding="utf-8")
        command = simulation.split("public struct NetworkPlayerInputCommand", 1)[1].split("public void NetworkSerialize", 1)[0]
        for required in ("uint Tick", "MoveX", "MoveY", "LookX", "LookY", "Sprint", "JumpPressed", "FirePressed"):
            with self.subTest(field=required):
                self.assertIn(required, command)
        self.assertNotIn("Position", command)
        self.assertNotIn("Transform", command)
        for required in ("DisplayName", "EastMeters", "UpMeters", "NorthMeters", "YawDegrees", "VelocityEastMetersPerSecond", "Grounded", "Health", "Alive", "Team", "LastProcessedInputTick"):
            with self.subTest(field=required):
                self.assertIn(required, simulation)
        self.assertIn("RequireOwnership = true", network_player)
        self.assertIn("TryGetGroundSurface", network_player)
        self.assertIn("CanFitPlayerCapsule", network_player)
        self.assertIn("LocalToGeographic", network_player)

    def test_phase_five_locomotion_view_geographic_hud_and_minimap_are_wired(self) -> None:
        settings = (ROOT / "Assets/WorldPvp/Runtime/Configuration/PhaseOneWorldSettings.cs").read_text(encoding="utf-8")
        simulation = (ROOT / "Assets/WorldPvp/Runtime/Battles/NetworkPlayerSimulation.cs").read_text(encoding="utf-8")
        network_player = (ROOT / "Assets/WorldPvp/Runtime/Battles/NetworkPlayer.cs").read_text(encoding="utf-8")
        manager = (ROOT / "Assets/WorldPvp/Runtime/Geospatial/GeospatialWorldManager.cs").read_text(encoding="utf-8")
        builder = (ROOT / "Assets/WorldPvp/Editor/PhaseOneSceneBuilder.cs").read_text(encoding="utf-8")
        presentation = (ROOT / "Assets/WorldPvp/Runtime/Player/ProceduralPlayerPresentation.cs").read_text(encoding="utf-8")
        hud = (ROOT / "Assets/WorldPvp/Runtime/UI/PhaseFiveGameplayHud.cs").read_text(encoding="utf-8")
        for field in (
            "crouchSpeedMetersPerSecond",
            "groundAccelerationMetersPerSecondSquared",
            "groundDecelerationMetersPerSecondSquared",
            "airAccelerationMetersPerSecondSquared",
            "airDecelerationMetersPerSecondSquared",
            "characterHeightMeters",
            "maximumSlopeDegrees",
            "stepOffsetMeters",
            "firstPersonEyeHeightMeters",
        ):
            with self.subTest(setting=field):
                self.assertIn(field, settings)
        self.assertIn("public bool Crouch", simulation)
        self.assertIn("GroundNormalEast", simulation)
        self.assertIn("NetworkPlayerMovementState.CrouchWalking", simulation)
        self.assertIn("NetworkPlayerMovementState.Landing", simulation)
        self.assertIn("TryGetGroundSurface", manager)
        self.assertIn("CanFitPlayerCapsule", manager)
        self.assertIn("SimulateWithGroundSupport", network_player)
        self.assertIn("FirstPersonCameraPivot", builder)
        self.assertIn("settings.FirstPersonEyeHeightMeters", builder)
        self.assertIn("PhaseFiveGameplayHud", builder)
        self.assertIn("ConfigureRemote", presentation)
        for state in ("Idle", "Walking", "Sprinting", "Jumping", "Falling", "Landing", "Dead"):
            with self.subTest(state=state):
                self.assertIn("NetworkPlayerMovementState." + state, presentation)
        self.assertIn("LatitudeDegrees", hud)
        self.assertIn("LongitudeDegrees", hud)
        self.assertIn("AltitudeMeters", hud)
        self.assertIn("GameplayRadiusMeters", hud)
        self.assertIn("CurrentState", hud)
        self.assertNotIn("Google Maps", hud)

        checklist = (ROOT / "Tests/PHASE5_ACCEPTANCE.md").read_text(encoding="utf-8")
        self.assertIn("Status at Phase 5 source implementation: NOT RUN", checklist)
        self.assertIn("five separate browser instances", checklist)
        self.assertIn("flat ground, slopes, steps, roads, parks", checklist)
        self.assertIn("`fire` field remains future-facing intent only", checklist)

    def test_phase_three_acceptance_is_not_claimed_as_run(self) -> None:
        checklist = (ROOT / "Tests/PHASE3_ACCEPTANCE.md").read_text(encoding="utf-8")
        self.assertIn("Status at Phase 3 source implementation: NOT RUN", checklist)
        self.assertIn("two separate browser tabs", checklist)
        self.assertIn("No successful two-tab run is claimed", checklist)
        self.assertIn("not cheat-proof", checklist)

    def test_no_plausible_google_api_key_is_tracked(self) -> None:
        key_pattern = re.compile(r"AIza[0-9A-Za-z_-]{35}")
        for path in ROOT.rglob("*"):
            if not path.is_file() or ".git" in path.parts:
                continue
            if any(part in {"Library", "Temp", "obj", "bin"} for part in path.parts):
                continue
            try:
                content = path.read_text(encoding="utf-8")
            except (UnicodeDecodeError, OSError):
                continue
            self.assertIsNone(key_pattern.search(content), f"Possible real API key in {path.relative_to(ROOT)}")

        gitignore = (ROOT / ".gitignore").read_text(encoding="utf-8")
        self.assertIn("google-tiles-key.local.txt", gitignore)

    def test_unity_lockfile_is_not_fabricated(self) -> None:
        lock_path = ROOT / "Packages/packages-lock.json"
        if lock_path.exists():
            lock = json.loads(lock_path.read_text(encoding="utf-8"))
            self.assertIsInstance(lock.get("dependencies"), dict)
        else:
            print("NOTE: packages-lock.json is pending Unity Package Manager resolution.")

    def test_phase_two_manual_acceptance_is_honestly_unverified(self) -> None:
        checklist = (ROOT / "Tests/PHASE2_ACCEPTANCE.md").read_text(encoding="utf-8")
        self.assertIn("Status at Phase 2 implementation: NOT RUN", checklist)
        self.assertIn("server must evaluate", checklist)
        self.assertIn("server enforcement", checklist)
        self.assertEqual(checklist.count("| NOT RUN |"), 6)

    def test_phase_one_manual_gate_remains_unverified(self) -> None:
        checklist = (ROOT / "Tests/PHASE1_ACCEPTANCE.md").read_text(encoding="utf-8")
        self.assertIn("Status at scaffold creation: NOT RUN", checklist)
        self.assertIn("Phase 1 gate", checklist)


if __name__ == "__main__":
    unittest.main(verbosity=2)
