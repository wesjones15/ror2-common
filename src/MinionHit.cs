using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Where a minion with no RoR2 body is, and what it's doing, when it hits.
    public struct MinionState
    {
        public Vector3 position;
        public bool moving, grounded;
        /// Its own health, 0..1.
        public float healthFraction;
    }

    // A hit by a minion that has no RoR2 body (one Pikmin of a hundred): `NativeHit` from its
    // owner, for kill credit, crits and on-hit items, but with the checks RoR2 makes against
    // the attacker's body made against the minion. So far that's one: HealthComponent.TakeDamage
    // takes `attackerBody.corePosition - damageInfo.position` once, for Focus Crystal's range
    // and Bandit's backstab; the hook (MinionHooks.cs) swaps in the minion's position. Add
    // checks here only when a test shows an item reading the owner wrongly.
    //
    // The hook needs MonoMod: a plugin that defines ROR2COMMON_HOOKS and references
    // Mono.Cecil, MonoMod.Utils and MonoMod.RuntimeDetour (BepInEx/core) gets it; without, hits
    // still land, measured from the owner.
    public static partial class MinionHit
    {
        static bool tried, hooked;
        // The minion whose hit is landing, while Deal runs.
        static bool dealing;
        static Vector3 at;

        /// Whether the attacker-body checks follow the minion (the hook is in).
        public static bool Hooked
        {
            get
            {
                if (!tried)
                {
                    tried = true;
                    InstallHooks(ref hooked);
                }
                return hooked;
            }
        }

        /// Deals `spec` to `victim` as `owner`'s hit, made by `minion`. Server only.
        public static bool Deal(CharacterBody owner, in MinionState minion, HealthComponent victim, in NativeHitSpec spec)
        {
            _ = Hooked;
            dealing = true;
            at = minion.position;
            try
            {
                return NativeHit.Deal(owner, victim, spec);
            }
            finally
            {
                dealing = false;
            }
        }

        /// The attacker's position as TakeDamage should see it.
        static Vector3 AttackerPosition(Vector3 body) => dealing ? at : body;

        static partial void InstallHooks(ref bool hooked);
    }
}
