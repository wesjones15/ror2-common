#if ROR2COMMON_VR
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // The VR mod's camera rig, moved only where a character asks and never turned behind the
    // player's back. The VR mod (CameraFixes.SetCameraStateOverride) anchors the view at a
    // "VRCamera" transform it hangs under the local body at the capsule's eye height, and every
    // frame puts its "VR Camera Rig" there, turned by the player's own turning; the headset's
    // tracked pose sits inside that.
    //
    // `EyeOffset` raises or lowers that anchor from where the VR mod put it (a character's head
    // dropping in a slide): the authored offset, never the headset's own motion, which stays
    // the TrackedPoseDriver's. `RenderTurn` is the one exception to the view being the player's,
    // for a character's opt-in flip: the rig is turned about a pivot just before rendering and
    // put back first thing the next frame, so the gameplay (move direction, pointers, aim) never
    // sees the turn and the VR mod's own rotation is never fed back into itself.
    public static class VrRig
    {
        const string AnchorName = "VRCamera";
        const string RigName = "VR Camera Rig";

        static Transform anchor;
        static Vector3 anchorBase;

        /// Moves the VR mod's view anchor on `body` `metres` up (negative: down) from where the VR
        /// mod put it, in world metres. Zero puts it back.
        public static void EyeOffset(CharacterBody body, float metres)
        {
            if (!body || !Vr.IsLocal(body)) return;
            var t = body.transform.Find(AnchorName);
            if (!t) return;
            if (t != anchor)
            {
                // The VR mod re-anchors (a new capsule, a respawn) by making a new one.
                anchor = t;
                anchorBase = t.localPosition;
            }
            float scale = Mathf.Abs(body.transform.lossyScale.y);
            t.localPosition = anchorBase + Vector3.up * (metres / (scale > 1e-4f ? scale : 1f));
        }

        static Transform rig;
        static Quaternion turn = Quaternion.identity;
        static Vector3 pivot;
        static bool pending, applied;
        static Vector3 savedPosition;
        static Quaternion savedRotation;

        /// Turns the view by `rotation` about the world point `pivot` for this frame's render
        /// only. Ask every frame it should last.
        public static void RenderTurn(Quaternion rotation, Vector3 about)
        {
            if (!Vr.Active) return;
            Restorer.Ensure();
            turn = rotation;
            pivot = about;
            pending = true;
        }

        static Transform Rig()
        {
            if (!rig)
            {
                var go = GameObject.Find(RigName);
                rig = go ? go.transform : null;
            }
            return rig;
        }

        static void BeforeRender()
        {
            if (!pending || applied) return;
            pending = false;
            var r = Rig();
            if (!r) return;
            savedPosition = r.position;
            savedRotation = r.rotation;
            r.SetPositionAndRotation(pivot + turn * (savedPosition - pivot), turn * savedRotation);
            applied = true;
        }

        static void Restore()
        {
            if (!applied) return;
            applied = false;
            if (rig) rig.SetPositionAndRotation(savedPosition, savedRotation);
        }

        // Puts the rig back before anything of the frame reads it.
        [DefaultExecutionOrder(-30000)]
        sealed class Restorer : MonoBehaviour
        {
            static Restorer instance;

            internal static void Ensure()
            {
                if (instance) return;
                var go = new GameObject("Ror2Common VrRig");
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                instance = go.AddComponent<Restorer>();
                Application.onBeforeRender += BeforeRender;
            }

            void Update() => Restore();
            void FixedUpdate() => Restore();

            void OnDestroy()
            {
                Application.onBeforeRender -= BeforeRender;
                Restore();
                if (instance == this) instance = null;
            }
        }
    }
}
#endif
