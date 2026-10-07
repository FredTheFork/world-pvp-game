#!/usr/bin/env python3
"""Unity-independent static checks for Phase 7 combat source and integration boundaries."""
from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


class PhaseSevenStaticChecks(unittest.TestCase):
    def test_pinned_engine_and_direct_dependency_versions_are_unchanged(self) -> None:
        manifest = json.loads(read("Packages/manifest.json"))
        self.assertEqual(manifest["dependencies"].get("com.cesium.unity"), "1.26.0")
        self.assertIn("6000.3.24f1", read("ProjectSettings/ProjectVersion.txt"))
        self.assertIn("no package upgrades", read("README.md"))

    def test_weapon_stats_and_zone_scaling_are_asset_backed(self) -> None:
        definition = read("Assets/WorldPvp/Runtime/Combat/WeaponDefinition.cs")
        for field in (
            "weaponId",
            "fireRateShotsPerSecond",
            "damage",
            "rangeMeters",
            "spreadDegrees",
            "magazineCapacity",
            "reloadTimeSeconds",
            "headDamageMultiplier",
            "torsoDamageMultiplier",
            "limbDamageMultiplier",
        ):
            with self.subTest(field=field):
                self.assertIn(f"SerializeField] private", definition)
                self.assertIn(field, definition)
        self.assertIn("headDamageMultiplier > torsoDamageMultiplier", definition)
        self.assertIn("limbDamageMultiplier < torsoDamageMultiplier", definition)
        self.assertIn("DamageForZone", definition)

        builder = read("Assets/WorldPvp/Editor/PhaseSevenCombatAssetBuilder.cs")
        for value in (
            '"fireRateShotsPerSecond", 5f',
            '"damage", 34f',
            '"rangeMeters", 150f',
            '"spreadDegrees", 0.45f',
            '"magazineCapacity", 30',
            '"reloadTimeSeconds", 2.1f',
            '"headDamageMultiplier", 2f',
            '"torsoDamageMultiplier", 1f',
            '"limbDamageMultiplier", 0.65f',
        ):
            with self.subTest(default=value):
                self.assertIn(value, builder)

    def test_fire_command_is_intent_only_and_serializes_tick_pose_and_weapon_id(self) -> None:
        models = read("Assets/WorldPvp/Runtime/Combat/CombatModels.cs")
        fire = re.search(r"public struct FireCommand\s*:\s*INetworkSerializable\s*\{(.*?)\n    \}", models, re.S)
        self.assertIsNotNone(fire)
        payload = fire.group(1)
        for field in (
            "Tick",
            "OriginEastMeters",
            "OriginUpMeters",
            "OriginNorthMeters",
            "DirectionEast",
            "DirectionUp",
            "DirectionNorth",
            "WeaponId",
        ):
            with self.subTest(field=field):
                self.assertIn(field, payload)
        field_names = re.findall(r"public\s+\w+\s+(\w+)\s*;", payload)
        for field_name in field_names:
            lowered_name = field_name.lower()
            self.assertFalse(any(token in lowered_name for token in ("target", "victim", "damage", "health", "hit")))
        self.assertEqual(set(field_names), {
            "Tick",
            "OriginEastMeters",
            "OriginUpMeters",
            "OriginNorthMeters",
            "DirectionEast",
            "DirectionUp",
            "DirectionNorth",
            "WeaponId",
        })

    def test_host_validates_fire_and_performs_its_own_raycast_and_damage(self) -> None:
        player = read("Assets/WorldPvp/Runtime/Battles/NetworkPlayer.cs")
        for requirement in (
            "[ServerRpc(RequireOwnership = true)]\n        private void SubmitFireCommandServerRpc",
            "senderClientId != OwnerClientId",
            "serverState.Alive",
            "serverState.Health == 0",
            "serverState.MatchFinished",
            "matchController.IsServerMatchActive",
            "command.WeaponId.ToString()",
            "IsSequenceNewer(command.Tick, serverState.LastProcessedInputTick)",
            "MaximumUncompensatedFireRpcGapSeconds",
            "MaximumFireOriginDeviationMeters",
            "MaximumFireAimDeviationDegrees",
            "weapon.ServerTryConsumeRound(acceptedInput.ReceivedAtServerTime, out fireFailure)",
            "LagCompensationMath.TryIntersectPlayer(",
            "TryRaycast(",
            "Physics.RaycastNonAlloc(",
            "weapon.Definition.RangeMeters",
            "weapon.Definition.DamageForZone(hitboxZone)",
            "victim.ServerApplyDamage(damage",
            "BroadcastCombatEventClientRpc(shotEvent)",
        ):
            with self.subTest(requirement=requirement):
                self.assertIn(requirement, player)
        self.assertIn("QueryTriggerInteraction.Ignore", player)
        build_start = player.index("private FireCommand BuildFireCommand")
        build_end = player.index("private void ResolveServerFireCommand", build_start)
        client_fire_builder = player[build_start:build_end]
        self.assertIn("CombatMath.TryNormalizeDirection(direction, out direction)", client_fire_builder)
        self.assertIn("DirectionEast = direction.x", client_fire_builder)
        self.assertIn("No shot was submitted", player)

    def test_server_hitboxes_cover_all_six_zones_and_death_is_host_owned(self) -> None:
        player = read("Assets/WorldPvp/Runtime/Battles/NetworkPlayer.cs")
        hitbox_math = read("Assets/WorldPvp/Runtime/Combat/LagCompensationMath.cs")
        for zone in ("Head", "Torso", "LeftArm", "RightArm", "LeftLeg", "RightLeg"):
            with self.subTest(zone=zone):
                self.assertIn(f"CombatHitboxZone.{zone}", hitbox_math)
        self.assertIn("if (!IsServer || !serverInitialized", player)
        self.assertIn("serverState.Health = (ushort)health", player)
        self.assertIn("serverState.Alive = false", player)
        self.assertIn("serverState.MovementState = NetworkPlayerMovementState.Dead", player)
        self.assertIn("CombatEventType.PlayerDied", player)
        self.assertIn("!serverState.Alive || serverState.Health == 0", player)
        self.assertNotIn("Respawn", player)

    def test_eliminated_players_spectate_and_round_freezes_without_respawn(self) -> None:
        match = read("Assets/WorldPvp/Runtime/Combat/CombatMatchController.cs")
        hud = read("Assets/WorldPvp/Runtime/UI/PhaseSevenCombatHud.cs")
        motor = read("Assets/WorldPvp/Runtime/Player/PhaseOneTestCharacterController.cs")
        self.assertIn("finishedByLastSurvivor", match)
        self.assertIn("finishedByDisconnect", match)
        self.assertIn("participants.Count != manager.ConnectedClientsList.Count", match)
        self.assertIn("OnClientConnectedCallback", match)
        self.assertIn("OnClientDisconnectCallback", match)
        self.assertIn("serverMatchEnded = true", match)
        self.assertIn("player.ServerSetMatchFinished()", match)
        self.assertIn("CombatEventType.MatchEnded", match)
        self.assertIn("EnterSpectatorCamera()", hud)
        self.assertIn("CycleSpectatorTarget()", hud)
        self.assertIn("tabKey.wasPressedThisFrame", hud)
        self.assertIn("SetSpectatorCameraDetached(true)", hud)
        self.assertIn("SetSpectatorCameraPose", motor)
        self.assertNotIn("Respawn", match + hud + motor)

    def test_client_feedback_is_event_driven_and_runtime_hud_states_authority(self) -> None:
        hud = read("Assets/WorldPvp/Runtime/UI/PhaseSevenCombatHud.cs")
        self.assertIn("NetworkPlayer.CombatEventReceived += OnCombatEvent", hud)
        self.assertIn("combatEvent.Type == CombatEventType.ShotResolved", hud)
        self.assertIn("combatEvent.ShooterId == localId", hud)
        self.assertIn("combatEvent.VictimId == localId", hud)
        self.assertIn("SpawnEffect", hud)
        self.assertIn("PlaySound", hud)
        self.assertIn("ELIMINATED  ·  SPECTATOR MODE", hud)
        self.assertIn("MATCH COMPLETE", hud)

        live_hud = read("Assets/WorldPvp/Runtime/UI/PhaseThreeRuntimeHud.cs")
        self.assertIn("!localPlayer.CurrentState.MatchFinished", live_hud)
        self.assertIn("alone decides hits, damage, ammo, and death", live_hud)
        self.assertNotIn("The fire bit is future-facing only", live_hud)

    def test_scene_builder_generates_asset_wires_weapon_and_phase7_components(self) -> None:
        builder = read("Assets/WorldPvp/Editor/PhaseOneSceneBuilder.cs")
        self.assertIn("PhaseSevenCombatAssetBuilder.LoadOrCreatePrototypeRifle()", builder)
        self.assertIn("LoadOrCreateSessionPlayerPrefab(prototypeRifle)", builder)
        self.assertIn("temporaryPlayer.AddComponent<Weapon>()", builder)
        self.assertIn("weapon.ConfigureDefinition(defaultWeapon)", builder)
        self.assertIn("AddComponent<CombatMatchController>()", builder)
        self.assertIn("AddComponent<PhaseSevenCombatHud>()", builder)
        self.assertIn("Tools/World PvP/Phase 7/Build or Rebuild Combat Scene", builder)

    def test_phase7_tests_and_docs_keep_unverified_runtime_status_honest(self) -> None:
        test_source = read("Assets/WorldPvp/Tests/EditMode/CombatSystemTests.cs")
        for test in (
            "WeaponDefinition_UsesDataDrivenStats",
            "Weapon_ServerMagazineRateAndReloadAreBoundedByDefinition",
            "FireCommand_ContainsIntentButNoTargetOrClaimedOutcome",
            "CombatAimMath_NormalizesAndRejectsImplausibleDirections",
            "CombatSpread_IsDeterministicAndStaysInsideAssetCone",
        ):
            with self.subTest(test=test):
                self.assertIn(test, test_source)

        acceptance = read("Tests/PHASE7_ACCEPTANCE.md")
        architecture = read("Documentation/ARCHITECTURE_PHASE7.md")
        for doc in (acceptance, architecture):
            self.assertIn("NOT RUN", doc)
            self.assertTrue(
                "not a dedicated server" in doc or "not a trusted dedicated server" in doc
            )
        self.assertIn("Phase 3 two-tab", acceptance)
        self.assertIn("Phase 4 five-browser", acceptance)
        self.assertIn("actual browser instances", acceptance)
        self.assertIn("client-observed estimate", read("README.md"))

    def test_project_assembly_definitions_and_manifest_remain_valid(self) -> None:
        for path in (
            "Assets/WorldPvp/Runtime/WorldPvp.Phase1.asmdef",
            "Assets/WorldPvp/Tests/EditMode/WorldPvp.Phase1.Tests.asmdef",
            "Packages/manifest.json",
        ):
            with self.subTest(path=path):
                json.loads(read(path))


if __name__ == "__main__":
    unittest.main(verbosity=2)
