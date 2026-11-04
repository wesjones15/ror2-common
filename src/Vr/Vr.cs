#if ROR2COMMON_VR
using System;
using System.Reflection;
using BepInEx.Bootstrap;
using RoR2;
using UnityEngine;
using UnityEngine.XR;

namespace Ror2Common
{
    // Opt-in support for Blowntobytes' VR mod (Resurrected_VRMod 2.26.54, still plugin id
    // com.DrBibop.VRMod), compiled in only by a plugin that defines ROR2COMMON_VR and references
    // UnityEngine.XRModule / UnityEngine.VRModule. Nothing links against the VR mod: its hands are
    // reached by reflection (VRMod.MotionControls.HandsReady / GetHandByDominance, VRMod.ModConfig;
    // VRAPI's own dominantHand getter logs an error every call before the hands exist) and its
    // controllers through Unity's XR input (VrPad), so a plugin loads and plays the same without it.
    //
    // What the VR mod already does for any body using RoR2's basic player camera: the headset
    // owns the scene camera (a TrackedPoseDriver under its "VR Camera Rig"), the right stick turns
    // the rig, the stick's move vector follows the head or the off hand (its setting), the
    // crosshair ray is the dominant hand's (CameraFixes.GetVRCrosshairRaycastRay), the body's
    // aimOriginTransform is the dominant muzzle, and it switches off the renderers under the
    // body's model. Our characters are drawn by ModelView outside the model, so hiding them, arms,
    // HUD and anything that read the camera as the aim are each character's to do with these.
    public static class Vr
    {
        public const string PluginGuid = "com.DrBibop.VRMod";

        static bool? installed;
        /// The VR mod is loaded (it may still be running flat, with VR off in its config).
        public static bool Installed
        {
            get
            {
                if (installed == null) installed = Chainloader.PluginInfos.ContainsKey(PluginGuid);
                return installed.Value;
            }
        }

        /// The VR mod is loaded and a headset is rendering.
        public static bool Active
        {
            get
            {
                if (!Installed) return false;
                try { return XRSettings.enabled && XRSettings.isDeviceActive; }
                catch { return false; }
            }
        }

        static bool resolved;
        static PropertyInfo handsReady, leftHanded, motionControls;
        static MethodInfo handByDominance;
        static PropertyInfo handMuzzle, handRenderers;

