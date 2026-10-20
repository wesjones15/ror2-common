using System;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// The generic Melee character-select presenter: on selection it plays the
    /// fighter's announcer call, runs the fighter's up special once (with the
    /// shared presenter panning the preview camera to follow the rise), then
    /// returns the fighter to idle on the UI clock. Every fighter supplies only
    /// its kind and costume selector; the up-B detection is the shared move id 23
    /// (SpecialHi) and the reset is a shared grounded return to Wait.
    public sealed class MeleeSelectSource : IMeleeSelectSource, IDisplayEffects
    {
        static readonly MeleeNative.CollideFn collide = Floor;
        readonly string assetsDir;
        readonly uint kind;
        readonly Func<uint> costume;
        readonly float scale;
        readonly bool idleOnly;
        readonly bool logEffects;
        readonly Func<uint, IMeleeObjectView> effectView;
        readonly Func<IMeleeObjectView> chargeView;
        readonly MeleeNative.Vec3 introAim;

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
        public MeleeSelectSource(string assetsDir, uint kind, Func<uint> costume, float scale,
            bool idleOnly = false, bool logEffects = false,
            Func<uint, IMeleeObjectView> effectView = null, Func<IMeleeObjectView> chargeView = null,
            bool introUp = false, float speedScale = 1f)
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
            catch { effects?.Dispose(); particles?.Dispose(); view?.Dispose(); MeleeNative.ssbm_sim_free(sim); throw; }
            host = new MeleeNative.Host { collide = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(collide) };
        }

        static void Floor(IntPtr user, in MeleeNative.Vec3 from, in MeleeNative.Vec3 to, out MeleeNative.Collision c)
        { c = new MeleeNative.Collision { position = new MeleeNative.Vec3(to.x, Mathf.Max(0, to.y), to.z), grounded = to.y <= 0 ? 1u : 0u }; }

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
            MeleeNative.ssbm_sim_step(sim, in host, in pad, dt);
            effects?.Step(sim, dt); particles?.Step(dt);
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
        }

        public void Dispose() { effects?.Dispose(); particles?.Dispose(); view?.Dispose(); MeleeNative.ssbm_sim_free(sim); }
    }
}
