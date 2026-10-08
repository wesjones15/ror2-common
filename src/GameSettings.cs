using System.Collections.Generic;
using System.Globalization;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    public static class GameSettings
    {
        /// RoR2's master times sound-effects volume (its convars are 0-100), 0..1.
        public static float SfxVolume()
            => Volume("volume_sfx");

        public static float MusicVolume()
            => Volume("volume_music");

        static float Volume(string channel)
        {
            float Get(string name)
            {
                var cv = RoR2.Console.instance ? RoR2.Console.instance.FindConVar(name) : null;
                return cv != null && float.TryParse(cv.GetString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float v) ? Mathf.Clamp01(v / 100f) : 1f;
            }
            return Get("volume_master") * Get(channel);
        }

        /// The first local player's camera (their rig's scene camera, else Camera.main).
        public static Transform PlayerCamera()
        {
            var user = LocalUserManager.GetFirstLocalUser();
            var rig = user != null ? user.cameraRigController : null;
            if (rig && rig.sceneCam) return rig.sceneCam.transform;
            return Camera.main ? Camera.main.transform : null;
        }

        static readonly List<Renderer> renderers = new List<Renderer>();

        /// Hides (or shows again) a body's own model and everything under it (item displays,
        /// effects parented to it) by switching its renderers off. RoR2's invisibilityCount
        /// (`CharacterModel.UpdateForCamera`: visibility Invisible, no cloak, no effect on AI)
        /// only covers the renderers CharacterModel manages; this covers every renderer in the
        /// model's hierarchy. RoR2 never touches forceRenderingOff, so it stays put. Call every
        /// frame while hiding, as items add renderers.
        public static void HideModel(CharacterBody body, ref bool hidden, bool hide)
        {
            if (!hide && !hidden) return;
            var model = body && body.modelLocator ? body.modelLocator.modelTransform : null;
            if (!model) return;
            HideRenderers(model, hide);
            hidden = hide;
        }

        /// Switches every renderer under `root` off (or back on) with forceRenderingOff.
        public static void HideRenderers(Transform root, bool hide)
        {
            if (!root) return;
            root.GetComponentsInChildren(true, renderers);
            foreach (var r in renderers)
                if (r && r.forceRenderingOff != hide) r.forceRenderingOff = hide;
            renderers.Clear();
        }
    }
}
