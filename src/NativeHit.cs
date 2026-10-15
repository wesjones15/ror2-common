using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    /// One hit from a native sim (a Pikmin's swing, a FLUDD droplet), for `NativeHit.Deal`.
    public struct NativeHitSpec
    {
        /// Damage as a multiple of the attacker's damage stat (RoR2's damageCoefficient).
        public float coefficient;
        /// Where it lands: damage numbers, impact effects and on-hit items start here.
        public Vector3 position;
        public Vector3 force;
        /// How strongly on-hit items proc (1 is a normal bullet). Keep it low for anything
        /// that hits many times a second: every hit runs `OnHitEnemy`.
        public float procCoefficient;
        public DamageTypeCombo damageType;
        public DamageColorIndex color;
        /// What hit, as a projectile would be; null for the attacker itself.
        public GameObject inflictor;
    }

    // Damage from a native sim, landing as RoR2 damage from a character, so its items, crits
    // and kill credit work: what BulletAttack does for a bullet (TakeDamage, then
    // OnHitEnemy and OnHitAll).
    public static class NativeHit
    {
        static bool warned;

        /// Deals `spec` to `victim` from `attacker`. Server only; returns whether it landed (a
        /// live victim on the server).
        public static bool Deal(CharacterBody attacker, HealthComponent victim, in NativeHitSpec spec)
        {
            if (!NetworkServer.active)
            {
                if (!warned) CommonLog.Warn("NativeHit.Deal on a client; hits are the server's");
                warned = true;
                return false;
            }
            if (!victim || !victim.alive) return false;
            var info = new DamageInfo
            {
                damage = spec.coefficient * (attacker ? attacker.damage : 12f),
                crit = attacker && attacker.RollCrit(),
                attacker = attacker ? attacker.gameObject : null,
                inflictor = spec.inflictor ? spec.inflictor : attacker ? attacker.gameObject : null,
                position = spec.position,
                force = spec.force,
                procChainMask = default,
                procCoefficient = spec.procCoefficient,
                damageType = spec.damageType,
                damageColorIndex = spec.color,
            };
            victim.TakeDamage(info);
            if (attacker && GlobalEventManager.instance)
            {
                GlobalEventManager.instance.OnHitEnemy(info, victim.gameObject);
                GlobalEventManager.instance.OnHitAll(info, victim.gameObject);
            }
            return true;
        }
    }
}
