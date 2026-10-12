using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // How far a body's cooldown items have shortened a skill slot's cooldowns, as a ratio, for
    // native moves that have no cooldown of their own but should grow with cooldown items
    // (Mario's hover lasting longer with Alien Head). RoR2 keeps each slot's cooldown scale
    // (Alien Head, and the slot's own items: Hardlight for utility, Lysate for special) and flat
    // reduction (Purity); `CharacterBody.RecalculateStats` sets them.
    public static class SkillCooldown
    {
        /// The slot's cooldown now over its base, 0..1, for a skill whose base cooldown is
        /// `reference` seconds: the same sum `GenericSkill.CalculateFinalRechargeInterval` does,
        /// half a second at least. 1 without a slot.
        public static float Ratio(GenericSkill slot, float reference = 10f)
        {
            if (!slot || reference <= 0f) return 1f;
            float final = Mathf.Min(reference, Mathf.Max(0.5f, reference * slot.cooldownScale - slot.flatCooldownReduction));
            return Mathf.Clamp(final / reference, 0.01f, 1f);
        }

        /// A native move's boost from the slot's cooldown items: `1 / ratio ^ exponent`. With
        /// 0.5, one Alien Head is about x1.15, four x1.8, eight x3.2. Brainstalks' no-cooldown
        /// buff drives the scale to nothing; it's no cooldown item, so while it lasts the boost
        /// stays at `held`, the last one seen without it (pass the same field each time).
        public static float Boost(GenericSkill slot, float exponent, ref float held)
        {
            if (held <= 0f) held = 1f;
            if (!slot) return held;
            if (NativeSkills.Unlimited(slot.characterBody)) return held;
            held = Mathf.Pow(Ratio(slot), -Mathf.Max(0f, exponent));
            return held;
        }
    }
}
