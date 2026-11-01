using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    // Native-sim victims of RoR2.OverlapAttack. Geometry and repeat-hit timing follow
    // this build's Fire / addIgnoredHitList / cleanupRetriggerList. No fake hurtboxes.
    public static class OverlapDamage
    {
        public readonly struct Volume
        {
            public readonly Vector3 Centre, HalfExtents;
            public readonly Quaternion Rotation;
            public readonly bool Sphere;
            public Volume(Vector3 centre, Vector3 scale, Quaternion rotation, bool sphere)
            {
                Centre = centre;
                HalfExtents = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) * .5f;
                Rotation = rotation;
                Sphere = sphere;
            }
            public bool Touches(Vector3 centre, float radius)
            {
                if (!Finite(Centre) || !Finite(HalfExtents) || !Finite(centre)
                    || !float.IsFinite(radius) || radius < 0f) return false;
                if (Sphere)
                {
                    // OverlapAttack's sphere radius is max(lossyScale), not half of it.
                    float reach = 2f * Mathf.Max(HalfExtents.x, Mathf.Max(HalfExtents.y, HalfExtents.z)) + radius;
                    return (centre - Centre).sqrMagnitude <= reach * reach;
                }
                float norm = Rotation.x*Rotation.x + Rotation.y*Rotation.y + Rotation.z*Rotation.z + Rotation.w*Rotation.w;
                if (!float.IsFinite(norm) || norm < .000001f) return false;
                var local = Quaternion.Inverse(Rotation) * (centre - Centre);
                float x = Mathf.Max(0f, Mathf.Abs(local.x) - HalfExtents.x);
                float y = Mathf.Max(0f, Mathf.Abs(local.y) - HalfExtents.y);
                float z = Mathf.Max(0f, Mathf.Abs(local.z) - HalfExtents.z);
                return x*x + y*y + z*z <= radius*radius;
            }

            /// Squared distance from `point` to this volume's centre (0 when they coincide).
            /// Ranks native victims inside one swing; `Touches` stays the actual test.
            public float SquaredDistanceTo(Vector3 point)
            {
                if (!Finite(Centre) || !Finite(point)) return float.MaxValue;
                return (point - Centre).sqrMagnitude;
            }
            static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        }

        public sealed class Swing
        {
            readonly Dictionary<(object world, ulong victim), float> nextHit = new Dictionary<(object, ulong), float>();
            public bool TryHit(object world, ulong victim, float now, float retrigger)
            {
                var key = (world, victim);
                if (nextHit.TryGetValue(key, out float expires) && now <= expires) return false;
                nextHit[key] = now + Mathf.Max(0f, retrigger);
                return true;
            }
        }

        public sealed class Sample
        {
            public readonly Volume[] Volumes;
            public readonly GameObject Attacker, Inflictor;
            public readonly float Damage, Proc, Now, Retrigger;
            public readonly DamageTypeCombo Type;
            public readonly bool Crit;
            public Sample(OverlapAttack attack)
            {
                Attacker = attack.attacker; Inflictor = attack.inflictor;
                Damage = attack.damage; Proc = attack.procCoefficient; Type = attack.damageType;
                Crit = attack.isCrit; Now = Time.realtimeSinceStartup; Retrigger = attack.retriggerTimeout;
                var volumes = new List<Volume>();
                var boxes = attack.hitBoxGroup ? attack.hitBoxGroup.hitBoxes : null;
                if (boxes != null)
                    foreach (var box in boxes)
                        if (box && box.enabled && box.gameObject.activeInHierarchy && box.transform)
                        {
                            var t = box.transform;
                            volumes.Add(new Volume(t.position, t.lossyScale, t.rotation, attack.isSphereOverlap));
                        }
                Volumes = volumes.ToArray();
            }
            public bool Touches(Vector3 centre, float radius)
            {
                foreach (var volume in Volumes) if (volume.Touches(centre, radius)) return true;
                return false;
            }

            /// Squared distance from `point` to the nearest active volume's centre. Ranks
            /// native victims so the cap takes the Pikmin on the attacker first.
            public float SquaredDistanceTo(Vector3 point)
            {
                float best = float.MaxValue;
                foreach (var volume in Volumes)
                {
                    float d = volume.SquaredDistanceTo(point);
                    if (d < best) best = d;
                }
                return best;
            }
        }

        static readonly ConditionalWeakTable<OverlapAttack, Swing> swings = new ConditionalWeakTable<OverlapAttack, Swing>();
        static Sample current;
        public static bool CoversReport(DamageInfo info) => current != null && info != null
            && ReferenceEquals(info.attacker, current.Attacker) && ReferenceEquals(info.inflictor, current.Inflictor)
            && info.damageType.Equals(current.Type);

        public static void Reset(OverlapAttack attack) { if (attack != null) swings.Remove(attack); }

        public static bool Fire(Func<OverlapAttack, List<HurtBox>, bool> original, OverlapAttack attack,
            List<HurtBox> results, Func<Sample, Swing, bool> apply)
        {
            bool enemy = NetworkServer.active && attack != null && attack.teamIndex != TeamIndex.Player
                && attack.teamIndex != TeamIndex.None && (attack.damageType & DamageType.DoT) == 0;
            if (!enemy) return original(attack, results);
            var sample = new Sample(attack);
            // Nothing was checked by Fire: preserve legacy report forwarding in that case.
            if (sample.Volumes.Length == 0) return original(attack, results);
            var swing = swings.GetValue(attack, _ => new Swing());
            var previous = current;
            bool hit;
            current = sample;
            try { hit = original(attack, results); }
            finally { current = previous; }
            // A native victim also counts as a hit to the attacking state (hit pause, etc.).
            return apply(sample, swing) || hit;
        }
    }
}
