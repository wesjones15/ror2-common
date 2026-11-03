#if ROR2COMMON_VR
using UnityEngine;
using UnityEngine.Rendering;

namespace Ror2Common
{
    // The pointer's crosshair in the headset: a ring at the world point the pointer lands on, at
    // its real depth (a screen-centre crosshair is meaningless with the aim off the head), kept the
    // same size to the eye at any distance and drawn over the scene so walls and smoke never hide
    // it. Drawn with Graphics.DrawMesh each frame it's shown; nothing to clean up.
    public static class VrCrosshair
    {
        static Mesh quad;
        static Material material;
        static bool tried;
        /// The ring's size to the eye, in degrees across.
        public static float Degrees = 1.6f;

        /// Draws the ring at `point` this frame, facing the headset; `hot` (an enemy under it, or
        /// a lock) colours it.
        public static void Show(Vector3 point, bool hot)
        {
            var head = Vr.Head();
            if (!head || !Ensure()) return;
            var to = point - head.position;
            float dist = to.magnitude;
            if (dist < 0.05f) return;
            // Pulled a little toward the eye so it sits in front of what it marks.
            var at = point - to / dist * Mathf.Min(0.05f, dist * 0.1f);
            float size = 2f * dist * Mathf.Tan(Degrees * 0.5f * Mathf.Deg2Rad);
            var rot = Quaternion.LookRotation(to, head.up);
            material.SetColor("_TintColor", hot ? new Color(1f, 0.35f, 0.25f, 1f) : new Color(1f, 1f, 1f, 0.9f));
            Graphics.DrawMesh(quad, Matrix4x4.TRS(at, rot, Vector3.one * size), material, 0, null, 0, null,
                ShadowCastingMode.Off, false);
        }

        static bool Ensure()
        {
            if (material) return true;
            if (tried) return false;
            tried = true;
            var tex = Ring(64);
            material = Shaders.SeeThrough("VrCrosshair", tex, false, false);
            if (!material) return false;
            material.SetFloat("_ZTest", (float)CompareFunction.Always);
            material.renderQueue = 4000;
            quad = new Mesh { name = "VrCrosshair" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            return true;
        }

        // A soft white ring with a dot, dark-edged so it reads on snow too.
        static Texture2D Ring(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "VrCrosshair", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.75f) / 0.1f);
                    float dot = Mathf.Clamp01(1f - r / 0.15f);
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(r - 0.75f) / 0.2f) * 0.6f;
                    float white = Mathf.Max(ring, dot);
                    float a = Mathf.Max(white, edge);
                    byte v = (byte)(255f * white / Mathf.Max(a, 1e-3f));
                    px[y * n + x] = new Color32(v, v, v, (byte)(255f * a));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}
#endif
