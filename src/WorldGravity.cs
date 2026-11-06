using System;
using System.Reflection;
using UnityEngine;

namespace Ror2Common
{
    /// The stage's gravity as a multiple of RoR2's usual, for the native sims (Mario, Olimar and the Melee
    /// fighters run their own gravity, not the motor's). The moon (Commencement) lowers
    /// `Physics.gravity` through RoR2's `SetGravity` component: a sim fed this scale jumps higher and falls
    /// slower there, and is untouched (1) everywhere else.
    public static class WorldGravity
    {
        const float FallbackBase = 30f;
        static float baseline = FallbackBase;
        static bool looked;

        /// `Physics.gravity.y` over the usual, clamped to 0.05..4; 1 when anything is off.
        public static float Scale
        {
            get
            {
                float g = Mathf.Abs(Physics.gravity.y);
                float b = Baseline();
                if (b < 0.01f || g < 0.01f || float.IsNaN(g)) return 1f;
                float s = Mathf.Clamp(g / b, 0.05f, 4f);
                // Within a few percent is the usual: no churn from float noise.
                return Mathf.Abs(s - 1f) < 0.02f ? 1f : s;
            }
        }

        /// RoR2's usual gravity: `Run.baseGravity` when the build has it (read once a run exists), else 30.
        static float Baseline()
        {
            if (looked) return baseline;
            try
            {
                var f = typeof(RoR2.Run).GetField("baseGravity", BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static | BindingFlags.Instance);
                if (f == null || f.FieldType != typeof(float)) { looked = true; return baseline; }
                object owner = null;
                if (!f.IsStatic)
                {
                    var run = RoR2.Run.instance;
                    if (!run) return baseline; // no run yet: the fallback, and ask again
                    owner = run;
                }
                float v = Mathf.Abs((float)f.GetValue(owner));
                if (v > 1f && v < 200f) baseline = v;
                looked = true;
            }
            catch (Exception) { looked = true; }
            return baseline;
        }
    }
}
