using System;
using System.Runtime.InteropServices;

namespace Ror2Common
{
    // What ModelView needs from a native "view" (libsms's sms_view_*, libpikmin's pik_view_*):
    // models loaded once, then per frame a list of posed instances with their skinned vertices.
    // Both ABIs lay these structs out identically.

    [StructLayout(LayoutKind.Sequential)]
    public struct ViewVec3
    {
        public float x, y, z;
        public ViewVec3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    /// One model's sizes and bind-pose bounds (sim units).
    [StructLayout(LayoutKind.Sequential)]
    public struct ViewModelInfo
    {
        public uint vertex_count, index_count, batch_count, texture_count;
        public ViewVec3 lo, hi;
    }

    /// One batch: a submesh with one material.
    [StructLayout(LayoutKind.Sequential)]
    public struct ViewBatch
    {
        public uint first_index, index_count, first_vertex, vertex_count;
        /// Its plain texture, or -1.
        public int texture;
        /// ViewBlend.
        public byte blend;
        /// Lit in the game: 1 colour, 2 alpha.
        public byte lit;
        /// 0 back, 1 front, 2 none.
        public byte cull;
        /// 1: environment mapped (new texture coordinates every frame).
        public byte env;
    }

    public static class ViewBlend
    {
        public const byte Opaque = 0, Cutout = 1, Translucent = 2, Additive = 3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ViewTexture
    {
        public uint width, height;
        /// 0 repeat, 1 clamp, 2 mirror.
        public byte wrap_s, wrap_t, pad0, pad1;
    }

    /// One posed instance.
    [StructLayout(LayoutKind.Sequential)]
    public struct ViewDraw
    {
        public ulong key;
        public uint model;
        public byte kind;
        /// 1: flashing from a hit.
        public byte flags;
        public byte pad0, pad1;
        public ViewVec3 pos;
        public float rot_x, rot_y, rot_z, rot_w;
        public uint first_vertex, first_color;
        public float alpha;
        public uint id;
    }

    /// A view whose batches can fade by their vertices' alpha (`ViewBatch.lit` bit 4): the alpha
    /// each of a model's vertices carries. Optional beside `IViewSource`.
    public interface IVertexAlphaSource
    {
        uint VertexAlphas(uint model, float[] output, uint max);
    }

    /// Opt-in for rigid sources: all instances of a model have identical vertex positions,
    /// normals and frame UVs for this source's entire lifetime. Transforms, textures and
    /// material colours may still change. Animated sources must not implement this.
    public interface IStaticGeometrySource { }

    public interface IViewSource
    {
        uint ModelCount();
        bool Model(uint model, out ViewModelInfo info);
        uint Batches(uint model, ViewBatch[] output, uint max);
        uint Indices(uint model, int[] output, uint max);
        /// Pairs of floats, one pair per vertex.
        uint Uvs(uint model, float[] output, uint max);
        /// `rgba` null asks for the size only.
        bool Texture(uint id, out ViewTexture info, byte[] rgba, uint len);
        /// `output` null asks for the count.
        uint Draws(ViewDraw[] output, uint max);
        IntPtr Positions(out uint count);
        IntPtr Normals(out uint count);
        IntPtr Colors(out uint count);
        IntPtr TextureIds(out uint count);
        IntPtr FrameUvs(out uint count);
    }
}
