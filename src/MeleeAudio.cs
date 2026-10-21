using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ror2Common
{
    /// Pumps one native sim's 32 kHz mixer into the game audio. Fighter-independent:
    /// the sim handle already knows which fighter's banks it plays.
    public sealed class MeleeAudio : IDisposable
    {
        static readonly HashSet<MeleeAudio> active = new HashSet<MeleeAudio>();
        readonly IntPtr sim;
        PcmStream stream;

        public MeleeAudio(IntPtr handle)
        {
            sim = handle;
            try { stream = new PcmStream(32000); active.Add(this); }
            catch (Exception e) { CommonLog.Error("Melee audio unavailable: " + e.Message); }
        }

        static readonly Dictionary<IntPtr, float> gains = new Dictionary<IntPtr, float>();

        /// How loud a sim's sound is (1 is the game's SFX volume); another player's fighter fades with distance.
        public static void SetGain(IntPtr sim, float gain)
        {
            if (sim == IntPtr.Zero) return;
            if (gain >= 0.999f) gains.Remove(sim); else gains[sim] = Mathf.Max(0f, gain);
        }

        public void Update()
        {
            if (stream == null) return;
            float gain = gains.TryGetValue(sim, out var g) ? g : 1f;
            try { stream.Pump(Render, GameSettings.SfxVolume() * gain, Time.timeScale <= 0); }
            catch (Exception e) { CommonLog.Error("Melee audio stopped: " + e.Message + "; " + MeleeNative.Error); Dispose(); }
        }

        bool Render(float[] pcm) => MeleeNative.ssbm_sim_audio(sim, pcm, (uint)pcm.Length / 2) == pcm.Length / 2;

        public void Dispose() { stream?.Dispose(); stream = null; active.Remove(this); }
        public static void StopAll() { foreach (var audio in new List<MeleeAudio>(active)) audio.Dispose(); }
    }
}
