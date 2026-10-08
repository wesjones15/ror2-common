using System;
using System.IO;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Local arrival music, once per body. Each plugin supplies a disc-rendered PCM sting.
    public sealed class SpawnMusic : MonoBehaviour
    {
        static float[] clip;
        static int sampleRate;
        static ConfigEntry<bool> musicEnabled;
        static ConfigEntry<float> volume;
        PcmOutput source;
        bool played;
        bool paused;

        public static void Initialize(ConfigFile config, string folder)
        {
            musicEnabled = config.Bind("SpawnMusic", "Enabled", true, "Play a short musical sting when your character arrives.");
            volume = config.Bind("SpawnMusic", "Volume", 1f, "Sting volume, multiplied by RoR2 master and music volume (0-2).");
            try
            {
                // Our build tools emit canonical stereo PCM16 WAVs. Reject other formats.
                var b = File.ReadAllBytes(System.IO.Path.Combine(folder, "spawn.wav"));
                if (b.Length < 44 || System.Text.Encoding.ASCII.GetString(b, 0, 4) != "RIFF"
                    || System.Text.Encoding.ASCII.GetString(b, 8, 8) != "WAVEfmt "
                    || BitConverter.ToInt32(b, 16) != 16 || BitConverter.ToInt16(b, 20) != 1
                    || BitConverter.ToInt16(b, 22) != 2 || BitConverter.ToInt16(b, 34) != 16
                    || System.Text.Encoding.ASCII.GetString(b, 36, 4) != "data"
                    || BitConverter.ToInt32(b, 40) != b.Length - 44 || (b.Length - 44) % 4 != 0)
                    throw new InvalidDataException("Expected stereo PCM16 WAV");
                int rate = BitConverter.ToInt32(b, 24), frames = (b.Length - 44) / 4;
                if (rate < 8000 || rate > 96000 || frames <= 0 || frames > rate * 10)
                    throw new InvalidDataException("Invalid sting duration or sample rate");
                var pcm = new float[frames * 2];
                for (int i = 0; i < pcm.Length; i++)
                {
                    // Brief attack and release avoid a click or an abrupt musical cutoff.
                    float fade = Mathf.Min(Mathf.Clamp01((i / 2f) / (rate * .01f)),
                        Mathf.Clamp01((frames - 1 - i / 2) / (rate * .18f)));
                    pcm[i] = BitConverter.ToInt16(b, 44 + i * 2) / 32768f * fade;
                }
                clip = pcm; sampleRate = rate;
            }
            catch (Exception e) { CommonLog.Warn("Spawn music unavailable: " + e.Message); }
        }

        public static void PlayOnce(CharacterBody body)
        {
            if (RunAudio.Ended) return;
            if (!body || !body.healthComponent || !body.healthComponent.alive) return;
            bool local = false;
            foreach (var user in LocalUserManager.readOnlyLocalUsersList)
                if (user.cachedBody == body) { local = true; break; }
            if (!local) return;
            var player = body.GetComponent<SpawnMusic>() ?? body.gameObject.AddComponent<SpawnMusic>();
            if (player.played) return;
            player.played = true;
            if (musicEnabled == null || !musicEnabled.Value || clip == null) return;
            try
            {
                float gain = Mathf.Clamp(volume.Value, 0f, 2f) * GameSettings.MusicVolume();
                player.source = new PcmOutput(clip, sampleRate, gain);
                CommonLog.Info($"Arrival sting started ({clip.Length / (2f * sampleRate):F2}s, gain {gain:F2})");
            }
            catch (Exception e) { CommonLog.Warn("Arrival sting failed: " + e.Message); }
        }
        public static void StopAll()
        {
            foreach (var player in FindObjectsOfType<SpawnMusic>())
            {
                if (player.source == null) continue;
                player.source.Dispose();
                player.source = null;
            }
        }

        void OnDestroy() { source?.Dispose(); source = null; }

        void Update()
        {
            if (source == null) return;
            if (!musicEnabled.Value) { source.Dispose(); source = null; return; }
            bool pause = Time.timeScale == 0f;
            if (pause != paused)
            {
                source.Pause(pause);
                paused = pause;
            }
            if (paused) return;
            if (source.Finished) { source.Dispose(); source = null; }
        }
    }
}
