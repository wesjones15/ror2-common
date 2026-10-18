using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using Ror2Common;
using UnityEngine;

static class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    class Source : IViewSource, IDisposable
    {
        readonly GCHandle positions, normals, colors, textures, uvs;
        public byte blend, lit;
        public uint model;
        public float alpha = 1f;
        public byte flags;
        public void Tint(Color color) { ((Color[])colors.Target)[0] = color; }
        public Source()
        {
            positions = GCHandle.Alloc(new Vector3[] { new Vector3(1, 2, 3) }, GCHandleType.Pinned);
            normals = GCHandle.Alloc(new Vector3[] { Vector3.up }, GCHandleType.Pinned);
            colors = GCHandle.Alloc(new Color[] { Color.white }, GCHandleType.Pinned);
            textures = GCHandle.Alloc(new int[] { -1 }, GCHandleType.Pinned);
            uvs = GCHandle.Alloc(new Vector2[] { default }, GCHandleType.Pinned);
        }
        public uint ModelCount() => 2;
        public bool Model(uint m, out ViewModelInfo info)
        { info = new ViewModelInfo { vertex_count = 1, index_count = 3, batch_count = 1 }; return true; }
        public uint Batches(uint m, ViewBatch[] o, uint max)
        { o[0] = new ViewBatch { index_count = 3, vertex_count = 1, texture = -1, blend = blend, lit = lit, env = 1 }; return 1; }
        public uint Indices(uint m, int[] o, uint max) => 3;
        public uint Uvs(uint m, float[] o, uint max) => 1;
        public bool Texture(uint id, out ViewTexture info, byte[] rgba, uint len) { info = default; return false; }
        public uint Draws(ViewDraw[] output, uint max)
        {
            if (output != null) for (uint i = 0; i < 2; i++)
                output[i] = new ViewDraw { key = i + 1, model = model, alpha = alpha, flags = flags, rot_w = 1, pos = new ViewVec3(i * 10, 0, 0) };
            return 2;
        }
        public IntPtr Positions(out uint count) { count = 1; return positions.AddrOfPinnedObject(); }
        public IntPtr Normals(out uint count) { count = 1; return normals.AddrOfPinnedObject(); }
        public IntPtr Colors(out uint count) { count = 1; return colors.AddrOfPinnedObject(); }
        public IntPtr TextureIds(out uint count) { count = 1; return textures.AddrOfPinnedObject(); }
        public IntPtr FrameUvs(out uint count) { count = 1; return uvs.AddrOfPinnedObject(); }
        public void Dispose() { positions.Free(); normals.Free(); colors.Free(); textures.Free(); uvs.Free(); }
    }
    sealed class Rigid : Source, IStaticGeometrySource { }
    static object Instance(ModelView view, ulong key)
    {
        var field = typeof(ModelView).GetField("instances", BindingFlags.Instance | BindingFlags.NonPublic);
        return ((IDictionary)field.GetValue(view))[key];
    }
    static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name).GetValue(instance);
    static void Main()
    {
        using (var source = new Rigid())
        {
            var view = ModelView.Create(source, "rigid");
            Check(view != null, "rigid creates");
            Mesh.uploads = Mesh.normalUploads = Mesh.uvUploads = Mesh.boxes = 0;
            MeshRenderer.propertyUpdates = 0;
            view.Update(.01f); view.Update(.01f);
            var first = Field<Mesh>(Instance(view, 1), "mesh");
            Check(ReferenceEquals(first, Field<Mesh>(Instance(view, 2), "mesh")), "rigid instances share mesh");
            Check(Mesh.uploads == 1 && Mesh.normalUploads == 1 && Mesh.uvUploads == 1 && Mesh.boxes == 1, "rigid upload once across instances and frames");
            Check(UnityEngine.Object.searches == 0, "shader-lit models avoid scene light searches");
            Check(first.vertices[0].y == 2f, "actual source geometry uploaded");
            Check(MeshRenderer.propertyUpdates == 2, "unchanged material blocks submitted once per instance");
            source.Tint(new Color(.5f, 1, 1)); view.Update(.01f);
            Check(MeshRenderer.propertyUpdates == 4, "tint change still submitted");
            source.alpha = .5f; view.Update(.01f);
            Check(MeshRenderer.propertyUpdates == 6, "fade change still submitted");
            source.flags = 1; view.Update(.01f); view.Update(.01f);
            Check(MeshRenderer.propertyUpdates == 8, "hit flash updates without resubmitting unchanged state");
            view.FlashColor = new Color(.5f, .2f, 1); view.Update(.01f);
            Check(MeshRenderer.propertyUpdates == 10, "runtime flash colour updates");
            source.flags = 2; view.Update(.01f);
            Check(MeshRenderer.propertyUpdates == 12, "frozen sheen still updates");
            var skin = new Material();
            view.SkinFor = _ => skin; view.Update(.01f); view.Update(.01f);
            Check(MeshRenderer.propertyUpdates == 14, "skin change updates the block recipe");
            view.SkinFor = null; view.Update(.01f);
            Check(MeshRenderer.propertyUpdates == 16, "return to baked material restores its block recipe");
            UnityEngine.Object.Destroy(skin);
            Check(Math.Abs(Field<GameObject>(Instance(view, 2), "go").transform.position.x - .1f) < .000001f, "instance transforms retained");
            source.model = 1;
            view.Update(.01f);
            Check(!first.destroyed && Mesh.uploads == 2, "key reuse keeps old shared model alive and uploads new one once");
            view.Dispose(); view.Dispose();
            Check(first.destroyed && !UnityEngine.Object.doubleDestroyed, "shared mesh disposed exactly once");
            Console.WriteLine("Rigid: one geometry upload for two instances across two frames; animated baseline: four.");
        }
        using (var source = new Source())
        using (var view = ModelView.Create(source, "animated"))
        {
            Mesh.uploads = 0;
            view.Update(1); view.Update(1);
            Check(Mesh.uploads == 4 && !ReferenceEquals(Field<Mesh>(Instance(view, 1), "mesh"), Field<Mesh>(Instance(view, 2), "mesh")), "animated uploads and independent meshes retained");
        }
        using (var source = new Rigid { blend = ViewBlend.Translucent, lit = 1 })
        using (var view = ModelView.Create(source, "rotation-lit"))
        {
            Mesh.uploads = Mesh.colorUploads = 0;
            view.Update(1); view.Update(1);
            Check(Mesh.uploads == 4 && Mesh.colorUploads == 4, "vertex-lit rigid sources retain per-instance geometry and lighting");
            Check(UnityEngine.Object.searches == 1, "native vertex lighting retains light lookup");
        }
        Check(!UnityEngine.Object.doubleDestroyed, "all ownership paths dispose once");
        Console.WriteLine("ModelViewCheck: ok");
    }
}
