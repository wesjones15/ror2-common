using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// A move whose `Hit` event spawns a native article projectile in the select
    /// intro, mirroring the gameplay shot. The event's direction is the aim and the
    /// configured speed is the launch speed (Melee units per tick); `angleBias` lifts
    /// the launch above the aim's pitch (Yoshi's Egg Throw adds 22 degrees).
    public readonly struct MeleeSelectProjectile
    {
        public readonly uint Move, Article, State;
        public readonly float Speed, AngleBias;
        public MeleeSelectProjectile(uint move, uint article, uint state, float speed, float angleBias = 0f)
        { Move = move; Article = article; State = state; Speed = speed; AngleBias = angleBias; }
    }

    /// The generic Melee character-select presenter: on selection it plays the
    /// fighter's announcer call, runs the fighter's up special once (with the
    /// shared presenter panning the preview camera to follow the rise), then
    /// returns the fighter to idle on the UI clock. Every fighter supplies only
    /// its kind and costume selector; the up-B detection is the shared move id 23
    /// (SpecialHi) and the reset is a shared grounded return to Wait.
    public sealed class MeleeSelectSource : IMeleeSelectSource, IDisplayEffects
    {
        static readonly MeleeNative.CollideFn collide = Floor;
        static readonly MeleeNative.EventFn eventFn = Event;
        readonly string assetsDir;
        readonly uint kind;
        readonly Func<uint> costume;
        readonly float scale;
        readonly bool idleOnly;
        readonly bool logEffects;
        readonly Func<uint, IMeleeObjectView> effectView;
        readonly Func<IMeleeObjectView> chargeView;
        readonly MeleeNative.Vec3 introAim;
        readonly MeleeSelectProjectile[] projectiles;
        readonly List<Shot> shots = new List<Shot>();
        readonly List<MeleeNative.Event> queued = new List<MeleeNative.Event>();
        GCHandle self;

        MeleeFighterView view;
        uint shownCostume;
        readonly IntPtr sim;
        readonly MeleeNative.Host host;
        bool requestIntro, introStarted, sawUpB;
        MeleeParticles particles;
        MeleeEffects effects;
        readonly float[] anchorMatrix = new float[16];
        Vector3 drawOffset;
        static readonly Quaternion previewRotation = Quaternion.identity;

        public bool IntroFinished { get; private set; }
        public bool AudioActive => MeleeNative.ssbm_sim_audio_active(sim) == 1;
        public int AudioRate => 32000;
        public Vector3 DrawOffset { set { drawOffset = value; } }
        public ModelView Models => view.Models;
        public float Scale => scale;

        public Vector3 PoseAnchor
        {
            get
            {
                if (MeleeNative.ssbm_sim_state(sim, out var state) != 1 || MeleeNative.ssbm_sim_bone(sim, 4, anchorMatrix) != 1)
                    throw new InvalidOperationException(MeleeNative.Error);
                var facing = new Vector3(state.facing.x, 0, state.facing.z);
                var posed = new Vector3(anchorMatrix[12] - state.position.x, anchorMatrix[13] - state.position.y, anchorMatrix[14] - state.position.z);
                if (facing.sqrMagnitude > 0.001f) posed = Quaternion.Inverse(Quaternion.LookRotation(facing)) * posed;
                return previewRotation * posed;
            }
        }

        public float RiseMetres
        {
            get
            {
                if (idleOnly || MeleeNative.ssbm_sim_state(sim, out var state) != 1) return 0f;
                return state.position.y * Scale;
            }
        }

        /// <param name="effectView">Optional fighter effect view factory (source model id -> view).</param>
        /// <param name="chargeView">Optional charge-shot article view factory (Samus only).</param>
        /// <param name="introUp">Aim straight up during the intro (a fighter whose up special follows the aim,
        /// like Pikachu's Quick Attack, would otherwise zip sideways out of the preview).</param>
        /// <param name="speedScale">Sim speed scale for the preview (1 = source tuning).</param>
        /// <param name="projectiles">Moves whose Hit event spawns an article projectile in the intro
        /// (Yoshi's Egg Throw). Empty for a body-move up special.</param>
        public MeleeSelectSource(string assetsDir, uint kind, Func<uint> costume, float scale,
            bool idleOnly = false, bool logEffects = false,
            Func<uint, IMeleeObjectView> effectView = null, Func<IMeleeObjectView> chargeView = null,
            bool introUp = false, float speedScale = 1f, MeleeSelectProjectile[] projectiles = null)
        {
            introAim = introUp ? new MeleeNative.Vec3(0, 1, 0) : new MeleeNative.Vec3(0, 0, 1);
            this.assetsDir = assetsDir;
            this.kind = kind;
            this.costume = costume;
            this.scale = scale;
            this.idleOnly = idleOnly;
            this.logEffects = logEffects;
            this.effectView = effectView;
            this.chargeView = chargeView;
            this.projectiles = projectiles ?? Array.Empty<MeleeSelectProjectile>();
            self = GCHandle.Alloc(this);

            sim = MeleeNative.ssbm_sim_new_fighter(NativeLib.CString(assetsDir), default, kind);
            if (sim == IntPtr.Zero) throw new InvalidOperationException(MeleeNative.Error);
            if (speedScale != 1f)
                MeleeNative.ssbm_sim_tuning(sim, new MeleeNative.Tuning { speed_scale = speedScale, jump_scale = 1f, extra_jumps = 0, hold_charge = 0, smash_max_frames = 60 });
            try
            {
                shownCostume = costume();
                view = new MeleeFighterView(assetsDir, kind, shownCostume);
                if (!idleOnly && effectView != null)
                {
                    particles = new MeleeParticles(assetsDir, scale);
                    effects = new MeleeEffects(particles, scale, effectView, chargeView, logEffects);
                }
            }
            catch { effects?.Dispose(); particles?.Dispose(); view?.Dispose(); MeleeNative.ssbm_sim_free(sim); self.Free(); throw; }
            host = new MeleeNative.Host { user = GCHandle.ToIntPtr(self), collide = Marshal.GetFunctionPointerForDelegate(collide), evt = Marshal.GetFunctionPointerForDelegate(eventFn) };
        }

        static void Floor(IntPtr user, in MeleeNative.Vec3 from, in MeleeNative.Vec3 to, out MeleeNative.Collision c)
        { c = new MeleeNative.Collision { position = new MeleeNative.Vec3(to.x, Mathf.Max(0, to.y), to.z), grounded = to.y <= 0 ? 1u : 0u }; }

        static void Event(IntPtr user, in MeleeNative.Event e)
        {
            try { ((MeleeSelectSource)GCHandle.FromIntPtr(user).Target).Queue(e); }
            catch (Exception error) { CommonLog.Error("Melee select event: " + error.Message); }
        }
        void Queue(in MeleeNative.Event e)
        {
            // Only the projectile-move Hit (kind 2) matters; everything else is a body cue.
            if (e.kind == 2 && projectiles.Length > 0) queued.Add(e);
        }

        public void BeginIntro()
        {
            MeleeNative.ssbm_sim_place(sim, default, 0);
            IntroFinished = false; requestIntro = true; sawUpB = false;
            MeleeNative.ssbm_sim_play_sound(sim, MeleeNative.ssbm_announce_sound(kind));
        }

        public bool RenderAudio(float[] stereo) => MeleeNative.ssbm_sim_audio(sim, stereo, (uint)stereo.Length / 2) == stereo.Length / 2;

        public void Step(float dt)
        {
            uint selected = costume();
            if (selected != shownCostume) { var replacement = new MeleeFighterView(assetsDir, kind, selected); view.Dispose(); view = replacement; shownCostume = selected; }
            if (idleOnly) { view.IdlePose(); view.LocalPosition = drawOffset; view.LocalRotation = previewRotation; return; }
            var pad = new MeleeNative.Pad { aim = introAim, allowed = 0 };
            if (requestIntro)
            {
                Advance(in pad, 1f / 60);
                requestIntro = false; introStarted = true;
                pad.buttons = 1024; pad.allowed = 8; // up special (SpecialHi)
                Advance(in pad, 1f / 60);
                pad.buttons = 0; pad.allowed = 0;
            }
            Advance(in pad, dt);
            if (introStarted)
            {
                if (MeleeNative.ssbm_sim_state(sim, out var state) != 1) throw new InvalidOperationException(MeleeNative.Error);
                if (state.move_id == 23) sawUpB = true;
                if (sawUpB && state.move_id != 23 && state.grounded != 0)
                {
                    IntroFinished = true; introStarted = false;
                    MeleeNative.ssbm_sim_place(sim, default, 0); Advance(in pad, 1f / 60);
                }
            }
            view.Pose(sim, dt);
            if (MeleeNative.ssbm_sim_state(sim, out var rendered) != 1) throw new InvalidOperationException(MeleeNative.Error);
            view.LocalPosition = drawOffset + new Vector3(rendered.position.x, rendered.position.y, rendered.position.z);
            view.LocalRotation = previewRotation;
        }

        void Advance(in MeleeNative.Pad pad, float dt)
        {
            queued.Clear();
            MeleeNative.ssbm_sim_step(sim, in host, in pad, dt);
            for (int i = 0; i < queued.Count; i++) SpawnProjectile(queued[i]);
            queued.Clear();
            StepShots(dt);
            effects?.Step(sim, dt); particles?.Step(dt);
        }

        // --- Article projectiles (Yoshi's Egg Throw) -----------------------------
        sealed class Shot
        {
            public IntPtr handle;
            public MeleeFighterView view;
            public MeleeNative.ProjectileState state;
            public Vector3 spawn; // sim units, the throw point the projectile moves from
            public float age;
        }
        void SpawnProjectile(MeleeNative.Event e)
        {
            for (int i = 0; i < projectiles.Length; i++)
            {
                var spec = projectiles[i];
                if (spec.Move != e.move_id) continue;
                var aim = new Vector3(e.direction.x, e.direction.y, e.direction.z);
                if (aim.sqrMagnitude < 0.0001f) aim = Vector3.forward;
                aim = aim.normalized;
                // Match the gameplay shot: lift the launch above the aim pitch by the bias.
                float pitch = Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg;
                float angle = Mathf.Clamp(pitch + spec.AngleBias, 5f, 70f) * Mathf.Deg2Rad;
                var flat = new Vector3(aim.x, 0f, aim.z);
                flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
                var launch = flat * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
                var handle = MeleeNative.ssbm_projectile_new(NativeLib.CString(assetsDir), kind, spec.Article, spec.State, spec.Speed, new MeleeNative.Vec3(launch.x, launch.y, launch.z));
                if (handle == IntPtr.Zero) { CommonLog.Warn("Melee select projectile: " + MeleeNative.Error); continue; }
                MeleeNative.ssbm_projectile_lifetime(handle, 0);
                MeleeFighterView projectileView;
                try { projectileView = new MeleeFighterView(assetsDir, kind, spec.Article, spec.State); }
                catch (Exception error) { CommonLog.Warn("Melee select projectile view: " + error.Message); MeleeNative.ssbm_projectile_free(handle); continue; }
                // Throw from roughly hand height above the fighter's feet.
                shots.Add(new Shot { handle = handle, view = projectileView, spawn = new Vector3(e.position.x, e.position.y, e.position.z) + Vector3.up * 1.5f });
            }
        }
        void StepShots(float dt)
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var shot = shots[i];
                shot.age += dt;
                MeleeNative.ssbm_projectile_step(shot.handle, dt, default, 0);
                if (MeleeNative.ssbm_projectile_state(shot.handle, out var state) != 1) { FreeShot(i); continue; }
                shot.state = state;
                // Finished (native despawn) or fell through the select floor.
                if ((state.flags & 2) != 0 || (shot.spawn.y + state.position.y) * scale < 0f || shot.age > 8f)
                { FreeShot(i); }
            }
        }
        void FreeShot(int i)
        {
            var shot = shots[i];
            shot.view?.Dispose();
            if (shot.handle != IntPtr.Zero) { MeleeNative.ssbm_projectile_free(shot.handle); shot.handle = IntPtr.Zero; }
            shots.RemoveAt(i);
        }

        public void Draw(Transform display)
        {
            if (idleOnly) return;
            MeleeNative.ssbm_sim_state(sim, out var state);
            var facing = new Vector3(state.facing.x, 0, state.facing.z);
            var heading = facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : Quaternion.identity;
            var root = Matrix4x4.TRS(display.position, display.rotation, Vector3.one)
                * Matrix4x4.Translate(view.LocalPosition * Scale) * Matrix4x4.Rotate(previewRotation)
                * Matrix4x4.Rotate(Quaternion.Inverse(heading)) * Matrix4x4.Translate(-new Vector3(state.position.x, state.position.y, state.position.z) * Scale);
            particles?.Draw(root); effects?.Draw(root);
            DrawShots(root);
        }
        void DrawShots(Matrix4x4 root)
        {
            for (int i = 0; i < shots.Count; i++)
            {
                var shot = shots[i];
                shot.view.ObjectPose(shot.state.animation_frame);
                var position = (shot.spawn + new Vector3(shot.state.position.x, shot.state.position.y, shot.state.position.z)) * scale;
                var velocity = new Vector3(shot.state.velocity.x, shot.state.velocity.y, shot.state.velocity.z);
                var rotation = velocity.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
                position = root.MultiplyPoint3x4(position);
                rotation = root.rotation * rotation;
                shot.view.FaceCamera(position, rotation, scale);
                shot.view.Models.Update(scale, d => (position, rotation));
            }
        }

        public void Dispose()
        {
            effects?.Dispose(); particles?.Dispose(); view?.Dispose();
            for (int i = shots.Count - 1; i >= 0; i--) FreeShot(i);
            MeleeNative.ssbm_sim_free(sim);
            if (self.IsAllocated) self.Free();
        }
    }
}
