#if ROR2COMMON_VR
using UnityEngine;
using UnityEngine.Rendering;

namespace Ror2Common
{
    // A character's own IMGUI HUD on a panel over the player's off-hand wrist in the headset. A
    // flat game's OnGUI only reaches the desktop mirror window in VR; between Begin and End the
    // same drawing (screen coordinates, Screen.width/height as ever) goes into a screen-sized
    // texture instead, and Show puts that texture on a small quad above the wrist, turned to the
    // eye, the way a watch is read. PointAt maps the dominant pointer onto it, so a character's
    // menu drawn on the HUD can be picked by pointing at it. One panel for the local player.
    public static class VrPanel
    {
        /// The panel's width in metres (its height follows the screen's shape).
        public static float Width = 0.26f;
        /// Where its middle sits from the off-hand, in the hand's own frame (metres): above the
        /// back of the wrist, a little toward the elbow.
        public static Vector3 Offset = new Vector3(0f, 0.09f, -0.07f);

        static RenderTexture texture;
        static RenderTexture before;
        static Material material;
        static Mesh quad;
        static bool tried, drawing, shown;
        static Vector3 at;
        static Quaternion rotation;
        static Vector2 size;

        /// From OnGUI: true while this frame's HUD drawing should go to the panel (call End after
        /// it). False outside a Repaint, or with no panel to draw into; draw nothing then.
        public static bool Begin()
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
            GL.Clear(false, true, Color.clear);
            drawing = true;
            return true;
        }

        /// Ends Begin's drawing: the screen is the target again.
        public static void End()
        {
            if (!drawing) return;
            RenderTexture.active = before;
            before = null;
            drawing = false;
        }

        /// Each frame it's wanted (LateUpdate): draws the panel over the off-hand. Not called, it
        /// isn't drawn (and PointAt finds nothing).
        public static void Show()
        {
            shown = false;
            var hand = Vr.Hand(false);
            var head = Vr.Head();
            if (!hand || !head || !Ensure() || !texture.IsCreated()) return;
            at = hand.position + hand.rotation * Offset;
            var toEye = head.position - at;
            if (toEye.sqrMagnitude < 1e-4f) return;
            // Faces the eye, upright to the head, so it reads however the wrist is turned.
            rotation = Quaternion.LookRotation(-toEye, head.up);
            size = new Vector2(Width, Width * texture.height / texture.width);
            Graphics.DrawMesh(quad, Matrix4x4.TRS(at, rotation, new Vector3(size.x, size.y, 1f)), material, 0, null, 0,
                null, ShadowCastingMode.Off, false);
            shown = true;
        }

        /// Where `ray` meets the panel drawn this frame, in the HUD's own GUI coordinates (pixels
        /// from the top left of the screen), or false off it.
        public static bool PointAt(Ray ray, out Vector2 gui)
        {
            gui = default;
            if (!shown || !texture) return false;
            var normal = rotation * Vector3.forward;
            float facing = Vector3.Dot(ray.direction, normal);
            if (Mathf.Abs(facing) < 1e-4f) return false;
            float t = Vector3.Dot(at - ray.origin, normal) / facing;
            if (t < 0f) return false;
            var local = Quaternion.Inverse(rotation) * (ray.origin + ray.direction * t - at);
            float u = local.x / size.x + 0.5f, v = local.y / size.y + 0.5f;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            gui = new Vector2(u * texture.width, (1f - v) * texture.height);
            return true;
        }

        static bool Ensure()
        {
            if (material && texture) return true;
            if (tried) return false;
            tried = true;
            texture = new RenderTexture(Mathf.Max(64, Screen.width), Mathf.Max(64, Screen.height), 0, RenderTextureFormat.ARGB32)
            {
                name = "VrPanel",
                useMipMap = true,
                autoGenerateMips = true,
            };
            texture.Create();
            material = Shaders.SeeThrough("VrPanel", texture, false, false);
            if (!material) return false;
            // Over the hand's own models (Mario's glove under it) and anything else near the wrist.
            material.SetFloat("_ZTest", (float)CompareFunction.Always);
            material.renderQueue = 3990;
            quad = new Mesh { name = "VrPanel" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            return true;
        }
    }
}
#endif
