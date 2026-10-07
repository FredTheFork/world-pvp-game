using UnityEngine;
using WorldPvp.Phase1.Geospatial;

namespace WorldPvp.Phase1.Combat
{
    /// <summary>Pure metre-space hitbox tests used by the authoritative shot-time evaluator.</summary>
    public static class LagCompensationMath
    {
        private struct HitboxTemplate
        {
            public CombatHitboxZone Zone;
            public float CentreX;
            public float CentreYFraction;
            public Vector3 HalfExtents;

            public HitboxTemplate(
                CombatHitboxZone zone,
                float centreX,
                float centreYFraction,
                Vector3 halfExtents)
            {
                Zone = zone;
                CentreX = centreX;
                CentreYFraction = centreYFraction;
                HalfExtents = halfExtents;
            }
        }

        // Dimensions mirror the server-side character hitbox proportions. Values are metres.
        private static readonly HitboxTemplate[] Hitboxes =
        {
            new HitboxTemplate(CombatHitboxZone.Head, 0f, 0.86f, new Vector3(0.17f, 0.17f, 0.17f)),
            new HitboxTemplate(CombatHitboxZone.Torso, 0f, 0.51f, new Vector3(0.29f, 0.39f, 0.20f)),
            new HitboxTemplate(CombatHitboxZone.LeftArm, -0.34f, 0.57f, new Vector3(0.12f, 0.42f, 0.12f)),
            new HitboxTemplate(CombatHitboxZone.RightArm, 0.34f, 0.57f, new Vector3(0.12f, 0.42f, 0.12f)),
            new HitboxTemplate(CombatHitboxZone.LeftLeg, -0.15f, 0.22f, new Vector3(0.13f, 0.39f, 0.15f)),
            new HitboxTemplate(CombatHitboxZone.RightLeg, 0.15f, 0.22f, new Vector3(0.13f, 0.39f, 0.15f))
        };

        /// <summary>
        /// Intersect a metre-space ray with a player's historical yaw-aligned hitboxes. The ray
        /// direction must already be normalized and the result is the nearest zone in metres.
        /// </summary>
        public static bool TryIntersectPlayer(
            Vector3 rayOriginMeters,
            Vector3 normalizedDirection,
            float maximumDistanceMeters,
            NetworkPlayerSnapshot historicalState,
            float standingHeightMeters,
            out float hitDistanceMeters,
            out CombatHitboxZone hitZone)
        {
            hitDistanceMeters = 0f;
            hitZone = CombatHitboxZone.Torso;

            Vector3 rayDirection;
            if (!historicalState.Initialized || !historicalState.Alive || historicalState.Health == 0 ||
                historicalState.MatchFinished || !IsFinite(rayOriginMeters) ||
                !CombatMath.TryNormalizeDirection(normalizedDirection, out rayDirection) ||
                !IsFinite(maximumDistanceMeters) || maximumDistanceMeters <= 0f ||
                !IsFinite(standingHeightMeters) || standingHeightMeters <= 0f)
            {
                return false;
            }

            Vector3 targetPosition = new Vector3(
                (float)historicalState.EastMeters,
                (float)historicalState.UpMeters,
                (float)historicalState.NorthMeters);
            Vector3 relativeOrigin = rayOriginMeters - targetPosition;

            // Rotate the ray into the target's local yaw frame. Coordinates are E, U, N metres.
            float yawRadians = historicalState.YawDegrees * Mathf.Deg2Rad;
            float sine = Mathf.Sin(yawRadians);
            float cosine = Mathf.Cos(yawRadians);
            Vector3 localOrigin = new Vector3(
                relativeOrigin.x * cosine - relativeOrigin.z * sine,
                relativeOrigin.y,
                relativeOrigin.x * sine + relativeOrigin.z * cosine);
            Vector3 localDirection = new Vector3(
                rayDirection.x * cosine - rayDirection.z * sine,
                rayDirection.y,
                rayDirection.x * sine + rayDirection.z * cosine);

            float crouchScale = historicalState.Crouched ? 0.68f : 1f;
            float nearest = maximumDistanceMeters;
            bool foundHit = false;

            for (int i = 0; i < Hitboxes.Length; i++)
            {
                HitboxTemplate hitbox = Hitboxes[i];
                Vector3 centre = new Vector3(
                    hitbox.CentreX,
                    hitbox.CentreYFraction * standingHeightMeters * crouchScale,
                    0f);
                Vector3 halfExtents = new Vector3(
                    hitbox.HalfExtents.x,
                    hitbox.HalfExtents.y * crouchScale,
                    hitbox.HalfExtents.z);

                float candidateDistance;
                if (RayIntersectsAxisAlignedBox(
                        localOrigin,
                        localDirection,
                        centre,
                        halfExtents,
                        nearest,
                        out candidateDistance))
                {
                    if (!foundHit || candidateDistance < nearest)
                    {
                        foundHit = true;
                        nearest = candidateDistance;
                        hitZone = hitbox.Zone;
                    }
                }
            }

            if (foundHit)
            {
                hitDistanceMeters = nearest;
            }
            return foundHit;
        }

        public static bool RayIntersectsAxisAlignedBox(
            Vector3 rayOrigin,
            Vector3 normalizedDirection,
            Vector3 boxCentre,
            Vector3 halfExtents,
            float maximumDistance,
            out float hitDistance)
        {
            hitDistance = 0f;
            float entry = 0f;
            float exit = maximumDistance;

            if (!ClipAxis(rayOrigin.x - boxCentre.x, normalizedDirection.x, halfExtents.x, ref entry, ref exit) ||
                !ClipAxis(rayOrigin.y - boxCentre.y, normalizedDirection.y, halfExtents.y, ref entry, ref exit) ||
                !ClipAxis(rayOrigin.z - boxCentre.z, normalizedDirection.z, halfExtents.z, ref entry, ref exit) ||
                exit < 0f || entry > maximumDistance)
            {
                return false;
            }

            hitDistance = Mathf.Max(0f, entry);
            return hitDistance <= maximumDistance;
        }

        private static bool ClipAxis(
            float originFromCentre,
            float direction,
            float halfExtent,
            ref float entry,
            ref float exit)
        {
            if (Mathf.Abs(direction) < 0.000001f)
            {
                return Mathf.Abs(originFromCentre) <= halfExtent;
            }

            float first = (-halfExtent - originFromCentre) / direction;
            float second = (halfExtent - originFromCentre) / direction;
            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }

            entry = Mathf.Max(entry, first);
            exit = Mathf.Min(exit, second);
            return entry <= exit;
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
}
