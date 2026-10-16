using UnityEngine;

namespace Ror2Common
{
    // A stage's colliders can load after a body spawns (a client's first moments on a new stage). A
    // character that runs its own physics would take the missing floor for a void and fall for ever,
    // with no safe place yet to be put back to. The gate says when there is ground under the body, or
    // that a while has passed (the stage has no floor there, or ours can't see it) and the character
    // should carry on as it always did. Until then the caller holds the body where it is.
    public sealed class GroundGate
    {
        /// How far below the feet to look, metres.
        const float Reach = 2000f;

        readonly float limit;
        bool arrived, announced;
        float waited;

        public GroundGate(float seconds = 10f) => limit = seconds;

        /// Ground has been found (or the wait ran out): the gate stays open from then on.
        public bool Arrived => arrived;

        /// Call each physics step with the body's feet. True once the character may start.
        public bool Ready(Vector3 feet, float dt, string who)
        {
            if (arrived) return true;
            waited += dt;
            if (WorldProbe.Ground(feet, Reach, out _))
            {
                arrived = true;
                if (announced) CommonLog.Info($"The stage's ground arrived after {waited:F1} s; {who} starts at {feet}");
                return true;
            }
            if (waited >= limit)
            {
                arrived = true;
                CommonLog.Warn($"No ground under {who} at {feet} after {limit:0} s; starting anyway");
                return true;
            }
            if (!announced)
            {
                announced = true;
                CommonLog.Info($"No ground under {who} at {feet} yet; waiting up to {limit:0} s for the stage");
            }
            return false;
        }
    }
}
