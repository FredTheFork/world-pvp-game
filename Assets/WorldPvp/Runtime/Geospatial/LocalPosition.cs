using System;
using System.Globalization;
using UnityEngine;

namespace WorldPvp.Phase1.Geospatial
{
    /// <summary>
    /// A metre-based position in the selected world's fixed local tangent frame:
    /// X/East, Y/Up, Z/North (ENU). This is deliberately separate from GeoPosition.
    /// </summary>
    [Serializable]
    public struct LocalPosition : IEquatable<LocalPosition>
    {
        [SerializeField] private double eastMeters;
        [SerializeField] private double upMeters;
        [SerializeField] private double northMeters;

        public double EastMeters { get { return eastMeters; } }
        public double UpMeters { get { return upMeters; } }
        public double NorthMeters { get { return northMeters; } }

        // Familiar axis names, with ENU semantics made explicit in this type's contract.
        public double X { get { return eastMeters; } }
        public double Y { get { return upMeters; } }
        public double Z { get { return northMeters; } }

        public bool IsFinite
        {
            get
            {
                return IsFiniteValue(eastMeters) &&
                       IsFiniteValue(upMeters) &&
                       IsFiniteValue(northMeters);
            }
        }

        // A type cannot contain a property and a method with the same name,
        // so the per-component test is named separately from the IsFinite property.
        private static bool IsFiniteValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public LocalPosition(double xEastMeters, double yUpMeters, double zNorthMeters)
        {
            eastMeters = xEastMeters;
            upMeters = yUpMeters;
            northMeters = zNorthMeters;
        }

        public bool Equals(LocalPosition other)
        {
            return eastMeters.Equals(other.eastMeters) &&
                   upMeters.Equals(other.upMeters) &&
                   northMeters.Equals(other.northMeters);
        }

        public override bool Equals(object obj)
        {
            return obj is LocalPosition && Equals((LocalPosition)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + eastMeters.GetHashCode();
                hash = (hash * 31) + upMeters.GetHashCode();
                hash = (hash * 31) + northMeters.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "E {0:F2} m, U {1:F2} m, N {2:F2} m",
                eastMeters,
                upMeters,
                northMeters);
        }
    }
}
