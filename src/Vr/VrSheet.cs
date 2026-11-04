#if ROR2COMMON_VR
using System;
using UnityEngine;

namespace Ror2Common
{
    // Labels a character draws over the world (a carry count over a pellet, a name over a ship part)
    // in the headset. They're IMGUI at a projected screen point, which in VR only reaches the desktop
    // window. Here the same drawing goes onto a sheet held in front of the eye (a VrSurface), and the
    // projection is the sheet's own (WorldToScreenPoint), so each label lands over the thing it names
    // in the headset at the sheet's distance. Flat, every call is the camera's own and the drawing goes
    // to the screen as ever.
    public static class VrSheet
    {
        /// How far in front of the eye the sheet hangs (metres) and its height as an angle (degrees).
        public static float Distance = 2f, Fov = 80f;

        // Under the wrist panel, over the world.
        static readonly VrSurface surface = new VrSurface("VrSheet", 3980);
        static int shownFrame = -1;

        /// A headset is rendering the local view.
        public static bool Active => Vr.Active && Vr.Head();

        /// As Camera.WorldToScreenPoint (pixels from the bottom left, z the depth in metres, negative
        /// behind), onto the sheet in VR, else through `camera`.
        public static Vector3 WorldToScreenPoint(Camera camera, Vector3 world)
        {
            var head = Active ? Vr.Head() : null;
            if (!head) return camera ? camera.WorldToScreenPoint(world) : new Vector3(0f, 0f, -1f);
            var local = Quaternion.Inverse(head.rotation) * (world - head.position);
            if (local.z <= 1e-3f) return new Vector3(0f, 0f, local.z <= 0f ? local.z : -1f);
            float tan = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            return new Vector3((local.x / (local.z * tan * aspect) * 0.5f + 0.5f) * Screen.width,
                (local.y / (local.z * tan) * 0.5f + 0.5f) * Screen.height, local.z);
        }

        /// Runs a label drawer from OnGUI: in VR its Repaint goes onto the sheet (shown this frame), any
        /// other event to the screen; flat, all to the screen.
        public static void Draw(Action draw)
        {
            bool sheet = Active && Event.current != null && Event.current.type == EventType.Repaint;
            if (sheet && !surface.Begin()) return;
            try { draw(); }
            finally { if (sheet) surface.End(); }
        }

        /// Each LateUpdate: shows what was drawn on the sheet this frame, before the eye (once a frame).
        public static void Show()
        {
            if (shownFrame == Time.frameCount) return;
            shownFrame = Time.frameCount;
            var head = Active ? Vr.Head() : null;
            if (!head || !surface.Ready || !surface.Fresh) return;
            float h = 2f * Distance * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float w = h * surface.Width / surface.Height;
            surface.Draw(Matrix4x4.TRS(head.position + head.forward * Distance, head.rotation, new Vector3(w, h, 1f)));
        }
    }
}
#endif
