using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Battles
{
    public sealed class WorldLocationValidationResult
    {
        public bool Succeeded { get; private set; }
        public string UserMessage { get; private set; }
        public string TechnicalDetail { get; private set; }

        private WorldLocationValidationResult(bool succeeded, string userMessage, string technicalDetail)
        {
            Succeeded = succeeded;
            UserMessage = userMessage ?? string.Empty;
            TechnicalDetail = technicalDetail ?? string.Empty;
        }

        public static WorldLocationValidationResult Pass(string message)
        {
            return new WorldLocationValidationResult(true, message, string.Empty);
        }

        public static WorldLocationValidationResult Fail(string userMessage, string technicalDetail = "")
        {
            return new WorldLocationValidationResult(false, userMessage, technicalDetail);
        }
    }

    /// <summary>
    /// Fail-closed preflight for a World PvP battle. Centre terrain must be available, a user must
    /// confirm the official Google Photorealistic 3D coverage map, and one batched Cesium height query
    /// must succeed at the centre and surrounding render-envelope samples. Google does not publish a
    /// machine-readable surface-coverage endpoint, so height samples alone are not treated as proof
    /// of Photorealistic surface coverage.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldLocationValidator : MonoBehaviour
    {
        public const string CoverageUnavailableMessage =
            "Photorealistic coverage unavailable here. Choose another location.";
        public const string UnsupportedRadiusMessage =
            "Arena radius must be greater than zero and no more than 2,000 metres.";

        private const int SamplesPerRing = 8;
        private const int SampleAttempts = 2;
        private const int SampleTimeoutSeconds = 45;
        private const double InnerRingFraction = 0.5;

        [SerializeField] private GeospatialWorldManager worldManager;

        public void Configure(GeospatialWorldManager manager)
        {
            worldManager = manager;
        }

        public static bool IsSupportedRadius(double radiusMeters)
        {
            return MatchSession.IsSupportedRadius(radiusMeters);
        }

        public async Task<WorldLocationValidationResult> ValidateAsync(
            GeoPosition centre,
            double radiusMeters,
            bool photorealisticCoverageConfirmed)
        {
            if (!centre.IsValid)
            {
                return WorldLocationValidationResult.Fail(
                    "Enter a valid WGS84 latitude, longitude, and ellipsoid altitude.");
            }

            if (!IsSupportedRadius(radiusMeters))
            {
                return WorldLocationValidationResult.Fail(UnsupportedRadiusMessage);
            }

            if (!photorealisticCoverageConfirmed)
            {
                return WorldLocationValidationResult.Fail(
                    CoverageUnavailableMessage,
                    "The official visual coverage confirmation has not been provided.");
            }

            if (worldManager == null || worldManager.Tileset == null || !worldManager.IsReady ||
                !worldManager.HasArenaGroundPosition)
            {
                return WorldLocationValidationResult.Fail(
                    CoverageUnavailableMessage,
                    "Cesium terrain collision is not available at the arena centre.");
            }

            if (!worldManager.ArenaCentre.Equals(centre) || worldManager.GameplayRadiusMeters != radiusMeters)
            {
                return WorldLocationValidationResult.Fail(
                    "The loaded geographic arena does not match the proposed match centre and radius.",
                    "Arena centre/radius exact-value comparison failed.");
            }

            Cesium3DTileset tileset = worldManager.Tileset;
            if (!tileset.isActiveAndEnabled)
            {
                return WorldLocationValidationResult.Fail(
                    CoverageUnavailableMessage,
                    "The Google Photorealistic 3D Tileset is not active.");
            }

            double3[] samples;
            try
            {
                samples = BuildSurroundingSamples(worldManager, centre);
            }
            catch (Exception exception)
            {
                return WorldLocationValidationResult.Fail(
                    CoverageUnavailableMessage,
                    "Could not build the metre-based surrounding sample set (" + exception.GetType().Name + ").");
            }

            for (int attempt = 0; attempt < SampleAttempts; attempt++)
            {
                Task<CesiumSampleHeightResult> sampleTask;
                try
                {
                    sampleTask = tileset.SampleHeightMostDetailed(samples);
                }
                catch (Exception exception)
                {
                    return WorldLocationValidationResult.Fail(
                        CoverageUnavailableMessage,
                        "Cesium height sampling could not start (" + exception.GetType().Name + ").");
                }

                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(SampleTimeoutSeconds));
                Task completed = await Task.WhenAny(sampleTask, timeoutTask);
                if (completed != sampleTask)
                {
                    return WorldLocationValidationResult.Fail(
                        CoverageUnavailableMessage,
                        "Surrounding Cesium height sampling timed out.");
                }

                CesiumSampleHeightResult result;
                try
                {
                    result = await sampleTask;
                }
                catch (Exception exception)
                {
                    return WorldLocationValidationResult.Fail(
                        CoverageUnavailableMessage,
                        "Cesium height sampling failed (" + exception.GetType().Name + ").");
                }

                if (AllSamplesSucceeded(result, samples.Length))
                {
                    return WorldLocationValidationResult.Pass(
                        "Coverage checks passed. Terrain is available and the official photorealistic coverage map was confirmed.");
                }

                if (attempt + 1 < SampleAttempts)
                {
                    // A second batched query lets just-requested surrounding tiles finish loading.
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }

            return WorldLocationValidationResult.Fail(
                CoverageUnavailableMessage,
                "One or more centre / surrounding Cesium height samples were unavailable.");
        }

        private static double3[] BuildSurroundingSamples(
            GeospatialWorldManager manager,
            GeoPosition centre)
        {
            List<double3> samples = new List<double3>(1 + (2 * SamplesPerRing));
            samples.Add(new double3(
                centre.LongitudeDegrees,
                centre.LatitudeDegrees,
                centre.AltitudeMeters));

            double[] ringFractions = { InnerRingFraction, 1.0 };
            for (int ring = 0; ring < ringFractions.Length; ring++)
            {
                double radius = manager.RenderRadiusMeters * ringFractions[ring];
                for (int i = 0; i < SamplesPerRing; i++)
                {
                    double angle = (2.0 * Math.PI * i) / SamplesPerRing;
                    double eastMeters = Math.Cos(angle) * radius;
                    double northMeters = Math.Sin(angle) * radius;
                    GeoPosition point = manager.LocalToGeographic(
                        new LocalPosition(eastMeters, 0.0, northMeters));
                    samples.Add(new double3(
                        point.LongitudeDegrees,
                        point.LatitudeDegrees,
                        centre.AltitudeMeters));
                }
            }

            return samples.ToArray();
        }

        private static bool AllSamplesSucceeded(CesiumSampleHeightResult result, int expectedCount)
        {
            if (result == null || result.sampleSuccess == null || result.sampleSuccess.Length != expectedCount)
            {
                return false;
            }

            for (int i = 0; i < result.sampleSuccess.Length; i++)
            {
                if (!result.sampleSuccess[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
