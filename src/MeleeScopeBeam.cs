using System;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// One beam of the Super Scope (it/kinds/itsscopebeam.c): a straight shot along the crosshair that ends on the
    /// first enemy or surface it meets. The sim (libssbm `Projectile`, built by `ssbm_scope_beam_new`) owns the
    /// disc's speed, life, size and damage for the charge level; this host half sweeps the world, damages the enemy
    /// it touches and draws the level's model. The machine that owns the fighter only.
    public sealed class MeleeScopeBeam : MonoBehaviour
    {
        public sealed class Config
        {
            public string AssetsDir;
            /// Metres per Melee unit.
            public float Scale = 0.1f;
            /// Host adaptation: the beam flies and reaches this many times as far as the source's (an open world
            /// is bigger than a stage).
            public float RangeScale = 3f;
            /// Melee damage percent per unit of damage coefficient.
            public float PercentPerCoefficient = 3f;
            /// Enemy hit radius multiplier so a beam at range lands as easily as a bullet.
            public float HitRadiusScale = 1.5f;
            /// Knockback force of a level 0 beam and what each level adds.
            public float ForceBase = 120f, ForcePerLevel = 90f;
            /// Seconds before a beam goes whatever the sim says.
            public float MaxSeconds = 6f;
        }

        // The ten levels' speed, scale, life, damage, radius and element, read once from the disc.
        static float[] table;
        static string tableFor;

        Config cfg;
        CharacterBody owner;
        Vector3 origin;
        uint level;
        IntPtr projectile;
        MeleeNative.ProjectileState state;
        MeleeFighterView view;
        float age, modelScale = 1f;
        bool finished;

        public static bool Available(string assetsDir) => MeleeNative.HasScope;

        /// Host adaptation: the source's uncharged beam is 1% damage, unusable in RoR2. Raise the low
        /// levels and fade the bonus to zero by the top level, whose ~933% is already good.
        internal static float BeamCoefficient(uint level, float damage) =>
            damage / 3f + (1f - level / 9f) * (4f / 3f);

        public static MeleeScopeBeam Spawn(CharacterBody owner, Vector3 point, Vector3 direction, uint level, Config cfg)
        {
            if (!MeleeNet.Acts(owner) || level > 9) return null;
            var go = new GameObject("Melee Super Scope beam");
            go.transform.position = point;
            var beam = go.AddComponent<MeleeScopeBeam>();
            beam.owner = owner;
            beam.origin = point;
            beam.cfg = cfg;
            beam.level = level;
            beam.projectile = MeleeNative.ssbm_scope_beam_new(NativeLib.CString(cfg.AssetsDir), level, new MeleeNative.Vec3(direction.x, direction.y, direction.z));
            if (beam.projectile == IntPtr.Zero) { Destroy(go); return null; }
            MeleeNative.ssbm_projectile_lifetime(beam.projectile, 0); // life is bounded by MaxSeconds and the sim's own
            if (table == null || tableFor != cfg.AssetsDir)
            {
                var rows = new float[60];
                table = MeleeNative.ssbm_scope_beams(NativeLib.CString(cfg.AssetsDir), rows) == 1 ? rows : null;
                tableFor = cfg.AssetsDir;
            }
            beam.modelScale = table != null ? table[level * 6 + 1] : 1f;
            try { beam.view = MeleeFighterView.Item(cfg.AssetsDir, 0x26, level); }
            catch (Exception e) { CommonLog.Error("Super Scope beam model: " + e.Message); }
            beam.ReadState();
            return beam;
        }

        void FixedUpdate()
        {
            if (finished || projectile == IntPtr.Zero) return;
            if (!owner || !owner.healthComponent || !owner.healthComponent.alive) { End(); return; }
            age += Time.fixedDeltaTime;
            if (age > cfg.MaxSeconds) { End(); return; }
            var from = transform.position;
            MeleeNative.ssbm_projectile_step(projectile, Time.fixedDeltaTime, default, 0);
            ReadState();
            if ((state.flags & 2) != 0) { End(); return; }
            var next = origin + new Vector3(state.position.x, state.position.y, state.position.z) * (cfg.Scale * cfg.RangeScale);
            var step = next - from;
            float length = step.magnitude;
            if (length < 1e-5f) { transform.position = next; return; }
            float radius = Mathf.Max(state.radius * cfg.Scale, 0.1f);
            if (!Sweep(from, step / length, length, radius, out var contact)) { transform.position = next; return; }
            var enemy = contact.collider.GetComponent<HurtBox>();
            if (enemy && enemy.healthComponent) Strike(contact.point, step / length);
            else { transform.position = contact.point; End(); }
        }

        // Shared terrain/enemy radii with Samus Charge Shot.
        bool Sweep(Vector3 from, Vector3 dir, float length, float radius, out RaycastHit contact)
            => MeleeSweep.Ranged(owner, from, radius, dir, length, out contact);

        void Strike(Vector3 at, Vector3 heading)
        {
            transform.position = at;
            ReadState();
            var search = new SphereSearch { origin = at, radius = Mathf.Max(state.radius * cfg.Scale * cfg.HitRadiusScale, 0.6f), mask = LayerIndex.entityPrecise.mask };
            search.RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(owner.teamComponent.teamIndex)).FilterCandidatesByDistinctHurtBoxEntities();
            bool fire = state.element == 1;
            foreach (var hurt in search.GetHurtBoxes())
            {
                if (!hurt || !hurt.healthComponent || !hurt.healthComponent.alive) continue;
                NativeHit.Deal(owner, hurt.healthComponent, new NativeHitSpec
                {
                    coefficient = BeamCoefficient(level, state.damage),
                    position = hurt.transform.position,
                    force = heading * (cfg.ForceBase + cfg.ForcePerLevel * level),
                    procCoefficient = 1f,
                    damageType = fire ? DamageType.IgniteOnHit : DamageType.Generic,
                    color = DamageColorIndex.Default,
                    staggerDuration = level >= 3 ? 0.3f : 0.12f,
                });
                owner.GetComponent<MeleeDriver>()?.HitSpark(state.element, state.damage, hurt.transform.position);
            }
            End();
        }

        void End()
        {
            if (finished) return;
            finished = true;
            Destroy(gameObject);
        }

        void ReadState()
        {
            if (MeleeNative.ssbm_projectile_state(projectile, out state) != 1) throw new InvalidOperationException(MeleeNative.Error);
        }

        void LateUpdate()
        {
            if (view == null) return;
            var velocity = new Vector3(state.velocity.x, state.velocity.y, state.velocity.z);
            var rotation = velocity.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
            try
            {
                view.ObjectPose(state.animation_frame);
                float size = cfg.Scale * modelScale;
                view.FaceCamera(transform.position, rotation, size);
                view.Models.Update(size, d => (transform.position, rotation));
            }
            catch (Exception e) { CommonLog.Error("Super Scope beam view: " + e.Message); view?.Dispose(); view = null; }
        }

        void OnDestroy()
        {
            view?.Dispose(); view = null;
            if (projectile != IntPtr.Zero) { MeleeNative.ssbm_projectile_free(projectile); projectile = IntPtr.Zero; }
        }
    }
}
