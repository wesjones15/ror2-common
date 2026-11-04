#if ROR2COMMON_VR
using UnityEngine;

namespace Ror2Common
{
    // A character's own IMGUI HUD on a panel over the player's off-hand wrist in the headset. A
    // flat game's OnGUI only reaches the desktop mirror window in VR; between Begin and End the
    // same drawing (screen coordinates, Screen.width/height as ever) goes into a screen-sized
    // texture instead (VrSurface), and Show puts that texture on a small quad above the wrist,
    // turned to the eye, the way a watch is read. PointAt maps the dominant pointer onto it, so a
    // character's menu drawn on the HUD can be picked by pointing at it. One panel for the local
    // player, which several drawers share in a frame (a HUD, then its full-screen effects over it):
    // the first Begin of a frame clears it, and Show draws it once however often it's called.
    public static class VrPanel
    {
        /// The panel's width in metres (its height follows the screen's shape).
        public static float Width = 0.26f;
        /// Where its middle sits from the off-hand, in the hand's own frame (metres): above the
        /// back of the wrist, a little toward the elbow.
        public static Vector3 Offset = new Vector3(0f, 0.09f, -0.07f);

        // Over the labels on the sheet (VrSheet) and the hand's own models.
        static readonly VrSurface surface = new VrSurface("VrPanel", 3990);
        static bool shown;
        static int shownFrame = -1;
        static Vector3 at;
        static Quaternion rotation;
        static Vector2 size;

        /// From OnGUI: true while this frame's HUD drawing should go to the panel (call End after
        /// it). False outside a Repaint, or with no panel to draw into; draw nothing then.
        public static bool Begin() => surface.Begin();

        /// Ends Begin's drawing: the screen (or the surface drawn into before) is the target again.
        public static void End() => surface.End();

        /// Each frame it's wanted (LateUpdate): draws the panel over the off-hand. Not called, it
        /// isn't drawn (and PointAt finds nothing).
        public static void Show()
        {
            if (shownFrame == Time.frameCount) return;
            shownFrame = Time.frameCount;
            shown = false;
            var hand = Vr.Hand(false);
            var head = Vr.Head();
            if (!hand || !head || !surface.Ready) return;
            at = hand.position + hand.rotation * Offset;
            var toEye = head.position - at;
            if (toEye.sqrMagnitude < 1e-4f) return;
            // Faces the eye, upright to the head, so it reads however the wrist is turned; turned
            // over with the view in a character's flip (VrRig.RenderTurn).
            rotation = Quaternion.LookRotation(-toEye, head.up);
            (at, rotation) = VrRig.Turned(at, rotation);
            size = new Vector2(Width, Width * surface.Height / surface.Width);
            surface.Draw(Matrix4x4.TRS(at, rotation, new Vector3(size.x, size.y, 1f)));
            shown = true;
        }

        /// Where `ray` meets the panel drawn this frame, in the HUD's own GUI coordinates (pixels
        /// from the top left of the screen), or false off it.
        public static bool PointAt(Ray ray, out Vector2 gui)
        {
            gui = default;
            if (!shown) return false;
            var normal = rotation * Vector3.forward;
            float facing = Vector3.Dot(ray.direction, normal);
            if (Mathf.Abs(facing) < 1e-4f) return false;
            float t = Vector3.Dot(at - ray.origin, normal) / facing;
            if (t < 0f) return false;
            var local = Quaternion.Inverse(rotation) * (ray.origin + ray.direction * t - at);
            float u = local.x / size.x + 0.5f, v = local.y / size.y + 0.5f;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            gui = new Vector2(u * surface.Width, (1f - v) * surface.Height);
            return true;
        }
    }
}
#endif
