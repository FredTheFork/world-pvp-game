using UnityEngine;
using WorldPvp.Phase1.Battles;

namespace WorldPvp.Phase1.Combat
{
    /// <summary>Server-side trigger volume bound to a player identity and a damage zone.</summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerHitbox : MonoBehaviour
    {
        [SerializeField] private NetworkPlayer owner;
        [SerializeField] private CombatHitboxZone zone;

        public NetworkPlayer Owner { get { return owner; } }
        public CombatHitboxZone Zone { get { return zone; } }

        public void Configure(NetworkPlayer player, CombatHitboxZone hitboxZone)
        {
            owner = player;
            zone = hitboxZone;
        }
    }
}
