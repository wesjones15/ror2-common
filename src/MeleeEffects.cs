using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Fighter-independent effect lifecycle: source dispatch and posed attachment
    /// matrices come from the native sim; only the Unity model/particle instances
    /// live here. The per-fighter view factories supply the object models.
    public sealed class MeleeEffects : IDisposable
    {
        sealed class Effect
        {
            public ulong key, serial;
            public uint bone;
            public bool attached, stop, chargeAttachment;
            public Matrix4x4 transform;
            public IMeleeObjectView view;
            public float frame, duration;
            public uint cue, model, version;
            public int versions;
        }

        readonly MeleeParticles particles;
        readonly float scale;
        readonly Func<uint, IMeleeObjectView> effectView;
        readonly Func<IMeleeObjectView> chargeView;
        readonly bool logEffects;
        readonly List<Effect> active = new List<Effect>();
        Effect charge;
        readonly float[] boneMatrix = new float[16];
        MeleeNative.Event[] cues = new MeleeNative.Event[32];
        ulong nextKey = 1;

        public MeleeEffects(MeleeParticles particles, float scale,
            Func<uint, IMeleeObjectView> effectView, Func<IMeleeObjectView> chargeView = null, bool logEffects = false)
        {
            this.particles = particles;
            this.scale = scale;
            this.effectView = effectView;
            this.chargeView = chargeView;
            this.logEffects = logEffects;
        }

        Matrix4x4 Bone(IntPtr sim, uint bone)
        {
            if (MeleeNative.ssbm_sim_bone(sim, bone, boneMatrix) != 1) throw new InvalidOperationException("Effect attachment: " + MeleeNative.Error);
            var matrix = new Matrix4x4();
            for (int i = 0; i < 16; i++) matrix[i] = boneMatrix[i];
            return matrix;
        }

        public void Spawn(uint id, Matrix4x4 transform, ulong serial = 0, uint bone = 0, bool stop = false, IntPtr sim = default)
        {
            Spawn(id, MeleeNative.ssbm_effect_target(id), transform, serial, bone, stop, sim);
            // Some source effects spawn a second one with them (Falcon Punch's two flames, the
            // child slash of Link's Spin Attack).
            int second = MeleeNative.ssbm_effect_companion(id);
            if (second != int.MinValue) Spawn(id, second, transform, serial, bone, stop, sim);
        }

        void Spawn(uint id, int target, Matrix4x4 transform, ulong serial, uint bone, bool stop, IntPtr sim)
        {
            if (target == int.MaxValue) { CommonLog.Error("Unresolved Melee effect " + id); return; }
            if (target == int.MinValue) return;
            if (target < int.MinValue + 1000)
            {
                var point = transform.GetColumn(3);
                ShakeEmitter.CreateSimpleShakeEmitter(new Vector3(point.x, point.y, point.z) * scale, new Wave { amplitude = 0.15f, frequency = 12f, cycleOffset = 0 }, 0.15f, 20f, true);
                return;
            }
            var effect = new Effect { key = nextKey++, serial = serial, bone = bone, stop = stop, transform = transform, attached = (MeleeNative.ssbm_effect_flags(id) & 1) != 0 && sim != IntPtr.Zero, chargeAttachment = id == 1151 };
            if (effect.attached) effect.transform = effect.chargeAttachment && charge != null ? charge.transform : Bone(sim, bone);
            if (target >= 0) particles.Spawn((uint)target, effect.transform, effect.key);
            else if (effectView != null)
            {
                effect.view = effectView((uint)(-target - 1));
                effect.duration = MeleeNative.ssbm_object_duration(effect.view.Handle);
                effect.cue = id;
                effect.model = (uint)(-target - 1);
                if (logEffects) CommonLog.Info("Melee effect " + id + " -> model " + effect.model + ", duration " + effect.duration + " frames");
            }
            if (effect.view != null || effect.attached || effect.stop) active.Add(effect);
            if (active.Count > 128) { var old = active[0]; old.view?.Dispose(); particles.Stop(old.key); active.RemoveAt(0); }
        }

        public void Step(IntPtr sim, float dt, bool readCues = true)
        {
            if (MeleeNative.ssbm_sim_charge_visual(sim, boneMatrix) == 1)
            {
                if (charge == null && chargeView != null) charge = new Effect { view = chargeView() };
                var matrix = new Matrix4x4();
                for (int n = 0; n < 16; n++) matrix[n] = boneMatrix[n];
                charge.transform = matrix;
                charge.frame += dt * 60;
            }
            else if (charge != null) { charge.view.Dispose(); charge = null; }
            ulong serial = MeleeNative.ssbm_sim_motion_serial(sim);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var effect = active[i];
                effect.frame += dt * 60;
                if (effect.stop && effect.serial != serial || effect.view != null && effect.duration > 0 && effect.frame >= effect.duration)
                {
                    LogEnd(effect);
                    effect.view?.Dispose();
                    particles.Stop(effect.key);
                    active.RemoveAt(i);
                    continue;
                }
                if (effect.attached) { effect.transform = effect.chargeAttachment && charge != null ? charge.transform : Bone(sim, effect.bone); particles.Attach(effect.key, effect.transform); }
            }
            if (!readCues) return;
            uint count = MeleeNative.ssbm_sim_gfx(sim, null, 0);
            if (count > 1024) throw new InvalidOperationException("Native GFX cue budget exceeded");
            if (count > cues.Length) cues = new MeleeNative.Event[count];
            if (count > 0 && MeleeNative.ssbm_sim_gfx(sim, cues, (uint)cues.Length) != count) throw new InvalidOperationException(MeleeNative.Error);
            for (int i = 0; i < count; i++)
            {
                var cue = cues[i];
                var facing = new Vector3(cue.direction.x, 0, cue.direction.z);
                var rotation = facing.sqrMagnitude > 0 ? Quaternion.LookRotation(facing) : Quaternion.identity;
                var transform = Matrix4x4.TRS(new Vector3(cue.position.x, cue.position.y, cue.position.z), rotation, new Vector3(-1, 1, 1));
                if (cue.kind == 0x42b) transform *= Matrix4x4.Scale(Vector3.one * cue.strength);
                Spawn(cue.kind, transform, serial, cue.move_id & 65535, (cue.move_id & (1 << 17)) != 0, sim);
            }
        }

        public void Draw(Matrix4x4? placement = null)
        {
            foreach (var effect in Drawing())
            {
                if (effect.view == null) continue;
                effect.view.ObjectPose(effect.frame);
                if (logEffects) { uint version = effect.view.TextureVersion(0); if (version != effect.version) { effect.version = version; effect.versions++; } }
                var transform = effect.transform * Matrix4x4.Scale(new Vector3(-1, 1, 1));
                var p = transform.GetColumn(3);
                var f = transform.GetColumn(2);
                var u = transform.GetColumn(1);
                var position = new Vector3(p.x, p.y, p.z) * scale;
                var rotation = Quaternion.LookRotation(new Vector3(f.x, f.y, f.z), new Vector3(u.x, u.y, u.z));
                if (placement.HasValue) { position = placement.Value.MultiplyPoint3x4(position); rotation = placement.Value.rotation * rotation; }
                float size = transform.GetColumn(0).magnitude * scale;
                effect.view.FaceCamera(position, rotation, size);
                effect.view.Models.Update(size, d => (position, rotation));
            }
        }

        void LogEnd(Effect effect)
        {
            if (effect.view != null && logEffects)
                CommonLog.Info("Melee effect " + effect.cue + " (model " + effect.model + ") ended at frame " + effect.frame.ToString("F1") + " of " + effect.duration + "; first texture changed " + Math.Max(0, effect.versions - 1) + " times while drawn");
        }

        IEnumerable<Effect> Drawing() { foreach (var effect in active) yield return effect; if (charge != null) yield return charge; }

        public void Dispose()
        {
            foreach (var effect in active) { effect.view?.Dispose(); particles.Stop(effect.key); }
            active.Clear();
            charge?.view.Dispose();
            charge = null;
        }
    }
}
