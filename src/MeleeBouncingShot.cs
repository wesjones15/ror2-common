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
    /// damages an enemy it touches, and draws the article. The machine that owns the fighter only.
    public sealed class MeleeBouncingShot : MonoBehaviour
    {
        public sealed class Config
        {
            public string AssetsDir;
            public uint Fighter, Article, State;
            /// The article drawn as the shot (an effect-only article has no model: leave `ViewArticle` at uint.MaxValue).
            public uint ViewArticle = uint.MaxValue, ViewState;
            /// A short article flash at each surface contact. Pikachu's source contact replaces the
            /// ground jolt's 0x4BD aura with the air-jolt arc model for 24 ticks
            /// (itpikachutjoltground.c it_802B3554 / itpikachutjoltair.c xDF4 <= 24); the host
            /// draws that arc at the bounce instead of freezing it on the flying orb.
            public uint BounceViewArticle = uint.MaxValue, BounceViewState;
            /// Size multiplier for the bounce arc (the source item's model is large).
            public float BounceViewScale = 1f;
            /// Melee ticks the bounce arc lives (source air jolt: 24).
            public int BounceViewFrames = 24;
            /// Metres per Melee unit.
            public float Scale = 0.1f;
            public MeleeParticles Particles;
            /// Particle bank ids: an attached generator effect that follows the shot, and one-shot puffs.
            public uint TrailParticle, BouncePuff, ImpactPuff, DespawnPuff;
            /// Seconds a one-shot puff (Bounce/Impact/Despawn) may keep its generator. Several source
            /// child generators have a zero generator lifetime (infinite); spawned untracked they
            /// would emit sparks at the contact point for the rest of the run.
            public float PuffSeconds = 2f;
            /// Plays a source sound id through the owner's mixer.
            public Action<uint> PlaySound;
            public uint BounceSound;
            /// Hard cap on a shot's life in seconds, whatever the sim says.
            public float MaxSeconds = 10f;
            /// Hard cap on a shot's straight-line range in metres; it despawns
            /// past this (or the host's map bounds) even though its sim lifetime
            /// is unbounded.
            public float MaxRange = 200f;
            /// Host adaptation: fly level for this many metres before the disc's arc begins.
            /// The sim works in Melee units, so the spawn divides by `Scale`.
            public float StraightMetres;
            /// Melee damage percent per unit of damage coefficient.
            public float PercentPerCoefficient = 3f;
            /// Enemy hit radius multiplier so a shot at range lands as easily as a bullet.
            public float HitRadiusScale = 1.5f;
            /// Follow surfaces instead of bouncing (Pikachu's Thunder Jolt): after its first contact the
            /// shot crawls along the floor, up walls and across ceilings at its own speed, drops off ledges
            /// and crawls again where it lands.
            public bool Crawl;
            /// Gravity in m/s^2 while a crawling shot is off every surface.
            public float CrawlFall = 22f;
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
        // Crawl mode: after the first surface contact the host owns the motion.
        bool crawling, airborne, measured;
        Vector3 surface, tangent, heading, flight;
        float speed, travelled;
        readonly HashSet<HealthComponent> victims = new HashSet<HealthComponent>();
        readonly List<(ulong key, float until)> puffs = new List<(ulong key, float until)>();

        public static MeleeBouncingShot Spawn(CharacterBody owner, Vector3 point, Vector3 direction, Config cfg)
        {
            if (!MeleeNet.Acts(owner)) return null;
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
            if (cfg.StraightMetres > 0f)
                MeleeNative.ssbm_projectile_straight(shot.projectile, cfg.StraightMetres / cfg.Scale);
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
            ExpirePuffs();
            if (age > cfg.MaxSeconds) { End(true); return; }
            if ((transform.position - origin).sqrMagnitude > cfg.MaxRange * cfg.MaxRange) { End(true); return; }
            var from = transform.position;
            MeleeNative.ssbm_projectile_step(projectile, Time.fixedDeltaTime, default, 0);
            ReadState();
            if ((state.flags & 2) != 0) { End(true); return; }
            float radius = Mathf.Max(state.radius * cfg.Scale, 0.1f);
            if (crawling) { Crawl(radius); return; }
            var next = origin + new Vector3(state.position.x, state.position.y, state.position.z) * cfg.Scale;
            var step = next - from;
            float length = step.magnitude;
            if (!measured && length > 1e-5f) { measured = true; speed = length / Time.fixedDeltaTime; }
            if (length < 1e-5f) { transform.position = next; return; }
            if (!Sweep(from, step / length, length, radius, out var contact)) { transform.position = next; return; }
            var enemy = contact.collider.GetComponent<HurtBox>();
            if (enemy && enemy.healthComponent) { Strike(contact.point); return; }
            Surface(contact, radius);
        }

        /// The nearest thing the shot would touch along a sweep: an enemy, or world geometry. Allies, the
        /// owner and other bodies' non-hurtbox colliders are ignored.
        bool Sweep(Vector3 from, Vector3 dir, float length, float radius, out RaycastHit contact)
        {
            int n = Physics.SphereCastNonAlloc(from, radius, dir, hits, length,
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
            contact = best >= 0 ? hits[best] : default;
            return best >= 0;
        }

        // --- Crawl mode (Thunder Jolt, itpikachutjoltground.c) ----------------------------------------
        // The source's jolt flies down at x4 until it meets the ground, then follows the ground's line
        // from one tick to the next with no corner limit (xC is 0 on the disc), so it climbs whatever the
        // geometry does. Walls and ceilings are the same rule applied to a steeper line (host adaptation:
        // the source's stages only ever had a floor to follow).
        void BeginCrawl(Vector3 normal, Vector3 centre)
        {
            crawling = true;
            airborne = false;
            surface = normal;
            var travel = centre - transform.position;
            var along = new Vector3(state.velocity.x, state.velocity.y, state.velocity.z);
            heading = Vector3.ProjectOnPlane(along.sqrMagnitude > 1e-6f ? along : travel, Vector3.up);
            if (heading.sqrMagnitude < 1e-4f) heading = owner ? owner.transform.forward : Vector3.forward;
            heading.Normalize();
            tangent = Retangent(heading, normal);
            // After landing the jolt hits for the ground jolt's damage, which the sim swaps in on a bounce.
            MeleeNative.ssbm_projectile_bounce(projectile);
            ReadState();
        }

        /// The direction along a surface that carries on from `wanted`: its projection onto the surface,
        /// or, head-on into a wall, up the wall; head-on into a ceiling, the way the shot was heading.
        Vector3 Retangent(Vector3 wanted, Vector3 normal)
        {
            var t = Vector3.ProjectOnPlane(wanted, normal);
            if (t.sqrMagnitude > 0.04f) return t.normalized;
            if (Mathf.Abs(normal.y) < 0.7f)
            {
                var up = Vector3.ProjectOnPlane(Vector3.up, normal);
                if (up.sqrMagnitude > 1e-4f) return up.normalized;
            }
            t = Vector3.ProjectOnPlane(heading, normal);
            return t.sqrMagnitude > 1e-4f ? t.normalized : Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
        }

        void Crawl(float radius)
        {
            float dt = Time.fixedDeltaTime;
            var position = transform.position;
            if (airborne)
            {
                flight.y -= cfg.CrawlFall * dt;
                float flown = flight.magnitude * dt;
                if (flown > 1e-5f && Sweep(position, flight.normalized, flown, radius, out var land))
                {
                    var enemy = land.collider.GetComponent<HurtBox>();
                    if (enemy && enemy.healthComponent) { Strike(land.point); return; }
                    position = land.point + land.normal * (radius + 0.02f);
                    surface = land.normal;
                    var level = Vector3.ProjectOnPlane(flight, Vector3.up);
                    if (level.sqrMagnitude > 1e-4f) heading = level.normalized;
                    tangent = Retangent(flight, surface);
                    airborne = false;
                    Puff(cfg.BouncePuff, position);
                    if (cfg.BounceSound != 0) cfg.PlaySound?.Invoke(cfg.BounceSound);
                }
                else position += flight * dt;
                travelled += flown;
                transform.position = position;
                if (travelled > cfg.MaxRange) End(true);
                return;
            }
            float move = speed * dt;
            // Forward along the surface: an enemy ends the shot, a new surface turns it.
            if (Sweep(position, tangent, move + 0.02f, radius, out var ahead))
            {
                var enemy = ahead.collider.GetComponent<HurtBox>();
                if (enemy && enemy.healthComponent) { Strike(ahead.point); return; }
                if (Vector3.Dot(ahead.normal, surface) < 0.98f)
                {
                    position += tangent * Mathf.Max(ahead.distance - 0.02f, 0f);
                    var previous = tangent;
                    surface = ahead.normal;
                    tangent = Retangent(previous, surface);
                    travelled += move;
                    transform.position = position;
                    Puff(cfg.BouncePuff, position);
                    return;
                }
            }
            position += tangent * move;
            travelled += move;
            // Stay on the surface: pulled back to it, taking the slope under it as the new surface. With
            // nothing within reach the shot has run off an edge and falls.
            if (Sweep(position + surface * (radius * 0.5f), -surface, radius * 3f, radius * 0.6f, out var under)
                && !under.collider.GetComponent<HurtBox>())
            {
                position = under.point + under.normal * (radius + 0.02f);
                surface = under.normal;
                tangent = Vector3.ProjectOnPlane(tangent, surface);
                tangent = tangent.sqrMagnitude < 1e-4f ? Retangent(heading, surface) : tangent.normalized;
            }
            else
            {
                airborne = true;
                flight = tangent * speed;
            }
            transform.position = position;
            if (travelled > cfg.MaxRange) End(true);
        }

        void Surface(RaycastHit contact, float radius)
        {
            var normal = contact.normal;
            var centre = contact.point + normal * (radius + 0.02f);
            if (cfg.Crawl)
            {
                BeginCrawl(normal, centre);
                transform.position = centre;
                Puff(cfg.BouncePuff, centre);
                Arc(centre);
                if (cfg.BounceSound != 0) cfg.PlaySound?.Invoke(cfg.BounceSound);
                return;
            }
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
            Arc(centre);
            if (cfg.BounceSound != 0) cfg.PlaySound?.Invoke(cfg.BounceSound);
        }

        void Arc(Vector3 at)
        {
            if (cfg.BounceViewArticle == uint.MaxValue) return;
            var velocity = new Vector3(state.velocity.x, state.velocity.y, state.velocity.z);
            ShotArc.Spawn(cfg, at, velocity, owner ? owner.transform.forward : Vector3.forward);
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
                owner.GetComponent<MeleeDriver>()?.HitSpark(state.element, state.damage, hurt.transform.position);
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
            ulong key = nextKey++;
            try
            {
                cfg.Particles.Spawn(id, MeleeParticles.At(world, Quaternion.identity, cfg.Scale), key);
                puffs.Add((key, Time.time + Mathf.Max(0.1f, cfg.PuffSeconds)));
            }
            catch (Exception e) { CommonLog.Error("Melee shot puff: " + e.Message); }
        }

        // An untracked source generator would stay in the shared particle system for the
        // whole run; stop each puff once its short authored flash is over.
        void ExpirePuffs()
        {
            if (puffs.Count == 0) return;
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                if (Time.time < puffs[i].until) continue;
                try { cfg.Particles?.Stop(puffs[i].key); } catch (Exception) { }
                puffs.RemoveAt(i);
            }
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
            if (cfg != null && cfg.Particles != null)
                foreach (var puff in puffs) { try { cfg.Particles.Stop(puff.key); } catch (Exception) { } }
            puffs.Clear();
            view?.Dispose(); view = null;
            if (projectile != IntPtr.Zero) { MeleeNative.ssbm_projectile_free(projectile); projectile = IntPtr.Zero; }
        }
    }

    /// A short air-jolt arc at a surface contact. The source ground jolt destroys its 0x4BD
    /// aura on contact and spawns the air-jolt article, whose model runs for 24 ticks
    /// (itpikachutjoltair.c xDF4 <= 24). The host's bouncing arc repeats that flash at each
    /// contact instead of carrying the arc model on the flying orb.
    internal sealed class ShotArc : MonoBehaviour
    {
        MeleeBouncingShot.Config cfg;
        MeleeFighterView view;
        Vector3 direction;
        int ticks;

        internal static void Spawn(MeleeBouncingShot.Config cfg, Vector3 at, Vector3 velocity, Vector3 forward)
        {
            if (cfg == null || cfg.BounceViewArticle == uint.MaxValue) return;
            var go = new GameObject("Melee shot bounce arc");
            go.transform.position = at;
            var arc = go.AddComponent<ShotArc>();
            arc.cfg = cfg;
            arc.direction = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : forward;
            try
            {
                arc.view = new MeleeFighterView(cfg.AssetsDir, cfg.Fighter, cfg.BounceViewArticle, cfg.BounceViewState);
                arc.view.ObjectPose(0f);
            }
            catch (Exception e)
            {
                CommonLog.Warn("Melee shot bounce arc: " + e.Message);
                arc.view?.Dispose();
                arc.view = null;
                Destroy(go);
            }
        }

        void FixedUpdate()
        {
            ticks++;
            if (ticks >= cfg.BounceViewFrames) Destroy(gameObject);
        }

        void LateUpdate()
        {
            if (view == null) return;
            float scale = cfg.Scale * Mathf.Max(0.05f, cfg.BounceViewScale);
            var rotation = Quaternion.LookRotation(direction);
            try
            {
                view.ObjectPose(ticks);
                view.FaceCamera(transform.position, rotation, scale);
                view.Models.Update(scale, d => (transform.position, rotation));
            }
            catch (Exception e)
            {
                CommonLog.Error("Melee shot bounce arc draw: " + e.Message);
                view?.Dispose();
                view = null;
            }
        }

        void OnDestroy()
        {
            view?.Dispose();
            view = null;
        }
    }
}
