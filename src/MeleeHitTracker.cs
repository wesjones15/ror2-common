using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    /// Applies a native sim's posed hitbox samples as RoR2 damage: each hitbox is swept
    /// as a capsule between its last and current disc-bone position, hits each victim once
    /// per hitbox group generation, and maps Melee damage (percent) to a damage coefficient.
    /// Fighter-independent; the fighter's driver supplies the owner and the model scale.
    public sealed class MeleeHitTracker
    {
        ulong serial = ulong.MaxValue;
        readonly Dictionary<(uint, uint), HashSet<HealthComponent>> victims = new Dictionary<(uint, uint), HashSet<HealthComponent>>();
        readonly Dictionary<(uint, uint, uint), Vector3> previous = new Dictionary<(uint, uint, uint), Vector3>();
        Collider[] candidates = new Collider[64];

        /// Melee damage percent per unit of RoR2 damage coefficient.
        public float PercentPerCoefficient = 3f;
        /// Seconds an electric hit staggers a non-boss victim (0 disables).
        public float Stagger;
        /// Raised once per victim a hitbox lands on, with where it landed.
        public event Action<HealthComponent, Vector3> Connected;

        public void Apply(CharacterBody owner, MeleeNative.HitboxSample[] samples, int count, float scale)
        {
            if (!MeleeNet.Acts(owner)) return;
            for (int i = 0; i < count; i++)
            {
                var hit = samples[i];
                if (serial != hit.serial) { serial = hit.serial; victims.Clear(); previous.Clear(); }
                var forward = new Vector3(hit.facing.x, 0, hit.facing.z);
                var rotation = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
                var local = new Vector3(-hit.local_position.x, hit.local_position.y, hit.local_position.z);
                var point = new Vector3(hit.origin.x, hit.origin.y, hit.origin.z) * scale + rotation * local * scale;
                var key = (hit.group, hit.generation, hit.id);
                if (!previous.TryGetValue(key, out var from)) from = point;
                previous[key] = point;
                int n;
                while (true)
                {
                    n = Physics.OverlapCapsuleNonAlloc(from, point, hit.radius * scale, candidates, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
                    if (n < candidates.Length || candidates.Length >= 1024) break;
                    candidates = new Collider[candidates.Length * 2];
                }
                var group = (hit.group, hit.generation);
                if (!victims.TryGetValue(group, out var seen)) { seen = new HashSet<HealthComponent>(); victims[group] = seen; }
                for (int j = 0; j < n; j++)
                {
                    var hurt = candidates[j] ? candidates[j].GetComponent<HurtBox>() : null;
                    if (!hurt || !hurt.healthComponent || !hurt.healthComponent.alive || seen.Contains(hurt.healthComponent)) continue;
                    var target = hurt.healthComponent.body;
                    if (!target || !target.teamComponent || target.teamComponent.teamIndex == owner.teamComponent.teamIndex) continue;
                    bool ground = target.characterMotor && target.characterMotor.isGrounded;
                    if ((hit.flags & (ground ? 1u : 2u)) == 0) continue;
                    if (hit.damage <= 0) continue;
                    float angle = hit.angle == 361 ? 45f : Mathf.Min(hit.angle, 360u);
                    var direction = forward.normalized * Mathf.Cos(angle * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(angle * Mathf.Deg2Rad);
                    var at = hurt.collider.ClosestPoint(point);
                    if (NativeHit.Deal(owner, hurt.healthComponent, new NativeHitSpec
                    {
                        coefficient = hit.damage / PercentPerCoefficient, position = at,
                        force = direction * (hit.base_knockback + hit.growth) * 5f,
                        procCoefficient = 1f, damageType = hit.element == 1 ? DamageType.IgniteOnHit : DamageType.Generic,
                        color = DamageColorIndex.Default, staggerDuration = hit.element == 2 ? Stagger : 0f,
                    }))
                    {
                        seen.Add(hurt.healthComponent);                        owner.GetComponent<MeleeDriver>()?.HitSpark(hit.element,hit.damage,hurt.collider.ClosestPoint(point));
                        Connected?.Invoke(hurt.healthComponent, at);
                    }
                }
            }
        }
    }
}
