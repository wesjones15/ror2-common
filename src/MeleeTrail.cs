using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ror2Common
{
    /// Draws the sword trail the native sim reports (`ssbm_sim_trail`, the source's afterimage in
    /// ftafterimage.c): a translucent strip between the blade's inner and outer edges that fades
    /// out behind a swing. Call `Draw` every frame; it draws nothing while the trail is off.
    public sealed class MeleeTrail : IDisposable
    {
        const int MaxVertices = 96;
        readonly Mesh mesh = new Mesh { name = "Melee sword trail" };
        readonly float[] raw = new float[MaxVertices * 7];
        readonly Vector3[] vertices = new Vector3[MaxVertices];
        readonly Color[] colors = new Color[MaxVertices];
        readonly int[] triangles = new int[(MaxVertices - 2) * 3];
        Material material;
        bool tried;

        public MeleeTrail() { mesh.MarkDynamic(); }

        public void Draw(IntPtr sim, float scale)
        {
            uint count = MeleeNative.ssbm_sim_trail(sim, null, 0);
            if (count < 4) return;
            count = Math.Min(count, (uint)MaxVertices);
            if (MeleeNative.ssbm_sim_trail(sim, raw, count) != count) return;
            if (!tried)
            {
                tried = true;
                material = Shaders.SeeThrough("Melee sword trail", null, false, true);
            }
            if (!material) return;
            int n = (int)count;
            for (int i = 0; i < n; i++)
            {
                vertices[i] = new Vector3(raw[i * 7], raw[i * 7 + 1], raw[i * 7 + 2]) * scale;
                colors[i] = new Color(raw[i * 7 + 3], raw[i * 7 + 4], raw[i * 7 + 5], raw[i * 7 + 6]);
            }
            int t = 0;
            for (int i = 0; i + 2 < n; i++)
            {
                // A strip: alternate the winding so every triangle faces the same way.
                bool even = (i & 1) == 0;
                triangles[t++] = i;
                triangles[t++] = even ? i + 1 : i + 2;
                triangles[t++] = even ? i + 2 : i + 1;
            }
            mesh.Clear();
            mesh.SetVertices(vertices, 0, n);
            mesh.SetColors(colors, 0, n);
            mesh.SetTriangles(triangles, 0, t, 0, true);
            mesh.RecalculateBounds();
            Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        public void Dispose()
        {
            if (mesh) UnityEngine.Object.Destroy(mesh);
            if (material) UnityEngine.Object.Destroy(material);
        }
    }
}
