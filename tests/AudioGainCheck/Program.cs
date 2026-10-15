using System;
using Ror2Common;

static class Program
{
    static void Near(float actual, float expected, string message)
    {
        if (Math.Abs(actual - expected) > 0.00001f) throw new Exception(message);
    }

    static void Main()
    {
        Near(AudioGain.FromDecibels(-12), 0.25118864f, "dB must convert to amplitude, not power");
        Near(AudioGain.DefaultOutputDb, -4f, "default shared gain");
        Near(AudioGain.FromDecibels(AudioGain.DefaultOutputDb), 0.63095734f, "default amplitude");
        Near(AudioGain.Sfx(0.5f, 0.5f, 0), 0.25f, "master and SFX exactly once");
        Near(AudioGain.Music(0.25f, 0), 0.25f, "effective music must not apply master twice");
        Near(AudioGain.Sfx(0, 1, -12), 0, "master mute");
        Near(AudioGain.Sfx(1, 0, -12), 0, "SFX mute");
        Near(AudioGain.Music(0, -12), 0, "music mute");
        foreach (var missing in new[] { null, "", "NaN", "Infinity", "invalid" })
            Near(AudioGain.Percent(missing), 0, "invalid settings must not create a full-volume burst");
        Near(AudioGain.Percent("48.15751"), 0.4815751f, "invariant percentage");
        Near(AudioGain.Percent("-1"), 0, "negative input");
        Near(AudioGain.Percent("200"), 1, "percentage maximum");
        Near(AudioGain.FromDecibels(float.NaN), AudioGain.FromDecibels(-4), "invalid output config");
        // SFX and stings get the same calibration at equal effective game volume.
        Near(AudioGain.Sfx(0.5f, 0.5f, -12), AudioGain.Music(0.25f, -12), "one shared mix reference");
        Console.WriteLine("PASS: shared SFX/music calibration, dB conversion, mute and startup fallback");
    }
}
