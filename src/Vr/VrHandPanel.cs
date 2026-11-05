#if ROR2COMMON_VR
using UnityEngine;

namespace Ror2Common
{
    // A character's menu held in the dominant hand in the headset: the radial a button opens,
    // drawn in world space just above that controller and turned to the eye, big enough to read,
    // picked with that hand's stick. Like VrPanel it's IMGUI drawn between Begin and End into a
    // screen-sized texture (VrSurface), but only the square of it the drawer used (`region`, GUI
    // pixels) is shown, so a menu laid out anywhere on the screen fills the panel. Not shown on a
    // frame no one draws (its texture is cleared each frame it's drawn into).
    public static class VrHandPanel
    {
        /// The panel's width in metres.
        public static float Width = 0.2f;
        /// How far above the dominant controller its middle sits (world metres), and how far
        /// toward the eye, so the hand's own model never covers it.
        public static float Above = 0.13f, TowardEye = 0.04f;

        // Over the wrist panel.
        static readonly VrSurface surface = new VrSurface("VrHandPanel", 3992);
        static Rect region;

        /// From OnGUI: true while this frame's menu drawing goes to the hand panel (call End
        /// after it); `region` is the GUI rectangle it draws in. False outside a Repaint.
        public static bool Begin(Rect gui)
        {
            if (!surface.Begin()) return false;
            region = gui;
            return true;
        }

        /// Ends Begin's drawing.
        public static void End() => surface.End();

        /// Each LateUpdate it's wanted: draws last frame's menu over the dominant hand, if one
        /// was drawn.
        public static void Show()
        {
            if (!surface.Fresh || !surface.Ready || region.width < 1f || region.height < 1f) return;
            var hand = Vr.Hand(true);
            var head = Vr.Head();
            if (!hand || !head) return;
            var at = hand.position + Vector3.up * Above;
            var toEye = head.position - at;
            if (toEye.sqrMagnitude < 1e-4f) return;
            at += toEye.normalized * TowardEye;
            var rotation = Quaternion.LookRotation(-toEye, head.up);
            (at, rotation) = VrRig.Turned(at, rotation);
            float w = surface.Width, h = surface.Height;
            var uv = new Rect(region.x / w, 1f - region.yMax / h, region.width / w, region.height / h);
            var size = new Vector3(Width, Width * region.height / region.width, 1f);
            surface.Draw(Matrix4x4.TRS(at, rotation, size), uv);
        }
    }
}
#endif
