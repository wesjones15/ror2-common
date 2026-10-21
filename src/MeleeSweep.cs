using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Swept-sphere queries for hand-flown projectiles (Link's arrow, Yoshi's egg): the nearest
    /// thing the sphere meets that is not its owner or an ally.
    public static class MeleeSweep
    {
        static readonly RaycastHit[] hits = new RaycastHit[16];

        public static bool First(CharacterBody owner, Vector3 from, float radius, Vector3 direction, float distance, int mask, out RaycastHit best)
        {
            best = default;
            int n = Physics.SphereCastNonAlloc(from, radius, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var hit = hits[i];
                if (!hit.collider) continue;
                var hurt = hit.collider.GetComponent<HurtBox>();
                if (hurt && hurt.healthComponent) {
                    var body = hurt.healthComponent.body;
                    if (!body || body == owner || !body.teamComponent || !owner.teamComponent || body.teamComponent.teamIndex == owner.teamComponent.teamIndex) continue;
                } else if (hit.collider.GetComponentInParent<CharacterBody>()) continue;
                if (hit.distance < nearest) { nearest = hit.distance; best = hit; found = true; }
            }
            return found;
        }
    }
}
