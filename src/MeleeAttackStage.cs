using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Ror2Common
{
    /// A projectile article that a move's Hit event releases (the source's article spawn: Samus's
    /// Charge Shot, missiles and bomb). `Aimed` shots fly along the event's direction and have no
    /// offscreen lifetime cutoff; an unaimed one (the bomb) is placed where the event says.
    /// `Cues` plays the article's own source effects (the missile exhaust) beside it.
    public struct MeleeAttackShot
    {
        public uint Article, State;
        public float Charge;
        public bool Aimed, Cues;
    }

    /// One attack still: the real fighter sim is driven into a move, run `Ticks` fixed steps,
    /// and the pose, effects and articles at that instant are drawn. Fighter-independent; a fighter
    /// supplies only its button, the shot a Hit event releases, and where to point the camera.
    /// Everything in sim units, with the fighter's feet at the origin and facing +Z.
    public sealed class MeleeAttack
    {
        /// Sim pad button bits that start the move (NEUTRAL_B 256, SIDE_B 512, UP_B 1024, DOWN_B 2048).
        public uint Buttons;
        /// Host flag bits a staged special reads every step (Samus's bomb-then-roll is 4).
        public uint HostFlags;
        /// Stick held through the move (the Morph Ball rolls along it).
        public Vector3 Stick;
        public Vector3 Aim = new Vector3(0f, 0f, 1f);
        /// How far into the attack the still is taken: fixed 60 Hz steps from the press. This is the
        /// one number that moves the moment; raise it to see a later part of the move.
        public int Ticks = 20;
        /// Steps the button stays down (a charge move keeps it held; a tap is 1).
        public int Hold = int.MaxValue;
        /// Maps a Hit event to the article it releases; null result: the event spawns nothing.
        public Func<MeleeNative.Event, MeleeAttackShot?> Shot;
        /// The article shown held on the fighter while charging (Samus's charge ball); -1: none.
        public int ChargeArticle = -1;
        /// The camera aims here and shows `Half` sim units above and below it. The camera looks at a
        /// fighter that faces +Z from `Yaw` radians off +Z (positive: from the fighter's right, so the
        /// attack points toward the camera and a little to screen left).
        public Vector3 Focus;
        public float Half = 8f;
        public float Yaw = 0.5f, Pitch = 0.12f;
    }

    /// Builds and holds one `MeleeAttack` still. Owns the sim, the fighter's view, the particle and
    /// effect lifecycles and the article shots; the caller frames a camera, calls `Draw`, renders,
    /// then disposes. Uses the same MeleeEffects/MeleeParticles pipeline the game itself draws with,
    /// so the charge ball, exhaust and bursts sit exactly where they do in play.
    public sealed class MeleeAttackStage : IDisposable
    {
        const float Dt = 1f / 60f;
        static readonly MeleeNative.CollideFn collide = Floor;
        static readonly MeleeNative.EventFn eventFn = Event;

        sealed class Shot
        {
            public IntPtr handle;
            public MeleeFighterView view;
            public MeleeNative.ProjectileState state;
            public Vector3 origin;
            public MeleeAttackShot spec;
            public ulong key;
            public uint frame;
        }

        readonly string assetsDir;
        readonly uint kind;
        readonly MeleeAttack attack;
        readonly IntPtr sim;
        readonly MeleeNative.Host host;
        readonly MeleeFighterView fighter;
        readonly List<IMeleeObjectView> tracked = new List<IMeleeObjectView>();
        readonly List<MeleeNative.Event> queued = new List<MeleeNative.Event>();
        readonly List<Shot> shots = new List<Shot>();
        readonly uint[] cues = new uint[32];
        GCHandle self;
        ulong nextKey = 0x2000000000ul;

        public readonly MeleeParticles Particles;
        readonly MeleeEffects effects;

        public MeleeAttackStage(string assetsDir, uint kind, MeleeAttack attack)
        {
            this.assetsDir = assetsDir;
            this.kind = kind;
            this.attack = attack;
            self = GCHandle.Alloc(this);
            sim = MeleeNative.ssbm_sim_new_fighter(NativeLib.CString(assetsDir), default, kind);
            if (sim == IntPtr.Zero) { self.Free(); throw new InvalidOperationException(MeleeNative.Error); }
            try
            {
                fighter = new MeleeFighterView(assetsDir, kind, 0);
                Particles = new MeleeParticles(assetsDir, 1f);
                Func<IMeleeObjectView> charge = attack.ChargeArticle < 0 ? (Func<IMeleeObjectView>)null
                    : () => Track(new MeleeFighterView(assetsDir, kind, (uint)attack.ChargeArticle, 0));
                effects = new MeleeEffects(Particles, 1f, model => Track(MeleeFighterView.EffectAny(assetsDir, kind, model)), charge, false);
            }
            catch { Dispose(); throw; }
            host = new MeleeNative.Host { user = GCHandle.ToIntPtr(self), collide = Marshal.GetFunctionPointerForDelegate(collide), evt = Marshal.GetFunctionPointerForDelegate(eventFn) };
            MeleeNative.ssbm_sim_facing(sim, new MeleeNative.Vec3(0f, 0f, 1f));
            MeleeNative.ssbm_sim_tuning(sim, new MeleeNative.Tuning { speed_scale = 1f, jump_scale = 1f, extra_jumps = 0, hold_charge = 0, smash_max_frames = 60 });
        }

        IMeleeObjectView Track(IMeleeObjectView view) { tracked.Add(view); return view; }

        static void Floor(IntPtr user, in MeleeNative.Vec3 from, in MeleeNative.Vec3 to, out MeleeNative.Collision c)
        { c = new MeleeNative.Collision { position = new MeleeNative.Vec3(to.x, Mathf.Max(0f, to.y), to.z), grounded = to.y <= 0f ? 1u : 0u }; }

        static void Event(IntPtr user, in MeleeNative.Event e)
        {
            try { if (e.kind == 2) ((MeleeAttackStage)GCHandle.FromIntPtr(user).Target).queued.Add(e); }
            catch (Exception error) { CommonLog.Error("Melee attack still event: " + error.Message); }
        }

        /// Starts the move and runs it to the still's tick.
        public void Run()
        {
            var idle = new MeleeNative.Pad { aim = V(attack.Aim), allowed = 31 };
            for (int i = 0; i < 3; i++) MeleeNative.ssbm_sim_step(sim, in host, in idle, Dt);
            for (int t = 0; t < Mathf.Max(1, attack.Ticks); t++)
            {
                var pad = new MeleeNative.Pad { movement = V(attack.Stick), aim = V(attack.Aim), buttons = t < attack.Hold ? attack.Buttons : 0u, allowed = 31 };
                queued.Clear();
                if (attack.HostFlags != 0) MeleeNative.ssbm_sim_set_host_flags(sim, attack.HostFlags);
                MeleeNative.ssbm_sim_step(sim, in host, in pad, Dt);
                StepShots();
                for (int i = 0; i < queued.Count; i++) Spawn(queued[i]);
                effects.Step(sim, Dt);
                Particles.Step(Dt);
            }
            if (MeleeNative.ssbm_view_update(fighter.Handle, sim, Dt) != 1) throw new InvalidOperationException(MeleeNative.Error);
        }

        static MeleeNative.Vec3 V(Vector3 v) => new MeleeNative.Vec3(v.x, v.y, v.z);

        void Spawn(MeleeNative.Event e)
        {
            var spec = attack.Shot?.Invoke(e);
            if (!spec.HasValue) return;
            var s = spec.Value;
            var aim = new Vector3(e.direction.x, e.direction.y, e.direction.z);
            aim = s.Aimed && aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector3.zero;
            var handle = MeleeNative.ssbm_projectile_new(NativeLib.CString(assetsDir), kind, s.Article, s.State, s.Charge, V(aim));
            if (handle == IntPtr.Zero) { CommonLog.Warn("Melee attack still shot: " + MeleeNative.Error); return; }
            if (s.Aimed) MeleeNative.ssbm_projectile_lifetime(handle, 0f);
            MeleeFighterView view;
            try { view = new MeleeFighterView(assetsDir, kind, s.Article, s.State); }
            catch (Exception error) { CommonLog.Warn("Melee attack still shot view: " + error.Message); MeleeNative.ssbm_projectile_free(handle); return; }
            var shot = new Shot { handle = handle, view = view, spec = s, key = nextKey++, frame = 0, origin = new Vector3(e.position.x, e.position.y, e.position.z) };
            Read(shot);
            shots.Add(shot);
            if (s.Cues)
            {
                var rotation = aim.sqrMagnitude > 0f ? Quaternion.LookRotation(aim) : Quaternion.identity;
                SourceEffects(shot, uint.MaxValue, 0, MeleeParticles.At(shot.origin, rotation, 1f));
            }
        }

        void Read(Shot shot)
        {
            if (MeleeNative.ssbm_projectile_state(shot.handle, out shot.state) != 1) throw new InvalidOperationException(MeleeNative.Error);
        }

        // Mirrors the live Samus shot: step it, keep a grounded article on the floor, and replay the
        // article's source effect graphs from the previous frame to this one.
        void StepShots()
        {
            for (int i = 0; i < shots.Count; i++)
            {
                var shot = shots[i];
                MeleeNative.ssbm_projectile_step(shot.handle, Dt, default, 0);
                Read(shot);
                if (!shot.spec.Aimed && shot.origin.y + shot.state.position.y < 0f)
                {
                    MeleeNative.ssbm_projectile_place(shot.handle, new MeleeNative.Vec3(shot.state.position.x, -shot.origin.y, shot.state.position.z), 1);
                    Read(shot);
                }
                var velocity = new Vector3(shot.state.velocity.x, shot.state.velocity.y, shot.state.velocity.z);
                var rotation = velocity.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
                var at = MeleeParticles.At(shot.origin + new Vector3(shot.state.position.x, shot.state.position.y, shot.state.position.z), rotation, 1f);
                if (shot.spec.Cues)
                {
                    Particles.Attach(shot.key, at);
                    uint frame = (uint)shot.state.frame;
                    SourceEffects(shot, shot.frame, frame, at);
                    shot.frame = frame;
                }
            }
        }

        void SourceEffects(Shot shot, uint previous, uint frame, Matrix4x4 transform)
        {
            uint count = MeleeNative.ssbm_article_cues(shot.spec.Article, shot.spec.State, previous, frame, shot.spec.Charge >= 1f ? 1u : 0u, cues, (uint)cues.Length);
            for (int i = 0; i < count && i < cues.Length; i++)
            {
                int target = MeleeNative.ssbm_effect_target(cues[i]);
                if (target >= 0 && target < int.MaxValue) Particles.Spawn((uint)target, transform, shot.key);
            }
        }

        /// Every live model root this still owns, for layering and framing.
        public IEnumerable<Transform> Roots()
        {
            if (fighter?.Models != null && fighter.Models.Root) yield return fighter.Models.Root;
            foreach (var shot in shots) if (shot.view?.Models != null && shot.view.Models.Root) yield return shot.view.Models.Root;
            foreach (var view in tracked) if (view?.Models != null && view.Models.Root) yield return view.Models.Root;
        }

        /// Places the fighter, its effect models and the articles with the sim origin at `origin`.
        /// Particles are drawn separately, with `Placement(origin)`.
        public void Draw(Vector3 origin)
        {
            if (MeleeNative.ssbm_sim_state(sim, out var state) != 1) throw new InvalidOperationException(MeleeNative.Error);
            var at = origin + new Vector3(state.position.x, state.position.y, state.position.z);
            fighter.Models.Update(1f, d => (at, Quaternion.identity));
            effects.Draw(Placement(origin));
            foreach (var shot in shots)
            {
                shot.view.ObjectPose(shot.state.animation_frame);
                var position = origin + shot.origin + new Vector3(shot.state.position.x, shot.state.position.y, shot.state.position.z);
                var velocity = new Vector3(shot.state.velocity.x, shot.state.velocity.y, shot.state.velocity.z);
                var rotation = velocity.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
                shot.view.FaceCamera(position, rotation, 1f);
                shot.view.Models.Update(1f, d => (position, rotation));
            }
        }

        public static Matrix4x4 Placement(Vector3 origin) => Matrix4x4.Translate(origin);

        public void Dispose()
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                shots[i].view?.Dispose();
                if (shots[i].handle != IntPtr.Zero) MeleeNative.ssbm_projectile_free(shots[i].handle);
            }
            shots.Clear();
            effects?.Dispose();
            Particles?.Dispose();
            fighter?.Dispose();
            if (sim != IntPtr.Zero) MeleeNative.ssbm_sim_free(sim);
            if (self.IsAllocated) self.Free();
        }
    }
}
