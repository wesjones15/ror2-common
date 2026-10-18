using System;
using System.Runtime.InteropServices;

namespace Ror2Common
{
    /// The source texture's contribution to alpha. Decode format flags in the
    /// native reader; Unity's usual material-alpha multiplication is not universal.
    public enum TextureAlphaMode { Multiply=0, Blend=1, Replace=2 }

    public static class MaterialAlpha
    {
        static float Unit(float value)
        {
            if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentException("Nonfinite material alpha");
            return Math.Max(0f,Math.Min(1f,value));
        }
        /// Alpha tint for the shared textured shader. The texture retains its
        /// mask/borders; never force all zero material alphas opaque.
        public static float Tint(float material,TextureAlphaMode mode,float blend)
        {
            material=Unit(material);blend=Unit(blend);
            switch(mode) {
                case TextureAlphaMode.Multiply:return material;
                case TextureAlphaMode.Blend:return material+(1f-material)*blend;
                case TextureAlphaMode.Replace:return 1f;
                default:throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
    }

    /// Reusable IViewSource.Colors adapter. Input is RGBA float colours plus one
    /// packed float triplet per colour: raw material alpha, texture blend, mode.
    /// RGB is preserved. Pinned output lasts until Resolve/Dispose; no Unity types
    /// or game-specific flags. Legacy pre-resolved sources may pass no triplets.
    public sealed class MaterialAlphaBuffer:IDisposable
    {
        float[] values;GCHandle pinned;
        public unsafe IntPtr Resolve(IntPtr rgba,uint count,IntPtr sources,uint sourceCount)
        {
            if(sources==IntPtr.Zero&&sourceCount==0)return rgba;
            if(rgba==IntPtr.Zero||sources==IntPtr.Zero||sourceCount!=count||count>1048576)
                throw new ArgumentException("Material alpha/colour count mismatch");
            if(values==null||values.Length<count*4) {
                Dispose();values=new float[checked((int)count*4)];pinned=GCHandle.Alloc(values,GCHandleType.Pinned);
            }
            var input=(float*)rgba;var alpha=(float*)sources;
            for(uint i=0;i<count;i++) {
                values[i*4]=input[i*4];values[i*4+1]=input[i*4+1];values[i*4+2]=input[i*4+2];
                float mode=alpha[i*3+2];
                if(mode<0||mode>2||mode!=(int)mode)throw new ArgumentException("Invalid texture alpha mode");
                values[i*4+3]=MaterialAlpha.Tint(alpha[i*3],(TextureAlphaMode)(int)mode,alpha[i*3+1]);
            }
            return pinned.AddrOfPinnedObject();
        }
        public void Dispose(){if(pinned.IsAllocated)pinned.Free();values=null;}
    }
}
