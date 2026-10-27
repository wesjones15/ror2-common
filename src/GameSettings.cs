using System.Collections.Generic;
using Path = System.IO.Path;
using BepInEx;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    public static class GameSettings
    {
        static ConfigEntry<float> outputDb;

        static float OutputDb
        {
            get
            {
                if (outputDb == null)
                {
                    // Shared filename: both separately compiled plugins read the same setting.
                    var config = new ConfigFile(Path.Combine(Paths.ConfigPath, "Ror2Common.Audio.cfg"), true);
                    outputDb = config.Bind("Output", "GainDb", AudioGain.DefaultOutputDb,
                        new ConfigDescription("Level of ALL native mod audio: Mario, Olimar, voices, effects and stings. " +
                            "Applied before output clipping; does not change tone or the game's own audio. " +
                            "-4 dB is the default; 0 restores the old unattenuated output. Restart the game after editing.",
                            new AcceptableValueRange<float>(-60f, 0f)));
                    CommonLog.Info($"Native audio output: {outputDb.Value:F1} dB; shared config Ror2Common.Audio.cfg");
                }
                return outputDb.Value;
            }
        }

        /// Shared native mix level times RoR2's master and sound-effects settings.
        public static float SfxVolume()
            => AudioGain.Sfx(Volume("volume_master"), Volume("volume_sfx"), OutputDb);

        public static float MusicVolume()
            // DLC4's VolumeConVar folds master into Volume_MSX when either master or
            // parent_volume_music changes. GetString already returns that effective value.
            => AudioGain.Music(Volume("volume_music"), OutputDb);

        static float Volume(string channel)
        {
            var cv = RoR2.Console.instance ? RoR2.Console.instance.FindConVar(channel) : null;
            // Do not produce a full-volume burst while console/audio settings are unavailable.
            return AudioGain.Percent(cv?.GetString());
        }

        /// While set, `PlayerCamera` answers with this transform. An offscreen capture (the skill
        /// portraits) sets it so camera-facing billboards turn toward the capture camera, and clears it.
        public static Transform CameraOverride;

        /// The first local player's camera (their rig's scene camera, else Camera.main).
        public static Transform PlayerCamera()
        {
            if (CameraOverride) return CameraOverride;
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
