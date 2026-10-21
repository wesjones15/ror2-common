using System.Collections.Generic;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    /// Turns enemy projectiles back on their senders: what a Melee reflector (Fox's Reflector) does to
    /// a shot that touches its bubble. A reflected projectile changes owner and team to the reflector's,
    /// flies back the way it came (at the shooter when it is still alive) and hits harder. Server only.
    public static class MeleeReflect
    {
        static readonly Collider[] buffer = new Collider[128];
        static readonly HashSet<ProjectileController> seen = new HashSet<ProjectileController>();

        /// Reflects every enemy projectile within `radius` metres of `centre`. `damageMul` and `speedMul`
        /// are the source's ReflectDesc multipliers. Returns how many were turned.
        public static int Reflect(CharacterBody owner, Vector3 centre, float radius, float damageMul, float speedMul)
        {
            if (!owner || !owner.teamComponent) return 0;
            // A client fighter asks the server to turn them (it cannot say how many).
            if (!NetworkServer.active) { if (MeleeNet.Acts(owner)) MeleeNet.SendReflect(owner, centre, radius, damageMul, speedMul); return 0; }
            int n = Physics.OverlapSphereNonAlloc(centre, radius, buffer, LayerIndex.projectile.mask, QueryTriggerInteraction.Collide);
            seen.Clear();
            int turned = 0;
            for (int i = 0; i < n; i++)
            {
                var controller = buffer[i] ? buffer[i].GetComponentInParent<ProjectileController>() : null;
                if (!controller || !seen.Add(controller)) continue;
                var team = controller.teamFilter;
                if (!team || team.teamIndex == owner.teamComponent.teamIndex) continue;
                var body = controller.GetComponent<Rigidbody>();
                var from = controller.owner ? controller.owner.transform.position : (Vector3?)null;
                var velocity = body ? body.velocity : Vector3.zero;
                float speed = Mathf.Max(velocity.magnitude, 1f) * Mathf.Max(speedMul, 0.1f);
                var heading = -velocity.normalized;
                if (velocity.sqrMagnitude < 0.01f) heading = (centre - controller.transform.position).normalized * -1f;
                // Back at whoever fired it while it is still there; otherwise the way it came.
                if (from.HasValue)
                {
                    var toShooter = from.Value + Vector3.up - controller.transform.position;
                    if (toShooter.sqrMagnitude > 1f) heading = toShooter.normalized;
                }
                controller.owner = owner.gameObject;
                team.teamIndex = owner.teamComponent.teamIndex;
                var damage = controller.GetComponent<ProjectileDamage>();
                if (damage) damage.damage *= Mathf.Max(damageMul, 1f);
                controller.transform.rotation = Quaternion.LookRotation(heading);
                if (body) body.velocity = heading * speed;
                var simple = controller.GetComponent<ProjectileSimple>();
                if (simple) simple.desiredForwardSpeed = speed;
                turned++;
            }
            return turned;
        }
    }
}
