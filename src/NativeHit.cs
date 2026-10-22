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
        /// Seconds to stun the victim (0 = no stagger). Melee A attacks use this to
        /// briefly lock trash enemies out of attacking; bosses/elites are exempt.
        public float staggerDuration;
        /// Stagger bosses and elites too (the stun is part of what the move does, so no enemy is immune).
        public bool staggerBosses;
    }

    // Damage from a native sim, landing as RoR2 damage from a character, so its items, crits
    // and kill credit work: what BulletAttack does for a bullet (TakeDamage, then
    // OnHitEnemy and OnHitAll).
    public static class NativeHit
    {
        static bool warned;

        /// Deals `spec` to `victim` from `attacker`. Server only, except that a client Melee fighter
        /// sends it to the server; returns whether it landed (a live victim on the server) or was sent.
        public static bool Deal(CharacterBody attacker, HealthComponent victim, in NativeHitSpec spec)
        {
            if (!NetworkServer.active)
            {
                // A client's Melee fighter asks the server to land its hit (MeleeNet.RemoteDeal).
                bool sent = MeleeNet.RemoteDeal(attacker, victim, spec);
                if (MeleeDiag.On) MeleeDiag.Deal(attacker, victim, spec, sent ? "sent-to-server" : "client-unsent", sent, 0f, 0f, 0f, "n/a");
                if (sent) return true;
                if (!warned) CommonLog.Warn("NativeHit.Deal on a client; hits are the server's");
                warned = true;
                return false;
            }
            if (!victim || !victim.alive)
            {
                if (MeleeDiag.On) MeleeDiag.Deal(attacker, victim, spec, "victim-dead", false, 0f, 0f, 0f, "n/a");
                return false;
            }
            float healthBefore = MeleeDiag.On ? victim.health : 0f;
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
            string staggerResult = "none";
            if (spec.staggerDuration > 0f)
            {
                // Melee stagger: interrupt the victim so it cannot attack back while
                // being hit. RoR2's own SetStateOnHurt path (the one the grapple uses
                // to keep captures still); bosses and elites are exempt unless the move
                // asks for them too, which flips their stun immunity for this one call.
                var target = victim ? victim.body : null;
                staggerResult = "exempt-boss";
                if (target && (spec.staggerBosses || !MeleeGrapple.IsBoss(target)))
                {
                    var stun = target.GetComponent<SetStateOnHurt>();
                    staggerResult = stun ? "applied" : "no-SetStateOnHurt";
                    if (stun)
                    {
                        bool could = stun.canBeStunned;
                        if (spec.staggerBosses) stun.canBeStunned = true;
                        stun.SetStun(spec.staggerDuration);
                        stun.canBeStunned = could;
                    }
                }
            }
            if (attacker && GlobalEventManager.instance)
            {
                GlobalEventManager.instance.OnHitEnemy(info, victim.gameObject);
                GlobalEventManager.instance.OnHitAll(info, victim.gameObject);
            }
            if (MeleeDiag.On) MeleeDiag.Deal(attacker, victim, spec, "server", true, info.damage, healthBefore, victim ? victim.health : 0f, staggerResult);
            return true;
        }
    }
}
