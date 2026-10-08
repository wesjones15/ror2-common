using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // An owner's stats as a native sim can use them: how far its items, buffs and level have
    // taken each one from its base. A swarm of minions with no RoR2 bodies of their own (the
    // Pikmin) takes the owner's stats this way and feeds them to its sim, as Engineer's
    // turrets get their owner's items and level.
    public struct MinionStats
    {
        /// Each stat over its base (the body's base value, before level): 1 for an unbuffed
        /// level-1 body.
        public float damage, attackSpeed, moveSpeed, maxHealth;
        /// Health regenerated per second, as a fraction of max health.
        public float regenFraction;
        /// What a hit is multiplied by after armour (RoR2's 100 / (100 + armor); more than 1
        /// for negative armour).
        public float damageTaken;
        /// Critical chance, 0..1.
        public float crit;

        public static readonly MinionStats One = new MinionStats
        {
            damage = 1f, attackSpeed = 1f, moveSpeed = 1f, maxHealth = 1f, damageTaken = 1f,
        };

        /// `owner`'s stats now; `One` without a body. Move speed leaves sprinting out.
        public static MinionStats Of(CharacterBody owner)
        {
            if (!owner) return One;
            float move = owner.moveSpeed;
            if (owner.isSprinting && owner.sprintingSpeedMultiplier > 0f) move /= owner.sprintingSpeedMultiplier;
            float armor = owner.armor;
            return new MinionStats
            {
                damage = Ratio(owner.damage, owner.baseDamage),
                attackSpeed = Ratio(owner.attackSpeed, owner.baseAttackSpeed),
                moveSpeed = Ratio(move, owner.baseMoveSpeed),
                maxHealth = Ratio(owner.maxHealth, owner.baseMaxHealth),
                regenFraction = owner.maxHealth > 0f ? owner.regen / owner.maxHealth : 0f,
                damageTaken = armor >= 0f ? 100f / (100f + armor) : 2f - 100f / (100f - armor),
                crit = Mathf.Clamp01(owner.crit / 100f),
            };
        }

        static float Ratio(float value, float baseValue) => baseValue > 0f ? value / baseValue : 1f;
    }
}
