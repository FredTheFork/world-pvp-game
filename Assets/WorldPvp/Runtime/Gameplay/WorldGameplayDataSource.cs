using System;
using System.Collections.Generic;
using UnityEngine;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Gameplay
{
    public enum GameplaySurfaceKind : byte
    {
        Unknown,
        WalkableLand,
        Road,
        Park,
        Water,
        Building,
        Cliff,
        Restricted
    }

    public enum GameplayFeatureKind : byte
    {
        Building,
        Wall,
        Road,
        Park,
        Water,
        Landmark,
        District,
        RestrictedArea,
        Terrain
    }

    /// <summary>Independent gameplay terrain metadata; altitude is WGS84 ellipsoid metres.</summary>
    [Serializable]
    public struct GameplayGroundSample
    {
        public bool HasData;
        public double EllipsoidAltitudeMeters;
        public Vector3 NormalEastUpNorth;
        public GameplaySurfaceKind SurfaceKind;
        [Range(0f, 1f)] public float Confidence;

        public bool IsValid
        {
            get
            {
                return HasData && GeoPosition.IsFinite(EllipsoidAltitudeMeters) &&
                       IsFinite(NormalEastUpNorth) && NormalEastUpNorth.sqrMagnitude > 0.5f &&
                       IsFinite(Confidence) && Confidence >= 0f && Confidence <= 1f;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>Semantic area response used for safe-spawn and collision decisions.</summary>
    [Serializable]
    public struct GameplayAreaAssessment
    {
        public bool HasData;
        public bool IsWalkable;
        public bool IsWater;
        public bool IsBuilding;
        public bool IsRestricted;
        public bool IsRoad;
        public bool IsCliff;
        [Range(0f, 1f)] public float Confidence;
        public string FeatureId;
        public GameplayFeatureKind FeatureKind;
    }

    /// <summary>
    /// A persistent, simplified metre-sized box in geographic space. It is authored by an approved
    /// gameplay-data provider and is never extracted from the photorealistic renderer's mesh.
    /// </summary>
    [Serializable]
    public struct GameplayCollisionProxyDefinition
    {
        public string FeatureId;
        public GeoPosition GeographicCentre;
        public Vector3 SizeMeters;
        public float YawDegrees;
        public GameplayFeatureKind FeatureKind;
        public bool BlocksMovement;
        public bool BlocksProjectiles;

        public bool IsValid
        {
            get
            {
                return GeographicCentre.IsValid && IsFinite(SizeMeters.x) && IsFinite(SizeMeters.y) &&
                       IsFinite(SizeMeters.z) && SizeMeters.x > 0f && SizeMeters.y > 0f &&
                       SizeMeters.z > 0f && IsFinite(YawDegrees);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Plug-in boundary for separately licensed gameplay data. Implementations must not use Google
    /// Maps Content as an input, source a visual renderer's mesh, or infer persistent features from it.
    /// </summary>
    public abstract class WorldGameplayDataSource : ScriptableObject
    {
        [Header("Data provenance and required legal review")]
        [SerializeField] private string providerId = string.Empty;
        [SerializeField, TextArea(2, 5)] private string licenseAndTermsReference = string.Empty;
        [SerializeField, TextArea(2, 5)] private string requiredAttribution = string.Empty;
        [SerializeField] private bool legalReviewApproved;
        [Tooltip("Must remain false. Gameplay terrain, features, and collision proxies may not be derived from Google Maps Content.")]
        [SerializeField] private bool derivedFromGoogleMapsContent;
        [Tooltip("If true, the exact combined use requires explicit legal review approval and a terms reference.")]
        [SerializeField] private bool combinedWithGoogleMapsContent;

        public string ProviderId { get { return providerId; } }
        public string LicenseAndTermsReference { get { return licenseAndTermsReference; } }
        public string RequiredAttribution { get { return requiredAttribution; } }
        public bool LegalReviewApproved { get { return legalReviewApproved; } }
        public bool DerivedFromGoogleMapsContent { get { return derivedFromGoogleMapsContent; } }
        public bool CombinedWithGoogleMapsContent { get { return combinedWithGoogleMapsContent; } }

        /// <summary>Returns false when this provider does not cover the complete requested arena.</summary>
        public abstract bool HasCoverage(GeoPosition centre, double radiusMeters, out string reason);

        /// <summary>Samples stable terrain data from this provider, independent of the visual renderer.</summary>
        public abstract bool TrySampleGround(GeoPosition position, out GameplayGroundSample sample);

        /// <summary>Classifies a metre-radius area. Missing data must be reported, not treated as safe.</summary>
        public abstract bool TryAssessArea(
            GeoPosition centre,
            float radiusMeters,
            out GameplayAreaAssessment assessment);

        /// <summary>Collects simplified, stable blockers for the arena; do not return visual tile meshes.</summary>
        public abstract void CollectCollisionProxies(
            GeoPosition centre,
            double radiusMeters,
            List<GameplayCollisionProxyDefinition> destination);
    }
}
