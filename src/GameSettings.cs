using System.Globalization;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    public static class GameSettings
    {
        /// RoR2's master times sound-effects volume (its convars are 0-100), 0..1.
        public static float SfxVolume()
        {
            float Get(string name)
            {
                var cv = RoR2.Console.instance ? RoR2.Console.instance.FindConVar(name) : null;
                return cv != null && float.TryParse(cv.GetString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float v) ? Mathf.Clamp01(v / 100f) : 1f;
            }
            return Get("volume_master") * Get("volume_sfx");
        }

        /// The first local player's camera (their rig's scene camera, else Camera.main).
        public static Transform PlayerCamera()
        {
            var user = LocalUserManager.GetFirstLocalUser();
            var rig = user != null ? user.cameraRigController : null;
            if (rig && rig.sceneCam) return rig.sceneCam.transform;
            return Camera.main ? Camera.main.transform : null;
        }

        /// Hides (or shows again) a body's own model, counting like RoR2's cloaking does.
        public static void HideModel(CharacterBody body, ref bool hidden, bool hide)
        {
            if (hide == hidden || !body || !body.modelLocator || !body.modelLocator.modelTransform) return;
            var cm = body.modelLocator.modelTransform.GetComponent<CharacterModel>();
            if (!cm) return;
            cm.invisibilityCount += hide ? 1 : -1;
            hidden = hide;
        }
    }
}
