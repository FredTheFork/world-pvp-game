using CesiumForUnity;
using NUnit.Framework;
using UnityEngine;
using WorldPvp.Phase1.Configuration;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Tests
{
    public sealed class GeospatialWorldManagerArenaTests
    {
        private GameObject georeferenceObject;
        private GameObject managerObject;
        private CesiumGeoreference georeference;
        private GeospatialWorldManager manager;
        private PhaseOneWorldSettings settings;

        [SetUp]
        public void SetUp()
        {
            georeferenceObject = new GameObject("Test Cesium Georeference");
            georeference = georeferenceObject.AddComponent<CesiumGeoreference>();
            georeference.Initialize();

            managerObject = new GameObject("Test Geospatial World Manager");
            manager = managerObject.AddComponent<GeospatialWorldManager>();
            settings = ScriptableObject.CreateInstance<PhaseOneWorldSettings>();
            manager.ConfigureReferences(
                georeference,
                null,
                null,
                null,
                null,
                null,
                settings);
        }

        [TearDown]
        public void TearDown()
        {
            if (managerObject != null) Object.DestroyImmediate(managerObject);
            if (georeferenceObject != null) Object.DestroyImmediate(georeferenceObject);
            if (settings != null) Object.DestroyImmediate(settings);
        }

        [TestCase(51.5073, -0.1657, 100.0)]
        [TestCase(51.5073, -0.1657, 500.0)]
        [TestCase(51.5073, -0.1657, 1000.0)]
        [TestCase(51.6030, -0.4840, 100.0)]
        [TestCase(51.6030, -0.4840, 500.0)]
        [TestCase(51.6030, -0.4840, 1000.0)]
        public void EnuRoundTripAndArenaMembershipWorkAtDistinctOriginsAndTargetRadii(
            double latitude,
            double longitude,
            double radiusMeters)
        {
            GeoPosition centre = new GeoPosition(latitude, longitude, 0.0);
            Assert.That(manager.SetArenaDefinition(centre, radiusMeters), Is.True);
            Assert.That(manager.WorldOrigin.Equals(centre), Is.True);
            Assert.That(manager.GameplayRadiusMeters, Is.EqualTo(radiusMeters));

            LocalPosition sample = new LocalPosition(31.25, 2.5, -47.75);
            GeoPosition geographic = manager.LocalToGeographic(sample);
            LocalPosition roundTrip = manager.GeographicToLocal(geographic);
            Assert.That(roundTrip.EastMeters, Is.EqualTo(sample.EastMeters).Within(0.01));
            Assert.That(roundTrip.UpMeters, Is.EqualTo(sample.UpMeters).Within(0.01));
            Assert.That(roundTrip.NorthMeters, Is.EqualTo(sample.NorthMeters).Within(0.01));

            LocalPosition justInside = new LocalPosition(radiusMeters - 0.1, 0.0, 0.0);
            LocalPosition justOutside = new LocalPosition(radiusMeters + 0.1, 0.0, 0.0);
            Assert.That(manager.IsInsideArena(manager.LocalToGeographic(justInside)), Is.True);
            Assert.That(manager.IsInsideArena(manager.LocalToGeographic(justOutside)), Is.False);
            Assert.That(manager.IsInsideArena(manager.LocalToGeographic(
                new LocalPosition(0.0, 0.0, radiusMeters - 0.1))), Is.True);
            Assert.That(manager.IsInsideArena(manager.LocalToGeographic(
                new LocalPosition(0.0, 0.0, radiusMeters + 0.1))), Is.False);
            Assert.That(
                manager.GetDistanceFromArenaCentre(manager.LocalToGeographic(justOutside)),
                Is.EqualTo(radiusMeters + 0.1).Within(0.01));
        }

        [Test]
        public void RenderExtentAddsVisibilityAndPreloadBuffersOutsideGameplayRadius()
        {
            Assert.That(manager.SetArenaDefinition(new GeoPosition(51.5073, -0.1657, 0.0), 500.0), Is.True);
            Assert.That(manager.VisibilityBufferMeters, Is.EqualTo(100.0));
            Assert.That(manager.PreloadBufferMeters, Is.EqualTo(150.0));
            Assert.That(manager.VisibilityRadiusMeters, Is.EqualTo(600.0));
            Assert.That(manager.RenderRadiusMeters, Is.EqualTo(750.0));
        }

        [Test]
        public void FixedArenaLocalFrameSurvivesCesiumRenderOriginShift()
        {
            GeoPosition centre = new GeoPosition(51.5073, -0.1657, 0.0);
            Assert.That(manager.SetArenaDefinition(centre, 1000.0), Is.True);
            LocalPosition expected = new LocalPosition(725.0, 3.0, -210.0);
            GeoPosition geographic = manager.LocalToGeographic(expected);

            // Simulate CesiumOriginShift replacing the current rendering/georeference origin.
            georeference.SetOriginLongitudeLatitudeHeight(-0.4840, 51.6030, 15.0);

            LocalPosition afterShift = manager.GeographicToLocal(geographic);
            Assert.That(afterShift.EastMeters, Is.EqualTo(expected.EastMeters).Within(0.01));
            Assert.That(afterShift.UpMeters, Is.EqualTo(expected.UpMeters).Within(0.01));
            Assert.That(afterShift.NorthMeters, Is.EqualTo(expected.NorthMeters).Within(0.01));
            Assert.That(manager.WorldOrigin.Equals(centre), Is.True);

            Vector3 shiftedUnityPosition = manager.GeographicToUnityWorld(geographic);
            GeoPosition convertedBack = manager.UnityWorldToGeographic(shiftedUnityPosition);
            Assert.That(convertedBack.LatitudeDegrees, Is.EqualTo(geographic.LatitudeDegrees).Within(0.000001));
            Assert.That(convertedBack.LongitudeDegrees, Is.EqualTo(geographic.LongitudeDegrees).Within(0.000001));
            Assert.That(convertedBack.AltitudeMeters, Is.EqualTo(geographic.AltitudeMeters).Within(0.01));
        }
    }
}
