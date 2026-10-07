using System;
using System.Collections.Generic;
using UnityEngine;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Gameplay
{
    public delegate bool SpawnSurfaceSampler(LocalPosition horizontalCandidate, out LocalPosition groundedFootPosition);

    [Serializable]
    public struct ArenaSpawnPoint
    {
        public int Index;
        public LocalPosition FootPosition;
        public float YawDegrees;

        public ArenaSpawnPoint(int index, LocalPosition footPosition, float yawDegrees)
        {
            Index = index;
            FootPosition = footPosition;
            YawDegrees = yawDegrees;
        }
    }

    /// <summary>Pure deterministic ENU arena sampler; all heights and checks are supplied by gameplay data.</summary>
    public static class GeographicArenaGenerator
    {
        private const double GoldenAngleRadians = 2.39996322972865332;
        private const int MaximumCandidateAttempts = 8192;

        /// <summary>
        /// Generates safe, metre-spaced positions around an arena instead of placing players at its
        /// centre. The callback must reject unknown, wet, blocked, restricted, or steep locations.
        /// </summary>
        public static bool TryGenerateSpawnPoints(
            LocalPosition arenaCentre,
            double arenaRadiusMeters,
            float characterRadiusMeters,
            float boundaryInsetMeters,
            float minimumSeparationMeters,
            int requestedPointCount,
            int deterministicSeed,
            SpawnSurfaceSampler sampleSafeSurface,
            out List<ArenaSpawnPoint> spawnPoints,
            out string error)
        {
            spawnPoints = new List<ArenaSpawnPoint>(Mathf.Max(0, requestedPointCount));
            error = string.Empty;
            if (!arenaCentre.IsFinite || !GeoPosition.IsFinite(arenaRadiusMeters) || arenaRadiusMeters <= 0.0 ||
                !IsFinite(characterRadiusMeters) || characterRadiusMeters <= 0f ||
                !IsFinite(boundaryInsetMeters) || boundaryInsetMeters < 0f ||
                !IsFinite(minimumSeparationMeters) || minimumSeparationMeters < 0f ||
                requestedPointCount < 1 || requestedPointCount > 256 || sampleSafeSurface == null)
            {
                error = "Arena spawn generation received invalid dimensions or no gameplay surface sampler.";
                return false;
            }

            double maximumRadius = arenaRadiusMeters - characterRadiusMeters - boundaryInsetMeters;
            if (maximumRadius < characterRadiusMeters * 2.0)
            {
                error = "The usable arena is too small for separated player spawns after applying the character and boundary margins.";
                return false;
            }

            double minimumRadius = Math.Min(
                maximumRadius * 0.24,
                Math.Max(5.0, characterRadiusMeters * 6.0));
            int attempts = Mathf.Clamp(requestedPointCount * 128, 256, MaximumCandidateAttempts);
            double angleOffset = ((uint)deterministicSeed / (double)uint.MaxValue) * Math.PI * 2.0;
            float requestedSpacing = minimumSeparationMeters;

            // The configured player separation is a hard minimum. If this radius/data set cannot
            // fit everyone safely, fail the plan rather than silently packing players closer.
            for (int spacingPass = 0; spacingPass < 1; spacingPass++)
            {
                spawnPoints.Clear();
                float spacing = requestedSpacing;
                for (int attempt = 0; attempt < attempts && spawnPoints.Count < requestedPointCount; attempt++)
                {
                    double radialPhase = ((attempt + 1.0) * 0.61803398874989485 +
                                          ((uint)deterministicSeed / (double)uint.MaxValue)) % 1.0;
                    double fraction = 0.08 + 0.92 * radialPhase;
                    double candidateRadius = minimumRadius +
                                             (maximumRadius - minimumRadius) * Math.Sqrt(fraction);
                    double angle = angleOffset + attempt * GoldenAngleRadians;
                    double eastOffset = Math.Cos(angle) * candidateRadius;
                    double northOffset = Math.Sin(angle) * candidateRadius;
                    LocalPosition candidate = new LocalPosition(
                        arenaCentre.EastMeters + eastOffset,
                        arenaCentre.UpMeters,
                        arenaCentre.NorthMeters + northOffset);

                    double arenaDistanceSquared = eastOffset * eastOffset + northOffset * northOffset;
                    if (arenaDistanceSquared > maximumRadius * maximumRadius)
                    {
                        continue;
                    }

                    LocalPosition grounded;
                    if (!sampleSafeSurface(candidate, out grounded) || !grounded.IsFinite)
                    {
                        continue;
                    }

                    bool tooClose = false;
                    double minimumSpacingSquared = spacing * spacing;
                    for (int existing = 0; existing < spawnPoints.Count; existing++)
                    {
                        double eastDelta = grounded.EastMeters - spawnPoints[existing].FootPosition.EastMeters;
                        double northDelta = grounded.NorthMeters - spawnPoints[existing].FootPosition.NorthMeters;
                        if (eastDelta * eastDelta + northDelta * northDelta < minimumSpacingSquared)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose)
                    {
                        continue;
                    }

                    float yaw = Mathf.Repeat(
                        Mathf.Atan2((float)-eastOffset, (float)-northOffset) * Mathf.Rad2Deg,
                        360f);
                    spawnPoints.Add(new ArenaSpawnPoint(spawnPoints.Count, grounded, yaw));
                }

                if (spawnPoints.Count == requestedPointCount)
                {
                    return true;
                }
            }

            int safeCandidateCount = spawnPoints.Count;
            spawnPoints.Clear();
            error = "Only " + safeCandidateCount + " safe, separated terrain positions were found; " +
                    requestedPointCount + " are required. The arena needs more verified gameplay coverage or a larger radius.";
            return false;
        }

        /// <summary>Stable seed shared by browsers from the same WGS84 arena definition.</summary>
        public static int CreateDeterministicSeed(GeoPosition centre, double radiusMeters)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                hash = Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(centre.LatitudeDegrees));
                hash = Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(centre.LongitudeDegrees));
                hash = Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(centre.AltitudeMeters));
                hash = Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(radiusMeters));
                return (int)(hash ^ (hash >> 32));
            }
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(value & 0xFFUL);
                    hash *= 1099511628211UL;
                    value >>= 8;
                }
                return hash;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