        static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            if (!Installed) return;
            try
            {
                Assembly vrmod = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == "VRMod") { vrmod = a; break; }
                if (vrmod == null) { CommonLog.Log?.LogWarning("VR: the VR mod's assembly isn't loaded"); return; }
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var controls = vrmod.GetType("VRMod.MotionControls");
                var config = vrmod.GetType("VRMod.ModConfig");
                var hand = vrmod.GetType("VRMod.HandController");
                handsReady = controls?.GetProperty("HandsReady", any);
                handByDominance = controls?.GetMethod("GetHandByDominance", any, null, new[] { typeof(bool) }, null);
                leftHanded = config?.GetProperty("LeftHanded", any);
                motionControls = config?.GetProperty("MotionControlsEnabled", any);
                handMuzzle = hand?.GetProperty("muzzle", any);
                handRenderers = hand?.GetProperty("rendererInfos", any);
                if (handsReady == null || handByDominance == null || handMuzzle == null)
                    CommonLog.Log?.LogWarning("VR: this VR mod build has no hands we know how to read; pointing falls back to the camera");
            }
            catch (Exception e) { CommonLog.Log?.LogWarning($"VR: reading the VR mod failed: {e.Message}"); }
        }

        static bool Get(PropertyInfo p)
        {
            Resolve();
            if (p == null) return false;
            try { return (bool)p.GetValue(null); }
            catch { return false; }
        }

        /// The VR mod's motion controls are on (else the player holds a gamepad in VR).
        public static bool MotionControls => Active && Get(motionControls);
        /// The VR mod's "Left dominant hand".
        public static bool LeftHanded => Installed && Get(leftHanded);
        /// Both of the VR mod's hands exist for the current body.
        public static bool HandsReady => MotionControls && Get(handsReady);

        /// The local player's own body, seen in the headset.
        public static bool IsLocal(CharacterBody body)
        {
            if (!body || !Active) return false;
            var user = LocalUserManager.GetFirstLocalUser();
            return user != null && user.cachedBody == body;
        }

        /// The local body is played with motion controls (pointer aim, VrPad's buttons).
        public static bool IsMotionLocal(CharacterBody body) => IsLocal(body) && HandsReady;

        /// The VR mod's tracked hand (dominant or not), or null.
        public static Transform Hand(bool dominant)
        {
            var c = HandComponent(dominant);
            return c ? c.transform : null;
        }

        /// The hand's muzzle: where the pointer leaves the controller, pointing along it.
        public static Transform Muzzle(bool dominant)
        {
            var c = HandComponent(dominant);
            if (!c || handMuzzle == null) return null;
            try { return handMuzzle.GetValue(c) as Transform; }
            catch { return null; }
        }

        /// The hand's pointer ray (VRMod.HandController.aimRay: from the muzzle along it).
        public static bool Pointer(bool dominant, out Ray ray)
        {
            var m = Muzzle(dominant);
            if (!m) { ray = default; return false; }
            ray = new Ray(m.position, m.forward);
            return true;
        }

        /// The tracked hand on the player's left (or right) controller, whichever is dominant.
        public static Transform HandBySide(bool left) => Hand(left == LeftHanded);

        /// Shows or hides the VR mod's own hand model on a hand (its stand-in pointer hand, for a
        /// character drawing hands of its own there); the pointer's ray stays. Ask every frame:
        /// the VR mod swaps hand models with the body.
        public static void ShowHandModel(bool dominant, bool visible)
        {
            var c = HandComponent(dominant);
            if (!c || handRenderers == null) return;
            try
            {
                if (!(handRenderers.GetValue(c) is CharacterModel.RendererInfo[] infos)) return;
                foreach (var info in infos)
                    if (info.renderer && info.renderer.forceRenderingOff == visible) info.renderer.forceRenderingOff = !visible;
            }
            catch { }
        }

        static Component HandComponent(bool dominant)
        {
            if (!HandsReady || handByDominance == null) return null;
            try { return handByDominance.Invoke(null, new object[] { dominant }) as Component; }
            catch { return null; }
        }

        /// The headset (the local rig's scene camera), or null.
        public static Transform Head()
        {
            var user = LocalUserManager.GetFirstLocalUser();
            var rig = user != null ? user.cameraRigController : null;
            return rig && rig.sceneCam ? rig.sceneCam.transform : null;
        }

        /// Where the headset faces, flattened, or `fallback` without one.
        public static Vector3 HeadFlatForward(Vector3 fallback)
        {
            var head = Head();
            if (!head) return fallback;
            var f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f)
            {
                // Looking straight up or down: the top (or bottom) of the head is the heading.
                f = head.up * (head.forward.y > 0f ? -1f : 1f);
                f.y = 0f;
            }
            return f.sqrMagnitude > 1e-6f ? f.normalized : fallback;
        }

        static readonly RaycastHit[] hits = new RaycastHit[24];

        /// Where `ray` lands in the world (terrain and hurt boxes), past `self`'s own colliders and
        /// anything nearer than `minDistance`; `maxDistance` along the ray when it hits nothing.
        /// The pointer's crosshair: the aim is decoupled from where the player looks.
        public static Vector3 PointerTarget(Ray ray, Transform self, float maxDistance, float minDistance, out bool hit)
        {
            int n = Physics.RaycastNonAlloc(ray, hits, maxDistance,
                LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            hit = false;
            var point = ray.origin + ray.direction * maxDistance;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (!h.collider) continue;
                if (self && h.collider.transform.IsChildOf(self)) continue;
                if (h.distance < minDistance || h.distance >= best) continue;
                best = h.distance;
                point = h.point;
                hit = true;
            }
            return point;
        }
    }
}
#endif
