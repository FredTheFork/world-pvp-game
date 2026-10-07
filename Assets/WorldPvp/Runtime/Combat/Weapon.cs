using UnityEngine;

namespace WorldPvp.Phase1.Combat
{
    public enum WeaponFireFailure : byte
    {
        None,
        NotInitialized,
        NotEquipped,
        Reloading,
        EmptyMagazine,
        FireRate,
        InvalidServerTime
    }

    /// <summary>
    /// Per-player magazine/cooldown state. Only NetworkPlayer's server-side methods mutate combat
    /// state; clients use the definition for input pacing and receive ammo/reload through snapshots.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Weapon : MonoBehaviour
    {
        [SerializeField] private WeaponDefinition definition;
        [SerializeField] private bool equippedOnSpawn = true;

        private ushort magazineAmmo;
        private bool isReloading;
        private bool serverInitialized;
        private double reloadCompletesAt;
        private double nextServerShotAt;
        private float nextClientShotAt;

        public WeaponDefinition Definition { get { return definition; } }
        public bool IsEquipped { get { return equippedOnSpawn && definition != null && definition.IsValid; } }
        public ushort MagazineAmmo { get { return magazineAmmo; } }
        public bool IsReloading { get { return isReloading; } }
        public int MagazineCapacity { get { return definition != null ? definition.MagazineCapacity : 0; } }

        /// <summary>Editor scene builder assigns the data asset; it never copies stats into runtime code.</summary>
        public void ConfigureDefinition(WeaponDefinition value)
        {
            definition = value;
        }

        public void InitializeServerState()
        {
            serverInitialized = true;
            magazineAmmo = IsEquipped ? (ushort)definition.MagazineCapacity : (ushort)0;
            isReloading = false;
            reloadCompletesAt = 0.0;
            nextServerShotAt = 0.0;
            nextClientShotAt = 0f;
        }

        public void ResetClientFirePacing()
        {
            nextClientShotAt = 0f;
        }

        /// <summary>Local rate pacing is a UX optimization only; the host independently enforces fire rate.</summary>
        public bool ShouldRequestShot(float realtimeSinceStartup)
        {
            if (!IsEquipped || !IsFinite(realtimeSinceStartup) || realtimeSinceStartup < nextClientShotAt)
            {
                return false;
            }

            nextClientShotAt = realtimeSinceStartup + 1f / definition.FireRateShotsPerSecond;
            return true;
        }

        public bool ServerTryConsumeRound(double serverRealtime)
        {
            WeaponFireFailure ignored;
            return ServerTryConsumeRound(serverRealtime, out ignored);
        }

        public bool ServerTryConsumeRound(double serverRealtime, out WeaponFireFailure failure)
        {
            failure = WeaponFireFailure.None;
            if (!serverInitialized)
            {
                failure = WeaponFireFailure.NotInitialized;
                return false;
            }
            if (!IsEquipped)
            {
                failure = WeaponFireFailure.NotEquipped;
                return false;
            }
            if (!IsFinite(serverRealtime))
            {
                failure = WeaponFireFailure.InvalidServerTime;
                return false;
            }
            if (isReloading)
            {
                failure = WeaponFireFailure.Reloading;
                return false;
            }
            if (magazineAmmo <= 0)
            {
                failure = WeaponFireFailure.EmptyMagazine;
                return false;
            }
            if (serverRealtime + 0.0001 < nextServerShotAt)
            {
                failure = WeaponFireFailure.FireRate;
                return false;
            }

            magazineAmmo--;
            nextServerShotAt = serverRealtime + (1.0 / definition.FireRateShotsPerSecond);
            return true;
        }

        public bool ServerTryBeginReload(double serverRealtime)
        {
            if (!serverInitialized || !IsEquipped || isReloading ||
                magazineAmmo >= definition.MagazineCapacity || !IsFinite(serverRealtime))
            {
                return false;
            }

            isReloading = true;
            reloadCompletesAt = serverRealtime + definition.ReloadTimeSeconds;
            return true;
        }

        /// <returns>True exactly once when a server reload completes.</returns>
        public bool ServerTick(double serverRealtime)
        {
            if (!serverInitialized || !isReloading || !IsFinite(serverRealtime) || serverRealtime < reloadCompletesAt)
            {
                return false;
            }

            magazineAmmo = (ushort)definition.MagazineCapacity;
            isReloading = false;
            reloadCompletesAt = 0.0;
            return true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
