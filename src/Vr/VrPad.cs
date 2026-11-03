#if ROR2COMMON_VR
using UnityEngine;
using UnityEngine.XR;

namespace Ror2Common
{
    // The motion controllers read as they are (Unity's XR input, as the VR mod's own
    // PhysicalInput does), for a character whose layout needs more than RoR2's skill buttons: the
    // analog triggers (the VR mod hands RoR2 on/off skill presses), grips, the face buttons and
    // the sticks, named by hand (dominant / off, following the VR mod's "Left dominant hand").
    // The VR mod still sends its own RoR2 actions from the same buttons (interact, equipment,
    // pause, ping, sprint, turn), so a character maps its moves around the ones it keeps.
    //
    // Read once a frame (Read). Everything reads released while a menu has the pointer
    // (`blocked`), so a button held into the pause menu doesn't come back as a press.
    public sealed class VrPad
    {
        public struct Hand
        {
            /// 0..1 travel.
            public float Trigger, Grip;
            /// The lower face button (A / X), the upper (B / Y), the stick click.
            public bool Primary, Secondary, StickClick;
            public Vector2 Stick;
            /// The device was found this frame.
            public bool Tracked;
        }

        public Hand Dominant, Off;
        Hand lastDominant, lastOff;

        /// Reads both controllers. `blocked`: a menu or the pause screen has them.
        public void Read(bool blocked)
        {
            lastDominant = Dominant;
            lastOff = Off;
            bool left = Vr.LeftHanded;
            Dominant = blocked ? default : ReadHand(left ? XRNode.LeftHand : XRNode.RightHand);
            Off = blocked ? default : ReadHand(left ? XRNode.RightHand : XRNode.LeftHand);
        }

        /// The button went down since the previous Read.
        public static bool Pressed(bool now, bool before) => now && !before;
        public bool DominantPrimaryPressed => Pressed(Dominant.Primary, lastDominant.Primary);
        public bool DominantSecondaryPressed => Pressed(Dominant.Secondary, lastDominant.Secondary);
        public bool OffPrimaryPressed => Pressed(Off.Primary, lastOff.Primary);
        public bool OffSecondaryPressed => Pressed(Off.Secondary, lastOff.Secondary);

        /// A grip or trigger past `threshold` this read and not the read before.
        public static bool AxisPressed(float now, float before, float threshold = 0.5f) => now >= threshold && before < threshold;
        public bool DominantGripPressed => AxisPressed(Dominant.Grip, lastDominant.Grip);
        public bool OffGripPressed => AxisPressed(Off.Grip, lastOff.Grip);

        static Hand ReadHand(XRNode node)
        {
            var h = new Hand();
            var d = InputDevices.GetDeviceAtXRNode(node);
            if (!d.isValid) return h;
            h.Tracked = true;
            if (d.TryGetFeatureValue(CommonUsages.trigger, out float t)) h.Trigger = Mathf.Clamp01(t);
            else if (d.TryGetFeatureValue(CommonUsages.triggerButton, out bool tb) && tb) h.Trigger = 1f;
            if (d.TryGetFeatureValue(CommonUsages.grip, out float g)) h.Grip = Mathf.Clamp01(g);
            else if (d.TryGetFeatureValue(CommonUsages.gripButton, out bool gb) && gb) h.Grip = 1f;
            h.Primary = d.TryGetFeatureValue(CommonUsages.primaryButton, out bool p) && p;
            h.Secondary = d.TryGetFeatureValue(CommonUsages.secondaryButton, out bool s) && s;
            h.StickClick = d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool c) && c;
            if (d.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 v)) h.Stick = v;
            return h;
        }
    }
}
#endif
