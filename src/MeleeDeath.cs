using System;
using UnityEngine;

namespace Ror2Common
{
    /// Shared death presentation: the fighter's extracted fall pose, then the
    /// common ftCo_DeadDown KO effect (0x42b) and sound (0x61). `MeleeDeathSequence`
    /// owns the timing; only the fighter kind and assets dir differ.
    public sealed class MeleeDeath : IMeleeDeathSource
    {
        readonly Vector3 feet;
        readonly Quaternion facing;
        readonly float scale;
        readonly string assetsDir;
        readonly uint kind;
        IntPtr sim;
        MeleeFighterView view;
        MeleeParticles particles;
        MeleeEffects effects;
        PcmStream audio;
        bool vanished;

        public MeleeDeath(string assetsDir, uint kind, uint costume, Vector3 feet, Quaternion facing, float scale, bool logEffects = false)
        {
            this.assetsDir = assetsDir;
            this.kind = kind;
            this.feet = feet;
            this.facing = facing;
            this.scale = scale;
            try {
                view = new MeleeFighterView(assetsDir, kind, costume);
                sim = MeleeNative.ssbm_sim_new_fighter(NativeLib.CString(assetsDir), new MeleeNative.Vec3(feet.x / scale, feet.y / scale, feet.z / scale), kind);
                if (sim == IntPtr.Zero) throw new InvalidOperationException("Melee death sim: " + MeleeNative.Error);
                particles = new MeleeParticles(assetsDir, scale);
                effects = new MeleeEffects(particles, scale, model => MeleeFighterView.Effect(assetsDir, kind, model), null, logEffects);
                try { audio = new PcmStream(32000); } catch (Exception e) { CommonLog.Warn("Melee death audio unavailable: " + e.Message); }
            } catch { Dispose(); throw; }
        }

        public void Fall(float frame)
        {
            // RoR2 adaptation: play the source grounded knockdown before the
            // blast-zone KO, which in Melee normally occurs offscreen at once.
            view.ClipPose("DownBoundU", frame);
        }

        public void Vanish()
        {
            if (vanished) return; vanished = true;
            if (MeleeNative.ssbm_sim_begin_rebirth(sim, NativeLib.CString(assetsDir), new MeleeNative.Vec3(feet.x / scale, feet.y / scale, feet.z / scale)) != 1)
                throw new InvalidOperationException("Melee death KO: " + MeleeNative.Error);
            var host = new MeleeNative.Host(); var pad = new MeleeNative.Pad();
            MeleeNative.ssbm_sim_step(sim, in host, in pad, 1.0 / 60.0);
            effects.Step(sim, 1f / 60f);
        }

        public void Step(float dt)
        {
            if (vanished) { effects.Step(sim, dt, false); particles.Step(dt); }
            audio?.Pump(Render, GameSettings.SfxVolume(), false);
        }

        bool Render(float[] pcm) => MeleeNative.ssbm_sim_audio(sim, pcm, (uint)pcm.Length / 2) == pcm.Length / 2;

        public void Draw()
        {
            if (!vanished) view.Models.Update(scale, d => (feet, facing));
            else view.Models.HideAll();
            if (vanished) { particles.Draw(); effects.Draw(); }
        }

        public void Dispose()
        {
            audio?.Dispose(); audio = null;
            effects?.Dispose(); effects = null;
            particles?.Dispose(); particles = null;
            view?.Dispose(); view = null;
            if (sim != IntPtr.Zero) { MeleeNative.ssbm_sim_free(sim); sim = IntPtr.Zero; }
        }
    }

    /// The shared end-of-run GAME! burst. The model (IfAll.usd) and voice
    /// (0x7C855) are common to every fighter; only the assets dir and kind matter
    /// for the mixer bank.
    public sealed class MeleeGameEffect : IMeleeRoundEffect
    {
        const float Distance = 4f;      // metres in front of the camera
        const float TargetWidth = 3.6f; // desired on-screen width in metres
        readonly string assetsDir;
        readonly uint kind;
        readonly float metresPerUnit;
        IntPtr sim;
        PcmStream stream;
        MeleeFighterView view;
        float frame, scale;

        public MeleeGameEffect(string assetsDir, uint kind, float metresPerUnit)
        {
            this.assetsDir = assetsDir;
            this.kind = kind;
            this.metresPerUnit = metresPerUnit;
            try {
                sim = MeleeNative.ssbm_sim_new_fighter(NativeLib.CString(assetsDir), default, kind);
                if (sim == IntPtr.Zero) throw new InvalidOperationException("Melee GAME sound: " + MeleeNative.Error);
                if (MeleeNative.ssbm_sim_play_sound(sim, 0x7c855) != 1) throw new InvalidOperationException("Melee GAME cue: " + MeleeNative.Error);
                try { stream = new PcmStream(32000); } catch (Exception e) { CommonLog.Warn("Melee GAME audio unavailable: " + e.Message); }
                try {
                    view = MeleeFighterView.Game(assetsDir);
                    // The results-screen burst is a screen-space element authored in
                    // the viewer's handedness; render it without the usual model X
                    // mirror so its "GAME!" text reads left-to-right.
                    view.Models.MirrorX = false;
                    if (MeleeNative.ssbm_view_model(view.Handle, out var info) == 1) {
                        float width = info.hi.x - info.lo.x;
                        if (width > 0.001f) scale = TargetWidth / width;
                    }
                    if (scale <= 0.001f) scale = metresPerUnit;
                } catch (Exception e) { CommonLog.Warn("Melee GAME model unavailable: " + e.Message); view?.Dispose(); view = null; }
            } catch { Dispose(); throw; }
        }

        public void Step(float dt)
        {
            frame += dt * 60f;
            view?.ObjectPose(frame);
            stream?.Pump(Render, GameSettings.SfxVolume(), false);
        }

        bool Render(float[] pcm) => MeleeNative.ssbm_sim_audio(sim, pcm, (uint)pcm.Length / 2) == pcm.Length / 2;

        public void Draw()
        {
            if (view == null) return;
            var camera = GameSettings.PlayerCamera();
            if (!camera) return;
            var position = camera.position + camera.forward * Distance;
            var rotation = camera.rotation;
            view.FaceCamera(position, rotation, scale);
            view.Models.Update(scale, d => (position, rotation));
        }

        public void Dispose() { stream?.Dispose(); stream = null; view?.Dispose(); view = null; if (sim != IntPtr.Zero) { MeleeNative.ssbm_sim_free(sim); sim = IntPtr.Zero; } }
    }
}
