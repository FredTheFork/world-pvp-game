using System.Collections.Generic;
using System.Reflection;
using CesiumForUnity;
using NUnit.Framework;
using UnityEngine;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Gameplay;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Tests
{
    public sealed class FlatTestWorldGameplayDataSource : WorldGameplayDataSource
    {
        public GameplaySurfaceKind SurfaceKind = GameplaySurfaceKind.WalkableLand;
        public Vector3 GroundNormal = Vector3.up;
        public GameplayAreaAssessment Area = new GameplayAreaAssessment
        {
            HasData = true,
            IsWalkable = true,
            Confidence = 1f,
            FeatureKind = GameplayFeatureKind.Terrain
        };
        public readonly List<GameplayCollisionProxyDefinition> Proxies =
            new List<GameplayCollisionProxyDefinition>();

        public override bool HasCoverage(GeoPosition centre, double radiusMeters, out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public override bool TrySampleGround(GeoPosition position, out GameplayGroundSample sample)
        {
            sample = new GameplayGroundSample
            {
                HasData = true,
                EllipsoidAltitudeMeters = 0.0,
                NormalEastUpNorth = GroundNormal,
                SurfaceKind = SurfaceKind,
                Confidence = 1f
            };
            return true;
        }

        public override bool TryAssessArea(
            GeoPosition centre,
            float radiusMeters,
            out GameplayAreaAssessment assessment)
        {
            assessment = Area;
            return true;
        }

        public override void CollectCollisionProxies(
            GeoPosition centre,
            double radiusMeters,
            List<GameplayCollisionProxyDefinition> destination)
        {
            destination.AddRange(Proxies);
        }
    }

    public sealed class GameplayCollisionWorldTests
    {
        private GameObject georeferenceObject;
        private GameObject managerObject;
        private GameObject collisionObject;
        private CesiumGeoreference georeference;
        private GeospatialWorldManager manager;
        private GameplayCollisionWorld collisionWorld;
        private FlatTestWorldGameplayDataSource dataSource;
        private PhaseOneWorldSettings settings;
        private GeoPosition centre;

        [SetUp]
        public void SetUp()
        {
            centre = new GeoPosition(51.5073, -0.1657, 0.0);
            settings = ScriptableObject.CreateInstance<PhaseOneWorldSettings>();
            dataSource = ScriptableObject.CreateInstance<FlatTestWorldGameplayDataSource>();
            SetBaseField("providerId", "test-provider");
            SetBaseField("licenseAndTermsReference", "unit-test fixture only");
            SetBaseField("requiredAttribution", "test fixture");
            SetBaseField("legalReviewApproved", true);

            georeferenceObject = new GameObject("Gameplay collision test georeference");
            georeference = georeferenceObject.AddComponent<CesiumGeoreference>();
            georeference.Initialize();

            managerObject = new GameObject("Gameplay collision test manager");
            manager = managerObject.AddComponent<GeospatialWorldManager>();
            manager.ConfigureReferences(georeference, null, null, null, null, null, settings);
            Assert.That(manager.SetArenaDefinition(centre, 500.0), Is.True);

            LocalPosition proxyCentre = new LocalPosition(20.0, 2.0, 0.0);
            dataSource.Proxies.Add(new GameplayCollisionProxyDefinition
            {
                FeatureId = "test-wall-1",
                GeographicCentre = manager.LocalToGeographic(proxyCentre),
                SizeMeters = new Vector3(4f, 4f, 4f),
                YawDegrees = 0f,
                FeatureKind = GameplayFeatureKind.Wall,
                BlocksMovement = true,
                BlocksProjectiles = true
            });

            collisionObject = new GameObject("Gameplay collision test world");
            collisionWorld = collisionObject.AddComponent<GameplayCollisionWorld>();
            collisionWorld.ConfigureReferences(manager, settings, dataSource, false);
            string error;
            Assert.That(collisionWorld.ConfigureArena(centre, 500.0, false, out error), Is.True, error);
        }

        [TearDown]
        public void TearDown()
        {
            if (collisionObject != null) Object.DestroyImmediate(collisionObject);
            if (managerObject != null) Object.DestroyImmediate(managerObject);
            if (georeferenceObject != null) Object.DestroyImmediate(georeferenceObject);
            if (dataSource != null) Object.DestroyImmediate(dataSource);
            if (settings != null) Object.DestroyImmediate(settings);
        }

        [Test]
        public void ApprovedProviderBuildsStableProxyAndProductionReadyArena()
        {
            string error;
            Assert.That(collisionWorld.IsProductionReady(centre, 500.0, out error), Is.True, error);
            Assert.That(collisionWorld.HasIndependentData, Is.True);
            Assert.That(collisionWorld.CollisionProxyCount, Is.EqualTo(1));
            Assert.That(collisionWorld.IsPrototypeFallback, Is.False);
        }

        [Test]
        public void GoogleDerivedGameplayDataIsRejectedAndUnreviewedCombinationFailsClosed()
        {
            SetBaseField("derivedFromGoogleMapsContent", true);
            string error;
            Assert.That(collisionWorld.ConfigureArena(centre, 500.0, false, out error), Is.False);
            Assert.That(error, Does.Contain("derived from Google Maps Content"));

            SetBaseField("derivedFromGoogleMapsContent", false);
            SetBaseField("combinedWithGoogleMapsContent", true);
            SetBaseField("legalReviewApproved", false);
            Assert.That(collisionWorld.ConfigureArena(centre, 500.0, false, out error), Is.False);
            Assert.That(error, Does.Contain("explicit legal review"));
        }

        [Test]
        public void SafeSpawnAcceptsApprovedWalkableLandAwayFromBlockers()
        {
            LocalPosition grounded;
            string reason;
            Assert.That(collisionWorld.TryGetSafeSpawnFoot(
                new LocalPosition(100.0, 0.0, 100.0),
                0.30f,
                0.12f,
                48f,
                out grounded,
                out reason), Is.True, reason);
            Assert.That(grounded.IsFinite, Is.True);
        }

        [Test]
        public void SafeSpawnRejectsRoadsWaterUnknownSurfaceAndSteepGround()
        {
            LocalPosition candidate = new LocalPosition(100.0, 0.0, 100.0);
            LocalPosition grounded;
            string reason;

            dataSource.SurfaceKind = GameplaySurfaceKind.Road;
            Assert.That(collisionWorld.TryGetSafeSpawnFoot(candidate, 0.30f, 0.12f, 48f, out grounded, out reason), Is.False);

            dataSource.SurfaceKind = GameplaySurfaceKind.WalkableLand;
            dataSource.Area = new GameplayAreaAssessment
            {
                HasData = true,
                IsWalkable = true,
                Confidence = 1f,
                IsWater = true,
                FeatureKind = GameplayFeatureKind.Water
            };
            Assert.That(collisionWorld.TryGetSafeSpawnFoot(candidate, 0.30f, 0.12f, 48f, out grounded, out reason), Is.False);

            dataSource.Area = new GameplayAreaAssessment
            {
                HasData = false,
                IsWalkable = true,
                Confidence = 1f,
                FeatureKind = GameplayFeatureKind.Terrain
            };
            Assert.That(collisionWorld.TryGetSafeSpawnFoot(candidate, 0.30f, 0.12f, 48f, out grounded, out reason), Is.False);

            dataSource.Area = new GameplayAreaAssessment
            {
                HasData = true,
                IsWalkable = true,
                Confidence = 0.1f,
                FeatureKind = GameplayFeatureKind.Terrain
            };
            Assert.That(collisionWorld.TryGetSafeSpawnFoot(candidate, 0.30f, 0.12f, 48f, out grounded, out reason), Is.False);

            dataSource.Area = new GameplayAreaAssessment
            {
                HasData = true,
                IsWalkable = true,
                Confidence = 1f,
                FeatureKind = GameplayFeatureKind.Terrain
            };
            dataSource.GroundNormal = new Vector3(0.8660254f, 0.5f, 0f);
            Assert.That(collisionWorld.TryGetSafeSpawnFoot(candidate, 0.30f, 0.12f, 48f, out grounded, out reason), Is.False);
        }

        [Test]
        public void MovementSweepAndProjectileQueryUseTheStableProxyLayer()
        {
            LocalPosition spawnFoot;
            string rejection;
            Assert.That(collisionWorld.TryGetSafeSpawnFoot(
                new LocalPosition(20.0, 0.0, 0.0),
                0.30f,
                0.12f,
                48f,
                out spawnFoot,
                out rejection), Is.False);

            LocalPosition resolved;
            Assert.That(collisionWorld.TryResolveMovement(
                new LocalPosition(10.0, 0.0, 0.0),
                new LocalPosition(30.0, 0.0, 0.0),
                0.30f,
                1.8f,
                out resolved), Is.False);
            Assert.That(resolved.EastMeters, Is.LessThan(20.0));

            float hitDistance;
            GameplayFeatureKind featureKind;
            string featureId;
            Assert.That(collisionWorld.TryRaycast(
                new LocalPosition(10.0, 2.0, 0.0),
                Vector3.right,
                100f,
                out hitDistance,
                out featureKind,
                out featureId), Is.True);
            Assert.That(hitDistance, Is.EqualTo(8.0f).Within(0.05f));
            Assert.That(featureId, Is.EqualTo("test-wall-1"));
            Assert.That(featureKind, Is.EqualTo(GameplayFeatureKind.Wall));
        }

        private void SetBaseField(string fieldName, object value)
        {
            FieldInfo field = typeof(WorldGameplayDataSource).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing provider provenance field " + fieldName);
            field.SetValue(dataSource, value);
        }
    }
}
