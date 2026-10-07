#!/usr/bin/env python3
"""Repository-only Phase 9 source invariants; not a Unity compile or gameplay test."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
RUNTIME = ROOT / "Assets/WorldPvp/Runtime"


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


class PhaseNineSourceTests(unittest.TestCase):
    def test_exact_safety_notice_and_virtual_arena_scope_are_explicit(self):
        runtime = read("Assets/WorldPvp/Runtime/Gameplay/GameplayArenaRuntime.cs")
        hud = read("Assets/WorldPvp/Runtime/UI/PhaseThreeRuntimeHud.cs")
        exact = (
            "This is a virtual game. Stay aware of your surroundings. Do not enter roads, "
            "private property, restricted areas or dangerous locations while playing."
        )
        self.assertIn(exact, runtime)
        self.assertIn("GameplayArenaRuntime.SafetyWarning", hud)
        self.assertIn("DrawSafetyNotice();", hud)
        self.assertIn("real-world travel is not required", read("Assets/WorldPvp/Runtime/Geospatial/GeospatialWorldManager.cs"))

    def test_create_and_join_configure_the_same_session_arena_plan(self):
        coordinator = read("Assets/WorldPvp/Runtime/Battles/BattleSessionCoordinator.cs")
        self.assertGreaterEqual(coordinator.count("gameplayArenaRuntime.ConfigureMatch("), 2)
        self.assertIn("activeMatch.MaximumPlayers", coordinator)
        self.assertIn("ConfigureMatch(centre, radiusMeters, maximumPlayers", coordinator)

    def test_spawn_planner_is_deterministic_bounded_and_fails_closed(self):
        generator = read("Assets/WorldPvp/Runtime/Gameplay/GeographicArenaGenerator.cs")
        runtime = read("Assets/WorldPvp/Runtime/Gameplay/GameplayArenaRuntime.cs")
        network_player = read("Assets/WorldPvp/Runtime/Battles/NetworkPlayer.cs")
        tests = read("Assets/WorldPvp/Tests/EditMode/GeographicArenaGeneratorTests.cs")
        collision_tests = read("Assets/WorldPvp/Tests/EditMode/GameplayCollisionWorldTests.cs")
        self.assertIn("GoldenAngleRadians", generator)
        self.assertIn("CreateDeterministicSeed", generator)
        self.assertIn("The usable arena is too small", generator)
        self.assertIn("safe, separated terrain positions", generator)
        self.assertIn("IsTooCloseToLivePlayer", runtime)
        self.assertIn("TryAssignSpawn(", network_player)
        self.assertIn("GeneratesDeterministicDistributedPointsInsideTheUsableVirtualArena", tests)
        self.assertIn("FailsClosedWhenNoSafeGameplaySurfaceIsAvailable", tests)
        self.assertIn("MovementSweepAndProjectileQueryUseTheStableProxyLayer", collision_tests)
        self.assertIn("SafeSpawnRejectsRoadsWaterUnknownSurfaceAndSteepGround", collision_tests)
        self.assertNotIn("SpawnRadiusBaseMeters", network_player)
        self.assertNotIn("GoldenAngleRadians", network_player)

    def test_data_source_and_proxy_layer_are_renderer_independent(self):
        provider = read("Assets/WorldPvp/Runtime/Gameplay/WorldGameplayDataSource.cs")
        collision = read("Assets/WorldPvp/Runtime/Gameplay/GameplayCollisionWorld.cs")
        manager = read("Assets/WorldPvp/Runtime/Geospatial/GeospatialWorldManager.cs")
        self.assertIn("abstract class WorldGameplayDataSource : ScriptableObject", provider)
        self.assertIn("LegalReviewApproved", provider)
        self.assertIn("DerivedFromGoogleMapsContent", provider)
        self.assertIn("CombinedWithGoogleMapsContent", provider)
        self.assertIn("LicenseAndTermsReference", provider)
        self.assertIn("TryAssessArea", provider)
        self.assertIn("TryGetIndependentGroundSurface", collision)
        self.assertIn("TryResolveMovement(", collision)
        self.assertIn("TryRaycast(", collision)
        self.assertIn("BuildProxySpatialIndex", collision)
        self.assertIn("ProxySpatialCellSizeMeters = 25.0", collision)
        self.assertNotIn("CesiumForUnity", provider)
        self.assertNotIn("CesiumForUnity", collision)
        self.assertNotIn("Physics.", collision)
        self.assertIn("createPhysicsMeshes = gameplayCollisionWorld.IsPrototypeFallback", manager)
        self.assertIn("TryGetVisualGroundSurface", manager)

    def test_dedicated_server_requires_independent_data_without_google_key(self):
        bootstrap = read("Assets/WorldPvp/Runtime/Battles/PhaseEightDedicatedServerBootstrap.cs")
        self.assertIn("configuration.RadiusMeters,\n                        string.Empty,\n                        false", bootstrap)
        self.assertIn("IsProductionReady(", bootstrap)
        self.assertIn("TryPrepareArena(", bootstrap)
        self.assertNotIn("WORLD_PVP_GOOGLE_MAP_TILES_API_KEY", bootstrap)
        self.assertNotIn("MapTilesApiKey", bootstrap)

    def test_existing_spawn_acceptance_no_longer_requires_centre_offsets(self):
        phase4 = read("Tests/PHASE4_ACCEPTANCE.md")
        self.assertIn("no player is assigned the arena centre by default", phase4)
        self.assertIn("water/road/building/cliff/restricted-area avoidance is not verified", phase4)
        self.assertNotIn("deterministic, distinct ENU offsets near the exact arena centre", phase4)

    def test_phase9_remains_explicitly_unaccepted_until_real_verification(self):
        acceptance = read("Tests/PHASE9_ACCEPTANCE.md")
        self.assertIn("NOT ACCEPTED / NOT RUN", acceptance)
        self.assertIn("does **not** yet contain a concrete independent terrain/feature provider", acceptance)
        self.assertIn("**no respawn**", acceptance)
        self.assertIn("actual-browser gameplay", acceptance)


if __name__ == "__main__":
    unittest.main(verbosity=2)
