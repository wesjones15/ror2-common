using UnityEngine;

namespace Ror2Common
{
    // Lead a shot at a moving body: where it will be when water/throw reaches it, not where it is.
    // The host passes a speed in metres per second and a share (0: aim at the body, 1: full lead);
    // `maxLead` keeps a fast strafe from making the stream spray at empty air. The result is a
    // point, not a steering force: the shot still flies straight/ballistic after this.
    public static class Ballistics
    {
        /// <summary>Predicted aim point. A non-positive `speed` or a non-finite input returns `target`.</summary>
        public static Vector3 Intercept(Vector3 origin, Vector3 target, Vector3 velocity, float speed, float maxLead, float share)
        {
            if (!(speed > 0f) || float.IsInfinity(speed) || float.IsNaN(speed)) return target;
            if (float.IsNaN(velocity.x) || float.IsNaN(velocity.y) || float.IsNaN(velocity.z)
                || float.IsInfinity(velocity.x) || float.IsInfinity(velocity.y) || float.IsInfinity(velocity.z))
            {
                velocity = default;
            }

            // Three passes are plenty: each pass measures the flight time to the previous aim
            // point, so a body moving toward or away from the muzzle converges instead of
            // over-leading on the first guess.
            var aim = target;
            for (int i = 0; i < 3; i++)
            {
                var time = (aim - origin).magnitude / speed;
                aim = target + velocity * time;
            }

            var lead = (aim - target) * Mathf.Clamp01(share);
            if (maxLead > 0f && lead.sqrMagnitude > maxLead * maxLead)
            {
                lead = lead.normalized * maxLead;
            }
            return target + lead;
        }
    }
}
