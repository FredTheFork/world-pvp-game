#!/usr/bin/env python3
"""Unity-independent static checks for Phase 6 source wiring and honesty boundaries."""
from __future__ import annotations

import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


class PhaseSixStaticChecks(unittest.TestCase):
    def test_pinned_unity_and_cesium_versions_are_unchanged(self) -> None:
        manifest = json.loads(read("Packages/manifest.json"))
        self.assertEqual(manifest["dependencies"].get("com.cesium.unity"), "1.26.0")
        self.assertIn("6000.3.24f1", read("ProjectSettings/ProjectVersion.txt"))

    def test_streaming_uses_cesium_126_hierarchical_controls(self) -> None:
        controller = read("Assets/WorldPvp/Runtime/Streaming/WorldStreamingController.cs")
        for api in (
            "maximumScreenSpaceError",
            "maximumCachedBytes",
            "maximumSimultaneousTileLoads",
            "loadingDescendantLimit",
            "preloadAncestors",
            "preloadSiblings",
            "enableFrustumCulling",
            "CesiumCameraManager.GetOrCreate",
            "additionalCameras.Add",
            "additionalCameras.Remove",
        ):
            with self.subTest(api=api):
                self.assertIn(api, controller)
        self.assertIn("predictionCamera.enabled = false", controller)
        self.assertIn("PredictionMaximumAheadMeters", controller)
        self.assertIn("PredictionCameraFarMeters", controller)
        self.assertIn("not a guaranteed request or billing cap", controller)
        self.assertNotIn("maximumCacheOverflowBytes", controller)

    def test_benchmark_distance_bands_and_budgets_are_configurable(self) -> None:
        settings = read("Assets/WorldPvp/Runtime/Configuration/PhaseOneWorldSettings.cs")
        for field in (
            "veryHighDetailDistanceMeters = 100f",
            "highDetailDistanceMeters = 300f",
            "mediumDetailDistanceMeters = 750f",
            "maximumTileCacheMiB = 256f",
            "maximumSimultaneousTileLoads = 4",
            "loadingDescendantLimit = 16",
            "targetFramesPerSecond = 60",
            "p95FrameTimeBudgetMilliseconds = 16.7f",
            "unityReservedMemoryBudgetMiB = 1024f",
            "gpuMemoryBudgetMiB = 512f",
            "sustainedMapBandwidthBudgetMbps = 5f",
            "visibleCesiumTriangleBudget = 1500000",
            "mapRequestWarningPerMatch = 1000",
        ):
            with self.subTest(field=field):
                self.assertIn(field, settings)
        self.assertIn("not a metre-radius tile cutoff", settings)

    def test_map_usage_sources_and_cost_are_explicitly_estimates(self) -> None:
        tracker = read("Assets/WorldPvp/Runtime/Streaming/MapUsageTracker.cs")
        plugin = read("Assets/Plugins/WebGL/WorldPvpMapUsageTelemetry.jslib")
        hud = read("Assets/WorldPvp/Runtime/UI/PhaseSixDiagnosticsHud.cs")
        for field in (
            "RootTilesetLoadAttempts",
            "BrowserObservedRootRequestStarts",
            "BrowserObservedRendererRequestStarts",
            "CesiumTileGameObjectCreations",
            "SessionMilliseconds",
            "EstimateGrossCostUsd",
            "PlayerPrefs.Save",
            "MaximumSavedSessions = 20",
        ):
            with self.subTest(field=field):
                self.assertIn(field, tracker)
        self.assertIn("OnTileGameObjectCreated", tracker)
        self.assertIn("never a Google billing authority", tracker)
        self.assertIn("transferSize", plugin)
        self.assertIn("Never retain or return resource URLs", plugin)
        self.assertNotIn("console.log", plugin)
        self.assertIn("not Google billable events", hud)
        self.assertIn("does not stop Cesium requests", hud)

    def test_usage_rpc_is_separate_from_authoritative_movement_input(self) -> None:
        player = read("Assets/WorldPvp/Runtime/Battles/NetworkPlayer.cs")
        simulation = read("Assets/WorldPvp/Runtime/Battles/NetworkPlayerSimulation.cs")
        command_start = simulation.find("struct NetworkPlayerInputCommand")
        self.assertGreaterEqual(command_start, 0)
        command_tail = simulation[command_start:]
        command_tail = command_tail.split("struct NetworkPlayerSnapshot", 1)[0]
        self.assertNotIn("MapUsage", command_tail)
        self.assertIn("SubmitMapUsageSnapshotServerRpc", player)
        self.assertIn("RecordPlayerUsageSnapshot", player)
        self.assertIn("self-reported telemetry", player)
        self.assertIn("NetworkPlayerSimulation.Step", player)

    def test_web_build_enables_threads_frame_stats_and_existing_headers(self) -> None:
        build = read("Assets/WorldPvp/Editor/PhaseThreeWebBuildMenu.cs")
        self.assertIn("PlayerSettings.WebGL.threadsSupport = true", build)
        self.assertIn("PlayerSettings.enableFrameTimingStats = true", build)
        headers = json.loads(read("WebDeploy/Vercel/vercel.json"))
        flattened = json.dumps(headers)
        for header in (
            "Cross-Origin-Opener-Policy",
            "Cross-Origin-Embedder-Policy",
            "Cross-Origin-Resource-Policy",
        ):
            self.assertIn(header, flattened)

    def test_acceptance_docs_keep_live_checks_unrun(self) -> None:
        acceptance = read("Tests/PHASE6_ACCEPTANCE.md")
        architecture = read("Documentation/PHASE6_STREAMING_PERFORMANCE.md")
        self.assertIn("Status at Phase 6 source implementation: NOT RUN", acceptance)
        self.assertIn("Unity compilation and runtime acceptance are NOT RUN", architecture)
        for browser in ("Chrome", "Edge", "Firefox"):
            with self.subTest(browser=browser):
                self.assertIn(browser, acceptance)
        for unrun in ("Phase 3 two-tab", "Phase 4 five-browser", "Phase 5 terrain/gameplay"):
            self.assertIn(unrun, acceptance)
        self.assertIn("Do not publicly launch based on this client tracker alone", architecture)

    def test_project_configuration_json_remains_valid(self) -> None:
        for path in (
            "Assets/WorldPvp/Runtime/WorldPvp.Phase1.asmdef",
            "Assets/WorldPvp/Tests/EditMode/WorldPvp.Phase1.Tests.asmdef",
            "Packages/manifest.json",
            "WebDeploy/Vercel/vercel.json",
        ):
            with self.subTest(path=path):
                json.loads(read(path))


if __name__ == "__main__":
    unittest.main(verbosity=2)
