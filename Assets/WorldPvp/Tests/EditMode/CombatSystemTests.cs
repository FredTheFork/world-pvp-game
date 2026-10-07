using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using WorldPvp.Phase1.Combat;

namespace WorldPvp.Phase1.Tests
{
    public sealed class CombatSystemTests
    {
        private WeaponDefinition definition;

        [SetUp]
        public void SetUp()
        {
            definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            ConfigureDefinition(definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (definition != null)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void WeaponDefinition_UsesDataDrivenStatsAndEnforcesHeadTorsoLimbOrder()
        {
            Assert.IsTrue(definition.IsValid);
            Assert.AreEqual(34, definition.DamageForZone(CombatHitboxZone.Torso));
            Assert.AreEqual(68, definition.DamageForZone(CombatHitboxZone.Head));
            Assert.AreEqual(22, definition.DamageForZone(CombatHitboxZone.LeftArm));
            Assert.AreEqual(22, definition.DamageForZone(CombatHitboxZone.RightArm));
            Assert.AreEqual(22, definition.DamageForZone(CombatHitboxZone.LeftLeg));
            Assert.AreEqual(22, definition.DamageForZone(CombatHitboxZone.RightLeg));

            SetPrivateField(definition, "headDamageMultiplier", 1f);
            Assert.IsFalse(definition.IsValid, "Head damage must exceed torso damage.");

            SetPrivateField(definition, "headDamageMultiplier", 2f);
            SetPrivateField(definition, "limbDamageMultiplier", 1f);
            Assert.IsFalse(definition.IsValid, "Limb damage must be below torso damage.");
        }

        [Test]
        public void Weapon_ServerMagazineRateAndReloadAreBoundedByDefinition()
        {
            GameObject owner = new GameObject("WeaponTestOwner");
            try
            {
                Weapon weapon = owner.AddComponent<Weapon>();
                weapon.ConfigureDefinition(definition);
                weapon.InitializeServerState();

                Assert.AreEqual(30, weapon.MagazineAmmo);
                Assert.IsTrue(weapon.ServerTryConsumeRound(10.0));
                Assert.IsFalse(weapon.ServerTryConsumeRound(10.1), "Fire rate must be enforced on the server.");
                Assert.IsTrue(weapon.ServerTryConsumeRound(10.2));
                Assert.AreEqual(28, weapon.MagazineAmmo);

                Assert.IsTrue(weapon.ServerTryBeginReload(11.0));
                Assert.IsTrue(weapon.IsReloading);
                Assert.IsFalse(weapon.ServerTryConsumeRound(12.0), "Reloading blocks firing.");
                Assert.IsFalse(weapon.ServerTick(13.0), "A reload cannot complete early.");
                Assert.IsTrue(weapon.ServerTick(13.2));
                Assert.IsFalse(weapon.IsReloading);
                Assert.AreEqual(30, weapon.MagazineAmmo);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void FireCommand_ContainsIntentButNoTargetOrClaimedOutcome()
        {
            FieldInfo[] fields = typeof(FireCommand).GetFields(BindingFlags.Instance | BindingFlags.Public);
            string[] fieldNames = Array.ConvertAll(fields, field => field.Name);
            CollectionAssert.AreEquivalent(new[]
            {
                "Tick",
                "OriginEastMeters",
                "OriginUpMeters",
                "OriginNorthMeters",
                "DirectionEast",
                "DirectionUp",
                "DirectionNorth",
                "WeaponId"
            }, fieldNames);
            foreach (FieldInfo field in fields)
            {
                string name = field.Name.ToLowerInvariant();
                Assert.IsFalse(name.Contains("target"), "FireCommand must not name a hit target.");
                Assert.IsFalse(name.Contains("hit"), "FireCommand must not claim a hit result.");
                Assert.IsFalse(name.Contains("damage"), "FireCommand must not claim damage.");
            }
        }

        [Test]
        public void CombatAimMath_NormalizesAndRejectsImplausibleDirections()
        {
            Vector3 east = CombatMath.AimDirectionFromYawPitch(90f, 0f);
            Assert.That(east.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(east.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(east.z, Is.EqualTo(0f).Within(0.0001f));

            Vector3 pitchedDown = CombatMath.AimDirectionFromYawPitch(0f, 30f);
            Assert.That(pitchedDown.y, Is.EqualTo(-0.5f).Within(0.0001f));
            Assert.IsTrue(CombatMath.IsDirectionWithinTolerance(
                Quaternion.Euler(0f, 8f, 0f) * Vector3.forward,
                Vector3.forward,
                9f));
            Assert.IsFalse(CombatMath.IsDirectionWithinTolerance(
                Quaternion.Euler(0f, 20f, 0f) * Vector3.forward,
                Vector3.forward,
                9f));
            Assert.IsFalse(CombatMath.TryNormalizeDirection(Vector3.zero, out Vector3 ignored));
            Assert.IsFalse(CombatMath.TryNormalizeDirection(
                new Vector3(float.NaN, 0f, 1f), out ignored));
            Assert.IsFalse(CombatMath.TryNormalizeDirection(new Vector3(0f, 0f, 2f), out ignored));
        }

        [Test]
        public void CombatSpread_IsDeterministicAndStaysInsideAssetCone()
        {
            Vector3 forward = Vector3.forward;
            Vector3 first = CombatMath.ApplySpread(forward, 8f, 123456u);
            Vector3 repeat = CombatMath.ApplySpread(forward, 8f, 123456u);
            Assert.That(Vector3.Distance(first, repeat), Is.LessThan(0.000001f));
            Assert.That(Vector3.Angle(forward, first), Is.LessThanOrEqualTo(8.01f));
        }

        private static void ConfigureDefinition(WeaponDefinition target)
        {
            SetPrivateField(target, "weaponId", "test_rifle");
            SetPrivateField(target, "displayName", "Test Rifle");
            SetPrivateField(target, "fireRateShotsPerSecond", 5f);
            SetPrivateField(target, "damage", 34f);
            SetPrivateField(target, "rangeMeters", 150f);
            SetPrivateField(target, "spreadDegrees", 0.45f);
            SetPrivateField(target, "magazineCapacity", 30);
            SetPrivateField(target, "reloadTimeSeconds", 2.1f);
            SetPrivateField(target, "headDamageMultiplier", 2f);
            SetPrivateField(target, "torsoDamageMultiplier", 1f);
            SetPrivateField(target, "limbDamageMultiplier", 0.65f);
        }

        private static void SetPrivateField<T>(WeaponDefinition target, string fieldName, T value)
        {
            FieldInfo field = typeof(WeaponDefinition).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing serialized weapon field: " + fieldName);
            field.SetValue(target, value);
        }
    }
}
