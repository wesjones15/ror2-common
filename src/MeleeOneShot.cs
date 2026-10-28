using System;
using UnityEngine;

namespace Ror2Common
{
    /// A single sound from a fighter's extracted SEM/SSM banks, for a presentation that
    /// owns no sim (the shared Egg Lay on a body outside the Melee plugin, whose tongue
    /// still uses Yoshi's disc cue). Owns a hidden sound-only sim and its PCM stream
    /// until the cue is silent; the fighter's own driver keeps using its normal mixer.
    public sealed class MeleeOneShot : MonoBehaviour
    {
        /// A block of WinMM audio is queued ahead, so the source can read silent while the
        /// tail is still playing. Keep the stream alive this long after the play call.
        const float MinimumSeconds = 0.5f;

        IntPtr sim;
        PcmStream stream;
        float started;

        public static void Play(string assetsDir, uint kind, uint sound)
        {
            if (string.IsNullOrEmpty(assetsDir)) return;
            var go = new GameObject("Melee one-shot");
            var shot = go.AddComponent<MeleeOneShot>();
            try {
                shot.sim = MeleeNative.ssbm_sim_new_fighter(NativeLib.CString(assetsDir), default, kind);
                if (shot.sim == IntPtr.Zero) throw new InvalidOperationException(MeleeNative.Error);
                if (MeleeNative.ssbm_sim_play_sound(shot.sim, sound) != 1) throw new InvalidOperationException(MeleeNative.Error);
                shot.stream = new PcmStream(32000);
                shot.started = Time.unscaledTime;
            } catch (Exception e) {
                CommonLog.Warn("Melee one-shot " + sound + ": " + e.Message);
                Destroy(go);
            }
        }

        void Update()
        {
            if (stream == null) { Destroy(gameObject); return; }
            try { stream.Pump(Render, GameSettings.SfxVolume(), Time.timeScale <= 0); }
            catch (Exception e) {
                CommonLog.Warn("Melee one-shot stopped: " + e.Message);
                Destroy(gameObject);
                return;
            }
            if (Time.unscaledTime - started >= MinimumSeconds
                && MeleeNative.ssbm_sim_audio_active(sim) == 0)
                Destroy(gameObject);
        }

        bool Render(float[] pcm)
            => MeleeNative.ssbm_sim_audio(sim, pcm, (uint)pcm.Length / 2) == pcm.Length / 2;

        void OnDestroy()
        {
            stream?.Dispose(); stream = null;
            if (sim != IntPtr.Zero) { MeleeNative.ssbm_sim_free(sim); sim = IntPtr.Zero; }
        }
    }
}
