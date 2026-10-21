using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    /// A native-article shot that flies along the crosshair ray and bounces off surfaces:
    /// the arc Luigi's fireball and Pikachu's Thunder Shock share. The sim (libssbm
    /// `Projectile`) owns speed, gravity, the ground-bounce budget and the damage it deals;
    /// this host half sweeps the world, calls back into the sim on contact
    /// (`ssbm_projectile_bounce` on ground, `ssbm_projectile_reflect` on walls and ceilings),
    /// damages an enemy it touches, and draws the article. Server only.
    public sealed class MeleeBouncingShot : MonoBehaviour
    {
        public sealed class Config
        {
            public string AssetsDir;
            public uint Fighter, Article, State;
            /// The article drawn as the shot (an effect-only article has no model: leave `ViewArticle` at uint.MaxValue).
            public uint ViewArticle = uint.MaxValue, ViewState;
            /// Metres per Melee unit.
            public float Scale = 0.1f;
            public MeleeParticles Particles;
            /// Particle bank ids: a trail that follows the shot, and one-shot puffs.
            public uint TrailParticle, BouncePuff, ImpactPuff, DespawnPuff;
            /// Plays a source sound id through the owner's mixer.
            public Action<uint> PlaySound;
            public uint BounceSound;
            /// Hard cap on a shot's life in seconds, whatever the sim says.
            public float MaxSeconds = 10f;
            /// Hard cap on a shot's straight-line range in metres; it despawns
            /// past this (or the host's map bounds) even though its sim lifetime
            /// is unbounded.
            public float MaxRange = 200f;
            /// Melee damage percent per unit of damage coefficient.
            public float PercentPerCoefficient = 3f;
            /// Enemy hit radius multiplier so a shot at range lands as easily as a bullet.
            public float HitRadiusScale = 1.5f;
        }

        static ulong nextKey = 1ul << 40;
        static readonly RaycastHit[] hits = new RaycastHit[32];

        Config cfg;
        CharacterBody owner;
        Vector3 origin;
        IntPtr projectile;
        MeleeNative.ProjectileState state;
        MeleeFighterView view;
        ulong trailKey;
        float age;
        bool finished;
        readonly HashSet<HealthComponent> victims = new HashSet<HealthComponent>();

        public static MeleeBouncingShot Spawn(CharacterBody owner, Vector3 point, Vector3 direction, Config cfg)
        {
            if (!NetworkServer.active) return null;
            var go = new GameObject("Melee bouncing shot");
            go.transform.position = point;
            var shot = go.AddComponent<MeleeBouncingShot>();
            shot.owner = owner;
            shot.origin = point;
            shot.cfg = cfg;
            shot.projectile = MeleeNative.ssbm_projectile_new(NativeLib.CString(cfg.AssetsDir), cfg.Fighter, cfg.Article, cfg.State, 0,
                new MeleeNative.Vec3(direction.x, direction.y, direction.z));
            if (shot.projectile == IntPtr.Zero) { Destroy(go); return null; }
            // The sim's source life is an offscreen cutoff; open worlds are bounded by MaxSeconds.
            MeleeNative.ssbm_projectile_lifetime(shot.projectile, 0);
            if (cfg.ViewArticle != uint.MaxValue)
                shot.view = new MeleeFighterView(cfg.AssetsDir, cfg.Fighter, cfg.ViewArticle, cfg.ViewState);
            shot.ReadState();
            if (cfg.TrailParticle != 0 && cfg.Particles != null)
            {
                shot.trailKey = nextKey++;
                try { cfg.Particles.Spawn(cfg.TrailParticle, MeleeParticles.At(point, Quaternion.identity, cfg.Scale), shot.trailKey); }
                catch (Exception e) { CommonLog.Error("Melee shot trail: " + e.Message); shot.trailKey = 0; }
            }
            return shot;
        }

        void FixedUpdate()
        {
            if (finished || projectile == IntPtr.Zero) return;
            if (!owner || !owner.healthComponent || !owner.healthComponent.alive) { End(false); return; }
            age += Time.fixedDeltaTime;
            if (age > cfg.MaxSeconds) { End(true); return; }
            if ((transform.position - origin).sqrMagnitude > cfg.MaxRange * cfg.MaxRange) { End(true); return; }
            var from = transform.position;
            MeleeNative.ssbm_projectile_step(projectile, Time.fixedDeltaTime, default, 0);
            ReadState();
            if ((state.flags & 2) != 0) { End(true); return; }
            var next = origin + new Vector3(state.position.x, state.position.y, state.position.z) * cfg.Scale;
            var step = next - from;
            float length = step.magnitude;
            if (length < 1e-5f) { transform.position = next; return; }
            float radius = Mathf.Max(state.radius * cfg.Scale, 0.1f);
            int n = Physics.SphereCastNonAlloc(from, radius, step / length, hits, length,
                WorldProbe.Mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (!h.collider || h.distance <= 0f && h.normal == Vector3.zero) continue;
                var body = h.collider.GetComponentInParent<CharacterBody>();
                if (body == owner) continue;
                var hurt = h.collider.GetComponent<HurtBox>();
                if (hurt && hurt.healthComponent)
                {
                    var team = hurt.healthComponent.body && hurt.healthComponent.body.teamComponent ? hurt.healthComponent.body.teamComponent.teamIndex : TeamIndex.None;
                    if (team == owner.teamComponent.teamIndex || !hurt.healthComponent.alive) continue;
                }
                else if (body) continue; // another body's non-hurtbox collider (allies, props with motors)
                if (best < 0 || h.distance < hits[best].distance) best = i;
            }
            if (best < 0) { transform.position = next; return; }
            var contact = hits[best];
            var enemy = contact.collider.GetComponent<HurtBox>();
            if (enemy && enemy.healthComponent) { Strike(contact.point); return; }
            Surface(contact, radius);
        }

        void Surface(RaycastHit contact, float radius)
        {
            var normal = contact.normal;
            var centre = contact.point + normal * (radius + 0.02f);
            if (normal.y > 0.5f)
            {
                // Ground: the sim spends one bounce; when the budget is gone the shot ends.
                if (MeleeNative.ssbm_projectile_bounce(projectile) == 0) { End(true, centre); return; }
            }
            else
            {
                // Wall/ceiling: the sim reflects the shot and spends one of the same
                // surface contacts (Pikachu's ten arcs), so a Thunder Jolt arcs off
                // any surface, not just the floor. Exhausted budget ends the shot.
                if (MeleeNative.ssbm_projectile_reflect(projectile, new MeleeNative.Vec3(normal.x, normal.y, normal.z)) == 0) { End(true, centre); return; }
            }
            var rel = (centre - origin) / cfg.Scale;
            MeleeNative.ssbm_projectile_place(projectile, new MeleeNative.Vec3(rel.x, rel.y, rel.z), 0);
            transform.position = centre;
            Puff(cfg.BouncePuff, centre);
            if (cfg.BounceSound != 0) cfg.PlaySound?.Invoke(cfg.BounceSound);
        }

        void Strike(Vector3 at)
        {
            transform.position = at;
            ReadState();
            var search = new SphereSearch { origin = at, radius = Mathf.Max(state.radius * cfg.Scale * cfg.HitRadiusScale, 0.6f), mask = LayerIndex.entityPrecise.mask };
            search.RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(owner.teamComponent.teamIndex)).FilterCandidatesByDistinctHurtBoxEntities();
            foreach (var hurt in search.GetHurtBoxes())
            {
                if (!hurt || !hurt.healthComponent || !victims.Add(hurt.healthComponent)) continue;
                NativeHit.Deal(owner, hurt.healthComponent, new NativeHitSpec
                {
                    coefficient = state.damage / cfg.PercentPerCoefficient, position = hurt.transform.position, force = Vector3.zero,
                    procCoefficient = 1f, damageType = DamageType.Generic, color = DamageColorIndex.Default,
                });
            }
            Puff(cfg.ImpactPuff, at);
            End(false);
        }

        void End(bool despawn, Vector3? at = null)
        {
            if (finished) return;
            finished = true;
            if (despawn) Puff(cfg.DespawnPuff, at ?? transform.position);
            Destroy(gameObject);
        }

        void Puff(uint id, Vector3 world)
        {
            if (id == 0 || cfg.Particles == null) return;
            try { cfg.Particles.Spawn(id, MeleeParticles.At(world, Quaternion.identity, cfg.Scale), nextKey++); }
            catch (Exception e) { CommonLog.Error("Melee shot puff: " + e.Message); }
        }

        void ReadState()
        {
            if (MeleeNative.ssbm_projectile_state(projectile, out state) != 1) throw new InvalidOperationException(MeleeNative.Error);
        }

        void LateUpdate()
        {
            var velocity = new Vector3(state.velocity.x, state.velocity.y, state.velocity.z);
            var rotation = velocity.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
            if (trailKey != 0 && cfg.Particles != null)
            {
                try { cfg.Particles.Attach(trailKey, MeleeParticles.At(transform.position, rotation, cfg.Scale)); }
                catch (Exception) { trailKey = 0; }
            }
            if (view == null) return;
            try
            {
                view.ObjectPose(state.animation_frame);
                view.FaceCamera(transform.position, rotation, cfg.Scale);
                view.Models.Update(cfg.Scale, d => (transform.position, rotation));
            }
            catch (Exception e) { CommonLog.Error("Melee shot view: " + e.Message); view?.Dispose(); view = null; }
        }

        void OnDestroy()
        {
            if (trailKey != 0 && cfg != null && cfg.Particles != null) { try { cfg.Particles.Stop(trailKey); } catch (Exception) { } }
            view?.Dispose(); view = null;
            if (projectile != IntPtr.Zero) { MeleeNative.ssbm_projectile_free(projectile); projectile = IntPtr.Zero; }
        }
    }
}
