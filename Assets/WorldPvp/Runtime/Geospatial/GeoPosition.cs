using System;
using System.Globalization;
using UnityEngine;

namespace WorldPvp.Phase1.Geospatial
{
    /// <summary>
    /// WGS84 geographic position: latitude/longitude in degrees and altitude in metres
    /// above the reference ellipsoid. This is not a Unity/local position.
    /// </summary>
    [Serializable]
    public struct GeoPosition : IEquatable<GeoPosition>
    {
        [SerializeField] private double latitudeDegrees;
        [SerializeField] private double longitudeDegrees;
        [SerializeField] private double altitudeMeters;

        public double LatitudeDegrees { get { return latitudeDegrees; } }
        public double LongitudeDegrees { get { return longitudeDegrees; } }
        /// <summary>Height above the WGS84 ellipsoid, not terrain or mean sea level.</summary>
        public double AltitudeMeters { get { return altitudeMeters; } }

        public bool IsValid
        {
            get
            {
                return IsFinite(latitudeDegrees) &&
                       IsFinite(longitudeDegrees) &&
                       IsFinite(altitudeMeters) &&
                       latitudeDegrees >= -90.0 && latitudeDegrees <= 90.0 &&
                       longitudeDegrees >= -180.0 && longitudeDegrees <= 180.0;
            }
        }

        public GeoPosition(double latitudeDegrees, double longitudeDegrees, double altitudeMeters)
        {
            this.latitudeDegrees = latitudeDegrees;
            this.longitudeDegrees = longitudeDegrees;
            this.altitudeMeters = altitudeMeters;
        }

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public bool Equals(GeoPosition other)
        {
            return latitudeDegrees.Equals(other.latitudeDegrees) &&
                   longitudeDegrees.Equals(other.longitudeDegrees) &&
                   altitudeMeters.Equals(other.altitudeMeters);
        }

        public override bool Equals(object obj)
        {
            return obj is GeoPosition && Equals((GeoPosition)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + latitudeDegrees.GetHashCode();
                hash = (hash * 31) + longitudeDegrees.GetHashCode();
                hash = (hash * 31) + altitudeMeters.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:F6}, {1:F6}, {2:F1} m ellipsoid",
                latitudeDegrees,
                longitudeDegrees,
                altitudeMeters);
        }
    }
}
