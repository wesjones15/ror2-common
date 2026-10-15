# Native audio output (modern/2026)

Mario and Olimar effects use native CPAL streams; stings use WinMM through
`PcmOutput`. These streams bypass RoR2's Wwise mixer. `GameSettings` supplies their
game volume and one shared output calibration from
`BepInEx/config/Ror2Common.Audio.cfg`, `[Output] GainDb`.

The default is -4 dB (0.6309573 amplitude). This is a deliberate mix reference
with headroom, not a measurement of Wwise's gain or an exact reconstruction of
the old install. 0 dB restores unattenuated output. Restart after editing. The
existing character volume settings still multiply this level. Game audio is
unaffected. SFX applies master and SFX once; DLC4 music already includes master.
Unavailable/invalid game settings mute instead of falling back to full volume.

The October 15 low-pass filters and FLUDD-only attenuation were removed from both
host and native layers. Old `Sound.HighCutHz` and `Sound.FluddVolume` settings are
no longer consumed. There is no new filtering, pitch change, compressor or
limiter. The earlier Mario gain-before-clipping and JAI parameter fixes remain.

Gain must precede saturation. Mario already applies host gain before native PCM
clipping. Olimar now uses `SoundSystem.render_with_gain` before its output clamp;
the CPAL callback must not multiply the gain again. Standalone tools retain the
existing unity-gain `render` method. Stings apply the shared gain before WinMM's
PCM16 conversion (this cannot repair clipping already baked into a source WAV).

Evidence and limits:

- The inspected old config had approximately 45% master / 48% SFX; the DLC4
  config had 100% / 100%. This explains a 4.65x difference in the old host gain,
  but does not establish why the user hears similar vanilla volume.
- Olimar's regression test boosts the native mix by 8x and attenuates by 1/8.
  It reproduces the reference waveform with zero error at 32 and 48 kHz; applying
  attenuation after clipping fails this test.
- Mario's unfiltered full spray/hover simulation at the earlier -12 dB test reference
  has no clipped samples (peaks approximately 0.142 / 0.163 at 48 kHz).
- `tests/AudioGainCheck` verifies shared SFX/music calibration, dB conversion,
  mute and missing-setting behavior. `tests/RunAudioCheck` covers stream lifetime.

An in-game A/B listening check is still required for perceived balance and the
reported whine. These tests establish gain order and headroom, not a complete
diagnosis of the 2023-versus-DLC4 perceptual difference.

Olimar's full vanilla sundown bell is rendered from the user's disc into memory
when the sound view loads. Its death sting uses the shared SFX gain and survives
native presentation teardown at game over. It stops when the run is left; no bell
audio file is shipped with the mod. Install the matching ABI 28 DLL pair.
