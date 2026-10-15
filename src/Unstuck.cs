using UnityEngine;

namespace Ror2Common
{
    // Host recovery for a body a native sim moves through RoR2's geometry, when it is stuck
    // (the sim says it is fallen, prone or blocked) and has not moved for three seconds: search
    // for clear standing ground around it, then fall back to the last verified safe spot. Not a
    // game movement rule. The plugin says when the body is `fallen` (its sim's states) and
    // supplies the ground probe. Measure actual displacement, since a blocked sim can still
    // report a large falling/sliding velocity.
    public sealed class Unstuck
    {
        public delegate bool GroundProbe(Vector3 sample, float rise, float drop, out Vector3 feet);
        const float Delay = 3f;
        Vector3 anchor, safeFeet;
        bool watching, hasSafeFeet;
        float elapsed, safeAge;

        // External teleports, vehicles, death and loss of authority invalidate old ground.
        public void Reset()
        {
            watching = hasSafeFeet = false;
            elapsed = safeAge = 0f;
        }

        /// Metres below its last standing spot a body over a void is rescued at (about a second's
        /// fall), and the depth at which it is rescued anyway, even with a floor far below (150 m
        /// is past any drop RoR2's stages ask of a player).
        public const float VoidDrop = 20f, AnyDrop = 150f;

        /// Fall-off-the-map recovery, like RoR2's kill zones give its own characters, for a body a
        /// native sim moves (their motor is off, and a zone never sees it). Call every tick after
        /// `Step`, which keeps the last standing spot. True when `feet` has dropped `voidDrop`
        /// below that spot with nothing under it (or `anyDrop`, whatever is under), and
        /// `destination` is where to put the body back: the spot it last stood on, as clear as it
        /// is now, with no damage dealt. The caller teleports it and calls `Reset`.
        public bool Fell(Vector3 feet, float height, GroundProbe probe, out Vector3 destination,
            float voidDrop = VoidDrop, float anyDrop = AnyDrop)
        {
            destination = default;
            if (!hasSafeFeet) return false;
            float below = safeFeet.y - feet.y;
            if (below < voidDrop) return false;
            if (below < anyDrop && WorldProbe.Ground(feet, 2000f, out _)) return false;
            destination = probe(safeFeet, height * 0.25f, height * 0.5f, out var clear) ? clear : safeFeet;
            return true;
        }

        public bool Step(Vector3 feet, bool fallen, bool grounded, bool held, float dt,
            float height, GroundProbe probe, out Vector3 destination)
        {
            destination = default;
            float tolerance = Mathf.Max(0.05f, height * 0.08f);
            safeAge += dt;
            if (!fallen && grounded && !held && safeAge >= 0.25f)
            {
                safeAge = 0f;
                if (probe(feet, height * 0.25f, height * 0.5f, out var safe)
                    && (safe - feet).sqrMagnitude <= tolerance * tolerance)
                {
                    safeFeet = safe;
                    hasSafeFeet = true;
                }
            }
            if (!fallen || held)
            {
                watching = false;
                elapsed = 0f;
                return false;
            }
            if (!watching || (feet - anchor).sqrMagnitude > tolerance * tolerance)
            {
                anchor = feet;
                elapsed = 0f;
                watching = true;
            }
            elapsed += dt;
            // Float accumulation must not delay a 180-step / 60 Hz timeout by a tick.
            if (elapsed + 0.0001f < Delay) return false;
            elapsed = 0f; // Retry at most once every three seconds if no clear floor exists.

            // Start above the obstruction, then widen the search around it. The probe checks
            // floor support and standing clearance, including the fallen pod door.
            for (int ring = 0; ring <= 3; ring++)
            {
                int count = ring == 0 ? 1 : 12;
                for (int i = 0; i < count; i++)
                {
                    float angle = i * (2f * Mathf.PI / count);
                    var sample = feet + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (ring * height);
                    if (probe(sample, height * 2f, height * 3f, out destination)
                        && (destination - feet).sqrMagnitude > tolerance * tolerance)
                        return true;
                }
            }
            // A previously clear spot can become obstructed by a moving prop: recheck it.
            return hasSafeFeet && probe(safeFeet, height * 0.25f, height * 0.5f, out destination)
                && (destination - feet).sqrMagnitude > tolerance * tolerance;
        }
    }
}
