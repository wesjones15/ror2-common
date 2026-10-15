using System;

namespace Ror2Common
{
    /// Gain for native PCM, which bypasses RoR2's Wwise mixer. No EQ or compression.
    public static class AudioGain
    {
        // A common mix reference, not a claim about Wwise's internal volume curve.
        // Leaves headroom when the native voices overlap at 100% game volume.
        public const float DefaultOutputDb = -4f;

        public static float FromDecibels(float db)
        {
            if (float.IsNaN(db) || float.IsInfinity(db)) db = DefaultOutputDb;
            return (float)Math.Pow(10.0, Math.Max(-60f, Math.Min(0f, db)) / 20.0);
        }

        public static float Percent(string value)
        {
            if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v)
                || float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return Math.Max(0f, Math.Min(1f, v / 100f));
        }

        public static float Sfx(float master, float effects, float db) => master * effects * FromDecibels(db);
        // DLC4's volume_music already includes the master setting.
        public static float Music(float effectiveMusic, float db) => effectiveMusic * FromDecibels(db);
    }
}
