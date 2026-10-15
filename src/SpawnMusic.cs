using System;
using System.IO;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Local stings, each plugin supplying disc-rendered PCM: `spawn.wav` once a run, as the
    /// character first arrives on the run's first stage, and `death.wav` (optional) when the
    /// character dies. Optional extras: `gameover.wav` plays in place of the death sting when
    /// the character dies (`death.wav` is then for `PlayMiss`, a lesser loss), and `bluecoin.wav`
    /// is a cue for `PlayCue("bluecoin")`, as are `shineget` (the Shine Get fanfare) and `secret`.
    public sealed class SpawnMusic : MonoBehaviour
    {
        sealed class Clip
        {
            public float[] pcm;
            public int rate;
        }

        static Clip arrival, death;
        static Func<float> deathGain;
        static readonly System.Collections.Generic.Dictionary<string, Clip> extras =
            new System.Collections.Generic.Dictionary<string, Clip>();
        static readonly string[] extraNames = { "gameover", "bluecoin", "shineget", "secret" };
        static ConfigEntry<bool> musicEnabled, deathEnabled, cuesEnabled;
        static ConfigEntry<float> volume, deathVolume;
        // The run whose arrival sting has played (it plays once, on the first stage).
        static Run playedRun;
        PcmOutput source;
        bool paused;
        bool isDeath;

        /// Disc-rendered death SFX that must survive the native scene stream's teardown.
        public static void SetDeathPcm(float[] pcm, int rate, Func<float> gain)
        {
            death = new Clip { pcm = pcm, rate = rate };
            deathGain = gain;
        }
        // Which switch ends this one when turned off (cues have their own).
        ConfigEntry<bool> gate;

        public static void Initialize(ConfigFile config, string folder)
        {
            musicEnabled = config.Bind("SpawnMusic", "Enabled", true, "Play a short musical sting when your character arrives on a run's first stage.");
            deathEnabled = config.Bind("SpawnMusic", "DeathSting", true, "Play a short musical sting when your character dies.");
            cuesEnabled = config.Bind("SpawnMusic", "Cues", true, "Play the character's other disc sounds (a game over song, a blue coin's jingle) when their files are there.");
            volume = config.Bind("SpawnMusic", "Volume", 1f, "Sting volume, multiplied by RoR2 master and music volume (0-2).");
            deathVolume = config.Bind("SpawnMusic", "DeathVolume", 1.15f, "Extra gain for the death sting, on top of Volume (0-2). The miss jingle is quiet next to the game's own audio.");
            try { arrival = Load(System.IO.Path.Combine(folder, "spawn.wav")); }
            catch (Exception e) { CommonLog.Warn("Spawn music unavailable: " + e.Message); }
            foreach (var name in extraNames)
            {
                var path = System.IO.Path.Combine(folder, name + ".wav");
                if (!File.Exists(path)) continue;
                try { extras[name] = Load(path); }
                catch (Exception e) { CommonLog.Warn($"Cue {name} unavailable: " + e.Message); }
            }
            var deathPath = System.IO.Path.Combine(folder, "death.wav");
            if (File.Exists(deathPath))
            {
                try { death = Load(deathPath); }
                catch (Exception e) { CommonLog.Warn("Death sting unavailable: " + e.Message); }
            }
        }

        // Our build tools emit canonical stereo PCM16 WAVs. Reject other formats.
        static Clip Load(string path)
        {
            var b = File.ReadAllBytes(path);
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
            return new Clip { pcm = pcm, rate = rate };
        }

        static bool IsLocal(CharacterBody body)
        {
            foreach (var user in LocalUserManager.readOnlyLocalUsersList)
                if (user.cachedBody == body || (body.master && user.cachedMaster == body.master)) return true;
            return false;
        }

        /// The arrival sting, if `body` is a local player's and this is the run's first stage
        /// and its sting hasn't played yet.
        public static void PlayOnce(CharacterBody body)
        {
            if (RunAudio.Ended) return;
            if (!body || !body.healthComponent || !body.healthComponent.alive) return;
            var run = Run.instance;
            if (!run || run.stageClearCount > 0 || playedRun == run) return;
            if (!IsLocal(body)) return;
            playedRun = run;
            if (musicEnabled == null || !musicEnabled.Value || arrival == null) return;
            var player = body.GetComponent<SpawnMusic>() ?? body.gameObject.AddComponent<SpawnMusic>();
            player.Begin(arrival, "Arrival");
        }

        /// The death sting for a local player's `body`, now. It plays on its own object, so it
        /// outlasts the body and a game over (leaving the run stops it).
        public static void PlayDeath(CharacterBody body)
        {
            if (!body || !IsLocal(body)) return;
            if (deathEnabled == null || !deathEnabled.Value) return;
            // A character with a game over song dies to it.
            if (!extras.TryGetValue("gameover", out var clip)) clip = death;
            if (clip == null) return;
            var player = new GameObject("DeathSting").AddComponent<SpawnMusic>();
            player.isDeath = true;
            player.Begin(clip, "Death", Mathf.Clamp(deathVolume.Value, 0f, 2f), deathGain);
        }

        /// The death sting itself (`death.wav`), for a loss short of dying: a fall off the map. Only
        /// differs from `PlayDeath` for a character that has a game over song.
        public static void PlayMiss(CharacterBody body)
        {
            if (!body || !IsLocal(body)) return;
            if (deathEnabled == null || !deathEnabled.Value || death == null) return;
            var player = new GameObject("MissSting").AddComponent<SpawnMusic>();
            player.isDeath = true;
            player.Begin(death, "Miss", Mathf.Clamp(deathVolume.Value, 0f, 2f));
        }

        /// One of the character's extra cues (`bluecoin`, `shineget`, `secret`), now, for the local
        /// player. False if the cue isn't there or cues are off.
        public static bool PlayCue(string name)
        {
            if (cuesEnabled == null || !cuesEnabled.Value || !extras.TryGetValue(name, out var clip)) return false;
            var player = new GameObject("Cue " + name).AddComponent<SpawnMusic>();
            player.isDeath = true;
            player.gate = cuesEnabled;
            player.Begin(clip, name);
            return true;
        }

        void Begin(Clip clip, string what, float extra = 1f, Func<float> gainOverride = null)
        {
            if (source != null) return;
            try
            {
                float gain = gainOverride != null ? gainOverride()
                    : Mathf.Clamp(volume.Value, 0f, 2f) * extra * GameSettings.MusicVolume();
                source = new PcmOutput(clip.pcm, clip.rate, gain);
                CommonLog.Info($"{what} sting started ({clip.pcm.Length / (2f * clip.rate):F2}s, gain {gain:F2})");
            }
            catch (Exception e) { CommonLog.Warn($"{what} sting failed: " + e.Message); }
        }

        /// Stops the stings playing; `deathToo` false leaves death stings to finish.
        public static void StopAll(bool deathToo = true)
        {
            foreach (var player in FindObjectsOfType<SpawnMusic>())
            {
                if (player.source == null || (player.isDeath && !deathToo)) continue;
                player.source.Dispose();
                player.source = null;
            }
        }

        void OnDestroy() { source?.Dispose(); source = null; }

        void Update()
        {
            if (source == null)
            {
                if (isDeath) Destroy(gameObject);
                return;
            }
            if (!(gate != null ? gate.Value : isDeath ? deathEnabled.Value : musicEnabled.Value))
            {
                source.Dispose();
                source = null;
                return;
            }
            bool pause = Time.timeScale == 0f && !(isDeath && RunAudio.Ended);
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
