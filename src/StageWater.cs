using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// Stage water for hosts whose character is not a RoR2 body. The same heuristic
    /// MarioTest.UnityHost.Water uses: colliders (triggers included) on Unity's Water layer, or
    /// whose object, material or footstep surface is named for water.
    public static class StageWater
    {
        const int UnityWaterLayer = 4;
        static readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly Collider[] overlaps = new Collider[16];
        static readonly Dictionary<int, bool> cache = new Dictionary<int, bool>();

        /// Whether a body whose feet are at `feet` and whose height is `height` is standing in
        /// water or on a water surface.
        public static bool Contains(Vector3 feet, float height)
        {
            float best = float.NegativeInfinity;
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * height, Vector3.down, hits,
                height + 0.05f, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.point.y > best && IsWater(h.collider)) best = h.point.y;
            }
            if (best > float.NegativeInfinity) return true;
            n = Physics.OverlapSphereNonAlloc(feet + Vector3.up * (0.1f * height),
                0.05f * height, overlaps, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = overlaps[i];
                if (c && c.bounds.max.y > best && IsWater(c)) best = c.bounds.max.y;
            }
            return best > float.NegativeInfinity;
        }

        static bool IsWater(Collider c)
        {
            if (!c) return false;
            int id = c.GetInstanceID();
            if (cache.TryGetValue(id, out bool w)) return w;
            w = c.gameObject.layer == UnityWaterLayer
                || Watery(c.name) || Watery(c.transform.parent ? c.transform.parent.name : null)
                || (c.sharedMaterial && Watery(c.sharedMaterial.name));
            if (!w)
            {
                var r = c.GetComponent<Renderer>();
                w = r && r.sharedMaterial && Watery(r.sharedMaterial.name);
            }
            if (!w)
            {
                var s = c.GetComponent<SurfaceDefProvider>();
                w = s && s.surfaceDef && Watery(s.surfaceDef.name);
            }
            if (cache.Count > 4096) cache.Clear();
            cache[id] = w;
            return w;
        }

        static bool Watery(string name) =>
            name != null && name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
