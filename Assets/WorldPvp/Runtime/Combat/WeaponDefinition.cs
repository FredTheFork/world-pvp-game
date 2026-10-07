using System;
using UnityEngine;

namespace WorldPvp.Phase1.Combat
{
    /// <summary>Data asset for an authoritative hitscan weapon; gameplay code reads properties only from this asset.</summary>
    [CreateAssetMenu(menuName = "World PvP/Phase 7/Weapon Definition", fileName = "WeaponDefinition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string weaponId = string.Empty;
        [SerializeField] private string displayName = string.Empty;

        [Header("Server-enforced weapon properties")]
        [Min(0.1f)] [SerializeField] private float fireRateShotsPerSecond;
        [Min(0.1f)] [SerializeField] private float damage;
        [Min(1f)] [SerializeField] private float rangeMeters;
        [Range(0f, 20f)] [SerializeField] private float spreadDegrees;
        [Range(1, 255)] [SerializeField] private int magazineCapacity;
        [Min(0.1f)] [SerializeField] private float reloadTimeSeconds;

        [Header("Hitbox damage multipliers")]
        [Min(0f)] [SerializeField] private float headDamageMultiplier;
        [Min(0f)] [SerializeField] private float torsoDamageMultiplier;
        [Min(0f)] [SerializeField] private float limbDamageMultiplier;

        [Header("Optional presentation assets")]
        [SerializeField] private AudioClip fireSound;
        [SerializeField] private AudioClip reloadSound;

        public string WeaponId { get { return weaponId != null ? weaponId.Trim() : string.Empty; } }
        public string DisplayName { get { return string.IsNullOrWhiteSpace(displayName) ? WeaponId : displayName.Trim(); } }
        public float FireRateShotsPerSecond { get { return IsFinite(fireRateShotsPerSecond) ? Mathf.Clamp(fireRateShotsPerSecond, 0.1f, 30f) : 0f; } }
        public float Damage { get { return IsFinite(damage) ? Mathf.Clamp(damage, 0f, 1000f) : 0f; } }
        public float RangeMeters { get { return IsFinite(rangeMeters) ? Mathf.Clamp(rangeMeters, 1f, 5000f) : 0f; } }
        public float SpreadDegrees { get { return IsFinite(spreadDegrees) ? Mathf.Clamp(spreadDegrees, 0f, 20f) : 0f; } }
        public int MagazineCapacity { get { return Mathf.Clamp(magazineCapacity, 1, 255); } }
        public float ReloadTimeSeconds { get { return IsFinite(reloadTimeSeconds) ? Mathf.Clamp(reloadTimeSeconds, 0.1f, 60f) : 0f; } }
        public AudioClip FireSound { get { return fireSound; } }
        public AudioClip ReloadSound { get { return reloadSound; } }
        public bool IsValid
        {
            get
            {
                const float maximumMultiplier = 20f;
                return IsSafeId(WeaponId) && !string.IsNullOrWhiteSpace(DisplayName) &&
                       IsFinite(fireRateShotsPerSecond) && fireRateShotsPerSecond >= 0.1f && fireRateShotsPerSecond <= 30f &&
                       IsFinite(damage) && damage > 0f && damage <= 1000f &&
                       IsFinite(rangeMeters) && rangeMeters >= 1f && rangeMeters <= 5000f &&
                       IsFinite(spreadDegrees) && spreadDegrees >= 0f && spreadDegrees <= 20f &&
                       magazineCapacity >= 1 && magazineCapacity <= 255 &&
                       IsFinite(reloadTimeSeconds) && reloadTimeSeconds >= 0.1f && reloadTimeSeconds <= 60f &&
                       IsFinite(headDamageMultiplier) && headDamageMultiplier > torsoDamageMultiplier &&
                       headDamageMultiplier <= maximumMultiplier &&
                       IsFinite(torsoDamageMultiplier) && torsoDamageMultiplier > 0f &&
                       torsoDamageMultiplier <= maximumMultiplier &&
                       IsFinite(limbDamageMultiplier) && limbDamageMultiplier > 0f &&
                       limbDamageMultiplier < torsoDamageMultiplier && limbDamageMultiplier <= maximumMultiplier;
            }
        }

        public ushort DamageForZone(CombatHitboxZone zone)
        {
            if (!IsValid)
            {
                return 0;
            }

            float multiplier;
            switch (zone)
            {
                case CombatHitboxZone.Head:
                    multiplier = headDamageMultiplier;
                    break;
                case CombatHitboxZone.LeftArm:
                case CombatHitboxZone.RightArm:
                case CombatHitboxZone.LeftLeg:
                case CombatHitboxZone.RightLeg:
                    multiplier = limbDamageMultiplier;
                    break;
                default:
                    multiplier = torsoDamageMultiplier;
                    break;
            }

            float scaledDamage = Damage * Mathf.Clamp(multiplier, 0f, 20f);
            int rounded = Mathf.RoundToInt(scaledDamage);
            if (multiplier > 0f)
            {
                rounded = Mathf.Max(1, rounded);
            }
            return (ushort)Mathf.Clamp(rounded, 0, ushort.MaxValue);
        }

        private static bool IsSafeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 48)
            {
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool letter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
                bool digit = c >= '0' && c <= '9';
                if (!letter && !digit && c != '_' && c != '-')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
