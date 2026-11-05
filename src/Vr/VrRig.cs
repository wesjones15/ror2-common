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
        public static void EyeOffset(CharacterBody body, float metres) => ViewOffset(body, Vector3.up * metres);

        /// Moves the VR mod's view anchor on `body` by `offset` (world metres) from where the VR mod
        /// put it: a third-person view behind and above the body. Zero puts it back.
        public static void ViewOffset(CharacterBody body, Vector3 offset)
        {
            var t = Anchor(body);
            if (!t) return;
            var parent = t.parent ? t.parent : body.transform;
            t.localPosition = anchorBase + parent.InverseTransformVector(offset);
        }

        /// The world height of the VR mod's own eye on `body` (its view anchor before any offset
        /// here), for a character placing the eye at an absolute height. False with no anchor.
        public static bool BaseHeight(CharacterBody body, out float y)
        {
            y = 0f;
            var t = Anchor(body);
            if (!t) return false;
            var parent = t.parent ? t.parent : body.transform;
            y = parent.TransformPoint(anchorBase).y;
            return true;
        }

        static Transform Anchor(CharacterBody body)
        {
            if (!body || !Vr.IsLocal(body)) return null;
            var t = body.transform.Find(AnchorName);
            if (!t) return null;
            if (t != anchor)
            {
                // The VR mod re-anchors (a new capsule, a respawn) by making a new one.
                anchor = t;
                anchorBase = t.localPosition;
            }
            return t;
        }

        /// Where the player has turned the rig (snap or smooth turning), flattened; `fallback`
        /// with no rig. Not the headset: looking about never swings a view placed from this.
        public static Vector3 FlatForward(Vector3 fallback)
        {
            var r = Rig();
            if (!r) return fallback;
            var f = r.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-6f ? f.normalized : fallback;
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

        /// A pose in the rig's tracked space (on a hand, before the headset), turned with this
        /// frame's RenderTurn so it's drawn where the hand will be seen: ask after RenderTurn, for
        /// what's drawn there (a character's own hands, a wrist panel). Unchanged with no turn.
        public static (Vector3, Quaternion) Turned(Vector3 position, Quaternion rotation)
        {
            if (!pending) return (position, rotation);
            return (pivot + turn * (position - pivot), turn * rotation);
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
