using System;
using RoR2;
using UnityEngine.Networking;

namespace Ror2Common
{
    // Bridge explosions to entities a native sim owns, even when CollectHits finds no
    // RoR2 hurtboxes. Derived from this build's BlastAttack.Fire / HandleHits.
    public static class BlastDamage
    {
        static int enemyBlastDepth;

        public static bool CoversReport(DamageTypeCombo type) =>
            enemyBlastDepth > 0 && (type & DamageType.AOE) != 0;

        public static BlastAttack.Result Fire(Func<BlastAttack, BlastAttack.Result> original,
            BlastAttack blast, Action<BlastAttack> apply)
        {
            bool enemy = NetworkServer.active && blast != null
                && blast.teamIndex != TeamIndex.Player && blast.teamIndex != TeamIndex.None
                && (blast.damageType & DamageType.DoT) == 0;
            if (!enemy) return original(blast);
            BlastAttack.Result result;
            enemyBlastDepth++;
            try { result = original(blast); }
            finally { enemyBlastDepth--; }
            // Once per explosion, including zero hits; never once per reported victim.
            apply(blast);
            return result;
        }

        public static float Fraction(BlastAttack.FalloffModel model, float distance, float radius)
        {
            if (!float.IsFinite(distance) || !float.IsFinite(radius) || radius < 0f || distance > radius) return 0f;
            float t = radius > 0f ? Math.Clamp(distance / radius, 0f, 1f) : 0f;
            switch (model)
            {
                case BlastAttack.FalloffModel.None: return 1f;
                case BlastAttack.FalloffModel.Linear: return 1f - t;
                case BlastAttack.FalloffModel.SweetSpot: return t > .5f ? .25f : 1f;
                case BlastAttack.FalloffModel.HalfLinear: return 1f - .5f * t;
                case BlastAttack.FalloffModel.QuarterLinear: return 1f - .75f * t;
                case BlastAttack.FalloffModel.HalfSweetSpot: return t > .5f ? .5f : 1f;
                case BlastAttack.FalloffModel.ThreeQuarterSweetSpot: return t > .5f ? .75f : 1f;
                default: return 0f;
            }
        }
    }
}
