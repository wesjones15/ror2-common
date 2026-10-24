using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Swept-sphere queries for hand-flown projectiles (Link's arrow, Yoshi's egg): the nearest
    /// thing the sphere meets that is not its owner or an ally.
    public static class MeleeSweep
    {
        static readonly RaycastHit[] hits = new RaycastHit[16];
        /// The centreline radius terrain tests use for a large shot or a drawn lash:
        /// its own thickness must not stop it on a floor it merely passes over.
        public const float TerrainRadius = 0.1f;

        /// The nearest terrain along a narrow centreline probe. `ignoreInitialOverlap`
        /// skips Unity's zero-distance sentinel for a cast that starts touching a floor;
        /// a lash that starts inside geometry wants the stop instead.
        public static bool Terrain(CharacterBody owner, Vector3 from, Vector3 direction,
            float distance, out RaycastHit hit, bool ignoreInitialOverlap = false)
            => FirstFiltered(owner, from, TerrainRadius, direction, distance, WorldProbe.Mask,
                false, ignoreInitialOverlap, out hit);

        /// Large energy shots keep their generous enemy radius, but terrain
        /// tests use a narrow centerline probe so nearby floors do not eat them.
        /// Pick the nearer contact across both casts; enemy assist cannot win
        /// ahead of a closer wall simply because it was queried first.
        public static bool Ranged(CharacterBody owner, Vector3 from, float enemyRadius,
            Vector3 direction, float distance, out RaycastHit best)
        {
            bool world = Terrain(owner, from, direction, distance, out var wall, true);
            bool enemy = FirstFiltered(owner, from, enemyRadius, direction,
                distance, LayerIndex.entityPrecise.mask, true, true, out var target);
            best = enemy && (!world || target.distance < wall.distance) ? target : wall;
            return world || enemy;
        }

        public static bool First(CharacterBody owner, Vector3 from, float radius, Vector3 direction, float distance, int mask, out RaycastHit best)
            => FirstFiltered(owner, from, radius, direction, distance, mask, false, false, out best);

        static bool FirstFiltered(CharacterBody owner, Vector3 from, float radius, Vector3 direction,
            float distance, int mask, bool enemiesOnly, bool ignoreInitialOverlap, out RaycastHit best)
        {
            best = default;
            int n = Physics.SphereCastNonAlloc(from, radius, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var hit = hits[i];
                if (!hit.collider) continue;
                if (ignoreInitialOverlap && hit.distance <= 0f && hit.normal == Vector3.zero) continue;
                var hurt = hit.collider.GetComponent<HurtBox>();
                if (hurt && hurt.healthComponent) {
                    var body = hurt.healthComponent.body;
                    if (!body || body == owner || !body.teamComponent || !owner.teamComponent || !hurt.healthComponent.alive || body.teamComponent.teamIndex == owner.teamComponent.teamIndex) continue;
                } else if (enemiesOnly || hit.collider.GetComponentInParent<CharacterBody>()) continue;
                if (hit.distance < nearest) { nearest = hit.distance; best = hit; found = true; }
            }
            return found;
        }
    }
}
