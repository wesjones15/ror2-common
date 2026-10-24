using System;
using System.Runtime.InteropServices;

namespace Ror2Common
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ViewParticle
    {
        public ViewVec3 position,velocity;
        public float size,rotation;
        public float r,g,b,a,er,eg,eb,ea;
        public uint texture,flags,glow;
    }
    /// Native effects supply simulation and decoded textures. The shared RoR2
    /// renderer is independent of a fighter, asset archive or native DLL name.
    public interface IParticleSource : IDisposable
    {
        string Error { get; }
        bool Spawn(uint id,float[] transform,ulong key);
        bool Attach(ulong key,float[] transform);
        void Stop(ulong key);
        /// Host presentation override for one emitter's drawn size and alpha.
        bool Tune(uint id,float scale,float alpha,bool glow);
        bool Step(double dt);
        uint Draws(ViewParticle[] output,uint max);
        bool Texture(uint id,out ViewTexture info,byte[] rgba,uint len);
    }
}
