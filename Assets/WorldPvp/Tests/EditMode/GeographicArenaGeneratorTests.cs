using System.Collections.Generic;
using NUnit.Framework;
using WorldPvp.Phase1.Gameplay;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Tests
{
    public sealed class GeographicArenaGeneratorTests
    {
        [Test]
        public void GeneratesDeterministicDistributedPointsInsideTheUsableVirtualArena()
        {
            GeoPosition centre = new GeoPosition(51.5073, -0.1657, 24.0);
            LocalPosition localCentre = new LocalPosition(0.0, 0.0, 0.0);
            int seed = GeographicArenaGenerator.CreateDeterministicSeed(centre, 500.0);
            List<ArenaSpawnPoint> first;
            List<ArenaSpawnPoint> second;
            string error;

            Assert.That(GeographicArenaGenerator.TryGenerateSpawnPoints(
                localCentre, 500.0, 0.30f, 2f, 18f, 8, seed, FlatSafeSurface,
                out first, out error), Is.True, error);
            Assert.That(GeographicArenaGenerator.TryGenerateSpawnPoints(
                localCentre, 500.0, 0.30f, 2f, 18f, 8, seed, FlatSafeSurface,
                out second, out error), Is.True, error);
            Assert.That(first.Count, Is.EqualTo(8));
            Assert.That(second.Count, Is.EqualTo(first.Count));

            double maximumUsableRadius = 500.0 - 0.30 - 2.0;
            int distributedBeyond200m = 0;
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(first[i].Index, Is.EqualTo(i));
                Assert.That(first[i].FootPosition.Equals(second[i].FootPosition), Is.True);
                Assert.That(first[i].YawDegrees, Is.EqualTo(second[i].YawDegrees));
                double radius = HorizontalRadius(first[i].FootPosition);
                Assert.That(radius, Is.GreaterThan(0.0), "No player is placed at the arena centre.");
                Assert.That(radius, Is.LessThanOrEqualTo(maximumUsableRadius + 0.001));
                if (radius >= 200.0)
                {
                    distributedBeyond200m++;
                }

                for (int j = 0; j < i; j++)
                {
                    Assert.That(HorizontalDistance(first[i].FootPosition, first[j].FootPosition),
                        Is.GreaterThanOrEqualTo(18.0));
                }
            }
            Assert.That(distributedBeyond200m, Is.GreaterThanOrEqualTo(3),
                "Spawn points should use a broad portion of the selected virtual arena.");
        }

        [Test]
        public void FailsClosedWhenNoSafeGameplaySurfaceIsAvailable()
        {
            List<ArenaSpawnPoint> points;
            string error;
            bool generated = GeographicArenaGenerator.TryGenerateSpawnPoints(
                new LocalPosition(0.0, 0.0, 0.0),
                250.0,
                0.30f,
                2f,
                18f,
                4,
                12345,
                NoSafeSurface,
                out points,
                out error);

            Assert.That(generated, Is.False);
            Assert.That(points, Is.Empty);
            Assert.That(error, Does.Contain("safe, separated terrain positions"));
        }

        [Test]
        public void RejectsArenaTooSmallForPlayerAndBoundaryMargins()
        {
            List<ArenaSpawnPoint> points;
            string error;
            bool generated = GeographicArenaGenerator.TryGenerateSpawnPoints(
                new LocalPosition(0.0, 0.0, 0.0),
                1.0,
                0.30f,
                2f,
                18f,
                2,
                7,
                FlatSafeSurface,
                out points,
                out error);

            Assert.That(generated, Is.False);
            Assert.That(points, Is.Empty);
            Assert.That(error, Does.Contain("too small"));
        }

        [Test]
        public void DeterministicSeedChangesWithExactCentreOrRadius()
        {
            GeoPosition first = new GeoPosition(51.5073, -0.1657, 24.0);
            GeoPosition moved = new GeoPosition(51.5074, -0.1657, 24.0);
            int seed = GeographicArenaGenerator.CreateDeterministicSeed(first, 500.0);

            Assert.That(GeographicArenaGenerator.CreateDeterministicSeed(first, 500.0), Is.EqualTo(seed));
            Assert.That(GeographicArenaGenerator.CreateDeterministicSeed(moved, 500.0), Is.Not.EqualTo(seed));
            Assert.That(GeographicArenaGenerator.CreateDeterministicSeed(first, 501.0), Is.Not.EqualTo(seed));
        }

        private static bool FlatSafeSurface(LocalPosition candidate, out LocalPosition grounded)
        {
            grounded = new LocalPosition(candidate.EastMeters, 1.0, candidate.NorthMeters);
            return true;
        }

        private static bool NoSafeSurface(LocalPosition candidate, out LocalPosition grounded)
        {
            grounded = default(LocalPosition);
            return false;
        }

        private static double HorizontalRadius(LocalPosition position)
        {
            return System.Math.Sqrt(
                position.EastMeters * position.EastMeters +
                position.NorthMeters * position.NorthMeters);
        }

        private static double HorizontalDistance(LocalPosition first, LocalPosition second)
        {
            double east = first.EastMeters - second.EastMeters;
            double north = first.NorthMeters - second.NorthMeters;
            return System.Math.Sqrt(east * east + north * north);
        }
    }
}
