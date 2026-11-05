#if ROR2COMMON_VR
using UnityEngine;
using UnityEngine.Rendering;

namespace Ror2Common
{
    // A screen-sized texture IMGUI draws into (between Begin and End, in OnGUI's Repaint, in screen
    // coordinates as ever) and a quad that shows it in the headset: what VrPanel (the wrist) and
    // VrSheet (the labels over the world) are each made of. The first Begin of a frame clears it, so
    // several drawers share it in a frame; a Begin inside another surface's Begin draws here and
    // hands the target back on End.
    internal sealed class VrSurface
    {
        readonly string name;
        readonly int queue;
        RenderTexture texture, before;
        Material material;
        Mesh quad, crop;
        Rect cropUv;
        bool tried, drawing;
        int clearedFrame = -1;

        internal VrSurface(string name, int queue)
        {
            this.name = name;
            this.queue = queue;
        }

        /// The texture is made and holds a frame (for drawing it or mapping a point onto it).
        internal bool Ready => Ensure() && texture.IsCreated();
        internal int Width => texture ? texture.width : Mathf.Max(64, Screen.width);
        internal int Height => texture ? texture.height : Mathf.Max(64, Screen.height);

        internal bool Begin()
        {
            if (drawing || Event.current == null || Event.current.type != EventType.Repaint || !Ensure()) return false;
            int w = Mathf.Max(64, Screen.width), h = Mathf.Max(64, Screen.height);
            if (texture.width != w || texture.height != h)
            {
                texture.Release();
                texture.width = w;
                texture.height = h;
                texture.Create();
            }
            before = RenderTexture.active;
            RenderTexture.active = texture;
            if (clearedFrame != Time.frameCount)
            {
                GL.Clear(false, true, Color.clear);
                clearedFrame = Time.frameCount;
            }
            drawing = true;
            return true;
        }

        internal void End()
        {
            if (!drawing) return;
            RenderTexture.active = before;
            before = null;
            drawing = false;
        }

        /// Drawn into this frame or the last: a LateUpdate (before this frame's OnGUI) shows the last
        /// frame's drawing, and nothing once no drawer has drawn.
        internal bool Fresh => clearedFrame >= Time.frameCount - 1;

        /// Draws the quad (unit square about its middle) with `matrix` this frame.
        internal void Draw(Matrix4x4 matrix)
        {
            if (!Ready) return;
            Graphics.DrawMesh(quad, matrix, material, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        /// Draws only the part `uv` of the texture (0-1, y up) on the quad with `matrix`.
        internal void Draw(Matrix4x4 matrix, Rect uv)
        {
            if (!Ready) return;
            if (!crop)
            {
                crop = new Mesh { name = name + " crop" };
                crop.vertices = quad.vertices;
                crop.triangles = quad.triangles;
                crop.RecalculateBounds();
                cropUv = default;
            }
            if (uv != cropUv)
            {
                cropUv = uv;
                crop.uv = new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMin, uv.yMax) };
            }
            Graphics.DrawMesh(crop, matrix, material, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        bool Ensure()
        {
            if (material && texture) return true;
            if (tried) return false;
            tried = true;
            texture = new RenderTexture(Mathf.Max(64, Screen.width), Mathf.Max(64, Screen.height), 0, RenderTextureFormat.ARGB32)
            {
                name = name,
                useMipMap = true,
                autoGenerateMips = true,
            };
            texture.Create();
            material = Shaders.SeeThrough(name, texture, false, false);
            if (!material) return false;
            // Over the hand's own models and anything else in front of it.
            material.SetFloat("_ZTest", (float)CompareFunction.Always);
            material.renderQueue = queue;
            quad = new Mesh { name = name };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            return true;
        }
    }
}
#endif
