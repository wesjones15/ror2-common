using System;
using UnityEngine;

namespace Ror2Common
{
    /// Native particle bank wrapper. Fighter-independent: the bank is selected by
    /// the assets dir and effect ids, not by a character.
    public sealed class MeleeParticles : IDisposable
    {
        readonly ParticleView view;

        public MeleeParticles(string assetsDir, float scale) { view = new ParticleView(new Source(assetsDir), scale); }
        public static Matrix4x4 At(Vector3 world, Quaternion rotation, float scale) => ParticleView.At(world, rotation, scale);
        public void Spawn(uint id, Matrix4x4 transform, ulong key) => view.Spawn(id, transform, key);
        public void Attach(ulong key, Matrix4x4 transform) => view.Attach(key, transform);
        public void Stop(ulong key) => view.Stop(key);
        public void Tune(uint id, float scale, float alpha, bool glow = false) => view.Tune(id, scale, alpha, glow);
        public void Step(float dt) => view.Step(dt);
        public void Draw(Matrix4x4? placement = null) => view.Draw(placement);
        public void Draw(Camera camera, int layer, Matrix4x4? placement = null) => view.Draw(camera, layer, placement);
        public void Dispose() => view.Dispose();

        sealed class Source : IParticleSource
        {
            IntPtr handle;
            public Source(string assetsDir)
            {
                handle = MeleeNative.ssbm_particles_new(NativeLib.CString(assetsDir));
                if (handle == IntPtr.Zero) throw new InvalidOperationException(MeleeNative.Error);
            }
            public string Error => MeleeNative.Error;
            public bool Spawn(uint id, float[] transform, ulong key) => MeleeNative.ssbm_particles_spawn(handle, id, transform, key) == 1;
            public bool Attach(ulong key, float[] transform) => MeleeNative.ssbm_particles_attach(handle, key, transform) == 1;
            public void Stop(ulong key) => MeleeNative.ssbm_particles_stop(handle, key);
            public bool Tune(uint id, float scale, float alpha, bool glow) => MeleeNative.ssbm_particles_tune(handle, id, scale, alpha, glow ? 1u : 0u) == 1;
            public bool Step(double dt) => MeleeNative.ssbm_particles_step(handle, dt) == 1;
            public uint Draws(ViewParticle[] output, uint max) => MeleeNative.ssbm_particles_draws(handle, output, max);
            public bool Texture(uint id, out ViewTexture info, byte[] rgba, uint len) => MeleeNative.ssbm_particles_texture(handle, id, out info, rgba, len) == 1;
            public void Dispose() { if (handle != IntPtr.Zero) { MeleeNative.ssbm_particles_free(handle); handle = IntPtr.Zero; } }
        }
    }
}
