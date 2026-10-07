using NUnit.Framework;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Tests
{
    public sealed class GeoPositionTests
    {
        [Test]
        public void ValidWgs84PositionIsAccepted()
        {
            GeoPosition position = new GeoPosition(51.5007, -0.1246, 0.0);
            Assert.That(position.IsValid, Is.True);
            Assert.That(position.LatitudeDegrees, Is.EqualTo(51.5007));
            Assert.That(position.LongitudeDegrees, Is.EqualTo(-0.1246));
            Assert.That(position.AltitudeMeters, Is.EqualTo(0.0));
        }

        [TestCase(-90.0, -180.0)]
        [TestCase(90.0, 180.0)]
        public void InclusiveGeographicBoundariesAreAccepted(double latitude, double longitude)
        {
            Assert.That(new GeoPosition(latitude, longitude, -500.0).IsValid, Is.True);
        }

        [TestCase(-90.0001, 0.0)]
        [TestCase(90.0001, 0.0)]
        [TestCase(0.0, -180.0001)]
        [TestCase(0.0, 180.0001)]
        public void OutOfRangeLatitudeOrLongitudeIsRejected(double latitude, double longitude)
        {
            Assert.That(new GeoPosition(latitude, longitude, 0.0).IsValid, Is.False);
        }

        [Test]
        public void NonFiniteGeographicValuesAreRejected()
        {
            Assert.That(new GeoPosition(double.NaN, 0.0, 0.0).IsValid, Is.False);
            Assert.That(new GeoPosition(0.0, double.PositiveInfinity, 0.0).IsValid, Is.False);
            Assert.That(new GeoPosition(0.0, 0.0, double.NegativeInfinity).IsValid, Is.False);
        }

        [Test]
        public void LocalPositionNamesTheEnuAxesAndRejectsNonFiniteValues()
        {
            LocalPosition position = new LocalPosition(10.0, 2.0, -5.0);
            Assert.That(position.X, Is.EqualTo(10.0));
            Assert.That(position.Y, Is.EqualTo(2.0));
            Assert.That(position.Z, Is.EqualTo(-5.0));
            Assert.That(position.EastMeters, Is.EqualTo(position.X));
            Assert.That(position.UpMeters, Is.EqualTo(position.Y));
            Assert.That(position.NorthMeters, Is.EqualTo(position.Z));
            Assert.That(new LocalPosition(double.NaN, 0.0, 0.0).IsFinite, Is.False);
        }
    }
}
